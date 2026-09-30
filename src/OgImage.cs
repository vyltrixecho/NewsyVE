using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace NewsyVE
{
    // Pamiec adresow zdjec wyciagnietych ze stron artykulow. Artykul nie zmienia
    // zdjecia, wiec raz ustalony wynik jest wazny na zawsze - takze pusty, bo
    // inaczej przy kazdym odswiezeniu pukalibysmy do tych samych stron bez skutku.
    static class OgCache
    {
        static readonly Dictionary<string, string> map = new Dictionary<string, string>();
        static readonly object gate = new object();
        static bool loaded, dirty;

        static string Path0 { get { return System.IO.Path.Combine(Cfg.AppDir, "ogimage.cache"); } }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                if (!File.Exists(Path0)) return;
                foreach (string line in File.ReadAllLines(Path0, Encoding.UTF8))
                {
                    int i = line.IndexOf('\t');
                    if (i <= 0) continue;
                    map[line.Substring(0, i)] = line.Substring(i + 1);
                }
            }
            catch { }
        }

        public static string Get(string url)
        {
            lock (gate)
            {
                Load();
                string v;
                return map.TryGetValue(url, out v) ? v : null;
            }
        }

        public static void Put(string url, string img)
        {
            lock (gate)
            {
                Load();
                map[url] = img == null ? "" : img;
                dirty = true;
            }
        }

        public static void Save()
        {
            lock (gate)
            {
                if (!dirty) return;
                dirty = false;
                try
                {
                    // przy tysiacach wpisow plik rosnie bez sensu - trzymamy ostatnie
                    StringBuilder b = new StringBuilder();
                    int skip = Math.Max(0, map.Count - 1500);
                    int i = 0;
                    foreach (KeyValuePair<string, string> kv in map)
                    {
                        if (i++ < skip) continue;
                        if (kv.Key.IndexOf('\t') >= 0) continue;
                        b.Append(kv.Key).Append('\t').Append(kv.Value).Append("\r\n");
                    }
                    File.WriteAllText(Path0, b.ToString(), Encoding.UTF8);
                }
                catch { }
            }
        }
    }

    static partial class OgReader
    {
        static readonly Regex[] Rx = {
            new Regex("<meta[^>]+property=[\"']og:image[\"'][^>]+content=[\"']([^\"']+)",
                RegexOptions.IgnoreCase),
            new Regex("<meta[^>]+content=[\"']([^\"']+)[\"'][^>]+property=[\"']og:image[\"']",
                RegexOptions.IgnoreCase),
            new Regex("<meta[^>]+name=[\"']twitter:image[\"'][^>]+content=[\"']([^\"']+)",
                RegexOptions.IgnoreCase)
        };

        // Naglowek strony wystarczy - czytamy tylko poczatek odpowiedzi i zrywamy
        // polaczenie, zeby nie sciagac calego artykulu dla jednego adresu zdjecia.
        public static string Read(string url)
        {
            try
            {
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
                rq.UserAgent = "NewsyVE/2.0";
                rq.Timeout = 5000;
                rq.ReadWriteTimeout = 5000;
                rq.AllowAutoRedirect = true;

                using (WebResponse rs = rq.GetResponse())
                using (Stream st = rs.GetResponseStream())
                {
                    byte[] buf = new byte[96 * 1024];
                    int got = 0, n;
                    while (got < buf.Length &&
                           (n = st.Read(buf, got, buf.Length - got)) > 0) got += n;

                    string html = Encoding.UTF8.GetString(buf, 0, got);
                    foreach (Regex r in Rx)
                    {
                        Match m = r.Match(html);
                        if (!m.Success) continue;
                        string u = m.Groups[1].Value.Replace("&amp;", "&").Trim();
                        if (u.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return u;
                    }
                }
            }
            catch { }
            return "";
        }
    }
}
