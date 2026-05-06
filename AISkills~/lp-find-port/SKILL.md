---
name: lp-find-port
description: "Discover which port the LiminalPalette HTTP server is listening on by scanning 7610..7615. Use when you need to: (1) Detect Editor (7610) vs Play Mode (7611) vs both, (2) Recover after a port shift due to occupancy, (3) Verify LP is running before sending other curl requests."
---

# lp-find-port

LiminalPalette の HTTP サーバーが今どのポートで稼働しているかを `/health` スキャンで発見する。`/health` は認証不要なので token 無しで叩ける。

---

## Prerequisites

このスキルは **token 不要**。`lp-overview` の Prerequisites の Step 1 (token 読み込み) はスキップして良い。

---

## Usage

```bash
# 単純: 最初に応答したポートだけ取得
for port in 7610 7611 7612 7613 7614 7615; do
  if curl -s -m 1 "http://127.0.0.1:$port/api/v1/health" > /dev/null 2>&1; then
    export LP_PORT=$port
    export LP_BASE="http://127.0.0.1:$LP_PORT"
    break
  fi
done
echo "LP_PORT=$LP_PORT"
```

```bash
# 全ポートを叩いて応答内容を一覧 (両稼働 / 複数プロジェクト稼働の検出に有用)
for p in 7610 7611 7612 7613 7614 7615; do
  resp=$(curl -s -m 1 "http://127.0.0.1:$p/api/v1/health" 2>/dev/null)
  if [ -n "$resp" ]; then
    echo "$resp" | jq --arg p "$p" '. + {port: ($p|tonumber)}'
  fi
done
```

---

## Output

`/health` のレスポンス:

```json
{
  "status": "ok",
  "version": "0.4.0",
  "commandCount": 356
}
```

| フィールド | 用途 |
|---|---|
| `status` | 常に `"ok"` (返る = 生きている) |
| `version` | LP のパッケージバージョン |
| `commandCount` | 登録済みコマンド数。Editor / Player の判別に使える |

---

## Notes

### Editor / Play Mode の判別 (両稼働時)

LP は **Editor が常に 7610 を取り、Play Mode に入ると Runtime 用サーバが 7611 で立つ**。両方が同時に `/health` に応答するケースが普通に起きる。

判別ヒューリスティック:
- `port == 7610` → Editor 側 (Editor 限定コマンドを含む)
- `port == 7611` (もしくは隣接) → Play Mode の Runtime
- `commandCount` を比較すると Editor 側のほうが大きい傾向 (Editor 限定 `[ConsoleCommand]` の分)

両方使い分けたい場合:

```bash
export LP_PORT_EDITOR=7610
export LP_PORT_RUNTIME=7611
export LP_BASE_EDITOR="http://127.0.0.1:$LP_PORT_EDITOR"
export LP_BASE_RUNTIME="http://127.0.0.1:$LP_PORT_RUNTIME"
```

### 全ポートで応答が無い場合

| 原因 | 対処 |
|---|---|
| Unity Editor が未起動 | Editor を立ち上げる |
| Production ビルドを叩いている | LP は asmdef defineConstraints で Player Production からコンパイル除外。**応答することはない**。Development build を使う |
| `IpcSettings.Enabled = false` で明示的に切られている | 利用側の C# 設定を確認 |
| ポート 7616 以降にずれている | 7610 開始から 5 個まで隣接にしか試さないので、それより先に行ったらそもそも LP 起動が異常。Editor 再起動を推奨 |

### Editor 再起動でポートはどう動くか

Editor を再起動しても、**他の何らかのプロセスが 7610 を取っていない限りは 7610 に戻る**。Play Mode の Runtime サーバは Play Mode 開始時に新規 listener を立てるので、毎回 `lp-find-port` で発見し直すのが安全。

---

## Error Handling

`/health` は認証不要なので 401 は出ない。失敗パターンは:
- 接続拒否 (curl exit code 7) → そのポートに listener がいない
- タイムアウト (`-m 1` で 1 秒) → ポートはあるがプロセスがハングしている可能性

タイムアウトを長めにしたい場合 `-m 2` 等に上げる。

---

## 関連スキル

- `lp-overview` — token 読み込みを含む全体プリフライト
- `lp-list-commands` / `lp-execute` / `lp-get-state` etc. — ポート発見後に呼ぶ
