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
        /// <summary>空きの枠。文字は出さず、選んでも何もしない。</summary>
        Empty
    }

    /// <summary>小窓の一覧の 1 行。</summary>
    internal sealed class PopupRow
    {
        /// <summary>グループの表示 (名前の前にツリーの枝 ├ を付ける)。</summary>
        public static string GroupLabel(string name)
        {
            return "├ " + name;
        }
        public const string FolderMark = "📁 ";
        public const string FileMark = "📄 ";
        public const string UrlMark = "🌐 ";
        public const string TextMark = "✍ ";
        public const string PinMark = "📌";

        /// <summary>
        /// text が URI (https:、mailto:、ms-settings: など) か。空白を含まず「英字 2 文字以上のスキーム:」で始まるもの。
        /// C:\ のようなドライブ文字 (1 文字) は URI にしない。
        /// </summary>
        public static bool IsUri(string text)
        {
            // ponytail: 「TODO:直す」のような 1 語の文字列も URI と見なす。困ればスキームを既知のものに絞る
            System.Uri uri;
            return text != null
                && System.Text.RegularExpressions.Regex.IsMatch(text, @"\A[A-Za-z][A-Za-z0-9+.\-]+:\S+\z")
                && System.Uri.TryCreate(text, System.UriKind.Absolute, out uri);
        }

        /// <summary>開く先に合う印 (URI は 🌐、フォルダーは 📁、それ以外は 📄)。</summary>
        public static string MarkForPath(string path)
        {
            if (IsUri(path))
            {
                return UrlMark;
            }
            // ponytail: 表示のたびに存在確認。つながらないネットワーク先だと遅れる。遅ければ登録時に種類を保存する
            return System.IO.Directory.Exists(path) ? FolderMark : FileMark;
        }

        public PopupRow(PopupRowKind kind, string text)
        {
            Kind = kind;
            Text = text ?? string.Empty;
            // 履歴の「開く項目」は、印を外したパスを見せ、ファイル・フォルダーの絵文字を付ける
            string path, label;
            if (kind == PopupRowKind.Item && ClipboardHistory.TryGetLaunchPath(Text, out path, out label))
            {
                DisplayText = label;
                Mark = MarkForPath(path);
            }
            else
            {
                DisplayText = Text;
                if (kind == PopupRowKind.Item)
                {
                    Mark = TextMark;
                }
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
                    return string.Empty;
                default:
                    return IsPinned ? PinMark + " " + DisplayText : (Mark ?? string.Empty) + DisplayText;
            }
        }
    }
}
