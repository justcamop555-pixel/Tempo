using System.Drawing;
using Microsoft.Win32;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Reads Windows' current light/dark preference so Tempo can follow it. Windows
    /// stores the app theme choice in a per-user registry value that flips when the
    /// user (or a schedule / Night-mode automation) switches between light and dark.
    /// </summary>
    public static class SystemTheme
    {
        private const string PersonalizeKey =
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string AppsUseLightValue = "AppsUseLightTheme";

        /// <summary>
        /// True when Windows is set to LIGHT app mode, false for dark. Defaults to
        /// dark (false) if the value can't be read — Tempo's own default look.
        /// </summary>
        public static bool IsWindowsLight()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, false))
                {
                    object v = key?.GetValue(AppsUseLightValue);
                    if (v is int i)
                    {
                        return i != 0;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Reads the user's chosen Windows accent colour (the one Settings ›
        /// Personalization › Colors sets, shown on Start, taskbar and highlights) so
        /// "Match Windows" can adopt it as Tempo's accent. Returns false if it can't be
        /// resolved, in which case the caller keeps the theme's own accent.
        ///
        /// Primary source is the WinRT <c>UISettings</c> accent — exactly the colour
        /// Windows hands to apps. If that projection isn't reachable, falls back to the
        /// DWM registry accent (stored little-endian ABGR).
        /// </summary>
        public static bool TryGetWindowsAccent(out Color accent)
        {
            accent = Color.Empty;

            // Primary: the canonical accent Windows exposes to apps.
            try
            {
                var ui = new Windows.UI.ViewManagement.UISettings();
                Windows.UI.Color c = ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
                if (!(c.R == 0 && c.G == 0 && c.B == 0))
                {
                    accent = Color.FromArgb(255, c.R, c.G, c.B);
                    return true;
                }
            }
            catch { }

            // Fallback: DWM's accent DWORD, stored as 0xAABBGGRR (little-endian ABGR).
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM", false))
                {
                    object v = key?.GetValue("AccentColor");
                    if (v is int abgr)
                    {
                        int r = abgr & 0xFF;
                        int g = (abgr >> 8) & 0xFF;
                        int b = (abgr >> 16) & 0xFF;
                        accent = Color.FromArgb(255, r, g, b);
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// The current Windows accent as a 32-bit ARGB value, or 0 if unresolved. Used
        /// to notice when the OS accent changes so a "Match Windows" app can re-theme
        /// live (the light/dark flag alone doesn't move when only the accent changes).
        /// </summary>
        public static int CurrentAccentArgb()
        {
            return TryGetWindowsAccent(out Color c) ? c.ToArgb() : 0;
        }
    }
}
