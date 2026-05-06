using System;
using System.Globalization;
using UnityEngine;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// Vector2 / Vector3 / Vector4 のコンバータ。
    /// "1,2,3" / "(1, 2, 3)" / "[1 2 3]" など、区切り文字とカッコの組合せに寛容。
    /// </summary>
    public sealed class VectorConverter : ITypeConverter
    {
        public bool CanConvert(Type targetType) => targetType == typeof(Vector2) || targetType == typeof(Vector3) || targetType == typeof(Vector4) || targetType == typeof(Vector2Int) || targetType == typeof(Vector3Int);

        public string ToDisplayString(object value) => value == null ? "" : value.ToString();

        public bool TryFromString(string raw, Type targetType, out object value, out string error)
        {
            value = null;
            error = null;

            if (raw == null)
            {
                error = $"Cannot convert null to {targetType.Name}";
                return false;
            }

            // 期待コンポーネント数を型から決定。Vector2/2Int は 2、Vector3/3Int は 3、Vector4 は 4。
            var expected = ExpectedComponents(targetType);
            if (!TryParseComponents(raw, expected, out var fs, out var ferr))
            {
                error = $"Cannot parse '{raw}' as {targetType.Name}: {ferr}";
                return false;
            }

            if (targetType == typeof(Vector2)) value = new Vector2(fs[0], fs[1]);
            else if (targetType == typeof(Vector3)) value = new Vector3(fs[0], fs[1], fs[2]);
            else if (targetType == typeof(Vector4)) value = new Vector4(fs[0], fs[1], fs[2], fs[3]);
            else if (targetType == typeof(Vector2Int)) value = new Vector2Int((int)fs[0], (int)fs[1]);
            else if (targetType == typeof(Vector3Int)) value = new Vector3Int((int)fs[0], (int)fs[1], (int)fs[2]);
            else
            {
                error = $"Unsupported vector type: {targetType.Name}";
                return false;
            }
            return true;
        }

        private static int ExpectedComponents(Type t)
        {
            if (t == typeof(Vector2) || t == typeof(Vector2Int)) return 2;
            if (t == typeof(Vector3) || t == typeof(Vector3Int)) return 3;
            if (t == typeof(Vector4)) return 4;
            return 0;
        }

        // カッコと空白を取り除いて "," または空白で分割し、float に変換する。
        private static bool TryParseComponents(string raw, int expected, out float[] result, out string error)
        {
            result = null;
            error = null;

            // 先頭末尾のカッコ類を除去。中間の余白も後段の Split で吸収する。
            var trimmed = raw.Trim().Trim('(', ')', '[', ']', '{', '}');
            // ", " / " " 区切りどちらでも受け付ける。連続区切りは無視。
            var parts = trimmed.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != expected)
            {
                error = $"expected {expected} components, got {parts.Length}";
                return false;
            }

            var ci = CultureInfo.InvariantCulture;
            var values = new float[expected];
            for (var i = 0; i < expected; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, ci, out values[i]))
                {
                    error = $"component {i} is not a number ('{parts[i]}')";
                    return false;
                }
            }
            result = values;
            return true;
        }
    }
}
