---
name: liminal-execute
description: 'Invoke a [LiminalCommand] via `liminal exec`. Triggers gameplay actions (spawn enemies, set HP, teleport, change scene) and reads return values. All args are sent as strings (numbers, bools, Vector3, Color, enum) — see references/type-conversion.md for the format of each type. Use when the user wants Unity to actually do something, not just inspect state.'
when_to_use: 'Trigger phrases: "コマンド実行", "Player/X を Y で実行", "spawn する", "HP を 100 にして", "テレポート", "execute LP command", "run X", "trigger action", "call console command".'
allowed-tools: Bash(liminal *), Bash(jq *)
---

# liminal-execute

LiminalPalette の `[LiminalCommand]` を `liminal exec` で実行する。**ゲーム操作の中核スキル**。

引数の型変換クセが多い (Vector3 はカンマ区切り、enum は名前一致など) ため、初見の型が出てきたら **必ず [references/type-conversion.md](references/type-conversion.md) を確認**してから組み立てる。

---

## 構文

```bash
liminal exec <Command/Path> [name=value]...
```

| 部分 | 必須 | 説明 |
|---|---|---|
| `<Command/Path>` | ✅ | `liminal commands` で発見した path (大文字小文字区別) |
| `name=value` | (引数があれば) | 引数。**全 value は文字列として送られる** |

### 大原則

1. **引数は `name=value` 形式**: `value=100` / `enabled=true` / `pos=1,2,3` / `type=Goblin`
2. **`value` 側にはクォートなど不要**: シェルが空白を含む値を渡すなら `'pos=1, 2, 3'` のように shell quote
3. **path は完全一致**: 大文字小文字、スラッシュの数まで完全一致

---

## 基本パターン

### int 引数 1 つ

```bash
liminal exec Player/Health/Set value=100
```

### 引数 0 個

```bash
liminal exec Editor/Console/Clear
```

### Vector3 (カンマ区切り)

```bash
liminal exec Player/Position/Teleport pos=1,2,3
```

### bool / enum / Color の例は [examples/basic.md](examples/basic.md) を参照

複合パターン (async, retry, large args, multi-call) は [examples/advanced.md](examples/advanced.md)。

---

## Output (人間向け)

```
success  (1.07 ms)
  value : (2.00, 4.00, 6.00)

  logs (1):
    Log: [Echo] Hi
```

失敗時:

```
failed  (0.5 ms)
  error : 引数 amount は必須
  type  : System.ArgumentException
  ...stack trace...
```

exit code は **0=成功 / 2=`success:false` / 1=通信エラー** なので、シェルから条件分岐できる:

```bash
if liminal exec Player/Health/Set value=100; then
  echo "OK"
else
  echo "失敗 (rc=$?)"
fi
```

---

## Output (`--json`)

`--json` を付けると `/api/v1/execute` のレスポンスがそのまま返る:

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
RESP=$(liminal exec Math/Add a=3 b=4 --json)

echo "$RESP" | jq '{success, value, ms: .durationMs}'

# 失敗時の詳細
if [ "$(echo "$RESP" | jq -r '.success')" = "false" ]; then
  echo "$RESP" | jq '{error, exceptionType, stackTrace}'
fi
```

---

## 型変換のクセ (要約)

各型の受理フォーマット早見:

| 型 | 形式 | 例 |
|---|---|---|
| `int` / `long` / `float` / `double` | 数値リテラル | `value=42` / `value=3.14` |
| `bool` | `true` / `false` (大小無視) | `enabled=true` |
| `string` | そのまま | `name=hello` |
| `Vector2/3/4` | カンマ区切り | `pos=1,2,3` / `'pos=(1, 2, 3)'` / `'pos=[1 2 3]'` |
| `Vector2Int/3Int` | 同上、整数 | `pos=10,20,30` |
| `Color` (HEX) | `#RRGGBB` / `#RRGGBBAA` | `color=#FF8800` |
| `Color` (数値 0..1) | `r,g,b[,a]` | `'color=1.0, 0.53, 0.0'` |
| `Color32` (数値 0..255) | `r,g,b[,a]` | `'color=255, 136, 0, 255'` |
| Enum | 名前 (大小無視) または数値 | `dir=Up` / `dir=0` |
| `[Flags]` Enum | カンマ区切り名前 | `perm=Read,Write` |
| `UnityEngine.Object` | `@<entityID>` または `GameObject:<name>` | HTTP 経由はサポート限定的 |

詳細 (各 Converter の挙動 / fallback / 失敗時のメッセージ等) は [references/type-conversion.md](references/type-conversion.md)。

---

## エラー対処 (要約)

| 症状 | 状況 | 一次対処 |
|---|---|---|
| 終了コード 2 + `failed` | 実行例外 / 引数バインド失敗 | 出力の `error` + `type` を読む |
| HTTP 400 | 引数フォーマット異常 | `liminal commands --json | jq '.commands[] | select(.path==...)'` でスキーマ確認 |
| HTTP 401 | Token 不一致 | `~/.liminal-palette/token` を再生成 (Editor 再起動) |
| HTTP 404 | path 未登録 | `liminal commands --filter <prefix>` で綴り確認 |
| HTTP 413 | body 1 MB 超過 | ファイルパス渡しに切り替え |
| HTTP 429 | rate limit (30 req/s) | 間隔を空ける、または `liminal run --steps -` でまとめる |

詳細フローチャートと各エラーの根本原因は [references/error-handling.md](references/error-handling.md)。

---

## Notes

### Async コマンド

`isAsync: true` のコマンドは Task 完了まで HTTP レスポンスがブロックされる。`durationMs` がそのまま実時間。`liminal` のタイムアウトは 10 秒なので、長時間 async は調整が必要 (現状は CLI 側のソース修正が必要)。

### `result.logs` の使いどころ

実行中の `Debug.Log*` だけが切り取られて返る。AI Agent が「コマンドが何をしたか」を再現可能性付きで読める。Unity Console 全体ではない (それは uloop-get-logs)。

### Production ビルドでは動かない

LP の HTTP サーバ自体が asmdef defineConstraints で Production 除外。Production の APK / 実行ファイルに `liminal` を向けても応答しない。Development build か Editor のみ。

### レートリミットの枠は scenarios と共有

`liminal exec` と `liminal run` は **30 req/s 共有**。連投する場合は `liminal run --steps -` で 1 リクエストにまとめる方が効率的。

---

## See also

- `/liminal-list-commands` — path と引数スキーマの発見
- `/liminal-get-state` — 実行後のゲーム状態を検証
- `/liminal-get-logs` — invocation 履歴 (本スキルの実行も記録される)
- `/liminal-run-scenario` — 複数 execute + 検証を 1 リクエストにまとめる
- references:
  - [type-conversion.md](references/type-conversion.md) — 各型の完全な変換仕様
  - [error-handling.md](references/error-handling.md) — エラー status 別の根本原因と対処フロー
- examples:
  - [basic.md](examples/basic.md) — primitive / Vector / Color / enum の基本例
  - [advanced.md](examples/advanced.md) — async, retry, jq パイプ, 連続実行
