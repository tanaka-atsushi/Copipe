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

        private const string HistoryTitle = "クリップボード履歴";
        private const string PhraseTitle = "定型文";
        private const string HistoryEmptyMessage = "（履歴はありません）";
        private const string PathSeparator = " > ";

        // 元のアプリを前面に戻すとき、切り替わるまで待つ時間 (最大 20 ms × 15 回)
        private const int RestoreWaitSteps = 15;
        private const int RestoreWaitStepMs = 20;

        /// <summary>小窓に出しているもの。</summary>
        private enum PopupMode
        {
            History,
            Phrases
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
        private readonly NotifyIcon _trayIcon;
        private readonly DialogOwner _dialogOwner = new DialogOwner();
        // 定型文と、今いる階層 (一番上から順に入ったグループ。一番上なら空)
        private readonly List<PhraseNode> _phrasePath = new List<PhraseNode>();
        private PhraseBook _phrases = new PhraseBook();
        private DateTime _phrasesWritten = DateTime.MinValue;
        private PopupMode _mode = PopupMode.History;
        // ドラッグしている定型文の元の場所 (ドラッグしていなければ null)
        private PhraseNode _dragGroup;
        private int _dragIndex = -1;
        private bool _exiting;
        private bool _dialogOpen;
        // ホットキーを押したときに前面だったアプリ (入力先)。ダイアログを閉じた後にここへ前面を戻す
        private IntPtr _targetWindow;
        private bool _saveWarned;

        public CopipeApp()
        {
            _settings = Settings.Load(Settings.DefaultPath);
            _history = ClipboardHistory.Load(ClipboardHistory.DefaultPath, _settings.HistoryCount);

            _popup = new PopupForm();
            _popup.Prepare();
            // 小窓が閉じられたら (taskkill などで WM_CLOSE が届いたら) Copipe ごと終了する。
            // 小窓が無いと役目を果たせないため
            _popup.FormClosed += OnPopupClosed;
            _popup.RowActivated += ActivateRow;
            _popup.RowContextRequested += OnRowContextRequested;
            _popup.RowDragStarted += OnRowDragStarted;
            _popup.DragOpenRequested += OnDragOpenRequested;
            _popup.RowDragEnded += OnRowDragEnded;
            _popup.DropValidator = CanDropPhrase;
            _popup.InsertOnSingleClick = (_settings.InsertClick == InsertClick.Single);
            _inserter = new TextInserter(_popup.Handle);
            _popupKeys = new PopupKeys();
            _popupKeys.NumberPressed += OnNumberKeyPressed;
            _popupKeys.ModePressed += OnModeKeyPressed;
            _popupKeys.EscapePressed += OnEscapePressed;

            _hotkey = new HoldHotkey();
            _hotkey.Pressed += OnHotkeyPressed;
            _hotkey.Released += OnHotkeyReleased;

            _monitor = new ClipboardMonitor();
            _monitor.Changed += OnClipboardChanged;

            _retryTimer = new Timer();
            _retryTimer.Interval = RetryIntervalMs;
            _retryTimer.Tick += OnRetryTimerTick;

            _settingsItem = new ToolStripMenuItem("設定...", null, OnSettingsClick);
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add(_settingsItem);
            _trayMenu.Items.Add("履歴を消去", null, OnClearHistoryClick);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("終了", null, OnExitClick);

            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = SystemIcons.Application;
            _trayIcon.ContextMenuStrip = _trayMenu;

            UpdateLabels();
        }

        /// <summary>設定にあるホットキーの表示名 (例: F1、Ctrl+Space、無変換)。</summary>
        public string HotkeyName
        {
            get { return HotkeyText.Display(_settings.Hotkey); }
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
                    "クリップボードの変化を受け取れませんでした。履歴は貯まりません。\n\n" +
                    "Copipe をいったん終了して、もう一度お試しください。",
                    "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // 起動する前にコピーしていた内容も拾う
            CaptureClipboard(false);

            _trayIcon.Visible = true;
            return true;
        }

        private void OnClipboardChanged(object sender, EventArgs e)
        {
            // 本当にコピーされたとき。前にコピーした内容なら先頭へ移動する
            CaptureClipboard(true);
        }

        /// <summary>今のクリップボードを読み、テキストなら履歴に加える。読めた種類を返す。</summary>
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
            if (snapshot.Kind != ClipboardKind.Text)
            {
                // 履歴に残すのはテキストだけ (画像やファイルは残さない)
                return snapshot.Kind;
            }
            if (!promoteExisting && _history.Items.Contains(snapshot.Text))
            {
                return snapshot.Kind;
            }

            if (_history.Add(snapshot.Text))
            {
                SaveHistory();
                if (_popup.Visible && _mode == PopupMode.History)
                {
                    _popup.SetItems(_history.Items, HistoryEmptyMessage);
                }
            }
            return snapshot.Kind;
        }

        private void OnHotkeyPressed(object sender, EventArgs e)
        {
            if (_dialogOpen)
            {
                // 定型文のダイアログを出している間は、小窓を出さない
                return;
            }
            _targetWindow = NativeMethods.GetForegroundWindow();
            DebugTrace.Write("hotkey target=" + DebugTrace.Window(_targetWindow));
            _retryTimer.Stop();

            // 小窓を出している間だけ、数字キーで一覧から選び、モードキーで履歴と定型文を切り替えられるようにする。
            // 小窓が見えているのにキーが入力中のアプリに届いてしまう隙間が無いよう、小窓を出す前に登録する
            // (E2E で、表示の直後に押した数字が漏れたため)。
            // ホットキーを押したままなので、キーはホットキーと同じ修飾キー付きで届く。
            // モードキーがホットキーと同じキーだと、押したことが区別できないので登録しない
            Keys modeKey = HotkeyText.ConflictsWithHotkey(_settings.Hotkey, _settings.ModeKey) ? Keys.None : _settings.ModeKey;
            _popupKeys.Enable(_settings.Hotkey & Keys.Modifiers, modeKey);

            // 通知を取りこぼしていた場合の保険として、押した時点の内容も拾う
            ClipboardKind kind = CaptureClipboard(false);
            // 開くときはいつもクリップボード履歴から
            _mode = PopupMode.History;
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
                _inserter.Insert(text);
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
            _inserter.Insert(node.Text);
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
            if (_mode != PopupMode.Phrases || _phrasePath.Count == 0)
            {
                return;
            }
            _phrasePath.RemoveAt(_phrasePath.Count - 1);
            ShowMode();
        }

        /// <summary>モードキーで、クリップボード履歴と定型文を切り替える。小窓は出したままにする。</summary>
        private void OnModeKeyPressed()
        {
            if (RowMenu.IsOpen || _popup.IsDragging)
            {
                return;
            }
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
        /// 定型文モードに入る。いつも一番上の階層から。
        /// phrases.json を手で書き換えても再起動せずに反映されるよう、変わっていれば読み直す。
        /// </summary>
        private void EnterPhraseMode()
        {
            _mode = PopupMode.Phrases;
            _phrasePath.Clear();

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
                // 履歴はドラッグで並べ替えない
                _popup.AllowDrag = false;
                _popup.SetHeader(HistoryTitle, modeKeyName + ": " + PhraseTitle);
                _popup.SetItems(_history.Items, HistoryEmptyMessage);
                return;
            }

            _popupKeys.SetEscapeEnabled(true);
            _popup.AllowDrag = true;
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
                    rows.Add(new PopupRow(node.IsGroup ? PopupRowKind.Group : PopupRowKind.Item, node.Label));
                }
            }
            _popup.SetRows(rows, string.Empty);
            if (_dragGroup != null)
            {
                // ドラッグ中に別の階層を開いたときは、元の行はこの一覧に無い
                _popup.SetDragSource(CurrentPhraseGroup == _dragGroup ? _dragIndex : -1);
            }
        }

        // ---- 定型文のドラッグ＆ドロップ ---------------------------------------------------

        /// <summary>ドラッグを始めたとき。どの階層のどの枠かを覚えておく (別の階層を開いても追えるように)。</summary>
        private void OnRowDragStarted(int index)
        {
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
            if (_dragGroup == null || _mode != PopupMode.Phrases)
            {
                return;
            }
            PhraseNode dragged = _dragGroup.Slots[_dragIndex];
            if (target.Kind == DropKind.Into)
            {
                PhraseNode[] slots = CurrentPhraseGroup.Slots;
                if (target.Index < 0 || target.Index >= slots.Length || slots[target.Index] == null ||
                    !slots[target.Index].IsGroup || PhraseMoves.Contains(dragged, slots[target.Index]))
                {
                    return;
                }
                _phrasePath.Add(slots[target.Index]);
                ShowMode();
            }
            else if (target.Kind == DropKind.Level && target.Index < _phrasePath.Count)
            {
                _phrasePath.RemoveRange(target.Index, _phrasePath.Count - target.Index);
                ShowMode();
            }
        }

        /// <summary>ドラッグが終わったとき。落とした先に応じて入れ替える・グループに入れる・上の階層に出す。</summary>
        private void OnRowDragEnded(DropTarget target)
        {
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
            // 数字キーやモードキーを横取りしたままにすると、どのアプリでも打てなくなる。真っ先に解除する
            _popupKeys.Disable();
            // 離した後に遅れて表示が変わらないよう、先に読み直しを止める
            _retryTimer.Stop();
            // 右クリックのメニューも小窓と一緒に閉じる (項目を選ぶ前に離したとき)
            RowMenu.Close();
            _popup.HidePopup();
        }

        // ---- 定型文の右クリックのメニューと、登録・編集・削除 ------------------------------

        /// <summary>定型文モードで行が右クリックされたとき。枠の中身に応じたメニューを出す。</summary>
        private void OnRowContextRequested(int index, Point screen)
        {
            if (_mode != PopupMode.Phrases || _dialogOpen || RowMenu.IsOpen)
            {
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
                labels.Add("定型文を登録...");
                actions.Add(delegate { EditPhrase(group, index); });
                labels.Add("グループを作成...");
                actions.Add(delegate { EditGroup(group, index); });
            }
            else if (node.IsGroup)
            {
                labels.Add("名前を変更...");
                actions.Add(delegate { EditGroup(group, index); });
                labels.Add("削除");
                actions.Add(delegate { DeleteSlot(group, index); });
            }
            else
            {
                labels.Add("編集...");
                actions.Add(delegate { EditPhrase(group, index); });
                labels.Add("削除");
                actions.Add(delegate { DeleteSlot(group, index); });
            }

            int chosen = RowMenu.Show(_popup.Handle, screen, labels);
            if (chosen >= 0 && chosen < actions.Count)
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

        /// <summary>定型文の登録 (空きの枠) か編集 (定型文の枠)。</summary>
        private void EditPhrase(PhraseNode group, int index)
        {
            PhraseNode node = group.Slots[index];
            bool isNew = (node == null);
            RunPhraseDialog(delegate
            {
                using (PhraseDialog dialog = PhraseDialog.ForPhrase(
                    isNew ? "定型文を登録" : "定型文を編集", SlotLocation(index),
                    isNew ? string.Empty : node.Title, isNew ? string.Empty : node.Text))
                {
                    if (dialog.ShowDialog(_dialogOwner) != DialogResult.OK)
                    {
                        return;
                    }
                    if (isNew)
                    {
                        group.Slots[index] = PhraseNode.CreatePhrase(dialog.PhraseTitle, dialog.PhraseText);
                    }
                    else
                    {
                        node.Name = dialog.PhraseTitle;
                        node.Text = dialog.PhraseText;
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
                    isNew ? "グループを作成" : "グループの名前を変更", SlotLocation(index),
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

        /// <summary>枠を空きにする。取り消せないので確認する (グループは中の件数も示す)。</summary>
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
                    message = "グループ「" + name + "」を削除します。\n中の定型文 " + phrases + " 件とグループ " + groups +
                              " 件も削除されます。\n\nよろしいですか。";
                }
                else
                {
                    message = "定型文「" + name + "」を削除します。\n\nよろしいですか。";
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
                DebugTrace.Write("dialog start fg=" + DebugTrace.Window(NativeMethods.GetForegroundWindow()) + " target=" + DebugTrace.Window(_targetWindow));
                // 持ち主を先に出して前面にしておく。小窓を隠しても前面が他のアプリへ移らない
                _dialogOwner.Show();
                _dialogOwner.Activate();
                DebugTrace.Write("owner shown fg=" + DebugTrace.Window(NativeMethods.GetForegroundWindow()) + " owner=" + DebugTrace.Window(_dialogOwner.Handle));
                _popup.HidePopup();
                show();
                DebugTrace.Write("dialog closed fg=" + DebugTrace.Window(NativeMethods.GetForegroundWindow()));
            }
            finally
            {
                _dialogOpen = false;
                // 持ち主が前面のうちに元のアプリへ戻してから、持ち主を隠す
                RestoreTargetWindow();
                DebugTrace.Write("restored fg=" + DebugTrace.Window(NativeMethods.GetForegroundWindow()));
                _dialogOwner.Hide();
                DebugTrace.Write("owner hidden fg=" + DebugTrace.Window(NativeMethods.GetForegroundWindow()));
            }
        }

        /// <summary>
        /// ホットキーを押したときに前面だったアプリを、前面に戻す。
        /// 右クリックのメニューを出すと小窓が前面になり (Windows 標準のメニューの動き。実測)、
        /// ダイアログを閉じた後も前面が元のアプリに戻らないため。
        /// </summary>
        private void RestoreTargetWindow()
        {
            if (_targetWindow == IntPtr.Zero || !NativeMethods.IsWindow(_targetWindow) ||
                NativeMethods.GetForegroundWindow() == _targetWindow)
            {
                return;
            }
            bool setOk = NativeMethods.SetForegroundWindow(_targetWindow);
            DebugTrace.Write("SetForegroundWindow ok=" + setOk + " fg=" + DebugTrace.Window(NativeMethods.GetForegroundWindow()));

            // 前面の切り替えは、相手のアプリが応じてから終わる。終わる前にダイアログの持ち主を隠すと、
            // Windows が別のアプリ (実測では VS Code) を前面にしてしまうので、切り替わるまで少し待つ
            for (int i = 0; i < RestoreWaitSteps && NativeMethods.GetForegroundWindow() != _targetWindow; i++)
            {
                System.Threading.Thread.Sleep(RestoreWaitStepMs);
            }
        }

        /// <summary>ダイアログに出す、枠の場所 (例: 定型文 > 社外 の 3 番)。</summary>
        private string SlotLocation(int index)
        {
            return PhrasePathText() + " の " + ItemNumber.Label(index) + " 番";
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
                    "定型文を保存できませんでした。Copipe を終了すると、今の変更は消えます。\n\n" +
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
                _popup.HidePopup();

                // 登録できないキーが選ばれたときは、閉じずに設定画面へ戻る
                bool finished = false;
                while (!finished)
                {
                    Keys chosenHotkey;
                    Keys chosenModeKey;
                    int chosenCount;
                    InsertClick chosenClick;
                    using (SettingsDialog dialog = new SettingsDialog(
                        _settings.Hotkey, _settings.ModeKey, _settings.HistoryCount, Settings.MinHistoryCount, Settings.MaxHistoryCount,
                        _settings.InsertClick))
                    {
                        if (dialog.ShowDialog() != DialogResult.OK)
                        {
                            RestoreHotkey();
                            return;
                        }
                        chosenHotkey = dialog.SelectedHotkey;
                        chosenModeKey = dialog.SelectedModeKey;
                        chosenCount = dialog.SelectedHistoryCount;
                        chosenClick = dialog.SelectedInsertClick;
                    }

                    if (chosenHotkey == _settings.Hotkey || _hotkey.TryRegister(chosenHotkey))
                    {
                        if (chosenHotkey == _settings.Hotkey)
                        {
                            RestoreHotkey();
                        }
                        ApplySettings(chosenHotkey, chosenModeKey, chosenCount, chosenClick);
                        finished = true;
                    }
                    else
                    {
                        MessageBox.Show(
                            HotkeyText.Display(chosenHotkey) + " は、他のアプリまたは Windows が使用中のため設定できませんでした。\n\n" +
                            "別のキーを選んでください。",
                            "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            finally
            {
                _dialogOpen = false;
            }
        }

        private void ApplySettings(Keys hotkey, Keys modeKey, int historyCount, InsertClick insertClick)
        {
            bool changed = (hotkey != _settings.Hotkey) || (modeKey != _settings.ModeKey) ||
                           (historyCount != _settings.HistoryCount) ||
                           (insertClick != _settings.InsertClick);
            if (!changed)
            {
                return;
            }

            _settings.Hotkey = hotkey;
            _settings.ModeKey = modeKey;
            _settings.HistoryCount = historyCount;
            _settings.InsertClick = insertClick;
            _popup.InsertOnSingleClick = (insertClick == InsertClick.Single);
            _history.Capacity = historyCount;   // 減らした分は古いものから捨てられる
            SaveHistory();
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
                "ホットキー " + HotkeyName + " を登録できませんでした。\n\n" +
                "他のアプリまたは Windows が使用中です。Copipe をいったん終了して、もう一度お試しください。",
                "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnClearHistoryClick(object sender, EventArgs e)
        {
            if (MessageBox.Show(
                    "履歴をすべて消去します。よろしいですか。",
                    "Copipe", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _history.Clear();
            SaveHistory();
            if (_popup.Visible && _mode == PopupMode.History)
            {
                _popup.SetItems(_history.Items, HistoryEmptyMessage);
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
                    "設定を保存できませんでした。次に起動したときは元の設定に戻ります。\n\n" +
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
                    "履歴を保存できませんでした。Copipe を終了すると履歴は消えます。\n\n" +
                    ClipboardHistory.DefaultPath + "\n\n" + ex.Message,
                    "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateLabels()
        {
            string name = HotkeyName;
            _settingsItem.Text = "設定... (ホットキー: " + name + ")";

            string tip = "Copipe（" + name + " を押している間、履歴を表示）";
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
                _trayMenu.Dispose();
                _dialogOwner.Dispose();
                _popup.FormClosed -= OnPopupClosed;
                _popup.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
