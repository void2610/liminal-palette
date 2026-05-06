---
name: lp-execute
description: "Invoke a `[ConsoleCommand]` via LiminalPalette HTTP POST. Use when you need to: (1) Trigger gameplay actions (spawn enemies, set HP, teleport, change scene), (2) Read return values via `result.value` and captured `Debug.Log*` via `result.logs`, (3) Pass typed args (Vector3, enum, Color, bool) using LP's string-coerced format, (4) Check `result.error` / `result.exceptionType` on failure for retry logic."
---

# lp-execute

LiminalPalette に登録されたコマンドを HTTP POST で実行する。**ゲーム操作の中核**。型変換のクセが多いので、初見では本ページの「型変換のクセ」セクションを必ず読むこと。

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
curl -s -H "Authorization: Bearer $LP_TOKEN" \
     -H "Content-Type: application/json" \
     -X POST "$LP_BASE/api/v1/execute" \
     -d '{"path": "<Command/Path>", "args": {<key>: <string-value>, ...}}'
```

### Request body

| フィールド | 必須 | 型 | 説明 |
|---|---|---|---|
| `path` | ✅ | string | `lp-list-commands` で発見した `path` (大文字小文字区別) |
| `args` | ✅ | object | 引数。**全ての値を string として送る** (詳細は下記)。引数 0 個でも `{}` を必ず付ける |

---

## Examples

```bash
# 1) 単純な int 引数
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Player/Health/Set","args":{"value":"100"}}'

# 2) 引数 0 個 (args は {} を必ず付ける)
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Editor/Console/Clear","args":{}}'

# 3) Vector3 引数 (カンマ区切り、空白とカッコは寛容)
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Player/Position/Teleport","args":{"pos":"1,2,3"}}'

# 4) bool 引数 (string で "true" / "false")
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Game/SetGodMode","args":{"enabled":"true"}}'

# 5) enum 引数 (名前指定、大文字小文字無視)
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Enemy/Spawn","args":{"type":"Goblin"}}'

# 6) Color 引数 (HEX)
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"UI/Background/SetColor","args":{"c":"#FF8800"}}'

# 7) 結果から value だけ取り出す
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Math/Add","args":{"a":"3","b":"4"}}' \
  | jq -r '.value'

# 8) 失敗時のデバッグ (success が false なら error と stackTrace を見る)
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Player/Health/Set","args":{"value":"abc"}}' \
  | jq '{success, error, exceptionType, stackTrace}'

# 9) Debug.Log* の取得 (実行中のログを `result.logs` 経由で読む)
curl -s -H "Authorization: Bearer $LP_TOKEN" -H "Content-Type: application/json" \
  -X POST "$LP_BASE/api/v1/execute" \
  -d '{"path":"Test/Echo","args":{"msg":"hi"}}' \
  | jq '.logs[] | {type, message}'
```

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
    {
      "type": "Log",
      "message": "[Echo] Hi",
      "stackTrace": "...",
      "timestamp": "2026-04-30T12:34:56.789Z"
    }
  ]
}
```

| フィールド | 説明 |
|---|---|
| `success` | 実行成功なら true。引数バインドエラー / 例外いずれも false |
| `value` | 戻り値の `ToDisplayString` 文字列化。`void` / `Task` / 失敗時は null |
| `error` | 失敗時のエラーメッセージ |
| `exceptionType` | 例外起因の失敗時に `System.InvalidOperationException` 等の FullName |
| `stackTrace` | 例外のスタックトレース (デバッグ用) |
| `durationMs` | 実行所要時間 (ミリ秒) |
| `logs[]` | 実行中に Unity の `Debug.Log*` で出たログの配列 (時系列順) |

⚠️ **`Exception` オブジェクト本体は来ない**。プロセス境界を越える object を送らない原則のため、型名と stackTrace の string のみ。

---

## 型変換のクセ (★最重要★)

LP の `TypeConverterRegistry` は **全引数を string として受け取り、ターゲット型に変換する**。HTTP 経由では JSON の type fidelity を当てにせず、すべての値を **string でクォート**して送るのが安全。

### 共通ルール

- 数値 / bool / enum もすべて `"100"` / `"true"` / `"Goblin"` のように **クォートする**
- JSON で `{"value": 100}` のように直接書いても受け付けるが、内部で文字列化される (将来の挙動変更を避けるためクォート推奨)
- 引数 0 個でも `"args": {}` を**必ず指定する** (キーごと省略すると 400 BadRequest)

### 型ごとの受理フォーマット

#### Primitive (`int`, `long`, `float`, `double`, `bool`, `string`, `char`)

```json
{"args": {"i": "42", "f": "3.14", "b": "true", "s": "hello", "c": "A"}}
```

- 数値は `InvariantCulture` でパース (小数点は `.`)
- bool は `"true"` / `"false"` (大文字小文字無視)
- string はそのまま

#### `Vector2` / `Vector3` / `Vector4` / `Vector2Int` / `Vector3Int`

カンマ・空白・タブいずれも区切りとして OK。カッコ類 `()` `[]` `{}` は許容 (剥がされる)。

```json
{"args": {"v3":  "1,2,3"}}
{"args": {"v3":  "(1, 2, 3)"}}
{"args": {"v3":  "[1 2 3]"}}
{"args": {"v2":  "0.5, -0.5"}}
{"args": {"v3i": "10,20,30"}}
```

