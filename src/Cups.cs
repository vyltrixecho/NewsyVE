using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NewsyVE
{
    class CupMatch
    {
        public DateTime When;
        public string HomeId = "", AwayId = "";
        public string Home = "", Away = "", HomeBadge = "", AwayBadge = "";
        public int HomeScore = -1, AwayScore = -1;
        public int PenHome = -1, PenAway = -1;       // rzuty karne
        public int AggHome = -1, AggAway = -1;       // wynik dwumeczu (tylko rewanz)
        public string AggWinner = "", AggReason = "";
        public string CompId = "", Season = "";
        public string Label = "";                     // "LE · faza ligowa", "LK · baraże · rewanż"
        public string RoundEn = "";
        public bool Live, Done;
        public string Minute = "";

        public bool IsHome(string id) { return HomeId == id; }
        public string Opp(string id) { return HomeId == id ? Away : Home; }
        public string OppBadge(string id) { return HomeId == id ? AwayBadge : HomeBadge; }
        public int Mine(string id) { return HomeId == id ? HomeScore : AwayScore; }
        public int Theirs(string id) { return HomeId == id ? AwayScore : HomeScore; }
    }

    class CupClub
    {
        public string Id = "", Name = "", Badge = "";
        public string Comp = "", CompId = "";         // gdzie gra teraz, np. "Liga Europy"
        public string Standing = "";                  // "21. miejsce · 0 pkt po 1 meczu"
        public int Zone;                              // 1 awans, 2 baraze, 3 poza, 0 brak tabeli
        public List<CupMatch> Last = new List<CupMatch>();
        public List<CupMatch> Next = new List<CupMatch>();
        public string OutNote = "";                   // dla klubow, ktore juz nie graja
    }

    // Puchary europejskie i reprezentacja - z publicznego API UEFA, z ktorego
    // korzysta uefa.com. Obejmuje eliminacje, faze ligowa, dwumecze, Lige
    // Narodow, eliminacje MS i mecze towarzyskie kadry, a mecze na zywo maja
    // biezacy wynik i minute. Polskie kluby rozpoznajemy po kodzie kraju,
    // wiec nie trzeba ich nigdzie wpisywac.
    //
    // ESPN ma podobne dane, ale odpowiada 403 na kazdy User-Agent poza kilkoma
    // narzedziami - nie podszywamy sie pod nie.
    static class Cups
    {
        const string Match = "https://match.uefa.com/v5/matches?";
        const string Teams = "https://comp.uefa.com/v2/teams?";
        const string Table = "https://standings.uefa.com/v1/standings?";
        public const string Poland = "109";              // id reprezentacji w UEFA
        static readonly string[] ClubComps = { "1", "14", "2019" };

        public static List<CupClub> Clubs = new List<CupClub>();
        public static List<CupClub> Out = new List<CupClub>();
        public static List<CupMatch> NatLast = new List<CupMatch>();
        public static List<CupMatch> NatNext = new List<CupMatch>();
        public static List<Standing> NatTable = new List<Standing>();
        public static string NatGroup = "";
        public static string NatComp = "";               // id rozgrywek tabeli - do linku
        public static string Season = "";                // "2026/27"
        public static DateTime Stamp = DateTime.MinValue;
        public static bool Ok;

        // Lista polskich druzyn w pucharach zmienia sie raz na kilka tygodni.
        static readonly Dictionary<string, string[]> polish = new Dictionary<string, string[]>();
        static string polishFor = "";
        static DateTime polishAt = DateTime.MinValue;

        static string SeasonYear()
        {
            DateTime n = DateTime.Now;
            return (n.Month >= 7 ? n.Year + 1 : n.Year).ToString(CultureInfo.InvariantCulture);
        }

        // ---------------- nazwy ----------------
        public static string CompName(string id)
        {
            switch (id)
            {
                case "1": return "Liga Mistrzów";
                case "14": return "Liga Europy";
                case "2019": return "Liga Konferencji";
                case "2014": return "Liga Narodów";
                case "17": return "Mistrzostwa świata";
                case "3": return "Mistrzostwa Europy";
                case "19": return "Mecz towarzyski";
                default: return "";
            }
        }

        static string CompShort(string id, string round)
        {
            // baraze w lutym ("Knockout round play-offs") to juz faza pucharowa
            string r = round.ToLowerInvariant();
            bool q = r.Contains("qualif") || (r.Contains("play-off") && !r.Contains("knockout"));
            switch (id)
            {
                case "1": return q ? "el. LM" : "LM";
                case "14": return q ? "el. LE" : "LE";
                case "2019": return q ? "el. LK" : "LK";
                case "2014": return "LN";
                case "17": return q ? "el. MŚ" : "MŚ";
                case "3": return q ? "el. Euro" : "Euro";
                case "19": return "towarzyski";
                default: return "";
            }
        }

        static string RoundPl(string en)
        {
            string l = en.ToLowerInvariant();
            if (l.Length == 0 || l.Contains("friendl")) return "";
            // "el." stoi juz przy nazwie rozgrywek
            if (l.Contains("first qualifying")) return "I runda";
            if (l.Contains("second qualifying")) return "II runda";
            if (l.Contains("third qualifying")) return "III runda";
            if (l.Contains("preliminary")) return "runda wstępna";
            if (l.Contains("knockout") && l.Contains("play")) return "baraże o 1/8";
            if (l.Contains("play-off final")) return "finał baraży";
            if (l.Contains("play-off semi")) return "półfinał baraży";
            if (l.Contains("play-off") || l.Contains("playoff")) return "baraże";
            if (l.Contains("league phase")) return "faza ligowa";
            if (l.Contains("group")) return "faza grupowa";
            if (l.Contains("qualifiers")) return "eliminacje";
            if (l.Contains("round of 32")) return "1/16 finału";
            if (l.Contains("round of 16")) return "1/8 finału";
            if (l.Contains("quarter")) return "ćwierćfinał";
            if (l.Contains("semi")) return "półfinał";
            if (l.Contains("final")) return "finał";
            return en;
        }

        static readonly Dictionary<string, string> Countries = BuildCountries();
        static Dictionary<string, string> BuildCountries()
        {
            string[] p = {
                "ALB","Albania", "AND","Andora", "ARM","Armenia", "AUT","Austria", "AZE","Azerbejdżan",
                "BLR","Białoruś", "BEL","Belgia", "BIH","Bośnia i Hercegowina", "BUL","Bułgaria",
                "CRO","Chorwacja", "CYP","Cypr", "CZE","Czechy", "DEN","Dania", "ENG","Anglia",
                "EST","Estonia", "FRO","Wyspy Owcze", "FIN","Finlandia", "FRA","Francja", "GEO","Gruzja",
                "GER","Niemcy", "GIB","Gibraltar", "GRE","Grecja", "HUN","Węgry", "ISL","Islandia",
                "ISR","Izrael", "ITA","Włochy", "KAZ","Kazachstan", "KOS","Kosowo", "LVA","Łotwa",
                "LIE","Liechtenstein", "LTU","Litwa", "LUX","Luksemburg", "MLT","Malta", "MDA","Mołdawia",
                "MNE","Czarnogóra", "NED","Holandia", "MKD","Macedonia Płn.", "NIR","Irlandia Płn.",
                "NOR","Norwegia", "POL","Polska", "POR","Portugalia", "IRL","Irlandia", "ROU","Rumunia",
                "RUS","Rosja", "SMR","San Marino", "SCO","Szkocja", "SRB","Serbia", "SVK","Słowacja",
                "SVN","Słowenia", "ESP","Hiszpania", "SWE","Szwecja", "SUI","Szwajcaria", "TUR","Turcja",
                "UKR","Ukraina", "WAL","Walia", "USA","USA", "MEX","Meksyk", "CAN","Kanada",
                "BRA","Brazylia", "ARG","Argentyna", "URU","Urugwaj", "COL","Kolumbia", "CHI","Chile",
                "PER","Peru", "ECU","Ekwador", "PAR","Paragwaj", "NGA","Nigeria", "GHA","Ghana",
                "SEN","Senegal", "CMR","Kamerun", "CIV","Wybrzeże Kości Słoniowej", "MAR","Maroko",
                "TUN","Tunezja", "ALG","Algieria", "EGY","Egipt", "RSA","RPA", "JPN","Japonia",
                "KOR","Korea Płd.", "AUS","Australia", "NZL","Nowa Zelandia", "KSA","Arabia Saudyjska",
                "QAT","Katar", "IRN","Iran", "CRC","Kostaryka", "PAN","Panama", "JAM","Jamajka"
            };
            Dictionary<string, string> d = new Dictionary<string, string>();
            for (int i = 0; i + 1 < p.Length; i += 2) d[p[i]] = p[i + 1];
            return d;
        }

        // bez polskich znakow i wielkosci liter - UEFA pisze "Raków", ESPN
        // i inne serwisy "Rakow", a tabela Ekstraklasy "Raków Częstochowa"
        static string Fold(string s)
        {
            string d = s.ToLowerInvariant().Replace('ł', 'l').Normalize(NormalizationForm.FormD);
            StringBuilder b = new StringBuilder();
            foreach (char c in d)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) b.Append(c);
            return b.ToString();
        }

        // UEFA skraca nazwy polskich klubow ("Katowice", "Jagiellonia") -
        // pokazujemy je tak, jak w tabeli Ekstraklasy.
        static string ClubPl(string name)
        {
            string f = Fold(name);
            if (f.Length < 4) return name;
            foreach (string known in Sport.ClubNames())
            {
                string k = Fold(known);
                if (k == f || k.Contains(f) || f.Contains(k)) return known;
            }
            return name;
        }

        static string TeamName(object team)
        {
            string name = Json.Str(Json.At(team, "internationalName"));
            string code = Json.Str(Json.At(team, "countryCode"));
            bool national = Json.Str(Json.At(team, "teamTypeDetail")).StartsWith("NATIONAL", StringComparison.Ordinal);
            string pl;
            if (national && Countries.TryGetValue(code, out pl)) return pl;
            return code == "POL" ? ClubPl(name) : name;
        }

        // ---------------- pobieranie ----------------
        static object[] Get(string url) { return Json.Arr(Json.Parse(Api.FetchGz(url))); }

        static int Sc(object o, string side)
        {
            double v = Json.Num(Json.At(o, side));
            return double.IsNaN(v) ? -1 : (int)v;
        }

        static CupMatch Parse(object m)
        {
            object ko = Json.At(m, "kickOffTime");
            string dt = Json.Str(Json.At(ko, "dateTime"));
            DateTime when;
            // mecze bez godziny to wpisy archiwalne (np. towarzyskie z lat 30.)
            if (dt.Length == 0 || !DateTime.TryParse(dt, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out when))
                return null;

            CupMatch c = new CupMatch();
            c.When = when.ToLocalTime();
            object h = Json.At(m, "homeTeam"), a = Json.At(m, "awayTeam");
            c.HomeId = Json.Str(Json.At(h, "id"));
            c.AwayId = Json.Str(Json.At(a, "id"));
            c.Home = TeamName(h);
            c.Away = TeamName(a);
            c.HomeBadge = Json.Str(Json.At(h, "logoUrl"));
            c.AwayBadge = Json.Str(Json.At(a, "logoUrl"));
            c.CompId = Json.Str(Json.At(Json.At(m, "competition"), "id"));
            c.Season = Json.Str(Json.At(m, "seasonYear"));

            string status = Json.Str(Json.At(m, "status"));
            c.Live = status == "LIVE";
            c.Done = status == "FINISHED";

            object score = Json.At(m, "score");
            object total = Json.At(score, "total");
            if (total != null && (c.Live || c.Done))
            {
                c.HomeScore = Math.Max(0, Sc(total, "home"));
                c.AwayScore = Math.Max(0, Sc(total, "away"));
            }
            object pen = Json.At(score, "penalty");
            if (pen != null) { c.PenHome = Sc(pen, "home"); c.PenAway = Sc(pen, "away"); }

            object minute = Json.At(m, "minute");
            if (c.Live)
            {
                int n = Json.Int(Json.At(minute, "normal"));
                int inj = Json.Int(Json.At(minute, "injury"));
                c.Minute = n > 0 ? n + (inj > 0 ? "+" + inj : "") + "'" : "";
            }

            object leg = Json.At(m, "leg");
            int legNo = Json.Int(Json.At(leg, "number"));
            object agg = Json.At(score, "aggregate");
            if (legNo == 2 && agg != null && c.Done)
            {
                c.AggHome = Sc(agg, "home");
                c.AggAway = Sc(agg, "away");
                object w = Json.At(Json.At(m, "winner"), "aggregate");
                c.AggWinner = Json.Str(Json.At(Json.At(w, "team"), "id"));
                c.AggReason = Json.Str(Json.At(w, "reason"));
            }

            c.RoundEn = Json.Str(Json.At(Json.At(Json.At(m, "round"), "metaData"), "name"));
            string rl = c.RoundEn.ToLowerInvariant();
            string what;
            int md = Json.Int(Json.At(Json.At(m, "matchday"), "sequenceNumber"));
            // w fazie ligowej i grupach liczy sie kolejka, w dwumeczach runda i mecz
            if ((rl.Contains("league phase") || rl.Contains("group") || rl.Contains("qualifiers")) && md > 0)
                what = md + ". kolejka";
            else
            {
                what = RoundPl(c.RoundEn);
                if (legNo == 1) what += (what.Length > 0 ? " · " : "") + "1. mecz";
                else if (legNo == 2) what += (what.Length > 0 ? " · " : "") + "rewanż";
            }
            string cs = CompShort(c.CompId, c.RoundEn);
            if (cs.Length == 0) cs = Json.Str(Json.At(Json.At(Json.At(m, "competition"), "metaData"), "name"));
            c.Label = what.Length > 0 && c.CompId != "19" ? cs + " · " + what : cs;
            return c;
        }

        static List<CupMatch> Matches(string query)
        {
            List<CupMatch> o = new List<CupMatch>();
            foreach (object m in Get(Match + query))
            {
                CupMatch c = Parse(m);
                if (c != null) o.Add(c);
            }
            return o;
        }

        // Ostatnie rozegrane i najblizsze mecze druzyny we wszystkich rozgrywkach UEFA.
        static void Schedule(string teamId, int nLast, int nNext,
                             out List<CupMatch> last, out List<CupMatch> next)
        {
            last = Matches("teamId=" + teamId + "&limit=" + nLast + "&offset=0&order=DESC&status=FINISHED");
            List<CupMatch> raw = Matches("teamId=" + teamId + "&limit=" + (nNext + 4) +
                                         "&offset=0&order=ASC&status=UPCOMING,LIVE");
            next = new List<CupMatch>();
            DateTime from = DateTime.Now.AddDays(-1), to = DateTime.Now.AddDays(250);
            foreach (CupMatch c in raw)
                if (c.Live || (c.When > from && c.When < to)) next.Add(c);
            next.Sort(delegate (CupMatch x, CupMatch y)
            {
                if (x.Live != y.Live) return x.Live ? -1 : 1;
                return x.When.CompareTo(y.When);
            });
            if (next.Count > nNext) next = next.GetRange(0, nNext);
        }

        static readonly Dictionary<string, object[]> tables = new Dictionary<string, object[]>();

        static object[] TableFor(string comp, string season)
        {
            string key = comp + "|" + season;
            object[] t;
            if (tables.TryGetValue(key, out t)) return t;
            try { t = Get(Table + "competitionId=" + comp + "&seasonYear=" + season); }
            catch (Exception ex) { Program.Log("Tabela UEFA " + comp, ex); t = new object[0]; }
            tables[key] = t;
            return t;
        }

        // grupa zawierajaca druzyne: [grupa, pozycja druzyny]
        static bool FindTeam(object[] groups, string teamId, out object group, out object item)
        {
            foreach (object g in groups)
                foreach (object it in Json.Arr(Json.At(g, "items")))
                    if (Json.Str(Json.At(Json.At(it, "team"), "id")) == teamId)
                    {
                        group = g; item = it;
                        return true;
                    }
            group = null; item = null;
            return false;
        }

        static void LoadPolish(string season)
        {
            if (polishFor == season && (DateTime.Now - polishAt).TotalHours < 6 && polish.Count > 0) return;
            Dictionary<string, string[]> found = new Dictionary<string, string[]>();
            bool any = false;
            foreach (string comp in ClubComps)
            {
                try
                {
                    foreach (object t in Get(Teams + "competitionId=" + comp + "&seasonYear=" + season +
                                             "&limit=400&offset=0"))
                    {
                        if (Json.Str(Json.At(t, "countryCode")) != "POL") continue;
                        string id = Json.Str(Json.At(t, "id"));
                        if (id.Length == 0 || found.ContainsKey(id)) continue;
                        found[id] = new string[] { TeamName(t), Json.Str(Json.At(t, "logoUrl")) };
                    }
                    any = true;
                }
                catch (Exception ex) { Program.Log("Druzyny UEFA " + comp, ex); }
            }
            if (!any) return;                       // zostaje poprzednia lista
            polish.Clear();
            foreach (KeyValuePair<string, string[]> kv in found) polish[kv.Key] = kv.Value;
            polishFor = season;
            polishAt = DateTime.Now;
        }

        static void LoadClubs(List<CupClub> active, List<CupClub> gone)
        {
            DateTime recent = DateTime.Now.AddDays(-10);
            foreach (KeyValuePair<string, string[]> kv in polish)
            {
                CupClub c = new CupClub();
                c.Id = kv.Key; c.Name = kv.Value[0]; c.Badge = kv.Value[1];
                try { Schedule(c.Id, 3, 3, out c.Last, out c.Next); }
                catch (Exception ex) { Program.Log("Mecze UEFA " + c.Name, ex); continue; }

                CupMatch cur = c.Next.Count > 0 ? c.Next[0] : (c.Last.Count > 0 ? c.Last[0] : null);
                if (cur == null) continue;
                c.Comp = CompName(cur.CompId);
                c.CompId = cur.CompId;

                if (c.Next.Count == 0 && c.Last[0].When < recent)
                {
                    CupMatch l = c.Last[0];
                    string opp = l.Opp(c.Id);
                    if (l.AggWinner.Length > 0 && l.AggWinner != c.Id)
                    {
                        int my = l.IsHome(c.Id) ? l.AggHome : l.AggAway;
                        int th = l.IsHome(c.Id) ? l.AggAway : l.AggHome;
                        c.OutNote = "odpadł · " + l.Label.Replace(" · rewanż", "") + " · " + opp + " " + my + ":" + th;
                    }
                    else c.OutNote = l.Label + " · brak kolejnych meczów";
                    gone.Add(c);
                    continue;
                }

                // pozycja w fazie ligowej - tylko gdy klub w niej teraz gra
                if (cur.RoundEn.IndexOf("league phase", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    object g, it;
                    if (FindTeam(TableFor(cur.CompId, cur.Season), c.Id, out g, out it))
                    {
                        int rank = Json.Int(Json.At(it, "rank"));
                        int pts = Json.Int(Json.At(it, "points"));
                        int pl = Json.Int(Json.At(it, "played"));
                        c.Standing = rank + ". miejsce · " + pts + " pkt po " + pl + " " + (pl == 1 ? "meczu" : "meczach");
                        // 36 druzyn: 1-8 prosto do 1/8, 9-24 baraze, reszta odpada
                        c.Zone = rank <= 8 ? 1 : (rank <= 24 ? 2 : 3);
                    }
                }
                active.Add(c);
            }

            active.Sort(delegate (CupClub x, CupClub y)
            {
                bool lx = x.Next.Count > 0 && x.Next[0].Live, ly = y.Next.Count > 0 && y.Next[0].Live;
                if (lx != ly) return lx ? -1 : 1;
                DateTime nx = x.Next.Count > 0 ? x.Next[0].When : DateTime.MaxValue;
                DateTime ny = y.Next.Count > 0 ? y.Next[0].When : DateTime.MaxValue;
                return nx.CompareTo(ny);
            });
        }

        static void LoadNational()
        {
            List<CupMatch> last, next;
            Schedule(Poland, 3, 5, out last, out next);
            NatLast = last;
            NatNext = next;

            // tabela rozgrywek, w ktorych kadra teraz gra (towarzyskie nie maja tabeli)
            CupMatch pick = null;
            foreach (CupMatch c in next) if (c.CompId != "19") { pick = c; break; }
            if (pick == null)
                foreach (CupMatch c in last)
                    if (c.CompId != "19" && c.When > DateTime.Now.AddDays(-45)) { pick = c; break; }
            if (pick == null) { NatTable = new List<Standing>(); NatGroup = ""; return; }

            object g, it;
            if (!FindTeam(TableFor(pick.CompId, pick.Season), Poland, out g, out it)) return;

            List<Standing> rows = new List<Standing>();
            foreach (object x in Json.Arr(Json.At(g, "items")))
            {
                Standing s = new Standing();
                object t = Json.At(x, "team");
                s.Team = TeamName(t);
                s.Badge = Json.Str(Json.At(t, "logoUrl"));
                s.Rank = Json.Int(Json.At(x, "rank"));
                s.Played = Json.Int(Json.At(x, "played"));
                s.Win = Json.Int(Json.At(x, "won"));
                s.Draw = Json.Int(Json.At(x, "drawn"));
                s.Loss = Json.Int(Json.At(x, "lost"));
                s.GoalsFor = Json.Int(Json.At(x, "goalsFor"));
                s.GoalsAgainst = Json.Int(Json.At(x, "goalsAgainst"));
                s.Points = Json.Int(Json.At(x, "points"));
                s.Mine = Json.Str(Json.At(t, "id")) == Poland;
                rows.Add(s);
            }
            rows.Sort(delegate (Standing a, Standing b) { return a.Rank.CompareTo(b.Rank); });

            string grp = Json.Str(Json.At(Json.At(Json.At(g, "group"), "metaData"), "groupName"));
            grp = grp.Replace("Group", "grupa").Trim();
            NatTable = rows;
            NatComp = pick.CompId;
            NatGroup = CompShort(pick.CompId, pick.RoundEn).Replace("LN", "Liga Narodów") +
                       (grp.Length > 0 && grp != "League" ? " · " + grp : "");
        }

        public static void Refresh()
        {
            string season = SeasonYear();
            tables.Clear();                                    // tabele zmieniaja sie z kazdym meczem
            bool any = false;

            try { LoadNational(); any = true; }
            catch (Exception ex) { Program.Log("Kadra", ex); }

            try
            {
                LoadPolish(season);
                List<CupClub> active = new List<CupClub>(), gone = new List<CupClub>();
                LoadClubs(active, gone);
                Clubs = active;
                Out = gone;
                any = true;
            }
            catch (Exception ex) { Program.Log("Puchary", ex); }

            int y = int.Parse(season, CultureInfo.InvariantCulture);
            Season = (y - 1).ToString(CultureInfo.InvariantCulture) + "/" +
                     (y % 100).ToString("00", CultureInfo.InvariantCulture);
            if (!any) { Ok = false; return; }
            Stamp = DateTime.Now;
            Ok = true;
        }

        static bool Hot(List<CupMatch> list)
        {
            DateTime now = DateTime.Now;
            foreach (CupMatch c in list)
                if (c.Live || (c.When > now.AddMinutes(-150) && c.When < now.AddMinutes(10))) return true;
            return false;
        }

        // Czy wlasnie trwa (albo zaraz sie zacznie) mecz kadry lub polskiego klubu -
        // wtedy panel odswieza wyniki co dwie minuty, a nie co pol godziny.
        public static bool LiveWindow()
        {
            if (Hot(NatNext)) return true;
            foreach (CupClub c in Clubs) if (Hot(c.Next)) return true;
            return false;
        }

        public static bool AnyLive()
        {
            foreach (CupMatch c in NatNext) if (c.Live) return true;
            foreach (CupClub k in Clubs) foreach (CupMatch c in k.Next) if (c.Live) return true;
            return false;
        }

        public static string Url(string comp)
        {
            switch (comp)
            {
                case "1": return "https://www.uefa.com/uefachampionsleague/";
                case "14": return "https://www.uefa.com/uefaeuropaleague/";
                case "2019": return "https://www.uefa.com/uefaconferenceleague/";
                case "2014": return "https://www.uefa.com/uefanationsleague/";
                default: return "https://www.laczynaspilka.pl/";
            }
        }
    }
}
