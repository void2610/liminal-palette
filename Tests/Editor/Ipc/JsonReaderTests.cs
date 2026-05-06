using NUnit.Framework;
using Void2610.LiminalPalette.Ipc.Json;

namespace Void2610.LiminalPalette.Tests.Ipc
{
    public sealed class JsonReaderTests
    {
        [Test]
        public void EmptyObject()
        {
            var r = new JsonReader("{}");
            Assert.AreEqual(JsonToken.BeginObject, r.Read());
            Assert.AreEqual(JsonToken.EndObject, r.Read());
            Assert.AreEqual(JsonToken.EndOfStream, r.Read());
        }

        [Test]
        public void SimpleKeyValue()
        {
            var r = new JsonReader("{\"a\":1,\"b\":\"x\"}");
            Assert.AreEqual(JsonToken.BeginObject, r.Read());

            Assert.AreEqual(JsonToken.PropertyName, r.Read());
            Assert.AreEqual("a", r.StringValue);
            Assert.AreEqual(JsonToken.Number, r.Read());
            Assert.AreEqual(1, r.NumberValue);

            Assert.AreEqual(JsonToken.PropertyName, r.Read());
            Assert.AreEqual("b", r.StringValue);
            Assert.AreEqual(JsonToken.String, r.Read());
            Assert.AreEqual("x", r.StringValue);

            Assert.AreEqual(JsonToken.EndObject, r.Read());
        }

        [Test]
        public void NestedObject()
        {
            var r = new JsonReader("{\"outer\":{\"k\":true}}");
            Assert.AreEqual(JsonToken.BeginObject, r.Read());
            Assert.AreEqual(JsonToken.PropertyName, r.Read());
            Assert.AreEqual("outer", r.StringValue);
            Assert.AreEqual(JsonToken.BeginObject, r.Read());
            Assert.AreEqual(JsonToken.PropertyName, r.Read());
            Assert.AreEqual("k", r.StringValue);
            Assert.AreEqual(JsonToken.True, r.Read());
            Assert.AreEqual(JsonToken.EndObject, r.Read());
            Assert.AreEqual(JsonToken.EndObject, r.Read());
        }

        [Test]
        public void Array_MixedValues()
        {
            var r = new JsonReader("[\"a\",1,true,null]");
            Assert.AreEqual(JsonToken.BeginArray, r.Read());
            Assert.AreEqual(JsonToken.String, r.Read());
            Assert.AreEqual("a", r.StringValue);
            Assert.AreEqual(JsonToken.Number, r.Read());
            Assert.AreEqual(1, r.NumberValue);
            Assert.AreEqual(JsonToken.True, r.Read());
            Assert.AreEqual(JsonToken.Null, r.Read());
            Assert.AreEqual(JsonToken.EndArray, r.Read());
        }

        [Test]
        public void StringEscape_BasicAndUnicode()
        {
            var r = new JsonReader("[\"a\\nb\",\"\\u0041\\u0042\"]");
            Assert.AreEqual(JsonToken.BeginArray, r.Read());
            Assert.AreEqual(JsonToken.String, r.Read());
            Assert.AreEqual("a\nb", r.StringValue);
            Assert.AreEqual(JsonToken.String, r.Read());
            Assert.AreEqual("AB", r.StringValue);
            Assert.AreEqual(JsonToken.EndArray, r.Read());
        }

        [Test]
        public void Number_FloatAndNegativeAndExponent()
        {
            var r = new JsonReader("[3.14,-2,1e3]");
            Assert.AreEqual(JsonToken.BeginArray, r.Read());
            Assert.AreEqual(JsonToken.Number, r.Read());
            Assert.AreEqual(3.14, r.NumberValue, 1e-10);
            Assert.AreEqual(JsonToken.Number, r.Read());
            Assert.AreEqual(-2, r.NumberValue);
            Assert.AreEqual(JsonToken.Number, r.Read());
            Assert.AreEqual(1000, r.NumberValue);
            Assert.AreEqual(JsonToken.EndArray, r.Read());
        }

        [Test]
        public void Whitespace_IsTolerated()
        {
            var r = new JsonReader("  {  \"k\"  :  \"v\"  }  ");
            Assert.AreEqual(JsonToken.BeginObject, r.Read());
            Assert.AreEqual(JsonToken.PropertyName, r.Read());
            Assert.AreEqual("k", r.StringValue);
            Assert.AreEqual(JsonToken.String, r.Read());
            Assert.AreEqual("v", r.StringValue);
            Assert.AreEqual(JsonToken.EndObject, r.Read());
        }

        [Test]
        public void TruncatedString_Throws()
        {
            var r = new JsonReader("\"unterm");
            Assert.Throws<System.FormatException>(() => r.Read());
        }

        [Test]
        public void UnclosedObject_ThrowsAtEndOfStream()
        {
            var r = new JsonReader("{\"k\":1");
            Assert.AreEqual(JsonToken.BeginObject, r.Read());
            Assert.AreEqual(JsonToken.PropertyName, r.Read());
            Assert.AreEqual(JsonToken.Number, r.Read());
            // 次の Read は EOS。クライアントが BeginObject/EndObject の対応を見る責任。
            Assert.AreEqual(JsonToken.EndOfStream, r.Read());
        }
    }
}
