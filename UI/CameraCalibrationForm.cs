using System;
using System.Drawing;
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
    /// Raw Input is registered with RIDEV_INPUTSINK, so movement keeps arriving while
    /// the game — not this window — has focus.
    /// </summary>
    public sealed class CameraCalibrationForm : Form
    {
        // F10 by default: rarely bound in games, and easy to hold with the left hand
        // while the right hand sweeps the mouse.
        private const int VK_HOLD = 0x79;           // F10
        private const string HoldKeyName = "F10";

        private readonly RawMouseInput _mouse = new RawMouseInput();
        private readonly LowLevelKeyboardHook _keys = new LowLevelKeyboardHook();
        private readonly System.Windows.Forms.Timer _tick;

        private readonly Label _instructions;
        private readonly Label _liveCount;
        private readonly Label _result;
        private readonly Button _acceptBtn;

        private volatile bool _counting;
        private long _counts;
        private double _degPerCount;
        private bool _haveResult;

        /// <summary>The measured degrees-per-count, valid only when ShowDialog returns OK.</summary>
        public double DegreesPerCount => _degPerCount;

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

            Text = Utils.Localization.T("Tempo — calibrate camera sensitivity");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 300);
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
                    "2.  Hold {0} down.\n" +
                    "3.  While holding it, turn your camera EXACTLY one full circle (360°)\n" +
                    "     — all the way round, back to where it started.\n" +
                    "4.  Release {0}.\n\n" +
                    "Counting only runs while {0} is held, so moving the mouse back " +
                    "here afterwards won't spoil the measurement.",
                    HoldKeyName)
            };
            Controls.Add(_instructions);

            // As tall as its words, and everything below it moves down by what it grows. A line of text
            // is not 1.5× taller on a 150% screen but 25 px for 100%'s 15: the eight lines needed 200 px
            // of the 177 the 118 px box became, and the last sentence was not drawn. Measured in 100% units,
            // as the box is (UiFactory.Measure96).
            int grow = Math.Max(0, UiFactory.Measure96(_instructions.Text, _instructions.Font, 528, TextFormatFlags.Default).Height + 2 - 118);
            _instructions.Height += grow;
            ClientSize = new Size(560, 300 + grow);

            _liveCount = new Label
            {
                Left = 16, Top = 140 + grow, Width = 528, Height = 34,
                Font = new Font("Consolas", 13f, FontStyle.Bold),
                ForeColor = theme.TextMuted,
                Text = Utils.Localization.F("Hold {0} and turn…", HoldKeyName)
            };
            Controls.Add(_liveCount);

            _result = new Label
            {
                Left = 16, Top = 178 + grow, Width = 528, Height = 46,
                ForeColor = theme.TextMuted,
                Text = Utils.Localization.F("Current setting: {0} °/count", current.ToString("0.####"))
            };
            Controls.Add(_result);

            _acceptBtn = UiFactory.PrimaryButton("Use this value", 16, 236 + grow, 150, 34, theme);
            _acceptBtn.Enabled = false;
            _acceptBtn.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(_acceptBtn);

            var retryBtn = UiFactory.Button("Try again", 176, 236 + grow, 110, 34);
            retryBtn.Click += (s, e) => Reset();
            Controls.Add(retryBtn);

            var cancelBtn = UiFactory.Button("Cancel", 434, 236 + grow, 110, 34);
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
            try { _tick.Stop(); _tick.Dispose(); } catch { }
            try { _keys.KeyEvent -= OnKey; } catch { }
            try { _keys.Dispose(); } catch { }
            try { _mouse.Dispose(); } catch { }
        }

        private void OnKey(object sender, KeyboardHookEventArgs e)
        {
            if (e.Injected || e.VirtualKey != VK_HOLD)
            {
                return;
            }

            if (e.IsKeyDown)
            {
                if (!_counting)
                {
                    _counting = true;
                    _counts = 0;
                    _mouse.Drain(out _, out _);      // discard anything from before the press
                }
            }
            else
            {
                _counting = false;
            }

            // Swallow F10 so the game (and Windows' menu handling) never sees it while
            // it is doing duty as the calibration trigger.
            e.Suppress = true;
        }

        /// <summary>Drains raw movement on the UI thread and updates the readout.</summary>
        private void Pump()
        {
            _mouse.Drain(out int dx, out _);

            if (_counting)
            {
                // Only HORIZONTAL movement yaws the camera. Absolute value, so it does
                // not matter whether the user turns left or right.
                _counts += Math.Abs(dx);
                _liveCount.Text = Utils.Localization.F("Counting…  {0} counts", _counts);
                _liveCount.ForeColor = _theme.AccentText;
                _acceptBtn.Enabled = false;
                _haveResult = false;
                return;
            }

            if (_counts > 0 && !_haveResult)
            {
                Finish();
            }
        }

        private void Finish()
        {
            _haveResult = true;

            // A turn measured from a handful of counts is noise, not data.
            if (_counts < 50)
            {
                _liveCount.Text = Utils.Localization.F("Only {0} counts — too small to trust.", _counts);
                _liveCount.ForeColor = _theme.WarningText;
                _result.Text = Utils.Localization.F(
                    "That barely moved. Hold {0} and sweep a full 360° turn, then release.", HoldKeyName);
                _counts = 0;
                _acceptBtn.Enabled = false;
                return;
            }

            _degPerCount = CameraRelativeMovement.CalibrateFromFullTurn(_counts);
            _liveCount.Text = Utils.Localization.F("{0} counts for 360°", _counts);
            _liveCount.ForeColor = _theme.SuccessText;
            _result.Text = Utils.Localization.F("Measured: {0} °/count.\n"
                           + "If the character drifts off-heading as you turn, run this again — "
                           + "and make sure in-game mouse acceleration is OFF.",
                           _degPerCount.ToString("0.#####"));
            _acceptBtn.Enabled = true;
        }

        private void Reset()
        {
            _counting = false;
            _counts = 0;
            _haveResult = false;
            _acceptBtn.Enabled = false;
            _mouse.Drain(out _, out _);
            _liveCount.Text = Utils.Localization.F("Hold {0} and turn…", HoldKeyName);
            _liveCount.ForeColor = _theme.TextMuted;
        }
    }
}
