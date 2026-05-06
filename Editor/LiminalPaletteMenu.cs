using UnityEditor;

namespace Void2610.LiminalPalette.Editor
{
    /// <summary>
    /// LiminalPalette を Unity メニューバーから開けるようにする。
    /// ショートカットを覚えていない / OS でショートカットが衝突している場合のフォールバック導線。
    /// </summary>
    internal static class LiminalPaletteMenu
    {
        // ショートカットは [Shortcut("LiminalPalette/Toggle", ...)] (LiminalPaletteWindow) 側で
        // 一元管理する。MenuItem 側に %k を書くと Shortcut Manager で衝突警告が出るため、
        // ここではメニュー導線のみを提供してキーバインドは付けない。
        [MenuItem("Tools/LiminalPalette/Open Palette", priority = 100)]
        private static void Open() => LiminalPaletteWindow.ShowPalette();
    }
}
