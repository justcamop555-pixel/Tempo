using System;
using System.Text;
using System.Windows.Forms;

namespace AutoClicker.Models
{
    /// <summary>
    /// A mouse button that can serve as a hotkey trigger. Side buttons (X1/X2)
    /// and the middle button are the safe choices; Left/Right are allowed but only
    /// sensible when combined with a modifier, since binding a bare Left/Right
    /// would consume ordinary clicking.
    /// </summary>
    public enum HotkeyMouseButton
    {
        None = 0,
        Left = 1,
        Right = 2,
        Middle = 3,
        XButton1 = 4,
        XButton2 = 5
    }

    /// <summary>
    /// Serializable description of a hotkey combination. Stores modifier flags
    /// plus EITHER a keyboard key OR a mouse button, and can render itself as a
    /// friendly string such as "Ctrl + Shift + F6" or "Alt + Mouse X1".
    /// </summary>
    public sealed class HotkeyDefinition
    {
        public bool Control { get; set; }
        public bool Alt { get; set; }
        public bool Shift { get; set; }
        public bool Win { get; set; }

        /// <summary>The main key, stored as a <see cref="Keys"/> value.</summary>
        public Keys Key { get; set; } = Keys.None;

        /// <summary>
        /// The mouse button trigger. When this is not <see cref="HotkeyMouseButton.None"/>
        /// the hotkey is mouse-based and <see cref="Key"/> is ignored.
        /// </summary>
        public HotkeyMouseButton MouseButton { get; set; } = HotkeyMouseButton.None;

        public HotkeyDefinition()
        {
        }

        public HotkeyDefinition(Keys key, bool control = false, bool alt = false, bool shift = false, bool win = false)
        {
            Key = key;
            Control = control;
            Alt = alt;
            Shift = shift;
            Win = win;
        }

        /// <summary>True when this hotkey triggers on a mouse button.</summary>
        public bool IsMouse => MouseButton != HotkeyMouseButton.None;

        public bool IsValid => Key != Keys.None || MouseButton != HotkeyMouseButton.None;

        /// <summary>Modifier flags formatted for RegisterHotKey.</summary>
        public uint GetModifierFlags()
        {
            uint mods = 0;
            if (Control) mods |= 0x0002; // MOD_CONTROL
            if (Alt) mods |= 0x0001;     // MOD_ALT
            if (Shift) mods |= 0x0004;   // MOD_SHIFT
            if (Win) mods |= 0x0008;     // MOD_WIN
            return mods;
        }

        public uint GetVirtualKey()
        {
            return (uint)Key;
        }

        /// <summary>
        /// A stable identity for the question "are these the same combination?".
        /// Culture-independent, and never shown to anyone.
        ///
        /// <see cref="ToDisplayString"/> must NOT be used for that question, which is what
        /// the Keybinds tab used to do. That string is deliberately TRANSLATED — it names
        /// keys as they are printed on the user's own keyboard — and it funnels many
        /// distinct <see cref="Keys"/> values through a friendly-name table. So two
        /// different combinations rendering to one identical string is one translation
        /// away at any time, in any of the six languages, with nothing to catch it.
        ///
        /// That would not merely misreport. The tab does not just FLAG a duplicate, it
        /// clears the other field so the newest binding wins — so a false match silently
        /// destroys a hotkey the user had deliberately set. Comparing the flags and codes
        /// underneath cannot drift, whatever a translator does.
        /// </summary>
        public string ToIdentityString()
        {
            // Fixed-width flags and a tagged trigger, so no combination can spell
            // another: mouse and keyboard triggers live in separate namespaces, matching
            // ToDisplayString's rule that Key is ignored once MouseButton is set.
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return (Control ? "C" : "-")
                 + (Alt ? "A" : "-")
                 + (Shift ? "S" : "-")
                 + (Win ? "W" : "-")
                 + (IsMouse
                     ? "|m" + ((int)MouseButton).ToString(inv)
                     : "|k" + ((int)Key).ToString(inv));
        }

