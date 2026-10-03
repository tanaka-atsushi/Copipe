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

            // 二重起動は、ホットキーの登録に失敗する前にここで見分ける (他のアプリがキーを使っている場合と区別するため)。
            // Local\ なので、同じサインインの中だけで 1 つにする。終了するまで持ち続ける
            bool createdNew;
            using (Mutex single = new Mutex(true, @"Local\Copipe-SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    // 起動中の Copipe と同じ言語で知らせる
                    Lang.Apply(Services.Settings.Load(Services.Settings.DefaultPath).Language);
                    MessageBox.Show(
                        Lang.T("Copipe はすでに起動しています。", "Copipe is already running."),
                        "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 1;
                }
                return Run();
            }
        }

        private static int Run()
        {
            try
            {
                using (CopipeApp app = new CopipeApp())
                {
                    if (!app.Start())
                    {
                        MessageBox.Show(
                            Lang.T("ホットキー " + app.HotkeyName + " を登録できませんでした。\n\n" +
                                   "他のアプリまたは Windows が使用中です。",
                                   "Could not register the hotkey " + app.HotkeyName + ".\n\n" +
                                   "The key is in use by another app or Windows."),
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
                    Lang.T("起動できませんでした。\n\n", "Could not start.\n\n") + ex.ToString(),
                    "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            // 常駐アプリなので、予期しない例外でも内容を知らせたうえで動作を続ける
            MessageBox.Show(
                Lang.T("予期しないエラーが発生しました。\n\n", "An unexpected error occurred.\n\n") + e.Exception.Message +
                Lang.T("\n\n詳細:\n", "\n\nDetails:\n") + e.Exception.ToString(),
                "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
