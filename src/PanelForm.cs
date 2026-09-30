using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace NewsyVE
{
    class Hit
    {
        public Rectangle R;
        public Action Go;
        public Hit(Rectangle r, Action go) { R = r; Go = go; }
    }

    // Caly widget na jednym plotnie - wszystko widoczne naraz, bez przewijania.
    class PanelForm : Form
    {
        public const int W = 1496, H = 980;          // maksimum; kazda zakladka ma swoja
        int TabW(int t)
        {
            if (t == 0) return 900;
            if (t == 1) return Cards.TotalWidth();
            return sportView == 1 ? CupsW : SportW();
        }

        // Sport ma dwie podzakladki: Ekstraklasa oraz puchary europejskie
        // z reprezentacja. Druga: kadra, polskie kluby w pucharach, wazne newsy.
        const int CupNatW = 380, CupClubW = 380, CupNewsW = 420;
        const int CupsW = 12 + CupNatW + 8 + CupClubW + 8 + CupNewsW + 12;
        int sportView;                // 0 = Ekstraklasa, 1 = puchary i kadra

        // Sport: tabela + kolumna klubow z Ekstraklasy + kolumna klubu zagranicznego
        // z kolejka. Bez ktorejs grupy kolumna znika, a kolejka przechodzi wyzej.
        const int SportTableW = 580, SportColW = 300;
        static int SportCols()
        {
            // kolejka zawsze ma miejsce: pod klubem zagranicznym albo we wlasnej kolumnie
            bool home = Cfg.Club1.Length > 0 || Cfg.Club2.Length > 0;
            return home ? 2 : 1;
        }
        static int SportW() { return 12 + SportTableW + SportCols() * (Gap + SportColW) + 12; }
        int CurW { get { return TabW(tab); } }
        const int Pad = 12, Gap = 8, HeadH = 30;
        const int LeftX = 12, LeftW = 400, RightX = 424, RightW = 464;
        const int MktX = 900, MktW = 268;
        const int NewsX = 1180, NewsW = 304;
        const int MapH = 300;

        readonly List<Hit> hits = new List<Hit>();
        public readonly RadarEngine Radar = new RadarEngine();
        readonly Timer anim = new Timer();
        bool playing = true;
        int speedIdx = 1;
        static readonly int[] SpeedMs = { 900, 460, 240 };
        static readonly string[] SpeedTx = { "0,5×", "1×", "2×" };
        Rectangle mapRect;
        public DateTime HiddenAt = DateTime.MinValue;
        int tab;                      // 0 = pogoda, 1 = sport
        DateTime shownAt = DateTime.MinValue;

        // Plotno ma staly uklad W x H. Na mniejszym ekranie (np. laptop 125 %,
        // czyli 1536 x 864 dla aplikacji) cale jest rysowane w skali k < 1,
        // zamiast byc ucinane od dolu. Mysz przeliczamy z powrotem przez k.
        float k = 1f;
        Point Logical(Point p) { return new Point((int)(p.X / k), (int)(p.Y / k)); }
        void InvalidateL(Rectangle r)
        {
            Invalidate(Rectangle.FromLTRB((int)Math.Floor(r.Left * k) - 1, (int)Math.Floor(r.Top * k) - 1,
                                          (int)Math.Ceiling(r.Right * k) + 1, (int)Math.Ceiling(r.Bottom * k) + 1));
        }

        public PanelForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(W, H);
            BackColor = T.Bg;
            KeyPreview = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            Radar.View = new Size(RightW - 24, MapH);
            Radar.Changed += delegate { BeginInvoke((Action)delegate { Invalidate(); }); };

            anim.Interval = SpeedMs[speedIdx];
            anim.Tick += delegate { Step(1); };
            anim.Start();

            Store.Changed += delegate
            {
                if (IsHandleCreated) BeginInvoke((Action)delegate { Invalidate(); });
            };

            // zmiana ekranu glownego, rozdzielczosci albo skali przy otwartym panelu
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += delegate
            {
                if (IsHandleCreated) BeginInvoke((Action)delegate { if (Visible) Place(); });
            };
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) HidePanel();
            base.OnKeyDown(e);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            // Tuz po otwarciu Windows potrafi nie oddac pierwszego planu procesowi
            // w tle - bez tego okresu ochronnego panel zamykalby sie sam od razu.
            if ((DateTime.Now - shownAt).TotalMilliseconds < 700)
            {
                Native.ForceForeground(Handle);
                return;
            }
            HidePanel();
        }

        public void HidePanel()
        {
            if (!Visible) return;
            HiddenAt = DateTime.Now;
            Hide();
        }

        void Step(int d)
        {
            if (Radar.Frames.Count == 0) return;
            Radar.Index = (Radar.Index + d + Radar.Frames.Count) % Radar.Frames.Count;
            Radar.EnsureReady(Radar.Index);
            InvalidateL(mapRect);
            InvalidateL(new Rectangle(RightX, mapRect.Bottom, RightW, 56));
        }

        void SetPlaying(bool p)
        {
            playing = p;
            if (p) anim.Start(); else anim.Stop();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Point m = Logical(e.Location);
            foreach (Hit h in hits)
                if (h.R.Contains(m)) { h.Go(); Invalidate(); return; }
        }

        // ---------- pomocnicze rysowanie ----------
        void Card(Graphics g, Rectangle r) { Art.Card(g, r); }

        static string Deg(double v) { return Math.Round(v).ToString(CultureInfo.InvariantCulture) + "°"; }
        static string N1(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }
        static string Hm(DateTime t) { return t.ToString("HH:mm", CultureInfo.InvariantCulture); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            hits.Clear();

            using (SolidBrush bg = new SolidBrush(T.Bg)) g.FillRectangle(bg, ClientRectangle);
            using (Pen p = new Pen(T.Line))
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            if (k < 1f) g.ScaleTransform(k, k);           // dalej wszystko w ukladzie logicznym

            DrawHeader(g);
            int top = Pad + HeadH + Gap;

            if (tab == 2)
            {
                if (sportView == 1) DrawCups(g, top);
                else DrawSport(g, top);
                return;
            }

            if (tab == 1)
            {
                int colH = H - Pad - top;
                int cx = Cards.Pad;
                foreach (int kind in Cards.Order())
                {
                    int cw = Cards.Width(kind);
                    Rectangle rc = new Rectangle(cx, top, cw, colH);
                    switch (kind)
                    {
                        case Cards.Transit:
                            DrawDepartures(g, rc);
                            break;

                        case Cards.Local:
                            DrawNews(g, rc);
                            break;

                        case Cards.Sport:
                            DrawSportNews(g, rc);
                            break;

                        case Cards.Politics:
                            // obie karty dziela kolumne; gdy zostaje jedna,
                            // dostaje cala wysokosc
                            if (Cfg.CardPolitics && Cfg.CardAi)
                            {
                                int half = (colH - Gap) / 2;
                                DrawFeed(g, new Rectangle(cx, top, cw, half),
                                    "Polityka i kraj", News.PolItems, News.PolStamp, News.PolOk,
                                    "https://www.rmf24.pl/fakty/polska");
                                DrawFeed(g, new Rectangle(cx, top + half + Gap, cw, half),
                                    "Sztuczna inteligencja", News.AiItems, News.AiStamp, News.AiOk,
                                    "https://www.theverge.com/ai-artificial-intelligence");
                            }
                            else if (Cfg.CardPolitics)
                                DrawFeed(g, rc, "Polityka i kraj", News.PolItems,
                                    News.PolStamp, News.PolOk,
                                    "https://www.rmf24.pl/fakty/polska");
                            else
                                DrawFeed(g, rc, "Sztuczna inteligencja", News.AiItems,
                                    News.AiStamp, News.AiOk,
                                    "https://www.theverge.com/ai-artificial-intelligence");
                            break;

                        case Cards.Games:
                            DrawFeed(g, rc, "Gry — darmowe i promocje", News.GameItems,
                                News.GameStamp, News.GameOk,
                                "https://www.gamerpower.com/giveaways/pc/games");
                            break;

                        case Cards.Markets:
                            DrawMarkets(g, rc);
                            break;
                    }
                    cx += cw + Cards.Gap;
                }
                return;
            }

            int ly = top;
            ly = DrawTabs(g, ly);
            ly = DrawHero(g, ly);
            ly = DrawNowcast(g, ly);
            ly = DrawHours(g, ly);
            ly = DrawDays(g, ly);
            DrawDetails(g, ly);

            int ry = top;
            ry = DrawRadar(g, ry);
            DrawIcm(g, ry);
        }

        void DrawDepartures(Graphics g, Rectangle r)
        {
            int h = r.Height;
            Card(g, r);
            // nazwy przystankow bywaja dlugie, a po prawej stoi godzina
            string tytul = "Komunikacja · " + Transit.Where;
            using (Font hf = T.F(10.5f, FontStyle.Bold))
                tytul = Trim(g, tytul.ToUpperInvariant(), hf, r.Width - 76);
            Art.Header(g, tytul, r, Transit.Ok ? Hm(Transit.Stamp) : "—");

            if (!Transit.Ok || Transit.Next.Count == 0)
            {
                using (Font f = T.F(11))
                    Art.Text(g, Transit.Ok ? "brak odjazdów" : "wczytywanie…",
                        f, T.Tx3, r.X + 12, r.Y + 36);
                return;
            }

            using (Font f = T.F(9.5f))
                Art.Text(g, Trim(g, Transit.Sub, f, r.Width - 24), f, T.Tx3, r.X + 12, r.Y + 26);

            int rowH = 30;
            int cy = r.Y + 44;
            bool favMode = Store.CurPlace != null && Store.CurPlace.Lines.Count > 0;
            if (favMode && Transit.FavCount == 0)
            {
                // ulubione ustawione, ale w najblizszym czasie nie odjezdzaja
                using (Font f = T.F(10))
                    Art.Text(g, "Ulubione linie nie odjeżdżają w najbliższym czasie.",
                        f, T.Warn, r.X + 12, cy + 2);
                cy += 22;
            }
            for (int i = 0; i < Transit.Next.Count && cy + rowH <= r.Bottom - 4; i++)
            {
                Departure d = Transit.Next[i];

                // granica miedzy ulubionymi a reszta
                if (favMode && i == Transit.FavCount && i > 0)
                {
                    if (cy + 18 + rowH > r.Bottom - 4) break;
                    using (Font f = T.F(8.5f, FontStyle.Bold))
                        Art.Text(g, "INNE LINIE", f, T.Tx3, r.X + 12, cy + 3);
                    using (Pen pen = new Pen(Color.FromArgb(70, T.Line)))
                        g.DrawLine(pen, r.X + 84, cy + 9, r.Right - 12, cy + 9);
                    cy += 18;
                }

                Rectangle chip = new Rectangle(r.X + 12, cy + 3, 38, 19);
                using (GraphicsPath p = T.Round(chip, 5))
                using (SolidBrush b = new SolidBrush(d.Fav ? Color.FromArgb(60, T.Acc) : T.Card2))
                using (Pen pen = new Pen(d.Fav ? Color.FromArgb(150, T.Acc) : T.Line))
                { g.FillPath(b, p); g.DrawPath(pen, p); }
                using (Font f = T.F(11.5f, FontStyle.Bold))
                    CenterText(g, d.Line, f, d.Fav ? T.Tx : (favMode ? T.Tx2 : T.Tx), chip.X, chip.Y + 3, chip.Width);

                using (Font f = T.F(11.5f))
                    Art.Text(g, Trim(g, d.Direction, f, r.Width - 130), f, T.Tx, r.X + 58, cy + 2);
                using (Font f = T.F(8.5f))
                    Art.Text(g, d.Stop, f, T.Tx3, r.X + 58, cy + 16);

                string mins = Transit.Minutes(d);
                Color mc = d.Seconds <= 60 ? T.Ok : T.Tx;
                using (Font f = T.F(12, FontStyle.Bold))
                    Art.TextRight(g, mins, f, mc, r.Right - 12, cy + 3);
                if (d.Live)
                    using (Font f = T.F(8))
                        Art.TextRight(g, d.Delayed ? "na żywo · " + d.Time : "na żywo",
                            f, d.Delayed ? T.Warn : T.Tx3, r.Right - 12, cy + 17);
                cy += rowH;
            }
        }

        static string Trim(Graphics g, string s, Font f, int maxW)
        {
            if (g.MeasureString(s, f).Width <= maxW) return s;
            while (s.Length > 3 && g.MeasureString(s + "…", f).Width > maxW)
                s = s.Substring(0, s.Length - 1);
            return s + "…";
        }

        void DrawNews(Graphics g, Rectangle r)
        {
            Card(g, r);
            Art.Header(g, News.LocalTitle, r,
                News.Ok ? Hm(News.Stamp) : "—");

            if (!News.Ok || News.Items.Count == 0)
            {
                using (Font f = T.F(11))
                    Art.Text(g, "wczytywanie…", f, T.Tx3, r.X + 12, r.Y + 36);
                return;
            }

            int cy = r.Y + 30;
            int bottom = r.Bottom - 10;
            int ThW = 72 * Cfg.Scale / 100, ThH = 58 * Cfg.Scale / 100;
            using (Font ft = T.F(12f * Cfg.Scale / 100f))
            using (Font fs = T.F(9f * Cfg.Scale / 100f))
            using (Font fk = T.F(8.5f, FontStyle.Bold))
            {
                foreach (NewsItem n in News.Items)
                {
                    if (cy + ThH > bottom) break;

                    bool hasImg = n.Image.Length > 0;
                    int tx = r.X + 12;
                    if (hasImg)
                    {
                        Images.Thumb(g, n.Image, new Rectangle(r.X + 12, cy, ThW, ThH));
                        tx = r.X + 12 + ThW + 8;
                    }
                    int tw = r.Right - 12 - tx;
                    int ty = cy;

                    string tag = n.Kind == NewsKind.Accident ? "ZDARZENIE"
                               : n.Kind == NewsKind.Road ? "REMONT" : "";
                    Color tc = n.Kind == NewsKind.Accident ? T.Bad
                             : n.Kind == NewsKind.Road ? T.Warn : T.Tx3;
                    if (tag.Length > 0)
                    {
                        SizeF ts = g.MeasureString(tag, fk);
                        Rectangle chip = new Rectangle(tx, ty, (int)ts.Width + 10, 14);
                        using (GraphicsPath cp = T.Round(chip, 4))
                        using (SolidBrush cb = new SolidBrush(Color.FromArgb(46, tc)))
                        using (Pen cpen = new Pen(Color.FromArgb(110, tc)))
                        { g.FillPath(cb, cp); g.DrawPath(cpen, cp); }
                        Art.Text(g, tag, fk, tc, tx + 5, ty + 1);
                        ty += 16;
                    }

                    DrawWrapped(g, n.Title, ft, T.Tx, tx, ty, tw, tag.Length > 0 ? 2 : 3);

                    string when = n.Date == DateTime.MinValue ? n.Source
                                : n.Source + " · " + Ago(n.Date);
                    Art.Text(g, Trim(g, when, fs, tw), fs, T.Tx3, tx, cy + ThH - 11);

                    cy += ThH + 8;
                    if (cy < bottom - 10)
                        using (Pen pen = new Pen(Color.FromArgb(70, T.Line)))
                            g.DrawLine(pen, r.X + 12, cy - 4, r.Right - 12, cy - 4);
                }
            }
            hits.Add(new Hit(r, MakeOpen("https://www.krakow.pl/aktualnosci")));
        }

        // ---------- zakladka Sport ----------
        void DrawSport(Graphics g, int top)
        {
            const int TableW = SportTableW, ColW = SportColW;
            int h = H - Pad - top;
            int x = 12 + TableW + Gap;

            DrawTable(g, new Rectangle(12, top, TableW, h));

            // kluby z Ekstraklasy jeden pod drugim, po 5 meczow wstecz i w przod
            List<Club> home = new List<Club>();
            if (Sport.Home1 != null) home.Add(Sport.Home1);
            if (Sport.Home2 != null) home.Add(Sport.Home2);
            if (home.Count == 0 && (Cfg.Club1.Length > 0 || Cfg.Club2.Length > 0))
            {
                // kluby ustawione, ale dane jeszcze nie przyszly
                Rectangle r0 = new Rectangle(x, top, ColW, h);
                Card(g, r0);
                using (Font f = T.F(11)) Art.Text(g, "wczytywanie…", f, T.Tx3, r0.X + 12, r0.Y + 14);
                x += ColW + Gap;
            }
            else if (home.Count > 0)
            {
                int ch = (h - (home.Count - 1) * Gap) / home.Count;
                for (int i = 0; i < home.Count; i++)
                    DrawClub(g, new Rectangle(x, top + i * (ch + Gap), ColW, ch), home[i], true);
                x += ColW + Gap;
            }

            // klub zagraniczny celowo mniejszy, pod nim reszta biezacej kolejki
            if (Cfg.AbroadId.Length > 0)
            {
                int abH = 226;
                if (Sport.Abroad != null)
                    DrawClub(g, new Rectangle(x, top, ColW, abH), Sport.Abroad, false);
                else
                {
                    Rectangle r1 = new Rectangle(x, top, ColW, abH);
                    Card(g, r1);
                    using (Font f = T.F(11)) Art.Text(g, "wczytywanie…", f, T.Tx3, r1.X + 12, r1.Y + 14);
                }
                DrawRound(g, new Rectangle(x, top + abH + Gap, ColW, h - abH - Gap));
            }
            else DrawRound(g, new Rectangle(x, top, ColW, h));
        }

        // Pozostale spotkania biezacej kolejki - dane i tak sa juz pobrane.
        void DrawRound(Graphics g, Rectangle r)
        {
            Card(g, r);
            Art.Header(g, Sport.Round > 0 ? "Kolejka " + Sport.Round : "Kolejka", r,
                Sport.RoundMatches.Count > 0 ? Sport.RoundMatches.Count + " meczów" : "wczytywanie…");

            int cy = r.Y + 32, bottom = r.Bottom - 8;
            using (Font fd = T.F(9))
            using (Font ft = T.F(10))
            using (Font fsc = T.F(11, FontStyle.Bold))
            {
                foreach (Fixture m in Sport.RoundMatches)
                {
                    if (cy + 34 > bottom) break;

                    string date = m.When == DateTime.MinValue ? ""
                        : m.When.ToString("dd.MM", CultureInfo.InvariantCulture) + " · " +
                          m.When.ToString("HH:mm", CultureInfo.InvariantCulture);
                    Art.Text(g, date, fd, T.Tx3, r.X + 12, cy);

                    if (m.Played)
                        Art.TextRight(g, m.HomeScore + ":" + m.AwayScore, fsc, T.Tx, r.Right - 12, cy - 2);

                    cy += 14;
                    Images.Draw(g, m.HomeBadge, r.X + 12, cy, 16);
                    Art.Text(g, Trim(g, Sport.Pl(m.Home), ft, 92), ft, T.Tx2, r.X + 32, cy);
                    Art.Text(g, "–", ft, T.Tx3, r.X + 132, cy);
                    Images.Draw(g, m.AwayBadge, r.X + 146, cy, 16);
                    Art.Text(g, Trim(g, Sport.Pl(m.Away), ft, 92), ft, T.Tx2, r.X + 166, cy);

                    cy += 20;
                    if (cy < bottom - 8)
                        using (Pen pen = new Pen(Color.FromArgb(70, T.Line)))
                            g.DrawLine(pen, r.X + 12, cy - 4, r.Right - 12, cy - 4);
                    cy += 2;
                }
            }
        }

        // ---------- Sport: puchary i kadra ----------
        // Przelacznik podzakladek obok glownych zakladek - widoczny tylko w Sporcie.
        void DrawSportViews(Graphics g, int x)
        {
            string[] names = { "Ekstraklasa", "Puchary i kadra" };
            bool live = Cups.AnyLive();
            using (Font f = T.F(11, FontStyle.Bold))
            {
                int[] w = new int[names.Length];
                int total = 4;
                for (int i = 0; i < names.Length; i++)
                {
                    w[i] = (int)g.MeasureString(names[i], f).Width + 18 + (i == 1 && live ? 12 : 0);
                    total += w[i];
                }
                Rectangle box = new Rectangle(x, Pad + 2, total, 24);
                using (GraphicsPath p = T.Round(box, 8))
                using (SolidBrush b = new SolidBrush(T.Bg2))
                using (Pen pen = new Pen(T.Line))
                { g.FillPath(b, p); g.DrawPath(pen, p); }

                int sx = box.X + 2;
                for (int i = 0; i < names.Length; i++)
                {
                    Rectangle seg = new Rectangle(sx, box.Y + 2, w[i], box.Height - 4);
                    bool on = i == sportView;
                    if (on)
                        using (GraphicsPath p = T.Round(seg, 6))
                        using (SolidBrush b = new SolidBrush(T.Card2))
                            g.FillPath(b, p);
                    Art.Text(g, names[i], f, on ? T.Tx : T.Tx3, seg.X + 9, seg.Y + 3);
                    // mecz kadry albo polskiego klubu trwa - czerwona kropka
                    if (i == 1 && live)
                        using (SolidBrush b = new SolidBrush(T.Bad))
                            g.FillEllipse(b, seg.Right - 15, seg.Y + 7, 7, 7);
                    int idx = i;
                    hits.Add(new Hit(seg, delegate { SetSportView(idx); }));
                    sx += w[i];
                }
            }
        }

        void DrawCups(Graphics g, int top)
        {
            int h = H - Pad - top;
            int x = 12;
            DrawNational(g, new Rectangle(x, top, CupNatW, h));
            x += CupNatW + Gap;
            DrawCupClubs(g, new Rectangle(x, top, CupClubW, h));
            x += CupClubW + Gap;
            DrawFeed(g, new Rectangle(x, top, CupNewsW, h), "Ważne — puchary i kadra",
                News.CupItems, News.CupStamp, News.CupOk, "https://www.laczynaspilka.pl/");
        }

        void DrawNational(Graphics g, Rectangle r)
        {
            Card(g, r);
            Images.Draw(g, "https://img.uefa.com/imgml/flags/70x70/POL.png", r.X + 12, r.Y + 8, 22);
            using (Font f = T.F(12.5f, FontStyle.Bold))
                Art.Text(g, "REPREZENTACJA POLSKI", f, T.Tx2, r.X + 40, r.Y + 11);
            using (Font f = T.F(10))
                Art.TextRight(g, Cups.Ok ? Hm(Cups.Stamp) : "wczytywanie…", f, T.Tx3, r.Right - 12, r.Y + 13);
            if (!Cups.Ok) return;

            int y = r.Y + 40;
            List<CupMatch> next = new List<CupMatch>();
            foreach (CupMatch m in Cups.NatNext)
            {
                if (m.Live) y = LiveBox(g, r, y, m, Cups.Poland);
                else next.Add(m);
            }

            if (Cups.NatTable.Count > 0)
            {
                y = DrawGroup(g, r, y);
                y += 10;
            }

            y = CupRows(g, r, y, "Rozegrane", Cups.NatLast, Cups.Poland, 3);
            y += 6;
            CupRows(g, r, y, "Nadchodzące", next, Cups.Poland, 5);
            hits.Add(new Hit(r, MakeOpen(Cups.Url(Cups.NatComp))));
        }

        // Mala tabela grupy kadry (Liga Narodow, eliminacje).
        int DrawGroup(Graphics g, Rectangle r, int y)
        {
            int x = r.X + 12;
            int cM = r.Right - 186, cW = r.Right - 160, cR = r.Right - 136, cP = r.Right - 112;
            int cB = r.Right - 50, cPkt = r.Right - 12;
            using (Font fh = T.F(9, FontStyle.Bold))
            {
                Art.Text(g, Cups.NatGroup.ToUpperInvariant(), fh, T.Tx3, x, y);
                y += 18;
                Art.TextRight(g, "M", fh, T.Tx3, cM, y);
                Art.TextRight(g, "W", fh, T.Tx3, cW, y);
                Art.TextRight(g, "R", fh, T.Tx3, cR, y);
                Art.TextRight(g, "P", fh, T.Tx3, cP, y);
                Art.TextRight(g, "BRAMKI", fh, T.Tx3, cB, y);
                Art.TextRight(g, "PKT", fh, T.Tx3, cPkt, y);
            }
            y += 15;
            using (Pen pen = new Pen(T.Line)) g.DrawLine(pen, x, y, r.Right - 12, y);
            y += 4;

            const int rowH = 26;
            foreach (Standing s in Cups.NatTable)
            {
                if (s.Mine)
                    using (GraphicsPath p = T.Round(new Rectangle(x - 4, y, r.Width - 16, rowH - 2), 6))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(38, T.Acc)))
                    using (Pen pen = new Pen(Color.FromArgb(90, T.Acc)))
                    { g.FillPath(b, p); g.DrawPath(pen, p); }

                int ty = y + 5;
                using (Font f = T.F(11, s.Mine ? FontStyle.Bold : FontStyle.Regular))
                {
                    Art.TextRight(g, s.Rank.ToString(CultureInfo.InvariantCulture), f,
                        s.Rank == 1 ? T.Ok : T.Tx3, x + 12, ty);
                    Images.Draw(g, s.Badge, x + 18, y + 4, 17);
                    Art.Text(g, Trim(g, s.Team, f, cM - 30 - (x + 40)), f, s.Mine ? T.Tx : T.Tx2, x + 40, ty);
                    Art.TextRight(g, s.Played.ToString(CultureInfo.InvariantCulture), f, T.Tx2, cM, ty);
                    Art.TextRight(g, s.Win.ToString(CultureInfo.InvariantCulture), f, T.Tx2, cW, ty);
                    Art.TextRight(g, s.Draw.ToString(CultureInfo.InvariantCulture), f, T.Tx2, cR, ty);
                    Art.TextRight(g, s.Loss.ToString(CultureInfo.InvariantCulture), f, T.Tx2, cP, ty);
                    Art.TextRight(g, s.GoalsFor + ":" + s.GoalsAgainst, f, T.Tx2, cB, ty);
                }
                using (Font f = T.F(12, FontStyle.Bold))
                    Art.TextRight(g, s.Points.ToString(CultureInfo.InvariantCulture), f, T.Tx, cPkt, ty - 1);
                y += rowH;
            }
            return y;
        }

        void DrawCupClubs(Graphics g, Rectangle r)
        {
            Card(g, r);
            Art.Header(g, "Polskie kluby w pucharach", r,
                Cups.Ok ? (Cups.Season.Length > 0 ? "sezon " + Cups.Season : "") : "wczytywanie…");
            if (!Cups.Ok) return;

            int y = r.Y + 34;
            if (Cups.Clubs.Count == 0)
                using (Font f = T.F(11))
                {
                    Art.Text(g, "Żaden polski klub nie ma teraz meczów w pucharach.", f, T.Tx3, r.X + 12, y);
                    y += 24;
                }

            int outH = Cups.Out.Count > 0 ? 26 + Cups.Out.Count * 34 : 0;
            int per = Cups.Clubs.Count >= 3 ? 2 : 3;          // po tyle meczow wstecz i w przod
            for (int i = 0; i < Cups.Clubs.Count; i++)
            {
                CupClub c = Cups.Clubs[i];
                if (y + 110 > r.Bottom - outH) break;
                if (i > 0)
                {
                    using (Pen pen = new Pen(T.Line)) g.DrawLine(pen, r.X + 12, y - 6, r.Right - 12, y - 6);
                    y += 4;
                }

                Images.Draw(g, c.Badge, r.X + 12, y, 22);
                using (Font f = T.F(12.5f, FontStyle.Bold))
                    Art.Text(g, Trim(g, c.Name.ToUpperInvariant(), f, r.Width - 170), f, T.Tx, r.X + 40, y + 3);
                using (Font f = T.F(10))
                    Art.TextRight(g, c.Comp, f, T.Tx3, r.Right - 12, y + 5);
                y += 28;

                if (c.Standing.Length > 0)
                {
                    string zone = c.Zone == 1 ? "strefa awansu" : (c.Zone == 2 ? "strefa baraży" : "poza strefą awansu");
                    Color zc = c.Zone == 1 ? T.Ok : (c.Zone == 2 ? T.Warn : T.Bad);
                    using (Font f = T.F(10.5f))
                    {
                        Art.Text(g, c.Standing, f, T.Tx2, r.X + 12, y);
                        Art.TextRight(g, zone, f, zc, r.Right - 12, y);
                    }
                    y += 20;
                }

                List<CupMatch> next = new List<CupMatch>();
                foreach (CupMatch m in c.Next)
                {
                    if (m.Live) y = LiveBox(g, r, y, m, c.Id);
                    else next.Add(m);
                }
                y = CupRows(g, r, y, "Rozegrane", c.Last, c.Id, per);
                y += 2;
                y = CupRows(g, r, y, "Nadchodzące", next, c.Id, per);
                y += 12;
            }

            // kluby, ktore w tym sezonie juz odpadly - jedna linijka na klub
            if (Cups.Out.Count > 0 && y + 40 < r.Bottom)
            {
                y += 2;
                using (Font fh = T.F(9, FontStyle.Bold))
                    Art.Text(g, "ZAKOŃCZYLI GRĘ W PUCHARACH", fh, T.Tx3, r.X + 12, y);
                y += 18;
                using (Font fn = T.F(10.5f))
                using (Font fs = T.F(9))
                    foreach (CupClub c in Cups.Out)
                    {
                        if (y + 30 > r.Bottom - 8) break;
                        Images.Draw(g, c.Badge, r.X + 12, y + 1, 16);
                        Art.Text(g, c.Name, fn, T.Tx2, r.X + 34, y);
                        Art.Text(g, Trim(g, c.OutNote, fs, r.Width - 46), fs, T.Tx3, r.X + 34, y + 15);
                        y += 34;
                    }
            }

            string comp = Cups.Clubs.Count > 0 ? Cups.Clubs[0].CompId : "14";
            hits.Add(new Hit(r, MakeOpen(Cups.Url(comp))));
        }

        // Trwajacy mecz: wyraznie, z wynikiem i minuta.
        int LiveBox(Graphics g, Rectangle r, int y, CupMatch m, string me)
        {
            if (y + 62 > r.Bottom - 8) return y;
            Rectangle box = new Rectangle(r.X + 10, y, r.Width - 20, 56);
            using (GraphicsPath p = T.Round(box, 8))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(30, T.Bad)))
            using (Pen pen = new Pen(Color.FromArgb(120, T.Bad)))
            { g.FillPath(b, p); g.DrawPath(pen, p); }

            using (Font f = T.F(9, FontStyle.Bold))
                Art.Text(g, m.Minute.Length > 0 ? "NA ŻYWO · " + m.Minute : "NA ŻYWO",
                         f, T.Bad, box.X + 10, box.Y + 6);
            using (Font f = T.F(9))
                Art.TextRight(g, Trim(g, m.Label, f, box.Width - 110), f, T.Tx3, box.Right - 10, box.Y + 6);

            int cy = box.Y + 26;
            int mid = box.X + box.Width / 2;
            string score = m.HomeScore + " : " + m.AwayScore;
            using (Font fs = T.F(17, FontStyle.Bold))
            using (Font ft = T.F(11, FontStyle.Bold))
            {
                SizeF ss = g.MeasureString(score, fs);
                Art.Text(g, score, fs, T.Tx, mid - ss.Width / 2, cy - 5);
                int side = (int)(box.Width / 2 - ss.Width / 2 - 36);
                Images.Draw(g, m.HomeBadge, mid - ss.Width / 2 - 26, cy - 1, 18);
                Art.TextRight(g, Trim(g, m.Home, ft, side), ft, m.HomeId == me ? T.Tx : T.Tx2,
                              mid - ss.Width / 2 - 30, cy);
                Images.Draw(g, m.AwayBadge, mid + ss.Width / 2 + 8, cy - 1, 18);
                Art.Text(g, Trim(g, m.Away, ft, side), ft, m.AwayId == me ? T.Tx : T.Tx2,
                         mid + ss.Width / 2 + 30, cy);
            }
            return y + 64;
        }

        // Lista meczow z perspektywy jednej druzyny - jak w kartach klubow Ekstraklasy,
        // plus wynik dwumeczu pod rewanzem.
        int CupRows(Graphics g, Rectangle r, int y, string title, List<CupMatch> list, string me, int max)
        {
            if (y + 34 > r.Bottom - 10) return y;
            using (Font f = T.F(9, FontStyle.Bold))
                Art.Text(g, title.ToUpperInvariant(), f, T.Tx3, r.X + 12, y);
            y += 16;
            if (list.Count == 0)
            {
                using (Font f = T.F(10)) Art.Text(g, "brak meczów", f, T.Tx3, r.X + 12, y);
                return y + 18;
            }

            int shown = 0;
            using (Font fd = T.F(9))
            using (Font ft = T.F(10.5f))
            using (Font fs = T.F(12, FontStyle.Bold))
            using (Font fn = T.F(9))
            {
                foreach (CupMatch m in list)
                {
                    if (shown >= max) break;
                    bool agg = m.AggHome >= 0 && m.AggAway >= 0;
                    if (y + 34 + (agg ? 13 : 0) > r.Bottom - 10) break;

                    string date = m.When.ToString("dd.MM", CultureInfo.InvariantCulture);
                    if (!m.Done)
                    {
                        if (m.When.Date == DateTime.Today) date = "dziś";
                        else if (m.When.Date == DateTime.Today.AddDays(1)) date = "jutro";
                        date += " · " + m.When.ToString("HH:mm", CultureInfo.InvariantCulture);
                    }
                    Art.Text(g, date, fd, T.Tx3, r.X + 12, y);
                    Art.TextRight(g, Trim(g, m.Label, fd, r.Width - 110), fd, T.Tx3, r.Right - 12, y);
                    y += 13;

                    Images.Draw(g, m.OppBadge(me), r.X + 12, y - 1, 18);
                    Art.Text(g, Trim(g, m.Opp(me), ft, r.Width - 130), ft, T.Tx, r.X + 35, y);

                    if (m.Done)
                    {
                        int mine = m.Mine(me), th = m.Theirs(me);
                        string sc = mine + ":" + th;
                        int pm = m.IsHome(me) ? m.PenHome : m.PenAway, pt = m.IsHome(me) ? m.PenAway : m.PenHome;
                        bool pens = pm >= 0 && pt >= 0 && (pm + pt) > 0;
                        if (pens) sc += " (k. " + pm + ":" + pt + ")";
                        int a = pens ? pm : mine, b = pens ? pt : th;
                        Color c = a > b ? T.Ok : (a == b ? T.Warn : T.Bad);
                        Art.TextRight(g, sc, fs, c, r.Right - 12, y - 2);
                    }
                    else
                        Art.TextRight(g, m.IsHome(me) ? "u siebie" : "wyjazd", fd, T.Tx3, r.Right - 12, y + 2);
                    y += 20;

                    if (agg)
                    {
                        int am = m.IsHome(me) ? m.AggHome : m.AggAway, at = m.IsHome(me) ? m.AggAway : m.AggHome;
                        bool won = m.AggWinner == me;
                        string how = m.AggReason == "WIN_ON_PENALTIES" ? " po karnych"
                                   : m.AggReason == "WIN_ON_EXTRA_TIME" ? " po dogrywce" : "";
                        string txt = "dwumecz " + am + ":" + at;
                        if (m.AggWinner.Length > 0) txt += " · " + (won ? "awans" : "odpada") + how;
                        Art.Text(g, txt, fn, won ? T.Ok : T.Bad, r.X + 35, y - 3);
                        y += 13;
                    }

                    using (Pen pen = new Pen(Color.FromArgb(70, T.Line)))
                        g.DrawLine(pen, r.X + 12, y - 2, r.Right - 12, y - 2);
                    y += 4;
                    shown++;
                }
            }
            return y;
        }

        void DrawSportNews(Graphics g, Rectangle r)
        {
            DrawFeed(g, r, "Newsy sportowe", News.SportItems, News.SportStamp, News.SportOk,
                "https://sportowefakty.wp.pl/");
        }

        // Wspolna lista newsow tematycznych: miniatura po lewej, tytul obok.
        void DrawFeed(Graphics g, Rectangle r, string title, List<NewsItem> items,
                      DateTime stamp, bool ok, string url)
        {
            Card(g, r);
            Art.Header(g, title, r, ok ? Hm(stamp) : "wczytywanie…");
            if (!ok || items.Count == 0) return;

            int cy = r.Y + 30, bottom = r.Bottom - 10;
            int ThW = 76 * Cfg.Scale / 100, ThH = 60 * Cfg.Scale / 100;
            using (Font ft = T.F(12f * Cfg.Scale / 100f))
            using (Font fs = T.F(9f * Cfg.Scale / 100f))
            {
                foreach (NewsItem n in items)
                {
                    if (cy + ThH > bottom) break;

                    // bez zdjecia nie rezerwujemy miejsca - tytul dostaje cala szerokosc
                    bool hasImg = n.Image.Length > 0;
                    int tx = r.X + 12;
                    if (hasImg)
                    {
                        Images.Thumb(g, n.Image, new Rectangle(r.X + 12, cy, ThW, ThH));
                        tx = r.X + 12 + ThW + 8;
                    }
                    int tw = r.Right - 12 - tx;
                    int ty = cy;

                    // wpisy z wlasna etykieta (np. znizka) dostaja chip nad tytulem
                    if (n.Tag.Length > 0)
                    {
                        Color tc = n.Kind == NewsKind.Promo ? T.Ok : T.Warn;
                        using (Font fk = T.F(8, FontStyle.Bold))
                        {
                            SizeF ts = g.MeasureString(n.Tag, fk);
                            Rectangle chip = new Rectangle(tx, ty, (int)ts.Width + 10, 14);
                            using (GraphicsPath cp = T.Round(chip, 4))
                            using (SolidBrush cb = new SolidBrush(Color.FromArgb(46, tc)))
                            using (Pen cpen = new Pen(Color.FromArgb(110, tc)))
                            { g.FillPath(cb, cp); g.DrawPath(cpen, cp); }
                            Art.Text(g, n.Tag, fk, tc, tx + 5, ty + 1);
                        }
                        ty += 16;
                        DrawWrapped(g, n.Title, ft, T.Tx, tx, ty, tw, 2);
                    }
                    else DrawWrapped(g, n.Title, ft, T.Tx, tx, ty, tw, 3);

                    // promocje nie maja daty publikacji - "1 min temu" nic nie mowi
                    string when = n.Date == DateTime.MinValue || n.Kind == NewsKind.Promo ? n.Source
                                : n.Source + " · " + Ago(n.Date);
                    Art.Text(g, Trim(g, when, fs, tw), fs, T.Tx3, tx, cy + ThH - 12);

                    // klik w pozycje otwiera ja sama; hit karty (dodany na koncu) lapie reszte
                    if (n.Link.Length > 0)
                        hits.Add(new Hit(new Rectangle(r.X + 6, cy - 4, r.Width - 12, ThH + 8), MakeOpen(n.Link)));

                    cy += ThH + 8;
                    if (cy < bottom - 10)
                        using (Pen pen = new Pen(Color.FromArgb(70, T.Line)))
                            g.DrawLine(pen, r.X + 12, cy - 4, r.Right - 12, cy - 4);
                }
            }
            hits.Add(new Hit(r, MakeOpen(url)));
        }

        void DrawTable(Graphics g, Rectangle r)
        {
            Card(g, r);
            Art.Header(g, "PKO BP Ekstraklasa — tabela", r,
                Sport.Table.Count > 0 ? Sport.Table.Count + " drużyn" : "wczytywanie…");

            int x = r.X + 12;
            int[] col = { 0, 34, 300, 350, 396, 442, 488, 540 };
            using (Font fh = T.F(9, FontStyle.Bold))
            {
                int hy = r.Y + 30;
                Art.Text(g, "KLUB", fh, T.Tx3, x + col[1], hy);
                Art.TextRight(g, "M", fh, T.Tx3, x + col[2] + 24, hy);
                Art.TextRight(g, "W", fh, T.Tx3, x + col[3] + 24, hy);
                Art.TextRight(g, "R", fh, T.Tx3, x + col[4] + 24, hy);
                Art.TextRight(g, "P", fh, T.Tx3, x + col[5] + 24, hy);
                Art.TextRight(g, "BRAMKI", fh, T.Tx3, x + col[6] + 46, hy);
                Art.TextRight(g, "PKT", fh, T.Tx3, x + col[7] + 28, hy);
            }
            using (Pen pen = new Pen(T.Line)) g.DrawLine(pen, x, r.Y + 44, r.Right - 12, r.Y + 44);

            if (Sport.Table.Count == 0) return;

            int rowH = Math.Min(34, (r.Height - 62) / Math.Max(1, Sport.Table.Count));
            for (int i = 0; i < Sport.Table.Count; i++)
            {
                Standing s = Sport.Table[i];
                int cy = r.Y + 50 + i * rowH;
                if (cy + rowH > r.Bottom - 8) break;

                if (s.Mine)
                    using (GraphicsPath p = T.Round(new Rectangle(x - 4, cy - 1, r.Width - 16, rowH - 2), 6))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(38, T.Acc)))
                    using (Pen pen = new Pen(Color.FromArgb(90, T.Acc)))
                    { g.FillPath(b, p); g.DrawPath(pen, p); }

                Color tc = s.Mine ? T.Tx : T.Tx2;
                int ty = cy + (rowH - 15) / 2;
                using (Font f = T.F(11, s.Mine ? FontStyle.Bold : FontStyle.Regular))
                {
                    Art.TextRight(g, s.Rank.ToString(CultureInfo.InvariantCulture), f,
                        s.Rank <= 3 ? T.Ok : T.Tx3, x + 22, ty);
                    Images.Draw(g, s.Badge, x + col[1], cy + (rowH - 20) / 2f, 20);
                    Art.Text(g, Sport.Pl(s.Team), f, tc, x + col[1] + 26, ty);
                    Art.TextRight(g, s.Played.ToString(CultureInfo.InvariantCulture), f, T.Tx2, x + col[2] + 24, ty);
                    Art.TextRight(g, s.Win.ToString(CultureInfo.InvariantCulture), f, T.Tx2, x + col[3] + 24, ty);
                    Art.TextRight(g, s.Draw.ToString(CultureInfo.InvariantCulture), f, T.Tx2, x + col[4] + 24, ty);
                    Art.TextRight(g, s.Loss.ToString(CultureInfo.InvariantCulture), f, T.Tx2, x + col[5] + 24, ty);
                    Art.TextRight(g, s.GoalsFor + ":" + s.GoalsAgainst, f, T.Tx2, x + col[6] + 46, ty);
                }
                using (Font f = T.F(12, FontStyle.Bold))
                    Art.TextRight(g, s.Points.ToString(CultureInfo.InvariantCulture), f, T.Tx, x + col[7] + 28, ty - 1);
            }
            hits.Add(new Hit(r, MakeOpen("https://ekstraklasa.org/tabela")));
        }

        void DrawClub(Graphics g, Rectangle r, Club c, bool big)
        {
            Card(g, r);
            Images.Draw(g, c.Badge, r.X + 12, r.Y + 8, 22);
            using (Font f = T.F(12.5f, FontStyle.Bold))
                Art.Text(g, c.Name.ToUpperInvariant(), f, T.Tx2, r.X + 40, r.Y + 11);
            if (!big)
                using (Font f = T.F(10))
                    Art.TextRight(g, "dodatkowo", f, T.Tx3, r.Right - 12, r.Y + 13);

            int y = r.Y + 38;
            y = Fixtures(g, r, y, "Rozegrane", c.Last, true, c.Key);
            y += 6;
            Fixtures(g, r, y, "Nadchodzące", c.Next, false, c.Key);

        }

        int Fixtures(Graphics g, Rectangle r, int y, string title, List<Fixture> list, bool done, string me)
        {
            using (Font f = T.F(9, FontStyle.Bold))
                Art.Text(g, title.ToUpperInvariant(), f, T.Tx3, r.X + 12, y);
            y += 16;
            if (list.Count == 0)
            {
                using (Font f = T.F(10)) Art.Text(g, "brak danych", f, T.Tx3, r.X + 12, y);
                return y + 16;
            }

            string key = me;
            using (Font fd = T.F(9))
            using (Font ft = T.F(10.5f))
            using (Font fs = T.F(12, FontStyle.Bold))
            {
                foreach (Fixture m in list)
                {
                    if (y + 34 > r.Bottom - 10) break;

                    string date = m.When == DateTime.MinValue ? ""
                        : m.When.ToString("dd.MM", CultureInfo.InvariantCulture) +
                          (done ? "" : " · " + m.When.ToString("HH:mm", CultureInfo.InvariantCulture));
                    Art.Text(g, date, fd, T.Tx3, r.X + 12, y);

                    string league = m.League.Replace("Polish Ekstraklasa", "Ekstraklasa")
                                            .Replace("Spanish La Liga", "La Liga");
                    Art.TextRight(g, Trim(g, league, fd, 110), fd, T.Tx3, r.Right - 12, y);
                    y += 13;

                    bool homeMe = m.Home.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;
                    string opp = Sport.Pl(homeMe ? m.Away : m.Home);
                    string oppBadge = homeMe ? m.AwayBadge : m.HomeBadge;
                    if (oppBadge.Length == 0) oppBadge = Sport.BadgeFor(homeMe ? m.Away : m.Home);

                    Images.Draw(g, oppBadge, r.X + 12, y - 1, 18);
                    Art.Text(g, Trim(g, opp, ft, r.Width - 112), ft, T.Tx, r.X + 35, y);

                    if (m.Played)
                    {
                        int mine = homeMe ? m.HomeScore : m.AwayScore;
                        int th = homeMe ? m.AwayScore : m.HomeScore;
                        Color sc = mine > th ? T.Ok : (mine == th ? T.Warn : T.Bad);
                        Art.TextRight(g, mine + ":" + th, fs, sc, r.Right - 12, y - 2);
                    }
                    else
                        Art.TextRight(g, homeMe ? "u siebie" : "wyjazd", fd, T.Tx3, r.Right - 12, y + 2);

                    y += 20;
                    using (Pen pen = new Pen(Color.FromArgb(70, T.Line)))
                        g.DrawLine(pen, r.X + 12, y - 2, r.Right - 12, y - 2);
                    y += 4;
                }
            }
            return y;
        }

        // Lamie tytul na maksymalnie tyle linii, ile sie miesci obok miniatury.
        void DrawWrapped(Graphics g, string text, Font f, Color c, int x, int y, int w, int maxLines)
        {
            string rest = text;
            for (int line = 0; line < maxLines && rest.Length > 0; line++)
            {
                bool last = line == maxLines - 1;
                string fit = Trim(g, rest, f, w);
                bool cut = fit.EndsWith("…") && rest.Length > fit.Length;

                if (cut && !last)
                {
                    int n = fit.Length - 1;
                    int sp = rest.LastIndexOf(' ', Math.Min(n, rest.Length - 1));
                    if (sp > 4) { fit = rest.Substring(0, sp); rest = rest.Substring(sp + 1); }
                    else rest = rest.Substring(Math.Min(n, rest.Length));
                }
                else rest = "";

                Art.Text(g, fit, f, c, x, y + line * 14);
            }
        }

        static string Ago(DateTime d)
        {
            TimeSpan t = DateTime.Now - d;
            if (t.TotalMinutes < 60) return Math.Max(1, (int)t.TotalMinutes) + " min temu";
            if (t.TotalHours < 24) return (int)t.TotalHours + " h temu";
            if (t.TotalDays < 2) return "wczoraj";
            if (t.TotalDays < 7) return (int)t.TotalDays + " dni temu";
            return d.ToString("dd.MM", CultureInfo.InvariantCulture);
        }

        void DrawMarkets(Graphics g, Rectangle area)
        {
            List<Market> all = NewsyVE.Markets.Visible();
            if (all.Count == 0) return;
            // kompaktowe kafelki: przy 8 pozycjach ok. 107 px, a gdy jest ich
            // mniej, nie rozdmuchujemy wykresow na cala wysokosc kolumny
            int ch = Math.Min(170, (area.Height - (all.Count - 1) * Gap) / all.Count);

            for (int i = 0; i < all.Count; i++)
            {
                Market m = all[i];
                Rectangle r = new Rectangle(area.X, area.Y + i * (ch + Gap), area.Width, ch);
                Card(g, r);

                using (Font f = T.F(12, FontStyle.Bold))
                    Art.Text(g, m.Name, f, T.Tx, r.X + 12, r.Y + 7);
                using (Font f = T.F(9))
                    Art.TextRight(g, m.Short, f, T.Tx3, r.Right - 12, r.Y + 9);

                if (!m.Ok)
                {
                    using (Font f = T.F(10.5f))
                        Art.Text(g, "wczytywanie…", f, T.Tx3, r.X + 12, r.Y + 30);
                    hits.Add(new Hit(r, MakeOpen(m.Tv)));
                    continue;
                }

                string price = NewsyVE.Markets.Fmt(m.Price, m.Decimals);
                float pw;
                using (Font f = T.F(17, FontStyle.Bold))
                {
                    Art.Text(g, price, f, T.Tx, r.X + 12, r.Y + 24);
                    pw = g.MeasureString(price, f).Width;
                }
                using (Font f = T.F(10))
                    Art.Text(g, m.Unit, f, T.Tx2, r.X + 10 + pw, r.Y + 31);

                double ch24 = m.ChangePct;
                Color cc = ch24 >= 0 ? T.Ok : T.Bad;
                string ct = (ch24 >= 0 ? "+" : "−") +
                            NewsyVE.Markets.Fmt(Math.Abs(ch24), 2) + " %";
                using (Font f = T.F(11, FontStyle.Bold))
                    Art.TextRight(g, ct, f, cc, r.Right - 12, r.Y + 29);

                using (Font f = T.F(8.5f))
                {
                    float sw = g.MeasureString(m.Span, f).Width;
                    Art.TextRight(g, m.Span, f, T.Tx3, r.Right - 12, r.Y + 48);
                    if (m.Note.Length > 0)
                        Art.Text(g, Trim(g, m.Note, f, r.Width - 30 - (int)sw), f, T.Tx3, r.X + 12, r.Y + 48);
                }

                // miejsce na min/max po prawej, obok wykresu, a nie na nim
                Rectangle plot = new Rectangle(r.X + 12, r.Y + 66, r.Width - 24 - 48, r.Height - 66 - 9);
                if (plot.Height >= 12) DrawSpark(g, m, plot, r.Right - 12);
                hits.Add(new Hit(r, MakeOpen(m.Tv)));
            }
        }

        static Action MakeOpen(string url)
        {
            return delegate { try { System.Diagnostics.Process.Start(url); } catch { } };
        }

        void DrawSpark(Graphics g, Market m, Rectangle r, int labelRight)
        {
            double[] v = m.Series;
            if (v.Length < 2) return;
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (double x in v) { if (x < lo) lo = x; if (x > hi) hi = x; }
            if (hi - lo < 1e-9) { hi = lo + 1; }
            double span = hi - lo;

            PointF[] pts = new PointF[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                float px = r.X + (float)i / (v.Length - 1) * r.Width;
                float py = r.Bottom - (float)((v[i] - lo) / span) * r.Height;
                pts[i] = new PointF(px, py);
            }

            bool up = v[v.Length - 1] >= v[0];
            Color line = up ? T.Ok : T.Bad;

            // wypelnienie pod linia
            PointF[] fill = new PointF[v.Length + 2];
            Array.Copy(pts, fill, v.Length);
            fill[v.Length] = new PointF(r.Right, r.Bottom);
            fill[v.Length + 1] = new PointF(r.X, r.Bottom);
            using (LinearGradientBrush b = new LinearGradientBrush(r,
                Color.FromArgb(58, line), Color.FromArgb(6, line), LinearGradientMode.Vertical))
                g.FillPolygon(b, fill);

            using (Pen p = new Pen(line, 1.4f))
            {
                p.LineJoin = LineJoin.Round;
                g.DrawLines(p, pts);
            }

            // ostatni punkt
            PointF last = pts[pts.Length - 1];
            using (SolidBrush b = new SolidBrush(line))
                g.FillEllipse(b, last.X - 2.6f, last.Y - 2.6f, 5.2f, 5.2f);

            using (Font f = T.F(8.5f))
            {
                Art.TextRight(g, NewsyVE.Markets.Fmt(hi, m.Decimals), f, T.Tx3, labelRight, r.Y - 3);
                Art.TextRight(g, NewsyVE.Markets.Fmt(lo, m.Decimals), f, T.Tx3, labelRight, r.Bottom - 10);
            }
        }

        void DrawHeader(Graphics g)
        {
            using (GraphicsPath p = T.Round(new Rectangle(Pad, Pad, 24, 24), 7))
            using (LinearGradientBrush b = new LinearGradientBrush(new Point(Pad, Pad),
                new Point(Pad + 24, Pad + 24), Color.FromArgb(255, 72, 74, 82), Color.FromArgb(255, 44, 45, 50)))
                g.FillPath(b, p);
            using (Font f = T.F(10, FontStyle.Bold))
                Art.Text(g, "VE", f, T.Tx, Pad + 5, Pad + 6);
            using (Font f = T.F(14, FontStyle.Bold))
                Art.Text(g, "NewsyVE", f, T.Tx, Pad + 32, Pad + 4);

            string[] names = { "Pogoda", "News", "Sport" };
            int tx0 = Pad + 130;
            for (int i = 0; i < names.Length; i++)
            {
                if (i == 2 && !Cfg.SportTab) continue;      // zakladka wylaczona w ustawieniach
                using (Font f = T.F(12, FontStyle.Bold))
                {
                    SizeF sz = g.MeasureString(names[i], f);
                    Rectangle rt = new Rectangle(tx0, Pad + 1, (int)sz.Width + 22, 26);
                    bool on = i == tab;
                    using (GraphicsPath p = T.Round(rt, 8))
                    using (SolidBrush b = new SolidBrush(on ? T.Card2 : T.Card))
                    using (Pen pen = new Pen(on ? T.Acc : T.Line))
                    { g.FillPath(b, p); g.DrawPath(pen, p); }
                    CenterText(g, names[i], f, on ? T.Tx : T.Tx2, rt.X, rt.Y + 5, rt.Width);
                    int idx = i;
                    hits.Add(new Hit(rt, delegate { SwitchTab(idx); }));
                    tx0 += rt.Width + 6;
                }
            }
            if (tab == 2) DrawSportViews(g, tx0 + 10);

            string stamp = !Store.Online ? "brak połączenia"
                         : Store.Busy ? "odświeżanie…"
                         : (Store.Stamp == DateTime.MinValue ? "ładowanie…"
                                                             : "akt. " + Hm(Store.Stamp));
            using (Font f = T.F(11))
                Art.TextRight(g, stamp, f, Store.Online ? T.Tx3 : T.Bad, CurW - Pad - 70, Pad + 7);

            Rectangle rf = new Rectangle(CurW - Pad - 58, Pad + 2, 26, 24);
            Rectangle rc = new Rectangle(CurW - Pad - 28, Pad + 2, 26, 24);
            DrawGlyphBtn(g, rf, "refresh");
            DrawGlyphBtn(g, rc, "close");
            hits.Add(new Hit(rf, delegate { RefreshAll(); }));
            hits.Add(new Hit(rc, delegate { HidePanel(); }));
        }

        // Przycisk odswiezal wylacznie pogode i radar, wiec na zakladce News
        // i Sport klikniecie nie zmienialo niczego widocznego. Do tego
        // LoadFrames pobiera liste klatek przez siec, a wywolane wprost
        // z obslugi kliknięcia robilo to w watku interfejsu i zamrazalo panel.
        void RefreshAll()
        {
            Store.Refresh(true);
            Store.RefreshMarkets(true);
            Store.RefreshNews();
            Store.RefreshTransit();
            Store.RefreshSport();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try { Radar.LoadFrames(); Radar.PrefetchAll(); }
                catch (Exception ex) { Program.Log("Radar", ex); }
            });
            Invalidate();
        }

        void DrawGlyphBtn(Graphics g, Rectangle r, string kind)
        {
            using (GraphicsPath p = T.Round(r, 7))
            using (SolidBrush b = new SolidBrush(T.Card))
            using (Pen pen = new Pen(T.Line))
            { g.FillPath(b, p); g.DrawPath(pen, p); }

            using (Pen pen = new Pen(T.Tx2, 1.8f))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
                if (kind == "close")
                {
                    g.DrawLine(pen, cx - 4, cy - 4, cx + 4, cy + 4);
                    g.DrawLine(pen, cx + 4, cy - 4, cx - 4, cy + 4);
                }
                else
                {
                    g.DrawArc(pen, cx - 5, cy - 5, 10, 10, 40, 280);
                    g.DrawLine(pen, cx + 5, cy - 6, cx + 5, cy - 1);
                    g.DrawLine(pen, cx + 5, cy - 6, cx + 1, cy - 6);
                }
            }
        }

        int DrawTabs(Graphics g, int y)
        {
            int n = Math.Max(1, Math.Min(3, Cfg.Places.Count));
            // z prawej zostawiamy miejsce na przycisk zmiany miejscowosci
            const int BtnW = 34;
            int h = 42, w = (LeftW - BtnW - Gap - (n - 1) * Gap) / n;
            for (int i = 0; i < n; i++)
            {
                Rectangle r = new Rectangle(LeftX + i * (w + Gap), y, w, h);
                bool on = i == Store.Active;
                using (GraphicsPath p = T.Round(r, 11))
                using (SolidBrush b = new SolidBrush(on ? T.Card2 : T.Card))
                using (Pen pen = new Pen(on ? T.Acc : T.Line))
                { g.FillPath(b, p); g.DrawPath(pen, p); }

                PlaceData d = i < Store.Data.Length ? Store.Data[i] : null;
                if (d != null && d.Cur != null)
                    Art.Glyph(g, d.Cur.Code, d.Cur.Day, r.X + 9, r.Y + 10, 22);
                using (Font f = T.F(12.5f, FontStyle.Bold))
                    Art.Text(g, Trim(g, Cfg.Places[i].Name, f, w - 76), f,
                        on ? T.Tx : T.Tx2, r.X + 38, r.Y + 12);
                if (d != null && d.Cur != null)
                    using (Font f = T.F(14, FontStyle.Bold))
                        Art.TextRight(g, Deg(d.Cur.Temp), f, on ? T.Tx : T.Tx2, r.Right - 10, r.Y + 11);

                int idx = i;
                hits.Add(new Hit(r, delegate {
                    Store.Active = idx;
                    Store.LoadIcm();
                    Store.RefreshTransit();   // rozklad ma isc za wybranym miastem
                    Store.RefreshLocalNews(); // razem z newsami i utrudnieniami
                    Store.Fire();
                }));
            }

            // Dodanie albo podmiana drugiej i trzeciej miejscowosci - wczesniej
            // dalo sie to zrobic tylko z menu pod prawym przyciskiem na pasku.
            Rectangle rb = new Rectangle(LeftX + LeftW - BtnW, y, BtnW, h);
            using (GraphicsPath p = T.Round(rb, 11))
            using (SolidBrush b = new SolidBrush(T.Card))
            using (Pen pen = new Pen(T.Line))
            { g.FillPath(b, p); g.DrawPath(pen, p); }
            using (Font f = T.F(17, FontStyle.Bold))
                CenterText(g, Cfg.Places.Count < 3 ? "+" : "\u2261", f, T.Tx2,
                           rb.X, rb.Y + 8, rb.Width);
            hits.Add(new Hit(rb, delegate
            {
                if (Store.OpenPlaces != null) Store.OpenPlaces();
            }));

            return y + h + Gap;
        }

        int DrawHero(Graphics g, int y)
        {
            int h = 104;
            Rectangle r = new Rectangle(LeftX, y, LeftW, h);
            Card(g, r);
            PlaceData d = Store.Cur;
            if (d == null || d.Cur == null)
            {
                using (Font f = T.F(12)) Art.Text(g, "ładowanie…", f, T.Tx3, r.X + 14, r.Y + 40);
                return y + h + Gap;
            }
            CurrentWx c = d.Cur;
            Art.Glyph(g, c.Code, c.Day, r.X + 10, r.Y + 18, 66);

            using (Font f = T.F(42))
                Art.Text(g, Math.Round(c.Temp).ToString(CultureInfo.InvariantCulture), f, T.Tx, r.X + 86, r.Y + 10);
            float tw = 0;
            using (Font f = T.F(42))
                tw = g.MeasureString(Math.Round(c.Temp).ToString(CultureInfo.InvariantCulture), f).Width;
            using (Font f = T.F(16))
                Art.Text(g, "°C", f, T.Tx2, r.X + 80 + tw, r.Y + 18);

            using (Font f = T.F(13, FontStyle.Bold))
                Art.Text(g, Wmo.Text(c.Code), f, T.Tx, r.X + 86, r.Y + 58);
            using (Font f = T.F(11))
                Art.Text(g, Store.CurPlace.Name + " · odczuwalna " + Deg(c.Feels), f, T.Tx2, r.X + 86, r.Y + 76);

            DayWx d0 = d.Days.Count > 0 ? d.Days[0] : null;
            string line = "";
            if (d0 != null) line = "↑ " + Deg(d0.Max) + "   ↓ " + Deg(d0.Min) + "   ";
            line += "wiatr " + Math.Round(c.Wind) + " km/h " + Wmo.Dir(c.Dir);
            using (Font f = T.F(11))
                Art.TextRight(g, line, f, T.Tx2, r.Right - 12, r.Y + 14);
            using (Font f = T.F(10))
                Art.TextRight(g, "pomiar: " + c.Src, f, T.Tx3, r.Right - 12, r.Y + 34);

            return y + h + Gap;
        }

        int DrawNowcast(Graphics g, int y)
        {
            int h = 102;
            Rectangle r = new Rectangle(LeftX, y, LeftW, h);
            Card(g, r);
            Art.Header(g, "Opady — najbliższe 2 h", r, "ICON-D2");
            PlaceData d = Store.Cur;
            if (d == null || d.M15.Length == 0) return y + h + Gap;

            DateTime? rain = Store.FirstRain(d);
            string msg;
            Color col;
            if (!rain.HasValue) { msg = "Brak opadów w ciągu najbliższych 2 godzin."; col = T.Tx; }
            else
            {
                int min = (int)Math.Round((rain.Value - DateTime.Now).TotalMinutes);
                double sum = 0; foreach (double v in d.M15) sum += v;
                msg = (min <= 5 ? "Opady zaczynają się teraz" : "Opady około " + Hm(rain.Value) + " (za ~" + min + " min)")
                    + " · " + N1(sum) + " mm";
                col = T.Acc;
            }
            using (Font f = T.F(12, FontStyle.Bold)) Art.Text(g, msg, f, col, r.X + 12, r.Y + 28);

            double mx = 0.6;
            foreach (double v in d.M15) if (v > mx) mx = v;
            int n = d.M15.Length, bw = (LeftW - 24 - (n - 1) * 4) / n;
            int by = r.Y + 54, bh = 32;
            for (int i = 0; i < n; i++)
            {
                int bx = r.X + 12 + i * (bw + 4);
                int hh = d.M15[i] <= 0 ? 3 : Math.Max(5, (int)(d.M15[i] / mx * bh));
                using (SolidBrush b = new SolidBrush(d.M15[i] <= 0 ? T.Line : T.Acc))
                using (GraphicsPath p = T.Round(new Rectangle(bx, by + bh - hh, bw, hh), 2))
                    g.FillPath(b, p);
            }
            using (Font f = T.F(10))
            {
                Art.Text(g, Hm(d.M15T[0]), f, T.Tx3, r.X + 12, r.Y + 90 - 12);
                Art.TextRight(g, Hm(d.M15T[n - 1]), f, T.Tx3, r.Right - 12, r.Y + 90 - 12);
            }
            return y + h + Gap;
        }

        int DrawHours(Graphics g, int y)
        {
            int h = 158;
            Rectangle r = new Rectangle(LeftX, y, LeftW, h);
            Card(g, r);
            Art.Header(g, "Godzinowo — 24 h", r, Store.CurPlace.Name);
            PlaceData d = Store.Cur;
            if (d == null) return y + h + Gap;

            List<HourWx> list = new List<HourWx>();
            foreach (HourWx x in d.Hours)
            {
                if (x.T < DateTime.Now.AddMinutes(-30)) continue;
                list.Add(x);
                if (list.Count == 24) break;
            }
            int cols = 12, cw = (LeftW - 24) / cols, rowH = 60;
            for (int i = 0; i < list.Count; i++)
            {
                int cx = r.X + 12 + (i % cols) * cw;
                int cy = r.Y + 28 + (i / cols) * rowH;
                HourWx x = list[i];
                bool now = i == 0;
                if (now)
                    using (GraphicsPath p = T.Round(new Rectangle(cx - 1, cy - 2, cw, rowH - 4), 7))
                    using (SolidBrush b = new SolidBrush(T.Card2)) g.FillPath(b, p);

                using (Font f = T.F(10))
                    CenterText(g, now ? "teraz" : x.T.ToString("HH", CultureInfo.InvariantCulture), f, T.Tx2, cx, cy + 2, cw);
                Art.Glyph(g, x.Code, IsDay(d, x.T), cx + (cw - 22) / 2f, cy + 15, 22);
                using (Font f = T.F(11.5f, FontStyle.Bold))
                    CenterText(g, Deg(x.Temp), f, T.Tx, cx, cy + 38, cw);
                if (x.Pop >= 20)
                    using (Font f = T.F(9))
                        CenterText(g, x.Pop + "%", f, T.Acc, cx, cy + 50, cw);
            }
            return y + h + Gap;
        }

        static bool IsDay(PlaceData d, DateTime t)
        {
            foreach (DayWx x in d.Days)
                if (x.T.Date == t.Date) return t >= x.Sunrise && t <= x.Sunset;
            return true;
        }

        void CenterText(Graphics g, string s, Font f, Color c, int x, int y, int w)
        {
            SizeF sz = g.MeasureString(s, f);
            Art.Text(g, s, f, c, x + (w - sz.Width) / 2f, y);
        }

        int DrawDays(Graphics g, int y)
        {
            int h = 216;
            Rectangle r = new Rectangle(LeftX, y, LeftW, h);
            Card(g, r);
            Art.Header(g, "7 dni", r, Store.CurPlace.Name);
            PlaceData d = Store.Cur;
            if (d == null || d.Days.Count == 0) return y + h + Gap;

            double lo = double.MaxValue, hi = double.MinValue;
            foreach (DayWx x in d.Days) { if (x.Min < lo) lo = x.Min; if (x.Max > hi) hi = x.Max; }
            double span = Math.Max(1, hi - lo);
            string[] names = { "niedz.", "pon.", "wt.", "śr.", "czw.", "pt.", "sob." };

            for (int i = 0; i < d.Days.Count && i < 7; i++)
            {
                DayWx x = d.Days[i];
                int cy = r.Y + 30 + i * 26;
                string nm = i == 0 ? "dziś" : (i == 1 ? "jutro" : names[(int)x.T.DayOfWeek]);
                using (Font f = T.F(11.5f, FontStyle.Bold)) Art.Text(g, nm, f, T.Tx, r.X + 12, cy + 3);
                Art.Glyph(g, x.Code, true, r.X + 66, cy + 1, 22);
                if (x.Pop >= 20)
                    using (Font f = T.F(9.5f)) Art.TextRight(g, x.Pop + "%", f, T.Acc, r.X + 122, cy + 5);
                using (Font f = T.F(11.5f)) Art.TextRight(g, Deg(x.Min), f, T.Tx3, r.X + 152, cy + 4);

                int bx = r.X + 160, bw = LeftW - 160 - 52;
                using (SolidBrush b = new SolidBrush(T.Line))
                using (GraphicsPath p = T.Round(new Rectangle(bx, cy + 10, bw, 5), 2)) g.FillPath(b, p);
                int a0 = (int)((x.Min - lo) / span * bw), a1 = (int)((x.Max - lo) / span * bw);
                using (LinearGradientBrush b = new LinearGradientBrush(
                    new Rectangle(bx + a0, cy + 10, Math.Max(5, a1 - a0), 5),
                    T.AccDim, T.Warn, LinearGradientMode.Horizontal))
                using (GraphicsPath p = T.Round(new Rectangle(bx + a0, cy + 10, Math.Max(5, a1 - a0), 5), 2))
                    g.FillPath(b, p);

                using (Font f = T.F(11.5f, FontStyle.Bold))
                    Art.TextRight(g, Deg(x.Max), f, T.Tx, r.Right - 12, cy + 4);
            }
            return y + h + Gap;
        }

        void DrawDetails(Graphics g, int y)
        {
            int h = H - Pad - y;            // karta domyka lewa kolumne do dolu
            Rectangle r = new Rectangle(LeftX, y, LeftW, h);
            Card(g, r);
            Art.Header(g, "Szczegóły", r, "");
            PlaceData d = Store.Cur;
            if (d == null || d.Cur == null || d.Days.Count == 0) return;
            CurrentWx c = d.Cur;
            DayWx d0 = d.Days[0];
            TimeSpan len = d0.Sunset - d0.Sunrise;

            string aqTxt = "—", aqNote = "brak danych";
            Color aqCol = T.Tx3;
            if (d.Aqi >= 0)
            {
                aqTxt = d.Aqi.ToString(CultureInfo.InvariantCulture);
                if (d.Aqi <= 20) { aqCol = T.Ok; aqNote = "bardzo dobra"; }
                else if (d.Aqi <= 40) { aqCol = T.Ok; aqNote = "dobra"; }
                else if (d.Aqi <= 60) { aqCol = T.Warn; aqNote = "umiarkowana"; }
                else if (d.Aqi <= 80) { aqCol = T.Warn; aqNote = "niekorzystna"; }
                else { aqCol = T.Bad; aqNote = "zła"; }
                aqNote += " · PM2.5 " + N1(d.Pm25);
            }

            string src = c.SrcBasic;
            string[][] cells = new string[][] {
                new string[]{ "Wiatr", Math.Round(c.Wind) + " km/h", Wmo.Dir(c.Dir) + " · porywy " + Math.Round(c.Gust) },
                new string[]{ "Wilgotność", c.Hum + " %", src },
                new string[]{ "Ciśnienie", Math.Round(c.Press) + " hPa", src },
                new string[]{ "Zachmurzenie", c.Cloud + " %", Wmo.Text(c.Code).ToLowerInvariant() },
                new string[]{ "UV dziś", N1(d0.Uv), d0.Uv >= 6 ? "chroń skórę" : "niski" },
                new string[]{ "Opad dziś", N1(d0.Precip) + " mm", "szansa " + d0.Pop + "%" },
                new string[]{ "Wschód", Hm(d0.Sunrise), "dzień " + (int)len.TotalHours + " h " + len.Minutes + " min" },
                new string[]{ "Zachód", Hm(d0.Sunset), d0.Sunset > DateTime.Now
                    ? "za " + N1((d0.Sunset - DateTime.Now).TotalHours) + " h" : "po zachodzie" },
                new string[]{ "Powietrze", aqTxt, aqNote }
            };

            int cw = (LeftW - 24 - 2 * 6) / 3;
            int ch = Math.Max(48, (h - 28 - 10 - 2 * 6) / 3);
            for (int i = 0; i < cells.Length; i++)
            {
                int cx = r.X + 12 + (i % 3) * (cw + 6);
                int cy = r.Y + 28 + (i / 3) * (ch + 6);
                using (GraphicsPath p = T.Round(new Rectangle(cx, cy, cw, ch), 8))
                using (SolidBrush b = new SolidBrush(T.Bg2)) g.FillPath(b, p);
                int off = Math.Max(0, (ch - 43) / 2);
                using (Font f = T.F(9, FontStyle.Bold))
                    Art.Text(g, cells[i][0].ToUpperInvariant(), f, T.Tx3, cx + 8, cy + 5 + off);
                using (Font f = T.F(13, FontStyle.Bold))
                    Art.Text(g, cells[i][1], f, i == 8 ? aqCol : T.Tx, cx + 8, cy + 17 + off);
                using (Font f = T.F(9))
                    Art.Text(g, cells[i][2], f, T.Tx3, cx + 8, cy + 34 + off);
            }
        }

        int DrawRadar(Graphics g, int y)
        {
            // mapa podaza za wybrana miejscowoscia, a nie stoi na Krakowie
            Place rp = Store.CurPlace;
            if (rp != null && (Math.Abs(Radar.CenterLat - rp.Lat) > 0.001 ||
                               Math.Abs(Radar.CenterLon - rp.Lon) > 0.001))
            {
                Radar.CenterLat = rp.Lat;
                Radar.CenterLon = rp.Lon;
                Radar.Invalidate();
                Radar.EnsureReady(Radar.Index);
            }

            int h = 26 + MapH + 30 + 18 + 10;
            Rectangle r = new Rectangle(RightX, y, RightW, h);
            Card(g, r);
            Art.Header(g, "Radar opadów", r, Radar.Frames.Count > 0
                ? "RainViewer · 512 px" : "wczytywanie…");

            mapRect = new Rectangle(r.X + 12, r.Y + 26, RightW - 24, MapH);
            using (GraphicsPath clip = T.Round(mapRect, 9))
            {
                Region old = g.Clip;
                g.SetClip(clip, CombineMode.Replace);
                Radar.Draw(g, mapRect);
                g.Clip = old;
                using (Pen pen = new Pen(T.Line)) g.DrawPath(pen, clip);
            }
            DrawPins(g);
            DrawZoomBtns(g);

            int cy = mapRect.Bottom + 8;
            int bx = r.X + 12;
            bx = SmallBtn(g, bx, cy, "prev", delegate { SetPlaying(false); Step(-1); });
            bx = SmallBtn(g, bx, cy, playing ? "pause" : "play", delegate { SetPlaying(!playing); });
            bx = SmallBtn(g, bx, cy, "next", delegate { SetPlaying(false); Step(1); });

            RadarFrame f0 = Radar.Frames.Count > 0 ? Radar.Frames[Radar.Index] : null;
            using (Font f = T.F(11))
                Art.Text(g, f0 != null ? Hm(f0.Time) : "--:--", f, T.Tx2, bx + 4, cy + 4);

            int tx = bx + 48, tw = r.Right - 12 - 98 - tx;   // miejsce na etykiete i predkosc
            Rectangle track = new Rectangle(tx, cy + 9, tw, 5);
            using (SolidBrush b = new SolidBrush(T.Line))
            using (GraphicsPath p = T.Round(track, 2)) g.FillPath(b, p);
            if (Radar.Frames.Count > 0)
            {
                int nc = Radar.Frames.Count - Radar.PastCount;
                if (nc > 0)
                {
                    int nw = (int)((double)nc / Radar.Frames.Count * tw);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(90, T.Warn)))
                    using (GraphicsPath p = T.Round(new Rectangle(track.Right - nw, track.Y, nw, 5), 2))
                        g.FillPath(b, p);
                }
                float kx = tx + (float)Radar.Index / Math.Max(1, Radar.Frames.Count - 1) * tw;
                using (SolidBrush b = new SolidBrush(T.Acc))
                    g.FillEllipse(b, kx - 5, cy + 4, 10, 10);
                hits.Add(new Hit(new Rectangle(tx, cy, tw, 22), delegate
                {
                    Point m = Logical(PointToClient(Control.MousePosition));
                    SetPlaying(false);
                    int idx = (int)Math.Round((double)(m.X - tx) / tw * (Radar.Frames.Count - 1));
                    Radar.Index = Math.Max(0, Math.Min(Radar.Frames.Count - 1, idx));
                    Radar.EnsureReady(Radar.Index);
                }));
            }

            string tag = "";
            Color tagCol = T.Tx2;
            if (f0 != null)
            {
                int mins = (int)Math.Round((DateTime.Now - f0.Time).TotalMinutes);
                if (f0.Nowcast) { tag = "+" + Math.Abs(mins) + " min"; tagCol = T.Warn; }
                else tag = mins <= 2 ? "teraz" : "-" + mins + " min";
            }
            using (Font f = T.F(11)) Art.TextRight(g, tag, f, tagCol, r.Right - 46, cy + 4);

            Rectangle sp = new Rectangle(r.Right - 40, cy + 2, 30, 18);
            using (GraphicsPath p = T.Round(sp, 6))
            using (SolidBrush b = new SolidBrush(T.Card2))
            using (Pen pen = new Pen(T.Line)) { g.FillPath(b, p); g.DrawPath(pen, p); }
            using (Font f = T.F(9.5f)) CenterText(g, SpeedTx[speedIdx], f, T.Tx2, sp.X, sp.Y + 3, sp.Width);
            hits.Add(new Hit(sp, delegate
            {
                speedIdx = (speedIdx + 1) % SpeedMs.Length;
                anim.Interval = SpeedMs[speedIdx];
            }));

            int ly = cy + 26;
            Rectangle leg = new Rectangle(r.X + 42, ly + 3, RightW - 130, 7);
            using (LinearGradientBrush b = new LinearGradientBrush(leg,
                Color.FromArgb(255, 120, 170, 210), Color.FromArgb(255, 190, 90, 200), LinearGradientMode.Horizontal))
            {
                ColorBlend cb = new ColorBlend();
                cb.Colors = new Color[] {
                    Color.FromArgb(255,108,150,190), Color.FromArgb(255,74,120,180),
                    Color.FromArgb(255,96,170,130), Color.FromArgb(255,196,190,96),
                    Color.FromArgb(255,214,150,80), Color.FromArgb(255,204,96,96),
                    Color.FromArgb(255,178,92,186) };
                cb.Positions = new float[] { 0f, 0.18f, 0.38f, 0.58f, 0.75f, 0.9f, 1f };
                b.InterpolationColors = cb;
                using (GraphicsPath p = T.Round(leg, 3)) g.FillPath(b, p);
            }
            using (Font f = T.F(9))
            {
                Art.Text(g, "słaby", f, T.Tx3, r.X + 12, ly);
                Art.TextRight(g, "ulewa", f, T.Tx3, r.Right - 12, ly);
            }
            return y + h + Gap;
        }

        void DrawPins(Graphics g)
        {
            double ox = Radar.OriginX, oy = Radar.OriginY;
            for (int i = 0; i < Cfg.Places.Count; i++)
            {
                Place p = Cfg.Places[i];
                PointF pt = RadarEngine.LatLonToPx(p.Lat, p.Lon, Radar.Zoom, ox, oy);
                float px = mapRect.X + pt.X, py = mapRect.Y + pt.Y;
                if (px < mapRect.X || px > mapRect.Right || py < mapRect.Y || py > mapRect.Bottom) continue;
                bool on = i == Store.Active;
                using (SolidBrush b = new SolidBrush(on ? T.Acc : T.Tx3))
                    g.FillEllipse(b, px - (on ? 5 : 4), py - (on ? 5 : 4), on ? 10 : 8, on ? 10 : 8);
                using (Pen pen = new Pen(Color.White, on ? 2f : 1.4f))
                    g.DrawEllipse(pen, px - (on ? 5 : 4), py - (on ? 5 : 4), on ? 10 : 8, on ? 10 : 8);
                using (Font f = T.F(11, FontStyle.Bold))
                {
                    SizeF sz = g.MeasureString(p.Name, f);
                    // etykieta nad pinezka (pierwsza) albo pod nia (druga) - nie nachodza na siebie
                    float lx = px - sz.Width / 2f;
                    float ly = (i == 0) ? py - sz.Height - 9 : py + 9;
                    Rectangle chip = new Rectangle((int)(lx - 5), (int)(ly - 2),
                                                   (int)(sz.Width + 10), (int)(sz.Height + 3));
                    using (GraphicsPath cp = T.Round(chip, 5))
                    using (SolidBrush cb = new SolidBrush(Color.FromArgb(225, 16, 17, 19)))
                    using (Pen cpen = new Pen(Color.FromArgb(120, 255, 255, 255)))
                    { g.FillPath(cb, cp); g.DrawPath(cpen, cp); }
                    Art.Text(g, p.Name, f, on ? Color.White : Color.FromArgb(255, 198, 201, 208), lx, ly);
                }
            }
        }

        void DrawZoomBtns(Graphics g)
        {
            Rectangle rp = new Rectangle(mapRect.Right - 30, mapRect.Y + 8, 22, 22);
            Rectangle rm = new Rectangle(mapRect.Right - 30, mapRect.Y + 34, 22, 22);
            DrawZoomBtn(g, rp, "+");
            DrawZoomBtn(g, rm, "−");
            hits.Add(new Hit(rp, delegate { SetZoom(Radar.Zoom + 1); }));
            hits.Add(new Hit(rm, delegate { SetZoom(Radar.Zoom - 1); }));
        }

        void SetZoom(int z)
        {
            z = Math.Max(5, Math.Min(9, z));
            if (z == Radar.Zoom) return;
            Radar.Zoom = z;
            Radar.Invalidate();
            Radar.EnsureReady(Radar.Index);
            Invalidate();
        }

        void DrawZoomBtn(Graphics g, Rectangle r, string s)
        {
            using (GraphicsPath p = T.Round(r, 6))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(210, 24, 25, 27)))
            using (Pen pen = new Pen(T.Line)) { g.FillPath(b, p); g.DrawPath(pen, p); }
            using (Font f = T.F(13, FontStyle.Bold)) CenterText(g, s, f, T.Tx2, r.X, r.Y + 2, r.Width);
        }

        int SmallBtn(Graphics g, int x, int y, string kind, Action go)
        {
            Rectangle r = new Rectangle(x, y, 24, 22);
            using (GraphicsPath p = T.Round(r, 6))
            using (SolidBrush b = new SolidBrush(T.Card2))
            using (Pen pen = new Pen(T.Line)) { g.FillPath(b, p); g.DrawPath(pen, p); }
            float cx = r.X + 12, cy = r.Y + 11;
            using (SolidBrush b = new SolidBrush(T.Tx2))
            {
                if (kind == "pause")
                {
                    g.FillRectangle(b, cx - 4, cy - 5, 3, 10);
                    g.FillRectangle(b, cx + 1, cy - 5, 3, 10);
                }
                else if (kind == "play")
                    g.FillPolygon(b, new PointF[] { new PointF(cx - 4, cy - 5), new PointF(cx + 5, cy), new PointF(cx - 4, cy + 5) });
                else if (kind == "prev")
                {
                    g.FillPolygon(b, new PointF[] { new PointF(cx + 4, cy - 5), new PointF(cx - 3, cy), new PointF(cx + 4, cy + 5) });
                    g.FillRectangle(b, cx - 5, cy - 5, 2, 10);
                }
                else
                {
                    g.FillPolygon(b, new PointF[] { new PointF(cx - 4, cy - 5), new PointF(cx + 3, cy), new PointF(cx - 4, cy + 5) });
                    g.FillRectangle(b, cx + 3, cy - 5, 2, 10);
                }
            }
            hits.Add(new Hit(r, go));
            return x + 27;
        }

        void DrawIcm(Graphics g, int y)
        {
            int h = H - y - Pad;
            Rectangle r = new Rectangle(RightX, y, RightW, h);
            Card(g, r);
            Place p = Store.CurPlace;
            Art.Header(g, "Meteorogram UM 4 km · ICM", r,
                p.IcmNote.Length > 0 ? p.IcmNote : p.Name);

            Rectangle img = new Rectangle(r.X + 12, r.Y + 26, RightW - 24, h - 38);
            using (GraphicsPath clip = T.Round(img, 8))
            {
                Region old = g.Clip;
                g.SetClip(clip, CombineMode.Replace);
                if (Store.Icm != null && Store.IcmFor == Store.Active)
                {
                    using (SolidBrush b = new SolidBrush(Color.White)) g.FillRectangle(b, img);
                    float sc = Math.Min((float)img.Width / Store.Icm.Width, (float)img.Height / Store.Icm.Height);
                    int iw = (int)(Store.Icm.Width * sc), ih = (int)(Store.Icm.Height * sc);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(Store.Icm, img.X + (img.Width - iw) / 2, img.Y, iw, ih);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(T.Bg2)) g.FillRectangle(b, img);
                    using (Font f = T.F(11))
                        Art.Text(g, "meteorogram ICM — wczytywanie…", f, T.Tx3, img.X + 12, img.Y + 12);
                }
                g.Clip = old;
                using (Pen pen = new Pen(T.Line)) g.DrawPath(pen, clip);
            }
            if (Store.IcmFor == -2)
                using (Font f = T.F(10.5f))
                    Art.Text(g, "ICM nie liczy meteorogramu dla tego punktu.",
                        f, T.Tx3, r.X + 16, r.Y + 40);
            hits.Add(new Hit(img, delegate {
                try { System.Diagnostics.Process.Start(Cfg.IcmPage(Store.CurPlace)); } catch { }
            }));
        }

        void SwitchTab(int t)
        {
            if (t == tab) return;
            tab = t;
            Place();                 // kazda zakladka ma inna szerokosc
            Invalidate();
            if (tab == 1) Store.RefreshTransit();
            if (tab == 2 && sportView == 1) FreshCups();
        }

        void SetSportView(int v)
        {
            if (v == sportView) return;
            sportView = v;
            Place();
            Invalidate();
            if (v == 1) FreshCups();
        }

        // Timer w tle odswieza wyniki co dwie minuty tylko przy otwartym
        // panelu - po wejsciu na podzakladke nie czekamy na jego tykniecie.
        static void FreshCups()
        {
            if (Cups.LiveWindow() && (DateTime.Now - Cups.Stamp).TotalSeconds > 110) Store.RefreshCups();
        }

        // Po zmianie ustawien zmienia sie liczba kart, a wiec i szerokosc okna.
        public void Rebuild()
        {
            if (tab == 2 && !Cfg.SportTab) tab = 0;
            Place();
            Invalidate();
        }

        void Place()
        {
            Native.TaskbarInfo t = Native.Taskbar();
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            float nk = Math.Min(1f, Math.Min((wa.Height - 12) / (float)H, (wa.Width - 8) / (float)CurW));
            if (Math.Abs(nk - k) > 0.001f) { k = nk; Invalidate(); }
            int w = (int)Math.Ceiling(CurW * k);
            int ht = (int)Math.Ceiling(H * k);
            int bottom = t != null ? t.T : wa.Bottom;
            int y = bottom - ht - 8;
            if (y < 6) y = 6;
            int x = Cfg.Side == "Left"
                ? (t != null ? t.L + Cfg.Offset : wa.Left + 8)
                : (t != null ? t.TrayLeft - w - Cfg.Offset : wa.Right - w - 8);
            if (x + w > wa.Right) x = wa.Right - w - 8;
            if (x < wa.Left + 4) x = wa.Left + 4;
            Bounds = new Rectangle(x, y, w, ht);
        }

        public void ShowAt()
        {
            Place();
            shownAt = DateTime.Now;
            Show();
            Native.ForceForeground(Handle);
            Radar.EnsureReady(Radar.Index);
            if (Store.Icm == null || Store.IcmFor != Store.Active) Store.LoadIcm();
        }
    }
}
