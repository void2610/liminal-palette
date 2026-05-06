using System;
using System.Threading;
using System.Threading.Tasks;
using Void2610.LiminalPalette.Ipc.Json;
using Void2610.LiminalPalette.Ipc.Server;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Ipc.Endpoints
{
    /// <summary>
    /// GET /api/v1/logs: 起動履歴 (InvocationStore) を新しい順で返す。
    /// クエリ ?limit=N で件数制限 (既定 50、上限 InvocationStore.Capacity)。
    /// </summary>
    public sealed class ListLogsEndpoint : IIpcEndpoint
    {
        private const int DefaultLimit = 50;

        public bool RequiresAuth => true;

        public Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken ct)
        {
            var limit = ParseLimit(request);

            // InvocationStore はスレッドセーフ (内部 lock 済み) なので marshal 不要。
            var entries = InvocationStore.Instance.Entries;

            var w = new JsonWriter();
            w.BeginObject();
            w.BeginArray("invocations");
            // entries は古い順。新しい順で limit 件返す。
            var count = 0;
            for (var i = entries.Count - 1; i >= 0 && count < limit; i--, count++)
            {
                IpcContracts.WriteInvocation(w, entries[i]);
            }
            w.EndArray();
            w.WriteNumber("total", entries.Count);
            w.WriteNumber("limit", limit);
            w.EndObject();
            return Task.FromResult(IpcResponse.Json(200, w.ToString()));
        }

        private static int ParseLimit(IpcRequest request)
        {
            if (request.Query != null && request.Query.TryGetValue("limit", out var raw))
            {
                if (int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0)
                {
                    return Math.Min(v, InvocationStore.Capacity);
                }
            }
            return DefaultLimit;
        }
    }
}
