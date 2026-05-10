using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Void2610.LiminalPalette.Ipc.Json;
using Void2610.LiminalPalette.Ipc.Server;

namespace Void2610.LiminalPalette.Ipc.Endpoints
{
    /// <summary>
    /// GET /api/v1/health: 認証不要の生存確認。
    /// 戻り値: {"status":"ok","version":"0.4.0","projectName":"...","projectPath":"...","commandCount":N}
    /// projectName / projectPath は同一マシンで複数 Unity プロジェクトが
    /// 同時起動しているときに lp CLI 側がポートとプロジェクトを紐付けるために使う。
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
            w.WriteString("projectName", Application.productName ?? "");
            w.WriteString("projectPath", GetProjectPath());
            w.WriteNumber("commandCount", LiminalPalette.Registry.All.Count);
            w.EndObject();
            return Task.FromResult(IpcResponse.Json(200, w.ToString()));
        }

        // Application.dataPath は <project>/Assets を返す。親ディレクトリがプロジェクトルート。
        // Player ビルド等で空の場合もあるので null フォールバックで握る。
        private static string GetProjectPath()
        {
            var dataPath = Application.dataPath;
            if (string.IsNullOrEmpty(dataPath)) return "";
            return Path.GetDirectoryName(dataPath) ?? "";
        }
    }
}
