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
        None, Brand, Window, Play, Stop, Pin, Speech, Move, Power
    }

    /// <summary>
    /// Per-item styling Tempo attaches to a tray menu item via <c>Tag</c>. The
    /// renderer reads it to paint the icon and to recognise the branded header
    /// row. Colours are resolved from the ACTIVE theme at paint time (not stored
    /// here), so a theme change is reflected with no re-tagging.
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
        /// <summary>Status only: the state dot's colour (green = active, muted = idle).</summary>
        public Color Dot;
        /// <summary>Right-aligned muted hint (a hotkey like "F6") on normal items.</summary>
        public string Hint;

        public TrayItemStyle(TrayGlyph glyph) { Glyph = glyph; }
    }

    /// <summary>
    /// Draws Tempo's menus (the tray menu foremost) in Tempo's own theme instead of
    /// the stark white system look — a branded header banner, crisp vector icons in
    /// theme hues, a rounded accent selection with a left indicator bar, and themed
    /// separators. Colours come from the active Theme, so the menu follows every
    /// theme (Dark/Light/Match-Windows and all the named palettes) like the rest of
    /// the app.
    /// </summary>
    public sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer, IDisposable
    {
        private readonly Theme _theme;
        private readonly bool _lightBg;

        // ── menu animation ────────────────────────────────────────────────────
        // A light 30 fps timer runs while the menu is open: each item's hover
        // highlight FADES in/out (eased alpha per item) instead of snapping, and
        // the header's accent band breathes gently. The timer parks itself the
        // moment the menu closes and every fade has settled — zero cost while no
        // menu is on screen.
        private readonly System.Collections.Generic.Dictionary<ToolStripItem, float> _hover =
            new System.Collections.Generic.Dictionary<ToolStripItem, float>();
        private System.Windows.Forms.Timer _anim;
        private ToolStrip _owner;

        public ThemedMenuRenderer(Theme theme) : base(new ThemedColorTable(theme))
        {
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);
            _lightBg = _theme.Background.GetBrightness() > 0.5f;
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
            _owner = owner;
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
                var owner = _owner;
                if (owner == null || owner.IsDisposed || !owner.Visible)
                {
                    _hover.Clear();
                    _anim.Stop();
                    return;
                }

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
                if (_headerItem != null && !_headerItem.IsDisposed)
                {
                    InvalidateItem(owner, _headerItem);
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
            _owner = null;
            _headerItem = null;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(_theme.Surface))
            {
                e.Graphics.FillRectangle(b, e.AffectedBounds);
            }
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
                // whatever the frame rate: 2*PI radians per 3000 ms.
                double breath = 0.5 + 0.5 * Math.Sin(
                    (Environment.TickCount64 % 3000) * (2 * Math.PI / 3000.0));
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

            // Hover highlight with an EASED alpha (the timer advances it): the accent
            // fill and indicator bar fade in over ~100 ms and fade out on leave,
            // instead of flicking on and off.
            float hoverA = HoverAlpha(e.Item);
            if (e.Item.Selected && e.Item.Enabled && hoverA < 0.05f)
            {
                hoverA = 0.05f;                        // first paint before the timer ticks
            }
            if (hoverA > 0.01f)
            {
                int a = (int)(255 * Math.Min(1f, hoverA));
                var rect = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
                using (var path = Rounded(rect, 7))
                using (var b = new LinearGradientBrush(rect,
                    Color.FromArgb(a, _theme.AccentHover), Color.FromArgb(a, _theme.Accent),
                    LinearGradientMode.Vertical))
                {
                    g.FillPath(b, path);
                }
                // Left indicator bar, echoing the sidebar's selected tab.
                var bar = new Rectangle(3, 4, 3, e.Item.Height - 8);
                using (var bp = Rounded(bar, 1))
                using (var bb = new SolidBrush(Color.FromArgb(a, Lighten(_theme.Accent, 0.55))))
                {
                    g.FillPath(bb, bp);
                }
            }

            if (style != null && style.Glyph != TrayGlyph.None)
            {
                bool hot = e.Item.Selected && e.Item.Enabled && hoverA > 0.5f;
                var mi = e.Item as ToolStripMenuItem;
                bool on = mi != null && mi.Checked;
                Color hue = hot ? OnAccentColor() : GlyphHue(style.Glyph, e.Item);

                // A CHECKABLE item has to read as on or off at a glance. Only the pin
                // glyph implemented that (it fills when active) — every other glyph
                // ignored the state, so "Pop-up notifications" and "Screenshot alerts"
                // looked identical whether they were on or off. Colour now carries the
                // state for all of them: accent when on, clearly dimmed when off.
                bool checkable = mi != null && (mi.CheckOnClick || mi.Checked);
                if (checkable && !hot)
                {
                    hue = on ? _theme.Accent : Dim(hue, 0.55);
                }
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
                e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                var box = GlyphBox(e.Item);
                int d = 7;
                Color dot = style.Dot.IsEmpty ? _theme.TextMuted : style.Dot;
                using (var db = new SolidBrush(dot))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.FillEllipse(db,
                        box.X + (box.Width - d) / 2f, box.Y + (box.Height - d) / 2f, d, d);
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

            bool hotText = e.Item.Selected && e.Item.Enabled && HoverAlpha(e.Item) > 0.5f;
            e.TextColor = hotText
                ? OnAccentColor()
                : e.Item.Enabled ? _theme.Text : _theme.TextMuted;
            base.OnRenderItemText(e);

            // Right-aligned muted hint (the bound hotkey, "F6") — detail the plain
            // labels were missing; readable in both hover states.
            if (style != null && !string.IsNullOrEmpty(style.Hint))
            {
                e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                using (var small = new Font(e.TextFont.FontFamily, e.TextFont.Size - 1f))
                {
                    var sz = e.Graphics.MeasureString(style.Hint, small);
                    Color hintCol = hotText
                        ? Color.FromArgb(200, OnAccentColor())
                        : _theme.TextMuted;
                    using (var hb = new SolidBrush(hintCol))
                    {
                        e.Graphics.DrawString(style.Hint, small, hb,
                            e.Item.Width - sz.Width - 12,
                            (e.Item.Height - sz.Height) / 2f);
                    }
                }
            }
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
            // Suppress the boxed check entirely for our glyph items — the glyph
            // itself carries the on/off state (e.g. the pin fills when active).
            if (StyleOf(e.Item) != null) { return; }
            base.OnRenderItemCheck(e);
        }

        /// <summary>Fades a glyph toward the menu surface — the "off" state of a toggle.</summary>
        private Color Dim(Color c, double amount)
        {
            return Blend(c, _theme.Surface, amount);
        }

        // ── geometry & colour helpers ─────────────────────────────────────────

        private static Rectangle GlyphBox(ToolStripItem item)
        {
            const int sz = 16;
            return new Rectangle(8, (item.Height - sz) / 2, sz, sz);
        }

        /// <summary>A readable icon/text colour on top of the accent selection fill.</summary>
        private Color OnAccentColor()
        {
            return _theme.Accent.GetBrightness() > 0.62f
                ? Color.FromArgb(20, 20, 26) : Color.White;
        }

        /// <summary>
        /// Distinct per-glyph hue, drawn from a fixed reference palette so the icons
        /// read as a set on any theme, then nudged darker on light backgrounds so
        /// they stay legible on white.
        /// </summary>
        private Color GlyphHue(TrayGlyph glyph, ToolStripItem item)
        {
            Color c;
            switch (glyph)
            {
                case TrayGlyph.Window: c = Color.FromArgb(56, 189, 248); break;   // sky
                case TrayGlyph.Play:   c = Color.FromArgb(52, 211, 153); break;   // green
                case TrayGlyph.Stop:   c = Color.FromArgb(248, 113, 113); break;  // red
                case TrayGlyph.Pin:    c = Color.FromArgb(251, 191, 36); break;   // gold
                case TrayGlyph.Speech: c = Color.FromArgb(167, 139, 250); break;  // violet
                case TrayGlyph.Move:   c = Color.FromArgb(45, 212, 191); break;   // teal
                case TrayGlyph.Power:  c = Color.FromArgb(248, 113, 113); break;  // red
                default:               c = _theme.Accent; break;
            }
            // A disabled item's icon reads muted.
            if (!item.Enabled) { return Blend(_theme.TextMuted, c, 0.35); }
            return _lightBg ? Darken(c, 0.30) : c;
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
