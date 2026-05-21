using UnityEditor;
using UnityEngine;

namespace Void2610.LiminalPalette.Editor
{
    /// <summary>
    /// PlayMode を CLI / シナリオから操作するための Editor 専用 [LiminalCommand] 群。
    ///
    /// 背景:
    ///   - シナリオの先頭に差し込まれる LoadScene ステップは Application.isPlaying=false だと
    ///     失敗するので、`liminal run` を Editor 状態でいきなり叩くと全シナリオが落ちる。
    ///   - LP には PlayMode 制御 API が無かったため、利用者は Unity Editor で手動 Play する
    ///     しかなく、CLAUDE.md の「目視確認禁止」方針と矛盾していた。
    ///
    /// このファイルは 3 コマンドを追加して `liminal exec → liminal run` のフルオートを成立させる:
    ///   - Editor/Playmode/Enter  : EditorApplication.EnterPlaymode() を呼んで即リターン
    ///   - Editor/Playmode/Exit   : EditorApplication.ExitPlaymode() を呼んで即リターン
    ///   - Editor/Playmode/Status : 現在の遷移状態を文字列で返す (polling 用)
    ///
    /// PlayMode 突入は Domain Reload を伴うため、Enter コマンド自身は突入の完了を await しない。
    /// クライアントは Status を polling して "playing" になってから次のステップに進む。
    /// </summary>
    public static class PlayModeCommands
    {
        [LiminalCommand("Editor/Playmode/Enter",
            Description = "Editor を PlayMode に切替 (即リターン、突入完了は Editor/Playmode/Status で polling)")]
        public static void EnterPlaymode()
        {
            // 多重リクエストでも安全に通したいので、既に PlayMode 中なら何もせず正常終了扱い。
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.Log("[LiminalPalette] Already entering or in PlayMode.");
                return;
            }
            EditorApplication.EnterPlaymode();
        }

        [LiminalCommand("Editor/Playmode/Exit",
            Description = "Editor の PlayMode を終了 (即リターン、Editor/Playmode/Status で polling)")]
        public static void ExitPlaymode()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            {
                Debug.Log("[LiminalPalette] Not in PlayMode.");
                return;
            }
            EditorApplication.ExitPlaymode();
        }

        [LiminalCommand("Editor/Playmode/Status",
            Description = "PlayMode の現在状態を返す: 'playing' / 'transitioning' / 'editor'")]
        public static string Status()
        {
            // isPlaying は「完全に Play 中」、isPlayingOrWillChangePlaymode は「遷移を含む」。
            // 両者の差分が遷移中フェーズになる。
            if (EditorApplication.isPlaying)
                return "playing";
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "transitioning";
            return "editor";
        }
    }
}
