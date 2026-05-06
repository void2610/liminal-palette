using System.Threading;
using System.Threading.Tasks;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// 指定フレーム数の経過を待つ抽象。Editor / Runtime / テストで実装を差し替えるためのインターフェース。
    /// </summary>
    public interface IFrameWaiter
    {
        /// <summary>
        /// frames 分のフレームが経過するまで非同期に待機する。
        /// frames が 0 以下の場合は即時 await 完了。
        /// </summary>
        Task WaitFramesAsync(int frames, CancellationToken ct);
    }
}
