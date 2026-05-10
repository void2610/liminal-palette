using System.Collections.Generic;
using NUnit.Framework;

namespace Void2610.LiminalPalette.Tests
{
    public sealed class ArgumentBinderTests
    {
        [Test]
        public void Named_AllProvided_BindsCorrectly()
        {
            var parameters = new[]
            {
                Param("a", typeof(int), 0),
                Param("b", typeof(string), 1)
            };
            var args = new Dictionary<string, string> { ["a"] = "5", ["b"] = "hi" };
            var ok = ArgumentBinder.TryBind(parameters, args, out var bound, out var err);
            Assert.IsTrue(ok, err);
            Assert.AreEqual(5, bound[0]);
            Assert.AreEqual("hi", bound[1]);
        }

        [Test]
        public void Named_DefaultUsed_WhenMissing()
        {
            var parameters = new[]
            {
                Param("a", typeof(int), 0),
                Param("b", typeof(int), 1, hasDefault: true, defaultValue: 99)
            };
            var args = new Dictionary<string, string> { ["a"] = "1" };
            var ok = ArgumentBinder.TryBind(parameters, args, out var bound, out var err);
            Assert.IsTrue(ok, err);
            Assert.AreEqual(99, bound[1]);
        }

        [Test]
        public void Named_MissingRequired_Errors()
        {
            var parameters = new[] { Param("a", typeof(int), 0) };
            var args = new Dictionary<string, string>();
            var ok = ArgumentBinder.TryBind(parameters, args, out _, out var err);
            Assert.IsFalse(ok);
            StringAssert.Contains("a", err);
        }

        [Test]
        public void Named_TypeMismatch_ErrorContainsParameterName()
        {
            var parameters = new[] { Param("count", typeof(int), 0) };
            var args = new Dictionary<string, string> { ["count"] = "not-a-number" };
            var ok = ArgumentBinder.TryBind(parameters, args, out _, out var err);
            Assert.IsFalse(ok);
            StringAssert.Contains("count", err);
        }

        [Test]
        public void Named_KeyIsCaseInsensitive()
        {
            var parameters = new[] { Param("Value", typeof(int), 0) };
            var args = new Dictionary<string, string> { ["value"] = "7" };
            var ok = ArgumentBinder.TryBind(parameters, args, out var bound, out var err);
            Assert.IsTrue(ok, err);
            Assert.AreEqual(7, bound[0]);
        }

        [Test]
        public void Positional_BindsByIndex()
        {
            var parameters = new[]
            {
                Param("a", typeof(int), 0),
                Param("b", typeof(int), 1)
            };
            var ok = ArgumentBinder.TryBind(parameters, new[] { "1", "2" }, out var bound, out var err);
            Assert.IsTrue(ok, err);
            Assert.AreEqual(1, bound[0]);
            Assert.AreEqual(2, bound[1]);
        }

        [Test]
        public void Positional_FillsDefaultsWhenShort()
        {
            var parameters = new[]
            {
                Param("a", typeof(int), 0),
                Param("b", typeof(int), 1, hasDefault: true, defaultValue: 42)
            };
            var ok = ArgumentBinder.TryBind(parameters, new[] { "1" }, out var bound, out var err);
            Assert.IsTrue(ok, err);
            Assert.AreEqual(42, bound[1]);
        }

        [Test]
        public void Positional_TooMany_Errors()
        {
            var parameters = new[] { Param("a", typeof(int), 0) };
            var ok = ArgumentBinder.TryBind(parameters, new[] { "1", "2" }, out _, out var err);
            Assert.IsFalse(ok);
            Assert.IsNotNull(err);
        }

        // ---- LiminalParam.Min / Max による範囲バリデーション ----

        [Test]
        public void Range_Min_BelowMin_Errors()
        {
            var parameters = new[] { Param("amount", typeof(int), 0, min: 1f) };
            var ok = ArgumentBinder.TryBind(parameters, new Dictionary<string, string> { ["amount"] = "0" }, out _, out var err);
            Assert.IsFalse(ok);
            StringAssert.Contains("amount", err);
            StringAssert.Contains("min", err);
        }

        [Test]
        public void Range_Min_AtBoundary_Ok()
        {
            var parameters = new[] { Param("amount", typeof(int), 0, min: 1f) };
            var ok = ArgumentBinder.TryBind(parameters, new Dictionary<string, string> { ["amount"] = "1" }, out var bound, out var err);
            Assert.IsTrue(ok, err);
            Assert.AreEqual(1, bound[0]);
        }

        [Test]
        public void Range_Max_AboveMax_Errors()
        {
            var parameters = new[] { Param("level", typeof(int), 0, max: 100f) };
            var ok = ArgumentBinder.TryBind(parameters, new Dictionary<string, string> { ["level"] = "101" }, out _, out var err);
            Assert.IsFalse(ok);
            StringAssert.Contains("max", err);
        }

        [Test]
        public void Range_MinAndMax_OutOfBoth_Errors()
        {
            var parameters = new[] { Param("v", typeof(int), 0, min: 0f, max: 10f) };
            Assert.IsFalse(ArgumentBinder.TryBind(parameters, new Dictionary<string, string> { ["v"] = "-1" }, out _, out _));
            Assert.IsFalse(ArgumentBinder.TryBind(parameters, new Dictionary<string, string> { ["v"] = "11" }, out _, out _));
            Assert.IsTrue(ArgumentBinder.TryBind(parameters, new Dictionary<string, string> { ["v"] = "5" }, out _, out _));
        }

