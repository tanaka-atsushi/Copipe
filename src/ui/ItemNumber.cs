namespace Copipe.UI
{
    /// <summary>
    /// 一覧の先頭 10 件 (定型文の 1 階層の枠) に付ける番号。
    /// キーボードの並びどおり 1〜9、0 の順 (10 件目が 0)。11 件目以降は番号が無い。
    /// </summary>
    public static class ItemNumber
    {
        /// <summary>番号を付ける件数。</summary>
        public const int Count = 10;

        /// <summary>一覧の index 番目 (0 始まり) に付ける番号。11 件目以降と範囲外は null。</summary>
        public static string Label(int index)
        {
            if (index < 0 || index >= Count)
            {
                return null;
            }
            return index == Count - 1 ? "0" : (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
