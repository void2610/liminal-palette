using System.Linq;
using UnityEngine.UIElements;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// CommandResult を表示する VisualElement。実行直後にパレット下部へ表示される。
    /// 成功 / 失敗のバッジ、戻り値、所要時間、ログ、エラー時のスタックトレースを含む。
    /// </summary>
    public sealed class ResultView : VisualElement
    {
        private readonly Label _badge;
        private readonly Label _duration;
        private readonly Label _value;
        private readonly Label _error;
        private readonly Foldout _stackTrace;
        private readonly Label _stackTraceText;
        private readonly Label _logs;

        public ResultView()
        {
            AddToClassList("palette-result-view");
            AddToClassList("palette-result-view-hidden");

            var statusRow = new VisualElement();
            statusRow.AddToClassList("palette-result-status");
            _badge = new Label();
            _duration = new Label();
            _duration.AddToClassList("palette-result-duration");
            statusRow.Add(_badge);
            statusRow.Add(_duration);
            Add(statusRow);

            _value = new Label();
            _value.AddToClassList("palette-result-value");
            Add(_value);

            _error = new Label();
            _error.AddToClassList("palette-result-error");
            Add(_error);

            _stackTrace = new Foldout { text = "Stack trace", value = false };
            _stackTraceText = new Label();
            _stackTraceText.style.whiteSpace = WhiteSpace.Normal;
            _stackTrace.Add(_stackTraceText);
            Add(_stackTrace);

            _logs = new Label();
            _logs.AddToClassList("palette-result-logs");
            Add(_logs);
        }

        /// <summary>結果を表示する。Success / Fail で表示要素を切り替える。</summary>
        public void Show(CommandResult result)
        {
            RemoveFromClassList("palette-result-view-hidden");

            _badge.RemoveFromClassList("palette-result-badge-success");
            _badge.RemoveFromClassList("palette-result-badge-error");
            if (result.Success)
            {
                _badge.text = "OK";
                _badge.AddToClassList("palette-result-badge-success");
            }
            else
            {
                _badge.text = "FAIL";
                _badge.AddToClassList("palette-result-badge-error");
            }

            _duration.text = $"{result.Duration.TotalMilliseconds:F2} ms";

            // 戻り値: TypeConverterRegistry を通すと Unity 型 (Vector3 など) も読みやすく整形される。
            if (result.Success && result.Value != null)
            {
                _value.text = TypeConverterRegistry.ToDisplayString(result.Value);
                _value.style.display = DisplayStyle.Flex;
            }
            else
            {
                _value.style.display = DisplayStyle.None;
            }

            // エラー文字列。
            if (!result.Success && !string.IsNullOrEmpty(result.Error))
            {
                _error.text = result.Error;
                _error.style.display = DisplayStyle.Flex;
            }
            else
            {
                _error.style.display = DisplayStyle.None;
            }

            // スタックトレースは例外がある時だけ折りたたみで表示。
            if (result.Exception != null)
            {
                _stackTraceText.text = result.Exception.StackTrace ?? "";
                _stackTrace.style.display = DisplayStyle.Flex;
            }
            else
            {
                _stackTrace.style.display = DisplayStyle.None;
                _stackTrace.value = false;
            }

            // ログ: 行数を抑えるため最初の MaxLogLines 件まで表示し、超過分は省略件数を末尾に明示する。
            // パネル高さの肥大化と TextField のレンダリング負荷を抑える狙い。
            if (result.Logs.Count > 0)
            {
                var lines = result.Logs.Take(MaxLogLines).Select(l => $"[{l.Type}] {l.Message}");
                var text = string.Join("\n", lines);
                if (result.Logs.Count > MaxLogLines)
                {
                    text += $"\n… ({result.Logs.Count - MaxLogLines} more)";
                }
                _logs.text = text;
                _logs.style.display = DisplayStyle.Flex;
            }
            else
            {
                _logs.style.display = DisplayStyle.None;
            }
        }

        // ログ表示の上限。これを超えると末尾に省略件数を表示する。
        private const int MaxLogLines = 20;

        /// <summary>結果領域を非表示にする (再オープン時)。</summary>
        public void Clear()
        {
            AddToClassList("palette-result-view-hidden");
        }
    }
}
