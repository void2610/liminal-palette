using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Tests.UI
{
    /// <summary>
    /// RuntimeColorEditor の単体テスト。Editor で動作するが、実装は UnityEditor 非依存なので
    /// EditMode で構造とコールバックを検証する。
    /// 値変更コールバックは panel 接続が無いと SendEvent が空振りするため、一時 EditorWindow に attach して検証する。
    /// </summary>
    public sealed class RuntimeColorEditorTests
    {
        private EditorWindow _host;

        [SetUp]
        public void SetUp()
        {
            // 仮の EditorWindow を生成し rootVisualElement のパネルを使う。Show は不要 (CreateInstance で十分)。
            _host = ScriptableObject.CreateInstance<EditorWindow>();
            _host.ShowUtility();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) _host.Close();
            _host = null;
        }

        private static ParameterDescriptor Param(Type type, bool hasDefault = false, object defaultValue = null)
            => new ParameterDescriptor("p", type, 0, hasDefault, defaultValue, "", Array.Empty<string>());

        // 4 つの Slider (R/G/B/A) をルートから掘り出すヘルパ。
        private static Slider FindSlider(VisualElement root, string channel)
            => root.Q<Slider>($"lp-color-{channel}-slider");

        // ChangeEvent 発火を伴う value 変更ヘルパ。SetValueWithoutNotify + 手動 SendEvent でパネル不依存に動作する。
        private static void SetValueAndNotify(Slider slider, float newValue)
        {
            var old = slider.value;
            slider.SetValueWithoutNotify(newValue);
            using (var evt = ChangeEvent<float>.GetPooled(old, newValue))
            {
                evt.target = slider;
                slider.SendEvent(evt);
            }
        }

        [Test]
        public void CanHandle_ColorAndColor32_ReturnsTrue()
        {
            var ed = new RuntimeColorEditor();
            Assert.IsTrue(ed.CanHandle(typeof(Color)));
            Assert.IsTrue(ed.CanHandle(typeof(Color32)));
            Assert.IsFalse(ed.CanHandle(typeof(int)));
        }

        [Test]
        public void Build_Color_ContainsFourSliders()
        {
            var ed = new RuntimeColorEditor();
            var ve = ed.Build(Param(typeof(Color)), _ => { });
            Assert.IsNotNull(FindSlider(ve, "r"));
            Assert.IsNotNull(FindSlider(ve, "g"));
            Assert.IsNotNull(FindSlider(ve, "b"));
            Assert.IsNotNull(FindSlider(ve, "a"));
        }

        [Test]
        public void Build_Color_NoDefault_StartsAtWhite()
        {
            var ed = new RuntimeColorEditor();
            var ve = ed.Build(Param(typeof(Color)), _ => { });
            Assert.AreEqual(1f, FindSlider(ve, "r").value);
            Assert.AreEqual(1f, FindSlider(ve, "g").value);
            Assert.AreEqual(1f, FindSlider(ve, "b").value);
            Assert.AreEqual(1f, FindSlider(ve, "a").value);
        }

        [Test]
        public void Build_Color_WithDefault_ReflectsDefault()
        {
            var ed = new RuntimeColorEditor();
            var initial = new Color(0.25f, 0.5f, 0.75f, 1f);
            var ve = ed.Build(Param(typeof(Color), true, initial), _ => { });
            Assert.AreEqual(0.25f, FindSlider(ve, "r").value, 1e-4f);
            Assert.AreEqual(0.5f, FindSlider(ve, "g").value, 1e-4f);
            Assert.AreEqual(0.75f, FindSlider(ve, "b").value, 1e-4f);
            Assert.AreEqual(1f, FindSlider(ve, "a").value, 1e-4f);
        }

        [Test]
        public void Build_Color_OnSliderChange_FiresColorThroughOnChanged()
        {
            var ed = new RuntimeColorEditor();
            object captured = null;
            var ve = ed.Build(Param(typeof(Color)), v => captured = v);
            _host.rootVisualElement.Add(ve);
            SetValueAndNotify(FindSlider(ve, "r"), 0.3f);
            Assert.IsInstanceOf<Color>(captured);
            var c = (Color)captured;
            Assert.AreEqual(0.3f, c.r, 1e-4f);
        }

        [Test]
        public void Build_Color32_OnSliderChange_FiresColor32ThroughOnChanged()
        {
            var ed = new RuntimeColorEditor();
            object captured = null;
            var ve = ed.Build(Param(typeof(Color32)), v => captured = v);
            _host.rootVisualElement.Add(ve);
            // 値変更で型が Color32 にキャストされて流れることを検証 (初期値 1f なので 0f に変化させる)。
            SetValueAndNotify(FindSlider(ve, "r"), 0f);
            Assert.IsInstanceOf<Color32>(captured);
        }

        [Test]
        public void Build_Color32_WithDefault_ReflectsDefault()
        {
            var ed = new RuntimeColorEditor();
            var initial = new Color32(64, 128, 192, 255);
            var ve = ed.Build(Param(typeof(Color32), true, initial), _ => { });
            // Color32 → Color の正規化 (64/255 ≒ 0.251)。
            Assert.AreEqual(64f / 255f, FindSlider(ve, "r").value, 1e-2f);
            Assert.AreEqual(128f / 255f, FindSlider(ve, "g").value, 1e-2f);
            Assert.AreEqual(192f / 255f, FindSlider(ve, "b").value, 1e-2f);
            Assert.AreEqual(1f, FindSlider(ve, "a").value, 1e-2f);
        }
    }
}
