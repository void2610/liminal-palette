using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Void2610.LiminalPalette.Runtime
{
    /// <summary>
    /// シナリオ実行中であることを画面に伝えるオーバーレイ。
    /// 専用 UIDocument に画面全体に張り付く透明な VisualElement を 1 枚置き、
    /// 4 辺に枠線を描く + 上部にシナリオ Path とステップ進捗を表示する。
    ///
    /// 通信路は <see cref="ScenarioExecutor.ScenarioRunStarted"/> 等の static event。
    /// 利用側は何も呼ばなくてよく、ScenarioExecutor が実行を開始すれば自動で出現する。
    ///
    /// Editor / Player Build 双方で動く。Edit Mode (PlayMode 外) では UIDocument が
    /// Game View に描画されないため、Edit Mode シナリオでは見えないが害もない。
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class ScenarioOverlay : MonoBehaviour
    {
        private static ScenarioOverlay _instance;
        public static ScenarioOverlay Instance => _instance;

        // SubsystemRegistration は Reload Domain off の Play Mode 開始でも必ず呼ばれる。
        // LiminalPaletteRuntime と同じ理由で static フィールドをリセットする。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        private UIDocument _document;
        private PanelSettings _panelSettings;
        // CreateInstance した PanelSettings だけ OnDestroy で破棄する。Resources 由来は触らない。
        private bool _ownsPanelSettings;

        private VisualElement _root;
        private Label _badge;

        private PaletteRuntimeSettings _settings;

        // 表示状態。実行中だけ true。OnDestroy で event を確実に解除するためのフラグも兼ねる。
        private bool _eventsSubscribed;

        private void Awake()
        {
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
            UnsubscribeEvents();
            if (_instance == this) _instance = null;
            if (_ownsPanelSettings && _panelSettings != null)
            {
                Destroy(_panelSettings);
                _panelSettings = null;
                _ownsPanelSettings = false;
            }
        }

        /// <summary>RuntimeBootstrap から呼ばれる初期化。設定値を取り込み、UI を組む。</summary>
        public void Configure(PaletteRuntimeSettings settings)
        {
            _settings = settings;
            // 設定で無効化されている場合は GameObject 自体を生かしておく必要がない。
            // ただし RuntimeBootstrap 側で生成自体スキップする前提なのでここでは Disable のみ。
            if (settings != null && !settings.ShowScenarioOverlay)
            {
                enabled = false;
                return;
            }
            EnsureDocument();
            EnsureView();
            SubscribeEvents();
            HideOverlay();
        }

        private void EnsureDocument()
        {
            if (_document != null) return;
            _document = gameObject.GetComponent<UIDocument>();
            if (_document == null) _document = gameObject.AddComponent<UIDocument>();

            // パレット本体と同じく Resources で利用側が上書き可能。
            // 無ければ動的生成 (所有権を取り OnDestroy で破棄)。
            _panelSettings = Resources.Load<PanelSettings>("LiminalPaletteOverlayPanelSettings");
            if (_panelSettings == null)
            {
                _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                _ownsPanelSettings = true;
                _panelSettings.name = "LiminalPaletteOverlayPanelSettings";
                _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                _panelSettings.referenceResolution = new Vector2Int(1440, 810);
                _panelSettings.match = 0.5f;
                _panelSettings.targetTexture = null;
                _panelSettings.sortingOrder = _settings != null ? _settings.ScenarioOverlaySortingOrder : 999;
            }

            if (_panelSettings.themeStyleSheet == null)
            {
                var theme = Resources.Load<ThemeStyleSheet>("LiminalPaletteRuntimeTheme");
                if (theme != null) _panelSettings.themeStyleSheet = theme;
            }
            _document.panelSettings = _panelSettings;
        }

        // 画面に張り付く透明な VisualElement と、上端中央のシナリオバッジを組む。
        // picking は無効にして、オーバーレイがゲーム入力 / パレット操作を奪わないようにする。
        private void EnsureView()
        {
            if (_root != null) return;
            var hostRoot = _document.rootVisualElement;
            hostRoot.style.flexGrow = 1;
            // 親 root 全体を picking 透過にする。Game View / パレットへ入力をそのまま流す。
            hostRoot.pickingMode = PickingMode.Ignore;

            _root = new VisualElement { name = "scenario-overlay-root" };
            _root.pickingMode = PickingMode.Ignore;
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.right = 0;
            _root.style.top = 0;
            _root.style.bottom = 0;
            _root.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            // 枠線 (4 辺)。
            var color = _settings != null
                ? _settings.ScenarioOverlayColor
                : new Color(1f, 0.55f, 0.1f, 0.95f);
            var w = _settings != null ? Mathf.Max(0f, _settings.ScenarioOverlayBorderWidth) : 6f;
            _root.style.borderTopWidth = w;
            _root.style.borderBottomWidth = w;
            _root.style.borderLeftWidth = w;
            _root.style.borderRightWidth = w;
            _root.style.borderTopColor = color;
            _root.style.borderBottomColor = color;
            _root.style.borderLeftColor = color;
            _root.style.borderRightColor = color;
            hostRoot.Add(_root);

            // 上端中央にシナリオバッジを置くためのラッパー (横方向中央寄せ専用)。
            // VisualElement のデフォルト flex-direction は column のため、justifyContent は
            // 主軸 (= 縦) を制御してしまう。横方向中央寄せには alignItems を使う。
            var topBar = new VisualElement { name = "scenario-overlay-topbar" };
            topBar.pickingMode = PickingMode.Ignore;
            topBar.style.position = Position.Absolute;
            topBar.style.top = 0;
            topBar.style.left = 0;
            topBar.style.right = 0;
            topBar.style.alignItems = Align.Center;
            _root.Add(topBar);

            _badge = new Label { name = "scenario-overlay-badge", text = "" };
            _badge.pickingMode = PickingMode.Ignore;
            _badge.style.backgroundColor = color;
            _badge.style.color = Color.white;
            _badge.style.paddingTop = 4;
            _badge.style.paddingBottom = 4;
            _badge.style.paddingLeft = 12;
            _badge.style.paddingRight = 12;
            _badge.style.borderBottomLeftRadius = 6;
            _badge.style.borderBottomRightRadius = 6;
            _badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            _badge.style.fontSize = 13;
            _badge.style.whiteSpace = WhiteSpace.NoWrap;
            topBar.Add(_badge);
        }

        private void SubscribeEvents()
        {
            if (_eventsSubscribed) return;
            ScenarioExecutor.ScenarioRunStarted += OnScenarioStarted;
            ScenarioExecutor.ScenarioRunStepChanged += OnScenarioStepChanged;
            ScenarioExecutor.ScenarioRunFinished += OnScenarioFinished;
            _eventsSubscribed = true;
        }

        private void UnsubscribeEvents()
        {
            if (!_eventsSubscribed) return;
            ScenarioExecutor.ScenarioRunStarted -= OnScenarioStarted;
            ScenarioExecutor.ScenarioRunStepChanged -= OnScenarioStepChanged;
            ScenarioExecutor.ScenarioRunFinished -= OnScenarioFinished;
            _eventsSubscribed = false;
        }

        private void OnScenarioStarted(ScenarioProgress p)
        {
            // Configure 前 (= UI 未構築) でも安全にスキップ。
            if (_root == null || _badge == null) return;
            _badge.text = BuildBadgeText(p.Path, 0, p.TotalSteps, null);
            ShowOverlay();
        }

        private void OnScenarioStepChanged(ScenarioProgress p)
        {
            if (_root == null || _badge == null) return;
            // StepIndex は 0-origin。表示は 1-origin の方が読みやすいので +1 してから組み立てる。
            _badge.text = BuildBadgeText(p.Path, p.StepIndex + 1, p.TotalSteps, p.CurrentStep);
        }

        private void OnScenarioFinished(ScenarioResult result)
        {
            if (_root == null) return;
            HideOverlay();
        }

        // 例: "Scenario: Combat/EnemyTakesDamage  3/5  Command"
        // path 未指定 (ad-hoc 実行) のときは "(ad-hoc)" を出す。
        private static string BuildBadgeText(string path, int currentStep, int totalSteps, ScenarioStep step)
        {
            var name = string.IsNullOrEmpty(path) ? "(ad-hoc)" : path;
            if (totalSteps <= 0)
            {
                return $"Scenario: {name}";
            }
            var stepKind = step != null ? step.Kind.ToString() : "";
            var stepInfo = string.IsNullOrEmpty(stepKind)
                ? $"{currentStep}/{totalSteps}"
                : $"{currentStep}/{totalSteps}  {stepKind}";
            return $"Scenario: {name}   {stepInfo}";
        }

        private void ShowOverlay()
        {
            if (_root != null) _root.style.display = DisplayStyle.Flex;
        }

        private void HideOverlay()
        {
            if (_root != null) _root.style.display = DisplayStyle.None;
        }
    }
}
