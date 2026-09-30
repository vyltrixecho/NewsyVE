using System;
using Microsoft.Win32;

namespace NewsyVE
{
    // Autostart przez klucz Run biezacego uzytkownika - bez uprawnien
    // administratora i bez skrotu w katalogu Autostart, ktory uzytkownik
    // moglby skasowac nie wiedzac, skad sie wzial.
    static class Startup
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "NewsyVE";

        static string ExePath
        {
            get { return System.Reflection.Assembly.GetExecutingAssembly().Location; }
        }

        public static bool Enabled
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key, false))
                    {
                        if (k == null) return false;
                        string v = k.GetValue(Name) as string;
                        if (string.IsNullOrEmpty(v)) return false;
                        // wpis moze wskazywac na inna kopie - wtedy to nie nasz autostart
                        return v.Replace("\"", "").Trim().Equals(ExePath, StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch { return false; }
            }
        }

        public static void Set(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
                {
                    if (k == null) return;
                    if (on) k.SetValue(Name, "\"" + ExePath + "\"");
                    else if (k.GetValue(Name) != null) k.DeleteValue(Name, false);
                }
            }
            catch (Exception ex) { Program.Log("Autostart", ex); }
        }
    }
}
