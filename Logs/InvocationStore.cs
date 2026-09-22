using System;
using System.Collections.Generic;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// コマンド実行の履歴を保持するシングルトン。
    /// Log タブ (詳細閲覧) と History タブ (再実行) が共通で参照するソース。
    /// </summary>
    public sealed class InvocationStore
    {
        /// <summary>手動実行 (History タブの再実行対象) の保持上限。</summary>
        public const int Capacity = 200;

        /// <summary>自動化由来 (IPC / シナリオ) の保持上限。手動実行とは独立した枠で回す。</summary>
        public const int AutomatedCapacity = 200;

        /// <summary>両枠を合わせた保持件数の上限。ログ取得 API の limit 上限もこれに合わせる。</summary>
        public const int MaxRetained = Capacity + AutomatedCapacity;

        public static InvocationStore Instance { get; } = new InvocationStore();

        private readonly List<CommandInvocation> _entries = new List<CommandInvocation>(MaxRetained);
        private readonly object _lock = new object();

        // 枠ごとの件数。溢れ判定のたびにリストを数え直さないためのカウンタ。
        private int _userCount;
        private int _automatedCount;

        /// <summary>追加 / クリア時に発火。UI 側で itemsSource を更新する。</summary>
        public event Action Changed;

        /// <summary>取得時点のスナップショット (古い順)。新しい順に並べたい場合は呼び出し側で逆走査する。</summary>
        public IReadOnlyList<CommandInvocation> Entries
        {
            get { lock (_lock) return _entries.ToArray(); }
        }

        public int Count
        {
            get { lock (_lock) return _entries.Count; }
        }

        private InvocationStore() { }

        /// <summary>1 回の実行を記録する。args は CommandExecutor に渡された型解決済み辞書を想定。</summary>
        public void Record(string path, IReadOnlyDictionary<string, object> args, CommandResult result)
            => Record(path, args, result, InvocationOrigin.User);

        /// <summary>
        /// 1 回の実行を経路付きで記録する。
        /// 自動化由来 (IPC / シナリオ) は History タブから除外されるうえ、保持枠も手動実行とは別に回す。
        /// 枠を共有すると、E2E やシナリオの大量実行だけでユーザーが手で打った履歴が押し出されて消えるため。
        /// </summary>
        public void Record(string path, IReadOnlyDictionary<string, object> args, CommandResult result, InvocationOrigin origin)
        {
            if (string.IsNullOrEmpty(path) || result == null) return;
            // 引数辞書はディフェンシブにコピーする (呼び出し側が UI 状態として再利用する辞書だと履歴が破壊されるため)。
            var copy = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (args != null)
            {
                foreach (var kv in args) copy[kv.Key] = kv.Value;
            }
            var entry = new CommandInvocation(path, copy, result, DateTime.UtcNow, origin);
            lock (_lock)
            {
                _entries.Add(entry);
                if (entry.IsAutomated) _automatedCount++;
                else _userCount++;
                TrimOverflow(entry.IsAutomated);
            }
            Changed?.Invoke();
        }

        /// <summary>互換用オーバーロード。true は <see cref="InvocationOrigin.Scenario"/> として扱う。</summary>
        public void Record(string path, IReadOnlyDictionary<string, object> args, CommandResult result, bool isFromScenario)
            => Record(path, args, result, isFromScenario ? InvocationOrigin.Scenario : InvocationOrigin.User);

        /// <summary>履歴を消す。</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
                _userCount = 0;
                _automatedCount = 0;
            }
            Changed?.Invoke();
        }

        // 追加した側の枠だけが溢れうるので、その枠の最古から上限まで捨てる。
        // 時系列の並び自体は 1 本のリストで保つため、Entries 側でマージし直す必要がない。
        private void TrimOverflow(bool automated)
        {
            var limit = automated ? AutomatedCapacity : Capacity;
            while ((automated ? _automatedCount : _userCount) > limit)
            {
                for (var i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].IsAutomated != automated) continue;
                    _entries.RemoveAt(i);
                    if (automated) _automatedCount--;
                    else _userCount--;
                    break;
                }
            }
        }
    }
}
