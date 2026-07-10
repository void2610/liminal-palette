using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Void2610.LiminalPalette.Ipc.Endpoints;
using Void2610.LiminalPalette.Ipc.Server;
using Void2610.LiminalPalette.Ipc.TestRunning;
using Void2610.LiminalPalette.Ipc.Threading;

namespace Void2610.LiminalPalette.Tests.Ipc
{
    /// <summary>
    /// RunTestsEndpoint / TestResultEndpoint の単体テスト。
    /// 実際の TestRunnerApi は編集時 UI からしか駆動できないため、<see cref="TestRunnerBridge.Current"/>
    /// にフェイクの <see cref="ITestRunnerService"/> を差し込んで JSON 整形と分岐だけを検証する。
    /// </summary>
    public sealed class TestRunnerEndpointsTests
    {
        private ITestRunnerService _saved;

        [SetUp]
        public void SetUp()
        {
            MainThreadDispatcher.RegisterMainThread(Thread.CurrentThread.ManagedThreadId);
            MainThreadDispatcher.ClearForTest();
            _saved = TestRunnerBridge.Current;
        }

        [TearDown]
        public void TearDown()
        {
            TestRunnerBridge.Current = _saved;
        }

        private static IpcRequest Get(string path)
            => new IpcRequest("GET", path, null, null, "");

        private static IpcRequest PostJson(string path, string body)
            => new IpcRequest("POST", path, null, null, body);

        // ---------- 認証 ----------

        [Test]
        public void RunTests_RequiresAuth() => Assert.IsTrue(new RunTestsEndpoint().RequiresAuth);

        [Test]
        public void TestResult_RequiresAuth() => Assert.IsTrue(new TestResultEndpoint().RequiresAuth);

        // ---------- 未登録 (test-framework 未導入) ----------

        [Test]
        public async Task RunTests_WhenServiceUnavailable_Returns501()
        {
            TestRunnerBridge.Current = null;
            var res = await new RunTestsEndpoint().HandleAsync(
                PostJson("/api/v1/tests/run", "{\"mode\":\"playmode\"}"), CancellationToken.None);
            Assert.AreEqual(501, res.StatusCode);
            StringAssert.Contains("com.unity.test-framework", res.Body);
        }

        [Test]
        public async Task TestResult_WhenServiceUnavailable_Returns501()
        {
            TestRunnerBridge.Current = null;
            var res = await new TestResultEndpoint().HandleAsync(
                Get("/api/v1/tests/result"), CancellationToken.None);
            Assert.AreEqual(501, res.StatusCode);
        }

        // ---------- body 検証 ----------

        [Test]
        public async Task RunTests_MissingMode_Returns400()
        {
            TestRunnerBridge.Current = new FakeService();
            var res = await new RunTestsEndpoint().HandleAsync(
                PostJson("/api/v1/tests/run", "{}"), CancellationToken.None);
            Assert.AreEqual(400, res.StatusCode);
        }

        [Test]
        public async Task RunTests_InvalidMode_Returns400()
        {
            TestRunnerBridge.Current = new FakeService();
            var res = await new RunTestsEndpoint().HandleAsync(
                PostJson("/api/v1/tests/run", "{\"mode\":\"bogus\"}"), CancellationToken.None);
            Assert.AreEqual(400, res.StatusCode);
        }

        [Test]
        public async Task RunTests_EmptyBody_Returns400()
        {
            TestRunnerBridge.Current = new FakeService();
            var res = await new RunTestsEndpoint().HandleAsync(
                PostJson("/api/v1/tests/run", ""), CancellationToken.None);
            Assert.AreEqual(400, res.StatusCode);
        }

        // ---------- 開始 ----------

