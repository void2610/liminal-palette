using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Void2610.LiminalPalette.Integration.VContainer
{
    /// <summary>
    /// 指定された LifetimeScope 派生型のインスタンスを、各シーン読込み時に
    /// active scene へ自動生成する汎用 Bootstrap。
    ///
    /// シーン上に LifetimeScope GameObject を毎回手配置する運用 (シーンが増えるたびに作業が増える)
    /// を避けるための仕組み。Unity Configurable Enter Play Mode の Reload Domain off /
    /// Reload Scene off の組み合わせにも対応する:
    ///   - [InitializeOnLoadMethod] が呼ばれない → playModeStateChanged.EnteredPlayMode で生成
    ///   - sceneLoaded が発火しない → 同上
    ///   - SceneManager.LoadScene 経由の再読込み → sceneLoaded で再生成
    ///   - Player Build (DEVELOPMENT_BUILD) → RuntimeInitializeOnLoadMethod(AfterSceneLoad) で初期化
    ///
    /// 利用側は静的初期化メソッドから Register&lt;T&gt;() を呼ぶだけ:
    ///
    /// <code>
    ///   internal static class MyDebugBootstrap
    ///   {
    /// #if UNITY_EDITOR
    ///       [UnityEditor.InitializeOnLoadMethod]
    ///       private static void EditorInit() =>
    ///           SceneLifetimeScopeBootstrap.Register&lt;MyDebugLifetimeScope&gt;();
    /// #endif
    ///       [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    ///       private static void PlayerInit() =>
    ///           SceneLifetimeScopeBootstrap.Register&lt;MyDebugLifetimeScope&gt;();
    ///   }
    /// </code>
    ///
    /// 生成 GameObject には EditorOnly タグを付与するため、Production build 時は Unity が
    /// GameObject ごと自動除外する (ただし利用側 LifetimeScope を含む asmdef を
    /// defineConstraints で除外していれば二重防御になる)。
    /// </summary>
    public static class SceneLifetimeScopeBootstrap
    {
        // 同じ T を二重登録した時に handlers が重複購読しないようにする冪等キー。
        // Reload Domain off で前回 Editor セッションの static state が残ったまま
        // [InitializeOnLoadMethod] が再呼出されないケースでも、HashSet に Type があれば早期 return する。
        private static readonly HashSet<Type> Registered = new HashSet<Type>();

        /// <summary>
        /// T 型 LifetimeScope を「シーン読込み時に active scene へ動的生成」する登録を行う。
        /// 同じ T 型に対する複数回呼び出しは冪等 (handlers の二重購読は起きない)。
        /// </summary>
        public static void Register<T>() where T : LifetimeScope
        {
            var type = typeof(T);
            // 既に handlers を仕掛け済みの T は早期 return。ただし Play Mode 中の呼出なら
            // 即時生成だけは行う (RuntimeInitializeOnLoadMethod 経由でこの分岐に来るため)。
            var firstTime = Registered.Add(type);

            if (firstTime)
            {
                // 二重防御: 同じ Bootstrap loader が複数フェーズから呼ばれた場合でも、
                // -= してから += することで二重購読を絶対に避ける。
                SceneManager.sceneLoaded -= OnSceneLoaded<T>;
                SceneManager.sceneLoaded += OnSceneLoaded<T>;
#if UNITY_EDITOR
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged<T>;
                EditorApplication.playModeStateChanged += OnPlayModeStateChanged<T>;
#endif
            }

            // RuntimeInitializeOnLoadMethod(AfterSceneLoad) 経由で呼ばれた場合は既に
            // Play Mode 中で active scene が読み込まれているため、即時生成する。
            if (Application.isPlaying)
            {
                EnsureScope<T>(SceneManager.GetActiveScene());
            }
        }

        private static void OnSceneLoaded<T>(Scene scene, LoadSceneMode mode) where T : LifetimeScope
        {
            EnsureScope<T>(scene);
        }

#if UNITY_EDITOR
        private static void OnPlayModeStateChanged<T>(PlayModeStateChange change) where T : LifetimeScope
        {
            // Reload Scene off では Play Mode 開始時に sceneLoaded が発火しないため、
            // EnteredPlayMode で active scene に対し 1 回だけ強制生成する。
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            EnsureScope<T>(SceneManager.GetActiveScene());
        }
#endif

        private static void EnsureScope<T>(Scene scene) where T : LifetimeScope
        {
            // 既存 (例: ユーザーがシーンに手配置した、あるいは前回ロード分が DontDestroyOnLoad で残った) があれば
            // 二重生成しない。
            if (UnityEngine.Object.FindAnyObjectByType<T>() != null) return;

            var go = new GameObject(typeof(T).Name) { tag = "EditorOnly" };
            // Additive ロード時に呼ばれた scene が active scene と異なる場合に備えて移動する。
            if (scene.IsValid()) SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<T>();
        }
    }
}
