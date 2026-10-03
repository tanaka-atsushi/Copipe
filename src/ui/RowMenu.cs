using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Copipe.UI
{
    /// <summary>
    /// 定型文の行の右クリックメニュー。Windows 標準のポップアップメニュー (TrackPopupMenuEx) で出し、
    /// 選ばれるか閉じられるまで待つ。
    /// WinForms の ContextMenuStrip は、フォーカスを奪わない小窓から出すと、コードで閉じた後の
    /// 2 回目以降の表示が遅れた (E2E で実測)。標準のメニューは Close (EndMenu) で確実に閉じられる。
    /// UI スレッドで使うこと。
    /// </summary>
    internal static class RowMenu
    {
        private const uint MF_STRING = 0x0000;
        private const uint MF_SEPARATOR = 0x0800;

        /// <summary>Show に渡す項目のうち、区切り線にするもの。</summary>
        public const string Separator = "-";
        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const uint TPM_RETURNCMD = 0x0100;
        private const uint TPM_NONOTIFY = 0x0080;

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr tpm);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyMenu(IntPtr menu);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EndMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ModifyMenu(IntPtr menu, uint position, uint flags, UIntPtr id, string text);

        [DllImport("user32.dll")]
        private static extern int MenuItemFromPoint(IntPtr hwnd, IntPtr menu, POINT screen);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr Hwnd;
            public int Message;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public POINT Pt;
        }

        private const uint MF_BYCOMMAND = 0x0000;
        private const int WH_MSGFILTER = -1;
        private const int MSGF_MENU = 2;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;
        // 出しているメニューの大きさを、今の項目の文字に合わせて計算し直させる (メニューのウィンドウ #32768 への非公開のメッセージ)
        private const int MN_SIZEWINDOW = 0x01E2;

        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;
        private const int VK_MBUTTON = 0x04;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        private static bool IsDown(int key)
        {
            return (GetAsyncKeyState(key) & 0x8000) != 0;
        }

        private static bool _swallowClick;

        // 確認つきの項目 (Show の confirmIndex) を扱う、メニューを出している間だけのフック
        private static readonly HookProc _filterProc = OnMenuMessage;
        private static IntPtr _shownMenu;
        private static int _confirmIndex = -1;
        private static string _normalLabel;
        private static string _confirmLabel;
        private static bool _confirming;

        /// <summary>今メニューを出しているか。</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>
        /// メニューの外をクリックして閉じた、そのクリックの最中か。
        /// true の間は一覧がクリックを受け付けない (メニューを閉じるだけにする)。ボタンを全部離すと false に戻る。
        /// </summary>
        public static bool IsSwallowingClick
        {
            get
            {
                if (_swallowClick && !IsDown(VK_LBUTTON) && !IsDown(VK_RBUTTON) && !IsDown(VK_MBUTTON))
                {
                    _swallowClick = false;
                }
                return _swallowClick;
            }
        }

        /// <summary>
        /// メニューを出して、選ばれた項目の位置 (0 始まり) を返す。選ばずに閉じたら -1。
        /// 選ばれるか閉じられるまで戻らない (その間もメッセージは処理される)。
        /// confirmIndex の項目 (削除) は、選んでもメニューを閉じずにその項目の文字を confirmLabel に変え、
        /// もう一度選んだときだけ confirmIndex を返す。別の項目にマウスを動かすと元の文字に戻る。
        /// </summary>
        public static int Show(IntPtr owner, Point screen, IList<string> items, int confirmIndex = -1, string confirmLabel = null)
        {
            IntPtr menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                return -1;
            }
            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    // 0 は「選ばなかった」なので、ID は 1 から
                    if (items[i] == Separator)
                    {
                        AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                    }
                    else
                    {
                        AppendMenu(menu, MF_STRING, new UIntPtr((uint)(i + 1)), items[i]);
                    }
                }
                IsOpen = true;
                IntPtr hook = IntPtr.Zero;
                if (confirmIndex >= 0 && confirmIndex < items.Count && confirmLabel != null)
                {
                    _shownMenu = menu;
                    _confirmIndex = confirmIndex;
                    _normalLabel = items[confirmIndex];
                    _confirmLabel = confirmLabel;
                    _confirming = false;
                    // メニューの中のマウスの操作は、このスレッドの WH_MSGFILTER (MSGF_MENU) を通る
                    hook = SetWindowsHookEx(WH_MSGFILTER, _filterProc, IntPtr.Zero, GetCurrentThreadId());
                }
                int chosen;
                try
                {
                    chosen = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_NONOTIFY | TPM_RIGHTBUTTON,
                                              screen.X, screen.Y, owner, IntPtr.Zero);
                }
                finally
                {
                    if (hook != IntPtr.Zero)
                    {
                        UnhookWindowsHookEx(hook);
                    }
                    _shownMenu = IntPtr.Zero;
                    _confirmIndex = -1;
                }
                // 選ばずに、ボタンを押した瞬間に閉じた = メニューの外のクリック。そのクリックは下の一覧に届くので、離すまで無視させる
                _swallowClick = chosen == 0 && (IsDown(VK_LBUTTON) || IsDown(VK_RBUTTON) || IsDown(VK_MBUTTON));
                return chosen - 1;
            }
            finally
            {
                IsOpen = false;
                DestroyMenu(menu);
            }
        }

        /// <summary>
        /// メニューの中のメッセージ。確認つきの項目の上でボタンを離したら、1 回目はメニューに渡さず
        /// (選ばれて閉じるのを止める)、項目の文字を確認に変える。別の項目にマウスが移ったら元に戻す。
        /// </summary>
        private static IntPtr OnMenuMessage(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code == MSGF_MENU && _shownMenu != IntPtr.Zero)
            {
                MSG msg = (MSG)Marshal.PtrToStructure(lParam, typeof(MSG));
                if (msg.Message == WM_LBUTTONUP || msg.Message == WM_RBUTTONUP || msg.Message == WM_MOUSEMOVE)
                {
                    int item = MenuItemFromPoint(IntPtr.Zero, _shownMenu, msg.Pt);
                    if (msg.Message == WM_MOUSEMOVE)
                    {
                        if (_confirming && item >= 0 && item != _confirmIndex)
                        {
                            SetConfirmLabel(msg.Hwnd, false);
                        }
                    }
                    else if (item == _confirmIndex && !_confirming)
                    {
                        SetConfirmLabel(msg.Hwnd, true);
                        return new IntPtr(1);
                    }
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        private static void SetConfirmLabel(IntPtr menuWindow, bool confirming)
        {
            _confirming = confirming;
            uint id = (uint)(_confirmIndex + 1);
            ModifyMenu(_shownMenu, id, MF_BYCOMMAND | MF_STRING, new UIntPtr(id), confirming ? _confirmLabel : _normalLabel);
            // 文字が長くなってもはみ出さないように、メニューの幅を計算し直させてから描き直す
            SendMessage(menuWindow, MN_SIZEWINDOW, IntPtr.Zero, IntPtr.Zero);
            InvalidateRect(menuWindow, IntPtr.Zero, true);
        }

        /// <summary>出しているメニューを閉じる (Show は -1 を返す)。出していなければ何もしない。</summary>
        public static void Close()
        {
            if (IsOpen)
            {
                EndMenu();
            }
        }
    }
}
