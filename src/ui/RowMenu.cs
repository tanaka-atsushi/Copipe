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
        /// </summary>
        public static int Show(IntPtr owner, Point screen, IList<string> items)
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
                int chosen = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_NONOTIFY | TPM_RIGHTBUTTON,
                                              screen.X, screen.Y, owner, IntPtr.Zero);
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
