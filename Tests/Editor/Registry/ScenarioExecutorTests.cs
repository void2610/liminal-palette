using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using R3;

namespace Void2610.LiminalPalette.Tests
{
    /// <summary>
    /// ScenarioExecutor の単体テスト。
    /// 偽の ICommandExecutor / IObservableFieldRegistry / IFrameWaiter を渡して挙動を検証する。
    /// </summary>
    public sealed class ScenarioExecutorTests
    {
        // ------------------------------------------------------------
        // テスト用フェイク
        // ------------------------------------------------------------

        private sealed class FakeCommandExecutor : ICommandExecutor
        {
            public int CallCount;
            public bool ShouldFail;
            // CallCount がこの値未満の間は instance 未解決失敗を返す (LoadScene 直後の DI 未構築を模擬)
            public int UnresolvedUntilCall;

            public Task<CommandResult> ExecuteAsync(string pathOrAlias, IReadOnlyDictionary<string, string> args, CancellationToken ct = default)
                => Task.FromResult(BuildResult());

            public Task<CommandResult> ExecuteAsync(string pathOrAlias, IReadOnlyList<string> positionalArgs, CancellationToken ct = default)
                => Task.FromResult(BuildResult());

            public Task<CommandResult> ExecuteWithTypedArgsAsync(string pathOrAlias, IReadOnlyDictionary<string, object> args, CancellationToken ct = default)
            {
                CallCount++;
                return Task.FromResult(BuildResult());
            }

            private CommandResult BuildResult()
            {
                if (CallCount < UnresolvedUntilCall)
                    return CommandResult.Fail("Instance not resolved for Fake",
                        new InstanceUnresolvedException("Instance not resolved for Fake"),
                        System.Array.Empty<LogEntry>(), System.TimeSpan.Zero);
                return ShouldFail
                    ? CommandResult.Fail("simulated failure", null, System.Array.Empty<LogEntry>(), System.TimeSpan.Zero)
                    : CommandResult.Ok(null, System.Array.Empty<LogEntry>(), System.TimeSpan.Zero);
            }
        }

        private sealed class FakeFrameWaiter : IFrameWaiter
        {
            public int LastFramesRequested;
            public int CallCount;
            // N 回目 (1 始まり) の WaitFramesAsync 呼び出し時に観測値を書き換える等に使う。
            public System.Action<int> OnWait;
            public Task WaitFramesAsync(int frames, CancellationToken ct)
            {
                LastFramesRequested = frames;
                CallCount++;
                OnWait?.Invoke(CallCount);
                return Task.CompletedTask;
            }
        }

        // ObservableField のテストは ObservableFieldRegistry.Default を直接使う方がシンプル。
        private sealed class FakeContainer
        {
            [LiminalObservableField("ScenarioTest/Hp")]
            public ReactiveProperty<int> Hp { get; } = new ReactiveProperty<int>(100);
        }

        private FakeContainer _fakeContainer;
        private sealed class FakeResolver : IInstanceResolver
        {
            public FakeContainer Container;
            public object Resolve(System.Type type)
            {
                if (type == typeof(FakeContainer)) return Container;
                return null;
            }
        }

        private FakeResolver _fakeResolver;
        private IInstanceResolver _previousResolver;

        [SetUp]
        public void SetUp()
        {
            ObservableFieldRegistry.Default.ClearForTest();
            ObservableFieldScanner.ScanAll();
            _fakeContainer = new FakeContainer();
            _fakeResolver = new FakeResolver { Container = _fakeContainer };
            _previousResolver = LiminalPalette.InstanceResolver;
            LiminalPalette.SetInstanceResolver(_fakeResolver);
        }

        [TearDown]
        public void TearDown()
        {
            LiminalPalette.SetInstanceResolver(_previousResolver);
            // ClearForTest だけだと、テスト後にドメインリロードまで Default の Field 群が消えたままになり、
            // 本物のシナリオが ObservableField を Find できなくなる。Bootstrap と同等まで戻す。
            ObservableFieldRegistry.Default.ClearForTest();
            ObservableFieldScanner.ScanAll();
        }

        // ------------------------------------------------------------
        // Command ステップ
        // ------------------------------------------------------------

