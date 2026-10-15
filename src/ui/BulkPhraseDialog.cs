using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Copipe.Model;

namespace Copipe.UI
{
    /// <summary>
    /// 定型文の一括登録・一括編集。小窓と同じ「グループ → 10 個の枠」の形で、まとめて見て直せる。
    /// 左にグループの木、真ん中に選んだグループの 1〜9、0 の枠、右に選んだ枠の中身 (種類と、その種類の欄だけ)。
    /// Excel などからコピーした行を、今のグループの空きの枠にまとめて入れられる。
    /// 定型文の複製を編集し、OK で確かめてから呼び出し側が元に戻す (ApplyTo)。キャンセルなら何も変わらない。
    /// 部品の Name (groupTree・slotList・textKind・pathKind・groupKind・nameBox・textBox・pathBox・okButton・cancelButton・status)
    /// は、どの部品かを示す目印。
    /// </summary>
    internal sealed class BulkPhraseDialog : Form
    {
        /// <summary>枠の中身の種類。</summary>
        private enum SlotKind
        {
            Text,
            Path,
            Group
        }

        private static readonly Color ErrorColor = Color.FromArgb(0xC0, 0x30, 0x00);

        private readonly PhraseNode _root;
        private readonly Dictionary<PhraseNode, PhraseNode> _map = new Dictionary<PhraseNode, PhraseNode>();
        // 「開く」種類の定型文 (それ以外の定型文は「文字を入力する」種類)。パスが空でも種類を覚えておくため
        private readonly HashSet<PhraseNode> _pathKind = new HashSet<PhraseNode>();

        private readonly TreeView _tree;
        private readonly ListBox _slotList;
        private readonly Label _slotHeader;
        private readonly RadioButton _textKind;
        private readonly RadioButton _pathKindButton;
        private readonly RadioButton _groupKind;
        private readonly Label _nameLabel;
        private readonly TextBox _nameBox;
        private readonly Label _textLabel;
        private readonly TextBox _textBox;
        private readonly Label _pathLabel;
        private readonly TextBox _pathBox;
        private readonly Button _fileButton;
        private readonly Button _folderButton;
        private readonly Label _groupNote;
        private readonly Button _openGroupButton;
        private readonly Button _clearButton;
        private readonly Label _status;
        // 見出しの太字 (1 つを使い回し、閉じるときに片づける)
        private readonly Font _boldFont;
        // 右側の見出し (幅を右側に合わせる)
        private readonly List<Label> _fieldLabels = new List<Label>();

        private PhraseNode _group;
        private int _slot;
        // 空きの枠で選んでいる種類 (書き始めると、この種類で作る)
        private SlotKind _emptyKind = SlotKind.Text;
        // 画面に値を入れている間は、欄の変更を定型文に書かない
        private bool _loading;
        private bool _dirty;

        public BulkPhraseDialog(PhraseNode root)
        {
            _root = PhraseEditing.Clone(root, _map);
            foreach (PhraseNode original in _map.Keys)
            {
                if (!original.IsGroup && original.Path.Trim().Length > 0)
                {
                    _pathKind.Add(_map[original]);
                }
            }

            Text = Lang.T("定型文をまとめて編集", "Edit snippets in bulk");
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            Font = SystemFonts.MessageBoxFont;
            _boldFont = new Font(Font, FontStyle.Bold);
            BackColor = SystemColors.Window;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Scaled(960), Scaled(560));
            MinimumSize = new Size(Scaled(820), Scaled(460));

            Label guide = new Label();
            guide.Dock = DockStyle.Top;
            guide.UseMnemonic = false;
            guide.ForeColor = SystemColors.GrayText;
            guide.Padding = new Padding(Scaled(10), Scaled(10), Scaled(10), Scaled(6));
            guide.Text = Lang.T("① 左でグループを選び、② 真ん中で番号の枠を選んで、③ 右で中身を書きます。OK を押すと保存します。",
                                "1. Choose a group on the left.  2. Choose a slot in the middle.  3. Fill it in on the right.  OK saves.");
            guide.Height = TextRenderer.MeasureText(guide.Text, Font).Height + guide.Padding.Vertical;

