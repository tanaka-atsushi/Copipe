using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Copipe.Services
{
    /// <summary>
    /// コピーしたテキストの履歴。新しい順に持ち、保持数を超えた分は古いものから捨てる。
    /// 同じ内容をまたコピーしたときは、増やさずに先頭へ移動する (CopyQ などと同じ)。
    /// </summary>
    public sealed class ClipboardHistory
    {
        /// <summary>
        /// 1 件あたりの上限。これを超えるコピーは履歴に入れない。
        /// Excel の広範囲コピーのたびに数 MB をディスクへ書かないようにするため。
        /// </summary>
        public const int MaxTextLength = 100000;

        private readonly List<string> _items = new List<string>();
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

        /// <summary>新しい順。</summary>
        public ReadOnlyCollection<string> Items
        {
            get { return _items.AsReadOnly(); }
        }

        /// <summary>履歴に加える。加えた (または先頭へ移動した) なら true。</summary>
        public bool Add(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > MaxTextLength)
            {
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

                using (FileStream stream = File.OpenRead(path))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(HistoryFile));
                    HistoryFile file = serializer.ReadObject(stream) as HistoryFile;
                    if (file != null && file.Items != null)
                    {
                        foreach (string item in file.Items)
                        {
                            if (history._items.Count >= capacity)
                            {
                                break;
                            }
                            if (string.IsNullOrEmpty(item) || item.Length > MaxTextLength || history._items.Contains(item))
                            {
                                continue;
                            }
                            history._items.Add(item);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 壊れたファイル・読み取り権限が無いなど。履歴より起動を優先する
                history._items.Clear();
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
        }
    }
}
