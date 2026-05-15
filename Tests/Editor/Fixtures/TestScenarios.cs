using System.Collections.Generic;

namespace Void2610.LiminalPalette.Tests
{
    /// <summary>
    /// 各テストから参照する [LiminalScenario] 付き static メソッド群。
    /// パスは "TestScenario/..." で始め、製品コードのシナリオと衝突しないようにしている。
    /// </summary>
    internal static class TestScenarios
    {
        [LiminalScenario("TestScenario/Empty", Description = "no-op scenario")]
        public static IEnumerable<ScenarioStep> Empty()
        {
            yield break;
        }

        [LiminalScenario("TestScenario/SingleCommand")]
        public static IEnumerable<ScenarioStep> SingleCommand()
        {
            yield return ScenarioStep.Run("Test/NoArg");
        }

        [LiminalScenario("TestScenario/CommandThenWait")]
        public static IEnumerable<ScenarioStep> CommandThenWait()
        {
            yield return ScenarioStep.Run("Test/NoArg");
            yield return ScenarioStep.WaitFrames(0);
        }

        [LiminalScenario("TestScenario/FailingCommand")]
        public static IEnumerable<ScenarioStep> FailingCommand()
        {
            // Throws コマンドは InvalidOperationException を投げるので CommandResult.Success=false。
            // fail-fast で 2 件目 (NoArg) は実行されない。
            yield return ScenarioStep.Run("Test/Throws");
            yield return ScenarioStep.Run("Test/NoArg");
        }
    }

    // 不正なシグネチャの「シナリオもどき」メソッド。
    //
    // [LiminalScenario] を直接付けると Bootstrap の ScanAll が起動毎に拾って
    // 「Skipping invalid scenario...」警告を吐き続けるので、属性は付けず
    // ScannerTests 側で TryBuildDescriptor に手で属性インスタンスを渡して検証する。
    internal static class InvalidScenarioShapes
    {
        public static IEnumerable<ScenarioStep> WithArgs(int x)
        {
            yield break;
        }

        public static int BadReturnType() => 0;

        // 正しい no-op シグネチャ。Scene 属性のテストなどで「shape は正しいが何もしない」シナリオが
        // 必要なときに使う。属性は付けないので Scanner.ScanAll では拾われない。
        public static IEnumerable<ScenarioStep> NoOpSteps()
        {
            yield break;
        }
    }
}
