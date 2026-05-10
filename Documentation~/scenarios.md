# Scenarios

`[LiminalScenario]` 属性で「コマンドを順次実行するシナリオ」を C# で宣言する仕組み。
ボタン 1 つで「敵スポーン → アイテム所持 → 特定ステージへワープ」のような連続操作を再現できるほか、HTTP API (`/api/v1/scenarios/run`) 経由で CI から走らせて統合テストとしても使える。

---

## 最小例

```csharp
using System.Collections.Generic;
using Void2610.LiminalPalette;

public static class CombatScenarios
{
    [LiminalScenario("Combat/EnemyTakesDamage", Description = "敵にダメージを与えて HP が減ることを検証")]
    public static IEnumerable<ScenarioStep> EnemyTakesDamage()
    {
        yield return ScenarioStep.Run("Enemy/Spawn", new() { ["type"] = "Goblin" });
        yield return ScenarioStep.AssertEquals("Enemy/Hp", 100, "spawn 直後は満タン");
        yield return ScenarioStep.Run("Enemy/Damage", new() { ["amount"] = 30 });
        yield return ScenarioStep.WaitFrames(1);
        yield return ScenarioStep.AssertEquals("Enemy/Hp", 70, "30 ダメージ後は 70");
    }
}
```

これで Cmd+K → **Scenario タブ** に `Combat/EnemyTakesDamage` が並ぶ。**Run Scenario** で全ステップが順次走り、各ステップの ✓ / ✗ と所要時間が下部に表示される。

---

## ステップ種別

ファクトリは `ScenarioStep` の static メソッドで提供される。

| ステップ | ファクトリ | 説明 |
|---|---|---|
| Command | `ScenarioStep.Run(path, args, description)` | `[LiminalCommand]` を呼ぶ。引数は `IReadOnlyDictionary<string, object>` (型解決済み)。失敗で fail-fast |
| WaitSeconds | `ScenarioStep.WaitSeconds(seconds, description)` | 実時間で待機 (`Task.Delay`) |
| WaitFrames | `ScenarioStep.WaitFrames(frames, description)` | フレーム数で待機。Edit Mode は `EditorApplication.update` tick、Play Mode / Player ビルドは `Time.frameCount` |
| AssertEquals | `ScenarioStep.AssertEquals(observableFieldPath, expected, description)` | `[LiminalObservableField]` の現在値が `expected` と一致 |
| AssertNotEquals | `ScenarioStep.AssertNotEquals(observableFieldPath, unexpected, description)` | 上記の否定 |

`description` は省略可。指定するとシナリオ結果の各ステップ行に表示されて読み手にとって意図が分かりやすくなる。

### Assert の expected 型

- `expected` が `string` で `ObservableField.ValueType` が string 以外なら、`TypeConverterRegistry.TryConvert` で変換してから比較する。HTTP の ad-hoc 経路は文字列推奨 (= JSON との往復で型が落ちるため)。
- C# 直書きの場合は素の値を渡せる: `ScenarioStep.AssertEquals("Enemy/Hp", 100)`。

---

## `[LiminalScenario]` の全パラメータ

```csharp
[LiminalScenario(
    path:        "Category/Subcategory/Action",
    Description: "ヒトに見せる説明"
)]
```

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| `Path` | `string` | ✅ | "/" 区切り。Command と同じバリデーション (空・先頭/末尾 `/` 不可) |
| `Description` | `string` | — | UI / `/api/v1/scenarios` で表示される説明文 |

> **Note**: Production 除外はビルド単位の防御層 (asmdef defineConstraints + `ProductionGuard` + `LIMINAL_PALETTE_DISABLED` define) で行う。個別シナリオだけ除外したい場合は `#if DEVELOPMENT_BUILD` 等で対応する。

---

## メソッドの形

| 要件 | 必須 | 備考 |
|---|---|---|
| `public` | ✅ | `private` / `internal` は登録されない |
| 静的 / インスタンス | — | 両方対応 (Command と同じ。インスタンスは VContainer で解決) |
| 引数なし | ✅ | 引数を取るメソッドは Scanner が弾く |
| 戻り値 | `IEnumerable<ScenarioStep>` | `IList<ScenarioStep>` / `ScenarioStep[]` でも可 |

不正なシグネチャは `ScenarioScanner` が `Debug.LogWarning` でスキップ理由を通知する。

### static の例

