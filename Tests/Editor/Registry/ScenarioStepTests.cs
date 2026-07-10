using System.Collections.Generic;
using NUnit.Framework;

namespace Void2610.LiminalPalette.Tests
{
    public sealed class ScenarioStepTests
    {
        [Test]
        public void Run_BuildsCommandStepWithKindAndPath()
        {
            var step = ScenarioStep.Run("Foo/Bar", new Dictionary<string, object> { { "x", 1 } }, "desc");
            Assert.AreEqual(ScenarioStepKind.Command, step.Kind);
            Assert.AreEqual("desc", step.Description);
            // 派生型は internal だが、Tests アセンブリは InternalsVisibleTo されているので確認できる。
            Assert.IsInstanceOf<CommandStep>(step);
            var cs = (CommandStep)step;
            Assert.AreEqual("Foo/Bar", cs.CommandPath);
            Assert.AreEqual(1, cs.Args["x"]);
        }

        [Test]
        public void Run_WithNullArgs_TreatedAsEmptyDict()
        {
            var step = (CommandStep)ScenarioStep.Run("Foo/Bar");
            Assert.IsNotNull(step.Args);
            Assert.AreEqual(0, step.Args.Count);
        }

        [Test]
        public void WaitSeconds_BuildsWaitStep()
        {
            var step = ScenarioStep.WaitSeconds(0.5f, "wait half");
            Assert.AreEqual(ScenarioStepKind.WaitSeconds, step.Kind);
            Assert.AreEqual("wait half", step.Description);
            Assert.AreEqual(0.5f, ((WaitStep)step).Seconds);
        }

        [Test]
        public void WaitFrames_BuildsWaitStep()
        {
            var step = ScenarioStep.WaitFrames(3);
            Assert.AreEqual(ScenarioStepKind.WaitFrames, step.Kind);
            Assert.AreEqual(3, ((WaitStep)step).Frames);
        }

        [Test]
        public void AssertEquals_BuildsAssertStep()
        {
            var step = ScenarioStep.AssertEquals("Hp", 100);
            Assert.AreEqual(ScenarioStepKind.AssertEquals, step.Kind);
            var a = (AssertStep)step;
            Assert.AreEqual("Hp", a.ObservableFieldPath);
            Assert.AreEqual(100, a.Expected);
        }

        [Test]
        public void AssertNotEquals_BuildsAssertStep()
        {
            var step = ScenarioStep.AssertNotEquals("Hp", 0);
            Assert.AreEqual(ScenarioStepKind.AssertNotEquals, step.Kind);
        }

        [Test]
        public void AssertEventually_BuildsAssertEventuallyStep()
        {
            var step = ScenarioStep.AssertEventually("Game/State", "WorldMap", 3f, "boot");
            Assert.AreEqual(ScenarioStepKind.AssertEventually, step.Kind);
            Assert.AreEqual("boot", step.Description);
            var a = (AssertEventuallyStep)step;
            Assert.AreEqual("Game/State", a.ObservableFieldPath);
            Assert.AreEqual("WorldMap", a.Expected);
            Assert.AreEqual(3f, a.TimeoutSeconds);
        }

        [Test]
        public void AssertEventually_RejectsEmptyPathAndNonPositiveTimeout()
        {
            Assert.Throws<System.ArgumentException>(() => ScenarioStep.AssertEventually("", "x"));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ScenarioStep.AssertEventually("Hp", 1, 0f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ScenarioStep.AssertEventually("Hp", 1, -1f));
        }

