using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Void2610.LiminalPalette.Runtime
{
    /// <summary>
    /// シーン切替・現在シーン取得のための組み込みランタイムコマンド。
    /// パス prefix は `Scene/` で `Editor/` ではないので Editor / PlayMode / Player ビルドの
    /// 3 経路すべてから呼べる (LoadScene 自体は PlayMode/Player のみ動作)。
    /// 利用例:
    /// <list type="bullet">
    ///   <item>`liminal exec Scene/Load sceneName=Test_Foo` で対象シーンへ即時切替</item>
    ///   <item>`liminal exec Scene/Current` で active シーン名を取得</item>
    /// </list>
    /// シナリオから使う場合は <see cref="ScenarioStep.LoadScene"/> または
    /// `[LiminalScenario(Scene = "...")]` 属性を使うほうがネイティブ。
    /// LP の Runtime asmdef は `autoReferenced: true` なので、利用側は何もせずに
    /// これらがパレットに出現する。
    /// </summary>
    public static class SceneCommands
    {
        [LiminalCommand("Scene/Current", Description = "現在 active なシーン名を返す")]
        public static string Current() => SceneManager.GetActiveScene().name;

        [LiminalCommand("Scene/Load", Description = "指定名のシーンを Single モードで非同期ロードし、完了まで待機する (Build Settings に登録済みである必要あり)")]
        public static async Task<string> Load(
            [LiminalParam(Description = "Build Settings に登録済みのシーン名")] string sceneName)
        {
            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (op == null) return $"LoadSceneAsync returned null: '{sceneName}' (Build Settings に登録されているか確認)";
            // ScenarioExecutor.RunLoadSceneStep と同じく Task.Yield で 1 frame ずつ進む。
            while (!op.isDone) await Task.Yield();
            return $"Loaded: {SceneManager.GetActiveScene().name}";
        }
    }
}
