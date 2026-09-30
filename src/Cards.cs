using System.Collections.Generic;

namespace NewsyVE
{
    // Zakladka News sklada sie z kolumn wlaczanych w ustawieniach. Szerokosc
    // okna liczona jest z tego, ile kart zostalo - wylaczenie karty realnie
    // zweza panel, zamiast zostawiac po niej puste miejsce.
    static class Cards
    {
        public const int Transit = 0, Local = 1, Sport = 2,
                         Politics = 3, Games = 4, Markets = 5;

        public const int Pad = 12, Gap = 8;
        public const int Wide = 304, Narrow = 268;

        public static int Width(int kind) { return kind == Markets ? Narrow : Wide; }

        public static List<int> Order()
        {
            List<int> o = new List<int>();
            if (Cfg.CardTransit) o.Add(Transit);
            if (Cfg.CardLocal) o.Add(Local);
            if (Cfg.CardSport) o.Add(Sport);
            // polityka i AI dziela jedna kolumne - gdy zostanie jedna z nich,
            // zajmuje cala wysokosc
            if (Cfg.CardPolitics || Cfg.CardAi) o.Add(Politics);
            if (Cfg.CardGames) o.Add(Games);
            if (Cfg.CardMarkets || Cfg.CardFuel) o.Add(Markets);
            return o;
        }

        public static int TotalWidth()
        {
            List<int> o = Order();
            if (o.Count == 0) return 620;          // puste, ale nie zerowe okno
            int w = Pad * 2;
            for (int i = 0; i < o.Count; i++)
            {
                w += Width(o[i]);
                if (i > 0) w += Gap;
            }
            return w;
        }
    }
}
