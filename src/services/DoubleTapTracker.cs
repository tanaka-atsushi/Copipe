using System.Windows.Forms;
using Copipe.UI;

namespace Copipe.Services
{
    /// <summary>ダブルタップの判定の結果。</summary>
    public enum DoubleTapEvent
    {
        /// <summary>何も起きていない。</summary>
        None,
        /// <summary>ダブルタップの 2 回目が押された (押し続けている間、小窓を出す)。</summary>
        Pressed,
        /// <summary>Pressed の後、2 回目が離された。</summary>
        Released
    }

    /// <summary>
    /// 修飾キー (Ctrl・Shift・Alt) のダブルタップの判定。キーの押す・離すを時刻とともに順に渡す。
    /// 1 回目は FirstTapMaxMs 以内に離し、離してから SecondTapMaxGapMs 以内に 2 回目を押すと Pressed。
    /// 間に別のキーが押されたら取り消す (Ctrl+C を 2 回、などを取り違えないため)。
    /// 左右のキー (LControlKey など) は同じキーとして扱う。押し続けたときの押下の繰り返しは 1 回として扱う。
    /// </summary>
    public sealed class DoubleTapTracker
    {
        /// <summary>1 回目を押してから離すまでの上限。これより長く押していたらタップとみなさない。</summary>
        public const int FirstTapMaxMs = 300;

        /// <summary>1 回目を離してから 2 回目を押すまでの上限。</summary>
        public const int SecondTapMaxGapMs = 400;

        private enum State
        {
            Idle,
            FirstDown,
            FirstUp,
            Holding
        }

        private readonly Keys _target;
        private State _state = State.Idle;
        private bool _targetDown;
        private long _time;

        /// <param name="target">見るキー (ControlKey・ShiftKey・Menu。左右のキーでもよい)。</param>
        public DoubleTapTracker(Keys target)
        {
            _target = HotkeyText.NormalizeModifier(target);
        }

        /// <summary>キーの押す (down = true)・離す (false) を渡す。timeMs は増えていく時刻 (ms)。</summary>
        public DoubleTapEvent Feed(Keys key, bool down, long timeMs)
        {
            bool isTarget = HotkeyText.NormalizeModifier(key) == _target;
            if (!isTarget)
            {
                // 別のキー。押し続けている間 (数字キーなど) は取り消さない。それ以外は数え直し
                if (down && _state != State.Holding)
                {
                    _state = State.Idle;
                }
                return DoubleTapEvent.None;
            }

            if (down)
            {
                if (_targetDown)
                {
                    // 押し続けたときの押下の繰り返し
                    return DoubleTapEvent.None;
                }
                _targetDown = true;
                if (_state == State.FirstUp && timeMs - _time <= SecondTapMaxGapMs)
                {
                    _state = State.Holding;
                    return DoubleTapEvent.Pressed;
                }
                _state = State.FirstDown;
                _time = timeMs;
                return DoubleTapEvent.None;
            }

            // 離した
            bool wasDown = _targetDown;
            _targetDown = false;
            if (!wasDown)
            {
                // 見張り始めたときに押されていたキーの「離す」
                _state = State.Idle;
                return DoubleTapEvent.None;
            }
            if (_state == State.Holding)
            {
                _state = State.Idle;
                return DoubleTapEvent.Released;
            }
            if (_state == State.FirstDown && timeMs - _time <= FirstTapMaxMs)
            {
                _state = State.FirstUp;
                _time = timeMs;
                return DoubleTapEvent.None;
            }
            _state = State.Idle;
            return DoubleTapEvent.None;
        }

        /// <summary>数え直す (小窓を別の方法で出したとき・見張りを止めたときなど)。</summary>
        public void Reset()
        {
            _state = State.Idle;
            _targetDown = false;
        }
    }
}
