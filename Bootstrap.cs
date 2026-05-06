using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// 起動時に AttributeScanner を回し、CommandRegistry.Default を構築するエントリ。
    /// Editor では DomainReload ごとに、Runtime では BeforeSceneLoad のタイミングで実行される。
    /// </summary>
    internal static class Bootstrap
    {
        // DomainReload で static フィールドはリセットされるため、Editor でも 1 セッション中の重複起動だけ防ぐ。
        private static bool _initialized;

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void InitializeEditor()
        {
            Initialize();
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeRuntime()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                var commands = AttributeScanner.Scan(assemblies);

                // ここで Clear() は呼ばない: Editor の DomainReload 後は Default が空に作り直されており、
                // _initialized で重複起動も防いでいるため Clear は不要。逆に Clear すると、
                // 別ソース (例: Editor 側で MenuItem を動的登録する EditorMenuItemBootstrap) が
                // 先に登録した分まで巻き込んで消してしまう副作用がある。
                for (var i = 0; i < commands.Count; i++)
                {
                    CommandRegistry.Default.Register(commands[i]);
                }

                // Phase 5a: [ConsoleObservableField] のスキャン。
                // ReactiveProperty<T> / Observable<T> を発見して ObservableFieldRegistry.Default に投入。
                ObservableFieldScanner.ScanAll();

                // Phase 5b: [ConsoleScenario] のスキャン。
                // IEnumerable<ScenarioStep> を返すメソッドを発見して ScenarioRegistry.Default に投入。
                ScenarioScanner.ScanAll();
            }
            catch (Exception ex)
            {
                // スキャン失敗で Unity 全体が止まるのは避けたい。警告のみ残して継続。
                Debug.LogWarning($"[LiminalPalette] Bootstrap failed: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
