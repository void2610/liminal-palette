namespace Void2610.LiminalPalette.Ipc.TestRunning
{
    /// <summary>
    /// <see cref="ITestRunnerService"/> の実装を Ipc レイヤに橋渡しする静的ホルダ。
    ///
    /// 編集時サブ asmdef (<c>Void2610.LiminalPalette.Editor.TestRunner</c>) が
    /// <c>[InitializeOnLoad]</c> で <see cref="Current"/> をセットする。エンドポイントは
    /// リクエスト時に <see cref="Current"/> を読むだけなので、bootstrap と登録の順序に依存しない
    /// (DomainReload のたびに両者が再実行され、null → 実装 の順で解決される)。
    ///
    /// <see cref="Current"/> が null のケース:
    ///   - <c>com.unity.test-framework</c> 未導入 (サブ asmdef がコンパイルされない)
    ///   - Runtime (Player / Play Mode) — テスト実行は編集時専用。Runtime bootstrap は
    ///     テスト系エンドポイントを登録しないが、仮に到達しても 501 になる。
    /// </summary>
    public static class TestRunnerBridge
    {
        /// <summary>登録済みの Test Runner サービス。未導入 / Runtime では null。</summary>
        public static ITestRunnerService Current { get; set; }
    }
}
