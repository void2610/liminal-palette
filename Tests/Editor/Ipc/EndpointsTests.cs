using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Void2610.LiminalPalette.Ipc.Endpoints;
using Void2610.LiminalPalette.Ipc.Server;
using Void2610.LiminalPalette.Ipc.Threading;

namespace Void2610.LiminalPalette.Tests.Ipc
{
    /// <summary>
    /// 4 エンドポイント (Health / ListCommands / Execute / ListLogs) の単体テスト。
    /// MainThreadDispatcher.RegisterMainThread をテスト実行スレッドに合わせるため、
    /// メインスレッド経路と判定されて RunAsync が即時実行される。
    /// </summary>
    public sealed class EndpointsTests
    {
        [SetUp]
        public void SetUp()
        {
            MainThreadDispatcher.RegisterMainThread(Thread.CurrentThread.ManagedThreadId);
            MainThreadDispatcher.ClearForTest();
        }

        private static IpcRequest Get(string path, IReadOnlyDictionary<string, string> query = null)
            => new IpcRequest("GET", path, query, null, "");

        private static IpcRequest PostJson(string path, string body)
            => new IpcRequest("POST", path, null, null, body);

        // ---------- HealthEndpoint ----------

        [Test]
        public async Task Health_ReturnsOkAndCommandCount()
        {
            var ep = new HealthEndpoint();
            var res = await ep.HandleAsync(Get("/api/v1/health"), CancellationToken.None);
            Assert.AreEqual(200, res.StatusCode);
            StringAssert.Contains("\"status\":\"ok\"", res.Body);
            StringAssert.Contains("\"commandCount\":", res.Body);
        }

        [Test]
        public async Task Health_ReturnsProjectIdentity()
        {
            // 複数 Unity プロジェクト同時起動時に lp CLI が紐付け判定に使う 2 フィールド。
            var ep = new HealthEndpoint();
            var res = await ep.HandleAsync(Get("/api/v1/health"), CancellationToken.None);
            StringAssert.Contains("\"projectName\":", res.Body);
            StringAssert.Contains("\"projectPath\":", res.Body);
        }

        [Test]
        public void Health_DoesNotRequireAuth()
        {
            Assert.IsFalse(new HealthEndpoint().RequiresAuth);
        }

        // ---------- ListCommandsEndpoint ----------

        [Test]
        public async Task ListCommands_ReturnsKnownTestCommands()
        {
            var ep = new ListCommandsEndpoint();
            var res = await ep.HandleAsync(Get("/api/v1/commands"), CancellationToken.None);
            Assert.AreEqual(200, res.StatusCode);
            // TestCommands.cs で登録した "Test/Int" が含まれるはず。
            StringAssert.Contains("\"path\":\"Test/Int\"", res.Body);
            StringAssert.Contains("\"commands\":[", res.Body);
        }

        [Test]
        public void ListCommands_RequiresAuth()
        {
            Assert.IsTrue(new ListCommandsEndpoint().RequiresAuth);
        }

        // ---------- ExecuteCommandEndpoint ----------

        [Test]
        public async Task Execute_KnownCommand_Succeeds()
        {
            var ep = new ExecuteCommandEndpoint();
            var res = await ep.HandleAsync(PostJson("/api/v1/execute",
                "{\"path\":\"Test/Int\",\"args\":{\"a\":\"3\"}}"), CancellationToken.None);
            Assert.AreEqual(200, res.StatusCode);
            StringAssert.Contains("\"success\":true", res.Body);
            // a=3, b=default(10) で 13 が返る。
            StringAssert.Contains("\"value\":\"13\"", res.Body);
        }

        [Test]
        public async Task Execute_UnknownPath_ReturnsSuccessFalse()
        {
            // Phase 1 流儀: 不明 path はバリデーションエラーとして success=false で 200 を返す。
            var ep = new ExecuteCommandEndpoint();
            var res = await ep.HandleAsync(PostJson("/api/v1/execute",
                "{\"path\":\"Does/Not/Exist\",\"args\":{}}"), CancellationToken.None);
            Assert.AreEqual(200, res.StatusCode);
            StringAssert.Contains("\"success\":false", res.Body);
        }

        [Test]
        public async Task Execute_BodyWithoutPath_Returns400()
        {
            var ep = new ExecuteCommandEndpoint();
            var res = await ep.HandleAsync(PostJson("/api/v1/execute", "{\"args\":{}}"), CancellationToken.None);
            Assert.AreEqual(400, res.StatusCode);
        }

        [Test]
        public async Task Execute_EmptyBody_Returns400()
        {
            var ep = new ExecuteCommandEndpoint();
            var res = await ep.HandleAsync(PostJson("/api/v1/execute", ""), CancellationToken.None);
            Assert.AreEqual(400, res.StatusCode);
        }

