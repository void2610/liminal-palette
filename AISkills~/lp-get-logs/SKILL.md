---
name: lp-get-logs
description: 'Fetch recent command invocation history from LiminalPalette InvocationStore (UI + HTTP + scenarios all merged). Use to audit which commands ran, recover args from a previous failed call to retry, time-correlate game events with executions, or filter IsFromScenario to separate scenario-internal calls. NOT the same as Unity Console logs (use uloop-get-logs for those).'
when_to_use: 'Trigger phrases: "直近の実行履歴", "何が走ったか", "前回の失敗を見せて", "log の確認", "command history", "what did I run last", "audit invocations".'
allowed-tools: Bash(curl *), Bash(jq *), Bash(cat *)
---

# lp-get-logs

LiminalPalette の `InvocationStore` に記録された **コマンド実行履歴**を新しい順で取得する。UI 経由 / HTTP 経由 / シナリオ内すべてが同じ Store に記録される。

⚠️ Unity の `Debug.Log*` 全体ではない (本スキルは LP の `[ConsoleCommand]` 実行履歴限定)。Unity Console を見たい場合は `uloop-get-logs` などの別経路を使う。

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
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/logs?limit=50"
```

| Query | 既定 | 上限 | 説明 |
|---|---|---|---|
| `limit` | 50 | 200 (`InvocationStore.Capacity`) | 取得件数。新しい順 |

---

## よく使うパターン

### 直近 10 件の path / 時刻 / 成否

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/logs?limit=10" \
  | jq '.invocations[] | {path, ts: .timestamp, ok: .result.success}'
```

### 失敗だけ抽出

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/logs?limit=200" \
  | jq '.invocations[] | select(.result.success == false) | {path, error: .result.error, args}'
```

### シナリオ外の直接実行のみ

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/logs?limit=200" \
  | jq '.invocations[] | select(.isFromScenario != true) | .path'
```

### 直近 1 件の `result.value`

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/logs?limit=1" \
  | jq -r '.invocations[0].result.value'
```

### 所要時間が長いコマンド (durationMs > 100)

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/logs?limit=200" \
  | jq '.invocations[] | select(.result.durationMs > 100) | {path, ms: .result.durationMs}'
```

より多くのレシピは [examples/jq-queries.md](examples/jq-queries.md)。

---

## Output

```json
{
  "invocations": [
    {
      "path": "Test/Vector",
      "timestamp": "2026-04-30T12:34:56.789Z",
      "args": {"v": "(1, 2, 3)"},
      "isFromScenario": false,
      "result": {
        "success": true,
        "value": "(2.00, 4.00, 6.00)",
        "error": null,
        "exceptionType": null,
        "stackTrace": null,
        "durationMs": 1.07,
        "logs": []
      }
    }
  ],
  "total": 12,
  "limit": 50
}
```

| フィールド | 説明 |
|---|---|
| `invocations[].path` | 実行されたコマンドの path |
| `invocations[].timestamp` | UTC ISO 8601 |
| `invocations[].args` | 実行時の引数 (string 化済み)。**リトライに使える** |
| `invocations[].isFromScenario` | シナリオ内ステップとして実行されたか |
| `invocations[].result` | `lp-execute` のレスポンスと**同一スキーマ** (success, value, error, exceptionType, stackTrace, durationMs, logs) |
| `total` | Store 内の総件数 (limit と独立) |
| `limit` | 実際に返した件数の上限 |

---

## 失敗デバッグの定石

直近の失敗から原因を辿るパターン:

```bash
# 1. 直近 1 件の失敗を取得
LAST_FAIL=$(curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/logs?limit=200" \
  | jq '[.invocations[] | select(.result.success == false)] | .[0]')

echo "$LAST_FAIL" | jq '{path, args, error: .result.error, exceptionType: .result.exceptionType, stack: .result.stackTrace}'

# 2. 引数の型が間違っていた可能性 → スキーマ確認
PATH_FAIL=$(echo "$LAST_FAIL" | jq -r '.path')
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
  | jq --arg p "$PATH_FAIL" '.commands[] | select(.path == $p) | .parameters'

# 3. 修正版で再実行
NEW_ARGS='{"value":"50"}'
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d "{\"path\":\"$PATH_FAIL\",\"args\":$NEW_ARGS}"
```

---

## シナリオ実行との関係

シナリオ (`/scenarios/run`) 内で `command` ステップとして実行されたコマンドも `InvocationStore` に **`isFromScenario: true`** で記録される。

| 用途 | フィルタ |
|---|---|
| 直接実行のみ (UI / HTTP /execute 経由) | `select(.isFromScenario != true)` |
| シナリオ内ステップのみ | `select(.isFromScenario == true)` |
| シナリオ集約 (シナリオ全体を 1 行で見る) | path が `Scenario/<シナリオ path>` 形式の行を探す (LP 側でシナリオ実行ごとに集約レコードも記録される) |

---

## Notes

### Capacity

`InvocationStore` のリングバッファは **200 件で固定**。古いものから消える。長時間プレイで履歴を全部取りたい場合は **定期的に `/logs` を取って外部に保存**するパターン。

```bash
# 定期 dump
mkdir -p /tmp/lp-logs
while true; do
  curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/logs?limit=200" \
    > "/tmp/lp-logs/$(date +%Y%m%d-%H%M%S).json"
  sleep 60
done
```

### Editor / Runtime ごとに別 Store

両稼働時、Editor (7610) と Runtime (7611) で **別の `InvocationStore`** が立っている。Editor で叩いたコマンドは Runtime の `/logs` には出ない (逆も)。

```bash
# 両方統合して見る
echo "=== Editor ==="
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE_EDITOR/api/v1/logs?limit=10" \
  | jq '.invocations[] | "[E] " + .path'

echo "=== Runtime ==="
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE_RUNTIME/api/v1/logs?limit=10" \
  | jq '.invocations[] | "[R] " + .path'
```

### LP UI との連携

ここで取得できる履歴は **Cmd+K パレットの Log タブ**の中身と同じソース。AI Agent が curl で叩いたコマンドが、開発者の手元の Editor UI 上にも履歴として並ぶ。

### `result.value` で前回戻り値を取り出すパターン

直前のコマンドが副作用なしで何かを返すタイプ (例: `Player/Position/Get`) の場合、`/logs?limit=1` で最新を取って `result.value` を読む手が使える。ただし**レースコンディション注意** — 実行と取得の間に別コマンドが入ると別の戻り値が来る。同期的に取りたいなら `lp-execute` の戻り値を直接使う。

### `uloop-get-logs` との違い

| skill | 取得対象 |
|---|---|
| `lp-get-logs` (本スキル) | LP の `[ConsoleCommand]` 実行履歴 (UI/HTTP/scenario 統合) |
| `uloop-get-logs` | Unity Editor の Console Window のログ (`Debug.Log*` 全体) |

両方使い分け可能。コマンド実行に伴う `Debug.Log*` は `lp-get-logs` の `result.logs[]` にも入るので、ピンポイントで欲しいなら LP 側で十分。

---

## Error Handling

| Status | 状況 | 対処 |
|---|---|---|
| 401 | Token 不一致 | `~/.liminal-palette/token` 再読み込み |
| 400 | `limit` が数値でない | クエリを確認 |

`limit` を 200 超で送ると、サーバ側で 200 にクランプ (エラーにはならない)。

---

## See also

- `/lp-execute` — 履歴に記録されるコマンドを実行
- `/lp-run-scenario` — シナリオ内コマンドも履歴に記録される
- examples: [jq-queries.md](examples/jq-queries.md) — フィルタ / 集計 / レポート用 jq パターン集
