using System;
using System.Collections.Generic;

namespace Void2610.LiminalPalette
{
    /// <summary>シナリオステップの種別。</summary>
    public enum ScenarioStepKind
    {
        Command,
        WaitSeconds,
        WaitFrames,
        AssertEquals,
        AssertNotEquals,
    }

    /// <summary>
    /// シナリオステップの不変表現。判別子 (Kind) を持つことで JSON シリアライズ /
    /// UI 表示 / Executor の switch が素直に書ける。派生型は internal で隠す。
    /// </summary>
    public abstract class ScenarioStep
    {
        public ScenarioStepKind Kind { get; }

        /// <summary>ログ / UI 表示用の説明文 (任意)。</summary>
        public string Description { get; }

        protected ScenarioStep(ScenarioStepKind kind, string description)
        {
            Kind = kind;
            Description = description ?? "";
        }

        // ---- ファクトリ ----

        /// <summary>コマンドを呼び出すステップ。args は名前→値の型解決済み辞書。</summary>
        public static ScenarioStep Run(
            string commandPath,
            IReadOnlyDictionary<string, object> args = null,
            string description = null)
        {
            if (string.IsNullOrEmpty(commandPath))
                throw new ArgumentException("commandPath must not be null or empty", nameof(commandPath));
            return new CommandStep(commandPath, args, description);
        }

        /// <summary>指定秒数だけ待機するステップ。</summary>
        public static ScenarioStep WaitSeconds(float seconds, string description = null)
        {
            if (seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds), "seconds must be >= 0");
            return new WaitStep(ScenarioStepKind.WaitSeconds, seconds, 0, description);
        }

        /// <summary>指定フレーム数だけ待機するステップ。</summary>
        public static ScenarioStep WaitFrames(int frames, string description = null)
        {
            if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames), "frames must be >= 0");
            return new WaitStep(ScenarioStepKind.WaitFrames, 0f, frames, description);
        }

        /// <summary>ConsoleObservableField の現在値が expected と一致することを検証するステップ。</summary>
        public static ScenarioStep AssertEquals(
            string observableFieldPath,
            object expected,
            string description = null)
        {
            if (string.IsNullOrEmpty(observableFieldPath))
                throw new ArgumentException("observableFieldPath must not be null or empty", nameof(observableFieldPath));
            return new AssertStep(ScenarioStepKind.AssertEquals, observableFieldPath, expected, description);
        }

        /// <summary>ConsoleObservableField の現在値が unexpected と一致しないことを検証するステップ。</summary>
        public static ScenarioStep AssertNotEquals(
            string observableFieldPath,
            object unexpected,
            string description = null)
        {
            if (string.IsNullOrEmpty(observableFieldPath))
                throw new ArgumentException("observableFieldPath must not be null or empty", nameof(observableFieldPath));
            return new AssertStep(ScenarioStepKind.AssertNotEquals, observableFieldPath, unexpected, description);
        }
    }

    /// <summary>コマンドを呼び出すステップ。</summary>
    internal sealed class CommandStep : ScenarioStep
    {
        public string CommandPath { get; }
        public IReadOnlyDictionary<string, object> Args { get; }

        public CommandStep(string commandPath, IReadOnlyDictionary<string, object> args, string description)
            : base(ScenarioStepKind.Command, description)
        {
            CommandPath = commandPath;
            // null は空辞書として正規化。Executor 側で再ガードする手間を省く。
            Args = args ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>時間 / フレーム数で待機するステップ。Kind で WaitSeconds / WaitFrames を区別する。</summary>
    internal sealed class WaitStep : ScenarioStep
    {
        public float Seconds { get; }
        public int Frames { get; }

        public WaitStep(ScenarioStepKind kind, float seconds, int frames, string description)
            : base(kind, description)
        {
            Seconds = seconds;
            Frames = frames;
        }
    }

    /// <summary>ObservableField の現在値を検証するステップ。Kind で Equals / NotEquals を区別する。</summary>
    internal sealed class AssertStep : ScenarioStep
    {
        public string ObservableFieldPath { get; }
        public object Expected { get; }

        public AssertStep(ScenarioStepKind kind, string observableFieldPath, object expected, string description)
            : base(kind, description)
        {
            ObservableFieldPath = observableFieldPath;
            Expected = expected;
        }
    }
}
