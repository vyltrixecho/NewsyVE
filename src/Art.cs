using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace NewsyVE
{
    static class Art
    {
        static readonly Color Sun   = Color.FromArgb(255, 236, 180, 76);
        static readonly Color Moon  = Color.FromArgb(255, 214, 219, 228);
        static readonly Color Cloud = Color.FromArgb(255, 206, 210, 218);
        static readonly Color Cloud2= Color.FromArgb(255, 150, 155, 165);
        static readonly Color Rain  = Color.FromArgb(255, 120, 160, 198);
        static readonly Color Snow  = Color.FromArgb(255, 205, 214, 226);
        static readonly Color Bolt  = Color.FromArgb(255, 232, 178, 84);

        // Ikona pogodowa wpisana w kwadrat (x,y,s).
        public static void Glyph(Graphics g, int code, bool day, float x, float y, float s)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool hasCloud = code >= 2;
            Color body = day ? Sun : Moon;

            if (code <= 2 || (code >= 80 && code <= 82))
            {
                float r = s * (hasCloud ? 0.19f : 0.26f);
                float cx = x + s * (hasCloud ? 0.34f : 0.5f);
                float cy = y + s * (hasCloud ? 0.33f : 0.5f);
                using (SolidBrush b = new SolidBrush(body))
                    g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
                if (!hasCloud)
                    using (Pen p = new Pen(body, Math.Max(1f, s * 0.072f)))
                    {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                        for (int a = 0; a < 360; a += 45)
                        {
                            double t = a * Math.PI / 180;
                            g.DrawLine(p,
                                (float)(cx + Math.Cos(t) * r * 1.45), (float)(cy + Math.Sin(t) * r * 1.45),
                                (float)(cx + Math.Cos(t) * r * 1.95), (float)(cy + Math.Sin(t) * r * 1.95));
                        }
                    }
            }

            if (hasCloud)
            {
                Color cc = (code == 3) ? Cloud2 : Cloud;
                using (SolidBrush b = new SolidBrush(cc))
                {
                    g.FillEllipse(b, x + s * 0.13f, y + s * 0.40f, s * 0.42f, s * 0.40f);
                    g.FillEllipse(b, x + s * 0.41f, y + s * 0.29f, s * 0.48f, s * 0.46f);
                    g.FillRectangle(b, x + s * 0.21f, y + s * 0.56f, s * 0.60f, s * 0.22f);
                }
            }

            bool wet = (code >= 51 && code <= 67) || (code >= 80 && code <= 82) || code >= 95;
            bool ice = (code >= 71 && code <= 77) || code == 85 || code == 86;
            if (wet || ice)
                using (Pen p = new Pen(ice ? Snow : Rain, Math.Max(1.2f, s * 0.085f)))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    float[] dx = { 0.32f, 0.54f };
                    foreach (float d in dx)
                        g.DrawLine(p, x + s * d, y + s * 0.80f, x + s * (d - 0.05f), y + s * 0.96f);
                }

            if (code == 45 || code == 48)
                using (Pen p = new Pen(Cloud2, Math.Max(1.2f, s * 0.07f)))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, x + s * 0.20f, y + s * 0.80f, x + s * 0.78f, y + s * 0.80f);
                    g.DrawLine(p, x + s * 0.26f, y + s * 0.92f, x + s * 0.84f, y + s * 0.92f);
                }

            if (code >= 95)
                using (SolidBrush b = new SolidBrush(Bolt))
                    g.FillPolygon(b, new PointF[] {
                        new PointF(x + s*0.54f, y + s*0.70f), new PointF(x + s*0.38f, y + s*0.93f),
                        new PointF(x + s*0.52f, y + s*0.93f), new PointF(x + s*0.45f, y + s*1.08f),
                        new PointF(x + s*0.68f, y + s*0.82f), new PointF(x + s*0.53f, y + s*0.82f) });

            g.SmoothingMode = old;
        }

        // Logo aplikacji: grafitowy kafelek ze slonkiem i chmura.
        public static Bitmap AppIcon(int S)
        {
            Bitmap bmp = new Bitmap(S, S, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (GraphicsPath p = T.Round(new Rectangle(0, 0, S, S), (int)(S * 0.22)))
                using (LinearGradientBrush b = new LinearGradientBrush(
                    new Point(0, 0), new Point(S, S),
                    Color.FromArgb(255, 62, 64, 70), Color.FromArgb(255, 30, 31, 34)))
                    g.FillPath(b, p);

                float s = S * 0.78f;
                Glyph(g, 2, true, S * 0.11f, S * 0.08f, s);
            }
            return bmp;
        }

        public static Icon IconFrom(Bitmap b, out IntPtr handle)
        {
            handle = b.GetHicon();
            return Icon.FromHandle(handle);
        }

        public static void Card(Graphics g, Rectangle r)
        {
            using (GraphicsPath p = T.Round(r, 12))
            using (SolidBrush b = new SolidBrush(T.Card))
            using (Pen pen = new Pen(T.Line))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
        }

        public static void Text(Graphics g, string s, Font f, Color c, float x, float y)
        {
            using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, x, y);
        }

        public static void TextRight(Graphics g, string s, Font f, Color c, float right, float y)
        {
            SizeF sz = g.MeasureString(s, f);
            Text(g, s, f, c, right - sz.Width, y);
        }

        public static void Header(Graphics g, string s, Rectangle card, string rightNote)
        {
            using (Font f = T.F(10.5f, FontStyle.Bold))
            {
                Text(g, s.ToUpperInvariant(), f, T.Tx3, card.X + 12, card.Y + 9);
                if (!string.IsNullOrEmpty(rightNote))
                    using (Font f2 = T.F(10.5f))
                        TextRight(g, rightNote, f2, T.Tx3, card.Right - 12, card.Y + 9);
            }
        }
    }
}
