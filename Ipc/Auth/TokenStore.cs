using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace Void2610.LiminalPalette.Ipc.Auth
{
    /// <summary>
    /// HTTP API のアクセストークンを ~/.liminal-palette/token に保存 / 読み出すユーティリティ。
    /// 初回 LoadOrCreate 時に 256bit ランダムを base64 でエンコードしてファイル化する。
    /// macOS / Linux では chmod 600 をベストエフォートで試みる (Windows ではユーザープロファイル ACL に任せる)。
    /// </summary>
    public static class TokenStore
    {
        // テストが実ユーザーのトークンを消さないよう、テスト時のみ保存先を差し替えられる
        internal static string OverrideDirectoryForTest;

        public static string TokenDirectoryPath
            => OverrideDirectoryForTest
               ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".liminal-palette");

        public static string TokenFilePath => Path.Combine(TokenDirectoryPath, "token");

        /// <summary>
        /// トークンを読み込み、無ければ生成して返す。改行混入は Trim する。
        /// 例外は内部で握りつぶさず呼び出し側へ伝搬する (起動時に失敗を可視化したい)。
        /// </summary>
        public static string LoadOrCreate()
        {
            if (File.Exists(TokenFilePath))
            {
                var existing = File.ReadAllText(TokenFilePath).Trim();
                if (!string.IsNullOrEmpty(existing)) return existing;
            }

            // 既存ファイルが空 / 不存在なら生成。
            var token = GenerateToken();
            Directory.CreateDirectory(TokenDirectoryPath);
            File.WriteAllText(TokenFilePath, token);
            TryRestrictPermissions(TokenFilePath);
            return token;
        }

        /// <summary>テストから任意のトークンを書き込む。</summary>
        internal static void WriteTokenForTest(string token)
        {
            Directory.CreateDirectory(TokenDirectoryPath);
            File.WriteAllText(TokenFilePath, token ?? "");
        }

        /// <summary>
        /// テスト後の cleanup 用。token ファイルのみ削除する (親ディレクトリは残す)。
        /// ディレクトリ内に他のユーザーファイルが置かれている可能性を考慮して、ディレクトリ自体は触らない。
        /// </summary>
        internal static void DeleteForTest()
        {
            try
            {
                if (File.Exists(TokenFilePath)) File.Delete(TokenFilePath);
            }
            catch { /* swallow: テスト cleanup なので失敗してもよい */ }
        }

        // 256 bit ランダム → base64。base64 は URL-safe ではないが Authorization ヘッダ値としては OK。
        private static string GenerateToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes);
        }

        // macOS / Linux で chmod 600 を試みる。失敗してもログ警告のみ (ファイル生成自体は成功)。
        private static void TryRestrictPermissions(string path)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor
                || Application.platform == RuntimePlatform.WindowsPlayer) return;

            try
            {
                var psi = new ProcessStartInfo("chmod", $"600 \"{path}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                };
                using (var p = Process.Start(psi))
                {
                    if (p != null && !p.WaitForExit(2000))
                    {
                        p.Kill();
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[LiminalPalette.Ipc] chmod 600 失敗 (token はファイルとしては存在): {ex.Message}");
            }
        }
    }
}
