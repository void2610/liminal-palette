# `liminal` Rust 実装 TDD テスト項目

`SPEC.md` に対応する TDD 用テストリスト。**フェーズ順に実装すれば常にグリーンを保てる**ように
依存関係を組んだ。各テストは独立して赤→緑→リファクタを回せる粒度。

## 全体方針

- **テストファースト**: 各フェーズの先頭でテストを全部書く → 1 つずつ緑にする → リファクタ。
- **依存性注入**: HTTP / 時刻 / 標準入力 / ファイルシステム / TTY 判定はすべて trait + impl で抽象化し、
  ユニットテストでは fake を差し込む。
- **黄金ファイル**: 出力フォーマット (text / JSON / JUnit XML) は、生成物を期待値ファイル (`tests/fixtures/...`) と
  バイト一致比較する。`insta` などのスナップショットライブラリ可。
- **integration test**: `cargo test --test cli` で `assert_cmd` + `predicates` を使い、サーバを `wiremock` で立てて
  end-to-end を回す。

## フェーズ依存図

```
P1 (pure) ──┬─→ P2 (fs) ──┬─→ P4 (discovery) ──┬─→ P5 (server-less subcmds)
            │             │                      │
            └─→ P3 (http) ┘                      ├─→ P6 (read-only auth subcmds)
                                                  ├─→ P7 (exec)
                                                  └─→ P8 (run)
                                                       │
                                                       └─→ P9 (CLI integration / e2e)
```

P1〜P3 は並行実装可能。P4 が組み上がってから P5 以降に進む。

---

## Phase 1 — Pure helpers (I/O なし)

**ゴール**: 副作用ゼロの純粋関数を全部緑に。テストはすべてミリ秒オーダー。

### 1.1 Color helpers (`SPEC §10`)

- [ ] `color::wrap(code, s)` — TTY=on, `NO_COLOR` 未設定 → `\x1b[{code}m{s}\x1b[0m`
- [ ] TTY=off → 元の文字列をそのまま
- [ ] `NO_COLOR` (空文字列でも) セット時 → 元の文字列をそのまま
- [ ] `green` / `red` / `yellow` / `cyan` / `dim` / `bold` がそれぞれ `32 / 31 / 33 / 36 / 2 / 1` を使う

### 1.2 Glob (Python `fnmatch.fnmatchcase` 互換) (`SPEC §4.10`)

