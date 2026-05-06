using System;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// シナリオの 1 ステップの実行結果。
    /// 成否、所要時間、種別ごとの詳細 (CommandResult / 取得した実値) を保持する。
    /// </summary>
    public sealed class StepResult
    {
        /// <summary>このステップが対象としていた ScenarioStep への参照 (UI 表示用)。</summary>
        public ScenarioStep Step { get; }

        /// <summary>ステップが成功したかどうか。</summary>
        public bool Success { get; }

        /// <summary>失敗時の人間可読メッセージ。Success が true のときは null。</summary>
        public string Error { get; }

        /// <summary>Command ステップのときのみ non-null。Wait / Assert では null。</summary>
        public CommandResult CommandResult { get; }

        /// <summary>Assert ステップのときのみ non-null。実際に取得した値。</summary>
        public object ActualValue { get; }

        /// <summary>このステップの所要時間。</summary>
        public TimeSpan Duration { get; }

        public StepResult(
            ScenarioStep step,
            bool success,
            string error,
            CommandResult commandResult,
            object actualValue,
            TimeSpan duration)
        {
            Step = step;
            Success = success;
            Error = error;
            CommandResult = commandResult;
            ActualValue = actualValue;
            Duration = duration;
        }

        /// <summary>成功扱いの StepResult を生成。</summary>
        public static StepResult Ok(ScenarioStep step) =>
            new StepResult(step, true, null, null, null, TimeSpan.Zero);

        /// <summary>失敗扱いの StepResult を生成。</summary>
        public static StepResult Fail(ScenarioStep step, string error) =>
            new StepResult(step, false, error, null, null, TimeSpan.Zero);

        /// <summary>所要時間だけを差し替えた複製を返す (Executor 内で 1 回だけ呼ばれる)。</summary>
        public StepResult WithDuration(TimeSpan duration) =>
            new StepResult(Step, Success, Error, CommandResult, ActualValue, duration);
    }
}
