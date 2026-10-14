using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// The vector icon a tray menu item shows in its margin. Painted by the
    /// renderer in a theme-derived hue, so it stays crisp and consistent instead
    /// of relying on the OS's multi-coloured emoji (which rendered 💬 in full
    /// colour next to a flat 🗔 — the look this replaces).
    /// </summary>
    public enum TrayGlyph
    {
        None, Brand, Window, Play, Stop, Pin, Speech, Move, Power,
        // One per job, so the icon column says something. Before these, four different
        // items shared the speech bubble and three the window outline — an icon that
        // appears beside Settings, "Check for updates" AND "Screenshot alerts" tells you
        // nothing about any of them.
        Captions, History, Inbox, Bell, Camera, Gear, Refresh, Code, Profile, Accounts, Lock,
        /// <summary>An update is waiting to be installed (the "Check for updates" item, once one is known).</summary>
        Download,
        /// <summary>Restart Tempo: the power ring and stem with an arrow where the ring opens — off, and straight back on.</summary>
        Restart,
        /// <summary>A submenu choice: a filled dot beside the one that is current, nothing beside the rest.</summary>
        Dot,
        /// <summary>The running badges (Overlays): a pill with its status dot, like the badge itself.</summary>
        Badge,
        /// <summary>Windows' shortcuts held back during a run: a keyboard (Keybinds' colour).</summary>
        Keys
    }

    /// <summary>
    /// Per-item styling Tempo attaches to a tray menu item via <c>Tag</c>. The
    /// renderer reads it to paint the icon and to recognise the branded header
    /// row. Colours are resolved from the ACTIVE theme at paint time (not stored
    /// here), so a theme change is reflected with no re-tagging.
    ///
    /// The right-hand hint ("F6", "3 missed") is the item's ShortcutKeyDisplayString, so
    /// the menu makes room for it: WinForms sizes a menu's shortcut column to the widest
    /// one. It used to be painted over the item's right edge with nothing reserved, and a
    /// long hotkey beside a long label in a long language had nowhere to go but on top of
    /// the label.
    /// </summary>
    public sealed class TrayItemStyle
    {
        public TrayGlyph Glyph;
        public bool Header;
        /// <summary>Header only: small caption drawn to the right (e.g. version).</summary>
        public string Caption;
        /// <summary>A live STATUS row: small muted text with a coloured state dot,
        /// non-interactive — the menu's at-a-glance "what is Tempo doing right now".</summary>
        public bool Status;
        /// <summary>Status only: the state dot's colour (Success = active, muted = idle).</summary>
        public Color Dot;
        /// <summary>Status only: the dot breathes while something is running.</summary>
        public bool Pulse;
        /// <summary>
        /// The item switches something on or off: its Checked state is drawn as a small switch
        /// in the shortcut column — Settings' own toggle switch, in its colours — and its icon
        /// is in the muted ink while off. Give the item <see cref="SwitchRoom"/> as its
        /// ShortcutKeyDisplayString so the column is wide enough.
        /// </summary>
        public bool Toggle;
        /// <summary>The hint is something to act on ("3 missed"): drawn in the warning colour.</summary>
        public bool HintAlert;

        /// <summary>A toggle's shortcut text: figure spaces, which every font gives a digit's width.</summary>
        public const string SwitchRoom = "     ";

        public TrayItemStyle(TrayGlyph glyph) { Glyph = glyph; }
    }

    /// <summary>
    /// Draws Tempo's menus (the tray menu foremost) in Tempo's own theme instead of
    /// the stark white system look — a branded header banner, crisp vector icons, the
    /// sidebar's selection look, and themed separators.
    ///
    /// EVERY COLOUR COMES FROM THE APP. An icon wears the colour of the part of Tempo it
    /// belongs to — the colour of that part's sidebar tab (SectionColors) — or, for an
    /// action that belongs to no tab, the palette's own colour for it: Start is Success
    /// and Stop is Danger, exactly as the Clicker tab's buttons. The menu used to carry a
    /// fixed palette of its own that matched nothing in the window, a highlight in a solid
    /// accent with white text on it (about 2:1 on Slate's sky blue, where the rest of the
    /// app puts dark text), and "off" toggles as muddy blends of their colour and the
    /// background. Colours drawn outside the palette go through the palette's HDR
    /// correction like the palette itself (Theme.Painted).
    /// </summary>
    public sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer, IDisposable
    {
        private readonly Theme _theme;
        private readonly bool _lightBg;

        /// <summary>
        /// How far the hot row's surface is washed toward the accent: the sidebar's selected
        /// tab under the pointer (RoundedButton: 0.20 selected, +0.10 hovered).
        /// </summary>
        private const double SelectedWash = 0.30;

        // ── menu animation ────────────────────────────────────────────────────
        // A light 30 fps timer runs while the menu is open: each item's hover
        // highlight FADES in/out (eased alpha per item) instead of snapping, and
        // the header's accent band breathes gently. The timer parks itself the
        // moment the menu closes and every fade has settled — zero cost while no
        // menu is on screen.
        private readonly System.Collections.Generic.Dictionary<ToolStripItem, float> _hover =
            new System.Collections.Generic.Dictionary<ToolStripItem, float>();
        private System.Windows.Forms.Timer _anim;

        // Every menu this renderer is animating right now — the tray menu AND any submenu
        // open off it. It used to be one field, so opening a submenu took the animation
        // over and the row you had come from was left half-faded until you went back.
        private readonly System.Collections.Generic.HashSet<ToolStrip> _owners =
            new System.Collections.Generic.HashSet<ToolStrip>();

        public ThemedMenuRenderer(Theme theme) : base(new ThemedColorTable(theme))
        {
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);
            _lightBg = SectionColors.IsLight(_theme);
            RoundedEdges = true;
        }

        private static TrayItemStyle StyleOf(ToolStripItem item)
        {
            return item?.Tag as TrayItemStyle;
        }

        /// <summary>Current eased hover alpha for an item, advancing the animation.</summary>
        private float HoverAlpha(ToolStripItem item)
        {
            float a;
            _hover.TryGetValue(item, out a);
            return a;
        }

        private void EnsureAnimating(ToolStrip owner)
        {
            if (owner != null) { _owners.Add(owner); }
            if (_anim == null)
            {
                _anim = new System.Windows.Forms.Timer { Interval = 33 };
                _anim.Tick += (s, e) => AnimTick();
            }
            if (!_anim.Enabled) { _anim.Start(); }
        }

        private void AnimTick()
        {
            try
            {
                // Fade each item's highlight on a CLOCK, not per frame.
                //
                // This used to move a fixed fraction of the remaining distance every
                // tick (a += (target - a) * 0.35), so how fast a highlight faded
                // depended on how reliably the timer fired — and a menu's timer is a
                // WinForms one, whose WM_TIMER is the lowest-priority message Windows
                // delivers. Under load the fade both stuttered and took longer.
                long now = Environment.TickCount64;
                float step = Math.Min(1f, (now - _lastAnimTick) / (float)HoverFadeMs);
                _lastAnimTick = now;

                foreach (ToolStrip owner in new System.Collections.Generic.List<ToolStrip>(_owners))
                {
                    if (owner == null || owner.IsDisposed || !owner.Visible)
                    {
                        // Closed: its rows start from rest next time it opens.
                        _owners.Remove(owner);
                        if (owner != null && !owner.IsDisposed)
                        {
                            foreach (ToolStripItem gone in owner.Items) { _hover.Remove(gone); }
                        }
                        continue;
                    }

                    foreach (ToolStripItem item in owner.Items)
                    {
                        float target = item.Selected && item.Enabled ? 1f : 0f;
                        float a = HoverAlpha(item);
                        if (a == target) { continue; }

                        float next = a < target
                            ? Math.Min(target, a + step)
                            : Math.Max(target, a - step);
                        if (Math.Abs(next - target) < 0.02f) { next = target; }
                        _hover[item] = next;

                        // Repaint only THIS row. The old code invalidated the entire menu on
                        // every tick — 30 full menu repaints a second, for a highlight on one
                        // row and a slow glow on the header.
                        InvalidateItem(owner, item);
                    }

                    // The header's breath is also read from the clock now. Accumulating
                    // (_pulse += 0.10) made the period depend on frame delivery, so the
                    // "gentle 3 second breath" ran at whatever rate the timer happened to
                    // manage. Only the header row is repainted for it.
                    if (_headerItem != null && !_headerItem.IsDisposed && ReferenceEquals(_headerItem.Owner, owner))
                    {
                        InvalidateItem(owner, _headerItem);
                    }
                    // The status dot's beat while something runs, the same way.
                    if (_statusItem != null && !_statusItem.IsDisposed && ReferenceEquals(_statusItem.Owner, owner)
                        && StyleOf(_statusItem)?.Pulse == true)
                    {
                        InvalidateItem(owner, _statusItem);
                    }
                }

                if (_owners.Count == 0)
                {
                    _hover.Clear();
                    _anim.Stop();
                }
            }
            catch { try { _anim.Stop(); } catch { } }
        }

        /// <summary>Repaints one row, not the whole menu.</summary>
        private static void InvalidateItem(ToolStrip owner, ToolStripItem item)
        {
            try
            {
                Rectangle b = item.Bounds;
                b.Inflate(2, 2);        // cover the rounded selection's antialiased edge
                owner.Invalidate(b);
            }
            catch { owner.Invalidate(); }
        }

        /// <summary>How long a hover highlight takes to reach full strength, in ms.</summary>
        private const int HoverFadeMs = 130;
        private long _lastAnimTick;
        private ToolStripItem _headerItem;
        private ToolStripItem _statusItem;

        /// <summary>0..1..0 on the wall clock, one cycle per <paramref name="periodMs"/>.</summary>
        private static double Breath(int periodMs)
        {
            return 0.5 + 0.5 * Math.Sin((Environment.TickCount64 % periodMs) * (2 * Math.PI / periodMs));
        }

        /// <summary>
        /// Stops and releases the animation timer.
        ///
        /// The renderer owns a Timer but had no disposal, and a fresh one is built every
        /// time the theme is applied — which happens on every settings save — as well as
        /// each time the second-cursor menu is rebuilt. A discarded renderer whose timer
        /// was still enabled went on ticking and calling Invalidate on a menu it no
        /// longer draws, and the Tick handler kept the whole renderer alive. That is a
        /// leak that grows for the life of the process.
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (_anim != null)
                {
                    _anim.Stop();
                    _anim.Dispose();
                    _anim = null;
                }
            }
            catch { }
            _hover.Clear();
            _owners.Clear();
            _headerItem = null;
            _statusItem = null;
        }

        /// <summary>
        /// The submenu arrow in the theme's ink. The base renderer draws it in the system's
        /// dark grey, which all but vanished on every dark theme.
        /// </summary>
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item != null && !e.Item.Enabled ? _theme.TextMuted : _theme.Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            RoundCorners(e.ToolStrip);
            using (var b = new SolidBrush(_theme.Surface))
            {
                e.Graphics.FillRectangle(b, e.AffectedBounds);
            }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWCP_ROUNDSMALL = 3;

        /// <summary>Which window each menu was last rounded for (a menu can get a new one).</summary>
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ToolStrip, System.Runtime.CompilerServices.StrongBox<IntPtr>> _roundedFor =
            new System.Runtime.CompilerServices.ConditionalWeakTable<ToolStrip, System.Runtime.CompilerServices.StrongBox<IntPtr>>();

        /// <summary>
        /// Windows 11's rounded menu corners, with the outline in the theme's border colour —
        /// the look of every other menu on the system, which a custom-drawn one does not get
        /// by itself (Tempo's were square). Once per menu window. Windows 10 has neither
        /// attribute: the call fails and the corners stay square, as they were.
        /// </summary>
        private void RoundCorners(ToolStrip strip)
        {
            if (!(strip is ToolStripDropDown) || strip.IsDisposed || !strip.IsHandleCreated) { return; }
            try
            {
                IntPtr h = strip.Handle;
                if (_roundedFor.TryGetValue(strip, out var done) && done.Value == h) { return; }
                _roundedFor.AddOrUpdate(strip, new System.Runtime.CompilerServices.StrongBox<IntPtr>(h));

                int pref = DWMWCP_ROUNDSMALL;
                DwmSetWindowAttribute(h, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
                Color b = _theme.Border;
                int colorref = b.R | (b.G << 8) | (b.B << 16);
                DwmSetWindowAttribute(h, DWMWA_BORDER_COLOR, ref colorref, sizeof(int));
            }
            catch (Exception ex) { Utils.Logger.Swallow("ThemedMenuRenderer.RoundCorners", ex); }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var style = StyleOf(e.Item);
            if (e.Item.Owner != null) { EnsureAnimating(e.Item.Owner); }

            // Header row: a subtle accent-tinted banner, not a clickable item. The
            // accent BREATHES gently while the menu is open (a slow sine on the
            // blend), and a soft glow swells behind the brand mark in step.
            if (style != null && style.Header)
            {
                // Remember the header so the animation tick can repaint just this row
                // for the breath instead of the whole menu.
                _headerItem = e.Item;
                // Read from the wall clock so the breath keeps a true ~3 s period
                // whatever the frame rate.
                double breath = Breath(3000);
                double baseBlend = _lightBg ? 0.10 : 0.16;
                var band = new Rectangle(0, 0, e.Item.Width, e.Item.Height);
                using (var b = new LinearGradientBrush(band,
                    Blend(_theme.Surface, _theme.Accent, baseBlend + 0.07 * breath),
                    _theme.Surface, LinearGradientMode.Horizontal))
                {
                    g.FillRectangle(b, band);
                }
                var gb = GlyphBox(e.Item);
                using (var glowPath = new GraphicsPath())
                {
                    int r = 14;
                    glowPath.AddEllipse(gb.X + gb.Width / 2 - r, gb.Y + gb.Height / 2 - r, r * 2, r * 2);
                    using (var glow = new PathGradientBrush(glowPath)
                    {
                        CenterColor = Color.FromArgb((int)(40 + 45 * breath), _theme.Accent),
                        SurroundColors = new[] { Color.FromArgb(0, _theme.Accent) }
                    })
                    {
                        g.FillPath(glow, glowPath);
                    }
                }
                using (var p = new Pen(Blend(_theme.Border, _theme.Accent, 0.25)))
                {
                    g.DrawLine(p, 8, e.Item.Height - 1, e.Item.Width - 8, e.Item.Height - 1);
                }
                DrawGlyph(g, TrayGlyph.Brand, gb, _theme.Accent, true);
                return;
            }

            var full = new Rectangle(Point.Empty, e.Item.Size);
            using (var bg = new SolidBrush(_theme.Surface))
            {
                g.FillRectangle(bg, full);
            }

            // Status row: no hover chrome — just its dot + text (drawn in the text
            // pass); a faint inset band separates it from the actionable items.
            if (style != null && style.Status)
            {
                return;
            }

            // The hot row looks like the sidebar's selected tab (RoundedButton): the surface
            // washed with the accent, a touch lighter at the top than the bottom, and an accent
            // bar down the left that grows from the middle as the row lights. It eases in over
            // ~130 ms on the clock (the timer advances it) and out again on leave.
            //
            // It was a solid accent fill with white text and white icons on it: on Slate's sky
            // blue that is about 2:1, where every other place in Tempo that puts text on the
            // accent chooses dark text. On a wash the row keeps its own ink, and its icon keeps
            // its colour.
            float hoverA = HoverAlpha(e.Item);
            if (e.Item.Selected && e.Item.Enabled && hoverA < 0.05f)
            {
                hoverA = 0.05f;                        // first paint before the timer ticks
            }
            if (hoverA > 0.01f)
            {
                float t = Math.Min(1f, hoverA);
                int a = (int)(255 * t);
                var rect = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
                Color wash = Blend(_theme.Surface, _theme.Accent, SelectedWash);
                using (var path = Rounded(rect, 7))
                using (var b = new LinearGradientBrush(rect,
                    Color.FromArgb(a, Lighten(wash, 0.07)), Color.FromArgb(a, Darken(wash, 0.05)),
                    LinearGradientMode.Vertical))
                {
                    g.FillPath(b, path);
                }
                int barFull = Math.Max(10, (int)(e.Item.Height * 0.56));
                int bh = Math.Max(2, (int)(barFull * t));
                var bar = new Rectangle(4, (e.Item.Height - bh) / 2, 3, bh);
                using (var bp = Rounded(bar, 1))
                using (var bb = new SolidBrush(_theme.Accent))
                {
                    g.FillPath(bb, bp);
                }
            }

            if (style != null && style.Glyph != TrayGlyph.None)
            {
                var mi = e.Item as ToolStripMenuItem;
                bool on = mi != null && mi.Checked;

                // A switch that is OFF, and anything disabled, is drawn in the muted ink — the
                // exact colour of muted text. Off used to be the icon's colour blended 55% into
                // the background, which gave a brown pin and an olive bell: colours that are
                // in no palette, and read as a different icon rather than as "off".
                Color hue = !e.Item.Enabled || (style.Toggle && !on)
                    ? _theme.TextMuted
                    : GlyphHue(style.Glyph);
                DrawGlyph(g, style.Glyph, GlyphBox(e.Item), hue, on);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var style = StyleOf(e.Item);
            if (style != null && style.Header)
            {
                // Brand wordmark in the accent, with an optional muted caption
                // (the version) trailing on the right.
                e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                var r = e.TextRectangle;
                r.X = GlyphBox(e.Item).Right + 6;
                r.Width = e.Item.Width - r.X - 8;
                using (var bold = new Font(e.TextFont, FontStyle.Bold))
                using (var brand = new SolidBrush(_theme.Accent))
                {
                    e.Graphics.DrawString(e.Text, bold, brand, r.Left,
                        r.Top + (r.Height - e.TextFont.Height) / 2f);
                    if (!string.IsNullOrEmpty(style.Caption))
                    {
                        var sz = e.Graphics.MeasureString(style.Caption, e.TextFont);
                        using (var mut = new SolidBrush(_theme.TextMuted))
                        {
                            e.Graphics.DrawString(style.Caption, e.TextFont, mut,
                                e.Item.Width - sz.Width - 10,
                                r.Top + (r.Height - e.TextFont.Height) / 2f);
                        }
                    }
                }
                return;
            }

            // Live status row: a coloured state dot + small muted text — the menu's
            // "what is Tempo doing right now", refreshed every time the menu opens.
            if (style != null && style.Status)
            {
                _statusItem = e.Item;
                e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var box = GlyphBox(e.Item);
                int d = 7;
                float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f;
                Color dot = style.Dot.IsEmpty ? _theme.TextMuted : style.Dot;
                if (style.Pulse)
                {
                    // A soft ring that swells and fades from the dot while something runs.
                    if (e.Item.Owner != null) { EnsureAnimating(e.Item.Owner); }
                    double beat = Breath(1600);
                    float ring = d / 2f + 1f + 4f * (float)beat;
                    using (var rb = new SolidBrush(Color.FromArgb((int)(110 * (1 - beat)), dot)))
                    {
                        e.Graphics.FillEllipse(rb, cx - ring, cy - ring, ring * 2, ring * 2);
                    }
                }
                using (var db = new SolidBrush(dot))
                {
                    e.Graphics.FillEllipse(db, cx - d / 2f, cy - d / 2f, d, d);
                }
                using (var small = new Font(e.TextFont.FontFamily, e.TextFont.Size - 0.75f))
                using (var mut = new SolidBrush(_theme.TextMuted))
                {
                    var r = e.TextRectangle;
                    e.Graphics.DrawString(e.Text, small, mut, r.Left,
                        r.Top + (r.Height - small.Height) / 2f);
                }
                return;
            }

            bool hot = e.Item.Selected && e.Item.Enabled && HoverAlpha(e.Item) > 0.5f;

            // The shortcut column: a toggle's switch, or the hint ("F6", "2× tray", "3 missed").
            if (IsShortcutText(e))
            {
                if (style != null && style.Toggle)
                {
                    DrawSwitch(e.Graphics, e.TextRectangle, e.Item, hot);
                    return;
                }
                Color hint = style != null && style.HintAlert
                    ? _theme.WarningText
                    : !e.Item.Enabled ? _theme.TextMuted
                    : hot ? Blend(_theme.TextMuted, _theme.Text, 0.45) : _theme.TextMuted;
                using (var small = new Font(e.TextFont.FontFamily, Math.Max(6f, e.TextFont.Size - 1f)))
                {
                    TextRenderer.DrawText(e.Graphics, e.Text, small, e.TextRectangle, hint,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                }
                return;
            }

            // The row's own ink, lit or not: the wash under a hot row keeps the theme's text
            // readable (7:1 on Slate). Not on every palette by itself — Nord's came to 4.39:1 —
            // so on a lit row it is checked against the wash's lighter top and moved if short.
            e.TextColor = !e.Item.Enabled ? _theme.TextMuted : hot ? HotText() : _theme.Text;
            base.OnRenderItemText(e);
        }

        /// <summary>The text colour on a lit row: the theme's own, unless the wash needs it moved to read 4.5:1.</summary>
        internal Color HotText()
        {
            if (_hotText.IsEmpty)
            {
                Color wash = Blend(_theme.Surface, _theme.Accent, SelectedWash);
                _hotText = Theme.Readable(_theme.Text, Lighten(wash, 0.07), 4.5);
            }
            return _hotText;
        }

        private Color _hotText = Color.Empty;

        /// <summary>True when this call draws the item's shortcut text, not its label.</summary>
        private static bool IsShortcutText(ToolStripItemTextRenderEventArgs e)
        {
            return e.Item is ToolStripMenuItem mi
                   && !string.IsNullOrEmpty(mi.ShortcutKeyDisplayString)
                   && string.Equals(e.Text, mi.ShortcutKeyDisplayString, StringComparison.Ordinal);
        }

        /// <summary>
        /// Settings' toggle switch (ToggleSwitch), small, in the shortcut column: an accent track
        /// with the knob right when on; the second surface outlined in the border colour, knob
        /// left, when off (outlined in the accent under the pointer, as there).
        /// </summary>
        private void DrawSwitch(Graphics g, Rectangle column, ToolStripItem item, bool hot)
        {
            var mi = item as ToolStripMenuItem;
            bool on = mi != null && mi.Checked;
            bool enabled = item.Enabled;

            // ToggleSwitch is 38×20 with a 14 px knob beside 9 pt text; this keeps its shape at
            // the menu's size and follows the font's scaling.
            int h = Math.Max(10, (int)Math.Round(item.Font.Height * 0.78));
            if (h > column.Height) { h = column.Height; }
            int w = (int)Math.Round(h * 1.9);
            var track = new Rectangle(column.Right - w, column.Y + (column.Height - h) / 2, w, h);

            var prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Rounded(track, h / 2))
            {
                Color fill = on ? (enabled ? _theme.Accent : _theme.TextMuted) : _theme.Surface2;
                using (var b = new SolidBrush(fill)) { g.FillPath(b, p); }
                if (!on)
                {
                    using (var pen = new Pen(hot ? _theme.Accent : _theme.Border, 1.2f)) { g.DrawPath(pen, p); }
                }
            }
            int m = Math.Max(2, (int)Math.Round(h * 0.15));
            int knob = h - 2 * m;
            int kx = on ? track.Right - knob - m : track.Left + m;
            using (var b = new SolidBrush(Color.White))
            {
                g.FillEllipse(b, kx, track.Y + m, knob, knob);
            }
            g.SmoothingMode = prev;
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(_theme.Border))
            {
                e.Graphics.DrawLine(p, 12, y, e.Item.Width - 12, y);
            }
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // Suppress the boxed check entirely for our glyph items — the glyph and the
            // switch carry the on/off state.
            if (StyleOf(e.Item) != null) { return; }

            // Everywhere else, Settings' own check box (ModernCheckBox): an accent square with
            // the tick in whichever of black and white reads on it. The base renderer drew the
            // system's pale blue box — the one colour in a themed menu that came from Windows.
            Rectangle r = e.ImageRectangle;
            if (r.Width < 6 || r.Height < 6) { return; }
            int size = Math.Min(16, Math.Min(r.Width, r.Height));
            var box = new Rectangle(r.X + (r.Width - size) / 2, r.Y + (r.Height - size) / 2, size, size);
            Color fill = e.Item.Enabled ? _theme.Accent : _theme.TextMuted;
            Graphics g = e.Graphics;
            var prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(box, Math.Max(2, size * 5 / 16)))
            using (var b = new SolidBrush(fill))
            {
                g.FillPath(b, path);
            }
            float s = size / 16f;
            using (var pen = new Pen(Theme.ReadableOn(fill), 2f * s)
                   { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLines(pen, new[]
                {
                    new PointF(box.Left + 4 * s, box.Top + 8 * s),
                    new PointF(box.Left + 6 * s, box.Top + 11 * s),
                    new PointF(box.Left + 12 * s, box.Top + 4 * s)
                });
            }
            g.SmoothingMode = prev;
        }

        // ── geometry & colour helpers ─────────────────────────────────────────

        private static Rectangle GlyphBox(ToolStripItem item)
        {
            const int sz = 16;
            return new Rectangle(8, (item.Height - sz) / 2, sz, sz);
        }

        /// <summary>
        /// The icon's colour, taken from the app, never invented here:
        /// <list type="bullet">
        /// <item>something that belongs to a part of Tempo wears that part's sidebar colour —
        /// Profile the Profiles tab's, the three caption items the Captions tab's, Roblox
        /// accounts and the vault the Accounts tab's, and the settings (Always on top, the
        /// notification switches, their history, Settings…) the Settings tab's;</item>
        /// <item>an action wears the palette's colour for what it does, as the window's own
        /// buttons do: Start and "Update to…" Success, Stop and Exit Danger, Restart Warning
        /// (Pause's colour on the Clicker tab: it interrupts, then carries on), and Tempo's
        /// own window and the update check the accent;</item>
        /// <item>the way out to GitHub, which is none of those, the theme's ink.</item>
        /// </list>
        /// An icon is a graphic, so it must stand 3:1 against the menu (WCAG 1.4.11); the
        /// palettes are audited for their text, not for every section colour on the menu's
        /// surface, so the rare one that falls short is moved in lightness, keeping its hue.
        /// </summary>
        private Color GlyphHue(TrayGlyph glyph)
        {
            Color c;
            switch (glyph)
            {
                case TrayGlyph.Play:
                case TrayGlyph.Download: c = _theme.Success; break;
                case TrayGlyph.Stop:
                case TrayGlyph.Power: c = _theme.Danger; break;
                case TrayGlyph.Restart: c = _theme.Warning; break;
                case TrayGlyph.Profile: c = SectionColors.For(Section.Profiles, _theme); break;
                case TrayGlyph.Badge: c = SectionColors.For(Section.Clicker, _theme); break;
                case TrayGlyph.Keys: c = SectionColors.For(Section.Keybinds, _theme); break;
                case TrayGlyph.Accounts:
                case TrayGlyph.Lock: c = SectionColors.For(Section.Accounts, _theme); break;
                case TrayGlyph.Captions:
                case TrayGlyph.History:
                case TrayGlyph.Move:
                case TrayGlyph.Speech: c = SectionColors.For(Section.Captions, _theme); break;
                case TrayGlyph.Pin:
                case TrayGlyph.Bell:
                case TrayGlyph.Camera:
                case TrayGlyph.Inbox:
                case TrayGlyph.Gear: c = SectionColors.For(Section.Settings, _theme); break;
                case TrayGlyph.Code: c = _theme.Text; break;
                default: c = _theme.Accent; break;      // Brand, Window, Refresh, Dot
            }
            return Theme.Readable(c, _theme.Surface, 3.0);
        }

        // ── the vector glyphs ─────────────────────────────────────────────────

        private static void DrawGlyph(Graphics g, TrayGlyph glyph, Rectangle box, Color color, bool on)
        {
            var prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float x = box.X, y = box.Y, w = box.Width, h = box.Height;
            using (var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var fill = new SolidBrush(color))
            {
                switch (glyph)
                {
                    case TrayGlyph.Brand:
                    {
                        // A lightning bolt — Tempo's mark.
                        var pts = new[]
                        {
                            new PointF(x + w * 0.58f, y + h * 0.06f),
                            new PointF(x + w * 0.28f, y + h * 0.56f),
                            new PointF(x + w * 0.48f, y + h * 0.56f),
                            new PointF(x + w * 0.40f, y + h * 0.96f),
                            new PointF(x + w * 0.74f, y + h * 0.40f),
                            new PointF(x + w * 0.52f, y + h * 0.40f),
                            new PointF(x + w * 0.66f, y + h * 0.06f),
                        };
                        g.FillPolygon(fill, pts);
                        break;
                    }
                    case TrayGlyph.Window:
                    {
                        var r = new RectangleF(x + 1.5f, y + 2.5f, w - 3, h - 5);
                        using (var rp = RoundedF(r, 2.2f))
                        {
                            g.DrawPath(pen, rp);
                        }
                        // Title bar strip.
                        g.DrawLine(pen, r.Left + 0.5f, r.Top + 3.4f, r.Right - 0.5f, r.Top + 3.4f);
                        break;
                    }
                    case TrayGlyph.Play:
                    {
                        var pts = new[]
                        {
                            new PointF(x + w * 0.30f, y + h * 0.22f),
                            new PointF(x + w * 0.30f, y + h * 0.78f),
                            new PointF(x + w * 0.80f, y + h * 0.50f),
                        };
                        g.FillPolygon(fill, pts);
                        break;
                    }
                    case TrayGlyph.Stop:
                    {
                        var r = new RectangleF(x + w * 0.26f, y + h * 0.26f, w * 0.48f, h * 0.48f);
                        using (var rp = RoundedF(r, 2f))
                        {
                            g.FillPath(fill, rp);
                        }
                        break;
                    }
                    case TrayGlyph.Pin:
                    {
                        // "Keep on top": an up-chevron pointing at a bar. Filled
                        // arrowhead when pinned (on), outline when off.
                        g.DrawLine(pen, x + w * 0.30f, y + h * 0.24f, x + w * 0.70f, y + h * 0.24f);
                        var head = new[]
                        {
                            new PointF(x + w * 0.28f, y + h * 0.60f),
                            new PointF(x + w * 0.50f, y + h * 0.38f),
                            new PointF(x + w * 0.72f, y + h * 0.60f),
                        };
                        if (on) { g.FillPolygon(fill, head); }
                        else { g.DrawLines(pen, head); }
                        g.DrawLine(pen, x + w * 0.50f, y + h * 0.44f, x + w * 0.50f, y + h * 0.80f);
                        break;
                    }
                    case TrayGlyph.Speech:
                    {
                        var r = new RectangleF(x + 1.5f, y + 2f, w - 3, h - 6);
                        using (var rp = RoundedF(r, 3f))
                        {
                            g.DrawPath(pen, rp);
                        }
                        // Little tail.
                        var tail = new[]
                        {
                            new PointF(x + w * 0.32f, r.Bottom - 0.5f),
                            new PointF(x + w * 0.30f, y + h * 0.92f),
                            new PointF(x + w * 0.48f, r.Bottom - 0.5f),
                        };
                        g.DrawLines(pen, tail);
                        // Two speech dots.
                        using (var dot = new SolidBrush(color))
                        {
                            g.FillEllipse(dot, x + w * 0.36f, y + h * 0.36f, 1.6f, 1.6f);
                            g.FillEllipse(dot, x + w * 0.56f, y + h * 0.36f, 1.6f, 1.6f);
                        }
                        break;
                    }
                    case TrayGlyph.Move:
                    {
                        float cx = x + w * 0.5f, cy = y + h * 0.5f;
                        g.DrawLine(pen, cx, y + h * 0.16f, cx, y + h * 0.84f);
                        g.DrawLine(pen, x + w * 0.16f, cy, x + w * 0.84f, cy);
                        float a = 3.0f;
                        g.DrawLines(pen, new[] { new PointF(cx - a, y + h * 0.16f + a), new PointF(cx, y + h * 0.16f), new PointF(cx + a, y + h * 0.16f + a) });
                        g.DrawLines(pen, new[] { new PointF(cx - a, y + h * 0.84f - a), new PointF(cx, y + h * 0.84f), new PointF(cx + a, y + h * 0.84f - a) });
                        g.DrawLines(pen, new[] { new PointF(x + w * 0.16f + a, cy - a), new PointF(x + w * 0.16f, cy), new PointF(x + w * 0.16f + a, cy + a) });
                        g.DrawLines(pen, new[] { new PointF(x + w * 0.84f - a, cy - a), new PointF(x + w * 0.84f, cy), new PointF(x + w * 0.84f - a, cy + a) });
                        break;
                    }
                    case TrayGlyph.Power:
                    {
                        var r = new RectangleF(x + w * 0.24f, y + h * 0.24f, w * 0.52f, h * 0.52f);
                        // Ring with a gap at the top.
                        g.DrawArc(pen, r.X, r.Y, r.Width, r.Height, -60, 300);
                        g.DrawLine(pen, x + w * 0.5f, y + h * 0.12f, x + w * 0.5f, y + h * 0.46f);
                        break;
                    }
                    case TrayGlyph.Captions:
                    {
                        // A screen with two lines of subtitle along its foot.
                        var r = new RectangleF(x + 1.5f, y + 2.5f, w - 3, h - 5);
                        using (var rp = RoundedF(r, 2.2f)) { g.DrawPath(pen, rp); }
                        g.DrawLine(pen, x + w * 0.28f, y + h * 0.56f, x + w * 0.72f, y + h * 0.56f);
                        g.DrawLine(pen, x + w * 0.36f, y + h * 0.72f, x + w * 0.64f, y + h * 0.72f);
                        break;
                    }
                    case TrayGlyph.History:
                    {
                        // A clock whose rim turns back on itself: "what happened before".
                        var r = new RectangleF(x + w * 0.16f, y + h * 0.16f, w * 0.68f, h * 0.68f);
                        g.DrawArc(pen, r.X, r.Y, r.Width, r.Height, 200, 290);
                        float ax = x + w * 0.19f, ay = y + h * 0.38f;
                        g.DrawLines(pen, new[] { new PointF(ax - 1.8f, ay - 2.6f), new PointF(ax, ay), new PointF(ax + 2.6f, ay - 1.2f) });
                        g.DrawLines(pen, new[] { new PointF(x + w * 0.5f, y + h * 0.32f), new PointF(x + w * 0.5f, y + h * 0.52f), new PointF(x + w * 0.64f, y + h * 0.60f) });
                        break;
                    }
                    case TrayGlyph.Inbox:
                    {
                        // A tray: open top, a dip in the middle where things land.
                        g.DrawLines(pen, new[]
                        {
                            new PointF(x + w * 0.14f, y + h * 0.52f), new PointF(x + w * 0.14f, y + h * 0.80f),
                            new PointF(x + w * 0.86f, y + h * 0.80f), new PointF(x + w * 0.86f, y + h * 0.52f),
                        });
                        g.DrawLines(pen, new[]
                        {
                            new PointF(x + w * 0.14f, y + h * 0.52f), new PointF(x + w * 0.36f, y + h * 0.52f),
                            new PointF(x + w * 0.42f, y + h * 0.64f), new PointF(x + w * 0.58f, y + h * 0.64f),
                            new PointF(x + w * 0.64f, y + h * 0.52f), new PointF(x + w * 0.86f, y + h * 0.52f),
                        });
                        // Sloped sides up to the rim.
                        g.DrawLine(pen, x + w * 0.14f, y + h * 0.52f, x + w * 0.26f, y + h * 0.22f);
                        g.DrawLine(pen, x + w * 0.86f, y + h * 0.52f, x + w * 0.74f, y + h * 0.22f);
                        g.DrawLine(pen, x + w * 0.26f, y + h * 0.22f, x + w * 0.74f, y + h * 0.22f);
                        break;
                    }
                    case TrayGlyph.Bell:
                    {
                        using (var path = new GraphicsPath())
                        {
                            path.AddArc(x + w * 0.26f, y + h * 0.16f, w * 0.48f, h * 0.50f, 180, 180);
                            path.AddLine(x + w * 0.74f, y + h * 0.41f, x + w * 0.80f, y + h * 0.70f);
                            path.AddLine(x + w * 0.80f, y + h * 0.70f, x + w * 0.20f, y + h * 0.70f);
                            path.CloseFigure();
                            if (on) { g.FillPath(fill, path); } else { g.DrawPath(pen, path); }
                        }
                        g.FillEllipse(fill, x + w * 0.42f, y + h * 0.74f, w * 0.16f, h * 0.14f);
                        break;
                    }
                    case TrayGlyph.Camera:
                    {
                        var body = new RectangleF(x + 1.5f, y + h * 0.30f, w - 3, h * 0.56f);
                        using (var rp = RoundedF(body, 2.2f)) { g.DrawPath(pen, rp); }
                        g.DrawLines(pen, new[]
                        {
                            new PointF(x + w * 0.34f, body.Top), new PointF(x + w * 0.40f, y + h * 0.16f),
                            new PointF(x + w * 0.60f, y + h * 0.16f), new PointF(x + w * 0.66f, body.Top),
                        });
                        float lr = w * 0.15f;
                        g.DrawEllipse(pen, x + w * 0.5f - lr, body.Top + body.Height / 2 - lr, lr * 2, lr * 2);
                        break;
                    }
                    case TrayGlyph.Gear:
                    {
                        float cx = x + w * 0.5f, cy = y + h * 0.5f;
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            g.DrawLine(pen,
                                cx + (float)Math.Cos(a) * w * 0.28f, cy + (float)Math.Sin(a) * h * 0.28f,
                                cx + (float)Math.Cos(a) * w * 0.42f, cy + (float)Math.Sin(a) * h * 0.42f);
                        }
                        g.DrawEllipse(pen, cx - w * 0.28f, cy - h * 0.28f, w * 0.56f, h * 0.56f);
                        g.DrawEllipse(pen, cx - w * 0.10f, cy - h * 0.10f, w * 0.20f, h * 0.20f);
                        break;
                    }
                    case TrayGlyph.Refresh:
                    {
                        var r = new RectangleF(x + w * 0.18f, y + h * 0.18f, w * 0.64f, h * 0.64f);
                        g.DrawArc(pen, r.X, r.Y, r.Width, r.Height, -80, 300);
                        // The arrowhead where the arc ends, pointing on round.
                        float ax = x + w * 0.60f, ay = y + h * 0.19f;
                        g.DrawLines(pen, new[] { new PointF(ax - 3.2f, ay - 2.2f), new PointF(ax, ay), new PointF(ax - 2.4f, ay + 2.8f) });
                        break;
                    }
                    case TrayGlyph.Restart:
                    {
                        // Power's ring and stem, with an arrowhead on the ring's right-hand end
                        // pointing on round into the gap: switched off, and straight back on.
                        var r = new RectangleF(x + w * 0.24f, y + h * 0.24f, w * 0.52f, h * 0.52f);
                        g.DrawArc(pen, r.X, r.Y, r.Width, r.Height, -60, 300);
                        g.DrawLine(pen, x + w * 0.5f, y + h * 0.12f, x + w * 0.5f, y + h * 0.46f);
                        double a = -60 * Math.PI / 180;
                        float ex = r.X + r.Width / 2 + (float)Math.Cos(a) * r.Width / 2;
                        float ey = r.Y + r.Height / 2 + (float)Math.Sin(a) * r.Height / 2;
                        // Travelling anticlockwise on screen from there, i.e. up and into the gap.
                        double travel = Math.Atan2(-Math.Cos(a), Math.Sin(a));
                        float wing = w * 0.22f;
                        foreach (double spread in new[] { 0.7, -0.7 })
                        {
                            double back = travel + Math.PI + spread;
                            g.DrawLine(pen, ex, ey, ex + (float)Math.Cos(back) * wing, ey + (float)Math.Sin(back) * wing);
                        }
                        break;
                    }
                    case TrayGlyph.Download:
                    {
                        // An arrow down into a tray: something new is waiting to be installed.
                        g.DrawLine(pen, x + w * 0.50f, y + h * 0.10f, x + w * 0.50f, y + h * 0.60f);
                        g.DrawLines(pen, new[] { new PointF(x + w * 0.28f, y + h * 0.40f), new PointF(x + w * 0.50f, y + h * 0.62f), new PointF(x + w * 0.72f, y + h * 0.40f) });
                        g.DrawLines(pen, new[] { new PointF(x + w * 0.14f, y + h * 0.66f), new PointF(x + w * 0.14f, y + h * 0.86f), new PointF(x + w * 0.86f, y + h * 0.86f), new PointF(x + w * 0.86f, y + h * 0.66f) });
                        break;
                    }
                    case TrayGlyph.Code:
                    {
                        g.DrawLines(pen, new[] { new PointF(x + w * 0.34f, y + h * 0.28f), new PointF(x + w * 0.12f, y + h * 0.50f), new PointF(x + w * 0.34f, y + h * 0.72f) });
                        g.DrawLines(pen, new[] { new PointF(x + w * 0.66f, y + h * 0.28f), new PointF(x + w * 0.88f, y + h * 0.50f), new PointF(x + w * 0.66f, y + h * 0.72f) });
                        g.DrawLine(pen, x + w * 0.56f, y + h * 0.20f, x + w * 0.44f, y + h * 0.80f);
                        break;
                    }
                    case TrayGlyph.Profile:
                    {
                        g.DrawEllipse(pen, x + w * 0.34f, y + h * 0.12f, w * 0.32f, h * 0.32f);
                        g.DrawArc(pen, x + w * 0.16f, y + h * 0.52f, w * 0.68f, h * 0.60f, 180, 180);
                        break;
                    }
                    case TrayGlyph.Accounts:
                    {
                        // Two people — several accounts, where Profile is one.
                        g.DrawEllipse(pen, x + w * 0.10f, y + h * 0.22f, w * 0.28f, h * 0.28f);
                        g.DrawArc(pen, x - w * 0.02f, y + h * 0.58f, w * 0.52f, h * 0.48f, 180, 180);
                        g.DrawEllipse(pen, x + w * 0.54f, y + h * 0.14f, w * 0.30f, h * 0.30f);
                        g.DrawArc(pen, x + w * 0.42f, y + h * 0.52f, w * 0.56f, h * 0.52f, 180, 180);
                        break;
                    }
                    case TrayGlyph.Lock:
                    {
                        g.DrawArc(pen, x + w * 0.30f, y + h * 0.10f, w * 0.40f, h * 0.44f, 180, 180);
                        g.DrawLine(pen, x + w * 0.30f, y + h * 0.32f, x + w * 0.30f, y + h * 0.44f);
                        g.DrawLine(pen, x + w * 0.70f, y + h * 0.32f, x + w * 0.70f, y + h * 0.44f);
                        var body = new RectangleF(x + w * 0.20f, y + h * 0.44f, w * 0.60f, h * 0.44f);
                        using (var rp = RoundedF(body, 1.8f)) { g.FillPath(fill, rp); }
                        break;
                    }
                    case TrayGlyph.Dot:
                    {
                        if (on)
                        {
                            float d = w * 0.38f;
                            g.FillEllipse(fill, x + (w - d) / 2f, y + (h - d) / 2f, d, d);
                        }
                        break;
                    }
                    case TrayGlyph.Badge:
                    {
                        // The running badge: a rounded pill with its status dot at the left.
                        var r = new RectangleF(x + 1f, y + h * 0.28f, w - 2f, h * 0.44f);
                        using (var rp = RoundedF(r, r.Height / 2f))
                        {
                            g.DrawPath(pen, rp);
                        }
                        float d = r.Height * 0.46f;
                        g.FillEllipse(fill, r.Left + r.Height * 0.3f, r.Top + (r.Height - d) / 2f, d, d);
                        break;
                    }
                    case TrayGlyph.Keys:
                    {
                        // A keyboard: its outline, two rows of keys and the space bar.
                        var r = new RectangleF(x + 1f, y + h * 0.24f, w - 2f, h * 0.54f);
                        using (var rp = RoundedF(r, 2f)) { g.DrawPath(pen, rp); }
                        float k = w * 0.10f;
                        for (int row = 0; row < 2; row++)
                        {
                            float ky = r.Top + r.Height * (row == 0 ? 0.22f : 0.48f);
                            for (int i = 0; i < 4; i++)
                            {
                                float kx = r.Left + r.Width * (0.16f + i * 0.21f);
                                g.FillRectangle(fill, kx, ky, k, k * 0.9f);
                            }
                        }
                        g.DrawLine(pen, r.Left + r.Width * 0.30f, r.Bottom - r.Height * 0.20f,
                                        r.Right - r.Width * 0.30f, r.Bottom - r.Height * 0.20f);
                        break;
                    }
                }
            }
            g.SmoothingMode = prev;
        }

        // ── small drawing utilities ───────────────────────────────────────────

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = Math.Max(2, radius * 2);
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static GraphicsPath RoundedF(RectangleF r, float radius)
        {
            float d = Math.Max(1f, radius * 2);
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static Color Lighten(Color c, double amount)
        {
            return Color.FromArgb(c.A,
                (int)(c.R + (255 - c.R) * amount),
                (int)(c.G + (255 - c.G) * amount),
                (int)(c.B + (255 - c.B) * amount));
        }

        private static Color Darken(Color c, double amount)
        {
            return Color.FromArgb(c.A,
                (int)(c.R * (1 - amount)),
                (int)(c.G * (1 - amount)),
                (int)(c.B * (1 - amount)));
        }

        private static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>Theme-fed colour table for the chrome the base renderer draws.</summary>
        private sealed class ThemedColorTable : ProfessionalColorTable
        {
            private readonly Theme _t;
            public ThemedColorTable(Theme t) { _t = t ?? Theme.ForKind(Models.ThemeKind.Dark); }

            public override Color ToolStripDropDownBackground => _t.Surface;
            public override Color ImageMarginGradientBegin => _t.Surface;
            public override Color ImageMarginGradientMiddle => _t.Surface;
            public override Color ImageMarginGradientEnd => _t.Surface;
            public override Color MenuBorder => _t.Border;
            public override Color MenuItemBorder => Color.Transparent;
            public override Color SeparatorDark => _t.Border;
            public override Color SeparatorLight => _t.Border;
        }
    }
}
