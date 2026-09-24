using System.Drawing;
using System.IO;

namespace Copipe.UI
{
    /// <summary>
    /// Copipe のアイコン (assets\icon\copipe.ico を exe に埋め込んだもの)。
    /// exe 自体のアイコン (エクスプローラー・タスクバー) は、ビルド時に /win32icon で同じファイルを付けている。
    /// </summary>
    internal static class AppIcon
    {
        private const string ResourceName = "Copipe.copipe.ico";

        /// <summary>
        /// その大きさのアイコンを読む。ico には 16〜256 の大きさが入っていて、一番近いものが選ばれる
        /// (トレイの大きさを渡せば、大きいものを縮めてぼやけることがない)。
        /// 読めなければ Windows 標準のアプリのアイコンを返す。
        /// </summary>
        public static Icon Load(Size size)
        {
            using (Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    return (Icon)SystemIcons.Application.Clone();
                }
                return new Icon(stream, size);
            }
        }
    }
}