```csharp
public static class CombatScenarios
{
    [LiminalScenario("Combat/Smoke")]
    public static IEnumerable<ScenarioStep> Smoke()
    {
        yield return ScenarioStep.Run("Player/Health/FullHeal");
    }
}
```

### インスタンスの例 (VContainer)

```csharp
public sealed class CombatScenarios
{
    private readonly EnemySpawner _spawner;

    public CombatScenarios(EnemySpawner spawner) { _spawner = spawner; }

    [LiminalScenario("Combat/EnemyTakesDamage")]
    public IEnumerable<ScenarioStep> EnemyTakesDamage()
    {
        yield return ScenarioStep.Run("Enemy/Spawn", new() { ["type"] = _spawner.DefaultType });
        // ...
    }
}
```

`LifetimeScope.Configure` で型を登録 + `LiminalPaletteEntryPoint` を登録するだけ:
```csharp
builder.Register<CombatScenarios>(Lifetime.Singleton);
builder.RegisterEntryPoint<LiminalPaletteEntryPoint>();
```

詳細: [integrations.md](integrations.md)

---

## yield return パターンの注意

`IEnumerable<ScenarioStep>` を返す `yield return` メソッドは **呼び出すたびに新しい列挙子を返す**。これは:
- 同じシナリオを連続実行 → 各回でステップ列が新規生成される (安全)
- `yield return` 行間に副作用 (Debug.Log・状態変更等) を書く → 毎実行で発火する (仕様)

**注意**: `Scenario タブの Steps 列表示` と `/api/v1/scenarios` の `stepCount` 取得時にも、ステップ列を 1 度 enumerate する。副作用付きの生成は表示用呼び出しでも発火するので、**ステップ列生成は純粋に保つこと**。重い処理 / I/O は最初のステップ内に書く。

---

## ファサード API

```csharp
using Void2610.LiminalPalette;

// 名前指定で実行
ScenarioResult result = await LiminalPalette.RunScenarioAsync("Combat/EnemyTakesDamage");

// ad-hoc にステップ列を直接渡す
var steps = new[]
{
    ScenarioStep.Run("Enemy/Spawn", new() { ["type"] = "Goblin" }),
    ScenarioStep.WaitFrames(1),
    ScenarioStep.AssertEquals("Enemy/Hp", 100),
};
ScenarioResult result2 = await LiminalPalette.RunScenarioAsync(steps);

// レジストリにアクセス
var all = LiminalPalette.Scenarios.All;
```

`ScenarioResult` の主なフィールド:
- `Success` (`bool`) — 全ステップ Pass で `true`
- `Steps` (`IReadOnlyList<StepResult>`) — 実行された分のみ (fail-fast 後は途中まで)
- `FailedAtStep` (`int`) — 最初に失敗したステップの index、無ければ `-1`
- `Duration` (`TimeSpan`) — 全体所要時間
- `WasRejectedAsAlreadyRunning` (`bool`) — シナリオ排他で弾かれた場合 `true`

---

## 実行モデル

- **fail-fast**: 最初の失敗で打ち切る。途中失敗時の `Steps` はその直前まで
- **シナリオ間排他**: `SemaphoreSlim(1, 1)` で 1 並列。実行中に別シナリオが来ると即座に `WasRejectedAsAlreadyRunning = true` で返る (待たない)
- **通常コマンドとの並列**: 制限なし。シナリオ実行中も Cmd+K → コマンド単独実行は可能
- **メインスレッド**: HTTP 経由は `MainThreadDispatcher` で marshal される。`Time.frameCount` 等メインスレッド限定 API も安全に触れる

### 副作用の保護: lock 取得は StepsFactory より前

並行 2 リクエストが Named 実行に来ても、**lock 取得前に `StepsFactory(instance)` が消費されることはない**。AlreadyRunning で弾かれる側では `yield` 内の副作用も発火しない。

---

## Log / History タブとの連携

シナリオ内の Command ステップは **`InvocationStore` に記録される** が、`IsFromScenario = true` でマークされる:

| タブ | シナリオ内 Command | シナリオ集約 (`Scenario/<path>`) | 直接実行 |
|---|---|---|---|
| Log | ✅ 表示 (詳細閲覧用) | ✅ 表示 | ✅ 表示 |
| History | ❌ 除外 | ❌ 除外 | ✅ 表示 (再実行可) |

