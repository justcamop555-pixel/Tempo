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

        // Steps the recorder has captured that the tab has not shown yet, and whether a UI
        // turn to show them is already queued (see WireMacroEvents).
        private readonly System.Collections.Concurrent.ConcurrentQueue<MacroAction> _recordFeed =
            new System.Collections.Concurrent.ConcurrentQueue<MacroAction>();
        private int _recordFeedPosted;

        // Where playback is — (loop << 32) | step — and whether a UI turn to show it is queued.
        private long _playbackStepLatest = -1;
        private int _playbackLoopLatest;
        private int _playbackStepPosted;

        /// <summary>The "3, 2, 1" in front of a macro playback or recording, while it is up.</summary>
        private CountdownOverlayForm _macroCountdown;

        // Extra macro-tab controls (declared here; this is a partial of MainForm).
        private CheckBox _recordKeysCheck;
        private CheckBox _recordCameraCheck;
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
        private NumericUpDown _macroCountdownNum;
        private NumericUpDown _macroLoopDelayNum;
        private TextBox _macroSearchBox;
        private ComboBox _macroSortCombo;
        private string _macroFilter = string.Empty;
        private ThemedProgressBar _macroProgressBar;
        private Label _macroProgressLabel;

        /// <summary>The last playback stopped the clicker, so the finish line can say so.</summary>
        private bool _clickerYieldedToMacro;

        /// <summary>Names the macros in the library that have Problem-level faults.</summary>
        private Label _macroHealthLabel;

        /// <summary>
        /// Sweeps the WHOLE library with the macro doctor and names anything that is
        /// actually broken — an input pressed and never released, or coordinates outside
        /// the monitors attached right now — plus script steps whose file has gone.
        ///
        /// All three checks already existed; what did not was anything that RAN them.
        /// "Fix…" reports on the one macro you have selected, once you think to press it,
        /// so a recording that cannot work is indistinguishable from one that can. Only
        /// Problem-level findings count here: the doctor's Suggestions (redundant moves,
        /// robotic timing) are improvements, not faults, and a notice that fires on those
        /// would be showing for almost every recording ever made.
        /// </summary>
        private void RefreshMacroHealthNotice()
        {
            if (_macroHealthLabel == null || _macroHealthLabel.IsDisposed) { return; }

            var bad = new List<string>();
            try
            {
                if (_macros != null && _macros.Macros != null)
                {
                    foreach (Macro m in _macros.Macros)
                    {
                        if (m == null || m.Actions == null || m.Actions.Count == 0) { continue; }

                        bool broken = false;
                        foreach (Engine.MacroFinding f in Engine.MacroDoctor.Diagnose(m))
                        {
                            if (f.Level == Engine.MacroFindingLevel.Problem) { broken = true; break; }
                        }

                        // Script steps are checked outside the doctor for the reason given
                        // at OnFixMacroClicked: it only reports what it can also repair,
                        // and it cannot guess where a moved .py went.
                        if (!broken)
                        {
                            foreach (MacroAction a in m.Actions)
                            {
                                if (a == null || a.Type != MacroActionType.Script) { continue; }
                                if (string.IsNullOrWhiteSpace(a.ScriptPath) ||
                                    !System.IO.File.Exists(a.ScriptPath))
                                {
                                    broken = true;
                                    break;
                                }
                            }
                        }

                        if (broken) { bad.Add(m.Name); }
                    }
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("RefreshMacroHealthNotice", ex); }

            if (bad.Count == 0)
            {
                _macroHealthLabel.Visible = false;
                return;
            }

            bad.Sort(StringComparer.CurrentCultureIgnoreCase);
            string names = bad.Count <= 3
                ? string.Join(", ", bad.ToArray())
                : string.Join(", ", bad.GetRange(0, 3).ToArray()) + ", "
                  + Utils.Localization.F("and {0} more", bad.Count - 3);

            _macroHealthLabel.Text = bad.Count == 1
                ? Utils.Localization.F(
                    "⚠ {0} won't play correctly — select it and press “Fix…” to see what's wrong.", names)
                : Utils.Localization.F(
                    "⚠ {0} won't play correctly — select each and press “Fix…” to see what's wrong.", names);
            _macroHealthLabel.Visible = true;
            Utils.Logger.Info("[Macros] health sweep: " + bad.Count + " of " +
                              (_macros?.Macros?.Count ?? 0) + " macro(s) have problems (" + names + ").");
        }

        // Live monitor + append-recording state.
        private CheckBox _appendRecordCheck;
        private CheckBox _cursorTrailCheck;
        private LiveStepListView _liveStepList;
        private Label _liveHeaderLabel;
        private CheckBox _liveEveryStepCheck;
        private Macro _appendTarget;
        private Macro _liveMonitorMacro;
        private bool _liveRecording;

        // The macro being PLAYED. The Live Monitor showed whichever macro was last selected, and the
        // list is not locked during playback: clicking another macro mid-run put ITS steps under the
        // playback highlight, and the "finished" notice then named the macro clicked, not the one run.
        private Macro _playingMacro;

        // ── Live Monitor model ─────────────────────────────────────────────────
        // The list is virtual: a row is made when it is drawn, from these. It used to make a
        // ListViewItem per step, so it stopped at the first 2,000 steps of a macro (and kept the last
        // 1,000 of a recording), and past step 2,000 the playback highlight simply stopped following.
        // Every step is listed now, at any length, and filling it costs no window work.
        //
        // In the compact view (the default; "Show every step" turns it off) a run of mouse travel —
        // the moves and the short waits between them, which is most of any recording — is ONE row
        // ("Path · 48 moves → (1067, 446) · 0.86s"), and so is a run of camera turns; a short wait
        // between two other steps folds into the Time column. A wait of a quarter second or more
        // keeps a row of its own: that is a pause somebody meant.
        private struct LiveRow
        {
            public int First, Last;     // the steps the row covers
            public long AtMs, EndMs;    // when its first step starts, and when its last one is done
            public int Moves;           // the moves (or camera turns) in a travel row
            public long SumX, SumY;     // a camera row's whole turn
        }

        private readonly List<LiveRow> _liveRows = new List<LiveRow>();
        private IList<MacroAction> _liveActions = Array.Empty<MacroAction>();
        private readonly List<MacroAction> _liveTake = new List<MacroAction>();
        private long _liveClockMs;          // the time once every step fed so far is done
        private bool _liveTravelOpen;       // the last row is a travel row still taking moves
        private const int LiveLongWaitMs = 250;

        // Playback progress + per-macro default bookkeeping.
        private int _playbackTotalSteps;
        private int _playbackTotalLoops;
        private int _playbackCurrentLoop;
        /// <summary>The macro being played, for the tray's status line.</summary>
        private string _playbackMacroName;
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
                "an existing recording. In the list: drag a macro to reorder it, Enter plays, " +
                "Ctrl+D duplicates, F2 renames, Delete moves to the recycle bin. Bind \"Record\" " +
                "and \"Play\" on the Keybinds tab to control it hands-free.";
            var help = UiFactory.Label(helpText, 12, 12);
            help.MaximumSize = new Size(720, 0);
            help.AutoSize = true;
            help.ForeColor = _theme.TextMuted;

            // The row is centred on the Sort combo (61-91 once it has its window): the 23 px search box sat on
            // its Top, a step up the row. The list starts 6 px under the search box.
            var listLabel = UiFactory.Label(Utils.Localization.T("Saved macros"), 12, 68, FontStyle.Bold);
            _macroSearchBox = UiFactory.Text(140, 64, 172);
            _macroSearchBox.PlaceholderText = Utils.Localization.T("Search macros\u2026");
            _macroSearchBox.TextChanged += (s, e) => { _macroFilter = _macroSearchBox.Text; RefreshMacroList(); };
            // 296 wide, ending at 308: the three columns (list, Manage, Record/Playback) sat edge to edge with no
            // gap at all; now 6 px between each, and the list still ends at 460.
            _macroListBox = new MacroListBox
            {
                Left = 12,
                Top = 93,
                Width = 296,
                Height = 367,
                Font = UiFactory.BodyFont,
                BorderStyle = BorderStyle.FixedSingle
            };
            _macroListBox.DoubleClick += (s, e) => EditSelectedMacro();
            _macroListBox.SelectedIndexChanged += (s, e) => LoadMacroDefaultsIntoUi();
            _macroListBox.KeyDown += OnMacroListKeyDown;

            // Drag a macro to reorder it, replacing the Move up / Move down buttons — the same change
            // the Accounts list got, for the same reason ("we can add drag account to anywhere").
            // A ListBox has no ItemDrag (that is ListView/TreeView), so the drag starts by hand: remember
            // what was pressed, and begin only once the pointer has moved past the system drag threshold
            // — otherwise an ordinary click to select would start a drag.
            _macroListBox.AllowDrop = true;
            _macroListBox.MouseDown += OnMacroDragMouseDown;
            _macroListBox.MouseMove += OnMacroDragMouseMove;
            _macroListBox.MouseUp += (s, e) => _macroDragCandidate = null;
            _macroListBox.DragEnter += OnMacroDragOver;
            _macroListBox.DragOver += OnMacroDragOver;
            _macroListBox.DragLeave += (s, e) => _macroListBox.DropLineIndex = -1;
            _macroListBox.DragDrop += OnMacroDragDrop;

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
            // Top 92, not 84: the Sort combo above it ends at y=91, so the card's top
            // border ran 7px through the bottom of the combo. Height is unchanged, so the
            // bottom moves to 628 and still clears the Live Monitor card at 634.
            // 460 rather than 536: Move up / Move down are gone (drag the rows instead), so the card is
            // two rows shorter and still ends well clear of the Live Monitor at y=634.
            // 314-438 and from 97 down: 6 px from the list on its left, the Record and Playback cards on its right
            // (it touched both) and the Sort combo above (it was a pixel under it).
            var manageGroup = UiFactory.Group(Utils.Localization.T("Manage"), 314, 97, 124, 460, CardIcon.Gear);
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

            // Move up / Move down are gone: dragging a row does it directly, and two buttons to shuffle
            // one position at a time were the slowest possible way to say "put this one at the top".
            // Everything below them moves up by the 76px the pair occupied.
            _exportMacroBtn = UiFactory.Button(Utils.Localization.T("Export…"), mx, 168, mw, 30);
            _exportMacroBtn.Click += OnExportMacro;

            _importMacroBtn = UiFactory.Button(Utils.Localization.T("Import…"), mx, 202, mw, 30);
            _importMacroBtn.Click += OnImportMacro;

            _exportAllBtn = UiFactory.Button(Utils.Localization.T("Export all…"), mx, 236, mw, 30);
            _exportAllBtn.Click += OnExportAllMacros;

            _importAllBtn = UiFactory.Button(Utils.Localization.T("Import all…"), mx, 270, mw, 30);
            _importAllBtn.Click += OnImportAllMacros;

            _mergeMacroBtn = UiFactory.Button(Utils.Localization.T("Merge…"), mx, 312, mw, 30);
            _mergeMacroBtn.Click += OnMergeMacroClicked;

            // Sits directly above Delete: the repair pass a recording usually wants
            // before it is trusted (stuck keys, off-screen clicks, robotic timing).
            // Delete stays last so the destructive button keeps its own corner.
            _fixMacroBtn = UiFactory.Button(Utils.Localization.T("Fix…"), mx, 346, mw, 30);
            _fixMacroBtn.Click += OnFixMacroClicked;

            _deleteMacroBtn = UiFactory.Button("Delete", mx, 380, mw, 30);
            _deleteMacroBtn.ForeColor = _theme.DangerText;
            _deleteMacroBtn.Click += OnDeleteMacroClicked;

            // Directly under Delete, because that is the button people press by
            // accident and this is the way back from it.
            _macroRecycleBtn = UiFactory.Button(Utils.Localization.T("Recycle bin"), mx, 418, mw, 30);
            _macroRecycleBtn.Click += OnMacroRecycleBinClicked;

            manageGroup.Controls.Add(_macroRecycleBtn);
            manageGroup.Controls.Add(_fixMacroBtn);
            manageGroup.Controls.Add(_editMacroBtn);
            manageGroup.Controls.Add(_renameMacroBtn);
            manageGroup.Controls.Add(_duplicateMacroBtn);
            manageGroup.Controls.Add(_notesMacroBtn);
            manageGroup.Controls.Add(_exportMacroBtn);
            manageGroup.Controls.Add(_importMacroBtn);
            manageGroup.Controls.Add(_exportAllBtn);
            manageGroup.Controls.Add(_importAllBtn);
            manageGroup.Controls.Add(_mergeMacroBtn);
            manageGroup.Controls.Add(_deleteMacroBtn);

            // Sort lives above the list, next to the search box.
            _macroSortCombo = UiFactory.Combo(314, 61, 124, Utils.Localization.T("Sort: A \u2192 Z"), Utils.Localization.T("Sort: Most played"), Utils.Localization.T("Sort: Newest"));
            _macroSortCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            // As wide as its longest choice, still ending at the column's right edge; the search box
            // beside it gives up the difference. "Ordem: mais usadas" needed 114 px of the 100 it had
            // (Spanish and German too), and ended in "\u2026" whenever it was the order chosen.
            if (_macroSortCombo is FlatComboBox sortBox)
            {
                sortBox.Width = sortBox.WidthForChoices96(124);
                sortBox.Left = 438 - sortBox.Width;   // ends where the Manage card does
                if (_macroSearchBox != null)
                {
                    _macroSearchBox.Width = Math.Max(96, sortBox.Left - 8 - _macroSearchBox.Left);
                }
            }
            _macroSortCombo.SelectedIndexChanged += OnMacroSortChanged;

            // Pin / Reset stats sit under the list as a compact action row.
            _pinMacroBtn = UiFactory.Button("\u2605 Pin", 12, 468, 144, 30);
            LayoutFitter.DeclareCaptions(_pinMacroBtn, Utils.Localization.T("\u2605 Pin"), Utils.Localization.T("\u2605 Unpin"));
            _pinMacroBtn.Click += OnPinMacroClicked;

            _resetMacroStatsBtn = UiFactory.Button("Reset stats", 164, 468, 144, 30);
            _resetMacroStatsBtn.Click += OnResetMacroStats;

            _macroSummaryLabel = UiFactory.Caption("", 12, 504);
            _macroSummaryLabel.AutoSize = false;
            _macroSummaryLabel.Width = 296;
            _macroSummaryLabel.Height = 16;
            _macroSummaryLabel.ForeColor = _theme.TextMuted;

            // Library-wide health. "Fix…" has always been able to find a stuck key or a
            // click aimed at a monitor that is gone — but only for the macro you happen to
            // have selected, and only if you think to press it. A macro that cannot work
            // looks exactly like one that can until then. This says so without being asked.
            // Hidden when the library is clean, like every other notice in the app.
            // (12,526)..(308,622): under the summary line, clear of the Manage card at
            // x=314 and the Live Monitor at y=634.
            _macroHealthLabel = new Label
            {
                AutoSize = false,
                Location = new Point(12, 526),
                Size = new Size(296, 96),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = _theme.WarningText,
                Visible = false
            };

            // ── Record group ───────────────────────────────────────────────────
            // 224 tall, not 202: one more row, the camera option under mouse movement. The
            // Playback card and the Live Monitor below move down by the same 22 px.
            var recGroup = UiFactory.Group(Utils.Localization.T("Record"), 444, 80, 268, 224, CardIcon.Target);

            // Every row 4 px lower than it was: at y = 22 the first tick box ran 4 px into the card's
            // title badge (which reaches y = 26), found by the layout check once it measured painted
            // titles. The card had 23 px to spare at the bottom.
            _recordMovesCheck = UiFactory.Check("Capture mouse movement", 16, 26, true);
            _recordMovesCheck.Checked = _settings == null || _settings.RecordMacroMovements;
            _recordMovesCheck.CheckedChanged += OnRecordMovesChanged;
            recGroup.Controls.Add(_recordMovesCheck);

            // Indented under "Capture mouse movement": HOW the movement is recorded, so it
            // only means something while that is ticked.
            _recordCameraCheck = UiFactory.Check("…as camera turns (games)", 36, 48, false);
            _recordCameraCheck.Checked = _settings != null && _settings.RecordMacroCameraMovement;
            _recordCameraCheck.Enabled = _recordMovesCheck.Checked;
            _recordCameraCheck.CheckedChanged += OnRecordCameraChanged;
            recGroup.Controls.Add(_recordCameraCheck);

            _recordKeysCheck = UiFactory.Check("Capture keyboard", 16, 70, true);
            _recordKeysCheck.Checked = _settings == null || _settings.RecordMacroKeyboard;
            _recordKeysCheck.CheckedChanged += OnRecordKeysChanged;
            recGroup.Controls.Add(_recordKeysCheck);

            _appendRecordCheck = UiFactory.Check("Append to selected macro", 16, 92, false);
            recGroup.Controls.Add(_appendRecordCheck);

            // Optional "3, 2, 1" countdown before capture starts, so you can switch to the
            // target window first (its own alt-tab isn't recorded). Symmetric with playback.
            recGroup.Controls.Add(UiFactory.Caption("Countdown (s, 0 = off)", 16, 120));
            _recordCountdownNum = UiFactory.Numeric(178, 116, 60, 0, 10, 0);
            if (_settings != null) _recordCountdownNum.Value = Clamp(_settings.RecordCountdownSeconds, 0, 10);
            _recordCountdownNum.ValueChanged += OnRecordCountdownChanged;
            recGroup.Controls.Add(_recordCountdownNum);

            _recordBtn = UiFactory.Button("● Record", 16, 146, 118, 34);
            _recordBtn.ForeColor = _theme.DangerText;
            _recordBtn.Click += (s, e) => StartRecording();
            recGroup.Controls.Add(_recordBtn);

            _stopRecordBtn = UiFactory.Button("■ Stop", 140, 146, 112, 34);
            _stopRecordBtn.Enabled = false;
            _stopRecordBtn.Click += (s, e) => StopRecording();
            recGroup.Controls.Add(_stopRecordBtn);

            _recordStatusLabel = UiFactory.Label("Not recording.", 16, 190, FontStyle.Italic, 9f);
            _recordStatusLabel.MaximumSize = new Size(236, 0);
            _recordStatusLabel.ForeColor = _theme.TextMuted;
            recGroup.Controls.Add(_recordStatusLabel);

            // ── Playback group ─────────────────────────────────────────────────
            // 362, not 334: a row for holding back Windows' shortcuts while a macro plays or records.
            var playGroup = UiFactory.Group(Utils.Localization.T("Playback"), 444, 314, 268, 362, CardIcon.Play);

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

            // Settings ▸ Behaviour's "Hold back Windows shortcuts…", here where a macro is played and
            // recorded (the same setting: SetGuardWindowsShortcuts).
            _macroGuardCheck = MakeGuardShortcutsCheck(12, 326, "Macros tab");
            playGroup.Controls.Add(_macroGuardCheck);

            _stopPlayBtn = UiFactory.Button("■ Stop", 178, 176, 74, 38);
            _stopPlayBtn.BackColor = _theme.Danger;
            _stopPlayBtn.ForeColor = Theme.ReadableOn(_theme.Danger);
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
            // 656, not 634: the Record card grew a row (the camera option) and pushed the
            // Playback card down by 22 px. 684: the Playback card grew a row of its own (28 px).
            var liveGroup = UiFactory.Group(Utils.Localization.T("Live Monitor"), 12, 684, 700, 240, CardIcon.Wave);

            // Compact rows by default; this shows one row per step instead. Right-aligned, so the
            // header keeps whatever width the switch leaves in the language shown.
            _liveEveryStepCheck = UiFactory.Check("Show every step", 0, 24, _settings != null && _settings.LiveMonitorEveryStep);
            // In the window's layout units (LayoutSize): an auto-sized box measures itself in screen
            // pixels while the window is built, 1.5× too wide on a 150% screen.
            _liveEveryStepCheck.Left = Math.Max(16, 684 - LayoutSize(_liveEveryStepCheck).Width);
            _liveEveryStepCheck.CheckedChanged += OnLiveEveryStepChanged;
            liveGroup.Controls.Add(_liveEveryStepCheck);

            _liveHeaderLabel = UiFactory.Label("Select, record, or play a macro to see steps here.", 16, 26, FontStyle.Italic, 9f);
            _liveHeaderLabel.AutoSize = false;
            _liveHeaderLabel.Width = Math.Max(120, _liveEveryStepCheck.Left - 16 - 12);
            _liveHeaderLabel.Height = 20;
            _liveHeaderLabel.AutoEllipsis = true;
            _liveHeaderLabel.UseMnemonic = false;   // a macro name may hold an "&"
            _liveHeaderLabel.ForeColor = _theme.TextMuted;
            liveGroup.Controls.Add(_liveHeaderLabel);

            _liveStepList = new LiveStepListView
            {
                Left = 16,
                Top = 50,
                Width = 668,
                Height = 186,
                RowSource = MakeLiveItem
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
            page.Controls.Add(_macroHealthLabel);
            page.Controls.Add(recGroup);
            page.Controls.Add(playGroup);
            page.Controls.Add(liveGroup);

            // The cursor trail moved to Settings › Behaviour. It is an app-wide
            // preference that has nothing to do with macros, and it sat on this page only
            // because it was added here; the Macros tab is for recording and playback.

            // If the intro paragraph wraps to more lines than designed (e.g. at a
            // higher display scale), push everything below it down so nothing
            // overlaps. The intro itself stays put.
            ShiftBelowIntro(page, help, 720, firstRowY: 60);

            _tabs.TabPages.Add(page);
        }

        /// <summary>
        /// Measures an intro label's wrapped height and, if it would reach below the
        /// first row of controls, shifts every other control on the page down by the
        /// overflow. Keeps hand-positioned layouts from overlapping at higher DPI.
        /// </summary>
        private void ShiftBelowIntro(Control page, Control intro, int wrapWidth, int firstRowY)
        {
            // In the window's units (MeasureLayout): this runs while the window is built, in 100%
            // numbers, and a height in screen pixels shifted the page 1.5× too far on a 150% screen.
            // The words it SHOWS: it measured the English it was built from, and the German intro on
            // Multi-Point, a line longer, ran into the Order row on a 150% screen.
            int introBottom = intro.Top + MeasureLayout(intro.Text, intro.Font, wrapWidth, TextFormatFlags.Default).Height;
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

        /// <summary>
        /// Queues <paramref name="action"/> for the UI thread's next turn — even when called ON
        /// the UI thread, which is the point: UiInvoke runs inline there. False when the window
        /// cannot take it (no handle yet, or closing).
        /// </summary>
        private bool PostToUi(Action action)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(action);
                    return true;
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            return false;
        }

        private void WireMacroEvents()
        {
            // This runs INSIDE the recorder's low-level hooks, and every mouse and key event on
            // the PC waits for them to return. The hooks live on this UI thread, so UiInvoke ran
            // the whole update right there: a Live Monitor row added, scrolled to and relabelled
            // for every step, i.e. pointer lag across the desktop while recording, spent against
            // the deadline Windows removes slow hooks at. The hook now only queues the step, and
            // the tab catches up on its own turn, in one batch.
            _recorder.ActionRecorded += (s, action) =>
            {
                _recordFeed.Enqueue(action);
                if (System.Threading.Interlocked.Exchange(ref _recordFeedPosted, 1) == 0 &&
                    !PostToUi(DrainRecordFeed))
                {
                    System.Threading.Volatile.Write(ref _recordFeedPosted, 0);
                }
            };

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
                _liveHeaderLabel.ForeColor = _theme.AccentText;
                Macro played = _playingMacro ?? _liveMonitorMacro;
                string playName = played != null
                    ? played.Name : Utils.Localization.T("macro");
                string playMeta = played != null
                    ? Utils.Localization.F("  \u2014  {0} steps, \u2248{1}",
                        played.StepCount,
                        FormatLiveTime(played.EstimatedDurationMs))
                    : "";
                _liveHeaderLabel.Text = Utils.Localization.F("\u25B6 Playing {0}", playName) + playMeta;
            });

            // Both raised on the PLAYBACK thread: once per loop, and once per STEP. One UI
            // update used to be posted per step, and a recorded path runs hundreds of steps a
            // second (more at 2× or 4×), each relabelling, repainting the progress bar and
            // moving the Live Monitor's highlight — queued faster than the UI could do it, on
            // the thread that also answers the hotkeys, while the macro was driving a game. Now
            // the newest position is kept and ONE update is queued at a time; it shows wherever
            // playback has got to by the time it runs.
            _player.LoopChanged += (s, loop) => System.Threading.Volatile.Write(ref _playbackLoopLatest, loop);

            _player.StepExecuted += (s, index) =>
            {
                // Loop and step stored as one value, so the tab never pairs a step with the
                // loop before or after it.
                long packed = ((long)System.Threading.Volatile.Read(ref _playbackLoopLatest) << 32) | (uint)index;
                System.Threading.Interlocked.Exchange(ref _playbackStepLatest, packed);
                if (System.Threading.Interlocked.Exchange(ref _playbackStepPosted, 1) == 0 &&
                    !PostToUi(ShowPlaybackStep))
                {
                    System.Threading.Volatile.Write(ref _playbackStepPosted, 0);
                }
            };

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
                // Answer "why did my clicker stop?" where the person is already looking.
                // Stopping it is the right call (see StopClickerBecause) but doing it in
                // silence would just move the mystery.
                _macroProgressLabel.Text = _clickerYieldedToMacro
                    ? Utils.Localization.T("Ready. The auto-clicker was stopped for this macro.")
                    : Utils.Localization.T("Ready.");
                _clickerYieldedToMacro = false;
                Macro played = _playingMacro;
                _playingMacro = null;
                ClearLiveHighlight();
                HideMacroIndicator();
                _liveHeaderLabel.ForeColor = _theme.TextMuted;
                // Back to the monitor's resting rule, "the selected macro" \u2014 with the run counted in
                // its header. It used to say "Finished \u2014 select a macro to view its steps" over the
                // steps of the macro it had just played.
                PopulateLiveMonitor(SelectedMacro() ?? played);
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
                        string nm = played != null && !string.IsNullOrEmpty(played.Name)
                            ? played.Name : Utils.Localization.T("macro");
                        // Translated: it was English in every language.
                        TempoNotify(2000, "Tempo",
                            _playbackTotalLoops == 1
                                ? Utils.Localization.F("Macro '{0}' finished (1 loop).", nm)
                                : Utils.Localization.F("Macro '{0}' finished ({1} loops).", nm, _playbackTotalLoops),
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
                // A macro started by a hotkey from inside a game ended with Tempo popping up over the game and taking
                // its focus — after every run of a key the user presses again and again. It stays down then; a
                // macro started from Tempo's own window brings it back as before.
                RestoreAfterMacro(wasAutoMin, restore: !_playbackFromOtherApp);
            });
        }

        /// <summary>
        /// Shows where playback has got to: progress bar, loop/step line, the on-screen
        /// indicator and the Live Monitor's highlight. Queued at most once at a time by the
        /// StepExecuted handler, and reads the NEWEST position when it runs.
        /// </summary>
        private void ShowPlaybackStep()
        {
            // Cleared first, so a step executed while this runs queues the next update.
            System.Threading.Volatile.Write(ref _playbackStepPosted, 0);
            long packed = System.Threading.Interlocked.Read(ref _playbackStepLatest);
            if (packed < 0)
            {
                return;
            }
            int index = (int)(packed & 0xFFFFFFFFL);
            _playbackCurrentLoop = (int)(packed >> 32);

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
        }

        /// <summary>
        /// Shows the steps the recorder has captured since the last turn, as one batch.
        /// Queued at most once at a time by the ActionRecorded handler.
        /// </summary>
        private void DrainRecordFeed()
        {
            // Cleared first, so a step captured while this runs queues the next batch.
            System.Threading.Volatile.Write(ref _recordFeedPosted, 0);

            // A take that has already stopped: StopRecording has had the last word on these
            // labels ("Saved 'X' (N steps)") and refilled the monitor from the saved macro, so
            // anything still queued is only dropped.
            bool recording = _recorder != null && _recorder.IsRecording;
            bool live = recording && _liveRecording && _liveStepList != null;
            int count = 0;
            int rowsBefore = _liveRows.Count;
            while (_recordFeed.TryDequeue(out MacroAction action))
            {
                count++;
                if (live)
                {
                    _liveTake.Add(action);
                    FeedLive(_liveTake.Count - 1);
                }
            }

            if (!recording || count == 0)
            {
                return;
            }

            _recordedStepCount += count;
            _recordStatusLabel.Text = Utils.Localization.F(
                "Recording… {0} steps captured.", _recordedStepCount);
            _recordIndicator?.SetStepCount(_recordedStepCount);

            if (live)
            {
                // Told once for the whole batch, and scrolled to the newest row once.
                ShowLiveRows(rowsBefore, follow: true);
                UpdateLiveRecordingHeader();
            }
        }

        // ── Live Monitor ───────────────────────────────────────────────────────

        /// <summary>
        /// Shows a macro's steps (at rest, on selection, on play); null shows nothing. Every step,
        /// however many: the rows are made as they are drawn (see the Live Monitor model).
        /// </summary>
        private void PopulateLiveMonitor(Macro macro)
        {
            _liveMonitorMacro = macro;
            if (!_liveRecording) { _liveTake.Clear(); }    // a finished take's steps, let go
            RebuildLiveRows(macro?.Actions);
            // After the rows exist, so the column fit accounts for the scrollbar.
            _liveStepList.RefreshLayout();
            _liveHeaderLabel.Text = macro != null
                ? LiveMonitorHeader(macro)
                : Utils.Localization.T("Select, record, or play a macro to see steps here.");
        }

        /// <summary>
        /// The Live Monitor's line about a macro at rest. One builder: the header after playback
        /// used to drop the notes, and the notes went in whole, line breaks and all, into a label
        /// one line high.
        /// </summary>
        private static string LiveMonitorHeader(Macro m)
        {
            string notes = (m.Notes ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (notes.Length > 80) { notes = notes.Substring(0, 79).TrimEnd() + "…"; }
            return Utils.Localization.F("{0}  —  {1:N0} steps, ≈{2}",
                       m.Name, m.StepCount, FormatLiveTime(m.EstimatedDurationMs)) +
                   (m.TimesPlayed > 0 ? Utils.Localization.F("  •  played {0}×", m.TimesPlayed) : "") +
                   LastPlayedText(m) +
                   (notes.Length > 0 ? "   “" + notes + "”" : "");
        }

        /// <summary>Rebuilds the rows for <paramref name="actions"/> (a macro's, or the take so far).</summary>
        private void RebuildLiveRows(IList<MacroAction> actions)
        {
            _liveActions = actions ?? (IList<MacroAction>)Array.Empty<MacroAction>();
            _liveRows.Clear();
            _liveClockMs = 0;
            _liveTravelOpen = false;
            for (int i = 0; i < _liveActions.Count; i++)
            {
                FeedLive(i);
            }
            try
            {
                _liveStepList.HighlightIndex = -1;
                _liveStepList.SelectedIndices.Clear();
            }
            catch { }
            ShowLiveRows(0, follow: false);
        }

        /// <summary>
        /// Tells the list how many rows there are. The row that was last before (<paramref name="rowsBefore"/>
        /// − 1) is redrawn too: a travel row keeps taking moves, so it may say something new.
        /// </summary>
        private void ShowLiveRows(int rowsBefore, bool follow)
        {
            try
            {
                int n = _liveRows.Count;
                if (_liveStepList.VirtualListSize != n) { _liveStepList.VirtualListSize = n; }
                if (rowsBefore > 0 && rowsBefore <= n) { _liveStepList.RedrawItems(rowsBefore - 1, rowsBefore - 1, true); }
                if (follow && n > 0) { _liveStepList.EnsureVisible(n - 1); }
                _liveStepList.Invalidate();
            }
            catch { }
        }

        /// <summary>
        /// Adds step <paramref name="i"/> of <see cref="_liveActions"/> to the rows: a row of its own,
        /// a move (or camera turn) or short wait joining the travel row before it, or — in the compact
        /// view — a short wait between other steps, folded into the Time column.
        /// </summary>
        private void FeedLive(int i)
        {
            MacroAction a = _liveActions[i];
            long at = _liveClockMs;
            if (a.Type == MacroActionType.Delay) { _liveClockMs += Math.Max(0, a.DelayMilliseconds); }

            if (_settings != null && _settings.LiveMonitorEveryStep)
            {
                _liveRows.Add(new LiveRow { First = i, Last = i, AtMs = at, EndMs = _liveClockMs });
                return;
            }

            bool travel = a.Type == MacroActionType.MouseMove || a.Type == MacroActionType.MouseMoveRelative;
            bool shortWait = a.Type == MacroActionType.Delay && a.DelayMilliseconds < LiveLongWaitMs;
            if (_liveTravelOpen && _liveRows.Count > 0)
            {
                LiveRow last = _liveRows[_liveRows.Count - 1];
                MacroActionType kind = _liveActions[last.First].Type;
                if (shortWait || a.Type == kind)
                {
                    last.Last = i;
                    last.EndMs = _liveClockMs;
                    if (a.Type == kind)
                    {
                        last.Moves++;
                        if (kind == MacroActionType.MouseMoveRelative) { last.SumX += a.X; last.SumY += a.Y; }
                    }
                    _liveRows[_liveRows.Count - 1] = last;
                    return;
                }
            }
            _liveTravelOpen = false;
            if (shortWait) { return; }

            var row = new LiveRow { First = i, Last = i, AtMs = at, EndMs = _liveClockMs };
            if (travel)
            {
                row.Moves = 1;
                if (a.Type == MacroActionType.MouseMoveRelative) { row.SumX = a.X; row.SumY = a.Y; }
                _liveTravelOpen = true;
            }
            _liveRows.Add(row);
        }

        /// <summary>The row holding step <paramref name="step"/>, or -1. Rows are in step order.</summary>
        private int LiveRowOf(int step)
        {
            int lo = 0, hi = _liveRows.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (_liveRows[mid].First <= step) { found = mid; lo = mid + 1; }
                else { hi = mid - 1; }
            }
            return found;
        }

        /// <summary>Makes row <paramref name="index"/> for the virtual list.</summary>
        private ListViewItem MakeLiveItem(int index)
        {
            if (index < 0 || index >= _liveRows.Count) { return null; }
            LiveRow r = _liveRows[index];
            if (r.First < 0 || r.First >= _liveActions.Count) { return null; }
            MacroAction first = _liveActions[r.First];
            bool grouped = r.Moves >= 2;
            string action, detail;
            if (grouped && first.Type == MacroActionType.MouseMoveRelative)
            {
                action = Models.MacroAction.FriendlyType(MacroActionType.MouseMoveRelative);
                detail = Utils.Localization.F("{0} turns · {1} · {2}", r.Moves,
                    MacroAction.RelativeText((int)Math.Max(int.MinValue, Math.Min(int.MaxValue, r.SumX)),
                                             (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, r.SumY))),
                    FormatLiveTime(r.EndMs - r.AtMs));
            }
            else if (grouped)
            {
                MacroAction end = first;
                for (int k = Math.Min(r.Last, _liveActions.Count - 1); k >= r.First; k--)
                {
                    if (_liveActions[k].Type == MacroActionType.MouseMove) { end = _liveActions[k]; break; }
                }
                action = Utils.Localization.T("Path");
                detail = Utils.Localization.F("{0} moves → ({1}, {2}) · {3}", r.Moves, end.X, end.Y,
                    FormatLiveTime(r.EndMs - r.AtMs));
            }
            else
            {
                action = Models.MacroAction.FriendlyType(first.Type);
                detail = LiveDetail(first);
            }
            var item = new ListViewItem(new[] { (r.First + 1).ToString(), FormatLiveTime(r.AtMs), action, detail })
            {
                Tag = first.Type      // exact colour, no name matching needed
            };
            if (r.Last > r.First)
            {
                item.ToolTipText = Utils.Localization.F("Steps {0}–{1}", r.First + 1, r.Last + 1);
            }
            return item;
        }

        /// <summary>"Show every step" switched: the same steps, regrouped, with the playhead kept.</summary>
        private void OnLiveEveryStepChanged(object sender, EventArgs e)
        {
            if (_settings == null || _liveEveryStepCheck == null) { return; }
            if (_settings.LiveMonitorEveryStep == _liveEveryStepCheck.Checked) { return; }
            _settings.LiveMonitorEveryStep = _liveEveryStepCheck.Checked;
            try { SettingsManager.Save(_settings); } catch { }

            int hl = _liveStepList.HighlightIndex;
            int step = hl >= 0 && hl < _liveRows.Count ? _liveRows[hl].First : -1;
            RebuildLiveRows(_liveActions);
            _liveStepList.RefreshLayout();
            if (step >= 0)
            {
                int row = LiveRowOf(step);
                if (row >= 0) { _liveStepList.MoveHighlightTo(row); }
            }
            else if (_liveRecording && _liveRows.Count > 0)
            {
                try { _liveStepList.EnsureVisible(_liveRows.Count - 1); } catch { }
            }
        }

        private static string LastPlayedText(Macro m)
        {
            if (m == null || m.LastPlayedUtc == null)
            {
                return "";
            }
            return "  •  " + Utils.Localization.F("last {0}",
                m.LastPlayedUtc.Value.ToLocalTime().ToString("d MMM HH:mm", Utils.Localization.DateCulture));
        }

        /// <summary>The header while recording: every step captured is listed, with no cap.</summary>
        private void UpdateLiveRecordingHeader()
        {
            _liveHeaderLabel.Text = Utils.Localization.F("● Recording…  {0} steps captured", _liveTake.Count);
        }

        /// <summary>
        /// Highlights the row holding the step being played (in the compact view, the travel row a
        /// move belongs to; a short wait, the row before it) and keeps it in view.
        /// </summary>
        private void HighlightLiveStep(int index)
        {
            int row = LiveRowOf(index);
            if (row < 0 || row >= _liveStepList.VirtualListSize || row == _liveStepList.HighlightIndex)
            {
                return;
            }

            // The list owner-draws itself from this one index, so moving the playhead
            // is a single assignment. The old code stamped BackColor/ForeColor onto the
            // row and reset the previous one to the LIST's colours — which would now
            // erase that row's own action colour and its stripe.
            try { _liveStepList.SelectedIndices.Clear(); } catch { }
            _liveStepList.MoveHighlightTo(row);
        }

        private void ClearLiveHighlight()
        {
            _liveStepList.HighlightIndex = -1;
            try { _liveStepList.SelectedIndices.Clear(); } catch { }
            if (_liveMonitorMacro != null)
            {
                _liveHeaderLabel.Text = LiveMonitorHeader(_liveMonitorMacro);
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
                case MacroActionType.MouseMoveRelative: return MacroAction.RelativeText(a.X, a.Y);
                // ▲ up, ▼ down, in the wheel's own units (120 a notch). It read "delta 120", in
                // English in every language, with no sign of the direction.
                case MacroActionType.Wheel: return (a.WheelDelta >= 0 ? "▲ " : "▼ ") + Math.Abs(a.WheelDelta);
                case MacroActionType.KeyDown:
                case MacroActionType.KeyUp: return MacroAction.KeyName(a.VirtualKey);
                // As the step editor shows it. A script step fell through to the position below and
                // read "(0, 0)".
                case MacroActionType.Script: return a.ScriptFileName() + "  ·  " + a.ScriptTimeoutMs + " ms";
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

            // Show the selected macro's steps in the live monitor — unless it is busy: with a
            // recording's feed, or with the macro being played (the list is not locked while one
            // plays, and its steps under the playback highlight were another macro's).
            // PlaybackFinished shows the selection once the run ends.
            if (!_liveRecording && !(_player != null && _player.IsPlaying))
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
            ScheduleMacroDefaultsSave();
            UpdateMacroEstTime();
        }

        // Holding a spinner's arrow changes it many times a second, and every change rewrote
        // macros.json — all the macros and the recycle bin, on the UI thread (7–16 ms each here,
        // far more for a library of long recordings), so the arrow stuttered. The macro takes the
        // new value at once (Play, the estimate and a switch to another macro all see it); only
        // the file waits until the spinner has settled. Any other macro save, exit (CleanUp's
        // "save macros") and Windows ending the session (FlushStateForShutdown) write it sooner.
        private System.Windows.Forms.Timer _macroDefaultsSaveTimer;

        private void ScheduleMacroDefaultsSave()
        {
            if (_macroDefaultsSaveTimer == null)
            {
                _macroDefaultsSaveTimer = new System.Windows.Forms.Timer { Interval = 700 };
                _macroDefaultsSaveTimer.Tick += (s, e) =>
                {
                    _macroDefaultsSaveTimer.Stop();
                    _macros.Save();
                };
            }
            _macroDefaultsSaveTimer.Stop();
            _macroDefaultsSaveTimer.Start();
        }

        /// <summary>Writes a spinner change that is still waiting for its save, now.</summary>
        private void FlushPendingMacroDefaultsSave()
        {
            if (_macroDefaultsSaveTimer != null && _macroDefaultsSaveTimer.Enabled)
            {
                _macroDefaultsSaveTimer.Stop();
                _macros.Save();
            }
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

            // Every path that changes the library ends up here, so this is the one place
            // the health sweep has to be wired into.
            RefreshMacroHealthNotice();
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
            if (_recorder.IsRecording || _macroCountdown != null)
            {
                return;
            }

            // Not while a macro plays: the Record button is disabled then, but the Record hotkey came
            // straight here and recorded over the playback — whose own input never reaches a take, so
            // the recording held your input and the playback's results, and the Live Monitor showed
            // both at once.
            if (_player != null && _player.IsBusy)
            {
                Logger.Info("[Macro] recording refused: a macro is playing.");
                TempoNotify(3500, "Tempo", Localization.T("A macro is playing. Stop it first, then record."),
                    ToolTipIcon.Info, always: true);
                return;
            }

            // Optional "3, 2, 1" countdown so you can switch to the target window before
            // capture begins (the overlay's own focus change isn't recorded — capture only
            // starts once it closes). Cancelling (Esc) aborts before any state changes.
            int recCountdown = _settings != null ? _settings.RecordCountdownSeconds : 0;
            if (recCountdown > 0 && !RunMacroCountdown(recCountdown))
            {
                return;
            }

            // CONFLICT with camera-relative movement, and a silent one. While it is armed
            // it swallows the physical W/A/S/D so it can inject its own re-mixed keys — so
            // the recorder, sitting on a different hook further down the chain, never sees
            // them. A recording made in that state is missing every movement key and gives
            // no hint why; you find out when you play it back and the character stands
            // still. Recording is the explicit action here, so movement yields to it.
            // After the countdown, so cancelling with Esc changes nothing.
            DisarmMovementBecause("recording a macro needs the real W/A/S/D");

            // And the clicker, which is a quieter version of the same trap. The recorder
            // drops injected input, so Tempo's own clicks never appear IN the take — but
            // they still reach the app you are recording, so the recording captures what
            // they did without the clicks that did it, and plays back into a window that
            // has moved on. See StopClickerBecause.
            _clickerYieldedToMacro =
                StopClickerBecause("recording a macro should capture only your own input");

            _recorder.RecordMovements = _recordMovesCheck.Checked;
            _recorder.RecordCamera = _recordCameraCheck != null && _recordCameraCheck.Checked;
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
            PrepareRecordingExclusions();

            _recordedStepCount = 0;
            // Anything a previous take left queued for the tab is not this take's.
            while (_recordFeed.TryDequeue(out _)) { }

            // Remember whether to append to the currently selected macro.
            _appendTarget = _appendRecordCheck.Checked ? SelectedMacro() : null;

            // Reset the live monitor for a fresh recording feed.
            _liveRecording = true;
            _liveTake.Clear();
            RebuildLiveRows(_liveTake);
            _liveHeaderLabel.Text = Utils.Localization.T("● Recording…");

            string name = "Macro " + DateTime.Now.ToString("HH-mm-ss");
            if (!_recorder.Start(name))
            {
                _liveRecording = false;
                PopulateLiveMonitor(SelectedMacro());   // not left saying "● Recording…"
                ShowWarning("Could not start recording. The input hook failed to install.");
                return;
            }

            _recordBtn.Enabled = false;
            RefreshBusyLock();
            _stopRecordBtn.Enabled = true;
            // Set here, not where the clicker is actually stopped: this line runs a moment
            // later and would overwrite anything said earlier.
            _recordStatusLabel.Text = _clickerYieldedToMacro
                ? Utils.Localization.T("Recording… (the auto-clicker was stopped so the take is only your input). "
                    + "Bind/press the Record or Emergency-stop hotkey to finish.")
                : Utils.Localization.T("Recording… bind/press the Record or Emergency-stop hotkey to finish.");
            _clickerYieldedToMacro = false;
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
                _autoMinimizedForRecording = MinimizeForMacro();
            }
        }

        /// <summary>
        /// Shows the "3, 2, 1" in front of a macro playback or recording and returns true when
        /// it ran to the end. Tracked while it is up, so a macro hotkey pressed meanwhile
        /// cancels it (see DispatchAction) — the overlay says "Esc or your stop key to cancel",
        /// and only the clicker's count-in used to listen for the key.
        /// </summary>
        private bool RunMacroCountdown(int seconds)
        {
            using (var overlay = new CountdownOverlayForm(_theme, seconds))
            {
                _macroCountdown = overlay;
                try
                {
                    // No owner: Play can be triggered by hotkey while the main window is
                    // hidden in the tray, and a hidden owner breaks modal display.
                    return overlay.ShowDialog() == DialogResult.OK;
                }
                finally
                {
                    _macroCountdown = null;
                }
            }
        }

        /// <summary>
        /// A macro hotkey pressed while a macro countdown is on screen cancels that countdown
        /// and does nothing else. The stop keys did nothing at all before; Play or Record
        /// pressed again stacked a second countdown inside the first, and both then ran —
        /// a macro that played twice, or a recording restarted under itself.
        /// </summary>
        private bool CancelMacroCountdownFor(HotkeyAction action)
        {
            CountdownOverlayForm countdown = _macroCountdown;
            if (countdown == null || countdown.IsDisposed)
            {
                return false;
            }

            switch (action)
            {
                case HotkeyAction.EmergencyStop:
                case HotkeyAction.StopMacro:
                case HotkeyAction.PlayMacro:
                case HotkeyAction.PlayMacro1:
                case HotkeyAction.PlayMacro2:
                case HotkeyAction.PlayMacro3:
                case HotkeyAction.ToggleRecordMacro:
                    Utils.Logger.Info("[Macro] countdown cancelled by the " + action + " hotkey.");
                    countdown.CancelCountdown();
                    return true;
                default:
                    return false;
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
                // Where it was dragged, the overlays' opacity, and movable if moving is on.
                DropOverlayPreview(OverlayBadge.Recording);
                WireBadge(_recordIndicator);
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
                // Being placed: the take ended, but there is still a badge to put somewhere.
                if (_overlayMoveMode) { ShowOverlayPreview(OverlayBadge.Recording); }
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
            // Brought forward: a finished recording is what the user wants to look at next.
            RestoreAfterMacro(wasAutoMin, restore: true);

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
                // The take's rows and "● Recording… N steps captured" stayed up, though the take
                // (all of it trimmed away, or nothing caught) was never saved.
                PopulateLiveMonitor(SelectedMacro());
                _emptyTakeToExplain = true;
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

            // Said out loud, once the window is back: an empty take used to end in a small grey "Nothing was
            // recorded." under the buttons — easy to miss after the window had been minimised — and a short
            // test then simply looked like Tempo not saving it (the user's report, 2026-10-18).
            if (_emptyTakeToExplain)
            {
                _emptyTakeToExplain = false;
                ShowInfo(ExplainEmptyTake(_recorder.LastTake));
            }
        }

        private bool _emptyTakeToExplain;

        /// <summary>Why a take came out with nothing to save, in the user's terms, and what to do instead.</summary>
        private string ExplainEmptyTake(AutoClicker.Engine.MacroRecorder.TakeReport take)
        {
            take = take ?? new AutoClicker.Engine.MacroRecorder.TakeReport();
            string text;
            if (take.OwnWindowInput > 0)
            {
                text = Utils.Localization.F(
                    "Nothing was saved: the {0} click(s) and key press(es) in this take went to Tempo's own window. "
                    + "A macro leaves those out, because played back they would land on whatever is there by then.\n\n"
                    + "Press Record, then switch to the app you want to automate and do the steps there.",
                    take.OwnWindowInput);
                bool canStopByHotkey =
                    (_settings?.HotkeyFor(HotkeyAction.ToggleRecordMacro)?.IsValid ?? false) ||
                    (_settings?.HotkeyFor(HotkeyAction.EmergencyStop)?.IsValid ?? false);
                if (_settings != null && (!_settings.MinimizeWhileRecording || !canStopByHotkey))
                {
                    text += "\n\n" + Utils.Localization.T(
                        "Tip: give Record a hotkey and turn on \"Minimise window during macro record & playback\" in "
                        + "Settings — Tempo then steps out of the way the moment recording starts.");
                }
            }
            else if (take.HotkeyPresses > 0 && take.MovementSteps == 0)
            {
                text = Utils.Localization.T(
                    "Nothing was saved: the only keys pressed were Tempo's own hotkeys, and those are never part of a macro.");
            }
            else if (take.MovementSteps > 0)
            {
                text = Utils.Localization.F(
                    "Nothing was saved: the take was only the pointer moving to and from Tempo's buttons ({0}). "
                    + "Record at least one click, key press or scroll — or a second or more of movement.",
                    IntegrityDetailsForm.Elapsed(take.MovementMs));
            }
            else
            {
                text = Utils.Localization.T(
                    "Nothing was saved: no click, key press or mouse movement reached Tempo while it was recording.");
                if (_recordMovesCheck != null && _recordKeysCheck != null &&
                    (!_recordMovesCheck.Checked || !_recordKeysCheck.Checked))
                {
                    text += "\n\n" + Utils.Localization.T(
                        "\"Capture mouse movement\" or \"Capture keyboard\" is off on the Macros tab — turn on what you want recorded.");
                }
            }
            Utils.Logger.Info("[Macro] the take had nothing to save; told the user why.");
            return text;
        }

        // ── Playback ───────────────────────────────────────────────────────────

        // True only while a playback that WE auto-minimised the window for is in flight,
        // so PlaybackFinished restores the window exactly when it should — and never
        // steals focus from a window the user minimised themselves.
        private bool _autoMinimizedForPlayback;
        private bool _autoMinimizedForRecording;
        private bool _autoMinimizedAccountsWindow;
        private bool _playbackFromOtherApp;

        /// <summary>
        /// Gets Tempo out of the way of a macro being recorded or played ("Minimise window during macro record &amp;
        /// playback"): the main window, and the Accounts window when it is open on its own — it stayed up, over the
        /// game, through every recording and playback (pinned on top, in front of it), and a recording took any
        /// click made on it. Only windows on screen are touched, and only those are put back afterwards: the flag
        /// used to be set even when Tempo was already minimised or in the tray, and the end of the macro then
        /// brought up a window the user had put away themselves. Returns whether the MAIN window was minimised.
        /// </summary>
        private bool MinimizeForMacro()
        {
            bool main = false;
            try
            {
                if (Visible && WindowState != FormWindowState.Minimized)
                {
                    WindowState = FormWindowState.Minimized;
                    main = true;
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("MinimizeForMacro", ex); }
            try
            {
                if (AccountsAreDetached && _accountsWindow.Visible && _accountsWindow.WindowState != FormWindowState.Minimized)
                {
                    _accountsWindow.WindowState = FormWindowState.Minimized;
                    _autoMinimizedAccountsWindow = true;
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("MinimizeForMacro/accounts", ex); }
            return main;
        }

        /// <summary>
        /// Puts back what <see cref="MinimizeForMacro"/> took down — when <paramref name="restore"/>, the main window
        /// in front; otherwise both stay minimised.
        /// </summary>
        private void RestoreAfterMacro(bool mainWasMinimizedByUs, bool restore)
        {
            bool accounts = _autoMinimizedAccountsWindow;
            _autoMinimizedAccountsWindow = false;
            if (!restore) { return; }
            try
            {
                if (accounts && AccountsAreDetached && _accountsWindow.WindowState == FormWindowState.Minimized)
                {
                    _accountsWindow.WindowState = FormWindowState.Normal;
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("RestoreAfterMacro/accounts", ex); }
            if (mainWasMinimizedByUs && WindowState == FormWindowState.Minimized)
            {
                try { WindowState = FormWindowState.Normal; Activate(); } catch { }
            }
        }

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
            else if (!_player.IsBusy && _macroCountdown == null)
            {
                // The key did nothing, with no sign why: nothing is selected on the Macros tab (none
                // recorded yet, or the list was emptied). Said in a card like the other refusals —
                // a box would pop up over the game.
                TempoNotify(3500, "Tempo", Localization.T("Select a macro to play first."),
                    ToolTipIcon.Info, always: true);
            }
        }

        private void PlayMacro(Macro macro, int? loopsOverride = null)
        {
            // IsBusy, not IsPlaying: a stopped run whose last step has not returned yet still
            // owns the player (see MacroPlayer.IsBusy). And not while a macro countdown is up.
            if (macro == null || _player.IsBusy || _macroCountdown != null)
            {
                return;
            }

            // Not while recording. The tab's Play buttons are disabled then (RefreshBusyLock), but the
            // Play hotkeys came straight here: the macro ran into the app being recorded, and the take
            // got its consequences without its input (the recorder drops injected input) — the same
            // half-recording the clicker is stopped to prevent.
            if (_recorder != null && _recorder.IsRecording)
            {
                Logger.Info("[Macro] play '" + macro.Name + "' refused: a recording is in progress.");
                TempoNotify(3500, "Tempo", Localization.T("A macro is being recorded. Stop the recording first, then play."),
                    ToolTipIcon.Info, always: true);
                return;
            }

            if (macro.StepCount == 0)
            {
                // Pressed from a game (a Play or quick-play hotkey), a modal message box popped up
                // over it and took its focus. Said in a card there; the box stays for the tab's own
                // Play button.
                IntPtr fg = AutoClicker.Native.NativeMethods.GetForegroundWindow();
                if (fg != IntPtr.Zero && !IsOwnWindow(fg))
                {
                    TempoNotify(3500, "Tempo", Localization.T("This macro has no steps yet — record or edit it first."),
                        ToolTipIcon.Info, always: true);
                }
                else
                {
                    ShowInfo("This macro has no steps yet — record or edit it first.");
                }
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

            // The PLAYED macro's own settings. The boxes on the tab show the SELECTED macro's,
            // and a quick-play hotkey plays a macro that need not be selected — so slot 2 ran
            // with the loop count, speed and countdown of whatever happened to be highlighted:
            // an infinite loop borrowed from the selection kept a one-shot macro going until
            // someone stopped it. For the selected macro the boxes still win, exactly as before
            // (they are what the user is looking at, and are saved onto it as they change).
            bool shownOnTab = ReferenceEquals(macro, SelectedMacro());
            int countdown = shownOnTab
                ? (int)_macroCountdownNum.Value
                : (int)Clamp(macro.PreplayCountdownSeconds, (int)_macroCountdownNum.Minimum, (int)_macroCountdownNum.Maximum);
            if (countdown > 0 && !RunMacroCountdown(countdown))
            {
                _macroProgressLabel.Text = Utils.Localization.T("Playback cancelled.");
                return;
            }

            // The countdown pumps messages: anything could have started meanwhile.
            if (_player.IsBusy)
            {
                return;
            }

            int loops = loopsOverride ?? (shownOnTab
                ? (int)_macroLoopNum.Value
                : (int)Clamp(macro.DefaultLoops, (int)_macroLoopNum.Minimum, (int)_macroLoopNum.Maximum));
            double speed = (shownOnTab
                ? (double)_macroSpeedNum.Value
                : (double)Clamp(macro.DefaultSpeed, (int)_macroSpeedNum.Minimum, (int)_macroSpeedNum.Maximum)) / 10.0;
            // The player is handed the macro's own loop delay (below), so the estimate uses
            // the same number rather than the box's.
            int loopDelayMs = Math.Max(0, macro.LoopDelayMs);

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
                _playbackTotalEstimateMs = oneLoopMs * loops + (double)loopDelayMs * (loops - 1);
            }
            else
            {
                _playbackTotalEstimateMs = 0; // infinite loop — no estimate
            }

            // Show the macro in the live monitor so playback can highlight steps — and remember
            // which macro is running, whatever is selected meanwhile.
            _playingMacro = macro;
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

            // And the auto-clicker, for the same reason: it clicks wherever the pointer
            // is, and moving the pointer is what a macro does. Measured: a 50 ms clicker
            // put 26 clicks into a window the macro merely passed over. See
            // StopClickerBecause.
            _clickerYieldedToMacro = StopClickerBecause("a macro is driving the pointer");

            _statusState.Text = Utils.Localization.T("Playing macro");
            ShowMacroIndicator(macro.Name);

            // Get out of the way during playback (same option as recording), but only
            // if a stop hotkey is bound so the user can always finish while minimised.
            bool canStopByHotkey =
                (_settings?.HotkeyFor(HotkeyAction.StopMacro)?.IsValid ?? false) ||
                (_settings?.HotkeyFor(HotkeyAction.EmergencyStop)?.IsValid ?? false);
            if (_settings != null && _settings.MinimizeWhileRecording && canStopByHotkey)
            {
                _autoMinimizedForPlayback = MinimizeForMacro();
            }
            // Started by a hotkey from inside another app — a game, usually: that is where the user is, and where
            // they stay when it ends.
            _playbackFromOtherApp = haveExternalTarget;

            // Re-aim keyboard/mouse focus at the game (captured above) so the very first
            // keystrokes land there and not in Tempo/desktop after the countdown + minimise.
            // Without this, movement keys are dropped in-game even though the same macro
            // works when replayed into a normal window. Only done when an external window
            // was foreground at trigger time (i.e. a hotkey fired from inside the game).
            if (haveExternalTarget)
            {
                RefocusPlayTarget(playTarget);
            }

            _playbackMacroName = macro.Name;
            // Before the worker exists, so its first step can't be overwritten by this reset.
            System.Threading.Interlocked.Exchange(ref _playbackStepLatest, -1);
            System.Threading.Volatile.Write(ref _playbackLoopLatest, 0);
            _player.Play(macro, loops, speed, loopDelayMs);
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

            string newName = TextPromptForm.Show(this, _theme, Localization.T("Rename Macro"),
                Localization.T("New macro name:"), macro.Name)?.Trim();

            if (string.IsNullOrWhiteSpace(newName) || newName == macro.Name)
            {
                return;
            }

            string oldName = macro.Name;
            if (!_macros.Rename(macro.Name, newName))
            {
                ShowWarning("A macro with that name already exists.");
                return;
            }

            // The quick-play slots hold a macro by NAME (so re-sorting cannot change what a key plays — see
            // PlayMacroSlot). A rename left them pointing at the old name: "Play macro 1" then did nothing,
            // and the Keybinds tab showed the macro you had just renamed as "(missing)".
            bool slotsFollowed = false;
            for (int slot = 1; slot <= 3; slot++)
            {
                if (string.Equals(MacroSlotName(slot), oldName, StringComparison.OrdinalIgnoreCase))
                {
                    SetMacroSlotName(slot, newName);
                    slotsFollowed = true;
                }
            }
            if (slotsFollowed)
            {
                try { SettingsManager.Save(_settings); } catch (Exception ex) { Logger.Swallow("OnRenameMacro.slots", ex); }
                Logger.Info("[Macros] quick-play slot(s) followed the rename '" + oldName + "' → '" + newName + "'.");
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

        // ── Drag to reorder ────────────────────────────────────────────────────
        //
        // The list is NOT a straight window onto the stored order: favourites are drawn first, and the
        // search box filters rows out. So a drop's row number is meaningless to the store — what it
        // means is "put this macro next to THAT one", and the neighbour's own position is looked up in
        // the store to turn it into an index. Reordering while a search is active is refused outright:
        // the rows on screen are a subset in a different order, so there is no honest answer to where
        // a drop belongs among the macros the user cannot see.

        private Macro _macroDragCandidate;   // pressed, but not yet dragged far enough to count
        private Point _macroDragStart;

        private void OnMacroDragMouseDown(object sender, MouseEventArgs e)
        {
            _macroDragCandidate = null;
            if (_macroListBox == null || e.Button != MouseButtons.Left) { return; }

            int i = _macroListBox.IndexFromPoint(e.Location);
            if (i < 0 || i >= _macroListBox.Items.Count) { return; }
            // Taken from the row under the pointer, not from SelectedMacro(): at mouse-down the
            // selection may still be the previous row.
            _macroDragCandidate = _macroListBox.Items[i] as Macro;
            _macroDragStart = e.Location;
        }

        private void OnMacroDragMouseMove(object sender, MouseEventArgs e)
        {
            if (_macroDragCandidate == null || (e.Button & MouseButtons.Left) == 0) { return; }

            Size threshold = SystemInformation.DragSize;
            if (Math.Abs(e.X - _macroDragStart.X) < threshold.Width
                && Math.Abs(e.Y - _macroDragStart.Y) < threshold.Height)
            {
                return;   // still a click, not a drag
            }

            Macro dragged = _macroDragCandidate;
            _macroDragCandidate = null;

            if (!string.IsNullOrWhiteSpace(_macroFilter))
            {
                // Say why rather than letting the drag silently do nothing: while a search is active the
                // rows are a subset in a different order, so there is no honest place to drop into.
                if (_recordStatusLabel != null)
                {
                    _recordStatusLabel.Text = Utils.Localization.T("Clear the search box to reorder macros by dragging.");
                }
                return;
            }
            if (_macros.Macros.Count < 2) { return; }

            _macroListBox.DoDragDrop(dragged, DragDropEffects.Move);
        }

        private void OnMacroDragOver(object sender, DragEventArgs e)
        {
            if (_macroListBox == null || e.Data == null || !e.Data.GetDataPresent(typeof(Macro)))
            {
                e.Effect = DragDropEffects.None;   // something from outside Tempo — not ours to take
                return;
            }
            e.Effect = DragDropEffects.Move;
            Point local = _macroListBox.PointToClient(new Point(e.X, e.Y));
            _macroListBox.DropLineIndex = _macroListBox.DropIndexAt(local);
        }

        private void OnMacroDragDrop(object sender, DragEventArgs e)
        {
            if (_macroListBox == null) { return; }
            int dropAt = _macroListBox.DropLineIndex;
            _macroListBox.DropLineIndex = -1;

            var dragged = e.Data.GetData(typeof(Macro)) as Macro;
            if (dragged == null || dropAt < 0) { return; }

            int target = StoreIndexForDrop(dropAt, dragged);
            if (target < 0 || !_macros.MoveTo(dragged.Name, target)) { return; }

            _macros.Save();
            RefreshMacroList();
            SelectMacro(dragged);
            Utils.Logger.Info("[Macro] reordered the library by drag.");
        }

        /// <summary>
        /// Turns a drop position in the LIST into an insertion index in the STORE, by asking where the
        /// row it was dropped against actually lives. Returns -1 when it cannot be resolved.
        /// </summary>
        private int StoreIndexForDrop(int dropAt, Macro dragged)
        {
            // Dropped at the very end of the list → the end of the store.
            if (dropAt >= _macroListBox.Items.Count) { return _macros.Macros.Count; }

            var before = _macroListBox.Items[dropAt] as Macro;
            if (before == null) { return -1; }
            if (ReferenceEquals(before, dragged)) { return -1; }   // dropped on itself

            int index = _macros.IndexOf(before.Name);
            return index < 0 ? -1 : index;
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
                    // Either kind of file: a backup made with Export all… brings all its macros.
                    List<Macro> imported = _macros.ImportManyFromFile(dialog.FileName);
                    if (imported == null)
                    {
                        ShowWarning("Could not import that file. It may not be a valid macro.");
                        return;
                    }
                    FinishMacroImport(imported);
                }
            }
        }

        /// <summary>
        /// The end of both imports: asks about the macros that run scripts, then saves, shows
        /// them and says what arrived.
        /// </summary>
        private void FinishMacroImport(List<Macro> imported)
        {
            if (imported.Count == 0)
            {
                ShowInfo(Localization.T("No macros found in that file."));
                return;
            }

            int leftOut = ConfirmImportedScripts(imported);
            if (imported.Count == 0)
            {
                return;     // all turned down: nothing arrived, and the question already said why
            }

            _macros.Save();
            RefreshMacroList();
            SelectMacro(imported[0]);
            if (leftOut > 0)
            {
                ShowInfo(Localization.F("Imported {0} macro(s). Left out {1} that run a Python script.", imported.Count, leftOut));
            }
            else if (imported.Count == 1)
            {
                ShowInfo(Localization.F("Imported '{0}' ({1} steps).", imported[0].Name, imported[0].StepCount));
            }
            else
            {
                ShowInfo(Localization.F("Imported {0} macro(s).", imported.Count));
            }
        }

        /// <summary>
        /// Asks before keeping imported macros that run Python, listing what they would run.
        /// The ones turned down are taken back out — from the library and from
        /// <paramref name="imported"/> — and how many that was is returned.
        ///
        /// A macro is a shareable JSON file, and a Script step is a path to a program. So
        /// "import this macro someone sent me" would otherwise mean "run whatever .py that
        /// file points at, on your machine, the moment you press Play" — with no sign
        /// beforehand that the macro does anything but click. Recording, editing and
        /// exporting your OWN macro is untouched; this is only the moment a macro arrives
        /// from somewhere else. Import all… skipped the question entirely, so a shared
        /// collection carried its scripts in unseen; both imports ask now.
        /// </summary>
        private int ConfirmImportedScripts(List<Macro> imported)
        {
            try
            {
                var risky = new List<Macro>();
                var pathsOf = new Dictionary<Macro, List<string>>();
                foreach (Macro m in imported)
                {
                    var paths = new List<string>();
                    foreach (MacroAction a in m.Actions)
                    {
                        if (a.Type == MacroActionType.Script && !string.IsNullOrWhiteSpace(a.ScriptPath)
                            && !paths.Contains(a.ScriptPath))
                        {
                            paths.Add(a.ScriptPath);
                        }
                    }
                    if (paths.Count > 0) { risky.Add(m); pathsOf[m] = paths; }
                }
                if (risky.Count == 0) { return 0; }

                // Bounded, so a big collection can't grow the question past the screen: "(+N)" says
                // how many more there are without a sentence to translate.
                const int MaxMacrosListed = 8, MaxPathsListed = 4;
                var list = new System.Text.StringBuilder();
                string question;
                if (risky.Count == 1)
                {
                    List<string> paths = pathsOf[risky[0]];
                    for (int i = 0; i < paths.Count && i < MaxPathsListed * 2; i++) { list.Append("   •  ").AppendLine(paths[i]); }
                    if (paths.Count > MaxPathsListed * 2) { list.Append("   …  (+").Append(paths.Count - MaxPathsListed * 2).AppendLine(")"); }
                    question = Localization.F("'{0}' contains {1} step(s) that run a Python script:\n\n{2}\n"
                        + "Scripts run as you, with your files and your network. Only keep this "
                        + "macro if you trust where it came from and you have read the script.\n\n"
                        + "Import it anyway?", risky[0].Name, paths.Count, list.ToString());
                }
                else
                {
                    for (int i = 0; i < risky.Count && i < MaxMacrosListed; i++)
                    {
                        List<string> paths = pathsOf[risky[i]];
                        list.Append("   •  ").AppendLine(risky[i].Name);
                        for (int j = 0; j < paths.Count && j < MaxPathsListed; j++) { list.Append("         ").AppendLine(paths[j]); }
                        if (paths.Count > MaxPathsListed) { list.Append("         …  (+").Append(paths.Count - MaxPathsListed).AppendLine(")"); }
                    }
                    if (risky.Count > MaxMacrosListed) { list.Append("   …  (+").Append(risky.Count - MaxMacrosListed).AppendLine(")"); }
                    question = Localization.F("{0} of these macros contain steps that run a Python script:\n\n{1}\n"
                        + "Scripts run as you, with your files and your network. Only keep these "
                        + "macros if you trust where they came from and you have read the scripts.\n\n"
                        + "Import them anyway? No leaves them out.", risky.Count, list.ToString());
                }

                Logger.Warn("[Python] " + risky.Count + " imported macro(s) carry script steps; asking the user.");

                DialogResult answer = MessageBox.Show(this, question,
                    "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (answer == DialogResult.Yes) { return 0; }

                // Declined: take them back out WITHOUT the recycle bin, so "No" leaves nothing behind.
                foreach (Macro m in risky)
                {
                    try { _macros.Discard(m); } catch (Exception ex) { Logger.Swallow("ConfirmImportedScripts.Discard", ex); }
                    imported.Remove(m);
                }
                Logger.Info("[Python] import declined; " + risky.Count + " macro(s) discarded.");
                return risky.Count;
            }
            catch (Exception ex)
            {
                Logger.Swallow("ConfirmImportedScripts", ex);
                return 0;    // never block an ordinary import because this check broke
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
                Localization.T("Macro notes"),
                Localization.F("Description / notes for '{0}':", macro.Name),
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
            // "…as camera turns" says how movement is recorded, so it waits on this one.
            if (_recordCameraCheck != null)
            {
                _recordCameraCheck.Enabled = _recordMovesCheck.Checked;
            }
        }

        private void OnRecordCameraChanged(object sender, EventArgs e)
        {
            if (_settings == null)
            {
                return;
            }
            _settings.RecordMacroCameraMovement = _recordCameraCheck.Checked;
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
                    // F() strings: the text was built by concatenation, so it could never match a
                    // translation and stayed English (TempoNotify translates whole strings only).
                    TempoNotify(4000, Localization.T("Macro fixed"),
                                changed == 1
                                    ? Localization.F("1 step changed in \"{0}\".", macro.Name)
                                    : Localization.F("{0} steps changed in \"{1}\".", changed, macro.Name),
                                ToolTipIcon.Info);
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

            Macro source = ChooseMacro(Localization.F("Merge into '{0}'", target.Name),
                Localization.T("Append the steps of which macro?"), others);
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
                    list.Items.Add(m.Name + "  (" + Localization.F("{0} steps", m.StepCount) + ")");
                }
                list.SelectedIndex = 0;
                // A double-click on a macro chooses it, as in any picker.
                list.MouseDoubleClick += (s, e) =>
                {
                    if (list.IndexFromPoint(e.Location) >= 0) { dlg.DialogResult = DialogResult.OK; }
                };
                dlg.Controls.Add(list);

                // Sized to their words ("Zusammenführen" was cut off at 84 px), right-aligned.
                var ok = UiFactory.PrimaryButton("Merge", 168, 274, 84, 32, _theme);
                ok.DialogResult = DialogResult.OK;
                var cancel = UiFactory.Button("Cancel", 260, 274, 84, 32);
                cancel.DialogResult = DialogResult.Cancel;
                UiFactory.FitToText96(ok, 84);
                UiFactory.FitToText96(cancel, 84);
                cancel.Left = 344 - cancel.Width;
                ok.Left = cancel.Left - 8 - ok.Width;
                dlg.Controls.Add(ok);
                dlg.Controls.Add(cancel);

                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;

                // Scaled like every other window: on a 125/150% screen it kept its 100% boxes around
                // the bigger text (the list and the buttons cut off). Its title bar was the plain
                // light Windows one; only the chrome is themed (as SaveMacroForm does) — the full
                // ThemeManager.Apply repaints every button in Surface2, and Merge lost its accent.
                ThemeManager.ApplyWindowChrome(dlg, _theme);
                UiFactory.ScaleToScreen(dlg);

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

                // Either kind of file (one exported macro works too), and the same question about
                // scripts as a single import — Import all… used to keep them without asking.
                List<Macro> imported = _macros.ImportManyFromFile(dialog.FileName);
                if (imported == null)
                {
                    ShowWarning("Could not read that file as a macro collection.");
                    return;
                }
                FinishMacroImport(imported);
            }
        }

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
            if (_player.IsBusy) { return; }

            string wanted = MacroSlotName(slot);
            if (!string.IsNullOrEmpty(wanted))
            {
                var byName = _macros.GetByName(wanted);
                if (byName != null)
                {
                    PlayMacro(byName);
                    return;
                }

                // Assigned but gone — deleted (a rename now carries the slot along). Say so rather
                // than silently playing whatever now sits in that position — and say it where the
                // person pressing the key is looking: a log line meant the key just did nothing.
                Logger.Warn("[Macros] quick-play slot " + slot + " is assigned to '" + wanted +
                            "', which no longer exists. Nothing played.");
                TempoNotify(5000, Localization.T("Macro not found"),
                    Localization.F("“{0}” (Play macro #{1}) is no longer in your macros, so nothing played. "
                        + "Pick its macro again on the Keybinds tab, or restore it from the recycle bin.", wanted, slot),
                    ToolTipIcon.Warning, always: true);
                return;
            }

            // The Nth macro as the Macros tab LISTS it — favourites first. This took the Nth of the stored
            // order, so with a macro pinned "the 1st macro in the list" was not the one at the top of it.
            Macro positional = MacroAtListPosition(slot - 1);
            if (positional == null)
            {
                // No macro picked for the slot (unassigned, slot N plays the Nth macro in the list)
                // and fewer macros than its number: the key did nothing at all.
                TempoNotify(3500, "Tempo",
                    Localization.F("Nothing is set for Play macro #{0}. Pick a macro for it on the Keybinds tab.", slot),
                    ToolTipIcon.Info, always: true);
                return;
            }
            PlayMacro(positional);
        }

        /// <summary>
        /// The macro at <paramref name="index"/> in the Macros tab's list as RefreshMacroList draws it —
        /// favourites first, then the stored order — ignoring the search box, which only hides rows for
        /// a while. Null past the end.
        /// </summary>
        private Macro MacroAtListPosition(int index)
        {
            if (_macros == null || index < 0) { return null; }
            int at = 0;
            foreach (bool favourites in new[] { true, false })
            {
                foreach (Macro m in _macros.Macros)
                {
                    if (m == null || m.IsFavorite != favourites) { continue; }
                    if (at++ == index) { return m; }
                }
            }
            return null;
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
                            ? m.LastPlayedUtc.Value.ToLocalTime().ToString("g", Localization.DateCulture)
                            : Localization.T("never")
                    }
                });
            }
            return rows;
        }

        /// <summary>
        /// What a take leaves out: the main key of every hotkey that controls macros (see
        /// AddExclusions), every key press that runs any other Tempo hotkey (MacroRecorder.FiresHotkey),
        /// and every MOUSE-button hotkey, whatever it does. The hotkey hook
        /// swallows those presses, so the app being recorded never saw them — and replayed, a
        /// side button bound to, say, Start / Stop would be Back or Forward in whatever window
        /// is under the pointer.
        /// </summary>
        private void PrepareRecordingExclusions()
        {
            _recorder.ExcludedVirtualKeys.Clear();
            _recorder.ExcludedMouseHotkeys.Clear();
            // And any key press that runs one of Tempo's other hotkeys, asked at the moment of the
            // press — the routes and the Shift/Ctrl versions are the manager's to know.
            _recorder.FiresHotkey = vk => _hotkeys != null && _hotkeys.FiresHotkeyNow(vk);
            _recorder.LeaveOutWindowsShortcuts = false;
            if (_settings == null)
            {
                return;
            }
            _recorder.ExtraShiftCtrl = _settings.HotkeysIgnoreExtraShiftCtrl;
            // With the guard: the shortcuts it holds back while recording never happened, so they are no
            // part of the take either (MacroRecorder.LeaveOutWindowsShortcuts). Without it they work, and
            // are recorded like any other keys.
            _recorder.LeaveOutWindowsShortcuts = _settings.GuardWindowsShortcuts;

            foreach (HotkeyAction a in new[]
            {
                HotkeyAction.ToggleRecordMacro, HotkeyAction.EmergencyStop, HotkeyAction.StopMacro,
                HotkeyAction.PlayMacro, HotkeyAction.PlayMacro1, HotkeyAction.PlayMacro2, HotkeyAction.PlayMacro3
            })
            {
                AddExclusions(_settings.HotkeyFor(a));
            }

            foreach (HotkeyAction a in Enum.GetValues(typeof(HotkeyAction)))
            {
                HotkeyDefinition hk = _settings.HotkeyFor(a);
                if (hk != null && hk.IsMouse)
                {
                    AddExclusions(hk);
                }
            }
        }

        private void AddExclusions(HotkeyDefinition hk)
        {
            if (hk == null || !hk.IsValid)
            {
                return;
            }

            // A mouse-button hotkey: the recorder leaves out a press of that button made with
            // exactly its modifiers held — the press the hotkey hook swallows. Bare Left/Right
            // are never swallowed (the hotkey manager won't eat ordinary clicking), so those
            // reach the app and stay in the take. Key is ignored for a mouse hotkey.
            if (hk.IsMouse)
            {
                bool bareLeftRight =
                    (hk.MouseButton == HotkeyMouseButton.Left || hk.MouseButton == HotkeyMouseButton.Right) &&
                    !hk.Control && !hk.Alt && !hk.Shift && !hk.Win;
                if (!bareLeftRight)
                {
                    _recorder.ExcludedMouseHotkeys.Add(hk.Clone());
                }
                return;
            }

            // Exclude only the hotkey's MAIN key from the recording — never its
            // modifiers. Excluding Ctrl/Shift/Alt would stop the recorder from
            // capturing everyday shortcuts (Ctrl+C, Ctrl+D, Ctrl+V …) — they'd record
            // as just "C"/"D"/"V". The main key alone keeps the stop-hotkey itself out
            // of the macro; the recorder drops the modifiers' presses left at the very
            // end of the take (see MacroRecorder.DropStopChord).
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
