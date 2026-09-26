using System;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace Copipe.UI
{
    /// <summary>
    /// ホットキーの「画面に出す名前」「設定ファイルに書く文字列」「使えるキーかどうか」を扱う。
    /// </summary>
    public static class HotkeyText
    {
        // 日本語入力の切り替えキー。.NET の Keys に名前が無く、KeysConverter は空文字列を返す。
        // さらに、押して離しても GetAsyncKeyState が「押されたまま」を返し続ける (実測)。
        // 次に押したときに切り替わる作りのため、離したことを判定できない。
        private const Keys ImeAlphanumeric = (Keys)0xF0;        // 英数 (VK_OEM_ATTN)
        private const Keys ImeKatakanaHiragana = (Keys)0xF2;    // カタカナ ひらがな (VK_OEM_COPY)
        private const Keys ImeZenkakuWhenOff = (Keys)0xF3;      // 半角/全角 (IME がオフのとき。VK_OEM_AUTO)
        private const Keys ImeZenkakuWhenOn = (Keys)0xF4;       // 半角/全角 (IME がオンのとき。VK_OEM_ENLW)

        private const string VkPrefix = "VK";

        /// <summary>
        /// 押している間の判定ができないキー。押して離しても「押されたまま」に見えるので、
        /// 小窓が出たまま消えなくなる (実測: 半角/全角・英数・カタカナ ひらがな)。
        /// </summary>
        public static bool CannotDetectRelease(Keys keys)
        {
            Keys code = keys & Keys.KeyCode;
            return code == ImeAlphanumeric
                || code == ImeKatakanaHiragana
                || code == ImeZenkakuWhenOff
                || code == ImeZenkakuWhenOn;
        }

        /// <summary>
        /// 数字キー (上段・テンキー) か。小窓を出している間は一覧から選ぶために使うので、
        /// ホットキーにはできない (修飾キーの有無によらない)。
        /// </summary>
        public static bool IsDigitKey(Keys keys)
        {
            return ItemNumber.IndexFromKey(keys) >= 0;
        }

        /// <summary>
        /// ホットキーとして使えるキーか。修飾キー (Ctrl・Shift・Alt) との組み合わせは使えない
        /// (修飾キーを押したままだと、小窓で使う Tab が Alt+Tab になるなど、Windows と取り合いになるため)。
        /// 修飾キー単独・離したことを判定できないキー・数字キーも使えない。
        /// </summary>
        public static bool IsValid(Keys keys)
        {
            if (CannotDetectRelease(keys) || IsDigitKey(keys) || (keys & Keys.Modifiers) != Keys.None)
            {
                return false;
            }

            Keys code = keys & Keys.KeyCode;
            switch (code)
            {
                case Keys.None:
                case Keys.ControlKey:
                case Keys.ShiftKey:
                case Keys.Menu:          // Alt
                case Keys.LControlKey:
                case Keys.RControlKey:
                case Keys.LShiftKey:
                case Keys.RShiftKey:
                case Keys.LMenu:
                case Keys.RMenu:
                case Keys.LWin:
                case Keys.RWin:
                case Keys.ProcessKey:    // IME が処理したキー
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// モードキー (小窓を出している間に押して、履歴と定型文を切り替えるキー) として使えるか。
        /// ホットキーと同じ条件 (修飾キーは付けない)。
        /// </summary>
        public static bool IsValidModeKey(Keys keys)
        {
            return IsValid(keys);
        }

        /// <summary>
        /// モードキーがホットキーと同じキーか (修飾キーは見ない)。同じだと、モードキーを押すことが
        /// ホットキーを押すことになってしまうので使えない。
        /// </summary>
        public static bool ConflictsWithHotkey(Keys hotkey, Keys modeKey)
        {
            return (hotkey & Keys.KeyCode) == (modeKey & Keys.KeyCode);
        }

        /// <summary>左右のある修飾キー (LControlKey・RMenu など) を、左右の無いキー (ControlKey・Menu) にそろえる。それ以外はキーだけにする。</summary>
        public static Keys NormalizeModifier(Keys keys)
        {
            Keys code = keys & Keys.KeyCode;
            switch (code)
            {
                case Keys.LControlKey:
                case Keys.RControlKey:
                    return Keys.ControlKey;
                case Keys.LShiftKey:
                case Keys.RShiftKey:
                    return Keys.ShiftKey;
                case Keys.LMenu:
                case Keys.RMenu:
                    return Keys.Menu;
                default:
                    return code;
            }
        }

        /// <summary>ダブルタップに使えるキーか (Ctrl・Shift・Alt。左右どちらでもよい)。</summary>
        public static bool IsValidDoubleTap(Keys keys)
        {
            Keys code = NormalizeModifier(keys);
            return code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu;
        }

        /// <summary>ダブルタップのキーの表示名 (Ctrl・Shift・Alt)。使えないキーなら空文字列。設定ファイルにもこの名前で書く。</summary>
        public static string DoubleTapDisplay(Keys keys)
        {
            switch (NormalizeModifier(keys))
            {
                case Keys.ControlKey:
                    return "Ctrl";
                case Keys.ShiftKey:
                    return "Shift";
                case Keys.Menu:
                    return "Alt";
                default:
                    return string.Empty;
            }
        }

        /// <summary>設定ファイルのダブルタップの値 (Ctrl・Shift・Alt・None) を読む。読めなければ false。</summary>
        public static bool TryParseDoubleTap(string text, out Keys keys)
        {
            keys = Keys.None;
            string value = text == null ? string.Empty : text.Trim();
            foreach (Keys candidate in new[] { Keys.ControlKey, Keys.ShiftKey, Keys.Menu })
            {
                if (string.Equals(value, DoubleTapDisplay(candidate), StringComparison.OrdinalIgnoreCase))
                {
                    keys = candidate;
                    return true;
                }
            }
            return string.Equals(value, NoneSetting, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>設定ファイルで「使わない」を表す値。</summary>
        public const string NoneSetting = "None";

        /// <summary>画面に出す名前。日本語キーボード固有のキーは日本語で出す (例: 無変換)。</summary>
        public static string Display(Keys keys)
        {
            return Modifiers(keys) + KeyName(keys & Keys.KeyCode);
        }

        /// <summary>
        /// 設定ファイルに書く文字列。読み戻せるように、キーの名前は .NET の Keys の名前を使う
        /// (例: F1、Ctrl+Space、IMENonconvert)。名前が無いキーは VK 番号で書く (例: VK243)。
        /// </summary>
        public static string ToSetting(Keys keys)
        {
            Keys code = keys & Keys.KeyCode;
            string name = ConvertKeyCode(code);
            if (name.Length == 0)
            {
                name = VkPrefix + ((int)code).ToString(CultureInfo.InvariantCulture);
            }
            return Modifiers(keys) + name;
        }

        /// <summary>
        /// 設定ファイルの文字列を読み戻す。読めない、またはホットキーに使えないキーなら false。
        /// </summary>
        public static bool TryParse(string text, out Keys keys)
        {
            keys = Keys.None;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string rest = text.Trim();
            Keys modifiers = Keys.None;
            bool more = true;
            while (more)
            {
                if (rest.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= Keys.Control;
                    rest = rest.Substring(5).Trim();
                }
                else if (rest.StartsWith("Shift+", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= Keys.Shift;
                    rest = rest.Substring(6).Trim();
                }
                else if (rest.StartsWith("Alt+", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= Keys.Alt;
                    rest = rest.Substring(4).Trim();
                }
                else
                {
                    more = false;
                }
            }

            if (rest.Length == 0)
            {
                return false;
            }

            Keys code;
            if (rest.StartsWith(VkPrefix, StringComparison.OrdinalIgnoreCase))
            {
                int number;
                if (!int.TryParse(rest.Substring(VkPrefix.Length), NumberStyles.None,
                                  CultureInfo.InvariantCulture, out number) ||
                    number <= 0 || number > 0xFF)
                {
                    return false;
                }
                code = (Keys)number;
            }
            else
            {
                try
                {
                    object value = new KeysConverter().ConvertFromInvariantString(rest);
                    if (!(value is Keys))
                    {
                        return false;
                    }
                    code = (Keys)value;
                }
                catch (Exception)
                {
                    // KeysConverter は読めない文字列で例外を投げる
                    return false;
                }
            }

            Keys parsed = modifiers | code;
            if (!IsValid(parsed))
            {
                return false;
            }
            keys = parsed;
            return true;
        }

        private static string Modifiers(Keys keys)
        {
            StringBuilder sb = new StringBuilder();
            if ((keys & Keys.Control) == Keys.Control) { sb.Append("Ctrl+"); }
            if ((keys & Keys.Shift) == Keys.Shift) { sb.Append("Shift+"); }
            if ((keys & Keys.Alt) == Keys.Alt) { sb.Append("Alt+"); }
            return sb.ToString();
        }

        private static string KeyName(Keys code)
        {
            // 日本語キーボードのキーは、キートップの刻印に合わせる
            switch (code)
            {
                case Keys.IMENonconvert:
                    return Lang.T("無変換", "Nonconvert");
                case Keys.IMEConvert:
                    return Lang.T("変換", "Convert");
                case Keys.KanaMode:
                case ImeKatakanaHiragana:
                    return Lang.T("カタカナ ひらがな", "Katakana/Hiragana");
                case ImeAlphanumeric:
                    return Lang.T("英数", "Eisu");
                case ImeZenkakuWhenOff:
                case ImeZenkakuWhenOn:
                    return Lang.T("半角/全角", "Hankaku/Zenkaku");
            }

            string name = ConvertKeyCode(code);
            if (name.Length > 0)
            {
                return name;
            }
            // Keys に名前が無いキー。何も出ないと選んだキーが分からないので番号で示す
            return string.Format(CultureInfo.InvariantCulture, Lang.T("キー (0x{0:X2})", "Key (0x{0:X2})"), (int)code);
        }

        /// <summary>Keys の名前 (無い場合は空文字列)。</summary>
        private static string ConvertKeyCode(Keys code)
        {
            try
            {
                string name = new KeysConverter().ConvertToInvariantString(code);
                return name == null ? string.Empty : name;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
