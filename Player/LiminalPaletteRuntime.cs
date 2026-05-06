using UnityEngine;
using UnityEngine.UIElements;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Player
{
    /// <summary>
    /// Runtime (Player ビルド / Play Mode) でパレットをホストする DontDestroyOnLoad シングルトン。
    /// UIDocument を 1 つ持ち、Show / Hide で rootVisualElement.style.display を切り替えるだけ。
    /// PaletteView 自体は初回 Show で生成し以降は使い回す (UXML パース代を節約)。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LiminalPaletteRuntime : MonoBehaviour
    {
        private static LiminalPaletteRuntime _instance;
        public static LiminalPaletteRuntime Instance => _instance;

        // Configurable Enter Play Mode で "Reload Domain" がオフだと、
        // 前回 Play Mode で生成した破棄済み MonoBehaviour 参照が _instance に残る可能性がある。
        // SubsystemRegistration は Play Mode に入るたびに必ず呼ばれるのでここでクリア。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        private UIDocument _document;
        private PanelSettings _panelSettings;
        // 本クラスで CreateInstance した場合のみ true。利用側 Resources 提供の PanelSettings は破棄しない。
        private bool _ownsPanelSettings;
        private PaletteController _controller;
        private PaletteView _view;
        private IPaletteInput _input;
        private PaletteInputBlocker _blocker;
        private PaletteRuntimeSettings _settings;
        private bool _viewBuilt;

        public bool IsVisible => _view != null
            && _view.style.display.value == DisplayStyle.Flex;

        private void Awake()
        {
            // SceneManager.LoadScene(LoadSceneMode.Single) で旧インスタンスが残った場合は破棄。
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            // 本クラスで CreateInstance した PanelSettings のみ後始末する。
            // 旧実装の name 比較では、利用側が Resources/LiminalPaletteRuntimePanelSettings.asset を提供していた場合に
            // 同名のため共有アセットを誤って Destroy してしまう恐れがあった (他の UIDocument にも影響)。
            if (_ownsPanelSettings && _panelSettings != null)
            {
                Destroy(_panelSettings);
                _panelSettings = null;
                _ownsPanelSettings = false;
            }
        }

        /// <summary>RuntimeBootstrap から渡される設定で初期化する。</summary>
        public void Configure(PaletteRuntimeSettings settings)
        {
            _settings = settings;
            EnsureDocument();
            EnsureInput();
            _blocker ??= new PaletteInputBlocker();
            // Configure 直後は非表示。
            HideInternal();
        }

        public void Toggle()
        {
            if (IsVisible) Hide();
            else Show();
        }

        public void Show()
        {
            EnsureView();
            _view.style.display = DisplayStyle.Flex;
            // ResetOnEachOpen が true なら毎回 Reset、false なら状態保持。
            if (_settings != null)
            {
                _controller.ResetIfRequested(_settings.ResetOnEachOpen
                    ? PaletteController.PaletteResetPolicy.OnEachOpen
                    : PaletteController.PaletteResetPolicy.KeepState);
            }
            _view.Focus();
            _blocker?.Engage();
        }

        public void Hide() => HideInternal();

        private void HideInternal()
        {
            if (_view != null) _view.style.display = DisplayStyle.None;
            _blocker?.Disengage();
        }

        private void EnsureDocument()
        {
            if (_document != null) return;
            _document = gameObject.GetComponent<UIDocument>();
            if (_document == null) _document = gameObject.AddComponent<UIDocument>();
            // PanelSettings は Resources で利用側が上書き可能。なければ動的生成 (このとき所有権を取り OnDestroy で破棄)。
            _panelSettings = Resources.Load<PanelSettings>("LiminalPaletteRuntimePanelSettings");
            if (_panelSettings == null)
            {
                _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                _ownsPanelSettings = true;
                _panelSettings.name = "LiminalPaletteRuntimePanelSettings";
                // ConstantPixelSize は物理ピクセル単位で固定サイズになり、Mac Retina 等の高 DPI 環境で
                // UI が極端に小さく描画される (Screen.width/height が物理ピクセルで返るため)。
                // ScaleWithScreenSize で referenceResolution=1440x810 (PlayerSettings の defaultScreen と同じ) に
                // 設定することで、ウィンドウ起動時 (1440x810) で scale=1.0、フルスクリーン (1920x1080) で
                // scale≒1.33 と少し大きめになる。Editor Game View も実用的なサイズに収まる。
                _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                _panelSettings.referenceResolution = new Vector2Int(1440, 810);
                _panelSettings.match = 0.5f;
                _panelSettings.targetTexture = null;
                _panelSettings.sortingOrder = _settings != null ? _settings.PanelSortingOrder : 1000;
            }

            // ThemeStyleSheet を設定しないと UI Toolkit Runtime panel に何も描画されない (Unity 6 既定挙動)。
            // 動的生成 / Resources ロード両方の経路で themeStyleSheet が null なら同梱の LiminalPaletteRuntimeTheme をロードして設定する。
            // 利用側が独自テーマを使いたければ PanelSettings asset 側で予め themeStyleSheet を差し替えておけばこの分岐に入らない。
            if (_panelSettings.themeStyleSheet == null)
            {
                var theme = Resources.Load<ThemeStyleSheet>("LiminalPaletteRuntimeTheme");
                if (theme != null) _panelSettings.themeStyleSheet = theme;
            }
            _document.panelSettings = _panelSettings;
        }

        private void EnsureInput()
        {
            if (_input != null) return;
            var toggleKey = _settings != null ? _settings.ToggleKey : KeyCode.BackQuote;
            var requireMod = _settings == null || _settings.RequireModifier;
            // 常に EventPaletteInput (IMGUI) を返す。OnGUI で Event.current を流し込む必要がある。
            _input = PaletteInputFactory.Create(toggleKey, requireMod);
        }

        private void EnsureView()
        {
            if (_viewBuilt) return;
            _viewBuilt = true;

            _controller = new PaletteController(
                CommandRegistry.Default,
                new CommandExecutor(CommandRegistry.Default),
                new PlayerPrefsCommandHistory());

            _view = new PaletteView(_controller);
            _view.CloseRequested += Hide;
            // Runtime ではフルスクリーン overlay として張り付かせる。
            // rootVisualElement のデフォルトは flex / column なので、自分側で flexGrow=1 にして親 (PanelRootElement) を埋める。
            // PanelRootElement 自体は PanelSettings の参照解像度に合わせて画面サイズになる。
            var root = _document.rootVisualElement;
            root.style.flexGrow = 1;
            _view.style.flexGrow = 1;
            _view.style.position = Position.Absolute;
            _view.style.left = 0;
            _view.style.top = 0;
            _view.style.right = 0;
            _view.style.bottom = 0;
            // ゲーム画面が薄く見える程度に黒バックドロップを敷く (Runtime 限定の演出)。
            // 内側の .palette-root / .palette-bottom は USS で opaque な暗色背景を持つので
            // ここはパネル外側 (= 上下スペースなどパネルが届かない領域) のディマーとして 0.7 で十分。
            _view.style.backgroundColor = new Color(0f, 0f, 0f, 0.7f);
            root.Add(_view);
        }

        private void Update()
        {
            if (_input == null) return;
            if (_input.ConsumeToggle())
            {
                Toggle();
                return;
            }
            // 表示中のみ Cancel (Escape) をフォールバック。UIDocument にフォーカスがある時は PaletteView の
            // KeyDownEvent が拾うのでここまで届かないことが多いが、フォーカス外でも Escape で閉じられるよう保険として用意。
            // ↑↓ Enter Tab 等の UI ナビは UIDocument 側で完結するためここでは扱わない。
            if (!IsVisible) return;
            if (_input.ConsumeCancel()) Hide();
        }

        // EventPaletteInput は IMGUI 経由で KeyDown を拾う設計のため、ここで Event.current を流し込む。
        // OnGUI は 1 フレームに複数回呼ばれる (Layout / KeyDown / Repaint など) が、
        // KeyDown 以外は HandleEvent 内で no-op になる。
        private void OnGUI()
        {
            if (_input is EventPaletteInput ep) ep.HandleEvent(Event.current);
        }
    }
}
