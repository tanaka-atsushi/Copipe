using System.Drawing;
using System.Windows.Forms;
using Copipe.Interop;

namespace Copipe.UI
{
    /// <summary>
    /// 定型文のダイアログと確認メッセージの持ち主にする、見えないウインドウ (画面外の 1×1)。
    /// ダイアログを閉じると Windows は持ち主を前面にするので、閉じた直後も Copipe が前面のままでいられ、
    /// 元のアプリに前面を戻せる。持ち主が無いと、閉じた瞬間に別のアプリ (実測では VS Code) が
    /// 前面になり、Copipe からは前面を戻せなくなる。
    /// </summary>
    internal sealed class DialogOwner : Form
    {
        public DialogOwner()
        {
            // 小窓 ("Copipe") と区別できるよう、名前は付けない
            Text = string.Empty;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // Alt+Tab に出さない
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;
                return cp;
            }
        }
    }
}
