using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A small frameless top-most overlay that counts down "N ... 2 ... 1 ... GO!"
    /// before a macro begins playing. Used by the macros tab when the selected
    /// macro has a non-zero <c>PreplayCountdownSeconds</c>. The user gets time to
    /// alt-tab to the target window before input starts firing.
    ///
    /// The form's <see cref="Form.DialogResult"/> is set to
    /// <see cref="DialogResult.OK"/> when the countdown finishes naturally and to
    /// <see cref="DialogResult.Cancel"/> if the user pressed Esc.
    /// </summary>
    public sealed class CountdownOverlayForm : Form
    {
        private readonly Theme _theme;
        private readonly Label _bigNumber;
        private readonly Label _caption;
        private readonly Timer _tick;
        private int _remaining;

        public CountdownOverlayForm(Theme theme, int seconds)
        {
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);
            _remaining = Math.Max(1, seconds);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            Size = new Size(220, 160);
            BackColor = _theme.Surface;
            Region = RoundedRegion(220, 160, 16);
            ForeColor = _theme.Text;
            DoubleBuffered = true;

            _bigNumber = new Label
            {
                Text = _remaining.ToString(),
                Font = new Font("Segoe UI", 56f, FontStyle.Bold),
                ForeColor = _theme.Accent,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };

            _caption = new Label
            {
                Text = "Get ready…",
                Font = new Font("Segoe UI", 10f, FontStyle.Regular),
                ForeColor = _theme.TextMuted,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Bottom,
                Height = 32,
                BackColor = Color.Transparent
            };

            Controls.Add(_bigNumber);
            Controls.Add(_caption);

            _tick = new Timer { Interval = 1000 };
            _tick.Tick += OnTick;

            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };

            Paint += DrawAccentBorder;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _tick.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _tick.Stop();
            _tick.Dispose();
            _bigNumber.Font?.Dispose();
            _caption.Font?.Dispose();
            base.OnFormClosed(e);
        }

        private void OnTick(object sender, EventArgs e)
        {
            _remaining--;
            if (_remaining > 0)
            {
                _bigNumber.Text = _remaining.ToString();
            }
            else
            {
                _bigNumber.Text = "GO!";
                _bigNumber.ForeColor = _theme.Success;
                _tick.Stop();

                // Brief flash of "GO!" before closing.
                var closeTimer = new Timer { Interval = 350 };
                closeTimer.Tick += (cs, ce) =>
                {
                    closeTimer.Stop();
                    closeTimer.Dispose();
                    DialogResult = DialogResult.OK;
                    Close();
                };
                closeTimer.Start();
            }
        }

        private void DrawAccentBorder(object sender, PaintEventArgs e)
        {
            // Rounded accent border around the overlay.
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundedPath(Width, Height, 16))
            using (var pen = new Pen(_theme.Accent, 2))
            {
                e.Graphics.DrawPath(pen, path);
            }
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
    }
}
