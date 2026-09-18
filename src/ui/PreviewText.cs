using System;

namespace Copipe.UI
{
    /// <summary>履歴の 1 件を、小窓の一覧に出す 1 行の文字列に変換する。</summary>
    public static class PreviewText
    {
        /// <summary>一覧の 1 行に渡す最大文字数。小窓の幅に収まる量より十分多く、描画が重くならない量。</summary>
        public const int LineMaxChars = 200;

        /// <summary>
        /// 履歴の 1 件を一覧の 1 行にする。改行とタブは空白にし、前後の空白は落とし、
        /// 長すぎる場合は先頭だけを返す。
        /// </summary>
        public static string Line(string text, int maxChars)
        {
            if (maxChars < 1)
            {
                throw new ArgumentOutOfRangeException("maxChars", maxChars, "1 以上を指定してください。");
            }
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string one = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            if (one.Length <= maxChars)
            {
                return one;
            }

            int cut = maxChars;
            // 絵文字などのサロゲートペアを半分で切ると、描画で化けた文字になる
            if (char.IsHighSurrogate(one[cut - 1]))
            {
                cut--;
            }
            return one.Substring(0, cut) + "…";
        }
    }
}
