using Copipe.Services;

namespace Copipe.UI
{
    /// <summary>小窓の一覧の行の種類。</summary>
    internal enum PopupRowKind
    {
        /// <summary>入力できる項目 (履歴か定型文)。</summary>
        Item,
        /// <summary>定型文のグループ。選ぶと中に入る。</summary>
        Group,
        /// <summary>定型文の空きの枠。選んでも何もしない。</summary>
        Empty
    }

    /// <summary>小窓の一覧の 1 行。</summary>
    internal sealed class PopupRow
    {
        public static string EmptyText
        {
            get { return Lang.T("（空き）", "(empty)"); }
        }
        /// <summary>グループの表示 (名前の前にツリーの枝 ├ を付ける)。</summary>
        public static string GroupLabel(string name)
        {
            return "├ " + name;
        }
        public const string FolderMark = "📁 ";
        public const string FileMark = "📄 ";
        public const string PinMark = "📌";

        public PopupRow(PopupRowKind kind, string text)
        {
            Kind = kind;
            Text = text ?? string.Empty;
            // 履歴の「開く項目」は、印を外したパスを見せ、ファイル・フォルダーの絵文字を付ける
            string path, label;
            if (kind == PopupRowKind.Item && ClipboardHistory.TryGetLaunchPath(Text, out path, out label))
            {
                DisplayText = label;
                Mark = System.IO.Directory.Exists(path) ? FolderMark : FileMark;
            }
            else
            {
                DisplayText = Text;
            }
        }

        /// <summary>一覧に見せる文字 (Text から履歴の「開く項目」の印を外したもの)。</summary>

        public string DisplayText { get; private set; }

        public PopupRowKind Kind { get; private set; }

        /// <summary>項目なら全文、グループなら名前。空きでは使わない。</summary>
        public string Text { get; private set; }

        /// <summary>ピン止めした履歴の項目か (一覧の上部に番号なしで出し、右端に 📌 を付ける)。</summary>
        public bool IsPinned { get; set; }

        /// <summary>項目の名前の前に付ける印 (ファイル・フォルダーを開く定型文の 📄・📁)。無ければ空。</summary>
        public string Mark { get; set; }

        /// <summary>
        /// 一覧に見せる文字列。ListBox はこれを項目の文字列として持つので、
        /// 検証ハーネスも LB_GETTEXT で同じものを読める。
        /// </summary>
        public override string ToString()
        {
            switch (Kind)
            {
                case PopupRowKind.Group:
                    return GroupLabel(Text);
                case PopupRowKind.Empty:
                    return EmptyText;
                default:
                    return IsPinned ? PinMark + " " + DisplayText : (Mark ?? string.Empty) + DisplayText;
            }
        }
    }
}
