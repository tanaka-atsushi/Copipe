using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Copipe.UI
{
    /// <summary>Copipe について。バージョンと、Buy Me a Coffee のカンパの QR コードとリンクを出す。</summary>
    internal sealed class AboutDialog : Form
    {
        public const string CoffeeUrl = "https://buymeacoffee.com/bigcomi";
        private const string QrResourceName = "Copipe.bmc-qr.png";

        private readonly Image _qr;
        private readonly Font _nameFont;

        public AboutDialog()
        {
            Text = Lang.T("Copipe について", "About Copipe");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(Scaled(320), Scaled(400));

            Label name = new Label();
            name.Text = "Copipe";
            _nameFont = new Font(Font.FontFamily, Font.Size * 1.6f, FontStyle.Bold);
            name.Font = _nameFont;
            name.AutoSize = true;
            name.Location = new Point(Scaled(16), Scaled(14));
            Controls.Add(name);

            Label version = new Label();
            version.Name = "versionLabel";
            version.Text = Lang.T("バージョン ", "Version ") + Version;
            version.AutoSize = true;
            version.Location = new Point(Scaled(18), Scaled(48));
            Controls.Add(version);

            Label coffee = new Label();
            coffee.Text = Lang.T("役に立ったら、コーヒー 1 杯分の応援をいただけるとうれしいです。",
                                 "If Copipe helps you, you can buy me a coffee.");
            coffee.Bounds = new Rectangle(Scaled(16), Scaled(78), Scaled(288), Scaled(36));
            Controls.Add(coffee);

            PictureBox qr = new PictureBox();
            qr.SizeMode = PictureBoxSizeMode.Zoom;
            qr.Bounds = new Rectangle(Scaled(70), Scaled(116), Scaled(180), Scaled(180));
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

            LinkLabel link = new LinkLabel();
            link.Name = "coffeeLink";
            link.Text = CoffeeUrl;
            link.AutoSize = true;
            link.Location = new Point(Scaled(16), Scaled(308));
            link.LinkClicked += delegate { OpenCoffee(); };
            Controls.Add(link);

            Button ok = new Button();
            ok.Text = "OK";
            ok.DialogResult = DialogResult.OK;
            ok.Bounds = new Rectangle(Scaled(220), Scaled(354), Scaled(84), Scaled(30));
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = ok;
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
                // 既定のブラウザーが無いなど。リンクの文字は出ているので、手で開いてもらう
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
                _nameFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
