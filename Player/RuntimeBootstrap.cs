using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Void2610.LiminalPalette.Player
{
    /// <summary>
    /// Runtime 起動時にパレットの DontDestroyOnLoad シングルトンを生成するブートストラップ。
    ///
    /// 旧実装は <c>RuntimeInitializeOnLoadMethod(BeforeSceneLoad)</c> + <c>static _initialized</c>
    /// フラグで二重生成を抑止していたが、以下の問題があった:
    ///
    ///   - Editor の再コンパイル (= Domain Reload) のたびに Initialize が走って新しい
    ///     <c>[LiminalPaletteRuntime]</c> GameObject が生成される。
    ///   - GameObject の hideFlags が HideAndDontSave で Domain Reload を跨いで残るため
    ///     orphan が累積する (時間が経つほど数十〜数百個に膨れる)。
    ///   - 累積した複数 LiminalPaletteRuntime はそれぞれ Update で Cmd+K を検知して
    ///     PaletteInputBlocker.Engage を連鎖発火させ、InputSystem の ActionMap 復元を
    ///     破壊する (= Cmd+K 後にゲーム入力が戻らない、コマンドが反応しない 等)。
    ///   - Configurable Enter Play Mode の Reload Domain off では BeforeSceneLoad が
    ///     呼ばれないため、Play Mode 開始時の冪等な再初期化が成立しない。
    ///
    /// 本実装は次の 3 経路すべてから同じ <see cref="CleanupAndInitialize"/> を呼んで
    /// 「既存全 destroy → 新規 1 個生成」を冪等に行う:
    ///
    ///   - Editor: <c>[InitializeOnLoadMethod]</c> で Editor 起動 / スクリプト再コンパイル時
    ///   - Editor: <c>EditorApplication.playModeStateChanged</c> で Play Mode 開始時
    ///     (Reload Domain off でも playModeStateChanged は必ず発火する)
    ///   - Player Build: <c>RuntimeInitializeOnLoadMethod(BeforeSceneLoad)</c>
    /// </summary>
    internal static class RuntimeBootstrap
    {
#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void EditorInit()
        {
            // 起動 / 再コンパイル時に既存の orphan を全部掃除し、必要なら 1 個生成する。
            // Editor 編集中 (Play Mode に入る前) も Inspector や Scene からパレットが
            // 作られた状態を確認したいケースに備えて Initialize まで行う。
            CleanupAndInitialize();

            // Reload Domain off の Play Mode 開始では [InitializeOnLoadMethod] が呼ばれない。
            // playModeStateChanged は Reload Domain off でも必ず発火するので、
            // EnteredPlayMode で再度 Cleanup + Initialize する。
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            CleanupAndInitialize();
        }
#endif

        // Player Build (DEVELOPMENT_BUILD) はそもそも累積しないが、
        // 経路を統一して挙動を読みやすくするため同じ Cleanup 経由で初期化する。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RuntimeInit() => CleanupAndInitialize();

        private static void CleanupAndInitialize()
        {
            var settings = PaletteRuntimeSettings.LoadOrCreateDefault();
            if (ProductionGuard.ShouldDisableInRuntime(settings))
            {
                // Production guard が有効な場合は既存 orphan も含めて全部破棄する。
                DestroyAllRuntimes();
                return;
            }

            // 既存の LiminalPaletteRuntime をすべて破棄してから 1 個だけ新規生成する。
            // (1 個再利用ではなく毎回 destroy + create にするのは、再コンパイルで参照が
            // 中途半端に切れた MonoBehaviour を引きずらないため。)
            DestroyAllRuntimes();

            var go = new GameObject("[LiminalPaletteRuntime]");
            go.hideFlags = HideFlags.HideAndDontSave;

            var runtime = go.AddComponent<LiminalPaletteRuntime>();
            runtime.Configure(settings);
        }

        private static void DestroyAllRuntimes()
        {
            var existing = Resources.FindObjectsOfTypeAll<LiminalPaletteRuntime>();
            for (var i = 0; i < existing.Length; i++)
            {
                if (existing[i] == null) continue;
                var go = existing[i].gameObject;
                if (go == null) continue;
                // Editor 編集中も対応するため Application.isPlaying で分岐する必要はない。
                // DestroyImmediate は HideAndDontSave のような hideFlags でも確実に破棄する。
                Object.DestroyImmediate(go);
            }
        }
    }
}
