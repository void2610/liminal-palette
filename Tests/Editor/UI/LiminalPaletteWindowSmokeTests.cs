using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using Void2610.LiminalPalette.Editor;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Tests.UI
{
    /// <summary>
    /// LiminalPaletteWindow の最低限のスモークテスト。
    /// 通常タブ (`utility: false`) として開く前提で、PaletteView の主要要素 (検索ボックス / 結果リスト) が
    /// rootVisualElement に正しく挿入されているかを検証する。
    /// `OnLostFocus` 自動クローズはドッキング体験のため廃止済みなので、テスト中の意図しないクローズは発生しない。
    /// </summary>
    public sealed class LiminalPaletteWindowSmokeTests
    {
        private LiminalPaletteWindow _window;

        [SetUp]
        public void SetUp()
        {
            // 通常タブとして開く (ドッキング可能)。OnLostFocus 自動クローズは廃止済みのためテスト中の意図しない close は発生しない。
            _window = LiminalPaletteWindow.ShowPalette();
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null) _window.Close();
            _window = null;
        }

        [Test]
        public void Show_OpensWindow_AndAttachesPaletteView()
        {
            Assert.IsNotNull(_window);
            // PaletteView が rootVisualElement の子として存在すること。
            var view = _window.rootVisualElement.Q<PaletteView>();
            Assert.IsNotNull(view, "PaletteView should be present in rootVisualElement");
        }

        [Test]
        public void Show_HasSearchInput()
        {
            // UXML の検索ボックスが見つかること。
            var search = _window.rootVisualElement.Q<TextField>("search-input");
            Assert.IsNotNull(search);
        }

        [Test]
        public void Show_HasResultsList()
        {
            var list = _window.rootVisualElement.Q<ListView>("results-list");
            Assert.IsNotNull(list);
        }

        [Test]
        public void Close_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _window.Close());
            // 二重 Close 防止のため null 化しておく。
            _window = null;
        }
    }
}
