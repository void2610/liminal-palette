# LP HTTP Server — Authentication

## トークン

LP は **Bearer トークン認証**を採用。`/health` 以外の全 endpoint で `Authorization: Bearer <token>` ヘッダが必須。

### 場所

| OS | パス |
|---|---|
| macOS / Linux | `~/.liminal-palette/token` |
| Windows | `%USERPROFILE%\.liminal-palette\token` |

中身: 256 bit ランダムを **base64** エンコードした文字列。改行混入は読み込み時に Trim される。

### 生成タイミング

- 初回 Editor 起動時に自動生成
- 既存ファイルがあれば読み込みのみ
- ファイルが消されたら次回 Editor 起動時に新規生成

### 権限

- **macOS / Linux**: 生成時に `chmod 600` を best-effort で適用 (= 所有者のみ読み書き可)
- **Windows**: ユーザープロファイル配下なので NTFS の ACL に任せる

## トークンの取り扱い

### 環境変数経由が標準

```bash
export LP_TOKEN=$(cat ~/.liminal-palette/token)

curl -H "Authorization: Bearer $LP_TOKEN" "$LP_BASE/api/v1/commands"
```

シェル履歴に生のトークンが残らないので推奨。

### スクリプトに直書きしない

```bash
# NG
TOKEN="abc123def456..."
curl -H "Authorization: Bearer $TOKEN" ...
```

リポジトリにコミットされる可能性があるため。代わりに:

```bash
# OK
TOKEN=$(cat ~/.liminal-palette/token)
```

### Discord / Slack / GitHub Issue に貼らない

LP は **localhost のみ**にバインドされるので LAN 経由で外部から叩かれるリスクは低いが、トークンが漏れた手元で別のユーザーがログインしている場合は全コマンドが叩ける。

## トークンの再生成

漏洩した / 共有環境で他人に見られた場合:

```bash
# 1. 削除
rm ~/.liminal-palette/token

# 2. Editor 再起動 (Unity > Quit → 再度起動)

# 3. 新しいトークンを読み込み
export LP_TOKEN=$(cat ~/.liminal-palette/token)
```

## エラー: 401 Unauthorized

### 原因と対処

| 原因 | 対処 |
|---|---|
| `Authorization` ヘッダ自体が無い | `-H "Authorization: Bearer $LP_TOKEN"` を付ける |
| `Bearer ` (末尾スペース) が抜けている | `Bearer<TOKEN>` でなく `Bearer <TOKEN>` (1 半角スペース必須) |
| `$LP_TOKEN` が空 | `echo "$LP_TOKEN"` で確認。空なら `cat ~/.liminal-palette/token` の戻り値が空 → ファイル不在か読めない |
| トークンが古い | Editor が再起動して新規トークンが生成された場合、env の古い値で叩いている |
| ファイルに改行混入 | サーバ側で Trim するので通常は問題ない。手動で `printf` で書き戻したケースのみ要注意 |

### デバッグの定石

```bash
# ヘッダが正しいか
echo "Authorization: Bearer $LP_TOKEN"

# /health は認証不要なので疎通確認
curl -s "$LP_BASE/api/v1/health"

# /commands は認証必要 → これで 401 が出るかが認証問題の切り分け
curl -s -w "\nHTTP %{http_code}\n" \
  -H "Authorization: Bearer $LP_TOKEN" \
  "$LP_BASE/api/v1/commands" | tail -5
```

## チームでの運用

### 各開発者で個別トークン

LP は **マシンごとに** トークンを持つ。共有しない。チームメンバー A の環境で動くスクリプトを B に渡す時、トークンは渡さず `cat ~/.liminal-palette/token` を含むスクリプトを渡せば各自の環境で正しく動く。

### CI 環境

CI で LP のシナリオを回したい場合:

1. CI 用にダミーの `~/.liminal-palette/token` を seed する
2. Unity Editor を CI 内で起動 (例: GameCI)
3. シナリオを `curl` で実行

LP 自身に CI ヘルパスクリプト (`scripts/ci-run-scenario.sh` の参考実装) はまだ実体ファイルが無いが、`Documentation~/scenarios.md` に終了コード設計が示されている。

## なぜ Bearer Token を採用したか

OAuth / API key の代替案もあったが:

- **localhost only バインド** → 外部からの攻撃面が狭く、認証はあくまで「同マシン内の他ユーザーから守る」程度で十分
- **OAuth は重い** (Editor 起動時に外部サーバにアクセスする運用は避けたい)
- **API key を `Authorization: Bearer` で送る** ことで通常の HTTP client (curl/jq/Postman/Python requests) と互換性が取れる

## なぜ /health だけ認証不要か

AI Agent や監視スクリプトが「LP が起動しているか」を確認する用途。認証必須にすると:

- ポートスキャンする時に毎回トークン送信 → トークン漏洩面の拡大
- LP が立ち上がっていない時の error と auth error の区別が難しくなる

`/health` は応答内容が limited (status, version, commandCount のみ) で、機密情報を含まないため認証不要。
