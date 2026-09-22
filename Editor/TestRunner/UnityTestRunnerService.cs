using System.Collections.Generic;
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
        internal const string FailuresKey = "LiminalPalette.TestRunner.Failures";
        internal const string MutedKey = "LiminalPalette.TestRunner.Muted";
        internal const string MutePrevKey = "LiminalPalette.TestRunner.MutePrev";
        internal const string HeartbeatKey = "LiminalPalette.TestRunner.Heartbeat";

        /// <summary>
        /// Running=true のまま、この秒数どのコールバックも来なければ「中断された残骸」と見なす。
        /// テスト 1 件ごとに TestStarted / TestFinished が来るので、実行中なら必ず更新され続ける。
        /// 実測 (PlayMode 53 件で 155 秒) に対して十分な余裕を取り、単体で長いテストを誤検知しない値にする。
        /// </summary>
        internal const double StaleAfterSeconds = 300.0;

        // 失敗一覧の肥大でレスポンスと SessionState が膨れないよう上限を切る (超過分は件数だけ分かれば十分)
        internal const int MaxFailures = 30;
        internal const int MaxFailureMessageLength = 2000;

        // SessionState は文字列の配列/リストを直接持てないため、制御文字 (RS/US) 区切りで失敗一覧を 1 キーに詰める
        private const char FailureSeparator = '\u001e';
        private const char FieldSeparator = '\u001f';

        private readonly TestRunnerApi _api;

        public UnityTestRunnerService(TestRunnerApi api)
        {
            _api = api;
        }

        // 実行が生きている印。開始時と各コールバックで更新する。
        internal static void Beat() =>
            SessionState.SetString(HeartbeatKey, System.DateTime.UtcNow.Ticks.ToString());

        // Running=true なのに一定時間どのコールバックも来ていないか。
        private static bool IsStale()
        {
            if (!SessionState.GetBool(RunningKey, false)) return false;
            var raw = SessionState.GetString(HeartbeatKey, "");
            // 開始時に必ず打つので、印が無い = 旧版が残した状態。復旧対象にする。
            if (!long.TryParse(raw, out var ticks)) return true;
            var elapsed = (System.DateTime.UtcNow - new System.DateTime(ticks, System.DateTimeKind.Utc)).TotalSeconds;
            return elapsed > StaleAfterSeconds;
        }

        /// <summary>
        /// 中断された実行の残骸を倒す。RunFinished が来ないまま終わった場合 (Test Runner ウィンドウでの
        /// キャンセル、Editor のクラッシュ等) に Running=true が残り続け、以降の実行が全部弾かれるため。
        /// 倒した場合 true。
        /// </summary>
        internal static bool RecoverIfStale()
        {
            if (!IsStale()) return false;
            ForceClearRunning();
            Debug.LogWarning(
                "[LiminalPalette] 中断されたテスト実行の状態を検出したため解除しました " +
                $"({StaleAfterSeconds} 秒以上コールバックがありません)。");
            return true;
        }

        // 走行状態とミュートを強制的に巻き戻す。
        private static void ForceClearRunning()
        {
            SessionState.SetBool(RunningKey, false);
            if (SessionState.GetBool(MutedKey, false))
            {
                EditorUtility.audioMasterMute = SessionState.GetBool(MutePrevKey, false);
                SessionState.SetBool(MutedKey, false);
            }
        }

        public bool TryStartRun(string mode, string filter, bool force, out string error)
        {
            error = null;
            // force は利用者が明示的に「残骸を無視して始める」と言った場合の逃げ道。
            if (force) ForceClearRunning();
            else RecoverIfStale();

            if (SessionState.GetBool(RunningKey, false))
            {
                error = "a test run is already in progress; poll GET /api/v1/tests/result "
                    + "(中断された状態が残っている場合は force=true で解除できます)";
                return false;
            }

            var testMode = mode == "editmode" ? TestMode.EditMode : TestMode.PlayMode;
            var displayMode = testMode == TestMode.EditMode ? "EditMode" : "PlayMode";

            var testFilter = new Filter { testMode = testMode };
            // filter はテスト full name の正規表現 (空で全件)。groupNames が full name への regex マッチ。
            if (!string.IsNullOrEmpty(filter)) testFilter.groupNames = new[] { filter };

            // Execute 前に走行状態を確定させる (polling が即 running を観測できるように)。
            SessionState.SetBool(RunningKey, true);
            Beat();
            SessionState.SetString(ModeKey, displayMode);
            SessionState.EraseString(ResultKey);
            SessionState.EraseString(FailuresKey);

            // CLI 駆動のテスト実行はゲーム音を鳴らす意味が無いため、実行中だけエディタの出力段を落とす。
            // 元値は SessionState に退避し、PlayMode の DomainReload を跨いでも RunFinished で復元できるようにする
            SessionState.SetBool(MutePrevKey, EditorUtility.audioMasterMute);
            SessionState.SetBool(MutedKey, true);
            EditorUtility.audioMasterMute = true;

            try
            {
                _api.Execute(new ExecutionSettings(testFilter));
            }
            catch (System.Exception ex)
            {
                // Execute が同期例外を投げたら走行状態とミュートを巻き戻して失敗を返す (RunFinished は来ない)。
                SessionState.SetBool(RunningKey, false);
                EditorUtility.audioMasterMute = SessionState.GetBool(MutePrevKey, false);
                SessionState.SetBool(MutedKey, false);
                error = $"failed to start test run: {ex.Message}";
                return false;
            }

            return true;
        }

        public TestRunStatus GetStatus()
        {
            // 結果の polling でも残骸を倒す。ここで倒さないと `test result` が running を返し続ける。
            RecoverIfStale();
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
                mode,
                ReadFailures());
        }

        private static IReadOnlyList<TestFailureInfo> ReadFailures()
        {
            var raw = SessionState.GetString(FailuresKey, "");
            if (string.IsNullOrEmpty(raw)) return System.Array.Empty<TestFailureInfo>();
            var list = new List<TestFailureInfo>();
            foreach (var entry in raw.Split(FailureSeparator))
            {
                if (entry.Length == 0) continue;
                var sep = entry.IndexOf(FieldSeparator);
                if (sep < 0) list.Add(new TestFailureInfo(entry, ""));
                else list.Add(new TestFailureInfo(entry.Substring(0, sep), entry.Substring(sep + 1)));
            }
            return list;
        }

        private static void AppendFailure(string name, string message)
        {
            var raw = SessionState.GetString(FailuresKey, "");
            // 上限超過はレスポンス肥大を避けて黙って切り捨てる (総数は failed カウントで分かる)
            if (!string.IsNullOrEmpty(raw) && raw.Split(FailureSeparator).Length >= MaxFailures) return;
            message ??= "";
            if (message.Length > MaxFailureMessageLength) message = message.Substring(0, MaxFailureMessageLength) + "…";
            name = (name ?? "").Replace(FailureSeparator, ' ').Replace(FieldSeparator, ' ');
            message = message.Replace(FailureSeparator, ' ').Replace(FieldSeparator, ' ');
            var entry = name + FieldSeparator + message;
            SessionState.SetString(FailuresKey, string.IsNullOrEmpty(raw) ? entry : raw + FailureSeparator + entry);
        }

        /// <summary>
        /// RunFinished を捕捉して結果を SessionState に書き出す。callback は毎 DomainReload で
        /// 登録し直されるため (下記 bootstrap 参照)、PlayMode の再ロードを跨いだ完了も拾える。
        /// </summary>
        internal sealed class ResultCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) => Beat();

            public void RunFinished(ITestResultAdaptor result)
            {
                SessionState.SetString(ResultKey, result.TestStatus.ToString());
                SessionState.SetInt(PassedKey, result.PassCount);
                SessionState.SetInt(FailedKey, result.FailCount);
                SessionState.SetInt(SkippedKey, result.SkipCount);
                SessionState.SetInt(InconclusiveKey, result.InconclusiveCount);
                SessionState.SetFloat(DurationKey, (float)result.Duration);
                SessionState.SetBool(RunningKey, false);

                // 自分でミュートした実行だけ元値へ戻す (Test Runner ウィンドウ等の外部実行では触らない)
                if (SessionState.GetBool(MutedKey, false))
                {
                    EditorUtility.audioMasterMute = SessionState.GetBool(MutePrevKey, false);
                    SessionState.SetBool(MutedKey, false);
                }
            }

            // テスト 1 件ごとに打つ。単体で長いテストでも生存が伝わる。
            public void TestStarted(ITestAdaptor test) => Beat();

            public void TestFinished(ITestResultAdaptor result)
            {
                Beat();
                // suite ノードは子の失敗を重複集計するため leaf (実テスト) だけ拾う
                if (result.Test.HasChildren) return;
                if (result.TestStatus != TestStatus.Failed) return;
                AppendFailure(result.FullName, result.Message);
            }
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
