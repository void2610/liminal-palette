using System;

namespace Void2610.LiminalPalette.Player
{
    /// <summary>
    /// パレット表示中にゲーム側の入力を一時的に止めるベストエフォート機構。
    /// 実際の停止 / 復元処理は OnEngage / OnDisengage の購読側 (Player.InputSystem asmdef 等) が行う。
    /// 本 asmdef では Unity.InputSystem を直接参照しないため、フックの提供にとどめる。
    /// </summary>
    public sealed class PaletteInputBlocker
    {
        /// <summary>Engage 時に呼ばれる。InputSystem の ActionMap を全停止する処理などが購読する。</summary>
        public static event Action OnEngage;

        /// <summary>Disengage 時に呼ばれる。Engage で停止したものを復元する処理が購読する。</summary>
        public static event Action OnDisengage;

        private bool _engaged;

        public bool IsEngaged => _engaged;

        public void Engage()
        {
            if (_engaged) return;
            _engaged = true;
            OnEngage?.Invoke();
        }

        public void Disengage()
        {
            if (!_engaged) return;
            _engaged = false;
            OnDisengage?.Invoke();
        }
    }
}
