using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Void2610.LiminalPalette.Tests
{
    public sealed class CommandExecutorTests
    {
        // 個別テストごとに新しい Registry / Executor を作って、
        // Bootstrap が登録した Default レジストリの状態に依存しないようにする。
        private CommandRegistry _registry;
        private CommandExecutor _executor;

        [SetUp]
        public void SetUp()
        {
            _registry = new CommandRegistry();
            _executor = new CommandExecutor(_registry);

            // テスト対象アセンブリ内の TestCommands を登録する (Bootstrap と同じ経路)。
            var asm = typeof(TestCommands).Assembly;
            var commands = AttributeScanner.Scan(new[] { asm });
            foreach (var c in commands) _registry.Register(c);
        }

        [Test]
        public async Task Sync_NoArg_Succeeds()
        {
            var r = await _executor.ExecuteAsync("Test/NoArg", new Dictionary<string, string>());
            Assert.IsTrue(r.Success, r.Error);
            Assert.IsNull(r.Value);
        }

        [Test]
        public async Task Sync_IntCommand_Adds()
        {
            var r = await _executor.ExecuteAsync("Test/Int", new Dictionary<string, string> { ["a"] = "5" });
            Assert.IsTrue(r.Success, r.Error);
            // b は default 10 が使われる。
            Assert.AreEqual(15, r.Value);
        }

        [Test]
        public async Task Async_StringCommand_AwaitedAndUppercased()
        {
            var r = await _executor.ExecuteAsync("Test/Async", new Dictionary<string, string> { ["s"] = "hello" });
            Assert.IsTrue(r.Success, r.Error);
            Assert.AreEqual("HELLO", r.Value);
        }

        [Test]
        public async Task UniTask_NonGeneric_AwaitedAndNullValue()
        {
            var r = await _executor.ExecuteAsync("Test/UniTaskVoid", new Dictionary<string, string>());
            Assert.IsTrue(r.Success, r.Error);
            Assert.IsNull(r.Value);
        }

        [Test]
        public async Task UniTask_Generic_AwaitedAndResultReturned()
        {
            var r = await _executor.ExecuteAsync("Test/UniTaskString", new Dictionary<string, string> { ["s"] = "hello" });
            Assert.IsTrue(r.Success, r.Error);
            Assert.AreEqual("HELLO", r.Value);
        }

        [Test]
        public async Task Throws_ConvertedToFail_WithExceptionPreserved()
        {
            var r = await _executor.ExecuteAsync("Test/Throws", new Dictionary<string, string>());
            Assert.IsFalse(r.Success);
            Assert.AreEqual("boom", r.Error);
            Assert.IsNotNull(r.Exception);
            Assert.IsInstanceOf<System.InvalidOperationException>(r.Exception);
        }

        [Test]
        public async Task NotFound_Path_ReturnsFail()
        {
            var r = await _executor.ExecuteAsync("Does/Not/Exist", new Dictionary<string, string>());
            Assert.IsFalse(r.Success);
            StringAssert.Contains("Does/Not/Exist", r.Error);
        }

        [Test]
        public async Task BindError_ReturnsFail_WithoutInvokingMethod()
        {
            // a が必須なのに渡していないのでバインド時に失敗する。
            var r = await _executor.ExecuteAsync("Test/Int", new Dictionary<string, string>());
            Assert.IsFalse(r.Success);
            StringAssert.Contains("a", r.Error);
        }

        [Test]
        public async Task LogCapture_CollectsDebugLogDuringExecution()
        {
            // LogAssert を使う代わりに、Logs プロパティに集約されていることを確認する。
            // Test/Log は Debug.Log を 1 回呼ぶ。
            UnityEngine.TestTools.LogAssert.Expect(LogType.Log, "captured");
            var r = await _executor.ExecuteAsync("Test/Log", new Dictionary<string, string> { ["msg"] = "captured" });
            Assert.IsTrue(r.Success, r.Error);
            Assert.IsTrue(r.Logs.Any(l => l.Message == "captured"),
                $"expected 'captured' in logs, got [{string.Join(", ", r.Logs.Select(l => l.Message))}]");
        }

        [Test]
        public async Task Duration_IsRecorded()
        {
            var r = await _executor.ExecuteAsync("Test/NoArg", new Dictionary<string, string>());
            Assert.IsTrue(r.Success);
            // 同期 no-op でも Stopwatch.Elapsed は >=0 を返す。
            Assert.IsTrue(r.Duration.Ticks >= 0);
        }

        [Test]
        public async Task Vector3Command_RoundTripsThroughConverter()
        {
            var r = await _executor.ExecuteAsync("Test/Vector", new Dictionary<string, string> { ["v"] = "1,2,3" });
            Assert.IsTrue(r.Success, r.Error);
            Assert.AreEqual(new Vector3(2, 4, 6), r.Value);
        }

        [Test]
        public async Task Alias_ResolvesToSameCommand()
        {
            var r = await _executor.ExecuteAsync("Test/I", new Dictionary<string, string> { ["a"] = "3" });
            Assert.IsTrue(r.Success, r.Error);
            Assert.AreEqual(13, r.Value);
        }

        [Test]
        public async Task Cancelled_BeforeInvocation_ReturnsFail()
        {
            // 事前にキャンセル済みのトークンを渡すと本体を走らせず Fail で返ること。
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();

            var r = await _executor.ExecuteAsync(
                "Test/Int",
                new Dictionary<string, string> { ["a"] = "1" },
                cts.Token);

            Assert.IsFalse(r.Success);
            Assert.AreEqual("Cancelled", r.Error);
            Assert.IsInstanceOf<System.OperationCanceledException>(r.Exception);
        }
    }
}
