using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Persistence;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    public partial class MainForm
    {
        private int _recordedStepCount;

        // Extra macro-tab controls (declared here; this is a partial of MainForm).
        private CheckBox _recordKeysCheck;
        private NumericUpDown _recordCountdownNum;
        private Button _editMacroBtn;
        private Button _renameMacroBtn;
        private Button _duplicateMacroBtn;
        private Button _exportMacroBtn;
        private Button _importMacroBtn;
        private Button _notesMacroBtn;
        private Button _exportAllBtn;
        private Button _importAllBtn;
        private Button _mergeMacroBtn;
        private Button _fixMacroBtn;
        private Button _pinMacroBtn;
        private Button _resetMacroStatsBtn;
        private Label _macroSummaryLabel;
        private CheckBox _macroSmoothCheck;
        private CheckBox _macroPreserveHoldsCheck;
        private Button _macroMoveUpBtn;
        private Button _macroMoveDownBtn;
        private NumericUpDown _macroCountdownNum;
        private NumericUpDown _macroLoopDelayNum;
        private TextBox _macroSearchBox;
        private ComboBox _macroSortCombo;
        private string _macroFilter = string.Empty;
        private ThemedProgressBar _macroProgressBar;
        private Label _macroProgressLabel;

        // Live monitor + append-recording state.
        private CheckBox _appendRecordCheck;
        private CheckBox _cursorTrailCheck;
        private LiveStepListView _liveStepList;
        private int _liveHighlightIndex = -1;
        private Label _liveHeaderLabel;
        private Macro _appendTarget;
        private Macro _liveMonitorMacro;
        private long _liveCumulativeMs;
        private bool _liveRecording;

        // Playback progress + per-macro default bookkeeping.
        private int _playbackTotalSteps;
        private int _playbackTotalLoops;
        private int _playbackCurrentLoop;
        private DateTime _playbackStartUtc;
        private double _playbackTotalEstimateMs;
        private bool _suppressMacroDefaults;
        private RecordingIndicatorForm _recordIndicator;

        private void BuildMacrosTab()
        {
            var page = new BackdropTabPage(Utils.Localization.T("Macros")) { AutoScroll = true };
            page.Name = "macros";   // stable key for LastTabKey

            string helpText =
                "Record mouse and keyboard input, edit the steps, then play it back. " +
                "The Live Monitor below fills in real time as you record and highlights " +
                "each step during playback. Tick \"Append to selected macro\" to add onto " +
                "an existing recording. In the list: Enter plays, Ctrl+D duplicates, " +
                "F2 renames, Delete moves to the recycle bin. Bind \"Record\" and \"Play\" " +
                "on the Keybinds tab to control it hands-free.";
            var help = UiFactory.Label(helpText, 12, 12);
            help.MaximumSize = new Size(720, 0);
            help.AutoSize = true;
            help.ForeColor = _theme.TextMuted;

            var listLabel = UiFactory.Label(Utils.Localization.T("Saved macros"), 12, 64, FontStyle.Bold);
            _macroSearchBox = UiFactory.Text(140, 61, 172);
            _macroSearchBox.PlaceholderText = Utils.Localization.T("Search macros\u2026");
            _macroSearchBox.TextChanged += (s, e) => { _macroFilter = _macroSearchBox.Text; RefreshMacroList(); };
            _macroListBox = new MacroListBox
            {
                Left = 12,
                Top = 86,
                Width = 300,
                Height = 374,
                Font = UiFactory.BodyFont,
                BorderStyle = BorderStyle.FixedSingle
            };
            _macroListBox.DoubleClick += (s, e) => EditSelectedMacro();
            _macroListBox.SelectedIndexChanged += (s, e) => LoadMacroDefaultsIntoUi();
            _macroListBox.KeyDown += OnMacroListKeyDown;

            var macroMenu = new ContextMenuStrip();
            macroMenu.Items.Add(Utils.Localization.T("Play"), null, OnPlayMacroClicked);
            macroMenu.Items.Add(Utils.Localization.T("Play once"), null, OnPlayMacroOnceClicked);
            // Five of these nine were raw literals sitting between translated siblings, so
            // the right-click menu came up half in English in every other language. All
            // five were already in the tables and had simply never been reached.
            macroMenu.Items.Add(Utils.Localization.T("Edit…"), null, (s, e) => EditSelectedMacro());
            macroMenu.Items.Add(Utils.Localization.T("Rename…"), null, OnRenameMacro);
            macroMenu.Items.Add(Utils.Localization.T("Duplicate"), null, OnDuplicateMacro);
            macroMenu.Items.Add(Utils.Localization.T("Export…"), null, OnExportMacro);
            macroMenu.Items.Add(new ToolStripSeparator());
            macroMenu.Items.Add(Utils.Localization.T("Pin / Unpin"), null, OnPinMacroClicked);
            macroMenu.Items.Add(Utils.Localization.T("Delete"), null, OnDeleteMacroClicked);
            _macroListBox.ContextMenuStrip = macroMenu;
            _macroListBox.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    int i = _macroListBox.IndexFromPoint(e.Location);
                    if (i >= 0) _macroListBox.SelectedIndex = i;
                }
            };

            // ── Management panel ───────────────────────────────────────────────
            // Grouped into a titled panel so the actions read as one organized block
            // rather than a loose column of buttons (which looked dated).
            // 496 tall, not 458: one more row for "Fix…". The Live Monitor below starts
            // at y=634, so there is room for it without moving anything else.
            // 536 rather than 496: the Recycle bin button below Delete needs the room,
            // and the Live Monitor card does not start until y=634.
            var manageGroup = UiFactory.Group(Utils.Localization.T("Manage"), 320, 84, 124, 536, CardIcon.Gear);
            int mx = 6;
            int mw = 112;

            _editMacroBtn = UiFactory.Button(Utils.Localization.T("Edit…"), mx, 24, mw, 30);
            _editMacroBtn.Click += (s, e) => EditSelectedMacro();

            _renameMacroBtn = UiFactory.Button(Utils.Localization.T("Rename…"), mx, 58, mw, 30);
            _renameMacroBtn.Click += OnRenameMacro;

            _duplicateMacroBtn = UiFactory.Button(Utils.Localization.T("Duplicate"), mx, 92, mw, 30);
            _duplicateMacroBtn.Click += OnDuplicateMacro;

            _notesMacroBtn = UiFactory.Button(Utils.Localization.T("Notes…"), mx, 126, mw, 30);
            _notesMacroBtn.Click += OnEditMacroNotes;

            _macroMoveUpBtn = UiFactory.Button(Utils.Localization.T("Move up"), mx, 168, mw, 30);
            _macroMoveUpBtn.Click += (s, e) => MoveMacro(-1);

            _macroMoveDownBtn = UiFactory.Button(Utils.Localization.T("Move down"), mx, 202, mw, 30);
            _macroMoveDownBtn.Click += (s, e) => MoveMacro(1);

            _exportMacroBtn = UiFactory.Button(Utils.Localization.T("Export…"), mx, 244, mw, 30);
            _exportMacroBtn.Click += OnExportMacro;

            _importMacroBtn = UiFactory.Button(Utils.Localization.T("Import…"), mx, 278, mw, 30);
            _importMacroBtn.Click += OnImportMacro;

            _exportAllBtn = UiFactory.Button(Utils.Localization.T("Export all…"), mx, 312, mw, 30);
            _exportAllBtn.Click += OnExportAllMacros;

            _importAllBtn = UiFactory.Button(Utils.Localization.T("Import all…"), mx, 346, mw, 30);
            _importAllBtn.Click += OnImportAllMacros;

            _mergeMacroBtn = UiFactory.Button(Utils.Localization.T("Merge…"), mx, 388, mw, 30);
            _mergeMacroBtn.Click += OnMergeMacroClicked;

            // Sits directly above Delete: the repair pass a recording usually wants
            // before it is trusted (stuck keys, off-screen clicks, robotic timing).
            // Delete stays last so the destructive button keeps its own corner.
            _fixMacroBtn = UiFactory.Button(Utils.Localization.T("Fix…"), mx, 422, mw, 30);
            _fixMacroBtn.Click += OnFixMacroClicked;

            _deleteMacroBtn = UiFactory.Button("Delete", mx, 456, mw, 30);
            _deleteMacroBtn.ForeColor = _theme.Danger;
            _deleteMacroBtn.Click += OnDeleteMacroClicked;

            // Directly under Delete, because that is the button people press by
            // accident and this is the way back from it.
            _macroRecycleBtn = UiFactory.Button(Utils.Localization.T("Recycle bin"), mx, 494, mw, 30);
            _macroRecycleBtn.Click += OnMacroRecycleBinClicked;

            manageGroup.Controls.Add(_macroRecycleBtn);
            manageGroup.Controls.Add(_fixMacroBtn);
            manageGroup.Controls.Add(_editMacroBtn);
            manageGroup.Controls.Add(_renameMacroBtn);
            manageGroup.Controls.Add(_duplicateMacroBtn);
            manageGroup.Controls.Add(_notesMacroBtn);
            manageGroup.Controls.Add(_macroMoveUpBtn);
            manageGroup.Controls.Add(_macroMoveDownBtn);
            manageGroup.Controls.Add(_exportMacroBtn);
            manageGroup.Controls.Add(_importMacroBtn);
            manageGroup.Controls.Add(_exportAllBtn);
            manageGroup.Controls.Add(_importAllBtn);
            manageGroup.Controls.Add(_mergeMacroBtn);
            manageGroup.Controls.Add(_deleteMacroBtn);

            // Sort lives above the list, next to the search box.
            _macroSortCombo = UiFactory.Combo(320, 61, 124, Utils.Localization.T("Sort: A \u2192 Z"), Utils.Localization.T("Sort: Most played"), Utils.Localization.T("Sort: Newest"));
            _macroSortCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _macroSortCombo.SelectedIndexChanged += OnMacroSortChanged;

            // Pin / Reset stats sit under the list as a compact action row.
            _pinMacroBtn = UiFactory.Button("\u2605 Pin", 12, 468, 146, 30);
            _pinMacroBtn.Click += OnPinMacroClicked;

            _resetMacroStatsBtn = UiFactory.Button("Reset stats", 166, 468, 146, 30);
            _resetMacroStatsBtn.Click += OnResetMacroStats;

            _macroSummaryLabel = UiFactory.Caption("", 12, 504);
            _macroSummaryLabel.AutoSize = false;
            _macroSummaryLabel.Width = 300;
            _macroSummaryLabel.Height = 16;
            _macroSummaryLabel.ForeColor = _theme.TextMuted;

            // ── Record group ───────────────────────────────────────────────────
            var recGroup = UiFactory.Group(Utils.Localization.T("Record"), 444, 80, 268, 202, CardIcon.Target);

            _recordMovesCheck = UiFactory.Check("Capture mouse movement", 16, 22, true);
            _recordMovesCheck.Checked = _settings == null || _settings.RecordMacroMovements;
            _recordMovesCheck.CheckedChanged += OnRecordMovesChanged;
            recGroup.Controls.Add(_recordMovesCheck);

            _recordKeysCheck = UiFactory.Check("Capture keyboard", 16, 44, true);
            _recordKeysCheck.Checked = _settings == null || _settings.RecordMacroKeyboard;
            _recordKeysCheck.CheckedChanged += OnRecordKeysChanged;
            recGroup.Controls.Add(_recordKeysCheck);

            _appendRecordCheck = UiFactory.Check("Append to selected macro", 16, 66, false);
            recGroup.Controls.Add(_appendRecordCheck);

            // Optional "3, 2, 1" countdown before capture starts, so you can switch to the
            // target window first (its own alt-tab isn't recorded). Symmetric with playback.
            recGroup.Controls.Add(UiFactory.Caption("Countdown (s, 0 = off)", 16, 94));
            _recordCountdownNum = UiFactory.Numeric(178, 90, 60, 0, 10, 0);
            if (_settings != null) _recordCountdownNum.Value = Clamp(_settings.RecordCountdownSeconds, 0, 10);
            _recordCountdownNum.ValueChanged += OnRecordCountdownChanged;
            recGroup.Controls.Add(_recordCountdownNum);

            _recordBtn = UiFactory.Button("● Record", 16, 120, 118, 34);
            _recordBtn.ForeColor = _theme.Danger;
            _recordBtn.Click += (s, e) => StartRecording();
            recGroup.Controls.Add(_recordBtn);

            _stopRecordBtn = UiFactory.Button("■ Stop", 140, 120, 112, 34);
            _stopRecordBtn.Enabled = false;
            _stopRecordBtn.Click += (s, e) => StopRecording();
            recGroup.Controls.Add(_stopRecordBtn);

            _recordStatusLabel = UiFactory.Label("Not recording.", 16, 164, FontStyle.Italic, 9f);
            _recordStatusLabel.MaximumSize = new Size(236, 0);
            _recordStatusLabel.ForeColor = _theme.TextMuted;
            recGroup.Controls.Add(_recordStatusLabel);

            // ── Playback group ─────────────────────────────────────────────────
            var playGroup = UiFactory.Group(Utils.Localization.T("Playback"), 444, 292, 268, 334, CardIcon.Play);

            playGroup.Controls.Add(UiFactory.Caption("Loops (0 = infinite)", 16, 26));
            _macroLoopNum = UiFactory.Numeric(170, 22, 86, 0, 1000000, 1);
            _macroLoopNum.ValueChanged += OnMacroDefaultChanged;
            playGroup.Controls.Add(_macroLoopNum);

            playGroup.Controls.Add(UiFactory.Caption("Speed (10 = 1.0x)", 16, 54));
            // Speed is an integer 1..100 meaning 0.1x .. 10.0x.
            _macroSpeedNum = UiFactory.Numeric(170, 50, 86, 1, 100, 10);
            _macroSpeedNum.ValueChanged += OnMacroDefaultChanged;
            playGroup.Controls.Add(_macroSpeedNum);

            playGroup.Controls.Add(UiFactory.Caption("Countdown (s, 0 = off)", 16, 82));
            _macroCountdownNum = UiFactory.Numeric(170, 78, 86, 0, 10, 0);
            _macroCountdownNum.ValueChanged += OnMacroDefaultChanged;
            playGroup.Controls.Add(_macroCountdownNum);

            playGroup.Controls.Add(UiFactory.Caption("Loop delay (ms)", 16, 110));
            _macroLoopDelayNum = UiFactory.Numeric(170, 106, 86, 0, 3600000, 0);
            _macroLoopDelayNum.ValueChanged += OnMacroDefaultChanged;
            playGroup.Controls.Add(_macroLoopDelayNum);

            // Quick playback-speed presets so you don't have to know the "10 = 1.0x"
            // scale — each just sets the Speed value above (0.5x = 5, 1x = 10, …).
            playGroup.Controls.Add(UiFactory.Caption(Utils.Localization.T("Quick speed:"), 16, 144));
            var speedPresets = new[] { new[] { 5 }, new[] { 10 }, new[] { 20 }, new[] { 40 } };
            string[] speedLabels = { "0.5×", "1×", "2×", "4×" };
            int spX = 96;
            for (int si = 0; si < speedPresets.Length; si++)
            {
                int val = speedPresets[si][0];
                var sb = UiFactory.Button(speedLabels[si], spX, 140, 38, 26);
                sb.Click += (s, e) => { if (_macroSpeedNum != null) _macroSpeedNum.Value = val; };
                playGroup.Controls.Add(sb);
                spX += 42;
            }

            _playMacroBtn = UiFactory.PrimaryButton("▶ Play", 16, 176, 80, 38, _theme);
            _playMacroBtn.Click += OnPlayMacroClicked;
            playGroup.Controls.Add(_playMacroBtn);

            _playOnceBtn = UiFactory.Button("Once", 102, 176, 70, 38);
            _playOnceBtn.Click += OnPlayMacroOnceClicked;
            playGroup.Controls.Add(_playOnceBtn);

            _macroSmoothCheck = UiFactory.Check("Smooth mouse movement", 12, 274);
            _macroSmoothCheck.CheckedChanged += OnMacroSmoothChanged;
            playGroup.Controls.Add(_macroSmoothCheck);

            // When on, the speed presets only speed up the gaps between actions — held keys
            // and buttons keep their real recorded duration (so 2x/4x won't shrink a WASD
            // movement hold into a tap).
            _macroPreserveHoldsCheck = UiFactory.Check("Keep key/button holds at speed", 12, 300);
            _macroPreserveHoldsCheck.CheckedChanged += OnMacroPreserveHoldsChanged;
            playGroup.Controls.Add(_macroPreserveHoldsCheck);

            _stopPlayBtn = UiFactory.Button("■ Stop", 178, 176, 74, 38);
            _stopPlayBtn.BackColor = _theme.Danger;
            _stopPlayBtn.ForeColor = Color.White;
            _stopPlayBtn.FlatAppearance.BorderSize = 0;
            _stopPlayBtn.Enabled = false;
            _stopPlayBtn.Click += (s, e) => _player.Stop();
            playGroup.Controls.Add(_stopPlayBtn);

            _macroProgressBar = new ThemedProgressBar
            {
                Left = 16,
                Top = 222,
                Width = 236,
                Height = 16,
                Maximum = 100,
                Value = 0
            };
            _macroProgressBar.ApplyTheme(_theme);
            playGroup.Controls.Add(_macroProgressBar);

            _macroProgressLabel = UiFactory.Label("Ready.", 16, 242, FontStyle.Italic, 8.5f);
            _macroProgressLabel.MaximumSize = new Size(236, 0);
            _macroProgressLabel.ForeColor = _theme.TextMuted;
            playGroup.Controls.Add(_macroProgressLabel);

            // ── Live Monitor ───────────────────────────────────────────────────
            // A real-time view of macro steps: it fills as you record, shows the
            // selected macro at rest, and highlights the current step during
            // playback (auto-scrolling to follow along).
            var liveGroup = UiFactory.Group(Utils.Localization.T("Live Monitor"), 12, 634, 700, 240, CardIcon.Wave);

            _liveHeaderLabel = UiFactory.Label("Select, record, or play a macro to see steps here.", 16, 26, FontStyle.Italic, 9f);
            _liveHeaderLabel.AutoSize = false;
            _liveHeaderLabel.Width = 668;
            _liveHeaderLabel.Height = 20;
            _liveHeaderLabel.ForeColor = _theme.TextMuted;
            liveGroup.Controls.Add(_liveHeaderLabel);

            _liveStepList = new LiveStepListView
            {
                Left = 16,
                Top = 50,
                Width = 668,
                Height = 186
            };
            _liveStepList.Columns.Add("#", 54);
            _liveStepList.Columns.Add(Utils.Localization.T("Time"), 84);
            _liveStepList.Columns.Add(Utils.Localization.T("Action"), 132);
            // Detail is sized to whatever is left over (see FitLastColumn) rather than
            // a fixed 350: the fixed widths used to total a fraction MORE than the
            // client area once the scrollbar appeared, which put a useless horizontal
            // scrollbar across the bottom of the card.
            _liveStepList.Columns.Add(Utils.Localization.T("Detail"), 320);
            _liveStepList.ApplyTheme(_theme);
            _liveStepList.FitLastColumn();
            liveGroup.Controls.Add(_liveStepList);

            page.Controls.Add(help);
            page.Controls.Add(listLabel);
            page.Controls.Add(_macroSearchBox);
            page.Controls.Add(_macroSortCombo);
            page.Controls.Add(_macroListBox);
            page.Controls.Add(manageGroup);
            page.Controls.Add(_pinMacroBtn);
            page.Controls.Add(_resetMacroStatsBtn);
            page.Controls.Add(_macroSummaryLabel);
            page.Controls.Add(recGroup);
            page.Controls.Add(playGroup);
            page.Controls.Add(liveGroup);

            // Just-for-fun: a colourful trail that follows the mouse cursor.
            _cursorTrailCheck = UiFactory.Check("Colorful cursor trail (just for fun)", 12, 854);
            _cursorTrailCheck.Checked = _settings != null && _settings.CursorTrailEnabled;
            _cursorTrailCheck.CheckedChanged += OnCursorTrailChanged;
            page.Controls.Add(_cursorTrailCheck);

            // If the intro paragraph wraps to more lines than designed (e.g. at a
            // higher display scale), push everything below it down so nothing
            // overlaps. The intro itself stays put.
            ShiftBelowIntro(page, help, helpText, 720, firstRowY: 60);

            _tabs.TabPages.Add(page);
        }

        /// <summary>
        /// Measures an intro label's wrapped height and, if it would reach below the
        /// first row of controls, shifts every other control on the page down by the
        /// overflow. Keeps hand-positioned layouts from overlapping at higher DPI.
        /// </summary>
        private static void ShiftBelowIntro(Control page, Control intro, string text, int wrapWidth, int firstRowY)
        {
            int introBottom = 12 + TextRenderer.MeasureText(
                text, intro.Font, new Size(wrapWidth, 0), TextFormatFlags.WordBreak).Height;
            int delta = (introBottom + 12) - firstRowY;
            if (delta <= 0)
            {
                return;
            }

            foreach (Control c in page.Controls)
            {
                if (!ReferenceEquals(c, intro))
                {
                    c.Top += delta;
                }
            }
        }

        private void WireMacroEvents()
        {
            _recorder.ActionRecorded += (s, action) => UiInvoke(() =>
            {
                _recordedStepCount++;
                _recordStatusLabel.Text = Utils.Localization.F(
                    "Recording… {0} steps captured.", _recordedStepCount);
                _recordIndicator?.SetStepCount(_recordedStepCount);

                // Live feed: append the step as it is captured.
                if (_liveRecording)
                {
                    AppendLiveStep(action);
                }
            });

            _player.PlaybackStarted += (s, e) => UiInvoke(() =>
            {
                _playMacroBtn.Enabled = false;
                _stopPlayBtn.Enabled = true;
                RefreshBusyLock();
                if (_playOnceBtn != null) _playOnceBtn.Enabled = false;
                _statusState.Text = Utils.Localization.T("Playing macro");
                _macroProgressBar.Value = 0;
                _playbackCurrentLoop = 0;
                _macroProgressLabel.Text = Utils.Localization.T("Playing…");
                _liveHeaderLabel.ForeColor = _theme.Accent;
                string playName = _liveMonitorMacro != null
                    ? _liveMonitorMacro.Name : Utils.Localization.T("macro");
                string playMeta = _liveMonitorMacro != null
                    ? Utils.Localization.F("  \u2014  {0} steps, \u2248{1}",
                        _liveMonitorMacro.StepCount,
                        FormatLiveTime(_liveMonitorMacro.EstimatedDurationMs))
                    : "";
                _liveHeaderLabel.Text = Utils.Localization.F("\u25B6 Playing {0}", playName) + playMeta;
            });

            _player.LoopChanged += (s, loop) => UiInvoke(() =>
            {
                _playbackCurrentLoop = loop;
            });

            _player.StepExecuted += (s, index) => UiInvoke(() =>
            {
                if (_playbackTotalSteps > 0)
                {
                    // Finite runs fill across ALL loops (loop 2/4 sits at ~25–50%),
                    // not 0→100 restarting every loop. Infinite runs keep the
                    // per-loop fill — the only sensible reading for them.
                    long doneUnits = _playbackTotalLoops > 0
                        ? (long)Math.Max(0, _playbackCurrentLoop - 1) * _playbackTotalSteps + index + 1
                        : index + 1;
                    long totalUnits = _playbackTotalLoops > 0
                        ? (long)_playbackTotalLoops * _playbackTotalSteps
                        : _playbackTotalSteps;
                    // Guard against divide-by-zero (e.g. a macro with no steps, or
                    // totals not yet computed) which would crash the progress update.
                    int pct = totalUnits > 0 ? (int)(doneUnits * 100 / totalUnits) : 0;
                    if (pct < 0) pct = 0;
                    if (pct > 100) pct = 100;
                    if (_macroProgressBar.Value != pct) _macroProgressBar.Value = pct;

                    string loop = Utils.Localization.T("Loop");
                    string step = Utils.Localization.T("step");
                    string loopText = _playbackTotalLoops <= 0
                        ? $"{loop} {_playbackCurrentLoop} (∞)"
                        : $"{loop} {_playbackCurrentLoop} / {_playbackTotalLoops}";

                    // Estimated time remaining for finite runs, from the precomputed
                    // total minus elapsed. Clamped at zero so it never shows negative
                    // when a run overshoots the estimate.
                    string remainText = "";
                    if (_playbackTotalEstimateMs > 0)
                    {
                        double leftMs = _playbackTotalEstimateMs -
                            (DateTime.UtcNow - _playbackStartUtc).TotalMilliseconds;
                        if (leftMs < 0) leftMs = 0;
                        remainText = "  •  " + Utils.Localization.F("~{0} left",
                            FormatDuration(TimeSpan.FromMilliseconds(leftMs)));
                    }

                    _macroProgressLabel.Text = Utils.Localization.F("{0}  •  {1} {2} / {3}{4}",
                        loopText, step, index + 1, _playbackTotalSteps, remainText);
                    UpdateMacroIndicator($"{loopText}  ·  {step} {index + 1}/{_playbackTotalSteps}");
                }

                HighlightLiveStep(index);
            });

            // Raised on the PLAYBACK thread, so it marshals like every other player event.
            // Told through a notification rather than a modal box: a macro can loop, and a
            // dialog would stack one copy per loop behind the game the user is looking at.
            // The full detail and the script's own output are in Live debug.
            _player.ScriptFailed += (s, f) => UiInvoke(() =>
            {
                try
                {
                    string name = f.Action.ScriptFileName();
                    string detail = f.Result != null ? f.Result.Detail : "";
                    TempoNotify(6000, "Tempo",
                        Utils.Localization.F("Script step failed: {0}\n{1}", name, detail),
                        ToolTipIcon.Warning);
                    _recordStatusLabel.Text = Utils.Localization.F("Script step failed: {0}", name);
                }
                catch (Exception ex) { Utils.Logger.Swallow("ScriptFailed handler", ex); }
            });

            _player.PlaybackFinished += (s, e) => UiInvoke(() =>
            {
                _stopPlayBtn.Enabled = false;
                // Re-gate Play/Edit/etc. on whether something is still selected,
                // instead of blindly enabling Play (which could enable it with no
                // selection if the list changed during playback).
                RefreshMacroButtons();
                _statusState.Text = Utils.Localization.T("Idle");
                _macroProgressBar.Value = 0;
                _macroProgressLabel.Text = Utils.Localization.T("Ready.");
                ClearLiveHighlight();
                HideMacroIndicator();
                _liveHeaderLabel.ForeColor = _theme.TextMuted;
                _liveHeaderLabel.Text = Utils.Localization.T("Finished \u2014 select a macro to view its steps.");
                UpdateMacroEstTime();
                RefreshBusyLock();

                // Same completion notice as the clicker's fixed runs: chime + tray
                // notice when a finite playback ends on its own. Manual stops stay
                // silent, and infinite loops (0) never finish "naturally".
                if (_settings != null && _settings.NotifyOnRepeatFinish &&
                    _playbackTotalLoops > 0 && _player != null && _player.LastRunCompletedNaturally)
                {
                    try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                    try
                    {
                        string nm = _liveMonitorMacro != null && !string.IsNullOrEmpty(_liveMonitorMacro.Name)
                            ? _liveMonitorMacro.Name : "Macro";
                        TempoNotify(2000, "Tempo",
                            "Macro '" + nm + "' finished (" + _playbackTotalLoops +
                            (_playbackTotalLoops == 1 ? " loop)." : " loops)."),
                            ToolTipIcon.Info);
                    }
                    catch { }
                }

                // Bring the window back ONLY if WE auto-minimised it for this playback.
                // Previously any finishing macro popped the window to Normal and stole
                // focus even when the user had minimised Tempo themselves (e.g. left it
                // running while they did something else) — disruptive mid-task. The flag
                // is consumed here whether the run finished naturally or was stopped.
                bool wasAutoMin = _autoMinimizedForPlayback;
                _autoMinimizedForPlayback = false;
                if (wasAutoMin && WindowState == FormWindowState.Minimized)
                {
                    try { WindowState = FormWindowState.Normal; Activate(); } catch { }
                }
            });
        }

        // ── Live Monitor ───────────────────────────────────────────────────────

        /// <summary>Fills the live monitor with a macro's steps (used at rest / on selection / on play).</summary>
        private void PopulateLiveMonitor(Macro macro)
        {
            _liveMonitorMacro = macro;
            _liveStepList.BeginUpdate();
            _liveStepList.Items.Clear();
            _liveHighlightIndex = -1;

            // Huge macros (a long recording can run to hundreds of thousands of
            // steps) would freeze the UI if every row were created. The first
            // window is enough to inspect — and playback highlighting stays
            // index-aligned with it; beyond the window it simply stops following.
            const int LivePopulateMaxRows = 2000;
            bool truncated = macro != null && macro.Actions.Count > LivePopulateMaxRows;
            int rows = macro == null ? 0 : Math.Min(macro.Actions.Count, LivePopulateMaxRows);

            long cumulative = 0;
            if (macro != null)
            {
                for (int i = 0; i < rows; i++)
                {
                    MacroAction a = macro.Actions[i];
                    var item = new ListViewItem((i + 1).ToString());
                    item.SubItems.Add(FormatLiveTime(cumulative));
                    item.SubItems.Add(Models.MacroAction.FriendlyType(a.Type));
                    item.SubItems.Add(LiveDetail(a));
                    item.Tag = a.Type;      // exact colour, no name matching needed
                    _liveStepList.Items.Add(item);

                    if (a.Type == MacroActionType.Delay)
                    {
                        cumulative += a.DelayMilliseconds;
                    }
                }
            }

            _liveStepList.EndUpdate();
            // After the rows exist, so the column fit accounts for the scrollbar.
            _liveStepList.RefreshLayout();

            if (macro != null)
            {
                _liveHeaderLabel.Text =
                    Utils.Localization.F("{0}  —  {1:N0} steps, ≈{2}",
                        macro.Name, macro.StepCount, FormatLiveTime(macro.EstimatedDurationMs)) +
                    (truncated
                        ? Utils.Localization.F("  (showing first {0:N0})", LivePopulateMaxRows) : "") +
                    (macro.TimesPlayed > 0
                        ? Utils.Localization.F("  •  played {0}×", macro.TimesPlayed) : "") +
                    LastPlayedText(macro) +
                    (!string.IsNullOrWhiteSpace(macro.Notes) ? $"   “{macro.Notes}”" : "");
            }
        }

        private static string LastPlayedText(Macro m)
        {
            if (m == null || m.LastPlayedUtc == null)
            {
                return "";
            }
            return "  •  " + Utils.Localization.F("last {0}",
                m.LastPlayedUtc.Value.ToLocalTime().ToString("d MMM HH:mm"));
        }

        /// <summary>Appends one captured step to the live monitor and scrolls to it.</summary>
        /// <summary>
        /// The live list keeps only the most recent rows during recording; the
        /// recording itself is unaffected. Without a cap, a long session (hours —
        /// the recorder happily runs for 24h+) would pour millions of ListView
        /// rows into the UI and exhaust memory long before the engine cared.
        /// </summary>
        private const int LiveRecordingMaxRows = 1000;
        private long _liveStepTotal;

        private void AppendLiveStep(MacroAction action)
        {
            _liveStepTotal++;
            var item = new ListViewItem(_liveStepTotal.ToString());
            item.SubItems.Add(FormatLiveTime(_liveCumulativeMs));
            item.SubItems.Add(Models.MacroAction.FriendlyType(action.Type));
            item.SubItems.Add(LiveDetail(action));
            item.Tag = action.Type;

            if (_liveStepList.Items.Count >= LiveRecordingMaxRows)
            {
                _liveStepList.Items.RemoveAt(0);
            }
            _liveStepList.Items.Add(item);
            item.EnsureVisible();

            if (action.Type == MacroActionType.Delay)
            {
                _liveCumulativeMs += action.DelayMilliseconds;
            }

            _liveHeaderLabel.Text = _liveStepTotal > LiveRecordingMaxRows
                ? Utils.Localization.F("● Recording…  {0:N0} steps captured (showing last {1:N0})",
                    _liveStepTotal, LiveRecordingMaxRows)
                : Utils.Localization.F("● Recording…  {0} steps captured", _liveStepTotal);
        }

        /// <summary>Highlights the step currently being played and keeps it in view.</summary>
        private void HighlightLiveStep(int index)
        {
            if (index < 0 || index >= _liveStepList.Items.Count)
            {
                return;
            }

            // The list owner-draws itself from this one index, so moving the playhead
            // is a single assignment. The old code stamped BackColor/ForeColor onto the
            // row and reset the previous one to the LIST's colours — which would now
            // erase that row's own action colour and its stripe.
            _liveStepList.SelectedItems.Clear();
            _liveStepList.Items[index].Selected = false;
            _liveStepList.MoveHighlightTo(index);
            _liveHighlightIndex = index;
        }

        private void ClearLiveHighlight()
        {
            _liveStepList.HighlightIndex = -1;
            _liveHighlightIndex = -1;
            _liveStepList.SelectedItems.Clear();
            if (_liveMonitorMacro != null)
            {
                // Same key as PopulateLiveSteps above, so both headers share one translation.
                _liveHeaderLabel.Text =
                    Utils.Localization.F("{0}  —  {1:N0} steps, ≈{2}",
                        _liveMonitorMacro.Name, _liveMonitorMacro.StepCount,
                        FormatLiveTime(_liveMonitorMacro.EstimatedDurationMs)) +
                    (_liveMonitorMacro.TimesPlayed > 0
                        ? Utils.Localization.F("  •  played {0}×", _liveMonitorMacro.TimesPlayed) : "") +
                    LastPlayedText(_liveMonitorMacro);
            }
        }

        private static string FormatLiveTime(long ms)
        {
            if (ms >= 60_000)
            {
                long min = ms / 60_000;
                double rem = (ms - min * 60_000) / 1000.0;
                return $"{min}m {rem:0.0}s";
            }
            if (ms >= 1_000)
            {
                return $"{ms / 1000.0:0.00}s";
            }
            return ms + "ms";
        }

        private static string LiveDetail(MacroAction a)
        {
            switch (a.Type)
            {
                case MacroActionType.Delay: return a.DelayMilliseconds + " ms";
                case MacroActionType.MouseMove: return $"({a.X}, {a.Y})";
                case MacroActionType.Wheel: return "delta " + a.WheelDelta;
                case MacroActionType.KeyDown:
                case MacroActionType.KeyUp: return MacroAction.KeyName(a.VirtualKey);
                default: return $"({a.X}, {a.Y})";
            }
        }

        /// <summary>
        /// Loads the selected macro's stored default loops/speed/countdown into the
        /// playback controls. Guarded so it does not trigger the change handler that
        /// writes values back.
        /// </summary>
        /// <summary>
        /// Enables or disables the per-macro action buttons based on whether a macro
        /// is selected. Without this the Play/Edit/Delete buttons stayed enabled with
        /// an empty list or no selection, so clicking them only produced a warning.
        /// Skipped while playing/recording so it never fights those states.
        /// </summary>
        private void RefreshMacroButtons()
        {
            bool playing = _player != null && _player.IsPlaying;
            bool recording = _liveRecording;
            if (playing || recording)
            {
                return;
            }

            bool has = SelectedMacro() != null;
            if (_playMacroBtn != null) _playMacroBtn.Enabled = has;
            if (_playOnceBtn != null) _playOnceBtn.Enabled = has;
            if (_editMacroBtn != null) _editMacroBtn.Enabled = has;
            if (_renameMacroBtn != null) _renameMacroBtn.Enabled = has;
            if (_deleteMacroBtn != null) _deleteMacroBtn.Enabled = has;
            if (_exportMacroBtn != null) _exportMacroBtn.Enabled = has;
            if (_pinMacroBtn != null) _pinMacroBtn.Enabled = has;
        }

        private void LoadMacroDefaultsIntoUi()
        {
            Macro macro = SelectedMacro();
            RefreshMacroButtons();
            if (macro == null)
            {
                return;
            }

            _suppressMacroDefaults = true;
            try
            {
                _macroLoopNum.Value = Clamp(macro.DefaultLoops, (int)_macroLoopNum.Minimum, (int)_macroLoopNum.Maximum);
                _macroSpeedNum.Value = Clamp(macro.DefaultSpeed, (int)_macroSpeedNum.Minimum, (int)_macroSpeedNum.Maximum);
                _macroCountdownNum.Value = Clamp(macro.PreplayCountdownSeconds, (int)_macroCountdownNum.Minimum, (int)_macroCountdownNum.Maximum);
                _macroLoopDelayNum.Value = Clamp(macro.LoopDelayMs, (int)_macroLoopDelayNum.Minimum, (int)_macroLoopDelayNum.Maximum);
                if (_macroSmoothCheck != null) _macroSmoothCheck.Checked = macro.SmoothMovement;
                if (_macroPreserveHoldsCheck != null) _macroPreserveHoldsCheck.Checked = macro.PreserveKeyHolds;
                if (_pinMacroBtn != null)
                {
                    _pinMacroBtn.Text = Utils.Localization.T(macro.IsFavorite ? "★ Unpin" : "★ Pin");
                }
            }
            finally
            {
                _suppressMacroDefaults = false;
            }

            // Show the selected macro's steps in the live monitor (unless we're
            // mid-recording, in which case the monitor is busy with the feed).
            if (!_liveRecording)
            {
                PopulateLiveMonitor(macro);
            }

            UpdateMacroEstTime();
        }

        /// <summary>
        /// Writes the current playback control values back onto the selected macro
        /// and persists them, so each macro remembers its own preferences.
        /// </summary>
        private void OnMacroDefaultChanged(object sender, EventArgs e)
        {
            if (_suppressMacroDefaults)
            {
                return;
            }

            Macro macro = SelectedMacro();
            if (macro == null)
            {
                return;
            }

            macro.DefaultLoops = (int)_macroLoopNum.Value;
            macro.DefaultSpeed = (int)_macroSpeedNum.Value;
            macro.PreplayCountdownSeconds = (int)_macroCountdownNum.Value;
            macro.LoopDelayMs = (int)_macroLoopDelayNum.Value;
            _macros.Save();
            UpdateMacroEstTime();
        }

        /// <summary>
        /// Shows the estimated total playback time for the selected macro (with the
        /// current loop/speed/delay settings) in the progress label while idle.
        /// </summary>
        private void UpdateMacroEstTime()
        {
            if (_macroProgressLabel == null || _player == null || _player.IsPlaying)
            {
                return;
            }

            Macro macro = SelectedMacro();
            if (macro == null || macro.StepCount == 0)
            {
                _macroProgressLabel.Text = Utils.Localization.T("Ready.");
                return;
            }

            int loops = (int)_macroLoopNum.Value;
            double speed = (double)_macroSpeedNum.Value / 10.0;
            if (speed <= 0) speed = 1.0;

            string steps = "  \u2022  " + Utils.Localization.F("{0} step(s)", macro.StepCount);

            if (loops <= 0)
            {
                _macroProgressLabel.Text = Utils.Localization.T("Est. total: ∞ (looping)") + steps;
                return;
            }

            double oneLoopMs = macro.EstimatedDurationMs / speed;
            double totalMs = oneLoopMs * loops + (double)_macroLoopDelayNum.Value * (loops - 1);
            _macroProgressLabel.Text = Utils.Localization.T("Est. total: ~")
                + FormatDuration(TimeSpan.FromMilliseconds(totalMs)) + steps;
        }

        private void RefreshMacroList()
        {
            if (_macroListBox == null)
            {
                return;
            }

            // Remember the currently selected macro so a refresh (search change, a
            // rename, etc.) doesn't drop the user's selection out from under them.
            Macro previouslySelected = _macroListBox.SelectedItem as Macro;

            _macroListBox.BeginUpdate();
            _macroListBox.Items.Clear();
            string filter = _macroFilter?.Trim() ?? string.Empty;

            // Favourites first, otherwise preserve the current order.
            var ordered = new System.Collections.Generic.List<Macro>();
            foreach (var m in _macros.Macros) if (m.IsFavorite) ordered.Add(m);
            foreach (var m in _macros.Macros) if (!m.IsFavorite) ordered.Add(m);

            foreach (var m in ordered)
            {
                if (filter.Length == 0 || MacroMatchesFilter(m, filter))
                {
                    _macroListBox.Items.Add(m);
                }
            }

            // Re-select the same macro if it's still in the (possibly filtered) list.
            if (previouslySelected != null)
            {
                int idx = _macroListBox.Items.IndexOf(previouslySelected);
                if (idx >= 0)
                {
                    _macroListBox.SelectedIndex = idx;
                }
            }
            _macroListBox.EndUpdate();

            // "The macro library changed" has one choke point, and this is it, so the
            // bin button's count and the quick-play pickers both refresh from here
            // rather than from every add / rename / delete / sort site.
            UpdateMacroRecycleButton();
            RefreshMacroSlotCombos();

            if (_macroSummaryLabel != null)
            {
                int total = _macros.Macros.Count;
                int shown = _macroListBox.Items.Count;
                long steps = 0;
                foreach (var m in _macros.Macros) steps += m.StepCount;
                if (total == 0)
                {
                    _macroSummaryLabel.Text = Utils.Localization.T("No macros saved yet.");
                }
                else if (filter.Length > 0 && shown != total)
                {
                    // Searching: show how many of the library matched, so the count
                    // reflects what's on screen rather than the whole library.
                    _macroSummaryLabel.Text = shown == 0
                        ? Utils.Localization.F("No macros match \u201c{0}\u201d  \u2022  {1} total", filter, total)
                        : Utils.Localization.F("{0} of {1} macros match  \u2022  {2:N0} steps total",
                            shown, total, steps);
                }
                else
                {
                    // Singular and plural as separate keys \u2014 "1 macro"/"2 macros" cannot be
                    // built by appending an "s" in most of the languages Tempo ships.
                    _macroSummaryLabel.Text = total == 1
                        ? Utils.Localization.F("{0} macro  \u2022  {1:N0} steps total", total, steps)
                        : Utils.Localization.F("{0} macros  \u2022  {1:N0} steps total", total, steps);
                }
            }

            // Selection may have changed (e.g. a delete) — keep the action buttons
            // in step with whether anything is selected.
            RefreshMacroButtons();
        }

        /// <summary>
        /// Whether a macro answers the search box — by name, by its notes, or by what it
        /// actually DOES.
        ///
        /// Name and notes alone were not much use against a library of auto-named
        /// recordings: "Macro 16-11-12" tells you nothing, so finding the one that presses
        /// F5 meant opening them one at a time. The steps are searched too now, so "F5",
        /// "right", "wheel" or a script's filename all find it.
        ///
        /// Scanned directly rather than cached. A cache would have to be invalidated on
        /// every record, edit, import, merge and fix, and a stale search index is a bug
        /// you only notice when it hides the macro you were looking for. The whole
        /// library here is a few thousand steps and each one is a couple of string
        /// compares that stop at the first hit.
        /// </summary>
        private static bool MacroMatchesFilter(Macro m, string filter)
        {
            if (m == null) { return false; }
            const StringComparison Ci = StringComparison.OrdinalIgnoreCase;

            if (m.Name != null && m.Name.IndexOf(filter, Ci) >= 0) { return true; }
            if (m.Notes != null && m.Notes.IndexOf(filter, Ci) >= 0) { return true; }
            if (m.Actions == null) { return false; }

            foreach (MacroAction a in m.Actions)
            {
                if (a == null) { continue; }

                // The friendly type name — "Left click", "Key down", "Run script" — which
                // is the same wording the editor and the Live Monitor show.
                string type = MacroAction.FriendlyType(a.Type);
                if (type != null && type.IndexOf(filter, Ci) >= 0) { return true; }

                if (a.VirtualKey != 0)
                {
                    string key = MacroAction.KeyName(a.VirtualKey);
                    if (key != null && key.IndexOf(filter, Ci) >= 0) { return true; }
                }

                if (!string.IsNullOrEmpty(a.ScriptPath))
                {
                    if (a.ScriptPath.IndexOf(filter, Ci) >= 0) { return true; }
                }
            }
            return false;
        }

        private Macro SelectedMacro()
        {
            return _macroListBox?.SelectedItem as Macro;
        }

        private void SelectMacro(Macro m)
        {
            if (m != null && _macroListBox.Items.Contains(m))
            {
                _macroListBox.SelectedItem = m;
            }
        }

        // ── Recording (usable from button or hotkey) ───────────────────────────

        /// <summary>Toggles recording; bound to the Record hotkey.</summary>
        private void ToggleMacroRecording()
        {
            if (_recorder.IsRecording)
            {
                StopRecording();
            }
            else
            {
                StartRecording();
            }
        }

        private void StartRecording()
        {
            if (_recorder.IsRecording)
            {
                return;
            }

            // Optional "3, 2, 1" countdown so you can switch to the target window before
            // capture begins (the overlay's own focus change isn't recorded — capture only
            // starts once it closes). Cancelling (Esc) aborts before any state changes.
            int recCountdown = _settings != null ? _settings.RecordCountdownSeconds : 0;
            if (recCountdown > 0)
            {
                using (var overlay = new CountdownOverlayForm(_theme, recCountdown))
                {
                    if (overlay.ShowDialog() != DialogResult.OK)
                    {
                        return;
                    }
                }
            }

            // CONFLICT with camera-relative movement, and a silent one. While it is armed
            // it swallows the physical W/A/S/D so it can inject its own re-mixed keys — so
            // the recorder, sitting on a different hook further down the chain, never sees
            // them. A recording made in that state is missing every movement key and gives
            // no hint why; you find out when you play it back and the character stands
            // still. Recording is the explicit action here, so movement yields to it.
            // After the countdown, so cancelling with Esc changes nothing.
            DisarmMovementBecause("recording a macro needs the real W/A/S/D");

            _recorder.RecordMovements = _recordMovesCheck.Checked;
            _recorder.RecordKeyboard = _recordKeysCheck.Checked;

            // Do not capture any key that participates in toggling recording or in
            // emergency stop — including the modifier keys of those combos.
            // Every key that CONTROLS macros must stay out of the recording, not just
            // the record toggle.
            //
            // Only ToggleRecordMacro and EmergencyStop were excluded, so a key bound to
            // "Stop macro" or "Play macro" was captured like any other keystroke and
            // baked into the take. Playing that macro back then pressed it — and the
            // macro stopped itself part-way through, or re-triggered playback, entirely
            // on its own. From the outside it just looks like macros randomly cut short.
            // Anything that starts, stops or re-enters playback belongs on this list for
            // the same reason the record toggle already did.
            _recorder.ExcludedVirtualKeys.Clear();
            AddExclusions(_settings.HotkeyFor(HotkeyAction.ToggleRecordMacro));
            AddExclusions(_settings.HotkeyFor(HotkeyAction.EmergencyStop));
            AddExclusions(_settings.HotkeyFor(HotkeyAction.StopMacro));
            AddExclusions(_settings.HotkeyFor(HotkeyAction.PlayMacro));
            AddExclusions(_settings.HotkeyFor(HotkeyAction.PlayMacro1));
            AddExclusions(_settings.HotkeyFor(HotkeyAction.PlayMacro2));
            AddExclusions(_settings.HotkeyFor(HotkeyAction.PlayMacro3));

            _recordedStepCount = 0;

            // Remember whether to append to the currently selected macro.
            _appendTarget = _appendRecordCheck.Checked ? SelectedMacro() : null;

            // Reset the live monitor for a fresh recording feed.
            _liveRecording = true;
            _liveCumulativeMs = 0;
            _liveStepTotal = 0;
            _liveStepList.Items.Clear();
            _liveHighlightIndex = -1;
            _liveHeaderLabel.Text = Utils.Localization.T("● Recording…");

            string name = "Macro " + DateTime.Now.ToString("HH-mm-ss");
            if (!_recorder.Start(name))
            {
                _liveRecording = false;
                ShowWarning("Could not start recording. The input hook failed to install.");
                return;
            }

            _recordBtn.Enabled = false;
            RefreshBusyLock();
            _stopRecordBtn.Enabled = true;
            _recordStatusLabel.Text = Utils.Localization.T("Recording… bind/press the Record or Emergency-stop hotkey to finish.");
            _statusState.Text = Utils.Localization.T("Recording macro");

            // Show a small always-on-top REC badge so the user knows recording is
            // live even when this window is not focused.
            ShowRecordingIndicator();

            // Get the window out of the way so it isn't captured in the recording —
            // but only if there's a hotkey to stop with, so the user is never stuck
            // with no visible way to finish.
            bool canStopByHotkey =
                (_settings?.HotkeyFor(HotkeyAction.ToggleRecordMacro)?.IsValid ?? false) ||
                (_settings?.HotkeyFor(HotkeyAction.EmergencyStop)?.IsValid ?? false);
            if (_settings != null && _settings.MinimizeWhileRecording && canStopByHotkey)
            {
                try { WindowState = FormWindowState.Minimized; _autoMinimizedForRecording = true; } catch { }
            }
        }

        private void ShowRecordingIndicator()
        {
            HideRecordingIndicator();
            try
            {
                // Tell the user which key stops recording — prefer the dedicated
                // record toggle, otherwise the emergency-stop key.
                string hint = null;
                var rec = _settings?.HotkeyFor(HotkeyAction.ToggleRecordMacro);
                if (rec != null && rec.IsValid)
                {
                    hint = rec.ToDisplayString();
                }
                else
                {
                    var stop = _settings?.HotkeyFor(HotkeyAction.EmergencyStop);
                    if (stop != null && stop.IsValid)
                    {
                        hint = stop.ToDisplayString();
                    }
                }

                _recordIndicator = new RecordingIndicatorForm(_theme, hint);
                _recordIndicator.Show();
            }
            catch
            {
                _recordIndicator = null; // non-fatal: indicator is cosmetic
            }
        }

        private void HideRecordingIndicator()
        {
            if (_recordIndicator != null)
            {
                try
                {
                    _recordIndicator.Close();
                    _recordIndicator.Dispose();
                }
                catch { /* ignore */ }
                _recordIndicator = null;
            }
        }

        private void StopRecording()
        {
            if (!_recorder.IsRecording)
            {
                return;
            }

            _lastRecorded = _recorder.Stop();
            _recordBtn.Enabled = true;
            _stopRecordBtn.Enabled = false;
            _statusState.Text = Utils.Localization.T("Idle");
            _liveRecording = false;
            HideRecordingIndicator();
            RefreshBusyLock();

            // Bring the window back ONLY if WE auto-minimised it for this recording — the
            // same rule playback already follows. This previously restored on ANY minimised
            // state, so a user who started recording and then minimised Tempo themselves
            // (or who has the minimise option off entirely) had the window pop back up and
            // steal focus the moment recording ended.
            bool wasAutoMin = _autoMinimizedForRecording;
            _autoMinimizedForRecording = false;
            if (wasAutoMin && WindowState == FormWindowState.Minimized)
            {
                try { WindowState = FormWindowState.Normal; Activate(); } catch { }
            }

            if (_lastRecorded != null && _lastRecorded.StepCount > 0)
            {
                if (_appendTarget != null && _macros.IndexOf(_appendTarget.Name) >= 0)
                {
                    // Append the freshly-recorded steps to the existing macro. A
                    // short separator delay keeps the two segments from running
                    // into each other.
                    if (_appendTarget.Actions.Count > 0)
                    {
                        _appendTarget.Actions.Add(
                            new MacroAction(MacroActionType.Delay) { DelayMilliseconds = 500 });
                    }
                    _appendTarget.Actions.AddRange(_lastRecorded.Actions);

                    _macros.Save();
                    RefreshMacroList();
                    SelectMacro(_appendTarget);
                    PopulateLiveMonitor(_appendTarget);
                    _recordStatusLabel.Text = Utils.Localization.F(
                        "Appended {0} steps to '{1}'.", _lastRecorded.StepCount, _appendTarget.Name);
                }
                else
                {
                    // Let the user name the recording (and add a note / pin it)
                    // while it's fresh. "Keep default" or closing still saves under
                    // the automatic name — a recording is never thrown away.
                    using (var dlg = new SaveMacroForm(_theme, _lastRecorded))
                    {
                        if (dlg.ShowDialog(this) == DialogResult.OK)
                        {
                            string chosen = dlg.MacroName?.Trim();
                            if (!string.IsNullOrEmpty(chosen))
                            {
                                _lastRecorded.Name = chosen;
                            }
                            _lastRecorded.Notes = dlg.Notes ?? "";
                            _lastRecorded.IsFavorite = dlg.Pin;
                        }
                    }

                    _macros.Add(_lastRecorded);
                    _macros.Save();
                    RefreshMacroList();
                    SelectMacro(_lastRecorded);
                    PopulateLiveMonitor(_lastRecorded);
                    _recordStatusLabel.Text = Utils.Localization.F("Saved '{0}' ({1} steps).",
                        _lastRecorded.Name, _lastRecorded.StepCount);
                }
            }
            else
            {
                _recordStatusLabel.Text = Utils.Localization.T("Nothing was recorded.");
            }

            _appendTarget = null;

            // A hidden (tray) window still comes back — being left running with no visible
            // way in is worse than an unwanted pop-up.
            if (!Visible)
            {
                Show();
            }
            // Un-minimising, though, uses the same rule as the block at the top of this
            // method and as playback: only if WE minimised it. This second restore used to
            // be unconditional, which quietly defeated that check — a user who minimised
            // Tempo themselves still had it pop up and steal focus when recording ended.
            if (wasAutoMin && WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
        }

        // ── Playback ───────────────────────────────────────────────────────────

        // True only while a playback that WE auto-minimised the window for is in flight,
        // so PlaybackFinished restores the window exactly when it should — and never
        // steals focus from a window the user minimised themselves.
        private bool _autoMinimizedForPlayback;
        private bool _autoMinimizedForRecording;

        private void OnMacroListKeyDown(object sender, KeyEventArgs e)
        {
            if (SelectedMacro() == null)
            {
                return;
            }

            // Ctrl+D duplicates, matching the Multi-Point list.
            if (e.Control && e.KeyCode == Keys.D)
            {
                OnDuplicateMacro(sender, EventArgs.Empty);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            switch (e.KeyCode)
            {
                case Keys.Enter:
                    OnPlayMacroClicked(sender, EventArgs.Empty);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    break;
                case Keys.Delete:
                    OnDeleteMacroClicked(sender, EventArgs.Empty);
                    e.Handled = true;
                    break;
                case Keys.F2:
                    OnRenameMacro(sender, EventArgs.Empty);
                    e.Handled = true;
                    break;
            }
        }

        private void OnPlayMacroClicked(object sender, EventArgs e)
        {
            Macro macro = SelectedMacro();
            if (macro == null)
            {
                ShowWarning("Select a macro to play first.");
                return;
            }

            PlayMacro(macro);
        }

        private void OnPlayMacroOnceClicked(object sender, EventArgs e)
        {
            Macro macro = SelectedMacro();
            if (macro == null)
            {
                ShowWarning("Select a macro to play first.");
                return;
            }

            // Play a single pass regardless of the configured loop count.
            PlayMacro(macro, 1);
        }

        /// <summary>Plays the selected macro; bound to the Play hotkey.</summary>
        private void PlaySelectedMacroViaHotkey()
        {
            Macro macro = SelectedMacro();
            if (macro != null)
            {
                PlayMacro(macro);
            }
        }

        private void PlayMacro(Macro macro, int? loopsOverride = null)
        {
            if (macro == null || _player.IsPlaying)
            {
                return;
            }

            if (macro.StepCount == 0)
            {
                ShowInfo("This macro has no steps yet — record or edit it first.");
                return;
            }

            // Capture the window the macro is aimed at BEFORE anything can steal focus.
            // When playback is triggered by a hotkey while the user is in a game, that
            // game is the foreground window right now. The keystrokes we synthesise go to
            // whatever holds keyboard focus, so if Tempo's own countdown overlay or its
            // self-minimise below hands focus to the desktop/Tempo, WASD / arrow keys land
            // in the wrong window and the character doesn't move — the reported "macro
            // doesn't work for movement in-game". We re-assert this window just before we
            // start sending input. If Tempo itself is foreground (played from the in-app
            // Play button rather than a hotkey) there's no game to aim at, so we skip it.
            IntPtr playTarget = AutoClicker.Native.NativeMethods.GetForegroundWindow();
            bool haveExternalTarget = playTarget != IntPtr.Zero && !IsOwnWindow(playTarget);

            int countdown = (int)_macroCountdownNum.Value;
            if (countdown > 0)
            {
                using (var overlay = new CountdownOverlayForm(_theme, countdown))
                {
                    // No owner: Play can be triggered by hotkey while the main
                    // window is hidden in the tray, and a hidden owner breaks modal
                    // display.
                    if (overlay.ShowDialog() != DialogResult.OK)
                    {
                        _macroProgressLabel.Text = Utils.Localization.T("Playback cancelled.");
                        return;
                    }
                }
            }

            int loops = loopsOverride ?? (int)_macroLoopNum.Value;
            double speed = (double)_macroSpeedNum.Value / 10.0;

            // Remember how many steps so the progress bar can show a percentage.
            _playbackTotalSteps = macro.StepCount;
            _playbackTotalLoops = loops;
            _playbackCurrentLoop = 0;

            // For the "time left" readout: when the run is finite, estimate its
            // total length from the macro's recorded delays, the speed multiplier
            // and the delay between loops.
            _playbackStartUtc = DateTime.UtcNow;
            if (loops > 0 && speed > 0)
            {
                double oneLoopMs = macro.EstimatedDurationMs / speed;
                _playbackTotalEstimateMs = oneLoopMs * loops + (double)_macroLoopDelayNum.Value * (loops - 1);
            }
            else
            {
                _playbackTotalEstimateMs = 0; // infinite loop — no estimate
            }

            // Show the macro in the live monitor so playback can highlight steps.
            PopulateLiveMonitor(macro);

            // Track run statistics.
            macro.TimesPlayed++;
            macro.LastPlayedUtc = DateTime.UtcNow;
            _macros.Save();

            // Same conflict as recording, from the other direction. Playback drives the
            // keyboard; the movement engine is ALSO driving it, holding W/A/S/D down and
            // re-mixing them against a camera estimate the macro knows nothing about. Both
            // reach the game and it adds them, so the character goes somewhere neither
            // asked for — and because movement holds keys rather than tapping them, a key
            // it is holding stays down straight through the macro. The macro is the
            // explicit request, so movement yields to it here too.
            DisarmMovementBecause("playing a macro drives the keyboard itself");

            _statusState.Text = Utils.Localization.T("Playing macro");
            ShowMacroIndicator(macro.Name);

            // Get out of the way during playback (same option as recording), but only
            // if a stop hotkey is bound so the user can always finish while minimised.
            bool canStopByHotkey =
                (_settings?.HotkeyFor(HotkeyAction.StopMacro)?.IsValid ?? false) ||
                (_settings?.HotkeyFor(HotkeyAction.EmergencyStop)?.IsValid ?? false);
            if (_settings != null && _settings.MinimizeWhileRecording && canStopByHotkey)
            {
                try { WindowState = FormWindowState.Minimized; _autoMinimizedForPlayback = true; } catch { }
            }

            // Re-aim keyboard/mouse focus at the game (captured above) so the very first
            // keystrokes land there and not in Tempo/desktop after the countdown + minimise.
            // Without this, movement keys are dropped in-game even though the same macro
            // works when replayed into a normal window. Only done when an external window
            // was foreground at trigger time (i.e. a hotkey fired from inside the game).
            if (haveExternalTarget)
            {
                RefocusPlayTarget(playTarget);
            }

            _player.Play(macro, loops, speed, macro.LoopDelayMs);
        }

        /// <summary>True if <paramref name="hWnd"/> belongs to Tempo (main window, an
        /// owned dialog, or one of its overlays) rather than an external app.</summary>
        private bool IsOwnWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return false;
            }
            if (hWnd == Handle)
            {
                return true;
            }
            try
            {
                foreach (Form f in Application.OpenForms)
                {
                    if (f != null && f.IsHandleCreated && f.Handle == hWnd)
                    {
                        return true;
                    }
                }
            }
            catch { /* best effort */ }
            return false;
        }

        /// <summary>
        /// Brings the macro's target window (the game the user triggered playback over)
        /// back to the foreground so synthesised input is delivered to it. SetForegroundWindow
        /// is best-effort (Windows restricts cross-process focus changes), so we also attach
        /// to its input thread, which reliably lets a background app hand focus back to the
        /// window that was foreground a moment ago.
        /// </summary>
        private void RefocusPlayTarget(IntPtr target)
        {
            if (target == IntPtr.Zero)
            {
                return;
            }
            try
            {
                if (AutoClicker.Native.NativeMethods.SetForegroundWindow(target))
                {
                    return;
                }
                // Fallback: attach our thread's input to the target's so the OS permits the
                // foreground change, set it, then detach.
                uint targetThread = AutoClicker.Native.NativeMethods.GetWindowThreadProcessId(target, out _);
                uint ourThread = AutoClicker.Native.NativeMethods.GetCurrentThreadId();
                if (targetThread != 0 && targetThread != ourThread)
                {
                    AutoClicker.Native.NativeMethods.AttachThreadInput(ourThread, targetThread, true);
                    AutoClicker.Native.NativeMethods.SetForegroundWindow(target);
                    AutoClicker.Native.NativeMethods.AttachThreadInput(ourThread, targetThread, false);
                }
            }
            catch { /* best effort — focus is a nicety, playback still proceeds */ }
        }

        // ── Management ─────────────────────────────────────────────────────────

        private void EditSelectedMacro()
        {
            Macro macro = SelectedMacro();
            if (macro == null)
            {
                return;
            }

            using (var editor = new MacroEditorForm(_theme, macro))
            {
                if (editor.ShowDialog(this) == DialogResult.OK && editor.Result != null)
                {
                    // The edited macro keeps the same name, so Update replaces it.
                    editor.Result.Name = macro.Name;
                    _macros.Update(editor.Result);
                    _macros.Save();
                    RefreshMacroList();
                    SelectMacro(editor.Result);
                }
            }
        }

        private void OnRenameMacro(object sender, EventArgs e)
        {
            Macro macro = SelectedMacro();
            if (macro == null)
            {
                return;
            }

            string newName = TextPromptForm.Show(this, _theme, "Rename Macro",
                "New macro name:", macro.Name);

            if (string.IsNullOrWhiteSpace(newName) || newName == macro.Name)
            {
                return;
            }

            if (!_macros.Rename(macro.Name, newName))
            {
                ShowWarning("A macro with that name already exists.");
                return;
            }

            _macros.Save();
            RefreshMacroList();
            SelectMacro(macro);
        }

        private void OnDuplicateMacro(object sender, EventArgs e)
        {
            Macro macro = SelectedMacro();
            if (macro == null)
            {
                return;
            }

            Macro copy = _macros.Duplicate(macro.Name);
            if (copy != null)
            {
                _macros.Save();
                RefreshMacroList();
                SelectMacro(copy);
            }
        }

        private void MoveMacro(int delta)
        {
            Macro macro = SelectedMacro();
            if (macro == null)
            {
                return;
            }

            if (_macros.Move(macro.Name, delta))
            {
                _macros.Save();
                RefreshMacroList();
                SelectMacro(macro);
            }
        }

        private void OnExportMacro(object sender, EventArgs e)
        {
            Macro macro = SelectedMacro();
            if (macro == null)
            {
                ShowWarning("Select a macro to export first.");
                return;
            }

            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = Utils.Localization.T("Export Macro");
                dialog.Filter = Utils.Localization.T("AutoClicker macro (*.json)|*.json|All files (*.*)|*.*");
                dialog.FileName = MakeSafeFileName(macro.Name) + ".json";

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (MacroStore.ExportToFile(macro, dialog.FileName))
                    {
                        ShowInfo(Localization.F("Macro exported to:\n{0}", dialog.FileName));
                    }
                    else
                    {
                        ShowWarning("Could not export the macro.");
                    }
                }
            }
        }

        private void OnImportMacro(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = Utils.Localization.T("Import Macro");
                dialog.Filter = Utils.Localization.T("AutoClicker macro (*.json)|*.json|All files (*.*)|*.*");
                dialog.CheckFileExists = true;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    Macro imported = _macros.ImportFromFile(dialog.FileName);
                    if (imported != null)
                    {
                        if (!ConfirmImportedScripts(imported)) { return; }
                        _macros.Save();
                        RefreshMacroList();
                        SelectMacro(imported);
                        ShowInfo(Localization.F("Imported '{0}' ({1} steps).", imported.Name, imported.StepCount));
                    }
                    else
                    {
                        ShowWarning("Could not import that file. It may not be a valid macro.");
                    }
                }
            }
        }

        /// <summary>
        /// Asks before keeping an imported macro that runs Python, listing what it would
        /// run. Returns false if the user declines, in which case the import is dropped.
        ///
        /// A macro is a shareable JSON file, and a Script step is a path to a program. So
        /// "import this macro someone sent me" would otherwise mean "run whatever .py that
        /// file points at, on your machine, the moment you press Play" — with no sign
        /// beforehand that the macro does anything but click. Recording, editing and
        /// exporting your OWN macro is untouched; this is only the moment a macro arrives
        /// from somewhere else.
        /// </summary>
        private bool ConfirmImportedScripts(Macro imported)
        {
            try
            {
                var paths = new System.Collections.Generic.List<string>();
                foreach (MacroAction a in imported.Actions)
                {
                    if (a.Type == MacroActionType.Script && !string.IsNullOrWhiteSpace(a.ScriptPath))
                    {
                        if (!paths.Contains(a.ScriptPath)) { paths.Add(a.ScriptPath); }
                    }
                }
                if (paths.Count == 0) { return true; }

                var list = new System.Text.StringBuilder();
                foreach (string p in paths) { list.Append("   •  ").AppendLine(p); }

                Utils.Logger.Warn("[Python] imported macro '" + imported.Name + "' carries " +
                                  paths.Count + " script step(s); asking the user.");

                DialogResult answer = MessageBox.Show(this,
                    Localization.F("'{0}' contains {1} step(s) that run a Python script:\n\n{2}\n"
                        + "Scripts run as you, with your files and your network. Only keep this "
                        + "macro if you trust where it came from and you have read the script.\n\n"
                        + "Import it anyway?", imported.Name, paths.Count, list.ToString()),
                    "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (answer == DialogResult.Yes) { return true; }

                // Declined: take it back out again, so "No" leaves nothing behind.
                try { _macros.Remove(imported.Name); } catch (Exception ex) { Utils.Logger.Swallow("ConfirmImportedScripts.Remove", ex); }
                Utils.Logger.Info("[Python] import declined; the macro was discarded.");
                return false;
            }
            catch (Exception ex)
            {
                Utils.Logger.Swallow("ConfirmImportedScripts", ex);
                return true;    // never block an ordinary import because this check broke
            }
        }

        private void OnEditMacroNotes(object sender, EventArgs e)
        {
            Macro macro = SelectedMacro();
            if (macro == null)
            {
                ShowWarning("Select a macro first.");
                return;
            }

            string note = TextPromptForm.Show(this, _theme,
                "Macro notes",
                "Description / notes for '" + macro.Name + "':",
                macro.Notes ?? "");

            if (note != null)
            {
                macro.Notes = note.Trim();
                _macros.Save();
                if (_liveMonitorMacro == macro)
                {
                    PopulateLiveMonitor(macro);
                }
            }
        }

        private void OnCursorTrailChanged(object sender, EventArgs e)
        {
            if (_settings == null)
            {
                return;
            }
            _settings.CursorTrailEnabled = _cursorTrailCheck.Checked;
            SettingsManager.Save(_settings);
            ApplyCursorTrail(_settings.CursorTrailEnabled);
        }

        // The Macros tab has no "Save Settings" button, so these record toggles must
        // persist immediately (like the cursor-trail toggle above). Without this their
        // on/off state was lost on every launch.
        private void OnRecordMovesChanged(object sender, EventArgs e)
        {
            if (_settings == null)
            {
                return;
            }
            _settings.RecordMacroMovements = _recordMovesCheck.Checked;
            SettingsManager.Save(_settings);
        }

        private void OnRecordKeysChanged(object sender, EventArgs e)
        {
            if (_settings == null)
            {
                return;
            }
            _settings.RecordMacroKeyboard = _recordKeysCheck.Checked;
            SettingsManager.Save(_settings);
        }

        private void OnRecordCountdownChanged(object sender, EventArgs e)
        {
            if (_settings == null || _recordCountdownNum == null)
            {
                return;
            }
            _settings.RecordCountdownSeconds = (int)_recordCountdownNum.Value;
            SettingsManager.Save(_settings);
        }

        private void OnMacroSmoothChanged(object sender, EventArgs e)
        {
            if (_suppressMacroDefaults)
            {
                return;
            }

            Macro macro = SelectedMacro();
            if (macro == null)
            {
                return;
            }

            macro.SmoothMovement = _macroSmoothCheck.Checked;
            _macros.Save();
        }

        private void OnMacroPreserveHoldsChanged(object sender, EventArgs e)
        {
            if (_suppressMacroDefaults)
            {
                return;
            }

            Macro macro = SelectedMacro();
            if (macro == null)
            {
                return;
            }

            macro.PreserveKeyHolds = _macroPreserveHoldsCheck.Checked;
            _macros.Save();
        }

        private void OnPinMacroClicked(object sender, EventArgs e)
        {
            Macro macro = SelectedMacro();
            if (macro == null)
            {
                ShowWarning("Select a macro to pin first.");
                return;
            }

            macro.IsFavorite = !macro.IsFavorite;
            _macros.Save();
            _pinMacroBtn.Text = Utils.Localization.T(macro.IsFavorite ? "★ Unpin" : "★ Pin");
            RefreshMacroList();
            SelectMacro(macro);
        }

        private void OnResetMacroStats(object sender, EventArgs e)
        {
            Macro macro = SelectedMacro();
            if (macro == null)
            {
                ShowWarning("Select a macro first.");
                return;
            }

            if (macro.TimesPlayed == 0 && macro.LastPlayedUtc == null)
            {
                ShowInfo("This macro has no play stats to reset.");
                return;
            }

            macro.TimesPlayed = 0;
            macro.LastPlayedUtc = null;
            _macros.Save();
            RefreshMacroList();
            SelectMacro(macro);
            if (_liveMonitorMacro == macro || !_player.IsPlaying)
            {
                PopulateLiveMonitor(macro);
            }
        }

        /// <summary>
        /// Runs the macro doctor over the selected recording and offers the repairs it
        /// found. Nothing is changed unless the user picks fixes and confirms — the work
        /// happens on a CLONE, which only replaces the stored macro after a successful
        /// save, so a failure part-way can't leave a half-repaired recording behind.
        /// </summary>
        private void OnFixMacroClicked(object sender, EventArgs e)
        {
            Macro macro = SelectedMacro();
            if (macro == null)
            {
                ShowWarning("Select a macro to check first.");
                return;
            }

            try
            {
                // Script files are checked HERE rather than in MacroDoctor: everything
                // that lives there is something it can also repair, and Tempo cannot
                // guess where a moved .py went. This is a report, so it belongs with the
                // report — and "looks healthy" must not be said about a macro whose
                // script vanished.
                var missing = new System.Collections.Generic.List<string>();
                foreach (MacroAction a in macro.Actions)
                {
                    if (a.Type != MacroActionType.Script) { continue; }
                    if (string.IsNullOrWhiteSpace(a.ScriptPath) ||
                        !System.IO.File.Exists(a.ScriptPath))
                    {
                        string p = string.IsNullOrWhiteSpace(a.ScriptPath) ? "(none)" : a.ScriptPath;
                        if (!missing.Contains(p)) { missing.Add(p); }
                    }
                }
                if (missing.Count > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (string p in missing) { sb.Append("   •  ").AppendLine(p); }
                    ShowWarning(Localization.F(
                        "{0} script step(s) in \"{1}\" point at a file that isn't there:\n\n{2}\n"
                        + "Open the macro editor and pick the file again, or delete those steps.",
                        missing.Count, macro.Name, sb.ToString()));
                    return;
                }

                var findings = Engine.MacroDoctor.Diagnose(macro);
                if (findings.Count == 0)
                {
                    ShowInfo(Localization.F("\"{0}\" looks healthy — no stuck keys, no off-screen clicks, no duplicate steps.", macro.Name));
                    return;
                }

                using (var dlg = new MacroFixerForm(_theme, macro, findings))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Chosen.Count == 0)
                    {
                        return;
                    }

                    // Repair a copy first, then commit. If Apply throws, the stored macro
                    // is still the untouched original rather than a half-fixed one.
                    Macro repaired = macro.Clone();
                    int changed = Engine.MacroDoctor.Apply(repaired, dlg.Chosen);
                    macro.Actions = repaired.Actions;
                    _macros.Save();
                    RefreshMacroList();
                    Utils.Logger.Info("[Macro] fixer applied " + dlg.Chosen.Count + " fix(es) to \"" +
                                      macro.Name + "\": " + changed + " step(s) changed.");
                    TempoNotify(4000, "Macro fixed",
                                changed + " step" + (changed == 1 ? "" : "s") + " changed in \"" +
                                macro.Name + "\".", ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                Utils.Logger.Error("Macro fixer failed.", ex);
                ShowWarning(Localization.F("Couldn't check that macro: {0}", ex.Message));
            }
        }

        private void OnMergeMacroClicked(object sender, EventArgs e)
        {
            Macro target = SelectedMacro();
            if (target == null)
            {
                ShowWarning("Select the macro to merge into first.");
                return;
            }

            var others = new System.Collections.Generic.List<Macro>();
            foreach (Macro m in _macros.Macros)
            {
                if (!ReferenceEquals(m, target))
                {
                    others.Add(m);
                }
            }

            if (others.Count == 0)
            {
                ShowInfo("There are no other macros to merge in.");
                return;
            }

            Macro source = ChooseMacro("Merge into '" + target.Name + "'",
                "Append the steps of which macro?", others);
            if (source == null)
            {
                return;
            }

            // Separate the two segments with a short pause so the target's last step and
            // the source's first step don't fire back-to-back — matching the append-recording
            // behaviour.
            if (target.Actions.Count > 0 && source.Actions.Count > 0)
            {
                target.Actions.Add(new MacroAction(MacroActionType.Delay) { DelayMilliseconds = 400 });
            }
            foreach (MacroAction a in source.Actions)
            {
                target.Actions.Add(a.Clone());
            }

            _macros.Save();
            // Rebuild the list so the merged macro's row shows its new step count / duration
            // (the ListBox caches each row's text); RefreshMacroList re-selects the target.
            RefreshMacroList();
            if (_liveMonitorMacro == target || !_player.IsPlaying)
            {
                PopulateLiveMonitor(target);
            }
            UpdateMacroEstTime();
            ShowInfo(Localization.F("Merged {0} step(s) from '{1}' into '{2}'.", source.Actions.Count, source.Name, target.Name));
        }

        /// <summary>Small modal list picker; returns the chosen macro or null.</summary>
        private Macro ChooseMacro(string title, string prompt, System.Collections.Generic.List<Macro> options)
        {
            using (var dlg = new Form())
            using (var list = new ListBox())
            {
                dlg.Text = title;
                dlg.FormBorderStyle = FormBorderStyle.FixedSingle;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.ClientSize = new System.Drawing.Size(360, 320);
                dlg.BackColor = _theme.Background;
                dlg.ForeColor = _theme.Text;
                dlg.Font = UiFactory.BodyFont;

                var label = UiFactory.Label(prompt, 16, 14);
                label.AutoSize = false;
                label.Width = 328;
                label.Height = 20;
                dlg.Controls.Add(label);

                list.Left = 16;
                list.Top = 42;
                list.Width = 328;
                list.Height = 220;
                list.BackColor = _theme.InputBackground;
                list.ForeColor = _theme.Text;
                list.BorderStyle = BorderStyle.FixedSingle;
                foreach (Macro m in options)
                {
                    list.Items.Add(m.Name + "  (" + m.StepCount + " steps)");
                }
                list.SelectedIndex = 0;
                dlg.Controls.Add(list);

                var ok = UiFactory.PrimaryButton("Merge", 168, 274, 84, 32, _theme);
                ok.DialogResult = DialogResult.OK;
                dlg.Controls.Add(ok);

                var cancel = UiFactory.Button("Cancel", 260, 274, 84, 32);
                cancel.DialogResult = DialogResult.Cancel;
                dlg.Controls.Add(cancel);

                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;

                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return null;
                }

                int idx = list.SelectedIndex;
                return idx >= 0 && idx < options.Count ? options[idx] : null;
            }
        }

        private void OnExportAllMacros(object sender, EventArgs e)
        {
            if (_macros.Macros.Count == 0)
            {
                ShowWarning("There are no macros to export.");
                return;
            }

            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = Utils.Localization.T("Export all macros");
                dialog.Filter = Utils.Localization.T("Tempo macros (*.json)|*.json|All files (*.*)|*.*");
                dialog.FileName = "tempo-macros.json";

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (MacroStore.ExportAllToFile(_macros.Macros, dialog.FileName))
                    {
                        ShowInfo(Localization.F("Exported {0} macro(s).", _macros.Macros.Count));
                    }
                    else
                    {
                        ShowWarning("Could not export the macros.");
                    }
                }
            }
        }

        /// <summary>Reorders the macro list by the chosen criterion and persists it.</summary>
        private void OnMacroSortChanged(object sender, EventArgs e)
        {
            if (_macros.Macros.Count < 2)
            {
                return;
            }

            Macro keepSelected = SelectedMacro();

            switch (_macroSortCombo.SelectedIndex)
            {
                case 0: // Name A → Z
                    _macros.SortBy((a, b) =>
                        string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.OrdinalIgnoreCase));
                    break;
                case 1: // Most played
                    _macros.SortBy((a, b) => b.TimesPlayed.CompareTo(a.TimesPlayed));
                    break;
                case 2: // Newest (most recently played first; never-played sink to the bottom)
                    _macros.SortBy((a, b) =>
                    {
                        DateTime ax = a.LastPlayedUtc ?? DateTime.MinValue;
                        DateTime bx = b.LastPlayedUtc ?? DateTime.MinValue;
                        return bx.CompareTo(ax);
                    });
                    break;
                default:
                    return;
            }

            RefreshMacroList();
            if (keepSelected != null)
            {
                SelectMacro(keepSelected);
            }
        }

        private void OnImportAllMacros(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = Utils.Localization.T("Import macros");
                dialog.Filter = Utils.Localization.T("Tempo macros (*.json)|*.json|All files (*.*)|*.*");
                dialog.CheckFileExists = true;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                int count = _macros.ImportAllFromFile(dialog.FileName);
                if (count < 0)
                {
                    ShowWarning("Could not read that file as a macro collection.");
                    return;
                }

                _macros.Save();
                RefreshMacroList();
                ShowInfo(count == 0 ? Localization.T("No macros found in that file.") : Localization.F("Imported {0} macro(s).", count));
            }
        }

        /// <summary>Plays the macro at the given 1-based slot (for quick-play hotkeys).</summary>
        /// <summary>The macro name assigned to a quick-play slot, or "" if unassigned.</summary>
        private string MacroSlotName(int slot)
        {
            if (_settings == null) { return ""; }
            switch (slot)
            {
                case 1: return _settings.MacroSlot1 ?? "";
                case 2: return _settings.MacroSlot2 ?? "";
                case 3: return _settings.MacroSlot3 ?? "";
                default: return "";
            }
        }

        private void SetMacroSlotName(int slot, string name)
        {
            if (_settings == null) { return; }
            name = name ?? "";
            switch (slot)
            {
                case 1: _settings.MacroSlot1 = name; break;
                case 2: _settings.MacroSlot2 = name; break;
                case 3: _settings.MacroSlot3 = name; break;
            }
        }

        /// <summary>
        /// Fires one of the three quick-play hotkeys.
        ///
        /// BY NAME, not by position. This used to run _macros.Macros[slot-1], and that
        /// list is not stable: the Sort dropdown calls MacroStore.SortBy, which sorts
        /// the stored list in place and saves it, and Move up / Move down / Delete
        /// shift it as well. So changing the sort order silently changed which macro a
        /// hotkey played — into whatever game was in the foreground.
        ///
        /// An unassigned slot still falls back to the old positional behaviour, so
        /// anyone who was relying on it before this change keeps what they had.
        /// </summary>
        private void PlayMacroSlot(int slot)
        {
            if (_player.IsPlaying) { return; }

            string wanted = MacroSlotName(slot);
            if (!string.IsNullOrEmpty(wanted))
            {
                var byName = _macros.GetByName(wanted);
                if (byName != null)
                {
                    PlayMacro(byName);
                    return;
                }

                // Assigned but gone — renamed or deleted. Say so rather than silently
                // playing whatever now sits in that position.
                Logger.Warn("[Macros] quick-play slot " + slot + " is assigned to '" + wanted +
                            "', which no longer exists. Nothing played.");
                return;
            }

            int index = slot - 1;
            if (index < 0 || index >= _macros.Macros.Count) { return; }
            PlayMacro(_macros.Macros[index]);
        }

        private void OnDeleteMacroClicked(object sender, EventArgs e)
        {
            Macro macro = SelectedMacro();
            if (macro == null)
            {
                return;
            }

            var confirm = MessageBox.Show(this,
                Localization.F("Delete macro '{0}'? You can restore it from the recycle bin.", macro.Name),
                "Tempo",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm == DialogResult.Yes)
            {
                int steps = macro.Actions != null ? macro.Actions.Count : 0;
                _macros.Remove(macro.Name);
                _macros.Save();
                RefreshMacroList();
                UpdateMacroRecycleButton();
                Logger.Info("[Macros] '" + macro.Name + "' (" + steps +
                            " step(s)) moved to the recycle bin.");
            }
        }

        /// <summary>Keeps the bin button's count and enabled state honest.</summary>
        private void UpdateMacroRecycleButton()
        {
            if (_macroRecycleBtn == null) { return; }
            int binned = _macros?.RecycleBin?.Count ?? 0;
            _macroRecycleBtn.Text = binned > 0
                ? Localization.F("Recycle bin ({0})", binned.ToString())
                : Localization.T("Recycle bin");
            _macroRecycleBtn.Enabled = binned > 0;
        }

        /// <summary>
        /// The window onto deleted macros. Shares its implementation with the profile
        /// bin — see <see cref="RecycleBinForm"/> — so the two cannot drift apart.
        /// </summary>
        private void OnMacroRecycleBinClicked(object sender, EventArgs e)
        {
            if (_macros == null) { return; }

            bool changed = RecycleBinForm.Show(this, _theme,
                Localization.T("Recently deleted macros"),
                Localization.T("Deleting a macro keeps a copy here so it can be brought back. " +
                               "Restoring one whose name is in use again gives it a new name."),
                new[] { Localization.T("Macro"), Localization.T("Steps"), Localization.T("Last played") },
                new[] { 230, 80, 170 },
                Localization.T("The bin is empty."),
                "Permanently delete {0} macro(s)? This cannot be undone.",
                BuildMacroBinRows,
                name =>
                {
                    var restored = _macros.RestoreFromRecycleBin(name);
                    if (restored == null) { return false; }
                    _macros.Save();
                    Logger.Info("[Macros] restored '" + restored.Name + "' from the recycle bin.");
                    return true;
                },
                () =>
                {
                    int n = _macros.RecycleBin.Count;
                    _macros.EmptyRecycleBin();
                    _macros.Save();
                    Logger.Info("[Macros] recycle bin emptied (" + n + " macro(s)).");
                });

            if (changed)
            {
                RefreshMacroList();
            }
            UpdateMacroRecycleButton();
        }

        /// <summary>Rows for the bin window, newest deletion first.</summary>
        private List<RecycleBinForm.Entry> BuildMacroBinRows()
        {
            var rows = new List<RecycleBinForm.Entry>();
            if (_macros?.RecycleBin == null) { return rows; }

            for (int i = _macros.RecycleBin.Count - 1; i >= 0; i--)
            {
                var m = _macros.RecycleBin[i];
                if (m == null) { continue; }
                rows.Add(new RecycleBinForm.Entry
                {
                    Id = m.Name,
                    Cells = new[]
                    {
                        m.Name,
                        (m.Actions != null ? m.Actions.Count : 0).ToString(),
                        m.LastPlayedUtc.HasValue
                            ? m.LastPlayedUtc.Value.ToLocalTime().ToString("g")
                            : Localization.T("never")
                    }
                });
            }
            return rows;
        }

        private void AddExclusions(HotkeyDefinition hk)
        {
            if (hk == null || !hk.IsValid)
            {
                return;
            }

            // Exclude only the hotkey's MAIN key from the recording — never its
            // modifiers. Excluding Ctrl/Shift/Alt would stop the recorder from
            // capturing everyday shortcuts (Ctrl+C, Ctrl+D, Ctrl+V …) — they'd record
            // as just "C"/"D"/"V". The main key alone keeps the stop-hotkey itself out
            // of the macro, which is all the exclusion needs to do.
            if (hk.Key != System.Windows.Forms.Keys.None)
            {
                _recorder.ExcludedVirtualKeys.Add((int)hk.Key);
            }
        }

        private static string MakeSafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "macro";
            }

            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name;
        }
    }
}
