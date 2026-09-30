// Reklamy Vyltrix Echo dla programow WinForms. Pisane w C# 5, zeby kompilowal je
// takze csc z .NET Framework (tak buduje sie np. NewsyVE) - bez =>, ?. i $"".
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace VyltrixEcho.Promo
{
    /// <summary>
    /// Ten sam uklad co w OpenFences: pasek nad ustawieniami (banner z lewej, QR z prawej,
    /// oba 88 px) i przycisk "Postaw kawe" do stopki.
    /// Grafiki sa osadzone w .exe jako zasoby "VyltrixPromo.VyltrixEcho.png",
    /// "VyltrixPromo.BuyCoffeeQr.png", "VyltrixPromo.BuyCoffee.png" (patrz README zestawu).
    /// </summary>
    public static class VyltrixPromo
    {
        public const string WebsiteUrl = "https://vyltrixecho.pl";
        public const string CoffeeUrl = "https://buycoffee.to/vyltrixecho";

        static readonly Color Gold = Color.FromArgb(0xE8, 0xC4, 0x68);
        static readonly Color GoldDim = Color.FromArgb(0x70, 0x5E, 0x33); // #66E8C468 na czarnym

        public static string BannerTip = "vyltrixecho.pl - otwiera strone w przegladarce";
        public static string QrTip = "Zeskanuj, zeby postawic kawe na buycoffee.to";
        public static string CoffeeTip = "Postaw kawe dla VyltrixEcho na buycoffee.to";

        /// <summary>Pasek 88 px: banner z lewej, QR z prawej. Dock = Top nad reszta okna.</summary>
        public static Control Header(ToolTip tips)
        {
            var panel = new Panel { Height = 88, Dock = DockStyle.Top, BackColor = Color.Transparent };

            // Kafelek bannera: czarne tlo jak w bitmapie, 1 px zlotej ramki, 11/12 px oddechu,
            // grafika 64 px wysokosci - razem 88 px, rowno z QR.
            var bannerImage = Load("VyltrixEcho.png");
            var bannerWidth = bannerImage == null ? 200 : (int)Math.Round(bannerImage.Width * 64.0 / bannerImage.Height);

            var banner = new PictureBox
            {
                Image = bannerImage,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                Size = new Size(bannerWidth + 2 * 13, 88),
                Padding = new Padding(13, 12, 13, 12),
                Location = new Point(0, 0),
                Cursor = Cursors.Hand,
            };
            var hover = false;
            banner.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(hover ? Gold : GoldDim))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, banner.Width - 1, banner.Height - 1);
                }
            };
            banner.MouseEnter += delegate { hover = true; banner.Invalidate(); };
            banner.MouseLeave += delegate { hover = false; banner.Invalidate(); };
            banner.Click += delegate { Open(WebsiteUrl); };
            tips.SetToolTip(banner, BannerTip);

            // QR: 88 px, biale tlo - ciemna otulina psuje odczyt. Mniejszy juz nie bedzie czytelny.
            var qr = new PictureBox
            {
                Image = Load("BuyCoffeeQr.png"),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White,
                Size = new Size(88, 88),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand,
            };
            qr.Click += delegate { Open(CoffeeUrl); };
            tips.SetToolTip(qr, QrTip);

            panel.Controls.Add(banner);
            panel.Controls.Add(qr);
            panel.Layout += delegate { qr.Location = new Point(panel.ClientSize.Width - qr.Width, 0); };
            return panel;
        }

        /// <summary>Oficjalny przycisk buycoffee.to (351x92) w 35 px wysokosci.</summary>
        public static Control CoffeeButton(ToolTip tips)
        {
            var image = Load("BuyCoffee.png");
            var button = new PictureBox
            {
                Image = image,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(image == null ? 134 : (int)Math.Round(image.Width * 35.0 / image.Height), 35),
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent,
            };
            button.Click += delegate { Open(CoffeeUrl); };
            tips.SetToolTip(button, CoffeeTip);
            return button;
        }

        static Image Load(string file)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VyltrixPromo." + file);
            if (stream == null)
            {
                return null;
            }

            // Image.FromStream wymaga otwartego strumienia przez cale zycie obrazka - kopiujemy do Bitmap.
            using (stream)
            using (var temp = Image.FromStream(stream))
            {
                return new Bitmap(temp);
            }
        }

        static void Open(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Nie udalo sie otworzyc " + url + ":\n" + ex.Message, "Vyltrix Echo",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
