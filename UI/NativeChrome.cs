using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// Opts the app into the OS dark theme for non-client chrome that WinForms can't
    /// colour itself: the window scroll bars and the title bar. This replaces the chunky
    /// light-grey scroll bars with Windows' thin dark "DarkMode_Explorer" scroll bars and
    /// gives a dark title bar to match. All calls are best-effort — some use uxtheme
    /// ordinals that are version-specific — so any failure simply leaves the native look.
    /// </summary>
    internal static class NativeChrome
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        // uxtheme ordinals (Windows 10 1809+). Best-effort; wrapped in try/catch.
        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        private static extern int SetPreferredAppMode(int mode);

        [DllImport("uxtheme.dll", EntryPoint = "#136")]
        private static extern void FlushMenuThemes();

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private const int AppModeForceDark = 2;
        private const int AppModeForceLight = 3;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private static bool _appModeSet;
        private static bool _lastDark;

        /// <summary>Switches the process-wide preferred app mode (affects scroll bars, menus).</summary>
        public static void SetAppDarkMode(bool dark)
        {
            if (_appModeSet && _lastDark == dark)
            {
                return;
            }
            try
            {
                SetPreferredAppMode(dark ? AppModeForceDark : AppModeForceLight);
                FlushMenuThemes();
                _appModeSet = true;
                _lastDark = dark;
            }
            catch { /* ordinal not available on this build — ignore */ }
        }

        // Windows 11 (build 22000+) lets an app colour the DWM-drawn title bar itself,
        // instead of only choosing dark or light. Older builds simply ignore these.
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        /// <summary>COLORREF is 0x00BBGGRR — the reverse of the usual RGB packing.</summary>
        private static int ToColorRef(System.Drawing.Color c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }

        /// <summary>
        /// Paints the window's TITLE BAR in the theme's own colours, so the system chrome
        /// continues the app instead of sitting on top of it as a black (or white) strip.
        /// A dark title bar still didn't match a coloured theme — on Synthwave the bar
        /// read as plain black above a purple header. <paramref name="caption"/> should be
        /// the colour the app paints immediately below the bar, so the seam disappears.
        /// Silently does nothing on Windows 10, where only dark/light is available.
        /// </summary>
        public static void TintTitleBar(IntPtr hwnd, System.Drawing.Color caption,
                                        System.Drawing.Color text, System.Drawing.Color border)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }
            try
            {
                int cap = ToColorRef(caption);
                int txt = ToColorRef(text);
                int bdr = ToColorRef(border);
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref cap, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref txt, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref bdr, sizeof(int));
            }
            catch { /* pre-22000 build — the dark/light title bar stays as-is */ }
        }

        /// <summary>
        /// Themes the scroll bars of every scrolling control under <paramref name="root"/>.
        ///
        /// The scroll bar you see is drawn by Windows, and it only comes out dark if the
        /// owning control has been given the "DarkMode_Explorer" visual style. That was
        /// being applied to tab pages, list views, list boxes and combo boxes — and to
        /// nothing else. Multi-line text boxes and rich text boxes were missed entirely,
        /// so the Live debug log's scroll bar (a RichTextBox) rendered in the LIGHT style
        /// right next to a page whose scroll bar was dark. Two scroll bars, two different
        /// colours, in the same app.
        ///
        /// Separate windows were missed too: the theming only ever walked the main form,
        /// so every dialog with something scrollable in it — the update notes, the crash
        /// report, the macro editor — kept light scroll bars regardless of the theme.
        ///
        /// Only controls that actually scroll are touched. Handing a Button or a Label a
        /// different visual-style class would change how it draws for no reason.
        /// </summary>
        public static void ApplyAllScrollbarThemes(Control root, bool dark)
        {
            if (root == null) { return; }
            foreach (Control c in root.Controls)
            {
                try
                {
                    if (c is ListView lv)
                    {
                        ApplyListViewTheme(lv, dark);
                    }
                    else if (c is ComboBox)
                    {
                        ApplyComboListTheme(c, dark);
                    }
                    else if (c is TextBoxBase || c is ListBox || c is ScrollableControl)
                    {
                        // TextBoxBase covers TextBox AND RichTextBox; ScrollableControl
                        // covers Panel/TabPage/anything with AutoScroll.
                        ApplyScrollbarTheme(c, dark);
                    }
                }
                catch { /* one control refusing must not stop the rest */ }
                ApplyAllScrollbarThemes(c, dark);
            }
        }

        /// <summary>Gives <paramref name="c"/>'s scroll bars the dark or light Explorer theme.</summary>
        public static void ApplyScrollbarTheme(Control c, bool dark)
        {
            if (c == null || !c.IsHandleCreated)
            {
                return;
            }
            try { SetWindowTheme(c.Handle, dark ? "DarkMode_Explorer" : "Explorer", null); }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        private const int LVM_GETHEADER = 0x1000 + 31;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct CBRECT { public int Left, Top, Right, Bottom; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct COMBOBOXINFO
        {
            public int cbSize;
            public CBRECT rcItem;
            public CBRECT rcButton;
            public int stateButton;
            public IntPtr hwndCombo;
            public IntPtr hwndItem;
            public IntPtr hwndList;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetComboBoxInfo(IntPtr hwndCombo, ref COMBOBOXINFO pcbi);

        /// <summary>
        /// Themes a combo box's DROP-DOWN LIST window (a separate top-level native
        /// window) so its scroll bar matches the dark/light theme. The rows themselves
        /// are owner-drawn; only the scroll bar was still native-light.
        /// </summary>
        public static void ApplyComboListTheme(System.Windows.Forms.Control combo, bool dark)
        {
            if (combo == null || !combo.IsHandleCreated)
            {
                return;
            }
            try
            {
                var info = new COMBOBOXINFO
                {
                    cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(COMBOBOXINFO))
                };
                if (GetComboBoxInfo(combo.Handle, ref info) && info.hwndList != IntPtr.Zero)
                {
                    SetWindowTheme(info.hwndList, dark ? "DarkMode_Explorer" : "Explorer", null);
                }
            }
            catch { }
        }

        /// <summary>
        /// Dark (or light) native theme for a ListView INCLUDING its column-header strip.
        /// Without this the header control keeps Windows' light visual style in dark
        /// themes — a permanently light band that also "flashes" on every list refresh.
        /// </summary>
        public static void ApplyListViewTheme(System.Windows.Forms.ListView lv, bool dark)
        {
            if (lv == null || !lv.IsHandleCreated)
            {
                return;
            }
            try
            {
                SetWindowTheme(lv.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                IntPtr header = SendMessage(lv.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                if (header != IntPtr.Zero)
                {
                    SetWindowTheme(header, dark ? "DarkMode_ItemsView" : "ItemsView", null);
                }
            }
            catch { }
        }

        /// <summary>Dark or light title bar for the given top-level window.</summary>
        public static void SetTitleBarDark(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }
            try
            {
                int v = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));
                // Force the non-client frame to repaint so the caption picks up the new
                // colour immediately (otherwise it only changes on the next activate/resize).
                const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_FRAMECHANGED = 0x0020;
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);
            }
            catch { }
        }
    }
}
