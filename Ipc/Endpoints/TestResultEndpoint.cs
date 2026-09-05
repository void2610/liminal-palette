using System;
using System.Threading;
using System.Threading.Tasks;
using Void2610.LiminalPalette.Ipc.Json;
using Void2610.LiminalPalette.Ipc.Server;
using Void2610.LiminalPalette.Ipc.TestRunning;
using Void2610.LiminalPalette.Ipc.Threading;

namespace Void2610.LiminalPalette.Ipc.Endpoints
{
    /// <summary>
    /// GET /api/v1/tests/result: 直近の <c>POST /api/v1/tests/run</c> の結果を返す (認証必須、編集時専用)。
    ///
    /// Response 200:
    ///   - {"state":"idle"}                                   … 未実行 (結果なし)
    ///   - {"state":"running","mode":"PlayMode"}              … 実行中
    ///   - {"state":"completed","result":"Passed","mode":"PlayMode",
    ///      "passed":N,"failed":N,"skipped":N,"inconclusive":N,"durationSeconds":X,
    ///      "failures":[{"name":"...","message":"..."}]}   … failures は失敗があるときのみ (上限 30 件)
    ///
    /// 結果は実装側で SessionState に保存されるため、PlayMode テストの DomainReload を跨いでも
    /// polling で取得できる。<see cref="TestRunnerBridge.Current"/> 未登録時は 501。
    ///
    /// SessionState はメインスレッド限定なので読み取りは <see cref="MainThreadDispatcher"/> 経由。
    /// </summary>
    public sealed class TestResultEndpoint : IIpcEndpoint
    {
        public bool RequiresAuth => true;

        public async Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken ct)
        {
            var service = TestRunnerBridge.Current;
            if (service == null)
                return IpcResponse.Json(501, ErrorBody(
                    "Test Runner is unavailable. Install com.unity.test-framework and run from the Unity Editor."));

            TestRunStatus status;
            try
            {
                status = await MainThreadDispatcher.RunAsync(() => Task.FromResult(service.GetStatus()));
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
            w.BeginObject();
            switch (status.Phase)
            {
                case TestRunPhase.Running:
                    w.WriteString("state", "running");
                    if (!string.IsNullOrEmpty(status.Mode)) w.WriteString("mode", status.Mode);
                    break;
                case TestRunPhase.Completed:
                    w.WriteString("state", "completed");
                    w.WriteString("result", status.Result);
                    if (!string.IsNullOrEmpty(status.Mode)) w.WriteString("mode", status.Mode);
                    w.WriteNumber("passed", status.Passed);
                    w.WriteNumber("failed", status.Failed);
                    w.WriteNumber("skipped", status.Skipped);
                    w.WriteNumber("inconclusive", status.Inconclusive);
                    w.WriteNumber("durationSeconds", status.DurationSeconds);
                    if (status.Failures is { Count: > 0 })
                    {
                        w.BeginArray("failures");
                        foreach (var f in status.Failures)
                        {
                            w.BeginObject();
                            w.WriteString("name", f.Name);
                            w.WriteString("message", f.Message);
                            w.EndObject();
                        }
                        w.EndArray();
                    }
                    break;
                default:
                    w.WriteString("state", "idle");
                    break;
            }
            w.EndObject();
            return IpcResponse.Json(200, w.ToString());
        }

        private static string ErrorBody(string error)
        {
            var w = new JsonWriter();
            w.BeginObject();
            w.WriteString("error", error ?? "");
            w.EndObject();
            return w.ToString();
        }
    }
}
