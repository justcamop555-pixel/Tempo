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
            // Sized from their captions, with the row's spare room at the end shared out so
            // the icons fit too: "Nuevo" and "Guardar" were 3 and 8 px short and, until the
            // buttons learned to drop an icon rather than cut a word, painted "Guar…".
            PackGlyphRow(300, 708, 6, _newProfileBtn, _saveProfileBtn, _duplicateProfileBtn, _deleteProfileBtn);

            var nameLabel = UiFactory.Label(Localization.T("Name:"), 12, 50, FontStyle.Bold);
            nameLabel.Text = nameLabel.Text.Replace(":", string.Empty).ToUpperInvariant();
            _profileNameText = UiFactory.Text(70, 47, 220);
            // The details dialog's limit. This box had none, so a name could be as long as the
            // user cared to type — and the card, the tray and the header then cut it anyway.
            _profileNameText.MaxLength = ClickProfile.MaxNameLength;

            // Mirrors the Keybinds tab: edits live only in these controls until Save. Switching
            // profiles asks about them (or, from a hotkey, keeps them for when the profile is
            // chosen again) — see SwitchToProfile.
            _profileDirtyLabel = new Label
            {
                Text = Localization.T("\u25CF Unsaved changes \u2014 click Save"),
                AutoSize = true,
                Location = new Point(300, 51),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = _theme.AccentText,
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

            // "Keyboard", not "Keyboard key": beside Left / Right / Middle it says the same, and the
            // longer words ended in "…" in the 90 px box in every language, English included
            // ("Tasto della tastiera" needed 104 px of 66). The index is what is read, never the text.
            var buttonCaption = UiFactory.Caption("Button", 16, 28);
            clickGroup.Controls.Add(buttonCaption);
            _buttonCombo = UiFactory.Combo(16, 46, 90, Utils.Localization.T("Left"), Utils.Localization.T("Right"), Utils.Localization.T("Middle"), Utils.Localization.T("Keyboard"));
            _buttonCombo.SelectedIndexChanged += OnClickTargetChanged;
            clickGroup.Controls.Add(_buttonCombo);

            var typeCaption = UiFactory.Caption("Type", 116, 28);
            clickGroup.Controls.Add(typeCaption);
            _styleCombo = UiFactory.Combo(116, 46, 100, Utils.Localization.T("Single"), Utils.Localization.T("Double"), Utils.Localization.T("Triple"), Utils.Localization.T("Quadruple"));
            _styleCombo.SelectedIndexChanged += (s, e) => { UpdateIntervalHint(); RepaintSpeedLabel(); };
            clickGroup.Controls.Add(_styleCombo);

            var modeCaption = UiFactory.Caption("Mode", 216, 28);
            clickGroup.Controls.Add(modeCaption);
            _modeCombo = UiFactory.Combo(216, 46, 100, Utils.Localization.T("Interval"), Utils.Localization.T("Hold"), Utils.Localization.T("Burst"));
            _modeCombo.SelectedIndexChanged += OnModeChanged;
            clickGroup.Controls.Add(_modeCombo);

            // Each box as wide as its own widest choice, packed left to right with their captions:
            // Italian "Tieni premuto" needed 82 px in the 76 the Mode box left for it.
            if (_buttonCombo is FlatComboBox b0 && _styleCombo is FlatComboBox b1 && _modeCombo is FlatComboBox b2)
            {
                b0.Width = b0.WidthForChoices96(86);
                b1.Left = b0.Right + 6;
                b1.Width = b1.WidthForChoices96(90);
                b2.Left = b1.Right + 6;
                b2.Width = b2.WidthForChoices96(90);
                typeCaption.Left = b1.Left;
                modeCaption.Left = b2.Left;
            }

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

            // One line ending in "…" when it runs long, with the whole estimate on hover —
            // never a second line cut off by the 16 px box ("≈ 600 cliques" is wider than
            // 74 px in Portuguese, and a big count is wider in every language). Both change
            // with every keystroke, so the start-up fitter cannot size them.
            foreach (Label est in new[] { _repeatCountEstLabel, _repeatDurationEstLabel })
            {
                est.AutoEllipsis = true;
                TipStyle.FollowText(est);
            }

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
            // It reads "✓ Humanize" while on (see UpdateHumanizeButton): measured both ways.
            LayoutFitter.DeclareCaptions(_humanizeBtn, Localization.T("Humanize"), "✓ " + Localization.T("Humanize"));
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
                ForeColor = _theme.AccentText,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };

            _startBtn = UiFactory.Button("▶  Start", 356, 472, 124, 48);
            _startBtn.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            _startBtn.BackColor = _theme.Success;
            // Not Color.White: Success is a mid-to-light green in most palettes, so a
            // white label came out at 1.31:1 in the worst theme and below AA in ALL 38.
            // ReadableOn picks black or white against the actual fill.
            _startBtn.ForeColor = Theme.ReadableOn(_theme.Success);
            _startBtn.FlatAppearance.BorderSize = 0;
            _startBtn.Click += (s, e) => OnStartOrPauseClicked();

            _stopBtn = UiFactory.Button("■  Stop", 488, 472, 124, 48);
            _stopBtn.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            _stopBtn.BackColor = _theme.Danger;
            _stopBtn.ForeColor = Theme.ReadableOn(_theme.Danger);
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
                // The suppression guards every other settings handler has, and this one lacked:
                // LoadAntiFreezeIntoUi() writes this box from the settings file (a start, a reset,
                // an import, a profile switch), which fired this handler and SAVED the file in the
                // middle of loading it. The value written back happened to match, so nothing broke
                // — but a handler that writes to disk while settings are being read into the UI is
                // one reordering away from persisting a half-applied state.
                if (_settings == null || _suppressSettingsEvents || _suppressAntiFreeze) { return; }
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

            // ── Windows shortcuts ──────────────────────────────────────────────
            // Settings ▸ Behaviour's "Hold back Windows shortcuts…", here where a run is started
            // (the same setting: SetGuardWindowsShortcuts). In the right-hand column's gap under
            // Anti-Freeze, its bottom level with Manual Speed's.
            var guardGroup = UiFactory.Group(Localization.T("Windows Shortcuts"), 384, 668, 324, 62, CardIcon.Keyboard);
            _clickerGuardCheck = MakeGuardShortcutsCheck(16, 28, "Clicker tab");
            guardGroup.Controls.Add(_clickerGuardCheck);

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
            page.Controls.Add(guardGroup);

            Control secondCursorGroup = BuildSecondCursorGroup();
            page.Controls.Add(secondCursorGroup);
            Control bgClickGroup = BuildBackgroundClickGroup();
            Control soundGroup = BuildClickSoundGroup();
            // One row, one height: whichever card's words need more, both get. And under the
            // Second Cursor card however tall its words made it (10 px, the gap it always had).
            int bottomRowHeight = Math.Max(bgClickGroup.Height, soundGroup.Height);
            bgClickGroup.Height = bottomRowHeight;
            soundGroup.Height = bottomRowHeight;
            bgClickGroup.Top = secondCursorGroup.Bottom + 10;
            soundGroup.Top = secondCursorGroup.Bottom + 10;
            page.Controls.Add(bgClickGroup);
            page.Controls.Add(soundGroup);

            // Footer hint bar (matches the design): a faint info line under the cards.
            //
            // It used to sit at y=904, which is INSIDE the Second Cursor card's band
            // (766-928). LayoutFitter saw the collision and did its job — it clamped
            // the tip to 174px — so the line has been rendering as
            // "Tip: Press F6 to start/stop    •    All t…" in every language since the
            // card was added. It belongs below the last card, not on top of it.
            //
            // And it names the key that is actually bound: it said "Press F6" whatever the Keybinds tab
            // held, so rebinding Start / Stop, unbinding it or switching to Hold-to-click left the tip
            // telling people to press a key that did something else or nothing. Refreshed with the
            // other key hints (UpdateStartButtonHotkeyHint).
            // Below the row it follows, wherever that ends. It was fixed at 1070 when the row
            // ended at 1060; the row grew to 1074 and the line's top ran into the cards' border.
            _clickerTipLabel = new Label
            {
                Text = ClickerFooterTip(),
                Left = 14,
                Top = bgClickGroup.Bottom + 8,
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = _theme.TextMuted,
                BackColor = Color.Transparent
            };
            page.Controls.Add(_clickerTipLabel);

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
                    _humanizeBtn.ForeColor = Theme.ReadableOn(HumanizePurple);
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
            // The Multi-Point tab's "about N per cycle" is built from the same interval, hold and
            // style — but it was only ever recomputed when the point LIST changed, so editing any of
            // them left that line describing the old rate. This is the one path all of those edits
            // already go through. First, so none of this method's early returns can skip it.
            UpdateCycleInfo();

            if (_intervalHint == null)
            {
                return;
            }

            long ms = GetUiIntervalMs();
            if (ms < 1) ms = 1;

            // Real clicks per press: 2 for a double click, and 1 for a keyboard key whatever the
            // (then irrelevant) click-type box still says.
            int styleFactor = ClicksPerPressUi();

            ClickMode mode = GetSelectedMode();

            // Built in pieces, so a line too long for its card can shed its least useful piece
            // instead of running off the edge: LEAD, the rate (accent-coloured); DETAIL, the
            // interval restated (it is in the boxes right above); then the cap and the hold,
            // which are warnings and are kept to the last.
            string lead;
            string shortLead = null;   // the rate without its working, for when nothing else is left to drop
            string detail;

            // The fastest rate this line is about to claim, for the Anti-Freeze check
            // below. Set in every branch so the check can never read a stale zero.
            double claimedCps;

            // Every sentence below goes through Localization. They were interpolated English, so
            // the most-read line on the Clicker tab stayed English in all six languages — and no
            // translation scan could see it, because nothing was ever offered for translation.
            // The "   ·   " separator stays OUTSIDE the translated pieces.
            const string Sep = "   ·   ";
            if (mode == ClickMode.Burst)
            {
                // Average rate over a whole burst-plus-pause cycle.
                long burst = _burstSizeNum != null ? (long)_burstSizeNum.Value : 1;
                if (burst < 1) burst = 1;
                long pause = _burstPauseNum != null ? (long)_burstPauseNum.Value : 0;
                double cycleMs = burst * ms + pause;
                double avgCps = cycleMs > 0 ? burst * styleFactor * 1000.0 / cycleMs : 0;
                lead = Localization.F("≈ {0} CPS avg", avgCps.ToString("0.0"));
                detail = Sep + Localization.F("burst {0} / {1} ms", burst.ToString("N0"), pause.ToString("N0"));
                // Inside a burst the clicks come at the interval, and that is what the
                // cap acts on — not the average across the pause.
                claimedCps = 1000.0 / ms * styleFactor;
            }
            else
            {
                double cps = 1000.0 / ms * styleFactor;
                claimedCps = cps;

                bool rand = _randIntervalCheck != null && _randIntervalCheck.Checked;
                long jitter = (_intervalJitterNum != null) ? (long)_intervalJitterNum.Value : 0;

                if (rand && jitter > 0)
                {
                    long lo = Math.Max(1, ms - jitter);
                    long hi = ms + jitter;
                    double cpsHi = 1000.0 / lo * styleFactor;   // shortest delay = fastest
                    double cpsLo = 1000.0 / hi * styleFactor;   // longest delay = slowest
                    // Numbers and units only — nothing here to translate.
                    lead = "≈ " + cpsLo.ToString("0.0") + "–" + cpsHi.ToString("0.0") + " CPS";
                    detail = Sep + ms.ToString("N0") + " ± " + jitter.ToString("N0") + " ms";
                    claimedCps = cpsHi;                          // the cap bites at the top
                }
                else if (cps >= 1)
                {
                    // Say WHICH rate this is when a multi-click style is inflating it.
                    // Bare "114.3 CPS" next to Manual Speed's "29 CPS" (same interval,
                    // Quadruple) read as a straight contradiction.
                    if (styleFactor > 1)
                    {
                        lead = Localization.F("≈ {0}/s × {1} = {2} clicks/s",
                            (1000.0 / ms).ToString("0.0"), styleFactor, cps.ToString("0.0"));
                        shortLead = "≈ " + cps.ToString("0.0") + " CPS";
                    }
                    else
                    {
                        lead = "≈ " + cps.ToString("0.0") + " CPS";
                    }
                    detail = Sep + Localization.F("{0} ms between clicks", ms.ToString("N0"));
                }
                else
                {
                    lead = Localization.F("1 click every {0} s", (ms / 1000.0).ToString("0.0"));
                    detail = Sep + ms.ToString("N0") + " ms";
                }
            }

            // Anti-Freeze will clamp anything above its cap. The Manual Speed "Target:"
            // label has always said so; THIS line — the primary way a rate gets set —
            // never did, so the two sat on the same page contradicting each other, and
            // the one people type into was the one that lied.
            // Kept SHORT. This line lives inside a 360px card and already carries the rate
            // and the interval; the long form ("capped to 300 CPS by Anti-Freeze") was cut
            // off at the card edge, which is how the Manual Speed label had been failing
            // unnoticed. The Anti-Freeze card beside it spells the rest out.
            string tail = "";
            int afCap = EffectiveCap();
            if (claimedCps > afCap)
            {
                tail += Localization.F("   ·   max {0}", afCap);
            }

            // If each click is held down, note it — and warn if the hold is long
            // enough to cap the rate you asked for. A press of a double click holds TWICE (each
            // click is held), so it is the whole press that has to fit inside the interval; the
            // warning compared a single hold and stayed quiet while a double click was capped.
            long hold = _holdMsNum != null ? (long)_holdMsNum.Value : 0;
            if (hold > 0)
            {
                tail += Sep + Localization.F("hold {0} ms", hold.ToString("N0"));
                if (mode != ClickMode.Burst && hold * styleFactor >= ms)
                {
                    tail += " " + Localization.T("(caps rate)");
                }
            }

            // The rate in the accent colour, the rest muted.
            if (_intervalHint is SpeedTargetLabel seg)
            {
                seg.AccentColor = _theme.Accent;
                seg.MutedColor = _theme.TextMuted;

                // The first version that fits: all of it; then without the interval restated; then
                // the bare rate. Nothing here wraps or ellipsizes, and with a hold this line was
                // 341 px in English (about 400 in French and German) in a 336 px label — the
                // "(caps rate)" warning at its end was the part cut off.
                string rest = detail + tail;
                if (!seg.Fits("", lead, rest))
                {
                    rest = tail;
                    if (shortLead != null && !seg.Fits("", lead, rest))
                    {
                        lead = shortLead;
                    }
                }
                seg.SetParts("", lead, rest);
            }
            else
            {
                _intervalHint.Text = lead + detail + tail;
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

            // The real time between presses, so the estimates stay honest. A press can't finish
            // faster than its hold — and a double click holds twice, once per click. Anti-Freeze
            // spaces presses out to its cap (which counts clicks). These estimates ignored both
            // the cap and the click type's holds, and in Burst mode ignored the pause between
            // bursts: 1,000 presses in bursts of 10 at 10 ms with a 1 s pause was "≈ 10.0 s"
            // for a run that takes 110.
            long hold = _holdMsNum != null ? (long)_holdMsNum.Value : 0;
            int perPress = ClicksPerPressUi();
            double capGap = 1000.0 * perPress / EffectiveCap();
            double period = Math.Max(Math.Max(ms, hold * perPress), capGap);
            if (GetSelectedMode() == ClickMode.Burst)
            {
                // Averaged over a burst plus its pause — the same cycle the rate line uses.
                long burst = _burstSizeNum != null ? Math.Max(1, (long)_burstSizeNum.Value) : 1;
                long pause = _burstPauseNum != null ? (long)_burstPauseNum.Value : 0;
                period = (burst * period + Math.Max(pause, capGap)) / burst;
            }

            long count = (long)_repeatCountNum.Value;
            double seconds = count * period / 1000.0;
            _repeatCountEstLabel.Text = "≈ " + FormatShortDuration(seconds);

            int dur = (int)_repeatDurationNum.Value;
            double clicks = dur * 1000.0 / period * perPress;
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

                // The labels were painted (by ApplySpeedUnlock) BEFORE the cap moved, and the
                // suppressed numeric above doesn't repaint them — so unlocking left the rate line
                // saying "max 200" under a 2,000 cap.
                UpdateIntervalHint();
                RepaintSpeedLabel();

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

        /// <summary>
        /// Real clicks one press sends with the Clicker's current settings: 2 for a double click,
        /// 4 for a quadruple — and 1 for a keyboard key, because the engine presses the key once
        /// whatever the click-type box (still filled in beside it) says. Every rate on this tab,
        /// and the Anti-Freeze cap, counts these clicks.
        /// </summary>
        private int ClicksPerPressUi()
        {
            if (_buttonCombo != null && _buttonCombo.SelectedIndex == KeyTargetIndex)
            {
                return 1;
            }
            int n = (_styleCombo != null ? _styleCombo.SelectedIndex : 0) + 1;
            return n < 1 ? 1 : n;
        }

        // What the Target label says before the click-type multiplier and the Anti-Freeze cap are
        // applied — kept so a change to either can repaint the label WITHOUT going back through
        // the slider. Re-deriving the slider from the interval instead would round a profile
        // loaded at 2,000 CPS down to 1,000 (its whole-millisecond interval is 1 ms).
        private string _speedValueText = "";
        private string _speedDetailText = "";
        private double _speedPressCps;

        /// <summary>Sets the Target label's base text (the press rate) and paints it.</summary>
        private void PaintSpeedLabel(string valueText, string detailText, double pressCps)
        {
            _speedValueText = valueText;
            _speedDetailText = detailText;
            _speedPressCps = pressCps;
            RepaintSpeedLabel();
        }

        /// <summary>
        /// Paints the Target label from its base text, the click type and the Anti-Freeze cap.
        /// Changing the click type or the cap used to leave this label describing the old ones,
        /// because only the slider ever repainted it — and typing an interval repainted it
        /// WITHOUT the "×4 = … clicks/s" part at all.
        /// </summary>
        private void RepaintSpeedLabel()
        {
            // Nothing painted yet (a click-type change while the tab is still being built): the
            // slider's own first paint is still to come, and would find "Target: " with no value.
            if (_speedLabel == null || _theme == null || string.IsNullOrEmpty(_speedValueText))
            {
                return;
            }

            // A Double/Triple/Quadruple click sends 2/3/4 real clicks per press, so this "CPS"
            // (the PRESS rate the slider sets) is not the number of clicks the game receives.
            // The interval hint above shows the multiplied figure, which meant the same screen
            // displayed "29 CPS" and "114.3 CPS" for the very same setting, with nothing to
            // explain the contradiction. Spell it out.
            string prefix = Localization.T("Target:") + " ";
            string detail = _speedDetailText;
            int perPress = ClicksPerPressUi();
            if (perPress > 1)
            {
                // What it adds up to matters more than the bracketed interval and per-minute figures
                // (the interval is in the boxes above), and together they were 365 px in this 336 px
                // label — "×2 = 180 clicks/s" was the part cut off, in every language.
                string times = Localization.F("×{0} = {1} clicks/s",
                    perPress, (_speedPressCps * perPress).ToString("N0"));
                detail = _speedLabel.Fits(prefix, _speedValueText, detail + "  ·  " + times)
                    ? detail + "  ·  " + times
                    : "  (" + times + ")";
            }

            // REPLACES the ms/per-minute detail rather than following it: this label is ~330px
            // inside a 360px card and the base text already fills it, so an appended warning was
            // cut off mid-word. The cap counts CLICKS (as the engine does), so a quadruple click
            // is capped at a quarter of the press rate — shown here in this label's own unit,
            // presses, so "Target: 100 CPS (max 50 CPS)" reads as what will actually happen. It
            // compared the PRESS rate with the click cap, and said nothing while a quadruple click
            // at 100 was being cut to 50.
            int cap = EffectiveCap();
            if (_speedPressCps * perPress > cap)
            {
                detail = Localization.F("  (max {0} CPS · Anti-Freeze)", Math.Max(1, cap / perPress));
            }
            _speedLabel.AccentColor = _theme.Accent;
            _speedLabel.MutedColor = _theme.TextMuted;
            _speedLabel.SetParts(prefix, _speedValueText, detail);
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

            // The click-type multiplier and the Anti-Freeze cap are added by RepaintSpeedLabel,
            // which the click type and the cap also call when THEY change.
            PaintSpeedLabel($"{cps} CPS", detail, cps);

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
            b.ForeColor = _theme.OnAccent;   // readable on ANY accent - see Theme.OnAccent
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = _theme.AccentHover;
        }

        /// <summary>Sets the little icon drawn before an action button's label.</summary>
        private static void SetGlyph(Button b, ActionGlyph glyph)
        {
            if (b is RoundedButton rb) rb.Glyph = glyph;
        }

        /// <summary>
        /// Lays a row of glyph buttons out left to right from <paramref name="left"/>, widening
        /// each to fit its icon AND its caption for as long as the whole row still ends by
        /// <paramref name="maxRight"/>. A button that cannot be widened keeps its designed
        /// width and draws its caption without the icon (RoundedButton.GlyphFits) — never the
        /// other way round. In English every button already fits, so nothing moves.
        /// </summary>
        private void PackGlyphRow(int left, int maxRight, int gap, params Button[] row)
        {
            if (row == null || row.Length == 0) { return; }
            var widths = new int[row.Length];
            int total = gap * (row.Length - 1);
            for (int i = 0; i < row.Length; i++) { widths[i] = row[i].Width; total += widths[i]; }

            for (int i = 0; i < row.Length; i++)
            {
                if (!(row[i] is RoundedButton rb)) { continue; }
                // What the button paints is screen pixels; the row is in the window's units (100% ones
                // while it is built): a 150% laptop widened these by half again.
                int extra = (int)Math.Ceiling(rb.CaptionWidthWanted() * LayoutUnitsPerPixel) - widths[i];
                if (extra > 0 && left + total + extra <= maxRight)
                {
                    widths[i] += extra;
                    total += extra;
                }
            }

            int x = left;
            for (int i = 0; i < row.Length; i++)
            {
                row[i].Left = x;
                row[i].Width = widths[i];
                x += widths[i] + gap;
            }
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
                    b.ForeColor = _theme.OnAccent;
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
            b.ForeColor = Theme.ReadableOn(_theme.Danger);
            b.FlatAppearance.BorderSize = 0;
        }

        // A fixed violet for the "Humanize" action so it reads as a distinct, special
        // tool (as in the design) regardless of the current theme's accent.
        private static readonly Color HumanizePurple = Color.FromArgb(139, 92, 246);

        private void StylePurpleButton(Button b)
        {
            if (b == null) return;
            b.BackColor = HumanizePurple;
            b.ForeColor = Theme.ReadableOn(HumanizePurple);
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
            PaintSpeedLabel(valueText, detailText, cps);

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

            // A Reset or an Import can change the cap, and the boxes above were filled with
            // their events suppressed: bring the lines that quote it up to date.
            UpdateIntervalHint();
            RepaintSpeedLabel();
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

            // Everything that quotes the cap: the rate line's "max N", the Target label, the
            // repeat estimates and the Multi-Point cycle time (UpdateIntervalHint does the last
            // two). Only the Multi-Point line used to hear about it, so turning Anti-Freeze off
            // left "max 200" on the rate line until some other field was touched.
            UpdateIntervalHint();
            RepaintSpeedLabel();

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

            // A key press is one "click" whatever the click-type box says, so switching to or
            // from a key changes every rate on the tab.
            UpdateIntervalHint();
            RepaintSpeedLabel();
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

            // Pick the Start/Stop key here and the clicker stops itself on its first press
            // (measured: started and stopped 6 ms apart). The Keybinds tab carries the full
            // notice; this card has no room for a line of its own, so the warning rides on
            // the button's tooltip — where the key is actually chosen.
            if (_keyClashTip != null)
            {
                HotkeyAction? clash = keyMode && _selectedKeyVk != 0
                    ? ActionFiredBy(_selectedKeyVk, false, false, false, false)
                    : null;
                // Through TipStyle: the button's help tip is a second component, and the two
                // used to pop up together, one on top of the other.
                TipStyle.Set(_keyClashTip, _setKeyBtn, clash.HasValue
                    ? Localization.F(
                        "⚠ {0} is also bound to “{1}”. Tempo's own key presses fire its own hotkeys, "
                        + "so every press would run that action too. Pick another key, or rebind it "
                        + "on the Keybinds tab.",
                        KeyLabel(_selectedKeyVk), HotkeyActions.LabelFor(clash.Value))
                    : "");
            }
            RefreshSelfPressedKeyNotice();
        }

        /// <summary>Carries the "this key is also a hotkey" warning on the Set-key button.</summary>
        private readonly ToolTip _keyClashTip = new ToolTip
        {
            AutoPopDelay = 20000,
            InitialDelay = 350,
            ReshowDelay = 150,
            ShowAlways = true
        };

        private static string KeyLabel(int vk)
        {
            // Named as the Keybinds tab names it: "1", not "D1"; the layout's "Ö", not "Oemtilde".
            try { return KeyNames.Friendly(vk); }
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
                RestoreCursorOnStop = _restoreCursorCheck.Checked,
                // These three were missing from the rebuild entirely. BackgroundClick
                // in particular meant a profile saved with it on came back with it off,
                // because Save rebuilds the profile from the controls and there was no
                // control carrying it.
                BackgroundClick = _backgroundClickCheck != null && _backgroundClickCheck.Checked,
                PlaySoundOnStart = _soundOnStartCheck != null && _soundOnStartCheck.Checked,
                PlaySoundOnStop = _soundOnStopCheck != null && _soundOnStopCheck.Checked
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

        /// <summary>
        /// Current clicker setup as JSON, for unsaved-change detection.
        ///
        /// The library bookkeeping is zeroed first, and that is not tidiness — it is the
        /// whole reason this comparison used to be wrong. ClickProfile stamps
        /// <c>CreatedUtc = DateTime.UtcNow</c> in its initialiser, so every
        /// BuildProfileFromUi() produced a profile that differed from the one taken
        /// moments earlier by nothing but the clock. "Unsaved changes" therefore came up
        /// on the first UI tick after loading ANY profile and never went away — the
        /// indicator could not report "saved" even immediately after a save.
        ///
        /// None of these four come from the clicker controls, so dropping them compares
        /// exactly what the user can actually edit here.
        /// </summary>
        private string SerializeProfileSafe()
        {
            try
            {
                ClickProfile p = BuildProfileFromUi();
                p.CreatedUtc = DateTime.MinValue;
                p.LastUsedUtc = DateTime.MinValue;
                p.TimesUsed = 0;
                p.TotalRuntimeSeconds = 0;
                return System.Text.Json.JsonSerializer.Serialize(p);
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
            // The Multi-Point tab edits this same profile, and its points are just as lost on a profile
            // switch — so it shows the same state, next to a Save of its own.
            UpdatePointsSaveState(dirty);
            // Keep the accent colour after theme switches (ThemeManager resets labels).
            if (dirty && _theme != null && _profileDirtyLabel.ForeColor != _theme.Accent)
            {
                _profileDirtyLabel.ForeColor = Theme.Readable(_theme.Accent, _theme.Surface);
            }
        }

        /// <summary>
        /// Puts a profile into the Clicker and Multi-Point controls. <paramref name="asBaseline"/>
        /// false leaves the unsaved-changes baseline alone: that is how a draft kept from a hotkey
        /// switch comes back on top of its saved profile, still marked unsaved.
        /// </summary>
        private void LoadProfileIntoUi(ClickProfile p, bool asBaseline = true)
        {
            if (p == null)
            {
                return;
            }

            // An index a combo does not have — a value from a newer Tempo, say — threw half-way
            // through this load and left the tab showing two profiles mixed. The nearest thing
            // the combo has is shown instead (see ClickProfile.Sanitize).
            int Index(ComboBox combo, int i, int limit) =>
                i >= 0 && i < Math.Min(limit, combo.Items.Count) ? i : 0;

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
                    : Index(_buttonCombo, (int)p.Button, KeyTargetIndex);
                RefreshKeyButton();
                _styleCombo.SelectedIndex = Index(_styleCombo, (int)p.Style, int.MaxValue);
                _modeCombo.SelectedIndex = Index(_modeCombo, (int)p.Mode, int.MaxValue);

                _posCurrentRadio.Checked = p.PositionMode == PositionMode.CurrentPosition;
                _posFixedRadio.Checked = p.PositionMode == PositionMode.FixedPosition;
                _posMultiRadio.Checked = p.PositionMode == PositionMode.MultiPoint;

                _fixedXNum.Value = Clamp(p.FixedX, -100000, 100000);
                _fixedYNum.Value = Clamp(p.FixedY, -100000, 100000);
                _restoreCursorCheck.Checked = p.RestoreCursorOnStop;
                if (_backgroundClickCheck != null) { _backgroundClickCheck.Checked = p.BackgroundClick; }
                if (_soundOnStartCheck != null) { _soundOnStartCheck.Checked = p.PlaySoundOnStart; }
                if (_soundOnStopCheck != null) { _soundOnStopCheck.Checked = p.PlaySoundOnStop; }

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
                if (p.Points != null)
                {
                    foreach (var pt in p.Points)
                    {
                        if (pt != null) { _workingPoints.Add(pt.Clone()); }
                    }
                }
                if (_pointOrderCombo != null)
                {
                    _pointOrderCombo.SelectedIndex = Index(_pointOrderCombo, (int)p.PointOrder, int.MaxValue);
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

            // This load IS the new baseline — nothing is unsaved right now. (Not for a draft
            // coming back over its saved profile: that one is unsaved, and the UI tick says so.)
            if (asBaseline)
            {
                _profileSnapshotJson = SerializeProfileSafe();
                if (_profileDirtyLabel != null)
                {
                    _profileDirtyLabel.Visible = false;
                }
            }

            // _currentProfileName was set above, and this is the only place it moves
            // during a normal switch — so the Profiles grid re-rings from here. Doing
            // it in SelectProfileInCombo alone would miss the startup load, where the
            // combo is selected BEFORE the profile is loaded and the name is still
            // empty at that point.
            RefreshProfileGridActive();
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
                // The remembered name can be one that is gone; the tray named it and ticked
                // nothing until another profile was chosen.
                _settings.LastProfileName = target.Name;
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

            // "The library changed" has exactly one choke point, and this is it, so
            // the Profiles grid rebuilds from here rather than from each of the six
            // call sites that add, rename, duplicate or delete a profile.
            RefreshProfileGrid();
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

            ShowProfileNameInStatus(name);

            // Re-ring the active card. This only repaints, so it is cheap enough to
            // run on every profile switch — including the Ctrl+Tab cycle.
            RefreshProfileGridActive();
        }

        /// <summary>
        /// The profile's name in the status bar and the header. The status bar is a tool strip,
        /// which reads "&amp;" as a shortcut marker and swallows it: "Slow &amp; steady" showed as
        /// "Slow  steady". Doubled, as the tray menu already does (MenuText). The header draws
        /// its own text and shows it as it is.
        /// </summary>
        private void ShowProfileNameInStatus(string name)
        {
            if (_statusProfile != null)
            {
                _statusProfile.Text = Localization.T("Profile: ") + MenuText(name ?? "");
            }
            if (_header != null)
            {
                _header.ProfileText = Localization.T("Profile  •  ") + (name ?? "");
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

            // Safe to switch from here: this handler returns early on _suppressProfileEvents,
            // which every programmatic combo change sets, so it runs only for a real user switch
            // or the Next/Previous-profile hotkeys (CycleProfile, which says so).
            var source = _profileSwitchByHotkey ? ProfileSwitchSource.Hotkey : ProfileSwitchSource.Ui;
            if (!SwitchToProfile(name, source))
            {
                // Cancelled (unsaved edits the user chose to keep working on): the dropdown
                // goes back to the profile that is still loaded.
                SelectProfileInCombo(_currentProfileName);
            }
        }

        /// <summary>
        /// Switching WHILE A RUN IS IN PROGRESS is not a no-op, and it looks like one. The
        /// engine picks the new interval up live, so the run becomes a hybrid — measured: two
        /// seconds at Default's 20 ms then two at another profile's 500 ms, 108 clicks where
        /// 206 were expected, with nothing on screen to explain the drop. The run stays
        /// credited to the profile it started under (see _runProfileName); this says so while
        /// it happens, in the one place that is visible with the window in the tray.
        ///
        /// Called by EVERY way of switching: the Clicker dropdown and the profile hotkeys
        /// always said so, but a switch from the Profiles tab did the same thing silently.
        /// </summary>
        private void NoteSwitchDuringRun(string name)
        {
            if (_engine != null && _engine.IsRunning &&
                !string.IsNullOrEmpty(_runProfileName) &&
                !string.Equals(_runProfileName, name, StringComparison.OrdinalIgnoreCase))
            {
                _statusProfile.Text = Localization.F("Profile: {0}  (run started on {1})",
                    MenuText(name), MenuText(_runProfileName));
                Utils.Logger.Warn("[Profiles] switched to '" + name + "' while a run started on '" +
                    _runProfileName + "' is going — the running clicker now uses '" + name +
                    "' settings; the run stays credited to '" + _runProfileName + "'.");
            }
        }

        private void OnNewProfile(object sender, EventArgs e)
        {
            // The edits of the profile being left come first: answered Cancel, nothing is created
            // (asked after, a Cancel left a new profile behind that was never opened).
            if (!HandleUnsavedEditsBeforeSwitch(ProfileSwitchSource.Ui)) { return; }

            // Named in the user's language, as the Profiles tab's New button always did — this
            // one made "New Profile" in English whatever the language.
            var profile = new ClickProfile(Localization.T("New Profile"))
            {
                IntervalMilliseconds = 100
            };

            _profiles.Add(profile);
            _profiles.Save();
            RefreshProfileCombo();
            // Through the one switch every other path uses. Loading it directly skipped the rest:
            // the tray kept ticking the old profile and a restart reopened it, because the last-used
            // name never moved.
            if (!SwitchToProfile(profile.Name, ProfileSwitchSource.Ui, askAboutEdits: false))
            {
                SelectProfileInCombo(_currentProfileName);
            }
        }

        private void OnSaveProfile(object sender, EventArgs e)
        {
            SaveCurrentProfile(announce: true);
        }

        /// <summary>
        /// Saves the Clicker tab's controls into the active profile. False when it could not
        /// (it failed validation, or a rename would clash), after saying why — the unsaved-edits
        /// prompt on a profile switch relies on that to stay put instead of losing the edits.
        /// </summary>
        private bool SaveCurrentProfile(bool announce)
        {
            ClickProfile edited = BuildProfileFromUi();
            string error = edited.Validate();
            if (error != null)
            {
                ShowWarning(error);
                return false;
            }

            string oldName = _currentProfileName;
            ClickProfile stored = string.IsNullOrEmpty(oldName) ? null : _profiles.GetByName(oldName);

            // Renaming: IN PLACE. This used to delete the old profile — into the recycle bin,
            // as if the user had deleted it — and add the edited one as a new profile, which
            // the manager quietly renamed "Name 2" when the new name was already taken.
            bool renaming = stored != null &&
                            !string.Equals(oldName, edited.Name, StringComparison.OrdinalIgnoreCase);
            if (renaming)
            {
                if (_profiles.Exists(edited.Name))
                {
                    ShowWarning(Localization.F("A profile called '{0}' already exists.", edited.Name));
                    return false;
                }
                _profiles.Rename(oldName, edited.Name);
                // A run in progress is credited by name when it stops.
                if (string.Equals(_runProfileName, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    _runProfileName = edited.Name;
                }
            }

            // The controls hold only the clicking setup; everything else about the profile
            // comes from the stored copy (see ClickProfile.KeepLibraryDataOf).
            edited.KeepLibraryDataOf(stored);
            _profiles.Update(edited);

            // Saved: nothing is waiting under either name any more.
            if (!string.IsNullOrEmpty(oldName)) { _profileDrafts.Remove(oldName); }
            _profileDrafts.Remove(edited.Name);

            _profiles.Save();
            RefreshProfileCombo();
            SelectProfileInCombo(edited.Name);
            _currentProfileName = edited.Name;
            _settings.LastProfileName = edited.Name;
            ShowProfileNameInStatus(edited.Name);
            if (announce)
            {
                ShowInfo(Localization.F("Profile '{0}' saved.", edited.Name));
            }

            _profileSnapshotJson = SerializeProfileSafe();
            if (_profileDirtyLabel != null)
            {
                _profileDirtyLabel.Visible = false;
            }
            return true;
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
            //
            // The copy is switched to, so unsaved edits are asked about — BEFORE copying, so that
            // "Save" puts them into the copy too (asked after, the copy was of the old setup).
            if (!HandleUnsavedEditsBeforeSwitch(ProfileSwitchSource.Ui)) { return; }

            var copy = _profiles.Duplicate(_currentProfileName);
            if (copy != null)
            {
                _profiles.Save();
                RefreshProfileCombo();
                if (!SwitchToProfile(copy.Name, ProfileSwitchSource.Ui, askAboutEdits: false))
                {
                    SelectProfileInCombo(_currentProfileName);
                }
            }
        }

        private void OnDeleteProfile(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentProfileName))
            {
                return;
            }

            // The same words as the Profiles tab's Delete: it goes to the recycle bin from here too.
            var confirm = MessageBox.Show(this,
                Localization.F("Delete profile '{0}'? You can restore it from the recycle bin.", _currentProfileName),
                "Tempo",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            string deleted = _currentProfileName;
            _profiles.Remove(deleted);
            _profileDrafts.Remove(deleted);
            _profiles.Save();
            RefreshProfileCombo();

            if (_profiles.Count > 0)
            {
                // Its edits went with it, so nothing to ask about: straight onto what is left,
                // with that profile's own keybinds and appearance (loading it directly skipped them).
                SwitchToProfile(_profiles.Profiles[0].Name, ProfileSwitchSource.Ui, askAboutEdits: false);
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
            // The Multi-Point tab's summary line reports whether this mode is the one
            // selected, so it has to be re-read when the radio moves.
            UpdateCycleInfo();
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

        /// <summary>
        /// Background clicking: deliver the click to the window under the target point
        /// instead of moving the pointer there.
        ///
        /// THE ENGINE HAS ALWAYS SUPPORTED THIS. ClickEngine reads
        /// ClickProfile.BackgroundClick and routes through
        /// InputSimulator.BackgroundClick / BackgroundClickHeld — a complete
        /// implementation. There was simply no control anywhere that set the flag, so
        /// BuildProfileFromUi left it at its default and the whole path was dead.
        ///
        /// It was also quietly destructive: a profile whose JSON had the flag on loaded
        /// fine, then lost it the moment the user pressed Save, because the rebuilt
        /// profile did not carry the field.
        /// </summary>
        private Control BuildBackgroundClickGroup()
        {
            // At least 136 tall, and as tall as its words need (see the end). Nothing sits under
            // this row, so the card grows rather than the text losing a line.
            var g = UiFactory.Group(Localization.T("Background Clicking"), 12, 938, 360, 136, CardIcon.Cursor);

            _backgroundClickCheck = UiFactory.Check("Click without moving my cursor", 16, 28);
            g.Controls.Add(_backgroundClickCheck);

            var note = UiFactory.Caption(
                "Sends each click straight to the window under the target point, so your " +
                "real pointer stays where it is and you can keep using the PC while Tempo " +
                "runs. Needs a fixed or multi-point target. Many games ignore posted " +
                "clicks and only react to a real pointer — leave this off for those.",
                16, 54);
            note.MaximumSize = new Size(330, 0);
            note.AutoSize = true;
            note.ForeColor = _theme.TextMuted;
            g.Controls.Add(note);

            // Filled in by UpdatePositionControlsEnabled when the current position mode
            // cannot post clicks; empty the rest of the time. Under the explanation, in a
            // slot of its own. It sat beside the checkbox, 124 px wide, where it wrapped to
            // three lines straight across the explanation's first ones — and the layout
            // fitter's answer, squeezing the explanation to 198 px, cut the explanation's
            // end off for good, in every language, English included, warning or no warning.
            // A fixed slot, so the fitter measures the room it takes even while it is empty.
            // In the window's units: the explanation above sizes itself in screen pixels while the
            // window is built, and so does a font's height (LayoutBottom, LayoutUnitsPerPixel).
            _backgroundClickNote = UiFactory.Caption("", 16, LayoutBottom(note) + 4);
            _backgroundClickNote.AutoSize = false;
            _backgroundClickNote.AutoEllipsis = true;
            _backgroundClickNote.Size = new Size(330, (int)Math.Ceiling(_backgroundClickNote.Font.Height * LayoutUnitsPerPixel) * 2 + 2);
            g.Controls.Add(_backgroundClickNote);

            g.Height = Math.Max(g.Height, _backgroundClickNote.Bottom + 8);
            return g;
        }

        /// <summary>
        /// A short tone when a run starts and stops.
        ///
        /// PlaySoundOnStart and PlaySoundOnStop have been on ClickProfile since it was
        /// written, serialised on every save, and read by nothing at all. They earn
        /// their place here: Tempo normally lives in the tray and is driven by F6, so
        /// the usual question is "did that actually start?" with no window on screen to
        /// answer it. Two different pitches answer it without one.
        /// </summary>
        private Control BuildClickSoundGroup()
        {
            // Same row, same overflow in Italian; kept the same height as its neighbour.
            var g = UiFactory.Group(Localization.T("Sound"), 384, 938, 324, 136, CardIcon.Bolt);

            _soundOnStartCheck = UiFactory.Check("Beep when clicking starts", 16, 28);
            g.Controls.Add(_soundOnStartCheck);

            _soundOnStopCheck = UiFactory.Check("Beep when clicking stops", 16, 54);
            g.Controls.Add(_soundOnStopCheck);

            var note = UiFactory.Caption(
                "A rising tone to start and a falling one to stop, so a hotkey press is " +
                "confirmed even with the window hidden in the tray.",
                16, 80);
            note.MaximumSize = new Size(292, 0);
            note.AutoSize = true;
            note.ForeColor = _theme.TextMuted;
            g.Controls.Add(note);

            g.Height = Math.Max(g.Height, LayoutBottom(note) + 8);
            return g;
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
            // "middle-click to spam": the MIDDLE button toggles spam — left and right are real
            // clicks at the second cursor (see OnRawDeviceInput, and the hint below). This
            // label said right-click, in all six languages, from before that changed.
            _secondMouseUseCheck = UiFactory.Check("Let a 2nd real mouse control it — move, click, middle-click to spam (needs 2 mice)", 16, 50);
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
                // "Ask by wiggling" asks now, like the same entry in the cursor's own menu. On
                // its own it only forgot the chosen mouse, and one already bound stayed bound.
                if (raw.Length == 0) { _secondCursor?.RepickSecondMouse(); }
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
            // As written: with shortcut markers on, "opens apps & hits anything" lost its "&" in
            // English ("opens apps  hits anything"), found by the layout check's ampersand pass.
            hint.UseMnemonic = false;
            // As tall as its words: three lines in French, German, Italian and Portuguese,
            // where a fixed two stopped drawing mid-sentence — losing the Emergency-Stop part.
            // The card grows to hold it, and BuildClickerTab moves the row below it down.
            // In 100% units: the window is scaled after it is built (UiFactory.Measure96).
            hint.Height = Math.Max(30, UiFactory.Measure96(hint.Text, hint.Font, hint.Width, TextFormatFlags.NoPrefix).Height + 2);
            g.Controls.Add(hint);
            g.Height = Math.Max(g.Height, hint.Bottom + 8);

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
                    _secondMouseCombo.Items.Add(new MouseComboItem { Raw = "", Display = Localization.T("Ask by wiggling") });
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
                // In the user's language: this line was assembled from English pieces in
                // every language (the engine's own summary stays English, for the log).
                string text = Localization.F("Detected: {0}", MiceSummaryText());
                if (_settings != null && _settings.SecondCursorUsePhysicalMouse)
                {
                    if (_secondCursor != null && _secondCursor.SecondMouseBound)
                    {
                        string nm = _secondCursor.SecondMouseName;
                        text += "  —  " + Localization.F("{0} is driving the cursor", nm.Length > 0 ? nm : Localization.T("2nd mouse"));
                    }
                    else if (_secondCursor != null && _secondCursor.Assigning)
                    {
                        text += "  —  " + Localization.T("now wiggle the mouse you want to use");
                    }
                    else if (count < 2)
                    {
                        text += "  —  " + Localization.T("waiting: plug in a 2nd mouse");
                    }
                    else
                    {
                        text += "  —  " + Localization.T("pick your 2nd mouse above");
                    }
                }
                _miceDetectedLabel.Text = text;
                _miceDetectedLabel.ForeColor = (count >= 2) ? _theme.Text : _theme.TextMuted;
            }
            catch { }
        }

        /// <summary>
        /// "2 mice: Razer DeathAdder, Logitech G305" in the user's language, for the Clicker
        /// tab and the second cursor's menu. The engine's own summary is kept English for the
        /// log and Live debug.
        /// </summary>
        private static string MiceSummaryText()
        {
            try
            {
                var mice = Native.SecondMouseListener.EnumerateRealMice();
                if (mice.Count == 0)
                {
                    return Localization.T("No mice detected");
                }
                var names = new System.Text.StringBuilder();
                for (int i = 0; i < mice.Count && i < 4; i++)
                {
                    if (i > 0) { names.Append(", "); }
                    names.Append(mice[i].FriendlyName);
                }
                return mice.Count == 1
                    ? Localization.F("{0} mouse: {1}", mice.Count, names.ToString())
                    : Localization.F("{0} mice: {1}", mice.Count, names.ToString());
            }
            catch { return ""; }
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
                _secondCursor.PositionChanged += (s, e) => RunOnUi(OnSecondCursorMoved);
                _secondCursor.SpamChanged += (s, e) => RunOnUi(OnSecondCursorSpamChanged);
                // Back where it was parked last time (onto a screen, if that one is gone).
                if (_settings.SecondCursorX != int.MinValue && _settings.SecondCursorY != int.MinValue)
                {
                    _secondCursor.SetPosition(_settings.SecondCursorX, _settings.SecondCursorY);
                }
            }
            var shape = (SecondCursorShape)Math.Max(0, Math.Min(3, _settings.SecondCursorShape));
            _secondCursor.SetAppearance(shape, Color.FromArgb(_settings.SecondCursorColorArgb),
                Math.Max(50, Math.Min(250, _settings.SecondCursorScale)));
            int cps = Math.Max(1, _settings.SecondCursorSpamCps);
            _secondCursor.SetSpamSettings(
                (MouseButtonType)Math.Max(0, Math.Min(2, _settings.SecondCursorSpamButton)),
                ClickStyle.Single, Math.Max(1, 1000 / cps), _settings.SecondCursorSpamRealClicks);
            _secondCursor.SetSecondMouseSensitivity(_settings.SecondCursorMouseSensitivity);
            _secondCursor.SetEnabled(_settings.SecondCursorEnabled);
            // Tell the controller which mouse the user chose BEFORE arming the mode, so it
            // binds that one directly instead of asking for a wiggle.
            _secondCursor.SetPreferredDevice(_settings.SecondCursorMouseDeviceName);
            // Arm the "2nd real mouse" mode if the user asked for it (needs the cursor
            // showing; it now waits for a 2nd mouse rather than hard-failing).
            _secondCursor.SetUsePhysicalMouse(_settings.SecondCursorEnabled && _settings.SecondCursorUsePhysicalMouse);
        }

        // Saves the parked spot a moment after the last move rather than on every one: a
        // window being dragged carries the spot along up to a hundred times a second.
        private System.Windows.Forms.Timer _secondCursorSaveTimer;

        /// <summary>The second cursor was parked somewhere new — remember it for the next launch.</summary>
        private void OnSecondCursorMoved()
        {
            if (_secondCursor == null || _settings == null) { return; }
            _settings.SecondCursorX = _secondCursor.X;
            _settings.SecondCursorY = _secondCursor.Y;
            if (_secondCursorSaveTimer == null)
            {
                _secondCursorSaveTimer = new System.Windows.Forms.Timer { Interval = 1500 };
                _secondCursorSaveTimer.Tick += (s, e) =>
                {
                    _secondCursorSaveTimer.Stop();
                    Persistence.SettingsManager.Save(_settings);
                };
            }
            _secondCursorSaveTimer.Stop();
            _secondCursorSaveTimer.Start();
        }

        /// <summary>
        /// Spam-clicking started or stopped: the clicker's start / stop tones, when those are
        /// on. It is mostly toggled from a hotkey or the second mouse's middle button with a
        /// game in front, where nothing else says whether the toggle took.
        /// </summary>
        private void OnSecondCursorSpamChanged()
        {
            if (_secondCursor == null) { return; }
            bool on = _secondCursor.Spamming;
            if (on && _soundOnStartCheck != null && _soundOnStartCheck.Checked) { PlayRunTone(true); }
            if (!on && _soundOnStopCheck != null && _soundOnStopCheck.Checked) { PlayRunTone(false); }
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

            var header = new ToolStripMenuItem(Utils.Localization.T("Second cursor")) { Enabled = false };
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            // "&&", not "&". A ToolStripMenuItem reads a single ampersand as the mnemonic
            // prefix and EATS it, so "Grab & place" was rendering on screen as "Grab  place"
            // with a stray double space. Doubling it escapes the mnemonic.
            // Through the translator, like every sibling below: these few were bare English
            // literals, so the menu came up half translated in every other language.
            menu.Items.Add(Localization.T(_secondCursor.Placing ? "Placing… (click to drop)" : "Grab && place"), null,
                (s, ev) => _secondCursor.StartPlacement());

            // Spam clicks are posted, so they are invisible: a spam that has paused itself
            // because the window it was aimed at went away looks exactly like one that is
            // working. This menu is the thing the user opens to find out, so it answers.
            string spamText = Localization.T("Spam-click here");
            if (_secondCursor.Spamming)
            {
                string paused = _secondCursor.SpamPausedReason;
                spamText = paused.Length > 0
                    ? Localization.F("Stop spam-click  ·  paused: {0}", Localization.T(paused))
                    : Localization.T("Stop spam-click");
            }
            var spam = new ToolStripMenuItem(spamText)
            { Checked = _secondCursor.Spamming, CheckOnClick = false };
            spam.Click += (s, ev) => _secondCursor.ToggleSpam();
            menu.Items.Add(spam);
            // What it is aimed at and how many clicks have gone out: spam aimed at the wrong
            // window looks exactly like spam aimed at the right one. The title is the window's
            // own text, so it is escaped like any other outside string.
            if (_secondCursor.Spamming)
            {
                string aimedAt = _secondCursor.SpamTargetName;
                menu.Items.Add(new ToolStripMenuItem(MenuText(Localization.F("Aimed at: {0}  ·  clicks: {1}",
                    aimedAt.Length > 0 ? aimedAt : "…", _secondCursor.SpamClickCount.ToString("N0"))))
                { Enabled = false });
            }

            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(Utils.Localization.T("Change colour…"), null, (s, ev) =>
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

            // Spam BUTTON. The setting has always existed and always been read — the
            // controller clamps it to Left/Right/Middle on every apply — but nothing
            // anywhere could change it, so the second cursor could only ever spam left
            // clicks. Every one of its siblings (shape, size, speed) had a submenu here;
            // this one was simply missed.
            var buttonItem = new ToolStripMenuItem(Localization.T("Spam button"));
            string[] buttonNames = { "Left", "Right", "Middle" };   // MouseButtonType order
            for (int i = 0; i < buttonNames.Length; i++)
            {
                int idx = i;
                var it = new ToolStripMenuItem(Localization.T(buttonNames[i]))
                { Checked = _settings.SecondCursorSpamButton == idx, CheckOnClick = false };
                it.Click += (s, ev) =>
                {
                    _settings.SecondCursorSpamButton = idx;
                    Persistence.SettingsManager.Save(_settings);
                    ApplySecondCursorSettings();
                };
                buttonItem.DropDownItems.Add(it);
            }
            int btnNow = Math.Max(0, Math.Min(buttonNames.Length - 1, _settings.SecondCursorSpamButton));
            buttonItem.Text = Localization.F("Spam button  ·  {0}", Localization.T(buttonNames[btnNow]));
            menu.Items.Add(buttonItem);

            // How spam clicks. Posted clicks leave the pointer alone and work behind other
            // windows, but a game that reads the mouse itself never sees them. Real clicks reach
            // games, at the price of the pointer jumping there for each click.
            var methodItem = new ToolStripMenuItem(Localization.T("Click method"));
            (bool real, string text)[] methods =
            {
                (false, "In the background — your pointer stays free"),
                (true, "Real clicks — for games: your pointer jumps there for each click, only while that game is in front"),
            };
            foreach (var m in methods)
            {
                bool real = m.real;
                var it = new ToolStripMenuItem(Localization.T(m.text))
                { Checked = _settings.SecondCursorSpamRealClicks == real, CheckOnClick = false };
                it.Click += (s, ev) =>
                {
                    _settings.SecondCursorSpamRealClicks = real;
                    Persistence.SettingsManager.Save(_settings);
                    ApplySecondCursorSettings();
                };
                methodItem.DropDownItems.Add(it);
            }
            methodItem.Text = Localization.F("Click method  ·  {0}",
                Localization.T(_settings.SecondCursorSpamRealClicks ? "Real clicks" : "In the background"));
            menu.Items.Add(methodItem);

            // ── second physical mouse ──
            menu.Items.Add(new ToolStripSeparator());
            int miceCount = Engine.SecondCursorController.DetectedMouseCount();
            menu.Items.Add(new ToolStripMenuItem(MenuText(MiceSummaryText())) { Enabled = false });

            // With only one mouse the controller deliberately ARMS and waits for a second to
            // be plugged in, rather than refusing. That is the right behaviour, but the menu
            // said nothing about it: you ticked the item, the cursor carried on ignoring
            // your mouse, and the only explanation was a line in the log. Say it here.
            string use2ndText = Localization.T(_settings.SecondCursorUsePhysicalMouse
                ? (miceCount < 2 ? "Waiting for a 2nd mouse — click to stop" : "Stop using my 2nd mouse")
                : (miceCount < 2 ? "Use my 2nd mouse — plug one in first (experimental)"
                                 : "Use my 2nd mouse (experimental)"));
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
            var chooseItem = new ToolStripMenuItem(Utils.Localization.T("Choose 2nd mouse"));
            string chosenRaw = _settings.SecondCursorMouseDeviceName ?? "";
            var wiggleEntry = new ToolStripMenuItem(Utils.Localization.T("Ask by wiggling"))
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
            menu.Items.Add(Utils.Localization.T("Hide second cursor"), null, (s, ev) =>
            {
                _settings.SecondCursorEnabled = false;
                Persistence.SettingsManager.Save(_settings);
                if (_secondCursorEnableCheck != null) { _secondCursorEnableCheck.Checked = false; }
                ApplySecondCursorSettings();
            });

            ShowOverlayMenu(menu, new Point(e.X, e.Y));
        }

        /// <summary>
        /// Shows a menu that belongs to one of Tempo's overlay windows (the second cursor, the caption
        /// bar, the caption history) and gives the menu ITSELF the foreground.
        ///
        /// Force its OWN popup foreground so its item clicks register — WITHOUT popping Tempo's main
        /// window. When Tempo isn't the active app, Windows blocks SetForegroundWindow (the
        /// "foreground lock"), which is why item clicks were being treated as outside-clicks and the
        /// menu just closed. The documented workaround is to briefly attach our input thread to the
        /// currently-foreground window's thread, which lifts the lock.
        /// </summary>
        private void ShowOverlayMenu(ContextMenuStrip menu, Point at)
        {
            menu.Show(at);
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

            // Background clicking needs a TARGET POINT to post the click to. The engine
            // only honours it in the fixed-position and multi-point branches; in
            // "current cursor position" — the default — the flag is read and silently
            // ignored, so ticking the box did nothing whatsoever and said nothing about
            // it. The card's own text has always carried the condition ("Needs a fixed
            // or multi-point target"), which is no use at the moment it stops applying.
            //
            // Greyed with the reason beside it, rather than hidden: the feature still
            // exists, it just needs a different position mode first.
            if (_backgroundClickCheck != null)
            {
                bool canPost = _posFixedRadio.Checked || _posMultiRadio.Checked;
                _backgroundClickCheck.Enabled = canPost;
                if (_backgroundClickNote != null)
                {
                    _backgroundClickNote.Text = canPost
                        ? ""
                        : Utils.Localization.T("Pick a fixed or multi-point target to use this.");
                    _backgroundClickNote.ForeColor = _theme.WarningText;
                }
            }
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
                    // Rebuild what Hide() threw away. A bare Show() left the window in
                    // the state the user reported: the tab page's backdrop composite was
                    // never rebuilt, and because a wallpaper flips every label, checkbox
                    // and panel to a TRANSPARENT background, there was nothing painting
                    // behind them — so the window came back as a hole with the desktop
                    // showing through and only a few controls visible.
                    //
                    // This is the same repair the tray-restore path does — and like it, done
                    // with the window cloaked, so it comes back whole instead of in pieces.
                    ShowWholeAfterHide(() =>
                    {
                        Show();
                        EnsureOnScreen();
                        Activate();
                        ReassertTopMost();
                    });
                }
            }
        }

        private void OpenCpsTest()
        {
            // The clicker's rate while it runs, so a run it makes on the pad shows against what it was set to.
            Func<double> clickerRate = () => _engine != null && _engine.IsRunning && !_engine.IsPaused ? _engine.EffectiveClicksPerSecond : 0;
            using (var form = new CpsTestForm(_theme, _settings.CpsTestBest, _settings.CpsTestBests, clickerRate))
            {
                form.ShowDialog(this);
                bool changed = false;
                if (form.AllTimeBest > _settings.CpsTestBest)
                {
                    _settings.CpsTestBest = form.AllTimeBest;
                    changed = true;
                }
                // The best for each input and length the test was run with.
                if (form.BestsChanged)
                {
                    if (_settings.CpsTestBests == null)
                    {
                        _settings.CpsTestBests = new System.Collections.Generic.Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    }
                    foreach (var kv in form.Bests)
                    {
                        _settings.CpsTestBests.TryGetValue(kv.Key, out double had);
                        if (kv.Value > had) { _settings.CpsTestBests[kv.Key] = kv.Value; changed = true; }
                    }
                }
                if (changed)
                {
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
            // Marked as the hotkey's doing: unsaved edits are then kept as a draft rather than
            // asked about — a dialog would take focus from the game the hotkey was pressed in.
            _profileSwitchByHotkey = true;
            try { _profileCombo.SelectedIndex = index; }   // fires OnProfileSelected -> switches
            finally { _profileSwitchByHotkey = false; }
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
