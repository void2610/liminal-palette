using System;

namespace Void2610.LiminalPalette.Runtime
{
    /// <summary>
    /// シナリオオーバーレイの一時抑制 (公開ファサード)。
    /// スクリーンショット比較等でオーバーレイの写り込みを防ぎたい利用側が、
    /// using スコープで囲んでいる間だけ表示を止める。スコープ終了で走行中表示へ自動復帰する。
    /// </summary>
    public static class ScenarioOverlaySuppression
    {
        public static IDisposable Begin() => ScenarioOverlay.Suppress();
    }
}
