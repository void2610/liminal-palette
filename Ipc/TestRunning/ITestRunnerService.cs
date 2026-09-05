using System.Collections.Generic;

namespace Void2610.LiminalPalette.Ipc.TestRunning
{
    /// <summary>
    /// Unity Test Runner を起動 / 監視する編集時専用サービスの抽象。
    ///
    /// 実装は <c>UnityEditor.TestTools.TestRunner.Api</c> に依存するため、
    /// <c>com.unity.test-framework</c> 導入時のみコンパイルされる独立サブ asmdef
    /// (<c>Void2610.LiminalPalette.Editor.TestRunner</c>) に置き、起動時に
    /// <see cref="TestRunnerBridge.Current"/> へ自身を登録する。
    ///
    /// Ipc レイヤ (本 asmdef) は test-framework に依存できない (Runtime でもコンパイルされる) ため、
    /// この境界インターフェイス越しにのみ Test Runner を触る。未登録 (= test-framework 未導入 /
    /// Runtime) の場合、<see cref="RunTestsEndpoint"/> / <see cref="TestResultEndpoint"/> は
    /// 501 を返す。
    ///
    /// すべてのメソッドは Unity メインスレッドから呼ばれる前提 (TestRunnerApi / SessionState が
    /// メインスレッド限定)。エンドポイント側で <c>MainThreadDispatcher</c> を通す。
    /// </summary>
    public interface ITestRunnerService
    {
        /// <summary>
        /// テスト実行を開始する (即リターン、実行は非同期)。
        /// <paramref name="mode"/> は "playmode" / "editmode" (検証済みの正規化文字列)。
        /// <paramref name="filter"/> はテスト full name の正規表現 (空 / null で全件)。
        /// 前回の実行が未完了なら開始せず false を返し <paramref name="error"/> を埋める。
        /// </summary>
        bool TryStartRun(string mode, string filter, out string error);

        /// <summary>直近の実行状態を返す。</summary>
        TestRunStatus GetStatus();
    }

    /// <summary>失敗したテスト 1 件の要約 (full name + 失敗メッセージ)。</summary>
    public readonly struct TestFailureInfo
    {
        public readonly string Name;
        public readonly string Message;

        public TestFailureInfo(string name, string message)
        {
            Name = name ?? "";
            Message = message ?? "";
        }
    }

    /// <summary>テスト実行のフェーズ。</summary>
    public enum TestRunPhase
    {
        /// <summary>一度も実行していない (結果なし)。</summary>
        Idle,

        /// <summary>実行中。</summary>
        Running,

        /// <summary>完了 (結果あり)。</summary>
        Completed,
    }

    /// <summary>
    /// テスト実行結果のスナップショット (不変)。DomainReload を跨いでも読めるよう、
    /// 実装は SessionState 等の永続ストアから組み立てる。
    /// </summary>
    public readonly struct TestRunStatus
    {
        public readonly TestRunPhase Phase;

        /// <summary>Completed 時の総合結果 ("Passed" / "Failed" / "Inconclusive" / "Skipped")。それ以外は空。</summary>
        public readonly string Result;

        public readonly int Passed;
        public readonly int Failed;
        public readonly int Skipped;
        public readonly int Inconclusive;

        /// <summary>実行にかかった秒数 (Completed 時のみ有効)。</summary>
        public readonly double DurationSeconds;

        /// <summary>直近に実行した mode ("PlayMode" / "EditMode")。未実行なら空。</summary>
        public readonly string Mode;

        /// <summary>失敗したテストの要約 (Completed かつ Failed 時のみ非空。件数上限あり)。</summary>
        public readonly IReadOnlyList<TestFailureInfo> Failures;

        public TestRunStatus(TestRunPhase phase, string result, int passed, int failed,
            int skipped, int inconclusive, double durationSeconds, string mode)
            : this(phase, result, passed, failed, skipped, inconclusive, durationSeconds, mode, null)
        {
        }

        public TestRunStatus(TestRunPhase phase, string result, int passed, int failed,
            int skipped, int inconclusive, double durationSeconds, string mode,
            IReadOnlyList<TestFailureInfo> failures)
        {
            Phase = phase;
            Result = result ?? "";
            Passed = passed;
            Failed = failed;
            Skipped = skipped;
            Inconclusive = inconclusive;
            DurationSeconds = durationSeconds;
            Mode = mode ?? "";
            Failures = failures ?? System.Array.Empty<TestFailureInfo>();
        }

        public static TestRunStatus Idle => new TestRunStatus(TestRunPhase.Idle, "", 0, 0, 0, 0, 0, "");

        public static TestRunStatus Running(string mode)
            => new TestRunStatus(TestRunPhase.Running, "", 0, 0, 0, 0, 0, mode);
    }
}
