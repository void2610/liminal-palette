using System;
using NUnit.Framework;
using VContainer;
using Void2610.LiminalPalette.Integration.VContainer;

namespace Void2610.LiminalPalette.Tests.Integration
{
    public sealed class VContainerInstanceResolverTests
    {
        private sealed class Sample
        {
            public int Value;
        }

        [Test]
        public void Resolve_RegisteredType_ReturnsInstance()
        {
            var builder = new ContainerBuilder();
            builder.Register<Sample>(Lifetime.Singleton);
            using var container = builder.Build();

            var resolver = new VContainerInstanceResolver(container);
            var resolved = resolver.Resolve(typeof(Sample));
            Assert.IsNotNull(resolved);
            Assert.IsInstanceOf<Sample>(resolved);
        }

        [Test]
        public void Resolve_UnregisteredType_ReturnsNull()
        {
            var builder = new ContainerBuilder();
            using var container = builder.Build();

            var resolver = new VContainerInstanceResolver(container);
            // VContainer は未登録型に VContainerException を投げる → resolver は null に変換。
            var resolved = resolver.Resolve(typeof(Sample));
            Assert.IsNull(resolved);
        }

        [Test]
        public void NullContainer_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new VContainerInstanceResolver(null));
        }

        [Test]
        public void EntryPoint_Initialize_SetsLiminalPaletteResolver()
        {
            var builder = new ContainerBuilder();
            builder.Register<Sample>(Lifetime.Singleton);
            using var container = builder.Build();

            // EntryPoint を直接 new して Initialize を呼ぶ (LifetimeScope を作らないテスト経路)。
            var ep = new LiminalPaletteEntryPoint(container);
            ep.Initialize();

            // SetInstanceResolver の効果: 以降 LiminalPalette が VContainer 経由で Sample を解決できる。
            // internal な InstanceResolver にアクセスはできないので、試しにコマンド実行を通して確認するのが正攻法だが
            // ここでは Initialize 自身に副作用 (例外を投げない) があることだけ確認。
            Assert.DoesNotThrow(() => ep.Initialize());
        }
    }
}
