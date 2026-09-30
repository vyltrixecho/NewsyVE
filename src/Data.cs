using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace NewsyVE
{
    class CurrentWx
    {
        public double Temp, Feels, Wind, Gust, Press;
        public int Code, Hum, Cloud, Dir;
        public bool Day;
        public string Src = "model ICON";
        public string SrcBasic = "model ICON";   // zrodlo wilgotnosci i cisnienia
    }

    class HourWx { public DateTime T; public double Temp; public int Code, Pop; }

    class DayWx
    {
        public DateTime T, Sunrise, Sunset;
        public double Min, Max, Precip, Uv;
        public int Code, Pop;
    }

    class PlaceData
    {
        public CurrentWx Cur;
        public List<HourWx> Hours = new List<HourWx>();
        public List<DayWx> Days = new List<DayWx>();
        public double[] M15 = new double[0];
        public DateTime[] M15T = new DateTime[0];
        public int Aqi = -1;
        public double Pm25;
    }

    class WarnItem { public string Name, Until; public int Level; }

    static class Json
    {
        static readonly JavaScriptSerializer S = MakeSer();
        static JavaScriptSerializer MakeSer()
        {
            JavaScriptSerializer s = new JavaScriptSerializer();
            s.MaxJsonLength = 40 * 1024 * 1024;
            return s;
        }

        public static object Parse(string s) { return S.DeserializeObject(s); }

        public static object At(object o, string key)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d == null) return null;
            object v;
            return d.TryGetValue(key, out v) ? v : null;
        }

        public static object[] Arr(object o)
        {
            object[] a = o as object[];
            return a == null ? new object[0] : a;
        }

        public static double Num(object o)
        {
            if (o == null) return double.NaN;
            if (o is double) return (double)o;
            if (o is int) return (int)o;
            if (o is decimal) return (double)(decimal)o;
            double d;
            if (double.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return d;
            return double.NaN;
        }

        public static int Int(object o) { double d = Num(o); return double.IsNaN(d) ? 0 : (int)Math.Round(d); }
        public static string Str(object o) { return o == null ? "" : Convert.ToString(o); }

        public static DateTime Time(object o)
        {
            DateTime t;
            if (DateTime.TryParse(Str(o), CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return t;
            return DateTime.MinValue;
        }
    }

    static class Api
    {
        // Adres zdjecia z naglowka og:image strony artykulu - dla kanalow,
        // ktore zdjec nie podaja.
        public static string OgImage(string url) { return OgReader.Read(url); }

        // WebClient sam nie prosi o kompresje, a odpowiedzi API UEFA sa po
        // spakowaniu 6-8 razy mniejsze. Tylko gzip i tylko na zyczenie:
        // wlaczone dla wszystkich zapytan zepsulo pogode - Open-Meteo odsyla
        // "deflate" w postaci zlib, ktorej .NET Framework nie rozpakowuje.
        class GzClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest r = base.GetWebRequest(address);
                HttpWebRequest h = r as HttpWebRequest;
                if (h != null) h.AutomaticDecompression = DecompressionMethods.GZip;
                return r;
            }
        }

        public static string FetchGz(string url)
        {
            using (WebClient c = new GzClient())
            {
                c.Encoding = Encoding.UTF8;
                c.Headers[HttpRequestHeader.UserAgent] = "NewsyVE/2.0";
                c.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                return c.DownloadString(url);
            }
        }

        public static string Fetch(string url)
        {
            using (WebClient c = new WebClient())
            {
                c.Encoding = Encoding.UTF8;
                c.Headers[HttpRequestHeader.UserAgent] = "NewsyVE/2.0";
                c.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                return c.DownloadString(url);
            }
        }

        // Kanaly RSS potrafia klamac w deklaracji XML - naglowek HTTP ma pierwszenstwo,
        // a gdy go brak, sprawdzamy czy bajty w ogole sa poprawnym UTF-8.
        public static string FetchText(string url)
        {
            using (WebClient c = new WebClient())
            {
                c.Headers[HttpRequestHeader.UserAgent] = "NewsyVE/2.0";
                c.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                byte[] data = c.DownloadData(url);

                string charset = null;
                string ct = c.ResponseHeaders != null ? c.ResponseHeaders["Content-Type"] : null;
                if (ct != null)
                {
                    int i = ct.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
                    if (i >= 0) charset = ct.Substring(i + 8).Trim().Trim('"', ';', ' ');
                }
                if (!string.IsNullOrEmpty(charset))
                {
                    try { return Encoding.GetEncoding(charset).GetString(data); }
                    catch { }
                }
                try { return new UTF8Encoding(false, true).GetString(data); }
                catch { }
                try { return Encoding.GetEncoding("iso-8859-2").GetString(data); }
                catch { return Encoding.UTF8.GetString(data); }
            }
        }

        public static byte[] FetchBytes(string url)
        {
            using (WebClient c = new WebClient())
            {
                c.Headers[HttpRequestHeader.UserAgent] = "NewsyVE/2.0";
                return c.DownloadData(url);
            }
        }

        static string Join(List<Place> p, bool lat)
        {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < p.Count; i++)
            {
                if (i > 0) b.Append(',');
                b.Append((lat ? p[i].Lat : p[i].Lon).ToString(CultureInfo.InvariantCulture));
            }
            return b.ToString();
        }

        public static PlaceData[] Forecast(List<Place> places)
        {
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" + Join(places, true) +
                "&longitude=" + Join(places, false) + "&timezone=Europe%2FWarsaw" +
                "&current=temperature_2m,apparent_temperature,relative_humidity_2m,is_day,weather_code," +
                "cloud_cover,pressure_msl,wind_speed_10m,wind_direction_10m,wind_gusts_10m" +
                "&minutely_15=precipitation&forecast_minutely_15=8" +
                "&hourly=temperature_2m,precipitation_probability,weather_code&forecast_hours=24" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum," +
                "precipitation_probability_max,sunrise,sunset&forecast_days=7" +
                // Domyslny best_match potrafi dla Krakowa zwrocic wilgotnosc 1%
                // i zero opadow w trakcie deszczu - ICON liczy ten obszar wlasna
                // siatka 2 km i zgadza sie z pomiarem IMGW.
                "&models=icon_seamless";

            object root = Json.Parse(Fetch(url));
            object[] items = root as object[];
            if (items == null) items = new object[] { root };

            PlaceData[] outp = new PlaceData[places.Count];
            for (int i = 0; i < places.Count && i < items.Length; i++)
                outp[i] = ParseOne(items[i]);
            return outp;
        }

        static PlaceData ParseOne(object o)
        {
            PlaceData d = new PlaceData();

            object c = Json.At(o, "current");
            CurrentWx w = new CurrentWx();
            w.Temp = Json.Num(Json.At(c, "temperature_2m"));
            w.Feels = Json.Num(Json.At(c, "apparent_temperature"));
            w.Hum = Json.Int(Json.At(c, "relative_humidity_2m"));
            w.Day = Json.Int(Json.At(c, "is_day")) != 0;
            w.Code = Json.Int(Json.At(c, "weather_code"));
            w.Cloud = Json.Int(Json.At(c, "cloud_cover"));
            w.Press = Json.Num(Json.At(c, "pressure_msl"));
            w.Wind = Json.Num(Json.At(c, "wind_speed_10m"));
            w.Dir = Json.Int(Json.At(c, "wind_direction_10m"));
            w.Gust = Json.Num(Json.At(c, "wind_gusts_10m"));
            d.Cur = w;

            object m = Json.At(o, "minutely_15");
            object[] mt = Json.Arr(Json.At(m, "time"));
            object[] mp = Json.Arr(Json.At(m, "precipitation"));
            d.M15 = new double[mp.Length];
            d.M15T = new DateTime[mt.Length];
            for (int i = 0; i < mp.Length; i++) d.M15[i] = Json.Num(mp[i]);
            for (int i = 0; i < mt.Length; i++) d.M15T[i] = Json.Time(mt[i]);

            object h = Json.At(o, "hourly");
            object[] ht = Json.Arr(Json.At(h, "time"));
            object[] hT = Json.Arr(Json.At(h, "temperature_2m"));
            object[] hP = Json.Arr(Json.At(h, "precipitation_probability"));
            object[] hC = Json.Arr(Json.At(h, "weather_code"));
            for (int i = 0; i < ht.Length; i++)
            {
                HourWx x = new HourWx();
                x.T = Json.Time(ht[i]);
                x.Temp = i < hT.Length ? Json.Num(hT[i]) : 0;
                x.Pop = i < hP.Length ? Json.Int(hP[i]) : 0;
                x.Code = i < hC.Length ? Json.Int(hC[i]) : 0;
                d.Hours.Add(x);
            }

            object dl = Json.At(o, "daily");
            object[] dt = Json.Arr(Json.At(dl, "time"));
            object[] dmax = Json.Arr(Json.At(dl, "temperature_2m_max"));
            object[] dmin = Json.Arr(Json.At(dl, "temperature_2m_min"));
            object[] dcode = Json.Arr(Json.At(dl, "weather_code"));
            object[] dpop = Json.Arr(Json.At(dl, "precipitation_probability_max"));
            object[] dsum = Json.Arr(Json.At(dl, "precipitation_sum"));
            object[] dsr = Json.Arr(Json.At(dl, "sunrise"));
            object[] dss = Json.Arr(Json.At(dl, "sunset"));
            object[] duv = Json.Arr(Json.At(dl, "uv_index_max"));
            for (int i = 0; i < dt.Length; i++)
            {
                DayWx x = new DayWx();
                x.T = Json.Time(dt[i]);
                x.Max = i < dmax.Length ? Json.Num(dmax[i]) : 0;
                x.Min = i < dmin.Length ? Json.Num(dmin[i]) : 0;
                x.Code = i < dcode.Length ? Json.Int(dcode[i]) : 0;
                x.Pop = i < dpop.Length ? Json.Int(dpop[i]) : 0;
                x.Precip = i < dsum.Length ? Json.Num(dsum[i]) : 0;
                x.Sunrise = i < dsr.Length ? Json.Time(dsr[i]) : DateTime.MinValue;
                x.Sunset = i < dss.Length ? Json.Time(dss[i]) : DateTime.MinValue;
                x.Uv = i < duv.Length ? Json.Num(duv[i]) : 0;
                d.Days.Add(x);
            }
            return d;
        }

        public static void AirQuality(List<Place> places, PlaceData[] data)
        {
            string url = "https://air-quality-api.open-meteo.com/v1/air-quality?latitude=" + Join(places, true) +
                "&longitude=" + Join(places, false) + "&current=european_aqi,pm2_5" +
                // UV liczy CAMS, a nie model pogodowy - stad razem z powietrzem
                "&daily=uv_index_max&forecast_days=7&timezone=Europe%2FWarsaw";
            object root = Json.Parse(Fetch(url));
            object[] items = root as object[];
            if (items == null) items = new object[] { root };
            for (int i = 0; i < data.Length && i < items.Length; i++)
            {
                if (data[i] == null) continue;
                object c = Json.At(items[i], "current");
                data[i].Aqi = Json.Int(Json.At(c, "european_aqi"));
                data[i].Pm25 = Json.Num(Json.At(c, "pm2_5"));

                object[] duv = Json.Arr(Json.At(Json.At(items[i], "daily"), "uv_index_max"));
                for (int k = 0; k < duv.Length && k < data[i].Days.Count; k++)
                    data[i].Days[k].Uv = Json.Num(duv[k]);
            }
        }

        // Stacja synoptyczna IMGW - tylko dla miejscowosci, ktore maja ja w poblizu.
        public static void ImgwStation(Place p, PlaceData d)
        {
            if (p.Imgw == null || d == null || d.Cur == null) return;
            try
            {
                object o = Json.Parse(Fetch("https://danepubliczne.imgw.pl/api/data/synop/id/" + p.Imgw));
                string date = Json.Str(Json.At(o, "data_pomiaru"));
                string hour = Json.Str(Json.At(o, "godzina_pomiaru"));
                double t = Json.Num(Json.At(o, "temperatura"));
                double hum = Json.Num(Json.At(o, "wilgotnosc_wzgledna"));
                double pr = Json.Num(Json.At(o, "cisnienie"));
                DateTime obs;
                if (!DateTime.TryParse(date + " " + hour.PadLeft(2, '0') + ":00",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out obs)) return;
                double age = (DateTime.Now - obs).TotalMinutes;
                string stamp = "IMGW " + p.ImgwName + " " + hour.PadLeft(2, '0') + ":00";
                bool fresh = age > -70 && age < 100;
                if (fresh)
                {
                    if (!double.IsNaN(hum)) d.Cur.Hum = (int)Math.Round(hum);
                    if (!double.IsNaN(pr)) d.Cur.Press = pr;
                    d.Cur.SrcBasic = stamp;
                }
                if (fresh && !double.IsNaN(t))
                {
                    d.Cur.Temp = t;
                    d.Cur.Src = stamp;
                }
                else if (!double.IsNaN(t))
                {
                    d.Cur.Src = "model ICON · " + p.ImgwName + " " + t.ToString("0.0", CultureInfo.InvariantCulture) +
                                "° o " + hour.PadLeft(2, '0') + ":00";
                }
            }
            catch { }
        }

        public static List<WarnItem> Warnings(Place p)
        {
            List<WarnItem> res = new List<WarnItem>();
            try
            {
                string raw = Fetch("https://danepubliczne.imgw.pl/api/data/warningsmeteo");
                object[] list = Json.Parse(raw) as object[];
                if (list == null) return res;
                foreach (object w in list)
                {
                    // IMGW podaje powiaty w TERYT; pierwsze dwie cyfry to
                    // wojewodztwo, wiec filtrujemy po nim, a dodatkowo po nazwie.
                    string txt = new JavaScriptSerializer().Serialize(w);
                    string code = p.Teryt2;
                    bool mine = false;
                    if (code.Length == 2 && txt.IndexOf("\"" + code, StringComparison.Ordinal) >= 0) mine = true;
                    if (!mine && p.Region.Length > 0)
                    {
                        string woj = p.Region.ToLowerInvariant().Replace("wojew\u00f3dztwo ", "");
                        if (woj.Length > 3 && txt.ToLowerInvariant().Contains(woj)) mine = true;
                    }
                    if (!mine) continue;
                    WarnItem it = new WarnItem();
                    it.Name = Json.Str(Json.At(w, "nazwa_zdarzenia"));
                    if (it.Name.Length == 0) it.Name = "Ostrzeżenie meteorologiczne";
                    it.Level = Json.Int(Json.At(w, "stopien"));
                    it.Until = Json.Str(Json.At(w, "obowiazuje_do")).Replace("T", " ");
                    if (it.Until.Length > 16) it.Until = it.Until.Substring(0, 16);
                    res.Add(it);
                }
            }
            catch { }   // 404 = brak ostrzezen
            return res;
        }
    }

    static class Wmo
    {
        static readonly Dictionary<int, string> M = Build();
        static Dictionary<int, string> Build()
        {
            Dictionary<int, string> d = new Dictionary<int, string>();
            d[0] = "Bezchmurnie"; d[1] = "Prawie bezchmurnie"; d[2] = "Częściowe zachmurzenie";
            d[3] = "Pochmurno"; d[45] = "Mgła"; d[48] = "Mgła osadzająca szadź";
            d[51] = "Słaba mżawka"; d[53] = "Mżawka"; d[55] = "Silna mżawka";
            d[56] = "Marznąca mżawka"; d[57] = "Silna marznąca mżawka";
            d[61] = "Słaby deszcz"; d[63] = "Deszcz"; d[65] = "Silny deszcz";
            d[66] = "Marznący deszcz"; d[67] = "Silny marznący deszcz";
            d[71] = "Słabe opady śniegu"; d[73] = "Opady śniegu"; d[75] = "Intensywne opady śniegu";
            d[77] = "Ziarna śnieżne"; d[80] = "Przelotny deszcz"; d[81] = "Przelotny deszcz";
            d[82] = "Ulewne przelotne opady"; d[85] = "Przelotny śnieg"; d[86] = "Intensywny przelotny śnieg";
            d[95] = "Burza"; d[96] = "Burza z gradem"; d[99] = "Silna burza z gradem";
            return d;
        }
        public static string Text(int code)
        {
            string s;
            return M.TryGetValue(code, out s) ? s : "—";
        }

        static readonly string[] Dirs = { "N","NNE","NE","ENE","E","ESE","SE","SSE","S","SSW","SW","WSW","W","WNW","NW","NNW" };
        public static string Dir(int deg) { return Dirs[((int)Math.Round(deg / 22.5)) % 16]; }
    }
}
