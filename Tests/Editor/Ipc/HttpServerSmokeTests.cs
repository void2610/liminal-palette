using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Void2610.LiminalPalette.Ipc;
using Void2610.LiminalPalette.Ipc.Auth;
using Void2610.LiminalPalette.Ipc.Endpoints;
using Void2610.LiminalPalette.Ipc.Server;
using Void2610.LiminalPalette.Ipc.TestRunning;
using Void2610.LiminalPalette.Ipc.Threading;

namespace Void2610.LiminalPalette.Tests.Ipc
{
    /// <summary>
    /// HttpServer の実 HttpListener を使った統合スモークテスト。
    /// CI で並列実行されるとポート衝突するので、テスト用ポートを DefaultPort + 1000 にずらしている。
    /// 各テスト後に Stop して必ず listener を解放する。
    /// </summary>
    public sealed class HttpServerSmokeTests
    {
        private const int TestPort = IpcSettings.DefaultPort + 1000;

        private HttpServer _server;
        private HttpClient _client;

        [SetUp]
        public void SetUp()
        {
            // メインスレッドをテスト実行スレッドに合わせて MainThreadDispatcher.RunAsync が即時実行されるように。
            MainThreadDispatcher.RegisterMainThread(Thread.CurrentThread.ManagedThreadId);
            MainThreadDispatcher.ClearForTest();
            // Test/Int 等のフィクスチャを Registry に登録 (Bootstrap は .Tests アセンブリ除外のため)。
            EndpointsTests.EnsureTestCommandFixturesRegistered();

            var router = new IpcRouter(new TokenAuthenticator("smoke-token"));
            router.Register("GET", "/api/v1/health", new HealthEndpoint("editor", "", ""));
            router.Register("GET", "/api/v1/commands", new ListCommandsEndpoint());
            router.Register("POST", "/api/v1/execute", new ExecuteCommandEndpoint());
            router.Register("GET", "/api/v1/logs", new ListLogsEndpoint());
            router.Register("POST", "/api/v1/tests/run", new RunTestsEndpoint());

            _server = new HttpServer(router, TestPort);
            _server.Start();

            _client = new HttpClient { Timeout = System.TimeSpan.FromSeconds(5) };
        }

        [TearDown]
        public void TearDown()
        {
            try { _client?.Dispose(); } catch { /* swallow */ }
            try { _server?.Dispose(); } catch { /* swallow */ }
            _client = null;
            _server = null;
        }

        private string Url(string path) => $"http://127.0.0.1:{_server.Port}{path}";

        [Test]
        public async Task Health_ReturnsOk_NoAuthRequired()
        {
            var res = await _client.GetAsync(Url("/api/v1/health"));
            Assert.AreEqual(System.Net.HttpStatusCode.OK, res.StatusCode);
            var body = await res.Content.ReadAsStringAsync();
            StringAssert.Contains("\"status\":\"ok\"", body);
        }

        [Test]
        public async Task ListCommands_NoToken_Returns401()
        {
            var res = await _client.GetAsync(Url("/api/v1/commands"));
            Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, res.StatusCode);
        }

