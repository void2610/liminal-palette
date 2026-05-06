using System;
using System.Collections.Generic;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// FuzzyMatcher 1 回のマッチ結果。
    /// MatchedIndices は target 上の文字位置 (0 始まり) で、ハイライト描画にそのまま使える。
    /// </summary>
    public readonly struct MatchResult
    {
        /// <summary>true ならマッチ成立 (Score > 0 の保証はないが、>=0 とは限らないので Matched で判定する)。</summary>
        public bool Matched { get; }

        /// <summary>マッチスコア。高いほど優先度が高い。アンマッチ時は 0。</summary>
        public int Score { get; }

        /// <summary>target 上のマッチ文字位置。エイリアス経由マッチでは空配列を返す。</summary>
        public IReadOnlyList<int> MatchedIndices { get; }

        public MatchResult(bool matched, int score, IReadOnlyList<int> matchedIndices)
        {
            Matched = matched;
            Score = score;
            MatchedIndices = matchedIndices ?? Array.Empty<int>();
        }

        /// <summary>アンマッチを表す既定値。</summary>
        public static MatchResult NoMatch => new MatchResult(false, 0, Array.Empty<int>());
    }
}
