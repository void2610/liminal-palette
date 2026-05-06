---
name: lp-run-scenario
description: "Run a named or ad-hoc scenario via LiminalPalette HTTP API. Use when you need to: (1) Execute a pre-declared `[ConsoleScenario]` by path, (2) Compose ad-hoc steps (command / wait_seconds / wait_frames / assert_equals / assert_not_equals) on the fly for one-shot integration tests, (3) Read fail-fast step results with `failedAtStep`, (4) Bundle multiple `lp-execute` + state checks into a single request to save rate limit budget."
---

# lp-run-scenario

LiminalPalette のシナリオ機能で、複数ステップ (コマンド実行 / 待機 / 状態 assert) を 1 リクエストで順次実行する。**named** (事前宣言済み `[ConsoleScenario]` を path 指定) と **ad-hoc** (curl 側でステップ列を組み立てる) の 2 経路。

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
# named (事前宣言済み)
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
     -X POST "$LP_BASE/api/v1/scenarios/run" \
     -d '{"path": "<Scenario/Path>"}'

# ad-hoc (ステップ列を直接渡す)
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
     -X POST "$LP_BASE/api/v1/scenarios/run" \
     -d '{"steps": [...]}'
```

`path` と `steps` は **排他**。両方指定 / どちらも未指定は 400 BadRequest。

---

## ステップ種別

| `type` | 必須フィールド | オプション | 用途 |
|---|---|---|---|
| `command` | `path` (string), `args` (object) | `description` | `[ConsoleCommand]` を実行。`args` は `lp-execute` と同じ string 化規則 |
| `wait_seconds` | `seconds` (number) | `description` | 実時間で待機 (`Task.Delay`) |
| `wait_frames` | `frames` (integer) | `description` | フレーム数で待機。Edit Mode は `EditorApplication.update` tick、Play Mode / Player は `Time.frameCount` |
| `assert_equals` | `path` (string), `expected` | `description` | `[ConsoleObservableField]` の現在値が `expected` と一致するか |
| `assert_not_equals` | `path` (string), `expected` | `description` | 上記の否定 |

`expected` は **string 推奨** (`"100"` / `"true"` / `"1,2,3"`)。HTTP 経由は JSON 往復で型が落ちるため、`TypeConverterRegistry` 任せの string 比較が安全。

---

## Examples

### 1) Named 実行

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/scenarios/run" \
  -d '{"path":"Combat/EnemyTakesDamage"}'
```

### 2) Ad-hoc: spawn → wait → assert の典型パターン

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/scenarios/run" \
  -d '{
    "steps": [
      {"type":"command","path":"Enemy/Spawn","args":{"type":"Goblin"}},
      {"type":"assert_equals","path":"Enemy/Hp","expected":"100","description":"spawn 直後は満タン"},
      {"type":"command","path":"Enemy/Damage","args":{"amount":"30"}},
      {"type":"wait_frames","frames":1},
      {"type":"assert_equals","path":"Enemy/Hp","expected":"70","description":"30 ダメージ後は 70"}
    ]
  }'
```

### 3) Ad-hoc: 一括 setup (assert なし)

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/scenarios/run" \
  -d '{
    "steps": [
      {"type":"command","path":"Player/Health/Set","args":{"value":"100"}},
      {"type":"command","path":"Player/Mana/Set","args":{"value":"50"}},
      {"type":"command","path":"Enemy/ClearAll","args":{}}
    ]
  }'
```

`/execute` を 3 連投する代わりに 1 リクエストにまとめる → レートリミット消費 1/3、ネットワーク往復 1/3。

### 4) 結果のパース (失敗時のステップ index 取得)

```bash
resp=$(curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/scenarios/run" \
  -d '{"path":"Combat/EnemyTakesDamage"}')

echo "$resp" | jq '{success, failedAtStep, durationMs}'

# 失敗ステップだけ抽出
echo "$resp" | jq '.steps[] | select(.success == false)'
```

