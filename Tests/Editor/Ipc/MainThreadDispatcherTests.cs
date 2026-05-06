using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Void2610.LiminalPalette.Ipc.Threading;

namespace Void2610.LiminalPalette.Tests.Ipc
{
    public sealed class MainThreadDispatcherTests
    {
        [SetUp]
        public void SetUp()
        {
            MainThreadDispatcher.ClearForTest();
            // テスト実行スレッドをメインスレッド扱いに登録。
            MainThreadDispatcher.RegisterMainThread(Thread.CurrentThread.ManagedThreadId);
        }

        [TearDown]
        public void TearDown() => MainThreadDispatcher.ClearForTest();

        [Test]
        public async Task RunAsync_FromMainThread_ExecutesInline()
        {
            // メインスレッドから呼ばれたら Tick を待たずに即実行されるはず。
            var result = await MainThreadDispatcher.RunAsync(() => Task.FromResult(42));
            Assert.AreEqual(42, result);
            Assert.AreEqual(0, MainThreadDispatcher.QueuedCountForTest);
        }

        [Test]
        public void RunAsync_FromWorker_QueuesUntilTickIsCalled()
        {
            // メインスレッド ID をテスト実行スレッドとは別の値にして、ワーカー扱いにさせる。
            MainThreadDispatcher.RegisterMainThread(int.MinValue);

            var task = MainThreadDispatcher.RunAsync(() => Task.FromResult(7));
            Assert.IsFalse(task.IsCompleted, "Tick 前は完了していない");
            Assert.AreEqual(1, MainThreadDispatcher.QueuedCountForTest);

            MainThreadDispatcher.Tick();

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(7, task.Result);
        }

        [Test]
        public void RunAsync_PropagatesException()
        {
            MainThreadDispatcher.RegisterMainThread(int.MinValue);
            var task = MainThreadDispatcher.RunAsync<int>(() => throw new System.InvalidOperationException("boom"));
            MainThreadDispatcher.Tick();
            Assert.IsTrue(task.IsFaulted);
            Assert.IsInstanceOf<System.InvalidOperationException>(task.Exception.InnerException);
        }

        [Test]
        public void Tick_ProcessesUpTo100PerCall()
        {
            MainThreadDispatcher.RegisterMainThread(int.MinValue);
            for (var i = 0; i < 150; i++)
            {
                MainThreadDispatcher.RunAsync(() => Task.FromResult(true));
            }
            Assert.AreEqual(150, MainThreadDispatcher.QueuedCountForTest);
            MainThreadDispatcher.Tick();
            // 100 件処理されて 50 件残る。
            Assert.AreEqual(50, MainThreadDispatcher.QueuedCountForTest);
            MainThreadDispatcher.Tick();
            Assert.AreEqual(0, MainThreadDispatcher.QueuedCountForTest);
        }

        [Test]
        public void Tick_PreservesFifoOrder()
        {
            MainThreadDispatcher.RegisterMainThread(int.MinValue);
            var order = new System.Collections.Generic.List<int>();
            for (var i = 0; i < 5; i++)
            {
                var captured = i;
                MainThreadDispatcher.RunAsync(() => { order.Add(captured); return Task.FromResult(true); });
            }
            MainThreadDispatcher.Tick();
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, order);
        }
    }
}
