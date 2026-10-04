using System;
using System.Text;
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
            Mutex single;
            try
            {
                single = new Mutex(true, @"Local\Copipe-SingleInstance", out createdNew);
            }
            catch (UnauthorizedAccessException)
            {
                // 管理者として起動している Copipe の Mutex は、普通の権限からは開けない (小窓に WM_CLOSE も届かない)
                Lang.Apply(Services.Settings.Load(Services.Settings.DefaultPath).Language);
                MessageBox.Show(
                    Lang.T("管理者として起動している Copipe があるため、入れ替えられませんでした。",
                           "Could not replace Copipe because it is running as administrator."),
                    "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
            using (single)
            {
                if (!createdNew)
                {
                    // 起動中の Copipe と同じ言語で聞く
                    Lang.Apply(Services.Settings.Load(Services.Settings.DefaultPath).Language);
                    if (MessageBox.Show(
                            Lang.T("Copipe はすでに起動しています。入れ替えますか？",
                                   "Copipe is already running. Replace it?"),
                            "Copipe", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    {
                        return 1;
                    }
                    if (!ReplaceRunning(single))
                    {
                        MessageBox.Show(
                            Lang.T("起動中の Copipe を終了できなかったので、入れ替えられませんでした。",
                                   "Could not replace Copipe because the running one did not exit."),
                            "Copipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return 1;
                    }
                }
                return Run();
            }
        }

        /// <summary>
        /// 起動中の Copipe に終了を頼み、終わったら Mutex を引き継ぐ。
        /// インストーラーや tools\Stop-Copipe.ps1 と同じく、小窓 (タイトル "Copipe") に WM_CLOSE を送って正常に終わらせる
        /// (小窓が閉じると Copipe ごと終了する)。強制終了はしない。
        /// </summary>
        private static bool ReplaceRunning(Mutex single)
        {
            // このプロセスはまだウインドウを作っていないので、見つかるのは起動中の Copipe の窓だけ
            IntPtr wnd = IntPtr.Zero;
            while ((wnd = NativeMethods.FindWindowEx(IntPtr.Zero, wnd, null, "Copipe")) != IntPtr.Zero)
            {
                // 同じタイトルのエクスプローラー (Copipe フォルダーを開いた窓) などは除く。小窓は WinForms の窓
                StringBuilder cls = new StringBuilder(256);
                NativeMethods.GetClassName(wnd, cls, cls.Capacity);
                if (cls.ToString().StartsWith("WindowsForms10.", StringComparison.Ordinal))
                {
                    NativeMethods.PostMessage(wnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
            }
            try
            {
                return single.WaitOne(5000);
            }
            catch (AbandonedMutexException)
            {
                // 起動中の Copipe は Mutex を解放せずに終わるので、こちらになる。引き継げている
                return true;
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
