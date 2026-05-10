---
name: lp-overview
description: 'Entry point for LiminalPalette HTTP API automation via the bundled `lp` CLI. Explains the seven lp-* skills and which one to pick, plus links to references on ports/auth/troubleshooting. Invoke this first when a task mentions LiminalPalette.'
when_to_use: 'User mentions LiminalPalette, LP, or asks to script a Unity Editor / Play Mode action. Trigger phrases: "LP のヘルスチェック", "LP に何ができる", "Unity を CLI で操作", "list available commands", "what skills are there for the palette".'
allowed-tools: Bash(lp *), Bash(jq *), Read
---

# lp-overview

LiminalPalette (LP) は Unity プロジェクトに `[LiminalCommand]` で登録された C# メソッドを HTTP API 経由で実行できるライブラリ。AI Agent (Claude Code 等) が Editor / Play Mode を CLI で自動操作することを主用途とする。

このスキルは LP を使う **最初の入り口**。専用 CLI `lp` 経由で叩く前提で、他 7 個の `lp-*` スキルへの索引と運用ルールを提供する。

> `lp` はトークン読み込みとポート発見を自動でやるので、各スキルでセットアップを書く必要はない。Editor を再起動した後でも `lp` 側で再スキャンされる。

---

## 前提: `lp` CLI

LP リポジトリ同梱の `Tools~/lp/lp` (Python 3 標準ライブラリのみ、依存ゼロ) を PATH に通しておく。

```bash
ln -s "<lp-package-path>/Tools~/lp/lp" ~/.local/bin/lp
lp health   # → ok ... が出れば設定 OK
```

詳細: `Tools~/lp/README.md`。

| 自動化される項目 | 出所 |
|---|---|
| トークン | `~/.liminal-palette/token` (Editor 初回起動時に自動生成) または `$LP_TOKEN` |
| ベース URL | `~/.liminal-palette/ports.json` のキャッシュ → 失敗時 `7610〜7615` を short timeout で probe |
| ターゲットプロジェクト | `--project` / `$LP_PROJECT` / cwd の `ProjectSettings/ProjectVersion.txt` 検出 (`/health` の `projectName`+`projectPath` で照合) |

明示したい場合は `--token`, `--port`, `--base-url`, `--project` で個別に上書きできる。複数 Unity プロジェクトが同時起動しているときは `lp doctor` で全体像を確認すると速い。

---

## ワークフロー早見表

| やりたいこと | 使うスキル | `lp` サブコマンド |
|---|---|---|
| LP が起動しているか確認 | `/lp-find-port` | `lp health` / `lp doctor` |
| 利用できるコマンドを発見 | `/lp-list-commands` | `lp commands [--filter Player/]` |
| コマンドを実行する | `/lp-execute` | `lp exec <path> key=value...` |
| 現在のゲーム状態を読む | `/lp-get-state` | `lp state [<path>]` |
| 直近の実行履歴を見る | `/lp-get-logs` | `lp logs --limit N` |
| 宣言済みシナリオ一覧 | `/lp-list-scenarios` | `lp scenarios` |
| シナリオ実行 (named/ad-hoc) | `/lp-run-scenario` | `lp run <path>` / `lp run --steps -` |

### 典型フロー 1: 探索 → 実行 → 検証

```
lp health → lp commands --filter Player/ → lp exec ... → lp state Player/HP
```

### 典型フロー 2: 統合テスト (1 リクエストで複数操作)

```
lp scenarios → lp run <path>     # named
lp run --steps -                  # ad-hoc を stdin から流す
```

### 典型フロー 3: 失敗した実行をデバッグ

```
lp logs --json | jq '.invocations[] | select(.result.success==false)' → lp commands --filter <prefix> → lp exec ... (修正)
```

---

## `--json` で機械可読モード

人間向けの装飾 (色 / 整形) を切って生 JSON が出る。`jq` と組み合わせる時に使う:

