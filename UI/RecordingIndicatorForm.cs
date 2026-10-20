using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A small frameless top-most badge shown in the top-right corner of the
    /// primary screen while a macro is recording. Shows a pulsing red dot, the
    /// elapsed time and captured-step count, plus a reminder of which hotkey stops
    /// recording — important because Tempo can auto-minimise while recording, so the
    /// user needs to know how to finish without the window in view.
    ///
    /// Click-through and never activated, like the clicking badge. It was neither: a click that landed
    /// on it activated Tempo in the middle of the app being recorded — taking the keyboard from it, so
    /// the next keys went to Tempo instead. It can be dragged elsewhere (Overlays ▸ Move overlays in the
    /// tray, see <see cref="SetMovable"/>) and faded (Overlays ▸ Opacity).
    /// </summary>
    public sealed class RecordingIndicatorForm : Form
    {
        private Theme _theme;
        private readonly Label _label;
        private readonly Label _hint;
        private readonly Timer _tick;
        private readonly DateTime _start = DateTime.UtcNow;
        private readonly string _stopHint;
        private bool _dotOn = true;
        private int _steps;
        private byte _alpha = 245;

        public RecordingIndicatorForm(Theme theme) : this(theme, null) { }

        public RecordingIndicatorForm(Theme theme, string stopHint)
        {
            AutoScaleMode = AutoScaleMode.None; // positioned in raw screen pixels
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            // Its numbers are 100% ones in the screen's pixels (D): the window is not scaled by WinForms,
            // and its fonts grow with the screen — at 150% the two lines no longer fit the 60 px badge.
            Size = new Size(D(236), D(60));
            BackColor = _theme.Surface;
            DoubleBuffered = true;

            Place();

            // Rounded corners to match the modern on-screen overlay.
            Region = RoundedRegion(Width, Height, D(14));

            _label = new PassThroughLabel
            {
                Left = 1,
                Top = D(8),
                Width = Width - 2,
                Height = D(24),
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = _theme.Text,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                // Placeholder until the first tick; Refreshindicator formats the live one
                // through the same "{0} REC  {1}:{2:00}   {3} steps" key.
                Text = Utils.Localization.F("{0} REC  {1}:{2:00}   {3} steps", "●", 0, 0, 0)
            };
            Controls.Add(_label);

            _stopHint = string.IsNullOrWhiteSpace(stopHint)
                ? Utils.Localization.T("Use your stop hotkey to finish")
                : Utils.Localization.F("Press {0} to stop", stopHint);
            _hint = new PassThroughLabel
            {
                Left = 1,
                Top = D(32),
                Width = Width - 2,
                Height = D(20),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = _theme.TextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                Text = _stopHint
            };
            Controls.Add(_hint);
            FitToText();

            _tick = new Timer { Interval = 500 };
            _tick.Tick += (s, e) => Refreshindicator();

            Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = RoundedPath(Width, Height, D(14)))
                using (var pen = new Pen(_theme.Danger, 2f * DeviceDpi / 96f))
                {
                    // Dashed while it can be dragged, like the other overlays in move mode.
                    if (_movable) { pen.DashStyle = DashStyle.Dash; }
                    e.Graphics.DrawPath(pen, path);
                }
            };
        }

        /// <summary>One of this badge's 100% numbers in the screen's pixels.</summary>
        private int D(int px) => (int)Math.Round(px * DeviceDpi / 96f);

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

        private static Region RoundedRegion(int w, int h, int r)
        {
            using (var path = RoundedPath(w, h, r))
            {
                return new Region(path);
            }
        }

        /// <summary>
        /// As wide as its longest line needs (never under 236), so a long translation of the stop hint
        /// or the move hint is not cut; re-rounds its corners and keeps its place.
        /// </summary>
        private void FitToText()
        {
            try
            {
                int need = Math.Max(TextRenderer.MeasureText(_label.Text, _label.Font).Width,
                                    TextRenderer.MeasureText(_hint.Text, _hint.Font).Width) + D(28);
                // From its position, not Screen.FromControl: that makes the window handle, and this
                // runs in the constructor.
                int max = Screen.FromPoint(new Point(Left + D(8), Top + D(8))).WorkingArea.Width - D(24);
                int w = Math.Max(D(236), Math.Min(need, max));
                if (w == Width) { return; }
                Width = w;
                _label.Width = w - 2;
                _hint.Width = w - 2;
                Region = RoundedRegion(Width, Height, D(14));
                Place();
            }
            catch { }
        }

        /// <summary>Updates the captured-step count shown in the badge.</summary>
        public void SetStepCount(int steps)
        {
            _steps = steps;
        }

        /// <summary>
        /// Re-themes the badge. It had no way to be told: a theme picked while recording (or while placing
        /// the badge from the tray's Overlays menu) left it in the colours it came up with — surface, text,
        /// the red of the dot and the outline.
        /// </summary>
        public void ApplyTheme(Theme theme)
        {
            if (theme == null || IsDisposed) { return; }
            _theme = theme;
            BackColor = theme.Surface;
            _hint.ForeColor = theme.TextMuted;
            // The dot's half of the pulse as it stands now — not a tick of the pulse.
            _label.ForeColor = Preview || _dotOn ? theme.Danger : theme.Text;
            Invalidate(true);
        }

        // ── Where the user put it, and how opaque ─────────────────────────────────────────────

        /// <summary>The badge's top-left as the user dragged it, or null for its corner (top right).</summary>
        public Point? FreePosition
        {
            get => _free;
            set { _free = value; Place(); }
        }
        private Point? _free;

        /// <summary>A drag ended; the badge's new top-left.</summary>
        public event Action<Point> Moved;

        /// <summary>Double-clicked while movable: "I'm done moving".</summary>
        public event Action MoveDone;

        public bool Movable => _movable;
        private bool _movable;

        /// <summary>
        /// A stand-in shown only so it can be placed: the clock stops at 0:00 and the dot stops pulsing.
        /// Set before it is shown.
        /// </summary>
        public bool Preview { get; set; }

        /// <summary>Lets it be dragged (double-click to finish), or makes it click-through again.</summary>
        public void SetMovable(bool on)
        {
            if (_movable == on) { return; }
            _movable = on;
            SetClickThrough(!on);
            _hint.Text = on ? Utils.Localization.T("Drag to move · double-click when done") : _stopHint;
            FitToText();
            Invalidate();
        }

        /// <summary>Opacity, 40–100 %.</summary>
        public void SetOpacity(int opacityPct)
        {
            _alpha = (byte)Math.Round(Math.Max(40, Math.Min(100, opacityPct)) * 255.0 / 100.0);
            if (IsHandleCreated)
            {
                try { SetLayeredWindowAttributes(Handle, 0, _alpha, LWA_ALPHA); } catch { }
            }
        }

        private void Place()
        {
            if (_free.HasValue)
            {
                Point p = _free.Value;
                foreach (Screen s in Screen.AllScreens)
                {
                    Rectangle wa = s.WorkingArea;
                    if (!wa.Contains(p.X + D(8), p.Y + D(8))) { continue; }
                    Location = new Point(Math.Max(wa.Left, Math.Min(p.X, wa.Right - Width)),
                                         Math.Max(wa.Top, Math.Min(p.Y, wa.Bottom - Height)));
                    return;
                }
            }
            // Anchor to the top-right corner of the working area.
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - D(16), area.Top + D(16));
        }

        private void SetClickThrough(bool through)
        {
            if (!IsHandleCreated) { return; }
            try
            {
                int ex = GetWindowLong(Handle, GWL_EXSTYLE);
                int want = through ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
                if (want != ex) { SetWindowLong(Handle, GWL_EXSTYLE, want); }
            }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // A layered window stays invisible until its alpha is set.
            try { SetLayeredWindowAttributes(Handle, 0, _alpha, LWA_ALPHA); } catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            OverlayTopmost.Register(Handle);   // stay above fullscreen games / video
            if (!Preview) { _tick.Start(); }
            Refreshindicator();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            OverlayTopmost.Unregister(Handle);
            _tick.Stop();
            _tick.Dispose();
            _label.Font?.Dispose();
            _hint.Font?.Dispose();
            base.OnFormClosed(e);
        }

        // The overlay should never steal focus from the window being recorded.
        protected override bool ShowWithoutActivation => true;

        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int GWL_EXSTYLE = -20;
        private const uint LWA_ALPHA = 0x2;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_LAYERED = 0x00080000;
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;  // keep out of Alt-Tab
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                if (!_movable) { cp.ExStyle |= WS_EX_TRANSPARENT; }
                return cp;
            }
        }

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCLBUTTONDBLCLK = 0x00A3;
        private const int WM_EXITSIZEMOVE = 0x0232;
        private const int HTCAPTION = 2;

        protected override void WndProc(ref Message m)
        {
            if (_movable)
            {
                switch (m.Msg)
                {
                    case WM_NCHITTEST:
                        m.Result = (IntPtr)HTCAPTION;
                        return;
                    case WM_NCLBUTTONDBLCLK:
                        // Never to Windows: a double-click on a "caption" maximises the window.
                        MoveDone?.Invoke();
                        return;
                    case WM_EXITSIZEMOVE:
                        base.WndProc(ref m);
                        _free = Location;
                        Moved?.Invoke(Location);
                        return;
                }
            }
            base.WndProc(ref m);
        }

        private void Refreshindicator()
        {
            if (Preview)
            {
                // Placing it: the look of a recording, without a clock running for nothing.
                _label.ForeColor = _theme.Danger;
                _label.Text = Utils.Localization.F("{0} REC  {1}:{2:00}   {3} steps", "●", 0, 0, 0);
                return;
            }
            _dotOn = !_dotOn;
            TimeSpan elapsed = DateTime.UtcNow - _start;
            string dot = _dotOn ? "●" : "○";
            _label.ForeColor = _dotOn ? _theme.Danger : _theme.Text;
            _label.Text = Utils.Localization.F("{0} REC  {1}:{2:00}   {3} steps",
                dot, (int)elapsed.TotalMinutes, elapsed.Seconds, _steps);
        }

        /// <summary>
        /// A label the mouse goes through to the badge under it, so the whole badge drags in move mode
        /// (a label answers its own hit test and swallowed the press).
        /// </summary>
        private sealed class PassThroughLabel : Label
        {
            private const int WM_NCHITTEST = 0x0084;
            private const int HTTRANSPARENT = -1;

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_NCHITTEST)
                {
                    m.Result = (IntPtr)HTTRANSPARENT;
                    return;
                }
                base.WndProc(ref m);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
