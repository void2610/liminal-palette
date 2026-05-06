using System;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// UnityEngine.Object 派生型用の Runtime エディタ。
    /// UnityEditor.UIElements.ObjectField は Editor 専用なので、Runtime では TextField + UnityObjectConverter で代替する。
    /// 入力形式は UnityObjectConverter のサポートに準じる: "@&lt;entityID&gt;" / "GameObject:&lt;name&gt;" / 空 (null)。
    /// 変換失敗時は赤縁 (lp-input-error クラス) と tooltip でエラー表示し、onChanged は呼ばない (FallbackTextEditor と同じ流儀)。
    /// </summary>
    public sealed class RuntimeObjectEditor : IParameterEditor
    {
        // CSS クラス名は USS 側 (PaletteStyles.uss) と合わせる必要がある。
        private const string ErrorClass = "lp-input-error";

        public bool CanHandle(Type type) => type != null && typeof(Object).IsAssignableFrom(type);

        public VisualElement Build(ParameterDescriptor param, Action<object> onChanged)
        {
            // 初期値の解決。
            // CLR の `!= null` では Unity のいわゆる fake null (== null だが CLR null ではない destroyed Object) を
            // 検知できないため、UnityEngine.Object にキャストしてから Unity の `==` 演算子で判定する。
            // これで destroyed default を空表示 + onChanged 不発火に倒せる。
            var defaultObj = param.HasDefault ? param.DefaultValue as Object : null;
            var hasValidDefault = defaultObj != null;

            var initialText = hasValidDefault
                ? TypeConverterRegistry.ToDisplayString(defaultObj)
                : "";

            var field = new TextField { value = initialText };
            field.AddToClassList("lp-object-runtime");
            // ピッカーが無いことを明示するためのプレースホルダ的 tooltip。
            field.tooltip = "Enter '@<entityID>' or 'GameObject:<name>'. Empty for null.";

            // 初期値が valid (生きた Object) なら最初の onChanged を発火 (FallbackTextEditor と同じ)。
            if (hasValidDefault)
            {
                onChanged(defaultObj);
            }

            field.RegisterValueChangedCallback(e =>
            {
                if (TypeConverterRegistry.TryConvert(e.newValue, param.Type, out var v, out var err))
                {
                    field.RemoveFromClassList(ErrorClass);
                    field.tooltip = "Enter '@<entityID>' or 'GameObject:<name>'. Empty for null.";
                    onChanged(v);
                }
                else
                {
                    field.AddToClassList(ErrorClass);
                    field.tooltip = err ?? "Invalid input";
                    // 変換失敗時は onChanged を呼ばない (前の有効値を保持)。
                }
            });
            return field;
        }
    }
}
