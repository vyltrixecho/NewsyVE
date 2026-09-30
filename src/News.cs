using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NewsyVE
{
    enum NewsKind { Other = 0, Road = 1, Accident = 2, Promo = 3 }

    class NewsItem
    {
        public string Title, Link, Source;
        public string Image = "";
        public string Tag = "";        // wlasna etykieta, np. wysokosc znizki
        public DateTime Date;
        public NewsKind Kind;
    }

    // Wiadomosci z krakowskich kanalow RSS. Kazdy wpis dostaje tag:
    // remont/utrudnienie, wypadek/zdarzenie albo zwykla wiadomosc.
    static class News
    {
        class Feed
        {
            public string Url, Name;
            public string[] Only;        // gdy ustawione, bierzemy tylko pasujace tytuly
            public Feed(string u, string n) { Url = u; Name = n; }
            public Feed(string u, string n, string[] only) { Url = u; Name = n; Only = only; }
        }

        static readonly Feed[] Feeds = new Feed[]
        {
            new Feed("https://www.krakow.pl/feeds/rss/komunikaty/26", "krakow.pl"),
            new Feed("https://lovekrakow.pl/rss.xml", "LoveKraków"),
            new Feed("https://www.radiokrakow.pl/rss.xml", "Radio Kraków")
        };

        // Wzorce celowo waskie - "policj" albo samo "zamkni" lapaly
        // wiadomosci niezwiazane z ruchem.
        static readonly string[] Road = {
            "remont", "objazd", "utrudni", "przebudow", "awaria",
            "zmiana trasy", "zmiany tras", "nocne prace", "prace na ul", "prace przy ul",
            "prace drogow", "roboty drogow", "zamknięta ul", "zamknięcie ul",
            "zwężen", "torowisk", "rozbudowa ul", "modernizacja ul",
            "nieprzejezdn", "korek", "korki"
        };
        static readonly string[] Accident = {
            "wypadek", "wypadku", "wypadki", "kolizj", "potrąc", "zderz",
            "pożar", "śmierteln", "ranny", "ranni", "rannych",
            "zginął", "zginęła", "ewakuacj", "czołow",
            "karetk", "strażac"
        };

        static readonly Feed[] SportFeeds = new Feed[]
        {
            new Feed("https://tvpsport.pl/rss", "TVP Sport"),
            new Feed("https://sportowefakty.wp.pl/rss.xml", "Sportowe Fakty"),
            new Feed("https://sport.pl/pub/rss/sport.htm", "Sport.pl")
        };

        // Zakladka "Puchary i kadra": tylko to, co naprawde wazne - europejskie
        // puchary i reprezentacja. Ogolne kanaly sportowe przepuszczamy przez
        // slowa kluczowe, a agregator pytamy wprost o te rozgrywki.
        static readonly string[] CupWords = {
            "mistrzów", "europy", "konferencji", "narodów", "reprezentac", "kadr",
            "selekcjoner", "biało-czerwon", "mundial", "eliminac", "uefa", "fifa"
        };

        // Zapowiedzi transmisji i relacje minuta po minucie zajmowaly polowe
        // listy, a wynik i tak widac obok w karcie meczu.
        static readonly string[] CupNoise = {
            "gdzie oglądać", "gdzie obejrzeć", "transmisj", "o której", "program tv",
            "kiedy gra", "kiedy mecz", "[live]", "(live)", "relacja live", "na żywo", "online",
            "typy", "kursy", "bukmacher", "zakład"
        };

        static readonly Feed[] CupFeeds = new Feed[]
        {
            new Feed("https://news.google.com/rss/search?q=" + Uri.EscapeDataString(
                "\"Liga Mistrzów\" OR \"Liga Europy\" OR \"Liga Konferencji\" OR " +
                "\"Liga Narodów\" OR \"reprezentacja Polski\" when:3d") +
                "&hl=pl&gl=PL&ceid=PL:pl", ""),
            new Feed("https://sportowefakty.wp.pl/rss.xml", "Sportowe Fakty", CupWords),
            new Feed("https://tvpsport.pl/rss", "TVP Sport", CupWords),
            new Feed("https://sport.pl/pub/rss/sport.htm", "Sport.pl", CupWords)
        };

        static readonly Feed[] PolFeeds = new Feed[]
        {
            new Feed("https://www.rmf24.pl/fakty/polska/feed", "RMF24"),
            new Feed("https://fakty.interia.pl/feed", "Interia"),
            new Feed("https://tvn24.pl/najnowsze.xml", "TVN24")
        };

        // Spider's Web pisze o technologii szeroko, wiec przepuszczamy tylko
        // teksty o AI; pozostale kanaly sa juz tematyczne.
        static readonly string[] AiWords = {
            "ai", "sztuczn", "chatgpt", "openai", "gemini", "copilot", "llm",
            "anthropic", "claude", "deepseek", "nvidia", "model językow",
            "sieci neuron", "uczenie maszynow", "agent"
        };

        static readonly Feed[] AiFeeds = new Feed[]
        {
            new Feed("https://www.theverge.com/rss/ai-artificial-intelligence/index.xml", "The Verge"),
            new Feed("https://www.artificialintelligence-news.com/feed/", "AI News"),
            new Feed("https://techcrunch.com/category/artificial-intelligence/feed/", "TechCrunch"),
            new Feed("https://spidersweb.pl/feed", "Spider's Web", AiWords)
        };

        static readonly Feed[] GameFeeds = new Feed[]
        {
            new Feed("https://www.gry-online.pl/rss/news.xml", "GRY-Online"),
            new Feed("https://www.eurogamer.pl/feed", "Eurogamer"),
            new Feed("https://cdaction.pl/rss", "CD-Action")
        };

        public static List<NewsItem> Items = new List<NewsItem>();
        public static List<NewsItem> GameItems = new List<NewsItem>();
        public static DateTime GameStamp = DateTime.MinValue;
        public static bool GameOk;
        public static List<NewsItem> SportItems = new List<NewsItem>();
        public static List<NewsItem> CupItems = new List<NewsItem>();
        public static DateTime CupStamp = DateTime.MinValue;
        public static bool CupOk;
        public static List<NewsItem> PolItems = new List<NewsItem>();
        public static List<NewsItem> AiItems = new List<NewsItem>();
        public static DateTime Stamp = DateTime.MinValue;
        public static DateTime SportStamp = DateTime.MinValue;
        public static DateTime PolStamp = DateTime.MinValue;
        public static DateTime AiStamp = DateTime.MinValue;
        public static bool Ok, SportOk, PolOk, AiOk;
        public static string LocalTitle = "Newsy lokalne";   // naglowek karty
        static string itemsFor = "";                         // czyja jest biezaca lista

        static NewsKind Classify(string title)
        {
            string t = title.ToLowerInvariant();
            foreach (string k in Accident) if (t.Contains(k)) return NewsKind.Accident;
            foreach (string k in Road) if (t.Contains(k)) return NewsKind.Road;
            return NewsKind.Other;
        }

        static readonly Regex RxEntity = new Regex(@"&#(x?)([0-9A-Fa-f]+);");

        // Kanaly sportowe podaja polskie znaki jako encje (&#x107;, &#322;),
        // czesto podwojnie zakodowane - stad dwa przejscia.
        static string Clean(string s)
        {
            if (s == null) return "";
            s = Regex.Replace(s, "<[^>]+>", " ");
            for (int pass = 0; pass < 2; pass++)
            {
                s = RxEntity.Replace(s, delegate (Match m)
                {
                    try
                    {
                        int code = m.Groups[1].Value.Length > 0
                            ? Convert.ToInt32(m.Groups[2].Value, 16)
                            : int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                        return (code > 0 && code <= 0x10FFFF) ? char.ConvertFromUtf32(code) : m.Value;
                    }
                    catch { return m.Value; }
                });
                s = s.Replace("&nbsp;", " ").Replace("&quot;", "\"").Replace("&apos;", "'")
                     .Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
            }
            return string.Join(" ", s.Split(new char[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries));
        }

        // Czesc serwisow (m.in. The Verge) nadaje w Atomie, gdzie wpis to <entry>,
        // tytul ma atrybuty, odnosnik siedzi w atrybucie href, a data nazywa sie
        // <updated>. Bez tego taki kanal dawal zero wpisow i cala karte zajmowal
        // jeden serwis - w dodatku ten bez zdjec.
        static readonly Regex RxSource =
            new Regex("<source[^>]*>(.*?)</source>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        static readonly Regex RxItem = new Regex(@"<(?:item|entry)(?:\s[^>]*)?>(.*?)</(?:item|entry)>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        static readonly Regex RxTitle = new Regex(@"<title(?:\s[^>]*)?>\s*(?:<!\[CDATA\[)?(.*?)(?:\]\]>)?\s*</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        static readonly Regex RxLink = new Regex(@"<link>\s*(?:<!\[CDATA\[)?(.*?)(?:\]\]>)?\s*</link>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        static readonly Regex RxLinkAtom = new Regex(@"<link[^>]*\srel=""alternate""[^>]*\shref=""([^""]+)""", RegexOptions.IgnoreCase);
        static readonly Regex RxDate = new Regex(@"<pubDate>\s*(.*?)\s*</pubDate>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        static readonly Regex RxDateAtom = new Regex(@"<(?:updated|published)>\s*(.*?)\s*</(?:updated|published)>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Kanaly trzymaja zdjecie w roznych miejscach - bierzemy pierwsze, ktore jest.
        static readonly Regex[] RxImage = {
            new Regex("<enclosure[^>]*url=\"([^\"]+)\"", RegexOptions.IgnoreCase),
            new Regex("<media:content[^>]*url=\"([^\"]+)\"", RegexOptions.IgnoreCase),
            new Regex("<media:thumbnail[^>]*url=\"([^\"]+)\"", RegexOptions.IgnoreCase),
            new Regex("<img[^>]*src=[\"']?([^\"'\\s>]+)", RegexOptions.IgnoreCase)
        };

        static string FindImage(string body)
        {
            foreach (Regex rx in RxImage)
            {
                Match m = rx.Match(body);
                if (!m.Success) continue;
                // The Verge koduje ampersandy numerycznie (&#038;), wiec sam
                // Replace na &amp; zostawialby adres, ktorego serwer nie przyjmie
                string u = Clean(m.Groups[1].Value).Trim();
                if (u.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return u;
            }
            return "";
        }

        // Newsy tematyczne: bez tagow, wystarczy swiezosc.
        public static void RefreshSport()
        {
            List<NewsItem> r = Topic(SportFeeds);
            if (r == null) { SportOk = false; return; }
            // Lista idzie na ekran od razu; dociaganie brakujacych zdjec
            // to kilka zapytan HTTP i potrafi potrwac, wiec robimy je po
            // opublikowaniu i odswiezamy panel ponownie.
            SportItems = r; SportStamp = DateTime.Now; SportOk = true;
            Store.Fire();
            FillImages(r, 6);
        }

        public static void RefreshCups()
        {
            List<NewsItem> all = Harvest(CupFeeds, false);
            if (all == null) { CupOk = false; return; }

            List<NewsItem> keep = new List<NewsItem>();
            foreach (NewsItem n in all)
            {
                string low = n.Title.ToLowerInvariant();
                bool noise = false;
                foreach (string k in CupNoise) if (low.Contains(k)) { noise = true; break; }
                if (!noise) keep.Add(n);
            }
            keep.Sort(delegate (NewsItem a, NewsItem b) { return b.Date.CompareTo(a.Date); });
            keep = Dedup(keep);

            CupItems = keep; CupStamp = DateTime.Now; CupOk = true;
            Store.Fire();

            // og:image strony Google News to logo agregatora, a nie zdjecie
            // z artykulu - te wpisy zostaja bez miniatur
            List<NewsItem> own = new List<NewsItem>();
            foreach (NewsItem n in keep)
                if (n.Link.IndexOf("news.google.", StringComparison.OrdinalIgnoreCase) < 0) own.Add(n);
            FillImages(own, 6);
        }

        public static void RefreshPolitics()
        {
            List<NewsItem> r = Topic(PolFeeds);
            if (r == null) { PolOk = false; return; }
            PolItems = r; PolStamp = DateTime.Now; PolOk = true;
        }

        // Kolejnosc na karcie: najpierw gry za darmo (wygasaja najszybciej),
        // potem promocje - Steam w zlotowkach, reszta sklepow z CheapSharka -
        // a na koncu zwykle newsy. Limity pilnuja, zeby promocje nie
        // wypchnely newsow z karty.
        public static void RefreshGames()
        {
            List<NewsItem> free = FreeGames();
            List<NewsItem> steam = SteamDeals();
            List<NewsItem> other = OtherDeals();
            List<NewsItem> news = Topic(GameFeeds);

            if (news == null && free.Count + steam.Count + other.Count == 0) { GameOk = false; return; }

            List<NewsItem> all = new List<NewsItem>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            AddUnique(all, seen, free, 5);
            // Steam i pozostale sklepy na przemian
            List<NewsItem> deals = new List<NewsItem>();
            for (int i = 0; i < Math.Max(steam.Count, other.Count); i++)
            {
                if (i < steam.Count) deals.Add(steam[i]);
                if (i < other.Count) deals.Add(other[i]);
            }
            AddUnique(all, seen, deals, 5);
            if (news != null) all.AddRange(news);

            GameItems = all;
            GameStamp = DateTime.Now;
            GameOk = true;
            Store.Fire();
            FillImages(all, 6);
        }

        static void AddUnique(List<NewsItem> to, Dictionary<string, bool> seen, List<NewsItem> from, int max)
        {
            int n = 0;
            foreach (NewsItem it in from)
            {
                if (n >= max) break;
                string key = Regex.Replace(it.Title.ToLowerInvariant(), "[^a-z0-9]", "");
                if (key.Length == 0 || seen.ContainsKey(key)) continue;
                seen[key] = true;
                to.Add(it);
                n++;
            }
        }

        // GamerPower zbiera rozdawnictwa ze wszystkich duzych sklepow. Bez filtra
        // platform zalewa liste drobiazgami z itch.io, stad zawezenie.
        static readonly Regex RxGiveaway = new Regex(@"\s*(\([^)]*\))?\s*(Key\s+)?Giveaway\s*$", RegexOptions.IgnoreCase);

        static List<NewsItem> FreeGames()
        {
            List<NewsItem> o = new List<NewsItem>();
            try
            {
                object[] arr = Json.Arr(Json.Parse(Api.Fetch(
                    "https://www.gamerpower.com/api/filter?platform=epic-games-store.steam.gog.ubisoft.origin.battlenet&type=game")));
                foreach (object it in arr)
                {
                    NewsItem n = new NewsItem();
                    n.Title = RxGiveaway.Replace(Json.Str(Json.At(it, "title")), "").Trim();
                    if (n.Title.Length == 0) continue;
                    n.Kind = NewsKind.Promo;
                    n.Tag = "ZA DARMO";
                    n.Image = Json.Str(Json.At(it, "thumbnail"));
                    n.Link = Json.Str(Json.At(it, "open_giveaway_url"));
                    n.Date = DateTime.Now;

                    string src = Platform(Json.Str(Json.At(it, "platforms")));
                    DateTime end;
                    if (DateTime.TryParse(Json.Str(Json.At(it, "end_date")), CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out end))
                    {
                        if (end < DateTime.Now) continue;
                        src += " · do " + end.ToString("dd.MM", CultureInfo.InvariantCulture);
                    }
                    string worth = Json.Str(Json.At(it, "worth"));
                    if (worth.Length > 0 && worth != "N/A") src += " · było " + worth;
                    n.Source = src;
                    o.Add(n);
                }
            }
            catch (Exception ex) { Program.Log("Darmowe gry", ex); }
            return o;
        }

        static string Platform(string list)
        {
            string l = list.ToLowerInvariant();
            if (l.Contains("epic")) return "Epic Games";
            if (l.Contains("steam")) return "Steam";
            if (l.Contains("gog")) return "GOG";
            if (l.Contains("ubisoft")) return "Ubisoft";
            if (l.Contains("origin") || l.Contains("ea app")) return "EA";
            if (l.Contains("battle")) return "Battle.net";
            return list.Length > 0 ? list : "PC";
        }

        // Promocje spoza Steama. CheapShark podaje ceny w dolarach - Steam
        // mamy osobno, w zlotowkach. Po jednej-dwie najlepsze z kazdego sklepu,
        // zeby jeden sklep nie zajal calej listy.
        static readonly string[][] Shops = {
            new string[] { "7", "GOG" }, new string[] { "25", "Epic Games" },
            new string[] { "11", "Humble" }, new string[] { "15", "Fanatical" }
        };

        static List<NewsItem> OtherDeals()
        {
            List<NewsItem> o = new List<NewsItem>();
            foreach (string[] shop in Shops)
            {
                try
                {
                    object[] arr = Json.Arr(Json.Parse(Api.Fetch(
                        "https://www.cheapshark.com/api/1.0/deals?sortBy=Deal%20Rating&pageSize=4&storeID=" + shop[0])));
                    int taken = 0;
                    foreach (object it in arr)
                    {
                        if (taken >= 2) break;
                        double sale = Json.Num(Json.At(it, "salePrice"));
                        double cut = Json.Num(Json.At(it, "savings"));
                        if (double.IsNaN(sale) || sale <= 0 || double.IsNaN(cut) || cut < 1) continue;  // darmowe sa wyzej

                        NewsItem n = new NewsItem();
                        n.Title = Json.Str(Json.At(it, "title"));
                        if (n.Title.Length == 0) continue;
                        n.Kind = NewsKind.Promo;
                        n.Tag = "−" + Math.Round(cut).ToString(CultureInfo.InvariantCulture) + "%";
                        n.Image = Json.Str(Json.At(it, "thumb"));
                        n.Date = DateTime.Now;
                        n.Source = shop[1] + " · $" + sale.ToString("0.00", CultureInfo.InvariantCulture);
                        n.Link = "https://www.cheapshark.com/redirect?dealID=" + Json.Str(Json.At(it, "dealID"));
                        o.Add(n);
                        taken++;
                    }
                }
                catch (Exception ex) { Program.Log("CheapShark " + shop[1], ex); }
            }
            return o;
        }

        static List<NewsItem> SteamDeals()
        {
            List<NewsItem> o = new List<NewsItem>();
            try
            {
                object root = Json.Parse(Api.Fetch(
                    "https://store.steampowered.com/api/featuredcategories?cc=pl&l=polish"));
                foreach (object it in Json.Arr(Json.At(Json.At(root, "specials"), "items")))
                {
                    int cut = Json.Int(Json.At(it, "discount_percent"));
                    if (cut <= 0) continue;

                    NewsItem n = new NewsItem();
                    n.Title = Json.Str(Json.At(it, "name"));
                    if (n.Title.Length == 0) continue;
                    n.Tag = "\u2212" + cut.ToString(CultureInfo.InvariantCulture) + "%";
                    n.Kind = NewsKind.Promo;
                    n.Image = Json.Str(Json.At(it, "header_image"));
                    n.Date = DateTime.Now;

                    double zl = Json.Num(Json.At(it, "final_price")) / 100.0;
                    n.Source = double.IsNaN(zl) || zl <= 0 ? "Steam"
                             : "Steam \u00b7 " + zl.ToString("0.00", CultureInfo.InvariantCulture) + " z\u0142";

                    int id = Json.Int(Json.At(it, "id"));
                    n.Link = "https://store.steampowered.com/app/" +
                             id.ToString(CultureInfo.InvariantCulture) + "/";
                    o.Add(n);
                }
            }
            catch (Exception ex) { Program.Log("Steam", ex); }
            return o;
        }

        public static void RefreshAi()
        {
            List<NewsItem> r = Topic(AiFeeds);
            if (r == null) { AiOk = false; return; }
            AiItems = r; AiStamp = DateTime.Now; AiOk = true;
            Store.Fire();
            FillImages(r, 6);
        }

        static List<NewsItem> Topic(Feed[] feeds)
        {
            List<NewsItem> all = Harvest(feeds, false);
            if (all == null) return null;
            all.Sort(delegate (NewsItem a, NewsItem b) { return b.Date.CompareTo(a.Date); });
            return Balance(Dedup(all));
        }

        // Najaktywniejszy serwis potrafi wypelnic cala karte soba - przeplatamy
        // zrodla, zeby kazde doszlo do glosu i zeby karta nie zalezala od tego,
        // ktory akurat opublikowal serie tekstow.
        static List<NewsItem> Balance(List<NewsItem> all)
        {
            List<string> order = new List<string>();
            Dictionary<string, List<NewsItem>> by = new Dictionary<string, List<NewsItem>>();
            foreach (NewsItem n in all)
            {
                if (!by.ContainsKey(n.Source))
                {
                    by[n.Source] = new List<NewsItem>();
                    order.Add(n.Source);
                }
                by[n.Source].Add(n);
            }

            List<NewsItem> o = new List<NewsItem>();
            for (int i = 0; ; i++)
            {
                bool added = false;
                foreach (string s in order)
                    if (by[s].Count > i) { o.Add(by[s][i]); added = true; }
                if (!added) break;
            }
            return o;
        }

        // Czesc serwisow (TVP Sport w ogole, Radio Krakow w wiekszosci wpisow)
        // nie daje zdjecia w kanale. Zdjecie jest za to w naglowku og:image samej
        // strony, wiec dociagamy je - ale tylko dla wpisow, ktore naprawde beda
        // widoczne, i tylko raz na artykul: wynik siedzi w ogimage.cache.
        static void FillImages(List<NewsItem> list, int max)
        {
            int used = 0;
            for (int i = 0; i < list.Count && used < max; i++)
            {
                NewsItem n = list[i];
                if (n.Image.Length > 0 || n.Link.Length == 0) continue;

                string img = OgCache.Get(n.Link);
                if (img == null)
                {
                    img = Api.OgImage(n.Link);
                    OgCache.Put(n.Link, img);
                    used++;
                }
                if (img.Length > 0) n.Image = img;
            }
            OgCache.Save();
        }

        static List<NewsItem> Dedup(List<NewsItem> all)
        {
            List<NewsItem> uniq = new List<NewsItem>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            foreach (NewsItem n in all)
            {
                string key = n.Title.Length > 40 ? n.Title.Substring(0, 40).ToLowerInvariant()
                                                 : n.Title.ToLowerInvariant();
                if (seen.ContainsKey(key)) continue;
                seen[key] = true;
                uniq.Add(n);
            }
            return uniq;
        }

        // Agregator dokleja " - Nazwa serwisu" na koncu tytulu, a nazwe
        // podaje osobno w <source>.
        static void FromAggregator(NewsItem n, string body)
        {
            Match src = RxSource.Match(body);
            if (src.Success) n.Source = Clean(src.Groups[1].Value);
            int cut = n.Title.LastIndexOf(" - ", StringComparison.Ordinal);
            if (cut > 20 && n.Title.Length - cut < 40)
                n.Title = n.Title.Substring(0, cut).Trim();
        }

        static List<NewsItem> Harvest(Feed[] feeds, bool classify)
        {
            List<NewsItem> all = new List<NewsItem>();
            bool any = false;
            foreach (Feed f in feeds)
            {
                try
                {
                    string xml = Api.FetchText(f.Url);
                    foreach (Match m in RxItem.Matches(xml))
                    {
                        string body = m.Groups[1].Value;
                        Match t = RxTitle.Match(body);
                        if (!t.Success) continue;
                        NewsItem n = new NewsItem();
                        n.Title = Clean(t.Groups[1].Value);
                        if (n.Title.Length < 5) continue;
                        Match l = RxLink.Match(body);
                        n.Link = l.Success ? Clean(l.Groups[1].Value) : "";
                        if (n.Link.Length == 0)
                        {
                            Match la = RxLinkAtom.Match(body);
                            if (la.Success) n.Link = Clean(la.Groups[1].Value);
                        }
                        Match d = RxDate.Match(body);
                        if (!d.Success) d = RxDateAtom.Match(body);
                        DateTime when;
                        if (d.Success && DateTime.TryParse(d.Groups[1].Value,
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
                            n.Date = when;
                        else
                            n.Date = DateTime.MinValue;
                        if (f.Only != null)
                        {
                            string low = " " + n.Title.ToLowerInvariant() + " ";
                            bool hit = false;
                            foreach (string k in f.Only)
                            {
                                if (k.Length <= 2)                     // "ai" tylko jako osobne slowo
                                {
                                    if (low.Contains(" " + k + " ") || low.Contains(" " + k + ".") ||
                                        low.Contains(" " + k + ",")) { hit = true; break; }
                                }
                                else if (low.Contains(k)) { hit = true; break; }
                            }
                            if (!hit) continue;
                        }
                        n.Source = f.Name;
                        if (n.Source.Length == 0) FromAggregator(n, body);
                        n.Image = FindImage(body);
                        n.Kind = classify ? Classify(n.Title) : NewsKind.Other;
                        all.Add(n);
                    }
                    any = true;
                }
                catch { }
            }
            return any ? all : null;
        }

        public static void Refresh()
        {
            List<NewsItem> all = new List<NewsItem>();
            bool any = false;

            // Serwisy miejskie maja sens tylko dla Krakowa; gdzie indziej
            // ta karta pokazuje wiadomosci ogolnopolskie.
            // Karta idzie za miejscowoscia wybrana w zakladce Pogoda. Dla samego
            // Krakowa uzywamy serwisow miejskich - nikt lepiej nie opisuje
            // remontow i objazdow. Dla kazdej innej miejscowosci, takze wsi,
            // pytamy agregator o jej nazwe; wies 20 km od Rynku to juz nie Krakow,
            // wiec krakowskie komunikaty nie sa o niej.
            Place lp = Store.CurPlace;
            bool wKrakowie = lp != null &&
                Geo.Km(lp.Lat, lp.Lon, Geo.KrakowLat, Geo.KrakowLon) <= 15;

            // Tytul karty zmienia sie natychmiast, a lista dopiero po pobraniu.
            // Bez tego przez kilkanascie sekund widac naglowek nowego miasta
            // nad newsami poprzedniego.
            string czyje = lp != null ? lp.Name : "";
            if (czyje != itemsFor)
            {
                itemsFor = czyje;
                Items = new List<NewsItem>();
                Ok = false;
                Store.Fire();
            }

            Feed[] use;
            if (wKrakowie)
            {
                use = Feeds;
                LocalTitle = "Kraków — utrudnienia i newsy";
            }
            else if (lp != null && lp.Name.Length > 0)
            {
                use = new Feed[] { new Feed(
                    "https://news.google.com/rss/search?q=" +
                    Uri.EscapeDataString(lp.Name) + "&hl=pl&gl=PL&ceid=PL:pl", "") };
                LocalTitle = lp.Name + " — newsy";
            }
            else
            {
                use = PolFeeds;
                LocalTitle = "Polska — najnowsze";
            }

            foreach (Feed f in use)
            {
                try
                {
                    string xml = Api.FetchText(f.Url);
                    foreach (Match m in RxItem.Matches(xml))
                    {
                        string body = m.Groups[1].Value;
                        Match t = RxTitle.Match(body);
                        if (!t.Success) continue;
                        NewsItem n = new NewsItem();
                        n.Title = Clean(t.Groups[1].Value);
                        if (n.Title.Length < 5) continue;
                        Match l = RxLink.Match(body);
                        n.Link = l.Success ? Clean(l.Groups[1].Value) : "";
                        if (n.Link.Length == 0)
                        {
                            Match la = RxLinkAtom.Match(body);
                            if (la.Success) n.Link = Clean(la.Groups[1].Value);
                        }
                        Match d = RxDate.Match(body);
                        if (!d.Success) d = RxDateAtom.Match(body);
                        DateTime when;
                        if (d.Success && DateTime.TryParse(d.Groups[1].Value,
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
                            n.Date = when;
                        else
                            n.Date = DateTime.MinValue;
                        n.Source = f.Name;
                        if (n.Source.Length == 0) FromAggregator(n, body);
                        n.Image = FindImage(body);
                        n.Kind = Classify(n.Title);
                        all.Add(n);
                    }
                    any = true;
                }
                catch { }
            }

            if (!any) { Ok = false; return; }

            // duplikaty miedzy serwisami
            List<NewsItem> uniq = new List<NewsItem>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            foreach (NewsItem n in all)
            {
                string key = n.Title.Length > 40 ? n.Title.Substring(0, 40).ToLowerInvariant()
                                                 : n.Title.ToLowerInvariant();
                if (seen.ContainsKey(key)) continue;
                seen[key] = true;
                uniq.Add(n);
            }

            // najpierw wypadki, potem remonty, w kazdej grupie od najnowszych
            uniq.Sort(delegate (NewsItem a, NewsItem b)
            {
                if (a.Kind != b.Kind) return ((int)b.Kind).CompareTo((int)a.Kind);
                return b.Date.CompareTo(a.Date);
            });

            Items = uniq;
            Stamp = DateTime.Now;
            Store.Fire();
            FillImages(uniq, 6);
            Ok = true;
        }
    }
}
