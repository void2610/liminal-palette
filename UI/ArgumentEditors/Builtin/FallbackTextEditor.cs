using System;
using UnityEngine.UIElements;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// 他のエディタが扱えない型に対する最後の保険。
    /// TextField の入力値を Phase 1 の TypeConverterRegistry に通して変換する。
    /// 変換に失敗した間は入力欄に "lp-input-error" クラスを付け、tooltip にエラーを表示する。
    /// 利用側は ITypeConverter を登録するだけで UI を追加しなくても任意型のコマンドが動かせる。
    /// </summary>
    public sealed class FallbackTextEditor : IParameterEditor
    {
        // CSS クラス名は USS 側 (PaletteVariables.uss) と合わせる必要がある。
        private const string ErrorClass = "lp-input-error";

        // 全型を受け入れる。最低優先度として登録される (ParameterEditorRegistry.RegisterDefaults を参照)。
        public bool CanHandle(Type type) => type != null;

        public VisualElement Build(ParameterDescriptor param, Action<object> onChanged)
        {
            // 初期値: HasDefault があれば TypeConverterRegistry で表示文字列化、なければ空文字。
            var initialText = "";
            if (param.HasDefault && param.DefaultValue != null)
            {
                initialText = TypeConverterRegistry.ToDisplayString(param.DefaultValue);
            }

            var field = new TextField { value = initialText };

            // 初期値が変換可能なら最初の onChanged を発火しておく (空入力で必須エラーにならないため)。
            if (param.HasDefault && param.DefaultValue != null)
            {
                onChanged(param.DefaultValue);
            }

            field.RegisterValueChangedCallback(e =>
            {
                if (TypeConverterRegistry.TryConvert(e.newValue, param.Type, out var v, out var err))
                {
                    field.RemoveFromClassList(ErrorClass);
                    field.tooltip = "";
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
