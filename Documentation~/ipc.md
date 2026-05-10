# IPC / HTTP API

LiminalPalette のコマンドを HTTP 経由で叩くためのリファレンス。

---

## 概要

- **localhost のみ** バインド (`127.0.0.1` と `localhost`)。LAN への露出はゼロ。
- **Bearer トークン認証**。`/health` 以外のすべての endpoint で必須。
- **既定ポート 7610**。占有時は隣接 (`7611`, `7612`, ...) に最大 5 回リトライ。
- **JSON 応答**。サードパーティ依存なしの自前 `JsonWriter` で組み立て。
- **メインスレッドで実行**。HTTP のリクエスト処理はワーカースレッドだが、コマンド実行は `MainThreadDispatcher` でメインスレッドへ marshal される。

## 起動条件

| 環境 | サーバー起動 | ポート |
|---|---|---|
| Editor (常時) | ✅ | 7610 |
| Play Mode (Editor の) | ✅ | 7611 (Editor が 7610 を占有しているため隣接) |
| Standalone Development build | ✅ | 7610 |
| Standalone Production build | ❌ | (asmdef defineConstraints で **コンパイル除外**) |

詳細: [security.md](security.md)

---

## 認証

### トークンの場所

初回起動時に自動生成:

| OS | パス |
|---|---|
| macOS / Linux | `~/.liminal-palette/token` |
| Windows | `%USERPROFILE%\.liminal-palette\token` |

中身は **256 bit ランダム** を base64 エンコードした文字列 (改行混入は読み込み時に Trim)。

### 権限

- **macOS / Linux**: 生成時に `chmod 600` を best-effort で適用
- **Windows**: ユーザープロファイル配下なので NTFS の ACL に任せる

### トークンの取り扱い

```bash
# 環境変数に読み込んで使う
export LP_TOKEN=$(cat ~/.liminal-palette/token)

# curl で送る
curl -H "Authorization: Bearer $LP_TOKEN" ...
```

⚠️ **トークンを誰かに見せない** (Discord 等にコピペ NG)。漏れた場合は `~/.liminal-palette/token` を削除すれば次回 Editor 起動時に再生成される。

---

## エンドポイント一覧

### `GET /api/v1/health` (認証不要)

サーバーの生存確認。AI Agent / 監視スクリプトはこれで起動済みポートをスキャンする。

**Response 200**:
```json
{
  "status": "ok",
  "version": "0.4.0",
  "commandCount": 356
}
```

### `GET /api/v1/commands` (認証必須)

登録済みコマンドのスキーマ一覧。

**Response 200**:
```json
{
  "commands": [
    {
      "path": "Player/Health/Set",
      "name": "Set",
      "category": "Player/Health",
      "description": "プレイヤーの HP を設定",
      "isAsync": false,
      "returnType": "Void",
      "aliases": [],
      "parameters": [
        {
          "name": "value",
          "type": "Int32",
          "position": 0,
          "hasDefault": false,
          "default": null,
          "description": "",
          "choices": []
        }
      ]
    },
    ...
  ]
}
```

`isAsync` は `Task<T>` / `ValueTask<T>` 戻り値の場合に `true`。`returnType` は `Type.Name` (短縮名)。

### `POST /api/v1/execute` (認証必須)

コマンドを実行する。文字列引数のみサポート (HTTP では typed args 経路は使わない)。

**Request body**:
```json
{
  "path": "Player/Health/Set",
  "args": {
    "value": "100"
  }
}
```

`args` の値は **すべて string で送る**。サーバー側の `TypeConverterRegistry` が文字列 → 型解決済み値に変換する。数値リテラル (`"value": 100`) や `true`/`false` でも受け入れるが、内部で文字列化される。

**Response 200**:
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
| `success` | 実行成功なら true。引数バインドエラー / 例外ともに false |
| `value` | 戻り値の `ToDisplayString` 文字列化。`void` / `Task` / 失敗時は null |
| `error` | 失敗時のエラーメッセージ |
| `exceptionType` | 例外起因の失敗時に `System.InvalidOperationException` 等の FullName |
| `stackTrace` | 例外のスタックトレース (デバッグ用) |
| `durationMs` | 実行所要時間 (ミリ秒) |
| `logs` | コマンド実行中に取り込まれた `Debug.Log*` の配列 |

