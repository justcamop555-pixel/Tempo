using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A small frameless, always-on-top, click-through badge shown near the top of
    /// the primary screen while the clicker is running. It pulses an accent dot and
    /// shows the live click count and CPS, so you can always tell Tempo is active —
    /// even with the main window minimised or hidden to the tray (handy on Windows
    /// 11, where a background app gives no other visible sign it's clicking).
    ///
    /// The window is click-through (WS_EX_TRANSPARENT) and never activates, so it
    /// can never intercept the auto-clicks or steal focus from whatever you're using.
    /// </summary>
    public sealed class ClickingIndicatorForm : Form
    {
        private Theme _theme;
        private readonly Timer _tick;
        private bool _dotOn = true;
        private string _stats = "";
        private string _title = "Tempo \u2014 clicking";
        private string _hint = "";
        // Built once. These used to be constructed inside OnPaint, which runs twice a
        // second for the pulsing dot, and they are needed outside painting anyway now
        // that the badge measures its own text.
        private readonly Font _titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        private readonly Font _subFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        private Color _dotColor;

        // Layout the user chose in Settings \u2192 overlay "Customise\u2026".
        private int _corner;          // 0 top-centre,1 TL,2 TR,3 BL,4 BR,5 bottom-centre
        private byte _alpha = 245;    // window opacity (0\u2013255)
        private bool _showClicks = true;
        private bool _showCps = true;
        private bool _showElapsed;

        public ClickingIndicatorForm(Theme theme) : this(theme, null, default(Color)) { }

        public ClickingIndicatorForm(Theme theme, string title, Color dotColor)
        {
            AutoScaleMode = AutoScaleMode.None; // positioned in raw screen pixels
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);
            if (!string.IsNullOrEmpty(title)) _title = title;
            _dotColor = dotColor.A == 0 ? _theme.Success : dotColor;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(232, 46);
            DoubleBuffered = true;
            BackColor = _theme.Surface;

            RepositionForCorner();

            _tick = new Timer { Interval = 500 };
            _tick.Tick += (s, e) =>
            {
                _dotOn = !_dotOn;
                Invalidate();
                // Keep the badge above other top-most windows (e.g. a maximised main
                // window) without stealing focus, so it can never get buried and look
                // like it didn't appear.
                if (IsHandleCreated)
                {
                    try { SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); } catch { }
                }
            };
        }

        /// <summary>Updates the live "N clicks · X CPS" text shown in the badge.</summary>
        public void SetStats(long clicks, double cps)
        {
            string next = $"{clicks:N0} clicks   \u00B7   {cps:0.0} CPS";
            if (next != _stats)
            {
                _stats = next;
                FitToContent();
                Invalidate();
            }
        }

        /// <summary>
        /// Applies the user's overlay preferences: which corner, how opaque, and which
        /// of clicks / CPS / elapsed to show. Safe to call before or after the handle
        /// exists.
        /// </summary>
        public void Configure(int corner, int opacityPct, bool showClicks, bool showCps, bool showElapsed)
        {
            _corner = corner < 0 || corner > 5 ? 0 : corner;
            if (opacityPct < 40) { opacityPct = 40; }
            if (opacityPct > 100) { opacityPct = 100; }
            _alpha = (byte)Math.Round(opacityPct * 255.0 / 100.0);
            // If the user turned every element off, keep CPS so the badge isn't blank.
            if (!showClicks && !showCps && !showElapsed) { showCps = true; }
            _showClicks = showClicks;
            _showCps = showCps;
            _showElapsed = showElapsed;

            RepositionForCorner();
            if (IsHandleCreated)
            {
                try { SetLayeredWindowAttributes(Handle, 0, _alpha, LWA_ALPHA); } catch { }
            }
            Invalidate();
        }

        private void RepositionForCorner()
        {
            Screen scr = Screen.FromPoint(Cursor.Position) ?? Screen.PrimaryScreen;
            var wa = scr.WorkingArea;
            const int m = 12;                        // margin from the screen edge
            int x, y;
            switch (_corner)
            {
                case 1: x = wa.Left + m;                       y = wa.Top + m; break;
                case 2: x = wa.Right - Width - m;              y = wa.Top + m; break;
                case 3: x = wa.Left + m;                       y = wa.Bottom - Height - m; break;
                case 4: x = wa.Right - Width - m;              y = wa.Bottom - Height - m; break;
                case 5: x = wa.Left + (wa.Width - Width) / 2;  y = wa.Bottom - Height - m; break;
                default: x = wa.Left + (wa.Width - Width) / 2; y = wa.Top + m; break;
            }
            // Don't sit on top of the caption bar.
            //
            // Both overlays place themselves from the work area with no idea the other
            // exists, and both favour the bottom: the caption bar sits bottom-centre
            // (56 px up), and this indicator's "bottom centre" corner lands in the same
            // place — so with Live Captions on, the running indicator covered the
            // captions, or the captions covered it. The bottom-left and bottom-right
            // corners collide with it too on a narrow screen. Whoever is showing
            // captions is reading them, so the indicator is the one that moves: it hops
            // above the bar, keeping its chosen corner.
            var avoid = AvoidRect;
            if (avoid.Width > 0 && avoid.Height > 0)
            {
                var mine = new Rectangle(x, y, Width, Height);
                if (mine.IntersectsWith(avoid))
                {
                    int lifted = avoid.Top - Height - m;
                    // Only lift if there is somewhere to lift TO; otherwise drop below.
                    y = lifted >= wa.Top ? lifted : Math.Min(wa.Bottom - Height, avoid.Bottom + m);
                }
            }

            Location = new Point(x, y);
        }

        /// <summary>
        /// A screen rectangle this indicator must not overlap (the caption bar). Empty
        /// when there is nothing to avoid. Set by the form that owns both overlays.
        /// </summary>
        public Rectangle AvoidRect { get; set; }

        /// <summary>
        /// Updates the live badge line from the enabled elements only, joined by a dot.
        /// </summary>
        public void SetStats(long clicks, double cps, long elapsedSeconds)
        {
            var parts = new System.Collections.Generic.List<string>(3);
            if (_showClicks) { parts.Add($"{clicks:N0} clicks"); }
            if (_showCps) { parts.Add($"{cps:0.0} CPS"); }
            if (_showElapsed && elapsedSeconds >= 0)
            {
                var t = TimeSpan.FromSeconds(elapsedSeconds);
                parts.Add(t.Hours > 0 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss"));
            }
            string next = string.Join("   \u00B7   ", parts);
            if (next != _stats)
            {
                _stats = next;
                FitToContent();
                Invalidate();
            }
        }

        /// <summary>Sets the sub-line text directly (used for non-click activity).</summary>
        public void SetStatusText(string text)
        {
            if (text != _stats) { _stats = text ?? ""; FitToContent(); Invalidate(); }
        }

        // Where the text column starts, past the pulsing dot, and the gap kept on the right.
        private const int TextLeft = 34;
        private const int TextRightPad = 14;
        private const int MinBadgeWidth = 232;

        /// <summary>
        /// Grows the badge to whatever its longest line actually needs.
        ///
        /// The width was a hard-coded 232 px while the text was drawn at a fixed offset
        /// with no measuring, so a long line simply ran off the end and was cut. It only
        /// bites once the numbers get big — "1,234,567 clicks · 199.9 CPS · 1:23:45"
        /// needs 214 px of the 198 available — which is exactly the long, fast run where
        /// someone is most likely to be watching the badge instead of the window. Shrinks
        /// back down again too, so a short line doesn't leave a stretched badge behind.
        /// </summary>
        private void FitToContent()
        {
            try
            {
                int needed;
                using (var g = CreateGraphics())
                {
                    string statsLine = string.IsNullOrEmpty(_stats) ? "running…" : _stats;
                    float w = g.MeasureString(_title, _titleFont).Width;
                    w = Math.Max(w, g.MeasureString(statsLine, _subFont).Width);
                    if (!string.IsNullOrEmpty(_hint))
                    {
                        w = Math.Max(w, g.MeasureString(_hint, _subFont).Width);
                    }
                    needed = TextLeft + (int)Math.Ceiling(w) + TextRightPad;
                }

                // Never below the original size, and never wider than the screen it sits on.
                int max = MinBadgeWidth;
                try
                {
                    Screen scr = Screen.FromPoint(Cursor.Position) ?? Screen.PrimaryScreen;
                    max = Math.Max(MinBadgeWidth, scr.WorkingArea.Width - 24);
                }
                catch { }
                int target = Math.Min(Math.Max(needed, MinBadgeWidth), max);

                if (target != Width)
                {
                    Width = target;
                    if (IsHandleCreated) { ReclipRegion(); }
                    // The right- and centre-anchored corners are measured from the badge
                    // width, so a resize has to re-place it or it drifts off its corner.
                    RepositionForCorner();
                }
            }
            catch { /* measuring must never take the overlay down */ }
        }

        public void ApplyTheme(Theme theme)
        {
            if (theme != null) { _theme = theme; BackColor = theme.Surface; Invalidate(); }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _tick.Start();
            Invalidate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            OverlayTopmost.Unregister(Handle);
            _tick.Stop();
            _tick.Dispose();
            _titleFont.Dispose();
            _subFont.Dispose();
            base.OnFormClosed(e);
        }

        // Never steal focus from the foreground window.
        protected override bool ShowWithoutActivation => true;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
        private const uint LWA_ALPHA = 0x2;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // WS_EX_LAYERED windows stay invisible until their layered attributes are
            // set. Apply the user's chosen opacity so the painted badge shows (while
            // still being click-through via WS_EX_TRANSPARENT).
            try { SetLayeredWindowAttributes(Handle, 0, _alpha, LWA_ALPHA); } catch { }
            ReclipRegion();
            OverlayTopmost.Register(Handle);   // stay above fullscreen games / video
        }

        private void ReclipRegion()
        {
            // Clip the window to a rounded shape so the corners aren't boxy.
            try
            {
                using (var path = Rounded(new Rectangle(0, 0, Width, Height), 10))
                {
                    Region = new Region(path);
                }
            }
            catch { }
        }

        /// <summary>
        /// Sets an optional small third line (e.g. "Press F8 to stop"). When set, the
        /// badge grows to make room; pass null/empty to remove it.
        /// </summary>
        public void SetHint(string hint)
        {
            _hint = hint ?? "";
            int targetH = string.IsNullOrEmpty(_hint) ? 46 : 62;
            if (Height != targetH)
            {
                Height = targetH;
                if (IsHandleCreated) ReclipRegion();
                // A bottom-anchored badge is positioned from its own height, so growing
                // for the hint line pushed it 16 px further down — towards the taskbar
                // it was meant to clear — until something else happened to re-place it.
                RepositionForCorner();
            }
            FitToContent();
            Invalidate();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_LAYERED = 0x00080000;
                const int WS_EX_TRANSPARENT = 0x00000020; // mouse events pass through
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;  // keep out of Alt-Tab
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Rounded(rect, 10))
            {
                using (var fill = new SolidBrush(_theme.Surface))
                {
                    g.FillPath(fill, path);
                }
                using (var pen = new Pen(_dotColor.A == 0 ? _theme.Success : _dotColor, 2))
                {
                    g.DrawPath(pen, path);
                }
            }

            // Pulsing status dot.
            int dotD = 12;
            var dot = new Rectangle(14, (Height - dotD) / 2, dotD, dotD);
            Color baseDot = _dotColor.A == 0 ? _theme.Success : _dotColor;
            Color dotColor = _dotOn ? baseDot : Blend(baseDot, _theme.Surface, 0.55);
            using (var db = new SolidBrush(dotColor))
            {
                g.FillEllipse(db, dot);
            }

            using (var tb = new SolidBrush(_theme.Text))
            using (var mb = new SolidBrush(_theme.TextMuted))
            {
                g.DrawString(_title, _titleFont, tb, TextLeft, 5);
                g.DrawString(string.IsNullOrEmpty(_stats) ? "running\u2026" : _stats, _subFont, mb, TextLeft, 25);
                if (!string.IsNullOrEmpty(_hint))
                {
                    g.DrawString(_hint, _subFont, mb, TextLeft, 43);
                }
            }
        }

        private static Color Blend(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
