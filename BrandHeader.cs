using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// The application header bar. Owner-drawn so the "Tempo" wordmark renders as a
    /// vivid true-colour gradient with a rounded accent logo tile and a metronome
    /// glyph — rather than flat themed text.
    /// </summary>
    public sealed class BrandHeader : Panel
    {
        private Theme _theme = Theme.ForKind(Models.ThemeKind.Dark);

        public BrandHeader()
        {
            Dock = DockStyle.Top;
            Height = 66;
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
        }

        public void ApplyTheme(Theme theme)
        {
            if (theme != null)
            {
                _theme = theme;
                BackColor = theme.Surface;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var rect = new Rectangle(0, 0, Width, Height);

            // ── Background: subtle vertical surface gradient + accent underline ──
            using (var bg = new LinearGradientBrush(rect,
                       Blend(_theme.Surface, _theme.Background, 0.0),
                       Blend(_theme.Surface, _theme.Background, 0.35),
                       LinearGradientMode.Vertical))
            {
                g.FillRectangle(bg, rect);
            }

            // Accent hairline along the bottom edge — a full true-colour sweep.
            var underline = new Rectangle(0, Height - 3, Width, 3);
            using (var ub = new LinearGradientBrush(underline,
                       _theme.Accent, _theme.AccentHover, LinearGradientMode.Horizontal))
            {
                g.FillRectangle(ub, underline);
            }

            // ── Logo tile ───────────────────────────────────────────────────────
            var tile = new Rectangle(16, (Height - 36) / 2 - 1, 36, 36);
            using (var tilePath = Rounded(tile, 9))
            using (var tileBrush = new LinearGradientBrush(tile,
                       _theme.Accent, _theme.AccentHover, LinearGradientMode.ForwardDiagonal))
            {
                g.FillPath(tileBrush, tilePath);
            }

            // Metronome / bolt glyph inside the tile (white for contrast).
            DrawBolt(g, tile);

            // ── Wordmark: "Tempo" in a horizontal accent gradient ───────────────
            float textLeft = tile.Right + 12;
            using (var titleFont = new Font("Segoe UI", 17f, FontStyle.Bold))
            {
                string word = "Tempo";
                SizeF size = g.MeasureString(word, titleFont);
                float ty = (Height - size.Height) / 2f - 6f;

                var textRect = new RectangleF(textLeft, ty, size.Width + 2, size.Height);
                using (var grad = new LinearGradientBrush(textRect,
                           _theme.Accent, _theme.AccentHover, LinearGradientMode.Horizontal))
                {
                    // Brighten the gradient stops for a vivid, true-colour wordmark.
                    grad.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { _theme.Accent, _theme.AccentHover, Brighten(_theme.AccentHover, 0.25) },
                        Positions = new[] { 0f, 0.6f, 1f }
                    };
                    g.DrawString(word, titleFont, grad, textLeft, ty);
                }

                // Tagline beneath the wordmark.
                using (var tagFont = new Font("Segoe UI", 8.5f, FontStyle.Regular))
                using (var tagBrush = new SolidBrush(_theme.TextMuted))
                {
                    g.DrawString("AUTO CLICKER", tagFont, tagBrush, textLeft + 2, ty + size.Height - 6);
                }
            }
        }

        private void DrawBolt(Graphics g, Rectangle tile)
        {
            // A simple lightning bolt centred in the tile.
            float cx = tile.Left + tile.Width / 2f;
            float cy = tile.Top + tile.Height / 2f;
            PointF[] bolt =
            {
                new PointF(cx + 3, cy - 11),
                new PointF(cx - 7, cy + 2),
                new PointF(cx - 1, cy + 2),
                new PointF(cx - 3, cy + 11),
                new PointF(cx + 7, cy - 3),
                new PointF(cx + 1, cy - 3),
            };
            using (var b = new SolidBrush(Color.White))
            {
                g.FillPolygon(b, bolt);
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color Blend(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static Color Brighten(Color c, double amount)
        {
            return Color.FromArgb(
                Math.Min(255, (int)(c.R + (255 - c.R) * amount)),
                Math.Min(255, (int)(c.G + (255 - c.G) * amount)),
                Math.Min(255, (int)(c.B + (255 - c.B) * amount)));
        }
    }
}
