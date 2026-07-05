using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Void2610.LiminalPalette.Ipc.Json;
using Void2610.LiminalPalette.Ipc.Server;
using Void2610.LiminalPalette.Ipc.TestRunning;
using Void2610.LiminalPalette.Ipc.Threading;

namespace Void2610.LiminalPalette.Ipc.Endpoints
{
    /// <summary>
    /// POST /api/v1/tests/run: Unity Test Runner を起動する (認証必須、編集時専用)。
    ///
    /// Body: {"mode": "playmode" | "editmode", "filter": "&lt;regex&gt;"}
    ///   - mode: 必須。"playmode" / "editmode" (大文字小文字無視)。
    ///   - filter: 任意。テスト full name の正規表現 (空 / 省略で全件)。
    ///
    /// 即リターンし、結果は GET /api/v1/tests/result を polling して取得する。
    /// enterPlayModeOptions を一切書き換えないため、外部 MCP ブリッジ (uLoopMCP 等) の
    /// run-tests のように ProjectSettings/EditorSettings.asset を汚す churn を起こさない。
    ///
    /// TestRunnerApi はメインスレッド限定なので実行は <see cref="MainThreadDispatcher"/> 経由。
    /// <see cref="TestRunnerBridge.Current"/> が未登録 (com.unity.test-framework 未導入) の場合は 501。
    /// </summary>
    public sealed class RunTestsEndpoint : IIpcEndpoint
    {
        public bool RequiresAuth => true;

        // ExecuteCommandEndpoint / RunScenarioEndpoint と同じスライディングウィンドウのレートリミット。
        private readonly object _rateLock = new object();
        private readonly LinkedList<long> _recentTicks = new LinkedList<long>();

        public async Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken ct)
        {
            if (!TryAcquireRateSlot(out var rateErr))
                return IpcResponse.TooManyRequests(rateErr);

            var service = TestRunnerBridge.Current;
            if (service == null)
                return IpcResponse.Json(501, ErrorBody(
                    "Test Runner is unavailable. Install com.unity.test-framework and run from the Unity Editor."));

            if (!TryParseBody(request.Body, out var mode, out var filter, out var parseErr))
                return IpcResponse.BadRequest(parseErr);

            bool started;
            string startError = null;
            try
            {
                var captured = new string[1];
                started = await MainThreadDispatcher.RunAsync(() =>
                {
                    var ok = service.TryStartRun(mode, filter, out var err);
                    captured[0] = err;
                    return Task.FromResult(ok);
                });
                startError = captured[0];
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return IpcResponse.InternalError(ex.Message);
            }

            if (!started)
            {
                // 既に実行中。RunScenarioEndpoint に倣い 409 で status を返す (error 形式ではない)。
                var busy = new JsonWriter();
                busy.BeginObject();
                busy.WriteString("status", "running");
                busy.WriteString("message", string.IsNullOrEmpty(startError)
                    ? "a test run is already in progress"
                    : startError);
                busy.EndObject();
                return IpcResponse.Json(409, busy.ToString());
            }

            var w = new JsonWriter();
            w.BeginObject();
            w.WriteString("status", "started");
            w.WriteString("mode", mode == "editmode" ? "EditMode" : "PlayMode");
            w.WriteString("filter", string.IsNullOrEmpty(filter) ? "all" : filter);
            w.EndObject();
            return IpcResponse.Json(200, w.ToString());
        }

        // body を JSON として読み、{mode, filter} を取り出す。
        // mode は必須で "playmode" / "editmode" に正規化。それ以外は 400。
        internal static bool TryParseBody(string body, out string mode, out string filter, out string parseErr)
        {
            mode = null;
            filter = "";
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

                string modeLocal = null;
                string filterLocal = "";

                while (true)
                {
                    var t = r.Read();
                    if (t == JsonToken.EndObject) break;
                    if (t != JsonToken.PropertyName) { parseErr = "Expected property name"; return false; }
                    var key = r.StringValue;
                    var valueToken = r.Read();
                    switch (key)
                    {
                        case "mode":
                            if (valueToken != JsonToken.String) { parseErr = "'mode' must be string"; return false; }
                            modeLocal = r.StringValue;
                            break;
                        case "filter":
                            if (valueToken == JsonToken.Null) { filterLocal = ""; break; }
                            if (valueToken != JsonToken.String) { parseErr = "'filter' must be string or null"; return false; }
                            filterLocal = r.StringValue;
                            break;
                        default:
                            SkipValue(r, valueToken);
                            break;
                    }
                }

                if (string.IsNullOrEmpty(modeLocal)) { parseErr = "'mode' is required (\"playmode\" or \"editmode\")"; return false; }
                var normalized = modeLocal.Trim().ToLowerInvariant();
                if (normalized != "playmode" && normalized != "editmode")
                {
                    parseErr = $"'mode' must be \"playmode\" or \"editmode\", got \"{modeLocal}\"";
                    return false;
                }

                mode = normalized;
                filter = filterLocal ?? "";
                return true;
            }
            catch (Exception ex)
            {
                parseErr = $"JSON parse error: {ex.Message}";
                return false;
            }
        }

        private static string ErrorBody(string error)
        {
            var w = new JsonWriter();
            w.BeginObject();
            w.WriteString("error", error ?? "");
            w.EndObject();
            return w.ToString();
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
