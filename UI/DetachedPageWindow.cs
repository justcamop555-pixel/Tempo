using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A separate top-level window that hosts ONE of Tempo's pages, so it can sit beside the
    /// thing it is about instead of hiding whatever tab you were on.
    ///
    /// The page it shows is a real <see cref="BackdropTabPage"/>, and the caller MOVES its
    /// content in rather than copying it, so every control, handler, timer and theme role
    /// comes along and moving it back is the same operation in reverse.
    ///
    /// It carries its own <see cref="ModernTabControl"/> for one reason, learned the hard way:
    /// WinForms refuses to parent a TabPage to a Form —
    ///     "TabPage cannot be added to a 'System.Windows.Forms.Form'. TabPages can only be
    ///      added to TabControls."
    /// — and it throws at run time, not compile time. ModernTabControl already hides its
    /// header strip, so a one-page instance reads as a plain panel.
    ///
    /// Deliberately NOT owned by the main window. An owned form is minimised and hidden with
    /// its owner, which would defeat the point: the window exists so it can stay up while
    /// Tempo itself is minimised or in the tray.
    ///
    /// THEMED BY PAINTING, NOT BY CHILD CONTROLS. The first version put a Label on a
    /// Surface-coloured strip; ThemeManager gives a label the colour of the nearest container
    /// it RECOGNISES, and a plain Panel is not one, so it walked past the strip to the Form
    /// and painted the title on the page colour — a visible dark box around "Accounts" on a
    /// lighter bar. The header is drawn here now (surface or wallpaper slice, glyph, title,
    /// hairline), so there is no child control left to disagree with it.
    /// </summary>
    public sealed class DetachedPageWindow : Form, IBackdropHost
    {
        private readonly ModernTabControl _host;
        private readonly HeaderStrip _strip;
        private readonly RoundedButton _pinBtn;
        private readonly RoundedButton _dockBtn;
        private Theme _theme;

        // The shared wallpaper (owned and animated by the main window — never disposed here).
        private Image _backdrop;
        private int _backdropDim = 55;

        private const int StripHeight = 40;

        // The screen's scale (1.25 at 125%, 1.5 at 150%). Tempo is System-DPI-aware and this window is
        // not auto-scaled by WinForms, so its own sizes — the strip, its buttons, the minimum and first
        // size — are multiplied by hand. They were fixed 96-dpi pixels, so on a laptop at 150% the
        // 40 px strip held 150% text and the 26 px buttons were cramped around their captions.
        private readonly float _scale;
        private int D(int v) => (int)Math.Round(v * _scale);

        /// <summary>Raised when the user asks for the page to go back to being a tab.</summary>
        public event EventHandler DockRequested;

        /// <summary>Raised when the always-on-top pin is toggled; the argument is the new state.</summary>
        public event EventHandler<bool> TopMostChanged;

        private bool _pinned;
        private bool _companionTopMost;

        /// <summary>The window's own "Always on top" pin: what its button shows and what is saved.</summary>
        public bool Pinned
        {
            get { return _pinned; }
            set { _pinned = value; ApplyTopMost(); SyncPinLook(); }
        }

        /// <summary>
        /// The main window is on top of everything ("Always on top", or full screen). This is a separate window,
        /// so it used to open UNDER it — usually right under it, both centred on the screen — and clicking
        /// Accounts seemed to do nothing at all. While the main window floats, so does this one; the pin is
        /// untouched and comes back into force when the main window stops floating.
        /// </summary>
        public bool CompanionTopMost
        {
            get { return _companionTopMost; }
            set { _companionTopMost = value; ApplyTopMost(); }
        }

        private void ApplyTopMost()
        {
            bool want = _pinned || _companionTopMost;
            if (TopMost != want) { TopMost = want; }
        }

        /// <summary>
        /// Raised after the window's size or position settles, so the caller can persist it.
        /// Not raised while minimised or maximised, where the bounds describe nothing useful.
        /// </summary>
        public event EventHandler<Rectangle> BoundsSettled;

        /// <summary>
        /// Raised when Windows starts (true) and finishes (false) moving or resizing this
        /// window.
        ///
        /// The main window already pauses its per-frame work — the animated logo, the
        /// wallpaper animator — for the length of its OWN drag, because the move loop has to
        /// paint through all of it. Dragging this window paused nothing: the animated logo
        /// went on setting a new icon on it about 15 times a second, and each of those
        /// repaints its frame and invalidates it. Measured, the window fell up to 60 px behind
        /// the cursor, on every run. The two windows share one UI thread, so the caller
        /// pauses the same work for this window's drag as for its own.
        /// </summary>
        public event EventHandler<bool> MoveLoopChanged;

        /// <summary>True while the user is dragging or resizing this window.</summary>
        public bool InMoveLoop => _inMoveLoop;
        private bool _inMoveLoop;

        public DetachedPageWindow(string title, Theme theme, Icon icon, NavIconKind glyph = NavIconKind.None)
        {
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);
            _scale = Math.Max(1f, DeviceDpi / 96f);

            Text = title ?? "";
            if (icon != null) { Icon = icon; }
            StartPosition = FormStartPosition.CenterScreen;
            // Wide enough for the widest screen it shows — the account list is laid out at 760 px, plus
            // a vertical scrollbar and the frame. At the old 620 the list scrolled sideways (measured: an
            // 813 px range in a 587 px page) and half of it was always out of view.
            MinimumSize = new Size(D(800), D(480));
            ClientSize = new Size(D(820), D(720));
            ShowInTaskbar = true;
            KeyPreview = true;
            BackColor = _theme.Background;
            ForeColor = _theme.Text;

            _strip = new HeaderStrip(this)
            {
                Dock = DockStyle.Top,
                Height = D(StripHeight),
                Title = title ?? "",
                Glyph = glyph,
            };

            _dockBtn = StripButton(Utils.Localization.T("Dock as a tab"));
            _dockBtn.Click += (s, e) => DockRequested?.Invoke(this, EventArgs.Empty);

            _pinBtn = StripButton(Utils.Localization.T("Always on top"));
            _pinBtn.Click += (s, e) =>
            {
                Pinned = !Pinned;
                TopMostChanged?.Invoke(this, Pinned);
            };

            _strip.Controls.Add(_pinBtn);
            _strip.Controls.Add(_dockBtn);
            _strip.Resize += (s, e) => LayoutStrip();

            _host = new ModernTabControl { Dock = DockStyle.Fill };

            // Fill first, Top second: WinForms docks in reverse z-order, so the strip has to be
            // added AFTER the fill control to end up above it rather than under it.
            Controls.Add(_host);
            Controls.Add(_strip);

            // The title bar in the theme from the first frame, not after the first re-theme —
            // otherwise the window opens with a system-coloured bar and then changes colour.
            ThemeManager.ApplyWindowChrome(this, _theme);

            LayoutStrip();
            ApplyTheme(_theme);
        }

        /// <summary>The hue for the header's glyph — the page's own sidebar colour.</summary>
        public Color GlyphColor
        {
            get => _strip.GlyphColor;
            set { _strip.GlyphColor = value; _strip.Invalidate(); }
        }

        private RoundedButton StripButton(string text)
        {
            var b = new RoundedButton
            {
                Text = text,
                Height = D(26),
                Width = D(120),
                CornerRadius = D(8),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                // Never Selected: on a RoundedButton that means "the active SIDEBAR tab" — a
                // left accent bar and accent-coloured text — which on an accent-filled pin
                // button would be accent text on an accent fill, i.e. invisible.
                Selected = false,
            };
            return b;
        }

        /// <summary>Right-aligns the two strip buttons and sizes each to its own text.</summary>
        private void LayoutStrip()
        {
            if (_strip == null || _pinBtn == null || _dockBtn == null) { return; }

            // Sized from the text, not a constant: "Always on top" is "Sempre in primo piano"
            // in Italian and "Toujours au premier plan" in French.
            UiFactory.FitToText(_dockBtn, D(88), D(22));
            UiFactory.FitToText(_pinBtn, D(88), D(22));

            int right = _strip.ClientSize.Width - D(10);
            int top = (_strip.Height - 1 - _dockBtn.Height) / 2;   // centred above the hairline
            _dockBtn.Top = top;
            _dockBtn.Left = right - _dockBtn.Width;
            _pinBtn.Top = top;
            _pinBtn.Left = _dockBtn.Left - D(8) - _pinBtn.Width;

            // The title gives way to the buttons rather than running under them.
            _strip.TitleRight = _pinBtn.Left - D(12);
            _strip.Invalidate();
        }

        private void SyncPinLook()
        {
            if (_pinBtn == null || _theme == null) { return; }
            _pinBtn.Selected = false;
            _pinBtn.AccentColor = _theme.Accent;
            // OnAccent, not AccentText: one is the readable ink for text sitting ON the accent
            // fill, the other is the accent made readable against the PAGE. Using the second on
            // a filled button is how an accent-on-accent, unreadable pill happens.
            // The pin, not TopMost: the window also floats while the main window does (CompanionTopMost), and
            // the button must not claim the user pinned it.
            _pinBtn.BackColor = _pinned ? _theme.Accent : _theme.Surface2;
            _pinBtn.ForeColor = _pinned ? _theme.OnAccent : _theme.Text;
            _pinBtn.FlatAppearance.BorderSize = _pinned ? 0 : 1;
            _pinBtn.FlatAppearance.BorderColor = _theme.Border;
            // Screen readers get the state too, not just the colour.
            _pinBtn.AccessibleDescription = _pinned
                ? Utils.Localization.T("On")
                : Utils.Localization.T("Off");
            _pinBtn.Invalidate();
        }

        /// <summary>Puts <paramref name="page"/> into this window. Pass null to give it up again.</summary>
        public void HostPage(BackdropTabPage page)
        {
            if (page == null)
            {
                _host.TabPages.Clear();
                return;
            }
            if (_host.TabPages.Contains(page)) { return; }
            _host.TabPages.Clear();
            _host.TabPages.Add(page);
            _host.SelectedTab = page;
            page.ApplyTheme(_theme);
            ApplyBackdropToPage();
        }

        /// <summary>The page currently on show here, or null.</summary>
        public BackdropTabPage Page => _host.TabPages.Count > 0 ? _host.TabPages[0] as BackdropTabPage : null;

        /// <summary>
        /// Themes this window's OWN parts: the title bar, the header, its two buttons, the
        /// host control, the page's base colour and the native scrollbars.
        ///
        /// It does not walk the page's content. That belongs to the caller, which knows which
        /// colours on it carry meaning (an error line, the primary button) and has to restore
        /// them after any generic pass — a generic walk from here would reset them first.
        /// </summary>
        public void ApplyTheme(Theme theme)
        {
            if (theme == null || IsDisposed) { return; }
            _theme = theme;

            BackColor = theme.Background;
            ForeColor = theme.Text;
            ThemeManager.ApplyWindowChrome(this, theme);

            _strip.ApplyTheme(theme);

            // The dock button is the quiet one; the pin shows its state in its fill.
            _dockBtn.Selected = false;
            _dockBtn.AccentColor = theme.Accent;
            _dockBtn.BackColor = theme.Surface2;
            _dockBtn.ForeColor = theme.Text;
            _dockBtn.FlatAppearance.BorderSize = 1;
            _dockBtn.FlatAppearance.BorderColor = theme.Border;
            _dockBtn.Invalidate();
            SyncPinLook();

            _host.ApplyTheme(theme);
            // After the host: ModernTabControl.ApplyTheme sets every page's BackColor to the
            // plain background, and a BackdropTabPage keeps its own base for the wallpaper scrim.
            Page?.ApplyTheme(theme);

            ApplyNativeScrollbars();
            LayoutStrip();
            Invalidate(true);
        }

        /// <summary>
        /// Dark or light native scrollbars to match. The page scrolls (a long account list),
        /// and a light Win32 scrollbar down the side of a dark window was the loudest thing
        /// left in it that the theme did not reach.
        /// </summary>
        private void ApplyNativeScrollbars()
        {
            if (!IsHandleCreated || _theme == null) { return; }
            try
            {
                bool dark = _theme.Background.GetBrightness() < 0.5f;
                NativeChrome.ApplyAllScrollbarThemes(this, dark);
            }
            catch (Exception ex) { Utils.Logger.Swallow("DetachedPageWindow scrollbars", ex); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyNativeScrollbars();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            // A move loop cannot normally outlive its window, but if it ever did, the pause
            // it started must not: the main window would stop animating for good.
            EndMoveLoop();
            base.OnHandleDestroyed(e);
        }

        // ── moving and resizing ──────────────────────────────────────────────────

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_ENTERSIZEMOVE = 0x0231;
        private const int WM_EXITSIZEMOVE = 0x0232;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int HTTRANSPARENT = -1;

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_ENTERSIZEMOVE:
                    BeginMoveLoop();
                    break;

                case WM_EXITSIZEMOVE:
                    EndMoveLoop();
                    break;

                case WM_NCHITTEST:
                    // The header strip is a title bar too. It looks like one — the page's icon
                    // and name in a bar across the top — so it is where people reach to move the
                    // window, and it did nothing. Answering HTCAPTION here (the strip passes the
                    // point through with HTTRANSPARENT) hands the whole gesture to Windows:
                    // dragging, double-click to maximise, Aero Snap and the right-click window
                    // menu all behave exactly as they do on the real title bar. The two buttons
                    // are windows of their own, so their clicks never reach this.
                    base.WndProc(ref m);
                    if (m.Result == (IntPtr)HTCLIENT && IsOverStrip(m.LParam))
                    {
                        m.Result = (IntPtr)HTCAPTION;
                    }
                    return;
            }
            base.WndProc(ref m);
        }

        /// <summary>Whether a WM_NCHITTEST point (screen coordinates) is on the header strip.</summary>
        private bool IsOverStrip(IntPtr lParam)
        {
            if (_strip == null || _strip.IsDisposed || !_strip.Visible) { return false; }
            try
            {
                // Two SIGNED 16-bit values: a monitor left of the primary has negative x.
                long lp = lParam.ToInt64();
                var screen = new Point(unchecked((short)(lp & 0xFFFF)), unchecked((short)((lp >> 16) & 0xFFFF)));
                return _strip.ClientRectangle.Contains(_strip.PointToClient(screen));
            }
            catch { return false; }
        }

        private void BeginMoveLoop()
        {
            if (_inMoveLoop) { return; }       // Windows can send WM_ENTERSIZEMOVE twice for one gesture
            _inMoveLoop = true;
            // A composited window repaints whole on every step of a move. Compositing only
            // exists to keep SCROLLING over the wallpaper clean, and nothing scrolls mid-drag.
            try { _compositeOff?.Stop(); } catch { }
            SetComposited(false);
            try { MoveLoopChanged?.Invoke(this, true); }
            catch (Exception ex) { Utils.Logger.Swallow("DetachedPageWindow.MoveLoopChanged(start)", ex); }
        }

        private void EndMoveLoop()
        {
            if (!_inMoveLoop) { return; }
            _inMoveLoop = false;
            try { MoveLoopChanged?.Invoke(this, false); }
            catch (Exception ex) { Utils.Logger.Swallow("DetachedPageWindow.MoveLoopChanged(end)", ex); }
        }

        // ── the wallpaper ────────────────────────────────────────────────────────

        /// <summary>
        /// Shows Tempo's wallpaper behind this window's header and page, cover-fitted to THIS
        /// window, or clears it with null. The image is the main window's shared instance:
        /// it is never disposed here and never given a second animator.
        /// </summary>
        public void SetBackdrop(Image img, int dimPercent)
        {
            // The same picture with a new Dim (the Settings slider, many times a second while it is
            // dragged): only the scrim changes. Re-applying the backdrop re-themed every label on the page.
            if (img != null && ReferenceEquals(img, _backdrop))
            {
                if (dimPercent == _backdropDim) { return; }
                _backdropDim = dimPercent;
                _strip.Invalidate();
                BackdropTabPage page = Page;
                if (page != null)
                {
                    page.SetDim(dimPercent);
                    page.Invalidate(true);   // the see-through labels draw their own slice of it
                }
                return;
            }
            _backdrop = img;
            _backdropDim = dimPercent;
            _strip.Invalidate();
            ApplyBackdropToPage();
            if (img == null) { SetComposited(false); }
        }

        /// <summary>True while a wallpaper is showing here.</summary>
        public bool HasBackdrop => _backdrop != null;

        private void ApplyBackdropToPage()
        {
            BackdropTabPage page = Page;
            if (page == null) { return; }
            page.SetBackdrop(_backdrop, _backdropDim);
            page.SetActive(_backdrop != null);
            // Labels directly on the page go see-through over a wallpaper and solid without
            // one — the same rule the main window's pages follow.
            try { ThemeManager.RefreshBackdropBackgrounds(page, _theme); } catch { }
        }

        /// <summary>One animation frame: repaint the parts that show the wallpaper.</summary>
        public void InvalidateBackdrop()
        {
            if (_backdrop == null || IsDisposed || !Visible || WindowState == FormWindowState.Minimized) { return; }
            _strip.Invalidate();
            Page?.InvalidateBackdrop();
        }

        // Scrolling over a pinned wallpaper leaves blit remnants under see-through labels
        // unless the window is WS_EX_COMPOSITED for the duration — and holding that style
        // permanently costs most of a CPU core. So, exactly as the main window does, it is
        // armed by the page's scroll and lapses shortly after the scrolling stops.
        private System.Windows.Forms.Timer _compositeOff;
        private bool _composited;

        void IBackdropHost.NotifyBackdropScroll()
        {
            if (_backdrop == null || IsDisposed) { return; }
            SetComposited(true);
            if (_compositeOff == null)
            {
                _compositeOff = new System.Windows.Forms.Timer { Interval = 700 };
                _compositeOff.Tick += (s, e) => { _compositeOff.Stop(); SetComposited(false); };
            }
            _compositeOff.Stop();
            _compositeOff.Start();
        }

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_COMPOSITED = 0x02000000;
        private const uint SWP_FLAGS = 0x0002 | 0x0001 | 0x0004 | 0x0010 | 0x0020;   // no move/size/z/activate + frame

        private void SetComposited(bool on)
        {
            if (_composited == on || !IsHandleCreated || IsDisposed) { return; }
            _composited = on;
            try
            {
                int ex = GetWindowLong(Handle, GWL_EXSTYLE);
                int want = on ? (ex | WS_EX_COMPOSITED) : (ex & ~WS_EX_COMPOSITED);
                if (want != ex)
                {
                    SetWindowLong(Handle, GWL_EXSTYLE, want);
                    SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_FLAGS);
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("DetachedPageWindow composited", ex); }
        }

        // ── placement ────────────────────────────────────────────────────────────

        /// <summary>
        /// Restores a remembered position, but only onto a monitor that is still there.
        /// A saved rectangle from a second screen that has since been unplugged would put the
        /// window somewhere nobody can reach it, so an off-screen one is dropped and the window
        /// centres on <paramref name="home"/> instead — the working area of the screen the main
        /// window (or the tray click) is on, where a window never placed goes too.
        ///
        /// And always INSIDE a working area, never larger than it. The first size is 820 × 720 at
        /// 100% — 1025 × 900 at 125% — and a 1366 × 768 laptop has 728 px of desk: centred, the
        /// window began above the top of the screen, its header strip and buttons with it ("clicked
        /// Accounts and didn't get it"). A size saved on a big monitor came back as large on a laptop.
        /// </summary>
        // Not "RestoreBounds": that name is Form's own property (the normal-state rectangle of a
        // maximised window), and a method called the same silently hid it on this class.
        public void RestoreSavedBounds(int left, int top, int width, int height, Rectangle home)
        {
            Rectangle area = home;
            Rectangle wanted;
            if (width > 0 && height > 0)
            {
                if (width < MinimumSize.Width) { width = MinimumSize.Width; }
                if (height < MinimumSize.Height) { height = MinimumSize.Height; }
                wanted = new Rectangle(left, top, width, height);

                // The screen that shows most of it, if one shows enough of it to grab: a sliver is not "reachable".
                Screen best = null;
                long bestArea = 0;
                foreach (Screen sc in Screen.AllScreens)
                {
                    Rectangle overlap = Rectangle.Intersect(sc.WorkingArea, wanted);
                    if (overlap.Width < 120 || overlap.Height < 40) { continue; }
                    long a = (long)overlap.Width * overlap.Height;
                    if (a > bestArea) { bestArea = a; best = sc; }
                }
                if (best != null) { area = best.WorkingArea; }
                else { wanted = Centred(wanted.Size, home); }   // on a monitor that has gone: centre on home
            }
            else
            {
                wanted = Centred(Size, home);   // never placed: the first size, centred on home
            }

            if (area.Width <= 0 || area.Height <= 0) { area = Screen.PrimaryScreen.WorkingArea; }
            // A minimum the desk cannot hold is brought down to it, or WinForms would grow the window back out.
            if (MinimumSize.Width > area.Width || MinimumSize.Height > area.Height)
            {
                MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
            }
            int w = Math.Min(wanted.Width, area.Width);
            int h = Math.Min(wanted.Height, area.Height);
            int x = Math.Max(area.Left, Math.Min(wanted.Left, area.Right - w));
            int y = Math.Max(area.Top, Math.Min(wanted.Top, area.Bottom - h));
            // Size, not ClientSize: these are OUTER bounds. Assigning them as the client area grew the window by its
            // frame and title bar every time.
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(x, y, w, h);
        }

        private static Rectangle Centred(Size size, Rectangle area)
        {
            return new Rectangle(area.Left + (area.Width - size.Width) / 2, area.Top + (area.Height - size.Height) / 2,
                                 size.Width, size.Height);
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            ReportBounds();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LayoutStrip();
            ApplyNativeScrollbars();
        }

        private void ReportBounds()
        {
            if (WindowState != FormWindowState.Normal) { return; }
            BoundsSettled?.Invoke(this, Bounds);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Capture the place BEFORE the handle goes, or the bounds read back as the
            // default and every close would forget where the window was.
            ReportBounds();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _compositeOff?.Stop(); _compositeOff?.Dispose(); } catch { }
                _compositeOff = null;
            }
            base.Dispose(disposing);
        }

        // NB: nothing is rescued from the page here on purpose. WinForms raises FormClosed and
        // only then disposes the form, so a listener gets its turn first — that is where the
        // caller moves its own controls off this window's page. The page itself belongs to this
        // window and is meant to go with it; the next detach builds a fresh one.

        /// <summary>
        /// The header: the page's glyph and title on the theme's surface colour (or the
        /// window's slice of the wallpaper under a readability scrim), with a hairline under
        /// it, and the two buttons as its only children.
        /// </summary>
        private sealed class HeaderStrip : Panel
        {
            private readonly DetachedPageWindow _owner;
            private Theme _theme;

            public string Title { get; set; } = "";
            public NavIconKind Glyph { get; set; } = NavIconKind.None;
            public Color GlyphColor { get; set; } = Color.Empty;

            /// <summary>The x the title must end before — the left edge of the buttons.</summary>
            public int TitleRight { get; set; } = int.MaxValue;

            public HeaderStrip(DetachedPageWindow owner)
            {
                _owner = owner;
                DoubleBuffered = true;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                         | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            }

            /// <summary>
            /// See-through to hit-testing, so a press on the strip reaches the window, which
            /// answers "title bar" (see DetachedPageWindow.WndProc). Only the strip's own
            /// surface: its buttons are child windows and are hit-tested on their own.
            /// </summary>
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_NCHITTEST && _owner != null && !_owner.IsDisposed)
                {
                    m.Result = (IntPtr)HTTRANSPARENT;
                    return;
                }
                base.WndProc(ref m);
            }

            public void ApplyTheme(Theme theme)
            {
                _theme = theme;
                BackColor = theme.Surface;
                ForeColor = theme.Text;
                Invalidate();
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                Theme t = _theme ?? Theme.ForKind(Models.ThemeKind.Dark);
                // The window's own slice of the shared wallpaper, cover-fitted to THIS window
                // so it lines up with the page below; the flat surface colour without one.
                if (_owner._backdrop == null
                    || !WindowBackdrop.Paint(e.Graphics, this, _owner._backdrop, _owner._backdropDim, t.Surface))
                {
                    using (var b = new SolidBrush(t.Surface))
                    {
                        e.Graphics.FillRectangle(b, ClientRectangle);
                    }
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Theme t = _theme ?? Theme.ForKind(Models.ThemeKind.Dark);
                Graphics g = e.Graphics;

                // Hairline between the header and the page, the same seam the main window's
                // header has — without it a Surface header on a Background page read as a
                // band that had been painted slightly the wrong colour.
                using (var pen = new Pen(t.Border, 1))
                {
                    g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
                }

                float k = _owner != null ? _owner._scale : 1f;
                int x = (int)Math.Round(14 * k);
                if (Glyph != NavIconKind.None)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float size = 18f * k;
                    var box = new RectangleF(x, (Height - 1 - size) / 2f, size, size);
                    NavIcons.Draw(g, Glyph, box, GlyphColor.IsEmpty ? t.Accent : GlyphColor);
                    x = (int)(box.Right + 9 * k);
                }

                int right = Math.Min(TitleRight, Width - (int)Math.Round(12 * k));
                if (right - x > 20 && !string.IsNullOrEmpty(Title))
                {
                    using (var font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold))
                    {
                        var rect = new Rectangle(x, 0, right - x, Height - 1);
                        TextRenderer.DrawText(g, Title, font, rect, t.Text,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    }
                }
            }
        }
    }
}
