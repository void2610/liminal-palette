using System;
using System.IO;
using UnityEngine;

namespace Void2610.LiminalPalette.Ipc
{
    /// <summary>
    /// プロジェクトごとの IPC 設定を <project>/ProjectSettings/LiminalPalette.json から読む。
    /// 同一マシンで複数 Unity プロジェクトを同時起動するときに、各プロジェクトが
    /// 固定ポートを宣言できるようにするための仕組み。lp CLI は cwd 検出 →
    /// このファイルの port を最優先候補として probe する。
    ///
    /// ファイルフォーマット (例):
    ///   { "port": 7613 }
    ///
    /// ファイルが存在しない / port が 0 以下 / 65535 超 / パースエラーの場合は null を返し、
    /// 呼び出し側は IpcSettings.DefaultPort にフォールバックする。
    /// </summary>
    public static class ProjectConfig
    {
        public const string FileName = "LiminalPalette.json";

        [Serializable]
        private class Dto
        {
            public int port;
        }

        /// <summary>
        /// 設定ファイルの絶対パス。Editor / Play Mode では <project>/ProjectSettings/LiminalPalette.json。
        /// Player ビルドでは Application.dataPath が build フォルダを指すため戻り値もそこの架空パス
        /// になる (通常ファイルは存在せず GetPreferredPort は null を返す)。
        /// </summary>
        public static string ConfigFilePath => GetConfigFilePathAt(GetProjectRoot());

        /// <summary>プロジェクト設定で指定された preferred port を返す。未設定なら null。</summary>
        public static int? GetPreferredPort() => GetPreferredPortAt(GetProjectRoot());

        /// <summary>
        /// テスト / 任意プロジェクトディレクトリから preferred port を読む。
        /// production code では <see cref="GetPreferredPort"/> を使う。
        /// </summary>
        internal static int? GetPreferredPortAt(string projectRoot)
        {
            try
            {
                var path = GetConfigFilePathAt(projectRoot);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                var dto = JsonUtility.FromJson<Dto>(json);
                if (dto == null || dto.port <= 0 || dto.port > 65535) return null;
                return dto.port;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LiminalPalette.Ipc] Failed to read {FileName}: {ex.Message}");
                return null;
            }
        }

        private static string GetProjectRoot()
        {
            var dataPath = Application.dataPath;
            if (string.IsNullOrEmpty(dataPath)) return "";
            return Path.GetDirectoryName(dataPath) ?? "";
        }

        private static string GetConfigFilePathAt(string projectRoot)
        {
            if (string.IsNullOrEmpty(projectRoot)) return "";
            return Path.Combine(projectRoot, "ProjectSettings", FileName);
        }
    }
}
