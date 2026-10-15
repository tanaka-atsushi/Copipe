using System;
using System.Drawing;
using System.Windows.Forms;
using Copipe.Services;

namespace Copipe.UI
{
    /// <summary>
    /// 定型文の登録・編集 (表示名・本文・起動するパス) と、グループの作成・名前の変更 (名前) のダイアログ。
    /// 部品の Name (titleBox・textBox・pathBox・nameBox・okButton・cancelButton) は、どの部品かを示す目印。
    /// </summary>
    internal sealed class PhraseDialog : Form
    {
        private readonly TextBox _titleBox;
        private readonly TextBox _textBox;
        private readonly TextBox _pathBox;
        private readonly TextBox _nameBox;
        private readonly Button _ok;
        // 本文とパスは、どちらか一方だけを使う。入力カーソルを入れた側 (書いた側) に赤い ● と赤い枠を付けて「使う側」とし、
        // もう一方は ○ にして灰色にする (灰色の側も書ける。カーソルを入れるとそちらが使う側になる)。OK で保存するのは使う側だけ。
        // 開いた直後はどちらにも ● を付けず、灰色にもしない (片方が書けないように見えるため。ユーザーが決めた)
        private readonly Label _textLabel;
        private readonly Label _pathLabel;
        private readonly Label _textMark;
        private readonly Label _pathMark;
        private readonly Panel _textFrame;
        private readonly Panel _pathFrame;
        private readonly Font _markFont;
        private Side _side;
        // 表示名・グループ名の欄は、英字に固定 (半角/全角 で日本語も打てる)。名前は英字で探すため
        private readonly EnglishTyping _english;

        // 使う側の目印の色 (● と欄の枠)
        private static readonly Color ChosenColor = Color.FromArgb(0xE0, 0x20, 0x20);

        // 目印は記号だけ (「使う」などの文字は付けない。● と灰色で十分に分かるため。ユーザーが決めた)
        private const string UsedMark = "●";
        private const string UnusedMark = "○";

        /// <summary>使う側。None はまだ決めていない (開いた直後) で、どちらも灰色にしない。</summary>
        private enum Side { None, Text, Path }

