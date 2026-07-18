using System.IO;
using NUnit.Framework;
using Void2610.LiminalPalette.Ipc.Auth;

namespace Void2610.LiminalPalette.Tests.Ipc
{
    /// <summary>
    /// TokenStore の単体テスト。保存先を一時ディレクトリへ差し替えて実ユーザーのトークンに触れずに検証する。
    /// </summary>
    public sealed class TokenStoreTests
    {
        [SetUp]
        public void SetUp()
        {
            // 実ユーザーの ~/.liminal-palette/token を消さないよう一時ディレクトリへ差し替える
            TokenStore.OverrideDirectoryForTest =
                Path.Combine(Path.GetTempPath(), "lp-token-test-" + System.Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            TokenStore.DeleteForTest();
            TokenStore.OverrideDirectoryForTest = null;
        }

        [Test]
        public void LoadOrCreate_FirstCall_CreatesFile_AndReturnsToken()
        {
            Assert.IsFalse(File.Exists(TokenStore.TokenFilePath));
            var token = TokenStore.LoadOrCreate();
            Assert.IsNotNull(token);
            Assert.IsTrue(token.Length > 0);
            Assert.IsTrue(File.Exists(TokenStore.TokenFilePath));
        }

        [Test]
        public void LoadOrCreate_SecondCall_ReturnsSameToken()
        {
            var t1 = TokenStore.LoadOrCreate();
            var t2 = TokenStore.LoadOrCreate();
            Assert.AreEqual(t1, t2);
        }

        [Test]
        public void LoadOrCreate_TrimsTrailingNewline()
        {
            // エディタが付与しがちな末尾改行を許容することを保証。
            TokenStore.WriteTokenForTest("abc123\n");
            var t = TokenStore.LoadOrCreate();
            Assert.AreEqual("abc123", t);
        }

        [Test]
        public void LoadOrCreate_AfterDelete_RegeneratesNewToken()
        {
            var t1 = TokenStore.LoadOrCreate();
            TokenStore.DeleteForTest();
            var t2 = TokenStore.LoadOrCreate();
            Assert.AreNotEqual(t1, t2, "削除後は新しいトークンが生成されるべき");
        }

        [Test]
        public void GeneratedToken_HasReasonableEntropy()
        {
            // 256 bit base64 = 約 44 文字。
            var t = TokenStore.LoadOrCreate();
            Assert.GreaterOrEqual(t.Length, 40, "256bit base64 は 44 字前後 (パディング込み)");
        }
    }
}
