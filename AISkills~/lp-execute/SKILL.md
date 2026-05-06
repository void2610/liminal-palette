---
name: lp-execute
description: 'Invoke a [ConsoleCommand] via LiminalPalette HTTP POST. Triggers gameplay actions (spawn enemies, set HP, teleport, change scene) and reads return values. All args are sent as strings (numbers, bools, Vector3, Color, enum) — see references/type-conversion.md for the format of each type. Use when the user wants Unity to actually do something, not just inspect state.'
when_to_use: 'Trigger phrases: "コマンド実行", "Player/X を Y で実行", "spawn する", "HP を 100 にして", "テレポート", "execute LP command", "run X", "trigger action", "call console command".'
allowed-tools: Bash(curl *), Bash(jq *), Bash(cat *)
---

# lp-execute

LiminalPalette の `[ConsoleCommand]` を HTTP POST で実行する。**ゲーム操作の中核スキル**。

引数の型変換クセが多い (全部 string で送る、Vector3 はカンマ区切り、enum は名前一致など) ため、初見の型が出てきたら **必ず [references/type-conversion.md](references/type-conversion.md) を確認**してから組み立てる。

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

## リクエスト形式

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" \
     -H "Content-Type: application/json" \
     -X POST "$LP_BASE/api/v1/execute" \
     -d '{"path": "<Command/Path>", "args": {<key>: <string-value>, ...}}'
```

| フィールド | 必須 | 型 | 説明 |
|---|---|---|---|
| `path` | ✅ | string | `lp-list-commands` で発見した path (大文字小文字区別) |
| `args` | ✅ | object | 引数。**全 value を string で送る**。引数 0 個でも `{}` を必ず付ける |

### 大原則

1. **全引数を string にクォート**: `"100"` / `"true"` / `"1,2,3"` / `"Goblin"` / `"#FF8800"`
2. **`args` キーは省略しない**: 引数 0 個でも `"args": {}` 必須
3. **path は完全一致**: 大文字小文字、スラッシュの数まで完全一致

---

## 基本パターン

### int 引数 1 つ

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Player/Health/Set","args":{"value":"100"}}'
```

### 引数 0 個

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Editor/Console/Clear","args":{}}'
```

### Vector3 (カンマ区切り)

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Player/Position/Teleport","args":{"pos":"1,2,3"}}'
```

### bool / enum / Color の例は [examples/basic.md](examples/basic.md) を参照

複合パターン (async, retry, large args, multi-call) は [examples/advanced.md](examples/advanced.md)。

---

## Output

```json
{
  "success": true,
  "value": "(2.00, 4.00, 6.00)",
  "error": null,
  "exceptionType": null,
  "stackTrace": null,
  "durationMs": 1.0656,
  "logs": [
    {"type":"Log","message":"[Echo] Hi","stackTrace":"...","timestamp":"2026-04-30T12:34:56.789Z"}
  ]
}
```

| フィールド | 説明 |
|---|---|
| `success` | 実行成功で true。引数バインドエラー / 例外いずれも false |
| `value` | 戻り値の `ToDisplayString` 文字列化。void / Task / 失敗時は null |
| `error` | 失敗時のエラーメッセージ (失敗時のみ) |
| `exceptionType` | 例外の FullName (例: `System.InvalidOperationException`) |
| `stackTrace` | 例外のスタックトレース (デバッグ用) |
| `durationMs` | 実行所要時間 (ミリ秒) |
| `logs[]` | 実行中の `Debug.Log*` 配列 (時系列順) |

⚠️ **`Exception` オブジェクト本体は来ない**。プロセス境界を越える object を送らない原則のため、型名 + stackTrace を string で返す。

### 結果の典型パース

```bash
RESP=$(curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Math/Add","args":{"a":"3","b":"4"}}')

echo "$RESP" | jq '{success, value, ms: .durationMs}'

# 失敗時
if [ "$(echo "$RESP" | jq -r '.success')" = "false" ]; then
  echo "$RESP" | jq '{error, exceptionType, stackTrace}'
fi
```

