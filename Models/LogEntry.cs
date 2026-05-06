using System;
using UnityEngine;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// コマンド実行中に取り込んだ Debug.Log エントリの 1 行。
    /// </summary>
    public sealed class LogEntry
    {
        /// <summary>Unity の LogType (Log / Warning / Error / Exception / Assert)。</summary>
        public LogType Type { get; }

        /// <summary>ログ本文。</summary>
        public string Message { get; }

        /// <summary>スタックトレース (Unity が付与した文字列をそのまま保持)。</summary>
        public string StackTrace { get; }

        /// <summary>ログを取り込んだ時刻 (UTC)。</summary>
        public DateTime TimestampUtc { get; }

        public LogEntry(LogType type, string message, string stackTrace, DateTime timestampUtc)
        {
            Type = type;
            Message = message ?? "";
            StackTrace = stackTrace ?? "";
            TimestampUtc = timestampUtc;
        }
    }
}
