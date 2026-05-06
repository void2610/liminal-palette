using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// Color / Color32 用の Runtime UI Toolkit エディタ。
    /// UnityEditor.UIElements.ColorField は Editor 専用なので、Runtime では Slider 4 本 (R/G/B/A) と
    /// プレビュー用 VisualElement で代替する。Editor 側ではより操作性の良い ColorField を後から登録して上書きする。
    /// </summary>
    public sealed class RuntimeColorEditor : IParameterEditor
    {
        public bool CanHandle(Type type) => type == typeof(Color) || type == typeof(Color32);

        public VisualElement Build(ParameterDescriptor param, Action<object> onChanged)
        {
            var t = param.Type;
            var initial = ResolveInitial(param);

            // 縦方向に「プレビュー + 4 スライダ」を並べる。テストで子要素数を検証するため構造を固定。
            var root = new VisualElement();
            root.AddToClassList("lp-color-runtime");
            root.style.flexDirection = FlexDirection.Column;

            var preview = new VisualElement();
            preview.AddToClassList("lp-color-runtime-preview");
            preview.style.height = 16;
            preview.style.marginBottom = 4;
            preview.style.backgroundColor = initial;
            root.Add(preview);

            var current = initial;

            var rSlider = BuildSlider("R", initial.r, v =>
            {
                current.r = v;
                preview.style.backgroundColor = current;
                Emit(t, current, onChanged);
            });
            var gSlider = BuildSlider("G", initial.g, v =>
            {
                current.g = v;
                preview.style.backgroundColor = current;
                Emit(t, current, onChanged);
            });
            var bSlider = BuildSlider("B", initial.b, v =>
            {
                current.b = v;
                preview.style.backgroundColor = current;
                Emit(t, current, onChanged);
            });
            var aSlider = BuildSlider("A", initial.a, v =>
            {
                current.a = v;
                preview.style.backgroundColor = current;
                Emit(t, current, onChanged);
            });

            root.Add(rSlider);
            root.Add(gSlider);
            root.Add(bSlider);
            root.Add(aSlider);

            return root;
        }

        // 初期値の解決。HasDefault があれば DefaultValue を Color に正規化、なければ白。
        private static Color ResolveInitial(ParameterDescriptor param)
        {
            if (!param.HasDefault) return Color.white;
            if (param.DefaultValue is Color c) return c;
            if (param.DefaultValue is Color32 c32) return c32;
            return Color.white;
        }

        // Slider と値ラベルを横並びにした 1 行コンポーネントを返す。
        private static VisualElement BuildSlider(string label, float initial, Action<float> onChanged)
        {
            var row = new VisualElement();
            row.AddToClassList("lp-color-runtime-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var lbl = new Label(label) { name = $"lp-color-{label.ToLowerInvariant()}-label" };
            lbl.style.minWidth = 16;
            row.Add(lbl);

            var slider = new Slider(0f, 1f) { name = $"lp-color-{label.ToLowerInvariant()}-slider", value = Mathf.Clamp01(initial) };
            slider.style.flexGrow = 1;
            slider.RegisterValueChangedCallback(e => onChanged(Mathf.Clamp01(e.newValue)));
            row.Add(slider);

            return row;
        }

        // Color32 が要求されているならキャストして渡す。
        private static void Emit(Type targetType, Color current, Action<object> onChanged)
        {
            if (targetType == typeof(Color32)) onChanged((Color32)current);
            else onChanged(current);
        }
    }
}