        [Test]
        public void Range_FloatParameter_Validated()
        {
            var parameters = new[] { Param("scale", typeof(float), 0, min: 0f, max: 1f) };
            Assert.IsFalse(ArgumentBinder.TryBind(parameters, new Dictionary<string, string> { ["scale"] = "1.5" }, out _, out _));
            Assert.IsTrue(ArgumentBinder.TryBind(parameters, new Dictionary<string, string> { ["scale"] = "0.5" }, out _, out _));
        }

        [Test]
        public void Range_NonNumericType_IsIgnored()
        {
            // string に Min/Max を付けても黙って通す (UI ヒント等で誤指定されても落ちない)。
            var parameters = new[] { Param("name", typeof(string), 0, min: 1f, max: 10f) };
            var ok = ArgumentBinder.TryBind(parameters, new Dictionary<string, string> { ["name"] = "hello" }, out var bound, out var err);
            Assert.IsTrue(ok, err);
            Assert.AreEqual("hello", bound[0]);
        }

        [Test]
        public void Range_DefaultValue_NotValidated()
        {
            // 引数を省略してデフォルト値が使われるケースは範囲検証対象外 (デフォルト値の妥当性は定義者の責任)。
            // デフォルトを意図的に Min より小さい値にしてもエラーにならないことを確認。
            var parameters = new[] { Param("amount", typeof(int), 0, hasDefault: true, defaultValue: 0, min: 1f) };
            var ok = ArgumentBinder.TryBind(parameters, new Dictionary<string, string>(), out var bound, out var err);
            Assert.IsTrue(ok, err);
            Assert.AreEqual(0, bound[0]);
        }

        [Test]
        public void Range_PositionalBinding_Validated()
        {
            var parameters = new[] { Param("amount", typeof(int), 0, min: 1f) };
            Assert.IsFalse(ArgumentBinder.TryBind(parameters, new[] { "0" }, out _, out var err));
            StringAssert.Contains("min", err);
        }

        [Test]
        public void Range_TypedBinding_Validated()
        {
            var parameters = new[] { Param("amount", typeof(int), 0, min: 1f) };
            var ok = ArgumentBinder.TryBindTyped(parameters, new Dictionary<string, object> { ["amount"] = 0 }, out _, out var err);
            Assert.IsFalse(ok);
            StringAssert.Contains("min", err);
        }

        [Test]
        public void Typed_NullableInt_AcceptsBoxedInt()
        {
            // int? パラメータに boxed int (実体型 int) を渡す。
            // 旧実装は p.Type.IsInstanceOfType(0) が false で弾いていたが、
            // Nullable.GetUnderlyingType を考慮して許容するように修正済み。
            var parameters = new[] { Param("count", typeof(int?), 0) };
            var ok = ArgumentBinder.TryBindTyped(parameters, new Dictionary<string, object> { ["count"] = 5 }, out var bound, out var err);
            Assert.IsTrue(ok, err);
            Assert.AreEqual(5, bound[0]);
        }

        [Test]
        public void Typed_NullableInt_AcceptsNull()
        {
            // null も Nullable<T> なら受け付ける (既存の null 処理の回帰確認)。
            var parameters = new[] { Param("count", typeof(int?), 0) };
            var ok = ArgumentBinder.TryBindTyped(parameters, new Dictionary<string, object> { ["count"] = null }, out var bound, out var err);
            Assert.IsTrue(ok, err);
            Assert.IsNull(bound[0]);
        }

        [Test]
        public void Typed_NullableInt_RangeValidated()
        {
            // Nullable<T> に Min/Max を付けた場合でも、boxed 値経由で範囲検証が効く。
            var parameters = new[] { Param("count", typeof(int?), 0, min: 1f) };
            Assert.IsFalse(ArgumentBinder.TryBindTyped(parameters, new Dictionary<string, object> { ["count"] = 0 }, out _, out _));
            Assert.IsTrue(ArgumentBinder.TryBindTyped(parameters, new Dictionary<string, object> { ["count"] = 1 }, out _, out _));
        }

        [Test]
        public void Range_ErrorMessage_UsesInvariantCulture()
        {
            // CurrentCulture を de-DE (小数点が ',') に切り替えてもエラーメッセージは
            // InvariantCulture で format されるため、'.' を含むことを確認する。
            var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                var parameters = new[] { Param("scale", typeof(float), 0, min: 1f) };
                Assert.IsFalse(ArgumentBinder.TryBind(parameters, new Dictionary<string, string> { ["scale"] = "0.5" }, out _, out var err));
                StringAssert.Contains("0.5", err);
                StringAssert.DoesNotContain("0,5", err);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = prev;
            }
        }

        // 検証専用に最小の ParameterDescriptor を作るユーティリティ。
        private static ParameterDescriptor Param(string name, System.Type type, int pos,
            bool hasDefault = false, object defaultValue = null,
            float min = float.NaN, float max = float.NaN)
        {
            return new ParameterDescriptor(name, type, pos, hasDefault, defaultValue, "", System.Array.Empty<string>(), min, max);
        }
    }
}
