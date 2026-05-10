using System;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// インスタンスメソッドの [LiminalCommand] を実行する際に、
    /// メソッドが属する型のインスタンスを解決するための抽象。
    /// VContainer / Zenject など DI コンテナと連携する想定。
    /// 解決できない場合は null を返す (例外は投げない)。
    /// </summary>
    public interface IInstanceResolver
    {
        /// <summary>type のインスタンスを取得する。解決できない場合は null。</summary>
        object Resolve(Type type);
    }
}
