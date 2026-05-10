---
name: liminal-find-port
description: 'Verify that the LiminalPalette HTTP server is up via `liminal health` / `liminal doctor`. The CLI caches the discovered port per Unity project at `~/.liminal-palette/ports.json` and falls back to scanning 7610..7615. Per-project port pinning via `ProjectSettings/LiminalPalette.json` (`port` for Editor, `runtimePort` for Play Mode). Multi-listener disambiguation uses `--project` / `$LP_PROJECT` / cwd auto-detect plus `--mode editor|runtime` against `/health` `mode`+`projectName`+`projectPath`. Use when LP appears down, after Editor restart, when both Editor + Play Mode are running, or when multiple Unity projects are open at once.'
when_to_use: 'Trigger phrases: "LP のヘルスチェック", "LP が動いているか確認", "ポートが分からない", "connection refused", "Play Mode と Editor 両方確認", "what port is LP on", "scan LP ports", "複数プロジェクト", "liminal doctor".'
allowed-tools: Bash(liminal *), Bash(jq *), Bash(lsof *), Bash(echo *)
---

# liminal-find-port

LP の HTTP サーバが今どこで動いているかを `liminal health` / `liminal doctor` で確認する。

`liminal` は **直近成功ポートを `~/.liminal-palette/ports.json` にキャッシュ**しており、次回呼び出しはそこから試す。キャッシュが効かない場合のみ `7610〜7615` を short timeout (0.4s) で probe する。単に「LP に届くか」を見たいなら `liminal health` を 1 発叩けば終わり (`/health` は認証不要)。

明示的に切り分けが必要なのは次のケース:
- **Editor + Play Mode が両方走っている** (同じプロジェクト内、別ポート)
- **複数 Unity プロジェクトが同時起動している** (別プロジェクト、別ポート)

---

## 1 ポートだけ確認 (通常)

```bash
liminal health
```

出力例:

```
ok  http://127.0.0.1:7610
  version       : 0.4.0
  mode          : editor
  projectName   : MyGame
  projectPath   : /Users/me/dev/MyGame
  commandCount  : 395
```

応答した URL がそのまま使われる。これで OK なら他のスキルもそのまま叩いて良い。

## 環境まるごと診断 (`liminal doctor`)

token / cwd 検出 / キャッシュ / 生存ポート / 解決結果を一発で出す。LP の挙動が怪しいときの最初の一手:

```bash
liminal doctor
```

出力には次が並ぶ:

- Token: 存在 / `$LP_TOKEN` 経由か / 未取得か
- Project detection: cwd / cwd→Unity プロジェクト / `--project` / `$LP_PROJECT` / 解決後ターゲット
- Port cache: `~/.liminal-palette/ports.json` の中身 (プロジェクトごとのポート)
- Live probe: `7610〜7615` を全部叩いた結果 (生存ポート × `projectName`/`projectPath`/`commandCount`)
- Resolution: 最終的にどのポートが選ばれるか / 曖昧なら警告

---

## Editor + Play Mode 両稼働の検出

両方走っている場合、Editor は `port`、Play Mode は `runtimePort` (省略時は `port+1` など隣接) で別々の listener を立てる。`liminal` は `/health` の `mode` フィールドで Editor/Runtime を区別するので、`--mode editor|runtime` で 1 つに絞れる:

```bash
liminal --mode editor health    # Editor 側
liminal --mode runtime health   # Play Mode (Runtime IpcServer)
```

明示的なポートで叩きたい場合は `--port` で従来どおり指定可能。

## 複数 Unity プロジェクト同時起動

**推奨セットアップ: プロジェクトごとに Editor と Play Mode の固定ポートを宣言**

```bash
# プロジェクト A で
cd ~/dev/MyGame
liminal project set-port 7613             # Editor (port)
liminal project set-port --runtime 7700   # Play Mode (runtimePort)

# プロジェクト B で
cd ~/dev/Other
liminal project set-port 7620
liminal project set-port --runtime 7720
```

`ProjectSettings/LiminalPalette.json` は次の形になり、Git にコミット可能:

```json
{
  "port": 7613,
  "runtimePort": 7700
}
```

Unity Editor は `port`、Play Mode IpcServer は `runtimePort` に bind する。`liminal` は cwd から自動でこの 2 つを最優先候補として probe する。`liminal project show` で現在の設定 + 生存している listener を一発確認できる。

