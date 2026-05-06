using System;
using System.Collections.Generic;
using NUnit.Framework;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Tests.UI
{
    /// <summary>
    /// ScenarioInvocationRecorder のテスト。
    /// 各 Command ステップ + シナリオ全体の集約 1 件が InvocationStore に積まれることを検証する。
    /// </summary>
    public sealed class ScenarioInvocationRecorderTests
    {
        [SetUp]
        public void SetUp() => InvocationStore.Instance.Clear();

        [TearDown]
        public void TearDown() => InvocationStore.Instance.Clear();

        private static StepResult OkCommandStep(string path, IReadOnlyDictionary<string, object> args = null)
        {
            var step = ScenarioStep.Run(path, args);
            var cr = CommandResult.Ok(null, Array.Empty<LogEntry>(), TimeSpan.FromMilliseconds(1));
            return new StepResult(step, true, null, cr, null, TimeSpan.FromMilliseconds(1));
        }

        private static StepResult FailCommandStep(string path, string error)
        {
            var step = ScenarioStep.Run(path);
            var cr = CommandResult.Fail(error, null, Array.Empty<LogEntry>(), TimeSpan.FromMilliseconds(1));
            return new StepResult(step, false, error, cr, null, TimeSpan.FromMilliseconds(1));
        }

        [Test]
        public void Record_RecordsEachCommandStep_PlusAggregate()
        {
            var steps = new[]
            {
                OkCommandStep("Foo/A", new Dictionary<string, object> { ["x"] = 1 }),
                OkCommandStep("Foo/B"),
            };
            var result = new ScenarioResult(success: true, steps, TimeSpan.FromMilliseconds(5), failedAtStep: -1, path: "Test/Smoke");

            ScenarioInvocationRecorder.Record(result, "Test/Smoke");

            // 各 Command ステップ (2) + シナリオ集約 (1) = 3 件。すべて IsFromScenario=true。
            Assert.AreEqual(3, InvocationStore.Instance.Count);
            var entries = InvocationStore.Instance.Entries;
            Assert.AreEqual("Foo/A", entries[0].Path);
            Assert.AreEqual(1, entries[0].Args["x"]);
            Assert.IsTrue(entries[0].IsFromScenario, "シナリオ内 Command は IsFromScenario=true");
            Assert.AreEqual("Foo/B", entries[1].Path);
            Assert.IsTrue(entries[1].IsFromScenario);
            Assert.AreEqual("Scenario/Test/Smoke", entries[2].Path);
            Assert.IsTrue(entries[2].Result.Success);
            Assert.IsTrue(entries[2].IsFromScenario, "シナリオ集約も IsFromScenario=true");
        }

        [Test]
        public void Record_DefaultRecord_NotMarkedAsFromScenario()
        {
            // 通常のコマンド実行記録 (UI 経由 / HTTP 経由) は IsFromScenario=false のまま。
            InvocationStore.Instance.Record("Foo/Direct", null,
                CommandResult.Ok(null, Array.Empty<LogEntry>(), TimeSpan.Zero));
            var entries = InvocationStore.Instance.Entries;
            Assert.AreEqual(1, entries.Count);
            Assert.IsFalse(entries[0].IsFromScenario);
        }

        [Test]
        public void Record_FailedScenario_AggregateContainsFailureMessage()
        {
            var steps = new[]
            {
                OkCommandStep("Foo/A"),
                FailCommandStep("Foo/B", "boom"),
            };
            var result = new ScenarioResult(success: false, steps, TimeSpan.FromMilliseconds(3), failedAtStep: 1, path: "Test/Fails");

            ScenarioInvocationRecorder.Record(result);

            var entries = InvocationStore.Instance.Entries;
            Assert.AreEqual(3, entries.Count);
            var aggregate = entries[2];
            Assert.AreEqual("Scenario/Test/Fails", aggregate.Path);
            Assert.IsFalse(aggregate.Result.Success);
            StringAssert.Contains("Step 1", aggregate.Result.Error);
            StringAssert.Contains("boom", aggregate.Result.Error);
        }

        [Test]
        public void Record_AdHoc_NoPath_UsesAdHocLabel()
        {
            var result = new ScenarioResult(
                success: true,
                steps: new[] { OkCommandStep("Foo/A") },
                duration: TimeSpan.FromMilliseconds(1),
                failedAtStep: -1,
                path: null);

            ScenarioInvocationRecorder.Record(result);

            var entries = InvocationStore.Instance.Entries;
            Assert.AreEqual("Scenario/(ad-hoc)", entries[1].Path);
        }

        [Test]
        public void Record_AlreadyRunning_RecordsNothing()
        {
            var rejected = ScenarioResult.AlreadyRunning("Test/Busy");
            ScenarioInvocationRecorder.Record(rejected, "Test/Busy");
            Assert.AreEqual(0, InvocationStore.Instance.Count);
        }

        [Test]
        public void Record_SkipsWaitAndAssertSteps()
        {
            // Wait / Assert は CommandResult を持たないので個別記録対象外。集約のみ 1 件積まれる。
            var waitStep = new StepResult(ScenarioStep.WaitFrames(1), true, null, null, null, TimeSpan.Zero);
            var assertStep = new StepResult(ScenarioStep.AssertEquals("X", 1), true, null, null, 1, TimeSpan.Zero);
            var result = new ScenarioResult(true, new[] { waitStep, assertStep }, TimeSpan.Zero, -1, "Test/Pure");

            ScenarioInvocationRecorder.Record(result, "Test/Pure");

            var entries = InvocationStore.Instance.Entries;
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("Scenario/Test/Pure", entries[0].Path);
        }

        [Test]
        public void Record_NullResult_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => ScenarioInvocationRecorder.Record(null));
        }
    }
}