        private PhraseDialog(string caption, string location, bool isGroup, string first, string second, string path)
        {
            Text = caption;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            Font = SystemFonts.MessageBoxFont;
            BackColor = SystemColors.Window;
            KeyPreview = true;

            int y = Scaled(14);
            y += AddLabel(Lang.T("場所: ", "Location: ") + location, y, true) + Scaled(10);

            if (isGroup)
            {
                y += AddLabel(Lang.T("グループ名", "Group name"), y, false) + Scaled(6);
                _nameBox = new TextBox();
                _nameBox.Name = "nameBox";
                _nameBox.Text = first ?? string.Empty;
                _nameBox.TextChanged += delegate { UpdateState(); };
                _english = new EnglishTyping(_nameBox);
                y = AddNameField(_nameBox, y) + Scaled(14);
            }
            else
            {
                y += AddLabel(Lang.T("表示名 (省略すると、本文の最初の行を一覧に出します)", "Display name (optional; the first line of the text if empty)"), y, false) + Scaled(6);
                _titleBox = new TextBox();
                _titleBox.Name = "titleBox";
                _titleBox.Text = first ?? string.Empty;
                _english = new EnglishTyping(_titleBox);
                y = AddNameField(_titleBox, y) + Scaled(10);

                _markFont = new Font(Font, FontStyle.Bold);
                _textMark = AddMark(y);
                _textLabel = AddHeading(Lang.T("本文 (入力する文字。改行もそのまま入ります)", "Text (inserted as is, including line breaks)"),
                                        _textMark, ref y);
                y += Scaled(6);
                _textBox = new IndentPasteTextBox();
                _textBox.Name = "textBox";
                _textBox.Multiline = true;
                _textBox.AcceptsReturn = true;
                _textBox.ScrollBars = ScrollBars.Vertical;
                _textBox.WordWrap = true;
                _textBox.MaxLength = ClipboardHistory.MaxTextLength;
                _textBox.Text = second ?? string.Empty;
                _textBox.TextChanged += delegate { OnFieldChanged(false); };
                _textBox.Enter += delegate { OnFieldEnter(false); };
                _textBox.Leave += delegate { OnFieldLeave(false); };
                _textFrame = AddFramed(_textBox, new Rectangle(Scaled(16), y, Scaled(428), Scaled(110)));
                y += Scaled(120);

                _pathMark = AddMark(y);
                _pathLabel = AddHeading(Lang.T("ファイル・フォルダー・URI (選んだとき、本文を入力する代わりにこれを開きます)",
                                               "File, folder or URI (opened instead of typing text)"), _pathMark, ref y);
                y += Scaled(6);
                _pathBox = new TextBox();
                _pathBox.Name = "pathBox";
                _pathBox.Text = path ?? string.Empty;
                _pathBox.TextChanged += delegate { OnFieldChanged(true); };
                _pathBox.Enter += delegate { OnFieldEnter(true); };
                _pathBox.Leave += delegate { OnFieldLeave(true); };
                _pathFrame = AddFramed(_pathBox, new Rectangle(Scaled(16), y, Scaled(248), Scaled(26)));

                // 前の版で両方を入れた定型文は、パスを使っていた (小窓で選ぶと開いた) ので、パスを使う側にする。
                // それ以外はどちらにも ● を付けずに始める (片方だけに中身があれば、OK でそちらを保存する)
                _side = _textBox.Text.Trim().Length > 0 && _pathBox.Text.Trim().Length > 0 ? Side.Path : Side.None;

                // ファイル・フォルダーを、ダイアログのどこに落としてもパス欄に入る
                // (TextBox は AllowDrop が無いと落とせず、親にも伝わらないので、両方に付ける)
                AllowDrop = true;
                _pathBox.AllowDrop = true;
                DragEnter += OnPathDragEnter;
                DragDrop += OnPathDragDrop;
                _pathBox.DragEnter += OnPathDragEnter;
                _pathBox.DragDrop += OnPathDragDrop;

                Button fileButton = new Button();
                fileButton.Name = "browseFileButton";
                fileButton.Text = Lang.T("ファイル...", "File...");
                fileButton.Bounds = new Rectangle(Scaled(270), y - Scaled(1), Scaled(84), Scaled(28));
                fileButton.Click += delegate { BrowseFile(); };
                Controls.Add(fileButton);

                Button folderButton = new Button();
                folderButton.Name = "browseFolderButton";
                folderButton.Text = Lang.T("フォルダー...", "Folder...");
                folderButton.Bounds = new Rectangle(Scaled(360), y - Scaled(1), Scaled(84), Scaled(28));
                folderButton.Click += delegate { BrowseFolder(); };
                Controls.Add(folderButton);
                y += Scaled(42);

                // 灰色の側の見出し・● をクリックしても、そちらを使う側にできる (中身があるときだけ)
                foreach (Control control in new Control[] { _textMark, _textLabel })
                {
                    control.Click += delegate { Choose(false); };
                }
                foreach (Control control in new Control[] { _pathMark, _pathLabel })
                {
                    control.Click += delegate { Choose(true); };
                }

                y += AddLabel(Lang.T("本文の欄では Enter で改行、Ctrl+Enter で確定します。Esc で取り消します。",
                                     "In the text box, Enter adds a line break and Ctrl+Enter saves. Esc cancels."), y, true) + Scaled(10);
            }

            _ok = new Button();
            _ok.Name = "okButton";
            _ok.Text = "OK";
            _ok.DialogResult = DialogResult.OK;
            _ok.Bounds = new Rectangle(Scaled(268), y, Scaled(84), Scaled(30));
            Controls.Add(_ok);

            Button cancel = new Button();
            cancel.Name = "cancelButton";
            cancel.Text = Lang.T("キャンセル", "Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Bounds = new Rectangle(Scaled(360), y, Scaled(84), Scaled(30));
            Controls.Add(cancel);

            ClientSize = new Size(Scaled(460), y + Scaled(46));
            AcceptButton = _ok;
            CancelButton = cancel;

            // マウスカーソルのあるモニターの真ん中に出す (小窓を出していた画面)
            StartPosition = FormStartPosition.Manual;
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);

            UpdateState();
        }

