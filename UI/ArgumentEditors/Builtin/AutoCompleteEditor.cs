using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// string パラメータに動的/静的候補がある場合のオートコンプリートUI。
    /// TextField + 候補リスト構成。入力でフィルタ、↑↓ で選択、Enter またはクリックで確定。
    ///
    /// パレットは Enter を「次へ / 実行」に使い、NavigationSubmit / NavigationMove も root で
    /// 握り潰しているため、候補の選択はここで KeyDownEvent を直接拾ってキーボードだけで完結させる。
    /// </summary>
    internal sealed class AutoCompleteEditor
    {
        private const int MaxVisibleItems = 8;
        private const string SuggestionListClass = "lp-autocomplete-list";
        private const string SuggestionItemClass = "lp-autocomplete-item";

        private static readonly Color HighlightColor = new Color(0.3f, 0.5f, 0.8f, 0.4f);

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

            // フィルタ後の候補と、↑↓ で動かす選択位置。リスト表示中のみ有効。
            var matched = new List<string>();
            var highlight = 0;

            void ApplyHighlight()
            {
                for (var i = 0; i < suggestionList.childCount; i++)
                {
                    suggestionList[i].style.backgroundColor = i == highlight ? HighlightColor : Color.clear;
                }
                // 選択が見えないと ↑↓ の意味が無いのでスクロールを追従させる。
                // panel に載っていない (テスト等でレイアウトが無い) 場合は何もしない。
                if (suggestionList.panel != null
                    && highlight >= 0 && highlight < suggestionList.childCount)
                {
                    suggestionList.ScrollTo(suggestionList[highlight]);
                }
            }

            void Rebuild(string filter)
            {
                RebuildSuggestions(suggestionList, field, param, filter, onChanged, matched,
                    i => { highlight = i; ApplyHighlight(); });
                highlight = 0;
                ApplyHighlight();
            }

            field.RegisterValueChangedCallback(e =>
            {
                onChanged(e.newValue);
                Rebuild(e.newValue);
            });

            // フォーカス取得時に候補表示
            field.RegisterCallback<FocusInEvent>(_ => Rebuild(field.value));

            // フォーカス喪失時に候補非表示（少し遅延してクリックを拾えるようにする）
            field.RegisterCallback<FocusOutEvent>(_ =>
                field.schedule.Execute(() => suggestionList.style.display = DisplayStyle.None).ExecuteLater(150));

            // ↑↓ で候補を移動する。TextField のキャレット移動より優先する
            // (候補が出ている間は上下 = 候補選択、という方が直感に合う)。
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (suggestionList.style.display == DisplayStyle.None || matched.Count == 0) return;

                int delta;
                switch (evt.keyCode)
                {
                    case KeyCode.UpArrow:
                        delta = -1;
                        break;
                    case KeyCode.DownArrow:
                        delta = 1;
                        break;
                    default:
                        return;
                }

                highlight = ((highlight + delta) % matched.Count + matched.Count) % matched.Count;
                ApplyHighlight();
                evt.StopImmediatePropagation();
                evt.PreventDefault();
            });

            // PaletteViewからEnter時に呼ばれる補完確定関数
            // 戻り値: 補完が実行されたらtrue
            Func<bool> tryComplete = () =>
            {
                if (suggestionList.style.display == DisplayStyle.None) return false;
                if (highlight < 0 || highlight >= matched.Count) return false;

                var value = matched[highlight];
                field.SetValueWithoutNotify(value);
                onChanged(value);
                suggestionList.style.display = DisplayStyle.None;
                matched.Clear();
                return true;
            };

            root.userData = tryComplete;

            root.Add(field);
            root.Add(suggestionList);
            return root;
        }

        /// <summary>
        /// 候補リストを再構築し、絞り込み後の value を <paramref name="matched"/> に詰める。
        /// </summary>
        private static void RebuildSuggestions(
            ScrollView list, TextField field, ParameterDescriptor param,
            string filter, Action<object> onChanged, List<string> matched, Action<int> onHover)
        {
            list.Clear();
            matched.Clear();

            var choices = GetChoiceItems(param);
            if (choices == null || choices.Count == 0)
            {
                list.style.display = DisplayStyle.None;
                return;
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
                matched.Add(item.Value);

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

                // ホバーでも選択位置を動かす。キーボードとマウスで「選択中」の概念を 1 つにする。
                var index = matched.Count - 1;
                label.RegisterCallback<MouseEnterEvent>(_ => onHover(index));

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
