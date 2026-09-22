namespace Void2610.LiminalPalette
{
    /// <summary>
    /// コマンド実行がどの経路から起きたか。<see cref="InvocationStore"/> の保持枠と
    /// History タブの表示対象を決めるための分類。
    /// </summary>
    public enum InvocationOrigin
    {
        /// <summary>パレット UI からの手動実行 (History タブの再実行対象)。</summary>
        User = 0,

        /// <summary>HTTP API (CLI / MCP / E2E ランナー) からの実行。</summary>
        Ipc = 1,

        /// <summary>シナリオ実行由来 (個別ステップ + シナリオ集約)。</summary>
        Scenario = 2,
    }
}
