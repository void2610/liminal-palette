namespace Void2610.LiminalPalette
{
    /// <summary>
    /// <see cref="InvocationStore"/> の永続化先。
    ///
    /// Core は Runtime でもコンパイルされるため EditorPrefs を直接触れない。
    /// <see cref="ICommandHistory"/> と同じく、保存手段は外から注入する。
    /// 実装は失敗を握り潰すベストエフォートでよい (履歴が消えても実行は続けられるべき)。
    /// </summary>
    public interface IInvocationStorage
    {
        /// <summary>保存済みの生文字列。無ければ null / 空。</summary>
        string Read();

        /// <summary>生文字列を保存する。</summary>
        void Write(string raw);

        /// <summary>保存を消す。</summary>
        void Delete();
    }
}