        /// <summary>定型文の登録・編集のダイアログ。</summary>
        public static PhraseDialog ForPhrase(string caption, string location, string title, string text, string path)
        {
            return new PhraseDialog(caption, location, false, title, text, path);
        }

        /// <summary>グループの作成・名前の変更のダイアログ。</summary>
        public static PhraseDialog ForGroup(string caption, string location, string name)
        {
            return new PhraseDialog(caption, location, true, name, null, null);
        }

        /// <summary>起動するファイル・フォルダーのパス (前後の空白は除く。本文を使う側にしたときは空)。</summary>
        public string PhrasePath
        {
            get { return _pathBox == null || !PathChosen ? string.Empty : _pathBox.Text.Trim(); }
        }

        /// <summary>定型文の表示名 (前後の空白は除く)。</summary>
        public string PhraseTitle
        {
            get { return _titleBox == null ? string.Empty : _titleBox.Text.Trim(); }
        }

        /// <summary>定型文の本文 (入力したまま。前後の空白や改行も残す。パスを使う側にしたときは空)。</summary>
        public string PhraseText
        {
            get { return _textBox == null || PathChosen ? string.Empty : _textBox.Text; }
        }

        /// <summary>
        /// パスを使う側にしているか (本文の側なら false)。まだ決めていないときは、パスに中身があればパスを使う。
        /// </summary>
        public bool PathChosen
        {
            get { return _side == Side.Path || (_side == Side.None && _pathBox != null && _pathBox.Text.Trim().Length > 0); }
        }

        /// <summary>グループの名前 (前後の空白は除く)。</summary>
        public string GroupName
        {
            get { return _nameBox == null ? string.Empty : _nameBox.Text.Trim(); }
        }

