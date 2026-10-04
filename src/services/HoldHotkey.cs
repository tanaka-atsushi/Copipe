using System;
using System.Windows.Forms;
using Copipe.Interop;
using Copipe.UI;

namespace Copipe.Services
{
    /// <summary>
    /// 「押し続けている間」を知らせるホットキー。
    /// 押した瞬間は RegisterHotKey の WM_HOTKEY で受け取り、離した瞬間は押されている間だけ
    /// GetAsyncKeyState を短い間隔で調べて検知する。キーボードフック (全キー入力の監視) は使わない。
    /// ただし CapsLock だけは、RegisterHotKey で受け取っても CapsLock が切り替わってしまうので、
    /// そのキーだけを低レベルのキーボードフック (KeyboardHook) で受け取って握りつぶす。
    /// UI スレッドで作り、UI スレッドで使うこと (イベントも UI スレッドで発生する)。
    /// </summary>
    internal sealed class HoldHotkey : NativeWindow, IDisposable
    {
        private const int HotkeyId = 1;

        /// <summary>離したことに気づくまでの最大の遅れ。人が遅れを感じない程度に短く、負荷にならない程度に長く。</summary>
        private const int ReleasePollIntervalMs = 30;

        private readonly Timer _releaseTimer;
        private Keys _keys;
        private bool _registered;
        private bool _held;
        // CapsLock のときだけ。押す・離すはフックから届くので、WM_HOTKEY と離したことの見回りは使わない
        private KeyboardHook _keyHook;

        public HoldHotkey()
        {
            // 画面に出ないメッセージ専用ウインドウで WM_HOTKEY を受け取る
            CreateParams cp = new CreateParams();
            cp.Parent = NativeMethods.HWND_MESSAGE;
            CreateHandle(cp);

            _releaseTimer = new Timer();
            _releaseTimer.Interval = ReleasePollIntervalMs;
            _releaseTimer.Tick += OnReleaseTimerTick;
        }

        /// <summary>キーが押されたとき。押し続けても 1 回だけ発生する。</summary>
        public event EventHandler Pressed;

        /// <summary>Pressed の後、キーが離されたとき。</summary>
        public event EventHandler Released;

        /// <summary>今登録しているキー (未登録なら Keys.None)。</summary>
        public Keys Current
        {
            get { return _registered ? _keys : Keys.None; }
        }

        /// <summary>
        /// 指定したキーで登録し直す。他のアプリ (または起動済みの Copipe) が同じキーを
        /// 登録していると失敗し、false を返す (このとき未登録の状態になる)。
        /// </summary>
        /// <param name="keys">キーと修飾キーの組み合わせ。例: Keys.F1、Keys.Control | Keys.Space。
        /// 離したかどうかはキー (修飾キー以外) だけで判定する。</param>
        public bool TryRegister(Keys keys)
        {
            Unregister();
            if ((keys & Keys.KeyCode) == Keys.None)
            {
                // キーが無ければ何も登録しない
                return true;
            }

            uint modifiers = NativeMethods.MOD_NOREPEAT;
            if ((keys & Keys.Control) == Keys.Control) { modifiers |= NativeMethods.MOD_CONTROL; }
            if ((keys & Keys.Shift) == Keys.Shift) { modifiers |= NativeMethods.MOD_SHIFT; }
            if ((keys & Keys.Alt) == Keys.Alt) { modifiers |= NativeMethods.MOD_ALT; }

            _keys = keys;
            if (HotkeyText.UsesKeyboardHook(keys))
            {
                _keyHook = KeyboardHook.Start(Handle, HotkeyText.HookVirtualKeys(keys));
                if (_keyHook == null)
                {
                    return false;
                }
                if ((keys & Keys.KeyCode) == Keys.Capital)
                {
                    // 握りつぶしている間は CapsLock だけではオフに戻せないので、オンならオフにしておく
                    // (起動したとき・設定画面で CapsLock を押して取り込んだときなど)。
                    // 下の RegisterHotKey より先にする。登録した後だとオフにする入力がホットキーとして吸い込まれ、
                    // 入力先のアプリに届かずに CapsLock がかかったままになることがあった
                    _keyHook.TurnOffCapsLock();
                }
            }

            // フックで受け取るキーも登録はしておく。他のアプリが使っているキーを
            // 今までどおり「登録できなかった」と知らせるため (WM_HOTKEY はフックが先に握りつぶすので届かない)
            _registered = NativeMethods.RegisterHotKey(Handle, HotkeyId, modifiers, (uint)(keys & Keys.KeyCode));
            if (!_registered && _keyHook != null)
            {
                _keyHook.Dispose();
                _keyHook = null;
            }
            return _registered;
        }

        /// <summary>登録を解除する。押している途中なら Released を発生させてから解除する。</summary>
        public void Unregister()
        {
            if (_held)
            {
                _releaseTimer.Stop();
                _held = false;
                RaiseReleased();
            }
            if (_keyHook != null)
            {
                _keyHook.Dispose();
                _keyHook = null;
            }
            if (_registered)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                _registered = false;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                // フックで受け取っているときは、フックからの知らせだけを使う
                if (_keyHook == null)
                {
                    OnHotkey();
                }
                return;
            }
            if (m.Msg == KeyboardHook.PressedMessage)
            {
                // 解除した後に届いた古い知らせは無視する
                if (_keyHook != null)
                {
                    OnHotkey();
                }
                return;
            }
            if (m.Msg == KeyboardHook.ReleasedMessage)
            {
                if (_keyHook != null && _held)
                {
                    _held = false;
                    RaiseReleased();
                }
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>
        /// NativeWindow の既定はウインドウプロシージャ内の例外を握りつぶす。
        /// それだと「押しても何も起きない」だけになって原因が分からないので、
        /// アプリの例外ハンドラー (Program の OnThreadException) に渡す。
        /// </summary>
        protected override void OnThreadException(Exception e)
        {
            Application.OnThreadException(e);
        }

        private void OnHotkey()
        {
            // MOD_NOREPEAT なので押しっぱなしで重ねて届くことは無いが、
            // 離したことに気づく前に押し直された場合に Pressed を二重に出さない
            if (_held)
            {
                return;
            }

            _held = true;
            if (_keyHook == null)
            {
                // フックで受け取るキーは握りつぶすので GetAsyncKeyState に反映されない。離したこともフックから届く
                _releaseTimer.Start();
            }

            EventHandler handler = Pressed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void OnReleaseTimerTick(object sender, EventArgs e)
        {
            if ((NativeMethods.GetAsyncKeyState((int)(_keys & Keys.KeyCode)) & 0x8000) != 0)
            {
                return;
            }

            _releaseTimer.Stop();
            _held = false;
            RaiseReleased();
        }

        private void RaiseReleased()
        {
            EventHandler handler = Released;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            _releaseTimer.Dispose();
            if (_keyHook != null)
            {
                _keyHook.Dispose();
                _keyHook = null;
            }
            if (_registered)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                _registered = false;
            }
            DestroyHandle();
        }
    }
}
