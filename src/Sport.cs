using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NewsyVE
{
    class Standing
    {
        public int Rank, Played, Win, Draw, Loss, Points, GoalsFor, GoalsAgainst;
        public string Team = "", Badge = "";
        public bool Mine;               // klub wybrany w ustawieniach
    }

    class Fixture
    {
        public DateTime When;
        public int Round;
        public string Home = "", Away = "", League = "";
        public string HomeBadge = "", AwayBadge = "";
        public int HomeScore = -1, AwayScore = -1;
        public bool Played { get { return HomeScore >= 0 && AwayScore >= 0; } }
    }

    class Club
    {
        public string Id, Name, Key, Badge = "";
        public List<Fixture> Last = new List<Fixture>();
        public List<Fixture> Next = new List<Fixture>();
        public Club(string id, string name, string key) { Id = id; Name = name; Key = key; }
    }

    // Tabela i mecze Ekstraklasy pochodza z oficjalnego serwisu, bo darmowy klucz
    // TheSportsDB jest ostro przyciety: z tabeli oddaje piec pozycji, a eventslast
    // i eventsnext po JEDNYM meczu. Dla klubu zagranicznego zostaje mimo wszystko
    // TheSportsDB - obejmuje wszystkie wieksze ligi.
    static class Sport
    {
        const string Sdb = "https://www.thesportsdb.com/api/v1/json/3";
        const string Base = "https://ekstraklasa.org/";

        public static List<Standing> Table = new List<Standing>();
        // kluby z ustawien; null = pozycja wylaczona
        public static Club Home1, Home2, Abroad;
        public static DateTime Stamp = DateTime.MinValue;
        public static bool Ok;

        // Kluby Ekstraklasy na wypadek, gdyby tabela nie byla jeszcze pobrana
        // (sezon 2026/27). Lista w ustawieniach bierze najpierw aktualna tabele.
        public static readonly string[] KnownClubs = {
            "Cracovia", "GKS Katowice", "Górnik Zabrze", "Jagiellonia Białystok",
            "Korona Kielce", "Lech Poznań", "Legia Warszawa", "Motor Lublin",
            "Piast Gliwice", "Pogoń Szczecin", "Radomiak Radom", "Raków Częstochowa",
            "Śląsk Wrocław", "Widzew Łódź", "Wieczysta Kraków", "Wisła Kraków",
            "Wisła Płock", "Zagłębie Lubin"
        };

        static string cfgKey = null;

        // Po zmianie w ustawieniach tworzymy kluby od nowa - stare mecze i herb
        // nie moga zostac przy nowej nazwie.
        public static void Configure()
        {
            string key = Cfg.Club1 + "|" + Cfg.Club2 + "|" + Cfg.AbroadId;
            if (key == cfgKey) return;
            cfgKey = key;
            Home1 = Cfg.Club1.Length > 0 ? new Club("", Cfg.Club1, Cfg.Club1.ToLowerInvariant()) : null;
            Home2 = Cfg.Club2.Length > 0 && Cfg.Club2 != Cfg.Club1
                  ? new Club("", Cfg.Club2, Cfg.Club2.ToLowerInvariant()) : null;
            Abroad = Cfg.AbroadId.Length > 0
                   ? new Club(Cfg.AbroadId, Cfg.AbroadName, Cfg.AbroadName.ToLowerInvariant()) : null;
            foreach (Standing s in Table) s.Mine = IsMine(s.Team);
            foreach (Club c in new Club[] { Home1, Home2 })
                if (c != null) { c.Badge = BadgeFor(c.Name); Fill(c); }
        }

        static bool IsMine(string team)
        {
            string low = team.ToLowerInvariant();
            return (Home1 != null && low.Contains(Home1.Key)) || (Home2 != null && low.Contains(Home2.Key));
        }

        public static List<string> ClubNames()
        {
            List<string> o = new List<string>();
            foreach (Standing s in Table) o.Add(s.Team);
            if (o.Count == 0) o.AddRange(KnownClubs);
            o.Sort(string.Compare);
            return o;
        }

        // Wyszukiwarka klubow zagranicznych do ustawien: [id, nazwa, liga].
        public static List<string[]> SearchTeams(string q)
        {
            List<string[]> o = new List<string[]>();
            object root = Json.Parse(Api.Fetch(Sdb + "/searchteams.php?t=" + Uri.EscapeDataString(q.Trim())));
            foreach (object t in Json.Arr(Json.At(root, "teams")))
            {
                if (Json.Str(Json.At(t, "strSport")) != "Soccer") continue;
                o.Add(new string[] {
                    Json.Str(Json.At(t, "idTeam")), Json.Str(Json.At(t, "strTeam")),
                    Json.Str(Json.At(t, "strLeague")) });
            }
            return o;
        }

        // nazwa klubu -> adres herbu (budowane przy okazji parsowania tabeli)
        static readonly Dictionary<string, string> Crest =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // numer kolejki -> mecze (rozegrane kolejki sie nie zmieniaja, wiec je trzymamy)
        static readonly Dictionary<int, List<Fixture>> Rounds = new Dictionary<int, List<Fixture>>();

        static string SeasonSlug(int shift)
        {
            DateTime n = DateTime.Now;
            int start = (n.Month >= 7 ? n.Year : n.Year - 1) + shift;
            return start.ToString(CultureInfo.InvariantCulture) + "-" +
                   (start + 1).ToString(CultureInfo.InvariantCulture);
        }

        static readonly Regex RxCell  = new Regex("<td[^>]*>(.*?)</td>", RegexOptions.Singleline);
        static readonly Regex RxGoals = new Regex(@"(\d+)\s*:\s*(\d+)");
        static readonly Regex RxImg   = new Regex("<img[^>]*src=\"([^\"]+)\"[^>]*alt=\"([^\"]{2,40})\"");
        static readonly Regex RxMatch = new Regex("href=\"/mecz/[0-9a-f-]+/([a-z0-9-]+)/");

        static int Num(string s)
        {
            int v;
            return int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : -1;
        }

        static string Flat(string html)
        {
            string t = Regex.Replace(html, "<[^>]*?>", " ");
            return string.Join(" ", t.Split(new char[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries));
        }

        // ---------------- tabela ----------------
        // Kolejnosc komorek w wierszu: punkty, mecze, W, R, P, "bramki : stracone".
        static void LoadTable()
        {
            string html = null;
            for (int shift = 0; shift >= -1 && html == null; shift--)
            {
                try { html = Api.FetchText(Base + "tabela/" + SeasonSlug(shift) + "/"); }
                catch { html = null; }
                if (html != null && html.IndexOf("/kluby/", StringComparison.Ordinal) < 0) html = null;
            }
            if (html == null) return;

            List<Standing> rows = new List<Standing>();
            foreach (string chunk in Regex.Split(html, "href=\"/kluby/"))
            {
                Match img = RxImg.Match(chunk);
                if (!img.Success) continue;

                int end = chunk.IndexOf("</tr>", StringComparison.Ordinal);
                string body = end > 0 ? chunk.Substring(0, end) : chunk;

                List<string> cells = new List<string>();
                foreach (Match c in RxCell.Matches(body))
                {
                    string t = Flat(c.Groups[1].Value);
                    if (t.Length > 0) cells.Add(t);
                }
                if (cells.Count < 6) continue;

                Standing s = new Standing();
                s.Team   = img.Groups[2].Value.Trim();
                s.Badge  = img.Groups[1].Value.Trim();
                s.Points = Num(cells[0]);
                s.Played = Num(cells[1]);
                s.Win    = Num(cells[2]);
                s.Draw   = Num(cells[3]);
                s.Loss   = Num(cells[4]);
                if (s.Points < 0 || s.Played < 0) continue;

                Match g = RxGoals.Match(cells[5]);
                if (g.Success)
                {
                    s.GoalsFor = Num(g.Groups[1].Value);
                    s.GoalsAgainst = Num(g.Groups[2].Value);
                }

                string low = s.Team.ToLowerInvariant();
                s.Mine = IsMine(s.Team);
                if (s.Badge.Length > 0) Crest[s.Team] = s.Badge;
                if (Home1 != null && low.Contains(Home1.Key)) Home1.Badge = s.Badge;
                if (Home2 != null && low.Contains(Home2.Key)) Home2.Badge = s.Badge;

                rows.Add(s);
            }

            for (int i = 0; i < rows.Count; i++) rows[i].Rank = i + 1;
            if (rows.Count >= 10) Table = rows;      // krotsza lista = zmiana ukladu strony
        }

        // ---------------- kolejki ----------------
        // Strona kolejki podaje pary herbow (alt = nazwa), wynik i termin.
        static List<Fixture> LoadRound(int round)
        {
            string html = Api.FetchText(Base + "terminarz/" + SeasonSlug(0) + "/" +
                                        round.ToString(CultureInfo.InvariantCulture) + "/");
            List<Fixture> res = new List<Fixture>();

            MatchCollection links = RxMatch.Matches(html);
            int prev = 0;
            foreach (Match lk in links)
            {
                string block = html.Substring(prev, lk.Index - prev);
                prev = lk.Index;

                // Strona kolejki serwuje herby spod innych adresow niz tabela
                // (bez slowa "crest"), a obok nich logo ligi i sponsora. Pewnym
                // filtrem jest lista klubow zebrana wczesniej z tabeli.
                List<string> names = new List<string>();
                foreach (Match im in RxImg.Matches(block))
                {
                    string nm = im.Groups[2].Value.Trim();
                    if (nm.Length < 2 || !Crest.ContainsKey(nm)) continue;
                    if (names.Count == 0 || names[names.Count - 1] != nm) names.Add(nm);
                }
                if (names.Count < 2) continue;

                Fixture f = new Fixture();
                f.Round = round;
                f.League = "Ekstraklasa";
                f.Home = names[names.Count - 2];
                f.Away = names[names.Count - 1];
                f.HomeBadge = Crest[f.Home];
                f.AwayBadge = Crest[f.Away];

                MatchCollection nums = Regex.Matches(block, ">(\\d{1,2})<");
                if (nums.Count >= 2)
                {
                    f.HomeScore = Num(nums[nums.Count - 2].Groups[1].Value);
                    f.AwayScore = Num(nums[nums.Count - 1].Groups[1].Value);
                }

                Match dm = Regex.Match(block, ">(\\d{1,2})\\.(\\d{1,2})<");
                Match tm = Regex.Match(block, ">(\\d{1,2}):(\\d{2})<");
                if (dm.Success)
                {
                    int day = Num(dm.Groups[1].Value), mon = Num(dm.Groups[2].Value);
                    int hh = tm.Success ? Num(tm.Groups[1].Value) : 0;
                    int mi = tm.Success ? Num(tm.Groups[2].Value) : 0;
                    int year = int.Parse(SeasonSlug(0).Substring(0, 4), CultureInfo.InvariantCulture);
                    if (mon < 7) year++;                     // runda wiosenna
                    try { f.When = new DateTime(year, mon, day, hh, mi, 0); }
                    catch { f.When = DateTime.MinValue; }
                }
                res.Add(f);
            }
            return res;
        }

        public static int Round;                 // numer biezacej kolejki
        public static List<Fixture> RoundMatches = new List<Fixture>();

        static int CurrentRound()
        {
            int max = 0;
            foreach (Standing s in Table) if (s.Played > max) max = s.Played;
            return Math.Max(1, max + 1);
        }

        static void LoadFixtures()
        {
            if (Table.Count == 0) return;
            int cur = CurrentRound();
            int from = Math.Max(1, cur - 6), to = Math.Min(34, cur + 5);

            for (int r = from; r <= to; r++)
            {
                // rozegrane kolejki juz sie nie zmieniaja - pobieramy je tylko raz
                if (Rounds.ContainsKey(r) && r < cur - 1) continue;
                try { Rounds[r] = LoadRound(r); }
                catch { }
            }

            Round = cur;
            List<Fixture> rm;
            if (!Rounds.TryGetValue(cur, out rm)) Rounds.TryGetValue(cur - 1, out rm);
            RoundMatches = rm ?? new List<Fixture>();

            if (Home1 != null) Fill(Home1);
            if (Home2 != null) Fill(Home2);
        }

        static void Fill(Club c)
        {
            if (c == null) return;
            List<Fixture> mine = new List<Fixture>();
            foreach (KeyValuePair<int, List<Fixture>> kv in Rounds)
                foreach (Fixture f in kv.Value)
                {
                    if (f.Home.ToLowerInvariant().Contains(c.Key) ||
                        f.Away.ToLowerInvariant().Contains(c.Key)) mine.Add(f);
                }

            List<Fixture> past = new List<Fixture>(), next = new List<Fixture>();
            foreach (Fixture f in mine)
            {
                if (f.Played) past.Add(f);
                else next.Add(f);
            }
            past.Sort(delegate (Fixture a, Fixture b) { return b.Round.CompareTo(a.Round); });
            next.Sort(delegate (Fixture a, Fixture b) { return a.Round.CompareTo(b.Round); });
            if (past.Count > 5) past = past.GetRange(0, 5);
            if (next.Count > 5) next = next.GetRange(0, 5);
            c.Last = past;
            c.Next = next;
        }

        // ---------------- klub zagraniczny (TheSportsDB) ----------------
        static DateTime Ts(object o)
        {
            DateTime d;
            if (DateTime.TryParse(Json.Str(o), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out d))
                return d.ToLocalTime();
            return DateTime.MinValue;
        }

        static int Score(object o)
        {
            string s = Json.Str(o);
            int v;
            return (s.Length > 0 && int.TryParse(s, out v)) ? v : -1;
        }

        static List<Fixture> Events(string url, string key)
        {
            List<Fixture> res = new List<Fixture>();
            object root = Json.Parse(Api.Fetch(url));
            foreach (object e in Json.Arr(Json.At(root, key)))
            {
                Fixture m = new Fixture();
                m.Home = Json.Str(Json.At(e, "strHomeTeam"));
                m.Away = Json.Str(Json.At(e, "strAwayTeam"));
                m.League = Json.Str(Json.At(e, "strLeague"))
                    .Replace("Spanish La Liga", "La Liga")
                    .Replace("English Premier League", "Premier League")
                    .Replace("German Bundesliga", "Bundesliga")
                    .Replace("Italian Serie A", "Serie A")
                    .Replace("French Ligue 1", "Ligue 1")
                    .Replace("UEFA Champions League", "Liga Mistrzów")
                    .Replace("UEFA Europa League", "Liga Europy");
                m.When = Ts(Json.At(e, "strTimestamp"));
                m.HomeScore = Score(Json.At(e, "intHomeScore"));
                m.AwayScore = Score(Json.At(e, "intAwayScore"));
                if (m.Home.Length > 0) res.Add(m);
            }
            return res;
        }

        static void LoadAbroad()
        {
            Club club = Abroad;                  // ustawienia moga podmienic klub w trakcie
            if (club == null) return;
            List<Fixture> past = new List<Fixture>(), next = new List<Fixture>();
            try { past = Events(Sdb + "/eventslast.php?id=" + club.Id, "results"); } catch { }
            try { next = Events(Sdb + "/eventsnext.php?id=" + club.Id, "events"); } catch { }

            DateTime cut = DateTime.Now.AddHours(-2);
            List<Fixture> realNext = new List<Fixture>();
            foreach (Fixture f in next)
            {
                if (f.Played || (f.When != DateTime.MinValue && f.When < cut)) past.Add(f);
                else realNext.Add(f);
            }
            past.Sort(delegate (Fixture a, Fixture b) { return b.When.CompareTo(a.When); });
            realNext.Sort(delegate (Fixture a, Fixture b) { return a.When.CompareTo(b.When); });
            club.Last = past;
            club.Next = realNext;

            if (club.Badge.Length == 0)
            {
                try
                {
                    object root = Json.Parse(Api.Fetch(Sdb + "/lookupteam.php?id=" + club.Id));
                    object[] t = Json.Arr(Json.At(root, "teams"));
                    if (t.Length > 0) club.Badge = Json.Str(Json.At(t[0], "strBadge"));
                }
                catch { }
            }
        }

        public static string BadgeFor(string team)
        {
            if (string.IsNullOrEmpty(team)) return "";
            string v;
            if (Crest.TryGetValue(team, out v)) return v;
            foreach (KeyValuePair<string, string> kv in Crest)
                if (kv.Key.IndexOf(team, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    team.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0) return kv.Value;
            return "";
        }

        public static void Refresh()
        {
            Configure();
            bool any = false;
            try { LoadTable(); if (Table.Count > 0) any = true; } catch { }
            try { LoadFixtures(); } catch { }
            try { LoadAbroad(); } catch { }
            if (Abroad != null && Abroad.Last.Count > 0) any = true;
            if (!any) { Ok = false; return; }
            Stamp = DateTime.Now;
            Ok = true;
        }

        public static string Pl(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("Legia Warsaw", "Legia").Replace("Warsaw", "Warszawa");
        }
    }
}
