using System.Threading;
using System.Threading.Tasks;
using Void2610.LiminalPalette.Ipc.Json;
using Void2610.LiminalPalette.Ipc.Server;

namespace Void2610.LiminalPalette.Ipc.Endpoints
{
    /// <summary>
    /// GET /api/v1/health: 認証不要の生存確認。
    /// 戻り値: {"status":"ok","version":"0.4.0","commandCount":N}
    /// AI Agent / 監視スクリプトはこれでサーバー稼働を確認する。
    /// </summary>
    public sealed class HealthEndpoint : IIpcEndpoint
    {
        // 認証は不要。クライアントから token 無しでも到達できる。
        public bool RequiresAuth => false;

        public Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken ct)
        {
            var w = new JsonWriter();
            w.BeginObject();
            w.WriteString("status", "ok");
            w.WriteString("version", "0.4.0");
            w.WriteNumber("commandCount", LiminalPalette.Registry.All.Count);
            w.EndObject();
            return Task.FromResult(IpcResponse.Json(200, w.ToString()));
        }
    }
}
