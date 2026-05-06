using System;
using UnityEngine.UIElements;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// プリミティブ型 (数値 / bool / string / char) に対する標準エディタ。
    /// Phase 1 の PrimitiveConverter が扱う型と同じ範囲をカバーする。
    /// </summary>
    public sealed class PrimitiveEditor : IParameterEditor
    {
        public bool CanHandle(Type type)
        {
            if (type == null) return false;
            if (type == typeof(string)) return true;
            if (type == typeof(bool)) return true;
            if (type == typeof(char)) return true;
            if (type == typeof(byte) || type == typeof(sbyte)) return true;
            if (type == typeof(short) || type == typeof(ushort)) return true;
            if (type == typeof(int) || type == typeof(uint)) return true;
            if (type == typeof(long) || type == typeof(ulong)) return true;
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return true;
            return false;
        }

        public VisualElement Build(ParameterDescriptor param, Action<object> onChanged)
        {
            var t = param.Type;

            // bool は Toggle。
            if (t == typeof(bool))
            {
                var f = new Toggle { value = param.HasDefault && (bool)param.DefaultValue };
                f.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return f;
            }

            // string は TextField。
            if (t == typeof(string))
            {
                var f = new TextField { value = param.HasDefault ? (string)param.DefaultValue ?? "" : "" };
                f.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return f;
            }

            // char は単文字制限の TextField。長さチェックを RegisterValueChangedCallback で実施。
            if (t == typeof(char))
            {
                var f = new TextField { value = param.HasDefault ? ((char)param.DefaultValue).ToString() : "", maxLength = 1 };
                f.RegisterValueChangedCallback(e =>
                {
                    if (string.IsNullOrEmpty(e.newValue)) onChanged('\0');
                    else onChanged(e.newValue[0]);
                });
                return f;
            }

            // 整数系は IntegerField / LongField。範囲チェックは型キャストで自然に弾かれる (overflow)。
            if (t == typeof(int))
            {
                var f = new IntegerField { value = param.HasDefault ? (int)param.DefaultValue : 0 };
                f.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return f;
            }
            if (t == typeof(long))
            {
                var f = new LongField { value = param.HasDefault ? (long)param.DefaultValue : 0L };
                f.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return f;
            }
            // それ以外の整数系は IntegerField + 型キャスト。
            // unsigned (uint / ulong / byte / ushort) や狭い範囲 (sbyte / short) は範囲外入力で
            // Convert.ChangeType が OverflowException を投げるため、try/catch で保護して
            // UI 上はエラー表示にとどめる (FallbackTextEditor と同じ流儀)。
            if (t == typeof(short) || t == typeof(ushort) ||
                t == typeof(byte) || t == typeof(sbyte) ||
                t == typeof(uint) || t == typeof(ulong))
            {
                var defaultValue = param.HasDefault ? Convert.ToInt32(param.DefaultValue) : 0;
                var f = new IntegerField { value = defaultValue };
                f.RegisterValueChangedCallback(e =>
                {
                    try
                    {
                        var converted = Convert.ChangeType(e.newValue, t);
                        f.RemoveFromClassList("lp-input-error");
                        f.tooltip = "";
                        onChanged(converted);
                    }
                    catch (Exception ex)
                    {
                        f.AddToClassList("lp-input-error");
                        f.tooltip = $"Out of range for {t.Name}: {ex.Message}";
                        // 変換失敗時は onChanged を呼ばず前の有効値を保持する。
                    }
                });
                return f;
            }

            // 浮動小数点系。
            if (t == typeof(float))
            {
                var f = new FloatField { value = param.HasDefault ? (float)param.DefaultValue : 0f };
                f.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return f;
            }
            if (t == typeof(double))
            {
                var f = new DoubleField { value = param.HasDefault ? (double)param.DefaultValue : 0.0 };
                f.RegisterValueChangedCallback(e => onChanged(e.newValue));
                return f;
            }
            if (t == typeof(decimal))
            {
                // decimal 用の専用フィールドは UI Toolkit にないので DoubleField で代替する。
                // 利用側で精度が問題になるケースは ITypeConverter / IParameterEditor を上書きすること。
                var defaultValue = param.HasDefault ? (double)(decimal)param.DefaultValue : 0.0;
                var f = new DoubleField { value = defaultValue };
                f.RegisterValueChangedCallback(e => onChanged((decimal)e.newValue));
                return f;
            }

            // ここには到達しないはず (CanHandle で弾かれている)。安全のため null 入りの TextField を返す。
            return new TextField();
        }
    }
}
