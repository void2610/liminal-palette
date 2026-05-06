using System;
using System.Collections.Generic;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// プロセス内のみで保持する履歴実装。Runtime の既定値、およびテストの注入対象。
    /// </summary>
    public sealed class InMemoryCommandHistory : ICommandHistory
    {
        /// <summary>履歴の上限件数。超えたら古いものから削除する。</summary>
        public const int MaxEntries = 50;

        // 先頭が最新。null / 空は受理しない。
        private readonly List<string> _entries = new List<string>(MaxEntries);

        public IReadOnlyList<string> RecentPaths => _entries;

        public void Record(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            // 既存があれば削除して先頭に移動 (大文字小文字を区別しない比較)。
            for (var i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i], path, StringComparison.OrdinalIgnoreCase))
                {
                    _entries.RemoveAt(i);
                    break;
                }
            }

            _entries.Insert(0, path);

            // 上限を超えていたら末尾から削除。
            while (_entries.Count > MaxEntries) _entries.RemoveAt(_entries.Count - 1);
        }

        public void Clear() => _entries.Clear();

        public bool Contains(string path) => IndexOf(path) >= 0;

        public int IndexOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return -1;
            for (var i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i], path, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }
    }
}
