using System;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// インスタンスメソッドコマンドの対象型が VContainer / resolver で未解決だったことを表す。
    /// LoadScene 直後は対象シーンの DI スコープ構築が完了するまで一過性で未解決になりうるため、
    /// シナリオ実行側はこの型を検出して bounded timeout でリトライできる (恒久未登録なら timeout で Fail)。
    /// </summary>
    public sealed class InstanceUnresolvedException : InvalidOperationException
    {
        public InstanceUnresolvedException(string message) : base(message) { }
    }
}