            // ---- 左: グループの木 ----
            Panel left = new Panel();
            left.Dock = DockStyle.Left;
            left.Width = Scaled(210);
            left.Padding = new Padding(Scaled(10), 0, Scaled(4), Scaled(4));
            _tree = new TreeView();
            _tree.Name = "groupTree";
            _tree.Dock = DockStyle.Fill;
            _tree.HideSelection = false;
            _tree.FullRowSelect = true;
            _tree.ShowLines = true;
            _tree.ItemHeight = Scaled(24);
            _tree.AfterSelect += delegate(object sender, TreeViewEventArgs e)
            {
                PhraseNode group = e.Node == null ? null : e.Node.Tag as PhraseNode;
                if (group != null && group != _group)
                {
                    ShowGroup(group, 0);
                }
            };
            left.Controls.Add(_tree);
            left.Controls.Add(Caption(Lang.T("グループ", "Groups")));

            // ---- 真ん中: 枠の一覧 ----
            Panel middle = new Panel();
            middle.Dock = DockStyle.Left;
            middle.Width = Scaled(330);
            middle.Padding = new Padding(Scaled(4), 0, Scaled(4), Scaled(4));
            _slotList = new ListBox();
            _slotList.Name = "slotList";
            _slotList.Dock = DockStyle.Fill;
            _slotList.DrawMode = DrawMode.OwnerDrawFixed;
            _slotList.ItemHeight = Scaled(30);
            _slotList.IntegralHeight = false;
            for (int i = 0; i < PhraseNode.SlotCount; i++)
            {
                _slotList.Items.Add(i);
            }
            _slotList.DrawItem += OnDrawSlot;
            _slotList.SelectedIndexChanged += delegate
            {
                if (_slotList.SelectedIndex >= 0 && _slotList.SelectedIndex != _slot)
                {
                    _slot = _slotList.SelectedIndex;
                    ShowSlot();
                }
            };
            _slotList.DoubleClick += delegate { OpenSelectedGroup(); };
            _slotList.KeyDown += OnSlotListKeyDown;
            Panel moveBar = new Panel();
            moveBar.Dock = DockStyle.Bottom;
            moveBar.Height = Scaled(38);
            Button up = SmallButton(Lang.T("↑ 上へ", "↑ Up"), 0);
            up.Name = "moveUpButton";
            up.Click += delegate { MoveSlot(-1); };
            Button down = SmallButton(Lang.T("↓ 下へ", "↓ Down"), Scaled(92));
            down.Name = "moveDownButton";
            down.Click += delegate { MoveSlot(1); };
            moveBar.Controls.Add(up);
            moveBar.Controls.Add(down);
            middle.Controls.Add(_slotList);
            middle.Controls.Add(moveBar);
            middle.Controls.Add(Caption(Lang.T("番号の枠 (小窓と同じ並び)", "Slots (same order as the popup)")));

            // ---- 右: 枠の中身 ----
            Panel editor = new Panel();
            editor.Dock = DockStyle.Fill;
            int width = Scaled(380);
            int y = 0;
            _slotHeader = new Label();
            _slotHeader.UseMnemonic = false;
            _slotHeader.AutoSize = false;
            _slotHeader.Font = _boldFont;
            _slotHeader.Bounds = new Rectangle(Scaled(8), y, width, Scaled(26));
            _slotHeader.TextAlign = ContentAlignment.MiddleLeft;
            editor.Controls.Add(_slotHeader);
            y += Scaled(32);

            editor.Controls.Add(FieldLabel(Lang.T("種類", "Type"), y, width));
            y += Scaled(22);
            _textKind = KindButton("textKind", Lang.T("文字を入力する", "Type text"), y);
            _pathKindButton = KindButton("pathKind", Lang.T("ファイル・フォルダー・URL を開く", "Open a file, folder or URL"), y + Scaled(24));
            _groupKind = KindButton("groupKind", Lang.T("グループ (中に 10 個の枠を持つ)", "Group (holds 10 more slots)"), y + Scaled(48));
            editor.Controls.Add(_textKind);
            editor.Controls.Add(_pathKindButton);
            editor.Controls.Add(_groupKind);
            y += Scaled(80);

            _nameLabel = FieldLabel(string.Empty, y, width);
            editor.Controls.Add(_nameLabel);
            y += Scaled(22);
            _nameBox = new TextBox();
            _nameBox.Name = "nameBox";
            _nameBox.Bounds = new Rectangle(Scaled(8), y, width, Scaled(24));
            _nameBox.TextChanged += delegate { OnFieldChanged(); };
            editor.Controls.Add(_nameBox);
            y += Scaled(36);

