using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace Void2610.LiminalPalette.Tests
{
    public sealed class ScenarioRegistryTests
    {
        private ScenarioDescriptor MakeStub(string path) =>
            MakeStubFor(path, typeof(TestScenarios).GetMethod(nameof(TestScenarios.Empty), BindingFlags.Public | BindingFlags.Static));

        private ScenarioDescriptor MakeStubFor(string path, MethodInfo method)
        {
            return new ScenarioDescriptor(
                path: path,
                description: "",
                declaringType: typeof(TestScenarios),
                method: method,
                isStatic: true,
                stepsFactory: _ => System.Linq.Enumerable.Empty<ScenarioStep>());
        }

        [Test]
        public void Register_AndFind()
        {
            var registry = new ScenarioRegistry();
            registry.Register(MakeStub("Foo/A"));
            Assert.AreEqual(1, registry.All.Count);
            Assert.IsNotNull(registry.Find("Foo/A"));
            Assert.IsNotNull(registry.Find("foo/a"), "case-insensitive find");
            Assert.IsNull(registry.Find("Missing"));
        }

        [Test]
        public void Register_DuplicatePath_Overwrites()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            // 異なる Method を持つ 2 つのシナリオが同じパスを要求する = 実装ミス。後勝ちで上書き + 警告。
            var m1 = typeof(TestScenarios).GetMethod(nameof(TestScenarios.Empty), BindingFlags.Public | BindingFlags.Static);
            var m2 = typeof(TestScenarios).GetMethod(nameof(TestScenarios.SingleCommand), BindingFlags.Public | BindingFlags.Static);
            var registry = new ScenarioRegistry();
            var d1 = MakeStubFor("Foo/A", m1);
            var d2 = MakeStubFor("Foo/A", m2);
            registry.Register(d1);
            registry.Register(d2);
            Assert.AreEqual(1, registry.All.Count, "duplicate path should overwrite");
            Assert.AreSame(d2, registry.Find("Foo/A"));
        }

        [Test]
        public void Register_DuplicatePath_SameMethod_SilentlyIgnored()
        {
            // 同じ Method (= ScanAll の二重呼び出し) は黙ってスキップ。
            var registry = new ScenarioRegistry();
            var d1 = MakeStub("Foo/A");
            var d2 = MakeStub("Foo/A");
            registry.Register(d1);
            registry.Register(d2);
            Assert.AreEqual(1, registry.All.Count);
            Assert.AreSame(d1, registry.Find("Foo/A"));
        }

        [Test]
        public void Clear_RemovesAll()
        {
            var registry = new ScenarioRegistry();
            registry.Register(MakeStub("A"));
            registry.Register(MakeStub("B"));
            registry.Clear();
            Assert.AreEqual(0, registry.All.Count);
        }
    }
}
