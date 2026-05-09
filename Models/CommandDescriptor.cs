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
        /// Editor 専用コマンドかどうか。true の場合、Play Mode / Player ビルドのランタイムパレット UI からは
        /// 表示対象から除外される (Unity の [MenuItem] を自動収集した "Menu/..." 系などが該当)。
        /// レジストリ登録自体は共通なので Editor 側 Window では引き続き見える。
        /// </summary>
        public bool IsEditorOnly { get; }

        public CommandDescriptor(
            string path,
            string description,
            IReadOnlyList<string> aliases,
            IReadOnlyList<ParameterDescriptor> parameters,
            Type returnType,
            bool isAsync,
            MethodInfo method)
            : this(path, description, aliases, parameters, returnType, isAsync, method, null, false)
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
            : this(path, description, aliases, parameters, returnType, isAsync, method, invoker, false)
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
            Func<object[], object> invoker,
            bool isEditorOnly)
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
            IsEditorOnly = isEditorOnly;
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
