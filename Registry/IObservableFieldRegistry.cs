using System.Collections.Generic;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// [LiminalObservableField] で公開された状態フィールドのレジストリ。
    /// CommandRegistry と同じく プロセス共有の static シングルトン (ObservableFieldRegistry.Default)。
    /// </summary>
    public interface IObservableFieldRegistry
    {
        /// <summary>登録された全フィールド (登録順)。</summary>
        IReadOnlyList<ObservableFieldDescriptor> All { get; }

        /// <summary>Path で完全一致検索 (大小無視)。</summary>
        ObservableFieldDescriptor Find(string path);

        /// <summary>Path prefix で前方一致検索 (大小無視)。UI が選択コマンドに対する関連 Field を探すのに使う。</summary>
        IReadOnlyList<ObservableFieldDescriptor> FindByPathPrefix(string prefix);
    }
}
