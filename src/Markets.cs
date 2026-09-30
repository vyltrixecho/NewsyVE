using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NewsyVE
{
    class Market
    {
        public string Symbol;      // symbol Yahoo
        public string Name;        // nazwa po polsku
        public string Short;       // krotki opis instrumentu
        public string Tv;          // strona TradingView
        public string Unit;        // jednostka ceny
        public int Decimals = 2;
        public bool Metal;
        public string Fuel;        // etykieta serii w wykresie AutoCentrum; null = notowanie Yahoo
        public bool Energy;        // ropa i paliwa - osobny przelacznik w ustawieniach
        public string Span = "3 miesiące";

        public double Price, Prev;
        public double[] Series = new double[0];
        public string Note = "";
        public bool Ok;

        public double ChangePct
        {
            get { return (Prev > 0) ? (Price - Prev) / Prev * 100.0 : 0; }
        }
    }

    static class Markets
    {
        public const double OzToGram = 31.1034768;

        public static Market[] All = new Market[]
        {
            new Market { Symbol = "USDPLN=X", Name = "Dolar",  Short = "USD / PLN",
                         Tv = "https://pl.tradingview.com/symbols/USDPLN/", Unit = "zł", Decimals = 4 },
            new Market { Symbol = "EURPLN=X", Name = "Euro",   Short = "EUR / PLN",
                         Tv = "https://pl.tradingview.com/symbols/EURPLN/", Unit = "zł", Decimals = 4 },
            new Market { Symbol = "GC=F",     Name = "Złoto",  Short = "COMEX · za uncję",
                         Tv = "https://pl.tradingview.com/symbols/COMEX-GC1!/", Unit = "$", Decimals = 2, Metal = true },
            new Market { Symbol = "SI=F",     Name = "Srebro", Short = "COMEX · za uncję",
                         Tv = "https://pl.tradingview.com/symbols/COMEX-SI1!/", Unit = "$", Decimals = 3, Metal = true },
            new Market { Symbol = "BZ=F",     Name = "Ropa Brent", Short = "ICE · za baryłkę",
                         Tv = "https://pl.tradingview.com/symbols/NYMEX-BB1!/", Unit = "$", Decimals = 2, Energy = true },
            // ceny na stacjach: srednie tygodniowe dla calej Polski
            new Market { Symbol = "PB95", Fuel = "Polska 95",  Name = "Benzyna 95", Short = "średnia w Polsce",
                         Tv = FuelPage, Unit = "zł/l", Decimals = 2, Energy = true, Span = "14 tygodni" },
            new Market { Symbol = "ON",   Fuel = "Polska ON",  Name = "Olej napędowy", Short = "średnia w Polsce",
                         Tv = FuelPage, Unit = "zł/l", Decimals = 2, Energy = true, Span = "14 tygodni" },
            new Market { Symbol = "LPG",  Fuel = "Polska LPG", Name = "Gaz LPG", Short = "średnia w Polsce",
                         Tv = FuelPage, Unit = "zł/l", Decimals = 2, Energy = true, Span = "14 tygodni" }
        };

        const string FuelPage = "https://www.autocentrum.pl/paliwa/ceny-paliw/";
        const double LitersPerBarrel = 158.987;

        // ktore kafelki pokazac - waluty i metale oraz ropa i paliwa maja
        // osobne przelaczniki
        public static List<Market> Visible()
        {
            List<Market> o = new List<Market>();
            foreach (Market m in All)
                if (m.Energy ? Cfg.CardFuel : Cfg.CardMarkets) o.Add(m);
            return o;
        }

        static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Notowania pobierane raz na dobe - NBP publikuje raz dziennie,
        // a metale i tak ogladamy w skali 3 miesiecy.
        public static DateTime LastFetch = DateTime.MinValue;
        public static bool NeedsRefresh
        {
            get
            {
                if (LastFetch.Date != DateTime.Now.Date) return true;
                // nowy kafelek, ktorego nie bylo jeszcze w cache - nie czekamy do jutra
                foreach (Market m in All) if (!m.Ok) return true;
                return false;
            }
        }

        static string CachePath { get { return Path.Combine(Cfg.AppDir, "markets.cache"); } }

        public static void SaveCache()
        {
            try
            {
                StringBuilder b = new StringBuilder();
                b.AppendLine("day=" + LastFetch.ToString("yyyy-MM-dd HH:mm", Inv));
                foreach (Market m in All)
                {
                    if (!m.Ok) continue;
                    StringBuilder ser = new StringBuilder();
                    for (int i = 0; i < m.Series.Length; i++)
                    {
                        if (i > 0) ser.Append(',');
                        ser.Append(m.Series[i].ToString("R", Inv));
                    }
                    b.AppendLine(string.Join("|", new string[] {
                        m.Symbol, m.Price.ToString("R", Inv), m.Prev.ToString("R", Inv),
                        m.Note.Replace('|', ' '), ser.ToString() }));
                }
                File.WriteAllText(CachePath, b.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static void LoadCache()
        {
            try
            {
                if (!File.Exists(CachePath)) return;
                foreach (string line in File.ReadAllLines(CachePath, Encoding.UTF8))
                {
                    if (line.StartsWith("day=", StringComparison.Ordinal))
                    {
                        DateTime d;
                        if (DateTime.TryParseExact(line.Substring(4), "yyyy-MM-dd HH:mm", Inv,
                                DateTimeStyles.None, out d)) LastFetch = d;
                        continue;
                    }
                    string[] p2 = line.Split('|');
                    if (p2.Length < 5) continue;
                    foreach (Market m in All)
                    {
                        if (m.Symbol != p2[0]) continue;
                        double price, prev;
                        if (!double.TryParse(p2[1], NumberStyles.Float, Inv, out price)) continue;
                        double.TryParse(p2[2], NumberStyles.Float, Inv, out prev);
                        List<double> vals = new List<double>();
                        foreach (string v in p2[4].Split(','))
                        {
                            double x;
                            if (double.TryParse(v, NumberStyles.Float, Inv, out x)) vals.Add(x);
                        }
                        m.Price = price; m.Prev = prev; m.Note = p2[3];
                        m.Series = vals.ToArray(); m.Ok = true;
                    }
                }
            }
            catch { }
        }

        public static string Fmt(double v, int dec)
        {
            return v.ToString("N" + dec.ToString(CultureInfo.InvariantCulture), Pl);
        }

        static void LoadOne(Market m)
        {
            string url = "https://query1.finance.yahoo.com/v8/finance/chart/" +
                         Uri.EscapeDataString(m.Symbol) + "?range=3mo&interval=1d";
            object root = Json.Parse(Api.Fetch(url));
            object[] res = Json.Arr(Json.At(Json.At(root, "chart"), "result"));
            if (res.Length == 0) return;
            object r = res[0];

            object meta = Json.At(r, "meta");
            double price = Json.Num(Json.At(meta, "regularMarketPrice"));

            object[] q = Json.Arr(Json.At(Json.At(r, "indicators"), "quote"));
            List<double> vals = new List<double>();
            if (q.Length > 0)
                foreach (object c in Json.Arr(Json.At(q[0], "close")))
                {
                    double v = Json.Num(c);
                    if (!double.IsNaN(v)) vals.Add(v);
                }

            if (vals.Count == 0 && double.IsNaN(price)) return;
            if (double.IsNaN(price) && vals.Count > 0) price = vals[vals.Count - 1];

            m.Price = price;
            m.Series = vals.ToArray();
            // zmiana dzienna: wzgledem poprzedniego zamkniecia, nie poczatku zakresu
            m.Prev = vals.Count >= 2 ? vals[vals.Count - 2] : price;
            m.Ok = true;
        }

        public static void Refresh(bool force)
        {
            if (!force && !NeedsRefresh) return;
            bool any = false;
            foreach (Market m in All)
            {
                if (m.Fuel != null) continue;
                try { LoadOne(m); if (m.Ok) any = true; } catch { }
            }
            try { if (LoadFuel()) any = true; }
            catch (Exception ex) { Program.Log("Paliwa", ex); }
            if (!any) return;            // brak sieci - zostawiamy to, co bylo w cache
            AddNotes();
            LastFetch = DateTime.Now;
            SaveCache();
        }

        // AutoCentrum trzyma dane swojego wykresu wprost w atrybucie data-chart:
        // etykiety tygodni i po jednej serii na paliwo. Ostatni punkt to biezacy,
        // niepelny tydzien - zmiana liczona jest wzgledem poprzedniego.
        static readonly System.Text.RegularExpressions.Regex RxChart =
            new System.Text.RegularExpressions.Regex("data-chart='([^']*)'");

        static bool LoadFuel()
        {
            System.Text.RegularExpressions.Match mc = RxChart.Match(Api.FetchText(FuelPage));
            if (!mc.Success) return false;
            object root = Json.Parse(System.Net.WebUtility.HtmlDecode(mc.Groups[1].Value));
            object[] labels = Json.Arr(Json.At(root, "labels"));
            string week = labels.Length > 0 ? Json.Str(labels[labels.Length - 1]) : "";

            bool any = false;
            foreach (object ds in Json.Arr(Json.At(root, "datasets")))
            {
                string label = Json.Str(Json.At(ds, "label"));
                foreach (Market m in All)
                {
                    if (m.Fuel != label) continue;
                    List<double> vals = new List<double>();
                    foreach (object v in Json.Arr(Json.At(ds, "data")))
                    {
                        double x = Json.Num(v);
                        if (!double.IsNaN(x) && x > 0) vals.Add(x);
                    }
                    if (vals.Count == 0) continue;
                    m.Series = vals.ToArray();
                    m.Price = vals[vals.Count - 1];
                    m.Prev = vals.Count >= 2 ? vals[vals.Count - 2] : m.Price;
                    m.Note = WeekNote(week) + "AutoCentrum";
                    m.Ok = true;
                    any = true;
                }
            }
            return any;
        }

        // "14.09 - 20.09" -> "14.09–20.09 · "; biezacy, dopiero zaczety tydzien
        // AutoCentrum podpisuje "21.09 - 21.09" - wtedy "od 21.09 · "
        static string WeekNote(string label)
        {
            string[] p = label.Split(new string[] { " - " }, StringSplitOptions.None);
            if (p.Length != 2) return label.Length > 0 ? label + " · " : "";
            if (p[0].Trim() == p[1].Trim()) return "od " + p[0].Trim() + " · ";
            return p[0].Trim() + "–" + p[1].Trim() + " · ";
        }

        // Druga linia: kurs sredni NBP dla walut, przelicznik na zlote za gram dla metali.
        static void AddNotes()
        {
            double usdPln = 0;
            foreach (Market m in All) if (m.Symbol == "USDPLN=X" && m.Ok) usdPln = m.Price;

            foreach (Market m in All)
            {
                if (m.Fuel != null) continue;              // notatke ustawia LoadFuel
                if (!m.Ok) { m.Note = ""; continue; }
                if (m.Metal)
                {
                    if (usdPln > 0)
                        m.Note = Fmt(m.Price / OzToGram * usdPln, 2) + " zł za gram";
                    else
                        m.Note = "";
                }
                else if (m.Energy)
                {
                    // surowiec, nie paliwo: bez akcyzy, VAT-u i marzy stacji
                    m.Note = usdPln > 0
                        ? Fmt(m.Price * usdPln / LitersPerBarrel, 2) + " zł za litr surowca"
                        : "";
                }
            }

            TryNbp("USD", "USDPLN=X");
            TryNbp("EUR", "EURPLN=X");
            TryNbpGold();
        }

        static void TryNbp(string code, string symbol)
        {
            try
            {
                object o = Json.Parse(Api.Fetch(
                    "https://api.nbp.pl/api/exchangerates/rates/A/" + code + "/?format=json"));
                object[] rates = Json.Arr(Json.At(o, "rates"));
                if (rates.Length == 0) return;
                double mid = Json.Num(Json.At(rates[0], "mid"));
                string day = Json.Str(Json.At(rates[0], "effectiveDate"));
                foreach (Market m in All)
                    if (m.Symbol == symbol)
                        m.Note = "NBP " + Fmt(mid, 4) + " (" + day + ")";
            }
            catch { }
        }

        static void TryNbpGold()
        {
            try
            {
                object[] a = Json.Arr(Json.Parse(Api.Fetch(
                    "https://api.nbp.pl/api/cenyzlota/last/1/?format=json")));
                if (a.Length == 0) return;
                double cena = Json.Num(Json.At(a[0], "cena"));
                foreach (Market m in All)
                    if (m.Symbol == "GC=F" && m.Ok)
                        m.Note = m.Note + "  ·  NBP " + Fmt(cena, 2) + " zł/g";
            }
            catch { }
        }
    }
}
