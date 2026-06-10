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
        private Image _bgGif;
        private bool _bgAnimating;

        /// <summary>
        /// Sets an optional animated image drawn across the header as a backdrop
        /// (behind a readability scrim). Pass null to remove it. The header takes
        /// ownership of the image and disposes it.
        /// </summary>
        public void SetBackgroundGif(Image img)
        {
            if (_bgGif != null)
            {
                try { ImageAnimator.StopAnimate(_bgGif, OnGifFrame); } catch { }
                _bgGif.Dispose();
                _bgGif = null;
            }
            _bgAnimating = false;

            _bgGif = img;
            if (_bgGif != null)
            {
                try
                {
                    if (ImageAnimator.CanAnimate(_bgGif))
                    {
                        ImageAnimator.Animate(_bgGif, OnGifFrame);
                        _bgAnimating = true;
                    }
                }
                catch { }
            }
            Invalidate();
        }

        /// <summary>
        /// Pauses or resumes the backdrop animation. Used to stop spending CPU on an
        /// animated GIF while the window is minimised or hidden to the tray.
        /// </summary>
        public void SetAnimationActive(bool active)
        {
            if (_bgGif == null)
            {
                return;
            }
            if (active && !_bgAnimating)
            {
                try
                {
                    if (ImageAnimator.CanAnimate(_bgGif))
                    {
                        ImageAnimator.Animate(_bgGif, OnGifFrame);
                        _bgAnimating = true;
                    }
                }
                catch { }
            }
            else if (!active && _bgAnimating)
            {
                try { ImageAnimator.StopAnimate(_bgGif, OnGifFrame); } catch { }
                _bgAnimating = false;
            }
        }

        private void OnGifFrame(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try { BeginInvoke((Action)Invalidate); } catch { }
        }

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

            // Optional animated GIF backdrop, cover-fit, behind a scrim so the
            // wordmark and controls stay readable over any image.
            if (_bgGif != null)
            {
                try { ImageAnimator.UpdateFrames(_bgGif); } catch { }
                DrawCover(g, _bgGif, rect);
                using (var scrim = new SolidBrush(Color.FromArgb(150, _theme.Surface)))
                {
                    g.FillRectangle(scrim, rect);
                }
            }

            // Accent hairline along the bottom edge — a full true-colour sweep.
            var underline = new Rectangle(0, Height - 3, Width, 3);
            using (var ub = new LinearGradientBrush(underline,
                       _theme.Accent, _theme.AccentHover, LinearGradientMode.Horizontal))
            {
                g.FillRectangle(ub, underline);
            }

            // ── Logo tile (soft shadow + top highlight for a bit of depth) ──────
            var tile = new Rectangle(18, (Height - 38) / 2, 38, 38);

            using (var shadowPath = Rounded(new Rectangle(tile.Left + 1, tile.Top + 3, tile.Width, tile.Height), 10))
            using (var sb = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            {
                g.FillPath(sb, shadowPath);
            }

            using (var tilePath = Rounded(tile, 10))
            {
                using (var tileBrush = new LinearGradientBrush(tile,
                           _theme.Accent, _theme.AccentHover, LinearGradientMode.ForwardDiagonal))
                {
                    g.FillPath(tileBrush, tilePath);
                }

                // Subtle top highlight, clipped to the rounded tile.
                g.SetClip(tilePath);
                var hiRect = new Rectangle(tile.Left, tile.Top, tile.Width, tile.Height / 2);
                using (var hi = new LinearGradientBrush(hiRect,
                           Color.FromArgb(55, 255, 255, 255), Color.FromArgb(0, 255, 255, 255),
                           LinearGradientMode.Vertical))
                {
                    g.FillRectangle(hi, hiRect);
                }
                g.ResetClip();
            }

            // Metronome / bolt glyph inside the tile (white for contrast).
            DrawBolt(g, tile);

            // ── Wordmark: "Tempo" in a horizontal accent gradient, centred ──────
            float textLeft = tile.Right + 14;
            using (var titleFont = new Font("Segoe UI", 19f, FontStyle.Bold))
            {
                string word = "Tempo";
                SizeF size = g.MeasureString(word, titleFont);
                float ty = (Height - size.Height) / 2f;

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

        private static void DrawCover(Graphics g, Image img, Rectangle dest)
        {
            if (img == null || img.Width <= 0 || img.Height <= 0 || dest.Width <= 0 || dest.Height <= 0)
            {
                return;
            }
            // Scale to cover the destination, preserving aspect ratio, centred.
            double scale = Math.Max(dest.Width / (double)img.Width, dest.Height / (double)img.Height);
            int w = (int)Math.Ceiling(img.Width * scale);
            int h = (int)Math.Ceiling(img.Height * scale);
            int x = dest.X + (dest.Width - w) / 2;
            int y = dest.Y + (dest.Height - h) / 2;

            // Smooth, high-quality scaling so the animated backdrop doesn't look
            // blocky or shimmer when it's stretched to cover the bar.
            var prevInterp = g.InterpolationMode;
            var prevOffset = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(img, new Rectangle(x, y, w, h));
            g.InterpolationMode = prevInterp;
            g.PixelOffsetMode = prevOffset;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _bgGif != null)
            {
                try { ImageAnimator.StopAnimate(_bgGif, OnGifFrame); } catch { }
                _bgGif.Dispose();
                _bgGif = null;
            }
            base.Dispose(disposing);
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
