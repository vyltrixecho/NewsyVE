using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

// Numer wersji dopisuje Build-Setup.ps1 (z src\AssemblyInfo.cs aplikacji),
// zeby instalator i aplikacja nie rozjechaly sie przy kolejnym wydaniu.
[assembly: AssemblyTitle("NewsyVE — instalator")]
[assembly: AssemblyCompany("VyltrixEcho")]
[assembly: AssemblyProduct("NewsyVE")]
[assembly: AssemblyCopyright("© 2026 Paweł Juszczyk (VyltrixEcho) · licencja MIT")]

namespace NewsyVESetup
{
    // Instalator NewsyVE: aplikacja i README siedza w zasobach tego pliku,
    // wiec calosc to jeden plik do uruchomienia.
    //
    //   NewsyVE-Setup.exe                 okno kreatora
    //   NewsyVE-Setup.exe /silent         instalacja bez okien (skrot na pulpicie, start po instalacji)
    //   NewsyVE-Setup.exe /uninstall      odinstalowanie - tak wola go "Aplikacje i funkcje"
    //   ... /uninstall /silent            odinstalowanie bez pytania
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            bool silent = false, uninstall = false, norun = false;
            foreach (string a in args)
            {
                string s = a.TrimStart('/', '-').ToLowerInvariant();
                if (s == "silent" || s == "s" || s == "verysilent" || s == "q") silent = true;
                else if (s == "uninstall") uninstall = true;
                else if (s == "norun") norun = true;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (uninstall) return Core.UninstallFromWindows(silent);
            if (silent)
            {
                try
                {
                    Core.Install(true, Core.IsAutostart(), !norun, null);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("NewsyVE: " + ex.Message);
                    return 1;
                }
            }
            Application.Run(new SetupForm());
            return 0;
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

    // Instalacja i odinstalowanie - wspolne dla okna, trybu cichego
    // i wywolania z "Aplikacje i funkcje".
    static class Core
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewsyVE");
        static readonly string Programs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs");
        static string Exe { get { return Path.Combine(Dir, "NewsyVE.exe"); } }
        // kopia instalatora obok aplikacji - na nia wskazuje wpis odinstalowania
        static string Uninstaller { get { return Path.Combine(Dir, "NewsyVE-Setup.exe"); } }
        static string DesktopLink
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NewsyVE.lnk");
            }
        }
        static string AutostartLink { get { return Path.Combine(Programs, @"Startup\NewsyVE.lnk"); } }
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NewsyVE";

        public static bool Installed() { return File.Exists(Exe); }
        public static bool IsAutostart() { return File.Exists(AutostartLink); }

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

        public static void Install(bool desktop, bool autostart, bool run, Action<string> say)
        {
            if (say == null) say = delegate { };
            say("Zatrzymywanie działającej kopii…");
            StopRunning();

            if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);

            say("Rozpakowywanie plików…");
            Extract("NewsyVE.exe", Exe);
            try { Extract("README.md", Path.Combine(Dir, "README.md")); } catch { }

            // instalator uruchomiony z katalogu aplikacji (z "Aplikacje i funkcje")
            // nie moze nadpisac sam siebie - i nie musi
            string self = Assembly.GetExecutingAssembly().Location;
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(Uninstaller),
                    StringComparison.OrdinalIgnoreCase))
                File.Copy(self, Uninstaller, true);

            say("Tworzenie skrótów…");
            Shortcut(Path.Combine(Programs, "NewsyVE.lnk"), Exe, "NewsyVE — pogoda, newsy i sport");

            if (desktop) Shortcut(DesktopLink, Exe, "NewsyVE");
            else if (File.Exists(DesktopLink)) File.Delete(DesktopLink);

            if (autostart) Shortcut(AutostartLink, Exe, "NewsyVE");
            else if (File.Exists(AutostartLink)) File.Delete(AutostartLink);

            Register();

            if (run)
            {
                try { Process.Start(Exe); } catch { }
            }
        }

        // Wpis w "Aplikacje i funkcje" (HKCU - bez uprawnien administratora).
        static void Register()
        {
            string ver = FileVersionInfo.GetVersionInfo(Exe).FileVersion ?? "";
            Version v;
            if (Version.TryParse(ver, out v)) ver = v.ToString(3);
            long kb = 0;
            foreach (string f in Directory.GetFiles(Dir)) kb += new FileInfo(f).Length / 1024;

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "NewsyVE");
                k.SetValue("DisplayVersion", ver);
                k.SetValue("Publisher", "VyltrixEcho");
                k.SetValue("DisplayIcon", Exe + ",0");
                k.SetValue("InstallLocation", Dir);
                k.SetValue("UninstallString", "\"" + Uninstaller + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + Uninstaller + "\" /uninstall /silent");
                k.SetValue("URLInfoAbout", "https://github.com/vyltrixecho/NewsyVE");
                k.SetValue("URLUpdateInfo", "https://github.com/vyltrixecho/NewsyVE/releases");
                k.SetValue("EstimatedSize", (int)kb, RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        public static void Uninstall()
        {
            StopRunning();
            foreach (string p in new string[] {
                Path.Combine(Programs, "NewsyVE.lnk"), AutostartLink, DesktopLink })
            {
                try { if (File.Exists(p)) File.Delete(p); } catch { }
            }
            // stary wpis autostartu z ustawien aplikacji (klucz Run)
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    if (run != null && run.GetValue("NewsyVE") != null) run.DeleteValue("NewsyVE", false);
            }
            catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
        }

        // "Aplikacje i funkcje" uruchamia kopie instalatora z katalogu aplikacji,
        // a dzialajacy plik nie moze skasowac sam siebie ani swojego katalogu.
        // Dlatego najpierw przenosimy sie do katalogu tymczasowego.
        public static int UninstallFromWindows(bool silent)
        {
            string self = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
            if (self.StartsWith(Path.GetFullPath(Dir) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                string tmp = Path.Combine(Path.GetTempPath(), "NewsyVE-uninstall.exe");
                File.Copy(self, tmp, true);
                Process.Start(tmp, "/uninstall" + (silent ? " /silent" : ""));
                return 0;
            }

            if (!silent && MessageBox.Show("Usunąć NewsyVE razem z ustawieniami?", "NewsyVE",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 1;
            try
            {
                Uninstall();
                if (!silent)
                    MessageBox.Show("NewsyVE zostało odinstalowane.", "NewsyVE",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                if (!silent)
                    MessageBox.Show("Błąd: " + ex.Message, "NewsyVE",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            finally { CleanupSelf(self); }
        }

        // kopia z katalogu tymczasowego sprzata po sobie, gdy juz sie zamknie
        static void CleanupSelf(string self)
        {
            if (!self.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                ProcessStartInfo si = new ProcessStartInfo("cmd.exe",
                    "/c ping 127.0.0.1 -n 3 >nul & del /f /q \"" + self + "\"");
                si.CreateNoWindow = true;
                si.UseShellExecute = false;
                Process.Start(si);
            }
            catch { }
        }
    }

    class SetupForm : Form
    {
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

            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            Label ver = new Label();
            ver.Text = "wersja " + v.ToString(3);
            ver.Font = T.F(11);
            ver.ForeColor = T.Tx3;
            ver.TextAlign = ContentAlignment.MiddleRight;
            ver.SetBounds(300, 32, 134, 18);

            Label sub = new Label();
            sub.Text = "Pogoda, newsy, kursy i sport na pasku zadań.\n" +
                       "Jedna aplikacja, bez przeglądarki w tle.";
            sub.Font = T.F(12);
            sub.ForeColor = T.Tx2;
            sub.SetBounds(26, 58, 420, 40);

            Label where = new Label();
            where.Text = "Katalog: " + Core.Dir;
            where.Font = T.F(11);
            where.ForeColor = T.Tx3;
            where.SetBounds(26, 104, 420, 18);

            cbDesktop.Text = "Skrót na pulpicie";
            cbDesktop.Checked = true;
            Style(cbDesktop, 140);

            cbAutostart.Text = "Uruchamiaj razem z Windows";
            cbAutostart.Checked = Core.IsAutostart();
            Style(cbAutostart, 168);

            cbRun.Text = "Uruchom po instalacji";
            cbRun.Checked = true;
            Style(cbRun, 196);

            btnInstall.Text = Core.Installed() ? "Aktualizuj" : "Zainstaluj";
            btnInstall.Tint = T.Acc;
            btnInstall.SetBounds(26, 238, 200, 36);
            btnInstall.Click += delegate { Install(); };

            btnRemove.Text = "Odinstaluj";
            btnRemove.Tint = T.Line;
            btnRemove.ForeColor = T.Tx2;
            btnRemove.SetBounds(238, 238, 130, 36);
            btnRemove.Enabled = Core.Installed();
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
                title, ver, sub, where, cbDesktop, cbAutostart, cbRun,
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

        void Say(string text, Color c)
        {
            status.ForeColor = c;
            status.Text = text;
            status.Refresh();
        }

        void Install()
        {
            try
            {
                btnInstall.Enabled = false;
                Core.Install(cbDesktop.Checked, cbAutostart.Checked, cbRun.Checked,
                    delegate (string s) { Say(s, T.Tx3); });
                btnRemove.Enabled = true;
                btnInstall.Text = "Aktualizuj";
                Say("Gotowe. Aplikacja jest w menu Start jako „NewsyVE”.", T.Ok);
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
                Core.Uninstall();
                btnRemove.Enabled = false;
                btnInstall.Text = "Zainstaluj";
                Say("Odinstalowano.", T.Tx2);
            }
            catch (Exception ex) { Say("Błąd: " + ex.Message, T.Bad); }
        }
    }
}
