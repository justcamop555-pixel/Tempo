using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A small clicks-per-second tester. The user clicks the big button as fast
    /// as they can within a fixed window; the form reports total clicks and CPS,
    /// and beside it the details of the run: clicks in each second, the best
    /// second, the rhythm, the gaps, whether the speed held, and this session's tries.
    /// </summary>
    public sealed class CpsTestForm : Form
    {
        // The test on the left, in the 364 px it always had; the details pane beside it.
        private const int LeftWidth = 364;
        private const int PaneLeft = 376;
        private const int PaneWidth = 300;

        /// <summary>
        /// After the time runs out, clicks are ignored this long. Someone clicking flat out is
        /// still clicking when it ends, and the next click started a NEW test at once — wiping
        /// the result they had just earned before they could read it.
        /// </summary>
        internal const int CooldownMs = 1000;

        private readonly Theme _theme;
        private readonly Button _clickArea;
        private readonly Label _cpsLabel;
        private readonly Label _countLabel;
        private readonly Label _timeLabel;
        private readonly Label _bestLabel;
        private ThemedProgressBar _timeBar;
        private Button[] _durationButtons;
        private MouseButtons _testButton = MouseButtons.Left;
        private Button[] _mbButtons;
        private Label _titleLabel;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly System.Windows.Forms.Timer _cooldownTimer;

        // The details pane.
        private readonly MiniBarChart _chart;
        private readonly Label[] _statNames = new Label[7];
        private readonly Label[] _statValues = new Label[7];
        private readonly Label _historyTitle;
        private readonly Label _historyLabel;
        private readonly ToolTip _tips = new ToolTip();

        // Every counted hit, in ms from the first one. A Stopwatch, not the wall clock: the
        // gaps are tens of milliseconds, and a clock adjusted mid-test moved them all.
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly List<double> _hits = new List<double>();

        private bool _running;
        private bool _cooldown;
        private int _count;
        private int _testSeconds = 10;
        private bool _keyboardMode;   // test spacebar presses instead of mouse clicks
        private bool _keyHeld;        // ignore OS auto-repeat while a key is held
        private Stats _lastStats;

        private readonly Dictionary<string, double> _bests = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Attempt> _attempts = new List<Attempt>();

        // Tempo's own clicker on the pad: how many of this run's hits it made (they carry its tag),
        // and the rate it was set to, read from the running engine.
        private readonly Func<double> _clickerRate;
        private int _tempoHits;
        private double _clickerSetCps;

        private sealed class Attempt
        {
            public double Cps;
            public int Seconds;
            public string Input;
            public bool NewBest;
            public bool ByClicker;
        }

        /// <summary>All-time best average CPS (persisted by the caller across sessions).</summary>
        public double AllTimeBest { get; private set; }

        /// <summary>The best for each input and length (<see cref="SetupKey"/>), with this session's records in it.</summary>
        public IReadOnlyDictionary<string, double> Bests => _bests;

        /// <summary>True when a test set a new best for its input and length.</summary>
        public bool BestsChanged { get; private set; }

        public CpsTestForm(Theme theme, double allTimeBest) : this(theme, allTimeBest, null)
        {
        }

        public CpsTestForm(Theme theme, double allTimeBest, IDictionary<string, double> bests) : this(theme, allTimeBest, bests, null)
        {
        }

        /// <param name="clickerRate">The clicks a second Tempo's clicker is set to while it runs (0 when it doesn't),
        /// so a run it makes on the pad can be shown against what it was asked for.</param>
        public CpsTestForm(Theme theme, double allTimeBest, IDictionary<string, double> bests, Func<double> clickerRate)
        {
            AllTimeBest = allTimeBest;
            _clickerRate = clickerRate;
            if (bests != null)
            {
                foreach (var kv in bests)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value > 0) { _bests[kv.Key] = kv.Value; }
                }
            }
            // Built in 100% units; scaled with the screen at the end (UiFactory.ScaleToScreen).
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            Text = Utils.Localization.T("CPS Test");
            ClientSize = new Size(PaneLeft + PaneWidth + 16, 456);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = _theme.Background;
            // Title bar in the theme too, so the dialog matches the app.
            ThemeManager.ApplyWindowChrome(this, _theme);
            ForeColor = _theme.Text;
            Font = UiFactory.BodyFont;
            KeyPreview = true; // Space presses for keyboard-mode testing, Esc to cancel

            var title = UiFactory.Label("Click as fast as you can!", 0, 16, FontStyle.Bold, 13f);
            title.Width = 360;
            title.TextAlign = ContentAlignment.MiddleCenter;
            title.AutoSize = false;
            title.Height = 26;
            _titleLabel = title;

            _timeLabel = UiFactory.Label("", 0, 50, FontStyle.Regular, 10f);
            _timeLabel.Text = Utils.Localization.F("Time: {0:0.0} s", (double)_testSeconds);
            _timeLabel.Width = 360;
            _timeLabel.AutoSize = false;
            _timeLabel.TextAlign = ContentAlignment.MiddleCenter;

            _clickArea = UiFactory.PrimaryButton("CLICK!", 60, 90, 260, 120, _theme);
            // Every caption the pad can show is measured: "Press SPACE!" in keyboard mode, the retry
            // line, "Time's up!" while clicks are ignored after the end. The pad draws ONE line and ends
            // a longer one with "…", and two retry captions did that unseen, because only the first two
            // captions were ever measured: "Press SPACE to retry" (up to 364 px of the pad's 260) and
            // "New best — retry" (up to 304). Keyboard mode's retry is "Press SPACE!" again, and a new best
            // is announced by the ★ line under the result, so neither is shown any more.
            LayoutFitter.DeclareCaptions(_clickArea, Utils.Localization.T("CLICK!"), Utils.Localization.T("Press SPACE!"),
                Utils.Localization.T("Time's up!"), Utils.Localization.T("Click to retry"));
            // The biggest size at which every caption fits: French "Appuyez sur ESPACE !" needed
            // 272 px at 18 pt in the 260 px the pad has, and lost its "!" every time keyboard mode was on.
            float clickSize = 12f;
            foreach (float size in new[] { 18f, 16f, 14f, 13f, 12f })
            {
                using (var trial = new Font("Segoe UI", size, FontStyle.Bold))
                {
                    // In 100% units, like the pad's width: the window is scaled after it is built.
                    int widest = 0;
                    foreach (string caption in LayoutFitter.CaptionsOf(_clickArea))
                    {
                        widest = Math.Max(widest, UiFactory.Measure96(caption, trial).Width);
                    }
                    if (widest + 16 <= _clickArea.Width) { clickSize = size; break; }
                }
            }
            _clickArea.Font = new Font("Segoe UI", clickSize, FontStyle.Bold);
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

            // These read-outs are built from the SAME format strings the live updates use, rather
            // than an English twin of each. Hard-coding "Clicks: 0" here left the dialog opening
            // with English labels among translated ones, which then switched language on the first
            // click — and because the literal sat at a UiFactory call site rather than inside a T(),
            // no translation audit could see it.
            _countLabel = UiFactory.Label("", 0, 224, FontStyle.Bold, 12f);
            _countLabel.Text = Utils.Localization.F("Clicks: {0}", 0);
            _countLabel.Width = 360;
            _countLabel.AutoSize = false;
            _countLabel.TextAlign = ContentAlignment.MiddleCenter;

            _cpsLabel = UiFactory.Label("", 0, 252, FontStyle.Bold, 16f);
            _cpsLabel.Text = Utils.Localization.F("CPS: {0:0.0}", 0.0);
            _cpsLabel.Width = 360;
            _cpsLabel.AutoSize = false;
            // A whole 16 pt line (30 px): switching AutoSize off left it the default 23, which
            // clipped the line top and bottom — a French "0,0" lost the tail of its comma.
            // (In 100% units: the window is scaled after it is built — UiFactory.Measure96.)
            _cpsLabel.Height = UiFactory.Measure96("Ag", _cpsLabel.Font, 0, TextFormatFlags.Default).Height;
            _cpsLabel.TextAlign = ContentAlignment.MiddleCenter;
            _cpsLabel.ForeColor = _theme.AccentText;

            // The best for THIS input and length, and the all-time best beside it. Two lines of room:
            // "Meilleur pour Gauche, 10 s : 9,8  ·  Record absolu : 11,2" is most of the column.
            _bestLabel = UiFactory.Label("", 12, 290, FontStyle.Regular, 9.5f);
            _bestLabel.Width = 340;
            _bestLabel.Height = 38;
            _bestLabel.AutoSize = false;
            _bestLabel.TextAlign = ContentAlignment.TopCenter;
            _bestLabel.ForeColor = _theme.TextMuted;

            Controls.AddRange(new Control[] { title, _timeLabel, _clickArea, _countLabel, _cpsLabel, _bestLabel });

            // Test-duration selector (disabled while a test is running), with the label vertically
            // centred against its button row.
            var durLabel = UiFactory.Label("Test length:", 60, 346, FontStyle.Regular, 9f);
            durLabel.AutoSize = true;
            Controls.Add(durLabel);

            int[] options = { 5, 10, 15, 30 };
            _durationButtons = new Button[options.Length];
            int bx = 150;
            for (int i = 0; i < options.Length; i++)
            {
                int secs = options[i];
                var b = UiFactory.Button(secs + "s", bx, 340, 44, 28);
                b.Tag = secs;
                b.Click += OnDurationClicked;
                _durationButtons[i] = b;
                Controls.Add(b);
                bx += 50;
            }
            HighlightDurationButtons();

            // Input selector: test Left/Middle/Right mouse clicks, or spacebar presses.
            var mbLabel = UiFactory.Label("Input:", 60, 386, FontStyle.Regular, 9f);
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
                var b = UiFactory.Button(mbNames[i], mx, 380, 58, 28);
                b.Tag = mbTags[i];
                b.Click += OnMouseButtonClicked;
                _mbButtons[i] = b;
                Controls.Add(b);
                mx += 62;
            }
            HighlightMouseButtons();
            UpdateTitleForButton(title);

            // Both rows were laid out at fixed x: the label at 60, the buttons from 150 and 104.
            // "Duración de la prueba:" ran 36 px into the "5s" button, "Entrada:" / "Eingang:" /
            // "Ingresso:" under the first input button, and 58 px buttons cut "Izquierdo",
            // "Esquerdo" — and "⌨ Space" even in English. Each row is now sized from its own
            // words and centred as a group, which is also how the rest of this column sits.
            LayOutRow(durLabel, _durationButtons, 40, 6);
            LayOutRow(mbLabel, _mbButtons, 52, 4);

            // ── The details pane ─────────────────────────────────────────────────────────────
            _chart = new MiniBarChart
            {
                Left = PaneLeft,
                Top = 16,
                Width = PaneWidth,
                Height = 150,
                CardColor = _theme.Surface,
                BarColor = _theme.Accent,
                TextColor = _theme.Text,
                MutedColor = _theme.TextMuted
            };
            Controls.Add(_chart);

            // One row per figure: its name on the left, the value right-aligned. The names column is
            // as wide as its longest translation, so no language's name runs into its value.
            string[] names = { "Speed", "Best second", "Consistency", "Fastest gap", "Average gap", "Slowest gap", "Speed held" };
            Font nameFont = UiFactory.FontFor(9f, FontStyle.Regular);
            Font valueFont = UiFactory.FontFor(9.5f, FontStyle.Bold);
            int nameWidth = 0;
            foreach (string n in names)
            {
                nameWidth = Math.Max(nameWidth, UiFactory.Measure96(Utils.Localization.T(n), nameFont, 0, TextFormatFlags.Default).Width + 10);
            }
            nameWidth = Math.Min(nameWidth, PaneWidth - 120);
            for (int i = 0; i < names.Length; i++)
            {
                int y = 178 + i * 22;
                var name = UiFactory.Label(names[i], PaneLeft + 6, y, FontStyle.Regular, 9f);
                name.AutoSize = false;
                name.Width = nameWidth;
                name.Height = 20;
                name.ForeColor = _theme.TextMuted;
                var value = UiFactory.Label("", PaneLeft + 6 + nameWidth, y - 1, FontStyle.Bold, 9.5f);
                value.Font = valueFont;
                value.AutoSize = false;
                value.Width = PaneWidth - 12 - nameWidth;
                value.Height = 20;
                value.TextAlign = ContentAlignment.TopRight;
                value.ForeColor = _theme.Text;
                value.Text = "–";
                _statNames[i] = name;
                _statValues[i] = value;
                Controls.Add(name);
                Controls.Add(value);
            }
            // What the less obvious figures mean.
            TipStyle.Set(_tips, _statNames[1], Utils.Localization.T("The most clicks in any one second of the test, wherever that second started."));
            TipStyle.Set(_tips, _statNames[2], Utils.Localization.T("How even the gaps between your clicks were. 100% is a metronome; the lower it is, the more your rhythm wandered."));
            TipStyle.Set(_tips, _statNames[6], Utils.Localization.T("Your speed in the second half of the test against the first half. Under 100% you slowed down; over 100% you sped up."));

            _historyTitle = UiFactory.Label("This session", PaneLeft + 6, 340, FontStyle.Bold, 9f);
            Controls.Add(_historyTitle);
            _historyLabel = UiFactory.Label("", PaneLeft + 6, 360, FontStyle.Regular, 9f);
            _historyLabel.AutoSize = false;
            _historyLabel.Width = PaneWidth - 12;
            _historyLabel.Height = 88;
            _historyLabel.ForeColor = _theme.TextMuted;
            Controls.Add(_historyLabel);

            ShowDetails(null);
            RefreshHistory();
            RefreshBestLine();

            _timer = new System.Windows.Forms.Timer { Interval = 100 };
            _timer.Tick += OnTick;
            _cooldownTimer = new System.Windows.Forms.Timer { Interval = CooldownMs };
            _cooldownTimer.Tick += (s, e) => EndCooldown();
            UiFactory.ScaleToScreen(this);
        }

        /// <summary>
        /// Lays out "label  [b1] [b2] …" as one row: every button as wide as its own caption
        /// needs (never below <paramref name="minWidth"/>), the whole group centred in the
        /// test's column. If it still does not fit, the buttons drop to a smaller font first — the
        /// same last resort the main window's layout pass uses — rather than overlapping.
        /// </summary>
        private void LayOutRow(Label label, Button[] buttons, int minWidth, int gap)
        {
            if (label == null || buttons == null || buttons.Length == 0) { return; }
            int avail = LeftWidth - 16;

            // In 100% units, like the layout here: this runs in the constructor, before the window is
            // scaled, and measured in screen pixels a row was 1.5× too wide on a 150% laptop — its
            // buttons dropped to the small font for nothing and the row ran off the window once scaled.
            int Measure()
            {
                int w = UiFactory.Measure96(label.Text, label.Font, 0, TextFormatFlags.Default).Width + 8 + gap * (buttons.Length - 1);
                foreach (Button b in buttons)
                {
                    b.Width = Math.Max(minWidth, UiFactory.Measure96(b.Text, b.Font, 0, TextFormatFlags.Default).Width + 16);
                    w += b.Width;
                }
                return w;
            }

            int total = Measure();
            if (total > avail)
            {
                foreach (Button b in buttons) { b.Font = new Font(b.Font.FontFamily, 8.25f, b.Font.Style); }
                total = Measure();
            }

            int x = Math.Max(8, (LeftWidth - total) / 2);
            label.Left = x;
            // Past the label's 100% width, not its Right: an AutoSize label is already as wide as its
            // screen-pixel text, and scaling the buttons' x with the window then counted that twice.
            x = label.Left + UiFactory.Measure96(label.Text, label.Font, 0, TextFormatFlags.Default).Width + 8;
            foreach (Button b in buttons)
            {
                b.Left = x;
                x += b.Width + gap;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Match the main window: dark title bar on dark themes.
            NativeChrome.SetTitleBarDark(Handle, _theme.Background.GetBrightness() < 0.5f);
            // Tempo's clicker may click the pad (and its key-presser press Space here) while this is open:
            // the guard that keeps a run off Tempo's own windows dropped all of it, and the test counted
            // nothing. The rest of Tempo — this window's other buttons included — stays guarded.
            SelfClickGuard.WelcomeOwnInput(_clickArea, this);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            SelfClickGuard.ForgetOwnInput(this);
            base.OnHandleDestroyed(e);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetMessageExtraInfo();

        /// <summary>
        /// The input being handled right now was sent by Tempo itself (its clicker or key-presser): it
        /// carries Tempo's tag. A second mouse's relayed press carries another tag and counts as yours.
        /// </summary>
        private static bool IsTempoInput()
        {
            try { return GetMessageExtraInfo() == Engine.InputSimulator.ExtraInfo; }
            catch { return false; }
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
                RefreshBestLine();
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
                b.ForeColor = selected ? _theme.OnAccent : _theme.Text;
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

        // ── The figures ──────────────────────────────────────────────────────────────────────

        /// <summary>Everything worked out from one run's hits.</summary>
        internal sealed class Stats
        {
            public int Seconds;
            public int Hits;
            public double Cps;
            /// <summary>Hits in each second of the test, the first second first.</summary>
            public int[] PerSecond = new int[0];
            /// <summary>The most hits in any one-second stretch [t, t + 1 s).</summary>
            public int BestSecond;
            public bool HasGaps;
            public double AverageGapMs, FastestGapMs, SlowestGapMs;
            public bool HasConsistency;
            /// <summary>0–100: how even the gaps were (100 = perfectly even). From their coefficient of variation.</summary>
            public double ConsistencyPct;
            public bool HasHeld;
            /// <summary>Hits in the second half against the first half, in percent: under 100 slowed down.</summary>
            public double HeldPct;
            /// <summary>The run counted spacebar presses (set by the window, not by <see cref="Compute"/>).</summary>
            public bool KeyboardRun;
            /// <summary>Tempo's own clicker made most of the hits (set by the window): not anyone's personal best.</summary>
            public bool ByClicker;
        }

        /// <summary>
        /// Works out a run's figures from its hits (ms from the first hit) over a test of
        /// <paramref name="seconds"/>. Hits at or past the end are not part of it. Pure: the tests
        /// feed it timelines.
        /// </summary>
        internal static Stats Compute(IList<double> hitMs, int seconds)
        {
            var s = new Stats { Seconds = Math.Max(1, seconds) };
            double endMs = s.Seconds * 1000.0;
            List<double> hits = (hitMs ?? new double[0]).Where(t => t >= 0 && t < endMs).OrderBy(t => t).ToList();
            s.Hits = hits.Count;
            s.Cps = hits.Count / (double)s.Seconds;

            s.PerSecond = new int[s.Seconds];
            foreach (double t in hits)
            {
                int i = (int)(t / 1000.0);
                if (i >= 0 && i < s.PerSecond.Length) { s.PerSecond[i]++; }
            }

            // The best one-second stretch, wherever it starts: a window sliding over the hits.
            int start = 0;
            for (int end = 0; end < hits.Count; end++)
            {
                while (hits[end] - hits[start] >= 1000.0) { start++; }
                s.BestSecond = Math.Max(s.BestSecond, end - start + 1);
            }

            if (hits.Count >= 2)
            {
                var gaps = new List<double>(hits.Count - 1);
                for (int i = 1; i < hits.Count; i++) { gaps.Add(hits[i] - hits[i - 1]); }
                s.HasGaps = true;
                s.AverageGapMs = gaps.Average();
                s.FastestGapMs = gaps.Min();
                s.SlowestGapMs = gaps.Max();
                if (gaps.Count >= 2 && s.AverageGapMs > 0.0001)
                {
                    double mean = s.AverageGapMs;
                    double sd = Math.Sqrt(gaps.Sum(g => (g - mean) * (g - mean)) / gaps.Count);
                    // Coefficient of variation -> consistency. A CV of 0 is perfectly even
                    // (100%); a CV of 1 or more reads as 0%.
                    s.HasConsistency = true;
                    s.ConsistencyPct = Math.Max(0, Math.Min(100, (1.0 - sd / mean) * 100.0));
                }
            }

            double half = endMs / 2;
            int first = hits.Count(t => t < half);
            int second = hits.Count - first;
            if (first > 0)
            {
                s.HasHeld = true;
                s.HeldPct = second * 100.0 / first;
            }
            return s;
        }

        // ── Input ───────────────────────────────────────────────────────────────────────────

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
            RegisterHit(IsTempoInput());
        }

        /// <summary>Registers one counted input (a click or a key press), starting the
        /// test on the first one. <paramref name="byTempo"/>: Tempo's own clicker made it.</summary>
        internal void RegisterHit(bool byTempo = false)
        {
            if (_cooldown)
            {
                return;   // the test just ended: this is the hand still going, not a new test
            }
            if (!_running)
            {
                StartTest();
            }
            else if (_clock.Elapsed.TotalMilliseconds >= _testSeconds * 1000.0)
            {
                // Time is up but the 100 ms tick has not noticed yet. This click came too late to
                // count: it used to, adding up to a click per tenth of a second to the result.
                FinishTest();
                return;
            }
            _count++;
            _hits.Add(_clock.Elapsed.TotalMilliseconds);
            if (byTempo)
            {
                _tempoHits++;
                double set = 0;
                try { set = _clickerRate?.Invoke() ?? 0; } catch { }
                if (set > 0) { _clickerSetCps = set; }
            }
            // Two whole sentences rather than a spliced noun: languages that inflect the
            // count word cannot be served by swapping one token inside a fixed frame.
            _countLabel.Text = _keyboardMode
                ? Utils.Localization.F("Presses: {0}", _count)
                : Utils.Localization.F("Clicks: {0}", _count);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                // Esc stops a test that's going wrong without a result; with none going, it closes.
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (_running) { CancelTest(); }
                else { Close(); }
                return;
            }
            if (_keyboardMode && e.KeyCode == Keys.Space)
            {
                // Don't let Space also "click" the focused button; count one press per
                // physical keydown, ignoring the OS auto-repeat while it's held.
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (!_keyHeld)
                {
                    _keyHeld = true;
                    RegisterHit(IsTempoInput());
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
                if (_clickArea != null && !_cooldown) _clickArea.Text = Utils.Localization.T(_keyboardMode ? "Press SPACE!" : "CLICK!");
                RefreshBestLine();
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
                // Selected fills the button with Accent, and white on that is below 4.5:1
                // in 36 of the 38 palettes — read the label off its own fill instead.
                b.ForeColor = selected ? Theme.ReadableOn(_theme.Accent) : _theme.Text;
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
            }
            else
            {
                // One whole sentence per button rather than splicing the button name into a
                // frame — "left/right/middle" inflects with the noun in several languages.
                title.Text = _testButton == MouseButtons.Middle
                    ? Utils.Localization.T("Click as fast as you can!  (middle button)")
                    : _testButton == MouseButtons.Right
                        ? Utils.Localization.T("Click as fast as you can!  (right button)")
                        : Utils.Localization.T("Click as fast as you can!  (left button)");
            }
            FitTitle(title);
        }

        /// <summary>
        /// One line, at the largest size that fits it. The title is one line tall, and in
        /// Spanish, French, Italian and Portuguese "Click as fast as you can!  (left button)"
        /// is wider than the dialog at 13 pt: the second line was simply not drawn, taking
        /// the button's name with it. The font steps down, to 9 pt at the least, until it fits.
        /// </summary>
        private static void FitTitle(Label title)
        {
            // Measured the way the label DRAWS it (word-wrapped, with its padding): "one line"
            // means the wrapped text is no taller than a single line. A single-line width with
            // no padding looked like a fit a size too early, and Portuguese still wrapped.
            const TextFormatFlags drawn = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
            float size = 13f;
            Font font = UiFactory.FontFor(size, FontStyle.Bold);
            var unbounded = new Size(int.MaxValue, int.MaxValue);
            // The width it will really have on screen. Run from the constructor, before the window is
            // scaled, the label is still in 100% units while the text is measured in the screen's
            // pixels: on a 150% laptop the title was wrapped at two-thirds of its width and given a
            // smaller font than it needed. (Later, choosing another button, both are screen pixels.)
            // Asked of the window, not of its handle: it is scaled at the end of its constructor.
            Form owner = title.FindForm();
            float k = UiFactory.IsScaledToScreen(owner) ? 1f : UiFactory.BuildScale;
            int width = Math.Max(1, (int)Math.Round(title.Width * k));
            while (size > 9f)
            {
                // One line's height with no width to wrap in — Size.Empty with WordBreak is a
                // width of NOTHING, which breaks even "Ag" in two and makes everything "fit".
                int line = TextRenderer.MeasureText("Ag", font, unbounded, drawn).Height;
                int need = TextRenderer.MeasureText(title.Text, font, new Size(width, int.MaxValue), drawn).Height;
                if (need <= line) { break; }
                size -= 0.5f;
                font = UiFactory.FontFor(size, FontStyle.Bold);
            }
            title.Font = font;
        }

        // ── The run ─────────────────────────────────────────────────────────────────────────

        /// <summary>"left-10", "space-5": which best a result counts towards.</summary>
        internal string SetupKey => InputKey + "-" + _testSeconds;

        private string InputKey => _keyboardMode ? "space"
            : _testButton == MouseButtons.Right ? "right"
            : _testButton == MouseButtons.Middle ? "middle" : "left";

        /// <summary>The input's name as its button shows it, translated.</summary>
        private string InputName => Utils.Localization.T(_keyboardMode ? "⌨ Space"
            : _testButton == MouseButtons.Right ? "Right"
            : _testButton == MouseButtons.Middle ? "Middle" : "Left");

        private void StartTest()
        {
            _running = true;
            _count = 0;
            _hits.Clear();
            _tempoHits = 0;
            _clickerSetCps = 0;
            // _keyHeld is NOT cleared here: the press that starts a test is the one holding it, and
            // clearing it let that same key's auto-repeat count as a second press. Key up clears it.
            _clock.Restart();
            _clickArea.Text = Utils.Localization.T(_keyboardMode ? "Press SPACE!" : "CLICK!");
            if (_cpsLabel != null)
            {
                _cpsLabel.ForeColor = _theme.AccentText;
                _cpsLabel.Font = UiFactory.FontFor(16f, FontStyle.Bold);   // FitCpsLabel may have shrunk it
                _cpsLabel.Text = Utils.Localization.F("CPS: {0:0.0}", 0.0);
            }
            if (_timeBar != null) _timeBar.Value = 0;
            RefreshBestLine();
            // The chart fills in second by second as the test runs.
            _chart.Title = Utils.Localization.T(_keyboardMode ? "Presses each second" : "Clicks each second");
            _chart.ValueFormat = _keyboardMode ? "{0} presses" : "{0} clicks";
            _chart.ReferenceValue = double.NaN;
            _chart.SetValues(new long[_testSeconds], SecondLabels(_testSeconds));
            foreach (Label v in _statValues) { v.Text = "–"; v.ForeColor = _theme.Text; }
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
            if (!_running) { return; }
            double elapsed = _clock.Elapsed.TotalSeconds;
            double remaining = _testSeconds - elapsed;

            if (remaining <= 0)
            {
                FinishTest();
                return;
            }

            _timeLabel.Text = Utils.Localization.F("Time: {0:0.0} s", remaining);
            if (_timeBar != null)
            {
                int prog = _testSeconds > 0 ? (int)Math.Round(elapsed / _testSeconds * 100) : 0;
                prog = Math.Max(0, Math.Min(100, prog));
                if (_timeBar.Value != prog) _timeBar.Value = prog;
            }

            // Over at least half a second: one click 50 ms in read as 20 CPS.
            _cpsLabel.Text = Utils.Localization.F("CPS: {0:0.0}", _count / Math.Max(0.5, elapsed));

            var buckets = new long[_testSeconds];
            foreach (double t in _hits)
            {
                int i = (int)(t / 1000.0);
                if (i >= 0 && i < buckets.Length) { buckets[i]++; }
            }
            _chart.SetValues(buckets, SecondLabels(_testSeconds));
        }

        /// <summary>The seconds under the chart: each one for a short test, every fifth for a long one.</summary>
        private static string[] SecondLabels(int seconds)
        {
            var labels = new string[seconds];
            int step = seconds <= 10 ? 1 : 5;
            for (int i = 0; i < seconds; i++)
            {
                labels[i] = (i + 1) % step == 0 || i == 0 ? (i + 1).ToString() : "";
            }
            return labels;
        }

        private void FinishTest()
        {
            if (!_running) { return; }
            _timer.Stop();
            _clock.Stop();
            _running = false;
            if (_timeBar != null) _timeBar.Value = 100;
            _timeLabel.Text = Utils.Localization.F("Time: {0:0.0} s", 0.0);
            if (_durationButtons != null)
            {
                foreach (Button b in _durationButtons) b.Enabled = true;
            }
            if (_mbButtons != null)
            {
                foreach (Button b in _mbButtons) b.Enabled = true;
            }

            Stats stats = Compute(_hits, _testSeconds);
            stats.KeyboardRun = _keyboardMode;
            // Tempo's own clicker made most of these hits: a measurement of the clicker, not a score.
            stats.ByClicker = stats.Hits > 0 && _tempoHits * 2 > stats.Hits;
            _lastStats = stats;
            double finalCps = stats.Cps;
            _countLabel.Text = _keyboardMode
                ? Utils.Localization.F("Presses: {0}", stats.Hits)
                : Utils.Localization.F("Clicks: {0}", stats.Hits);
            _cpsLabel.Text = Utils.Localization.F("{0:0.0} CPS  ·  {1}",
                finalCps, Utils.Localization.T(stats.ByClicker ? "Tempo's clicker" : RankFor(finalCps)));
            _cpsLabel.ForeColor = stats.ByClicker ? _theme.AccentText : RankColor(finalCps);
            FitCpsLabel();

            if (stats.ByClicker)
            {
                // Never anyone's best: a clicker at 100 CPS would bury every real result. Shown
                // against the rate it was set to, which is what someone measuring it wants to know.
                _attempts.Add(new Attempt { Cps = finalCps, Seconds = _testSeconds, Input = InputName, ByClicker = true });
                _bestLabel.ForeColor = _theme.AccentText;
                _bestLabel.Text = _clickerSetCps > 0
                    ? Utils.Localization.F("Tempo's clicker: {0:0.0} CPS, set to {1:0.0}", finalCps, _clickerSetCps)
                    : Utils.Localization.F("Tempo's clicker: {0:0.0} CPS", finalCps);
                ShowDetails(stats);
                RefreshHistory();
                StartCooldown();
                return;
            }

            // The best for this input and length, and the best of all.
            bool newBest = false, newAllTime = false;
            if (finalCps > 0)
            {
                _bests.TryGetValue(SetupKey, out double had);
                if (finalCps > had)
                {
                    _bests[SetupKey] = finalCps;
                    BestsChanged = true;
                    newBest = true;
                }
                if (finalCps > AllTimeBest)
                {
                    AllTimeBest = finalCps;
                    newAllTime = true;
                }
                _attempts.Add(new Attempt { Cps = finalCps, Seconds = _testSeconds, Input = InputName, NewBest = newBest });
            }
            if (newAllTime)
            {
                _bestLabel.ForeColor = _theme.AccentText;
                _bestLabel.Text = Utils.Localization.F("★ New best!  {0:0.0} CPS", AllTimeBest);
            }
            else if (newBest)
            {
                _bestLabel.ForeColor = _theme.AccentText;
                _bestLabel.Text = Utils.Localization.F("★ New best for {0}, {1} s: {2:0.0} CPS", InputName, _testSeconds, finalCps);
            }
            else
            {
                RefreshBestLine();
            }

            ShowDetails(stats);
            RefreshHistory();
            StartCooldown();
        }

        /// <summary>
        /// The result line at the biggest size, 16 pt down to 11, that keeps it on its one line: "84.3 CPS ·
        /// Clic automático de Tempo" is wider than the column at 16 pt. (Screen pixels on both sides: this runs
        /// after the window is scaled.)
        /// </summary>
        private void FitCpsLabel()
        {
            foreach (float size in new[] { 16f, 14f, 12f, 11f })
            {
                Font f = UiFactory.FontFor(size, FontStyle.Bold);
                if (size <= 11f || TextRenderer.MeasureText(_cpsLabel.Text, f).Width <= _cpsLabel.Width - 4)
                {
                    _cpsLabel.Font = f;
                    return;
                }
            }
        }

        /// <summary>A moment in which clicks are ignored, so the hand (or clicker) still going doesn't start another test.</summary>
        private void StartCooldown()
        {
            _cooldown = true;
            _clickArea.Text = Utils.Localization.T("Time's up!");
            _cooldownTimer.Stop();
            _cooldownTimer.Start();
        }

        private void EndCooldown()
        {
            _cooldownTimer.Stop();
            _cooldown = false;
            _clickArea.Text = Utils.Localization.T(_keyboardMode ? "Press SPACE!" : "Click to retry");
        }

        /// <summary>Esc during a test: stops it with no result, and puts the last finished test's details back.</summary>
        private void CancelTest()
        {
            _timer.Stop();
            _clock.Stop();
            _running = false;
            _count = 0;
            _hits.Clear();
            if (_timeBar != null) _timeBar.Value = 0;
            _timeLabel.Text = Utils.Localization.F("Time: {0:0.0} s", (double)_testSeconds);
            _countLabel.Text = _keyboardMode ? Utils.Localization.F("Presses: {0}", 0) : Utils.Localization.F("Clicks: {0}", 0);
            _cpsLabel.ForeColor = _theme.AccentText;
            _cpsLabel.Font = UiFactory.FontFor(16f, FontStyle.Bold);
            _cpsLabel.Text = Utils.Localization.F("CPS: {0:0.0}", 0.0);
            _clickArea.Text = Utils.Localization.T(_keyboardMode ? "Press SPACE!" : "CLICK!");
            if (_durationButtons != null) { foreach (Button b in _durationButtons) b.Enabled = true; }
            if (_mbButtons != null) { foreach (Button b in _mbButtons) b.Enabled = true; }
            ShowDetails(_lastStats);
            RefreshBestLine();
        }

        /// <summary>The details pane for a finished run; null shows it empty.</summary>
        private void ShowDetails(Stats s)
        {
            // Worded for the run shown, even after the input has been switched for the next one.
            bool keys = s != null ? s.KeyboardRun : _keyboardMode;
            _chart.Title = Utils.Localization.T(keys ? "Presses each second" : "Clicks each second");
            _chart.ValueFormat = keys ? "{0} presses" : "{0} clicks";
            if (s == null)
            {
                _chart.ReferenceValue = double.NaN;
                _chart.SetValues(null);
                foreach (Label v in _statValues) { if (v != null) { v.Text = "–"; } }
                return;
            }
            _chart.ReferenceValue = s.Cps;
            _chart.ReferenceLabel = Utils.Localization.F("avg {0:0.0}", s.Cps);
            _chart.SetValues(s.PerSecond.Select(v => (long)v).ToList(), SecondLabels(s.Seconds));

            const string none = "–";
            _statValues[0].Text = s.Cps.ToString("0.0") + " CPS";
            _statValues[1].Text = s.Hits > 0
                ? Utils.Localization.F(keys ? "{0} presses" : "{0} clicks", s.BestSecond)
                : none;
            // Through F: French and German put a space before the % sign.
            _statValues[2].Text = s.HasConsistency ? Utils.Localization.F("{0:0}%", s.ConsistencyPct) : none;
            _statValues[3].Text = s.HasGaps ? s.FastestGapMs.ToString("0") + " ms" : none;
            _statValues[4].Text = s.HasGaps ? s.AverageGapMs.ToString("0") + " ms" : none;
            _statValues[5].Text = s.HasGaps ? s.SlowestGapMs.ToString("0") + " ms" : none;
            _statValues[6].Text = s.HasHeld ? Utils.Localization.F("{0:0}%", s.HeldPct) : none;
            _statValues[6].ForeColor = !s.HasHeld ? _theme.Text
                : s.HeldPct >= 95 ? _theme.SuccessText
                : s.HeldPct >= 80 ? _theme.Text
                : _theme.WarningText;
        }

        /// <summary>This session's tries, newest first; a star marks a new best, and the clicker's runs say so.</summary>
        private void RefreshHistory()
        {
            if (_attempts.Count == 0)
            {
                // Says how to measure Tempo's own clicker, too: nothing else in the window does.
                _historyLabel.Text = Utils.Localization.T("Your tries this session will be listed here. To measure Tempo's clicker, start it with the pointer on the pad.");
                return;
            }
            var lines = new List<string>();
            for (int i = _attempts.Count - 1; i >= 0 && lines.Count < 5; i--)
            {
                Attempt a = _attempts[i];
                lines.Add(a.ByClicker
                    ? Utils.Localization.F("{0:0.0} CPS  ·  {1} s  ·  Tempo's clicker", a.Cps, a.Seconds)
                    : Utils.Localization.F("{0:0.0} CPS  ·  {1} s  ·  {2}", a.Cps, a.Seconds, a.Input) + (a.NewBest ? "  ★" : ""));
            }
            _historyLabel.Text = string.Join("\n", lines);
        }

        /// <summary>The best for the chosen input and length, and the all-time best beside it.</summary>
        private void RefreshBestLine()
        {
            if (_bestLabel == null) { return; }
            _bestLabel.ForeColor = _theme.TextMuted;
            _bests.TryGetValue(SetupKey, out double best);
            string name = InputName;
            if (best > 0 && AllTimeBest > best + 0.05)
            {
                _bestLabel.Text = Utils.Localization.F("Best for {0}, {1} s: {2:0.0}  ·  All-time: {3:0.0}", name, _testSeconds, best, AllTimeBest);
            }
            else if (best > 0)
            {
                _bestLabel.Text = Utils.Localization.F("Best for {0}, {1} s: {2:0.0}", name, _testSeconds, best);
            }
            else if (AllTimeBest > 0)
            {
                _bestLabel.Text = Utils.Localization.F("No best for {0}, {1} s yet  ·  All-time: {2:0.0}", name, _testSeconds, AllTimeBest);
            }
            else
            {
                _bestLabel.Text = Utils.Localization.F("No best for {0}, {1} s yet", name, _testSeconds);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            _cooldownTimer.Stop();
            _cooldownTimer.Dispose();
            _tips.Dispose();
            base.OnFormClosed(e);
        }
    }
}
