using System.Collections.Generic;
using NUnit.Framework;
using Void2610.LiminalPalette.Ipc.Auth;
using Void2610.LiminalPalette.Ipc.Server;

namespace Void2610.LiminalPalette.Tests.Ipc
{
    public sealed class TokenAuthenticatorTests
    {
        private static IpcRequest WithAuth(string authHeader)
        {
            var headers = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            if (authHeader != null) headers["Authorization"] = authHeader;
            return new IpcRequest("GET", "/x", null, headers, "");
        }

        [Test]
        public void Authenticate_ValidBearerToken_ReturnsTrue()
        {
            var auth = new TokenAuthenticator("secret123");
            Assert.IsTrue(auth.Authenticate(WithAuth("Bearer secret123")));
        }

        [Test]
        public void Authenticate_WrongToken_ReturnsFalse()
        {
            var auth = new TokenAuthenticator("secret123");
            Assert.IsFalse(auth.Authenticate(WithAuth("Bearer wrong")));
        }

        [Test]
        public void Authenticate_NoAuthHeader_ReturnsFalse()
        {
            var auth = new TokenAuthenticator("secret123");
            Assert.IsFalse(auth.Authenticate(WithAuth(null)));
        }

        [Test]
        public void Authenticate_EmptyAuthHeader_ReturnsFalse()
        {
            var auth = new TokenAuthenticator("secret123");
            Assert.IsFalse(auth.Authenticate(WithAuth("")));
        }

        [Test]
        public void Authenticate_BearerPrefixCaseMatters()
        {
            // RFC 6750 で大小固定。"bearer xxx" は不一致扱い。
            var auth = new TokenAuthenticator("secret123");
            Assert.IsFalse(auth.Authenticate(WithAuth("bearer secret123")));
        }

        [Test]
        public void Authenticate_NoBearerPrefix_ReturnsFalse()
        {
            var auth = new TokenAuthenticator("secret123");
            Assert.IsFalse(auth.Authenticate(WithAuth("secret123")));
        }

        [Test]
        public void Authenticate_TrimsTrailingWhitespace()
        {
            // ヘッダ末尾の余白は無視 (実装の Trim 仕様)。
            var auth = new TokenAuthenticator("secret123");
            Assert.IsTrue(auth.Authenticate(WithAuth("Bearer secret123  ")));
        }

        [Test]
        public void Authenticate_DifferentLength_ReturnsFalse()
        {
            // 固定時間比較でも長さ違いは false。
            var auth = new TokenAuthenticator("abc");
            Assert.IsFalse(auth.Authenticate(WithAuth("Bearer abcdef")));
        }
    }
}
