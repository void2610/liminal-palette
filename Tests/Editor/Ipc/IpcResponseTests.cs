using NUnit.Framework;
using Void2610.LiminalPalette.Ipc.Json;
using Void2610.LiminalPalette.Ipc.Server;

namespace Void2610.LiminalPalette.Tests.Ipc
{
    /// <summary>
    /// IpcResponse のエラー body が常に valid JSON であることを検証する。
    /// 旧実装は手動 Escape のため制御文字 (\t \b \f, U+0000-U+001F) で壊れていた。
    /// </summary>
    public sealed class IpcResponseTests
    {
        [Test]
        public void ErrorBody_ContainsControlChars_ProducesValidJson()
        {
            // \t \b \f と U+0001 を含む邪悪なメッセージ。
            var nasty = "tab\tback\bff\fctrlend";
            var res = IpcResponse.BadRequest(nasty);
            Assert.AreEqual(400, res.StatusCode);
            // JsonReader でパースできれば valid JSON である証明。
            var r = new JsonReader(res.Body);
            Assert.AreEqual(JsonToken.BeginObject, r.Read());
            Assert.AreEqual(JsonToken.PropertyName, r.Read());
            Assert.AreEqual("error", r.StringValue);
            Assert.AreEqual(JsonToken.String, r.Read());
            Assert.AreEqual(nasty, r.StringValue, "エスケープ往復で原文が完全復元されるべき");
            Assert.AreEqual(JsonToken.EndObject, r.Read());
        }

        [Test]
        public void ErrorBody_ContainsQuotesAndBackslashes_ProducesValidJson()
        {
            var msg = "He said \"hi\" with \\ and \"quotes\"";
            var res = IpcResponse.InternalError(msg);
            var r = new JsonReader(res.Body);
            Assert.AreEqual(JsonToken.BeginObject, r.Read());
            Assert.AreEqual(JsonToken.PropertyName, r.Read());
            Assert.AreEqual(JsonToken.String, r.Read());
            Assert.AreEqual(msg, r.StringValue);
        }

        [Test]
        public void ErrorBody_NullMessage_BecomesEmptyString()
        {
            var res = IpcResponse.NotFound(null);
            var r = new JsonReader(res.Body);
            Assert.AreEqual(JsonToken.BeginObject, r.Read());
            Assert.AreEqual(JsonToken.PropertyName, r.Read());
            Assert.AreEqual(JsonToken.String, r.Read());
            Assert.AreEqual("", r.StringValue);
        }

        [Test]
        public void AllErrorHelpers_ProduceValidJsonObject()
        {
            // 全ヘルパで {"error": "..."} の形になっていることを確認。
            var responses = new[]
            {
                IpcResponse.BadRequest("a"),
                IpcResponse.Unauthorized(),
                IpcResponse.NotFound("b"),
                IpcResponse.MethodNotAllowed("c"),
                IpcResponse.PayloadTooLarge("d"),
                IpcResponse.TooManyRequests("e"),
                IpcResponse.InternalError("f"),
            };
            foreach (var res in responses)
            {
                StringAssert.StartsWith("{\"error\":\"", res.Body);
                StringAssert.EndsWith("\"}", res.Body);
            }
        }
    }
}
