using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Tests.UI
{
    public sealed class RuntimeEnumFlagsEditorTests
    {
        [Flags] private enum SampleFlags { None = 0, X = 1, Y = 2, Z = 4 }

        private enum NotFlags { A, B }

        private EditorWindow _host;

        [SetUp]
        public void SetUp()
        {
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

        // panel 不在では BaseField.value セッタが SendEvent をスキップするので、明示的に発火させる。
        private static void SetToggleAndNotify(Toggle toggle, bool newValue)
        {
            var old = toggle.value;
            toggle.SetValueWithoutNotify(newValue);
            using (var evt = ChangeEvent<bool>.GetPooled(old, newValue))
            {
                evt.target = toggle;
                toggle.SendEvent(evt);
            }
        }

        [Test]
        public void CanHandle_FlagsEnum_ReturnsTrue()
        {
            var ed = new RuntimeEnumFlagsEditor();
            Assert.IsTrue(ed.CanHandle(typeof(SampleFlags)));
        }

        [Test]
        public void CanHandle_NonFlagsEnum_ReturnsFalse()
        {
            var ed = new RuntimeEnumFlagsEditor();
            Assert.IsFalse(ed.CanHandle(typeof(NotFlags)));
        }

        [Test]
        public void CanHandle_NonEnum_ReturnsFalse()
        {
            var ed = new RuntimeEnumFlagsEditor();
            Assert.IsFalse(ed.CanHandle(typeof(int)));
        }

        [Test]
        public void Build_GeneratesToggleForEachValue()
        {
            var ed = new RuntimeEnumFlagsEditor();
            var ve = ed.Build(Param(typeof(SampleFlags)), _ => { });
            // None / X / Y / Z の 4 値ぶん Toggle が並ぶ。
            Assert.IsNotNull(ve.Q<Toggle>("lp-flag-None"));
            Assert.IsNotNull(ve.Q<Toggle>("lp-flag-X"));
            Assert.IsNotNull(ve.Q<Toggle>("lp-flag-Y"));
            Assert.IsNotNull(ve.Q<Toggle>("lp-flag-Z"));
        }

        [Test]
        public void Build_NoDefault_NoneToggleIsInitiallyOn()
        {
            var ed = new RuntimeEnumFlagsEditor();
            var ve = ed.Build(Param(typeof(SampleFlags)), _ => { });
            Assert.IsTrue(ve.Q<Toggle>("lp-flag-None").value);
            Assert.IsFalse(ve.Q<Toggle>("lp-flag-X").value);
            Assert.IsFalse(ve.Q<Toggle>("lp-flag-Y").value);
        }

        [Test]
        public void Build_WithDefault_RelevantTogglesAreOn()
        {
            var ed = new RuntimeEnumFlagsEditor();
            var ve = ed.Build(Param(typeof(SampleFlags), true, SampleFlags.X | SampleFlags.Z), _ => { });
            Assert.IsFalse(ve.Q<Toggle>("lp-flag-None").value);
            Assert.IsTrue(ve.Q<Toggle>("lp-flag-X").value);
            Assert.IsFalse(ve.Q<Toggle>("lp-flag-Y").value);
            Assert.IsTrue(ve.Q<Toggle>("lp-flag-Z").value);
        }

        [Test]
        public void Build_TogglingBit_FiresOredEnumValue()
        {
            var ed = new RuntimeEnumFlagsEditor();
            object captured = null;
            var ve = ed.Build(Param(typeof(SampleFlags)), v => captured = v);
            _host.rootVisualElement.Add(ve);

            SetToggleAndNotify(ve.Q<Toggle>("lp-flag-X"), true);
            Assert.AreEqual(SampleFlags.X, (SampleFlags)captured);

            SetToggleAndNotify(ve.Q<Toggle>("lp-flag-Y"), true);
            Assert.AreEqual(SampleFlags.X | SampleFlags.Y, (SampleFlags)captured);

            SetToggleAndNotify(ve.Q<Toggle>("lp-flag-X"), false);
            Assert.AreEqual(SampleFlags.Y, (SampleFlags)captured);
        }

        [Test]
        public void Build_TogglingNoneOn_ClearsOtherBits()
        {
            var ed = new RuntimeEnumFlagsEditor();
            object captured = null;
            var ve = ed.Build(Param(typeof(SampleFlags), true, SampleFlags.X | SampleFlags.Y), v => captured = v);
            _host.rootVisualElement.Add(ve);

            SetToggleAndNotify(ve.Q<Toggle>("lp-flag-None"), true);
            Assert.AreEqual(SampleFlags.None, (SampleFlags)captured);
            Assert.IsFalse(ve.Q<Toggle>("lp-flag-X").value);
            Assert.IsFalse(ve.Q<Toggle>("lp-flag-Y").value);
        }

        [Test]
        public void Build_TogglingBitOn_AlsoClearsNoneToggle()
        {
            // None=true 初期から X を ON にすると None toggle は自動で OFF になる (UI と value の整合)。
            var ed = new RuntimeEnumFlagsEditor();
            var ve = ed.Build(Param(typeof(SampleFlags)), _ => { });
            _host.rootVisualElement.Add(ve);
            Assume.That(ve.Q<Toggle>("lp-flag-None").value, Is.True);

            SetToggleAndNotify(ve.Q<Toggle>("lp-flag-X"), true);
            Assert.IsFalse(ve.Q<Toggle>("lp-flag-None").value, "X を ON にしたら None も自動 OFF になるべき");
        }

        [Test]
        public void Build_TogglingAllBitsOff_RestoresNoneToggle()
        {
            // X | Y 初期から両方 OFF にすると current=0 になり、None toggle が自動で ON に戻る。
            var ed = new RuntimeEnumFlagsEditor();
            var ve = ed.Build(Param(typeof(SampleFlags), true, SampleFlags.X | SampleFlags.Y), _ => { });
            _host.rootVisualElement.Add(ve);

            SetToggleAndNotify(ve.Q<Toggle>("lp-flag-X"), false);
            SetToggleAndNotify(ve.Q<Toggle>("lp-flag-Y"), false);
            Assert.IsTrue(ve.Q<Toggle>("lp-flag-None").value, "全 bit OFF 後は None が自動 ON になるべき");
        }

        [Test]
        public void Build_TogglingNoneOff_IsIgnored()
        {
            // None を直接 OFF にしても無視され、Toggle は true に戻り onChanged も呼ばれない。
            var ed = new RuntimeEnumFlagsEditor();
            var fired = false;
            object captured = null;
            var ve = ed.Build(Param(typeof(SampleFlags)), v => { fired = true; captured = v; });
            _host.rootVisualElement.Add(ve);

            SetToggleAndNotify(ve.Q<Toggle>("lp-flag-None"), false);
            Assert.IsTrue(ve.Q<Toggle>("lp-flag-None").value, "None OFF は無視され true に戻るべき");
            Assert.IsFalse(fired, "None OFF では onChanged が呼ばれないべき");
            Assert.IsNull(captured);
        }
    }
}
