# LiminalPalette ドキュメント

このディレクトリには LiminalPalette の詳細ドキュメントが置かれている。最初に読む順序の推奨:

1. **[getting-started](getting-started.md)** — インストールと最初のコマンド (5 分)
2. **[integrations](integrations.md)** — R3 + VContainer 統合 (必須)
3. **[commands](commands.md)** — `[ConsoleCommand]` / `[ConsoleObservableField]` でできることのすべて
4. **[scenarios](scenarios.md)** — `[ConsoleScenario]` でコマンドチェイン (= デバッグ再現 + 統合テスト)
5. **[ui](ui.md)** — Editor Window と Runtime UI の使い分け、Current values セクション
6. **[ipc](ipc.md)** — AI Agent / curl から叩く HTTP API (`/state` / `/scenarios` 含む)
7. **[extensibility](extensibility.md)** — `ITypeConverter` / `IParameterEditor` / `IInstanceResolver` で拡張する
8. **[asmdef](asmdef.md)** — どの asmdef がどこに依存するか (内部構造)
9. **[security](security.md)** — localhost / トークン / Production 除外の原則
10. **[troubleshooting](troubleshooting.md)** — 困ったとき

---

## 全体構成 (実装が頭に入っている人向け)

```
[ ユーザーのコード ]
         │
         │ [ConsoleCommand] 属性
         ↓
[ AttributeScanner ]                      ← 起動時に全 Assembly をリフレクションスキャン
         ↓
[ CommandRegistry ]                       ← Path → CommandDescriptor のテーブル
         │
         ↓
[ CommandExecutor ]                       ← 引数バインド / async unwrap / Log capture / try-catch
         ↑       ↑       ↑
   [Editor]  [Runtime]  [HTTP API]        ← 3 つのホスト
   Cmd+K    Cmd+K       /api/v1/execute
```

中核は `CommandRegistry + CommandExecutor` の 2 つ。UI と IPC はその上に乗る対称なクライアントとして実装されている。

## 用語

| 用語 | 意味 |
|---|---|
| **Path** | コマンドの識別子。`Category/Subcategory/Action` 形式 (例: `Player/Health/Set`) |
| **Descriptor** | コマンドの不変メタデータ (`CommandDescriptor`)。Path / 引数スキーマ / 戻り値型を持つ |
| **Result** | 実行結果 (`CommandResult`)。Success / Value / Error / Logs / Duration |
| **Invocation** | 1 回の実行記録 (`CommandInvocation`)。Path + 引数 + Result + Timestamp |
| **Palette** | UI 全体 (`PaletteView`、`VisualElement`)。Editor / Runtime で同じものをホストする |
| **Host** | パレットを表示する側 (Editor: `LiminalPaletteWindow`、Runtime: `LiminalPaletteRuntime`) |
| **IPC** | HTTP API のこと。`HttpServer` + 7 エンドポイント (health / commands / execute / logs / state / scenarios / scenarios/run) |
| **InstanceResolver** | インスタンスメソッド `[ConsoleCommand]` のインスタンス解決経路 (`IInstanceResolver`)。VContainer 統合経由で設定される |
| **ObservableField** | `[ConsoleObservableField]` で公開された読み取り専用状態。`ReactiveProperty<T>` / `Observable<T>` を保持し、UI が R3 push 駆動で表示 |
| **Scenario** | `[ConsoleScenario]` で宣言したコマンドチェイン。Run / Wait / Assert ステップを順次実行し、デバッグ再現 + 統合テストとして使う |

## ファサード API

ほとんどのケースで `Void2610.LiminalPalette.LiminalPalette` の static メソッドだけ使えば足りる:

```csharp
using Void2610.LiminalPalette;

// コマンド実行 (文字列引数)
var result = await LiminalPalette.ExecuteAsync("Player/Health/Set",
    new Dictionary<string, string> { ["value"] = "100" });

// 型解決済み引数で実行 (UI 経路)
var result2 = await LiminalPalette.ExecuteWithTypedArgsAsync("Test/Vector",
    new Dictionary<string, object> { ["v"] = new Vector3(1, 2, 3) });

// Registry にアクセス (動的登録 / 検索)
var commands = LiminalPalette.Registry.All;

// 利用側 ITypeConverter 登録
LiminalPalette.RegisterTypeConverter(new MyCustomConverter());

// インスタンス解決経路の差替 (通常は Integration.VContainer の LiminalPaletteEntryPoint が自動でやる)
LiminalPalette.SetInstanceResolver(new MyCustomResolver());

// シナリオ実行 (名前指定 / ad-hoc)
var sr = await LiminalPalette.RunScenarioAsync("Combat/EnemyTakesDamage");
```

詳細は [commands.md](commands.md) / [scenarios.md](scenarios.md) / [extensibility.md](extensibility.md) を参照。

## 設計原則 (Phase 1〜4 で確立)

- **`Assets/Plugins/` 配置**: プロジェクトの `R3.Subject` 自動置換 CodeFix を回避するため、`event Action<T>` ベースを維持
- **外部依存ゼロ**: `Newtonsoft.Json` / `Utf8Json` / `UniTask` 等を引き込まない
- **Editor / Runtime 共有シングルトン**: `CommandRegistry.Default` と `InvocationStore.Instance` はプロセス共通
- **三重防御の Production 除外**: asmdef `defineConstraints` + `ProductionGuard` + 設定フラグで HTTP / 機能を Player ビルドから完全に外せる
- **localhost only HTTP**: `127.0.0.1` と `localhost` のみバインド。`0.0.0.0` には絶対にしない

詳細は [security.md](security.md) と [asmdef.md](asmdef.md) を参照。

---
