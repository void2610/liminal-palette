using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// string パラメータに動的/静的候補がある場合のオートコンプリートUI。
    /// TextField + 候補リスト構成。入力でフィルタ、クリックでvalueを確定。
    /// 候補が1件の場合、PaletteViewからTryCompleteを呼ぶとEnterで自動確定できる。
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
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            var field = new TextField
            {
                value = param.HasDefault ? (string)param.DefaultValue ?? "" : ""
            };

            var suggestionList = new ScrollView(ScrollViewMode.Vertical);
            suggestionList.AddToClassList(SuggestionListClass);
            suggestionList.style.maxHeight = MaxVisibleItems * 22;
            suggestionList.style.display = DisplayStyle.None;

            // フィルタ後の唯一の候補を保持
            string soleMatchValue = null;

            // テキスト変更時にフィルタ + onChanged
            field.RegisterValueChangedCallback(e =>
            {
                onChanged(e.newValue);
                soleMatchValue = RebuildSuggestions(suggestionList, field, param, e.newValue, onChanged);
            });

            // フォーカス取得時に候補表示
            field.RegisterCallback<FocusInEvent>(_ =>
                soleMatchValue = RebuildSuggestions(suggestionList, field, param, field.value, onChanged));

            // フォーカス喪失時に候補非表示（少し遅延してクリックを拾えるようにする）
            field.RegisterCallback<FocusOutEvent>(_ =>
                field.schedule.Execute(() => suggestionList.style.display = DisplayStyle.None).ExecuteLater(150));

            // PaletteViewからEnter時に呼ばれる補完確定関数
            // 戻り値: 補完が実行されたらtrue
            Func<bool> tryComplete = () =>
            {
                if (soleMatchValue == null) return false;
                field.SetValueWithoutNotify(soleMatchValue);
                onChanged(soleMatchValue);
                suggestionList.style.display = DisplayStyle.None;
                soleMatchValue = null;
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
            string filter, Action<object> onChanged)
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
                    onChanged(capturedValue);
                    list.style.display = DisplayStyle.None;
                    e.StopPropagation();
                });

                list.Add(label);
            }

            list.style.display = matchedItems.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            // 候補が1件だけならそのvalueを返す
            return matchedItems.Count == 1 ? matchedItems[0].Value : null;
        }

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
