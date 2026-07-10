using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Void2610.LiminalPalette.Ipc.TestRunning;

namespace Void2610.LiminalPalette.Editor.TestRunning
{
    /// <summary>
    /// <see cref="ITestRunnerService"/> を <c>TestRunnerApi</c> で実装し、HTTP エンドポイント
    /// (POST /api/v1/tests/run, GET /api/v1/tests/result) の裏側を担う編集時専用サービス。
    ///
    /// enterPlayModeOptions を一切書き換えずに Unity Test Runner を起動するため、外部 MCP ブリッジ
    /// (uLoopMCP 等) の run-tests が ProjectSettings/EditorSettings.asset を汚す churn を避けられる。
    ///
    /// <c>UnityEditor.TestTools.TestRunner.Api</c> 依存を隔離する独立サブ asmdef
    /// (<c>Void2610.LiminalPalette.Editor.TestRunner</c>) に置き、<c>com.unity.test-framework</c>
    /// 導入時のみコンパイルされる (LitMotion 統合と同じ optional 方式)。
    ///
    /// 状態は <see cref="SessionState"/> に保存するため、PlayMode テストの DomainReload を跨いでも
    /// GET /api/v1/tests/result で読める。
    /// </summary>
    public sealed class UnityTestRunnerService : ITestRunnerService
    {
        internal const string RunningKey = "LiminalPalette.TestRunner.Running";
        internal const string ModeKey = "LiminalPalette.TestRunner.Mode";
        internal const string ResultKey = "LiminalPalette.TestRunner.Result";
        internal const string PassedKey = "LiminalPalette.TestRunner.Passed";
        internal const string FailedKey = "LiminalPalette.TestRunner.Failed";
        internal const string SkippedKey = "LiminalPalette.TestRunner.Skipped";
        internal const string InconclusiveKey = "LiminalPalette.TestRunner.Inconclusive";
        internal const string DurationKey = "LiminalPalette.TestRunner.Duration";

        private readonly TestRunnerApi _api;

        public UnityTestRunnerService(TestRunnerApi api)
        {
            _api = api;
        }

        public bool TryStartRun(string mode, string filter, out string error)
        {
            error = null;
            if (SessionState.GetBool(RunningKey, false))
            {
                error = "a test run is already in progress; poll GET /api/v1/tests/result";
                return false;
            }

            var testMode = mode == "editmode" ? TestMode.EditMode : TestMode.PlayMode;
            var displayMode = testMode == TestMode.EditMode ? "EditMode" : "PlayMode";

            var testFilter = new Filter { testMode = testMode };
            // filter はテスト full name の正規表現 (空で全件)。groupNames が full name への regex マッチ。
            if (!string.IsNullOrEmpty(filter)) testFilter.groupNames = new[] { filter };

            // Execute 前に走行状態を確定させる (polling が即 running を観測できるように)。
            SessionState.SetBool(RunningKey, true);
            SessionState.SetString(ModeKey, displayMode);
            SessionState.EraseString(ResultKey);

            try
            {
                _api.Execute(new ExecutionSettings(testFilter));
            }
            catch (System.Exception ex)
            {
                // Execute が同期例外を投げたら走行状態を巻き戻して失敗を返す。
                SessionState.SetBool(RunningKey, false);
                error = $"failed to start test run: {ex.Message}";
                return false;
            }

            return true;
        }

        public TestRunStatus GetStatus()
        {
            var mode = SessionState.GetString(ModeKey, "");
            if (SessionState.GetBool(RunningKey, false))
                return TestRunStatus.Running(mode);

            var result = SessionState.GetString(ResultKey, "");
            if (string.IsNullOrEmpty(result))
                return TestRunStatus.Idle;

            return new TestRunStatus(
                TestRunPhase.Completed,
                result,
                SessionState.GetInt(PassedKey, 0),
                SessionState.GetInt(FailedKey, 0),
                SessionState.GetInt(SkippedKey, 0),
                SessionState.GetInt(InconclusiveKey, 0),
                SessionState.GetFloat(DurationKey, 0f),
                mode);
        }

        /// <summary>
        /// RunFinished を捕捉して結果を SessionState に書き出す。callback は毎 DomainReload で
        /// 登録し直されるため (下記 bootstrap 参照)、PlayMode の再ロードを跨いだ完了も拾える。
        /// </summary>
        internal sealed class ResultCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                SessionState.SetString(ResultKey, result.TestStatus.ToString());
                SessionState.SetInt(PassedKey, result.PassCount);
                SessionState.SetInt(FailedKey, result.FailCount);
                SessionState.SetInt(SkippedKey, result.SkipCount);
                SessionState.SetInt(InconclusiveKey, result.InconclusiveCount);
                SessionState.SetFloat(DurationKey, (float)result.Duration);
                SessionState.SetBool(RunningKey, false);
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result) { }
        }
    }

    /// <summary>
    /// 編集時起動のたびに Test Runner サービスを組み立て、<see cref="TestRunnerBridge.Current"/> へ登録する。
    ///
    /// callback は毎 DomainReload で登録し直す必要がある (静的状態はリセットされる) ため、
    /// <c>[InitializeOnLoad]</c> でここに集約する。TestRunnerApi (ScriptableObject) は静的フィールドで
    /// 保持し GC を防ぐ。
    /// </summary>
    [InitializeOnLoad]
    internal static class TestRunnerServiceBootstrap
    {
        // GC 防止のため実行中は保持する (ScriptableObject を静的に握る)。
        private static readonly TestRunnerApi _api;

        static TestRunnerServiceBootstrap()
        {
            _api = ScriptableObject.CreateInstance<TestRunnerApi>();
            _api.RegisterCallbacks(new UnityTestRunnerService.ResultCallbacks());
            TestRunnerBridge.Current = new UnityTestRunnerService(_api);
        }
    }
}