        /// <summary>
        /// True when <paramref name="other"/> is the same combination as this one.
        /// Field-by-field rather than via <see cref="ToIdentityString"/> so a pairwise
        /// check costs no allocation; the two agree by construction.
        /// </summary>
        public bool SameCombination(HotkeyDefinition other)
        {
            if (other == null)
            {
                return false;
            }

            if (Control != other.Control || Alt != other.Alt ||
                Shift != other.Shift || Win != other.Win)
            {
                return false;
            }

            if (MouseButton != other.MouseButton)
            {
                return false;
            }

            // Both mouse: the buttons matched above and Key is not part of the identity.
            return IsMouse || Key == other.Key;
        }

        public string ToDisplayString()
        {
            if (!IsValid)
            {
                return Utils.Localization.T("(none)");
            }

            // The modifier names are translated because they are printed on the user's
            // actual keyboard in their own language: a German board says Strg, an
            // Italian one Maiusc. Showing "Ctrl + Shift" to someone whose keys are
            // labelled otherwise makes them translate the shortcut in their head every
            // time they read it. Win is left alone — it is a logo, not a word.
            var sb = new StringBuilder();
            if (Control) sb.Append(Utils.Localization.T("Ctrl")).Append(" + ");
            if (Alt) sb.Append(Utils.Localization.T("Alt")).Append(" + ");
            if (Shift) sb.Append(Utils.Localization.T("Shift")).Append(" + ");
            if (Win) sb.Append("Win + ");

            if (IsMouse)
            {
                sb.Append(MouseButtonToString(MouseButton));
            }
            else
            {
                sb.Append(KeyToString(Key));
            }

            return sb.ToString();
        }

        private static string MouseButtonToString(HotkeyMouseButton button)
        {
            switch (button)
            {
                case HotkeyMouseButton.Left: return Utils.Localization.T("Left Click");
                case HotkeyMouseButton.Right: return Utils.Localization.T("Right Click");
                case HotkeyMouseButton.Middle: return Utils.Localization.T("Middle Click");
                case HotkeyMouseButton.XButton1: return Utils.Localization.T("Mouse X1 (Back)");
                case HotkeyMouseButton.XButton2: return Utils.Localization.T("Mouse X2 (Forward)");
                default: return Utils.Localization.T("(mouse)");
            }
        }

        private static string KeyToString(Keys key)
        {
            // Friendly names. Keys.ToString() produces things like "Oemtilde",
            // "PageUp" and "Return", which read like debug output on a settings page.
            switch (key)
            {
                case Keys.Oemtilde: return "`";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.OemPipe: return "\\";
                case Keys.Space: return Utils.Localization.T("Space");
                case Keys.Escape: return Utils.Localization.T("Esc");
                case Keys.Tab: return Utils.Localization.T("Tab");
                case Keys.Return: return Utils.Localization.T("Enter");
                case Keys.Back: return Utils.Localization.T("Backspace");
                case Keys.Delete: return Utils.Localization.T("Delete");
                case Keys.Insert: return Utils.Localization.T("Insert");
                case Keys.PageUp: return Utils.Localization.T("Page Up");
                case Keys.PageDown: return Utils.Localization.T("Page Down");
                case Keys.Left: return Utils.Localization.T("Left Arrow");
                case Keys.Right: return Utils.Localization.T("Right Arrow");
                case Keys.Up: return Utils.Localization.T("Up Arrow");
                case Keys.Down: return Utils.Localization.T("Down Arrow");
                case Keys.Capital: return Utils.Localization.T("Caps Lock");
                case Keys.PrintScreen: return Utils.Localization.T("Print Screen");

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
                default: return key.ToString();
            }
        }

