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
        private Label _peakLabel;
        private Label _attemptsLabel;
        private readonly System.Collections.Generic.List<double> _attempts = new System.Collections.Generic.List<double>();
        private ThemedProgressBar _timeBar;
        private Button[] _durationButtons;
        private MouseButtons _testButton = MouseButtons.Left;
        private Button[] _mbButtons;
        private Label _titleLabel;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly List<DateTime> _clicks = new List<DateTime>();

        private bool _running;
        private DateTime _startUtc;
        private int _count;
        private double _best;
        private double _peak;
        private int _testSeconds = 10;

        /// <summary>All-time best average CPS (persisted by the caller across sessions).</summary>
        public double AllTimeBest { get; private set; }

        public CpsTestForm(Theme theme, double allTimeBest)
        {
            AllTimeBest = allTimeBest;
            AutoScaleMode = AutoScaleMode.Font;
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            Text = "CPS Test";
            ClientSize = new Size(364, 462);
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
            _titleLabel = title;

            _timeLabel = UiFactory.Label($"Time: {_testSeconds}.0 s", 0, 50, FontStyle.Regular, 10f);
            _timeLabel.Width = 360;
            _timeLabel.AutoSize = false;
            _timeLabel.TextAlign = ContentAlignment.MiddleCenter;

            _clickArea = UiFactory.PrimaryButton("CLICK!", 60, 90, 260, 120, _theme);
            _clickArea.Font = new Font("Segoe UI", 18f, FontStyle.Bold);
            _clickArea.MouseDown += OnClickAreaMouseDown;

            _timeBar = new ThemedProgressBar
            {
                Left = 60,
                Top = 76,
                Width = 260,
                Height = 8,
                Maximum = 100,
                Value = 0
            };
            _timeBar.ApplyTheme(_theme);
            Controls.Add(_timeBar);

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
            if (AllTimeBest > 0) _bestLabel.Text = $"All-time best: {AllTimeBest:0.0}";

            _peakLabel = UiFactory.Label("Peak (1s): 0", 0, 310, FontStyle.Regular, 9.5f);
            _peakLabel.Width = 360;
            _peakLabel.AutoSize = false;
            _peakLabel.TextAlign = ContentAlignment.MiddleCenter;
            _peakLabel.ForeColor = _theme.TextMuted;

            _attemptsLabel = UiFactory.Label("", 0, 418, FontStyle.Regular, 9f);
            _attemptsLabel.Width = 360;
            _attemptsLabel.AutoSize = false;
            _attemptsLabel.TextAlign = ContentAlignment.MiddleCenter;
            _attemptsLabel.ForeColor = _theme.TextMuted;

            Controls.AddRange(new Control[]
            {
                title, _timeLabel, _clickArea, _countLabel, _cpsLabel, _bestLabel, _peakLabel, _attemptsLabel
            });

            // Test-duration selector (disabled while a test is running).
            var durLabel = UiFactory.Label("Test length:", 60, 348, FontStyle.Regular, 9f);
            durLabel.AutoSize = true;
            Controls.Add(durLabel);

            int[] options = { 5, 10, 15, 30 };
            _durationButtons = new Button[options.Length];
            int bx = 150;
            for (int i = 0; i < options.Length; i++)
            {
                int secs = options[i];
                var b = UiFactory.Button(secs + "s", bx, 344, 44, 28);
                b.Tag = secs;
                b.Click += OnDurationClicked;
                _durationButtons[i] = b;
                Controls.Add(b);
                bx += 50;
            }
            HighlightDurationButtons();

            // Mouse-button selector: test Left, Middle or Right clicks.
            var mbLabel = UiFactory.Label("Button:", 60, 388, FontStyle.Regular, 9f);
            mbLabel.AutoSize = true;
            Controls.Add(mbLabel);

            string[] mbNames = { "Left", "Middle", "Right" };
            MouseButtons[] mbVals = { MouseButtons.Left, MouseButtons.Middle, MouseButtons.Right };
            _mbButtons = new Button[mbNames.Length];
            int mx = 150;
            for (int i = 0; i < mbNames.Length; i++)
            {
                var b = UiFactory.Button(mbNames[i], mx, 384, 64, 28);
                b.Tag = mbVals[i];
                b.Click += OnMouseButtonClicked;
                _mbButtons[i] = b;
                Controls.Add(b);
                mx += 70;
            }
            HighlightMouseButtons();
            UpdateTitleForButton(title);

            _timer = new System.Windows.Forms.Timer { Interval = 100 };
            _timer.Tick += OnTick;
        }

        private void OnDurationClicked(object sender, EventArgs e)
        {
            if (_running)
            {
                return; // don't change length mid-test
            }

            if (sender is Button b && b.Tag is int secs)
            {
                _testSeconds = secs;
                _timeLabel.Text = $"Time: {_testSeconds}.0 s";
                HighlightDurationButtons();
            }
        }

        private void HighlightDurationButtons()
        {
            if (_durationButtons == null)
            {
                return;
            }

            foreach (Button b in _durationButtons)
            {
                bool selected = b.Tag is int s && s == _testSeconds;
                b.BackColor = selected ? _theme.Accent : _theme.Surface2;
                b.ForeColor = selected ? Color.White : _theme.Text;
            }
        }

        /// <summary>A light-hearted rating for a result, by clicks per second.</summary>
        private static string RankFor(double cps)
        {
            if (cps < 4) return "Slow";
            if (cps < 7) return "Average";
            if (cps < 10) return "Good";
            if (cps < 13) return "Fast";
            if (cps < 16) return "Very fast";
            return "Insane!";
        }

        /// <summary>A distinct colour per rating so it stands out at a glance.</summary>
        private Color RankColor(double cps)
        {
            if (cps < 4) return _theme.Danger;
            if (cps < 7) return _theme.Warning;
            if (cps < 10) return _theme.Text;
            if (cps < 13) return _theme.Success;
            if (cps < 16) return _theme.Accent;
            return Color.Gold;
        }

        /// <summary>Highest number of clicks in any one-second window (peak burst rate).</summary>
        private int ComputePeakCps()
        {
            if (_clicks.Count <= 1)
            {
                return _clicks.Count;
            }

            int peak = 0;
            int start = 0;
            for (int end = 0; end < _clicks.Count; end++)
            {
                while ((_clicks[end] - _clicks[start]).TotalSeconds > 1.0)
                {
                    start++;
                }
                int window = end - start + 1;
                if (window > peak)
                {
                    peak = window;
                }
            }
            return peak;
        }

        private void OnClickAreaMouseDown(object sender, MouseEventArgs e)
        {
            // Only count presses of the button being tested.
            if (e.Button != _testButton)
            {
                return;
            }
            if (!_running)
            {
                StartTest();
            }

            _count++;
            _clicks.Add(DateTime.UtcNow);
            _countLabel.Text = $"Clicks: {_count}";
        }

        private void OnMouseButtonClicked(object sender, EventArgs e)
        {
            if (_running)
            {
                return; // don't change the tested button mid-test
            }
            if (sender is Button b && b.Tag is MouseButtons mb)
            {
                _testButton = mb;
                HighlightMouseButtons();
                UpdateTitleForButton(_titleLabel);
            }
        }

        private void HighlightMouseButtons()
        {
            if (_mbButtons == null)
            {
                return;
            }
            foreach (Button b in _mbButtons)
            {
                bool selected = b.Tag is MouseButtons mb && mb == _testButton;
                b.BackColor = selected ? _theme.Accent : _theme.Surface2;
                b.ForeColor = selected ? Color.White : _theme.Text;
            }
        }

        private void UpdateTitleForButton(Label title)
        {
            if (title == null)
            {
                return;
            }
            string name = _testButton == MouseButtons.Middle ? "middle"
                        : _testButton == MouseButtons.Right ? "right" : "left";
            title.Text = $"Click as fast as you can!  ({name} button)";
        }

        private void StartTest()
        {
            _running = true;
            _count = 0;
            _peak = 0;
            _clicks.Clear();
            _startUtc = DateTime.UtcNow;
            _clickArea.Text = "CLICK!";
            if (_bestLabel != null) _bestLabel.ForeColor = _theme.TextMuted;
            if (_cpsLabel != null)
            {
                _cpsLabel.ForeColor = _theme.Accent;
                _cpsLabel.Text = "CPS: 0.0";
            }
            if (_timeBar != null) _timeBar.Value = 0;
            if (_durationButtons != null)
            {
                foreach (Button b in _durationButtons) b.Enabled = false;
            }
            if (_mbButtons != null)
            {
                foreach (Button b in _mbButtons) b.Enabled = false;
            }
            _timer.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            double elapsed = (DateTime.UtcNow - _startUtc).TotalSeconds;
            double remaining = _testSeconds - elapsed;

            if (remaining <= 0)
            {
                remaining = 0;
                FinishTest();
            }

            _timeLabel.Text = $"Time: {remaining:0.0} s";
            if (_timeBar != null)
            {
                int prog = (int)Math.Round(Math.Min(elapsed, _testSeconds) / _testSeconds * 100);
                _timeBar.Value = prog;
            }

            if (elapsed > 0.0001)
            {
                double cps = _count / Math.Min(elapsed, _testSeconds);
                _cpsLabel.Text = $"CPS: {cps:0.0}";
            }

            int peak = ComputePeakCps();
            if (peak > _peak)
            {
                _peak = peak;
                _peakLabel.Text = $"Peak (1s): {peak}";
            }
        }

        private void FinishTest()
        {
            _timer.Stop();
            _running = false;
            if (_timeBar != null) _timeBar.Value = 100;
            if (_durationButtons != null)
            {
                foreach (Button b in _durationButtons) b.Enabled = true;
            }
            if (_mbButtons != null)
            {
                foreach (Button b in _mbButtons) b.Enabled = true;
            }

            double finalCps = _count / (double)_testSeconds;
            _cpsLabel.Text = $"{finalCps:0.0} CPS  \u00b7  {RankFor(finalCps)}";
            _cpsLabel.ForeColor = RankColor(finalCps);
            _clickArea.Text = "Click to retry";

            // Track this session's attempts so the user can see how recent tries
            // compare without losing them to the next round.
            if (finalCps > 0)
            {
                _attempts.Add(finalCps);
                double sum = 0, max = 0;
                foreach (double a in _attempts)
                {
                    sum += a;
                    if (a > max) max = a;
                }
                double avg = sum / _attempts.Count;
                _attemptsLabel.Text = $"This session: {_attempts.Count} tries  ·  avg {avg:0.0}  ·  best {max:0.0}";
            }

            int peak = ComputePeakCps();
            if (peak > _peak)
            {
                _peak = peak;
            }
            _peakLabel.Text = $"Peak (1s): {(int)_peak}";

            bool newAllTimeBest = finalCps > AllTimeBest && finalCps > 0;
            if (finalCps > _best)
            {
                _best = finalCps;
            }
            if (newAllTimeBest)
            {
                AllTimeBest = finalCps;
            }

            if (newAllTimeBest)
            {
                _bestLabel.ForeColor = _theme.Accent;
                _bestLabel.Text = $"\u2605 New best!  {AllTimeBest:0.0} CPS";
                _clickArea.Text = "New best \u2014 retry";
            }
            else
            {
                _bestLabel.ForeColor = _theme.TextMuted;
                _bestLabel.Text = AllTimeBest > 0
                    ? $"Best: {_best:0.0}   \u00b7   All-time: {AllTimeBest:0.0}"
                    : $"Best CPS: {_best:0.0}";
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
