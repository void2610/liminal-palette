---
name: lp-list-scenarios
description: "List all `[ConsoleScenario]` declared in the running Unity project via LiminalPalette HTTP API. Use when you need to: (1) Pick a named scenario to invoke with `lp-run-scenario`, (2) Show stepCount and description before running, (3) Detect unresolved instances (`stepCount: -1` indicates VContainer mis-registration)."
---

# lp-list-scenarios

LiminalPalette に `[ConsoleScenario]` 属性で宣言されたシナリオの一覧を取得する。`lp-run-scenario` で named 実行する前の発見ステップ。

---

## Prerequisites

```bash
export LP_TOKEN=$(cat ~/.liminal-palette/token)
for port in 7610 7611 7612 7613 7614 7615; do
  if curl -s -m 1 "http://127.0.0.1:$port/api/v1/health" > /dev/null 2>&1; then
    export LP_PORT=$port; break
  fi
done
export LP_BASE="http://127.0.0.1:$LP_PORT"
```

---

## Usage

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/scenarios"
```

---

## Examples

```bash
# 全シナリオの path / stepCount / description だけ
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/scenarios" \
  | jq '.scenarios[] | {path, stepCount, description}'

# 特定カテゴリ (prefix) で絞る
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/scenarios" \
  | jq '.scenarios[] | select(.path | startswith("Combat/"))'

# stepCount が -1 のシナリオ (インスタンス未解決の検出)
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/scenarios" \
  | jq '.scenarios[] | select(.stepCount == -1) | .path'

# シナリオ数だけ
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/scenarios" \
  | jq '.scenarios | length'
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

## Error Handling

| Status | 状況 | 対処 |
|---|---|---|
| 401 Unauthorized | Token 不一致 | `~/.liminal-palette/token` を再読み込み / Editor 再起動 |

---

## Notes

### `stepCount: -1` の意味

シナリオの step 列を 1 度 enumerate して数えるが、**インスタンスメソッドのシナリオで VContainer 解決ができないとカウント不能**。

対処: 利用側で対象クラスを `LifetimeScope.Configure` に登録 + `RegisterEntryPoint<LiminalPaletteEntryPoint>` を入れる。

```csharp
builder.Register<CombatScenarios>(Lifetime.Singleton);
builder.RegisterEntryPoint<LiminalPaletteEntryPoint>();
```

詳細は LP の `Documentation~/scenarios.md`。

### 副作用付きシナリオ生成の注意

`stepCount` を取るために `IEnumerable<ScenarioStep>` を一度 enumerate する。`yield return` の行間に副作用 (Debug.Log・状態変更) を書いていると、**この一覧取得時にも発火する**。

シナリオの step 列生成は**純粋に保つこと** (重い処理 / I/O は最初のステップ内に書く)。

### Command との違い

| 観点 | `[ConsoleCommand]` | `[ConsoleScenario]` |
|---|---|---|
| 一覧 endpoint | `/api/v1/commands` | `/api/v1/scenarios` |
| 実行 endpoint | `POST /execute` | `POST /scenarios/run` |
| 単位 | 1 メソッド = 1 コマンド | 複数ステップを順次実行 (fail-fast + assert 機能付き) |
| 用途 | ゲーム操作の最小単位 | 統合テスト / 「敵スポーン → 待つ → assert」のような連鎖 |

両方とも `lp-list-commands` と本スキルで別々に発見する必要がある。

---

## 関連スキル

- `lp-run-scenario` — ここで発見した path を named 実行 (or ad-hoc に組む)
- `lp-list-commands` — シナリオ内 `command` ステップで使う `[ConsoleCommand]` の発見
- `lp-get-state` — シナリオ内 assert で使う `[ConsoleObservableField]` の現在値確認
