using System;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// テスト実行中に利用者の永続データ (EditorPrefs / PlayerPrefs の本番キー) を
    /// 書き換えようとしたら止めるための門番。
    ///
    /// EditMode テストは Editor と同じプロセス / 同じ EditorPrefs を共有するため、
    /// 本番キーに書くテストを 1 つ書くだけで利用者の履歴が実際に消える。
    /// 過去に 2 度これをやっており、レビューでは止められなかったので仕組みで落とす。
    ///
    /// 読み取りは許可する (パレットウィンドウの生成等で正当に読むため)。止めるのは書き込みと削除だけ。
    /// </summary>
    public static class ProductionStateGuard
    {
        /// <summary>
        /// テスト実行中か。Test Runner のコールバックから設定する
        /// (Test Runner ウィンドウ / HTTP API のどちらから起動されても効く)。
        /// </summary>
        public static bool TestRunInProgress { get; set; }

        /// <summary>テスト実行中なら例外を投げる。<paramref name="what"/> は診断用の対象名。</summary>
        public static void ThrowIfTestRun(string what)
        {
            if (!TestRunInProgress) return;
            throw new ProductionStateViolationException(
                $"テスト実行中に本番の永続データ ({what}) を書き換えようとしました。" +
                "テストは専用キー / 専用インスタンスを使ってください " +
                "(EditorCommandHistory / PlayerPrefsCommandHistory / EditorPrefsInvocationStorage は " +
                "internal コンストラクタでキーを差し替えられます)。");
        }
    }

    /// <summary>テスト実行中に本番の永続データを書き換えようとしたときに投げる。</summary>
    public sealed class ProductionStateViolationException : Exception
    {
        public ProductionStateViolationException(string message) : base(message)
        {
        }
    }
}
