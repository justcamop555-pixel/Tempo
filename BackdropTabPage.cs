using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A TabPage that can paint an animated GIF (or static image) as a full-page
    /// background "wallpaper": cover-scaled to fill the viewport with a readability
    /// scrim on top. Content controls sit above it as normal opaque cards, so the
    /// image shows through the page margins and the gaps between cards.
    ///
    /// Only the page background is repainted each GIF frame — the child controls are
    /// not invalidated — which keeps the effect flicker-free and cheap.
    /// </summary>
    public sealed class BackdropTabPage : TabPage
    {
        private Image _gif;
        private bool _active;
        private bool _paused;
        private bool _animating;
        private Color _base = Color.FromArgb(30, 30, 30);
        private Color _scrim = Color.FromArgb(140, 30, 30, 30);

        public BackdropTabPage(string text) : base(text)
        {
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.ResizeRedraw,
                true);
        }

        private bool ShouldShow => _active && _gif != null;

        public void ApplyTheme(Theme theme)
        {
            if (theme == null)
            {
                return;
            }
            _base = theme.Background;
            // Scrim in the page background colour so cards and text stay readable
            // while the image still shows through.
            _scrim = Color.FromArgb(140, theme.Background);
            if (!ShouldShow)
            {
                BackColor = theme.Background;
            }
            Invalidate();
        }

        public void SetBackdrop(Image img)
        {
            if (ReferenceEquals(_gif, img))
            {
                return;
            }
            StopAnim();
            _gif = img;
            if (ShouldShow && !_paused)
            {
                StartAnim();
            }
            Invalidate();
        }

        public void SetActive(bool active)
        {
            if (_active == active)
            {
                return;
            }
            _active = active;
            if (ShouldShow && !_paused)
            {
                StartAnim();
            }
            else
            {
                StopAnim();
                BackColor = _base;
            }
            Invalidate();
        }

        /// <summary>Pause/resume the animation (e.g. when the window is hidden).</summary>
        public void SetAnimationActive(bool on)
        {
            _paused = !on;
            if (on && ShouldShow)
            {
                StartAnim();
            }
            else
            {
                StopAnim();
            }
        }

        private void StartAnim()
        {
            // Guard against double-registration: SetActive and SetAnimationActive can
            // both ask to start, and calling ImageAnimator.Animate twice on the same
            // image makes it advance frames twice as fast.
            if (_animating || _gif == null)
            {
                return;
            }
            try { ImageAnimator.Animate(_gif, OnGifFrame); _animating = true; } catch { }
        }

        private void StopAnim()
        {
            if (!_animating || _gif == null)
            {
                return;
            }
            try { ImageAnimator.StopAnimate(_gif, OnGifFrame); } catch { }
            _animating = false;
        }

        private void OnGifFrame(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            // Repaint only the background; children are left untouched (no flicker).
            try { BeginInvoke((Action)Invalidate); } catch { }
        }

        /// <summary>
        /// Keep the current scroll position when a child control gains focus.
        /// By default WinForms scrolls an AutoScroll panel to bring the focused
        /// control into view, which made the page jump to the top whenever a control
        /// was clicked, enabled/disabled, or when clicking started (focus moves as
        /// buttons toggle). Returning the current position disables that jump.
        /// </summary>
        protected override Point ScrollToControl(Control activeControl)
        {
            return AutoScrollPosition;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (!ShouldShow)
            {
                base.OnPaintBackground(e);
                return;
            }

            Graphics g = e.Graphics;
            // Pin the wallpaper to the visible viewport rather than the scroll offset.
            g.ResetTransform();

            var rect = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            using (var b = new SolidBrush(_base))
            {
                g.FillRectangle(b, rect);
            }

            try { ImageAnimator.UpdateFrames(_gif); } catch { }

            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            DrawCover(g, _gif, rect);

            using (var s = new SolidBrush(_scrim))
            {
                g.FillRectangle(s, rect);
            }
        }

        private static void DrawCover(Graphics g, Image img, Rectangle bounds)
        {
            if (img == null || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }
            float ir = (float)img.Width / img.Height;
            float br = (float)bounds.Width / bounds.Height;
            int w, h;
            if (ir > br)
            {
                h = bounds.Height;
                w = (int)(h * ir);
            }
            else
            {
                w = bounds.Width;
                h = (int)(w / ir);
            }
            int x = bounds.X + (bounds.Width - w) / 2;
            int y = bounds.Y + (bounds.Height - h) / 2;
            g.DrawImage(img, new Rectangle(x, y, w, h));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopAnim();
            }
            base.Dispose(disposing);
        }
    }
}
