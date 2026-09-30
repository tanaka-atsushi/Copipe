using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Copipe.Services
{
    /// <summary>
    /// コピーしたテキストの履歴。新しい順に持ち、保持数を超えた分は古いものから捨てる。
    /// 同じ内容をまたコピーしたときは、増やさずに先頭へ移動する (CopyQ などと同じ)。
    /// ピン止めした項目は別に持ち (Pinned)、保持数に数えず、消去もしない。
    /// </summary>
    public sealed class ClipboardHistory
    {
        /// <summary>
        /// 1 件あたりの上限。これを超えるコピーは履歴に入れない。
        /// Excel の広範囲コピーのたびに数 MB をディスクへ書かないようにするため。
        /// </summary>
        public const int MaxTextLength = 100000;

        /// <summary>
        /// Copipe が残す履歴の件数 (ピン止めは数えない)。小窓で振れる番号 (1〜9、0) と同じ 10 件に固定する。
        /// </summary>
        public const int MaxItems = 10;

        /// <summary>
        /// 履歴に入れたファイル・フォルダーの印。履歴は文字列だけを持つので、
        /// 「開く項目」は先頭にこの制御文字 (コピーした文字には通常現れない) を付けたパスとして持つ。
        /// </summary>
        private const char LaunchMark = '\u0001';

        /// <summary>開く項目の中で、パスと一覧に出す名前を分ける文字。</summary>
        private const char LabelMark = '\u0002';

        /// <summary>
        /// ファイル・フォルダーを開く履歴の項目にする。label は一覧に出す名前
        /// (定型文モードの表示と同じ。空ならパスをそのまま出す)。
        /// </summary>
        public static string LaunchEntry(string path, string label)
        {
            return LaunchMark + path + (string.IsNullOrEmpty(label) ? string.Empty : LabelMark + label);
        }

        /// <summary>text が開く項目なら、パスと一覧に出す名前 (無ければパス) を取り出して true。</summary>
        public static bool TryGetLaunchPath(string text, out string path, out string label)
        {
            if (!string.IsNullOrEmpty(text) && text[0] == LaunchMark)
            {
                int cut = text.IndexOf(LabelMark);
                path = cut < 0 ? text.Substring(1) : text.Substring(1, cut - 1);
                label = cut < 0 ? path : text.Substring(cut + 1);
                return true;
            }
            path = null;
            label = null;
            return false;
        }

        private readonly List<string> _items = new List<string>();
        private readonly List<string> _pinned = new List<string>();
        private int _capacity;

        public ClipboardHistory(int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException("capacity", capacity, "1 以上を指定してください。");
            }
            _capacity = capacity;
        }

        /// <summary>%LOCALAPPDATA%\Copipe\history.json</summary>
        public static string DefaultPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Copipe");
                return Path.Combine(dir, "history.json");
            }
        }

        /// <summary>保持数。減らすと古いものから捨てる。</summary>
        public int Capacity
        {
            get { return _capacity; }
            set
            {
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException("value", value, "1 以上を指定してください。");
                }
                _capacity = value;
                Trim();
            }
        }

        /// <summary>普通の履歴 (ピン止めしていないもの)。新しい順。</summary>
        public ReadOnlyCollection<string> Items
        {
            get { return _items.AsReadOnly(); }
        }

        /// <summary>ピン止めした項目。新しくピン止めしたものが先頭。</summary>
        public ReadOnlyCollection<string> Pinned
        {
            get { return _pinned.AsReadOnly(); }
        }

        /// <summary>普通の履歴かピン止めに、同じ内容があるか。</summary>
        public bool Contains(string text)
        {
            return _items.Contains(text) || _pinned.Contains(text);
        }

        /// <summary>普通の履歴の項目をピン止めする (ピン止めの先頭に入る)。ピン止めしたなら true。</summary>
        public bool Pin(string text)
        {
            if (!_items.Remove(text))
            {
                return false;
            }
            _pinned.Insert(0, text);
            return true;
        }

        /// <summary>
        /// 履歴に無い内容 (定型文など) もピン止めする。ピン止めの先頭に入れ、普通の履歴にあればそこから移す。
        /// ピン止め済みなら先頭へ移す。変わったなら true。
        /// </summary>
        public bool PinText(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > MaxTextLength)
            {
                return false;
            }
            if (_pinned.Count > 0 && _pinned[0] == text)
            {
                return false;
            }
            _pinned.Remove(text);
            _items.Remove(text);
            _pinned.Insert(0, text);
            return true;
        }

        /// <summary>
        /// 並べ替える。ピン止めの内容ならピン止めの中で、普通の履歴なら普通の履歴の中で、
        /// 抜き出して toIndex (その中での位置) に差し込む。動いたなら true。
        /// </summary>
        public bool Move(string text, int toIndex)
        {
            if (text == null)
            {
                return false;
            }
            List<string> list = _pinned.Contains(text) ? _pinned : _items;
            int from = list.IndexOf(text);
            if (from < 0 || toIndex < 0 || toIndex >= list.Count || toIndex == from)
            {
                return false;
            }
            list.RemoveAt(from);
            list.Insert(toIndex, text);
            return true;
        }

        /// <summary>ピン止めを外す。普通の履歴の先頭に戻り、保持数を超えたら古いものから捨てる。外したなら true。</summary>
        public bool Unpin(string text)
        {
            if (!_pinned.Remove(text))
            {
                return false;
            }
            _items.Insert(0, text);
            Trim();
            return true;
        }

        /// <summary>履歴に加える。加えた (または先頭へ移動した) なら true。</summary>
        public bool Add(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > MaxTextLength)
            {
                return false;
            }
            if (_pinned.Contains(text))
            {
                // ピン止めした内容。普通の履歴には増やさず、ピン止めの位置も変えない
                return false;
            }

            int index = _items.IndexOf(text);
            if (index == 0)
            {
                // 直前と同じ内容。並びも件数も変わらない
                return false;
            }
            if (index > 0)
            {
                _items.RemoveAt(index);
            }

            _items.Insert(0, text);
            Trim();
            return true;
        }

        /// <summary>普通の履歴を消す。ピン止めは残す。</summary>
        public void Clear()
        {
            _items.Clear();
        }

        /// <summary>読み込む。ファイルが無い・壊れているときは空にする (例外は投げない)。</summary>
        public static ClipboardHistory Load(string path, int capacity)
        {
            ClipboardHistory history = new ClipboardHistory(capacity);
            try
            {
                if (!File.Exists(path))
                {
                    return history;
                }

                // メモ帳などで BOM 付きで保存されても読めるよう、文字列として読んでから渡す
                // (DataContractJsonSerializer は先頭の BOM を読めない。定型文で実測)
                string json = File.ReadAllText(path, Encoding.UTF8);
                using (MemoryStream stream = new MemoryStream(new UTF8Encoding(false).GetBytes(json)))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(HistoryFile));
                    HistoryFile file = serializer.ReadObject(stream) as HistoryFile;
                    if (file != null)
                    {
                        // ピン止めを先に読む。普通の履歴と重なった内容はピン止めを優先する
                        if (file.Pinned != null)
                        {
                            foreach (string item in file.Pinned)
                            {
                                if (IsStorable(item) && !history._pinned.Contains(item))
                                {
                                    history._pinned.Add(item);
                                }
                            }
                        }
                        if (file.Items != null)
                        {
                            foreach (string item in file.Items)
                            {
                                if (history._items.Count >= capacity)
                                {
                                    break;
                                }
                                if (!IsStorable(item) || history.Contains(item))
                                {
                                    continue;
                                }
                                history._items.Add(item);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 壊れたファイル・読み取り権限が無いなど。履歴より起動を優先する
                history._items.Clear();
                history._pinned.Clear();
            }
            return history;
        }

        /// <summary>保存する。フォルダーが無ければ作る。失敗したら例外を投げる。</summary>
        public void Save(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            HistoryFile file = new HistoryFile();
            file.Items = _items.ToArray();
            // ピン止めが無ければ書かない (前の形式と同じ中身になる)
            file.Pinned = _pinned.Count > 0 ? _pinned.ToArray() : null;

            // 書いている途中で電源が切れてもファイルが壊れないよう、一時ファイルに書いてから置き換える
            string temp = path + ".tmp";
            using (FileStream stream = File.Create(temp))
            {
                new DataContractJsonSerializer(typeof(HistoryFile)).WriteObject(stream, file);
            }

            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        private static bool IsStorable(string text)
        {
            return !string.IsNullOrEmpty(text) && text.Length <= MaxTextLength;
        }

        private void Trim()
        {
            while (_items.Count > _capacity)
            {
                _items.RemoveAt(_items.Count - 1);
            }
        }

        /// <summary>保存ファイルの形。JSON にすると改行やタブを含む文字もそのまま扱える。</summary>
        [DataContract]
        public sealed class HistoryFile
        {
            [DataMember]
            public string[] Items { get; set; }

            /// <summary>ピン止めした項目 (先頭が一番上)。前の形式のファイルには無い。</summary>
            [DataMember(EmitDefaultValue = false)]
            public string[] Pinned { get; set; }
        }
    }
}
