using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Copipe.Interop;
using Copipe.Services;

namespace Copipe.UI
{
    /// <summary>
    /// ホットキーで出す小窓。中に一覧の欄 (PopupPane) を置く。押したら出したままの設定では、上に検索窓も置く。
    /// 左に履歴の欄 (Primary)、右に定型文の欄 (Secondary) を並べて出す。
    /// 入力中のアプリからフォーカスを奪わず (検索窓を出すときだけは奪う)、タスクバーや Alt+Tab にも出ない。
    /// 表示のたびに作り直さず、表示・非表示を切り替えて使う。
    /// </summary>
    internal sealed class PopupForm : Form
    {
        // 96 DPI (拡大率 100%) のときの大きさ。実際の DPI に合わせて拡大する
        private const int BaseWidth = 420;
        private const int BasePadding = 8;
        private const int BaseCursorOffset = 16;

        private static readonly Color PopupBackColor = Color.FromArgb(0xF9, 0xF9, 0xF9);

        // 左の欄 (履歴)・右の欄 (定型文) と、その間のすき間
        private readonly PopupPane _primary;
        private readonly PopupPane _secondary;
        private readonly Panel _gap;
        private PopupPane _active;
        private Point _cursor;

        public PopupForm()
        {
            // 画面には出ないが、ウインドウの名前として残る (検証ハーネスが小窓を見つける目印)
            Text = "Copipe";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = PopupBackColor;
            Padding = new Padding(ScaleByDpi(BasePadding));

            _secondary = CreatePane();
            _secondary.Dock = DockStyle.Fill;
            Controls.Add(_secondary);
            _gap = new Panel();
            _gap.Dock = DockStyle.Left;
            _gap.Width = ScaleByDpi(BasePadding) * 2;
            _gap.BackColor = BackColor;
            Controls.Add(_gap);
            _primary = CreatePane();
            _primary.Dock = DockStyle.Left;
            _primary.Width = ScaleByDpi(BaseWidth);
            Controls.Add(_primary);
            ArrangeDock();
            // 履歴の項目は、右の定型文の欄の空きの枠にも落とせる (定型文として登録する)
            _primary.DropPeer = _secondary;
            _active = _primary;

            UpdateMarks();
            UpdateSize();
        }

        private PopupPane CreatePane()
        {
            PopupPane pane = new PopupPane(BackColor);
            pane.InsertOnSingleClick = _insertOnSingleClick;
            pane.DropValidator = _dropValidator;
            pane.OutsideDragData = _outsideDragData;
            pane.Engaged += OnPaneEngaged;
            pane.ContentChanged += delegate { UpdateSize(); };
            KeepSearchFocus(pane);
            // 数字キーが効く欄 (_active) の操作だけを知らせる。もう一方の欄は、押した時点で Engaged から _active になる
            pane.RowActivated += delegate(int index) { if (pane == _active) { Raise(RowActivated, index); } };
            pane.RowContextRequested += delegate(int index, Point screen)
            {
                Action<int, Point> handler = RowContextRequested;
                if (pane == _active && handler != null)
                {
                    handler(index, screen);
                }
            };
            pane.EmptyRowClicked += delegate(int index) { if (pane == _active) { Raise(EmptyRowClicked, index); } };
            pane.RowDragStarted += delegate(int index) { if (pane == _active) { Raise(RowDragStarted, index); } };
            pane.DragOpenRequested += delegate(DropTarget target) { if (pane == _active) { Raise(DragOpenRequested, target); } };
            pane.LevelClicked += delegate(int level) { if (pane == _active) { Raise(LevelClicked, level); } };
            pane.PinIconClicked += delegate(int index) { if (pane == _active) { Raise(PinIconClicked, index); } };
            pane.RowDragEnded += delegate(DropTarget target) { if (pane == _active) { Raise(RowDragEnded, target); } };
            pane.ExternalDropped += delegate(int index, string path, string text)
            {
                Action<int, string, string> handler = ExternalDropped;
                if (pane == _active && handler != null)
                {
                    handler(index, path, text);
                }
            };
            foreach (Control grip in pane.Grips)
            {
                grip.MouseDown += OnGripMouseDown;
                grip.MouseMove += OnGripMouseMove;
                grip.MouseUp += delegate { _moving = false; };
            }
            return pane;
        }

