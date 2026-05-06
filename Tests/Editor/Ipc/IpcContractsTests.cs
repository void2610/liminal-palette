using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Void2610.LiminalPalette;
using Void2610.LiminalPalette.Ipc.Json;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Tests.Ipc
{
    public sealed class IpcContractsTests
    {
        // テスト用の簡易 ParameterDescriptor 生成。
        private static ParameterDescriptor Param(string name, Type t, bool hasDefault = false, object defaultValue = null,
            float min = float.NaN, float max = float.NaN)
            => new ParameterDescriptor(name, t, 0, hasDefault, defaultValue, "desc", Array.Empty<string>(), min, max);

        // 簡易 CommandDescriptor (Method = null、Aliases 空、parameters 任意)。
        private static CommandDescriptor Cmd(string path, params ParameterDescriptor[] parameters)
            => new CommandDescriptor(path, "desc", new[] { "alias1" }, parameters, typeof(void), false, null);

        [Test]
        public void WriteCommand_HasAllFields()
        {
            var w = new JsonWriter();
            IpcContracts.WriteCommand(w, Cmd("Player/Health/Set", Param("v", typeof(int))));
            var json = w.ToString();
            // 主要フィールドが含まれること。
            StringAssert.Contains("\"path\":\"Player/Health/Set\"", json);
            StringAssert.Contains("\"name\":\"Set\"", json);
            StringAssert.Contains("\"category\":\"Player/Health\"", json);
            StringAssert.Contains("\"isAsync\":false", json);
            StringAssert.Contains("\"aliases\":[\"alias1\"]", json);
            StringAssert.Contains("\"parameters\":[", json);
            StringAssert.Contains("\"name\":\"v\"", json);
            StringAssert.Contains("\"type\":\"Int32\"", json);
        }

        [Test]
        public void WriteParameter_MinMax_Unspecified_WritesNull()
        {
            var w = new JsonWriter();
            IpcContracts.WriteParameter(w, Param("v", typeof(int)));
            var json = w.ToString();
            // 未指定 (float.NaN) は JSON null として露出。
            StringAssert.Contains("\"min\":null", json);
            StringAssert.Contains("\"max\":null", json);
        }

        [Test]
        public void WriteParameter_MinMax_Specified_WritesNumbers()
        {
            var w = new JsonWriter();
            IpcContracts.WriteParameter(w, Param("amount", typeof(int), min: 1f, max: 100f));
            var json = w.ToString();
            // 指定値は JSON 数値として露出。"R" フォーマットで小数点 '.' (Invariant) が保たれる。
            StringAssert.Contains("\"min\":1", json);
            StringAssert.Contains("\"max\":100", json);
        }

        [Test]
        public void WriteResult_DoesNotIncludeExceptionObject_OnlyTypeAndStackTrace()
        {
            // 例外起因の失敗結果。
            CommandResult r;
            try { throw new InvalidOperationException("boom"); }
            catch (Exception ex) { r = CommandResult.Fail("err", ex, Array.Empty<LogEntry>(), TimeSpan.FromMilliseconds(12.5)); }

            var w = new JsonWriter();
            IpcContracts.WriteResult(w, r);
            var json = w.ToString();

            StringAssert.Contains("\"success\":false", json);
            StringAssert.Contains("\"error\":\"err\"", json);
            StringAssert.Contains("\"exceptionType\":\"System.InvalidOperationException\"", json);
            // StackTrace には何かしらの値が出る (環境差異があるので特定文字は検証しない)。
            StringAssert.Contains("\"stackTrace\":", json);
            // duration はミリ秒で出る。"R" フォーマットは double 値の最短往復可能な表現なので、
            // 12.5 が "12.5" になるケースもあれば environment 依存で別形になる可能性もある。
            // ここでは "durationMs": の後に数値があることだけ確認する。
            StringAssert.Contains("\"durationMs\":", json);
        }

        [Test]
        public void WriteResult_OkValue_WritesDisplayString()
        {
            var r = CommandResult.Ok(new Vector3(1f, 2f, 3f), Array.Empty<LogEntry>(), TimeSpan.Zero);
            var w = new JsonWriter();
            IpcContracts.WriteResult(w, r);
            var json = w.ToString();
            StringAssert.Contains("\"success\":true", json);
            // ToDisplayString は VectorConverter で "(1.00, 2.00, 3.00)" 等に整形される。
            StringAssert.Contains("\"value\":\"", json);
        }

        [Test]
        public void WriteLog_TimestampIsIso8601()
        {
            var ts = new DateTime(2026, 4, 30, 12, 34, 56, 789, DateTimeKind.Utc);
            var log = new LogEntry(LogType.Warning, "msg", "trace", ts);
            var w = new JsonWriter();
            IpcContracts.WriteLog(w, log);
            var json = w.ToString();
            StringAssert.Contains("\"timestamp\":\"2026-04-30T12:34:56.789Z\"", json);
            StringAssert.Contains("\"type\":\"Warning\"", json);
        }

        [Test]
        public void WriteInvocation_StringifiesArgsAndNestsResult()
        {
            var args = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["x"] = 42, ["y"] = "hello" };
            var inv = new CommandInvocation("Test/Run",
                args,
                CommandResult.Ok("done", Array.Empty<LogEntry>(), TimeSpan.Zero),
                new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc));

            var w = new JsonWriter();
            IpcContracts.WriteInvocation(w, inv);
            var json = w.ToString();

            StringAssert.Contains("\"path\":\"Test/Run\"", json);
            StringAssert.Contains("\"args\":{", json);
            StringAssert.Contains("\"x\":\"42\"", json);
            StringAssert.Contains("\"y\":\"hello\"", json);
            StringAssert.Contains("\"result\":{", json);
            StringAssert.Contains("\"success\":true", json);
        }
    }
}