            // 種類ごとの欄は同じ場所に重ね、選んだ種類のものだけを見せる
            int kindTop = y;
            _textLabel = FieldLabel(Lang.T("入力する文字 (Enter で改行)", "Text to type (Enter adds a line break)"), kindTop, width);
            editor.Controls.Add(_textLabel);
            _textBox = new IndentPasteTextBox();
            _textBox.Name = "textBox";
            _textBox.Multiline = true;
            _textBox.AcceptsReturn = true;
            _textBox.ScrollBars = ScrollBars.Vertical;
            _textBox.MaxLength = Copipe.Services.ClipboardHistory.MaxTextLength;
            _textBox.Bounds = new Rectangle(Scaled(8), kindTop + Scaled(22), width, Scaled(120));
            _textBox.TextChanged += delegate { OnFieldChanged(); };
            editor.Controls.Add(_textBox);

            _pathLabel = FieldLabel(Lang.T("開くファイル・フォルダー・URL", "File, folder or URL to open"), kindTop, width);
            editor.Controls.Add(_pathLabel);
            _pathBox = new TextBox();
            _pathBox.Name = "pathBox";
            _pathBox.Bounds = new Rectangle(Scaled(8), kindTop + Scaled(22), width, Scaled(24));
            _pathBox.TextChanged += delegate { OnFieldChanged(); };
            editor.Controls.Add(_pathBox);
            _fileButton = new Button();
            _fileButton.Text = Lang.T("ファイルを選ぶ...", "Choose file...");
            _fileButton.Bounds = new Rectangle(Scaled(8), kindTop + Scaled(54), Scaled(140), Scaled(30));
            _fileButton.Click += delegate { BrowseFile(); };
            editor.Controls.Add(_fileButton);
            _folderButton = new Button();
            _folderButton.Text = Lang.T("フォルダーを選ぶ...", "Choose folder...");
            _folderButton.Bounds = new Rectangle(Scaled(156), kindTop + Scaled(54), Scaled(150), Scaled(30));
            _folderButton.Click += delegate { BrowseFolder(); };
            editor.Controls.Add(_folderButton);

            _groupNote = new Label();
            _groupNote.UseMnemonic = false;
            _groupNote.AutoSize = false;
            _groupNote.ForeColor = SystemColors.GrayText;
            _groupNote.Text = Lang.T("グループの中身は、左の一覧でこのグループを選ぶと編集できます。",
                                     "To edit what is inside, choose this group on the left.");
            _groupNote.Bounds = new Rectangle(Scaled(8), kindTop, width, Scaled(40));
            editor.Controls.Add(_groupNote);
            _openGroupButton = new Button();
            _openGroupButton.Text = Lang.T("このグループの中を開く", "Open this group");
            _openGroupButton.Bounds = new Rectangle(Scaled(8), kindTop + Scaled(44), Scaled(200), Scaled(30));
            _openGroupButton.Click += delegate { OpenSelectedGroup(); };
            editor.Controls.Add(_openGroupButton);

            _clearButton = new Button();
            _clearButton.Name = "clearButton";
            _clearButton.Text = Lang.T("この枠を空にする", "Clear this slot");
            _clearButton.Click += delegate { ClearSlot(); };
            editor.Controls.Add(_clearButton);

            // 本文の欄と「空にする」ボタンは、右側の大きさが決まってから合わせる
            editor.Resize += delegate { LayoutEditor(editor, kindTop); };

            // ---- 下: 貼り付け・保存 ----
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Size = new Size(ClientSize.Width, Scaled(52));
            Button paste = new Button();
            paste.Name = "pasteButton";
            paste.Text = Lang.T("Excel などから貼り付け", "Paste from Excel etc.");
            paste.Bounds = new Rectangle(Scaled(10), Scaled(10), Scaled(190), Scaled(32));
            paste.Click += delegate { PasteRows(); };
            bottom.Controls.Add(paste);
            ToolTip tip = new ToolTip();
            tip.SetToolTip(paste, Lang.T(
                "コピーした行を、今のグループの空いている枠に前から入れます。\n" +
                "1 列なら中身だけ、2 列なら「名前・中身」として読みます。URL やパスは「開く」種類になります。",
                "Puts copied rows into the free slots of the current group.\n" +
                "One column: the contents. Two columns: name, contents. URLs and paths become \"open\" snippets."));

            _status = new Label();
            _status.Name = "status";
            _status.UseMnemonic = false;
            _status.AutoSize = false;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Bounds = new Rectangle(Scaled(208), Scaled(4), ClientSize.Width - Scaled(208) - Scaled(200), Scaled(44));
            _status.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            bottom.Controls.Add(_status);

