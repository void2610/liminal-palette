# asmdef 構成

LiminalPalette は **9 つの asmdef** で構成される。Production ビルドへの混入を防ぎ、UPM パッケージ化したときに利用側が必要な部分だけ取り込める設計。

---

## 全体図

```
                    ┌─────────────────────────────────────┐
                    │ Void2610.LiminalPalette (Core)      │  autoReferenced=true
                    │  Registry / Executor / Models /      │  全プラットフォーム
                    │  Conversion / Attributes             │
                    └─────────────────┬───────────────────┘
                                      ↑
                ┌─────────────────────┼─────────────────────┐
                │                     │                     │
                │                     │                     │
        ┌───────┴────────┐    ┌───────┴────────┐    ┌──────┴────────┐
        │ .UI            │    │ .Runtime       │    │ .Ipc          │
        │ (autoRef=false)│    │ (autoRef=true) │    │ (autoRef=false)│
        │ 全プラットフォーム │    │ 全プラットフォーム │    │ 全プラットフォーム │
        └───────┬────────┘    └───────┬────────┘    └──────┬────────┘
                │                     │                     │
        ┌───────┼─────┐       ┌───────┼─────┐       ┌──────┴────┐
        │       │     │       │       │     │       │           │
        ↑       ↑     ↑       ↑       ↑     ↑       ↑           ↑
   ┌────┴───┐  │  ┌──┴──┐  ┌──┴──┐    │  ┌──┴──┐  ┌─┴────────┐  │
   │.Editor │  │  │     │  │.Runtime.   │  │.Runtime.      │  │
   │(Editor)│  │  │     │  │ InputSystem│  │ Ipc           │  │
   │        │  │  │     │  │ (constraint│  │ (constraint   │  │
   │        │  │  │     │  │ INPUTSYSTEM│  │ EDITOR \|\|     │  │
   │        │  │  │     │  │ )          │  │ DEV BUILD)    │  │
   └────────┘  │  └─────┘  └────────────┘  └───────────────┘  │
               │                                               │
               └───────────────────────────────────────────────┘
                              ↑
                         .Tests (Editor only)
```

---

## asmdef 一覧

| # | asmdef | references | autoRef | platforms | defineConstraints |
|---|---|---|---|---|---|
| 1 | `Void2610.LiminalPalette` | **R3.Unity** | ✅ | 全て | — |
| 2 | `Void2610.LiminalPalette.UI` | Core | ❌ | 全て | — |
| 3 | `Void2610.LiminalPalette.Editor` | Core, UI, Ipc | ❌ | Editor | — |
| 4 | `Void2610.LiminalPalette.Runtime` | Core, UI | ✅ | 全て | — |
| 5 | `Void2610.LiminalPalette.Runtime.InputSystem` | Runtime, Unity.InputSystem | ✅ | 全て | `LIMINAL_PALETTE_INPUTSYSTEM` |
| 6 | `Void2610.LiminalPalette.Ipc` | Core, UI | ❌ | 全て | — |
| 7 | `Void2610.LiminalPalette.Runtime.Ipc` | Core, UI, Runtime, Ipc | ✅ | 全て | `UNITY_EDITOR \|\| DEVELOPMENT_BUILD \|\| LIMINAL_PALETTE_FORCE_ENABLE` |
| 8 | **`Void2610.LiminalPalette.Integration.VContainer`** | Core, **VContainer** | ✅ | 全て | — |
| 9 | `Void2610.LiminalPalette.Tests` | 1〜8 + TestRunner + R3.Unity + VContainer | ❌ | Editor | `UNITY_INCLUDE_TESTS` |

---

## 各 asmdef の役割

### 1. `Void2610.LiminalPalette` (Core)

`Bootstrap.cs` / `LiminalPalette.cs` ファサード / Registry (Command / ObservableField / Scenario) / Executor (Command / Scenario / `IFrameWaiter` + `RuntimeFrameWaiter`) / Models / Conversion / Attributes (`LiminalCommand` / `LiminalObservableField` / `LiminalScenario` / `LiminalParam`) / Resolution。

