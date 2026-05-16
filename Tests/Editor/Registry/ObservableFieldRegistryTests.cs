using System;
using NUnit.Framework;
using R3;
using Void2610.LiminalPalette;

namespace Void2610.LiminalPalette.Tests.Registry
{
    public sealed class ObservableFieldRegistryTests
    {
        // テスト対象。[LiminalObservableField] 付き ReactiveProperty<int> を持つ。
        private sealed class FakeMonoBehaviour
        {
            [LiminalObservableField("Test/Hp", Description = "test field")]
            public ReactiveProperty<int> Hp { get; } = new ReactiveProperty<int>(50);

            [LiminalObservableField("Test/Stream")]
            public Observable<int> Stream => Hp;
        }

        // 静的フィールドを公開するユーティリティ。組み込み Time/Scale のような static utility が
        // ObservableField として動くこと (IsStatic 判定、ReadCurrent(null) で値が取れる) を検証する。
        private static class StaticFakeHolder
        {
            public static readonly ReactiveProperty<int> SharedCounter = new ReactiveProperty<int>(7);

            [LiminalObservableField("Test/Static/Counter")]
            public static ReactiveProperty<int> Counter => SharedCounter;
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

        // ---- 静的フィールド対応 ----

        [Test]
        public void Scanner_FlagsStaticPropertyAsIsStatic()
        {
            var d = ObservableFieldRegistry.Default.Find("Test/Static/Counter");
            Assert.IsNotNull(d, "static ReactiveProperty<int> プロパティが登録されるべき");
            Assert.IsTrue(d.IsStatic, "static プロパティは IsStatic=true でフラグされるべき");
            Assert.AreEqual(typeof(int), d.ValueType);
        }

        [Test]
        public void Scanner_FlagsInstancePropertyAsNonStatic()
        {
            var d = ObservableFieldRegistry.Default.Find("Test/Hp");
            Assert.IsNotNull(d);
            Assert.IsFalse(d.IsStatic, "instance プロパティは IsStatic=false であるべき");
        }

        [Test]
        public void ReadCurrent_OnStaticField_AcceptsNullInstance()
        {
            // 静的 ObservableField は instance=null で ReadCurrent しても値が取れるべき
            // (UI / IPC / Scenario の static 経路はこの前提で IInstanceResolver をスキップする)。
            var d = ObservableFieldRegistry.Default.Find("Test/Static/Counter");
            StaticFakeHolder.SharedCounter.Value = 42;
            var current = d.ReadCurrent(null);
            Assert.AreEqual(42, current);
        }
    }
}
