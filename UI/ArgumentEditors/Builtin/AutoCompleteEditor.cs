using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// パラメータに動的/静的候補がある場合のオートコンプリートUI。
    /// TextField + 候補リスト構成。入力でフィルタ、クリックでvalueを確定。
    /// 候補が1件の場合、PaletteViewからTryCompleteを呼ぶとEnterで自動確定できる。
    ///
    /// 選択の移動は「打って絞り込む」で代替する方針 (パレット全体でキーボードのホームポジションから
    /// 手を離さずに操作できるようにするため)。enum もこの UI に載せ、確定時に文字列を enum へ変換する。
    /// </summary>
    internal sealed class AutoCompleteEditor
    {
        private const int MaxVisibleItems = 8;
        private const string SuggestionListClass = "lp-autocomplete-list";
        private const string SuggestionItemClass = "lp-autocomplete-item";

        /// <summary>
        /// rootのuserDataに格納するキー。PaletteViewからアクセスする。
        /// </summary>
        internal const string TryCompleteKey = "lp-autocomplete-try-complete";

        public VisualElement Build(ParameterDescriptor param, Action<object> onChanged)
            => Build(param, onChanged, InitialTextForString(param), v => v);

        /// <summary>
        /// 初期表示テキストと、確定値の変換方法を差し替えられる版。
        /// enum パラメータを同じ UI に載せるために使う (文字列 → enum に変換して返す)。
        /// <paramref name="convert"/> が null を返した値は「まだ確定していない」とみなして通知しない。
        /// </summary>
        internal VisualElement Build(
            ParameterDescriptor param,
            Action<object> onChanged,
            string initialText,
            Func<string, object> convert)
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            // 変換できない中途半端な入力では通知しない (enum の打ちかけで型エラーにしないため)
            void Notify(string raw)
            {
                var converted = convert(raw);
                if (converted != null) onChanged(converted);
            }

            var field = new TextField
            {
                value = initialText
            };

            var suggestionList = new ScrollView(ScrollViewMode.Vertical);
            suggestionList.AddToClassList(SuggestionListClass);
            suggestionList.style.maxHeight = MaxVisibleItems * 22;
            suggestionList.style.display = DisplayStyle.None;

            // フィルタ後の先頭候補を保持（候補リスト表示中のみ有効）
            string topMatchValue = null;

            // テキスト変更時にフィルタ + onChanged
            field.RegisterValueChangedCallback(e =>
            {
                Notify(e.newValue);
                topMatchValue = RebuildSuggestions(suggestionList, field, param, e.newValue, Notify);
            });

            // フォーカス取得時に候補表示
            field.RegisterCallback<FocusInEvent>(_ =>
                topMatchValue = RebuildSuggestions(suggestionList, field, param, field.value, Notify));

            // フォーカス喪失時に候補非表示（少し遅延してクリックを拾えるようにする）
            field.RegisterCallback<FocusOutEvent>(_ =>
                field.schedule.Execute(() => suggestionList.style.display = DisplayStyle.None).ExecuteLater(150));

            // PaletteViewからEnter時に呼ばれる補完確定関数
            // 戻り値: 補完が実行されたらtrue
            Func<bool> tryComplete = () =>
            {
                if (topMatchValue == null || suggestionList.style.display == DisplayStyle.None) return false;
                field.SetValueWithoutNotify(topMatchValue);
                Notify(topMatchValue);
                suggestionList.style.display = DisplayStyle.None;
                topMatchValue = null;
                return true;
            };

            root.userData = tryComplete;

            root.Add(field);
            root.Add(suggestionList);
            return root;
        }

        /// <summary>
        /// 候補リストを再構築する。候補が1件だけならそのvalueを返す。
        /// </summary>
        private static string RebuildSuggestions(
            ScrollView list, TextField field, ParameterDescriptor param,
            string filter, Action<string> notify)
        {
            list.Clear();

            var choices = GetChoiceItems(param);
            if (choices == null || choices.Count == 0)
            {
                list.style.display = DisplayStyle.None;
                return null;
            }

            var filterLower = (filter ?? "").ToLowerInvariant();
            var matchedItems = new List<ChoiceItem>();

            for (var i = 0; i < choices.Count; i++)
            {
                var item = choices[i];
                // value と displayName の両方でフィルタ
                if (filterLower.Length > 0
                    && !item.Value.ToLowerInvariant().Contains(filterLower)
                    && !item.DisplayName.ToLowerInvariant().Contains(filterLower))
                    continue;

                matchedItems.Add(item);
            }

            foreach (var item in matchedItems)
            {
                // 表示: "日本語名 (内部値)" or 同じなら値のみ
                var labelText = item.DisplayName != item.Value
                    ? $"{item.DisplayName} ({item.Value})"
                    : item.Value;

                var label = new Label(labelText);
                label.AddToClassList(SuggestionItemClass);
                label.style.cursor = StyleKeyword.None;
                label.style.paddingLeft = 4;
                label.style.paddingRight = 4;
                label.style.paddingTop = 2;
                label.style.paddingBottom = 2;

                // ホバーハイライト
                label.RegisterCallback<MouseEnterEvent>(_ =>
                    label.style.backgroundColor = new Color(0.3f, 0.5f, 0.8f, 0.4f));
                label.RegisterCallback<MouseLeaveEvent>(_ =>
                    label.style.backgroundColor = Color.clear);

                // クリックでvalueを確定
                var capturedValue = item.Value;
                label.RegisterCallback<MouseDownEvent>(e =>
                {
                    field.SetValueWithoutNotify(capturedValue);
                    notify(capturedValue);
                    list.style.display = DisplayStyle.None;
                    e.StopPropagation();
                });

                list.Add(label);
            }

            list.style.display = matchedItems.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            // 候補があれば先頭のvalueを返す
            return matchedItems.Count > 0 ? matchedItems[0].Value : null;
        }

        // string パラメータの初期テキスト。既定値が無ければ空。
        private static string InitialTextForString(ParameterDescriptor param)
            => param.HasDefault ? param.DefaultValue as string ?? "" : "";

        private static IReadOnlyList<ChoiceItem> GetChoiceItems(ParameterDescriptor param)
        {
            // 動的候補優先
            if (param.DynamicChoices != null)
            {
                try { return param.DynamicChoices(); }
                catch { return null; }
            }

            // 静的Choicesをフォールバック
            if (param.Choices.Count > 0)
            {
                var items = new ChoiceItem[param.Choices.Count];
                for (var i = 0; i < param.Choices.Count; i++)
                    items[i] = new ChoiceItem(param.Choices[i]);
                return items;
            }

            return null;
        }
    }
}
