using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Copipe.Interop;
using Copipe.Model;
using Copipe.Services;
using Copipe.UI;

namespace Copipe
{
    /// <summary>
    /// 常駐部分。コピーされた文字を履歴に貯め、ホットキーを押している間だけ小窓に一覧で出す。
    /// 設定 (ホットキーと保持数) はトレイメニューから変更でき、設定ファイルに保存する。
    /// </summary>
    internal sealed class CopipeApp : ApplicationContext
    {
        /// <summary>押した瞬間に読めなかったとき、押している間に読み直す間隔。</summary>
        private const int RetryIntervalMs = 100;

        /// <summary>NotifyIcon.Text の上限 (.NET Framework の制限)。</summary>
        private const int TrayTextLimit = 63;

        private static string HistoryTitle
        {
            get { return Lang.T("クリップボード履歴", "Clipboard history"); }
        }
        private static string PhraseTitle
        {
            get { return Lang.T("定型文", "Snippets"); }
        }
        private const string PathSeparator = " > ";

        // 元のアプリを前面に戻した後、切り替えが終わるのを待ってダイアログの持ち主を隠す (最大 100 ms × 30 回)
        private const int OwnerHideIntervalMs = 100;
        private const int OwnerHideMaxTicks = 30;

        /// <summary>小窓に出しているもの。</summary>
        private enum PopupMode
        {
            History,
            Phrases
        }

        /// <summary>小窓を出した起動方法。</summary>
        private enum PopupTrigger
        {
            None,
            Hotkey
        }

        private readonly Settings _settings;
        private readonly ClipboardHistory _history;
        private readonly PopupForm _popup;
        private readonly HoldHotkey _hotkey;
        private readonly ClipboardMonitor _monitor;
        private readonly TextInserter _inserter;
        private readonly PopupKeys _popupKeys;
        private readonly Timer _retryTimer;
        private readonly ContextMenuStrip _trayMenu;
        private readonly ToolStripMenuItem _settingsItem;
        private readonly ToolStripMenuItem _clearItem;
        private readonly ToolStripMenuItem _aboutItem;
        private readonly ToolStripMenuItem _exitItem;
        private readonly NotifyIcon _trayIcon;
        // トレイのアイコンの画像。NotifyIcon は閉じても画像を解放しないので、自分で解放する
        private readonly Icon _trayImage;
        private readonly DialogOwner _dialogOwner = new DialogOwner();
        private readonly Timer _ownerHideTimer = new Timer();
        private int _ownerHideTicks;
        // 定型文と、今いる階層 (一番上から順に入ったグループ。一番上なら空)
        private readonly List<PhraseNode> _phrasePath = new List<PhraseNode>();
        private PhraseBook _phrases = new PhraseBook();
        private DateTime _phrasesWritten = DateTime.MinValue;
        private PopupMode _mode = PopupMode.History;
        // 小窓を出している起動方法 (出していなければ None)。出した方法でだけ消す
        private PopupTrigger _trigger = PopupTrigger.None;
        // ドラッグしている定型文の元の場所 (ドラッグしていなければ null)
        private PhraseNode _dragGroup;
        private int _dragIndex = -1;
        // 履歴の行をドラッグしている間: つかんだ内容と、ピン止めの行か (ドラッグしていなければ null)
        private string _historyDragText;
        private bool _historyDragPinned;
        private bool _exiting;
        private bool _dialogOpen;
        // ホットキーを押したときに前面だったアプリ (入力先)。ダイアログを閉じた後にここへ前面を戻す
        private IntPtr _targetWindow;
        private bool _saveWarned;
        // 初めての起動か (設定ファイルがまだ無い)。起動したら最初に設定画面を出す
        private readonly bool _firstRun;

        public CopipeApp()
        {
            _firstRun = !File.Exists(Settings.DefaultPath);
            _settings = Settings.Load(Settings.DefaultPath);
            Lang.Apply(_settings.Language);
            _history = ClipboardHistory.Load(ClipboardHistory.DefaultPath, ClipboardHistory.MaxItems);

            _popup = new PopupForm();
            _popup.Prepare();
            // 小窓が閉じられたら (taskkill などで WM_CLOSE が届いたら) Copipe ごと終了する。
            // 小窓が無いと役目を果たせないため
            _popup.FormClosed += OnPopupClosed;
            _popup.RowActivated += ActivateRow;
            _popup.RowContextRequested += OnRowContextRequested;
            _popup.RowDragStarted += OnRowDragStarted;
            _popup.DragOpenRequested += OnDragOpenRequested;
            _popup.LevelClicked += OnLevelClicked;
            _popup.PinIconClicked += OnPinIconClicked;
            _popup.WheelNotched += OnWheelNotched;
            _popup.RowDragEnded += OnRowDragEnded;
            _popup.ExternalDropped += OnExternalDropped;
            _popup.DropValidator = CanDrop;
            _popup.OutsideDragData = OutsideDragData;
            _popup.InsertOnSingleClick = (_settings.InsertClick == InsertClick.Single);
            _inserter = new TextInserter(_popup.Handle);
            _popupKeys = new PopupKeys();
            _popupKeys.NumberPressed += OnNumberKeyPressed;
            _popupKeys.LetterPressed += OnLetterKeyPressed;
            _popupKeys.ModePressed += OnModeKeyPressed;
            _popupKeys.EscapePressed += OnEscapePressed;
            _popupKeys.ArrowPressed += OnArrowPressed;

            _hotkey = new HoldHotkey();
            _hotkey.Pressed += OnHotkeyPressed;
            _hotkey.Released += OnHotkeyReleased;

            _monitor = new ClipboardMonitor();
            _monitor.Changed += OnClipboardChanged;

            _retryTimer = new Timer();
            _retryTimer.Interval = RetryIntervalMs;
            _retryTimer.Tick += OnRetryTimerTick;

            _ownerHideTimer.Interval = OwnerHideIntervalMs;
            _ownerHideTimer.Tick += OnOwnerHideTimerTick;

            // 文字は UpdateLabels で入れる (言語を変えたときも入れ直す)
            _settingsItem = new ToolStripMenuItem(string.Empty, null, OnSettingsClick);
            _clearItem = new ToolStripMenuItem(string.Empty, null, OnClearHistoryClick);
            _aboutItem = new ToolStripMenuItem(string.Empty, null, OnAboutClick);
            _exitItem = new ToolStripMenuItem(string.Empty, null, OnExitClick);
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add(_settingsItem);
            _trayMenu.Items.Add(_clearItem);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(_aboutItem);
            _trayMenu.Items.Add(_exitItem);

            _trayIcon = new NotifyIcon();
            // トレイの大きさ (SmallIconSize。拡大率 100% で 16、150% で 24 など) のものを、縮めずにそのまま読む
            _trayImage = AppIcon.Load(SystemInformation.SmallIconSize);
            _trayIcon.Icon = _trayImage;
            _trayIcon.ContextMenuStrip = _trayMenu;

            UpdateLabels();
        }

        /// <summary>設定にあるホットキーの表示名 (例: F1、Ctrl+Space、無変換)。</summary>
        public string HotkeyName
        {
            get { return HotkeyText.Display(_settings.Hotkey); }
        }

        /// <summary>起動方法の表示 (例: ホットキー: Pause)。</summary>
        private string TriggerText
        {
            get { return Lang.T("ホットキー: ", "Hotkey: ") + HotkeyName; }
        }

