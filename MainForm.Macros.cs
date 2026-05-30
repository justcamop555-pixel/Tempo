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
        private ListView _liveStepList;
        private Label _liveHeaderLabel;
        private Macro _appendTarget;
        private Macro _liveMonitorMacro;
        private long _liveCumulativeMs;
        private bool _liveRecording;

        // Playback progress + per-macro default bookkeeping.
        private int _playbackTotalSteps;
        private int _playbackTotalLoops;
        private int _playbackCurrentLoop;
        private bool _suppressMacroDefaults;
        private RecordingIndicatorForm _recordIndicator;

        private void BuildMacrosTab()
        {
            var page = new TabPage("Macros") { AutoScroll = true };

            var help = UiFactory.Label(
                "Record mouse and keyboard input, edit the steps, then play it back. " +
                "The Live Monitor below fills in real time as you record and highlights " +
                "each step during playback. Tick \"Append to selected macro\" to add onto " +
                "an existing recording. Bind \"Record\" and \"Play\" on the Keybinds tab " +
                "to control it hands-free.",
                12, 12);
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
            var playGroup = UiFactory.Group("Playback", 444, 292, 268, 236);

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

            _playMacroBtn = UiFactory.PrimaryButton("▶ Play", 16, 140, 118, 38, _theme);
            _playMacroBtn.Click += OnPlayMacroClicked;
            playGroup.Controls.Add(_playMacroBtn);

            _stopPlayBtn = UiFactory.Button("■ Stop", 140, 140, 112, 38);
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
            var liveGroup = UiFactory.Group("Live Monitor", 12, 540, 700, 250);

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
            page.Controls.Add(recGroup);
            page.Controls.Add(playGroup);
            page.Controls.Add(liveGroup);

            _tabs.TabPages.Add(page);
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
                _statusState.Text = "Playing macro";
                _macroProgressBar.Value = 0;
                _playbackCurrentLoop = 0;
                _macroProgressLabel.Text = "Playing…";
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

                    string loopText = _playbackTotalLoops <= 0
                        ? $"Loop {_playbackCurrentLoop} (∞)"
                        : $"Loop {_playbackCurrentLoop} / {_playbackTotalLoops}";
                    _macroProgressLabel.Text = $"{loopText}  •  step {index + 1} / {_playbackTotalSteps}";
                }

                HighlightLiveStep(index);
            });

            _player.PlaybackFinished += (s, e) => UiInvoke(() =>
            {
                _playMacroBtn.Enabled = true;
                _stopPlayBtn.Enabled = false;
                _statusState.Text = "Idle";
                _macroProgressBar.Value = 0;
                _macroProgressLabel.Text = "Ready.";
                ClearLiveHighlight();
            });
        }

        // ── Live Monitor ───────────────────────────────────────────────────────

        /// <summary>Fills the live monitor with a macro's steps (used at rest / on selection / on play).</summary>
        private void PopulateLiveMonitor(Macro macro)
        {
            _liveMonitorMacro = macro;
            _liveStepList.BeginUpdate();
            _liveStepList.Items.Clear();

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
                    $"{macro.Name}  —  {macro.StepCount} steps, ≈{macro.EstimatedDurationMs} ms" +
                    (macro.TimesPlayed > 0 ? $"  •  played {macro.TimesPlayed}×" : "") +
                    (!string.IsNullOrWhiteSpace(macro.Notes) ? $"   “{macro.Notes}”" : "");
            }
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

            _liveStepList.SelectedItems.Clear();
            ListViewItem item = _liveStepList.Items[index];
            item.Selected = true;
            item.EnsureVisible();
        }

        private void ClearLiveHighlight()
        {
            _liveStepList.SelectedItems.Clear();
            if (_liveMonitorMacro != null)
            {
                _liveHeaderLabel.Text =
                    $"{_liveMonitorMacro.Name}  —  {_liveMonitorMacro.StepCount} steps, " +
                    $"≈{_liveMonitorMacro.EstimatedDurationMs} ms" +
                    (_liveMonitorMacro.TimesPlayed > 0 ? $"  •  played {_liveMonitorMacro.TimesPlayed}×" : "");
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
            foreach (var m in _macros.Macros)
            {
                if (filter.Length == 0 ||
                    (m.Name != null && m.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    _macroListBox.Items.Add(m);
                }
            }
            _macroListBox.EndUpdate();
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
            _liveHeaderLabel.Text = "● Recording…";

            string name = "Macro " + DateTime.Now.ToString("HH-mm-ss");
            if (!_recorder.Start(name))
            {
                _liveRecording = false;
                ShowWarning("Could not start recording. The input hook failed to install.");
                return;
            }

            _recordBtn.Enabled = false;
            _stopRecordBtn.Enabled = true;
            _recordStatusLabel.Text = "Recording… bind/press the Record or Emergency-stop hotkey to finish.";
            _statusState.Text = "Recording macro";

            // Show a small always-on-top REC badge so the user knows recording is
            // live even when this window is not focused.
            ShowRecordingIndicator();
        }

        private void ShowRecordingIndicator()
        {
            HideRecordingIndicator();
            try
            {
                _recordIndicator = new RecordingIndicatorForm(_theme);
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
            _statusState.Text = "Idle";
            _liveRecording = false;
            HideRecordingIndicator();

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
                _recordStatusLabel.Text = "Nothing was recorded.";
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

        /// <summary>Plays the selected macro; bound to the Play hotkey.</summary>
        private void PlaySelectedMacroViaHotkey()
        {
            Macro macro = SelectedMacro();
            if (macro != null)
            {
                PlayMacro(macro);
            }
        }

        private void PlayMacro(Macro macro)
        {
            if (macro == null || _player.IsPlaying)
            {
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
                        _macroProgressLabel.Text = "Playback cancelled.";
                        return;
                    }
                }
            }

            int loops = (int)_macroLoopNum.Value;
            double speed = (double)_macroSpeedNum.Value / 10.0;

            // Remember how many steps so the progress bar can show a percentage.
            _playbackTotalSteps = macro.StepCount;
            _playbackTotalLoops = loops;
            _playbackCurrentLoop = 0;

            // Show the macro in the live monitor so playback can highlight steps.
            PopulateLiveMonitor(macro);

            // Track run statistics.
            macro.TimesPlayed++;
            macro.LastPlayedUtc = DateTime.UtcNow;
            _macros.Save();

            _statusState.Text = "Playing macro";
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
                        ShowInfo("Macro exported.");
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

            foreach (int vk in hk.ExcludedVirtualKeys())
            {
                _recorder.ExcludedVirtualKeys.Add(vk);
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