- [ ] `*` が空文字列にマッチ
- [ ] `*` が `/` を跨ぐ (`Battle/*` が `Battle/Repro/X` にヒット)
- [ ] `?` が任意の 1 文字 (`/` 含む) にマッチ
- [ ] `[abc]` が `a` / `b` / `c` にマッチ、`d` にマッチしない
- [ ] `[!abc]` (否定) が `d` にマッチ、`a` にマッチしない
- [ ] `[a-c]` のレンジ
- [ ] エスケープなし (Python `fnmatch` は `\` を特別扱いしない)
- [ ] リテラルのみ → 完全一致のみ true
- [ ] 大文字小文字は区別する (`fnmatchcase`)

### 1.3 候補ポート構築 `build_candidate_ports` (`SPEC §5`)

- [ ] `--port` 明示 → `[port]` のみ (preferred / cache / DEFAULT は無視)
- [ ] `mode=editor`, `preferred.editor=7613`, cache 空 → `[7613,7614,7615,7616,7617,7618, 7610,7611,7612]` (重複除外で 9 個)
- [ ] `mode=runtime`, `preferred.runtime=7700`, `preferred.editor=7613` → `7700..=7705` → `7613..=7618` → `DEFAULT_PORTS` (重複除外)
- [ ] `mode=runtime`, `preferred.runtime=None`, `preferred.editor=7613` → editor フォールバックで `7613..=7618` → DEFAULT
- [ ] `mode=None`, `preferred = {editor:7613, runtime:7700}` → editor → runtime → DEFAULT 順
- [ ] cache に `mode=runtime, port=7800` あり、`mode=editor` 指定 → 7800 は **含まれない**
- [ ] cache の port が DEFAULT_PORTS と被る → 重複除外
- [ ] `seed=65530` → `65530..=65535` のみ (overflow しない)
- [ ] `seed=65535` → `[65535]` のみ
- [ ] preferred 全部 None, cache 空, mode 未指定 → DEFAULT_PORTS そのまま

### 1.4 マッチャ (`SPEC §5`)

- [ ] `matches_project(info, None)` → true
- [ ] `info.projectPath == target` → true
- [ ] `info.projectName == target` → true
- [ ] `target` を `canonicalize` した結果が projectPath と一致 → true
- [ ] どれにも一致しない → false
- [ ] `target` が存在しないパス → エラーで落ちず false
- [ ] `matches_mode(info, None)` → true
- [ ] `info.mode` が `"editor"` で `mode=editor` → true
- [ ] `info.mode` 欠落 + `mode=editor` → true (古いサーバ互換)
- [ ] `info.mode` 欠落 + `mode=runtime` → false
- [ ] `info.mode = "unknown"` + `mode=editor` → true (unknown は editor とみなす)

### 1.5 XML エスケープ (`SPEC §7`)

- [ ] `&` → `&amp;`
- [ ] `<` → `&lt;`
- [ ] `>` → `&gt;`
- [ ] `"` → `&quot;`
- [ ] `'` は **エスケープしない**
- [ ] 順序: `&` を最初に置換 (二重エスケープしない) — `&lt;` を入力 → `&amp;lt;`

### 1.6 JUnit XML 生成 (`SPEC §7`)

- [ ] 全 pass: `<testcase name="X" time="0.012"/>` のみ、`failures="0"`
- [ ] failure 1 件: `<failure message="..." >...</failure>` を含む
- [ ] `failedAtStep` が `steps[]` 範囲内 → message に `failedAtStep=N — Kind — error`
- [ ] `failedAtStep` 範囲外 + `resp.error` あり → body に `resp.error` 1 行
- [ ] `failedAtStep` 範囲外 + `resp.error` なし → body 空
- [ ] body 行: `step[N] Kind` / `  actualValue: ...` (非 null) / `  expected: ...` (非 null) / `  error: ...` (あれば)
- [ ] `time` は `{:.3}` 形式 (秒)
- [ ] `name` 内の `<` がエスケープされる
- [ ] testsuites/testsuite で 2 重に囲む、`name="liminal"` 固定
- [ ] 末尾改行 1 個

### 1.7 `key=value` 引数パース (`SPEC §4.6`)

- [ ] `["value=100"]` → `{"value": "100"}`
- [ ] `["a=1", "b=2"]` → `{"a":"1","b":"2"}`
- [ ] `["path=foo=bar"]` → `{"path": "foo=bar"}` (split は最初の `=` で 1 回だけ)
- [ ] `["novalue"]` → エラー (exit 1)
- [ ] `[]` → 空 dict (引数 0 個コマンド対応)
- [ ] 値は **常に文字列** (数値化しない)

### 1.8 ad-hoc steps body builder (`SPEC §4.10`)

- [ ] JSON 配列 `[{"type":"command",...}]` → `{"steps": [...]}`
- [ ] JSON object `{"steps":[...]}` → 同じ object を返す
- [ ] それ以外の JSON 形 (`{"foo":1}`、文字列、数値) → fatal
- [ ] パース失敗 → fatal
- [ ] 入力ソース: `-` は stdin、それ以外はファイル名 (P2 の I/O fake で確認)

---

## Phase 2 — Local file I/O

**ゴール**: ファイルシステムを `tempfile::TempDir` で隔離して全ケースをカバー。

### 2.1 Token 解決 (`SPEC §2`)

- [ ] `--token "abc"` → `Some("abc")` (env / file は読まない)
- [ ] `LP_TOKEN="xyz"` (file あり) → `Some("xyz")` (env が file より優先)
- [ ] `LP_TOKEN="   "` (空白のみ) → file にフォールバック (≠ Some(""))
- [ ] `LP_TOKEN=""` (空) → file にフォールバック
- [ ] file 中身 `"abc\n"` → `Some("abc")` (strip)
- [ ] file 中身 `"   "` → `Some("")` (strip 後空 = 取得失敗扱い)
- [ ] file 不在 → `None`

### 2.2 cwd → project 検出 (`SPEC §2`)

- [ ] cwd 直下に `ProjectSettings/ProjectVersion.txt` → cwd を返す
- [ ] 親ディレクトリにある → 親を返す
- [ ] root まで辿って見つからない → `None`
- [ ] cwd が削除済み (OSError) → `None` (panic しない)

### 2.3 Project config 読み (`SPEC §9`)

- [ ] `project_path = None` → `{editor: None, runtime: None}`
- [ ] ファイル不在 → `{None, None}`
- [ ] `{"port": 7613, "runtimePort": 7700}` → `{Some(7613), Some(7700)}`
- [ ] `{"port": 7613}` → `{Some(7613), None}`
- [ ] `{"port": "7613"}` (型違い) → `{None, None}`
- [ ] `{"port": 0}` → `{None, ...}` (範囲外)
- [ ] `{"port": 65536}` → `{None, ...}` (範囲外)
- [ ] `{"port": -1}` → `{None, ...}`
- [ ] 壊れた JSON → `{None, None}` (例外無し)
- [ ] JSON が array → `{None, None}` (object 以外)
- [ ] 未知フィールド (`{"foo": 1, "port": 7613}`) → port は読める

### 2.4 Project config 書き (`SPEC §9`)

- [ ] 新規ファイル: `$schema` + 指定キー、末尾改行 1 個、indent=2
- [ ] 既存に `$schema` あり → 値を保持 (canonical で上書きしない)
- [ ] 既存に `$schema` なし → canonical URL を追加
- [ ] 既存の他キー (`port`) は保持しつつ `runtimePort` を追記
- [ ] 既存の同キーは更新
- [ ] `$schema` が常に **先頭** に出る
- [ ] 親ディレクトリ不在 → `mkdir_p` してから書く
- [ ] OSError は呼び出し側で SystemExit (write 関数自体は Result 返す)

### 2.5 `project unset-port` ロジック (`SPEC §4.4`)

- [ ] ファイル不在 → no-op (戻り値で「何もしなかった」を表現)
- [ ] JSON パース失敗 → エラー (寛容にしない)
- [ ] JSON が object でない → エラー
- [ ] `field` が元々無い → 「元々設定されていません」相当
- [ ] `port` 削除後 `$schema` 以外残る → ファイル更新
- [ ] `port` 削除後 `$schema` のみ残る → **ファイル削除**
- [ ] `runtime=true` で `runtimePort` のみが対象

### 2.6 Cache load (`SPEC §8`)

- [ ] ファイル不在 → `{version:2, projects:{}}`
- [ ] `version=1` → 空キャッシュ (silent reset)
- [ ] `version` フィールド欠落 → 空キャッシュ
- [ ] 壊れた JSON → 空キャッシュ
- [ ] `projects` が array (型違い) → `projects` を `{}` に補正
- [ ] 正常な v2 → そのまま返す (roundtrip)

### 2.7 Cache save

- [ ] 親ディレクトリ不在 → `mkdir_p` してから書く
- [ ] indent=2 / 非 ASCII (日本語の projectName) がそのまま (`\uXXXX` ではない)
- [ ] ディレクトリが書けない (権限なし) → silent (panic しない)

### 2.8 `record_cache`

- [ ] `info.projectPath` 欠落 → no-op
- [ ] `info.mode` 欠落 → `editor` として記録
- [ ] `info.mode = "weird"` → `editor` として記録
- [ ] 既存 entry の他 mode (`runtime` 7700) は保持しつつ `editor` 7613 を追記
- [ ] `projectName` を更新 (info にあれば)

### 2.9 `cache_iter_entries`

- [ ] 各 entry を `(path, name, mode, port)` で yield
- [ ] `entry` が dict 以外 → スキップ
- [ ] `mode` が `editor`/`runtime` 以外 → スキップ
- [ ] `port` が int 以外 → スキップ

---

## Phase 3 — HTTP layer

**ゴール**: HTTP クライアント周辺を `wiremock` で完全カバー。

### 3.1 リクエスト構築

- [ ] GET + token あり → `Authorization: Bearer <token>` ヘッダ
- [ ] GET + token なし → Authorization ヘッダなし
- [ ] POST + body → `Content-Type: application/json` + UTF-8 body
- [ ] `Accept: application/json` を常に付ける
- [ ] body の JSON は `ensure_ascii=false` 相当 (日本語そのまま)

### 3.2 レスポンス処理

- [ ] 200 + JSON → `(200, parsed)` で返す
- [ ] 200 + 空 body → `(200, "")`
- [ ] 200 + 非 JSON → `(200, raw_string)`
- [ ] 4xx + `{"error": "msg"}` → `(4xx, parsed)`
- [ ] 5xx + 生文字列 → `(5xx, raw_string)`

### 3.3 エラーハンドリング

- [ ] 接続失敗 (port closed) → `ApiError(status=0, ...)`
- [ ] DNS 失敗 (該当する場合) → `ApiError`
- [ ] timeout → `ApiError`
- [ ] `check_status(200, _)` → ok
- [ ] `check_status(404, {"error":"x"})` → SystemExit("HTTP 404: x")
- [ ] `check_status(500, "raw")` → SystemExit("HTTP 500: raw")

### 3.4 `probe_port` (`SPEC §5`)

- [ ] 200 + JSON object → `Some(dict)`
- [ ] 200 + JSON array → `None` (object 以外は捨てる)
- [ ] 200 + 非 JSON → `None`
- [ ] 404 → `None`
- [ ] 500 → `None`
- [ ] connection refused → `None` (例外を表に出さない)
- [ ] timeout (0.4s 以上応答せず) → `None`
- [ ] 0.4s タイムアウトが効いている (1.0s 待たない) — performance regression test

### 3.5 `probe_url` (`--base-url` 用)

- [ ] 同上、ただし timeout 2.0s
- [ ] URL 末尾の `/` を取り除いてから `/api/v1/health` を付ける

---

## Phase 4 — Discovery

**ゴール**: `discover_base_url` 全分岐を fake probe で網羅。

### 4.1 ショートカット

- [ ] `--base-url` 指定 → 1 度も `probe_port` を呼ばない、`(base_url, info_or_empty)` 返す
- [ ] `--base-url` 指定 + `/health` 失敗 → `(base_url, {})` (致命でない)
- [ ] `--port` 指定 → その port のみ probe される

### 4.2 cache 早出し

- [ ] target あり + cache hit + `/health` で target/mode 一致 → 即採用、cache 更新、他 port を probe しない
- [ ] target あり + cache hit だが `/health` で target 不一致 → 通常 probe フェーズに進む
- [ ] target あり + cache hit だが mode 不一致 → 通常 probe フェーズに進む
- [ ] mode 指定時、cache の他 mode の port は早出し対象外

### 4.3 通常 probe フェーズ

- [ ] alive 0 → fatal `Liminal Palette サーバーが見つかりません (試したポート: ...)`、exit 1
- [ ] alive 1 + target/mode 未指定 → 採用
- [ ] alive 1 + target 指定で一致 → 採用
- [ ] alive 1 + target 指定で不一致 → fatal `指定のプロジェクト '...' に一致する Unity サーバーが見つかりません`
- [ ] alive 1 + mode 指定で不一致 → fatal `mode=... の Unity サーバーが生存していません`
- [ ] alive 2 + 同 path / 別 mode + フィルタなし → fatal、ヒント `--mode editor|runtime`
- [ ] alive 2 + 別 path + フィルタなし → fatal、ヒント `--project (または --mode)`
- [ ] alive 2 + 同 path + 同 mode (異常状態) → fatal、ヒント `--port`
- [ ] alive 2 + `--mode` 指定で 1 件に絞れる → 採用
- [ ] alive 2 + `--project` 指定で 1 件に絞れる → 採用

### 4.4 cache 副作用

- [ ] 採用ポートが cache に書き戻される
- [ ] probe 中に見つけた alive 全部 (採用しなかったものも含めて) が cache に記録される

---

## Phase 5 — サーバ不要のサブコマンド

**ゴール**: `init` / `doctor` / `project show|set-port|unset-port` の I/O 統合テスト。
`assert_cmd` でバイナリを起動 + `tempfile` で隔離 + 出力 stdout/stderr/exit code を検証。

### 5.1 `project show`

- [ ] cwd に Unity プロジェクトなし → exit 1、stderr に検出失敗メッセージ
- [ ] config 不在 → 「(no LiminalPalette.json — IpcSettings.DefaultPort=7610 にフォールバック)」
- [ ] config あり → `port` / `runtimePort` 表示 + raw 全行 dim 表示
- [ ] live probe セクション: probe ヒットなら `● {port} [{mode}]` 表示
- [ ] live probe: preferred と一致するなら `(matches port)` / `(matches runtimePort)` / `(matches port, runtimePort unset)` タグ
- [ ] live probe: ヒット 0 → 「(no listener for this project ...)」

### 5.2 `project set-port`

- [ ] PORT が `0` → fatal `port は 1..65535 の範囲で指定してください`
- [ ] PORT が `65536` → fatal
- [ ] PORT が `7613` → ファイル書き込み + `wrote ...` 表示
- [ ] `--runtime` フラグ → `runtimePort` フィールドに書く
- [ ] 新規ファイル → `$schema` 追加メッセージ
- [ ] 既存ファイル + `$schema` あり → schema 追加メッセージは出ない
- [ ] 既存の他キーは保持

### 5.3 `project unset-port`

- [ ] ファイル不在 → exit 0 + 「何もしません」
- [ ] JSON パース失敗 → exit 1 + 修正案メッセージ
- [ ] フィールド削除後 user 値が残る → ファイル更新メッセージ
- [ ] フィールド削除後 `$schema` のみ → ファイル削除メッセージ
- [ ] `--runtime` フラグ → `runtimePort` のみ削除

### 5.4 `init`

- [ ] cwd に Unity プロジェクトなし → fatal exit 1
- [ ] フラグなし → ファイル作成しない (副作用ゼロを assert: 実行前後の inode / mtime 一致)
- [ ] `--port 7613` → ファイル作成 + `set port = 7613` 表示
- [ ] `--runtime-port 7700` → `runtimePort` 設定
- [ ] 両方 → 両方書き込み
- [ ] `--port 0` → fatal
- [ ] config 既存 + `$schema` 無し + フラグ無し → 黄色ヒント「liminal init --port N」
- [ ] Token セクション: ファイル不在 → `missing` 黄色
- [ ] Token: 空ファイル → `empty` 黄色
- [ ] Token: 中身あり → `exists` 緑 + 文字数
- [ ] AI Skills: `.claude/skills/liminal-foo` が 1 個 → `installed 1 skill(s)` 緑
- [ ] AI Skills: `.claude/skills/lp-foo` (legacy) → 黄色警告
- [ ] AI Skills: ディレクトリ自体無し → dim メッセージ
- [ ] CLI セクション: バイナリ絶対パス + symlink ヒント
- [ ] Live check: probe 0 件 → dim、1 件 → 緑
- [ ] 最後に `init complete` 緑

### 5.5 `doctor`

- [ ] **どんな状態でも exit 0**
- [ ] Token セクションが Token 解決ロジックと一致 (env > file)
- [ ] Project detection: cwd / `--project` / `$LP_PROJECT` / resolved target を表示
- [ ] preferred port (`port` / `runtimePort`) を表示、未設定なら dim
- [ ] Port cache: 全 entry を表示、空なら `(empty)`
- [ ] Live probe: 候補ポートを列挙して結果を `●` / `○` で表示
- [ ] Resolution: 採用、ambiguous、no match のメッセージが SPEC §4.3 と一致
- [ ] `--prune-stale`: probe 対象に含まれていて応答なしの cache entry を削除
- [ ] `--prune-stale`: 削除件数を `pruned N stale cache entry/entries` で表示
- [ ] `--prune-stale` 未指定: cache を削除しない

---

## Phase 6 — Read-only 認証コマンド (`health` / `commands` / `state` / `scenarios` / `logs`)

**ゴール**: `wiremock` で `/api/v1/*` を立ててテキスト + `--json` 両方を検証。

### 6.1 `health` (認証なし)

- [ ] サーバ 200 → text に `ok` + `version` / `mode` / `projectName` / `projectPath` / `commandCount`
- [ ] フィールド欠落 → `(unknown)` を dim
- [ ] `--json` → body をそのまま `to_string_pretty`
- [ ] サーバ 0 (接続失敗) → exit 1

### 6.2 `commands`

- [ ] サーバから 5 件取得 → 5 行表示 + `total: 5`
- [ ] `--filter Player/` → prefix マッチのみ
- [ ] フィルタ後 0 件 → `(コマンドなし)` dim
- [ ] パラメータ表示 `(name:Type, ...)` を dim
- [ ] パス幅は `min(max, 60)` で揃う
- [ ] `--json` → `{"commands": [...]}` (フィルタ済み) を出力
- [ ] 401 (token 無効) → exit 1 + `HTTP 401: Unauthorized`

### 6.3 `state`

- [ ] PATH 指定 → `GET /api/v1/state?path=...` (URL エンコード)
- [ ] PATH なし → `GET /api/v1/state`
- [ ] 単一: `path` / `value` / `type` 表示
- [ ] 全件: `instanceResolved=true` は `●`、`false` は `○`
- [ ] `value=null` の表示は文字列 `"null"`
- [ ] `--json` 透過
- [ ] 404 (未登録 path) → exit 1
- [ ] フィールド 0 件 → `(state なし)` dim

### 6.4 `scenarios`

- [ ] 一覧表示 + `total: N`
- [ ] `stepCount = -1` → `?` 表示
- [ ] 0 件 → `(scenario なし)`
- [ ] `--json` 透過

### 6.5 `logs`

- [ ] `--limit 10` → `GET /api/v1/logs?limit=10`
- [ ] `--limit` 未指定 → 既定値 20 でクエリ送信
- [ ] success=true → `✓` 緑
- [ ] success=false → `✗` 赤 + `error: ...` 行
- [ ] `args` あり → `args: k=v, ...` を dim
- [ ] `value` 非 null → `value: ...` dim
- [ ] 末尾 `shown N / total M`
- [ ] `--json` 透過

### 6.6 トークン未取得時の挙動

- [ ] 認証必須コマンド + token 無し → exit 1 + 「トークンが見つかりません」
- [ ] `health` は token 無くても OK

---

## Phase 7 — `exec`

### 7.1 引数

- [ ] `exec Foo/Bar value=100` → POST body `{"path":"Foo/Bar","args":{"value":"100"}}`
- [ ] `exec Foo/Bar` → POST body `{"path":"Foo/Bar","args":{}}`
- [ ] `exec Foo/Bar value` (= 無し) → exit 1
- [ ] `exec Foo/Bar a=1 b=2` → args 2 個

### 7.2 出力

- [ ] success=true → `success (1.07 ms)` 緑 + `value : ...`
- [ ] success=false → `failed` 赤 + `error : ...` + `type : ...` + stackTrace dim
- [ ] `value=null` → value 行を出さない
- [ ] logs[].type=Log → dim、Warning → 黄、Error → 赤
- [ ] logs 0 件 → logs ブロックを出さない

### 7.3 Exit code

- [ ] success=true → exit 0
- [ ] success=false → exit 2
- [ ] success=false + `--json` → JSON 出力 + exit 2 (両立を assert)
- [ ] HTTP 500 → exit 1 (success フィールドに到達しない)
- [ ] 接続失敗 → exit 1

---

## Phase 8 — `run`

### 8.1 入力モード判定

- [ ] `run Foo/Bar` → 単一実行、`POST /scenarios/run` body=`{"path":"Foo/Bar"}`
- [ ] `run --steps file.json` → ad-hoc、PATH 無し OK
- [ ] `run Foo/Bar --steps file.json` → fatal `path と --steps は同時に指定できません`
- [ ] `run` (引数なし) → fatal `path か --steps を指定`
- [ ] `run 'Battle/*'` → glob モード
- [ ] `run 'Battle/?'` → glob モード
- [ ] `run 'Battle/[A-Z]'` → glob モード
- [ ] `run Battle/Plain` → 通常モード (グロブメタ文字なし)

### 8.2 ad-hoc

- [ ] JSON 配列ファイル → body=`{"steps":[...]}`
- [ ] `{"steps":[...]}` ファイル → そのまま
- [ ] その他の JSON → fatal
- [ ] パース失敗 → fatal
- [ ] `--steps -` → stdin から読む
- [ ] ファイル不在 → fatal

### 8.3 glob 実行

- [ ] `/api/v1/scenarios` を 1 回引いてフィルタ
- [ ] マッチ 0 件 → fatal `glob '...' に一致するシナリオがありません`
- [ ] マッチ複数 → ソートして順次 POST
- [ ] glob にヒットしない scenarios は実行されない

### 8.4 出力 (単一・テキスト)

- [ ] PASS → `PASS Foo/Bar (12.5 ms)` 緑
- [ ] FAIL → `FAIL Foo/Bar (12.5 ms)` 赤 + `failedAtStep: N`
- [ ] 各 step を `✓ [i] Kind extra (Nms)` 形式
- [ ] `kind=Command` → `extra = commandPath`
- [ ] `kind=AssertEquals` → `extra = actual=X  error` (失敗時)
- [ ] `kind=AssertEquals` 成功 → `actual=X` のみ

### 8.5 出力 (単一・JSON)

- [ ] レスポンス body をそのまま出力
- [ ] success=false でも JSON 出力 → exit 2

### 8.6 出力 (glob・テキスト)

- [ ] 各 scenario 1 行: `✓ Foo/Bar  (12 ms)` または `✗ Foo/Bar  (12 ms)  failedAtStep=N`
- [ ] FAIL 行の次に `      <error>` を 1 行 (赤)
- [ ] サマリ `PASS|FAIL  N scenarios, P passed, F failed  (T ms total)`
- [ ] 全 pass で `PASS` 緑、1 つでも fail なら `FAIL` 赤

### 8.7 出力 (glob・JSON)

- [ ] payload 形: `{scenarios:[...], total, passed, failed}`
- [ ] 各 entry の `path` は **label** (glob 展開後のパス)、resp の `path` を上書き
- [ ] ad-hoc + glob は理論上発生しないが、resp の `path:null` が label で潰れることを assert

### 8.8 `--report PATH`

- [ ] PATH 親ディレクトリ不在 → 自動作成
- [ ] XML 全体が `<?xml version="1.0" ...>` で始まり末尾改行 1 個
- [ ] 全 pass / 一部 fail / 全 fail のいずれも生成成功
- [ ] 書き込み失敗 (権限なし) → fatal exit 1
- [ ] `--json` と併用しても XML 書き込みは行う
- [ ] テキストモードで書いた場合 stderr に `JUnit report: <path>` を dim

### 8.9 Exit code

- [ ] 単一 PASS → exit 0
- [ ] 単一 FAIL → exit 2
- [ ] glob 全 pass → exit 0
- [ ] glob 1 件以上 fail → exit 2
- [ ] HTTP 500 → exit 1
- [ ] glob 展開時の HTTP 500 → exit 1

### 8.10 サーバ side condition

- [ ] 409 `alreadyRunning` → exit 1 (HTTP エラー扱い、success=false ではない)
- [ ] 429 (rate limit) → exit 1

---

## Phase 9 — CLI integration / e2e

**ゴール**: `clap` の挙動と全体パイプラインを最終確認。

### 9.1 引数パース

- [ ] サブコマンドなし → `clap` がエラー (exit 2 相当、`clap` 仕様)
- [ ] 未知のサブコマンド `liminal foo` → エラー
- [ ] グローバルフラグはサブコマンド前後どちらでも置ける (`liminal --json health` / `liminal health --json`)
- [ ] `liminal --help` → 0 で終了 + サブコマンド一覧
- [ ] `liminal exec --help` → サブコマンドヘルプ
- [ ] `--mode foo` (enum 違反) → clap エラー

### 9.2 環境変数

- [ ] `LP_TOKEN` がパイプラインで使われる
- [ ] `LP_PROJECT` がパイプラインで使われる
- [ ] `NO_COLOR` でカラー抑制

### 9.3 Exit code マトリクス

| 状況 | 期待 |
|---|---|
| `health` 成功 | 0 |
| `health` 接続失敗 | 1 |
| `commands` token 無し | 1 |
| `exec` success=true | 0 |
| `exec` success=false | 2 |
| `exec` HTTP 500 | 1 |
| `run` 単一 pass | 0 |
| `run` 単一 fail | 2 |
| `run` glob 全 pass | 0 |
| `run` glob 1 fail | 2 |
| `run` glob 0 マッチ | 1 |
| `doctor` (alive 0) | 0 |
| `doctor` (alive 多数) | 0 |
| `init` 成功 | 0 |
| `init` cwd 非 Unity | 1 |
| `project set-port 70000` | 1 |
| 引数パース失敗 | 2 (clap 既定) |

### 9.4 Discovery e2e

- [ ] 2 個の wiremock を別ポートで立てて、`--mode editor` / `--mode runtime` で正しい方が選ばれる
- [ ] `--project NAME` で該当 listener が 1 つに絞られる
- [ ] 2 個 alive + フラグなし → exit 1 + ambiguous メッセージ

### 9.5 ファイル副作用

- [ ] 通常コマンド (`health` / `exec` / `commands` / ...) は cache 以外を書かない
- [ ] cache は alive 探索の副作用としてのみ更新される
- [ ] `init` フラグ無しは ProjectSettings 配下に書かない

### 9.6 性能 (任意)

- [ ] preferred port が立っている状態で `liminal health` が **1 リクエストで完了** (probe loop に入らない) — リクエスト回数を assert

---

## カバレッジゴール

| Phase | 行カバレッジ目標 | 主な未カバー領域 |
|---|---|---|
| P1 | 100% | — |
| P2 | 95%+ | OSError 詳細分岐 |
| P3 | 90%+ | timeout の precise タイミング |
| P4 | 95%+ | — |
| P5 | 80%+ | 端末幅依存の表示 |
| P6 | 85%+ | 端末幅依存 |
| P7 | 95%+ | — |
| P8 | 95%+ | — |
| P9 | 80%+ | clap が裏で出すメッセージ全文 |

## fixtures / 共通ユーティリティ

`tests/common/` に置くもの:

- `mock_server.rs` — wiremock セットアップ + `/api/v1/health` 等の典型レスポンス。
- `temp_project.rs` — `TempDir` に `ProjectSettings/ProjectVersion.txt` を作る helper。
- `fixtures/` — JSON / JUnit XML の期待値。
- `fake_clock.rs` (任意) — `durationMs` 出力の決定論化。

## 実装順序の推奨

1. **Phase 1 全部** を 1 PR で。テストファイルだけ先にレビューしてもらうと意図が伝わる。
2. **Phase 2 / 3 並行** で 2 PR。
3. **Phase 4** で 1 PR。これが終わると CLI 化に進める。
4. **Phase 5** から 1 サブコマンドずつ PR を切る (`init` → `doctor` → `project`)。
5. **Phase 6** も同様にコマンドごとに PR。
6. **Phase 7 / 8** はそれぞれ 1 PR。
7. **Phase 9** は 1 PR で全 e2e。
