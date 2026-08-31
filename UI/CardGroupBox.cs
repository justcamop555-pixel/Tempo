using System;
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
        List, Play, Chart, Keyboard, Gear, Caption, Folder, Star, Pin
    }

    public sealed class CardGroupBox : GroupBox
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
            string title = (Text ?? string.Empty).Replace("&&", "&").ToUpperInvariant();
            if (title.Length > 0)
            {
                float th = Font.GetHeight(g);
                if (Icon == CardIcon.None)
                {
                    var bar = new Rectangle(12, 5, 3, 14);
                    using (var barPath = Rounded(bar, 1))
                    using (var accentBar = new SolidBrush(_accent))
                    {
                        g.FillPath(accentBar, barPath);
                    }
                    using (var tb = new SolidBrush(_title))
                    {
                        DrawTracked(g, title, Font, tb, 22f, 5f + (14f - th) / 2f, 0.9f);
                    }
                }
                else
                {
                    // A rounded icon badge with a tinted accent fill, then the title.
                    var chip = new Rectangle(12, 4, 22, 22);
                    using (var chipPath = Rounded(chip, 7))
                    using (var chipFill = new SolidBrush(Blend(_accent, _surface, 0.28f)))
                    {
                        g.FillPath(chipFill, chipPath);
                    }
                    DrawGlyph(g, Icon, chip, _accent);
                    using (var tb = new SolidBrush(_title))
                    {
                        DrawTracked(g, title, Font, tb, chip.Right + 9f, chip.Top + (chip.Height - th) / 2f, 0.9f);
                    }
                }
            }
        }

        // Draws text with a little extra space between letters (tracking), used for the
        // uppercase card titles. Returns the x just past the last glyph.
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
                g.DrawString(s, font, brush, x, y, sf);
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

        // Minimal, crisp line glyphs drawn inside the given chip rectangle.
        private static void DrawGlyph(Graphics g, CardIcon icon, Rectangle chip, Color color)
        {
            var prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Inner drawing area, padded inside the chip.
            var r = Rectangle.Inflate(chip, -5, -5);
            using (var pen = new Pen(color, 1.6f))
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
