using System;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// enum 型のコンバータ。値名と数値文字列の両方を受け付ける。
    /// </summary>
    public sealed class EnumConverter : ITypeConverter
    {
        public bool CanConvert(Type targetType) => targetType != null && targetType.IsEnum;

        public string ToDisplayString(object value) => value == null ? "" : value.ToString();

        public bool TryFromString(string raw, Type targetType, out object value, out string error)
        {
            value = null;
            error = null;

            if (raw == null)
            {
                error = $"Cannot convert null to enum {targetType.Name}";
                return false;
            }

            // Enum.TryParse は大文字小文字無視で名前 / 数値文字列の両方を解決する。
            try
            {
                value = Enum.Parse(targetType, raw, ignoreCase: true);
                // 数値文字列で IsDefined を満たさない値も許容するかは議論の余地があるが、
                // FlagsAttribute や将来追加された値の組合せを通すため検証は厳格化しない。
                return true;
            }
            catch (Exception ex)
            {
                error = $"Cannot parse '{raw}' as enum {targetType.Name}: {ex.Message}";
                return false;
            }
        }
    }
}
