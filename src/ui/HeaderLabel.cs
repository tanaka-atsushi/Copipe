using System.Windows.Forms;

namespace Copipe.UI
{
    /// <summary>
    /// 小窓の見出しに使う Label。表示した瞬間に背景を塗る。
    /// 標準の Label は二重バッファのため背景も文字も WM_PAINT でまとめて描き (AllPaintingInWmPaint)、
    /// 表示の時点 (WM_ERASEBKGND) では何も塗らない。WM_PAINT が届くまでの間、見出しが黒く見えた。
    /// 一覧 (ListBox) と同じく、表示の時点で背景を塗るようにする。文字が短いので、二重バッファが無くてもちらつかない。
    /// </summary>
    internal class HeaderLabel : Label
    {
        public HeaderLabel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, false);
        }
    }
}
