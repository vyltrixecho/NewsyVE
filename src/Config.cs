using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NewsyVE
{
    class Place
    {
        public string Name = "", Region = "";
        public double Lat, Lon;
        public string Imgw;                  // id stacji synoptycznej albo null
        public string ImgwName = "";
        public double ImgwKm;
        public int IcmRow, IcmCol;
        public string IcmNote = "";
        public List<string> Lines = new List<string>();   // ulubione linie komunikacji

        // "4, 52 212;N1" -> [4, 52, 212, N1]; wielkosc liter bez znaczenia
        public static List<string> ParseLines(string s)
        {
            List<string> o = new List<string>();
            if (string.IsNullOrEmpty(s)) return o;
            foreach (string x in s.Split(new char[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string v = x.Trim().ToUpperInvariant();
                if (v.Length > 0 && v.Length <= 8 && !o.Contains(v)) o.Add(v);
            }
            return o;
        }

        public bool IsFav(string line)
        {
            return Lines.Count > 0 && line != null && Lines.Contains(line.Trim().ToUpperInvariant());
        }

        // MPK i serwisy miejskie maja sens tylko w okolicy Krakowa
        public bool NearKrakow
        {
            get { return Geo.Km(Lat, Lon, Geo.KrakowLat, Geo.KrakowLon) <= 35; }
        }

        public string Teryt2                 // dwucyfrowy kod wojewodztwa (TERYT)
        {
            get { return Cfg.VoivodeshipCode(Region); }
        }

        public void Recalc()
        {
            Geo.IcmPoint(Lat, Lon, out IcmCol, out IcmRow);
            double err = Geo.IcmErrorKm(Lat, Lon);
            IcmNote = err >= 1.0
                ? "węzeł siatki ICM — " +
                  err.ToString("0.0", CultureInfo.InvariantCulture) + " km stąd"
                : "";
        }

        public string Serialize()
        {
            return string.Join("|", new string[] {
                Name.Replace('|', ' '),
                Lat.ToString("R", CultureInfo.InvariantCulture),
                Lon.ToString("R", CultureInfo.InvariantCulture),
                Region.Replace('|', ' '),
                string.Join(",", Lines.ToArray())
            });
        }

        public static Place Parse(string s)
        {
            string[] p = s.Split('|');
            if (p.Length < 3) return null;
            Place x = new Place();
            x.Name = p[0];
            if (!double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out x.Lat)) return null;
            if (!double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out x.Lon)) return null;
            if (p.Length > 3) x.Region = p[3];
            if (p.Length > 4) x.Lines = ParseLines(p[4]);     // starsze pliki nie maja tego pola
            x.Recalc();
            return x;
        }
    }

    static class Cfg
    {
        public static string Side = "Left";      // Left | Right | Off
        public static int Offset = 8;
        public static int BarPlace = 0;          // ktora miejscowosc na pasku zadan
        public static string AccuKey = "";       // wlasny klucz AccuWeather (opcjonalnie)
        public static int TextLevel = 4;         // sila napisu na pasku: 1..5
        public static string Contact = "";       // kontakt doklejany do User-Agent
        public static int WxMin = 10;            // co ile minut pogoda
        public static int NewsMin = 15;          // co ile minut newsy
        public static bool Configured;           // czy plik ustawien juz istnial

        // Ktore karty pokazuje zakladka News. Szerokosc okna liczy sie z tego,
        // ile jest wlaczonych - wylaczenie karty naprawde zweza panel,
        // a nie zostawia po niej dziury.
        public static bool CardTransit = true;
        public static bool CardLocal = true;
        public static bool CardSport = true;
        public static bool CardPolitics = true;
        public static bool CardAi = true;
        public static bool CardGames = true;
        public static bool CardMarkets = true;
        public static bool CardFuel = true;      // ropa i ceny paliw w kolumnie gieldy

        public static int Scale = 100;           // wielkosc pisma w newsach: 90..140 %

        // Zakladka Sport: do dwoch klubow z Ekstraklasy (nazwa jak w tabeli
        // ekstraklasa.org, pusta = brak) i jeden klub zagraniczny z TheSportsDB.
        public static bool SportTab = true;
        public static string Club1 = "Wisła Kraków";
        public static string Club2 = "Cracovia";
        public static string AbroadId = "133739";
        public static string AbroadName = "Barcelona";   // nazwa jak w TheSportsDB - po niej rozpoznajemy mecze

        public static List<Place> Places = new List<Place>();

        static readonly string[][] Voiv = {
            new string[]{"dolnośląskie","02"},      new string[]{"kujawsko-pomorskie","04"},
            new string[]{"lubelskie","06"},                   new string[]{"lubuskie","08"},
            new string[]{"łódzkie","10"},           new string[]{"małopolskie","12"},
            new string[]{"mazowieckie","14"},                 new string[]{"opolskie","16"},
            new string[]{"podkarpackie","18"},                new string[]{"podlaskie","20"},
            new string[]{"pomorskie","22"},                   new string[]{"śląskie","24"},
            new string[]{"świętokrzyskie","26"},    new string[]{"warmińsko-mazurskie","28"},
            new string[]{"wielkopolskie","30"},               new string[]{"zachodniopomorskie","32"}
        };

        public static string VoivodeshipCode(string region)
        {
            if (string.IsNullOrEmpty(region)) return "";
            string r = region.ToLowerInvariant();
            foreach (string[] v in Voiv) if (r.Contains(v[0])) return v[1];
            return "";
        }

        // Transitous prosi, zeby aplikacja przedstawiala sie wlasnym
        // User-Agentem i zostawiala kontakt - inaczej odpowiada bledem 403.
        public static string UserAgent
        {
            get
            {
                return "NewsyVE/2.0 (widget pulpitu" +
                       (Contact.Length > 0 ? "; " + Contact : "") + ")";
            }
        }

        public static string AppDir
        {
            get
            {
                return Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
        }

        static string Path0 { get { return Path.Combine(AppDir, "newsyve.cfg"); } }

        public static void Load()
        {
            try
            {
                if (File.Exists(Path0))
                {
                    foreach (string line in File.ReadAllLines(Path0, Encoding.UTF8))
                    {
                        int i = line.IndexOf('=');
                        if (i <= 0) continue;
                        string k = line.Substring(0, i).Trim();
                        string v = line.Substring(i + 1).Trim();
                        if (k == "side") Side = v;
                        else if (k == "offset") int.TryParse(v, out Offset);
                        else if (k == "place") int.TryParse(v, out BarPlace);
                        else if (k == "accukey") AccuKey = v;
                        else if (k == "textlevel") int.TryParse(v, out TextLevel);
                        else if (k == "kontakt") Contact = v;
                        else if (k == "wxmin") int.TryParse(v, out WxMin);
                        else if (k == "newsmin") int.TryParse(v, out NewsMin);
                        else if (k == "karta_komunikacja") CardTransit = v != "0";
                        else if (k == "karta_lokalne") CardLocal = v != "0";
                        else if (k == "karta_sport") CardSport = v != "0";
                        else if (k == "karta_polityka") CardPolitics = v != "0";
                        else if (k == "karta_ai") CardAi = v != "0";
                        else if (k == "karta_gry") CardGames = v != "0";
                        else if (k == "karta_gielda") CardMarkets = v != "0";
                        else if (k == "karta_paliwa") CardFuel = v != "0";
                        else if (k == "skala") int.TryParse(v, out Scale);
                        else if (k == "sport_zakladka") SportTab = v != "0";
                        else if (k == "sport_klub1") Club1 = v;
                        else if (k == "sport_klub2") Club2 = v;
                        else if (k == "sport_zagraniczny")
                        {
                            int bar = v.IndexOf('|');
                            AbroadId = bar > 0 ? v.Substring(0, bar) : "";
                            AbroadName = bar > 0 ? v.Substring(bar + 1) : "";
                        }
                        else if (k.StartsWith("miejsce", StringComparison.Ordinal))
                        {
                            Place p = Place.Parse(v);
                            if (p != null) Places.Add(p);
                        }
                    }
                }
            }
            catch { }

            Configured = Places.Count > 0;
            if (Places.Count == 0) Places.Add(Default());
            if (BarPlace < 0 || BarPlace >= Places.Count) BarPlace = 0;
            if (Side != "Left" && Side != "Right" && Side != "Off") Side = "Left";
            if (TextLevel < 1 || TextLevel > 5) TextLevel = 4;
            if (Scale < 90 || Scale > 140) Scale = 100;
            if (WxMin < 5 || WxMin > 60) WxMin = 10;
            if (NewsMin < 5 || NewsMin > 180) NewsMin = 15;
        }

        public static Place Default()
        {
            Place p = new Place();
            p.Name = "Kraków";
            p.Lat = Geo.KrakowLat;
            p.Lon = Geo.KrakowLon;
            p.Region = "Województwo małopolskie";
            p.Recalc();
            return p;
        }

        public static void Save()
        {
            try
            {
                StringBuilder b = new StringBuilder();
                b.AppendLine("side=" + Side);
                b.AppendLine("offset=" + Offset.ToString(CultureInfo.InvariantCulture));
                b.AppendLine("place=" + BarPlace.ToString(CultureInfo.InvariantCulture));
                b.AppendLine("textlevel=" + TextLevel.ToString(CultureInfo.InvariantCulture));
                b.AppendLine("kontakt=" + Contact);
                b.AppendLine("wxmin=" + WxMin.ToString(CultureInfo.InvariantCulture));
                b.AppendLine("newsmin=" + NewsMin.ToString(CultureInfo.InvariantCulture));
                b.AppendLine("accukey=" + AccuKey);
                b.AppendLine("karta_komunikacja=" + (CardTransit ? "1" : "0"));
                b.AppendLine("karta_lokalne=" + (CardLocal ? "1" : "0"));
                b.AppendLine("karta_sport=" + (CardSport ? "1" : "0"));
                b.AppendLine("karta_polityka=" + (CardPolitics ? "1" : "0"));
                b.AppendLine("karta_ai=" + (CardAi ? "1" : "0"));
                b.AppendLine("karta_gry=" + (CardGames ? "1" : "0"));
                b.AppendLine("karta_gielda=" + (CardMarkets ? "1" : "0"));
                b.AppendLine("karta_paliwa=" + (CardFuel ? "1" : "0"));
                b.AppendLine("skala=" + Scale.ToString(CultureInfo.InvariantCulture));
                b.AppendLine("sport_zakladka=" + (SportTab ? "1" : "0"));
                b.AppendLine("sport_klub1=" + Club1);
                b.AppendLine("sport_klub2=" + Club2);
                b.AppendLine("sport_zagraniczny=" + (AbroadId.Length > 0 ? AbroadId + "|" + AbroadName.Replace('|', ' ') : ""));
                for (int i = 0; i < Places.Count; i++)
                    b.AppendLine("miejsce" + i.ToString(CultureInfo.InvariantCulture) + "=" + Places[i].Serialize());
                File.WriteAllText(Path0, b.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static string IcmPage(Place p)
        {
            return "https://www.meteo.pl/um/php/meteorogram_list.php?ntype=0u&row=" + p.IcmRow +
                   "&col=" + p.IcmCol + "&lang=pl&cname=" + Uri.EscapeDataString(p.Name);
        }

        public static string IcmImage(int row, int col)
        {
            return "https://www.meteo.pl/um/metco/mgram_pict.php?ntype=0u&row=" + row +
                   "&col=" + col + "&lang=pl&_=" + DateTime.Now.Ticks;
        }
    }
}
