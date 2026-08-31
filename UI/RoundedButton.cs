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
        /// Optional keyboard hint drawn small and dimmed at the right edge (e.g. "1" for
        /// Ctrl+1). Tempo has had Ctrl+1…9 tab jumping for ages with nothing in the UI to
        /// reveal it; this surfaces the shortcut where the user's eye already is, without
        /// competing with the label. Empty (the default) draws nothing.
        /// </summary>
        public string ShortcutHint { get; set; }

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

            Color fore = Enabled
                ? Mix(ForeColor, AccentColor, _selT)
                : Mix(ForeColor, Color.Gray, 0.5);

            // Action glyph + centred label (New, Save, Start, …): draw the icon and the
            // text together, centred as a group.
            if (Glyph != ActionGlyph.None && IconKind == NavIconKind.None)
            {
                Size ts = TextRenderer.MeasureText(Text ?? string.Empty, Font);
                float gsize = Math.Min(Height - 12, 17);
                if (gsize < 12) gsize = 12;
                const float gap = 7f;
                float totalW = gsize + gap + ts.Width;
                float startX = (Width - totalW) / 2f;
                if (startX < 8) startX = 8;
                var gbox = new RectangleF(startX, (Height - gsize) / 2f, gsize, gsize);
                DrawActionGlyph(g, Glyph, gbox, fore);
                var tr = new Rectangle((int)(gbox.Right + gap), 0,
                    (int)(Width - gbox.Right - gap), Height);
                TextRenderer.DrawText(g, Text, Font, tr, fore,
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
                // The icon's own hue when one is set (disabled still greys out).
                Color iconFore = IconColor.IsEmpty || !Enabled ? fore : IconColor;
                NavIcons.Draw(g, IconKind, iconBox, iconFore);
                textLeftPad = (int)(iconBox.Right + 8f);
            }

            // Keyboard hint at the right edge, drawn BEFORE the label so the label's
            // rectangle can be shortened to clear it (no overlap, ellipsis stays honest).
            // Drawn with TextRenderer, like the label: GDI+ DrawString adds its own
            // padding around the glyph and could push the digit past its rectangle and
            // into the label (or clip it against the rounded edge). TextRenderer with
            // NoPadding places it exactly where we measure it.
            int hintReserve = 0;
            if (!string.IsNullOrEmpty(ShortcutHint) && Enabled)
            {
                using (var hintFont = new Font(Font.FontFamily, Math.Max(6.5f, Font.Size - 2.5f), FontStyle.Regular))
                {
                    const TextFormatFlags hintFlags =
                        TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

                    Size hs = TextRenderer.MeasureText(g, ShortcutHint, hintFont,
                        new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);

                    // Clearance from the rounded right edge, so the digit never sits on
                    // the border, and a wide gap before the label so the two never touch.
                    int edgeGap = Math.Max(10, Padding.Right + 6);
                    int labelGap = 14;

                    var hintRect = new Rectangle(rect.Right - edgeGap - hs.Width, rect.Y,
                                                 hs.Width, rect.Height);

                    // A hint, not a badge — but it still has to be READABLE. `fore` is
                    // already the muted text colour, so layering a low alpha on top
                    // dimmed it twice and left the digit at ~1.1:1 against the row
                    // (measured #224449 on #19403F — invisible). These values keep it
                    // clearly quieter than the label while staying legible.
                    // Rides the same eased values, so the shortcut digit brightens with
                    // the row instead of stepping between three fixed alphas.
                    int alpha = (int)(165 + 45 * _hoverT + 25 * _selT);
                    TextRenderer.DrawText(g, ShortcutHint, hintFont, hintRect,
                                          Color.FromArgb(alpha, fore), hintFlags);

                    hintReserve = hs.Width + edgeGap + labelGap;
                }
            }

            TextFormatFlags flags = TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            var rectForText = new Rectangle(rect.X, rect.Y,
                Math.Max(0, rect.Width - hintReserve), rect.Height);
            var textRect = rectForText;
            switch (TextAlign)
            {
                case ContentAlignment.TopLeft:
                case ContentAlignment.MiddleLeft:
                case ContentAlignment.BottomLeft:
                    flags |= TextFormatFlags.Left;
                    textRect = new Rectangle(rect.X + textLeftPad, rect.Y,
                        rect.Width - textLeftPad, rect.Height);
                    break;
                case ContentAlignment.TopRight:
                case ContentAlignment.MiddleRight:
                case ContentAlignment.BottomRight:
                    flags |= TextFormatFlags.Right;
                    textRect = new Rectangle(rect.X, rect.Y,
                        rect.Width - Math.Max(0, Padding.Right), rect.Height);
                    break;
                default:
                    flags |= TextFormatFlags.HorizontalCenter;
                    break;
            }

            TextRenderer.DrawText(g, Text, Font, textRect, fore, flags);
        }

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
