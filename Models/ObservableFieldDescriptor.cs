using System;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// [LiminalObservableField] が付与されたプロパティ / フィールドの不変メタデータ。
    /// AttributeScanner が生成して ObservableFieldRegistry に登録する。
    /// </summary>
    public sealed class ObservableFieldDescriptor
    {
        public string Path { get; }
        public string Description { get; }

        /// <summary>所属型 (= IInstanceResolver.Resolve に渡す型)。IsStatic=true の場合は読み取りに使われない。</summary>
        public Type DeclaringType { get; }

        /// <summary>値の型 T (ReactiveProperty&lt;T&gt; / Observable&lt;T&gt; の T)。</summary>
        public Type ValueType { get; }

        /// <summary>
        /// 静的メンバー (static property / static field) かどうか。
        /// true なら UI / HTTP は IInstanceResolver を経由せず、ReadCurrent / Subscribe に null を渡せる。
        /// 用途: 組み込み Time/Scale など、所属クラスが static utility のケースを VContainer 登録なしで扱う。
        /// </summary>
        public bool IsStatic { get; }

        /// <summary>
        /// インスタンスから現在値を取り出す関数。
        /// ReactiveProperty&lt;T&gt; の場合は Value (現在値) を返す。
        /// Observable&lt;T&gt; 単体の場合は値を保持しないため常に null を返す
        /// (Phase 5a では last observed value のキャッシュは行わない)。
        /// </summary>
        public Func<object, object> ReadCurrent { get; }

        /// <summary>
        /// インスタンスに対して Subscribe を実行する関数。
        /// onNext は値が来た時に呼ばれる。返り値の IDisposable で購読解除可能。
        /// </summary>
        public Func<object, Action<object>, IDisposable> Subscribe { get; }

        public ObservableFieldDescriptor(
            string path,
            string description,
            Type declaringType,
            Type valueType,
            Func<object, object> readCurrent,
            Func<object, Action<object>, IDisposable> subscribe,
            bool isStatic = false)
        {
            Path = path;
            Description = description ?? "";
            DeclaringType = declaringType;
            ValueType = valueType;
            ReadCurrent = readCurrent;
            Subscribe = subscribe;
            IsStatic = isStatic;
        }
    }
}
