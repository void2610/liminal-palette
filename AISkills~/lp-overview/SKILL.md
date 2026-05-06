---
name: lp-overview
description: 'Entry point and shared setup for LiminalPalette HTTP API automation. Loads $LP_TOKEN and $LP_BASE into the shell, explains the seven lp-* skills and which one to pick, and links to detailed references on ports, auth, and troubleshooting. Invoke this first before any other lp-* skill.'
when_to_use: 'User mentions LiminalPalette, LP, or asks to script a Unity Editor / Play Mode action via curl. Trigger phrases: "LP のヘルスチェック", "LP に何ができる", "Unity に curl で", "list available commands", "what skills are there for the palette".'
allowed-tools: Bash(cat *), Bash(curl *), Bash(jq *), Bash(echo *), Read
---

# lp-overview

LiminalPalette (LP) は Unity プロジェクトに `[ConsoleCommand]` 属性で登録された C# メソッドを HTTP API 経由で実行できるライブラリ。AI Agent (Claude Code 等) が `curl` で Editor / Play Mode を自動操作することを主用途とする。

このスキルは LP HTTP API を使う **最初の入り口**。token + 稼働ポートを env に流し込み、他 7 個の `lp-*` スキルへの索引と運用ルールを提供する。

> このスキルがロードされた時点で本文は同じ会話の最後まで context に残る。`lp-find-port` 等を後で呼んでも Prerequisites を再実行する必要はない (ただし Editor を再起動した場合は port が変わる可能性があるので `lp-find-port` で再発見する)。

---

## Setup (必ず最初に実行)

```bash
# 1. Token 読み込み (Editor 初回起動時に ~/.liminal-palette/token に自動生成される)
export LP_TOKEN=$(cat ~/.liminal-palette/token)

# 2. 稼働ポート発見 (Editor=7610, Play Mode=7611, 占有時は隣接にずれる)
unset LP_PORT
for port in 7610 7611 7612 7613 7614 7615; do
  if curl -s -m 1 "http://127.0.0.1:$port/api/v1/health" > /dev/null 2>&1; then
    export LP_PORT=$port
    break
  fi
done

# 3. Base URL
[ -n "$LP_PORT" ] || { echo "ERROR: LP not running. Start Unity Editor."; }
export LP_BASE="http://127.0.0.1:$LP_PORT"

# 4. 確認
curl -s "$LP_BASE/api/v1/health" | jq .
# → {"status":"ok","version":"0.4.0","commandCount":356}
```

セットアップが失敗する典型原因は [references/troubleshooting.md](references/troubleshooting.md) を参照。

---

## ワークフロー早見表

| やりたいこと | 使うスキル | endpoint |
|---|---|---|
| LP が起動しているか確認 | `/lp-find-port` | `GET /health` |
| 利用できるコマンドを発見 | `/lp-list-commands` | `GET /commands` |
| コマンドを実行する | `/lp-execute` | `POST /execute` |
| 現在のゲーム状態を読む | `/lp-get-state` | `GET /state` |
| 直近の実行履歴を見る | `/lp-get-logs` | `GET /logs` |
| 宣言済みシナリオ一覧 | `/lp-list-scenarios` | `GET /scenarios` |
| シナリオ実行 (named/ad-hoc) | `/lp-run-scenario` | `POST /scenarios/run` |

### 典型フロー 1: 探索 → 実行 → 検証

```
lp-find-port → lp-list-commands → lp-execute → lp-get-state
```

### 典型フロー 2: 統合テスト (1 リクエストで複数操作)

```
lp-find-port → lp-list-scenarios → lp-run-scenario
```

### 典型フロー 3: 失敗した実行をデバッグ

```
lp-get-logs (直近の失敗を見る) → lp-list-commands (path/args 確認) → lp-execute (修正して再実行)
```

---

## 主要事実 (詳細は references/)

- **ポート割り当て**: Editor=7610, Play Mode=7611, build=7610。Production build は **コンパイル除外で応答しない**。詳細: [references/ports.md](references/ports.md)
- **認証**: Bearer token。`/health` 以外で必須。token は `~/.liminal-palette/token`。漏洩時は削除→Editor 再起動で再生成。詳細: [references/auth.md](references/auth.md)
- **レートリミット**: `/execute` と `/scenarios/run` で 30 req/s 共有。1 秒スライディングウィンドウ
- **body 上限**: 全 POST endpoint で 1 MB
- **Production 除外**: HTTP サーバ自体が Player Production からコンパイル除外される

---

## ULoop と LiminalPalette の使い分け

両方インストール済み環境での選択基準:

| やりたいこと | 推奨 | 理由 |
|---|---|---|
| Unity Editor 自体の操作 (asset 作成, scene 編集) | `uloop-*` | Editor SDK へのフルアクセス |
| Game View スクリーンショット | `uloop-screenshot` | LP に対応 endpoint なし |
| 利用側プロジェクトに `[ConsoleCommand]` で公開された **ゲームロジック** | `lp-*` | プロジェクトコードへの最短パス |
| ゲーム内 reactive 状態 (`ReactiveProperty<T>`) を読む | `lp-get-state` | Observable 専用の endpoint |
| spawn → wait → assert の連鎖 (統合テスト) | `lp-run-scenario` ad-hoc | fail-fast + 1 リクエストで完結 |

両方の名前空間に似た skill (例: `uloop-get-logs` vs `lp-get-logs`) があるが **目的が違う** ことに注意:
- `uloop-get-logs` → Unity Console 全体 (`Debug.Log*` 含む)
- `lp-get-logs` → LP の invocation history のみ

---

## エラーステータス早見

| Status | 意味 | 一次対処 |
|---|---|---|
| 401 | Token 不一致/欠落 | `~/.liminal-palette/token` を再 cat / Editor 再起動 |
| 404 | path 未登録 | `lp-list-commands` / `lp-list-scenarios` で確認 |
| 405 | method 違い | endpoint の GET/POST を確認 |
| 409 | scenario 排他実行中 | 完了を待つ (1 並列のみ) |
| 413 | body 1 MB 超過 | ファイルパス渡しに切り替え |
| 429 | rate limit 超過 | 間隔を空ける |
| 500 | endpoint 内例外 | response の `error` 本文を確認 |

詳細別表は [references/troubleshooting.md](references/troubleshooting.md)。

---

## See also

- LP 本体ドキュメント: `Packages/com.void2610.liminal-palette/Documentation~/{ipc,scenarios,security,commands}.md`
- 個別 skill: `/lp-find-port`, `/lp-list-commands`, `/lp-execute`, `/lp-get-state`, `/lp-get-logs`, `/lp-list-scenarios`, `/lp-run-scenario`
- references/
  - [ports.md](references/ports.md) — Editor/Play Mode/Build のポート割り当てと両稼働の判別
  - [auth.md](references/auth.md) — token の生成/再生成/権限/共有時の注意
  - [troubleshooting.md](references/troubleshooting.md) — 「全ポートで応答が無い」「401 が来る」等の網羅
