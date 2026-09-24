using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Copipe.Interop
{
    /// <summary>
    /// Win32 API の宣言。すべて通常ユーザー権限で使えるもので、キーボードフックは使わない。
    /// </summary>
    internal static class NativeMethods
    {
        // ---- クリップボード -------------------------------------------------------
        // WinForms の Clipboard クラスは、他のアプリがクリップボードを開いていると
        // 100ms 間隔で 10 回リトライして最大約 1 秒固まる。小窓を即座に出したいので
        // Win32 API を直接使い、リトライの回数と間隔を自分で決める。

        internal const uint CF_BITMAP = 2;
        internal const uint CF_DIB = 8;
        internal const uint CF_UNICODETEXT = 13;
        internal const uint CF_HDROP = 15;
        internal const uint CF_DIBV5 = 17;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint EnumClipboardFormats(uint format);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int GetClipboardFormatName(uint format, StringBuilder lpszFormatName, int cchMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        /// <summary>今のクリップボードの持ち主 (最後に EmptyClipboard したウインドウ)。</summary>
        [DllImport("user32.dll")]
        internal static extern IntPtr GetClipboardOwner();

        internal const uint GMEM_MOVEABLE = 0x0002;

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern UIntPtr GlobalSize(IntPtr hMem);

        // ---- クリップボードの変化の通知 -------------------------------------------

        internal const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AddClipboardFormatListener(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RemoveClipboardFormatListener(IntPtr hWnd);

        // ---- ホットキー -----------------------------------------------------------

        internal const int WM_HOTKEY = 0x0312;
        internal const uint MOD_ALT = 0x0001;
        internal const uint MOD_CONTROL = 0x0002;
        internal const uint MOD_SHIFT = 0x0004;
        /// <summary>押しっぱなしのキーリピートで WM_HOTKEY が重ねて届かないようにする。</summary>
        internal const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        /// <summary>最上位ビットが立っていれば、呼び出した時点でキーが押されている。</summary>
        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int vKey);

        // ---- キー入力を送る (ダブルクリックで入力するときの Ctrl+V) ---------------

        internal const uint INPUT_KEYBOARD = 1;
        internal const uint KEYEVENTF_KEYUP = 0x0002;
        internal const ushort VK_CONTROL = 0x11;
        internal const ushort VK_V = 0x56;
        internal const uint MAPVK_VK_TO_VSC = 0;

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        /// <summary>
        /// INPUT の共用体部分。一番大きい MOUSEINPUT を含めておかないと、
        /// 構造体の大きさが実際と合わず SendInput が失敗する。
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        /// <summary>
        /// キー入力をまとめて送る。まとめて送った入力の間に、利用者の入力が割り込むことは無い。
        /// 管理者として実行中のアプリには届かない (UIPI)。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        internal static extern uint MapVirtualKey(uint uCode, uint uMapType);

        // ---- ウインドウ -----------------------------------------------------------

        /// <summary>メッセージ専用ウインドウの親に指定する値。</summary>
        internal static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);
        internal static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        internal const int WS_EX_TOPMOST = 0x00000008;
        internal const int WS_EX_TOOLWINDOW = 0x00000080;
        internal const int WS_EX_NOACTIVATE = 0x08000000;

        internal const int WM_MOUSEACTIVATE = 0x0021;
        internal const int MA_NOACTIVATE = 3;

        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr hWnd);

        // ---- Raw Input (修飾キーのダブルタップを見分けるため、キーの押す・離すを読むだけ) --------
        // キーボードフックと違い、キー入力を横取りも遅延もさせない

        internal const int WM_INPUT = 0x00FF;
        internal const uint RIDEV_INPUTSINK = 0x00000100;   // 前面でなくても受け取る
        internal const uint RIDEV_REMOVE = 0x00000001;
        internal const uint RID_INPUT = 0x10000003;
        internal const uint RIM_TYPEMOUSE = 0;
        internal const uint RIM_TYPEKEYBOARD = 1;
        internal const ushort RI_KEY_BREAK = 0x0001;         // 離した

        [StructLayout(LayoutKind.Sequential)]
        internal struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RAWKEYBOARD
        {
            public ushort MakeCode;
            public ushort Flags;
            public ushort Reserved;
            public ushort VKey;
            public uint Message;
            public uint ExtraInformation;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

        /// <summary>
        /// システム DPI 対応を宣言する。ウインドウを 1 つも作る前に呼ぶこと。
        /// 宣言しないと 125% や 150% の画面で文字がぼやける。
        /// </summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetProcessDPIAware();

        // ---- DWM (Windows 11 の角丸) ----------------------------------------------

        internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        internal const int DWMWCP_ROUND = 2;

        /// <summary>Windows 10 では角丸の指定に対応しておらずエラーを返すが、害は無い。</summary>
        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
