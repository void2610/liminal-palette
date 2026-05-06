using System;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Editor
{
    /// <summary>
    /// [Flags] 属性付き enum 用の Editor 専用エディタ (UnityEditor.UIElements.EnumFlagsField を使用)。
    /// UI 側の EnumEditor よりも具体的 (Flags 限定) なため、後から登録して上書き優先にする。
    /// </summary>
    public sealed class EditorEnumFlagsEditor : IParameterEditor
    {
        public bool CanHandle(Type type)
            => type != null && type.IsEnum && type.IsDefined(typeof(FlagsAttribute), inherit: false);

        public VisualElement Build(ParameterDescriptor param, Action<object> onChanged)
        {
            var defaultEnum = param.HasDefault
                ? (Enum)param.DefaultValue
                : (Enum)Enum.GetValues(param.Type).GetValue(0);

            var field = new EnumFlagsField(defaultEnum);
            field.RegisterValueChangedCallback(e => onChanged(e.newValue));
            return field;
        }
    }
}
