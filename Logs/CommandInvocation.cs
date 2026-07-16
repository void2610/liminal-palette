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
        /// シナリオ実行 (各ステップ + シナリオ集約) 由来のエントリかどうか。
        /// History タブは「過去に直接実行したコマンドを同じ引数で再実行する」ことを目的とするため、
        /// シナリオ前提の状態を要求する個別ステップを再実行候補として並べると UX として混乱する。
        /// → History タブはこのフラグが true のエントリを除外する。
        /// Log タブはデバッグ用途で全件表示する (シナリオ由来も詳細閲覧したいケースがあるため)。
        /// </summary>
        public bool IsFromScenario { get; }

        public CommandInvocation(string path, IReadOnlyDictionary<string, object> args, CommandResult result, DateTime timestampUtc)
            : this(path, args, result, timestampUtc, isFromScenario: false)
        {
        }

        public CommandInvocation(string path, IReadOnlyDictionary<string, object> args, CommandResult result, DateTime timestampUtc, bool isFromScenario)
        {
            Path = path ?? "";
            Args = args ?? new Dictionary<string, object>();
            Result = result;
            TimestampUtc = timestampUtc;
            IsFromScenario = isFromScenario;
        }
    }
}
