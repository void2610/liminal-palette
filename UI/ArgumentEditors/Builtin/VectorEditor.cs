using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// Vector2 / Vector3 / Vector4 / Vector2Int / Vector3Int に対する標準エディタ。
    /// Phase 1 の VectorConverter と対応する型を網羅する。
    /// </summary>
    public sealed class VectorEditor : IParameterEditor
    {
        public bool CanHandle(Type type)
            => type == typeof(Vector2)
            || type == typeof(Vector3)
            || type == typeof(Vector4)
            || type == typeof(Vector2Int)
            || type == typeof(Vector3Int);

        public VisualElement Build(ParameterDescriptor param, Action<object> onChanged)
        {
            var t = param.Type;

            if (t == typeof(Vector2))
            {
                var f = new Vector2Field { value = param.HasDefault ? (Vector2)param.DefaultValue : Vector2.zero };
                f.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return f;
            }
            if (t == typeof(Vector3))
            {
                var f = new Vector3Field { value = param.HasDefault ? (Vector3)param.DefaultValue : Vector3.zero };
                f.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return f;
            }
            if (t == typeof(Vector4))
            {
                var f = new Vector4Field { value = param.HasDefault ? (Vector4)param.DefaultValue : Vector4.zero };
                f.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return f;
            }
            if (t == typeof(Vector2Int))
            {
                var f = new Vector2IntField { value = param.HasDefault ? (Vector2Int)param.DefaultValue : Vector2Int.zero };
                f.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return f;
            }
            if (t == typeof(Vector3Int))
            {
                var f = new Vector3IntField { value = param.HasDefault ? (Vector3Int)param.DefaultValue : Vector3Int.zero };
                f.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return f;
            }

            // CanHandle で弾かれているため到達しない想定。
            return new TextField();
        }
    }
}
