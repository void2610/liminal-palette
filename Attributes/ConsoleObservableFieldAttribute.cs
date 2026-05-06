using System;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// プロパティ / フィールドを「読み取り専用の状態」として LiminalPalette UI と HTTP API に公開する属性。
    /// 戻り値は R3 の `ReactiveProperty<T>` または `Observable<T>` を想定。
    /// UI は選択コマンドの Path prefix と一致するこの属性を「Current values」セクションに表示し、
    /// R3 Subscribe で値変更時に自動更新する (polling 不要)。
    /// HTTP API は GET /api/v1/state?path=&lt;Path&gt; で現在値スナップショットを取得できる。
    ///
    /// 制約 (Phase 5a):
    ///   - インスタンスメソッドコマンドと同じく、所属クラスは VContainer に登録されている必要がある
    ///   - T (ReactiveProperty&lt;T&gt; の T) は TypeConverterRegistry.ToDisplayString で文字列化できる型
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class ConsoleObservableFieldAttribute : Attribute
    {
        /// <summary>状態の識別子。コマンド Path と同じ階層 ("/" 区切り) を使う想定。</summary>
        public string Path { get; }

        /// <summary>UI / API 表示用の説明文 (任意)。</summary>
        public string Description { get; set; } = "";

        public ConsoleObservableFieldAttribute(string path) => Path = path;
    }
}
