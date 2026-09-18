using System;
using System.Threading;
using System.Windows.Forms;
using Copipe.Interop;

namespace Copipe
{
    public static class Program
    {
        [STAThread]
        public static int Main()
        {
            // ウインドウを 1 つも作る前に宣言する必要がある
            NativeMethods.SetProcessDPIAware();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += OnThreadException;

            try
            {
                using (CopipeApp app = new CopipeApp())
                {
                    if (!app.Start())
                    {
                        MessageBox.Show(
                            "ホットキー " + app.HotkeyName + " を登録できませんでした。\n\n" +
                            "Copipe がすでに起動しているか、他のアプリまたは Windows が使用中です。",
                            "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return 1;
                    }

                    Application.Run(app);
                    return 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "起動できませんでした。\n\n" + ex.ToString(),
                    "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            // 常駐アプリなので、予期しない例外でも内容を知らせたうえで動作を続ける
            MessageBox.Show(
                "予期しないエラーが発生しました。\n\n" + e.Exception.Message +
                "\n\n詳細:\n" + e.Exception.ToString(),
                "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
