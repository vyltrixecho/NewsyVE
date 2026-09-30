using System;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace NewsyVE
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (Mutex m = new Mutex(true, "Local\\NewsyVE-App", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11;
                System.Net.ServicePointManager.DefaultConnectionLimit = 16;
                Cfg.Load();
                Images.Prune();
                Application.ThreadException += delegate (object s2, System.Threading.ThreadExceptionEventArgs a) { Log("UI", a.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s2, UnhandledExceptionEventArgs a) { Log("APP", a.ExceptionObject as Exception); };
                Note("start");
                Application.ApplicationExit += delegate { Note("koniec - zamkniecie aplikacji"); };
                Microsoft.Win32.SystemEvents.SessionEnding += delegate (object s3,
                    Microsoft.Win32.SessionEndingEventArgs a3)
                { Note("koniec - Windows konczy sesje (" + a3.Reason + ")"); };

                Application.Run(new AppContext());
                Note("koniec - petla komunikatow zakonczona");
            }
        }

        // Gdy widget znika bez sladu, to jedyny zapis pozwalajacy stwierdzic,
        // czy proces zostal zamkniety, czy padl.
        public static void Note(string what)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(Cfg.AppDir, "newsyve-zycie.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + what +
                    Environment.NewLine);
            }
            catch { }
        }

        public static void Log(string ctx, Exception ex)
        {
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(Cfg.AppDir, "newsyve-error.log"),
                    DateTime.Now.ToString("HH:mm:ss") + "  " + ctx + ": " +
                    (ex == null ? "?" : ex.ToString()) + Environment.NewLine);
            }
            catch { }
        }
    }

    class AppContext : ApplicationContext
    {
        readonly NotifyIcon tray = new NotifyIcon();
        readonly BarForm bar = new BarForm();
        readonly PanelForm panel = new PanelForm();
        readonly System.Windows.Forms.Timer dataTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer posTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer radarTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer mktTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer newsTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer depTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer sportTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer cupsTimer = new System.Windows.Forms.Timer();
        IntPtr iconHandle = IntPtr.Zero;
        ToolStripMenuItem[] miPlace;
        ToolStripMenuItem miLeft, miRight, miOff;
        ToolStripMenuItem[] miText;

        public AppContext()
        {
            BuildTray();

            bar.Clicked += TogglePanel;
            bar.RightClicked += delegate (Point p)
            {
                Native.ForceForeground(bar.Handle);   // inaczej menu zamyka sie natychmiast
                tray.ContextMenuStrip.Show(p);
            };

            Store.Changed += delegate
            {
                if (bar.IsHandleCreated)
                    bar.BeginInvoke((Action)delegate { bar.Invalidate(); bar.Reposition(); UpdateTip(); });
            };
            Store.RainAlert += delegate
            {
                if (bar.IsHandleCreated) bar.BeginInvoke((Action)ShowRainBalloon);
            };

            dataTimer.Interval = Cfg.WxMin * 60 * 1000;
            dataTimer.Tick += delegate { Store.Refresh(panel.Visible); };
            dataTimer.Start();

            posTimer.Interval = 2000;
            posTimer.Tick += delegate { bar.Reposition(); };
            posTimer.Start();

            radarTimer.Interval = 2 * 60 * 1000;
            radarTimer.Tick += delegate
            {
                ThreadPool.QueueUserWorkItem(delegate { panel.Radar.LoadFrames(); panel.Radar.PrefetchAll(); });
            };
            radarTimer.Start();

            // raz na dobe: co godzine sprawdzamy tylko, czy zmienil sie dzien
            NewsyVE.Markets.LoadCache();
            mktTimer.Interval = 60 * 60 * 1000;
            mktTimer.Tick += delegate { Store.RefreshMarkets(false); };
            mktTimer.Start();

            newsTimer.Interval = Cfg.NewsMin * 60 * 1000;
            newsTimer.Tick += delegate { Store.RefreshNews(); };
            newsTimer.Start();

            depTimer.Interval = 30 * 1000;
            depTimer.Tick += delegate { if (panel.Visible) Store.RefreshTransit(); };
            depTimer.Start();

            sportTimer.Interval = 30 * 60 * 1000;
            sportTimer.Tick += delegate { Store.RefreshSport(); };
            sportTimer.Start();

            // w trakcie meczu kadry albo polskiego klubu wynik ma byc swiezy,
            // ale tylko gdy ktos na niego patrzy
            cupsTimer.Interval = 60 * 1000;
            cupsTimer.Tick += delegate
            {
                if (panel.Visible && Cups.LiveWindow() && (DateTime.Now - Cups.Stamp).TotalSeconds > 110)
                    Store.RefreshCups();
            };
            cupsTimer.Start();

            bar.Show();
            bar.Reposition();
            HookPlaces();
            if (!Cfg.Configured) FirstRun();

            Store.Refresh(false);
            Store.RefreshMarkets(false);
            Store.RefreshNews();
            Store.RefreshTransit();
            Store.RefreshSport();
            ThreadPool.QueueUserWorkItem(delegate { panel.Radar.LoadFrames(); panel.Radar.PrefetchAll(); });
        }

        void BuildTray()
        {
            Bitmap bmp = Art.AppIcon(Math.Max(16, SystemInformation.SmallIconSize.Width));
            tray.Icon = Art.IconFrom(bmp, out iconHandle);
            bmp.Dispose();
            tray.Visible = true;
            tray.Text = "NewsyVE";
            tray.MouseClick += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) TogglePanel();
            };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = new ToolStripProfessionalRenderer();
            menu.Items.Add("Pokaż widget", null, delegate { TogglePanel(); });
            menu.Items.Add("Ustawienia…", null, delegate { OpenSettings(); });
            menu.Items.Add("Odśwież teraz", null, delegate { Store.Refresh(true); Store.RefreshMarkets(true); Store.RefreshNews(); Store.RefreshTransit(); Store.RefreshSport(); });
            menu.Items.Add(new ToolStripSeparator());

            placeMenu = menu;
            placeAt = menu.Items.Count;
            BuildPlaceMenu();
            menu.Items.Add(new ToolStripSeparator());
            miLeft = new ToolStripMenuItem("Pasek po lewej", null, delegate { SetSide("Left"); });
            miRight = new ToolStripMenuItem("Pasek po prawej", null, delegate { SetSide("Right"); });
            miOff = new ToolStripMenuItem("Ukryj pasek (tylko zasobnik)", null, delegate { SetSide("Off"); });
            menu.Items.Add(miLeft); menu.Items.Add(miRight); menu.Items.Add(miOff);

            ToolStripMenuItem sub = new ToolStripMenuItem("Siła napisu");
            string[] names = { "1 — najdelikatniejszy", "2", "3 — średni", "4", "5 — najmocniejszy" };
            miText = new ToolStripMenuItem[5];
            for (int i = 0; i < 5; i++)
            {
                int lvl = i + 1;
                miText[i] = new ToolStripMenuItem(names[i], null, delegate
                {
                    Cfg.TextLevel = lvl; Cfg.Save(); SyncMenu();
                    bar.Invalidate(); bar.Reposition();
                });
                sub.DropDownItems.Add(miText[i]);
            }
            menu.Items.Add(sub);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("meteo.pl — meteorogram ICM", null, delegate {
                Open(Cfg.IcmPage(Cfg.Places[Cfg.BarPlace])); });
            menu.Items.Add("AccuWeather — radar", null, delegate {
                Open("https://www.accuweather.com/pl/pl/krakow/274455/weather-radar/274455"); });
            menu.Items.Add("iradar.app", null, delegate {
                Place p = Cfg.Places[Cfg.BarPlace];
                Open("https://iradar.app/#map=" +
                     p.Lat.ToString("0.0000", CultureInfo.InvariantCulture) + "/" +
                     p.Lon.ToString("0.0000", CultureInfo.InvariantCulture) + "/7.52/c/czcomp/DBZH"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Zakończ", null, delegate { Quit(); });

            tray.ContextMenuStrip = menu;
            SyncMenu();
        }

        static void Open(string url)
        {
            try { System.Diagnostics.Process.Start(url); } catch { }
        }

        // Przy pierwszym starcie proponujemy miejscowosc z IP i od razu
        // pokazujemy okno wyboru - IP potrafi sie mylic o setki kilometrow.
        void FirstRun()
        {
            try
            {
                GeoHit h = Geo.ByIp();
                if (h != null)
                {
                    Place p = new Place();
                    p.Name = h.Name; p.Lat = h.Lat; p.Lon = h.Lon; p.Region = h.Region;
                    p.Recalc();
                    Cfg.Places.Clear();
                    Cfg.Places.Add(p);
                }
            }
            catch { }
            Cfg.Save();
            OpenSettings();
        }

        void HookPlaces() { Store.OpenPlaces = delegate { OpenSettings(); }; }

        SettingsForm settings;

        void OpenSettings()
        {
            // menu zasobnika dziala takze przy otwartym oknie ustawien -
            // drugie okno nadpisaloby zmiany pierwszego
            if (settings != null) { settings.Activate(); return; }
            using (SettingsForm f = new SettingsForm())
            {
                settings = f;
                f.Live = delegate
                {
                    bar.Visible = Cfg.Side != "Off";
                    bar.Invalidate();
                    bar.Reposition();
                };
                try { f.ShowDialog(); }
                finally { settings = null; }
                SyncMenu();
                if (!f.Applied) return;
            }
            Store.Data = new PlaceData[0];
            Store.Active = 0;
            foreach (Place p in Cfg.Places) p.Imgw = null;      // stacje policzymy od nowa
            Store.Icm = null; Store.IcmFor = -1;
            BuildPlaceMenu();
            SyncMenu();
            dataTimer.Interval = Cfg.WxMin * 60 * 1000;
            newsTimer.Interval = Cfg.NewsMin * 60 * 1000;
            panel.Rebuild();
            Store.Refresh(true);
            Store.RefreshNews();
            Store.RefreshTransit();
            Store.RefreshSport();                 // kluby mogly sie zmienic
            bar.Visible = Cfg.Side != "Off";
            bar.Invalidate(); bar.Reposition();
        }

        void SetSide(string s)
        {
            Cfg.Side = s; Cfg.Save(); SyncMenu();
            if (s == "Off") bar.Hide(); else bar.Reposition();
        }

        ContextMenuStrip placeMenu;
        int placeAt;

        void BuildPlaceMenu()
        {
            if (placeMenu == null) return;
            if (miPlace != null)
                foreach (ToolStripMenuItem it in miPlace)
                    if (placeMenu.Items.Contains(it)) placeMenu.Items.Remove(it);

            miPlace = new ToolStripMenuItem[Cfg.Places.Count];
            for (int i = 0; i < Cfg.Places.Count; i++)
            {
                int idx = i;
                miPlace[i] = new ToolStripMenuItem("Pokazuj: " + Cfg.Places[i].Name, null, delegate
                {
                    Cfg.BarPlace = idx; Cfg.Save(); SyncMenu();
                    bar.Invalidate(); bar.Reposition(); UpdateTip();
                });
                placeMenu.Items.Insert(placeAt + i, miPlace[i]);
            }
            SyncMenu();
        }

        void SyncMenu()
        {
            // SyncMenu bywa wolane z BuildPlaceMenu, zanim powstana pozostale
            // pozycje menu - stad ostrozne sprawdzanie kazdej z nich.
            if (miPlace != null)
                for (int i = 0; i < miPlace.Length; i++)
                    if (miPlace[i] != null) miPlace[i].Checked = (i == Cfg.BarPlace);
            if (miLeft != null) miLeft.Checked = Cfg.Side == "Left";
            if (miRight != null) miRight.Checked = Cfg.Side == "Right";
            if (miOff != null) miOff.Checked = Cfg.Side == "Off";
            if (miText != null)
                for (int i = 0; i < miText.Length; i++) miText[i].Checked = (i + 1 == Cfg.TextLevel);
        }

        void UpdateTip()
        {
            PlaceData d = Store.Data.Length > Cfg.BarPlace ? Store.Data[Cfg.BarPlace] : null;
            string nm = Cfg.Places[Cfg.BarPlace].Name;
            if (d == null || d.Cur == null || !Store.Online)
            {
                tray.Text = "NewsyVE — brak połączenia";
                return;
            }
            string t = nm + " " + Math.Round(d.Cur.Temp).ToString(CultureInfo.InvariantCulture) +
                       "°C · " + Wmo.Text(d.Cur.Code);
            if (t.Length > 62) t = t.Substring(0, 62);
            tray.Text = t;
        }

        void ShowRainBalloon()
        {
            PlaceData d = Store.Data.Length > Cfg.BarPlace ? Store.Data[Cfg.BarPlace] : null;
            DateTime? r = Store.FirstRain(d);
            if (!r.HasValue) return;
            tray.BalloonTipTitle = "NewsyVE — nadchodzą opady";
            tray.BalloonTipText = Cfg.Places[Cfg.BarPlace].Name + ": około " +
                                  r.Value.ToString("HH:mm", CultureInfo.InvariantCulture) + ".";
            tray.ShowBalloonTip(8000);
        }

        void TogglePanel()
        {
            try
            {
                // panel chowa sie sam przy utracie fokusu; bez tej przerwy
                // klik w pasek natychmiast otwieralby go z powrotem
                if (!panel.Visible && (DateTime.Now - panel.HiddenAt).TotalMilliseconds < 250) return;
                if (panel.Visible) panel.HidePanel();
                else panel.ShowAt();
            }
            catch (Exception ex) { Program.Log("Toggle", ex); }
        }

        void Quit()
        {
            tray.Visible = false;
            if (iconHandle != IntPtr.Zero) Native.DestroyIcon(iconHandle);
            tray.Dispose();
            ExitThread();
        }
    }
}
