using System;
using System.Collections.Generic;
using NUnit.Framework;
using Void2610.LiminalPalette;

namespace Void2610.LiminalPalette.Tests.Resolution
{
    /// <summary>
    /// Phase 5a インスタンスメソッド対応テスト。
    /// IInstanceResolver 経由でインスタンスを解決し、メソッドが正しく呼ばれることを確認。
    /// 既存の static [LiminalCommand] が回帰しないことも併せて検証。
    /// </summary>
    public sealed class InstanceMethodCommandTests
    {
        // テスト用のインスタンスメソッドを持つコマンド対象クラス。
        // [LiminalCommand] を直接付けると AttributeScanner で自動登録されるが、
        // テスト独立性のため動的登録にする (Path 衝突回避 + テスト後の cleanup 楽)。
        private sealed class Counter
        {
            public int Value;
            public int Increment(int by) { Value += by; return Value; }
            public int CurrentValue() => Value;
        }

        // 単純な resolver: コンストラクタで渡したインスタンスを Type で返す。
        private sealed class StubResolver : IInstanceResolver
        {
            private readonly Dictionary<Type, object> _map;
            public StubResolver(params object[] instances)
            {
                _map = new Dictionary<Type, object>();
                foreach (var i in instances) _map[i.GetType()] = i;
            }
            public object Resolve(Type type) => _map.TryGetValue(type, out var v) ? v : null;
        }

        [SetUp]
        public void SetUp()
        {
            // 既存テストとの干渉を避けるため、resolver を NullInstanceResolver に戻す。
            // 元の resolver の復元は行わない (各テストが必要な resolver を SetInstanceResolver で明示設定する方針)。
            LiminalPalette.SetInstanceResolver(null);
        }

        [TearDown]
        public void TearDown() => LiminalPalette.SetInstanceResolver(null);

        [Test]
        public void InstanceMethod_WithResolver_Invokes()
        {
            var counter = new Counter();
            LiminalPalette.SetInstanceResolver(new StubResolver(counter));

            var registry = new CommandRegistry();
            var executor = new CommandExecutor(registry);

            // インスタンスメソッドを動的登録 (属性スキャンせずに MethodInfo 経由で descriptor を作る)。
            var method = typeof(Counter).GetMethod(nameof(Counter.Increment));
            var parameters = new[]
            {
                new ParameterDescriptor("by", typeof(int), 0, false, null, "", Array.Empty<string>())
            };
            var descriptor = new CommandDescriptor("Test/Counter/Increment", "increments",
                Array.Empty<string>(), parameters, typeof(int), false, method);
            registry.Register(descriptor);

            var result = executor.ExecuteAsync("Test/Counter/Increment",
                new Dictionary<string, string> { ["by"] = "5" }).Result;

            Assert.IsTrue(result.Success, $"Expected success but got: {result.Error}");
            Assert.AreEqual(5, (int)result.Value);
            Assert.AreEqual(5, counter.Value);
        }

        [Test]
        public void InstanceMethod_WithoutResolver_FailsWithMessage()
        {
            // resolver は NullInstanceResolver のまま (= 何も解決しない)。
            var registry = new CommandRegistry();
            var executor = new CommandExecutor(registry);

            var method = typeof(Counter).GetMethod(nameof(Counter.Increment));
            var parameters = new[]
            {
                new ParameterDescriptor("by", typeof(int), 0, false, null, "", Array.Empty<string>())
            };
            registry.Register(new CommandDescriptor("Test/Counter/Increment", "", Array.Empty<string>(),
                parameters, typeof(int), false, method));

            var result = executor.ExecuteAsync("Test/Counter/Increment",
                new Dictionary<string, string> { ["by"] = "1" }).Result;

            Assert.IsFalse(result.Success);
            // 利用者に対処方法を案内するメッセージが含まれる。
            StringAssert.Contains("Instance not resolved", result.Error);
            StringAssert.Contains("VContainer", result.Error);
        }

        [Test]
        public void StaticMethod_StillWorks_NoRegression()
        {
            // 既存の Test/NoArg / Test/Int (static) が引き続き動くこと。
            var executor = new CommandExecutor(LiminalPalette.Registry);
            var result = executor.ExecuteAsync("Test/Int",
                new Dictionary<string, string> { ["a"] = "3" }).Result;
            Assert.IsTrue(result.Success);
            Assert.AreEqual(13, (int)result.Value); // 3 + 10 (default)
        }
    }
}
