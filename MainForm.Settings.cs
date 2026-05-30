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

        private void BuildSettingsTab()
        {
            var page = new TabPage("Settings") { AutoScroll = true };

            // ── Appearance ─────────────────────────────────────────────────────
            var appearance = UiFactory.Group("Appearance", 12, 12, 696, 80);
            appearance.Controls.Add(UiFactory.Label("Theme", 16, 32));
            _themeCombo = UiFactory.Combo(120, 29, 170,
                "Dark", "Light", "Midnight", "Ocean", "Forest", "Crimson",
                "Solarized", "AMOLED", "Nord", "Dracula");
            _themeCombo.SelectedIndexChanged += OnThemeChanged;
            appearance.Controls.Add(_themeCombo);

            _alwaysOnTopCheck = UiFactory.Check("Always on top", 320, 31);
            appearance.Controls.Add(_alwaysOnTopCheck);

            // ── Startup & Window ───────────────────────────────────────────────
            var startup = UiFactory.Group("Startup & Window", 12, 104, 696, 86);

            _launchStartupCheck = UiFactory.Check("Launch Tempo when I sign in to Windows", 16, 30);
            _hideWhenClickingCheck = UiFactory.Check("Hide window to tray when clicking starts", 16, 58);
            startup.Controls.Add(_launchStartupCheck);
            startup.Controls.Add(_hideWhenClickingCheck);

            var kbNote = UiFactory.Caption("Hotkeys are configured on the Keybinds tab.", 360, 32);
            kbNote.ForeColor = _theme.TextMuted;
            startup.Controls.Add(kbNote);

            // ── Behaviour ──────────────────────────────────────────────────────
            var behaviour = UiFactory.Group("Behaviour", 12, 200, 696, 160);

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

            // ── Data ───────────────────────────────────────────────────────────
            var data = UiFactory.Group("Data & Backup", 12, 372, 696, 96);

            var openFolderBtn = UiFactory.Button("Open data folder", 16, 30, 150, 30);
            openFolderBtn.Click += OnOpenDataFolder;
            data.Controls.Add(openFolderBtn);

            var exportBtn = UiFactory.Button("Export settings…", 176, 30, 150, 30);
            exportBtn.Click += OnExportSettings;
            data.Controls.Add(exportBtn);

            var importBtn = UiFactory.Button("Import settings…", 336, 30, 150, 30);
            importBtn.Click += OnImportSettings;
            data.Controls.Add(importBtn);

            var pathLabel = UiFactory.Caption(SettingsManager.GetSettingsDirectory(), 16, 70);
            pathLabel.ForeColor = _theme.TextMuted;
            pathLabel.AutoSize = false;
            pathLabel.Width = 664;
            pathLabel.Height = 16;
            data.Controls.Add(pathLabel);

            // ── Buttons ────────────────────────────────────────────────────────
            _saveSettingsBtn = UiFactory.PrimaryButton("Save Settings", 12, 484, 150, 36, _theme);
            _saveSettingsBtn.Click += OnSaveSettings;

            var aboutBtn = UiFactory.Button("About…", 172, 484, 120, 36);
            aboutBtn.Click += (s, e) => OpenAbout();

            var checkUpdatesBtn = UiFactory.Button("Check for updates", 302, 484, 160, 36);
            checkUpdatesBtn.Click += OnCheckForUpdatesClicked;

            var resetBtn = UiFactory.Button("Reset to defaults", 560, 484, 148, 36);
            resetBtn.Click += OnResetSettings;

            page.Controls.Add(appearance);
            page.Controls.Add(startup);
            page.Controls.Add(behaviour);
            page.Controls.Add(data);
            page.Controls.Add(_saveSettingsBtn);
            page.Controls.Add(aboutBtn);
            page.Controls.Add(checkUpdatesBtn);
            page.Controls.Add(resetBtn);

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

                _minimizeToTrayCheck.Checked = _settings.MinimizeToTrayOnClose;
                _startMinimizedCheck.Checked = _settings.StartMinimizedToTray;
                _trayNotifyCheck.Checked = _settings.ShowTrayNotifications;
                _confirmExitCheck.Checked = _settings.ConfirmBeforeExitWhileRunning;
                _safetyEscapeCheck.Checked = _settings.SafetyStopOnEscape;
                _launchStartupCheck.Checked = _settings.LaunchAtStartup;
                _hideWhenClickingCheck.Checked = _settings.HideWhenClicking;
                _checkUpdatesCheck.Checked = _settings.CheckForUpdatesOnLaunch;

                int startDelay = _settings.ClickerStartDelaySeconds;
                if (startDelay < 0) startDelay = 0;
                if (startDelay > 60) startDelay = 60;
                _startDelayNum.Value = startDelay;
            }
            finally
            {
                _suppressSettingsEvents = false;
            }
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
        }

        private void OnSaveSettings(object sender, EventArgs e)
        {
            _settings.Theme = (ThemeKind)_themeCombo.SelectedIndex;
            _settings.AlwaysOnTop = _alwaysOnTopCheck.Checked;

            _settings.MinimizeToTrayOnClose = _minimizeToTrayCheck.Checked;
            _settings.StartMinimizedToTray = _startMinimizedCheck.Checked;
            _settings.ShowTrayNotifications = _trayNotifyCheck.Checked;
            _settings.ConfirmBeforeExitWhileRunning = _confirmExitCheck.Checked;
            _settings.SafetyStopOnEscape = _safetyEscapeCheck.Checked;
            _settings.ClickerStartDelaySeconds = (int)_startDelayNum.Value;
            _settings.LaunchAtStartup = _launchStartupCheck.Checked;
            _settings.HideWhenClicking = _hideWhenClickingCheck.Checked;
            _settings.CheckForUpdatesOnLaunch = _checkUpdatesCheck.Checked;

            // Sync the Windows startup entry with the chosen setting.
            StartupManager.SetEnabled(_settings.LaunchAtStartup);

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
            if (_trayAlwaysOnTopItem != null)
            {
                _trayAlwaysOnTopItem.Checked = _settings.AlwaysOnTop;
            }
        }

        private void OnCheckForUpdatesClicked(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
            {
                btn.Enabled = false;
                btn.Text = "Checking…";
            }

            System.Threading.Tasks.Task.Run(() =>
            {
                UpdateChecker.UpdateResult result = UpdateChecker.Check();
                UiInvoke(() =>
                {
                    if (btn != null)
                    {
                        btn.Enabled = true;
                        btn.Text = "Check for updates";
                    }
                    PresentUpdateResult(result, announceUpToDate: true);
                });
            });
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

            string notes = string.IsNullOrWhiteSpace(result.Notes) ? "" : "\n\nWhat's new:\n" + result.Notes;
            string body =
                $"A new version of Tempo is available.\n\n" +
                $"Installed: {UpdateChecker.CurrentVersion}\n" +
                $"Latest:    {result.LatestVersion}{notes}\n\n" +
                "Open the download page now?";

            DialogResult choice = MessageBox.Show(this, body, "Update available",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (choice == DialogResult.Yes)
            {
                string url = string.IsNullOrWhiteSpace(result.DownloadUrl)
                    ? UpdateChecker.ManifestUrl
                    : result.DownloadUrl;
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
            _settings.MinimizeToTrayOnClose = _minimizeToTrayCheck.Checked;
            _settings.StartMinimizedToTray = _startMinimizedCheck.Checked;
            _settings.ShowTrayNotifications = _trayNotifyCheck.Checked;
            _settings.ConfirmBeforeExitWhileRunning = _confirmExitCheck.Checked;
            _settings.SafetyStopOnEscape = _safetyEscapeCheck.Checked;
            _settings.ClickerStartDelaySeconds = (int)_startDelayNum.Value;
            _settings.LaunchAtStartup = _launchStartupCheck.Checked;
            _settings.HideWhenClicking = _hideWhenClickingCheck.Checked;
            _settings.CheckForUpdatesOnLaunch = _checkUpdatesCheck.Checked;
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
            if (keyData == Keys.Escape &&
                _settings != null && _settings.SafetyStopOnEscape &&
                _engine != null && _engine.IsRunning)
            {
                EmergencyStop();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
