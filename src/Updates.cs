using System;
using System.Globalization;
using System.Threading;

namespace NewsyVE
{
    // Raz na dobe pyta GitHuba o najnowsze wydanie. Tylko informuje - nic nie
    // pobiera ani nie podmienia. Instalator z Releases zrobi aktualizacje sam,
    // a uzytkownik widzi, co uruchamia.
    static class Updates
    {
        public const string Repo = "vyltrixecho/NewsyVE";
        public const string Page = "https://github.com/" + Repo + "/releases/latest";

        public static string Latest = "";        // nowsza wersja niz biezaca, pusta = brak
        public static event Action Found;

        // z menu ("Sprawdz aktualizacje") pytamy zawsze, z timera tylko gdy
        // uzytkownik tego nie wylaczyl
        public static void Check(bool force, Action<string> done)
        {
            if (!force && !Cfg.UpdCheck) return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                string result;
                try
                {
                    object rel = Json.Parse(Api.Fetch(
                        "https://api.github.com/repos/" + Repo + "/releases/latest"));
                    string tag = Convert.ToString(Json.At(rel, "tag_name"), CultureInfo.InvariantCulture) ?? "";
                    Version remote;
                    if (!Version.TryParse(tag.TrimStart('v', 'V'), out remote))
                        throw new FormatException("nieczytelny numer wydania: " + tag);

                    if (Newer(remote))
                    {
                        Latest = Short(remote);
                        result = "Dostępna jest wersja " + Latest + ".";
                        Action f = Found;
                        if (f != null) f();
                    }
                    else result = "Masz najnowszą wersję (" + Cfg.Version + ").";
                }
                catch (Exception ex)
                {
                    Program.Log("Updates", ex);
                    result = "Nie udało się sprawdzić — brak połączenia z GitHubem.";
                }
                if (done != null) done(result);
            });
        }

        static bool Newer(Version remote)
        {
            Version cur;
            if (!Version.TryParse(Cfg.Version, out cur)) return false;
            return Norm(remote) > Norm(cur);
        }

        // "2.1" i "2.1.0" to ta sama wersja - Version uwaza je za rozne
        static Version Norm(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }

        static string Short(Version v) { return Norm(v).ToString(3); }
    }
}