        private void OnPaneEngaged(object sender, EventArgs e)
        {
            PopupPane pane = (PopupPane)sender;
            Action<PopupPane> handler = PaneEngaged;
            if (pane != _active && handler != null)
            {
                handler(pane);
            }
        }

        /// <summary>
        /// 数字キーが効いていない欄 (並べて出しているときのもう一方) が押された・ドラッグされてきたとき。
        /// 受け取る側が ActivePane をその欄にすると、続く行の操作 (クリック・ドラッグ・右クリック) が届く。
        /// </summary>
        public event Action<PopupPane> PaneEngaged;

        /// <summary>左の欄 (履歴)。</summary>
        public PopupPane Primary
        {
            get { return _primary; }
        }

        /// <summary>右の欄 (定型文)。</summary>
        public PopupPane Secondary
        {
            get { return _secondary; }
        }

        /// <summary>
        /// Dock は並びの後ろのものから順に場所を取る。検索窓 (上)、左の欄、すき間、右の欄 (Fill) の順に取らせる。
        /// </summary>
        private void ArrangeDock()
        {
            _gap.SendToBack();
            _primary.SendToBack();
            if (_searchPanel != null)
            {
                _searchPanel.SendToBack();
            }
        }

        // ---- 検索窓 (押したら出したままの設定のとき) ------------------------------------

        /// <summary>候補を一度に見せる行数の上限。これを超える分はスクロールで見る。</summary>
        private const int MaxVisibleCandidates = 8;

        // 検索窓と候補の一覧 (検索窓を出さないときは null)
        private Panel _searchPanel;
        // 検索窓を入れる行
        private Panel _searchRow;
        private TextBox _search;
        private ListBox _candidates;

        /// <summary>
        /// 小窓の上に検索窓を出すか。
        /// 矢印・Enter・Esc はホットキー (PopupKeys) で受け取るので、検索窓には届かない。
        /// </summary>
        public bool SearchEnabled
        {
            get { return _searchPanel != null; }
            set
            {
                if (value == SearchEnabled)
                {
                    return;
                }
                if (value)
                {
                    CreateSearch();
                }
                else
                {
                    Controls.Remove(_searchPanel);
                    StopSearchHook();
                    _searchPanel.Dispose();
                    _searchPanel = null;
                    _searchRow = null;
                    _search = null;
                    _candidates = null;
                }
                UpdateSize();
            }
        }

        /// <summary>
        /// 検索窓を出すとき、小窓を前面にして検索窓に入力カーソルを置くか (入力中のアプリからフォーカスを取る)。
        /// false なら前面にせず、英字・数字・記号・空白・Backspace をフックで受け取って検索窓に入れる
        /// (押している間だけ出す設定。入力中のアプリに貼り付けるため、フォーカスはそのままにする。日本語は打てない)。
        /// </summary>
        public bool SearchTakesFocus
        {
            get { return _searchTakesFocus; }
            set { _searchTakesFocus = value; }
        }

        private bool _searchTakesFocus = true;

        /// <summary>検索窓のフックで受け取らないキー (ホットキー・モードキー。英字や空白のこともあるため)。</summary>
        public Keys[] SearchIgnoredKeys { get; set; }

        private void CreateSearch()
        {
            _searchPanel = new Panel();
            _searchPanel.Dock = DockStyle.Top;
            _searchPanel.BackColor = BackColor;
            _searchPanel.Padding = new Padding(0, 0, 0, ScaleByDpi(BasePadding));

            // 中も Dock は後ろのものから場所を取る。候補を先に、検索窓を後に追加して、検索窓を上にする
            _candidates = new ListBox();
            _candidates.Dock = DockStyle.Top;
            _candidates.Font = SystemFonts.MessageBoxFont;
            _candidates.IntegralHeight = false;
            _candidates.Visible = false;
            _candidates.MouseClick += OnCandidateClick;
            _candidates.MouseDoubleClick += OnCandidateDoubleClick;
            _candidates.MouseUp += OnCandidateMouseUp;
            _searchPanel.Controls.Add(_candidates);

            _search = new TextBox();
            _search.Dock = DockStyle.Fill;
            _search.Font = SystemFonts.MessageBoxFont;
            _search.TextChanged += OnSearchTextChanged;
            _searchRow = new Panel();
            _searchRow.Dock = DockStyle.Top;
            _searchRow.Height = _search.PreferredHeight;
            _searchRow.Controls.Add(_search);
            _searchPanel.Controls.Add(_searchRow);

            KeepSearchFocus(_candidates);
            Controls.Add(_searchPanel);
            ArrangeDock();
            UpdateSearchHeight();
        }

