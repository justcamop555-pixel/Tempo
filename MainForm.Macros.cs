using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Persistence;

namespace AutoClicker.UI
{
    public partial class MainForm
    {
        private int _recordedStepCount;

        // Extra macro-tab controls (declared here; this is a partial of MainForm).
        private CheckBox _recordKeysCheck;
        private Button _editMacroBtn;
        private Button _renameMacroBtn;
        private Button _duplicateMacroBtn;
        private Button _exportMacroBtn;
        private Button _importMacroBtn;
        private Button _notesMacroBtn;
        private Button _exportAllBtn;
        private Button _importAllBtn;
        private Button _mergeMacroBtn;
        private Button _pinMacroBtn;
        private Button _resetMacroStatsBtn;
        private Label _macroSummaryLabel;
        private CheckBox _macroSmoothCheck;
        private Button _macroMoveUpBtn;
        private Button _macroMoveDownBtn;
        private NumericUpDown _macroCountdownNum;
        private NumericUpDown _macroLoopDelayNum;
        private TextBox _macroSearchBox;
        private ComboBox _macroSortCombo;
        private string _macroFilter = string.Empty;
        private ProgressBar _macroProgressBar;
        private Label _macroProgressLabel;

        // Live monitor + append-recording state.
        private CheckBox _appendRecordCheck;
        private CheckBox _cursorTrailCheck;
        private ListView _liveStepList;
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

            string helpText =
                "Record mouse and keyboard input, edit the steps, then play it back. " +
                "The Live Monitor below fills in real time as you record and highlights " +
                "each step during playback. Tick \"Append to selected macro\" to add onto " +
                "an existing recording. In the list: Enter plays, Ctrl+D duplicates, " +
                "F2 renames, Delete removes. Bind \"Record\" and \"Play\" on the Keybinds tab " +
                "to control it hands-free.";
            var help = UiFactory.Label(helpText, 12, 12);
            help.MaximumSize = new Size(720, 0);
            help.AutoSize = true;
            help.ForeColor = _theme.TextMuted;