### 5) HEREDOC で長い JSON を組み立てる

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/scenarios/run" \
  -d @- <<'EOF'
{
  "steps": [
    {"type":"command","path":"Player/Position/Teleport","args":{"pos":"0,0,0"}},
    {"type":"wait_frames","frames":1},
    {"type":"assert_equals","path":"Player/Position","expected":"(0.00, 0.00, 0.00)"},
    {"type":"command","path":"Enemy/Spawn","args":{"type":"Goblin"}},
    {"type":"wait_seconds","seconds":0.5},
    {"type":"assert_not_equals","path":"Enemy/Count","expected":"0"}
  ]
}
EOF
```

---

## Output

```json
{
  "success": false,
  "durationMs": 124.3,
  "failedAtStep": 4,
  "path": "Combat/EnemyTakesDamage",
  "alreadyRunning": false,
  "steps": [
    {"kind":"Command","success":true,"durationMs":1.2,"commandPath":"Enemy/Spawn","args":{"type":"Goblin"},"commandResult":{"success":true,"value":null,"durationMs":1.0,"logs":[]}},
    {"kind":"AssertEquals","success":true,"durationMs":0.1,"observableFieldPath":"Enemy/Hp","expected":"100","actualValue":"100"},
    {"kind":"Command","success":true,"durationMs":0.8,"commandPath":"Enemy/Damage","args":{"amount":"30"},"commandResult":{...}},
    {"kind":"WaitFrames","success":true,"durationMs":16.7,"frames":1},
    {"kind":"AssertEquals","success":false,"durationMs":0.1,"actualValue":"65","error":"expected '70' but got '65'"}
  ]
}
```

| フィールド | 説明 |
|---|---|
| `success` | 全ステップ Pass で true |
| `durationMs` | シナリオ全体の所要時間 |
| `failedAtStep` | 最初に失敗したステップの index、無ければ -1 |
| `path` | named 実行時のシナリオ path、ad-hoc は null |
| `alreadyRunning` | 他のシナリオが実行中で弾かれた場合 true |
| `steps[]` | 実行された分のみ (fail-fast 後は途中まで)。各要素は `kind` で形が変わる |

### 各ステップ結果の `kind` ごとの形

| `kind` | 主な追加フィールド |
|---|---|
| `Command` | `commandPath`, `args`, `commandResult` (`/execute` のレスポンスと同形) |
| `WaitSeconds` | `seconds` |
| `WaitFrames` | `frames` |
| `AssertEquals` | `observableFieldPath`, `expected`, `actualValue`, `error` (失敗時) |
| `AssertNotEquals` | 同上 |

---

## Error Handling

| Status | 状況 | 対処 |
|---|---|---|
| 200 + `success:false` | ステップ失敗 (assert / command 失敗) | `failedAtStep` と該当 `steps[i]` の `error` を読む |
| 400 BadRequest | body の文法エラー / `path` と `steps` の同時指定 / 未知の `type` 等 | request body を再確認 |
| 401 Unauthorized | Token 不一致 | `~/.liminal-palette/token` 再読み込み / Editor 再起動 |
| 404 Not Found | named 実行で path が未登録 | `lp-list-scenarios` で確認 |
| 409 Conflict | 別シナリオが排他実行中 (`alreadyRunning: true`) | 完了を待つ。1 並列のみ (`SemaphoreSlim(1, 1)`) |
| 429 Too Many Requests | レートリミット超過 (`/execute` と枠共有、30 req/s) | 間隔を空ける。複数 `/execute` を 1 シナリオにまとめる方が効率的 |

---

## Notes

### Assert の対象は `[ConsoleObservableField]` のみ

直前 `command` ステップの戻り値に対する assert はできない (意図的にスコープ外。LP の設計判断)。「Command 経由で副作用を起こし → ObservableField で観測される値を assert」のスタイルに統一。

戻り値を見たい場合は `commandResult.value` を結果 JSON から `jq` で取り出すこと:

```bash
echo "$resp" | jq '.steps[] | select(.kind == "Command") | .commandResult.value'
```

### fail-fast

最初の失敗ステップで打ち切り、後続は実行されない。`steps[]` は **失敗ステップを含むそこまで** が入る。

### シナリオ排他 (1 並列)

LP は **`SemaphoreSlim(1, 1)` で 1 並列に絞っている**。実行中に別シナリオを送ると即座に `409 Conflict` で `alreadyRunning: true` が返る (待たない)。並行実行が必要なら別プロセスで Editor を立てるか、ad-hoc を 1 つにまとめる。

通常コマンド (`/execute`) はシナリオ実行中でも並行で叩ける (排他は scenario 同士のみ)。

### `wait_frames` の挙動

| 環境 | 1 frame の意味 |
|---|---|
| Edit Mode | `EditorApplication.update` tick (≒ 1/60〜1/30 秒) |
| Play Mode / Player build | `Time.frameCount` 増分 |

物理 / アニメーション / Rigidbody のフィードバックを待つには 1〜数フレーム挟むのが定石:

```json
{"type":"command","path":"Player/Position/Teleport","args":{"x":"0","y":"0"}}
{"type":"wait_frames","frames":1}
{"type":"assert_equals","path":"Player/Position","expected":"(0.00, 0.00)"}
```

### 副作用付きステップ生成の注意 (named のみ)

named シナリオは `IEnumerable<ScenarioStep>` を返す yield return メソッド。`yield return` 行間に副作用を書いていると `lp-list-scenarios` の `stepCount` 取得時にも発火する。**生成は純粋に保つ**。

### ad-hoc vs named の使い分け

| ケース | 推奨 |
|---|---|
| 同じ手順を何度も再現する / リポジトリで共有する | named (`[ConsoleScenario]` で C# 宣言) |
| その場限りの統合テスト / 探索的検証 | ad-hoc (curl で steps 列を組む) |
| AI Agent が状況に応じて動的にステップ列を組む | ad-hoc |
| CI で固定シナリオを回す | named |

### CI / シェルスクリプトから使う

LP 本体には CI ヘルパスクリプトの参考実装が `Documentation~/scenarios.md` に記載されている (`scripts/ci-run-scenario.sh` という想定だが、本リポジトリにはまだ実体ファイルなし)。シナリオ失敗時の終了コードを切り出す例:

```bash
resp=$(curl -s -w '\n%{http_code}' -H "Authorization: Bearer $LP_TOKEN" \
         -H "Content-Type: application/json" \
         -X POST "$LP_BASE/api/v1/scenarios/run" \
         -d '{"path":"Combat/Smoke"}')

http_code=$(echo "$resp" | tail -n1)
body=$(echo "$resp" | sed '$d')

case "$http_code" in
  200) success=$(echo "$body" | jq -r '.success'); [ "$success" = "true" ] && exit 0 || exit 1 ;;
  409) exit 6 ;;  # already running
  401) exit 4 ;;  # auth
  *)   exit 2 ;;  # request failure
esac
```

---

## 関連スキル

- `lp-list-scenarios` — named 実行用の path 発見
- `lp-list-commands` — ad-hoc の `command` ステップで使う path 発見
- `lp-execute` — 単発実行 (シナリオ化するほどでもない場合)
- `lp-get-state` — assert 対象の `[ConsoleObservableField]` の現在値確認
