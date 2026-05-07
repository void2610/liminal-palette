using System;
using System.Collections.Generic;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// コマンドの 1 引数を表す不変メタデータ。
    /// MethodInfo の ParameterInfo から AttributeScanner が組み立てる。
    /// </summary>
    public sealed class ParameterDescriptor
    {
        /// <summary>パラメータ名 (C# 上の引数名と同一)。</summary>
        public string Name { get; }

        /// <summary>パラメータの C# 型。TypeConverter による変換対象。</summary>
        public Type Type { get; }

        /// <summary>引数の宣言位置 (0 始まり)。位置指定束縛で使用。</summary>
        public int Position { get; }

        /// <summary>デフォルト値を持つかどうか。持つ場合のみ DefaultValue が有効。</summary>
        public bool HasDefault { get; }

        /// <summary>デフォルト値。HasDefault が false の場合は null。</summary>
        public object DefaultValue { get; }

        /// <summary>UI / CLI 表示用の説明 (ConsoleParamAttribute から)。</summary>
        public string Description { get; }

        /// <summary>UI ドロップダウン候補 (ConsoleParamAttribute から)。空配列で「候補なし」。</summary>
        public IReadOnlyList<string> Choices { get; }

        /// <summary>
        /// 下限値 (含む)。float.NaN を「未指定」の Sentinel として扱う (ConsoleParamAttribute と同じ規約)。
        /// </summary>
        public float Min { get; }

        /// <summary>
        /// 上限値 (含む)。float.NaN を「未指定」の Sentinel として扱う (ConsoleParamAttribute と同じ規約)。
        /// </summary>
        public float Max { get; }

        /// <summary>
        /// 動的候補を返すファクトリ。nullなら静的Choicesを使う。
        /// 呼び出しのたびに最新の候補を返す。
        /// </summary>
        public Func<IReadOnlyList<ChoiceItem>> DynamicChoices { get; }

        public ParameterDescriptor(
            string name,
            Type type,
            int position,
            bool hasDefault,
            object defaultValue,
            string description,
            IReadOnlyList<string> choices,
            float min = float.NaN,
            float max = float.NaN,
            Func<IReadOnlyList<ChoiceItem>> dynamicChoices = null)
        {
            Name = name;
            Type = type;
            Position = position;
            HasDefault = hasDefault;
            DefaultValue = defaultValue;
            Description = description ?? "";
            Choices = choices ?? Array.Empty<string>();
            Min = min;
            Max = max;
            DynamicChoices = dynamicChoices;
        }
    }
}