        /// <summary>検索窓の文字が変わったとき。</summary>
        public event EventHandler SearchChanged;

        /// <summary>候補の行がクリック (ダブルクリック) されたとき。引数は候補の位置。</summary>
        public event Action<int> CandidateActivated;

        /// <summary>検索窓の文字 (検索窓を出していなければ空)。</summary>
        public string SearchText
        {
            get { return _search == null ? string.Empty : _search.Text; }
        }

        /// <summary>候補を出しているか。</summary>
        public bool HasCandidates
        {
            get { return _candidates != null && _candidates.Items.Count > 0; }
        }

        /// <summary>選んでいる候補の位置 (候補が無ければ -1)。</summary>
        public int CandidateIndex
        {
            get { return HasCandidates ? _candidates.SelectedIndex : -1; }
        }

        /// <summary>候補の一覧を入れる。空なら一覧を隠す。最初の候補を選んでおく (Enter でそれを使う)。</summary>
        public void SetCandidates(IList<string> labels)
        {
            if (_candidates == null)
            {
                return;
            }
            _candidates.BeginUpdate();
            try
            {
                _candidates.Items.Clear();
                foreach (string label in labels)
                {
                    _candidates.Items.Add(label);
                }
            }
            finally
            {
                _candidates.EndUpdate();
            }
            _candidates.Visible = labels.Count > 0;
            if (labels.Count > 0)
            {
                _candidates.SelectedIndex = 0;
            }
            // 上下の枠線の分も足す。足さないと最後の行が切れる (ウインドウを作る前は大きさの差では測れない)
            int border = SystemInformation.Border3DSize.Height * 2;
            _candidates.Height = Math.Max(1, Math.Min(labels.Count, MaxVisibleCandidates)) * _candidates.ItemHeight + border;
            UpdateSearchHeight();
        }

        /// <summary>選んでいる候補を step (上なら -1、下なら +1) 動かす。端まで来たら反対側へ回る。</summary>
        public void MoveCandidate(int step)
        {
            if (!HasCandidates)
            {
                return;
            }
            int count = _candidates.Items.Count;
            _candidates.SelectedIndex = ((_candidates.SelectedIndex + step) % count + count) % count;
        }

        /// <summary>
        /// 検索窓に文字を打つ (選んでいる部分があれば置き換える)。'\b' なら 1 文字消す。
        /// 押している間だけ出す設定では、入力カーソルが入力中のアプリにあるので、キーボードフックで受け取ってここで入れる。
        /// </summary>
        public void TypeSearch(char c)
        {
            if (_search == null || (c != '\b' && char.IsControl(c)))
            {
                return;
            }
            if (!_search.Focused)
            {
                FocusSearch();
            }
            if (c != '\b')
            {
                _search.SelectedText = c.ToString();
                return;
            }
            if (_search.SelectionLength == 0 && _search.SelectionStart > 0)
            {
                _search.Select(_search.SelectionStart - 1, 1);
            }
            _search.SelectedText = string.Empty;
        }

        /// <summary>検索窓を空にする (候補も消える)。</summary>
        public void ClearSearch()
        {
            if (_search != null)
            {
                _search.Text = string.Empty;
            }
        }

        private void UpdateSearchHeight()
        {
            // 小窓を隠している間は Visible が false を返すので、候補の有無は件数で見る
            int height = _searchRow.Height + _searchPanel.Padding.Bottom + (_candidates.Items.Count > 0 ? _candidates.Height : 0);
            if (_searchPanel.Height != height)
            {
                _searchPanel.Height = height;
            }
            UpdateSize();
        }

        /// <summary>フックが受け取った文字を検索窓に入れる。右クリックのメニューやドラッグの間は入れない。</summary>
        private void OnHookTyped(char c)
        {
            if (RowMenu.IsOpen || IsDragging || !Visible)
            {
                return;
            }
            TypeSearch(c);
        }

