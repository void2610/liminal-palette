using UnityEditor;
using Void2610.LiminalPalette;

namespace Void2610.LiminalPalette.Editor
{
    /// <summary>
    /// <see cref="InvocationStore"/> の Editor 用保存先。EditorPrefs に置くので Editor を再起動しても残る。
    ///
    /// <see cref="EditorCommandHistory"/> と同じく、キーは差し替えられるようにしてある
    /// (テストが利用者の記録を壊さないため)。
    /// </summary>
    public sealed class EditorPrefsInvocationStorage : IInvocationStorage
    {
        public const string PrefsKey = "Void2610.LiminalPalette.Invocations";

        private readonly string _prefsKey;

        public EditorPrefsInvocationStorage() : this(PrefsKey)
        {
        }

        internal EditorPrefsInvocationStorage(string prefsKey)
        {
            _prefsKey = prefsKey;
        }

        public string Read() => EditorPrefs.GetString(_prefsKey, "");

        public void Write(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                EditorPrefs.DeleteKey(_prefsKey);
                return;
            }
            EditorPrefs.SetString(_prefsKey, raw);
        }

        public void Delete() => EditorPrefs.DeleteKey(_prefsKey);
    }

    /// <summary>
    /// 起動と domain reload のたびに保存先を繋ぎ直す。
    /// static シングルトンは reload で作り直されるため、毎回ここで復元しないと中身が空のままになる。
    /// </summary>
    [InitializeOnLoad]
    internal static class InvocationStorageBootstrap
    {
        static InvocationStorageBootstrap()
        {
            InvocationStore.Instance.AttachStorage(new EditorPrefsInvocationStorage());
        }
    }
}
