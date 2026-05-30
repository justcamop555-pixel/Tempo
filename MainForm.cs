using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Engine;
using AutoClicker.Models;
using AutoClicker.Native;
using AutoClicker.Persistence;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// The application's main window. The implementation is split across several
    /// partial files, one per tab, to keep each section focused:
    ///   MainForm.cs            - fields, lifecycle, tray, hotkeys, engine events
    ///   MainForm.Clicker.cs    - the primary clicker tab
    ///   MainForm.MultiPoint.cs - the multi-point editor tab
    ///   MainForm.Macros.cs     - macro recording / playback tab
    ///   MainForm.Statistics.cs - live statistics tab
    ///   MainForm.Settings.cs   - settings tab
    /// </summary>
    public partial class MainForm : Form
    {
        // ── Core services ─────────────────────────────────────────────────────
        private readonly SessionStatistics _statistics = new SessionStatistics();
        private ClickEngine _engine;
        private GlobalHotkeyManager _hotkeys;
        private readonly ProfileManager _profiles = new ProfileManager();
        private readonly MacroStore _macros = new MacroStore();
        private readonly SessionHistoryStore _history = new SessionHistoryStore();
        private long _runStartClicks;
        private MacroRecorder _recorder;
        private MacroPlayer _player;
        private AppSettings _settings;
        private Theme _theme;

        // ── Top level UI ──────────────────────────────────────────────────────
        private ModernTabControl _tabs;
        private StatusStrip _statusStrip;
        private BrandHeader _header;
        private Label _headerProfile;
        private StatusPill _statePill;
        private ToolStripStatusLabel _statusState;
        private ToolStripStatusLabel _statusClicks;
        private ToolStripStatusLabel _statusCps;
        private ToolStripStatusLabel _statusProfile;
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _trayAlwaysOnTopItem;
        private System.Windows.Forms.Timer _uiTimer;
        private System.Windows.Forms.Timer _holdPollTimer;
        private bool _holdActive;
        private bool _reallyClosing;
        private long _lifetimeBaseline;

        // ── Clicker tab controls ──────────────────────────────────────────────
        private ComboBox _profileCombo;
        private Button _newProfileBtn;
        private Button _saveProfileBtn;
        private Button _deleteProfileBtn;
        private Button _duplicateProfileBtn;
        private TextBox _profileNameText;

        private NumericUpDown _hoursNum;
        private NumericUpDown _minutesNum;
        private NumericUpDown _secondsNum;
        private NumericUpDown _millisNum;

        private ComboBox _buttonCombo;
        private ComboBox _styleCombo;
        private ComboBox _modeCombo;

        private RadioButton _posCurrentRadio;
        private RadioButton _posFixedRadio;
        private RadioButton _posMultiRadio;
        private NumericUpDown _fixedXNum;
        private NumericUpDown _fixedYNum;
        private Button _pickFixedBtn;

        private RadioButton _repeatUntilRadio;
        private RadioButton _repeatCountRadio;
        private NumericUpDown _repeatCountNum;

        private NumericUpDown _burstSizeNum;
        private NumericUpDown _burstPauseNum;
        private GroupBox _burstGroup;

        private CheckBox _randIntervalCheck;
        private NumericUpDown _intervalJitterNum;
        private CheckBox _randPosCheck;
        private NumericUpDown _posJitterNum;

        private Button _startBtn;
        private Button _stopBtn;
        private Button _cpsTestBtn;
        private Label _bigStatusLabel;

        // ── Multi-point tab controls ──────────────────────────────────────────
        private ListView _pointsList;
        private Button _addPointBtn;
        private Button _editPointBtn;
        private Button _removePointBtn;
        private Button _clearPointsBtn;
        private Button _movePointUpBtn;
        private Button _movePointDownBtn;
        private Button _capturePointBtn;
        private Button _duplicatePointBtn;
        private Button _togglePointBtn;
        private Button _showPointsBtn;
        private ComboBox _pointOrderCombo;
        private Label _cycleInfoLabel;

        // ── Macros tab controls ───────────────────────────────────────────────
        private ListBox _macroListBox;
        private Button _recordBtn;
        private Button _stopRecordBtn;
        private Button _playMacroBtn;
        private Button _stopPlayBtn;
        private Button _deleteMacroBtn;
        private NumericUpDown _macroLoopNum;
        private NumericUpDown _macroSpeedNum;
        private CheckBox _recordMovesCheck;
        private Label _recordStatusLabel;
        private Macro _lastRecorded;

        // ── Statistics tab controls ───────────────────────────────────────────
        private StatCard _cardSessionClicks;
        private StatCard _cardTotalClicks;
        private StatCard _cardCurrentCps;
        private StatCard _cardPeakCps;
        private StatCard _cardAvgCps;
        private StatCard _cardClicksPerMin;
        private StatCard _cardElapsed;
        private StatCard _cardLeft;
        private StatCard _cardRight;
        private StatCard _cardMiddle;
        private StatCard _cardLifeClicks;
        private StatCard _cardLifeSessions;
        private StatCard _cardLifePeak;
        private StatCard _cardLifeRuntime;
        private StatCard _cardMostClicks;
        private StatCard _cardLongestRun;
        private StatCard _cardToday;
        private StatCard _cardAvgPerSession;
        private StatCard _cardAvgRunLength;
        private SparklineControl _cpsSparkline;
        private DistributionBar _distBar;
        private MiniBarChart _sessionBarChart;
        private MiniBarChart _dailyBarChart;
        private ListView _sessionHistoryList;
        private int _histSortColumn = -1;
        private bool _histSortAsc = true;
        private Button _resetStatsBtn;
        private Button _resetLifetimeBtn;

        // ── Settings tab controls ─────────────────────────────────────────────
        private ComboBox _themeCombo;
        private CheckBox _minimizeToTrayCheck;
        private CheckBox _startMinimizedCheck;
        private CheckBox _trayNotifyCheck;
        private CheckBox _alwaysOnTopCheck;
        private CheckBox _confirmExitCheck;
        private CheckBox _safetyEscapeCheck;
        private NumericUpDown _startDelayNum;
        private Button _saveSettingsBtn;

        public MainForm()
        {
            Logger.Initialize();

            _settings = SettingsManager.Load();
            _lifetimeBaseline = _settings.LifetimeClicks;
            _profiles.Load();
            _macros.Load();
            _history.Load();

            _theme = Theme.ForKind(_settings.Theme);
            _engine = new ClickEngine(_statistics);
            _hotkeys = new GlobalHotkeyManager();
            _recorder = new MacroRecorder();
            _player = new MacroPlayer();

            InitializeShell();
            BuildClickerTab();
            BuildMultiPointTab();
            BuildMacrosTab();
            BuildStatisticsTab();
            BuildKeybindsTab();
            BuildSettingsTab();

            WireEngineEvents();
            WireHotkeyEvents();
            WireMacroEvents();

            ApplyThemeToEverything();
            LoadInitialProfile();
            RefreshMacroList();
            LoadKeybindsIntoUi();
            LoadSettingsIntoUi();

            ApplyHotkeysFromSettings();
            ApplyWindowPreferences();

            MaybeCheckForUpdatesOnLaunch();
        }

        /// <summary>
        /// If enabled, checks for a newer version in the background shortly after
        /// launch and notifies the user only when an update is actually available.
        /// </summary>
        private void MaybeCheckForUpdatesOnLaunch()
        {
            if (_settings == null || !_settings.CheckForUpdatesOnLaunch)
            {
                return;
            }

            System.Threading.Tasks.Task.Run(() =>
            {
                // Small delay so the window settles before any dialog could appear.
                System.Threading.Thread.Sleep(2500);
                AutoClicker.Utils.UpdateChecker.UpdateResult result =
                    AutoClicker.Utils.UpdateChecker.Check();

                if (result != null && result.Success && result.UpdateAvailable)
                {
                    UiInvoke(() => PresentUpdateResult(result, announceUpToDate: false));
                }
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Shell construction
        // ─────────────────────────────────────────────────────────────────────

        private void InitializeShell()
        {
            Text = "Tempo";
            MinimumSize = new Size(740, 700);
            Size = new Size(800, 824);
            StartPosition = FormStartPosition.CenterScreen;
            Font = UiFactory.BodyFont;
            Icon = SystemIcons.Application;

            _tabs = new ModernTabControl
            {
                Dock = DockStyle.Fill,
                Font = UiFactory.BodyFont
            };

            BuildHeader();

            _statusStrip = new StatusStrip();
            _statusState = new ToolStripStatusLabel("Idle") { AutoSize = true };
            _statusProfile = new ToolStripStatusLabel("Profile: -") { AutoSize = true };
            _statusClicks = new ToolStripStatusLabel("Clicks: 0") { AutoSize = true };
            _statusCps = new ToolStripStatusLabel("CPS: 0.0") { AutoSize = true };

            var spring = new ToolStripStatusLabel { Spring = true };
            _statusStrip.Items.Add(_statusState);
            _statusStrip.Items.Add(new ToolStripSeparator());
            _statusStrip.Items.Add(_statusProfile);
            _statusStrip.Items.Add(spring);
            _statusStrip.Items.Add(_statusClicks);
            _statusStrip.Items.Add(_statusCps);

            // Order matters for docking: status strip bottom first, header top
            // next, then the tab control fills the remainder.
            Controls.Add(_tabs);
            Controls.Add(_header);
            Controls.Add(_statusStrip);

            SetupTray();

            _uiTimer = new System.Windows.Forms.Timer { Interval = 200 };
            _uiTimer.Tick += (s, e) => UpdateLiveDisplays();
            _uiTimer.Start();

            _holdPollTimer = new System.Windows.Forms.Timer { Interval = 15 };
            _holdPollTimer.Tick += (s, e) => PollHoldKey();
        }

        private void BuildHeader()
        {
            _header = new BrandHeader();

            _headerProfile = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                AutoSize = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Width = 360,
                Height = 24,
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            };

            _statePill = new StatusPill
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Width = 110,
                Height = 28,
                Text = "IDLE"
            };

            _header.Controls.Add(_headerProfile);
            _header.Controls.Add(_statePill);

            _header.Resize += (s, e) => LayoutHeader();
            LayoutHeader();
        }

        private void LayoutHeader()
        {
            if (_header == null)
            {
                return;
            }

            const int rightPad = 18;
            const int gap = 12;

            _statePill.Top = (_header.Height - _statePill.Height) / 2;
            _statePill.Left = _header.ClientSize.Width - _statePill.Width - rightPad;

            _headerProfile.Top = (_header.Height - _headerProfile.Height) / 2;
            _headerProfile.Left = _statePill.Left - _headerProfile.Width - gap;
        }

        private void SetupTray()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Show / Hide", null, (s, e) => ToggleWindowVisibility());
            _trayMenu.Items.Add("Start / Stop", null, (s, e) => ToggleEngine());

            _trayMenu.Items.Add(new ToolStripSeparator());

            _trayAlwaysOnTopItem = new ToolStripMenuItem("Always on top")
            {
                CheckOnClick = true,
                Checked = _settings != null && _settings.AlwaysOnTop
            };
            _trayAlwaysOnTopItem.Click += (s, e) =>
            {
                // Calling ToggleAlwaysOnTop after CheckOnClick already flipped the
                // menu item would double-flip; just sync the setting and re-assert.
                _settings.AlwaysOnTop = _trayAlwaysOnTopItem.Checked;
                if (_alwaysOnTopCheck != null)
                {
                    _alwaysOnTopCheck.Checked = _settings.AlwaysOnTop;
                }
                ReassertTopMost();
            };
            _trayMenu.Items.Add(_trayAlwaysOnTopItem);

            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Exit", null, (s, e) => ExitApplication());

            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "Tempo",
                Visible = true,
                ContextMenuStrip = _trayMenu
            };
            _trayIcon.DoubleClick += (s, e) => ToggleWindowVisibility();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Engine event wiring (marshalled to the UI thread)
        // ─────────────────────────────────────────────────────────────────────

        private void WireEngineEvents()
        {
            _engine.StateChanged += (s, e) => UiInvoke(() => OnEngineStateChanged(e.NewState));
            _engine.RunCompleted += (s, e) => UiInvoke(OnEngineRunCompleted);
        }

        private void OnEngineStateChanged(EngineState state)
        {
            switch (state)
            {
                case EngineState.Running:
                    _bigStatusLabel.Text = "RUNNING";
                    _bigStatusLabel.ForeColor = _theme.Success;
                    _statusState.Text = "Running";
                    _startBtn.Enabled = false;
                    _stopBtn.Enabled = true;
                    break;

                case EngineState.Idle:
                    _bigStatusLabel.Text = "IDLE";
                    _bigStatusLabel.ForeColor = _theme.TextMuted;
                    _statusState.Text = "Idle";
                    _startBtn.Enabled = true;
                    _stopBtn.Enabled = false;
                    break;

                case EngineState.Paused:
                    _bigStatusLabel.Text = "PAUSED";
                    _bigStatusLabel.ForeColor = _theme.Warning;
                    _statusState.Text = "Paused";
                    _startBtn.Enabled = false;
                    _stopBtn.Enabled = true;
                    break;
            }

            RefreshStatePill();
        }

        private void OnEngineRunCompleted()
        {
            // The worker finished — either it was stopped or a fixed repeat count
            // was reached. Reflect the idle state in the UI and persist stats.
            _startBtn.Enabled = true;
            _stopBtn.Enabled = false;
            _bigStatusLabel.Text = "IDLE";
            _bigStatusLabel.ForeColor = _theme.TextMuted;
            _statusState.Text = "Idle";
            RefreshStatePill();

            // Roll this run's figures into the lifetime totals.
            if (_statistics.PeakClicksPerSecond > _settings.LifetimePeakCps)
            {
                _settings.LifetimePeakCps = _statistics.PeakClicksPerSecond;
            }

            double runSeconds = _statistics.GetElapsed().TotalSeconds;
            _settings.LifetimeRuntimeSeconds += (long)runSeconds;

            // Record this run in the session history (skip empty runs).
            long runClicks = _statistics.TotalClicks - _runStartClicks;
            if (runClicks > 0)
            {
                var record = new Models.SessionRecord
                {
                    WhenUtc = DateTime.UtcNow,
                    Clicks = runClicks,
                    DurationSeconds = runSeconds,
                    AverageCps = runSeconds > 0.01 ? runClicks / runSeconds : 0,
                    PeakCps = _statistics.PeakClicksPerSecond,
                    Profile = _currentProfileName ?? ""
                };
                _history.Add(record);
                RefreshSessionHistory();

                if (runClicks > _settings.LifetimeMostClicksRun)
                {
                    _settings.LifetimeMostClicksRun = runClicks;
                }
                if ((long)runSeconds > _settings.LifetimeLongestRunSeconds)
                {
                    _settings.LifetimeLongestRunSeconds = (long)runSeconds;
                }
            }

            PersistLifetimeStats();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Hotkey wiring
        // ─────────────────────────────────────────────────────────────────────

        private void WireHotkeyEvents()
        {
            _hotkeys.HotkeyPressed += (s, e) => PostHotkey(e.Name);
        }

        /// <summary>
        /// Queues a hotkey action onto the message loop. This is deliberately
        /// asynchronous: mouse-button hotkeys are detected inside a low-level mouse
        /// hook callback, and running the action inline there (e.g. a modal
        /// countdown) would stall global input. BeginInvoke lets the hook return
        /// immediately and the action run a moment later on the UI thread.
        /// </summary>
        private void PostHotkey(string name)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                if (IsHandleCreated)
                {
                    BeginInvoke((Action)(() => OnHotkey(name)));
                }
            }
            catch (ObjectDisposedException) { /* shutting down */ }
            catch (InvalidOperationException) { /* handle gone */ }
        }

        private void OnHotkey(string name)
        {
            // The registration name is the HotkeyAction enum value's name.
            if (!Enum.TryParse(name, out HotkeyAction action))
            {
                return;
            }

            DispatchAction(action);
        }

        /// <summary>Executes the handler for a bound action.</summary>
        private void DispatchAction(HotkeyAction action)
        {
            switch (action)
            {
                case HotkeyAction.ToggleStartStop:
                    ToggleEngine();
                    break;
                case HotkeyAction.StartClicking:
                    if (!_engine.IsRunning) BeginStartWithCountdown();
                    break;
                case HotkeyAction.StopClicking:
                    _engine.Stop();
                    break;
                case HotkeyAction.TogglePause:
                    _engine.TogglePause();
                    break;
                case HotkeyAction.PickPosition:
                    PickFixedPosition();
                    break;
                case HotkeyAction.EmergencyStop:
                    EmergencyStop();
                    break;
                case HotkeyAction.NextProfile:
                    CycleProfile(1);
                    break;
                case HotkeyAction.PreviousProfile:
                    CycleProfile(-1);
                    break;
                case HotkeyAction.IncreaseInterval:
                    NudgeInterval(_settings.IntervalStepMilliseconds);
                    break;
                case HotkeyAction.DecreaseInterval:
                    NudgeInterval(-_settings.IntervalStepMilliseconds);
                    break;
                case HotkeyAction.ToggleAlwaysOnTop:
                    ToggleAlwaysOnTop();
                    break;
                case HotkeyAction.ToggleRecordMacro:
                    ToggleMacroRecording();
                    break;
                case HotkeyAction.PlayMacro:
                    PlaySelectedMacroViaHotkey();
                    break;
                case HotkeyAction.StopMacro:
                    _player.Stop();
                    break;
                case HotkeyAction.ShowHideWindow:
                    ToggleWindowVisibility();
                    break;
                case HotkeyAction.ShowPointsOverlay:
                    OnShowPointsOverlay(this, EventArgs.Empty);
                    break;
                case HotkeyAction.ToggleAntiFreeze:
                    ToggleAntiFreezeProtection();
                    break;
                case HotkeyAction.AddPointAtCursor:
                    AddPointAtCursor();
                    break;
                case HotkeyAction.PlayMacro1:
                    PlayMacroSlot(1);
                    break;
                case HotkeyAction.PlayMacro2:
                    PlayMacroSlot(2);
                    break;
                case HotkeyAction.PlayMacro3:
                    PlayMacroSlot(3);
                    break;
            }
        }

        /// <summary>
        /// Registers all bound global hotkeys. The Toggle-Start/Stop hotkey is left
        /// unregistered while the active mode is hold-to-click, because that mode
        /// polls the key state directly instead.
        /// </summary>
        private void ApplyHotkeysFromSettings()
        {
            _hotkeys.UnregisterAll();

            bool holdMode = GetSelectedMode() == ClickMode.HoldToClick;
            _settings.EnsureBindings();

            foreach (var binding in _settings.Bindings)
            {
                if (binding == null || binding.Hotkey == null || !binding.Hotkey.IsValid)
                {
                    continue;
                }

                // Skip the toggle hotkey in hold mode so the poll timer owns it.
                if (holdMode && binding.Action == HotkeyAction.ToggleStartStop)
                {
                    continue;
                }

                if (binding.Hotkey.IsMouse)
                {
                    _hotkeys.RegisterMouse(binding.Action.ToString(), binding.Hotkey);
                }
                else
                {
                    _hotkeys.Register(
                        binding.Action.ToString(),
                        binding.Hotkey.GetModifierFlags(),
                        binding.Hotkey.GetVirtualKey());
                }
            }

            // Enable hold polling only in hold mode.
            _holdPollTimer.Enabled = holdMode;
        }

        private void PollHoldKey()
        {
            HotkeyDefinition toggle = _settings.HotkeyFor(HotkeyAction.ToggleStartStop);
            if (toggle == null || !toggle.IsValid)
            {
                return;
            }

            // Resolve the virtual key to poll: a mouse button maps to its own VK
            // (GetAsyncKeyState works for mouse buttons too), otherwise the key.
            int vk = toggle.IsMouse ? MouseButtonVk(toggle.MouseButton) : (int)toggle.Key;
            if (vk == 0)
            {
                return;
            }

            bool down = (NativeMethods.GetAsyncKeyState(vk) & NativeMethods.KEY_PRESSED_MASK) != 0;

            // Require the configured modifiers too, so e.g. "Shift + X1 (hold)"
            // only engages while Shift is also held.
            if (down && !toggle.ModifiersMatch(v => (NativeMethods.GetAsyncKeyState(v) & NativeMethods.KEY_PRESSED_MASK) != 0))
            {
                down = false;
            }

            if (down && !_holdActive)
            {
                _holdActive = true;
                StartEngine();
            }
            else if (!down)
            {
                if (_holdActive)
                {
                    _holdActive = false;
                    _engine.Stop();
                }
                else if (_engine.IsRunning)
                {
                    // Safety net: in Hold mode the engine should never be running
                    // when the hold key is not physically held — even if something
                    // else (a bound "Start clicking" hotkey, say) started it.
                    _engine.Stop();
                }
            }
        }

        /// <summary>Maps a mouse-button trigger to its Win32 virtual-key code.</summary>
        private static int MouseButtonVk(HotkeyMouseButton button)
        {
            switch (button)
            {
                case HotkeyMouseButton.Left: return 0x01;   // VK_LBUTTON
                case HotkeyMouseButton.Right: return 0x02;  // VK_RBUTTON
                case HotkeyMouseButton.Middle: return 0x04; // VK_MBUTTON
                case HotkeyMouseButton.XButton1: return 0x05; // VK_XBUTTON1
                case HotkeyMouseButton.XButton2: return 0x06; // VK_XBUTTON2
                default: return 0;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Engine control helpers
        // ─────────────────────────────────────────────────────────────────────

        private void ToggleEngine()
        {
            if (_engine.IsRunning)
            {
                _engine.Stop();
            }
            else
            {
                BeginStartWithCountdown();
            }
        }

        /// <summary>
        /// Starts clicking, first showing a countdown overlay if the user has
        /// configured a start delay. Used by the explicit start paths (Start
        /// button, Start/Toggle hotkeys) but never by Hold mode, which must engage
        /// instantly.
        /// </summary>
        private void BeginStartWithCountdown()
        {
            if (_engine.IsRunning)
            {
                return;
            }

            // Validate up-front so an invalid profile fails immediately instead of
            // after sitting through the countdown.
            string error = BuildProfileFromUi().Validate();
            if (error != null)
            {
                ShowWarning(error);
                return;
            }

            int secs = _settings != null ? _settings.ClickerStartDelaySeconds : 0;
            if (secs > 0)
            {
                using (var overlay = new CountdownOverlayForm(_theme, secs))
                {
                    if (overlay.ShowDialog() != DialogResult.OK)
                    {
                        return; // user cancelled with Esc
                    }
                }
            }

            StartEngine();
        }

        private void StartEngine()
        {
            bool wasRunning = _engine.IsRunning;

            // Apply anti-freeze settings to the engine before each start.
            ApplyAntiFreezeToEngine();

            ClickProfile profile = BuildProfileFromUi();
            string error = _engine.Start(profile);
            if (error != null)
            {
                ShowWarning(error);
                return;
            }

            // Only bump the lifetime session counter on a real idle → running
            // transition, so the hold-mode poll timer cannot inflate it.
            if (!wasRunning && _engine.IsRunning)
            {
                _settings.LifetimeSessions++;
                _runStartClicks = _statistics.TotalClicks;
            }

            if (_settings.ShowTrayNotifications && !Visible)
            {
                _trayIcon.ShowBalloonTip(1500, "Tempo", "Clicking started.", ToolTipIcon.Info);
            }

            // Optionally tuck the window away to the tray once clicking begins.
            if (!wasRunning && _engine.IsRunning && _settings.HideWhenClicking && Visible)
            {
                Hide();
            }
        }

        /// <summary>Copies the user's anti-freeze settings onto the engine.</summary>
        private void ApplyAntiFreezeToEngine()
        {
            if (_engine == null || _settings == null)
            {
                return;
            }

            _engine.AntiFreezeEnabled = _settings.AntiFreezeEnabled;
            _engine.MaxClicksPerSecond = _settings.MaxClicksPerSecond < 1 ? 1 : _settings.MaxClicksPerSecond;
            _engine.CpuThresholdPercent = _settings.AntiFreezeCpuThreshold;
        }

        private void EmergencyStop()
        {
            _holdActive = false;
            _engine.Stop();
            _player.Stop();

            // Go through StopRecording so the record/stop buttons, status label,
            // and macro saving all behave the same way as a normal stop.
            if (_recorder.IsRecording)
            {
                StopRecording();
            }

            _statusState.Text = "Stopped (emergency)";
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Live display refresh
        // ─────────────────────────────────────────────────────────────────────

        private void UpdateLiveDisplays()
        {
            _statusClicks.Text = $"Clicks: {_statistics.SessionClicks}";
            _statusCps.Text = $"CPS: {_statistics.GetCurrentCps():0.0}";

            UpdateStatisticsTab();
            UpdateAntiFreezeStatus();
            UpdateMultiPointLive();
        }

        /// <summary>
        /// Refreshes the live anti-freeze "detection" readout in the Clicker tab:
        /// measured CPU, the actual rate being issued, and whether protection is
        /// currently throttling the clicker.
        /// </summary>
        private void UpdateAntiFreezeStatus()
        {
            if (_antiFreezeStatusLabel == null)
            {
                return;
            }

            if (!_settings.AntiFreezeEnabled)
            {
                _antiFreezeStatusLabel.Text = "Detection: off — no rate limit";
                _antiFreezeStatusLabel.ForeColor = _theme.TextMuted;
                return;
            }

            if (!_engine.IsRunning)
            {
                _antiFreezeStatusLabel.Text = $"Detection: idle  •  cap {_settings.MaxClicksPerSecond} CPS";
                _antiFreezeStatusLabel.ForeColor = _theme.TextMuted;
                return;
            }

            double cpu = _engine.MeasuredCpuPercent;
            double cps = _engine.EffectiveClicksPerSecond;

            if (_engine.IsThrottling)
            {
                _antiFreezeStatusLabel.Text = $"⚠ Throttling — CPU {cpu:0}%  •  holding {cps:0.0} CPS";
                _antiFreezeStatusLabel.ForeColor = _theme.Warning;
            }
            else
            {
                _antiFreezeStatusLabel.Text = $"✓ Protected — CPU {cpu:0}%  •  {cps:0.0} CPS";
                _antiFreezeStatusLabel.ForeColor = _theme.Success;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Theming
        // ─────────────────────────────────────────────────────────────────────

        private void ApplyThemeToEverything()
        {
            _theme = Theme.ForKind(_settings.Theme);
            BackColor = _theme.Background;
            ForeColor = _theme.Text;
            ThemeManager.Apply(this, _theme);

            // Re-apply accent colours to the primary action buttons.
            if (_startBtn != null)
            {
                _startBtn.BackColor = _theme.Success;
                _startBtn.ForeColor = Color.White;
                _startBtn.FlatAppearance.BorderSize = 0;
            }

            if (_stopBtn != null)
            {
                _stopBtn.BackColor = _theme.Danger;
                _stopBtn.ForeColor = Color.White;
                _stopBtn.FlatAppearance.BorderSize = 0;
            }

            // Custom controls that don't go through ThemeManager.
            if (_tabs != null)
            {
                _tabs.ApplyTheme(_theme);
            }

            if (_header != null)
            {
                _header.ApplyTheme(_theme);
            }
            if (_headerProfile != null)
            {
                _headerProfile.ForeColor = _theme.TextMuted;
            }

            // Pill colour is driven by current engine state; refresh it.
            RefreshStatePill();

            // Theme the statistics dashboard cards + graph.
            ApplyThemeToStatCards();

            Invalidate(true);
        }

        private void RefreshStatePill()
        {
            if (_statePill == null || _theme == null)
            {
                return;
            }

            EngineState state = _engine != null ? _engine.State : EngineState.Idle;
            switch (state)
            {
                case EngineState.Running:
                    _statePill.PillColor = _theme.Success;
                    _statePill.Text = "RUNNING";
                    break;
                case EngineState.Paused:
                    _statePill.PillColor = _theme.Warning;
                    _statePill.Text = "PAUSED";
                    break;
                case EngineState.Idle:
                default:
                    _statePill.PillColor = _theme.TextMuted;
                    _statePill.Text = "IDLE";
                    break;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Window / tray behaviour
        // ─────────────────────────────────────────────────────────────────────

        private void ApplyWindowPreferences()
        {
            ReassertTopMost();

            if (_settings.RememberWindowPosition &&
                _settings.WindowLeft >= 0 && _settings.WindowTop >= 0)
            {
                var bounds = SystemInformation.VirtualScreen;
                int left = Math.Min(Math.Max(_settings.WindowLeft, bounds.Left), bounds.Right - 100);
                int top = Math.Min(Math.Max(_settings.WindowTop, bounds.Top), bounds.Bottom - 100);
                StartPosition = FormStartPosition.Manual;
                Location = new Point(left, top);
            }

            // NOTE: "Start minimised to tray" is intentionally handled in the
            // SetVisibleCore override below, not here. Calling BeginInvoke/Hide in
            // the constructor throws because the window handle does not exist yet.
        }

        private bool _startMinimizedApplied;

        /// <summary>
        /// Honours the "start minimised to tray" option without a visible flash by
        /// suppressing the very first show. The handle is still created so timers
        /// and global hotkeys work while the window sits in the tray.
        /// </summary>
        protected override void SetVisibleCore(bool value)
        {
            if (!_startMinimizedApplied && value &&
                _settings != null && _settings.StartMinimizedToTray)
            {
                _startMinimizedApplied = true;

                if (!IsHandleCreated)
                {
                    CreateHandle();
                }

                base.SetVisibleCore(false);

                if (_settings.ShowTrayNotifications && _trayIcon != null)
                {
                    _trayIcon.ShowBalloonTip(1500, "Tempo", "Running in the tray.", ToolTipIcon.Info);
                }

                return;
            }

            base.SetVisibleCore(value);
        }

        private void ToggleWindowVisibility()
        {
            if (Visible)
            {
                Hide();
            }
            else
            {
                BringToFront();
                Show();
                WindowState = FormWindowState.Normal;
                EnsureOnScreen();
                Activate();
                ReassertTopMost();
            }
        }

        private void ToggleAlwaysOnTop()
        {
            _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
            ReassertTopMost();

            // Keep the Settings tab checkbox in sync if it exists.
            if (_alwaysOnTopCheck != null)
            {
                _alwaysOnTopCheck.Checked = _settings.AlwaysOnTop;
            }

            // Update the tray menu item's checked state too.
            if (_trayAlwaysOnTopItem != null)
            {
                _trayAlwaysOnTopItem.Checked = _settings.AlwaysOnTop;
            }

            if (_settings.ShowTrayNotifications)
            {
                _trayIcon?.ShowBalloonTip(1000, "Tempo",
                    _settings.AlwaysOnTop ? "Always on top: ON" : "Always on top: OFF",
                    ToolTipIcon.Info);
            }
        }

        /// <summary>
        /// Forces the window's HWND_TOPMOST state to match
        /// <c>_settings.AlwaysOnTop</c>. Windows loses the topmost z-order across
        /// <c>Hide()</c> / <c>Show()</c> cycles, restore-from-minimised, and some
        /// focus changes; the .NET <see cref="Form.TopMost"/> setter no-ops when
        /// the cached value already matches, so we toggle through the opposite
        /// value first to guarantee SetWindowPos is called.
        /// </summary>
        private void ReassertTopMost()
        {
            if (_settings == null)
            {
                return;
            }

            bool desired = _settings.AlwaysOnTop;
            TopMost = !desired;
            TopMost = desired;
        }

        /// <summary>
        /// Makes sure the window is somewhere a human can actually see. Useful
        /// after restoring from the tray on a system where a monitor was
        /// disconnected since the window position was saved.
        /// </summary>
        private void EnsureOnScreen()
        {
            var virt = SystemInformation.VirtualScreen;
            int margin = 80;

            if (Left + Width < virt.Left + margin ||
                Top + Height < virt.Top + margin ||
                Left > virt.Right - margin ||
                Top > virt.Bottom - margin)
            {
                CenterToScreen();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Window-state overrides that re-assert TopMost
        // ─────────────────────────────────────────────────────────────────────

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);

            // Every transition into visible re-asserts TOPMOST so the window
            // doesn't sink behind other windows after a tray-restore.
            if (Visible)
            {
                ReassertTopMost();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            // Coming back from a minimised state is the other case where the
            // HWND topmost flag can quietly disappear.
            if (WindowState == FormWindowState.Normal && Visible)
            {
                ReassertTopMost();
            }
        }

        private void ExitApplication()
        {
            _reallyClosing = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Minimise to tray instead of closing, unless we are really exiting.
            if (!_reallyClosing && _settings.MinimizeToTrayOnClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (_settings.ShowTrayNotifications)
                {
                    _trayIcon.ShowBalloonTip(1500, "Tempo", "Minimised to tray.", ToolTipIcon.Info);
                }
                return;
            }

            if (_engine.IsRunning && _settings.ConfirmBeforeExitWhileRunning && !_reallyClosing)
            {
                var result = MessageBox.Show(
                    "Clicking is still running. Exit anyway?",
                    "Tempo",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            // Update settings in memory; CleanUp() performs a single
            // SettingsManager.Save() during OnFormClosed.
            SaveWindowPosition();
            _settings.LifetimeClicks = _lifetimeBaseline + _statistics.TotalClicks;
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            CleanUp();
            base.OnFormClosed(e);
        }

        private void SaveWindowPosition()
        {
            // Mutate settings in memory only — the single shutdown write happens
            // in CleanUp(). Saving here would be one of three redundant writes
            // during shutdown.
            if (_settings.RememberWindowPosition && WindowState == FormWindowState.Normal)
            {
                _settings.WindowLeft = Location.X;
                _settings.WindowTop = Location.Y;
            }
        }

        private void PersistLifetimeStats()
        {
            // Recompute from the fixed baseline plus this session's in-memory total.
            // Idempotent: repeated calls do not double-count because the baseline
            // is captured once at startup and never folded back in.
            _settings.LifetimeClicks = _lifetimeBaseline + _statistics.TotalClicks;
            SettingsManager.Save(_settings);
        }

        private void CleanUp()
        {
            try
            {
                _uiTimer?.Stop();
                _uiTimer?.Dispose();
                _holdPollTimer?.Stop();
                _holdPollTimer?.Dispose();

                HideRecordingIndicator();

                _engine?.Dispose();
                _player?.Dispose();
                _recorder?.Dispose();
                _hotkeys?.Dispose();

                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }

                _profiles.Save();
                _macros.Save();
                SettingsManager.Save(_settings);
            }
            catch (Exception ex)
            {
                Logger.Error("Error during cleanup.", ex);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Small shared helpers
        // ─────────────────────────────────────────────────────────────────────

        private void UiInvoke(Action action)
        {
            if (action == null || IsDisposed)
            {
                return;
            }

            try
            {
                if (InvokeRequired)
                {
                    // Only marshal once the handle exists. A cross-thread event that
                    // somehow arrives before the window is created is simply dropped,
                    // which is safe because there is no UI yet to update.
                    if (IsHandleCreated)
                    {
                        BeginInvoke(action);
                    }
                }
                else
                {
                    action();
                }
            }
            catch (ObjectDisposedException)
            {
                // The form is gone; ignore.
            }
            catch (InvalidOperationException)
            {
                // Handle not created / being destroyed; ignore.
            }
        }

        private void ShowWarning(string message)
        {
            MessageBox.Show(this, message, "Tempo",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ShowInfo(string message)
        {
            MessageBox.Show(this, message, "Tempo",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
