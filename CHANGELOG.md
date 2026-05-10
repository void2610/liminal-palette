# Changelog

このプロジェクトは [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に従い、[Semantic Versioning](https://semver.org/lang/ja/) を採用する。

## [Unreleased]

### Added
- AI Agent 用の Claude Code Skills を 8 個同梱 (`AISkills~/liminal-*/SKILL.md`): liminal-overview / liminal-find-port / liminal-list-commands / liminal-execute / liminal-get-state / liminal-get-logs / liminal-list-scenarios / liminal-run-scenario。
- Editor メニュー `Tools > LiminalPalette > Install AI Skills... / Uninstall AI Skills` を追加 (`Editor/AISkillsInstaller.cs`)。利用側プロジェクトの `.claude/skills/` への配布 / 削除に対応 (旧 `lp-*` skill ディレクトリも legacy として一掃)。
- `/api/v1/health` レスポンスに `projectName` / `projectPath` / `mode` を追加。`mode` は `"editor"` か `"runtime"` で、同一プロジェクト内で Editor IpcServer と Play Mode / Player の Runtime IpcServer を区別する。
- プロジェクト固定ポート設定 `<project>/ProjectSettings/LiminalPalette.json` を追加。`port` は Editor 用、`runtimePort` は Play Mode / Runtime 用 (省略時は `port` にフォールバック)。`Void2610.LiminalPalette.Ipc.ProjectConfig.GetPreferredPort()` / `GetPreferredRuntimePort()` から読む。複数 Unity プロジェクト + 同プロジェクト内 Editor/Play Mode を衝突なく同時起動できる。
- `liminal` CLI: 上記 preferred port を cwd / `--project` から読み、最優先候補として probe する。さらに `~/.liminal-palette/ports.json` (v2) に mode 別ポートをキャッシュして次回起動を高速化。
- `liminal` CLI: 複数 listener 同時起動時のターゲット指定として `--mode editor|runtime` フラグ、`--project` フラグ、`$LP_PROJECT` 環境変数、cwd からの Unity プロジェクト自動検出 (`ProjectSettings/ProjectVersion.txt`) を追加。
- `liminal doctor`: トークン / cwd 検出 / preferred port / キャッシュ / 生存ポート / 解決結果 を一覧表示する診断コマンド。`--prune-stale` で probe しても応答が無かった cache エントリを削除。
- `liminal project show` / `liminal project set-port [--runtime] <N>` / `liminal project unset-port [--runtime]`: cwd 配下のプロジェクト固定ポート設定を表示 / 編集するコマンド。`show` はライブ probe で listener の現在位置も表示する。

### Changed
- CLI コマンド名を `lp` から `liminal` に変更 (`lp` は macOS の line printer ユーティリティと衝突するため)。`Tools~/lp/` → `Tools~/liminal/`、AI Skill 名も `lp-*` → `liminal-*` にリネーム。AISkillsInstaller の Uninstall は legacy `lp-*` ディレクトリも自動的に掃除する。

### Fixed
- `/api/v1/health` が HTTP ワーカースレッドから `Application.productName` / `Application.dataPath` を呼び 500 になるバグを修正 (#5)。Editor / Runtime bootstrap がメインスレッドで取得済みの値を `HealthEndpoint` のコンストラクタに渡すように変更。

## [0.1.0] - 2026-05-06

### Added
- 初回リリース。`apocalyptic-apartment-hunting` リポジトリの `Assets/Plugins/LiminalPalette/` から UPM パッケージとして切り出し。
- Phase 1〜5b 相当の機能を同梱:
  - Core: `[LiminalCommand]` / Registry / Executor / TypeConverter
  - UI Toolkit ベースの Editor Window + Runtime overlay
  - HTTP API (`/api/v1/{health, commands, execute, logs, scenarios}`)
  - インスタンスメソッドコマンド対応 (R3 / VContainer 必須化)
  - Scenarios (`[LiminalScenario]` / Scenario タブ / 2 endpoint)
