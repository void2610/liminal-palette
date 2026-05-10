using System;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// コマンドのパラメータに任意で付与し、UI / CLI 表示用のメタ情報および
    /// 数値型の範囲制約を補強する属性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
    public sealed class LiminalParamAttribute : Attribute
    {
        /// <summary>
        /// パラメータの説明文。
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// UI ドロップダウン用の候補値。Core では参考情報としてのみ保持し、検証はしない。
        /// </summary>
        public string[] Choices { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 許容する最小値 (含む)。数値型 (byte/sbyte/short/ushort/int/uint/long/ulong/float/double/decimal)
        /// および対応する <see cref="System.Nullable{T}"/> にのみ作用し、ArgumentBinder がバインド時に
        /// 下回ったらエラーを返す。未指定なら下限なし。
        /// float.NaN を Sentinel に使うため、Attribute named property のデフォルトを NaN に置く。
        /// </summary>
        public float Min { get; set; } = float.NaN;

        /// <summary>
        /// 許容する最大値 (含む)。Min と対称。未指定なら上限なし。
        /// 対象型は Min と同じ (数値型および対応する <see cref="System.Nullable{T}"/>)。
        /// </summary>
        public float Max { get; set; } = float.NaN;

        /// <summary>
        /// <see cref="IChoicesProvider"/> を実装した型。ランタイムで候補を動的に生成する。
        /// <see cref="Choices"/> (静的) と併用した場合、ChoicesProvider が優先される。
        /// Activator.CreateInstance で生成するため、パラメータレスコンストラクタが必要。
        /// </summary>
        public Type ChoicesProvider { get; set; }
    }
}
