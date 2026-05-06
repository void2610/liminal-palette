using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Void2610.LiminalPalette.Editor
{
    /// <summary>
    /// Editor 起動時に Unity の全 [MenuItem] 属性を TypeCache 経由で収集し、
    /// `Menu/<元の menu path>` の形でパレットに自動登録する。
    /// 個別に [ConsoleCommand] を書かなくても、`Window/General/Hierarchy` などのエディタメニューが
    /// すべてパレットからファジー検索で開けるようになる。
    /// </summary>
    internal static class EditorMenuItemBootstrap
    {
        // パレットに登録する際のパスプレフィックス。手書き [ConsoleCommand] と衝突しないように分ける。
        private const string PathPrefix = "Menu/";

        // 除外するメニューパスの prefix。CONTEXT/ はコンポーネントの右クリック専用で、
        // ExecuteMenuItem で叩いてもターゲットがないため意味がない。
        private static readonly string[] ExcludePrefixes =
        {
            "CONTEXT/",
            "internal:",
        };

        // ダミーの MethodInfo (CommandDescriptor.Method に渡す。Invoker を経由するので実際には呼ばれない)。
        private static readonly MethodInfo DummyMethod
            = typeof(EditorMenuItemBootstrap).GetMethod(nameof(NoOp), BindingFlags.Static | BindingFlags.NonPublic);

        private static void NoOp() { }

        [InitializeOnLoadMethod]
        private static void Register()
        {
            try
            {
                var registry = LiminalPalette.Registry;
                var registered = 0;
                foreach (var menuPath in DiscoverMenuItems())
                {
                    var commandPath = PathPrefix + menuPath;

                    // すでに同じパスが登録済みなら上書きしない (ユーザーが手書きした優先)。
                    if (registry.Find(commandPath) != null) continue;

                    // ExecuteMenuItem は shortcut suffix (例: " %t") を含めたパスを受け付けないため、
                    // 表示用とは別に suffix を除去した版を invoker 内で使う。
                    var stripped = StripShortcutSuffix(menuPath);

                    var descriptor = new CommandDescriptor(
                        path: commandPath,
                        description: $"Editor menu: {menuPath}",
                        aliases: Array.Empty<string>(),
                        parameters: Array.Empty<ParameterDescriptor>(),
                        returnType: typeof(void),
                        isAsync: false,
                        method: DummyMethod,
                        invoker: _ =>
                        {
                            EditorApplication.ExecuteMenuItem(stripped);
                            return null;
                        });
                    registry.Register(descriptor);
                    registered++;
                }
                // ノイズを避けるためサイレント。必要なら Debug.Log でカウント表示してもよい。
                _ = registered;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LiminalPalette] MenuItem auto-discovery failed: {ex.Message}");
            }
        }

        // TypeCache.GetMethodsWithAttribute<MenuItem> で全 MenuItem を収集し、
        // validate (条件チェック用ペア) と除外プレフィックスを除いてユニークなパスのみ返す。
        private static IEnumerable<string> DiscoverMenuItems()
        {
            var seen = new HashSet<string>();
            var methods = TypeCache.GetMethodsWithAttribute<MenuItem>();
            foreach (var m in methods)
            {
                MenuItem[] attrs;
                try
                {
                    attrs = (MenuItem[])m.GetCustomAttributes(typeof(MenuItem), inherit: false);
                }
                catch
                {
                    continue;
                }
                foreach (var a in attrs)
                {
                    if (a == null) continue;
                    if (a.validate) continue;
                    var path = a.menuItem;
                    if (string.IsNullOrEmpty(path)) continue;
                    if (ExcludePrefixes.Any(p => path.StartsWith(p, StringComparison.Ordinal))) continue;
                    if (seen.Add(path)) yield return path;
                }
            }
        }

        // "Edit/Cut %x" のような shortcut 修飾子を末尾から取り除く。
        // ExecuteMenuItem は shortcut なしのパスを期待するため必須の前処理。
        private static string StripShortcutSuffix(string menuPath)
        {
            var idx = menuPath.LastIndexOf(' ');
            if (idx < 0) return menuPath;
            var suffix = menuPath.Substring(idx + 1);
            if (suffix.Length == 0) return menuPath;
            var first = suffix[0];
            // % = Cmd/Ctrl, # = Shift, & = Alt, _ = no modifier
            if (first == '%' || first == '#' || first == '&' || first == '_') return menuPath.Substring(0, idx);
            return menuPath;
        }
    }
}
