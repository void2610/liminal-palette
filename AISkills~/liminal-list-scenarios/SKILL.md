---
name: liminal-list-scenarios
description: 'List all [LiminalScenario] declared in the running Unity project via `liminal scenarios`. Use to pick a named scenario before invoking liminal-run-scenario, show stepCount and description, or detect VContainer mis-registration via stepCount=-1.'
when_to_use: 'Trigger phrases: "シナリオ一覧", "scenarios", "宣言済みのシナリオ", "list scenarios", "what scenarios", "before liminal-run-scenario", "stepCount を見たい".'
allowed-tools: Bash(liminal *), Bash(jq *)
---

# liminal-list-scenarios

LiminalPalette に `[LiminalScenario]` で宣言されたシナリオの一覧を `liminal scenarios` で取得する。`liminal-run-scenario` で named 実行する前の発見ステップ。

シナリオは「複数ステップ (command / wait / assert) を順次実行する宣言」で、`[LiminalCommand]` の集合体に近い。詳細は `/liminal-run-scenario` を参照。

---

## 基本

```bash
liminal scenarios
```

出力例:

```
  Combat/EnemyTakesDamage  敵にダメージを与えて HP が減ることを検証 [5 steps]
  Boot/ResetAllItems       (no description) [? steps]

  total: 2
```

`[? steps]` は `stepCount: -1` (インスタンス未解決等で計測不能) を表す。

---

## `--json` で取って `jq` で絞る

### 全シナリオの path / stepCount / description

```bash
liminal scenarios --json | jq '.scenarios[] | {path, stepCount, description}'
```

### prefix で絞り込み

```bash
liminal scenarios --json | jq '.scenarios[] | select(.path | startswith("Combat/"))'
```

### `stepCount: -1` (インスタンス未解決) を検出

```bash
liminal scenarios --json | jq -r '.scenarios[] | select(.stepCount == -1) | .path'
```

### シナリオ数 / カテゴリ別件数

```bash
liminal scenarios --json | jq '{
  total: (.scenarios | length),
  byCategory: ([.scenarios[] | (.path | split("/")[0])] | group_by(.) | map({k: .[0], v: length}))
}'
```

### Markdown リスト化

```bash
liminal scenarios --json \
  | jq -r '.scenarios | sort_by(.path) | map("- `" + .path + "` (steps=" + (.stepCount|tostring) + ")" + (if .description != "" then " — " + .description else "" end)) | .[]'
```

---

## Output (`--json`)

```json
{
  "scenarios": [
    {
      "path": "Combat/EnemyTakesDamage",
      "description": "敵にダメージを与えて HP が減ることを検証",
      "stepCount": 5
    },
    {
      "path": "Boot/ResetAllItems",
      "description": "",
      "stepCount": -1
    }
  ]
}
```

| フィールド | 説明 |
|---|---|
| `path` | `[LiminalScenario("...")]` で指定された path |
| `description` | `[LiminalScenario(Description = ...)]` の値 (空文字は description 未指定) |
| `stepCount` | シナリオに含まれるステップ数。**`-1` はインスタンス未解決等で計測不能** |

---

## `stepCount: -1` の意味と対処

シナリオの step 列を 1 度 enumerate して数える実装だが、**インスタンスメソッドのシナリオで VContainer 解決ができないとカウント不能**になり -1 が返る。

### 対処

利用側で対象クラスを LifetimeScope に登録 + `LiminalPaletteEntryPoint` を入れる:

```csharp
public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        builder.Register<CombatScenarios>(Lifetime.Singleton);
        builder.RegisterEntryPoint<LiminalPaletteEntryPoint>();
    }
}
```

`stepCount: -1` のシナリオを `liminal run` で実行しようとすると 500 で「Instance not resolved」が返るので事前に検出しておくと役立つ。

---

## 副作用付きステップ生成の罠

`[LiminalScenario]` メソッドは `IEnumerable<ScenarioStep>` を返す yield return パターン。`stepCount` 取得のために LP は **ステップ列を 1 度 enumerate する**。

問題: `yield return` の **行間に副作用** (`Debug.Log`、状態変更等) を書いていると、本スキルでシナリオ一覧を取得しただけで副作用が発火する:

```csharp
// NG (yield return 行間で副作用)
[LiminalScenario("Bad/Example")]
public IEnumerable<ScenarioStep> Bad()
{
    Debug.Log("Generating step 1");          // ← liminal scenarios で発火する
    yield return ScenarioStep.Run("Foo");

    SpawnSomething();                         // ← 同上
    yield return ScenarioStep.Run("Bar");
}
```

→ **シナリオの step 列生成は純粋に保つ**。重い処理 / I/O / 副作用は最初のステップ内に書くこと。

```csharp
// OK
[LiminalScenario("Good/Example")]
public IEnumerable<ScenarioStep> Good()
{
    yield return ScenarioStep.Run("Setup/PrepareWorld");   // 副作用は LiminalCommand 内に
    yield return ScenarioStep.Run("Foo");
    yield return ScenarioStep.Run("Bar");
}
```

---

## Command との違い

| 観点 | `[LiminalCommand]` | `[LiminalScenario]` |
|---|---|---|
| 一覧取得 | `liminal commands` | `liminal scenarios` |
| 実行 | `liminal exec <path>` | `liminal run <path>` (named) / `liminal run --steps -` (ad-hoc) |
| 単位 | 1 メソッド = 1 コマンド | 複数ステップを順次実行 (fail-fast + assert) |
| 用途 | ゲーム操作の最小単位 | 統合テスト / "敵spawn → 待つ → assert" の連鎖 |
| 並列 | 制限なし | scenarios 同士は 1 並列 (`SemaphoreSlim`) |

両方とも別々に発見する必要あり。`liminal commands` と本スキルで両方を見て使い分ける。

---

## Notes

### Editor / Runtime で違うシナリオ

両稼働時、Editor (7610) と Runtime (7611) で別の `ScenarioRegistry` が立っているケースあり。Editor 限定 / Runtime 限定のシナリオを切り分けたい時は両ポートで本スキルを実行:

```bash
echo "=== Editor ==="
liminal --port 7610 scenarios --json | jq -r '.scenarios[].path'

echo "=== Runtime ==="
liminal --port 7611 scenarios --json | jq -r '.scenarios[].path'
```

### Cmd+K UI との関係

ここで取得できる一覧は LP の **Scenario タブ**に並ぶシナリオと同じソース。AI Agent から見えるシナリオは開発者の手元の Editor UI でも実行可能。

---

## Error Handling

| 症状 | 状況 | 対処 |
|---|---|---|
| HTTP 401 | Token 不一致 | `~/.liminal-palette/token` 再生成 |

---

## See also

- `/liminal-run-scenario` — ここで発見した path を named 実行 / ad-hoc に組む
- `/liminal-list-commands` — シナリオ内 `command` ステップで使う `[LiminalCommand]` の発見
- `/liminal-get-state` — シナリオ内 `assert_equals` で使う `[LiminalObservableField]` の現在値
- LP 本体: `Documentation~/scenarios.md`