            var listLabel = UiFactory.Label("Saved macros", 12, 64, FontStyle.Bold);
            _macroSearchBox = UiFactory.Text(140, 61, 172);
            _macroSearchBox.TextChanged += (s, e) => { _macroFilter = _macroSearchBox.Text; RefreshMacroList(); };
            _macroListBox = new ListBox
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
            macroMenu.Items.Add("Play", null, OnPlayMacroClicked);
            macroMenu.Items.Add("Play once", null, OnPlayMacroOnceClicked);
            macroMenu.Items.Add("Edit…", null, (s, e) => EditSelectedMacro());
            macroMenu.Items.Add("Rename…", null, OnRenameMacro);
            macroMenu.Items.Add("Duplicate", null, OnDuplicateMacro);
            macroMenu.Items.Add("Export…", null, OnExportMacro);
            macroMenu.Items.Add(new ToolStripSeparator());
            macroMenu.Items.Add("Pin / Unpin", null, OnPinMacroClicked);
            macroMenu.Items.Add("Delete", null, OnDeleteMacroClicked);
            _macroListBox.ContextMenuStrip = macroMenu;
            _macroListBox.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    int i = _macroListBox.IndexFromPoint(e.Location);
                    if (i >= 0) _macroListBox.SelectedIndex = i;
                }
            };

            // ── Management button column ───────────────────────────────────────
            int mx = 320;
            int mw = 114;

            _macroSortCombo = UiFactory.Combo(mx, 61, mw, "Sort: A → Z", "Sort: Most played", "Sort: Newest");
            _macroSortCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _macroSortCombo.SelectedIndexChanged += OnMacroSortChanged;
            _editMacroBtn = UiFactory.Button("Edit…", mx, 86, mw, 30);
            _editMacroBtn.Click += (s, e) => EditSelectedMacro();

            _renameMacroBtn = UiFactory.Button("Rename…", mx, 122, mw, 30);
            _renameMacroBtn.Click += OnRenameMacro;

            _duplicateMacroBtn = UiFactory.Button("Duplicate", mx, 158, mw, 30);
            _duplicateMacroBtn.Click += OnDuplicateMacro;

            _macroMoveUpBtn = UiFactory.Button("Move Up", mx, 200, mw, 30);
            _macroMoveUpBtn.Click += (s, e) => MoveMacro(-1);

            _macroMoveDownBtn = UiFactory.Button("Move Down", mx, 236, mw, 30);
            _macroMoveDownBtn.Click += (s, e) => MoveMacro(1);

            _exportMacroBtn = UiFactory.Button("Export…", mx, 278, mw, 30);
            _exportMacroBtn.Click += OnExportMacro;

            _importMacroBtn = UiFactory.Button("Import…", mx, 314, mw, 30);
            _importMacroBtn.Click += OnImportMacro;

            _deleteMacroBtn = UiFactory.Button("Delete", mx, 356, mw, 30);
            _deleteMacroBtn.Click += OnDeleteMacroClicked;

            _notesMacroBtn = UiFactory.Button("Notes…", mx, 398, mw, 30);
            _notesMacroBtn.Click += OnEditMacroNotes;

            _exportAllBtn = UiFactory.Button("Export all…", mx, 434, mw, 30);
            _exportAllBtn.Click += OnExportAllMacros;

            _importAllBtn = UiFactory.Button("Import all…", mx, 470, mw, 30);
            _importAllBtn.Click += OnImportAllMacros;

            _mergeMacroBtn = UiFactory.Button("Merge…", mx, 506, mw, 30);
            _mergeMacroBtn.Click += OnMergeMacroClicked;

            _pinMacroBtn = UiFactory.Button("★ Pin", 12, 468, 146, 30);
            _pinMacroBtn.Click += OnPinMacroClicked;

            _resetMacroStatsBtn = UiFactory.Button("Reset stats", 166, 468, 146, 30);
            _resetMacroStatsBtn.Click += OnResetMacroStats;

            _macroSummaryLabel = UiFactory.Caption("", 12, 504);
            _macroSummaryLabel.AutoSize = false;
            _macroSummaryLabel.Width = 300;
            _macroSummaryLabel.Height = 16;
            _macroSummaryLabel.ForeColor = _theme.TextMuted;

            // ── Record group ───────────────────────────────────────────────────
            var recGroup = UiFactory.Group("Record", 444, 80, 268, 202);

            _recordMovesCheck = UiFactory.Check("Capture mouse movement", 16, 24, true);
            recGroup.Controls.Add(_recordMovesCheck);

            _recordKeysCheck = UiFactory.Check("Capture keyboard", 16, 48, true);
            recGroup.Controls.Add(_recordKeysCheck);

            _appendRecordCheck = UiFactory.Check("Append to selected macro", 16, 72, false);
            recGroup.Controls.Add(_appendRecordCheck);

            _recordBtn = UiFactory.Button("● Record", 16, 104, 118, 34);
            _recordBtn.ForeColor = _theme.Danger;
            _recordBtn.Click += (s, e) => StartRecording();
            recGroup.Controls.Add(_recordBtn);

            _stopRecordBtn = UiFactory.Button("■ Stop", 140, 104, 112, 34);
            _stopRecordBtn.Enabled = false;
            _stopRecordBtn.Click += (s, e) => StopRecording();
            recGroup.Controls.Add(_stopRecordBtn);

            _recordStatusLabel = UiFactory.Label("Not recording.", 16, 150, FontStyle.Italic, 9f);
            _recordStatusLabel.MaximumSize = new Size(236, 0);
            _recordStatusLabel.ForeColor = _theme.TextMuted;
            recGroup.Controls.Add(_recordStatusLabel);

            // ── Playback group ─────────────────────────────────────────────────
            var playGroup = UiFactory.Group("Playback", 444, 292, 268, 270);

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

            _playMacroBtn = UiFactory.PrimaryButton("▶ Play", 16, 140, 80, 38, _theme);
            _playMacroBtn.Click += OnPlayMacroClicked;
            playGroup.Controls.Add(_playMacroBtn);

            _playOnceBtn = UiFactory.Button("Once", 102, 140, 70, 38);
            _playOnceBtn.Click += OnPlayMacroOnceClicked;
            playGroup.Controls.Add(_playOnceBtn);

            _macroSmoothCheck = UiFactory.Check("Smooth mouse movement", 12, 238);
            _macroSmoothCheck.CheckedChanged += OnMacroSmoothChanged;
            playGroup.Controls.Add(_macroSmoothCheck);

            _stopPlayBtn = UiFactory.Button("■ Stop", 178, 140, 74, 38);
            _stopPlayBtn.BackColor = _theme.Danger;
            _stopPlayBtn.ForeColor = Color.White;
            _stopPlayBtn.FlatAppearance.BorderSize = 0;
            _stopPlayBtn.Enabled = false;
            _stopPlayBtn.Click += (s, e) => _player.Stop();
            playGroup.Controls.Add(_stopPlayBtn);

            _macroProgressBar = new ProgressBar
            {
                Left = 16,
                Top = 188,
                Width = 236,
                Height = 14,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            playGroup.Controls.Add(_macroProgressBar);

            _macroProgressLabel = UiFactory.Label("Ready.", 16, 206, FontStyle.Italic, 8.5f);
            _macroProgressLabel.MaximumSize = new Size(236, 0);
            _macroProgressLabel.ForeColor = _theme.TextMuted;
            playGroup.Controls.Add(_macroProgressLabel);

            // ── Live Monitor ───────────────────────────────────────────────────
            // A real-time view of macro steps: it fills as you record, shows the
            // selected macro at rest, and highlights the current step during
            // playback (auto-scrolling to follow along).
            var liveGroup = UiFactory.Group("Live Monitor", 12, 576, 700, 240);

            _liveHeaderLabel = UiFactory.Label("Select, record, or play a macro to see steps here.", 16, 26, FontStyle.Italic, 9f);
            _liveHeaderLabel.AutoSize = false;
            _liveHeaderLabel.Width = 668;
            _liveHeaderLabel.Height = 20;
            _liveHeaderLabel.ForeColor = _theme.TextMuted;
            liveGroup.Controls.Add(_liveHeaderLabel);

            _liveStepList = new ListView
            {
                Left = 16,
                Top = 50,
                Width = 668,
                Height = 186,
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                GridLines = false,
                MultiSelect = false,
                HideSelection = false
            };
            _liveStepList.Columns.Add("#", 50);
            _liveStepList.Columns.Add("Time", 90);
            _liveStepList.Columns.Add("Action", 160);
            _liveStepList.Columns.Add("Detail", 350);
            liveGroup.Controls.Add(_liveStepList);

            page.Controls.Add(help);
            page.Controls.Add(listLabel);
            page.Controls.Add(_macroSearchBox);
            page.Controls.Add(_macroSortCombo);
            page.Controls.Add(_macroListBox);
            page.Controls.Add(_editMacroBtn);
            page.Controls.Add(_renameMacroBtn);
            page.Controls.Add(_duplicateMacroBtn);
            page.Controls.Add(_macroMoveUpBtn);
            page.Controls.Add(_macroMoveDownBtn);
            page.Controls.Add(_exportMacroBtn);
            page.Controls.Add(_importMacroBtn);
            page.Controls.Add(_deleteMacroBtn);
            page.Controls.Add(_notesMacroBtn);
            page.Controls.Add(_exportAllBtn);
            page.Controls.Add(_importAllBtn);
            page.Controls.Add(_mergeMacroBtn);
            page.Controls.Add(_pinMacroBtn);
            page.Controls.Add(_resetMacroStatsBtn);
            page.Controls.Add(_macroSummaryLabel);
            page.Controls.Add(recGroup);
            page.Controls.Add(playGroup);
            page.Controls.Add(liveGroup);

            // Just-for-fun: a colourful trail that follows the mouse cursor.
            _cursorTrailCheck = UiFactory.Check("Colorful cursor trail (just for fun)", 12, 824);
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
                _recordStatusLabel.Text = $"Recording… {_recordedStepCount} steps captured.";
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
                string playName = _liveMonitorMacro != null ? _liveMonitorMacro.Name : "macro";
                string playMeta = _liveMonitorMacro != null
                    ? $"  \u2014  {_liveMonitorMacro.StepCount} steps, \u2248{FormatLiveTime(_liveMonitorMacro.EstimatedDurationMs)}"
                    : "";
                _liveHeaderLabel.Text = "\u25B6 Playing " + playName + playMeta;
            });

            _player.LoopChanged += (s, loop) => UiInvoke(() =>
            {
                _playbackCurrentLoop = loop;
            });

            _player.StepExecuted += (s, index) => UiInvoke(() =>
            {
                if (_playbackTotalSteps > 0)
                {
                    int pct = (int)((index + 1) * 100L / _playbackTotalSteps);
                    if (pct < 0) pct = 0;
                    if (pct > 100) pct = 100;
                    _macroProgressBar.Value = pct;

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
                        remainText = "  •  ~" + FormatDuration(TimeSpan.FromMilliseconds(leftMs)) + " left";
                    }

                    _macroProgressLabel.Text = $"{loopText}  •  {step} {index + 1} / {_playbackTotalSteps}{remainText}";
                    UpdateMacroIndicator($"{loopText}  ·  {step} {index + 1}/{_playbackTotalSteps}");
                }

                HighlightLiveStep(index);
            });

            _player.PlaybackFinished += (s, e) => UiInvoke(() =>
            {
                _playMacroBtn.Enabled = true;
                if (_playOnceBtn != null) _playOnceBtn.Enabled = true;
                _stopPlayBtn.Enabled = false;
                _statusState.Text = Utils.Localization.T("Idle");
                _macroProgressBar.Value = 0;
                _macroProgressLabel.Text = Utils.Localization.T("Ready.");
                ClearLiveHighlight();
                HideMacroIndicator();
                _liveHeaderLabel.ForeColor = _theme.TextMuted;
                _liveHeaderLabel.Text = "Finished \u2014 select a macro to view its steps.";
                UpdateMacroEstTime();
                RefreshBusyLock();

                // Bring the window back if we auto-minimised it for playback.
                if (WindowState == FormWindowState.Minimized)
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

            long cumulative = 0;
            if (macro != null)
            {
                for (int i = 0; i < macro.Actions.Count; i++)
                {
                    MacroAction a = macro.Actions[i];
                    var item = new ListViewItem((i + 1).ToString());
                    item.SubItems.Add(FormatLiveTime(cumulative));
                    item.SubItems.Add(a.Type.ToString());
                    item.SubItems.Add(LiveDetail(a));
                    _liveStepList.Items.Add(item);

                    if (a.Type == MacroActionType.Delay)
                    {
                        cumulative += a.DelayMilliseconds;
                    }
                }
            }

            _liveStepList.EndUpdate();

            if (macro != null)
            {
                _liveHeaderLabel.Text =
                    $"{macro.Name}  —  {macro.StepCount} steps, ≈{FormatLiveTime(macro.EstimatedDurationMs)}" +
                    (macro.TimesPlayed > 0 ? $"  •  played {macro.TimesPlayed}×" : "") +
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
            return "  •  last " + m.LastPlayedUtc.Value.ToLocalTime().ToString("d MMM HH:mm");
        }

        /// <summary>Appends one captured step to the live monitor and scrolls to it.</summary>
        private void AppendLiveStep(MacroAction action)
        {
            int index = _liveStepList.Items.Count;
            var item = new ListViewItem((index + 1).ToString());
            item.SubItems.Add(FormatLiveTime(_liveCumulativeMs));
            item.SubItems.Add(action.Type.ToString());
            item.SubItems.Add(LiveDetail(action));
            _liveStepList.Items.Add(item);
            item.EnsureVisible();

            if (action.Type == MacroActionType.Delay)
            {
                _liveCumulativeMs += action.DelayMilliseconds;
            }

            _liveHeaderLabel.Text = $"● Recording…  {_liveStepList.Items.Count} steps captured";
        }

        /// <summary>Highlights the step currently being played and keeps it in view.</summary>
        private void HighlightLiveStep(int index)
        {
            if (index < 0 || index >= _liveStepList.Items.Count)
            {
                return;
            }

            // Clear the colour from the previously highlighted row.
            if (_liveHighlightIndex >= 0 && _liveHighlightIndex < _liveStepList.Items.Count)
            {
                ListViewItem prev = _liveStepList.Items[_liveHighlightIndex];
                prev.BackColor = _liveStepList.BackColor;
                prev.ForeColor = _liveStepList.ForeColor;
            }

            _liveStepList.SelectedItems.Clear();
            ListViewItem item = _liveStepList.Items[index];
            // Paint the active step with the accent so it stands out clearly during
            // playback, regardless of which control currently has focus.
            item.BackColor = _theme.Accent;
            item.ForeColor = Color.White;
            item.Selected = false;
            item.EnsureVisible();
            _liveHighlightIndex = index;
        }

        private void ClearLiveHighlight()
        {
            if (_liveHighlightIndex >= 0 && _liveHighlightIndex < _liveStepList.Items.Count)
            {
                ListViewItem prev = _liveStepList.Items[_liveHighlightIndex];
                prev.BackColor = _liveStepList.BackColor;
                prev.ForeColor = _liveStepList.ForeColor;
            }
            _liveHighlightIndex = -1;
            _liveStepList.SelectedItems.Clear();
            if (_liveMonitorMacro != null)
            {
                _liveHeaderLabel.Text =
                    $"{_liveMonitorMacro.Name}  —  {_liveMonitorMacro.StepCount} steps, " +
                    $"≈{FormatLiveTime(_liveMonitorMacro.EstimatedDurationMs)}" +
                    (_liveMonitorMacro.TimesPlayed > 0 ? $"  •  played {_liveMonitorMacro.TimesPlayed}×" : "") +
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
        private void LoadMacroDefaultsIntoUi()
        {
            Macro macro = SelectedMacro();
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
                if (_pinMacroBtn != null) _pinMacroBtn.Text = macro.IsFavorite ? "★ Unpin" : "★ Pin";
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

            string steps = $"  \u2022  {macro.StepCount} step(s)";

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

            _macroListBox.BeginUpdate();
            _macroListBox.Items.Clear();
            string filter = _macroFilter?.Trim() ?? string.Empty;

            // Favourites first, otherwise preserve the current order.
            var ordered = new System.Collections.Generic.List<Macro>();
            foreach (var m in _macros.Macros) if (m.IsFavorite) ordered.Add(m);
            foreach (var m in _macros.Macros) if (!m.IsFavorite) ordered.Add(m);

            foreach (var m in ordered)
            {
                if (filter.Length == 0 ||
                    (m.Name != null && m.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    _macroListBox.Items.Add(m);
                }
            }
            _macroListBox.EndUpdate();

            if (_macroSummaryLabel != null)
            {
                int count = _macros.Macros.Count;
                long steps = 0;
                foreach (var m in _macros.Macros) steps += m.StepCount;
                _macroSummaryLabel.Text = count == 0
                    ? "No macros saved yet."
                    : $"{count} macro{(count == 1 ? "" : "s")}  •  {steps:N0} steps total";
            }
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

            _recorder.RecordMovements = _recordMovesCheck.Checked;
            _recorder.RecordKeyboard = _recordKeysCheck.Checked;

            // Do not capture any key that participates in toggling recording or in
            // emergency stop — including the modifier keys of those combos.
            _recorder.ExcludedVirtualKeys.Clear();
            AddExclusions(_settings.HotkeyFor(HotkeyAction.ToggleRecordMacro));
            AddExclusions(_settings.HotkeyFor(HotkeyAction.EmergencyStop));

            _recordedStepCount = 0;

            // Remember whether to append to the currently selected macro.
            _appendTarget = _appendRecordCheck.Checked ? SelectedMacro() : null;

            // Reset the live monitor for a fresh recording feed.
            _liveRecording = true;
            _liveCumulativeMs = 0;
            _liveStepList.Items.Clear();
            _liveHighlightIndex = -1;
            _liveHeaderLabel.Text = "● Recording…";

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
                try { WindowState = FormWindowState.Minimized; } catch { }
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

            // Bring the window back if we auto-minimised it for recording.
            if (WindowState == FormWindowState.Minimized)
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
                    _recordStatusLabel.Text =
                        $"Appended {_lastRecorded.StepCount} steps to '{_appendTarget.Name}'.";
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
                    _recordStatusLabel.Text = $"Saved '{_lastRecorded.Name}' ({_lastRecorded.StepCount} steps).";
                }
            }
            else
            {
                _recordStatusLabel.Text = Utils.Localization.T("Nothing was recorded.");
            }

            _appendTarget = null;

            // Make sure the window is visible again if it was hidden.
            if (!Visible)
            {
                Show();
            }
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
        }

        // ── Playback ───────────────────────────────────────────────────────────

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

            _statusState.Text = Utils.Localization.T("Playing macro");
            ShowMacroIndicator(macro.Name);

            // Get out of the way during playback (same option as recording), but only
            // if a stop hotkey is bound so the user can always finish while minimised.
            bool canStopByHotkey =
                (_settings?.HotkeyFor(HotkeyAction.StopMacro)?.IsValid ?? false) ||
                (_settings?.HotkeyFor(HotkeyAction.EmergencyStop)?.IsValid ?? false);
            if (_settings != null && _settings.MinimizeWhileRecording && canStopByHotkey)
            {
                try { WindowState = FormWindowState.Minimized; } catch { }
            }

            _player.Play(macro, loops, speed, macro.LoopDelayMs);
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
                dialog.Title = "Export Macro";
                dialog.Filter = "AutoClicker macro (*.json)|*.json|All files (*.*)|*.*";
                dialog.FileName = MakeSafeFileName(macro.Name) + ".json";

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (MacroStore.ExportToFile(macro, dialog.FileName))
                    {
                        ShowInfo("Macro exported to:\n" + dialog.FileName);
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
                dialog.Title = "Import Macro";
                dialog.Filter = "AutoClicker macro (*.json)|*.json|All files (*.*)|*.*";
                dialog.CheckFileExists = true;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    Macro imported = _macros.ImportFromFile(dialog.FileName);
                    if (imported != null)
                    {
                        _macros.Save();
                        RefreshMacroList();
                        SelectMacro(imported);
                        ShowInfo($"Imported '{imported.Name}' ({imported.StepCount} steps).");
                    }
                    else
                    {
                        ShowWarning("Could not import that file. It may not be a valid macro.");
                    }
                }
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
            _pinMacroBtn.Text = macro.IsFavorite ? "★ Unpin" : "★ Pin";
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

            foreach (MacroAction a in source.Actions)
            {
                target.Actions.Add(a.Clone());
            }

            _macros.Save();
            SelectMacro(target);
            if (_liveMonitorMacro == target || !_player.IsPlaying)
            {
                PopulateLiveMonitor(target);
            }
            UpdateMacroEstTime();
            ShowInfo($"Merged {source.Actions.Count} step(s) from '{source.Name}' into '{target.Name}'.");
        }

        /// <summary>Small modal list picker; returns the chosen macro or null.</summary>
        private Macro ChooseMacro(string title, string prompt, System.Collections.Generic.List<Macro> options)
        {
            using (var dlg = new Form())
            using (var list = new ListBox())
            {
                dlg.Text = title;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
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
                dialog.Title = "Export all macros";
                dialog.Filter = "Tempo macros (*.json)|*.json|All files (*.*)|*.*";
                dialog.FileName = "tempo-macros.json";

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (MacroStore.ExportAllToFile(_macros.Macros, dialog.FileName))
                    {
                        ShowInfo($"Exported {_macros.Macros.Count} macro(s).");
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
                dialog.Title = "Import macros";
                dialog.Filter = "Tempo macros (*.json)|*.json|All files (*.*)|*.*";
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
                ShowInfo(count == 0 ? "No macros found in that file." : $"Imported {count} macro(s).");
            }
        }

        /// <summary>Plays the macro at the given 1-based slot (for quick-play hotkeys).</summary>
        private void PlayMacroSlot(int slot)
        {
            int index = slot - 1;
            if (index < 0 || index >= _macros.Macros.Count || _player.IsPlaying)
            {
                return;
            }

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
                $"Delete macro '{macro.Name}'?",
                "Tempo",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm == DialogResult.Yes)
            {
                _macros.Remove(macro.Name);
                _macros.Save();
                RefreshMacroList();
            }
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
