using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// デバッグコンソールの公開ファサード。
    /// 利用側は `using Void2610.LiminalPalette;` の上で
    /// `LiminalPalette.ExecuteAsync(...)` のみを呼べば足りる。
    /// </summary>
    public static class LiminalPalette
    {
        /// <summary>共有レジストリ。動的登録 / 検索を行いたい場合に使用する。</summary>
        public static ICommandRegistry Registry => CommandRegistry.Default;

        // Default レジストリと組で 1 つだけ Executor を持つ。テストでは new CommandExecutor() を直接生成する。
        private static readonly ICommandExecutor _executor = new CommandExecutor(CommandRegistry.Default);

        // ファサード側で null を空コレクションに正規化してから Executor に渡す。
        // ICommandExecutor の引数も nullable に揃えており、CommandExecutor 側でも同様の正規化を行うが、
        // ファサード呼び出し時に意図を明示しておくことで Phase 2 以降に Executor 実装を差し替えても
        // null 受領時の挙動が変わらないことを保証する。
        private static readonly IReadOnlyDictionary<string, string> _emptyNamedArgs = new Dictionary<string, string>();
        private static readonly IReadOnlyDictionary<string, object> _emptyTypedArgs = new Dictionary<string, object>();

        /// <summary>名前指定でコマンドを実行する。args が null の場合は空辞書として扱う。</summary>
        public static Task<CommandResult> ExecuteAsync(
            string path,
            IReadOnlyDictionary<string, string>? args = null,
            CancellationToken ct = default)
            => _executor.ExecuteAsync(path, args ?? _emptyNamedArgs, ct);

        /// <summary>位置指定でコマンドを実行する。null の場合は空配列として扱う。</summary>
        public static Task<CommandResult> ExecuteAsync(
            string path,
            IReadOnlyList<string>? positionalArgs,
            CancellationToken ct = default)
            => _executor.ExecuteAsync(path, positionalArgs ?? Array.Empty<string>(), ct);

        /// <summary>
        /// 型解決済みの値でコマンドを実行する。Phase 2 の UI から呼ばれる経路。
        /// 文字列変換を介さないため Vector3 / Color などの精度を維持できる。
        /// </summary>
        public static Task<CommandResult> ExecuteWithTypedArgsAsync(
            string path,
            IReadOnlyDictionary<string, object>? args = null,
            CancellationToken ct = default)
            => _executor.ExecuteWithTypedArgsAsync(path, args ?? _emptyTypedArgs, ct);

        /// <summary>
        /// 利用側 ITypeConverter を登録する。後から登録したものが標準コンバータより優先される。
        /// </summary>
        public static void RegisterTypeConverter(ITypeConverter converter) => TypeConverterRegistry.Register(converter);

        // Phase 5a: インスタンスメソッド対応のためのインスタンス解決経路。
        // 既定は NullInstanceResolver で、SetInstanceResolver を呼ぶまでインスタンスメソッドコマンドは
        // CommandExecutor 内で「インスタンス未解決」エラーになる。
        // VContainer 統合では LiminalPaletteEntryPoint (Integration.VContainer asmdef) が SetInstanceResolver を呼ぶ。
        private static IInstanceResolver _instanceResolver = new NullInstanceResolver();

        /// <summary>
        /// 現在登録されている IInstanceResolver。Core 外 (UI / Ipc) からも resolver.Resolve(type) を呼ぶ必要があるため public。
        /// SetInstanceResolver で差し替える。
        /// </summary>
        public static IInstanceResolver InstanceResolver => _instanceResolver;

        /// <summary>
        /// インスタンスメソッドの [ConsoleCommand] 解決に使う IInstanceResolver を差し替える。
        /// VContainer 統合経由では builder.RegisterEntryPoint&lt;LiminalPaletteEntryPoint&gt;() が呼ばれた時点で自動で設定される。
        /// </summary>
        public static void SetInstanceResolver(IInstanceResolver resolver)
            => _instanceResolver = resolver ?? new NullInstanceResolver();

        // ---- Scenarios (Phase 5b) ----

        /// <summary>シナリオレジストリ。</summary>
        public static IScenarioRegistry Scenarios => ScenarioRegistry.Default;

        // ScenarioExecutor は IFrameWaiter に依存するため、デフォルトでは RuntimeFrameWaiter を採用する。
        // Editor (Edit Mode) で動かす場合は Editor 側 Bootstrap が SetScenarioFrameWaiter で差し替える。
        private static IFrameWaiter _frameWaiter = new RuntimeFrameWaiter();
        private static ScenarioExecutor _scenarioExecutor;

        private static ScenarioExecutor GetOrCreateScenarioExecutor()
        {
            // 遅延生成: 初回 RunScenarioAsync 呼び出し時に組み立てる。
            // _frameWaiter が SetScenarioFrameWaiter で差し替えられた後でも反映できるよう、
            // _frameWaiter が同じ参照を返すうちは同一インスタンスを再利用する。
            if (_scenarioExecutor == null)
            {
                _scenarioExecutor = new ScenarioExecutor(_executor, ObservableFieldRegistry.Default, _frameWaiter);
            }
            return _scenarioExecutor;
        }

        /// <summary>
        /// シナリオ実行に使う IFrameWaiter を差し替える。Editor (Edit Mode) では EditorFrameWaiter、
        /// Runtime / Play Mode では RuntimeFrameWaiter (デフォルト) を入れる想定。
        /// </summary>
        public static void SetScenarioFrameWaiter(IFrameWaiter frameWaiter)
        {
            _frameWaiter = frameWaiter ?? new RuntimeFrameWaiter();
            // 既存の executor を破棄して、次回呼び出し時に新しい frameWaiter で作り直す。
            _scenarioExecutor = null;
        }

        /// <summary>登録済みシナリオを Path 指定で実行する。</summary>
        public static Task<ScenarioResult> RunScenarioAsync(string path, CancellationToken ct = default)
            => GetOrCreateScenarioExecutor().ExecuteAsync(ScenarioRegistry.Default, path, ct);

        /// <summary>ad-hoc にステップ列を指定して実行する (HTTP の /scenarios/run ad-hoc 経路と同じ)。</summary>
        public static Task<ScenarioResult> RunScenarioAsync(IReadOnlyList<ScenarioStep> steps, CancellationToken ct = default)
            => GetOrCreateScenarioExecutor().ExecuteAsync(steps, path: null, ct);
    }
}
