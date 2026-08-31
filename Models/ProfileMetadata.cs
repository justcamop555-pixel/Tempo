using System.Collections.Generic;
using System.Windows.Forms;

namespace AutoClicker.Models
{
    /// <summary>The library bucket a profile belongs to in the Profiles tab.</summary>
    public enum ProfileCategory
    {
        Gaming = 0,
        Work = 1,
        Productivity = 2,
        Custom = 3
    }

    /// <summary>
    /// A snapshot of the user's keybinds, saved inside a profile so that activating
    /// the profile can also restore its hotkeys. Stored as a plain action→hotkey
    /// list so it round-trips through JSON cleanly and tolerates new actions being
    /// added later (unknown ones are simply ignored on apply).
    /// </summary>
    public sealed class ProfileKeybinds
    {
        public List<HotkeyBinding> Bindings { get; set; } = new List<HotkeyBinding>();

        public ProfileKeybinds Clone()
        {
            var copy = new ProfileKeybinds();
            if (Bindings != null)
            {
                foreach (var b in Bindings)
                {
                    if (b != null) copy.Bindings.Add(b.Clone());
                }
            }
            return copy;
        }

        /// <summary>Captures the current binding set from AppSettings.</summary>
        public static ProfileKeybinds CaptureFrom(AppSettings settings)
        {
            var pk = new ProfileKeybinds();
            if (settings?.Bindings != null)
            {
                foreach (var b in settings.Bindings)
                {
                    if (b != null) pk.Bindings.Add(b.Clone());
                }
            }
            return pk;
        }

        /// <summary>Applies these bindings onto the given AppSettings in place.</summary>
        public void ApplyTo(AppSettings settings)
        {
            if (settings == null || Bindings == null) return;
            if (settings.Bindings == null)
            {
                settings.Bindings = new List<HotkeyBinding>();
            }
            foreach (var b in Bindings)
            {
                if (b == null || b.Hotkey == null) continue;
                var existing = settings.GetBinding(b.Action);
                if (existing != null)
                {
                    existing.Hotkey = b.Hotkey.Clone();
                }
                else
                {
                    settings.Bindings.Add(b.Clone());
                }
            }
        }
    }

    /// <summary>
    /// A snapshot of the application / overlay / theme settings saved inside a
    /// profile. Only the fields it makes sense to vary per profile are captured;
    /// anything not listed is left untouched when a profile is applied.
    /// </summary>
    public sealed class ProfileAppSettings
    {
        // Appearance
        public ThemeKind Theme { get; set; } = ThemeKind.Dark;
        public bool CustomAccentEnabled { get; set; }
        public int CustomAccentArgb { get; set; }

        // Notifications / overlay
        public bool ShowTrayNotifications { get; set; } = true;
        public bool AlwaysOnTop { get; set; }
        public bool ShowClickingIndicator { get; set; } = true;
        public bool NotifyOnRepeatFinish { get; set; }
        public int WindowOpacity { get; set; } = 100;

        // Caption / overlay look
        public bool CaptionOverlayEnabled { get; set; } = true;
        public int CaptionFontSize { get; set; } = 20;
        public int CaptionOpacity { get; set; } = 50;
        public string CaptionFontFamily { get; set; } = "Segoe UI";
        public bool CaptionUseCustomColor { get; set; } = true;
        public int CaptionColorArgb { get; set; } = unchecked((int)0xFFF4BF4F);
        public bool CaptionShowBackground { get; set; } = true;

        public ProfileAppSettings Clone()
        {
            return (ProfileAppSettings)MemberwiseClone();
        }

        /// <summary>Captures the relevant settings from a live AppSettings.</summary>
        public static ProfileAppSettings CaptureFrom(AppSettings s)
        {
            if (s == null) return new ProfileAppSettings();
            return new ProfileAppSettings
            {
                Theme = s.Theme,
                CustomAccentEnabled = s.CustomAccentEnabled,
                CustomAccentArgb = s.CustomAccentArgb,
                ShowTrayNotifications = s.ShowTrayNotifications,
                AlwaysOnTop = s.AlwaysOnTop,
                ShowClickingIndicator = s.ShowClickingIndicator,
                NotifyOnRepeatFinish = s.NotifyOnRepeatFinish,
                WindowOpacity = s.WindowOpacity,
                CaptionOverlayEnabled = s.CaptionOverlayEnabled,
                CaptionFontSize = s.CaptionFontSize,
                CaptionOpacity = s.CaptionOpacity,
                CaptionFontFamily = s.CaptionFontFamily,
                CaptionUseCustomColor = s.CaptionUseCustomColor,
                CaptionColorArgb = s.CaptionColorArgb,
                CaptionShowBackground = s.CaptionShowBackground
            };
        }

        /// <summary>Applies these settings onto a live AppSettings in place.</summary>
        public void ApplyTo(AppSettings s)
        {
            if (s == null) return;
            s.Theme = Theme;
            s.CustomAccentEnabled = CustomAccentEnabled;
            s.CustomAccentArgb = CustomAccentArgb;
            s.ShowTrayNotifications = ShowTrayNotifications;
            s.AlwaysOnTop = AlwaysOnTop;
            s.ShowClickingIndicator = ShowClickingIndicator;
            s.NotifyOnRepeatFinish = NotifyOnRepeatFinish;
            s.WindowOpacity = WindowOpacity;
            s.CaptionOverlayEnabled = CaptionOverlayEnabled;
            s.CaptionFontSize = CaptionFontSize;
            s.CaptionOpacity = CaptionOpacity;
            s.CaptionFontFamily = CaptionFontFamily;
            s.CaptionUseCustomColor = CaptionUseCustomColor;
            s.CaptionColorArgb = CaptionColorArgb;
            s.CaptionShowBackground = CaptionShowBackground;
        }
    }
}
