using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Void2610.LiminalPalette.UI
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
        // Phase 5a: 選択コマンドの prefix と一致する [ConsoleObservableField] を表示するセクション。
        private ObservableFieldsView _observableFields;
        private Button _runButton;
        private ResultView _resultView;
        private Label _logStackLabel;
        private Label _columnPath;
        private Label _columnDescription;
        private Label _columnCurrent;

        private ViewMode _mode = ViewMode.Commands;
        // Logs / History タブ用の最新スナップショット (新しい順)。InvocationStore.Changed のたびに再構築する。
        private readonly List<CommandInvocation> _invocationSnapshot = new List<CommandInvocation>();
        // 起動履歴で選択中の行 (Logs / History モードで共有)。Commands モードでは未使用。
        private int _invocationSelectedIndex = 0;
        // 起動履歴に対する検索クエリ (Path / args の単純部分一致)。
        private string _invocationQuery = "";

        // タブ。ボタンと filter のペア。
        private readonly List<(Button button, string label, Func<CommandDescriptor, bool> filter)> _tabs
            = new List<(Button, string, Func<CommandDescriptor, bool>)>();

        // 引数の現在値。各 IParameterEditor の onChanged で更新される。
        private readonly Dictionary<string, object> _currentArgValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // 直前にバインドされたコマンドのパス。SelectedCommand が変わったときだけ引数パネルを再構築する。
        private string _boundCommandPath;

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

            // 列ヘッダの Label 参照を保持してモードに応じて文言を切り替える。
            var columnHeader = this.Q<VisualElement>("palette-column-header");
            _columnPath = columnHeader.Q<Label>(className: "palette-column-header-path");
            _columnDescription = columnHeader.Q<Label>(className: "palette-column-header-description");
            _columnCurrent = columnHeader.Q<Label>(className: "palette-column-header-current");

            _resultsList.makeItem = MakeRow;
            _resultsList.bindItem = BindRow;
            _resultsList.selectionType = SelectionType.Single;
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
        // タブ自体には filter は持たせず、ActivateTab 内で ViewMode を切り替える。
        private void BuildTabs()
        {
            _tabs.Clear();
            _tabsBar.Clear();
            AddTab("Command", null);
            AddTab("Scenario", null);
            AddTab("Log", null);
            AddTab("History", null);
            ActivateTab(0);
        }

        private void AddTab(string label, Func<CommandDescriptor, bool> filter)
        {
            var btn = new Button { text = label };
            btn.AddToClassList("palette-tab");
            btn.style.flexShrink = 0;
            var index = _tabs.Count;
            btn.clicked += () => ActivateTab(index);
            _tabsBar.Add(btn);
            _tabs.Add((btn, label, filter));
        }

        private void ActivateTab(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;
            for (var i = 0; i < _tabs.Count; i++)
            {
                if (i == index) _tabs[i].button.AddToClassList("palette-tab-active");
                else _tabs[i].button.RemoveFromClassList("palette-tab-active");
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
            UpdateColumnHeaders();
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

        // モードに応じて列ヘッダの見出しを切り替える。
        private void UpdateColumnHeaders()
        {
            switch (_mode)
            {
                case ViewMode.Scenarios:
                    if (_columnPath != null) _columnPath.text = "Path";
                    if (_columnDescription != null) _columnDescription.text = "Description";
                    if (_columnCurrent != null) _columnCurrent.text = "Steps";
                    break;
                case ViewMode.Logs:
                    if (_columnPath != null) _columnPath.text = "Path";
                    if (_columnDescription != null) _columnDescription.text = "Status";
                    if (_columnCurrent != null) _columnCurrent.text = "Time";
                    break;
                case ViewMode.History:
                    if (_columnPath != null) _columnPath.text = "Path";
                    if (_columnDescription != null) _columnDescription.text = "Args";
                    if (_columnCurrent != null) _columnCurrent.text = "Time";
                    break;
                default:
                    if (_columnPath != null) _columnPath.text = "Name";
                    if (_columnDescription != null) _columnDescription.text = "Description";
                    if (_columnCurrent != null) _columnCurrent.text = "Current";
                    break;
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
            if (header != null) header.style.flexShrink = 0;
            if (_searchInput != null)
            {
                _searchInput.style.minHeight = 24;
                _searchInput.style.flexShrink = 0;
            }

            var columnHeader = this.Q<VisualElement>("palette-column-header");
            if (columnHeader != null)
            {
                columnHeader.style.flexDirection = FlexDirection.Row;
                columnHeader.style.alignItems = Align.Center;
                columnHeader.style.flexShrink = 0;

                // ヘッダ各列の幅を data 行と一致させる (USS class でも書いているがインラインで強制)。
                var hMark = columnHeader.Q<Label>(className: "palette-column-header-mark");
                if (hMark != null)
                {
                    hMark.style.width = 14;
                    hMark.style.flexShrink = 0;
                }
                var hPath = columnHeader.Q<Label>(className: "palette-column-header-path");
                if (hPath != null)
                {
                    hPath.style.flexGrow = 1;
                    hPath.style.flexShrink = 1;
                }
                var hDesc = columnHeader.Q<Label>(className: "palette-column-header-description");
                if (hDesc != null)
                {
                    hDesc.style.width = 240;
                    hDesc.style.flexShrink = 0;
                }
                var hCurrent = columnHeader.Q<Label>(className: "palette-column-header-current");
                if (hCurrent != null)
                {
                    hCurrent.style.width = 80;
                    hCurrent.style.flexShrink = 0;
                }
            }

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

            // 矢印キーは OnKeyDown が責任を持って MoveSelection するので、
            // ListView 組み込みの NavigationMove (Up/Down) は捕まえてここで握りつぶす。
            // 放置すると KeyDown を StopImmediatePropagation してもナビゲーション系統が
            // 別経路で ListView の selectedIndex を進め、結果として「1 押下で 2 行進む」
            // 二重選択が起きる。Left/Right や Submit/Cancel は他用途で使うため触らない。
            RegisterCallback<NavigationMoveEvent>(evt =>
            {
                if (evt.direction == NavigationMoveEvent.Direction.Up
                    || evt.direction == NavigationMoveEvent.Direction.Down)
                {
                    evt.StopImmediatePropagation();
                }
            }, TrickleDown.TrickleDown);
        }

        // ------------------------------------------------------------
        // ListView 行 (3 列: マーク / Name / Description / Current)
        // ------------------------------------------------------------

        private VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("palette-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.flexShrink = 0;
            row.style.flexGrow = 1;

            // 列幅は data 行とヘッダで一致させる必要があるため、インラインで明示する。
            var historyMark = new Label("•");
            historyMark.AddToClassList("palette-row-history-mark");
            historyMark.name = "history-mark";
            historyMark.style.width = 14;
            historyMark.style.flexShrink = 0;
            row.Add(historyMark);

            var path = new Label();
            path.AddToClassList("palette-row-path");
            path.enableRichText = true;
            path.name = "row-path";
            path.style.flexGrow = 1;
            path.style.flexShrink = 1;
            path.style.overflow = Overflow.Hidden;
            path.style.textOverflow = TextOverflow.Ellipsis;
            path.style.whiteSpace = WhiteSpace.NoWrap;
            row.Add(path);

            var description = new Label();
            description.AddToClassList("palette-row-description");
            description.name = "row-description";
            description.style.width = 240;
            description.style.flexShrink = 0;
            description.style.whiteSpace = WhiteSpace.NoWrap;
            description.style.overflow = Overflow.Hidden;
            description.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(description);

            var current = new Label();
            current.AddToClassList("palette-row-current");
            current.name = "row-current";
            current.style.width = 80;
            current.style.flexShrink = 0;
            current.style.whiteSpace = WhiteSpace.NoWrap;
            current.style.overflow = Overflow.Hidden;
            current.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(current);

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

            var pathLabel = row.Q<Label>("row-path");
            pathLabel.text = BuildHighlightedPath(ranked.Descriptor.Path, ranked.MatchedIndices);
            // Logs/History モードで設定した inline 色が ListView の行リサイクルで残るため、
            // Commands モードでは USS 既定値に戻す。
            pathLabel.style.color = StyleKeyword.Null;

            var descLabel = row.Q<Label>("row-description");
            descLabel.text = ranked.Descriptor.Description ?? "";
            descLabel.style.color = StyleKeyword.Null;

            var currentLabel = row.Q<Label>("row-current");
            currentLabel.text = FormatCurrentColumn(ranked.Descriptor);

            row.RemoveFromClassList("palette-row-selected");
            if (index == _controller.SelectedIndex) row.AddToClassList("palette-row-selected");
        }

        // Scenario タブの 1 行レンダリング。Path / Description / Step 数 を表示。
        private void BindScenarioRow(VisualElement row, int index)
        {
            if (index < 0 || index >= _scenarioSnapshot.Count) return;
            var d = _scenarioSnapshot[index];

            var historyMark = row.Q<Label>("history-mark");
            historyMark.style.visibility = Visibility.Hidden;

            var pathLabel = row.Q<Label>("row-path");
            pathLabel.text = d.Path;
            pathLabel.style.color = StyleKeyword.Null;

            var descLabel = row.Q<Label>("row-description");
            descLabel.text = d.Description ?? "";
            descLabel.style.color = StyleKeyword.Null;

            var currentLabel = row.Q<Label>("row-current");
            // RefreshScenarioSnapshot で計算したキャッシュから読み取る。bind ごとに
            // StepsFactory を起動しないことが目的 (副作用回避 + 仮想化の負荷削減)。
            var count = _scenarioStepCounts.TryGetValue(d.Path, out var c) ? c : -1;
            currentLabel.text = count < 0 ? "?" : count.ToString(CultureInfo.InvariantCulture);

            row.RemoveFromClassList("palette-row-selected");
            if (index == _scenarioSelectedIndex) row.AddToClassList("palette-row-selected");
        }

        // Logs / History 共通の行レンダリング。
        // showArgs=false (Logs) → Path / Status / Time
        // showArgs=true  (History) → Path / Args 要約 / Time
        private void BindInvocationRow(VisualElement row, int index, bool showArgs)
        {
            if (index < 0 || index >= _invocationSnapshot.Count) return;
            var inv = _invocationSnapshot[index];

            var historyMark = row.Q<Label>("history-mark");
            historyMark.style.visibility = Visibility.Hidden;

            var pathLabel = row.Q<Label>("row-path");
            pathLabel.text = inv.Path;
            pathLabel.style.color = inv.Result.Success
                ? new Color(0.85f, 0.85f, 0.85f, 1f)
                : new Color(0.92f, 0.45f, 0.45f, 1f);

            var descLabel = row.Q<Label>("row-description");
            descLabel.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            if (showArgs)
            {
                descLabel.text = FormatArgsSummary(inv.Args);
            }
            else
            {
                descLabel.text = inv.Result.Success
                    ? $"OK ({inv.Result.Duration.TotalMilliseconds:F1}ms)"
                    : $"FAIL — {inv.Result.Error}";
            }

            var currentLabel = row.Q<Label>("row-current");
            currentLabel.text = inv.TimestampUtc.ToLocalTime().ToString("HH:mm:ss");

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
        //   1) コマンドの親階層 (= Path の最後の "/" 前) prefix に紐づく [ConsoleObservableField] が
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
                        var instance = LiminalPalette.InstanceResolver.Resolve(d.DeclaringType);
                        if (instance != null)
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
                    var instance = LiminalPalette.InstanceResolver.Resolve(d.DeclaringType);
                    if (instance == null) continue;
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
            _argumentPanel.style.display = DisplayStyle.Flex;
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
        // キーボード
        // ------------------------------------------------------------

        private void OnKeyDown(KeyDownEvent evt)
        {
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
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    // 候補表示中なら先頭候補で確定してから実行
                    if (IsArgumentFieldFocused())
                        TryAutoComplete();
                    // Logs モードは閲覧専用のため Enter を無視する (再実行は History モードの責務)。
                    if (_mode != ViewMode.Logs)
                    {
                        _ = ExecuteSelectedAsync();
                    }
                    evt.StopImmediatePropagation();
                    break;
                case KeyCode.Escape:
                    CloseRequested?.Invoke();
                    evt.StopImmediatePropagation();
                    break;
                case KeyCode.Tab:
                    if (evt.shiftKey)
                    {
                        _searchInput.Focus();
                    }
                    else if (_argumentPanel.childCount > 0)
                    {
                        var firstInput = FindFocusableDescendant(_argumentPanel);
                        firstInput?.Focus();
                    }
                    evt.StopImmediatePropagation();
                    break;
                case KeyCode.Alpha1:
                case KeyCode.Alpha2:
                case KeyCode.Alpha3:
                case KeyCode.Alpha4:
                case KeyCode.Alpha5:
                case KeyCode.Alpha6:
                case KeyCode.Alpha7:
                case KeyCode.Alpha8:
                case KeyCode.Alpha9:
                    if (evt.actionKey)
                    {
                        var index = evt.keyCode - KeyCode.Alpha1;
                        if (index < _tabs.Count)
                        {
                            ActivateTab(index);
                            evt.StopImmediatePropagation();
                        }
                    }
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

        /// <summary>
        /// 引数パネル内のAutoCompleteEditorの補完を試みる。
        /// 候補が1件に絞り込まれていれば確定してtrueを返す。
        /// </summary>
        private bool TryAutoComplete()
        {
            for (var i = 0; i < _argumentPanel.childCount; i++)
            {
                var row = _argumentPanel[i];
                for (var j = 0; j < row.childCount; j++)
                {
                    if (row[j].userData is Func<bool> tryComplete && tryComplete())
                        return true;
                }
            }
            return false;
        }

        /// <summary>引数パネル内のTextFieldにフォーカスがあるかどうか</summary>
        private bool IsArgumentFieldFocused()
        {
            var focused = focusController?.focusedElement as VisualElement;
            while (focused != null)
            {
                if (focused == _argumentPanel) return true;
                focused = focused.parent;
            }
            return false;
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
            if (_controller.SelectedCommand == null) return;
            await _controller.ExecuteSelectedAsync(_currentArgValues);
            // 実行後にパラメータパネルをリビルドして入力をクリア
            RebuildArgumentPanel(_controller.SelectedCommand);
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
