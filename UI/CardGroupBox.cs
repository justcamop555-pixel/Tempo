using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A <see cref="GroupBox"/> that paints itself as a modern rounded "card": a
    /// filled surface with a soft rounded border and a clean title preceded by a
    /// small accent tab.
    ///
    /// Crucially it keeps exactly the same client area and child-coordinate origin as
    /// a normal GroupBox — only <see cref="OnPaint"/> and the control region change —
    /// so every existing child control stays exactly where it was. This lets the whole
    /// app pick up the new look without re-laying-out a single tab.
    /// </summary>
    public enum CardIcon
    {
        None, Clock, Cursor, Target, Repeat, Bolt, Wave, Gauge, Shield,
        List, Play, Chart, Keyboard, Gear, Caption, Folder, Star, Pin, People,
        Key, Lock
    }

    public sealed class CardGroupBox : GroupBox, IPaintedText
    {
        private Color _surface = Color.FromArgb(37, 37, 38);
        private Color _border = Color.FromArgb(64, 64, 64);
        private Color _title = Color.Gainsboro;
        private Color _accent = Color.DodgerBlue;
        private const int Radius = 14;

        /// <summary>Optional glyph drawn before the title, in the accent colour.</summary>
        public CardIcon Icon { get; set; } = CardIcon.None;

        public CardGroupBox()
        {
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = _surface;
        }

        public void ApplyTheme(Theme theme)
        {
            if (theme == null)
            {
                return;
            }
            _surface = theme.Surface;
            _border = theme.Border;
            _title = theme.Text;
            _accent = theme.Accent;
            BackColor = theme.Surface; // so transparent child labels blend with the card
            UpdateRegion();
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateRegion();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateRegion();
        }

        // Clip to a rounded shape so the corners reveal the page behind the card.
        private void UpdateRegion()
        {
            if (Width <= 1 || Height <= 1)
            {
                return;
            }
            using (var path = Rounded(new Rectangle(0, 0, Width, Height), Radius))
            {
                Region = new Region(path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);

            using (var path = Rounded(rect, Radius))
            using (var fill = new SolidBrush(_surface))
            {
                g.FillPath(fill, path);
            }

            using (var path = Rounded(rect, Radius))
            using (var pen = new Pen(_border, 1))
            {
                g.DrawPath(pen, path);
            }

            // Title: drawn UPPERCASE with light letter-spacing (tracking) for a clean
            // "section header" look, preceded by either a thin accent bar or a rounded
            // icon badge. (Undo the "&&" mnemonic escaping GroupBox text uses.)
            string title = TitleText;
            if (title.Length > 0)
            {
                bool badge = TitleGeometry(g, out Rectangle mark, out PointF at);
                if (!badge)
                {
                    using (var barPath = Rounded(mark, 1))
                    using (var accentBar = new SolidBrush(_accent))
                    {
                        g.FillPath(accentBar, barPath);
                    }
                }
                else
                {
                    // A rounded icon badge with a tinted accent fill, then the title.
                    using (var chipPath = Rounded(mark, 7))
                    using (var chipFill = new SolidBrush(Blend(_accent, _surface, 0.28f)))
                    {
                        g.FillPath(chipFill, chipPath);
                    }
                    DrawGlyph(g, Icon, mark, _accent);
                }
                using (var tb = new SolidBrush(_title))
                {
                    DrawTracked(g, title, Font, tb, at.X, at.Y, TitleTracking);
                }
            }
        }

        private const float TitleTracking = 0.9f;

        // The title's painted width, kept until the title or the font changes: measured a letter at a
        // time, and asked on every paint.
        private string _titleWidthFor;
        private Font _titleWidthFont;
        private float _titleWidth;

        private float TitleTextWidth(Graphics g)
        {
            string title = TitleText;
            if (!ReferenceEquals(_titleWidthFont, Font) || !string.Equals(_titleWidthFor, title, StringComparison.Ordinal))
            {
                _titleWidth = DrawTracked(g, title, Font, null, 0f, 0f, TitleTracking) - TitleTracking;
                _titleWidthFor = title;
                _titleWidthFont = Font;
            }
            return _titleWidth;
        }

        /// <summary>The title as painted: upper-case, with GroupBox's "&amp;&amp;" escaping undone.</summary>
        private string TitleText => (Text ?? string.Empty).Replace("&&", "&").ToUpperInvariant();

        /// <summary>
        /// The accent bar or icon badge before the title, and where the title's text starts. True when
        /// it is the badge.
        ///
        /// The badge is decoration and the title is the meaning: when the two do not both fit the card,
        /// the title keeps the room and the plain accent bar stands in — as a button leaves its icon
        /// off for its caption. German "VERWALTEN" ran into the border of the 124 px Manage card.
        /// </summary>
        private bool TitleGeometry(Graphics g, out Rectangle mark, out PointF text, bool? withBadge = null)
        {
            float th = Font.GetHeight(g);
            // Painting, the card is its real size in pixels; the layout check can decide for itself in
            // the window's units (see PaintedTexts) and say so here.
            bool badge = withBadge ?? (Icon != CardIcon.None && 43f + TitleTextWidth(g) <= Width - 8);
            if (!badge)
            {
                mark = new Rectangle(12, 5, 3, 14);
                text = new PointF(22f, 5f + (14f - th) / 2f);
            }
            else
            {
                mark = new Rectangle(12, 4, 22, 22);
                text = new PointF(mark.Right + 9f, mark.Top + (mark.Height - th) / 2f);
            }
            return badge;
        }

        /// <summary>
        /// The title row — badge or bar, then the letter-spaced words — for the layout checks. Nothing
        /// clips the title short of the card's own edge, so a long one runs into the rounded corner and
        /// off the card; the room ends a little inside the border.
        /// </summary>
        IEnumerable<PaintedText> IPaintedText.PaintedTexts(Graphics g, float unitsPerPixel)
        {
            string title = TitleText;
            if (title.Length == 0 || g == null) { yield break; }
            float u = unitsPerPixel > 0f ? unitsPerPixel : 1f;
            // The badge (or bar) and the title are painted in pixels; the card's width is a layout number.
            int badgeRoom = (int)Math.Ceiling((43f + TitleTextWidth(g)) * u);
            bool badge = Icon != CardIcon.None && badgeRoom <= Width - (int)Math.Ceiling(8 * u);
            TitleGeometry(g, out Rectangle mark, out PointF at, badge);
            float th = Font.GetHeight(g);
            float right = at.X + TitleTextWidth(g);
            var ink = Rectangle.FromLTRB((int)(mark.Left * u), (int)Math.Floor(Math.Min(mark.Top, at.Y) * u),
                                         (int)Math.Ceiling(right * u), (int)Math.Ceiling(Math.Max(mark.Bottom, at.Y + th) * u));
            var room = Rectangle.FromLTRB(ink.Left, 0, Math.Max(ink.Left, Width - (int)Math.Ceiling(8 * u)), ink.Bottom + 2);
            yield return new PaintedText(title, ink, room);
        }

        // Draws text with a little extra space between letters (tracking), used for the
        // uppercase card titles. Returns the x just past the last glyph. With no brush it only
        // measures, by the same steps.
        private static float DrawTracked(Graphics g, string text, Font font, Brush brush, float x, float y, float tracking)
        {
            StringFormat sf = StringFormat.GenericTypographic; // shared static — do not dispose
            // GenericTypographic measures a lone/trailing space as ~0 width, so derive a
            // real space advance from the difference of "x x" and "xx".
            float spaceW = g.MeasureString("x x", font, PointF.Empty, sf).Width
                         - g.MeasureString("xx", font, PointF.Empty, sf).Width;
            foreach (char ch in text)
            {
                if (ch == ' ')
                {
                    x += spaceW + tracking;
                    continue;
                }
                string s = ch.ToString();
                if (brush != null) { g.DrawString(s, font, brush, x, y, sf); }
                x += g.MeasureString(s, font, PointF.Empty, sf).Width + tracking;
            }
            return x;
        }

        private static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R * t + b.R * (1 - t)),
                (int)(a.G * t + b.G * (1 - t)),
                (int)(a.B * t + b.B * (1 - t)));
        }

        // Minimal, crisp line glyphs drawn inside the given chip rectangle. Shared with IconChip, so
        // a glyph means the same thing on a card's title and beside a line of text. The padding and
        // the stroke follow the chip's size (5 px and 1.6 px on the title's 22 px chip).
        internal static void DrawGlyph(Graphics g, CardIcon icon, Rectangle chip, Color color)
        {
            var prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Inner drawing area, padded inside the chip.
            int inset = Math.Max(5, (int)Math.Round(chip.Width * 5 / 22f));
            var r = Rectangle.Inflate(chip, -inset, -inset);
            using (var pen = new Pen(color, Math.Max(1.6f, chip.Width * 1.6f / 22f)))
            using (var br = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                int cx = r.Left + r.Width / 2;
                int cy = r.Top + r.Height / 2;
                switch (icon)
                {
                    case CardIcon.Clock:
                        g.DrawEllipse(pen, r);
                        g.DrawLine(pen, cx, cy, cx, r.Top + 2);
                        g.DrawLine(pen, cx, cy, r.Right - 3, cy);
                        break;
                    case CardIcon.Cursor:
                        g.DrawLines(pen, new[] {
                            new Point(r.Left+1, r.Top), new Point(r.Left+1, r.Bottom),
                            new Point(cx, cy+1), new Point(r.Right-1, r.Bottom-1) });
                        break;
                    case CardIcon.Target:
                        g.DrawEllipse(pen, r);
                        g.DrawEllipse(pen, Rectangle.Inflate(r, -3, -3));
                        g.FillEllipse(br, cx - 1, cy - 1, 2, 2);
                        break;
                    case CardIcon.Repeat:
                        g.DrawArc(pen, r, 30, 300);
                        g.DrawLine(pen, r.Right - 2, r.Top + 1, r.Right - 1, r.Top + 5);
                        g.DrawLine(pen, r.Right - 2, r.Top + 1, r.Right - 6, r.Top + 2);
                        break;
                    case CardIcon.Bolt:
                        g.DrawLines(pen, new[] {
                            new Point(cx+1, r.Top), new Point(r.Left+1, cy+1),
                            new Point(cx, cy+1), new Point(cx-1, r.Bottom),
                            new Point(r.Right-1, cy-1), new Point(cx, cy-1) });
                        break;
                    case CardIcon.Wave:
                        g.DrawCurve(pen, new[] {
                            new Point(r.Left, cy), new Point(r.Left+r.Width/4, r.Top),
                            new Point(cx, cy), new Point(r.Right-r.Width/4, r.Bottom),
                            new Point(r.Right, cy) });
                        break;
                    case CardIcon.Gauge:
                        g.DrawArc(pen, r.Left, r.Top, r.Width, r.Height, 180, 180);
                        g.DrawLine(pen, cx, cy, r.Right - 3, r.Top + 3);
                        break;
                    case CardIcon.Shield:
                        g.DrawLines(pen, new[] {
                            new Point(cx, r.Top), new Point(r.Right, r.Top+3),
                            new Point(cx, r.Bottom), new Point(r.Left, r.Top+3),
                            new Point(cx, r.Top) });
                        break;
                    case CardIcon.List:
                        for (int i = 0; i < 3; i++)
                            g.DrawLine(pen, r.Left, r.Top + i * (r.Height / 2), r.Right, r.Top + i * (r.Height / 2));
                        break;
                    case CardIcon.Play:
                        g.FillPolygon(br, new[] {
                            new Point(r.Left+1, r.Top), new Point(r.Right, cy), new Point(r.Left+1, r.Bottom) });
                        break;
                    case CardIcon.Chart:
                        g.DrawLine(pen, r.Left, r.Bottom, r.Right, r.Bottom);
                        g.DrawLine(pen, r.Left, r.Bottom, r.Left, r.Top + 2);
                        g.DrawLine(pen, r.Left + 2, r.Bottom, r.Left + 2, cy);
                        g.DrawLine(pen, cx, r.Bottom, cx, r.Top + 3);
                        g.DrawLine(pen, r.Right - 2, r.Bottom, r.Right - 2, cy - 2);
                        break;
                    case CardIcon.Keyboard:
                        g.DrawRectangle(pen, r.Left, r.Top + 2, r.Width - 1, r.Height - 5);
                        g.FillEllipse(br, r.Left + 2, cy, 1, 1);
                        g.FillEllipse(br, cx, cy, 1, 1);
                        g.FillEllipse(br, r.Right - 3, cy, 1, 1);
                        g.DrawLine(pen, r.Left + 3, r.Bottom - 2, r.Right - 3, r.Bottom - 2);
                        break;
                    case CardIcon.Gear:
                        g.DrawEllipse(pen, Rectangle.Inflate(r, -2, -2));
                        for (int a = 0; a < 360; a += 60)
                        {
                            double rad = a * Math.PI / 180;
                            g.DrawLine(pen,
                                (int)(cx + Math.Cos(rad) * (r.Width / 2 - 2)),
                                (int)(cy + Math.Sin(rad) * (r.Height / 2 - 2)),
                                (int)(cx + Math.Cos(rad) * (r.Width / 2)),
                                (int)(cy + Math.Sin(rad) * (r.Height / 2)));
                        }
                        break;
                    case CardIcon.Caption:
                        g.DrawRectangle(pen, r.Left, r.Top + 1, r.Width - 1, r.Height - 4);
                        g.DrawLine(pen, r.Left + 2, cy, r.Left + 5, cy);
                        g.DrawLine(pen, r.Left + 7, cy, r.Right - 2, cy);
                        break;
                    case CardIcon.Folder:
                        g.DrawLines(pen, new[] {
                            new Point(r.Left, r.Bottom), new Point(r.Left, r.Top+2),
                            new Point(r.Left+4, r.Top+2), new Point(r.Left+6, r.Top+4),
                            new Point(r.Right, r.Top+4), new Point(r.Right, r.Bottom),
                            new Point(r.Left, r.Bottom) });
                        break;
                    case CardIcon.Star:
                        DrawStar(g, br, cx, cy, r.Width / 2);
                        break;
                    case CardIcon.Pin:
                        g.DrawEllipse(pen, cx - 3, r.Top, 6, 6);
                        g.DrawLine(pen, cx, r.Top + 6, cx, r.Bottom);
                        break;
                    case CardIcon.People:
                        // Two heads and shoulders, the one in front a little larger.
                        g.DrawEllipse(pen, r.Left + 1, r.Top + 1, 4, 4);
                        g.DrawArc(pen, r.Left - 1, r.Top + 6, 8, 8, 200, 140);
                        g.DrawEllipse(pen, cx, r.Top, 5, 5);
                        g.DrawArc(pen, cx - 2, r.Top + 6, 10, 9, 200, 140);
                        break;
                    case CardIcon.Key:
                    {
                        // A ring at the left, the shaft to the right, two teeth hanging from it.
                        float ring = r.Height * 0.62f;
                        var bow = new RectangleF(r.Left, cy - ring / 2f, ring, ring);
                        g.DrawEllipse(pen, bow);
                        float shaftY = cy;
                        g.DrawLine(pen, bow.Right, shaftY, r.Right, shaftY);
                        float tooth = r.Height * 0.26f;
                        g.DrawLine(pen, r.Right - r.Width * 0.08f, shaftY, r.Right - r.Width * 0.08f, shaftY + tooth);
                        g.DrawLine(pen, r.Right - r.Width * 0.30f, shaftY, r.Right - r.Width * 0.30f, shaftY + tooth * 0.75f);
                        break;
                    }
                    case CardIcon.Lock:
                    {
                        // A body in the lower part and the shackle's arch above it.
                        float bodyTop = r.Top + r.Height * 0.42f;
                        var body = new RectangleF(r.Left + r.Width * 0.08f, bodyTop, r.Width * 0.84f, r.Bottom - bodyTop);
                        g.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);
                        float sw = body.Width * 0.62f;
                        var shackle = new RectangleF(cx - sw / 2f, r.Top, sw, (bodyTop - r.Top) * 2f);
                        g.DrawArc(pen, shackle, 180, 180);
                        g.FillEllipse(br, cx - 1.2f, body.Y + body.Height * 0.38f, 2.4f, 2.4f);
                        break;
                    }
                }
            }
            g.SmoothingMode = prev;
        }

        private static void DrawStar(Graphics g, Brush br, int cx, int cy, int radius)
        {
            var pts = new Point[10];
            for (int i = 0; i < 10; i++)
            {
                double ang = Math.PI / 5 * i - Math.PI / 2;
                int rad = (i % 2 == 0) ? radius : radius / 2;
                pts[i] = new Point((int)(cx + Math.Cos(ang) * rad), (int)(cy + Math.Sin(ang) * rad));
            }
            g.FillPolygon(br, pts);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;

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
