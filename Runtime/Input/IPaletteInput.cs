namespace Void2610.LiminalPalette.Runtime
{
    /// <summary>
    /// Runtime におけるパレットの入力経路の抽象化。
    /// 実装は LegacyPaletteInput (UnityEngine.Input) / InputSystemPaletteInput (com.unity.inputsystem) / NoOpPaletteInput (ヘッドレス・無効化) の 3 種。
    /// LiminalPaletteRuntime が毎フレーム ConsumeXxx を呼び、押下エッジが取れた時のみ true を返す前提。
    ///
    /// 注: Up/Down/Confirm/Cancel/Tab は UIDocument がフォーカスを保持していれば PaletteView の KeyDownEvent ハンドラが
    /// 拾ってくれるため、本インタフェースは「フォーカス外で開く Toggle と、fallback としての UI ナビ」という二重経路を用意するもの。
    /// </summary>
    public interface IPaletteInput
    {
        /// <summary>パレットの開閉トグル (修飾キー + ToggleKey)。1 フレームに 1 回だけ true。</summary>
        bool ConsumeToggle();

        /// <summary>結果リスト 1 つ上 (押下エッジ)。UIDocument がフォーカスを失ったときのフォールバック。</summary>
        bool ConsumeUp();

        /// <summary>結果リスト 1 つ下 (押下エッジ)。</summary>
        bool ConsumeDown();

        /// <summary>選択中コマンドの実行 (押下エッジ)。</summary>
        bool ConsumeConfirm();

        /// <summary>パレットを閉じる (押下エッジ)。</summary>
        bool ConsumeCancel();

        /// <summary>タブ移動 (押下エッジ)。Shift 同時押しなら shift = true。</summary>
        bool ConsumeTab(out bool shift);
    }
}
