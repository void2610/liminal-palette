# LP `/execute` Error Handling — 完全リファレンス

`lp-execute` の失敗パターンを HTTP status code と response body の組み合わせで分類し、根本原因とリカバリ手順を示す。

---

## 分類フロー

```
HTTP status を見る
├── 200 → body の "success" を見る
│        ├── true  → 成功
│        └── false → 「アプリケーション層の失敗」(下記 §1)
├── 400 → 「リクエスト構文エラー」(下記 §2)
├── 401 → 「認証エラー」(下記 §3)
├── 404 → 「path 未登録」(下記 §4)
├── 405 → 「method 違い」(下記 §5)
├── 413 → 「body サイズ超過」(下記 §6)
├── 429 → 「レートリミット」(下記 §7)
└── 500 → 「サーバ内部例外」(下記 §8)
```

---

## §1. 200 OK + `success: false`

正しくリクエストが届き処理されたが、コマンド実行が失敗したケース。最も頻出。

### 1a. 引数バインド失敗 (exceptionType: null)

```json
{
  "success": false,
  "error": "Cannot parse '1,2' as Vector3 (expected 3 components, got 2)",
  "exceptionType": null,
  "value": null
}
```

**原因**: 型変換段階で失敗。args の値が想定形式と違う。

**対処**:
1. `lp-list-commands` で対象 path のスキーマを確認
2. `parameters[].type` を見て [type-conversion.md](type-conversion.md) で valid 形式を調べる
3. args を修正して再実行

### 1b. 必須引数の欠落

```json
{
  "success": false,
  "error": "Required parameter 'value' is missing",
  "exceptionType": null
}
```

**原因**: args の key 名が parameters[].name と一致していない (typo / 大文字小文字違い)、または key 自体が無い。

**対処**: `lp-list-commands` で正確な name を確認。

### 1c. choices 制約違反

```json
{
  "success": false,
  "error": "'Slime' is not a valid choice for parameter 'type'",
  "exceptionType": null
}
```

**原因**: enum / `[Choices]` で許可された値以外を送った。

**対処**: `parameters[].choices` 配列の値から選ぶ。

### 1d. コマンド実行中の例外 (exceptionType: 非 null)

```json
{
  "success": false,
  "error": "Object reference not set to an instance of an object",
  "exceptionType": "System.NullReferenceException",
  "stackTrace": "at MyGame.Player.SetHealth(Int32 value) at ...",
  "value": null,
  "durationMs": 2.5
}
```

