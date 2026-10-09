using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A small rounded badge holding one of the card glyphs (<see cref="CardIcon"/>), for the start
    /// of a line of text — the same mark a card's title carries, so an icon means one thing
    /// everywhere. Purely decorative: it takes no focus and screen readers skip it.
    ///
    /// Painted from the theme it is asked for at paint time (<see cref="ThemeSource"/>), so a theme
    /// change needs nothing but a repaint — no role to record and put back after a theming pass.
    /// </summary>
    internal sealed class IconChip : Control
    {
        public CardIcon Icon { get; set; } = CardIcon.None;

        /// <summary>The current theme, read at every paint.</summary>
        public Func<Theme> ThemeSource { get; set; }

        /// <summary>The glyph's hue in a theme; the accent when not set.</summary>
        public Func<Theme, Color> Hue { get; set; }

        public IconChip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            AccessibleRole = AccessibleRole.Graphic;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme theme = ThemeSource?.Invoke();
            if (theme == null) { return; }
            Graphics g = e.Graphics;
            Color under = Parent != null ? Parent.BackColor : theme.Surface;
            if (under.A < 255) { under = theme.Surface; }
            g.Clear(under);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color hue = Hue != null ? Hue(theme) : theme.Accent;
            Color fill = Theme.Blend(under, hue, 0.22);
            var chip = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Math.Max(4, Width / 3);
            using (var path = Rounded(chip, radius))
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
            }
            CardGroupBox.DrawGlyph(g, Icon, chip, Theme.Readable(hue, fill, 3.0));
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>
    /// A rounded, tinted box for a notice that must not be skimmed past — a warning before a risky
    /// switch, say — holding ordinary labels so they wrap, translate and pass the layout checks like
    /// any other text.
    ///
    /// The tint is the notice's tone (<see cref="Tone"/>, the theme's warning colour by default)
    /// mixed into the card it sits on, and it reports that colour through
    /// <see cref="IThemedSurface"/>, so <see cref="ThemeManager"/> gives the labels on it the tint
    /// rather than the card's colour. Painted from the theme read at paint time.
    /// </summary>
    internal sealed class InfoCallout : Panel, IThemedSurface
    {
        /// <summary>The current theme, read at every paint.</summary>
        public Func<Theme> ThemeSource { get; set; }

        /// <summary>The notice's tone in a theme; the warning colour when not set.</summary>
        public Func<Theme, Color> Tone { get; set; }

        public InfoCallout()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        /// <summary>The box's fill in <paramref name="theme"/>: the tone, faintly, over the card's surface.</summary>
        public Color SurfaceFor(Theme theme)
        {
            if (theme == null) { return BackColor; }
            return Theme.Blend(theme.Surface, ToneIn(theme), 0.12);
        }

        /// <summary>Ink for text on the box: the tone, darkened or lightened until it reads (4.5:1).</summary>
        public Color InkFor(Theme theme)
        {
            return Theme.Readable(ToneIn(theme), SurfaceFor(theme), 4.5);
        }

        private Color ToneIn(Theme theme) => Tone != null ? Tone(theme) : theme.WarningText;

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The corners outside the rounded box show what the box sits on.
            Theme theme = ThemeSource?.Invoke();
            Color under = Parent != null ? Parent.BackColor : (theme != null ? theme.Surface : BackColor);
            if (under.A < 255 && theme != null) { under = theme.Surface; }
            e.Graphics.Clear(under);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme theme = ThemeSource?.Invoke();
            if (theme == null) { return; }
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Math.Max(6, (int)Math.Round(10 * DeviceDpi / 96f));
            Color fill = SurfaceFor(theme);
            Color edge = Theme.Blend(theme.Surface, ToneIn(theme), 0.45);
            using (var path = RoundedBox(box, radius))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(edge, 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            // A slim bar of the tone down the left edge: the box reads as a notice at a glance.
            int bar = Math.Max(3, (int)Math.Round(3 * DeviceDpi / 96f));
            using (var path = RoundedBox(new Rectangle(0, 0, bar * 2, Height - 1), radius))
            using (var clip = RoundedBox(box, radius))
            using (var brush = new SolidBrush(InkFor(theme)))
            {
                Region old = g.Clip;
                g.SetClip(clip);
                g.FillRectangle(brush, 0, 0, bar, Height);
                g.Clip = old;
            }
        }

        private static GraphicsPath RoundedBox(Rectangle r, int radius)
        {
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
