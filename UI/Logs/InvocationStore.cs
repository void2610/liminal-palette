using System;
using System.Collections.Generic;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// パレット経由で行われたコマンド実行の履歴を保持するシングルトン。
    /// Log タブ (詳細閲覧) と History タブ (再実行) が共通で参照するソース。
    /// </summary>
    public sealed class InvocationStore
    {
        public const int Capacity = 200;

        public static InvocationStore Instance { get; } = new InvocationStore();

        private readonly List<CommandInvocation> _entries = new List<CommandInvocation>(Capacity);
        private readonly object _lock = new object();

        /// <summary>追加 / クリア時に発火。UI 側で itemsSource を更新する。</summary>
        public event Action Changed;

        /// <summary>取得時点のスナップショット (古い順)。新しい順に並べたい場合は呼び出し側で逆走査する。</summary>
        public IReadOnlyList<CommandInvocation> Entries
        {
            get { lock (_lock) return _entries.ToArray(); }
        }

        public int Count
        {
            get { lock (_lock) return _entries.Count; }
        }

        private InvocationStore() { }

        /// <summary>1 回の実行を記録する。args は CommandExecutor に渡された型解決済み辞書を想定。</summary>
        public void Record(string path, IReadOnlyDictionary<string, object> args, CommandResult result)
            => Record(path, args, result, isFromScenario: false);

        /// <summary>
        /// 1 回の実行を記録する (シナリオ由来フラグ付き)。
        /// シナリオ由来エントリは History タブで除外され、Log タブのみで閲覧可能。
        /// </summary>
        public void Record(string path, IReadOnlyDictionary<string, object> args, CommandResult result, bool isFromScenario)
        {
            if (string.IsNullOrEmpty(path) || result == null) return;
            // 引数辞書はディフェンシブにコピーする (呼び出し側が UI 状態として再利用する辞書だと履歴が破壊されるため)。
            var copy = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (args != null)
            {
                foreach (var kv in args) copy[kv.Key] = kv.Value;
            }
            var entry = new CommandInvocation(path, copy, result, DateTime.UtcNow, isFromScenario);
            lock (_lock)
            {
                _entries.Add(entry);
                while (_entries.Count > Capacity) _entries.RemoveAt(0);
            }
            Changed?.Invoke();
        }

        /// <summary>履歴を消す。</summary>
        public void Clear()
        {
            lock (_lock) _entries.Clear();
            Changed?.Invoke();
        }
    }
}
