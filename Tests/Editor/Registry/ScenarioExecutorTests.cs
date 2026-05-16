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
                => ShouldFail
                    ? CommandResult.Fail("simulated failure", null, System.Array.Empty<LogEntry>(), System.TimeSpan.Zero)
                    : CommandResult.Ok(null, System.Array.Empty<LogEntry>(), System.TimeSpan.Zero);
        }

        private sealed class FakeFrameWaiter : IFrameWaiter
        {
            public int LastFramesRequested;
            public Task WaitFramesAsync(int frames, CancellationToken ct)
            {
                LastFramesRequested = frames;
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
    }
}
