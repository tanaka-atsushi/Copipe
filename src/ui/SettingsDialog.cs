using System;
using System.Drawing;
using System.Windows.Forms;
using Copipe.Services;

namespace Copipe.UI
{
    /// <summary>
    /// 設定画面。ホットキー・モードキー・履歴の保持数・貼り付けの操作を変える。
    /// キーは押されたキーをその場で取り込む。表示している間、呼び出し側は今のホットキーを
    /// 解除しておくこと (解除しないと、今のキーを押してもここには届かない)。
    /// </summary>
    internal sealed class SettingsDialog : Form
    {
        private const string GuideNote = "キーの欄を選んでキーを押すと変わります。Enter で確定、Esc で取り消します。";
        private static readonly Color ErrorColor = Color.FromArgb(0xC0, 0x30, 0x00);

        private readonly Button _hotkeyBox;
        private readonly Button _modeKeyBox;
        private readonly Label _note;
        private readonly NumericUpDown _count;
        private readonly RadioButton _singleClick;
        private readonly Button _ok;
        private Keys _selected;
        private Keys _selectedModeKey;
        private bool _pending;

        public SettingsDialog(Keys currentHotkey, Keys currentModeKey, int currentHistoryCount,
                              int minHistoryCount, int maxHistoryCount, InsertClick currentInsertClick)
        {
            _selected = currentHotkey;
            _selectedModeKey = currentModeKey;

            Text = "設定";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            // IME にキーを横取りされると、無変換キーなどを取り込めない
            ImeMode = ImeMode.Disable;
            KeyPreview = true;
            Font = SystemFonts.MessageBoxFont;
            BackColor = SystemColors.Window;
            ClientSize = new Size(Scaled(380), Scaled(362));

            Label hotkeyLabel = new Label();
            hotkeyLabel.Text = "ホットキー (押し続けている間、小窓が出ます)";
            hotkeyLabel.AutoSize = true;
            hotkeyLabel.Location = new Point(Scaled(16), Scaled(14));
            Controls.Add(hotkeyLabel);

            _hotkeyBox = new Button();
            _hotkeyBox.Text = HotkeyText.Display(currentHotkey);
            _hotkeyBox.Font = new Font(Font.FontFamily, Font.SizeInPoints * 1.5f, FontStyle.Bold);
            _hotkeyBox.Bounds = new Rectangle(Scaled(16), Scaled(38), Scaled(348), Scaled(50));
            _hotkeyBox.UseMnemonic = false;
            Controls.Add(_hotkeyBox);

            Label modeKeyLabel = new Label();
            modeKeyLabel.Text = "モードキー (小窓で履歴と定型文を切り替える)";
            modeKeyLabel.AutoSize = true;
            modeKeyLabel.Location = new Point(Scaled(16), Scaled(100));
            Controls.Add(modeKeyLabel);

            _modeKeyBox = new Button();
            _modeKeyBox.Text = HotkeyText.Display(currentModeKey);
            _modeKeyBox.Font = new Font(Font.FontFamily, Font.SizeInPoints * 1.2f, FontStyle.Bold);
            _modeKeyBox.Bounds = new Rectangle(Scaled(16), Scaled(124), Scaled(348), Scaled(40));
            _modeKeyBox.UseMnemonic = false;
            Controls.Add(_modeKeyBox);

            _note = new Label();
            _note.Text = GuideNote;
            // 長い説明が右端で切れないよう、幅を決めて折り返す
            _note.AutoSize = false;
            _note.UseMnemonic = false;
            _note.ForeColor = SystemColors.GrayText;
            _note.Bounds = new Rectangle(Scaled(16), Scaled(172), Scaled(348), Scaled(34));
            Controls.Add(_note);

            Label countLabel = new Label();
            countLabel.Text = "履歴の保持数";
            countLabel.AutoSize = true;
            countLabel.Location = new Point(Scaled(16), Scaled(226));
            Controls.Add(countLabel);

            _count = new NumericUpDown();
            _count.Minimum = minHistoryCount;
            _count.Maximum = maxHistoryCount;
            _count.Value = Math.Max(minHistoryCount, Math.Min(maxHistoryCount, currentHistoryCount));
            _count.Bounds = new Rectangle(Scaled(120), Scaled(222), Scaled(70), Scaled(26));
            Controls.Add(_count);

            Label unitLabel = new Label();
            unitLabel.Text = "件 (" + minHistoryCount + "〜" + maxHistoryCount + ")";
            unitLabel.AutoSize = true;
            unitLabel.Location = new Point(Scaled(196), Scaled(226));
            Controls.Add(unitLabel);

            Label clickLabel = new Label();
            clickLabel.Text = "貼り付けの操作";
            clickLabel.AutoSize = true;
            clickLabel.Location = new Point(Scaled(16), Scaled(264));
            Controls.Add(clickLabel);

            RadioButton doubleClick = new RadioButton();
            doubleClick.Text = "ダブルクリック";
            doubleClick.AutoSize = true;
            doubleClick.Location = new Point(Scaled(120), Scaled(262));
            Controls.Add(doubleClick);

            _singleClick = new RadioButton();
            _singleClick.Text = "シングルクリック";
            _singleClick.AutoSize = true;
            _singleClick.Location = new Point(Scaled(236), Scaled(262));
            Controls.Add(_singleClick);

            doubleClick.Checked = (currentInsertClick != InsertClick.Single);
            _singleClick.Checked = (currentInsertClick == InsertClick.Single);

            _ok = new Button();
            _ok.Text = "OK";
            _ok.DialogResult = DialogResult.OK;
            _ok.Bounds = new Rectangle(Scaled(188), Scaled(312), Scaled(84), Scaled(30));
            Controls.Add(_ok);

            Button cancel = new Button();
            cancel.Text = "キャンセル";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Bounds = new Rectangle(Scaled(280), Scaled(312), Scaled(84), Scaled(30));
            Controls.Add(cancel);

            AcceptButton = _ok;
            CancelButton = cancel;
            UpdateState();
        }

