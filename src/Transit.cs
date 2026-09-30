using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NewsyVE
{
    class Departure
    {
        public string Line;        // numer linii
        public string Direction;   // kierunek
        public string Stop;        // z ktorego przystanku
        public string Time;        // godzina odjazdu
        public int Seconds;        // ile sekund do odjazdu
        public bool Live;          // z pomiaru pojazdu, a nie z rozkladu
        public bool Delayed;       // rozjazd z rozkladem
        public bool Fav;           // linia z listy ulubionych tej miejscowosci
    }

    // Najblizsze odjazdy dla dowolnej miejscowosci.
    //
    // Kazde miasto ma wlasnego przewoznika i wlasne API (Krakow - TTSS,
    // Warszawa - api.um.warszawa.pl z kluczem, Gdansk - ZTM, i tak dalej),
    // wiec zamiast zszywac kilkanascie osobnych integracji korzystamy
    // z Transitous: spolecznego agregatora rozkladow GTFS obejmujacego
    // Polske i Europe, bez klucza API. Tam, gdzie przewoznik publikuje
    // dane czasu rzeczywistego, dostajemy je razem z rozkladem.
    static class Transit
    {
        public class StopRef
        {
            public string Id, Name;
            public double Lat, Lon, Km;
        }

        public static List<Departure> Next = new List<Departure>();
        public static DateTime Stamp = DateTime.MinValue;
        public static bool Ok;
        public static string Where = "";      // nazwa najblizszego przystanku
        public static string Sub = "";        // podpis: z czego zlozona jest lista

        public static int FavCount;           // ile pozycji z Next to ulubione (sa na poczatku)

        const string Api = "https://api.transitous.org/api/v1/";
        const int MaxStops = 4;               // tyle zapytan na odswiezenie
        const int PerStop = 8;                // tyle odjazdow z kazdego przystanku
        // Z ulubionymi liniami pytamy szerzej: rzadko jezdzacy autobus nie
        // zmiescilby sie w 8 najblizszych odjazdach, a moze nie stawac
        // na samym najblizszym przystanku.
        const int FavStops = 8, FavPerStop = 30;

        static readonly List<StopRef> stops = new List<StopRef>();
        static double forLat = 999, forLon = 999;

        public static void Refresh()
        {
            Place p = Store.CurPlace;
            if (p == null) return;

            if (Math.Abs(p.Lat - forLat) > 0.0005 || Math.Abs(p.Lon - forLon) > 0.0005)
            {
                // odjazdy poprzedniej miejscowosci sa juz nieaktualne - lepiej
                // pokazac "wczytywanie" niz cudzy rozklad
                Next.Clear();
                Ok = false;
                Store.Fire();

                ResolveStops(p);
                forLat = p.Lat; forLon = p.Lon;
            }

            if (stops.Count == 0)
            {
                Next.Clear(); Ok = true;
                Where = p.Name;
                Sub = "brak przystanków w pobliżu";
                Stamp = DateTime.Now;
                return;
            }

            List<Departure> all = new List<Departure>();
            bool any = false;
            bool favs = p.Lines.Count > 0;
            int nStops = favs ? FavStops : MaxStops;
            int perStop = favs ? FavPerStop : PerStop;

            for (int si = 0; si < stops.Count && si < nStops; si++)
            {
                StopRef s = stops[si];
                try
                {
                    object root = Json.Parse(Api2.Get(Api + "stoptimes?stopId=" +
                        Uri.EscapeDataString(s.Id) + "&n=" +
                        perStop.ToString(CultureInfo.InvariantCulture)));

                    foreach (object t in Json.Arr(Json.At(root, "stopTimes")))
                    {
                        object pl = Json.At(t, "place");
                        DateTime dep = Iso(Json.Str(Json.At(pl, "departure")));
                        if (dep == DateTime.MinValue) continue;

                        Departure d = new Departure();
                        d.Line = Json.Str(Json.At(t, "routeShortName"));
                        if (d.Line.Length == 0) d.Line = Json.Str(Json.At(t, "mode"));
                        d.Direction = Json.Str(Json.At(t, "headsign"));
                        d.Stop = s.Name;
                        d.Time = dep.ToString("HH:mm", CultureInfo.InvariantCulture);
                        d.Seconds = (int)(dep - DateTime.Now).TotalSeconds;
                        d.Live = Json.At(t, "realTime") as bool? == true;

                        DateTime sch = Iso(Json.Str(Json.At(pl, "scheduledDeparture")));
                        d.Delayed = d.Live && sch != DateTime.MinValue &&
                                    Math.Abs((dep - sch).TotalSeconds) >= 60;
                        d.Fav = p.IsFav(d.Line);

                        // kursy, ktore juz odjechaly, nikogo nie interesuja
                        if (d.Seconds > -60 && d.Line.Length > 0) all.Add(d);
                    }
                    any = true;
                }
                catch (Exception ex) { Program.Log("Transit", ex); }
            }

            if (!any) { Ok = false; return; }

            all.Sort(delegate (Departure a, Departure b) { return a.Seconds.CompareTo(b.Seconds); });

            // ten sam kurs potrafi przyjsc z dwoch peronow jednego przystanku
            List<Departure> uniq = new List<Departure>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            foreach (Departure d in all)
            {
                string key = d.Line + "|" + d.Direction + "|" + d.Time + "|" + d.Stop;
                if (seen.ContainsKey(key)) continue;
                seen[key] = true;
                uniq.Add(d);
            }

            // ulubione na gorze, pozostale pod nimi - obie grupy wg czasu
            List<Departure> fav = uniq.FindAll(delegate (Departure d) { return d.Fav; });
            List<Departure> rest = uniq.FindAll(delegate (Departure d) { return !d.Fav; });
            // bez ulubionych dalej obowiazuje dawny limit - szersze zapytanie
            // robimy tylko po to, zeby je znalezc
            if (favs && rest.Count > MaxStops * PerStop) rest.RemoveRange(MaxStops * PerStop, rest.Count - MaxStops * PerStop);
            fav.AddRange(rest);
            FavCount = favs ? uniq.FindAll(delegate (Departure d) { return d.Fav; }).Count : 0;

            if (favs)
            {
                // "ulubione: 4, 52 · Rondo Mogilskie · ..." - zeby bylo widac, czego szukamy
                string head = "ulubione: " + string.Join(", ", p.Lines.ToArray());
                string baseSub = SubFor(Math.Min(stops.Count, nStops));
                Sub = head + " · " + baseSub;
            }
            else Sub = SubFor(Math.Min(stops.Count, MaxStops));

            Next = fav;
            Stamp = DateTime.Now;
            Ok = true;
        }

        // Przystanki szukane sa w kwadracie wokol miejscowosci; jesli nic nie ma,
        // kwadrat rosnie - inaczej wies bez przystanku w centrum wygladalaby
        // na miejsce bez zadnej komunikacji.
        static void ResolveStops(Place p)
        {
            stops.Clear();
            Where = p.Name; Sub = "";

            double[] spans = { 0.006, 0.014, 0.030 };
            List<StopRef> found = new List<StopRef>();

            foreach (double d in spans)
            {
                found = Query(p.Lat, p.Lon, d);
                if (found.Count > 0) break;
            }
            if (found.Count == 0) return;

            found.Sort(delegate (StopRef a, StopRef b) { return a.Km.CompareTo(b.Km); });

            // rozne perony tego samego przystanku to rozne kierunki, wiec
            // zostawiamy je obok siebie; ile z nich pytac, decyduje Refresh
            for (int i = 0; i < found.Count && stops.Count < FavStops; i++)
                stops.Add(found[i]);

            Where = stops[0].Name;
            Sub = SubFor(Math.Min(stops.Count, MaxStops));
        }

        // podpis pod naglowkiem: nazwy pytanych przystankow i odleglosc
        static string SubFor(int n)
        {
            if (stops.Count == 0) return "";
            List<string> names = new List<string>();
            for (int i = 0; i < n && i < stops.Count; i++)
                if (!names.Contains(stops[i].Name)) names.Add(stops[i].Name);

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < names.Count && i < 3; i++)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(names[i]);
            }
            sb.Append("  ");
            sb.Append(Dist(stops[0].Km));
            return sb.ToString();
        }

        // Podpowiedz do ustawien: jakie linie w ogole odjezdzaja z okolicy
        // miejscowosci. Wolane z watku w tle - kilka zapytan HTTP.
        public static List<string> LinesNear(double lat, double lon)
        {
            List<StopRef> found = new List<StopRef>();
            foreach (double d in new double[] { 0.006, 0.014, 0.030 })
            {
                found = Query(lat, lon, d);
                if (found.Count > 0) break;
            }
            found.Sort(delegate (StopRef a, StopRef b) { return a.Km.CompareTo(b.Km); });

            List<string> o = new List<string>();
            for (int i = 0; i < found.Count && i < FavStops; i++)
            {
                try
                {
                    object root = Json.Parse(Api2.Get(Api + "stoptimes?stopId=" +
                        Uri.EscapeDataString(found[i].Id) + "&n=40"));
                    foreach (object t in Json.Arr(Json.At(root, "stopTimes")))
                    {
                        string l = Json.Str(Json.At(t, "routeShortName")).Trim().ToUpperInvariant();
                        if (l.Length > 0 && !o.Contains(l)) o.Add(l);
                    }
                }
                catch (Exception ex) { Program.Log("Transit/linie", ex); }
            }
            // najpierw numery rosnaco (4 przed 52), potem oznaczenia literowe
            o.Sort(delegate (string a, string b)
            {
                int x, y;
                bool na = int.TryParse(a, out x), nb = int.TryParse(b, out y);
                if (na && nb) return x.CompareTo(y);
                if (na != nb) return na ? -1 : 1;
                return string.CompareOrdinal(a, b);
            });
            return o;
        }

        static List<StopRef> Query(double lat, double lon, double d)
        {
            List<StopRef> o = new List<StopRef>();
            try
            {
                string url = Api + "map/stops?min=" + Ll(lat - d) + "," + Ll(lon - d) +
                             "&max=" + Ll(lat + d) + "," + Ll(lon + d);
                foreach (object s in Json.Arr(Json.Parse(Api2.Get(url))))
                {
                    StopRef r = new StopRef();
                    r.Id = Json.Str(Json.At(s, "stopId"));
                    r.Name = Json.Str(Json.At(s, "name"));
                    r.Lat = Json.Num(Json.At(s, "lat"));
                    r.Lon = Json.Num(Json.At(s, "lon"));
                    if (r.Id.Length == 0 || double.IsNaN(r.Lat)) continue;
                    r.Km = Geo.Km(lat, lon, r.Lat, r.Lon);
                    o.Add(r);
                }
            }
            catch (Exception ex) { Program.Log("Transit/stops", ex); }
            return o;
        }

        static string Ll(double v)
        {
            return v.ToString("0.0000", CultureInfo.InvariantCulture);
        }

        static string Dist(double km)
        {
            if (km < 1.0) return "ok. " + ((int)Math.Round(km * 1000 / 10.0) * 10)
                .ToString(CultureInfo.InvariantCulture) + " m";
            return "ok. " + km.ToString("0.0", CultureInfo.InvariantCulture) + " km";
        }

        static DateTime Iso(string s)
        {
            if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
            DateTime t;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t))
                return t.ToLocalTime();
            return DateTime.MinValue;
        }

        public static string Minutes(Departure d)
        {
            if (d.Seconds == int.MaxValue) return d.Time;
            int m = (int)Math.Round(d.Seconds / 60.0);
            if (m <= 0) return "teraz";
            if (m < 60) return m + " min";
            return d.Time;
        }

        // Serwisy miejskie (krakow.pl, LoveKrakow) maja sens tylko w Krakowie -
        // komunikacja dziala juz wszedzie, wiec to pytanie dotyczy samych newsow.
        public static bool AnyNearKrakow()
        {
            foreach (Place p in Cfg.Places) if (p.NearKrakow) return true;
            return false;
        }
    }

    // Transitous wymaga wlasnego naglowka User-Agent i prosi o kontakt do
    // autora - stad osobna sciezka zamiast wspolnego Api.Fetch.
    static class Api2
    {
        public static string Get(string url)
        {
            using (System.Net.WebClient c = new System.Net.WebClient())
            {
                c.Encoding = Encoding.UTF8;
                c.Headers[System.Net.HttpRequestHeader.UserAgent] = Cfg.UserAgent;
                return c.DownloadString(url);
            }
        }
    }
}