        [Test]
        public async Task Execute_CommandSuccess_ReturnsSuccess()
        {
            var ce = new FakeCommandExecutor();
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(new[] { ScenarioStep.Run("Foo/Bar") }, "test", CancellationToken.None);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.Steps.Count);
            Assert.IsTrue(result.Steps[0].Success);
            Assert.AreEqual(1, ce.CallCount);
        }

        [Test]
        public async Task Execute_CommandFailure_StopsImmediately()
        {
            var ce = new FakeCommandExecutor { ShouldFail = true };
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.Run("Foo/Bar"), ScenarioStep.Run("Baz/Qux") },
                "test", CancellationToken.None);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, result.Steps.Count, "fail-fast: 2 件目は実行されない");
            Assert.AreEqual(0, result.FailedAtStep);
            Assert.AreEqual(1, ce.CallCount);
        }

        [Test]
        public async Task Execute_CommandInstanceUnresolved_RetriesUntilResolved()
        {
            // LoadScene 直後の DI 構築レースを模擬: 3 回目の呼び出しで instance が解決される
            var ce = new FakeCommandExecutor { UnresolvedUntilCall = 3 };
            var fw = new FakeFrameWaiter();
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, fw);
            var result = await ex.ExecuteAsync(new[] { ScenarioStep.Run("Foo/Bar") }, "test", CancellationToken.None);
            Assert.IsTrue(result.Success, "未解決が解消したら成功する");
            Assert.AreEqual(3, ce.CallCount, "解決するまでリトライする");
            Assert.AreEqual(2, fw.CallCount, "リトライ間はフレーム待ちする");
        }

        // ------------------------------------------------------------
        // Wait ステップ
        // ------------------------------------------------------------

        [Test]
        public async Task Execute_WaitFrames_DelegatesToFrameWaiter()
        {
            var ce = new FakeCommandExecutor();
            var fw = new FakeFrameWaiter();
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, fw);
            var result = await ex.ExecuteAsync(new[] { ScenarioStep.WaitFrames(5) }, null, CancellationToken.None);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(5, fw.LastFramesRequested);
        }

        [Test]
        public async Task Execute_WaitSeconds_Zero_CompletesImmediately()
        {
            var ce = new FakeCommandExecutor();
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(new[] { ScenarioStep.WaitSeconds(0f) }, null, CancellationToken.None);
            Assert.IsTrue(result.Success);
        }

        // ------------------------------------------------------------
        // Assert ステップ
        // ------------------------------------------------------------

        [Test]
        public async Task Execute_AssertEquals_Passes()
        {
            var ce = new FakeCommandExecutor();
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.AssertEquals("ScenarioTest/Hp", 100) },
                null, CancellationToken.None);
            Assert.IsTrue(result.Success);
        }

        [Test]
        public async Task Execute_AssertEquals_StringConvertedToValueType()
        {
            var ce = new FakeCommandExecutor();
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            // string "100" を int に変換して比較するパス。
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.AssertEquals("ScenarioTest/Hp", "100") },
                null, CancellationToken.None);
            Assert.IsTrue(result.Success);
        }

        [Test]
        public async Task Execute_AssertEquals_Fails()
        {
            var ce = new FakeCommandExecutor();
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.AssertEquals("ScenarioTest/Hp", 999) },
                null, CancellationToken.None);
            Assert.IsFalse(result.Success);
            StringAssert.Contains("999", result.Steps[0].Error);
            StringAssert.Contains("100", result.Steps[0].Error);
        }

        [Test]
        public async Task Execute_AssertNotEquals_Passes()
        {
            var ce = new FakeCommandExecutor();
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.AssertNotEquals("ScenarioTest/Hp", 0) },
                null, CancellationToken.None);
            Assert.IsTrue(result.Success);
        }

        [Test]
        public async Task Execute_AssertOnUnknownField_Fails()
        {
            var ce = new FakeCommandExecutor();
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.AssertEquals("Missing/Field", 0) },
                null, CancellationToken.None);
            Assert.IsFalse(result.Success);
            StringAssert.Contains("not found", result.Steps[0].Error);
        }

        // ------------------------------------------------------------
        // AssertEventually ステップ
        // ------------------------------------------------------------

        [Test]
        public async Task Execute_AssertEventually_PassesWhenValueReachesExpected()
        {
            // 初期値 100。3 回目のポーリング (WaitFramesAsync) 時に 42 へ書き換わり、
            // 次の再評価で一致 → 成功する。固定待ちなしで遅延確定値を拾えることを確認。
            var ce = new FakeCommandExecutor();
            var fw = new FakeFrameWaiter
            {
                OnWait = n => { if (n >= 3) _fakeContainer.Hp.Value = 42; }
            };
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, fw);
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.AssertEventually("ScenarioTest/Hp", 42, 5f) },
                null, CancellationToken.None);
            Assert.IsTrue(result.Success, result.Steps[0].Error);
            Assert.GreaterOrEqual(fw.CallCount, 3, "一致前に複数回ポーリングしているはず");
        }

        [Test]
        public async Task Execute_AssertEventually_TimesOut_Fails()
        {
            // 値が期待値に到達しないままタイムアウトするケース。
            // FakeFrameWaiter は即時完了するため実時間ベースの timeout (0.05s) で打ち切られる。
            // 万一 timeout 判定が壊れて無限ループになってもテストをハングさせないよう、
            // 呼び出し回数が異常に増えたら CancellationToken で打ち切る安全網を張る。
            var ce = new FakeCommandExecutor();
            var cts = new CancellationTokenSource();
            var fw = new FakeFrameWaiter
            {
                OnWait = n => { if (n > 1_000_000) cts.Cancel(); }
            };
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, fw);
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.AssertEventually("ScenarioTest/Hp", 999, 0.05f) },
                null, cts.Token);
            Assert.IsFalse(result.Success);
            StringAssert.Contains("not satisfied within", result.Steps[0].Error);
            StringAssert.Contains("999", result.Steps[0].Error);
            StringAssert.Contains("100", result.Steps[0].Error);
        }

        [Test]
        public async Task Execute_AssertEventually_UnknownField_FailsImmediatelyWithoutPolling()
        {
            // ObservableField 未登録は「待っても解決しない構成エラー」なので、タイムアウトを待たず即時失敗。
            var ce = new FakeCommandExecutor();
            var fw = new FakeFrameWaiter();
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, fw);
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.AssertEventually("Missing/Field", 0, 5f) },
                null, CancellationToken.None);
            Assert.IsFalse(result.Success);
            StringAssert.Contains("not found", result.Steps[0].Error);
            Assert.AreEqual(0, fw.CallCount, "構成エラーはポーリングせず即時失敗する");
        }

        [Test]
        public async Task Execute_AssertEventually_ConversionError_FailsImmediatelyWithoutPolling()
        {
            // 型変換失敗 (int field に変換不能な string) も構成エラーとして即時失敗。
            var ce = new FakeCommandExecutor();
            var fw = new FakeFrameWaiter();
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, fw);
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.AssertEventually("ScenarioTest/Hp", "not-an-int", 5f) },
                null, CancellationToken.None);
            Assert.IsFalse(result.Success);
            StringAssert.Contains("convert", result.Steps[0].Error);
            Assert.AreEqual(0, fw.CallCount, "変換失敗はポーリングせず即時失敗する");
        }

        // ------------------------------------------------------------
        // LoadScene ステップ
        // ------------------------------------------------------------

        [Test]
        public async Task Execute_LoadScene_FailsInEditMode()
        {
            // EditMode (Application.isPlaying=false) では LoadScene は明示的に失敗させる仕様。
            // PlayMode 内での実シーン読込検証は別途 PlayMode テストで担保する。
            var ce = new FakeCommandExecutor();
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.LoadScene("NoSuchScene") },
                null, CancellationToken.None);
            Assert.IsFalse(result.Success);
            StringAssert.Contains("PlayMode", result.Steps[0].Error);
        }

        // ------------------------------------------------------------
        // AssertCommandReturns ステップ
        // ------------------------------------------------------------

        // FakeCommandExecutor を継承して任意の戻り値文字列を返す版。
        private sealed class FakeCommandExecutorWithValue : ICommandExecutor
        {
            public string ReturnValue;
            public bool ShouldFail;
            public Task<CommandResult> ExecuteAsync(string p, IReadOnlyDictionary<string, string> a, CancellationToken ct = default)
                => Task.FromResult(Build());
            public Task<CommandResult> ExecuteAsync(string p, IReadOnlyList<string> a, CancellationToken ct = default)
                => Task.FromResult(Build());
            public Task<CommandResult> ExecuteWithTypedArgsAsync(string p, IReadOnlyDictionary<string, object> a, CancellationToken ct = default)
                => Task.FromResult(Build());
            private CommandResult Build()
                => ShouldFail
                    ? CommandResult.Fail("simulated", null, System.Array.Empty<LogEntry>(), System.TimeSpan.Zero)
                    : CommandResult.Ok(ReturnValue, System.Array.Empty<LogEntry>(), System.TimeSpan.Zero);
        }

        [Test]
        public async Task Execute_AssertCommandReturns_ExpectedMatches_Passes()
        {
            var ce = new FakeCommandExecutorWithValue { ReturnValue = "ok" };
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.AssertCommandReturns("Foo/Bar", expected: "ok") },
                null, CancellationToken.None);
            Assert.IsTrue(result.Success, result.Steps[0].Error);
        }

        [Test]
        public async Task Execute_AssertCommandReturns_ExpectedMismatch_Fails()
        {
            var ce = new FakeCommandExecutorWithValue { ReturnValue = "actual_value" };
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.AssertCommandReturns("Foo/Bar", expected: "expected_value") },
                null, CancellationToken.None);
            Assert.IsFalse(result.Success);
            StringAssert.Contains("actual_value", result.Steps[0].Error);
            StringAssert.Contains("expected_value", result.Steps[0].Error);
        }

        [Test]
        public async Task Execute_AssertCommandReturns_NullExpected_PassesWhenSucceeds()
        {
            // expected=null モード: 戻り値内容を問わず実行成功なら pass。
            var ce = new FakeCommandExecutorWithValue { ReturnValue = "whatever" };
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.AssertCommandReturns("Foo/Bar", expected: null) },
                null, CancellationToken.None);
            Assert.IsTrue(result.Success);
        }

        [Test]
        public async Task Execute_AssertCommandReturns_CommandFails_StepFails()
        {
            var ce = new FakeCommandExecutorWithValue { ShouldFail = true };
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.AssertCommandReturns("Foo/Bar", expected: "ok") },
                null, CancellationToken.None);
            Assert.IsFalse(result.Success);
            StringAssert.Contains("command", result.Steps[0].Error);
        }

        [Test]
        public async Task Execute_AssertCommandEventually_PassesWhenCommandReachesExpected()
        {
            var ce = new FakeCommandExecutorWithValue { ReturnValue = "wait" };
            var fw = new FakeFrameWaiter { OnWait = n => { if (n >= 3) ce.ReturnValue = "ready"; } };
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, fw);
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.AssertCommandEventually("Foo/Bar", expected: "ready", timeoutSeconds: 5f) },
                null, CancellationToken.None);
            Assert.IsTrue(result.Success, result.Steps[0].Error);
            Assert.GreaterOrEqual(fw.CallCount, 3, "一致前に複数回ポーリングしているはず");
        }

        [Test]
        public async Task Execute_AssertCommandEventually_TimesOut_Fails()
        {
            var ce = new FakeCommandExecutorWithValue { ReturnValue = "never" };
            var cts = new CancellationTokenSource();
            var fw = new FakeFrameWaiter { OnWait = n => { if (n > 1_000_000) cts.Cancel(); } };
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, fw);
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.AssertCommandEventually("Foo/Bar", expected: "ready", timeoutSeconds: 0.05f) },
                null, cts.Token);
            Assert.IsFalse(result.Success);
            StringAssert.Contains("not satisfied within", result.Steps[0].Error);
            StringAssert.Contains("ready", result.Steps[0].Error);
        }

        [Test]
        public async Task Execute_AssertCommandEventually_NullExpected_PassesWhenSucceeds()
        {
            var ce = new FakeCommandExecutorWithValue { ReturnValue = "whatever" };
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.AssertCommandEventually("Foo/Bar", expected: null, timeoutSeconds: 5f) },
                null, CancellationToken.None);
            Assert.IsTrue(result.Success);
        }

        [Test]
        public async Task Execute_AssertCommandEventually_KeepsPollingWhileCommandFails_ThenSucceeds()
        {
            // AssertCommandReturns と異なり、コマンド失敗を即 fail にせずポーリング継続する契約を守っているか。
            var ce = new FakeCommandExecutorWithValue { ShouldFail = true, ReturnValue = "ready" };
            var fw = new FakeFrameWaiter { OnWait = n => { if (n >= 2) ce.ShouldFail = false; } };
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, fw);
            var result = await ex.ExecuteAsync(
                new[] { ScenarioStep.AssertCommandEventually("Foo/Bar", expected: "ready", timeoutSeconds: 5f) },
                null, CancellationToken.None);
            Assert.IsTrue(result.Success, result.Steps[0].Error);
        }

        [Test]
        public async Task ExecuteByPath_AutoPrependsLoadSceneWhenAttributeHasScene()
        {
            // [LiminalScenario(Scene="...")] が付いていれば、ScenarioExecutor が本体ステップの前に
            // LoadScene ステップを自動で 1 つ差し込む。EditMode では LoadScene 自体が失敗するので
            // 「最初のステップが LoadScene で、それが PlayMode 専用エラーで fail」を観測することで
            // 前置きが行われた事実を確認する。
            var registry = new ScenarioRegistry();
            var descriptor = new ScenarioDescriptor(
                path: "TestScenario/WithScene",
                description: "",
                declaringType: null,
                method: null,
                isStatic: true,
                stepsFactory: _ => new[] { ScenarioStep.Run("Test/NoArg") },
                scene: "MyScene");
            registry.Register(descriptor);

            var ce = new FakeCommandExecutor();
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(registry, "TestScenario/WithScene", CancellationToken.None);

            Assert.IsFalse(result.Success, "EditMode で LoadScene は失敗するのでシナリオも失敗");
            Assert.AreEqual(0, result.FailedAtStep, "失敗位置は先頭 (= 自動前置きされた LoadScene)");
            Assert.AreEqual(ScenarioStepKind.LoadScene, result.Steps[0].Step.Kind);
            Assert.AreEqual(0, ce.CallCount, "LoadScene 失敗で本体 Run は呼ばれない");
        }

        [Test]
        public async Task ExecuteByPath_ReadyWhen_PrependsAssertEventuallyBeforeBody()
        {
            // 条件成立 (初期値 100 と一致) で本体 Run へ進むことで、前置きの位置と通過を確認する。
            var registry = new ScenarioRegistry();
            registry.Register(new ScenarioDescriptor(
                path: "TestScenario/WithReady",
                description: "",
                declaringType: null,
                method: null,
                isStatic: true,
                stepsFactory: _ => new[] { ScenarioStep.Run("Test/NoArg") },
                readyWhen: "ScenarioTest/Hp=100"));

            var ce = new FakeCommandExecutor();
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(registry, "TestScenario/WithReady", CancellationToken.None);

            Assert.IsTrue(result.Success, result.Success ? null : result.Steps[result.FailedAtStep].Error);
            Assert.AreEqual(2, result.Steps.Count, "AssertEventually + 本体 Run の 2 ステップ");
            Assert.AreEqual(ScenarioStepKind.AssertEventually, result.Steps[0].Step.Kind);
            Assert.AreEqual(1, ce.CallCount, "条件成立後に本体 Run が 1 回呼ばれる");
        }

        [Test]
        public async Task ExecuteByPath_Setup_RunsAfterReadyWhenAndBeforeBody()
        {
            // Setup は ReadyWhen (条件成立) の後・本体の前に 1 回だけ Run される。
            var registry = new ScenarioRegistry();
            registry.Register(new ScenarioDescriptor(
                path: "TestScenario/WithSetup",
                description: "",
                declaringType: null,
                method: null,
                isStatic: true,
                stepsFactory: _ => new[] { ScenarioStep.Run("Test/NoArg") },
                readyWhen: "ScenarioTest/Hp=100",
                setup: "Test/Setup"));

            var ce = new FakeCommandExecutor();
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(registry, "TestScenario/WithSetup", CancellationToken.None);

            Assert.IsTrue(result.Success, result.Success ? null : result.Steps[result.FailedAtStep].Error);
            Assert.AreEqual(3, result.Steps.Count, "AssertEventually + Setup + 本体 Run の 3 ステップ");
            Assert.AreEqual(ScenarioStepKind.AssertEventually, result.Steps[0].Step.Kind);
            Assert.AreEqual(ScenarioStepKind.Command, result.Steps[1].Step.Kind);
            Assert.AreEqual("Test/Setup", ((CommandStep)result.Steps[1].Step).CommandPath, "Setup が本体より先に実行される");
            Assert.AreEqual(2, ce.CallCount, "Setup と本体で Run が 2 回呼ばれる");
        }

        [Test]
        public async Task ExecuteByPath_ReuseScene_SkipsLoadAndRunsSetupBeforeReadyWhen()
        {
            // Scene が既にアクティブ (SceneHook で偽装) なら LoadScene を省略し、Setup → ReadyWhen → 本体の順になる。
            ScenarioExecutor.SceneHook.GetActiveSceneName = () => "MyScene";
            try
            {
                var registry = new ScenarioRegistry();
                registry.Register(new ScenarioDescriptor(
                    path: "TestScenario/Reuse",
                    description: "",
                    declaringType: null,
                    method: null,
                    isStatic: true,
                    stepsFactory: _ => new[] { ScenarioStep.Run("Test/NoArg") },
                    scene: "MyScene",
                    readyWhen: "ScenarioTest/Hp=100",
                    reuseScene: true,
                    setup: "Test/Setup"));

                var ce = new FakeCommandExecutor();
                var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
                var result = await ex.ExecuteAsync(registry, "TestScenario/Reuse", CancellationToken.None);

                Assert.IsTrue(result.Success, result.Success ? null : result.Steps[result.FailedAtStep].Error);
                Assert.AreEqual(3, result.Steps.Count, "Setup + AssertEventually + 本体 Run (LoadScene は省略)");
                Assert.AreEqual("Test/Setup", ((CommandStep)result.Steps[0].Step).CommandPath, "再利用時は Setup が先");
                Assert.AreEqual(ScenarioStepKind.AssertEventually, result.Steps[1].Step.Kind);
                Assert.AreEqual(2, ce.CallCount);

                // 別シーンがアクティブなら再利用せず、先頭に LoadScene が入る (EditMode では PlayMode 専用エラーで止まる)
                ScenarioExecutor.SceneHook.GetActiveSceneName = () => "OtherScene";
                var loaded = await ex.ExecuteAsync(registry, "TestScenario/Reuse", CancellationToken.None);
                Assert.IsFalse(loaded.Success);
                Assert.AreEqual(ScenarioStepKind.LoadScene, loaded.Steps[0].Step.Kind);
            }
            finally
            {
                ScenarioExecutor.SceneHook.ResetToDefault();
            }
        }

        [Test]
        public async Task Execute_LoadScene_SkipsWhenSceneAlreadyActiveAndSkipIfActive()
        {
            // skipIfActive=true で既にアクティブなシーン名なら、EditMode でもロードせず成功扱いになる
            // (PlayMode 専用エラーより先に判定される)。アクティブシーン名は SceneHook で差し替える。
            ScenarioExecutor.SceneHook.GetActiveSceneName = () => "ActiveScene";
            try
            {
                var ce = new FakeCommandExecutor();
                var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
                var result = await ex.ExecuteAsync(
                    new[] { ScenarioStep.LoadScene("ActiveScene", skipIfActive: true), ScenarioStep.Run("Test/NoArg") },
                    null, CancellationToken.None);

                Assert.IsTrue(result.Success, result.Success ? null : result.Steps[result.FailedAtStep].Error);
                Assert.AreEqual(ScenarioStepKind.LoadScene, result.Steps[0].Step.Kind);
                Assert.AreEqual(1, ce.CallCount, "ロード省略後に本体 Run が呼ばれる");

                // 別のシーンがアクティブなら省略されず、EditMode では PlayMode 専用エラーで失敗する
                var other = await ex.ExecuteAsync(
                    new[] { ScenarioStep.LoadScene("OtherScene", skipIfActive: true) },
                    null, CancellationToken.None);
                Assert.IsFalse(other.Success);
                StringAssert.Contains("PlayMode", other.Steps[0].Error);
            }
            finally
            {
                ScenarioExecutor.SceneHook.ResetToDefault();
            }
        }

        [Test]
        public async Task ExecuteByPath_InvalidReadyWhen_FailsWithoutRunningBody()
        {
            // '=' を含まない ReadyWhen は構成エラーとしてステップ実行前に失敗する。
            var registry = new ScenarioRegistry();
            registry.Register(new ScenarioDescriptor(
                path: "TestScenario/BrokenReady",
                description: "",
                declaringType: null,
                method: null,
                isStatic: true,
                stepsFactory: _ => new[] { ScenarioStep.Run("Test/NoArg") },
                readyWhen: "ScenarioTest/Hp"));

            var ce = new FakeCommandExecutor();
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(registry, "TestScenario/BrokenReady", CancellationToken.None);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("invalid ReadyWhen", result.Steps[0].Error);
            Assert.AreEqual(0, ce.CallCount, "構成エラーで本体 Run は呼ばれない");
        }

        // TimeScaleHook を偽装して PlayMode 相当の適用・復元ロジックを EditMode で検証するヘルパ。
        private sealed class FakeTimeScale : System.IDisposable
        {
            public float Current = 1f;
            public readonly List<float> SetHistory = new List<float>();

            public FakeTimeScale(bool isPlaying)
            {
                ScenarioExecutor.TimeScaleHook.IsPlaying = () => isPlaying;
                ScenarioExecutor.TimeScaleHook.Get = () => Current;
                ScenarioExecutor.TimeScaleHook.Set = v => { Current = v; SetHistory.Add(v); };
            }

            public void Dispose() => ScenarioExecutor.TimeScaleHook.ResetToDefault();
        }

        private static ScenarioDescriptor TimeScaleDescriptor(System.Func<object, IEnumerable<ScenarioStep>> steps)
            => new ScenarioDescriptor(
                path: "TestScenario/WithTimeScale",
                description: "",
                declaringType: null,
                method: null,
                isStatic: true,
                stepsFactory: steps,
                timeScale: 20f);

        [Test]
        public async Task ExecuteByPath_TimeScale_AppliesAndRestoresOnSuccess()
        {
            using var fake = new FakeTimeScale(isPlaying: true);
            fake.Current = 0.5f;
            var registry = new ScenarioRegistry();
            registry.Register(TimeScaleDescriptor(_ => new[] { ScenarioStep.Run("Test/NoArg") }));

            var ce = new FakeCommandExecutor();
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(registry, "TestScenario/WithTimeScale", CancellationToken.None);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(new List<float> { 20f, 0.5f }, fake.SetHistory, "実行前に 20 へ上書きし、終了時に元値 0.5 へ復元");
        }

        [Test]
        public async Task ExecuteByPath_TimeScale_RestoresOnFailure()
        {
            using var fake = new FakeTimeScale(isPlaying: true);
            fake.Current = 1f;
            var registry = new ScenarioRegistry();
            registry.Register(TimeScaleDescriptor(_ => new[] { ScenarioStep.Run("Test/NoArg") }));

            var ce = new FakeCommandExecutor { ShouldFail = true };
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(registry, "TestScenario/WithTimeScale", CancellationToken.None);

            Assert.IsFalse(result.Success, "ステップ失敗でシナリオは失敗");
            Assert.AreEqual(1f, fake.Current, "失敗経路でも finally で元値へ復元");
        }

        [Test]
        public async Task ExecuteByPath_TimeScale_NotAppliedInEditMode()
        {
            using var fake = new FakeTimeScale(isPlaying: false);
            fake.Current = 1f;
            var registry = new ScenarioRegistry();
            registry.Register(TimeScaleDescriptor(_ => new[] { ScenarioStep.Run("Test/NoArg") }));

            var ce = new FakeCommandExecutor();
            var ex = new ScenarioExecutor(ce, ObservableFieldRegistry.Default, new FakeFrameWaiter());
            var result = await ex.ExecuteAsync(registry, "TestScenario/WithTimeScale", CancellationToken.None);

            Assert.IsTrue(result.Success);
            Assert.IsEmpty(fake.SetHistory, "isPlaying=false では timeScale を一切触らない");
        }
    }
}
