namespace Void2610.LiminalPalette
{
    /// <summary>
    /// 直前に待ったフレームで進んだゲーム内の秒数を返せる <see cref="IFrameWaiter"/> が実装する。
    /// これを実装する待ち手では、AssertEventually の打ち切りを実時間ではなくゲームの進みで決める
    /// </summary>
    public interface IFrameTimeSource
    {
        /// <summary>直前のフレームで進んだゲーム内の秒数</summary>
        float FrameDeltaSeconds { get; }
    }
}
