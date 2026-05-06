using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Void2610.LiminalPalette.Ipc.Auth;
using Void2610.LiminalPalette.Ipc.Endpoints;
using Void2610.LiminalPalette.Ipc.Server;

namespace Void2610.LiminalPalette.Tests.Ipc
{
    public sealed class IpcRouterTests
    {
        // テスト用 endpoint。
        private sealed class StubEndpoint : IIpcEndpoint
        {
            private readonly IpcResponse _response;
            public bool RequiresAuth { get; }
            public bool Called { get; private set; }
            public StubEndpoint(IpcResponse response, bool requiresAuth)
            {
                _response = response;
                RequiresAuth = requiresAuth;
            }
            public Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken ct)
            {
                Called = true;
                return Task.FromResult(_response);
            }
        }

        private sealed class ThrowingEndpoint : IIpcEndpoint
        {
            public bool RequiresAuth => false;
            public Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken ct)
                => throw new System.InvalidOperationException("boom");
        }

        private static IpcRequest Req(string method, string path, string token = null)
        {
            var headers = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            if (token != null) headers["Authorization"] = "Bearer " + token;
            return new IpcRequest(method, path, null, headers, "");
        }

        [Test]
        public async Task RouteAsync_RegisteredEndpoint_IsCalled()
        {
            var router = new IpcRouter();
            var stub = new StubEndpoint(IpcResponse.PlainText(200, "ok"), requiresAuth: false);
            router.Register("GET", "/api/v1/x", stub);

            var res = await router.RouteAsync(Req("GET", "/api/v1/x"), CancellationToken.None);
            Assert.AreEqual(200, res.StatusCode);
            Assert.IsTrue(stub.Called);
        }

        [Test]
        public async Task RouteAsync_UnknownPath_Returns404()
        {
            var router = new IpcRouter();
            var res = await router.RouteAsync(Req("GET", "/missing"), CancellationToken.None);
            Assert.AreEqual(404, res.StatusCode);
        }

        [Test]
        public async Task RouteAsync_KnownPathDifferentMethod_Returns405()
        {
            var router = new IpcRouter();
            router.Register("POST", "/api/v1/x", new StubEndpoint(IpcResponse.PlainText(200, "ok"), false));
            var res = await router.RouteAsync(Req("GET", "/api/v1/x"), CancellationToken.None);
            Assert.AreEqual(405, res.StatusCode);
        }

        [Test]
        public async Task RouteAsync_RequiresAuth_NoToken_Returns401()
        {
            var auth = new TokenAuthenticator("token");
            var router = new IpcRouter(auth);
            router.Register("GET", "/api/v1/secure", new StubEndpoint(IpcResponse.PlainText(200, "ok"), requiresAuth: true));
            var res = await router.RouteAsync(Req("GET", "/api/v1/secure"), CancellationToken.None);
            Assert.AreEqual(401, res.StatusCode);
        }

        [Test]
        public async Task RouteAsync_RequiresAuth_ValidToken_Returns200()
        {
            var auth = new TokenAuthenticator("token");
            var router = new IpcRouter(auth);
            router.Register("GET", "/api/v1/secure", new StubEndpoint(IpcResponse.PlainText(200, "ok"), requiresAuth: true));
            var res = await router.RouteAsync(Req("GET", "/api/v1/secure", "token"), CancellationToken.None);
            Assert.AreEqual(200, res.StatusCode);
        }

        [Test]
        public async Task RouteAsync_EndpointThrows_Returns500()
        {
            var router = new IpcRouter();
            router.Register("GET", "/api/v1/boom", new ThrowingEndpoint());
            var res = await router.RouteAsync(Req("GET", "/api/v1/boom"), CancellationToken.None);
            Assert.AreEqual(500, res.StatusCode);
        }

        [Test]
        public async Task RouteAsync_MethodCaseInsensitive()
        {
            var router = new IpcRouter();
            router.Register("get", "/api/v1/x", new StubEndpoint(IpcResponse.PlainText(200, "ok"), false));
            var res = await router.RouteAsync(Req("GET", "/api/v1/x"), CancellationToken.None);
            Assert.AreEqual(200, res.StatusCode);
        }
    }
}
