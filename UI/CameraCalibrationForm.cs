using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using AutoClicker.Engine;
using AutoClicker.Native;

namespace AutoClicker.UI
{
    /// <summary>
    /// Measures how far the game's camera turns per unit of raw mouse movement — the
    /// single value the whole camera-relative movement system rests on.
    ///
    /// The measurement has to happen INSIDE the game (only the game knows how far its
    /// camera turned), which creates an awkward problem: the user cannot click a
    /// "Stop" button to finish, because moving the mouse over to the button would add
    /// hundreds of counts of horizontal movement to the very number being measured.
    ///
    /// So counting is gated on a HELD KEY instead. Hold the key, turn the camera one
    /// full circle, release. The keyboard adds no mouse movement, so the count stays
    /// clean, and the user never has to leave the game to do it.
    ///
    /// Then CHECK it, the way sensitivity matchers do: a second key has Tempo turn the
    /// camera exactly one full circle by the measured value (relative mouse movement
    /// reaches a raw-input game count for count). If it does not stop where it started,
    /// holding the first key and turning the rest of the way back gives the exact count:
    /// Tempo's turn plus the correction. A full circle turned by hand is rarely within a few
    /// percent; this lands on it.
    ///
    /// Raw Input is registered with RIDEV_INPUTSINK, so movement keeps arriving while
    /// the game — not this window — has focus.
    /// </summary>
    public sealed class CameraCalibrationForm : Form
    {
        // F10 by default: rarely bound in games, and easy to hold with the left hand
        // while the right hand sweeps the mouse.
        private const int VK_HOLD = 0x79;           // F10
        private const string HoldKeyName = "F10";
        // F11 for the check: one press, no mouse needed. (Roblox's full-screen key: taken while
        // this window is open, and given back when it closes.)
        private const int VK_CHECK = 0x7A;          // F11
        private const string CheckKeyName = "F11";

        /// <summary>How long Tempo's check turn takes: slow enough to watch, quick enough not to wait for.</summary>
        internal const double CheckTurnSeconds = 1.2;

        /// <summary>A hold after a check that turns less than this share of a circle corrects the check.</summary>
        internal const double CorrectionShare = 0.25;

        private readonly RawMouseInput _mouse = new RawMouseInput();
        private readonly LowLevelKeyboardHook _keys = new LowLevelKeyboardHook();
        private readonly System.Windows.Forms.Timer _tick;

        private readonly Label _instructions;
        private readonly Label _liveCount;
        private readonly Label _result;
        private readonly Button _acceptBtn;

        private volatile bool _counting;
        // SIGNED: turning past the start and back again is the same full circle. Adding up the
        // size of every step counted the overshoot twice.
        private long _counts;
        private double _degPerCount;
        private bool _haveResult;
        private readonly double _original;
        private readonly bool _currentIsDefault;

        // The check turn: how many counts Tempo sent last (0 = no check to correct), and the run itself.
        private long _checkCounts;
        private volatile bool _checking;
        private bool _checkKeyDown;
        private ManualResetEventSlim _checkCancel;
        private Thread _checkThread;

        /// <summary>The measured degrees-per-count, valid only when ShowDialog returns OK.</summary>
        public double DegreesPerCount => _degPerCount;

        /// <summary>For the tests: replaces the mouse movement a check turn sends (counts, to the right).</summary>
        internal Action<int> SendTurnForTest;

        /// <summary>For the tests: the window that counts as in front (the check refuses Tempo's own).</summary>
        internal Func<bool> TempoInFrontForTest;

        /// <summary>
        /// Kept so the live status line can colour itself from the palette.
        ///
        /// It used to use four fixed colours — a pale blue, amber, mint and grey chosen
        /// against this dialog's dark default. On a light theme they sit on a near-white
        /// Background at between 1.2:1 and 2.5:1, i.e. the status text this dialog exists
        /// to show is the part you cannot read.
        /// </summary>
        private readonly Theme _theme;

        public CameraCalibrationForm(Theme theme, double current)
        {
            // Built in 100% units and scaled with the screen at the end (UiFactory.ScaleToScreen); its
            // buttons are fitted in 100% units to match (UiFactory.FitToText96).
            theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);
            _theme = theme;
            _degPerCount = current > 0 ? current : 0.06;
            _original = _degPerCount;
            _currentIsDefault = Math.Abs(_degPerCount - 0.06) < 1e-9;

            Text = Utils.Localization.T("Tempo — calibrate camera sensitivity");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 318);
            BackColor = theme.Background;
            // Title bar in the theme too, so the dialog matches the app.
            ThemeManager.ApplyWindowChrome(this, theme);
            ForeColor = theme.Text;
            Font = new Font("Segoe UI", 9f);
            // Stay visible over the game so the live counter can be glanced at, but
            // never steal focus — the game must keep it, or it won't turn the camera.
            TopMost = true;

