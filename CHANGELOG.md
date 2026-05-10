# Changelog

このプロジェクトは [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に従い、[Semantic Versioning](https://semver.org/lang/ja/) を採用する。

## [Unreleased]

### Added
- AI Agent 用の Claude Code Skills を 8 個同梱 (`AISkills~/lp-*/SKILL.md`): lp-overview / lp-find-port / lp-list-commands / lp-execute / lp-get-state / lp-get-logs / lp-list-scenarios / lp-run-scenario。
- Editor メニュー `Tools > LiminalPalette > Install AI Skills... / Uninstall AI Skills` を追加 (`Editor/AISkillsInstaller.cs`)。利用側プロジェクトの `.claude/skills/` への配布 / 削除に対応。

## [0.1.0] - 2026-05-06

### Added
- 初回リリース。`apocalyptic-apartment-hunting` リポジトリの `Assets/Plugins/LiminalPalette/` から UPM パッケージとして切り出し。
- Phase 1〜5b 相当の機能を同梱:
  - Core: `[LiminalCommand]` / Registry / Executor / TypeConverter
  - UI Toolkit ベースの Editor Window + Runtime overlay
  - HTTP API (`/api/v1/{health, commands, execute, logs, scenarios}`)
  - インスタンスメソッドコマンド対応 (R3 / VContainer 必須化)
  - Scenarios (`[LiminalScenario]` / Scenario タブ / 2 endpoint)
