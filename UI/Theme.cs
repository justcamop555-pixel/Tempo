using System;
using System.Drawing;
using AutoClicker.Models;

namespace AutoClicker.UI
{
    /// <summary>
    /// A named palette of colours used to style the whole application. Three
    /// presets are provided via <see cref="ForKind"/>.
    /// </summary>
    public sealed class Theme
    {
        public ThemeKind Kind { get; private set; }

        public Color Background { get; private set; }
        public Color Surface { get; private set; }
        public Color Surface2 { get; private set; }
        public Color Border { get; private set; }
        public Color Accent { get; private set; }
        public Color AccentHover { get; private set; }
        public Color Success { get; private set; }
        public Color Danger { get; private set; }
        public Color Warning { get; private set; }
        public Color Text { get; private set; }
        public Color TextMuted { get; private set; }
        public Color InputBackground { get; private set; }

        /// <summary>
        /// Returns a copy of this theme with the accent (and a derived hover shade)
        /// replaced by <paramref name="accent"/>. Used for the custom-accent option.
        /// </summary>
        public Theme WithAccent(Color accent)
        {
            return new Theme
            {
                Kind = Kind,
                Background = Background,
                Surface = Surface,
                Surface2 = Surface2,
                Border = Border,
                Accent = accent,
                AccentHover = Lighten(accent, 0.14),
                Success = Success,
                Danger = Danger,
                Warning = Warning,
                Text = Text,
                TextMuted = TextMuted,
                InputBackground = InputBackground
            };
        }

        /// <summary>
        /// The readable text colour for anything drawn ON <see cref="Accent"/> — black or
        /// white, whichever the eye can actually read.
        ///
        /// Primary buttons paint their label straight onto the accent, and that label was
        /// hardcoded to white. For the built-in themes that is fine, because all 38 are
        /// audited against their own colours. A CUSTOM accent skips that audit entirely,
        /// so picking a pale one — yellow, mint, light cyan — left white text on a light
        /// background and made every primary button in the app unreadable, with nothing
        /// anywhere to say so.
        ///
        /// Deciding per-colour rather than per-theme means any accent the user can choose
        /// stays legible, including ones nobody has thought of.
        /// </summary>
        public Color OnAccent => ReadableOn(Accent);

        /// <summary>
        /// Black or white, whichever contrasts better with <paramref name="bg"/>, by the
        /// WCAG relative-luminance formula the theme audit uses.
        /// </summary>
        public static Color ReadableOn(Color bg)
        {
            return ContrastRatio(Color.White, bg) >= ContrastRatio(Color.Black, bg)
                ? Color.White : Color.Black;
        }

        /// <summary>WCAG 2.1 contrast ratio between two colours, 1.0 to 21.0.</summary>
        public static double ContrastRatio(Color a, Color b)
        {
            double la = RelativeLuminance(a), lb = RelativeLuminance(b);
            double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
            return (hi + 0.05) / (lo + 0.05);
        }

