using System;
using System.Collections.Generic;
using UnityEngine;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// ICommandRegistry の標準実装。
    /// パスと別名の双方を Dictionary で引けるよう保持し、検索コストを O(1) に保つ。
    /// </summary>
    public sealed class CommandRegistry : ICommandRegistry
    {
        /// <summary>プロセス共有のデフォルトインスタンス。Bootstrap がここに登録する。</summary>
        public static CommandRegistry Default { get; } = new CommandRegistry();

        public IReadOnlyList<CommandDescriptor> All => _ordered;

        // 登録順を保つために List も併用 (UI で「登録順」表示するケースを想定)。
        private readonly List<CommandDescriptor> _ordered = new List<CommandDescriptor>();

        // パスとエイリアスは別辞書で持ち、衝突を検出しやすくする。
        // 検索は両方をマージして OrdinalIgnoreCase で行う。
        private readonly Dictionary<string, CommandDescriptor> _byPath
            = new Dictionary<string, CommandDescriptor>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CommandDescriptor> _byAlias
            = new Dictionary<string, CommandDescriptor>(StringComparer.OrdinalIgnoreCase);

        public event Action<CommandDescriptor> Registered;
        public event Action<CommandDescriptor> Unregistered;

        public CommandDescriptor Find(string pathOrAlias)
        {
            if (string.IsNullOrEmpty(pathOrAlias)) return null;
            if (_byPath.TryGetValue(pathOrAlias, out var byPath)) return byPath;
            if (_byAlias.TryGetValue(pathOrAlias, out var byAlias)) return byAlias;
            return null;
        }

        public IEnumerable<CommandDescriptor> FindByCategory(string categoryPrefix)
        {
            // 空プレフィックスは全件返却。末尾スラッシュは正規化して比較する。
            var prefix = categoryPrefix ?? "";
            if (prefix.EndsWith("/")) prefix = prefix.Substring(0, prefix.Length - 1);

            for (var i = 0; i < _ordered.Count; i++)
            {
                var c = _ordered[i];
                if (prefix.Length == 0)
                {
                    yield return c;
                    continue;
                }
                // "Player" は "Player/Health/Set" を含むが "PlayerExtra/..." を誤って含まないよう
                // 末尾が "/" である境界も比較する。
                if (string.Equals(c.Category, prefix, StringComparison.OrdinalIgnoreCase)) yield return c;
                else if (c.Category.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)) yield return c;
                else if (string.Equals(c.Path, prefix, StringComparison.OrdinalIgnoreCase)) yield return c;
            }
        }

        public void Register(CommandDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            // 同一パスがすでに登録されていれば、警告を出して上書きする (後勝ち)。
            // ただし同じ MethodInfo (= 同じメンバーの再スキャン) なら警告無しで黙ってスキップする。
            // ScanAll を複数回呼んだときに警告が積み上がるのを避けるための連続スキャン耐性。
            if (_byPath.TryGetValue(descriptor.Path, out var existing))
            {
                if (existing.Method == descriptor.Method) return;
                Debug.LogWarning($"[LiminalPalette] Duplicate command path '{descriptor.Path}' — overwriting previous registration ({existing.Method.DeclaringType?.FullName}.{existing.Method.Name}).");
                RemoveInternal(existing);
            }

            _byPath[descriptor.Path] = descriptor;
            _ordered.Add(descriptor);

            // エイリアスを登録。パスや他コマンドのエイリアスと衝突したら警告して登録は中止 (パス側のみ有効)。
            for (var i = 0; i < descriptor.Aliases.Count; i++)
            {
                var alias = descriptor.Aliases[i];
                if (string.IsNullOrEmpty(alias)) continue;

                if (_byPath.ContainsKey(alias))
                {
                    Debug.LogWarning($"[LiminalPalette] Alias '{alias}' for '{descriptor.Path}' collides with an existing command path — alias ignored.");
                    continue;
                }
                if (_byAlias.TryGetValue(alias, out var aliasOwner) && aliasOwner != descriptor)
                {
                    Debug.LogWarning($"[LiminalPalette] Alias '{alias}' is already used by '{aliasOwner.Path}' — alias ignored for '{descriptor.Path}'.");
                    continue;
                }
                _byAlias[alias] = descriptor;
            }

            Registered?.Invoke(descriptor);
        }

        public bool Unregister(string pathOrAlias)
        {
            var d = Find(pathOrAlias);
            if (d == null) return false;
            RemoveInternal(d);
            Unregistered?.Invoke(d);
            return true;
        }

        public void Clear()
        {
            _ordered.Clear();
            _byPath.Clear();
            _byAlias.Clear();
        }

        // 内部削除: Register の上書き処理から呼ばれる共通ロジック。
        private void RemoveInternal(CommandDescriptor d)
        {
            _byPath.Remove(d.Path);
            _ordered.Remove(d);
            // エイリアスはこのコマンドが所有しているもののみ削除する。
            // 他コマンドのエイリアスを巻き込まないよう参照同一性で確認。
            var keysToRemove = new List<string>();
            foreach (var kv in _byAlias)
            {
                if (kv.Value == d) keysToRemove.Add(kv.Key);
            }
            for (var i = 0; i < keysToRemove.Count; i++) _byAlias.Remove(keysToRemove[i]);
        }
    }
}
