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
            // 既存スキル (現行 liminal-* および旧 lp-* 残骸) と衝突する場合は事前に通知する。
            var existing = Directory.Exists(dstRoot)
                ? CollectInstalledSkills(dstRoot)
                : System.Array.Empty<string>();

            var prompt = $"Install {skillDirs.Length} skill(s) into:\n  {dstRoot}";
            if (existing.Length > 0)
                prompt += $"\n\n{existing.Length} existing skill(s) (liminal-*/lp-*) will be OVERWRITTEN or removed.";

            if (!EditorUtility.DisplayDialog("LiminalPalette - Install AI Skills",
                    prompt, "Install", "Cancel"))
                return;

            Directory.CreateDirectory(dstRoot);
            // 旧 lp-* (legacy) はリネーム前のディレクトリ名なので、新規インストールでは取り残されてしまう。
            // 重複を避けるため、Install 時に明示的に削除して新しい liminal-* に置き換える。
            foreach (var legacy in Directory.GetDirectories(dstRoot, "lp-*"))
            {
                try { Directory.Delete(legacy, recursive: true); }
                catch { /* swallow: legacy cleanup なので失敗してもよい */ }
            }
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

            // 現行の liminal-* と旧 lp-* を両方クリーンアップする (リネーム前にインストールしたユーザー向け)。
            var skills = CollectInstalledSkills(dstRoot);
            if (skills.Length == 0)
            {
                EditorUtility.DisplayDialog("LiminalPalette",
                    "No liminal-* / lp-* skills are currently installed.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("LiminalPalette - Uninstall AI Skills",
                    $"Remove {skills.Length} skill(s) (liminal-*/lp-*) from:\n{dstRoot}",
                    "Remove", "Cancel"))
                return;

            foreach (var dir in skills)
                Directory.Delete(dir, recursive: true);

            Debug.Log($"[LiminalPalette] Uninstalled {skills.Length} AI skill(s) from {dstRoot}");
            EditorUtility.DisplayDialog("LiminalPalette",
                $"Removed {skills.Length} skill(s).", "OK");
        }

        /// <summary>
        /// インストール済みのスキルディレクトリ (現行 liminal-* と legacy lp-*) を集める。
        /// </summary>
        private static string[] CollectInstalledSkills(string dstRoot)
        {
            var current = Directory.GetDirectories(dstRoot, "liminal-*");
            var legacy = Directory.GetDirectories(dstRoot, "lp-*");
            var combined = new string[current.Length + legacy.Length];
            current.CopyTo(combined, 0);
            legacy.CopyTo(combined, current.Length);
            return combined;
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
