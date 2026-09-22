# `liminal` CLI は別リポジトリへ移りました

このディレクトリにあった Python 製の CLI (`Tools~/liminal/liminal`) は **v0.4.0 で削除**しました。

後継は [**liminal-cli**](https://github.com/void2610/liminal-cli) — Rust 実装の単体バイナリです。
外から見える振る舞い (引数 / 出力 / ファイル形式 / exit code) は揃えてあります。

## 入れ直す

Unity の Editor メニュー:

```
Tools > LiminalPalette > Install CLI...
```

自分のプラットフォーム向けバイナリを GitHub Releases から取得して `~/.local/bin/liminal` に置きます。
手動で入れる場合は [Releases](https://github.com/void2610/liminal-cli/releases/latest) から直接どうぞ。

## symlink が壊れている場合

以前の手順でこのディレクトリへ symlink を張っていた場合、削除により `liminal: command not found` になります。

```bash
ls -l "$(command -v liminal)"   # ここを指していたら張り直す
rm -f ~/.local/bin/liminal      # 壊れた symlink を消してから上のメニューで入れ直す
```

## 移行で変わる点

- 引数エラーの exit code が 2 → **1** (2 は「サーバには届いたが失敗」専用になりました)
- HTTP エラーにサーバ側のメッセージが付きます (`HTTP 401: token が一致しません`)
- `--json` がレスポンスを型に落とさず中継するので、サーバが増やしたフィールドも落ちません

不具合の切り分けは [`Documentation~/troubleshooting.md`](../../Documentation~/troubleshooting.md) の「CLI (`liminal`) 系」を参照してください。