            Button ok = new Button();
            ok.Name = "okButton";
            ok.Text = "OK";
            ok.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            ok.Bounds = new Rectangle(ClientSize.Width - Scaled(192), Scaled(10), Scaled(86), Scaled(32));
            ok.Click += delegate { TrySave(); };
            bottom.Controls.Add(ok);
            Button cancel = new Button();
            cancel.Name = "cancelButton";
            cancel.Text = Lang.T("キャンセル", "Cancel");
            cancel.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            cancel.Bounds = new Rectangle(ClientSize.Width - Scaled(98), Scaled(10), Scaled(86), Scaled(32));
            cancel.DialogResult = DialogResult.Cancel;
            bottom.Controls.Add(cancel);

            // Dock は後から追加したものから順に場所を取る。上・下・左・真ん中を先に、右 (Fill) を最後にする
            Controls.Add(editor);
            Controls.Add(middle);
            Controls.Add(left);
            Controls.Add(bottom);
            Controls.Add(guide);

            BuildTree();
            ShowGroup(_root, 0);
            LayoutEditor(editor, kindTop);
        }

        /// <summary>
        /// OK で閉じたとき、編集した定型文を元の木 (root) に戻す。一番上の枠を入れ替えるので、
        /// グループは新しいものになる (開いていた階層は Map で対応させる)。
        /// </summary>
        public void ApplyTo(PhraseNode root)
        {
            for (int i = 0; i < root.Slots.Length; i++)
            {
                root.Slots[i] = _root.Slots[i];
            }
        }

