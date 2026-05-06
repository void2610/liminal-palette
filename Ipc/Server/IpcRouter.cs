using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Void2610.LiminalPalette.Ipc.Auth;
using Void2610.LiminalPalette.Ipc.Endpoints;

namespace Void2610.LiminalPalette.Ipc.Server
{
    /// <summary>
    /// (method, path) → IIpcEndpoint のテーブル。HttpServer から呼ばれる。
    /// 同パス別 method (例: GET と POST) は別エントリで登録する。
    /// path は固定文字列のみサポート。動的パラメータ (/foo/:id 等) は未対応。
    /// </summary>
    public sealed class IpcRouter
    {
        private readonly Dictionary<(string method, string path), IIpcEndpoint> _routes
            = new Dictionary<(string, string), IIpcEndpoint>();

        // 認証 (Bearer token) を担当する。
        // null の場合、RequiresAuth=true の endpoint は **常に 401** を返す (認証スキップにはならない)。
        // 認証無しでテストしたい場合は RequiresAuth=false の endpoint を使うか、テスト側で TokenAuthenticator をモックする。
        private readonly TokenAuthenticator _auth;

        public IpcRouter(TokenAuthenticator auth = null)
        {
            _auth = auth;
        }

        public void Register(string method, string path, IIpcEndpoint endpoint)
        {
            if (string.IsNullOrEmpty(method)) throw new ArgumentNullException(nameof(method));
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            _routes[(method.ToUpperInvariant(), path)] = endpoint;
        }

        /// <summary>
        /// リクエストをルートに振り分ける。
        ///   - 該当 endpoint が無い → 404 (path 自体が無い) または 405 (path はあるが method 違い)。
        ///   - endpoint.RequiresAuth が true で認証失敗 → 401。
        ///   - それ以外は endpoint.HandleAsync を呼ぶ。endpoint 内で例外が出たら 500。
        /// </summary>
        public async Task<IpcResponse> RouteAsync(IpcRequest request, CancellationToken ct)
        {
            var key = (request.Method.ToUpperInvariant(), request.Path);
            if (!_routes.TryGetValue(key, out var endpoint))
            {
                // 同 path で別 method があるか調べて 405 を返す。
                if (HasAnyMethodForPath(request.Path))
                    return IpcResponse.MethodNotAllowed($"Method {request.Method} not allowed for {request.Path}");
                return IpcResponse.NotFound($"No route for {request.Method} {request.Path}");
            }

            if (endpoint.RequiresAuth)
            {
                if (_auth == null || !_auth.Authenticate(request))
                    return IpcResponse.Unauthorized();
            }

            try
            {
                return await endpoint.HandleAsync(request, ct);
            }
            catch (OperationCanceledException)
            {
                throw; // CancellationToken の伝播は HttpServer 側で扱う。
            }
            catch (Exception ex)
            {
                return IpcResponse.InternalError(ex.Message);
            }
        }

        private bool HasAnyMethodForPath(string path)
        {
            foreach (var key in _routes.Keys)
                if (key.path == path) return true;
            return false;
        }
    }
}
