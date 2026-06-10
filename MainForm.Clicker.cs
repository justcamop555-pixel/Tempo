using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Persistence;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    public partial class MainForm
    {
        private string _currentProfileName = string.Empty;
        private bool _suppressProfileEvents;
        private Label _intervalHint;
        private Label _repeatCountEstLabel;
        private CheckBox _notifyFinishCheck;
        private bool _lastRunWasFinite;
        private Label _repeatDurationEstLabel;

        // Manual speed slider
        private TrackBar _speedTrack;
        private Label _speedLabel;
        private CheckBox _unlockSpeedCheck;
        private Button _speedMinusBtn;
        private Button _speedPlusBtn;
        private bool _suppressSpeedSync;

        // Anti-freeze controls
        private CheckBox _antiFreezeCheck;
        private NumericUpDown _maxCpsNum;
        private NumericUpDown _cpuThresholdNum;
        private Label _antiFreezeStatusLabel;
        private bool _suppressAntiFreeze;

        private void BuildClickerTab()
        {
            var page = new BackdropTabPage(Utils.Localization.T("Clicker")) { AutoScroll = true };

            // ── Profile bar ───────────────────────────────────────────────────
            var profileLabel = UiFactory.Label("Profile:", 12, 16, FontStyle.Bold);
            _profileCombo = UiFactory.Combo(70, 12, 220);
            _profileCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _profileCombo.SelectedIndexChanged += OnProfileSelected;

            _newProfileBtn = UiFactory.Button("New", 300, 11, 70, 26);
            _newProfileBtn.Click += OnNewProfile;
            _saveProfileBtn = UiFactory.Button("Save", 376, 11, 70, 26);
            _saveProfileBtn.Click += OnSaveProfile;
            _duplicateProfileBtn = UiFactory.Button("Duplicate", 452, 11, 90, 26);
            _duplicateProfileBtn.Click += OnDuplicateProfile;
            _deleteProfileBtn = UiFactory.Button("Delete", 548, 11, 80, 26);
            _deleteProfileBtn.Click += OnDeleteProfile;

            var nameLabel = UiFactory.Label("Name:", 12, 50, FontStyle.Bold);
            _profileNameText = UiFactory.Text(70, 47, 220);

            // ── Interval group ─────────────────────────────────────────────────
            var intervalGroup = UiFactory.Group("Click Interval", 12, 84, 360, 110);

            intervalGroup.Controls.Add(UiFactory.Caption("Hours", 16, 28));
            _hoursNum = UiFactory.Numeric(16, 46, 70, 0, 999, 0);
            _hoursNum.ValueChanged += OnIntervalFieldChanged;
            intervalGroup.Controls.Add(_hoursNum);

            intervalGroup.Controls.Add(UiFactory.Caption("Minutes", 96, 28));
            _minutesNum = UiFactory.Numeric(96, 46, 70, 0, 59, 0);
            _minutesNum.ValueChanged += OnIntervalFieldChanged;
            intervalGroup.Controls.Add(_minutesNum);

            intervalGroup.Controls.Add(UiFactory.Caption("Seconds", 176, 28));
            _secondsNum = UiFactory.Numeric(176, 46, 70, 0, 59, 0);
            _secondsNum.ValueChanged += OnIntervalFieldChanged;
            intervalGroup.Controls.Add(_secondsNum);

            intervalGroup.Controls.Add(UiFactory.Caption("Millis", 256, 28));
            _millisNum = UiFactory.Numeric(256, 46, 80, 0, 999, 100);
            _millisNum.ValueChanged += OnIntervalFieldChanged;
            intervalGroup.Controls.Add(_millisNum);

            _intervalHint = UiFactory.Caption("Total delay between clicks. Minimum 1 ms.", 16, 80);
            intervalGroup.Controls.Add(_intervalHint);

            // ── Click options group ────────────────────────────────────────────
            var clickGroup = UiFactory.Group("Click Options", 384, 84, 324, 110);

            clickGroup.Controls.Add(UiFactory.Caption("Button", 16, 28));
            _buttonCombo = UiFactory.Combo(16, 46, 90, "Left", "Right", "Middle");
            clickGroup.Controls.Add(_buttonCombo);

            clickGroup.Controls.Add(UiFactory.Caption("Type", 116, 28));
            _styleCombo = UiFactory.Combo(116, 46, 90, "Single", "Double", "Triple");
            _styleCombo.SelectedIndexChanged += (s, e) => UpdateIntervalHint();
            clickGroup.Controls.Add(_styleCombo);

            clickGroup.Controls.Add(UiFactory.Caption("Mode", 216, 28));
            _modeCombo = UiFactory.Combo(216, 46, 100, "Interval", "Hold", "Burst");
            _modeCombo.SelectedIndexChanged += OnModeChanged;
            clickGroup.Controls.Add(_modeCombo);

            clickGroup.Controls.Add(UiFactory.Caption("Hold each click (ms)", 16, 80));
            _holdMsNum = UiFactory.Numeric(170, 78, 70, 0, 5000, 0);
            _holdMsNum.ValueChanged += (s, e) => UpdateIntervalHint();
            clickGroup.Controls.Add(_holdMsNum);

            // ── Position group ─────────────────────────────────────────────────
            var positionGroup = UiFactory.Group("Click Position", 12, 200, 360, 172);

            _posCurrentRadio = UiFactory.Radio("Current cursor position", 16, 26, true);
            _posCurrentRadio.CheckedChanged += OnPositionModeChanged;
            _posFixedRadio = UiFactory.Radio("Fixed position", 16, 52);
            _posFixedRadio.CheckedChanged += OnPositionModeChanged;
            _posMultiRadio = UiFactory.Radio("Multi-point (see Multi-Point tab)", 16, 78);
            _posMultiRadio.CheckedChanged += OnPositionModeChanged;

            positionGroup.Controls.Add(_posCurrentRadio);
            positionGroup.Controls.Add(_posFixedRadio);
            positionGroup.Controls.Add(_posMultiRadio);

            positionGroup.Controls.Add(UiFactory.Caption("X", 40, 108));
            _fixedXNum = UiFactory.Numeric(58, 104, 80, -100000, 100000, 0);
            positionGroup.Controls.Add(_fixedXNum);

            positionGroup.Controls.Add(UiFactory.Caption("Y", 146, 108));
            _fixedYNum = UiFactory.Numeric(164, 104, 80, -100000, 100000, 0);
            positionGroup.Controls.Add(_fixedYNum);

            _pickFixedBtn = UiFactory.Button("Pick…", 252, 103, 90, 26);
            _pickFixedBtn.Click += (s, e) => PickFixedPosition();
            positionGroup.Controls.Add(_pickFixedBtn);

            _restoreCursorCheck = UiFactory.Check("Restore cursor position when stopped", 16, 138);
            positionGroup.Controls.Add(_restoreCursorCheck);

            // ── Repeat group ───────────────────────────────────────────────────
            var repeatGroup = UiFactory.Group("Repeat", 384, 200, 324, 102);
            _repeatUntilRadio = UiFactory.Radio("Until stopped", 16, 24, true);
            _repeatUntilRadio.CheckedChanged += OnRepeatModeChanged;
            _repeatCountRadio = UiFactory.Radio("Fixed count:", 16, 48);
            _repeatCountRadio.CheckedChanged += OnRepeatModeChanged;
            _repeatCountNum = UiFactory.Numeric(130, 45, 110, 1, 100000000, 100);
            _repeatDurationRadio = UiFactory.Radio("For (seconds):", 16, 72);
            _repeatDurationRadio.CheckedChanged += OnRepeatModeChanged;
            _repeatDurationNum = UiFactory.Numeric(130, 69, 110, 1, 86400, 60);
            repeatGroup.Controls.Add(_repeatUntilRadio);
            repeatGroup.Controls.Add(_repeatCountRadio);
            repeatGroup.Controls.Add(_repeatCountNum);
            repeatGroup.Controls.Add(_repeatDurationRadio);
            repeatGroup.Controls.Add(_repeatDurationNum);

            _repeatCountEstLabel = UiFactory.Caption("", 246, 48);
            _repeatCountEstLabel.AutoSize = false;
            _repeatCountEstLabel.Width = 74;
            _repeatCountEstLabel.Height = 16;
            _repeatCountEstLabel.ForeColor = _theme.TextMuted;
            repeatGroup.Controls.Add(_repeatCountEstLabel);

            _repeatDurationEstLabel = UiFactory.Caption("", 246, 72);
            _repeatDurationEstLabel.AutoSize = false;
            _repeatDurationEstLabel.Width = 74;
            _repeatDurationEstLabel.Height = 16;
            _repeatDurationEstLabel.ForeColor = _theme.TextMuted;
            repeatGroup.Controls.Add(_repeatDurationEstLabel);

            _repeatCountNum.ValueChanged += (s, e) => UpdateRepeatEstimates();
            _repeatDurationNum.ValueChanged += (s, e) => UpdateRepeatEstimates();

            // ── Burst group ────────────────────────────────────────────────────
            _burstGroup = UiFactory.Group("Burst Settings", 384, 310, 324, 66);
            _burstGroup.Controls.Add(UiFactory.Caption("Clicks per burst", 16, 24));
            _burstSizeNum = UiFactory.Numeric(120, 36, 70, 1, 100000, 10);
            _burstSizeNum.ValueChanged += (s, e) => UpdateIntervalHint();
            _burstGroup.Controls.Add(_burstSizeNum);
            _burstGroup.Controls.Add(UiFactory.Caption("Pause (ms)", 200, 24));
            _burstPauseNum = UiFactory.Numeric(200, 36, 100, 0, 3600000, 1000);
            _burstPauseNum.ValueChanged += (s, e) => UpdateIntervalHint();
            _burstGroup.Controls.Add(_burstPauseNum);

            // ── Randomization group ────────────────────────────────────────────
            var randGroup = UiFactory.Group("Randomization (anti-pattern)", 12, 382, 696, 72);
            _randIntervalCheck = UiFactory.Check("Randomize interval  ±", 16, 30);
            _randIntervalCheck.CheckedChanged += (s, e) => { _intervalJitterNum.Enabled = _randIntervalCheck.Checked; UpdateIntervalHint(); };
            _intervalJitterNum = UiFactory.Numeric(180, 28, 80, 0, 100000, 0);
            _intervalJitterNum.Enabled = false;
            _intervalJitterNum.ValueChanged += (s, e) => UpdateIntervalHint();
            randGroup.Controls.Add(UiFactory.Caption("ms", 264, 32));

            _randPosCheck = UiFactory.Check("Randomize position  ±", 320, 30);
            _randPosCheck.CheckedChanged += (s, e) => _posJitterNum.Enabled = _randPosCheck.Checked;
            _posJitterNum = UiFactory.Numeric(490, 28, 80, 0, 1000, 0);
            _posJitterNum.Enabled = false;
            randGroup.Controls.Add(UiFactory.Caption("px", 574, 32));

            randGroup.Controls.Add(_randIntervalCheck);
            randGroup.Controls.Add(_intervalJitterNum);
            randGroup.Controls.Add(_randPosCheck);
            randGroup.Controls.Add(_posJitterNum);

            _humanizeBtn = UiFactory.Button("Humanize", 600, 27, 96, 28);
            _humanizeBtn.Click += OnHumanizeClicked;
            randGroup.Controls.Add(_humanizeBtn);

            // ── Action area ────────────────────────────────────────────────────
            _bigStatusLabel = new Label
            {
                Text = "IDLE",
                Left = 12,
                Top = 466,
                Width = 200,
                Height = 60,
                Font = new Font("Segoe UI", 26f, FontStyle.Bold),
                ForeColor = _theme.TextMuted,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };

            // Live click-rate readout next to the big status word (only while running).
            _liveCpsLabel = new Label
            {
                Text = string.Empty,
                Left = 216,
                Top = 488,
                Width = 138,
                Height = 28,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = _theme.Accent,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };

            _startBtn = UiFactory.Button("▶  Start", 360, 472, 130, 48);
            _startBtn.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            _startBtn.BackColor = _theme.Success;
            _startBtn.ForeColor = Color.White;
            _startBtn.FlatAppearance.BorderSize = 0;
            _startBtn.Click += (s, e) => OnStartOrPauseClicked();

            _stopBtn = UiFactory.Button("■  Stop", 498, 472, 130, 48);
            _stopBtn.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            _stopBtn.BackColor = _theme.Danger;
            _stopBtn.ForeColor = Color.White;
            _stopBtn.FlatAppearance.BorderSize = 0;
            _stopBtn.Enabled = false;
            _stopBtn.Click += (s, e) => _engine.Stop();

            _cpsTestBtn = UiFactory.Button("CPS Test", 636, 472, 72, 48);

            // Optional chime + tray notice when a fixed-count / fixed-duration run
            // completes on its own (manual stops stay silent).
            _notifyFinishCheck = UiFactory.Check("Notify when a fixed run finishes", 16, 486);
            _notifyFinishCheck.CheckedChanged += (s, e) =>
            {
                if (_settings == null) return;
                _settings.NotifyOnRepeatFinish = _notifyFinishCheck.Checked;
                SettingsManager.Save(_settings);
            };
            page.Controls.Add(_notifyFinishCheck);
            _cpsTestBtn.Click += (s, e) => OpenCpsTest();

            // ── Manual speed (quick adjust slider) ─────────────────────────────
            var speedGroup = UiFactory.Group("Manual Speed", 12, 532, 360, 128);

            _speedLabel = UiFactory.Label("Target: 10 CPS  (100 ms)", 16, 26, FontStyle.Bold, 10f);
            _speedLabel.AutoSize = false;
            _speedLabel.Width = 330;
            _speedLabel.Height = 20;
            _speedLabel.AutoEllipsis = true;
            speedGroup.Controls.Add(_speedLabel);

            bool unlockedInit = _settings != null && _settings.AdvancedUnlockSpeed;

            _speedTrack = new TrackBar
            {
                Left = 12,
                Top = 46,
                Width = 250,
                Minimum = 1,
                Maximum = unlockedInit ? 2000 : 200,
                TickFrequency = unlockedInit ? 200 : 20,
                SmallChange = 1,
                LargeChange = unlockedInit ? 50 : 5,
                Value = 10
            };
            _speedTrack.Scroll += (s, e) => OnSpeedSlider();
            speedGroup.Controls.Add(_speedTrack);

            // Own row beneath the slider so it never overlaps the target label.
            _unlockSpeedCheck = UiFactory.Check("Unlock max speed (advanced)", 16, 98);
            _unlockSpeedCheck.AutoSize = true;
            _unlockSpeedCheck.Checked = unlockedInit;            // set before wiring → no warning
            _unlockSpeedCheck.CheckedChanged += OnToggleSpeedUnlock;
            speedGroup.Controls.Add(_unlockSpeedCheck);

            _speedMinusBtn = UiFactory.Button("−", 268, 48, 40, 30);
            _speedMinusBtn.Click += (s, e) => NudgeSpeed(-1);
            speedGroup.Controls.Add(_speedMinusBtn);

            _speedPlusBtn = UiFactory.Button("+", 312, 48, 40, 30);
            _speedPlusBtn.Click += (s, e) => NudgeSpeed(+1);
            speedGroup.Controls.Add(_speedPlusBtn);

            // ── Anti-freeze protection ─────────────────────────────────────────
            var afGroup = UiFactory.Group("Anti-Freeze Protection", 384, 532, 324, 128);

            _antiFreezeCheck = UiFactory.Check("Enabled (prevents system freeze)", 16, 24, true);
            _antiFreezeCheck.CheckedChanged += (s, e) => OnAntiFreezeChanged();
            afGroup.Controls.Add(_antiFreezeCheck);

            afGroup.Controls.Add(UiFactory.Caption("Max CPS", 16, 50));
            _maxCpsNum = UiFactory.Numeric(78, 46, 70, 1, 2000, 200);
            _maxCpsNum.ValueChanged += (s, e) => OnAntiFreezeChanged();
            afGroup.Controls.Add(_maxCpsNum);

            afGroup.Controls.Add(UiFactory.Caption("CPU %", 160, 50));
            _cpuThresholdNum = UiFactory.Numeric(214, 46, 70, 10, 99, 80);
            _cpuThresholdNum.ValueChanged += (s, e) => OnAntiFreezeChanged();
            afGroup.Controls.Add(_cpuThresholdNum);

            _antiFreezeStatusLabel = UiFactory.Label("Detection: idle", 16, 74, FontStyle.Regular, 9f);
            _antiFreezeStatusLabel.AutoSize = false;
            _antiFreezeStatusLabel.Width = 296;
            _antiFreezeStatusLabel.Height = 16;
            _antiFreezeStatusLabel.ForeColor = _theme.TextMuted;
            afGroup.Controls.Add(_antiFreezeStatusLabel);

            page.Controls.Add(profileLabel);
            page.Controls.Add(_profileCombo);
            page.Controls.Add(_newProfileBtn);
            page.Controls.Add(_saveProfileBtn);
            page.Controls.Add(_duplicateProfileBtn);
            page.Controls.Add(_deleteProfileBtn);
            page.Controls.Add(nameLabel);
            page.Controls.Add(_profileNameText);
            page.Controls.Add(intervalGroup);
            page.Controls.Add(clickGroup);
            page.Controls.Add(positionGroup);
            page.Controls.Add(repeatGroup);
            page.Controls.Add(_burstGroup);
            page.Controls.Add(randGroup);
            page.Controls.Add(_bigStatusLabel);
            page.Controls.Add(_liveCpsLabel);
            page.Controls.Add(_startBtn);
            page.Controls.Add(_stopBtn);
            page.Controls.Add(_cpsTestBtn);
            page.Controls.Add(speedGroup);
            page.Controls.Add(afGroup);

            _tabs.TabPages.Add(page);

            UpdatePositionControlsEnabled();
            UpdateRepeatControlsEnabled();
            UpdateBurstGroupEnabled();
            LoadAntiFreezeIntoUi();
            SyncSpeedSliderFromInterval();
            UpdateIntervalHint();
        }

        // ── Manual speed slider ──────────────────────────────────────────────────

        /// <summary>
        /// Handles edits to the H/M/S/ms fields: refreshes the rate hint, keeps the
        /// manual-speed slider in sync, and — if a run is in progress in Interval
        /// mode — applies the new interval live so tweaks take effect immediately.
        /// </summary>
        private void OnHumanizeClicked(object sender, EventArgs e)
        {
            // One-click "make the clicks look less robotic": switch on both
            // randomizers with sensible starting values the user can fine-tune.
            long ms = GetUiIntervalMs();
            int intervalJitter = (int)Math.Max(5, Math.Round(ms * 0.2));

            _randIntervalCheck.Checked = true;
            _intervalJitterNum.Enabled = true;
            _intervalJitterNum.Value = Math.Min(_intervalJitterNum.Maximum, intervalJitter);

            _randPosCheck.Checked = true;
            _posJitterNum.Enabled = true;
            _posJitterNum.Value = Math.Min(_posJitterNum.Maximum, 2);
        }

        private void OnIntervalFieldChanged(object sender, EventArgs e)
        {
            UpdateIntervalHint();

            // When the slider is driving the change it already handles sync + the
            // live engine update, so avoid doing it twice.
            if (_suppressSpeedSync)
            {
                return;
            }

            SyncSpeedSliderFromInterval();

            if (_engine != null && _engine.IsRunning && GetSelectedMode() == ClickMode.Interval)
            {
                _engine.UpdateInterval(
                    (int)_hoursNum.Value, (int)_minutesNum.Value,
                    (int)_secondsNum.Value, (int)_millisNum.Value);
            }
        }

        /// <summary>Shows the effective click rate beneath the interval fields.</summary>
        private void UpdateIntervalHint()
        {
            if (_intervalHint == null)
            {
                return;
            }

            long ms = GetUiIntervalMs();
            if (ms < 1) ms = 1;

            int styleFactor = (_styleCombo != null ? _styleCombo.SelectedIndex : 0) + 1;
            if (styleFactor < 1) styleFactor = 1;

            string text;
            ClickMode mode = GetSelectedMode();

            if (mode == ClickMode.Burst)
            {
                // Average rate over a whole burst-plus-pause cycle.
                long burst = _burstSizeNum != null ? (long)_burstSizeNum.Value : 1;
                if (burst < 1) burst = 1;
                long pause = _burstPauseNum != null ? (long)_burstPauseNum.Value : 0;
                double cycleMs = burst * ms + pause;
                double avgCps = cycleMs > 0 ? burst * styleFactor * 1000.0 / cycleMs : 0;
                text = $"≈ {avgCps:0.0} CPS avg   ·   burst {burst:N0} / {pause:N0} ms";
            }
            else
            {
                double cps = 1000.0 / ms * styleFactor;

                bool rand = _randIntervalCheck != null && _randIntervalCheck.Checked;
                long jitter = (_intervalJitterNum != null) ? (long)_intervalJitterNum.Value : 0;

                if (rand && jitter > 0)
                {
                    long lo = Math.Max(1, ms - jitter);
                    long hi = ms + jitter;
                    double cpsHi = 1000.0 / lo * styleFactor;   // shortest delay = fastest
                    double cpsLo = 1000.0 / hi * styleFactor;   // longest delay = slowest
                    text = $"≈ {cpsLo:0.0}–{cpsHi:0.0} CPS   ·   {ms:N0} ± {jitter:N0} ms";
                }
                else if (cps >= 1)
                {
                    text = $"≈ {cps:0.0} CPS   ·   {ms:N0} ms between clicks";
                }
                else
                {
                    text = $"1 click every {ms / 1000.0:0.0} s   ·   {ms:N0} ms";
                }
            }

            // If each click is held down, note it — and warn if the hold is long
            // enough to cap the rate you asked for.
            long hold = _holdMsNum != null ? (long)_holdMsNum.Value : 0;
            if (hold > 0)
            {
                text += $"   ·   hold {hold:N0} ms";
                if (mode != ClickMode.Burst && hold >= ms)
                {
                    text += " (caps rate)";
                }
            }

            _intervalHint.Text = text;
            UpdateRepeatEstimates();
        }

        /// <summary>Shows an "≈ time / ≈ clicks" estimate next to the Repeat fields.</summary>
        private void UpdateRepeatEstimates()
        {
            if (_repeatCountEstLabel == null || _repeatDurationEstLabel == null)
            {
                return;
            }

            long ms = GetUiIntervalMs();
            if (ms < 1) ms = 1;

            // Each click can't fire faster than its hold time, so the real period
            // between clicks is whichever is longer — keep the estimates honest.
            long hold = _holdMsNum != null ? (long)_holdMsNum.Value : 0;
            long period = Math.Max(ms, hold);

            long count = (long)_repeatCountNum.Value;
            double seconds = count * period / 1000.0;
            _repeatCountEstLabel.Text = "≈ " + FormatShortDuration(seconds);

            int dur = (int)_repeatDurationNum.Value;
            int styleFactor = (_styleCombo != null ? _styleCombo.SelectedIndex : 0) + 1;
            if (styleFactor < 1) styleFactor = 1;
            double clicks = dur * 1000.0 / period * styleFactor;
            _repeatDurationEstLabel.Text = "≈ " + FormatShortCount(clicks) + " clicks";

            // Only show the estimate next to the mode that's actually selected.
            _repeatCountEstLabel.Visible = _repeatCountRadio != null && _repeatCountRadio.Checked;
            _repeatDurationEstLabel.Visible = _repeatDurationRadio != null && _repeatDurationRadio.Checked;
        }

        private static string FormatShortDuration(double seconds)
        {
            if (seconds >= 3600) return $"{seconds / 3600.0:0.0} h";
            if (seconds >= 60) return $"{seconds / 60.0:0.0} min";
            return $"{seconds:0.0} s";
        }

        private static string FormatShortCount(double n)
        {
            if (n >= 1_000_000) return $"{n / 1_000_000.0:0.0}M";
            if (n >= 1_000) return $"{n / 1_000.0:0.0}k";
            return ((long)Math.Round(n)).ToString("N0");
        }

        // Highest CPS the slider exposes once "Unlock max" is on. Above 1000 CPS the
        // engine uses a sub-millisecond interval (a whole-millisecond value tops out
        // at 1000 CPS). Note that very high rates can saturate a CPU core, and Windows
        // and the target app may not actually register every click at these speeds.
        private const int UnlockedMaxCps = 2000;
        private const int NormalMaxCps = 200;

        private void OnToggleSpeedUnlock(object sender, EventArgs e)
        {
            if (_suppressSettingsEvents)
            {
                return;
            }

            if (_unlockSpeedCheck.Checked)
            {
                DialogResult ok = MessageBox.Show(this,
                    "Unlock maximum click speed?\n\n" +
                    "This raises the speed slider far above the normal limit " +
                    "(up to " + UnlockedMaxCps + " clicks/second).\n\n" +
                    "At extreme speeds Tempo can:\n" +
                    "   •  use a lot of CPU and make the mouse hard to control,\n" +
                    "   •  be obviously automated — many games ban auto-clickers.\n\n" +
                    "Windows and the target app may not register every click above ~1000/s.\n" +
                    "Use the Anti-Freeze option on this tab to cap CPU if needed.\n\n" +
                    "Enable advanced speed?",
                    "Advanced speed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (ok != DialogResult.Yes)
                {
                    _suppressSettingsEvents = true;
                    _unlockSpeedCheck.Checked = false;
                    _suppressSettingsEvents = false;
                    return;
                }
            }

            ApplySpeedUnlock(_unlockSpeedCheck.Checked);

            if (_settings != null)
            {
                _settings.AdvancedUnlockSpeed = _unlockSpeedCheck.Checked;
                SettingsManager.Save(_settings);
            }
        }

        /// <summary>Raises or restores the speed slider's ceiling.</summary>
        private void ApplySpeedUnlock(bool unlocked)
        {
            if (_speedTrack == null)
            {
                return;
            }

            int newMax = unlocked ? UnlockedMaxCps : NormalMaxCps;
            if (!unlocked && _speedTrack.Value > NormalMaxCps)
            {
                _speedTrack.Value = NormalMaxCps;
            }
            _speedTrack.Maximum = newMax;
            _speedTrack.TickFrequency = unlocked ? 200 : 20;
            _speedTrack.LargeChange = unlocked ? 50 : 5;
            OnSpeedSlider();
        }

        /// <summary>Maps the slider to the interval and live engine.</summary>
        private void OnSpeedSlider()
        {
            int cps = _speedTrack.Value;
            if (cps < 1) cps = 1;

            // Above 1000 CPS a whole-millisecond interval can't express the rate, so
            // keep the millisecond field at 1 (for validation/display) and drive the
            // real timing with a sub-millisecond value.
            long ms;
            double precise = 0.0;
            if (cps > 1000)
            {
                ms = 1;
                precise = 1000.0 / cps;   // e.g. 0.5 ms at 2000 CPS
            }
            else
            {
                ms = (long)Math.Round(1000.0 / cps);
                if (ms < 1) ms = 1;
            }

            _suppressSpeedSync = true;
            try
            {
                SetUiIntervalMs(ms);
            }
            finally
            {
                _suppressSpeedSync = false;
            }

            string perMin = (cps * 60).ToString("N0");
            _speedLabel.Text = precise > 0
                ? $"Target: {cps} CPS  (~{precise:0.00} ms \u00b7 {perMin}/min)"
                : $"Target: {cps} CPS  ({ms} ms \u00b7 {perMin}/min)";

            if (_engine.IsRunning && GetSelectedMode() == ClickMode.Interval)
            {
                _engine.UpdateInterval(
                    (int)_hoursNum.Value, (int)_minutesNum.Value,
                    (int)_secondsNum.Value, (int)_millisNum.Value, precise);
            }
        }

        private void NudgeSpeed(int delta)
        {
            // Step by the slider's LargeChange (5 normally, 25 when unlocked) so the
            // buttons move at a useful pace across the wide CPS range, not 1 at a time.
            int step = _speedTrack.LargeChange > 0 ? _speedTrack.LargeChange : 1;
            int v = _speedTrack.Value + delta * step;
            if (v < _speedTrack.Minimum) v = _speedTrack.Minimum;
            if (v > _speedTrack.Maximum) v = _speedTrack.Maximum;
            _speedTrack.Value = v;
            OnSpeedSlider();
        }

        /// <summary>Moves the slider to match the current interval (best effort).</summary>
        private void SyncSpeedSliderFromInterval()
        {
            if (_speedTrack == null || _suppressSpeedSync)
            {
                return;
            }

            long ms = GetUiIntervalMs();
            double cps = ms > 0 ? 1000.0 / ms : 100.0;
            int v = (int)Math.Round(cps);
            if (v < _speedTrack.Minimum) v = _speedTrack.Minimum;
            if (v > _speedTrack.Maximum) v = _speedTrack.Maximum;

            _suppressSpeedSync = true;
            try { _speedTrack.Value = v; }
            finally { _suppressSpeedSync = false; }

            string perMin = (cps * 60).ToString("N0");
            _speedLabel.Text = cps > _speedTrack.Maximum
                ? $"Target: {cps:0.0} CPS  ({ms} ms \u00b7 {perMin}/min)"
                : $"Target: {v}.0 CPS  ({ms} ms \u00b7 {perMin}/min)";
        }

        // ── Anti-freeze ──────────────────────────────────────────────────────────

        private void LoadAntiFreezeIntoUi()
        {
            if (_antiFreezeCheck == null || _settings == null)
            {
                return;
            }

            _suppressAntiFreeze = true;
            try
            {
                _antiFreezeCheck.Checked = _settings.AntiFreezeEnabled;
                if (_notifyFinishCheck != null)
                {
                    _notifyFinishCheck.Checked = _settings.NotifyOnRepeatFinish;
                }
                _maxCpsNum.Value = Clamp(_settings.MaxClicksPerSecond, 1, 2000);
                _cpuThresholdNum.Value = Clamp(_settings.AntiFreezeCpuThreshold, 10, 99);
            }
            finally
            {
                _suppressAntiFreeze = false;
            }

            UpdateAntiFreezeControlsEnabled();
        }

        private void OnAntiFreezeChanged()
        {
            if (_suppressAntiFreeze || _settings == null)
            {
                return;
            }

            _settings.AntiFreezeEnabled = _antiFreezeCheck.Checked;
            _settings.MaxClicksPerSecond = (int)_maxCpsNum.Value;
            _settings.AntiFreezeCpuThreshold = (int)_cpuThresholdNum.Value;

            // Apply live (engine reads these properties on the fly) and persist.
            ApplyAntiFreezeToEngine();
            SettingsManager.Save(_settings);

            UpdateAntiFreezeControlsEnabled();
        }

        private void UpdateAntiFreezeControlsEnabled()
        {
            bool on = _antiFreezeCheck.Checked;
            _maxCpsNum.Enabled = on;
            _cpuThresholdNum.Enabled = on;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Profile <-> UI mapping
        // ─────────────────────────────────────────────────────────────────────

        private ClickProfile BuildProfileFromUi()
        {
            var p = new ClickProfile
            {
                Name = string.IsNullOrWhiteSpace(_profileNameText.Text) ? "Profile" : _profileNameText.Text.Trim(),
                IntervalHours = (int)_hoursNum.Value,
                IntervalMinutes = (int)_minutesNum.Value,
                IntervalSeconds = (int)_secondsNum.Value,
                IntervalMilliseconds = (int)_millisNum.Value,
                Button = (MouseButtonType)_buttonCombo.SelectedIndex,
                Style = (ClickStyle)_styleCombo.SelectedIndex,
                Mode = GetSelectedMode(),
                FixedX = (int)_fixedXNum.Value,
                FixedY = (int)_fixedYNum.Value,
                RepeatMode = _repeatCountRadio.Checked ? RepeatMode.FixedCount
                           : _repeatDurationRadio.Checked ? RepeatMode.ForDuration
                           : RepeatMode.UntilStopped,
                RepeatCount = (long)_repeatCountNum.Value,
                RepeatDurationSeconds = (int)_repeatDurationNum.Value,
                BurstSize = (int)_burstSizeNum.Value,
                BurstPauseMilliseconds = (int)_burstPauseNum.Value,
                RandomizeInterval = _randIntervalCheck.Checked,
                IntervalJitterMilliseconds = (int)_intervalJitterNum.Value,
                RandomizePosition = _randPosCheck.Checked,
                PositionJitterPixels = (int)_posJitterNum.Value,
                ClickHoldMilliseconds = (int)_holdMsNum.Value,
                RestoreCursorOnStop = _restoreCursorCheck.Checked
            };

            // Above 1000 CPS the manual slider needs a sub-millisecond interval that
            // the whole-millisecond fields can't hold; supply it here for Interval mode.
            if (p.Mode == ClickMode.Interval && _speedTrack != null && _speedTrack.Value > 1000)
            {
                p.ManualPreciseIntervalMs = 1000.0 / _speedTrack.Value;
            }

            if (_posFixedRadio.Checked)
            {
                p.PositionMode = PositionMode.FixedPosition;
            }
            else if (_posMultiRadio.Checked)
            {
                p.PositionMode = PositionMode.MultiPoint;
            }
            else
            {
                p.PositionMode = PositionMode.CurrentPosition;
            }

            // Copy the working multi-point list.
            p.Points.Clear();
            foreach (var pt in _workingPoints)
            {
                p.Points.Add(pt.Clone());
            }
            p.PointOrder = _pointOrderCombo != null
                ? (MultiPointOrder)_pointOrderCombo.SelectedIndex
                : MultiPointOrder.Sequential;

            return p;
        }

        private void LoadProfileIntoUi(ClickProfile p)
        {
            if (p == null)
            {
                return;
            }

            _suppressProfileEvents = true;
            try
            {
                _profileNameText.Text = p.Name;
                _hoursNum.Value = Clamp(p.IntervalHours, 0, 999);
                _minutesNum.Value = Clamp(p.IntervalMinutes, 0, 59);
                _secondsNum.Value = Clamp(p.IntervalSeconds, 0, 59);
                _millisNum.Value = Clamp(p.IntervalMilliseconds, 0, 999);

                _buttonCombo.SelectedIndex = (int)p.Button;
                _styleCombo.SelectedIndex = (int)p.Style;
                _modeCombo.SelectedIndex = (int)p.Mode;

                _posCurrentRadio.Checked = p.PositionMode == PositionMode.CurrentPosition;
                _posFixedRadio.Checked = p.PositionMode == PositionMode.FixedPosition;
                _posMultiRadio.Checked = p.PositionMode == PositionMode.MultiPoint;

                _fixedXNum.Value = Clamp(p.FixedX, -100000, 100000);
                _fixedYNum.Value = Clamp(p.FixedY, -100000, 100000);
                _restoreCursorCheck.Checked = p.RestoreCursorOnStop;

                _repeatUntilRadio.Checked = p.RepeatMode == RepeatMode.UntilStopped;
                _repeatCountRadio.Checked = p.RepeatMode == RepeatMode.FixedCount;
                _repeatDurationRadio.Checked = p.RepeatMode == RepeatMode.ForDuration;
                _repeatCountNum.Value = Clamp(p.RepeatCount, 1, 100000000);
                _repeatDurationNum.Value = Clamp(p.RepeatDurationSeconds, 1, 86400);

                _burstSizeNum.Value = Clamp(p.BurstSize, 1, 100000);
                _burstPauseNum.Value = Clamp(p.BurstPauseMilliseconds, 0, 3600000);

                _randIntervalCheck.Checked = p.RandomizeInterval;
                _intervalJitterNum.Value = Clamp(p.IntervalJitterMilliseconds, 0, 100000);
                _intervalJitterNum.Enabled = p.RandomizeInterval;

                _randPosCheck.Checked = p.RandomizePosition;
                _posJitterNum.Value = Clamp(p.PositionJitterPixels, 0, 1000);
                _holdMsNum.Value = Clamp(p.ClickHoldMilliseconds, 0, 5000);
                _posJitterNum.Enabled = p.RandomizePosition;

                // Working points list.
                _workingPoints.Clear();
                foreach (var pt in p.Points)
                {
                    _workingPoints.Add(pt.Clone());
                }
                if (_pointOrderCombo != null)
                {
                    _pointOrderCombo.SelectedIndex = (int)p.PointOrder;
                }
                RefreshPointsList();

                _currentProfileName = p.Name;
            }
            finally
            {
                _suppressProfileEvents = false;
            }

            UpdatePositionControlsEnabled();
            UpdateRepeatControlsEnabled();
            UpdateBurstGroupEnabled();
            SyncSpeedSliderFromInterval();
            UpdateIntervalHint();
        }

        private ClickMode GetSelectedMode()
        {
            if (_modeCombo == null)
            {
                return ClickMode.Interval;
            }

            switch (_modeCombo.SelectedIndex)
            {
                case 1: return ClickMode.HoldToClick;
                case 2: return ClickMode.Burst;
                default: return ClickMode.Interval;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Profile commands
        // ─────────────────────────────────────────────────────────────────────

        private void LoadInitialProfile()
        {
            RefreshProfileCombo();

            ClickProfile target = null;
            if (!string.IsNullOrEmpty(_settings.LastProfileName))
            {
                target = _profiles.GetByName(_settings.LastProfileName);
            }

            if (target == null && _profiles.Count > 0)
            {
                target = _profiles.Profiles[0];
            }

            if (target != null)
            {
                SelectProfileInCombo(target.Name);
                LoadProfileIntoUi(target);
            }
        }

        private void RefreshProfileCombo()
        {
            _suppressProfileEvents = true;
            try
            {
                _profileCombo.Items.Clear();
                foreach (var p in _profiles.Profiles)
                {
                    _profileCombo.Items.Add(p.Name);
                }
            }
            finally
            {
                _suppressProfileEvents = false;
            }
        }

        private void SelectProfileInCombo(string name)
        {
            _suppressProfileEvents = true;
            try
            {
                int index = _profileCombo.Items.IndexOf(name);
                if (index >= 0)
                {
                    _profileCombo.SelectedIndex = index;
                }
            }
            finally
            {
                _suppressProfileEvents = false;
            }

            _statusProfile.Text = Localization.T("Profile: ") + name;
            if (_headerProfile != null)
            {
                _headerProfile.Text = Localization.T("Profile  •  ") + name;
            }
        }

        private void OnProfileSelected(object sender, EventArgs e)
        {
            if (_suppressProfileEvents)
            {
                return;
            }

            string name = _profileCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            var profile = _profiles.GetByName(name);
            if (profile != null)
            {
                LoadProfileIntoUi(profile);
                _statusProfile.Text = Localization.T("Profile: ") + name;
                if (_headerProfile != null)
                {
                    _headerProfile.Text = Localization.T("Profile  •  ") + name;
                }
                _settings.LastProfileName = name;
            }
        }

        private void OnNewProfile(object sender, EventArgs e)
        {
            var profile = new ClickProfile("New Profile")
            {
                IntervalMilliseconds = 100
            };

            _profiles.Add(profile);
            RefreshProfileCombo();
            SelectProfileInCombo(profile.Name);
            LoadProfileIntoUi(profile);
            _profiles.Save();
        }

        private void OnSaveProfile(object sender, EventArgs e)
        {
            ClickProfile edited = BuildProfileFromUi();
            string error = edited.Validate();
            if (error != null)
            {
                ShowWarning(error);
                return;
            }

            // Renaming: if the name changed, update the stored entry's key.
            if (!string.IsNullOrEmpty(_currentProfileName) &&
                !string.Equals(_currentProfileName, edited.Name, StringComparison.OrdinalIgnoreCase))
            {
                var existing = _profiles.GetByName(_currentProfileName);
                if (existing != null)
                {
                    // Remove the old, add under new name (kept unique by manager).
                    _profiles.Remove(_currentProfileName);
                }
                _profiles.Add(edited);
            }
            else
            {
                _profiles.Update(edited);
            }

            _profiles.Save();
            RefreshProfileCombo();
            SelectProfileInCombo(edited.Name);
            _currentProfileName = edited.Name;
            _settings.LastProfileName = edited.Name;
            _statusProfile.Text = Localization.T("Profile: ") + edited.Name;
            if (_headerProfile != null)
            {
                _headerProfile.Text = Localization.T("Profile  •  ") + edited.Name;
            }
            ShowInfo($"Profile '{edited.Name}' saved.");
        }

        private void OnDuplicateProfile(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentProfileName))
            {
                return;
            }

            // Duplicate the stored profile only — not the in-memory edits. Saving
            // unsaved edits as a side effect of "Duplicate" caused a confusing
            // edge case where renaming first and then duplicating produced three
            // entries (the rename + the duplicate of the original) instead of
            // two. Users wanting to duplicate edits should press Save first.
            var copy = _profiles.Duplicate(_currentProfileName);
            if (copy != null)
            {
                _profiles.Save();
                RefreshProfileCombo();
                SelectProfileInCombo(copy.Name);
                LoadProfileIntoUi(copy);
            }
        }

        private void OnDeleteProfile(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentProfileName))
            {
                return;
            }

            var confirm = MessageBox.Show(this,
                $"Delete profile '{_currentProfileName}'?",
                "Tempo",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            _profiles.Remove(_currentProfileName);
            _profiles.Save();
            RefreshProfileCombo();

            if (_profiles.Count > 0)
            {
                var first = _profiles.Profiles[0];
                SelectProfileInCombo(first.Name);
                LoadProfileIntoUi(first);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Control-state helpers
        // ─────────────────────────────────────────────────────────────────────

        private void OnModeChanged(object sender, EventArgs e)
        {
            UpdateBurstGroupEnabled();
            UpdateIntervalHint();

            // Hold mode polls the key directly, so re-evaluate hotkey registration.
            ApplyHotkeysFromSettings();
        }

        private void OnPositionModeChanged(object sender, EventArgs e)
        {
            UpdatePositionControlsEnabled();
        }

        private void OnRepeatModeChanged(object sender, EventArgs e)
        {
            UpdateRepeatControlsEnabled();
        }

        private void UpdatePositionControlsEnabled()
        {
            bool fixedPos = _posFixedRadio.Checked;
            _fixedXNum.Enabled = fixedPos;
            _fixedYNum.Enabled = fixedPos;
            _pickFixedBtn.Enabled = fixedPos;
        }

        private void UpdateRepeatControlsEnabled()
        {
            _repeatCountNum.Enabled = _repeatCountRadio.Checked;
            _repeatDurationNum.Enabled = _repeatDurationRadio.Checked;
            UpdateRepeatEstimates();
        }

        private void UpdateBurstGroupEnabled()
        {
            bool burst = GetSelectedMode() == ClickMode.Burst;
            _burstGroup.Enabled = burst;
        }

        private void PickFixedPosition()
        {
            // Hide ourselves briefly so the overlay covers the desktop cleanly.
            bool wasVisible = Visible;
            Hide();
            System.Threading.Thread.Sleep(150);

            try
            {
                using (var picker = new CoordinatePickerForm(_theme))
                {
                    if (picker.ShowDialog() == DialogResult.OK)
                    {
                        // Ensure we are in fixed mode and store the coordinate.
                        _posFixedRadio.Checked = true;
                        _fixedXNum.Value = Clamp(picker.PickedX, (int)_fixedXNum.Minimum, (int)_fixedXNum.Maximum);
                        _fixedYNum.Value = Clamp(picker.PickedY, (int)_fixedYNum.Minimum, (int)_fixedYNum.Maximum);
                        UpdatePositionControlsEnabled();
                    }
                }
            }
            finally
            {
                if (wasVisible)
                {
                    Show();
                    EnsureOnScreen();
                    Activate();
                    ReassertTopMost();
                }
            }
        }

        private void OpenCpsTest()
        {
            using (var form = new CpsTestForm(_theme, _settings.CpsTestBest))
            {
                form.ShowDialog(this);
                if (form.AllTimeBest > _settings.CpsTestBest)
                {
                    _settings.CpsTestBest = form.AllTimeBest;
                    SettingsManager.Save(_settings);
                }
            }
        }

        /// <summary>Selects the next/previous profile in the combo, wrapping around.</summary>
        private void CycleProfile(int delta)
        {
            int count = _profileCombo.Items.Count;
            if (count == 0)
            {
                return;
            }

            int index = _profileCombo.SelectedIndex;
            if (index < 0)
            {
                index = 0;
            }

            index = ((index + delta) % count + count) % count;
            _profileCombo.SelectedIndex = index; // fires OnProfileSelected -> loads it
        }

        /// <summary>
        /// Returns the configured interval (ms) as currently shown in the UI.
        /// </summary>
        private long GetUiIntervalMs()
        {
            long ms =
                (long)_hoursNum.Value * 3_600_000L +
                (long)_minutesNum.Value * 60_000L +
                (long)_secondsNum.Value * 1_000L +
                (long)_millisNum.Value;
            return ms < 1 ? 1 : ms;
        }

        /// <summary>Writes a total interval (ms) back into the H/M/S/ms controls.</summary>
        private void SetUiIntervalMs(long totalMs)
        {
            if (totalMs < 1)
            {
                totalMs = 1;
            }

            long hours = totalMs / 3_600_000L;
            totalMs -= hours * 3_600_000L;
            long minutes = totalMs / 60_000L;
            totalMs -= minutes * 60_000L;
            long seconds = totalMs / 1_000L;
            long ms = totalMs - seconds * 1_000L;

            _hoursNum.Value = Clamp(hours, 0, 999);
            _minutesNum.Value = Clamp(minutes, 0, 59);
            _secondsNum.Value = Clamp(seconds, 0, 59);
            _millisNum.Value = Clamp(ms, 0, 999);
        }

        /// <summary>Adds (or subtracts) milliseconds to the interval on the fly.</summary>
        private void NudgeInterval(int deltaMs)
        {
            long updated = GetUiIntervalMs() + deltaMs;
            SetUiIntervalMs(updated);

            // If a run is in progress, push the new interval live to the engine so
            // the change takes effect on the next iteration without a stop+start
            // (which would have blocked the UI for up to two seconds joining the
            // worker thread).
            if (_engine.IsRunning && GetSelectedMode() == ClickMode.Interval)
            {
                _engine.UpdateInterval(
                    (int)_hoursNum.Value,
                    (int)_minutesNum.Value,
                    (int)_secondsNum.Value,
                    (int)_millisNum.Value);
            }

            _statusState.Text = $"Interval: {GetUiIntervalMs()} ms";
            SyncSpeedSliderFromInterval();
        }

        // ── numeric clamp helpers ──────────────────────────────────────────────

        private static decimal Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static decimal Clamp(long value, long min, long max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