**依存**: R3.Unity (Phase 5a で必須化)。`ReactiveProperty<T>` / `Observable<T>` を `[LiminalObservableField]` に直接使うため。

`autoReferenced: true` なので利用側は `using Void2610.LiminalPalette;` だけで使える。

### 2. `Void2610.LiminalPalette.UI`

`PaletteView` (Command / Scenario / Log / History の 4 タブ) / `PaletteController` / `IParameterEditor` / `ICommandHistory` / `InvocationStore` / `ScenarioInvocationRecorder` / `ScenarioResultView` / UXML / USS。

UnityEditor 非依存 (Runtime でも動く UI Toolkit のみ使用)。

`autoReferenced: false`: UI を使わない利用側 (CLI のみ使う等) に巻き込まれないため。

### 3. `Void2610.LiminalPalette.Editor`

`LiminalPaletteWindow` (EditorWindow) / `EditorCommandHistory` / Editor 用 `ColorField` / `ObjectField` / `EnumFlagsField` 系の `IParameterEditor` / `EditorMenuItemBootstrap` / `EditorIpcBootstrap` / `EditorFrameWaiter` (Edit Mode 用 `IFrameWaiter`) / `EditorScenarioBootstrap` (PlayMode 状態に応じて `RuntimeFrameWaiter` / `EditorFrameWaiter` を切り替え)。

UnityEditor.* に依存するためこの asmdef に閉じ込める。Editor プラットフォーム限定。

### 4. `Void2610.LiminalPalette.Runtime`

`LiminalPaletteRuntime` (DontDestroyOnLoad シングルトン) / `RuntimeBootstrap` / `PaletteRuntimeSettings` / `IPaletteInput` 抽象 / `EventPaletteInput` (IMGUI 実装、現行のデフォルト) / `LegacyPaletteInput` / `NoOpPaletteInput` / `PaletteInputBlocker` / `ProductionGuard`。

`autoReferenced: true`: 利用側が何もせずに Runtime ブートストラップが走る。

UnityEngine.InputSystem は **直接参照しない** (= Runtime asmdef だけでは InputSystem 有り無しに関わらず動く)。

#### Runtime ホットキー検出に InputSystem を使わない理由

Runtime のパレット toggle (Cmd+K / Ctrl+K) は **必ず IMGUI (`UnityEngine.Event`) ベースの `EventPaletteInput`** で実装する。InputSystem 経由で Keyboard を polling する旧実装はやめた。経緯:

- macOS で **Cmd+P → Play Mode 突入** すると、Editor が Cmd の keyup を消費して InputSystem 内部では Cmd が `isPressed=true` のまま固着する (Unity 既知挙動)。
- 固着したまま modifier 判定すると、ユーザーが K 単独を押しただけでも Cmd+K として通り、パレットが誤発火する。
- rising-edge フラグやデバイス reset で局所的に回避を試みたが、Active Input Handler や Configurable Enter Play Mode の組合せで再発するため断念。
- IMGUI の KeyDown イベントは OS のイベントキュー由来で、KeyDown 個別に `e.command` / `e.control` / `e.shift` が付与される。フレームをまたいで持ち越される "stuck" な状態が無いため、Cmd+P 直後でも誤発火しない。
- IMGUI は **Active Input Handler の設定 (Legacy / InputSystem / Both) に依存しない** ため、利用側プロジェクトの設定差異も吸収できる。

このため `PaletteInputFactory` は分岐を持たず、常に `EventPaletteInput` を返す。`LiminalPaletteRuntime.OnGUI` で `Event.current` を `EventPaletteInput.HandleEvent` に流し込み、`Update` から `ConsumeXxx` で読み取り消費する。

### 5. `Void2610.LiminalPalette.Runtime.InputSystem`

`InputSystemBootstrap` のみ。

`defineConstraints: ["LIMINAL_PALETTE_INPUTSYSTEM"]`:
- このシンボルは Runtime asmdef の `versionDefines` で `com.unity.inputsystem >= 1.0.0` のときに立つ
- → InputSystem 未導入プロジェクトでは asmdef 自体がコンパイル対象外

