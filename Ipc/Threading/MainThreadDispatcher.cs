using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Void2610.LiminalPalette.Ipc.Threading
{
    /// <summary>
    /// HTTP サーバーのワーカースレッドから Unity メインスレッドへ作業を marshal する仕組み。
    /// HttpListener.GetContextAsync の継続はワーカースレッドで実行されるが、UnityEngine.* API は
    /// メインスレッド限定なので、エンドポイント内で Registry / Executor を呼ぶ際には必ずここを通す。
    ///
    /// 駆動方法:
    ///   - Editor: EditorApplication.update += MainThreadDispatcher.Tick (EditorIpcBootstrap)
    ///   - Runtime: 専用 MonoBehaviour の Update から MainThreadDispatcher.Tick を呼ぶ (RuntimeIpcBootstrap)
    ///
    /// 設計判断:
    ///   - キューは ConcurrentQueue で順序保証 (FIFO)。
    ///   - 1 Tick で最大 100 件まで処理 (それ以上は次フレームに回す)。Tick 1 回で Unity を長くブロックしない。
    ///   - メインスレッドからの呼び出しは即時実行 (Tick を待たない)。
    /// </summary>
    public static class MainThreadDispatcher
    {
        private const int MaxActionsPerTick = 100;

        private static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private static int _mainThreadId = -1;

        /// <summary>
        /// メインスレッド ID を登録する。Editor/Runtime それぞれの起動経路で 1 回だけ呼ぶ。
        /// </summary>
        public static void RegisterMainThread(int threadId) => _mainThreadId = threadId;

        public static int MainThreadId => _mainThreadId;

        /// <summary>
        /// メインスレッドで action を実行し、その完了を待つ。
        /// メインスレッドから呼ばれた場合は即時実行 (Tick を待たない)。
        /// </summary>
        public static Task<T> RunAsync<T>(Func<Task<T>> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            // メインスレッド上で呼ばれたら直接呼び出し。
            if (Thread.CurrentThread.ManagedThreadId == _mainThreadId)
            {
                return action();
            }

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue(() =>
            {
                try
                {
                    var task = action();
                    if (task == null)
                    {
                        tcs.SetException(new InvalidOperationException("MainThreadDispatcher.RunAsync: action returned null."));
                        return;
                    }
                    // task の完了を tcs にブリッジ。await 中の継続もメインスレッドで実行される
                    // (Unity の SynchronizationContext がメインスレッドに張り付いているため)。
                    task.ContinueWith(t =>
                    {
                        if (t.IsCanceled) tcs.TrySetCanceled();
                        else if (t.IsFaulted) tcs.TrySetException(t.Exception);
                        else tcs.TrySetResult(t.Result);
                    }, TaskContinuationOptions.ExecuteSynchronously);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        /// <summary>
        /// 値を返さない版。await 用に Task を返す。
        /// </summary>
        public static Task RunAsync(Func<Task> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            return RunAsync<bool>(async () => { await action(); return true; });
        }

        /// <summary>EditorApplication.update / MonoBehaviour.Update から定期呼び出し。</summary>
        public static void Tick()
        {
            for (var i = 0; i < MaxActionsPerTick; i++)
            {
                if (!_queue.TryDequeue(out var action)) break;
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        /// <summary>テスト用。キューを空にする。</summary>
        internal static void ClearForTest()
        {
            while (_queue.TryDequeue(out _)) { }
        }

        /// <summary>テスト用。キューに溜まっている件数。</summary>
        internal static int QueuedCountForTest
        {
            get
            {
                var n = 0;
                foreach (var _ in _queue) n++;
                return n;
            }
        }
    }
}