        /// <summary>ホットキーを登録し、クリップボードの監視を始めて、トレイにアイコンを出す。</summary>
        public bool Start()
        {
            if (!_hotkey.TryRegister(_settings.Hotkey))
            {
                return false;
            }

            if (!_monitor.Start())
            {
                MessageBox.Show(
                    Lang.T("クリップボードの変化を受け取れませんでした。履歴は貯まりません。\n\n" +
                           "Copipe をいったん終了して、もう一度お試しください。",
                           "Could not watch the clipboard. History will not be recorded.\n\n" +
                           "Please exit Copipe and try again."),
                    "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // 起動する前にコピーしていた内容も拾う
            CaptureClipboard(false);

            _trayIcon.Visible = true;
            if (_firstRun)
            {
                // メッセージループが回り始めてから出す (Start は Application.Run の前に呼ばれる)
                _popup.BeginInvoke((Action)ShowFirstRunSettings);
            }
            return true;
        }

        /// <summary>
        /// 初めての起動のとき、最初に設定画面を出す (ホットキーなどを知ってもらい、選んでもらうため)。
        /// 取り消しても既定の設定で設定ファイルを作り、次からは出さない。
        /// </summary>
        private void ShowFirstRunSettings()
        {
            OnSettingsClick(this, EventArgs.Empty);
            if (!File.Exists(Settings.DefaultPath))
            {
                SaveSettings();
            }
        }

        private void OnClipboardChanged(object sender, EventArgs e)
        {
            // 本当にコピーされたとき。前にコピーした内容なら先頭へ移動する
            CaptureClipboard(true);
        }

        /// <summary>
        /// 今のクリップボードを読み、テキストなら履歴に加える。ファイル・フォルダーなら 1 つずつ開く項目として加える
        /// (コピーした並びの先頭が履歴の先頭に来る)。画像などは加えない。読めた種類を返す。
        /// </summary>
        /// <param name="promoteExisting">
        /// すでに履歴にある内容を先頭へ移動するか。変化の通知 (本当のコピー) のときだけ true にする。
        /// 起動時やホットキーを押した時の拾い直しは取りこぼしの保険なので、並びは変えない。
        /// そうしないと、再起動の前に Copipe が貼り付けに使った内容 (持ち主の小窓はもう無いので、
        /// 自分の書き込みと見分けられない) が、起動し直すたびに先頭へ戻ってきてしまう
        /// </param>
        private ClipboardKind CaptureClipboard(bool promoteExisting)
        {
            // Copipe 自身が入力のために置いた内容は、履歴に加えない (並びも変えない)。
            // 持ち主が小窓なら自分の書き込み
            if (ClipboardWriter.IsOwnedBy(_popup.Handle))
            {
                return ClipboardKind.Text;
            }

            ClipboardSnapshot snapshot = ClipboardReader.Read(_popup.Handle);
            List<string> entries = new List<string>();
            if (snapshot.Kind == ClipboardKind.Text)
            {
                entries.Add(snapshot.Text);
            }
            else if (snapshot.Kind == ClipboardKind.Files)
            {
                foreach (string file in snapshot.Files)
                {
                    entries.Add(FileEntry(file));
                }
                // 1 件ずつ先頭に入れるので、逆から入れてコピーした並びのままにする
                entries.Reverse();
            }

            bool added = false;
            foreach (string entry in entries)
            {
                if (promoteExisting || !_history.Contains(entry))
                {
                    added |= _history.Add(entry);
                }
            }
            if (added)
            {
                SaveHistory();
                if (_popup.Visible && _mode == PopupMode.History)
                {
                    ShowHistory();
                }
            }
            return snapshot.Kind;
        }

        private void OnHotkeyPressed(object sender, EventArgs e)
        {
            ShowPopupFor(PopupTrigger.Hotkey);
        }

        /// <summary>小窓を出す。trigger は出した起動方法 (その方法で離したときだけ消す)。</summary>
        private void ShowPopupFor(PopupTrigger trigger)
        {
            if (_dialogOpen || _trigger != PopupTrigger.None)
            {
                // 定型文のダイアログや設定画面を出している間、または既に出している間は何もしない
                return;
            }
            _trigger = trigger;
            _targetWindow = NativeMethods.GetForegroundWindow();
            _retryTimer.Stop();

            // 小窓を出している間だけ、数字キーで一覧から選び、モードキーで履歴と定型文を切り替えられるようにする。
            // 小窓が見えているのにキーが入力中のアプリに届いてしまう隙間が無いよう、小窓を出す前に登録する
            // (E2E で、表示の直後に押した数字が漏れたため)。
            // モードキーがホットキーと同じキーだと、押したことが区別できないので登録しない
            Keys modeKey = HotkeyText.ConflictsWithHotkey(_settings.Hotkey, _settings.ModeKey) ? Keys.None : _settings.ModeKey;
            _popupKeys.Enable(modeKey);

            // 通知を取りこぼしていた場合の保険として、押した時点の内容も拾う
            ClipboardKind kind = CaptureClipboard(false);
            // 開くときはいつもクリップボード履歴から。定型文も一番上の階層から
            _mode = PopupMode.History;
            _phrasePath.Clear();
            ShowMode();
            _popup.ShowAt(Cursor.Position);

            // コピーした直後は、CopyQ などの履歴ツールやエクスプローラーが一瞬クリップボードを
            // 開いていて読めないことがある (実測)。押している間は読み直す
            if (kind == ClipboardKind.Unavailable)
            {
                _retryTimer.Start();
            }
        }

        private void OnRetryTimerTick(object sender, EventArgs e)
        {
            if (CaptureClipboard(false) == ClipboardKind.Unavailable)
            {
                return;
            }
            _retryTimer.Stop();
        }

        /// <summary>数字キー (1〜9、0) で一覧から選んだとき。クリックと同じ扱い。</summary>
        private void OnNumberKeyPressed(int index)
        {
            if (RowMenu.IsOpen || _popup.IsDragging)
            {
                // 右クリックのメニューを出している間は、一覧を動かさない
                return;
            }
            // 履歴モードでは、ピン止めの行は番号なし。数字は普通の履歴の何件目か
            ActivateRow(_mode == PopupMode.History ? index + _popup.PinnedCount : index);
        }

        /// <summary>a〜z でピン止めの項目を選んだとき (履歴モードだけ)。クリックと同じ扱い。</summary>
        private void OnLetterKeyPressed(int index)
        {
            if (RowMenu.IsOpen || _popup.IsDragging || _mode != PopupMode.History || index >= _popup.PinnedCount)
            {
                return;
            }
            ActivateRow(index);
        }

        /// <summary>
        /// 一覧の index 番目を選んだとき (数字キー、またはクリック・ダブルクリック)。
        /// 項目 (履歴・定型文) なら前面のアプリのテキストカーソルの位置に入力し、グループなら中に入る。
        /// 小窓はフォーカスを奪わないので、入力中のアプリのテキストカーソルはそのまま残っている。
        /// 小窓はキーを離すまで出したままにする (続けて別の項目も入力できる)。
        /// 今表示している一覧で選ぶので、表示中に新しいコピーが増えても、見えている番号どおりに入る。
        /// </summary>
        private void ActivateRow(int index)
        {
            if (_mode == PopupMode.History)
            {
                string text = _popup.ItemAt(index);
                if (text == null)
                {
                    // 項目が無い番号 (履歴が 3 件のときの 7 など) は何もしない
                    return;
                }
                _popup.SelectItem(index);
                UseEntry(text);
                return;
            }

            PhraseNode[] slots = CurrentPhraseGroup.Slots;
            if (index < 0 || index >= slots.Length || slots[index] == null)
            {
                // 空きの枠は何もしない
                return;
            }
            PhraseNode node = slots[index];
            if (node.IsGroup)
            {
                _phrasePath.Add(node);
                ShowMode();
                return;
            }
            _popup.SelectItem(index);
            // 入力した文字・開いたものは履歴にも入れる (次から履歴モードでも使える)
            string entry = HistoryEntry(node);
            if (_history.Add(entry))
            {
                SaveHistory();
            }
            UseEntry(entry);
        }

        /// <summary>
        /// コピー・ドロップされたファイル・フォルダーを、履歴の開く項目にする。
        /// 名前だけを一覧に出す (ドライブ C:\ など名前が無ければパスのまま)。
        /// </summary>
        private static string FileEntry(string path)
        {
            return ClipboardHistory.LaunchEntry(path, Path.GetFileName(path.TrimEnd('\\')));
        }

        /// <summary>定型文を、履歴に入れるときの形にする (開く項目なら開く印付きのパス、それ以外は本文)。</summary>
        private static string HistoryEntry(PhraseNode node)
        {
            return node.Path.Length > 0 ? ClipboardHistory.LaunchEntry(node.Path, node.Label) : node.Text;
        }

        /// <summary>履歴の項目を使う。開く項目なら開き、それ以外は入力する。</summary>
        private void UseEntry(string entry)
        {
            string path, label;
            if (ClipboardHistory.TryGetLaunchPath(entry, out path, out label))
            {
                LaunchPath(path);
                return;
            }
            // テキストカーソルの位置に入力する。小窓はキーを離すまで出したまま (続けて入力できる)
            _inserter.Insert(entry);
        }

        /// <summary>ファイル・フォルダーを、関連付けられたアプリで開く。</summary>
        private void LaunchPath(string path)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Lang.T("開けませんでした: ", "Could not open: ") + path + "\r\n" + ex.Message,
                    "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// 矢印キー。↑↓は選択を動かす。定型文モードでは、→は選んでいるグループの中へ、←は 1 つ上の階層へ。
        /// 上の階層へ戻ったときは、出てきたグループを選んでおく。
        /// </summary>
        private void OnArrowPressed(Keys key)
        {
            if (RowMenu.IsOpen || _popup.IsDragging)
            {
                return;
            }
            switch (key)
            {
                case Keys.Up:
                    _popup.MoveSelection(-1);
                    break;
                case Keys.Down:
                    _popup.MoveSelection(1);
                    break;
                case Keys.Right:
                    EnterSelectedGroup();
                    break;
                case Keys.Return:
                    // グループなら中へ、文字列なら入力、ファイル・フォルダーなら開く
                    if (!EnterSelectedGroup() && _popup.SelectedIndex >= 0)
                    {
                        ActivateRow(_popup.SelectedIndex);
                    }
                    break;
                case Keys.Left:
                    if (_mode == PopupMode.Phrases && _phrasePath.Count > 0)
                    {
                        PhraseNode left = _phrasePath[_phrasePath.Count - 1];
                        _phrasePath.RemoveAt(_phrasePath.Count - 1);
                        ShowMode();
                        _popup.SelectItem(Array.IndexOf(CurrentPhraseGroup.Slots, left));
                    }
                    break;
            }
        }

        /// <summary>定型文モードで、選んでいる行がグループなら中に入り、最初の項目を選ぶ。入ったら true。</summary>
        private bool EnterSelectedGroup()
        {
            int selected = _popup.SelectedIndex;
            PhraseNode[] slots = CurrentPhraseGroup.Slots;
            if (_mode != PopupMode.Phrases || selected < 0 || selected >= slots.Length ||
                slots[selected] == null || !slots[selected].IsGroup)
            {
                return false;
            }
            ActivateRow(selected);
            _popup.MoveSelection(1);
            return true;
        }

        /// <summary>Esc で、定型文の 1 つ上の階層に戻る。一番上では何もしない。</summary>
        private void OnEscapePressed()
        {
            // ドラッグ中は、ドラッグを取り消すだけ
            if (_popup.IsDragging)
            {
                _popup.CancelDrag();
                return;
            }
            // 右クリックのメニューを出しているときは、メニューを閉じるだけ
            if (RowMenu.IsOpen)
            {
                RowMenu.Close();
                return;
            }
            if (_mode == PopupMode.Phrases && _phrasePath.Count > 0)
            {
                _phrasePath.RemoveAt(_phrasePath.Count - 1);
                ShowMode();
            }
        }

        /// <summary>
        /// 小窓の上でホイールを回したとき。上へ回すと履歴モード、下へ回すと定型文モード。
        /// 端では止まる (履歴で上へ・定型文で下へ回しても変わらない)。
        /// </summary>
        private void OnWheelNotched(int step)
        {
            if (!_popup.Visible || RowMenu.IsOpen || _popup.IsDragging)
            {
                return;
            }
            if (step < 0 && _mode == PopupMode.History)
            {
                EnterPhraseMode();
                ShowMode();
            }
            else if (step > 0 && _mode == PopupMode.Phrases)
            {
                _mode = PopupMode.History;
                ShowMode();
            }
        }

        /// <summary>
        /// モードキーで、クリップボード履歴と定型文を切り替える。小窓は出したままにする。
        /// 履歴の項目をドラッグしている間も切り替え、定型文の空きの枠に落とせるようにする。
        /// </summary>
        private void OnModeKeyPressed()
        {
            if (_popup.IsDragging && _historyDragText == null)
            {
                return;
            }
            // 右クリックのメニューを出しているときは、メニューを閉じてから切り替える
            RowMenu.Close();
            if (_mode == PopupMode.History)
            {
                EnterPhraseMode();
            }
            else
            {
                _mode = PopupMode.History;
            }
            ShowMode();
        }

        /// <summary>
        /// 定型文モードに入る。小窓を出している間は、前に定型文モードでいた階層から (開き直すと一番上から)。
        /// phrases.json を手で書き換えても再起動せずに反映されるよう、変わっていれば読み直す。
        /// </summary>
        private void EnterPhraseMode()
        {
            _mode = PopupMode.Phrases;

            DateTime written = DateTime.MinValue;
            try
            {
                if (File.Exists(PhraseBook.DefaultPath))
                {
                    written = File.GetLastWriteTimeUtc(PhraseBook.DefaultPath);
                }
            }
            catch (Exception)
            {
                // 読めなければ、前に読んだ内容のまま
                return;
            }
            if (written != _phrasesWritten)
            {
                _phrases = PhraseBook.Load(PhraseBook.DefaultPath);
                _phrasesWritten = written;
                // 読み直すと前のグループは使えないので、一番上から
                _phrasePath.Clear();
            }
        }

        /// <summary>今いる定型文の階層 (一番上ならルート)。</summary>
        private PhraseNode CurrentPhraseGroup
        {
            get { return _phrasePath.Count == 0 ? _phrases.Root : _phrasePath[_phrasePath.Count - 1]; }
        }

        /// <summary>今のモードの見出しと一覧を小窓に入れる。Esc は定型文モードの間だけ受け取る。</summary>
        private void ShowMode()
        {
            string modeKeyName = HotkeyText.Display(_settings.ModeKey);
            if (_mode == PopupMode.History)
            {
                _popupKeys.SetEscapeEnabled(false);
                _popupKeys.SetLettersEnabled(true);
                // 履歴もドラッグで並べ替える (ピン止めはピン止めの中、普通の履歴は普通の履歴の中)
                _popup.AllowDrag = true;
                // 他のアプリから落としたファイル・フォルダー・文字列は、すぐにピン止めする
                _popup.AllowExternalDrop = true;
                _popup.ExternalDropAnywhere = true;
                _popup.SetHeader(HistoryTitle, modeKeyName + ": " + PhraseTitle);
                ShowHistory();
                return;
            }

            _popupKeys.SetEscapeEnabled(true);
            _popupKeys.SetLettersEnabled(false);
            _popup.AllowDrag = true;
            _popup.AllowExternalDrop = true;
            _popup.ExternalDropAnywhere = false;
            // 見出しは今いる階層 (例: 定型文 > 社外 > 挨拶)。階層名はドラッグした項目を落とす先にもなる
            List<string> path = new List<string>();
            path.Add(PhraseTitle);
            foreach (PhraseNode group in _phrasePath)
            {
                path.Add(group.Label);
            }
            _popup.SetHeaderPath(path, modeKeyName + ": " + HistoryTitle);

            List<PopupRow> rows = new List<PopupRow>(PhraseNode.SlotCount);
            foreach (PhraseNode node in CurrentPhraseGroup.Slots)
            {
                if (node == null)
                {
                    rows.Add(new PopupRow(PopupRowKind.Empty, null));
                }
                else
                {
                    PopupRow row = new PopupRow(node.IsGroup ? PopupRowKind.Group : PopupRowKind.Item, node.Label);
                    if (!node.IsGroup && node.Path.Length > 0)
                    {
                        row.Mark = PopupRow.MarkForPath(node.Path);
                    }
                    rows.Add(row);
                }
            }
            _popup.SetRows(rows, string.Empty);
            if (_dragGroup != null)
            {
                // ドラッグ中に別の階層を開いたときは、元の行はこの一覧に無い
                _popup.SetDragSource(CurrentPhraseGroup == _dragGroup ? _dragIndex : -1);
            }
            else if (_historyDragText != null)
            {
                // 履歴からドラッグしてきた項目の行は、定型文の一覧には無い
                _popup.SetDragSource(-1);
            }
        }

        // ---- 定型文のドラッグ＆ドロップ ---------------------------------------------------

        /// <summary>ドラッグを始めたとき。どの階層のどの枠かを覚えておく (別の階層を開いても追えるように)。</summary>
        private void OnRowDragStarted(int index)
        {
            if (_mode == PopupMode.History)
            {
                StartHistoryDrag(index);
                return;
            }
            PhraseNode group = CurrentPhraseGroup;
            if (_mode != PopupMode.Phrases || index < 0 || index >= group.Slots.Length || group.Slots[index] == null)
            {
                _popup.CancelDrag();
                return;
            }
            _dragGroup = group;
            _dragIndex = index;
        }

        /// <summary>そこに落とせるか。小窓はこれを見て、落とせない先ではカーソルを「禁止」にする。</summary>
        private bool CanDrop(DropTarget target)
        {
            return _historyDragText != null ? CanDropHistory(target) : CanDropPhrase(target);
        }

        private bool CanDropPhrase(DropTarget target)
        {
            if (_dragGroup == null || _mode != PopupMode.Phrases)
            {
                return false;
            }
            PhraseNode current = CurrentPhraseGroup;
            switch (target.Kind)
            {
                case DropKind.Swap:
                    return PhraseMoves.CanSwap(_dragGroup, _dragIndex, current, target.Index);
                case DropKind.Into:
                    return target.Index >= 0 && target.Index < current.Slots.Length &&
                           PhraseMoves.CanMoveInto(_dragGroup, _dragIndex, current.Slots[target.Index]);
                case DropKind.Level:
                    return PhraseMoves.CanMoveInto(_dragGroup, _dragIndex, LevelGroup(target.Index));
                default:
                    return false;
            }
        }

        /// <summary>
        /// ドラッグしたまま止めたとき。グループの行ならそのグループを、見出しの階層名ならその階層を開く。
        /// ドラッグしている項目自身や、その中のグループは開かない (そこには落とせないため)。
        /// </summary>
        private void OnDragOpenRequested(DropTarget target)
        {
            if ((_dragGroup == null && _historyDragText == null) || _mode != PopupMode.Phrases)
            {
                return;
            }
            // 履歴からドラッグしてきた項目 (dragged は null) は、どのグループでも開く
            PhraseNode dragged = _dragGroup != null ? _dragGroup.Slots[_dragIndex] : null;
            if (target.Kind == DropKind.Into)
            {
                PhraseNode[] slots = CurrentPhraseGroup.Slots;
                if (target.Index < 0 || target.Index >= slots.Length || slots[target.Index] == null ||
                    !slots[target.Index].IsGroup || (dragged != null && PhraseMoves.Contains(dragged, slots[target.Index])))
                {
                    return;
                }
                _phrasePath.Add(slots[target.Index]);
                ShowMode();
            }
            else if (target.Kind == DropKind.Level)
            {
                GoToLevel(target.Index);
            }
        }

        /// <summary>見出しの階層名をクリックしたとき。その階層へ移動する。</summary>
        private void OnLevelClicked(int level)
        {
            if (_mode != PopupMode.Phrases || RowMenu.IsOpen)
            {
                return;
            }
            GoToLevel(level);
        }

        /// <summary>見出しの階層 (0 が一番上) へ移動する。今いる階層以下なら何もしない。</summary>
        private void GoToLevel(int level)
        {
            if (level < 0 || level >= _phrasePath.Count)
            {
                return;
            }
            _phrasePath.RemoveRange(level, _phrasePath.Count - level);
            ShowMode();
        }

        /// <summary>ドラッグが終わったとき。落とした先に応じて入れ替える・グループに入れる・上の階層に出す。</summary>
        private void OnRowDragEnded(DropTarget target)
        {
            if (_historyDragText != null)
            {
                EndHistoryDrag(target);
                return;
            }
            PhraseNode fromGroup = _dragGroup;
            int fromIndex = _dragIndex;
            _dragGroup = null;
            _dragIndex = -1;
            if (fromGroup == null || _mode != PopupMode.Phrases)
            {
                return;
            }

            PhraseNode current = CurrentPhraseGroup;
            bool moved = false;
            switch (target.Kind)
            {
                case DropKind.Swap:
                    moved = PhraseMoves.Swap(fromGroup, fromIndex, current, target.Index);
                    break;
                case DropKind.Into:
                    moved = target.Index >= 0 && target.Index < current.Slots.Length &&
                            PhraseMoves.MoveInto(fromGroup, fromIndex, current.Slots[target.Index]);
                    break;
                case DropKind.Level:
                    moved = PhraseMoves.MoveInto(fromGroup, fromIndex, LevelGroup(target.Index));
                    break;
            }
            if (moved)
            {
                SavePhrases();
            }
            if (_popup.Visible)
            {
                ShowMode();
            }
        }

        // ---- 履歴のドラッグ＆ドロップ (並べ替え) ------------------------------------------

        /// <summary>
        /// 履歴の行のドラッグを始めたとき。行の位置ではなく内容を覚えておく
        /// (ドラッグ中に新しいコピーが入って行がずれても、つかんだ項目を動かせるように)。
        /// </summary>
        private void StartHistoryDrag(int index)
        {
            string text = _popup.ItemAt(index);
            if (text == null)
            {
                _popup.CancelDrag();
                return;
            }
            _historyDragText = text;
            _historyDragPinned = index < _popup.PinnedCount;
        }

        /// <summary>
        /// 履歴の行に落とせるか。ピン止めはピン止めの行へ、普通の履歴は普通の履歴の行へだけ。自分の行には落とさない。
        /// </summary>
        private bool CanDropHistory(DropTarget target)
        {
            if (_mode == PopupMode.Phrases)
            {
                PhraseNode group;
                return PhraseDropSlot(target, out group) >= 0;
            }
            if (target.Kind != DropKind.Swap)
            {
                return false;
            }
            int toIndex = HistoryDropIndex(target.Index);
            if (toIndex < 0)
            {
                return false;
            }
            IList<string> list = _historyDragPinned ? _history.Pinned : _history.Items;
            return list.IndexOf(_historyDragText) >= 0 && list.IndexOf(_historyDragText) != toIndex;
        }

        /// <summary>小窓の行の位置を、ドラッグしている側 (ピン止め / 普通の履歴) の中での位置にする。側が違えば -1。</summary>
        private int HistoryDropIndex(int row)
        {
            int pinnedCount = _popup.PinnedCount;
            if (_historyDragPinned)
            {
                return (row >= 0 && row < pinnedCount) ? row : -1;
            }
            return (row >= pinnedCount && row - pinnedCount < _history.Items.Count) ? row - pinnedCount : -1;
        }

        /// <summary>履歴の行のドラッグが終わったとき。落とした行の位置へ移す。</summary>
        private void EndHistoryDrag(DropTarget target)
        {
            string text = _historyDragText;
            _historyDragText = null;
            if (_mode == PopupMode.Phrases)
            {
                PhraseNode group;
                int slot = PhraseDropSlot(target, out group);
                if (slot >= 0)
                {
                    group.Slots[slot] = PhraseFromHistory(text);
                    SavePhrases();
                }
                if (_popup.Visible)
                {
                    ShowMode();
                }
                return;
            }
            if (target.Kind != DropKind.Swap)
            {
                return;
            }
            int toIndex = HistoryDropIndex(target.Index);
            if (toIndex >= 0 && _history.Move(text, toIndex))
            {
                SaveHistory();
            }
            if (_popup.Visible)
            {
                ShowHistory();
            }
        }

        /// <summary>
        /// 履歴の項目を定型文として落とす枠。今の階層の空きの枠ならそこ、グループの行の中央か見出しの階層名なら
        /// そのグループの最初の空き (定型文のドラッグと同じ)。落とせなければ -1。
        /// </summary>
        private int PhraseDropSlot(DropTarget target, out PhraseNode group)
        {
            PhraseNode[] slots = CurrentPhraseGroup.Slots;
            group = null;
            switch (target.Kind)
            {
                case DropKind.Swap:
                    group = CurrentPhraseGroup;
                    return target.Index >= 0 && target.Index < slots.Length && slots[target.Index] == null ? target.Index : -1;
                case DropKind.Into:
                    group = target.Index >= 0 && target.Index < slots.Length ? slots[target.Index] : null;
                    break;
                case DropKind.Level:
                    // 今いる階層は、空きの枠に直接落とせばよいので対象にしない
                    group = target.Index < _phrasePath.Count ? LevelGroup(target.Index) : null;
                    break;
            }
            return group != null && group.IsGroup ? PhraseMoves.FirstEmptySlot(group) : -1;
        }

        /// <summary>履歴の項目を定型文にする。開く項目 (ファイル・フォルダー・URI) は、それを開く定型文にする。</summary>
        private static PhraseNode PhraseFromHistory(string entry)
        {
            string path, label;
            if (ClipboardHistory.TryGetLaunchPath(entry, out path, out label))
            {
                // 名前がパスそのままなら表示名は空にする (一覧にはファイル名が出る)
                return PhraseNode.CreatePhrase(label == path ? string.Empty : label, string.Empty, path);
            }
            return PhraseNode.CreatePhrase(string.Empty, entry, string.Empty);
        }

        // ---- 他のアプリへのドラッグ＆ドロップ ----------------------------------------------

        /// <summary>
        /// ドラッグしている項目を小窓の外へ出したとき、他のアプリに渡すデータ。
        /// ファイル・フォルダーはそのもの、それ以外 (URI も) は文字列として渡す。グループは渡さない (null)。
        /// </summary>
        private IDataObject OutsideDragData()
        {
            string entry = _historyDragText;
            if (entry == null && _dragGroup != null)
            {
                PhraseNode node = _dragGroup.Slots[_dragIndex];
                if (node != null && !node.IsGroup)
                {
                    entry = HistoryEntry(node);
                }
            }
            if (entry == null)
            {
                return null;
            }
            string path, label;
            return ClipboardHistory.TryGetLaunchPath(entry, out path, out label) ? PathData(path) : TextData(entry);
        }

        private static IDataObject PathData(string path)
        {
            // 開くパスに URI (https:、mailto: など) を登録した項目は、文字として渡す。
            // ファイルとして渡すと、ブラウザーはローカルのファイル (file:///https://…) として開こうとする
            if (PopupRow.IsUri(path))
            {
                return TextData(path);
            }
            DataObject data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { path });
            return data;
        }

        private static IDataObject TextData(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            // ANSI のテキストしか読まないアプリにも落とせるよう、両方の形で渡す
            // (クリップボードと違い、ドラッグ＆ドロップでは Windows が変換してくれない)
            DataObject data = new DataObject();
            data.SetData(DataFormats.UnicodeText, text);
            data.SetData(DataFormats.Text, text);
            return data;
        }

        /// <summary>見出しの階層名の番号に当たるグループ (0 が一番上)。</summary>
        private PhraseNode LevelGroup(int level)
        {
            if (level <= 0)
            {
                return _phrases.Root;
            }
            return level <= _phrasePath.Count ? _phrasePath[level - 1] : null;
        }

        private void OnHotkeyReleased(object sender, EventArgs e)
        {
            // 小窓を出していないとき (ダイアログを出している間に押して離したときなど) は何もしない
            if (_trigger == PopupTrigger.Hotkey)
            {
                ClosePopup();
            }
        }

        /// <summary>小窓を閉じる。</summary>
        private void ClosePopup()
        {
            _trigger = PopupTrigger.None;
            // 数字キーやモードキーを横取りしたままにすると、どのアプリでも打てなくなる。真っ先に解除する
            _popupKeys.Disable();
            // 離した後に遅れて表示が変わらないよう、先に読み直しを止める
            _retryTimer.Stop();
            // 右クリックのメニューも小窓と一緒に閉じる (項目を選ぶ前に離したとき)
            RowMenu.Close();
            // メニューを出すと小窓が前面になる。前面のまま隠すと Windows が別のアプリ (実測では CLaunch) を
            // 前面にし、Copipe からは元のアプリに戻せなくなる。隠す前に元のアプリへ戻す
            if (NativeMethods.GetForegroundWindow() == _popup.Handle)
            {
                RestoreTargetWindow();
            }
            _popup.HidePopup();
        }

        // ---- 定型文の右クリックのメニューと、登録・編集・削除 ------------------------------

        /// <summary>定型文モードで行が右クリックされたとき。枠の中身に応じたメニューを出す。</summary>
        /// <summary>履歴の一覧を小窓に入れる (ピン止めを上部に)。</summary>
        private void ShowHistory()
        {
            _popup.SetHistory(_history.Pinned, _history.Items, ClipboardHistory.MaxItems);
        }

        // ---- 履歴のピン止め --------------------------------------------------------------

        /// <summary>履歴の行を右クリックしたとき。「ピン止め」か「ピン止めを外す」と、「削除」を出す。</summary>
        private void ShowHistoryMenu(int index, Point screen)
        {
            string text = _popup.ItemAt(index);
            if (text == null)
            {
                return;
            }
            bool pinned = index < _popup.PinnedCount;
            string label = pinned ? Lang.T("ピン止めを外す", "Unpin")
                : _history.IsPinnedFull ? Lang.T("ピン止めは 26 件までです", "Up to 26 pins")
                : Lang.T("ピン止め", "Pin");
            // 履歴の項目は、コピーし直せば戻せるので確認しない
            int chosen = ShowRowMenu(screen, new[] { label, RowMenu.Separator, Lang.T("削除", "Delete") });
            if (chosen == 0)
            {
                ChangePin(text, !pinned);
            }
            else if (chosen == 2 && _history.Remove(text))
            {
                SaveHistory();
                if (_popup.Visible && _mode == PopupMode.History)
                {
                    ShowHistory();
                }
            }
            // メニューを出すと小窓が前面になるので、元のアプリに戻す (続けて入力できるように)
            RestoreTargetWindow();
        }

        /// <summary>ピン止めの行の 📌 がクリックされたとき。ピン止めを外す。</summary>
        private void OnPinIconClicked(int index)
        {
            if (_mode != PopupMode.History || index >= _popup.PinnedCount)
            {
                return;
            }
            string text = _popup.ItemAt(index);
            if (text != null)
            {
                ChangePin(text, false);
            }
        }

        private void ChangePin(string text, bool pin)
        {
            bool changed = pin ? _history.Pin(text) : _history.Unpin(text);
            if (!changed)
            {
                return;
            }
            SaveHistory();
            if (_popup.Visible && _mode == PopupMode.History)
            {
                ShowHistory();
            }
        }

        /// <summary>
        /// 右クリックのメニューを出す。小窓はフォーカスを奪わないので、メニューにはキーが届かない。
        /// メニューを出している間は Esc をホットキーで受け取り、メニューを閉じる (OnEscapePressed)。
        /// </summary>
        private int ShowRowMenu(Point screen, IList<string> labels)
        {
            _popupKeys.SetEscapeEnabled(true);
            try
            {
                return RowMenu.Show(_popup.Handle, screen, labels);
            }
            finally
            {
                _popupKeys.SetEscapeEnabled(_mode == PopupMode.Phrases);
            }
        }

        private void OnRowContextRequested(int index, Point screen)
        {
            if (_dialogOpen || RowMenu.IsOpen)
            {
                return;
            }
            if (_mode == PopupMode.History)
            {
                ShowHistoryMenu(index, screen);
                return;
            }
            PhraseNode group = CurrentPhraseGroup;
            if (index < 0 || index >= group.Slots.Length)
            {
                return;
            }

            // どの枠の操作かは、メニューを出した時点で決めておく
            PhraseNode node = group.Slots[index];
            List<string> labels = new List<string>();
            List<Action> actions = new List<Action>();
            if (node == null)
            {
                labels.Add(Lang.T("定型文を登録...", "New snippet..."));
                actions.Add(delegate { EditPhrase(group, index); });
                labels.Add(Lang.T("グループを作成...", "New group..."));
                actions.Add(delegate { EditGroup(group, index); });
            }
            else if (node.IsGroup)
            {
                labels.Add(Lang.T("名前を変更...", "Rename..."));
                actions.Add(delegate { EditGroup(group, index); });
                labels.Add(Lang.T("削除", "Delete"));
                actions.Add(delegate { DeleteSlot(group, index); });
            }
            else
            {
                if (node.Text.Length > 0 || node.Path.Length > 0)
                {
                    labels.Add(Lang.T("履歴にピン止め", "Pin to history"));
                    actions.Add(delegate { PinPhrase(node); });
                    labels.Add(RowMenu.Separator);
                    actions.Add(null);
                }
                labels.Add(Lang.T("編集...", "Edit..."));
                actions.Add(delegate { EditPhrase(group, index); });
                labels.Add(Lang.T("削除", "Delete"));
                actions.Add(delegate { DeleteSlot(group, index); });
            }

            int chosen = ShowRowMenu(screen, labels);
            if (chosen >= 0 && chosen < actions.Count && actions[chosen] != null)
            {
                // 右クリックの処理から抜けてからダイアログを出す
                _popup.BeginInvoke(actions[chosen]);
            }
            else
            {
                // メニューを出すと小窓が前面になるので、選ばなかったときは元のアプリに戻す
                // (戻さないと、続けて数字キーで選んだ定型文が元のアプリに入らない)
                RestoreTargetWindow();
            }
        }

        /// <summary>定型文の本文 (開く項目ならパス) を、クリップボード履歴のピン止めの先頭に入れる。小窓は定型文モードのまま。</summary>
        private void PinPhrase(PhraseNode node)
        {
            // 開く項目は、履歴に入れるときと同じ形 (開く印付き) でピン止めする
            if (_history.PinText(HistoryEntry(node)))
            {
                SaveHistory();
            }
            // メニューを出すと小窓が前面になるので、元のアプリに戻す (続けて入力できるように)
            RestoreTargetWindow();
        }

        /// <summary>
        /// 他のアプリから落とされたファイル・フォルダー (path) か文字列 (text) を受け取る。
        /// 履歴モードではピン止めの先頭に入れ、定型文モードでは空きの枠 (index) に登録するダイアログを出す。
        /// </summary>
        private void OnExternalDropped(int index, string path, string text)
        {
            if (_mode == PopupMode.History)
            {
                // ponytail: ピン止めが満杯なら何も起きない。知らせが要るならトレイの通知などで出す
                string entry = path != null ? FileEntry(path) : text;
                // URI の文字列は、文字として入力するのではなく開く項目にする
                if (path == null && PopupRow.IsUri(text.Trim()))
                {
                    entry = ClipboardHistory.LaunchEntry(text.Trim(), null);
                }
                if (!_dialogOpen && _history.PinText(entry))
                {
                    SaveHistory();
                    ShowHistory();
                }
                return;
            }
            PhraseNode group = CurrentPhraseGroup;
            if (_dialogOpen || _mode != PopupMode.Phrases || index < 0 || index >= group.Slots.Length ||
                group.Slots[index] != null)
            {
                return;
            }
            // ドラッグ元のアプリを待たせないよう、ドロップの処理から抜けてからダイアログを出す
            _popup.BeginInvoke((Action)delegate { EditPhrase(group, index, text, path); });
        }

        /// <summary>定型文の登録 (空きの枠) か編集 (定型文の枠)。登録では、本文とパスの初期値を渡せる。</summary>
        private void EditPhrase(PhraseNode group, int index, string newText = null, string newPath = null)
        {
            PhraseNode node = group.Slots[index];
            bool isNew = (node == null);
            RunPhraseDialog(delegate
            {
                using (PhraseDialog dialog = PhraseDialog.ForPhrase(
                    isNew ? Lang.T("定型文を登録", "New snippet") : Lang.T("定型文を編集", "Edit snippet"), SlotLocation(index),
                    isNew ? string.Empty : node.Title, isNew ? (newText ?? string.Empty) : node.Text,
                    isNew ? (newPath ?? string.Empty) : node.Path))
                {
                    if (dialog.ShowDialog(_dialogOwner) != DialogResult.OK)
                    {
                        return;
                    }
                    if (isNew)
                    {
                        group.Slots[index] = PhraseNode.CreatePhrase(dialog.PhraseTitle, dialog.PhraseText, dialog.PhrasePath);
                    }
                    else
                    {
                        node.Name = dialog.PhraseTitle;
                        node.Text = dialog.PhraseText;
                        node.Path = dialog.PhrasePath;
                    }
                    SavePhrases();
                }
            });
        }

        /// <summary>グループの作成 (空きの枠) か名前の変更 (グループの枠)。</summary>
        private void EditGroup(PhraseNode group, int index)
        {
            PhraseNode node = group.Slots[index];
            bool isNew = (node == null);
            RunPhraseDialog(delegate
            {
                using (PhraseDialog dialog = PhraseDialog.ForGroup(
                    isNew ? Lang.T("グループを作成", "New group") : Lang.T("グループの名前を変更", "Rename group"), SlotLocation(index),
                    isNew ? string.Empty : node.Name))
                {
                    if (dialog.ShowDialog(_dialogOwner) != DialogResult.OK)
                    {
                        return;
                    }
                    if (isNew)
                    {
                        group.Slots[index] = PhraseNode.CreateGroup(dialog.GroupName);
                    }
                    else
                    {
                        node.Name = dialog.GroupName;
                    }
                    SavePhrases();
                }
            });
        }

        /// <summary>
        /// 枠を空きにする。取り消せないので確認する (グループは中の件数も示す)。
        /// 確認はメニューを閉じてからダイアログで出す。メニューの中で確認すると、メニューを開いたままになり、
        /// その間は CapsLock のホットキーのフックが呼ばれず、離しても小窓とメニューが消えなかった
        /// </summary>
        private void DeleteSlot(PhraseNode group, int index)
        {
            PhraseNode node = group.Slots[index];
            if (node == null)
            {
                return;
            }
            RunPhraseDialog(delegate
            {
                string name = PreviewText.Line(node.Label, 40);
                string message;
                if (node.IsGroup)
                {
                    int phrases = 0;
                    int groups = 0;
                    CountContents(node, ref phrases, ref groups);
                    message = Lang.T("グループ「" + name + "」を削除します。\n中の定型文 " + phrases + " 件とグループ " + groups +
                                     " 件も削除されます。\n\nよろしいですか。",
                                     "Delete the group \"" + name + "\"?\nThe " + phrases + " snippet(s) and " + groups +
                                     " group(s) inside it will also be deleted.");
                }
                else
                {
                    message = Lang.T("定型文「" + name + "」を削除します。\n\nよろしいですか。", "Delete the snippet \"" + name + "\"?");
                }
                if (MessageBox.Show(_dialogOwner, message, "Copipe", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    return;
                }
                group.Slots[index] = null;
                SavePhrases();
            });
        }

        private static void CountContents(PhraseNode group, ref int phrases, ref int groups)
        {
            foreach (PhraseNode child in group.Slots)
            {
                if (child == null)
                {
                    continue;
                }
                if (child.IsGroup)
                {
                    groups++;
                    CountContents(child, ref phrases, ref groups);
                }
                else
                {
                    phrases++;
                }
            }
        }

        /// <summary>
        /// ダイアログを出す前後の片付け。小窓を閉じて数字キー・モードキー・Esc の横取りをやめ
        /// (ダイアログで数字や Tab を打てるように)、閉じたら元のアプリを前面に戻す。
        /// </summary>
        private void RunPhraseDialog(Action show)
        {
            if (_dialogOpen)
            {
                return;
            }
            _dialogOpen = true;
            try
            {
                _popupKeys.Disable();
                _retryTimer.Stop();
                _ownerHideTimer.Stop();
                // 持ち主を先に出して前面にしておく。小窓を隠しても前面が他のアプリへ移らない
                _dialogOwner.Show();
                _dialogOwner.Activate();
                _trigger = PopupTrigger.None;
                _popup.HidePopup();
                show();
            }
            finally
            {
                _dialogOpen = false;
                // 持ち主が前面のうちに元のアプリへ戻す。持ち主は、切り替えが終わってから隠す
                RestoreTargetWindow();
                HideDialogOwnerWhenRestored();
            }
        }

        /// <summary>
        /// ホットキーを押したときに前面だったアプリを、前面に戻す。
        /// 右クリックのメニューを出すと小窓が前面になり (Windows 標準のメニューの動き。実測)、
        /// ダイアログを閉じた後も前面が元のアプリに戻らないため。
        /// </summary>
        /// <summary>
        /// ダイアログの持ち主を、元のアプリへの前面の切り替えが終わってから隠す。
        /// 切り替えは相手のアプリが応じて終わるので、終わる前 (前面が「なし」の間) に隠すと、
        /// Windows が別のアプリ (実測では VS Code) を前面にしてしまう。UI を止めないよう、タイマーで待つ。
        /// </summary>
        private void HideDialogOwnerWhenRestored()
        {
            _ownerHideTicks = 0;
            if (IsRestoreDone())
            {
                _ownerHideTimer.Stop();
                _dialogOwner.Hide();
                return;
            }
            _ownerHideTimer.Start();
        }

        private void OnOwnerHideTimerTick(object sender, EventArgs e)
        {
            _ownerHideTicks++;
            if (!IsRestoreDone() && _ownerHideTicks < OwnerHideMaxTicks)
            {
                return;
            }
            _ownerHideTimer.Stop();
            // 次のダイアログを出しているなら、持ち主はそのまま使う
            if (!_dialogOpen)
            {
                _dialogOwner.Hide();
            }
        }

        /// <summary>元のアプリへの切り替えが終わったか (戻す先が無くなった・別のアプリが前面になったときも終わりとする)。</summary>
        private bool IsRestoreDone()
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            return _targetWindow == IntPtr.Zero || !NativeMethods.IsWindow(_targetWindow) ||
                   (foreground != IntPtr.Zero && foreground != _dialogOwner.Handle);
        }

        private void RestoreTargetWindow()
        {
            if (_targetWindow == IntPtr.Zero || !NativeMethods.IsWindow(_targetWindow) ||
                NativeMethods.GetForegroundWindow() == _targetWindow)
            {
                return;
            }
            NativeMethods.SetForegroundWindow(_targetWindow);
        }

        /// <summary>ダイアログに出す、枠の場所 (例: 定型文 > 社外 の 3 番)。</summary>
        private string SlotLocation(int index)
        {
            return Lang.T(PhrasePathText() + " の " + ItemNumber.Label(index) + " 番", PhrasePathText() + ", slot " + ItemNumber.Label(index));
        }

        private string PhrasePathText()
        {
            StringBuilder text = new StringBuilder(PhraseTitle);
            foreach (PhraseNode group in _phrasePath)
            {
                text.Append(PathSeparator).Append(group.Label);
            }
            return text.ToString();
        }

        private void SavePhrases()
        {
            try
            {
                _phrases.Save(PhraseBook.DefaultPath);
                // 自分で書いた内容を、次に定型文モードに入ったとき読み直さないように覚えておく
                _phrasesWritten = File.GetLastWriteTimeUtc(PhraseBook.DefaultPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Lang.T("定型文を保存できませんでした。Copipe を終了すると、今の変更は消えます。\n\n",
                           "Could not save snippets. Your changes will be lost when Copipe exits.\n\n") +
                    PhraseBook.DefaultPath + "\n\n" + ex.Message,
                    "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnSettingsClick(object sender, EventArgs e)
        {
            if (_dialogOpen)
            {
                return;
            }
            _dialogOpen = true;
            try
            {
                // 解除しないと、今のホットキーを押しても設定画面に取り込めない
                _hotkey.Unregister();
                _popupKeys.Disable();
                _trigger = PopupTrigger.None;
                _popup.HidePopup();

                // 登録できないキーが選ばれたときは、閉じずに設定画面へ戻る
                bool finished = false;
                while (!finished)
                {
                    Keys chosenHotkey;
                    Keys chosenModeKey;
                    InsertClick chosenClick;
                    UiLanguage chosenLanguage;
                    using (SettingsDialog dialog = new SettingsDialog(
                        _settings.Hotkey, _settings.ModeKey, _settings.InsertClick, _settings.Language))
                    {
                        if (dialog.ShowDialog() != DialogResult.OK)
                        {
                            RestoreHotkey();
                            return;
                        }
                        chosenHotkey = dialog.SelectedHotkey;
                        chosenModeKey = dialog.SelectedModeKey;
                        chosenClick = dialog.SelectedInsertClick;
                        chosenLanguage = dialog.SelectedLanguage;
                    }

                    if (chosenHotkey == _settings.Hotkey || _hotkey.TryRegister(chosenHotkey))
                    {
                        if (chosenHotkey == _settings.Hotkey)
                        {
                            RestoreHotkey();
                        }
                        ApplySettings(chosenHotkey, chosenModeKey, chosenClick, chosenLanguage);
                        finished = true;
                    }
                    else
                    {
                        MessageBox.Show(
                            Lang.T(HotkeyText.Display(chosenHotkey) + " は、他のアプリまたは Windows が使用中のため設定できませんでした。\n\n" +
                                   "別のキーを選んでください。",
                                   HotkeyText.Display(chosenHotkey) + " is in use by another app or Windows and cannot be set.\n\n" +
                                   "Please choose another key."),
                            "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            finally
            {
                _dialogOpen = false;
            }
        }

        private void ApplySettings(Keys hotkey, Keys modeKey, InsertClick insertClick, UiLanguage language)
        {
            bool changed = (hotkey != _settings.Hotkey) || (modeKey != _settings.ModeKey) ||
                           (insertClick != _settings.InsertClick) || (language != _settings.Language);
            if (!changed)
            {
                return;
            }

            _settings.Hotkey = hotkey;
            _settings.ModeKey = modeKey;
            _settings.InsertClick = insertClick;
            _settings.Language = language;
            Lang.Apply(language);
            _popup.InsertOnSingleClick = (insertClick == InsertClick.Single);
            SaveSettings();
            UpdateLabels();
        }

        /// <summary>設定にあるホットキーで登録し直す (設定画面のために解除していたものを戻す)。</summary>
        private void RestoreHotkey()
        {
            if (_hotkey.TryRegister(_settings.Hotkey))
            {
                return;
            }

            MessageBox.Show(
                Lang.T("ホットキー " + HotkeyName + " を登録できませんでした。\n\n" +
                       "他のアプリまたは Windows が使用中です。Copipe をいったん終了して、もう一度お試しください。",
                       "Could not register the hotkey " + HotkeyName + ".\n\n" +
                       "It is in use by another app or Windows. Please exit Copipe and try again."),
                "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnAboutClick(object sender, EventArgs e)
        {
            if (_dialogOpen)
            {
                return;
            }
            // 出している間は、小窓を出さない (設定画面と同じ扱い)
            _dialogOpen = true;
            try
            {
                using (AboutDialog dialog = new AboutDialog())
                {
                    dialog.ShowDialog();
                }
            }
            finally
            {
                _dialogOpen = false;
            }
        }

        private void OnClearHistoryClick(object sender, EventArgs e)
        {
            if (MessageBox.Show(
                    Lang.T("履歴をすべて消去します。よろしいですか。\n\nピン止めした項目は残ります。",
                           "Clear all history?\n\nPinned items will be kept."),
                    "Copipe", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _history.Clear();
            SaveHistory();
            if (_popup.Visible && _mode == PopupMode.History)
            {
                ShowHistory();
            }
        }

        private void SaveSettings()
        {
            try
            {
                _settings.Save(Settings.DefaultPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Lang.T("設定を保存できませんでした。次に起動したときは元の設定に戻ります。\n\n",
                           "Could not save settings. The previous settings will be used next time.\n\n") +
                    Settings.DefaultPath + "\n\n" + ex.Message,
                    "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SaveHistory()
        {
            try
            {
                _history.Save(ClipboardHistory.DefaultPath);
            }
            catch (Exception ex)
            {
                // コピーのたびに出しては邪魔になるので、一度だけ知らせる
                if (_saveWarned)
                {
                    return;
                }
                _saveWarned = true;
                MessageBox.Show(
                    Lang.T("履歴を保存できませんでした。Copipe を終了すると履歴は消えます。\n\n",
                           "Could not save history. History will be lost when Copipe exits.\n\n") +
                    ClipboardHistory.DefaultPath + "\n\n" + ex.Message,
                    "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateLabels()
        {
            string triggers = TriggerText;
            _settingsItem.Text = Lang.T("設定... (", "Settings... (") + triggers + ")";
            _clearItem.Text = Lang.T("履歴を消去", "Clear history");
            _aboutItem.Text = Lang.T("Copipe について...", "About Copipe...");
            _exitItem.Text = Lang.T("終了", "Exit");

            string tip = Lang.T("Copipe（" + triggers + "）", "Copipe (" + triggers + ")");
            if (tip.Length > TrayTextLimit)
            {
                tip = tip.Substring(0, TrayTextLimit);
            }
            _trayIcon.Text = tip;
        }

        private void OnExitClick(object sender, EventArgs e)
        {
            ExitThread();
        }

        private void OnPopupClosed(object sender, FormClosedEventArgs e)
        {
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            if (_exiting)
            {
                return;
            }
            _exiting = true;

            // 消さずに終わると、マウスを重ねるまでトレイにアイコンの抜け殻が残る
            _trayIcon.Visible = false;
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _monitor.Dispose();
                _popupKeys.Dispose();
                _inserter.Dispose();
                _hotkey.Dispose();
                _retryTimer.Dispose();
                _trayIcon.Dispose();
                // Dispose は終了時に 2 回呼ばれることがある。Icon は 2 回解放しても例外にならない
                _trayImage.Dispose();
                _trayMenu.Dispose();
                _ownerHideTimer.Dispose();
                _dialogOwner.Dispose();
                _popup.FormClosed -= OnPopupClosed;
                _popup.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
