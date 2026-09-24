using System;
using System.Drawing;
using System.Windows.Forms;
using Copipe.Services;

namespace Copipe.UI
{
    /// <summary>
    /// 設定画面。ホットキー・ダブルタップ・モードキー・貼り付けの操作を変える。
    /// キーは押されたキーをその場で取り込む。表示している間、呼び出し側は今のホットキーを
    /// 解除しておくこと (解除しないと、今のキーを押してもここには届かない)。
    /// </summary>
    internal sealed class SettingsDialog : Form
    {
        private const string GuideNote = "キーの欄を選んでキーを押すと変わります。Enter で確定、Esc で取り消します。";
        private static readonly Color ErrorColor = Color.FromArgb(0xC0, 0x30, 0x00);

        private readonly Button _hotkeyBox;
        private readonly Button _hotkeyClear;
        private readonly Button _doubleTapBox;
        private readonly Button _doubleTapClear;
        private readonly Button _modeKeyBox;
        private readonly Label _note;
        private readonly RadioButton _singleClick;
        private readonly Button _ok;
        private Keys _selected;
        private Keys _selectedDoubleTap;
        private Keys _selectedModeKey;
        private bool _pending;

        public SettingsDialog(Keys currentHotkey, Keys currentDoubleTap, Keys currentModeKey,
                              InsertClick currentInsertClick)
        {
            _selected = currentHotkey;
            _selectedDoubleTap = HotkeyText.NormalizeModifier(currentDoubleTap);
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
            ClientSize = new Size(Scaled(380), Scaled(408));

            Label hotkeyLabel = new Label();
            hotkeyLabel.Text = "ホットキー (押し続けている間、小窓が出ます)";
            hotkeyLabel.AutoSize = true;
            hotkeyLabel.Location = new Point(Scaled(16), Scaled(14));
            Controls.Add(hotkeyLabel);

            _hotkeyBox = new Button();
            _hotkeyBox.Text = HotkeyDisplay(currentHotkey);
            _hotkeyBox.Font = new Font(Font.FontFamily, Font.SizeInPoints * 1.5f, FontStyle.Bold);
            _hotkeyBox.Bounds = new Rectangle(Scaled(16), Scaled(38), Scaled(268), Scaled(50));
            _hotkeyBox.UseMnemonic = false;
            Controls.Add(_hotkeyBox);

            _hotkeyClear = NewClearButton(new Rectangle(Scaled(292), Scaled(38), Scaled(72), Scaled(50)));
            _hotkeyClear.Click += delegate
            {
                _pending = false;
                _selected = Keys.None;
                _hotkeyBox.Text = NoneText;
                ShowNote(GuideNote, false);
                UpdateState();
            };

            Label doubleTapLabel = new Label();
            doubleTapLabel.Text = "ダブルタップ (Ctrl などを 2 回押し、押し続ける間)";
            doubleTapLabel.AutoSize = true;
            doubleTapLabel.Location = new Point(Scaled(16), Scaled(100));
            Controls.Add(doubleTapLabel);

            _doubleTapBox = new Button();
            _doubleTapBox.Text = DoubleTapText(_selectedDoubleTap);
            _doubleTapBox.Font = new Font(Font.FontFamily, Font.SizeInPoints * 1.2f, FontStyle.Bold);
            _doubleTapBox.Bounds = new Rectangle(Scaled(16), Scaled(124), Scaled(268), Scaled(40));
            _doubleTapBox.UseMnemonic = false;
            Controls.Add(_doubleTapBox);

            _doubleTapClear = NewClearButton(new Rectangle(Scaled(292), Scaled(124), Scaled(72), Scaled(40)));
            _doubleTapClear.Click += delegate
            {
                _pending = false;
                _selectedDoubleTap = Keys.None;
                _doubleTapBox.Text = NoneText;
                ShowNote(GuideNote, false);
                UpdateState();
            };

            Label modeKeyLabel = new Label();
            modeKeyLabel.Text = "モードキー (小窓で履歴と定型文を切り替える)";
            modeKeyLabel.AutoSize = true;
            modeKeyLabel.Location = new Point(Scaled(16), Scaled(176));
            Controls.Add(modeKeyLabel);

            _modeKeyBox = new Button();
            _modeKeyBox.Text = HotkeyText.Display(currentModeKey);
            _modeKeyBox.Font = new Font(Font.FontFamily, Font.SizeInPoints * 1.2f, FontStyle.Bold);
            _modeKeyBox.Bounds = new Rectangle(Scaled(16), Scaled(200), Scaled(348), Scaled(40));
            _modeKeyBox.UseMnemonic = false;
            Controls.Add(_modeKeyBox);

            _note = new Label();
            _note.Text = GuideNote;
            // 長い説明が右端で切れないよう、幅を決めて折り返す (3 行まで入る高さにする)
            _note.AutoSize = false;
            _note.UseMnemonic = false;
            _note.ForeColor = SystemColors.GrayText;
            _note.Bounds = new Rectangle(Scaled(16), Scaled(248), Scaled(348), Scaled(52));
            Controls.Add(_note);


            Label clickLabel = new Label();
            clickLabel.Text = "貼り付けの操作";
            clickLabel.AutoSize = true;
            clickLabel.Location = new Point(Scaled(16), Scaled(310));
            Controls.Add(clickLabel);

            RadioButton doubleClick = new RadioButton();
            doubleClick.Text = "ダブルクリック";
            doubleClick.AutoSize = true;
            doubleClick.Location = new Point(Scaled(120), Scaled(308));
            Controls.Add(doubleClick);

            _singleClick = new RadioButton();
            _singleClick.Text = "シングルクリック";
            _singleClick.AutoSize = true;
            _singleClick.Location = new Point(Scaled(236), Scaled(308));
            Controls.Add(_singleClick);

            doubleClick.Checked = (currentInsertClick != InsertClick.Single);
            _singleClick.Checked = (currentInsertClick == InsertClick.Single);

            _ok = new Button();
            _ok.Text = "OK";
            _ok.DialogResult = DialogResult.OK;
            _ok.Bounds = new Rectangle(Scaled(188), Scaled(358), Scaled(84), Scaled(30));
            Controls.Add(_ok);

            Button cancel = new Button();
            cancel.Text = "キャンセル";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Bounds = new Rectangle(Scaled(280), Scaled(358), Scaled(84), Scaled(30));
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

        /// <summary>OK で閉じたときに選ばれたダブルタップのキー (ControlKey・ShiftKey・Menu。使わなければ None)。</summary>
        public Keys SelectedDoubleTap
        {
            get { return _selectedDoubleTap; }
        }

        /// <summary>OK で閉じたときに選ばれたモードキー。</summary>
        public Keys SelectedModeKey
        {
            get { return _selectedModeKey; }
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

                if (ActiveControl == _hotkeyBox || ActiveControl == _doubleTapBox || ActiveControl == _modeKeyBox)
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
                    else if (ActiveControl == _doubleTapBox)
                    {
                        CaptureDoubleTap(keyData);
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
        /// 使えないキーを押して離したときは、選ばれているキーの表示に戻す
        /// (使えないキーが出たまま OK を押せてしまい、表示と実際の設定が食い違うのを防ぐ)。
        /// </summary>
        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (_pending)
            {
                _pending = false;
                _hotkeyBox.Text = HotkeyDisplay(_selected);
                _doubleTapBox.Text = DoubleTapText(_selectedDoubleTap);
                _modeKeyBox.Text = HotkeyText.Display(_selectedModeKey);
                ShowNote(GuideNote, false);
                UpdateState();
            }
        }

        private void CaptureHotkey(Keys keys)
        {
            CaptureKey(keys, "ホットキー", _hotkeyBox, delegate(Keys code) { _selected = code; });
        }

        /// <summary>
        /// ダブルタップの欄。Ctrl・Shift・Alt (左右どちらでも) を押すと取り込む。他のキーは使えるキーを案内する。
        /// Alt は使えるが、アプリによっては押して離すとメニューバーにフォーカスが移るので注意を出す。
        /// </summary>
        private void CaptureDoubleTap(Keys keys)
        {
            Keys code = HotkeyText.NormalizeModifier(keys);
            if (HotkeyText.IsValidDoubleTap(code))
            {
                _pending = false;
                _selectedDoubleTap = code;
                _doubleTapBox.Text = DoubleTapText(code);
                if (code == Keys.Menu)
                {
                    ShowNote("注意: Alt は、アプリによっては押して離すとメニューバーにフォーカスが移ります。" +
                             "よければ Enter か OK で確定します。", true);
                }
                else
                {
                    ShowNote("このキーでよければ Enter か OK で確定します。", false);
                }
                UpdateState();
                return;
            }

            // 使えないキーは、_pending のままにして OK を押させない
            _pending = true;
            _doubleTapBox.Text = HotkeyText.Display(keys & Keys.KeyCode);
            ShowNote("ダブルタップには Ctrl・Shift・Alt が使えます。どれかを押してください。", true);
            UpdateState();
        }

        private void CaptureModeKey(Keys keys)
        {
            CaptureKey(keys, "モードキー", _modeKeyBox, delegate(Keys code) { _selectedModeKey = code; });
        }

        /// <summary>
        /// ホットキーとモードキーの取り込み (どちらも同じ決まり)。修飾キーは付けずに、キーだけを取る。
        /// Ctrl などを押したままキーを押しても、キーだけになる。Ctrl や Shift だけを押したときは何もしない。
        /// 使えるキーなら select で選んだことにし、使えないキーなら理由を出して、離すまで OK を押させない。
        /// </summary>
        private void CaptureKey(Keys keys, string what, Button box, Action<Keys> select)
        {
            Keys code = keys & Keys.KeyCode;
            if (HotkeyText.IsValid(code))
            {
                _pending = false;
                // 先に選んだことにしてから確かめる (ホットキーとモードキーの重なりを、新しいキーで判定するため)
                select(code);
                box.Text = HotkeyText.Display(code);
                ShowNote("このキーでよければ Enter か OK で確定します。", false);
                UpdateState();
                return;
            }

            if (!HotkeyText.IsDigitKey(code) && !HotkeyText.CannotDetectRelease(code))
            {
                // Ctrl や Shift だけ。キーを押す途中なので、何もしない
                return;
            }

            // 使えないキーは、_pending のままにして OK を押させない
            _pending = true;
            box.Text = HotkeyText.Display(code);
            if (HotkeyText.IsDigitKey(code))
            {
                // 数字キーは、小窓の一覧から選ぶために使う
                ShowNote("数字キーは一覧から項目を選ぶために使うので、" + what + "にはできません。別のキーを押してください。", true);
            }
            else
            {
                // 半角/全角・英数・カタカナ ひらがな。押して離しても「押されたまま」に見えるので、
                // 小窓が出たまま消えなくなる
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
            bool noHotkey = (_selected == Keys.None);
            bool conflict = !noHotkey && HotkeyText.ConflictsWithHotkey(_selected, _selectedModeKey);
            // どちらも「なし」だと、小窓を出す方法が無くなる
            bool noTrigger = noHotkey && _selectedDoubleTap == Keys.None;
            // 使えないキーを表示している間は、実際に確定されるキーと食い違うので押させない
            _ok.Enabled = !_pending && !conflict && !noTrigger &&
                          (noHotkey || HotkeyText.IsValid(_selected)) && HotkeyText.IsValidModeKey(_selectedModeKey);
            if (_pending)
            {
                return;
            }
            if (noTrigger)
            {
                ShowNote("ホットキーかダブルタップのどちらかを設定してください (両方「なし」にはできません)。", true);
            }
            else if (conflict)
            {
                ShowNote("ホットキーとモードキーに同じキーは使えません。どちらかを別のキーにしてください。", true);
            }
        }

        /// <summary>欄に出す「使わない」の表示。</summary>
        private const string NoneText = "（なし）";

        private static string HotkeyDisplay(Keys keys)
        {
            return keys == Keys.None ? NoneText : HotkeyText.Display(keys);
        }

        private static string DoubleTapText(Keys keys)
        {
            return keys == Keys.None ? NoneText : HotkeyText.DoubleTapDisplay(keys);
        }

        /// <summary>欄の右に置く「なし」ボタン (押すと、その起動方法を使わない設定にする)。</summary>
        private Button NewClearButton(Rectangle bounds)
        {
            Button button = new Button();
            button.Text = "なし";
            button.Bounds = bounds;
            Controls.Add(button);
            return button;
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
                if (_doubleTapBox != null && _doubleTapBox.Font != null)
                {
                    _doubleTapBox.Font.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
