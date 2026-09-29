using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Copipe.UI
{
    /// <summary>Copipe について。バージョンと、Buy Me a Coffee のカンパの QR コードを出す (クリックでも開く)。</summary>
    internal sealed class AboutDialog : Form
    {
        public const string CoffeeUrl = "https://buymeacoffee.com/bigcomi";
        private const string QrResourceName = "Copipe.bmc-qr.png";

        private readonly Image _qr;

        public AboutDialog()
        {
            Text = Lang.T("Copipe について", "About Copipe");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(Scaled(300), Scaled(288));

            Label version = new Label();
            version.Name = "versionLabel";
            version.Text = Lang.T("バージョン ", "Version ") + Version;
            version.TextAlign = ContentAlignment.MiddleCenter;
            version.Bounds = new Rectangle(Scaled(8), Scaled(20), Scaled(284), Scaled(20));
            Controls.Add(version);

            Label coffee = new Label();
            coffee.Text = Lang.T("役に立ったら缶コーヒー１本おごってください。",
                                 "If Copipe helps you, you can buy me a coffee.");
            coffee.TextAlign = ContentAlignment.MiddleCenter;
            coffee.Bounds = new Rectangle(Scaled(8), Scaled(44), Scaled(284), Scaled(20));
            Controls.Add(coffee);

            PictureBox qr = new PictureBox();
            qr.SizeMode = PictureBoxSizeMode.Zoom;
            qr.Bounds = new Rectangle(Scaled(60), Scaled(84), Scaled(180), Scaled(180));
            qr.Cursor = Cursors.Hand;
            qr.Click += delegate { OpenCoffee(); };
            using (Stream stream = typeof(AboutDialog).Assembly.GetManifestResourceStream(QrResourceName))
            {
                if (stream != null)
                {
                    // Image.FromStream はストリームを閉じると描けなくなるので、写しを持つ
                    using (Image loaded = Image.FromStream(stream))
                    {
                        _qr = new Bitmap(loaded);
                    }
                    qr.Image = _qr;
                }
            }
            Controls.Add(qr);

            // OK ボタンは置かないので、Esc / Enter で閉じられるようにする
            KeyPreview = true;
            KeyDown += delegate (object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter)
                {
                    Close();
                }
            };
        }

        /// <summary>AssemblyInfo.cs の AssemblyInformationalVersion (例: 1.0.0)。</summary>
        public static string Version
        {
            get
            {
                object[] found = typeof(AboutDialog).Assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
                return found.Length > 0 ? ((AssemblyInformationalVersionAttribute)found[0]).InformationalVersion : string.Empty;
            }
        }

        private static void OpenCoffee()
        {
            try
            {
                Process.Start(CoffeeUrl);
            }
            catch (Exception)
            {
                // 既定のブラウザーが無いなど。QR をスマホで読んでもらう
            }
        }

        private int Scaled(int value)
        {
            return (int)Math.Round(value * DeviceDpi / 96.0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_qr != null)
                {
                    _qr.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
