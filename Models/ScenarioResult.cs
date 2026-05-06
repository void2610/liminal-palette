using System;
using System.Collections.Generic;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// シナリオ実行結果。各ステップの StepResult を集約する。
    /// fail-fast 設計のため、最初の失敗ステップ以降は実行されず、Steps もそこまでで打ち切られる。
    /// </summary>
    public sealed class ScenarioResult
    {
        /// <summary>全ステップが Pass なら true。</summary>
        public bool Success { get; }

        /// <summary>各ステップの結果 (実行された分のみ)。</summary>
        public IReadOnlyList<StepResult> Steps { get; }

        /// <summary>シナリオ全体の所要時間。</summary>
        public TimeSpan Duration { get; }

        /// <summary>最初に失敗したステップの index。失敗が無ければ -1。</summary>
        public int FailedAtStep { get; }

        /// <summary>名前付き実行のときの Path。ad-hoc 実行のときは null。</summary>
        public string Path { get; }

        /// <summary>シナリオが既に他で実行中だったために拒否されたケース。利用者が判別するためのフラグ。</summary>
        public bool WasRejectedAsAlreadyRunning { get; }

        public ScenarioResult(
            bool success,
            IReadOnlyList<StepResult> steps,
            TimeSpan duration,
            int failedAtStep,
            string path,
            bool wasRejectedAsAlreadyRunning = false)
        {
            Success = success;
            Steps = steps ?? Array.Empty<StepResult>();
            Duration = duration;
            FailedAtStep = failedAtStep;
            Path = path;
            WasRejectedAsAlreadyRunning = wasRejectedAsAlreadyRunning;
        }

        /// <summary>「既に他のシナリオが実行中」状態を表す結果。</summary>
        public static ScenarioResult AlreadyRunning(string path) =>
            new ScenarioResult(
                success: false,
                steps: Array.Empty<StepResult>(),
                duration: TimeSpan.Zero,
                failedAtStep: -1,
                path: path,
                wasRejectedAsAlreadyRunning: true);
    }
}
