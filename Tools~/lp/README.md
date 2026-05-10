# `lp` — LiminalPalette CLI

LiminalPalette の HTTP API (`/api/v1/*`) を叩くシングルファイル CLI。

- **依存ゼロ**: Python 3 標準ライブラリのみ。`chmod +x` だけで動く
- **ポート自動発見**: `7610〜7615` を `/health` で順に叩いて生きている方を選ぶ (Editor が 7610、Play Mode が 7611 でも勝手に当たる)
- **トークン自動読込**: `~/.liminal-palette/token` を勝手に読む
- **JSON モード**: `--json` で生 JSON が出るので `jq` と組み合わせ可能

## インストール

```bash
# どこでも好きな場所に symlink。例:
ln -s "$(pwd)/Tools~/lp/lp" ~/.local/bin/lp

# または PATH に通したディレクトリへコピー:
cp Tools~/lp/lp ~/.local/bin/lp
```

`Tools~` は `~` 接尾辞によって Unity の asset import から除外される。

## 使い方

```bash
# 生存確認 (認証不要)
lp health

# コマンド一覧 (Player/ 配下に絞る)
lp commands --filter Player/

# コマンド実行 (引数は key=value、すべて文字列で OK)
lp exec Player/HP/Heal amount=10

# 引数 0 個
lp exec Editor/Console/Clear

# 履歴
lp logs --limit 10

# State スナップショット (全件 / 単一)
lp state
lp state Player/HP

# シナリオ
lp scenarios
lp run Battle/Repro/StairDescendingBack
```

## グローバルオプション

| オプション | 既定 | 説明 |
|---|---|---|
| `--base-url URL` | (auto-discover) | ベース URL を直接指定。例: `http://127.0.0.1:7611` |
| `--port N` | (auto-discover) | ポートだけ上書き。 `7610〜7615` の自動探索を 1 つに絞る |
| `--token T` | `~/.liminal-palette/token` または `$LP_TOKEN` | Bearer トークン |
| `--json` | off | 生 JSON で出力。`jq` と合わせる時に |

## Exit code

| code | 状況 |
|---|---|
| 0 | 成功 |
| 1 | HTTP / ネットワークエラー、トークン未設定 等の使用エラー |
| 2 | コマンド実行は届いたが `success: false` (`exec` / `run`) |

## `--json` の例

```bash
# 失敗履歴だけ拾う
lp logs --limit 100 --json | jq '.invocations[] | select(.result.success == false)'

# Player 配下のコマンドパスだけ取り出す
lp commands --json | jq -r '.commands[] | select(.path | startswith("Player/")) | .path'

# State の値が null じゃないものだけ
lp state --json | jq '.fields[] | select(.value != null)'
```

## 関連

- API リファレンス: [`Documentation~/ipc.md`](../../Documentation~/ipc.md)
- セキュリティ / トークン: [`Documentation~/security.md`](../../Documentation~/security.md)
