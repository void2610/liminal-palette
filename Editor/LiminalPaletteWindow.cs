using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Editor
{
    /// <summary>
    /// LiminalPalette を Editor 上でホストする EditorWindow。
    /// 通常タブとしてドッキング可能。`Cmd/Ctrl+K` でトグル、検索ボックスに即フォーカス。
    /// </summary>
    public sealed class LiminalPaletteWindow : EditorWindow
    {
        private const string WindowTitle = "LiminalPalette";
        private static readonly Vector2 DefaultSize = new Vector2(640, 420);

        private PaletteController _controller;
        private PaletteView _view;

        /// <summary>
        /// ショートカット (Cmd/Ctrl+K) で呼ばれるトグルエントリ。
        /// 元の `Cmd/Ctrl+Shift+P` は Play Mode Pause と衝突するため変更。
        ///
        /// 注: Unity の [Shortcut] は Editor グローバルで Game ウィンドウフォーカス中も発火するが、
        /// macOS で Cmd+P から Play Mode に入った直後は OS / Editor 側で Cmd の keyup を取り損ね、
        /// ShortcutManager が K 単独押下でも Cmd+K として誤発火するケースがある (Unity 既知挙動)。
        ///
        /// このため Play Mode 中はここでは何もせず、Runtime 側 (LiminalPaletteRuntime + 自前の
        /// IPaletteInput 実装) で K の入力を処理する。Runtime 側は modifier の rising-edge を
        /// 自前で追跡しており、stuck な Cmd を弾けるため誤発火しない。
        ///
        /// 旧実装で「Editor が キー入力を消費するから Runtime に届かない」とコメントしていたが、
        /// 実際には ShortcutManager (UI レイヤ) と InputSystem (バックエンド) は経路が独立で、
        /// Game View フォーカス時の物理キー押下は両方に届く。Editor Shortcut から Runtime に
        /// ディスパッチする必要は無い。
        /// </summary>
        [Shortcut("LiminalPalette/Toggle", null, KeyCode.K, ShortcutModifiers.Action)]
        public static void Toggle()
        {
            // Play Mode (実行中 / 切替中) では Runtime 側 IPaletteInput が直接 K を拾うのでここでは何もしない。
            // Editor Shortcut の誤発火 (Cmd+P 直後の stuck modifier) でこのハンドラが呼ばれても無害。
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            // EditMode: Editor 側 Window を開閉する。
            var existing = Resources.FindObjectsOfTypeAll<LiminalPaletteWindow>();
            if (existing != null && existing.Length > 0)
            {
                existing[0].Close();
                return;
            }

            ShowPalette();
        }

        /// <summary>
        /// パレットを開く (テストやメニューからの直接呼び出し用)。
        /// `utility: false` で通常タブとして開き、ユーザーが Inspector などと同様にドッキングできるようにする。
        /// </summary>
        public static LiminalPaletteWindow ShowPalette()
        {
            var window = GetWindow<LiminalPaletteWindow>(utility: false, title: WindowTitle, focus: true);
            window.minSize = DefaultSize;
            // 既にユーザーがドッキング配置している場合は位置を上書きしない。
            // 初回 (フローティング) のみ画面中央に置く。
            if (!window.docked)
            {
                window.position = CenterOnScreen(DefaultSize);
            }
            window.Focus();
            return window;
        }

        // メインスクリーンの中央に配置する。マルチディスプレイ環境ではプライマリにフォールバック。
        private static Rect CenterOnScreen(Vector2 size)
        {
            var main = EditorGUIUtility.GetMainWindowPosition();
            if (main.width <= 1 || main.height <= 1)
            {
                // GetMainWindowPosition が機能しない (まれ) 場合のフォールバック。
                return new Rect((Screen.currentResolution.width - size.x) * 0.5f,
                                (Screen.currentResolution.height - size.y) * 0.5f,
                                size.x, size.y);
            }
            return new Rect(main.x + (main.width - size.x) * 0.5f,
                            main.y + (main.height - size.y) * 0.5f,
                            size.x, size.y);
        }

        private void OnEnable()
        {
            // OnEnable は DomainReload 後にも呼ばれる。controller / view を再生成して整合させる。
            _controller = new PaletteController(
                CommandRegistry.Default,
                new CommandExecutor(CommandRegistry.Default),
                new EditorCommandHistory());
            _view = new PaletteView(_controller);
            _view.CloseRequested += Close;
            rootVisualElement.Add(_view);
        }

        private void OnDisable()
        {
            if (_view != null) _view.CloseRequested -= Close;
            // _controller のイベント購読は PaletteView 側 (StateChanged) のみで、_view と同時に GC される。
        }

        private void OnFocus()
        {
            // ウィンドウがフォーカスされたら (タブ切替・再アクティブ化を含む) 検索ボックスへ。
            // ドッキング中もタブをクリックするたびに即タイプできる体験を維持する。
            _view?.Focus();
        }

        // 注: 旧実装にあった OnLostFocus 自動クローズはドッキング体験と相性が悪いため削除。
        // ウィンドウのクローズはユーザー操作 (×ボタン) または Esc キー (PaletteView.CloseRequested) のみ。
    }
}
