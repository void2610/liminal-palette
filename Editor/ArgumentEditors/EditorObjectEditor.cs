using System;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Void2610.LiminalPalette.UI;
using Object = UnityEngine.Object;

namespace Void2610.LiminalPalette.Editor
{
    /// <summary>
    /// UnityEngine.Object 派生型用の Editor 専用エディタ (UnityEditor.UIElements.ObjectField を使用)。
    /// Runtime UI Toolkit にも ObjectField はあるが、ピッカーや SceneObjects 対応が Editor 版の方が確実なので Editor 限定とする。
    /// </summary>
    public sealed class EditorObjectEditor : IParameterEditor
    {
        public bool CanHandle(Type type) => type != null && typeof(Object).IsAssignableFrom(type);

        public VisualElement Build(ParameterDescriptor param, Action<object> onChanged)
        {
            var f = new ObjectField
            {
                objectType = param.Type,
                allowSceneObjects = true,
                value = param.HasDefault ? (Object)param.DefaultValue : null,
            };
            f.RegisterValueChangedCallback(e => onChanged(e.newValue));
            return f;
        }
    }
}
