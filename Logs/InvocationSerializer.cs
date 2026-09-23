using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// 手動実行の記録を 1 本の文字列に詰める / 戻す。
    ///
    /// JSON を使わないのは、Core から Ipc の JsonWriter を参照できない (依存が逆) ため。
    /// <see cref="EditorCommandHistory"/> と同じく制御文字区切りのフラット形式にする。
    ///
    /// 保存するのは path / 引数 / 時刻 / 成否 / 所要時間 / エラー文だけ。
    /// ログ本文とスタックトレースは肥大するので捨てる (再実行に要らない)。
    /// </summary>
    internal static class InvocationSerializer
    {
        // レコード区切り / フィールド区切り / 引数ペア区切り / キーと値の区切り
        private const char RecordSep = '\u001e';
        private const char FieldSep = '\u001f';
        private const char PairSep = '\u0002';
        private const char KeyValueSep = '\u0003';

        /// <summary>直列化する。失敗しうる値は文字列化して落とす。</summary>
        internal static string Serialize(IEnumerable<CommandInvocation> entries)
        {
            var sb = new StringBuilder();
            foreach (var e in entries)
            {
                if (sb.Length > 0) sb.Append(RecordSep);
                sb.Append(Sanitize(e.Path)).Append(FieldSep);
                sb.Append(e.TimestampUtc.Ticks.ToString(CultureInfo.InvariantCulture)).Append(FieldSep);
                sb.Append(e.Result != null && e.Result.Success ? '1' : '0').Append(FieldSep);
                sb.Append((e.Result?.Duration.TotalMilliseconds ?? 0.0).ToString("R", CultureInfo.InvariantCulture)).Append(FieldSep);
                sb.Append(Sanitize(e.Result?.Error)).Append(FieldSep);
                sb.Append(SerializeArgs(e.Args));
            }
            return sb.ToString();
        }

        /// <summary>復元する。壊れたレコードは黙って捨てる (履歴のために起動を止めない)。</summary>
        internal static List<CommandInvocation> Deserialize(string raw)
        {
            var list = new List<CommandInvocation>();
            if (string.IsNullOrEmpty(raw)) return list;

            foreach (var record in raw.Split(RecordSep))
            {
                if (record.Length == 0) continue;
                var f = record.Split(FieldSep);
                if (f.Length < 6) continue;
                if (!long.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)) continue;
                if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) continue;

                double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var durationMs);
                var success = f[2] == "1";
                var error = string.IsNullOrEmpty(f[4]) ? null : f[4];
                var result = success
                    ? CommandResult.Ok(null, Array.Empty<LogEntry>(), TimeSpan.FromMilliseconds(durationMs))
                    : CommandResult.Fail(error ?? "", null, Array.Empty<LogEntry>(), TimeSpan.FromMilliseconds(durationMs));

                list.Add(new CommandInvocation(
                    f[0],
                    DeserializeArgs(f[5]),
                    result,
                    new DateTime(ticks, DateTimeKind.Utc),
                    InvocationOrigin.User,
                    restored: true));
            }
            return list;
        }

        private static string SerializeArgs(IReadOnlyDictionary<string, object> args)
        {
            if (args == null || args.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var kv in args)
            {
                if (sb.Length > 0) sb.Append(PairSep);
                sb.Append(Sanitize(kv.Key)).Append(KeyValueSep);
                // 値は表示用文字列で保存する。再実行は文字列経路 (TypeConverter) を通すため、
                // Vector3 / Color / UnityEngine.Object もこの形から復元できる。
                sb.Append(Sanitize(kv.Value == null ? "" : TypeConverterRegistry.ToDisplayString(kv.Value)));
            }
            return sb.ToString();
        }

        private static Dictionary<string, object> DeserializeArgs(string raw)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(raw)) return dict;
            foreach (var pair in raw.Split(PairSep))
            {
                if (pair.Length == 0) continue;
                var i = pair.IndexOf(KeyValueSep);
                if (i < 0) continue;
                dict[pair.Substring(0, i)] = pair.Substring(i + 1);
            }
            return dict;
        }

        // 区切り文字が値に混ざると復元できなくなるので空白へ潰す。
        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace(RecordSep, ' ').Replace(FieldSep, ' ')
                    .Replace(PairSep, ' ').Replace(KeyValueSep, ' ');
        }
    }
}
