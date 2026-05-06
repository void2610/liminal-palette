using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Void2610.LiminalPalette.Ipc.Server
{
    /// <summary>
    /// HttpListener ベースのローカル HTTP サーバー。
    /// 127.0.0.1 と localhost のみにバインドする (LAN への露出を避ける)。
    /// ポート競合時は IpcSettings.PortRetryCount まで隣接ポートを試す。
    /// 各リクエスト処理は Task.Run で別スレッドで動かす (accept ループをブロックしない)。
    ///
    /// 設計判断:
    ///   - Stop() は CancellationTokenSource.Cancel + listener.Abort で確実に accept ループを抜ける。
    ///   - エンドポイント内例外は IpcRouter が 500 に変換するので、HttpServer 側では握りつぶさない。
    ///   - Stop 後に再 Start する場合は新しいインスタンスを作ること (Stop は dispose 同等)。
    /// </summary>
    public sealed class HttpServer : IDisposable
    {
        private readonly IpcRouter _router;
        private readonly int _requestedPort;
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private Task _acceptLoop;
        private int _actualPort;

        /// <summary>実際に listen しているポート (リトライ後のポート)。Start 前は -1。</summary>
        public int Port => _actualPort;

        public HttpServer(IpcRouter router, int port)
        {
            _router = router ?? throw new ArgumentNullException(nameof(router));
            _requestedPort = port;
            _actualPort = -1;
        }

        /// <summary>
        /// listener を起動して accept ループを開始する。
        /// ポート競合時は port+1, port+2, ... と試す (IpcSettings.PortRetryCount 回)。
        /// </summary>
        public void Start()
        {
            if (_listener != null) throw new InvalidOperationException("Already started.");

            for (var i = 0; i <= IpcSettings.PortRetryCount; i++)
            {
                var tryPort = _requestedPort + i;
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{tryPort}/");
                listener.Prefixes.Add($"http://localhost:{tryPort}/");
                try
                {
                    listener.Start();
                    _listener = listener;
                    _actualPort = tryPort;
                    break;
                }
                catch (HttpListenerException)
                {
                    // ポート競合 (Windows でよく出るパス)。次を試す。
                    try { listener.Close(); } catch { /* swallow */ }
                }
                catch (System.Net.Sockets.SocketException)
                {
                    // ポート競合 (Mac / Linux で "Address already in use" として出るパス)。
                    // .NET の HttpListener は OS によって例外型が異なるため両方拾って次 port を試す。
                    try { listener.Close(); } catch { /* swallow */ }
                }
                catch (Exception)
                {
                    try { listener.Close(); } catch { /* swallow */ }
                    throw;
                }
            }

            if (_listener == null)
                throw new InvalidOperationException(
                    $"Failed to bind any port from {_requestedPort} to {_requestedPort + IpcSettings.PortRetryCount}.");

            _cts = new CancellationTokenSource();
            _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        /// <summary>accept ループを止めて listener を閉じる。</summary>
        public void Stop()
        {
            if (_listener == null) return;
            try { _cts?.Cancel(); } catch { /* swallow */ }
            try { _listener.Abort(); } catch { /* swallow */ }
            try { _listener.Close(); } catch { /* swallow */ }

            // accept ループの終了を最大 2 秒だけ待つ (DomainReload 等で長時間待たないため)。
            try { _acceptLoop?.Wait(2000); } catch { /* swallow (Cancellation/AggregateException 想定) */ }

            _listener = null;
            _cts = null;
            _acceptLoop = null;
            _actualPort = -1;
        }

        public void Dispose() => Stop();

        // ---- internals ----

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException)
                {
                    // Stop() が呼ばれた。
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                // 各リクエストは別タスクで処理 (accept をブロックしない)。
                var ctxLocal = ctx;
                _ = Task.Run(() => HandleRequestAsync(ctxLocal, ct), ct);
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            try
            {
                var request = await BuildIpcRequestAsync(ctx, ct).ConfigureAwait(false);
                IpcResponse response;
                if (request == null)
                {
                    response = IpcResponse.PayloadTooLarge($"Body exceeds limit ({IpcSettings.MaxRequestBodyBytes} bytes)");
                }
                else
                {
                    response = await _router.RouteAsync(request, ct).ConfigureAwait(false);
                }
                await WriteResponseAsync(ctx, response).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 想定外の例外。500 を返してログに出す。
                try
                {
                    await WriteResponseAsync(ctx, IpcResponse.InternalError(ex.Message)).ConfigureAwait(false);
                }
                catch { /* 既に response が閉じている等 */ }
                Debug.LogError($"[LiminalPalette.Ipc] Unhandled: {ex}");
            }
        }

        private static async Task<IpcRequest> BuildIpcRequestAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            var req = ctx.Request;
            // Content-Length が信用できる場合は事前判定で fail-fast。
            // ただし Content-Length 無し / 偽装の可能性があるため body 読み込み中も累積で判定する。
            if (req.ContentLength64 > IpcSettings.MaxRequestBodyBytes) return null;

            var headers = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < req.Headers.Count; i++)
            {
                var key = req.Headers.GetKey(i);
                var values = req.Headers.GetValues(i);
                if (values != null && values.Length > 0) headers[key] = values[0];
            }

            var query = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < req.QueryString.Count; i++)
            {
                var key = req.QueryString.GetKey(i);
                if (key == null) continue;
                query[key] = req.QueryString[i];
            }

            string body = "";
            if (req.HasEntityBody)
            {
                // チャンク読み込み: CopyToAsync で全量を読んでから判定する旧実装は、
                // クライアントが Content-Length を偽装すると無制限にメモリ確保される DoS 面があった。
                // 8KB バッファで読みつつ、累積バイト数が上限を超えた瞬間に null を返して 413 にする。
                const int BufferSize = 8192;
                var buffer = new byte[BufferSize];
                using var ms = new MemoryStream();
                while (true)
                {
                    var read = await req.InputStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                    if (read <= 0) break;
                    ms.Write(buffer, 0, read);
                    if (ms.Length > IpcSettings.MaxRequestBodyBytes) return null;
                }
                var encoding = req.ContentEncoding ?? Encoding.UTF8;
                body = encoding.GetString(ms.ToArray());
            }

            return new IpcRequest(req.HttpMethod, req.Url.AbsolutePath, query, headers, body);
        }

        private static async Task WriteResponseAsync(HttpListenerContext ctx, IpcResponse response)
        {
            var res = ctx.Response;
            res.StatusCode = response.StatusCode;
            res.ContentType = response.ContentType;
            if (response.ExtraHeaders != null)
            {
                foreach (var kv in response.ExtraHeaders) res.AddHeader(kv.Key, kv.Value);
            }
            var bytes = Encoding.UTF8.GetBytes(response.Body);
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            res.OutputStream.Close();
        }
    }
}
