---
name: lp-find-port
description: 'Verify that the LiminalPalette HTTP server is up via `lp health`. The CLI auto-discovers the port (scans 7610..7615), so explicit detection is only needed when distinguishing Editor (7610) vs Play Mode (7611) — use `lp --port N health` to target each. Use when LP appears down, after Editor restart, or when both Editor + Play Mode are running.'
when_to_use: 'Trigger phrases: "LP のヘルスチェック", "LP が動いているか確認", "ポートが分からない", "connection refused", "Play Mode と Editor 両方確認", "what port is LP on", "scan LP ports".'
allowed-tools: Bash(lp *), Bash(jq *), Bash(lsof *), Bash(echo *)
---

# lp-find-port

LP の HTTP サーバが今どこで動いているかを `lp health` で確認する。

`lp` は通常呼び出し時に **`7610〜7615` を自動スキャン**して最初に応答したポートを使うので、単に「LP に届くか」を見たいなら `lp health` を 1 発叩けば終わり (`/health` は認証不要)。

明示的にポートを切り分ける必要があるのは **Editor + Play Mode が両方走っている** ケースだけ。

---

## 1 ポートだけ確認 (通常)

```bash
lp health
```

出力例:

```
ok  http://127.0.0.1:7610
  version       : 0.4.0
  commandCount  : 395
```

応答した URL がそのまま使われる。これで OK なら他のスキルもそのまま叩いて良い。

---

## Editor + Play Mode 両稼働の検出

両方走っている場合、Editor が 7610、Play Mode が 7611 にずれる。明示的にどちらか指定するには `--port` を使う:

```bash
lp --port 7610 health   # → Editor
lp --port 7611 health   # → Play Mode
```

`commandCount` を比較すると判別できる (Editor 側に Editor 限定 `[LiminalCommand]` が含まれるため通常 Editor の方が多い):

```bash
for p in 7610 7611 7612 7613 7614 7615; do
  out=$(lp --port "$p" --json health 2>/dev/null) || continue
  echo "$out" | jq --arg p "$p" '. + {port: ($p|tonumber)}'
done | jq -s .
```

両方並行で叩く運用なら、毎回 `--port` を渡すか `--base-url` で固定する:

```bash
lp --base-url http://127.0.0.1:7610 commands --filter Editor/   # Editor
lp --base-url http://127.0.0.1:7611 state                       # Play Mode
```

---

## Output (`/health` レスポンス)

`--json` 無しなら整形済み、`--json` 付きなら以下が返る:

```json
{"status":"ok","version":"0.4.0","commandCount":356}
```

| フィールド | 用途 |
|---|---|
| `status` | 常に `"ok"`。返ること自体が「生きている」サイン |
| `version` | LP パッケージのバージョン |
| `commandCount` | 登録済み `[LiminalCommand]` の数。Editor / Runtime の判別ヒント |

---

## 全ポートで応答が無い場合

`lp health` が `Liminal Palette サーバーが見つかりません` を返すケース:

| 原因 | 対処 |
|---|---|
| Unity Editor 未起動 | Editor を起動 |
| Production ビルドの実行ファイルを叩いている | LP は Production 除外。Development build を使う |
| `IpcSettings.Enabled = false` で明示的に切られている | 利用側 C# 設定を確認 |
| 7616 以降にずれている (異常) | Editor Console の `IpcServer started on port: <N>` ログを確認 → `lp --port N health` |
| 別プロセスがポートを占有 | `lsof -i :7610` 等で確認 |

詳細: `/lp-overview` の [references/troubleshooting.md](../lp-overview/references/troubleshooting.md)

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

Play Mode の Runtime listener は Play Mode 終了時に消え、開始時に新規で立つ。Play Mode を出入りするたびに `lp health` で確認するのが確実。

### Domain Reload 直後

C# 編集 → Reload の数百 ms 間は応答しないことがある。`lp` のタイムアウトは 10 秒なので通常は十分待つが、Reload 直後にスキャンが走ると connection refused で 1 ポート空振りすることがある。1 秒置いて再試行で復帰する。

### IPv6

LP は IPv4 (`127.0.0.1`) のみにバインド。`lp` も内部で `127.0.0.1` を直書きしているので環境依存はない。

---

## See also

- `/lp-overview` — `lp` のセットアップ全体像
- references: `../lp-overview/references/ports.md` — ポート割り当ての全表
