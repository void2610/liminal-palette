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
    }
}
