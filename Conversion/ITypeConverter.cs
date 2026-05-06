using System;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// 文字列 ↔ オブジェクトの相互変換を担う拡張ポイント。
    /// UI 入力 / CLI 引数 / テストの全てで同じインタフェースを通すことで、
    /// 入力経路を増やしても型解釈が分岐しないようにしている。
    /// </summary>
    public interface ITypeConverter
    {
        /// <summary>このコンバータが targetType を扱えるなら true。</summary>
        bool CanConvert(Type targetType);

        /// <summary>
        /// 文字列を targetType のインスタンスに変換する。
        /// 失敗時は false を返し、人間可読な error 文字列を出力する (例外は投げない)。
        /// </summary>
        bool TryFromString(string raw, Type targetType, out object value, out string error);

        /// <summary>UI / ログ表示用の文字列化。可能なら ToString と等価でよい。</summary>
        string ToDisplayString(object value);
    }
}
