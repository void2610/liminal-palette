# UI

Editor Window と Runtime UI の使い方、ショートカット、入力ブロッカー。

---

## ショートカット

**Editor / Runtime とも `Cmd/Ctrl + K` で開閉する** (Phase 3 で統一)。

Unity は **フォーカスを持つウィンドウのショートカットしか発火しない** ため、Editor / Game ウィンドウの両方で `Cmd+K` を割り当てても競合しない:

- Editor (Inspector / Hierarchy / Scene 等にフォーカス) → `LiminalPaletteWindow` が開閉
- Game ウィンドウにフォーカス (Play Mode 中) → `LiminalPaletteRuntime` が開閉

### Runtime ショートカットの変更

`PaletteRuntimeSettings` (ScriptableObject) で変更可能:

```csharp
// 利用側の Resources/PaletteRuntimeSettings.asset を作成して上書きする
public class PaletteRuntimeSettings : ScriptableObject
{
    public bool EnableInRuntime = true;        // false で起動しない
    public bool RequireModifier = true;         // Cmd/Ctrl 修飾を必須にするか
    public KeyCode ToggleKey = KeyCode.K;       // 既定キー
    public bool DisableInProductionBuilds = true;
    public int PanelSortingOrder = 1000;        // 利用側 UI と被るなら調整
    public bool ResetOnEachOpen = true;         // false なら前回の検索を保持
}
```

上書き手順:
1. Project 内に `Assets/Resources/PaletteRuntimeSettings.asset` を作成 (`Create → LiminalPalette → Runtime Settings`)
2. Inspector で値を変更
3. `LiminalPaletteRuntime` が起動時に `Resources.Load` でこの asset を拾う

### Editor ショートカットの変更

`Edit → Shortcuts...` から `LiminalPalette/Toggle` を任意のキーに割り当てる。

---

## 4 タブ構成

パレットは Command / Scenario / Log / History の 4 タブを持つ:

### Command タブ (新規実行)

- 全コマンドを fuzzy 検索
- 履歴順で先頭、続いてアルファベット順
- 引数を入力して **Run Command** で実行
- 実行結果は下部 ResultView に表示 (success/error バッジ + Value + Logs)

### Scenario タブ (コマンドチェインの実行)

- `[ConsoleScenario]` で宣言した全シナリオを表示
- 各行: Path / Description / ステップ数 (副作用付き生成で計測不能なら "?")
- **Run Scenario** で全ステップを順次実行
- 実行結果は下部 `ScenarioResultView` で各ステップごとの ✓ / ✗ + 所要時間 + 失敗詳細
- 詳細: [scenarios.md](scenarios.md)

### Log タブ (起動履歴の詳細閲覧)

- `InvocationStore.Instance.Entries` の全履歴を新しい順で表示
- 各行: timestamp / Path / 引数 / Success/Error
- 行を選択すると下部に詳細 (引数全件 + Debug.Log 全件 + StackTrace) が出る
- 検索ボックスで Path / 引数の部分一致絞り込み
- **シナリオ由来エントリも全件表示** (個別 Command ステップ + `Scenario/<path>` の集約)

### History タブ (再実行特化)

- Log タブと同じデータソースだが、**選択した行をそのまま再実行** することに特化
- 引数欄は表示せず (前回の引数を再利用)
- Run Command で前回と同じ引数で実行される
- **シナリオ由来エントリは除外**: 前提状態を欠いた単独再実行の混乱を避けるため (シナリオの再実行は Scenario タブから行う)

---

## 引数入力 UI

引数の型に応じて自動で UI 要素が生成される。`IParameterEditor` ベースの拡張可能な仕組み (詳細は [extensibility.md](extensibility.md))。

| 型 | Editor の UI | Runtime の UI |
|---|---|---|
| `int` / `long` | `IntegerField` / `LongField` | 同左 |
| `float` / `double` | `FloatField` / `DoubleField` | 同左 |
| `string` | `TextField` | 同左 |
| `bool` | `Toggle` | 同左 |
| `enum` (通常) | `EnumField` | 同左 |
| `enum` (`[Flags]`) | `EnumFlagsField` | 各値ごとの Toggle 列 |
| `Vector2/3/4` | `VectorXField` | 同左 |
| `Color` / `Color32` | `ColorField` (UnityEditor 専用) | Slider 4 本 (R/G/B/A) + プレビュー |
| `UnityEngine.Object` | `ObjectField` (ピッカー付き) | TextField + UnityObjectConverter (`@<entityID>` / `GameObject:<name>`) |
| 任意型 | `FallbackTextEditor` | 同左 |

### 入力エラーの表示

ユーザーが不正な値を入れた場合 (例: `byte` に 999 を入れる、`Color32` の文字列パースエラー):

- フィールドが赤い枠で囲まれる (`lp-input-error` クラス)
- tooltip にエラーメッセージ
- 内部値は前回の有効値を保持し、`onChanged` は呼ばれない

`TypeConverterRegistry.TryConvert` の戻り値で判定される。

---

## Current values セクション

コマンドを選択すると引数欄の **直前** に、関連する `[ConsoleObservableField]` の現在値が表示される。

```
┌──────────────────────────────────────┐
│ Cmd: Player/Health/Set                │
├──────────────────────────────────────┤
│ Current values                        │
│   Player/Health: 75                   │  ← R3 push 駆動で自動更新
├──────────────────────────────────────┤
│ Args:                                 │
│   value: [____ 100 ____]              │
│                                       │
│            [ Run Command ]            │
└──────────────────────────────────────┘
```