⚠️ `Exception` オブジェクト自体は **JSON に含まれない** (プロセス境界を超える object を送らない原則)。代わりに型名と stack trace を string で出す。

### `GET /api/v1/logs` (認証必須)

コマンド実行履歴を新しい順で返す。

**Query parameters**:
- `?limit=N`: 件数制限 (既定 50、上限 `InvocationStore.Capacity = 200`)

**Response 200**:
```json
{
  "invocations": [
    {
      "path": "Test/Vector",
      "timestamp": "2026-04-30T12:34:56.789Z",
      "args": {
        "v": "(1, 2, 3)"
      },
      "result": {
        "success": true,
        "value": "(2.00, 4.00, 6.00)",
        "durationMs": 1.07,
        ...
      }
    },
    ...
  ],
  "total": 12,
  "limit": 50
}
```

`invocations[].result` は `/execute` のレスポンスと同じスキーマ。

### `GET /api/v1/state` (認証必須)

`[ConsoleObservableField]` で公開された読み取り専用状態のスナップショット。`?path=` 指定で単一フィールド、未指定で全件。

**Request (単一)**:
```
GET /api/v1/state?path=Player/Health
```

**Response 200 (単一)**:
```json
{
  "path": "Player/Health",
  "value": "75",
  "type": "Int32"
}
```

**Request (全件)**:
```
GET /api/v1/state
```

**Response 200 (全件)**:
```json
{
  "fields": [
    { "path": "Player/Health", "value": "75", "type": "Int32", "instanceResolved": true },
    { "path": "Player/Mana",   "value": "30", "type": "Int32", "instanceResolved": true },
    { "path": "Enemy/Count",   "value": null, "type": "Int32", "instanceResolved": false }
  ]
}
```

| フィールド | 説明 |
|---|---|
| `value` | `ReactiveProperty.Value` を `TypeConverterRegistry.ToDisplayString` で文字列化。次のいずれかでは JSON `null` を返す: (1) インスタンス未解決、(2) `Observable<T>` 単体 (現在値を保持しないため)、(3) `ReactiveProperty.Value` が null |
| `type` | T の Type.Name |
| `instanceResolved` | VContainer でインスタンス解決できたかどうか。false なら `value` は必ず `null` |

**エラー**:
- `?path=` 指定 + 未登録 path → 404 Not Found
- インスタンス未解決 → 500 Internal Server Error (利用者向けの対処メッセージ付き)

### `GET /api/v1/scenarios` (認証必須)

`[ConsoleScenario]` で登録された全シナリオの一覧。

**Response 200**:
```json
{
  "scenarios": [
    { "path": "Combat/EnemyTakesDamage", "description": "...", "stepCount": 5 }
  ]
}
```

`stepCount` が `-1` の場合はインスタンス未解決等で計測不能。

### `POST /api/v1/scenarios/run` (認証必須)

シナリオを実行する。`path` (名前指定) と `steps` (ad-hoc) は**排他**。

**Request (名前指定)**:
```json
{"path": "Combat/EnemyTakesDamage"}
```

**Request (ad-hoc)**:
```json
{
  "steps": [
    {"type": "command", "path": "Enemy/Spawn", "args": {"type": "Goblin"}},
    {"type": "wait_frames", "frames": 1},
    {"type": "assert_equals", "path": "Enemy/Hp", "expected": "100"}
  ]
}
```

**ステップ JSON のフィールド**:

| `type` | 必須フィールド | オプション |
|---|---|---|
| `command` | `path` (string), `args` (object) | `description` |
| `wait_seconds` | `seconds` (number) | `description` |
| `wait_frames` | `frames` (integer) | `description` |
| `assert_equals` | `path` (string), `expected` (string\|number\|bool\|null) | `description` |
| `assert_not_equals` | `path` (string), `expected` | `description` |

**Response 200**:
```json
{
  "success": false,
  "durationMs": 124.3,
  "failedAtStep": 4,
  "path": "Combat/EnemyTakesDamage",
  "alreadyRunning": false,
  "steps": [
    {"kind": "Command", "success": true, "durationMs": 1.2, "commandPath": "Enemy/Spawn", "args": {...}, "commandResult": {...}},
    {"kind": "AssertEquals", "success": false, "durationMs": 0.1, "actualValue": "65", "error": "expected '70' but got '65'"}
  ]
}
```

