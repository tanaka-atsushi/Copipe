using System;
using System.Drawing;

namespace Copipe.UI
{
    /// <summary>小窓を出す位置の計算。画面に依存しない純粋関数にして検証しやすくしている。</summary>
    public static class PopupPlacement
    {
        /// <summary>
        /// カーソルの右下に offset だけ離して置く。右端・下端で入りきらなければ
        /// カーソルの反対側 (左・上) に回り込み、それでも入らなければ作業領域の中に押し込む。
        /// 作業領域より大きい場合は左上を揃える。
        /// </summary>
        /// <param name="cursor">カーソル位置 (スクリーン座標。サブモニターではマイナスになりうる)</param>
        /// <param name="size">小窓の大きさ</param>
        /// <param name="workingArea">カーソルがあるモニターの作業領域 (タスクバーを除いた範囲)</param>
        /// <param name="offset">カーソルからの距離</param>
        public static Point Place(Point cursor, Size size, Rectangle workingArea, int offset)
        {
            int x = cursor.X + offset;
            if (x + size.Width > workingArea.Right)
            {
                x = cursor.X - offset - size.Width;
            }

            int y = cursor.Y + offset;
            if (y + size.Height > workingArea.Bottom)
            {
                y = cursor.Y - offset - size.Height;
            }

            // Min を先に取るので、作業領域より大きいときは Max 側 (左上) が勝つ
            x = Math.Max(workingArea.Left, Math.Min(x, workingArea.Right - size.Width));
            y = Math.Max(workingArea.Top, Math.Min(y, workingArea.Bottom - size.Height));
            return new Point(x, y);
        }
    }
}
