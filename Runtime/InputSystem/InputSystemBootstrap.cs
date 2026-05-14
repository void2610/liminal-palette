using UnityEngine;
using UnityEngine.InputSystem;

namespace Void2610.LiminalPalette.Runtime.InputSystemImpl
{
    /// <summary>
    /// パレット表示中だけゲーム側 InputSystem ActionMap を一括停止／復元するブートストラップ。
    /// Runtime のホットキー検出自体は EventPaletteInput (IMGUI) に一本化したので、
    /// 本 asmdef はもはや「パレットを開閉する側」の入力には関与しない。
    /// 残っている責務はパレット展開中にゲーム入力をブロックするための ActionMap 停止のみ。
    ///
    /// 本 asmdef は LIMINAL_PALETTE_INPUTSYSTEM が立っている環境でのみリンクされるため、
    /// InputSystem 未導入プロジェクトでは Hook が呼ばれず ActionMap の自動停止も発生しない (パレットは普通に開閉する)。
    /// </summary>
    internal static class InputSystemBootstrap
    {
        // Configurable Enter Play Mode で "Reload Domain" がオフだと、
        // static event 購読 (OnEngage / OnDisengage) と _stash が Play Mode 間で持ち越される。
        // Play Mode 2 回目以降で OnEngage に DisableAllActionMaps が多重登録され、
        // _stash には前回の (破棄済み) ActionMap 参照が残るのを避けるためのリセット。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PaletteInputBlocker.OnEngage -= DisableAllActionMaps;
            PaletteInputBlocker.OnDisengage -= RestoreActionMaps;
            _stash.Clear();
        }

        // BeforeSplashScreen で登録 (LiminalPaletteRuntime の BeforeSceneLoad より前)。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Hook()
        {
            // 二重登録防止のため一度外してから足す (ResetStatics と二重保険)。
            PaletteInputBlocker.OnEngage -= DisableAllActionMaps;
            PaletteInputBlocker.OnEngage += DisableAllActionMaps;
            PaletteInputBlocker.OnDisengage -= RestoreActionMaps;
            PaletteInputBlocker.OnDisengage += RestoreActionMaps;
        }

        // Engage 時点で enabled だった ActionMap を覚えておくスタッシュ。
        private static readonly System.Collections.Generic.List<InputActionMap> _stash =
            new System.Collections.Generic.List<InputActionMap>();

        private static void DisableAllActionMaps()
        {
            _stash.Clear();
            var assets = Object.FindObjectsByType<InputActionAsset>(FindObjectsSortMode.None);
            foreach (var asset in assets)
            {
                if (asset == null) continue;
                foreach (var map in asset.actionMaps)
                {
                    if (map == null || !map.enabled) continue;
                    _stash.Add(map);
                    map.Disable();
                }
            }
        }

        private static void RestoreActionMaps()
        {
            for (var i = 0; i < _stash.Count; i++)
            {
                var map = _stash[i];
                if (map == null) continue;
                map.Enable();
            }
            _stash.Clear();
        }
    }
}
