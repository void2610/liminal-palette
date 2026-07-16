using System;
using System.Collections.Generic;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// ScenarioResult を <see cref="InvocationStore"/> に書き出す薄いヘルパ。
    ///
    /// 設計判断:
    ///   - 呼び出しサイト (PaletteView と RunScenarioEndpoint) で RunScenarioAsync の戻り値を
    ///     直接渡す方式にした。Core (ScenarioExecutor) に static event を持たせて UI で
    ///     [InitializeOnLoadMethod] / [RuntimeInitializeOnLoadMethod] で購読する案もあったが、
    ///     ・DomainReload を跨いだ静的状態と二重購読に注意が要る
    ///     ・Core から UI を見えなくする責務分離が中途半端になる
    ///     という理由で却下し、呼び出しサイトで明示的に Record する方針に揃えた。
    ///   - 各 Command ステップは通常コマンド実行と同じ形式で記録 → Log/History タブに並ぶ。
    ///   - シナリオ全体は "Scenario/&lt;path&gt;" の擬似 Path で 1 件記録 → Log タブに俯瞰行が出る
    ///     (History タブから Run しても CommandRegistry にヒットしないため再実行は失敗する。
    ///     再実行は Scenario タブから行う運用)。
    ///   - WasRejectedAsAlreadyRunning は実際に走っていないため記録しない。
    /// </summary>
    public static class ScenarioInvocationRecorder
    {
        // CommandResult を生成するときに渡す空の logs。各シナリオ呼び出しで使い回す。
        private static readonly LogEntry[] EmptyLogs = Array.Empty<LogEntry>();

        // ad-hoc 実行 (Path 未指定) のときに使う表示名。
        // Log タブで「ad-hoc シナリオの実行」を 1 行で識別できるよう、Scenario プレフィックスを付ける。
        private const string AdHocPath = "Scenario/(ad-hoc)";

        /// <summary>
        /// シナリオ実行結果を InvocationStore に書き込む。
        /// Command ステップごとの記録 + シナリオ全体の集約 1 件を行う。
        /// </summary>
        /// <param name="result">RunScenarioAsync の戻り値。null は無視。</param>
        /// <param name="scenarioPath">
        /// 名前付き実行のシナリオ Path。ad-hoc 実行のときは null を渡す
        /// (result.Path が non-null ならそちらが優先される)。
        /// </param>
        public static void Record(ScenarioResult result, string scenarioPath = null)
        {
            if (result == null) return;
            // 実際に走らなかったケース (= 既に他シナリオが実行中で弾かれた) は記録しない。
            // 利用者から見ても「実行が起きなかった」イベントなので Log タブに混ぜたくない。
            if (result.WasRejectedAsAlreadyRunning) return;

            // ステップ単位で Command を記録。Wait/Assert は CommandResult を持たないためスキップ。
            // 全てシナリオ由来フラグを立てておく (History タブ側で除外、Log タブで閲覧可能)。
            for (var i = 0; i < result.Steps.Count; i++)
            {
                var sr = result.Steps[i];
                if (sr.Step is CommandStep cs && sr.CommandResult != null)
                {
                    InvocationStore.Instance.Record(cs.CommandPath, cs.Args, sr.CommandResult, isFromScenario: true);
                }
            }

            // シナリオ全体を 1 件記録。Path 解決の優先度は引数 > result.Path > "(ad-hoc)"。
            // 集約自体も History からは除外したい (再実行は Scenario タブから行う運用)。
            var rawPath = !string.IsNullOrEmpty(scenarioPath) ? scenarioPath : result.Path;
            var displayPath = string.IsNullOrEmpty(rawPath) ? AdHocPath : "Scenario/" + rawPath;
            var aggregate = BuildAggregate(result);
            InvocationStore.Instance.Record(displayPath, EmptyArgs, aggregate, isFromScenario: true);
        }

        // 引数辞書は毎回同じ空のものを渡す (InvocationStore 側でディフェンシブにコピーされるため共有 OK)。
        private static readonly IReadOnlyDictionary<string, object> EmptyArgs
            = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // ScenarioResult から Log タブ表示用の CommandResult を合成する。
        // 成功時: Ok(null, [], duration)
        // 失敗時: Fail("Step N (<Kind>) failed: <error>", null, [], duration)
        // logs はサブステップから収集した順に詰める。
        private static CommandResult BuildAggregate(ScenarioResult result)
        {
            var logs = CollectStepLogs(result);
            if (result.Success)
            {
                // 戻り値はなし (シナリオ自体は値を返さない)。所要時間と success のみ伝われば足りる。
                return CommandResult.Ok(null, logs, result.Duration);
            }

            string error;
            if (result.FailedAtStep >= 0 && result.FailedAtStep < result.Steps.Count)
            {
                var failed = result.Steps[result.FailedAtStep];
                var kind = failed.Step?.Kind.ToString() ?? "Unknown";
                error = $"Step {result.FailedAtStep} ({kind}) failed: {failed.Error ?? "<no message>"}";
            }
            else
            {
                error = "Scenario failed (no step result)";
            }
            return CommandResult.Fail(error, null, logs, result.Duration);
        }

        // 全ステップの CommandResult.Logs を平坦化して 1 リストにする。Log タブの表示用なので
        // 元の log.Type / Message / TimestampUtc はそのまま保持する。
        private static IReadOnlyList<LogEntry> CollectStepLogs(ScenarioResult result)
        {
            // 大半の場合 0 件 or ごく少数。capacity を雑に推定。
            List<LogEntry> all = null;
            for (var i = 0; i < result.Steps.Count; i++)
            {
                var cr = result.Steps[i].CommandResult;
                if (cr == null || cr.Logs.Count == 0) continue;
                if (all == null) all = new List<LogEntry>(cr.Logs.Count);
                for (var j = 0; j < cr.Logs.Count; j++) all.Add(cr.Logs[j]);
            }
            return (IReadOnlyList<LogEntry>)all ?? EmptyLogs;
        }
    }
}
