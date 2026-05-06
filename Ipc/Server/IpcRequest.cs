using System;
using System.Collections.Generic;

namespace Void2610.LiminalPalette.Ipc.Server
{
    /// <summary>
    /// HttpListener から組み立てた IPC リクエストの不変表現。
    /// HttpServer 層と endpoint 層の境界を明示するため、HttpListenerContext を直接エンドポイントに渡さない。
    /// </summary>
    public sealed class IpcRequest
    {
        public string Method { get; }
        public string Path { get; }
        public IReadOnlyDictionary<string, string> Query { get; }
        public IReadOnlyDictionary<string, string> Headers { get; }
        public string Body { get; }

        public IpcRequest(string method, string path,
            IReadOnlyDictionary<string, string> query,
            IReadOnlyDictionary<string, string> headers,
            string body)
        {
            Method = method ?? "";
            Path = path ?? "/";
            Query = query ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Body = body ?? "";
        }
    }
}
