using System;
using NUnit.Framework;
using R3;
using Void2610.LiminalPalette;

namespace Void2610.LiminalPalette.Tests.Registry
{
    public sealed class ObservableFieldRegistryTests
    {
        // テスト対象。[ConsoleObservableField] 付き ReactiveProperty<int> を持つ。
        private sealed class FakeMonoBehaviour
        {
            [ConsoleObservableField("Test/Hp", Description = "test field")]
            public ReactiveProperty<int> Hp { get; } = new ReactiveProperty<int>(50);

            [ConsoleObservableField("Test/Stream")]
            public Observable<int> Stream => Hp;
        }

        [SetUp]
        public void SetUp()
        {
            ObservableFieldRegistry.Default.ClearForTest();
            // ScanAll は全 Assembly を見るのでテスト用の独自コンテナクラスは普通に拾われる。
            ObservableFieldScanner.ScanAll();
        }

        [TearDown]
        public void TearDown() => ObservableFieldRegistry.Default.ClearForTest();

        [Test]
        public void Scanner_FindsReactivePropertyField()
        {
            var d = ObservableFieldRegistry.Default.Find("Test/Hp");
            Assert.IsNotNull(d, "ReactiveProperty<int> が登録されるべき");
            Assert.AreEqual(typeof(int), d.ValueType);
            Assert.AreEqual(typeof(FakeMonoBehaviour), d.DeclaringType);
        }

        [Test]
        public void Scanner_FindsObservableField()
        {
            var d = ObservableFieldRegistry.Default.Find("Test/Stream");
            Assert.IsNotNull(d);
            Assert.AreEqual(typeof(int), d.ValueType);
        }

        [Test]
        public void ReadCurrent_ReturnsReactivePropertyValue()
        {
            var d = ObservableFieldRegistry.Default.Find("Test/Hp");
            var instance = new FakeMonoBehaviour();
            instance.Hp.Value = 77;
            var current = d.ReadCurrent(instance);
            Assert.AreEqual(77, current);
        }

        [Test]
        public void Subscribe_PushesValueOnChange()
        {
            // R3 の ReactiveProperty<T> は Subscribe 直後に現在値を push する仕様だが、
            // ObservableExtensions.Subscribe<T> 経由 (Reflection で取得した汎用 Subscribe) では
            // 初回 push が起きない実装パスもあり得るため、本テストでは「Value 変更時に値が来る」
            // 一点に絞って検証する (UI 側は ReadCurrent で初期値を得るので両方そろえば OK)。
            var d = ObservableFieldRegistry.Default.Find("Test/Hp");
            var instance = new FakeMonoBehaviour();
            object lastValue = null;
            using var sub = d.Subscribe(instance, v => lastValue = v);
            instance.Hp.Value = 99;
            Assert.AreEqual(99, lastValue);
        }

        [Test]
        public void FindByPathPrefix_ReturnsMatching()
        {
            var matches = ObservableFieldRegistry.Default.FindByPathPrefix("Test/");
            Assert.GreaterOrEqual(matches.Count, 2);
        }

        [Test]
        public void FindByPathPrefix_NoMatch_ReturnsEmpty()
        {
            var matches = ObservableFieldRegistry.Default.FindByPathPrefix("Nonexistent/");
            Assert.AreEqual(0, matches.Count);
        }
    }
}