        /// <summary>
        /// True when this is a BARE (unmodified) key that Windows normally needs for
        /// typing or moving around — a letter, a digit, Tab, Space, Enter, an arrow…
        ///
        /// This matters because a global hotkey is registered SYSTEM-WIDE: the key stops
        /// being delivered to whatever app you are using. Bind a bare "A" and you can no
        /// longer type the letter A anywhere while Tempo runs; bind a bare Tab and you
        /// can no longer Tab between fields in any program. Function keys are exempt —
        /// they are what hotkeys are for, and are why F6/F8 are the defaults.
        ///
        /// Tempo still ALLOWS these (a game pad-style binding may want exactly this) —
        /// it just refuses to let it happen silently.
        /// </summary>
        public bool IsRiskyBareKey
        {
            get
            {
                if (IsMouse || Key == Keys.None) { return false; }
                if (Control || Alt || Shift || Win) { return false; }   // modified = safe

                if (Key >= Keys.F1 && Key <= Keys.F24) { return false; }
                if (Key >= Keys.A && Key <= Keys.Z) { return true; }
                if (Key >= Keys.D0 && Key <= Keys.D9) { return true; }
                if (Key >= Keys.NumPad0 && Key <= Keys.NumPad9) { return true; }

                switch (Key)
                {
                    case Keys.Tab:
                    case Keys.Space:
                    case Keys.Return:
                    case Keys.Back:
                    case Keys.Delete:
                    case Keys.Escape:
                    case Keys.Left:
                    case Keys.Right:
                    case Keys.Up:
                    case Keys.Down:
                    case Keys.Home:
                    case Keys.End:
                    case Keys.PageUp:
                    case Keys.PageDown:
                        return true;

                    // The media / browser row belongs here for exactly the reason above,
                    // and it only became reachable once the capture control learned to
                    // read WM_APPCOMMAND. Bind Volume Up and you can no longer change the
                    // volume anywhere while Tempo runs; bind Play/Pause and it stops
                    // reaching your music player. Unlike F1-F24 — which exist to be
                    // bound — these keys already have a job the user expects them to do,
                    // so taking one over is a trade worth stating out loud. Tempo still
                    // allows it; it just refuses to do it silently.
                    case Keys.VolumeUp:
                    case Keys.VolumeDown:
                    case Keys.VolumeMute:
                    case Keys.MediaPlayPause:
                    case Keys.MediaStop:
                    case Keys.MediaNextTrack:
                    case Keys.MediaPreviousTrack:
                    case Keys.BrowserBack:
                    case Keys.BrowserForward:
                    case Keys.BrowserRefresh:
                    case Keys.BrowserStop:
                    case Keys.BrowserSearch:
                    case Keys.BrowserFavorites:
                    case Keys.BrowserHome:
                    case Keys.LaunchMail:
                    case Keys.SelectMedia:
                    case Keys.LaunchApplication1:
                    case Keys.LaunchApplication2:
                        return true;

                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// True for a bare mouse binding that Tempo will SWALLOW system-wide.
        ///
        /// The keyboard half of this — <see cref="IsRiskyBareKey"/> — has always earned a
        /// prominent amber warning, and the mouse half had none, because that property
        /// returns false for every mouse binding on its first line. But
        /// GlobalHotkeyManager suppresses every match that is not a bare Left/Right, so a
        /// bare Middle, X1 or X2 genuinely does stop doing its normal job everywhere while
        /// Tempo runs — middle-click no longer opens links in a new tab, X1 no longer goes
        /// Back — which is exactly the consequence the amber warning exists to announce.
        ///
        /// Bare Left/Right are excluded because the manager deliberately lets those
        /// through rather than eating ordinary clicking.
        /// </summary>
        public bool IsRiskyBareMouseButton
        {
            get
            {
                if (!IsMouse) { return false; }
                if (Control || Alt || Shift || Win) { return false; }
                return MouseButton != HotkeyMouseButton.Left
                    && MouseButton != HotkeyMouseButton.Right;
            }
        }

        public HotkeyDefinition Clone()
        {
            return new HotkeyDefinition
            {
                Control = Control,
                Alt = Alt,
                Shift = Shift,
                Win = Win,
                Key = Key,
                MouseButton = MouseButton
            };
        }

        /// <summary>
        /// Returns true when exactly the modifiers required by this hotkey are
        /// currently physically held (no more, no fewer). Used by the hook-based
        /// mouse-button hotkeys and the hold-mode poll. <paramref name="isDown"/>
        /// is the async key-state probe (returns true if the given VK is down).
        /// </summary>
        public bool ModifiersMatch(Func<int, bool> isDown)
        {
            if (isDown == null)
            {
                return false;
            }

            bool ctrl = isDown(0x11);
            bool shift = isDown(0x10);
            bool alt = isDown(0x12);
            bool win = isDown(0x5B) || isDown(0x5C);

            return ctrl == Control && shift == Shift && alt == Alt && win == Win;
        }

        public override string ToString()
        {
            return ToDisplayString();
        }
    }
}
