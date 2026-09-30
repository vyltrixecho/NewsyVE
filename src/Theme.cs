using System.Drawing;
using System.Drawing.Drawing2D;

namespace NewsyVE
{
    // Motyw grafitowy - neutralne szarosci, bez niebieskiej poswiaty.
    static class T
    {
        public static readonly Color Bg      = Color.FromArgb(255, 30, 31, 34);
        public static readonly Color Bg2     = Color.FromArgb(255, 24, 25, 27);
        public static readonly Color Card    = Color.FromArgb(255, 39, 40, 44);
        public static readonly Color Card2   = Color.FromArgb(255, 47, 48, 53);
        public static readonly Color Line    = Color.FromArgb(255, 58, 59, 65);
        public static readonly Color Tx      = Color.FromArgb(255, 232, 233, 237);
        public static readonly Color Tx2     = Color.FromArgb(255, 158, 160, 167);
        public static readonly Color Tx3     = Color.FromArgb(255, 112, 114, 122);
        public static readonly Color Acc     = Color.FromArgb(255, 126, 166, 204);
        public static readonly Color AccDim  = Color.FromArgb(255, 74, 98, 122);
        public static readonly Color Warn    = Color.FromArgb(255, 215, 163, 92);
        public static readonly Color Bad     = Color.FromArgb(255, 208, 106, 108);
        public static readonly Color Ok      = Color.FromArgb(255, 126, 184, 140);

        public const string Face = "Segoe UI";

        public static Font F(float size)               { return new Font(Face, size, FontStyle.Regular, GraphicsUnit.Pixel); }
        public static Font F(float size, FontStyle st) { return new Font(Face, size, st, GraphicsUnit.Pixel); }

        public static GraphicsPath Round(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
