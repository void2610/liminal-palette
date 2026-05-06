using System.Collections.Generic;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// コマンド実行履歴。新しい順に並び、空クエリ時の表示やスコアブーストの計算に使う。
    /// 永続化の有無は実装側に任せる (Editor は EditorPrefs、Runtime は PlayerPrefs / In-memory)。
    /// </summary>
    public interface ICommandHistory
    {
        /// <summary>新しい順にコマンドパスを並べたスナップショット。</summary>
        IReadOnlyList<string> RecentPaths { get; }

        /// <summary>新しい実行を記録する。同一パスがあれば削除して先頭に移動する。上限超過は古いものから捨てる。</summary>
        void Record(string path);

        /// <summary>履歴をすべて消す。</summary>
        void Clear();

        /// <summary>パスが履歴に存在するかを返す。</summary>
        bool Contains(string path);

        /// <summary>パスの新しい順での位置 (0 が最新)。なければ -1。スコアブースト計算で使う想定。</summary>
        int IndexOf(string path);
    }
}
