using System;
using System.Collections.Generic;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// [LiminalScenario] が付与されたメソッド由来のシナリオを保持するレジストリ。
    /// CommandRegistry / ObservableFieldRegistry と同じく Default シングルトンを持つ。
    /// </summary>
    public interface IScenarioRegistry
    {
        /// <summary>登録された全シナリオ (登録順)。</summary>
        IReadOnlyList<ScenarioDescriptor> All { get; }

        /// <summary>Path で完全一致検索 (大小無視)。</summary>
        ScenarioDescriptor Find(string path);

        /// <summary>登録通知。</summary>
        event Action<ScenarioDescriptor> Registered;

        /// <summary>1 件登録する。同一 Path が既に存在する場合は警告ログを残して上書き。</summary>
        void Register(ScenarioDescriptor descriptor);

        /// <summary>全件削除。テスト用。</summary>
        void Clear();
    }
}
