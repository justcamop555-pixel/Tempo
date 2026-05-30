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

        public string ToDisplayString()
        {
            if (!IsValid)
            {
                return "(none)";
            }

            var sb = new StringBuilder();
            if (Control) sb.Append("Ctrl + ");
            if (Alt) sb.Append("Alt + ");
            if (Shift) sb.Append("Shift + ");
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
                case HotkeyMouseButton.Left: return "Left Click";
                case HotkeyMouseButton.Right: return "Right Click";
                case HotkeyMouseButton.Middle: return "Middle Click";
                case HotkeyMouseButton.XButton1: return "Mouse X1 (Back)";
                case HotkeyMouseButton.XButton2: return "Mouse X2 (Forward)";
                default: return "(mouse)";
            }
        }

        private static string KeyToString(Keys key)
        {
            // Friendly names for the most common keys.
            switch (key)
            {
                case Keys.Oemtilde: return "`";
                case Keys.Space: return "Space";
                case Keys.Escape: return "Esc";
                default: return key.ToString();
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

        /// <summary>
        /// Enumerates every virtual-key code that physically participates in this
        /// hotkey: the main key plus the generic and left/right variants of any
        /// modifier present. Used by the macro recorder so the keys you press to
        /// toggle recording (or trigger emergency stop) do not end up captured in
        /// the macro itself.
        /// </summary>
        public System.Collections.Generic.IEnumerable<int> ExcludedVirtualKeys()
        {
            if (Key != System.Windows.Forms.Keys.None)
            {
                yield return (int)Key;
            }

            if (Control)
            {
                yield return 0x11; // VK_CONTROL
                yield return 0xA2; // VK_LCONTROL
                yield return 0xA3; // VK_RCONTROL
            }

            if (Shift)
            {
                yield return 0x10; // VK_SHIFT
                yield return 0xA0; // VK_LSHIFT
                yield return 0xA1; // VK_RSHIFT
            }

            if (Alt)
            {
                yield return 0x12; // VK_MENU
                yield return 0xA4; // VK_LMENU
                yield return 0xA5; // VK_RMENU
            }

            if (Win)
            {
                yield return 0x5B; // VK_LWIN
                yield return 0x5C; // VK_RWIN
            }
        }

        public override string ToString()
        {
            return ToDisplayString();
        }
    }
}