**原因**: コマンド本体 (利用側 C# コード) が例外を投げた。

**対処**:
1. `stackTrace` を読んで例外発生箇所を特定
2. 多くは利用側コードのバグ → ユーザに報告
3. 環境依存 (例: Player が未生成) なら前提条件を整えて再実行

### 1e. インスタンス未解決

```json
{
  "success": false,
  "error": "Failed to resolve instance of MyGame.Player from VContainer",
  "exceptionType": "System.InvalidOperationException"
}
```

**原因**: インスタンスメソッドの `[ConsoleCommand]` だが、利用側で VContainer 登録が抜けている。

**対処**: 利用側で:

```csharp
builder.RegisterComponentInHierarchy<Player>();
builder.RegisterEntryPoint<LiminalPaletteEntryPoint>();
```

---

## §2. 400 Bad Request

### 2a. JSON 文法エラー

```json
{"error": "Invalid JSON: Unexpected token..."}
```

**原因**: request body の JSON が壊れている (クォート抜け / カンマ過剰 / 末尾改行欠落等)。

**対処**: curl の `-d` の中身を確認。bash の単引用 / 二重引用 / エスケープを点検:

```bash
# OK (single quote で JSON 全体を囲む)
-d '{"path":"X","args":{}}'

# NG (double quote の中で " をエスケープし忘れ)
-d "{"path":"X","args":{}}"
```

長い JSON は HEREDOC が安全:

```bash
curl ... -d @- <<'EOF'
{"path": "X", "args": {"key": "value"}}
EOF
```

### 2b. 必須フィールド欠落

```json
{"error": "Missing required field 'path'"}
{"error": "Missing required field 'args'"}
```

**対処**: `path` と `args` 両方を必ず付ける。引数 0 個でも `"args": {}` 必須。

### 2c. path が空文字

```json
{"error": "path must be non-empty"}
```

---

## §3. 401 Unauthorized

```json
{"error": "Unauthorized"}
```

### 原因と対処

| 原因 | 対処 |
|---|---|
| `Authorization` ヘッダ自体が無い | `-H "Authorization: Bearer $LP_TOKEN"` を付ける |
| `Bearer ` (末尾スペース) が抜けている | `Bearer<TOKEN>` でなく `Bearer <TOKEN>` (1 半角スペース必須) |
| `$LP_TOKEN` が空 | `echo "$LP_TOKEN"` で確認 |
| トークンが古い (Editor 再起動でローテートされた) | `export LP_TOKEN=$(cat ~/.liminal-palette/token)` で再読み込み |

詳細: `/lp-overview` の references/auth.md。

---

## §4. 404 Not Found

```json
{"error": "No route for /api/v1/execute"}
{"error": "Command not found: Player/HelthSet"}
```

### 4a. URL が違う

`/api/v1/execute` を `/api/execute` のようにバージョン抜きで叩いている。

### 4b. path が未登録 (typo)

```json
{"error": "Command not found: Player/HelthSet"}
```

LP は path の **大文字小文字を区別する**。`Player/HelthSet` (typo) と `Player/Health/Set` は別物。

**対処**: `lp-list-commands` で正確な path を確認。fuzzy 検索を使う:

```bash
curl -s -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands" \
  | jq '.commands[] | select((.path|ascii_downcase) | contains("health"))'
```

### 4c. Editor / Runtime のポート違いで未登録扱い

Editor 限定コマンド (`Editor/Console/Clear` 等) を Runtime ポート (7611) に送ると 404。逆も然り。

**対処**: `lp-find-port` で両方のポートを発見し、適切な base URL を使う。

---

## §5. 405 Method Not Allowed

```json
{"error": "Method GET not allowed for /api/v1/execute"}
```

**原因**: `/execute` は POST。`-X POST` 抜け、または curl のデフォルト GET で叩いた。

**対処**: `-X POST -H "Content-Type: application/json"` を付ける。

---

## §6. 413 Payload Too Large

```json
{"error": "Body exceeds limit (1048576 bytes)"}
```

**原因**: request body が `IpcSettings.MaxRequestBodyBytes` (既定 1 MB) を超えた。長文 JSON を args に詰めている時に発生。

### 対処オプション

#### A. ファイルパス渡しに切り替え (推奨)

```csharp
// 利用側
[ConsoleCommand("Data/Import")]
public void Import(string filePath) {
    var json = File.ReadAllText(filePath);
    // ...
}
```

```bash
# AI Agent 側
echo "$BIG_JSON" > /tmp/payload.json
curl ... -d '{"path":"Data/Import","args":{"filePath":"/tmp/payload.json"}}'
```

#### B. 上限を上げる (利用側で設定)

```csharp
[InitializeOnLoadMethod]
static void EnlargeBody() {
    Void2610.LiminalPalette.Ipc.IpcSettings.MaxRequestBodyBytes = 4 * 1024 * 1024;
}
```

メモリ DoS のリスクが上がるので慎重に。

---

## §7. 429 Too Many Requests

```json
{"error": "Rate limit exceeded (30 req/s)"}
```

**原因**: 1 秒スライディングウィンドウで 30 req を超過。`/execute` と `/scenarios/run` で **共有**。

### 対処

#### A. 間隔を空ける

```bash
for cmd in path1 path2 path3 ...; do
  curl ... -d "..."
  sleep 0.05  # 1秒/30req = 33ms 以上空ける
done
```

#### B. 1 リクエストにまとめる (推奨)

`lp-run-scenario` の ad-hoc に複数 command ステップを並べると 1 リクエストで完結 → リミット消費 1:

```json
{
  "steps": [
    {"type":"command","path":"path1","args":{}},
    {"type":"command","path":"path2","args":{}},
    {"type":"command","path":"path3","args":{}}
  ]
}
```

#### C. リミットを上げる (利用側で設定)

```csharp
[InitializeOnLoadMethod]
static void TweakRateLimit() {
    Void2610.LiminalPalette.Ipc.IpcSettings.ExecuteRateLimitPerSecond = 100;
}
```

---

## §8. 500 Internal Server Error

```json
{"error": "<exception message>"}
```

**原因**: LP の endpoint 処理内部で想定外の例外。コマンド実行中の例外は §1d に分類されるので、ここに落ちるのは LP 自体のバグか深刻な環境問題。

### 対処

1. response の `error` 本文を読む
2. Editor Console を確認 (LP がスタックトレースを出している可能性)
3. LP の GitHub Issue で報告 (再現手順付き)

---

## connection refused / timeout

HTTP status が返らない場合:

| 状況 | 原因 | 対処 |
|---|---|---|
| `curl: (7) Failed to connect` | LP が listener を立てていない | `lp-find-port` でポート再確認 / Editor 起動確認 |
| `curl: (28) Operation timed out` | サーバが応答しない (Domain Reload 中 / メインスレッド詰まり) | `--max-time` を上げて再試行 |

---

## AI Agent 向けリトライ戦略

```
リクエスト送信
├── HTTP 200
│   ├── success: true → 完了
│   └── success: false
│       ├── exceptionType: null → 引数を修正して 1 回リトライ
│       └── exceptionType 非 null → ユーザに報告 (利用側コードのバグの可能性)
├── HTTP 401 → token 再読み込みして 1 回リトライ
├── HTTP 404 → lp-list-commands で path 確認 → ユーザに報告
├── HTTP 429 → sleep 1s してリトライ
├── HTTP 5xx → 1 回リトライ、それでも失敗ならユーザに報告
└── connection error → lp-find-port → リトライ
```

無限ループは避ける。**同じエラーで 2 回失敗したら停止してユーザに状況を報告**するのが定石。
