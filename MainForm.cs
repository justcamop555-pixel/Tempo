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
        private bool _runCompletedHandled;
        private MacroRecorder _recorder;
        private MacroPlayer _player;
        private AppSettings _settings;
        private Theme _theme;

        // ── Top level UI ──────────────────────────────────────────────────────
        private ModernTabControl _tabs;
        private Panel _sidebar;
        private readonly System.Collections.Generic.List<RoundedButton> _navButtons = new System.Collections.Generic.List<RoundedButton>();
        private StatusStrip _statusStrip;
        private BrandHeader _header;
        private GifBackdropPanel _footerGif;
        private Image _fullBgImage;
        private ClickingIndicatorForm _clickingIndicator;
        private CursorTrailForm _cursorTrail;
        private ClickingIndicatorForm _macroIndicator;
        private bool _isFullScreen;
        private FormBorderStyle _fsPrevBorder;
        private FormWindowState _fsPrevState;
        private Rectangle _fsPrevBounds;
        private bool _fsPrevTopMost;
        private Size _fsPrevMinSize;
        private int _fsPrevFooterHeight = -1;
        private readonly System.Collections.Generic.Dictionary<Control, int> _autoFitBaseLeft
            = new System.Collections.Generic.Dictionary<Control, int>();
        private Label _headerProfile;
        private StatusPill _statePill;
        private ToolStripStatusLabel _statusState;
        private ToolStripStatusLabel _statusClicks;
        private ToolStripStatusLabel _statusCps;
        private ToolStripStatusLabel _statusElapsed;
        private ToolStripStatusLabel _statusProfile;
        private ToolStripStatusLabel _statusHint;
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
        private NumericUpDown _holdMsNum;

        private RadioButton _posCurrentRadio;
        private RadioButton _posFixedRadio;
        private RadioButton _posMultiRadio;
        private CheckBox _restoreCursorCheck;
        private NumericUpDown _fixedXNum;
        private NumericUpDown _fixedYNum;
        private Button _pickFixedBtn;

        private RadioButton _repeatUntilRadio;
        private RadioButton _repeatCountRadio;
        private NumericUpDown _repeatCountNum;
        private RadioButton _repeatDurationRadio;
        private NumericUpDown _repeatDurationNum;

        private NumericUpDown _burstSizeNum;
        private NumericUpDown _burstPauseNum;
        private GroupBox _burstGroup;

        private CheckBox _randIntervalCheck;
        private Button _humanizeBtn;
        private NumericUpDown _intervalJitterNum;
        private CheckBox _randPosCheck;
        private NumericUpDown _posJitterNum;

        private Button _startBtn;
        private Button _stopBtn;
        private Button _cpsTestBtn;
        private Label _bigStatusLabel;
        private Label _liveCpsLabel;

        // ── Multi-point tab controls ──────────────────────────────────────────
        private ListView _pointsList;
        private Button _addPointBtn;
        private Button _editPointBtn;
        private Button _removePointBtn;
        private Button _clearPointsBtn;
        private Button _toggleAllPointsBtn;
        private Label _pointsEmptyHint;
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
        private Button _playOnceBtn;
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
        // Insights section (new)
        private StatCard _cardThisWeek;
        private StatCard _cardThisMonth;
        private StatCard _cardLifeAvgCps;
        private StatCard _cardActiveDays;
        private StatCard _cardBestDay;
        private StatCard _cardBusiestWeekday;
        private StatCard _cardBusiestHour;
        private StatCard _cardTopProfile;
        private StatCard _cardStreak;
        private StatCard _cardLongestStreak;
        private StatCard _cardDailyAvg;
        private StatCard _cardThisYear;
        private TabPage _statsPage;
        private MiniBarChart _hourChart;
        private MiniBarChart _weekdayChart;
        private TextBox _historySearchBox;
        private string _historySearchText = "";
        private SparklineControl _cpsSparkline;
        private DistributionBar _distBar;
        private MiniBarChart _sessionBarChart;
        private MiniBarChart _dailyBarChart;
        private ListView _sessionHistoryList;
        private NumericUpDown _sessionGoalNum;
        private ThemedProgressBar _goalProgressBar;
        private Label _goalProgressLabel;
        private ThemedProgressBar _milestoneBar;
        private Label _milestoneLabel;
        private Label _milestoneBadges;
        private long _lastMilestoneClicks = -1; // highest tier reached at last check; -1 = not yet initialised
        private bool _goalReachedNotified;
        private double _displayCps;
        private ComboBox _historyProfileFilter;
        private Label _historySummaryLabel;
        private bool _suppressGoalEvent;
        private bool _suppressHistoryFilterEvent;
        private int _histSortColumn = -1;
        private bool _histSortAsc = true;
        private Button _resetStatsBtn;
        private Button _resetLifetimeBtn;

        // ── Settings tab controls ─────────────────────────────────────────────
        private ComboBox _themeCombo;
        private Label _lastCheckedLabel;
        private ComboBox _languageCombo;
        private CheckBox _minimizeToTrayCheck;
        private CheckBox _startMinimizedCheck;
        private CheckBox _trayNotifyCheck;
        private CheckBox _alwaysOnTopCheck;
        private CheckBox _customAccentCheck;
        private Button _chooseAccentBtn;
        private Panel _accentSwatch;
        private Panel[] _previewSwatches;
        private Button _previewButton;
        private Label _previewSample;
        private CheckBox _confirmExitCheck;
        private CheckBox _safetyEscapeCheck;
        private NumericUpDown _startDelayNum;
        private Button _saveSettingsBtn;

        public MainForm()
        {
            Logger.Initialize();

            _settings = SettingsManager.Load();
            _lifetimeBaseline = _settings.LifetimeClicks;
            Logger.Enabled = _settings.WriteLogFile;

            // Apply the chosen UI language before any tabs/controls are built.
            Localization.Current = _settings.Language;

            _profiles.Load();
            _macros.Load();
            _history.Load();

            _theme = BuildActiveTheme();
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
            BuildSidebar();

            WireEngineEvents();
            WireHotkeyEvents();
            WireMacroEvents();

            ApplyThemeToEverything();
            ApplyBackgroundGif();
            EnableAutoFit();
            RefreshBusyLock();
            LoadInitialProfile();
            RefreshMacroList();
            LoadKeybindsIntoUi();
            LoadSettingsIntoUi();

            ApplyHotkeysFromSettings();
            ApplyWindowPreferences();

            SetupTooltips();
            RestoreLastTab();

            MaybeCheckForUpdatesOnLaunch();
        }

        /// <summary>Reopens the tab that was active last time, and saves changes.</summary>
        private void RestoreLastTab()
        {
            if (_tabs == null || _tabs.TabPages.Count == 0)
            {
                return;
            }

            int index = _settings != null ? _settings.LastTabIndex : 0;
            if (index >= 0 && index < _tabs.TabPages.Count)
            {
                _tabs.SelectedIndex = index;
            }
            RefreshSidebarSelection();

            _tabs.SelectedIndexChanged += (s, e) =>
            {
                if (_settings == null)
                {
                    return;
                }
                // Bring the Statistics dashboard fully up to date the moment it's
                // shown, since the periodic tick skips it while it's hidden.
                if (_tabs.SelectedTab == _statsPage)
                {
                    UpdateStatisticsTab();
                    RefreshSessionHistory();
                }
                _settings.LastTabIndex = _tabs.SelectedIndex;
                try { Persistence.SettingsManager.Save(_settings); } catch { /* best effort */ }
                UpdateBackdropActivePage();
                RefreshSidebarSelection();

                // Re-centre the page now that it's the active one (and therefore sized
                // to the real viewport). This corrects any tab that was last laid out
                // at a different width — e.g. while the window was full-screen — so it
                // can never show up with a stray horizontal scrollbar.
                if (_tabs.SelectedTab != null)
                {
                    CenterPageContent(_tabs.SelectedTab);
                }
            };
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

            // Throttle automatic checks to roughly once a day so we don't hammer
            // the GitHub API (unauthenticated requests are rate-limited per IP).
            if (_settings.LastUpdateCheckUtc != null &&
                (DateTime.UtcNow - _settings.LastUpdateCheckUtc.Value).TotalHours < 20)
            {
                return;
            }

            System.Threading.Tasks.Task.Run(() =>
            {
                // Small delay so the window settles before any dialog could appear.
                System.Threading.Thread.Sleep(2500);
                AutoClicker.Utils.UpdateChecker.UpdateResult result =
                    AutoClicker.Utils.UpdateChecker.Check();

                if (result == null || !result.Success)
                {
                    return; // stay quiet on the launch check; don't reset the timer on failure
                }

                UiInvoke(() =>
                {
                    _settings.LastUpdateCheckUtc = DateTime.UtcNow;
                    SettingsManager.Save(_settings);
                    UpdateLastCheckedLabel();

                    // Don't nag about a version the user chose to skip.
                    bool skipped = !string.IsNullOrWhiteSpace(_settings.SkippedUpdateVersion) &&
                                   string.Equals(_settings.SkippedUpdateVersion, result.LatestVersion?.ToString(),
                                       StringComparison.OrdinalIgnoreCase);

                    if (result.UpdateAvailable && !skipped)
                    {
                        PresentUpdateResult(result, announceUpToDate: false);
                    }
                });
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Shell construction
        // ─────────────────────────────────────────────────────────────────────

        private void InitializeShell()
        {
            Text = "Tempo";
            // Scale the whole UI to the display's DPI (no effect at 100%).
            AutoScaleMode = AutoScaleMode.Font;
            // Wider than before to make room for the left navigation sidebar while
            // still fitting the page content beside it (the Statistics grid is ~740px
            // wide, plus the 188px sidebar and a scrollbar).
            MinimumSize = new Size(980, 700);
            Size = new Size(1020, 824);
            StartPosition = FormStartPosition.CenterScreen;
            Font = UiFactory.BodyFont;
            Icon = Utils.AppIcon.Get();

            _tabs = new ModernTabControl
            {
                Dock = DockStyle.Fill,
                Font = UiFactory.BodyFont
            };

            _sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 188,
                Padding = new Padding(12, 14, 12, 12)
            };
            // A thin divider down the sidebar's right edge to separate it cleanly from
            // the page content.
            _sidebar.Paint += (s, e) =>
            {
                if (_theme == null) return;
                int x = _sidebar.ClientSize.Width - 1;
                using (var pen = new Pen(_theme.Border, 2))
                {
                    e.Graphics.DrawLine(pen, x, 0, x, _sidebar.ClientSize.Height);
                }
            };

            BuildHeader();

            _statusStrip = new StatusStrip();
            _statusState = new ToolStripStatusLabel("Idle") { AutoSize = true };
            _statusProfile = new ToolStripStatusLabel("Profile: -") { AutoSize = true };
            _statusClicks = new ToolStripStatusLabel(Utils.Localization.T("Clicks:") + " 0") { AutoSize = true };
            _statusCps = new ToolStripStatusLabel(Utils.Localization.T("CPS:") + " 0.0") { AutoSize = true };
            _statusElapsed = new ToolStripStatusLabel(Utils.Localization.T("Time:") + " 00:00") { AutoSize = true };
            _statusHint = new ToolStripStatusLabel("") { AutoSize = true };

            _statusStrip.Items.Add(_statusState);
            _statusStrip.Items.Add(new ToolStripSeparator());
            _statusStrip.Items.Add(_statusProfile);
            _statusStrip.Items.Add(new ToolStripStatusLabel { Spring = true });
            _statusStrip.Items.Add(_statusHint);
            _statusStrip.Items.Add(new ToolStripStatusLabel { Spring = true });
            _statusStrip.Items.Add(_statusClicks);
            _statusStrip.Items.Add(_statusCps);
            _statusStrip.Items.Add(_statusElapsed);

            // Order matters for docking: status strip bottom first, header top
            // next, the left sidebar claims the left of the remaining area, then the
            // tab control fills what's left.
            Controls.Add(_tabs);
            Controls.Add(_sidebar);
            _footerGif = new GifBackdropPanel { Dock = DockStyle.Bottom, Height = 46, Visible = false };
            Controls.Add(_footerGif);
            Controls.Add(_header);
            Controls.Add(_statusStrip);

            // Keep the sidebar docked to the *inner* (post header/footer) region so it
            // sits between the header and the status bar rather than spanning the
            // whole window height.
            Controls.SetChildIndex(_sidebar, 1);

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

        /// <summary>Toggles borderless full-screen (bound to F11; Esc also exits).</summary>
        internal void ToggleFullScreen()
        {
            try
            {
                if (!_isFullScreen)
                {
                    _fsPrevBorder = FormBorderStyle;
                    _fsPrevState = WindowState;
                    _fsPrevBounds = Bounds;
                    _fsPrevTopMost = TopMost;
                    _fsPrevMinSize = MinimumSize;

                    // Pick the screen the window is currently on.
                    Rectangle screen = Screen.FromRectangle(Bounds).Bounds;

                    // Order matters: leave maximised state, drop the border, lift any
                    // minimum-size constraint, then cover the whole screen on top of
                    // the taskbar.
                    if (WindowState != FormWindowState.Normal)
                    {
                        WindowState = FormWindowState.Normal;
                    }
                    MinimumSize = new Size(0, 0);
                    FormBorderStyle = FormBorderStyle.None;
                    TopMost = true;
                    Bounds = screen;
                    _isFullScreen = true;

                    // Make the GIF footer band more prominent in full-screen (only if
                    // a GIF is set, so an empty band never grows for no reason).
                    if (_footerGif != null && _footerGif.HasGif)
                    {
                        _fsPrevFooterHeight = _footerGif.Height;
                        _footerGif.Height = Math.Min(160, Math.Max(120, screen.Height / 6));
                    }

                    // Briefly tell the user how to get back out — borderless
                    // full-screen has no close button, and not everyone knows F11.
                    ShowFullScreenToast();
                }
                else
                {
                    _isFullScreen = false;
                    if (_fsToast != null) _fsToast.Visible = false;

                    // Restore the GIF footer band height.
                    if (_footerGif != null && _fsPrevFooterHeight > 0)
                    {
                        _footerGif.Height = _fsPrevFooterHeight;
                        _fsPrevFooterHeight = -1;
                    }

                    FormBorderStyle = _fsPrevBorder;
                    MinimumSize = _fsPrevMinSize;
                    TopMost = _fsPrevTopMost;
                    WindowState = _fsPrevState;
                    if (_fsPrevState == FormWindowState.Normal)
                    {
                        Bounds = _fsPrevBounds;
                    }
                    // Restore the user's always-on-top preference precisely.
                    if (_settings != null)
                    {
                        TopMost = _settings.AlwaysOnTop;
                    }
                }
            }
            catch { }

            // After the window finishes resizing, re-centre every page (not just the
            // active one) so background tabs don't keep full-screen offsets that would
            // show a stray horizontal scrollbar when you switch to them.
            try
            {
                BeginInvoke((Action)RecenterAllPages);
            }
            catch { }
        }

        internal bool IsFullScreen => _isFullScreen;

        private Label _fsToast;
        private System.Windows.Forms.Timer _fsToastTimer;

        /// <summary>
        /// Shows "Full screen — press F11 or Esc to exit" top-centre for a few
        /// seconds after entering full-screen, then hides itself.
        /// </summary>
        private void ShowFullScreenToast()
        {
            try
            {
                if (_fsToast == null)
                {
                    _fsToast = new Label
                    {
                        AutoSize = false,
                        Size = new Size(340, 34),
                        TextAlign = ContentAlignment.MiddleCenter,
                        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                        Visible = false
                    };
                    Controls.Add(_fsToast);
                    _fsToastTimer = new System.Windows.Forms.Timer { Interval = 2800 };
                    _fsToastTimer.Tick += (s, e) =>
                    {
                        _fsToastTimer.Stop();
                        if (_fsToast != null) _fsToast.Visible = false;
                    };
                }

                _fsToast.BackColor = _theme != null ? _theme.Surface2 : SystemColors.ControlDark;
                _fsToast.ForeColor = _theme != null ? _theme.Text : SystemColors.ControlText;
                _fsToast.Text = "Full screen  \u2014  press F11 or Esc to exit";
                _fsToast.Left = (ClientSize.Width - _fsToast.Width) / 2;
                _fsToast.Top = 56;
                _fsToast.Visible = true;
                _fsToast.BringToFront();
                _fsToastTimer.Stop();
                _fsToastTimer.Start();
            }
            catch
            {
                // Cosmetic only.
            }
        }

        /// <summary>
        /// Makes the fixed-width tab content sit centred when the window is wider than
        /// the content (e.g. maximised or full-screen), instead of clinging to the
        /// top-left corner. Only horizontal offsets change — nothing is resized — so
        /// layouts stay intact, and narrow windows keep their normal scroll behaviour.
        /// </summary>
        private void EnableAutoFit()
        {
            if (_tabs == null)
            {
                return;
            }
            foreach (TabPage page in _tabs.TabPages)
            {
                TabPage p = page;
                foreach (Control c in p.Controls)
                {
                    if (!_autoFitBaseLeft.ContainsKey(c))
                    {
                        _autoFitBaseLeft[c] = c.Left;
                    }
                }
                p.SizeChanged += (s, e) => CenterPageContent(p);
                CenterPageContent(p);
            }
        }

        /// <summary>
        /// Builds the left navigation sidebar — one rounded "card" button per tab,
        /// stacked vertically — that drives <c>_tabs.SelectedIndex</c>. This replaces
        /// the old horizontal tab strip across the top.
        /// </summary>
        private void BuildSidebar()
        {
            if (_sidebar == null || _tabs == null)
            {
                return;
            }

            _sidebar.Controls.Clear();
            _navButtons.Clear();

            int top = _sidebar.Padding.Top;
            int width = _sidebar.Width - _sidebar.Padding.Left - _sidebar.Padding.Right;
            const int btnHeight = 44;
            const int gap = 8;

            // Tab order is fixed (Clicker, Multi-Point, Macros, Statistics, Keybinds,
            // Settings), so map a recognisable icon to each by position.
            NavIconKind[] icons =
            {
                NavIconKind.Cursor, NavIconKind.Points, NavIconKind.Macro,
                NavIconKind.Chart, NavIconKind.Keyboard, NavIconKind.Gear
            };

            for (int i = 0; i < _tabs.TabPages.Count; i++)
            {
                int index = i;
                var nav = new RoundedButton
                {
                    Text = _tabs.TabPages[i].Text,
                    Left = _sidebar.Padding.Left,
                    Top = top,
                    Width = width,
                    Height = btnHeight,
                    CornerRadius = 10,
                    IconKind = i < icons.Length ? icons[i] : NavIconKind.None,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(14, 0, 6, 0),
                    Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                nav.FlatAppearance.BorderSize = 0;
                nav.Click += (s, e) =>
                {
                    if (index >= 0 && index < _tabs.TabPages.Count)
                    {
                        _tabs.SelectedIndex = index;
                    }
                };
                _navButtons.Add(nav);
                _sidebar.Controls.Add(nav);
                top += btnHeight + gap;
            }

            RefreshSidebarSelection();
        }

        /// <summary>Highlights the sidebar button for the currently selected tab.</summary>
        private void RefreshSidebarSelection()
        {
            if (_tabs == null || _theme == null)
            {
                return;
            }
            int sel = _tabs.SelectedIndex;
            for (int i = 0; i < _navButtons.Count; i++)
            {
                RoundedButton nav = _navButtons[i];
                nav.FlatAppearance.BorderSize = 0;
                if (i == sel)
                {
                    nav.BackColor = _theme.Accent;
                    nav.ForeColor = Color.White;
                }
                else
                {
                    nav.BackColor = _theme.Surface;
                    nav.ForeColor = _theme.TextMuted;
                }
                nav.Invalidate();
            }
        }

        private void CenterPageContent(TabPage page)
        {
            int minLeft = int.MaxValue;
            int maxRight = 0;
            foreach (Control c in page.Controls)
            {
                if (!_autoFitBaseLeft.TryGetValue(c, out int baseLeft))
                {
                    continue;
                }
                if (baseLeft < minLeft) minLeft = baseLeft;
                if (baseLeft + c.Width > maxRight) maxRight = baseLeft + c.Width;
            }
            if (minLeft == int.MaxValue)
            {
                return;
            }

            // Use the page's own client width — that is the exact width AutoScroll
            // measures against when it decides whether a horizontal scrollbar is
            // needed, and it already accounts for a vertical scrollbar when one is
            // showing. (Going through the tab's DisplayRectangle instead left a ~17px
            // mismatch whenever the vertical bar appeared, which is what intermittently
            // produced the stray bottom scrollbar.)
            int available = page.ClientSize.Width;
            if (available <= 0)
            {
                return;
            }

            int contentWidth = maxRight - minLeft;
            int offset = (available - contentWidth) / 2 - minLeft;
            if (offset < 0) offset = 0;

            // Hard guarantee: never let the right-most control spill past the client
            // width. If it would, pull everything back so it fits exactly. With no
            // control past the right edge, AutoScroll has nothing to scroll sideways,
            // so the horizontal scrollbar simply never appears — no OS-level hacks, no
            // fighting AutoScroll, no intermittent behaviour.
            if (maxRight + offset > available)
            {
                offset = available - maxRight;
            }
            if (offset < 0) offset = 0;

            // Repositioning child controls inside an AutoScroll page makes WinForms
            // snap the view back to the top. Remember the scroll position and put it
            // back so the user stays where they were.
            var scrollable = page as ScrollableControl;
            Point savedScroll = scrollable != null ? scrollable.AutoScrollPosition : Point.Empty;

            foreach (Control c in page.Controls)
            {
                if (_autoFitBaseLeft.TryGetValue(c, out int baseLeft))
                {
                    c.Left = baseLeft + offset;
                }
            }

            if (scrollable != null)
            {
                scrollable.AutoScrollPosition = new Point(-savedScroll.X, -savedScroll.Y);
            }
        }

        /// <summary>
        /// Reads the active tab's scroll position (returns the value as the
        /// <see cref="ScrollableControl.AutoScrollPosition"/> getter reports it, i.e.
        /// with negative offsets when scrolled).
        /// </summary>
        private Point CaptureActiveScroll()
        {
            var p = _tabs != null ? _tabs.SelectedTab as ScrollableControl : null;
            return p != null ? p.AutoScrollPosition : Point.Empty;
        }

        /// <summary>
        /// Restores a scroll position captured by <see cref="CaptureActiveScroll"/>.
        /// Live updates (status text, stats cards, etc.) can make a page jump to the
        /// top; re-asserting the position keeps the user where they were. It's a no-op
        /// when nothing actually moved.
        /// </summary>
        private void RestoreActiveScroll(Point saved)
        {
            var p = _tabs != null ? _tabs.SelectedTab as ScrollableControl : null;
            if (p != null)
            {
                p.AutoScrollPosition = new Point(-saved.X, -saved.Y);
            }
        }

        /// <summary>Re-centres every tab page (used after a full-screen toggle).</summary>
        private void RecenterAllPages()
        {
            if (_tabs == null)
            {
                return;
            }
            foreach (TabPage page in _tabs.TabPages)
            {
                CenterPageContent(page);
            }
        }

        private void UpdateGifAnimationState()
        {
            bool active = WindowState != FormWindowState.Minimized && Visible;
            _header?.SetAnimationActive(active);
            _footerGif?.SetAnimationActive(active);
            if (_tabs != null)
            {
                foreach (TabPage page in _tabs.TabPages)
                {
                    (page as BackdropTabPage)?.SetAnimationActive(active);
                }
            }
        }

        /// <summary>
        /// Activates the full-window backdrop on the currently visible page only and
        /// deactivates it on the rest, so a single animator runs at a time.
        /// </summary>
        private void UpdateBackdropActivePage()
        {
            if (_tabs == null)
            {
                return;
            }
            bool haveBg = _fullBgImage != null;
            foreach (TabPage page in _tabs.TabPages)
            {
                if (page is BackdropTabPage bp)
                {
                    bp.SetActive(haveBg && ReferenceEquals(page, _tabs.SelectedTab));
                }
            }
        }

        private void ApplyBackgroundGif()
        {
            if (_header == null)
            {
                return;
            }

            Image img = LoadGifImage(_settings != null ? _settings.BackgroundGifPath : null);
            _header.SetBackgroundGif(img);

            // Second backdrop: a bottom band that only appears when a GIF is chosen.
            if (_footerGif != null)
            {
                Image img2 = LoadGifImage(_settings != null ? _settings.BackgroundGifPath2 : null);
                _footerGif.SetGif(img2);
                _footerGif.Visible = img2 != null;
            }

            // Full-window backdrop: paint one image across every tab page behind the
            // (opaque) cards so it reads as a wallpaper for the whole GUI.
            Image full = LoadGifImage(_settings != null ? _settings.FullBackgroundGifPath : null);
            Image oldFull = _fullBgImage;
            _fullBgImage = full;
            if (_tabs != null)
            {
                foreach (TabPage page in _tabs.TabPages)
                {
                    if (page is BackdropTabPage bp)
                    {
                        bp.SetBackdrop(full);
                    }
                }
                // Only the visible page animates — activating every page would run six
                // animators on the one shared image at once (wasteful, and repeated
                // ImageAnimator.Animate calls on the same image can play it too fast).
                UpdateBackdropActivePage();
            }

            // Every page has now switched to the new image (each stopped animating the
            // old one), so the previous shared image is safe to release. Without this,
            // repeatedly changing the background would leak GDI+ image handles.
            if (oldFull != null && !ReferenceEquals(oldFull, full))
            {
                try { oldFull.Dispose(); } catch { }
            }
        }

        private static Image LoadGifImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }
            if (!System.IO.File.Exists(path))
            {
                Utils.Logger.Warn("Background image not found (was it moved or deleted?): " + path);
                return null;
            }
            try
            {
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                return Image.FromStream(new System.IO.MemoryStream(bytes));
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("Background image failed to load (" + ex.GetType().Name + "): " + path);
                return null;
            }
        }

        /// <summary>
        /// True if the file at <paramref name="path"/> loads as an image. Used by the
        /// Settings pickers so a corrupt or non-image file is rejected with a message
        /// instead of being saved and silently showing nothing.
        /// </summary>
        private static bool CanLoadImageFile(string path)
        {
            Image probe = LoadGifImage(path);
            if (probe == null)
            {
                return false;
            }
            probe.Dispose();
            return true;
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
                Icon = Utils.AppIcon.Get(),
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
            Point keepScroll = CaptureActiveScroll();
            switch (state)
            {
                case EngineState.Running:
                    _bigStatusLabel.Text = Localization.T("RUNNING");
                    _bigStatusLabel.ForeColor = _theme.Success;
                    _statusState.Text = Localization.T("Running");
                    _stopBtn.Enabled = true;
                    ShowClickingIndicator(true);
                    break;

                case EngineState.Idle:
                    _bigStatusLabel.Text = Localization.T("IDLE");
                    _bigStatusLabel.ForeColor = _theme.TextMuted;
                    _statusState.Text = Localization.T("Idle");
                    _stopBtn.Enabled = false;
                    ShowClickingIndicator(false);
                    break;

                case EngineState.Paused:
                    _bigStatusLabel.Text = Localization.T("PAUSED");
                    _bigStatusLabel.ForeColor = _theme.Warning;
                    _statusState.Text = Localization.T("Paused");
                    _stopBtn.Enabled = true;
                    ShowClickingIndicator(false);
                    break;
            }

            UpdateStartButtonAppearance();
            RefreshStatePill();
            RefreshBusyLock();
            UpdateStatusHint();
            RestoreActiveScroll(keepScroll);
        }

        /// <summary>
        /// Locks the UI so only one operation can run at a time and nothing
        /// conflicting can be triggered mid-run. While clicking, a macro playing, or
        /// recording is active, the other start triggers, profile management and the
        /// CPS test are disabled; only the matching Stop stays available (hotkeys and
        /// the emergency stop always work). Everything re-enables automatically when
        /// the operation ends. This prevents accidentally starting two things at once
        /// or changing profiles in the middle of a run.
        /// </summary>
        private void RefreshBusyLock()
        {
            bool clicking = _engine != null && (_engine.IsRunning || _engine.IsPaused);
            bool playing = _player != null && _player.IsPlaying;
            bool recording = _liveRecording;
            bool busy = clicking || playing || recording;

            void En(Control c, bool enabled) { if (c != null) c.Enabled = enabled; }

            // Profile management and the CPS test are never allowed mid-operation.
            En(_profileCombo, !busy);
            En(_newProfileBtn, !busy);
            En(_saveProfileBtn, !busy);
            En(_duplicateProfileBtn, !busy);
            En(_deleteProfileBtn, !busy);
            En(_cpsTestBtn, !busy);

            // Clicker: the primary button stays available while clicking so it can
            // pause/resume; it's only locked out when a macro is playing or recording
            // (you can't start a click run then). Stop is available while clicking.
            En(_startBtn, clicking || (!playing && !recording));
            En(_stopBtn, clicking);

            // Macros: can only record/play when nothing is running.
            En(_recordBtn, !busy);
            En(_playMacroBtn, !busy);
            En(_playOnceBtn, !busy);
            En(_stopPlayBtn, playing);     // Stop playback only while playing.
            En(_stopRecordBtn, recording); // Stop recording only while recording.
        }

        /// <summary>
        /// Shows or hides the small click-through "clicking" overlay. Honors the
        /// user's setting; never throws if the overlay can't be created.
        /// </summary>
        private void ShowClickingIndicator(bool show)
        {
            try
            {
                bool wanted = show && _settings != null && _settings.ShowClickingIndicator;

                if (!wanted)
                {
                    if (_clickingIndicator != null)
                    {
                        var ind = _clickingIndicator;
                        _clickingIndicator = null;
                        if (!ind.IsDisposed) ind.Close();
                    }
                    return;
                }

                if (_clickingIndicator == null || _clickingIndicator.IsDisposed)
                {
                    _clickingIndicator = new ClickingIndicatorForm(_theme);

                    // Show which hotkey stops clicking (Stop, else Start/Stop, else Emergency).
                    string stopHint = null;
                    var sc = _settings?.HotkeyFor(HotkeyAction.StopClicking);
                    if (sc != null && sc.IsValid)
                    {
                        stopHint = sc.ToDisplayString();
                    }
                    else
                    {
                        var es = _settings?.HotkeyFor(HotkeyAction.EmergencyStop);
                        if (es != null && es.IsValid) stopHint = es.ToDisplayString();
                    }
                    if (!string.IsNullOrEmpty(stopHint))
                    {
                        _clickingIndicator.SetHint("Press " + stopHint + " to stop");
                    }

                    _clickingIndicator.Show();
                }
                else
                {
                    _clickingIndicator.ApplyTheme(_theme);
                }
            }
            catch
            {
                // The overlay is a nicety; never let it break start/stop.
            }
        }

        /// <summary>Shows the "playing macro" overlay (honors the same overlay setting).</summary>
        private void ShowMacroIndicator(string macroName)
        {
            try
            {
                if (_settings == null || !_settings.ShowClickingIndicator) return;
                if (_macroIndicator == null || _macroIndicator.IsDisposed)
                {
                    _macroIndicator = new ClickingIndicatorForm(_theme, "Tempo \u2014 playing macro", _theme.Accent);

                    // Show which hotkey stops playback (Stop-macro, else Emergency-stop).
                    string stopHint = null;
                    var sm = _settings?.HotkeyFor(HotkeyAction.StopMacro);
                    if (sm != null && sm.IsValid)
                    {
                        stopHint = sm.ToDisplayString();
                    }
                    else
                    {
                        var es = _settings?.HotkeyFor(HotkeyAction.EmergencyStop);
                        if (es != null && es.IsValid) stopHint = es.ToDisplayString();
                    }
                    if (!string.IsNullOrEmpty(stopHint))
                    {
                        _macroIndicator.SetHint("Press " + stopHint + " to stop");
                    }

                    _macroIndicator.Show();
                }
                _macroIndicator.SetStatusText(string.IsNullOrEmpty(macroName) ? "playing\u2026" : macroName);
            }
            catch { }
        }

        private void UpdateMacroIndicator(string text)
        {
            if (_macroIndicator != null && !_macroIndicator.IsDisposed)
            {
                _macroIndicator.SetStatusText(text);
            }
        }

        private void HideMacroIndicator()
        {
            try
            {
                if (_macroIndicator != null && !_macroIndicator.IsDisposed)
                {
                    _macroIndicator.Close();
                }
            }
            catch { }
            _macroIndicator = null;
        }

        private void OnEngineRunCompleted()
        {
            // A run is recorded exactly once. On a normal stop this fires via the
            // engine's RunCompleted event; on app-close-while-running we call it
            // directly (see OnFormClosing) because the async event would be dropped
            // during shutdown. The guard stops the two paths double-counting.
            if (_runCompletedHandled)
            {
                return;
            }
            _runCompletedHandled = true;

            // Optional completion notice — only when a finite run (fixed count or
            // duration) ended on its own, never for a manual stop.
            if (_settings != null && _settings.NotifyOnRepeatFinish &&
                _lastRunWasFinite && _engine != null && _engine.LastRunCompletedNaturally)
            {
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                try
                {
                    _trayIcon?.ShowBalloonTip(2000, "Tempo",
                        "Fixed run finished (" + _statistics.SessionClicks.ToString("N0") + " session clicks).",
                        ToolTipIcon.Info);
                }
                catch { }
            }

            // The worker finished — either it was stopped or a fixed repeat count
            // was reached. Reflect the idle state in the UI and persist stats.
            _startBtn.Enabled = true;
            _stopBtn.Enabled = false;
            _bigStatusLabel.Text = Localization.T("IDLE");
            _bigStatusLabel.ForeColor = _theme.TextMuted;
            _statusState.Text = Localization.T("Idle");
            RefreshStatePill();

            // Privacy: when history recording is turned off, a finished run leaves no
            // trace — no history row and no change to the lifetime totals. We also pull
            // this run's clicks out of the persistence baseline so they can never be
            // folded into the lifetime count later, even if recording is re-enabled.
            if (!_settings.RecordSessionHistory)
            {
                _lifetimeBaseline -= (_statistics.TotalClicks - _runStartClicks);
                return;
            }

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

            UpdateStartButtonHotkeyHint();
        }

        /// <summary>
        /// Shows the bound Start/Stop toggle hotkey on the Start button (e.g.
        /// "▶  Start · F6") so the shortcut is discoverable without opening the
        /// Keybinds tab. Falls back to a plain label when nothing is bound.
        /// </summary>
        private void UpdateStartButtonHotkeyHint()
        {
            if (_stopBtn != null)
            {
                HotkeyDefinition toggle = _settings?.HotkeyFor(HotkeyAction.ToggleStartStop);
                if (toggle != null && toggle.IsValid)
                {
                    string hk = toggle.ToDisplayString().Replace(" + ", "+");
                    _stopBtn.Text = "\u25A0  " + Utils.Localization.T("Stop") + "   \u00b7  " + hk;
                }
                else
                {
                    _stopBtn.Text = "\u25A0  " + Utils.Localization.T("Stop");
                }
            }
            UpdateStartButtonAppearance();
        }

        /// <summary>
        /// Keeps the primary button in step with the engine: it reads "Start" when
        /// idle, "Pause" while clicking and "Resume" once paused, so pause/resume is
        /// reachable by mouse and not only via the hotkey. (Whether it's *enabled* is
        /// decided by RefreshBusyLock; this only sets the text and colour.)
        /// </summary>
        private void UpdateStartButtonAppearance()
        {
            if (_startBtn == null)
            {
                return;
            }

            bool paused = _engine != null && _engine.IsPaused;
            bool running = _engine != null && _engine.IsRunning && !paused;

            if (paused)
            {
                _startBtn.Text = "\u25B6  " + Utils.Localization.T("Resume");
                _startBtn.BackColor = _theme.Success;
            }
            else if (running)
            {
                _startBtn.Text = "\u2759\u2759  " + Utils.Localization.T("Pause");
                _startBtn.BackColor = _theme.Warning;
            }
            else
            {
                string baseText = "\u25B6  " + Utils.Localization.T("Start");
                HotkeyDefinition toggle = _settings?.HotkeyFor(HotkeyAction.ToggleStartStop);
                if (toggle != null && toggle.IsValid)
                {
                    _startBtn.Text = baseText + "   \u00b7  " + toggle.ToDisplayString().Replace(" + ", "+");
                }
                else
                {
                    _startBtn.Text = baseText;
                }
                _startBtn.BackColor = _theme.Success;
            }
            _startBtn.ForeColor = Color.White;
        }

        /// <summary>
        /// Primary-button click: start when idle, pause while running, resume when
        /// paused. Pause/resume reuse the engine's existing, tested toggle.
        /// </summary>
        private void OnStartOrPauseClicked()
        {
            if (_engine == null)
            {
                return;
            }
            if (_engine.IsPaused)
            {
                _engine.TogglePause();   // resume
            }
            else if (_engine.IsRunning)
            {
                _engine.TogglePause();   // pause
            }
            else
            {
                BeginStartWithCountdown();
            }
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
            _lastRunWasFinite = profile.RepeatMode != RepeatMode.UntilStopped;
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
                _runCompletedHandled = false;
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

            _statusState.Text = Localization.T("Stopped (emergency)");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Live display refresh
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fills the middle of the status bar with something useful: the start/stop
        /// hotkey and what the clicker will do, or the macro that's playing.
        /// </summary>
        private void UpdateStatusHint()
        {
            if (_statusHint == null)
            {
                return;
            }

            string text;
            try
            {
                if (_player != null && _player.IsPlaying)
                {
                    Macro m = SelectedMacro();
                    text = "Playing macro" + (m != null && !string.IsNullOrEmpty(m.Name) ? ": " + m.Name : "");
                }
                else
                {
                    string hk = "";
                    HotkeyDefinition toggle = _settings != null ? _settings.HotkeyFor(HotkeyAction.ToggleStartStop) : null;
                    if (toggle != null && toggle.IsValid)
                    {
                        hk = toggle.ToDisplayString().Replace(" + ", "+");
                    }

                    if (_engine != null && _engine.IsPaused)
                    {
                        text = hk.Length > 0 ? ("Paused \u2014 " + hk + " to resume") : "Paused";
                    }
                    else if (_engine != null && _engine.IsRunning)
                    {
                        text = hk.Length > 0 ? ("Clicking \u2014 " + hk + " to stop") : "Clicking";
                    }
                    else
                    {
                        string summary = BuildClickerSummary();
                        string start = hk.Length > 0 ? (hk + " to start") : "Ready";
                        text = summary.Length > 0 ? (summary + "    \u00b7    " + start) : start;
                    }
                }
            }
            catch
            {
                text = "";
            }

            if (_statusHint.Text != text)
            {
                _statusHint.Text = text;
            }
        }

        /// <summary>A short "Interval · 10 CPS · Left" summary of the current clicker setup.</summary>
        private string BuildClickerSummary()
        {
            try
            {
                ClickMode mode = GetSelectedMode();
                string modeName = mode == ClickMode.HoldToClick ? "Hold" : mode == ClickMode.Burst ? "Burst" : "Interval";
                string s = modeName;
                if (_speedTrack != null)
                {
                    s += " \u00b7 " + _speedTrack.Value + " CPS";
                }
                if (_buttonCombo != null && _buttonCombo.SelectedItem != null)
                {
                    s += " \u00b7 " + _buttonCombo.SelectedItem;
                }
                return s;
            }
            catch
            {
                return "";
            }
        }

        private void UpdateLiveDisplays()
        {
            // Keep the user where they scrolled: live updates below can otherwise
            // snap the visible page to the top.
            Point keepScroll = CaptureActiveScroll();

            // The status bar is visible on every tab, so always keep it current.
            _statusClicks.Text = Utils.Localization.T("Clicks:") + " " + _statistics.SessionClicks.ToString("N0");
            _statusCps.Text = Utils.Localization.T("CPS:") + $" {_statistics.GetCurrentCps():0.0}";
            if (_statusElapsed != null)
            {
                _statusElapsed.Text = Utils.Localization.T("Time:") + " " + FormatDuration(_statistics.GetElapsed());
            }

            // Prominent live rate next to the big status word, only while clicking.
            if (_liveCpsLabel != null)
            {
                _liveCpsLabel.Text = (_engine != null && _engine.IsRunning && !_engine.IsPaused)
                    ? $"{_statistics.GetCurrentCps():0.0} CPS"
                    : string.Empty;
            }

            // Keep the on-screen running overlay (if shown) in step with the stats.
            if (_clickingIndicator != null && !_clickingIndicator.IsDisposed)
            {
                _clickingIndicator.SetStats(_statistics.SessionClicks, _statistics.GetCurrentCps());
            }

            // The full statistics dashboard (cards, charts, insights) is expensive to
            // recompute, so only do it while the Statistics tab is actually showing.
            // It's also refreshed on tab-switch and whenever a session ends.
            if (_statsPage != null && _tabs != null && _tabs.SelectedTab == _statsPage)
            {
                UpdateStatisticsTab();
            }

            UpdateAntiFreezeStatus();
            UpdateMultiPointLive();
            CheckMilestoneCrossing();
            UpdateStatusHint();

            RestoreActiveScroll(keepScroll);
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
                _antiFreezeStatusLabel.Text = Localization.T("Detection: off — no rate limit");
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

        /// <summary>Builds the active theme, applying the custom accent if enabled.</summary>
        private Theme BuildActiveTheme()
        {
            Theme t = Theme.ForKind(_settings.Theme);
            if (_settings.CustomAccentEnabled)
            {
                t = t.WithAccent(System.Drawing.Color.FromArgb(_settings.CustomAccentArgb));
            }
            return t;
        }

        private void ApplyThemeToEverything()
        {
            _theme = BuildActiveTheme();
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

            if (_sidebar != null)
            {
                _sidebar.BackColor = _theme.Background;
                _sidebar.Invalidate();
            }
            RefreshSidebarSelection();

            if (_header != null)
            {
                _header.ApplyTheme(_theme);
            }
            if (_footerGif != null)
            {
                _footerGif.ApplyTheme(_theme);
            }
            if (_clickingIndicator != null && !_clickingIndicator.IsDisposed)
            {
                _clickingIndicator.ApplyTheme(_theme);
            }
            if (_headerProfile != null)
            {
                _headerProfile.ForeColor = _theme.TextMuted;
            }

            // Pill colour is driven by current engine state; refresh it.
            RefreshStatePill();

            // Theme the statistics dashboard cards + graph.
            ApplyThemeToStatCards();

            // Keep the Settings live preview in sync.
            RefreshThemePreview();

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
                    _statePill.Text = Localization.T("RUNNING");
                    break;
                case EngineState.Paused:
                    _statePill.PillColor = _theme.Warning;
                    _statePill.Text = Localization.T("PAUSED");
                    break;
                case EngineState.Idle:
                default:
                    _statePill.PillColor = _theme.TextMuted;
                    _statePill.Text = Localization.T("IDLE");
                    break;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Window / tray behaviour
        // ─────────────────────────────────────────────────────────────────────

        private void ApplyWindowPreferences()
        {
            ReassertTopMost();

            // Switch to manual positioning up front so the shell's CenterScreen
            // default doesn't fight the saved position. The actual bounds are applied
            // in OnLoad (RestoreWindowBounds), after the form has been DPI-scaled and
            // fully constructed — applying them here in the constructor is unreliable.
            if (_settings.RememberWindowPosition &&
                _settings.WindowLeft >= 0 && _settings.WindowTop >= 0)
            {
                StartPosition = FormStartPosition.Manual;
            }

            // NOTE: "Start minimised to tray" is intentionally handled in the
            // SetVisibleCore override below, not here. Calling BeginInvoke/Hide in
            // the constructor throws because the window handle does not exist yet.
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RestoreWindowBounds();
            ApplyDarkTitleBar();
            // Start hidden so the window can fade in once it's shown, giving a smooth
            // launch (and a smooth hand-off when the app restarts itself).
            try { Opacity = 0; } catch { /* opacity unsupported — ignore */ }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            StartFadeIn();
            if (_settings != null && _settings.CursorTrailEnabled)
            {
                ApplyCursorTrail(true);
            }
        }

        /// <summary>Shows or hides the just-for-fun colourful cursor trail overlay.</summary>
        private void ApplyCursorTrail(bool on)
        {
            try
            {
                if (on)
                {
                    if (_cursorTrail == null || _cursorTrail.IsDisposed)
                    {
                        _cursorTrail = new CursorTrailForm();
                    }
                    _cursorTrail.Begin();
                }
                else if (_cursorTrail != null && !_cursorTrail.IsDisposed)
                {
                    _cursorTrail.End();
                }
            }
            catch
            {
                // Cosmetic only — never let it disrupt the app.
            }
        }

        private System.Windows.Forms.Timer _fadeTimer;

        /// <summary>Fades the window in from transparent to its configured opacity.</summary>
        private void StartFadeIn()
        {
            double target = 1.0;
            if (_settings != null)
            {
                target = _settings.WindowOpacity / 100.0;
            }
            if (target < 0.2) target = 0.2;   // never settle invisible
            if (target > 1.0) target = 1.0;

            try { Opacity = 0; } catch { return; }

            double finalTarget = target;
            _fadeTimer?.Stop();
            _fadeTimer?.Dispose();
            _fadeTimer = new System.Windows.Forms.Timer { Interval = 15 };
            _fadeTimer.Tick += (s, ev) =>
            {
                double next = Opacity + 0.10;
                if (next >= finalTarget)
                {
                    try { Opacity = finalTarget; } catch { }
                    _fadeTimer.Stop();
                    _fadeTimer.Dispose();
                    _fadeTimer = null;
                }
                else
                {
                    try { Opacity = next; } catch { }
                }
            };
            _fadeTimer.Start();
        }

        /// <summary>
        /// Fades the window out and then restarts the app — used for the language
        /// change so the transition between the old and new instance feels smooth
        /// rather than an abrupt flash.
        /// </summary>
        private void FadeOutThenRestart()
        {
            _reallyClosing = true;

            // Cover the window with a brief, centred "Restarting…" message so the
            // restart reads as a deliberate, polished transition rather than a flash.
            try
            {
                var overlay = new Panel
                {
                    Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height),
                    BackColor = _theme != null ? _theme.Background : BackColor,
                    Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
                };
                var label = new Label
                {
                    Text = "Restarting to apply changes\u2026",
                    ForeColor = _theme != null ? _theme.Text : ForeColor,
                    Font = new Font("Segoe UI", 13f, FontStyle.Regular),
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Fill
                };
                overlay.Controls.Add(label);
                Controls.Add(overlay);
                overlay.BringToFront();
                overlay.Refresh();
            }
            catch
            {
                // Non-fatal: if the overlay can't be shown we still fade and restart.
            }

            // Smaller steps on a slightly longer interval give a smoother fade than
            // the previous quick blink.
            var t = new System.Windows.Forms.Timer { Interval = 16 };
            t.Tick += (s, ev) =>
            {
                double next = Opacity - 0.08;
                if (next <= 0)
                {
                    t.Stop();
                    t.Dispose();
                    try { Opacity = 0; } catch { }
                    try { Application.Restart(); }
                    catch
                    {
                        ShowInfo("Tempo couldn't restart automatically. Please reopen it to apply the new language.");
                    }
                }
                else
                {
                    try { Opacity = next; } catch { }
                }
            };
            t.Start();
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>
        /// Asks the desktop window manager to render this window's title bar (and its
        /// native minimize / maximize / close buttons) in dark mode, so the system
        /// chrome matches the dark app instead of showing a bright white bar on top.
        /// No-ops gracefully on Windows versions that don't support it.
        /// </summary>
        private void ApplyDarkTitleBar()
        {
            try
            {
                int on = 1;
                // DWMWA_USE_IMMERSIVE_DARK_MODE is 20 on current Windows 10/11 builds
                // and was 19 on early Windows 10 2004 builds — try both.
                if (DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
                }
            }
            catch { /* not supported on this OS — leave the default title bar */ }
        }

        /// <summary>
        /// Applies the saved window size and position. Runs from OnLoad (after the
        /// form is constructed and DPI-scaled) so the values reliably "stick" — doing
        /// this in the constructor gets overridden by auto-scaling.
        /// </summary>
        private void RestoreWindowBounds()
        {
            if (_settings == null || !_settings.RememberWindowPosition)
            {
                return;
            }

            // Restore the size first (clamped to the minimum so it can't be tiny).
            if (_settings.WindowWidth >= MinimumSize.Width &&
                _settings.WindowHeight >= MinimumSize.Height)
            {
                Size = new Size(_settings.WindowWidth, _settings.WindowHeight);
            }

            // Then the position, clamped to stay on a visible screen.
            if (_settings.WindowLeft >= 0 && _settings.WindowTop >= 0)
            {
                Rectangle vs = SystemInformation.VirtualScreen;
                int left = Math.Min(Math.Max(_settings.WindowLeft, vs.Left), vs.Right - 100);
                int top = Math.Min(Math.Max(_settings.WindowTop, vs.Top), vs.Bottom - 100);
                StartPosition = FormStartPosition.Manual;
                Location = new Point(left, top);
            }
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
            UpdateGifAnimationState();
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
            UpdateGifAnimationState();
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

            // Stop a run in progress and record its session NOW, while the UI thread
            // is still pumping messages. During shutdown the engine's async
            // RunCompleted event would be dropped, losing the final session from the
            // history and lifetime totals.
            if (_engine != null && (_engine.IsRunning || _engine.IsPaused))
            {
                _engine.Stop();
                OnEngineRunCompleted();
            }

            // We're committed to closing now. Stop any active clicking or macro
            // playback cleanly while the process is still alive, so the worker thread
            // finishes its current click (releasing the mouse button) rather than being
            // killed mid-press on exit and leaving a button stuck down.
            bool wasActive = (_engine != null && _engine.IsRunning)
                             || (_player != null && _player.IsPlaying);
            try { _engine?.Stop(); } catch { }
            try { if (_player != null && _player.IsPlaying) _player.Stop(); } catch { }
            try { _cursorTrail?.End(); _cursorTrail?.Dispose(); } catch { }
            if (wasActive)
            {
                // Safety net for hold-clicks / held playback: never exit with a button down.
                try
                {
                    InputSimulator.ButtonUp(MouseButtonType.Left);
                    InputSimulator.ButtonUp(MouseButtonType.Right);
                    InputSimulator.ButtonUp(MouseButtonType.Middle);
                }
                catch { }
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
                _settings.WindowWidth = Size.Width;
                _settings.WindowHeight = Size.Height;
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
                _tips?.Dispose();

                HideRecordingIndicator();

                try
                {
                    if (_clickingIndicator != null && !_clickingIndicator.IsDisposed)
                    {
                        _clickingIndicator.Close();
                    }
                }
                catch { }
                _clickingIndicator = null;

                try
                {
                    if (_macroIndicator != null && !_macroIndicator.IsDisposed)
                    {
                        _macroIndicator.Close();
                    }
                }
                catch { }
                _macroIndicator = null;

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
