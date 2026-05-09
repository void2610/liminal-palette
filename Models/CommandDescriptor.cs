using System;
using System.Collections.Generic;
using System.Reflection;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// コマンド 1 件の不変メタデータ。レジストリに格納される単位。
    /// </summary>
    public sealed class CommandDescriptor
    {
        /// <summary>"/" 区切りのフルパス (例: "Player/Health/Set")。</summary>
        public string Path { get; }

        /// <summary>パスの最終セグメント (例: "Set")。</summary>
        public string Name { get; }

        /// <summary>パスの最終セグメントを除いたカテゴリ部分 (例: "Player/Health")。トップレベルなら空文字。</summary>
        public string Category { get; }

        /// <summary>コマンドの説明。</summary>
        public string Description { get; }

        /// <summary>登録された別名一覧。空配列で「別名なし」。</summary>
        public IReadOnlyList<string> Aliases { get; }

        /// <summary>引数メタデータ。順序はメソッドの宣言順。</summary>
        public IReadOnlyList<ParameterDescriptor> Parameters { get; }

        /// <summary>戻り値の型 (void なら typeof(void))。</summary>
        public Type ReturnType { get; }

        /// <summary>Task / Task&lt;T&gt; / ValueTask / ValueTask&lt;T&gt; のいずれかなら true。</summary>
        public bool IsAsync { get; }

        /// <summary>呼び出し対象の MethodInfo。Phase 1 では static メソッド限定。Invoker を使う場合はダミーでもよい。</summary>
        public MethodInfo Method { get; }

        /// <summary>
        /// 呼び出しを差し替えるためのオプショナルなデリゲート。
        /// non-null の場合、CommandExecutor は MethodInfo.Invoke の代わりにこれを呼ぶ。
        /// 動的に登録するコマンド (Unity の MenuItem 自動収集など、対応する static メソッドを持たないもの) で使用する。
        /// </summary>
        public Func<object[], object> Invoker { get; }

        /// <summary>
        /// Editor 専用コマンドかどうか。Path の prefix から自動判定する:
        ///   - "Editor/..."  : 利用側が手書きで [ConsoleCommand("Editor/...")] と宣言した Editor 専用コマンド
        ///   - "Menu/..."    : EditorMenuItemBootstrap が Unity の [MenuItem] から自動収集したコマンド
        /// true の場合、Play Mode / Player ビルドのランタイムパレット UI からは表示対象外。
        /// レジストリ登録自体は共通なので Editor 側 Window では引き続き見える。
        /// </summary>
        public bool IsEditorOnly => IsEditorOnlyPath(Path);

        public CommandDescriptor(
            string path,
            string description,
            IReadOnlyList<string> aliases,
            IReadOnlyList<ParameterDescriptor> parameters,
            Type returnType,
            bool isAsync,
            MethodInfo method)
            : this(path, description, aliases, parameters, returnType, isAsync, method, null)
        {
        }

        public CommandDescriptor(
            string path,
            string description,
            IReadOnlyList<string> aliases,
            IReadOnlyList<ParameterDescriptor> parameters,
            Type returnType,
            bool isAsync,
            MethodInfo method,
            Func<object[], object> invoker)
        {
            Path = path;
            Name = ExtractName(path);
            Category = ExtractCategory(path);
            Description = description ?? "";
            Aliases = aliases ?? Array.Empty<string>();
            Parameters = parameters ?? Array.Empty<ParameterDescriptor>();
            ReturnType = returnType;
            IsAsync = isAsync;
            Method = method;
            Invoker = invoker;
        }

        // Path の prefix で Editor 専用かどうかを判定する。Editor / Menu の 2 つは「自明に Editor 専用」として
        // 予約 prefix 扱いし、利用側はこの 2 つから選んで命名するだけでランタイムから自動的に隠れる。
        private static bool IsEditorOnlyPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return false;
            return p.StartsWith("Editor/", StringComparison.OrdinalIgnoreCase)
                || p.StartsWith("Menu/", StringComparison.OrdinalIgnoreCase);
        }

        // パスの末尾セグメント。"/" を含まないなら全体がそのまま Name となる。
        private static string ExtractName(string path)
        {
            var idx = path.LastIndexOf('/');
            return idx < 0 ? path : path.Substring(idx + 1);
        }

        // パスから末尾セグメントを除いた部分。"/" を含まないなら空文字。
        private static string ExtractCategory(string path)
        {
            var idx = path.LastIndexOf('/');
            return idx < 0 ? "" : path.Substring(0, idx);
        }
    }
}
