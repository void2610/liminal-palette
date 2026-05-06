using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// 全 Assembly をスキャンし、[ConsoleCommand] を付与した static メソッドを CommandDescriptor に変換する。
    /// 起動時に Bootstrap から 1 回だけ呼ばれる想定。
    /// </summary>
    internal static class AttributeScanner
    {
        // Unity / .NET 標準アセンブリはコマンドを持ち得ないのでスキップしてスキャン時間を抑える。
        // 比較対象は asm.GetName().Name (短い名前、カンマや version 等を含まない) なのでカンマ付きエントリは置かない。
        private static readonly string[] SkipPrefixes =
        {
            "mscorlib",
            "System",      // "System" 単体 / "System.Linq" 等まで包含
            "Microsoft.",
            "UnityEngine",
            "UnityEditor",
            "Unity.",
            "Mono.",
            "nunit.",
            "netstandard",
            "Bee.",
            "ExCSS.",
            "JetBrains.",
            "log4net",
        };

        /// <summary>
        /// アセンブリ列をスキャンし、CommandDescriptor を返す。
        /// 実装ミス (ReflectionTypeLoadException 等) は握りつぶし、読めた型だけで処理を継続する。
        /// </summary>
        public static IReadOnlyList<CommandDescriptor> Scan(IEnumerable<Assembly> assemblies)
        {
            var results = new List<CommandDescriptor>();
            if (assemblies == null) return results;

            foreach (var asm in assemblies)
            {
                if (asm == null) continue;
                if (ShouldSkip(asm)) continue;

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // 一部の型がロードできなくても、読めた分だけで処理を継続する。
                    // null を含むので除外して使う。
                    types = ex.Types.Where(t => t != null).ToArray();
                }
                catch
                {
                    // それ以外のロード例外もスキップ。コマンド検出を全体停止させない。
                    continue;
                }

                for (var ti = 0; ti < types.Length; ti++)
                {
                    var type = types[ti];
                    MethodInfo[] methods;
                    try
                    {
                        // Phase 5a: インスタンスメソッドも対象に含める。
                        // インスタンス解決は CommandExecutor で IInstanceResolver 経由で行う。
                        // public のみを対象とする (commands.md の仕様: private / internal は登録されない)。
                        // 公開 API として使われる前提のため、意図しない private メソッドの公開を避ける。
                        methods = type.GetMethods(
                            BindingFlags.Public |
                            BindingFlags.Static | BindingFlags.Instance |
                            BindingFlags.DeclaredOnly);
                    }
                    catch
                    {
                        continue;
                    }

                    for (var mi = 0; mi < methods.Length; mi++)
                    {
                        var method = methods[mi];
                        ConsoleCommandAttribute attr;
                        try
                        {
                            attr = method.GetCustomAttribute<ConsoleCommandAttribute>();
                        }
                        catch
                        {
                            continue;
                        }
                        if (attr == null) continue;

                        if (!TryBuildDescriptor(method, attr, out var descriptor, out var error))
                        {
                            Debug.LogWarning($"[LiminalPalette] Skipping invalid command on {type.FullName}.{method.Name}: {error}");
                            continue;
                        }
                        results.Add(descriptor);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// MethodInfo + 属性から CommandDescriptor を構築する。
        /// パスのバリデーションと async 判定もここで行う。
        /// </summary>
        public static bool TryBuildDescriptor(
            MethodInfo method,
            ConsoleCommandAttribute attr,
            out CommandDescriptor descriptor,
            out string error)
        {
            descriptor = null;
            error = null;

            if (method == null) { error = "method is null"; return false; }
            if (attr == null) { error = "attribute is null"; return false; }

            var path = attr.Path;
            if (!ValidatePath(path, out var pathError))
            {
                error = pathError;
                return false;
            }

            // パラメータメタデータ。ref/out は Phase 1 では弾く (UI / CLI 経由で扱えないため)。
            var paramInfos = method.GetParameters();
            var parameters = new ParameterDescriptor[paramInfos.Length];
            for (var i = 0; i < paramInfos.Length; i++)
            {
                var pi = paramInfos[i];
                if (pi.ParameterType.IsByRef)
                {
                    error = $"parameter '{pi.Name}' uses ref/out which is not supported";
                    return false;
                }

                var paramAttr = pi.GetCustomAttribute<ConsoleParamAttribute>();
                var description = paramAttr != null ? paramAttr.Description : "";
                var choices = paramAttr != null ? paramAttr.Choices : Array.Empty<string>();
                // Min/Max は float.NaN を「未指定」の Sentinel に使う (属性側のデフォルトと揃える)。
                var min = paramAttr != null ? paramAttr.Min : float.NaN;
                var max = paramAttr != null ? paramAttr.Max : float.NaN;

                parameters[i] = new ParameterDescriptor(
                    pi.Name,
                    pi.ParameterType,
                    pi.Position,
                    pi.HasDefaultValue,
                    pi.HasDefaultValue ? pi.DefaultValue : null,
                    description,
                    choices,
                    min: min,
                    max: max);
            }

            var isAsync = IsAsyncReturn(method.ReturnType);

            descriptor = new CommandDescriptor(
                path: path,
                description: attr.Description,
                aliases: NormalizeAliases(attr.Aliases),
                parameters: parameters,
                returnType: method.ReturnType,
                isAsync: isAsync,
                method: method);
            return true;
        }

        // 戻り値が Task / Task<T> / ValueTask / ValueTask<T> なら true。UniTask は Phase 2 以降で対応。
        private static bool IsAsyncReturn(Type t)
        {
            if (t == null) return false;
            if (t == typeof(Task) || t == typeof(ValueTask)) return true;
            if (!t.IsGenericType) return false;
            var def = t.GetGenericTypeDefinition();
            return def == typeof(Task<>) || def == typeof(ValueTask<>);
        }

        // パスは "/" 区切りの非空セグメント列でなければならない。
        private static bool ValidatePath(string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path))
            {
                error = "path is null or empty";
                return false;
            }
            if (path.StartsWith("/") || path.EndsWith("/"))
            {
                error = $"path '{path}' must not start or end with '/'";
                return false;
            }
            // "Foo//Bar" のような空セグメントを禁止。
            var segs = path.Split('/');
            for (var i = 0; i < segs.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(segs[i]))
                {
                    error = $"path '{path}' contains empty segment";
                    return false;
                }
            }
            return true;
        }

        // null や空エイリアスを除去した不変リストを返す。
        private static IReadOnlyList<string> NormalizeAliases(string[] aliases)
        {
            if (aliases == null || aliases.Length == 0) return Array.Empty<string>();
            var list = new List<string>(aliases.Length);
            for (var i = 0; i < aliases.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(aliases[i])) list.Add(aliases[i]);
            }
            return list;
        }

        private static bool ShouldSkip(Assembly asm)
        {
            var name = asm.GetName().Name ?? "";
            for (var i = 0; i < SkipPrefixes.Length; i++)
            {
                if (name.StartsWith(SkipPrefixes[i], StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
