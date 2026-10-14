using System;
using System.Runtime.InteropServices;

namespace AutoClicker.UI
{
    /// <summary>
    /// Tells the Windows shell that a window covering whole monitors is NOT a full-screen app.
    ///
    /// The shell treats any visible window that covers a monitor as a full-screen app — invisible,
    /// click-through and never in front included — and Windows 11 then switches Do Not Disturb on by
    /// itself ("when using an app in full-screen mode", on out of the box): banners stop, and
    /// SHQueryUserNotificationState answers "busy". The cursor trail is such a window, spanning the
    /// virtual screen for as long as the trail is on, so turning the trail on silently turned Do Not
    /// Disturb on with it.
    ///
    /// Measured on this PC (scratchpad/trailrude): nothing → accepts notifications, DND off; a
    /// trail-shaped window → "busy (full screen)", DND ON; the same window with the shell's
    /// "NonRudeHWND" property → accepts notifications, DND off. The property is the shell's own opt-out
    /// for exactly this ("rude" is its word for a full-screen window); unlike making the window a pixel
    /// short of each edge, it holds for any monitor arrangement — a monitor in the MIDDLE of three is
    /// still covered by a shrunken virtual-screen window.
    ///
    /// Set it before the window is shown (the shell judges a window as it appears), and remove it
    /// before the window is destroyed, as with any window property.
    /// </summary>
    internal static class NotFullscreen
    {
        private const string Property = "NonRudeHWND";

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetProp(IntPtr hWnd, string name, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr RemoveProp(IntPtr hWnd, string name);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetProp(IntPtr hWnd, string name);

        public static void Mark(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) { return; }
            try { SetProp(hwnd, Property, (IntPtr)1); }
            catch (Exception ex) { Utils.Logger.Swallow("NotFullscreen.Mark", ex); }
        }

        public static void Unmark(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) { return; }
            try { RemoveProp(hwnd, Property); }
            catch { /* the window is going anyway */ }
        }

        /// <summary>For Live debug and the tests: is the window marked?</summary>
        public static bool IsMarked(IntPtr hwnd)
        {
            try { return hwnd != IntPtr.Zero && GetProp(hwnd, Property) != IntPtr.Zero; }
            catch { return false; }
        }
    }
}
