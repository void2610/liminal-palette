---
name: liminal-run-scenario
description: 'Run a named or ad-hoc multi-step scenario via `liminal run`. Bundles command / wait_seconds / wait_frames / assert_equals / assert_not_equals steps into a single request with fail-fast semantics. Use for integration tests, spawn-wait-assert chains, or to bundle multiple liminal-execute calls and save rate-limit budget.'
when_to_use: 'Trigger phrases: "シナリオ実行", "シナリオ走らせて", "統合テスト", "spawn して assert", "run scenario", "execute named scenario", "ad-hoc steps", "bundle multiple commands".'
allowed-tools: Bash(liminal *), Bash(jq *), Bash(cat *), Read
---

# liminal-run-scenario

LiminalPalette のシナリオ機能で、複数ステップ (コマンド実行 / 待機 / 状態 assert) を 1 リクエストで順次実行する。**named** (事前宣言済み `[LiminalScenario]` を path 指定) と **ad-hoc** (CLI 側でステップ列を組み立てて `--steps` で渡す) の 2 経路。

シナリオは **fail-fast** (最初の失敗で打ち切り) + **1 並列 (排他)** で実行される。詳細な内部仕様は [references/step-types.md](references/step-types.md)。

---

## 構文

```bash
# named: 事前宣言済みシナリオを path で叩く
liminal run <Scenario/Path>

# ad-hoc: stdin から JSON ステップ列を流す
liminal run --steps -

# ad-hoc: ファイルから JSON を読む
liminal run --steps path/to/steps.json
```

`<path>` と `--steps` は **排他**。ad-hoc では JSON は配列直書き (`[{...}, ...]`) でも `{"steps":[...]}` でも受ける。

---

## ステップ種別 (要約)

| `type` | 必須フィールド | 用途 |
|---|---|---|
| `command` | `path` (string), `args` (object) | `[LiminalCommand]` を実行。`args` は `liminal exec` と同じ string 化規則 |
| `wait_seconds` | `seconds` (number) | 実時間で待機 |
| `wait_frames` | `frames` (integer) | フレーム数で待機 |
| `assert_equals` | `path` (string), `expected` (string\|number\|bool\|null) | `[LiminalObservableField]` の現在値が `expected` と一致するか |
| `assert_not_equals` | `path` (string), `expected` | 上記の否定 |

各ステップに任意の `description` フィールドを足せる (結果 JSON に出る)。詳細仕様 (`expected` の型解決 / 失敗時の挙動 / フィールド一覧) は [references/step-types.md](references/step-types.md)。

---

## 例 1: Named 実行

```bash
liminal run Combat/EnemyTakesDamage
```

事前宣言済みのシナリオ。CI で安定したテストを回す用途に。

## 例 2: Ad-hoc (典型的な spawn → assert)

```bash
cat <<'EOF' | liminal run --steps -
[
  {"type":"command","path":"Enemy/Spawn","args":{"type":"Goblin"}},
  {"type":"assert_equals","path":"Enemy/Hp","expected":"100","description":"spawn 直後は満タン"},
  {"type":"command","path":"Enemy/Damage","args":{"amount":"30"}},
  {"type":"wait_frames","frames":1},
  {"type":"assert_equals","path":"Enemy/Hp","expected":"70","description":"30 ダメージ後は 70"}
]
EOF
```

## 例 3: Ad-hoc セットアップ (assert なし、`liminal exec` を 3 連投する代わりに)

```bash
cat <<'EOF' | liminal run --steps -
[
  {"type":"command","path":"Player/Health/Set","args":{"value":"100"}},
  {"type":"command","path":"Player/Mana/Set","args":{"value":"50"}},
  {"type":"command","path":"Enemy/ClearAll","args":{}}
]
EOF
```

`liminal exec` を 3 連投する代わりに 1 リクエスト → レートリミット消費 1/3、ネットワーク往復 1/3。

## 例 4: ファイルに保存して使い回す

```bash
# steps.json に保存しておけば再実行が楽
liminal run --steps tests/repro-bug-42.json
```

より多くの ad-hoc レシピは [examples/ad-hoc-recipes.md](examples/ad-hoc-recipes.md)、named シナリオ運用例は [examples/named.md](examples/named.md)。

---

## Output (人間向け)

```
PASS  Combat/EnemyTakesDamage  (124.3 ms)
  ✓ [0] Command          Enemy/Spawn (1.2ms)
  ✓ [1] AssertEquals     actual=100 (0.1ms)
  ✓ [2] Command          Enemy/Damage (0.8ms)
  ✓ [3] WaitFrames       (16.7ms)
  ✗ [4] AssertEquals     actual=65  expected '70' but got '65' (0.1ms)
```

exit code は **0=PASS / 2=FAIL / 1=通信エラー**。

---

## Output (`--json`)

`/api/v1/scenarios/run` の生レスポンス:

