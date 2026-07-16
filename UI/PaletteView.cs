using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Void2610.LiminalPalette;

Void2610.LiminalPalette.UI
{
    /// <summary>
    /// パレット本体の VisualElement。Editor / Runtime の双方でホスト先 (rootVisualElement / UIDocument) に追加して使う。
    /// PaletteController と双方向にバインドする (UI 入力 → controller、controller の状態変更 → UI 更新)。
    /// 構造: [Tabs] [Search] [ColumnHeader] [List (flex)] [Bottom: Cmd / Status / Args / Run / Result]
    /// </summary>
    public sealed class PaletteView : VisualElement
    {
        // ハイライト色は USS 変数 --lp-match と同じ値。Unity の richText は CSS 変数を解釈しないため hex で直接指定。
        private const string HighlightColorHex = "#FFC850";

        // パレットの表示モード。
        //   Commands:  全コマンド (新規実行)
        //   Scenarios: 登録シナリオの一覧 (Run でステップ列を順次実行)
        //   Logs:      起動履歴 (詳細閲覧。引数 / Debug.Log / スタックトレースを確認)
        //   History:   起動履歴 (再実行特化。Run で同じ引数のまま実行)
        private enum ViewMode { Commands, Scenarios, Logs, History }

        private readonly PaletteController _controller;
        private VisualElement _tabsBar;
        private TextField _searchInput;
        private ListView _resultsList;
        private VisualElement _bottom;
        private Label _bottomCmd;
        private Label _bottomStatus;
        private VisualElement _argumentPanel;
        // Phase 5a: 選択コマンドの prefix と一致する [LiminalObservableField] を表示するセクション。
        private ObservableFieldsView _observableFields;
        private Button _runButton;
        // 検索ヘッダ右端に常時置く Submit ボタン。物理 Enter / NavigationSubmit に加えて、
        // モバイル (WebGL) ソフトキーボードのように Enter 系イベントを発火しない環境向けの
        // 確定手段として用意する。タップで Enter 押下と同等の動作 (引数フロー開始 or 実行) を行う。
        private Button _headerSubmitButton;
        private ResultView _resultView;
        private Label _logStackLabel;

        private ViewMode _mode = ViewMode.Commands;
        // Logs / History タブ用の最新スナップショット (新しい順)。InvocationStore.Changed のたびに再構築する。
        private readonly List<CommandInvocation> _invocationSnapshot = new List<CommandInvocation>();
        // 起動履歴で選択中の行 (Logs / History モードで共有)。Commands モードでは未使用。
        private int _invocationSelectedIndex = 0;
        // 起動履歴に対する検索クエリ (Path / args の単純部分一致)。
        private string _invocationQuery = "";

        // タブ。ボタンと filter のペア。
        private readonly List<Button> _tabs = new List<Button>();

        // 引数の現在値。各 IParameterEditor の onChanged で更新される。
        private readonly Dictionary<string, object> _currentArgValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // 直前にバインドされたコマンドのパス。SelectedCommand が変わったときだけ引数パネルを再構築する。
        private string _boundCommandPath;

        // VSCode 風の複数ステップ引数入力フロー用の状態。
        // Enter で 1 つずつパラメータを確定していき、最後の Enter で実行する。
        // Esc で検索 (results-list) モードに戻す。フロー中は results-list と bottom 引数パネルを隠す。
        private bool _paramFlowActive;
        private CommandDescriptor _paramFlowCommand;
        private int _paramFlowIndex;
        private VisualElement _paramFlowPanel;
        private Label _paramFlowCmdLabel;
        private VisualElement _paramFlowBreadcrumbs;
        private Label _paramFlowStepInfo;
        // 引数フローパネル内に置く確定ボタン。最終ステップでは「実行」、それ以外は「次へ」。
        // モバイル (WebGL) でソフトキーボードの Enter が効かない場合の確定手段として常時表示する。
        private Button _paramFlowSubmitButton;
        private Label _paramFlowStepDesc;
        private VisualElement _paramFlowEditorHost;
        private Label _paramFlowHint;

        // Scenario モード用の状態。
        // _scenarioSnapshot は registry から最新を引いたもの (検索クエリで絞り込み済み)。
        // _scenarioSelectedIndex は ListView 上の選択インデックス。
        // _scenarioQuery はパス部分一致のフィルタクエリ。
        // _scenarioResultView は最新のシナリオ結果を表示する dedicated VisualElement。
        // _scenarioRunning が true の間は Run ボタンを無効化して二重起動を防ぐ。
        private readonly List<ScenarioDescriptor> _scenarioSnapshot = new List<ScenarioDescriptor>();
        private int _scenarioSelectedIndex = 0;
        private string _scenarioQuery = "";
        private ScenarioResultView _scenarioResultView;
        private bool _scenarioRunning;

        // Scenario タブの「Steps」列に表示するステップ数のキャッシュ。
        // ListView の bindItem は仮想化のため可視範囲のリサイクル + Rebuild/RefreshItems の
        // たびに走り、各 bind で StepsFactory を呼ぶとリフレクション越しにユーザー定義シナリオ
        // 生成メソッドが何度も起動してしまう (yield 中の副作用も毎回発火)。
        // RefreshScenarioSnapshot 時に Path -> ステップ数を 1 度だけ計算してここに保持し、
        // BindScenarioRow は TryGetValue で読み取るだけにする。
        private readonly Dictionary<string, int> _scenarioStepCounts
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Phase 5a: ListView の "Current" 列に表示される ObservableField への購読。
        // Path をキーに dedupe し、値変更時は ListView 全体を RefreshItems() で再描画する。
        // Detach / モード切替 / 検索結果更新のたびに張り直す。
        private readonly Dictionary<string, IDisposable> _listFieldSubs
            = new Dictionary<string, IDisposable>(StringComparer.OrdinalIgnoreCase);

        public event Action CloseRequested;

        public PaletteView(PaletteController controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            AddToClassList("palette-view-host");
            style.flexGrow = 1;

            var uxml = Resources.Load<VisualTreeAsset>("Palette");
            if (uxml == null) throw new InvalidOperationException("Palette.uxml not found in Resources.");
            uxml.CloneTree(this);

            // 注: 以前は "Palette" で StyleSheet をロードしていたが、Palette.uxml が生成する
            // inlineStyle と Resources.Load の名前空間が衝突して uxml 由来の空 StyleSheet が返ってきていた。
            // .uss を PaletteStyles.uss にリネームして名前衝突を避ける (Phase 3 修正)。
            var variables = Resources.Load<StyleSheet>("PaletteVariables");
            var palette = Resources.Load<StyleSheet>("PaletteStyles");
            if (variables != null) styleSheets.Add(variables);
            if (palette != null) styleSheets.Add(palette);

            BindElements();
            BuildTabs();
            ForceLayoutStyles();
            WireEvents();

            // controller.StateChanged / InvocationStore.Changed の購読は Attach/Detach で開閉する。
            // EditorWindow を閉じた後に静的シングルトン (InvocationStore) や controller が破棄済みの View を
            // 呼び続けるとリーク + NRE になるため、パネル detach 時に必ず外す。
            RegisterCallback<AttachToPanelEvent>(OnAttachedToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);

            OnStateChanged();
        }

        public new void Focus()
        {
            // パレットを開く操作 (トグルキー / ボタンタップ) は新鮮なユーザージェスチャ内で
            // 走るため、モバイルブラウザも programmatic Focus に追従して soft keyboard を
            // 開いてくれる (ユーザー要望: パレットを開いたら即座に入力開始したい)。
            // 「貼り付き」問題が出るのは soft keyboard が既に開いていた状態から
            // dismiss → 別要素を Focus、というシナリオ (Enter→param flow など) なので、
            // ここでは IsTouchDevice 判定をしない。
            // Runtime UIDocument の初回 Show 直後はレイアウトが完了していないことがあり、
            // 同期的に Focus() を呼んでも当たらないケースがある。schedule で次フレーム以降に
            // 確実にフォーカスを当てるよう遅延させる。Editor 側でも害はない (1 フレ遅れるだけ)。
            schedule.Execute(() => _searchInput?.Focus()).ExecuteLater(0);
        }

        // ------------------------------------------------------------
        // セットアップ
        // ------------------------------------------------------------

        private void BindElements()
        {
            _tabsBar = this.Q<VisualElement>("palette-tabs");
            _searchInput = this.Q<TextField>("search-input");
            _resultsList = this.Q<ListView>("results-list");
            _bottom = this.Q<VisualElement>("palette-bottom");
            _bottomCmd = this.Q<Label>("bottom-cmd");
            _bottomStatus = this.Q<Label>("bottom-status");
            _argumentPanel = this.Q<VisualElement>("argument-panel");
            _runButton = this.Q<Button>("run-button");

            // 検索ヘッダ右端に Submit ボタンを差し込む。モバイル WebGL のソフトキーボードでは
            // Enter / NavigationSubmit が発火しない端末があるため、タップで確定できる導線を
            // UI 上に常時用意しておく。Editor / 物理キーボード環境でも同じボタンが見えるが、
            // 機能は同じなので害はない。
            var header = this.Q<VisualElement>("palette-header");
            if (header != null && _searchInput != null)
            {
                _headerSubmitButton = new Button { name = "header-submit-button", text = "▶" };
                _headerSubmitButton.AddToClassList("palette-header-submit");
                _headerSubmitButton.clicked += () => { var _ = ExecuteSelectedAsync(); };
                header.Add(_headerSubmitButton);
            }

            // モバイル (WebGL) のソフトキーボードでは「完了 / Return」が KeyDownEvent や
            // NavigationSubmitEvent を発火させず、入力欄の blur (= フォーカス解除) のみが起きる
            // 端末がある。そのため、検索バーの FocusOutEvent を「Enter 押下」相当として拾う。
            // 別要素 (行 / Submit ボタン) にフォーカスが移ったケースは除外するため次フレームで確認する。
            if (_searchInput != null)
            {
                _searchInput.RegisterCallback<FocusOutEvent>(OnSearchInputFocusOut);
                // UIToolkit のキーボードナビゲーション (Project Settings → Input System の Navigate action) は
                // 既定で WASD を NavigationMoveEvent に変換するため、検索欄で w/a/s/d を打つと文字入力と同時に
                // 別 element へ focus が飛んでしまう (ルートの NavigationMove 抑制は internal navigation を
                // 止め切れない場合がある)。TextField focus 中だけ全方向の NavigationMove を握りつぶす。
                _searchInput.RegisterCallback<NavigationMoveEvent>(
                    evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
            }

            // Phase 5a: ObservableFieldsView を引数パネルの直前 (上) に挿入。
            // 選択コマンドが変わるたびに ShowFor(path) で再構築。
            _observableFields = new ObservableFieldsView();
            var argumentParent = _argumentPanel?.parent;
            if (argumentParent != null)
            {
                var indexOfArguments = argumentParent.IndexOf(_argumentPanel);
                argumentParent.Insert(indexOfArguments, _observableFields);
            }

            var resultViewPlaceholder = this.Q<VisualElement>("result-view");
            _resultView = new ResultView();
            resultViewPlaceholder?.Add(_resultView);

            // Scenario タブ用の結果表示。Commands タブの ResultView と同じ親に追加し、
            // モード切替で表示を入れ替える。
            _scenarioResultView = new ScenarioResultView();
            _scenarioResultView.style.display = DisplayStyle.None;
            resultViewPlaceholder?.Add(_scenarioResultView);

            // Log モード用のスタックトレース表示 Label。bottom 内に追加 (visibility はモードで切替)。
            _logStackLabel = new Label();
            _logStackLabel.style.color = new Color(0.55f, 0.55f, 0.55f, 1f);
            _logStackLabel.style.whiteSpace = WhiteSpace.Normal;
            _logStackLabel.style.fontSize = 11;
            _logStackLabel.style.marginTop = 6;
            _logStackLabel.style.maxHeight = 160;
            _logStackLabel.style.display = DisplayStyle.None;
            _bottom.Add(_logStackLabel);

            // VSCode 風に description-first の 2 段表示にしたため、テーブル列ヘッダは廃止する。
            // 互換のため UXML 側の要素は残しているが表示はしない。
            var columnHeader = this.Q<VisualElement>("palette-column-header");
            if (columnHeader != null) columnHeader.style.display = DisplayStyle.None;

            // 行は title + subtitle の 2 段構造。Runtime の Label metrics が乗っても破綻しない高さに調整。
            _resultsList.fixedItemHeight = 36;
            _resultsList.makeItem = MakeRow;
            _resultsList.bindItem = BindRow;
            _resultsList.selectionType = SelectionType.Single;

            BuildParamFlowPanel();
        }

        // VSCode の QuickPick 的な複数ステップ入力 UI。results-list と同じ位置に重ねず、
        // results-list の直前に挿入してフロー中だけ display:Flex にする。
        private void BuildParamFlowPanel()
        {
            _paramFlowPanel = new VisualElement { name = "palette-param-flow" };
            _paramFlowPanel.AddToClassList("palette-param-flow");
            _paramFlowPanel.style.display = DisplayStyle.None;
            _paramFlowPanel.style.flexDirection = FlexDirection.Column;
            _paramFlowPanel.style.flexShrink = 0;
            _paramFlowPanel.style.paddingLeft = 12;
            _paramFlowPanel.style.paddingRight = 12;
            _paramFlowPanel.style.paddingTop = 12;
            _paramFlowPanel.style.paddingBottom = 12;

            _paramFlowCmdLabel = new Label();
            _paramFlowCmdLabel.AddToClassList("palette-param-flow-cmd");
            _paramFlowPanel.Add(_paramFlowCmdLabel);

            _paramFlowBreadcrumbs = new VisualElement { name = "palette-param-flow-breadcrumbs" };
            _paramFlowBreadcrumbs.AddToClassList("palette-param-flow-breadcrumbs");
            _paramFlowBreadcrumbs.style.flexDirection = FlexDirection.Row;
            _paramFlowBreadcrumbs.style.flexWrap = Wrap.Wrap;
            _paramFlowPanel.Add(_paramFlowBreadcrumbs);

            _paramFlowStepInfo = new Label();
            _paramFlowStepInfo.AddToClassList("palette-param-flow-step");
            _paramFlowPanel.Add(_paramFlowStepInfo);

            _paramFlowStepDesc = new Label();
            _paramFlowStepDesc.AddToClassList("palette-param-flow-step-desc");
            _paramFlowStepDesc.style.whiteSpace = WhiteSpace.Normal;
            _paramFlowPanel.Add(_paramFlowStepDesc);

            _paramFlowEditorHost = new VisualElement { name = "palette-param-flow-editor" };
            _paramFlowEditorHost.AddToClassList("palette-param-flow-editor");
            _paramFlowPanel.Add(_paramFlowEditorHost);

            // 次へ / 実行ボタン。ShowCurrentParamFlowStep でラベルを最終ステップなら「実行」へ切り替える。
            // モバイル WebGL ではこのタップが Enter の代替手段になる。
            _paramFlowSubmitButton = new Button { name = "palette-param-flow-submit", text = "次へ ▶" };
            _paramFlowSubmitButton.AddToClassList("palette-param-flow-submit");
            _paramFlowSubmitButton.clicked += () => { var _ = AdvanceParamFlowAsync(); };
            _paramFlowPanel.Add(_paramFlowSubmitButton);

            _paramFlowHint = new Label("Enter または「次へ」で確定 / Esc で戻る");
            _paramFlowHint.AddToClassList("palette-param-flow-hint");
            _paramFlowPanel.Add(_paramFlowHint);

            // results-list の直前に差し込む。
            var root = _resultsList.parent;
            if (root != null)
            {
                var idx = root.IndexOf(_resultsList);
                root.Insert(idx, _paramFlowPanel);
            }
        }

        // パネルへの attach 時にイベント購読を開始する。重複防止のため一度外してから足す
        // (Attach は EditorWindow の rootVisualElement 接続のたびに発火しうる)。
        private void OnAttachedToPanel(AttachToPanelEvent _)
        {
            _controller.StateChanged -= OnStateChanged;
            _controller.StateChanged += OnStateChanged;
            InvocationStore.Instance.Changed -= OnInvocationStoreChanged;
            InvocationStore.Instance.Changed += OnInvocationStoreChanged;
        }

        // detach 時に必ず購読解除。シングルトンの InvocationStore に View が残るとウィンドウを閉じても
        // ハンドラが呼ばれ続け、破棄済み UI への参照で NRE になる。
        private void OnDetachedFromPanel(DetachFromPanelEvent _)
        {
            _controller.StateChanged -= OnStateChanged;
            InvocationStore.Instance.Changed -= OnInvocationStoreChanged;
            DisposeListFieldSubs();
        }

        private void OnInvocationStoreChanged()
        {
            if (_mode == ViewMode.Commands) return;
            schedule.Execute(() => { RefreshInvocationSnapshot(); UpdateView(); }).ExecuteLater(0);
        }

        // 4 つの固定タブ (Command / Scenario / Log / History) を生成する。
        //   Command:  全コマンド (新規実行)
        //   Scenario: 登録シナリオの一覧 (Run でステップ列を順次実行)
        //   Log:      起動履歴の詳細閲覧 (引数 / Debug.Log / スタックトレース)
        //   History:  起動履歴の再実行特化 (同じ引数で Run)
        private void BuildTabs()
        {
            _tabs.Clear();
            _tabsBar.Clear();
            AddTab("Command");
            AddTab("Scenario");
            AddTab("Log");
            AddTab("History");
            ActivateTab(0);
        }

        private void AddTab(string label)
        {
            var btn = new Button { text = label };
            btn.AddToClassList("palette-tab");
            btn.style.flexShrink = 0;
            var index = _tabs.Count;
            btn.clicked += () => ActivateTab(index);
            _tabsBar.Add(btn);
            _tabs.Add(btn);
        }

        private void ActivateTab(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;
            for (var i = 0; i < _tabs.Count; i++)
            {
                if (i == index) _tabs[i].AddToClassList("palette-tab-active");
                else _tabs[i].RemoveFromClassList("palette-tab-active");
            }
            ViewMode newMode;
            switch (index)
            {
                case 1: newMode = ViewMode.Scenarios; break;
                case 2: newMode = ViewMode.Logs; break;
                case 3: newMode = ViewMode.History; break;
                default: newMode = ViewMode.Commands; break;
            }
            _mode = newMode;
            if (newMode == ViewMode.Commands)
            {
                // SetFilter が StateChanged を発火し OnStateChanged → UpdateView へ繋がるので、
                // ここで明示的に UpdateView を呼ぶと Rebuild が二重に走る。
                _controller.SetFilter("All", null);
            }
            else if (newMode == ViewMode.Scenarios)
            {
                RefreshScenarioSnapshot();
                UpdateView();
            }
            else
            {
                RefreshInvocationSnapshot();
                UpdateView();
            }
        }

        // ScenarioRegistry から最新の登録一覧を取り直す。検索クエリで Path 部分一致フィルタを適用する。
        // 同時にステップ数キャッシュ (_scenarioStepCounts) も再構築して、bind ループでの
        // StepsFactory 再起動を回避する。
        private void RefreshScenarioSnapshot()
        {
            _scenarioSnapshot.Clear();
            _scenarioStepCounts.Clear();
            var all = ScenarioRegistry.Default.All;
            for (var i = 0; i < all.Count; i++)
            {
                var d = all[i];
                if (string.IsNullOrEmpty(_scenarioQuery)
                    || (d.Path != null && d.Path.IndexOf(_scenarioQuery, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    _scenarioSnapshot.Add(d);
                    // ステップ数の計算は副作用 (Debug.Log・状態変更等) を伴い得るため、
                    // snapshot 構築時の 1 回だけにする。BindScenarioRow ではこのキャッシュを参照するだけ。
                    _scenarioStepCounts[d.Path] = CountScenarioSteps(d);
                }
            }
            if (_scenarioSelectedIndex >= _scenarioSnapshot.Count)
                _scenarioSelectedIndex = Math.Max(0, _scenarioSnapshot.Count - 1);
        }

        // ステップ列を 1 度だけ enumerate して数える。副作用付きシナリオに当たる可能性があるが、
        // ListScenariosEndpoint と同じ仕様 (表示用の概算値) なので許容。
        // 失敗時は -1 を返してテーブル上は "?" と表示する。
        private static int CountScenarioSteps(ScenarioDescriptor d)
        {
            try
            {
                object instance = null;
                if (!d.IsStatic)
                {
                    instance = LiminalPalette.InstanceResolver.Resolve(d.DeclaringType);
                    if (instance == null) return -1;
                }
                var count = 0;
                foreach (var s in d.StepsFactory(instance))
                {
                    if (s == null) continue;
                    count++;
                }
                return count;
            }
            catch
            {
                return -1;
            }
        }

        // InvocationStore から最新を新しい順に取り直す。検索クエリで Path / args 部分一致フィルタを適用する。
        // History モードのときはシナリオ由来エントリ (個別ステップ + 集約) を除外する。
        // Log モードではシナリオ由来も含めて全件表示する (詳細閲覧用途)。
        private void RefreshInvocationSnapshot()
        {
            _invocationSnapshot.Clear();
            var entries = InvocationStore.Instance.Entries;
            var hideFromScenario = _mode == ViewMode.History;
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                if (hideFromScenario && e.IsFromScenario) continue;
                if (string.IsNullOrEmpty(_invocationQuery)
                    || (e.Path != null && e.Path.IndexOf(_invocationQuery, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    _invocationSnapshot.Add(e);
                }
            }
            if (_invocationSelectedIndex >= _invocationSnapshot.Count) _invocationSelectedIndex = Math.Max(0, _invocationSnapshot.Count - 1);
        }

        // UXML 由来要素のレイアウトを確実に効かせるためのインラインスタイル設定。
        private void ForceLayoutStyles()
        {
            var root = this.Q<VisualElement>("palette-root");
            if (root != null)
            {
                root.style.flexGrow = 1;
                root.style.flexDirection = FlexDirection.Column;
            }

            if (_tabsBar != null)
            {
                _tabsBar.style.flexDirection = FlexDirection.Row;
                _tabsBar.style.flexShrink = 0;
                _tabsBar.style.flexWrap = Wrap.Wrap;
            }

            var header = this.Q<VisualElement>("palette-header");
            if (header != null)
            {
                header.style.flexShrink = 0;
                header.style.flexDirection = FlexDirection.Row;
                header.style.alignItems = Align.Center;
            }
            if (_searchInput != null)
            {
                _searchInput.style.minHeight = 24;
                _searchInput.style.flexGrow = 1;
                _searchInput.style.flexShrink = 1;
            }
            if (_headerSubmitButton != null)
            {
                // モバイル想定のタップターゲットなので高さ・余白は広めにとる。
                _headerSubmitButton.style.flexShrink = 0;
                _headerSubmitButton.style.minHeight = 28;
                _headerSubmitButton.style.minWidth = 40;
                _headerSubmitButton.style.marginLeft = 6;
            }

            // 列ヘッダは廃止 (BindElements で display:none)。レイアウト指定は不要。

            if (_resultsList != null)
            {
                _resultsList.style.flexGrow = 1;
                _resultsList.style.flexShrink = 1;
                _resultsList.style.minHeight = 0;
                // 下部 overlay (Position.Absolute, bottom:0) の高さ分だけ ListView 末尾の項目が
                // 隠れないよう padding-bottom で逃がす。overlay の概算高さに合わせて 200px。
                _resultsList.style.paddingBottom = 200;
            }

            if (_bottom != null)
            {
                // 下部は overlay として absolute 配置。リストに重ねるが背景を半透明黒にして
                // 後ろのリストが薄く見える状態を保つ。左右・下端は palette-root 端に密着 (フルブリード)。
                _bottom.style.position = Position.Absolute;
                _bottom.style.left = 0;
                _bottom.style.right = 0;
                _bottom.style.bottom = 0;
                _bottom.style.flexDirection = FlexDirection.Column;
                _bottom.style.backgroundColor = new Color(0f, 0f, 0f, 0.85f);
                _bottom.style.borderTopWidth = 1;
                _bottom.style.borderTopColor = new Color(0.34f, 0.61f, 0.84f, 1f);
                _bottom.style.paddingLeft = 10;
                _bottom.style.paddingRight = 10;
                _bottom.style.paddingTop = 8;
                _bottom.style.paddingBottom = 8;
            }

            if (_argumentPanel != null) _argumentPanel.style.flexDirection = FlexDirection.Column;

            var actions = this.Q<VisualElement>("palette-bottom-actions");
            if (actions != null)
            {
                // Run ボタンを右下に配置するため flex-end。
                actions.style.flexDirection = FlexDirection.Row;
                actions.style.alignItems = Align.Center;
                actions.style.justifyContent = Justify.FlexEnd;
            }

            // Run ボタンの色は USS class が UXML 由来 Button では塗り直されないことがあるため、
            // 確実に緑にするためインラインで強制する。
            if (_runButton != null)
            {
                var green = new Color(0.25f, 0.6f, 0.25f, 1f);
                _runButton.style.backgroundColor = green;
                _runButton.style.color = Color.white;
                _runButton.style.unityFontStyleAndWeight = FontStyle.Bold;
                _runButton.style.borderTopWidth = 0;
                _runButton.style.borderBottomWidth = 0;
                _runButton.style.borderLeftWidth = 0;
                _runButton.style.borderRightWidth = 0;
                _runButton.style.borderTopLeftRadius = 3;
                _runButton.style.borderTopRightRadius = 3;
                _runButton.style.borderBottomLeftRadius = 3;
                _runButton.style.borderBottomRightRadius = 3;
                _runButton.style.paddingLeft = 14;
                _runButton.style.paddingRight = 14;
                _runButton.style.paddingTop = 5;
                _runButton.style.paddingBottom = 5;
            }
        }

        private void WireEvents()
        {
            _searchInput.RegisterValueChangedCallback(e =>
            {
                if (_mode == ViewMode.Commands)
                {
                    _controller.SetQuery(e.newValue);
                }
                else if (_mode == ViewMode.Scenarios)
                {
                    _scenarioQuery = e.newValue ?? "";
                    RefreshScenarioSnapshot();
                    UpdateView();
                }
                else
                {
                    _invocationQuery = e.newValue ?? "";
                    RefreshInvocationSnapshot();
                    UpdateView();
                }
            });

            _resultsList.selectedIndicesChanged += indices =>
            {
                foreach (var idx in indices)
                {
                    if (_mode == ViewMode.Commands)
                    {
                        _controller.SetSelection(idx);
                    }
                    else if (_mode == ViewMode.Scenarios)
                    {
                        _scenarioSelectedIndex = idx;
                        _resultsList.RefreshItems();
                        UpdateBottomScenarios();
                    }
                    else
                    {
                        _invocationSelectedIndex = idx;
                        // BindRow が _invocationSelectedIndex を見て行ハイライトの class を付け直すため、
                        // 選択変更後は ListView を再バインドしないとハイライトが古いまま残る。
                        _resultsList.RefreshItems();
                        if (_mode == ViewMode.Logs) UpdateBottomLogs();
                        else UpdateBottomHistory();
                    }
                    break;
                }
            };

            _resultsList.itemsChosen += chosen =>
            {
                if (_mode == ViewMode.Commands || _mode == ViewMode.History || _mode == ViewMode.Scenarios)
                {
                    var _ = ExecuteSelectedAsync();
                }
            };

            _runButton.clicked += () =>
            {
                var _ = ExecuteSelectedAsync();
            };

            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            // モバイルのソフトキーボードや UIToolkit の汎用ナビゲーションでは KeyDownEvent が
            // 発火しないため、Enter / Esc 相当の操作は NavigationSubmit / NavigationCancel で受ける。
            // (物理キーボードでは KeyDown と Navigation の両方が走るが、Return / Escape は
            //  OnKeyDown 側で扱わないようにして二重発火を避ける。)
            RegisterCallback<NavigationSubmitEvent>(OnNavigationSubmit, TrickleDown.TrickleDown);
            RegisterCallback<NavigationCancelEvent>(OnNavigationCancel, TrickleDown.TrickleDown);

            // 矢印キーは OnKeyDown が責任を持って MoveSelection するので、
            // panel が解釈する NavigationMove は全方向で握りつぶす。
            // 放置すると ListView の selectedIndex が別経路で進み「1 押下で 2 行進む」二重選択が起き、
            // Runtime では uGUI EventSystem の Move axis (WASD) が panel に NavigationMove を
            // 配送して検索欄から focus が抜ける。Left/Right の TextField キャレット移動は
            // KeyDownEvent 経由で別途効くため、NavigationMove を全部止めても害はない。
            //
            // 重要: StopImmediatePropagation だけでは focusController の default action
            // (Move 方向への focus 移動) は止まらないため、 PreventDefault も併用する。
            RegisterCallback<NavigationMoveEvent>(evt =>
            {
                evt.StopImmediatePropagation();
                evt.PreventDefault();
            }, TrickleDown.TrickleDown);
        }

        // ------------------------------------------------------------
        // ListView 行 (VSCode コマンドパレット風: 説明を主、Path を副に表示)
        //   [• mark] [title (description or path)]                [current]
        //                [path (subtitle, dim)]
        // ------------------------------------------------------------

        private VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("palette-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.flexShrink = 0;
            row.style.flexGrow = 1;

            var historyMark = new Label("•");
            historyMark.AddToClassList("palette-row-history-mark");
            historyMark.name = "history-mark";
            historyMark.style.width = 14;
            historyMark.style.flexShrink = 0;
            row.Add(historyMark);

            // 説明 + path を縦積みするコンテナ。
            var text = new VisualElement();
            text.name = "row-text";
            text.AddToClassList("palette-row-text");
            text.style.flexDirection = FlexDirection.Column;
            text.style.flexGrow = 1;
            text.style.flexShrink = 1;
            text.style.overflow = Overflow.Hidden;

            var title = new Label();
            title.AddToClassList("palette-row-title");
            title.name = "row-title";
            title.enableRichText = true;
            title.style.whiteSpace = WhiteSpace.NoWrap;
            title.style.overflow = Overflow.Hidden;
            title.style.textOverflow = TextOverflow.Ellipsis;
            text.Add(title);

            var subtitle = new Label();
            subtitle.AddToClassList("palette-row-subtitle");
            subtitle.name = "row-subtitle";
            subtitle.enableRichText = true;
            subtitle.style.whiteSpace = WhiteSpace.NoWrap;
            subtitle.style.overflow = Overflow.Hidden;
            subtitle.style.textOverflow = TextOverflow.Ellipsis;
            text.Add(subtitle);

            row.Add(text);

            // 右端のメタ情報 (Current 値 / Time / Steps 数 など)。
            var meta = new Label();
            meta.AddToClassList("palette-row-meta");
            meta.name = "row-meta";
            meta.style.flexShrink = 0;
            meta.style.marginLeft = 8;
            meta.style.whiteSpace = WhiteSpace.NoWrap;
            meta.style.overflow = Overflow.Hidden;
            meta.style.textOverflow = TextOverflow.Ellipsis;
            meta.style.maxWidth = 160;
            row.Add(meta);

            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            if (_mode == ViewMode.Scenarios)
            {
                BindScenarioRow(row, index);
                return;
            }
            if (_mode == ViewMode.Logs)
            {
                BindInvocationRow(row, index, showArgs: false);
                return;
            }
            if (_mode == ViewMode.History)
            {
                BindInvocationRow(row, index, showArgs: true);
                return;
            }

            if (index < 0 || index >= _controller.Results.Count) return;
            var ranked = _controller.Results[index];

            var historyMark = row.Q<Label>("history-mark");
            historyMark.style.visibility = ranked.FromHistory ? Visibility.Visible : Visibility.Hidden;

            var title = row.Q<Label>("row-title");
            var subtitle = row.Q<Label>("row-subtitle");
            var meta = row.Q<Label>("row-meta");

            // 説明は必須前提。1 行目に Description、2 行目に Path (検索ハイライト付き) を必ず表示する。
            title.text = ranked.Descriptor.Description ?? "";
            title.style.color = StyleKeyword.Null;
            subtitle.text = BuildHighlightedPath(ranked.Descriptor.Path, ranked.MatchedIndices);
            subtitle.style.color = StyleKeyword.Null;

            meta.text = FormatCurrentColumn(ranked.Descriptor);
            meta.style.color = StyleKeyword.Null;

            row.RemoveFromClassList("palette-row-selected");
            if (index == _controller.SelectedIndex) row.AddToClassList("palette-row-selected");
        }

        // Scenario タブの 1 行レンダリング。説明を主、Path を副、右端にステップ数。
        private void BindScenarioRow(VisualElement row, int index)
        {
            if (index < 0 || index >= _scenarioSnapshot.Count) return;
            var d = _scenarioSnapshot[index];

            var historyMark = row.Q<Label>("history-mark");
            historyMark.style.visibility = Visibility.Hidden;

            var title = row.Q<Label>("row-title");
            var subtitle = row.Q<Label>("row-subtitle");
            var meta = row.Q<Label>("row-meta");

            // 説明は必須前提。1 行目に Description、2 行目に Path を必ず表示する。
            title.text = d.Description ?? "";
            title.style.color = StyleKeyword.Null;
            subtitle.text = d.Path;
            subtitle.style.color = StyleKeyword.Null;

            var count = _scenarioStepCounts.TryGetValue(d.Path, out var c) ? c : -1;
            meta.text = count < 0 ? "?" : $"{count.ToString(CultureInfo.InvariantCulture)} steps";
            meta.style.color = StyleKeyword.Null;

            row.RemoveFromClassList("palette-row-selected");
            if (index == _scenarioSelectedIndex) row.AddToClassList("palette-row-selected");
        }

        // Logs / History 共通の行レンダリング。
        // 主: Path (成否で色付け)
        // 副: showArgs=false (Logs) → Status / showArgs=true (History) → Args 要約
        // メタ: Time (HH:mm:ss)
        private void BindInvocationRow(VisualElement row, int index, bool showArgs)
        {
            if (index < 0 || index >= _invocationSnapshot.Count) return;
            var inv = _invocationSnapshot[index];

            var historyMark = row.Q<Label>("history-mark");
            historyMark.style.visibility = Visibility.Hidden;

            var title = row.Q<Label>("row-title");
            var subtitle = row.Q<Label>("row-subtitle");
            var meta = row.Q<Label>("row-meta");

            title.text = inv.Path;
            title.style.color = inv.Result.Success
                ? new Color(0.92f, 0.92f, 0.92f, 1f)
                : new Color(0.92f, 0.45f, 0.45f, 1f);

            subtitle.style.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            if (showArgs)
            {
                subtitle.text = FormatArgsSummary(inv.Args);
            }
            else
            {
                subtitle.text = inv.Result.Success
                    ? $"OK ({inv.Result.Duration.TotalMilliseconds:F1}ms)"
                    : $"FAIL — {inv.Result.Error}";
            }

            meta.text = inv.TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
            meta.style.color = new Color(0.55f, 0.55f, 0.55f, 1f);

            row.RemoveFromClassList("palette-row-selected");
            if (index == _invocationSelectedIndex) row.AddToClassList("palette-row-selected");
        }

        // Args の 1 行要約。"key=val, key2=val2" 形式で長すぎたら切り詰める。
        private static string FormatArgsSummary(IReadOnlyDictionary<string, object> args)
        {
            if (args == null || args.Count == 0) return "(no args)";
            var sb = new StringBuilder();
            var first = true;
            foreach (var kv in args)
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(kv.Key).Append('=').Append(FormatArgValue(kv.Value));
                if (sb.Length > 80) { sb.Length = 80; sb.Append("…"); break; }
            }
            return sb.ToString();
        }

        private static string FormatArgValue(object v)
        {
            if (v == null) return "null";
            if (v is string s) return $"\"{s}\"";
            if (v is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
            return v.ToString();
        }

        // "Current" 列の値:
        //   1) コマンドの親階層 (= Path の最後の "/" 前) prefix に紐づく [LiminalObservableField] が
        //      あればその現在値を表示。値変化は RebuildListFieldSubscriptions の Subscribe で
        //      RefreshItems() に流れ、行が再描画される。
        //   2) ObservableField が無いコマンドは従来通り「第 1 引数の DefaultValue」を表示。
        private static string FormatCurrentColumn(CommandDescriptor cmd)
        {
            var path = cmd.Path;
            var lastSlash = path.LastIndexOf('/');
            if (lastSlash > 0)
            {
                var prefix = path.Substring(0, lastSlash);
                var registry = ObservableFieldRegistry.Default;
                if (registry != null)
                {
                    var matches = registry.FindByPathPrefix(prefix);
                    if (matches.Count > 0)
                    {
                        // 同 prefix に複数 Field がある場合は先頭を採用 (列が 1 つしかないため)。
                        // 利用側が衝突を避けたければ Path 命名で区別する想定。
                        var d = matches[0];
                        // IsStatic な field は VContainer 登録不要 (静的 utility 想定)。instance=null で読む。
                        var instance = d.IsStatic ? null : LiminalPalette.InstanceResolver.Resolve(d.DeclaringType);
                        if (d.IsStatic || instance != null)
                        {
                            try
                            {
                                var v = d.ReadCurrent(instance);
                                return v == null ? "-" : TypeConverterRegistry.ToDisplayString(v);
                            }
                            catch
                            {
                                // 値読み取り失敗時は黙って DefaultValue 表示にフォールバックする。
                            }
                        }
                    }
                }
            }

            if (cmd.Parameters.Count == 0) return "";
            var p = cmd.Parameters[0];
            if (!p.HasDefault) return "";
            if (p.DefaultValue == null) return "null";
            return p.DefaultValue is IFormattable f
                ? f.ToString(null, CultureInfo.InvariantCulture)
                : p.DefaultValue.ToString();
        }

        // 現在表示中の Results に含まれるコマンドの parent prefix 集合から
        // ObservableField を引き当て、Subscribe して値変更時に ListView を再描画する。
        // 既存購読は全 Dispose してから張り直す (差分管理は不要なほど Field 数は少ない前提)。
        private void RebuildListFieldSubscriptions()
        {
            DisposeListFieldSubs();
            if (_mode != ViewMode.Commands) return;

            var registry = ObservableFieldRegistry.Default;
            if (registry == null || _resultsList == null) return;

            // 同じ Field を 2 重 Subscribe しないため Path で dedupe する。
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = _controller.Results;
            for (var ri = 0; ri < results.Count; ri++)
            {
                var p = results[ri].Descriptor.Path;
                var ls = p.LastIndexOf('/');
                if (ls <= 0) continue;
                var prefix = p.Substring(0, ls);
                var matches = registry.FindByPathPrefix(prefix);
                for (var mi = 0; mi < matches.Count; mi++)
                {
                    var d = matches[mi];
                    if (!seen.Add(d.Path)) continue;
                    // IsStatic な field は instance=null で Subscribe する (VContainer 登録不要)。
                    var instance = d.IsStatic ? null : LiminalPalette.InstanceResolver.Resolve(d.DeclaringType);
                    if (!d.IsStatic && instance == null) continue;
                    try
                    {
                        // 値変更時は ListView 行を再 Bind させる。RefreshItems は可視範囲のみ
                        // 再描画するため軽量で、初回 Subscribe 直後の push も含めて実害無し。
                        var sub = d.Subscribe(instance, _ => _resultsList?.RefreshItems());
                        _listFieldSubs[d.Path] = sub;
                    }
                    catch
                    {
                        // Subscribe 失敗時は静かに諦める (ReadCurrent 経路で初期値だけは出る)。
                    }
                }
            }
        }

        private void DisposeListFieldSubs()
        {
            foreach (var kv in _listFieldSubs)
            {
                try { kv.Value?.Dispose(); } catch { /* swallow */ }
            }
            _listFieldSubs.Clear();
        }

        // ------------------------------------------------------------
        // 状態反映
        // ------------------------------------------------------------

        private void OnStateChanged()
        {
            // controller.StateChanged は Commands モードのときだけ ListView を更新する。
            // Logs / History モードの最新化は InvocationStore.Changed → OnInvocationStoreChanged で行う。
            if (_mode == ViewMode.Commands) UpdateView();
        }

        // モードに応じて itemsSource と bottom を更新する単一エントリポイント。
        private void UpdateView()
        {
            switch (_mode)
            {
                case ViewMode.Scenarios:
                    _resultsList.itemsSource = _scenarioSnapshot;
                    _resultsList.Rebuild();
                    if (_scenarioSnapshot.Count > 0)
                    {
                        _resultsList.selectedIndex = _scenarioSelectedIndex;
                        _resultsList.ScrollToItem(_scenarioSelectedIndex);
                    }
                    UpdateBottomScenarios();
                    break;
                case ViewMode.Logs:
                case ViewMode.History:
                    _resultsList.itemsSource = _invocationSnapshot;
                    _resultsList.Rebuild();
                    if (_invocationSnapshot.Count > 0)
                    {
                        _resultsList.selectedIndex = _invocationSelectedIndex;
                        _resultsList.ScrollToItem(_invocationSelectedIndex);
                    }
                    if (_mode == ViewMode.Logs) UpdateBottomLogs();
                    else UpdateBottomHistory();
                    break;
                default:
                    _resultsList.itemsSource = (System.Collections.IList)_controller.Results;
                    _resultsList.Rebuild();
                    if (_controller.Results.Count > 0)
                    {
                        _resultsList.selectedIndex = _controller.SelectedIndex;
                        _resultsList.ScrollToItem(_controller.SelectedIndex);
                    }
                    // ListView の "Current" 列を ObservableField 連動で動的に更新するための
                    // 購読セットを、現在表示中のコマンド集合に合わせて張り直す。
                    RebuildListFieldSubscriptions();
                    UpdateBottom();
                    break;
            }
        }

        // Log モード時の bottom: 選択 invocation の引数 / 出力 / スタックトレースを詳細表示。Run は出さない。
        private void UpdateBottomLogs()
        {
            HideCommandModeWidgets();
            _logStackLabel.style.display = DisplayStyle.Flex;

            if (_invocationSnapshot.Count == 0)
            {
                _bottomCmd.text = "Log: (no entries)";
                _bottomStatus.text = "";
                _logStackLabel.text = "";
                return;
            }
            var inv = _invocationSnapshot[Mathf.Clamp(_invocationSelectedIndex, 0, _invocationSnapshot.Count - 1)];
            _bottomCmd.text = inv.Result.Success
                ? $"Cmd: {inv.Path}  →  OK ({inv.Result.Duration.TotalMilliseconds:F1}ms)"
                : $"Cmd: {inv.Path}  →  FAIL — {inv.Result.Error}";
            _bottomStatus.text = $"Time: {inv.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}    Args: {FormatArgsSummary(inv.Args)}";

            // 出力 (captured Debug.Log) + スタックトレースを 1 つの label にまとめる。
            var sb = new StringBuilder();
            if (inv.Result.Logs.Count > 0)
            {
                sb.AppendLine("[Output]");
                for (var i = 0; i < inv.Result.Logs.Count; i++)
                {
                    var l = inv.Result.Logs[i];
                    sb.Append("  [").Append(l.Type).Append("] ").AppendLine(l.Message);
                }
            }
            if (inv.Result.Exception != null)
            {
                sb.AppendLine("[Stack trace]");
                sb.AppendLine(inv.Result.Exception.StackTrace ?? "(none)");
            }
            _logStackLabel.text = sb.Length > 0 ? sb.ToString() : "(no output / stack trace)";
        }

        // Scenario モード時の bottom: 選択シナリオの Path + Description + Run ボタン + ScenarioResultView。
        // 引数編集欄 / Command 用 ResultView は出さない。
        private void UpdateBottomScenarios()
        {
            _argumentPanel.AddToClassList("palette-arguments-empty");
            _argumentPanel.style.display = DisplayStyle.None;
            _resultView.Clear();
            _resultView.style.display = DisplayStyle.None;
            _logStackLabel.style.display = DisplayStyle.None;
            _runButton.style.display = DisplayStyle.Flex;
            _scenarioResultView.style.display = DisplayStyle.Flex;

            if (_scenarioSnapshot.Count == 0)
            {
                _bottomCmd.text = "Scenario: (no entries)";
                _bottomStatus.text = "";
                _runButton.SetEnabled(false);
                _scenarioResultView.Clear();
                return;
            }
            var d = _scenarioSnapshot[Mathf.Clamp(_scenarioSelectedIndex, 0, _scenarioSnapshot.Count - 1)];
            _bottomCmd.text = $"Scenario: {d.Path}";
            _bottomStatus.text = string.IsNullOrEmpty(d.Description) ? "" : d.Description;
            _runButton.text = _scenarioRunning ? "Running…" : "Run Scenario";
            _runButton.SetEnabled(!_scenarioRunning);
        }

        // History モード時の bottom: 選択 invocation の Path + args summary + Run ボタンで再実行。詳細は出さない。
        private void UpdateBottomHistory()
        {
            // Run ボタンは表示する。引数編集欄 / ResultView / stack label は出さない。
            _argumentPanel.AddToClassList("palette-arguments-empty");
            _argumentPanel.style.display = DisplayStyle.None;
            _resultView.Clear();
            _resultView.style.display = DisplayStyle.None;
            _logStackLabel.style.display = DisplayStyle.None;
            _runButton.style.display = DisplayStyle.Flex;
            if (_scenarioResultView != null)
            {
                _scenarioResultView.Clear();
                _scenarioResultView.style.display = DisplayStyle.None;
            }

            if (_invocationSnapshot.Count == 0)
            {
                _bottomCmd.text = "History: (no entries)";
                _bottomStatus.text = "";
                _runButton.SetEnabled(false);
                return;
            }
            var inv = _invocationSnapshot[Mathf.Clamp(_invocationSelectedIndex, 0, _invocationSnapshot.Count - 1)];
            _bottomCmd.text = $"Cmd: {inv.Path}";
            _bottomStatus.text = $"Args: {FormatArgsSummary(inv.Args)}    Time: {inv.TimestampUtc.ToLocalTime():HH:mm:ss}";
            _runButton.SetEnabled(true);
            _runButton.text = "Run Command";
        }

        // Log / History モード共通の隠し処理。
        private void HideCommandModeWidgets()
        {
            _argumentPanel.AddToClassList("palette-arguments-empty");
            _argumentPanel.style.display = DisplayStyle.None;
            _runButton.style.display = DisplayStyle.None;
            _resultView.Clear();
            _resultView.style.display = DisplayStyle.None;
            // Scenario モード以外では Scenario 結果ビューも隠す。
            if (_scenarioResultView != null)
            {
                _scenarioResultView.Clear();
                _scenarioResultView.style.display = DisplayStyle.None;
            }
        }

        // 選択中コマンドが変わるたびに、下部パネル (Cmd / Status / 引数 / 結果) を更新する。
        private void UpdateBottom()
        {
            var cmd = _controller.SelectedCommand;
            var hasResult = _controller.LastResult != null;

            _bottomCmd.text = cmd != null ? $"Cmd: {cmd.Path}" : "Cmd:";
            _bottomStatus.text = FormatStatus(_controller.LastResult);

            // Logs モード用の要素を非表示に戻す。
            _logStackLabel.style.display = DisplayStyle.None;
            _runButton.style.display = DisplayStyle.Flex;
            // 引数入力は複数ステップフロー (TryBeginParamFlow) に統合したため、
            // 選択中コマンドのプレビューでは inline 引数パネルを表示しない。
            _argumentPanel.style.display = DisplayStyle.None;
            _resultView.style.display = DisplayStyle.Flex;
            if (_scenarioResultView != null)
            {
                _scenarioResultView.Clear();
                _scenarioResultView.style.display = DisplayStyle.None;
            }

            // SelectedCommand が変わったときだけ引数フィールドを作り直す。
            if (cmd != null && cmd.Path != _boundCommandPath)
            {
                _boundCommandPath = cmd.Path;
                RebuildArgumentPanel(cmd);
                // Phase 5a: ObservableFieldsView も同期更新。
                _observableFields?.ShowFor(cmd.Path);
            }
            else if (cmd == null)
            {
                _argumentPanel.Clear();
                _argumentPanel.AddToClassList("palette-arguments-empty");
                _currentArgValues.Clear();
                _boundCommandPath = null;
                _observableFields?.Hide();
            }

            _runButton.SetEnabled(cmd != null);

            if (hasResult) _resultView.Show(_controller.LastResult);
            else _resultView.Clear();
        }

        // Status 行の文言。LastResult が無いときは "Status:" 空、有るときは OK / FAIL + duration。
        private static string FormatStatus(CommandResult r)
        {
            if (r == null) return "Status:";
            if (r.Success) return $"Status: OK ({r.Duration.TotalMilliseconds:F1}ms)";
            return $"Status: FAIL — {r.Error}";
        }

        private void RebuildArgumentPanel(CommandDescriptor cmd)
        {
            _argumentPanel.Clear();
            _currentArgValues.Clear();

            if (cmd.Parameters.Count == 0)
            {
                _argumentPanel.AddToClassList("palette-arguments-empty");
                return;
            }
            _argumentPanel.RemoveFromClassList("palette-arguments-empty");

            for (var i = 0; i < cmd.Parameters.Count; i++)
            {
                var param = cmd.Parameters[i];
                var row = new VisualElement();
                row.AddToClassList("palette-arg-row");

                var label = new Label($"{param.Name} : {param.Type.Name}");
                label.AddToClassList("palette-arg-label");
                row.Add(label);

                var editor = ParameterEditorRegistry.Resolve(param);
                var paramName = param.Name;
                var ve = editor.Build(param, value => _currentArgValues[paramName] = value);
                row.Add(ve);
                _argumentPanel.Add(row);

                _currentArgValues[param.Name] = ResolveInitialValue(param);
            }
        }

        private static object ResolveInitialValue(ParameterDescriptor param)
        {
            if (param.HasDefault) return param.DefaultValue;
            var t = param.Type;
            if (t == typeof(string)) return "";
            if (t.IsEnum) return Enum.GetValues(t).GetValue(0);
            if (t.IsValueType) return Activator.CreateInstance(t);
            return null;
        }

        // ------------------------------------------------------------
        // VSCode 風の複数ステップ引数入力フロー
        // ------------------------------------------------------------

        // フロー開始可能か判定し、可能なら開始する。可能だった場合は true。
        // 引数 0 個のコマンドはフローを使わず即実行扱いとし false を返す。
        private bool TryBeginParamFlow(CommandDescriptor cmd)
        {
            if (cmd == null || cmd.Parameters.Count == 0) return false;
            _paramFlowActive = true;
            _paramFlowCommand = cmd;
            _paramFlowIndex = 0;
            _currentArgValues.Clear();
            // 全引数の初期値を埋めておく (途中スキップやデフォルト確定で参照される)。
            for (var i = 0; i < cmd.Parameters.Count; i++)
            {
                var p = cmd.Parameters[i];
                _currentArgValues[p.Name] = ResolveInitialValue(p);
            }
            _resultsList.style.display = DisplayStyle.None;
            if (_bottom != null) _bottom.style.display = DisplayStyle.None;
            _paramFlowPanel.style.display = DisplayStyle.Flex;
            ShowCurrentParamFlowStep();
            return true;
        }

        // フローを抜けて検索 UI へ戻す。実行確定で抜ける場合は executeAfterExit=true。
        private void EndParamFlow(bool clearArgs)
        {
            _paramFlowActive = false;
            _paramFlowCommand = null;
            _paramFlowIndex = 0;
            _paramFlowPanel.style.display = DisplayStyle.None;
            _paramFlowEditorHost.Clear();
            _paramFlowBreadcrumbs.Clear();
            _resultsList.style.display = DisplayStyle.Flex;
            if (_bottom != null) _bottom.style.display = DisplayStyle.Flex;
            if (clearArgs) _currentArgValues.Clear();
            // 検索バーに戻す。同フレーム内で focus を再付与しておかないと、Runtime 側の
            // Esc フォールバックが「パレット内にフォーカス無し」と誤判定して Hide を呼ぶ可能性がある。
            // ただしモバイルでは programmatic Focus() が「貼り付き」を起こすため、auto-focus を見送る。
            if (!IsTouchDevice())
            {
                _searchInput?.Focus();
                schedule.Execute(() => _searchInput?.Focus()).ExecuteLater(0);
            }
        }

        // 現在のステップ (cmd.Parameters[_paramFlowIndex]) の編集 UI を構築し、フォーカスする。
        private void ShowCurrentParamFlowStep()
        {
            var cmd = _paramFlowCommand;
            if (cmd == null) return;
            var i = _paramFlowIndex;
            if (i < 0 || i >= cmd.Parameters.Count) return;
            var param = cmd.Parameters[i];

            _paramFlowCmdLabel.text = $"▸ {cmd.Path}";

            _paramFlowBreadcrumbs.Clear();
            for (var k = 0; k < i; k++)
            {
                var prev = cmd.Parameters[k];
                var v = _currentArgValues.TryGetValue(prev.Name, out var raw) ? raw : null;
                var chip = new Label($"{prev.Name}: {FormatArgValue(v)}");
                chip.AddToClassList("palette-param-flow-chip");
                _paramFlowBreadcrumbs.Add(chip);
            }

            _paramFlowStepInfo.text = $"Step {i + 1}/{cmd.Parameters.Count}  —  {param.Name} : {param.Type.Name}";
            _paramFlowStepDesc.text = string.IsNullOrEmpty(param.Description) ? "" : param.Description;
            _paramFlowStepDesc.style.display = string.IsNullOrEmpty(param.Description)
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            // 最終ステップなら「実行」、それ以外は「次へ」を表示。
            var isLastStep = i + 1 >= cmd.Parameters.Count;
            if (_paramFlowSubmitButton != null)
                _paramFlowSubmitButton.text = isLastStep ? "▶ 実行" : "次へ ▶";

            _paramFlowEditorHost.Clear();
            var editor = ParameterEditorRegistry.Resolve(param);
            var paramName = param.Name;
            var ve = editor.Build(param, value => _currentArgValues[paramName] = value);
            _paramFlowEditorHost.Add(ve);

            // モバイル WebGL のソフトキーボード「完了」対応として、エディタ内のフォーカス可能要素 (TextField 等)
            // が blur したら advance するよう FocusOutEvent をフック。submit ボタンへフォーカスが移ったケースは
            // OnParamFlowEditorFocusOut 側で除外する。
            HookFocusOutForParamFlow(ve);

            // モバイル WebGL ではプログラム的な Focus() ではソフトキーボードが開かず、
            // しかも UIToolkit 上は「focus 済」と判定されるためユーザーが再タップしても
            // キーボードが立ち上がらない閉塞状態になる。タッチデバイスでは auto-focus を
            // 行わず、ユーザーがエディタを直接タップして focus + キーボード起動するに任せる。
            if (!IsTouchDevice())
            {
                // 次フレームでエディタ内部の入力欄にフォーカスを当てる (PC / Editor 用)。
                schedule.Execute(() =>
                {
                    var f = FindFocusableDescendant(_paramFlowEditorHost);
                    f?.Focus();
                }).ExecuteLater(0);
            }
        }

        // タッチデバイス (= スマホ / タブレット, 含む WebGL on mobile) 判定。
        // プログラム的 Focus() でソフトキーボードが立ち上がらず、focus 状態が「貼り付く」挙動を避けるため、
        // モバイルでは auto-focus 系の処理をスキップする目印として使う。
        private static bool IsTouchDevice()
        {
            return UnityEngine.SystemInfo.deviceType == UnityEngine.DeviceType.Handheld
                || UnityEngine.Application.isMobilePlatform
                || UnityEngine.Input.touchSupported;
        }

        // 引数フローエディタの子孫 (TextField の input element など) すべてに FocusOutEvent を仕込む。
        // VisualElement.Query<VisualElement>().ForEach は子孫を辿るユーティリティだが、最低限の依存で
        // 済むよう手書きの再帰で巡回する。
        private void HookFocusOutForParamFlow(VisualElement root)
        {
            if (root == null) return;
            root.RegisterCallback<FocusOutEvent>(OnParamFlowEditorFocusOut);
            for (var i = 0; i < root.childCount; i++)
            {
                HookFocusOutForParamFlow(root[i]);
            }
        }

        // 連続発火ガード: 物理 Enter (NavigationSubmit) と blur (FocusOut) が同一操作で
        // 二度入ってくる可能性があるため、進行中の advance を 1 つに直列化する。
        private bool _advancing;

        // フロー中の Enter ハンドラ。確定 → 次へ / 最終ステップなら実行。
        private async System.Threading.Tasks.Task AdvanceParamFlowAsync()
        {
            if (_advancing || !_paramFlowActive) return;
            _advancing = true;
            try
            {
                // AutoComplete エディタが先頭候補で確定可能なら拾う (旧 Enter ハンドラと同じ救済)。
                for (var j = 0; j < _paramFlowEditorHost.childCount; j++)
                {
                    if (_paramFlowEditorHost[j].userData is Func<bool> tryComplete && tryComplete())
                        break;
                }

                var cmd = _paramFlowCommand;
                if (cmd == null) { EndParamFlow(clearArgs: false); return; }

                _paramFlowIndex++;
                if (_paramFlowIndex >= cmd.Parameters.Count)
                {
                    // フロー完了 → 検索 UI に戻してから実行 (結果表示は bottom の ResultView で見せる)。
                    EndParamFlow(clearArgs: false);
                    await _controller.ExecuteSelectedAsync(_currentArgValues);
                    _currentArgValues.Clear();
                    _boundCommandPath = null;
                    return;
                }
                ShowCurrentParamFlowStep();
            }
            finally
            {
                _advancing = false;
            }
        }

        // ------------------------------------------------------------
        // キーボード / ナビゲーション
        // ------------------------------------------------------------

        // 物理キーボードの Enter, ソフトキーボードの「完了」/ Submit ボタン, ゲームパッドの A 等を
        // すべて受ける汎用ハンドラ。スマホでは KeyDownEvent が来ないため、確定系の操作は
        // 必ずここを通す。
        // 検索バーがフォーカスを失った瞬間のハンドラ。モバイル WebGL の「完了」キー対応として、
        // 別の UI 要素にフォーカスが移っていない (= フォーカスが null になった) 場合のみ、
        // ソフトキーボードの確定操作とみなして ExecuteSelectedAsync を呼ぶ。
        // - 行をタップ / Submit ボタンをタップ → 対応する要素に focus が移るので何もしない
        // - 引数フローへの遷移で programmatic に focus 移動 → focus は editor に移るので何もしない
        // - パレットを閉じた直後 → IsVisible が false なので何もしない
        private void OnSearchInputFocusOut(FocusOutEvent _)
        {
            if (style.display.value != DisplayStyle.Flex) return;
            if (_paramFlowActive) return;
            schedule.Execute(() =>
            {
                if (style.display.value != DisplayStyle.Flex) return;
                if (_paramFlowActive) return;
                var focused = focusController?.focusedElement as VisualElement;
                if (focused == null)
                {
                    // フォーカスがどの UI 要素にも当たっていない = モバイル soft keyboard の「完了」とみなす。
                    var _ = ExecuteSelectedAsync();
                    return;
                }
                // _searchInput 自身 / 内部要素に戻っているなら通常操作。
                if (focused == _searchInput || focused.GetFirstAncestorOfType<TextField>() == _searchInput) return;
                // 引数フローパネル配下への意図的な focus 移動は許容する。
                if (_argumentPanel != null)
                {
                    var cur = focused;
                    while (cur != null)
                    {
                        if (cur == _argumentPanel) return;
                        cur = cur.parent;
                    }
                }
                // それ以外 (WASD / NavigationMove / Tab nav で ListView 等に飛ばされた) → 検索欄に強制復帰。
                // Runtime で panel が InputAction の Navigate を直接購読していると
                // NavigationMoveEvent の TrickleDown ガードでも止めきれないケースがあるため、
                // 「抜けても即戻す」対症療法で確実に検索欄に focus を留める。
                _searchInput?.Focus();
            }).ExecuteLater(0);
        }

        // 引数フローエディタがフォーカスを失った瞬間のハンドラ。検索バーと同じ理屈で、
        // フォーカスが引数フローパネル外 (= ソフトキーボード dismiss など) に抜けたら AdvanceParamFlowAsync。
        // 「次へ」ボタンに移った場合はそちらの click ハンドラが advance するので、ここでは何もしない。
        private void OnParamFlowEditorFocusOut(FocusOutEvent _)
        {
            if (!_paramFlowActive) return;
            schedule.Execute(() =>
            {
                if (!_paramFlowActive) return;
                var focused = focusController?.focusedElement as VisualElement;
                // パネル内 (Submit ボタン / 次の editor 等) にフォーカスが残っていれば、こちらでは確定しない。
                if (focused != null && IsDescendantOf(focused, _paramFlowPanel)) return;
                var _ = AdvanceParamFlowAsync();
            }).ExecuteLater(0);
        }

        private static bool IsDescendantOf(VisualElement el, VisualElement ancestor)
        {
            if (ancestor == null) return false;
            while (el != null)
            {
                if (el == ancestor) return true;
                el = el.parent;
            }
            return false;
        }

        private void OnNavigationSubmit(NavigationSubmitEvent evt)
        {
            if (_paramFlowActive)
            {
                _ = AdvanceParamFlowAsync();
                evt.StopImmediatePropagation();
                return;
            }
            // Logs モードは閲覧専用のため Submit を無視する (再実行は History モードの責務)。
            if (_mode != ViewMode.Logs)
            {
                _ = ExecuteSelectedAsync();
            }
            evt.StopImmediatePropagation();
        }

        // 物理キーボードの Esc, モバイルの戻る操作, ゲームパッドの B 等を受ける汎用ハンドラ。
        private void OnNavigationCancel(NavigationCancelEvent evt)
        {
            if (_paramFlowActive)
            {
                EndParamFlow(clearArgs: true);
                evt.StopImmediatePropagation();
                return;
            }
            CloseRequested?.Invoke();
            evt.StopImmediatePropagation();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            // フロー中は Up/Down も含めて KeyDown は素通しする (TextField のキャレット移動等に委ねる)。
            // Return / Escape は OnNavigationSubmit / OnNavigationCancel が拾うのでここでは扱わない
            // (スマホのソフトキーボードでは KeyDownEvent が発火しないため)。
            if (_paramFlowActive) return;

            switch (evt.keyCode)
            {
                case KeyCode.UpArrow:
                    MoveSelectionForCurrentMode(-1);
                    // UIToolkit のデフォルトナビゲーションが矢印キーで focus を ListView 等へ
                    // 移してしまい、検索バーで以後の文字入力ができなくなるのを防ぐため、
                    // 次フレームで明示的に検索バーへフォーカスを戻す。
                    schedule.Execute(() => _searchInput?.Focus()).ExecuteLater(0);
                    evt.StopImmediatePropagation();
                    break;
                case KeyCode.DownArrow:
                    MoveSelectionForCurrentMode(+1);
                    schedule.Execute(() => _searchInput?.Focus()).ExecuteLater(0);
                    evt.StopImmediatePropagation();
                    break;
                case KeyCode.Tab:
                    if (_tabs.Count > 0)
                    {
                        var current = (int)_mode;
                        var delta = evt.shiftKey ? -1 : 1;
                        var next = ((current + delta) % _tabs.Count + _tabs.Count) % _tabs.Count;
                        ActivateTab(next);
                        // UIToolkit のデフォルト Tab フォーカス移動で検索バーから抜けないよう、
                        // 矢印キーと同様に次フレームで明示的にフォーカスを戻す。
                        schedule.Execute(() => _searchInput?.Focus()).ExecuteLater(0);
                    }
                    evt.StopImmediatePropagation();
                    break;
            }
        }

        // ↑↓ キーによる選択移動。Commands は controller 経由、Logs/History は invocation 配列に対する
        // ローカル選択を直接動かし、行ハイライト更新のため ListView を RefreshItems する。
        private void MoveSelectionForCurrentMode(int delta)
        {
            if (_mode == ViewMode.Commands)
            {
                _controller.MoveSelection(delta);
                return;
            }
            if (_mode == ViewMode.Scenarios)
            {
                if (_scenarioSnapshot.Count == 0) return;
                var n = Mathf.Clamp(_scenarioSelectedIndex + delta, 0, _scenarioSnapshot.Count - 1);
                if (n == _scenarioSelectedIndex) return;
                _scenarioSelectedIndex = n;
                _resultsList.selectedIndex = _scenarioSelectedIndex;
                _resultsList.ScrollToItem(_scenarioSelectedIndex);
                _resultsList.RefreshItems();
                UpdateBottomScenarios();
                return;
            }
            if (_invocationSnapshot.Count == 0) return;
            var next = Mathf.Clamp(_invocationSelectedIndex + delta, 0, _invocationSnapshot.Count - 1);
            if (next == _invocationSelectedIndex) return;
            _invocationSelectedIndex = next;
            _resultsList.selectedIndex = _invocationSelectedIndex;
            _resultsList.ScrollToItem(_invocationSelectedIndex);
            _resultsList.RefreshItems();
            if (_mode == ViewMode.Logs) UpdateBottomLogs();
            else UpdateBottomHistory();
        }

        private static VisualElement FindFocusableDescendant(VisualElement root)
        {
            for (var i = 0; i < root.childCount; i++)
            {
                var c = root[i];
                if (c.focusable) return c;
                var nested = FindFocusableDescendant(c);
                if (nested != null) return nested;
            }
            return null;
        }

        // ------------------------------------------------------------
        // 実行
        // ------------------------------------------------------------

        private async System.Threading.Tasks.Task ExecuteSelectedAsync()
        {
            if (_mode == ViewMode.Scenarios)
            {
                // Scenario モード: 選択中シナリオを Path 指定で実行する。実行中は Run ボタンを disable し、
                // 完了後に ScenarioResultView へ結果を反映する。
                if (_scenarioSnapshot.Count == 0) return;
                if (_scenarioRunning) return;
                var d = _scenarioSnapshot[Mathf.Clamp(_scenarioSelectedIndex, 0, _scenarioSnapshot.Count - 1)];
                _scenarioRunning = true;
                UpdateBottomScenarios();
                ScenarioResult result;
                try
                {
                    result = await LiminalPalette.RunScenarioAsync(d.Path);
                }
                catch (Exception ex)
                {
                    // 想定外例外もファサード側で握り潰す方針なのでここに来る可能性は低いが、念のため。
                    Debug.LogWarning($"[LiminalPalette] Scenario '{d.Path}' raised: {ex.Message}");
                    result = null;
                }
                finally
                {
                    _scenarioRunning = false;
                }
                if (result != null)
                {
                    // Log/History タブにシナリオ実行を記録する。各 Command ステップは通常コマンドと
                    // 同じ形で個別に、シナリオ全体は "Scenario/<path>" の擬似 Path で 1 件積む。
                    ScenarioInvocationRecorder.Record(result, d.Path);
                    _scenarioResultView.Show(result);
                }
                UpdateBottomScenarios();
                return;
            }
            if (_mode == ViewMode.History)
            {
                // History モード: 選択中 invocation を同じ引数で再実行する。controller.ReplayAsync が
                // 新規実行と同様に InvocationStore に記録するので、再実行も履歴に積み上がる。
                if (_invocationSnapshot.Count == 0) return;
                var inv = _invocationSnapshot[Mathf.Clamp(_invocationSelectedIndex, 0, _invocationSnapshot.Count - 1)];
                await _controller.ReplayAsync(inv);
                return;
            }
            var selected = _controller.SelectedCommand;
            if (selected == null) return;

            // 引数があるコマンドは VSCode 風の複数ステップ入力フローへ遷移する。
            // 引数 0 個ならそのまま即時実行 (従来動作)。
            if (TryBeginParamFlow(selected)) return;

            await _controller.ExecuteSelectedAsync(_currentArgValues);
            RebuildArgumentPanel(selected);
        }

        // ------------------------------------------------------------
        // ヘルパ
        // ------------------------------------------------------------

        private static string BuildHighlightedPath(string path, IReadOnlyList<int> matchedIndices)
        {
            if (string.IsNullOrEmpty(path)) return path ?? "";

            var hasHighlights = matchedIndices != null && matchedIndices.Count > 0;
            HashSet<int> set = hasHighlights ? new HashSet<int>(matchedIndices) : null;
            var sb = new StringBuilder(path.Length + (hasHighlights ? matchedIndices.Count * 20 : 0));
            for (var i = 0; i < path.Length; i++)
            {
                var open = hasHighlights && set.Contains(i);
                if (open) sb.Append("<color=").Append(HighlightColorHex).Append('>');
                AppendEscaped(sb, path[i]);
                if (open) sb.Append("</color>");
            }
            return sb.ToString();
        }

        private static void AppendEscaped(StringBuilder sb, char c)
        {
            if (c == '<') sb.Append("&lt;");
            else if (c == '>') sb.Append("&gt;");
            else sb.Append(c);
        }
    }
}
