# `liminal` — LiminalPalette CLI

LiminalPalette の HTTP API (`/api/v1/*`) を叩くシングルファイル CLI。

- **依存ゼロ**: Python 3 標準ライブラリのみ。`chmod +x` だけで動く
- **プロジェクト固定ポート**: `ProjectSettings/LiminalPalette.json` で Editor と Play Mode の port を別々に固定可能 (`{"port": 7613, "runtimePort": 7700}`)。複数 Unity プロジェクトを同時起動する運用向け
- **ポート自動発見 + キャッシュ**: preferred port が無くても直近成功ポートを `~/.liminal-palette/ports.json` に覚え、失敗時は `7610〜7615` を short timeout で probe
- **複数プロジェクト + Editor/Play Mode 同時対応**: cwd / `--project` / `$LP_PROJECT` でプロジェクト、`--mode editor|runtime` で Editor/Play Mode を選別。`/health` の `mode` / `projectName` / `projectPath` で照合
- **トークン自動読込**: `~/.liminal-palette/token` を勝手に読む
- **JSON モード**: `--json` で生 JSON が出るので `jq` と組み合わせ可能
- **`liminal doctor`**: トークン・cwd 検出・preferred port・キャッシュ・生存ポートを一発で可視化 (`--prune-stale` で古い cache エントリを削除)
- **`liminal project`**: `show` で設定確認 + ライブ probe、`set-port [--runtime] N` で書き込み、`unset-port [--runtime]` で削除

## インストール

```bash
# どこでも好きな場所に symlink。例:
ln -s "$(pwd)/Tools~/liminal/liminal" ~/.local/bin/liminal

# または PATH に通したディレクトリへコピー:
cp Tools~/liminal/liminal ~/.local/bin/liminal
```

`Tools~` は `~` 接尾辞によって Unity の asset import から除外される。

## 使い方

```bash
# 生存確認 (認証不要)
liminal health

# 環境診断: token / cwd 検出 / cache / 生存ポート / 解決結果 (認証不要)
liminal doctor

# コマンド一覧 (Player/ 配下に絞る)
liminal commands --filter Player/

# コマンド実行 (引数は key=value、すべて文字列で OK)
liminal exec Player/HP/Heal amount=10

# 引数 0 個
liminal exec Editor/Console/Clear

# 履歴
liminal logs --limit 10

# State スナップショット (全件 / 単一)
liminal state
liminal state Player/HP

# シナリオ
liminal scenarios
liminal run Battle/Repro/StairDescendingBack
```

## グローバルオプション

| オプション | 既定 | 説明 |
|---|---|---|
| `--base-url URL` | (auto-discover) | ベース URL を直接指定。例: `http://127.0.0.1:7611` |
| `--port N` | (auto-discover) | ポートだけ上書き。 `7610〜7615` の自動探索を 1 つに絞る |
| `--project NAME_OR_PATH` | `$LP_PROJECT` または cwd 検出 | 複数 Unity プロジェクトが起動している時のターゲット指定。`/health` の `projectName` か `projectPath` と一致するもの |
| `--mode editor\|runtime` | (auto) | 同一プロジェクトで Editor 側 (常時) と Play Mode / Runtime 側 (Play Mode 中だけ) の listener を区別する |
| `--token T` | `~/.liminal-palette/token` または `$LP_TOKEN` | Bearer トークン |
| `--json` | off | 生 JSON で出力。`jq` と合わせる時に |

## 複数プロジェクト + Editor / Play Mode 同時起動

同一マシンで複数の Unity Editor / Play Mode が走っていても、各インスタンスが固有のポートを取るので
ターゲットを 1 つに決める必要がある。**推奨: 各プロジェクトに固定ポートを宣言する**:

```bash
# プロジェクト A の repo ルートで
liminal project set-port 7613             # Editor (port)
liminal project set-port --runtime 7700   # Play Mode (runtimePort)

# プロジェクト B の repo ルートで
liminal project set-port 7620
liminal project set-port --runtime 7720
```

`ProjectSettings/LiminalPalette.json` は次のような形になる:

```json
{
  "port": 7613,
  "runtimePort": 7700
}
```

Unity Editor は `port` に、Play Mode (Runtime IpcServer) は `runtimePort` に bind する。`runtimePort` 未設定なら
Runtime も `port` を試し、Editor と衝突したら隣接 (`port+1`...) にずれる (= 既存挙動)。ファイルは ProjectSettings
配下なので Git にコミットしてチームで共有可能。

`liminal` は cwd / `--project` から JSON を読み、preferred port を最優先候補として probe する。同じプロジェクトで
Editor + Play Mode 両方が走っている場合は `--mode editor|runtime` で 1 つに絞る:

```bash
liminal --mode editor state    # Editor 側を叩く
liminal --mode runtime state   # Play Mode 側を叩く
```

固定していない場合のプロジェクト解決優先順位:

1. `--project NAME_OR_PATH`
2. `$LP_PROJECT`
3. cwd を起点に親ディレクトリを辿り、`ProjectSettings/ProjectVersion.txt` がある場所を Unity プロジェクトと判定

ターゲットが決まると `/health` の `projectName` / `projectPath` で照合し、合致するポートだけを採用する。
2 つ以上ヒットした場合 (例: Editor + Play Mode) はエラーで止めて生存中のポート一覧を表示するので、
`--mode` か `--project` を付けて再実行する。

### Discovery の挙動

`liminal` は次の順で probe する (`--port` / `--base-url` 明示時はそれを最優先):

1. `ProjectSettings/LiminalPalette.json` の preferred ports (cwd / `--project` から判定):
   - `--mode editor`: `port` と隣接 5 個
   - `--mode runtime`: `runtimePort`、次に fallback として `port` と隣接 5 個ずつ
   - `--mode` 未指定: `port` → `runtimePort` の順で隣接展開
2. `~/.liminal-palette/ports.json` キャッシュにヒットした候補 (mode 別)
3. デフォルト `7610〜7615`

各 probe は short timeout (0.4s) で済むので、preferred port が立っていれば 1 リクエストで終わる。

## Exit code

| code | 状況 |
|---|---|
| 0 | 成功 |
| 1 | HTTP / ネットワークエラー、トークン未設定 等の使用エラー |
| 2 | コマンド実行は届いたが `success: false` (`exec` / `run`) |

## `--json` の例

```bash
# 失敗履歴だけ拾う
liminal logs --limit 100 --json | jq '.invocations[] | select(.result.success == false)'

# Player 配下のコマンドパスだけ取り出す
liminal commands --json | jq -r '.commands[] | select(.path | startswith("Player/")) | .path'

# State の値が null じゃないものだけ
liminal state --json | jq '.fields[] | select(.value != null)'
```

## 関連

- API リファレンス: [`Documentation~/ipc.md`](../../Documentation~/ipc.md)
- セキュリティ / トークン: [`Documentation~/security.md`](../../Documentation~/security.md)
