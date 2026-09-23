using System;
using UnityEngine.UIElements;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// enum 型に対する標準エディタ。Runtime / Editor 両方で動作する EnumField を使う。
    /// Flags 属性付き enum はビット和編集ができないため、Editor 側で EnumFlagsEditor を別途登録して上書きする。
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
            return field;
        }
    }
}
