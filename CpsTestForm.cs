using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A small clicks-per-second tester. The user clicks the big button as fast
    /// as they can within a fixed window; the form reports total clicks and CPS.
    /// </summary>
    public sealed class CpsTestForm : Form
    {
        private readonly Theme _theme;
        private readonly Button _clickArea;
        private readonly Label _cpsLabel;
        private readonly Label _countLabel;
        private readonly Label _timeLabel;
        private readonly Label _bestLabel;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly List<DateTime> _clicks = new List<DateTime>();

        private bool _running;
        private DateTime _startUtc;
        private int _count;
        private double _best;
        private const int TestSeconds = 10;

        public CpsTestForm(Theme theme)
        {
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            Text = "CPS Test";
            Size = new Size(380, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = _theme.Background;
            ForeColor = _theme.Text;
            Font = UiFactory.BodyFont;

            var title = UiFactory.Label("Click as fast as you can!", 0, 16, FontStyle.Bold, 13f);
            title.Width = 360;
            title.TextAlign = ContentAlignment.MiddleCenter;
            title.AutoSize = false;
            title.Height = 26;

            _timeLabel = UiFactory.Label($"Time: {TestSeconds}.0 s", 0, 50, FontStyle.Regular, 10f);
            _timeLabel.Width = 360;
            _timeLabel.AutoSize = false;
            _timeLabel.TextAlign = ContentAlignment.MiddleCenter;

            _clickArea = UiFactory.PrimaryButton("CLICK!", 60, 90, 260, 120, _theme);
            _clickArea.Font = new Font("Segoe UI", 22f, FontStyle.Bold);
            _clickArea.Click += OnClickArea;

            _countLabel = UiFactory.Label("Clicks: 0", 0, 224, FontStyle.Bold, 12f);
            _countLabel.Width = 360;
            _countLabel.AutoSize = false;
            _countLabel.TextAlign = ContentAlignment.MiddleCenter;

            _cpsLabel = UiFactory.Label("CPS: 0.0", 0, 252, FontStyle.Bold, 16f);
            _cpsLabel.Width = 360;
            _cpsLabel.AutoSize = false;
            _cpsLabel.TextAlign = ContentAlignment.MiddleCenter;
            _cpsLabel.ForeColor = _theme.Accent;

            _bestLabel = UiFactory.Label("Best CPS: 0.0", 0, 288, FontStyle.Regular, 9.5f);
            _bestLabel.Width = 360;
            _bestLabel.AutoSize = false;
            _bestLabel.TextAlign = ContentAlignment.MiddleCenter;
            _bestLabel.ForeColor = _theme.TextMuted;

            Controls.AddRange(new Control[]
            {
                title, _timeLabel, _clickArea, _countLabel, _cpsLabel, _bestLabel
            });

            _timer = new System.Windows.Forms.Timer { Interval = 100 };
            _timer.Tick += OnTick;
        }

        private void OnClickArea(object sender, EventArgs e)
        {
            if (!_running)
            {
                StartTest();
            }

            _count++;
            _clicks.Add(DateTime.UtcNow);
            _countLabel.Text = $"Clicks: {_count}";
        }

        private void StartTest()
        {
            _running = true;
            _count = 0;
            _clicks.Clear();
            _startUtc = DateTime.UtcNow;
            _timer.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            double elapsed = (DateTime.UtcNow - _startUtc).TotalSeconds;
            double remaining = TestSeconds - elapsed;

            if (remaining <= 0)
            {
                remaining = 0;
                FinishTest();
            }

            _timeLabel.Text = $"Time: {remaining:0.0} s";

            if (elapsed > 0.0001)
            {
                double cps = _count / Math.Min(elapsed, TestSeconds);
                _cpsLabel.Text = $"CPS: {cps:0.0}";
                if (cps > _best)
                {
                    _best = cps;
                    _bestLabel.Text = $"Best CPS: {_best:0.0}";
                }
            }
        }

        private void FinishTest()
        {
            _timer.Stop();
            _running = false;

            double finalCps = _count / (double)TestSeconds;
            _cpsLabel.Text = $"CPS: {finalCps:0.0}";
            _clickArea.Text = "Done! Click to retry";

            if (finalCps > _best)
            {
                _best = finalCps;
                _bestLabel.Text = $"Best CPS: {_best:0.0}";
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            base.OnFormClosed(e);
        }
    }
}
