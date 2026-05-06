using System.Collections.Generic;
using Void2610.LiminalPalette.Ipc.Json;

namespace Void2610.LiminalPalette.Ipc.Server
{
    /// <summary>
    /// IPC レスポンスの不変表現。HttpServer が HttpListenerResponse に詰め直して返す。
    /// </summary>
    public sealed class IpcResponse
    {
        public int StatusCode { get; }
        public string ContentType { get; }
        public string Body { get; }
        public IReadOnlyDictionary<string, string> ExtraHeaders { get; }

        public IpcResponse(int statusCode, string contentType, string body,
            IReadOnlyDictionary<string, string> extraHeaders = null)
        {
            StatusCode = statusCode;
            ContentType = contentType ?? "text/plain; charset=utf-8";
            Body = body ?? "";
            ExtraHeaders = extraHeaders;
        }

        public static IpcResponse Json(int status, string json)
            => new IpcResponse(status, "application/json; charset=utf-8", json);

        public static IpcResponse PlainText(int status, string text)
            => new IpcResponse(status, "text/plain; charset=utf-8", text);

        // よく使うエラー類をヘルパで提供。body は常に JSON で {"error": "..."} 形式に揃える。
        public static IpcResponse BadRequest(string error) => Json(400, ErrorBody(error));
        public static IpcResponse Unauthorized(string error = "Unauthorized") => Json(401, ErrorBody(error));
        public static IpcResponse NotFound(string error) => Json(404, ErrorBody(error));
        public static IpcResponse MethodNotAllowed(string error) => Json(405, ErrorBody(error));
        public static IpcResponse PayloadTooLarge(string error) => Json(413, ErrorBody(error));
        public static IpcResponse TooManyRequests(string error) => Json(429, ErrorBody(error));
        public static IpcResponse InternalError(string error) => Json(500, ErrorBody(error));

        // JsonWriter で組み立てる。手動 Escape の旧実装は \ " \n \r しか扱わず、
        // 制御文字 (\t \b \f や U+0000-U+001F) を含むメッセージで invalid JSON 化する不具合があった。
        // JsonWriter 側で全 control char を \uXXXX に正しくエスケープする。
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
