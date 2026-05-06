using System;
using System.Collections.Generic;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// コマンドメタデータの保管・検索インタフェース。
    /// Phase 1 では完全一致検索のみ。ファジー検索は Phase 2 でこの上のレイヤーとして実装する。
    /// </summary>
    public interface ICommandRegistry
    {
        /// <summary>登録済みコマンドの一覧 (登録順)。</summary>
        IReadOnlyList<CommandDescriptor> All { get; }

        /// <summary>パスまたは別名で完全一致検索。大文字小文字を区別しない。見つからなければ null。</summary>
        CommandDescriptor? Find(string pathOrAlias);

        /// <summary>カテゴリプレフィックスに一致するコマンドを返す (例: "Player" → "Player/..." 全件)。</summary>
        IEnumerable<CommandDescriptor> FindByCategory(string categoryPrefix);

        /// <summary>コマンドを登録する。同一パスが既にあれば警告ログを出して上書きする。</summary>
        void Register(CommandDescriptor descriptor);

        /// <summary>パスまたは別名で削除する。削除できたら true。</summary>
        bool Unregister(string pathOrAlias);

        /// <summary>全登録を削除する (テスト・再スキャン向け)。</summary>
        void Clear();

        /// <summary>新規登録時に発火。UI が動的にリストを更新するためのフック。</summary>
        event Action<CommandDescriptor> Registered;

        /// <summary>登録解除時に発火。</summary>
        event Action<CommandDescriptor> Unregistered;
    }
}
