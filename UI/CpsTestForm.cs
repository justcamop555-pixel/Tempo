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
        private Label _detailLabel;
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
        private bool _keyboardMode;   // test spacebar presses instead of mouse clicks
        private bool _keyHeld;        // ignore OS auto-repeat while a key is held

        /// <summary>All-time best average CPS (persisted by the caller across sessions).</summary>
        public double AllTimeBest { get; private set; }

        public CpsTestForm(Theme theme, double allTimeBest)
        {
            AllTimeBest = allTimeBest;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            Text = Utils.Localization.T("CPS Test");
            ClientSize = new Size(364, 488);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = _theme.Background;
            // Title bar in the theme too, so the dialog matches the app.
            ThemeManager.ApplyWindowChrome(this, _theme);
            ForeColor = _theme.Text;
            Font = UiFactory.BodyFont;
            KeyPreview = true; // so the form sees Space presses for keyboard-mode testing

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

            // These four read-outs are built from the SAME format strings the live
            // updates use, rather than an English twin of each. Hard-coding "Clicks: 0"
            // here left the dialog opening with four English labels among translated
            // ones, which then switched language on the first click — and because the
            // literal sat at a UiFactory call site rather than inside a T(), no
            // translation audit could see it.
            _countLabel = UiFactory.Label(Utils.Localization.F("Clicks: {0}", 0), 0, 224, FontStyle.Bold, 12f);
            _countLabel.Width = 360;
            _countLabel.AutoSize = false;
            _countLabel.TextAlign = ContentAlignment.MiddleCenter;

            _cpsLabel = UiFactory.Label(Utils.Localization.F("CPS: {0:0.0}", 0.0), 0, 252, FontStyle.Bold, 16f);
            _cpsLabel.Width = 360;
            _cpsLabel.AutoSize = false;
            _cpsLabel.TextAlign = ContentAlignment.MiddleCenter;
            _cpsLabel.ForeColor = _theme.Accent;

            _bestLabel = UiFactory.Label(Utils.Localization.F("Best CPS: {0:0.0}", 0.0), 0, 288, FontStyle.Regular, 9.5f);
            _bestLabel.Width = 360;
            _bestLabel.AutoSize = false;
            _bestLabel.TextAlign = ContentAlignment.MiddleCenter;
            _bestLabel.ForeColor = _theme.TextMuted;
            if (AllTimeBest > 0) _bestLabel.Text = Utils.Localization.F("All-time best: {0:0.0}", AllTimeBest);

            _peakLabel = UiFactory.Label(Utils.Localization.F("Peak (1s): {0}", 0), 0, 310, FontStyle.Regular, 9.5f);
            _peakLabel.Width = 360;
            _peakLabel.AutoSize = false;
            _peakLabel.TextAlign = ContentAlignment.MiddleCenter;
            _peakLabel.ForeColor = _theme.TextMuted;

            _attemptsLabel = UiFactory.Label("", 0, 452, FontStyle.Regular, 9f);
            _attemptsLabel.Width = 360;
            _attemptsLabel.AutoSize = false;
            _attemptsLabel.TextAlign = ContentAlignment.MiddleCenter;
            _attemptsLabel.ForeColor = _theme.TextMuted;

            // Extra detail shown after a run: how steady the clicking was and the
            // single fastest gap between two clicks.
            _detailLabel = UiFactory.Label("", 0, 334, FontStyle.Regular, 9.5f);
            _detailLabel.Width = 360;
            _detailLabel.AutoSize = false;
            _detailLabel.TextAlign = ContentAlignment.MiddleCenter;
            _detailLabel.ForeColor = _theme.TextMuted;

            Controls.AddRange(new Control[]
            {
                title, _timeLabel, _clickArea, _countLabel, _cpsLabel, _bestLabel, _peakLabel, _detailLabel, _attemptsLabel
            });

            // Test-duration selector (disabled while a test is running). Sits well below
            // the results detail line so the two never overlap, with the label vertically
            // centred against its button row.
            var durLabel = UiFactory.Label("Test length:", 60, 374, FontStyle.Regular, 9f);
            durLabel.AutoSize = true;
            Controls.Add(durLabel);

            int[] options = { 5, 10, 15, 30 };
            _durationButtons = new Button[options.Length];
            int bx = 150;
            for (int i = 0; i < options.Length; i++)
            {
                int secs = options[i];
                var b = UiFactory.Button(secs + "s", bx, 368, 44, 28);
                b.Tag = secs;
                b.Click += OnDurationClicked;
                _durationButtons[i] = b;
                Controls.Add(b);
                bx += 50;
            }
            HighlightDurationButtons();

            // Input selector: test Left/Middle/Right mouse clicks, or spacebar presses.
            var mbLabel = UiFactory.Label("Input:", 60, 414, FontStyle.Regular, 9f);
            mbLabel.AutoSize = true;
            Controls.Add(mbLabel);

            // Tag is a MouseButtons for the three mouse options; the string "kbd" marks the
            // keyboard (spacebar) option.
            string[] mbNames = { "Left", "Middle", "Right", "⌨ Space" };
            object[] mbTags = { MouseButtons.Left, MouseButtons.Middle, MouseButtons.Right, "kbd" };
            _mbButtons = new Button[mbNames.Length];
            int mx = 104;
            for (int i = 0; i < mbNames.Length; i++)
            {
                var b = UiFactory.Button(mbNames[i], mx, 408, 58, 28);
                b.Tag = mbTags[i];
                b.Click += OnMouseButtonClicked;
                _mbButtons[i] = b;
                Controls.Add(b);
                mx += 62;
            }
            HighlightMouseButtons();
            UpdateTitleForButton(title);

            _timer = new System.Windows.Forms.Timer { Interval = 100 };
            _timer.Tick += OnTick;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Match the main window: dark title bar on dark themes.
            NativeChrome.SetTitleBarDark(Handle, _theme.Background.GetBrightness() < 0.5f);
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
                _timeLabel.Text = Utils.Localization.F("Time: {0:0.0} s", (double)_testSeconds);
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

        /// <summary>
        /// Computes the single fastest gap between two clicks (ms) and a 0-100
        /// "consistency" score - how even the gaps were (100 = perfectly metronomic).
        /// Consistency is derived from the spread of the inter-click gaps relative to
        /// their average, so steadier clicking scores higher regardless of speed.
        /// </summary>
        private void ComputeRhythm(out double minGapMs, out double consistencyPct)
        {
            minGapMs = 0;
            consistencyPct = 0;
            if (_clicks.Count < 3)
            {
                return;
            }

            var gaps = new List<double>(_clicks.Count - 1);
            for (int i = 1; i < _clicks.Count; i++)
            {
                double ms = (_clicks[i] - _clicks[i - 1]).TotalMilliseconds;
                if (ms < 0) ms = 0;
                gaps.Add(ms);
            }

            double sum = 0, min = double.MaxValue;
            foreach (double g in gaps)
            {
                sum += g;
                if (g < min) min = g;
            }
            double mean = sum / gaps.Count;
            minGapMs = (min == double.MaxValue) ? 0 : min;

            if (mean <= 0.0001)
            {
                consistencyPct = 0;
                return;
            }

            double varSum = 0;
            foreach (double g in gaps)
            {
                double d = g - mean;
                varSum += d * d;
            }
            double stdDev = Math.Sqrt(varSum / gaps.Count);

            // Coefficient of variation -> consistency. CV of 0 is perfectly even
            // (100%); a CV of 1 or more reads as 0%.
            double cv = stdDev / mean;
            consistencyPct = Math.Max(0, Math.Min(100, (1.0 - cv) * 100.0));
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
            if (_keyboardMode)
            {
                return; // keyboard mode counts Space presses, not mouse clicks
            }
            // Only count presses of the button being tested.
            if (e.Button != _testButton)
            {
                return;
            }
            RegisterHit();
        }

        /// <summary>Registers one counted input (a click or a key press), starting the
        /// test on the first one.</summary>
        private void RegisterHit()
        {
            if (!_running)
            {
                StartTest();
            }
            _count++;
            _clicks.Add(DateTime.UtcNow);
            // Two whole sentences rather than a spliced noun: languages that inflect the
            // count word cannot be served by swapping one token inside a fixed frame.
            _countLabel.Text = _keyboardMode
                ? Utils.Localization.F("Presses: {0}", _count)
                : Utils.Localization.F("Clicks: {0}", _count);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_keyboardMode && e.KeyCode == Keys.Space)
            {
                // Don't let Space also "click" the focused button; count one press per
                // physical keydown, ignoring the OS auto-repeat while it's held.
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (!_keyHeld)
                {
                    _keyHeld = true;
                    RegisterHit();
                }
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space)
            {
                _keyHeld = false;
            }
        }

        private void OnMouseButtonClicked(object sender, EventArgs e)
        {
            if (_running)
            {
                return; // don't change the tested input mid-test
            }
            if (sender is Button b)
            {
                if (b.Tag is MouseButtons mb)
                {
                    _keyboardMode = false;
                    _testButton = mb;
                }
                else if (b.Tag is string s && s == "kbd")
                {
                    _keyboardMode = true;
                }
                HighlightMouseButtons();
                UpdateTitleForButton(_titleLabel);
                if (_clickArea != null) _clickArea.Text = Utils.Localization.T(_keyboardMode ? "Press SPACE!" : "CLICK!");
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
                bool selected = b.Tag is MouseButtons mb
                    ? (!_keyboardMode && mb == _testButton)
                    : _keyboardMode; // the "kbd" (Space) button
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
            if (_keyboardMode)
            {
                title.Text = Utils.Localization.T("Press SPACE as fast as you can!");
                return;
            }
            // One whole sentence per button rather than splicing the button name into a
            // frame — "left/right/middle" inflects with the noun in several languages.
            title.Text = _testButton == MouseButtons.Middle
                ? Utils.Localization.T("Click as fast as you can!  (middle button)")
                : _testButton == MouseButtons.Right
                    ? Utils.Localization.T("Click as fast as you can!  (right button)")
                    : Utils.Localization.T("Click as fast as you can!  (left button)");
        }

        private void StartTest()
        {
            _running = true;
            _count = 0;
            _peak = 0;
            _clicks.Clear();
            _keyHeld = false;
            _startUtc = DateTime.UtcNow;
            _clickArea.Text = Utils.Localization.T(_keyboardMode ? "Press SPACE!" : "CLICK!");
            if (_bestLabel != null) _bestLabel.ForeColor = _theme.TextMuted;
            if (_cpsLabel != null)
            {
                _cpsLabel.ForeColor = _theme.Accent;
                _cpsLabel.Text = Utils.Localization.F("CPS: {0:0.0}", 0.0);
            }
            if (_timeBar != null) _timeBar.Value = 0;
            if (_detailLabel != null) _detailLabel.Text = "";
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

            _timeLabel.Text = Utils.Localization.F("Time: {0:0.0} s", remaining);
            if (_timeBar != null)
            {
                int prog = _testSeconds > 0
                    ? (int)Math.Round(Math.Min(elapsed, _testSeconds) / _testSeconds * 100)
                    : 0;
                if (prog < 0) prog = 0;
                if (prog > 100) prog = 100;
                if (_timeBar.Value != prog) _timeBar.Value = prog;
            }

            if (elapsed > 0.0001)
            {
                double cps = _count / Math.Min(elapsed, _testSeconds);
                _cpsLabel.Text = Utils.Localization.F("CPS: {0:0.0}", cps);
            }

            int peak = ComputePeakCps();
            if (peak > _peak)
            {
                _peak = peak;
                _peakLabel.Text = Utils.Localization.F("Peak (1s): {0}", peak);
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
            _cpsLabel.Text = Utils.Localization.F("{0:0.0} CPS  \u00b7  {1}",
                finalCps, Utils.Localization.T(RankFor(finalCps)));
            _cpsLabel.ForeColor = RankColor(finalCps);
            _clickArea.Text = Utils.Localization.T(
                _keyboardMode ? "Press SPACE to retry" : "Click to retry");

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
                _attemptsLabel.Text = Utils.Localization.F(
                    "This session: {0} tries  ·  avg {1:0.0}  ·  best {2:0.0}", _attempts.Count, avg, max);
            }

            int peak = ComputePeakCps();
            if (peak > _peak)
            {
                _peak = peak;
            }
            _peakLabel.Text = Utils.Localization.F("Peak (1s): {0}", (int)_peak);

            // Detail metrics: consistency (how even the gaps between clicks were) and
            // the single fastest gap. Needs at least a few clicks to be meaningful.
            if (_clicks.Count >= 3)
            {
                double minGapMs, consistencyPct;
                ComputeRhythm(out minGapMs, out consistencyPct);
                _detailLabel.Text = Utils.Localization.F(
                    "Consistency: {0:0}%  \u00b7  fastest gap: {1:0} ms", consistencyPct, minGapMs);
            }
            else
            {
                _detailLabel.Text = "";
            }

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
                _bestLabel.Text = Utils.Localization.F("\u2605 New best!  {0:0.0} CPS", AllTimeBest);
                _clickArea.Text = Utils.Localization.T("New best \u2014 retry");
            }
            else
            {
                _bestLabel.ForeColor = _theme.TextMuted;
                _bestLabel.Text = AllTimeBest > 0
                    ? Utils.Localization.F("Best: {0:0.0}   \u00b7   All-time: {1:0.0}", _best, AllTimeBest)
                    : Utils.Localization.F("Best CPS: {0:0.0}", _best);
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
