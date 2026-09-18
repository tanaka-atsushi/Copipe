using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Copipe.Interop;
using Copipe.Model;

namespace Copipe.Services
{
    /// <summary>
    /// 現在のクリップボードの内容を Win32 API で読み取る。
    /// 他のアプリがクリップボードを開いたままでも、短時間であきらめて戻る。
    /// </summary>
    public static class ClipboardReader
    {
        private const int OpenAttempts = 5;
        private const int OpenRetryDelayMs = 10;

        // 登録形式 (RegisterClipboardFormat で作られる形式) の ID はこの値以上になる
        private const uint FirstRegisteredFormat = 0xC000;

        // DROPFILES 構造体: pFiles (DWORD), pt (POINT), fNC (BOOL), fWide (BOOL) の 20 バイト
        private const int DropFilesSize = 20;
        private const int DropFilesWideOffset = 16;

        /// <summary>
        /// 読み取った内容を返す。クリップボードを読めなかったときは例外を投げず Unavailable を返す。
        /// </summary>
        /// <param name="window">
        /// このプロセスのウインドウ。クリップボードを開くときに指定する。
        /// NULL で開くと、他のアプリが NULL で開いている最中でも開けてしまい (実測)、
        /// その相手が空にしている途中の状態を読むおそれがある。ウインドウを指定すれば、
        /// 相手の開き方によらず開いている間は開けない (実測) ので、NULL は受け付けない。
        /// </param>
        public static ClipboardSnapshot Read(IntPtr window)
        {
            if (window == IntPtr.Zero)
            {
                throw new ArgumentException("クリップボードを開くウインドウを指定してください。", "window");
            }

            try
            {
                if (!TryOpen(window))
                {
                    return ClipboardSnapshot.Unavailable;
                }
                try
                {
                    return ReadOpened();
                }
                finally
                {
                    // 開いている間は他のアプリがコピーできないので、読み終えたらすぐ閉じる
                    NativeMethods.CloseClipboard();
                }
            }
            catch (Exception)
            {
                return ClipboardSnapshot.Unavailable;
            }
        }

        private static bool TryOpen(IntPtr window)
        {
            // コピー直後の約 200 ms の間は、CopyQ や Clibor などの履歴ツールやエクスプローラーが
            // 断続的にクリップボードを開いている (実測)。短い間なら待ち、開いたままなら
            // 小窓の表示を遅らせないようにあきらめる (押している間の読み直しは CopipeApp が行う)。
            return TryOpen(window, OpenAttempts);
        }

        /// <summary>10 ms 間隔で attempts 回までクリップボードを開こうとする。開けたら true (閉じるのは呼び出し側)。</summary>
        internal static bool TryOpen(IntPtr window, int attempts)
        {
            for (int i = 0; i < attempts; i++)
            {
                if (i > 0)
                {
                    Thread.Sleep(OpenRetryDelayMs);
                }
                if (NativeMethods.OpenClipboard(window))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// テキスト → ファイル → 画像 の順に調べる。Excel のようにテキストと画像を
        /// 両方置くアプリでは、テキストとして扱う。
        /// </summary>
        private static ClipboardSnapshot ReadOpened()
        {
            // 「ある」と報告されたのに読めなかった形式があるか。遅延レンダリングの持ち主が
            // 応答しない・描画に失敗した場合に起きる。そのときは「テキスト以外」や「空」と
            // 誤って表示せず、読み取れなかったことにする
            bool unreadable = false;

            if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT))
            {
                string text = ReadUnicodeText();
                if (text != null)
                {
                    return ClipboardSnapshot.FromText(text);
                }
                unreadable = true;
            }

            if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_HDROP))
            {
                string[] files = ReadFileDrop();
                if (files != null)
                {
                    return ClipboardSnapshot.FromFiles(files);
                }
                unreadable = true;
            }

