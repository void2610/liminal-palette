using System;
using System.Collections.Generic;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// コマンド実行結果。UI / IPC / テストで共通利用される構造化レスポンス。
    /// 例外は本クラスに変換され、利用側は try-catch せずに済む。
    /// </summary>
    public sealed class CommandResult
    {
        /// <summary>true なら正常終了。false なら Error / Exception を確認すること。</summary>
        public bool Success { get; }

        /// <summary>戻り値。void / Task / 失敗時は null。</summary>
        public object? Value { get; }

        /// <summary>失敗時のメッセージ。Success が true なら null。</summary>
        public string? Error { get; }

        /// <summary>失敗時の例外オブジェクト。デバッグ用途。IPC 送信時には除外する想定。Success が true、または非例外起因の失敗 (バインドエラー等) の場合は null。</summary>
        public Exception? Exception { get; }

        /// <summary>実行中に取り込まれた Debug.Log の集約。</summary>
        public IReadOnlyList<LogEntry> Logs { get; }

        /// <summary>実行所要時間。引数バインド失敗などコマンド未起動の場合は TimeSpan.Zero。</summary>
        public TimeSpan Duration { get; }

        private CommandResult(bool success, object? value, string? error, Exception? exception, IReadOnlyList<LogEntry> logs, TimeSpan duration)
        {
            Success = success;
            Value = value;
            Error = error;
            Exception = exception;
            Logs = logs ?? Array.Empty<LogEntry>();
            Duration = duration;
        }

        /// <summary>成功結果を生成。</summary>
        public static CommandResult Ok(object? value, IReadOnlyList<LogEntry> logs, TimeSpan duration) => new CommandResult(true, value, null, null, logs, duration);

        /// <summary>失敗結果を生成。例外がない (バインドエラー等) 場合は exception に null を渡す。</summary>
        public static CommandResult Fail(string error, Exception? exception, IReadOnlyList<LogEntry> logs, TimeSpan duration) => new CommandResult(false, null, error ?? "Unknown error", exception, logs, duration);
    }
}
