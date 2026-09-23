using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// enum 型に対する標準エディタ。Runtime / Editor 両方で動作する EnumField を使う。
    /// Flags 属性付き enum はビット和編集ができないため、Editor 側で EnumFlagsEditor を別途登録して上書きする。
    ///
    /// ドロップダウンはマウスでしか開けない (パレットは Enter を「次へ / 実行」に使っており、
    /// NavigationSubmit / NavigationMove も root で握り潰している) ため、
    /// ↑↓ で値を巡回できるようにしてキーボードだけで完結させる。
    /// </summary>
    public sealed class EnumEditor : IParameterEditor
    {
        public bool CanHandle(Type type) => type != null && type.IsEnum;

        public VisualElement Build(ParameterDescriptor param, Action<object> onChanged)
        {
            // 初期値: HasDefault → DefaultValue、そうでなければ enum の最初の値。
            var defaultEnum = param.HasDefault
                ? (Enum)param.DefaultValue
                : (Enum)Enum.GetValues(param.Type).GetValue(0);

            var field = new EnumField(defaultEnum);
            field.RegisterValueChangedCallback(e => onChanged(e.newValue));
            AttachKeyboardCycling(field, param.Type);
            return field;
        }

        /// <summary>↑↓ で値を巡回させる。ドロップダウンを開かずに変更できるようにするため。</summary>
        internal static void AttachKeyboardCycling(EnumField field, Type enumType)
        {
            var values = Enum.GetValues(enumType);
            if (values.Length == 0) return;

            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                int delta;
                switch (evt.keyCode)
                {
                    case KeyCode.UpArrow:
                        delta = -1;
                        break;
                    case KeyCode.DownArrow:
                        delta = 1;
                        break;
                    default:
                        return;
                }

                var current = IndexOf(values, field.value);
                // 端で止めず巡回させる (選択肢が少ないので行き止まりより回る方が速い)
                var next = ((current + delta) % values.Length + values.Length) % values.Length;
                field.value = (Enum)values.GetValue(next);

                // パレット側の選択移動に食われないよう、ここで止める。
                evt.StopImmediatePropagation();
                evt.PreventDefault();
            });
        }

        // 現在値の位置。見つからなければ先頭扱い。
        private static int IndexOf(Array values, Enum current)
        {
            if (current == null) return 0;
            for (var i = 0; i < values.Length; i++)
            {
                if (Equals(values.GetValue(i), current)) return i;
            }
            return 0;
        }
    }
}
