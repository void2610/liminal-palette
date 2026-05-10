# Getting Started

LiminalPalette を最短で動かす手順。所要 5 分。

## 前提条件

- Unity **6000.3** 以降
- C# 9 以降 (`.NET Standard 2.1` ビルド設定)
- 入力: Legacy Input Manager / Input System Package のどちらでも可
- **R3** ([Cysharp/R3](https://github.com/Cysharp/R3)) — 必須 (Phase 5a 以降)
- **VContainer** ([hadashiA/VContainer](https://github.com/hadashiA/VContainer)) — 必須 (Phase 5a 以降)

`Packages/manifest.json`:
```json
{
  "dependencies": {
    "com.cysharp.r3": "https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity",
    "jp.hadashikick.vcontainer": "1.x.y"
  }
}
```

## インストール

### 方法 A: 本リポジトリ内 (現状)

このリポジトリ (apocalyptic-apartment-hunting) を clone するだけで `Assets/Plugins/LiminalPalette/` が同梱されているため、追加の手順は不要。Unity を開けばコマンドパレットが既に動く状態になっている。

### 方法 B: 別プロジェクトに導入

`Assets/Plugins/LiminalPalette/` フォルダを丸ごと別プロジェクトの `Assets/Plugins/` 直下にコピー。`.meta` ファイルもセットでコピーすること (asmdef の guid を維持するため)。

```bash
cp -r /path/to/source/Assets/Plugins/LiminalPalette /path/to/target/Assets/Plugins/
```

### 方法 C: UPM 経由

git URL で導入可能:

```json
// Packages/manifest.json
{
  "dependencies": {
    "com.void2610.liminal-palette": "https://github.com/void2610-org/liminal-palette.git#v0.5.0"
  }
}
```

## Hello World

### 1. コマンドメソッドを書く

任意の C# ファイルを作成 (例: `Assets/Scripts/MyCommands.cs`):

```csharp
using R3;
using UnityEngine;
using Void2610.LiminalPalette;

public class Player : MonoBehaviour
{
    public ReactiveProperty<int> Hp { get; } = new(100);

    // [ConsoleObservableField] で現在値が UI に表示される (R3 push 駆動で自動更新)
    [ConsoleObservableField("Player/Health")]
    public ReactiveProperty<int> HpField => Hp;

    // インスタンスメソッドを [ConsoleCommand] でパレットから叩ける (VContainer 経由解決)
    [ConsoleCommand("Player/Health/Set", Description = "プレイヤーの HP を設定する")]
    public void SetHealth(int value) => Hp.Value = value;

    [ConsoleCommand("Player/Health/Damage", Description = "ダメージを受ける")]
    public void Damage(int amount) => Hp.Value = Mathf.Max(0, Hp.Value - amount);
}

// 静的メソッドも従来通り使える (インスタンス不要)
public static class TimeCommands
{
    [ConsoleCommand("Editor/Time/SlowMotion", Description = "Time.timeScale = 0.25")]
    public static void SlowMotion() => Time.timeScale = 0.25f;
}
```

ポイント:
- メソッドは `public static` または `public` インスタンス
- インスタンスメソッドは VContainer に登録された型のみ実行可能
- `[ConsoleObservableField]` で `ReactiveProperty<T>` を直接公開して現在値を UI に表示
- 戻り値の型は何でも OK (`void` / プリミティブ / `Task<T>` / `Vector3` 等)
- 引数のデフォルト値も尊重される

### 1.5. VContainer に登録

`Player` のインスタンスメソッドを叩けるようにするため、`LifetimeScope` で型を登録 + `LiminalPaletteEntryPoint` を 1 行追加:

```csharp
using VContainer;
using VContainer.Unity;
using Void2610.LiminalPalette.Integration.VContainer;

public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterComponentInHierarchy<Player>();
        builder.RegisterEntryPoint<LiminalPaletteEntryPoint>();   // ← これでパレットが VContainer 経由で解決
    }
}
```

詳細は [integrations.md](integrations.md)。

### 2. パレットを開く

Unity に戻ると、AttributeScanner が起動時に全 Assembly をスキャンしてコマンドを登録する。

**Editor 上で**:
- macOS: `Cmd + K`
- Windows / Linux: `Ctrl + K`

`LiminalPalette` ウィンドウが開き、検索ボックスに "Hello Echo" や "Hello/Add" と入力すると候補が絞られる。

### 3. 引数を入力して実行

- "Hello/Echo" を選択 → 引数欄に `message` の TextField が出る
- 適当な文字列を入れて **Run Command** を押す
- Unity Console に `[Echo] 入力した文字列` が出力される

### 4. Play Mode でも動く

Play Mode (▶ ボタン) を押してから、**Game ウィンドウにフォーカスを置いて** `Cmd/Ctrl + K` を押す。

半透明の黒オーバーレイ越しにゲーム画面が薄く見える状態でパレットが開く。Editor とまったく同じ操作で実行できる。

> Editor / Game ウィンドウのフォーカスにより自動で開閉先が切り替わる (Editor 側に居れば `LiminalPaletteWindow`、Game 側に居れば `LiminalPaletteRuntime`)。両者は競合しない。

### 5. HTTP API でも叩ける

Unity Editor を起動した状態で、別ターミナルから:

```bash
TOKEN=$(cat ~/.liminal-palette/token)

# 動作確認 (認証不要)
curl -s http://127.0.0.1:7610/api/v1/health
# → {"status":"ok","version":"0.4.0","commandCount":...}

# コマンド一覧 (認証必須)
curl -s -H "Authorization: Bearer $TOKEN" \
     http://127.0.0.1:7610/api/v1/commands | grep -A 2 '"path":"Hello'

# 実行
curl -s -H "Authorization: Bearer $TOKEN" \
     -H "Content-Type: application/json" \
     -X POST http://127.0.0.1:7610/api/v1/execute \
     -d '{"path":"Hello/Echo","args":{"message":"Hi"}}'
# → {"success":true,"value":null,...}
```

詳細は [ipc.md](ipc.md) を参照。

## 動作確認チェックリスト

- Editor で `Cmd/Ctrl+K` を押すとパレットが開く
- 検索ボックスに `Hello` を入力すると `Hello/Echo` と `Hello/Add` が候補に出る
- `Hello/Echo` を選択して引数を入力 → Run Command で Unity Console にログが出る
- Play Mode で `Cmd/Ctrl+K` を押すと半透明 overlay でパレットが開く
- `curl http://127.0.0.1:7610/api/v1/health` が `200 ok` を返す

ここまで動けば導入完了。次は [commands.md](commands.md) で `[ConsoleCommand]` の全機能を学ぶ。

## Hello Scenario (5 分)

複数コマンドをまとめて 1 クリックで再生したい / CI から統合テストとして叩きたいなら **Scenario** を使う。`[ConsoleScenario]` を付けたメソッドが `IEnumerable<ScenarioStep>` を返すだけ:

```csharp
using System.Collections.Generic;
using Void2610.LiminalPalette;

public static class HelloScenarios
{
    [ConsoleScenario("Hello/Smoke", Description = "echo + add の連続実行")]
    public static IEnumerable<ScenarioStep> Smoke()
    {
        yield return ScenarioStep.Run("Hello/Echo", new() { ["message"] = "hi" });
        yield return ScenarioStep.Run("Hello/Add", new() { ["a"] = 2, ["b"] = 3 });
    }
}
```

Cmd+K → **Scenario タブ** に `Hello/Smoke` が並ぶ。Run Scenario で 2 ステップが順次走り、各ステップの ✓ + 所要時間が表示される。

`[ConsoleObservableField]` で公開した値を Assert ステップで検証すれば統合テストにできる。詳細は [scenarios.md](scenarios.md) を参照。


## トラブル時

- **`Cmd+K` で開かない**: Editor / Game ウィンドウどちらにフォーカスがあるか確認。Project ウィンドウなど他にフォーカスがあると Editor の Shortcut が効かないことがある
- **コマンドがリストに出ない**: `[ConsoleCommand]` を付けたメソッドが `public static` か確認
- **引数欄が表示されない**: パラメータの型が UI でサポートされているか確認 (詳細は [commands.md](commands.md) の「サポート型」)
- **HTTP API が動かない**: Editor を開いているか確認。トークンファイル `~/.liminal-palette/token` の存在も確認
- **その他**: [troubleshooting.md](troubleshooting.md) を参照