責務: パレット表示中だけゲーム側の InputSystem `ActionMap` を一括停止／復元する。`InputSystemBootstrap.Hook` が `[RuntimeInitializeOnLoadMethod(BeforeSplashScreen)]` で:
- `PaletteInputBlocker.OnEngage` に全 ActionMap 停止処理を登録
- `PaletteInputBlocker.OnDisengage` に復元処理を登録

> **注**: 旧実装ではここで `PaletteInputFactory.OverrideFactory` に `InputSystemPaletteInput` を登録し、ホットキー検出も InputSystem に任せていた。現在は IMGUI ベースに一本化したためこの asmdef はホットキー検出に関与しない。`InputSystemPaletteInput` は削除済み、`PaletteInputFactory.OverrideFactory` も廃止済み。詳細は本ファイル「Runtime ホットキー検出に InputSystem を使わない理由」節を参照。

### 6. `Void2610.LiminalPalette.Ipc`

`HttpServer` / `IpcRouter` / `IpcRequest` / `IpcResponse` / 7 エンドポイント (health / commands / execute / logs / state / scenarios / scenarios/run) / `TokenStore` / `TokenAuthenticator` / `JsonWriter` / `JsonReader` / `IpcContracts` / `MainThreadDispatcher` / `IpcSettings`。

UnityEditor 非依存。`HttpListener` (System.Net) を使うが Player でも Editor でも動く。

`autoReferenced: false`: HTTP API を使わないプロジェクトに巻き込まれないため。

### 7. `Void2610.LiminalPalette.Runtime.Ipc`

`RuntimeIpcBootstrap` / `IpcRuntimeTicker` (MonoBehaviour)。

`defineConstraints: ["UNITY_EDITOR || DEVELOPMENT_BUILD || LIMINAL_PALETTE_FORCE_ENABLE"]`:
- **Production ビルドでは asmdef ごとコンパイル対象外** (オプトインのため利用側が `LIMINAL_PALETTE_FORCE_ENABLE` を Scripting Define Symbols に追加した場合のみ復活する)
- これにより HTTP サーバー機構が Player ビルドに混入しない

`autoReferenced: true`: 利用側が何もしなくても Runtime IPC が起動する (Development build のみ)。

### 8. `Void2610.LiminalPalette.Integration.VContainer`

`VContainerInstanceResolver` + `LiminalPaletteEntryPoint`。

VContainer の `IObjectResolver` を `IInstanceResolver` にアダプトする層。利用側は `LifetimeScope.Configure` で `builder.RegisterEntryPoint<LiminalPaletteEntryPoint>()` を呼ぶだけで、コンテナ全体がインスタンスメソッド `[LiminalCommand]` と `[LiminalObservableField]` の解決経路として機能する。

`autoReferenced: true`、VContainer references 必須。VContainer 未導入プロジェクトでは asmdef がコンパイル不能になる (Phase 5a で必須化、利用側コード量最小化のため意図的)。

### 9. `Void2610.LiminalPalette.Tests`

EditMode テスト一式 (Phase 5a 完了時点で 230 件)。`Editor` プラットフォーム限定。

`overrideReferences: true` + `precompiledReferences: ["nunit.framework.dll", "R3.dll", "Microsoft.Bcl.TimeProvider.dll", "Microsoft.Bcl.AsyncInterfaces.dll"]`。

`overrideReferences=true` の asmdef は他 asmdef の `precompiledReferences` を引き継がないため、R3 の DLL 群を明示的に列挙する必要がある (落とし穴)。

---

## 依存方向の原則

- **Core (Void2610.LiminalPalette) は何にも依存しない**
- UI / Editor / Runtime / Ipc が Core を参照する (一方向)
- Editor は UI と Ipc を参照する (UI 側エディタ + IPC ブートストラップ)
- Runtime.InputSystem は Runtime を参照 (Hook 登録のため)
- Runtime.Ipc は Runtime と Ipc 両方を参照 (Bootstrap が両者を使う)
- Tests は全部参照する (検証のため)

逆方向の依存 (例: UI → Editor、Runtime → UI 内部) は **絶対に作らない**。

---

## defineConstraints の活用

### `LIMINAL_PALETTE_INPUTSYSTEM`

