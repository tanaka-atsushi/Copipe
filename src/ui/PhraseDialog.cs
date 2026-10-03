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
                _nameBox.Bounds = new Rectangle(Scaled(16), y, Scaled(428), Scaled(26));
                _nameBox.TextChanged += delegate { UpdateState(); };
                Controls.Add(_nameBox);
                y += Scaled(42);
            }
            else
            {
                y += AddLabel(Lang.T("表示名 (省略すると、本文の最初の行を一覧に出します)", "Display name (optional; the first line of the text if empty)"), y, false) + Scaled(6);
                _titleBox = new TextBox();
                _titleBox.Name = "titleBox";
                _titleBox.Text = first ?? string.Empty;
                _titleBox.Bounds = new Rectangle(Scaled(16), y, Scaled(428), Scaled(26));
                Controls.Add(_titleBox);
                y += Scaled(38);

                y += AddLabel(Lang.T("本文 (入力する文字。改行もそのまま入ります)", "Text (inserted as is, including line breaks)"), y, false) + Scaled(6);
                _textBox = new TextBox();
                _textBox.Name = "textBox";
                _textBox.Multiline = true;
                _textBox.AcceptsReturn = true;
                _textBox.ScrollBars = ScrollBars.Vertical;
                _textBox.WordWrap = true;
                _textBox.MaxLength = ClipboardHistory.MaxTextLength;
                _textBox.Text = second ?? string.Empty;
                _textBox.Bounds = new Rectangle(Scaled(16), y, Scaled(428), Scaled(110));
                _textBox.TextChanged += delegate { UpdateState(); };
                Controls.Add(_textBox);
                y += Scaled(116);

                y += AddLabel(Lang.T("ファイル・フォルダー・URI (省略可。指定すると、選んだとき本文の代わりにこれを開きます)",
                                     "File, folder or URI (optional; opened instead of typing the text)"), y, false) + Scaled(6);
                _pathBox = new TextBox();
                _pathBox.Name = "pathBox";
                _pathBox.Text = path ?? string.Empty;
                _pathBox.Bounds = new Rectangle(Scaled(16), y, Scaled(248), Scaled(26));
                _pathBox.TextChanged += delegate { UpdateState(); };
                Controls.Add(_pathBox);

                // ファイル・フォルダーを、ダイアログのどこに落としてもパス欄に入る
                // (TextBox は AllowDrop が無いと落とせず、親にも伝わらないので、両方に付ける)
                AllowDrop = true;
                _pathBox.AllowDrop = true;
                DragEnter += OnPathDragEnter;
                DragDrop += OnPathDragDrop;
                _pathBox.DragEnter += OnPathDragEnter;
                _pathBox.DragDrop += OnPathDragDrop;

                Button file = new Button();
                file.Name = "browseFileButton";
                file.Text = Lang.T("ファイル...", "File...");
                file.Bounds = new Rectangle(Scaled(270), y - Scaled(1), Scaled(84), Scaled(28));
                file.Click += delegate { BrowseFile(); };
                Controls.Add(file);

                Button folder = new Button();
                folder.Name = "browseFolderButton";
                folder.Text = Lang.T("フォルダー...", "Folder...");
                folder.Bounds = new Rectangle(Scaled(360), y - Scaled(1), Scaled(84), Scaled(28));
                folder.Click += delegate { BrowseFolder(); };
                Controls.Add(folder);
                y += Scaled(40);

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

        /// <summary>起動するファイル・フォルダーのパス (前後の空白は除く。無ければ空)。</summary>
        public string PhrasePath
        {
            get { return _pathBox == null ? string.Empty : _pathBox.Text.Trim(); }
        }

        /// <summary>定型文の表示名 (前後の空白は除く)。</summary>
        public string PhraseTitle
        {
            get { return _titleBox == null ? string.Empty : _titleBox.Text.Trim(); }
        }

        /// <summary>定型文の本文 (入力したまま。前後の空白や改行も残す)。</summary>
        public string PhraseText
        {
            get { return _textBox == null ? string.Empty : _textBox.Text; }
        }

        /// <summary>グループの名前 (前後の空白は除く)。</summary>
        public string GroupName
        {
            get { return _nameBox == null ? string.Empty : _nameBox.Text.Trim(); }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 小窓はフォーカスを奪わない作りなので、ここで前面に出してすぐ入力できるようにする
            Activate();
            if (_textBox != null)
            {
                // 本文は必須なので、本文の欄から始める
                _textBox.Focus();
                _textBox.SelectionStart = _textBox.TextLength;
            }
            else
            {
                _nameBox.Focus();
                _nameBox.SelectAll();
            }
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
            // 空白だけの本文・名前では登録させない (一覧に何も出ない行ができてしまう)。
            // 定型文は、パスがあれば本文が空でもよい
            _ok.Enabled = _textBox != null
                ? _textBox.Text.Trim().Length > 0 || _pathBox.Text.Trim().Length > 0
                : _nameBox.Text.Trim().Length > 0;
        }

        private static string DroppedPath(DragEventArgs e)
        {
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            return paths != null && paths.Length > 0 ? paths[0] : null;
        }

        private void OnPathDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DroppedPath(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
        }

        /// <summary>複数を落としたときは最初の 1 つだけを入れる (1 つの定型文で開けるのは 1 つ)。</summary>
        private void OnPathDragDrop(object sender, DragEventArgs e)
        {
            string path = DroppedPath(e);
            if (path != null)
            {
                _pathBox.Text = path;
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
                }
            }
        }

        private int Scaled(int value)
        {
            return (int)Math.Round(value * DeviceDpi / 96.0);
        }
    }
}
