using System;
using NUnit.Framework;
using Void2610.LiminalPalette;

namespace Void2610.LiminalPalette.Tests.UI
{
    /// <summary>
    /// InvocationStore の保持枠のテスト。
    /// 手動実行と自動化由来 (IPC / シナリオ) が独立した容量で回ることを検証する。
    /// </summary>
    public sealed class InvocationStoreTests
    {
        // Instance は Editor の Log / History タブが参照している実ストア。
        // ここを Clear すると利用者の記録が実際に消えるので、テストは専用インスタンスを使う。
        private InvocationStore _store;

        [SetUp]
        public void SetUp() => _store = new InvocationStore();

        private static CommandResult Ok() => CommandResult.Ok(null, Array.Empty<LogEntry>(), TimeSpan.Zero);

        private void Record(string path, InvocationOrigin origin)
            => _store.Record(path, null, Ok(), origin);

        [Test]
        public void AutomatedFlood_DoesNotEvictUserEntries()
        {
            Record("User/First", InvocationOrigin.User);
            for (var i = 0; i < InvocationStore.AutomatedCapacity * 2; i++)
            {
                Record($"Auto/{i}", i % 2 == 0 ? InvocationOrigin.Ipc : InvocationOrigin.Scenario);
            }

            var entries = _store.Entries;
            var userEntries = new System.Collections.Generic.List<CommandInvocation>();
            foreach (var e in entries)
            {
                if (!e.IsAutomated) userEntries.Add(e);
            }
            Assert.AreEqual(1, userEntries.Count, "自動化由来が溢れても手動実行は押し出されない");
            Assert.AreEqual("User/First", userEntries[0].Path);
        }

        [Test]
        public void AutomatedEntries_TrimmedAtOwnCapacity()
        {
            for (var i = 0; i < InvocationStore.AutomatedCapacity + 5; i++)
            {
                Record($"Auto/{i}", InvocationOrigin.Scenario);
            }

            var entries = _store.Entries;
            Assert.AreEqual(InvocationStore.AutomatedCapacity, entries.Count);
            Assert.AreEqual("Auto/5", entries[0].Path, "自動化枠は最古から捨てられる");
        }

        [Test]
        public void UserEntries_TrimmedAtOwnCapacity_KeepingAutomatedIntact()
        {
            Record("Auto/Keep", InvocationOrigin.Ipc);
            for (var i = 0; i < InvocationStore.Capacity + 3; i++)
            {
                Record($"User/{i}", InvocationOrigin.User);
            }

            var entries = _store.Entries;
            Assert.AreEqual(InvocationStore.Capacity + 1, entries.Count);
            Assert.AreEqual("Auto/Keep", entries[0].Path, "手動実行が溢れても自動化由来は残る");
            Assert.AreEqual("User/3", entries[1].Path, "手動枠は最古から捨てられる");
        }

        [Test]
        public void Entries_KeepChronologicalOrderAcrossOrigins()
        {
            Record("A", InvocationOrigin.User);
            Record("B", InvocationOrigin.Scenario);
            Record("C", InvocationOrigin.User);

            var entries = _store.Entries;
            Assert.AreEqual(new[] { "A", "B", "C" }, new[] { entries[0].Path, entries[1].Path, entries[2].Path });
        }

        [Test]
        public void Origin_MapsToLegacyFlags()
        {
            Record("U", InvocationOrigin.User);
            Record("I", InvocationOrigin.Ipc);
            Record("S", InvocationOrigin.Scenario);

            var entries = _store.Entries;
            Assert.IsFalse(entries[0].IsAutomated);
            Assert.IsFalse(entries[0].IsFromScenario);
            Assert.IsTrue(entries[1].IsAutomated, "IPC は自動化扱い");
            Assert.IsFalse(entries[1].IsFromScenario, "IPC はシナリオ由来ではない");
            Assert.IsTrue(entries[2].IsAutomated);
            Assert.IsTrue(entries[2].IsFromScenario);
        }

        [Test]
        public void Clear_ResetsPerOriginCounts()
        {
            for (var i = 0; i < InvocationStore.Capacity; i++) Record($"User/{i}", InvocationOrigin.User);
            _store.Clear();

            Record("User/After", InvocationOrigin.User);
            var entries = _store.Entries;
            Assert.AreEqual(1, entries.Count, "Clear 後は枠のカウントもリセットされる");
            Assert.AreEqual("User/After", entries[0].Path);
        }
    }
}