        [Test]
        public async Task RunTests_PlayMode_StartsAndReturns200()
        {
            var fake = new FakeService();
            TestRunnerBridge.Current = fake;
            var res = await new RunTestsEndpoint().HandleAsync(
                PostJson("/api/v1/tests/run", "{\"mode\":\"playmode\",\"filter\":\"My.Ns.*\"}"),
                CancellationToken.None);
            Assert.AreEqual(200, res.StatusCode);
            StringAssert.Contains("\"status\":\"started\"", res.Body);
            StringAssert.Contains("\"mode\":\"PlayMode\"", res.Body);
            StringAssert.Contains("\"filter\":\"My.Ns.*\"", res.Body);
            Assert.AreEqual("playmode", fake.LastMode);
            Assert.AreEqual("My.Ns.*", fake.LastFilter);
        }

        [Test]
        public async Task RunTests_EditModeCaseInsensitive_Normalized()
        {
            var fake = new FakeService();
            TestRunnerBridge.Current = fake;
            var res = await new RunTestsEndpoint().HandleAsync(
                PostJson("/api/v1/tests/run", "{\"mode\":\"EditMode\"}"), CancellationToken.None);
            Assert.AreEqual(200, res.StatusCode);
            StringAssert.Contains("\"mode\":\"EditMode\"", res.Body);
            StringAssert.Contains("\"filter\":\"all\"", res.Body);
            Assert.AreEqual("editmode", fake.LastMode);
        }

        [Test]
        public async Task RunTests_AlreadyRunning_Returns409()
        {
            TestRunnerBridge.Current = new FakeService { StartSucceeds = false };
            var res = await new RunTestsEndpoint().HandleAsync(
                PostJson("/api/v1/tests/run", "{\"mode\":\"playmode\"}"), CancellationToken.None);
            Assert.AreEqual(409, res.StatusCode);
            StringAssert.Contains("\"status\":\"running\"", res.Body);
        }

        // ---------- 結果 ----------

        [Test]
        public async Task TestResult_Idle()
        {
            TestRunnerBridge.Current = new FakeService { Status = TestRunStatus.Idle };
            var res = await new TestResultEndpoint().HandleAsync(
                Get("/api/v1/tests/result"), CancellationToken.None);
            Assert.AreEqual(200, res.StatusCode);
            StringAssert.Contains("\"state\":\"idle\"", res.Body);
        }

        [Test]
        public async Task TestResult_Running()
        {
            TestRunnerBridge.Current = new FakeService { Status = TestRunStatus.Running("PlayMode") };
            var res = await new TestResultEndpoint().HandleAsync(
                Get("/api/v1/tests/result"), CancellationToken.None);
            StringAssert.Contains("\"state\":\"running\"", res.Body);
            StringAssert.Contains("\"mode\":\"PlayMode\"", res.Body);
        }

        [Test]
        public async Task TestResult_Completed_IncludesCounts()
        {
            TestRunnerBridge.Current = new FakeService
            {
                Status = new TestRunStatus(TestRunPhase.Completed, "Passed", 12, 0, 1, 0, 3.5, "PlayMode"),
            };
            var res = await new TestResultEndpoint().HandleAsync(
                Get("/api/v1/tests/result"), CancellationToken.None);
            StringAssert.Contains("\"state\":\"completed\"", res.Body);
            StringAssert.Contains("\"result\":\"Passed\"", res.Body);
            StringAssert.Contains("\"passed\":12", res.Body);
            StringAssert.Contains("\"failed\":0", res.Body);
            StringAssert.Contains("\"skipped\":1", res.Body);
        }

        private sealed class FakeService : ITestRunnerService
        {
            public bool StartSucceeds = true;
            public TestRunStatus Status = TestRunStatus.Idle;
            public string LastMode;
            public string LastFilter;

            public bool TryStartRun(string mode, string filter, out string error)
            {
                LastMode = mode;
                LastFilter = filter;
                if (!StartSucceeds)
                {
                    error = "already running";
                    return false;
                }
                error = null;
                return true;
            }

            public TestRunStatus GetStatus() => Status;
        }
    }
}
