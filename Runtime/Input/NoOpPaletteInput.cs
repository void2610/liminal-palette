namespace Void2610.LiminalPalette.Runtime
{
    /// <summary>
    /// すべて false を返す入力実装。ヘッドレス環境やテスト、入力モジュールが利用できないシーン用。
    /// </summary>
    public sealed class NoOpPaletteInput : IPaletteInput
    {
        public bool ConsumeToggle() => false;
        public bool ConsumeUp() => false;
        public bool ConsumeDown() => false;
        public bool ConsumeConfirm() => false;
        public bool ConsumeCancel() => false;
        public bool ConsumeTab(out bool shift) { shift = false; return false; }
    }
}
