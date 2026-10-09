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
        None, Plus, Save, Copy, Trash, Play, Stop, Gauge, Person, Info, Pencil, Download, Import, Search
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
        /// Sidebar "active tab" style: instead of a heavy solid-accent fill, the button
        /// paints a soft accent-tinted background, an accent indicator bar down its left
        /// edge, and accent-coloured text/icon. Set <see cref="AccentColor"/> too.
        /// </summary>
        public bool Selected { get; set; }

        /// <summary>Accent used for the <see cref="Selected"/> indicator/tint.</summary>
        public Color AccentColor { get; set; } = Color.DodgerBlue;

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
        private float _hoverT;
        private float _selT;
        private bool _lastSelected;

        private const int FadeMs = 120;
        private static System.Windows.Forms.Timer _fadeTimer;
        private static readonly System.Collections.Generic.List<RoundedButton> _fading =
            new System.Collections.Generic.List<RoundedButton>();
        private static long _lastFadeTick;

        private void BeginFade()
        {
            if (IsDisposed || !IsHandleCreated) { return; }
            if (!_fading.Contains(this)) { _fading.Add(this); }
            if (_fadeTimer == null)
            {
                _fadeTimer = new System.Windows.Forms.Timer { Interval = 16 };
                _fadeTimer.Tick += (s, e) => FadeTick();
            }
            if (!_fadeTimer.Enabled)
            {
                _lastFadeTick = Environment.TickCount64;
                _fadeTimer.Start();
            }
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

            if (_fading.Count == 0) { _fadeTimer.Stop(); }
        }

        /// <summary>Moves both eased values toward their targets; false once settled.</summary>
        private bool Advance(float step)
        {
            float hoverTarget = _hover && Enabled ? 1f : 0f;
            float selTarget = Selected && Enabled ? 1f : 0f;
            bool busy = false;

            if (Math.Abs(_hoverT - hoverTarget) > 0.001f)
            {
                _hoverT = _hoverT < hoverTarget
                    ? Math.Min(hoverTarget, _hoverT + step)
                    : Math.Max(hoverTarget, _hoverT - step);
                busy = true;
            }
            if (Math.Abs(_selT - selTarget) > 0.001f)
            {
                _selT = _selT < selTarget
                    ? Math.Min(selTarget, _selT + step)
                    : Math.Max(selTarget, _selT - step);
                busy = true;
            }
            return busy;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; BeginFade(); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _down = false; BeginFade(); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; Invalidate(); }
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
            int text = MeasureCaption(caption);

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
                return (int)Math.Ceiling(14 + iconSize + 8) + text + 8 + (OpensWindow ? OpensWindowRoom : 0);
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
            int text = MeasureCaption(caption ?? Text);
            float gsize = Math.Min(Height - 12, 17);
            if (gsize < 12) { gsize = 12; }
            return (int)Math.Ceiling(8 + gsize + 7 + text + rightMargin);
        }

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
                using (var barBrush = new SolidBrush(AccentColor))
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
            float strength = Math.Max(_hoverT, _selT);
            if (_down)
            {
                // A press stays instant — feedback for your own click should never lag.
                fill = Shade(fill, light ? -0.06 : -0.08);
                strength = 1f;
            }
            if (strength <= 0.01f) { return; }

            int a = (int)Math.Round(255 * Math.Min(1f, strength));
            using (var path = Rounded(rect, CornerRadius))
            using (var brush = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(1, Width), Math.Max(2, Height)),
                Color.FromArgb(a, Shade(fill, 0.07)), Color.FromArgb(a, Shade(fill, -0.05)),
                LinearGradientMode.Vertical))
            {
                g.FillPath(brush, path);
            }
        }

        /// <summary>Every other button: its own fill, a little lighter at the top.</summary>
        private void PaintButtonBackground(Graphics g, Rectangle rect)
        {
            Color back = BackColor;
            if (!Enabled)
            {
                back = Mix(BackColor, Color.Gray, 0.45);
            }
            else if (_down)
            {
                // A press stays instant — feedback for your own click should never lag.
                back = Shade(BackColor, -0.12);
            }
            else
            {
                // Blend the three resting looks by their eased weights instead of
                // picking one: plain → hover lift → selected accent wash.
                Color unselected = Mix(BackColor, Shade(BackColor, 0.12), _hoverT);
                Color selected = Mix(BackColor, AccentColor, 0.20 + 0.10 * _hoverT);
                back = Mix(unselected, selected, _selT);
            }

            using (var path = Rounded(rect, CornerRadius))
            {
                // Subtle top-lighter → bottom-darker gradient gives the flat buttons a
                // little depth to match the card UI. Solid fallback for zero-height.
                if (Height > 1)
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
                float startX = (Width - totalW) / 2f;
                if (startX < 8) startX = 8;
                var gbox = new RectangleF(startX, (Height - gsize) / 2f, gsize, gsize);
                DrawActionGlyph(g, Glyph, gbox, fore);
                var tr = new Rectangle((int)(gbox.Right + gap), 0,
                    (int)(Width - gbox.Right - gap), Height);
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
                var iconBox = new RectangleF(14f, (Height - iconSize) / 2f, iconSize, iconSize);
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

            // The ↗ of a page that opens in its own window, at the right; the label stops short of it.
            int trailing = 0;
            if (NavRow && OpensWindow && Enabled)
            {
                float s = Math.Max(8f, Math.Min(11f, Height * 0.26f));
                float ax = rect.Right - 12 - s, ay = (Height - s) / 2f;
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
                trailing = OpensWindowRoom;
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
                        g.DrawLine(pen, r.Left, r.Bottom, r.Right - 1, r.Top + 1);
                        g.DrawLine(pen, r.Left, r.Bottom, r.Left + 2, r.Bottom - 2);
                        break;
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
                }
            }
            g.SmoothingMode = prev;
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
