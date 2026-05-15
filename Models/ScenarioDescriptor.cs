using System;
using System.Collections.Generic;
using System.Reflection;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// シナリオ 1 件の不変メタデータ。レジストリに格納される単位。
    /// </summary>
    public sealed class ScenarioDescriptor
    {
        /// <summary>"/" 区切りのフルパス。</summary>
        public string Path { get; }

        /// <summary>UI / API 表示用の説明文。</summary>
        public string Description { get; }

        /// <summary>所属型 (= IInstanceResolver.Resolve に渡す型)。static メソッドの場合も DeclaringType を保持。</summary>
        public Type DeclaringType { get; }

        /// <summary>呼び出し対象のメソッド情報。</summary>
        public MethodInfo Method { get; }

        /// <summary>true なら static メソッド。false ならインスタンスメソッドで InstanceResolver が必要。</summary>
        public bool IsStatic { get; }

        /// <summary>
        /// 任意のシーン名。空でなければ ScenarioExecutor がシナリオ本体ステップの前に
        /// LoadScene ステップを自動で差し込む (PlayMode のみ有効)。詳細は
        /// <see cref="LiminalScenarioAttribute.Scene"/> 参照。
        /// </summary>
        public string Scene { get; }

        /// <summary>
        /// インスタンス (static の場合は null) を受け取って ScenarioStep の列を返すファクトリ。
        /// 列挙のたびに新しい列が作られるため、シナリオを連続実行しても各回で副作用が発火する仕様。
        /// </summary>
        public Func<object, IEnumerable<ScenarioStep>> StepsFactory { get; }

        public ScenarioDescriptor(
            string path,
            string description,
            Type declaringType,
            MethodInfo method,
            bool isStatic,
            Func<object, IEnumerable<ScenarioStep>> stepsFactory,
            string scene = "")
        {
            Path = path;
            Description = description ?? "";
            DeclaringType = declaringType;
            Method = method;
            IsStatic = isStatic;
            Scene = scene ?? "";
            StepsFactory = stepsFactory ?? throw new ArgumentNullException(nameof(stepsFactory));
        }
    }
}