            _instructions = new Label
            {
                Left = 16, Top = 14, Width = 528, Height = 118,
                ForeColor = theme.Text,
                // One format string rather than eight concatenated fragments. Split up,
                // the pieces could not be translated at all: no language keeps English's
                // word order across "Hold X down" / "Release X", and a translator handed
                // " down.\n" on its own has nothing to work with.
                Text = Utils.Localization.F(
                    "1.  Switch to your game (this window stays on top).\n" +
                    "2.  Hold {0} and turn your camera EXACTLY one full circle (360°), back to where it started, then let go of {0}.\n" +
                    "3.  Check it: press {1} in the game and Tempo turns the camera one full circle by itself. " +
                    "If your game turns the camera only while a mouse button is held, hold it.\n" +
                    "4.  If it doesn't stop exactly where it started, hold {0} and turn the rest of the way back, " +
                    "then let go: Tempo corrects the value.\n\n" +
                    "Counting only runs while {0} is held, so moving the mouse back here afterwards won't spoil the measurement.",
                    HoldKeyName, CheckKeyName)
            };
            Controls.Add(_instructions);

            // As tall as its words, and everything below it moves down by what it grows. A line of text
            // is not 1.5× taller on a 150% screen but 25 px for 100%'s 15: the eight lines needed 200 px
            // of the 177 the 118 px box became, and the last sentence was not drawn. Measured in 100% units,
            // as the box is (UiFactory.Measure96).
            int grow = Math.Max(0, UiFactory.Measure96(_instructions.Text, _instructions.Font, 528, TextFormatFlags.Default).Height + 2 - 118);
            _instructions.Height += grow;
            ClientSize = new Size(560, 318 + grow);

            _liveCount = new Label
            {
                Left = 16, Top = 140 + grow, Width = 528, Height = 34,
                Font = new Font("Consolas", 13f, FontStyle.Bold),
                ForeColor = theme.TextMuted,
                Text = Utils.Localization.F("Hold {0} and turn…", HoldKeyName)
            };
            Controls.Add(_liveCount);

            // Four lines: the correction's result says what it was made of, and the longest
            // translations need the room.
            _result = new Label
            {
                Left = 16, Top = 178 + grow, Width = 528, Height = 64,
                ForeColor = theme.TextMuted,
                Text = CurrentText()
            };
            Controls.Add(_result);

            _acceptBtn = UiFactory.PrimaryButton("Use this value", 16, 254 + grow, 150, 34, theme);
            _acceptBtn.Enabled = false;
            _acceptBtn.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(_acceptBtn);

            var retryBtn = UiFactory.Button("Try again", 176, 254 + grow, 110, 34);
            retryBtn.Click += (s, e) => Reset();
            Controls.Add(retryBtn);

