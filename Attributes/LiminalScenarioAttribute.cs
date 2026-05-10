using System;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// メソッドをデバッグコンソールのシナリオ (コマンドチェイン) として公開する属性。
    /// 対象メソッドは <see cref="ScenarioStep"/> の列を返す必要がある (戻り値型は
    /// IEnumerable&lt;ScenarioStep&gt; もしくはそれを実装するコレクション)。
    ///
    /// インスタンスメソッドの場合は VContainer 経由で解決される (<see cref="LiminalCommandAttribute"/> と同じ流儀)。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class LiminalScenarioAttribute : Attribute
    {
        /// <summary>シナリオの識別子。"/" 区切りで階層を表現する (例: "Combat/EnemyTakesDamage")。</summary>
        public string Path { get; }

        /// <summary>UI / API 表示用の説明文。</summary>
        public string Description { get; set; } = "";

        public LiminalScenarioAttribute(string path) => Path = path;
    }
}
