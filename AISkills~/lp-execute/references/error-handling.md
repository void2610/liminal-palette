# LP `/execute` Error Handling — 完全リファレンス

`lp exec` の失敗パターンを HTTP status code と response body の組み合わせで分類し、根本原因とリカバリ手順を示す。

`lp` の exit code は **0=成功 / 2=`success:false` (アプリケーション層失敗) / 1=通信・HTTP エラー (4xx/5xx)**。`--json` を付けるとサーバの生レスポンスがそのまま読める。

---

## 分類フロー

```
HTTP status を見る
├── 200 → body の "success" を見る
│        ├── true  → 成功 (lp exit 0)
│        └── false → 「アプリケーション層の失敗」(下記 §1) (lp exit 2)
├── 400 → 「リクエスト構文エラー」(下記 §2) (lp exit 1)
├── 401 → 「認証エラー」(下記 §3) (lp exit 1)
├── 404 → 「path 未登録」(下記 §4) (lp exit 1)
├── 405 → 「method 違い」(下記 §5) (lp exit 1)
├── 413 → 「body サイズ超過」(下記 §6) (lp exit 1)
├── 429 → 「レートリミット」(下記 §7) (lp exit 1)
└── 500 → 「サーバ内部例外」(下記 §8) (lp exit 1)
```

---

## §1. 200 OK + `success: false`

正しくリクエストが届き処理されたが、コマンド実行が失敗したケース。最も頻出。`lp exec` は装飾出力で `failed` + `error:` 行を出し、exit code 2 で終わる。

### 1a. 引数バインド失敗 (exceptionType: null)

```json
{
  "success": false,
  "error": "Cannot parse '1,2' as Vector3 (expected 3 components, got 2)",
  "exceptionType": null,
  "value": null
}
```

**原因**: 型変換段階で失敗。値が想定形式と違う。

**対処**:
1. `lp commands --json | jq '.commands[] | select(.path == "<path>")'` でスキーマ確認
2. `parameters[].type` を見て [type-conversion.md](type-conversion.md) で valid 形式を調べる
3. `name=value` を修正して再実行

### 1b. 必須引数の欠落

```json
{
  "success": false,
  "error": "Required parameter 'value' is missing",
  "exceptionType": null
}
```

**原因**: `name=value` の name が parameters[].name と一致していない (typo / 大文字小文字違い)、または name 自体が無い。

**対処**: `lp commands --filter <prefix>` で正確な name を確認。

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
1. `stackTrace` を読んで例外発生箇所を特定 (`lp exec ... --json | jq -r .stackTrace`)
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

**原因**: インスタンスメソッドの `[LiminalCommand]` だが、利用側で VContainer 登録が抜けている。

**対処**: 利用側で:

```csharp
builder.RegisterComponentInHierarchy<Player>();
builder.RegisterEntryPoint<LiminalPaletteEntryPoint>();
```

---

## §2. 400 Bad Request

`lp exec` は内部で正しい JSON を組み立てるので、通常はここに落ちない。`lp run --steps -` で手書きした JSON が壊れている時に発生する。

### 2a. JSON 文法エラー (`lp run --steps -`)

```json
{"error": "Invalid JSON: Unexpected token..."}
```

**原因**: `--steps` で渡した JSON が壊れている (クォート抜け / カンマ過剰 / 末尾改行欠落等)。

**対処**: stdin / ファイルの中身を `jq .` で先に検証:

```bash
cat my-steps.json | jq .   # → 構文エラーなら jq が指摘してくれる
cat my-steps.json | lp run --steps -
```

### 2b. 必須フィールド欠落

```json
{"error": "Missing required field 'path'"}
{"error": "Missing required field 'args'"}
```

`lp exec` 経由では起きない。直接 API を叩いている場合は `path` と `args` 両方を必ず付ける (引数 0 個でも `"args": {}` 必須)。

### 2c. path が空文字

```json
{"error": "path must be non-empty"}
```

---

## §3. 401 Unauthorized

```json
{"error": "Unauthorized"}
```

`lp` は token を自動で読むので通常起きないが、起きるケース:

| 原因 | 対処 |
|---|---|
| `~/.liminal-palette/token` が空または存在しない | Editor を再起動して再生成 |
| `$LP_TOKEN` が古い値で残っている | `unset LP_TOKEN` でファイル読込みに戻す |
| `--token` で間違った値を渡した | フラグを外して自動読込みに切り替え |

