using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using R3;
using Void2610.LiminalPalette;
using Void2610.LiminalPalette.Ipc.Endpoints;
using Void2610.LiminalPalette.Ipc.Server;
using Void2610.LiminalPalette.Ipc.Threading;

namespace Void2610.LiminalPalette.Tests.Ipc
{
    /// <summary>
    /// GetStateEndpoint のテスト。
    /// ObservableFieldRegistry に手動登録 + Stub IInstanceResolver でインスタンス解決を制御する。
    /// </summary>
    public sealed class GetStateEndpointTests
    {
        private sealed class FakeOwner
        {
            public ReactiveProperty<int> Hp { get; } = new ReactiveProperty<int>(75);
        }

        private sealed class StubResolver : IInstanceResolver
        {
            private readonly Dictionary<Type, object> _map = new Dictionary<Type, object>();
            public StubResolver(params object[] instances)
            {
                foreach (var i in instances) _map[i.GetType()] = i;
            }
            public object Resolve(Type type) => _map.TryGetValue(type, out var v) ? v : null;
        }

        [SetUp]
        public void SetUp()
        {
            MainThreadDispatcher.RegisterMainThread(Thread.CurrentThread.ManagedThreadId);
            MainThreadDispatcher.ClearForTest();
            ObservableFieldRegistry.Default.ClearForTest();
            LiminalPalette.SetInstanceResolver(null); // Null
        }

        [TearDown]
        public void TearDown()
        {
            ObservableFieldRegistry.Default.ClearForTest();
            LiminalPalette.SetInstanceResolver(null);
        }

        private static IpcRequest GetWithQuery(string path)
            => new IpcRequest("GET", "/api/v1/state",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["path"] = path },
                null, "");

        private static IpcRequest GetAll() => new IpcRequest("GET", "/api/v1/state", null, null, "");

        // ObservableFieldDescriptor を直接組み立てて Registry に登録するヘルパ。
        private static void RegisterFakeField(string path, FakeOwner owner)
        {
            var d = new ObservableFieldDescriptor(
                path: path,
                description: "",
                declaringType: typeof(FakeOwner),
                valueType: typeof(int),
                readCurrent: instance => ((FakeOwner)instance).Hp.Value,
                subscribe: (instance, onNext) => ((FakeOwner)instance).Hp.Subscribe(v => onNext(v)));
            ObservableFieldRegistry.Default.Register(d);
        }

        [Test]
        public async Task GetByPath_ReturnsCurrentValue()
        {
            var owner = new FakeOwner { };
            owner.Hp.Value = 42;
            LiminalPalette.SetInstanceResolver(new StubResolver(owner));
            RegisterFakeField("Test/Hp", owner);

            var ep = new GetStateEndpoint();
            var res = await ep.HandleAsync(GetWithQuery("Test/Hp"), CancellationToken.None);
            Assert.AreEqual(200, res.StatusCode);
            StringAssert.Contains("\"path\":\"Test/Hp\"", res.Body);
            StringAssert.Contains("\"value\":\"42\"", res.Body);
        }

        [Test]
        public async Task GetByPath_UnknownPath_404()
        {
            var ep = new GetStateEndpoint();
            var res = await ep.HandleAsync(GetWithQuery("Does/Not/Exist"), CancellationToken.None);
            Assert.AreEqual(404, res.StatusCode);
        }

        [Test]
        public async Task GetByPath_InstanceNotResolved_500()
        {
            // resolver は null のまま (NullInstanceResolver) → 500
            var owner = new FakeOwner();
            RegisterFakeField("Test/Hp", owner);

            var ep = new GetStateEndpoint();
            var res = await ep.HandleAsync(GetWithQuery("Test/Hp"), CancellationToken.None);
            Assert.AreEqual(500, res.StatusCode);
            StringAssert.Contains("Instance not resolved", res.Body);
        }

        [Test]
        public async Task GetAll_ReturnsList()
        {
            var owner = new FakeOwner();
            owner.Hp.Value = 10;
            LiminalPalette.SetInstanceResolver(new StubResolver(owner));
            RegisterFakeField("Test/Hp", owner);

            var ep = new GetStateEndpoint();
            var res = await ep.HandleAsync(GetAll(), CancellationToken.None);
            Assert.AreEqual(200, res.StatusCode);
            StringAssert.Contains("\"fields\":[", res.Body);
            StringAssert.Contains("\"path\":\"Test/Hp\"", res.Body);
            StringAssert.Contains("\"value\":\"10\"", res.Body);
            StringAssert.Contains("\"instanceResolved\":true", res.Body);
        }

        [Test]
        public void RequiresAuth()
        {
            Assert.IsTrue(new GetStateEndpoint().RequiresAuth);
        }
    }
}
