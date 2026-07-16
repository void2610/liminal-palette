using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Void2610.LiminalPalette;

Void2610.LiminalPalette.UI
{
    /// <summary>
    /// パレットの状態管理ロジック。UI から独立しているため EditMode テストで挙動を網羅できる。
    /// View はこのクラスの StateChanged を購読して描画を更新する。
    /// </summary>
    public sealed class PaletteController
    {
        // 履歴に存在するコマンドへのスコアブースト。検索結果が完全に乱されないよう小さめに設定。
        public const int HistoryBoost = 30;

        // UI に返す結果の上限。仮想化前提だが安全のため上限を切る。
        public const int MaxResults = 100;

        private readonly ICommandRegistry _registry;
        private readonly ICommandExecutor _executor;
        private readonly ICommandHistory _history;

        // タブ切替や検索とは独立に常に適用される base フィルタ。null なら無効。
        // ランタイム (Play Mode / Player ビルド) では cmd => !cmd.IsEditorOnly を渡し、
        // Editor 専用コマンド (Unity MenuItem 自動収集分など) を一律で表示対象から外す用途。
        private readonly Func<CommandDescriptor, bool> _baseFilter;

        public string Query { get; private set; } = "";
        public IReadOnlyList<RankedCommand> Results { get; private set; } = Array.Empty<RankedCommand>();
        public int SelectedIndex { get; private set; } = 0;
        public CommandResult LastResult { get; private set; }

        /// <summary>
        /// 結果を絞り込むフィルタ。タブによるカテゴリ切替などで使う。null なら全件通す。
        /// 適用順は: Filter で全コマンドを絞る → Query で fuzzy match → 履歴ブースト → ソート。
        /// </summary>
        public Func<CommandDescriptor, bool> Filter { get; private set; }

        /// <summary>UI 上のタブ等、現在のフィルタを示すラベル (任意)。</summary>
        public string FilterLabel { get; private set; } = "All";

        /// <summary>選択中のコマンド。Results が空なら null。</summary>
        public CommandDescriptor SelectedCommand
        {
            get
            {
                if (Results.Count == 0) return null;
                var idx = Mathf.Clamp(SelectedIndex, 0, Results.Count - 1);
                return Results[idx].Descriptor;
            }
        }

        /// <summary>Phase 1 と同じく event Action ベース。R3.Subject に置き換えないこと (Plugins/ で守られている前提)。</summary>
        public event Action StateChanged;

        /// <summary>注入されたレジストリ。タブ生成などで View 側から参照する。</summary>
        public ICommandRegistry Registry => _registry;

        /// <summary>注入された履歴。タブの "history" フィルタで View 側から参照する。</summary>
        public ICommandHistory History => _history;

        public PaletteController(ICommandRegistry registry, ICommandExecutor executor, ICommandHistory history)
            : this(registry, executor, history, baseFilter: null)
        {
        }

        /// <summary>
        /// baseFilter を指定するコンストラクタ。タブ Filter とは別にレジストリ全件に対して常時適用される。
        /// 主な用途は Runtime 側で IsEditorOnly コマンドを除外すること。
        /// </summary>
        public PaletteController(
            ICommandRegistry registry,
            ICommandExecutor executor,
            ICommandHistory history,
            Func<CommandDescriptor, bool> baseFilter)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _baseFilter = baseFilter;

            // 初期表示として全件を計算しておく。
            RecomputeResults();
        }

        /// <summary>検索クエリを更新し、結果を再計算する。</summary>
        public void SetQuery(string query)
        {
            Query = query ?? "";
            SelectedIndex = 0;
            RecomputeResults();
            StateChanged?.Invoke();
        }

        /// <summary>
        /// 表示を絞り込むフィルタを設定する (タブ切替の本体)。
        /// label は UI 表示用の任意文字列。filter が null なら全件通す。
        /// </summary>
        public void SetFilter(string label, Func<CommandDescriptor, bool> filter)
        {
            Filter = filter;
            FilterLabel = label ?? "All";
            SelectedIndex = 0;
            RecomputeResults();
            StateChanged?.Invoke();
        }

        /// <summary>選択を delta だけ動かす (端で止まる、ループしない)。</summary>
        public void MoveSelection(int delta)
        {
            if (Results.Count == 0)
            {
                SelectedIndex = 0;
                StateChanged?.Invoke();
                return;
            }
            var next = Mathf.Clamp(SelectedIndex + delta, 0, Results.Count - 1);
            if (next == SelectedIndex) return;
            SelectedIndex = next;
            StateChanged?.Invoke();
        }

        /// <summary>選択を絶対インデックスで指定する。範囲外はクランプ。</summary>
        public void SetSelection(int index)
        {
            var clamped = Mathf.Clamp(index, 0, Math.Max(0, Results.Count - 1));
            if (clamped == SelectedIndex) return;
            SelectedIndex = clamped;
            StateChanged?.Invoke();
        }

        /// <summary>選択中コマンドを型解決済み引数で実行し、履歴に記録する。</summary>
        public Task<CommandResult> ExecuteSelectedAsync(IReadOnlyDictionary<string, object> typedArgs, CancellationToken ct = default)
        {
            var cmd = SelectedCommand;
            if (cmd == null)
            {
                LastResult = CommandResult.Fail("No command selected", null, Array.Empty<LogEntry>(), TimeSpan.Zero);
                StateChanged?.Invoke();
                return Task.FromResult(LastResult);
            }
            return RunAsync(cmd.Path, typedArgs, ct);
        }

        /// <summary>過去の invocation を同じ引数で再実行する。Log / History タブの再実行経路。</summary>
        public Task<CommandResult> ReplayAsync(CommandInvocation invocation, CancellationToken ct = default)
        {
            if (invocation == null)
            {
                LastResult = CommandResult.Fail("Invocation is null", null, Array.Empty<LogEntry>(), TimeSpan.Zero);
                StateChanged?.Invoke();
                return Task.FromResult(LastResult);
            }
            return RunAsync(invocation.Path, invocation.Args, ct);
        }

        // 実行 + 履歴記録 + 状態通知を 1 つにまとめた本体。新規実行 (ExecuteSelectedAsync) と
        // 再実行 (ReplayAsync) の両方からこれを呼ぶ。
        private async Task<CommandResult> RunAsync(string path, IReadOnlyDictionary<string, object> typedArgs, CancellationToken ct)
        {
            // ConfigureAwait(false) は付けない。Unity の SynchronizationContext (メインスレッド) に
            // 戻った状態で StateChanged を発火させ、PaletteView の UI 更新がメインスレッドで実行されることを保証する。
            var result = await _executor.ExecuteWithTypedArgsAsync(path, typedArgs, ct);
            LastResult = result;

            // パレットの Log / History タブに実行記録を蓄積する (パレット経由実行のみが対象)。
            InvocationStore.Instance.Record(path, typedArgs, result);

            // 成否に関わらず履歴に記録する。エラーでも「最近何を試したか」を残しておく方が UX として有用。
            _history.Record(path);

            // 履歴ブーストが反映された結果を再計算しておく (空クエリ時の並びが変わるため)。
            RecomputeResults();
            StateChanged?.Invoke();
            return result;
        }

        /// <summary>パレット再オープン時に呼ぶ。クエリと選択をリセットし、結果は履歴を反映した最新状態に戻す。</summary>
        public void Reset()
        {
            Query = "";
            SelectedIndex = 0;
            LastResult = null;
            RecomputeResults();
            StateChanged?.Invoke();
        }

        /// <summary>
        /// パレットの再オープン挙動。Editor は毎回フレッシュ (OnEachOpen) を既定とし、
        /// Runtime はゲーム中の利便性を優先してクエリを覚えておく (KeepState) ケースもある。
        /// </summary>
        public enum PaletteResetPolicy
        {
            /// <summary>Show のたびに Reset() を呼んでクエリ / 選択を初期化する。</summary>
            OnEachOpen,
            /// <summary>前回の状態を保持する。閉じる側で必要なら Reset() を呼ぶ。</summary>
            KeepState,
        }

        /// <summary>
        /// policy に応じて Reset() を呼ぶ。Show / Toggle のたびに呼べるよう副作用を一箇所に閉じ込める。
        /// </summary>
        public void ResetIfRequested(PaletteResetPolicy policy)
        {
            if (policy == PaletteResetPolicy.OnEachOpen) Reset();
            // KeepState は何もしない (前回の Query / SelectedIndex を保持)。
        }

        // 2 つのフィルタ (baseFilter とタブ Filter) を AND 結合した述語を返す。null は「素通し」扱い。
        // どちらも null なら null を返し、呼び出し側でフィルタ自体をスキップできるようにする。
        private bool PassesFilters(CommandDescriptor cmd)
        {
            if (_baseFilter != null && !_baseFilter(cmd)) return false;
            if (Filter != null && !Filter(cmd)) return false;
            return true;
        }

        // 検索結果の再計算。クエリ有無で 2 系統のロジックに分岐する。
        // baseFilter (Runtime での IsEditorOnly 除外など) と タブ Filter を AND で適用してから検索を当てる。
        private void RecomputeResults()
        {
            var all = _registry.All;
            // baseFilter とタブ Filter を AND で先に絞る。両方 null なら全件。
            var hasAnyFilter = _baseFilter != null || Filter != null;
            var pool = hasAnyFilter
                ? (IReadOnlyList<CommandDescriptor>)all.Where(PassesFilters).ToList()
                : (IReadOnlyList<CommandDescriptor>)all;
            var list = new List<RankedCommand>(Math.Min(pool.Count, MaxResults));

            if (string.IsNullOrEmpty(Query))
            {
                // クエリなし: 履歴に含まれるコマンドが先頭、そうでないものはアルファベット順 (Path 昇順)。
                var byPath = new List<CommandDescriptor>(pool);
                byPath.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

                // 履歴順で先に並べる (Filter を通ったものだけ)。
                for (var i = 0; i < _history.RecentPaths.Count && list.Count < MaxResults; i++)
                {
                    var path = _history.RecentPaths[i];
                    var cmd = _registry.Find(path);
                    if (cmd == null) continue;
                    if (!PassesFilters(cmd)) continue;
                    list.Add(new RankedCommand(cmd, 0, Array.Empty<int>(), fromHistory: true));
                }
                // 残りをアルファベット順で。履歴に既出のものは除外する。
                for (var i = 0; i < byPath.Count && list.Count < MaxResults; i++)
                {
                    var cmd = byPath[i];
                    if (_history.Contains(cmd.Path)) continue;
                    list.Add(new RankedCommand(cmd, 0, Array.Empty<int>(), fromHistory: false));
                }
            }
            else
            {
                // クエリあり: フィルタ通過分に FuzzyMatcher を当て、マッチしたものだけ採用。履歴ブースト適用。
                for (var i = 0; i < pool.Count; i++)
                {
                    var cmd = pool[i];
                    var match = FuzzyMatcher.Match(Query, cmd.Path, cmd.Aliases);
                    if (!match.Matched) continue;
                    var fromHistory = _history.Contains(cmd.Path);
                    var score = match.Score + (fromHistory ? HistoryBoost : 0);
                    list.Add(new RankedCommand(cmd, score, match.MatchedIndices, fromHistory));
                }

                // スコア降順、同スコアなら Path 昇順。
                list.Sort((a, b) =>
                {
                    var byScore = b.Score.CompareTo(a.Score);
                    if (byScore != 0) return byScore;
                    return string.Compare(a.Descriptor.Path, b.Descriptor.Path, StringComparison.OrdinalIgnoreCase);
                });

                // 上限超過分を切り詰める。
                if (list.Count > MaxResults) list.RemoveRange(MaxResults, list.Count - MaxResults);
            }

            Results = list;
            // 選択が範囲外になった場合の補正。
            if (SelectedIndex >= Results.Count) SelectedIndex = Math.Max(0, Results.Count - 1);
        }
    }
}
