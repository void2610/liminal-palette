using NUnit.Framework;
using Void2610.LiminalPalette.Ipc.Json;

namespace Void2610.LiminalPalette.Tests.Ipc
{
    public sealed class JsonWriterTests
    {
        [Test]
        public void EmptyObject()
        {
            var w = new JsonWriter();
            w.BeginObject();
            w.EndObject();
            Assert.AreEqual("{}", w.ToString());
        }

        [Test]
        public void EmptyArray()
        {
            var w = new JsonWriter();
            w.BeginArray();
            w.EndArray();
            Assert.AreEqual("[]", w.ToString());
        }

        [Test]
        public void ObjectWithStringValue()
        {
            var w = new JsonWriter();
            w.BeginObject();
            w.WriteString("key", "value");
            w.EndObject();
            Assert.AreEqual("{\"key\":\"value\"}", w.ToString());
        }

        [Test]
        public void ObjectWithMultipleEntries_InsertsCommas()
        {
            var w = new JsonWriter();
            w.BeginObject();
            w.WriteString("a", "1");
            w.WriteNumber("b", 2);
            w.WriteBool("c", true);
            w.WriteNull("d");
            w.EndObject();
            Assert.AreEqual("{\"a\":\"1\",\"b\":2,\"c\":true,\"d\":null}", w.ToString());
        }

        [Test]
        public void ArrayWithMixedValues()
        {
            var w = new JsonWriter();
            w.BeginArray();
            w.WriteString("x");
            w.WriteNumber(42);
            w.WriteBool(false);
            w.WriteNull();
            w.EndArray();
            Assert.AreEqual("[\"x\",42,false,null]", w.ToString());
        }

        [Test]
        public void NestedObjectInsideArray()
        {
            var w = new JsonWriter();
            w.BeginArray();
            w.BeginObject();
            w.WriteString("k", "v");
            w.EndObject();
            w.BeginObject();
            w.WriteNumber("n", 1);
            w.EndObject();
            w.EndArray();
            Assert.AreEqual("[{\"k\":\"v\"},{\"n\":1}]", w.ToString());
        }

        [Test]
        public void StringEscape_QuotesAndBackslashes()
        {
            var w = new JsonWriter();
            w.BeginObject();
            w.WriteString("k", "\"quoted\" and \\backslash\\");
            w.EndObject();
            Assert.AreEqual("{\"k\":\"\\\"quoted\\\" and \\\\backslash\\\\\"}", w.ToString());
        }

        [Test]
        public void StringEscape_ControlCharacters()
        {
            var w = new JsonWriter();
            w.BeginArray();
            w.WriteString("a\nb\tc\rd\bf");
            w.EndArray();
            Assert.AreEqual("[\"a\\nb\\tc\\rd\\bf\"]", w.ToString());
        }

        [Test]
        public void StringEscape_UnicodeControlCharacters()
        {
            // U+0001 のような制御文字は \u00XX 形式。
            var w = new JsonWriter();
            w.BeginArray();
            w.WriteString("");
            w.EndArray();
            Assert.AreEqual("[\"\\u0001\\u001f\"]", w.ToString());
        }

        [Test]
        public void NumberFormat_IsInvariantCulture()
        {
            // Locale が ja-JP / fr-FR でも 3.14 になることを保証する (決して "3,14" にはならない)。
            var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
                var w = new JsonWriter();
                w.BeginObject();
                w.WriteNumber("v", 3.14);
                w.EndObject();
                Assert.IsTrue(w.ToString().Contains("3.14"), "InvariantCulture で '.' を保つ");
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = prev;
            }
        }

        [Test]
        public void NullString_WritesJsonNull()
        {
            var w = new JsonWriter();
            w.BeginObject();
            w.WriteString("k", (string)null);
            w.EndObject();
            Assert.AreEqual("{\"k\":null}", w.ToString());
        }
    }
}
