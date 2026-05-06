using System.Threading;
using System.Threading.Tasks;
using Void2610.LiminalPalette.Ipc.Json;
using Void2610.LiminalPalette.Ipc.Server;
using Void2610.LiminalPalette.Ipc.Threading;

namespace Void2610.LiminalPalette.Ipc.Endpoints
{
    /// <summary>
    /// GET /api/v1/commands: 登録済みコマンド一覧 (認証必須)。
    /// AI Agent はこれを叩いて利用可能なコマンドと引数スキーマを発見する。
    /// </summary>
    public sealed class ListCommandsEndpoint : IIpcEndpoint
    {
        public bool RequiresAuth => true;

        public Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken ct)
        {
            // Registry の全件取得は同期 API だが、念のためメインスレッド経由で読む
            // (Phase 5 以降で Registry に動的更新が入っても安全に走るように)。
            return MainThreadDispatcher.RunAsync(async () =>
            {
                await Task.CompletedTask; // メインスレッドへの marshal が目的なので await 1 回入れる。
                var w = new JsonWriter();
                w.BeginObject();
                w.BeginArray("commands");
                var all = LiminalPalette.Registry.All;
                for (var i = 0; i < all.Count; i++)
                {
                    IpcContracts.WriteCommand(w, all[i]);
                }
                w.EndArray();
                w.EndObject();
                return IpcResponse.Json(200, w.ToString());
            });
        }
    }
}
