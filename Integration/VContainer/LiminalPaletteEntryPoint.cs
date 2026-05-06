using VContainer;
using VContainer.Unity;

namespace Void2610.LiminalPalette.Integration.VContainer
{
    /// <summary>
    /// VContainer の LifetimeScope.Configure で
    ///   builder.RegisterEntryPoint&lt;LiminalPaletteEntryPoint&gt;();
    /// と登録すると、VContainer の Initialize 段階で LiminalPalette.SetInstanceResolver を
    /// 呼び、コンテナ全体の解決経路をライブラリに繋ぐ。
    /// </summary>
    public sealed class LiminalPaletteEntryPoint : IInitializable
    {
        private readonly IObjectResolver _container;

        public LiminalPaletteEntryPoint(IObjectResolver container)
        {
            _container = container;
        }

        public void Initialize()
        {
            LiminalPalette.SetInstanceResolver(new VContainerInstanceResolver(_container));
        }
    }
}
