using System;
using System.Collections.Generic;
using UnityEngine;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// IScenarioRegistry の標準実装。プロセス共有の Default インスタンスを持つ。
    /// </summary>
    public sealed class ScenarioRegistry : IScenarioRegistry
    {
        public static ScenarioRegistry Default { get; } = new ScenarioRegistry();

        public IReadOnlyList<ScenarioDescriptor> All => _ordered;

        private readonly List<ScenarioDescriptor> _ordered = new List<ScenarioDescriptor>();
        private readonly Dictionary<string, ScenarioDescriptor> _byPath
            = new Dictionary<string, ScenarioDescriptor>(StringComparer.OrdinalIgnoreCase);

        public event Action<ScenarioDescriptor> Registered;

        public ScenarioDescriptor Find(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            _byPath.TryGetValue(path, out var d);
            return d;
        }

        public void Register(ScenarioDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (_byPath.TryGetValue(descriptor.Path, out var existing))
            {
                // 同じ MethodInfo (= 同じメンバーを再スキャンしたケース) は黙ってスキップ。
                // ScanAll を複数回呼んでも警告が積み上がらないようにするための連続スキャン耐性。
                if (existing.Method == descriptor.Method) return;
                Debug.LogWarning($"[LiminalPalette] Duplicate scenario path '{descriptor.Path}' — overwriting previous registration ({existing.Method?.DeclaringType?.FullName}.{existing.Method?.Name}).");
                _ordered.Remove(existing);
            }
            _byPath[descriptor.Path] = descriptor;
            _ordered.Add(descriptor);
            Registered?.Invoke(descriptor);
        }

        public void Clear()
        {
            _ordered.Clear();
            _byPath.Clear();
        }
    }
}
