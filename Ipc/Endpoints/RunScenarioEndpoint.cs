using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Void2610.LiminalPalette.Ipc.Json;
using Void2610.LiminalPalette.Ipc.Server;
using Void2610.LiminalPalette.Ipc.Threading;
using Void2610.LiminalPalette;

namespace Void2610.LiminalPalette.Ipc.Endpoints
{
    /// <summary>
    /// POST /api/v1/scenarios/run: シナリオを実行する (認証必須)。
    ///
    /// Body は 2 形態:
    ///   - 名前指定: {"path": "Combat/EnemyTakesDamage"}
    ///   - ad-hoc:  {"steps": [ {"type": "command", ...}, ... ]}
    ///
    /// 実行は MainThreadDispatcher 経由で必ずメインスレッドで行う。
    /// 既存 ExecuteCommandEndpoint と同等のレートリミットを適用する。
    /// </summary>
    public sealed class RunScenarioEndpoint : IIpcEndpoint
    {
        public bool RequiresAuth => true;

        // ExecuteCommandEndpoint と同じ流儀でスライディングウィンドウのレートリミット。
        private readonly object _rateLock = new object();
        private readonly LinkedList<long> _recentTicks = new LinkedList<long>();

        public async Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken ct)
        {
            if (!TryAcquireRateSlot(out var rateErr))
                return IpcResponse.TooManyRequests(rateErr);

            if (!TryParseBody(request.Body, out var path, out var steps, out var parseErr))
                return IpcResponse.BadRequest(parseErr);

            ScenarioResult result;
            try
            {
                result = await MainThreadDispatcher.RunAsync(async () =>
                {
                    ScenarioResult inner;
                    if (steps != null)
                    {
                        inner = await LiminalPalette.RunScenarioAsync(steps, ct);
                    }
                    else
                    {
                        inner = await LiminalPalette.RunScenarioAsync(path, ct);
                    }
                    // UI 経由実行と同じく Log/History タブに記録する。InvocationStore はメインスレッド
                    // 限定の前提なので、MainThreadDispatcher の継続中 (= メインスレッド) で書き込む。
                    ScenarioInvocationRecorder.Record(inner, path);
                    return inner;
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return IpcResponse.InternalError(ex.Message);
            }

            var w = new JsonWriter();
            IpcContracts.WriteScenarioResult(w, result);
            // 既に他で実行中なら 409 Conflict、シナリオ未登録/その他失敗は 200 で result.success=false。
            // (HTTP セマンティクスを厳密に守るより、利用側のパースを単純に保つことを優先。)
            var status = result.WasRejectedAsAlreadyRunning ? 409 : 200;
            return IpcResponse.Json(status, w.ToString());
        }

        // /api/v1/scenarios/run のリクエストボディをパースする。
        // 戻り値 false でエラー、true なら path / steps のいずれか一方が non-null。
        internal static bool TryParseBody(
            string body,
            out string path,
            out IReadOnlyList<ScenarioStep> steps,
            out string parseErr)
        {
            path = null;
            steps = null;
            parseErr = null;

            if (string.IsNullOrEmpty(body))
            {
                parseErr = "Body is empty";
                return false;
            }

            try
            {
                var r = new JsonReader(body);
                if (r.Read() != JsonToken.BeginObject) { parseErr = "Body must be a JSON object"; return false; }

                string pathLocal = null;
                List<ScenarioStep> stepsLocal = null;

                while (true)
                {
                    var t = r.Read();
                    if (t == JsonToken.EndObject) break;
                    if (t != JsonToken.PropertyName) { parseErr = "Expected property name"; return false; }
                    var key = r.StringValue;
                    var valueToken = r.Read();
                    switch (key)
                    {
                        case "path":
                            if (valueToken == JsonToken.Null) break;
                            if (valueToken != JsonToken.String) { parseErr = "'path' must be string"; return false; }
                            pathLocal = r.StringValue;
                            break;
                        case "steps":
                            if (valueToken == JsonToken.Null) { stepsLocal = new List<ScenarioStep>(); break; }
                            if (valueToken != JsonToken.BeginArray) { parseErr = "'steps' must be array or null"; return false; }
                            stepsLocal = new List<ScenarioStep>();
                            if (!ReadSteps(r, stepsLocal, out var stepsErr))
                            {
                                parseErr = stepsErr;
                                return false;
                            }
                            break;
                        default:
                            SkipValue(r, valueToken);
                            break;
                    }
                }

                // path と steps は排他。両方指定 / 両方未指定はエラー。
                var hasPath = !string.IsNullOrEmpty(pathLocal);
                var hasSteps = stepsLocal != null;
                if (hasPath && hasSteps) { parseErr = "specify either 'path' or 'steps', not both"; return false; }
                if (!hasPath && !hasSteps) { parseErr = "either 'path' or 'steps' is required"; return false; }

                path = pathLocal;
                steps = stepsLocal;
                return true;
            }
            catch (Exception ex)
            {
                parseErr = $"JSON parse error: {ex.Message}";
                return false;
            }
        }

        // 配列要素 (各 step) を読み取り、ScenarioStep に変換して result に詰める。
        private static bool ReadSteps(JsonReader r, List<ScenarioStep> result, out string err)
        {
            err = null;
            while (true)
            {
                var t = r.Read();
                if (t == JsonToken.EndArray) return true;
                if (t != JsonToken.BeginObject) { err = "step must be a JSON object"; return false; }
                if (!ReadStep(r, out var step, out err)) return false;
                if (step != null) result.Add(step);
            }
        }

        // 1 ステップ分の object を読み取って ScenarioStep を組み立てる。
        // 必須フィールドは type。残りは type に応じて分岐。
        private static bool ReadStep(JsonReader r, out ScenarioStep step, out string err)
        {
            step = null;
            err = null;

            string type = null;
            string description = null;
            string commandPath = null;
            string observableFieldPath = null;
            string sceneName = null;
            object expected = null;
            bool expectedSet = false;
            float seconds = 0f;
            int frames = 0;
            Dictionary<string, object> args = null;

            while (true)
            {
                var t = r.Read();
                if (t == JsonToken.EndObject) break;
                if (t != JsonToken.PropertyName) { err = "expected property name in step"; return false; }
                var key = r.StringValue;
                var v = r.Read();
                switch (key)
                {
                    case "type":
                        if (v != JsonToken.String) { err = "step.type must be string"; return false; }
                        type = r.StringValue;
                        break;
                    case "description":
                        if (v == JsonToken.Null) break;
                        if (v != JsonToken.String) { err = "step.description must be string or null"; return false; }
                        description = r.StringValue;
                        break;
                    case "path":
                        if (v != JsonToken.String) { err = "step.path must be string"; return false; }
                        commandPath = r.StringValue;
                        observableFieldPath = r.StringValue;
                        break;
                    case "args":
                        if (v == JsonToken.Null) { args = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); break; }
                        if (v != JsonToken.BeginObject) { err = "step.args must be object or null"; return false; }
                        args = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        while (true)
                        {
                            var at = r.Read();
                            if (at == JsonToken.EndObject) break;
                            if (at != JsonToken.PropertyName) { err = "step.args: expected property name"; return false; }
                            var argKey = r.StringValue;
                            var av = r.Read();
                            switch (av)
                            {
                                // ExecuteCommandEndpoint と同じく、すべて string に正規化して args に詰める。
                                // ArgumentBinder が LiminalCommand のパラメータ型に応じて変換する。
                                case JsonToken.String: args[argKey] = r.StringValue; break;
                                case JsonToken.Number: args[argKey] = r.NumberValue.ToString(CultureInfo.InvariantCulture); break;
                                case JsonToken.True: args[argKey] = "true"; break;
                                case JsonToken.False: args[argKey] = "false"; break;
                                case JsonToken.Null: args[argKey] = ""; break;
                                default: err = $"step.args.{argKey}: unsupported value type"; return false;
                            }
                        }
                        break;
                    case "expected":
                        // assert_equals / assert_not_equals 用。string / number / bool / null を許容。
                        switch (v)
                        {
                            case JsonToken.String: expected = r.StringValue; break;
                            case JsonToken.Number: expected = r.NumberValue.ToString(CultureInfo.InvariantCulture); break;
                            case JsonToken.True: expected = "true"; break;
                            case JsonToken.False: expected = "false"; break;
                            case JsonToken.Null: expected = null; break;
                            default: err = "step.expected: unsupported value type"; return false;
                        }
                        expectedSet = true;
                        break;
                    case "seconds":
                        if (v != JsonToken.Number) { err = "step.seconds must be number"; return false; }
                        seconds = (float)r.NumberValue;
                        break;
                    case "frames":
                        if (v != JsonToken.Number) { err = "step.frames must be number"; return false; }
                        frames = (int)r.NumberValue;
                        break;
                    case "sceneName":
                        if (v != JsonToken.String) { err = "step.sceneName must be string"; return false; }
                        sceneName = r.StringValue;
                        break;
                    default:
                        SkipValue(r, v);
                        break;
                }
            }

            if (string.IsNullOrEmpty(type)) { err = "step.type is required"; return false; }
            switch (type)
            {
                case "command":
                    if (string.IsNullOrEmpty(commandPath)) { err = "command step requires 'path'"; return false; }
                    step = ScenarioStep.Run(commandPath, args, description);
                    return true;
                case "wait_seconds":
                    if (seconds < 0f) { err = "wait_seconds: seconds must be >= 0"; return false; }
                    step = ScenarioStep.WaitSeconds(seconds, description);
                    return true;
                case "wait_frames":
                    if (frames < 0) { err = "wait_frames: frames must be >= 0"; return false; }
                    step = ScenarioStep.WaitFrames(frames, description);
                    return true;
                case "assert_equals":
                    if (string.IsNullOrEmpty(observableFieldPath)) { err = "assert_equals requires 'path'"; return false; }
                    if (!expectedSet) { err = "assert_equals requires 'expected'"; return false; }
                    step = ScenarioStep.AssertEquals(observableFieldPath, expected, description);
                    return true;
                case "assert_not_equals":
                    if (string.IsNullOrEmpty(observableFieldPath)) { err = "assert_not_equals requires 'path'"; return false; }
                    if (!expectedSet) { err = "assert_not_equals requires 'expected'"; return false; }
                    step = ScenarioStep.AssertNotEquals(observableFieldPath, expected, description);
                    return true;
                case "load_scene":
                    if (string.IsNullOrEmpty(sceneName)) { err = "load_scene requires 'sceneName'"; return false; }
                    step = ScenarioStep.LoadScene(sceneName, description);
                    return true;
                case "assert_command_returns":
                    if (string.IsNullOrEmpty(commandPath)) { err = "assert_command_returns requires 'path'"; return false; }
                    // expected は明示的に省略 (= 戻り値内容を問わずコマンド成功だけ確かめる) も許容するため、
                    // expectedSet=false なら null を渡す。string 以外を expected に渡すと parser 段で
                    // string に正規化されるので、ここでは expected を ToString() してから渡す。
                    step = ScenarioStep.AssertCommandReturns(
                        commandPath, args,
                        expected: expectedSet ? expected?.ToString() : null,
                        description: description);
                    return true;
                default:
                    err = $"unknown step type: {type}";
                    return false;
            }
        }

        private static void SkipValue(JsonReader r, JsonToken first)
        {
            if (first != JsonToken.BeginObject && first != JsonToken.BeginArray) return;
            var depth = 1;
            while (depth > 0)
            {
                var t = r.Read();
                if (t == JsonToken.BeginObject || t == JsonToken.BeginArray) depth++;
                else if (t == JsonToken.EndObject || t == JsonToken.EndArray) depth--;
                else if (t == JsonToken.EndOfStream) break;
            }
        }

        private bool TryAcquireRateSlot(out string error)
        {
            error = null;
            var limit = IpcSettings.ExecuteRateLimitPerSecond;
            if (limit <= 0) return true;
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var freq = System.Diagnostics.Stopwatch.Frequency;
            var oneSecondAgo = now - freq;
            lock (_rateLock)
            {
                while (_recentTicks.First != null && _recentTicks.First.Value < oneSecondAgo)
                    _recentTicks.RemoveFirst();
                if (_recentTicks.Count >= limit)
                {
                    error = $"Rate limit exceeded ({limit} req/s)";
                    return false;
                }
                _recentTicks.AddLast(now);
                return true;
            }
        }
    }
}
