---
name: liminal-get-logs
description: 'Fetch recent command invocation history from LiminalPalette InvocationStore via `liminal logs` (UI + HTTP + scenarios all merged). Use to audit which commands ran, recover args from a previous failed call to retry, time-correlate game events with executions, or filter IsFromScenario to separate scenario-internal calls. NOT the same as Unity Console logs (use uloop-get-logs for those).'
when_to_use: 'Trigger phrases: "直近の実行履歴", "何が走ったか", "前回の失敗を見せて", "log の確認", "command history", "what did I run last", "audit invocations".'
allowed-tools: Bash(liminal *), Bash(jq *)
---

# liminal-get-logs

LiminalPalette の `InvocationStore` に記録された **コマンド実行履歴**を新しい順で取得する。UI 経由 / HTTP 経由 / シナリオ内すべてが同じ Store に記録される。

⚠️ Unity の `Debug.Log*` 全体ではない (本スキルは LP の `[LiminalCommand]` 実行履歴限定)。Unity Console を見たい場合は `uloop-get-logs` などの別経路を使う。

---

## 基本

```bash
# 直近 20 件 (既定)
liminal logs

# 件数指定
liminal logs --limit 50
```

| 引数 | 既定 | 上限 | 説明 |
|---|---|---|---|
| `--limit N` | 20 | 400 (`InvocationStore.MaxRetained`) | 取得件数。新しい順 |

出力例 (装飾付き):

```
  ✓  2026-05-10T00:26:24.304Z  Player/MoveTo  (0.15ms)
      args: x=3, y=3
      value: プレイヤーを (3, 3, h=5) に移動しました
  ✗  2026-05-10T00:25:11.108Z  Enemy/Spawn  (0.41ms)
      args: type=UnknownEnemy
      error: type 'UnknownEnemy' is not a valid EnemyType

  shown 2 / total 12
```

---

## `--json` で取って `jq` で絞る

### 失敗だけ抽出

```bash
liminal logs --limit 200 --json \
  | jq '.invocations[] | select(.result.success == false) | {path, error: .result.error, args}'
```

### 手動実行 (パレット UI) のみ

```bash
liminal logs --limit 400 --json \
  | jq -r '.invocations[] | select(.origin == "user") | .path'
```

### 直近 1 件の `result.value`

```bash
liminal logs --limit 1 --json | jq -r '.invocations[0].result.value'
```

### 所要時間が長いコマンド (durationMs > 100)

```bash
liminal logs --limit 200 --json \
  | jq '.invocations[] | select(.result.durationMs > 100) | {path, ms: .result.durationMs}'
```

より多くのレシピは [examples/jq-queries.md](examples/jq-queries.md)。

---

## Output (`--json`)

```json
{
  "invocations": [
    {
      "path": "Test/Vector",
      "timestamp": "2026-04-30T12:34:56.789Z",
      "args": {"v": "(1, 2, 3)"},
      "origin": "user",
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
| `invocations[].origin` | 実行経路。`user` (パレット UI) / `ipc` (本 API) / `scenario` (シナリオ内ステップ・集約) |
| `invocations[].result` | `liminal exec` のレスポンスと**同一スキーマ** (success, value, error, exceptionType, stackTrace, durationMs, logs) |
| `total` | Store 内の総件数 (limit と独立) |
| `limit` | 実際に返した件数の上限 |

---

## 失敗デバッグの定石

直近の失敗から原因を辿るパターン:

```bash
# 1. 直近 1 件の失敗を取得
LAST_FAIL=$(liminal logs --limit 200 --json \
  | jq '[.invocations[] | select(.result.success == false)] | .[0]')

echo "$LAST_FAIL" \
  | jq '{path, args, error: .result.error, exceptionType: .result.exceptionType, stack: .result.stackTrace}'

# 2. 引数の型が間違っていた可能性 → スキーマ確認
PATH_FAIL=$(echo "$LAST_FAIL" | jq -r '.path')
liminal commands --json \
  | jq --arg p "$PATH_FAIL" '.commands[] | select(.path == $p) | .parameters'

