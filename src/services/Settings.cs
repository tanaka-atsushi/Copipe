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
        /// <summary>設定ファイルが無いときのホットキー。</summary>
        public const Keys DefaultHotkey = Keys.F1;

        /// <summary>設定ファイルが無いときのモードキー (ホットキーを押したまま押して、履歴と定型文を切り替える)。</summary>
        public const Keys DefaultModeKey = Keys.Tab;

        /// <summary>履歴の保持数の既定値と範囲。</summary>
        public const int DefaultHistoryCount = 10;
        public const int MinHistoryCount = 1;
        public const int MaxHistoryCount = 50;

        private const string HotkeyName = "Hotkey";
        private const string HistoryCountName = "HistoryCount";
        private const string InsertClickName = "InsertClick";
        private const string ModeKeyName = "ModeKey";

        public Settings()
        {
            Hotkey = DefaultHotkey;
            HistoryCount = DefaultHistoryCount;
            InsertClick = InsertClick.Double;
            ModeKey = DefaultModeKey;
        }

        public Keys Hotkey { get; set; }

        /// <summary>履歴に残す件数。</summary>
        public int HistoryCount { get; set; }

        /// <summary>履歴の項目を貼り付ける操作 (ダブルクリック / シングルクリック)。</summary>
        public InsertClick InsertClick { get; set; }

        /// <summary>
        /// 小窓を出している間に押して、クリップボード履歴と定型文を切り替えるキー。修飾キーは付けない
        /// (ホットキーに Ctrl などが付いていれば、押したままなので自動で同じ修飾キー付きで受け取る)。
        /// </summary>
        public Keys ModeKey { get; set; }

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
                        if (HotkeyText.TryParse(value, out keys))
                        {
                            settings.Hotkey = keys;
                        }
                    }
                    else if (string.Equals(name, HistoryCountName, StringComparison.OrdinalIgnoreCase))
                    {
                        int count;
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out count) &&
                            count >= MinHistoryCount && count <= MaxHistoryCount)
                        {
                            settings.HistoryCount = count;
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
            sb.AppendLine("# Copipe の設定 (メモ帳で編集できます)");
            sb.AppendLine("# Hotkey: 押している間だけ小窓を出すキー。例: F1、Ctrl+Space、IMENonconvert (無変換)");
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture, "{0}={1}", HotkeyName, HotkeyText.ToSetting(Hotkey)));
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture, "# HistoryCount: 履歴に残す件数 ({0}〜{1})", MinHistoryCount, MaxHistoryCount));
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture, "{0}={1}", HistoryCountName, HistoryCount));
            sb.AppendLine("# InsertClick: 履歴の項目を貼り付ける操作。Double (ダブルクリック) か Single (シングルクリック)");
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture, "{0}={1}", InsertClickName, InsertClick));
            sb.AppendLine("# ModeKey: 小窓を出している間に押して、クリップボード履歴と定型文を切り替えるキー。例: Tab、F2");
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture, "{0}={1}", ModeKeyName, HotkeyText.ToSetting(ModeKey)));

            // 日本語のコメントが化けないよう BOM 付きの UTF-8 で書く
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
    }
}
