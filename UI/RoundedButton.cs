using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A flat <see cref="Button"/> that paints itself with rounded corners so it
    /// matches the rounded "card" panels. It renders entirely from the standard
    /// button properties the theme already sets — <see cref="Control.BackColor"/>,
    /// <see cref="Control.ForeColor"/>, and <see cref="ButtonBase.FlatAppearance"/>'s
    /// border colour/size — so theming and the per-button accent colours (Start =
    /// green, Stop = red, primary = accent) keep working untouched. Hover and pressed
    /// states are derived by lightening/darkening the current back colour.
    /// </summary>
    /// <summary>Small line/solid glyph drawn before an action button's centred label.</summary>
    public enum ActionGlyph
    {
        None, Plus, Save, Copy, Trash, Play, Stop, Gauge, Person, Info, Pencil, Download, Import, Search,
        // Round 40 (the Macros tab's Manage column, Record and Pin): an icon for every action.
        Rename, Notes, Upload, Link, Wrench, Recycle, Star, Refresh, Record,
        // … and Multi-Point's column (Quick Capture, Toggle On/Off, Select all, Show on screen, Clear All).
        Crosshair, Toggle, SelectAll, Eye, Clear,
        // … and the Advanced sections' Show / Hide: which way the section will move.
        ChevronDown, ChevronUp
    }

    public class RoundedButton : Button
    {
        public int CornerRadius { get; set; } = 9;

        /// <summary>Optional glyph drawn to the left of the text (sidebar nav).</summary>
        public NavIconKind IconKind { get; set; } = NavIconKind.None;

        /// <summary>
        /// Optional own colour for the nav icon. Empty = the icon follows the text
        /// colour (the old look). The sidebar gives each tab's icon its own hue so
        /// the tabs can be told apart at a glance.
        /// </summary>
        public Color IconColor { get; set; } = Color.Empty;

        /// <summary>Optional action glyph drawn before the centred label (New, Save, …).</summary>
        public ActionGlyph Glyph { get; set; } = ActionGlyph.None;

        /// <summary>
        /// The glyph's own colour (the red dot of Record, the green of Recycle bin). Empty: the caption's colour. A disabled
        /// button greys it with the caption either way.
        /// </summary>
        public Color GlyphColor { get; set; } = Color.Empty;

        /// <summary>
        /// Glyph at the left edge and the caption left-aligned after it, instead of the two centred as a group — for a
        /// column of buttons, so their icons line up one under another and their words start at one place (the Macros
        /// tab's Manage card, round 40). A caption with no room for the glyph is drawn alone, centred, as before.
        /// </summary>
        public bool GlyphAlignLeft { get; set; }

        /// <summary>
        /// A short count in a small pill at the right end of the button ("3" on the Recycle bin while it holds three macros),
        /// tinted with <see cref="GlyphColor"/> (or the caption's colour). Null or empty: none. The caption stops short of it.
        /// It used to be part of the caption — "Recycle bin (3)" — which left no room for the icon in a column button.
        /// </summary>
        public string BadgeText { get; set; }

        /// <summary>The badge's width plus its gap before the caption, in pixels; 0 without one.</summary>
        private int BadgeRoom()
        {
            if (string.IsNullOrEmpty(BadgeText)) { return 0; }
            using (var small = BadgeFont())
            {
                int text = TextRenderer.MeasureText(BadgeText, small, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
                int pad = (int)Math.Round(6f * DeviceDpi / 96f);
                int min = (int)Math.Round(18f * DeviceDpi / 96f);
                return Math.Max(min, text + pad * 2) + (int)Math.Round(8f * DeviceDpi / 96f);
            }
        }

        private Font BadgeFont() => new Font(Font.FontFamily, Math.Max(7f, Font.SizeInPoints * 0.82f), FontStyle.Bold);

        /// <summary>
        /// <see cref="BadgeText"/> as a round bubble over the glyph's top-right corner: filled with the glyph's colour, the
        /// count in the ink that reads on it, and a ring of the button's own fill around it so it stands off the glyph.
        /// </summary>
        private void DrawBadgeBubble(Graphics g, RectangleF glyph, Color hue)
        {
            using (var small = new Font(Font.FontFamily, Math.Max(6.5f, Font.SizeInPoints * 0.72f), FontStyle.Bold))
            {
                Size text = TextRenderer.MeasureText(BadgeText, small, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                float h = Math.Max(text.Height, glyph.Height * 0.58f);
                float w = Math.Max(h, text.Width + h * 0.5f);
                var bubble = new RectangleF(glyph.Right - w * 0.45f, glyph.Top - h * 0.5f, w, h);
                var prev = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color ring = Enabled ? BackColor : Mix(BackColor, Color.Gray, 0.45);
                using (var ringPath = Capsule(RectangleF.Inflate(bubble, 1.5f, 1.5f)))
                using (var r = new SolidBrush(ring))
                {
                    g.FillPath(r, ringPath);
                }
                using (var path = Capsule(bubble))
                using (var fill = new SolidBrush(hue))
                {
                    g.FillPath(fill, path);
                }
                g.SmoothingMode = prev;
                TextRenderer.DrawText(g, BadgeText, small, Rectangle.Round(bubble), Theme.ReadableOn(hue),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }

        /// <summary>
        /// Sidebar "active tab" style: instead of a heavy solid-accent fill, the button
        /// paints a soft accent-tinted background, an accent indicator bar down its left
        /// edge, and accent-coloured text/icon. Set <see cref="AccentColor"/> too.
        /// </summary>
        public bool Selected { get; set; }

        /// <summary>Accent used for the <see cref="Selected"/> indicator/tint.</summary>
        public Color AccentColor { get; set; } = Color.DodgerBlue;

        /// <summary>
        /// The theme's second accent (round 42): the open row's wash runs from <see cref="AccentColor"/> at its left to this
        /// at its right, and its indicator bar from one to the other down its length. Empty: one colour, as before.
        /// </summary>
        public Color Accent2Color { get; set; } = Color.Empty;

        private Color _gradFrom = Color.Empty, _gradTo = Color.Empty;

        /// <summary>
        /// A fill that runs from <paramref name="from"/> at the left to <paramref name="to"/> at the right (round 42: a
        /// primary button's accent into the theme's second accent, <see cref="Theme.PrimaryEnd"/>). Only while BackColor is
        /// still <paramref name="from"/>: a button re-coloured since — Start turned into a red Stop — paints its own colour,
        /// never a gradient left over from a role it no longer has. An empty <paramref name="to"/> takes it away.
        /// </summary>
        public void SetGradient(Color from, Color to)
        {
            if (_gradFrom.ToArgb() == from.ToArgb() && _gradTo.ToArgb() == to.ToArgb() && _gradTo.IsEmpty == to.IsEmpty) { return; }
            _gradFrom = from;
            _gradTo = to;
            Invalidate();
        }

        private bool GradientApplies =>
            !_gradTo.IsEmpty && _gradTo.ToArgb() != _gradFrom.ToArgb() && BackColor.ToArgb() == _gradFrom.ToArgb();

        /// <summary>
        /// The row's keyboard shortcut ("1" for Ctrl+1), shown in place of its icon while
        /// <see cref="RevealShortcut"/> is on — the window turns it on while Ctrl is held.
        ///
        /// It used to be a dimmed digit in a column of its own at the right of every row.
        /// That column took 32 px from a 164 px row, so the longer labels no longer fitted
        /// and the layout checker shrank them one by one: every language had one to three
        /// tabs in a smaller font than the rest ("Multi-Point" in English, "Configurações"
        /// in Portuguese), and nine digits sat beside the labels all the time.
        /// </summary>
        public string ShortcutHint { get; set; }

        /// <summary>Draw <see cref="ShortcutHint"/> as a key cap where the icon is (see there).</summary>
        public bool RevealShortcut { get; set; }

        /// <summary>
        /// A row in the navigation rail: nothing of its own at rest — the rail shows through
        /// (<see cref="BackdropPainter"/>) — a soft lift under the pointer, and the accent
        /// wash with its bar while <see cref="Selected"/>. Every unselected tab used to be a
        /// box of its own with a lighter top edge, so the list read as a stack of buttons.
        /// </summary>
        public bool NavRow { get; set; }

        /// <summary>Paints what is behind a <see cref="NavRow"/> (the rail's own backdrop). Null: BackColor.</summary>
        public Action<Graphics, Control> BackdropPainter { get; set; }

        /// <summary>
        /// A <see cref="NavRow"/> whose page lives in a window of its own (Accounts, when it is set
        /// to): a small ↗ at the right says a click brings up that window rather than a page here.
        /// </summary>
        public bool OpensWindow { get; set; }

        /// <summary>Room the ↗ of <see cref="OpensWindow"/> takes at the right of the row.</summary>
        private const int OpensWindowRoom = 22;

        /// <summary>
        /// A <see cref="NavRow"/>'s short note at the right, in small muted type — "Off" for a tab whose feature is turned
        /// off in Settings (MainForm.Features.cs). The label stops short of it. Null: none.
        /// </summary>
        public string TrailingNote { get; set; }

        /// <summary>
        /// A <see cref="NavRow"/> that holds other rows under it (Clicker holds Profiles — the user, 2026-11-13: "tab Clicker
        /// needs add expand > Profiles"): a chevron at its right end, pointing right while they are hidden and down while
        /// they show. A click on the chevron raises <see cref="ExpanderClicked"/> instead of Click; the rest of the row
        /// opens its own tab as before.
        /// </summary>
        public bool HasExpander { get; set; }

        /// <summary>How far the chevron has turned: 0 (›, the rows under it hidden) to 1 (⌄, shown). Eased by the window
        /// while those rows slide out or back.</summary>
        public float ExpandProgress { get; set; }

        /// <summary>Raised instead of Click when the chevron of a <see cref="HasExpander"/> row is clicked.</summary>
        public event EventHandler ExpanderClicked;

        /// <summary>A <see cref="NavRow"/> under another one (Profiles under Clicker): its icon and label set in by one step.</summary>
        public int IndentLevel { get; set; }

        /// <summary>
        /// A <see cref="HasExpander"/> row whose hidden rows include the open tab: a quieter accent bar at its left, so the
        /// rail still says where the open page is while the row that holds it is folded away.
        /// </summary>
        public bool HoldsSelection { get; set; }

        /// <summary>The set-in of an <see cref="IndentLevel"/> step, in pixels: in step with the row's height (its scale).</summary>
        private int IndentPx => IndentLevel <= 0 ? 0 : (int)Math.Round(Math.Max(12, Height * 0.40) * IndentLevel);

        /// <summary>The chevron's room at the right end of a <see cref="HasExpander"/> row, in pixels.</summary>
        private int ExpanderPx => HasExpander ? (int)Math.Round(Math.Max(20, Height * 0.60)) : 0;

        /// <summary>Where a click turns the chevron rather than opening the row's tab (the button's own coordinates).</summary>
        internal Rectangle ExpanderBounds => HasExpander ? new Rectangle(Width - ExpanderPx - 6, 0, ExpanderPx + 6, Height) : Rectangle.Empty;

        private bool _overExpander, _expanderPress;

        /// <summary>A <see cref="NavRow"/>'s text under the pointer (brighter than its resting ForeColor).</summary>
        public Color HotForeColor { get; set; } = Color.Empty;

        /// <summary>A <see cref="NavRow"/>'s text when selected. Empty: <see cref="AccentColor"/>.</summary>
        public Color SelectedForeColor { get; set; } = Color.Empty;

        /// <summary>
        /// A <see cref="NavRow"/>'s icon when selected — its <see cref="IconColor"/>, moved where
        /// it would not stand out 3:1 from the accent wash under it. Empty: IconColor.
        /// </summary>
        public Color SelectedIconColor { get; set; } = Color.Empty;

        private bool _hover;
        private bool _down;

        // ── Busy ─────────────────────────────────────────────────────────────────────────────────────
        //
        // A button that starts something slow ("Check for updates", "Re-check", "Test") used to say "Checking…" and
        // grey out — which reads the same as "not available". While Busy it also spins a small ring before its
        // caption, in the accent colour even while disabled, so it reads as working (2026-11-08, "animation for
        // refresh, loading"). The ring takes frames from Motion only while it is busy.
        private bool _busy;
        private double _busySince;
        private Action _busyTick;

        /// <summary>A spinning ring before the caption while true: whatever this button started is still running.</summary>
        public bool Busy
        {
            get => _busy;
            set
            {
                if (_busy == value) { return; }
                _busy = value;
                _busySince = Motion.NowMs;
                if (_busyTick == null)
                {
                    _busyTick = () => { if (IsHandleCreated && !IsDisposed && Visible) { Invalidate(); } };
                }
                if (value) { Motion.Subscribe(_busyTick); } else { Motion.Unsubscribe(_busyTick); }
                Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_busyTick != null) { Motion.Unsubscribe(_busyTick); }
            base.Dispose(disposing);
        }

        public RoundedButton()
        {
            FlatStyle = FlatStyle.Flat;
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
        }

        // ── Eased hover / selection ───────────────────────────────────────────
        //
        // Hover and selection used to snap between two states in a single frame, which
        // is the one place left in Tempo that still did — the notification cards, the
        // tray menu and the window itself all ease now, so the nav rail was the odd one
        // out. These two run 0→1 on a clock, so the wash and the active-tab bar grow in
        // instead of appearing.
        //
        // ONE timer is shared by every button, it only runs while something is actually
        // mid-transition, and it parks itself the moment everything has settled — a
        // timer per button, or one that never stops, is exactly the kind of idle repaint
        // cost this app has had to hunt down before.
        // _hoverT and _selT are what is painted: _hoverP and _selP run straight on the clock, and the painted values
        // follow them through an ease-out curve (2026-11-08), so the wash and the bar move off quickly and settle softly
        // instead of travelling at one speed and stopping dead.
        private float _hoverT;
        private float _selT;
        private float _hoverP;
        private float _selP;
        private bool _lastSelected;

        private const int FadeMs = 150;
        private static System.Windows.Forms.Timer _fadeTimer;
        private static readonly System.Collections.Generic.List<RoundedButton> _fading =
            new System.Collections.Generic.List<RoundedButton>();
        private static long _lastFadeTick;

        // The fades run on Motion's frame clock — one tick per frame the screen shows, posted to the UI thread —
        // and on a WinForms Timer only where there is no clock. The Timer's WM_TIMER is the last message Windows
        // hands out, after every input, posted message and paint: clicking through tabs (each switch paints for
        // 100–170 ms) it hardly ticked, and the rail sat half-way between rows — Macros still lit while Settings was
        // open (the user's screenshot, 2026-11-11).
        private static bool _onClock;
        private static readonly Action FadeFrame = FadeTick;

        private void BeginFade()
        {
            if (IsDisposed || !IsHandleCreated) { return; }
            if (!_fading.Contains(this)) { _fading.Add(this); }
            if (_onClock || (_fadeTimer != null && _fadeTimer.Enabled)) { return; }
            _lastFadeTick = Environment.TickCount64;
            Motion.Subscribe(FadeFrame);
            _onClock = Motion.IsSubscribed(FadeFrame);
            if (_onClock) { return; }
            if (_fadeTimer == null)
            {
                _fadeTimer = new System.Windows.Forms.Timer { Interval = 16 };
                _fadeTimer.Tick += (s, e) => FadeTick();
            }
            _fadeTimer.Start();
        }

        private static void FadeTick()
        {
            long now = Environment.TickCount64;
            float step = Math.Min(1f, (now - _lastFadeTick) / (float)FadeMs);
            _lastFadeTick = now;

            for (int i = _fading.Count - 1; i >= 0; i--)
            {
                RoundedButton b = _fading[i];
                if (b == null || b.IsDisposed || !b.IsHandleCreated)
                {
                    _fading.RemoveAt(i);
                    continue;
                }
                bool busy = b.Advance(step);
                b.Invalidate();
                if (!busy) { _fading.RemoveAt(i); }
            }

            if (_fading.Count == 0)
            {
                if (_onClock) { Motion.Unsubscribe(FadeFrame); _onClock = false; }
                _fadeTimer?.Stop();
            }
        }

        /// <summary>
        /// Puts the selection where it is going at once, with no fade: a tab switch is about to keep the window busy
        /// for a moment, and the rail must answer the click before that, not after (MainForm.MarkSidebarRowNow).
        /// </summary>
        internal void SnapSelection()
        {
            float target = Selected && Enabled ? 1f : 0f;
            _selP = target;
            _selT = target;
            _lastSelected = Selected;
        }

        /// <summary>Moves both eased values toward their targets; false once settled.</summary>
        private bool Advance(float step)
        {
            float hoverTarget = _hover && Enabled ? 1f : 0f;
            float selTarget = Selected && Enabled ? 1f : 0f;
            bool busy = false;

            if (Math.Abs(_hoverP - hoverTarget) > 0.001f)
            {
                _hoverP = _hoverP < hoverTarget
                    ? Math.Min(hoverTarget, _hoverP + step)
                    : Math.Max(hoverTarget, _hoverP - step);
                busy = true;
            }
            if (Math.Abs(_selP - selTarget) > 0.001f)
            {
                _selP = _selP < selTarget
                    ? Math.Min(selTarget, _selP + step)
                    : Math.Max(selTarget, _selP - step);
                busy = true;
            }
            _hoverT = Eased(_hoverP, hoverTarget);
            _selT = Eased(_selP, selTarget);
            return busy;
        }

        /// <summary>
        /// The painted value for progress <paramref name="p"/> heading to <paramref name="target"/>: fast off the mark and
        /// soft into place both ways — ease-out going up, its mirror going down. With Windows' animations off, the target.
        /// </summary>
        private static float Eased(float p, float target)
        {
            if (!Motion.Enabled) { return target; }
            return target >= 0.5f
                ? (float)Motion.EaseOutCubic(p)
                : 1f - (float)Motion.EaseOutCubic(1f - p);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; BeginFade(); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _down = false; _overExpander = false; BeginFade(); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            // Pressed on the chevron: the click that follows turns it (OnClick) rather than opening the row's tab.
            _expanderPress = HasExpander && e.Button == MouseButtons.Left && ExpanderBounds.Contains(e.Location);
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            // Where it was let go, before the base raises Click from here.
            _releaseOnExpander = HasExpander && ExpanderBounds.Contains(e.Location);
            base.OnMouseUp(e);
            _down = false;
            Invalidate();
        }

        private bool _releaseOnExpander;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            // The chevron lights up on its own under the pointer: it does something other than the row.
            bool over = HasExpander && ExpanderBounds.Contains(e.Location);
            if (over != _overExpander)
            {
                _overExpander = over;
                Invalidate();
            }
        }

        protected override void OnClick(EventArgs e)
        {
            // Pressed and let go on the chevron: shows or hides the rows under it. A key (Space, Enter) still opens the tab.
            bool expander = _expanderPress && _releaseOnExpander && HasExpander;
            _expanderPress = false;
            _releaseOnExpander = false;
            if (expander)
            {
                try { ExpanderClicked?.Invoke(this, EventArgs.Empty); }
                catch (Exception ex) { AutoClicker.Utils.Logger.Swallow("RoundedButton.ExpanderClicked", ex); }
                return;
            }
            base.OnClick(e);
        }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateRegion(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateRegion(); }

        /// <summary>
        /// The narrowest width at which this button draws its caption WITHOUT cutting it —
        /// including whatever it paints beside the text: an action glyph, a nav icon, a
        /// shortcut digit.
        ///
        /// Lives here, next to OnPaint, because it is the same geometry. The layout checker
        /// used to measure only the text, so a glyph button — the Clicker tab's Start and
        /// Stop — needed ~24 px more than it was told, and a translated caption could be
        /// cut to "Démar…" while the check reported the button as fine.
        /// </summary>
        internal int CaptionWidthNeeded(int clipPad) => CaptionWidthNeeded(clipPad, Text);

        /// <summary>
        /// <see cref="CaptionWidthNeeded(int)"/> for a caption this button can show besides the one it
        /// has now (Start / Stop, Show / Hide): the layout checks measure every one of them.
        /// </summary>
        internal int CaptionWidthNeeded(int clipPad, string caption)
        {
            // A count badge at the right takes its room from the caption's (BadgeText).
            int text = MeasureCaption(caption) + (IconKind == NavIconKind.None ? BadgeRoom() : 0);

            if (Glyph != ActionGlyph.None && IconKind == NavIconKind.None)
            {
                // The glyph gives way when it does not fit (see GlyphFits), so what the
                // CAPTION needs to stay uncut is the text alone.
                return text + clipPad;
            }

            if (IconKind != NavIconKind.None)
            {
                // Icon, gap, label, margin. The shortcut takes no room: it is drawn over the
                // icon, and only while Ctrl is held (see ShortcutHint).
                float iconSize = Math.Min(Height - 16, 20);
                if (iconSize < 10) { iconSize = 10; }
                // A row set in under another starts further right; a row holding others keeps its chevron's room.
                return (int)Math.Ceiling(14 + IndentPx + iconSize + 8) + text + 8 + (OpensWindow ? OpensWindowRoom : 0) + ExpanderPx;
            }

            return text + clipPad;
        }

        /// <summary>
        /// The width at which the action glyph AND the caption both fit — what the button
        /// would like, as opposed to <see cref="CaptionWidthNeeded"/>, what it must have.
        /// Mirrors OnPaint: glyph (12–17 px) + 7 px gap + text, with 8 px either side.
        /// </summary>
        internal int CaptionWidthWanted() => CaptionWidthWanted(Text);

        /// <summary><see cref="CaptionWidthWanted()"/> for another caption this button can show.</summary>
        internal int CaptionWidthWanted(string caption)
        {
            if (Glyph == ActionGlyph.None || IconKind != NavIconKind.None) { return CaptionWidthNeeded(8, caption); }
            return GlyphRowWidth(8, caption);
        }

        /// <summary>
        /// Glyph + gap + caption, starting 8 px in (as OnPaint places it), plus
        /// <paramref name="rightMargin"/> before the button's edge.
        /// </summary>
        private int GlyphRowWidth(int rightMargin, string caption = null)
        {
            // No room for a count badge here: with the glyph drawn it rides on the glyph (DrawBadgeBubble).
            int text = MeasureCaption(caption ?? Text);
            float gsize = Math.Min(Height - 12, 17);
            if (gsize < 12) { gsize = 12; }
            return (int)Math.Ceiling(GlyphStartPad + gsize + 7 + text + rightMargin);
        }

        /// <summary>
        /// Where the glyph starts: 8 px in for a centred group (its least), a little further in at the left edge of a
        /// <see cref="GlyphAlignLeft"/> button, in step with the screen's scale.
        /// </summary>
        private float GlyphStartPad => GlyphAlignLeft ? Math.Max(8f, (float)Math.Round(11f * DeviceDpi / 96f)) : 8f;

        /// <summary>
        /// A caption's width as OnPaint draws it: with NoPrefix, so an "&amp;" is a character. Measured
        /// without it, "&amp;" was taken for a shortcut marker and left out — a caption with one was
        /// measured a character narrower than it is drawn.
        /// </summary>
        private int MeasureCaption(string caption)
        {
            return TextRenderer.MeasureText(caption ?? string.Empty, Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPrefix).Width;
        }

        /// <summary>
        /// Whether the action glyph fits beside the caption at the current width.
        ///
        /// When it does not, the caption is drawn ALONE. A glyph is decoration and the words
        /// are the meaning: French "Enregistrer" and German "Speichern" beside their save
        /// icons needed ~100 px in 80 px buttons, so the text was cut to fit a picture of a
        /// floppy disk. Now the picture goes first.
        ///
        /// The test is the PAINT's, not the comfortable width above: OnPaint starts the glyph
        /// 8 px in and lets the caption run to the edge, so 4 px of air on the right is
        /// enough. Using the 8 px comfort margin here dropped the icon from English
        /// "Humanize", which had always fitted with 7 px to spare.
        /// </summary>
        internal bool GlyphFits => Glyph == ActionGlyph.None || IconKind != NavIconKind.None
                                   || GlyphRowWidth(4) <= Width;

        // Clip to the rounded shape so the corners reveal whatever is behind the button.
        private void UpdateRegion()
        {
            if (Width <= 1 || Height <= 1)
            {
                return;
            }
            using (var path = Rounded(new Rectangle(0, 0, Width, Height), CornerRadius))
            {
                Region = new Region(path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);

            // Selection changes from OUTSIDE (clicking another tab sets Selected on two
            // buttons at once) have no mouse event to hook, so notice it here.
            if (Selected != _lastSelected)
            {
                _lastSelected = Selected;
                BeginFade();
            }

            if (NavRow)
            {
                PaintNavRowBackground(g, rect);
            }
            else
            {
                PaintButtonBackground(g, rect);
            }

            // Active-tab indicator: a rounded accent bar down the left edge.
            // Grows out from the middle as the tab becomes active, rather than blinking
            // into place at full height.
            if (_selT > 0.01f && Enabled)
            {
                int full = Math.Max(12, (int)(Height * 0.52));
                int bh = Math.Max(2, (int)(full * _selT));
                int by = (Height - bh) / 2;
                using (var barPath = Rounded(new Rectangle(0, by, 5, bh), 2))
                {
                    // The accent at the top, the theme's second accent at the foot (round 42).
                    if (!Accent2Color.IsEmpty && bh > 3)
                    {
                        using (var barBrush = new LinearGradientBrush(new Rectangle(0, by - 1, 5, bh + 2), AccentColor, Accent2Color,
                                                                      LinearGradientMode.Vertical))
                        {
                            g.FillPath(barBrush, barPath);
                        }
                    }
                    else
                    {
                        using (var barBrush = new SolidBrush(AccentColor)) { g.FillPath(barBrush, barPath); }
                    }
                }
            }
            else if (NavRow && HoldsSelection && Enabled)
            {
                // The open tab is folded away under this row: a shorter, softer bar where the open row's would be.
                int bh = Math.Max(8, (int)(Height * 0.32));
                using (var barPath = Rounded(new Rectangle(0, (Height - bh) / 2, 4, bh), 2))
                using (var barBrush = new SolidBrush(Color.FromArgb(150, AccentColor)))
                {
                    g.FillPath(barBrush, barPath);
                }
            }

            PaintContent(g, rect);

            // Where the keyboard is, when it is being used: an accent outline. A nav row had
            // no focus cue at all, so tabbing into the rail left you guessing which row Enter
            // would open.
            if (NavRow && Focused && ShowFocusCues && Enabled)
            {
                using (var pen = new Pen(AccentColor, 1.5f))
                using (var path = Rounded(Rectangle.Inflate(rect, -1, -1), Math.Max(2, CornerRadius - 1)))
                {
                    g.DrawPath(pen, path);
                }
            }
        }

        /// <summary>
        /// A <see cref="NavRow"/>: whatever is behind it at rest, then — faded in on the clock —
        /// a lift under the pointer (lighter on a dark theme, darker on a light one, where
        /// "lighter" was all but invisible) and the accent wash when selected.
        /// </summary>
        private void PaintNavRowBackground(Graphics g, Rectangle rect)
        {
            try
            {
                if (BackdropPainter != null) { BackdropPainter(g, this); }
                else { g.Clear(BackColor); }
            }
            catch { g.Clear(BackColor); }

            if (!Enabled) { return; }

            bool light = BackColor.GetBrightness() > 0.5f;
            Color lift = light ? Shade(BackColor, -0.06) : Shade(BackColor, 0.10);
            Color wash = Mix(BackColor, AccentColor, 0.20 + 0.10 * _hoverT);
            Color fill = Mix(lift, wash, _selT);
            // The open row's wash runs into the theme's second accent along the row (round 42).
            Color fillEnd = fill;
            if (!Accent2Color.IsEmpty && _selT > 0.001f)
            {
                fillEnd = Mix(lift, Mix(BackColor, Accent2Color, 0.20 + 0.10 * _hoverT), _selT);
            }
            float strength = Math.Max(_hoverT, _selT);
            if (_down)
            {
                // A press stays instant — feedback for your own click should never lag.
                fill = Shade(fill, light ? -0.06 : -0.08);
                fillEnd = Shade(fillEnd, light ? -0.06 : -0.08);
                strength = 1f;
            }
            if (strength <= 0.01f) { return; }

            int a = (int)Math.Round(255 * Math.Min(1f, strength));
            using (var path = Rounded(rect, CornerRadius))
            {
                var area = new Rectangle(0, 0, Math.Max(1, Width), Math.Max(2, Height));
                if (fillEnd.ToArgb() != fill.ToArgb())
                {
                    // Two-tone along the row, then lit at the top and shaded at the foot as before.
                    using (var brush = new LinearGradientBrush(area, Color.FromArgb(a, fill), Color.FromArgb(a, fillEnd),
                                                               LinearGradientMode.Horizontal))
                    {
                        g.FillPath(brush, path);
                    }
                    using (var shade = new LinearGradientBrush(area, Color.FromArgb(18 * a / 255, 255, 255, 255),
                                                               Color.FromArgb(13 * a / 255, 0, 0, 0), LinearGradientMode.Vertical))
                    {
                        g.FillPath(shade, path);
                    }
                }
                else
                {
                    using (var brush = new LinearGradientBrush(area,
                        Color.FromArgb(a, Shade(fill, 0.07)), Color.FromArgb(a, Shade(fill, -0.05)),
                        LinearGradientMode.Vertical))
                    {
                        g.FillPath(brush, path);
                    }
                }
            }
        }

        /// <summary>Every other button: its own fill, a little lighter at the top.</summary>
        private void PaintButtonBackground(Graphics g, Rectangle rect)
        {
            PaintBehind(g);
            Color back = BackColor;
            // A primary button's far end (SetGradient), in the same state as its start.
            Color backEnd = Color.Empty;
            bool twoTone = Enabled && GradientApplies;
            if (!Enabled)
            {
                back = Mix(BackColor, Color.Gray, 0.45);
            }
            else if (_down)
            {
                // A press stays instant — feedback for your own click should never lag.
                back = Shade(BackColor, -0.12);
                if (twoTone) { backEnd = Shade(_gradTo, -0.12); }
            }
            else
            {
                // Blend the three resting looks by their eased weights instead of
                // picking one: plain → hover lift → selected accent wash.
                Color unselected = Mix(BackColor, Shade(BackColor, 0.12), _hoverT);
                Color selected = Mix(BackColor, AccentColor, 0.20 + 0.10 * _hoverT);
                back = Mix(unselected, selected, _selT);
                if (twoTone) { backEnd = Mix(_gradTo, Shade(_gradTo, 0.12), _hoverT); }
            }

            using (var path = Rounded(rect, CornerRadius))
            {
                // Subtle top-lighter → bottom-darker gradient gives the flat buttons a
                // little depth to match the card UI. Solid fallback for zero-height.
                if (Height > 1 && !backEnd.IsEmpty)
                {
                    // Two-tone left to right (round 42), lit at the top and shaded at the foot like every other button.
                    var area = new Rectangle(0, 0, Math.Max(1, Width), Height);
                    using (var fill = new LinearGradientBrush(area, back, backEnd, LinearGradientMode.Horizontal))
                    {
                        g.FillPath(fill, path);
                    }
                    using (var shade = new LinearGradientBrush(area, Color.FromArgb(18, 255, 255, 255), Color.FromArgb(13, 0, 0, 0),
                                                               LinearGradientMode.Vertical))
                    {
                        g.FillPath(shade, path);
                    }
                }
                else if (Height > 1)
                {
                    using (var fill = new LinearGradientBrush(
                        new Rectangle(0, 0, Math.Max(1, Width), Height),
                        Shade(back, 0.07), Shade(back, -0.05),
                        LinearGradientMode.Vertical))
                    {
                        g.FillPath(fill, path);
                    }
                }
                else
                {
                    using (var fill = new SolidBrush(back))
                    {
                        g.FillPath(fill, path);
                    }
                }
                if (FlatAppearance.BorderSize > 0)
                {
                    using (var pen = new Pen(FlatAppearance.BorderColor, 1))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }
        }

        /// <summary>
        /// What lies behind the button, under its rounded shape: the nearest solid background colour up its parents (a
        /// card's, which is the colour the card paints), or the slice of a page's wallpaper when only see-through parents
        /// lie between. The window region cuts the corners away; this is for the anti-aliased pixels along the edge.
        /// Nothing was painted there before (2026-11-08): the edge blended with whatever the double buffer — one bitmap
        /// shared by every control — last held, which was another control's picture. The safety card's ✕ carried a
        /// sliver of the title's letters down its right edge (scratchpad/closeprobe recoloured the title and the sliver
        /// changed colour with it); a borderless button anywhere could show the same.
        /// </summary>
        private void PaintBehind(Graphics g)
        {
            try
            {
                for (Control p = Parent; p != null; p = p.Parent)
                {
                    if (p is BackdropTabPage page) { page.PaintBackdropSlice(g, this); return; }
                    if (p.BackColor.A == 255) { g.Clear(p.BackColor); return; }
                }
            }
            catch { }
            g.Clear(BackColor.A == 255 ? BackColor : SystemColors.Control);
        }

        /// <summary>The glyph or icon and the caption, in the colours of the current state.</summary>
        private void PaintContent(Graphics g, Rectangle rect)
        {
            Color fore;
            if (!Enabled)
            {
                fore = Mix(ForeColor, Color.Gray, 0.5);
            }
            else if (NavRow)
            {
                // Resting ink, brighter under the pointer, the selected colour when open.
                Color hot = HotForeColor.IsEmpty ? ForeColor : HotForeColor;
                Color sel = SelectedForeColor.IsEmpty ? AccentColor : SelectedForeColor;
                fore = Mix(Mix(ForeColor, hot, _hoverT), sel, _selT);
            }
            else
            {
                fore = Mix(ForeColor, AccentColor, _selT);
            }

            // The count badge (the Recycle bin's "3"). With the glyph drawn, it is a bubble on the glyph's corner — like a
            // count on an app's icon — and needs no room of its own; the glyph is a column button's whole left edge, so a
            // pill beside the caption would have pushed it out. Without a glyph, a small tinted pill at the right end, and
            // the caption keeps clear of it.
            bool glyphShown = Glyph != ActionGlyph.None && IconKind == NavIconKind.None && GlyphFits && !NavRow;
            int badgeRoom = 0;
            if (!NavRow && IconKind == NavIconKind.None && !string.IsNullOrEmpty(BadgeText) && !glyphShown)
            {
                badgeRoom = BadgeRoom();
                using (var small = BadgeFont())
                {
                    int gapPx = (int)Math.Round(8f * DeviceDpi / 96f);
                    int pillW = badgeRoom - gapPx;
                    int pillH = Math.Min(Height - 8, (int)Math.Round(18f * DeviceDpi / 96f));
                    var pill = new Rectangle(Width - gapPx - pillW, (Height - pillH) / 2, pillW, pillH);
                    Color hue = GlyphColor.IsEmpty || !Enabled ? fore : GlyphColor;
                    using (var path = Rounded(pill, pillH / 2))
                    using (var fill = new SolidBrush(Color.FromArgb(Enabled ? 70 : 40, hue)))
                    {
                        g.FillPath(fill, path);
                    }
                    TextRenderer.DrawText(g, BadgeText, small, pill, fore,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
            }

            // Busy: the ring takes the glyph's place (or comes before a caption that had none), centred with the
            // caption as a group — when both fit; a caption that doesn't leave room keeps the button to itself.
            if (_busy && !NavRow && IconKind == NavIconKind.None)
            {
                int textWidth = MeasureCaption(Text);
                float rsize = Math.Max(10f, Math.Min(Height - 14, 15));
                const float rgap = 7f;
                if (8 + rsize + rgap + textWidth + 4 <= Width)
                {
                    float startX = Math.Max(8f, (Width - (rsize + rgap + textWidth)) / 2f);
                    var rbox = new RectangleF(startX, (Height - rsize) / 2f, rsize, rsize);
                    double seconds = Motion.Enabled ? (Motion.NowMs - _busySince) / 1000.0 : 0.45;
                    ProgressRing.Paint(g, rbox, AccentColor, seconds, Math.Max(1.8f, rsize / 7.5f));
                    var tr = new Rectangle((int)(rbox.Right + rgap), 0, (int)(Width - rbox.Right - rgap), Height);
                    ColorEmoji.DrawText(g, Text, Font, tr, fore,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    return;
                }
            }

            // Action glyph + centred label (New, Save, Start, …): draw the icon and the
            // text together, centred as a group — when both fit. When they do not, this
            // falls through to the plain caption below and the glyph is left out.
            if (Glyph != ActionGlyph.None && IconKind == NavIconKind.None && GlyphFits)
            {
                int textWidth = MeasureCaption(Text);
                float gsize = Math.Min(Height - 12, 17);
                if (gsize < 12) gsize = 12;
                const float gap = 7f;
                float totalW = gsize + gap + textWidth;
                float startX = GlyphAlignLeft ? GlyphStartPad : (Width - badgeRoom - totalW) / 2f;
                if (startX < 8) startX = 8;
                var gbox = new RectangleF(startX, (Height - gsize) / 2f, gsize, gsize);
                // Its own colour when it has one (Record's red dot) — never while disabled: then it greys with the words.
                Color glyphInk = GlyphColor.IsEmpty || !Enabled ? fore : GlyphColor;
                DrawActionGlyph(g, Glyph, gbox, glyphInk);
                if (!string.IsNullOrEmpty(BadgeText))
                {
                    DrawBadgeBubble(g, gbox, glyphInk);
                }
                var tr = new Rectangle((int)(gbox.Right + gap), 0,
                    Math.Max(0, (int)(Width - gbox.Right - gap) - badgeRoom), Height);
                ColorEmoji.DrawText(g, Text, Font, tr, fore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }

            // Optional left-hand glyph (used by the navigation sidebar).
            int textLeftPad = Math.Max(0, Padding.Left);
            if (IconKind != NavIconKind.None)
            {
                float iconSize = Math.Min(Height - 16, 20);
                if (iconSize < 10) iconSize = 10;
                // A row under another is set in by a step (Profiles under Clicker).
                var iconBox = new RectangleF(14f + IndentPx, (Height - iconSize) / 2f, iconSize, iconSize);
                // The icon's own hue when one is set (disabled still greys out). A nav row keeps
                // it calm at rest — 72% of the way from its muted text to the hue — and shows the
                // full hue under the pointer and when open.
                Color iconFore;
                if (IconColor.IsEmpty || !Enabled) { iconFore = fore; }
                else if (NavRow)
                {
                    Color open = SelectedIconColor.IsEmpty ? IconColor : SelectedIconColor;
                    iconFore = Mix(Mix(Mix(ForeColor, IconColor, 0.72), IconColor, _hoverT), open, _selT);
                }
                else { iconFore = IconColor; }

                if (RevealShortcut && !string.IsNullOrEmpty(ShortcutHint) && Enabled)
                {
                    DrawKeyTip(g, iconBox, iconFore);
                }
                else
                {
                    NavIcons.Draw(g, IconKind, iconBox, iconFore);
                }
                textLeftPad = (int)(iconBox.Right + 8f);
            }

            // The chevron of a row holding others, at its right end: › while they are hidden, turning to ⌄ as they show.
            int trailing = badgeRoom;
            if (NavRow && HasExpander)
            {
                trailing = ExpanderPx;
                float cx = rect.Right - 4 - ExpanderPx / 2f, cy = Height / 2f;
                float s = Math.Max(3.5f, Math.Min(5.5f, Height * 0.12f));
                var prevMode = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (_overExpander && Enabled)
                {
                    // Under the pointer it is a button of its own: a soft round lift behind it.
                    float r = Math.Min(ExpanderPx / 2f, Height / 2f - 3);
                    using (var lift = new SolidBrush(Color.FromArgb(46, fore)))
                    {
                        g.FillEllipse(lift, cx - r, cy - r, r * 2, r * 2);
                    }
                }
                var saved = g.Transform;
                try
                {
                    g.TranslateTransform(cx, cy);
                    g.RotateTransform(90f * Math.Max(0f, Math.Min(1f, ExpandProgress)));
                    using (var pen = new Pen(Color.FromArgb(_overExpander ? 255 : 205, fore), Math.Max(1.5f, Height / 26f))
                           { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    {
                        g.DrawLines(pen, new[] { new PointF(-s * 0.5f, -s), new PointF(s * 0.5f, 0), new PointF(-s * 0.5f, s) });
                    }
                }
                finally
                {
                    g.Transform = saved;
                    g.SmoothingMode = prevMode;
                }
            }

            // The ↗ of a page that opens in its own window, at the right (left of a chevron); the label stops short of it.
            if (NavRow && OpensWindow && Enabled)
            {
                float s = Math.Max(8f, Math.Min(11f, Height * 0.26f));
                float ax = rect.Right - 12 - s - trailing, ay = (Height - s) / 2f;
                var prevMode = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.FromArgb(215, fore), 1.6f)
                       { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawLine(pen, ax, ay + s, ax + s, ay);
                    g.DrawLines(pen, new[]
                    {
                        new PointF(ax + s * 0.40f, ay), new PointF(ax + s, ay), new PointF(ax + s, ay + s * 0.60f)
                    });
                }
                g.SmoothingMode = prevMode;
                trailing += OpensWindowRoom;
            }

            // The note ("Off") right of the label, before the ↗ when both are there.
            if (NavRow && !string.IsNullOrEmpty(TrailingNote))
            {
                using (var small = new Font(Font.FontFamily, Math.Max(7f, Font.SizeInPoints * 0.78f), FontStyle.Regular))
                {
                    Size size = TextRenderer.MeasureText(g, TrailingNote, small, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    int right = rect.Right - 10 - trailing;
                    var noteRect = new Rectangle(right - size.Width, rect.Y, size.Width, rect.Height);
                    // Toward grey, not toward BackColor: a row's own back colour can be see-through (the rail shows).
                    TextRenderer.DrawText(g, TrailingNote, small, noteRect, Mix(fore, Color.Gray, 0.4),
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    trailing += size.Width + 14;
                }
            }

            TextFormatFlags flags = TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            var textRect = new Rectangle(rect.X, rect.Y, Math.Max(0, rect.Width - trailing), rect.Height);
            switch (TextAlign)
            {
                case ContentAlignment.TopLeft:
                case ContentAlignment.MiddleLeft:
                case ContentAlignment.BottomLeft:
                    flags |= TextFormatFlags.Left;
                    textRect = new Rectangle(rect.X + textLeftPad, rect.Y,
                        Math.Max(0, rect.Width - textLeftPad - trailing), rect.Height);
                    break;
                case ContentAlignment.TopRight:
                case ContentAlignment.MiddleRight:
                case ContentAlignment.BottomRight:
                    flags |= TextFormatFlags.Right;
                    textRect = new Rectangle(rect.X, rect.Y,
                        Math.Max(0, rect.Width - Math.Max(0, Padding.Right) - trailing), rect.Height);
                    break;
                default:
                    flags |= TextFormatFlags.HorizontalCenter;
                    break;
            }

            // ColorEmoji keeps an emoji in the label (🔒 Lock vault, the profile icon picker) in colour;
            // TextRenderer painted it as a flat silhouette in the button's text colour.
            ColorEmoji.DrawText(g, Text, Font, textRect, fore, flags);
        }

        /// <summary>
        /// The row's shortcut digit on a key cap in the icon's colour, where the icon was — so
        /// holding Ctrl turns the rail into a numbered keypad without moving anything.
        /// </summary>
        private void DrawKeyTip(Graphics g, RectangleF box, Color hue)
        {
            var r = Rectangle.Round(RectangleF.Inflate(box, 1f, 1f));
            using (var path = Rounded(r, Math.Max(3, r.Height / 4)))
            using (var cap = new SolidBrush(hue))
            {
                g.FillPath(cap, path);
            }
            using (var digit = new Font(Font.FontFamily, Math.Max(8f, r.Height * 0.66f), FontStyle.Bold, GraphicsUnit.Pixel))
            {
                TextRenderer.DrawText(g, ShortcutHint, digit, r, Theme.ReadableOn(hue),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); if (NavRow) { Invalidate(); } }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); if (NavRow) { Invalidate(); } }

        // Crisp little line/solid glyphs for the action buttons, drawn inside box.
        private static void DrawActionGlyph(Graphics g, ActionGlyph glyph, RectangleF box, Color color)
        {
            var prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = RectangleF.Inflate(box, -1.5f, -1.5f);
            float cx = r.Left + r.Width / 2f;
            float cy = r.Top + r.Height / 2f;
            using (var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var br = new SolidBrush(color))
            {
                switch (glyph)
                {
                    case ActionGlyph.Plus:
                        g.DrawLine(pen, cx, r.Top, cx, r.Bottom);
                        g.DrawLine(pen, r.Left, cy, r.Right, cy);
                        break;
                    case ActionGlyph.Save: // floppy disk
                        using (var p = Rounded(Rectangle.Round(r), 3))
                            g.DrawPath(pen, p);
                        g.DrawRectangle(pen, r.Left + 3, r.Top, r.Width - 7, r.Height * 0.32f);
                        g.DrawRectangle(pen, r.Left + 3, cy + 1, r.Width - 6, r.Height * 0.38f);
                        break;
                    case ActionGlyph.Copy: // two overlapping sheets
                        g.DrawRectangle(pen, r.Left, r.Top + 3, r.Width - 4, r.Height - 4);
                        g.DrawRectangle(pen, r.Left + 4, r.Top, r.Width - 4, r.Height - 4);
                        break;
                    case ActionGlyph.Trash: // bin
                        g.DrawLine(pen, r.Left, r.Top + 2, r.Right, r.Top + 2);
                        g.DrawLine(pen, cx - 2, r.Top + 2, cx - 2, r.Top - 0.5f);
                        g.DrawLine(pen, cx + 2, r.Top + 2, cx + 2, r.Top - 0.5f);
                        g.DrawLines(pen, new[] {
                            new PointF(r.Left + 1.5f, r.Top + 2), new PointF(r.Left + 2.5f, r.Bottom),
                            new PointF(r.Right - 2.5f, r.Bottom), new PointF(r.Right - 1.5f, r.Top + 2) });
                        g.DrawLine(pen, cx, r.Top + 5, cx, r.Bottom - 2);
                        break;
                    case ActionGlyph.Play: // triangle
                        g.FillPolygon(br, new[] {
                            new PointF(r.Left + 1, r.Top), new PointF(r.Right, cy), new PointF(r.Left + 1, r.Bottom) });
                        break;
                    case ActionGlyph.Stop: // rounded square
                        using (var p = Rounded(Rectangle.Round(RectangleF.Inflate(r, -0.5f, -0.5f)), 2))
                            g.FillPath(br, p);
                        break;
                    case ActionGlyph.Gauge: // speedometer
                        g.DrawArc(pen, r.Left, r.Top + 1, r.Width, r.Height, 180, 180);
                        g.DrawLine(pen, cx, cy + 1, r.Right - 2, r.Top + 3);
                        g.FillEllipse(br, cx - 1.3f, cy - 0.3f, 2.6f, 2.6f);
                        break;
                    case ActionGlyph.Person: // head + shoulders
                        g.DrawEllipse(pen, cx - 2.6f, r.Top, 5.2f, 5.2f);
                        g.DrawArc(pen, r.Left, cy + 1.5f, r.Width, r.Height, 200, 140);
                        break;
                    case ActionGlyph.Info:
                        g.DrawEllipse(pen, r.Left, r.Top, r.Width, r.Height);
                        g.FillEllipse(br, cx - 0.9f, r.Top + 2.5f, 1.8f, 1.8f);
                        g.DrawLine(pen, cx, cy, cx, r.Bottom - 2);
                        break;
                    case ActionGlyph.Pencil:
                    {
                        // A pencil on the slant: its body (an outline), the cap line near the top end, the point at the
                        // bottom left. It was one diagonal line, which read as a slash (round 40).
                        float len = (float)Math.Sqrt((r.Width - 1) * (r.Width - 1) + (r.Height - 1) * (r.Height - 1));
                        var dir = new PointF((r.Width - 1) / len, -(r.Height - 1) / len);
                        var perp = new PointF(-dir.Y, dir.X);
                        float hw = Math.Max(1.8f, r.Width * 0.15f);
                        PointF At(float t) => new PointF(r.Left + dir.X * len * t, r.Bottom + dir.Y * len * t);
                        PointF Off(PointF p, float k) => new PointF(p.X + perp.X * hw * k, p.Y + perp.Y * hw * k);
                        PointF tipEnd = At(0.30f), top = At(1f), cap = At(0.80f);
                        g.DrawPolygon(pen, new[] { Off(tipEnd, 1), Off(top, 1), Off(top, -1), Off(tipEnd, -1) });
                        g.DrawLine(pen, Off(cap, 1), Off(cap, -1));
                        g.DrawLines(pen, new[] { Off(tipEnd, 1), At(0f), Off(tipEnd, -1) });
                        break;
                    }
                    case ActionGlyph.Download:
                        g.DrawLine(pen, cx, r.Top, cx, r.Bottom - 3);
                        g.DrawLines(pen, new[] { new PointF(cx - 3, cy + 1), new PointF(cx, r.Bottom - 3), new PointF(cx + 3, cy + 1) });
                        g.DrawLine(pen, r.Left, r.Bottom, r.Right, r.Bottom);
                        break;
                    case ActionGlyph.Import:
                        g.DrawLine(pen, cx, r.Bottom - 3, cx, r.Top);
                        g.DrawLines(pen, new[] { new PointF(cx - 3, r.Top + 3), new PointF(cx, r.Top), new PointF(cx + 3, r.Top + 3) });
                        g.DrawLine(pen, r.Left, r.Bottom, r.Right, r.Bottom);
                        break;
                    case ActionGlyph.Search:
                        g.DrawEllipse(pen, r.Left, r.Top, r.Width - 3, r.Height - 3);
                        g.DrawLine(pen, r.Right - 3, r.Bottom - 3, r.Right, r.Bottom);
                        break;
                    case ActionGlyph.Upload: // arrow up off a base line (Export: out of Tempo)
                        g.DrawLine(pen, cx, r.Bottom - 3, cx, r.Top);
                        g.DrawLines(pen, new[] { new PointF(cx - 3, r.Top + 3), new PointF(cx, r.Top), new PointF(cx + 3, r.Top + 3) });
                        g.DrawLine(pen, r.Left, r.Bottom, r.Right, r.Bottom);
                        break;
                    case ActionGlyph.Rename: // a letter A and a text cursor beside it
                    {
                        float aw = r.Width * 0.62f;
                        g.DrawLines(pen, new[] {
                            new PointF(r.Left, r.Bottom), new PointF(r.Left + aw / 2f, r.Top + 0.5f), new PointF(r.Left + aw, r.Bottom) });
                        g.DrawLine(pen, r.Left + aw * 0.24f, r.Top + r.Height * 0.64f, r.Left + aw * 0.76f, r.Top + r.Height * 0.64f);
                        float ix = r.Right - 1f;
                        g.DrawLine(pen, ix, r.Top, ix, r.Bottom);
                        g.DrawLine(pen, ix - 1.8f, r.Top, ix + 1.2f, r.Top);
                        g.DrawLine(pen, ix - 1.8f, r.Bottom, ix + 1.2f, r.Bottom);
                        break;
                    }
                    case ActionGlyph.Notes: // a sheet with a folded corner and lines of text
                    {
                        float fold = Math.Max(3f, r.Width * 0.28f);
                        float left = r.Left + 1.5f, right = r.Right - 1f;
                        g.DrawLines(pen, new[] {
                            new PointF(right - fold, r.Top), new PointF(left, r.Top), new PointF(left, r.Bottom),
                            new PointF(right, r.Bottom), new PointF(right, r.Top + fold), new PointF(right - fold, r.Top),
                            new PointF(right - fold, r.Top + fold), new PointF(right, r.Top + fold) });
                        float ly = r.Top + r.Height * 0.52f;
                        g.DrawLine(pen, left + 2.5f, ly, right - 2.5f, ly);
                        g.DrawLine(pen, left + 2.5f, ly + r.Height * 0.24f, right - 4f, ly + r.Height * 0.24f);
                        break;
                    }
                    case ActionGlyph.Link: // two chain links, at a slant
                    {
                        var saved = g.Transform;
                        try
                        {
                            g.TranslateTransform(cx, cy);
                            g.RotateTransform(-45f);
                            float lw = r.Width * 0.62f, lh = r.Height * 0.40f;
                            using (var a = Capsule(new RectangleF(-lw + lw * 0.22f, -lh / 2f, lw, lh)))
                            using (var b = Capsule(new RectangleF(-lw * 0.22f, -lh / 2f, lw, lh)))
                            {
                                g.DrawPath(pen, a);
                                g.DrawPath(pen, b);
                            }
                        }
                        finally { g.Transform = saved; }
                        break;
                    }
                    case ActionGlyph.Wrench: // handle from the bottom left, the open jaw top right
                    {
                        float jr = Math.Max(2.6f, r.Width * 0.27f);
                        var jc = new PointF(r.Right - jr, r.Top + jr);
                        g.DrawArc(pen, jc.X - jr, jc.Y - jr, jr * 2f, jr * 2f, 0f, 270f);
                        double a = 135 * Math.PI / 180;
                        g.DrawLine(pen, r.Left + 1f, r.Bottom - 1f, jc.X + (float)(jr * Math.Cos(a)), jc.Y + (float)(jr * Math.Sin(a)));
                        break;
                    }
                    case ActionGlyph.Recycle: // three arrows chasing round a triangle
                    {
                        var p0 = new PointF(cx, r.Top + 0.5f);
                        var p1 = new PointF(r.Right, r.Bottom - 0.5f);
                        var p2 = new PointF(r.Left, r.Bottom - 0.5f);
                        ChaseArrow(g, pen, br, p0, p1);
                        ChaseArrow(g, pen, br, p1, p2);
                        ChaseArrow(g, pen, br, p2, p0);
                        break;
                    }
                    case ActionGlyph.Star:
                    {
                        float ro = Math.Min(r.Width, r.Height) / 2f + 0.8f, ri = ro * 0.45f;
                        var pts = new PointF[10];
                        for (int i = 0; i < 10; i++)
                        {
                            double ang = Math.PI / 5 * i - Math.PI / 2;
                            float rad = i % 2 == 0 ? ro : ri;
                            pts[i] = new PointF(cx + (float)(Math.Cos(ang) * rad), cy + 0.6f + (float)(Math.Sin(ang) * rad));
                        }
                        g.FillPolygon(br, pts);
                        break;
                    }
                    case ActionGlyph.Refresh: // a circle with a gap at the top and an arrow at its end
                    {
                        float rr = Math.Min(r.Width, r.Height) / 2f;
                        g.DrawArc(pen, cx - rr, cy - rr, rr * 2f, rr * 2f, 300f, 290f);
                        double t = 230 * Math.PI / 180;
                        var end = new PointF(cx + (float)(rr * Math.Cos(t)), cy + (float)(rr * Math.Sin(t)));
                        ArrowHead(g, br, end, new PointF((float)-Math.Sin(t), (float)Math.Cos(t)), Math.Max(3.2f, rr * 0.75f));
                        break;
                    }
                    case ActionGlyph.Record: // a filled dot
                    {
                        float rd = Math.Min(r.Width, r.Height) * 0.40f;
                        g.FillEllipse(br, cx - rd, cy - rd, rd * 2f, rd * 2f);
                        break;
                    }
                    case ActionGlyph.Crosshair: // a ring, four ticks reaching in, the centre dot
                    {
                        float rr = Math.Min(r.Width, r.Height) / 2f - 0.5f;
                        g.DrawEllipse(pen, cx - rr * 0.68f, cy - rr * 0.68f, rr * 1.36f, rr * 1.36f);
                        g.DrawLine(pen, cx, cy - rr - 0.5f, cx, cy - rr * 0.38f);
                        g.DrawLine(pen, cx, cy + rr + 0.5f, cx, cy + rr * 0.38f);
                        g.DrawLine(pen, cx - rr - 0.5f, cy, cx - rr * 0.38f, cy);
                        g.DrawLine(pen, cx + rr + 0.5f, cy, cx + rr * 0.38f, cy);
                        g.FillEllipse(br, cx - 1.3f, cy - 1.3f, 2.6f, 2.6f);
                        break;
                    }
                    case ActionGlyph.Toggle: // a switch: a rounded track, its knob at the right
                    {
                        float th = r.Height * 0.62f;
                        var track = new RectangleF(r.Left, cy - th / 2f, r.Width, th);
                        using (var p = Capsule(track)) { g.DrawPath(pen, p); }
                        float kd = th - 3.4f;
                        g.FillEllipse(br, track.Right - kd - 1.7f, cy - kd / 2f, kd, kd);
                        break;
                    }
                    case ActionGlyph.SelectAll: // a box with a tick, another box behind it
                    {
                        float s = r.Width * 0.72f;
                        g.DrawLines(pen, new[] {
                            new PointF(r.Left + r.Width - s + 2.5f, r.Top), new PointF(r.Right, r.Top),
                            new PointF(r.Right, r.Top + s - 2.5f) });
                        var front = new RectangleF(r.Left, r.Bottom - s, s, s);
                        g.DrawRectangle(pen, front.X, front.Y, front.Width, front.Height);
                        g.DrawLines(pen, new[] {
                            new PointF(front.Left + s * 0.22f, front.Top + s * 0.52f),
                            new PointF(front.Left + s * 0.43f, front.Top + s * 0.72f),
                            new PointF(front.Left + s * 0.80f, front.Top + s * 0.28f) });
                        break;
                    }
                    case ActionGlyph.Eye: // the almond of an eye and its pupil
                    {
                        float eh = r.Height * 0.70f;
                        using (var p = new GraphicsPath())
                        {
                            p.AddBezier(r.Left, cy, r.Left + r.Width * 0.30f, cy - eh * 0.75f, r.Right - r.Width * 0.30f, cy - eh * 0.75f, r.Right, cy);
                            p.AddBezier(r.Right, cy, r.Right - r.Width * 0.30f, cy + eh * 0.75f, r.Left + r.Width * 0.30f, cy + eh * 0.75f, r.Left, cy);
                            p.CloseFigure();
                            g.DrawPath(pen, p);
                        }
                        float pr = Math.Max(1.8f, r.Height * 0.18f);
                        g.FillEllipse(br, cx - pr, cy - pr, pr * 2f, pr * 2f);
                        break;
                    }
                    case ActionGlyph.ChevronDown:
                    case ActionGlyph.ChevronUp:
                    {
                        // A chevron in a soft ring: down = the section opens below, up = it folds back up.
                        float rr = Math.Min(r.Width, r.Height) / 2f;
                        using (var ring = new Pen(Color.FromArgb(110, color), 1.2f))
                        {
                            g.DrawEllipse(ring, cx - rr, cy - rr, rr * 2f, rr * 2f);
                        }
                        float k = rr * 0.42f, dy = glyph == ActionGlyph.ChevronDown ? k * 0.55f : -k * 0.55f;
                        g.DrawLines(pen, new[] {
                            new PointF(cx - k, cy - dy), new PointF(cx, cy + dy), new PointF(cx + k, cy - dy) });
                        break;
                    }
                    case ActionGlyph.Clear: // a circle with a cross in it
                    {
                        float rr = Math.Min(r.Width, r.Height) / 2f;
                        g.DrawEllipse(pen, cx - rr, cy - rr, rr * 2f, rr * 2f);
                        float k = rr * 0.42f;
                        g.DrawLine(pen, cx - k, cy - k, cx + k, cy + k);
                        g.DrawLine(pen, cx + k, cy - k, cx - k, cy + k);
                        break;
                    }
                }
            }
            g.SmoothingMode = prev;
        }

        /// <summary>A rounded bar whose ends are half circles (a chain link).</summary>
        private static GraphicsPath Capsule(RectangleF r)
        {
            var path = new GraphicsPath();
            float d = Math.Min(r.Width, r.Height);
            path.AddArc(r.Left, r.Top, d, d, 90, 180);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 180);
            path.CloseFigure();
            return path;
        }

        /// <summary>A small filled arrowhead at <paramref name="tip"/>, pointing along <paramref name="dir"/> (a unit vector).</summary>
        private static void ArrowHead(Graphics g, Brush br, PointF tip, PointF dir, float size)
        {
            // The two back corners: back along the direction, out to either side.
            var back = new PointF(tip.X - dir.X * size, tip.Y - dir.Y * size);
            var side = new PointF(-dir.Y * size * 0.55f, dir.X * size * 0.55f);
            g.FillPolygon(br, new[] {
                new PointF(tip.X + dir.X * size * 0.25f, tip.Y + dir.Y * size * 0.25f),
                new PointF(back.X + side.X, back.Y + side.Y), new PointF(back.X - side.X, back.Y - side.Y) });
        }

        /// <summary>One side of the recycling triangle: a line from near <paramref name="a"/> to near <paramref name="b"/>, its arrowhead at b.</summary>
        private static void ChaseArrow(Graphics g, Pen pen, Brush br, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1f) { return; }
            var dir = new PointF(dx / len, dy / len);
            float gap = len * 0.16f, head = Math.Max(2.6f, len * 0.26f);
            var from = new PointF(a.X + dir.X * gap, a.Y + dir.Y * gap);
            var tip = new PointF(b.X - dir.X * gap, b.Y - dir.Y * gap);
            g.DrawLine(pen, from, new PointF(tip.X - dir.X * head * 0.6f, tip.Y - dir.Y * head * 0.6f));
            ArrowHead(g, br, tip, dir, head);
        }

        private static Color Shade(Color c, double f)
        {
            if (f >= 0)
            {
                return Color.FromArgb(c.A,
                    (int)(c.R + (255 - c.R) * f),
                    (int)(c.G + (255 - c.G) * f),
                    (int)(c.B + (255 - c.B) * f));
            }
            f = -f;
            return Color.FromArgb(c.A,
                (int)(c.R * (1 - f)),
                (int)(c.G * (1 - f)),
                (int)(c.B * (1 - f)));
        }

        private static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;

            var path = new GraphicsPath();
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
