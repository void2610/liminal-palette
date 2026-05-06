using System;
using System.Threading;
using System.Threading.Tasks;
using Void2610.LiminalPalette.Ipc.Json;
using Void2610.LiminalPalette.Ipc.Server;
using Void2610.LiminalPalette.Ipc.Threading;

namespace Void2610.LiminalPalette.Ipc.Endpoints
{
    /// <summary>
    /// GET /api/v1/scenarios: 登録済みシナリオ一覧 (認証必須)。
    /// stepCount は名前付きシナリオを 1 回 enumerate した結果のステップ数 (副作用付きシナリオでは
    /// 不正確になり得るが、表示用なので許容する)。
    /// </summary>
    public sealed class ListScenariosEndpoint : IIpcEndpoint
    {
        public bool RequiresAuth => true;

        public Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken ct)
        {
            return MainThreadDispatcher.RunAsync(async () =>
            {
                await Task.CompletedTask;
                var w = new JsonWriter();
                w.BeginObject();
                w.BeginArray("scenarios");
                var all = LiminalPalette.Scenarios.All;
                for (var i = 0; i < all.Count; i++)
                {
                    var d = all[i];
                    var stepCount = TryCountSteps(d);
                    IpcContracts.WriteScenario(w, d, stepCount);
                }
                w.EndArray();
                w.EndObject();
                return IpcResponse.Json(200, w.ToString());
            });
        }

        // ステップ数を 1 回だけ enumerate して数える。失敗した場合は -1 を返して
        // 「不明」状態として UI / クライアント側で扱えるようにする。
        private static int TryCountSteps(ScenarioDescriptor d)
        {
            try
            {
                object instance = null;
                if (!d.IsStatic)
                {
                    instance = LiminalPalette.InstanceResolver.Resolve(d.DeclaringType);
                    // インスタンス未解決でも一覧表示はしたいので、ステップ数不明扱いの -1 を返す。
                    // UI 側 (PaletteView の Steps 列) はこの -1 を "?" と表示する。
                    // 実行時は ScenarioExecutor が「Instance not resolved」エラーを返す。
                    if (instance == null) return -1;
                }
                var count = 0;
                foreach (var s in d.StepsFactory(instance))
                {
                    if (s == null) continue;
                    count++;
                }
                return count;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
