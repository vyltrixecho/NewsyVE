using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;

namespace NewsyVE
{
    // Herby klubow: pobierane raz, trzymane w pamieci i na dysku.
    // Get() nigdy nie blokuje rysowania - przy pierwszym wywolaniu zwraca null
    // i dociaga obrazek w tle, potem odswieza panel.
    static class Images
    {
        // Kazdy wczytany obrazek zostawal tu na zawsze - przy newsach
        // odswiezanych co kwadrans pamiec rosla bez konca. Trzymamy wiec
        // ograniczona liczbe, wyrzucajac najdawniej uzywane. Nie wolamy
        // Dispose: panel moze akurat rysowac ten obrazek w innym watku,
        // wiec zwalnianiem zajmuje sie odsmiecacz.
        const int MaxCache = 140;
        static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>();
        static readonly Dictionary<string, long> used = new Dictionary<string, long>();
        static readonly HashSet<string> pending = new HashSet<string>();
        static readonly object gate = new object();
        static long clock;

        static void Touch(string url) { used[url] = ++clock; }

        static void Trim()
        {
            while (cache.Count > MaxCache)
            {
                string oldest = null;
                long best = long.MaxValue;
                foreach (KeyValuePair<string, Image> kv in cache)
                {
                    long t;
                    if (!used.TryGetValue(kv.Key, out t)) t = 0;
                    if (t < best) { best = t; oldest = kv.Key; }
                }
                if (oldest == null) return;
                cache.Remove(oldest);
                used.Remove(oldest);
            }
        }

        static string Dir
        {
            get
            {
                string d = Path.Combine(Cfg.AppDir, "herby");
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                return d;
            }
        }

        static string FileFor(string url)
        {
            int h = 17;
            foreach (char c in url) h = h * 31 + c;
            return Path.Combine(Dir, (h & 0x7fffffff).ToString("x8") + ".img");
        }

        // Zdjecia z kanalow zmieniaja sie codziennie, a odkad dociagamy je takze
        // ze stron artykulow, katalog potrafi urosnac o kilkadziesiat megabajtow
        // dziennie. Stad dwa ograniczenia: wiek i laczny rozmiar.
        const int KeepDays = 3;
        const long MaxBytes = 40L * 1024 * 1024;

        public static void Prune()
        {
            try
            {
                DateTime cut = DateTime.Now.AddDays(-KeepDays);
                List<FileInfo> left = new List<FileInfo>();
                foreach (string f in Directory.GetFiles(Dir, "*.img"))
                {
                    try
                    {
                        FileInfo fi = new FileInfo(f);
                        if (fi.LastWriteTime < cut) fi.Delete();
                        else left.Add(fi);
                    }
                    catch { }
                }

                long total = 0;
                foreach (FileInfo fi in left) total += fi.Length;
                if (total <= MaxBytes) return;

                // najstarsze ida pierwsze, az zmiescimy sie w limicie
                left.Sort(delegate (FileInfo a, FileInfo b)
                { return a.LastWriteTime.CompareTo(b.LastWriteTime); });
                foreach (FileInfo fi in left)
                {
                    if (total <= MaxBytes) break;
                    try { long n = fi.Length; fi.Delete(); total -= n; }
                    catch { }
                }
            }
            catch { }
        }

