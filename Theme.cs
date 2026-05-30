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

        public static Theme ForKind(ThemeKind kind)
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
                        TextMuted = Color.FromArgb(102, 118, 152),
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
                        Success = Color.FromArgb(133, 153, 0),
                        Danger = Color.FromArgb(220, 50, 47),
                        Warning = Color.FromArgb(181, 137, 0),
                        Text = Color.FromArgb(40, 54, 60),
                        TextMuted = Color.FromArgb(131, 148, 150),
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
                        TextMuted = Color.FromArgb(120, 120, 135),
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
                        TextMuted = Color.FromArgb(150, 160, 180),
                        InputBackground = Color.FromArgb(59, 66, 82)
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
                        TextMuted = Color.FromArgb(145, 150, 175),
                        InputBackground = Color.FromArgb(50, 52, 67)
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
