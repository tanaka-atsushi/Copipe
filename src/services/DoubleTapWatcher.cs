using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Copipe.Interop;

namespace Copipe.Services
{
    /// <summary>
    /// 修飾キー (Ctrl・Shift・Alt) のダブルタップを見張り、2 回目を押したとき Pressed、離したとき Released を出す。
    /// 修飾キー単独はホットキーとして登録できないので、Raw Input でキーの押す・離すを読む
    /// (読むだけで、キー入力を横取りも遅延もさせない)。ダブルタップを設定しているときだけ登録する。
    /// UI スレッドで作り、UI スレッドで使うこと。
    /// </summary>
    internal sealed class DoubleTapWatcher : NativeWindow, IDisposable
    {
        private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
        private const ushort HID_USAGE_GENERIC_KEYBOARD = 0x06;

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private DoubleTapTracker _tracker;
        private Keys _key = Keys.None;
        private bool _registered;
        private bool _holding;

        public DoubleTapWatcher()
        {
            // Raw Input の INPUTSINK は、メッセージ専用ウインドウでも受け取れる
            CreateParams cp = new CreateParams();
            cp.Parent = NativeMethods.HWND_MESSAGE;
            CreateHandle(cp);
        }

        /// <summary>ダブルタップの 2 回目が押されたとき。</summary>
        public event EventHandler Pressed;

        /// <summary>Pressed の後、2 回目が離されたとき。</summary>
        public event EventHandler Released;

        /// <summary>見張っているキー (見張っていなければ Keys.None)。</summary>
        public Keys Key
        {
            get { return _key; }
        }

        /// <summary>見張るキーを変える。Keys.None なら見張りをやめる。登録できなければ false。</summary>
        public bool SetKey(Keys key)
        {
            Stop();
            _key = Keys.None;
            if (key == Keys.None)
            {
                return true;
            }
            if (!Register(NativeMethods.RIDEV_INPUTSINK, Handle))
            {
                return false;
            }
            _registered = true;
            _key = key;
            _tracker = new DoubleTapTracker(key);
            return true;
        }

        /// <summary>見張りを一時的にやめる (設定画面を開いている間など)。押している途中なら Released を出す。</summary>
        public void Stop()
        {
            if (_registered)
            {
                Register(NativeMethods.RIDEV_REMOVE, IntPtr.Zero);
                _registered = false;
            }
            if (_tracker != null)
            {
                _tracker.Reset();
            }
            EndHold();
        }

        /// <summary>Stop の後、設定どおりの見張りに戻す。</summary>
        public void Resume()
        {
            if (_key != Keys.None && !_registered)
            {
                SetKey(_key);
            }
        }

        private static bool Register(uint flags, IntPtr target)
        {
            NativeMethods.RAWINPUTDEVICE[] devices = new NativeMethods.RAWINPUTDEVICE[1];
            devices[0].usUsagePage = HID_USAGE_PAGE_GENERIC;
            devices[0].usUsage = HID_USAGE_GENERIC_KEYBOARD;
            devices[0].dwFlags = flags;
            devices[0].hwndTarget = target;
            return NativeMethods.RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf(typeof(NativeMethods.RAWINPUTDEVICE)));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_INPUT && _registered)
            {
                ReadKeyboard(m.LParam);
                // WM_INPUT は既定の処理 (DefWindowProc) に渡して後片付けさせる
            }
            base.WndProc(ref m);
        }

        /// <summary>
        /// WM_INPUT の中身を読む。大きさは環境 (32 ビット / 64 ビット) で違うので、先に問い合わせてから読む。
        /// </summary>
        private void ReadKeyboard(IntPtr rawInput)
        {
            uint headerSize = (uint)Marshal.SizeOf(typeof(NativeMethods.RAWINPUTHEADER));
            uint size = 0;
            if (NativeMethods.GetRawInputData(rawInput, NativeMethods.RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
            {
                return;
            }
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (NativeMethods.GetRawInputData(rawInput, NativeMethods.RID_INPUT, buffer, ref size, headerSize) == uint.MaxValue)
                {
                    return;
                }
                NativeMethods.RAWINPUTHEADER header = (NativeMethods.RAWINPUTHEADER)Marshal.PtrToStructure(buffer, typeof(NativeMethods.RAWINPUTHEADER));
                if (header.dwType != NativeMethods.RIM_TYPEKEYBOARD)
                {
                    return;
                }
                NativeMethods.RAWKEYBOARD keyboard = (NativeMethods.RAWKEYBOARD)Marshal.PtrToStructure(
                    new IntPtr(buffer.ToInt64() + headerSize), typeof(NativeMethods.RAWKEYBOARD));
                OnKey(keyboard.VKey, (keyboard.Flags & NativeMethods.RI_KEY_BREAK) == 0);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private void OnKey(ushort vkey, bool down)
        {
            DoubleTapEvent e = _tracker.Feed((Keys)vkey, down, _clock.ElapsedMilliseconds);
            if (e == DoubleTapEvent.Pressed)
            {
                _holding = true;
                EventHandler handler = Pressed;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
            else if (e == DoubleTapEvent.Released)
            {
                EndHold();
            }
        }

        private void EndHold()
        {
            if (!_holding)
            {
                return;
            }
            _holding = false;
            EventHandler handler = Released;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// NativeWindow の既定はウインドウプロシージャ内の例外を握りつぶすので、
        /// アプリの例外ハンドラー (Program の OnThreadException) に渡す。
        /// </summary>
        protected override void OnThreadException(Exception e)
        {
            Application.OnThreadException(e);
        }

        public void Dispose()
        {
            Stop();
            DestroyHandle();
        }
    }
}
