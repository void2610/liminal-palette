# Troubleshooting

よくある問題と解決策、既知の制約。

---

## UI / コマンド系

### Q. パレットが `Cmd/Ctrl + K` で開かない

**A**: フォーカスを持つウィンドウで挙動が分かれる。

- Editor (Inspector / Hierarchy / Scene 等) にフォーカス → `LiminalPaletteWindow` が開く
- Game ウィンドウ (Play Mode 中) にフォーカス → `LiminalPaletteRuntime` が開く
- Project ウィンドウ / Console ウィンドウ等にフォーカス → どちらも開かない場合がある

確認:
1. ウィンドウタブをクリックしてフォーカスを取る
2. 再度 `Cmd/Ctrl + K`

### Q. コマンドが結果リストに表示されない

**A**: いくつか原因が考えられる:

1. **`public` でない**: `private` / `internal` は登録対象外
2. **属性が間違ったクラスに付いている**: メソッドにのみ付与可能
3. **`Path` が空文字 / 末尾 `/`**: AttributeScanner が例外で弾く (Console に warning が出る)
4. **`#if DEVELOPMENT_BUILD` 等でメソッドを囲んでいる**: 非開発ビルドではメソッド自体が存在せず、当然登録もされない
5. **Domain Reload 直後に古いキャッシュが残っている**: `Cmd/Ctrl + R` で Unity を強制リロード

確認:
```csharp
// Console で以下を実行 (任意の Editor スクリプト経由)
foreach (var cmd in LiminalPalette.Registry.All)
    Debug.Log(cmd.Path);
```

### Q. 引数欄が表示されない / 入力できない

**A**: 引数の型が UI でサポートされていない可能性。

サポート型は [commands.md](commands.md) の「サポートする引数の型」を参照。

任意型は `IParameterEditor` を実装して `ParameterEditorRegistry.Register` で登録すれば追加できる ([extensibility.md](extensibility.md))。

### Q. Runtime UI で文字が黒くて読めない

**A**: 既知の制約。本ライブラリでは対応済みだが、利用側で USS を上書きしている場合に再発する可能性がある。

- Unity 6 の Runtime UIDocument は `:root` セレクタが効かない
- → `var(--lp-*)` の CSS 変数が解決されず、文字色が初期値の暗いグレーになる
- 対策: `Resources/PaletteStyles.uss` で具体色を直接指定 (Phase 3 で実施済み)

利用側で USS を上書きする場合は **`var()` を使わず rgb 値を直接書く**。

### Q. Runtime UI が画面に出ない (Show() しても見えない)

**A**: `PanelSettings.themeStyleSheet` が未設定だと UIDocument は何も描画しない (Unity 6 既定挙動)。

- Phase 3 で `LiminalPalette/UI/Resources/LiminalPaletteRuntimeTheme.tss` を同梱して自動ロードするようになっている
- 利用側で独自の `PanelSettings` asset を `Resources/LiminalPaletteRuntimePanelSettings.asset` に置く場合、`themeStyleSheet` を必ず設定する

確認:
```csharp
var inst = LiminalPaletteRuntime.Instance;
var doc = inst.gameObject.GetComponent<UIDocument>();
Debug.Log($"theme: {doc.panelSettings.themeStyleSheet?.name ?? \"NULL\"}");
```

NULL なら themeStyleSheet を設定する。

### Q. Game ウィンドウのキー入力がパレット閉じた後も止まったまま

**A**: `PaletteInputBlocker` の `Disengage` が呼ばれていない可能性。

- 通常 `Hide()` 内で `Disengage` が呼ばれる
- DontDestroyOnLoad シングルトンが破棄された場合は復元されない
- 強制復元: `LiminalPaletteRuntime.Instance.Hide()` を再度呼ぶ、または Unity Editor を再起動

利用側で `PaletteInputBlocker.OnEngage` / `OnDisengage` を購読している場合、これらのコールバック内で例外を投げると後続の購読者がスキップされるため、try-catch で囲むこと。

### Q. インスタンスメソッドコマンドで「Instance not resolved」エラー

**A**: VContainer に該当型が登録されていない、または `LiminalPaletteEntryPoint` が未登録。

確認:
```csharp
public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // 1. 型が登録されているか
        builder.RegisterComponentInHierarchy<Player>();

        // 2. LiminalPaletteEntryPoint が登録されているか
        builder.RegisterEntryPoint<LiminalPaletteEntryPoint>();
    }
}
```

両方無いと実行時に Fail する。詳細は [integrations.md](integrations.md)。

### Q. Current values セクションが `(instance not resolved)` と表示される

**A**: 上と同じ原因。VContainer 登録 + EntryPoint 登録を確認。

