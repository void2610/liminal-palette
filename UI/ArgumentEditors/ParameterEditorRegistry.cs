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

        private static readonly AutoCompleteEditor _autoCompleteEditor = new AutoCompleteEditor();

        /// <summary>
        /// ParameterDescriptor を見て最適なエディタを返す。
        /// 候補を持つ string / enum は AutoCompleteEditor を優先する。
        /// </summary>
        public static IParameterEditor Resolve(ParameterDescriptor param)
        {
            if (param.Type == typeof(string)
                && (param.DynamicChoices != null || param.Choices.Count > 0))
            {
                return new AutoCompleteEditorAdapter(_autoCompleteEditor, param);
            }

            // enum は同じ「打って絞り込む」UI に載せる。EnumField のドロップダウンも
            // EnumFlagsField も Toggle 列も、いずれもマウス無しでは操作できず、
            // パレットのキーボード操作の前提から外れるため。
            if (param.Type != null && param.Type.IsEnum)
            {
                return new EnumAutoCompleteAdapter(_autoCompleteEditor, param);
            }

            return Resolve(param.Type);
        }

        private static bool IsFlagsEnum(Type t)
            => t != null && t.IsEnum && t.IsDefined(typeof(FlagsAttribute), inherit: false);

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

        /// <summary>
        /// AutoCompleteEditor を IParameterEditor として扱うアダプタ。
        /// ParameterDescriptor を保持し、Build時に渡す。
        /// </summary>
        private sealed class AutoCompleteEditorAdapter : IParameterEditor
        {
            private readonly AutoCompleteEditor _editor;
            private readonly ParameterDescriptor _param;

            public AutoCompleteEditorAdapter(AutoCompleteEditor editor, ParameterDescriptor param)
            {
                _editor = editor;
                _param = param;
            }

            public bool CanHandle(Type type) => type == typeof(string);

            public UnityEngine.UIElements.VisualElement Build(ParameterDescriptor param, Action<object> onChanged)
                => _editor.Build(_param, onChanged);
        }

        /// <summary>
        /// enum を AutoCompleteEditor に載せるアダプタ。
        /// 表示は候補名、確定値は enum に戻して渡す (型付き実行経路がそのまま使えるように)。
        /// </summary>
        private sealed class EnumAutoCompleteAdapter : IParameterEditor
        {
            private readonly AutoCompleteEditor _editor;
            private readonly ParameterDescriptor _param;

            public EnumAutoCompleteAdapter(AutoCompleteEditor editor, ParameterDescriptor param)
            {
                _editor = editor;
                _param = param;
            }

            public bool CanHandle(Type type) => type != null && type.IsEnum;

            public UnityEngine.UIElements.VisualElement Build(ParameterDescriptor param, Action<object> onChanged)
                => _editor.Build(
                    _param,
                    onChanged,
                    InitialText(_param),
                    raw => Parse(_param.Type, raw),
                    // [Flags] は値を続けて足せるよう、カンマ区切りの区画単位で確定する
                    IsFlagsEnum(_param.Type) ? new AutoCompleteEditor.MultiValueTextPolicy() : null);

            // 既定値があればその名前、無ければ先頭の値の名前を初期表示にする。
            private static string InitialText(ParameterDescriptor param)
            {
                if (param.HasDefault && param.DefaultValue != null)
                {
                    return param.DefaultValue.ToString();
                }
                var values = Enum.GetValues(param.Type);
                return values.Length > 0 ? values.GetValue(0).ToString() : "";
            }

            // 打ちかけの文字列は null を返して「まだ確定していない」ことを示す。
            // [Flags] は "Fire, Ice" のようなカンマ区切りを Enum.Parse がそのまま解釈する。
            // 末尾の区切りは打ちかけなので落としてから渡す。
            private static object Parse(Type enumType, string raw)
            {
                var text = (raw ?? "").Trim().TrimEnd(',').Trim();
                if (text.Length == 0) return null;
                try
                {
                    return Enum.Parse(enumType, text, ignoreCase: true);
                }
                catch (ArgumentException)
                {
                    return null;
                }
                catch (OverflowException)
                {
                    return null;
                }
            }
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