        /// <summary>表示名 (定型文) かグループ名 (グループ) の欄。名前で探すときに使う名前なので、英字で打つ。</summary>
        private TextBox NameField
        {
            get { return _titleBox ?? _nameBox; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 名前は英字で探すので、名前の欄に入力カーソルがある間は、日本語入力や Meltype などがオンでも英字が入るようにする
            // (小窓の検索窓と同じ。ユーザーが決めた)。半角/全角 で日本語も打てる
            _english.Start(Handle, NameField.Handle);
            // 小窓はフォーカスを奪わない作りなので、ここで前面に出してすぐ入力できるようにする
            Activate();
            if (_textBox != null)
            {
                // 表示名の欄から始める (名前で探すときに使う名前を、先に決めてもらう)
                _titleBox.Focus();
                _titleBox.SelectionStart = _titleBox.TextLength;
            }
            else
            {
                _nameBox.Focus();
                _nameBox.SelectAll();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _english.Stop();
            base.OnFormClosed(e);
        }

        /// <summary>
        /// 名前の欄を置き、右に入力のしかたの印 (A・あ)、下に切り替え方の説明を置く。説明の下の y を返す。
        /// </summary>
        private int AddNameField(TextBox box, int y)
        {
            int indicatorWidth = Scaled(28);
            box.Bounds = new Rectangle(Scaled(16), y, Scaled(428) - indicatorWidth - Scaled(4), Scaled(26));
            Controls.Add(box);
            Label indicator = _english.Indicator;
            indicator.Bounds = new Rectangle(Scaled(444) - indicatorWidth, y, indicatorWidth, box.Height);
            Controls.Add(indicator);
            y += box.Height + Scaled(4);
            return y + AddLabel(Lang.T("英字で入ります。日本語は 半角/全角 キーか、右の A のクリックで切り替えます。",
                                       "Typed in English. Press Hankaku/Zenkaku or click A for Japanese."), y, true);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // 本文の欄では Enter が改行になるので、Ctrl+Enter で確定する
            if (keyData == (Keys.Control | Keys.Enter))
            {
                if (_ok.Enabled)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// 見出しの左の目印 (使う側は赤い ●、使わない側は灰色の ○) を置く。
        /// どちらでも切れないよう、大きい方の幅を取っておく。
        /// </summary>
        private Label AddMark(int y)
        {
            Label mark = new Label();
            mark.AutoSize = false;
            mark.UseMnemonic = false;
            mark.Font = _markFont;
            int width = 0;
            int height = 0;
            foreach (string text in new[] { UsedMark, UnusedMark })
            {
                Size size = TextRenderer.MeasureText(text, _markFont);
                width = Math.Max(width, size.Width);
                height = Math.Max(height, size.Height);
            }
            mark.Bounds = new Rectangle(Scaled(14), y, width + Scaled(4), height);
            Controls.Add(mark);
            return mark;
        }

        /// <summary>本文・パスの見出しを、目印の右に置く (欄の幅で折り返す)。</summary>
        private Label AddHeading(string heading, Label mark, ref int y)
        {
            Label label = new Label();
            label.Text = heading;
            label.AutoSize = false;
            label.UseMnemonic = false;
            int left = mark.Right + Scaled(4);
            int width = Scaled(444) - left;
            // Label は TextRenderer より少し狭い幅で折り返すことがあるので、幅を少し狭くして測る
            int height = Math.Max(mark.Height, TextRenderer.MeasureText(heading, Font, new Size(width - Scaled(8), 0),
                                                                        TextFormatFlags.WordBreak).Height);
            label.Bounds = new Rectangle(left, y, width, height);
            Controls.Add(label);
            y += height;
            return label;
        }

        /// <summary>
        /// 欄を枠 (Panel) の中に入れて置く。枠は使う側だけ赤くする。
        /// (欄の後ろに枠を重ねる形だと、DrawToBitmap の撮影で枠が欄の上に描かれ、欄が見えなくなった)
        /// </summary>
        private Panel AddFramed(TextBox box, Rectangle bounds)
        {
            int border = Scaled(2);
            Panel frame = new Panel();
            frame.BackColor = BackColor;
            Controls.Add(frame);
            frame.Controls.Add(box);
            box.Location = new Point(border, border);
            // 1 行の欄は、文字の大きさで高さが決まる。決まった高さに枠を合わせる
            box.Size = bounds.Size;
            frame.Bounds = new Rectangle(bounds.X - border, bounds.Y - border, box.Width + border * 2, box.Height + border * 2);
            return frame;
        }

        /// <summary>
        /// 欄の幅で折り返すラベルを置き、その高さを返す。長い説明や深い階層の場所でも切れないよう、
        /// 折り返した高さを測って決める (言語や Windows の文字の大きさで行数が変わる)。gray なら薄い色の説明にする。
        /// </summary>
        private int AddLabel(string text, int y, bool gray)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.UseMnemonic = false;
            if (gray)
            {
                label.ForeColor = SystemColors.GrayText;
            }
            int width = Scaled(428);
            int height = TextRenderer.MeasureText(text, Font, new Size(width, 0), TextFormatFlags.WordBreak).Height;
            label.Bounds = new Rectangle(Scaled(16), y, width, height);
            Controls.Add(label);
            return height;
        }

        private void UpdateState()
        {
            if (_textBox == null)
            {
                // 空白だけの名前では作らせない (一覧に何も出ない行ができてしまう)
                _ok.Enabled = _nameBox.Text.Trim().Length > 0;
                return;
            }

            ShowChoice(_textMark, _textLabel, _textFrame, _textBox, _side == Side.Text);
            ShowChoice(_pathMark, _pathLabel, _pathFrame, _pathBox, _side == Side.Path);
            // 空白だけの本文は入れていないものとみなす (一覧に何も出ない行ができてしまうので、登録させない)
            _ok.Enabled = (PathChosen ? _pathBox : _textBox).Text.Trim().Length > 0;
        }

        /// <summary>本文かパスの欄に入力カーソルが入ったとき。その側を使う側にする (空でも。書くつもりで入れたので)。</summary>
        private void OnFieldEnter(bool path)
        {
            _side = path ? Side.Path : Side.Text;
            UpdateState();
        }

        /// <summary>
        /// 本文かパスの欄から入力カーソルが出たとき。空のまま出たなら、使う側を戻す
        /// (もう一方に中身があればそちら、無ければ決めていない状態)。空の側では OK を押せないので。
        /// </summary>
        private void OnFieldLeave(bool path)
        {
            Release(path);
            UpdateState();
        }

        /// <summary>使う側が空なら、もう一方に中身があればそちらを使う側にし、無ければ決めていない状態に戻す。</summary>
        private void Release(bool path)
        {
            TextBox box = path ? _pathBox : _textBox;
            TextBox other = path ? _textBox : _pathBox;
            if (_side == (path ? Side.Path : Side.Text) && box.Text.Trim().Length == 0)
            {
                _side = other.Text.Trim().Length > 0 ? (path ? Side.Text : Side.Path) : Side.None;
            }
        }

        /// <summary>
        /// 本文かパスが書き換えられたとき。書いた側を使う側にする。
        /// 書いた側を空にしたときは、入力カーソルが残っている間はそのままにし (カーソルが出たときに戻す)、
        /// カーソルが無ければすぐに戻す (空の側を使うことはできないので)。
        /// </summary>
        private void OnFieldChanged(bool path)
        {
            TextBox box = path ? _pathBox : _textBox;
            if (box.Text.Trim().Length > 0)
            {
                _side = path ? Side.Path : Side.Text;
            }
            else if (!box.Focused)
            {
                Release(path);
            }
            UpdateState();
        }

        /// <summary>
        /// 見出しをクリックしたとき・パスを落としたり選んだりしたとき。中身があれば、その側を使う側にし、
        /// 入力カーソルもそちらに移す (● の側とカーソルの側を揃える)。
        /// </summary>
        private void Choose(bool path)
        {
            TextBox box = path ? _pathBox : _textBox;
            if (box.Text.Trim().Length > 0)
            {
                _side = path ? Side.Path : Side.Text;
                UpdateState();
                if (box.CanFocus)
                {
                    box.Focus();
                }
            }
        }

        /// <summary>
        /// 使う側は赤い ● と赤い枠を付ける。
        /// 使わない側は ○ にし、見出しと欄の文字・背景を灰色にする (書けるが、書くまで使わない)。
        /// まだ使う側を決めていないときは、どちらも ○ にして灰色にしない。
        /// </summary>
        private void ShowChoice(Label mark, Label label, Panel frame, TextBox box, bool chosen)
        {
            bool gray = !chosen && _side != Side.None;
            mark.Text = chosen ? UsedMark : UnusedMark;
            mark.ForeColor = chosen ? ChosenColor : gray ? SystemColors.GrayText : SystemColors.ControlText;
            label.ForeColor = gray ? SystemColors.GrayText : SystemColors.ControlText;
            frame.BackColor = chosen ? ChosenColor : BackColor;
            box.BackColor = gray ? SystemColors.Control : SystemColors.Window;
            box.ForeColor = gray ? SystemColors.GrayText : SystemColors.WindowText;
            // 中身のある灰色の側は、見出しをクリックするとそちらに切り替えられる
            Cursor cursor = (gray && box.Text.Trim().Length > 0) ? Cursors.Hand : Cursors.Default;
            mark.Cursor = cursor;
            label.Cursor = cursor;
        }

        private static string DroppedPath(DragEventArgs e)
        {
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            return paths != null && paths.Length > 0 ? paths[0] : null;
        }

        private void OnPathDragEnter(object sender, DragEventArgs e)
        {
            // 落とすとパスを使う側になる
            e.Effect = DroppedPath(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
        }

        /// <summary>複数を落としたときは最初の 1 つだけを入れる (1 つの定型文で開けるのは 1 つ)。</summary>
        private void OnPathDragDrop(object sender, DragEventArgs e)
        {
            string path = DroppedPath(e);
            if (path != null)
            {
                _pathBox.Text = path;
                // 同じパスを入れ直したときは TextChanged が来ないので、ここでも使う側にする
                Choose(true);
            }
        }

        private void BrowseFile()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.CheckFileExists = true;
                dialog.Title = Lang.T("起動するファイルを選ぶ", "Choose a file to open");
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _pathBox.Text = dialog.FileName;
                    Choose(true);
                }
            }
        }

        private void BrowseFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = Lang.T("起動するフォルダーを選ぶ", "Choose a folder to open");
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _pathBox.Text = dialog.SelectedPath;
                    Choose(true);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _english.Dispose();
                if (_markFont != null)
                {
                    _markFont.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private int Scaled(int value)
        {
            return (int)Math.Round(value * DeviceDpi / 96.0);
        }
    }
}
