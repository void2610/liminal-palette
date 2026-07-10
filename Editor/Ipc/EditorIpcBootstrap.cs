using System;
using System.IO;
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

            // Application.productName / Application.dataPath はメインスレッド専用 API。
            // bootstrap はメインスレッドで動くのでここで取り、HealthEndpoint に渡しておく
            // (HTTP ワーカースレッドから直接読むと "can only be called from the main thread" で 500 になる)。
            var projectName = Application.productName ?? "";
            var dataPath = Application.dataPath;
            var projectPath = string.IsNullOrEmpty(dataPath)
                ? ""
                : (Path.GetDirectoryName(dataPath) ?? "");
            router.Register("GET", "/api/v1/health", new HealthEndpoint("editor", projectName, projectPath));
            router.Register("GET", "/api/v1/commands", new ListCommandsEndpoint());
            router.Register("POST", "/api/v1/execute", new ExecuteCommandEndpoint());
            router.Register("GET", "/api/v1/logs", new ListLogsEndpoint());
            router.Register("GET", "/api/v1/state", new GetStateEndpoint());
            router.Register("GET", "/api/v1/scenarios", new ListScenariosEndpoint());
            router.Register("POST", "/api/v1/scenarios/run", new RunScenarioEndpoint());
            // テスト実行は編集時専用。実装 (TestRunnerApi) は com.unity.test-framework 導入時のみ
            // TestRunnerBridge に登録され、未導入なら両エンドポイントは 501 を返す。
            router.Register("POST", "/api/v1/tests/run", new RunTestsEndpoint());
            router.Register("GET", "/api/v1/tests/result", new TestResultEndpoint());

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
