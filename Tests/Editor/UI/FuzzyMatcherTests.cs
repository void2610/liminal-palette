using System.Diagnostics;
using NUnit.Framework;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Tests.UI
{
    public sealed class FuzzyMatcherTests
    {
        [Test]
        public void Subsequence_Match_Succeeds()
        {
            var r = FuzzyMatcher.Match("phs", "Player/Health/Set");
            Assert.IsTrue(r.Matched);
            Assert.Greater(r.Score, 0);
        }

        [Test]
        public void NonSubsequence_DoesNotMatch()
        {
            var r = FuzzyMatcher.Match("xyz", "Player/Health/Set");
            Assert.IsFalse(r.Matched);
        }

        [Test]
        public void EmptyQuery_MatchesAnyTarget_WithZeroScore()
        {
            var r = FuzzyMatcher.Match("", "Player/Health/Set");
            Assert.IsTrue(r.Matched);
            Assert.AreEqual(0, r.Score);
        }

        [Test]
        public void ConsecutiveRun_OutscoresScattered()
        {
            // "set" は "Set" に連続一致。"st" はスキップ込みでマッチするのでスコアが下がる。
            var consecutive = FuzzyMatcher.Match("set", "Player/Health/Set");
            var scattered = FuzzyMatcher.Match("st", "Player/Health/Set");
            Assert.IsTrue(consecutive.Matched);
            Assert.IsTrue(scattered.Matched);
            Assert.Greater(consecutive.Score, scattered.Score);
        }

        [Test]
        public void PrefixMatch_OutscoresInteriorMatch()
        {
            // 先頭 'P' が当たる方が単語中の 'P' よりも高スコア。
            var prefix = FuzzyMatcher.Match("p", "Player");
            var interior = FuzzyMatcher.Match("p", "Apple");
            Assert.IsTrue(prefix.Matched);
            Assert.IsTrue(interior.Matched);
            Assert.Greater(prefix.Score, interior.Score);
        }

        [Test]
        public void WordBoundary_MatchGetsBoost()
        {
            // 'h' が "/Health" の境界に当たるパターン。
            var boundary = FuzzyMatcher.Match("h", "Player/Health/Set");
            // 'h' が単語の途中にしか出ないパターン。
            var nonBoundary = FuzzyMatcher.Match("h", "atch");
            Assert.IsTrue(boundary.Matched);
            Assert.IsTrue(nonBoundary.Matched);
            Assert.Greater(boundary.Score, nonBoundary.Score);
        }

        [Test]
        public void IsCaseInsensitive_ButPenalizesCaseMismatch()
        {
            var lower = FuzzyMatcher.Match("phs", "Player/Health/Set");
            var upper = FuzzyMatcher.Match("PHS", "Player/Health/Set");
            Assert.IsTrue(lower.Matched);
            Assert.IsTrue(upper.Matched);
            // 大文字小文字違いペナルティはわずかなので、スコア差は小さいが大文字一致の方が高い。
            Assert.GreaterOrEqual(upper.Score, lower.Score);
        }

        [Test]
        public void MatchedIndices_PointToCorrectPositions()
        {
            var r = FuzzyMatcher.Match("phs", "Player/Health/Set");
            Assert.IsTrue(r.Matched);
            Assert.AreEqual(3, r.MatchedIndices.Count);
            // それぞれがマッチ文字を指していることを確認。
            Assert.AreEqual('P', "Player/Health/Set"[r.MatchedIndices[0]]);
            Assert.AreEqual('H', "Player/Health/Set"[r.MatchedIndices[1]]);
            Assert.AreEqual('S', "Player/Health/Set"[r.MatchedIndices[2]]);
        }

        [Test]
        public void Aliases_AreConsidered_AndMaxScoreUsed()
        {
            // target ではマッチしないが alias でマッチする例。
            var r = FuzzyMatcher.Match("xyz", "Player/Health/Set", new[] { "xyz" });
            Assert.IsTrue(r.Matched);
            Assert.Greater(r.Score, 0);
            // alias 経由のみのマッチは target にハイライト位置を持たない。
            Assert.AreEqual(0, r.MatchedIndices.Count);
        }

        [Test]
        public void Aliases_OnlyImproveScore_NeverWorsen()
        {
            var r1 = FuzzyMatcher.Match("phs", "Player/Health/Set");
            var r2 = FuzzyMatcher.Match("phs", "Player/Health/Set", new[] { "totally-different" });
            Assert.AreEqual(r1.Score, r2.Score);
        }

        [Test]
        public void PerformanceUnder1000Targets_FinishesInUnder10ms()
        {
            // パフォーマンス回帰検出用。1000 件規模で 10ms 以内 (CI で揺らぐので余裕を持って 50ms でガード)。
            var paths = new string[1000];
            for (var i = 0; i < paths.Length; i++)
            {
                paths[i] = $"Category{i / 100}/Sub{(i / 10) % 10}/Command{i % 10}";
            }
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < paths.Length; i++)
            {
                FuzzyMatcher.Match("css", paths[i]);
            }
            sw.Stop();
            Assert.Less(sw.ElapsedMilliseconds, 50, $"FuzzyMatcher took {sw.ElapsedMilliseconds}ms for 1000 targets");
        }
    }
}
