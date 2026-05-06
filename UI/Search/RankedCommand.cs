using System;
using System.Collections.Generic;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// 検索結果 1 件。コマンドメタデータ + マッチスコア + ハイライト情報 + 履歴フラグ。
    /// </summary>
    public sealed class RankedCommand
    {
        /// <summary>対応するコマンド。</summary>
        public CommandDescriptor Descriptor { get; }

        /// <summary>FuzzyMatcher のスコア + 履歴ブースト後の最終値。</summary>
        public int Score { get; }

        /// <summary>target 上のマッチ位置 (UI ハイライト用)。空クエリ時や alias 経由マッチ時は空配列。</summary>
        public IReadOnlyList<int> MatchedIndices { get; }

        /// <summary>履歴 (ICommandHistory) に存在するかどうか。UI で「最近使った」マークを出すのに使う。</summary>
        public bool FromHistory { get; }

        public RankedCommand(CommandDescriptor descriptor, int score, IReadOnlyList<int> matchedIndices, bool fromHistory)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Score = score;
            MatchedIndices = matchedIndices ?? Array.Empty<int>();
            FromHistory = fromHistory;
        }
    }
}