#### `Color` / `Color32`

| 入力形式 | 例 |
|---|---|
| HEX (RGB / RGBA) | `"#FF8800"` / `"#FF8800CC"` |
| 数値 0..1 (Color) | `"1.0, 0.53, 0.0"` または `"1.0, 0.53, 0.0, 1.0"` |
| 数値 0..255 (Color32) | `"255, 136, 0, 255"` |

`#` 付きは `ColorUtility.TryParseHtmlString` 経由なので Unity 標準色名 (`"red"`, `"blue"` 等は `#` 無しでは弾かれる — 必ず `#` 付き HEX か数値で送る)。

#### Enum

```json
{"args": {"dir": "Up"}}        // 名前 (大文字小文字無視)
{"args": {"dir": "0"}}         // 数値文字列でも OK
{"args": {"flag": "Read,Write"}}  // [Flags] enum はカンマ区切り
```

**`parameters[].choices` が空でない場合、必ずそこから選ぶ** (`lp-list-commands` で確認)。

#### `UnityEngine.Object` 派生 (GameObject / Component / Asset)

HTTP 経由はサポートが限定的:

| 入力形式 | 用途 | 制限 |
|---|---|---|
| `"@<entityID>"` | Resources.EntityIdToObject で解決 | UI ピッカーで取得した ID 前提 |
| `"GameObject:<name>"` | シーン上の GameObject 名前検索 | Runtime のみ |

curl でランダムに asset を渡すのは難しい。**`UnityEngine.Object` 引数を取るコマンドは UI 経由 (Cmd+K パレット) が現実的**。HTTP からは事前に `[ConsoleCommand]` 側で「名前で解決して返す」ファサードを作るのが筋。

---

## Error Handling

| Status | 状況 | 対処 |
|---|---|---|
| 200 + `success:false` | コマンド実行例外 / 引数バインド失敗 | `result.error` + `result.exceptionType` を読む。引数の型を見直す |
| 400 BadRequest | JSON パース失敗 / `path` 欠落 / `args` 欠落 | request body を再確認。`args` は `{}` でも明示必須 |
| 401 Unauthorized | Token 不一致 | `~/.liminal-palette/token` を再読み込み / Editor 再起動 |
| 404 Not Found | `path` が未登録 | `lp-list-commands` で正確な綴りを確認 (大文字小文字区別) |
| 405 Method Not Allowed | GET で叩いた | `-X POST` を忘れていないか |
| 413 Payload Too Large | body 1 MB 超過 | 引数経由でなくファイルパス渡しに切り替え (利用側で `IpcSettings.MaxRequestBodyBytes` を上げる選択肢もあり) |
| 429 Too Many Requests | 30 req/s 超過 | 間隔を空ける。`/scenarios/run` と枠共有 |
| 500 Internal Server Error | endpoint 内例外 | `error` 本文 / Editor の Console を確認 |

### よくある失敗パターン

#### `success: false` で `error: "Cannot convert ... to Vector3"`
- → カンマ区切りでない / 要素数が 3 でない。`"1,2,3"` 形式に。

#### `success: false` で `error: "Required parameter 'value' is missing"`
- → `args` のキー名が `parameters[].name` と一致していない。typo もしくは大文字小文字違い。

#### `success: false` で `value: null` だが `error` も null
- → コマンドの戻り値が `void` / `Task` / `ValueTask`。これは正常 (`success: true` と組み合わせなら成功)。

#### `result.logs[]` が空なのにコマンド内で `Debug.Log` を呼んだはず
- → コマンド内で別スレッドから `Debug.Log` した場合、メインスレッドにマーシャルされる前に capture が止まる可能性あり。`MainThreadDispatcher` 経由でログ出すか、`async` コマンドにする。

---

## Notes

### Async コマンド

`isAsync: true` のコマンドは Task 完了まで HTTP レスポンスがブロックされる。`durationMs` がそのまま実時間。タイムアウトしたい場合は curl 側で `--max-time` を指定:

```bash
curl --max-time 30 -s -H "Authorization: Bearer $LP_TOKEN" ...
```

### result.logs の使い道

`result.logs[]` は **実行中の `Debug.Log* `だけを切り取った** ログ。AI Agent が「何が起きたか」を再現可能性付きで読むのに使える。Unity Console 全体ではない (Console 全体は `uloop-get-logs` などの別経路)。

### レートリミット

`/execute` と `/scenarios/run` は **30 req/s の枠を共有**する (1 秒スライディングウィンドウ)。連続実行する場合は `sleep 0.04` 程度を挟むか、`lp-run-scenario` ad-hoc で 1 リクエストにまとめると効率的。

### Production ビルドでは動かない

LP の HTTP サーバ自体が asmdef の defineConstraints で Production からコンパイル除外される。Production の APK / 実行ファイルに対して curl を送っても応答することはない。Development build か Editor 限定。

---

## 関連スキル

- `lp-list-commands` — 実行する `path` と引数スキーマの発見
- `lp-get-state` — 実行後のゲーム状態を検証
- `lp-get-logs` — invocation 履歴 (`/execute` で実行したものも記録される)
- `lp-run-scenario` ad-hoc 経路 — 複数 `lp-execute` を 1 リクエストにまとめたい時