        /// <summary>OK で閉じたときに選ばれたホットキー。</summary>
        public Keys SelectedHotkey
        {
            get { return _selected; }
        }

        /// <summary>OK で閉じたときに選ばれたモードキー。</summary>
        public Keys SelectedModeKey
        {
            get { return _selectedModeKey; }
        }

        /// <summary>OK で閉じたときに選ばれた保持数。</summary>
        public int SelectedHistoryCount
        {
            get { return (int)_count.Value; }
        }

        /// <summary>OK で閉じたときに選ばれた貼り付けの操作。</summary>
        public InsertClick SelectedInsertClick
        {
            get { return _singleClick.Checked ? InsertClick.Single : InsertClick.Double; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 開いた直後はキーを取り込める状態にしておく
            _hotkeyBox.Focus();
        }

        /// <summary>
        /// キーの欄にいる間は、押されたキーをすべて取り込む。
        /// こうしないと F1・Tab・Alt・Enter などが取り込めない。
        /// 保持数の欄では、数字を打てるよう取り込まない。
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            const int WM_KEYDOWN = 0x0100;
            const int WM_SYSKEYDOWN = 0x0104;

            if (msg.Msg == WM_KEYDOWN || msg.Msg == WM_SYSKEYDOWN)
            {
                Keys code = keyData & Keys.KeyCode;

                if (code == Keys.Escape)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                    return true;
                }

                if (ActiveControl == _hotkeyBox || ActiveControl == _modeKeyBox)
                {
                    // Enter は「確定」に使う。ホットキーにしてしまうと、どのアプリでも
                    // Enter が Copipe に奪われて文字入力ができなくなる
                    if (code == Keys.Enter)
                    {
                        if (_ok.Enabled)
                        {
                            DialogResult = DialogResult.OK;
                            Close();
                        }
                        return true;
                    }

                    if (ActiveControl == _hotkeyBox)
                    {
                        CaptureHotkey(keyData);
                    }
                    else
                    {
                        CaptureModeKey(keyData);
                    }
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// 修飾キーだけ押して離したときは、選ばれているキーの表示に戻す
        /// (「Ctrl+…」と出たまま OK を押せてしまい、表示と実際の設定が食い違うのを防ぐ)。
        /// </summary>
        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (_pending)
            {
                _pending = false;
                _hotkeyBox.Text = HotkeyText.Display(_selected);
                _modeKeyBox.Text = HotkeyText.Display(_selectedModeKey);
                ShowNote(GuideNote, false);
                UpdateState();
            }
        }

        private void CaptureHotkey(Keys keys)
        {
            if (HotkeyText.IsValid(keys))
            {
                _pending = false;
                _selected = keys;
                _hotkeyBox.Text = HotkeyText.Display(keys);
                ShowNote("このキーでよければ Enter か OK で確定します。", false);
                UpdateState();
                return;
            }

            // 使えないキーは、_pending のままにして OK を押させない
            _pending = true;
            if (HotkeyText.IsDigitKey(keys))
            {
                // 数字キーは、小窓の一覧から選ぶために使う
                _hotkeyBox.Text = HotkeyText.Display(keys);
                ShowNote("数字キーは一覧から項目を選ぶために使うので、ホットキーにはできません。別のキーを押してください。", true);
            }
            else if (HotkeyText.CannotDetectRelease(keys))
            {
                // 半角/全角・英数・カタカナ ひらがな。押して離しても「押されたまま」に見えるので、
                // 小窓が出たまま消えなくなる
                _hotkeyBox.Text = HotkeyText.Display(keys);
                ShowNote("このキーは、離したことを判定できないため使えません。別のキーを押してください。", true);
            }
            else
            {
                // Ctrl だけ・Shift だけなど。組み合わせるキーを押す途中かもしれないので、
                // 離すまでは「押している途中」として見せる
                _hotkeyBox.Text = HotkeyText.DisplayPending(keys);
                ShowNote("Ctrl や Shift だけでは設定できません。組み合わせるキーも押してください。", true);
            }
            UpdateState();
        }

        /// <summary>
        /// モードキーは修飾キーを付けずに取り込む (ホットキーを押したまま押すので、
        /// ホットキーの修飾キーは自動で付く)。Ctrl などを押したままでも、キーだけを取る。
        /// </summary>
        private void CaptureModeKey(Keys keys)
        {
            Keys code = keys & Keys.KeyCode;
            if (HotkeyText.IsValidModeKey(code))
            {
                _pending = false;
                _selectedModeKey = code;
                _modeKeyBox.Text = HotkeyText.Display(code);
                ShowNote("このキーでよければ Enter か OK で確定します。", false);
                UpdateState();
                return;
            }

            if (!HotkeyText.IsDigitKey(code) && !HotkeyText.CannotDetectRelease(code))
            {
                // Ctrl や Shift だけ。組み合わせるキーを押す途中なので、何もしない
                return;
            }

            // 使えないキーは、_pending のままにして OK を押させない
            _pending = true;
            _modeKeyBox.Text = HotkeyText.Display(code);
            if (HotkeyText.IsDigitKey(code))
            {
                ShowNote("数字キーは一覧から項目を選ぶために使うので、モードキーにはできません。別のキーを押してください。", true);
            }
            else
            {
                ShowNote("このキーは、離したことを判定できないため使えません。別のキーを押してください。", true);
            }
            UpdateState();
        }

        private void ShowNote(string text, bool error)
        {
            _note.Text = text;
            _note.ForeColor = error ? ErrorColor : SystemColors.GrayText;
        }

        private void UpdateState()
        {
            bool conflict = HotkeyText.ConflictsWithHotkey(_selected, _selectedModeKey);
            // 「Ctrl+…」と表示している間は、実際に確定されるキーと食い違うので押させない
            _ok.Enabled = !_pending && !conflict &&
                          HotkeyText.IsValid(_selected) && HotkeyText.IsValidModeKey(_selectedModeKey);
            if (conflict && !_pending)
            {
                ShowNote("ホットキーとモードキーに同じキーは使えません。どちらかを別のキーにしてください。", true);
            }
        }

        private int Scaled(int value)
        {
            return (int)Math.Round(value * DeviceDpi / 96.0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 自分で作った大きめのフォントを片づける
                if (_hotkeyBox != null && _hotkeyBox.Font != null)
                {
                    _hotkeyBox.Font.Dispose();
                }
                if (_modeKeyBox != null && _modeKeyBox.Font != null)
                {
                    _modeKeyBox.Font.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
