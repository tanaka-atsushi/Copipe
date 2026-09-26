using System.Globalization;

namespace Copipe
{
    /// <summary>画面の言葉の設定。</summary>
    public enum UiLanguage
    {
        /// <summary>Windows の表示言語に合わせる (日本語なら日本語、それ以外は英語)。既定。</summary>
        Auto,
        Japanese,
        English
    }

    /// <summary>
    /// 画面に出す言葉の切り替え。既定は Windows の表示言語に合わせ、設定で日本語・英語に固定できる。
    /// </summary>
    public static class Lang
    {
        /// <summary>アプリの名前 (メッセージの題名など)。どの言語でも同じ。</summary>
        public const string AppName = "Copipe";

        private static readonly bool WindowsJapanese = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja";

        /// <summary>日本語で出すか。検証では書き換えて表示を確かめる。</summary>
        public static bool Japanese = WindowsJapanese;

        /// <summary>設定の言語にする。以後の T はその言語の言葉を返す。</summary>
        public static void Apply(UiLanguage language)
        {
            Japanese = language == UiLanguage.Auto ? WindowsJapanese : language == UiLanguage.Japanese;
        }

        /// <summary>日本語なら ja、それ以外なら en。</summary>
        public static string T(string ja, string en)
        {
            return Japanese ? ja : en;
        }
    }
}
