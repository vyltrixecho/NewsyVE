using System;
using System.Runtime.InteropServices;

namespace NewsyVE
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr FindWindow(string c, string w);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr c, string cls, string win);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_TOP = IntPtr.Zero;
        public const uint SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
        public const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
        public const int SW_SHOW = 5, SW_HIDE = 0;

        // Windows blokuje SetForegroundWindow z procesu, ktory nie jest na wierzchu.
        public static void ForceForeground(IntPtr h)
        {
            uint fg = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            uint my = GetCurrentThreadId();
            bool att = (fg != 0 && fg != my && AttachThreadInput(my, fg, true));
            ShowWindow(h, SW_SHOW);
            BringWindowToTop(h);
            SetForegroundWindow(h);
            if (att) AttachThreadInput(my, fg, false);
        }

        public class TaskbarInfo
        {
            public int L, T, R, B, TrayLeft;
            public int Height { get { return B - T; } }
        }

        public static TaskbarInfo Taskbar()
        {
            IntPtr tb = FindWindow("Shell_TrayWnd", null);
            if (tb == IntPtr.Zero) return null;
            RECT r;
            if (!GetWindowRect(tb, out r)) return null;
            TaskbarInfo t = new TaskbarInfo();
            t.L = r.Left; t.T = r.Top; t.R = r.Right; t.B = r.Bottom; t.TrayLeft = r.Right;
            IntPtr tray = FindWindowEx(tb, IntPtr.Zero, "TrayNotifyWnd", null);
            if (tray != IntPtr.Zero)
            {
                RECT tr;
                if (GetWindowRect(tray, out tr)) t.TrayLeft = tr.Left;
            }
            return t;
        }

        public static bool ForegroundIsFullscreen()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            if (fg == FindWindow("Progman", null)) return false;
            RECT r;
            if (!GetWindowRect(fg, out r)) return false;
            foreach (System.Windows.Forms.Screen s in System.Windows.Forms.Screen.AllScreens)
            {
                System.Drawing.Rectangle b = s.Bounds;
                if (r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom)
                    return true;
            }
            return false;
        }
    
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        // Belka tytulu zwyklego okna jest rysowana przez system i nie slucha
        // BackColor - o ciemny wariant trzeba poprosic DWM-a.
        public static void DarkTitleBar(IntPtr h)
        {
            try
            {
                int on = 1;
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE; starsze kompilacje Win10 znaja 19
                if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0)
                    DwmSetWindowAttribute(h, 19, ref on, 4);
            }
            catch { }
        }
    }
}
