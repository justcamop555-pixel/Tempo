using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>What kind of notification this is — drives the accent colour and glyph.</summary>
    public enum ToastKind { Info, Success, Warning, Error, Mirror }

    /// <summary>
    /// A single animated notification card, styled to read like a Windows 11 toast:
    /// a top row with the SOURCE APP'S ICON + its name + a close (✕) button, then a
    /// bold title, then the description. Borderless, top-most, never steals focus.
    /// It slides in from the screen edge with a fade, sits while a thin progress bar
    /// drains, then slides out. Hovering pauses the countdown; a click dismisses it.
    ///
    /// The <see cref="NotificationCenter"/> owns a stack of these and tells each one
    /// where to sit (<see cref="MoveTo"/>) as cards come and go.
    /// </summary>
    public sealed class NotificationToastForm : Form
    {
        private readonly Theme _theme;
        private string _appName;
        private readonly string _title;
        private string _body;
        private readonly ToastKind _kind;
        private int _corner;                   // 0 TR, 1 TL, 2 BR, 3 BL — live, can change
        private readonly int _dwellMs;
        private Image _icon;                   // source-app icon (owned; disposed on close)
        private readonly Image _hero;          // large picture below the text (owned; optional)
        private Action _onActivate;            // run when the card body is clicked (open the app)

        // Fonts (disposed on close).
        private readonly Font _appFont;
        private readonly Font _titleFont;
        private readonly Font _bodyFont;
        private readonly Font _glyphFont;
        private readonly Font _closeFont;

        // Geometry — a Windows 11-like card.
        private const int CardWidth = 386;
        private const int Pad = 18;            // was 16 — the text sat tight to the edges
        private const int IconSize = 26;       // app icon
        private const int CloseBox = 22;       // ✕ button (bigger hit target)
        private const int CloseInset = 4;      // keeps ✕ off the rounded corner
        private const int ProgressH = 2;       // slimmer — a timer, not a divider
        private const int Radius = 11;
        private const int HeroMaxH = 150;      // max height of the picture below the text

        // Animation.
        private readonly Timer _anim;
        private enum Phase { In, Dwell, Out }
        private Phase _phase = Phase.In;
        private double _x;                     // current left (float for easing)
        private double _y;                     // current top
        private int _targetX;
        private int _targetY;
        private double _alpha;                 // 0..1
        private int _remainingMs;              // dwell countdown
        // Last values actually pushed to the window, so a tick that changes nothing
        // costs nothing (see OnAnimTick).
        private double _lastAppliedAlpha = -1;
        private Point _lastAppliedPoint = new Point(int.MinValue, int.MinValue);
        private long _lastBarPaintTick;
        private bool _hovered;

        // ── Time-based motion ──────────────────────────────────────────────────
        //
        // The slide and fade used to be frame-COUNTED: each tick moved a fixed
        // FRACTION of the remaining distance (x += (target - x) * 0.62). That only
        // looks right if every frame arrives on time, and these frames do not — the
        // animation runs on a WinForms Timer, whose WM_TIMER is the lowest-priority
        // message Windows delivers, handed over only when the queue is otherwise
        // empty. Any busy moment therefore both DROPPED frames and made the card
        // travel a different distance per frame, so the motion visibly hitched and
        // sped up and slowed down.
        //
        // Driving position and alpha from ELAPSED TIME instead makes the animation
        // take the same wall-clock duration and follow the same curve no matter how
        // the frames actually land: a late frame just draws the card where it should
        // be by then, rather than restarting the easing from wherever it got to.
        private const int SlideInMs = 240;
        private const int SlideOutMs = 190;
        private const int ReflowMs = 260;   // easing to a new stack slot

        private long _phaseStartTick;       // when In/Out began
        private double _fromX;              // x when the current phase started
        private double _fromAlpha;
        private long _yTweenStartTick;      // reflow tween (retargets independently)
        private double _fromY;
        private int _yTweenTarget = int.MinValue;

        /// <summary>Ease-out cubic — fast departure, soft landing. The Windows 11 feel.</summary>
        private static double EaseOut(double t)
        {
            if (t <= 0) { return 0; }
            if (t >= 1) { return 1; }
            double inv = 1 - t;
            return 1 - inv * inv * inv;
        }

        /// <summary>Ease-in quad for the exit: it leaves a little more decisively.</summary>
        private static double EaseIn(double t)
        {
            if (t <= 0) { return 0; }
            if (t >= 1) { return 1; }
            return t * t;
        }

        private long _lastTickTick;

        /// <summary>
        /// Starts a phase: stamps the clock, records where the eased values start from,
        /// and matches the tick rate to what that phase actually needs.
        ///
        /// Dwell is by far the longest phase and the card is STATIONARY throughout it —
        /// only the countdown bar drains. Ticking it at 60 fps woke the UI thread ~4x
        /// more often than the bar can show, for every card on screen at once.
        /// </summary>
        private void EnterPhase(Phase next)
        {
            _phase = next;
            _phaseStartTick = Environment.TickCount64;
            _fromX = _x;
            _fromAlpha = _alpha;

            int want = next == Phase.Dwell ? 60 : 16;
            if (_anim.Interval != want) { _anim.Interval = want; }
        }

        /// <summary>
        /// Whether cards draw a ✕. Set once from settings (see NotificationCenter) rather
        /// than threaded through every Notify call — it is a global look preference, not a
        /// per-notification one. With it off the card still dismisses on click and still
        /// auto-times-out, so nothing becomes unreachable.
        /// </summary>
        public static bool ShowCloseButton { get; set; } = true;

        /// <summary>Raised (on the UI thread) when the card has fully closed.</summary>
        public event Action<NotificationToastForm> Dismissed;

        /// <summary>
        /// Which corner this card belongs to. The centre updates this live during a
        /// reflow so that switching the corner setting migrates ALL on-screen cards to
        /// the new corner together (they ease across), instead of stranding the older
        /// cards in the previous corner.
        /// </summary>
        public int Corner
        {
            get => _corner;
            set => _corner = (value < 0 || value > 3) ? 0 : value;
        }

        public NotificationToastForm(Theme theme, string appName, string title,
                                     string body, ToastKind kind, int corner, int dwellMs,
                                     Image icon, Image hero, Action onActivate)
        {
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);
            _appName = appName ?? "";
            _title = title ?? "";
            _body = body ?? "";
            _kind = kind;
            _corner = corner < 0 || corner > 3 ? 0 : corner;
            _dwellMs = Math.Max(1500, dwellMs);
            _remainingMs = _dwellMs;
            _icon = icon;
            _hero = hero;
            _onActivate = onActivate;

            AutoScaleMode = AutoScaleMode.None;   // raw screen pixels, like the other overlays
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = _theme.Surface;
            Opacity = 0;
            // A hand cursor signals the whole card opens the app when clicked (Windows 11).
            Cursor = onActivate != null ? Cursors.Hand : Cursors.Default;

            _appFont = new Font("Segoe UI", 9f, FontStyle.Regular);
            _titleFont = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
            _bodyFont = new Font("Segoe UI", 9.25f, FontStyle.Regular);
            _glyphFont = new Font("Segoe UI Symbol", 12f, FontStyle.Bold);
            _closeFont = new Font("Segoe UI", 9f, FontStyle.Regular);

            Width = CardWidth;
            Height = MeasureHeight();

            _anim = new Timer { Interval = 16 };   // ~60 fps
            _anim.Tick += OnAnimTick;
        }

        // Never take focus from the game / app the user is looking at.
        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;   // keep out of Alt-Tab / taskbar
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        /// <summary>Accent colour for this kind, from the active theme.</summary>
        private Color Accent
        {
            get
            {
                switch (_kind)
                {
                    case ToastKind.Success: return _theme.Success;
                    case ToastKind.Warning: return _theme.Warning;
                    case ToastKind.Error: return _theme.Danger;
                    default: return _theme.Accent;   // Info + Mirror
                }
            }
        }

        // Fallback glyph when the app didn't provide an icon.
        private string Glyph
        {
            get
            {
                switch (_kind)
                {
                    case ToastKind.Success: return "✔";
                    case ToastKind.Warning: return "⚠";
                    case ToastKind.Error: return "✖";
                    case ToastKind.Mirror: return "🔔";
                    default: return "🔔";
                }
            }
        }

        private int TextWidth => Width - Pad * 2;

        /// <summary>
        /// The size the hero picture is drawn at: fit inside (content-width × HeroMaxH)
        /// preserving aspect ratio. Zero when there's no picture.
        /// </summary>
        private Size HeroDrawSize()
        {
            if (_hero == null || _hero.Width <= 0 || _hero.Height <= 0) { return Size.Empty; }
            int maxW = TextWidth;
            double scale = Math.Min((double)maxW / _hero.Width, (double)HeroMaxH / _hero.Height);
            if (scale > 1.0) { scale = 1.0; }   // never upscale past native size
            int w = Math.Max(1, (int)Math.Round(_hero.Width * scale));
            int h = Math.Max(1, (int)Math.Round(_hero.Height * scale));
            return new Size(w, h);
        }

        /// <summary>Measures the card height for the current text (top row + title + body + picture).</summary>
        private int MeasureHeight()
        {
            using (var g = CreateGraphics())
            {
                int tw = TextWidth;
                var wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;

                int topRow = Math.Max(IconSize, _appFont.Height);
                int titleH = TextRenderer.MeasureText(g, _title, _titleFont, new Size(tw, 0), wrap).Height;
                int bodyLine = _bodyFont.Height;
                int bodyH = string.IsNullOrEmpty(_body)
                    ? 0
                    : Math.Min(bodyLine * 3,
                        TextRenderer.MeasureText(g, _body, _bodyFont, new Size(tw, 0), wrap).Height);

                int h = Pad + topRow + 8 + titleH;
                if (bodyH > 0) { h += 3 + bodyH; }
                var hero = HeroDrawSize();
                if (hero.Height > 0) { h += 10 + hero.Height; }
                h += Pad + ProgressH;
                return h;
            }
        }

        // ── positioning (called by the centre) ──────────────────────────────────

        /// <summary>The X the card rests at, for the current corner and screen.</summary>
        public int RestingX(Rectangle wa)
        {
            bool right = _corner == 0 || _corner == 2;
            const int margin = 18;
            return right ? wa.Right - Width - margin : wa.Left + margin;
        }

        /// <summary>
        /// Assigns the resting position. On the first call the card starts just off its
        /// resting spot and slides in; later calls retarget it and it eases there (how
        /// the stack reflows and how cards migrate when the corner changes).
        /// </summary>
        public void MoveTo(int x, int y, bool firstShow)
        {
            _targetX = x;
            _targetY = y;
            if (firstShow)
            {
                bool right = _corner == 0 || _corner == 2;
                _x = right ? x + 60 : x - 60;
                _y = y;
                try { Location = new Point((int)_x, (int)_y); } catch { }
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            OverlayTopmost.Register(Handle);   // stay above fullscreen games / video
            // Stamp the clock HERE, not in the constructor: the gap between building the
            // card and actually showing it (icon decode, layout) would otherwise be
            // counted as animation time already elapsed, and the card would jump part-way
            // through its slide on the very first frame.
            _y = _targetY;
            _yTweenTarget = _targetY;
            EnterPhase(Phase.In);
            _anim.Start();
        }

        /// <summary>
        /// Replaces what a click on this card does. Paired with <see cref="UpdateSource"/>
        /// so a card shown before its source app was known can later open the file in
        /// that app instead of the default handler.
        /// </summary>
        public void SetActivate(Action onActivate)
        {
            _onActivate = onActivate;
        }

        /// <summary>
        /// Re-labels an ALREADY VISIBLE card with the app that really produced it, and
        /// swaps in that app's icon. This is what lets a screenshot card appear instantly
        /// (before the capture app's own notification has even arrived) and still end up
        /// wearing "Snipping Tool" rather than "Tempo": the card is shown at once and
        /// upgraded a fraction of a second later, instead of being delayed to wait for
        /// the identity. Takes ownership of <paramref name="icon"/>.
        /// </summary>
        public void UpdateSource(string appName, Image icon, string body)
        {
            try
            {
                if (IsDisposed) { icon?.Dispose(); return; }
                if (InvokeRequired)
                {
                    BeginInvoke((Action)(() => UpdateSource(appName, icon, body)));
                    return;
                }
                if (!string.IsNullOrWhiteSpace(appName)) { _appName = appName; }
                if (body != null) { _body = body; }
                if (icon != null)
                {
                    Image old = _icon;
                    _icon = icon;
                    // Never dispose an image the hero slot is also using.
                    if (old != null && !ReferenceEquals(old, _hero)) { try { old.Dispose(); } catch { } }
                }
                Invalidate();
            }
            catch { try { icon?.Dispose(); } catch { } }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            OverlayTopmost.Unregister(Handle);
            _anim.Stop();
            _anim.Dispose();
            _appFont?.Dispose();
            _titleFont?.Dispose();
            _bodyFont?.Dispose();
            _glyphFont?.Dispose();
            _closeFont?.Dispose();
            _icon?.Dispose();
            if (_hero != null && !ReferenceEquals(_hero, _icon)) { _hero.Dispose(); }
            base.OnFormClosed(e);
        }

        /// <summary>Begins the slide-out; the card closes itself when it finishes.</summary>
        /// <summary>The app, title and body this card is showing — used to spot a repeat.</summary>
        internal bool Matches(string appName, string title, string body)
        {
            // ALL THREE must match. An earlier version used "app OR (title AND body)",
            // which would have collapsed every unrelated notification from the same app
            // into one card — the opposite of the point.
            return string.Equals(_appName ?? "", appName ?? "", StringComparison.Ordinal)
                   && string.Equals(_title ?? "", title ?? "", StringComparison.Ordinal)
                   && string.Equals(_body ?? "", body ?? "", StringComparison.Ordinal);
        }

        private int _repeats = 1;

        /// <summary>
        /// The same message arrived again while this card was still on screen. Rather
        /// than stack an identical copy, count it and restart the dwell so the card
        /// stays as long as it would have.
        ///
        /// This is the "we already know, it kept telling us" case: a warning that fires
        /// on a loop used to produce a column of identical cards, each demanding the same
        /// attention for the same fact.
        /// </summary>
        internal void Repeat(int dwellMs)
        {
            _repeats++;
            _remainingMs = Math.Max(_remainingMs, dwellMs);
            if (_phase == Phase.Out) { EnterPhase(Phase.Dwell); }
            Invalidate();
        }

        /// <summary>The "×3" marker, or empty for a card that has only arrived once.</summary>
        private string RepeatBadge => _repeats > 1 ? "  ×" + _repeats : "";

        public void BeginDismiss()
        {
            // Must go through EnterPhase: the exit is timed from the moment it starts, so
            // assigning the phase alone would leave the previous phase's stamp in place
            // and the card would vanish on the next frame instead of easing out.
            if (_phase != Phase.Out) { EnterPhase(Phase.Out); }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            Invalidate();
        }

        /// <summary>
        /// The clickable ✕ region (a little larger than the drawn glyph). Follows the
        /// button to the LEFT edge, and collapses to nothing when the ✕ is hidden — an
        /// invisible dead zone that still swallowed clicks would be worse than no button.
        /// </summary>
        private Rectangle CloseHitRect =>
            ShowCloseButton
                ? new Rectangle(Pad + CloseInset - 5, Pad - 2, CloseBox + 10, CloseBox + 10)
                : Rectangle.Empty;

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            // Clicking the ✕ only dismisses; clicking the body opens the app that sent
            // the notification (Windows 11 behaviour), then dismisses.
            if (CloseHitRect.Contains(e.Location))
            {
                BeginDismiss();
                return;
            }
            if (_onActivate != null)
            {
                var act = _onActivate;
                try { act(); } catch { }
            }
            BeginDismiss();
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            long now = Environment.TickCount64;
            long sinceLast = _lastTickTick == 0 ? _anim.Interval : now - _lastTickTick;
            _lastTickTick = now;

            switch (_phase)
            {
                case Phase.In:
                {
                    double t = (now - _phaseStartTick) / (double)SlideInMs;
                    double k = EaseOut(t);
                    _x = _fromX + (_targetX - _fromX) * k;
                    _alpha = _fromAlpha + (1.0 - _fromAlpha) * k;
                    if (t >= 1)
                    {
                        _x = _targetX;
                        _alpha = 1.0;
                        EnterPhase(Phase.Dwell);
                    }
                    break;
                }

                case Phase.Dwell:
                    _x = _targetX;
                    if (!_hovered)
                    {
                        // Count down by REAL elapsed time. Subtracting the timer's
                        // nominal interval assumed every tick arrived exactly on
                        // schedule, so a card that lost frames sat there longer than
                        // its dwell — and the progress bar drifted out of step with it.
                        _remainingMs -= (int)Math.Min(sinceLast, 500);
                        if (_remainingMs <= 0) { EnterPhase(Phase.Out); }
                    }
                    break;

                case Phase.Out:
                {
                    bool right = _corner == 0 || _corner == 2;
                    double off = right ? _targetX + 80 : _targetX - 80;
                    double t = (now - _phaseStartTick) / (double)SlideOutMs;
                    double k = EaseIn(t);
                    _x = _fromX + (off - _fromX) * k;
                    _alpha = _fromAlpha * (1 - k);
                    if (t >= 1)
                    {
                        Close();
                        Dismissed?.Invoke(this);
                        return;
                    }
                    break;
                }
            }

            // Ease vertically toward the centre-assigned slot (stack reflow / migration).
            // Retargeted whenever the stack reflows, so it restarts its own tween from
            // wherever the card currently is — never a jump.
            if (_targetY != _yTweenTarget)
            {
                _yTweenTarget = _targetY;
                _fromY = _y;
                _yTweenStartTick = now;
            }
            if (Math.Abs(_y - _targetY) > 0.4)
            {
                double ty = (now - _yTweenStartTick) / (double)ReflowMs;
                _y = _fromY + (_targetY - _fromY) * EaseOut(ty);
                if (ty >= 1) { _y = _targetY; }
            }
            else
            {
                _y = _targetY;
            }

            try
            {
                // Only touch the window when something actually CHANGED.
                //
                // This ran three expensive calls 60 times a second for the whole life of
                // the card: Opacity drives a layered-window update, Location is a
                // SetWindowPos, and Invalidate() repainted the entire card — gradients,
                // rounded paths, icon and all. During Dwell, which is most of the card's
                // life, the position and alpha have already converged, so all three were
                // repeating identical work. With several toasts stacked that is a lot of
                // redundant compositing competing with the main window for the UI thread,
                // which is what made the animation feel like it stuttered.
                double alpha = Math.Max(0, Math.Min(1, _alpha));
                if (Math.Abs(alpha - _lastAppliedAlpha) > 0.004)
                {
                    _lastAppliedAlpha = alpha;
                    Opacity = alpha;
                }

                var pt = new Point((int)Math.Round(_x), (int)Math.Round(_y));
                if (pt != _lastAppliedPoint)
                {
                    _lastAppliedPoint = pt;
                    Location = pt;
                }

                // NOTHING the card paints depends on where it is or how transparent it
                // is: OnPaint/OnPaintBackground read only the hover state, the theme,
                // the content and the countdown. Moving the window is a blit Windows
                // does itself, and the fade is layered-window alpha applied by the
                // compositor — neither needs a single pixel redrawn. So the whole-card
                // Invalidate() that used to run on every frame of the slide and the fade
                // was redrawing gradients, rounded paths, the icon and the hero image
                // 60 times a second to produce an identical bitmap.
                //
                // The one thing that genuinely changes is the draining countdown bar, so
                // only that strip is invalidated, and only while it is actually draining.
                // 50 ms, not 66: the Dwell tick is itself 60 ms now, and a 66 ms gate
                // would skip every other one and drop the countdown to ~8 fps. This lets
                // each Dwell tick through, so the bar drains at the tick rate.
                if (_phase == Phase.Dwell && now - _lastBarPaintTick >= 50)
                {
                    _lastBarPaintTick = now;
                    Invalidate(new Rectangle(0, Height - ProgressH - 6, Width, ProgressH + 6));
                }
            }
            catch { /* handle destroyed mid-tick */ }
        }

        // ── painting ─────────────────────────────────────────────────────────────

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Lighten slightly on hover so the card reads as clickable (Windows 11 does
            // the same). Region-clipped to the rounded shape by the form.
            Color bg = _hovered && _onActivate != null
                ? Blend(_theme.Surface, _theme.Surface2, 0.55)
                : _theme.Surface;
            e.Graphics.Clear(bg);
        }

        private static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Region = RoundedRegion(Width, Height, Radius);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color accent = Accent;

            // Card border (Windows 11 style — no big coloured stripe).
            using (var path = RoundedPath(Width, Height, Radius))
            using (var border = new Pen(_theme.Border, 1))
            {
                g.DrawPath(border, path);
            }

            // ── Top row: [✕] [app icon] app name ───────────────────────────────
            // The ✕ lives on the LEFT now, inset from the rounded corner. It used to sit
            // hard against the top-right, which read as cramped and put it exactly where
            // the pointer travels when you click the card body to open the app — so a
            // mis-click dismissed the notification instead of opening it. Moving it left
            // separates "dismiss" from "open" and gives the corner room to breathe.
            bool showClose = ShowCloseButton;
            int closeLeft = Pad + CloseInset;
            int contentLeft = showClose ? closeLeft + CloseBox + 10 : Pad;
            var iconRect = new Rectangle(contentLeft, Pad, IconSize, IconSize);
            if (_icon != null)
            {
                // The DETECTED source-app icon, rounded to a squircle like Win11.
                using (var clip = RoundedRect(iconRect, 6))
                {
                    var saved = g.Save();
                    g.SetClip(clip, CombineMode.Intersect);
                    g.DrawImage(_icon, iconRect);
                    g.Restore(saved);
                }
            }
            else
            {
                // Fallback: a kind-tinted badge with a glyph.
                using (var fill = new SolidBrush(Color.FromArgb(42, accent)))
                {
                    g.FillPath(fill, RoundedRect(iconRect, 6));
                }
                using (var bp = new Pen(Color.FromArgb(150, accent), 1.2f))
                {
                    g.DrawPath(bp, RoundedRect(iconRect, 6));
                }
                TextRenderer.DrawText(g, Glyph, _glyphFont, iconRect, accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            int nameLeft = contentLeft + IconSize + 10;
            int nameWidth = Width - Pad - nameLeft;
            var nameRect = new Rectangle(nameLeft, Pad, Math.Max(10, nameWidth), IconSize);
            // The repeat marker rides on the small app line rather than the title: it is
            // metadata about the card, not part of what the message says.
            string appLabel = (string.IsNullOrEmpty(_appName) ? "Notification" : _appName) + RepeatBadge;
            TextRenderer.DrawText(g, appLabel, _appFont, nameRect, _theme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            // Close ✕ (brighter on hover, with a subtle hover pad — like Windows 11).
            // Hidden entirely when the user turns it off: the card still dismisses on a
            // click and still times out, so nothing becomes unreachable.
            var closeRect = new Rectangle(closeLeft, Pad + (IconSize - CloseBox) / 2, CloseBox, CloseBox);
            if (showClose && _hovered)
            {
                using (var hb = new SolidBrush(_theme.Surface2))
                {
                    g.FillPath(hb, RoundedRect(closeRect, 5));
                }
            }
            if (showClose)
            {
                TextRenderer.DrawText(g, "✕", _closeFont, closeRect,
                    _hovered ? _theme.Text : _theme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            // ── Title + body (full width, left-aligned under the icon) ─────────
            int tx = Pad;
            int tw = TextWidth;
            int y = Pad + Math.Max(IconSize, _appFont.Height) + 8;
            var wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

            int titleH = TextRenderer.MeasureText(g, _title, _titleFont, new Size(tw, 0),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
            TextRenderer.DrawText(g, _title, _titleFont, new Rectangle(tx, y, tw, titleH), _theme.Text, wrap);
            y += titleH;

            if (!string.IsNullOrEmpty(_body))
            {
                y += 5;   // was 3 — the body crowded the title
                int bodyH = Math.Min(_bodyFont.Height * 3,
                    TextRenderer.MeasureText(g, _body, _bodyFont, new Size(tw, 0),
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height);
                TextRenderer.DrawText(g, _body, _bodyFont, new Rectangle(tx, y, tw, bodyH),
                    _theme.TextMuted, wrap);
                y += bodyH;
            }

            // The picture (Windows 11 hero image), rounded, centred under the text.
            var heroSize = HeroDrawSize();
            if (heroSize.Height > 0 && _hero != null)
            {
                y += 10;
                int hx = tx + (tw - heroSize.Width) / 2;
                var heroRect = new Rectangle(hx, y, heroSize.Width, heroSize.Height);
                using (var clip = RoundedRect(heroRect, 8))
                {
                    var saved = g.Save();
                    g.SetClip(clip, CombineMode.Intersect);
                    g.DrawImage(_hero, heroRect);
                    g.Restore(saved);
                }
                using (var hb = new Pen(Color.FromArgb(40, _theme.Text), 1))
                {
                    g.DrawPath(hb, RoundedRect(heroRect, 8));
                }
            }

            // Draining progress bar along the bottom (time remaining). Slimmer than
            // before, rounded at the ends, and faded along its length so it reads as a
            // countdown rather than a hard divider under the card.
            double frac = Math.Max(0, Math.Min(1, _remainingMs / (double)_dwellMs));
            int barW = (int)((Width - Pad * 2) * frac);
            if (barW > 2)
            {
                var barRect = new Rectangle(Pad, Height - ProgressH - 3, barW, ProgressH);
                using (var track = new SolidBrush(Color.FromArgb(38, accent)))
                {
                    // A faint full-width track behind it shows how much time has gone.
                    g.FillPath(track, RoundedRect(
                        new Rectangle(Pad, barRect.Y, Width - Pad * 2, ProgressH), ProgressH / 2f));
                }
                using (var pb = new LinearGradientBrush(
                           new Rectangle(barRect.X, barRect.Y, Math.Max(2, barRect.Width), ProgressH),
                           Color.FromArgb(120, accent), Color.FromArgb(225, accent),
                           LinearGradientMode.Horizontal))
                {
                    g.FillPath(pb, RoundedRect(barRect, ProgressH / 2f));
                }
            }
        }

        private static GraphicsPath RoundedPath(int w, int h, int r)
        {
            var path = new GraphicsPath();
            int d = r * 2;
            path.AddArc(0, 0, d, d, 180, 90);
            path.AddArc(w - d - 1, 0, d, d, 270, 90);
            path.AddArc(w - d - 1, h - d - 1, d, d, 0, 90);
            path.AddArc(0, h - d - 1, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>Rounded rect with a fractional radius (the 2 px progress bar needs 1.0).</summary>
        private static GraphicsPath RoundedRect(Rectangle rect, float r)
        {
            var path = new GraphicsPath();
            float d = Math.Max(0.5f, r * 2f);
            if (rect.Width <= d || rect.Height <= d)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int r)
        {
            var path = new GraphicsPath();
            int d = r * 2;
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Region RoundedRegion(int w, int h, int r)
        {
            using (var path = RoundedPath(w, h, r))
            {
                return new Region(path);
            }
        }
    }
}
