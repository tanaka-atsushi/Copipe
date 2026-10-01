using System;
using System.Windows.Forms;
using Copipe.Interop;

namespace Copipe.Services
{
    /// <summary>
    /// 「押し続けている間」を知らせるホットキー。
    /// 押した瞬間は RegisterHotKey の WM_HOTKEY で受け取り、離した瞬間は押されている間だけ
    /// GetAsyncKeyState を短い間隔で調べて検知する。キーボードフック (全キー入力の監視) は使わない。
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
                // ホットキーを使わない設定 (ダブルタップだけで小窓を出す)
                return true;
            }

            uint modifiers = NativeMethods.MOD_NOREPEAT;
            if ((keys & Keys.Control) == Keys.Control) { modifiers |= NativeMethods.MOD_CONTROL; }
            if ((keys & Keys.Shift) == Keys.Shift) { modifiers |= NativeMethods.MOD_SHIFT; }
            if ((keys & Keys.Alt) == Keys.Alt) { modifiers |= NativeMethods.MOD_ALT; }

            _keys = keys;
            _registered = NativeMethods.RegisterHotKey(Handle, HotkeyId, modifiers, (uint)(keys & Keys.KeyCode));
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
                OnHotkey();
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
            _releaseTimer.Start();

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
            if (_registered)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                _registered = false;
            }
            DestroyHandle();
        }
    }
}
