# `lp` — LiminalPalette CLI

LiminalPalette の HTTP API (`/api/v1/*`) を叩くシングルファイル CLI。

- **依存ゼロ**: Python 3 標準ライブラリのみ。`chmod +x` だけで動く
- **ポート自動発見 + キャッシュ**: 直近成功したポートを `~/.liminal-palette/ports.json` に覚え、次回はそこから試す。失敗時のみ `7610〜7615` を short timeout で probe
- **複数プロジェクト対応**: cwd / `--project` / `$LP_PROJECT` でターゲットを指定でき、`/health` の `projectName` / `projectPath` で照合してポートを選ぶ
- **トークン自動読込**: `~/.liminal-palette/token` を勝手に読む
- **JSON モード**: `--json` で生 JSON が出るので `jq` と組み合わせ可能
- **`lp doctor`**: トークン・cwd 検出・キャッシュ・生存ポートを一発で可視化

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

# 環境診断: token / cwd 検出 / cache / 生存ポート / 解決結果 (認証不要)
lp doctor

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
| `--project NAME_OR_PATH` | `$LP_PROJECT` または cwd 検出 | 複数 Unity プロジェクトが起動している時のターゲット指定。`/health` の `projectName` か `projectPath` と一致するもの |
| `--token T` | `~/.liminal-palette/token` または `$LP_TOKEN` | Bearer トークン |
| `--json` | off | 生 JSON で出力。`jq` と合わせる時に |

## 複数プロジェクト同時起動

同一マシンで複数の Unity Editor / Play Mode が走っていても、各インスタンスが固有のポートを取るので
ターゲットを 1 つに決める必要がある。優先順位は次の通り:

1. `--project NAME_OR_PATH`
2. `$LP_PROJECT`
3. cwd を起点に親ディレクトリを辿り、`ProjectSettings/ProjectVersion.txt` がある場所を Unity プロジェクトと判定

ターゲットが決まると `/health` の `projectName` / `projectPath` で照合し、合致するポートだけを採用する。
2 つ以上ヒットした場合や、ターゲット未指定で複数プロジェクトが生きている場合はエラーで止めて生存中の
ポート一覧を表示するので、`--project` を付けて再実行する。

ポートはプロジェクト単位で `~/.liminal-palette/ports.json` にキャッシュされ、次回起動時はキャッシュ済みの
ポートを最初に試すので普段は 1 リクエストで discovery が終わる。Unity 側が再起動して別ポートを取った場合は
キャッシュ照合に失敗するので、自動で `7610〜7615` の probe にフォールバックする。

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
