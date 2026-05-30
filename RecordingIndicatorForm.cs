using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A small frameless top-most badge shown in the top-right corner of the
    /// primary screen while a macro is recording. Displays a pulsing red dot, the
    /// elapsed time, and the number of steps captured so far, so the user always
    /// knows recording is live even when the main window is not focused.
    /// </summary>
    public sealed class RecordingIndicatorForm : Form
    {
        private readonly Theme _theme;
        private readonly Label _label;
        private readonly Timer _tick;
        private readonly DateTime _start = DateTime.UtcNow;
        private bool _dotOn = true;
        private int _steps;

        public RecordingIndicatorForm(Theme theme)
        {
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(190, 44);
            BackColor = _theme.Surface;
            DoubleBuffered = true;

            // Anchor to the top-right corner of the working area.
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 16, wa.Top + 16);

            _label = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = _theme.Text,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                Text = "● REC  0:00"
            };
            Controls.Add(_label);

            _tick = new Timer { Interval = 500 };
            _tick.Tick += (s, e) => Refreshindicator();

            Paint += (s, e) =>
            {
                using (var pen = new Pen(_theme.Danger, 2))
                {
                    e.Graphics.DrawRectangle(pen, 1, 1, Width - 2, Height - 2);
                }
            };
        }

        /// <summary>Updates the captured-step count shown in the badge.</summary>
        public void SetStepCount(int steps)
        {
            _steps = steps;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _tick.Start();
            Refreshindicator();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _tick.Stop();
            _tick.Dispose();
            base.OnFormClosed(e);
        }

        // The overlay should never steal focus from the window being recorded.
        protected override bool ShowWithoutActivation => true;

        private void Refreshindicator()
        {
            _dotOn = !_dotOn;
            TimeSpan elapsed = DateTime.UtcNow - _start;
            string dot = _dotOn ? "●" : "○";
            _label.ForeColor = _dotOn ? _theme.Danger : _theme.Text;
            _label.Text = $"{dot} REC  {(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}   {_steps} steps";
        }
    }
}