| フィールド | 説明 |
|---|---|
| `success` | 全ステップ Pass で true |
| `durationMs` | シナリオ全体の所要時間 |
| `failedAtStep` | 最初に失敗したステップの index、無ければ -1 |
| `path` | 名前指定実行のシナリオ Path、ad-hoc は null |
| `alreadyRunning` | 他のシナリオが実行中で弾かれたかどうか |
| `steps` | 実行された分のみ (fail-fast 後は途中まで)。各ステップは `kind` で形が変わる |

**ステータスコード**:
- `200 OK` — 通常実行 (success / failure 両方)
- `400 BadRequest` — body 文法エラー / `path` と `steps` の同時指定 / 未知の `type` 等
- `409 Conflict` — `alreadyRunning: true` (他のシナリオが実行中)
- `429 Too Many Requests` — レートリミット (`/execute` と共通の制限)

**レートリミット**: `IpcSettings.ExecuteRateLimitPerSecond` の枠を `/execute` と共有。

詳細: [scenarios.md](scenarios.md)

### curl 例

```bash
TOKEN=$(cat ~/.liminal-palette/token)

# 単一
curl -s -H "Authorization: Bearer $TOKEN" \
     "http://127.0.0.1:7610/api/v1/state?path=Player/Health"

# 全件 → AI Agent が「現在の状態を観測してから次のコマンドを決める」用途
curl -s -H "Authorization: Bearer $TOKEN" \
     "http://127.0.0.1:7610/api/v1/state" | jq '.fields[] | select(.value != null)'
```

詳細は [commands.md](commands.md) の `[ConsoleObservableField]` 章。

---

## エラーステータスコード

| Status | 状況 | レスポンス body |
|---|---|---|
| `400 Bad Request` | JSON パース失敗 / 必須フィールド欠落 / `path` が空 | `{"error":"<reason>"}` |
| `401 Unauthorized` | トークン欠落 / 不一致 / `Bearer ` プレフィックス無し | `{"error":"Unauthorized"}` |
| `404 Not Found` | 未登録のルート | `{"error":"No route for ..."}` |
| `405 Method Not Allowed` | パスは存在するが method 違い | `{"error":"Method ... not allowed for ..."}` |
| `413 Payload Too Large` | body サイズが `MaxRequestBodyBytes` (既定 1 MB) を超過 | `{"error":"Body exceeds limit ..."}` |
| `429 Too Many Requests` | `/execute` でレートリミット超過 (既定 30 req/s) | `{"error":"Rate limit exceeded ..."}` |
| `500 Internal Server Error` | endpoint 内で想定外の例外 | `{"error":"<exception message>"}` |

---

## curl 例

```bash
# トークン取得
TOKEN=$(cat ~/.liminal-palette/token)
BASE=http://127.0.0.1:7610
H="Authorization: Bearer $TOKEN"

# Health
curl -s $BASE/api/v1/health
# → {"status":"ok","version":"0.4.0","commandCount":356}

# コマンド一覧 (Player/ 配下だけ)
curl -s -H "$H" $BASE/api/v1/commands | jq '.commands[] | select(.path | startswith("Player/"))'

# シンプルな実行
curl -s -H "$H" -H "Content-Type: application/json" \
     -X POST $BASE/api/v1/execute \
     -d '{"path": "Player/Health/Set", "args": {"value": "100"}}'

# 引数 0 個のコマンド
curl -s -H "$H" -H "Content-Type: application/json" \
     -X POST $BASE/api/v1/execute \
     -d '{"path": "Editor/Console/Clear", "args": {}}'

# Vector3 引数 (文字列で "x,y,z")
curl -s -H "$H" -H "Content-Type: application/json" \
     -X POST $BASE/api/v1/execute \
     -d '{"path": "Test/Vector", "args": {"v": "1,2,3"}}'

# 履歴 10 件
curl -s -H "$H" "$BASE/api/v1/logs?limit=10" | jq '.invocations[].path'
```

---

## AI Agent との連携 (Claude Code / 自作 Discord bot 等)

### 基本パターン

1. **発見**: `GET /commands` で利用可能コマンドを取得。`description` と `parameters` を読んで AI が引数を組み立てる
2. **実行**: `POST /execute` でコマンド呼び出し
3. **観察**: `result.logs` と `result.value` で結果を確認
4. **再試行**: 失敗時は `error` / `stackTrace` を読んで AI が訂正リクエストを送る

### 推奨プロンプト形 (Claude Code 等向け)

