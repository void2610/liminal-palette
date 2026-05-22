using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// シナリオ実行中の進捗スナップショット。オーバーレイ UI などのリアルタイム表示用。
    /// StepIndex が -1 のときは「これから 1 ステップ目に入る」開始通知を表す。
    /// </summary>
    public readonly struct ScenarioProgress
    {
        public string Path { get; }
        public int StepIndex { get; }
        public int TotalSteps { get; }
        public ScenarioStep CurrentStep { get; }

        public ScenarioProgress(string path, int stepIndex, int totalSteps, ScenarioStep currentStep)
        {
            Path = path;
            StepIndex = stepIndex;
            TotalSteps = totalSteps;
            CurrentStep = currentStep;
        }
    }

    /// <summary>
    /// シナリオステップ列を順次実行して ScenarioResult を返す責務。
    /// fail-fast: 最初に失敗したステップで打ち切る。
    /// </summary>
    public sealed class ScenarioExecutor
    {
        // --- 進捗イベント (オーバーレイ UI など、シナリオ実行をリアルタイム追跡したい購読者向け) ---
        // AlreadyRunning で弾かれた呼び出しと、ステップ列構築前に失敗した呼び出しでは発火しない
        // (= 実際に 1 ステップでも回ったケースだけ Started/Finished が対になる)。
        // DomainReload 跨ぎの subscriber 残留対策として ResetStatics で null クリアする。

        /// <summary>シナリオ実行が開始された (最初のステップ実行直前)。StepIndex=-1。</summary>
        public static event Action<ScenarioProgress> ScenarioRunStarted;

        /// <summary>各ステップを実行する直前に発火。StepIndex は 0..TotalSteps-1。</summary>
        public static event Action<ScenarioProgress> ScenarioRunStepChanged;

        /// <summary>シナリオ実行が終了 (成功 / 失敗 / キャンセル)。キャンセル時は result=null。</summary>
        public static event Action<ScenarioResult> ScenarioRunFinished;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ScenarioRunStarted = null;
            ScenarioRunStepChanged = null;
            ScenarioRunFinished = null;
        }

        private readonly ICommandExecutor _commandExecutor;
        private readonly IObservableFieldRegistry _fieldRegistry;
        private readonly IFrameWaiter _frameWaiter;

        // 並行実行を防ぐ。シナリオ実行中に別シナリオが来たら拒否する。
        private readonly SemaphoreSlim _runLock = new SemaphoreSlim(1, 1);

        public ScenarioExecutor(
            ICommandExecutor commandExecutor,
            IObservableFieldRegistry fieldRegistry,
            IFrameWaiter frameWaiter)
        {
            _commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
            _fieldRegistry = fieldRegistry ?? throw new ArgumentNullException(nameof(fieldRegistry));
            _frameWaiter = frameWaiter ?? throw new ArgumentNullException(nameof(frameWaiter));
        }

        /// <summary>シナリオを実行中かどうか。UI で Run ボタンを disable する判定に使う。</summary>
        public bool IsRunning => _runLock.CurrentCount == 0;

        /// <summary>ステップ列を直接受け取って実行する (ad-hoc 実行 / HTTP)。</summary>
        public async Task<ScenarioResult> ExecuteAsync(
            IReadOnlyList<ScenarioStep> steps,
            string path,
            CancellationToken ct)
        {
            if (steps == null) throw new ArgumentNullException(nameof(steps));

            // すでに実行中なら即座に拒否する (待たない)。利用者は ScenarioResult.WasRejectedAsAlreadyRunning で判別。
            if (!await _runLock.WaitAsync(0, ct))
            {
                return ScenarioResult.AlreadyRunning(path);
            }
            try
            {
                return await ExecuteCoreAsync(steps, path, ct);
            }
            finally
            {
                _runLock.Release();
            }
        }

        /// <summary>登録済みシナリオを Path 指定で実行する。</summary>
        public async Task<ScenarioResult> ExecuteAsync(
            IScenarioRegistry registry,
            string scenarioPath,
            CancellationToken ct)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            var descriptor = registry.Find(scenarioPath);
            if (descriptor == null)
            {
                // 「シナリオ未登録」も 1 つの失敗結果として返す。
                var step = ScenarioStep.Run("<n/a>");
                return new ScenarioResult(
                    success: false,
                    steps: new[] { StepResult.Fail(step, $"Scenario not found: {scenarioPath}") },
                    duration: TimeSpan.Zero,
                    failedAtStep: 0,
                    path: scenarioPath);
            }

            // インスタンス解決 (static の場合は null を渡す)。
            object instance = null;
            if (!descriptor.IsStatic)
            {
                instance = LiminalPalette.InstanceResolver.Resolve(descriptor.DeclaringType);
                if (instance == null)
                {
                    var step = ScenarioStep.Run("<n/a>");
                    return new ScenarioResult(
                        success: false,
                        steps: new[] { StepResult.Fail(step, $"Instance not resolved for {descriptor.DeclaringType?.FullName}") },
                        duration: TimeSpan.Zero,
                        failedAtStep: 0,
                        path: scenarioPath);
                }
            }

            // 並行実行を禁じるため _runLock を先に取得する。StepsFactory の foreach は
            // yield ベースで副作用 (Debug.Log・状態変更・乱数等) を含み得るため、AlreadyRunning
            // で弾かれる側でステップ列を「先に」消費してしまうとシナリオ間排他が形骸化する。
            // 取得できなかった場合はステップ列を一切消費せず即座に拒否を返す。
            if (!await _runLock.WaitAsync(0, ct))
            {
                return ScenarioResult.AlreadyRunning(scenarioPath);
            }
            try
            {
                List<ScenarioStep> stepList;
                try
                {
                    stepList = new List<ScenarioStep>();
                    // [LiminalScenario(Scene=...)] が付いていれば、本体ステップの前に LoadScene を差し込む。
                    // 利用側は EnterTestScene のような毎シナリオの定型コードを書かなくて済む。
                    // 復帰 (元シーンへ戻す) はしない仕様 — 最後にロードされたシーンがそのまま残る。
                    if (!string.IsNullOrEmpty(descriptor.Scene))
                    {
                        stepList.Add(ScenarioStep.LoadScene(descriptor.Scene, $"auto: load {descriptor.Scene}"));
                    }
                    foreach (var s in descriptor.StepsFactory(instance))
                    {
                        if (s == null) continue;
                        stepList.Add(s);
                    }
                }
                catch (Exception ex)
                {
                    var step = ScenarioStep.Run("<n/a>");
                    return new ScenarioResult(
                        success: false,
                        steps: new[] { StepResult.Fail(step, $"failed to build steps: {ex.Message}") },
                        duration: TimeSpan.Zero,
                        failedAtStep: 0,
                        path: scenarioPath);
                }
                return await ExecuteCoreAsync(stepList, scenarioPath, ct);
            }
            finally
            {
                _runLock.Release();
            }
        }

        // 共通本体。lock 取得後に呼ばれる前提。
        private async Task<ScenarioResult> ExecuteCoreAsync(
            IReadOnlyList<ScenarioStep> steps,
            string path,
            CancellationToken ct)
        {
            var results = new List<StepResult>(steps.Count);
            var sw = Stopwatch.StartNew();
            var failedIndex = -1;
            ScenarioResult finalResult = null;

            // 開始通知。StepIndex=-1 はオーバーレイ側で「シナリオ起動直後」を表す。
            try { ScenarioRunStarted?.Invoke(new ScenarioProgress(path, -1, steps.Count, null)); }
            catch (Exception ex) { UnityEngine.Debug.LogWarning($"[LiminalPalette] ScenarioRunStarted handler threw: {ex}"); }

            try
            {
                for (var i = 0; i < steps.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var step = steps[i];
                    // 各ステップ実行直前にも進捗を通知。オーバーレイは「現在 N/M」を更新する用途。
                    try { ScenarioRunStepChanged?.Invoke(new ScenarioProgress(path, i, steps.Count, step)); }
                    catch (Exception ex) { UnityEngine.Debug.LogWarning($"[LiminalPalette] ScenarioRunStepChanged handler threw: {ex}"); }
                    var stepSw = Stopwatch.StartNew();
                    StepResult sr;
                    try
                    {
                        switch (step.Kind)
                        {
                            case ScenarioStepKind.Command:
                                sr = await RunCommandStep((CommandStep)step, ct);
                                break;
                            case ScenarioStepKind.WaitSeconds:
                                sr = await RunWaitSecondsStep((WaitStep)step, ct);
                                break;
                            case ScenarioStepKind.WaitFrames:
                                sr = await RunWaitFramesStep((WaitStep)step, ct);
                                break;
                            case ScenarioStepKind.AssertEquals:
                                sr = RunAssertStep((AssertStep)step, equals: true);
                                break;
                            case ScenarioStepKind.AssertNotEquals:
                                sr = RunAssertStep((AssertStep)step, equals: false);
                                break;
                            case ScenarioStepKind.LoadScene:
                                sr = await RunLoadSceneStep((LoadSceneStep)step, ct);
                                break;
                            case ScenarioStepKind.AssertCommandReturns:
                                sr = await RunAssertCommandReturnsStep((AssertCommandReturnsStep)step, ct);
                                break;
                            default:
                                sr = StepResult.Fail(step, $"unknown step kind: {step.Kind}");
                                break;
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        sr = StepResult.Fail(step, $"unexpected: {ex.Message}");
                    }
                    stepSw.Stop();
                    sr = sr.WithDuration(stepSw.Elapsed);
                    results.Add(sr);

                    if (!sr.Success)
                    {
                        failedIndex = i;
                        break;
                    }
                }
                sw.Stop();
                finalResult = new ScenarioResult(failedIndex < 0, results, sw.Elapsed, failedIndex, path);
                return finalResult;
            }
            finally
            {
                // 成功 / 失敗 / キャンセル いずれの経路でも必ず Finished を発火する
                // (オーバーレイの片付け漏れで枠線が出っぱなしになるのを防ぐ)。
                // OperationCanceledException で抜けた場合 finalResult は null のまま。
                try { ScenarioRunFinished?.Invoke(finalResult); }
                catch (Exception ex) { UnityEngine.Debug.LogWarning($"[LiminalPalette] ScenarioRunFinished handler threw: {ex}"); }
            }
        }

        private async Task<StepResult> RunCommandStep(CommandStep step, CancellationToken ct)
        {
            var result = await _commandExecutor.ExecuteWithTypedArgsAsync(step.CommandPath, step.Args, ct);
            return new StepResult(step, result.Success, result.Error, result, null, TimeSpan.Zero);
        }

        // AssertCommandReturns: 内部でコマンドを実行し、戻り値文字列が expected と一致するか検証する。
        // 失敗パターンは 2 通り:
        //   1) コマンド実行自体が失敗 (success=false) → そのまま fail
        //   2) コマンドは成功したが戻り値が expected と不一致 → fail (ordinal 比較)
        // expected==null は「コマンドが成功すれば OK」モード。
        private async Task<StepResult> RunAssertCommandReturnsStep(AssertCommandReturnsStep step, CancellationToken ct)
        {
            var result = await _commandExecutor.ExecuteWithTypedArgsAsync(step.CommandPath, step.Args, ct);
            if (!result.Success)
            {
                var sr = new StepResult(step, success: false,
                    error: $"command '{step.CommandPath}' failed: {result.Error ?? "<no error>"}",
                    commandResult: result, actualValue: null, duration: TimeSpan.Zero);
                return sr;
            }

            // expected が null の場合は戻り値内容を問わず成功扱い (コマンドの実行可否だけ確かめたいケース)。
            if (step.Expected == null)
            {
                return new StepResult(step, success: true, error: null,
                    commandResult: result, actualValue: result.Value, duration: TimeSpan.Zero);
            }

            var actual = result.Value == null ? "" : TypeConverterRegistry.ToDisplayString(result.Value);
            if (string.Equals(actual, step.Expected, StringComparison.Ordinal))
            {
                return new StepResult(step, success: true, error: null,
                    commandResult: result, actualValue: actual, duration: TimeSpan.Zero);
            }
            return new StepResult(step, success: false,
                error: $"command '{step.CommandPath}' returned '{actual}', expected '{step.Expected}'",
                commandResult: result, actualValue: actual, duration: TimeSpan.Zero);
        }

        private async Task<StepResult> RunWaitSecondsStep(WaitStep step, CancellationToken ct)
        {
            if (step.Seconds > 0f)
            {
                await Task.Delay(TimeSpan.FromSeconds(step.Seconds), ct);
            }
            return StepResult.Ok(step);
        }

        // 指定シーンを Single モードで非同期ロード。完了 (op.isDone) まで Task.Yield で待つ。
        // PlayMode 専用 (Edit Mode では Application.isPlaying=false なので Fail にする)。
        // Single モードで現シーンを置換するので、利用側 VContainer のスコープは作り直され、
        // 後続コマンドは自動的に新シーンの instance に解決される。
        private async Task<StepResult> RunLoadSceneStep(LoadSceneStep step, CancellationToken ct)
        {
            // 既にキャンセル要求が来ている場合は LoadSceneAsync を呼ばずに伝搬する。
            // 呼んでしまうと「キャンセル後に意図しないシーン切替が発生」する問題を防ぐ。
            ct.ThrowIfCancellationRequested();

            if (!Application.isPlaying)
                return StepResult.Fail(step, "LoadScene step is only supported in PlayMode");

            AsyncOperation op;
            try
            {
                op = SceneManager.LoadSceneAsync(step.SceneName, LoadSceneMode.Single);
            }
            catch (Exception ex)
            {
                return StepResult.Fail(step, $"LoadSceneAsync threw: {ex.Message}");
            }
            if (op == null)
                return StepResult.Fail(step, $"LoadSceneAsync returned null for '{step.SceneName}' (Build Settings に登録されているか確認)");

            while (!op.isDone)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }
            return StepResult.Ok(step);
        }

        private async Task<StepResult> RunWaitFramesStep(WaitStep step, CancellationToken ct)
        {
            await _frameWaiter.WaitFramesAsync(step.Frames, ct);
            return StepResult.Ok(step);
        }

        private StepResult RunAssertStep(AssertStep step, bool equals)
        {
            var d = _fieldRegistry.Find(step.ObservableFieldPath);
            if (d == null)
                return StepResult.Fail(step, $"ObservableField not found: {step.ObservableFieldPath}");

            // インスタンス解決。IsStatic な field は VContainer 登録不要 (静的 utility 想定) なので
            // Resolve を経由せず instance=null のまま ReadCurrent に渡す。
            // インスタンスフィールドで instance が null の場合は ReadCurrent 内部で例外になり下の catch で吸い上げる。
            var instance = d.IsStatic ? null : LiminalPalette.InstanceResolver.Resolve(d.DeclaringType);

            object actual;
            try
            {
                actual = d.ReadCurrent(instance);
            }
            catch (Exception ex)
            {
                return StepResult.Fail(step, $"failed to read {step.ObservableFieldPath}: {ex.Message}");
            }

            // expected が string なら ValueType に変換、そうでなければそのまま比較。
            // 比較対象が同じ型に揃わないと object.Equals が false になるため、ここで型を揃える。
            object expectedTyped = step.Expected;
            if (step.Expected is string s && d.ValueType != typeof(string))
            {
                if (!TypeConverterRegistry.TryConvert(s, d.ValueType, out expectedTyped, out var err))
                    return StepResult.Fail(step, $"cannot convert expected '{s}' to {d.ValueType.Name}: {err}");
            }

            var matches = object.Equals(actual, expectedTyped);
            var passed = equals ? matches : !matches;

            if (passed)
                return new StepResult(step, true, null, null, actual, TimeSpan.Zero);

            var actualStr = actual == null ? "null" : TypeConverterRegistry.ToDisplayString(actual);
            var expectedStr = expectedTyped == null ? "null" : TypeConverterRegistry.ToDisplayString(expectedTyped);
            var error = equals
                ? $"expected '{expectedStr}' but got '{actualStr}'"
                : $"expected NOT '{expectedStr}' but got '{actualStr}'";
            return new StepResult(step, false, error, null, actual, TimeSpan.Zero);
        }
    }
}
