using System;
using System.IO;
using System.Threading;
using UnityEngine;
using Void2610.LiminalPalette.Ipc;
using Void2610.LiminalPalette.Ipc.Auth;
using Void2610.LiminalPalette.Ipc.Endpoints;
using Void2610.LiminalPalette.Ipc.Server;
using Void2610.LiminalPalette.Ipc.Threading;

namespace Void2610.LiminalPalette.Player.Ipc
{
    /// <summary>
    /// Runtime (Player ビルド / Play Mode) で IPC HTTP サーバーを立てるブートストラップ。
    ///
    /// 三重防御:
    ///   (1) asmdef defineConstraints: "UNITY_EDITOR || DEVELOPMENT_BUILD"
    ///       → Production ビルドでは asmdef 自体がコンパイルされず、HttpServer のシンボルが Player に混入しない。
    ///   (2) ProductionGuard.ShouldDisableInRuntime
    ///       → 設定で「Production ビルドでは無効」を明示できる。
    ///   (3) IpcSettings.EnableInRuntime = false
    ///       → 利用側が起動時に明示的にオプトアウト可能。
    /// </summary>
    internal static class RuntimeIpcBootstrap
    {
        private static HttpServer _server;
        private static GameObject _tickerGo;

        // Configurable Enter Play Mode で "Reload Domain" がオフでも、Play Mode に入るたびに
        // 必ず呼ばれる SubsystemRegistration で静的フィールドをクリアする。
        // _server / _tickerGo は前回 Play Mode の Application.quitting で破棄済みのため null 化のみ。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _server = null;
            _tickerGo = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            // Configurable Enter Play Mode で Reload Domain off の場合、static `_server` が前 Editor session の
            // HttpServer を保持したまま Play Mode に入る。新 Server の port bind が "Address already in use" で
            // 失敗するのを防ぐため、Initialize 冒頭で既存 server / ticker GO を必ず破棄する。
            Shutdown();

            try
            {
                var settings = PaletteRuntimeSettings.LoadOrCreateDefault();
                if (ProductionGuard.ShouldDisableInRuntime(settings)) return;
                if (!IpcSettings.EnableInRuntime) return;

                // メインスレッド ID を捕捉 (BeforeSceneLoad はメインスレッドで動く)。
                MainThreadDispatcher.RegisterMainThread(Thread.CurrentThread.ManagedThreadId);

                // Tick を駆動する MonoBehaviour を生成。LiminalPaletteRuntime とは別 GameObject。
                _tickerGo = new GameObject("[LiminalPaletteIpcTicker]");
                UnityEngine.Object.DontDestroyOnLoad(_tickerGo);
                _tickerGo.hideFlags = HideFlags.HideAndDontSave;
                _tickerGo.AddComponent<IpcRuntimeTicker>();

                // Application.quitting は Editor では Editor アプリ終了時にのみ発火するので、Play Mode 終了
                // (= Stop ボタン) では呼ばれない。Editor 限定で playModeStateChanged.ExitingPlayMode でも
                // Shutdown を走らせて port を解放しないと、次 Play Mode で Address already in use になる。
                Application.quitting -= Shutdown;
                Application.quitting += Shutdown;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif

                StartServer();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LiminalPalette.Ipc] Runtime bootstrap failed: {ex.Message}");
            }
        }

#if UNITY_EDITOR
        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
        {
            // Play Mode 終了 (= ExitingPlayMode) で port を解放する。EnteredEditMode 時点では既に
            // 次 Initialize の準備に入る可能性があるため、ExitingPlayMode のタイミングで処理する。
            if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                Shutdown();
            }
        }
#endif

        private static void StartServer()
        {
            var token = TokenStore.LoadOrCreate();
            var router = new IpcRouter(new TokenAuthenticator(token));

            // Unity API はメインスレッド専用なので bootstrap (= ここはメインスレッド) で
            // 取得済みの値を HealthEndpoint に渡す。HTTP ワーカースレッドから直接呼ぶと
            // "can only be called from the main thread" で 500 になる。
            var projectName = Application.productName ?? "";
            var dataPath = Application.dataPath;
            var projectPath = string.IsNullOrEmpty(dataPath)
                ? ""
                : (Path.GetDirectoryName(dataPath) ?? "");
            router.Register("GET", "/api/v1/health", new HealthEndpoint("runtime", projectName, projectPath));
            router.Register("GET", "/api/v1/commands", new ListCommandsEndpoint());
            router.Register("POST", "/api/v1/execute", new ExecuteCommandEndpoint());
            router.Register("GET", "/api/v1/logs", new ListLogsEndpoint());
            router.Register("GET", "/api/v1/state", new GetStateEndpoint());
            router.Register("GET", "/api/v1/scenarios", new ListScenariosEndpoint());
            router.Register("POST", "/api/v1/scenarios/run", new RunScenarioEndpoint());

            // Play Mode 中は <project>/ProjectSettings/LiminalPalette.json が読めるので preferred port を採用。
            // フォールバック順: runtimePort (Play Mode 専用) → port (Editor 共通) → DefaultPort。
            // Player ビルドでは ProjectSettings/ が同梱されないので両方 null になり DefaultPort になる。
            var preferred = ProjectConfig.GetPreferredRuntimePort()
                ?? ProjectConfig.GetPreferredPort()
                ?? IpcSettings.DefaultPort;
            _server = new HttpServer(router, preferred);
            _server.Start();

            Debug.Log($"[LiminalPalette.Ipc] Runtime server listening on http://127.0.0.1:{_server.Port}/  Token: {TokenStore.TokenFilePath}  (do not share)");
        }

        private static void Shutdown()
        {
            try { _server?.Dispose(); } catch { /* swallow */ }
            _server = null;
            if (_tickerGo != null)
            {
                UnityEngine.Object.Destroy(_tickerGo);
                _tickerGo = null;
            }
        }
    }

    /// <summary>MainThreadDispatcher.Tick を Update で駆動するだけの薄い MonoBehaviour。</summary>
    internal sealed class IpcRuntimeTicker : MonoBehaviour
    {
        private void Update() => MainThreadDispatcher.Tick();
    }
}
