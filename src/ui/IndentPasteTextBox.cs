using System.Windows.Forms;
using Copipe.Services;

namespace Copipe.UI
{
    /// <summary>
    /// 定型文の本文の欄。貼り付けるとき (Ctrl+V・右クリックの「貼り付け」・Shift+Insert。どれも WM_PASTE になる)、
    /// Outlook・Word からコピーした文なら、段落の字下げを行頭の全角スペースにして入れる (WordIndent)。
    /// それ以外は普通の貼り付けと同じ。
    /// </summary>
    internal sealed class IndentPasteTextBox : TextBox
    {
        private const int WM_PASTE = 0x0302;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_PASTE && !ReadOnly)
            {
                string text = ClipboardReader.ReadTextKeepingIndent(Handle);
                if (text != null)
                {
                    SelectedText = text;
                    return;
                }
            }
            base.WndProc(ref m);
        }
    }
}
