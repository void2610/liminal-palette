# LP HTTP Server — Authentication

## トークン

LP は **Bearer トークン認証**を採用。`/health` 以外の全 endpoint で `Authorization: Bearer <token>` ヘッダが必須。`liminal` CLI はこのヘッダ付与を自動でやる。

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

### `liminal` 経由なら何もしなくて良い

```bash
liminal commands   # → 自動で ~/.liminal-palette/token を読んで Bearer 付与
```

`liminal` の優先順位:

1. `--token T` フラグ (最優先)
2. 環境変数 `$LP_TOKEN`
3. `~/.liminal-palette/token` ファイル

### 明示指定したいとき

別マシンの token を CI で使うなど:

```bash
liminal --token "$(cat /path/to/special/token)" commands
# または
LP_TOKEN="$(cat /path/to/special/token)" liminal commands
```

### スクリプトに直書きしない

```bash
# NG (リポジトリにコミットされる可能性)
TOKEN="abc123def456..."
liminal --token "$TOKEN" ...
```

代わりに環境変数経由かファイル参照:

```bash
# OK
liminal commands   # ~/.liminal-palette/token から自動読込
```

### Discord / Slack / GitHub Issue に貼らない

LP は **localhost のみ**にバインドされるので LAN 経由で外部から叩かれるリスクは低いが、トークンが漏れた手元で別のユーザーがログインしている場合は全コマンドが叩ける。

## トークンの再生成

漏洩した / 共有環境で他人に見られた場合:

```bash
# 1. 削除
rm ~/.liminal-palette/token

# 2. Editor 再起動 (Unity > Quit → 再度起動)

# 3. 環境変数に古い値が残っていれば消す
unset LP_TOKEN

# 4. 確認
liminal health   # → 新しい token で疎通
```

## エラー: 401 Unauthorized

### 原因と対処

| 原因 | 対処 |
|---|---|
| `~/.liminal-palette/token` が空または存在しない | Editor を再起動して再生成 |
| `$LP_TOKEN` が古い (token ローテート後) | `unset LP_TOKEN` でファイル読み込みに戻す |
| `--token` で渡した値が間違っている | フラグ無しで叩いて自動読込に切り替える |
| ファイルに改行混入 | `liminal` 側で Trim するので通常問題ない。手動で `printf` で書き戻したケースのみ要注意 |

### デバッグの定石

```bash
# /health は認証不要なので疎通確認
liminal health

# /commands は認証必要 → これで通れば認証 OK
liminal commands --filter Player/ | head -5

# token が何を読んでいるか
liminal --token DEADBEEF commands 2>&1 | head   # わざと壊して比較
```

## チームでの運用

### 各開発者で個別トークン

LP は **マシンごとに** トークンを持つ。共有しない。チームメンバー A の環境で動くスクリプトを B に渡す時、トークンは渡さず `liminal` を使う形にしておけば各自の環境で正しく動く (各人の `~/.liminal-palette/token` が読まれる)。

### CI 環境

CI で LP のシナリオを回したい場合:

1. CI 用にダミーの `~/.liminal-palette/token` を seed する
2. Unity Editor を CI 内で起動 (例: GameCI)
3. シナリオを `liminal run <path>` で実行 (exit code が 0/1/2 で成否を返す)

LP 自身に CI ヘルパスクリプト (`scripts/ci-run-scenario.sh` の参考実装) はまだ実体ファイルが無いが、`Documentation~/scenarios.md` に終了コード設計が示されている。`/liminal-run-scenario` の examples/named.md にも `liminal run` ベースの簡易 CI スクリプトを掲載。

## なぜ Bearer Token を採用したか

OAuth / API key の代替案もあったが:

- **localhost only バインド** → 外部からの攻撃面が狭く、認証はあくまで「同マシン内の他ユーザーから守る」程度で十分
- **OAuth は重い** (Editor 起動時に外部サーバにアクセスする運用は避けたい)
- **API key を `Authorization: Bearer` で送る** ことで通常の HTTP client (curl/jq/Postman/Python requests/`liminal`) と互換性が取れる

## なぜ /health だけ認証不要か

AI Agent や監視スクリプトが「LP が起動しているか」を確認する用途。認証必須にすると:

- ポートスキャンする時に毎回トークン送信 → トークン漏洩面の拡大
- LP が立ち上がっていない時の error と auth error の区別が難しくなる

`/health` は応答内容が limited (status, version, commandCount のみ) で、機密情報を含まないため認証不要。`liminal` はポート発見にこの endpoint を使う。
