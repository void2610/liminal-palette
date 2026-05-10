using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Void2610.LiminalPalette.Editor
{
    /// <summary>
    /// LiminalPalette 自身を git HEAD に再フェッチするコマンド。
    /// Packages/manifest.json に書かれた git URL をそのまま Client.Add に渡すので、
    /// 利用者がフォークや任意ブランチで指定していてもその通りに更新される。
    /// 完了は EditorApplication.update でポーリングし、結果を Debug.Log に出す。
    /// </summary>
    public static class PackageUpdateCommand
    {
        // パッケージ名は固定。manifest.json のキーと一致させる。
        private const string PackageName = "com.void2610.liminal-palette";

        // Path prefix "Editor/" が予約済みで CommandDescriptor.IsEditorOnly が自動 true になるため、
        // Play Mode / Player ビルドのランタイムパレットからは表示されない (Editor Window のみ)。
        [LiminalCommand("Editor/Package/Update LiminalPalette",
            Description = "LiminalPalette を git の最新コミットに再フェッチし packages-lock.json を更新")]
        public static void Update()
        {
            var url = ReadManifestEntry(PackageName);
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogWarning($"[LiminalPalette] Packages/manifest.json に '{PackageName}' のエントリが見つかりません。");
                return;
            }

            // git URL でのインストールでない場合 (registry version / file: 参照など) は更新を諦める。
            // git source 以外は Client.Add の挙動が変わるため、誤って別の install を発火させない。
            if (!IsGitUrl(url))
            {
                Debug.LogWarning($"[LiminalPalette] '{PackageName}' は git URL で参照されていないため更新できません (value='{url}')。");
                return;
            }

            Debug.Log($"[LiminalPalette] {url} を再フェッチ中...");
            var request = Client.Add(url);
            EditorApplication.update += Poll;

            // ローカル関数はクロージャで request を捕まえ、同一デリゲート参照で +/- できる。
            // 完了後は必ず unsubscribe して update tick を残さない。
            void Poll()
            {
                if (!request.IsCompleted) return;
                EditorApplication.update -= Poll;
                if (request.Status == StatusCode.Success)
                {
                    var p = request.Result;
                    Debug.Log($"[LiminalPalette] 更新完了: {p.name}@{p.version} (resolved={p.resolvedPath})");
                }
                else
                {
                    Debug.LogError($"[LiminalPalette] 更新失敗: {request.Error?.message}");
                }
            }
        }

        // manifest.json から指定パッケージの value (URL or version 文字列) を抽出する。
        // JSON ライブラリ依存を避けるため Regex で十分。dependencies 以外に同名キーが
        // 出ない前提だが、Unity の manifest.json は dependencies / scopedRegistries 等の
        // トップレベル構造のみで衝突は起きない。
        private static string ReadManifestEntry(string packageName)
        {
            const string manifestPath = "Packages/manifest.json";
            if (!File.Exists(manifestPath)) return null;
            var json = File.ReadAllText(manifestPath);
            var pattern = "\"" + Regex.Escape(packageName) + "\"\\s*:\\s*\"([^\"]+)\"";
            var m = Regex.Match(json, pattern);
            return m.Success ? m.Groups[1].Value : null;
        }

        // Unity の git source として有効な URL かを簡易判定する。
        // https / http / git+ssh / git@ 等の形式を許容し、それ以外 (registry version) は弾く。
        private static bool IsGitUrl(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("git@", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("git+", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase);
        }
    }
}
