using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using Void2610.LiminalPalette;
using Void2610.LiminalPalette.Editor;

namespace Void2610.LiminalPalette.Tests.UI
{
    /// <summary>
    /// InvocationStore の永続化 (Log / History タブが domain reload と Editor 再起動を跨いで残ること)。
    ///
    /// InvocationStore は static シングルトンなので、スクリプト再コンパイル / Play Mode 出入り /
    /// テスト実行のたびに中身が消える。保存が無いと Log / History タブが実用にならない。
    /// </summary>
    public sealed class InvocationPersistenceTests
    {
        private string _key;

        [SetUp]
        public void SetUp()
        {
            // 本番キーは触らない (利用者の記録を壊さないため)
            _key = "Void2610.LiminalPalette.Invocations.Test." + Guid.NewGuid().ToString("N");
        }

        [TearDown]
        public void TearDown() => EditorPrefs.DeleteKey(_key);

        private EditorPrefsInvocationStorage Storage() => new EditorPrefsInvocationStorage(_key);

        private static CommandResult Ok(double ms = 1.0) =>
            CommandResult.Ok("v", Array.Empty<LogEntry>(), TimeSpan.FromMilliseconds(ms));

        [Test]
        public void 手動実行は別インスタンスへ復元される()
        {
            var a = new InvocationStore();
            a.AttachStorage(Storage());
            a.Record("Player/Damage",
                new Dictionary<string, object> { ["amount"] = 10 },
                Ok(), InvocationOrigin.User);

            // domain reload を模して作り直す
            var b = new InvocationStore();
            b.AttachStorage(Storage());

            Assert.AreEqual(1, b.Entries.Count);
            Assert.AreEqual("Player/Damage", b.Entries[0].Path);
            Assert.IsTrue(b.Entries[0].IsRestored);
            Assert.AreEqual("10", b.Entries[0].Args["amount"]);
        }

        [Test]
        public void 自動化由来は保存しない()
        {
            var a = new InvocationStore();
            a.AttachStorage(Storage());
            a.Record("Ipc/X", null, Ok(), InvocationOrigin.Ipc);
            a.Record("Scn/Y", null, Ok(), InvocationOrigin.Scenario);

            var b = new InvocationStore();
            b.AttachStorage(Storage());

            // E2E を回すだけで保存先が埋まらないこと
            Assert.AreEqual(0, b.Entries.Count);
        }

        [Test]
        public void 成否とエラー文が復元される()
        {
            var a = new InvocationStore();
            a.AttachStorage(Storage());
            a.Record("F/X", null,
                CommandResult.Fail("boom", null, Array.Empty<LogEntry>(), TimeSpan.Zero),
                InvocationOrigin.User);

            var b = new InvocationStore();
            b.AttachStorage(Storage());

            Assert.IsFalse(b.Entries[0].Result.Success);
            Assert.AreEqual("boom", b.Entries[0].Result.Error);
        }

        [Test]
        public void 保存は上限までで最新が残る()
        {
            var a = new InvocationStore();
            a.AttachStorage(Storage());
            for (var i = 0; i < InvocationStore.PersistedCapacity + 10; i++)
            {
                a.Record($"P/{i}", null, Ok(), InvocationOrigin.User);
            }

            var b = new InvocationStore();
            b.AttachStorage(Storage());

            Assert.AreEqual(InvocationStore.PersistedCapacity, b.Entries.Count);
            Assert.AreEqual($"P/{InvocationStore.PersistedCapacity + 9}", b.Entries[b.Entries.Count - 1].Path);
        }

        [Test]
        public void Clear_で保存も消える()
        {
            var a = new InvocationStore();
            a.AttachStorage(Storage());
            a.Record("A", null, Ok(), InvocationOrigin.User);
            a.Clear();

            Assert.IsFalse(EditorPrefs.HasKey(_key));
        }

        [Test]
        public void 壊れた保存は黙って捨てる()
        {
            EditorPrefs.SetString(_key, "ゴミ\u001fデータ");

            var s = new InvocationStore();
            Assert.DoesNotThrow(() => s.AttachStorage(Storage()));
            Assert.AreEqual(0, s.Entries.Count);
        }

        [Test]
        public void 本番キーを汚さない()
        {
            EditorPrefs.SetString(EditorPrefsInvocationStorage.PrefsKey, "USER-DATA");
            try
            {
                var s = new InvocationStore();
                s.AttachStorage(Storage());
                s.Record("A", null, Ok(), InvocationOrigin.User);
                s.Clear();

                Assert.AreEqual("USER-DATA",
                    EditorPrefs.GetString(EditorPrefsInvocationStorage.PrefsKey, ""));
            }
            finally
            {
                EditorPrefs.DeleteKey(EditorPrefsInvocationStorage.PrefsKey);
            }
        }
    }
}
