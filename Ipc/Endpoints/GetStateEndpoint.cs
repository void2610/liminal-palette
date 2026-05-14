using System.Threading;
using System.Threading.Tasks;
using Void2610.LiminalPalette.Ipc.Json;
using Void2610.LiminalPalette.Ipc.Server;
using Void2610.LiminalPalette.Ipc.Threading;

namespace Void2610.LiminalPalette.Ipc.Endpoints
{
    /// <summary>
    /// GET /api/v1/state[?path=Player/Health]: [LiminalObservableField] が公開する状態のスナップショット。
    /// path 指定時はそのフィールドのみ、未指定時は全フィールドの一覧を返す。
    /// 値は ReactiveProperty.Value 相当を ToDisplayString で文字列化して返す。
    /// </summary>
    public sealed class GetStateEndpoint : IIpcEndpoint
    {
        public bool RequiresAuth => true;

        public Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken ct)
        {
            // Registry / InstanceResolver / ReadCurrent はメインスレッドで読みたい (Unity API 呼出が起こりうるため)。
            return MainThreadDispatcher.RunAsync(async () =>
            {
                await Task.CompletedTask;

                var registry = ObservableFieldRegistry.Default;
                var w = new JsonWriter();

                // ?path= 指定がある場合は単一フィールド、なければ all。
                if (request.Query != null && request.Query.TryGetValue("path", out var path) && !string.IsNullOrEmpty(path))
                {
                    var d = registry.Find(path);
                    if (d == null) return IpcResponse.NotFound($"ObservableField not found: {path}");
                    // IsStatic な field は VContainer 登録不要 (静的 utility 想定)。instance=null で読む。
                    var instance = d.IsStatic ? null : LiminalPalette.InstanceResolver.Resolve(d.DeclaringType);
                    if (!d.IsStatic && instance == null) return IpcResponse.InternalError(
                        $"Instance not resolved for {d.DeclaringType?.FullName ?? "<unknown>"}.");
                    var value = d.ReadCurrent(instance);
                    w.BeginObject();
                    w.WriteString("path", d.Path);
                    if (value == null) w.WriteNull("value");
                    else w.WriteString("value", TypeConverterRegistry.ToDisplayString(value));
                    w.WriteString("type", d.ValueType?.Name ?? "");
                    w.EndObject();
                    return IpcResponse.Json(200, w.ToString());
                }

                // 全件
                var all = registry.All;
                w.BeginObject();
                w.BeginArray("fields");
                for (var i = 0; i < all.Count; i++)
                {
                    var d = all[i];
                    // IsStatic は instance=null でも instanceResolved=true として扱う (VContainer 経路を通らない)。
                    var instance = d.IsStatic ? null : LiminalPalette.InstanceResolver.Resolve(d.DeclaringType);
                    var resolved = d.IsStatic || instance != null;
                    var value = resolved ? d.ReadCurrent(instance) : null;
                    w.BeginObject();
                    w.WriteString("path", d.Path);
                    if (value == null) w.WriteNull("value");
                    else w.WriteString("value", TypeConverterRegistry.ToDisplayString(value));
                    w.WriteString("type", d.ValueType?.Name ?? "");
                    w.WriteBool("instanceResolved", resolved);
                    w.EndObject();
                }
                w.EndArray();
                w.EndObject();
                return IpcResponse.Json(200, w.ToString());
            });
        }
    }
}
