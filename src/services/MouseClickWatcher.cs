using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Copipe.Interop;

namespace Copipe.Services
{
    /// <summary>
    /// マウスのボタン (左・右・中・X1・X2) が押されたことを、どのアプリの上で押されても知らせる。
    /// ダブルタップで出したままの小窓を、外のクリックで閉じるために使う。
    /// GetAsyncKeyState を一定間隔で見る方法では、押してすぐ離すクリック (タッチパッドのタップなど) を
    /// 見落とすので、Raw Input で押した瞬間を受け取る (読むだけで、クリックを横取りも遅延もさせない)。
    /// Start から Stop までの間だけ登録する。UI スレッドで作り、UI スレッドで使うこと。
    /// </summary>
    internal sealed class MouseClickWatcher : NativeWindow, IDisposable
    {
        private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
        private const ushort HID_USAGE_GENERIC_MOUSE = 0x02;

        // RAWMOUSE の usButtonFlags のうち「押した」の印
        private const ushort ButtonDownFlags =
            0x0001 /* LEFT_DOWN */ | 0x0004 /* RIGHT_DOWN */ | 0x0010 /* MIDDLE_DOWN */ |
            0x0040 /* BUTTON_4_DOWN */ | 0x0100 /* BUTTON_5_DOWN */;

        // RAWMOUSE の中での usButtonFlags の位置 (usFlags と詰め物の後)
        private const int ButtonFlagsOffset = 4;

        private bool _registered;

        public MouseClickWatcher()
        {
            // Raw Input の INPUTSINK は、メッセージ専用ウインドウでも受け取れる
            CreateParams cp = new CreateParams();
            cp.Parent = NativeMethods.HWND_MESSAGE;
            CreateHandle(cp);
        }

        /// <summary>マウスのボタンが押されたとき。</summary>
        public event EventHandler ButtonPressed;

        /// <summary>見張りを始める。登録できなければ false。</summary>
        public bool Start()
        {
            if (_registered)
            {
                return true;
            }
            _registered = Register(NativeMethods.RIDEV_INPUTSINK, Handle);
            return _registered;
        }

        /// <summary>見張りをやめる。</summary>
        public void Stop()
        {
            if (!_registered)
            {
                return;
            }
            Register(NativeMethods.RIDEV_REMOVE, IntPtr.Zero);
            _registered = false;
        }

        private static bool Register(uint flags, IntPtr target)
        {
            NativeMethods.RAWINPUTDEVICE[] devices = new NativeMethods.RAWINPUTDEVICE[1];
            devices[0].usUsagePage = HID_USAGE_PAGE_GENERIC;
            devices[0].usUsage = HID_USAGE_GENERIC_MOUSE;
            devices[0].dwFlags = flags;
            devices[0].hwndTarget = target;
            return NativeMethods.RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf(typeof(NativeMethods.RAWINPUTDEVICE)));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_INPUT && _registered && IsButtonDown(m.LParam))
            {
                EventHandler handler = ButtonPressed;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
            // WM_INPUT は既定の処理 (DefWindowProc) に渡して後片付けさせる
            base.WndProc(ref m);
        }

        /// <summary>
        /// WM_INPUT の中身がボタンを押したものか。大きさは環境 (32 ビット / 64 ビット) で違うので、先に問い合わせてから読む。
        /// </summary>
        private static bool IsButtonDown(IntPtr rawInput)
        {
            uint headerSize = (uint)Marshal.SizeOf(typeof(NativeMethods.RAWINPUTHEADER));
            uint size = 0;
            if (NativeMethods.GetRawInputData(rawInput, NativeMethods.RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0 ||
                size < headerSize + ButtonFlagsOffset + 2)
            {
                return false;
            }
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (NativeMethods.GetRawInputData(rawInput, NativeMethods.RID_INPUT, buffer, ref size, headerSize) == uint.MaxValue)
                {
                    return false;
                }
                NativeMethods.RAWINPUTHEADER header = (NativeMethods.RAWINPUTHEADER)Marshal.PtrToStructure(buffer, typeof(NativeMethods.RAWINPUTHEADER));
                if (header.dwType != NativeMethods.RIM_TYPEMOUSE)
                {
                    return false;
                }
                ushort buttonFlags = (ushort)Marshal.ReadInt16(buffer, (int)headerSize + ButtonFlagsOffset);
                return (buttonFlags & ButtonDownFlags) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
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
