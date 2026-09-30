using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Threading;

namespace NewsyVE
{
    class RadarFrame
    {
        public DateTime Time;
        public string Path;      // np. https://host/v2/radar/xxxx
        public bool Nowcast;
    }

    // Radar skladany z kafli: podklad Esri + warstwa opadow RainViewer.
    // Kafle 512 px rysowane w 256 px -> obraz jest nadprobkowany, a nie rozmyty.
    class RadarEngine
    {
        const string EsriBase = "https://server.arcgisonline.com/ArcGIS/rest/services/Canvas/World_Dark_Gray_Base/MapServer/tile/";
        const string EsriRef  = "https://server.arcgisonline.com/ArcGIS/rest/services/Canvas/World_Dark_Gray_Reference/MapServer/tile/";
        const int TileDraw = 256;
        const int RvMaxNative = 7;

        readonly Dictionary<string, Image> cache = new Dictionary<string, Image>();
        readonly object gate = new object();

        public List<RadarFrame> Frames = new List<RadarFrame>();
        public int PastCount;
        public int Index;
        public double CenterLat = Geo.KrakowLat, CenterLon = Geo.KrakowLon;
        public int Zoom = 7;
        public Size View = new Size(460, 300);

        Bitmap baseMap;
        readonly Dictionary<int, Bitmap> overlays = new Dictionary<int, Bitmap>();
        string baseKey = "";

        public event Action Changed;
        void Fire() { Action a = Changed; if (a != null) a(); }

        static double Lon2X(double lon, int z) { return (lon + 180.0) / 360.0 * Math.Pow(2, z); }
        static double Lat2Y(double lat, int z)
        {
            double r = lat * Math.PI / 180.0;
            return (1 - Math.Log(Math.Tan(r) + 1.0 / Math.Cos(r)) / Math.PI) / 2.0 * Math.Pow(2, z);
        }
        public static PointF LatLonToPx(double lat, double lon, int z, double originX, double originY)
        {
            return new PointF((float)(Lon2X(lon, z) * TileDraw - originX), (float)(Lat2Y(lat, z) * TileDraw - originY));
        }

        public double OriginX { get { return Lon2X(CenterLon, Zoom) * TileDraw - View.Width / 2.0; } }
        public double OriginY { get { return Lat2Y(CenterLat, Zoom) * TileDraw - View.Height / 2.0; } }

        Image Tile(string url)
        {
            lock (gate) { Image c; if (cache.TryGetValue(url, out c)) return c; }
            try
            {
                byte[] b = Api.FetchBytes(url);
                Image img = Image.FromStream(new MemoryStream(b));
                lock (gate)
                {
                    if (cache.Count > 240)   // prosty limit pamieci na kafle
                    {
                        foreach (Image old in cache.Values) if (old != null) old.Dispose();
                        cache.Clear();
                    }
                    cache[url] = img;
                }
                return img;
            }
            catch
            {
                return null;   // bez zapisu do cache - nastepny rysunek sprobuje ponownie
            }
        }

        void ForEachTile(Action<int, int, float, float> draw)
        {
            double ox = OriginX, oy = OriginY;
            int x0 = (int)Math.Floor(ox / TileDraw), x1 = (int)Math.Floor((ox + View.Width) / TileDraw);
            int y0 = (int)Math.Floor(oy / TileDraw), y1 = (int)Math.Floor((oy + View.Height) / TileDraw);
            int max = (int)Math.Pow(2, Zoom);
            for (int tx = x0; tx <= x1; tx++)
                for (int ty = y0; ty <= y1; ty++)
                {
                    if (ty < 0 || ty >= max) continue;
                    int wx = ((tx % max) + max) % max;
                    draw(wx, ty, (float)(tx * TileDraw - ox), (float)(ty * TileDraw - oy));
                }
        }

        public void BuildBase()
        {
            string key = Zoom + "/" + CenterLat.ToString("0.####", CultureInfo.InvariantCulture) + "/" +
                         CenterLon.ToString("0.####", CultureInfo.InvariantCulture) + "/" + View.Width + "x" + View.Height;
            if (key == baseKey && baseMap != null) return;

            Bitmap bmp = new Bitmap(View.Width, View.Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(T.Bg2);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                ForEachTile(delegate (int tx, int ty, float px, float py)
                {
                    Image im = Tile(EsriBase + Zoom + "/" + ty + "/" + tx);
                    if (im != null) g.DrawImage(im, px, py, TileDraw, TileDraw);
                });
                System.Drawing.Imaging.ColorMatrix cm = new System.Drawing.Imaging.ColorMatrix();
                cm.Matrix33 = 0.62f;                       // slabsze angielskie nazwy z Esri
                using (System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes())
                {
                    ia.SetColorMatrix(cm);
                    ForEachTile(delegate (int tx, int ty, float px, float py)
                    {
                        Image im = Tile(EsriRef + Zoom + "/" + ty + "/" + tx);
                        if (im == null) return;
                        g.DrawImage(im, new Rectangle((int)px, (int)py, TileDraw, TileDraw),
                            0, 0, im.Width, im.Height, GraphicsUnit.Pixel, ia);
                    });
                }
                using (SolidBrush dim = new SolidBrush(Color.FromArgb(70, 16, 17, 19)))
                    g.FillRectangle(dim, 0, 0, View.Width, View.Height);
            }
            lock (gate)
            {
                if (baseMap != null) baseMap.Dispose();
                baseMap = bmp; baseKey = key;
            }
        }

