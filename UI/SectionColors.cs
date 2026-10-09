using System.Drawing;

namespace AutoClicker.UI
{
    /// <summary>The parts of Tempo that have a colour of their own, in sidebar order.</summary>
    public enum Section
    {
        Clicker = 0,
        Profiles = 1,
        MultiPoint = 2,
        Macros = 3,
        Statistics = 4,
        Keybinds = 5,
        Captions = 6,
        Settings = 7,
        Accounts = 8
    }

    /// <summary>
    /// The colour of each part of Tempo: its sidebar tab's icon colour. One table, so that
    /// everything standing for a part shows it in exactly the same colour.
    ///
    /// The tray menu kept a palette of its own that matched nothing: Captions was violet in
    /// the tray and teal in the sidebar, Profiles teal against lime, Settings grey against
    /// orchid, Accounts cyan against periwinkle. The two tables lived in two files and had
    /// drifted apart entirely.
    /// </summary>
    public static class SectionColors
    {
        /// <summary>For a light theme — saturated darks, so they don't wash out.</summary>
        internal static readonly Color[] Light =
        {
            Color.FromArgb(180, 78, 22),    // Clicker — burnt orange
            Color.FromArgb(86, 130, 24),    // Profiles — deep lime
            Color.FromArgb(12, 128, 84),    // Multi-Point — deep mint
            Color.FromArgb(112, 66, 200),   // Macros — violet
            Color.FromArgb(0, 100, 190),    // Statistics — deep sky blue
            Color.FromArgb(150, 108, 0),    // Keybinds — dark gold
            Color.FromArgb(24, 116, 130),   // Captions — deep teal
            Color.FromArgb(160, 44, 128),   // Settings — orchid
            Color.FromArgb(28, 86, 168)     // Accounts — deep indigo
        };

        /// <summary>For a dark theme. Hues match the Live-debug category palette.</summary>
        internal static readonly Color[] Dark =
        {
            Color.FromArgb(255, 170, 120),  // Clicker — soft orange
            Color.FromArgb(200, 235, 140),  // Profiles — soft lime
            Color.FromArgb(120, 230, 175),  // Multi-Point — mint
            Color.FromArgb(190, 165, 255),  // Macros — lavender
            Color.FromArgb(120, 200, 255),  // Statistics — sky blue
            Color.FromArgb(240, 205, 120),  // Keybinds — gold
            Color.FromArgb(125, 220, 225),  // Captions — teal
            Color.FromArgb(235, 150, 210),  // Settings — orchid
            Color.FromArgb(150, 185, 255)   // Accounts — periwinkle
        };

        /// <summary>True when <paramref name="theme"/> is a light one (the sidebar's own test).</summary>
        public static bool IsLight(Theme theme) =>
            theme != null && theme.Background.GetBrightness() > 0.5f;

        /// <summary>
        /// The section's colour as <paramref name="theme"/> paints it: the light or dark set, then
        /// the theme's HDR correction when its own colours had it — so it sits beside them exactly.
        /// </summary>
        public static Color For(Section section, Theme theme)
        {
            Color[] table = IsLight(theme) ? Light : Dark;
            int i = (int)section;
            Color designed = i >= 0 && i < table.Length ? table[i] : Color.Gray;
            return theme != null ? theme.Painted(designed) : designed;
        }
    }
}
