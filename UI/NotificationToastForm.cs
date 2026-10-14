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
    /// A MIRRORED card also shows what the toast itself carried (Utils.NotificationExtras):
    /// the sender Windows names (an Edge web site), the picture beside the text, a progress
    /// bar — and a reminder, alarm or call stays until dismissed, as Windows keeps it.
    /// Hovering a card whose message was cut off opens it to the whole message.
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

        /// <summary>
        /// True for Tempo's OWN notifications, so the card draws the live frame of an
        /// animated custom logo instead of a still taken when it was built. Off for
        /// mirrored cards — those show the sending app's icon, which must not be replaced
        /// by Tempo's.
        /// </summary>
        public bool AnimateAppIcon { get; set; }

        private bool _logoSubscribed;
        private readonly Image _hero;          // large picture below the text (owned; optional)
        private Action _onActivate;            // run when the card body is clicked (open the app)

        // What a MIRRORED toast said beyond its text — the sender Windows names, a progress bar, its
        // scenario — and the picture beside its text. Both null on Tempo's own cards.
        private readonly Utils.NotificationExtras _extras;
        private readonly Image _logo;          // owned; disposed on close
        private readonly DateTime _createdUtc = DateTime.UtcNow;

        // Fonts (disposed on close).
        private readonly Font _appFont;
        private readonly Font _titleFont;
        private readonly Font _bodyFont;
        private readonly Font _glyphFont;
        private readonly Font _closeFont;
        private readonly Font _smallFont;      // a toast's progress bar: its title, status and value

        // Geometry — a Windows 11-like card.
        private const int CardWidth = 386;
        private const int Pad = 18;            // was 16 — the text sat tight to the edges
        private const int IconSize = 26;       // app icon
        private const int CloseBox = 22;       // ✕ button (bigger hit target)
        private const int CloseInset = 4;      // keeps ✕ off the rounded corner
        private const int ProgressH = 2;       // slimmer — a timer, not a divider
        private const int Radius = 11;
        private const int HeroMaxH = 150;      // max height of the picture below the text
        private const int LogoSize = 44;       // the picture beside the text (Windows draws it at 48)
        private const int LogoGap = 12;
        private const int BarH = 4;            // a toast's own progress bar

        // How much of the message shows. Collapsed, as Windows shows a toast (it allows the title two
        // lines and the text four); hovered for a moment, the card opens to all of it — a long chat
        // message or a web page's paragraph was cut at three lines with no way to read the rest.
        private const int TitleLines = 3, BodyLines = 4;
        private const int TitleLinesOpen = 8, BodyLinesOpen = 30;
        private const int HeroMaxHOpen = 300;
        private const int OpenAfterMs = 350;   // hover intent: passing over the card does not open it
        private const int CloseAfterMs = 450;  // and leaving it for a moment does not snap it shut
        private bool _canOpen;                 // the collapsed card cuts something off
        private bool _open;
        private int _openCap = int.MaxValue;   // an opened card stays within the screen
        private long _hoverSinceTick, _leftTick;
        private Rectangle _toastBarRect;       // where the toast's own (indeterminate) bar was last painted

        // The toast's own buttons (a mirrored card's), pressed the way Windows presses them — see
        // Utils.AppActivator.BuildButtonAction. Only those Tempo can press are drawn.
        private readonly System.Collections.Generic.List<Utils.NotificationExtras.ToastButton> _buttons =
            new System.Collections.Generic.List<Utils.NotificationExtras.ToastButton>();
        private Rectangle[] _buttonRects = new Rectangle[0];   // where they were last laid out
        private int _hotButton = -1;
        private const int ButtonH = 30, ButtonGap = 8;

        // Windows 11 rounds the card itself (smooth corners, a border in the theme's colour, like its own pop-ups);
        // before that, a region cuts the corners.
        private bool _dwmRounded;

        /// <summary>
        /// Kept on screen until dismissed — Windows' way with a reminder, an alarm or a call. No countdown
        /// runs and none is drawn. Set by the centre before the card is shown.
        /// </summary>
        internal bool StaysUntilDismissed { get; set; }

        /// <summary>Raised when the card opens to its whole message or closes again, so the stack can make room.</summary>
        internal event Action<NotificationToastForm> HeightChanged;

        /// <summary>"Dismiss all" from this card's menu (set by the centre).</summary>
        internal Action DismissAllRequested;

        /// <summary>How many cards are up or waiting — "Dismiss all" is offered only when this one is not alone.</summary>
        internal Func<int> CardsOnScreen;

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
                                     Image icon, Image hero, Action onActivate,
                                     Utils.NotificationExtras extras = null, Image logo = null)
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
            _extras = extras;
            _logo = logo;
            if (extras != null)
            {
                foreach (var b in extras.Buttons) { if (b != null && b.Invoke != null) { _buttons.Add(b); } }
            }

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
            _smallFont = new Font("Segoe UI", 8.25f, FontStyle.Regular);

            Width = CardWidth;
            Height = MeasureHeight(false);
            // Only a card that cuts something off — text past its lines, a picture shrunk — opens on hover.
            _canOpen = MeasureHeight(true) > Height;
            if (_buttons.Count > 0)
            {
                using (var g = CreateGraphics()) { _buttonRects = CurrentLayout(g, false).Buttons ?? new Rectangle[0]; }
            }

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

        /// <summary>
        /// The size the hero picture is drawn at: fit inside (card content width × <paramref name="maxH"/>)
        /// preserving aspect ratio. Zero when there's no picture.
        /// </summary>
        private Size HeroDrawSize(int maxH)
        {
            if (_hero == null || _hero.Width <= 0 || _hero.Height <= 0) { return Size.Empty; }
            int maxW = Width - Pad * 2;
            double scale = Math.Min((double)maxW / _hero.Width, (double)maxH / _hero.Height);
            if (scale > 1.0) { scale = 1.0; }   // never upscale past native size
            int w = Math.Max(1, (int)Math.Round(_hero.Width * scale));
            int h = Math.Max(1, (int)Math.Round(_hero.Height * scale));
            return new Size(w, h);
        }

        /// <summary>Where everything on the card goes. Measuring and painting share it, so they cannot disagree.</summary>
        private struct CardLayout
        {
            public int Height;
            public Rectangle Logo, Title, Body, BarTitle, Bar, BarText, Hero;
            public Rectangle[] Buttons;
        }

        /// <summary>
        /// The card laid out collapsed or <paramref name="open"/>: the header row, then the title, the
        /// text and a toast's progress bar (beside the toast's picture, when it has one), then the big
        /// picture across the card, then the countdown.
        /// </summary>
        private CardLayout ComputeLayout(Graphics g, bool open, int bodyLinesOpen = BodyLinesOpen)
        {
            var L = new CardLayout();
            var wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
            int y = Pad + Math.Max(IconSize, _appFont.Height) + 8;   // under the header row
            int textLeft = Pad;
            if (_logo != null)
            {
                L.Logo = new Rectangle(Pad, y + 2, LogoSize, LogoSize);
                textLeft = Pad + LogoSize + LogoGap;
            }
            int tw = Math.Max(40, Width - Pad - textLeft);

            int titleFull = TextRenderer.MeasureText(g, _title, _titleFont, new Size(tw, 0), wrap).Height;
            L.Title = new Rectangle(textLeft, y, tw, Math.Min(titleFull, _titleFont.Height * (open ? TitleLinesOpen : TitleLines)));
            y += L.Title.Height;

            if (!string.IsNullOrEmpty(_body))
            {
                y += 5;   // was 3 — the body crowded the title
                int bodyFull = TextRenderer.MeasureText(g, _body, _bodyFont, new Size(tw, 0), wrap).Height;
                L.Body = new Rectangle(textLeft, y, tw, Math.Min(bodyFull, _bodyFont.Height * (open ? bodyLinesOpen : BodyLines)));
                y += L.Body.Height;
            }

            Utils.NotificationExtras.ProgressBar bar = _extras?.Progress;
            if (bar != null)
            {
                y += 9;
                if (bar.Title.Length > 0)
                {
                    L.BarTitle = new Rectangle(textLeft, y, tw, _smallFont.Height);
                    y += _smallFont.Height + 3;
                }
                L.Bar = new Rectangle(textLeft, y, tw, BarH);
                y += BarH;
                if (bar.Status.Length > 0 || bar.ValueLabel().Length > 0)
                {
                    y += 3;
                    L.BarText = new Rectangle(textLeft, y, tw, _smallFont.Height);
                    y += _smallFont.Height;
                }
            }
            if (!L.Logo.IsEmpty) { y = Math.Max(y, L.Logo.Bottom); }

            Size hero = HeroDrawSize(open ? HeroMaxHOpen : HeroMaxH);
            if (hero.Height > 0)
            {
                y += 10;
                L.Hero = new Rectangle(Pad + (Width - Pad * 2 - hero.Width) / 2, y, hero.Width, hero.Height);
                y += hero.Height;
            }

            // The toast's buttons, side by side across the card, as Windows lays them out.
            L.Buttons = new Rectangle[_buttons.Count];
            if (_buttons.Count > 0)
            {
                y += 12;
                int full = Width - Pad * 2;
                int w = (full - ButtonGap * (_buttons.Count - 1)) / _buttons.Count;
                for (int i = 0; i < _buttons.Count; i++)
                {
                    int x = Pad + i * (w + ButtonGap);
                    // The last one takes the rounding, so the row ends exactly at the margin.
                    L.Buttons[i] = new Rectangle(x, y, i == _buttons.Count - 1 ? Pad + full - x : w, ButtonH);
                }
                y += ButtonH;
            }
            L.Height = y + Pad + ProgressH;
            return L;
        }

        /// <summary>The layout as the card is now — opened, but never taller than the screen allows.</summary>
        private CardLayout CurrentLayout(Graphics g, bool open)
        {
            CardLayout L = ComputeLayout(g, open);
            if (open && L.Height > _openCap && !L.Body.IsEmpty)
            {
                // Too tall to fit: give up whole lines of text until it does (never fewer than collapsed).
                int over = L.Height - _openCap;
                int lines = Math.Max(BodyLines, L.Body.Height / Math.Max(1, _bodyFont.Height) - (over + _bodyFont.Height - 1) / Math.Max(1, _bodyFont.Height));
                L = ComputeLayout(g, true, lines);
            }
            return L;
        }

        /// <summary>The card's height collapsed or opened.</summary>
        private int MeasureHeight(bool open)
        {
            using (var g = CreateGraphics()) { return CurrentLayout(g, open).Height; }
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

            // Follow the logo's frames only when there is an animation to follow, and only
            // on Tempo's own cards. A mirrored card must keep the sending app's icon.
            if (AnimateAppIcon && !_logoSubscribed && Utils.AnimatedLogo.IsAnimating)
            {
                Utils.AnimatedLogo.FrameChanged += OnLogoFrameRepaint;
                _logoSubscribed = true;
            }
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
                bool newText = body != null && !string.Equals(body, _body, StringComparison.Ordinal);
                if (body != null) { _body = body; }
                if (icon != null)
                {
                    Image old = _icon;
                    _icon = icon;
                    // Never dispose an image the hero slot is also using.
                    if (old != null && !ReferenceEquals(old, _hero)) { try { old.Dispose(); } catch { } }
                }
                if (newText)
                {
                    // The new text can need more (or fewer) lines than the old one had.
                    _canOpen = MeasureHeight(true) > MeasureHeight(false);
                    ApplyHeight(MeasureHeight(_open && _canOpen));
                    if (!_canOpen) { _open = false; }
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
            _smallFont?.Dispose();
            if (_logoSubscribed)
            {
                // A static event outlives this card; staying subscribed would keep it
                // alive and repaint a closed window.
                try { Utils.AnimatedLogo.FrameChanged -= OnLogoFrameRepaint; } catch { }
                _logoSubscribed = false;
            }
            // The card's own menu goes with it — see ShowCardMenu.
            try { _cardMenu?.Close(); } catch { }
            try { _cardMenu?.Dispose(); } catch { }
            _cardMenu = null;
            _icon?.Dispose();
            if (_hero != null && !ReferenceEquals(_hero, _icon)) { _hero.Dispose(); }
            if (_logo != null && !ReferenceEquals(_logo, _icon) && !ReferenceEquals(_logo, _hero)) { _logo.Dispose(); }
            base.OnFormClosed(e);
        }

        /// <summary>The app, title, body and sender this card is showing — used to spot a repeat.</summary>
        internal bool Matches(string appName, string title, string body, string sender = "")
        {
            // ALL of them must match. An earlier version used "app OR (title AND body)",
            // which would have collapsed every unrelated notification from the same app
            // into one card — the opposite of the point. The sender too: two web sites
            // sending "New message" through one browser are two messages.
            return string.Equals(_appName ?? "", appName ?? "", StringComparison.Ordinal)
                   && string.Equals(_title ?? "", title ?? "", StringComparison.Ordinal)
                   && string.Equals(_body ?? "", body ?? "", StringComparison.Ordinal)
                   && string.Equals(_extras?.Sender ?? "", sender ?? "", StringComparison.Ordinal);
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
            if (!_hovered) { _hoverSinceTick = Environment.TickCount64; }
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            _leftTick = Environment.TickCount64;
            _hotButton = -1;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hot = ButtonAt(e.Location);
            if (hot == _hotButton) { return; }
            _hotButton = hot;
            Cursor = hot >= 0 || _onActivate != null ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        /// <summary>The toast button under <paramref name="p"/>, or -1.</summary>
        private int ButtonAt(Point p)
        {
            Rectangle[] rects = _buttonRects;
            for (int i = 0; i < rects.Length && i < _buttons.Count; i++)
            {
                if (rects[i].Contains(p)) { return i; }
            }
            return -1;
        }

        /// <summary>Opens the card to its whole message, or closes it back to Windows' toast size.</summary>
        private void SetOpen(bool open)
        {
            if (_open == open || IsDisposed) { return; }
            if (open)
            {
                // Never taller than most of the screen it is on.
                try { _openCap = Math.Max(240, Screen.FromControl(this).WorkingArea.Height * 4 / 5); } catch { }
            }
            _open = open;
            ApplyHeight(MeasureHeight(open));
        }

        /// <summary>
        /// Resizes the card in place. In a BOTTOM corner the stack is built upwards, so the card grows
        /// upwards: its bottom edge stays where it is and the cards above make room. Growing downwards
        /// there would push it under the taskbar while the stack caught up.
        /// </summary>
        private void ApplyHeight(int h)
        {
            if (IsDisposed || h == Height) { return; }
            int delta = h - Height;
            if (_corner == 2 || _corner == 3)
            {
                _y -= delta;
                _fromY -= delta;
                _targetY -= delta;
                _yTweenTarget = _targetY;
            }
            try
            {
                var pt = new Point((int)Math.Round(_x), (int)Math.Round(_y));
                if (IsHandleCreated && Visible) { SetBounds(pt.X, pt.Y, Width, h); _lastAppliedPoint = pt; }
                else { Height = h; }
                // The corners follow by themselves: Windows rounds the new size, or OnSizeChanged re-cuts the region.
            }
            catch { /* closing */ }
            Invalidate();
            try { HeightChanged?.Invoke(this); } catch (Exception ex) { Utils.Logger.Swallow("NotificationToastForm.HeightChanged", ex); }
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

        /// <summary>
        /// Set for a MIRRORED card: "mute this app" from the card itself. Null on Tempo's
        /// own cards — muting Tempo from a Tempo card would be a trap.
        /// </summary>
        internal Action<string> MuteAppRequested;

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);

            // Right-click is a menu, not an "open". It used to fall through to the body
            // click and launch the app — the one gesture on this card that could do
            // something the user did not ask for.
            if (e.Button == MouseButtons.Right)
            {
                ShowCardMenu(e.Location);
                return;
            }

            // Clicking the ✕ only dismisses; clicking the body opens the app that sent
            // the notification (Windows 11 behaviour), then dismisses.
            if (CloseHitRect.Contains(e.Location))
            {
                BeginDismiss();
                return;
            }
            // One of the toast's own buttons: that button's action, not the body's.
            int pressed = ButtonAt(e.Location);
            if (pressed >= 0)
            {
                var press = _buttons[pressed].Invoke;
                try { press?.Invoke(); } catch (Exception ex) { Utils.Logger.Swallow("NotificationToastForm.Button", ex); }
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

        /// <summary>
        /// The card's own little menu. It holds the card open while it is up — a menu that
        /// vanished mid-decision because the dwell ran out would be worse than no menu.
        /// </summary>
        private void ShowCardMenu(Point at)
        {
            try
            {
                var menu = new ContextMenuStrip { ShowImageMargin = false };
                menu.Renderer = new ToolStripProfessionalRenderer();
                menu.BackColor = _theme.Surface2;
                menu.ForeColor = _theme.Text;

                void AddMute(string name)
                {
                    var mute = new ToolStripMenuItem(Utils.Localization.F("Mute {0}", name));
                    mute.Click += (s, e) =>
                    {
                        var ask = MuteAppRequested;
                        BeginDismiss();
                        try { ask(name); } catch { }
                    };
                    menu.Items.Add(mute);
                }
                if (MuteAppRequested != null)
                {
                    // A sender of its own — a web site in Edge — can be muted by itself, without
                    // silencing every other site the browser passes on.
                    string sender = _extras?.Sender ?? "";
                    if (sender.Length > 0) { AddMute(sender); }
                    if (!string.IsNullOrWhiteSpace(_appName)) { AddMute(_appName); }
                }

                var close = new ToolStripMenuItem(Utils.Localization.T("Dismiss"));
                close.Click += (s, e) => BeginDismiss();
                menu.Items.Add(close);

                // With a reminder or two kept up until dismissed, clearing the corner one card at a
                // time is a chore.
                int others = 0;
                try { others = CardsOnScreen?.Invoke() ?? 0; } catch { }
                if (DismissAllRequested != null && others > 1)
                {
                    var all = new ToolStripMenuItem(Utils.Localization.T("Dismiss all"));
                    all.Click += (s, e) =>
                    {
                        var dismissAll = DismissAllRequested;
                        try { dismissAll(); } catch (Exception ex) { Utils.Logger.Swallow("NotificationToastForm.DismissAll", ex); }
                    };
                    menu.Items.Add(all);
                }

                // Keep the card alive while the menu is open, and let the dwell resume
                // (from a full window, so there is time to read what is left) after.
                _remainingMs = Math.Max(_remainingMs, _dwellMs);
                if (_phase == Phase.Out) { EnterPhase(Phase.Dwell); }
                _menuOpen = true;
                menu.Closed += (s, e) =>
                {
                    _menuOpen = false;
                    _remainingMs = Math.Max(_remainingMs, 2000);
                    // Deliberately NOT disposed here. Closed fires while WinForms is still
                    // inside the drop-down's own hide, and disposing from under it throws
                    // ObjectDisposedException out of the click — choosing "Mute" took the
                    // app down. The previous menu is disposed when the next one opens, and
                    // the last one when the card closes.
                };
                // Held in a field, not just shown: a card can be taken away while its menu
                // is open (the centre disposes the stack on shutdown), and a menu whose card
                // no longer exists would sit on screen with nothing behind it.
                try { _cardMenu?.Dispose(); } catch { }
                _cardMenu = menu;
                menu.Show(this, at);
            }
            catch (Exception ex) { Utils.Logger.Swallow("NotificationToastForm.Menu", ex); }
        }

        private bool _menuOpen;
        private ContextMenuStrip _cardMenu;

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
                    // Hovering opens a card that cuts its message off, after a moment (a pointer
                    // passing over it must not); leaving closes it again, after a moment too.
                    if (_canOpen)
                    {
                        if (_hovered || _menuOpen)
                        {
                            if (!_open && now - _hoverSinceTick >= OpenAfterMs) { SetOpen(true); }
                        }
                        else if (_open && now - _leftTick >= CloseAfterMs) { SetOpen(false); }
                    }
                    // Hovering pauses the countdown, and so does an open card menu: the
                    // mouse is over the MENU, not the card, so _hovered alone would let the
                    // card slide away underneath the choice being made. A card kept up until
                    // dismissed (a reminder, a call) has no countdown at all.
                    if (!_hovered && !_menuOpen && !StaysUntilDismissed)
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
                    if (!StaysUntilDismissed) { Invalidate(new Rectangle(0, Height - ProgressH - 6, Width, ProgressH + 6)); }
                    // A toast's own bar that cannot say how far along it is runs a moving stripe.
                    if (!_toastBarRect.IsEmpty && _extras?.Progress != null && _extras.Progress.Indeterminate)
                    {
                        Invalidate(Rectangle.Inflate(_toastBarRect, 2, 2));
                    }
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

        /// <summary>
        /// Repaints just the app-icon square when the logo advances a frame. Invalidating
        /// only that rectangle matters: these cards can be on screen over a game, and
        /// repainting the whole card ten times a second for a 26px icon would not be a
        /// fair trade.
        /// </summary>
        private void OnLogoFrameRepaint()
        {
            if (IsDisposed || !IsHandleCreated) { return; }
            try
            {
                bool showClose = ShowCloseButton;
                int contentLeft = showClose ? Pad + CloseInset + CloseBox + 10 : Pad;
                Invalidate(new Rectangle(contentLeft, Pad, IconSize, IconSize));
            }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _dwmRounded = TryRoundWithDwm();
            // Region = null too: the window is sized while it is being created, BEFORE this runs, and a region cut
            // then (by OnSizeChanged) would stop Windows rounding it — the corners stayed region-cut and jagged.
            Region = _dwmRounded ? null : RoundedRegion(Width, Height, Radius);
            _cornersChosen = true;
        }

        private bool _cornersChosen;

        /// <summary>
        /// Keeps the region-cut corners on the card's REAL size. The card is measured after its window already
        /// exists (measuring needs a Graphics, which creates the window), so the region cut in OnHandleCreated was
        /// for the window's starting 300 px: every card's bottom corners came out square below a rounded border.
        /// </summary>
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_cornersChosen && !_dwmRounded && IsHandleCreated) { Region = RoundedRegion(Width, Height, Radius); }   // the setter disposes the old one
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34, DWMWCP_ROUND = 2;

        /// <summary>
        /// Asks Windows 11 to round the card itself: smooth corners (a region's are cut pixel by pixel, jagged) and a
        /// border in the theme's colour — the look of Windows' own pop-ups, as Tempo's menus already have. Windows
        /// will not round a window that has a region, so with this there is none. False before Windows 11, which has
        /// neither attribute; the card then cuts its corners with a region.
        /// </summary>
        private bool TryRoundWithDwm()
        {
            if (Environment.OSVersion.Version.Build < 22000) { return false; }
            try
            {
                int pref = DWMWCP_ROUND;
                if (DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)) != 0) { return false; }
                Color b = _theme.Border;
                int colorref = b.R | (b.G << 8) | (b.B << 16);
                DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref colorref, sizeof(int));
                return true;
            }
            catch { return false; }
        }

        /// <summary>For the tests: whether Windows rounds this card (true) or a region cuts its corners.</summary>
        internal bool RoundedByWindows => _dwmRounded;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color accent = Accent;

            // Card border (Windows 11 style — no big coloured stripe). When Windows rounds the card it draws the
            // border itself, in the theme's colour, along its own (smooth) corners; a second one painted here
            // would run inside it.
            if (!_dwmRounded)
            {
                using (var path = RoundedPath(Width, Height, Radius))
                using (var border = new Pen(_theme.Border, 1))
                {
                    g.DrawPath(border, path);
                }
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

            // For TEMPO'S OWN cards, draw whichever frame the logo is showing right now.
            //
            // An animated custom logo plays in the title bar, the taskbar, the tray and
            // the header — and stopped dead on the notification, which took a still copy
            // of frame 0 when the card was built. One surface out of five wearing a frozen
            // logo reads as the wrong icon, not as a deliberate still.
            //
            // BORROWED, never stored: this form owns _icon and disposes it on close, and
            // the frame belongs to AnimatedLogo. Using it only as a local keeps the
            // ownership rule intact and costs no allocation per frame.
            Image appIcon = _icon;
            if (AnimateAppIcon)
            {
                Image live = Utils.AnimatedLogo.CurrentTile;
                if (live != null) { appIcon = live; }
            }

            if (appIcon != null)
            {
                // The DETECTED source-app icon, rounded to a squircle like Win11.
                using (var clip = RoundedRect(iconRect, 6))
                {
                    var saved = g.Save();
                    g.SetClip(clip, CombineMode.Intersect);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(appIcon, iconRect);
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
            TextRenderer.DrawText(g, HeaderText(), _appFont, nameRect, _theme.TextMuted,
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

            // ── Title + body (left-aligned under the icon; beside the toast's picture when it has one) ──
            CardLayout L = CurrentLayout(g, _open);
            // TextBoxControl (DT_EDITCONTROL) is what makes the "…" appear on the LAST line that fits when
            // the text runs past its lines; with word-wrapping alone the text was just cut off, which gave
            // no hint that hovering opens the rest.
            var wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis
                       | TextFormatFlags.TextBoxControl;

            // The toast's own picture beside the text — a contact's photo (round, as Windows draws a
            // person), a web site's notification icon — at the size Windows gives it.
            if (_logo != null && !L.Logo.IsEmpty)
            {
                bool round = _extras != null && _extras.LogoCircle;
                using (var clip = round ? Circle(L.Logo) : RoundedRect(L.Logo, 8))
                {
                    var saved = g.Save();
                    g.SetClip(clip, CombineMode.Intersect);
                    g.DrawImage(_logo, FitInto(_logo.Size, L.Logo, fill: round));
                    g.Restore(saved);
                }
            }

            TextRenderer.DrawText(g, _title, _titleFont, L.Title, _theme.Text, wrap);
            if (!L.Body.IsEmpty)
            {
                TextRenderer.DrawText(g, _body, _bodyFont, L.Body, _theme.TextMuted, wrap);
            }

            // A toast's own progress bar (a download, an install), as Windows lays it out: its title
            // above, the bar, then its status on the left and how far along on the right.
            Utils.NotificationExtras.ProgressBar bar = _extras?.Progress;
            _toastBarRect = bar != null ? L.Bar : Rectangle.Empty;
            if (bar != null)
            {
                var line = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                           | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
                if (!L.BarTitle.IsEmpty) { TextRenderer.DrawText(g, bar.Title, _smallFont, L.BarTitle, _theme.TextMuted, line); }
                DrawToastBar(g, bar, L.Bar, accent);
                if (!L.BarText.IsEmpty)
                {
                    string value = bar.ValueLabel();
                    int valueW = value.Length == 0 ? 0
                        : TextRenderer.MeasureText(g, value, _smallFont, Size.Empty, TextFormatFlags.NoPadding).Width;
                    if (value.Length > 0)
                    {
                        TextRenderer.DrawText(g, value, _smallFont, new Rectangle(L.BarText.Right - valueW, L.BarText.Y, valueW, L.BarText.Height),
                            _theme.TextMuted, line);
                    }
                    if (bar.Status.Length > 0)
                    {
                        TextRenderer.DrawText(g, bar.Status, _smallFont,
                            new Rectangle(L.BarText.X, L.BarText.Y, Math.Max(10, L.BarText.Width - valueW - (valueW > 0 ? 12 : 0)), L.BarText.Height),
                            _theme.TextMuted, line);
                    }
                }
            }

            // The picture (Windows 11 hero image), rounded, centred under the text.
            if (_hero != null && !L.Hero.IsEmpty)
            {
                Rectangle heroRect = L.Hero;
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

            // The toast's own buttons, as Windows draws them: a row of quiet rounded buttons under the content.
            _buttonRects = L.Buttons ?? new Rectangle[0];
            for (int i = 0; i < _buttonRects.Length && i < _buttons.Count; i++)
            {
                Rectangle r = _buttonRects[i];
                bool hot = i == _hotButton;
                using (var path = RoundedRect(r, 6))
                using (var fill = new SolidBrush(hot ? Blend(_theme.Surface2, accent, 0.22) : _theme.Surface2))
                using (var edge = new Pen(hot ? Color.FromArgb(160, accent) : _theme.Border, 1))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(edge, path);
                }
                TextRenderer.DrawText(g, _buttons[i].Text, _appFont, Rectangle.Inflate(r, -6, 0), _theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }

            // Draining progress bar along the bottom (time remaining). Slimmer than
            // before, rounded at the ends, and faded along its length so it reads as a
            // countdown rather than a hard divider under the card. None on a card kept up
            // until dismissed: nothing is counting down.
            double frac = Math.Max(0, Math.Min(1, _remainingMs / (double)_dwellMs));
            int barW = StaysUntilDismissed ? 0 : (int)((Width - Pad * 2) * frac);
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

        /// <summary>
        /// The small line beside the icon: who sent it, as Windows' own pop-up names it — the web site and
        /// the browser ("www.kenherbert.dev · Microsoft Edge"), the app and its attribution ("Phone Link ·
        /// via SMS") — then when, if it happened well before it arrived, then the repeat count.
        /// </summary>
        private string HeaderText()
        {
            string app = string.IsNullOrEmpty(_appName) ? "Notification" : _appName;
            string line = _extras == null ? app : Utils.NotificationExtras.SourceLine(app, _extras.Sender, _extras.Attribution);
            // A message delivered late (the PC was asleep, the app caught up) says when it was sent, as
            // Windows' notification centre does — otherwise it reads as something that just happened.
            if (_extras?.SentUtc is DateTime sent)
            {
                TimeSpan late = _createdUtc - sent;
                if (late >= TimeSpan.FromMinutes(2) && late <= TimeSpan.FromDays(7))
                {
                    line += " · " + sent.ToLocalTime().ToString(late >= TimeSpan.FromHours(20) ? "ddd HH:mm" : "HH:mm");
                }
            }
            return line + RepeatBadge;
        }

        /// <summary>A toast's own progress bar: filled to its value, or a moving stripe when it cannot say.</summary>
        private void DrawToastBar(Graphics g, Utils.NotificationExtras.ProgressBar bar, Rectangle r, Color accent)
        {
            if (r.Width < 8) { return; }
            using (var track = new SolidBrush(Color.FromArgb(46, accent)))
            using (var path = RoundedRect(r, BarH / 2f))
            {
                g.FillPath(track, path);
            }
            Rectangle fill;
            if (bar.Indeterminate)
            {
                // A third of the bar sliding across, once every 1.6 s (repainted from the dwell tick).
                int seg = Math.Max(BarH * 2, r.Width / 3);
                double t = (Environment.TickCount64 % 1600) / 1600.0;
                int x = r.X - seg + (int)Math.Round((r.Width + seg) * t);
                fill = Rectangle.Intersect(r, new Rectangle(x, r.Y, seg, r.Height));
            }
            else
            {
                fill = new Rectangle(r.X, r.Y, (int)Math.Round(r.Width * bar.Value), r.Height);
            }
            if (fill.Width < 2) { return; }
            using (var b = new SolidBrush(accent))
            using (var path = RoundedRect(fill, BarH / 2f))
            {
                g.FillPath(b, path);
            }
        }

        /// <summary>
        /// Where a picture of <paramref name="size"/> is drawn inside <paramref name="box"/>, keeping its shape:
        /// filling the box (a round photo — its circle must be full) or fitting inside it.
        /// </summary>
        private static Rectangle FitInto(Size size, Rectangle box, bool fill)
        {
            if (size.Width <= 0 || size.Height <= 0) { return box; }
            double sx = (double)box.Width / size.Width, sy = (double)box.Height / size.Height;
            double s = fill ? Math.Max(sx, sy) : Math.Min(sx, sy);
            int w = Math.Max(1, (int)Math.Round(size.Width * s));
            int h = Math.Max(1, (int)Math.Round(size.Height * s));
            return new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
        }

        private static GraphicsPath Circle(Rectangle r)
        {
            var path = new GraphicsPath();
            path.AddEllipse(r);
            return path;
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
