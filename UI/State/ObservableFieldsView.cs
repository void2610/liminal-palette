using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// 選択コマンドの Path prefix と一致する [LiminalObservableField] を「Current values」セクションに表示する VisualElement。
    /// Show(prefix) で関連 Field を購読し、値変更時にラベルを push 駆動で更新する (polling 不要)。
    /// 別 prefix への切替時 / Detach 時に全購読を Dispose してリークを防ぐ。
    ///
    /// インスタンス解決に IInstanceResolver を経由するため、resolver 未登録 (NullInstanceResolver のまま) の
    /// プロジェクトでは関連 Field があっても表示されない (= 値を取れないため)。
    /// </summary>
    public sealed class ObservableFieldsView : VisualElement
    {
        // 現在 active な購読をフィールドごとに保持。Path をキーに使う。
        private readonly Dictionary<string, IDisposable> _subscriptions = new Dictionary<string, IDisposable>(StringComparer.OrdinalIgnoreCase);
        private readonly IObservableFieldRegistry _registry;

        // 直近 ShowFor に使った prefix。同じ prefix で再呼出されたら no-op にできる。
        private string _currentPrefix;

        public ObservableFieldsView(IObservableFieldRegistry registry = null)
        {
            _registry = registry ?? ObservableFieldRegistry.Default;
            AddToClassList("palette-observable-fields");
            // 既定は非表示 (関連フィールドが見つかった時だけ Flex に切替)。
            style.display = DisplayStyle.None;
            RegisterCallback<DetachFromPanelEvent>(_ => DisposeAllSubscriptions());
        }

        /// <summary>
        /// 選択コマンドの Path prefix にマッチする ObservableField を表示する。
        /// 例: ShowFor("Player/Health/Set") → "Player/Health" 等の Field を引っ張る。
        /// 引数末尾の "/Set" などは除去せず、prefix で StartsWith マッチさせる。
        /// </summary>
        public void ShowFor(string commandPath)
        {
            if (string.IsNullOrEmpty(commandPath))
            {
                Hide();
                return;
            }

            // prefix の取り方: "Player/Health/Set" → "Player/Health" を生成して prefix 検索。
            // 単純化: コマンド Path の親階層 (= 最後の "/" の前まで) を prefix とする。
            var lastSlash = commandPath.LastIndexOf('/');
            var prefix = lastSlash > 0 ? commandPath.Substring(0, lastSlash) : commandPath;

            if (string.Equals(prefix, _currentPrefix, StringComparison.OrdinalIgnoreCase)) return;
            _currentPrefix = prefix;

            DisposeAllSubscriptions();
            Clear();

            var matches = _registry.FindByPathPrefix(prefix);
            if (matches.Count == 0)
            {
                Hide();
                return;
            }

            // ヘッダ
            var header = new Label("Current values");
            header.AddToClassList("palette-observable-fields-header");
            Add(header);

            // 各フィールド
            for (var i = 0; i < matches.Count; i++)
            {
                var d = matches[i];
                var row = new VisualElement();
                row.AddToClassList("palette-observable-fields-row");
                // 初期テキストは下で確定する。プレースホルダ "(no value)" は ipc.md の挙動と揃える
                // (Observable<T> 単体 / 未解決時は値が即時に得られないため)。
                var label = new Label();
                label.AddToClassList("palette-observable-fields-value");
                row.Add(label);
                Add(row);

                // インスタンス解決
                var instance = LiminalPalette.InstanceResolver.Resolve(d.DeclaringType);
                if (instance == null)
                {
                    // 未解決時は "(not resolved)" 表示にとどめる。利用者が VContainer 設定漏れに気付ける。
                    label.text = $"{d.Path}: (instance not resolved)";
                    continue;
                }

                // 初期表示: ReadCurrent で現在値を一度読み、Subscribe より前にラベルを埋める。
                // ReactiveProperty<T> なら Value が取れるが、Observable<T> 単体は null が返る (ObservableFieldDescriptor 仕様)。
                // null は "(no value)" に統一して "(no value)" / "(instance not resolved)" / 値文字列の 3 状態にまとめる。
                label.text = $"{d.Path}: {FormatInitial(d.ReadCurrent(instance))}";

                // Subscribe して値変更を push 受信。
                try
                {
                    var sub = d.Subscribe(instance, v => label.text = $"{d.Path}: {Format(v)}");
                    _subscriptions[d.Path] = sub;
                }
                catch (Exception ex)
                {
                    label.text = $"{d.Path}: (subscribe failed: {ex.Message})";
                }
            }

            style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            DisposeAllSubscriptions();
            Clear();
            _currentPrefix = null;
            style.display = DisplayStyle.None;
        }

        private void DisposeAllSubscriptions()
        {
            foreach (var kv in _subscriptions)
            {
                try { kv.Value?.Dispose(); } catch { /* swallow */ }
            }
            _subscriptions.Clear();
        }

        private static string Format(object value)
        {
            if (value == null) return "null";
            // ToDisplayString で型に応じた整形 (Vector / Color はフォーマット済み文字列、その他は ToString)。
            return TypeConverterRegistry.ToDisplayString(value);
        }

        // 初期表示用フォーマッタ。Subscribe 後の onNext (Format) では null を "null" と表示するが、
        // 初期値段階で null になるのは「Observable<T> 単体で現在値を持たない」場合で意味が異なるため、
        // ipc.md の挙動と揃えて "(no value)" プレースホルダにする。
        private static string FormatInitial(object value)
        {
            if (value == null) return "(no value)";
            return TypeConverterRegistry.ToDisplayString(value);
        }
    }
}
