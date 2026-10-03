using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Copipe.UI;

namespace Copipe.Services
{
    /// <summary>履歴の項目を貼り付ける操作。</summary>
    public enum InsertClick
    {
        /// <summary>ダブルクリックで貼り付ける (既定)。シングルクリックでは何もしない。</summary>
        Double,
        /// <summary>クリック 1 回で貼り付ける。</summary>
        Single
    }

    /// <summary>
    /// 設定の読み書き。中身は「項目名=値」の行が並ぶだけのテキストで、メモ帳でも直せる。
    /// 読めない値は既定に戻す (設定が壊れていても起動できるようにする)。
    /// </summary>
    public sealed class Settings
    {
        /// <summary>設定ファイルが無いときのホットキー (押し続ける。CapsLock 本来の切り替えはしなくなる)。</summary>
        public const Keys DefaultHotkey = Keys.Capital;

        /// <summary>設定ファイルが無いときのモードキー (ホットキーを押したまま押して、履歴と定型文を切り替える)。</summary>
        public const Keys DefaultModeKey = Keys.Tab;


        private const string HotkeyName = "Hotkey";

        private const string InsertClickName = "InsertClick";
        private const string ModeKeyName = "ModeKey";
        private const string LanguageName = "Language";

        public Settings()
        {
            Hotkey = DefaultHotkey;
            InsertClick = InsertClick.Double;
            ModeKey = DefaultModeKey;
            Language = UiLanguage.Auto;
        }

        public Keys Hotkey { get; set; }


        /// <summary>履歴の項目を貼り付ける操作 (ダブルクリック / シングルクリック)。</summary>
        public InsertClick InsertClick { get; set; }

        /// <summary>
        /// 小窓を出している間に押して、クリップボード履歴と定型文を切り替えるキー。修飾キーは付けない
        /// (ホットキーに Ctrl などが付いていれば、押したままなので自動で同じ修飾キー付きで受け取る)。
        /// </summary>
        public Keys ModeKey { get; set; }

        /// <summary>画面の言葉 (Windows に合わせる / 日本語 / 英語)。</summary>
        public UiLanguage Language { get; set; }

        /// <summary>%LOCALAPPDATA%\Copipe\settings.ini</summary>
        public static string DefaultPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Copipe");
                return Path.Combine(dir, "settings.ini");
            }
        }

        /// <summary>読み込む。ファイルが無い・壊れている・使えないキーなら既定を返す (例外は投げない)。</summary>
        public static Settings Load(string path)
        {
            Settings settings = new Settings();
            try
            {
                if (!File.Exists(path))
                {
                    return settings;
                }

                foreach (string line in File.ReadAllLines(path))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed[0] == '#')
                    {
                        continue;
                    }

                    int separator = trimmed.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string name = trimmed.Substring(0, separator).Trim();
                    string value = trimmed.Substring(separator + 1).Trim();
                    if (string.Equals(name, HotkeyName, StringComparison.OrdinalIgnoreCase))
                    {
                        Keys keys;
                        // None (使わない) は読まずに既定のままにする。ホットキーが無いと小窓を出せないため
                        // (以前の版はダブルタップだけで使えたので None を書いていた)
                        if (HotkeyText.TryParse(value, out keys))
                        {
                            settings.Hotkey = keys;
                        }
                    }
                    else if (string.Equals(name, ModeKeyName, StringComparison.OrdinalIgnoreCase))
                    {
                        Keys keys;
                        if (HotkeyText.TryParse(value, out keys) && HotkeyText.IsValidModeKey(keys))
                        {
                            settings.ModeKey = keys;
                        }
                    }
                    else if (string.Equals(name, LanguageName, StringComparison.OrdinalIgnoreCase))
                    {
                        // 名前だけを受け付ける (Enum.Parse だと "1" のような数字まで読めてしまう)
                        foreach (UiLanguage language in new[] { UiLanguage.Auto, UiLanguage.Japanese, UiLanguage.English })
                        {
                            if (string.Equals(value, language.ToString(), StringComparison.OrdinalIgnoreCase))
                            {
                                settings.Language = language;
                            }
                        }
                    }
                    else if (string.Equals(name, InsertClickName, StringComparison.OrdinalIgnoreCase))
                    {
                        // 名前だけを受け付ける (Enum.Parse だと "1" のような数字まで読めてしまう)
                        if (string.Equals(value, "Single", StringComparison.OrdinalIgnoreCase))
                        {
                            settings.InsertClick = InsertClick.Single;
                        }
                        else if (string.Equals(value, "Double", StringComparison.OrdinalIgnoreCase))
                        {
                            settings.InsertClick = InsertClick.Double;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 壊れたファイル・読み取り権限が無いなど。設定より起動を優先する
                return new Settings();
            }
            return settings;
        }

        /// <summary>保存する。フォルダーが無ければ作る。失敗したら例外を投げる (呼び出し側で知らせる)。</summary>
        public void Save(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Lang.T("# Copipe の設定 (メモ帳で編集できます)", "# Copipe settings (you can edit this file in Notepad)"));
            sb.AppendLine(Lang.T("# Hotkey: 押している間だけ小窓を出すキー。修飾キー (Ctrl など) は付けられない。例: F1、Pause、IMENonconvert (無変換)",
                                 "# Hotkey: key that shows the popup while held. No modifiers (Ctrl etc.). e.g. F1, Pause"));
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture, "{0}={1}", HotkeyName, HotkeyText.ToSetting(Hotkey)));
            sb.AppendLine(Lang.T("# InsertClick: 履歴の項目を貼り付ける操作。Double (ダブルクリック) か Single (シングルクリック)",
                                 "# InsertClick: click to paste an item. Double or Single"));
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture, "{0}={1}", InsertClickName, InsertClick));
            sb.AppendLine(Lang.T("# ModeKey: 小窓を出している間に押して、クリップボード履歴と定型文を切り替えるキー。例: Tab、F2",
                                 "# ModeKey: key that switches between clipboard history and snippets in the popup. e.g. Tab, F2"));
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture, "{0}={1}", ModeKeyName, HotkeyText.ToSetting(ModeKey)));
            sb.AppendLine(Lang.T("# Language: 画面の言葉。Auto (Windows の表示言語に合わせる)、Japanese、English",
                                 "# Language: display language. Auto (follow Windows), Japanese or English"));
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture, "{0}={1}", LanguageName, Language));

            // 日本語のコメントが化けないよう BOM 付きの UTF-8 で書く
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
    }
}
