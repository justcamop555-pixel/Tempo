using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A window that can host a <see cref="BackdropTabPage"/> and knows how to keep its
    /// wallpaper clean while the page scrolls (by switching WS_EX_COMPOSITED on briefly).
    ///
    /// The page used to ask for <c>MainForm</c> by name. That stopped being the only
    /// window a page can live in once the Accounts page could be detached into its own
    /// window — and a page that only knows one host silently loses its scroll repair in
    /// every other one.
    /// </summary>
    public interface IBackdropHost
    {
        void NotifyBackdropScroll();
    }

    /// <summary>
    /// Something lying over a page without being part of it (the startup safety note card) that hands the wheel turns
    /// made over it to the page under it — or a quarter of the page could not be scrolled while it is up. The page then
    /// takes those turns together with its own (BackdropTabPage.TakeQueuedWheelTurns), as for a label of its own.
    /// </summary>
    public interface IWheelToPage
    {
        /// <summary>The page under it now, or null.</summary>
        Control WheelPage { get; }
    }

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

        // The same without the scrim: base + cover-scaled frame. The high-quality scaling is the
        // expensive step, and the Dim slider changes only the scrim — so a dim change re-composes
        // from this (a blit and a fill) instead of rescaling the whole image on every step of a drag.
        private Bitmap _scaled;
        private bool _scaleDirty = true;

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

        // ── Scrolling held off (a turned-off feature's cover lies over the page) ──────────────────

        private bool _scrollLocked;
        private bool _autoScrollWanted;

        /// <summary>
        /// While true the page does not scroll, whatever asks it to: a cover over the whole page (Settings ▸ Disable
        /// features, FeatureOffCover) would scroll away with the page and show the controls it hides. Turning
        /// AutoScroll off once was not enough — WinForms turns it back on by itself whenever AutoScrollMinSize changes
        /// (F11's vertical centring sets one on a short page), and the scroll-range repair toggles it off and on, so a
        /// covered Multi-Point tab scrolled again in full screen and Tempo's scroll bar sat over the cover. What was
        /// asked for meanwhile is kept and applied when the lock goes.
        /// </summary>
        internal bool ScrollLocked
        {
            get => _scrollLocked;
            set
            {
                if (_scrollLocked == value) { return; }
                if (value)
                {
                    _autoScrollWanted = base.AutoScroll;
                    if (base.AutoScroll) { AutoScrollPosition = Point.Empty; }
                    _scrollLocked = true;
                    base.AutoScroll = false;
                }
                else
                {
                    _scrollLocked = false;
                    base.AutoScroll = _autoScrollWanted;
                }
            }
        }

        public override bool AutoScroll
        {
            get => base.AutoScroll;
            set
            {
                if (_scrollLocked) { _autoScrollWanted = value; return; }
                base.AutoScroll = value;
            }
        }

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
            // The same picture with a new Dim: only the scrim changes (see SetDim).
            if (img != null && ReferenceEquals(img, _gif))
            {
                SetDim(dimPercent);
                return;
            }
            _gif = img;
            _dimPercent = dimPercent;
            if (!ShouldShow)
            {
                BackColor = _base;
            }
            InvalidateComposite();
            Invalidate();
        }

        /// <summary>
        /// A new readability scrim over the same picture — the Dim slider, many times a second while
        /// it is dragged. Re-composes from the already-scaled frame instead of rescaling the image.
        /// </summary>
        public void SetDim(int dimPercent)
        {
            if (dimPercent == _dimPercent)
            {
                return;
            }
            _dimPercent = dimPercent;
            _composeDirty = true;
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
            _scaleDirty = true;      // a new frame: rescale it
            _composeDirty = true;
            Invalidate();
        }

        /// <summary>Marks the cached composite stale so the next paint rebuilds it (frame and scrim).</summary>
        private void InvalidateComposite()
        {
            _scaleDirty = true;
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
            if (!sizeChanged && !_composeDirty && !_scaleDirty)
            {
                return;
            }

            if (sizeChanged)
            {
                _composed?.Dispose();
                _composed = new Bitmap(w, h);
            }
            if (_scaled == null || _scaled.Width != w || _scaled.Height != h)
            {
                _scaled?.Dispose();
                _scaled = new Bitmap(w, h);
                _scaleDirty = true;
            }

            var rect = new Rectangle(0, 0, w, h);
            if (_scaleDirty)
            {
                using (Graphics g = Graphics.FromImage(_scaled))
                {
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
                }
                _scaleDirty = false;
            }

            // The scrim over the scaled frame — all a Dim change has to redo.
            using (Graphics g = Graphics.FromImage(_composed))
            {
                g.DrawImageUnscaled(_scaled, 0, 0);
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

        // ── Scrolling ───────────────────────────────────────────────────────────────────────────────
        //
        // AutoScroll moves a page by sliding the pixels already on screen (ScrollWindowEx) and asking only the
        // strip it uncovered to be painted — and every card, chart and label on the page is a window of its
        // own, painted by its own WM_PAINT, which Windows hands out only when nothing else is waiting. A fast
        // wheel, a touchpad or a held Page Down queues scrolls faster than that: each one slid the pixels again
        // before the uncovered strips and the cards under them had been painted, so the stale ones were slid
        // along with the rest. The Statistics page filled with smeared copies of its cards while it moved (the
        // user's stress test, 2026-11-08; scratchpad/scrollstress filmed 97 of 155 frames like that, the worst
        // with 12% of the page wrong) and came right only once the wheel stopped.
        //
        // Now every scroll is finished before the next one is looked at: what it uncovered — the page and
        // every window in it — is painted at once (RedrawWindow UPDATENOW|ALLCHILDREN, which paints only what
        // is already marked: a strip, not the page). And wheel turns that have queued up behind the one being
        // handled are taken together, as one scroll by their sum, so painting each step never makes the page
        // trail behind the wheel.
        private const int WM_HSCROLL = 0x0114, WM_VSCROLL = 0x0115, WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E;
        private const int MK_SHIFT = 0x0004;

        /// <summary>For scratchpad/scrollstress's before/after in one run: false scrolls as before 2026-11-08 (slide only).</summary>
        internal static bool ScrollFixForTest = true;
        /// <summary>For scratchpad/scrollstress: false skips <see cref="WaitForFrameStart"/>.</summary>
        internal static bool FrameWaitForTest = true;

        protected override void WndProc(ref Message m)
        {
            bool wheel = m.Msg == WM_MOUSEWHEEL || m.Msg == WM_MOUSEHWHEEL;
            if ((!wheel && m.Msg != WM_VSCROLL && m.Msg != WM_HSCROLL) || !ScrollFixForTest)
            {
                base.WndProc(ref m);
                return;
            }

            Point before = AutoScrollPosition;
            long stepStart = System.Diagnostics.Stopwatch.GetTimestamp();
            if (IsHandleCreated) { WaitForFrameStart(); }
            int merged = wheel && IsHandleCreated ? TakeQueuedWheelTurns(ref m) : 0;

            // The page held still on the screen for the step (HoldStep): the slide and the paint happen under its
            // picture, so the screen shows the page before the step or after it, never the moment between.
            bool held = IsHandleCreated && HoldStep();
            try
            {
                // Sideways: the tilt wheel and a touchpad's sideways swipe (WM_MOUSEHWHEEL), which WinForms ignores
                // altogether, and Shift with the wheel as in a browser — the latter only while the page has a sideways
                // bar, so Shift+wheel stays the ordinary wheel everywhere else (as in Live debug's panes).
                int keys = (int)((long)m.WParam & 0xFFFF);
                bool sideways = m.Msg == WM_MOUSEHWHEEL || (m.Msg == WM_MOUSEWHEEL && (keys & MK_SHIFT) != 0 && HScroll);
                if (sideways && HScroll)
                {
                    int delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
                    // Tilting right is positive; with Shift, turning the wheel towards you (negative) goes right.
                    ScrollSideways(m.Msg == WM_MOUSEHWHEEL ? delta : -delta);
                    m.Result = IntPtr.Zero;
                }
                else
                {
                    base.WndProc(ref m);
                }

                if (!IsHandleCreated || AutoScrollPosition == before)
                {
                    return;   // at an end, or nothing to scroll: nothing was slid, nothing to paint
                }

                if (ShouldShow)
                {
                    // A wallpaper is pinned to the VIEWPORT, so the slide moved it with the cards — everything is
                    // repainted, children too, before the slid frame can be shown. The form puts the window into
                    // WS_EX_COMPOSITED for the scroll (a transparent label over the wallpaper left remnants
                    // otherwise) and lets it lapse once scrolling stops: held, it cost ~95% of a core.
                    try { (FindForm() as IBackdropHost)?.NotifyBackdropScroll(); } catch { }
                    Invalidate(true);
                }
                PaintNow();
                NoteStep(stepStart, merged, held);
            }
            finally
            {
                if (held) { _scrollHold.HoldEnd(); }
            }
        }

        // ── For Live debug: the last scroll, in numbers ─────────────────────────────────────────────────
        private static int _scrollSteps, _scrollMerged, _scrollHeld;
        private static double _scrollMsTotal, _scrollMsWorst;
        private static long _scrollLastStamp;
        private static DateTime _scrollStartedAt;
        private static string _scrollPage;

        /// <summary>One step that moved the page: counted into the scroll it belongs to (steps under 1.5 s apart).</summary>
        private void NoteStep(long started, int merged, bool held)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (now - _scrollLastStamp > System.Diagnostics.Stopwatch.Frequency * 3 / 2 || !string.Equals(_scrollPage, Text, StringComparison.Ordinal))
            {
                _scrollSteps = _scrollMerged = _scrollHeld = 0;
                _scrollMsTotal = _scrollMsWorst = 0;
                _scrollStartedAt = DateTime.Now;
                _scrollPage = Text;
            }
            _scrollLastStamp = now;
            double ms = (now - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _scrollSteps++;
            _scrollMerged += merged;
            if (held) { _scrollHeld++; }
            _scrollMsTotal += ms;
            if (ms > _scrollMsWorst) { _scrollMsWorst = ms; }
        }

        /// <summary>The last scroll of any page, for Live debug; null before the first.</summary>
        internal static string LastScrollSummary()
        {
            if (_scrollSteps == 0) { return null; }
            double ago = (System.Diagnostics.Stopwatch.GetTimestamp() - _scrollLastStamp) / (double)System.Diagnostics.Stopwatch.Frequency;
            return "last on " + (_scrollPage ?? "?") + " at " + _scrollStartedAt.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
                   + (ago < 1.5 ? " (going on)" : "") + " · " + _scrollSteps + " step(s)"
                   + (_scrollMerged > 0 ? ", " + _scrollMerged + " more wheel turn(s) taken together with them" : "")
                   + " · " + _scrollHeld + " held still · a step " + (_scrollMsTotal / _scrollSteps).ToString("0.0") + " ms on average, "
                   + _scrollMsWorst.ToString("0.0") + " ms at worst";
        }

        // ── Holding the page still for a step ──────────────────────────────────────────────────────────────
        //
        // Painting each step at once and waiting for the frame cut the smear to an occasional strip, but could not end
        // it: the screen shows the window's surface as it is at each refresh, and a step is two changes to that surface —
        // the slide, then the paint of what it uncovered (about 3 ms on the Statistics page). A refresh between the two
        // showed the uncovered strip with the pixels slid out of it, for one frame (filmed: 19 of 164 frames at a fast
        // wheel, bands up to 37 px). So each step now runs under a picture of the page as it was (the tab switch's
        // curtain, SwitchCurtain.HoldStart), taken down the moment the step has painted: whatever the screen catches is
        // a whole page. Its memory is kept between steps and let go a little after the scroll stops.
        private SwitchCurtain _scrollHold;
        private Form _scrollHoldOwner;
        private Timer _holdRelease;

        /// <summary>For scratchpad/scrollstress's before/after: false scrolls without the held picture.</summary>
        internal static bool ScrollHoldForTest = true;

        private bool HoldStep()
        {
            // Not an animation — nothing moves that would not move anyway — so it holds with Windows' animations off too.
            if (!ScrollHoldForTest || !Visible) { return false; }
            Form owner = FindForm();
            if (owner == null) { return false; }
            try
            {
                // The picture is a window owned by the page's window, kept in step with it: a page moved into a window of
                // its own (MainForm.TabWindows.cs) gets a picture owned by that window, or it would stack with the main one.
                if (_scrollHold != null && !ReferenceEquals(_scrollHoldOwner, owner))
                {
                    _scrollHold.Dispose();
                    _scrollHold = null;
                }
                if (_scrollHold == null)
                {
                    _scrollHold = new SwitchCurtain();
                    _scrollHoldOwner = owner;
                }
                if (!_scrollHold.HoldStart(this, owner)) { return false; }
                if (_holdRelease == null)
                {
                    _holdRelease = new Timer { Interval = 1500 };
                    _holdRelease.Tick += (s, e) => { _holdRelease.Stop(); _scrollHold?.ReleaseHold(); };
                }
                _holdRelease.Stop();
                _holdRelease.Start();
                return true;
            }
            catch { return false; }
        }

        private long _lastScrollStamp;

        /// <summary>
        /// While a scroll goes on, starts each step just after the screen has shown a frame. The slide and the paint
        /// of what it uncovered are two steps on the window's surface, and the screen shows that surface as it is at
        /// each refresh: one that fell between them showed the uncovered strip still holding its old pixels (filmed:
        /// half the frames of a fast wheel, one strip each). Begun right after a refresh, the step is done before the
        /// next one. The wait is at most one frame, and the wheel turns that queue meanwhile are taken together with
        /// this one (TakeQueuedWheelTurns), so a fast wheel moves the page once per frame instead of falling behind.
        /// The first step of a scroll does not wait: nothing is moving yet to tear.
        /// </summary>
        private void WaitForFrameStart()
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            bool scrolling = now - _lastScrollStamp < System.Diagnostics.Stopwatch.Frequency / 8;
            _lastScrollStamp = now;
            if (scrolling && Visible && FrameWaitForTest)
            {
                // On the UI thread: at most one frame. A straight DwmFlush waited as long as DWM stalled, and all of Tempo
                // with it (FramePacer).
                FramePacer.WaitFrame(17);
                FrameWaitTicksForTest += System.Diagnostics.Stopwatch.GetTimestamp() - now;
            }
        }

        /// <summary>For scratchpad/scrollstress: the time spent waiting for frames, to tell it from the scrolling itself.</summary>
        internal static long FrameWaitTicksForTest;

        /// <summary>Paints whatever is marked for painting in the page and every window in it, now.</summary>
        private void PaintNow()
        {
            try { RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, RDW_UPDATENOW | RDW_ALLCHILDREN); }
            catch { }
        }

        /// <summary>
        /// Folds the wheel turns waiting in the queue right behind this one into it — the same message, the same
        /// keys held, aimed at this page or at a window in it that passes the wheel up to it (a card, a label, a
        /// chart; never a list, a text box or a combo box, which scroll or change by themselves). Stops at the
        /// first message that is anything else, so nothing is ever taken out of order.
        /// </summary>
        private int TakeQueuedWheelTurns(ref Message m)
        {
            int taken = 0;
            try
            {
                int keys = (int)((long)m.WParam & 0xFFFF);
                int delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
                while (taken < 64 && PeekMessage(out MSG next, IntPtr.Zero, 0, 0, PM_NOREMOVE | PM_NOYIELD))
                {
                    if (next.message != m.Msg || (int)((long)next.wParam & 0xFFFF) != keys || !PassesWheelHere(next.hwnd))
                    {
                        break;
                    }
                    if (!PeekMessage(out MSG got, next.hwnd, m.Msg, m.Msg, PM_REMOVE | PM_NOYIELD)) { break; }
                    delta += (short)(((long)got.wParam >> 16) & 0xFFFF);
                    taken++;
                }
                if (taken == 0) { return 0; }
                delta = Math.Max(short.MinValue, Math.Min(short.MaxValue, delta));
                m.WParam = (IntPtr)(long)(uint)(((delta & 0xFFFF) << 16) | keys);
            }
            catch { }
            return taken;
        }

        /// <summary>Whether a wheel turn aimed at <paramref name="hwnd"/> ends up scrolling this page.</summary>
        private bool PassesWheelHere(IntPtr hwnd)
        {
            if (hwnd == Handle) { return true; }
            if (hwnd == IntPtr.Zero) { return false; }
            if (!IsChild(Handle, hwnd)) { return HandedOnHere(hwnd); }
            for (IntPtr h = hwnd; h != IntPtr.Zero && h != Handle; h = GetParent(h))
            {
                Control c = Control.FromHandle(h);
                if (c == null || !PassesWheelOn(c)) { return false; }   // a native part of a control: its own
            }
            return true;
        }

        /// <summary>
        /// A turn aimed at something lying over this page without being in it — the safety note card, or a label on it —
        /// that hands its wheel turns on to the page under it (<see cref="IWheelToPage"/>).
        /// </summary>
        private bool HandedOnHere(IntPtr hwnd)
        {
            for (IntPtr h = hwnd; h != IntPtr.Zero; h = GetParent(h))
            {
                Control c = Control.FromHandle(h);
                if (c is IWheelToPage over) { return ReferenceEquals(over.WheelPage, this); }
                if (c == null || !PassesWheelOn(c)) { return false; }
            }
            return false;
        }

        private static readonly System.Collections.Generic.Dictionary<Type, bool> _passesWheel =
            new System.Collections.Generic.Dictionary<Type, bool>();

        /// <summary>A control that does nothing with the wheel itself, so Windows hands the turn to its parent.</summary>
        private static bool PassesWheelOn(Control c)
        {
            if (c is TextBoxBase || c is ListView || c is ListBox || c is ComboBox || c is UpDownBase || c is TrackBar
                || c is TreeView || c is DataGridView || c is WebBrowserBase || c is DateTimePicker || c is MonthCalendar
                || c is FeatureOffCover)
            {
                return false;
            }
            if (c is ScrollableControl sc && sc.AutoScroll) { return false; }   // a scrolling panel scrolls itself
            Type t = c.GetType();
            if (!_passesWheel.TryGetValue(t, out bool passes))
            {
                // One of Tempo's controls that answers the wheel itself (a spinner, a list) overrides OnMouseWheel.
                System.Reflection.MethodInfo on = t.GetMethod("OnMouseWheel",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
                    null, new[] { typeof(MouseEventArgs) }, null);
                passes = on == null || on.DeclaringType == typeof(Control) || on.DeclaringType == typeof(ScrollableControl);
                _passesWheel[t] = passes;
            }
            return passes;
        }

        /// <summary>Scrolls sideways by <paramref name="right"/> pixels (negative: left), within the page's range.</summary>
        private void ScrollSideways(int right)
        {
            int max = Math.Max(0, DisplayRectangle.Width - ClientSize.Width);
            int x = -AutoScrollPosition.X;
            int to = Math.Max(0, Math.Min(max, x + right));
            if (to != x)
            {
                AutoScrollPosition = new Point(to, -AutoScrollPosition.Y);
            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public int message;
            public IntPtr wParam;
            public IntPtr lParam;
            public int time;
            public int ptX, ptY;
            public int lPrivate;
        }

        private const uint PM_NOREMOVE = 0x0000, PM_REMOVE = 0x0001, PM_NOYIELD = 0x0002;
        private const uint RDW_ALLCHILDREN = 0x0080, RDW_UPDATENOW = 0x0100;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, int filterMin, int filterMax, uint remove);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsChild(IntPtr parent, IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);

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
                _scaled?.Dispose();
                _scaled = null;
                _holdRelease?.Dispose();
                _holdRelease = null;
                _scrollHold?.Dispose();
                _scrollHold = null;
            }
            base.Dispose(disposing);
        }
    }
}
