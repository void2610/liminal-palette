using System.IO;
using UnityEditor;
using UnityEngine;

namespace Void2610.LiminalPalette.Editor
{
    /// <summary>
    /// AI Agent (Claude Code 等) 用の skill 群を、利用側プロジェクトの .claude/skills/ にコピーするインストーラ。
    /// Source of Truth は Packages/com.void2610.liminal-palette/AISkills~/ に同梱されている。
    /// `~` 付きディレクトリは Unity が無視するので、生の md ファイルとして package に同梱できる。
    /// </summary>
    internal static class AISkillsInstaller
    {
        private const string PackageName = "com.void2610.liminal-palette";
        private const string SkillsSourceDir = "AISkills~";
        private const string ClaudeSkillsRel = ".claude/skills";

        [MenuItem("Tools/LiminalPalette/Install AI Skills...", priority = 200)]
        private static void Install()
        {
            if (!TryGetPackageSkillsRoot(out var srcRoot))
            {
                EditorUtility.DisplayDialog(
                    "LiminalPalette",
                    $"Skill source not found.\nExpected: Packages/{PackageName}/{SkillsSourceDir}\n\n" +
                    "LiminalPalette が UPM パッケージとして正しく解決されていない可能性があります。",
                    "OK");
                return;
            }

            var skillDirs = Directory.GetDirectories(srcRoot);
            if (skillDirs.Length == 0)
            {
                EditorUtility.DisplayDialog("LiminalPalette",
                    $"No skills found under {srcRoot}.", "OK");
                return;
            }

            var dstRoot = Path.Combine(GetProjectRoot(), ClaudeSkillsRel);
            // 既存の lp-* スキル (前バージョンのインストール残骸) と衝突する場合は事前に通知する。
            var existing = Directory.Exists(dstRoot)
                ? Directory.GetDirectories(dstRoot, "lp-*")
                : System.Array.Empty<string>();

            var prompt = $"Install {skillDirs.Length} skill(s) into:\n  {dstRoot}";
            if (existing.Length > 0)
                prompt += $"\n\n{existing.Length} existing lp-* skill(s) will be OVERWRITTEN.";

            if (!EditorUtility.DisplayDialog("LiminalPalette - Install AI Skills",
                    prompt, "Install", "Cancel"))
                return;

            Directory.CreateDirectory(dstRoot);
            var copied = 0;
            foreach (var skillDir in skillDirs)
            {
                var name = Path.GetFileName(skillDir);
                var dst = Path.Combine(dstRoot, name);
                if (Directory.Exists(dst))
                    Directory.Delete(dst, recursive: true);
                CopyDirectory(skillDir, dst);
                copied++;
            }

            Debug.Log($"[LiminalPalette] Installed {copied} AI skill(s) -> {dstRoot}");
            EditorUtility.DisplayDialog("LiminalPalette",
                $"Installed {copied} skill(s) to:\n{dstRoot}\n\n" +
                "Claude Code を再起動するか新しいセッションを開くと skill が認識されます。",
                "OK");
        }

        [MenuItem("Tools/LiminalPalette/Uninstall AI Skills", priority = 201)]
        private static void Uninstall()
        {
            var dstRoot = Path.Combine(GetProjectRoot(), ClaudeSkillsRel);
            if (!Directory.Exists(dstRoot))
            {
                EditorUtility.DisplayDialog("LiminalPalette",
                    $".claude/skills/ does not exist.\nNothing to uninstall.", "OK");
                return;
            }

            var lpSkills = Directory.GetDirectories(dstRoot, "lp-*");
            if (lpSkills.Length == 0)
            {
                EditorUtility.DisplayDialog("LiminalPalette",
                    "No lp-* skills are currently installed.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("LiminalPalette - Uninstall AI Skills",
                    $"Remove {lpSkills.Length} lp-* skill(s) from:\n{dstRoot}",
                    "Remove", "Cancel"))
                return;

            foreach (var dir in lpSkills)
                Directory.Delete(dir, recursive: true);

            Debug.Log($"[LiminalPalette] Uninstalled {lpSkills.Length} AI skill(s) from {dstRoot}");
            EditorUtility.DisplayDialog("LiminalPalette",
                $"Removed {lpSkills.Length} lp-* skill(s).", "OK");
        }

        /// <summary>
        /// package 内の AISkills~ への絶対パスを解決する。
        /// Unity の Package Manager が `Packages/&lt;id&gt;/` を仮想パスとして扱うため、
        /// Embedded / Local / Git / PackageCache のいずれでも Path.GetFullPath で解決される。
        /// </summary>
        private static bool TryGetPackageSkillsRoot(out string fullPath)
        {
            var rel = $"Packages/{PackageName}/{SkillsSourceDir}";
            fullPath = Path.GetFullPath(rel);
            return Directory.Exists(fullPath);
        }

        /// <summary>
        /// 利用側プロジェクトのルートパス (Assets/ の親)。
        /// </summary>
        private static string GetProjectRoot()
            => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
            foreach (var d in Directory.GetDirectories(src))
                CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)));
        }
    }
}
