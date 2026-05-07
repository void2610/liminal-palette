using System;
using System.Collections.Generic;
using System.Reflection;
using R3;
using UnityEngine;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// [ConsoleObservableField] が付いたプロパティ / フィールドを全 Assembly からスキャンして
    /// ObservableFieldRegistry.Default に登録する。
    /// Bootstrap の起動経路から呼ばれる。
    ///
    /// 受け入れる型 (Phase 5a):
    ///   - R3.ReactiveProperty&lt;T&gt; / R3.Observable&lt;T&gt;
    ///   - その他は警告でスキップ
    /// </summary>
    public static class ObservableFieldScanner
    {
        public static void ScanAll()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var ai = 0; ai < assemblies.Length; ai++)
            {
                var asm = assemblies[ai];
                if (ShouldSkip(asm)) continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types ?? Array.Empty<Type>(); }
                catch { continue; }

                for (var ti = 0; ti < types.Length; ti++)
                {
                    var type = types[ti];
                    if (type == null) continue;
                    ScanType(type);
                }
            }
        }

        private static bool ShouldSkip(Assembly asm)
        {
            var name = asm.GetName().Name ?? "";
            // 標準アセンブリ / フレームワーク系をスキップ (CommandRegistry と同じ流儀)。
            return name.StartsWith("System") || name.StartsWith("Microsoft") ||
                   name.StartsWith("mscorlib") || name.StartsWith("UnityEngine") ||
                   name.StartsWith("UnityEditor") || name.StartsWith("Unity.") ||
                   name.StartsWith("nunit") || name.StartsWith("Mono.");
        }

        private static void ScanType(Type type)
        {
            // public のみを対象とする (troubleshooting.md / docs の仕様: [ConsoleObservableField] は public 必須)。
            // 公開 API として使われる前提のため、意図しない private/internal メンバーの公開を避ける。
            const BindingFlags flags = BindingFlags.Public |
                                       BindingFlags.Instance | BindingFlags.Static |
                                       BindingFlags.DeclaredOnly;
            // Properties
            PropertyInfo[] props;
            try { props = type.GetProperties(flags); }
            catch { props = Array.Empty<PropertyInfo>(); }
            for (var pi = 0; pi < props.Length; pi++)
            {
                var p = props[pi];
                ConsoleObservableFieldAttribute attr;
                try { attr = p.GetCustomAttribute<ConsoleObservableFieldAttribute>(); }
                catch { continue; }
                if (attr == null) continue;
                if (!TryBuildFromProperty(type, p, attr, out var desc, out var err))
                {
                    Debug.LogWarning($"[LiminalPalette] ObservableField skipped on {type.FullName}.{p.Name}: {err}");
                    continue;
                }
                ObservableFieldRegistry.Default.Register(desc);
            }

            // Fields
            FieldInfo[] fields;
            try { fields = type.GetFields(flags); }
            catch { fields = Array.Empty<FieldInfo>(); }
            for (var fi = 0; fi < fields.Length; fi++)
            {
                var f = fields[fi];
                ConsoleObservableFieldAttribute attr;
                try { attr = f.GetCustomAttribute<ConsoleObservableFieldAttribute>(); }
                catch { continue; }
                if (attr == null) continue;
                if (!TryBuildFromField(type, f, attr, out var desc, out var err))
                {
                    Debug.LogWarning($"[LiminalPalette] ObservableField skipped on {type.FullName}.{f.Name}: {err}");
                    continue;
                }
                ObservableFieldRegistry.Default.Register(desc);
            }
        }

        private static bool TryBuildFromProperty(Type declaringType, PropertyInfo prop,
            ConsoleObservableFieldAttribute attr, out ObservableFieldDescriptor descriptor, out string error)
        {
            return TryBuildCommon(
                declaringType, prop.PropertyType, attr,
                getMember: instance => prop.GetValue(instance),
                memberName: prop.Name,
                outDescriptor: out descriptor,
                outError: out error);
        }

        private static bool TryBuildFromField(Type declaringType, FieldInfo field,
            ConsoleObservableFieldAttribute attr, out ObservableFieldDescriptor descriptor, out string error)
        {
            return TryBuildCommon(
                declaringType, field.FieldType, attr,
                getMember: instance => field.GetValue(instance),
                memberName: field.Name,
                outDescriptor: out descriptor,
                outError: out error);
        }

        // 戻り値型が ReactiveProperty<T> / Observable<T> なら descriptor を組み立てる。
        // それ以外は false を返してスキップさせる。
        private static bool TryBuildCommon(Type declaringType, Type memberType,
            ConsoleObservableFieldAttribute attr, Func<object, object> getMember, string memberName,
            out ObservableFieldDescriptor outDescriptor, out string outError)
        {
            outDescriptor = null;
            outError = null;

            if (string.IsNullOrEmpty(attr.Path))
            {
                outError = "Path is empty";
                return false;
            }

            // ジェネリック型の T を取り出す。ReactiveProperty<T> / Observable<T> 両対応。
            var valueType = ExtractObservableValueType(memberType);
            if (valueType == null)
            {
                outError = $"Member type {memberType.Name} is not ReactiveProperty<T> or Observable<T>";
                return false;
            }

            var isReactiveProperty = IsReactiveProperty(memberType);

            // ReadCurrent: ReactiveProperty.Value を取る or Observable のラスト値 (未対応)。
            Func<object, object> readCurrent;
            if (isReactiveProperty)
            {
                // ReactiveProperty<T> は CurrentValue (R3) または Value プロパティ。
                // R3 の ReactiveProperty<T> は public T Value { get; set; } を持つ。
                var valueProp = memberType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                if (valueProp == null)
                {
                    outError = "ReactiveProperty<T>.Value not found (R3 API mismatch?)";
                    return false;
                }
                readCurrent = instance =>
                {
                    var rp = getMember(instance);
                    return rp == null ? null : valueProp.GetValue(rp);
                };
            }
            else
            {
                // Observable<T> 単体は最新値を持たない。null を返して UI 側で「未観測」表示にする。
                readCurrent = _ => null;
            }

            // Subscribe: Observable<T>.Subscribe(Action<T>) を呼ぶ。
            // ReactiveProperty<T> も Observable<T> を継承するので同じ経路で OK。
            // ただし Action<T> ではなく Action<object> なので、ボックス化する変換ラムダを Reflection で組む。
            var subscribe = BuildSubscribeFunc(memberType, valueType, getMember);

            outDescriptor = new ObservableFieldDescriptor(
                path: attr.Path,
                description: attr.Description,
                declaringType: declaringType,
                valueType: valueType,
                readCurrent: readCurrent,
                subscribe: subscribe);
            return true;
        }

        // memberType = ReactiveProperty<T> / ReadOnlyReactiveProperty<T> / Observable<T>。T を取り出す。
        // 該当しない型なら null。
        private static Type ExtractObservableValueType(Type memberType)
        {
            var t = memberType;
            while (t != null && t != typeof(object))
            {
                if (t.IsGenericType)
                {
                    var def = t.GetGenericTypeDefinition();
                    if (def == typeof(ReactiveProperty<>) ||
                        def == typeof(ReadOnlyReactiveProperty<>) ||
                        def == typeof(Observable<>))
                    {
                        return t.GetGenericArguments()[0];
                    }
                }
                t = t.BaseType;
            }
            return null;
        }

        // ReactiveProperty<T> または ReadOnlyReactiveProperty<T> かどうか判定する。
        // どちらも CurrentValue / Value プロパティで現在値を読める。
        private static bool IsReactiveProperty(Type memberType)
        {
            var t = memberType;
            while (t != null && t != typeof(object))
            {
                if (t.IsGenericType)
                {
                    var def = t.GetGenericTypeDefinition();
                    if (def == typeof(ReactiveProperty<>) || def == typeof(ReadOnlyReactiveProperty<>))
                        return true;
                }
                t = t.BaseType;
            }
            return false;
        }

        // Observable<T>.Subscribe(Action<T>) を呼ぶラムダを組む。
        // R3 の Subscribe は extension method (R3.ObservableExtensions.Subscribe<T>) で、
        // Reflection で extension を探すよりもジェネリックヘルパに丸投げして C# コンパイラに
        // 解決させる方が確実。
        private static Func<object, Action<object>, IDisposable> BuildSubscribeFunc(
            Type memberType, Type valueType, Func<object, object> getMember)
        {
            // SubscribeTyped<T> を valueType でクローズドにする。
            var helper = typeof(ObservableFieldScanner)
                .GetMethod(nameof(SubscribeTyped), BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(valueType);

            return (instance, onNext) =>
            {
                var observable = getMember(instance);
                if (observable == null) return Disposable.Empty;
                try
                {
                    return (IDisposable)helper.Invoke(null, new[] { observable, onNext });
                }
                catch (System.Reflection.TargetInvocationException tie)
                {
                    UnityEngine.Debug.LogWarning($"[LiminalPalette] Subscribe failed: {tie.InnerException?.Message}");
                    return Disposable.Empty;
                }
            };
        }

        // R3 の extension method `Observable<T>.Subscribe(Action<T>)` を C# コンパイラに解決させる。
        // 呼び出し元はクローズドジェネリックメソッド (= 型 T が確定済み) として MakeGenericMethod 経由で呼ぶ。
        private static IDisposable SubscribeTyped<T>(object source, Action<object> onNext)
        {
            // source は ReactiveProperty<T> もしくは Observable<T> 派生。Observable<T> にキャストできる。
            var typed = (Observable<T>)source;
            // using R3; を入れた前提で、ここで extension Subscribe(Action<T>) が解決される。
            return typed.Subscribe(v => onNext(v));
        }

        // フォールバック用の no-op Disposable。
        private static class Disposable
        {
            public static readonly IDisposable Empty = new EmptyDisposable();
            private sealed class EmptyDisposable : IDisposable { public void Dispose() { } }
        }
    }
}
