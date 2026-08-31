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
        private int _lastRunDurationSeconds;
        private long _lastRunTargetClicks;
        private Label _profileDirtyLabel;
        private string _profileSnapshotJson;
        private Label _repeatDurationEstLabel;

        // Manual speed slider
        private SmoothTrackBar _speedTrack;
        private SpeedTargetLabel _speedLabel;
        private CheckBox _unlockSpeedCheck;
        private Button _speedMinusBtn;
        private Button _speedPlusBtn;
        private NumericUpDown _exactCpsNum;
        private Button _exactCpsSetBtn;
        private readonly System.Collections.Generic.List<Button> _cpsPresetBtns = new System.Collections.Generic.List<Button>();
        private int[] _cpsPresetValues;
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
            page.Name = "clicker";   // stable key for LastTabKey

            // ── Profile bar ───────────────────────────────────────────────────
            var profileLabel = UiFactory.Label(Localization.T("Profile:"), 12, 16, FontStyle.Bold);
            profileLabel.Text = profileLabel.Text.Replace(":", string.Empty).ToUpperInvariant();
            _profileCombo = UiFactory.Combo(70, 12, 220);
            // Editable + autocomplete so you can type to search/jump to a profile by name.
            // (A plain DropDownList only prefix-jumps one character at a time.) Picking a
            // match fires OnProfileSelected; free text you don't pick is harmless - the
            // real current profile is always shown in the status bar and header, and only
            // the New/Save buttons ever create or rename a profile.
            _profileCombo.DropDownStyle = ComboBoxStyle.DropDown;
            _profileCombo.AutoCompleteMode = AutoCompleteMode.Suggest;
            _profileCombo.AutoCompleteSource = AutoCompleteSource.ListItems;
            _profileCombo.SelectedIndexChanged += OnProfileSelected;

            _newProfileBtn = UiFactory.Button(Localization.T("New"), 300, 10, 78, 28);
            _newProfileBtn.Click += OnNewProfile;
            StyleAccentButton(_newProfileBtn);
            SetGlyph(_newProfileBtn, ActionGlyph.Plus);
            _saveProfileBtn = UiFactory.Button(Localization.T("Save"), 384, 10, 80, 28);
            _saveProfileBtn.Click += OnSaveProfile;
            SetGlyph(_saveProfileBtn, ActionGlyph.Save);
            _duplicateProfileBtn = UiFactory.Button(Localization.T("Duplicate"), 470, 10, 104, 28);
            _duplicateProfileBtn.Click += OnDuplicateProfile;
            SetGlyph(_duplicateProfileBtn, ActionGlyph.Copy);
            _deleteProfileBtn = UiFactory.Button(Localization.T("Delete"), 580, 10, 86, 28);
            _deleteProfileBtn.Click += OnDeleteProfile;
            StyleDangerButton(_deleteProfileBtn);
            SetGlyph(_deleteProfileBtn, ActionGlyph.Trash);

            var nameLabel = UiFactory.Label(Localization.T("Name:"), 12, 50, FontStyle.Bold);
            nameLabel.Text = nameLabel.Text.Replace(":", string.Empty).ToUpperInvariant();
            _profileNameText = UiFactory.Text(70, 47, 220);

            // Mirrors the Keybinds tab: edits live only in these controls until
            // Save, and switching profiles silently discards them — say so.
            _profileDirtyLabel = new Label
            {
                Text = Localization.T("\u25CF Unsaved changes \u2014 click Save"),
                AutoSize = true,
                Location = new Point(300, 51),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = _theme.Accent,
                Visible = false
            };
            page.Controls.Add(_profileDirtyLabel);

            // ── Interval group ─────────────────────────────────────────────────
            var intervalGroup = UiFactory.Group(Localization.T("Click Interval"), 12, 84, 360, 110, CardIcon.Clock);

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

            // Three-segment label so the "≈ 114.3 CPS" part reads in the accent colour
            // with the rest muted, like the design mock.
            _intervalHint = new SpeedTargetLabel
            {
                Left = 16,
                Top = 80,
                Width = 336,   // group is 360 wide, less the 16 px left inset and a little padding
                Height = 16,
                Font = new Font("Segoe UI", 8.5f),
                BoldValue = true,
                AccentColor = _theme.Accent,
                MutedColor = _theme.TextMuted,
                ForeColor = _theme.TextMuted,
                BackColor = _theme.Surface
            };
            ((SpeedTargetLabel)_intervalHint).SetParts("", "", "Total delay between clicks. Minimum 1 ms.");
            intervalGroup.Controls.Add(_intervalHint);

            // ── Click options group ────────────────────────────────────────────
            var clickGroup = UiFactory.Group(Localization.T("Click Options"), 384, 84, 324, 110, CardIcon.Cursor);

            clickGroup.Controls.Add(UiFactory.Caption("Button", 16, 28));
            _buttonCombo = UiFactory.Combo(16, 46, 90, "Left", "Right", "Middle", "Keyboard key");
            _buttonCombo.SelectedIndexChanged += OnClickTargetChanged;
            clickGroup.Controls.Add(_buttonCombo);

            clickGroup.Controls.Add(UiFactory.Caption("Type", 116, 28));
            _styleCombo = UiFactory.Combo(116, 46, 100, "Single", "Double", "Triple", "Quadruple");
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

            // Shown/enabled only when the Button dropdown is set to "Keyboard key": pick
            // which key to auto-press. Sits on the same row as Hold (which applies to keys
            // too — a non-zero hold holds the key down each press).
            _setKeyBtn = UiFactory.Button("⌨ Set key…", 246, 77, 74, 28);
            _setKeyBtn.Click += OnSetAutoPressKey;
            _setKeyBtn.Enabled = false;
            clickGroup.Controls.Add(_setKeyBtn);

            // ── Position group ─────────────────────────────────────────────────
            var positionGroup = UiFactory.Group(Localization.T("Click Position"), 12, 200, 360, 172, CardIcon.Target);

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

            _pickFixedBtn = UiFactory.Button(Localization.T("Pick…"), 252, 103, 90, 26);
            _pickFixedBtn.Click += (s, e) => PickFixedPosition();
            positionGroup.Controls.Add(_pickFixedBtn);

            _restoreCursorCheck = UiFactory.Check("Restore cursor position when stopped", 16, 138);
            positionGroup.Controls.Add(_restoreCursorCheck);

            // ── Repeat group ───────────────────────────────────────────────────
            var repeatGroup = UiFactory.Group(Localization.T("Repeat"), 384, 200, 324, 102, CardIcon.Repeat);
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
            // Single aligned row: each label sits to the LEFT of its field on the same
            // baseline. The previous layout stacked a caption at y=24 over a field at
            // y=36 inside a 66px card, so the labels were clipped under the spinners.
            _burstGroup = UiFactory.Group(Localization.T("Burst Settings"), 384, 310, 324, 66, CardIcon.Bolt);
            _burstGroup.Controls.Add(UiFactory.Caption("Clicks per burst", 14, 37));
            _burstSizeNum = UiFactory.Numeric(112, 33, 58, 1, 100000, 10);
            _burstSizeNum.ValueChanged += (s, e) => UpdateIntervalHint();
            _burstGroup.Controls.Add(_burstSizeNum);
            _burstGroup.Controls.Add(UiFactory.Caption("Pause (ms)", 180, 37));
            _burstPauseNum = UiFactory.Numeric(246, 33, 64, 0, 3600000, 1000);
            _burstPauseNum.ValueChanged += (s, e) => UpdateIntervalHint();
            _burstGroup.Controls.Add(_burstPauseNum);

            // ── Randomization group ────────────────────────────────────────────
            var randGroup = UiFactory.Group(Localization.T("Randomization (anti-pattern)"), 12, 382, 696, 72, CardIcon.Wave);
            _randIntervalCheck = UiFactory.Check("Randomize interval  ±", 16, 30);
            _randIntervalCheck.CheckedChanged += (s, e) => { _intervalJitterNum.Enabled = _randIntervalCheck.Checked; UpdateIntervalHint(); SyncHumanizeButtonFromState(); };
            _intervalJitterNum = UiFactory.Numeric(180, 28, 80, 0, 100000, 0);
            _intervalJitterNum.Enabled = false;
            _intervalJitterNum.ValueChanged += (s, e) => { UpdateIntervalHint(); SyncHumanizeButtonFromState(); };
            randGroup.Controls.Add(UiFactory.Caption("ms", 264, 32));

            _randPosCheck = UiFactory.Check("Randomize position  ±", 320, 30);
            _randPosCheck.CheckedChanged += (s, e) => { _posJitterNum.Enabled = _randPosCheck.Checked; SyncHumanizeButtonFromState(); };
            _posJitterNum = UiFactory.Numeric(490, 28, 80, 0, 1000, 0);
            _posJitterNum.Enabled = false;
            _posJitterNum.ValueChanged += (s, e) => SyncHumanizeButtonFromState();
            randGroup.Controls.Add(UiFactory.Caption("px", 574, 32));

            randGroup.Controls.Add(_randIntervalCheck);
            randGroup.Controls.Add(_intervalJitterNum);
            randGroup.Controls.Add(_randPosCheck);
            randGroup.Controls.Add(_posJitterNum);

            _humanizeBtn = UiFactory.Button(Localization.T("Humanize"), 596, 26, 100, 30);
            _humanizeBtn.Click += OnHumanizeClicked;
            StylePurpleButton(_humanizeBtn);
            SetGlyph(_humanizeBtn, ActionGlyph.Person);
            randGroup.Controls.Add(_humanizeBtn);

            // ── Action area ────────────────────────────────────────────────────
            _bigStatusLabel = new Label
            {
                Text = Localization.T("IDLE"),
                Left = 12,
                Top = 462,
                Width = 200,
                Height = 42,
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
                Top = 470,
                Width = 138,
                Height = 28,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = _theme.Accent,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };

            _startBtn = UiFactory.Button("▶  Start", 356, 472, 124, 48);
            _startBtn.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            _startBtn.BackColor = _theme.Success;
            _startBtn.ForeColor = Color.White;
            _startBtn.FlatAppearance.BorderSize = 0;
            _startBtn.Click += (s, e) => OnStartOrPauseClicked();

            _stopBtn = UiFactory.Button("■  Stop", 488, 472, 124, 48);
            _stopBtn.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            _stopBtn.BackColor = _theme.Danger;
            _stopBtn.ForeColor = Color.White;
            _stopBtn.FlatAppearance.BorderSize = 0;
            _stopBtn.Enabled = false;
            _stopBtn.Click += (s, e) => { _engine.Stop(); try { _secondCursor?.StopSpam(); } catch { } };

            // Wider than the text alone so the gauge icon + full "CPS Test" label never
            // clips (it was showing "CPS Tes").
            _cpsTestBtn = UiFactory.Button(Localization.T("CPS Test"), 620, 472, 92, 48);
            SetGlyph(_cpsTestBtn, ActionGlyph.Gauge);

            // Optional chime + tray notice when a fixed-count / fixed-duration run
            // completes on its own (manual stops stay silent).
            _notifyFinishCheck = UiFactory.Toggle("Notify when a fixed run finishes", 16, 506);
            _notifyFinishCheck.CheckedChanged += (s, e) =>
            {
                if (_settings == null) return;
                _settings.NotifyOnRepeatFinish = _notifyFinishCheck.Checked;
                SettingsManager.Save(_settings);
            };
            page.Controls.Add(_notifyFinishCheck);
            _cpsTestBtn.Click += (s, e) => OpenCpsTest();

            // ── Manual speed (quick adjust slider) ─────────────────────────────
            var speedGroup = UiFactory.Group(Localization.T("Manual Speed"), 12, 532, 360, 198, CardIcon.Gauge);

            _speedLabel = new SpeedTargetLabel
            {
                Left = 16,
                Top = 26,
                Width = 336,
                Height = 22,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                AccentColor = _theme.Accent,
                MutedColor = _theme.TextMuted,
                ForeColor = _theme.Text,
                BackColor = _theme.Surface
            };
            _speedLabel.SetParts(Localization.T("Target:") + " ", "10 CPS", "  (100 ms)");
            speedGroup.Controls.Add(_speedLabel);

            bool unlockedInit = _settings != null && _settings.AdvancedUnlockSpeed;

            _speedTrack = new SmoothTrackBar
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

            // One-tap CPS presets so you don't have to type interval values or drag the
            // slider. Each sets the speed (and the click-interval fields) to that rate.
            speedGroup.Controls.Add(UiFactory.Caption("Presets (CPS):", 16, 134));
            _cpsPresetValues = new[] { 10, 50, 100, 200 };
            int presetX = 110;
            foreach (int cps in _cpsPresetValues)
            {
                var pb = UiFactory.Button(cps.ToString(), presetX, 130, 52, 26);
                int target = cps;
                pb.Click += (s, e) => ApplyCpsPreset(target);
                speedGroup.Controls.Add(pb);
                _cpsPresetBtns.Add(pb);
                presetX += 58;
            }

            // Type an exact target rate when none of the presets is the number you want.
            // It's clamped to the slider's current ceiling (enable "Unlock max speed"
            // above to set rates beyond 200 CPS).
            speedGroup.Controls.Add(UiFactory.Caption(Localization.T("Set exact CPS:"), 16, 166));
            _exactCpsNum = UiFactory.Numeric(110, 162, 74, 1, UnlockedMaxCps, 10);
            _exactCpsNum.Value = Clamp(_speedTrack.Value, 1, UnlockedMaxCps);
            speedGroup.Controls.Add(_exactCpsNum);
            _exactCpsSetBtn = UiFactory.Button(Localization.T("Set"), 192, 161, 58, 28);
            _exactCpsSetBtn.Click += (s, e) => ApplyCpsPreset((int)_exactCpsNum.Value);
            StyleAccentButton(_exactCpsSetBtn);
            _exactCpsNum.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    ApplyCpsPreset((int)_exactCpsNum.Value);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
            speedGroup.Controls.Add(_exactCpsSetBtn);

            // ── Anti-freeze protection ─────────────────────────────────────────
            var afGroup = UiFactory.Group(Localization.T("Anti-Freeze Protection"), 384, 532, 324, 128, CardIcon.Shield);

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

            page.Controls.Add(BuildSecondCursorGroup());

            // Footer hint bar (matches the design): a faint info line under the cards.
            var tipLabel = new Label
            {
                Text = "ⓘ  " + Localization.T("Tip: Press F6 to start/stop")
                     + "    •    " + Localization.T("All times in milliseconds"),
                Left = 14,
                Top = 904,
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = _theme.TextMuted,
                BackColor = Color.Transparent
            };
            page.Controls.Add(tipLabel);

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
            // Toggle "make clicks look less robotic". The old button only ever turned
            // randomization ON, with no one-click way back; now a second press clears
            // it. ON applies a sensible natural default (jitter scaled to the interval
            // plus a couple of pixels of position wobble) that the fields below still
            // let you fine-tune.
            _humanizeLevel = _humanizeLevel == 0 ? 1 : 0;

            if (_humanizeLevel == 0)
            {
                _randIntervalCheck.Checked = false;
                _intervalJitterNum.Value = 0;
                _randPosCheck.Checked = false;
                _posJitterNum.Value = 0;
            }
            else
            {
                long ms = GetUiIntervalMs();
                int intervalJitter = (int)Math.Max(5, Math.Round(ms * 0.2));

                _randIntervalCheck.Checked = true;
                _intervalJitterNum.Enabled = true;
                _intervalJitterNum.Value = Math.Min(_intervalJitterNum.Maximum, intervalJitter);

                _randPosCheck.Checked = true;
                _posJitterNum.Enabled = true;
                _posJitterNum.Value = Math.Min(_posJitterNum.Maximum, 2);
            }

            UpdateHumanizeButton();
            UpdateIntervalHint();
        }

        /// <summary>
        /// Shows whether Humanize is active by filling the button (on) vs a quieter
        /// outline (off), and adds a check to the label. Also re-synced when a profile
        /// loads so the button reflects the loaded randomizer state, not a stale click.
        /// </summary>
        private void UpdateHumanizeButton()
        {
            if (_humanizeBtn == null)
            {
                return;
            }
            bool on = _humanizeLevel != 0;
            _humanizeBtn.Text = on ? "✓ " + Localization.T("Humanize") : Localization.T("Humanize");
            try
            {
                if (on)
                {
                    _humanizeBtn.BackColor = HumanizePurple;
                    _humanizeBtn.ForeColor = Color.White;
                    _humanizeBtn.FlatAppearance.BorderSize = 0;
                }
                else
                {
                    // Quiet "available" look: the theme surface with a purple outline.
                    _humanizeBtn.BackColor = _theme != null ? _theme.Surface2 : Color.FromArgb(40, 40, 48);
                    _humanizeBtn.ForeColor = HumanizePurple;
                    _humanizeBtn.FlatAppearance.BorderColor = HumanizePurple;
                    _humanizeBtn.FlatAppearance.BorderSize = 1;
                }
            }
            catch { }
        }

        /// <summary>
        /// Keeps the Humanize button in sync with the actual randomizer state (called
        /// after a profile loads and after manual randomizer edits), so it never shows
        /// "on" when both randomizers are off, or vice-versa.
        /// </summary>
        private void SyncHumanizeButtonFromState()
        {
            bool anyRandom =
                (_randIntervalCheck != null && _randIntervalCheck.Checked && _intervalJitterNum != null && _intervalJitterNum.Value > 0) ||
                (_randPosCheck != null && _randPosCheck.Checked && _posJitterNum != null && _posJitterNum.Value > 0);
            _humanizeLevel = anyRandom ? 1 : 0;
            UpdateHumanizeButton();
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
                    // Say WHICH rate this is when a multi-click style is inflating it.
                    // Bare "114.3 CPS" next to Manual Speed's "29 CPS" (same interval,
                    // Quadruple) read as a straight contradiction.
                    text = styleFactor > 1
                        ? $"≈ {1000.0 / ms:0.0}/s × {styleFactor} = {cps:0.0} clicks/s   ·   {ms:N0} ms between clicks"
                        : $"≈ {cps:0.0} CPS   ·   {ms:N0} ms between clicks";
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

            // Highlight the leading rate ("≈ 114.3 CPS") in the accent colour, keep the
            // detail after the first separator muted.
            if (_intervalHint is SpeedTargetLabel seg)
            {
                seg.AccentColor = _theme.Accent;
                seg.MutedColor = _theme.TextMuted;
                int sep = text.IndexOf("   ·   ", StringComparison.Ordinal);
                if (sep > 0)
                {
                    seg.SetParts("", text.Substring(0, sep), text.Substring(sep));
                }
                else
                {
                    seg.SetParts("", text, "");
                }
            }
            else
            {
                _intervalHint.Text = text;
            }
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
            _repeatDurationEstLabel.Text = Localization.F("≈ {0} clicks", FormatShortCount(clicks));

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
                    Localization.F("Unlock maximum click speed?\n\n"
                    + "This raises the speed slider far above the normal limit "
                    + "(up to {0} clicks/second).\n\n"
                    + "At extreme speeds Tempo can:\n"
                    + "   •  use a lot of CPU and make the mouse hard to control,\n"
                    + "   •  be obviously automated — many games ban auto-clickers.\n\n"
                    + "Windows and the target app may not register every click above ~1000/s.\n"
                    + "Use the Anti-Freeze option on this tab to cap CPU if needed.\n\n"
                    + "Enable advanced speed?", UnlockedMaxCps),
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

                // Keep the two speed features consistent: the Anti-Freeze hard cap
                // (default 200 CPS) would otherwise silently clamp the unlocked rate,
                // making "Unlock max speed" do nothing. Raise the cap to the unlocked
                // ceiling when unlocking; restore the safe default when re-locking, but
                // only if the cap is still the auto-raised value so a custom cap is kept.
                if (_unlockSpeedCheck.Checked)
                {
                    if (_settings.MaxClicksPerSecond < UnlockedMaxCps)
                    {
                        _settings.MaxClicksPerSecond = UnlockedMaxCps;
                    }
                }
                else if (_settings.MaxClicksPerSecond >= UnlockedMaxCps)
                {
                    _settings.MaxClicksPerSecond = NormalMaxCps;
                }

                // Reflect the cap in the Anti-Freeze numeric and push it to the engine.
                if (_maxCpsNum != null)
                {
                    _suppressAntiFreeze = true;
                    try { _maxCpsNum.Value = Clamp(_settings.MaxClicksPerSecond, 1, 2000); }
                    finally { _suppressAntiFreeze = false; }
                }
                ApplyAntiFreezeToEngine();

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
            // Keep the "Set exact CPS" box's ceiling in step with the slider so a typed
            // value above the current limit isn't silently snapped to a stale maximum.
            if (_exactCpsNum != null)
            {
                if (_exactCpsNum.Value > newMax)
                {
                    _exactCpsNum.Value = newMax;
                }
                _exactCpsNum.Maximum = newMax;
            }
            OnSpeedSlider();
        }

        /// <summary>
        /// The clicks-per-second ceiling actually enforced by Anti-Freeze (or "no cap"
        /// when it's off). Used to warn on the Target label when the requested rate would
        /// be silently limited below what the slider/preset asks for.
        /// </summary>
        private int EffectiveCap()
        {
            return _settings != null && _settings.AntiFreezeEnabled
                ? Math.Max(1, _settings.MaxClicksPerSecond)
                : int.MaxValue;
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
            string detail = precise > 0
                ? $"  (~{precise:0.00} ms \u00b7 {perMin}/min)"
                : $"  ({ms} ms \u00b7 {perMin}/min)";

            // A Double/Triple/Quadruple click sends 2/3/4 real clicks per actuation, so
            // this "CPS" (the PRESS rate the slider sets) is not the number of clicks the
            // game receives. The interval hint above already showed the multiplied figure,
            // which meant the same screen displayed "29 CPS" and "114.3 CPS" for the very
            // same setting, with nothing to explain the contradiction. Spell it out.
            int styleMul = (_styleCombo != null ? _styleCombo.SelectedIndex : 0) + 1;
            if (styleMul > 1)
            {
                detail += $"  \u00b7  \u00d7{styleMul} = {cps * styleMul:N0} clicks/s";
            }

            int cap = EffectiveCap();
            if (cps > cap)
            {
                detail += $"  \u2014 capped to {cap} by Anti-Freeze";
            }
            _speedLabel.AccentColor = _theme.Accent;
            _speedLabel.MutedColor = _theme.TextMuted;
            _speedLabel.SetParts(Localization.T("Target:") + " ", $"{cps} CPS", detail);

            if (_engine.IsRunning && GetSelectedMode() == ClickMode.Interval)
            {
                _engine.UpdateInterval(
                    (int)_hoursNum.Value, (int)_minutesNum.Value,
                    (int)_secondsNum.Value, (int)_millisNum.Value, precise);
            }

            // Keep the "Set exact CPS" input mirroring the current rate, no matter how
            // it changed (slider, ±, a preset or an unlock). Setting Value here doesn't
            // re-enter this method, so there's no feedback loop.
            if (_exactCpsNum != null && (int)_exactCpsNum.Value != cps)
            {
                decimal shown = cps;
                if (shown < _exactCpsNum.Minimum) shown = _exactCpsNum.Minimum;
                if (shown > _exactCpsNum.Maximum) shown = _exactCpsNum.Maximum;
                _exactCpsNum.Value = shown;
            }

            HighlightActivePreset(cps);
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

        /// <summary>
        /// Styles a button as a filled accent "primary" action (New, Humanize, Set):
        /// accent fill, white text, no border. Must be re-applied after a theme switch
        /// because ThemeManager resets every Button to the neutral surface colour.
        /// </summary>
        private void StyleAccentButton(Button b)
        {
            if (b == null) return;
            b.BackColor = _theme.Accent;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = _theme.AccentHover;
        }

        /// <summary>Sets the little icon drawn before an action button's label.</summary>
        private static void SetGlyph(Button b, ActionGlyph glyph)
        {
            if (b is RoundedButton rb) rb.Glyph = glyph;
        }

        /// <summary>
        /// Highlights the CPS preset button that matches the current rate (accent fill),
        /// leaving the others as plain surface buttons — so the active preset is obvious.
        /// </summary>
        private void HighlightActivePreset(int cps)
        {
            if (_theme == null) return;
            for (int i = 0; i < _cpsPresetBtns.Count; i++)
            {
                Button b = _cpsPresetBtns[i];
                bool active = _cpsPresetValues != null && i < _cpsPresetValues.Length && _cpsPresetValues[i] == cps;
                if (active)
                {
                    b.BackColor = _theme.Accent;
                    b.ForeColor = Color.White;
                    b.FlatAppearance.BorderSize = 0;
                }
                else
                {
                    b.BackColor = _theme.Surface2;
                    b.ForeColor = _theme.Text;
                    b.FlatAppearance.BorderSize = 1;
                    b.FlatAppearance.BorderColor = _theme.Border;
                }
            }
        }

        /// <summary>Styles a button as a filled danger (red) action, e.g. Delete.</summary>
        private void StyleDangerButton(Button b)
        {
            if (b == null) return;
            b.BackColor = _theme.Danger;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderSize = 0;
        }

        // A fixed violet for the "Humanize" action so it reads as a distinct, special
        // tool (as in the design) regardless of the current theme's accent.
        private static readonly Color HumanizePurple = Color.FromArgb(139, 92, 246);

        private void StylePurpleButton(Button b)
        {
            if (b == null) return;
            b.BackColor = HumanizePurple;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(160, 120, 250);
        }

        /// <summary>Jumps the speed slider (and the click-interval fields) to a preset CPS.</summary>
        private void ApplyCpsPreset(int cps)
        {
            if (_speedTrack == null)
            {
                return;
            }
            int v = cps;
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

            // Above 1000 CPS the whole-millisecond interval saturates at 1 ms, so the
            // real rate lives in the sub-millisecond precise value. Prefer it when
            // present so a profile saved at e.g. 2000 CPS shows 2000 on reload instead
            // of collapsing to ~1000.
            long ms = GetUiIntervalMs();
            double cps;
            if (_lastLoadedPreciseMs > 0.0)
            {
                cps = 1000.0 / _lastLoadedPreciseMs;
            }
            else
            {
                cps = ms > 0 ? 1000.0 / ms : 100.0;
            }

            int v = (int)Math.Round(cps);
            if (v < _speedTrack.Minimum) v = _speedTrack.Minimum;
            if (v > _speedTrack.Maximum) v = _speedTrack.Maximum;

            _suppressSpeedSync = true;
            try { _speedTrack.Value = v; }
            finally { _suppressSpeedSync = false; }

            string perMin = (cps * 60).ToString("N0");
            string valueText, detailText;
            if (_lastLoadedPreciseMs > 0.0)
            {
                valueText = $"{cps:0} CPS";
                detailText = $"  (~{_lastLoadedPreciseMs:0.00} ms \u00b7 {perMin}/min)";
            }
            else if (cps > _speedTrack.Maximum)
            {
                valueText = $"{cps:0.0} CPS";
                detailText = $"  ({ms} ms \u00b7 {perMin}/min)";
            }
            else
            {
                valueText = $"{v} CPS";
                detailText = $"  ({ms} ms \u00b7 {perMin}/min)";
            }
            int cap = EffectiveCap();
            if (cps > cap)
            {
                detailText += $"  — capped to {cap} by Anti-Freeze";
            }
            _speedLabel.AccentColor = _theme.Accent;
            _speedLabel.MutedColor = _theme.TextMuted;
            _speedLabel.SetParts(Localization.T("Target:") + " ", valueText, detailText);

            // This path sets the slider directly (not via OnSpeedSlider), so refresh the
            // active-preset highlight here too or it stays stuck on the startup value.
            HighlightActivePreset(v);

            // Keep the "Set exact CPS" box matching the slider after a profile load
            // (this path sets the slider directly without going through OnSpeedSlider).
            if (_exactCpsNum != null)
            {
                decimal shown = v;
                if (shown < _exactCpsNum.Minimum) shown = _exactCpsNum.Minimum;
                if (shown > _exactCpsNum.Maximum) shown = _exactCpsNum.Maximum;
                if (_exactCpsNum.Value != shown) _exactCpsNum.Value = shown;
            }

            // One-shot: clear so manual slider/interval edits use whole-ms again.
            _lastLoadedPreciseMs = 0.0;
        }

        /// <summary>Sub-ms interval from the most recently loaded profile, for slider sync.</summary>
        private double _lastLoadedPreciseMs;

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

        // Index of the "Keyboard key" item in the Button dropdown.
        private const int KeyTargetIndex = 3;

        private void OnClickTargetChanged(object sender, EventArgs e)
        {
            RefreshKeyButton();
        }

        /// <summary>Enables the "Set key" button in keyboard mode and shows the chosen key.</summary>
        private void RefreshKeyButton()
        {
            if (_setKeyBtn == null || _buttonCombo == null)
            {
                return;
            }
            bool keyMode = _buttonCombo.SelectedIndex == KeyTargetIndex;
            _setKeyBtn.Enabled = keyMode;
            _setKeyBtn.Text = keyMode && _selectedKeyVk != 0
                ? "⌨ " + KeyLabel(_selectedKeyVk)          // a key name, not prose
                : Localization.T("⌨ Set key…");
        }

        private static string KeyLabel(int vk)
        {
            try { return ((Keys)vk).ToString(); }
            catch { return "0x" + vk.ToString("X2"); }
        }

        private void OnSetAutoPressKey(object sender, EventArgs e)
        {
            int? vk = CaptureKeyModal();
            if (vk.HasValue)
            {
                _selectedKeyVk = vk.Value;
                RefreshKeyButton();
            }
        }

        /// <summary>Small modal that captures the next key press. Returns its virtual-key
        /// code, or null if cancelled (Esc).</summary>
        private int? CaptureKeyModal()
        {
            using (var dlg = new Form())
            {
                dlg.Text = Localization.T("Set auto-press key");
                dlg.FormBorderStyle = FormBorderStyle.FixedSingle;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.ShowInTaskbar = false;
                dlg.ClientSize = new System.Drawing.Size(320, 96);
                dlg.KeyPreview = true;
                if (_theme != null)
                {
                    dlg.BackColor = _theme.Background;
                    dlg.ForeColor = _theme.Text;
                }

                var lbl = new Label
                {
                    Text = Localization.T("Press the key you want Tempo to auto-press.\n(Esc to cancel)"),
                    Dock = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter
                };
                dlg.Controls.Add(lbl);

                int? captured = null;
                dlg.KeyDown += (s, ev) =>
                {
                    ev.Handled = true;
                    ev.SuppressKeyPress = true;
                    if (ev.KeyCode == Keys.Escape)
                    {
                        dlg.DialogResult = DialogResult.Cancel;
                        return;
                    }
                    captured = (int)ev.KeyCode;
                    dlg.DialogResult = DialogResult.OK;
                };

                dlg.ShowDialog(this);
                return dlg.DialogResult == DialogResult.OK ? captured : (int?)null;
            }
        }

        private ClickProfile BuildProfileFromUi()
        {
            var p = new ClickProfile
            {
                Name = string.IsNullOrWhiteSpace(_profileNameText.Text) ? "Profile" : _profileNameText.Text.Trim(),
                IntervalHours = (int)_hoursNum.Value,
                IntervalMinutes = (int)_minutesNum.Value,
                IntervalSeconds = (int)_secondsNum.Value,
                IntervalMilliseconds = (int)_millisNum.Value,
                Button = _buttonCombo.SelectedIndex == KeyTargetIndex
                    ? MouseButtonType.Left
                    : (MouseButtonType)_buttonCombo.SelectedIndex,
                Target = _buttonCombo.SelectedIndex == KeyTargetIndex ? ClickTarget.Key : ClickTarget.Mouse,
                KeyVirtualKey = _selectedKeyVk,
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

        /// <summary>Current clicker setup as JSON, for unsaved-change detection.</summary>
        private string SerializeProfileSafe()
        {
            try
            {
                return System.Text.Json.JsonSerializer.Serialize(BuildProfileFromUi());
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Shows "Unsaved changes" whenever the controls differ from the profile
        /// that was last loaded or saved. Compared by snapshot on the UI tick, so
        /// no per-control event wiring is needed and programmatic loads can never
        /// false-positive (they refresh the snapshot themselves).
        /// </summary>
        private void UpdateProfileDirty()
        {
            if (_profileDirtyLabel == null || _profileSnapshotJson == null)
            {
                return;
            }
            string cur = SerializeProfileSafe();
            bool dirty = cur != null && !string.Equals(cur, _profileSnapshotJson, StringComparison.Ordinal);
            if (_profileDirtyLabel.Visible != dirty)
            {
                _profileDirtyLabel.Visible = dirty;
            }
            // Keep the accent colour after theme switches (ThemeManager resets labels).
            if (dirty && _theme != null && _profileDirtyLabel.ForeColor != _theme.Accent)
            {
                _profileDirtyLabel.ForeColor = _theme.Accent;
            }
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

                _selectedKeyVk = p.KeyVirtualKey;
                _buttonCombo.SelectedIndex = p.Target == ClickTarget.Key
                    ? KeyTargetIndex
                    : (int)p.Button;
                RefreshKeyButton();
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
            _lastLoadedPreciseMs = p.ManualPreciseIntervalMs > 0.0 ? p.ManualPreciseIntervalMs : 0.0;
            SyncSpeedSliderFromInterval();
            UpdateIntervalHint();
            SyncHumanizeButtonFromState();   // reflect the loaded randomizer state

            // This load IS the new baseline — nothing is unsaved right now.
            _profileSnapshotJson = SerializeProfileSafe();
            if (_profileDirtyLabel != null)
            {
                _profileDirtyLabel.Visible = false;
            }
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
            if (_header != null)
            {
                _header.ProfileText = Localization.T("Profile  •  ") + name;
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
                if (_header != null)
                {
                    _header.ProfileText = Localization.T("Profile  •  ") + name;
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
            if (_header != null)
            {
                _header.ProfileText = Localization.T("Profile  •  ") + edited.Name;
            }
            ShowInfo(Localization.F("Profile '{0}' saved.", edited.Name));

            _profileSnapshotJson = SerializeProfileSafe();
            if (_profileDirtyLabel != null)
            {
                _profileDirtyLabel.Visible = false;
            }
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
                Localization.F("Delete profile '{0}'?", _currentProfileName),
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

        // ── Second cursor ("second mouse") ────────────────────────────────────

        /// <summary>One entry in the "which 2nd mouse" picker; carries the raw device path.</summary>
        private sealed class MouseComboItem
        {
            public string Raw;
            public string Display;
            public override string ToString() => Display;
        }

        private Control BuildSecondCursorGroup()
        {
            var g = UiFactory.Group(Localization.T("Second Cursor (second mouse)"), 12, 766, 696, 162, CardIcon.Target);

            _secondCursorEnableCheck = UiFactory.Check("Show a second mouse cursor", 16, 26);
            _secondCursorEnableCheck.AutoSize = true;
            _secondCursorEnableCheck.CheckedChanged += (s, e) =>
            {
                if (_suppressProfileEvents || _settings == null) { return; }
                _settings.SecondCursorEnabled = _secondCursorEnableCheck.Checked;
                Persistence.SettingsManager.Save(_settings);
                ApplySecondCursorSettings();
                RefreshMiceUi();
            };
            g.Controls.Add(_secondCursorEnableCheck);

            // Bind a second, real mouse (if one is plugged in) to the second cursor.
            _secondMouseUseCheck = UiFactory.Check("Let a 2nd real mouse control it — move, click, right-click to spam (needs 2 mice)", 16, 50);
            _secondMouseUseCheck.AutoSize = true;
            _secondMouseUseCheck.CheckedChanged += (s, e) =>
            {
                if (_suppressProfileEvents || _settings == null) { return; }
                _settings.SecondCursorUsePhysicalMouse = _secondMouseUseCheck.Checked;
                // Turning this on only makes sense with the cursor visible — switch it on too.
                if (_secondMouseUseCheck.Checked && !_secondCursorEnableCheck.Checked)
                {
                    _secondCursorEnableCheck.Checked = true; // fires its own save + apply
                }
                Persistence.SettingsManager.Save(_settings);
                ApplySecondCursorSettings();
                RefreshMiceUi();
            };
            g.Controls.Add(_secondMouseUseCheck);

            // Which physical mouse drives the second cursor.
            var whichLbl = UiFactory.Caption("Which mouse:", 16, 79);
            whichLbl.AutoSize = true;
            g.Controls.Add(whichLbl);

            _secondMouseCombo = UiFactory.Combo(108, 75, 402);
            _secondMouseCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_suppressMouseComboEvent || _settings == null) { return; }
                var item = _secondMouseCombo.SelectedItem as MouseComboItem;
                string raw = item?.Raw ?? "";
                _settings.SecondCursorMouseDeviceName = raw;
                Persistence.SettingsManager.Save(_settings);
                _secondCursor?.SetPreferredDevice(raw);
                RefreshMiceDetectedLabel();
            };
            g.Controls.Add(_secondMouseCombo);

            _miceDetectedLabel = UiFactory.Caption("", 16, 106);
            _miceDetectedLabel.ForeColor = _theme.TextMuted;
            _miceDetectedLabel.AutoSize = false;
            _miceDetectedLabel.Width = 664;
            _miceDetectedLabel.Height = 18;
            g.Controls.Add(_miceDetectedLabel);

            var hint = UiFactory.Caption("Pick the mouse above (auto-detects when you plug one in) or choose \"Ask by wiggling\". Move it to aim "
                + "the second cursor (your real pointer stays put); left/right = a real click that opens apps & hits anything; middle = start/stop spam. "
                + "Emergency-Stop halts it.", 16, 128);
            hint.ForeColor = _theme.TextMuted;
            hint.AutoSize = false;
            hint.Width = 664;
            hint.Height = 30;
            g.Controls.Add(hint);

            // Keep detection live: a plugged-in mouse shows up within ~1.5 s even before
            // the mode is on (the controller's own watch only runs while the mode is on).
            _miceRefreshTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            _miceRefreshTimer.Tick += (s, e) => RefreshMiceUi();
            _miceRefreshTimer.Start();

            RefreshMiceUi();
            return g;
        }

        /// <summary>Refreshes both the mouse picker and the status line (call on any device change).</summary>
        private void RefreshMiceUi()
        {
            RefreshMouseCombo();
            RefreshMiceDetectedLabel();
        }

        /// <summary>Rebuilds the "which mouse" dropdown from the currently-connected mice.</summary>
        private void RefreshMouseCombo()
        {
            if (_secondMouseCombo == null) { return; }
            try
            {
                var mice = Native.SecondMouseListener.EnumerateRealMice();
                // Only rebuild when the set of devices actually changed — otherwise we'd
                // fight the user's open dropdown and reset their selection every tick.
                var sig = new System.Text.StringBuilder();
                foreach (var m in mice) { sig.Append(m.DeviceName).Append('|'); }
                string chosen = _settings?.SecondCursorMouseDeviceName ?? "";
                string signature = sig.ToString() + "##" + chosen;
                if (signature == _mouseComboSig) { return; }
                _mouseComboSig = signature;

                _suppressMouseComboEvent = true;
                try
                {
                    _secondMouseCombo.Items.Clear();
                    _secondMouseCombo.Items.Add(new MouseComboItem { Raw = "", Display = "Ask by wiggling" });
                    int selectIdx = 0;
                    for (int i = 0; i < mice.Count; i++)
                    {
                        var m = mice[i];
                        string disp = m.FriendlyName;
                        // Disambiguate identical names (two same-model mice).
                        int dupes = 0;
                        for (int j = 0; j < i; j++) { if (mice[j].FriendlyName == m.FriendlyName) { dupes++; } }
                        if (dupes > 0) { disp += " (" + (dupes + 1) + ")"; }
                        _secondMouseCombo.Items.Add(new MouseComboItem { Raw = m.DeviceName, Display = disp });
                        if (string.Equals(m.DeviceName, chosen, StringComparison.OrdinalIgnoreCase))
                        {
                            selectIdx = _secondMouseCombo.Items.Count - 1;
                        }
                    }
                    _secondMouseCombo.SelectedIndex = selectIdx;
                }
                finally { _suppressMouseComboEvent = false; }
            }
            catch { }
        }

        /// <summary>Updates the "N mice detected" line under the second-cursor toggles.</summary>
        private void RefreshMiceDetectedLabel()
        {
            if (_miceDetectedLabel == null) { return; }
            try
            {
                int count = Engine.SecondCursorController.DetectedMouseCount();
                string text = "Detected: " + Engine.SecondCursorController.DetectedMouseSummary();
                if (_settings != null && _settings.SecondCursorUsePhysicalMouse)
                {
                    if (_secondCursor != null && _secondCursor.SecondMouseBound)
                    {
                        string nm = _secondCursor.SecondMouseName;
                        text += "  —  " + (nm.Length > 0 ? nm : "2nd mouse") + " is driving the cursor";
                    }
                    else if (_secondCursor != null && _secondCursor.Assigning)
                    {
                        text += "  —  now wiggle the mouse you want to use";
                    }
                    else if (count < 2)
                    {
                        text += "  —  waiting: plug in a 2nd mouse";
                    }
                    else
                    {
                        text += "  —  pick your 2nd mouse above";
                    }
                }
                _miceDetectedLabel.Text = text;
                _miceDetectedLabel.ForeColor = (count >= 2) ? _theme.Text : _theme.TextMuted;
            }
            catch { }
        }

        /// <summary>(Re)creates the second-cursor controller and pushes the saved look / spam settings into it.</summary>
        private void ApplySecondCursorSettings()
        {
            if (_settings == null)
            {
                return;
            }
            if (_secondCursor == null)
            {
                _secondCursor = new Engine.SecondCursorController();
                _secondCursor.MenuRequested += OnSecondCursorMenuRequested;
                // The controller tells us when mice come/go and when one gets bound; keep
                // the picker + status line and the saved choice in sync with reality.
                _secondCursor.MiceChanged += (s, e) => RunOnUi(RefreshMiceUi);
                _secondCursor.SecondMouseBoundChanged += (s, e) => RunOnUi(OnSecondMouseBound);
            }
            var shape = (SecondCursorShape)Math.Max(0, Math.Min(3, _settings.SecondCursorShape));
            _secondCursor.SetAppearance(shape, Color.FromArgb(_settings.SecondCursorColorArgb),
                Math.Max(50, Math.Min(250, _settings.SecondCursorScale)));
            int cps = Math.Max(1, _settings.SecondCursorSpamCps);
            _secondCursor.SetSpamSettings(
                (MouseButtonType)Math.Max(0, Math.Min(2, _settings.SecondCursorSpamButton)),
                ClickStyle.Single, Math.Max(1, 1000 / cps));
            _secondCursor.SetSecondMouseSensitivity(_settings.SecondCursorMouseSensitivity);
            _secondCursor.SetEnabled(_settings.SecondCursorEnabled);
            // Tell the controller which mouse the user chose BEFORE arming the mode, so it
            // binds that one directly instead of asking for a wiggle.
            _secondCursor.SetPreferredDevice(_settings.SecondCursorMouseDeviceName);
            // Arm the "2nd real mouse" mode if the user asked for it (needs the cursor
            // showing; it now waits for a 2nd mouse rather than hard-failing).
            _secondCursor.SetUsePhysicalMouse(_settings.SecondCursorEnabled && _settings.SecondCursorUsePhysicalMouse);
        }

        /// <summary>A mouse just got bound — persist which one so it re-binds next time.</summary>
        private void OnSecondMouseBound()
        {
            if (_secondCursor == null || _settings == null) { return; }
            string raw = _secondCursor.SecondMouseDeviceName;
            if (!string.Equals(raw, _settings.SecondCursorMouseDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                _settings.SecondCursorMouseDeviceName = raw;
                Persistence.SettingsManager.Save(_settings);
            }
            RefreshMiceUi();
        }

        /// <summary>Runs an action on the UI thread (controller events may fire from a timer/message pump).</summary>
        private void RunOnUi(Action action)
        {
            try
            {
                if (IsHandleCreated && InvokeRequired) { BeginInvoke(action); }
                else { action(); }
            }
            catch { }
        }

        /// <summary>
        /// Escapes a string that came from OUTSIDE Tempo before it goes into a menu item.
        /// A ToolStripMenuItem reads "&amp;" as the mnemonic prefix and swallows it, so a
        /// mouse whose product name contains one — combo sets are routinely called things
        /// like "Wireless Keyboard &amp; Mouse" — would lose the character and underline an
        /// arbitrary letter. Needed for every device-supplied string, and only those: text
        /// we write ourselves is escaped at the literal.
        /// </summary>
        private static string MenuText(string s)
        {
            return string.IsNullOrEmpty(s) ? s : s.Replace("&", "&&");
        }

        /// <summary>
        /// Builds and shows the second cursor's own right-click menu at the point the
        /// user clicked it: Grab &amp; place, Spam-click, Change colour, Change size.
        /// Themed to match the tray menu.
        /// </summary>
        private void OnSecondCursorMenuRequested(object sender, Engine.SecondCursorMenuEventArgs e)
        {
            if (_secondCursor == null || _settings == null)
            {
                return;
            }

            Utils.Logger.Info("[2nd cursor] menu opened.");
            // Dispose the PREVIOUS menu now (safe — it's already closed), never during
            // its own Closed event (that disposed a submenu mid-handle-create → crash).
            try { _secondCursorMenu?.Dispose(); } catch { }

            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                Font = new Font("Segoe UI", 9.75f)
            };
            _secondCursorMenu = menu;
            try
            {
                // This menu is rebuilt every time it is shown, so its renderer (and the
                // animation timer inside it) has to be released with it.
                menu.Disposed += (s, e) => (menu.Renderer as ThemedMenuRenderer)?.Dispose();
                menu.Renderer = new ThemedMenuRenderer(_theme);
                menu.BackColor = _theme.Surface;
                menu.ForeColor = _theme.Text;
            }
            catch { }

            var header = new ToolStripMenuItem("Second cursor") { Enabled = false };
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            // "&&", not "&". A ToolStripMenuItem reads a single ampersand as the mnemonic
            // prefix and EATS it, so "Grab & place" was rendering on screen as "Grab  place"
            // with a stray double space. Doubling it escapes the mnemonic.
            menu.Items.Add(_secondCursor.Placing ? "Placing… (click to drop)" : "Grab && place", null,
                (s, ev) => _secondCursor.StartPlacement());

            // Spam clicks are posted, so they are invisible: a spam that has paused itself
            // because the window it was aimed at went away looks exactly like one that is
            // working. This menu is the thing the user opens to find out, so it answers.
            string spamText = "Spam-click here";
            if (_secondCursor.Spamming)
            {
                string paused = _secondCursor.SpamPausedReason;
                spamText = paused.Length > 0 ? "Stop spam-click  ·  paused: " + paused : "Stop spam-click";
            }
            var spam = new ToolStripMenuItem(spamText)
            { Checked = _secondCursor.Spamming, CheckOnClick = false };
            spam.Click += (s, ev) => _secondCursor.ToggleSpam();
            menu.Items.Add(spam);

            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("Change colour…", null, (s, ev) =>
            {
                using (var dlg = new ColorDialog { Color = Color.FromArgb(_settings.SecondCursorColorArgb), FullOpen = true })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        _settings.SecondCursorColorArgb = dlg.Color.ToArgb();
                        Persistence.SettingsManager.Save(_settings);
                        ApplySecondCursorSettings();
                    }
                }
            });

            // Change size — a small submenu of presets, current one checked.
            //
            // The parent items below all carry their CURRENT value. A submenu that only
            // reveals what is selected once you open it makes the reader hunt through four
            // of them to answer "what is this set to?" — and this menu exists precisely to
            // adjust those things quickly.
            var sizeItem = new ToolStripMenuItem(Localization.T("Change size"));
            int[] sizes = { 75, 100, 150, 200, 250 };
            string[] names = { "Small", "Normal", "Large", "Bigger", "Huge" };
            string sizeNow = _settings.SecondCursorScale + "%";
            for (int i = 0; i < sizes.Length; i++)
            {
                int sz = sizes[i];
                string sizeName = Localization.T(names[i]);
                if (_settings.SecondCursorScale == sz) { sizeNow = sizeName + " (" + sz + "%)"; }
                var it = new ToolStripMenuItem(sizeName + "  (" + sz + "%)")
                { Checked = _settings.SecondCursorScale == sz, CheckOnClick = false };
                it.Click += (s, ev) =>
                {
                    _settings.SecondCursorScale = sz;
                    Persistence.SettingsManager.Save(_settings);
                    ApplySecondCursorSettings();
                };
                sizeItem.DropDownItems.Add(it);
            }
            sizeItem.Text = Localization.F("Change size  ·  {0}", sizeNow);
            menu.Items.Add(sizeItem);

            // Change shape.
            var shapeItem = new ToolStripMenuItem(Localization.T("Change shape"));
            string[] shapeNames = { "Arrow", "Ring", "Crosshair", "Dot" };
            for (int i = 0; i < shapeNames.Length; i++)
            {
                int idx = i;
                var it = new ToolStripMenuItem(Localization.T(shapeNames[i]))
                { Checked = _settings.SecondCursorShape == idx, CheckOnClick = false };
                it.Click += (s, ev) =>
                {
                    _settings.SecondCursorShape = idx;
                    Persistence.SettingsManager.Save(_settings);
                    ApplySecondCursorSettings();
                };
                shapeItem.DropDownItems.Add(it);
            }
            int shapeNow = _settings.SecondCursorShape;
            shapeItem.Text = Localization.F("Change shape  ·  {0}", Localization.T(
                shapeNow >= 0 && shapeNow < shapeNames.Length ? shapeNames[shapeNow] : shapeNames[0]));
            menu.Items.Add(shapeItem);

            // Spam speed.
            var speedItem = new ToolStripMenuItem(Localization.T("Spam speed"));
            int[] cpsOpts = { 5, 10, 20, 50, 100 };
            foreach (int cps in cpsOpts)
            {
                int c = cps;
                var it = new ToolStripMenuItem(c + " CPS")
                { Checked = _settings.SecondCursorSpamCps == c, CheckOnClick = false };
                it.Click += (s, ev) =>
                {
                    _settings.SecondCursorSpamCps = c;
                    Persistence.SettingsManager.Save(_settings);
                    ApplySecondCursorSettings();
                };
                speedItem.DropDownItems.Add(it);
            }
            speedItem.Text = Localization.F("Spam speed  ·  {0} CPS", _settings.SecondCursorSpamCps);
            menu.Items.Add(speedItem);

            // ── second physical mouse ──
            menu.Items.Add(new ToolStripSeparator());
            int miceCount = Engine.SecondCursorController.DetectedMouseCount();
            menu.Items.Add(new ToolStripMenuItem(MenuText(Engine.SecondCursorController.DetectedMouseSummary())) { Enabled = false });

            // With only one mouse the controller deliberately ARMS and waits for a second to
            // be plugged in, rather than refusing. That is the right behaviour, but the menu
            // said nothing about it: you ticked the item, the cursor carried on ignoring
            // your mouse, and the only explanation was a line in the log. Say it here.
            string use2ndText = _settings.SecondCursorUsePhysicalMouse
                ? (miceCount < 2 ? "Waiting for a 2nd mouse — click to stop" : "Stop using my 2nd mouse")
                : (miceCount < 2 ? "Use my 2nd mouse — plug one in first (experimental)"
                                 : "Use my 2nd mouse (experimental)");
            var use2nd = new ToolStripMenuItem(use2ndText)
            {
                Checked = _settings.SecondCursorUsePhysicalMouse,
                CheckOnClick = false
            };
            use2nd.Click += (s, ev) =>
            {
                bool now = !_settings.SecondCursorUsePhysicalMouse;
                _settings.SecondCursorUsePhysicalMouse = now;
                if (now) { _settings.SecondCursorEnabled = true; }
                Persistence.SettingsManager.Save(_settings);
                bool prev = _suppressProfileEvents;
                _suppressProfileEvents = true;
                try
                {
                    if (_secondCursorEnableCheck != null) { _secondCursorEnableCheck.Checked = _settings.SecondCursorEnabled; }
                    if (_secondMouseUseCheck != null) { _secondMouseUseCheck.Checked = now; }
                }
                finally { _suppressProfileEvents = prev; }
                ApplySecondCursorSettings();
                RefreshMiceUi();
            };
            menu.Items.Add(use2nd);

            // Choose which physical mouse — real product names, current one checked.
            var chooseItem = new ToolStripMenuItem("Choose 2nd mouse");
            string chosenRaw = _settings.SecondCursorMouseDeviceName ?? "";
            var wiggleEntry = new ToolStripMenuItem("Ask by wiggling")
            { Checked = chosenRaw.Length == 0, CheckOnClick = false };
            wiggleEntry.Click += (s, ev) =>
            {
                _settings.SecondCursorMouseDeviceName = "";
                Persistence.SettingsManager.Save(_settings);
                _secondCursor.SetPreferredDevice("");
                _secondCursor.RepickSecondMouse();
                RefreshMiceUi();
            };
            chooseItem.DropDownItems.Add(wiggleEntry);
            chooseItem.DropDownItems.Add(new ToolStripSeparator());
            foreach (var m in Native.SecondMouseListener.EnumerateRealMice())
            {
                string raw = m.DeviceName;
                var it = new ToolStripMenuItem(MenuText(m.FriendlyName))
                { Checked = string.Equals(raw, chosenRaw, StringComparison.OrdinalIgnoreCase), CheckOnClick = false };
                it.Click += (s, ev) =>
                {
                    _settings.SecondCursorMouseDeviceName = raw;
                    if (!_settings.SecondCursorUsePhysicalMouse) { _settings.SecondCursorUsePhysicalMouse = true; }
                    _settings.SecondCursorEnabled = true;
                    Persistence.SettingsManager.Save(_settings);
                    bool prev = _suppressProfileEvents;
                    _suppressProfileEvents = true;
                    try
                    {
                        if (_secondCursorEnableCheck != null) { _secondCursorEnableCheck.Checked = true; }
                        if (_secondMouseUseCheck != null) { _secondMouseUseCheck.Checked = true; }
                    }
                    finally { _suppressProfileEvents = prev; }
                    ApplySecondCursorSettings();
                    RefreshMiceUi();
                };
                chooseItem.DropDownItems.Add(it);
            }
            menu.Items.Add(chooseItem);

            // 2nd-mouse movement speed (only meaningful when the mode can run).
            if (_settings.SecondCursorUsePhysicalMouse || miceCount >= 2)
            {
                var speed2nd = new ToolStripMenuItem(Localization.T("2nd mouse speed"));
                (int pct, string name)[] speeds =
                {
                    (50, "Slower"), (75, "Slow"), (100, "Normal"), (150, "Fast"), (250, "Faster"),
                };
                foreach (var opt in speeds)
                {
                    int pct = opt.pct;
                    var it = new ToolStripMenuItem(Localization.T(opt.name) + "  (" + pct + "%)")
                    { Checked = _settings.SecondCursorMouseSensitivity == pct, CheckOnClick = false };
                    it.Click += (s, ev) =>
                    {
                        _settings.SecondCursorMouseSensitivity = pct;
                        Persistence.SettingsManager.Save(_settings);
                        ApplySecondCursorSettings();
                    };
                    speed2nd.DropDownItems.Add(it);
                }
                string sens = _settings.SecondCursorMouseSensitivity + "%";
                foreach (var opt in speeds)
                {
                    if (opt.pct == _settings.SecondCursorMouseSensitivity)
                    {
                        sens = Localization.T(opt.name) + " (" + opt.pct + "%)";
                    }
                }
                speed2nd.Text = Localization.F("2nd mouse speed  ·  {0}", sens);
                menu.Items.Add(speed2nd);
            }

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Hide second cursor", null, (s, ev) =>
            {
                _settings.SecondCursorEnabled = false;
                Persistence.SettingsManager.Save(_settings);
                if (_secondCursorEnableCheck != null) { _secondCursorEnableCheck.Checked = false; }
                ApplySecondCursorSettings();
            });

            // Show the menu and force its OWN popup foreground so its item clicks
            // register — WITHOUT popping Tempo's main window. When Tempo isn't the
            // active app, Windows blocks SetForegroundWindow (the "foreground lock"),
            // which is why item clicks were being treated as outside-clicks and the
            // menu just closed. The documented workaround is to briefly attach our input
            // thread to the currently-foreground window's thread, which lifts the lock.
            menu.Show(new Point(e.X, e.Y));
            try
            {
                IntPtr fg = GetForegroundWindow();
                uint fgThread = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out _) : 0;
                uint myThread = GetCurrentThreadId();
                bool attached = fgThread != 0 && fgThread != myThread && AttachThreadInput(myThread, fgThread, true);
                if (menu.Handle != IntPtr.Zero)
                {
                    SetForegroundWindow(menu.Handle);
                    BringWindowToTop(menu.Handle);
                }
                if (attached) { AttachThreadInput(myThread, fgThread, false); }
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        private ContextMenuStrip _secondCursorMenu;

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
            // IGNORE the hotkey while Tempo is in the tray.
            //
            // The picker takes over the whole screen and then writes the coordinate into
            // controls on a window the user cannot see, so pressing it from the tray
            // "worked" but produced nothing visible — and the restore afterwards brought
            // the window back half-painted. There is nothing to pick INTO when the window
            // isn't up, so the press is dropped and logged rather than acted on.
            if (!Visible || WindowState == FormWindowState.Minimized)
            {
                Utils.Logger.Info("[UI] Pick-position hotkey ignored — Tempo is in the tray.");
                return;
            }

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

                    // Rebuild what Hide() threw away. A bare Show() left the window in
                    // the state the user reported: the tab page's backdrop composite was
                    // never rebuilt, and because a wallpaper flips every label, checkbox
                    // and panel to a TRANSPARENT background, there was nothing painting
                    // behind them — so the window came back as a hole with the desktop
                    // showing through and only a few controls visible.
                    //
                    // This is the same repair the tray-restore path already does; the
                    // picker simply never called it.
                    TryRepairLayoutNow();
                    InvalidateBackdropSurfaces();
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

            _statusState.Text = Localization.F("Interval: {0} ms", GetUiIntervalMs());
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
