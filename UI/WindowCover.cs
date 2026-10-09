using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// How much of a window the user can actually see.
    ///
    /// Windows calls a window "visible" when it is open and not minimised, however deeply it is
    /// buried. The tray's Show/Hide took that for "on screen", so double-clicking the tray icon to
    /// get a Tempo that sat behind a game or a browser HID it instead: nothing appeared, and it
    /// took a second double-click to bring it up. This looks at what actually lies above it.
    /// </summary>
    internal static class WindowCover
    {
        /// <summary>
        /// The share (0..1) of points spread across <paramref name="hwnd"/> that no other program's
        /// window covers. What does not count as covering: Tempo's own windows (its menus, dialogs
        /// and overlays), click-through overlays, and cloaked or minimised windows. Points that are
        /// on no screen are left out. 0 for a hidden or minimised window.
        /// </summary>
        internal static double VisibleShare(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || IsIconic(hwnd)) { return 0; }
            Rectangle me = FrameBounds(hwnd);
            if (me.Width <= 0 || me.Height <= 0) { return 0; }

            uint self = (uint)Environment.ProcessId;
            var covers = new List<Rectangle>();
            // Everything ABOVE it in the z-order, nearest first. The guard is Windows' own advice
            // for walking a list that can change while it is walked.
            IntPtr h = GetWindow(hwnd, GW_HWNDPREV);
            for (int guard = 0; h != IntPtr.Zero && guard < 5000; guard++, h = GetWindow(h, GW_HWNDPREV))
            {
                if (!IsWindowVisible(h) || IsIconic(h)) { continue; }
                GetWindowThreadProcessId(h, out uint pid);
                if (pid == self) { continue; }
                if (IsCloaked(h)) { continue; }
                int ex = GetWindowLong(h, GWL_EXSTYLE);
                if ((ex & WS_EX_LAYERED) != 0 && (ex & WS_EX_TRANSPARENT) != 0) { continue; }   // click-through overlay
                Rectangle r = FrameBounds(h);
                if (r.Width > 0 && r.Height > 0 && r.IntersectsWith(me)) { covers.Add(r); }
            }

            const int n = 7;
            int seen = 0, total = 0;
            for (int iy = 0; iy < n; iy++)
            {
                for (int ix = 0; ix < n; ix++)
                {
                    var p = new Point(me.Left + (int)((ix + 0.5) * me.Width / n),
                                      me.Top + (int)((iy + 0.5) * me.Height / n));
                    if (!OnAScreen(p)) { continue; }
                    total++;
                    bool covered = false;
                    foreach (Rectangle c in covers)
                    {
                        if (c.Contains(p)) { covered = true; break; }
                    }
                    if (!covered) { seen++; }
                }
            }
            return total == 0 ? 0 : (double)seen / total;
        }

        private static bool OnAScreen(Point p)
        {
            foreach (Screen s in Screen.AllScreens)
            {
                if (s.Bounds.Contains(p)) { return true; }
            }
            return false;
        }

        /// <summary>The window's visible frame: without the invisible resize borders DWM adds around it.</summary>
        private static Rectangle FrameBounds(IntPtr h)
        {
            if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT f, Marshal.SizeOf<RECT>()) == 0
                && f.Right > f.Left && f.Bottom > f.Top)
            {
                return Rectangle.FromLTRB(f.Left, f.Top, f.Right, f.Bottom);
            }
            return GetWindowRect(h, out RECT r) ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom) : Rectangle.Empty;
        }

        /// <summary>Cloaked: "visible" to Windows but not drawn (a suspended app, another virtual desktop).</summary>
        private static bool IsCloaked(IntPtr h)
        {
            return DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
        }

        private const uint GW_HWNDPREV = 3;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const int DWMWA_CLOAKED = 14;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);
    }
}
