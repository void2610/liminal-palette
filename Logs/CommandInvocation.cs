using System;
using System.Collections.Generic;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// パレット経由で実行されたコマンド 1 回分の不変記録。
    /// Log タブの詳細表示と History タブの再実行に必要な情報をすべて持つ。
    /// </summary>
    public sealed class CommandInvocation
    {
        public string Path { get; }
        /// <summary>実行時に Executor へ渡された型解決済み引数 (key = パラメータ名)。再実行時にそのまま使える。</summary>
        public IReadOnlyDictionary<string, object> Args { get; }
        public CommandResult Result { get; }
        public DateTime TimestampUtc { get; }

        /// <summary>
        /// 実行経路。History タブは <see cref="InvocationOrigin.User"/> のみを再実行候補として並べ、
        /// Log タブはデバッグ用途で全経路を表示する。
        /// シナリオ前提の状態を要求する個別ステップや、自動化ツールが大量に叩くコマンドを
        /// 手打ち履歴と同列に扱うと、再実行候補としても保持枠としても手打ちの記録を潰してしまう。
        /// </summary>
        public InvocationOrigin Origin { get; }

        /// <summary>手動実行以外 (Ipc / Scenario) 由来かどうか。保持枠の振り分けに使う。</summary>
        public bool IsAutomated => Origin != InvocationOrigin.User;

        /// <summary>シナリオ実行由来かどうか。</summary>
        public bool IsFromScenario => Origin == InvocationOrigin.Scenario;

        /// <summary>
        /// 保存から復元したエントリか。復元時は引数が文字列に落ちているので、
        /// 再実行は型付き経路ではなく文字列経路 (TypeConverter 経由) を通す必要がある。
        /// ログ / スタックトレースも保存していないため空になる。
        /// </summary>
        public bool IsRestored { get; }

        public CommandInvocation(string path, IReadOnlyDictionary<string, object> args, CommandResult result, DateTime timestampUtc)
            : this(path, args, result, timestampUtc, InvocationOrigin.User)
        {
        }

        /// <summary>互換用オーバーロード。true は <see cref="InvocationOrigin.Scenario"/> として扱う。</summary>
        public CommandInvocation(string path, IReadOnlyDictionary<string, object> args, CommandResult result, DateTime timestampUtc, bool isFromScenario)
            : this(path, args, result, timestampUtc, isFromScenario ? InvocationOrigin.Scenario : InvocationOrigin.User)
        {
        }

        public CommandInvocation(string path, IReadOnlyDictionary<string, object> args, CommandResult result, DateTime timestampUtc, InvocationOrigin origin)
            : this(path, args, result, timestampUtc, origin, false)
        {
        }

        internal CommandInvocation(string path, IReadOnlyDictionary<string, object> args, CommandResult result, DateTime timestampUtc, InvocationOrigin origin, bool restored)
        {
            IsRestored = restored;
            Path = path ?? "";
            Args = args ?? new Dictionary<string, object>();
            Result = result;
            TimestampUtc = timestampUtc;
            Origin = origin;
        }
    }
}
