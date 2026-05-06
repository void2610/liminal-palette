using System;
using System.Collections.Generic;
using UnityEditor;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Editor
{
    /// <summary>
    /// EditorPrefs に永続化する Editor 専用の履歴実装。
    /// シリアライズは制御文字 ( = Unit Separator) で区切る単純なフラット形式。
    /// JsonUtility 経由でラッパー class を持つよりも軽量で、コマンドパスにこの文字を許容する設計上の理由もない。
    /// </summary>
    public sealed class EditorCommandHistory : ICommandHistory
    {
        // EditorPrefs のキー。プロジェクト共通だが namespace で衝突を避ける。
        public const string PrefsKey = "Void2610.LiminalPalette.History";

        private const char Separator = '';

        private readonly InMemoryCommandHistory _inner = new InMemoryCommandHistory();

        public EditorCommandHistory()
        {
            Load();
        }

        public IReadOnlyList<string> RecentPaths => _inner.RecentPaths;

        public void Record(string path)
        {
            _inner.Record(path);
            Save();
        }

        public void Clear()
        {
            _inner.Clear();
            EditorPrefs.DeleteKey(PrefsKey);
        }

        public bool Contains(string path) => _inner.Contains(path);
        public int IndexOf(string path) => _inner.IndexOf(path);

        private void Load()
        {
            var raw = EditorPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(raw)) return;

            var parts = raw.Split(Separator);
            // 入力時の上限保証は InMemoryCommandHistory.Record がやってくれるので逆順に詰める。
            // (新しい順で保存しているので、古い側から Record して先頭に最新が積まれる順を再現する)
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(parts[i])) _inner.Record(parts[i]);
            }
        }

        private void Save()
        {
            var paths = _inner.RecentPaths;
            if (paths.Count == 0)
            {
                EditorPrefs.DeleteKey(PrefsKey);
                return;
            }
            // string.Join で十分。string.Concat でも可だが Separator を 1 文字なので Join の方が自然。
            var arr = new string[paths.Count];
            for (var i = 0; i < paths.Count; i++) arr[i] = paths[i];
            EditorPrefs.SetString(PrefsKey, string.Join(Separator.ToString(), arr));
        }
    }
}