        /// <summary>
        /// 検索窓で IME が変換中なら、その文字を確定 (complete) か取り消し (cancel) して true を返す。
        /// 小窓を出している間は Enter・Esc をホットキーで受け取るので、IME に届かない。代わりにここで確定・取り消しする。
        /// </summary>
        public bool FinishImeComposition(bool complete)
        {
            if (_search == null || !_search.IsHandleCreated)
            {
                return false;
            }
            IntPtr context = NativeMethods.ImmGetContext(_search.Handle);
            if (context == IntPtr.Zero)
            {
                return false;
            }
            try
            {
                if (NativeMethods.ImmGetCompositionStringW(context, NativeMethods.GCS_COMPSTR, IntPtr.Zero, 0) <= 0)
                {
                    return false;
                }
                NativeMethods.ImmNotifyIME(context, NativeMethods.NI_COMPOSITIONSTR,
                                           complete ? NativeMethods.CPS_COMPLETE : NativeMethods.CPS_CANCEL, 0);
                return true;
            }
            finally
            {
                NativeMethods.ImmReleaseContext(_search.Handle, context);
            }
        }

        private void OnSearchTextChanged(object sender, EventArgs e)
        {
            EventHandler handler = SearchChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        /// <summary>候補の index 番目が、1 回のクリックで開くもの (グループ) か。受け取る側が決める。</summary>
        public Func<int, bool> CandidateOpensOnClick { get; set; }

        // 最後に候補を 1 回のクリックで使った時刻。直後のダブルクリックの 2 回目を使わないため
        private DateTime _candidateClickedAt = DateTime.MinValue;

        private int CandidateAt(MouseEventArgs e)
        {
            int index = _candidates.IndexFromPoint(e.Location);
            return (e.Button == MouseButtons.Left && index >= 0 && index < _candidates.Items.Count) ? index : -1;
        }

        private void OnCandidateClick(object sender, MouseEventArgs e)
        {
            int index = CandidateAt(e);
            Func<int, bool> opensOnClick = CandidateOpensOnClick;
            // グループは「貼り付けの操作」の設定によらず、1 回のクリックで開く
            if (index >= 0 && (InsertOnSingleClick || (opensOnClick != null && opensOnClick(index))))
            {
                _candidateClickedAt = DateTime.UtcNow;
                Raise(CandidateActivated, index);
            }
        }

        private void OnCandidateDoubleClick(object sender, MouseEventArgs e)
        {
            int index = CandidateAt(e);
            if (index >= 0 && !InsertOnSingleClick &&
                (DateTime.UtcNow - _candidateClickedAt).TotalMilliseconds > SystemInformation.DoubleClickTime)
            {
                Raise(CandidateActivated, index);
            }
        }

        /// <summary>候補を右クリックしたとき (一覧の行と同じく、編集・削除などのメニューを出すため)。</summary>
        public event Action<int, Point> CandidateContextRequested;

        private void OnCandidateMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }
            int index = _candidates.IndexFromPoint(e.Location);
            if (index < 0 || index >= _candidates.Items.Count)
            {
                return;
            }
            // どの候補のメニューか分かるよう、メニューを出す前に選択状態にする
            _candidates.SelectedIndex = index;
            Action<int, Point> handler = CandidateContextRequested;
            if (handler != null)
            {
                handler(index, _candidates.PointToScreen(e.Location));
            }
        }

        /// <summary>
        /// 一覧をクリックすると入力カーソルが一覧に移り、打った英字が検索窓に入らなくなる。
        /// 検索窓を出しているときは、マウスのボタンを離してから検索窓に戻す。
        /// 押している間に戻すと、一覧がマウスの捕捉を失い、クリック・ダブルクリックが届かなくなる
        /// (グループをクリックしても中に入らなかった)。
        /// </summary>
        private void KeepSearchFocus(Control control)
        {
            control.GotFocus += delegate
            {
                if (_search != null && control != _search)
                {
                    if (_refocusTimer == null)
                    {
                        _refocusTimer = new Timer();
                        _refocusTimer.Interval = RefocusPollMs;
                        _refocusTimer.Tick += OnRefocusTimerTick;
                    }
                    _refocusTimer.Start();
                }
            };
            foreach (Control child in control.Controls)
            {
                KeepSearchFocus(child);
            }
        }

        /// <summary>検索窓に入力カーソルを戻す前に、マウスのボタンが離されたかを調べる間隔。</summary>
        private const int RefocusPollMs = 30;

        private Timer _refocusTimer;

