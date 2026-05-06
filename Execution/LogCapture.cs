using System;
using System.Collections.Generic;
using UnityEngine;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// コマンド実行中の Application.logMessageReceivedThreaded を購読し、LogEntry に集約する IDisposable。
    /// 並列実行時はログが混ざる可能性がある (Phase 1 のスコープ外。Phase 2 で実行 ID 振り分けを検討)。
    /// </summary>
    internal sealed class LogCapture : IDisposable
    {
        private readonly List<LogEntry> _entries = new List<LogEntry>();
        private readonly object _lock = new object();
        private bool _disposed;

        public LogCapture()
        {
            // logMessageReceivedThreaded を選ぶ理由: 別スレッド (Task continuation など) からの
            // Debug.Log もキャプチャしたいため。
            Application.logMessageReceivedThreaded += OnLog;
        }

        /// <summary>これまでに集めたエントリのスナップショットを返し、内部バッファはクリアしない。</summary>
        public IReadOnlyList<LogEntry> Drain()
        {
            lock (_lock)
            {
                return _entries.ToArray();
            }
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (_disposed) return;
            lock (_lock)
            {
                _entries.Add(new LogEntry(type, condition, stackTrace, DateTime.UtcNow));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Application.logMessageReceivedThreaded -= OnLog;
        }
    }
}
