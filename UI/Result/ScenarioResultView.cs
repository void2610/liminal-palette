using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Void2610.LiminalPalette.UI
{
    /// <summary>
    /// シナリオ実行結果を表示する VisualElement。
    /// 全体のステータス + 各ステップの ✓/✗ アイコン + 失敗詳細を縦リストで描画する。
    /// CommandResult を表示する <see cref="ResultView"/> の姉妹クラス (継承ではなく独立)。
    /// </summary>
    public sealed class ScenarioResultView : VisualElement
    {
        private readonly Label _badge;
        private readonly Label _duration;
        private readonly Label _path;
        private readonly VisualElement _stepsContainer;

        public ScenarioResultView()
        {
            AddToClassList("palette-scenario-result-view");
            AddToClassList("palette-result-view-hidden");
            style.flexDirection = FlexDirection.Column;

            var statusRow = new VisualElement();
            statusRow.style.flexDirection = FlexDirection.Row;
            statusRow.style.alignItems = Align.Center;
            _badge = new Label();
            _badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            _badge.style.marginRight = 8;
            _duration = new Label();
            _duration.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            _duration.style.marginRight = 8;
            _path = new Label();
            _path.style.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            statusRow.Add(_badge);
            statusRow.Add(_duration);
            statusRow.Add(_path);
            Add(statusRow);

            _stepsContainer = new VisualElement();
            _stepsContainer.style.flexDirection = FlexDirection.Column;
            _stepsContainer.style.marginTop = 6;
            Add(_stepsContainer);
        }

        public void Show(ScenarioResult result)
        {
            RemoveFromClassList("palette-result-view-hidden");
            style.display = DisplayStyle.Flex;

            if (result.WasRejectedAsAlreadyRunning)
            {
                _badge.text = "BUSY";
                _badge.style.color = new Color(0.95f, 0.75f, 0.3f, 1f);
                _duration.text = "";
                _path.text = "Another scenario is already running";
                _stepsContainer.Clear();
                return;
            }

            _badge.text = result.Success ? "OK" : "FAIL";
            _badge.style.color = result.Success
                ? new Color(0.4f, 0.85f, 0.4f, 1f)
                : new Color(0.95f, 0.45f, 0.45f, 1f);
            _duration.text = $"{result.Duration.TotalMilliseconds:F1} ms";
            _path.text = result.Path ?? "(ad-hoc)";

            _stepsContainer.Clear();
            for (var i = 0; i < result.Steps.Count; i++)
            {
                _stepsContainer.Add(BuildStepRow(i, result.Steps[i]));
            }
        }

        // VisualElement.Clear() を意図的にシャドウし、ステップ行の削除と非表示化をまとめて行う公開 API として再定義する。
        public new void Clear()
        {
            AddToClassList("palette-result-view-hidden");
            style.display = DisplayStyle.None;
            _stepsContainer.Clear();
        }

        // ステップ 1 件を表示する 1 行を組み立てる。
        // 成功 → "✓ 1. <summary> <duration>"、失敗 → "✗ ... <error>" を赤字で。
        private static VisualElement BuildStepRow(int index, StepResult sr)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.marginTop = 2;

            var icon = new Label(sr.Success ? "✓" : "✗");
            icon.style.width = 16;
            icon.style.flexShrink = 0;
            icon.style.unityFontStyleAndWeight = FontStyle.Bold;
            icon.style.color = sr.Success
                ? new Color(0.4f, 0.85f, 0.4f, 1f)
                : new Color(0.95f, 0.45f, 0.45f, 1f);
            row.Add(icon);

            var num = new Label($"{index + 1}.");
            num.style.width = 26;
            num.style.flexShrink = 0;
            num.style.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            row.Add(num);

            var summary = new Label(BuildStepSummary(sr));
            summary.style.flexGrow = 1;
            summary.style.flexShrink = 1;
            summary.style.whiteSpace = WhiteSpace.Normal;
            summary.style.color = sr.Success
                ? new Color(0.85f, 0.85f, 0.85f, 1f)
                : new Color(0.95f, 0.6f, 0.6f, 1f);
            row.Add(summary);

            var duration = new Label($"{sr.Duration.TotalMilliseconds:F1}ms");
            duration.style.width = 70;
            duration.style.flexShrink = 0;
            duration.style.unityTextAlign = TextAnchor.MiddleRight;
            duration.style.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            row.Add(duration);

            return row;
        }

        // 1 ステップの 1 行説明を組み立てる。失敗時は error を末尾に付ける。
        private static string BuildStepSummary(StepResult sr)
        {
            var sb = new StringBuilder();
            switch (sr.Step)
            {
                case CommandStep cs:
                    sb.Append("Run ").Append(cs.CommandPath);
                    if (cs.Args != null && cs.Args.Count > 0)
                    {
                        sb.Append(" (");
                        var first = true;
                        foreach (var kv in cs.Args)
                        {
                            if (!first) sb.Append(", ");
                            first = false;
                            sb.Append(kv.Key).Append('=').Append(kv.Value);
                        }
                        sb.Append(')');
                    }
                    break;
                case WaitStep ws:
                    sb.Append(ws.Kind == ScenarioStepKind.WaitSeconds
                        ? $"Wait {ws.Seconds}s"
                        : $"WaitFrames({ws.Frames})");
                    break;
                case AssertStep asr:
                    var op = asr.Kind == ScenarioStepKind.AssertEquals ? "==" : "!=";
                    sb.Append("Assert ").Append(asr.ObservableFieldPath).Append(' ').Append(op).Append(' ');
                    sb.Append(asr.Expected == null ? "null" : TypeConverterRegistry.ToDisplayString(asr.Expected));
                    break;
                default:
                    sb.Append(sr.Step?.Kind.ToString() ?? "(null)");
                    break;
            }
            if (!string.IsNullOrEmpty(sr.Step?.Description))
            {
                sb.Append("  — ").Append(sr.Step.Description);
            }
            if (!sr.Success && !string.IsNullOrEmpty(sr.Error))
            {
                sb.Append("\n   ").Append(sr.Error);
            }
            return sb.ToString();
        }
    }
}
