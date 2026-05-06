using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace Void2610.LiminalPalette.Editor
{
    /// <summary>
    /// Edit Mode (Play していない Editor) でフレーム経過を扱うための IFrameWaiter 実装。
    /// EditorApplication.update tick を 1 フレーム = 1 tick として扱う。
    /// Play Mode 中でも一応動作するが、その場合は RuntimeFrameWaiter (Time.frameCount ベース) の方が正確。
    /// </summary>
    public sealed class EditorFrameWaiter : IFrameWaiter
    {
        public async Task WaitFramesAsync(int frames, CancellationToken ct)
        {
            for (var i = 0; i < frames; i++)
            {
                ct.ThrowIfCancellationRequested();
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                EditorApplication.CallbackFunction tick = null;
                tick = () =>
                {
                    EditorApplication.update -= tick;
                    tcs.TrySetResult(true);
                };
                EditorApplication.update += tick;
                using (ct.Register(() =>
                {
                    EditorApplication.update -= tick;
                    tcs.TrySetCanceled();
                }))
                {
                    await tcs.Task;
                }
            }
        }
    }
}
