using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("NewsyVE — instalator")]
[assembly: AssemblyCompany("VyltrixEcho")]
[assembly: AssemblyProduct("NewsyVE")]
[assembly: AssemblyCopyright("© 2026 Paweł Juszczyk (VyltrixEcho) · licencja MIT")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace NewsyVESetup
{
    // Instalator NewsyVE: aplikacja i README siedza w zasobach tego pliku,
    // wiec calosc to jeden plik do uruchomienia.
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }

    static class T
    {
        public static readonly Color Bg    = Color.FromArgb(30, 31, 34);
        public static readonly Color Card  = Color.FromArgb(39, 40, 44);
        public static readonly Color Line  = Color.FromArgb(58, 59, 65);
        public static readonly Color Tx    = Color.FromArgb(232, 233, 237);
        public static readonly Color Tx2   = Color.FromArgb(158, 160, 167);
        public static readonly Color Tx3   = Color.FromArgb(112, 114, 122);
        public static readonly Color Acc   = Color.FromArgb(126, 166, 204);
        public static readonly Color Ok    = Color.FromArgb(126, 184, 140);
        public static readonly Color Bad   = Color.FromArgb(208, 106, 108);
        public static Font F(float s) { return new Font("Segoe UI", s, FontStyle.Regular, GraphicsUnit.Pixel); }
        public static Font F(float s, FontStyle st) { return new Font("Segoe UI", s, st, GraphicsUnit.Pixel); }
    }

    class Flat : Button
    {
        public Color Tint = T.Acc;
        public Flat()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = T.Card;
            ForeColor = T.Tx;
            Font = T.F(13, FontStyle.Bold);
            Cursor = Cursors.Hand;
            Height = 34;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Round(r, 8))
            using (SolidBrush b = new SolidBrush(Enabled ? BackColor : T.Card))
            using (Pen pen = new Pen(Enabled ? Tint : T.Line))
            { e.Graphics.FillPath(b, p); e.Graphics.DrawPath(pen, p); }
            TextRenderer.DrawText(e.Graphics, Text, Font, r,
                Enabled ? ForeColor : T.Tx3,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        public static GraphicsPath Round(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath(); int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    class SetupForm : Form
    {
        static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewsyVE");
        static readonly string Programs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs");
        static string Exe { get { return Path.Combine(Dir, "NewsyVE.exe"); } }

        readonly CheckBox cbDesktop = new CheckBox();
        readonly CheckBox cbAutostart = new CheckBox();
        readonly CheckBox cbRun = new CheckBox();
        readonly Label status = new Label();
        readonly Flat btnInstall = new Flat();
        readonly Flat btnRemove = new Flat();

        public SetupForm()
        {
            Text = "NewsyVE — instalacja";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 340);
            BackColor = T.Bg;
            ForeColor = T.Tx;
            Font = T.F(13);
            try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); }
            catch { }

            Label title = new Label();
            title.Text = "NewsyVE";
            title.Font = T.F(24, FontStyle.Bold);
            title.ForeColor = T.Tx;
            title.SetBounds(24, 20, 300, 34);
            title.BackColor = Color.Transparent;

            Label sub = new Label();
            sub.Text = "Pogoda, kursy, MPK i newsy na pasku zadań.\n" +
                       "Jedna aplikacja, bez przeglądarki w tle.";
            sub.Font = T.F(12);
            sub.ForeColor = T.Tx2;
            sub.SetBounds(26, 58, 420, 40);

            Label where = new Label();
            where.Text = "Katalog: " + Dir;
            where.Font = T.F(11);
            where.ForeColor = T.Tx3;
            where.SetBounds(26, 104, 420, 18);

            cbDesktop.Text = "Skrót na pulpicie";
            cbDesktop.Checked = true;
            Style(cbDesktop, 140);

            cbAutostart.Text = "Uruchamiaj razem z Windows";
            cbAutostart.Checked = IsAutostart();
            Style(cbAutostart, 168);

            cbRun.Text = "Uruchom po instalacji";
            cbRun.Checked = true;
            Style(cbRun, 196);

            btnInstall.Text = Installed() ? "Aktualizuj" : "Zainstaluj";
            btnInstall.Tint = T.Acc;
            btnInstall.SetBounds(26, 238, 200, 36);
            btnInstall.Click += delegate { Install(); };

            btnRemove.Text = "Odinstaluj";
            btnRemove.Tint = T.Line;
            btnRemove.ForeColor = T.Tx2;
            btnRemove.SetBounds(238, 238, 130, 36);
            btnRemove.Enabled = Installed();
            btnRemove.Click += delegate { Uninstall(); };

            Flat close = new Flat();
            close.Text = "Zamknij";
            close.Tint = T.Line;
            close.ForeColor = T.Tx2;
            close.SetBounds(378, 238, 56, 36);
            close.Click += delegate { Close(); };

            status.Font = T.F(11);
            status.ForeColor = T.Tx3;
            status.SetBounds(26, 288, 410, 36);

            Controls.AddRange(new Control[] {
                title, sub, where, cbDesktop, cbAutostart, cbRun,
                btnInstall, btnRemove, close, status });
        }

        void Style(CheckBox c, int y)
        {
            c.SetBounds(26, y, 400, 22);
            c.ForeColor = T.Tx;
            c.Font = T.F(13);
            c.FlatStyle = FlatStyle.Flat;
            c.Cursor = Cursors.Hand;
        }

        static bool Installed() { return File.Exists(Exe); }
        static string AutostartLink { get { return Path.Combine(Programs, @"Startup\NewsyVE.lnk"); } }
        static bool IsAutostart() { return File.Exists(AutostartLink); }

        void Say(string text, Color c)
        {
            status.ForeColor = c;
            status.Text = text;
            status.Refresh();
        }

        static void StopRunning()
        {
            foreach (Process p in Process.GetProcessesByName("NewsyVE"))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
        }

        static void Shortcut(string path, string target, string desc)
        {
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                new object[] { path });
            Type lt = lnk.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk,
                new object[] { Path.GetDirectoryName(target) });
            lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk,
                new object[] { target + ",0" });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { desc });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
        }

        static void Extract(string resource, string dest)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (s == null) throw new FileNotFoundException("Brak zasobu: " + resource);
                using (FileStream f = File.Create(dest)) s.CopyTo(f);
            }
        }

        void Install()
        {
            try
            {
                btnInstall.Enabled = false;
                Say("Zatrzymywanie działającej kopii…", T.Tx3);
                StopRunning();

                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);

                Say("Rozpakowywanie plików…", T.Tx3);
                Extract("NewsyVE.exe", Exe);
                try { Extract("README.md", Path.Combine(Dir, "README.md")); } catch { }

                Say("Tworzenie skrótów…", T.Tx3);
                Shortcut(Path.Combine(Programs, "NewsyVE.lnk"), Exe, "NewsyVE — pogoda, kursy i newsy");

                string desktop = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NewsyVE.lnk");
                if (cbDesktop.Checked) Shortcut(desktop, Exe, "NewsyVE");
                else if (File.Exists(desktop)) File.Delete(desktop);

                if (cbAutostart.Checked) Shortcut(AutostartLink, Exe, "NewsyVE");
                else if (File.Exists(AutostartLink)) File.Delete(AutostartLink);

                btnRemove.Enabled = true;
                btnInstall.Text = "Aktualizuj";
                Say("Gotowe. Aplikacja jest w menu Start jako „NewsyVE”.", T.Ok);

                if (cbRun.Checked)
                {
                    try { Process.Start(Exe); } catch { }
                }
            }
            catch (Exception ex)
            {
                Say("Błąd: " + ex.Message, T.Bad);
            }
            finally { btnInstall.Enabled = true; }
        }

        void Uninstall()
        {
            if (MessageBox.Show(this, "Usunąć NewsyVE razem z ustawieniami?", "NewsyVE",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                StopRunning();
                foreach (string p in new string[] {
                    Path.Combine(Programs, "NewsyVE.lnk"), AutostartLink,
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NewsyVE.lnk") })
                {
                    try { if (File.Exists(p)) File.Delete(p); } catch { }
                }
                if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
                btnRemove.Enabled = false;
                btnInstall.Text = "Zainstaluj";
                Say("Odinstalowano.", T.Tx2);
            }
            catch (Exception ex) { Say("Błąd: " + ex.Message, T.Bad); }
        }
    }
}