        [Test]
        public async Task ListCommands_ValidToken_Returns200()
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, Url("/api/v1/commands"));
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "smoke-token");
            var res = await _client.SendAsync(req);
            Assert.AreEqual(System.Net.HttpStatusCode.OK, res.StatusCode);
            var body = await res.Content.ReadAsStringAsync();
            StringAssert.Contains("\"commands\":[", body);
        }

        [Test]
        public async Task Execute_ValidToken_RoundTrip()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Url("/api/v1/execute"));
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "smoke-token");
            req.Content = new StringContent("{\"path\":\"Test/Int\",\"args\":{\"a\":\"5\",\"b\":\"6\"}}",
                System.Text.Encoding.UTF8, "application/json");
            var res = await _client.SendAsync(req);
            Assert.AreEqual(System.Net.HttpStatusCode.OK, res.StatusCode);
            var body = await res.Content.ReadAsStringAsync();
            StringAssert.Contains("\"success\":true", body);
            StringAssert.Contains("\"value\":\"11\"", body);
        }

        // CLI (ureq) は charset=utf-8 を付けて送る。Mono の HttpListener はこれを無視して us-ascii で読んでいた
        [TestCase("application/json; charset=utf-8")]
        [TestCase("application/json")]
        public async Task NonAsciiBody_IsDecodedAsUtf8(string contentType)
        {
            var saved = TestRunnerBridge.Current;
            var fake = new FilterCapturingService();
            TestRunnerBridge.Current = fake;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, Url("/api/v1/tests/run"));
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "smoke-token");
                req.Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("{\"mode\":\"editmode\",\"filter\":\"全StringTable\"}"));
                req.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                var res = await _client.SendAsync(req);

                Assert.AreEqual(System.Net.HttpStatusCode.OK, res.StatusCode);
                Assert.AreEqual("全StringTable", fake.LastFilter);
            }
            finally
            {
                TestRunnerBridge.Current = saved;
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("application/json")]
        [TestCase("application/json; charset=utf-8")]
        [TestCase("application/json; charset=\"UTF-8\"")]
        [TestCase("application/json; charset=no-such-charset")]
        public void ResolveBodyEncoding_DefaultsToUtf8(string contentType)
        {
            Assert.AreEqual(System.Text.Encoding.UTF8.WebName, HttpServer.ResolveBodyEncoding(contentType).WebName);
        }

        [Test]
        public void ResolveBodyEncoding_HonorsExplicitCharset()
        {
            Assert.AreEqual(System.Text.Encoding.Unicode.WebName, HttpServer.ResolveBodyEncoding("application/json; charset=utf-16").WebName);
        }

        [Test]
        public async Task UnknownPath_Returns404()
        {
            var res = await _client.GetAsync(Url("/api/v1/missing"));
            Assert.AreEqual(System.Net.HttpStatusCode.NotFound, res.StatusCode);
        }

        [Test]
        public async Task LargeBody_ExceedingLimit_Returns413()
        {
            // MaxRequestBodyBytes を一時的に 100 バイトに下げて、超過 body を送ると 413 になることを確認。
            // チャンク読み実装のため、Content-Length を信用せず累積サイズで判定する。
            var saved = Void2610.LiminalPalette.Ipc.IpcSettings.MaxRequestBodyBytes;
            Void2610.LiminalPalette.Ipc.IpcSettings.MaxRequestBodyBytes = 100;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, Url("/api/v1/execute"));
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "smoke-token");
                // 200 バイト超のダミー body。
                req.Content = new StringContent(new string('x', 500),
                    System.Text.Encoding.UTF8, "application/json");
                var res = await _client.SendAsync(req);
                // 413 (Payload Too Large)。
                Assert.AreEqual((System.Net.HttpStatusCode)413, res.StatusCode);
            }
            finally
            {
                Void2610.LiminalPalette.Ipc.IpcSettings.MaxRequestBodyBytes = saved;
            }
        }

        [Test]
        public void PortRetry_BindsAdjacentPortWhenOccupied()
        {
            // 同じポートを取ろうとする 2 個目の HttpServer を立てて、隣接ポートにずれることを確認。
            var router = new IpcRouter();
            router.Register("GET", "/api/v1/health", new HealthEndpoint("editor", "", ""));
            using var second = new HttpServer(router, TestPort);
            second.Start();
            Assert.AreNotEqual(TestPort, second.Port, "占有ポートとは違うポートにバインドされるはず");
            Assert.AreEqual(TestPort + 1, second.Port);
        }

        private sealed class FilterCapturingService : ITestRunnerService
        {
            public string LastFilter;

            public bool TryStartRun(string mode, string filter, bool force, out string error)
            {
                LastFilter = filter;
                error = null;
                return true;
            }

            public TestRunStatus GetStatus() => TestRunStatus.Idle;
        }
    }
}
