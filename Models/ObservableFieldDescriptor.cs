using System;

namespace Void2610.LiminalPalette
{
    /// <summary>
    /// [ConsoleObservableField] が付与されたプロパティ / フィールドの不変メタデータ。
    /// AttributeScanner が生成して ObservableFieldRegistry に登録する。
    /// </summary>
    public sealed class ObservableFieldDescriptor
    {
        public string Path { get; }
        public string Description { get; }

        /// <summary>所属型 (= IInstanceResolver.Resolve に渡す型)。</summary>
        public Type DeclaringType { get; }

        /// <summary>値の型 T (ReactiveProperty&lt;T&gt; / Observable&lt;T&gt; の T)。</summary>
        public Type ValueType { get; }

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
            Func<object, Action<object>, IDisposable> subscribe)
        {
            Path = path;
            Description = description ?? "";
            DeclaringType = declaringType;
            ValueType = valueType;
            ReadCurrent = readCurrent;
            Subscribe = subscribe;
        }
    }
}
