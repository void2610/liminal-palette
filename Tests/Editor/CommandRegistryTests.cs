using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Void2610.LiminalPalette.Tests
{
    public sealed class CommandRegistryTests
    {
        [Test]
        public void Default_IsSingleton() => Assert.AreSame(CommandRegistry.Default, CommandRegistry.Default);
        [Test]
        public void Register_And_Find_Works()
        {
            var reg = new CommandRegistry();
            var d = MakeDescriptor("Foo/Bar");
            reg.Register(d);

            Assert.AreSame(d, reg.Find("Foo/Bar"));
            Assert.AreEqual(1, reg.All.Count);
        }

        [Test]
        public void Find_IsCaseInsensitive()
        {
            var reg = new CommandRegistry();
            reg.Register(MakeDescriptor("Foo/Bar"));
            Assert.IsNotNull(reg.Find("foo/BAR"));
        }

        [Test]
        public void Find_ByAlias_Works()
        {
            var reg = new CommandRegistry();
            reg.Register(MakeDescriptor("Foo/Bar", new[] { "fb" }));
            Assert.IsNotNull(reg.Find("fb"));
        }

        [Test]
        public void DuplicatePath_LogsWarning_AndOverwrites()
        {
            var reg = new CommandRegistry();
            // 異なる Method を持つ 2 つのコマンドが同じパスを要求する = 実装ミス。後勝ちで上書き + 警告。
            var method1 = typeof(TestCommands).GetMethod(nameof(TestCommands.NoArg), BindingFlags.Public | BindingFlags.Static);
            var method2 = typeof(TestCommands).GetMethod(nameof(TestCommands.Throws), BindingFlags.Public | BindingFlags.Static);
            var d1 = MakeDescriptorWithMethod("Dup/Cmd", method1);
            var d2 = MakeDescriptorWithMethod("Dup/Cmd", method2);

            reg.Register(d1);

            // 2 度目の登録で警告ログが出ることを確認。
            LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("Duplicate command path"));
            reg.Register(d2);

            Assert.AreSame(d2, reg.Find("Dup/Cmd"));
            Assert.AreEqual(1, reg.All.Count);
        }

        [Test]
        public void DuplicatePath_SameMethod_SilentlyIgnored()
        {
            // 同じ Method (= ScanAll の二重呼び出し) は警告無しで黙ってスキップする。
            var reg = new CommandRegistry();
            var d1 = MakeDescriptor("Dup/Cmd");
            var d2 = MakeDescriptor("Dup/Cmd");

            reg.Register(d1);
            reg.Register(d2);

            // 既存エントリが保持されていること、警告ログが立たないこと。
            Assert.AreSame(d1, reg.Find("Dup/Cmd"));
            Assert.AreEqual(1, reg.All.Count);
        }

        [Test]
        public void Unregister_RemovesPathAndAliases()
        {
            var reg = new CommandRegistry();
            reg.Register(MakeDescriptor("Foo/Bar", new[] { "fb" }));
            Assert.IsTrue(reg.Unregister("Foo/Bar"));
            Assert.IsNull(reg.Find("Foo/Bar"));
            Assert.IsNull(reg.Find("fb"));
        }

        [Test]
        public void FindByCategory_ReturnsMatchingPrefix()
        {
            var reg = new CommandRegistry();
            reg.Register(MakeDescriptor("Player/Health/Set"));
            reg.Register(MakeDescriptor("Player/Health/Get"));
            reg.Register(MakeDescriptor("Enemy/Spawn"));

            var list = new List<CommandDescriptor>(reg.FindByCategory("Player"));
            Assert.AreEqual(2, list.Count);
        }

        [Test]
        public void Events_Fire_OnRegisterAndUnregister()
        {
            var reg = new CommandRegistry();
            CommandDescriptor registered = null;
            CommandDescriptor unregistered = null;
            reg.Registered += d => registered = d;
            reg.Unregistered += d => unregistered = d;

            var d = MakeDescriptor("Foo/Evt");
            reg.Register(d);
            Assert.AreSame(d, registered);

            reg.Unregister("Foo/Evt");
            Assert.AreSame(d, unregistered);
        }
        // テスト用の最小 CommandDescriptor を作るヘルパ。MethodInfo はダミーで、Execute はしない。
        private static CommandDescriptor MakeDescriptor(string path, string[] aliases = null)
        {
            var method = typeof(TestCommands).GetMethod(nameof(TestCommands.NoArg), BindingFlags.Public | BindingFlags.Static);
            return MakeDescriptorWithMethod(path, method, aliases);
        }

        private static CommandDescriptor MakeDescriptorWithMethod(string path, MethodInfo method, string[] aliases = null)
        {
            return new CommandDescriptor(
                path: path,
                description: "",
                aliases: aliases ?? System.Array.Empty<string>(),
                parameters: System.Array.Empty<ParameterDescriptor>(),
                returnType: typeof(void),
                isAsync: false,
                method: method);
        }
    }
}