            if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_DIB) ||
                NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_DIBV5) ||
                NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_BITMAP))
            {
                return ClipboardSnapshot.Image;
            }

            if (unreadable)
            {
                return ClipboardSnapshot.Unavailable;
            }
            return HasUserData() ? ClipboardSnapshot.Other : ClipboardSnapshot.Empty;
        }

        /// <summary>OLE の管理用の形式を除いて、何かの形式が置かれているか。</summary>
        private static bool HasUserData()
        {
            uint format = 0;
            while ((format = NativeMethods.EnumClipboardFormats(format)) != 0)
            {
                if (!IsOleBookkeepingFormat(format))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// WinForms の Clipboard.Clear() は空の DataObject をクリップボードに置くので、
        /// その後には "DataObject" と "Ole Private Data" だけが残る (実測)。中身は無いので空として扱う。
        /// </summary>
        private static bool IsOleBookkeepingFormat(uint format)
        {
            if (format < FirstRegisteredFormat)
            {
                return false;
            }

            StringBuilder name = new StringBuilder(64);
            if (NativeMethods.GetClipboardFormatName(format, name, name.Capacity) == 0)
            {
                return false;
            }

            string s = name.ToString();
            return s == "DataObject" || s == "Ole Private Data";
        }

        private static string ReadUnicodeText()
        {
            IntPtr handle = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            IntPtr ptr = NativeMethods.GlobalLock(handle);
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                // GlobalSize は確保されたサイズなので、終端の NUL の後ろに余りがありうる。
                // 確保サイズの範囲だけを読み、最初の NUL までを本文とする
                // (NUL が無い壊れたデータでも範囲外を読まない)。
                ulong bytes = NativeMethods.GlobalSize(handle).ToUInt64();
                int maxChars = (int)Math.Min(bytes / 2, (ulong)int.MaxValue);
                string raw = Marshal.PtrToStringUni(ptr, maxChars);
                int nul = raw.IndexOf('\0');
                return nul >= 0 ? raw.Substring(0, nul) : raw;
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }

        /// <summary>
        /// CF_HDROP (DROPFILES) からパスの一覧を取り出す。
        /// DragQueryFile(i) は呼ぶたびに先頭から i 件を読み飛ばすので、全件を取ると件数の 2 乗の
        /// 時間がかかる (実測: 1 万件で約 3 秒)。名前の並びを 1 回だけ走査して取り出す。
        /// </summary>
        private static string[] ReadFileDrop()
        {
            IntPtr handle = NativeMethods.GetClipboardData(NativeMethods.CF_HDROP);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            IntPtr ptr = NativeMethods.GlobalLock(handle);
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                long size = (long)Math.Min(NativeMethods.GlobalSize(handle).ToUInt64(), (ulong)int.MaxValue);
                if (size < DropFilesSize)
                {
                    return null;
                }

                int offset = Marshal.ReadInt32(ptr, 0);
                bool wide = Marshal.ReadInt32(ptr, DropFilesWideOffset) != 0;
                if (offset < DropFilesSize || offset >= size)
                {
                    return null;
                }

                // 名前は NUL 区切りで並び、空の名前 (NUL が 2 つ続く) で終わる。
                // ANSI 版はシステムのコードページ。Shift-JIS の 2 バイト目に 0 は現れないので NUL で区切れる
                IntPtr names = IntPtr.Add(ptr, offset);
                int length = (int)(size - offset);
                string block = wide
                    ? Marshal.PtrToStringUni(names, length / 2)
                    : Marshal.PtrToStringAnsi(names, length);

                List<string> files = new List<string>();
                int start = 0;
                while (start < block.Length)
                {
                    int end = block.IndexOf('\0', start);
                    if (end < 0 || end == start)
                    {
                        // 終端の NUL が無い (確保範囲で途切れた) 名前は信用しない。空の名前は一覧の終わり
                        break;
                    }
                    files.Add(block.Substring(start, end - start));
                    start = end + 1;
                }
                return files.ToArray();
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }
    }
}
