using System.Collections.Generic;
using UnityEngine;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// PlayerPrefs に永続化する Runtime 用の履歴実装。
    /// EditorCommandHistory と同じ流儀: Unit Separator () でフラットに区切り、InMemoryCommandHistory に委譲する。
    /// 共通基底に切り出すことも検討したが、EditorPrefs / PlayerPrefs のシグネチャ差で結局フックポイントが多くなり、
    /// 同じ短いコードを 2 箇所に置く方が読みやすいと判断 (Phase 2 の流儀を踏襲)。
    ///
    /// 永続化方針:
    ///   - Record() は SetString のみ即時実行 (PlayerPrefs.Save() は呼ばない)。
    ///     Save() は同期 IO で重く、コマンド連打時にフレーム落ちしうる。
    ///   - PlayerPrefs.Save() は Application.quitting でまとめて 1 回 + Clear() で 1 回。
    ///     Unity は通常終了時に自動保存するが quitting フックで明示的に呼んで安全側に倒す。
    ///   - 二重サブスクライブ防止のためコンストラクタ冒頭で必ず -= してから += する。
    /// </summary>
    public sealed class PlayerPrefsCommandHistory : ICommandHistory
    {
        // PlayerPrefs のキー。プロジェクト共通だが namespace で衝突を避ける。
        public const string PrefsKey = "Void2610.LiminalPalette.History";

        // Unit Separator (制御文字)。コマンドパスにこの文字は含めない前提。
        private const char Separator = '';

        private readonly InMemoryCommandHistory _inner = new InMemoryCommandHistory();

        public PlayerPrefsCommandHistory()
        {
            Load();
            // 終了時にまとめて flush するため Application.quitting に登録 (二重登録防止)。
            Application.quitting -= FlushOnQuit;
            Application.quitting += FlushOnQuit;
        }

        public IReadOnlyList<string> RecentPaths => _inner.RecentPaths;

        public void Record(string path)
        {
            _inner.Record(path);
            // SetString のみ即時。PlayerPrefs.Save() は終了時にまとめて呼ぶ。
            WriteCurrentToPrefs();
        }

        public void Clear()
        {
            _inner.Clear();
            PlayerPrefs.DeleteKey(PrefsKey);
            // Clear はユーザー操作 (履歴消去) なので即時永続化させる。テスト側からも明示確認できる。
            PlayerPrefs.Save();
        }

        public bool Contains(string path) => _inner.Contains(path);
        public int IndexOf(string path) => _inner.IndexOf(path);

        private void Load()
        {
            var raw = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(raw)) return;

            var parts = raw.Split(Separator);
            // 入力時の上限保証は InMemoryCommandHistory.Record に任せる。
            // 古い側から Record して先頭に最新が積まれる順を再現する。
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(parts[i])) _inner.Record(parts[i]);
            }
        }

        // 現在の RecentPaths を PlayerPrefs に書き込む。Save() は呼ばない (バッチ flush に委ねる)。
        private void WriteCurrentToPrefs()
        {
            var paths = _inner.RecentPaths;
            if (paths.Count == 0)
            {
                PlayerPrefs.DeleteKey(PrefsKey);
                return;
            }
            var arr = new string[paths.Count];
            for (var i = 0; i < paths.Count; i++) arr[i] = paths[i];
            PlayerPrefs.SetString(PrefsKey, string.Join(Separator.ToString(), arr));
        }

        // Application.quitting で 1 度だけ呼ばれ、Record で書き貯めた SetString を一括 flush する。
        private static void FlushOnQuit() => PlayerPrefs.Save();
    }
}
