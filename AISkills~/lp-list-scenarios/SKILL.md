---
name: lp-list-scenarios
description: 'List all [ConsoleScenario] declared in the running Unity project via LiminalPalette HTTP API. Use to pick a named scenario before invoking lp-run-scenario, show stepCount and description, or detect VContainer mis-registration via stepCount=-1.'
when_to_use: 'Trigger phrases: "シナリオ一覧", "scenarios", "宣言済みのシナリオ", "list scenarios", "what scenarios", "before lp-run-scenario", "stepCount を見たい".'
allowed-tools: Bash(curl *), Bash(jq *), Bash(cat *)
---

# lp-list-scenarios

LiminalPalette に `[ConsoleScenario]` 属性で宣言されたシナリオの一覧を取得する。`lp-run-scenario` で named 実行する前の発見ステップ。

シナリオは「複数ステップ (command / wait / assert) を順次実行する宣言」で、`[ConsoleCommand]` の集合体に近い。詳細は `/lp-run-scenario` を参照。

---

## Setup

```bash
[ -z "${LP_TOKEN:-}" ] && export LP_TOKEN=$(cat ~/.liminal-palette/token)
[ -z "${LP_BASE:-}" ] && {
  for p in 7610 7611 7612 7613 7614 7615; do
    curl -s -m 1 "http://127.0.0.1:$p/api/v1/health" >/dev/null 2>&1 && export LP_PORT=$p && break
  done
  export LP_BASE="http://127.0.0.1:$LP_PORT"
}
```

---

## 基本

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/scenarios"
```

---

## よく使うパターン

### 全シナリオの path / stepCount / description

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/scenarios" \
  | jq '.scenarios[] | {path, stepCount, description}'
```

### prefix で絞り込み

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/scenarios" \
  | jq '.scenarios[] | select(.path | startswith("Combat/"))'
```

### `stepCount: -1` (インスタンス未解決) を検出

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/scenarios" \
  | jq '.scenarios[] | select(.stepCount == -1) | .path'
```

### シナリオ数 / カテゴリ別件数

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/scenarios" \
  | jq '{
    total: (.scenarios | length),
    byCategory: ([.scenarios[] | (.path | split("/")[0])] | group_by(.) | map({k: .[0], v: length}))
  }'
```

### Markdown リスト化

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/scenarios" \
  | jq -r '.scenarios | sort_by(.path) | map("- `" + .path + "` (steps=" + (.stepCount|tostring) + ")" + (if .description != "" then " — " + .description else "" end)) | .[]'
```

---

## Output

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
| `path` | `[ConsoleScenario("...")]` で指定された path |
| `description` | `[ConsoleScenario(Description = ...)]` の値 (空文字は description 未指定) |
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

`stepCount: -1` のシナリオを `lp-run-scenario` で実行しようとすると 500 で「Instance not resolved」が返るので事前に検出しておくと役立つ。

---

## 副作用付きステップ生成の罠

`[ConsoleScenario]` メソッドは `IEnumerable<ScenarioStep>` を返す yield return パターン。`stepCount` 取得のために LP は **ステップ列を 1 度 enumerate する**。

問題: `yield return` の **行間に副作用** (`Debug.Log`、状態変更等) を書いていると、本スキルでシナリオ一覧を取得しただけで副作用が発火する:

```csharp
// NG (yield return 行間で副作用)
[ConsoleScenario("Bad/Example")]
public IEnumerable<ScenarioStep> Bad()
{
    Debug.Log("Generating step 1");          // ← /api/v1/scenarios で発火する
    yield return ScenarioStep.Run("Foo");

    SpawnSomething();                         // ← 同上
    yield return ScenarioStep.Run("Bar");
}
```

→ **シナリオの step 列生成は純粋に保つ**。重い処理 / I/O / 副作用は最初のステップ内に書くこと。

```csharp
// OK
[ConsoleScenario("Good/Example")]
public IEnumerable<ScenarioStep> Good()
{
    yield return ScenarioStep.Run("Setup/PrepareWorld");   // 副作用は ConsoleCommand 内に
    yield return ScenarioStep.Run("Foo");
    yield return ScenarioStep.Run("Bar");
}
```

---

## Command との違い

| 観点 | `[ConsoleCommand]` | `[ConsoleScenario]` |
|---|---|---|
| 一覧 endpoint | `/api/v1/commands` | `/api/v1/scenarios` |
| 実行 endpoint | `POST /execute` | `POST /scenarios/run` |
| 単位 | 1 メソッド = 1 コマンド | 複数ステップを順次実行 (fail-fast + assert) |
| 用途 | ゲーム操作の最小単位 | 統合テスト / "敵spawn → 待つ → assert" の連鎖 |
| 並列 | 制限なし | scenarios 同士は 1 並列 (`SemaphoreSlim`) |

両方とも別々に発見する必要あり。`lp-list-commands` と本スキルで両方を見て使い分ける。

---

## Notes

### Editor / Runtime で違うシナリオ

両稼働時、Editor (7610) と Runtime (7611) で別の `ScenarioRegistry` が立っているケースあり。Editor 限定 / Runtime 限定のシナリオを切り分けたい時は両ポートで本スキルを実行:

```bash
echo "=== Editor ==="
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE_EDITOR/api/v1/scenarios" \
  | jq '.scenarios[].path'

echo "=== Runtime ==="
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE_RUNTIME/api/v1/scenarios" \
  | jq '.scenarios[].path'
```

### Cmd+K UI との関係

ここで取得できる一覧は LP の **Scenario タブ**に並ぶシナリオと同じソース。AI Agent から見えるシナリオは開発者の手元の Editor UI でも実行可能。

---

## Error Handling

| Status | 状況 | 対処 |
|---|---|---|
| 401 | Token 不一致 | `~/.liminal-palette/token` 再読み込み |

---

## See also

- `/lp-run-scenario` — ここで発見した path を named 実行 / ad-hoc に組む
- `/lp-list-commands` — シナリオ内 `command` ステップで使う `[ConsoleCommand]` の発見
- `/lp-get-state` — シナリオ内 `assert_equals` で使う `[ConsoleObservableField]` の現在値
- LP 本体: `Documentation~/scenarios.md`
