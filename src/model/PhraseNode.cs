using System;

namespace Copipe.Model
{
    /// <summary>
    /// 定型文モードの 1 つの枠の中身。定型文 (表示名と本文) かグループ (名前と 10 個の枠) のどちらか。
    /// 空きの枠は null で表す。
    /// </summary>
    public sealed class PhraseNode
    {
        /// <summary>1 つの階層に置ける数。数字キーの 1〜9、0 に対応する。</summary>
        public const int SlotCount = 10;

        private PhraseNode(bool isGroup, string name, string text, string path)
        {
            IsGroup = isGroup;
            Name = name ?? string.Empty;
            Text = text ?? string.Empty;
            Path = path ?? string.Empty;
            Slots = isGroup ? new PhraseNode[SlotCount] : null;
        }

        /// <summary>グループを作る。枠は 10 個とも空き。</summary>
        public static PhraseNode CreateGroup(string name)
        {
            return new PhraseNode(true, name, null, null);
        }

        /// <summary>
        /// 定型文を作る。title は一覧に出す表示名 (空なら本文の最初の行かファイル名を出す)。
        /// path はファイル・フォルダーのパス (空なら普通の定型文)。
        /// </summary>
        public static PhraseNode CreatePhrase(string title, string text, string path)
        {
            return new PhraseNode(false, title, text, path);
        }

        /// <summary>グループなら true、定型文なら false。</summary>
        public bool IsGroup { get; private set; }

        /// <summary>グループの名前、または定型文の表示名 (空のこともある)。</summary>
        public string Name { get; set; }

        /// <summary>定型文の表示名 (Name と同じ。定型文として読むときの名前)。</summary>
        public string Title
        {
            get { return Name; }
        }

        /// <summary>定型文の本文。グループでは空。</summary>
        public string Text { get; set; }

        /// <summary>
        /// 起動するファイル・フォルダーのパス。空でなければ、選んだとき本文を入力せずこれを開く。
        /// グループでは空。
        /// </summary>
        public string Path { get; set; }

        /// <summary>グループの 10 個の枠 (空きは null)。定型文では null。</summary>
        public PhraseNode[] Slots { get; private set; }

        /// <summary>一覧に出す名前。グループは名前、定型文は表示名か、無ければ本文の最初の空でない行。</summary>
        public string Label
        {
            get
            {
                if (IsGroup || Name.Trim().Length > 0)
                {
                    return Name;
                }
                foreach (string line in Text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length > 0)
                    {
                        return trimmed;
                    }
                }
                if (Path.Trim().Length > 0)
                {
                    string trimmed = Path.Trim().TrimEnd('\\', '/');
                    int cut = trimmed.LastIndexOfAny(new[] { '\\', '/' });
                    return cut >= 0 ? trimmed.Substring(cut + 1) : trimmed;
                }
                return string.Empty;
            }
        }
    }
}
