using System;
using System.Globalization;
using Void2610.LiminalPalette;
using Void2610.LiminalPalette.UI;

namespace Void2610.LiminalPalette.Ipc.Json
{
    /// <summary>
    /// 各 endpoint のレスポンス JSON を組み立てるための DTO 変換ヘルパ。
    /// LiminalPalette 内部モデル (CommandDescriptor / CommandResult / LogEntry / CommandInvocation) を
    /// JsonWriter に書き出すルールを 1 箇所に集約する。
    ///
    /// 設計方針:
    ///   - Exception オブジェクトは JSON に含めない (プロセス境界を超える object を送らない)。
    ///     代わりに ExceptionType (FullName) と StackTrace を string として出す。
    ///   - CommandResult.Value は TypeConverterRegistry.ToDisplayString で文字列化。
    ///     Vector / Color などはユーザーフレンドリーに表示できる。
    ///   - CommandInvocation.Args は string→string にダウンキャスト (再実行できる形式に揃える)。
    ///   - DateTime は ISO 8601 (UTC, ミリ秒精度) で出力。
    /// </summary>
    public static class IpcContracts
    {
        public static void WriteCommand(JsonWriter w, CommandDescriptor cmd)
        {
            w.BeginObject();
            w.WriteString("path", cmd.Path);
            w.WriteString("name", cmd.Name);
            w.WriteString("category", cmd.Category);
            w.WriteString("description", cmd.Description);
            w.WriteBool("isAsync", cmd.IsAsync);
            w.WriteString("returnType", cmd.ReturnType?.Name ?? "void");

            w.BeginArray("aliases");
            for (var i = 0; i < cmd.Aliases.Count; i++) w.WriteString(cmd.Aliases[i]);
            w.EndArray();

            w.BeginArray("parameters");
            for (var i = 0; i < cmd.Parameters.Count; i++) WriteParameter(w, cmd.Parameters[i]);
            w.EndArray();

            w.EndObject();
        }

        public static void WriteParameter(JsonWriter w, ParameterDescriptor p)
        {
            w.BeginObject();
            w.WriteString("name", p.Name);
            w.WriteString("type", p.Type?.Name ?? "");
            w.WriteNumber("position", p.Position);
            w.WriteBool("hasDefault", p.HasDefault);
            if (p.HasDefault)
            {
                w.WriteString("default", p.DefaultValue == null
                    ? null
                    : TypeConverterRegistry.ToDisplayString(p.DefaultValue));
            }
            else
            {
                w.WriteNull("default");
            }
            w.WriteString("description", p.Description ?? "");
            // 動的候補がある場合は {value, displayName} オブジェクト配列で出力
            if (p.DynamicChoices != null)
            {
                System.Collections.Generic.IReadOnlyList<ChoiceItem> items;
                try { items = p.DynamicChoices(); }
                catch { items = System.Array.Empty<ChoiceItem>(); }
                w.BeginArray("choices");
                for (var i = 0; i < items.Count; i++)
                {
                    w.BeginObject();
                    w.WriteString("value", items[i].Value);
                    w.WriteString("displayName", items[i].DisplayName);
                    w.EndObject();
                }
                w.EndArray();
            }
            else
            {
                w.BeginArray("choices");
                for (var i = 0; i < p.Choices.Count; i++) w.WriteString(p.Choices[i]);
                w.EndArray();
            }
            // Min / Max は LiminalParam.Min/Max 由来。float.NaN は「未指定」の Sentinel なので
            // JSON null を出力する (NaN は JSON 標準で表現できないため代替)。
            if (float.IsNaN(p.Min)) w.WriteNull("min");
            else w.WriteNumber("min", p.Min);
            if (float.IsNaN(p.Max)) w.WriteNull("max");
            else w.WriteNumber("max", p.Max);
            w.EndObject();
        }

        public static void WriteResult(JsonWriter w, CommandResult r)
        {
            w.BeginObject();
            w.WriteBool("success", r.Success);
            // Value は ToDisplayString で文字列化。null は JSON null。
            if (r.Value == null) w.WriteNull("value");
            else w.WriteString("value", TypeConverterRegistry.ToDisplayString(r.Value));

            if (r.Error == null) w.WriteNull("error"); else w.WriteString("error", r.Error);
            w.WriteString("exceptionType", r.Exception?.GetType().FullName);
            w.WriteString("stackTrace", r.Exception?.StackTrace);
            w.WriteNumber("durationMs", r.Duration.TotalMilliseconds);

            w.BeginArray("logs");
            for (var i = 0; i < r.Logs.Count; i++) WriteLog(w, r.Logs[i]);
            w.EndArray();

            w.EndObject();
        }

        public static void WriteLog(JsonWriter w, LogEntry log)
        {
            w.BeginObject();
            w.WriteString("type", log.Type.ToString());
            w.WriteString("message", log.Message);
            w.WriteString("stackTrace", log.StackTrace);
            w.WriteString("timestamp", FormatIso8601(log.TimestampUtc));
            w.EndObject();
        }

