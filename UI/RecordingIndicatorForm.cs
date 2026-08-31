using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A small frameless top-most badge shown in the top-right corner of the
    /// primary screen while a macro is recording. Shows a pulsing red dot, the
    /// elapsed time and captured-step count, plus a reminder of which hotkey stops
    /// recording — important because Tempo can auto-minimise while recording, so the
    /// user needs to know how to finish without the window in view.
    /// </summary>
    public sealed class RecordingIndicatorForm : Form
    {
        private readonly Theme _theme;
        private readonly Label _label;
        private readonly Label _hint;
        private readonly Timer _tick;
        private readonly DateTime _start = DateTime.UtcNow;
        private bool _dotOn = true;
        private int _steps;

        public RecordingIndicatorForm(Theme theme) : this(theme, null) { }

        public RecordingIndicatorForm(Theme theme, string stopHint)
        {
            AutoScaleMode = AutoScaleMode.None; // positioned in raw screen pixels
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(236, 60);
            BackColor = _theme.Surface;
            DoubleBuffered = true;

            // Anchor to the top-right corner of the working area.
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 16, wa.Top + 16);

            // Rounded corners to match the modern on-screen overlay.
            Region = RoundedRegion(Width, Height, 14);

            _label = new Label
            {
                Left = 1,
                Top = 8,
                Width = Width - 2,
                Height = 24,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = _theme.Text,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                // Placeholder until the first tick; Refreshindicator formats the live one
                // through the same "{0} REC  {1}:{2:00}   {3} steps" key.
                Text = Utils.Localization.F("{0} REC  {1}:{2:00}   {3} steps", "\u25CF", 0, 0, 0)
            };
            Controls.Add(_label);

            _hint = new Label
            {
                Left = 1,
                Top = 32,
                Width = Width - 2,
                Height = 20,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = _theme.TextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                Text = string.IsNullOrWhiteSpace(stopHint)
                    ? Utils.Localization.T("Use your stop hotkey to finish")
                    : Utils.Localization.F("Press {0} to stop", stopHint)
            };
            Controls.Add(_hint);

            _tick = new Timer { Interval = 500 };
            _tick.Tick += (s, e) => Refreshindicator();

            Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = RoundedPath(Width, Height, 14))
                using (var pen = new Pen(_theme.Danger, 2))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            };
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

        private static Region RoundedRegion(int w, int h, int r)
        {
            using (var path = RoundedPath(w, h, r))
            {
                return new Region(path);
            }
        }

        /// <summary>Updates the captured-step count shown in the badge.</summary>
        public void SetStepCount(int steps)
        {
            _steps = steps;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            OverlayTopmost.Register(Handle);   // stay above fullscreen games / video
            _tick.Start();
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

        private void Refreshindicator()
        {
            _dotOn = !_dotOn;
            TimeSpan elapsed = DateTime.UtcNow - _start;
            string dot = _dotOn ? "\u25CF" : "\u25CB";
            _label.ForeColor = _dotOn ? _theme.Danger : _theme.Text;
            _label.Text = Utils.Localization.F("{0} REC  {1}:{2:00}   {3} steps",
                dot, (int)elapsed.TotalMinutes, elapsed.Seconds, _steps);
        }
    }
}
