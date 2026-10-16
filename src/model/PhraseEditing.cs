using System;
using System.Collections.Generic;
using System.Text;

namespace Copipe.Model
{
    /// <summary>
    /// 定型文の一括編集で使う、画面に依らない処理。定型文の木を複製して編集し (キャンセルできるように)、
    /// 貼り付けた表を空きの枠に入れる。
    /// </summary>
    public static class PhraseEditing
    {
        /// <summary>
        /// 定型文の木を丸ごと複製する。map には元の節 → 複製した節 を入れる (開いていた階層を、保存後に対応させるため)。
        /// </summary>
        public static PhraseNode Clone(PhraseNode node, Dictionary<PhraseNode, PhraseNode> map)
        {
            PhraseNode copy;
            if (node.IsGroup)
            {
                copy = PhraseNode.CreateGroup(node.Name);
                for (int i = 0; i < node.Slots.Length; i++)
                {
                    copy.Slots[i] = node.Slots[i] == null ? null : Clone(node.Slots[i], map);
                }
            }
            else
            {
                copy = PhraseNode.CreatePhrase(node.Name, node.Text, node.Path);
            }
            map[node] = copy;
            return copy;
        }

        /// <summary>グループの中の定型文とグループの数 (中のグループの中も数える)。</summary>
        public static void CountContents(PhraseNode group, ref int phrases, ref int groups)
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

        /// <summary>グループに何か入っているか。</summary>
        public static bool HasContents(PhraseNode group)
        {
            foreach (PhraseNode child in group.Slots)
            {
                if (child != null)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 開くものの書き方か (https: などの URI、C:\ などのドライブから始まるパス、\\ から始まる共有フォルダー)。
        /// 貼り付けた値を「開く定型文」にするか「文字の定型文」にするかを決めるのに使う。
        /// </summary>
        public static bool LooksLikePath(string value)
        {
            string text = (value ?? string.Empty).Trim();
            if (text.Length == 0 || text.IndexOf('\n') >= 0)
            {
                return false;
            }
            if (text.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return true;
            }
            if (text.Length >= 3 && char.IsLetter(text[0]) && text[1] == ':' && (text[2] == '\\' || text[2] == '/'))
            {
                return true;
            }
            // URI は https://… のような「://」付きと mailto: だけにする (「TODO:直す」のような本文を開くものにしない)
            Uri uri;
            return System.Text.RegularExpressions.Regex.IsMatch(text, @"\A([A-Za-z][A-Za-z0-9+.\-]*://|mailto:)\S+\z") &&
                   Uri.TryCreate(text, UriKind.Absolute, out uri);
        }

        /// <summary>
        /// 貼り付けた表 (1 行が 1 つ。列が 2 つ以上なら 1 列目が表示名、2 列目が中身。1 列なら中身だけ) を、
        /// グループの空いている枠に前から入れる。中身が開くものの書き方ならパス、それ以外は本文にする。
        /// 入れた枠の位置を返す。空きが足りなかった行の数を skipped に返す。中身の無い行は飛ばす。
        /// </summary>
        public static List<int> PasteInto(PhraseNode group, IList<List<string>> rows, out int skipped)
        {
            List<int> placed = new List<int>();
            skipped = 0;
            int slot = 0;
            foreach (List<string> row in rows)
            {
                string title = row.Count >= 2 ? row[0].Trim() : string.Empty;
                string value = row.Count >= 2 ? row[1] : (row.Count == 1 ? row[0] : string.Empty);
                if (value.Trim().Length == 0)
                {
                    continue;
                }
                while (slot < group.Slots.Length && group.Slots[slot] != null)
                {
                    slot++;
                }
                if (slot >= group.Slots.Length)
                {
                    skipped++;
                    continue;
                }
                group.Slots[slot] = LooksLikePath(value)
                    ? PhraseNode.CreatePhrase(title, string.Empty, value.Trim())
                    : PhraseNode.CreatePhrase(title, value, string.Empty);
                placed.Add(slot);
            }
            return placed;
        }

        /// <summary>
        /// タブ区切りの文字を表にする。Excel は改行やタブを含むセルを "…" で囲み、中の " を "" にするので、それも読む。
        /// 最後の改行の後の空の行は捨てる。セルの中の改行は Windows の改行 (\r\n) にそろえる。
        /// </summary>
        public static List<List<string>> ParseTabSeparated(string text)
        {
            List<List<string>> rows = new List<List<string>>();
            List<string> row = new List<string>();
            StringBuilder cell = new StringBuilder();
            bool quoted = false;
            bool cellStart = true;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        cell.Append(c);
                    }
                    continue;
                }
                if (c == '"' && cellStart)
                {
                    quoted = true;
                    cellStart = false;
                    continue;
                }
                cellStart = false;
                if (c == '\t')
                {
                    row.Add(cell.ToString());
                    cell.Length = 0;
                    cellStart = true;
                }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    row.Add(cell.ToString());
                    cell.Length = 0;
                    rows.Add(row);
                    row = new List<string>();
                    cellStart = true;
                }
                else
                {
                    cell.Append(c);
                }
            }
            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }
            foreach (List<string> r in rows)
            {
                for (int i = 0; i < r.Count; i++)
                {
                    r[i] = r[i].Replace("\r\n", "\n").Replace("\n", "\r\n");
                }
            }
            return rows;
        }
    }
}
