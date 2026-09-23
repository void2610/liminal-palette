namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// 引数エディタが Enter を「確定」に使い、フローを次のステップへ進ませないことを示すデリゲート。
    ///
    /// 単一値のエディタは確定後そのまま次へ進んでよい (従来どおり <c>Func&lt;bool&gt;</c> を使う) が、
    /// [Flags] のように値を続けて足す UI では、1 回目の Enter で進まれると 2 つ目を選べない。
    /// 戻り値 true で「この Enter は消費した」を意味する。
    /// </summary>
    /// <returns>確定して Enter を消費したら true。</returns>
    internal delegate bool TryCompleteAndConsume();
}