### Q. Current values に何も出ない (セクション自体が消えている)

**A**: 選択コマンドの Path prefix と一致する `[ConsoleObservableField]` がレジストリに無い。

確認:
- `[ConsoleObservableField("Player/Health")]` と `[ConsoleCommand("Player/Health/Set")]` のように prefix を揃える
- `[ConsoleObservableField]` を付けたメンバーが `public` で `ReactiveProperty<T>` または `Observable<T>` を返すか
- `Bootstrap.Initialize` 後に `ObservableFieldRegistry.Default.All` をデバッグ出力して登録されているか確認

```csharp
foreach (var f in ObservableFieldRegistry.Default.All)
    Debug.Log($"{f.Path} ({f.ValueType.Name})");
```

### Q. `R3` namespace not found のコンパイルエラー

**A**: R3 パッケージが未導入。`Packages/manifest.json` に `com.cysharp.r3` を追加。

### Q. `VContainer` namespace not found のコンパイルエラー

**A**: VContainer パッケージが未導入。`Packages/manifest.json` に `jp.hadashikick.vcontainer` を追加。Phase 5a 以降は両方が必須。

---

## Scenario 系

### Q. Scenario タブが空 / 登録したシナリオが出てこない

**A**: いくつか原因が考えられる:

1. **`public` でない**: `[ConsoleScenario]` は public メソッドのみ対応
2. **メソッドが引数を取っている**: 引数なしが必須 (Scanner が弾いて警告ログ)
3. **戻り値が `IEnumerable<ScenarioStep>` でない**: `IList<ScenarioStep>` / `ScenarioStep[]` でも可
4. **`Path` が空文字 / 末尾 `/`**: Scanner が例外で弾く
5. **`#if DEVELOPMENT_BUILD` 等でメソッドを囲んでいる**: 非開発ビルドではメソッド自体が存在しない
6. **テストで `ScenarioRegistry.Default.Clear()` を呼んだ後**: ドメインリロードまでレジストリが空のまま。テスト TearDown で `ScenarioScanner.ScanAll()` を呼んで復元すること

確認:
```csharp
foreach (var s in ScenarioRegistry.Default.All)
    Debug.Log(s.Path);
```

### Q. シナリオ Run で 「ObservableField not found」

**A**: Assert 対象の Path が `[ConsoleObservableField]` で登録されていない。typo / public 漏れ / VContainer 未登録のいずれか。詳細は [scenarios.md](scenarios.md) のトラブルシューティング章。

### Q. 「Scenario already running」になる (HTTP 409 Conflict)

**A**: シナリオ間排他 (`SemaphoreSlim(1, 1)`)。完了を待ってから再投入する。UI 側は Run ボタンが disable されるので発生しにくい。

### Q. シナリオ実行が Log タブには出るのに History タブに出ない

**A**: 仕様。シナリオ内 Command ステップを History に並べると「シナリオ前提の状態を欠いた単独再実行」になり混乱を招くため除外している。シナリオの再実行は **Scenario タブ** から行う。詳細: [scenarios.md](scenarios.md) の `Log / History タブとの連携` 節。

### Q. 物理 / アニメーションの状態を Assert したら値が古い

**A**: `ReactiveProperty<T>.Value = X` への同期書き込みは即時反映されるが、`Update` で計算される状態 (Rigidbody2D / Animator 等) は次フレームを待つ必要がある。`WaitFrames(1)` を間に挟む:
```csharp
yield return ScenarioStep.Run("Player/Position/Teleport", new() { ["x"] = 0f });
yield return ScenarioStep.WaitFrames(1);
yield return ScenarioStep.AssertEquals("Player/Position", new Vector2(0, 0));
```

---

## HTTP API 系

### Q. `curl http://127.0.0.1:7610/api/v1/health` がタイムアウトする

**A**: サーバーが起動していない。

確認手順:
1. Unity Editor が起動しているか
2. `IpcSettings.EnableInEditor = true` か (利用側で false に上書きしていないか)
3. ポート競合で別ポートに移っていないか:
   ```bash
   for port in 7610 7611 7612 7613 7614; do
     curl -s -m 1 http://127.0.0.1:$port/api/v1/health 2>/dev/null && echo "found at $port" && break
   done
   ```
4. Unity Console に「`[LiminalPalette.Ipc] Editor server listening on http://127.0.0.1:XXXX/`」のログが出ているか

### Q. 401 Unauthorized が返る

**A**: トークンが正しく送られていない。

