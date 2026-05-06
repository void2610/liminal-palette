---
name: lp-get-logs
description: "Fetch recent command invocation history from LiminalPalette. Use when you need to: (1) Audit which commands ran (UI + HTTP + scenarios all merged), (2) Re-derive args used in a previous failed call to retry with correction, (3) Time-correlate game events with executions, (4) Inspect `IsFromScenario` to separate scenario-internal calls from direct calls."
---

# lp-get-logs

LiminalPalette の `InvocationStore` に記録された **コマンド実行履歴**を新しい順で取得する。UI 経由 / HTTP 経由 / シナリオ内 すべてが同じ Store に記録される。

⚠️ Unity の `Debug.Log` 全体を取りたい場合は LP ではなく `uloop-get-logs` 等の別経路を使うこと。本スキルは **LP の invocation history** 限定。

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
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/logs?limit=50"
```

| Query | 既定 | 上限 | 説明 |
|---|---|---|---|
| `limit` | 50 | 200 (`InvocationStore.Capacity`) | 取得件数。新しい順 |

---

## Examples

```bash
# 直近 10 件の path と success だけ
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/logs?limit=10" \
  | jq '.invocations[] | {path, ts: .timestamp, ok: .result.success}'

# 失敗だけ抽出
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/logs?limit=200" \
  | jq '.invocations[] | select(.result.success == false) | {path, error: .result.error}'

# シナリオ外の直接実行だけ (UI / HTTP /execute 経由)
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/logs?limit=200" \
  | jq '.invocations[] | select(.isFromScenario != true) | .path'

# 特定 path の最近の args を再現 (リトライ用)
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/logs?limit=200" \
  | jq '.invocations[] | select(.path == "Player/Health/Set") | {timestamp, args, success: .result.success}'

# 直近 1 件の result.value だけ
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/logs?limit=1" \
  | jq -r '.invocations[0].result.value'

# 所要時間の長いコマンドだけ (durationMs > 100)
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/logs?limit=200" \
  | jq '.invocations[] | select(.result.durationMs > 100) | {path, ms: .result.durationMs}'
```

---

## Output

```json
{
  "invocations": [
    {
      "path": "Test/Vector",
      "timestamp": "2026-04-30T12:34:56.789Z",
      "args": { "v": "(1, 2, 3)" },
      "result": {
        "success": true,
        "value": "(2.00, 4.00, 6.00)",
        "error": null,
        "exceptionType": null,
        "stackTrace": null,
        "durationMs": 1.07,
        "logs": [...]
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
| `invocations[].args` | 実行時の引数 (string 化済み)。リトライに使える |
| `invocations[].result` | `lp-execute` のレスポンスと**同一スキーマ** |
| `total` | Store 内の総件数 (limit と独立) |
| `limit` | 実際に返した件数の上限 |

### `result` の中身

`lp-execute` の Output セクションと完全に同じ。`success` / `value` / `error` / `exceptionType` / `stackTrace` / `durationMs` / `logs[]`。

---

## Error Handling

| Status | 状況 | 対処 |
|---|---|---|
| 401 Unauthorized | Token 不一致 | `~/.liminal-palette/token` を再読み込み / Editor 再起動 |
| 400 BadRequest | `limit` が数値でない | クエリを確認 |

`limit` を 200 超で送ると、サーバ側で 200 にクランプされる (エラーにはならない)。

---

## Notes

### Capacity 上限

`InvocationStore` のリングバッファは 200 件で固定。それ以上は古いものから消える。長時間プレイで履歴を全部取りたい場合は **定期的に `/logs` を取って外部に保存**するパターン。

### シナリオ内コマンドの扱い

シナリオ (`/scenarios/run`) 内で `command` ステップとして実行されたコマンドも `InvocationStore` に記録される (`isFromScenario: true` でマークされる)。

| 用途 | フィルタ |
|---|---|
| 直接実行だけ見たい (UI / HTTP /execute 経由のみ) | `select(.isFromScenario != true)` |
| シナリオ内ステップだけ見たい | `select(.isFromScenario == true)` |
| シナリオ集約 (シナリオ全体を 1 行で見る) | path が `Scenario/<シナリオ path>` 形式の行を探す |

### LP UI との連携

ここで取得できる履歴は **Cmd+K パレットの Log タブ**の中身と同じソース。AI Agent が curl で叩いたコマンドが、開発者の手元の Editor UI 上にも履歴として並ぶ。

### `result.value` で前回戻り値を取り出すパターン

直前のコマンドが副作用なしで何かを返すタイプ (例: `Player/Position/Get`) の場合、`/logs?limit=1` で最新を取って `result.value` を読む手が使える。ただしレースコンディションに注意 (実行と取得の間に別コマンドが入ることがある) — 同期的に取りたいなら `lp-execute` の戻り値を直接使う。

### `uloop-get-logs` との違い

| skill | 取得対象 |
|---|---|
| `lp-get-logs` (本スキル) | LiminalPalette の `[ConsoleCommand]` 実行履歴 (UI/HTTP/scenario 統合) |
| `uloop-get-logs` | Unity Editor の Console Window のログ (`Debug.Log*` 全体) |

両方使い分け可能。コマンド実行に伴う `Debug.Log*` は `lp-get-logs` の `result.logs[]` にも入るので、ピンポイントで欲しいなら LP 側で十分。

---

## 関連スキル

- `lp-execute` — 履歴に記録されるコマンドを実行
- `lp-run-scenario` — シナリオも履歴に記録される
- `lp-overview` — レートリミット / Production 除外などの周辺ルール
