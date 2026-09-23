using NUnit.Framework;
using UnityEditor;
using Void2610.LiminalPalette.Editor;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Tests.UI
{
    public sealed class InMemoryCommandHistoryTests
    {
        [Test]
        public void Record_AddsToFront()
        {
            var h = new InMemoryCommandHistory();
            h.Record("A");
            h.Record("B");
            Assert.AreEqual("B", h.RecentPaths[0]);
            Assert.AreEqual("A", h.RecentPaths[1]);
        }

        [Test]
        public void Record_DuplicatePath_MovesToFrontWithoutDuplicating()
        {
            var h = new InMemoryCommandHistory();
            h.Record("A");
            h.Record("B");
            h.Record("A");
            Assert.AreEqual(2, h.RecentPaths.Count);
            Assert.AreEqual("A", h.RecentPaths[0]);
            Assert.AreEqual("B", h.RecentPaths[1]);
        }

        [Test]
        public void Record_ExceedingMax_DropsOldest()
        {
            var h = new InMemoryCommandHistory();
            for (var i = 0; i < InMemoryCommandHistory.MaxEntries + 5; i++)
            {
                h.Record($"P{i}");
            }
            Assert.AreEqual(InMemoryCommandHistory.MaxEntries, h.RecentPaths.Count);
            // 最新は P54 (54-49=5 を超過した最後の要素)
            Assert.AreEqual($"P{InMemoryCommandHistory.MaxEntries + 4}", h.RecentPaths[0]);
        }

        [Test]
        public void IndexOf_ReturnsZeroBasedRecency()
        {
            var h = new InMemoryCommandHistory();
            h.Record("A");
            h.Record("B");
            h.Record("C");
            Assert.AreEqual(0, h.IndexOf("C"));
            Assert.AreEqual(2, h.IndexOf("A"));
            Assert.AreEqual(-1, h.IndexOf("Z"));
        }

        [Test]
        public void IndexOf_IsCaseInsensitive()
        {
            var h = new InMemoryCommandHistory();
            h.Record("Foo/Bar");
            Assert.AreEqual(0, h.IndexOf("foo/bar"));
            Assert.IsTrue(h.Contains("FOO/BAR"));
        }

        [Test]
        public void Clear_EmptiesHistory()
        {
            var h = new InMemoryCommandHistory();
            h.Record("A");
            h.Clear();
            Assert.AreEqual(0, h.RecentPaths.Count);
        }
    }

    public sealed class EditorCommandHistoryTests
    {
        // 本番キーは絶対に触らない。EditMode テストは Editor と同じ EditorPrefs を共有するため、
        // 本番キーを消すと利用者の「最近使ったコマンド」が実際に消える (Editor 再起動で発覚する)。
        private string _key;

        [SetUp]
        public void SetUp()
        {
            _key = "Void2610.LiminalPalette.History.Test." + System.Guid.NewGuid().ToString("N");
        }

        [TearDown]
        public void TearDown() => EditorPrefs.DeleteKey(_key);

        [Test]
        public void 本番キーを汚さない()
        {
            // 本番キーには書き込みも削除もしない。観測するだけ。
            // (以前はここで SetString / DeleteKey しており、テストを回すたびに
            //  利用者の「最近使ったコマンド」が実際に消えていた)
            const string sentinel = "\u0000__absent__";
            var before = EditorPrefs.GetString(EditorCommandHistory.PrefsKey, sentinel);

            var h = new EditorCommandHistory(_key);
            h.Record("A");
            h.Clear();

            var after = EditorPrefs.GetString(EditorCommandHistory.PrefsKey, sentinel);
            Assert.AreEqual(before, after, "本番キーが変化している");
        }

        [Test]
        public void Record_PersistsAcrossInstances()
        {
            var h1 = new EditorCommandHistory(_key);
            h1.Record("Player/Health/Set");
            h1.Record("Enemy/Spawn");

            // 別インスタンスで読み戻し。
            var h2 = new EditorCommandHistory(_key);
            Assert.AreEqual(2, h2.RecentPaths.Count);
            Assert.AreEqual("Enemy/Spawn", h2.RecentPaths[0]);
            Assert.AreEqual("Player/Health/Set", h2.RecentPaths[1]);
        }

        [Test]
        public void Clear_RemovesPrefsKey()
        {
            var h = new EditorCommandHistory(_key);
            h.Record("A");
            h.Clear();
            Assert.IsFalse(EditorPrefs.HasKey(_key));
        }
    }

    /// <summary>
    /// Runtime 用 PlayerPrefsCommandHistory のテスト。EditorCommandHistory と挙動を揃える前提なので
    /// 同じケースを PlayerPrefs キーで実施する。テストは EditMode 上で動くが PlayerPrefs API は Editor / Runtime 両方で動作する。
    /// </summary>
    public sealed class PlayerPrefsCommandHistoryTests
    {
        // EditorCommandHistoryTests と同じ理由で本番キーは触らない。
        private string _key;

        [SetUp]
        public void SetUp()
        {
            _key = "Void2610.LiminalPalette.History.Test." + System.Guid.NewGuid().ToString("N");
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.PlayerPrefs.DeleteKey(_key);
            UnityEngine.PlayerPrefs.Save();
        }

        [Test]
        public void 本番キーを汚さない()
        {
            // EditorCommandHistoryTests と同じく、本番キーは観測するだけ。
            const string sentinel = "\u0000__absent__";
            var before = UnityEngine.PlayerPrefs.GetString(PlayerPrefsCommandHistory.PrefsKey, sentinel);

            var h = new PlayerPrefsCommandHistory(_key);
            h.Record("A");
            h.Clear();

            var after = UnityEngine.PlayerPrefs.GetString(PlayerPrefsCommandHistory.PrefsKey, sentinel);
            Assert.AreEqual(before, after, "本番キーが変化している");
        }

        [Test]
        public void Record_PersistsAcrossInstances()
        {
            var h1 = new PlayerPrefsCommandHistory(_key);
            h1.Record("Player/Health/Set");
            h1.Record("Enemy/Spawn");

            var h2 = new PlayerPrefsCommandHistory(_key);
            Assert.AreEqual(2, h2.RecentPaths.Count);
            Assert.AreEqual("Enemy/Spawn", h2.RecentPaths[0]);
            Assert.AreEqual("Player/Health/Set", h2.RecentPaths[1]);
        }

        [Test]
        public void Clear_RemovesPrefsKey()
        {
            var h = new PlayerPrefsCommandHistory(_key);
            h.Record("A");
            h.Clear();
            Assert.IsFalse(UnityEngine.PlayerPrefs.HasKey(_key));
        }

        [Test]
        public void Record_RespectsMaxEntriesAcrossInstances()
        {
            var h1 = new PlayerPrefsCommandHistory(_key);
            for (var i = 0; i < InMemoryCommandHistory.MaxEntries + 3; i++)
            {
                h1.Record($"P{i}");
            }

            var h2 = new PlayerPrefsCommandHistory(_key);
            Assert.AreEqual(InMemoryCommandHistory.MaxEntries, h2.RecentPaths.Count);
            Assert.AreEqual($"P{InMemoryCommandHistory.MaxEntries + 2}", h2.RecentPaths[0]);
        }

        [Test]
        public void IndexOf_IsCaseInsensitive()
        {
            var h = new PlayerPrefsCommandHistory(_key);
            h.Record("Foo/Bar");
            Assert.AreEqual(0, h.IndexOf("foo/bar"));
            Assert.IsTrue(h.Contains("FOO/BAR"));
        }
    }
}
