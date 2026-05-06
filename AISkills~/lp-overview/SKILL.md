---
name: lp-overview
description: "Entry point for LiminalPalette HTTP API automation. Use when you need to: (1) First-time setup of token + port discovery before any LP curl operation, (2) Understand the overall workflow (find-port → list-commands → execute → observe state), (3) Look up authentication / rate limit / port allocation rules, (4) Pick which `lp-*` skill to use for a given Unity automation task."
---

# lp-overview

LiminalPalette は Unity プロジェクトに `[ConsoleCommand]` 属性で登録された C# メソッドを HTTP API 経由で叩けるライブラリ。Editor / Play Mode 両対応で、AI Agent (Claude Code 等) が `curl` でゲーム操作を自動化することを主用途としている。

このスキルは LP HTTP API を使う**最初の入り口**。token と稼働ポートのセットアップ、ワークフロー全体像、他 7 個の `lp-*` スキルの索引を提供する。

---

## Prerequisites — 全 `lp-*` スキル共通の初期化

```bash
# 1. Token を環境変数に読み込む (初回 Editor 起動時に ~/.liminal-palette/token に自動生成される)
export LP_TOKEN=$(cat ~/.liminal-palette/token)

# 2. 稼働ポートを発見する (Editor=7610, Play Mode=7611, ポート占有時は隣接にずれる)
for port in 7610 7611 7612 7613 7614 7615; do
  if curl -s -m 1 "http://127.0.0.1:$port/api/v1/health" > /dev/null 2>&1; then
    export LP_PORT=$port
    break
  fi
done

# 3. ベース URL を決める
export LP_BASE="http://127.0.0.1:$LP_PORT"

# 確認
curl -s "$LP_BASE/api/v1/health" | jq .
# → {"status":"ok","version":"0.4.0","commandCount":356}
```

ポートが取れない (LP_PORT が空) → Unity Editor が起動していない / Production ビルドで動かしている (HTTP サーバ自体がコンパイル除外) / ファイアウォール。

---

## ワークフロー早見

| やりたいこと | 使うスキル | endpoint |
|---|---|---|
| LP が起動しているか確認 / どのポートにいるか | `lp-find-port` | `GET /health` |
| 利用できるコマンドを発見 (絞り込み付き) | `lp-list-commands` | `GET /commands` |
| コマンドを実行する (ゲーム操作の中核) | `lp-execute` | `POST /execute` |
| 現在のゲーム状態を読む (HP / カウント等) | `lp-get-state` | `GET /state` |
| 直近のコマンド実行履歴を見る | `lp-get-logs` | `GET /logs` |
| 宣言済みシナリオ一覧を見る | `lp-list-scenarios` | `GET /scenarios` |
| シナリオを実行する (named / ad-hoc) | `lp-run-scenario` | `POST /scenarios/run` |

典型的な探索フロー:

```
lp-find-port → lp-list-commands → lp-execute → lp-get-state (検証)
```

統合テスト的に複数操作を走らせたい:

```
lp-find-port → lp-list-scenarios → lp-run-scenario
```

---

## ポート割り当ての規則

| 環境 | サーバー起動 | ポート |
|---|---|---|
| Unity Editor | ✅ | 7610 |
| Editor の Play Mode | ✅ | 7611 (Editor が 7610 を占有しているため) |
| Standalone Development build | ✅ | 7610 |
| Standalone Production build | ❌ | (asmdef defineConstraints で**コンパイル除外**) |

**Editor + Play Mode 両稼働時は 2 ポート両方が `/health` を返す**。AI Agent はどちらに送るか文脈で判断:
- Editor 操作 (シーン編集、Asset 操作等) → 7610
- ゲーム状態操作 (Player HP、Enemy Spawn 等) → 7611
- `health.commandCount` の差で見分けるヒューリスティック: Editor 側 > Player 側 (Editor 限定コマンドが含まれるため)

---

## 認証