        Bitmap BuildOverlay(int i)
        {
            if (i < 0 || i >= Frames.Count) return null;
            int z = Math.Min(Zoom, RvMaxNative);
            double scale = Math.Pow(2, Zoom - z);

            Bitmap bmp = new Bitmap(View.Width, View.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                double ox = OriginX / scale, oy = OriginY / scale;
                int drawSize = (int)(TileDraw * scale);
                int x0 = (int)Math.Floor(ox / TileDraw), x1 = (int)Math.Floor((ox + View.Width / scale) / TileDraw);
                int y0 = (int)Math.Floor(oy / TileDraw), y1 = (int)Math.Floor((oy + View.Height / scale) / TileDraw);
                int max = (int)Math.Pow(2, z);
                for (int tx = x0; tx <= x1; tx++)
                    for (int ty = y0; ty <= y1; ty++)
                    {
                        if (ty < 0 || ty >= max) continue;
                        int wx = ((tx % max) + max) % max;
                        string url = Frames[i].Path + "/512/" + z + "/" + wx + "/" + ty + "/4/1_1.png";
                        Image im = Tile(url);
                        if (im == null) continue;
                        float px = (float)((tx * TileDraw - ox) * scale);
                        float py = (float)((ty * TileDraw - oy) * scale);
                        g.DrawImage(im, px, py, drawSize, drawSize);
                    }
            }
            return bmp;
        }

        public void LoadFrames()
        {
            try
            {
                object root = Json.Parse(Api.Fetch("https://api.rainviewer.com/public/weather-maps.json"));
                string host = Json.Str(Json.At(root, "host"));
                object radar = Json.At(root, "radar");
                object[] past = Json.Arr(Json.At(radar, "past"));
                object[] soon = Json.Arr(Json.At(radar, "nowcast"));
                if (past.Length == 0) return;

                List<RadarFrame> list = new List<RadarFrame>();
                foreach (object o in past)
                    list.Add(new RadarFrame {
                        Time = Epoch(Json.Num(Json.At(o, "time"))),
                        Path = host + Json.Str(Json.At(o, "path")), Nowcast = false });
                foreach (object o in soon)
                    list.Add(new RadarFrame {
                        Time = Epoch(Json.Num(Json.At(o, "time"))),
                        Path = host + Json.Str(Json.At(o, "path")), Nowcast = true });

                if (Frames.Count == list.Count && Frames.Count > 0 &&
                    Frames[Frames.Count - 1].Path == list[list.Count - 1].Path) return;

                lock (gate)
                {
                    foreach (Bitmap b in overlays.Values) if (b != null) b.Dispose();
                    overlays.Clear();
                    Frames = list;
                    PastCount = past.Length;
                    Index = PastCount - 1;
                }
                Fire();
            }
            catch { }
        }

        static DateTime Epoch(double s)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(s).ToLocalTime();
        }

        public void Invalidate()
        {
            lock (gate)
            {
                baseKey = "";
                foreach (Bitmap b in overlays.Values) if (b != null) b.Dispose();
                overlays.Clear();
            }
        }

        // Rysuje aktualna klatke; brakujace kafle doczytuje w tle.
        public void Draw(Graphics g, Rectangle r)
        {
            Bitmap bm, ov = null;
            lock (gate)
            {
                bm = baseMap;
                if (Frames.Count > 0) overlays.TryGetValue(Index, out ov);
            }
            if (bm != null) g.DrawImage(bm, r.X, r.Y, r.Width, r.Height);
            else
            {
                using (SolidBrush b = new SolidBrush(T.Bg2)) g.FillRectangle(b, r);
                using (Font f = T.F(11)) Art.Text(g, "wczytywanie mapy…", f, T.Tx3, r.X + 12, r.Y + 12);
            }
            if (ov != null)
            {
                System.Drawing.Imaging.ColorMatrix cm = new System.Drawing.Imaging.ColorMatrix();
                cm.Matrix33 = 0.88f;
                using (System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes())
                {
                    ia.SetColorMatrix(cm);
                    g.DrawImage(ov, r, 0, 0, ov.Width, ov.Height, GraphicsUnit.Pixel, ia);
                }
            }
        }

        int building;
        public void EnsureReady(int idx)
        {
            if (Interlocked.CompareExchange(ref building, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    BuildBase();
                    int[] want = { idx, (idx + 1) % Math.Max(1, Frames.Count) };
                    foreach (int i in want)
                    {
                        bool has;
                        lock (gate) has = overlays.ContainsKey(i);
                        if (has || Frames.Count == 0) continue;
                        Bitmap b = BuildOverlay(i);
                        lock (gate) { if (!overlays.ContainsKey(i)) overlays[i] = b; else if (b != null) b.Dispose(); }
                    }
                    Fire();
                }
                catch { }
                finally { Interlocked.Exchange(ref building, 0); }
            });
        }

        public void PrefetchAll()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    BuildBase();
                    for (int i = 0; i < Frames.Count; i++)
                    {
                        bool has;
                        lock (gate) has = overlays.ContainsKey(i);
                        if (has) continue;
                        Bitmap b = BuildOverlay(i);
                        lock (gate) { if (!overlays.ContainsKey(i)) overlays[i] = b; else if (b != null) b.Dispose(); }
                        Fire();
                    }
                }
                catch { }
            });
        }
    }
}
