using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AutoClicker.Models
{
    /// <summary>
    /// Keys named the way a keyboard prints them, and what a key combination already does somewhere
    /// else — one place for both, so the Keybinds tab, the clicker's key button and the macro steps
    /// stop naming the same key three different ways.
    ///
    /// <see cref="Keys.ToString"/> is a debugging name: the top-row 1 is "D1" (a binding read
    /// "Ctrl + D1"), the keypad's 1 "NumPad1", "Oemtilde", "Return", "LShiftKey", "Apps". And the
    /// punctuation keys were named from a US keyboard whatever the user's said: the key a German
    /// keyboard prints Ö on was shown as "`".
    /// </summary>
    public static class KeyNames
    {
        /// <summary>The name of a key as its keycap reads, in the interface language where it is a word.</summary>
        public static string Friendly(int virtualKey)
        {
            if (virtualKey <= 0 || virtualKey > 0xFF) { return "0x" + virtualKey.ToString("X2"); }
            return Friendly((Keys)virtualKey);
        }

        /// <summary>The name of a key as its keycap reads, in the interface language where it is a word.</summary>
        public static string Friendly(Keys key)
        {
            key &= Keys.KeyCode;
            if (key >= Keys.D0 && key <= Keys.D9) { return ((char)('0' + (key - Keys.D0))).ToString(); }
            // "Num 1": what Windows itself calls the keypad keys, and the word printed on Num Lock.
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) { return "Num " + (key - Keys.NumPad0); }
            if ((key >= Keys.A && key <= Keys.Z) || (key >= Keys.F1 && key <= Keys.F24)) { return key.ToString(); }

            switch (key)
            {
                case Keys.Multiply: return "Num *";
                case Keys.Add: return "Num +";
                case Keys.Subtract: return "Num -";
                case Keys.Decimal: return "Num .";
                case Keys.Divide: return "Num /";
                case Keys.Separator: return "Num ,";

                case Keys.Space: return Utils.Localization.T("Space");
                case Keys.Escape: return Utils.Localization.T("Esc");
                case Keys.Tab: return Utils.Localization.T("Tab");
                case Keys.Return: return Utils.Localization.T("Enter");
                case Keys.Back: return Utils.Localization.T("Backspace");
                case Keys.Delete: return Utils.Localization.T("Delete");
                case Keys.Insert: return Utils.Localization.T("Insert");
                case Keys.Home: return Utils.Localization.T("Home");
                case Keys.End: return Utils.Localization.T("End");
                case Keys.PageUp: return Utils.Localization.T("Page Up");
                case Keys.PageDown: return Utils.Localization.T("Page Down");
                case Keys.Left: return Utils.Localization.T("Left Arrow");
                case Keys.Right: return Utils.Localization.T("Right Arrow");
                case Keys.Up: return Utils.Localization.T("Up Arrow");
                case Keys.Down: return Utils.Localization.T("Down Arrow");
                case Keys.Capital: return Utils.Localization.T("Caps Lock");
                case Keys.NumLock: return Utils.Localization.T("Num Lock");
                case Keys.Scroll: return Utils.Localization.T("Scroll Lock");
                case Keys.PrintScreen: return Utils.Localization.T("Print Screen");
                case Keys.Pause: return Utils.Localization.T("Pause/Break");   // the keycap, not the verb
                case Keys.Apps: return Utils.Localization.T("Menu key");

                // Modifiers on their own: only macro steps press these.
                case Keys.ShiftKey: return Utils.Localization.T("Shift");
                case Keys.ControlKey: return Utils.Localization.T("Ctrl");
                case Keys.Menu: return Utils.Localization.T("Alt");
                case Keys.LShiftKey: return Utils.Localization.F("Left {0}", Utils.Localization.T("Shift"));
                case Keys.RShiftKey: return Utils.Localization.F("Right {0}", Utils.Localization.T("Shift"));
                case Keys.LControlKey: return Utils.Localization.F("Left {0}", Utils.Localization.T("Ctrl"));
                case Keys.RControlKey: return Utils.Localization.F("Right {0}", Utils.Localization.T("Ctrl"));
                case Keys.LMenu: return Utils.Localization.F("Left {0}", Utils.Localization.T("Alt"));
                case Keys.RMenu: return Utils.Localization.F("Right {0}", Utils.Localization.T("Alt"));
                case Keys.LWin: return Utils.Localization.F("Left {0}", "Win");
                case Keys.RWin: return Utils.Localization.F("Right {0}", "Win");

                // The media / browser row. Without these the enum names leak straight
                // into the UI as "MediaPreviousTrack" and "LaunchApplication1".
                case Keys.VolumeUp: return Utils.Localization.T("Volume Up");
                case Keys.VolumeDown: return Utils.Localization.T("Volume Down");
                case Keys.VolumeMute: return Utils.Localization.T("Mute");
                case Keys.MediaPlayPause: return Utils.Localization.T("Play / Pause");
                case Keys.MediaStop: return Utils.Localization.T("Stop");
                case Keys.MediaNextTrack: return Utils.Localization.T("Next Track");
                case Keys.MediaPreviousTrack: return Utils.Localization.T("Previous Track");
                case Keys.BrowserBack: return Utils.Localization.T("Browser Back");
                case Keys.BrowserForward: return Utils.Localization.T("Browser Forward");
                case Keys.BrowserRefresh: return Utils.Localization.T("Browser Refresh");
                case Keys.BrowserStop: return Utils.Localization.T("Browser Stop");
                case Keys.BrowserSearch: return Utils.Localization.T("Browser Search");
                case Keys.BrowserFavorites: return Utils.Localization.T("Browser Favourites");
                case Keys.BrowserHome: return Utils.Localization.T("Browser Home");
                case Keys.LaunchMail: return Utils.Localization.T("Mail");
                case Keys.SelectMedia: return Utils.Localization.T("Media Player");
                case Keys.LaunchApplication1: return Utils.Localization.T("App 1");
                case Keys.LaunchApplication2: return Utils.Localization.T("App 2");
            }

            string printed = PrintedCharacter(key);
            return printed ?? key.ToString();
        }

        /// <summary>
        /// The character a punctuation key prints on the keyboard layout in use (Ö, ß, &lt;, ´ …),
        /// falling back to the US keyboard's; null for any other key.
        /// </summary>
        private static string PrintedCharacter(Keys key)
        {
            string us;
            switch (key)
            {
                case Keys.Oemtilde: us = "`"; break;
                case Keys.OemMinus: us = "-"; break;
                case Keys.Oemplus: us = "="; break;
                case Keys.OemOpenBrackets: us = "["; break;
                case Keys.OemCloseBrackets: us = "]"; break;
                case Keys.OemSemicolon: us = ";"; break;
                case Keys.OemQuotes: us = "'"; break;
                case Keys.Oemcomma: us = ","; break;
                case Keys.OemPeriod: us = "."; break;
                case Keys.OemQuestion: us = "/"; break;
                case Keys.OemPipe: us = "\\"; break;
                case Keys.OemBackslash: us = "\\"; break;   // the key left of Z on a 102-key board
                case Keys.Oem8: us = null; break;
                default: return null;
            }
            try
            {
                // Unshifted character in the low word; bit 31 marks a dead key (´ ^ `), which still
                // prints its accent, so it is kept.
                uint mapped = MapVirtualKeyEx((uint)key, MAPVK_VK_TO_CHAR, GetKeyboardLayout(0));
                char c = (char)(mapped & 0xFFFF);
                if (c >= 0x20 && !char.IsWhiteSpace(c))
                {
                    return char.ToUpperInvariant(c).ToString();
                }
            }
            catch { }
            return us;
        }

        /// <summary>
        /// The character this Ctrl+Alt combination TYPES on the keyboard layout in use, or null.
        ///
        /// On most non-US layouts the right Alt key is AltGr, and Windows delivers AltGr as Ctrl+Alt.
        /// So Ctrl+Alt+Q is how a German keyboard types @, Ctrl+Alt+E is €, Ctrl+Alt+7 is {. A global
        /// hotkey on one of those takes the CHARACTER away from every program, and nothing said so: a
        /// modified combination counts as safe everywhere else on the Keybinds tab.
        /// </summary>
        public static string AltGrCharacter(HotkeyDefinition hk)
        {
            if (hk == null || hk.IsMouse || hk.Key == Keys.None || !hk.Control || !hk.Alt || hk.Win) { return null; }
            try
            {
                var state = new byte[256];
                state[VK_CONTROL] = 0x80;
                state[VK_LCONTROL] = 0x80;
                state[VK_MENU] = 0x80;
                state[VK_RMENU] = 0x80;
                if (hk.Shift) { state[VK_SHIFT] = 0x80; state[VK_LSHIFT] = 0x80; }

                IntPtr layout = GetKeyboardLayout(0);
                uint vk = (uint)(hk.Key & Keys.KeyCode);
                uint scan = MapVirtualKeyEx(vk, MAPVK_VK_TO_VSC, layout);
                var buffer = new StringBuilder(8);
                // Flag 4: leave the keyboard's dead-key state alone, so asking cannot change what
                // the user's next keystroke types.
                int n = ToUnicodeEx(vk, scan, state, buffer, buffer.Capacity, 4, layout);
                if (n == 0 || buffer.Length == 0) { return null; }
                string typed = buffer.ToString(0, Math.Min(buffer.Length, Math.Abs(n)));
                foreach (char c in typed)
                {
                    if (c < 0x20 || char.IsWhiteSpace(c)) { return null; }   // a control code, not a character
                }
                return typed;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// True for a combination nearly every program already uses — Ctrl+C, Ctrl+V, Ctrl+Z,
        /// Ctrl+S, Alt+Enter… A global hotkey takes it from all of them, and "add Ctrl/Alt/Shift"
        /// (the Keybinds tab's own advice for bare keys) leads straight to these.
        /// </summary>
        public static bool IsCommonShortcut(HotkeyDefinition hk)
        {
            if (hk == null || hk.IsMouse || hk.Win) { return false; }
            Keys k = hk.Key & Keys.KeyCode;
            if (hk.Control && !hk.Alt && !hk.Shift)
            {
                switch (k)
                {
                    case Keys.A: case Keys.C: case Keys.V: case Keys.X: case Keys.Z: case Keys.Y:
                    case Keys.S: case Keys.F: case Keys.P: case Keys.N: case Keys.O: case Keys.W:
                    case Keys.T:
                        return true;
                }
            }
            if (hk.Alt && !hk.Control && !hk.Shift)
            {
                return k == Keys.Return || k == Keys.Space;
            }
            return false;
        }

        /// <summary>
        /// True for one of Tempo's own window shortcuts: F11 (full screen), Ctrl+1…9 and the keypad's
        /// Ctrl+1…9 (tabs), Ctrl+Tab and Ctrl+Shift+Tab. A global hotkey is caught before any window
        /// sees the key — Tempo's own included — so binding one turns the shortcut off inside Tempo.
        /// </summary>
        public static bool IsTempoWindowShortcut(HotkeyDefinition hk)
        {
            if (hk == null || hk.IsMouse || hk.Win || hk.Alt) { return false; }
            Keys k = hk.Key & Keys.KeyCode;
            if (!hk.Control) { return !hk.Shift && k == Keys.F11; }
            if (k == Keys.Tab) { return true; }   // Ctrl+Tab and Ctrl+Shift+Tab
            if (hk.Shift) { return false; }
            return (k >= Keys.D1 && k <= Keys.D9) || (k >= Keys.NumPad1 && k <= Keys.NumPad9);
        }

        // ── Win32 ──────────────────────────────────────────────────────────────────────────────
        private const uint MAPVK_VK_TO_VSC = 0, MAPVK_VK_TO_CHAR = 2;
        private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12,
                          VK_LSHIFT = 0xA0, VK_LCONTROL = 0xA2, VK_RMENU = 0xA5;

        [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint idThread);
        [DllImport("user32.dll")] private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
                                              StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);
    }
}
