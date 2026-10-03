using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NewsyVE
{
    class GeoHit
    {
        public string Name = "", Region = "", Country = "";
        public double Lat, Lon;
        public string Label
        {
            get
            {
                string s = Name;
                if (Region.Length > 0) s += ", " + Region;
                if (Country.Length > 0 && Country != "Polska") s += " (" + Country + ")";
                return s;
            }
        }
    }

    static class Geo
    {
        public const double KrakowLat = 50.0614, KrakowLon = 19.9366;

        public static double Km(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0, d = Math.PI / 180.0;
            double dLat = (lat2 - lat1) * d, dLon = (lon2 - lon1) * d;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * d) * Math.Cos(lat2 * d) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(a)));
        }

        // ---------------- wyszukiwanie miejscowosci ----------------
        public static List<GeoHit> Search(string query)
        {
            List<GeoHit> res = new List<GeoHit>();
            if (query == null || query.Trim().Length < 2) return res;
            string url = "https://geocoding-api.open-meteo.com/v1/search?name=" +
                         Uri.EscapeDataString(query.Trim()) + "&count=8&language=pl&format=json";
            object root = Json.Parse(Api.Fetch(url));
            foreach (object r in Json.Arr(Json.At(root, "results")))
            {
                GeoHit h = new GeoHit();
                h.Name = Json.Str(Json.At(r, "name"));
                h.Region = Json.Str(Json.At(r, "admin1"));
                h.Country = Json.Str(Json.At(r, "country"));
                h.Lat = Json.Num(Json.At(r, "latitude"));
                h.Lon = Json.Num(Json.At(r, "longitude"));
                if (h.Name.Length > 0 && !double.IsNaN(h.Lat)) res.Add(h);
            }
            return res;
        }

        // ---------------- zgrubna lokalizacja po IP ----------------
        // Dokladnosc bywa fatalna (VPN, zakres operatora), wiec sluzy tylko
        // jako propozycja przy pierwszym uruchomieniu - uzytkownik moze poprawic.
        public static GeoHit ByIp()
        {
            string[] urls = {
                "http://ip-api.com/json/?fields=status,country,regionName,city,lat,lon",
                "https://ipwho.is/"
            };
            foreach (string u in urls)
            {
                try
                {
                    object o = Json.Parse(Api.Fetch(u));
                    GeoHit h = new GeoHit();
                    h.Name = Json.Str(Json.At(o, "city"));
                    h.Region = Json.Str(Json.At(o, "regionName"));
                    if (h.Region.Length == 0) h.Region = Json.Str(Json.At(o, "region"));
                    h.Country = Json.Str(Json.At(o, "country"));
                    object la = Json.At(o, "lat"); if (la == null) la = Json.At(o, "latitude");
                    object lo = Json.At(o, "lon"); if (lo == null) lo = Json.At(o, "longitude");
                    h.Lat = Json.Num(la); h.Lon = Json.Num(lo);
                    if (h.Name.Length > 0 && !double.IsNaN(h.Lat) && h.Lat != 0)
                    {
                        Polish(h);
                        return h;
                    }
                }
                catch { }
            }
            return null;
        }

        // Uslugi IP podaja nazwy po angielsku ("Warsaw", "Mazovia"). Region musi
        // jednak brzmiec "wojewodztwo mazowieckie", bo po nim dobierane sa
        // ostrzezenia IMGW. Szukanie po nazwie nie pomaga ("Cracow" trafia do
        // Australii), wiec pytamy o wspolrzedne. Gdy sie nie uda - zostaje
        // nazwa z IP.
        static void Polish(GeoHit h)
        {
            string lat = h.Lat.ToString("0.#####", CultureInfo.InvariantCulture);
            string lon = h.Lon.ToString("0.#####", CultureInfo.InvariantCulture);
            try
            {
                // OpenStreetMap wymaga wlasnego User-Agenta - stad Api2
                object o = Json.Parse(Api2.Get(
                    "https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=10&accept-language=pl" +
                    "&lat=" + lat + "&lon=" + lon));
                object a = Json.At(o, "address");
                string name = Json.Str(Json.At(a, "city"));
                if (name.Length == 0) name = Json.Str(Json.At(a, "town"));
                if (name.Length == 0) name = Json.Str(Json.At(a, "village"));
                if (name.Length == 0) name = Json.Str(Json.At(o, "name"));
                string region = Json.Str(Json.At(a, "state"));
                if (name.Length > 0)
                {
                    h.Name = name;
                    if (region.Length > 0) h.Region = region;
                    string country = Json.Str(Json.At(a, "country"));
                    if (country.Length > 0) h.Country = country;
                    return;
                }
            }
            catch { }
            try
            {
                object o = Json.Parse(Api.Fetch(
                    "https://api.bigdatacloud.net/data/reverse-geocode-client?localityLanguage=pl" +
                    "&latitude=" + lat + "&longitude=" + lon));
                string name = Json.Str(Json.At(o, "city"));
                if (name.Length == 0) name = Json.Str(Json.At(o, "locality"));
                if (name.Length > 0)
                {
                    h.Name = name;
                    string region = Json.Str(Json.At(o, "principalSubdivision"));
                    if (region.Length > 0) h.Region = region;
                    string country = Json.Str(Json.At(o, "countryName"));
                    if (country.Length > 0) h.Country = country;
                }
            }
            catch { }
        }

        // ---------------- siatka ICM UM 4 km ----------------
        // Kalibracja z trzech punktow odniesienia sprawdzonych na meteo.pl:
        //   (col 232, row 466) = 19,89389 E  50,02250 N
        //   (col 232, row 467) = 19,89361 E  49,98639 N
        //   (col 236, row 466) = 20,11806 E  50,02111 N
        const double LonC = 0.05604, LonR = -0.00028, Lon0 = 19.89389;
        const double LatC = -0.000347, LatR = -0.03611, Lat0 = 50.02250;

        public static void IcmPoint(double lat, double lon, out int col, out int row)
        {
            // uklad rownan na przesuniecia wzgledem punktu odniesienia
            double dLon = lon - Lon0, dLat = lat - Lat0;
            double det = LonC * LatR - LonR * LatC;
            double dc = (dLon * LatR - LonR * dLat) / det;
            double dr = (LonC * dLat - dLon * LatC) / det;
            col = 232 + (int)Math.Round(dc);
            row = 466 + (int)Math.Round(dr);
        }

        public static void IcmLatLon(int col, int row, out double lat, out double lon)
        {
            lon = Lon0 + LonC * (col - 232) + LonR * (row - 466);
            lat = Lat0 + LatC * (col - 232) + LatR * (row - 466);
        }

        // Jak daleko od wskazanego miejsca wypada najblizszy wezel siatki.
        public static double IcmErrorKm(double lat, double lon)
        {
            int c, r;
            IcmPoint(lat, lon, out c, out r);
            double gl, gn;
            IcmLatLon(c, r, out gl, out gn);
            return Km(lat, lon, gl, gn);
        }

        // ---------------- stacje synoptyczne IMGW ----------------
        public class Station
        {
            public string Id = "", Name = "";
            public double Lat, Lon;
        }

        static List<Station> stations;
        static readonly object gate = new object();
        static string CachePath { get { return Path.Combine(Cfg.AppDir, "stacje.cache"); } }

        static void LoadCache()
        {
            stations = new List<Station>();
            try
            {
                if (!File.Exists(CachePath)) return;
                foreach (string line in File.ReadAllLines(CachePath, Encoding.UTF8))
                {
                    string[] p = line.Split('|');
                    if (p.Length < 4) continue;
                    Station s = new Station();
                    s.Id = p[0]; s.Name = p[1];
                    if (!double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out s.Lat)) continue;
                    if (!double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out s.Lon)) continue;
                    stations.Add(s);
                }
            }
            catch { }
        }

        static void SaveCache()
        {
            try
            {
                StringBuilder b = new StringBuilder();
                foreach (Station s in stations)
                    b.AppendLine(string.Join("|", new string[] {
                        s.Id, s.Name,
                        s.Lat.ToString("R", CultureInfo.InvariantCulture),
                        s.Lon.ToString("R", CultureInfo.InvariantCulture) }));
                File.WriteAllText(CachePath, b.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        // IMGW podaje nazwy stacji bez wspolrzednych, wiec dogeokodowujemy je raz
        // i zapisujemy - przy kolejnych uruchomieniach wystarczy plik.
        public static void EnsureStations()
        {
            lock (gate)
            {
                if (stations == null) LoadCache();
                if (stations.Count > 20) return;
            }

            List<Station> found = new List<Station>();
            try
            {
                object[] arr = Json.Arr(Json.Parse(Api.Fetch("https://danepubliczne.imgw.pl/api/data/synop")));
                foreach (object o in arr)
                {
                    Station s = new Station();
                    s.Id = Json.Str(Json.At(o, "id_stacji"));
                    s.Name = Json.Str(Json.At(o, "stacja"));
                    if (s.Name.Length == 0) continue;
                    try
                    {
                        List<GeoHit> hits = Search(s.Name + ", Polska");
                        if (hits.Count == 0) hits = Search(s.Name);
                        if (hits.Count == 0) continue;
                        s.Lat = hits[0].Lat; s.Lon = hits[0].Lon;
                        found.Add(s);
                    }
                    catch { }
                }
            }
            catch { }

            if (found.Count > 20)
            {
                lock (gate) { stations = found; }
                SaveCache();
            }
        }

        // Najblizsza stacja, o ile jest sensownie blisko - inaczej zostaje model.
        public static Station NearestStation(double lat, double lon, double maxKm)
        {
            List<Station> list;
            lock (gate) { list = stations; }
            if (list == null || list.Count == 0) return null;
            Station best = null;
            double bestKm = double.MaxValue;
            foreach (Station s in list)
            {
                double d = Km(lat, lon, s.Lat, s.Lon);
                if (d < bestKm) { bestKm = d; best = s; }
            }
            return (best != null && bestKm <= maxKm) ? best : null;
        }

        public static double StationKm(Station s, double lat, double lon)
        {
            return s == null ? 0 : Km(lat, lon, s.Lat, s.Lon);
        }
    }
}
