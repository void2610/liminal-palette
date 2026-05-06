---
name: lp-find-port
description: 'Discover which port the LiminalPalette HTTP server is listening on. Scans 7610..7615 via /api/v1/health (no auth required), exports $LP_PORT and $LP_BASE, and detects Editor (7610) vs Play Mode (7611) when both are running. Use when LP_BASE is unset, after Editor restart, or when curl returns connection refused.'
when_to_use: 'Trigger phrases: "LP のヘルスチェック", "LP が動いているか確認", "ポートが分からない", "connection refused", "Play Mode と Editor 両方確認", "what port is LP on", "scan LP ports".'
allowed-tools: Bash(curl *), Bash(jq *), Bash(lsof *), Bash(echo *)
---

# lp-find-port

LiminalPalette の HTTP サーバが今どのポートで稼働しているかを `/health` スキャンで発見する。`/health` は認証不要なので token が無くても叩ける。

---

## 標準フロー (1 ポートだけ取る)

```bash
unset LP_PORT
for port in 7610 7611 7612 7613 7614 7615; do
  if curl -s -m 1 "http://127.0.0.1:$port/api/v1/health" > /dev/null 2>&1; then
    export LP_PORT=$port
    export LP_BASE="http://127.0.0.1:$LP_PORT"
    break
  fi
done
[ -n "$LP_PORT" ] && echo "Found LP at port $LP_PORT" || echo "ERROR: LP not running"
```

最初に応答したポートを取って終了。Editor 単体運用ならこれで十分。

---

## 両稼働の検出 (Editor + Play Mode)

Editor (7610) と Play Mode (7611) が同時に走っている場合、両方の `/health` が応答する。両方の port + commandCount を取得して使い分けたい時:

```bash
echo '['
sep=''
for p in 7610 7611 7612 7613 7614 7615; do
  resp=$(curl -s -m 1 "http://127.0.0.1:$p/api/v1/health" 2>/dev/null)
  if [ -n "$resp" ]; then
    printf '%s' "$sep"
    echo "$resp" | jq --arg p "$p" '. + {port: ($p|tonumber)}'
    sep=','
  fi
done
echo ']'
```

出力例:

```json
[
  {"status":"ok","version":"0.4.0","commandCount":420,"port":7610},
  {"status":"ok","version":"0.4.0","commandCount":312,"port":7611}
]
```

`commandCount` が大きいほうが Editor 側 (Editor 限定 `[ConsoleCommand]` を含むため)。スクリプト化したフルバージョンは [examples/multi-instance.sh](examples/multi-instance.sh) を参照。

### 両 base URL を環境変数に持つ

```bash
export LP_BASE_EDITOR="http://127.0.0.1:7610"
export LP_BASE_RUNTIME="http://127.0.0.1:7611"
```

`lp-execute` 等で `$LP_BASE_EDITOR` / `$LP_BASE_RUNTIME` を明示的に切り替える。

---

## Output (`/health` レスポンス)

```json
{
  "status": "ok",
  "version": "0.4.0",
  "commandCount": 356
}
```

| フィールド | 用途 |
|---|---|
| `status` | 常に `"ok"`。返ること自体が「生きている」サイン |
| `version` | LP パッケージのバージョン |
| `commandCount` | 登録済み `[ConsoleCommand]` の数。Editor / Runtime の判別ヒント |

---

## 全ポートで応答が無い場合

| 原因 | 対処 |
|---|---|
| Unity Editor 未起動 | Editor を起動 |
| Production ビルドの実行ファイルを叩いている | LP は Production 除外。Development build を使う |
| `IpcSettings.Enabled = false` で明示的に切られている | 利用側 C# 設定を確認 |
| 7616 以降にずれている (異常) | Editor Console の `IpcServer started on port: <N>` ログを確認 |
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

Play Mode の Runtime listener は Play Mode 終了時に消え、開始時に新規で立つ。Play Mode を出入りするたびに `lp-find-port` で確認するのが確実。

### Domain Reload 直後

C# 編集 → Reload の数百 ms 間は応答しないことがある。`-m 1` (1 秒タイムアウト) では弾かれる場合があるので、`-m 3` 程度に上げる手も:

```bash
for p in 7610 7611 7612 7613 7614 7615; do
  curl -s -m 3 "http://127.0.0.1:$p/api/v1/health" >/dev/null && export LP_PORT=$p && break
done
```

### IPv6

LP は IPv4 (`127.0.0.1`) のみにバインド。`localhost` が IPv6 解決される環境 (`::1` 優先) で問題になる場合は **明示的に `127.0.0.1`** を指定する。本スキルの全例は `127.0.0.1` 直書き。

---

## See also

- `/lp-overview` — token + port のフル setup
- references: `../lp-overview/references/ports.md` — ポート割り当ての全表
- examples: [multi-instance.sh](examples/multi-instance.sh) — Editor + Runtime 両検出を 1 ファイルにまとめた完全版
