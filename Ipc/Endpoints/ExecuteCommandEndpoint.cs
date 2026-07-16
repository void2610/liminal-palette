using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Void2610.LiminalPalette.Ipc.Json;
using Void2610.LiminalPalette.Ipc.Server;
using Void2610.LiminalPalette.Ipc.Threading;
using Void2610.LiminalPalette;

namespace Void2610.LiminalPalette.Ipc.Endpoints
{
    /// <summary>
    /// POST /api/v1/execute: コマンドを実行する (認証必須)。
    /// Body: {"path": "Player/Health/Set", "args": {"value": "100"}}
    /// 文字列引数のみサポート (Phase 4 の HTTP では typed args 経路は使わない)。
    /// 実行は MainThreadDispatcher 経由で必ずメインスレッドで行う。
    /// 結果はパレットの Log / History タブにも記録する (UI 経由実行と同じ流儀)。
    /// </summary>
    public sealed class ExecuteCommandEndpoint : IIpcEndpoint
    {
        public bool RequiresAuth => true;

        // 直近 1 秒内の実行タイムスタンプ。レートリミットの判定に使う。
        // ConcurrentQueue でなく List で十分 (HandleAsync は HttpListener の各リクエストハンドラから呼ばれるが
        // 同時実行は別 Task なので、ロックで保護する)。
        private readonly object _rateLock = new object();
        private readonly System.Collections.Generic.LinkedList<long> _recentTicks = new System.Collections.Generic.LinkedList<long>();

        public async Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken ct)
        {
            if (!TryAcquireRateSlot(out var rateErr))
                return IpcResponse.TooManyRequests(rateErr);

            if (!TryParseBody(request.Body, out var path, out var args, out var parseErr))
                return IpcResponse.BadRequest(parseErr);

            CommandResult result;
            try
            {
                result = await MainThreadDispatcher.RunAsync(async () =>
                {
                    var r = await LiminalPalette.ExecuteAsync(path, args, ct);
                    // パレットの Log / History タブに記録 (UI 経路と同じ )。
                    // typedArgs 辞書は持っていないので文字列を object として詰める (UI 側で ToDisplayString される)。
                    var typed = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in args) typed[kv.Key] = kv.Value;
                    InvocationStore.Instance.Record(path, typed, r);
                    return r;
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
            IpcContracts.WriteResult(w, result);
            return IpcResponse.Json(200, w.ToString());
        }

        // 直近 1 秒以内の実行数を数えて IpcSettings.ExecuteRateLimitPerSecond 以下なら受理。
        // 古いタイムスタンプはスライディングウィンドウで捨てる。
        private bool TryAcquireRateSlot(out string error)
        {
            error = null;
            var limit = Ipc.IpcSettings.ExecuteRateLimitPerSecond;
            if (limit <= 0) return true; // 0 以下は無制限扱い (テスト用)。
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var freq = System.Diagnostics.Stopwatch.Frequency;
            var oneSecondAgo = now - freq;
            lock (_rateLock)
            {
                // 1 秒より古いタイムスタンプを先頭から捨てる。
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

        // body を JSON として読み、{path, args} を取り出す。
        // args は object (key→string) を想定。値が string でないものは ToString() でフォールバック。
        // 不正な body は false + parseErr を返す。
        internal static bool TryParseBody(string body, out string path, out IReadOnlyDictionary<string, string> args, out string parseErr)
        {
            path = null;
            args = null;
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
                Dictionary<string, string> argsLocal = null;

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
                            if (valueToken != JsonToken.String) { parseErr = "'path' must be string"; return false; }
                            pathLocal = r.StringValue;
                            break;
                        case "args":
                            if (valueToken == JsonToken.Null) { argsLocal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); break; }
                            if (valueToken != JsonToken.BeginObject) { parseErr = "'args' must be object or null"; return false; }
                            argsLocal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            while (true)
                            {
                                var at = r.Read();
                                if (at == JsonToken.EndObject) break;
                                if (at != JsonToken.PropertyName) { parseErr = "args: expected property name"; return false; }
                                var argKey = r.StringValue;
                                var av = r.Read();
                                switch (av)
                                {
                                    case JsonToken.String: argsLocal[argKey] = r.StringValue; break;
                                    case JsonToken.Number: argsLocal[argKey] = r.NumberValue.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                                    case JsonToken.True: argsLocal[argKey] = "true"; break;
                                    case JsonToken.False: argsLocal[argKey] = "false"; break;
                                    case JsonToken.Null: argsLocal[argKey] = ""; break;
                                    default: parseErr = $"args.{argKey}: unsupported value type"; return false;
                                }
                            }
                            break;
                        default:
                            // 未知の key はスキップ (前方互換のため)。値が object/array の場合は深さを追って読み飛ばす。
                            SkipValue(r, valueToken);
                            break;
                    }
                }

                if (string.IsNullOrEmpty(pathLocal)) { parseErr = "'path' is required"; return false; }

                path = pathLocal;
                args = argsLocal ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return true;
            }
            catch (Exception ex)
            {
                parseErr = $"JSON parse error: {ex.Message}";
                return false;
            }
        }

        // 未知 key の value を読み飛ばす。object/array は内部要素まで。
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
    }
}
