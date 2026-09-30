using System;
using System.Drawing;
using System.Windows.Forms;

namespace Copipe.UI
{
    /// <summary>
    /// 行をドラッグできる ListBox。左ボタンを押したまま少し動かすとドラッグを始め、
    /// 離すまでの動きを画面座標で知らせる。
    /// ドラッグ中はマウスの動きを ListBox 本来の処理に渡さない (押したまま動かすと選択が動いてしまうため)。
    /// OLE のドラッグ＆ドロップ (DoDragDrop) は使わず、小窓の中だけで扱う。
    /// </summary>
    internal sealed class DragListBox : ListBox
    {
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_MBUTTONDBLCLK = 0x0209;
        private const int WM_CAPTURECHANGED = 0x0215;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int MK_LBUTTON = 0x0001;

        private bool _pressed;
        private Point _pressPoint;
        private int _pressIndex = -1;
        private bool _dragging;

        /// <summary>ドラッグを始めてよいか (定型文モードの間だけ true)。</summary>
        public bool DragEnabled { get; set; }

        /// <summary>今ドラッグしているか。</summary>
        public bool IsDragging
        {
            get { return _dragging; }
        }

        /// <summary>
        /// ドラッグが終わった直後のクリックを、項目を選ぶ操作として扱わないための印。
        /// ListBox は離したときにクリックとダブルクリックを出すので、受け取る側で見て消す。
        /// </summary>
        public bool SuppressClick { get; set; }

        /// <summary>行 (位置) を返す。ドラッグを始めてよい行でなければ -1 を返させる。</summary>
        public Func<Point, int> DraggableRowAt { get; set; }

        /// <summary>
        /// ホイールが回されたとき。引数は回した量 (上へ +、下へ -。1 ノッチが 120)。
        /// 一覧はスクロールしない (ホイールは小窓のモードの切り替えに使う)。
        /// </summary>
        public event Action<int> WheelTurned;

        /// <summary>ドラッグを始めたとき。引数は行の位置。</summary>
        public event Action<int> DragBegan;

        /// <summary>ドラッグ中にマウスが動いたとき。引数は画面座標。</summary>
        public event Action<Point> DragMoved;

        /// <summary>左ボタンを離してドラッグが終わったとき。引数は画面座標。</summary>
        public event Action<Point> DragDropped;

        /// <summary>ドラッグが途中で終わったとき (マウスの捕捉を失った・取り消された)。</summary>
        public event Action DragAborted;

        /// <summary>ドラッグをやめる (Esc やホットキーを離したとき)。DragAborted は出さない。</summary>
        public void CancelDrag()
        {
            if (!_dragging)
            {
                return;
            }
            _dragging = false;
            _pressed = false;
            // この後に左ボタンを離しても、クリックとして扱わない
            SuppressClick = true;
            Capture = false;
        }

        protected override void WndProc(ref Message m)
        {
            // 右クリックメニューを外のクリックで閉じたときの、そのクリックは無視する (選択も、クリックも、ドラッグも)
            if (m.Msg >= WM_LBUTTONDOWN && m.Msg <= WM_MBUTTONDBLCLK && RowMenu.IsSwallowingClick)
            {
                return;
            }
            switch (m.Msg)
            {
                case WM_MOUSEWHEEL:
                {
                    Action<int> wheel = WheelTurned;
                    if (wheel != null)
                    {
                        wheel((short)((m.WParam.ToInt64() >> 16) & 0xFFFF));
                        return;
                    }
                    break;
                }
                case WM_LBUTTONDOWN:
                {
                    SuppressClick = false;
                    _pressPoint = PointFromLParam(m.LParam);
                    _pressIndex = (DragEnabled && DraggableRowAt != null) ? DraggableRowAt(_pressPoint) : -1;
                    _pressed = true;
                    break;
                }
                case WM_MOUSEMOVE:
                {
                    Point p = PointFromLParam(m.LParam);
                    if (_dragging)
                    {
                        Raise(DragMoved, PointToScreen(p));
                        return;
                    }
                    if (_pressed && _pressIndex >= 0 && DragEnabled && (m.WParam.ToInt64() & MK_LBUTTON) != 0 &&
                        IsBeyondDragSize(p))
                    {
                        _dragging = true;
                        Capture = true;
                        Action<int> began = DragBegan;
                        if (began != null)
                        {
                            began(_pressIndex);
                        }
                        Raise(DragMoved, PointToScreen(p));
                        return;
                    }
                    break;
                }
                case WM_LBUTTONUP:
                {
                    _pressed = false;
                    if (_dragging)
                    {
                        // ListBox 本来の処理には渡さない (離した位置の行が選ばれ、クリックとしても扱われるため)。
                        // マウスの捕捉を外すと、ListBox は押している間の追跡をやめる
                        _dragging = false;
                        Raise(DragDropped, PointToScreen(PointFromLParam(m.LParam)));
                        Capture = false;
                        return;
                    }
                    break;
                }
                case WM_CAPTURECHANGED:
                {
                    if (_dragging && m.LParam != Handle)
                    {
                        _dragging = false;
                        _pressed = false;
                        SuppressClick = true;
                        Action aborted = DragAborted;
                        if (aborted != null)
                        {
                            aborted();
                        }
                    }
                    break;
                }
            }
            base.WndProc(ref m);
        }

        private bool IsBeyondDragSize(Point p)
        {
            Size size = SystemInformation.DragSize;
            return Math.Abs(p.X - _pressPoint.X) > size.Width / 2 || Math.Abs(p.Y - _pressPoint.Y) > size.Height / 2;
        }

        private static void Raise(Action<Point> handler, Point point)
        {
            if (handler != null)
            {
                handler(point);
            }
        }

        private static Point PointFromLParam(IntPtr lParam)
        {
            long value = lParam.ToInt64();
            return new Point((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
        }
    }
}
