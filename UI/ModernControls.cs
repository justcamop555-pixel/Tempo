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

    /// <summary>
    /// A control that paints its own caption, and knows where that caption starts.
    ///
    /// The layout checker has to know where a checkbox's TEXT is to tell whether it runs
    /// into its neighbour, and it used to assume every tick box put the text 24 px in. A
    /// ModernCheckBox starts it at 20; a ToggleSwitch — a 38 px track and an 8 px gap —
    /// at 46. So a toggle's caption could run 22 px into the control beside it with the
    /// check measuring a gap that was not there. Each control now says for itself, from
    /// the same constants it paints with, so the two can never drift apart.
    /// </summary>
    internal interface ICaptionOffset
    {
        /// <summary>X, in the control's own coordinates, where its caption begins.</summary>
        int CaptionLeft { get; }
    }

    /// <summary>
    /// An auto-sized control that takes its OWN size, the way a label does, instead of whatever its
    /// bounds are set to. WinForms' DPI scaling sets every control to 1.5× its bounds and leaves it to the
    /// next layout of its container to bring an auto-sized one back to its size — and a card nothing
    /// re-lays out never gets one. On a 150% screen 52 tick boxes came out of the scaling half as wide and
    /// half as tall again as their words, and four stayed so with their page on screen: captions sitting
    /// lower than their row, in boxes running over their neighbours.
    /// </summary>
    internal static class SelfSize
    {
        /// <summary>
        /// Whether <paramref name="c"/> should be its own size: auto-sized, and not sized by its container
        /// instead — docked, stretched between two anchors, or in a flow or table layout.
        /// </summary>
        internal static bool Applies(Control c)
        {
            if (c == null || !c.AutoSize || c.Dock != DockStyle.None) { return false; }
            if (c.Parent is FlowLayoutPanel || c.Parent is TableLayoutPanel) { return false; }
            AnchorStyles a = c.Anchor;
            return (a & (AnchorStyles.Left | AnchorStyles.Right)) != (AnchorStyles.Left | AnchorStyles.Right)
                && (a & (AnchorStyles.Top | AnchorStyles.Bottom)) != (AnchorStyles.Top | AnchorStyles.Bottom);
        }

        /// <summary>For SetBoundsCore: the control's own size in place of the one it was given.</summary>
        internal static void Apply(Control c, ref int width, ref int height)
        {
            if (!Applies(c)) { return; }
            Size p = c.GetPreferredSize(Size.Empty);
            width = p.Width;
            height = p.Height;
        }

        /// <summary>For a text or font change: take the new size now, not at the container's next layout.</summary>
        internal static void Refit(Control c)
        {
            if (Applies(c)) { c.Size = c.GetPreferredSize(Size.Empty); }
        }
    }

    /// <summary>A flat, accent-filled checkbox drawn entirely by us.</summary>
    public sealed class ModernCheckBox : CheckBox, ICaptionOffset
    {
        int ICaptionOffset.CaptionLeft => Box + Gap;

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

        // Its own size while AutoSize is on, whatever it is given (SelfSize).
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            SelfSize.Apply(this, ref width, ref height);
            base.SetBoundsCore(x, y, width, height, specified);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); SelfSize.Refit(this); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); SelfSize.Refit(this); }

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
                    // Check mark sized for the 16px box, in whichever of black/white reads
                    // on the fill behind it. It was hardcoded white, and white on Accent is
                    // below 4.5:1 in 36 of the 38 palettes (1.48:1 on Carbon) — the one
                    // mark that says whether a box is ticked, invisible on a light accent.
                    using (var pen = new Pen(Theme.ReadableOn(Enabled ? accent : muted), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
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
    public sealed class ToggleSwitch : CheckBox, ICaptionOffset
    {
        int ICaptionOffset.CaptionLeft => TrackW + Gap;

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

        // Its own size while AutoSize is on, whatever it is given (SelfSize).
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            SelfSize.Apply(this, ref width, ref height);
            base.SetBoundsCore(x, y, width, height, specified);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); SelfSize.Refit(this); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); SelfSize.Refit(this); }

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
    public sealed class ModernRadioButton : RadioButton, ICaptionOffset
    {
        int ICaptionOffset.CaptionLeft => Dot + Gap;

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

        // Its own size while AutoSize is on, whatever it is given (SelfSize).
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            SelfSize.Apply(this, ref width, ref height);
            base.SetBoundsCore(x, y, width, height, specified);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); SelfSize.Refit(this); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); SelfSize.Refit(this); }

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
                // Same as the checkbox tick: the dot sits ON the accent fill, so it takes
                // whichever of black/white reads there rather than always white.
                using (var fill = new SolidBrush(Theme.ReadableOn(Enabled ? accent : muted)))
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
            // ItemHeight BEFORE the owner-draw mode. Set after it, WinForms' ItemHeight setter creates
            // the native window on the spot (ComboBox.UpdateItemHeight → CreateControl), and every
            // property set after that costs real window messages: DropDownStyle destroys the window and
            // makes it again, SetBounds becomes SetWindowPos, each item a CB_ADDSTRING. Profiled at
            // start-up, 565 ms of the 1,113 ms MainForm constructor went on that — windows for combos on
            // tabs nobody had opened, and on a start to the tray, for no one at all. Without a window
            // the values are only stored, and WM_MEASUREITEM answers from them when the window is made.
            ItemHeight = DesiredItemHeight;
            DrawMode = DrawMode.OwnerDrawFixed;
        }

        // Keep the owner-drawn drop-down row height tied to the (DPI-scaled) font so list text isn't
        // vertically clipped at 125%/150%/200% display scaling.
        private int DesiredItemHeight => Math.Max(20, Font.Height + 8);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        private const int WmSetRedraw = 0x000B;

        /// <summary>What the code last asked for (Visible = …); a new control is visible.</summary>
        private bool _wantVisible = true;

        protected override void SetVisibleCore(bool value)
        {
            _wantVisible = value;
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            // WinForms puts every item into the new window here, one message each, and a combo made on
            // a showing page redraws for every one: measured on this machine, a 38-item combo took
            // ~80 ms to make and ~40 ms with redraw held off — the Settings page has ~120 items across
            // its combos, all made the first time it is shown. Only for a combo meant to be SEEN:
            // WM_SETREDRAW(TRUE) sets the visible style, and would show one meant to stay hidden. (Not
            // read from the window's style: WinForms makes a child window hidden and shows it after.)
            bool hold = _wantVisible;
            if (hold) { SendMessage(Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero); }
            try { base.OnHandleCreated(e); }
            finally
            {
                if (hold)
                {
                    SendMessage(Handle, WmSetRedraw, (IntPtr)1, IntPtr.Zero);
                    Invalidate();
                }
            }
            RoundRegion();
        }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); RoundRegion(); UpdateClosedTip(); }

        /// <summary>
        /// The height this combo has once its window exists: the owner-drawn row plus Windows' frame,
        /// 30 px at 100% scale. Without a window WinForms reports the height of its FONT (23 px) and
        /// will not take any other, and the layout checks measure controls on tabs that have not been
        /// shown — so they ask this (LayoutFitter.LaidOutBounds). Windows' own answer for this row
        /// height, measured once from a real combo made the way these used to be, and reused.
        /// </summary>
        internal int ClosedHeight
        {
            get
            {
                if (IsHandleCreated || DropDownStyle == ComboBoxStyle.Simple) { return Height; }
                try { return ClosedHeightFor(ItemHeight, DropDownStyle, Font); }
                catch { return Height; }
            }
        }

        private static readonly System.Collections.Generic.Dictionary<(int, ComboBoxStyle), int> ClosedHeights =
            new System.Collections.Generic.Dictionary<(int, ComboBoxStyle), int>();

        private static int ClosedHeightFor(int itemHeight, ComboBoxStyle style, Font font)
        {
            lock (ClosedHeights)
            {
                if (ClosedHeights.TryGetValue((itemHeight, style), out int h)) { return h; }
                using (var probe = new ComboBox { FlatStyle = FlatStyle.Flat, DropDownStyle = style, Font = font })
                {
                    probe.DrawMode = DrawMode.OwnerDrawFixed;
                    probe.ItemHeight = itemHeight;    // creates the probe's window: the one this costs
                    probe.CreateControl();
                    h = probe.Height;
                }
                ClosedHeights[(itemHeight, style)] = h;
                return h;
            }
        }

        private void RoundRegion()
        {
            if (Width <= 1 || Height <= 1) return;

            using (var p = ModernPaint.Rounded(new Rectangle(0, 0, Width, Height), 7))
            {
                Region = new Region(p);
            }
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            try
            {
                if (IsHandleCreated)
                {
                    ItemHeight = DesiredItemHeight;
                }
                else if (ItemHeight != DesiredItemHeight)
                {
                    // Stored without making the window (see the constructor): the setter only creates
                    // it while an owner-draw mode is on.
                    DrawMode mode = DrawMode;
                    DrawMode = DrawMode.Normal;
                    ItemHeight = DesiredItemHeight;
                    DrawMode = mode;
                }
            }
            catch { }
            UpdateClosedTip();
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
            // The highlighted row is filled with Accent and its text was always white —
            // below 4.5:1 in 36 of the 38 palettes. That is the row you are looking at
            // when a dropdown is open, so it is the worst place to lose the text.
            Color fg = (selected && !isEditField) ? Theme.ReadableOn(accent) : text;

            using (var b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, e.Bounds);

            if (e.Index >= 0 && e.Index < Items.Count)
            {
                string s = GetItemText(Items[e.Index]);
                var r = new Rectangle(e.Bounds.X + ListTextLeft, e.Bounds.Y, e.Bounds.Width - ListTextLeft, e.Bounds.Height);
                // A row of the open list may be drawn in a font of its own (a font picker shows each font
                // in itself); the closed box keeps the page's font, so it lines up with its neighbours.
                Font rowFont = null;
                if (!isEditField && ItemFont != null)
                {
                    try { rowFont = ItemFont(e.Index); } catch { rowFont = null; }
                }
                TextRenderer.DrawText(e.Graphics, s, rowFont ?? Font, r, fg, ItemFlags);
            }
        }

        /// <summary>
        /// The font to draw open-list row N in, or null for the box's own. The caller owns the fonts and keeps
        /// them alive while the box is (they are asked for on every draw).
        /// </summary>
        internal Func<int, Font> ItemFont { get; set; }

        // How a choice is drawn, in the open list and in the closed box. NoPrefix: a choice is shown as
        // written. Without it a lone "&" — in a profile, macro or device name — was taken for a shortcut
        // marker: dropped, with the letter after it underlined.
        private const TextFormatFlags ItemFlags =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        private const int ListTextLeft = 6, ClosedTextLeft = 6, ClosedArrowZone = 24;

        /// <summary>
        /// How wide this box must be to show its widest choice uncut when CLOSED — in 100% units, for a
        /// layout built before the window is scaled (UiFactory.Measure96) — and never below
        /// <paramref name="minWidth"/>.
        /// </summary>
        internal int WidthForChoices96(int minWidth)
        {
            int widest = 0;
            foreach (object item in Items)
            {
                widest = Math.Max(widest, UiFactory.Measure96(GetItemText(item), Font, 0, ItemFlags & ~TextFormatFlags.EndEllipsis).Width);
            }
            return Math.Max(minWidth, widest + ClosedArrowZone + 2);
        }

        /// <summary>How wide <paramref name="choice"/> is as drawn (for the layout checks).</summary>
        internal int ChoiceWidth(string choice) =>
            TextRenderer.MeasureText(choice ?? string.Empty, Font, new Size(int.MaxValue, int.MaxValue),
                ItemFlags & ~TextFormatFlags.EndEllipsis).Width;

        /// <summary>The room the CLOSED box draws its choice in (see PaintClosedCombo).</summary>
        internal int ClosedTextRoom => Math.Max(1, Width - ClosedArrowZone);

        /// <summary>The room a row of the OPEN list has: its width, less the list's border and scroll bar.</summary>
        internal int ListTextRoom => Math.Max(1, OpenListWidth - 2 - ListScrollBar - ListTextLeft);

        private int ListScrollBar => Items.Count > MaxDropDownItems ? SystemInformation.VerticalScrollBarWidth : 0;

        /// <summary>
        /// <see cref="ClosedTextRoom"/> in a layout's units, where a painted pixel is
        /// <paramref name="unitsPerPixel"/> of them (LayoutFitter.SetUnitsPerPixel): the box's width is a
        /// layout number, its arrow is pixels it paints.
        /// </summary>
        internal int ClosedTextRoomIn(float unitsPerPixel) =>
            Math.Max(1, Width - (int)Math.Ceiling(ClosedArrowZone * unitsPerPixel));

        /// <summary><see cref="ListTextRoom"/> in a layout's units (see <see cref="ClosedTextRoomIn"/>).</summary>
        internal int ListTextRoomIn(float unitsPerPixel)
        {
            int widest = 0;
            foreach (object item in Items) { widest = Math.Max(widest, ChoiceWidth(GetItemText(item))); }
            int edges = 2 + ListScrollBar + ListTextLeft;
            int list = Math.Max(Width, (int)Math.Ceiling((widest + edges + 4) * unitsPerPixel));
            return Math.Max(1, list - (int)Math.Ceiling(edges * unitsPerPixel));
        }

        /// <summary>
        /// How wide the list opens: as wide as its widest choice — never narrower than the box, never
        /// past the edge of the screen it opens on — so every choice reads in full while choosing. It
        /// was always the box's own width, and a long translated choice was cut in the list as well.
        /// </summary>
        internal int OpenListWidth
        {
            get
            {
                try
                {
                    int widest = 0;
                    foreach (object item in Items) { widest = Math.Max(widest, ChoiceWidth(GetItemText(item))); }
                    int want = widest + ListTextLeft + 2 + ListScrollBar + 4;
                    int max = Width;
                    if (IsHandleCreated)
                    {
                        Rectangle wa = Screen.FromControl(this).WorkingArea;
                        max = Math.Max(Width, wa.Right - PointToScreen(Point.Empty).X - 4);
                    }
                    else
                    {
                        max = Math.Max(Width, 640);
                    }
                    return Math.Max(Width, Math.Min(want, max));
                }
                catch { return Width; }
            }
        }

        protected override void OnDropDown(EventArgs e)
        {
            try
            {
                int width = OpenListWidth;
                if (DropDownWidth != width) { DropDownWidth = width; }
            }
            catch { }
            base.OnDropDown(e);
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            UpdateClosedTip();
        }

        /// <summary>
        /// A choice too long for the closed box ends in "…" there; its whole text is on hover — the
        /// same as a caption the layout pass shortens — and the tip goes away when it fits again.
        /// </summary>
        private void UpdateClosedTip()
        {
            try
            {
                string s = SelectedIndex >= 0 && SelectedIndex < Items.Count ? GetItemText(Items[SelectedIndex]) : Text;
                bool cut = !string.IsNullOrEmpty(s) && ChoiceWidth(s) > ClosedTextRoom;
                TipStyle.SetCaption(this, cut ? s : null);
            }
            catch { }
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
                var tr = new Rectangle(ClosedTextLeft, 0, ClosedTextRoom, Height);
                TextRenderer.DrawText(g, s, Font, tr, Enabled ? text : muted, ItemFlags);
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

        private const TextFormatFlags SegFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                                                 TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        /// <summary>
        /// True when the three parts, drawn as OnPaint draws them, fit the label's width. Nothing
        /// here wraps or ellipsizes: a line that is too long is simply cut off at the edge, so a
        /// caller with a long and a short way to say something asks first.
        /// </summary>
        public bool Fits(string prefix, string value, string suffix)
        {
            int w = 0;
            foreach (var seg in new[] { (prefix, Font), (value, ValueFont), (suffix, Font) })
            {
                if (!string.IsNullOrEmpty(seg.Item1))
                {
                    w += TextRenderer.MeasureText(seg.Item1, seg.Item2, new Size(int.MaxValue, Height), SegFlags).Width;
                }
            }
            return w <= ClientSize.Width;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor.A == 255 ? BackColor : (Parent != null ? Parent.BackColor : BackColor));
            const TextFormatFlags flags = SegFlags;
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
    /// <summary>
    /// A text box with rounded corners and an outline painted in the theme.
    ///
    /// The corners are cut by a rounded window region, and the outline used to be the square
    /// 1 px frame Windows draws for FixedSingle — so the region clipped that frame at every
    /// corner, and each box showed a top line, a bottom line and two short ticks for sides
    /// instead of an outline (every UiFactory.Text box in Tempo; the Accounts unlock screen made
    /// it obvious). The combo boxes beside them already paint their own rounded border. This does
    /// the same: Windows' square frame is covered in the box's own background, then a rounded
    /// outline is drawn in the theme's border colour — the accent while the box has focus.
    /// </summary>
    public sealed class FlatTextBox : TextBox
    {
        private const int WmPaint = 0x000F;
        private const int WmNcPaint = 0x0085;
        private const int Radius = 6;

        private Color _border = SystemColors.ControlDark;
        private Color _focusBorder = SystemColors.Highlight;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        /// <summary>Takes the outline colours from <paramref name="theme"/> (ThemeManager calls this).</summary>
        public void ApplyTheme(Theme theme)
        {
            if (theme == null) { return; }
            // Border is the theme's DIVIDER colour — on the light palettes barely different from an
            // input's fill, which drew an outline you had to hunt for. The edge of something you
            // type into wants 3:1 (WCAG 1.4.11), so the same hue is pushed until it gets there.
            _border = Theme.Readable(theme.Border, theme.InputBackground, 3.0);
            _focusBorder = Theme.Readable(theme.Accent, theme.InputBackground, 3.0);
            PaintOutline();
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); RoundRegion(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); RoundRegion(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); PaintOutline(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); PaintOutline(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); PaintOutline(); }
        protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); PaintOutline(); }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // After Windows has drawn its frame (WM_NCPAINT) or the text area (WM_PAINT), draw
            // the outline over it.
            if (m.Msg == WmNcPaint || m.Msg == WmPaint)
            {
                PaintOutline();
            }
        }

        private void RoundRegion()
        {
            if (Width <= 1 || Height <= 1) return;

            using (var p = ModernPaint.Rounded(new Rectangle(0, 0, Width, Height), Radius))
            {
                Region = new Region(p);
            }
        }

        private void PaintOutline()
        {
            if (!IsHandleCreated || Width <= 2 || Height <= 2) { return; }
            IntPtr hdc = GetWindowDC(Handle);          // whole window, the frame included
            if (hdc == IntPtr.Zero) { return; }
            try
            {
                using (var g = Graphics.FromHdc(hdc))
                {
                    // Cover the square frame first, so none of it shows through the anti-aliased
                    // edge of the rounded outline.
                    using (var cover = new Pen(BackColor, 2f))
                    {
                        g.DrawRectangle(cover, 0, 0, Width - 1, Height - 1);
                    }
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var path = ModernPaint.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
                    using (var pen = new Pen(Focused && Enabled ? _focusBorder : _border, 1.4f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }
            catch { /* the outline is cosmetic; never let painting throw */ }
            finally
            {
                ReleaseDC(Handle, hdc);
            }
        }
    }

    /// <summary>
    /// A RichTextBox whose frame is painted in the theme.
    ///
    /// Windows draws a RichTextBox's FixedSingle border as the classic 2 px sunken edge — grey on
    /// the top and left, #FFFFFF and #E3E3E3 on the bottom and right — whatever the theme, so the
    /// Captions transcript sat in a bright white frame on every dark theme (measured off the
    /// window). After Windows paints that edge, it is painted over: the outer pixel in the theme's
    /// input outline (the colour FlatTextBox uses), the inner one in the box's own background, so
    /// the text keeps exactly the inset it had.
    /// </summary>
    public sealed class ThemedRichTextBox : RichTextBox
    {
        private const int WmPaint = 0x000F;
        private const int WmNcPaint = 0x0085;

        private Color _frame = SystemColors.ControlDark;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        /// <summary>Takes the frame and background colours from <paramref name="theme"/>.</summary>
        public void ApplyTheme(Theme theme)
        {
            if (theme == null) { return; }
            _frame = Theme.Readable(theme.Border, theme.InputBackground, 3.0);
            BackColor = theme.InputBackground;
            ForeColor = theme.Text;
            PaintFrame();
        }

        protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); PaintFrame(); }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WmNcPaint || m.Msg == WmPaint)
            {
                PaintFrame();
            }
        }

        private void PaintFrame()
        {
            if (!IsHandleCreated || BorderStyle == BorderStyle.None || Width <= 4 || Height <= 4) { return; }
            IntPtr hdc = GetWindowDC(Handle);          // the whole window, the frame included
            if (hdc == IntPtr.Zero) { return; }
            try
            {
                using (var g = Graphics.FromHdc(hdc))
                using (var inner = new Pen(BackColor, 1f))
                using (var edge = new Pen(_frame, 1f))
                {
                    g.DrawRectangle(inner, 1, 1, Width - 3, Height - 3);
                    g.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
                }
            }
            catch { /* the frame is cosmetic; never let painting throw */ }
            finally
            {
                ReleaseDC(Handle, hdc);
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
        //
        // UiFactory.Numeric keeps BorderStyle.FixedSingle — it is what insets the edit box from the edge — so
        // Windows paints its own square, light visual-style frame there first. The rounded region cut that frame
        // at every corner, and every spinner in Tempo showed a top line, a bottom line and two short ticks for
        // sides — "[ 100 ]" — under the outline, the fault FlatTextBox had and lost. Same cure: the square frame
        // is covered in the box's own background, then the outline goes on, in the input outline colour (the
        // accent while the box has the focus).
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WmPaint && _theme != null)
            {
                PaintOutline();
            }
        }

        private void PaintOutline()
        {
            if (!IsHandleCreated || _theme == null || Width <= 2 || Height <= 2) { return; }
            try
            {
                using (var g = Graphics.FromHwnd(Handle))
                {
                    // UpDownBase is an Opaque control: it paints its border strips and a 1 px ring round the edit
                    // box and leaves everything else to its two children. With the edit box seated lower and cut
                    // short (SeatEdit), the strip it moved off and the rows under it kept whatever was on screen
                    // there before — orange and blue ClearType scraps above the digits in the real Clicker tab
                    // (build 261018-2335), which a freshly shown test window could not show. The children are
                    // clipped out of this DC (WS_CLIPCHILDREN), so the fill reaches exactly the box's own pixels.
                    using (var fill = new SolidBrush(BackColor))
                    {
                        g.FillRectangle(fill, ClientRectangle);
                    }
                    using (var cover = new Pen(BackColor, 2f))
                    {
                        g.DrawRectangle(cover, 0, 0, Width - 1, Height - 1);
                    }
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Color edge = ContainsFocus && Enabled
                        ? Theme.Readable(_theme.Accent, _theme.InputBackground, 3.0)
                        : Theme.Readable(_theme.Border, _theme.InputBackground, 3.0);
                    using (var path = ModernPaint.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 7))
                    using (var pen = new Pen(edge, 1.4f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }
            catch { /* the outline is cosmetic; never let painting throw */ }
        }

        // The outline follows the focus, which lives in the edit box inside.
        protected override void OnEnter(EventArgs e) { base.OnEnter(e); Invalidate(); }
        protected override void OnLeave(EventArgs e) { base.OnLeave(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        // Where the digits sit. UpDownBase puts its edit box 2 px inside the edge at the full height, and an edit
        // box writes on its TOP line. While ThemeManager gave that edit box a frame of its own, the frame was the
        // only gap (and the gray "[ ]" square); without it the user's crop (2026-10-18) showed "100000" 2 px from
        // the outline, 2 px above the middle, and the edit box's background covered the outline's left corners.
        // So after WinForms has placed it: the box moves down to the middle, its bottom is cut off where the
        // outline runs (a window region — resizing it instead would make UpDownBase put it straight back), and the
        // text gets a margin.
        private const int EmSetMargins = 0x00D3;
        private const int EcLeftMargin = 0x0001;
        private const int EcRightMargin = 0x0002;
        private Size _editRegionSize;
        private TextBox _edit;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private int Px(int size96) => (int)Math.Round(size96 * DeviceDpi / 96f, MidpointRounding.AwayFromZero);

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);   // UpDownBase's PositionControls: the edit box back at (2, 2), full height
            SeatEdit();
        }

        private TextBox EditBox()
        {
            foreach (Control c in Controls)
            {
                if (c is TextBox t) { return t; }
            }
            return null;
        }

        private void SeatEdit()
        {
            TextBox edit = EditBox();
            if (edit == null || BorderStyle == BorderStyle.None) { return; }
            if (!ReferenceEquals(edit, _edit))
            {
                if (_edit != null)
                {
                    _edit.HandleCreated -= OnEditFontOrHandle;
                    _edit.FontChanged -= OnEditFontOrHandle;
                }
                _edit = edit;
                edit.HandleCreated += OnEditFontOrHandle;
                edit.FontChanged += OnEditFontOrHandle;
                ApplyEditMargins();
            }

            int line = edit.Font.Height;
            int top = Math.Max(edit.Top, (ClientSize.Height - line + 1) / 2);
            if (edit.Top != top) { edit.Top = top; }

            // Keep the box out of the bottom 4 rows: the outline's bottom edge and the start of its corner arcs.
            int visible = Math.Max(line - 1, Math.Min(edit.Height, ClientSize.Height - 4 - top));
            var size = new Size(edit.Width, visible);
            if (size != _editRegionSize && size.Width > 0 && size.Height > 0)
            {
                _editRegionSize = size;
                edit.Region = new Region(new Rectangle(Point.Empty, size));
            }
        }

        // Windows puts an edit box's margins back to the font's defaults when its font changes.
        private void OnEditFontOrHandle(object sender, EventArgs e) => ApplyEditMargins();

        private void ApplyEditMargins()
        {
            if (_edit == null || !_edit.IsHandleCreated) { return; }
            int left = Px(3), right = Px(1);
            SendMessage(_edit.Handle, EmSetMargins, (IntPtr)(EcLeftMargin | EcRightMargin), (IntPtr)((right << 16) | left));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RoundRegion();
            ApplyEditMargins();
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
