using System;
using VContainer;

namespace Void2610.LiminalPalette.Integration.VContainer
{
    /// <summary>
    /// VContainer の IObjectResolver を IInstanceResolver にアダプトする。
    /// インスタンスメソッドコマンド実行時に CommandExecutor から呼ばれる。
    /// </summary>
    public sealed class VContainerInstanceResolver : IInstanceResolver
    {
        private readonly IObjectResolver _container;

        public VContainerInstanceResolver(IObjectResolver container)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public object Resolve(Type type)
        {
            try
            {
                return _container.Resolve(type);
            }
            catch (VContainerException)
            {
                // 未登録型などは null で返す。CommandExecutor 側で「Instance not resolved」エラーを組み立てる。
                return null;
            }
        }
    }
}
