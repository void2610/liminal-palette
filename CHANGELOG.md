# Changelog

このプロジェクトは [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に従い、[Semantic Versioning](https://semver.org/lang/ja/) を採用する。

## [Unreleased]

### Added
- `[LiminalScenario(ReadyWhen = "Game/State=WorldMap")]` 宣言的属性を追加。指定すると `ScenarioExecutor` が Scene ロード後・本体ステップ前に `AssertEventually` を自動で 1 つ差し込み、観測フィールドが期待値になるまでシナリオ開始を遅延する ("観測パス=期待値" 形式、最初の `=` で分割)。全シナリオ冒頭の「初期化完了を待つ」定型ステップが属性 1 行で済む。形式不正はステップ実行前に構成エラーとして失敗する。`ScenarioDescriptor.ReadyWhen` にも露出。
- `[LiminalScenario(TimeScale = 20f)]` 宣言的属性を追加。0 より大きい値を指定すると、シナリオ実行中だけ `Time.timeScale` をその値へ上書きし、終了時 (成功 / 失敗 / キャンセル問わず) に元の値へ復元する。ステップとして挿入せず `ScenarioExecutor` が実行全体を wrap するため、途中失敗でも復元漏れしない (シナリオ内の `Time/SetScale` + `Time/Reset` ペアはこの点で劣るため置き換え推奨)。PlayMode 専用 (`Application.isPlaying` ガード)。`ScenarioDescriptor.TimeScale` にも露出。
- `CommandExecutor` が `UniTask` / `UniTask<T>` 戻り値の `[LiminalCommand]` を await して結果を取得できるようになった (#24)。従来は unwrap 対象外で `UniTask<T>.ToString()` (`(Pending)`) が結果文字列として返り、`AssertCommandReturns` / HTTP API から async コマンドの完了と戻り値を検証できなかった。`AsTask` が `UniTaskExtensions` の拡張メソッドであるため、ジェネリック定義を `MakeGenericMethod` で閉じて `Task<T>` へ正規化してから既存の await 経路に合流させる (定義は static キャッシュ)。本体 asmdef が UniTask を直接参照するようになったため、利用側プロジェクトには UniTask のインストールが必須 (R3 / VContainer と同じ扱い、README の動作要件に明記)。
- 組み込みランタイムコマンド `Anim/CompleteAll` / `Anim/CancelAll` を追加 (`Runtime/Anim/LitMotion/LitMotionCommands.cs`)。`Anim/CompleteAll` はアクティブな LitMotion tween 全てを最終値まで即座に進め、bound property 反映と OnComplete 発火を同フレーム内に完了させる (`AssertEquals` を直後の行に書ける)。Sequence 内 tween / 無限ループ tween は仕様上 Complete できないので残り、`Anim/CancelAll` (OnCancel を同フレーム発火) を逃げ道として併設。E2E シナリオでの演出待機 (`WaitSeconds` / `AssertEventually`) を撲滅してテスト実行時間を短縮するのが目的。実装は LitMotion internal (`MotionManager.list` / `MotionStorage<>.sparseIndexLookup` / `SparseIndex`) にリフレクションで到達しており、独立サブ asmdef `Void2610.LiminalPalette.Runtime.LitMotion` に隔離した (`com.annulusgames.lit-motion >= 1.0` が導入されていて `LIMINAL_PALETTE_LITMOTION` define が立っているときのみコンパイル)。LitMotion 未導入プロジェクトでは lp 本体に一切影響しない。連鎖 OnComplete で新 tween が生えるケースは最大 8 iterations の bounded loop で snapshot 更新しつつ吸収、進捗ゼロで早期打ち切り。戻り文字列は `completed=N skipped=M iterations=I` (skipped は最終スナップショット時点の残数で、イテレーション毎に加算する水増し方式ではない)、上限打ち切り時は末尾に ` truncated=true` を付与する。
- `ScenarioStep.AssertEventually(observableFieldPath, expected, timeoutSeconds = 5f)` を first-class ステップ種別として追加 (新 `ScenarioStepKind.AssertEventually` + internal `AssertEventuallyStep`)。`AssertEquals` が即時 1 回評価なのに対し、本 step は `timeoutSeconds` 以内で field の現在値が `expected` と一致するまで毎フレーム再評価し、一致したら成功・超過したら最後の不一致内容を添えて失敗する。LitMotion / UniTask 演出の完了後に確定する値を、固定待ち (`WaitSeconds`) なしで検証するためのもの。比較規則は `AssertEquals` と同じ (expected が string なら field の型へ変換)。ObservableField 未登録 / 読取例外 / 型変換失敗は「待っても解決しない構成エラー」として即時失敗する。ad-hoc IPC のシリアライズ (`observableFieldPath` / `expected` / `timeoutSeconds`) にも露出。
- `ScenarioStep.AssertCommandReturns(commandPath, args, expected)` を first-class ステップ種別として追加 (新 `ScenarioStepKind.AssertCommandReturns` + internal `AssertCommandReturnsStep`)。内部でコマンドを実行して戻り値文字列が `expected` と ordinal 一致するかを検証する。`expected=null` の場合は「コマンドが成功すれば OK」モード。ad-hoc IPC からは `"type": "assert_command_returns"` で利用可能 (`path` / `args` / `expected`)。これにより利用側が `Foo/Assert/*` のような domain-specific Assert コマンドを書く必要が無くなり、観測コマンド (戻り値が string) + 本 step で済む。
- `Void2610.LiminalPalette.TestSupport` asmdef を新設し、`LiminalPaletteTestRunner` ヘルパを提供 (`TestSupport/LiminalPaletteTestRunner.cs`)。`GetScenariosWithPrefix(prefix)` で Registry から prefix 一致シナリオを列挙し、`RunScenario(path)` が `[UnityTest]` 互換の `IEnumerator` を返す。利用側は `[UnityTest] IEnumerator Run([ValueSource(nameof(Paths))] string path) => LiminalPaletteTestRunner.RunScenario(path)` の 1 メソッドで全シナリオを parametrized test 化でき、シナリオ毎に `[UnityTest]` を書くボイラープレートが消える。NUnit 依存のため `UNITY_INCLUDE_TESTS` 制約付き、`autoReferenced=false` で利用側 test asmdef が明示参照する形。
- 組み込みランタイムコマンド `Scene/Current` / `Scene/Load(sceneName)` を追加 (`Runtime/Scene/SceneCommands.cs`)。`Time/*` と同じ流儀で ad-hoc CLI (`liminal exec Scene/Load sceneName=Foo`) から即時シーン切替・現在シーン取得が可能。シナリオから使う場合は `ScenarioStep.LoadScene` / `[LiminalScenario(Scene=...)]` が引き続き推奨。
- `ScenarioStep.LoadScene(sceneName)` を first-class ステップ種別として追加 (新 `ScenarioStepKind.LoadScene` + 内部 `LoadSceneStep`)。`SceneManager.LoadSceneAsync(name, Single)` を呼んで完了まで `Task.Yield` で待機。PlayMode 専用 (Edit Mode では `Application.isPlaying=false` で fail 扱い)。
- `[LiminalScenario(Scene = "TestScene")]` 宣言的属性を追加。指定するとシナリオ本体ステップの前に `LoadScene` ステップを `ScenarioExecutor` が自動で 1 つ前置きする。「各テストを専用シーンで実行したい」「テスト間で状態を漏らさない」用途のボイラープレートが 1 行で済む。復帰 (元シーンへ戻す) はしない仕様 — 最後にロードされたシーンが残る。`ScenarioDescriptor.Scene` プロパティと `GET /api/v1/scenarios` の JSON `scene` フィールドにも露出。
- 組み込みランタイムコマンド `Time/SetScale` / `Time/Reset` / `Time/Pause` / `Time/Resume` + 観測フィールド `Time/Scale` (静的 `ReactiveProperty<float>`) を追加 (`Runtime/Time/TimeCommands.cs`)。`Editor/` prefix ではないので Editor / PlayMode / Player ビルドの 3 経路すべてから呼べる。`AssertEquals("Time/Scale", 5f)` でシナリオから検証可能。`Time.timeScale` 変更通知 API が無いため、`TimeScalePoller` (HideAndDontSave な常駐 GameObject) で 1 フレームごとにポーリングして外部書き換えも追従させる。
- `ObservableFieldDescriptor.IsStatic` を追加: `static` プロパティ / フィールドに `[LiminalObservableField]` を付けた場合、UI / IPC / Scenario の各経路が `IInstanceResolver` を経由せずに値を読めるようになった。組み込み `Time/Scale` のような static utility 用途を VContainer 登録なしで成立させるため。
- `liminal init` サブコマンド: cwd→Unity プロジェクト検出 / `ProjectSettings/LiminalPalette.json` 状態 / token / `.claude/skills/` の AI Skills / live probe を 1 コマンドで一覧。`--port` / `--runtime-port` を渡すとその場で固定ポートも書き込む。新規プロジェクトの onboarding 用。
- `ProjectSettings/LiminalPalette.json` の JSON Schema (`Documentation~/schemas/LiminalPalette.schema.json`) を同梱。CLI が書き出すファイルに `$schema` 参照を自動付与するので、VS Code / JetBrains 系 IDE で `port` / `runtimePort` の autocomplete と範囲チェックが効く。`JsonUtility` は未知フィールドを無視するため `ProjectConfig` 側に影響なし (`Tests/Editor/Ipc/ProjectConfigTests.cs:GetPreferredPortAt_TolerantToUnknownFields` で回帰テスト)。
- `liminal run` がシナリオパスの glob (例: `liminal run 'Battle/*'`) に対応。`/api/v1/scenarios` を引いて `fnmatch` で一致するシナリオを順次実行し、最後にサマリ (`N scenarios, X passed, Y failed`) を表示する。1 つでも失敗すれば exit code 2。
- `liminal run --report PATH` で JUnit XML レポートを書き出し可能に。CI システム (GitHub Actions の test reporter / Jenkins JUnit Plugin 等) でそのまま読める形式。`<testcase>` ごとに失敗時は `<failure message="...">` + 失敗ステップの詳細を含む。`--json` と併用すると aggregate 形式 (`{scenarios:[...], total, passed, failed}`) も同時に得られる。
- AI Agent 用の Claude Code Skills を 8 個同梱 (`AISkills~/liminal-*/SKILL.md`): liminal-overview / liminal-find-port / liminal-list-commands / liminal-execute / liminal-get-state / liminal-get-logs / liminal-list-scenarios / liminal-run-scenario。
- Editor メニュー `Tools > LiminalPalette > Install AI Skills... / Uninstall AI Skills` を追加 (`Editor/AISkillsInstaller.cs`)。利用側プロジェクトの `.claude/skills/` への配布 / 削除に対応 (旧 `lp-*` skill ディレクトリも legacy として一掃)。
- `/api/v1/health` レスポンスに `projectName` / `projectPath` / `mode` を追加。`mode` は `"editor"` か `"runtime"` で、同一プロジェクト内で Editor IpcServer と Play Mode / Player の Runtime IpcServer を区別する。
- プロジェクト固定ポート設定 `<project>/ProjectSettings/LiminalPalette.json` を追加。`port` は Editor 用、`runtimePort` は Play Mode / Runtime 用 (省略時は `port` にフォールバック)。`Void2610.LiminalPalette.Ipc.ProjectConfig.GetPreferredPort()` / `GetPreferredRuntimePort()` から読む。複数 Unity プロジェクト + 同プロジェクト内 Editor/Play Mode を衝突なく同時起動できる。
- `liminal` CLI: 上記 preferred port を cwd / `--project` から読み、最優先候補として probe する。さらに `~/.liminal-palette/ports.json` (v2) に mode 別ポートをキャッシュして次回起動を高速化。
- `liminal` CLI: 複数 listener 同時起動時のターゲット指定として `--mode editor|runtime` フラグ、`--project` フラグ、`$LP_PROJECT` 環境変数、cwd からの Unity プロジェクト自動検出 (`ProjectSettings/ProjectVersion.txt`) を追加。
- `liminal doctor`: トークン / cwd 検出 / preferred port / キャッシュ / 生存ポート / 解決結果 を一覧表示する診断コマンド。`--prune-stale` で probe しても応答が無かった cache エントリを削除。
- `liminal project show` / `liminal project set-port [--runtime] <N>` / `liminal project unset-port [--runtime]`: cwd 配下のプロジェクト固定ポート設定を表示 / 編集するコマンド。`show` はライブ probe で listener の現在位置も表示する。

### Changed
- ランタイム asmdef を `Void2610.LiminalPalette.Player` → `Void2610.LiminalPalette.Runtime` にリネーム (フォルダも `Player/` → `Runtime/`)。サブ asmdef も `Runtime.Ipc` / `Runtime.InputSystem` に追従。Unity Package Manager の標準慣習 (`Runtime/`) に合わせると同時に、利用側ゲームの「プレイヤー (キャラクター)」ドメインと名前衝突を回避するため。**Breaking**: 利用側 asmdef の references で `Void2610.LiminalPalette.Player` を参照していた箇所は `Void2610.LiminalPalette.Runtime` に書き換え必須。
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
