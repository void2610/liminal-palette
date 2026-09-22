using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Void2610.LiminalPalette.Editor
{
    /// <summary>
    /// Rust 実装の `liminal` CLI を GitHub Releases から取得して ~/.local/bin に置くインストーラ。
    ///
    /// パッケージにバイナリを同梱しない理由: プラットフォーム別に 3MB 強あり、git URL 配布の本パッケージに
    /// 入れると全利用者が全履歴を clone することになる。代わりに Release から都度取得する
    /// (AISkillsInstaller が同梱ファイルをコピーするのとは対照的に、こちらはネットワークが要る)。
    /// </summary>
    internal static class CliInstaller
    {
        private const string Repo = "void2610/liminal-cli";
        private const string BinaryName = "liminal";

        /// 常に最新リリースを指す安定 URL。
        private static string AssetUrl(string asset) =>
            $"https://github.com/{Repo}/releases/latest/download/{asset}";

        [MenuItem("Tools/LiminalPalette/Install CLI...", priority = 210)]
        private static void Install()
        {
            if (!TryGetAssetName(out var asset, out var platformError))
            {
                EditorUtility.DisplayDialog("LiminalPalette", platformError, "OK");
                return;
            }

            var dstDir = DefaultInstallDir();
            var dst = Path.Combine(dstDir, BinaryName + (IsWindows() ? ".exe" : ""));

            var existing = File.Exists(dst) ? $"\n\n既存のファイルを上書きします:\n{dst}" : "";
            if (!EditorUtility.DisplayDialog(
                    "LiminalPalette",
                    $"liminal CLI を取得してインストールします。\n\n取得元: {AssetUrl(asset)}\n配置先: {dst}{existing}",
                    "インストール", "キャンセル"))
            {
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("LiminalPalette", $"{asset} を取得中...", 0.3f);
                var bytes = Download(AssetUrl(asset));

                EditorUtility.DisplayProgressBar("LiminalPalette", "書き込み中...", 0.8f);
                Directory.CreateDirectory(dstDir);
                File.WriteAllBytes(dst, bytes);
                MakeExecutable(dst);
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[LiminalPalette] CLI のインストールに失敗しました: {e}");
                EditorUtility.DisplayDialog("LiminalPalette",
                    $"インストールに失敗しました。\n\n{e.Message}\n\n" +
                    $"手動で取得する場合:\n{AssetUrl(asset)}", "OK");
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"[LiminalPalette] liminal CLI をインストールしました: {dst}");
            var onPath = IsOnPath(dstDir);
            EditorUtility.DisplayDialog("LiminalPalette",
                $"インストールしました。\n{dst}\n\n" +
                (onPath
                    ? "ターミナルで `liminal health` を試してください。"
                    : $"{dstDir} が PATH に入っていません。シェルの設定に次を追加してください:\n\n" +
                      $"export PATH=\"{dstDir}:$PATH\""),
                "OK");
        }

        [MenuItem("Tools/LiminalPalette/Uninstall CLI", priority = 211)]
        private static void Uninstall()
        {
            var dst = Path.Combine(DefaultInstallDir(), BinaryName + (IsWindows() ? ".exe" : ""));
            if (!File.Exists(dst))
            {
                EditorUtility.DisplayDialog("LiminalPalette", $"見つかりません: {dst}", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("LiminalPalette", $"削除しますか?\n{dst}", "削除", "キャンセル"))
            {
                return;
            }
            File.Delete(dst);
            Debug.Log($"[LiminalPalette] liminal CLI を削除しました: {dst}");
        }

        /// 実行中のプラットフォームに対応する Release アセット名を決める。
        private static bool TryGetAssetName(out string asset, out string error)
        {
            asset = null;
            error = null;
            // Editor は 64bit 前提。Apple Silicon かどうかは OS 情報から判定する。
            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor:
                    asset = IsAppleSilicon()
                        ? "liminal-aarch64-apple-darwin"
                        : "liminal-x86_64-apple-darwin";
                    return true;
                case RuntimePlatform.LinuxEditor:
                    asset = "liminal-x86_64-unknown-linux-gnu";
                    return true;
                case RuntimePlatform.WindowsEditor:
                    asset = "liminal-x86_64-pc-windows-msvc.exe";
                    return true;
                default:
                    error = $"未対応のプラットフォームです: {Application.platform}";
                    return false;
            }
        }

        // SystemInfo.processorType は Apple Silicon で "Apple M1" のような文字列を返す。
        private static bool IsAppleSilicon() =>
            SystemInfo.processorType.IndexOf("Apple", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsWindows() => Application.platform == RuntimePlatform.WindowsEditor;

        private static string DefaultInstallDir()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            // Windows も含めて同じ配置にする (WSL / Git Bash から使う想定)。
            return Path.Combine(home, ".local", "bin");
        }

        private static byte[] Download(string url)
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(2);
            // GitHub の download URL は Release アセットへリダイレクトする。
            return Task.Run(() => http.GetByteArrayAsync(url)).GetAwaiter().GetResult();
        }

        // 実行権限を付ける。Unix 以外では何もしない。
        private static void MakeExecutable(string path)
        {
            if (IsWindows())
            {
                return;
            }
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/chmod",
                Arguments = $"+x \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            p?.WaitForExit();
        }

        private static bool IsOnPath(string dir)
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            var sep = IsWindows() ? ';' : ':';
            foreach (var entry in path.Split(sep))
            {
                if (string.IsNullOrEmpty(entry))
                {
                    continue;
                }
                if (Path.GetFullPath(entry.TrimEnd('/')) == Path.GetFullPath(dir.TrimEnd('/')))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