シナリオ内コマンドを History に並べると **シナリオ前提の状態 (例: HP 満タン) を欠いた単独再実行** になり、UX として混乱するため除外する。シナリオの再実行は **Scenario タブ** から行う。

---

## HTTP API

### `GET /api/v1/scenarios` — 一覧

```bash
curl -H "Authorization: Bearer $TOKEN" http://127.0.0.1:7610/api/v1/scenarios
```

```json
{
  "scenarios": [
    {
      "path": "Combat/EnemyTakesDamage",
      "description": "敵にダメージを与えて HP が減ることを検証",
      "stepCount": 5
    }
  ]
}
```

`stepCount` が `-1` の場合はインスタンス未解決等で計測不能 (UI では "?" と表示)。

### `POST /api/v1/scenarios/run` — 名前指定実行

```bash
curl -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
    http://127.0.0.1:7610/api/v1/scenarios/run \
    -d '{"path": "Combat/EnemyTakesDamage"}'
```

### `POST /api/v1/scenarios/run` — ad-hoc 実行

```bash
curl -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
    http://127.0.0.1:7610/api/v1/scenarios/run \
    -d '{
      "steps": [
        {"type": "command", "path": "Enemy/Spawn", "args": {"type": "Goblin"}},
        {"type": "assert_equals", "path": "Enemy/Hp", "expected": "100"},
        {"type": "command", "path": "Enemy/Damage", "args": {"amount": "30"}},
        {"type": "wait_frames", "frames": 1},
        {"type": "assert_equals", "path": "Enemy/Hp", "expected": "70"}
      ]
    }'
```

ステップ JSON のフィールド:

| `type` | 必須フィールド | オプション |
|---|---|---|
| `command` | `path` (string), `args` (object) | `description` |
| `wait_seconds` | `seconds` (number) | `description` |
| `wait_frames` | `frames` (integer) | `description` |
| `assert_equals` | `path` (string), `expected` (string\|number\|bool\|null) | `description` |
| `assert_not_equals` | `path` (string), `expected` | `description` |

`path` と `steps` は **排他**。両方 / どちらも未指定は 400 BadRequest。

### レスポンス

```json
{
  "success": false,
  "durationMs": 124.3,
  "failedAtStep": 4,
  "path": "Combat/EnemyTakesDamage",
  "alreadyRunning": false,
  "steps": [
    {"kind": "Command", "success": true, "durationMs": 1.2, "commandPath": "Enemy/Spawn", "args": {"type": "Goblin"}, "commandResult": {...}},
    {"kind": "AssertEquals", "success": true, "durationMs": 0.1, "observableFieldPath": "Enemy/Hp", "expected": "100", "actualValue": "100"},
    {"kind": "Command", "success": true, "durationMs": 0.8, ...},
    {"kind": "WaitFrames", "success": true, "durationMs": 16.7, "frames": 1},
    {"kind": "AssertEquals", "success": false, "durationMs": 0.1, "actualValue": "65", "error": "expected '70' but got '65'"}
  ]
}
```

ステータスコード:
- `200 OK` — 通常実行 (success / failure 両方ここ。利用側はボディで判別)
- `400 BadRequest` — body の文法エラー / `path` と `steps` の同時指定 等
- `409 Conflict` — `alreadyRunning: true` のとき

### CI で統合テストとして使う

リポジトリ同梱の `scripts/ci-run-scenario.sh` を使う:

```bash
# 必要: curl, jq (nix の場合は nix-shell -p curl jq --run "..." で囲む)
./scripts/ci-run-scenario.sh Combat/EnemyTakesDamage
echo "exit=$?"
```

終了コード:

| code | 意味 |
|---|---|
| 0 | シナリオ全ステップ成功 |
| 1 | シナリオ失敗 (assert / command 失敗 / fail-fast) |
| 2 | リクエスト送信失敗 (Editor 未起動 / ポート違い / 401 等) |
| 3 | 使用法エラー (引数不足) |
| 4 | 認証トークンが見つからない |
| 5 | curl / jq が PATH に無い |
| 6 | シナリオが既に実行中 (HTTP 409) |

環境変数で挙動調整可:

| 環境変数 | 既定 | 用途 |
|---|---|---|
| `LIMINAL_PALETTE_HOST` | `127.0.0.1` | 接続先ホスト |
| `LIMINAL_PALETTE_PORT` | (未指定なら 7610〜7615 を順に試行) | 単一ポートを使いたいとき指定 |
| `LIMINAL_PALETTE_TOKEN_FILE` | `~/.liminal-palette/token` | トークンファイル |

