namespace Void2610.LiminalPalette.Ipc
{
    /// <summary>
    /// IPC サーバーの起動有無・ポート・レートリミット等の設定。
    /// ScriptableObject にせず static にしているのは Phase 4 では設定変更頻度が低いため。
    /// 利用側が上書きしたい場合は [InitializeOnLoadMethod] / [RuntimeInitializeOnLoadMethod] で
    /// IpcSettings.EnableInEditor = false 等を書く。
    /// Phase 5 で UPM 化するときに ScriptableObject に格上げする選択肢を残す。
    /// </summary>
    public static class IpcSettings
    {
        /// <summary>既定ポート。占有時は隣接ポート (port+1, port+2, ...) に最大 5 回リトライ。</summary>
        public const int DefaultPort = 7610;

        /// <summary>ポート競合時のリトライ回数。</summary>
        public const int PortRetryCount = 5;

        /// <summary>Editor で IPC サーバーを起動するか。</summary>
        public static bool EnableInEditor = true;

        /// <summary>Runtime (Player) で IPC サーバーを起動するか。Production 判定は ProductionGuard 側でも行う。</summary>
        public static bool EnableInRuntime = true;

        /// <summary>Execute エンドポイントのレートリミット (1 秒あたりの最大実行数)。</summary>
        public static int ExecuteRateLimitPerSecond = 30;

        /// <summary>HTTP リクエスト body の最大サイズ。これより大きい body は 413 で拒否。</summary>
        public static int MaxRequestBodyBytes = 1024 * 1024; // 1 MB
    }
}
