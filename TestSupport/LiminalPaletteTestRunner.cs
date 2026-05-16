using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;

namespace Void2610.LiminalPalette.TestSupport
{
    /// <summary>
    /// Unity Test Runner (`[UnityTest]`) から `[LiminalScenario]` を実行するための薄いヘルパ。
    ///
    /// 利用想定:
    /// <code>
    /// public sealed class FooScenariosE2ETests
    /// {
    ///     [UnityTest]
    ///     public IEnumerator Run([ValueSource(nameof(Paths))] string path)
    ///         =&gt; LiminalPaletteTestRunner.RunScenario(path);
    ///
    ///     public static IEnumerable&lt;string&gt; Paths
    ///         =&gt; LiminalPaletteTestRunner.GetScenariosWithPrefix("Foo/Scenario/");
    /// }
    /// </code>
    ///
    /// `[UnityTest]` メソッドをシナリオごとに 1 つずつ書く代わりに、Registry に登録された
    /// 指定 prefix のシナリオを ValueSource で展開して parametrized test 化する。
    /// 新しいシナリオを追加すれば自動的に Test Runner に出現する。
    /// </summary>
    public static class LiminalPaletteTestRunner
    {
        /// <summary>
        /// Registry に登録済みのシナリオから、Path が <paramref name="prefix"/> で始まるものを列挙する。
        /// Registry が空の場合は <see cref="ScenarioScanner.ScanAll()"/> を呼んで自動的に populate する
        /// (PlayMode テスト起動直後で Bootstrap がまだ走っていないケースの防御)。
        /// </summary>
        public static IEnumerable<string> GetScenariosWithPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
                throw new ArgumentException("prefix must not be null or empty", nameof(prefix));

            if (ScenarioRegistry.Default.All.Count == 0)
            {
                ScenarioScanner.ScanAll();
            }

            foreach (var s in ScenarioRegistry.Default.All)
            {
                if (s.Path.StartsWith(prefix, StringComparison.Ordinal))
                    yield return s.Path;
            }
        }

        /// <summary>
        /// シナリオを 1 件実行する `[UnityTest]` 向けヘルパ。
        /// <see cref="LiminalPalette.RunScenarioAsync(string, System.Threading.CancellationToken)"/>
        /// の Task 完了まで `yield return null` で待機し、失敗時は失敗した step の詳細を含めて
        /// <see cref="Assert.Fail(string)"/> を呼ぶ。
        ///
        /// GetAwaiter().GetResult() でブロックするとメインループが回らずデッドロックするため、
        /// PlayMode テストでは IEnumerator + Task.IsCompleted を回す形が正攻法。
        /// </summary>
        public static IEnumerator RunScenario(string scenarioPath)
        {
            if (string.IsNullOrEmpty(scenarioPath))
                throw new ArgumentException("scenarioPath must not be null or empty", nameof(scenarioPath));

            // Bootstrap が走っていない (= Editor 経路でテスト Runner 単体実行) ケースの防御。
            if (ScenarioRegistry.Default.All.Count == 0)
            {
                ScenarioScanner.ScanAll();
            }

            var task = LiminalPalette.RunScenarioAsync(scenarioPath);
            while (!task.IsCompleted) yield return null;

            var result = task.Result;
            if (result.Success) yield break;

            string msg;
            if (result.FailedAtStep >= 0 && result.FailedAtStep < result.Steps.Count)
            {
                var step = result.Steps[result.FailedAtStep];
                msg = $"failed at step {result.FailedAtStep} ({step.Step?.Kind}): {step.Error ?? "<no message>"}";
            }
            else
            {
                msg = "no step-level failure detail (rejected as already running?)";
            }
            Assert.Fail($"scenario '{scenarioPath}' failed: {msg}");
        }
    }
}
