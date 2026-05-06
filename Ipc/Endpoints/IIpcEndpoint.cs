using System.Threading;
using System.Threading.Tasks;
using Void2610.LiminalPalette.Ipc.Server;

namespace Void2610.LiminalPalette.Ipc.Endpoints
{
    /// <summary>
    /// 1 個の HTTP エンドポイントを表す。Router がパスでディスパッチする。
    /// HandleAsync の戻り値の IpcResponse を HttpServer が HttpListenerResponse に詰め直す。
    /// </summary>
    public interface IIpcEndpoint
    {
        /// <summary>このエンドポイントが Authorization: Bearer トークンを必要とするか。</summary>
        bool RequiresAuth { get; }

        Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken ct);
    }
}
