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
        LoadScene,
        AssertCommandReturns,
        AssertEventually,
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

        /// <summary>LiminalObservableField の現在値が expected と一致することを検証するステップ。</summary>
        public static ScenarioStep AssertEquals(
            string observableFieldPath,
            object expected,
            string description = null)
        {
            if (string.IsNullOrEmpty(observableFieldPath))
                throw new ArgumentException("observableFieldPath must not be null or empty", nameof(observableFieldPath));
            return new AssertStep(ScenarioStepKind.AssertEquals, observableFieldPath, expected, description);
        }

        /// <summary>LiminalObservableField の現在値が unexpected と一致しないことを検証するステップ。</summary>
        public static ScenarioStep AssertNotEquals(
            string observableFieldPath,
            object unexpected,
            string description = null)
        {
            if (string.IsNullOrEmpty(observableFieldPath))
                throw new ArgumentException("observableFieldPath must not be null or empty", nameof(observableFieldPath));
            return new AssertStep(ScenarioStepKind.AssertNotEquals, observableFieldPath, unexpected, description);
        }

        /// <summary>
        /// 指定コマンドを実行し、戻り値の文字列が expected と一致することを検証するステップ。
        /// `AssertEquals` (ObservableField の現在値検証) との使い分け:
        ///   - ObservableField の値 → `AssertEquals`
        ///   - コマンドが返す文字列 (観測コマンド等) → `AssertCommandReturns`
        /// args は `Run` と同じく名前→値の辞書。expected との比較は ordinal な string 一致。
        /// expected を null にすると「コマンドが成功すれば OK (戻り値は問わない)」のチェックになる。
        /// </summary>
        public static ScenarioStep AssertCommandReturns(
            string commandPath,
            IReadOnlyDictionary<string, object> args = null,
            string expected = null,
            string description = null)
        {
            if (string.IsNullOrEmpty(commandPath))
                throw new ArgumentException("commandPath must not be null or empty", nameof(commandPath));
            return new AssertCommandReturnsStep(commandPath, args, expected, description);
        }

        /// <summary>
        /// LiminalObservableField の現在値が expected と一致するまで毎フレーム再評価するステップ。
        /// timeoutSeconds 以内に一致すれば成功、超過したら最後の不一致内容を添えて失敗する。
        /// 演出 (LitMotion / UniTask) 完了後に確定する値を、固定待ち (WaitSeconds) なしで検証するためのもの。
        /// 比較規則は `AssertEquals` と同じ (expected が string なら field の型へ変換して比較)。
        /// </summary>
        public static ScenarioStep AssertEventually(
            string observableFieldPath,
            object expected,
            float timeoutSeconds = 5f,
            string description = null)
        {
            if (string.IsNullOrEmpty(observableFieldPath))
                throw new ArgumentException("observableFieldPath must not be null or empty", nameof(observableFieldPath));
            if (timeoutSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "timeoutSeconds must be > 0");
            return new AssertEventuallyStep(observableFieldPath, expected, timeoutSeconds, description);
        }

        /// <summary>
        /// 指定シーンを Single モードで非同期ロードするステップ。完了 (op.isDone) まで待機する。
        /// シーン切替で VContainer のスコープが再構築されるため、後続コマンドは自動的に
        /// 新シーンに登録された instance に解決される。
        /// PlayMode 専用 (Edit Mode では `Application.isPlaying == false` のためステップが失敗する)。
        /// </summary>
        public static ScenarioStep LoadScene(string sceneName, string description = null)
        {
            if (string.IsNullOrEmpty(sceneName))
                throw new ArgumentException("sceneName must not be null or empty", nameof(sceneName));
            return new LoadSceneStep(sceneName, description);
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

    /// <summary>コマンドを実行し、戻り値文字列が expected と一致するかを検証するステップ。</summary>
    internal sealed class AssertCommandReturnsStep : ScenarioStep
    {
        public string CommandPath { get; }
        public IReadOnlyDictionary<string, object> Args { get; }

        /// <summary>期待する戻り値文字列。null の場合は「成功すれば OK」(戻り値内容は問わない)。</summary>
        public string Expected { get; }

        public AssertCommandReturnsStep(
            string commandPath,
            IReadOnlyDictionary<string, object> args,
            string expected,
            string description)
            : base(ScenarioStepKind.AssertCommandReturns, description)
        {
            CommandPath = commandPath;
            Args = args ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            Expected = expected;
        }
    }

    /// <summary>ObservableField の現在値が expected と一致するまでポーリングするステップ。</summary>
    internal sealed class AssertEventuallyStep : ScenarioStep
    {
        public string ObservableFieldPath { get; }
        public object Expected { get; }
        public float TimeoutSeconds { get; }

        public AssertEventuallyStep(string observableFieldPath, object expected, float timeoutSeconds, string description)
            : base(ScenarioStepKind.AssertEventually, description)
        {
            ObservableFieldPath = observableFieldPath;
            Expected = expected;
            TimeoutSeconds = timeoutSeconds;
        }
    }

    /// <summary>シーンを Single モードでロードするステップ。PlayMode 専用。</summary>
    internal sealed class LoadSceneStep : ScenarioStep
    {
        public string SceneName { get; }

        public LoadSceneStep(string sceneName, string description)
            : base(ScenarioStepKind.LoadScene, description)
        {
            SceneName = sceneName;
        }
    }
}
