using System;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// メソッドをデバッグコンソールのコマンドとして公開する属性。
    /// Phase 1 では static メソッドのみ対象。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class ConsoleCommandAttribute : Attribute
    {
        /// <summary>
        /// コマンドのパス。"/" 区切りで階層を表現する (例: "Player/Health/Set")。
        /// 空文字や末尾スラッシュは Scanner で例外として扱う。
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// UI / CLI で表示するコマンドの説明。
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// 別名のリスト。Path と同じく "/" 区切りで指定可能。
        /// </summary>
        public string[] Aliases { get; set; } = Array.Empty<string>();

        public ConsoleCommandAttribute(string path)
        {
            Path = path;
        }
    }
}