設定をやめるときは `liminal project unset-port [--runtime]`。

**固定していない場合の指定手段** (優先順位順):

```bash
# プロジェクト名で指定 (Application.productName と一致)
liminal --project MyGame state

# プロジェクトパスで指定 (絶対パス推奨)
liminal --project /Users/me/dev/Other state

# 環境変数で固定 (シェルセッション単位)
export LP_PROJECT=MyGame
liminal state
```

`liminal` は cwd を辿って `ProjectSettings/ProjectVersion.txt` を見つけるとそのプロジェクトをターゲット扱いする。
ターゲット未指定で複数生存している場合は曖昧として停止し、生存中のポート + プロジェクト一覧を出すので、それを見て `--project` を付け直す。

`commandCount` を比較すると判別できる (Editor 側に Editor 限定 `[LiminalCommand]` が含まれるため通常 Editor の方が多い):

```bash
for p in 7610 7611 7612 7613 7614 7615; do
  out=$(liminal --port "$p" --json health 2>/dev/null) || continue
  echo "$out" | jq --arg p "$p" '. + {port: ($p|tonumber)}'
done | jq -s .
```

両方並行で叩く運用なら、毎回 `--port` を渡すか `--base-url` で固定する:

```bash
liminal --base-url http://127.0.0.1:7610 commands --filter Editor/   # Editor
liminal --base-url http://127.0.0.1:7611 state                       # Play Mode
```

---

## Output (`/health` レスポンス)

`--json` 無しなら整形済み、`--json` 付きなら以下が返る:

```json
{"status":"ok","version":"0.4.0","mode":"editor","projectName":"MyGame","projectPath":"/Users/me/dev/MyGame","commandCount":356}
```

| フィールド | 用途 |
|---|---|
| `status` | 常に `"ok"`。返ること自体が「生きている」サイン |
| `version` | LP パッケージのバージョン |
| `mode` | `"editor"` または `"runtime"`。Editor IpcServer か Play Mode / Player の Runtime IpcServer かの判別 |
| `projectName` | `Application.productName`。複数プロジェクト同時起動時の照合キー |
| `projectPath` | `Application.dataPath` の親ディレクトリ。同マシン内で唯一 |
| `commandCount` | 登録済み `[LiminalCommand]` の数。`mode` 補強情報として利用可能 (Editor 限定 `[LiminalCommand]` 込みなので Editor の方が多い) |

---

## 全ポートで応答が無い場合

`liminal health` が `Liminal Palette サーバーが見つかりません` を返すケース:

| 原因 | 対処 |
|---|---|
| Unity Editor 未起動 | Editor を起動 |
| Production ビルドの実行ファイルを叩いている | LP は Production 除外。Development build を使う |
| `IpcSettings.Enabled = false` で明示的に切られている | 利用側 C# 設定を確認 |
| 7616 以降にずれている (異常) | Editor Console の `IpcServer started on port: <N>` ログを確認 → `liminal --port N health` |
| 別プロセスがポートを占有 | `lsof -i :7610` 等で確認 |

詳細: `/liminal-overview` の [references/troubleshooting.md](../liminal-overview/references/troubleshooting.md)

### Listener を強制終了する (異常時のみ)

LP listener が残ったままになって新しい Editor 起動でポートが取れない場合:

```bash
lsof -i :7610 | tail -1 | awk '{print $2}' | xargs -r kill -9
```

⚠️ 通常運用では不要。Editor 終了時に自動 unload される。

---

## Notes

### Editor 再起動でポートはどう動くか

通常は **同じポートに戻る**。他プロセスが 7610 を占有していなければ 7610 に再バインド。

Play Mode の Runtime listener は Play Mode 終了時に消え、開始時に新規で立つ。Play Mode を出入りするたびに `liminal health` で確認するのが確実。

### Domain Reload 直後

C# 編集 → Reload の数百 ms 間は応答しないことがある。`liminal` のタイムアウトは 10 秒なので通常は十分待つが、Reload 直後にスキャンが走ると connection refused で 1 ポート空振りすることがある。1 秒置いて再試行で復帰する。

### IPv6

LP は IPv4 (`127.0.0.1`) のみにバインド。`liminal` も内部で `127.0.0.1` を直書きしているので環境依存はない。

---

## See also

- `/liminal-overview` — `liminal` のセットアップ全体像
- references: `../liminal-overview/references/ports.md` — ポート割り当ての全表
