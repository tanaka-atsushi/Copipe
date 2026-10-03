using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Copipe.UI
{
    /// <summary>
    /// 小窓の見出しの左側。今いる階層を「定型文 > 社外 > 挨拶」のように出す。
    /// 階層名ごとの位置が分かるので、クリックでその階層へ移動でき、ドラッグした項目を階層名に落とせる。
    /// Text には全体 (区切りを含む) を入れておく (検証ハーネスが読む)。
    /// </summary>
    internal sealed class BreadcrumbLabel : HeaderLabel
    {
        public const string Separator = " > ";

        private const TextFormatFlags Flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                                              TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;

        private static readonly Color HighlightColor = Color.FromArgb(0xCC, 0xE4, 0xF7);

        private readonly List<string> _segments = new List<string>();
        private int _highlight = -1;

        /// <summary>階層名 (一番上から順)。</summary>
        public void SetSegments(IList<string> segments)
        {
            _segments.Clear();
            _segments.AddRange(segments);
            _highlight = -1;
            Text = string.Join(Separator, _segments.ToArray());
            Invalidate();
        }

        /// <summary>ドラッグした項目を落とせる階層名を強調する。-1 で消す。</summary>
        public int HighlightIndex
        {
            get { return _highlight; }
            set
            {
                if (_highlight != value)
                {
                    _highlight = value;
                    Invalidate();
                }
            }
        }

        /// <summary>
        /// 階層名がクリックされたとき。引数は階層の番号 (0 が一番上)。
        /// 今いる階層 (右端) のクリックでは出さない。
        /// </summary>
        public event Action<int> SegmentClicked;

        // 左ボタンを押した階層名 (押していなければ -1)
        private int _pressedSegment = -1;

        /// <summary>
        /// 同じ階層名の上で押して離したら、クリックとして扱う。
        /// WinForms の MouseClick は、短い間隔で続けたクリックをダブルクリックの 2 回目とみなして出さないので使わない
        /// (階層名を続けてクリックすると 2 回目が効かなかった。E2E で実測)。
        /// </summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _pressedSegment = e.Button == MouseButtons.Left ? SegmentAt(e.Location) : -1;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int pressed = _pressedSegment;
            _pressedSegment = -1;
            if (e.Button != MouseButtons.Left || pressed < 0 || SegmentAt(e.Location) != pressed)
            {
                return;
            }
            Action<int> handler = SegmentClicked;
            if (pressed < _segments.Count - 1 && handler != null)
            {
                handler(pressed);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            // 移動できる階層名 (今いる階層より上) の上では、指の形にする
            int segment = SegmentAt(e.Location);
            Cursor = (segment >= 0 && segment < _segments.Count - 1) ? Cursors.Hand : Cursors.Default;
        }

        /// <summary>その位置 (この部品の中の座標) にある階層名の番号。無ければ -1。</summary>
        public int SegmentAt(Point location)
        {
            if (location.Y < 0 || location.Y >= Height)
            {
                return -1;
            }
            using (Graphics g = CreateGraphics())
            {
                List<Rectangle> rects = LayoutSegments(g);
                for (int i = 0; i < rects.Count; i++)
                {
                    if (location.X >= rects[i].Left && location.X < rects[i].Right)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            List<Rectangle> rects = LayoutSegments(e.Graphics);
            for (int i = 0; i < rects.Count; i++)
            {
                if (i == _highlight)
                {
                    using (SolidBrush brush = new SolidBrush(HighlightColor))
                    {
                        e.Graphics.FillRectangle(brush, rects[i]);
                    }
                }
                TextRenderer.DrawText(e.Graphics, _segments[i], Font, rects[i], ForeColor, Flags);
                if (i < rects.Count - 1)
                {
                    Rectangle sep = new Rectangle(rects[i].Right, 0, SeparatorWidth(e.Graphics), Height);
                    TextRenderer.DrawText(e.Graphics, Separator, Font, sep, SystemColors.GrayText, Flags);
                }
            }
        }

        /// <summary>階層名ごとの枠 (区切りは含まない)。</summary>
        private List<Rectangle> LayoutSegments(Graphics g)
        {
            List<Rectangle> rects = new List<Rectangle>();
            int x = 0;
            int separator = SeparatorWidth(g);
            foreach (string segment in _segments)
            {
                int width = TextRenderer.MeasureText(g, segment, Font, Size.Empty, Flags).Width;
                rects.Add(new Rectangle(x, 0, width, Height));
                x += width + separator;
            }
            return rects;
        }

        private int SeparatorWidth(Graphics g)
        {
            return TextRenderer.MeasureText(g, Separator, Font, Size.Empty, Flags).Width;
        }
    }
}
