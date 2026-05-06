using System;
using System.Collections.Generic;
using System.Linq;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// IObservableFieldRegistry の標準実装。
    /// プロセス共有のシングルトン (Default) を持ち、Bootstrap が起動時にスキャン結果を投入する。
    /// 同一 Path の重複登録は警告ログ + 後勝ちで上書き (CommandRegistry と同じ流儀)。
    /// </summary>
    public sealed class ObservableFieldRegistry : IObservableFieldRegistry
    {
        public static ObservableFieldRegistry Default { get; } = new ObservableFieldRegistry();

        private readonly Dictionary<string, ObservableFieldDescriptor> _byPath
            = new Dictionary<string, ObservableFieldDescriptor>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ObservableFieldDescriptor> _ordered = new List<ObservableFieldDescriptor>();
        private readonly object _lock = new object();

        public IReadOnlyList<ObservableFieldDescriptor> All
        {
            get { lock (_lock) return _ordered.ToArray(); }
        }

        public void Register(ObservableFieldDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            lock (_lock)
            {
                if (_byPath.TryGetValue(descriptor.Path, out var existing))
                {
                    // 同 Path + 同 DeclaringType は ScanAll の再呼び出しによる二重登録とみなして黙ってスキップする。
                    // 同じメンバーから生成された descriptor の ReadCurrent/Subscribe は等価な Reflection を捕捉しており
                    // 機能的に同一なので、既存エントリを保持して問題ない (連続スキャン耐性)。
                    if (existing.DeclaringType == descriptor.DeclaringType) return;
                    // 異なる DeclaringType が同じ Path を要求しているのは利用側の設定ミス。後勝ちで上書き + 警告を残す。
                    UnityEngine.Debug.LogWarning(
                        $"[LiminalPalette] ObservableField duplicate path '{descriptor.Path}'. " +
                        $"Replacing previous entry from {existing.DeclaringType?.FullName} with {descriptor.DeclaringType?.FullName}.");
                    _ordered.RemoveAll(d => string.Equals(d.Path, descriptor.Path, StringComparison.OrdinalIgnoreCase));
                }
                _byPath[descriptor.Path] = descriptor;
                _ordered.Add(descriptor);
            }
        }

        public ObservableFieldDescriptor Find(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            lock (_lock)
            {
                _byPath.TryGetValue(path, out var d);
                return d;
            }
        }

        public IReadOnlyList<ObservableFieldDescriptor> FindByPathPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return Array.Empty<ObservableFieldDescriptor>();
            lock (_lock)
            {
                return _ordered
                    .Where(d => d.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
        }

        /// <summary>テスト用: 登録を全消去 + 再登録を呼べるようにする。</summary>
        internal void ClearForTest()
        {
            lock (_lock)
            {
                _byPath.Clear();
                _ordered.Clear();
            }
        }
    }
}