```json
{
  "success": false,
  "durationMs": 124.3,
  "failedAtStep": 4,
  "path": "Combat/EnemyTakesDamage",
  "alreadyRunning": false,
  "steps": [
    {"kind":"Command","success":true,"durationMs":1.2,"commandPath":"Enemy/Spawn","args":{"type":"Goblin"},"commandResult":{...}},
    {"kind":"AssertEquals","success":true,"durationMs":0.1,"observableFieldPath":"Enemy/Hp","expected":"100","actualValue":"100"},
    {"kind":"Command","success":true,"durationMs":0.8,"commandPath":"Enemy/Damage","args":{"amount":"30"},"commandResult":{...}},
    {"kind":"WaitFrames","success":true,"durationMs":16.7,"frames":1},
    {"kind":"AssertEquals","success":false,"durationMs":0.1,"actualValue":"65","error":"expected '70' but got '65'"}
  ]
}
```

| トップレベル | 説明 |
|---|---|
| `success` | 全ステップ Pass で true |
| `durationMs` | シナリオ全体の所要時間 |
| `failedAtStep` | 最初に失敗したステップの index、無ければ -1 |
| `path` | named 実行時のシナリオ path、ad-hoc は null |
| `alreadyRunning` | 他のシナリオが実行中で弾かれた場合 true |
| `steps[]` | 実行された分のみ (fail-fast 後は途中まで) |

各 `steps[i]` の形は `kind` で変わる (詳細: [references/step-types.md](references/step-types.md))。

### 結果のパース典型

```bash
RESP=$(liminal run Combat/EnemyTakesDamage --json)

echo "$RESP" | jq '{success, failedAtStep, durationMs}'

# 失敗ステップだけ
echo "$RESP" | jq '.steps[] | select(.success == false)'
```

---

## エラー対処

| 症状 | 状況 | 対処 |
|---|---|---|
| 終了コード 2 + `FAIL` | ステップ失敗 (assert / command 失敗) | 出力の `failedAtStep` と該当ステップの `error` を読む |
| HTTP 400 | body 文法エラー / 未知の `type` 等 | `--steps` の JSON を再確認 |
| HTTP 401 | Token 不一致 | `~/.liminal-palette/token` 再生成 |
| HTTP 404 | named 実行で path が未登録 | `liminal scenarios` で確認 |
| HTTP 409 | 別シナリオが排他実行中 (`alreadyRunning: true`) | 完了を待つ。1 並列のみ |
| HTTP 429 | レートリミット (`/execute` と枠共有、30 req/s) | 間隔を空ける。複数 exec を 1 シナリオにまとめる方が効率的 |

---

## Notes

### fail-fast

最初の失敗ステップで打ち切り、後続は実行されない。`steps[]` は **失敗ステップを含むそこまで** が入る。

### シナリオ排他 (1 並列)

LP は **`SemaphoreSlim(1, 1)` で 1 並列に絞っている**。実行中に別シナリオを送ると即座に 409 で `alreadyRunning: true` が返る (待たない)。並列実行が必要なら別プロセスで Editor を立てるか、ad-hoc を 1 つにまとめる。

通常コマンド (`liminal exec`) はシナリオ実行中でも並行で叩ける (排他は scenario 同士のみ)。

### ad-hoc vs named の使い分け

| ケース | 推奨 |
|---|---|
| 同じ手順を何度も再現する / リポジトリで共有する | named (`[LiminalScenario]` で C# 宣言) |
| その場限りの統合テスト / 探索的検証 | ad-hoc (`liminal run --steps -`) |
| AI Agent が状況に応じて動的にステップ列を組む | ad-hoc |
| CI で固定シナリオを回す | named |

### Assert 対象は `[LiminalObservableField]` のみ

直前 `command` ステップの戻り値に対する assert はできない (LP の設計判断、暗黙の "前ステップ" を排除するため)。「Command 経由で副作用 → ObservableField で観測される値を assert」のスタイルに統一。

戻り値を見たい場合は `commandResult.value` を結果 JSON から `jq` で取り出すこと:

```bash
liminal run Combat/EnemyTakesDamage --json \
  | jq '.steps[] | select(.kind == "Command") | .commandResult.value'
```

### `wait_frames` の挙動

| 環境 | 1 frame の意味 |
|---|---|
| Edit Mode | `EditorApplication.update` tick (≒ 1/60〜1/30 秒) |
| Play Mode / Player build | `Time.frameCount` 増分 |

物理 / アニメ / Rigidbody のフィードバックを待つには 1〜数フレーム挟むのが定石。

### `expected` は string 推奨

HTTP 経由は JSON 往復で型が落ちるため、`assert_equals` の `expected` は **string で送る**ほうが安全:

```json
{"type":"assert_equals","path":"Enemy/Hp","expected":"100"}      // ✓ 推奨
{"type":"assert_equals","path":"Enemy/Hp","expected":100}        // 動くが number として送られ、内部で string 化される
```

詳細: [references/step-types.md](references/step-types.md) の「expected の型解決」セクション。

---

## See also

- `/liminal-list-scenarios` — named 実行用の path 発見
- `/liminal-list-commands` — ad-hoc の `command` ステップで使う path 発見
- `/liminal-execute` — 単発実行 (シナリオ化するほどでもない場合)
- `/liminal-get-state` — assert 対象の `[LiminalObservableField]` の現在値確認
- references: [step-types.md](references/step-types.md) — 5 ステップ種別の完全仕様
- examples:
  - [named.md](examples/named.md) — named シナリオ運用 + CI 連携
  - [ad-hoc-recipes.md](examples/ad-hoc-recipes.md) — ad-hoc steps を動的生成する 10+ パターン
- LP 本体: `Documentation~/scenarios.md`