挙動:
1. 選択コマンドの Path から最後の `/` 以前を prefix として取り出す (`Player/Health/Set` → `Player/Health`)
2. `ObservableFieldRegistry.FindByPathPrefix(prefix)` で関連フィールドを検索
3. 各フィールドのインスタンスを `IInstanceResolver` (= VContainer) で取得
4. `Subscribe` で R3 購読を張り、値変更時に Label を書き換える (polling ゼロ)
5. ユーザーが別コマンドへ切り替えたとき、または UI が破棄されるとき (`DetachFromPanelEvent`) に全 `IDisposable.Dispose()` を呼んで購読解除

### 値が `(instance not resolved)` と表示される場合

VContainer に該当型が登録されていない、または `LiminalPaletteEntryPoint` が登録されていない。`integrations.md` の手順を再確認:

```csharp
builder.RegisterComponentInHierarchy<Player>();
builder.RegisterEntryPoint<LiminalPaletteEntryPoint>();
```

### 関連 Field が無い場合

セクションは非表示になる (`display: None`)。引数欄が直接トップに出る。

詳細は [commands.md](commands.md) の `[ConsoleObservableField]` 章と [integrations.md](integrations.md)。

---

## キーボード操作

パレットを開いた状態で:

| キー | 動作 |
|---|---|
| `↑` / `↓` | 結果リストの選択を移動 |
| `Enter` | 選択コマンドを実行 (Run Command と同じ) |
| `Tab` / `Shift+Tab` | 引数フィールド間の移動 |
| `Esc` | パレットを閉じる |

検索ボックスは開いた瞬間にフォーカスされる。

---

## Runtime 限定: 半透明 overlay

Runtime UI は **画面全体を半透明黒でディマー** したオーバーレイとして描画される (Phase 3 で導入):

- パレットのパネル背景: `rgba(18, 18, 20, 0.88)` (半透明の near-black)
- バックドロップ: `rgba(0, 0, 0, 0.7)` (パネル外側のディマー)

ゲーム画面が薄く透けて見えるので、座標確認しながらのコマンド実行が可能。

カスタマイズしたい場合は `Resources/PaletteStyles.uss` を上書きするか、利用側で `UI Toolkit` の theme を差し替える。

---

## 入力ブロッカー (Runtime)

パレット表示中はゲーム入力をベストエフォートで停止する。

仕組み:
- `PaletteInputBlocker` が **静的イベント** `OnEngage` / `OnDisengage` を提供
- `Player.InputSystem` asmdef (defineConstraint で隔離) が両イベントを購読:
  - **Engage**: 全 `InputActionAsset` の `actionMaps` をスナップショットして `Disable()`
  - **Disengage**: スナップショットされた map を `Enable()` で復元

```csharp
// 利用側でカスタムブロック処理を追加したい場合
PaletteInputBlocker.OnEngage += () => Time.timeScale = 0f;
PaletteInputBlocker.OnDisengage += () => Time.timeScale = 1f;
```

> 制約: `Update()` 内で `Input.GetKey` を直接読んでいるコードは止められない (これは利用側の責務)。

---

## PaletteController (UI から独立した状態管理)

UI Toolkit に依存しない `PaletteController` が状態管理を担当:

```csharp
public sealed class PaletteController
{
    public string Query { get; }
    public IReadOnlyList<RankedCommand> Results { get; }
    public int SelectedIndex { get; }
    public CommandResult LastResult { get; }
    public Func<CommandDescriptor, bool> Filter { get; }

    public event Action StateChanged;

    public void SetQuery(string query);
    public void SetFilter(string label, Func<CommandDescriptor, bool> filter);
    public void MoveSelection(int delta);
    public void SetSelection(int index);
    public Task<CommandResult> ExecuteSelectedAsync(IReadOnlyDictionary<string, object> typedArgs, CancellationToken ct = default);
    public Task<CommandResult> ReplayAsync(CommandInvocation invocation, CancellationToken ct = default);
    public void Reset();
    public void ResetIfRequested(PaletteResetPolicy policy);
}
```

`StateChanged` を購読すれば `PaletteView` 以外のカスタム UI もホストできる (CLI tool / Vim 風 TUI 等)。

---

## ホスト 2 種

UI のホスト先は 2 つある:

### `LiminalPaletteWindow` (Editor)

- `EditorWindow` を継承
- `[Shortcut("LiminalPalette/Toggle", null, KeyCode.K, ShortcutModifiers.Action)]` で Cmd/Ctrl+K 登録
- 通常タブとして開く (ドッキング可能)
- `OnEnable` で `PaletteController` + `EditorCommandHistory` を生成

### `LiminalPaletteRuntime` (Runtime)

- `MonoBehaviour` (DontDestroyOnLoad シングルトン)
- `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` で 1 つだけ生成
- `UIDocument` を持ち、PaletteView を `rootVisualElement` に追加
- 表示/非表示は `style.display` の切替 (`gameObject.SetActive` だと UIDocument が再生成されるため避ける)
- `PaletteRuntimeSettings` で挙動カスタマイズ
- `PaletteInputBlocker` で Engage / Disengage を発火

両者とも **同じ `PaletteView` インスタンス** (の別インスタンス) を内側で使う。

---

## 関連ドキュメント

- [commands.md](commands.md) — `[ConsoleCommand]` の書き方
- [extensibility.md](extensibility.md) — `IParameterEditor` で型ごとの UI を拡張
- [security.md](security.md) — Production ビルドで Runtime UI を無効化する方法
- [troubleshooting.md](troubleshooting.md) — UI が出ない / フォーカスが取られる等