        [Test]
        public void AssertEventually_RejectsNonFiniteTimeout()
        {
            // NaN / Infinity は TimeSpan.FromSeconds で例外になる「待っても解決しない」値なので Factory で弾く。
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ScenarioStep.AssertEventually("Hp", 1, float.NaN));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ScenarioStep.AssertEventually("Hp", 1, float.PositiveInfinity));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ScenarioStep.AssertEventually("Hp", 1, float.NegativeInfinity));
        }

        [Test]
        public void Run_RejectsEmptyPath()
        {
            Assert.Throws<System.ArgumentException>(() => ScenarioStep.Run(""));
            Assert.Throws<System.ArgumentException>(() => ScenarioStep.Run(null));
        }

        [Test]
        public void Wait_RejectsNegative()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ScenarioStep.WaitSeconds(-1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ScenarioStep.WaitFrames(-1));
        }

        [Test]
        public void LoadScene_BuildsLoadSceneStep()
        {
            var step = ScenarioStep.LoadScene("TestScene", "to test scene");
            Assert.AreEqual(ScenarioStepKind.LoadScene, step.Kind);
            Assert.AreEqual("to test scene", step.Description);
            var ls = (LoadSceneStep)step;
            Assert.AreEqual("TestScene", ls.SceneName);
        }

        [Test]
        public void LoadScene_RejectsEmptyName()
        {
            Assert.Throws<System.ArgumentException>(() => ScenarioStep.LoadScene(""));
            Assert.Throws<System.ArgumentException>(() => ScenarioStep.LoadScene(null));
        }

        [Test]
        public void AssertCommandReturns_BuildsStep()
        {
            var step = ScenarioStep.AssertCommandReturns(
                "Foo/Bar",
                new Dictionary<string, object> { ["x"] = 1 },
                expected: "ok",
                description: "test");
            Assert.AreEqual(ScenarioStepKind.AssertCommandReturns, step.Kind);
            Assert.AreEqual("test", step.Description);
            var s = (AssertCommandReturnsStep)step;
            Assert.AreEqual("Foo/Bar", s.CommandPath);
            Assert.AreEqual("ok", s.Expected);
            Assert.AreEqual(1, s.Args["x"]);
        }

        [Test]
        public void AssertCommandReturns_NullExpected_Allowed()
        {
            // expected=null は「コマンド成功すれば OK」モードとして許可されている。
            var step = (AssertCommandReturnsStep)ScenarioStep.AssertCommandReturns("Foo/Bar");
            Assert.IsNull(step.Expected);
            Assert.IsNotNull(step.Args);
            Assert.AreEqual(0, step.Args.Count);
        }

        [Test]
        public void AssertCommandReturns_RejectsEmptyPath()
        {
            Assert.Throws<System.ArgumentException>(() => ScenarioStep.AssertCommandReturns(""));
            Assert.Throws<System.ArgumentException>(() => ScenarioStep.AssertCommandReturns(null));
        }

        [Test]
        public void AssertCommandEventually_BuildsStep()
        {
            var step = ScenarioStep.AssertCommandEventually(
                "Foo/Bar",
                new Dictionary<string, object> { ["x"] = 1 },
                expected: "ok",
                timeoutSeconds: 3f,
                description: "test");
            Assert.AreEqual(ScenarioStepKind.AssertCommandEventually, step.Kind);
            Assert.AreEqual("test", step.Description);
            var s = (AssertCommandEventuallyStep)step;
            Assert.AreEqual("Foo/Bar", s.CommandPath);
            Assert.AreEqual("ok", s.Expected);
            Assert.AreEqual(3f, s.TimeoutSeconds);
            Assert.AreEqual(1, s.Args["x"]);
        }

        [Test]
        public void AssertCommandEventually_NullExpected_Allowed()
        {
            var step = (AssertCommandEventuallyStep)ScenarioStep.AssertCommandEventually("Foo/Bar");
            Assert.IsNull(step.Expected);
            Assert.AreEqual(0, step.Args.Count);
        }

        [Test]
        public void AssertCommandEventually_RejectsEmptyPathAndNonPositiveTimeout()
        {
            Assert.Throws<System.ArgumentException>(() => ScenarioStep.AssertCommandEventually(""));
            Assert.Throws<System.ArgumentException>(() => ScenarioStep.AssertCommandEventually(null));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ScenarioStep.AssertCommandEventually("Foo/Bar", timeoutSeconds: 0f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ScenarioStep.AssertCommandEventually("Foo/Bar", timeoutSeconds: -1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ScenarioStep.AssertCommandEventually("Foo/Bar", timeoutSeconds: float.NaN));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ScenarioStep.AssertCommandEventually("Foo/Bar", timeoutSeconds: float.PositiveInfinity));
        }
    }
}
