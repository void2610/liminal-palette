#if !ENABLE_INPUT_SYSTEM || ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine;

namespace Void2610.LiminalPalette.Runtime
{
    /// <summary>
    /// Legacy Input Manager (UnityEngine.Input) を使う実装。
    /// InputSystem が排他モード (ENABLE_INPUT_SYSTEM のみ) の場合は UnityEngine.Input が無効化されるため、
    /// この実装は ENABLE_LEGACY_INPUT_MANAGER もしくは Legacy 単体構成のときだけコンパイルされる。
    /// </summary>
    public sealed class LegacyPaletteInput : IPaletteInput
    {
        private readonly KeyCode _toggleKey;
        private readonly bool _requireModifier;

        public LegacyPaletteInput(KeyCode toggleKey, bool requireModifier)
        {
            _toggleKey = toggleKey;
            _requireModifier = requireModifier;
        }

        public bool ConsumeToggle()
        {
            if (!Input.GetKeyDown(_toggleKey)) return false;
            if (!_requireModifier) return true;
            // Ctrl (Win/Linux) / Cmd (Mac) を等価扱い。Apple は LeftCommand / RightCommand として届く。
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
        }

        public bool ConsumeUp() => Input.GetKeyDown(KeyCode.UpArrow);
        public bool ConsumeDown() => Input.GetKeyDown(KeyCode.DownArrow);
        public bool ConsumeConfirm() => Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        public bool ConsumeCancel() => Input.GetKeyDown(KeyCode.Escape);

        public bool ConsumeTab(out bool shift)
        {
            shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            return Input.GetKeyDown(KeyCode.Tab);
        }
    }
}
#endif
