using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A thin, full-width band that plays an optional animated GIF as a backdrop
    /// behind a readability scrim. Used for the second ("footer") background image.
    /// Owner-drawn and double-buffered so the animation stays smooth.
    /// </summary>
    public sealed class GifBackdropPanel : Panel
    {
        private Image _gif;
        private bool _animating;

        /// <summary>True when a background GIF/image is currently set.</summary>
        public bool HasGif => _gif != null;
        private Theme _theme = Theme.ForKind(Models.ThemeKind.Dark);

        /// <summary>Draw a thin accent line along the top edge (mirrors the header).</summary>
        public bool TopAccent { get; set; } = true;

        public GifBackdropPanel()
        {
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

        public void SetGif(Image img)
        {
            if (_gif != null)
            {
                try { ImageAnimator.StopAnimate(_gif, OnGifFrame); } catch { }
                _gif.Dispose();
                _gif = null;
            }
            _animating = false;

            _gif = img;
            if (_gif != null)
            {
                try
                {
                    if (ImageAnimator.CanAnimate(_gif))
                    {
                        ImageAnimator.Animate(_gif, OnGifFrame);
                        _animating = true;
                    }
                }
                catch { }
            }
            Invalidate();
        }

        /// <summary>Pauses/resumes animation to save CPU while hidden or minimised.</summary>
        public void SetAnimationActive(bool active)
        {
            if (_gif == null)
            {
                return;
            }
            if (active && !_animating)
            {
                try
                {
                    if (ImageAnimator.CanAnimate(_gif))
                    {
                        ImageAnimator.Animate(_gif, OnGifFrame);
                        _animating = true;
                    }
                }
                catch { }
            }
            else if (!active && _animating)
            {
                try { ImageAnimator.StopAnimate(_gif, OnGifFrame); } catch { }
                _animating = false;
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

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var rect = new Rectangle(0, 0, Width, Height);
            using (var bg = new SolidBrush(_theme.Surface))
            {
                g.FillRectangle(bg, rect);
            }

            if (_gif != null)
            {
                try { ImageAnimator.UpdateFrames(_gif); } catch { }
                DrawCover(g, _gif, rect);
                using (var scrim = new SolidBrush(Color.FromArgb(150, _theme.Surface)))
                {
                    g.FillRectangle(scrim, rect);
                }
            }

            if (TopAccent)
            {
                var line = new Rectangle(0, 0, Width, 2);
                using (var lb = new LinearGradientBrush(line,
                           _theme.Accent, _theme.AccentHover, LinearGradientMode.Horizontal))
                {
                    g.FillRectangle(lb, line);
                }
            }
        }

        private static void DrawCover(Graphics g, Image img, Rectangle dest)
        {
            if (img == null || img.Width <= 0 || img.Height <= 0 || dest.Width <= 0 || dest.Height <= 0)
            {
                return;
            }
            double scale = Math.Max(dest.Width / (double)img.Width, dest.Height / (double)img.Height);
            int w = (int)Math.Ceiling(img.Width * scale);
            int h = (int)Math.Ceiling(img.Height * scale);
            int x = dest.X + (dest.Width - w) / 2;
            int y = dest.Y + (dest.Height - h) / 2;
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
            if (disposing && _gif != null)
            {
                try { ImageAnimator.StopAnimate(_gif, OnGifFrame); } catch { }
                _gif.Dispose();
                _gif = null;
            }
            base.Dispose(disposing);
        }
    }
}
