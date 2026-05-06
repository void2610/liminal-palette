using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// シナリオステップ列を順次実行して ScenarioResult を返す責務。
    /// fail-fast: 最初に失敗したステップで打ち切る。
    /// </summary>
    public sealed class ScenarioExecutor
    {
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

            for (var i = 0; i < steps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var step = steps[i];
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
            return new ScenarioResult(failedIndex < 0, results, sw.Elapsed, failedIndex, path);
        }

        private async Task<StepResult> RunCommandStep(CommandStep step, CancellationToken ct)
        {
            var result = await _commandExecutor.ExecuteWithTypedArgsAsync(step.CommandPath, step.Args, ct);
            return new StepResult(step, result.Success, result.Error, result, null, TimeSpan.Zero);
        }

        private async Task<StepResult> RunWaitSecondsStep(WaitStep step, CancellationToken ct)
        {
            if (step.Seconds > 0f)
            {
                await Task.Delay(TimeSpan.FromSeconds(step.Seconds), ct);
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

            // インスタンス解決。static なフィールドは現状の ObservableField スキャナでも対象になり得るが、
            // ReadCurrent は instance を null で渡しても動くので一旦 null 許容。
            var instance = LiminalPalette.InstanceResolver.Resolve(d.DeclaringType);
            // instance が null でも static フィールドなら ReadCurrent(null) で値が取れることがある。
            // インスタンスフィールドで instance が null の場合は ReadCurrent 内部で例外になり下の catch で吸い上げる。

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
