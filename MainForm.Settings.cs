using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Persistence;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    public partial class MainForm
    {
        private bool _suppressSettingsEvents;
        private CheckBox _launchStartupCheck;
        private CheckBox _hideWhenClickingCheck;
        private CheckBox _checkUpdatesCheck;
        private CheckBox _writeLogCheck;
        private CheckBox _showIndicatorCheck;
        private CheckBox _minimizeRecordingCheck;
        private CheckBox _recordHistoryCheck;
        private CheckBox _rememberWindowCheck;
        private TrackBar _opacitySlider;
        private Label _opacityValueLabel;
        private Button _headerGifBtn;
        private Button _headerGifClearBtn;
        private Button _footerGifBtn;
        private Button _footerGifClearBtn;
        private Button _fullGifBtn;
        private Button _fullGifClearBtn;

        private void BuildSettingsTab()
        {
            var page = new BackdropTabPage(Utils.Localization.T("Settings")) { AutoScroll = true };

            // ── Appearance ─────────────────────────────────────────────────────
            var appearance = UiFactory.Group("Appearance", 12, 12, 696, 168);
            appearance.Controls.Add(UiFactory.Label("Theme", 16, 32));
            _themeCombo = UiFactory.Combo(120, 29, 150,
                "Dark", "Light", "Midnight", "Ocean", "Forest", "Crimson",
                "Solarized", "AMOLED", "Nord", "Dracula",
                "Monokai", "Gruvbox", "Synthwave", "Coffee",
                "Cosmos", "Rose", "Slate", "Sunset", "Mint", "Sand",
                "Lavender", "Sakura", "Emerald", "Steel", "Grape", "Arctic",
                "Indigo", "Teal", "Tangerine", "Bubblegum", "Carbon", "Honey",
                "Sapphire", "Olive", "Cyan", "Peach", "Wine", "Magenta");
            _themeCombo.SelectedIndexChanged += OnThemeChanged;
            appearance.Controls.Add(_themeCombo);

            appearance.Controls.Add(UiFactory.Label("Language", 290, 32));
            _languageCombo = UiFactory.Combo(356, 29, 140,
                "English", "Español", "Français", "Deutsch", "Italiano", "Português");
            _languageCombo.SelectedIndexChanged += OnLanguageChanged;
            appearance.Controls.Add(_languageCombo);

            _alwaysOnTopCheck = UiFactory.Check("Always on top", 512, 31);
            _alwaysOnTopCheck.CheckedChanged += OnAlwaysOnTopToggled;
            appearance.Controls.Add(_alwaysOnTopCheck);

            // Custom accent colour.
            _customAccentCheck = UiFactory.Check("Use a custom accent colour", 16, 66);
            _customAccentCheck.CheckedChanged += OnCustomAccentToggled;
            appearance.Controls.Add(_customAccentCheck);

            _chooseAccentBtn = UiFactory.Button("Choose colour…", 250, 62, 130, 26);
            _chooseAccentBtn.Click += OnChooseAccentClicked;
            appearance.Controls.Add(_chooseAccentBtn);

            _accentSwatch = new Panel { Left = 390, Top = 63, Width = 44, Height = 22, BorderStyle = BorderStyle.FixedSingle };
            appearance.Controls.Add(_accentSwatch);

            // Live preview.
            appearance.Controls.Add(UiFactory.Label("Preview", 16, 104));
            _previewSwatches = new Panel[6];
            for (int i = 0; i < _previewSwatches.Length; i++)
            {
                _previewSwatches[i] = new Panel
                {
                    Left = 90 + i * 38,
                    Top = 102,
                    Width = 34,
                    Height = 24,
                    BorderStyle = BorderStyle.FixedSingle
                };
                appearance.Controls.Add(_previewSwatches[i]);
            }

            _previewButton = new Button
            {
                Left = 330,
                Top = 100,
                Width = 96,
                Height = 28,
                Text = "Accent",
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            _previewButton.FlatAppearance.BorderSize = 0;
            appearance.Controls.Add(_previewButton);

            _previewSample = new Label
            {
                Left = 438,
                Top = 104,
                Width = 244,
                Height = 22,
                Text = "The quick brown fox  ·  Aa Bb 123",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
            appearance.Controls.Add(_previewSample);

            // Optional animated GIF backdrops (experimental): one behind the header,
            // one along the bottom, and one as a full-window wallpaper behind everything.
            appearance.Controls.Add(UiFactory.Label("Header", 16, 140));
            _headerGifBtn = UiFactory.Button("Choose…", 70, 136, 80, 26);
            _headerGifBtn.Click += OnChooseBackgroundGif;
            appearance.Controls.Add(_headerGifBtn);
            _headerGifClearBtn = UiFactory.Button("Clear", 152, 136, 48, 26);
            _headerGifClearBtn.Click += OnClearBackgroundGif;
            appearance.Controls.Add(_headerGifClearBtn);

            appearance.Controls.Add(UiFactory.Label("Footer", 210, 140));
            _footerGifBtn = UiFactory.Button("Choose…", 262, 136, 80, 26);
            _footerGifBtn.Click += OnChooseBackgroundGif2;
            appearance.Controls.Add(_footerGifBtn);
            _footerGifClearBtn = UiFactory.Button("Clear", 344, 136, 48, 26);
            _footerGifClearBtn.Click += OnClearBackgroundGif2;
            appearance.Controls.Add(_footerGifClearBtn);

            appearance.Controls.Add(UiFactory.Label("Window", 402, 140));
            _fullGifBtn = UiFactory.Button("Choose…", 462, 136, 80, 26);
            _fullGifBtn.Click += OnChooseFullGif;
            appearance.Controls.Add(_fullGifBtn);
            _fullGifClearBtn = UiFactory.Button("Clear", 544, 136, 48, 26);
            _fullGifClearBtn.Click += OnClearFullGif;
            appearance.Controls.Add(_fullGifClearBtn);

            var bgGifNote = UiFactory.Caption("(experimental)", 600, 140);
            bgGifNote.ForeColor = _theme.TextMuted;
            appearance.Controls.Add(bgGifNote);

            // ── Startup & Window ───────────────────────────────────────────────
            var startup = UiFactory.Group("Startup & Window", 12, 192, 696, 86);

            _launchStartupCheck = UiFactory.Check("Launch Tempo when I sign in to Windows", 16, 30);
            _hideWhenClickingCheck = UiFactory.Check("Hide window to tray when clicking starts", 16, 58);
            startup.Controls.Add(_launchStartupCheck);
            startup.Controls.Add(_hideWhenClickingCheck);

            var kbNote = UiFactory.Caption("Hotkeys are configured on the Keybinds tab.", 360, 32);
            kbNote.ForeColor = _theme.TextMuted;
            startup.Controls.Add(kbNote);

            _rememberWindowCheck = UiFactory.Check("Remember window position & size", 360, 56);
            startup.Controls.Add(_rememberWindowCheck);

            // ── Behaviour ──────────────────────────────────────────────────────
            var behaviour = UiFactory.Group("Behaviour", 12, 288, 696, 184);

            _minimizeToTrayCheck = UiFactory.Check("Minimise to tray instead of closing", 16, 30);
            _startMinimizedCheck = UiFactory.Check("Start minimised to tray", 16, 58);
            _trayNotifyCheck = UiFactory.Check("Show tray notifications", 16, 86);
            _confirmExitCheck = UiFactory.Check("Confirm before exit while running", 360, 30);
            _safetyEscapeCheck = UiFactory.Check("Allow Escape key as emergency stop", 360, 58);

            behaviour.Controls.Add(_minimizeToTrayCheck);
            behaviour.Controls.Add(_startMinimizedCheck);
            behaviour.Controls.Add(_trayNotifyCheck);
            behaviour.Controls.Add(_confirmExitCheck);
            behaviour.Controls.Add(_safetyEscapeCheck);

            behaviour.Controls.Add(UiFactory.Label("Start delay before clicking (s):", 360, 90));
            _startDelayNum = UiFactory.Numeric(560, 86, 70, 0, 60, 0);
            behaviour.Controls.Add(_startDelayNum);

            _checkUpdatesCheck = UiFactory.Check("Check for updates when Tempo starts", 16, 114);
            behaviour.Controls.Add(_checkUpdatesCheck);

            _writeLogCheck = UiFactory.Check("Write a log file to disk", 360, 118);
            behaviour.Controls.Add(_writeLogCheck);

            _recordHistoryCheck = UiFactory.Check("Record session history and statistics", 16, 138);
            behaviour.Controls.Add(_recordHistoryCheck);

            _showIndicatorCheck = UiFactory.Check("Show on-screen overlay while running", 360, 138);
            behaviour.Controls.Add(_showIndicatorCheck);

            _minimizeRecordingCheck = UiFactory.Check("Minimise window during macro record & playback", 16, 158);
            behaviour.Controls.Add(_minimizeRecordingCheck);

            // ── Data ───────────────────────────────────────────────────────────
            var data = UiFactory.Group("Data & Backup", 12, 484, 696, 128);

            var openFolderBtn = UiFactory.Button("Open data folder", 16, 30, 150, 30);
            openFolderBtn.Click += OnOpenDataFolder;
            data.Controls.Add(openFolderBtn);

            var exportBtn = UiFactory.Button("Export settings…", 176, 30, 150, 30);
            exportBtn.Click += OnExportSettings;
            data.Controls.Add(exportBtn);

            var importBtn = UiFactory.Button("Import settings…", 336, 30, 150, 30);
            importBtn.Click += OnImportSettings;
            data.Controls.Add(importBtn);

            var openLogBtn = UiFactory.Button("Open log file", 496, 30, 150, 30);
            openLogBtn.Click += OnOpenLogFile;
            data.Controls.Add(openLogBtn);

            var pathLabel = UiFactory.Caption(SettingsManager.GetSettingsDirectory(), 16, 70);
            pathLabel.ForeColor = _theme.TextMuted;
            pathLabel.AutoSize = false;
            pathLabel.Width = 664;
            pathLabel.Height = 16;
            data.Controls.Add(pathLabel);

            var uninstallBtn = UiFactory.Button("Uninstall Tempo…", 16, 92, 170, 28);
            uninstallBtn.Click += OnUninstallClicked;
            data.Controls.Add(uninstallBtn);

            var reportBugBtn = UiFactory.Button("Report a bug…", 196, 92, 150, 28);
            reportBugBtn.Click += OnReportBug;
            data.Controls.Add(reportBugBtn);

            var emailBugBtn = UiFactory.Button("Email a bug…", 356, 92, 150, 28);
            emailBugBtn.Click += OnEmailBug;
            data.Controls.Add(emailBugBtn);

            var backupBtn = UiFactory.Button("Back up all data…", 510, 92, 170, 28);
            backupBtn.Click += OnBackupAllData;
            data.Controls.Add(backupBtn);

            // ── Window & Display ───────────────────────────────────────────────
            var windowGroup = UiFactory.Group("Window & Display", 12, 628, 696, 88);

            windowGroup.Controls.Add(UiFactory.Label("Window opacity", 16, 34));
            _opacitySlider = new TrackBar
            {
                Left = 150, Top = 28, Width = 300, Height = 36,
                Minimum = 50, Maximum = 100, TickFrequency = 10,
                SmallChange = 1, LargeChange = 5, Value = 100
            };
            _opacitySlider.ValueChanged += OnOpacityChanged;
            windowGroup.Controls.Add(_opacitySlider);

            _opacityValueLabel = UiFactory.Label("100%", 460, 34);
            _opacityValueLabel.AutoSize = false;
            _opacityValueLabel.Width = 60;
            windowGroup.Controls.Add(_opacityValueLabel);

            var resetPosBtn = UiFactory.Button("Reset window position", 540, 28, 140, 30);
            resetPosBtn.Click += OnResetWindowPosition;
            windowGroup.Controls.Add(resetPosBtn);

            // ── Buttons ────────────────────────────────────────────────────────
            _saveSettingsBtn = UiFactory.PrimaryButton("Save Settings", 12, 724, 150, 36, _theme);
            _saveSettingsBtn.Click += OnSaveSettings;

            var aboutBtn = UiFactory.Button("About…", 172, 724, 120, 36);
            aboutBtn.Click += (s, e) => OpenAbout();

            var checkUpdatesBtn = UiFactory.Button("Check for updates", 302, 724, 160, 36);
            checkUpdatesBtn.Click += OnCheckForUpdatesClicked;

            var resetBtn = UiFactory.Button("Reset to defaults", 560, 724, 148, 36);
            resetBtn.Click += OnResetSettings;

            page.Controls.Add(appearance);
            page.Controls.Add(startup);
            page.Controls.Add(behaviour);
            page.Controls.Add(data);
            page.Controls.Add(windowGroup);
            page.Controls.Add(_saveSettingsBtn);
            page.Controls.Add(aboutBtn);
            page.Controls.Add(checkUpdatesBtn);
            page.Controls.Add(resetBtn);

            _lastCheckedLabel = UiFactory.Caption("", 12, 768);
            _lastCheckedLabel.AutoSize = false;
            _lastCheckedLabel.Width = 696;
            _lastCheckedLabel.Height = 18;
            page.Controls.Add(_lastCheckedLabel);
            UpdateLastCheckedLabel();

            var privacyNote = UiFactory.Caption(
                "Privacy: Tempo runs entirely on your PC. Your clicks, macros, profiles and " +
                "statistics never leave your computer. The only network use is the optional " +
                "update check (GitHub), which you can turn off under Behaviour.",
                12, 792);
            privacyNote.ForeColor = _theme.TextMuted;
            privacyNote.AutoSize = false;
            privacyNote.Width = 700;
            privacyNote.Height = 48;
            page.Controls.Add(privacyNote);

            var asmVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            string verText = asmVer != null
                ? $"Tempo v{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}"
                : "Tempo";
            var versionLabel = UiFactory.Caption(verText, 12, 724);
            versionLabel.ForeColor = _theme.TextMuted;
            page.Controls.Add(versionLabel);

            _tabs.TabPages.Add(page);
        }

        private void LoadSettingsIntoUi()
        {
            _suppressSettingsEvents = true;
            try
            {
                int themeIndex = (int)_settings.Theme;
                if (themeIndex < 0 || themeIndex >= _themeCombo.Items.Count)
                {
                    themeIndex = 0;
                }
                _themeCombo.SelectedIndex = themeIndex;
                _alwaysOnTopCheck.Checked = _settings.AlwaysOnTop;
                _customAccentCheck.Checked = _settings.CustomAccentEnabled;

                int langIndex = (int)_settings.Language;
                if (langIndex < 0 || langIndex >= _languageCombo.Items.Count) langIndex = 0;
                _languageCombo.SelectedIndex = langIndex;

                _minimizeToTrayCheck.Checked = _settings.MinimizeToTrayOnClose;
                _startMinimizedCheck.Checked = _settings.StartMinimizedToTray;
                _trayNotifyCheck.Checked = _settings.ShowTrayNotifications;
                _confirmExitCheck.Checked = _settings.ConfirmBeforeExitWhileRunning;
                _safetyEscapeCheck.Checked = _settings.SafetyStopOnEscape;
                _launchStartupCheck.Checked = _settings.LaunchAtStartup;
                _hideWhenClickingCheck.Checked = _settings.HideWhenClicking;
                _checkUpdatesCheck.Checked = _settings.CheckForUpdatesOnLaunch;
                _writeLogCheck.Checked = _settings.WriteLogFile;
                _recordHistoryCheck.Checked = _settings.RecordSessionHistory;
                _showIndicatorCheck.Checked = _settings.ShowClickingIndicator;
                _minimizeRecordingCheck.Checked = _settings.MinimizeWhileRecording;
                _rememberWindowCheck.Checked = _settings.RememberWindowPosition;

                if (_unlockSpeedCheck != null && _speedTrack != null)
                {
                    bool unlocked = _settings.AdvancedUnlockSpeed;
                    _unlockSpeedCheck.Checked = unlocked;
                    int max = unlocked ? 1000 : 200;
                    if (_speedTrack.Value > max) _speedTrack.Value = max;
                    _speedTrack.Maximum = max;
                    _speedTrack.TickFrequency = unlocked ? 100 : 20;
                    _speedTrack.LargeChange = unlocked ? 25 : 5;
                }

                int op = _settings.WindowOpacity;
                if (op < 50) op = 50;
                if (op > 100) op = 100;
                _opacitySlider.Value = op;
                _opacityValueLabel.Text = op + "%";

                int startDelay = _settings.ClickerStartDelaySeconds;
                if (startDelay < 0) startDelay = 0;
                if (startDelay > 60) startDelay = 60;
                _startDelayNum.Value = startDelay;
            }
            finally
            {
                _suppressSettingsEvents = false;
            }

            UpdateAccentControlsEnabled();
            RefreshThemePreview();
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (_suppressSettingsEvents)
            {
                return;
            }

            // Live-preview the theme as soon as it is chosen.
            _settings.Theme = (ThemeKind)_themeCombo.SelectedIndex;
            ApplyThemeToEverything();
            RefreshThemePreview();
        }

        private void OnAlwaysOnTopToggled(object sender, EventArgs e)
        {
            if (_suppressSettingsEvents)
            {
                return;
            }

            _settings.AlwaysOnTop = _alwaysOnTopCheck.Checked;
            if (_trayAlwaysOnTopItem != null)
            {
                _trayAlwaysOnTopItem.Checked = _settings.AlwaysOnTop;
            }
            ReassertTopMost();
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (_suppressSettingsEvents)
            {
                return;
            }

            var lang = (Language)_languageCombo.SelectedIndex;
            if (lang == _settings.Language)
            {
                return;
            }

            _settings.Language = lang;
            SettingsManager.Save(_settings);

            DialogResult restart = MessageBox.Show(this,
                "The language has been saved.\n\n" +
                "Tempo needs to restart to apply it everywhere. Restart now?",
                "Language changed",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (restart == DialogResult.Yes)
            {
                try
                {
                    _reallyClosing = true;
                    FadeOutThenRestart();
                }
                catch
                {
                    // If restart fails, the change still applies next launch.
                    ShowInfo("Tempo couldn't restart automatically. Please reopen it to apply the new language.");
                }
            }
        }

        private void OnOpacityChanged(object sender, EventArgs e)
        {
            int v = _opacitySlider.Value;
            _opacityValueLabel.Text = v + "%";
            // Live preview; the value is persisted when Save Settings is pressed.
            try { Opacity = v / 100.0; } catch { }
        }

        private void OnResetWindowPosition(object sender, EventArgs e)
        {
            try
            {
                if (_isFullScreen)
                {
                    ToggleFullScreen();
                }
                WindowState = FormWindowState.Normal;
                Size = new Size(800, 824);

                Rectangle wa = Screen.FromControl(this).WorkingArea;
                Location = new Point(
                    wa.X + (wa.Width - Width) / 2,
                    wa.Y + (wa.Height - Height) / 2);

                // Forget any stored position so it stays centred next launch too.
                _settings.WindowLeft = -1;
                _settings.WindowTop = -1;
                _settings.WindowWidth = -1;
                _settings.WindowHeight = -1;
                ShowInfo("Window position reset.");
            }
            catch { }
        }

        private void OnCustomAccentToggled(object sender, EventArgs e)
        {
            if (_suppressSettingsEvents)
            {
                return;
            }

            _settings.CustomAccentEnabled = _customAccentCheck.Checked;
            UpdateAccentControlsEnabled();
            ApplyThemeToEverything();
            RefreshThemePreview();
        }

        private void OnChooseAccentClicked(object sender, EventArgs e)
        {
            using (var dlg = new ColorDialog
            {
                Color = Color.FromArgb(_settings.CustomAccentArgb),
                FullOpen = true,
                AnyColor = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                _settings.CustomAccentArgb = dlg.Color.ToArgb();

                // Choosing a colour implies the user wants it on.
                if (!_customAccentCheck.Checked)
                {
                    _customAccentCheck.Checked = true; // fires OnCustomAccentToggled
                }
                else
                {
                    _settings.CustomAccentEnabled = true;
                    ApplyThemeToEverything();
                    RefreshThemePreview();
                }
            }
        }

        private void UpdateAccentControlsEnabled()
        {
            if (_chooseAccentBtn != null)
            {
                _chooseAccentBtn.Enabled = _customAccentCheck.Checked;
            }
        }

        /// <summary>Repaints the live theme-preview swatches and sample controls.</summary>
        private void RefreshThemePreview()
        {
            if (_previewSwatches == null)
            {
                return;
            }

            Color[] palette =
            {
                _theme.Background, _theme.Surface, _theme.Surface2,
                _theme.Accent, _theme.Success, _theme.Danger
            };
            for (int i = 0; i < _previewSwatches.Length && i < palette.Length; i++)
            {
                _previewSwatches[i].BackColor = palette[i];
            }

            if (_accentSwatch != null)
            {
                _accentSwatch.BackColor = _settings.CustomAccentEnabled
                    ? Color.FromArgb(_settings.CustomAccentArgb)
                    : _theme.Accent;
            }

            if (_previewButton != null)
            {
                _previewButton.BackColor = _theme.Accent;
                _previewButton.ForeColor = Color.White;
                _previewButton.FlatAppearance.BorderSize = 0;
            }

            if (_previewSample != null)
            {
                _previewSample.BackColor = _theme.Surface;
                _previewSample.ForeColor = _theme.Text;
            }
        }

        private void OnSaveSettings(object sender, EventArgs e)
        {
            _settings.Theme = (ThemeKind)_themeCombo.SelectedIndex;
            _settings.AlwaysOnTop = _alwaysOnTopCheck.Checked;
            _settings.CustomAccentEnabled = _customAccentCheck.Checked;
            _settings.Language = (Language)_languageCombo.SelectedIndex;

            _settings.MinimizeToTrayOnClose = _minimizeToTrayCheck.Checked;
            _settings.StartMinimizedToTray = _startMinimizedCheck.Checked;
            _settings.ShowTrayNotifications = _trayNotifyCheck.Checked;
            _settings.ConfirmBeforeExitWhileRunning = _confirmExitCheck.Checked;
            _settings.SafetyStopOnEscape = _safetyEscapeCheck.Checked;
            _settings.ClickerStartDelaySeconds = (int)_startDelayNum.Value;
            _settings.LaunchAtStartup = _launchStartupCheck.Checked;
            _settings.HideWhenClicking = _hideWhenClickingCheck.Checked;
            _settings.CheckForUpdatesOnLaunch = _checkUpdatesCheck.Checked;
            _settings.WriteLogFile = _writeLogCheck.Checked;
            _settings.RecordSessionHistory = _recordHistoryCheck.Checked;
            _settings.ShowClickingIndicator = _showIndicatorCheck.Checked;
            _settings.MinimizeWhileRecording = _minimizeRecordingCheck.Checked;
            _settings.RememberWindowPosition = _rememberWindowCheck.Checked;
            _settings.WindowOpacity = _opacitySlider.Value;
            if (_unlockSpeedCheck != null) _settings.AdvancedUnlockSpeed = _unlockSpeedCheck.Checked;
            Logger.Enabled = _settings.WriteLogFile;

            // Reflect the overlay preference immediately if a run is in progress.
            ShowClickingIndicator(_engine != null && _engine.IsRunning);

            // Sync the Windows startup entry with the chosen setting, and tell the
            // user if Windows blocked the registry write (otherwise it fails silently
            // and they think startup is on when it isn't).
            bool startupOk = StartupManager.SetEnabled(_settings.LaunchAtStartup);
            if (_settings.LaunchAtStartup && !startupOk)
            {
                ShowWarning(
                    "Tempo couldn't add itself to Windows startup — the registry write was " +
                    "blocked (this can happen on locked-down or work PCs). Startup launch is " +
                    "not active. You can try again, or add Tempo to startup manually.");
            }

            _settings.EnsureConsistency();
            SettingsManager.Save(_settings);

            // Re-apply everything that depends on settings.
            ReassertTopMost();
            if (_trayAlwaysOnTopItem != null)
            {
                _trayAlwaysOnTopItem.Checked = _settings.AlwaysOnTop;
            }
            ApplyThemeToEverything();
            ApplyHotkeysFromSettings();

            ShowInfo("Settings saved.");
        }

        private void OnChooseBackgroundGif(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Choose a background image (GIF animates)",
                Filter = "Images (*.gif;*.png;*.jpg;*.jpeg)|*.gif;*.png;*.jpg;*.jpeg|All files (*.*)|*.*"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                if (!CanLoadImageFile(dlg.FileName))
                {
                    ShowInfo("That file couldn't be loaded as an image, so it wasn't applied.\n\n" + dlg.FileName);
                    return;
                }
                _settings.BackgroundGifPath = dlg.FileName;
                try { Persistence.SettingsManager.Save(_settings); } catch { }
                ApplyBackgroundGif();
            }
        }

        private void OnClearBackgroundGif(object sender, EventArgs e)
        {
            _settings.BackgroundGifPath = "";
            try { Persistence.SettingsManager.Save(_settings); } catch { }
            ApplyBackgroundGif();
        }

        private void OnChooseBackgroundGif2(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Choose a second background image (GIF animates)",
                Filter = "Images (*.gif;*.png;*.jpg;*.jpeg)|*.gif;*.png;*.jpg;*.jpeg|All files (*.*)|*.*"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                if (!CanLoadImageFile(dlg.FileName))
                {
                    ShowInfo("That file couldn't be loaded as an image, so it wasn't applied.\n\n" + dlg.FileName);
                    return;
                }
                _settings.BackgroundGifPath2 = dlg.FileName;
                try { Persistence.SettingsManager.Save(_settings); } catch { }
                ApplyBackgroundGif();
            }
        }

        private void OnClearBackgroundGif2(object sender, EventArgs e)
        {
            _settings.BackgroundGifPath2 = "";
            try { Persistence.SettingsManager.Save(_settings); } catch { }
            ApplyBackgroundGif();
        }

        private void OnChooseFullGif(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Choose a full-window background image (GIF animates)",
                Filter = "Images (*.gif;*.png;*.jpg;*.jpeg)|*.gif;*.png;*.jpg;*.jpeg|All files (*.*)|*.*"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                if (!CanLoadImageFile(dlg.FileName))
                {
                    ShowInfo("That file couldn't be loaded as an image, so it wasn't applied.\n\n" + dlg.FileName);
                    return;
                }
                _settings.FullBackgroundGifPath = dlg.FileName;
                try { Persistence.SettingsManager.Save(_settings); } catch { }
                ApplyBackgroundGif();
            }
        }

        private void OnClearFullGif(object sender, EventArgs e)
        {
            _settings.FullBackgroundGifPath = "";
            try { Persistence.SettingsManager.Save(_settings); } catch { }
            ApplyBackgroundGif();
        }

        private void OnResetSettings(object sender, EventArgs e)
        {
            var confirm = MessageBox.Show(this,
                "Reset all settings to their defaults?",
                "Tempo",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            _settings = AppSettings.CreateDefault();
            _settings.EnsureConsistency();
            _lifetimeBaseline = _settings.LifetimeClicks;
            SettingsManager.Save(_settings);
            LoadSettingsIntoUi();
            LoadKeybindsIntoUi();
            ApplyThemeToEverything();
            ApplyHotkeysFromSettings();
            ReassertTopMost();
            ApplyBackgroundGif();
            if (_trayAlwaysOnTopItem != null)
            {
                _trayAlwaysOnTopItem.Checked = _settings.AlwaysOnTop;
            }
        }

        private void UpdateLastCheckedLabel()
        {
            if (_lastCheckedLabel == null)
            {
                return;
            }

            string text;
            if (_settings == null || _settings.LastUpdateCheckUtc == null)
            {
                text = "Updates haven't been checked yet.";
            }
            else
            {
                text = "Last checked for updates: " +
                       _settings.LastUpdateCheckUtc.Value.ToLocalTime().ToString("d MMM yyyy, HH:mm");
            }

            if (_settings != null && !string.IsNullOrWhiteSpace(_settings.SkippedUpdateVersion))
            {
                text += "   ·   Skipping version " + _settings.SkippedUpdateVersion;
            }

            _lastCheckedLabel.Text = text;
        }

        private void OnCheckForUpdatesClicked(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
            {
                btn.Enabled = false;
                btn.Text = Localization.T("Checking…");
            }

            // Guard so the result is presented exactly once — whichever happens first,
            // the network call finishing or the safety timeout firing.
            bool handled = false;
            object gate = new object();

            Action<UpdateChecker.UpdateResult, bool> finish = (result, timedOut) =>
            {
                lock (gate)
                {
                    if (handled) return;
                    handled = true;
                }
                UiInvoke(() =>
                {
                    if (btn != null)
                    {
                        btn.Enabled = true;
                        btn.Text = Localization.T("Check for updates");
                    }
                    if (timedOut)
                    {
                        ShowWarning("The update check took too long and was cancelled. " +
                                    "Please check your connection and try again.");
                        return;
                    }
                    if (result != null && result.Success)
                    {
                        _settings.LastUpdateCheckUtc = DateTime.UtcNow;
                        SettingsManager.Save(_settings);
                        UpdateLastCheckedLabel();
                    }
                    PresentUpdateResult(result, announceUpToDate: true);
                });
            };

            System.Threading.Tasks.Task.Run(() => UpdateChecker.Check())
                .ContinueWith(t =>
                {
                    UpdateChecker.UpdateResult r =
                        t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? t.Result : null;
                    finish(r, false);
                });

            // Hard safety net: the button can never stay stuck on "Checking…".
            System.Threading.Tasks.Task.Delay(12000)
                .ContinueWith(_ => finish(null, true));
        }

        /// <summary>
        /// Shows the outcome of an update check. When <paramref name="announceUpToDate"/>
        /// is false (the silent launch check), only a found update is reported.
        /// </summary>
        private void PresentUpdateResult(UpdateChecker.UpdateResult result, bool announceUpToDate)
        {
            if (result == null)
            {
                return;
            }

            if (!result.Success)
            {
                if (announceUpToDate)
                {
                    ShowWarning(result.Error ?? "The update check failed.");
                }
                return;
            }

            if (!result.UpdateAvailable)
            {
                if (announceUpToDate)
                {
                    ShowInfo($"You're up to date.\n\nTempo {UpdateChecker.CurrentVersion} is the latest version.");
                }
                return;
            }

            string notes = string.IsNullOrWhiteSpace(result.Notes) ? "" : result.Notes;

            bool canAutoInstall =
                !string.IsNullOrWhiteSpace(result.DownloadUrl) &&
                result.DownloadUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                UpdateInstaller.IsExeDirWritable();

            using (var dlg = new UpdatePromptForm(_theme, UpdateChecker.CurrentVersion?.ToString(),
                result.LatestVersion?.ToString(), notes, canAutoInstall, result.ReleaseDate))
            {
                dlg.ShowDialog(this);

                switch (dlg.Choice)
                {
                    case UpdatePromptForm.UpdateChoice.UpdateNow:
                        RunSelfUpdate(result);
                        break;

                    case UpdatePromptForm.UpdateChoice.OpenPage:
                        OpenDownloadPage(string.IsNullOrWhiteSpace(result.DownloadUrl)
                            ? UpdateChecker.ReleasesPageUrl
                            : result.DownloadUrl);
                        break;

                    case UpdatePromptForm.UpdateChoice.Skip:
                        _settings.SkippedUpdateVersion = result.LatestVersion?.ToString() ?? "";
                        SettingsManager.Save(_settings);
                        UpdateLastCheckedLabel();
                        break;

                    // Later: do nothing.
                }
            }
        }

        /// <summary>
        /// Downloads the new build (with a progress dialog) and, on success, hands
        /// off to the swap helper and exits so the running exe can be replaced.
        /// </summary>
        private void RunSelfUpdate(UpdateChecker.UpdateResult result)
        {
            string dest = UpdateInstaller.GetDownloadTargetPath(result.LatestVersion);

            DialogResult dr;
            string downloadedPath;
            string downloadError;
            using (var dlg = new UpdateDownloadForm(_theme, result.DownloadUrl, dest, result.LatestVersion, result.Sha256Url))
            {
                dr = dlg.ShowDialog(this);
                downloadedPath = dlg.DownloadedPath;
                downloadError = dlg.Error;
            }

            if (dr == DialogResult.Cancel)
            {
                return; // User cancelled the download.
            }

            if (dr != DialogResult.OK || string.IsNullOrEmpty(downloadedPath))
            {
                DialogResult fallback = MessageBox.Show(this,
                    (downloadError ?? "The download failed.") + "\n\nOpen the download page instead?",
                    "Update", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (fallback == DialogResult.Yes)
                {
                    OpenDownloadPage(result.DownloadUrl);
                }
                return;
            }

            // Stop clicking so nothing is mid-run when we hand off and exit.
            try
            {
                if (_engine != null && _engine.IsRunning)
                {
                    _engine.Stop();
                }
            }
            catch { /* best effort */ }

            if (UpdateInstaller.LaunchSwapAndExitHelper(downloadedPath, out string err))
            {
                // Remove the tray icon so it doesn't linger after we force-exit.
                try { _trayIcon?.Dispose(); } catch { }
                Logger.Info("Exiting to let the updater replace Tempo.exe.");
                Environment.Exit(0);
            }
            else
            {
                ShowWarning(err ?? "Couldn't start the updater. The new version was downloaded to:\n" + downloadedPath);
            }
        }

        private void OpenDownloadPage(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                url = UpdateChecker.ReleasesPageUrl;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowWarning("Couldn't open the download page: " + ex.Message);
            }
        }

        /// <summary>Copies the entire data folder (profiles, macros, settings,
        /// history) into a timestamped sub-folder of <paramref name="destRoot"/>.
        /// On success, <paramref name="result"/> is the backup path; on failure it is
        /// an error message.</summary>
        private bool BackupAllData(string destRoot, out string result)
        {
            result = null;
            try
            {
                string src = SettingsManager.GetSettingsDirectory();
                if (!Directory.Exists(src))
                {
                    result = "There is no data folder to back up yet.";
                    return false;
                }

                string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                string dest = Path.Combine(destRoot, "Tempo-backup-" + stamp);
                Directory.CreateDirectory(dest);

                foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                {
                    string rel = file.Substring(src.Length).TrimStart('\\', '/');
                    string target = Path.Combine(dest, rel);
                    string targetDir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }
                    File.Copy(file, target, true);
                }

                result = dest;
                return true;
            }
            catch (Exception ex)
            {
                result = ex.Message;
                return false;
            }
        }

        /// <summary>Asks where to save, then backs up all data. Returns true only if a
        /// backup was actually written.</summary>
        private bool PromptAndBackupAllData()
        {
            using (var dlg = new FolderBrowserDialog
            {
                Description = "Choose a folder to save your Tempo backup in"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return false;
                }

                if (BackupAllData(dlg.SelectedPath, out string res))
                {
                    ShowInfo("Backup saved to:\n" + res);
                    return true;
                }

                ShowWarning("Backup failed: " + res);
                return false;
            }
        }

        private void OnBackupAllData(object sender, EventArgs e)
        {
            // Make sure what's in memory is on disk before we copy the folder.
            try { CaptureSettingsFromUi(); SettingsManager.Save(_settings); } catch { }
            PromptAndBackupAllData();
        }

        private void OnOpenDataFolder(object sender, EventArgs e)
        {
            try
            {
                string dir = SettingsManager.GetSettingsDirectory();
                Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowWarning("Could not open the data folder: " + ex.Message);
            }
        }

        private void OnEmailBug(object sender, EventArgs e)
        {
            EmailReportChannel choice;
            using (var dlg = new EmailReportChooserForm(_theme))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                choice = dlg.Choice;
            }

            switch (choice)
            {
                case EmailReportChannel.EmailApp:
                    OpenExternal(CrashReporter.BuildBlankMailtoUrl(), "email app");
                    break;
                case EmailReportChannel.Gmail:
                    OpenExternal(CrashReporter.BuildGmailComposeUrl(), "browser");
                    break;
                case EmailReportChannel.Outlook:
                    OpenExternal(CrashReporter.BuildOutlookComposeUrl(), "browser");
                    break;
                case EmailReportChannel.Yahoo:
                    OpenExternal(CrashReporter.BuildYahooComposeUrl(), "browser");
                    break;
                case EmailReportChannel.Copy:
                    try
                    {
                        Clipboard.SetText(CrashReporter.BuildBlankReportText());
                        ShowInfo("Bug report copied to your clipboard.\n\nPaste it into an email to " +
                                 CrashReporter.SupportEmail + " (or anywhere else) and add what happened.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("Clipboard copy failed: " + ex.Message);
                        ShowInfo("Couldn't copy to the clipboard. You can email bug reports to:\n\n" +
                                 CrashReporter.SupportEmail);
                    }
                    break;
            }
        }

        /// <summary>Opens a URL (mailto, web compose, etc.) with the user's default handler.</summary>
        private void OpenExternal(string url, string what)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not open " + what + ": " + ex.Message);
                ShowInfo("Couldn't open your " + what + ". You can email bug reports to:\n\n" +
                         CrashReporter.SupportEmail);
            }
        }

        private void OnReportBug(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = CrashReporter.BuildBlankIssueUrl(),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not open the bug-report page: " + ex.Message);
                ShowInfo("Couldn't open your browser. You can report bugs at:\n\n" +
                         "https://github.com/" + CrashReporter.Repository + "/issues");
            }
        }

        private void OnOpenLogFile(object sender, EventArgs e)
        {
            try
            {
                string path = Logger.GetLogPath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    ShowInfo("No log file has been created yet.");
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowWarning("Could not open the log file: " + ex.Message);
            }
        }

        private void OnUninstallClicked(object sender, EventArgs e)
        {
            string dataFolder;
            try { dataFolder = SettingsManager.GetSettingsDirectory(); }
            catch { dataFolder = "%LocalAppData%\\AutoClicker"; }

            DialogResult confirm = MessageBox.Show(this,
                "Uninstall Tempo?\n\n" +
                "This will permanently remove:\n" +
                "   •  All profiles and saved macros\n" +
                "   •  Your settings and session history\n" +
                "   •  The log file\n" +
                "   •  The Windows start-up entry (if set)\n\n" +
                "All of that lives in:\n" +
                dataFolder + "\n\n" +
                "This cannot be undone. Continue?",
                "Uninstall Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            // Offer a full backup (profiles, macros, settings, history) before deleting.
            DialogResult backup = MessageBox.Show(this,
                "Back up all your data first?\n\n" +
                "Yes — choose a folder; Tempo copies all profiles, macros, settings\n" +
                "         and history there before uninstalling\n" +
                "No — uninstall without a backup",
                "Uninstall Tempo", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);

            if (backup == DialogResult.Cancel)
            {
                return;
            }
            if (backup == DialogResult.Yes)
            {
                // If the backup is cancelled or fails, abort so data isn't lost.
                if (!PromptAndBackupAllData())
                {
                    return;
                }
            }

            // Offer to also remove the program file itself.
            DialogResult alsoExe = MessageBox.Show(this,
                "Also delete the Tempo program file itself?\n\n" +
                "Yes — remove everything, including Tempo.exe\n" +
                "No — remove data only and keep Tempo.exe\n\n" +
                "Tempo will close to finish removing files.",
                "Uninstall Tempo", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (alsoExe == DialogResult.Cancel)
            {
                return;
            }

            bool deleteExe = alsoExe == DialogResult.Yes;

            // Stop clicking and remove the start-up entry now (no file lock involved).
            try
            {
                if (_engine != null && _engine.IsRunning)
                {
                    _engine.Stop();
                }
            }
            catch { /* best effort */ }

            Uninstaller.RemoveStartupEntry();

            if (Uninstaller.LaunchCleanupAndExitHelper(deleteExe, out string err))
            {
                try { _trayIcon?.Dispose(); } catch { }
                Logger.Info("Exiting for uninstall cleanup.");
                Environment.Exit(0);
            }
            else
            {
                ShowWarning(err ?? "Could not start the uninstaller.");
            }
        }

        private void OnExportSettings(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "Export settings",
                Filter = "Tempo settings (*.json)|*.json|All files (*.*)|*.*",
                FileName = "tempo-settings.json"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                // Capture the current UI state into settings before exporting.
                CaptureSettingsFromUi();

                if (SettingsManager.ExportTo(_settings, dlg.FileName))
                {
                    ShowInfo("Settings exported.");
                }
                else
                {
                    ShowWarning("Could not export settings.");
                }
            }
        }

        private void OnImportSettings(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Import settings",
                Filter = "Tempo settings (*.json)|*.json|All files (*.*)|*.*"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                AppSettings imported = SettingsManager.ImportFrom(dlg.FileName);
                if (imported == null)
                {
                    ShowWarning("That file could not be read as Tempo settings.");
                    return;
                }

                var confirm = MessageBox.Show(this,
                    "Replace your current settings and keybinds with the imported ones?",
                    "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes)
                {
                    return;
                }

                _settings = imported;
                _settings.EnsureConsistency();
                _lifetimeBaseline = _settings.LifetimeClicks;
                SettingsManager.Save(_settings);

                LoadSettingsIntoUi();
                LoadKeybindsIntoUi();
                StartupManager.SetEnabled(_settings.LaunchAtStartup);
                ApplyThemeToEverything();
                ApplyHotkeysFromSettings();
                ReassertTopMost();

                ShowInfo("Settings imported.");
            }
        }

        /// <summary>Writes the current Settings-tab control values back to _settings.</summary>
        private void CaptureSettingsFromUi()
        {
            _settings.Theme = (ThemeKind)_themeCombo.SelectedIndex;
            _settings.AlwaysOnTop = _alwaysOnTopCheck.Checked;
            _settings.CustomAccentEnabled = _customAccentCheck.Checked;
            _settings.Language = (Language)_languageCombo.SelectedIndex;
            _settings.MinimizeToTrayOnClose = _minimizeToTrayCheck.Checked;
            _settings.StartMinimizedToTray = _startMinimizedCheck.Checked;
            _settings.ShowTrayNotifications = _trayNotifyCheck.Checked;
            _settings.ConfirmBeforeExitWhileRunning = _confirmExitCheck.Checked;
            _settings.SafetyStopOnEscape = _safetyEscapeCheck.Checked;
            _settings.ClickerStartDelaySeconds = (int)_startDelayNum.Value;
            _settings.LaunchAtStartup = _launchStartupCheck.Checked;
            _settings.HideWhenClicking = _hideWhenClickingCheck.Checked;
            _settings.CheckForUpdatesOnLaunch = _checkUpdatesCheck.Checked;
            _settings.WriteLogFile = _writeLogCheck.Checked;
            _settings.RecordSessionHistory = _recordHistoryCheck.Checked;
            _settings.ShowClickingIndicator = _showIndicatorCheck.Checked;
            _settings.MinimizeWhileRecording = _minimizeRecordingCheck.Checked;
            _settings.RememberWindowPosition = _rememberWindowCheck.Checked;
            _settings.WindowOpacity = _opacitySlider.Value;
            if (_unlockSpeedCheck != null) _settings.AdvancedUnlockSpeed = _unlockSpeedCheck.Checked;
            Logger.Enabled = _settings.WriteLogFile;
        }

        private void OpenAbout()
        {
            using (var about = new AboutForm(_theme))
            {
                about.ShowDialog(this);
            }
        }

        // ── Escape-as-emergency-stop support ───────────────────────────────────
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F11)
            {
                ToggleFullScreen();
                return true;
            }

            if (keyData == Keys.Escape &&
                _settings != null && _settings.SafetyStopOnEscape &&
                _engine != null && _engine.IsRunning)
            {
                EmergencyStop();
                return true;
            }

            // Escape leaves full-screen when it isn't being used as a safety stop.
            if (keyData == Keys.Escape && _isFullScreen)
            {
                ToggleFullScreen();
                return true;
            }

            // Keyboard navigation for the tabs: Ctrl+1…9 jump to a tab,
            // Ctrl+Tab / Ctrl+Shift+Tab cycle forwards / backwards.
            if (_tabs != null && _tabs.TabPages.Count > 0 &&
                (keyData & Keys.Control) == Keys.Control)
            {
                Keys key = keyData & Keys.KeyCode;

                // Ctrl + digit (top row or numpad) → jump straight to that tab.
                int digit = -1;
                if (key >= Keys.D1 && key <= Keys.D9)
                {
                    digit = key - Keys.D1;
                }
                else if (key >= Keys.NumPad1 && key <= Keys.NumPad9)
                {
                    digit = key - Keys.NumPad1;
                }

                if (digit >= 0)
                {
                    if (digit < _tabs.TabPages.Count)
                    {
                        _tabs.SelectedIndex = digit;
                        return true;
                    }
                }

                if (key == Keys.Tab)
                {
                    int count = _tabs.TabPages.Count;
                    bool back = (keyData & Keys.Shift) == Keys.Shift;
                    int next = ((_tabs.SelectedIndex + (back ? -1 : 1)) % count + count) % count;
                    _tabs.SelectedIndex = next;
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
