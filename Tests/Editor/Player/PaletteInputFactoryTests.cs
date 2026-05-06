using NUnit.Framework;
using UnityEngine;
using Void2610.LiminalPalette.Player;

namespace Void2610.LiminalPalette.Tests.Player
{
    public sealed class PaletteInputFactoryTests
    {
        // Factory は IMGUI ベースの EventPaletteInput を常に返す設計に統一されたため、
        // 旧 OverrideFactory 経由の振り分けテストは廃止し、生成型と入力非依存性だけを保証する。

        [Test]
        public void Create_ReturnsEventPaletteInput()
        {
            var resolved = PaletteInputFactory.Create(KeyCode.BackQuote, true);
            Assert.IsNotNull(resolved);
            Assert.IsInstanceOf<EventPaletteInput>(resolved);
        }

        [Test]
        public void Create_WithoutModifier_StillReturnsEventPaletteInput()
        {
            var resolved = PaletteInputFactory.Create(KeyCode.K, false);
            Assert.IsNotNull(resolved);
            Assert.IsInstanceOf<EventPaletteInput>(resolved);
        }
    }
}
