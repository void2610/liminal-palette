using System;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// 既定の IInstanceResolver。常に null を返す。
    /// 利用側が SetInstanceResolver を呼んでいない状態でインスタンスメソッドが叩かれた場合、
    /// この resolver が null を返し、CommandExecutor が「インスタンス未解決」エラーで Fail する。
    /// </summary>
    internal sealed class NullInstanceResolver : IInstanceResolver
    {
        public object Resolve(Type type) => null;
    }
}
