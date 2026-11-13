using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Copipe.UI
{
    /// <summary>
    /// 小窓の中の 1 つの欄。見出しと一覧 (クリップボードの履歴か、定型文の 1 つの階層) を表示する。
    /// 履歴と定型文を切り替えて出すときは小窓に 1 つ、並べて出すときは 2 つ置く (PopupForm)。
    /// 小窓の位置・表示・ホイールは PopupForm が受け持つ。
    /// </summary>
    internal sealed class PopupPane : Panel
    {
        // 96 DPI (拡大率 100%) のときの大きさ。実際の DPI に合わせて拡大する
        private const int BaseItemHeight = 22;
        private const int BaseHeaderHeight = 24;

        /// <summary>見出しの下の線 (並べて出しているとき、数字キーが効く欄の印) の太さ (96 DPI のとき)。</summary>
        private const int BaseAccentHeight = 2;

        /// <summary>一度に見せる行数の上限。これを超える分はスクロールで見る。</summary>
        private const int MaxVisibleItems = 15;

        // 小窓から他のアプリへドラッグしている間 (DoDragDrop の中) は true。
        // 並べて出しているときは、もう一方の欄に戻しても受け取らないよう、欄どうしで共有する (小窓は 1 つだけ)
        private static bool s_draggingOut;

        private readonly DragListBox _list;
        private readonly Panel _header;
        private readonly BreadcrumbLabel _title;
        private readonly Label _hint;
        private bool _empty = true;
        private bool _dimmed;
        private bool _accent;

        // 一覧の上部にあるピン止めの行数 (履歴だけ。定型文では 0)
        private int _pinnedCount;
        // 最後に 📌 をクリックした時刻。直後のダブルクリック (2 回目のクリック) を入力として扱わないため
        private DateTime _pinClickedAt = DateTime.MinValue;
        // 最後にグループを 1 回のクリックで開いた時刻。直後のダブルクリックの 2 回目を、開いた先の行の操作にしないため
        private DateTime _groupClickedAt = DateTime.MinValue;

        /// <summary>ピン止めの行の右端にある 📌 の幅 (96 DPI のとき)。ここをクリックするとピン止めを外す。</summary>
        private const int BasePinIconWidth = 24;

        // ドラッグ中の状態。_hover はマウスの下にある先 (落とせるかは問わない)、
        // _dropTarget は実際に落とせる先 (落とせなければ Nowhere)
        private readonly Timer _springTimer;
        private int _dragSource = -1;
        private DropTarget _hover = DropTarget.Nowhere;
        private DropTarget _dropTarget = DropTarget.Nowhere;

        /// <summary>ドラッグした項目を止めたままにすると、グループ (階層) を開くまでの時間。</summary>
        private const int SpringOpenMs = 1000;

        private static readonly Color DropSwapColor = Color.FromArgb(0xCC, 0xE4, 0xF7);

        /// <summary>数字キーが効かない欄で選んでいる行の色。</summary>
        private static readonly Color DimSelectedColor = Color.FromArgb(0xE3, 0xE3, 0xE3);

        // 入力した行を点滅させる (FlashItem)。点滅させている行と今の段階 (奇数のとき選択の色)、
        // ↑↓ で続きから動かすための基準の行
        private readonly Timer _flashTimer;
        private int _flashIndex = -1;
        private int _flashStep;
        private int _anchorIndex = -1;

        /// <summary>点滅の 1 段階 (消える・点く) の時間。消える→点く→消える の 2 段階で終わる。</summary>
        private const int FlashStepMs = 50;
        private const int FlashSteps = 2;

        public PopupPane(Color backColor)
        {
            BackColor = backColor;

            // 将来、項目のダブルクリックやドラッグ＆ドロップを足せるよう、Label ではなく ListBox で作る
            _list = new DragListBox();
            _list.Dock = DockStyle.Fill;
            _list.BorderStyle = BorderStyle.None;
            _list.BackColor = BackColor;
            _list.ForeColor = Color.FromArgb(0x1A, 0x1A, 0x1A);
            _list.Font = SystemFonts.MessageBoxFont;
            _list.DrawMode = DrawMode.OwnerDrawFixed;
            _list.ItemHeight = ScaleByDpi(BaseItemHeight);
            _list.IntegralHeight = false;
            _list.SelectionMode = SelectionMode.One;
            _list.DrawItem += OnDrawItem;
            // ListBox は、1 回目のクリックで MouseClick、ダブルクリックの 2 回目で MouseDoubleClick を出す
            // (2 回目で MouseClick は出ない)。設定に応じてどちらか一方だけで選ぶので、
            // シングルクリックの設定でうっかりダブルクリックしても選ぶのは 1 回だけになる
            _list.MouseClick += OnListMouseClick;
            _list.MouseDoubleClick += OnListMouseDoubleClick;
            _list.MouseUp += OnListMouseUp;
            _list.MouseMove += OnListMouseMove;
            _list.DraggableRowAt = DraggableRowAt;
            _list.DragBegan += OnDragBegan;
            _list.DragMoved += OnDragMoved;
            _list.DragDropped += OnDragDropped;
            _list.DragAborted += delegate { EndDrag(DropTarget.Nowhere); };
            _list.WheelTurned += delegate(int delta) { Raise(WheelTurned, delta); };
            Controls.Add(_list);

            _springTimer = new Timer();
            _springTimer.Interval = SpringOpenMs;
            _springTimer.Tick += OnSpringTimerTick;

            _flashTimer = new Timer();
            _flashTimer.Interval = FlashStepMs;
            _flashTimer.Tick += OnFlashTimerTick;

            // 見出し: 左に今のモード、右に切り替え方。一覧より後に追加して、先に上へ寄せる
            // (Dock は後から追加したものから順に場所を取る)
            _header = new Panel();
            _header.Dock = DockStyle.Top;
            _header.Height = ScaleByDpi(BaseHeaderHeight);
            _header.BackColor = BackColor;
            _header.Paint += OnHeaderPaint;

            _title = new BreadcrumbLabel();
            _title.SegmentClicked += delegate(int level)
            {
                if (!_list.IsDragging)
                {
                    Raise(LevelClicked, level);
                }
            };
            _title.Dock = DockStyle.Left;
            _title.AutoSize = true;
            _title.UseMnemonic = false;
            _title.Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold);
            _title.ForeColor = _list.ForeColor;
            _title.TextAlign = ContentAlignment.MiddleLeft;
            _header.Controls.Add(_title);

            _hint = new HeaderLabel();
            _hint.Dock = DockStyle.Right;
            _hint.AutoSize = true;
            _hint.UseMnemonic = false;
            _hint.Font = SystemFonts.MessageBoxFont;
            _hint.ForeColor = SystemColors.GrayText;
            _hint.TextAlign = ContentAlignment.MiddleRight;
            _header.Controls.Add(_hint);

            // 見出しの空いたところ (階層名以外) をつかんで、小窓を動かせる。階層名は自分でカーソルを決める
            _header.Cursor = Cursors.SizeAll;
            _title.Cursor = Cursors.Default;

            Controls.Add(_header);

            // どこを押しても、この欄を使い始めたことを知らせる (並べて出しているとき、数字キーが効く欄を移すため)
            foreach (Control control in new Control[] { _list, _header, _title, _hint })
            {
                control.MouseDown += delegate { RaiseEngaged(); };
            }

            AcceptExternalDrops(this);
        }

        /// <summary>
        /// この欄を押した・他のアプリからドラッグしてきたとき。行の操作 (クリック・ドラッグ・右クリック) の前に出る。
        /// 並べて出しているとき、受け取る側がこの欄を数字キーの効く欄にする。
        /// </summary>
        public event EventHandler Engaged;

        /// <summary>
        /// 他のアプリから空きの枠の行 (0 始まり) に、ファイル・フォルダー (path) か文字列 (text) を落とされたとき。
        /// path と text はどちらか一方だけが入る。空きの枠以外・それ以外のものは無視して、ここには来ない。
        /// ExternalDropAnywhere ならこの欄のどこに落としてもよく、行は 0 で届く。
        /// </summary>
        public event Action<int, string, string> ExternalDropped;

        /// <summary>他のアプリからのドロップを受け付けるか。</summary>
        public bool AllowExternalDrop { get; set; }

        /// <summary>空きの枠に限らず、この欄のどこに落としてもよいか (履歴でピン止めするとき)。</summary>
        public bool ExternalDropAnywhere { get; set; }

        /// <summary>見出しの空いたところ (つかんで小窓を動かせるところ)。PopupForm が動かす処理を付ける。</summary>
        public IList<Control> Grips
        {
            get { return new Control[] { _header, _hint }; }
        }

        private void RaiseEngaged()
        {
            EventHandler handler = Engaged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        // AllowDrop の無い部品の上では落とせず親にも伝わらないので、全部品に付ける
        private void AcceptExternalDrops(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += OnExternalDragEnter;
            control.DragOver += OnExternalDragOver;
            control.DragDrop += OnExternalDragDrop;
            foreach (Control child in control.Controls)
            {
                AcceptExternalDrops(child);
            }
        }

        private static string DroppedPath(IDataObject data)
        {
            string[] paths = data.GetData(DataFormats.FileDrop) as string[];
            return paths != null && paths.Length > 0 ? paths[0] : null;
        }

        private static string DroppedText(IDataObject data)
        {
            string text = data.GetData(DataFormats.UnicodeText) as string;
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        /// <summary>落とせる行 (空きの枠) の位置。その位置が空きの枠でなければ -1。どこでもよいなら 0。</summary>
        private int EmptyRowAtScreen(int x, int y)
        {
            if (ExternalDropAnywhere)
            {
                return 0;
            }
            int index = RowIndexAt(_list.PointToClient(new Point(x, y)));
            PopupRow row = index < 0 ? null : _list.Items[index] as PopupRow;
            return (row != null && row.Kind == PopupRowKind.Empty) ? index : -1;
        }

        private void OnExternalDragEnter(object sender, DragEventArgs e)
        {
            if (!s_draggingOut)
            {
                // 落とす先の欄として使う (並べて出しているとき、受け取る側の扱いをこの欄に合わせる)
                RaiseEngaged();
            }
            OnExternalDragOver(sender, e);
        }

        private void OnExternalDragOver(object sender, DragEventArgs e)
        {
            // 小窓から外へ持ち出している項目は、小窓に戻しても受け取らない
            bool ok = !s_draggingOut && AllowExternalDrop && (DroppedPath(e.Data) != null || DroppedText(e.Data) != null) &&
                      EmptyRowAtScreen(e.X, e.Y) >= 0;
            e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnExternalDragDrop(object sender, DragEventArgs e)
        {
            // ファイルは文字列の形でも入っていることがあるので、ファイルを先に見る
            string path = DroppedPath(e.Data);
            string text = path == null ? DroppedText(e.Data) : null;
            int row = EmptyRowAtScreen(e.X, e.Y);
            Action<int, string, string> handler = ExternalDropped;
            if (AllowExternalDrop && handler != null && row >= 0 && (path != null || text != null))
            {
                handler(row, path, text);
            }
        }

        /// <summary>
        /// 行を選ぶ操作 (クリックまたはダブルクリック) がされたとき。引数は行の位置 (0 始まり)。
        /// 空きの枠や案内の行では出さない。何をするか (入力する・グループに入る) は受け取る側が決める。
        /// </summary>
        public event Action<int> RowActivated;

        /// <summary>行が右クリックされたとき。引数は行の位置 (0 始まり) と、画面上のクリック位置。</summary>
        public event Action<int, Point> RowContextRequested;

        /// <summary>定型文の空きの枠の行がクリックされたとき (登録画面を出すため)。引数は行の位置。</summary>
        public event Action<int> EmptyRowClicked;

        /// <summary>true ならクリック 1 回で、false (既定) ならダブルクリックで RowActivated を出す。</summary>
        public bool InsertOnSingleClick { get; set; }

        /// <summary>一覧のウインドウを先に作っておき、初回の表示を速くする。</summary>
        public void Prepare()
        {
            IntPtr pane = Handle;
            IntPtr list = _list.Handle;
            GC.KeepAlive(pane);
            GC.KeepAlive(list);
        }

        /// <summary>
        /// ドラッグした項目をその先に落とせるか (受け取る側が決める)。
        /// null なら、どこにでも落とせる。
        /// </summary>
        public Func<DropTarget, bool> DropValidator { get; set; }

        /// <summary>行のドラッグを始めたとき。引数は行の位置。</summary>
        public event Action<int> RowDragStarted;

        /// <summary>
        /// ドラッグしている行を小窓の外へ出したとき、他のアプリに渡すデータ (受け取る側が作る)。
        /// null を返すと外へは出さず、小窓の中のドラッグを続ける。
        /// </summary>
        public Func<IDataObject> OutsideDragData { get; set; }

        /// <summary>
        /// ドラッグしたまま、グループの行の中央か見出しの階層名で止めたとき。
        /// 受け取る側がそのグループ (階層) を開けば、そのままドラッグを続けられる。
        /// </summary>
        public event Action<DropTarget> DragOpenRequested;

        /// <summary>見出しの階層名がクリックされたとき。引数は階層 (0 が一番上)。今いる階層では出さない。</summary>
        public event Action<int> LevelClicked;

        /// <summary>一覧の上でホイールを回したとき。引数は回した量 (上へ +、下へ -。1 ノッチが 120)。</summary>
        public event Action<int> WheelTurned;

        /// <summary>ピン止めの行の 📌 がクリックされたとき。引数は行の位置。</summary>
        public event Action<int> PinIconClicked;

        /// <summary>行数が変わって、欄に要る高さ (ContentHeight) が変わったかもしれないとき。</summary>
        public event EventHandler ContentChanged;

        /// <summary>一覧の上部にあるピン止めの行数。数字キーの番号は、この後の行から 1 が付く。</summary>
        public int PinnedCount
        {
            get { return _pinnedCount; }
        }

        /// <summary>
        /// ドラッグが終わったとき。引数は落とした先。落とせない先で離した・取り消した・小窓を消したときは
        /// Kind が None。
        /// </summary>
        public event Action<DropTarget> RowDragEnded;

        /// <summary>行をドラッグできるか。</summary>
        public bool AllowDrag
        {
            get { return _list.DragEnabled; }
            set
            {
                _list.DragEnabled = value;
                if (!value)
                {
                    CancelDrag();
                }
            }
        }

        /// <summary>今ドラッグしているか。</summary>
        public bool IsDragging
        {
            get { return _list.IsDragging; }
        }

        /// <summary>
        /// 数字キーが効かない欄として、番号と見出しを灰色で出すか (並べて出しているときの、もう一方の欄)。
        /// </summary>
        public bool Dimmed
        {
            get { return _dimmed; }
            set
            {
                if (_dimmed == value)
                {
                    return;
                }
                _dimmed = value;
                _title.ForeColor = value ? SystemColors.GrayText : _list.ForeColor;
                _list.Invalidate();
            }
        }

        /// <summary>見出しの下に線を引いて、数字キーが効く欄であることを示すか (並べて出しているとき)。</summary>
        public bool Accent
        {
            get { return _accent; }
            set
            {
                if (_accent != value)
                {
                    _accent = value;
                    _header.Invalidate();
                }
            }
        }

        private void OnHeaderPaint(object sender, PaintEventArgs e)
        {
            if (!_accent)
            {
                return;
            }
            int height = ScaleByDpi(BaseAccentHeight);
            e.Graphics.FillRectangle(SystemBrushes.Highlight, 0, _header.Height - height, _header.Width, height);
        }

        /// <summary>ドラッグを取り消す (Esc・ホットキーを離したとき)。RowDragEnded (None) を出す。</summary>
        public void CancelDrag()
        {
            if (!_list.IsDragging)
            {
                return;
            }
            _list.CancelDrag();
            EndDrag(DropTarget.Nowhere);
        }

        /// <summary>
        /// 今の一覧の中で、ドラッグしている項目の行 (薄く出す)。
        /// 別の階層を開いてドラッグを続けているときは -1。
        /// </summary>
        public void SetDragSource(int index)
        {
            _dragSource = index;
            _list.Invalidate();
        }

        /// <summary>見出しを変える。title は今のモード (例: クリップボード履歴)、hint は切り替え方 (例: Tab: 定型文)。</summary>
        public void SetHeader(string title, string hint)
        {
            SetHeaderPath(new[] { title }, hint);
        }

        /// <summary>
        /// 見出しを、階層の並び (例: 定型文 > 社外 > 挨拶) にする。
        /// 階層名は、ドラッグした項目を落とす先になる。
        /// </summary>
        public void SetHeaderPath(IList<string> path, string hint)
        {
            _title.SetSegments(path);
            _hint.Text = hint;
        }

        /// <summary>
        /// 一覧を、履歴の並びにする。pinned (ピン止め) を上部に番号なしで出し、その後に items を出す。
        /// items が slotCount 件に満たない分は空きの行 (文字なし)で埋める (定型文と同じく、いつも同じ行数)。
        /// </summary>
        public void SetHistory(IList<string> pinned, IList<string> items, int slotCount)
        {
            List<PopupRow> rows = new List<PopupRow>();
            foreach (string text in pinned)
            {
                PopupRow row = new PopupRow(PopupRowKind.Item, text);
                row.IsPinned = true;
                rows.Add(row);
            }
            foreach (string text in items)
            {
                rows.Add(new PopupRow(PopupRowKind.Item, text));
            }
            for (int i = items.Count; i < slotCount; i++)
            {
                rows.Add(new PopupRow(PopupRowKind.Empty, null));
            }
            SetRows(rows, string.Empty);
        }

        /// <summary>一覧の中身を差し替える。行が無いときは emptyMessage を灰色で出す。</summary>
        public void SetRows(IList<PopupRow> rows, string emptyMessage)
        {
            _empty = (rows == null || rows.Count == 0);
            _pinnedCount = 0;
            while (!_empty && _pinnedCount < rows.Count && rows[_pinnedCount].IsPinned)
            {
                _pinnedCount++;
            }

            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                if (_empty)
                {
                    _list.Items.Add(emptyMessage);
                }
                else
                {
                    foreach (PopupRow row in rows)
                    {
                        _list.Items.Add(row);
                    }
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            _list.SelectedIndex = -1;
            _anchorIndex = -1;
            StopFlash();
            // 中身が変わったら、前の一覧での落とす先は無効 (ドラッグは続ける)
            _hover = DropTarget.Nowhere;
            _dropTarget = DropTarget.Nowhere;
            _springTimer.Stop();
            EventHandler changed = ContentChanged;
            if (changed != null)
            {
                changed(this, EventArgs.Empty);
            }
        }

        /// <summary>今の行数を出すのに要る高さ (見出しを含む。15 行を超える分はスクロール)。</summary>
        public int ContentHeight
        {
            get
            {
                int visible = Math.Max(1, Math.Min(_list.Items.Count, MaxVisibleItems));
                return ScaleByDpi(BaseHeaderHeight) + visible * _list.ItemHeight;
            }
        }

        /// <summary>小窓を隠すとき。ドラッグをやめ、点滅を止める。</summary>
        public void Reset()
        {
            CancelDrag();
            StopFlash();
        }

        private void OnListMouseClick(object sender, MouseEventArgs e)
        {
            if (_list.SuppressClick)
            {
                // ドラッグして離しただけ。項目を選んだことにしない
                _list.SuppressClick = false;
                return;
            }
            // 📌 のクリックは「貼り付けの操作」の設定によらず、1 回でピン止めを外す (入力はしない)
            if (e.Button == MouseButtons.Left && TryClickPinIcon(e.Location))
            {
                return;
            }
            // 空きの枠は「貼り付けの操作」の設定によらず、1 回のクリックで登録画面を出す。
            // 続く 2 回目のクリックは、グループと同じく使わない
            int emptyIndex = EmptyRowAt(e.Location);
            if (e.Button == MouseButtons.Left && emptyIndex >= 0)
            {
                _groupClickedAt = DateTime.UtcNow;
                Raise(EmptyRowClicked, emptyIndex);
                return;
            }
            // グループは「貼り付けの操作」の設定によらず、1 回のクリックで開く
            if (InsertOnSingleClick || IsGroupAt(e.Location))
            {
                if (!InsertOnSingleClick)
                {
                    _groupClickedAt = DateTime.UtcNow;
                }
                Activate(e);
            }
        }

        /// <summary>その位置が定型文の空きの枠の行なら、その行の位置。そうでなければ -1。</summary>
        private int EmptyRowAt(Point location)
        {
            int index = RowIndexAt(location);
            PopupRow row = index < 0 ? null : _list.Items[index] as PopupRow;
            return row != null && row.Kind == PopupRowKind.Empty ? index : -1;
        }

        private bool IsGroupAt(Point location)
        {
            int index = RowIndexAt(location);
            PopupRow row = index < 0 ? null : _list.Items[index] as PopupRow;
            return row != null && row.Kind == PopupRowKind.Group;
        }

        private void OnListMouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (_list.SuppressClick)
            {
                _list.SuppressClick = false;
                return;
            }
            // 📌 のクリックに続く 2 回目のクリック。行は外れて普通の履歴になっているが、入力しない。
            // グループを開いたクリックに続く 2 回目も同じ (開いた先の行を選ばない)
            if ((DateTime.UtcNow - _pinClickedAt).TotalMilliseconds <= SystemInformation.DoubleClickTime ||
                (DateTime.UtcNow - _groupClickedAt).TotalMilliseconds <= SystemInformation.DoubleClickTime)
            {
                return;
            }
            if (!InsertOnSingleClick)
            {
                Activate(e);
            }
        }

        /// <summary>その位置がピン止めの行の 📌 の上なら、その行の位置。そうでなければ -1。</summary>
        private int PinIconRowAt(Point location)
        {
            int index = RowIndexAt(location);
            if (index < 0)
            {
                return -1;
            }
            PopupRow row = _list.Items[index] as PopupRow;
            if (row == null || !row.IsPinned)
            {
                return -1;
            }
            return location.X >= _list.GetItemRectangle(index).Right - ScaleByDpi(BasePinIconWidth) ? index : -1;
        }

        private bool TryClickPinIcon(Point location)
        {
            int index = PinIconRowAt(location);
            if (index < 0)
            {
                return false;
            }
            _pinClickedAt = DateTime.UtcNow;
            Raise(PinIconClicked, index);
            return true;
        }

        private void OnListMouseMove(object sender, MouseEventArgs e)
        {
            if (_list.IsDragging)
            {
                return;
            }
            // 📌 の上では指の形にして、押せることを示す
            _list.Cursor = PinIconRowAt(e.Location) >= 0 ? Cursors.Hand : Cursors.Default;
        }

        private void OnListMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || _list.IsDragging)
            {
                return;
            }
            int index = RowIndexAt(e.Location);
            if (index < 0)
            {
                return;
            }
            // どの行のメニューか分かるよう、メニューを出す前に選択状態にする
            _list.SelectedIndex = index;
            Action<int, Point> handler = RowContextRequested;
            if (handler != null)
            {
                handler(index, _list.PointToScreen(e.Location));
            }
        }

        // ---- ドラッグ＆ドロップ ----------------------------------------------------------

        /// <summary>ドラッグを始められる行 (空きの枠と案内の行は動かせない)。</summary>
        private int DraggableRowAt(Point location)
        {
            int index = RowIndexAt(location);
            if (index < 0)
            {
                return -1;
            }
            PopupRow row = _list.Items[index] as PopupRow;
            return (row != null && row.Kind != PopupRowKind.Empty) ? index : -1;
        }

        private void OnDragBegan(int index)
        {
            _dragSource = index;
            _hover = DropTarget.Nowhere;
            _dropTarget = DropTarget.Nowhere;
            Raise(RowDragStarted, index);
            _list.Invalidate();
        }

        private void OnDragMoved(Point screen)
        {
            // 小窓 (並べて出しているときは 2 つの欄の全体) の外に出たら、他のアプリへのドラッグに引き継ぐ
            Control popup = TopLevelControl;
            if (popup != null && !popup.Bounds.Contains(screen) && TryDragOut())
            {
                return;
            }
            DropTarget hover = HitTest(screen);
            DropTarget drop = CanDrop(hover) ? hover : DropTarget.Nowhere;

            // 同じところで止めている間は、止め始めからの時間を数え直さない
            if (!hover.Equals(_hover))
            {
                _hover = hover;
                _springTimer.Stop();
                if (hover.Kind == DropKind.Into || hover.Kind == DropKind.Level)
                {
                    _springTimer.Start();
                }
            }
            if (!drop.Equals(_dropTarget))
            {
                _dropTarget = drop;
                _list.Invalidate();
                _title.HighlightIndex = drop.Kind == DropKind.Level ? drop.Index : -1;
            }
            // 落とせない先では「禁止」の形にする
            Cursor.Current = (hover.Kind != DropKind.None && drop.Kind == DropKind.None) ? Cursors.No : Cursors.Default;
        }

        /// <summary>
        /// 小窓の外に出たら、小窓の中のドラッグを終えて、他のアプリへのドラッグ (OLE) に引き継ぐ。
        /// 渡すデータが無ければ (グループなど) 何もせず false。
        /// </summary>
        private bool TryDragOut()
        {
            IDataObject data = OutsideDragData == null ? null : OutsideDragData();
            if (data == null)
            {
                return false;
            }
            // 左ボタンは押したままなので、DoDragDrop がそのままドラッグを続ける
            _list.CancelDrag();
            EndDrag(DropTarget.Nowhere);
            s_draggingOut = true;
            try
            {
                _list.DoDragDrop(data, DragDropEffects.Copy);
            }
            finally
            {
                s_draggingOut = false;
            }
            return true;
        }

        private void OnDragDropped(Point screen)
        {
            DropTarget target = HitTest(screen);
            EndDrag(CanDrop(target) ? target : DropTarget.Nowhere);
        }

        private void EndDrag(DropTarget target)
        {
            _springTimer.Stop();
            _dragSource = -1;
            _hover = DropTarget.Nowhere;
            _dropTarget = DropTarget.Nowhere;
            _title.HighlightIndex = -1;
            Cursor.Current = Cursors.Default;
            // 押したときに選ばれた行の色を残さない
            _list.SelectedIndex = -1;
            _list.Invalidate();
            Raise(RowDragEnded, target);
        }

        private void OnSpringTimerTick(object sender, EventArgs e)
        {
            _springTimer.Stop();
            if (!_list.IsDragging)
            {
                return;
            }
            Raise(DragOpenRequested, _hover);
        }

        private bool CanDrop(DropTarget target)
        {
            if (target.Kind == DropKind.None)
            {
                return false;
            }
            Func<DropTarget, bool> validator = DropValidator;
            return validator == null || validator(target);
        }

        /// <summary>
        /// 画面上のその位置に落とすと、どこに落ちるか。
        /// グループの行は、中央 (上下 1/4 を除く) ならグループに入れ、上下の端なら入れ替え。
        /// </summary>
        private DropTarget HitTest(Point screen)
        {
            Point inList = _list.PointToClient(screen);
            if (_list.ClientRectangle.Contains(inList))
            {
                int index = RowIndexAt(inList);
                if (index < 0)
                {
                    return DropTarget.Nowhere;
                }
                PopupRow row = _list.Items[index] as PopupRow;
                if (row != null && row.Kind == PopupRowKind.Group)
                {
                    Rectangle bounds = _list.GetItemRectangle(index);
                    int edge = bounds.Height / 4;
                    if (inList.Y >= bounds.Top + edge && inList.Y < bounds.Bottom - edge)
                    {
                        return new DropTarget(DropKind.Into, index);
                    }
                }
                return new DropTarget(DropKind.Swap, index);
            }

            Point inTitle = _title.PointToClient(screen);
            if (_title.ClientRectangle.Contains(inTitle))
            {
                int segment = _title.SegmentAt(inTitle);
                if (segment >= 0)
                {
                    return new DropTarget(DropKind.Level, segment);
                }
            }
            return DropTarget.Nowhere;
        }

        /// <summary>
        /// その位置にある行 (0 始まり)。行が無い・案内の行なら -1。
        /// 最後の項目より下の空いたところは、最後の項目とみなさない。
        /// </summary>
        private int RowIndexAt(Point location)
        {
            if (_empty)
            {
                return -1;
            }
            int index = _list.IndexFromPoint(location);
            if (index < 0 || index >= _list.Items.Count || !_list.GetItemRectangle(index).Contains(location))
            {
                return -1;
            }
            return index;
        }

        private void Activate(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            int index = RowIndexAt(e.Location);
            if (index < 0)
            {
                return;
            }

            PopupRow row = _list.Items[index] as PopupRow;
            if (row == null || row.Kind == PopupRowKind.Empty)
            {
                return;
            }
            Raise(RowActivated, index);
        }

        private void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _list.Items.Count)
            {
                return;
            }

            e.DrawBackground();

            // NoPrefix: 履歴の & をアクセスキーの印として消さない
            const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                                          TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;

            PopupRow row = _list.Items[e.Index] as PopupRow;
            if (row == null)
            {
                // 行が無いときの案内
                TextRenderer.DrawText(e.Graphics, _list.Items[e.Index].ToString(), e.Font, e.Bounds, SystemColors.GrayText, flags);
                return;
            }

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool dragging = _list.IsDragging;
            if (dragging)
            {
                // ドラッグ中は選択の色を出さず、落とす先だけを示す
                selected = false;
                using (SolidBrush background = new SolidBrush(_list.BackColor))
                {
                    e.Graphics.FillRectangle(background, e.Bounds);
                }
                if (_dropTarget.Kind == DropKind.Swap && _dropTarget.Index == e.Index)
                {
                    // 入れ替える (移す) 先: 行を薄い青で塗る
                    using (SolidBrush brush = new SolidBrush(DropSwapColor))
                    {
                        e.Graphics.FillRectangle(brush, e.Bounds);
                    }
                }
                else if (_dropTarget.Kind == DropKind.Into && _dropTarget.Index == e.Index)
                {
                    // グループに入れる: 行を枠で囲む
                    using (Pen pen = new Pen(SystemColors.Highlight, 2))
                    {
                        Rectangle box = e.Bounds;
                        box.Inflate(-1, -1);
                        e.Graphics.DrawRectangle(pen, box);
                    }
                }
            }
            else if (e.Index == _flashIndex && _flashStep % 2 == 1)
            {
                // 点滅の点いている段階: 選んだときと同じ色で出す
                selected = true;
                e.Graphics.FillRectangle(SystemBrushes.Highlight, e.Bounds);
            }
            else if (selected && _dimmed)
            {
                // 数字キーが効かない欄 (並べて出しているときのもう一方) で選んでいる行は、青ではなく薄い灰色で出す。
                // 選んでいる行は覚えておき、← → や Tab でこの欄に戻ったときに青に戻る
                selected = false;
                using (SolidBrush brush = new SolidBrush(DimSelectedColor))
                {
                    e.Graphics.FillRectangle(brush, e.Bounds);
                }
            }
            Color color = selected ? SystemColors.HighlightText : _list.ForeColor;
            // 空きの枠と、ドラッグしている項目の元の行は灰色で出す
            Color textColor = ((row.Kind == PopupRowKind.Empty || (dragging && e.Index == _dragSource)) && !selected)
                ? SystemColors.GrayText : color;

            // 先頭 10 件には、選ぶための数字キー (1〜9、0) を付ける。11 件目以降は番号の欄を空けて、
            // 本文の書き出しの位置をそろえる
            int numberWidth = TextRenderer.MeasureText(e.Graphics, "0. ", e.Font, Size.Empty, flags).Width;
            // ピン止めの行は a〜z。普通の履歴はピン止めの後から 1 を付ける
            string number = row.IsPinned ? ItemNumber.PinLabel(e.Index) : ItemNumber.Label(e.Index - _pinnedCount);
            if (number != null)
            {
                Rectangle numberBounds = new Rectangle(e.Bounds.Left, e.Bounds.Top, numberWidth, e.Bounds.Height);
                // 数字キーが効かない欄 (並べて出しているときのもう一方) では、番号を灰色にする
                Color numberColor = (_dimmed && !selected) ? SystemColors.GrayText : color;
                TextRenderer.DrawText(e.Graphics, number + ".", e.Font, numberBounds, numberColor, flags);
            }

            int pinWidth = row.IsPinned ? ScaleByDpi(BasePinIconWidth) : 0;
            Rectangle textBounds = Rectangle.FromLTRB(e.Bounds.Left + numberWidth, e.Bounds.Top, e.Bounds.Right - pinWidth, e.Bounds.Bottom);
            if (row.IsPinned)
            {
                // 右端に 📌 (クリックでピン止めを外す)
                Rectangle pinBounds = Rectangle.FromLTRB(e.Bounds.Right - pinWidth, e.Bounds.Top, e.Bounds.Right, e.Bounds.Bottom);
                TextRenderer.DrawText(e.Graphics, PopupRow.PinMark, e.Font, pinBounds, selected ? color : SystemColors.GrayText,
                                      flags | TextFormatFlags.HorizontalCenter);
                if (e.Index == _pinnedCount - 1 && _pinnedCount < _list.Items.Count)
                {
                    // ピン止めと普通の履歴の区切り線
                    using (Pen pen = new Pen(SystemColors.ControlDark))
                    {
                        e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                    }
                }
            }
            string line;
            switch (row.Kind)
            {
                case PopupRowKind.Empty:
                    line = string.Empty;
                    break;
                case PopupRowKind.Group:
                    line = PopupRow.GroupLabel(PreviewText.Line(row.Text, PreviewText.LineMaxChars));
                    break;
                default:
                    line = (row.Mark ?? string.Empty) + PreviewText.Line(row.DisplayText, PreviewText.LineMaxChars);
                    break;
            }
            TextRenderer.DrawText(e.Graphics, line, e.Font, textBounds, textColor, flags);
        }

        /// <summary>今表示している一覧の index 番目 (0 始まり) が入力できる項目なら、その全文。そうでなければ null。</summary>
        public string ItemAt(int index)
        {
            if (_empty || index < 0 || index >= _list.Items.Count)
            {
                return null;
            }
            PopupRow row = _list.Items[index] as PopupRow;
            return (row != null && row.Kind == PopupRowKind.Item) ? row.Text : null;
        }

        /// <summary>一覧の index 番目を選んだ状態 (強調表示) にする。</summary>
        public void SelectItem(int index)
        {
            if (!_empty && index >= 0 && index < _list.Items.Count)
            {
                _list.SelectedIndex = index;
            }
        }

        /// <summary>
        /// 入力した行の選択を外し、選択の色で短く 1 回点滅させて消す。選んだ色のまま残すと、止まっているように見えるため。
        /// 続けて ↑↓ を押したときは、この行から動かす。
        /// </summary>
        public void FlashItem(int index)
        {
            if (_empty || index < 0 || index >= _list.Items.Count)
            {
                return;
            }
            StopFlash();
            _list.SelectedIndex = -1;
            _anchorIndex = index;
            _flashIndex = index;
            _flashStep = 0;
            _list.Invalidate(_list.GetItemRectangle(index));
            _flashTimer.Start();
        }

        private void OnFlashTimerTick(object sender, EventArgs e)
        {
            _flashStep++;
            if (_flashStep >= FlashSteps)
            {
                StopFlash();
                return;
            }
            _list.Invalidate(_list.GetItemRectangle(_flashIndex));
        }

        private void StopFlash()
        {
            _flashTimer.Stop();
            if (_flashIndex >= 0 && _flashIndex < _list.Items.Count)
            {
                _list.Invalidate(_list.GetItemRectangle(_flashIndex));
            }
            _flashIndex = -1;
        }

        /// <summary>今選んでいる (強調表示している) 行の位置。無ければ -1。</summary>
        public int SelectedIndex
        {
            get { return _empty ? -1 : _list.SelectedIndex; }
        }

        /// <summary>
        /// ↑↓ で空きの枠も選べるか (定型文の欄。空きの枠で Enter を押すと登録画面を出す)。
        /// false なら空きの枠は飛ばす (履歴の欄)。
        /// </summary>
        public bool SelectEmptyRows { get; set; }

        /// <summary>
        /// 選択を step (上なら -1、下なら +1) 行動かす。空きの枠は飛ばし (SelectEmptyRows なら飛ばさない)、端まで来たら反対側へ回る。
        /// 何も選んでいないときは、直前に入力した行から動かす。それも無ければ、下なら最初の行、上なら最後の行を選ぶ。
        /// </summary>
        public void MoveSelection(int step)
        {
            int count = _list.Items.Count;
            if (_empty || count == 0)
            {
                return;
            }
            int start = _list.SelectedIndex;
            if (start < 0)
            {
                start = _anchorIndex >= 0 ? _anchorIndex : (step > 0 ? -1 : count);
            }
            for (int n = 1; n <= count; n++)
            {
                int i = ((start + step * n) % count + count) % count;
                PopupRow row = _list.Items[i] as PopupRow;
                if (row != null && (row.Kind != PopupRowKind.Empty || SelectEmptyRows))
                {
                    _list.SelectedIndex = i;
                    return;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _title != null && _title.Font != null)
            {
                // 自分で作った太字のフォントを片づける
                _title.Font.Dispose();
            }
            if (disposing && _springTimer != null)
            {
                _springTimer.Dispose();
            }
            if (disposing && _flashTimer != null)
            {
                _flashTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private static void Raise<T>(Action<T> handler, T value)
        {
            if (handler != null)
            {
                handler(value);
            }
        }

        private int ScaleByDpi(int value)
        {
            return (int)Math.Round(value * DeviceDpi / 96.0);
        }
    }
}
