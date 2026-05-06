using System;
using System.Collections.Generic;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// ParameterDescriptor 列と入力 (名前指定 / 位置指定) を受け取り、
    /// MethodInfo.Invoke に渡せる object[] にバインドするヘルパ。
    /// </summary>
    internal static class ArgumentBinder
    {
        /// <summary>
        /// 名前指定束縛。namedArgs はパラメータ名 (大文字小文字無視) → 文字列値。
        /// 該当キーがなければデフォルト値を使用、デフォルトもなければエラー。
        /// </summary>
        public static bool TryBind(
            IReadOnlyList<ParameterDescriptor> parameters,
            IReadOnlyDictionary<string, string> namedArgs,
            out object[] bound,
            out string error)
        {
            bound = null;
            error = null;

            // 名前一致を大文字小文字無視で行うため、内部で OrdinalIgnoreCase Dictionary に詰め替える。
            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (namedArgs != null)
            {
                foreach (var kv in namedArgs)
                {
                    if (kv.Key != null) lookup[kv.Key] = kv.Value;
                }
            }

            var result = new object[parameters.Count];
            for (var i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i];
                if (lookup.TryGetValue(p.Name, out var raw))
                {
                    if (!TypeConverterRegistry.TryConvert(raw, p.Type, out var v, out var convError))
                    {
                        error = $"parameter '{p.Name}' (type {p.Type.Name}): {convError}";
                        return false;
                    }
                    if (!TryValidateRange(p, v, out error)) return false;
                    result[i] = v;
                }
                else if (p.HasDefault)
                {
                    result[i] = p.DefaultValue;
                }
                else
                {
                    error = $"missing required parameter '{p.Name}' (type {p.Type.Name})";
                    return false;
                }
            }

            bound = result;
            return true;
        }

        /// <summary>
        /// 位置指定束縛。positional[i] が parameters[i] に対応する。
        /// 短い場合はデフォルト値で埋め、長い場合はエラーとする。
        /// </summary>
        public static bool TryBind(
            IReadOnlyList<ParameterDescriptor> parameters,
            IReadOnlyList<string> positional,
            out object[] bound,
            out string error)
        {
            bound = null;
            error = null;

            var supplied = positional?.Count ?? 0;
            if (supplied > parameters.Count)
            {
                error = $"too many positional args: expected at most {parameters.Count}, got {supplied}";
                return false;
            }

            var result = new object[parameters.Count];
            for (var i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i];
                if (i < supplied)
                {
                    var raw = positional[i];
                    if (!TypeConverterRegistry.TryConvert(raw, p.Type, out var v, out var convError))
                    {
                        error = $"parameter '{p.Name}' (position {i}, type {p.Type.Name}): {convError}";
                        return false;
                    }
                    if (!TryValidateRange(p, v, out error)) return false;
                    result[i] = v;
                }
                else if (p.HasDefault)
                {
                    result[i] = p.DefaultValue;
                }
                else
                {
                    error = $"missing required parameter '{p.Name}' (position {i}, type {p.Type.Name})";
                    return false;
                }
            }

            bound = result;
            return true;
        }

        /// <summary>
        /// 型解決済みの値で名前指定束縛。文字列変換を介さず、object をそのまま MethodInfo.Invoke に渡せる形に詰める。
        /// UI から Vector3 / Color などの構造体値をそのまま渡したいケースで使う (string を介すと精度劣化や変換往復のコストが発生するため)。
        /// </summary>
        public static bool TryBindTyped(
            IReadOnlyList<ParameterDescriptor> parameters,
            IReadOnlyDictionary<string, object> namedArgs,
            out object[] bound,
            out string error)
        {
            bound = null;
            error = null;

            // 名前一致を大文字小文字無視で行うため、内部で OrdinalIgnoreCase Dictionary に詰め替える。
            var lookup = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (namedArgs != null)
            {
                foreach (var kv in namedArgs)
                {
                    if (kv.Key != null) lookup[kv.Key] = kv.Value;
                }
            }

            var result = new object[parameters.Count];
            for (var i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i];
                if (lookup.TryGetValue(p.Name, out var value))
                {
                    if (value == null)
                    {
                        // 値型 (struct) は null 不可。enum / 数値 / Vector / Color などはここで弾く。
                        // Nullable<T> は IsValueType も true を返すが、Nullable.GetUnderlyingType で判定して許容する。
                        if (p.Type.IsValueType && Nullable.GetUnderlyingType(p.Type) == null)
                        {
                            error = $"parameter '{p.Name}' (type {p.Type.Name}): null is not assignable to value type";
                            return false;
                        }
                        result[i] = null;
                    }
                    else
                    {
                        // Nullable<T> 対応: int? に boxed int (ボクシング後の実体型 int) を渡された場合、
                        // p.Type.IsInstanceOfType(value) は false を返すため、underlying T で判定する。
                        // Range 検証 (TryValidateRange / IsNumeric) も Nullable<T> をサポートしており、整合する。
                        var targetType = Nullable.GetUnderlyingType(p.Type) ?? p.Type;
                        if (!targetType.IsInstanceOfType(value))
                        {
                            error = $"parameter '{p.Name}' (type {p.Type.Name}): value of type {value.GetType().Name} is not assignable";
                            return false;
                        }
                        result[i] = value;
                    }
                    if (!TryValidateRange(p, result[i], out error)) return false;
                }
                else if (p.HasDefault)
                {
                    result[i] = p.DefaultValue;
                }
                else
                {
                    error = $"missing required parameter '{p.Name}' (type {p.Type.Name})";
                    return false;
                }
            }

            bound = result;
            return true;
        }

        // ConsoleParamAttribute.Min/Max を ParameterDescriptor 経由で受け取り、数値型の範囲外なら error を返す。
        // float.NaN は「未指定」の Sentinel (属性側と同じ規約)。
        // 非数値型 (string / enum / Vector / Color など) は Min/Max を持っていても黙って通す
        // (定義側の利用ミスを ArgumentBinder で握る筋ではないため; UI ヒントとして残しておく方針)。
        private static bool TryValidateRange(ParameterDescriptor p, object value, out string error)
        {
            error = null;
            if (value == null) return true;
            var hasMin = !float.IsNaN(p.Min);
            var hasMax = !float.IsNaN(p.Max);
            if (!hasMin && !hasMax) return true;
            if (!IsNumeric(p.Type)) return true;

            float f;
            try
            {
                f = Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                // Convert で落ちる型は範囲検証対象外として通す。
                return true;
            }

            // FormattableString.Invariant で interpolation のロケール依存を排除する。
            // 既定の $"..." は CurrentCulture で format するため、de-DE 等で小数点が ',' になり
            // HTTP API / UI 表示の再現性が下がる。
            if (hasMin && f < p.Min)
            {
                error = FormattableString.Invariant($"parameter '{p.Name}': value {f} is less than min {p.Min}");
                return false;
            }
            if (hasMax && f > p.Max)
            {
                error = FormattableString.Invariant($"parameter '{p.Name}': value {f} is greater than max {p.Max}");
                return false;
            }
            return true;
        }

        // Min/Max 検証対象の数値型 (Nullable<T> も再帰で許容)。
        private static bool IsNumeric(Type t)
        {
            if (t == null) return false;
            var underlying = Nullable.GetUnderlyingType(t);
            if (underlying != null) return IsNumeric(underlying);
            return t == typeof(byte) || t == typeof(sbyte) ||
                   t == typeof(short) || t == typeof(ushort) ||
                   t == typeof(int) || t == typeof(uint) ||
                   t == typeof(long) || t == typeof(ulong) ||
                   t == typeof(float) || t == typeof(double) || t == typeof(decimal);
        }
    }
}