確認:
```bash
# トークンファイルの存在
ls -la ~/.liminal-palette/token

# 中身を確認 (秘密なので画面共有時は注意)
cat ~/.liminal-palette/token | head -c 20
echo

# 末尾改行が入って Trim されない場合があるので tr で削除して送る
TOKEN=$(cat ~/.liminal-palette/token | tr -d '\n\r')
curl -v -H "Authorization: Bearer $TOKEN" http://127.0.0.1:7610/api/v1/commands
```

`-v` で送信された `Authorization` ヘッダを確認。`Bearer ` の後に空白以外の余計な文字が入っていないことを確認。

### Q. 401 Unauthorized が返る (大文字小文字)

**A**: `Bearer` プレフィックスは大小区別 (RFC 6750 準拠)。

- ✅ `Authorization: Bearer abc123`
- ❌ `Authorization: bearer abc123`
- ❌ `Authorization: BEARER abc123`

### Q. 429 Too Many Requests

**A**: `/execute` のレートリミット (既定 30 req/s) を超過。

利用側で緩和:
```csharp
[RuntimeInitializeOnLoadMethod]
static void TweakLimit()
{
    Void2610.LiminalPalette.Ipc.IpcSettings.ExecuteRateLimitPerSecond = 100;
}
```

### Q. 413 Payload Too Large

**A**: リクエスト body が `MaxRequestBodyBytes` (既定 1 MB) を超えた。

利用側で緩和:
```csharp
Void2610.LiminalPalette.Ipc.IpcSettings.MaxRequestBodyBytes = 4 * 1024 * 1024; // 4 MB
```

ただし大きい引数を送る設計より「ファイルパスを引数で受け取って Unity 側で読む」設計に変える方が筋が良い。

### Q. DomainReload (アセンブリ再ロード) 後にポートが取れない

**A**: 旧 listener が残っている可能性。

`EditorIpcBootstrap` は `AssemblyReloadEvents.beforeAssemblyReload` で確実に Stop するため通常は起こらない。発生した場合:
1. Unity Editor を再起動
2. macOS なら `lsof -i :7610` で占有プロセスを確認 (Unity 以外なら kill)

### Q. Editor と Play Mode で同じポートを取り合う

**A**: 仕様。Editor が 7610 を取った後、Runtime は 7611 にずれる (`HttpServer` の自動ポートリトライ)。

```bash
# Editor
curl -s http://127.0.0.1:7610/api/v1/health

# Runtime (Play Mode 中のみ)
curl -s http://127.0.0.1:7611/api/v1/health
```

AI Agent 側で `/health` スキャンする運用を推奨。

### Q. Production ビルドで `lsof -i :7610` に何か listening している

**A**: LiminalPalette の HTTP サーバーは Production ビルドでは **絶対に起動しない**。

- asmdef `Player.Ipc` の `defineConstraints` でコンパイル除外
- ビルドログに「`Void2610.LiminalPalette.Player.Ipc` skipped」と出る

`lsof` で何か出る場合、それは LiminalPalette ではない別プロセス (調べる)。

念のためバイナリ内のシンボル確認:
```bash
strings Build/MyGame.app/Contents/Resources/Data/Managed/*.dll | grep "LiminalPalette.Ipc.Server"
# → 何も出ない (= 正しく除外されている)
```

### Q. JSON が壊れて返ってくる

**A**: Phase 4 修正後はすべての制御文字 (`\t` `\b` `\f` U+0000-U+001F) が `\uXXXX` で正しくエスケープされる。

それでも壊れる場合:
- Encoding が UTF-8 でない可能性 (`Content-Type: application/json; charset=utf-8` を確認)
- プロキシ / WAF が body を改変している可能性

---

## ビルド / asmdef 系

### Q. コンパイルエラー: `'Void2610.LiminalPalette.UI' could not be found`

**A**: 利用側 asmdef の references から漏れている。

UI / Editor / Player.InputSystem / Player.Ipc は `autoReferenced: false` または `defineConstraints` 付きなので、必要なら明示参照する:

```json
"references": [
    "Void2610.LiminalPalette",
    "Void2610.LiminalPalette.UI"
]
```

詳細は [asmdef.md](asmdef.md)。

### Q. Production ビルドが落ちる: `RuntimeIpcBootstrap` 関連エラー

**A**: 起こりえない (asmdef ごとコンパイル除外されているため)。

別の原因:
- 利用側コードが `Void2610.LiminalPalette.Player.Ipc` を直接参照している → `#if UNITY_EDITOR || DEVELOPMENT_BUILD` で囲む
- Player Settings の Scripting Define Symbols に矛盾がある

### Q. Runtime ホットキー検出はどの入力経路を使っている？

**A**: IMGUI (`UnityEngine.Event`) ベースの `EventPaletteInput` に一本化済み。`PaletteInputFactory` は分岐を持たず常にこれを返す。

