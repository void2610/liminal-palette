using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// Time.frameCount をベースにフレーム経過を判定する Runtime / Play Mode 向け実装。
    /// メインスレッドで await されることを前提とする (Time.frameCount はメインスレッド限定)。
    /// </summary>
    public sealed class RuntimeFrameWaiter : IFrameWaiter
    {
        public async Task WaitFramesAsync(int frames, CancellationToken ct)
        {
            if (frames <= 0) return;
            var startFrame = Time.frameCount;
            // frameCount は Update のたびに進む。Task.Yield で SynchronizationContext (メインスレッド) に
            // 戻りながらフレーム送りを待つ。
            while (Time.frameCount - startFrame < frames)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }
    }
}
