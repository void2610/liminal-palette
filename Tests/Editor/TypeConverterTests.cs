using NUnit.Framework;
using UnityEngine;

namespace Void2610.LiminalPalette.Tests
{
    public sealed class TypeConverterTests
    {
        private enum Sample { A = 0, B = 1, C = 2 }

        // 各テスト前に標準コンバータだけが入った状態に戻す。
        // テストでユーザーコンバータを Register したまま漏らしても他テストへ汚染しない。
        [SetUp]
        public void SetUp()
        {
            TypeConverterRegistry.ResetToDefaults();
        }

        [Test]
        public void Primitive_Int_Parses()
        {
            var ok = TypeConverterRegistry.TryConvert("42", typeof(int), out var v, out var err);
            Assert.IsTrue(ok, err);
            Assert.AreEqual(42, v);
        }

        [Test]
        public void Primitive_Float_UsesInvariantCulture()
        {
            var ok = TypeConverterRegistry.TryConvert("3.14", typeof(float), out var v, out var err);
            Assert.IsTrue(ok, err);
            Assert.AreEqual(3.14f, (float)v, 0.0001f);
        }

        [Test]
        public void Primitive_Bool_Parses()
        {
            Assert.IsTrue(TypeConverterRegistry.TryConvert("true", typeof(bool), out var v, out _));
            Assert.AreEqual(true, v);
        }

        [Test]
        public void Primitive_String_PassesThrough()
        {
            Assert.IsTrue(TypeConverterRegistry.TryConvert("hello", typeof(string), out var v, out _));
            Assert.AreEqual("hello", v);
        }

        [Test]
        public void Primitive_InvalidInt_ReturnsError()
        {
            var ok = TypeConverterRegistry.TryConvert("notanint", typeof(int), out _, out var err);
            Assert.IsFalse(ok);
            Assert.IsNotNull(err);
            StringAssert.Contains("notanint", err);
        }

        [Test]
        public void Enum_ParsesByName_CaseInsensitive()
        {
            Assert.IsTrue(TypeConverterRegistry.TryConvert("b", typeof(Sample), out var v, out _));
            Assert.AreEqual(Sample.B, v);
        }

        [Test]
        public void Enum_ParsesByNumericString()
        {
            Assert.IsTrue(TypeConverterRegistry.TryConvert("2", typeof(Sample), out var v, out _));
            Assert.AreEqual(Sample.C, v);
        }

        [Test]
        public void Vector3_ParsesCommaSeparated()
        {
            Assert.IsTrue(TypeConverterRegistry.TryConvert("1,2,3", typeof(Vector3), out var v, out _));
            Assert.AreEqual(new Vector3(1, 2, 3), (Vector3)v);
        }

        [Test]
        public void Vector3_ParsesParenthesesAndSpaces()
        {
            Assert.IsTrue(TypeConverterRegistry.TryConvert("(1, 2, 3)", typeof(Vector3), out var v, out _));
            Assert.AreEqual(new Vector3(1, 2, 3), (Vector3)v);
        }

        [Test]
        public void Vector3_WrongComponentCount_Errors()
        {
            Assert.IsFalse(TypeConverterRegistry.TryConvert("1,2", typeof(Vector3), out _, out var err));
            Assert.IsNotNull(err);
        }

        [Test]
        public void Color_ParsesHex()
        {
            Assert.IsTrue(TypeConverterRegistry.TryConvert("#FF8800", typeof(Color), out var v, out _));
            var c = (Color)v;
            Assert.AreEqual(1f, c.r, 0.001f);
            Assert.AreEqual(0.533f, c.g, 0.01f);
            Assert.AreEqual(0f, c.b, 0.001f);
        }

        [Test]
        public void Color_ParsesNumericTriplet_DefaultsAlphaTo1()
        {
            Assert.IsTrue(TypeConverterRegistry.TryConvert("1,0.5,0", typeof(Color), out var v, out _));
            var c = (Color)v;
            Assert.AreEqual(1f, c.a, 0.001f);
        }

        [Test]
        public void NoConverterRegisteredFor_UnknownType()
        {
            // 任意のクラス型はどの標準コンバータでも扱えないため null コンバータでエラーになる。
            var ok = TypeConverterRegistry.TryConvert("x", typeof(System.Exception), out _, out var err);
            Assert.IsFalse(ok);
            Assert.IsNotNull(err);
        }

        // 利用側 ITypeConverter による上書き検証用。
        private sealed class OverrideIntConverter : ITypeConverter
        {
            public bool CanConvert(System.Type t) => t == typeof(int);
            public string ToDisplayString(object value) => value?.ToString() ?? "";
            public bool TryFromString(string raw, System.Type t, out object value, out string error)
            {
                value = -1; error = null; return true; // 常に -1 を返す
            }
        }

        [Test]
        public void RegisterCustomConverter_OverridesBuiltin()
        {
            // [SetUp] が ResetToDefaults を呼んでいるので開始時点は標準状態。
            // 後続テストへの汚染も次の [SetUp] で除去される。
            TypeConverterRegistry.Register(new OverrideIntConverter());
            Assert.IsTrue(TypeConverterRegistry.TryConvert("42", typeof(int), out var v, out _));
            Assert.AreEqual(-1, v);
        }
    }
}
