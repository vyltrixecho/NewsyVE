using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;

namespace NewsyVE
{
    // Wspolne dane dla paska zadan i panelu - jeden pobor, dwa widoki.
    static class Store
    {
        public static PlaceData[] Data = new PlaceData[0];
        public static List<WarnItem> Warns = new List<WarnItem>();
        public static DateTime Stamp = DateTime.MinValue;
        public static bool Online = true;
        public static int Active = 0;          // miejscowosc pokazywana w panelu

        public static Image Icm;
        public static int IcmFor = -1;

        public static event Action Changed;
        public static event Action RainAlert;

        static int busy;
        static int busyMkt;
        static int fails;
        static int busyTransit, busyNews, busySport, busyCups;
        static int busyLocal, localAgain, transitAgain;

        // Czy cokolwiek jest wlasnie pobierane - panel pokazuje wtedy
        // "odswiezanie...", zeby klikniecie nie wygladalo na nieskuteczne.
        // Ustawiane przy starcie - pozwala otworzyc okno miejscowosci
        // wprost z panelu, bez szukania w menu zasobnika.
        public static Action OpenPlaces;

        public static bool Busy
        {
            get
            {
                return busy != 0 || busyMkt != 0 || busyTransit != 0 ||
                       busyNews != 0 || busySport != 0 || busyLocal != 0 || busyCups != 0;
            }
        }
        static System.Threading.Timer retry;
        static DateTime lastAlert = DateTime.MinValue;

        public static void Fire()
        {
            Action a = Changed;
            if (a != null) a();
        }

        public static PlaceData Cur
        {
            get { return (Active >= 0 && Active < Data.Length) ? Data[Active] : null; }
        }
        public static Place CurPlace
        {
            get { return Cfg.Places[Math.Min(Active, Cfg.Places.Count - 1)]; }
        }

        // Najblizsza stacja synoptyczna liczona raz - lista IMGW nie ma
        // wspolrzednych, wiec Geo dogeokodowuje ja i trzyma w pliku.
        static bool stationsReady;
        public static void ResolveStations()
        {
            try
            {
                if (!stationsReady) { Geo.EnsureStations(); stationsReady = true; }
                foreach (Place p in Cfg.Places)
                {
                    if (p.Imgw != null) continue;
                    Geo.Station st = Geo.NearestStation(p.Lat, p.Lon, 60);
                    if (st == null) { p.Imgw = ""; p.ImgwName = ""; continue; }
                    p.Imgw = st.Id; p.ImgwName = st.Name;
                    p.ImgwKm = Geo.StationKm(st, p.Lat, p.Lon);
                }
            }
            catch { }
        }

        public static void Refresh(bool withIcm)
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ResolveStations();
                    PlaceData[] d = Api.Forecast(Cfg.Places);
                    for (int i = 0; i < d.Length && i < Cfg.Places.Count; i++)
                        if (d[i] != null) Api.ImgwStation(Cfg.Places[i], d[i]);
                    try { Api.AirQuality(Cfg.Places, d); } catch { }
                    Data = d;
                    Online = true;
                    Stamp = DateTime.Now;
                    fails = 0;

                    try { Warns = Api.Warnings(CurPlace); } catch { Warns = new List<WarnItem>(); }

                    PlaceData bar = Data.Length > Cfg.BarPlace ? Data[Cfg.BarPlace] : null;
                    if (bar != null)
                    {
                        DateTime? r = FirstRain(bar);
                        if (r.HasValue && (DateTime.Now - lastAlert).TotalMinutes > 90)
                        {
                            lastAlert = DateTime.Now;
                            Action a = RainAlert;
                            if (a != null) a();
                        }
                    }
                }
                catch (Exception ex) { Online = false; Program.Log("Pogoda", ex); ScheduleRetry(); }
                finally { Interlocked.Exchange(ref busy, 0); }

