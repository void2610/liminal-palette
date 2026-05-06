using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Editor
{
    /// <summary>
    /// Color / Color32 用の Editor 専用エディタ (UnityEditor.UIElements.ColorField を使用)。
    /// Runtime UI Toolkit には ColorField がないため Editor asmdef に閉じ込める。
    /// </summary>
    public sealed class EditorColorEditor : IParameterEditor
    {
        public bool CanHandle(Type type) => type == typeof(Color) || type == typeof(Color32);

        public VisualElement Build(ParameterDescriptor param, Action<object> onChanged)
        {
            var t = param.Type;

            if (t == typeof(Color))
            {
                var f = new ColorField { value = param.HasDefault ? (Color)param.DefaultValue : Color.white };
                f.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return f;
            }

            // Color32 は ColorField を流用しつつコールバック側でキャストする。
            var defaultColor32 = param.HasDefault ? (Color32)param.DefaultValue : new Color32(255, 255, 255, 255);
            var field = new ColorField { value = (Color)defaultColor32 };
            field.RegisterValueChangedCallback(e => onChanged((Color32)e.newValue));
            return field;
        }
    }
}
