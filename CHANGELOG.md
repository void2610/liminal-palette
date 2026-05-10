# Changelog

このプロジェクトは [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に従い、[Semantic Versioning](https://semver.org/lang/ja/) を採用する。

## [Unreleased]

### Added
- AI Agent 用の Claude Code Skills を 8 個同梱 (`AISkills~/lp-*/SKILL.md`): lp-overview / lp-find-port / lp-list-commands / lp-execute / lp-get-state / lp-get-logs / lp-list-scenarios / lp-run-scenario。
- Editor メニュー `Tools > LiminalPalette > Install AI Skills... / Uninstall AI Skills` を追加 (`Editor/AISkillsInstaller.cs`)。利用側プロジェクトの `.claude/skills/` への配布 / 削除に対応。
- `/api/v1/health` レスポンスに `projectName` / `projectPath` を追加。同一マシンで複数 Unity プロジェクトが同時起動しているときに CLI 側がポートとプロジェクトを紐付けるための識別子。
- プロジェクト固定ポート設定 `<project>/ProjectSettings/LiminalPalette.json` (`{"port": <N>}`) を追加。`Void2610.LiminalPalette.Ipc.ProjectConfig` が読み取り、Editor / Play Mode の bootstrap が `IpcSettings.DefaultPort` の代わりに採用する。複数 Unity プロジェクトを衝突なく同時起動できる。
- `lp` CLI: 上記 preferred port を cwd / `--project` から読み、最優先候補として probe する。さらに `~/.liminal-palette/ports.json` への結果キャッシュを併用して通常 1 リクエストで discovery を終わらせる。
- `lp` CLI: 複数 Unity プロジェクト同時起動時のターゲット指定として `--project` フラグ・`$LP_PROJECT` 環境変数・cwd からの Unity プロジェクト自動検出 (`ProjectSettings/ProjectVersion.txt`) を追加。
- `lp doctor`: トークン / cwd 検出 / preferred port / キャッシュ / 生存ポート / 解決結果 を一覧表示する診断コマンド。
- `lp project show` / `lp project set-port <N>`: cwd 配下のプロジェクト固定ポート設定を表示 / 編集するコマンド。

## [0.1.0] - 2026-05-06

### Added
- 初回リリース。`apocalyptic-apartment-hunting` リポジトリの `Assets/Plugins/LiminalPalette/` から UPM パッケージとして切り出し。
- Phase 1〜5b 相当の機能を同梱:
  - Core: `[LiminalCommand]` / Registry / Executor / TypeConverter
  - UI Toolkit ベースの Editor Window + Runtime overlay
  - HTTP API (`/api/v1/{health, commands, execute, logs, scenarios}`)
  - インスタンスメソッドコマンド対応 (R3 / VContainer 必須化)
  - Scenarios (`[LiminalScenario]` / Scenario タブ / 2 endpoint)
