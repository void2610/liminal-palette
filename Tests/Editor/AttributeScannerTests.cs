using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Void2610.LiminalPalette.Tests
{
    public sealed class AttributeScannerTests
    {
        [Test]
        public void Scan_FindsAttributedStaticMethods()
        {
            // テスト用アセンブリのみを対象にスキャンする。
            var asm = typeof(TestCommands).Assembly;
            var commands = AttributeScanner.Scan(new[] { asm });

            // TestCommands に定義した 6 件が見つかること。
            // 他の本番コードに [ConsoleCommand] が将来増えても、このアセンブリ単体スキャンには影響しない。
            var paths = commands.Select(c => c.Path).ToHashSet();
            Assert.IsTrue(paths.Contains("Test/NoArg"), "Test/NoArg should be detected");
            Assert.IsTrue(paths.Contains("Test/Int"), "Test/Int should be detected");
            Assert.IsTrue(paths.Contains("Test/Async"), "Test/Async should be detected");
            Assert.IsTrue(paths.Contains("Test/Throws"), "Test/Throws should be detected");
            Assert.IsTrue(paths.Contains("Test/Vector"), "Test/Vector should be detected");
            Assert.IsTrue(paths.Contains("Test/Log"), "Test/Log should be detected");
        }

        [Test]
        public void Scan_IgnoresMethodsWithoutAttribute()
        {
            var asm = typeof(TestCommands).Assembly;
            var commands = AttributeScanner.Scan(new[] { asm });
            // NonCommands.Plain は属性なしなので含まれない。
            Assert.IsFalse(commands.Any(c => c.Method.DeclaringType == typeof(NonCommands)));
        }

        [Test]
        public void Scan_ExtractsAliases()
        {
            var asm = typeof(TestCommands).Assembly;
            var commands = AttributeScanner.Scan(new[] { asm });
            var intCmd = commands.First(c => c.Path == "Test/Int");
            CollectionAssert.Contains(intCmd.Aliases, "Test/I");
        }

        [Test]
        public void Scan_DetectsAsyncReturn()
        {
            var asm = typeof(TestCommands).Assembly;
            var commands = AttributeScanner.Scan(new[] { asm });
            var asyncCmd = commands.First(c => c.Path == "Test/Async");
            Assert.IsTrue(asyncCmd.IsAsync);

            var noArg = commands.First(c => c.Path == "Test/NoArg");
            Assert.IsFalse(noArg.IsAsync);
        }

        [Test]
        public void Scan_BuildsParameterDescriptors_WithDefaults()
        {
            var asm = typeof(TestCommands).Assembly;
            var commands = AttributeScanner.Scan(new[] { asm });
            var intCmd = commands.First(c => c.Path == "Test/Int");

            Assert.AreEqual(2, intCmd.Parameters.Count);
            Assert.AreEqual("a", intCmd.Parameters[0].Name);
            Assert.IsFalse(intCmd.Parameters[0].HasDefault);
            Assert.AreEqual("b", intCmd.Parameters[1].Name);
            Assert.IsTrue(intCmd.Parameters[1].HasDefault);
            Assert.AreEqual(10, intCmd.Parameters[1].DefaultValue);
        }

        [Test]
        public void TryBuildDescriptor_RejectsEmptyPath()
        {
            var method = typeof(TestCommands).GetMethod(nameof(TestCommands.NoArg), BindingFlags.Public | BindingFlags.Static);
            var ok = AttributeScanner.TryBuildDescriptor(method, new ConsoleCommandAttribute(""), out _, out var err);
            Assert.IsFalse(ok);
            Assert.IsNotNull(err);
        }

        [Test]
        public void TryBuildDescriptor_RejectsTrailingSlash()
        {
            var method = typeof(TestCommands).GetMethod(nameof(TestCommands.NoArg), BindingFlags.Public | BindingFlags.Static);
            var ok = AttributeScanner.TryBuildDescriptor(method, new ConsoleCommandAttribute("Foo/"), out _, out var err);
            Assert.IsFalse(ok);
            Assert.IsNotNull(err);
        }

        [Test]
        public void TryBuildDescriptor_RejectsDoubleSlash()
        {
            var method = typeof(TestCommands).GetMethod(nameof(TestCommands.NoArg), BindingFlags.Public | BindingFlags.Static);
            var ok = AttributeScanner.TryBuildDescriptor(method, new ConsoleCommandAttribute("Foo//Bar"), out _, out var err);
            Assert.IsFalse(ok);
            Assert.IsNotNull(err);
        }
    }
}