詳細: `/lp-overview` の references/auth.md。

---

## §4. 404 Not Found

```json
{"error": "No route for /api/v1/execute"}
{"error": "Command not found: Player/HelthSet"}
```

### 4a. URL が違う (基本起きない)

`lp` は内部で `/api/v1/execute` を組み立てるので 404 のうち URL 系は通常出ない。`--base-url` で間違った値を渡した時のみ。

### 4b. path が未登録 (typo)

```json
{"error": "Command not found: Player/HelthSet"}
```

LP は path の **大文字小文字を区別する**。`Player/HelthSet` (typo) と `Player/Health/Set` は別物。

**対処**: `lp commands --json | jq '.commands[] | select((.path|ascii_downcase) | contains("health"))'` でゆるい検索。

### 4c. Editor / Runtime のポート違いで未登録扱い

Editor 限定コマンド (`Editor/Console/Clear` 等) を Runtime ポート (7611) に送ると 404。逆も然り。

**対処**: `lp --port 7610 health` / `lp --port 7611 health` で両方確認し、適切なポートを `--port` で指定。

---

## §5. 405 Method Not Allowed

```json
{"error": "Method GET not allowed for /api/v1/execute"}
```

`lp` は内部で正しい method を選ぶので通常起きない。`--base-url` で別 endpoint を指している時のみ。

---

## §6. 413 Payload Too Large

```json
{"error": "Body exceeds limit (1048576 bytes)"}
```

**原因**: request body が `IpcSettings.MaxRequestBodyBytes` (既定 1 MB) を超えた。長文の値を `name=value` で渡している時に発生。

### 対処オプション

#### A. ファイルパス渡しに切り替え (推奨)

```csharp
// 利用側
[LiminalCommand("Data/Import")]
public void Import(string filePath) {
    var json = File.ReadAllText(filePath);
    // ...
}
```

```bash
# AI Agent 側
echo "$BIG_JSON" > /tmp/payload.json
lp exec Data/Import filePath=/tmp/payload.json
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
  lp exec "$cmd"
  sleep 0.05  # 1秒/30req = 33ms 以上空ける
done
```

#### B. 1 リクエストにまとめる (推奨)

`lp run --steps -` に複数 command ステップを並べると 1 リクエストで完結 → リミット消費 1:

```bash
cat <<'EOF' | lp run --steps -
[
  {"type":"command","path":"path1","args":{}},
  {"type":"command","path":"path2","args":{}},
  {"type":"command","path":"path3","args":{}}
]
EOF
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

1. `lp exec ... --json` の `error` 本文を読む
2. Editor Console を確認 (LP がスタックトレースを出している可能性)
3. LP の GitHub Issue で報告 (再現手順付き)

---

## connection refused / timeout

`lp` 側の出力:

| 状況 | 原因 | 対処 |
|---|---|---|
| `Liminal Palette サーバーが見つかりません` | LP が listener を立てていない | Editor 起動確認 / `lp health` で再スキャン |
| `urlopen error: timed out` | サーバが応答しない (Domain Reload 中 / メインスレッド詰まり) | 数秒待って再実行 |

`lp` の HTTP タイムアウトは 10 秒固定 (`Tools~/lp/lp` 内 `TIMEOUT_SEC`)。長時間 async は値を上げる必要あり。

---

## AI Agent 向けリトライ戦略

```
lp exec ...
├── exit 0 → 完了
├── exit 2 (success: false)
│   ├── exceptionType: null → 値を修正して 1 回リトライ
│   └── exceptionType 非 null → ユーザに報告 (利用側コードのバグの可能性)
└── exit 1 (HTTP 4xx/5xx / 通信エラー)
    ├── 401 → unset LP_TOKEN してリトライ
    ├── 404 → lp commands で path 確認 → ユーザに報告
    ├── 429 → sleep 1s してリトライ
    ├── 5xx → 1 回リトライ、それでも失敗ならユーザに報告
    └── connection error → lp health → リトライ
```

`lp` は `exec` / `run` の `--json` で生レスポンスを返すので、リトライ判定は `--json` で叩いてから `jq` で `success` / `exceptionType` を見るのが確実。

無限ループは避ける。**同じエラーで 2 回失敗したら停止してユーザに状況を報告**するのが定石。
