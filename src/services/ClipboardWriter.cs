using System;
using System.Runtime.InteropServices;
using Copipe.Interop;

namespace Copipe.Services
{
    /// <summary>
    /// クリップボードにテキストを置く (Win32 API)。ダブルクリックした履歴を入力するときに使う。
    /// </summary>
    public static class ClipboardWriter
    {
        // 書くときは読むときより少し粘る。コピー直後は CopyQ などが開いていることがあるため (実測)
        private const int OpenAttempts = 20;

        /// <summary>
        /// テキストを置く。置けたら true。持ち主は window になる (IsOwnedBy で自分の書き込みと分かる)。
        /// </summary>
        /// <param name="window">このプロセスのウインドウ。NULL だと EmptyClipboard が持ち主を
        /// 設定できず SetClipboardData が失敗するため、NULL は受け付けない。</param>
        public static bool SetText(IntPtr window, string text)
        {
            if (window == IntPtr.Zero)
            {
                throw new ArgumentException("クリップボードを開くウインドウを指定してください。", "window");
            }
            if (text == null)
            {
                throw new ArgumentNullException("text");
            }

            IntPtr memory = AllocateUnicode(text);
            if (memory == IntPtr.Zero)
            {
                return false;
            }

            bool handedOver = false;
            try
            {
                if (!ClipboardReader.TryOpen(window, OpenAttempts))
                {
                    return false;
                }
                try
                {
                    if (!NativeMethods.EmptyClipboard())
                    {
                        return false;
                    }
                    if (NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, memory) == IntPtr.Zero)
                    {
                        return false;
                    }
                    // ここから先、このメモリはクリップボードの持ち物なので解放してはいけない
                    handedOver = true;
                    return true;
                }
                finally
                {
                    NativeMethods.CloseClipboard();
                }
            }
            finally
            {
                if (!handedOver)
                {
                    NativeMethods.GlobalFree(memory);
                }
            }
        }

        /// <summary>今のクリップボードの持ち主が window か (= Copipe 自身が置いた内容か)。</summary>
        public static bool IsOwnedBy(IntPtr window)
        {
            return window != IntPtr.Zero && NativeMethods.GetClipboardOwner() == window;
        }

        /// <summary>NUL 終端の UTF-16 文字列を、クリップボードに渡せるメモリ (GMEM_MOVEABLE) に用意する。</summary>
        private static IntPtr AllocateUnicode(string text)
        {
            int bytes = (text.Length + 1) * 2;
            IntPtr memory = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, new UIntPtr((uint)bytes));
            if (memory == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr ptr = NativeMethods.GlobalLock(memory);
            if (ptr == IntPtr.Zero)
            {
                NativeMethods.GlobalFree(memory);
                return IntPtr.Zero;
            }
            try
            {
                Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                Marshal.WriteInt16(ptr, text.Length * 2, 0);
            }
            finally
            {
                NativeMethods.GlobalUnlock(memory);
            }
            return memory;
        }
    }
}
