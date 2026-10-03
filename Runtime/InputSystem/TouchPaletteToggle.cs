using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Void2610.LiminalPalette.Runtime.InputSystemImpl
{
    /// <summary>
    /// キーボードの無いタッチ端末 (スマホの WebGL 等) で、4 本指を同時に置いたときにパレットを開閉する。
    /// 揃った瞬間に 1 回だけ切り替え、指がすべて離れるまでは再発火しない。
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class TouchPaletteToggle : MonoBehaviour
    {
        // 3 本以下は通常のゲーム操作 (ピンチ・複数ボタン同時押し) と衝突しうる
        private const int REQUIRED_FINGERS = 4;

        private static TouchPaletteToggle _instance;

        // 離すときは 4→3→2→1→0 と段階的に減るため、0 本に戻るまで解除しない
        private bool _latched;

        // Reload Domain を切った Play Mode でも前回のインスタンス参照を持ち越さない
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
        }

        // LiminalPaletteRuntime は BeforeSceneLoad で立ち上がるので、その後に生成する
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // ProductionGuard でパレット自体が起動していない環境では検出も置かない
            if (_instance != null || LiminalPaletteRuntime.Instance == null) return;

            var go = new GameObject(nameof(TouchPaletteToggle)) { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<TouchPaletteToggle>();
        }

        private void OnEnable()
        {
            // 有効化しないと activeTouches が常に空になる。内部は参照カウントなので他所の Enable と共存できる
            EnhancedTouchSupport.Enable();
        }

        private void OnDisable()
        {
            EnhancedTouchSupport.Disable();
        }

        private void Update()
        {
            // InputTestFixture が Input System をリセットすると無効に戻され、activeTouches が例外を投げる
            if (!EnhancedTouchSupport.enabled) return;

            var count = ETouch.activeTouches.Count;
            if (count == 0)
            {
                _latched = false;
                return;
            }
            if (_latched || count < REQUIRED_FINGERS) return;

            _latched = true;
            LiminalPaletteRuntime.Instance.Toggle();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
