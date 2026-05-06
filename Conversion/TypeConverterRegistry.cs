using System;
using System.Collections.Generic;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// ITypeConverter を集約し、targetType に対応する最適なコンバータを引くレジストリ。
    /// 後から Register したコンバータが優先される (利用側で組み込みを上書き可能)。
    /// </summary>
    public static class TypeConverterRegistry
    {
        // 先頭ほど優先度が高い (前から順に CanConvert を試行する)。
        private static readonly List<ITypeConverter> _converters = new List<ITypeConverter>();
        private static readonly object _lock = new object();

        static TypeConverterRegistry()
        {
            RegisterDefaults();
        }

        // 標準コンバータの登録。後から Register された方が優先されるため、
        // 一般性の低いコンバータをあとに登録するべきだが、ここでは型一致が排他的なので順序非依存。
        // cctor / ResetToDefaults の双方から共有される。
        private static void RegisterDefaults()
        {
            Register(new PrimitiveConverter());
            Register(new EnumConverter());
            Register(new VectorConverter());
            Register(new ColorConverter());
            Register(new UnityObjectConverter());
        }

        /// <summary>
        /// コンバータを登録する。新しく登録したものが既存より優先される。
        /// </summary>
        public static void Register(ITypeConverter converter)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            lock (_lock)
            {
                // 先頭挿入で「あとから登録 = 優先」を実現。
                _converters.Insert(0, converter);
            }
        }

        /// <summary>
        /// targetType を扱える最初のコンバータを返す。見つからなければ null。
        /// </summary>
        public static ITypeConverter Find(Type targetType)
        {
            if (targetType == null) return null;
            lock (_lock)
            {
                for (var i = 0; i < _converters.Count; i++)
                {
                    if (_converters[i].CanConvert(targetType)) return _converters[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 文字列から targetType への変換。失敗時は false + error を返し、例外は投げない。
        /// </summary>
        public static bool TryConvert(string raw, Type targetType, out object value, out string error)
        {
            value = null;
            error = null;

            var converter = Find(targetType);
            if (converter == null)
            {
                error = $"No converter registered for type {targetType?.Name ?? "<null>"}";
                return false;
            }
            return converter.TryFromString(raw, targetType, out value, out error);
        }

        /// <summary>表示用文字列。コンバータが見つからなければ ToString フォールバック。</summary>
        public static string ToDisplayString(object value)
        {
            if (value == null) return "";
            var converter = Find(value.GetType());
            return converter != null ? converter.ToDisplayString(value) : value.ToString();
        }

        /// <summary>
        /// 登録済みコンバータをすべて削除する (テスト向け)。
        /// 呼び出し後は再度 Register するか、cctor の再実行を期待しないこと。
        /// </summary>
        internal static void Clear()
        {
            lock (_lock)
            {
                _converters.Clear();
            }
        }

        /// <summary>
        /// 標準コンバータ 5 種だけが登録された初期状態にリセットする (テスト向け)。
        /// テスト間でユーザー追加コンバータが累積するのを防ぐ。
        /// </summary>
        internal static void ResetToDefaults()
        {
            Clear();
            RegisterDefaults();
        }
    }
}
