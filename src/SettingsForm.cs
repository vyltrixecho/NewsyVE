using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace NewsyVE
{
    // Okno ustawien: wyszukiwarka miejscowosci oraz reszta pokretel widgetu.
    // Podzielone na trzy strony przelaczane wlasnymi przyciskami - zwykly
    // TabControl nie daje sie pomalowac na grafit i naglowki zakladek
    // zostalyby jasnoszare.
    class SettingsForm : Form
    {
        // --- strona 1: miejscowosci ---
        readonly TextBox box = new TextBox();
        readonly ListBox results = new ListBox();
        readonly ListBox chosen = new ListBox();
        readonly Label info = new Label();
        readonly Label lblLines = new Label();
        readonly TextBox tbLines = new TextBox();
        readonly Button btnNear = new Button();
        bool loadingLines;                       // wypelniamy pole z kodu - nie zapisuj
        readonly List<GeoHit> hits = new List<GeoHit>();
        readonly List<Place> places = new List<Place>();

        // --- strona 2: pasek ---
        readonly RadioButton rbLeft = new RadioButton();
        readonly RadioButton rbRight = new RadioButton();
        readonly RadioButton rbOff = new RadioButton();
        readonly NumericUpDown numOffset = new NumericUpDown();
        readonly TrackBar tbText = new TrackBar();
        readonly Label lblText = new Label();
        readonly CheckBox cbAuto = new CheckBox();

        // --- strona 3: dane ---
        readonly NumericUpDown numWx = new NumericUpDown();
        readonly NumericUpDown numNews = new NumericUpDown();
        readonly TextBox tbKey = new TextBox();
        readonly Label lblCache = new Label();

        // --- strona 3: karty ---
        readonly CheckBox[] cbCard = new CheckBox[8];
        readonly TrackBar tbScale = new TrackBar();
        readonly Label lblScale = new Label();

        // --- strona 4: sport ---
        readonly CheckBox cbSportTab = new CheckBox();
        readonly ComboBox cbClub1 = new ComboBox();
        readonly ComboBox cbClub2 = new ComboBox();
        readonly TextBox tbAbroad = new TextBox();
        readonly ListBox lbAbroad = new ListBox();
        readonly Label lblAbroad = new Label();
        readonly Label lblSportInfo = new Label();
        readonly List<string[]> abroadHits = new List<string[]>();
        string abroadId, abroadName;             // wybor zapisywany dopiero przy "Zapisz"
        const string NoClub = "— brak —";

        readonly Panel[] pages = new Panel[5];
        readonly Button[] nav = new Button[5];

        // stan sprzed edycji - zeby "Anuluj" cofnelo takze to, co bylo
        // podgladane na zywo na pasku zadan
        readonly string side0;
        readonly int off0, text0;

        // wywolywane po kazdej zmianie widocznej od razu na pasku
        public Action Live;
        public bool Applied;

        public SettingsForm()
        {
            Text = "NewsyVE — ustawienia";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(700, 480);
            BackColor = T.Bg;
            ForeColor = T.Tx;
            Font = T.F(13);
            // bez tego pasek tytulu i pasek zadan pokazuja domyslny kwadrat WinForms
            if (AppIcon != null) Icon = AppIcon;

            side0 = Cfg.Side; off0 = Cfg.Offset; text0 = Cfg.TextLevel;
            // kopie - edycja linii nie moze dotykac Cfg przed "Zapisz"
            foreach (Place p in Cfg.Places)
            {
                Place c = Place.Parse(p.Serialize());
                places.Add(c ?? p);
            }

            BuildNav();
            pages[0] = BuildPlacesPage();
            pages[1] = BuildBarPage();
            pages[2] = BuildCardsPage();
            pages[3] = BuildSportPage();
            pages[4] = BuildDataPage();
            foreach (Panel p in pages) { p.SetBounds(176, 16, 508, 400); Controls.Add(p); }

            Button ok = Btn("Zapisz", 486, 430, 96);
            ok.BackColor = T.AccDim;
            ok.Click += delegate { SaveAndClose(); };
            Button cancel = Btn("Anuluj", 590, 430, 94);
            cancel.Click += delegate { Close(); };      // cofniecie robi OnFormClosing
            Controls.Add(ok); Controls.Add(cancel);
            CancelButton = cancel;

            AddPromo();

            ShowPage(0);
            RefreshChosen();
        }

        // Reklamy Vyltrix Echo wg wspolnego zestawu (_Wspolne\VyltrixPromo, jak
        // w OpenFences): nad ustawieniami pasek 88 px - banner z lewej, QR do
        // kawy z prawej - a w lewej czesci stopki przycisk "Postaw kawe".
        // Uklad okna jest na sztywnych wspolrzednych, wiec zsuwamy go o pasek.
        readonly ToolTip tips = new ToolTip();

        void AddPromo()
        {
            const int shift = 88 + 12;
            foreach (Control c in Controls) c.Top += shift;
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + shift);

            // teksty podpowiedzi z README zestawu - z polskimi znakami
            VyltrixEcho.Promo.VyltrixPromo.BannerTip = "vyltrixecho.pl – otwiera stronę w przeglądarce";
            VyltrixEcho.Promo.VyltrixPromo.QrTip = "Zeskanuj, żeby postawić kawę na buycoffee.to";
            VyltrixEcho.Promo.VyltrixPromo.CoffeeTip = "Postaw kawę dla VyltrixEcho na buycoffee.to";

            Control head = VyltrixEcho.Promo.VyltrixPromo.Header(tips);
            head.Dock = DockStyle.None;
            head.SetBounds(12, 12, ClientSize.Width - 24, 88);
            Controls.Add(head);

            // przycisk 35 px, wysrodkowany wzgledem "Zapisz" (28 px)
            Control coffee = VyltrixEcho.Promo.VyltrixPromo.CoffeeButton(tips);
            coffee.Location = new Point(12, 430 + shift - (coffee.Height - 28) / 2);
            Controls.Add(coffee);
        }

        // ikona wkompilowana w exe (/win32icon w Build-NewsyVE.ps1)
        static Icon appIcon;
        static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                    try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                    catch { }
                return appIcon;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitleBar(Handle);
        }

        // Zamkniecie krzyzykiem albo Esc to tez "Anuluj" - inaczej podglad
        // na zywo zostawal na pasku, choc nic nie zapisano.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!Applied) Revert();
            base.OnFormClosing(e);
        }

        // ---------------------------------------------------------------- nawigacja

        void BuildNav()
        {
            string[] names = { "Miejscowości", "Pasek zadań", "Karty", "Sport", "Dane" };
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                Button b = new Button();
                b.Text = "   " + names[i];
                b.SetBounds(12, 16 + i * 40, 152, 36);
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.TextAlign = ContentAlignment.MiddleLeft;
                b.BackColor = T.Bg; b.ForeColor = T.Tx2;
                b.Font = T.F(12.5f);
                b.Cursor = Cursors.Hand;
                b.Click += delegate { ShowPage(idx); };
                nav[i] = b;
                Controls.Add(b);
            }
        }

        void ShowPage(int i)
        {
            for (int k = 0; k < 5; k++)
            {
                pages[k].Visible = (k == i);
                nav[k].BackColor = (k == i) ? T.Card : T.Bg;
                nav[k].ForeColor = (k == i) ? T.Tx : T.Tx2;
                nav[k].Font = T.F(12.5f, (k == i) ? FontStyle.Bold : FontStyle.Regular);
            }
        }

        // ---------------------------------------------------------------- strona 1

        Panel BuildPlacesPage()
        {
            Panel pg = Page();

            Lab(pg, "Szukaj miejscowości", 0, 0);
            box.SetBounds(0, 22, 320, 26);
            box.BackColor = T.Card; box.ForeColor = T.Tx; box.BorderStyle = BorderStyle.FixedSingle;
            box.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoSearch(); }
            };

            Button find = Btn("Szukaj", 328, 21, 78);
            find.Click += delegate { DoSearch(); };

            Button ip = Btn("Wykryj po IP", 414, 21, 94);
            ip.Click += delegate { DetectIp(); };

            Lab(pg, "Wyniki — kliknij dwukrotnie, aby dodać", 0, 60);
            results.SetBounds(0, 82, 508, 118);
            Style(results);
            results.DoubleClick += delegate { AddSelected(); };

            Lab(pg, "Twoje miejscowości (pierwsza trafia na pasek zadań)", 0, 212);
            chosen.SetBounds(0, 234, 388, 68);
            Style(chosen);
            chosen.SelectedIndexChanged += delegate { ShowLines(); };

            Button up = Btn("Na pasek", 396, 234, 112);
            up.Click += delegate { MoveUp(); };
            Button del = Btn("Usuń", 396, 270, 112);
            del.Click += delegate { Remove(); };

            lblLines.SetBounds(0, 308, 508, 18);
            lblLines.ForeColor = T.Tx2; lblLines.Font = T.F(11, FontStyle.Bold);
            tbLines.SetBounds(0, 328, 388, 26);
            tbLines.BackColor = T.Card; tbLines.ForeColor = T.Tx;
            tbLines.BorderStyle = BorderStyle.FixedSingle;
            tbLines.TextChanged += delegate
            {
                if (loadingLines) return;
                int i = chosen.SelectedIndex;
                if (i >= 0 && i < places.Count) places[i].Lines = Place.ParseLines(tbLines.Text);
            };

            btnNear.Text = "Linie w pobliżu";
            btnNear.SetBounds(396, 327, 112, 28);
            btnNear.FlatStyle = FlatStyle.Flat;
            btnNear.FlatAppearance.BorderColor = T.Line;
            btnNear.BackColor = T.Card; btnNear.ForeColor = T.Tx;
            btnNear.Font = T.F(12); btnNear.Cursor = Cursors.Hand;
            btnNear.Click += delegate { SuggestLines(); };

            info.SetBounds(0, 360, 508, 40);
            info.ForeColor = T.Tx3;
            info.Font = T.F(11);

            pg.Controls.AddRange(new Control[] { box, find, ip, results, chosen, up, del,
                                                 lblLines, tbLines, btnNear, info });
            return pg;
        }

        // Pole linii zawsze dotyczy miejscowosci zaznaczonej na liscie.
        void ShowLines()
        {
            int i = chosen.SelectedIndex;
            bool on = i >= 0 && i < places.Count;
            loadingLines = true;
            tbLines.Text = on ? string.Join(", ", places[i].Lines.ToArray()) : "";
            loadingLines = false;
            tbLines.Enabled = on; btnNear.Enabled = on;
            lblLines.Text = on ? "Ulubione linie — " + places[i].Name + " (np. 4, 52, 212)"
                               : "Ulubione linie — zaznacz miejscowość na liście";
        }

        void SuggestLines()
        {
            int i = chosen.SelectedIndex;
            if (i < 0 || i >= places.Count) return;
            Place p = places[i];
            Say("Sprawdzam, jakie linie odjeżdżają z okolicy " + p.Name + "…");
            btnNear.Enabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> l = new List<string>();
                try { l = Transit.LinesNear(p.Lat, p.Lon); }
                catch { }
                BeginInvoke((Action)delegate
                {
                    btnNear.Enabled = true;
                    Say(l.Count == 0
                        ? "Nie znalazłem odjazdów w pobliżu " + p.Name + "."
                        : "W pobliżu " + p.Name + " jeżdżą: " + string.Join(", ", l.ToArray()) +
                          ". Przepisz wybrane do pola powyżej.");
                });
            });
        }

        // ---------------------------------------------------------------- strona 2

        Panel BuildBarPage()
        {
            Panel pg = Page();

            Lab(pg, "Położenie wskaźnika", 0, 0);
            Radio(rbLeft, "Po lewej stronie paska", 0, 24, "Left");
            Radio(rbRight, "Po prawej, przy zasobniku", 0, 52, "Right");
            Radio(rbOff, "Ukryj — tylko ikona w zasobniku", 0, 80, "Off");
            rbLeft.Checked = Cfg.Side == "Left";
            rbRight.Checked = Cfg.Side == "Right";
            rbOff.Checked = Cfg.Side == "Off";

            Lab(pg, "Odstęp od krawędzi (piksele)", 0, 120);
            numOffset.SetBounds(0, 144, 80, 26);
            Num(numOffset, 0, 200, Cfg.Offset);
            numOffset.ValueChanged += delegate
            {
                Cfg.Offset = (int)numOffset.Value;
                if (Live != null) Live();
            };

            Lab(pg, "Siła napisu na pasku", 0, 186);
            tbText.SetBounds(-4, 208, 300, 45);
            tbText.Minimum = 1; tbText.Maximum = 5; tbText.TickFrequency = 1;
            tbText.Value = Math.Max(1, Math.Min(5, Cfg.TextLevel));
            tbText.BackColor = T.Bg;
            tbText.ValueChanged += delegate
            {
                Cfg.TextLevel = tbText.Value;
                lblText.Text = TextName(tbText.Value);
                if (Live != null) Live();
            };
            lblText.SetBounds(304, 218, 204, 22);
            lblText.ForeColor = T.Tx2; lblText.Font = T.F(12);
            lblText.Text = TextName(Cfg.TextLevel);

            Label hint = new Label();
            hint.SetBounds(0, 254, 508, 34);
            hint.ForeColor = T.Tx3; hint.Font = T.F(11);
            hint.Text = "Zmiany widać od razu na pasku — przesuń suwak i sprawdź, " +
                        "czy napis jest czytelny na Twoim tle.";

            cbAuto.SetBounds(0, 300, 508, 24);
            cbAuto.Text = "Uruchamiaj razem z Windows";
            cbAuto.ForeColor = T.Tx; cbAuto.BackColor = T.Bg;
            cbAuto.Font = T.F(12.5f);
            cbAuto.Checked = Startup.Enabled;

            Label hint2 = new Label();
            hint2.SetBounds(20, 326, 488, 40);
            hint2.ForeColor = T.Tx3; hint2.Font = T.F(11);
            hint2.Text = "Wpis w rejestrze użytkownika (klucz Run) wskazujący tę kopię " +
                         "aplikacji. Nie wymaga uprawnień administratora.";

            pg.Controls.AddRange(new Control[] { rbLeft, rbRight, rbOff, numOffset,
                                                 tbText, lblText, hint, cbAuto, hint2 });
            return pg;
        }

        void Radio(RadioButton r, string text, int x, int y, string side)
        {
            r.Text = text;
            r.SetBounds(x, y, 420, 24);
            r.ForeColor = T.Tx; r.BackColor = T.Bg;
            r.Font = T.F(12.5f);
            r.CheckedChanged += delegate
            {
                if (!r.Checked) return;
                Cfg.Side = side;
                if (Live != null) Live();
            };
        }

        static string TextName(int lvl)
        {
            string[] n = { "1 — najdelikatniejszy", "2 — delikatny", "3 — średni",
                           "4 — mocny", "5 — najmocniejszy" };
            return n[Math.Max(1, Math.Min(5, lvl)) - 1];
        }

        // ---------------------------------------------------------------- strona 3

        static readonly string[] CardNames = {
            "Komunikacja \u2014 najbli\u017csze odjazdy",
            "Newsy lokalne",
            "Newsy sportowe",
            "Polityka i kraj",
            "Sztuczna inteligencja",
            "Gry — darmowe i promocje",
            "Gie\u0142da \u2014 waluty i metale",
            "Ropa i ceny paliw \u2014 Pb95, ON, LPG"
        };

        static bool CardValue(int i)
        {
            switch (i)
            {
                case 0: return Cfg.CardTransit;
                case 1: return Cfg.CardLocal;
                case 2: return Cfg.CardSport;
                case 3: return Cfg.CardPolitics;
                case 4: return Cfg.CardAi;
                case 5: return Cfg.CardGames;
                case 6: return Cfg.CardMarkets;
                default: return Cfg.CardFuel;
            }
        }

        Panel BuildCardsPage()
        {
            Panel pg = Page();

            Lab(pg, "Kolumny na zak\u0142adce News", 0, 0);
            for (int i = 0; i < 8; i++)
            {
                CheckBox c = new CheckBox();
                c.Text = CardNames[i];
                c.SetBounds(0, 24 + i * 26, 470, 24);
                c.ForeColor = T.Tx; c.BackColor = T.Bg;
                c.Font = T.F(12);
                c.Checked = CardValue(i);
                cbCard[i] = c;
                pg.Controls.Add(c);
            }

            Label h1 = new Label();
            h1.SetBounds(0, 234, 508, 32);
            h1.ForeColor = T.Tx3; h1.Font = T.F(11);
            h1.Text = "Wy\u0142\u0105czona kolumna znika, a okno zw\u0119\u017ca si\u0119 o jej szeroko\u015b\u0107 \u2014 " +
                      "nie zostaje po niej puste miejsce.";

            Lab(pg, "Rozmiar pisma w newsach", 0, 272);
            tbScale.SetBounds(-4, 294, 300, 45);
            tbScale.Minimum = 90; tbScale.Maximum = 140;
            tbScale.TickFrequency = 10; tbScale.SmallChange = 5; tbScale.LargeChange = 10;
            tbScale.Value = Math.Max(90, Math.Min(140, Cfg.Scale));
            tbScale.BackColor = T.Bg;
            tbScale.ValueChanged += delegate
            {
                lblScale.Text = tbScale.Value.ToString(CultureInfo.InvariantCulture) + " %";
            };
            lblScale.SetBounds(304, 304, 204, 22);
            lblScale.ForeColor = T.Tx2; lblScale.Font = T.F(12);
            lblScale.Text = Cfg.Scale.ToString(CultureInfo.InvariantCulture) + " %";

            Label h2 = new Label();
            h2.SetBounds(0, 340, 508, 44);
            h2.ForeColor = T.Tx3; h2.Font = T.F(11);
            h2.Text = "Skaluje tytu\u0142y, podpisy i miniatury w kartach z newsami. " +
                      "Zmiana widoczna po zamkni\u0119ciu i ponownym otwarciu panelu.";

            pg.Controls.AddRange(new Control[] { h1, tbScale, lblScale, h2 });
            return pg;
        }

        // ---------------------------------------------------------------- strona Sport

        Panel BuildSportPage()
        {
            Panel pg = Page();
            abroadId = Cfg.AbroadId; abroadName = Cfg.AbroadName;

            cbSportTab.SetBounds(0, 0, 508, 24);
            cbSportTab.Text = "Pokazuj zakładkę Sport w panelu";
            cbSportTab.ForeColor = T.Tx; cbSportTab.BackColor = T.Bg;
            cbSportTab.Font = T.F(12.5f);
            cbSportTab.Checked = Cfg.SportTab;

            Lab(pg, "Kluby z Ekstraklasy — wyróżnione w tabeli, po 5 meczów wstecz i naprzód", 0, 40);
            Lab2(pg, "Klub 1", 0, 68);
            Combo(cbClub1, 70, 64, Cfg.Club1);
            Lab2(pg, "Klub 2", 0, 102);
            Combo(cbClub2, 70, 98, Cfg.Club2);

            Lab(pg, "Klub zagraniczny — mniejsza karta obok bieżącej kolejki", 0, 144);
            tbAbroad.SetBounds(0, 166, 300, 26);
            tbAbroad.BackColor = T.Card; tbAbroad.ForeColor = T.Tx;
            tbAbroad.BorderStyle = BorderStyle.FixedSingle;
            tbAbroad.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SearchAbroad(); }
            };
            Button find = Btn("Szukaj", 308, 165, 90);
            find.Click += delegate { SearchAbroad(); };
            Button none = Btn("Bez klubu", 406, 165, 102);
            none.Click += delegate { abroadId = ""; abroadName = ""; ShowAbroad(); };

            lbAbroad.SetBounds(0, 200, 508, 84);
            Style(lbAbroad);
            lbAbroad.DoubleClick += delegate { PickAbroad(); };

            lblAbroad.SetBounds(0, 290, 508, 20);
            lblAbroad.ForeColor = T.Tx; lblAbroad.Font = T.F(12, FontStyle.Bold);
            ShowAbroad();

            lblSportInfo.SetBounds(0, 316, 508, 80);
            lblSportInfo.ForeColor = T.Tx3; lblSportInfo.Font = T.F(11);
            lblSportInfo.Text = "Wpisz nazwę po angielsku, np. Arsenal, Real Madrid, Bayern Munich, " +
                                "i kliknij wynik dwukrotnie. Tabela i terminarz Ekstraklasy pochodzą " +
                                "z ekstraklasa.org, kluby zagraniczne z TheSportsDB.";

            pg.Controls.AddRange(new Control[] { cbSportTab, cbClub1, cbClub2, tbAbroad, find, none,
                                                 lbAbroad, lblAbroad, lblSportInfo });
            return pg;
        }

        static void Lab2(Panel pg, string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text; l.ForeColor = T.Tx2; l.Font = T.F(12);
            l.SetBounds(x, y, 66, 20);
            pg.Controls.Add(l);
        }

        static void Combo(ComboBox c, int x, int y, string current)
        {
            c.SetBounds(x, y, 300, 28);
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = T.Card; c.ForeColor = T.Tx;
            c.Font = T.F(12.5f);
            c.Items.Add(NoClub);
            List<string> names = Sport.ClubNames();
            // klub spoza biezacej tabeli (np. po spadku) nie moze zniknac z wyboru
            if (current.Length > 0 && !names.Contains(current)) names.Add(current);
            foreach (string n in names) c.Items.Add(n);
            c.SelectedItem = current.Length > 0 ? current : NoClub;
        }

        static string ComboValue(ComboBox c)
        {
            string v = c.SelectedItem as string;
            return (v == null || v == NoClub) ? "" : v;
        }

        void ShowAbroad()
        {
            lblAbroad.Text = abroadId.Length > 0 ? "Wybrany: " + abroadName : "Wybrany: bez klubu zagranicznego";
        }

        void SearchAbroad()
        {
            string q = tbAbroad.Text.Trim();
            if (q.Length < 2) return;
            lbAbroad.Items.Clear(); abroadHits.Clear();
            lbAbroad.Items.Add("Szukam…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string[]> r = new List<string[]>();
                try { r = Sport.SearchTeams(q); }
                catch (Exception ex) { Program.Log("Szukaj klubu", ex); }
                BeginInvoke((Action)delegate
                {
                    lbAbroad.Items.Clear();
                    abroadHits.AddRange(r);
                    foreach (string[] t in r) lbAbroad.Items.Add(t[1] + "  —  " + t[2]);
                    if (r.Count == 0) lbAbroad.Items.Add("Nic nie znaleziono — spróbuj pełnej nazwy po angielsku.");
                });
            });
        }

        void PickAbroad()
        {
            int i = lbAbroad.SelectedIndex;
            if (i < 0 || i >= abroadHits.Count) return;
            abroadId = abroadHits[i][0];
            abroadName = abroadHits[i][1];
            ShowAbroad();
        }

        // ---------------------------------------------------------------- strona Dane

        Panel BuildDataPage()
        {
            Panel pg = Page();

            Lab(pg, "Odświeżanie pogody (minuty)", 0, 0);
            numWx.SetBounds(0, 24, 80, 26);
            Num(numWx, 5, 60, Cfg.WxMin);

            Lab(pg, "Odświeżanie newsów (minuty)", 0, 58);
            numNews.SetBounds(0, 82, 80, 26);
            Num(numNews, 5, 180, Cfg.NewsMin);

            Label h1 = new Label();
            h1.SetBounds(96, 82, 412, 44);
            h1.ForeColor = T.Tx3; h1.Font = T.F(11);
            h1.Text = "Kursy walut i metali pobierane są raz na dobę niezależnie od tego " +
                      "ustawienia, a odjazdy co minutę przy otwartym panelu.";

            Lab(pg, "Klucz AccuWeather (opcjonalnie)", 0, 128);
            tbKey.SetBounds(0, 152, 508, 26);
            tbKey.BackColor = T.Card; tbKey.ForeColor = T.Tx;
            tbKey.BorderStyle = BorderStyle.FixedSingle;
            tbKey.Text = Cfg.AccuKey;

            Label h2 = new Label();
            h2.SetBounds(0, 182, 508, 32);
            h2.ForeColor = T.Tx3; h2.Font = T.F(11);
            h2.Text = "Własny klucz z developer.accuweather.com włącza ich warstwę radaru. " +
                      "Bez klucza działa RainViewer.";

            Lab(pg, "Pamięć podręczna", 0, 216);
            Button clr = Btn("Wyczyść", 0, 240, 112);
            clr.Click += delegate { ClearCache(); };

            lblCache.SetBounds(124, 244, 384, 22);
            lblCache.ForeColor = T.Tx3; lblCache.Font = T.F(11);
            lblCache.Text = CacheSize();

            Label h3 = new Label();
            h3.SetBounds(0, 276, 508, 32);
            h3.ForeColor = T.Tx3; h3.Font = T.F(11);
            h3.Text = "Kursy, współrzędne stacji IMGW, godła klubów i miniatury newsów.";

            pg.Controls.AddRange(new Control[] { numWx, numNews, h1, tbKey, h2,
                                                 clr, lblCache, h3 });
            return pg;
        }

        static readonly string[] CacheFiles = { "markets.cache", "stacje.cache" };

        static string CacheSize()
        {
            long n = 0;
            int files = 0;
            try
            {
                foreach (string f in CacheFiles)
                {
                    string p = Path.Combine(Cfg.AppDir, f);
                    if (File.Exists(p)) { n += new FileInfo(p).Length; files++; }
                }
                string dir = Path.Combine(Cfg.AppDir, "herby");
                if (Directory.Exists(dir))
                    foreach (string p in Directory.GetFiles(dir))
                    { n += new FileInfo(p).Length; files++; }
            }
            catch { }
            if (files == 0) return "Nic nie zapisano.";
            return files.ToString(CultureInfo.InvariantCulture) + " plików, " +
                   (n / 1024).ToString(CultureInfo.InvariantCulture) + " kB.";
        }

        void ClearCache()
        {
            try
            {
                foreach (string f in CacheFiles)
                {
                    string p = Path.Combine(Cfg.AppDir, f);
                    if (File.Exists(p)) File.Delete(p);
                }
                string dir = Path.Combine(Cfg.AppDir, "herby");
                if (Directory.Exists(dir))
                    foreach (string p in Directory.GetFiles(dir))
                    {
                        try { File.Delete(p); }
                        catch { }
                    }
            }
            catch (Exception ex) { Program.Log("Cache", ex); }
            lblCache.Text = CacheSize();
        }

        // ---------------------------------------------------------------- wspolne

        static Panel Page()
        {
            Panel p = new Panel();
            p.BackColor = T.Bg;
            return p;
        }

        static void Lab(Panel pg, string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text; l.ForeColor = T.Tx2; l.Font = T.F(11, FontStyle.Bold);
            l.SetBounds(x, y, 460, 18);
            pg.Controls.Add(l);
        }

        static void Num(NumericUpDown n, int min, int max, int val)
        {
            n.Minimum = min; n.Maximum = max;
            n.Value = Math.Max(min, Math.Min(max, val));
            n.BackColor = T.Card; n.ForeColor = T.Tx;
            n.BorderStyle = BorderStyle.FixedSingle;
            n.Font = T.F(12.5f);
        }

        static Button Btn(string text, int x, int y, int w)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, 28);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = T.Line;
            b.BackColor = T.Card; b.ForeColor = T.Tx;
            b.Font = T.F(12);
            b.Cursor = Cursors.Hand;
            return b;
        }

        static void Style(ListBox lb)
        {
            lb.BackColor = T.Bg2; lb.ForeColor = T.Tx;
            lb.BorderStyle = BorderStyle.FixedSingle;
            lb.Font = T.F(12);
            lb.IntegralHeight = false;
        }

        void Say(string s) { info.Text = s; info.Refresh(); }

        void DoSearch()
        {
            string q = box.Text;
            Say("Szukam…");
            results.Items.Clear(); hits.Clear();
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<GeoHit> r = new List<GeoHit>();
                try { r = Geo.Search(q); }
                catch { }
                BeginInvoke((Action)delegate
                {
                    hits.AddRange(r);
                    foreach (GeoHit h in r) results.Items.Add(h.Label);
                    Say(r.Count == 0 ? "Nic nie znaleziono." : r.Count + " wyników.");
                });
            });
        }

        void DetectIp()
        {
            Say("Sprawdzam lokalizację po adresie IP…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                GeoHit h = null;
                try { h = Geo.ByIp(); }
                catch { }
                BeginInvoke((Action)delegate
                {
                    if (h == null) { Say("Nie udało się ustalić lokalizacji."); return; }
                    results.Items.Clear(); hits.Clear();
                    hits.Add(h); results.Items.Add(h.Label);
                    results.SelectedIndex = 0;
                    Say("Po IP wyszło: " + h.Label + ". Adres IP wskazuje węzeł operatora, " +
                        "a nie Twój dom — sprawdź i w razie czego popraw wyszukiwarką.");
                });
            });
        }

        void AddSelected()
        {
            int i = results.SelectedIndex;
            if (i < 0 || i >= hits.Count) return;
            if (places.Count >= 3) { Say("Maksymalnie trzy miejscowości."); return; }
            GeoHit h = hits[i];
            foreach (Place q in places)
                if (Geo.Km(q.Lat, q.Lon, h.Lat, h.Lon) < 1.0)
                { Say("Ta miejscowość jest już na liście."); return; }
            Place p = new Place();
            p.Name = h.Name; p.Lat = h.Lat; p.Lon = h.Lon; p.Region = h.Region;
            p.Recalc();
            places.Add(p);
            RefreshChosen();
            chosen.SelectedIndex = places.Count - 1;     // od razu mozna wpisac jej linie
        }

        void Remove()
        {
            int i = chosen.SelectedIndex;
            if (i < 0) return;
            if (places.Count <= 1) { Say("Musi zostać przynajmniej jedna miejscowość."); return; }
            places.RemoveAt(i);
            RefreshChosen();
        }

        void MoveUp()
        {
            int i = chosen.SelectedIndex;
            if (i <= 0) return;
            Place p = places[i];
            places.RemoveAt(i);
            places.Insert(0, p);
            chosen.SelectedIndex = 0;
            RefreshChosen();
        }

        void RefreshChosen()
        {
            int sel = chosen.SelectedIndex;
            chosen.Items.Clear();
            foreach (Place p in places)
                chosen.Items.Add(p.Name + (p.Region.Length > 0 ? ", " + p.Region : "") +
                    "   " + p.Lat.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                    p.Lon.ToString("0.00", CultureInfo.InvariantCulture));
            if (places.Count > 0) chosen.SelectedIndex = Math.Max(0, Math.Min(sel, places.Count - 1));
            ShowLines();
        }

        void Revert()
        {
            Cfg.Side = side0; Cfg.Offset = off0; Cfg.TextLevel = text0;
            if (Live != null) Live();
        }

        void SaveAndClose()
        {
            if (places.Count == 0)
            {
                ShowPage(0);
                Say("Dodaj przynajmniej jedną miejscowość.");
                return;
            }
            Cfg.Places.Clear();
            Cfg.Places.AddRange(places);
            Cfg.BarPlace = 0;
            Cfg.Offset = (int)numOffset.Value;
            Cfg.TextLevel = tbText.Value;
            Cfg.CardTransit = cbCard[0].Checked;
            Cfg.CardLocal = cbCard[1].Checked;
            Cfg.CardSport = cbCard[2].Checked;
            Cfg.CardPolitics = cbCard[3].Checked;
            Cfg.CardAi = cbCard[4].Checked;
            Cfg.CardGames = cbCard[5].Checked;
            Cfg.CardMarkets = cbCard[6].Checked;
            Cfg.CardFuel = cbCard[7].Checked;
            Cfg.Scale = tbScale.Value;
            Cfg.WxMin = (int)numWx.Value;
            Cfg.NewsMin = (int)numNews.Value;
            Cfg.AccuKey = tbKey.Text.Trim();
            Cfg.SportTab = cbSportTab.Checked;
            Cfg.Club1 = ComboValue(cbClub1);
            Cfg.Club2 = ComboValue(cbClub2);
            if (Cfg.Club2 == Cfg.Club1) Cfg.Club2 = "";      // ten sam klub dwa razy nic nie wnosi
            Cfg.AbroadId = abroadId;
            Cfg.AbroadName = abroadName;
            Cfg.Save();
            if (cbAuto.Checked != Startup.Enabled) Startup.Set(cbAuto.Checked);
            Applied = true;
            Close();
        }
    }
}
