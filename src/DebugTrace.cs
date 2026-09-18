using System;
using System.IO;
using System.Text;

namespace Copipe
{
    // 一時的な調査用 (前面が元のアプリに戻らない原因を調べる)。調べ終わったら消す
    internal static class DebugTrace
    {
        private static readonly string Path = Environment.GetEnvironmentVariable("COPIPE_TRACE");

        public static void Write(string text)
        {
            if (string.IsNullOrEmpty(Path))
            {
                return;
            }
            try
            {
                File.AppendAllText(Path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + text + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        public static string Window(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            Interop.NativeMethods.GetClassNameForTrace(h, sb, 256);
            return h.ToString("X") + ":" + sb;
        }
    }
}
