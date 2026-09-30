using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace NewsyVE
{
    // Nakladka rysowana wprost na pasku zadan.
    class BarForm : Form
    {
        readonly Bitmap probe = new Bitmap(1, 1);
        public event Action Clicked;
        public event Action<Point> RightClicked;

        public BarForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(32, 32, 32);
            Size = new Size(150, 40);
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            Native.SetWindowLong(Handle, Native.GWL_EXSTYLE,
                ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right)
            {
                Action<Point> a = RightClicked;
                if (a != null) a(Control.MousePosition);
            }
            else if (e.Button == MouseButtons.Left)
            {
                Action a = Clicked;
                if (a != null) a();
            }
        }

        string TempText()
        {
            PlaceData d = Store.Data.Length > Cfg.BarPlace ? Store.Data[Cfg.BarPlace] : null;
            if (d == null || d.Cur == null || !Store.Online) return "--";
            return Math.Round(d.Cur.Temp).ToString(CultureInfo.InvariantCulture) + "°C";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            int h = Height;

            PlaceData d = Store.Data.Length > Cfg.BarPlace ? Store.Data[Cfg.BarPlace] : null;
            int code = (d != null && d.Cur != null) ? d.Cur.Code : 3;
            bool day = (d == null || d.Cur == null) ? true : d.Cur.Day;

            float gs = h * 0.62f;
            Art.Glyph(g, code, day, 4, (h - gs) / 2f, gs);

            string txt = TempText();
            string nm = Cfg.Places[Cfg.BarPlace].Name;
            using (Font fT = T.F(TempSize(h), Weight))
            using (Font fC = T.F(CitySize(h), Weight))
            {
                SizeF mT = g.MeasureString(txt, fT);
                float tx = gs + 9, ty = (h - mT.Height) / 2f;
                Halo(g, txt, fT, TempColor, tx, ty);

                SizeF mC = g.MeasureString(nm, fC);
                float cx = tx + mT.Width + 2, cy = (h - mC.Height) / 2f;
                Halo(g, nm, fC, CityColor, cx, cy);
            }
        }

        // poziom 1..5: jasnosc temperatury, jasnosc nazwy, krycie obwodki, pogrubienie
        static readonly int[,] Lv = {
            { 188, 166,  95, 0 },
            { 208, 186, 125, 0 },
            { 226, 204, 155, 0 },
            { 240, 221, 185, 1 },
            { 252, 238, 215, 1 }
        };
        static int L { get { return Math.Max(1, Math.Min(5, Cfg.TextLevel)) - 1; } }
        static FontStyle Weight { get { return Lv[L, 3] == 1 ? FontStyle.Bold : FontStyle.Regular; } }
        // na poziomie 5 v + 8 wychodzi poza 255 - bez przyciecia FromArgb rzuca wyjatek w OnPaint
        static int B(int v) { return Math.Min(255, v); }
        static Color TempColor { get { int v = Lv[L, 0]; return Color.FromArgb(255, B(v), B(v + 2), B(v + 8)); } }
        static Color CityColor { get { int v = Lv[L, 1]; return Color.FromArgb(255, B(v), B(v + 3), B(v + 11)); } }

        static float TempSize(int h) { return Math.Max(12f, h * 0.32f); }
        static float CitySize(int h) { return Math.Max(12f, h * 0.30f); }

        // Ciemna obwodka dookola liter - napis czytelny na kazdym tle paska zadan.
        static void Halo(Graphics g, string s, Font f, Color fill, float x, float y)
        {
            using (SolidBrush dark = new SolidBrush(Color.FromArgb(Lv[L, 2], 0, 0, 0)))
            {
                g.DrawString(s, f, dark, x - 1, y);
                g.DrawString(s, f, dark, x + 1, y);
                g.DrawString(s, f, dark, x, y - 1);
                g.DrawString(s, f, dark, x, y + 1);
            }
            using (SolidBrush b = new SolidBrush(fill)) g.DrawString(s, f, b, x, y);
        }

        public int NeededWidth()
        {
            int h = Height;
            float gs = h * 0.62f;
            using (Graphics g = Graphics.FromImage(probe))
            using (Font fT = T.F(TempSize(h), Weight))
            using (Font fC = T.F(CitySize(h), Weight))
            {
                float w = gs + 9 + g.MeasureString(TempText(), fT).Width + 2
                        + g.MeasureString(Cfg.Places[Cfg.BarPlace].Name, fC).Width + 6;
                return (int)w;
            }
        }

        public void Reposition()
        {
            if (Cfg.Side == "Off") { if (Visible) Hide(); return; }
            Native.TaskbarInfo t = Native.Taskbar();
            if (t == null) return;
            if (Native.ForegroundIsFullscreen()) { if (Visible) Hide(); return; }

            int h = Math.Max(28, Math.Min(48, t.Height - 4));
            if (Height != h) Height = h;
            int w = NeededWidth();
            if (Width != w) { Width = w; Invalidate(); }

            int y = t.T + (t.Height - h) / 2;
            int x = Cfg.Side == "Left" ? t.L + Cfg.Offset : t.TrayLeft - Width - Cfg.Offset;

            if (!Visible) Show();
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, x, y, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            SyncBackColor(x, y, Width, h);
        }

        // Tlo paska = kolor paska zadan obok - wtapia sie, a mysz nadal lapie.
        void SyncBackColor(int x, int y, int w, int h)
        {
            try
            {
                int px = Cfg.Side == "Left" ? x + w + 10 : x - 10;
                using (Graphics g = Graphics.FromImage(probe))
                    g.CopyFromScreen(px, y + h / 2, 0, 0, new Size(1, 1));
                Color c = probe.GetPixel(0, 0);
                if (BackColor.ToArgb() != c.ToArgb()) { BackColor = c; Invalidate(); }
            }
            catch { }
        }
    }
}