```bash
lp commands --json | jq -r '.commands[] | select(.path | startswith("Player/")) | .path'
lp logs --limit 100 --json | jq '.invocations[] | select(.result.success == false)'
lp state --json | jq '.fields[] | select(.value != null)'
```

通常は色付き整形で出るので `--json` 無しのまま読めば良い。

---

## 主要事実 (詳細は references/)

- **ポート割り当て**: Editor=7610, Play Mode=7611, build=7610。Production build は **コンパイル除外で応答しない**。詳細: [references/ports.md](references/ports.md)
- **認証**: Bearer token。`lp` が自動で読む。漏洩時は `~/.liminal-palette/token` を削除→Editor 再起動で再生成。詳細: [references/auth.md](references/auth.md)
- **レートリミット**: `lp exec` と `lp run` で 30 req/s 共有。1 秒スライディングウィンドウ
- **body 上限**: 全 POST endpoint で 1 MB
- **Production 除外**: HTTP サーバ自体が Player Production からコンパイル除外される

---

## ULoop と LiminalPalette の使い分け

両方インストール済み環境での選択基準:

| やりたいこと | 推奨 | 理由 |
|---|---|---|
| Unity Editor 自体の操作 (asset 作成, scene 編集) | `uloop-*` | Editor SDK へのフルアクセス |
| Game View スクリーンショット | `uloop-screenshot` | LP に対応 endpoint なし |
| 利用側プロジェクトに `[LiminalCommand]` で公開された **ゲームロジック** | `lp-*` | プロジェクトコードへの最短パス |
| ゲーム内 reactive 状態 (`ReactiveProperty<T>`) を読む | `lp-get-state` | Observable 専用の endpoint |
| spawn → wait → assert の連鎖 (統合テスト) | `lp-run-scenario` ad-hoc | fail-fast + 1 リクエストで完結 |

両方の名前空間に似た skill (例: `uloop-get-logs` vs `lp-get-logs`) があるが **目的が違う** ことに注意:
- `uloop-get-logs` → Unity Console 全体 (`Debug.Log*` 含む)
- `lp-get-logs` → LP の invocation history のみ

---

## エラー早見

`lp` の exit code:

| code | 状況 |
|---|---|
| 0 | 成功 |
| 1 | HTTP / ネットワーク / トークン未設定など使用エラー |
| 2 | サーバには届いたが `success: false` (`exec` / `run`) |

HTTP status 別の対処:

| Status | 意味 | 一次対処 |
|---|---|---|
| 401 | Token 不一致/欠落 | `~/.liminal-palette/token` を再 cat / Editor 再起動 |
| 404 | path 未登録 | `lp commands` / `lp scenarios` で確認 |
| 405 | method 違い | endpoint の GET/POST を確認 |
| 409 | scenario 排他実行中 | 完了を待つ (1 並列のみ) |
| 413 | body 1 MB 超過 | ファイルパス渡しに切り替え |
| 429 | rate limit 超過 | 間隔を空ける |
| 500 | endpoint 内例外 | response の `error` 本文を確認 |

詳細: [references/troubleshooting.md](references/troubleshooting.md)。

---

## See also

- LP 本体ドキュメント: `Documentation~/{ipc,scenarios,security,commands}.md`
- CLI 詳細: `Tools~/lp/README.md`
- 個別 skill: `/lp-find-port`, `/lp-list-commands`, `/lp-execute`, `/lp-get-state`, `/lp-get-logs`, `/lp-list-scenarios`, `/lp-run-scenario`
- references/
  - [ports.md](references/ports.md) — Editor/Play Mode/Build のポート割り当てと両稼働の判別
  - [auth.md](references/auth.md) — token の生成/再生成/権限/共有時の注意
  - [troubleshooting.md](references/troubleshooting.md) — 「全ポートで応答が無い」「401 が来る」等の網羅