        /// <summary>元の定型文・グループに当たる、編集後のもの (種類を変えたものは null。消したものは木に無い)。</summary>
        public PhraseNode Map(PhraseNode original)
        {
            PhraseNode node;
            return _map.TryGetValue(original, out node) ? node : null;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            _slotList.Focus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            // 直したのに、キャンセル・× で閉じようとしたときは確かめる
            if (DialogResult != DialogResult.OK && _dirty && !e.Cancel &&
                MessageBox.Show(this, Lang.T("変更を保存せずに閉じます。よろしいですか。", "Close without saving your changes?"),
                                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                e.Cancel = true;
                DialogResult = DialogResult.None;
            }
        }

        // ---- 部品を作る小さな手伝い ----------------------------------------------------

        private Label Caption(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.UseMnemonic = false;
            label.Dock = DockStyle.Top;
            label.Font = _boldFont;
            label.Height = Scaled(26);
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private Label FieldLabel(string text, int y, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.UseMnemonic = false;
            label.AutoSize = false;
            label.Bounds = new Rectangle(Scaled(8), y, width, Scaled(20));
            _fieldLabels.Add(label);
            return label;
        }

        private RadioButton KindButton(string name, string text, int y)
        {
            RadioButton button = new RadioButton();
            button.Name = name;
            button.Text = text;
            button.AutoSize = true;
            button.Location = new Point(Scaled(14), y);
            button.CheckedChanged += delegate
            {
                if (button.Checked && !_loading)
                {
                    ChangeKind(button == _pathKindButton ? SlotKind.Path : button == _groupKind ? SlotKind.Group : SlotKind.Text);
                }
            };
            return button;
        }

        private Button SmallButton(string text, int x)
        {
            Button button = new Button();
            button.Text = text;
            button.Bounds = new Rectangle(x, Scaled(4), Scaled(86), Scaled(30));
            return button;
        }

        /// <summary>
        /// 右側の欄の幅を右側いっぱいにし、本文の欄を下まで広げて、「空にする」ボタンを下に置く。
        /// (Anchor で右へ伸ばすと、右側の大きさが決まる前の大きさを基準にして、右端からはみ出した)
        /// </summary>
        private void LayoutEditor(Panel editor, int kindTop)
        {
            int width = Math.Max(Scaled(200), editor.ClientSize.Width - Scaled(8) - Scaled(14));
            foreach (Control control in new Control[] { _slotHeader, _nameBox, _pathBox, _groupNote })
            {
                control.Width = width;
            }
            foreach (Label label in _fieldLabels)
            {
                label.Width = width;
            }
            int clearTop = Math.Max(kindTop + Scaled(120), editor.ClientSize.Height - Scaled(40));
            _clearButton.Bounds = new Rectangle(Scaled(8), clearTop, Scaled(170), Scaled(32));
            _textBox.Bounds = new Rectangle(Scaled(8), kindTop + Scaled(22), width, clearTop - Scaled(10) - (kindTop + Scaled(22)));
        }

        // ---- 表示 ----------------------------------------------------------------------

        private string GroupName(PhraseNode group)
        {
            if (group == _root)
            {
                return Lang.T("定型文 (一番上)", "Snippets (top level)");
            }
            return group.Name.Trim().Length > 0 ? group.Name : Lang.T("(名前なし)", "(no name)");
        }

        /// <summary>グループの木を作り直し、今のグループを選ぶ。</summary>
        private void BuildTree()
        {
            _tree.BeginUpdate();
            try
            {
                _tree.Nodes.Clear();
                TreeNode top = new TreeNode(GroupName(_root));
                top.Tag = _root;
                AddTreeChildren(top, _root);
                _tree.Nodes.Add(top);
                _tree.ExpandAll();
            }
            finally
            {
                _tree.EndUpdate();
            }
            SelectTreeNode(_group ?? _root);
        }

        private void AddTreeChildren(TreeNode parent, PhraseNode group)
        {
            foreach (PhraseNode child in group.Slots)
            {
                if (child != null && child.IsGroup)
                {
                    TreeNode node = new TreeNode(GroupName(child));
                    node.Tag = child;
                    AddTreeChildren(node, child);
                    parent.Nodes.Add(node);
                }
            }
        }

        private void SelectTreeNode(PhraseNode group)
        {
            TreeNode found = FindTreeNode(_tree.Nodes, group);
            if (found != null && _tree.SelectedNode != found)
            {
                _tree.SelectedNode = found;
            }
        }

        private static TreeNode FindTreeNode(TreeNodeCollection nodes, PhraseNode group)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag == group)
                {
                    return node;
                }
                TreeNode child = FindTreeNode(node.Nodes, group);
                if (child != null)
                {
                    return child;
                }
            }
            return null;
        }

        /// <summary>グループの 10 個の枠を真ん中に出し、slot 番目を選ぶ。</summary>
        private void ShowGroup(PhraseNode group, int slot)
        {
            _group = group;
            SelectTreeNode(group);
            _slot = slot;
            _slotList.Invalidate();
            _slotList.SelectedIndex = slot;
            ShowSlot();
        }

        private PhraseNode Current
        {
            get { return _group.Slots[_slot]; }
        }

        private SlotKind KindOf(PhraseNode node)
        {
            if (node == null)
            {
                return _emptyKind;
            }
            return node.IsGroup ? SlotKind.Group : _pathKind.Contains(node) ? SlotKind.Path : SlotKind.Text;
        }

        private string SlotHeaderText(PhraseNode node)
        {
            return Lang.T(GroupName(_group) + " の " + ItemNumber.Label(_slot) + " 番",
                          GroupName(_group) + ", slot " + ItemNumber.Label(_slot)) +
                   (IsBlank(node) ? Lang.T("  (空き。書き始めると登録されます)", "  (empty; start typing to add)") : string.Empty);
        }

        /// <summary>選んでいる枠の中身を右に出す。</summary>
        private void ShowSlot()
        {
            _loading = true;
            try
            {
                PhraseNode node = Current;
                SlotKind kind = KindOf(node);
                _slotHeader.Text = SlotHeaderText(node);
                _textKind.Checked = kind == SlotKind.Text;
                _pathKindButton.Checked = kind == SlotKind.Path;
                _groupKind.Checked = kind == SlotKind.Group;
                _nameBox.Text = node == null ? string.Empty : node.Name;
                _textBox.Text = node == null || node.IsGroup ? string.Empty : node.Text;
                _pathBox.Text = node == null || node.IsGroup ? string.Empty : node.Path;
                ShowKindFields(kind);
                _clearButton.Enabled = node != null;
            }
            finally
            {
                _loading = false;
            }
        }

        private void ShowKindFields(SlotKind kind)
        {
            _nameLabel.Text = kind == SlotKind.Group
                ? Lang.T("グループ名", "Group name")
                : Lang.T("表示名 (一覧に出る名前。名前で探すときにも使います)", "Display name (shown in the list; used to find it by name)");
            _textLabel.Visible = _textBox.Visible = kind == SlotKind.Text;
            _pathLabel.Visible = _pathBox.Visible = _fileButton.Visible = _folderButton.Visible = kind == SlotKind.Path;
            _groupNote.Visible = _openGroupButton.Visible = kind == SlotKind.Group;
            _openGroupButton.Enabled = Current != null && Current.IsGroup;
        }

        /// <summary>何も書いていない定型文 (書き始めて消したものなど) は、空きとして扱う。</summary>
        private static bool IsBlank(PhraseNode node)
        {
            return node == null ||
                   (!node.IsGroup && node.Name.Trim().Length == 0 && node.Text.Trim().Length == 0 && node.Path.Trim().Length == 0);
        }

        private void OnDrawSlot(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || _group == null)
            {
                return;
            }
            e.DrawBackground();
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color main = selected ? SystemColors.HighlightText : SystemColors.ControlText;
            Color sub = selected ? SystemColors.HighlightText : SystemColors.GrayText;
            const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                                          TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            Rectangle bounds = e.Bounds;
            int numberWidth = Scaled(28);
            TextRenderer.DrawText(e.Graphics, ItemNumber.Label(e.Index) + ".", e.Font,
                                  new Rectangle(bounds.Left + Scaled(6), bounds.Top, numberWidth, bounds.Height), main, flags);
            Rectangle rest = Rectangle.FromLTRB(bounds.Left + Scaled(6) + numberWidth, bounds.Top, bounds.Right - Scaled(4), bounds.Bottom);

            PhraseNode node = _group.Slots[e.Index];
            string name;
            string detail;
            if (IsBlank(node))
            {
                name = string.Empty;
                detail = Lang.T("(空き)", "(empty)");
            }
            else if (node.IsGroup)
            {
                int phrases = 0;
                int groups = 0;
                PhraseEditing.CountContents(node, ref phrases, ref groups);
                name = PopupRow.GroupLabel(GroupName(node));
                detail = Lang.T("定型文 " + phrases + "・グループ " + groups, phrases + " snippet(s), " + groups + " group(s)");
            }
            else if (_pathKind.Contains(node))
            {
                name = (node.Path.Trim().Length > 0 ? PopupRow.MarkForPath(node.Path.Trim()) : PopupRow.FileMark) + ShortLabel(node);
                detail = node.Path;
            }
            else
            {
                name = ShortLabel(node);
                detail = node.Name.Trim().Length > 0 ? PreviewText.Line(node.Text, 80) : string.Empty;
            }
            // 名前の幅は省略記号なしで測る (省略記号付きで測ると狭く出て、短い名前まで「ad...」になった)
            int nameWidth = name.Length == 0 ? 0 : Math.Min(rest.Width * 3 / 5,
                TextRenderer.MeasureText(e.Graphics, name, e.Font, new Size(int.MaxValue, bounds.Height),
                                         TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + Scaled(10));
            TextRenderer.DrawText(e.Graphics, name, e.Font, new Rectangle(rest.Left, rest.Top, nameWidth, rest.Height), main, flags);
            TextRenderer.DrawText(e.Graphics, detail, e.Font,
                                  Rectangle.FromLTRB(rest.Left + nameWidth, rest.Top, rest.Right, rest.Bottom), sub, flags);
            e.DrawFocusRectangle();
        }

        /// <summary>一覧に出す名前 (表示名、無ければ本文の最初の行かファイル名)。</summary>
        private static string ShortLabel(PhraseNode node)
        {
            return PreviewText.Line(node.Label, 80);
        }

        // ---- 編集 ----------------------------------------------------------------------

        /// <summary>欄が書き換えられたとき。空きの枠なら、書き始めた時点で選んでいる種類の定型文を作る。</summary>
        private void OnFieldChanged()
        {
            if (_loading)
            {
                return;
            }
            _dirty = true;
            PhraseNode node = Current;
            bool wasBlank = IsBlank(node);
            if (node == null)
            {
                node = PhraseNode.CreatePhrase(string.Empty, string.Empty, string.Empty);
                if (_emptyKind == SlotKind.Path)
                {
                    _pathKind.Add(node);
                }
                _group.Slots[_slot] = node;
                _clearButton.Enabled = true;
            }
            node.Name = _nameBox.Text;
            if (!node.IsGroup)
            {
                node.Text = _textBox.Text;
                node.Path = _pathBox.Text;
            }
            else
            {
                // グループ名は木にも出す
                TreeNode tree = FindTreeNode(_tree.Nodes, node);
                if (tree != null)
                {
                    tree.Text = GroupName(node);
                }
            }
            if (wasBlank != IsBlank(node))
            {
                _slotHeader.Text = SlotHeaderText(node);
            }
            _slotList.Invalidate(_slotList.GetItemRectangle(_slot));
            _status.Text = string.Empty;
        }

        /// <summary>
        /// 種類を変える。文字 ⇔ 開く は、書いた内容を残したまま切り替える (保存するのは選んだ種類の欄だけ)。
        /// グループにするときは、本文・パスが消えるので確かめる。グループから戻すのは、中が空のときだけ。
        /// </summary>
        private void ChangeKind(SlotKind kind)
        {
            PhraseNode node = Current;
            SlotKind old = KindOf(node);
            if (kind == old)
            {
                return;
            }
            if (node == null && kind != SlotKind.Group)
            {
                _emptyKind = kind;
                ShowKindFields(kind);
                return;
            }
            if (kind == SlotKind.Group)
            {
                if (node != null && (node.Text.Trim().Length > 0 || node.Path.Trim().Length > 0) &&
                    MessageBox.Show(this, Lang.T("グループにすると、入力する文字・開くものは消えます。よろしいですか。",
                                                 "Making this a group removes its text or file. Continue?"),
                                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    ShowSlot();
                    return;
                }
                _group.Slots[_slot] = PhraseNode.CreateGroup(node == null ? string.Empty : node.Name);
                _dirty = true;
                BuildTree();
                ShowSlot();
                _slotList.Invalidate();
                _nameBox.Focus();
                return;
            }
            if (old == SlotKind.Group)
            {
                if (PhraseEditing.HasContents(node))
                {
                    MessageBox.Show(this, Lang.T("中に定型文やグループがあるグループは、種類を変えられません。先に中を空にしてください。",
                                                 "A group that has snippets or groups inside cannot change its type. Empty it first."),
                                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    ShowSlot();
                    return;
                }
                PhraseNode phrase = PhraseNode.CreatePhrase(node.Name, string.Empty, string.Empty);
                _group.Slots[_slot] = phrase;
                node = phrase;
                BuildTree();
            }
            if (kind == SlotKind.Path)
            {
                _pathKind.Add(node);
            }
            else
            {
                _pathKind.Remove(node);
            }
            _dirty = true;
            ShowSlot();
            _slotList.Invalidate();
        }

        /// <summary>選んでいる枠を空にする。中身のあるグループは、件数を出して確かめる。</summary>
        private void ClearSlot()
        {
            PhraseNode node = Current;
            if (node == null)
            {
                return;
            }
            if (node.IsGroup && PhraseEditing.HasContents(node))
            {
                int phrases = 0;
                int groups = 0;
                PhraseEditing.CountContents(node, ref phrases, ref groups);
                if (MessageBox.Show(this, Lang.T("グループ「" + GroupName(node) + "」を消します。\n中の定型文 " + phrases + " 件とグループ " + groups +
                                                 " 件も消えます。よろしいですか。",
                                                 "Delete the group \"" + GroupName(node) + "\"?\nThe " + phrases + " snippet(s) and " + groups +
                                                 " group(s) inside it will also be deleted."),
                                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    return;
                }
            }
            _group.Slots[_slot] = null;
            _pathKind.Remove(node);
            _dirty = true;
            if (node.IsGroup)
            {
                BuildTree();
            }
            ShowSlot();
            _slotList.Invalidate();
        }

        /// <summary>選んでいる枠を、上 (-1) か下 (+1) の枠と入れ替える。</summary>
        private void MoveSlot(int step)
        {
            int to = _slot + step;
            if (to < 0 || to >= PhraseNode.SlotCount)
            {
                return;
            }
            PhraseNode moving = _group.Slots[_slot];
            PhraseNode other = _group.Slots[to];
            _group.Slots[_slot] = other;
            _group.Slots[to] = moving;
            _dirty = true;
            if ((moving != null && moving.IsGroup) || (other != null && other.IsGroup))
            {
                // 木の並びも枠の順にする
                BuildTree();
            }
            _slotList.Invalidate();
            _slotList.SelectedIndex = to;
        }

        private void OpenSelectedGroup()
        {
            PhraseNode node = Current;
            if (node != null && node.IsGroup)
            {
                ShowGroup(node, 0);
                _slotList.Focus();
            }
        }

        private void OnSlotListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                ClearSlot();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                if (Current != null && Current.IsGroup)
                {
                    OpenSelectedGroup();
                }
                else
                {
                    // 中身を書き始められるよう、名前の欄へ
                    _nameBox.Focus();
                }
                e.Handled = true;
            }
        }

        private void BrowseFile()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.CheckFileExists = true;
                dialog.Title = Lang.T("開くファイルを選ぶ", "Choose a file to open");
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
                dialog.Description = Lang.T("開くフォルダーを選ぶ", "Choose a folder to open");
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _pathBox.Text = dialog.SelectedPath;
                }
            }
        }

        /// <summary>クリップボードの行 (Excel でコピーした範囲など) を、今のグループの空いている枠に入れる。</summary>
        private void PasteRows()
        {
            string text = null;
            try
            {
                text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // ほかのアプリがクリップボードを開いている
            }
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
            {
                ShowStatus(Lang.T("クリップボードに文字がありません。Excel などで行をコピーしてから押してください。",
                                  "The clipboard has no text. Copy some rows in Excel or elsewhere first."), true);
                return;
            }
            // 書き始めて消した定型文の枠も、空きとして使う
            for (int i = 0; i < _group.Slots.Length; i++)
            {
                if (_group.Slots[i] != null && IsBlank(_group.Slots[i]))
                {
                    _pathKind.Remove(_group.Slots[i]);
                    _group.Slots[i] = null;
                }
            }
            int skipped;
            List<int> placed = PhraseEditing.PasteInto(_group, PhraseEditing.ParseTabSeparated(text), out skipped);
            foreach (int slot in placed)
            {
                PhraseNode node = _group.Slots[slot];
                if (node.Path.Length > 0)
                {
                    _pathKind.Add(node);
                }
            }
            if (placed.Count > 0)
            {
                _dirty = true;
                ShowGroup(_group, placed[0]);
            }
            _slotList.Invalidate();
            string message = Lang.T(placed.Count + " 件を「" + GroupName(_group) + "」の空いている枠に入れました。",
                                    placed.Count + " item(s) added to free slots in \"" + GroupName(_group) + "\".");
            if (skipped > 0)
            {
                message += Lang.T(" 空きが足りず、" + skipped + " 件は入りませんでした。", " " + skipped + " did not fit (no free slots).");
            }
            ShowStatus(message, skipped > 0 || placed.Count == 0);
        }

        private void ShowStatus(string text, bool error)
        {
            _status.Text = text;
            _status.ForeColor = error ? ErrorColor : SystemColors.HotTrack;
        }

        // ---- 保存 ----------------------------------------------------------------------

        /// <summary>
        /// 確かめて閉じる。何も書いていない定型文と、名前も中身も無いグループは空きにする。
        /// 表示名だけで中身の無い定型文と、中身があるのに名前の無いグループがあれば、その枠を選んで知らせ、閉じない。
        /// 定型文は選んだ種類の欄だけを残す (文字なら パスを、開くなら 文字を 空にする)。
        /// </summary>
        private void TrySave()
        {
            PhraseNode problemGroup;
            int problemSlot;
            string problem = Finish(_root, out problemGroup, out problemSlot);
            if (problem != null)
            {
                BuildTree();
                ShowGroup(problemGroup, problemSlot);
                ShowStatus(Lang.T(GroupName(problemGroup) + " の " + ItemNumber.Label(problemSlot) + " 番: ",
                                  GroupName(problemGroup) + ", slot " + ItemNumber.Label(problemSlot) + ": ") + problem, true);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        private string Finish(PhraseNode group, out PhraseNode problemGroup, out int problemSlot)
        {
            problemGroup = null;
            problemSlot = -1;
            for (int i = 0; i < group.Slots.Length; i++)
            {
                PhraseNode node = group.Slots[i];
                if (node == null)
                {
                    continue;
                }
                if (node.IsGroup)
                {
                    string inner = Finish(node, out problemGroup, out problemSlot);
                    if (inner != null)
                    {
                        return inner;
                    }
                    if (node.Name.Trim().Length == 0)
                    {
                        if (!PhraseEditing.HasContents(node))
                        {
                            group.Slots[i] = null;
                            continue;
                        }
                        problemGroup = group;
                        problemSlot = i;
                        return Lang.T("グループ名を入れてください。", "Enter a group name.");
                    }
                    node.Name = node.Name.Trim();
                    continue;
                }
                bool isPath = _pathKind.Contains(node);
                string content = isPath ? node.Path : node.Text;
                if (content.Trim().Length == 0)
                {
                    if (node.Name.Trim().Length == 0)
                    {
                        group.Slots[i] = null;
                        continue;
                    }
                    problemGroup = group;
                    problemSlot = i;
                    return isPath ? Lang.T("開くファイル・フォルダー・URL を入れてください。", "Enter a file, folder or URL to open.")
                                  : Lang.T("入力する文字を入れてください。", "Enter the text to type.");
                }
                node.Name = node.Name.Trim();
                if (isPath)
                {
                    node.Path = node.Path.Trim();
                    node.Text = string.Empty;
                }
                else
                {
                    node.Path = string.Empty;
                }
            }
            return null;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && _boldFont != null)
            {
                _boldFont.Dispose();
            }
        }

        private int Scaled(int value)
        {
            return (int)Math.Round(value * DeviceDpi / 96.0);
        }
    }
}