- **Bearer トークン**: `~/.liminal-palette/token` に保存された 256 bit ランダムを base64 した文字列
- `/health` 以外の全 endpoint で必須: `Authorization: Bearer $LP_TOKEN`
- 漏洩した場合は `rm ~/.liminal-palette/token` → Editor 再起動で再生成
- **トークンを Discord / Slack / GitHub Issue 等に貼らない** (ローカル localhost にしかバインドしないが、漏れたら全コマンドが叩ける)

---

## レートリミットとサイズ上限

| 項目 | 既定 | 対象 endpoint |
|---|---|---|
| Rate limit | 30 req/s (1 秒スライディングウィンドウ) | `POST /execute` と `POST /scenarios/run` (枠共有) |
| Body size | 1 MB | 全 POST endpoint |

超過時はそれぞれ `429 Too Many Requests` / `413 Payload Too Large`。利用側で上げたい場合:

```csharp
[InitializeOnLoadMethod]
static void TweakIpcLimits()
{
    Void2610.LiminalPalette.Ipc.IpcSettings.ExecuteRateLimitPerSecond = 100;
    Void2610.LiminalPalette.Ipc.IpcSettings.MaxRequestBodyBytes = 4 * 1024 * 1024;
}
```

---

## エラーステータス早見

| Status | 意味 | 一次対処 |
|---|---|---|
| 401 | Token 不一致 / 欠落 | `~/.liminal-palette/token` を再 cat / Editor 再起動 |
| 404 | path 未登録 | `lp-list-commands` / `lp-list-scenarios` で確認 |
| 405 | method 違い | endpoint の GET/POST を確認 |
| 409 | scenario 排他実行中 | 完了を待つ (1 並列のみ) |
| 413 | body 1 MB 超過 | ファイルパス渡しに切り替え |
| 429 | rate limit 超過 | 間隔を空ける |
| 500 | endpoint 内例外 | `error` 本文で原因把握 |

---

## ULoop と LiminalPalette の使い分け

両方インストールされている環境での選択基準:

| やりたいこと | 使うべき |
|---|---|
| Unity Editor 自体の操作 (asset 作成、scene 編集、Console clear 等) | `uloop-*` (CLI 経由) |
| 利用側プロジェクトに `[ConsoleCommand]` で公開された **ゲームロジック**呼び出し | `lp-*` (HTTP 経由) |
| Game View スクリーンショット | `uloop-screenshot` |
| ゲーム内の reactive な状態 (`ReactiveProperty<T>`) を読む | `lp-get-state` |
| 統合テスト (spawn → wait → assert の連鎖) | `lp-run-scenario` ad-hoc |

両方の skill 名で似た操作 (例: `uloop-get-logs` vs `lp-get-logs`) があるが、`uloop-get-logs` は Unity Console、`lp-get-logs` は LP の invocation history。**目的が違う**。

---

## トラブルシューティング

### 全ポートで `/health` が返らない
- Unity Editor が起動しているか確認
- Production ビルドの実行ファイルを叩いていないか (asmdef defineConstraints でコンパイル除外されているため絶対に応答しない)
- `IpcSettings.Enabled = false` で明示的に切られていないか

### 401 が返る
- `cat ~/.liminal-palette/token` が空 / 改行混入のみ → Editor 再起動でトークン再生成
- `Authorization: Bearer ` (末尾スペース) が抜けていないか
- 環境変数 `$LP_TOKEN` の中身を `echo` で確認

### Editor を Play Mode に入れたら curl が反応しない
- ポートが 7610 → 7611 にずれている。`lp-find-port` で再発見

詳細: `Documentation~/troubleshooting.md` (LP 本体ドキュメント)。

---

## 関連ドキュメント (LP 本体)

- `Documentation~/ipc.md` — HTTP API 仕様の一次ソース
- `Documentation~/scenarios.md` — シナリオ機能
- `Documentation~/security.md` — トークン管理 / 攻撃面
- `Documentation~/commands.md` — `[ConsoleCommand]` の引数バインドと async 戻り値
