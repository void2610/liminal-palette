using System;
using System.Globalization;
using UnityEngine;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// Color / Color32 のコンバータ。
    /// "#RRGGBB" / "#RRGGBBAA" の HEX 表記、および "r,g,b" / "r,g,b,a" の数値表記をサポート。
    /// 数値表記は Color では 0..1、Color32 では 0..255 を期待する。
    /// </summary>
    public sealed class ColorConverter : ITypeConverter
    {
        public bool CanConvert(Type targetType) => targetType == typeof(Color) || targetType == typeof(Color32);

        public bool TryFromString(string raw, Type targetType, out object value, out string error)
        {
            value = null;
            error = null;

            if (raw == null)
            {
                error = $"Cannot convert null to {targetType.Name}";
                return false;
            }

            var s = raw.Trim();

            // HEX 表記。Unity の ColorUtility.TryParseHtmlString は "#RGB" "#RRGGBB" "RRGGBB" "red" などを受け付ける。
            if (s.StartsWith("#"))
            {
                if (!ColorUtility.TryParseHtmlString(s, out var c))
                {
                    error = $"Cannot parse '{raw}' as color (invalid hex)";
                    return false;
                }
                value = targetType == typeof(Color32) ? (object)(Color32)c : c;
                return true;
            }

            // 数値表記。"r,g,b" または "r,g,b,a"。
            var trimmed = s.Trim('(', ')', '[', ']', '{', '}');
            var parts = trimmed.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 && parts.Length != 4)
            {
                error = $"Cannot parse '{raw}' as {targetType.Name} (expected 3 or 4 components, got {parts.Length})";
                return false;
            }

            var ci = CultureInfo.InvariantCulture;
            var nums = new float[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, ci, out nums[i]))
                {
                    error = $"Cannot parse component {i} of '{raw}' as number";
                    return false;
                }
            }

            if (targetType == typeof(Color))
            {
                // Color は 0..1 範囲を期待。a が省略されたら 1。
                var a = parts.Length == 4 ? nums[3] : 1f;
                value = new Color(nums[0], nums[1], nums[2], a);
                return true;
            }

            // Color32 は 0..255 範囲を期待。クランプして byte に詰める。
            byte ToByte(float f) => (byte)Mathf.Clamp(Mathf.RoundToInt(f), 0, 255);
            var ab = parts.Length == 4 ? ToByte(nums[3]) : (byte)255;
            value = new Color32(ToByte(nums[0]), ToByte(nums[1]), ToByte(nums[2]), ab);
            return true;
        }

        public string ToDisplayString(object value)
        {
            switch (value)
            {
                case Color c: return $"#{ColorUtility.ToHtmlStringRGBA(c)}";
                case Color32 c32: return $"#{ColorUtility.ToHtmlStringRGBA(c32)}";
                default: return value == null ? "" : value.ToString();
            }
        }
    }
}
