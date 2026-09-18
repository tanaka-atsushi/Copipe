using System;
using System.Drawing;
using System.Windows.Forms;
using Copipe.Services;

namespace Copipe.UI
{
    /// <summary>
    /// 定型文の登録・編集 (表示名と本文) と、グループの作成・名前の変更 (名前) のダイアログ。
    /// 部品の Name (titleBox・textBox・nameBox・okButton・cancelButton) は、どの部品かを示す目印。
    /// </summary>
    internal sealed class PhraseDialog : Form
    {
        private readonly TextBox _titleBox;
        private readonly TextBox _textBox;
        private readonly TextBox _nameBox;
        private readonly Button _ok;

        private PhraseDialog(string caption, string location, bool isGroup, string first, string second)
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
            Label where = new Label();
            where.Text = "場所: " + location;
            where.AutoSize = false;
            where.UseMnemonic = false;
            where.ForeColor = SystemColors.GrayText;
            where.Bounds = new Rectangle(Scaled(16), y, Scaled(428), Scaled(20));
            Controls.Add(where);
            y += Scaled(30);

            if (isGroup)
            {
                AddLabel("グループ名", y);
                y += Scaled(22);
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
                AddLabel("表示名 (省略すると、本文の最初の行を一覧に出します)", y);
                y += Scaled(22);
                _titleBox = new TextBox();
                _titleBox.Name = "titleBox";
                _titleBox.Text = first ?? string.Empty;
                _titleBox.Bounds = new Rectangle(Scaled(16), y, Scaled(428), Scaled(26));
                Controls.Add(_titleBox);
                y += Scaled(38);

                AddLabel("本文 (入力する文字。改行もそのまま入ります)", y);
                y += Scaled(22);
                _textBox = new TextBox();
                _textBox.Name = "textBox";
                _textBox.Multiline = true;
                _textBox.AcceptsReturn = true;
                _textBox.ScrollBars = ScrollBars.Vertical;
                _textBox.WordWrap = true;
                _textBox.MaxLength = ClipboardHistory.MaxTextLength;
                _textBox.Text = second ?? string.Empty;
                _textBox.Bounds = new Rectangle(Scaled(16), y, Scaled(428), Scaled(160));
                _textBox.TextChanged += delegate { UpdateState(); };
                Controls.Add(_textBox);
                y += Scaled(166);

                Label note = new Label();
                note.Text = "本文の欄では Enter で改行、Ctrl+Enter で確定します。Esc で取り消します。";
                note.AutoSize = false;
                note.UseMnemonic = false;
                note.ForeColor = SystemColors.GrayText;
                note.Bounds = new Rectangle(Scaled(16), y, Scaled(428), Scaled(20));
                Controls.Add(note);
                y += Scaled(30);
            }

            _ok = new Button();
            _ok.Name = "okButton";
            _ok.Text = "OK";
            _ok.DialogResult = DialogResult.OK;
            _ok.Bounds = new Rectangle(Scaled(268), y, Scaled(84), Scaled(30));
            Controls.Add(_ok);

            Button cancel = new Button();
            cancel.Name = "cancelButton";
            cancel.Text = "キャンセル";
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
        public static PhraseDialog ForPhrase(string caption, string location, string title, string text)
        {
            return new PhraseDialog(caption, location, false, title, text);
        }

        /// <summary>グループの作成・名前の変更のダイアログ。</summary>
        public static PhraseDialog ForGroup(string caption, string location, string name)
        {
            return new PhraseDialog(caption, location, true, name, null);
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

        private void AddLabel(string text, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.UseMnemonic = false;
            label.Location = new Point(Scaled(16), y);
            Controls.Add(label);
        }

        private void UpdateState()
        {
            // 空白だけの本文・名前では登録させない (一覧に何も出ない行ができてしまう)
            _ok.Enabled = _textBox != null
                ? _textBox.Text.Trim().Length > 0
                : _nameBox.Text.Trim().Length > 0;
        }

        private int Scaled(int value)
        {
            return (int)Math.Round(value * DeviceDpi / 96.0);
        }
    }
}
