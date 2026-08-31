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
        private Image _gif;                 // shared window backdrop — NOT owned here
        private bool _active;
        private int _dimPercent = 55;
        private Color _base = Color.FromArgb(30, 30, 30);

        // Pre-composed backdrop (base + cover-scaled GIF frame + scrim) at the current
        // viewport size. Rebuilt only when the GIF frame advances or the page resizes —
        // NOT on every scroll. Scrolling/repainting then just blits this bitmap, which is
        // cheap, so the wallpaper no longer lags or stutters while you scroll a tab, and
        // the animation's frame timing isn't disturbed by scroll-driven repaints.
        private Bitmap _composed;

        public BackdropTabPage(string text) : base(text)
        {
            // Smooth scrolling WITHOUT compositing.
            //
            // Whole-window WS_EX_COMPOSITED used to keep scrolling artifact-free, but it
            // cost ~90 ms per scroll step (11 FPS), so it is now only enabled while a
            // wallpaper is showing. Measured alternatives on this page:
            //     whole-window composited …  90.3 ms/step   (11 FPS)
            //     page-only composited   …  48.0 ms/step   (21 FPS)
            //     no compositing         …   5.7 ms/step  (177 FPS)
            // Compositing at ANY scope is far too expensive, so the smoothness has to
            // come from buffering the page's own painting instead: the page draws into an
            // off-screen buffer and blits once, so a scroll can't show a half-repainted
            // background behind the cards and text (which is what read as smeared,
            // "slow-motion" labels once the composite was removed).
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ResizeRedraw, true);

            // Wheel scrolling covers a useful distance per notch — the AutoScroll default
            // small-change is tiny, which made scrolling tall pages feel slow.
            VerticalScroll.SmallChange = 28;
            HorizontalScroll.SmallChange = 28;
        }

        private bool ShouldShow => _active && _gif != null;

        /// <summary>
        /// True when a full-window backdrop image is assigned to this page. Labels and
        /// other page-level controls use this to switch to a transparent background so
        /// the wallpaper shows through them instead of being boxed out by a solid fill.
        /// (All pages share the same image, so this is the same across tabs; an inactive
        /// page simply paints the flat page colour behind its transparent controls.)
        /// </summary>
        public bool HasBackdropImage => _gif != null;

        public void ApplyTheme(Theme theme)
        {
            if (theme == null)
            {
                return;
            }
            _base = theme.Background;
            if (!ShouldShow)
            {
                BackColor = theme.Background;
            }
            InvalidateComposite();
            Invalidate();
        }

        /// <summary>
        /// Assigns the SHARED window backdrop image (owned and animated by the form —
        /// never disposed here) and the dim percentage for the readability scrim. The
        /// page paints its aligned slice of the window-spanning image, so it lines up
        /// with the header above and footer below. Pass null to clear.
        /// </summary>
        public void SetBackdrop(Image img, int dimPercent)
        {
            _gif = img;
            _dimPercent = dimPercent;
            if (!ShouldShow)
            {
                BackColor = _base;
            }
            InvalidateComposite();
            Invalidate();
        }

        public void SetActive(bool active)
        {
            if (_active == active)
            {
                return;
            }
            _active = active;
            if (!ShouldShow)
            {
                BackColor = _base;
            }
            InvalidateComposite();
            Invalidate();
        }

        /// <summary>
        /// Called by the form's single backdrop animator each GIF frame: rebuild the
        /// composite (which re-samples the shared image's current frame) and repaint.
        /// Only the active/visible page does real work.
        /// </summary>
        public void InvalidateBackdrop()
        {
            if (!ShouldShow)
            {
                return;
            }
            _composeDirty = true;
            Invalidate();
        }

        /// <summary>Marks the cached composite stale so the next paint rebuilds it.</summary>
        private void InvalidateComposite()
        {
            _composeDirty = true;
        }

        private bool _composeDirty = true;

        /// <summary>
        /// Rebuilds <see cref="_composed"/> (base + cover-scaled current GIF frame +
        /// scrim) at the current viewport size when it is missing, the wrong size, or
        /// marked dirty. The expensive high-quality scaling happens here — at most once
        /// per GIF frame / resize — instead of on every paint or scroll tick.
        /// </summary>
        private void EnsureComposite()
        {
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w <= 0 || h <= 0 || _gif == null)
            {
                return;
            }

            bool sizeChanged = _composed == null || _composed.Width != w || _composed.Height != h;
            if (!sizeChanged && !_composeDirty)
            {
                return;
            }

            if (sizeChanged)
            {
                _composed?.Dispose();
                _composed = new Bitmap(w, h);
            }

            using (Graphics g = Graphics.FromImage(_composed))
            {
                var rect = new Rectangle(0, 0, w, h);
                using (var b = new SolidBrush(_base))
                {
                    g.FillRectangle(b, rect);
                }

                // Draw this page's ALIGNED slice of the window-spanning image: cover-fit
                // the image over the whole FORM, then offset into it by the page's own
                // position, so the slice is continuous with the header/footer/sidebar.
                Form f = FindForm();
                if (f != null)
                {
                    Size fc = f.ClientSize;
                    Rectangle cover = WindowBackdrop.CoverRect(_gif.Size, fc);
                    if (!cover.IsEmpty)
                    {
                        Point off;
                        try { off = f.PointToClient(PointToScreen(Point.Empty)); }
                        catch { off = Point.Empty; }
                        try { ImageAnimator.UpdateFrames(_gif); } catch { }
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(_gif, new Rectangle(cover.X - off.X, cover.Y - off.Y, cover.Width, cover.Height));
                    }
                }

                int a = Math.Max(0, Math.Min(95, _dimPercent)) * 255 / 100;
                using (var s = new SolidBrush(Color.FromArgb(a, _base)))
                {
                    g.FillRectangle(s, rect);
                }
            }
            _composeDirty = false;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);

            // After a layout change, repaint the page (and children) so the backdrop
            // re-pins to the viewport and transparent controls redraw cleanly over it.
            // Invalidate(true) only marks dirty for the next paint — no synchronous
            // repaint, no forced layout — so it cannot recurse (the 1.0.219 crash was a
            // synchronous child repaint racing the layout repaint). Only needed while a
            // wallpaper is showing; without one, ResizeRedraw plus the children's own
            // invalidation already cover layout changes, and skipping the blanket
            // child-invalidate keeps tab switches and resizes snappy.
            if (ShouldShow)
            {
                Invalidate(true);
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            // After a scroll, repaint the whole page INCLUDING child controls. The
            // backdrop is pinned to the viewport while transparent labels/checkboxes
            // scroll over it, so without redrawing the children their old (transparent)
            // pixels can linger as ghost text. Invalidate(true) just marks the region +
            // children dirty for the next paint — it does no synchronous repaint and
            // triggers no layout, so it can't recurse (the 1.0.219 crash was synchronous).
            // (WM_HSCROLL 0x0114, WM_VSCROLL 0x0115, WM_MOUSEWHEEL 0x020A.)
            if (m.Msg == 0x0114 || m.Msg == 0x0115 || m.Msg == 0x020A)
            {
                // Only needed while a wallpaper is SHOWING: the backdrop is pinned to the
                // viewport, so AutoScroll's blit shifts it and a queued repaint would let
                // that ghosted frame flash (the "overlap" / GIF-restart look). Repainting
                // synchronously fixes that — but it makes every scroll tick repaint the
                // whole page and all children, which made plain (no-wallpaper) scrolling
                // feel heavy and slow. Without a backdrop the default blit is already
                // pixel-perfect, so skip the extra work entirely.
                if (ShouldShow)
                {
                    // Tell the form a scroll is in progress so it can put the window into
                    // WS_EX_COMPOSITED for the duration. That style is what stops a
                    // transparent control leaving blit remnants over the pinned
                    // wallpaper, but holding it permanently cost ~95% of a CPU core, so
                    // it is armed here and released shortly after scrolling stops.
                    try { (FindForm() as MainForm)?.NotifyBackdropScroll(); } catch { }

                    // Repaint NOW, synchronously, not on the next paint cycle, so the
                    // shifted frame is overwritten before it's ever shown.
                    Invalidate(true);
                    Update();
                }
            }
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

            EnsureComposite();

            Graphics g = e.Graphics;
            // Pin the wallpaper to the visible viewport (not the scroll offset) and just
            // blit the pre-composed bitmap — a cheap copy, so scrolling stays smooth.
            g.ResetTransform();
            if (_composed != null)
            {
                g.DrawImageUnscaled(_composed, 0, 0);
            }
            else
            {
                using (var b = new SolidBrush(_base))
                {
                    g.FillRectangle(b, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
                }
            }
        }

        /// <summary>
        /// Paints the slice of the (viewport-pinned) backdrop that sits behind
        /// <paramref name="child"/> into the given graphics at the child's own origin.
        /// Owner-drawn transparent controls call this to fill their double-buffer with
        /// the wallpaper instead of leaving stale pixels — which is what produced ghost
        /// text when scrolling. Screen coordinates are used so it works through scroll
        /// offsets and nested panels. With no active wallpaper it fills the flat page
        /// colour (so the control still erases cleanly).
        /// </summary>
        public void PaintBackdropSlice(Graphics g, Control child)
        {
            if (g == null || child == null)
            {
                return;
            }
            var dest = new Rectangle(0, 0, child.Width, child.Height);
            if (!ShouldShow)
            {
                using (var b = new SolidBrush(_base))
                {
                    g.FillRectangle(b, dest);
                }
                return;
            }
            EnsureComposite();
            if (_composed == null)
            {
                using (var b = new SolidBrush(_base))
                {
                    g.FillRectangle(b, dest);
                }
                return;
            }

            Point childScreen = child.PointToScreen(Point.Empty);
            Point pageScreen = PointToScreen(Point.Empty);
            int vx = childScreen.X - pageScreen.X;
            int vy = childScreen.Y - pageScreen.Y;
            var src = new Rectangle(vx, vy, child.Width, child.Height);
            g.DrawImage(_composed, dest, src, GraphicsUnit.Pixel);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _composed?.Dispose();
                _composed = null;
            }
            base.Dispose(disposing);
        }
    }
}
