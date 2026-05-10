using System.Threading;
using System.Threading.Tasks;
using Void2610.LiminalPalette.Ipc.Json;
using Void2610.LiminalPalette.Ipc.Server;

namespace Void2610.LiminalPalette.Ipc.Endpoints
{
    /// <summary>
    /// GET /api/v1/health: 認証不要の生存確認。
    /// 戻り値: {"status":"ok","version":"0.4.0","mode":"editor|runtime","projectName":"...","projectPath":"...","commandCount":N}
    /// projectName / projectPath は同一マシンで複数 Unity プロジェクトが
    /// 同時起動しているときに CLI 側がポートとプロジェクトを紐付けるために使う。
    /// mode は同一プロジェクト内で Editor / Runtime (Play Mode) listener を区別するためのフラグ。
    ///
    /// 重要: HandleAsync は HTTP ワーカースレッドから呼ばれるので Unity API
    /// (Application.productName / Application.dataPath 等) を直接触れない。
    /// projectName / projectPath は bootstrap (メインスレッド) で取得済みの値を
    /// コンストラクタ経由で受け取り、ここではそのまま JSON に書き出すだけにする。
    /// </summary>
    public sealed class HealthEndpoint : IIpcEndpoint
    {
        private readonly string _mode;
        private readonly string _projectName;
        private readonly string _projectPath;

        /// <summary>
        /// <paramref name="mode"/> は "editor" または "runtime"。
        /// <paramref name="projectName"/> は <c>Application.productName</c> をメインスレッドで取得した値。
        /// <paramref name="projectPath"/> は <c>Application.dataPath</c> の親ディレクトリ (同上)。
        /// 取れなかった場合は空文字列を渡す。
        /// </summary>
        public HealthEndpoint(string mode, string projectName, string projectPath)
        {
            _mode = string.IsNullOrEmpty(mode) ? "unknown" : mode;
            _projectName = projectName ?? "";
            _projectPath = projectPath ?? "";
        }

        // 認証は不要。クライアントから token 無しでも到達できる。
        public bool RequiresAuth => false;

        public Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken ct)
        {
            var w = new JsonWriter();
            w.BeginObject();
            w.WriteString("status", "ok");
            w.WriteString("version", "0.4.0");
            w.WriteString("mode", _mode);
            w.WriteString("projectName", _projectName);
            w.WriteString("projectPath", _projectPath);
            w.WriteNumber("commandCount", LiminalPalette.Registry.All.Count);
            w.EndObject();
            return Task.FromResult(IpcResponse.Json(200, w.ToString()));
        }
    }
}