---

## 型変換のクセ (要約)

各型の受理フォーマット早見:

| 型 | 形式 | 例 |
|---|---|---|
| `int` / `long` / `float` / `double` | 数値リテラル (string) | `"42"`, `"3.14"` |
| `bool` | `"true"` / `"false"` (大小無視) | `"true"` |
| `string` | そのまま | `"hello"` |
| `Vector2/3/4` | カンマ区切り | `"1,2,3"`, `"(1, 2, 3)"`, `"[1 2 3]"` |
| `Vector2Int/3Int` | 同上、整数 | `"10,20,30"` |
| `Color` (HEX) | `#RRGGBB` / `#RRGGBBAA` | `"#FF8800"` |
| `Color` (数値 0..1) | `r,g,b[,a]` | `"1.0, 0.53, 0.0"` |
| `Color32` (数値 0..255) | `r,g,b[,a]` | `"255, 136, 0, 255"` |
| Enum | 名前 (大小無視) または数値 | `"Up"`, `"0"` |
| `[Flags]` Enum | カンマ区切り名前 | `"Read,Write"` |
| `UnityEngine.Object` | `"@<entityID>"` または `"GameObject:<name>"` | curl 経由は限定的 |

詳細 (各 Converter の挙動 / fallback / 失敗時のメッセージ等) は [references/type-conversion.md](references/type-conversion.md)。

---

## エラー対処 (要約)

| Status | 状況 | 一次対処 |
|---|---|---|
| 200 + `success:false` | 実行例外 / 引数バインド失敗 | `result.error` + `result.exceptionType` を読む |
| 400 BadRequest | JSON 文法エラー / 必須欠落 | request body を再確認 |
| 401 | Token 不一致 | `~/.liminal-palette/token` 再 cat |
| 404 | path 未登録 | `lp-list-commands` で綴り確認 |
| 405 | method 違い | `-X POST` を確認 |
| 413 | body 1 MB 超過 | ファイルパス渡しに切り替え |
| 429 | rate limit (30 req/s) | 間隔を空ける |

詳細フローチャートと各エラーの根本原因は [references/error-handling.md](references/error-handling.md)。

---

## Notes

### Async コマンド

`isAsync: true` のコマンドは Task 完了まで HTTP レスポンスがブロックされる。`durationMs` がそのまま実時間。タイムアウトは curl 側で:

```bash
curl --max-time 30 -s -H "Authorization: Bearer $LP_TOKEN" ...
```

### `result.logs` の使いどころ

実行中の `Debug.Log*` だけが切り取られて返る。AI Agent が「コマンドが何をしたか」を再現可能性付きで読める。Unity Console 全体ではない (それは uloop-get-logs)。

### Production ビルドでは動かない

LP の HTTP サーバ自体が asmdef defineConstraints で Production 除外。Production の APK / 実行ファイルに curl しても応答しない。Development build か Editor のみ。

### レートリミットの枠は scenarios と共有

`/execute` と `/scenarios/run` は **30 req/s 共有**。連投する場合は `lp-run-scenario` の ad-hoc 経路で 1 リクエストにまとめる方が効率的。

---

## See also

- `/lp-list-commands` — path と引数スキーマの発見
- `/lp-get-state` — 実行後のゲーム状態を検証
- `/lp-get-logs` — invocation 履歴 (本スキルの実行も記録される)
- `/lp-run-scenario` — 複数 execute + 検証を 1 リクエストにまとめる
- references:
  - [type-conversion.md](references/type-conversion.md) — 各型の完全な変換仕様
  - [error-handling.md](references/error-handling.md) — エラー status 別の根本原因と対処フロー
- examples:
  - [basic.md](examples/basic.md) — primitive / Vector / Color / enum の基本例
  - [advanced.md](examples/advanced.md) — async, retry, jq パイプ, 連続実行
