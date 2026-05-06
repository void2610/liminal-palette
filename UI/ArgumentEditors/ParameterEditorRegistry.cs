using System;
using System.Collections.Generic;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// IParameterEditor を集約し、Type に対応する最適なエディタを返すレジストリ。
    /// Phase 1 の TypeConverterRegistry と完全に同じ流儀: 後から Register したものが優先、
    /// internal な ResetToDefaults() でテスト間の状態リセット可能、最終手段として FallbackTextEditor が常に解決を返す。
    /// </summary>
    public static class ParameterEditorRegistry
    {
        private static readonly List<IParameterEditor> _editors = new List<IParameterEditor>();
        private static readonly object _lock = new object();

        static ParameterEditorRegistry()
        {
            RegisterDefaults();
        }

        // 標準エディタの登録。Register は先頭挿入なので、最初に Register したものが末尾 (最低優先) になる。
        // FallbackTextEditor を最初に登録して常に末尾に置き、最後の保険として機能させる。
        // 注: Color / UnityEngine.Object / Flags enum 用のエディタは UnityEditor.UIElements 限定なので
        // ここでは登録せず、Editor asmdef 側で [InitializeOnLoadMethod] により追加登録する。
        private static void RegisterDefaults()
        {
            Register(new FallbackTextEditor());
            Register(new PrimitiveEditor());
            Register(new EnumEditor());
            Register(new VectorEditor());
        }

        /// <summary>エディタを登録する。新しく登録したものが既存より優先される。</summary>
        public static void Register(IParameterEditor editor)
        {
            if (editor == null) throw new ArgumentNullException(nameof(editor));
            lock (_lock)
            {
                _editors.Insert(0, editor);
            }
        }

        /// <summary>type を扱える最初のエディタを返す。Fallback が末尾にあるため null は返らない。</summary>
        public static IParameterEditor Resolve(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            lock (_lock)
            {
                for (var i = 0; i < _editors.Count; i++)
                {
                    if (_editors[i].CanHandle(type)) return _editors[i];
                }
            }
            // 通常ここには到達しない (FallbackTextEditor が CanHandle = true を返すため)。
            // 万一テストで Clear だけ呼んで標準を再登録しなかった場合の防御。
            throw new InvalidOperationException(
                $"No IParameterEditor registered for {type.Name}. Did you call Clear() without Register()?");
        }

        /// <summary>登録済みエディタをすべて削除する (テスト向け)。Reset 後は ResetToDefaults() で再登録すること。</summary>
        internal static void Clear()
        {
            lock (_lock)
            {
                _editors.Clear();
            }
        }

        /// <summary>標準エディタだけが登録された初期状態にリセットする (テスト向け)。</summary>
        internal static void ResetToDefaults()
        {
            Clear();
            RegisterDefaults();
        }
    }
}
