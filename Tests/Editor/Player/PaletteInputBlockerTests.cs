using NUnit.Framework;
using Void2610.LiminalPalette.Player;

namespace Void2610.LiminalPalette.Tests.Player
{
    /// <summary>
    /// PaletteInputBlocker は Player asmdef では薄いフックポイントだけを提供する。
    /// Engage / Disengage の冪等性 + イベントが想定通り発火することを EditMode で検証する。
    /// </summary>
    public sealed class PaletteInputBlockerTests
    {
        // OnEngage / OnDisengage は static event なので、テスト後に必ず購読を解除して他テストへ漏れさせない。
        private int _engageCount;
        private int _disengageCount;

        [SetUp]
        public void SetUp()
        {
            _engageCount = 0;
            _disengageCount = 0;
            PaletteInputBlocker.OnEngage += OnEngage;
            PaletteInputBlocker.OnDisengage += OnDisengage;
        }

        [TearDown]
        public void TearDown()
        {
            PaletteInputBlocker.OnEngage -= OnEngage;
            PaletteInputBlocker.OnDisengage -= OnDisengage;
        }

        private void OnEngage() => _engageCount++;
        private void OnDisengage() => _disengageCount++;

        [Test]
        public void Engage_FiresOnEngage_AndIsIdempotent()
        {
            var b = new PaletteInputBlocker();
            b.Engage();
            b.Engage();
            Assert.IsTrue(b.IsEngaged);
            Assert.AreEqual(1, _engageCount);
        }

        [Test]
        public void Disengage_AfterEngage_FiresOnDisengage_AndIsIdempotent()
        {
            var b = new PaletteInputBlocker();
            b.Engage();
            b.Disengage();
            b.Disengage();
            Assert.IsFalse(b.IsEngaged);
            Assert.AreEqual(1, _disengageCount);
        }

        [Test]
        public void Disengage_WithoutEngage_DoesNothing()
        {
            var b = new PaletteInputBlocker();
            b.Disengage();
            Assert.IsFalse(b.IsEngaged);
            Assert.AreEqual(0, _disengageCount);
        }
    }
}
