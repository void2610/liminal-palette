using System;
using System.Globalization;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// プリミティブ型 (数値 / bool / string / char) を扱う標準コンバータ。
    /// 文字列はパース不要のためそのまま返す。
    /// </summary>
    public sealed class PrimitiveConverter : ITypeConverter
    {
        public bool CanConvert(Type targetType)
        {
            if (targetType == null) return false;
            if (targetType == typeof(string)) return true;
            if (targetType == typeof(bool)) return true;
            if (targetType == typeof(char)) return true;
            if (targetType == typeof(byte) || targetType == typeof(sbyte)) return true;
            if (targetType == typeof(short) || targetType == typeof(ushort)) return true;
            if (targetType == typeof(int) || targetType == typeof(uint)) return true;
            if (targetType == typeof(long) || targetType == typeof(ulong)) return true;
            if (targetType == typeof(float) || targetType == typeof(double) || targetType == typeof(decimal)) return true;
            return false;
        }

        public bool TryFromString(string raw, Type targetType, out object value, out string error)
        {
            value = null;
            error = null;

            // string は変換不要。null も許容してそのまま渡す。
            if (targetType == typeof(string))
            {
                value = raw;
                return true;
            }

            if (raw == null)
            {
                error = $"Cannot convert null to {targetType.Name}";
                return false;
            }

            try
            {
                // bool は "true"/"false" を大文字小文字無視で受け付ける (TryParse の標準挙動)。
                if (targetType == typeof(bool))
                {
                    if (!bool.TryParse(raw, out var b))
                    {
                        error = $"Cannot parse '{raw}' as bool";
                        return false;
                    }
                    value = b;
                    return true;
                }

                if (targetType == typeof(char))
                {
                    if (raw.Length != 1)
                    {
                        error = $"Cannot parse '{raw}' as char (expected single character)";
                        return false;
                    }
                    value = raw[0];
                    return true;
                }

                // 数値系はカルチャ依存を避けるため InvariantCulture でパース。
                var ci = CultureInfo.InvariantCulture;
                if (targetType == typeof(byte)) { value = byte.Parse(raw, ci); return true; }
                if (targetType == typeof(sbyte)) { value = sbyte.Parse(raw, ci); return true; }
                if (targetType == typeof(short)) { value = short.Parse(raw, ci); return true; }
                if (targetType == typeof(ushort)) { value = ushort.Parse(raw, ci); return true; }
                if (targetType == typeof(int)) { value = int.Parse(raw, ci); return true; }
                if (targetType == typeof(uint)) { value = uint.Parse(raw, ci); return true; }
                if (targetType == typeof(long)) { value = long.Parse(raw, ci); return true; }
                if (targetType == typeof(ulong)) { value = ulong.Parse(raw, ci); return true; }
                if (targetType == typeof(float)) { value = float.Parse(raw, ci); return true; }
                if (targetType == typeof(double)) { value = double.Parse(raw, ci); return true; }
                if (targetType == typeof(decimal)) { value = decimal.Parse(raw, ci); return true; }

                error = $"Unsupported primitive type: {targetType.Name}";
                return false;
            }
            catch (Exception ex)
            {
                // 数値パース失敗時は具体的なエラーを利用側に返す。
                error = $"Cannot parse '{raw}' as {targetType.Name}: {ex.Message}";
                return false;
            }
        }

        public string ToDisplayString(object value)
        {
            if (value == null) return "";
            // 数値系は InvariantCulture で文字列化し、ロケール差を避ける。
            if (value is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
            return value.ToString();
        }
    }
}