                Fire();
                if (withIcm) LoadIcm();
            });
        }

        // Po nieudanym pobraniu nie czekamy pelnych 10 minut - probujemy
        // po 10, 20, 30... sekundach, maksymalnie co minute.
        static void ScheduleRetry()
        {
            fails++;
            if (fails > 8) return;
            int delay = Math.Min(60, 10 * fails) * 1000;
            System.Threading.TimerCallback cb = delegate { Refresh(false); };
            if (retry == null) retry = new System.Threading.Timer(cb, null, delay, System.Threading.Timeout.Infinite);
            else retry.Change(delay, System.Threading.Timeout.Infinite);
        }

        public static void RefreshMarkets(bool force)
        {
            if (Interlocked.CompareExchange(ref busyMkt, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { NewsyVE.Markets.Refresh(force); }
                catch (Exception ex) { Program.Log("Rynki", ex); }
                finally { Interlocked.Exchange(ref busyMkt, 0); }
                Fire();
            });
        }

        // Odjazdy sa danymi czasu rzeczywistego - odswiezamy je tylko wtedy,
        // gdy panel jest widoczny; inaczej odpytywalibysmy ZTP bez potrzeby.
        public static void RefreshTransit()
        {
            if (Interlocked.CompareExchange(ref busyTransit, 1, 0) != 0)
            {
                Interlocked.Exchange(ref transitAgain, 1);
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    do
                    {
                        Interlocked.Exchange(ref transitAgain, 0);
                        try { Transit.Refresh(); }
                        catch (Exception ex) { Program.Log("Odjazdy", ex); }
                    }
                    while (Interlocked.CompareExchange(ref transitAgain, 0, 1) == 1);
                }
                finally { Interlocked.Exchange(ref busyTransit, 0); }
                Fire();
            });
        }

        // Zmiana miasta dotyczy tylko karty lokalnej - sport, polityka, AI
        // i gry sa te same wszedzie, wiec nie ma po co ich przeladowywac.
        // Gdy pobieranie juz trwa, zapamietujemy prosbe i powtarzamy je zaraz
        // po zakonczeniu, zeby przelaczenie nigdy nie przepadlo.
        public static void RefreshLocalNews()
        {
            if (Interlocked.CompareExchange(ref busyLocal, 1, 0) != 0)
            {
                Interlocked.Exchange(ref localAgain, 1);
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    do
                    {
                        Interlocked.Exchange(ref localAgain, 0);
                        try { News.Refresh(); }
                        catch (Exception ex) { Program.Log("Newsy", ex); }
                    }
                    while (Interlocked.CompareExchange(ref localAgain, 0, 1) == 1);
                }
                finally { Interlocked.Exchange(ref busyLocal, 0); }
                Fire();
            });
        }

        public static void RefreshNews()
        {
            if (Interlocked.CompareExchange(ref busyNews, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { News.Refresh(); }
                catch (Exception ex) { Program.Log("Newsy", ex); }
                try { News.RefreshSport(); }
                catch (Exception ex) { Program.Log("NewsySport", ex); }
                try { News.RefreshPolitics(); }
                catch (Exception ex) { Program.Log("NewsyPolityka", ex); }
                try { News.RefreshGames(); }
                catch (Exception ex) { Program.Log("NewsyGry", ex); }
                try { News.RefreshAi(); }
                catch (Exception ex) { Program.Log("NewsyAI", ex); }
                finally { Interlocked.Exchange(ref busyNews, 0); }
                Fire();
            });
        }

        public static void RefreshSport()
        {
            if (Interlocked.CompareExchange(ref busySport, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (Cfg.SportTab)
                    {
                        try { Sport.Refresh(); }
                        catch (Exception ex) { Program.Log("Sport", ex); }
                        Fire();
                        RunCups();
                        try { News.RefreshCups(); }
                        catch (Exception ex) { Program.Log("NewsyPuchary", ex); }
                    }
                }
                finally { Interlocked.Exchange(ref busySport, 0); }
                Fire();
            });
        }

        // Same wyniki pucharow i kadry - w trakcie meczu co dwie minuty,
        // bez przeladowywania tabeli Ekstraklasy i newsow.
        public static void RefreshCups()
        {
            if (!Cfg.SportTab) return;
            ThreadPool.QueueUserWorkItem(delegate { RunCups(); Fire(); });
        }

        static void RunCups()
        {
            if (Interlocked.CompareExchange(ref busyCups, 1, 0) != 0) return;
            try { Cups.Refresh(); }
            catch (Exception ex) { Program.Log("Puchary", ex); }
            finally { Interlocked.Exchange(ref busyCups, 0); }
        }

        public static DateTime? FirstRain(PlaceData d)
        {
            if (d == null) return null;
            for (int i = 0; i < d.M15.Length && i < d.M15T.Length; i++)
                if (d.M15[i] > 0.02) return d.M15T[i];
            return null;
        }

        // ICM generuje meteorogramy dla wybranych wezlow; gdy trafiony punkt
        // zwraca pasek "brak danych", siegamy po sasiedni (najwyzej 4 km dalej).
        public static void LoadIcm()
        {
            int want = Active;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (want >= Cfg.Places.Count) return;
                    Place p = Cfg.Places[want];
                    int[][] around = {
                        new int[]{0,0}, new int[]{0,1}, new int[]{1,0}, new int[]{0,-1}, new int[]{-1,0},
                        new int[]{1,1}, new int[]{-1,-1}, new int[]{1,-1}, new int[]{-1,1}
                    };
                    foreach (int[] d in around)
                    {
                        int row = p.IcmRow + d[0], col = p.IcmCol + d[1];
                        try
                        {
                            byte[] b = Api.FetchBytes(Cfg.IcmImage(row, col));
                            using (MemoryStream ms = new MemoryStream(b))
                            using (Image raw = Image.FromStream(ms))
                            {
                                if (raw.Height < 300) continue;      // pasek "brak danych"
                                int top = (int)(raw.Height * 0.152);
                                Rectangle src = new Rectangle(0, top, raw.Width, raw.Height - top);
                                Bitmap crop = new Bitmap(src.Width, src.Height);
                                using (Graphics g = Graphics.FromImage(crop))
                                    g.DrawImage(raw, new Rectangle(0, 0, src.Width, src.Height), src, GraphicsUnit.Pixel);
                                Image old2 = Icm;
                                Icm = crop;
                                IcmFor = want;
                                p.IcmRow = row; p.IcmCol = col;
                                if (old2 != null) old2.Dispose();
                            }
                            Fire();
                            return;
                        }
                        catch { }
                    }
                    IcmFor = -2;                                     // nie ma czego pokazac
                    Fire();
                }
                catch { }
            });
        }
    }
}
