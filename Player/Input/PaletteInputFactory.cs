using UnityEngine;

namespace Void2610.LiminalPalette.Player
{
    /// <summary>
    /// IPaletteInput の生成口。
    /// Runtime のホットキー検出は IMGUI (UnityEngine.Event) で全環境を一発で賄えるので、
    /// 旧来の InputSystem 実装 / Legacy 実装の振り分けは廃止し常に <see cref="EventPaletteInput"/> を返す。
    ///
    /// 旧構成では InputSystem 経由で Keyboard を polling していたが、
    /// macOS で Cmd+P から Play Mode に入ったとき Cmd の keyup を Editor が消費して isPressed が固着し、
    /// K 単独でも toggle が誤発火する Unity 既知挙動を踏んでいた。
    /// IMGUI イベントは OS イベントキュー由来で固着しないため、ここで一本化する。
    /// </summary>
    public static class PaletteInputFactory
    {
        public static IPaletteInput Create(KeyCode toggleKey, bool requireModifier)
            => new EventPaletteInput(toggleKey, requireModifier);
    }
}
