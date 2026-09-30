using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Copipe.Model;

namespace Copipe.Services
{
    /// <summary>
    /// 定型文の読み書き。一番上の階層 (Root) もグループと同じく 10 個の枠を持つ。
    /// 手で書いたファイルも読めるよう、枠の数の過不足や読めない項目は直して読む。
    /// </summary>
    public sealed class PhraseBook
    {
        private const string GroupKind = "Group";
        private const string PhraseKind = "Phrase";

        /// <summary>手で書かれたファイルで深すぎる入れ子があっても落ちないよう、読む深さに上限を設ける。</summary>
        private const int MaxDepth = 64;

        public PhraseBook()
        {
            Root = PhraseNode.CreateGroup(string.Empty);
        }

        /// <summary>一番上の階層。</summary>
        public PhraseNode Root { get; private set; }

        /// <summary>%LOCALAPPDATA%\Copipe\phrases.json</summary>
        public static string DefaultPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Copipe");
                return Path.Combine(dir, "phrases.json");
            }
        }

        /// <summary>読み込む。ファイルが無い・壊れているときは空の 10 枠にする (例外は投げない)。</summary>
        public static PhraseBook Load(string path)
        {
            PhraseBook book = new PhraseBook();
            try
            {
                if (!File.Exists(path))
                {
                    return book;
                }

                // メモ帳などで BOM 付きで保存されても読めるよう、文字列として読んでから渡す
                // (DataContractJsonSerializer は先頭の BOM を読めない。実測)
                string json = File.ReadAllText(path, Encoding.UTF8);
                using (MemoryStream stream = new MemoryStream(new UTF8Encoding(false).GetBytes(json)))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(SlotData));
                    SlotData file = serializer.ReadObject(stream) as SlotData;
                    if (file != null)
                    {
                        FillSlots(book.Root, file.Slots, 0);
                    }
                }
            }
            catch (Exception)
            {
                // 壊れたファイル・読み取り権限が無いなど。定型文より起動を優先する
                return new PhraseBook();
            }
            return book;
        }

        /// <summary>保存する。フォルダーが無ければ作る。失敗したら例外を投げる。</summary>
        public void Save(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            SlotData file = new SlotData();
            file.Slots = ToData(Root.Slots);

            // 書いている途中で電源が切れてもファイルが壊れないよう、一時ファイルに書いてから置き換える。
            // 手でも直せるよう、字下げして書く
            string temp = path + ".tmp";
            using (FileStream stream = File.Create(temp))
            using (System.Xml.XmlDictionaryWriter writer =
                       JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), false, true))
            {
                new DataContractJsonSerializer(typeof(SlotData)).WriteObject(writer, file);
                writer.Flush();
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

        private static void FillSlots(PhraseNode group, List<SlotData> slots, int depth)
        {
            if (slots == null || depth > MaxDepth)
            {
                return;
            }
            // 足りない枠は空きのまま、11 個目以降は読み捨てる
            for (int i = 0; i < slots.Count && i < PhraseNode.SlotCount; i++)
            {
                group.Slots[i] = FromData(slots[i], depth + 1);
            }
        }

        private static PhraseNode FromData(SlotData data, int depth)
        {
            if (data == null)
            {
                return null;
            }
            if (string.Equals(data.Kind, GroupKind, StringComparison.OrdinalIgnoreCase))
            {
                PhraseNode group = PhraseNode.CreateGroup(data.Name);
                FillSlots(group, data.Slots, depth);
                return group;
            }
            if (string.Equals(data.Kind, PhraseKind, StringComparison.OrdinalIgnoreCase) &&
                (!string.IsNullOrEmpty(data.Text) || !string.IsNullOrEmpty(data.Path)))
            {
                return PhraseNode.CreatePhrase(data.Title, data.Text, data.Path);
            }
            // 読めない種類・本文もパスも無い定型文は空きにする
            return null;
        }

        private static List<SlotData> ToData(PhraseNode[] slots)
        {
            List<SlotData> list = new List<SlotData>(slots.Length);
            foreach (PhraseNode node in slots)
            {
                if (node == null)
                {
                    list.Add(null);
                    continue;
                }
                SlotData data = new SlotData();
                if (node.IsGroup)
                {
                    data.Kind = GroupKind;
                    data.Name = node.Name;
                    data.Slots = ToData(node.Slots);
                }
                else
                {
                    data.Kind = PhraseKind;
                    data.Title = node.Name.Length > 0 ? node.Name : null;
                    data.Text = node.Text.Length > 0 ? node.Text : null;
                    data.Path = node.Path.Length > 0 ? node.Path : null;
                }
                list.Add(data);
            }
            return list;
        }

        /// <summary>
        /// 保存ファイルの 1 つの枠 (ファイル全体は Slots だけを持つ一番上の枠)。
        /// 例: {"Kind":"Group","Name":"社外","Slots":[null,{"Kind":"Phrase","Title":"締め","Text":"…"}]}
        /// </summary>
        [DataContract]
        public sealed class SlotData
        {
            [DataMember(Order = 0, EmitDefaultValue = false)]
            public string Kind { get; set; }

            [DataMember(Order = 1, EmitDefaultValue = false)]
            public string Name { get; set; }

            [DataMember(Order = 2, EmitDefaultValue = false)]
            public string Title { get; set; }

            [DataMember(Order = 3, EmitDefaultValue = false)]
            public string Text { get; set; }

            [DataMember(Order = 4, EmitDefaultValue = false)]
            public List<SlotData> Slots { get; set; }

            [DataMember(Order = 5, EmitDefaultValue = false)]
            public string Path { get; set; }
        }
    }
}
