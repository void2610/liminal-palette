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
    /// /api/v1/scenarios と /api/v1/scenarios/run の単体テスト。
    /// </summary>
    public sealed class ScenariosEndpointsTests
    {
        [SetUp]
        public void SetUp()
        {
            MainThreadDispatcher.RegisterMainThread(Thread.CurrentThread.ManagedThreadId);
            MainThreadDispatcher.ClearForTest();

            // ScenarioRegistry に TestScenarios を登録しておく (Bootstrap が動かないテスト環境向け)。
            ScenarioRegistry.Default.Clear();
            var scenarios = ScenarioScanner.Scan(new[] { typeof(TestScenarios).Assembly });
            foreach (var s in scenarios) ScenarioRegistry.Default.Register(s);

            // CommandRegistry には TestCommands を登録 (TestCommands は static なので Bootstrap 経由で
            // 既に入っているはずだが、テスト独立性のため明示的に保証する)。
            if (CommandRegistry.Default.Find("Test/NoArg") == null)
            {
                var cmds = AttributeScanner.Scan(new[] { typeof(TestCommands).Assembly });
                foreach (var c in cmds) CommandRegistry.Default.Register(c);
            }
        }

        [TearDown]
        public void TearDown()
        {
            // テストが Default レジストリを汚染するため、最後に元の (起動時 ScanAll で構築される) 状態に戻す。
            // Clear だけだと UI の Scenario タブがドメインリロードまで空のままになる。
            ScenarioRegistry.Default.Clear();
            ScenarioScanner.ScanAll();
        }

        private static IpcRequest Get(string path)
            => new IpcRequest("GET", path, null, null, "");

        private static IpcRequest PostJson(string path, string body)
            => new IpcRequest("POST", path, null, null, body);

        // ---------- ListScenariosEndpoint ----------

        [Test]
        public async Task List_ReturnsRegisteredScenarios()
        {
            var ep = new ListScenariosEndpoint();
            var res = await ep.HandleAsync(Get("/api/v1/scenarios"), CancellationToken.None);
            Assert.AreEqual(200, res.StatusCode);
            StringAssert.Contains("\"scenarios\":[", res.Body);
            StringAssert.Contains("TestScenario/SingleCommand", res.Body);
        }

        [Test]
        public void List_RequiresAuth()
        {
            Assert.IsTrue(new ListScenariosEndpoint().RequiresAuth);
        }

        // ---------- RunScenarioEndpoint ----------

        [Test]
        public async Task Run_NamedScenario_Succeeds()
        {
            var ep = new RunScenarioEndpoint();
            var res = await ep.HandleAsync(
                PostJson("/api/v1/scenarios/run", "{\"path\":\"TestScenario/SingleCommand\"}"),
                CancellationToken.None);
            Assert.AreEqual(200, res.StatusCode);
            StringAssert.Contains("\"success\":true", res.Body);
        }

        [Test]
        public async Task Run_AdHocSteps_Succeeds()
        {
            var ep = new RunScenarioEndpoint();
            var body = "{\"steps\":[{\"type\":\"command\",\"path\":\"Test/NoArg\"}]}";
            var res = await ep.HandleAsync(PostJson("/api/v1/scenarios/run", body), CancellationToken.None);
            Assert.AreEqual(200, res.StatusCode);
            StringAssert.Contains("\"success\":true", res.Body);
        }

        [Test]
        public async Task Run_UnknownScenario_ReturnsFailure()
        {
            var ep = new RunScenarioEndpoint();
            var res = await ep.HandleAsync(
                PostJson("/api/v1/scenarios/run", "{\"path\":\"DoesNot/Exist\"}"),
                CancellationToken.None);
            Assert.AreEqual(200, res.StatusCode);
            StringAssert.Contains("\"success\":false", res.Body);
            StringAssert.Contains("not found", res.Body);
        }

        [Test]
        public async Task Run_BothPathAndSteps_BadRequest()
        {
            var ep = new RunScenarioEndpoint();
            var body = "{\"path\":\"X\",\"steps\":[]}";
            var res = await ep.HandleAsync(PostJson("/api/v1/scenarios/run", body), CancellationToken.None);
            Assert.AreEqual(400, res.StatusCode);
        }

        [Test]
        public async Task Run_NeitherPathNorSteps_BadRequest()
        {
            var ep = new RunScenarioEndpoint();
            var res = await ep.HandleAsync(PostJson("/api/v1/scenarios/run", "{}"), CancellationToken.None);
            Assert.AreEqual(400, res.StatusCode);
        }

        [Test]
        public async Task Run_UnknownStepType_BadRequest()
        {
            var ep = new RunScenarioEndpoint();
            var body = "{\"steps\":[{\"type\":\"frobnicate\"}]}";
            var res = await ep.HandleAsync(PostJson("/api/v1/scenarios/run", body), CancellationToken.None);
            Assert.AreEqual(400, res.StatusCode);
        }

        [Test]
        public void TryParseBody_ParsesAdHocSteps()
        {
            var body = "{\"steps\":[" +
                       "{\"type\":\"command\",\"path\":\"Test/NoArg\"}," +
                       "{\"type\":\"wait_frames\",\"frames\":3}," +
                       "{\"type\":\"assert_equals\",\"path\":\"X/Y\",\"expected\":\"100\"}]}";
            var ok = RunScenarioEndpoint.TryParseBody(body, out var path, out var steps, out var err);
            Assert.IsTrue(ok, err);
            Assert.IsNull(path);
            Assert.AreEqual(3, steps.Count);
            Assert.AreEqual(ScenarioStepKind.Command, steps[0].Kind);
            Assert.AreEqual(ScenarioStepKind.WaitFrames, steps[1].Kind);
            Assert.AreEqual(ScenarioStepKind.AssertEquals, steps[2].Kind);
        }

        [Test]
        public void TryParseBody_ParsesLoadSceneStep()
        {
            var body = "{\"steps\":[{\"type\":\"load_scene\",\"sceneName\":\"TestScene\"}]}";
            var ok = RunScenarioEndpoint.TryParseBody(body, out _, out var steps, out var err);
            Assert.IsTrue(ok, err);
            Assert.AreEqual(1, steps.Count);
            Assert.AreEqual(ScenarioStepKind.LoadScene, steps[0].Kind);
            Assert.AreEqual("TestScene", ((LoadSceneStep)steps[0]).SceneName);
        }

        [Test]
        public void TryParseBody_LoadSceneWithoutSceneName_Fails()
        {
            var body = "{\"steps\":[{\"type\":\"load_scene\"}]}";
            var ok = RunScenarioEndpoint.TryParseBody(body, out _, out _, out var err);
            Assert.IsFalse(ok);
            StringAssert.Contains("sceneName", err);
        }

        [Test]
        public void TryParseBody_ParsesAssertCommandReturnsStep()
        {
            var body = "{\"steps\":[{\"type\":\"assert_command_returns\",\"path\":\"Foo/Bar\",\"args\":{\"x\":\"1\"},\"expected\":\"ok\"}]}";
            var ok = RunScenarioEndpoint.TryParseBody(body, out _, out var steps, out var err);
            Assert.IsTrue(ok, err);
            Assert.AreEqual(1, steps.Count);
            Assert.AreEqual(ScenarioStepKind.AssertCommandReturns, steps[0].Kind);
            var s = (AssertCommandReturnsStep)steps[0];
            Assert.AreEqual("Foo/Bar", s.CommandPath);
            Assert.AreEqual("ok", s.Expected);
            Assert.AreEqual("1", s.Args["x"]);
        }

        [Test]
        public void TryParseBody_AssertCommandReturnsWithoutPath_Fails()
        {
            var body = "{\"steps\":[{\"type\":\"assert_command_returns\",\"expected\":\"ok\"}]}";
            var ok = RunScenarioEndpoint.TryParseBody(body, out _, out _, out var err);
            Assert.IsFalse(ok);
            StringAssert.Contains("path", err);
        }
    }
}
