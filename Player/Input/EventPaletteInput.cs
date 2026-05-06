using UnityEngine;

namespace Void2610.LiminalPalette.Player
{
    /// <summary>
    /// IMGUI (UnityEngine.Event) を使う Runtime 入力実装。
    /// 呼び出し側 MonoBehaviour の OnGUI から <see cref="HandleEvent"/> を毎回呼ばれる前提。
    ///
    /// 採用理由:
    /// Project の Active Input Handler が "Input System (New)" のみの構成だと UnityEngine.Input は使えない。
    /// 一方 InputSystem 経由で Keyboard を監視すると、macOS で Cmd+P から Play Mode に入ったとき
    /// Cmd キーの keyup を Editor が消費して isPressed=true のまま固着する Unity 既知挙動があり、
    /// 修飾キー判定が壊れて K 単独で toggle が走ってしまう。
    /// IMGUI の KeyDown イベントは OS のイベントキュー由来でこの固着を起こさず、
    /// Active Input Handler の設定にも非依存に動くため、Runtime のホットキー検出にはこちらを使う。
    ///
    /// 動作モデル:
    /// OnGUI で来る KeyDown を 1 フレーム保持の "エッジフラグ" にスタンプし、
    /// LiminalPaletteRuntime の Update から ConsumeXxx で読み取り消費する。
    /// 同フレームで複数の KeyDown が来てもフラグは true のままなので失う心配はない。
    /// </summary>
    public sealed class EventPaletteInput : IPaletteInput
    {
        private readonly KeyCode _toggleKey;
        private readonly bool _requireModifier;

        // OnGUI で立てて Update 側 (ConsumeXxx) で読み取り消費するエッジフラグ群。
        private bool _toggleEdge;
        private bool _upEdge;
        private bool _downEdge;
        private bool _confirmEdge;
        private bool _cancelEdge;
        private bool _tabEdge;
        private bool _tabShift;

        public EventPaletteInput(KeyCode toggleKey, bool requireModifier)
        {
            _toggleKey = toggleKey;
            _requireModifier = requireModifier;
        }

        /// <summary>
        /// OnGUI から呼ばれる。Event.current を渡すこと。
        /// EventType.KeyDown 以外は無視する (Repeat / KeyUp / Layout / Repaint など)。
        /// </summary>
        public void HandleEvent(Event e)
        {
            if (e == null) return;
            if (e.type != EventType.KeyDown) return;

            // Toggle 判定: 修飾キーが必要なら Cmd (macOS) / Ctrl (Win/Linux) のいずれかを許容。
            // IMGUI Event はこの修飾状態を OS イベント単位で持つので、フレームをまたいだ stuck 状態を踏まない。
            if (e.keyCode == _toggleKey)
            {
                if (!_requireModifier || e.command || e.control) _toggleEdge = true;
            }

            // UI ナビ用フォールバック。UIDocument にフォーカスがあるときは
            // PaletteView 側の KeyDownEvent が拾うので普段はここに来ない。
            switch (e.keyCode)
            {
                case KeyCode.UpArrow:
                    _upEdge = true;
                    break;
                case KeyCode.DownArrow:
                    _downEdge = true;
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    _confirmEdge = true;
                    break;
                case KeyCode.Escape:
                    _cancelEdge = true;
                    break;
                case KeyCode.Tab:
                    _tabEdge = true;
                    _tabShift = e.shift;
                    break;
            }
        }

        public bool ConsumeToggle()
        {
            var v = _toggleEdge;
            _toggleEdge = false;
            return v;
        }

        public bool ConsumeUp()
        {
            var v = _upEdge;
            _upEdge = false;
            return v;
        }

        public bool ConsumeDown()
        {
            var v = _downEdge;
            _downEdge = false;
            return v;
        }

        public bool ConsumeConfirm()
        {
            var v = _confirmEdge;
            _confirmEdge = false;
            return v;
        }

        public bool ConsumeCancel()
        {
            var v = _cancelEdge;
            _cancelEdge = false;
            return v;
        }

        public bool ConsumeTab(out bool shift)
        {
            shift = _tabShift;
            var v = _tabEdge;
            _tabEdge = false;
            // shift は次のエッジまで保持しても害はないが、混線回避のため毎回リセット。
            _tabShift = false;
            return v;
        }
    }
}