            var cancelBtn = UiFactory.Button("Cancel", 434, 254 + grow, 110, 34);
            cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancelBtn);

            // Each as wide as its words — German "Diesen Wert übernehmen" needs 186 px of the 150 — the
            // first two packed from the left, Cancel from the right.
            UiFactory.FitToText96(_acceptBtn, 150);
            UiFactory.FitToText96(retryBtn, 110);
            retryBtn.Left = _acceptBtn.Right + 10;
            UiFactory.FitToText96(cancelBtn, 110);
            cancelBtn.Left = 544 - cancelBtn.Width;

            _tick = new System.Windows.Forms.Timer { Interval = 60 };
            _tick.Tick += (s, e) => Pump();

            Load += (s, e) => StartCapture();
            FormClosed += (s, e) => StopCapture();
            UiFactory.ScaleToScreen(this);
        }

        /// <summary>The value in use, and what it means in counts.</summary>
        private string CurrentText()
        {
            return Utils.Localization.F("Current setting: {0} °/count ({1:N0} counts for a full turn).",
                _degPerCount.ToString("0.####"), CountsForFullTurn(_degPerCount));
        }

        private static long CountsForFullTurn(double degPerCount) => degPerCount > 0 ? (long)Math.Round(360.0 / degPerCount) : 0;

        private void StartCapture()
        {
            if (!_mouse.Start())
            {
                _liveCount.Text = Utils.Localization.T("Raw mouse input unavailable.");
                return;
            }
            _keys.KeyEvent += OnKey;
            _keys.Start();
            _tick.Start();
        }

        private void StopCapture()
        {
            CancelCheck();
            try { _tick.Stop(); _tick.Dispose(); } catch { }
            try { _keys.KeyEvent -= OnKey; } catch { }
            try { _keys.Dispose(); } catch { }
            try { _mouse.Dispose(); } catch { }
        }

        /// <summary>The hook callback, on this window's thread. Separate so the tests can feed it keys.</summary>
        internal void OnKey(object sender, KeyboardHookEventArgs e)
        {
            if (e.Injected || (e.VirtualKey != VK_HOLD && e.VirtualKey != VK_CHECK))
            {
                return;
            }
            // Both keys are this window's while it is open: the game (and Windows' menu handling,
            // for F10) never sees them doing duty here.
            e.Suppress = true;

            if (e.VirtualKey == VK_CHECK)
            {
                if (!e.IsKeyDown) { _checkKeyDown = false; return; }
                if (_checkKeyDown) { return; }   // auto-repeat of a press already handled
                _checkKeyDown = true;
                if (!_counting && !_checking) { StartCheckTurn(); }
                return;
            }

            if (_checking) { return; }   // the check is turning the camera: nothing to measure yet
            if (e.IsKeyDown)
            {
                if (!_counting)
                {
                    // Movement from before the press must not count. Raw reports still queued behind
                    // this callback (it is SENT, so it runs ahead of them) are taken out first.
                    RawMouseHub.DrainPending(e.TimeStamp);
                    _mouse.Drain(out _, out _);
                    _counts = 0;
                    _counting = true;
                }
            }
            else if (_counting)
            {
                // Everything up to the release counts — the last of the turn included. It used to be
                // left for the next timer tick, which threw it away because counting had stopped by then.
                RawMouseHub.DrainPending(e.TimeStamp);
                _mouse.Drain(out int dx, out _);
                _counts += dx;
                _counting = false;
                Finish();
            }
        }

        /// <summary>Drains raw movement on the UI thread and updates the readout.</summary>
        private void Pump()
        {
            _mouse.Drain(out int dx, out _);

            if (_counting)
            {
                // Only HORIZONTAL movement yaws the camera. Signed: a turn past the start and back
                // again nets out, so the count is how far the camera ended up from where it began.
                _counts += dx;
                _liveCount.Text = Utils.Localization.F("Counting…  {0} counts", Math.Abs(_counts));
                _liveCount.ForeColor = _theme.AccentText;
                _acceptBtn.Enabled = false;
                _haveResult = false;
            }
        }

        private void Finish()
        {
            long net = _counts;
            _counts = 0;

            // After a check: a short hold is the correction, Tempo's turn plus what it took to reach the
            // start. Its sign says which way: on past it (+) or back (−); the check always turns the same
            // way as a positive count, so the sum is the true full circle either way round.
            if (_checkCounts > 0 && Math.Abs(net) < _checkCounts * CorrectionShare)
            {
                long check = _checkCounts;
                _checkCounts = 0;
                if (Math.Abs(net) < 3)
                {
                    _liveCount.Text = Utils.Localization.T("No correction: the value stays.");
                    _liveCount.ForeColor = _theme.SuccessText;
                    _result.Text = CurrentText();
                    return;
                }
                long total = check + net;
                if (total < 50)
                {
                    TooSmall(Math.Abs(net));
                    return;
                }
                _degPerCount = CameraRelativeMovement.CalibrateFromFullTurn(total);
                _haveResult = true;
                _liveCount.Text = Utils.Localization.F("{0} counts for 360°", total);
                _liveCount.ForeColor = _theme.SuccessText;
                _result.Text = Utils.Localization.F(
                    "Corrected: {0} °/count. Tempo turned {1:N0} counts and you added {2}. Press {3} to check it again.",
                    _degPerCount.ToString("0.#####"), check, (net > 0 ? "+" : "−") + Math.Abs(net).ToString("N0"), CheckKeyName);
                _acceptBtn.Enabled = true;
                return;
            }

            long counts = Math.Abs(net);
            // A turn measured from a handful of counts is noise, not data.
            if (counts < 50)
            {
                TooSmall(counts);
                return;
            }

            _checkCounts = 0;
            _haveResult = true;
            _degPerCount = CameraRelativeMovement.CalibrateFromFullTurn(counts);
            _liveCount.Text = Utils.Localization.F("{0} counts for 360°", counts);
            _liveCount.ForeColor = _theme.SuccessText;
            _result.Text = Utils.Localization.F(
                "Measured: {0} °/count. Press {1} in the game to check it: Tempo turns one full circle with this value.",
                _degPerCount.ToString("0.#####"), CheckKeyName);
            _acceptBtn.Enabled = true;
        }

        private void TooSmall(long counts)
        {
            _haveResult = false;
            _liveCount.Text = Utils.Localization.F("Only {0} counts — too small to trust.", counts);
            _liveCount.ForeColor = _theme.WarningText;
            _result.Text = Utils.Localization.F(
                "That barely moved. Hold {0} and sweep a full 360° turn, then release.", HoldKeyName);
            _acceptBtn.Enabled = false;
        }

        // ── The check turn ───────────────────────────────────────────────────

        private void StartCheckTurn()
        {
            if (_currentIsDefault && !_haveResult)
            {
                // The built-in guess, never measured: Tempo would spin the camera some arbitrary number
                // of times, which checks nothing.
                _liveCount.Text = Utils.Localization.F("Measure it first: hold {0} and turn one full circle.", HoldKeyName);
                _liveCount.ForeColor = _theme.WarningText;
                return;
            }
            if (TempoInFront())
            {
                // The movement would go to Tempo — the pointer sliding across this window — not the game.
                _liveCount.Text = Utils.Localization.F("Switch to your game first, then press {0}.", CheckKeyName);
                _liveCount.ForeColor = _theme.WarningText;
                return;
            }

            long counts = CountsForFullTurn(_degPerCount);
            if (counts < 50 || counts > 5_000_000) { return; }

            _checking = true;
            _acceptBtn.Enabled = false;
            _liveCount.Text = Utils.Localization.F("Turning one full circle… {0:N0} counts", counts);
            _liveCount.ForeColor = _theme.AccentText;
            var cancel = new ManualResetEventSlim(false);
            _checkCancel = cancel;
            Action<int> send = SendTurnForTest ?? (step => InputSimulator.MoveRelative(step, 0));
            var thread = new Thread(() =>
            {
                bool completed = false;
                try { completed = SendTurn(counts, CheckTurnSeconds, cancel, send); }
                catch (Exception ex) { Utils.Logger.Swallow("calibration check turn", ex); }
                try
                {
                    if (!IsDisposed && IsHandleCreated) { BeginInvoke((Action)(() => CheckFinished(counts, completed))); }
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            })
            {
                IsBackground = true,
                Name = "Tempo calibration check turn"
            };
            _checkThread = thread;
            thread.Start();
        }

        private void CheckFinished(long counts, bool completed)
        {
            _checking = false;
            _checkThread = null;
            try { _checkCancel?.Dispose(); } catch { }
            _checkCancel = null;
            if (!completed) { return; }
            _checkCounts = counts;
            _acceptBtn.Enabled = _haveResult;
            _liveCount.Text = Utils.Localization.F("Tempo turned {0:N0} counts.", counts);
            _liveCount.ForeColor = _theme.TextMuted;
            _result.Text = Utils.Localization.F(
                "Did it stop exactly where it started? If not, hold {0} and turn the rest of the way back, then let go.",
                HoldKeyName);
        }

        private void CancelCheck()
        {
            ManualResetEventSlim cancel = _checkCancel;
            Thread thread = _checkThread;
            try { cancel?.Set(); } catch { }
            try { thread?.Join(1000); } catch { }
        }

        private bool TempoInFront()
        {
            if (TempoInFrontForTest != null) { return TempoInFrontForTest(); }
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) { return false; }
            GetWindowThreadProcessId(fg, out uint pid);
            return pid == (uint)Environment.ProcessId;
        }

        /// <summary>
        /// Sends <paramref name="counts"/> of rightward mouse movement over <paramref name="seconds"/>, in
        /// slices every 4 ms. The running total is rounded, not each slice, so the counts add up exactly.
        /// False when cancelled.
        /// </summary>
        internal static bool SendTurn(long counts, double seconds, ManualResetEventSlim cancel, Action<int> send)
        {
            int slices = Math.Max(1, (int)(seconds * 1000 / 4));
            long sent = 0;
            var clock = Stopwatch.StartNew();
            for (int i = 1; i <= slices; i++)
            {
                long due = (long)Math.Round(counts * (double)i / slices);
                int step = (int)(due - sent);
                if (step != 0)
                {
                    send(step);
                    sent = due;
                }
                double wait = i * seconds * 1000.0 / slices - clock.Elapsed.TotalMilliseconds;
                if (wait > 0 && PreciseWait.Wait(wait, cancel)) { return false; }
                if (cancel.IsSet) { return false; }
            }
            return true;
        }

        private void Reset()
        {
            CancelCheck();
            _counting = false;
            _counts = 0;
            _checkCounts = 0;
            _haveResult = false;
            _degPerCount = _original;   // "Current setting" is the saved value again, and a check checks that
            _acceptBtn.Enabled = false;
            _mouse.Drain(out _, out _);
            _liveCount.Text = Utils.Localization.F("Hold {0} and turn…", HoldKeyName);
            _liveCount.ForeColor = _theme.TextMuted;
            _result.Text = CurrentText();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
