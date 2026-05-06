using UnityEngine;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// Runtime 起動時に UI 内のフォールバック Runtime エディタ (Color / Object / Flags) を ParameterEditorRegistry に登録する。
    /// ParameterEditorRegistry.Register は先頭挿入なので、後から登録するほど高優先になる。
    /// 順序:
    ///   1. static cctor → FallbackText / Primitive / Enum / Vector  (最低優先、UI asmdef)
    ///   2. [RuntimeInitializeOnLoadMethod] → RuntimeColor / RuntimeObject / RuntimeEnumFlags  (中優先、本ファイル)
    ///   3. [InitializeOnLoadMethod] → EditorColor / EditorObject / EditorEnumFlags  (Editor のみ最高優先)
    /// これにより Runtime では本ファイルの登録が、Editor では Editor 用エディタが選ばれる。
    /// </summary>
    internal static class RuntimeArgumentEditorBootstrap
    {
        // BeforeSceneLoad にすることで最初のシーンの Awake より前に登録が完了する。
        // EditMode テストでは [InitializeOnLoadMethod] と同等の経路では呼ばれないため、
        // テスト側で明示的に Register を呼ぶ補助 API を Editor 用に用意する。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterRuntime()
        {
            RegisterAll();
        }

        /// <summary>テストや Editor 起動経路から手動で再登録するための補助 API。</summary>
        internal static void RegisterAll()
        {
            ParameterEditorRegistry.Register(new RuntimeColorEditor());
            ParameterEditorRegistry.Register(new RuntimeObjectEditor());
            ParameterEditorRegistry.Register(new RuntimeEnumFlagsEditor());
        }
    }
}
