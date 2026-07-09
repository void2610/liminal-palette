using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// ICommandExecutor の標準実装。
    /// 同期メソッドと Task / Task&lt;T&gt; / ValueTask / ValueTask&lt;T&gt; / UniTask / UniTask&lt;T&gt; を await して結果を CommandResult に詰める。
    /// 例外はすべて握りつぶして CommandResult.Fail に変換するため、利用側は try-catch 不要。
    /// </summary>
    public sealed class CommandExecutor : ICommandExecutor
    {
        private readonly ICommandRegistry _registry;

        public CommandExecutor(ICommandRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public Task<CommandResult> ExecuteAsync(
            string pathOrAlias,
            IReadOnlyDictionary<string, string>? args,
            CancellationToken ct = default)
        {
            var descriptor = _registry.Find(pathOrAlias);
            if (descriptor == null) return Task.FromResult(CommandResult.Fail($"Command not found: {pathOrAlias}", null, Array.Empty<LogEntry>(), TimeSpan.Zero));
            if (!ArgumentBinder.TryBind(descriptor.Parameters, args ?? new Dictionary<string, string>(), out var bound, out var bindError)) return Task.FromResult(CommandResult.Fail(bindError, null, Array.Empty<LogEntry>(), TimeSpan.Zero));
            return InvokeAsync(descriptor, bound, ct);
        }

        public Task<CommandResult> ExecuteAsync(
            string pathOrAlias,
            IReadOnlyList<string>? positionalArgs,
            CancellationToken ct = default)
        {
            var descriptor = _registry.Find(pathOrAlias);
            if (descriptor == null) return Task.FromResult(CommandResult.Fail($"Command not found: {pathOrAlias}", null, Array.Empty<LogEntry>(), TimeSpan.Zero));
            if (!ArgumentBinder.TryBind(descriptor.Parameters, positionalArgs ?? Array.Empty<string>(), out var bound, out var bindError)) return Task.FromResult(CommandResult.Fail(bindError, null, Array.Empty<LogEntry>(), TimeSpan.Zero));
            return InvokeAsync(descriptor, bound, ct);
        }

        public Task<CommandResult> ExecuteWithTypedArgsAsync(
            string pathOrAlias,
            IReadOnlyDictionary<string, object>? args,
            CancellationToken ct = default)
        {
            var descriptor = _registry.Find(pathOrAlias);
            if (descriptor == null) return Task.FromResult(CommandResult.Fail($"Command not found: {pathOrAlias}", null, Array.Empty<LogEntry>(), TimeSpan.Zero));
            if (!ArgumentBinder.TryBindTyped(descriptor.Parameters, args ?? new Dictionary<string, object>(), out var bound, out var bindError)) return Task.FromResult(CommandResult.Fail(bindError, null, Array.Empty<LogEntry>(), TimeSpan.Zero));
            return InvokeAsync(descriptor, bound, ct);
        }

        // 実呼び出しと async unwrap、ログキャプチャ、所要時間計測の本体。
        // Phase 1 では CancellationToken を実行直前と await 完了後にチェックし、キャンセル要求があれば
        // OperationCanceledException として CommandResult.Fail に変換する (他例外と同じ扱い)。
        // Phase 2 で [CancellationToken] 自動注入を入れた際は、コマンド本体にも ct が伝わるようにする。
        private static async Task<CommandResult> InvokeAsync(CommandDescriptor descriptor, object[] bound, CancellationToken ct)
        {
            using var capture = new LogCapture();
            var sw = Stopwatch.StartNew();
            try
            {
                // 呼び出し前にキャンセル要求を確認 (事前にキャンセル済みなら本体を走らせない)。
                ct.ThrowIfCancellationRequested();
                // Invoker が指定されている場合はそちらを優先 (動的登録コマンド向け)。
                // Method 経由は属性ベース登録 (Phase 1 由来) のための従来パス。
                // Phase 5a: インスタンスメソッド対応のため IsStatic で分岐。
                // 静的なら従来通り null を target に渡し、インスタンスなら IInstanceResolver で取得。
                object raw;
                if (descriptor.Invoker != null)
                {
                    raw = descriptor.Invoker(bound);
                }
                else if (descriptor.Method.IsStatic)
                {
                    raw = descriptor.Method.Invoke(null, bound);
                }
                else
                {
                    var instance = LiminalPalette.InstanceResolver.Resolve(descriptor.Method.DeclaringType);
                    if (instance == null)
                    {
                        // 未登録 / LoadScene 直後の一過性未解決。専用例外型でシナリオ側の識別・リトライを可能にする。
                        throw new InstanceUnresolvedException(
                            $"Instance not resolved for {descriptor.Method.DeclaringType?.FullName ?? "<unknown>"}. " +
                            "Register the type with VContainer (e.g. builder.RegisterComponentInHierarchy<T>()) " +
                            "and call builder.RegisterEntryPoint<LiminalPaletteEntryPoint>() in your LifetimeScope.");
                    }
                    raw = descriptor.Method.Invoke(instance, bound);
                }
                var value = await UnwrapAsync(raw, descriptor.ReturnType).ConfigureAwait(false);
                // 非同期 await 完了後に再チェック (実行中にキャンセル要求が来たケースに対応)。
                ct.ThrowIfCancellationRequested();
                sw.Stop();
                return CommandResult.Ok(value, capture.Drain(), sw.Elapsed);
            }
            catch (OperationCanceledException ex)
            {
                // キャンセルは Fail として返す。Exception を保持しておき、利用側がキャンセル判定に使えるようにする。
                sw.Stop();
                return CommandResult.Fail("Cancelled", ex, capture.Drain(), sw.Elapsed);
            }
            catch (TargetInvocationException tie)
            {
                // リフレクション経由の呼び出し例外は内側を取り出して報告する。
                // 内部が OperationCanceledException の場合もキャンセル扱いに揃える。
                sw.Stop();
                var inner = tie.InnerException ?? tie;
                if (inner is OperationCanceledException)
                {
                    return CommandResult.Fail("Cancelled", inner, capture.Drain(), sw.Elapsed);
                }
                return CommandResult.Fail(inner.Message, inner, capture.Drain(), sw.Elapsed);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return CommandResult.Fail(ex.Message, ex, capture.Drain(), sw.Elapsed);
            }
        }

        // 戻り値の async unwrap。
        // - void / null     → null
        // - Task            → await のち null
        // - Task<T>         → await のち T
        // - ValueTask       → await のち null
        // - ValueTask<T>    → await のち T
        // - UniTask         → await のち null
        // - UniTask<T>      → await のち T
        private static async Task<object> UnwrapAsync(object raw, Type returnType)
        {
            if (raw == null) return null;

            if (raw is Task task)
            {
                await task.ConfigureAwait(false);
                // Task<T> なら Result プロパティから取り出す。Task (非ジェネリック) なら null 扱い。
                var t = task.GetType();
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>)) return t.GetProperty("Result")?.GetValue(task);
                return null;
            }

            // ValueTask 系は AsTask で Task に正規化してから処理する。
            if (returnType == typeof(ValueTask))
            {
                var vt = (ValueTask)raw;
                await vt.ConfigureAwait(false);
                return null;
            }
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                // ValueTask<T> は AsTask() を呼ぶと Task<T> になる。MethodInfo 経由で呼び出して await する。
                var asTask = returnType.GetMethod("AsTask");
                if (asTask != null)
                {
                    var task2 = (Task)asTask.Invoke(raw, null);
                    await task2.ConfigureAwait(false);
                    return task2.GetType().GetProperty("Result")?.GetValue(task2);
                }
            }

            // UniTask 系も AsTask で Task に正規化する。AsTask はインスタンスメソッドではなく
            // UniTaskExtensions の拡張メソッドで、UniTask<T> の T は実行時にしか分からないため
            // ジェネリック定義を MakeGenericMethod で閉じて呼び出す。
            if (returnType == typeof(UniTask))
            {
                await ((UniTask)raw).AsTask().ConfigureAwait(false);
                return null;
            }
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(UniTask<>))
            {
                var asTask = UniTaskAsTaskGenericDefinition.MakeGenericMethod(returnType.GetGenericArguments()[0]);
                var task2 = (Task)asTask.Invoke(null, new[] { raw });
                await task2.ConfigureAwait(false);
                return task2.GetType().GetProperty("Result")?.GetValue(task2);
            }

            // 同期戻り値はそのまま返す。
            return raw;
        }

        // UniTaskExtensions.AsTask<T>(UniTask<T>) のジェネリック定義キャッシュ。
        private static readonly MethodInfo UniTaskAsTaskGenericDefinition =
            typeof(UniTaskExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == nameof(UniTaskExtensions.AsTask) && m.IsGenericMethodDefinition);
    }
}
