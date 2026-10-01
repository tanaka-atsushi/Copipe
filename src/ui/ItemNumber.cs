using System.Collections.ObjectModel;
using System.Windows.Forms;

namespace Copipe.UI
{
    /// <summary>
    /// 一覧の先頭 10 件に付ける番号と、それを選ぶ数字キーの対応。
    /// キーボードの並びどおり 1〜9、0 の順 (10 件目が 0)。11 件目以降は番号も数字キーも無い。
    /// </summary>
    public static class ItemNumber
    {
        /// <summary>番号を付ける件数。</summary>
        public const int Count = 10;

        private static readonly ReadOnlyCollection<Keys> _allKeys = new ReadOnlyCollection<Keys>(new Keys[]
        {
            Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9, Keys.D0,
            Keys.NumPad1, Keys.NumPad2, Keys.NumPad3, Keys.NumPad4, Keys.NumPad5,
            Keys.NumPad6, Keys.NumPad7, Keys.NumPad8, Keys.NumPad9, Keys.NumPad0,
        });

        /// <summary>小窓を出している間に受け取る数字キー (上段とテンキー)。</summary>
        public static ReadOnlyCollection<Keys> AllKeys
        {
            get { return _allKeys; }
        }

        /// <summary>一覧の index 番目 (0 始まり) に付ける番号。11 件目以降と範囲外は null。</summary>
        public static string Label(int index)
        {
            if (index < 0 || index >= Count)
            {
                return null;
            }
            return index == Count - 1 ? "0" : (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>ピン止めの index 番目 (0 始まり) に付ける文字 (a〜z)。27 件目以降と範囲外は null。</summary>
        public static string PinLabel(int index)
        {
            return (index < 0 || index >= 26) ? null : ((char)('a' + index)).ToString();
        }

        /// <summary>数字キーが指す一覧の位置 (0 始まり)。数字キーでなければ -1。修飾キーのビットは無視する。</summary>
        public static int IndexFromKey(Keys keys)
        {
            Keys code = keys & Keys.KeyCode;
            if (code >= Keys.D1 && code <= Keys.D9)
            {
                return code - Keys.D1;
            }
            if (code >= Keys.NumPad1 && code <= Keys.NumPad9)
            {
                return code - Keys.NumPad1;
            }
            if (code == Keys.D0 || code == Keys.NumPad0)
            {
                return Count - 1;
            }
            return -1;
        }
    }
}