        public static Image Get(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            lock (gate)
            {
                Image img;
                if (cache.TryGetValue(url, out img)) { Touch(url); return img; }
                if (pending.Contains(url)) return null;
                pending.Add(url);
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                Image loaded = null;
                try
                {
                    string f = FileFor(url);
                    byte[] data = File.Exists(f) ? File.ReadAllBytes(f) : null;
                    if (data == null)
                    {
                        data = Api.FetchBytes(url);
                        try { File.WriteAllBytes(f, data); } catch { }
                    }
                    using (Image src = Decode(data))
                    {
                        if (src == null) throw new Exception("nieznany format obrazu");
                        // zdjecia z kanalow potrafia miec 2000 px - w panelu i tak
                        // pokazujemy miniature, wiec trzymamy mniejsza kopie
                        const int MaxW = 360;
                        if (src.Width > MaxW)
                        {
                            int h = (int)Math.Round(src.Height * (MaxW / (double)src.Width));
                            Bitmap small = new Bitmap(MaxW, Math.Max(1, h));
                            using (Graphics gg = Graphics.FromImage(small))
                            {
                                gg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                gg.DrawImage(src, 0, 0, MaxW, h);
                            }
                            loaded = small;
                        }
                        else loaded = new Bitmap(src);
                    }
                }
                catch { loaded = null; }

                lock (gate) { cache[url] = loaded; Touch(url); pending.Remove(url); Trim(); }
                if (loaded != null) Store.Fire();
            });
            return null;
        }

        // Herb wpisany w kwadrat; gdy jeszcze go nie ma, rysuje sie delikatny placeholder.
        public static void Draw(Graphics g, string url, float x, float y, float size)
        {
            Image img = Get(url);
            if (img == null)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
                    g.FillEllipse(b, x, y, size, size);
                return;
            }
            System.Drawing.Drawing2D.InterpolationMode old = g.InterpolationMode;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            float sc = Math.Min(size / img.Width, size / img.Height);
            float w = img.Width * sc, h = img.Height * sc;
            g.DrawImage(img, x + (size - w) / 2, y + (size - h) / 2, w, h);
            g.InterpolationMode = old;
        }

        // Miniatura newsa: kadrowana do prostokata, z zaokraglonymi rogami.
        public static void Thumb(Graphics g, string url, Rectangle box)
        {
            Image img = Get(url);
            using (System.Drawing.Drawing2D.GraphicsPath p = T.Round(box, 5))
            {
                if (img == null)
                {
                    using (SolidBrush b = new SolidBrush(T.Bg2)) g.FillPath(b, p);
                    return;
                }
                Region old = g.Clip;
                g.SetClip(p, System.Drawing.Drawing2D.CombineMode.Replace);
                float sc = Math.Max(box.Width / (float)img.Width, box.Height / (float)img.Height);
                float w = img.Width * sc, h = img.Height * sc;
                System.Drawing.Drawing2D.InterpolationMode im = g.InterpolationMode;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
                g.InterpolationMode = im;
                g.Clip = old;
            }
        }
    
        // GDI+ nie zna WebP, a tak wysyla zdjecia czesc serwisow (m.in. Spider's Web).
        // Windows potrafi je rozpakowac przez WIC, wiec probujemy tej drogi jako
        // zapasowej. Gdy system nie ma kodeka, zostaje brak miniatury - jak dotad.
        static Image Decode(byte[] data)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(data))
                    return Image.FromStream(ms);
            }
            catch { }

            try
            {
                using (MemoryStream ms = new MemoryStream(data))
                {
                    System.Windows.Media.Imaging.BitmapDecoder dec =
                        System.Windows.Media.Imaging.BitmapDecoder.Create(ms,
                            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    if (dec.Frames.Count == 0) return null;

                    System.Windows.Media.Imaging.FormatConvertedBitmap conv =
                        new System.Windows.Media.Imaging.FormatConvertedBitmap(
                            dec.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
                    conv.Freeze();

                    int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
                    byte[] px = new byte[stride * h];
                    conv.CopyPixels(px, stride, 0);

                    Bitmap bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    System.Drawing.Imaging.BitmapData bd = bmp.LockBits(
                        new Rectangle(0, 0, w, h),
                        System.Drawing.Imaging.ImageLockMode.WriteOnly,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try
                    {
                        for (int y = 0; y < h; y++)
                            System.Runtime.InteropServices.Marshal.Copy(px, y * stride,
                                new IntPtr(bd.Scan0.ToInt64() + y * bd.Stride), stride);
                    }
                    finally { bmp.UnlockBits(bd); }
                    return bmp;
                }
            }
            catch (Exception ex) { Program.Log("WebP", ex); return null; }
        }
}
}