        [Test]
        public async Task Execute_MalformedJson_Returns400()
        {
            var ep = new ExecuteCommandEndpoint();
            var res = await ep.HandleAsync(PostJson("/api/v1/execute", "{not json"), CancellationToken.None);
            Assert.AreEqual(400, res.StatusCode);
        }

        [Test]
        public async Task Execute_ArgsWithDifferentValueTypes_AllStringified()
        {
            // args の値は string / number / bool / null 全て string として渡せる。
            var ep = new ExecuteCommandEndpoint();
            // Test/Int は int を取るので、数値リテラルで送って受け取れることを確認。
            var res = await ep.HandleAsync(PostJson("/api/v1/execute",
                "{\"path\":\"Test/Int\",\"args\":{\"a\":7,\"b\":3}}"), CancellationToken.None);
            Assert.AreEqual(200, res.StatusCode);
            StringAssert.Contains("\"value\":\"10\"", res.Body);
        }

        [Test]
        public void Execute_RequiresAuth()
        {
            Assert.IsTrue(new ExecuteCommandEndpoint().RequiresAuth);
        }

        [Test]
        public async Task Execute_RateLimit_Returns429AfterThreshold()
        {
            // 一時的にレートリミットを 2 に下げて、3 回目が 429 になることを確認。
            var saved = Void2610.LiminalPalette.Ipc.IpcSettings.ExecuteRateLimitPerSecond;
            Void2610.LiminalPalette.Ipc.IpcSettings.ExecuteRateLimitPerSecond = 2;
            try
            {
                var ep = new ExecuteCommandEndpoint();
                var ok1 = await ep.HandleAsync(PostJson("/api/v1/execute", "{\"path\":\"Test/NoArg\",\"args\":{}}"), CancellationToken.None);
                var ok2 = await ep.HandleAsync(PostJson("/api/v1/execute", "{\"path\":\"Test/NoArg\",\"args\":{}}"), CancellationToken.None);
                var deny = await ep.HandleAsync(PostJson("/api/v1/execute", "{\"path\":\"Test/NoArg\",\"args\":{}}"), CancellationToken.None);
                Assert.AreEqual(200, ok1.StatusCode);
                Assert.AreEqual(200, ok2.StatusCode);
                Assert.AreEqual(429, deny.StatusCode);
            }
            finally
            {
                Void2610.LiminalPalette.Ipc.IpcSettings.ExecuteRateLimitPerSecond = saved;
            }
        }

        // ---------- ListLogsEndpoint ----------

        [Test]
        public async Task Logs_ReturnsRecentInvocationsNewestFirst()
        {
            // Execute を 2 回叩いて履歴が積まれることを利用してテスト。
            var exec = new ExecuteCommandEndpoint();
            await exec.HandleAsync(PostJson("/api/v1/execute", "{\"path\":\"Test/NoArg\",\"args\":{}}"), CancellationToken.None);
            await exec.HandleAsync(PostJson("/api/v1/execute", "{\"path\":\"Test/Int\",\"args\":{\"a\":\"1\"}}"), CancellationToken.None);

            var ep = new ListLogsEndpoint();
            var res = await ep.HandleAsync(Get("/api/v1/logs"), CancellationToken.None);
            Assert.AreEqual(200, res.StatusCode);
            StringAssert.Contains("\"invocations\":[", res.Body);
            // 最新が Test/Int であることを path 順序で確認 (新しい順)。
            var idxIntPath = res.Body.IndexOf("\"path\":\"Test/Int\"");
            var idxNoArgPath = res.Body.IndexOf("\"path\":\"Test/NoArg\"");
            Assert.IsTrue(idxIntPath >= 0 && idxNoArgPath >= 0, "両 path がレスポンスに含まれる");
            Assert.IsTrue(idxIntPath < idxNoArgPath, "新しい (Test/Int) が先に並ぶ");
        }

        [Test]
        public async Task Logs_LimitQueryParam_LimitsResults()
        {
            var exec = new ExecuteCommandEndpoint();
            for (var i = 0; i < 3; i++)
                await exec.HandleAsync(PostJson("/api/v1/execute", "{\"path\":\"Test/NoArg\",\"args\":{}}"), CancellationToken.None);

            var ep = new ListLogsEndpoint();
            var res = await ep.HandleAsync(
                Get("/api/v1/logs", new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) { ["limit"] = "1" }),
                CancellationToken.None);
            StringAssert.Contains("\"limit\":1", res.Body);
        }

        [Test]
        public void Logs_RequiresAuth()
        {
            Assert.IsTrue(new ListLogsEndpoint().RequiresAuth);
        }
    }
}
