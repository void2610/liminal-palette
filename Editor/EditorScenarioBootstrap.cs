using UnityEditor;

namespace Void2610.LiminalPalette.Editor
{
    /// <summary>
    /// Editor 起動時に IFrameWaiter を EditorFrameWaiter に差し替えるブートストラップ。
    /// Play Mode に入ると RuntimeBootstrap が再度 RuntimeFrameWaiter に切り替えるため、
    /// Play 中は Time.frameCount ベースの実装が使われる (Edit Mode のみ EditorFrameWaiter)。
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorScenarioBootstrap
    {
        static EditorScenarioBootstrap()
        {
            // 既定値 (RuntimeFrameWaiter) を Editor 用に置き換える。
            // Play Mode 中も Editor で動かす分にはこちらの方が誤動作が少ない (Time.frameCount は
            // Editor 起動直後に 0 起点ではないため)。
            LiminalPalette.SetScenarioFrameWaiter(new EditorFrameWaiter());

            // Play Mode 状態変化時に frame waiter を切り替える。
            //   Play 開始 → RuntimeFrameWaiter (Time.frameCount ベース)
            //   Play 終了 → EditorFrameWaiter (EditorApplication.update tick ベース)
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    LiminalPalette.SetScenarioFrameWaiter(new RuntimeFrameWaiter());
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    LiminalPalette.SetScenarioFrameWaiter(new EditorFrameWaiter());
                    break;
            }
        }
    }
}
