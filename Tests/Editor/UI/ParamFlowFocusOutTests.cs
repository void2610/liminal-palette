using NUnit.Framework;
using UnityEngine.UIElements;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Tests.UI
{
    /// <summary>
    /// 引数入力フローの blur 確定フックの対象判定テスト。
    /// ドロップダウンを開くエディタまでフックすると、選択する前に次のステップへ進んでしまう
    /// (enum 引数が既定値でしか実行できなくなる) ため、対象をテキスト入力欄に限る。
    /// </summary>
    public sealed class ParamFlowFocusOutTests
    {
        [Test]
        public void TextInputFields_AreHooked()
        {
            Assert.IsTrue(PaletteView.IsTextInputField(new TextField()));
            Assert.IsTrue(PaletteView.IsTextInputField(new IntegerField()));
            Assert.IsTrue(PaletteView.IsTextInputField(new FloatField()));
        }

        [Test]
        public void DropdownAndToggleFields_AreNotHooked()
        {
            // EnumField はドロップダウンをパネル外のレイヤーに開き、フィールド自体は blur する。
            Assert.IsFalse(PaletteView.IsTextInputField(new EnumField(TestFlow.A)));
            Assert.IsFalse(PaletteView.IsTextInputField(new Toggle()));
            Assert.IsFalse(PaletteView.IsTextInputField(new VisualElement()));
        }

        [Test]
        public void Null_IsNotHooked()
        {
            Assert.IsFalse(PaletteView.IsTextInputField(null));
        }

        private enum TestFlow { A, B }
    }
}
