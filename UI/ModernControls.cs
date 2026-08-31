using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A Panel with double-buffering enabled, so owner-drawn content in its Paint
    /// handler (e.g. the shared window backdrop behind the sidebar) composites
    /// off-screen and never flickers as an animated backdrop repaints.
    /// </summary>
    public sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            // ResizeRedraw matters here: this panel OWNER-PAINTS content anchored to its
            // BOTTOM (the sidebar's separator line and "Tempo v…" stamp). Without it a
            // resize only invalidates the newly exposed strip, so the footer was redrawn
            // at the new bottom while the old one survived as stale pixels — the version
            // stamp appearing TWICE down the sidebar. Repainting the whole panel on
            // resize is cheap (it's 188 px wide) and makes that impossible.
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
    }

    /// <summary>
    /// Sends a mouse-wheel notch to the nearest scrolling ancestor.
    /// <para>
    /// Win32 delivers WM_MOUSEWHEEL to whatever control is under the cursor, and combo
    /// boxes and numeric spinners react by CHANGING THEIR VALUE. So merely scrolling a
    /// page while the pointer happened to pass over the Theme box silently switched the
    /// user's theme, and passing over "Millis" silently retuned the click interval.
    /// Those controls now bubble the wheel here instead, so the page scrolls — matching
    /// how browsers treat an unfocused select/number field.
    /// </para>
    /// </summary>

    /// <summary>
    /// Stops a focused input control from swallowing the keys people use to move around a
    /// long, scrolling settings page.
    ///
    /// A closed ComboBox, a NumericUpDown and a TrackBar all act on Home / End / PageUp /
    /// PageDown by jumping their VALUE — Home on a combo selects the first item. On a page
    /// you scroll, that is a trap: click a control, press Home or PageDown to get back to
    /// the top, and you have silently changed a setting instead. It is the same hazard the
    /// mouse wheel had (see <see cref="WheelBubble"/>), which is already handled; the
    /// keyboard half was missed, and it is the more destructive of the two because Home
    /// jumps straight to the first item rather than moving by one.
    ///
    /// Arrow keys and typing are deliberately NOT intercepted: those are the conventional,
    /// small, expected way to adjust a focused control, and taking them away would break
    /// keyboard accessibility for anyone driving the UI without a mouse.
    /// </summary>
    internal static class PageKeyGuard
    {
        /// <summary>True if this key should scroll the page rather than change the control.</summary>
        public static bool IsPageNavKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if ((keyData & (Keys.Control | Keys.Alt)) != 0) { return false; }
            return k == Keys.Home || k == Keys.End || k == Keys.PageUp || k == Keys.PageDown;
        }

        /// <summary>
        /// Scrolls the nearest AutoScroll ancestor as the key intended. Returns true when
        /// the key was consumed, so the caller reports it handled and the control never
        /// sees it.
        /// </summary>
        public static bool ScrollParent(Control source, Keys keyData)
        {
            if (source == null || !IsPageNavKey(keyData)) { return false; }

            Control p = source.Parent;
            while (p != null)
            {
                if (p is ScrollableControl sc2 && sc2.AutoScroll) { break; }
                p = p.Parent;
            }
            if (!(p is ScrollableControl sc)) { return false; }

            try
            {
                // AutoScrollPosition reads back NEGATIVE and is assigned POSITIVE - the
                // long-standing WinForms wart. Hence the Math.Abs on the way in.
                int y = Math.Abs(sc.AutoScrollPosition.Y);
                int page = Math.Max(40, sc.ClientSize.Height - 40);
                int max = Math.Max(0, sc.DisplayRectangle.Height - sc.ClientSize.Height);

                switch (keyData & Keys.KeyCode)
                {
                    case Keys.Home: y = 0; break;
                    case Keys.End: y = max; break;
                    case Keys.PageUp: y -= page; break;
                    case Keys.PageDown: y += page; break;
                }
                sc.AutoScrollPosition = new Point(0, Math.Max(0, Math.Min(max, y)));
            }
            catch { }
            return true;
        }
    }

    internal static class WheelBubble
    {
        private const int WM_MOUSEWHEEL = 0x020A;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>Forwards <paramref name="e"/> from <paramref name="source"/> to its scrolling parent.</summary>
        public static void ToParent(Control source, MouseEventArgs e)
        {
            if (source == null || e == null) return;

            Control p = source.Parent;
            while (p != null)
            {
                if (p is ScrollableControl sc && sc.AutoScroll) break;
                p = p.Parent;
            }
            if (p == null || !p.IsHandleCreated) return;

            // WPARAM: delta in the high word. LPARAM: cursor position in screen coords.
            IntPtr wParam = (IntPtr)((long)e.Delta << 16);
            Point s = source.PointToScreen(new Point(e.X, e.Y));
            IntPtr lParam = (IntPtr)((s.Y << 16) | (s.X & 0xFFFF));
            SendMessage(p.Handle, WM_MOUSEWHEEL, wParam, lParam);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Flat, theme-aware replacements for the three native Win32 controls that made
    //  the UI look dated: the checkbox glyph, the radio glyph, and the combo-box
    //  drop arrow. They are drop-in subclasses (a ModernCheckBox IS-A CheckBox, etc.)
    //  so every existing field, event and layout keeps working unchanged; only the
    //  painting differs. Each exposes ApplyTheme(Theme) and is wired into ThemeManager.
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>A flat, accent-filled checkbox drawn entirely by us.</summary>
    public sealed class ModernCheckBox : CheckBox
    {
        private Theme _theme;
        private bool _hover;
        // Kept close to the native checkbox's footprint so this drop-in replacement
        // doesn't grow wider than the control it replaces (a wider box would shove
        // long labels into neighbouring controls on tabs not yet re-laid-out).
        private const int Box = 16;
        private const int Gap = 4;

        public ModernCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            AutoSize = true;
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
        }

        public void ApplyTheme(Theme theme) { _theme = theme; Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size t = TextRenderer.MeasureText(Text ?? string.Empty, Font);
            // Height kept close to the native checkbox so rows the layout spaced for the
            // native control don't collide with this slightly-different replacement.
            return new Size(Box + Gap + t.Width + 2, Math.Max(Box + 2, t.Height + 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme th = _theme;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Opaque background erases cleanly; for a transparent one, blit the wallpaper
            // slice behind us so the buffer is filled (no stale ghost pixels when scrolled)
            // while the image still shows through instead of an ugly solid box.
            if (BackColor.A == 255) { g.Clear(BackColor); }
            else { ModernPaint.PaintTransparentBackdrop(this, g); }

            Color border = th != null ? th.Border : Color.Gray;
            Color accent = th != null ? th.Accent : Color.DodgerBlue;
            Color input = th != null ? th.InputBackground : Color.White;
            Color text = th != null ? th.Text : ForeColor;
            Color muted = th != null ? th.TextMuted : Color.Gray;
            if (!Enabled) { text = muted; }

            int by = (Height - Box) / 2;
            var box = new Rectangle(0, by, Box, Box);
            using (var path = ModernPaint.Rounded(box, 5))
            {
                if (Checked)
                {
                    using (var fill = new SolidBrush(Enabled ? accent : muted))
                        g.FillPath(fill, path);
                    // White check mark (sized for the 16px box).
                    using (var pen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        g.DrawLines(pen, new[]
                        {
                            new Point(box.Left + 4, box.Top + 8),
                            new Point(box.Left + 6, box.Top + 11),
                            new Point(box.Left + 12, box.Top + 4)
                        });
                }
                else
                {
                    using (var fill = new SolidBrush(input))
                        g.FillPath(fill, path);
                    using (var pen = new Pen(_hover && Enabled ? accent : border, 1.6f))
                        g.DrawPath(pen, path);
                }
            }

            var textRect = new Rectangle(Box + Gap, 0, Width - Box - Gap, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis);
        }
    }

    /// <summary>An iOS-style pill toggle switch (drop-in for a CheckBox).</summary>
    public sealed class ToggleSwitch : CheckBox
    {
        private Theme _theme;
        private bool _hover;
        private const int TrackW = 38;
        private const int TrackH = 20;
        private const int Knob = 14;
        private const int Gap = 8;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            AutoSize = true;
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
        }

        public void ApplyTheme(Theme theme) { _theme = theme; Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size t = TextRenderer.MeasureText(Text ?? string.Empty, Font);
            return new Size(TrackW + Gap + t.Width + 2, Math.Max(TrackH + 2, t.Height + 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme th = _theme;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (BackColor.A == 255) { g.Clear(BackColor); }
            else { ModernPaint.PaintTransparentBackdrop(this, g); }

            Color accent = th != null ? th.Accent : Color.DodgerBlue;
            Color off = th != null ? th.Surface2 : Color.Gray;
            Color border = th != null ? th.Border : Color.Gray;
            Color text = th != null ? th.Text : ForeColor;
            Color muted = th != null ? th.TextMuted : Color.Gray;
            if (!Enabled) text = muted;

            int ty = (Height - TrackH) / 2;
            var track = new Rectangle(0, ty, TrackW, TrackH);
            using (var p = ModernPaint.Rounded(track, TrackH / 2))
            {
                using (var b = new SolidBrush(Checked ? (Enabled ? accent : muted) : off))
                    g.FillPath(b, p);
                if (!Checked)
                    using (var pen = new Pen(_hover && Enabled ? accent : border, 1.4f))
                        g.DrawPath(pen, p);
            }
            int kx = Checked ? track.Right - Knob - 3 : track.Left + 3;
            int ky = ty + (TrackH - Knob) / 2;
            using (var b = new SolidBrush(Color.White))
                g.FillEllipse(b, kx, ky, Knob, Knob);

            var textRect = new Rectangle(TrackW + Gap, 0, Width - TrackW - Gap, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis);
        }
    }

    /// <summary>A flat, accent-filled radio button drawn entirely by us.</summary>
    public sealed class ModernRadioButton : RadioButton
    {
        private Theme _theme;
        private bool _hover;
        private const int Dot = 16;
        private const int Gap = 4;

        public ModernRadioButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            AutoSize = true;
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
        }

        public void ApplyTheme(Theme theme) { _theme = theme; Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size t = TextRenderer.MeasureText(Text ?? string.Empty, Font);
            return new Size(Dot + Gap + t.Width + 2, Math.Max(Dot + 2, t.Height + 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme th = _theme;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (BackColor.A == 255) { g.Clear(BackColor); }
            else { ModernPaint.PaintTransparentBackdrop(this, g); }

            Color border = th != null ? th.Border : Color.Gray;
            Color accent = th != null ? th.Accent : Color.DodgerBlue;
            Color input = th != null ? th.InputBackground : Color.White;
            Color text = th != null ? th.Text : ForeColor;
            Color muted = th != null ? th.TextMuted : Color.Gray;
            if (!Enabled) text = muted;

            int by = (Height - Dot) / 2;
            var circle = new Rectangle(0, by, Dot, Dot);
            using (var fill = new SolidBrush(Checked ? (Enabled ? accent : muted) : input))
                g.FillEllipse(fill, circle);
            using (var pen = new Pen(Checked ? (Enabled ? accent : muted) : (_hover && Enabled ? accent : border), 1.6f))
                g.DrawEllipse(pen, circle);
            if (Checked)
            {
                var dot = Rectangle.Inflate(circle, -5, -5);
                using (var fill = new SolidBrush(Color.White))
                    g.FillEllipse(fill, dot);
            }

            var textRect = new Rectangle(Dot + Gap, 0, Width - Dot - Gap, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis);
        }
    }

    /// <summary>
    /// A ComboBox that keeps native behaviour but paints a flat themed border, a clean
    /// chevron in place of the grey 3-D drop button, and themed drop-down items.
    /// </summary>
    public sealed class FlatComboBox : ComboBox
    {
        private const int WmPaint = 0x000F;
        private Theme _theme;

        public FlatComboBox()
        {
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = Math.Max(20, Font.Height + 8);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); RoundRegion(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); RoundRegion(); }

        private void RoundRegion()
        {
            if (Width <= 1 || Height <= 1) return;
            using (var p = ModernPaint.Rounded(new Rectangle(0, 0, Width, Height), 7))
            {
                Region = new Region(p);
            }
        }

        // Keep the owner-drawn drop-down row height tied to the (DPI-scaled) font so
        // list text isn't vertically clipped at 125%/150%/200% display scaling.
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            try { ItemHeight = Math.Max(20, Font.Height + 8); } catch { }
        }

        public void ApplyTheme(Theme theme)
        {
            _theme = theme;
            if (theme != null)
            {
                BackColor = theme.InputBackground;
                ForeColor = theme.Text;
            }
            Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            Theme th = _theme;
            Color input = th != null ? th.InputBackground : Color.White;
            Color accent = th != null ? th.Accent : Color.DodgerBlue;
            Color text = th != null ? th.Text : ForeColor;

            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool isEditField = (e.State & DrawItemState.ComboBoxEdit) != 0;
            Color bg = (selected && !isEditField) ? accent : input;
            Color fg = (selected && !isEditField) ? Color.White : text;

            using (var b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, e.Bounds);

            if (e.Index >= 0 && e.Index < Items.Count)
            {
                string s = GetItemText(Items[e.Index]);
                var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, s, Font, r, fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private const int WmEraseBkgnd = 0x0014;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct PAINTSTRUCT
        {
            public IntPtr hdc;
            public bool fErase;
            public int rcPaintLeft, rcPaintTop, rcPaintRight, rcPaintBottom;
            public bool fRestore, fIncUpdate;
            public int r0, r1, r2, r3, r4, r5, r6, r7;
        }

        /// <summary>
        /// A wheel notch over a CLOSED combo must scroll the page, not change the
        /// selection. Windows' default does the latter, which meant simply scrolling
        /// the Settings page with the pointer over the Theme box silently switched
        /// theme (and over Button/Type/Mode, silently retuned the clicker).
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Only while CLOSED: with the list open these keys legitimately move the
            // highlight. ProcessCmdKey rather than OnKeyDown because the ComboBox acts on
            // the key inside its own WndProc, which OnKeyDown does not reliably prevent.
            if (!DroppedDown && PageKeyGuard.IsPageNavKey(keyData) &&
                PageKeyGuard.ScrollParent(this, keyData))
            {
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!DroppedDown)
            {
                if (e is HandledMouseEventArgs h) h.Handled = true;
                WheelBubble.ToParent(this, e);
                return;   // never let the native combo change SelectedIndex
            }
            base.OnMouseWheel(e);
        }

        protected override void WndProc(ref Message m)
        {
            // Own the closed combo's painting COMPLETELY. Previously the NATIVE combo
            // painted first (a LIGHT visual-styles button/border in dark themes) and the
            // dark arrow/border was drawn over it afterwards — so every repaint (tab
            // switch, hover, dropdown close) flashed light. Swallowing the erase and
            // handling WM_PAINT without calling base means the light native art never
            // renders at all. The drop-down LIST is a separate window whose rows still
            // arrive through OnDrawItem, so it keeps working unchanged.
            if (m.Msg == WmEraseBkgnd)
            {
                m.Result = (IntPtr)1;
                return;
            }
            if (m.Msg == WmPaint)
            {
                PAINTSTRUCT ps;
                BeginPaint(m.HWnd, out ps);   // fetch + validate the update region
                try
                {
                    using (var g = Graphics.FromHwnd(Handle))
                    {
                        PaintClosedCombo(g);
                    }
                }
                finally { EndPaint(m.HWnd, ref ps); }
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>Draws the whole closed combo: dark fill, selected text, arrow, border.</summary>
        private void PaintClosedCombo(Graphics g)
        {
            Theme th = _theme;
            Color input = th != null ? th.InputBackground : Color.White;
            Color accent = th != null ? th.Accent : Color.DodgerBlue;
            Color border = th != null ? th.Border : Color.Gray;
            Color text = th != null ? th.Text : ForeColor;
            Color muted = th != null ? th.TextMuted : Color.Gray;

            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Background.
            using (var b = new SolidBrush(input))
                g.FillRectangle(b, new Rectangle(0, 0, Width, Height));

            // Arrow area scales with the (DPI-dependent) control height.
            int aw = Math.Max(20, Height);

            // Selected item's text (the closed combo's "edit" area). The text may run
            // under the outer edge of the arrow zone (only the small chevron near the
            // right edge must stay clear), matching how much room the old rendering gave.
            string s = SelectedIndex >= 0 && SelectedIndex < Items.Count
                ? GetItemText(Items[SelectedIndex])
                : Text;
            if (!string.IsNullOrEmpty(s))
            {
                var tr = new Rectangle(6, 0, Math.Max(1, Width - 24), Height);
                TextRenderer.DrawText(g, s, Font, tr, Enabled ? text : muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            // Drop arrow.
            int cx = Width - aw / 2 - 1;
            int cy = Height / 2;
            using (var pen = new Pen(Enabled ? accent : muted, 1.7f)
                { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[]
                {
                    new Point(cx - 4, cy - 2), new Point(cx, cy + 2), new Point(cx + 4, cy - 2)
                });

            // Rounded border.
            using (var path = ModernPaint.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 7))
            using (var pen = new Pen(border, 1.4f))
                g.DrawPath(pen, path);
        }
    }

    /// <summary>
    /// A label that draws three coloured segments — a muted prefix, an accent-coloured
    /// value, and a muted suffix — used for "Target: <b>200 CPS</b> (5 ms · …)".
    /// </summary>
    public sealed class SpeedTargetLabel : Label
    {
        private string _prefix = "Target: ";
        private string _value = string.Empty;
        private string _suffix = string.Empty;
        public Color AccentColor { get; set; } = Color.DodgerBlue;
        public Color MutedColor { get; set; } = Color.Gray;

        /// <summary>Draws the accent value segment in bold, so the number reads first.</summary>
        public bool BoldValue { get; set; }

        private Font _boldCache;

        public SpeedTargetLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            AutoSize = false;
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (_boldCache != null) { _boldCache.Dispose(); _boldCache = null; }
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _boldCache != null) { _boldCache.Dispose(); _boldCache = null; }
            base.Dispose(disposing);
        }

        private Font ValueFont
        {
            get
            {
                if (!BoldValue) return Font;
                if (_boldCache == null) _boldCache = new Font(Font, FontStyle.Bold);
                return _boldCache;
            }
        }

        public void SetParts(string prefix, string value, string suffix)
        {
            _prefix = prefix ?? string.Empty;
            _value = value ?? string.Empty;
            _suffix = suffix ?? string.Empty;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor.A == 255 ? BackColor : (Parent != null ? Parent.BackColor : BackColor));
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                                          TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            int x = 0;
            x += DrawSeg(g, _prefix, ForeColor, x, Font, flags);
            x += DrawSeg(g, _value, AccentColor, x, ValueFont, flags);
            DrawSeg(g, _suffix, MutedColor, x, Font, flags);
        }

        private int DrawSeg(Graphics g, string s, Color c, int x, Font f, TextFormatFlags flags)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            Size sz = TextRenderer.MeasureText(g, s, f, new Size(int.MaxValue, Height), flags);
            TextRenderer.DrawText(g, s, f, new Rectangle(x, 0, sz.Width, Height), c, flags);
            return sz.Width;
        }
    }

    /// <summary>
    /// A fully owner-drawn slider: dark track, accent-filled progress portion, round
    /// thumb and subtle tick marks. Replaces the native TrackBar (which painted the
    /// light Windows channel/thumb regardless of theme, and redrew natively during
    /// drags). Exposes the same members the app used on TrackBar — Minimum, Maximum,
    /// Value, TickFrequency, Small/LargeChange and the Scroll event — where Scroll is
    /// raised only for USER changes (mouse, wheel, keyboard), never for programmatic
    /// Value sets, matching the native semantics the callers rely on.
    /// Mouse-wheel steps by LargeChange and is marked handled so the page underneath
    /// doesn't scroll.
    /// </summary>
    public sealed class SmoothTrackBar : Control
    {
        private Theme _theme;
        private int _min;
        private int _max = 10;
        private int _value;
        private bool _dragging;
        private bool _hover;

        /// <summary>Raised when the USER moves the slider (drag, wheel or keyboard).</summary>
        public event EventHandler Scroll;

        public int Minimum
        {
            get { return _min; }
            set { _min = value; if (_max < _min) _max = _min; if (_value < _min) _value = _min; Invalidate(); }
        }

        public int Maximum
        {
            get { return _max; }
            set { _max = Math.Max(value, _min); if (_value > _max) _value = _max; Invalidate(); }
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = Math.Max(_min, Math.Min(_max, value));
                if (v != _value)
                {
                    _value = v;
                    Invalidate();
                }
            }
        }

        public int TickFrequency { get; set; } = 10;
        public int SmallChange { get; set; } = 1;
        public int LargeChange { get; set; } = 5;

        public SmoothTrackBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Height = 34;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public void ApplyTheme(Theme theme)
        {
            _theme = theme;
            Invalidate();
        }

        // Horizontal inset so the round thumb never clips at the ends.
        private const int PadX = 10;

        /// <summary>Sets the value from a user gesture, raising Scroll if it changed.</summary>
        private void UserSetValue(int v)
        {
            v = Math.Max(_min, Math.Min(_max, v));
            if (v != _value)
            {
                _value = v;
                Invalidate();
                Scroll?.Invoke(this, EventArgs.Empty);
            }
        }

        private void SetFromX(int x)
        {
            int w = Math.Max(1, Width - PadX * 2);
            double t = (x - PadX) / (double)w;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            UserSetValue(_min + (int)Math.Round(t * (_max - _min)));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                Focus();
                _dragging = true;
                SetFromX(e.X);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                SetFromX(e.X);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        /// <summary>
        /// The wheel only moves the slider when it has FOCUS — otherwise the page scrolls.
        ///
        /// This used to adjust the value on every notch no matter what, and swallow the
        /// event so nothing else saw it. Both halves were wrong, and this control sits in
        /// the worst possible place for it: the Manual Speed slider on the Clicker tab
        /// (and Window opacity in Settings). Scrolling down that page with the pointer
        /// happening to pass over the slider silently RETUNED THE CLICK RATE — on an
        /// auto-clicker, a change the user never asked for and would not notice — and
        /// the page refused to scroll while the pointer was over it, because the notch
        /// was consumed here.
        ///
        /// FlatComboBox and FlatNumericUpDown were both fixed for exactly this (a scroll
        /// past the Theme box used to switch theme, a scroll past "Millis" used to
        /// retune the interval); the slider was missed. Same rule as those two now:
        /// deliberate aim (focus) adjusts, a passing pointer scrolls the page.
        /// </summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!Focused)
            {
                if (e is HandledMouseEventArgs h2) h2.Handled = true;
                WheelBubble.ToParent(this, e);
                return;
            }
            int step = LargeChange > 0 ? LargeChange : 1;
            UserSetValue(Value + (e.Delta > 0 ? step : -step));
            if (e is HandledMouseEventArgs h) h.Handled = true;
            // Intentionally NOT calling base — we fully own the wheel behaviour.
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Home would slam this slider to its MINIMUM. On a scrolling settings page
            // that is what someone presses to get back to the top - see PageKeyGuard.
            if (PageKeyGuard.IsPageNavKey(keyData) && PageKeyGuard.ScrollParent(this, keyData))
            {
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                    return true;
                default:
                    return base.IsInputKey(keyData);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.Down: UserSetValue(Value - Math.Max(1, SmallChange)); e.Handled = true; break;
                case Keys.Right:
                case Keys.Up: UserSetValue(Value + Math.Max(1, SmallChange)); e.Handled = true; break;
                case Keys.PageDown: UserSetValue(Value - Math.Max(1, LargeChange)); e.Handled = true; break;
                case Keys.PageUp: UserSetValue(Value + Math.Max(1, LargeChange)); e.Handled = true; break;
                case Keys.Home: UserSetValue(Minimum); e.Handled = true; break;
                case Keys.End: UserSetValue(Maximum); e.Handled = true; break;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme th = _theme;
            Color surface = Parent != null ? Parent.BackColor : BackColor;
            Color trackCol = th != null ? th.Surface2 : Color.FromArgb(45, 45, 52);
            Color tickCol = Color.FromArgb(80, th != null ? th.Border : Color.Gray);
            Color accent = th != null ? th.Accent : Color.DodgerBlue;
            Color accent2 = th != null ? th.AccentHover : Color.CornflowerBlue;
            if (!Enabled)
            {
                accent = accent2 = th != null ? th.TextMuted : Color.Gray;
            }

            g.Clear(surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int cy = Height / 2;
            int w = Math.Max(1, Width - PadX * 2);
            double t = _max > _min ? (_value - _min) / (double)(_max - _min) : 0;
            int tx = PadX + (int)Math.Round(t * w);

            // Tick marks under the track (subtle, like a ruler).
            if (TickFrequency > 0 && _max > _min)
            {
                using (var tp = new Pen(tickCol))
                {
                    for (long v = _min; v <= _max; v += TickFrequency)
                    {
                        int x = PadX + (int)Math.Round((v - _min) / (double)(_max - _min) * w);
                        g.DrawLine(tp, x, cy + 8, x, cy + 12);
                    }
                }
            }

            bool active = (_hover || _dragging || Focused) && Enabled;

            // Track (full width), then the filled progress portion up to the thumb.
            using (var tb = new SolidBrush(trackCol))
            {
                FillRoundedBar(g, tb, PadX, cy - 3, w, 6);
            }
            if (tx > PadX + 2)
            {
                var fillRect = new Rectangle(PadX, cy - 3, tx - PadX, 6);
                // Soft glow bloom under the filled portion — stronger while active.
                using (var glow = new SolidBrush(Color.FromArgb(active ? 60 : 34, accent)))
                {
                    FillRoundedBar(g, glow, fillRect.X - 1, fillRect.Y - 2, fillRect.Width + 2, fillRect.Height + 4);
                }
                using (var fb = new LinearGradientBrush(fillRect, accent, accent2, LinearGradientMode.Horizontal))
                {
                    FillRoundedBar(g, fb, fillRect.X, fillRect.Y, fillRect.Width, fillRect.Height);
                }
            }

            // Round thumb: grows a touch and blooms an accent glow ring when the
            // slider is hovered, focused or being dragged.
            int r = active ? 9 : 8;

            // Soft drop shadow for depth.
            using (var sh = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            {
                g.FillEllipse(sh, tx - r, cy - r + 2, r * 2, r * 2);
            }

            // Accent glow halo (active only).
            if (active)
            {
                int gr = r + 7;
                using (var gp = new GraphicsPath())
                {
                    gp.AddEllipse(tx - gr, cy - gr, gr * 2, gr * 2);
                    using (var glow = new PathGradientBrush(gp)
                    {
                        CenterColor = Color.FromArgb(120, accent),
                        SurroundColors = new[] { Color.FromArgb(0, accent) },
                        CenterPoint = new PointF(tx, cy)
                    })
                    {
                        g.FillPath(glow, gp);
                    }
                }
            }

            using (var ob = new SolidBrush(accent))
            {
                g.FillEllipse(ob, tx - r, cy - r, r * 2, r * 2);
            }
            Color core = Color.FromArgb(255,
                Math.Min(255, accent.R + 70), Math.Min(255, accent.G + 70), Math.Min(255, accent.B + 70));
            using (var ib = new SolidBrush(Enabled ? core : accent))
            {
                g.FillEllipse(ib, tx - r + 3, cy - r + 3, (r - 3) * 2, (r - 3) * 2);
            }
        }

        private static void FillRoundedBar(Graphics g, Brush b, int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            int d = Math.Min(h, w);
            using (var path = new GraphicsPath())
            {
                path.AddArc(x, y, d, d, 90, 180);
                path.AddArc(x + w - d, y, d, d, 270, 180);
                path.CloseFigure();
                g.FillPath(b, path);
            }
        }
    }

    /// <summary>A single-line TextBox clipped to rounded corners to match the inputs.</summary>
    public sealed class FlatTextBox : TextBox
    {
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); RoundRegion(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); RoundRegion(); }

        private void RoundRegion()
        {
            if (Width <= 1 || Height <= 1) return;
            using (var p = ModernPaint.Rounded(new Rectangle(0, 0, Width, Height), 6))
            {
                Region = new Region(p);
            }
        }
    }

    /// <summary>
    /// A NumericUpDown whose grey native ▲▼ spin buttons are repainted as flat themed
    /// chevrons. The buttons are a private child control, so we subclass their window
    /// via a NativeWindow and paint over them on WM_PAINT — keeping all native spin
    /// behaviour (click, hold-to-repeat, keyboard) while changing only the look.
    /// </summary>
    public sealed class FlatNumericUpDown : NumericUpDown
    {
        private const int WmPaint = 0x000F;
        private Theme _theme;
        private Control _buttons;
        private SpinPainter _painter;
        private bool _hooked;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // PageUp/PageDown step a NumericUpDown by 10 and Home/End jump to its limits;
            // all four are page-navigation keys on a scrolling settings page.
            if (PageKeyGuard.IsPageNavKey(keyData) && PageKeyGuard.ScrollParent(this, keyData))
            {
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public FlatNumericUpDown()
        {
            // Borderless: we draw our own rounded border, and a rounded Region clips the
            // control to a pill-corner shape so it reads like the mockup's inputs.
            BorderStyle = BorderStyle.None;
        }

        /// <summary>
        /// Scrolling the page with the pointer over a spinner used to change its VALUE
        /// (Windows' default) — so a scroll past "Millis" silently retuned the click
        /// interval. The wheel now only adjusts the value when the field has focus, i.e.
        /// when the user deliberately aimed at it; otherwise the page scrolls.
        /// </summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!Focused)
            {
                if (e is HandledMouseEventArgs h) h.Handled = true;
                WheelBubble.ToParent(this, e);
                return;
            }
            base.OnMouseWheel(e);
        }

        public void ApplyTheme(Theme theme)
        {
            _theme = theme;
            if (theme != null)
            {
                BackColor = theme.InputBackground;
                ForeColor = theme.Text;
                if (Controls.Count > 1) Controls[1].BackColor = theme.InputBackground;
            }
            _buttons?.Invalidate();
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            RoundRegion();
        }

        // Clip to a rounded shape so the corners are smooth like the mockup inputs.
        private void RoundRegion()
        {
            if (Width <= 1 || Height <= 1) return;
            using (var p = ModernPaint.Rounded(new Rectangle(0, 0, Width, Height), 7))
            {
                Region = new Region(p);
            }
        }

        // Draw a rounded border on top of the native paint (the edit child sits inset, so
        // a 1px ring at the very edge stays visible).
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WmPaint && _theme != null)
            {
                using (var g = Graphics.FromHwnd(Handle))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var path = ModernPaint.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 7))
                    using (var pen = new Pen(_theme.Border, 1.4f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RoundRegion();
            BeginInvoke(new Action(HookButtons));
        }

        private void HookButtons()
        {
            if (_hooked) return;
            foreach (Control c in Controls)
            {
                if (c.GetType().Name.IndexOf("UpDownButtons", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _buttons = c;
                    break;
                }
            }
            if (_buttons == null && Controls.Count > 0) _buttons = Controls[0];
            if (_buttons == null) return;
            if (_buttons.IsHandleCreated)
            {
                _painter = new SpinPainter(this);
                _painter.AssignHandle(_buttons.Handle);
                _hooked = true;
                _buttons.Invalidate();
            }
            else
            {
                // Handle not ready yet — hook as soon as it is, so the spin buttons
                // never stay in the native style on a slow/timing-dependent layout.
                _buttons.HandleCreated += OnButtonsHandleCreated;
            }
        }

        private void OnButtonsHandleCreated(object sender, EventArgs e)
        {
            if (_buttons != null) _buttons.HandleCreated -= OnButtonsHandleCreated;
            HookButtons();
        }

        private void PaintSpin(IntPtr handle)
        {
            if (_buttons == null) return;
            Theme th = _theme;
            Color input = th != null ? th.InputBackground : Color.White;
            Color border = th != null ? th.Border : Color.Gray;
            Color glyph = th != null ? th.TextMuted : Color.Gray;

            using (var g = Graphics.FromHwnd(handle))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int w = _buttons.Width;
                int h = _buttons.Height;
                using (var b = new SolidBrush(input))
                    g.FillRectangle(b, 0, 0, w, h);

                // Just a faint vertical hairline separating the spinner from the field —
                // no heavy boxes — matching the mockup's clean stacked chevrons.
                using (var pen = new Pen(Color.FromArgb(90, border), 1))
                    g.DrawLine(pen, 0, 4, 0, h - 4);

                int cx = w / 2;
                using (var pen = new Pen(glyph, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    int upY = h / 4;
                    g.DrawLines(pen, new[] { new Point(cx - 3, upY + 1), new Point(cx, upY - 2), new Point(cx + 3, upY + 1) });
                    int dnY = h * 3 / 4;
                    g.DrawLines(pen, new[] { new Point(cx - 3, dnY - 2), new Point(cx, dnY + 1), new Point(cx + 3, dnY - 2) });
                }
            }
        }

        private sealed class SpinPainter : NativeWindow
        {
            private const int WmEraseBkgnd = 0x0014;
            private readonly FlatNumericUpDown _owner;
            public SpinPainter(FlatNumericUpDown owner) { _owner = owner; }

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            private struct PAINTSTRUCT
            {
                public IntPtr hdc;
                public bool fErase;
                public int rcPaintLeft, rcPaintTop, rcPaintRight, rcPaintBottom;
                public bool fRestore, fIncUpdate;
                public int r0, r1, r2, r3, r4, r5, r6, r7;
            }

            protected override void WndProc(ref Message m)
            {
                // Own the spinner's paint completely. Previously we let the native
                // UpDownButtons paint its LIGHT visual-style ▲▼ arrows (base.WndProc)
                // and then drew our dark chevrons over them — so on any repaint the
                // light arrows flashed for a frame first. Now we swallow WM_ERASEBKGND
                // (dark, no light erase) and handle WM_PAINT ourselves without calling
                // base, so the native light arrows never render at all.
                if (m.Msg == WmEraseBkgnd)
                {
                    m.Result = (IntPtr)1;
                    return;
                }
                if (m.Msg == WmPaint)
                {
                    PAINTSTRUCT ps;
                    BeginPaint(m.HWnd, out ps);   // fetch + validate the update region
                    try { _owner.PaintSpin(m.HWnd); }
                    finally { EndPaint(m.HWnd, ref ps); }
                    m.Result = IntPtr.Zero;
                    return;
                }
                base.WndProc(ref m);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _painter != null)
            {
                try { _painter.ReleaseHandle(); } catch { }
                _painter = null;
            }
            base.Dispose(disposing);
        }
    }

    internal static class ModernPaint
    {
        /// <summary>
        /// Fills an owner-drawn control's background for a transparent BackColor by
        /// blitting the wallpaper slice behind it from the nearest BackdropTabPage, so
        /// the buffer is cleared to the image (no stale ghost pixels) rather than left
        /// untouched. Falls back to an opaque parent-colour clear when there's no page.
        /// </summary>
        public static void PaintTransparentBackdrop(Control c, Graphics g)
        {
            Control p = c?.Parent;
            while (p != null)
            {
                if (p is BackdropTabPage bp)
                {
                    bp.PaintBackdropSlice(g, c);
                    return;
                }
                p = p.Parent;
            }
            Color fill = c?.Parent != null ? c.Parent.BackColor : (c != null ? c.BackColor : Color.Black);
            if (fill.A == 255)
            {
                g.Clear(fill);
            }
        }

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
