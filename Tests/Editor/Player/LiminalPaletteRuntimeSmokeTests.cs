using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Void2610.LiminalPalette.Player;

namespace Void2610.LiminalPalette.Tests.Player
{
    /// <summary>
    /// LiminalPaletteRuntime の最低限のスモークテスト。EditMode では [RuntimeInitializeOnLoadMethod] は走らないので、
    /// 手動で GameObject を作って AddComponent し、Configure → Show / Hide / Toggle の往復を検証する。
    /// </summary>
    public sealed class LiminalPaletteRuntimeSmokeTests
    {
        private GameObject _go;
        private LiminalPaletteRuntime _runtime;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("[TestLiminalPaletteRuntime]");
            _runtime = _go.AddComponent<LiminalPaletteRuntime>();
            _runtime.Configure(PaletteRuntimeSettings.LoadOrCreateDefault());
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
            _runtime = null;
        }

        [Test]
        public void AfterConfigure_NotVisible()
        {
            Assert.IsFalse(_runtime.IsVisible);
        }

        [Test]
        public void Show_MakesVisible()
        {
            _runtime.Show();
            Assert.IsTrue(_runtime.IsVisible);
        }

        [Test]
        public void Hide_MakesInvisible()
        {
            _runtime.Show();
            _runtime.Hide();
            Assert.IsFalse(_runtime.IsVisible);
        }

        [Test]
        public void Toggle_FlipsVisibility()
        {
            _runtime.Toggle();
            Assert.IsTrue(_runtime.IsVisible);
            _runtime.Toggle();
            Assert.IsFalse(_runtime.IsVisible);
        }

        [Test]
        public void Show_TwiceDoesNotRebuildView()
        {
            _runtime.Show();
            var doc = _go.GetComponent<UIDocument>();
            Assert.IsNotNull(doc);
            var firstChildCount = doc.rootVisualElement.childCount;
            _runtime.Hide();
            _runtime.Show();
            Assert.AreEqual(firstChildCount, doc.rootVisualElement.childCount);
        }
    }
}