        private void OnRefocusTimerTick(object sender, EventArgs e)
        {
            if (Control.MouseButtons != MouseButtons.None)
            {
                return;
            }
            _refocusTimer.Stop();
            if (_search != null && Visible && !_search.Focused && _searchTakesFocus)
            {
                _search.Focus();
            }
        }

        /// <summary>検索窓を出していて SearchTakesFocus なら、小窓を前面にして検索窓に入力カーソルを置く (右クリックのメニューを閉じた後など)。</summary>
        public void FocusSearch()
        {
            if (SearchEnabled && _searchTakesFocus && Visible)
            {
                ActivateForSearch();
            }
        }

        /// <summary>
        /// 小窓を前面にして、検索窓に入力カーソルを置く。Windows は、最後に入力を受けたプロセスでないと
        /// 前面を変えさせない (CapsLock のホットキーはフックで受けるので、Copipe は入力を受けたことにならない)。
        /// そのため、前面のアプリのスレッドと入力の状態をつないでから前面にする。
        /// </summary>
        private void ActivateForSearch()
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground != Handle)
            {
                uint me = NativeMethods.GetCurrentThreadId();
                uint other = foreground == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, IntPtr.Zero);
                bool attached = other != 0 && other != me && NativeMethods.AttachThreadInput(me, other, true);
                try
                {
                    NativeMethods.SetForegroundWindow(Handle);
                }
                finally
                {
                    if (attached)
                    {
                        NativeMethods.AttachThreadInput(me, other, false);
                    }
                }
            }
            ActiveControl = _search;
            _search.Focus();
        }

        /// <summary>数字キー・矢印キーと、行の操作が効く欄。</summary>
        public PopupPane ActivePane
        {
            get { return _active; }
            set
            {
                if (value == null || (value != _primary && value != _secondary) || value == _active)
                {
                    return;
                }
                _active = value;
                UpdateMarks();
            }
        }

        /// <summary>数字キーが効く欄に印を付け、もう一方を灰色にする。</summary>
        private void UpdateMarks()
        {
            foreach (PopupPane pane in Panes)
            {
                pane.Accent = pane == _active;
                pane.Dimmed = pane != _active;
            }
        }

        private IList<PopupPane> Panes
        {
            get { return new[] { _primary, _secondary }; }
        }

        /// <summary>欄の行数に合わせて、小窓の大きさを決める。表示中なら、画面からはみ出さないよう位置も決め直す。</summary>
        private void UpdateSize()
        {
            int height = 0;
            foreach (PopupPane pane in Panes)
            {
                height = Math.Max(height, pane.ContentHeight);
            }
            if (_searchPanel != null)
            {
                height += _searchPanel.Height;
            }
            int width = ScaleByDpi(BaseWidth) * 2 + _gap.Width;
            Size = new Size(Padding.Left + Padding.Right + width, Padding.Top + Padding.Bottom + height);
            if (Visible)
            {
                Place();
            }
        }

        /// <summary>
        /// 他のアプリから空きの枠の行 (0 始まり) に、ファイル・フォルダー (path) か文字列 (text) を落とされたとき。
        /// path と text はどちらか一方だけが入る (PopupPane.ExternalDropped)。
        /// </summary>
        public event Action<int, string, string> ExternalDropped;

        /// <summary>
        /// 行を選ぶ操作 (クリックまたはダブルクリック) がされたとき。引数は行の位置 (0 始まり)。
        /// 空きの枠や案内の行では出さない。何をするか (入力する・グループに入る) は受け取る側が決める。
        /// </summary>
        public event Action<int> RowActivated;

        /// <summary>行が右クリックされたとき。引数は行の位置 (0 始まり) と、画面上のクリック位置。</summary>
        public event Action<int, Point> RowContextRequested;

        /// <summary>定型文の空きの枠の行がクリックされたとき (1 回のクリックで登録画面を出すため)。</summary>
        public event Action<int> EmptyRowClicked;

        private bool _insertOnSingleClick;

        /// <summary>true ならクリック 1 回で、false (既定) ならダブルクリックで RowActivated を出す。</summary>
        public bool InsertOnSingleClick
        {
            get { return _insertOnSingleClick; }
            set
            {
                _insertOnSingleClick = value;
                foreach (PopupPane pane in Panes)
                {
                    pane.InsertOnSingleClick = value;
                }
            }
        }

        /// <summary>
        /// 小窓と一覧のウインドウを先に作っておき、初回の表示を速くする。
        /// </summary>
        public void Prepare()
        {
            IntPtr form = Handle;
            GC.KeepAlive(form);
            foreach (PopupPane pane in Panes)
            {
                pane.Prepare();
            }
        }

        private Func<DropTarget, bool> _dropValidator;

        /// <summary>
        /// ドラッグした項目をその先に落とせるか (受け取る側が決める)。
        /// null なら、どこにでも落とせる。
        /// </summary>
        public Func<DropTarget, bool> DropValidator
        {
            get { return _dropValidator; }
            set
            {
                _dropValidator = value;
                foreach (PopupPane pane in Panes)
                {
                    pane.DropValidator = value;
                }
            }
        }

        /// <summary>行のドラッグを始めたとき。引数は行の位置。</summary>
        public event Action<int> RowDragStarted;

        private Func<IDataObject> _outsideDragData;

        /// <summary>
        /// ドラッグしている行を小窓の外へ出したとき、他のアプリに渡すデータ (受け取る側が作る)。
        /// null を返すと外へは出さず、小窓の中のドラッグを続ける。
        /// </summary>
        public Func<IDataObject> OutsideDragData
        {
            get { return _outsideDragData; }
            set
            {
                _outsideDragData = value;
                foreach (PopupPane pane in Panes)
                {
                    pane.OutsideDragData = value;
                }
            }
        }

        /// <summary>
        /// ドラッグしたまま、グループの行の中央か見出しの階層名で止めたとき。
        /// 受け取る側がそのグループ (階層) を開けば、そのままドラッグを続けられる。
        /// </summary>
        public event Action<DropTarget> DragOpenRequested;

        /// <summary>見出しの階層名がクリックされたとき。引数は階層 (0 が一番上)。今いる階層では出さない。</summary>
        public event Action<int> LevelClicked;

        /// <summary>ピン止めの行の 📌 がクリックされたとき。引数は行の位置。</summary>
        public event Action<int> PinIconClicked;

        /// <summary>
        /// ドラッグが終わったとき。引数は落とした先。落とせない先で離した・取り消した・小窓を消したときは
        /// Kind が None。
        /// </summary>
        public event Action<DropTarget> RowDragEnded;

        // ---- 数字キーが効く欄 (ActivePane) への操作 ---------------------------------------

        /// <summary>数字キーが効く欄の、一覧の上部にあるピン止めの行数。</summary>
        public int PinnedCount
        {
            get { return _active.PinnedCount; }
        }

        /// <summary>どちらかの欄でドラッグしているか。</summary>
        public bool IsDragging
        {
            get
            {
                foreach (PopupPane pane in Panes)
                {
                    if (pane.IsDragging)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>ドラッグを取り消す (Esc・ホットキーを離したとき)。RowDragEnded (None) を出す。</summary>
        public void CancelDrag()
        {
            foreach (PopupPane pane in Panes)
            {
                pane.CancelDrag();
            }
        }

        /// <summary>数字キーが効く欄の、一覧の index 番目が入力できる項目なら、その全文。そうでなければ null。</summary>
        public string ItemAt(int index)
        {
            return _active.ItemAt(index);
        }

        /// <summary>数字キーが効く欄の、一覧の index 番目を選んだ状態 (強調表示) にする。</summary>
        public void SelectItem(int index)
        {
            _active.SelectItem(index);
        }

        /// <summary>数字キーが効く欄の、入力した行を点滅させる (PopupPane.FlashItem)。</summary>
        public void FlashItem(int index)
        {
            _active.FlashItem(index);
        }

        /// <summary>数字キーが効く欄で、今選んでいる行の位置。無ければ -1。</summary>
        public int SelectedIndex
        {
            get { return _active.SelectedIndex; }
        }

        /// <summary>数字キーが効く欄で、選択を step 行動かす (PopupPane.MoveSelection)。</summary>
        public void MoveSelection(int step)
        {
            _active.MoveSelection(step);
        }

        // ---- 表示と位置 -------------------------------------------------------------------

        /// <summary>
        /// カーソルの近くに、カーソルがあるモニターからはみ出さないように表示する。
        /// 中身は先に欄 (Primary・Secondary) に入れておく。
        /// </summary>
        public void ShowAt(Point cursor)
        {
            _cursor = cursor;
            Place();
            if (!Visible)
            {
                ClearSearch();
                // ShowWithoutActivation が true なので、アクティブにせずに表示される
                Show();
                if (SearchEnabled && _searchTakesFocus)
                {
                    ActivateForSearch();
                }
            }
        }

        // 押している間だけ出す設定で、キーを受け取るフック (出していないときは null)
        private SearchKeyHook _searchHook;

        /// <summary>
        /// 押している間だけ出す設定 (前面にしない) で、どのアプリが前面でもキーを先に受け取り、文字は検索窓に入れる。
        /// 表示の直後に打ったキーが入力中のアプリに漏れないよう、受け取る側が小窓を出す前に呼ぶ。小窓を隠すと止まる。
        /// 前面にする設定では、検索窓に普通に打つので呼ばない。
        /// </summary>
        public void StartSearchHook()
        {
            StopSearchHook();
            if (SearchEnabled)
            {
                _searchHook = SearchKeyHook.Start(Handle, SearchIgnoredKeys);
            }
        }

        private void StopSearchHook()
        {
            if (_searchHook != null)
            {
                _searchHook.Dispose();
                _searchHook = null;
            }
        }

        private void Place()
        {
            Point location;
            if (_moved)
            {
                // 見出しをつかんで動かした後は、カーソルの近くへ置き直さない。行数が変わってはみ出す分だけ押し戻す
                Rectangle area = Screen.FromControl(this).WorkingArea;
                location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)),
                                     Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
            }
            else
            {
                Rectangle workingArea = Screen.FromPoint(_cursor).WorkingArea;
                location = PopupPlacement.Place(_cursor, Size, workingArea, ScaleByDpi(BaseCursorOffset));
            }

            // 位置を決めると同時に、後から出た最前面ウインドウより前に出し直す。フォーカスは奪わない
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, location.X, location.Y, 0, 0,
                                       NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        public void HidePopup()
        {
            foreach (PopupPane pane in Panes)
            {
                pane.Reset();
            }
            // 動かした位置はその回だけ。次に出すときはカーソルの近くに戻す
            _moving = false;
            _moved = false;
            StopSearchHook();
            Hide();
        }

        // 見出しをつかんで動かしている間は _moving。この回に一度でも動かしたら _moved
        private bool _moving;
        private bool _moved;
        private Point _moveStart;
        private Point _moveOrigin;

        private void OnGripMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || IsDragging)
            {
                return;
            }
            _moving = true;
            _moveStart = Cursor.Position;
            _moveOrigin = Location;
        }

        private void OnGripMouseMove(object sender, MouseEventArgs e)
        {
            Point cursor = Cursor.Position;
            if (!_moving || cursor == _moveStart)
            {
                return;
            }
            _moved = true;
            // Location の変更は SWP_NOACTIVATE で動かすので、入力中のアプリが前面のまま
            Location = new Point(_moveOrigin.X + cursor.X - _moveStart.X, _moveOrigin.Y + cursor.Y - _moveStart.Y);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // 最前面は TopMost プロパティではなくスタイルで指定する。
                // TopMost プロパティを使うと、表示時にアクティブになってしまうことがあるため
                cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // Windows 11 の他のウインドウに合わせて角を丸くする。失敗しても見た目だけの問題なので無視する
            int preference = NativeMethods.DWMWCP_ROUND;
            try
            {
                NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        protected override void WndProc(ref Message m)
        {
            // クリックされてもアクティブにならない (入力中のアプリのフォーカスを保つ)。
            // MA_NOACTIVATE なのでクリック自体は一覧に届く (将来のダブルクリック・ドラッグ用)。
            // 検索窓を出して前面にする設定のときは、クリックでアクティブにする (検索窓に打てるように)
            if (m.Msg == SearchKeyHook.TypedMessage)
            {
                OnHookTyped(SearchKeyHook.CharFor(m.WParam, m.LParam));
                return;
            }
            if (m.Msg == NativeMethods.WM_MOUSEACTIVATE && !(SearchEnabled && _searchTakesFocus))
            {
                m.Result = new IntPtr(NativeMethods.MA_NOACTIVATE);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _refocusTimer != null)
            {
                _refocusTimer.Dispose();
            }
            if (disposing)
            {
                StopSearchHook();
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
