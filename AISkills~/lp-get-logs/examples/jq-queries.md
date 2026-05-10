# lp-get-logs — jq クエリレシピ集

`lp logs --limit N --json` の結果に対する jq パターン。`lp logs --limit N --json | jq '<expr>'` の `<expr>` 部分を示す。

## 基本フィルタ

### path 一覧 (重複あり、新しい順)

```jq
.invocations[].path
```

### path 一覧 (重複排除)

```jq
[.invocations[].path] | unique
```

### 直近 N 件の概要

```jq
.invocations[0:10] | map({path, ts: .timestamp, ok: .result.success, ms: .result.durationMs})
```

### 成功 / 失敗の件数

```jq
{
  total: (.invocations | length),
  success: ([.invocations[] | select(.result.success)] | length),
  failed:  ([.invocations[] | select(.result.success == false)] | length)
}
```

## 失敗 / エラー分析

### 失敗 invocation のみ

```jq
.invocations[] | select(.result.success == false)
```

### exceptionType 別の失敗件数

```jq
[.invocations[] | select(.result.exceptionType != null) | .result.exceptionType]
  | group_by(.) | map({type: .[0], count: length}) | sort_by(-.count)
```

### 直近の失敗 1 件 (デバッグ起点)

```jq
[.invocations[] | select(.result.success == false)] | .[0]
  | {path, args, error: .result.error, exceptionType: .result.exceptionType, stack: .result.stackTrace}
```

### 引数バインド失敗だけ (exceptionType: null + success: false)

```jq
.invocations[] | select(.result.success == false and .result.exceptionType == null)
  | {path, args, error: .result.error}
```

## path / カテゴリ別分析

### 特定 path の最近の実行

```jq
.invocations[] | select(.path == "Player/Health/Set") | {ts: .timestamp, args, ok: .result.success, value: .result.value}
```

### path 別実行回数

```jq
[.invocations[].path] | group_by(.) | map({path: .[0], count: length}) | sort_by(-.count)
```

### カテゴリ (path の prefix) 別

```jq
[.invocations[] | (.path | split("/")[0])]
  | group_by(.) | map({category: .[0], count: length}) | sort_by(-.count)
```

### 特定カテゴリだけ

```jq
.invocations[] | select(.path | startswith("Combat/"))
```

## 時間 / パフォーマンス

### durationMs > 100 のスローコマンド

```jq
.invocations[] | select(.result.durationMs > 100)
  | {path, ms: .result.durationMs, ts: .timestamp}
```

### path 別の平均所要時間

```jq
[.invocations[] | {path, ms: .result.durationMs}]
  | group_by(.path)
  | map({path: .[0].path, count: length, avgMs: ((map(.ms) | add) / length)})
  | sort_by(-.avgMs)
```

### 特定時刻以降の invocation

```jq
.invocations[] | select(.timestamp > "2026-05-07T00:00:00Z")
```

## scenarios 関連

### シナリオ内コマンドだけ

```jq
.invocations[] | select(.isFromScenario == true)
```

### 直接実行のみ

```jq
.invocations[] | select(.isFromScenario != true)
```

### シナリオ集約レコード (path が `Scenario/...` のもの)

```jq
.invocations[] | select(.path | startswith("Scenario/"))
```

## 引数 / 戻り値の解析

### 特定 path で使われた args の重複排除

```jq
[.invocations[] | select(.path == "Enemy/Spawn") | .args] | unique
```

### 戻り値が "true" の件数

```jq
.invocations[] | select(.result.value == "true") | .path
```

### `result.logs[]` に "Error" を含むもの

```jq
.invocations[] | select(any(.result.logs[]?; .type == "Error"))
  | {path, errorLogs: [.result.logs[] | select(.type == "Error") | .message]}
```

## レポート生成

### Markdown 表形式

```jq
"| Path | OK | Duration | Time |",
"|---|---|---|---|",
(.invocations[] | "| `\(.path)` | \(if .result.success then "✓" else "✗" end) | \(.result.durationMs)ms | \(.timestamp) |")
```

(`lp logs --json` の結果を `jq -r` で出力すると Markdown が直接得られる)

### 失敗だけ Markdown

```jq
"| Path | Args | Error |",
"|---|---|---|",
(.invocations[] | select(.result.success == false)
  | "| `\(.path)` | `\(.args | tojson)` | \(.result.error) |")
```

### CSV 化

```jq
-r '
  ["timestamp","path","success","durationMs","error"],
  (.invocations[] | [.timestamp, .path, .result.success, .result.durationMs, .result.error // ""])
  | @csv
'
```

## AI Agent 向け実用パターン

### 「セッションで何が走ったか」を要約

```jq
{
  total: (.invocations | length),
  byCategory: ([.invocations[] | (.path | split("/")[0])] | group_by(.) | map({k: .[0], v: length})),
  failed: [.invocations[] | select(.result.success == false) | {path, error: .result.error}],
  slowest: ([.invocations[] | {path, ms: .result.durationMs}] | sort_by(-.ms) | .[0:3])
}
```

### 「同じコマンドを直近で何回叩いたか」(spam 検出)

```jq
[.invocations[].path] | group_by(.)
  | map({path: .[0], count: length})
  | map(select(.count >= 3))
```

### 「失敗 → 成功のリトライパターン」検出

`Player/X` を引数 A で失敗、引数 B で成功した、というシーケンスを抽出:

```jq
[.invocations | reverse | _nwise(2)]
  | map(select(.[0].path == .[1].path and .[0].result.success == false and .[1].result.success == true))
  | map({path: .[0].path, failedArgs: .[0].args, successArgs: .[1].args})
```

(`_nwise` は jq の隣接 N 件取り出し。実装によっては `range` で組む)

### 「最後の N 件を JSON dump して別ツールで解析」

```bash
lp logs --limit 200 --json > /tmp/lp-logs-snapshot.json

jq '.invocations | length' /tmp/lp-logs-snapshot.json
```
