using UnityEditor;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Editor
{
    /// <summary>
    /// Editor 起動時に UnityEditor.UIElements 依存のエディタを ParameterEditorRegistry に追加登録する。
    /// UI asmdef は UnityEditor を参照しない設計のため、こちらで自動登録する。
    /// 後から Register したものが先頭 (高優先度) に来るので、Color / Object / Flags 系は UI 側の Fallback / EnumEditor より先に解決される。
    ///
    /// Phase 3 以降の登録順序 (先頭 = 高優先):
    ///   1. static cctor (UI asmdef) → FallbackText / Primitive / Enum / Vector  (最低優先)
    ///   2. [RuntimeInitializeOnLoadMethod] (UI asmdef, RuntimeArgumentEditorBootstrap)
    ///        → RuntimeColor / RuntimeObject / RuntimeEnumFlags  (中優先、Runtime で動作)
    ///   3. [InitializeOnLoadMethod] (本ファイル) → EditorColor / EditorObject / EditorEnumFlags  (最高優先、Editor のみ)
    /// 結果として Editor では Editor 用エディタが、Runtime では Runtime 用エディタが選ばれる。
    /// </summary>
    internal static class EditorParameterEditorBootstrap
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            // Editor 用エディタは UnityEditor.UIElements の ColorField / ObjectField / EnumFlagsField を使うため、
            // Runtime 版より UX が高い。Runtime 版の上から登録して上書き優先にする。
            ParameterEditorRegistry.Register(new EditorColorEditor());
            ParameterEditorRegistry.Register(new EditorObjectEditor());
            ParameterEditorRegistry.Register(new EditorEnumFlagsEditor());
        }
    }
}
