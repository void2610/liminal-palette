using UnityEngine;

namespace Void2610.LiminalPalette.Runtime
{
    /// <summary>
    /// `Time.timeScale` を操作する組み込みランタイムコマンド。
    /// パス prefix は `Time/` (`Editor/` ではない) なので Editor / PlayMode / Player ビルド
    /// すべてから呼べる。利用例:
    /// <list type="bullet">
    ///   <item>シナリオで AI の状態遷移を待つ間 `Time/SetScale 10` で高速化</item>
    ///   <item>UI チェックや表示確認で `Time/Pause` してから `Time/Resume`</item>
    /// </list>
    /// LP の Runtime asmdef は `autoReferenced: true` なので、利用側は何もせずにこれらが
    /// パレットに出現する。
    /// </summary>
    public static class TimeCommands
    {
        // ---- Set / Reset ----

        [LiminalCommand("Time/SetScale", Description = "Time.timeScale を指定値に設定 (例: 0=停止, 1=等速, 10=10倍速)")]
        public static string SetScale(
            [LiminalParam(Description = "スケール (0 以上)")] float scale)
        {
            // 負値は Unity 仕様で例外こそ起きないが意味が無いので 0 にクランプする。
            // ここで黙ってクランプするのは「シナリオ側の typo を即クラッシュさせない」配慮。
            if (scale < 0f) scale = 0f;
            Time.timeScale = scale;
            return $"Time.timeScale = {Time.timeScale}";
        }

        [LiminalCommand("Time/Reset", Description = "Time.timeScale を 1 (等速) に戻す")]
        public static string Reset()
        {
            Time.timeScale = 1f;
            return "Time.timeScale = 1";
        }

        // ---- Shortcuts ----

        [LiminalCommand("Time/Pause", Description = "Time.timeScale を 0 にして時間停止")]
        public static string Pause()
        {
            Time.timeScale = 0f;
            return "Time.timeScale = 0 (paused)";
        }

        [LiminalCommand("Time/Resume", Description = "Time.timeScale を 1 にして時間再開 (Reset と同義)")]
        public static string Resume()
        {
            Time.timeScale = 1f;
            return "Time.timeScale = 1 (resumed)";
        }

        // ---- Read ----

        [LiminalCommand("Time/Get", Description = "現在の Time.timeScale を取得")]
        public static string Get() => Time.timeScale.ToString("F3");
    }
}