        public static void WriteInvocation(JsonWriter w, CommandInvocation inv)
        {
            w.BeginObject();
            w.WriteString("path", inv.Path);
            w.WriteString("timestamp", FormatIso8601(inv.TimestampUtc));

            // args (object: name → ToDisplayString された値)
            w.BeginObject("args");
            foreach (var kv in inv.Args)
            {
                w.WriteString(kv.Key, kv.Value == null
                    ? null
                    : TypeConverterRegistry.ToDisplayString(kv.Value));
            }
            w.EndObject();

            // result
            w.BeginObject("result");
            // result の中身は WriteResult を再現する形で展開する (object 入れ子の都合上)。
            WriteResultBody(w, inv.Result);
            w.EndObject();

            w.EndObject();
        }

        // WriteResult の本体 (BeginObject/EndObject を呼ばない版)。WriteInvocation で result を入れ子にするのに使う。
        private static void WriteResultBody(JsonWriter w, CommandResult r)
        {
            w.WriteBool("success", r.Success);
            if (r.Value == null) w.WriteNull("value");
            else w.WriteString("value", TypeConverterRegistry.ToDisplayString(r.Value));
            if (r.Error == null) w.WriteNull("error"); else w.WriteString("error", r.Error);
            w.WriteString("exceptionType", r.Exception?.GetType().FullName);
            w.WriteString("stackTrace", r.Exception?.StackTrace);
            w.WriteNumber("durationMs", r.Duration.TotalMilliseconds);
            w.BeginArray("logs");
            for (var i = 0; i < r.Logs.Count; i++) WriteLog(w, r.Logs[i]);
            w.EndArray();
        }

        private static string FormatIso8601(DateTime utc)
            => utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        // ---- Scenarios (Phase 5b) ----

        /// <summary>シナリオ 1 件をリスト用 JSON として書き出す。</summary>
        public static void WriteScenario(JsonWriter w, ScenarioDescriptor s, int stepCount)
        {
            w.BeginObject();
            w.WriteString("path", s.Path);
            w.WriteString("description", s.Description ?? "");
            w.WriteNumber("stepCount", stepCount);
            w.EndObject();
        }

        /// <summary>シナリオ実行結果を JSON として書き出す。</summary>
        public static void WriteScenarioResult(JsonWriter w, ScenarioResult r)
        {
            w.BeginObject();
            w.WriteBool("success", r.Success);
            w.WriteNumber("durationMs", r.Duration.TotalMilliseconds);
            w.WriteNumber("failedAtStep", r.FailedAtStep);
            if (r.Path == null) w.WriteNull("path");
            else w.WriteString("path", r.Path);
            w.WriteBool("alreadyRunning", r.WasRejectedAsAlreadyRunning);

            w.BeginArray("steps");
            for (var i = 0; i < r.Steps.Count; i++) WriteStepResult(w, r.Steps[i]);
            w.EndArray();
            w.EndObject();
        }

        public static void WriteStepResult(JsonWriter w, StepResult s)
        {
            w.BeginObject();
            w.WriteString("kind", s.Step?.Kind.ToString() ?? "Unknown");
            w.WriteString("description", s.Step?.Description ?? "");
            w.WriteBool("success", s.Success);
            w.WriteNumber("durationMs", s.Duration.TotalMilliseconds);
            if (s.Error == null) w.WriteNull("error"); else w.WriteString("error", s.Error);

            // ステップ種別ごとの詳細を入れる。
            if (s.Step is CommandStep cs)
            {
                w.WriteString("commandPath", cs.CommandPath);
                w.BeginObject("args");
                if (cs.Args != null)
                {
                    foreach (var kv in cs.Args)
                    {
                        w.WriteString(kv.Key, kv.Value == null
                            ? null
                            : TypeConverterRegistry.ToDisplayString(kv.Value));
                    }
                }
                w.EndObject();
            }
            else if (s.Step is WaitStep ws)
            {
                if (ws.Kind == ScenarioStepKind.WaitSeconds)
                    w.WriteNumber("seconds", ws.Seconds);
                else
                    w.WriteNumber("frames", ws.Frames);
            }
            else if (s.Step is AssertStep asr)
            {
                w.WriteString("observableFieldPath", asr.ObservableFieldPath);
                w.WriteString("expected", asr.Expected == null ? null : TypeConverterRegistry.ToDisplayString(asr.Expected));
            }

            // CommandResult (Command ステップのみ)。
            if (s.CommandResult != null)
            {
                w.BeginObject("commandResult");
                w.WriteBool("success", s.CommandResult.Success);
                if (s.CommandResult.Value == null) w.WriteNull("value");
                else w.WriteString("value", TypeConverterRegistry.ToDisplayString(s.CommandResult.Value));
                if (s.CommandResult.Error == null) w.WriteNull("error");
                else w.WriteString("error", s.CommandResult.Error);
                w.WriteNumber("durationMs", s.CommandResult.Duration.TotalMilliseconds);
                w.BeginArray("logs");
                for (var i = 0; i < s.CommandResult.Logs.Count; i++) WriteLog(w, s.CommandResult.Logs[i]);
                w.EndArray();
                w.EndObject();
            }

            // ActualValue (Assert ステップのみ)。
            if (s.ActualValue != null)
            {
                w.WriteString("actualValue", TypeConverterRegistry.ToDisplayString(s.ActualValue));
            }

            w.EndObject();
        }
    }
}
