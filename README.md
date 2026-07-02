# LiminalPalette

VS Code のコマンドパレット風 UI を持つ、Unity 用のデバッグコンソール / コマンド実行ライブラリ。

`[LiminalCommand]` を付けた C# メソッドを **Editor / Runtime / HTTP API の 3 経路から統一的に実行できる**。AI Agent (Claude Code 等) や CLI / Discord bot から `curl` 一発でゲーム操作を自動化したいケースに特化している。

---

## できること

- **ファジー検索付きコマンドパレット** (Editor / Runtime 両対応、UI Toolkit 製)
- **4 タブ構成**: Command (新規実行) / Scenario (コマンドチェイン実行) / Log (起動履歴の詳細閲覧) / History (再実行特化)。`Tab` / `Shift+Tab` でタブ巡回
- **型解決済み引数 UI**: `int` / `float` / `string` / `bool` / `enum` / `Vector2/3/4` / `Color` / `[Flags] enum` / `UnityEngine.Object` 派生
- **HTTP API**: ローカル localhost で `/api/v1/{health, commands, execute, logs, state, scenarios, scenarios/run}` を提供。Bearer トークン認証 + レートリミット
- **Production ビルド除外**: `defineConstraints` の三重防御で Player ビルドにシンボル混入なし
- **拡張点**: `ITypeConverter` / `IParameterEditor` / `ICommandHistory` で利用側が拡張可能

---

## 動作要件

- Unity **6000.3** 以降 (UI Toolkit Runtime support 利用)
- .NET Standard 2.1 / C# 9 以降
- 入力: Legacy Input Manager / Input System Package のどちらでも動く (両方有効でも可)
- **R3** ([Cysharp/R3](https://github.com/Cysharp/R3)) — `ReactiveProperty<T>` / `Observable<T>` 対応 (Phase 5a 以降必須)
- **VContainer** ([hadashiA/VContainer](https://github.com/hadashiA/VContainer)) — インスタンスメソッドコマンドの解決 (Phase 5a 以降必須)
- **UniTask** ([Cysharp/UniTask](https://github.com/Cysharp/UniTask)) — `UniTask` / `UniTask<T>` 戻り値コマンドの await (v0.9 以降必須)

> Phase 4 までは外部依存ゼロだったが、Phase 5a で R3 + VContainer 必須に方針転換した。利用側のコード量を最小化する設計判断。

---

## クイックスタート (3 ステップ)

### 1. コマンドを書く

```csharp
using R3;
using UnityEngine;
using Void2610.LiminalPalette;

public class Player : MonoBehaviour
{
    public ReactiveProperty<int> Hp { get; } = new(100);

    [LiminalObservableField("Player/Health")]
    public ReactiveProperty<int> HpField => Hp;          // 現在値が UI に常時表示される

    [LiminalCommand("Player/Health/Set", Description = "プレイヤーの HP を設定する")]
    public void SetHealth(int value) => Hp.Value = value;  // インスタンスメソッドでも OK
}
```

### 2. VContainer に登録

```csharp
public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterComponentInHierarchy<Player>();
        builder.RegisterEntryPoint<LiminalPaletteEntryPoint>();   // 1 行で接続完了
    }
}
```

### 3. パレットを開いて実行

- **Editor**: `Cmd/Ctrl + K` でパレットを開き、"Player Set" などで検索 → 引数欄上部に現在 HP が表示 → Run で実行。
- **Play Mode** (ゲーム実行中): 同じ `Cmd/Ctrl + K` で半透明 overlay として開く。値は R3 push 駆動で自動更新。

### 3. HTTP API で叩く (任意)

```bash
TOKEN=$(cat ~/.liminal-palette/token)

curl -s -H "Authorization: Bearer $TOKEN" \
     -H "Content-Type: application/json" \
     -X POST http://127.0.0.1:7610/api/v1/execute \
     -d '{"path":"Player/Health/Set","args":{"value":"100"}}'
# → {"success":true,"value":null,"durationMs":0.51,...}
```

トークンは Editor 起動時に `~/.liminal-palette/token` へ自動生成される。

---

## AI Agent 連携 (Claude Code Skills)

Claude Code から HTTP API を叩くための **Agent Skills** を package に同梱している。利用側プロジェクトを Unity で開いた状態で:

```
Tools > LiminalPalette > Install AI Skills...
```

を選ぶと、プロジェクトルートの `.claude/skills/` に 8 個の `liminal-*` skill (liminal-overview / liminal-find-port / liminal-list-commands / liminal-execute / liminal-get-state / liminal-get-logs / liminal-list-scenarios / liminal-run-scenario) がコピーされる。Claude Code を再起動すれば skill が認識され、「LP のコマンド一覧」「Player/Health/Set を 50 で実行」のような指示で curl 操作が自動選択される。

アンインストール: `Tools > LiminalPalette > Uninstall AI Skills`。

skill ファイル本体は package 内の `AISkills~/` に同梱されており、上書きインストールで常に LP の最新版に同期する。

---

## ドキュメント

詳細は `Documentation~/` 配下を参照:

| 章 | 内容 |
|---|---|
| [index](Documentation~/index.md) | ドキュメントのハブ + 全体構成図 |
| [getting-started](Documentation~/getting-started.md) | インストール / Hello World / 最初の動作確認 |
| [commands](Documentation~/commands.md) | `[LiminalCommand]` の全機能、引数の型、async、動的登録 |
| [ui](Documentation~/ui.md) | Editor Window / Runtime UI / ショートカット / 入力ブロッカー |
| [ipc](Documentation~/ipc.md) | HTTP API リファレンス + curl 例 + AI Agent 連携 |
| [extensibility](Documentation~/extensibility.md) | `ITypeConverter` / `IParameterEditor` / `ICommandHistory` で利用側拡張 |
| [asmdef](Documentation~/asmdef.md) | 8 つの asmdef 構成と依存ルール / `defineConstraints` |
| [security](Documentation~/security.md) | localhost only / トークン / Production 除外 / レートリミット |
| [troubleshooting](Documentation~/troubleshooting.md) | よくある問題と既知の制約 |

---

## インストール

### A. Unity Package Manager (git URL)

`Packages/manifest.json` に以下を追加する:

```json
{
  "dependencies": {
    "com.void2610.liminal-palette": "https://github.com/void2610/liminal-palette.git",
    "com.cysharp.r3": "https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity",
    "jp.hadashikick.vcontainer": "https://github.com/hadashiA/VContainer.git?path=VContainer/Assets/VContainer"
  }
}
```

R3 と VContainer は Phase 5a 以降必須なので、利用側 manifest で別途インストールする必要がある (UPM の `dependencies` には git URL を書けないため、本パッケージの `package.json` には記載していない)。

特定のリリースに固定したい場合はタグを付ける:

```json
"com.void2610.liminal-palette": "https://github.com/void2610/liminal-palette.git#v0.1.0"
```

### B. ローカルパス (パッケージ開発時)

開発中のローカルクローンを参照する場合:

```json
"com.void2610.liminal-palette": "file:../../liminal-palette"
```

> 相対パスは利用側プロジェクトの `Packages/` フォルダから見て解決される。


---

## ライセンス

[MIT License](LICENSE.md) — Copyright (c) 2026 void2610

---

## 作者

[void2610](https://github.com/void2610)