# 3. 修正版で再実行
liminal exec "$PATH_FAIL" value=50
```

---

## 実行経路との関係

すべての記録は実行経路 `origin` を持つ。シナリオ (`liminal run`) 内の `command` ステップは `scenario`、`liminal exec` や MCP 経由の HTTP 実行は `ipc`、パレット UI からの手動実行は `user`。

| 用途 | フィルタ |
|---|---|
| 手動実行のみ (パレット UI) | `select(.origin == "user")` |
| CLI / MCP 経由のみ | `select(.origin == "ipc")` |
| シナリオ内ステップのみ | `select(.origin == "scenario")` |
| シナリオ集約 (シナリオ全体を 1 行で見る) | path が `Scenario/<シナリオ path>` 形式の行を探す (LP 側でシナリオ実行ごとに集約レコードも記録される) |

---

## Notes

### Capacity

`InvocationStore` は **手動実行 (`origin == "user"`) 200 件と、自動化由来 (`ipc` / `scenario`) 200 件を独立した枠で保持**する (合計最大 400 件)。枠ごとに古いものから消えるので、シナリオや E2E をいくら流しても手動実行の履歴は押し出されない。長時間プレイで履歴を全部取りたい場合は **定期的に `liminal logs` を取って外部に保存**するパターン。

```bash
# 定期 dump
mkdir -p /tmp/lp-logs
while true; do
  liminal logs --limit 400 --json > "/tmp/lp-logs/$(date +%Y%m%d-%H%M%S).json"
  sleep 60
done
```

### Editor / Runtime ごとに別 Store

両稼働時、Editor (7610) と Runtime (7611) で **別の `InvocationStore`** が立っている。Editor で叩いたコマンドは Runtime の logs には出ない (逆も)。

```bash
# 両方統合して見る
echo "=== Editor ==="
liminal --port 7610 logs --limit 10 --json | jq -r '.invocations[] | "[E] " + .path'

echo "=== Runtime ==="
liminal --port 7611 logs --limit 10 --json | jq -r '.invocations[] | "[R] " + .path'
```

### LP UI との連携

ここで取得できる履歴は **Cmd+K パレットの Log タブ**の中身と同じソース。AI Agent が CLI で叩いたコマンドが、開発者の手元の Editor UI 上にも履歴として並ぶ。

### `result.value` で前回戻り値を取り出すパターン

直前のコマンドが副作用なしで何かを返すタイプ (例: `Player/Position/Get`) の場合、`liminal logs --limit 1 --json` で最新を取って `result.value` を読む手が使える。ただし**レースコンディション注意** — 実行と取得の間に別コマンドが入ると別の戻り値が来る。同期的に取りたいなら `liminal exec --json | jq -r .value` を使う。

### `uloop-get-logs` との違い

| skill | 取得対象 |
|---|---|
| `liminal-get-logs` (本スキル) | LP の `[LiminalCommand]` 実行履歴 (UI/HTTP/scenario 統合) |
| `uloop-get-logs` | Unity Editor の Console Window のログ (`Debug.Log*` 全体) |

両方使い分け可能。コマンド実行に伴う `Debug.Log*` は `liminal logs` の `result.logs[]` にも入るので、ピンポイントで欲しいなら LP 側で十分。

---

## Error Handling

| 症状 | 状況 | 対処 |
|---|---|---|
| HTTP 401 | Token 不一致 | `~/.liminal-palette/token` 再生成 |
| `--limit` を 400 超で送った | サーバ側で 400 にクランプ (エラーにはならない) | そのままで OK |

---

## See also

- `/liminal-execute` — 履歴に記録されるコマンドを実行
- `/liminal-run-scenario` — シナリオ内コマンドも履歴に記録される
- examples: [jq-queries.md](examples/jq-queries.md) — フィルタ / 集計 / レポート用 jq パターン集

---

## `liminal` コマンド自体の不具合

`liminal` は本パッケージとは別の [liminal-cli](https://github.com/void2610/liminal-cli) リポジトリにある
Rust 製の単体バイナリ。出力の整形 / 引数の解釈 / exit code がおかしい場合はそちらへ。
サーバ (Unity 側) の応答自体がおかしい場合は liminal-palette へ。

```bash
liminal --version   # 版と出自を表示する
liminal doctor      # 切り分けに要る情報を一望する
```
