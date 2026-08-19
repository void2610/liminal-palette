using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// 全 Assembly をスキャンし、[LiminalScenario] を付与したメソッドを ScenarioDescriptor に変換する。
    /// 起動時に Bootstrap から 1 回だけ呼ばれる想定。
    ///
    /// 受け入れる戻り値型:
    ///   - IEnumerable&lt;ScenarioStep&gt;
    ///   - その他 (IList&lt;ScenarioStep&gt; / ScenarioStep[] など) でも IEnumerable&lt;ScenarioStep&gt; に
    ///     キャストできれば許容。
    ///
    /// 引数を持つメソッドは現状未対応 (将来 Phase で対応する余地を残す)。
    /// </summary>
    public static class ScenarioScanner
    {
        // CommandRegistry の AttributeScanner と揃える。
        private static readonly string[] SkipPrefixes =
        {
            "mscorlib",
            "System",
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

        /// <summary>全 Assembly をスキャンして ScenarioRegistry.Default に登録する。</summary>
        public static void ScanAll()
        {
            ScanAll(AppDomain.CurrentDomain.GetAssemblies());
        }

        /// <summary>
        /// 指定したアセンブリ列のみをスキャンして ScenarioRegistry.Default に登録する。
        /// Bootstrap でテストアセンブリを除外する用途に使う。
        /// </summary>
        public static void ScanAll(IEnumerable<Assembly> assemblies)
        {
            var descriptors = Scan(assemblies);
            for (var i = 0; i < descriptors.Count; i++)
            {
                ScenarioRegistry.Default.Register(descriptors[i]);
            }
        }

        /// <summary>アセンブリ列をスキャンし、ScenarioDescriptor のリストを返す (テスト向け公開エントリ)。</summary>
        public static IReadOnlyList<ScenarioDescriptor> Scan(IEnumerable<Assembly> assemblies)
        {
            var results = new List<ScenarioDescriptor>();
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
                    types = ex.Types.Where(t => t != null).ToArray();
                }
                catch
                {
                    continue;
                }

                for (var ti = 0; ti < types.Length; ti++)
                {
                    var type = types[ti];
                    MethodInfo[] methods;
                    try
                    {
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
                        LiminalScenarioAttribute attr;
                        try
                        {
                            attr = method.GetCustomAttribute<LiminalScenarioAttribute>();
                        }
                        catch
                        {
                            continue;
                        }
                        if (attr == null) continue;

                        if (!TryBuildDescriptor(method, attr, out var descriptor, out var error))
                        {
                            Debug.LogWarning($"[LiminalPalette] Skipping invalid scenario on {type.FullName}.{method.Name}: {error}");
                            continue;
                        }
                        results.Add(descriptor);
                    }
                }
            }

            return results;
        }

        /// <summary>MethodInfo + 属性から ScenarioDescriptor を構築する。テスト用に公開。</summary>
        public static bool TryBuildDescriptor(
            MethodInfo method,
            LiminalScenarioAttribute attr,
            out ScenarioDescriptor descriptor,
            out string error)
        {
            descriptor = null;
            error = null;

            if (method == null) { error = "method is null"; return false; }
            if (attr == null) { error = "attribute is null"; return false; }

            if (!ValidatePath(attr.Path, out var pathError))
            {
                error = pathError;
                return false;
            }

            // 引数を取るメソッドはサポート外 (yield return ベースの設計では引数の扱いが曖昧になる)。
            if (method.GetParameters().Length != 0)
            {
                error = "scenario method must have no parameters";
                return false;
            }

            // 戻り値が IEnumerable<ScenarioStep> として扱えることを確認する。
            // IEnumerable<ScenarioStep> 自体、もしくはそれを実装する型 (List<ScenarioStep> 等) を許容。
            if (!IsScenarioStepEnumerable(method.ReturnType))
            {
                error = $"scenario method must return IEnumerable<ScenarioStep> (got {method.ReturnType?.Name ?? "<null>"})";
                return false;
            }

            var declaringType = method.DeclaringType;
            var isStatic = method.IsStatic;

            // ファクトリは「インスタンスを受け取って IEnumerable<ScenarioStep> を返す」関数として組む。
            // static の場合は instance 引数を無視。
            // method.Invoke が返した object は IEnumerable<ScenarioStep> 互換のはずだが、
            // 古い IEnumerable のみ実装するケースに備えて Cast<ScenarioStep>() でフォールバック。
            Func<object, IEnumerable<ScenarioStep>> factory = instance =>
            {
                var raw = method.Invoke(isStatic ? null : instance, Array.Empty<object>());
                if (raw == null) return Array.Empty<ScenarioStep>();
                if (raw is IEnumerable<ScenarioStep> typed) return typed;
                if (raw is IEnumerable nonTyped) return nonTyped.Cast<ScenarioStep>();
                return Array.Empty<ScenarioStep>();
            };

            descriptor = new ScenarioDescriptor(
                path: attr.Path,
                description: attr.Description,
                declaringType: declaringType,
                method: method,
                isStatic: isStatic,
                stepsFactory: factory,
                scene: attr.Scene,
                readyWhen: attr.ReadyWhen,
                timeScale: attr.TimeScale,
                reuseScene: attr.ReuseScene,
                setup: attr.Setup);
            return true;
        }

        // 戻り値型が IEnumerable<ScenarioStep> として扱えるかを判定する。
        // ScenarioStep[] / List<ScenarioStep> / IEnumerable<ScenarioStep> 等を全部受け入れる。
        private static bool IsScenarioStepEnumerable(Type returnType)
        {
            if (returnType == null) return false;
            // 直接 IEnumerable<ScenarioStep> や派生
            foreach (var iface in returnType.GetInterfaces())
            {
                if (!iface.IsGenericType) continue;
                if (iface.GetGenericTypeDefinition() != typeof(IEnumerable<>)) continue;
                var arg = iface.GetGenericArguments()[0];
                if (arg == typeof(ScenarioStep) || typeof(ScenarioStep).IsAssignableFrom(arg))
                    return true;
            }
            // returnType そのものが IEnumerable<ScenarioStep>
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var arg = returnType.GetGenericArguments()[0];
                if (arg == typeof(ScenarioStep) || typeof(ScenarioStep).IsAssignableFrom(arg))
                    return true;
            }
            return false;
        }

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
