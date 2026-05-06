using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Void2610.LiminalPalette.Tests
{
    public sealed class ScenarioScannerTests
    {
        [Test]
        public void Scan_FindsAttributedScenarios()
        {
            var asm = typeof(TestScenarios).Assembly;
            var scenarios = ScenarioScanner.Scan(new[] { asm });
            var paths = scenarios.Select(s => s.Path).ToHashSet();
            Assert.IsTrue(paths.Contains("TestScenario/Empty"));
            Assert.IsTrue(paths.Contains("TestScenario/SingleCommand"));
            Assert.IsTrue(paths.Contains("TestScenario/CommandThenWait"));
            Assert.IsTrue(paths.Contains("TestScenario/FailingCommand"));
        }

        [Test]
        public void TryBuildDescriptor_RejectsMethodsWithArguments()
        {
            // 属性は付けず、テスト時に手で属性インスタンスを生成して TryBuildDescriptor に渡す。
            // [ConsoleScenario] を直接付けると Bootstrap が起動毎に警告を出し続けるため、
            // 「不正なシグネチャ」のテストはローカルでのみ検証する。
            var method = typeof(InvalidScenarioShapes)
                .GetMethod(nameof(InvalidScenarioShapes.WithArgs), BindingFlags.Public | BindingFlags.Static);
            var attr = new ConsoleScenarioAttribute("TestScenario/WithArgs");
            var ok = ScenarioScanner.TryBuildDescriptor(method, attr, out _, out var error);
            Assert.IsFalse(ok);
            StringAssert.Contains("no parameters", error);
        }

        [Test]
        public void TryBuildDescriptor_RejectsBadReturnType()
        {
            var method = typeof(InvalidScenarioShapes)
                .GetMethod(nameof(InvalidScenarioShapes.BadReturnType), BindingFlags.Public | BindingFlags.Static);
            var attr = new ConsoleScenarioAttribute("TestScenario/BadReturn");
            var ok = ScenarioScanner.TryBuildDescriptor(method, attr, out _, out var error);
            Assert.IsFalse(ok);
            StringAssert.Contains("IEnumerable<ScenarioStep>", error);
        }

        [Test]
        public void StepsFactory_EnumeratesSteps()
        {
            var asm = typeof(TestScenarios).Assembly;
            var scenarios = ScenarioScanner.Scan(new[] { asm });
            var single = scenarios.First(s => s.Path == "TestScenario/SingleCommand");
            var steps = single.StepsFactory(null).ToList();
            Assert.AreEqual(1, steps.Count);
            Assert.AreEqual(ScenarioStepKind.Command, steps[0].Kind);
        }
    }
}
