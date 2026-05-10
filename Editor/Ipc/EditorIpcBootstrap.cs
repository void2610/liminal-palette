using System;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Void2610.LiminalPalette.Ipc;
using Void2610.LiminalPalette.Ipc.Auth;
using Void2610.LiminalPalette.Ipc.Endpoints;
using Void2610.LiminalPalette.Ipc.Server;
using Void2610.LiminalPalette.Ipc.Threading;

namespace Void2610.LiminalPalette.Editor.Ipc
{
    /// <summary>
    /// Editor 起動時に IPC HTTP サーバーを立てるブートストラップ。
    /// DomainReload (アセンブリ再ロード) のたびに [InitializeOnLoadMethod] が再実行されるため、
    /// AssemblyReloadEvents.beforeAssemblyReload で確実に Stop して listener を解放する。
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorIpcBootstrap
    {
        // DomainReload を跨いで保持しないが、再 InitializeOnLoad で値が初期化される。
        private static HttpServer _server;

        static EditorIpcBootstrap()
        {
            // [InitializeOnLoad] static cctor で初期化。InitializeOnLoadMethod でも同等。
            try
            {
                if (!IpcSettings.EnableInEditor) return;

                // メインスレッド ID を Editor のメインスレッドとして登録。
                MainThreadDispatcher.RegisterMainThread(Thread.CurrentThread.ManagedThreadId);
                EditorApplication.update -= OnEditorUpdate; // 二重登録防止
                EditorApplication.update += OnEditorUpdate;

                // Quit / DomainReload で確実に Stop。
                EditorApplication.quitting -= Shutdown;
                EditorApplication.quitting += Shutdown;
                AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
                AssemblyReloadEvents.beforeAssemblyReload += Shutdown;

                StartServer();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LiminalPalette.Ipc] Editor bootstrap failed: {ex.Message}");
            }
        }

        private static void StartServer()
        {
            if (_server != null) return; // 既に起動済み (InitializeOnLoadMethod 二重発火対策)

            var token = TokenStore.LoadOrCreate();
            var router = new IpcRouter(new TokenAuthenticator(token));
            router.Register("GET", "/api/v1/health", new HealthEndpoint("editor"));
            router.Register("GET", "/api/v1/commands", new ListCommandsEndpoint());
            router.Register("POST", "/api/v1/execute", new ExecuteCommandEndpoint());
            router.Register("GET", "/api/v1/logs", new ListLogsEndpoint());
            router.Register("GET", "/api/v1/state", new GetStateEndpoint());
            router.Register("GET", "/api/v1/scenarios", new ListScenariosEndpoint());
            router.Register("POST", "/api/v1/scenarios/run", new RunScenarioEndpoint());

            // プロジェクト固有の preferred port があればそれを起点にする (複数 Unity プロジェクト同時起動対応)。
            // 未指定なら IpcSettings.DefaultPort にフォールバック。HttpServer 側で衝突時は隣接ポートに retry する。
            var preferred = ProjectConfig.GetPreferredPort() ?? IpcSettings.DefaultPort;
            _server = new HttpServer(router, preferred);
            _server.Start();

            // Token の場所をログに 1 度だけ表示 (他人に共有しないよう警告も付ける)。
            Debug.Log($"[LiminalPalette.Ipc] Editor server listening on http://127.0.0.1:{_server.Port}/  Token: {TokenStore.TokenFilePath}  (do not share)");
        }

        private static void Shutdown()
        {
            try
            {
                _server?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LiminalPalette.Ipc] Shutdown error: {ex.Message}");
            }
            _server = null;
        }

        private static void OnEditorUpdate() => MainThreadDispatcher.Tick();
    }
}
