using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Copipe.Model
{
    /// <summary>
    /// Outlook・Word からコピーした文の段落の字下げを、行頭の全角スペースにして文字に戻す。
    ///
    /// Outlook では、文頭に打ったスペースが字下げ (段落の書式) に変わる。クリップボードの文字 (CF_UNICODETEXT) には
    /// 書式が入らないので、そのまま取り込むと字下げが消えて左に寄る (ユーザーの報告)。同時に置かれる HTML (HTML Format) には
    /// 段落ごとの字下げ (margin-left・text-indent など) が入っているので、それを読んで、文字の行の頭に全角スペースを足す。
    /// 本当の字下げではないので、長い行を折り返した 2 行目は左端から始まる (ユーザーが選んだ案)。
    ///
    /// Word が作った HTML (Outlook・Word・OneNote など) のときだけ使う。ブラウザーの HTML は余白の付け方がまちまちで、
    /// 字下げではない余白まで空白にしてしまうため。
    /// </summary>
    public static class WordIndent
    {
        /// <summary>全角スペース 1 つ分とみなす幅 (pt)。日本語の Outlook の既定の文字の大きさ (10.5pt) に合わせる。</summary>
        private const double CharWidthPt = 10.5;

        /// <summary>1 行に足す全角スペースの上限 (壊れた値で行が空白だらけにならないように)。</summary>
        private const int MaxIndentChars = 40;

        /// <summary>行を対応させるとき、HTML の行を先へ何行まで探すか (文字の行に無い HTML の行を読み飛ばすため)。</summary>
        private const int MatchWindow = 8;

        /// <summary>HTML がこれより大きいときは読まない (大きなメールで、コピーのたびに時間がかからないように)。</summary>
        public const int MaxHtmlLength = 2 * 1024 * 1024;

        private static readonly Regex RemovedBlocks = new Regex(
            @"<!--.*?-->|<style\b.*?</style\s*>|<script\b.*?</script\s*>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Tag = new Regex(
            @"<(/?)([A-Za-z][A-Za-z0-9:]*)([^>]*)>|<![^>]*>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        private static readonly Regex StyleAttribute = new Regex(
            @"\bstyle\s*=\s*(?:'([^']*)'|""([^""]*)""|([^\s>]+))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Length = new Regex(
            @"^\s*(-?[0-9]*\.?[0-9]+)\s*(pt|px|in|cm|mm|gd|em)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// CF_HTML (HTML Format) の中身 (UTF-8 のバイト列を文字にしたもの) から、Word の HTML なら字下げを読み、
        /// text の対応する行の頭に全角スペースを足して返す。Word の HTML でないとき・字下げが無いときは text をそのまま返す。
        /// </summary>
        public static string Apply(string text, string cfHtml)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(cfHtml) || cfHtml.Length > MaxHtmlLength ||
                !IsWordHtml(cfHtml))
            {
                return text;
            }
            List<HtmlLine> htmlLines = ReadLines(Fragment(cfHtml));
            bool any = false;
            foreach (HtmlLine line in htmlLines)
            {
                if (line.Indent > 0)
                {
                    any = true;
                    break;
                }
            }
            if (!any)
            {
                return text;
            }

            // 文字の行と HTML の行を、空白を除いた中身が同じものどうし、前から順に対応させる
            StringBuilder result = new StringBuilder(text.Length + 64);
            int next = 0;
            int start = 0;
            while (start <= text.Length)
            {
                int newline = text.IndexOf('\n', start);
                int end = newline < 0 ? text.Length : newline + 1;
                string line = text.Substring(start, end - start);
                string key = Normalize(line);
                int indent = 0;
                if (key.Length == 0)
                {
                    // 空の行は、HTML の次の行も空のときだけ対応させる (空の段落は字下げを足さない)
                    if (next < htmlLines.Count && htmlLines[next].Key.Length == 0)
                    {
                        next++;
                    }
                }
                else
                {
                    for (int i = next; i < htmlLines.Count && i < next + MatchWindow; i++)
                    {
                        if (htmlLines[i].Key == key)
                        {
                            indent = htmlLines[i].Indent;
                            next = i + 1;
                            break;
                        }
                    }
                }
                if (indent > 0)
                {
                    result.Append('　', indent);
                }
                result.Append(line);
                if (newline < 0)
                {
                    break;
                }
                start = end;
            }
            return result.ToString();
        }

        /// <summary>Word (Outlook・OneNote も) が作った HTML か。</summary>
        private static bool IsWordHtml(string html)
        {
            return html.IndexOf("urn:schemas-microsoft-com:office:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   html.IndexOf("class=MsoNormal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   html.IndexOf("content=\"Microsoft Word", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   html.IndexOf("content=Microsoft Word", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>コピーした部分 (&lt;!--StartFragment--&gt; から &lt;!--EndFragment--&gt; まで)。印が無ければ全体。</summary>
        private static string Fragment(string html)
        {
            const string StartMark = "<!--StartFragment-->";
            const string EndMark = "<!--EndFragment-->";
            int start = html.IndexOf(StartMark, StringComparison.OrdinalIgnoreCase);
            int end = html.LastIndexOf(EndMark, StringComparison.OrdinalIgnoreCase);
            if (start >= 0 && end > start)
            {
                return html.Substring(start + StartMark.Length, end - start - StartMark.Length);
            }
            return html;
        }

        private sealed class HtmlLine
        {
            public string Key;
            public int Indent;
        }

        /// <summary>
        /// HTML の段落 (p・h1〜h6・li) を行に分ける (段落の中の &lt;br&gt; でも分ける)。
        /// 段落の最初の行は左の字下げ + 1 行目の字下げ、続く行は左の字下げだけを付ける。
        /// </summary>
        private static List<HtmlLine> ReadLines(string fragment)
        {
            List<HtmlLine> lines = new List<HtmlLine>();
            string html = RemovedBlocks.Replace(fragment, string.Empty);
            StringBuilder current = null;
            int firstIndent = 0;
            int leftIndent = 0;
            bool firstLine = true;
            int position = 0;
            foreach (Match tag in Tag.Matches(html))
            {
                if (current != null)
                {
                    current.Append(html, position, tag.Index - position);
                }
                position = tag.Index + tag.Length;
                string name = tag.Groups[2].Value.ToLowerInvariant();
                bool closing = tag.Groups[1].Value.Length > 0;
                if (IsParagraph(name))
                {
                    if (current != null)
                    {
                        AddLine(lines, current, firstLine ? firstIndent : leftIndent);
                        current = null;
                    }
                    if (!closing)
                    {
                        current = new StringBuilder();
                        ReadIndent(tag.Groups[3].Value, out leftIndent, out firstIndent);
                        firstLine = true;
                    }
                }
                else if (name == "br" && current != null)
                {
                    AddLine(lines, current, firstLine ? firstIndent : leftIndent);
                    current = new StringBuilder();
                    firstLine = false;
                }
            }
            if (current != null)
            {
                current.Append(html, position, html.Length - position);
                AddLine(lines, current, firstLine ? firstIndent : leftIndent);
            }
            return lines;
        }

        private static bool IsParagraph(string name)
        {
            return name == "p" || name == "li" ||
                   (name.Length == 2 && name[0] == 'h' && name[1] >= '1' && name[1] <= '6');
        }

        private static void AddLine(List<HtmlLine> lines, StringBuilder raw, int indent)
        {
            HtmlLine line = new HtmlLine();
            line.Key = Normalize(WebUtility.HtmlDecode(raw.ToString()));
            line.Indent = Math.Max(0, Math.Min(MaxIndentChars, indent));
            lines.Add(line);
        }

        /// <summary>行を比べるための形。空白 (改行・全角スペース・&amp;nbsp; も) を除く。</summary>
        private static string Normalize(string text)
        {
            StringBuilder key = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (!char.IsWhiteSpace(c) && c != ' ' && c != '　')
                {
                    key.Append(c);
                }
            }
            return key.ToString();
        }

        /// <summary>
        /// 段落の style から、左の字下げ (left) と 1 行目の字下げ (first = 左 + 1 行目) を全角スペースの数で読む。
        /// Word は文字数で決めた字下げを mso-para-margin-left (gd = 文字数)・mso-char-indent-count (文字数) にも書くので、
        /// あればそちらを使う (pt から割り戻すより正確)。
        /// </summary>
        private static void ReadIndent(string attributes, out int left, out int first)
        {
            left = 0;
            first = 0;
            Match style = StyleAttribute.Match(attributes);
            if (!style.Success)
            {
                return;
            }
            string value = style.Groups[1].Success ? style.Groups[1].Value
                : style.Groups[2].Success ? style.Groups[2].Value : style.Groups[3].Value;
            Dictionary<string, string> properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string declaration in WebUtility.HtmlDecode(value).Split(';'))
            {
                int colon = declaration.IndexOf(':');
                if (colon > 0)
                {
                    properties[declaration.Substring(0, colon).Trim()] = declaration.Substring(colon + 1).Trim();
                }
            }

            double leftChars;
            if (!TryProperty(properties, "mso-para-margin-left", out leftChars) &&
                !TryProperty(properties, "margin-left", out leftChars) &&
                !TryMarginLeft(properties, out leftChars))
            {
                leftChars = 0;
            }
            double firstChars;
            if (!TryCount(properties, "mso-char-indent-count", out firstChars) &&
                !TryProperty(properties, "text-indent", out firstChars))
            {
                firstChars = 0;
            }

            left = (int)Math.Round(Math.Max(0, leftChars), MidpointRounding.AwayFromZero);
            first = (int)Math.Round(Math.Max(0, leftChars + firstChars), MidpointRounding.AwayFromZero);
        }

        /// <summary>style の name の長さを、全角スペースの数で読む。</summary>
        private static bool TryProperty(Dictionary<string, string> properties, string name, out double chars)
        {
            string text;
            chars = 0;
            return properties.TryGetValue(name, out text) && TryChars(text, out chars);
        }

        /// <summary>margin (上 右 下 左) の左を、全角スペースの数で読む (値が 4 つのときだけ左が分かる)。</summary>
        private static bool TryMarginLeft(Dictionary<string, string> properties, out double chars)
        {
            string text;
            chars = 0;
            if (!properties.TryGetValue("margin", out text))
            {
                return false;
            }
            string[] parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 4 && TryChars(parts[3], out chars);
        }

        /// <summary>style の name の数 (単位なし。mso-char-indent-count など) を読む。</summary>
        private static bool TryCount(Dictionary<string, string> properties, string name, out double count)
        {
            string text;
            count = 0;
            return properties.TryGetValue(name, out text) &&
                   double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out count);
        }

        /// <summary>CSS の長さ (10.5pt・2.0gd など) を、全角スペースの数にする。</summary>
        private static bool TryChars(string text, out double chars)
        {
            chars = 0;
            Match match = Length.Match(text);
            double number;
            if (!match.Success ||
                !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                return false;
            }
            switch (match.Groups[2].Value.ToLowerInvariant())
            {
                case "gd":
                case "em":
                    chars = number;
                    return true;
                case "pt":
                    chars = number / CharWidthPt;
                    return true;
                case "px":
                    chars = number * 0.75 / CharWidthPt;
                    return true;
                case "in":
                    chars = number * 72 / CharWidthPt;
                    return true;
                case "cm":
                    chars = number * 72 / 2.54 / CharWidthPt;
                    return true;
                case "mm":
                    chars = number * 72 / 25.4 / CharWidthPt;
                    return true;
                case "":
                    // 単位の無い 0 だけ
                    return number == 0;
            }
            return false;
        }
    }
}
