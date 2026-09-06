using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Tests.UI
{
    public sealed class AutoCompleteEditorTests
    {
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

        private VisualElement Build(bool dynamicChoices, Action<object> onChanged)
        {
            var choices = new[] { "option-a", "option-b", "option-c" };
            var param = new ParameterDescriptor(
                "p", typeof(string), 0, false, null, "", choices,
                dynamicChoices: dynamicChoices
                    ? () => new[]
                    {
                        new ChoiceItem("option-a", "候補A"),
                        new ChoiceItem("option-b", "候補B"),
                        new ChoiceItem("option-c", "候補C")
                    }
                    : null);
            var root = ParameterEditorRegistry.Resolve(param).Build(param, onChanged);
            _host.rootVisualElement.Add(root);
            root.Q<TextField>().value = "option";
            return root;
        }

        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 2)]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(true, 2)]
        public void ClickSuggestion_ThenComplete_PreservesClickedValue(bool dynamicChoices, int index)
        {
            object captured = null;
            var changes = 0;
            var root = Build(dynamicChoices, value =>
            {
                captured = value;
                changes++;
            });
            var field = root.Q<TextField>();
            var list = root.Q<ScrollView>();
            var tryComplete = (Func<bool>)root.userData;
            Assert.AreEqual(3, list.childCount);
            Assert.AreEqual(DisplayStyle.Flex, list.style.display.value);

            var label = list[index];
            using (var evt = MouseDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0 }))
            {
                evt.target = label;
                label.SendEvent(evt);
            }

            var expected = new[] { "option-a", "option-b", "option-c" }[index];
            Assert.AreEqual(expected, field.value);
            Assert.AreEqual(expected, captured);
            Assert.AreEqual(DisplayStyle.None, list.style.display.value);
            var changesAfterClick = changes;
            Assert.IsFalse(tryComplete());
            Assert.AreEqual(expected, field.value);
            Assert.AreEqual(expected, captured);
            Assert.AreEqual(changesAfterClick, changes);

            field.value = "option";
            Assert.IsTrue(tryComplete());
            Assert.AreEqual("option-a", captured);
        }

        [TestCase(false, "option", "option-a")]
        [TestCase(true, "option", "option-a")]
        [TestCase(false, "option-b", "option-b")]
        [TestCase(true, "候補B", "option-b")]
        public void Complete_VisibleSuggestions_SelectsFirstMatch(bool dynamicChoices, string filter, string expected)
        {
            object captured = null;
            var root = Build(dynamicChoices, value => captured = value);
            var field = root.Q<TextField>();
            field.value = filter;
            var tryComplete = (Func<bool>)root.userData;

            Assert.IsTrue(tryComplete());
            Assert.AreEqual(expected, field.value);
            Assert.AreEqual(expected, captured);
            Assert.AreEqual(DisplayStyle.None, root.Q<ScrollView>().style.display.value);
            Assert.IsFalse(tryComplete());
        }
    }
}
