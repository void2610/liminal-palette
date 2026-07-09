using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Void2610.LiminalPalette.Editor
{
    /// <summary>enterPlayModeOptions を書き換えずに Unity Test Runner を起動する Editor 専用 [LiminalCommand] 群。</summary>
    public static class TestRunnerCommands
    {
        private const string ResultKey = "LiminalPalette.TestRunner.Result";
        private const string RunningKey = "LiminalPalette.TestRunner.Running";

        // TestRunnerApi (ScriptableObject) の GC を防ぐため実行中は保持する
        private static TestRunnerApi _api;

        [LiminalCommand("Editor/Test/RunPlayMode",
            Description = "PlayMode テストを TestRunnerApi で実行 (即リターン、結果は Editor/Test/Result で polling)。filter 空で全件、テスト full name の正規表現で絞り込み")]
        public static string RunPlayMode(string filter = "") => Run(TestMode.PlayMode, filter);

        [LiminalCommand("Editor/Test/RunEditMode",
            Description = "EditMode テストを TestRunnerApi で実行 (即リターン、結果は Editor/Test/Result で polling)。filter 空で全件、テスト full name の正規表現で絞り込み")]
        public static string RunEditMode(string filter = "") => Run(TestMode.EditMode, filter);

        [LiminalCommand("Editor/Test/Result",
            Description = "直近の Editor/Test/Run* 実行結果を返す: 'running' / 'result=.. passed=.. failed=..'")]
        public static string Result()
        {
            if (SessionState.GetBool(RunningKey, false)) return "running";
            var result = SessionState.GetString(ResultKey, "");
            return string.IsNullOrEmpty(result) ? "no result" : result;
        }

        private static string Run(TestMode mode, string filter)
        {
            if (SessionState.GetBool(RunningKey, false))
                return "running (前回の実行が未完了です。Editor/Test/Result で待機してください)";

            _api = ScriptableObject.CreateInstance<TestRunnerApi>();
            _api.RegisterCallbacks(new ResultCallbacks());

            var testFilter = new Filter { testMode = mode };
            if (!string.IsNullOrEmpty(filter)) testFilter.groupNames = new[] { filter };

            SessionState.SetBool(RunningKey, true);
            SessionState.EraseString(ResultKey);
            _api.Execute(new ExecutionSettings(testFilter));

            return $"{mode} テストを開始しました (filter={(string.IsNullOrEmpty(filter) ? "all" : filter)})。Editor/Test/Result で結果を取得してください";
        }

        // RunFinished の結果を SessionState に残し、DomainReload を跨いでも Editor/Test/Result で読めるようにする
        private sealed class ResultCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                SessionState.SetString(ResultKey,
                    $"result={result.TestStatus} passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount} inconclusive={result.InconclusiveCount} duration={result.Duration:F1}s");
                SessionState.SetBool(RunningKey, false);
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result) { }
        }
    }
}
