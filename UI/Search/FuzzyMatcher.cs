using System;
using System.Collections.Generic;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// VSCode コマンドパレット風のファジー検索。
    /// 部分文字列ではなく subsequence マッチを行い、連続一致 / 単語境界 / 先頭一致にスコアブーストを与える。
    /// 100〜1000 コマンド規模で毎フレーム呼んでも問題ない計算量を意図する。
    /// </summary>
    public static class FuzzyMatcher
    {
        // スコアリング定数。値の調整で検索体験が大きく変わるため、変更時は FuzzyMatcherTests を必ず通すこと。
        private const int BaseMatchScore = 10;
        private const int ConsecutiveBonus = 15;
        private const int WordBoundaryBonus = 20;
        private const int PrefixBonus = 25;
        private const int CaseMismatchPenalty = -2;
        private const int SkipPenaltyPerChar = -1;

        /// <summary>query が target にファジー一致するかを判定し、スコアとマッチ位置を返す。</summary>
        public static MatchResult Match(string query, string target)
        {
            // 空クエリは「全件マッチ・スコア 0」として扱う。呼び出し側でフィルタしないケースにも対応できる。
            if (string.IsNullOrEmpty(query)) return new MatchResult(true, 0, Array.Empty<int>());
            if (string.IsNullOrEmpty(target)) return MatchResult.NoMatch;

            // 貪欲法: query の各文字に対して target を左から走査し、最初に case-insensitive 一致した位置を採用する。
            // 計画書通り 1000 件規模なら十分高速。後でより高度なマッチングが必要になったら DP に置き換える。
            var matchedIndices = new int[query.Length];
            var qi = 0;
            var prevMatchIndex = -2; // 連続判定用 (初回は -2 にして先頭マッチでも連続にしない)
            var score = 0;
            var consecutiveRun = 0;

            for (var ti = 0; ti < target.Length && qi < query.Length; ti++)
            {
                if (!CharEqualsIgnoreCase(query[qi], target[ti])) continue;

                matchedIndices[qi] = ti;
                var charScore = BaseMatchScore;

                // 先頭 / 単語境界 / 連続マッチのボーナスを加算。
                if (ti == 0)
                {
                    charScore += PrefixBonus;
                }
                else if (IsWordBoundary(target, ti))
                {
                    charScore += WordBoundaryBonus;
                }

                if (ti == prevMatchIndex + 1)
                {
                    consecutiveRun++;
                    charScore += ConsecutiveBonus * consecutiveRun;
                }
                else
                {
                    consecutiveRun = 0;
                    // 直前のマッチからスキップした文字数にペナルティ (間延びしたマッチを抑制)。
                    if (prevMatchIndex >= 0)
                    {
                        charScore += SkipPenaltyPerChar * (ti - prevMatchIndex - 1);
                    }
                }

                // 大文字小文字が完全一致しない場合の小ペナルティ (それでもマッチは成立)。
                if (query[qi] != target[ti]) charScore += CaseMismatchPenalty;

                score += charScore;
                prevMatchIndex = ti;
                qi++;
            }

            if (qi < query.Length) return MatchResult.NoMatch;
            // クランプ: ペナルティでスコアが負になるケースは Score=0 + Matched=true として返す
            // (アンマッチではないが優先度は最低)。
            if (score < 0) score = 0;
            return new MatchResult(true, score, matchedIndices);
        }

        /// <summary>
        /// target に加えてエイリアス文字列も候補としてスコアリングする。最高スコアを採用するが、
        /// MatchedIndices は target に対するものだけを返す (UI ハイライトは target に対して行うため)。
        /// </summary>
        public static MatchResult Match(string query, string target, IReadOnlyList<string> aliases)
        {
            var targetResult = Match(query, target);
            if (aliases == null || aliases.Count == 0) return targetResult;

            var bestScore = targetResult.Score;
            var bestMatched = targetResult.Matched;
            for (var i = 0; i < aliases.Count; i++)
            {
                var alias = aliases[i];
                if (string.IsNullOrEmpty(alias)) continue;
                var aliasResult = Match(query, alias);
                if (!aliasResult.Matched) continue;
                if (!bestMatched || aliasResult.Score > bestScore)
                {
                    bestScore = aliasResult.Score;
                    bestMatched = true;
                }
            }

            if (!bestMatched) return MatchResult.NoMatch;
            // target でマッチしていればその MatchedIndices を保持。
            // alias 経由のみマッチした場合は target に対するハイライト位置がないため空配列で返す。
            var indices = targetResult.Matched ? targetResult.MatchedIndices : Array.Empty<int>();
            return new MatchResult(true, bestScore, indices);
        }

        // 単語境界判定: 直前文字が区切り文字 (/ _ - 空白) か、camelCase の境界 (前が小文字 + 自分が大文字) なら true。
        private static bool IsWordBoundary(string target, int index)
        {
            if (index <= 0) return false;
            var prev = target[index - 1];
            if (prev == '/' || prev == '_' || prev == '-' || prev == ' ' || prev == '\t' || prev == '.') return true;
            var cur = target[index];
            if (char.IsLower(prev) && char.IsUpper(cur)) return true;
            return false;
        }

        private static bool CharEqualsIgnoreCase(char a, char b)
        {
            if (a == b) return true;
            return char.ToLowerInvariant(a) == char.ToLowerInvariant(b);
        }
    }
}