```
あなたは Unity プロジェクトを操作するエージェントです。
利用可能コマンドは下記の JSON で発見できます:
  curl -H "Authorization: Bearer $TOKEN" http://127.0.0.1:7610/api/v1/commands

実行は:
  curl -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
       -X POST http://127.0.0.1:7610/api/v1/execute \
       -d '{"path": "...", "args": {...}}'

引数の値はすべて string で送ること (数値や bool もクォート)。
エラー時は result.error と result.stackTrace を読んでリトライ。
```

### Discord bot サンプル (擬似コード)

```python
# Discord bot から /unity exec 'Player/Health/Set 100'
@bot.slash_command()
async def unity_exec(ctx, command_string: str):
    parts = command_string.split(" ", 1)
    path = parts[0]
    arg_value = parts[1] if len(parts) > 1 else ""
    response = await http.post(
        "http://127.0.0.1:7610/api/v1/execute",
        headers={"Authorization": f"Bearer {LP_TOKEN}"},
        json={"path": path, "args": {"value": arg_value}})
    result = response.json()
    await ctx.respond(f"```\n{result}\n```")
```

---

## ポート競合

Editor + Play Mode 同時起動の場合、Editor が `7610` を取った後で Runtime は `7611` にずれる:

```bash
# Editor
curl -s http://127.0.0.1:7610/api/v1/health

# Runtime (Editor が 7610 を持っているため、Play Mode で隣接ポート)
curl -s http://127.0.0.1:7611/api/v1/health
```

AI Agent 側は `/health` でスキャンする運用:

```bash
for port in 7610 7611 7612 7613 7614 7615; do
  if curl -s -m 1 http://127.0.0.1:$port/api/v1/health > /dev/null 2>&1; then
    echo "Found at port $port"
    break
  fi
done
```

---

## InvocationStore との連携

HTTP `/execute` で実行されたコマンドも `InvocationStore` に記録される (UI 経路と同じ流儀):

- パレットの **Log タブ** に表示される
- パレットの **History タブ** から再実行できる
- `/api/v1/logs` でも取得できる

つまり curl で叩いたコマンドが、Editor の UI でも履歴として確認可能。

---

## レートリミット

`POST /api/v1/execute` のみ対象 (`IpcSettings.ExecuteRateLimitPerSecond`、既定 30 req/s):

- 1 秒のスライディングウィンドウで判定
- 超過すると 429 を返す
- 他の endpoint (`/health` / `/commands` / `/logs`) はリミットなし

利用側で変更する場合:

```csharp
[InitializeOnLoadMethod]
static void TweakIpcLimits()
{
    Void2610.LiminalPalette.Ipc.IpcSettings.ExecuteRateLimitPerSecond = 100;
}
```

---

## body サイズ上限

`IpcSettings.MaxRequestBodyBytes` (既定 1 MB)。超過した時点でストリーム読みを中断して 413 を返す (DoS 対策)。

大きい引数を送りたい場合:
- 利用側で上限を増やす (`IpcSettings.MaxRequestBodyBytes = 4 * 1024 * 1024`)
- またはコマンド側で「ファイルパスを引数で受け取って中身を読む」設計に変える

---

## 専用 CLI

`Tools~/lp/lp` に Python 3 標準ライブラリ製のシングルファイル CLI を同梱。
`chmod +x` するか PATH に symlink すれば `lp health` / `lp exec` / `lp logs` 等が使える。

```bash
ln -s "$(pwd)/Tools~/lp/lp" ~/.local/bin/lp
lp health
lp exec Player/HP/Heal amount=10
lp logs --limit 10 --json | jq '.invocations[].path'
```

詳細は [Tools~/lp/README.md](../Tools~/lp/README.md)。

## 拡張ポイント (将来検討)

- WebSocket / SSE によるストリーミング (長時間 async コマンドのプログレス通知)
- HTTPS / TLS (LAN 経由のリモート用)
- 動的コマンド登録 API (`POST /api/v1/commands`、任意コード実行リスクのため要慎重)
- キャンセルトークンの cancel エンドポイント

---

## 関連ドキュメント

- [security.md](security.md) — トークン管理 / Production 除外 / 攻撃面の評価
- [troubleshooting.md](troubleshooting.md) — 401 が返る / ポート占有 / DomainReload で listener が残る等
- [asmdef.md](asmdef.md) — `Player.Ipc` の `defineConstraints` 設計