利用側プロジェクトの Active Input Handler (Legacy / Input System / Both) の設定に依存せず動く。

経緯と理由は [asmdef.md の「Runtime ホットキー検出に InputSystem を使わない理由」節](asmdef.md) 参照。要点だけ:

- macOS で Cmd+P から Play Mode に入ると、InputSystem 側で Cmd が `isPressed=true` のまま固着する Unity 既知挙動がある。
- これにより K 単独押下が Cmd+K として通り、パレットが誤発火していた。
- IMGUI の KeyDown は OS イベントキュー由来で固着しないため、IMGUI に切り替えて根治した。
- 旧 `InputSystemPaletteInput` および `PaletteInputFactory.OverrideFactory` は削除済み。

### Q. macOS で Cmd+P (Play Mode 開始) 直後に K 単体でパレットが開いてしまう

**A**: 上記の Cmd 固着問題。すでに IMGUI 実装に切り替えて修正済み (commit `a6e2e14`)。本パッケージを最新化すれば再発しない。

もし再発しているなら以下を確認:
- `LiminalPaletteRuntime` を継承・上書きして独自に `IPaletteInput` を差し替えていないか
- `PaletteInputFactory.Create` が `EventPaletteInput` を返しているか (テスト `PaletteInputFactoryTests` でも検証している)
- `LiminalPaletteRuntime.OnGUI` が呼ばれる経路を `enabled=false` 等で塞いでいないか

---

## テスト系

### Q. `RuntimeColorEditorTests` でコールバックが発火しない

**A**: UI Toolkit の `BaseField.value` セッタは panel 接続が無いと `SendEvent` を空振りする。

テストでは:
- `EditorWindow.ShowUtility()` で一時 panel を作って `Add(visualElement)`
- もしくは `SetValueWithoutNotify` + 手動 `SendEvent` で発火

詳細は `Tests/Editor/UI/RuntimeColorEditorTests.cs` 参照。

### Q. `PlayerPrefsCommandHistoryTests` でファイルが残る

**A**: `[SetUp]` / `[TearDown]` で `PlayerPrefs.DeleteKey` + `PlayerPrefs.Save()` を呼ぶ。

`TokenStoreTests` も同様 (`TokenStore.DeleteForTest()`)。テスト後の cleanup 漏れに注意。

---

## 既知の制約 (Phase 1〜5a で確立)

| 項目 | 制約 |
|---|---|
| `[ConsoleCommand]` のメソッド | `public static` または `public instance method`。インスタンスは VContainer の `IObjectResolver.Resolve(type)` で解決される。VContainer に未登録の型は実行時に「Instance not resolved」エラーで Fail |
| 必須依存 | R3 + VContainer の両方が必須 (Phase 5a 以降)。両方未導入のプロジェクトでは Core asmdef がコンパイルできない |
| 複数インスタンス | `IObjectResolver.Resolve(typeof(T))` は単一インスタンスを返す前提。`Player[0]` / `Player[1]` のような区別はサポート外 |
| `[ConsoleObservableField]` の値型 | `ReactiveProperty<T>` (推奨) / `Observable<T>`。後者は subscribe 後に値が来るまで `(no value)` 表示 |
| `LogCapture` | 並列実行で混線する可能性 (1 ホスト 1 コマンドが前提) |
| `Time.timeScale` | パレット表示中もゲーム時間は進む。ポーズが必要なら利用側で `OnEngage` / `OnDisengage` 購読 |
| `PaletteInputBlocker` | `Update()` で `Input.GetKey` を直接読むコードは止められない (ベストエフォート) |
| Runtime ショートカット | UIDocument にフォーカスが無いと PaletteView の `KeyDownEvent` が届かないことがある |
| WebGL / iOS / Android | Phase 4 までスコープ外。WebGL は HttpListener が動かないため要別実装。Mobile はソフトキーボード対応未検証 |
| `:root` セレクタ | Runtime UIDocument で機能しない (Unity 6)。USS は具体色を直書きする |
| トークンの保護 | OS のファイル権限任せ (chmod 600 / NTFS ACL) |
| 動的コマンド登録 API | HTTP 経由は **意図的に未対応** (任意コード実行リスク) |
| HTTPS | 未対応 (localhost only 前提)。LAN 越しは Tailscale / SSH トンネル経由 |

---

## サポート

不明点があれば本リポジトリ `Docs/debug-console/phase{1..4}-implementation-notes.md` を確認。設計判断の経緯がフェーズごとに残っている。

それでも解決しない場合は GitHub Issue を立てる (Phase 5 で公式リポジトリに切り出した際にテンプレを追加予定)。
