using System;
using System.Text;
using Void2610.LiminalPalette.Ipc.Server;

namespace Void2610.LiminalPalette.Ipc.Auth
{
    /// <summary>
    /// HTTP リクエストの Authorization: Bearer ヘッダを検証する。
    /// 比較はタイミング攻撃対策として固定時間で行う (短いトークンではオーバーキルだが流儀として)。
    /// </summary>
    public sealed class TokenAuthenticator
    {
        private const string BearerPrefix = "Bearer ";

        private readonly byte[] _expected;

        public TokenAuthenticator(string expected)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            _expected = Encoding.UTF8.GetBytes(expected);
        }

        public bool Authenticate(IpcRequest request)
        {
            if (request == null) return false;
            if (!request.Headers.TryGetValue("Authorization", out var auth)) return false;
            if (string.IsNullOrEmpty(auth)) return false;
            // "Bearer " プレフィックスは大小区別する (RFC 6750 でケース固定)。
            if (!auth.StartsWith(BearerPrefix, StringComparison.Ordinal)) return false;
            var token = auth.Substring(BearerPrefix.Length).Trim();
            if (token.Length == 0) return false;

            var actual = Encoding.UTF8.GetBytes(token);
            return FixedTimeEquals(actual, _expected);
        }

        // 長さが違ってもダミー XOR を回し、長さ情報のリークを最小化する。
        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            var max = Math.Max(a.Length, b.Length);
            var diff = a.Length ^ b.Length;
            for (var i = 0; i < max; i++)
            {
                var ax = i < a.Length ? a[i] : (byte)0;
                var bx = i < b.Length ? b[i] : (byte)0;
                diff |= ax ^ bx;
            }
            return diff == 0;
        }
    }
}