**Runtime asmdef の `versionDefines`** で定義:
```json
"versionDefines": [
    {
        "name": "com.unity.inputsystem",
        "expression": "1.0.0",
        "define": "LIMINAL_PALETTE_INPUTSYSTEM"
    }
]
```

**Runtime.InputSystem asmdef の `defineConstraints`** で要求:
```json
"defineConstraints": [
    "LIMINAL_PALETTE_INPUTSYSTEM"
]
```

これで InputSystem 未導入プロジェクトでは Runtime.InputSystem asmdef がリンクされず、ActionMap の自動停止が走らないだけになる (パレット自体は IMGUI ベースの `EventPaletteInput` で問題なく開閉する)。

### `UNITY_EDITOR || DEVELOPMENT_BUILD`

**Runtime.Ipc asmdef の `defineConstraints`** で要求:
```json
"defineConstraints": [
    "UNITY_EDITOR || DEVELOPMENT_BUILD || LIMINAL_PALETTE_FORCE_ENABLE"
]
```

`LIMINAL_PALETTE_FORCE_ENABLE` は利用側の **明示的オプトイン** 用 (例: Production build でもパレットを残したい QA ビルド)。Scripting Define Symbols に追加しない限り Development ビルドと Editor 以外では asmdef がコンパイル対象外になる。

Production ビルド (Development build フラグ無し) では:
- asmdef 自体がコンパイル対象外
- → `RuntimeIpcBootstrap` も `IpcRuntimeTicker` も存在しない
- → HTTP サーバーが起動する経路が完全に消える

これは Production への HTTP 機構混入を防ぐ **三重防御の最も強い層**。

### `UNITY_INCLUDE_TESTS`

Tests asmdef は `UNITY_INCLUDE_TESTS` を要求するため、Test Runner が無効な利用環境ではコンパイルされない。

---

## autoReferenced の選び方

- ✅ `true`: Core / Runtime / Runtime.InputSystem / Runtime.Ipc
  - 利用側がコードを書かなくても自動起動して欲しいもの
  - `Bootstrap` 系 (`[RuntimeInitializeOnLoadMethod]`) が走るために必須
- ❌ `false`: UI / Editor / Ipc / Tests
  - 利用側が明示的に asmdef references を追加して使うもの
  - autoRef にすると本来不要なプロジェクトを巻き込む

---

## Production 除外の三重防御

1. **asmdef defineConstraints** (`Runtime.Ipc` で `UNITY_EDITOR || DEVELOPMENT_BUILD || LIMINAL_PALETTE_FORCE_ENABLE`)
   - 最も強い: コンパイル対象外
2. **`ProductionGuard.ShouldDisableInRuntime`** (Runtime のコード内チェック)
   - `PaletteRuntimeSettings.DisableInProductionBuilds` + `Debug.isDebugBuild` で判定
3. **`IpcSettings.EnableInRuntime` / `EnableInEditor`** (利用側の明示設定)
   - 起動時のオプトアウト

すべて独立して機能。1 つでも倒せば Runtime IPC は起動しない。

---

## 利用側の参照パターン

### 最小利用: Core だけ

```json
// 利用側の asmdef
"references": [
    "Void2610.LiminalPalette"
]
```

`[LiminalCommand]` の付与と `LiminalPalette.ExecuteAsync` 呼び出しのみ。UI / IPC は使わない。

### Editor / Runtime UI も使う (一般的)

何もしなくて良い。Core / Runtime / Runtime.InputSystem / Runtime.Ipc は `autoReferenced: true` なので自動的に巻き込まれる。

### IParameterEditor / 動的コマンドを書きたい

UI asmdef を明示参照:
```json
"references": [
    "Void2610.LiminalPalette",
    "Void2610.LiminalPalette.UI"
]
```

### HTTP API のクライアント実装をテストしたい

```json
"references": [
    "Void2610.LiminalPalette",
    "Void2610.LiminalPalette.Ipc"
]
```

---

## 関連ドキュメント

- [security.md](security.md) — defineConstraints による Production 除外の詳細
- [ipc.md](ipc.md) — Runtime.Ipc が起動する条件
- [extensibility.md](extensibility.md) — どの asmdef に拡張コードを書くべきか