        private static double RelativeLuminance(Color c)
        {
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        private static double Channel(int v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        /// <summary>
        /// <paramref name="fg"/> if it already reads against <paramref name="bg"/>,
        /// otherwise the same colour pushed away from the background until it does.
        ///
        /// The status colours are a palette's personality — Success is green, Warning is
        /// amber — so the fix for "that green is too pale to read" must not be "make it
        /// black". This keeps the hue and only moves lightness, which is the part that
        /// carries contrast. Measured across the 38 themes, the status words that needed
        /// it were sitting at 3.0–3.2:1 as 9pt text: visible, but exactly the kind of
        /// thing that is fine for the person who chose the palette and not for everyone
        /// else.
        ///
        /// ReadableOn is the last resort, for a hue that cannot reach the bar at all
        /// (a mid-grey on a mid-grey has nowhere to go).
        /// </summary>
        public static Color Readable(Color fg, Color bg, double need = 4.5)
        {
            if (ContrastRatio(fg, bg) >= need)
            {
                return fg;
            }

            // Away from the background: darken against a light one, lighten against dark.
            bool darken = RelativeLuminance(bg) > RelativeLuminance(fg);
            for (int i = 1; i <= 40; i++)
            {
                double amount = i * 0.025;
                Color c = darken ? Darken(fg, amount) : Lighten(fg, amount);
                if (ContrastRatio(c, bg) >= need)
                {
                    return c;
                }
            }
            return ReadableOn(bg);
        }

        /// <summary>
        /// Status colours adjusted to read as text on EVERY ground Tempo puts text on —
        /// the page, a card, and a nested panel.
        ///
        /// These used to be solved against <see cref="Background"/> alone, which is the
        /// wrong reference for most of the places they are actually used: warnings and
        /// notices nearly always sit inside a card, i.e. on <see cref="Surface"/>. The
        /// contrast scanner caught one at 4.01:1 on Gruvbox — passing the check the colour
        /// was designed for and failing the wall it was painted on.
        ///
        /// Solving against the worst single ground is not enough on its own: moving a hue
        /// away from one ground moves it toward another, so the candidate is tested
        /// against all three at each step. If no shade of the hue can satisfy all of them
        /// (a page and a card far apart in luminance), it falls back to the previous
        /// behaviour — readable on the page — rather than returning something worse.
        /// </summary>
        public Color SuccessText => ReadableAnywhere(Success);
        public Color WarningText => ReadableAnywhere(Warning);
        public Color DangerText => ReadableAnywhere(Danger);
        public Color AccentText => ReadableAnywhere(Accent);

        private Color ReadableAnywhere(Color fg, double need = 4.5)
        {
            if (ReadsEverywhere(fg, need))
            {
                return fg;
            }

            // Direction is chosen once, from the grounds' average lightness, so the search
            // does not oscillate between two grounds pulling opposite ways.
            double groundLum = (RelativeLuminance(Background)
                              + RelativeLuminance(Surface)
                              + RelativeLuminance(Surface2)) / 3.0;
            bool darken = groundLum > RelativeLuminance(fg);

            for (int i = 1; i <= 40; i++)
            {
                double amount = i * 0.025;
                Color c = darken ? Darken(fg, amount) : Lighten(fg, amount);
                if (ReadsEverywhere(c, need))
                {
                    return c;
                }
            }
            return Readable(fg, Background, need);
        }

        private bool ReadsEverywhere(Color fg, double need)
        {
            return ContrastRatio(fg, Background) >= need
                && ContrastRatio(fg, Surface) >= need
                && ContrastRatio(fg, Surface2) >= need;
        }

        private static Color Darken(Color c, double amount)
        {
            int r = (int)(c.R * (1 - amount));
            int g = (int)(c.G * (1 - amount));
            int b = (int)(c.B * (1 - amount));
            return Color.FromArgb(c.A,
                r < 0 ? 0 : r,
                g < 0 ? 0 : g,
                b < 0 ? 0 : b);
        }

        private static Color Lighten(Color c, double amount)
        {
            int r = (int)(c.R + (255 - c.R) * amount);
            int g = (int)(c.G + (255 - c.G) * amount);
            int b = (int)(c.B + (255 - c.B) * amount);
            return Color.FromArgb(c.A,
                r > 255 ? 255 : r,
                g > 255 ? 255 : g,
                b > 255 ? 255 : b);
        }

        /// <summary>
        /// How hard to push surfaces away from mid-grey when Windows HDR is tone-mapping
        /// Tempo's output. 0.35 is a per-channel gamma exponent, tuned to undo the
        /// "milky blacks" lift without turning a dark theme into a black hole.
        /// </summary>
        private const double HdrSurfaceGamma = 0.35;

        /// <summary>
        /// Set by the app when the user has HDR compensation switched on AND a display is
        /// actually tone-mapping. Static because every Theme is built through ForKind, and
        /// threading a flag through 38 switch arms would be worse than one gate here.
        /// </summary>
        public static bool CompensateForHdr { get; set; }

        public static Theme ForKind(ThemeKind kind)
        {
            Theme t = BaseForKind(kind);
            return CompensateForHdr ? t.HdrCompensated() : t;
        }

        /// <summary>
        /// Pre-distorts the palette so it survives Windows' SDR→HDR tone-mapping.
        ///
        /// Windows composites SDR content into the HDR signal against an SDR white level,
        /// and the visible result is that near-blacks LIFT: dark themes go milky, adjacent
        /// dark surfaces collapse into one another, and the whole window reads flat. Tempo
        /// cannot change that mapping — but it can hand the compositor a palette that lands
        /// where the theme intended after the mapping is applied.
        ///
        /// ONLY SURFACES MOVE. Text, muted text, the accent and the status colours are left
        /// exactly as designed, and that is a correctness property rather than a
        /// preference: all 38 themes are audited to WCAG AA against their own surfaces, so
        /// darkening a dark theme's background while holding its text fixed can only
        /// RAISE contrast. Nothing here can push a theme below the bar it already passes.
        ///
        /// Every surface takes the same gamma, so their relationships hold — a border stays
        /// a step away from the surface it separates instead of merging into it.
        /// </summary>
        private Theme HdrCompensated()
        {
            // Which way is "away from mid-grey"? Ask the theme, not the name: a light
            // theme is one whose TEXT is dark, and that holds for every custom accent and
            // every future theme without a list to maintain.
            bool darkTheme = Luminance(Text) > Luminance(Background);
            double g = darkTheme ? 1.0 + HdrSurfaceGamma : 1.0 / (1.0 + HdrSurfaceGamma);

            Background = Push(Background, g);
            Surface = Push(Surface, g);
            Surface2 = Push(Surface2, g);
            Border = Push(Border, g);
            InputBackground = Push(InputBackground, g);
            return this;
        }

        /// <summary>Per-channel gamma. Keeps hue, moves the colour toward black or white.</summary>
        private static Color Push(Color c, double gamma)
        {
            return Color.FromArgb(c.A, Chan(c.R, gamma), Chan(c.G, gamma), Chan(c.B, gamma));
        }

        private static int Chan(int v, double gamma)
        {
            double n = Math.Pow(v / 255.0, gamma);
            int outv = (int)Math.Round(n * 255.0);
            return outv < 0 ? 0 : (outv > 255 ? 255 : outv);
        }

        /// <summary>Relative luminance, the sRGB-weighted kind the contrast audit uses.</summary>
        private static double Luminance(Color c)
        {
            return 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
        }

        private static Theme BaseForKind(ThemeKind kind)
        {
            switch (kind)
            {
                case ThemeKind.Light:
                    return new Theme
                    {
                        Kind = ThemeKind.Light,
                        Background = Color.FromArgb(248, 249, 252),
                        Surface = Color.FromArgb(255, 255, 255),
                        Surface2 = Color.FromArgb(241, 243, 247),
                        Border = Color.FromArgb(220, 224, 232),
                        Accent = Color.FromArgb(79, 70, 229),
                        AccentHover = Color.FromArgb(99, 92, 240),
                        Success = Color.FromArgb(22, 163, 74),
                        Danger = Color.FromArgb(220, 38, 38),
                        Warning = Color.FromArgb(217, 119, 6),
                        Text = Color.FromArgb(17, 24, 39),
                        TextMuted = Color.FromArgb(107, 114, 128),
                        InputBackground = Color.FromArgb(255, 255, 255)
                    };

                case ThemeKind.Midnight:
                    return new Theme
                    {
                        Kind = ThemeKind.Midnight,
                        Background = Color.FromArgb(8, 11, 22),
                        Surface = Color.FromArgb(14, 19, 35),
                        Surface2 = Color.FromArgb(22, 28, 48),
                        Border = Color.FromArgb(34, 42, 70),
                        Accent = Color.FromArgb(56, 189, 248),
                        AccentHover = Color.FromArgb(80, 200, 255),
                        Success = Color.FromArgb(45, 212, 191),
                        Danger = Color.FromArgb(251, 113, 133),
                        Warning = Color.FromArgb(251, 191, 36),
                        Text = Color.FromArgb(232, 238, 248),
                        // 4% lighter than authored: the old value read 4.82:1 on
                        // Background but only 4.24:1 on the darker InputBackground, where
                        // eleven of the Settings hints actually sit.
                        TextMuted = Color.FromArgb(116, 131, 161),
                        InputBackground = Color.FromArgb(20, 26, 46)
                    };

                case ThemeKind.Ocean:
                    return new Theme
                    {
                        Kind = ThemeKind.Ocean,
                        Background = Color.FromArgb(11, 17, 32),
                        Surface = Color.FromArgb(17, 26, 46),
                        Surface2 = Color.FromArgb(26, 38, 64),
                        Border = Color.FromArgb(38, 54, 86),
                        Accent = Color.FromArgb(14, 165, 233),
                        AccentHover = Color.FromArgb(56, 189, 248),
                        Success = Color.FromArgb(16, 185, 129),
                        Danger = Color.FromArgb(244, 63, 94),
                        Warning = Color.FromArgb(245, 158, 11),
                        Text = Color.FromArgb(226, 235, 245),
                        TextMuted = Color.FromArgb(120, 140, 170),
                        InputBackground = Color.FromArgb(20, 30, 52)
                    };

                case ThemeKind.Forest:
                    return new Theme
                    {
                        Kind = ThemeKind.Forest,
                        Background = Color.FromArgb(12, 20, 15),
                        Surface = Color.FromArgb(18, 30, 22),
                        Surface2 = Color.FromArgb(28, 44, 33),
                        Border = Color.FromArgb(42, 64, 48),
                        Accent = Color.FromArgb(34, 197, 94),
                        AccentHover = Color.FromArgb(74, 222, 128),
                        Success = Color.FromArgb(132, 204, 22),
                        Danger = Color.FromArgb(248, 113, 113),
                        Warning = Color.FromArgb(250, 204, 21),
                        Text = Color.FromArgb(226, 240, 228),
                        TextMuted = Color.FromArgb(120, 150, 128),
                        InputBackground = Color.FromArgb(22, 36, 27)
                    };

                case ThemeKind.Crimson:
                    return new Theme
                    {
                        Kind = ThemeKind.Crimson,
                        Background = Color.FromArgb(22, 12, 14),
                        Surface = Color.FromArgb(33, 18, 22),
                        Surface2 = Color.FromArgb(48, 26, 32),
                        Border = Color.FromArgb(72, 38, 46),
                        Accent = Color.FromArgb(244, 63, 94),
                        AccentHover = Color.FromArgb(251, 113, 133),
                        Success = Color.FromArgb(52, 211, 153),
                        Danger = Color.FromArgb(239, 68, 68),
                        Warning = Color.FromArgb(251, 191, 36),
                        Text = Color.FromArgb(245, 230, 232),
                        TextMuted = Color.FromArgb(170, 128, 136),
                        InputBackground = Color.FromArgb(40, 22, 27)
                    };

                case ThemeKind.Solarized:
                    return new Theme
                    {
                        Kind = ThemeKind.Solarized,
                        Background = Color.FromArgb(253, 246, 227),
                        Surface = Color.FromArgb(255, 252, 242),
                        Surface2 = Color.FromArgb(238, 232, 213),
                        Border = Color.FromArgb(213, 205, 178),
                        Accent = Color.FromArgb(38, 139, 210),
                        AccentHover = Color.FromArgb(58, 159, 230),
                        Success = Color.FromArgb(132, 151, 0),
                        Danger = Color.FromArgb(220, 50, 47),
                        Warning = Color.FromArgb(179, 136, 0),
                        Text = Color.FromArgb(40, 54, 60),
                        TextMuted = Color.FromArgb(102, 115, 117),
                        InputBackground = Color.FromArgb(255, 252, 242)
                    };

                case ThemeKind.Amoled:
                    return new Theme
                    {
                        Kind = ThemeKind.Amoled,
                        Background = Color.FromArgb(0, 0, 0),
                        Surface = Color.FromArgb(12, 12, 14),
                        Surface2 = Color.FromArgb(22, 22, 26),
                        Border = Color.FromArgb(38, 38, 44),
                        Accent = Color.FromArgb(124, 92, 255),
                        AccentHover = Color.FromArgb(144, 116, 255),
                        Success = Color.FromArgb(56, 217, 169),
                        Danger = Color.FromArgb(248, 113, 113),
                        Warning = Color.FromArgb(251, 191, 36),
                        Text = Color.FromArgb(240, 240, 245),
                        // 2% lighter than authored; same reason as Midnight above.
                        TextMuted = Color.FromArgb(123, 123, 138),
                        InputBackground = Color.FromArgb(16, 16, 20)
                    };

                case ThemeKind.Nord:
                    return new Theme
                    {
                        Kind = ThemeKind.Nord,
                        Background = Color.FromArgb(46, 52, 64),
                        Surface = Color.FromArgb(59, 66, 82),
                        Surface2 = Color.FromArgb(67, 76, 94),
                        Border = Color.FromArgb(76, 86, 106),
                        Accent = Color.FromArgb(136, 192, 208),
                        AccentHover = Color.FromArgb(143, 188, 187),
                        Success = Color.FromArgb(163, 190, 140),
                        Danger = Color.FromArgb(191, 97, 106),
                        Warning = Color.FromArgb(235, 203, 139),
                        Text = Color.FromArgb(236, 239, 244),
                        TextMuted = Color.FromArgb(166, 174, 191),
                        InputBackground = Color.FromArgb(59, 66, 82)
                    };

                case ThemeKind.Monokai:
                    return new Theme
                    {
                        Kind = ThemeKind.Monokai,
                        Background = Color.FromArgb(39, 40, 34),
                        Surface = Color.FromArgb(49, 51, 44),
                        Surface2 = Color.FromArgb(62, 64, 55),
                        Border = Color.FromArgb(82, 85, 73),
                        Accent = Color.FromArgb(166, 226, 46),
                        AccentHover = Color.FromArgb(184, 240, 80),
                        Success = Color.FromArgb(166, 226, 46),
                        Danger = Color.FromArgb(249, 38, 114),
                        Warning = Color.FromArgb(253, 151, 31),
                        Text = Color.FromArgb(248, 248, 242),
                        TextMuted = Color.FromArgb(155, 155, 141),
                        InputBackground = Color.FromArgb(49, 51, 44)
                    };

                case ThemeKind.Gruvbox:
                    return new Theme
                    {
                        Kind = ThemeKind.Gruvbox,
                        Background = Color.FromArgb(40, 40, 40),
                        Surface = Color.FromArgb(50, 48, 47),
                        Surface2 = Color.FromArgb(60, 56, 54),
                        Border = Color.FromArgb(80, 73, 69),
                        Accent = Color.FromArgb(215, 153, 33),
                        AccentHover = Color.FromArgb(250, 189, 47),
                        Success = Color.FromArgb(152, 151, 26),
                        Danger = Color.FromArgb(209, 56, 49),
                        Warning = Color.FromArgb(214, 93, 14),
                        Text = Color.FromArgb(235, 219, 178),
                        TextMuted = Color.FromArgb(168, 153, 132),
                        InputBackground = Color.FromArgb(50, 48, 47)
                    };

                case ThemeKind.Synthwave:
                    return new Theme
                    {
                        Kind = ThemeKind.Synthwave,
                        Background = Color.FromArgb(26, 20, 41),
                        Surface = Color.FromArgb(38, 28, 58),
                        Surface2 = Color.FromArgb(52, 38, 78),
                        Border = Color.FromArgb(86, 58, 122),
                        Accent = Color.FromArgb(255, 45, 149),
                        AccentHover = Color.FromArgb(255, 92, 178),
                        Success = Color.FromArgb(54, 249, 246),
                        Danger = Color.FromArgb(255, 71, 87),
                        Warning = Color.FromArgb(255, 206, 84),
                        Text = Color.FromArgb(240, 230, 255),
                        TextMuted = Color.FromArgb(167, 139, 205),
                        InputBackground = Color.FromArgb(38, 28, 58)
                    };

                case ThemeKind.Coffee:
                    return new Theme
                    {
                        Kind = ThemeKind.Coffee,
                        Background = Color.FromArgb(38, 30, 24),
                        Surface = Color.FromArgb(50, 40, 32),
                        Surface2 = Color.FromArgb(64, 51, 41),
                        Border = Color.FromArgb(92, 74, 59),
                        Accent = Color.FromArgb(199, 145, 88),
                        AccentHover = Color.FromArgb(222, 170, 115),
                        Success = Color.FromArgb(150, 168, 110),
                        Danger = Color.FromArgb(206, 102, 86),
                        Warning = Color.FromArgb(224, 173, 92),
                        Text = Color.FromArgb(240, 228, 214),
                        TextMuted = Color.FromArgb(170, 150, 132),
                        InputBackground = Color.FromArgb(50, 40, 32)
                    };

                case ThemeKind.Dracula:
                    return new Theme
                    {
                        Kind = ThemeKind.Dracula,
                        Background = Color.FromArgb(40, 42, 54),
                        Surface = Color.FromArgb(50, 52, 67),
                        Surface2 = Color.FromArgb(68, 71, 90),
                        Border = Color.FromArgb(80, 84, 110),
                        Accent = Color.FromArgb(189, 147, 249),
                        AccentHover = Color.FromArgb(207, 170, 255),
                        Success = Color.FromArgb(80, 250, 123),
                        Danger = Color.FromArgb(255, 85, 85),
                        Warning = Color.FromArgb(241, 250, 140),
                        Text = Color.FromArgb(248, 248, 242),
                        TextMuted = Color.FromArgb(152, 156, 180),
                        InputBackground = Color.FromArgb(50, 52, 67)
                    };

                case ThemeKind.Cosmos:
                    // Deep-space nebula: dark indigo with a violet accent and a
                    // teal "success" glow (matches the Tempo cosmic artwork).
                    return new Theme
                    {
                        Kind = ThemeKind.Cosmos,
                        Background = Color.FromArgb(16, 14, 28),
                        Surface = Color.FromArgb(26, 22, 44),
                        Surface2 = Color.FromArgb(38, 32, 60),
                        Border = Color.FromArgb(64, 54, 96),
                        Accent = Color.FromArgb(167, 139, 250),
                        AccentHover = Color.FromArgb(190, 165, 255),
                        Success = Color.FromArgb(94, 234, 212),
                        Danger = Color.FromArgb(248, 113, 113),
                        Warning = Color.FromArgb(251, 191, 36),
                        Text = Color.FromArgb(236, 232, 248),
                        TextMuted = Color.FromArgb(160, 150, 190),
                        InputBackground = Color.FromArgb(26, 22, 44)
                    };

                case ThemeKind.Rose:
                    // Warm charcoal with a rose-pink accent.
                    return new Theme
                    {
                        Kind = ThemeKind.Rose,
                        Background = Color.FromArgb(28, 20, 24),
                        Surface = Color.FromArgb(40, 28, 34),
                        Surface2 = Color.FromArgb(54, 38, 46),
                        Border = Color.FromArgb(84, 58, 70),
                        Accent = Color.FromArgb(244, 114, 182),
                        AccentHover = Color.FromArgb(249, 150, 200),
                        Success = Color.FromArgb(134, 209, 150),
                        Danger = Color.FromArgb(239, 100, 97),
                        Warning = Color.FromArgb(240, 180, 100),
                        Text = Color.FromArgb(245, 230, 238),
                        TextMuted = Color.FromArgb(188, 158, 172),
                        InputBackground = Color.FromArgb(40, 28, 34)
                    };

                case ThemeKind.Slate:
                    // Cool, neutral blue-grey with a sky-blue accent.
                    return new Theme
                    {
                        Kind = ThemeKind.Slate,
                        Background = Color.FromArgb(24, 28, 35),
                        Surface = Color.FromArgb(33, 39, 49),
                        Surface2 = Color.FromArgb(44, 52, 64),
                        Border = Color.FromArgb(66, 78, 95),
                        Accent = Color.FromArgb(56, 189, 248),
                        AccentHover = Color.FromArgb(90, 205, 252),
                        Success = Color.FromArgb(74, 222, 128),
                        Danger = Color.FromArgb(248, 113, 113),
                        Warning = Color.FromArgb(251, 191, 36),
                        Text = Color.FromArgb(226, 232, 240),
                        TextMuted = Color.FromArgb(148, 163, 184),
                        InputBackground = Color.FromArgb(33, 39, 49)
                    };

                case ThemeKind.Sunset:
                    // Warm dusk: deep plum-brown with a sunset-orange accent.
                    return new Theme
                    {
                        Kind = ThemeKind.Sunset,
                        Background = Color.FromArgb(28, 22, 28),
                        Surface = Color.FromArgb(40, 30, 36),
                        Surface2 = Color.FromArgb(54, 40, 48),
                        Border = Color.FromArgb(86, 60, 70),
                        Accent = Color.FromArgb(251, 146, 60),
                        AccentHover = Color.FromArgb(253, 170, 100),
                        Success = Color.FromArgb(134, 209, 150),
                        Danger = Color.FromArgb(239, 100, 97),
                        Warning = Color.FromArgb(250, 200, 90),
                        Text = Color.FromArgb(245, 232, 228),
                        TextMuted = Color.FromArgb(186, 160, 158),
                        InputBackground = Color.FromArgb(40, 30, 36)
                    };

                case ThemeKind.Mint:
                    // Cool dark teal with a fresh mint-green accent.
                    return new Theme
                    {
                        Kind = ThemeKind.Mint,
                        Background = Color.FromArgb(14, 26, 24),
                        Surface = Color.FromArgb(22, 38, 35),
                        Surface2 = Color.FromArgb(32, 52, 48),
                        Border = Color.FromArgb(50, 80, 74),
                        Accent = Color.FromArgb(52, 211, 153),
                        AccentHover = Color.FromArgb(90, 225, 180),
                        Success = Color.FromArgb(110, 231, 183),
                        Danger = Color.FromArgb(248, 113, 113),
                        Warning = Color.FromArgb(251, 191, 36),
                        Text = Color.FromArgb(226, 242, 238),
                        TextMuted = Color.FromArgb(150, 180, 172),
                        InputBackground = Color.FromArgb(22, 38, 35)
                    };

                case ThemeKind.Sand:
                    // Warm light theme: cream paper with an amber accent.
                    return new Theme
                    {
                        Kind = ThemeKind.Sand,
                        Background = Color.FromArgb(245, 240, 232),
                        Surface = Color.FromArgb(252, 248, 240),
                        Surface2 = Color.FromArgb(236, 229, 217),
                        Border = Color.FromArgb(214, 204, 188),
                        Accent = Color.FromArgb(186, 127, 4),
                        AccentHover = Color.FromArgb(176, 118, 0),
                        Success = Color.FromArgb(95, 153, 12),
                        Danger = Color.FromArgb(200, 50, 40),
                        Warning = Color.FromArgb(186, 127, 4),
                        Text = Color.FromArgb(58, 48, 36),
                        TextMuted = Color.FromArgb(120, 107, 90),
                        InputBackground = Color.FromArgb(252, 248, 240)
                    };

                case ThemeKind.Lavender:
                    // Soft dark theme with a calm lavender accent.
                    return new Theme
                    {
                        Kind = ThemeKind.Lavender,
                        Background = Color.FromArgb(22, 20, 30),
                        Surface = Color.FromArgb(30, 27, 42),
                        Surface2 = Color.FromArgb(42, 37, 58),
                        Border = Color.FromArgb(60, 52, 82),
                        Accent = Color.FromArgb(167, 139, 250),
                        AccentHover = Color.FromArgb(196, 181, 253),
                        Success = Color.FromArgb(52, 211, 153),
                        Danger = Color.FromArgb(248, 113, 113),
                        Warning = Color.FromArgb(251, 191, 36),
                        Text = Color.FromArgb(235, 232, 245),
                        TextMuted = Color.FromArgb(150, 142, 170),
                        InputBackground = Color.FromArgb(36, 32, 50)
                    };

                case ThemeKind.Sakura:
                    // Light theme: cherry-blossom pink on soft paper.
                    return new Theme
                    {
                        Kind = ThemeKind.Sakura,
                        Background = Color.FromArgb(252, 242, 245),
                        Surface = Color.FromArgb(255, 250, 252),
                        Surface2 = Color.FromArgb(250, 232, 238),
                        Border = Color.FromArgb(238, 210, 222),
                        Accent = Color.FromArgb(219, 68, 134),
                        AccentHover = Color.FromArgb(190, 48, 114),
                        Success = Color.FromArgb(97, 156, 12),
                        Danger = Color.FromArgb(200, 50, 60),
                        Warning = Color.FromArgb(190, 130, 4),
                        Text = Color.FromArgb(60, 40, 50),
                        TextMuted = Color.FromArgb(138, 101, 118),
                        InputBackground = Color.FromArgb(255, 250, 252)
                    };

                case ThemeKind.Emerald:
                    // Deep green dark theme with an emerald accent.
                    return new Theme
                    {
                        Kind = ThemeKind.Emerald,
                        Background = Color.FromArgb(8, 22, 20),
                        Surface = Color.FromArgb(13, 32, 29),
                        Surface2 = Color.FromArgb(20, 46, 42),
                        Border = Color.FromArgb(30, 66, 60),
                        Accent = Color.FromArgb(16, 185, 129),
                        AccentHover = Color.FromArgb(52, 211, 153),
                        Success = Color.FromArgb(132, 204, 22),
                        Danger = Color.FromArgb(244, 63, 94),
                        Warning = Color.FromArgb(245, 158, 11),
                        Text = Color.FromArgb(220, 238, 232),
                        TextMuted = Color.FromArgb(110, 150, 140),
                        InputBackground = Color.FromArgb(14, 36, 32)
                    };

                case ThemeKind.Steel:
                    // Cool blue-grey industrial dark theme.
                    return new Theme
                    {
                        Kind = ThemeKind.Steel,
                        Background = Color.FromArgb(18, 22, 28),
                        Surface = Color.FromArgb(26, 32, 40),
                        Surface2 = Color.FromArgb(38, 46, 57),
                        Border = Color.FromArgb(56, 66, 80),
                        Accent = Color.FromArgb(96, 165, 200),
                        AccentHover = Color.FromArgb(130, 190, 220),
                        Success = Color.FromArgb(56, 217, 169),
                        Danger = Color.FromArgb(248, 113, 113),
                        Warning = Color.FromArgb(251, 191, 36),
                        Text = Color.FromArgb(226, 232, 240),
                        TextMuted = Color.FromArgb(132, 146, 166),
                        InputBackground = Color.FromArgb(32, 40, 50)
                    };

                case ThemeKind.Grape:
                    // Rich purple background with a vivid fuchsia accent.
                    return new Theme
                    {
                        Kind = ThemeKind.Grape,
                        Background = Color.FromArgb(26, 14, 34),
                        Surface = Color.FromArgb(38, 20, 50),
                        Surface2 = Color.FromArgb(54, 30, 70),
                        Border = Color.FromArgb(78, 44, 100),
                        Accent = Color.FromArgb(217, 70, 239),
                        AccentHover = Color.FromArgb(232, 121, 249),
                        Success = Color.FromArgb(52, 211, 153),
                        Danger = Color.FromArgb(251, 113, 133),
                        Warning = Color.FromArgb(251, 191, 36),
                        Text = Color.FromArgb(240, 230, 248),
                        TextMuted = Color.FromArgb(168, 140, 186),
                        InputBackground = Color.FromArgb(44, 24, 58)
                    };

                case ThemeKind.Arctic:
                    // Crisp light theme with an icy cyan accent.
                    return new Theme
                    {
                        Kind = ThemeKind.Arctic,
                        Background = Color.FromArgb(238, 246, 250),
                        Surface = Color.FromArgb(248, 252, 254),
                        Surface2 = Color.FromArgb(226, 238, 246),
                        Border = Color.FromArgb(200, 218, 230),
                        Accent = Color.FromArgb(6, 150, 180),
                        AccentHover = Color.FromArgb(8, 130, 158),
                        Success = Color.FromArgb(13, 148, 90),
                        Danger = Color.FromArgb(200, 50, 50),
                        Warning = Color.FromArgb(196, 127, 4),
                        Text = Color.FromArgb(28, 44, 54),
                        TextMuted = Color.FromArgb(92, 115, 131),
                        InputBackground = Color.FromArgb(248, 252, 254)
                    };

                case ThemeKind.Indigo:
                    // Deep indigo dark theme with a blue-violet accent.
                    return new Theme
                    {
                        Kind = ThemeKind.Indigo,
                        Background = Color.FromArgb(15, 16, 32),
                        Surface = Color.FromArgb(22, 24, 46),
                        Surface2 = Color.FromArgb(32, 35, 64),
                        Border = Color.FromArgb(48, 52, 92),
                        Accent = Color.FromArgb(99, 102, 241),
                        AccentHover = Color.FromArgb(129, 140, 248),
                        Success = Color.FromArgb(52, 211, 153),
                        Danger = Color.FromArgb(248, 113, 113),
                        Warning = Color.FromArgb(251, 191, 36),
                        Text = Color.FromArgb(228, 230, 248),
                        TextMuted = Color.FromArgb(138, 144, 184),
                        InputBackground = Color.FromArgb(26, 28, 52)
                    };

                case ThemeKind.Teal:
                    // Dark theme with a bright teal accent.
                    return new Theme
                    {
                        Kind = ThemeKind.Teal,
                        Background = Color.FromArgb(10, 24, 26),
                        Surface = Color.FromArgb(15, 34, 37),
                        Surface2 = Color.FromArgb(22, 48, 52),
                        Border = Color.FromArgb(32, 68, 73),
                        Accent = Color.FromArgb(20, 184, 166),
                        AccentHover = Color.FromArgb(45, 212, 191),
                        Success = Color.FromArgb(132, 204, 22),
                        Danger = Color.FromArgb(244, 63, 94),
                        Warning = Color.FromArgb(245, 158, 11),
                        Text = Color.FromArgb(220, 238, 236),
                        TextMuted = Color.FromArgb(112, 152, 150),
                        InputBackground = Color.FromArgb(16, 38, 40)
                    };

                case ThemeKind.Tangerine:
                    // Near-black charcoal with a vivid tangerine accent.
                    return new Theme
                    {
                        Kind = ThemeKind.Tangerine,
                        Background = Color.FromArgb(20, 16, 12),
                        Surface = Color.FromArgb(30, 24, 18),
                        Surface2 = Color.FromArgb(44, 34, 24),
                        Border = Color.FromArgb(66, 50, 34),
                        Accent = Color.FromArgb(249, 115, 22),
                        AccentHover = Color.FromArgb(251, 146, 60),
                        Success = Color.FromArgb(132, 204, 22),
                        Danger = Color.FromArgb(248, 113, 113),
                        Warning = Color.FromArgb(250, 204, 21),
                        Text = Color.FromArgb(244, 234, 224),
                        TextMuted = Color.FromArgb(168, 146, 124),
                        InputBackground = Color.FromArgb(34, 27, 20)
                    };

                case ThemeKind.Bubblegum:
                    // Playful light theme: pink accent on a cool-white surface.
                    return new Theme
                    {
                        Kind = ThemeKind.Bubblegum,
                        Background = Color.FromArgb(250, 244, 250),
                        Surface = Color.FromArgb(255, 251, 255),
                        Surface2 = Color.FromArgb(245, 234, 246),
                        Border = Color.FromArgb(232, 212, 234),
                        Accent = Color.FromArgb(236, 72, 153),
                        AccentHover = Color.FromArgb(219, 39, 119),
                        Success = Color.FromArgb(13, 148, 90),
                        Danger = Color.FromArgb(220, 38, 38),
                        Warning = Color.FromArgb(213, 117, 6),
                        Text = Color.FromArgb(60, 36, 54),
                        TextMuted = Color.FromArgb(134, 103, 125),
                        InputBackground = Color.FromArgb(255, 251, 255)
                    };

                case ThemeKind.Carbon:
                    // Minimal monochrome dark theme — neutral greys, near-white accent.
                    return new Theme
                    {
                        Kind = ThemeKind.Carbon,
                        Background = Color.FromArgb(18, 18, 19),
                        Surface = Color.FromArgb(26, 26, 28),
                        Surface2 = Color.FromArgb(38, 38, 41),
                        Border = Color.FromArgb(56, 56, 60),
                        Accent = Color.FromArgb(212, 212, 216),
                        AccentHover = Color.FromArgb(244, 244, 248),
                        Success = Color.FromArgb(74, 222, 128),
                        Danger = Color.FromArgb(248, 113, 113),
                        Warning = Color.FromArgb(250, 204, 21),
                        Text = Color.FromArgb(236, 236, 240),
                        TextMuted = Color.FromArgb(140, 140, 148),
                        InputBackground = Color.FromArgb(32, 32, 35)
                    };

                case ThemeKind.Honey:
                    // Warm dark theme with a golden amber accent.
                    return new Theme
                    {
                        Kind = ThemeKind.Honey,
                        Background = Color.FromArgb(24, 20, 12),
                        Surface = Color.FromArgb(34, 28, 16),
                        Surface2 = Color.FromArgb(48, 40, 22),
                        Border = Color.FromArgb(72, 58, 30),
                        Accent = Color.FromArgb(245, 185, 38),
                        AccentHover = Color.FromArgb(250, 204, 80),
                        Success = Color.FromArgb(132, 204, 22),
                        Danger = Color.FromArgb(248, 113, 113),
                        Warning = Color.FromArgb(245, 158, 11),
                        Text = Color.FromArgb(245, 238, 222),
                        TextMuted = Color.FromArgb(170, 152, 116),
                        InputBackground = Color.FromArgb(38, 31, 18)
                    };

                case ThemeKind.Sapphire:
                    // Rich royal-blue dark theme.
                    return new Theme
                    {
                        Kind = ThemeKind.Sapphire,
                        Background = Color.FromArgb(10, 18, 38),
                        Surface = Color.FromArgb(16, 26, 52),
                        Surface2 = Color.FromArgb(24, 38, 72),
                        Border = Color.FromArgb(36, 54, 96),
                        Accent = Color.FromArgb(56, 132, 255),
                        AccentHover = Color.FromArgb(96, 162, 255),
                        Success = Color.FromArgb(52, 211, 153),
                        Danger = Color.FromArgb(248, 113, 113),
                        Warning = Color.FromArgb(251, 191, 36),
                        Text = Color.FromArgb(224, 232, 248),
                        TextMuted = Color.FromArgb(132, 148, 184),
                        InputBackground = Color.FromArgb(18, 29, 56)
                    };

                case ThemeKind.Olive:
                    // Warm military-green / khaki dark theme.
                    return new Theme
                    {
                        Kind = ThemeKind.Olive,
                        Background = Color.FromArgb(22, 22, 14),
                        Surface = Color.FromArgb(31, 31, 20),
                        Surface2 = Color.FromArgb(44, 44, 28),
                        Border = Color.FromArgb(66, 66, 40),
                        Accent = Color.FromArgb(160, 168, 58),
                        AccentHover = Color.FromArgb(190, 198, 84),
                        Success = Color.FromArgb(132, 204, 22),
                        Danger = Color.FromArgb(232, 110, 90),
                        Warning = Color.FromArgb(234, 179, 8),
                        Text = Color.FromArgb(236, 238, 220),
                        TextMuted = Color.FromArgb(156, 158, 124),
                        InputBackground = Color.FromArgb(34, 34, 22)
                    };

                case ThemeKind.Cyan:
                    // Near-black with a vivid cyan accent.
                    return new Theme
                    {
                        Kind = ThemeKind.Cyan,
                        Background = Color.FromArgb(8, 16, 18),
                        Surface = Color.FromArgb(13, 24, 27),
                        Surface2 = Color.FromArgb(20, 36, 40),
                        Border = Color.FromArgb(30, 54, 60),
                        Accent = Color.FromArgb(34, 211, 238),
                        AccentHover = Color.FromArgb(103, 232, 249),
                        Success = Color.FromArgb(74, 222, 128),
                        Danger = Color.FromArgb(248, 113, 113),
                        Warning = Color.FromArgb(250, 204, 21),
                        Text = Color.FromArgb(220, 238, 240),
                        TextMuted = Color.FromArgb(118, 150, 156),
                        InputBackground = Color.FromArgb(14, 27, 30)
                    };

                case ThemeKind.Peach:
                    // Soft, light warm theme with a coral accent.
                    return new Theme
                    {
                        Kind = ThemeKind.Peach,
                        Background = Color.FromArgb(253, 245, 240),
                        Surface = Color.FromArgb(255, 250, 246),
                        Surface2 = Color.FromArgb(250, 238, 230),
                        Border = Color.FromArgb(238, 220, 208),
                        Accent = Color.FromArgb(227, 106, 87),
                        AccentHover = Color.FromArgb(234, 88, 66),
                        Success = Color.FromArgb(22, 150, 110),
                        Danger = Color.FromArgb(220, 38, 38),
                        Warning = Color.FromArgb(192, 131, 4),
                        Text = Color.FromArgb(74, 52, 44),
                        TextMuted = Color.FromArgb(134, 108, 97),
                        InputBackground = Color.FromArgb(255, 250, 246)
                    };

                case ThemeKind.Wine:
                    // Deep burgundy dark theme.
                    return new Theme
                    {
                        Kind = ThemeKind.Wine,
                        Background = Color.FromArgb(26, 12, 16),
                        Surface = Color.FromArgb(38, 18, 24),
                        Surface2 = Color.FromArgb(54, 26, 34),
                        Border = Color.FromArgb(80, 40, 50),
                        Accent = Color.FromArgb(190, 58, 84),
                        AccentHover = Color.FromArgb(214, 86, 112),
                        Success = Color.FromArgb(132, 204, 22),
                        Danger = Color.FromArgb(248, 113, 113),
                        Warning = Color.FromArgb(245, 158, 11),
                        Text = Color.FromArgb(244, 226, 230),
                        TextMuted = Color.FromArgb(172, 134, 142),
                        InputBackground = Color.FromArgb(42, 20, 27)
                    };

                case ThemeKind.Magenta:
                    // Near-black with a vivid magenta accent.
                    return new Theme
                    {
                        Kind = ThemeKind.Magenta,
                        Background = Color.FromArgb(20, 12, 22),
                        Surface = Color.FromArgb(30, 18, 33),
                        Surface2 = Color.FromArgb(44, 27, 48),
                        Border = Color.FromArgb(68, 42, 74),
                        Accent = Color.FromArgb(217, 70, 199),
                        AccentHover = Color.FromArgb(232, 110, 218),
                        Success = Color.FromArgb(74, 222, 128),
                        Danger = Color.FromArgb(248, 113, 113),
                        Warning = Color.FromArgb(250, 204, 21),
                        Text = Color.FromArgb(238, 226, 242),
                        TextMuted = Color.FromArgb(160, 134, 168),
                        InputBackground = Color.FromArgb(33, 20, 36)
                    };

                case ThemeKind.Dark:
                default:
                    // Slightly cooler dark palette with more contrast between
                    // surface levels and a sharper accent.
                    return new Theme
                    {
                        Kind = ThemeKind.Dark,
                        Background = Color.FromArgb(16, 18, 27),
                        Surface = Color.FromArgb(24, 27, 39),
                        Surface2 = Color.FromArgb(34, 38, 54),
                        Border = Color.FromArgb(48, 54, 74),
                        Accent = Color.FromArgb(124, 92, 255),
                        AccentHover = Color.FromArgb(144, 116, 255),
                        Success = Color.FromArgb(56, 217, 169),
                        Danger = Color.FromArgb(248, 113, 113),
                        Warning = Color.FromArgb(251, 191, 36),
                        Text = Color.FromArgb(232, 236, 246),
                        TextMuted = Color.FromArgb(132, 142, 168),
                        InputBackground = Color.FromArgb(32, 36, 52)
                    };
            }
        }
    }
}