---

## 設計ノート

### なぜ `IEnumerable<ScenarioStep>` を返すパターンにしたか

候補は 3 つあった:

| パターン | 例 | 採否 |
|---|---|---|
| async + Context | `async Task Run(IScenarioContext ctx) { await ctx.Run(...); }` | ✗ HTTP ad-hoc と表現が乖離する |
| **yield return (採用)** | `IEnumerable<ScenarioStep> Run() { yield return ...; }` | ✓ ad-hoc と同じ「ステップ列」表現 |
| List プロパティ | `List<ScenarioStep> Steps => new() { ... };` | △ 動的構築には向くが LINQ 縦並び読みにくい |

「ステップ列」を一元化することで HTTP ad-hoc 経路と C# 宣言の表現を揃えている。

### Assert の対象は `[LiminalObservableField]` のみ

`[LiminalObservableField]` で公開された Path をベースに値を引く。直前 Command の戻り値に対する Assert (例: `AssertReturn`) は意図的に入れていない: 暗黙の "前ステップ" 状態が発生して fail-fast の単純さが崩れるため。「Command 経由で副作用を起こし、ObservableField に出てきた値を検証」というスタイルに統一している。

---

## トラブルシューティング (Scenario 関連)

### Q. シナリオ Run で 「ObservableField not found」になる

**A**: Assert 対象の Path が `[LiminalObservableField]` で登録されていない。

確認:
```csharp
foreach (var f in ObservableFieldRegistry.Default.All)
    Debug.Log(f.Path);
```

`Path` の typo / `[LiminalObservableField]` の付け忘れ / `public` でない / VContainer 未登録のいずれか。詳細は [commands.md](commands.md) の `[LiminalObservableField]` 章を参照。

### Q. シナリオ Run で 「Instance not resolved」になる

**A**: インスタンスメソッドのシナリオを VContainer に登録していない。

```csharp
builder.Register<CombatScenarios>(Lifetime.Singleton);
builder.RegisterEntryPoint<LiminalPaletteEntryPoint>();
```

### Q. ステップ実行直後の Assert が失敗する (タイミング問題)

**A**: `[LiminalCommand]` 内で `ReactiveProperty<T>.Value = X` した直後に Assert すると、R3 の Subscribe コールバックが完了する前に `ReadCurrent` が呼ばれることはない。`ReactiveProperty<T>.Value = ...` は同期的に内部値を更新するため、AssertEquals は新しい値で評価される。

ただし「副作用が `Update` で反映されるタイプの状態」(物理 / アニメーション / 物理エンジン経由の Rigidbody 等) を Assert する場合は `WaitFrames(1)` を間に挟む必要がある:

```csharp
yield return ScenarioStep.Run("Player/Position/Teleport", new() { ["x"] = 0f, ["y"] = 0f });
yield return ScenarioStep.WaitFrames(1);  // 物理シミュレーション 1 フレーム待つ
yield return ScenarioStep.AssertEquals("Player/Position", new Vector2(0, 0));
```

### Q. 「Scenario already running」になる

**A**: 既に別のシナリオが実行中。`SemaphoreSlim(1, 1)` で 1 並列に絞っているため。完了を待ってから再実行するか、既存実行をキャンセルする。

UI 側は Run ボタンが disable されるので発生しにくいが、HTTP 経由で並列に投げると 409 Conflict が返る。

### Q. シナリオ実行が Log タブには出るのに History タブに出ない

**A**: 仕様。シナリオ内 Command ステップは前提状態を欠いた単独再実行を防ぐため History から除外している。シナリオの再実行は **Scenario タブ** から行う。詳細は本ファイル [Log / History タブとの連携](#log--history-タブとの連携) 節。

---

## 関連ドキュメント

- [commands.md](commands.md) — `[LiminalCommand]` の引数バインドと async 戻り値の扱い
- [integrations.md](integrations.md) — VContainer 統合の流儀
- [ipc.md](ipc.md) — HTTP API の認証 / レートリミット / body サイズ
- [ui.md](ui.md) — Scenario タブを含む 4 タブ構成
- [asmdef.md](asmdef.md) — 新規ファイルの配置 (`Execution/ScenarioExecutor` ほか)
