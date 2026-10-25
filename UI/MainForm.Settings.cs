using System;
using System.Collections.Generic;
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
        private CheckBox _openAtSignInCheck;
        private CheckBox _hideWhenClickingCheck;
        private CheckBox _checkUpdatesCheck;
        private CheckBox _checkRobloxUpdatesCheck;
        private CheckBox _writeLogCheck;
        private CheckBox _showIndicatorCheck;
        private CheckBox _minimizeRecordingCheck;
        private CheckBox _ignoreOwnWindowCheck;
        private CheckBox _guardShortcutsCheck;
        private CheckBox _recordHistoryCheck;
        private CheckBox _rememberWindowCheck;
        private CheckBox _rememberTabCheck;
        private CheckBox _integrityCheck;
        private Label _integrityStatusLabel;
        private Button _integrityTrustBtn;   // "Trust this copy" — "Repair" for an altered unpacked copy, "Show files" for planted code
        private Button _integrityDetailsBtn;
        private SmoothTrackBar _opacitySlider;
        private Label _opacityValueLabel;
        // NOTE: the separate header/footer backdrop pickers were replaced by the single
        // full-window one below. Their buttons are no longer built, so the fields that
        // held them (and their tooltips) were dead weight the compiler warned about on
        // every build. The SETTINGS they wrote are still read as fallback sources in
        // ApplyBackgroundGif, so an older config that used them keeps working.
        private Button _fullGifBtn;
        private Button _fullGifClearBtn;
        private SmoothTrackBar _bgDimTrack;
        private Label _bgDimLabel;

        /// <summary>The Dim a fresh install starts with (AppSettings.BackgroundDim); double-click the slider to return to it.</summary>
        private const int DefaultBackgroundDim = 55;
        // Read-only summary of the display Tempo is on, in the Window & Display card.
        private Label _displayInfoLabel;
        // Tab or its own window, for the Roblox Account Manager.
        private ComboBox _accountsPlacementCombo;
        private bool _updatingAccountsPlacement;

        /// <summary>
        /// Writes settings now, and never lets a disk problem reach the user as a crash — the
        /// setting the click just changed is already live in memory either way. For the toggles
        /// that must stick through a force-kill or a reboot rather than waiting for Save.
        /// </summary>
        private void SaveSettingsQuietly()
        {
            if (_settings == null) { return; }
            try { SettingsManager.Save(_settings); }
            catch (Exception ex) { Utils.Logger.Swallow("SaveSettingsQuietly", ex); }
        }

        /// <summary>Points the placement combo at what the setting actually says, without re-firing it.</summary>
        private void SyncAccountsPlacementCombo()
        {
            if (_accountsPlacementCombo == null || _settings == null) { return; }
            _updatingAccountsPlacement = true;
            try
            {
                _accountsPlacementCombo.SelectedIndex =
                    _settings.AccountsPlacement == AccountsPlacement.Window ? 1 : 0;
            }
            finally { _updatingAccountsPlacement = false; }
            // The Accounts row's ↗ follows the setting (every change of it passes here or through
            // the combo itself).
            RefreshSidebarSelection();
        }
        private NumericUpDown _captionLinesNum;
        // "(experimental)" normally; "file missing" when the configured background
        // image can no longer be read. Updated by ApplyBackgroundGif.
        private Label _bgGifNote;

        private void BuildSettingsTab()
        {
            var page = new BackdropTabPage(Utils.Localization.T("Settings")) { AutoScroll = true };
            page.Name = "settings";   // stable key for LastTabKey

            // SPIKE: see the end of this method, where the HTML page is laid OVER the
            // finished tab when the hidden flag is set.
            //
            // The first attempt returned from here instead, so none of the Settings
            // controls were ever created — and LoadSettingsIntoUi, which reads about
            // sixty of them without null-guards, crashed the app on the next line
            // (NullReferenceException, caught by the crash reporter). Building the tab
            // and covering it costs the ~300 ms the tab takes, which is a number we
            // already have and can subtract; it does not distort what the spike is
            // actually measuring, which is what WebView2 costs.

            // Sixty-odd settings over a 2,100px scroll is too many to hunt through.
            AddSettingsSearchRow(page);

            // NOTE ON THE Y VALUES BELOW. They no longer place the cards — the stack is
            // derived by RestackSettingsCards() at the end of this method, which is why
            // the warnings that used to live down by the buttons ("these y positions must
            // move whenever a group above grows") are gone. What they still do is
            // establish the ORDER the cards appear in, so keep them ascending. Heights
            // are real and do matter: a card is laid out at whatever height it declares.

            // ── Appearance ─────────────────────────────────────────────────────
            // 200, not 168: the logo row below was added under the background-image row.
            // 264, not 200: the HDR row and the display-colour line were added below the
            // logo row.
            // 300, not 272: the display note under "Correct colours for HDR" has four lines of
            // room now (facts, the correction's state, and the SDR-brightness advice).
            var appearance = UiFactory.Group(Localization.T("Appearance"), 12, 12, 696, 300, CardIcon.Star);
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

            // Follow the OS light/dark mode. When on, the manual theme picker is
            // ignored (and greyed) and Tempo re-themes live as Windows switches.
            // Row 1, beside the Language combo (which ends at 496) — NOT row 2 where it
            // used to sit. It follows the OS light/dark setting, so it belongs with the
            // theme picker rather than in the middle of the accent controls; moving it up
            // also gives the accent row back the width its details need. The layout suite
            // caught the collision when the details arrived, in all six languages.
            _followSystemThemeCheck = UiFactory.Check("Match Windows", 512, 32);
            _followSystemThemeCheck.AutoSize = true;
            _followSystemThemeCheck.CheckedChanged += OnFollowSystemThemeToggled;
            appearance.Controls.Add(_followSystemThemeCheck);

            appearance.Controls.Add(UiFactory.Label("Language", 290, 32));
            _languageCombo = UiFactory.Combo(356, 29, 140,
                "English", "Español", "Français", "Deutsch", "Italiano", "Português");
            _languageCombo.SelectedIndexChanged += OnLanguageChanged;
            appearance.Controls.Add(_languageCombo);

            // "Always on top" used to sit here, in Appearance. It is not an appearance
            // setting — it is what the WINDOW does — and the page has a card literally
            // called "Window & Display" that a user would check first. Moved there,
            // along with "Remember window position & size", which was stranded in
            // "Startup & Window" for the same reason.

            // Custom accent colour.
            _customAccentCheck = UiFactory.Check("Use a custom accent colour", 16, 66);
            _customAccentCheck.CheckedChanged += OnCustomAccentToggled;
            appearance.Controls.Add(_customAccentCheck);

            _chooseAccentBtn = UiFactory.Button("Choose colour…", 250, 62, 130, 26);
            _chooseAccentBtn.Click += OnChooseAccentClicked;
            appearance.Controls.Add(_chooseAccentBtn);

            // ── Accent details ──────────────────────────────────────────────
            //
            // The row used to be a checkbox, a "Choose colour…" button and a swatch, and
            // that was everything Tempo would tell you about a colour it paints across
            // the whole app. Three things were missing and all three matter:
            //
            //  • the VALUE. A swatch cannot be written down, pasted from a brand palette,
            //    or matched to anything. The hex box reads and writes it.
            //  • the HOVER colour, which is derived from the accent (Lighten 0.14) and
            //    shown on every primary button the moment the pointer touches it — but
            //    was never previewed, so a choice that looked fine could hover badly.
            //  • whether the accent is READABLE. Primary buttons paint their label onto
            //    it, and while the 38 built-in themes are audited to WCAG AA, a custom
            //    accent skips that entirely. The contrast figure is the only warning a
            //    user gets before shipping themselves an unreadable button.
            _accentSwatch = new Panel { Left = 390, Top = 63, Width = 44, Height = 22, BorderStyle = BorderStyle.FixedSingle };
            appearance.Controls.Add(_accentSwatch);

            _accentHoverSwatch = new Panel
            {
                Left = 436, Top = 63, Width = 22, Height = 22,
                BorderStyle = BorderStyle.FixedSingle
            };
            appearance.Controls.Add(_accentHoverSwatch);

            _accentHexBox = new FlatTextBox
            {
                Left = 464, Top = 62, Width = 78,
                Font = UiFactory.BodyFont,
                MaxLength = 7,
                TextAlign = HorizontalAlignment.Center
            };
            // Commit on Enter or on leaving the field, never per keystroke: repainting the
            // entire app on the way through "#1", "#12", "#123" would be a light show.
            _accentHexBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;   // no ding from the default button
                    CommitAccentHex();
                }
            };
            _accentHexBox.Leave += (s, e) => CommitAccentHex();
            appearance.Controls.Add(_accentHexBox);

            _accentContrastLabel = UiFactory.Caption("", 550, 66);
            _accentContrastLabel.AutoSize = false;
            _accentContrastLabel.Width = 132;
            _accentContrastLabel.Height = 18;
            appearance.Controls.Add(_accentContrastLabel);

            // Live preview.
            var previewLabel = UiFactory.Label("Preview", 16, 104);
            appearance.Controls.Add(previewLabel);
            // The swatches start after the label and share out whatever is left before the
            // Accent button at 330. At a fixed x=90 the Portuguese "Pré-visualização" (92 px)
            // ran 18 px under the first swatch.
            // LayoutRight: the label sizes itself in screen pixels while the window is built.
            int swatchLeft = Math.Max(90, LayoutRight(previewLabel) + 8);
            int swatchStep = Math.Min(38, (330 - 8 - swatchLeft) / 6);
            _previewSwatches = new Panel[6];
            for (int i = 0; i < _previewSwatches.Length; i++)
            {
                _previewSwatches[i] = new Panel
                {
                    Left = swatchLeft + i * swatchStep,
                    Top = 102,
                    Width = Math.Max(18, swatchStep - 4),
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
                Text = Localization.T("Accent"),
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
                // A pangram-style sample: translators should pick one that exercises
                // their own alphabet (accents included), not translate this literally.
                Text = Localization.T("The quick brown fox  ·  Aa Bb 123"),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
            appearance.Controls.Add(_previewSample);

            // Optional animated background image (experimental): ONE image now spans
            // the whole window as a single seamless wallpaper — the header, sidebar,
            // page and footer each paint their aligned slice of it. The Dim slider
            // controls the readability overlay, applied uniformly to every area.
            appearance.Controls.Add(UiFactory.Label("Background image", 16, 140));
            _fullGifBtn = UiFactory.Button("Choose…", 150, 136, 80, 26);
            _fullGifBtn.Click += OnChooseFullGif;
            appearance.Controls.Add(_fullGifBtn);
            _fullGifClearBtn = UiFactory.Button("Clear", 232, 136, 48, 26);
            _fullGifClearBtn.Click += OnClearFullGif;
            appearance.Controls.Add(_fullGifClearBtn);

            var dimLabel = UiFactory.Label("Dim", 300, 140);
            appearance.Controls.Add(dimLabel);
            // The slider starts where the label ENDS, not at a fixed 334. "Dim" is 26 px;
            // "Atenuar", "Atténuer", "Attenua" and "Escurecer" are 45–59 px and "Abdunkeln"
            // 65 — so in every language but English the slider was painted over the end of
            // its own label (31 px of it in German). The layout check could not see it: the
            // slider is a custom control, and it only knew about the framework's. It keeps
            // its right edge, so the percentage beside it does not move.
            int dimLeft = Math.Max(334, LayoutRight(dimLabel) + 6);
            _bgDimTrack = new SmoothTrackBar
            {
                Left = dimLeft, Top = 140, Width = Math.Max(80, 484 - dimLeft), Height = 22,
                Minimum = 0, Maximum = 90, TickFrequency = 15,
                SmallChange = 5, LargeChange = 10,
                Value = _settings != null ? _settings.BackgroundDim : 55
            };
            _bgDimLabel = UiFactory.Caption((_settings != null ? _settings.BackgroundDim : 55) + "%", 490, 140);
            _bgDimTrack.Scroll += (s, e) =>
            {
                _bgDimLabel.Text = _bgDimTrack.Value + "%";
                if (_suppressSettingsEvents || _settings == null) { return; }
                _settings.BackgroundDim = _bgDimTrack.Value;
                // Live and cheap: the picture keeps playing and only the scrim over it changes.
                // (ApplyBackgroundGif here reloaded the image on every step of a drag.)
                ApplyBackgroundDim();
            };
            // Double-click: back to where it starts.
            _bgDimTrack.DoubleClick += (s, e) =>
            {
                if (_suppressSettingsEvents || _settings == null) { return; }
                _bgDimTrack.Value = DefaultBackgroundDim;
                _bgDimLabel.Text = DefaultBackgroundDim + "%";
                _settings.BackgroundDim = DefaultBackgroundDim;
                ApplyBackgroundDim();
            };
            appearance.Controls.Add(_bgDimTrack);
            appearance.Controls.Add(_bgDimLabel);

            // Doubles as the background's status line. A configured image whose file has
            // gone (moved, deleted, USB stick unplugged) used to vanish from the window
            // with nothing but a log warning to explain it — the setting still showed a
            // path, so from the outside the feature just looked broken.
            _bgGifNote = UiFactory.Caption("(experimental)", 536, 140);
            _bgGifNote.ForeColor = _theme.TextMuted;
            _bgGifNote.AutoSize = false;
            _bgGifNote.Width = 150;
            appearance.Controls.Add(_bgGifNote);

            // The custom logo (About → "Choose image…") accepts a GIF, and a GIF now
            // plays wherever the logo appears. This is the off switch, and — more useful
            // day to day — the only place that says out loud what the logo is doing:
            // "3.2s loop" means it worked, "still image" means the file has one frame.
            _animateLogoCheck = UiFactory.Check("Animate my logo", 16, 176);
            _animateLogoCheck.AutoSize = true;
            _animateLogoCheck.CheckedChanged += OnAnimateLogoToggled;
            appearance.Controls.Add(_animateLogoCheck);

            // Playback speed. A logo GIF is authored for a web page, not for a 16px tray
            // icon — timing that reads well at 400px is often a blur at icon size, and an
            // ambient loop can be too sleepy to notice. The values are ratios of the
            // file's own delays, so "1×" always means "exactly as the author made it".
            _logoSpeedLabel = UiFactory.Caption("Speed:", 16, 206);
            _logoSpeedLabel.AutoSize = true;
            appearance.Controls.Add(_logoSpeedLabel);

            _logoSpeedCombo = UiFactory.Combo(100, 202, 76,
                "0.25×", "0.5×", "1×", "1.5×", "2×", "4×");
            _logoSpeedCombo.SelectedIndexChanged += OnLogoSpeedChanged;
            appearance.Controls.Add(_logoSpeedCombo);

            // What the DISPLAY is doing to these colours.
            //
            // "The theme doesn't look like the colours you picked" has causes that live
            // entirely outside Tempo, and this card — where the user is standing when they
            // notice — said nothing about any of them. Windows HDR is the big one: Tempo
            // is an SDR app, so the compositor converts everything it paints, dark themes
            // come out milky and near-blacks lift. Nothing inside the process can see that;
            // the pixels we drew are the pixels we asked for. The box pre-darkens the
            // palette by exactly the curve Windows uses (Theme.ForHdr), per screen.
            _hdrCompensateCheck = UiFactory.Check("Correct colours for HDR", 200, 204);
            _hdrCompensateCheck.AutoSize = true;
            _hdrCompensateCheck.CheckedChanged += OnHdrCompensationToggled;
            appearance.Controls.Add(_hdrCompensateCheck);

            // One colour per line (NoteLabel): a single warning used to turn the whole note amber.
            _displayColourNote = new NoteLabel
            {
                Left = 16,
                Top = 230,
                AutoSize = false,
                Width = 670,
                Height = 58,
                Font = UiFactory.FontFor(8.25f, FontStyle.Regular)
            };
            appearance.Controls.Add(_displayColourNote);
            RefreshDisplayColourNote();

            _animateLogoNote = UiFactory.Caption("", 190, 178);
            _animateLogoNote.AutoSize = false;
            _animateLogoNote.Width = 496;
            _animateLogoNote.ForeColor = _theme.TextMuted;
            appearance.Controls.Add(_animateLogoNote);

            // ── Startup & Window ───────────────────────────────────────────────
            var startup = UiFactory.Group(Localization.T("Startup & Window"), 12, 192, 696, 142, CardIcon.Play);

            _launchStartupCheck = UiFactory.Check("Launch Tempo when I sign in to Windows", 16, 30);
            _launchStartupCheck.CheckedChanged += (s, e) => SyncOpenAtSignInEnabled();
            startup.Controls.Add(_launchStartupCheck);

            // Whether that sign-in launch opens the window or stays in the tray. It always stayed
            // in the tray, and the caption above said so, so there was no way to have the window
            // up after signing in. Indented because it only means something while the box above
            // is ticked. Saved the moment it is clicked: the next sign-in is usually a reboot away,
            // and Save may never be pressed before it.
            _openAtSignInCheck = UiFactory.Check("Open the window at sign-in", 36, 58);
            _openAtSignInCheck.AutoSize = true;
            _openAtSignInCheck.CheckedChanged += (s, e) =>
            {
                if (_suppressSettingsEvents || _settings == null) { return; }
                _settings.OpenWindowAtSignIn = _openAtSignInCheck.Checked;
                try { SettingsManager.Save(_settings); } catch (Exception ex) { Logger.Swallow("OpenWindowAtSignIn.save", ex); }
            };
            startup.Controls.Add(_openAtSignInCheck);

            _hideWhenClickingCheck = UiFactory.Check("Hide window to tray when clicking starts", 16, 86);
            startup.Controls.Add(_hideWhenClickingCheck);

            // Reopen where you left off. Matters most after an unattended run: Windows
            // reboots overnight, launch-at-startup brings Tempo back, and landing on
            // Clicker instead of the Macros tab you were using reads as a lost page.
            _rememberTabCheck = UiFactory.Check("Reopen on the tab I used last", 16, 114);
            _rememberTabCheck.AutoSize = true;
            _rememberTabCheck.CheckedChanged += (s, e) =>
            {
                if (_suppressSettingsEvents || _settings == null) { return; }
                _settings.RememberLastTab = _rememberTabCheck.Checked;
                // Remembered immediately, like the notification toggles — it must stick
                // through a force-kill or reboot without needing Save.
                if (_rememberTabCheck.Checked && _tabs != null && _tabs.SelectedIndex >= 0)
                {
                    _settings.LastTabIndex = _tabs.SelectedIndex;
                    _settings.LastTabKey = CurrentTabKey();   // the index alone shifts when tabs are added
                }
                SaveLastTabNow();
            };
            startup.Controls.Add(_rememberTabCheck);

            // Optional wait before Tempo's launch-time network update check, so an
            // auto-start at sign-in doesn't fight everything else booting for the network.
            startup.Controls.Add(UiFactory.Label("Startup delay (s):", 360, 32));
            _startupDelayNum = UiFactory.Numeric(470, 28, 60, 0, 120, 0);
            startup.Controls.Add(_startupDelayNum);

            // "Remember window position & size" moved to the Window & Display card —
            // see the note in Appearance above.

            // ── Behaviour ──────────────────────────────────────────────────────
            // 256, not 228: one more row for the cursor trail, moved here from the
            // Macros tab where it never belonged. 284: and one for holding back Windows' shortcuts.
            // 312: and one for the Roblox update check.
            var behaviour = UiFactory.Group(Localization.T("Behaviour"), 12, 316, 696, 312, CardIcon.Gear);

            _minimizeToTrayCheck = UiFactory.Check("Minimise to tray instead of closing", 16, 30);
            _startMinimizedCheck = UiFactory.Check("Start minimised to tray", 16, 58);
            _trayNotifyCheck = UiFactory.Check("Show tray notifications", 16, 86);
            // Persist the moment it is clicked, exactly like "Use Tempo's animated pop-up
            // notifications" beside it.
            //
            // This one had NO handler: it was read only when Save Settings was pressed or
            // when the window closed cleanly. So turning notifications off and not pressing
            // Save — or pressing it and having the app killed rather than closed, which is
            // what a forced restart looks like — silently discarded the change, and the
            // notifications were back on next launch. Its neighbour never had that problem,
            // which is what made the behaviour look random.
            _trayNotifyCheck.CheckedChanged += (s, e) =>
            {
                if (_suppressSettingsEvents || _settings == null) { return; }
                _settings.ShowTrayNotifications = _trayNotifyCheck.Checked;
                PersistNotificationSettings();
            };
            _confirmExitCheck = UiFactory.Check("Confirm before exit while running", 360, 30);
            _safetyEscapeCheck = UiFactory.Check("Allow Escape key as emergency stop", 360, 58);

            behaviour.Controls.Add(_minimizeToTrayCheck);
            behaviour.Controls.Add(_startMinimizedCheck);

            // Safety: a forgotten Tempo in the tray shouldn't react to hotkeys.
            // Placed in the right column so the left column isn't over-stuffed (it used to
            // run past the card edge and collide with the row above it). At the bottom of it:
            // its old row now holds the overlay's Customise button (see below).
            _traySleepCheck = UiFactory.Check("Sleep in tray (pause hotkeys && cursor trail)", 360, 226);
            behaviour.Controls.Add(_traySleepCheck);
            behaviour.Controls.Add(_trayNotifyCheck);
            behaviour.Controls.Add(_confirmExitCheck);
            behaviour.Controls.Add(_safetyEscapeCheck);

            behaviour.Controls.Add(UiFactory.Label("Start delay (s):", 360, 90));
            _startDelayNum = UiFactory.Numeric(462, 86, 56, 0, 60, 0);
            behaviour.Controls.Add(_startDelayNum);
            _startDelayBeepCheck = UiFactory.Check("Beep", 530, 88);
            _startDelayBeepCheck.AutoSize = true;
            behaviour.Controls.Add(_startDelayBeepCheck);

            // Update checking: the checkbox is the master on/off; the combo beside it
            // sets how often the automatic check may actually run. The row reads
            // "Check for updates [Daily]". Not "… on start": it also runs while Tempo stays open,
            // which for a tray app is most of the time. And short on purpose — the combo leaves it
            // 184 px, and the longer captions ended in "…" (in five languages the old one did).
            _checkUpdatesCheck = UiFactory.Check("Check for updates", 16, 114);
            _checkUpdatesCheck.AutoSize = true;
            _checkUpdatesCheck.CheckedChanged += (s, e) =>
            {
                if (_updateFreqCombo != null) { _updateFreqCombo.Enabled = _checkUpdatesCheck.Checked; }
            };
            behaviour.Controls.Add(_checkUpdatesCheck);
            _updateFreqCombo = UiFactory.Combo(210, 111, 128,
                Localization.T("Every launch"), Localization.T("Daily"), Localization.T("Weekly"));
            // As wide as its longest choice (French "À chaque lancement" needed 116 px of the 104 it
            // had), short of the "Write a log file" tick box at x = 360.
            if (_updateFreqCombo is FlatComboBox freqBox) { freqBox.Width = Math.Min(142, freqBox.WidthForChoices96(128)); }
            behaviour.Controls.Add(_updateFreqCombo);

            _writeLogCheck = UiFactory.Check("Write a log file to disk", 360, 118);
            // Applies the moment it is ticked, like the other privacy switches: "turn off for extra
            // privacy" used to keep writing until Save, and left the log already written behind.
            _writeLogCheck.CheckedChanged += OnWriteLogChanged;
            behaviour.Controls.Add(_writeLogCheck);

            _recordHistoryCheck = UiFactory.Check("Record session history and statistics", 16, 142);
            behaviour.Controls.Add(_recordHistoryCheck);

            _showIndicatorCheck = UiFactory.Check("Show on-screen overlay while running", 360, 146);
            _showIndicatorCheck.AutoSize = true;
            // Applies at once — the badge of a run in progress appears or goes — as tray ▸ Overlays' copy
            // of this switch always did; here it waited for Save.
            _showIndicatorCheck.CheckedChanged += OnShowOverlayChanged;
            behaviour.Controls.Add(_showIndicatorCheck);
            // Under the checkbox, not beside it. Beside it left the caption 218 px, and it needs
            // more than that in every language — English included, where it always read "Show
            // on-screen overlay whi…".
            var overlayCustomizeBtn = UiFactory.Button("Customise…", 380, 171, 100, 26);
            overlayCustomizeBtn.Click += OnCustomiseOverlay;
            behaviour.Controls.Add(overlayCustomizeBtn);

            _minimizeRecordingCheck = UiFactory.Check("Minimise window during macro record && playback", 16, 170);
            behaviour.Controls.Add(_minimizeRecordingCheck);

            // Sits directly under the minimise option because the two are related: both keep
            // a run from operating Tempo itself. Minimising only covers macros (and only with
            // a stop hotkey bound); this one also covers the clicker and works while visible.
            _ignoreOwnWindowCheck = UiFactory.Check("Ignore Tempo's own window while clicking or playing a macro", 16, 198);
            // Applies and persists the moment it's ticked, rather than waiting for Save
            // Settings. A safety toggle that silently does nothing until you find the Save
            // button is precisely the confusion this option exists to avoid.
            _ignoreOwnWindowCheck.CheckedChanged += (s, e) =>
            {
                if (_suppressSettingsEvents || _settings == null) { return; }
                _settings.IgnoreOwnWindowWhileRunning = _ignoreOwnWindowCheck.Checked;
                PersistNotificationSettings();   // shared "save settings now" helper
            };
            behaviour.Controls.Add(_ignoreOwnWindowCheck);

            // Moved here from the Macros tab: an app-wide preference belongs with the
            // other app-wide preferences. Its state is loaded in LoadSettingsIntoUi's
            // tail, which already handled it and needs no change.
            _cursorTrailCheck = UiFactory.Check("Colorful cursor trail (just for fun)", 16, 226);
            _cursorTrailCheck.Checked = _settings != null && _settings.CursorTrailEnabled;
            _cursorTrailCheck.CheckedChanged += OnCursorTrailChanged;
            behaviour.Controls.Add(_cursorTrailCheck);

            // While a run lasts only Tempo's keybinds act: the Windows key, Win+…, Ctrl+Esc and the
            // Sticky Keys question are held back (Native.ShortcutGuard). A row of its own — the caption
            // needs the card's width. Applies the moment it is ticked, mid-run included, like the
            // safety option above it.
            _guardShortcutsCheck = UiFactory.Check("Hold back Windows shortcuts while clicking or running a macro", 16, 254);
            // The same switch is on the Clicker and Macros tabs and in the tray (SetGuardWindowsShortcuts).
            _guardShortcutsCheck.CheckedChanged += (s, e) =>
            {
                if (_suppressSettingsEvents || _settings == null) { return; }
                SetGuardWindowsShortcuts(_guardShortcutsCheck.Checked, "Settings page");
            };
            behaviour.Controls.Add(_guardShortcutsCheck);

            // Is a newer Roblox out (MainForm.RobloxUpdates)? Under Tempo's own update switch's card, a row of its own.
            // Applies the moment it is ticked, like the other switches that decide what goes online.
            _checkRobloxUpdatesCheck = UiFactory.Check("Check for Roblox updates", 16, 282);
            _checkRobloxUpdatesCheck.AutoSize = true;
            _checkRobloxUpdatesCheck.CheckedChanged += (s, e) =>
            {
                if (_suppressSettingsEvents || _settings == null) { return; }
                _settings.CheckRobloxUpdates = _checkRobloxUpdatesCheck.Checked;
                PersistNotificationSettings();   // shared "save settings now" helper
            };
            behaviour.Controls.Add(_checkRobloxUpdatesCheck);

            // (Live Captions controls moved to their own dedicated group below.)

            // ── Notifications ──────────────────────────────────────────────────
            // 252, not 220: one row for the history button, and one more for muted apps.
            // (The card's Y is derived by RestackSettingsCards, so growing it is safe.)
            var notify = UiFactory.Group(Localization.T("Notifications"), 12, 556, 696, 252, CardIcon.Gear);

            _customNotifyCheck = UiFactory.Check("Use Tempo's animated pop-up notifications", 16, 30);
            _customNotifyCheck.AutoSize = true;
            _customNotifyCheck.CheckedChanged += (s, e) =>
            {
                if (_suppressSettingsEvents) { return; }
                _settings.CustomNotifications = _customNotifyCheck.Checked;
                UpdateNotifyControlsEnabled();
                ApplyNotificationSettings();
                RefreshNotifyStatus();           // the mirror pauses / resumes with these
                PersistNotificationSettings();   // remember the on/off immediately
            };
            notify.Controls.Add(_customNotifyCheck);

            notify.Controls.Add(UiFactory.Label("Corner:", 416, 32));
            // 25-55: centred on its row, and clear of the "Show (s)" box under it — at 28 the combo, 30 tall once
            // it has its window, ran 2 px into that box.
            _notifyCornerCombo = UiFactory.Combo(472, 25, 206,
                Utils.Localization.T("Top-right"), Utils.Localization.T("Top-left"), Utils.Localization.T("Bottom-right"), Utils.Localization.T("Bottom-left"));
            _notifyCornerCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_suppressSettingsEvents) { return; }
                _settings.NotificationCorner = Math.Max(0, _notifyCornerCombo.SelectedIndex);
                _notifications?.Relayout();   // migrate any live cards to the new corner now
                PersistNotificationSettings();
            };
            notify.Controls.Add(_notifyCornerCombo);

            // Mirror OTHER apps' Windows notifications into Tempo's style. Honest about
            // the permission and that it doesn't stop Windows' own pop-up (see tooltip).
            _mirrorNotifyCheck = UiFactory.Check("Mirror Windows notifications from other apps (needs permission)", 16, 58);
            _mirrorNotifyCheck.AutoSize = true;
            _mirrorNotifyCheck.CheckedChanged += (s, e) =>
            {
                if (_suppressSettingsEvents) { return; }
                _settings.MirrorWindowsNotifications = _mirrorNotifyCheck.Checked;
                UpdateNotifyControlsEnabled();
                ApplyNotificationSettings();
                RefreshNotifyStatus();
                PersistNotificationSettings();
            };
            notify.Controls.Add(_mirrorNotifyCheck);

            var showLabel = UiFactory.Label("Show (s):", 416, 60);
            notify.Controls.Add(showLabel);
            // After its label, however wide that is. Text grows by more than the window does on a 150%
            // screen (glyphs round to whole pixels): "Anzeigen (s):" came out 112 px, 7 into the box at 486.
            _notifyDurationNum = UiFactory.Numeric(Math.Max(486, LayoutRight(showLabel) + 4), 57, 56, 2, 20, 5);
            _notifyDurationNum.ValueChanged += (s, e) =>
            {
                if (_suppressSettingsEvents) { return; }
                _settings.NotificationDurationSeconds = (int)_notifyDurationNum.Value;
                PersistNotificationSettings();
            };
            notify.Controls.Add(_notifyDurationNum);

            _mirrorClearCheck = UiFactory.Check("After mirroring, remove it from the Windows Action Center", 34, 86);
            _mirrorClearCheck.AutoSize = true;
            _mirrorClearCheck.CheckedChanged += (s, e) =>
            {
                if (_suppressSettingsEvents) { return; }
                _settings.MirrorClearFromActionCenter = _mirrorClearCheck.Checked;
                PersistNotificationSettings();
            };
            notify.Controls.Add(_mirrorClearCheck);

            var notifyTestBtn = UiFactory.Button("Test pop-up", 16, 112, 110, 28);
            notifyTestBtn.Click += (s, e) =>
            {
                _notifications?.Notify("Tempo", "Notifications are working",
                    "Click me to open Tempo — mirrored cards open the app that sent them.",
                    UI.ToastKind.Success, TempoNotifyIcon(), TempoHeroImage(), ShowFromTrayAndActivate);
            };
            notify.Controls.Add(notifyTestBtn);

            var notifyWinBtn = UiFactory.Button("Stop Windows pop-ups…", 134, 112, 250, 28);
            notifyWinBtn.Click += (s, e) =>
            {
                // The ONLY reliable way to stop Windows' own banner pop-ups is its
                // "Do not disturb" toggle at the top of this page. Under Do not disturb,
                // Windows stops bannering but STILL hands notifications to Tempo's
                // mirror — so only Tempo's cards appear. Tempo never flips this itself.
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    { FileName = "ms-settings:notifications", UseShellExecute = true });
                }
                catch (Exception ex) { Utils.Logger.Swallow("OpenNotifySettings", ex); }
            };
            notify.Controls.Add(notifyWinBtn);

            // Only appears when Windows has actually refused the listener. It opens the
            // GLOBAL notifications-access page, not the per-app list: Tempo is unpackaged
            // and so has no row of its own there — see WindowsNotificationMirror.
            // 430,182 and not beside the mirror status at 388,118: the overlap check
            // caught it sitting on top of that label by 104x20px. Hidden most of the
            // time, which is exactly why it needed catching — a collision nobody sees
            // until the one moment the control appears.
            _notifyPermissionBtn = UiFactory.Button(Localization.T("Allow notification access…"), 430, 182, 200, 28);
            _notifyPermissionBtn.Visible = false;
            _notifyPermissionBtn.Click += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    { FileName = "ms-settings:privacy-notifications", UseShellExecute = true });
                }
                catch (Exception ex) { Utils.Logger.Swallow("OpenNotifyPrivacySettings", ex); }
            };
            notify.Controls.Add(_notifyPermissionBtn);

            // Photo notifications: pop a card with the picture the instant you copy a
            // screenshot / image to the clipboard (a real photo alert).
            _notifyScreenshotCheck = UiFactory.Check("Show a preview pop-up when I copy a screenshot or image", 16, 150);
            _notifyScreenshotCheck.AutoSize = true;
            _notifyScreenshotCheck.CheckedChanged += (s, e) =>
            {
                if (_suppressSettingsEvents) { return; }
                _settings.NotifyOnClipboardImage = _notifyScreenshotCheck.Checked;
                ApplyClipboardImageWatcher();
                PersistNotificationSettings();
            };
            notify.Controls.Add(_notifyScreenshotCheck);

            // Sits on the free right-hand side of the same row, so the card doesn't grow
            // (and nothing below has to shift). The card still dismisses on a click and
            // still auto-closes without the ✕ — this hides a control, not an ability.
            _notifyCloseCheck = UiFactory.Check("Show ✕ on cards", 430, 150);
            _notifyCloseCheck.AutoSize = true;
            _notifyCloseCheck.CheckedChanged += (sX, eX) =>
            {
                if (_suppressSettingsEvents) { return; }
                _settings.NotificationShowClose = _notifyCloseCheck.Checked;
                UI.NotificationToastForm.ShowCloseButton = _settings.NotificationShowClose;
                PersistNotificationSettings();
            };
            notify.Controls.Add(_notifyCloseCheck);

            // Reachable from the tray too — that is where someone coming out of a game
            // will look — but it belongs here as well, beside the settings that decide
            // what gets shown in the first place.
            var notifyHistoryBtn = UiFactory.Button(Localization.T("Notification history…"), 16, 182, 190, 28);
            notifyHistoryBtn.Click += (s, e) => UI.NotificationHistoryForm.ShowFor(this, _theme);
            notify.Controls.Add(notifyHistoryBtn);

            _notifyMissedLabel = UiFactory.Caption("", 214, 188);
            notify.Controls.Add(_notifyMissedLabel);

            // Kept current as the history changes. It used to be set once, when settings were
            // loaded, and never again: clearing the history left "3 you didn't see" sitting
            // here until Tempo was restarted, and a notification missed DURING a game never
            // added to it either. NotificationHistory.Changed was already raised for both —
            // nothing had ever subscribed to it.
            Utils.NotificationHistory.Changed += OnNotificationHistoryChanged;

            // One noisy app used to mean turning the whole mirror off. Muting happens on
            // the card (right-click it); this is where you take it back, and the count is
            // the only place a muted app is visible when no card is on screen.
            _mutedAppsBtn = UiFactory.Button(Localization.T("Muted apps…"), 16, 214, 190, 28);
            _mutedAppsBtn.Click += (s, e) =>
            {
                using (var dlg = new UI.MutedAppsDialog(_theme, _settings, UnmuteMirroredApp))
                {
                    dlg.ShowDialog(this);
                }
                RefreshMutedAppsLabel();
            };
            notify.Controls.Add(_mutedAppsBtn);

            _mutedAppsLabel = UiFactory.Caption("", 214, 220);
            notify.Controls.Add(_mutedAppsLabel);

            _notifyStatusLabel = UiFactory.Caption("", 388, 118);
            _notifyStatusLabel.AutoSize = false;
            _notifyStatusLabel.Width = 292;
            // Two lines, not one: the running status — "● On — turn on Do Not Disturb to hide
            // Windows' own pop-ups", the one sentence that says how to stop Windows' copies — is
            // 338 px in English and up to 463 px translated, so a 20 px line cut it in EVERY
            // language. 30 px still ends above the next row (y 150).
            _notifyStatusLabel.Height = 30;
            _notifyStatusLabel.AutoEllipsis = true;
            notify.Controls.Add(_notifyStatusLabel);

            // ── Live Captions ─────────────────────────────────────────────────
            // 436, not 400: the language row below needs a line of its own, and every
            // other row in this card is already occupied edge to edge. 468 for the lyrics
            // row under it, for the same reason.
            var captions = UiFactory.Group(Localization.T("Live Captions (accessibility)"), 12, 754, 696, 468, CardIcon.Caption);

            _captionOverlayCheck = UiFactory.Check("Show Tempo's caption overlay bar when Live Captions is on", 16, 30);
            // This switch had no handler at all, so the two options that only affect the
            // BAR stayed live after it was turned off — see UpdateCaptionControlsEnabled.
            _captionOverlayCheck.CheckedChanged += (s, e) =>
            {
                UpdateCaptionControlsEnabled();
                // Same path the Captions tab's bar toggle uses, so the two surfaces
                // cannot mean different things by the same click. Guarded because the
                // funnel writes this checkbox back, and the loader sets it too.
                if (_suppressSettingsEvents || _settings == null) { return; }
                if (_settings.CaptionOverlayEnabled != _captionOverlayCheck.Checked)
                {
                    ApplyCaptionOverlayPreference(_captionOverlayCheck.Checked);
                }
            };
            captions.Controls.Add(_captionOverlayCheck);

            // Step 1: choose which engine produces the text. Order MUST match the
            // CaptionSource enum: Windows=0, Tempo=1, Auto=2.
            captions.Controls.Add(UiFactory.Label("1. Caption source:", 16, 64));
            _captionSourceCombo = UiFactory.Combo(150, 61, 250,
                Utils.Localization.T("Windows 11 Live Captions"),
                Utils.Localization.T("Tempo's Live Captions (offline)"),
                Utils.Localization.T("Auto \u2013 Tempo first, Windows fallback (recommended)"));
            _captionSourceCombo.SelectedIndexChanged += OnCaptionSourceChanged;
            // The three caption boxes share two full rows and their choices describe themselves ("Large
            // v3 (most accurate, GPU recommended)"): when chosen, a long one ends in "…" in the box, and
            // the open list (as wide as its widest choice) and the hover show it whole.
            LayoutFitter.DeclareShortenedChoices(_captionSourceCombo, "descriptive choices in a full row");
            captions.Controls.Add(_captionSourceCombo);

            // Pause-based speaker-turn labels ("Speaker 1:", "Speaker 2:") on the bar.
            _captionSpeakerCheck = UiFactory.Check("Label speakers (Speaker 1 / 2)", 420, 64);
            _captionSpeakerCheck.AutoSize = true;
            _captionSpeakerCheck.CheckedChanged += (sSp, eSp) => OnCaptionSwitchChanged();
            captions.Controls.Add(_captionSpeakerCheck);

            // Auto-start captions when a video site or game is playing with sound.
            _captionAutoStartCheck = UiFactory.Check("Auto-start for videos && games", 420, 30);
            _captionAutoStartCheck.AutoSize = true;
            _captionAutoStartCheck.CheckedChanged += (sAs, eAs) => OnCaptionSwitchChanged();
            captions.Controls.Add(_captionAutoStartCheck);

            // Step 2 (Tempo engine only): pick the speech model + see if it's ready.
            // The list is generated from WhisperModelManager.Available so every model
            // Tempo knows (Tiny → Large Turbo) shows up automatically and the combo can
            // never drift out of sync with the manager again.
            _captionModelLabel = UiFactory.Label("2. Tempo model:", 16, 98);
            captions.Controls.Add(_captionModelLabel);
            var modelLabels = new System.Collections.Generic.List<string>();
            foreach (var m in Utils.WhisperModelManager.Available)
            {
                // Translated here, because UiFactory.Combo does NOT translate its items
                // (labels, buttons and checkboxes do; combo items never have). Safe to
                // translate the display text: every reader of this combo goes through
                // SelectedIndex — WhisperModelKeyFromIndex — never through the string.
                modelLabels.Add(Utils.Localization.T(m.Label));
            }
            _captionModelCombo = UiFactory.Combo(150, 95, 220, modelLabels.ToArray());
            _captionModelCombo.SelectedIndexChanged += (s2, e2) => UpdateCaptionSourceUi();
            LayoutFitter.DeclareShortenedChoices(_captionModelCombo, "descriptive choices in a full row");
            captions.Controls.Add(_captionModelCombo);

            // Step 3 (Tempo engine only): what to listen to. Crucial when the PC has
            // no speaker - loopback can't capture system audio then, so Microphone
            // (or Auto, which falls back to mic) is the working choice.
            _captionCaptureLabel = UiFactory.Label("Listen to:", 386, 98);
            captions.Controls.Add(_captionCaptureLabel);
            _captionCaptureCombo = UiFactory.Combo(460, 95, 216,
                Utils.Localization.T("Auto (system audio, or mic if no speaker)"),
                Utils.Localization.T("System audio (needs a speaker)"),
                Utils.Localization.T("Microphone"));
            LayoutFitter.DeclareShortenedChoices(_captionCaptureCombo, "descriptive choices in a full row");
            captions.Controls.Add(_captionCaptureCombo);

            _captionModelStatus = UiFactory.Caption("", 16, 124);
            _captionModelStatus.AutoSize = false;
            _captionModelStatus.Width = 664;
            // Two lines — the model notes carry more detail now. Two lines as they measure at this scale:
            // a flat 30 was two 100% lines, and at 150% the second one (46 px of text in 45) was not drawn.
            _captionModelStatus.Height = Math.Max(30, MeasureLayout("Ag\nAg", _captionModelStatus.Font).Height);
            captions.Controls.Add(_captionModelStatus);

            // Experimental: on-device face/mouth analysis to drive speaker labels.
            _captionFaceCheck = UiFactory.Check("AI face && mouth analysis (experimental)", 16, 156);
            _captionFaceCheck.AutoSize = true;
            _captionFaceCheck.CheckedChanged += (sFa, eFa) => OnCaptionSwitchChanged();
            captions.Controls.Add(_captionFaceCheck);

            // Opt-in transcript files (writes spoken content to disk, hence opt-in).
            _captionTranscriptCheck = UiFactory.Check("Save transcripts to disk", 420, 156);
            _captionTranscriptCheck.AutoSize = true;
            _captionTranscriptCheck.CheckedChanged += (sTr, eTr) => OnCaptionSwitchChanged();
            captions.Controls.Add(_captionTranscriptCheck);

            // Appearance of the caption text (applies to both sources). Every change shows on the caption
            // bar and the transcript at once and is kept — it used to wait for Save, so picking a font
            // seemed to do nothing. The list draws each font in itself, and offers every installed text
            // font after the recommended ones (added the first time it opens, to keep the page quick).
            captions.Controls.Add(UiFactory.Label("Font:", 16, 188));
            // 181-211, centred on its label: at 185 the combo (30 tall once it has its window) ended a pixel above
            // the "Text size" box under it.
            _captionFontCombo = UiFactory.Combo(150, 181, 200,
                CaptionFontChoices());
            if (_captionFontCombo is FlatComboBox fontBox) { fontBox.ItemFont = CaptionFontPreview; }
            _captionFontCombo.DropDown += (sF, eF) => AddAllCaptionFonts();
            _captionFontCombo.SelectedIndexChanged += (sF, eF) => OnCaptionLookChanged();
            captions.Controls.Add(_captionFontCombo);

            captions.Controls.Add(UiFactory.Label("Colour:", 380, 188));
            _captionColorBtn = UiFactory.Button("Choose\u2026", 450, 183, 110, 26);
            _captionColorBtn.Click += OnPickCaptionColor;
            captions.Controls.Add(_captionColorBtn);

            captions.Controls.Add(UiFactory.Label("Text size:", 16, 220));
            _captionFontNum = UiFactory.Numeric(150, 216, 70, 10, 72, 20);
            _captionFontNum.ValueChanged += (sF, eF) => OnCaptionLookChanged();
            captions.Controls.Add(_captionFontNum);

            captions.Controls.Add(UiFactory.Label("Opacity (%):", 380, 220));
            _captionOpacityNum = UiFactory.Numeric(470, 216, 70, 10, 100, 50);
            _captionOpacityNum.ValueChanged += (sF, eF) => OnCaptionLookChanged();
            captions.Controls.Add(_captionOpacityNum);

            // How much of what was said stays readable. The bar kept three lines and
            // dropped the rest, so a phrase was gone within seconds of being spoken.
            //
            // Row 312, right-hand side — NOT row 280, where this first went. That row
            // already holds "Show audio source name on the bar" at x=16 and "Try GPU
            // engine" at x=420, so all three controls landed on top of existing ones and
            // the card rendered as overlapping text. Row 312 carries only the own-voice
            // checkbox, which ends around x=250.
            captions.Controls.Add(UiFactory.Label("Lines kept:", 300, 314));
            _captionLinesNum = UiFactory.Numeric(378, 310, 60, 1, 12, 6);
            _captionLinesNum.ValueChanged += (sLn, eLn) =>
            {
                if (_suppressSettingsEvents || _settings == null) { return; }
                _settings.CaptionMaxLines = (int)_captionLinesNum.Value;
                if (_captionOverlay != null && !_captionOverlay.IsDisposed)
                {
                    _captionOverlay.SetMaxLines(_settings.CaptionMaxLines);   // live
                }
            };
            captions.Controls.Add(_captionLinesNum);
            var linesHint = UiFactory.Caption("lines of speech kept on the bar", 446, 314);
            linesHint.AutoSize = false;
            linesHint.Width = 234;      // 446..680, the space left on this row
            linesHint.Height = 20;
            captions.Controls.Add(linesHint);

            _captionBackgroundCheck = UiFactory.Check("Show background panel", 16, 248);
            _captionBackgroundCheck.CheckedChanged += (sF, eF) => OnCaptionLookChanged();
            captions.Controls.Add(_captionBackgroundCheck);

            // Own-voice filter: skip captioning the USER's voice when it comes back
            // through the speakers (sidetone / "Listen to this device" / chat
            // monitoring). Opt-in because it opens a lightweight mic monitor (the
            // mic-in-use indicator shows) — envelope only, nothing recorded.
            // Its own row: at (420, 248) it sat ON TOP of the "Open models folder"
            // and "Download model" buttons (seen overlapping in a user screenshot).
            _captionOwnVoiceCheck = UiFactory.Check("Ignore my own voice (uses your mic)", 16, 312);
            _captionOwnVoiceCheck.AutoSize = true;
            _captionOwnVoiceCheck.CheckedChanged += (sOv, eOv) =>
            {
                if (_suppressSettingsEvents) { return; }
                _settings.CaptionFilterOwnVoice = _captionOwnVoiceCheck.Checked;
                ApplyOwnVoiceGuardLive();
            };
            captions.Controls.Add(_captionOwnVoiceCheck);

            // The "♪ App ·" tag on the caption bar: some users want to know where
            // the audio comes from, others call it clutter — so it's their choice.
            _captionSourceTagCheck = UiFactory.Check("Show audio source name on the bar (♪ YouTube ·)", 16, 280);
            _captionSourceTagCheck.AutoSize = true;
            _captionSourceTagCheck.CheckedChanged += (sTag, eTag) =>
            {
                if (_suppressSettingsEvents) { return; }
                _settings.CaptionShowSourceTag = _captionSourceTagCheck.Checked;
            };
            captions.Controls.Add(_captionSourceTagCheck);

            // Opt-in GPU speech engine (Vulkan): the only GPU path for AMD/Intel
            // cards, and worth a try on NVIDIA. Proven CPU engine stays the default;
            // if the GPU can't keep pace, that SESSION uses Windows Live Captions and
            // this stays on (see OnTempoModelTooSlow — a busy GPU is not a broken one).
            _captionGpuCheck = UiFactory.Check("Try GPU engine (experimental · needs restart)", 420, 280);
            _captionGpuCheck.AutoSize = true;
            _captionGpuCheck.CheckedChanged += (sGpu, eGpu) =>
            {
                if (_suppressSettingsEvents) { return; }
                _settings.CaptionTryGpu = _captionGpuCheck.Checked;
                // The speech engine's CPU/GPU choice is fixed the first time a model
                // loads and CANNOT change until Tempo restarts. Ticking this box while
                // captions had already run therefore did NOTHING, silently — you'd sit
                // there wondering why the GPU never sped anything up. Say so, and offer
                // to do the restart right now.
                PromptGpuRestartIfNeeded();
            };
            captions.Controls.Add(_captionGpuCheck);

            // Device pickers: which SPEAKER captions listen through (loopback) and
            // which MICROPHONE the mic mode uses. Every active device shows by its
            // model name (with an honest "Unknown … (model not reported)" when the
            // driver hides it); "Default" follows Windows, the old behaviour. The
            // lists refresh live as devices come and go.
            captions.Controls.Add(UiFactory.Label("🔊", 16, 343));
            _speakerDeviceCombo = UiFactory.Combo(44, 340, 290, "Default (follow Windows)");
            _speakerDeviceCombo.SelectedIndexChanged += (sDev, eDev) => OnCaptionDeviceChosen(true);
            captions.Controls.Add(_speakerDeviceCombo);

            captions.Controls.Add(UiFactory.Label("🎙", 352, 343));
            _micDeviceCombo = UiFactory.Combo(380, 340, 296, "Default (follow Windows)");
            _micDeviceCombo.SelectedIndexChanged += (sDev, eDev) => OnCaptionDeviceChosen(false);
            captions.Controls.Add(_micDeviceCombo);

            // Warnings only (no speaker at all, chosen device missing…); empty
            // when everything is healthy — the pickers themselves show the models.
            _audioDeviceStatus = UiFactory.Caption("", 16, 370);
            _audioDeviceStatus.AutoSize = false;
            _audioDeviceStatus.Width = 664;
            _audioDeviceStatus.Height = 22;
            captions.Controls.Add(_audioDeviceStatus);

            // Spoken language. Auto-detect is the default and stays first, but on a game
            // mix it frequently never settles — so pinning is the useful option and it
            // belongs where the user can find it, not only on the Captions tab.
            // "Spoken language", not "Language" — the Appearance card already has a
            // "Language" control for Tempo's OWN interface, and two settings a page
            // apart both called Language is a trap.
            _captionLangLabel = UiFactory.Label("Spoken language:", 16, 401);
            captions.Controls.Add(_captionLangLabel);
            _captionLangCombo = UiFactory.Combo(150, 398, 220, CaptionLanguageLabels());
            _captionLangCombo.SelectedIndexChanged += (sLang, eLang) =>
            {
                if (_suppressSettingsEvents || _settings == null) { return; }
                ApplyCaptionLanguage(CaptionLanguageCodeFromIndex(_captionLangCombo.SelectedIndex));
            };
            captions.Controls.Add(_captionLangCombo);
            // Kept short enough to FIT its 294px: the previous wording ran past the end
            // of the card and rendered clipped at "…on game and".
            _captionLangHint = UiFactory.Caption(
                "Steadier than auto-detect on game audio.", 386, 401);
            _captionLangHint.AutoSize = false;
            _captionLangHint.Width = 294;
            _captionLangHint.Height = 22;
            captions.Controls.Add(_captionLangHint);

            // Lyrics: the voice detection (Silero VAD) skips sound with no voice in it, and
            // singing can be heard as "no voice" — so the user decides whether music gets
            // through. A row of its own, under the language. Applies to a running session at
            // once and is saved at once, like the other switches that act immediately.
            _captionLyricsCheck = UiFactory.Check("Caption song lyrics (lets music through, so more stray words)", 16, 432);
            _captionLyricsCheck.AutoSize = true;
            _captionLyricsCheck.CheckedChanged += (sLy, eLy) =>
            {
                if (_suppressSettingsEvents || _settings == null) { return; }
                _settings.CaptionLyrics = _captionLyricsCheck.Checked;
                ApplyCaptionLyricsLive();
                PersistNotificationSettings();   // the shared "save settings now" helper
            };
            captions.Controls.Add(_captionLyricsCheck);

            // Same menu as the Captions tab's "Models…" button — one place to change, and
            // the two surfaces cannot drift apart on what a model source can be.
            var openModelsBtn = UiFactory.Button("Models folder…", 356, 244, 156, 28);
            openModelsBtn.Click += (s3, e3) => ShowModelSourceMenu(openModelsBtn);
            captions.Controls.Add(openModelsBtn);

            // One-click model install — the "skip the manual folder step" path.
            _captionDownloadModelBtn = UiFactory.PrimaryButton("Download model", 520, 244, 156, 28, _theme);
            _captionDownloadModelBtn.Click += OnDownloadCaptionModel;
            captions.Controls.Add(_captionDownloadModelBtn);

            // Troubleshooting: does Windows 11's own Live Captions bar work with Tempo here, and
            // if not, the button that fixes it (CaptionDiagnosticForm). Placed between the
            // background-panel check and the model buttons, on the same row.
            var captionDiagBtn = UiFactory.Button("Diagnose Windows bar", 178, 244, 172, 28);
            captionDiagBtn.Click += (sD, eD) => ShowCaptionDiagnostic();
            captions.Controls.Add(captionDiagBtn);

            // ── Data ───────────────────────────────────────────────────────────
            // 232, not 164: the tamper check adds a row of actions and a status line.
            // Everything below — Window & Display, the movement card in its own method,
            // the button row and the notes under it — shifts down by the same 68.
            // 266, not 232: a row for restore points under the tamper check (its final height is measured below).
            var data = UiFactory.Group(Localization.T("Data & Backup"), 12, 1202, 696, 266, CardIcon.Folder);

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

            // Real-time view of everything Tempo's caption stack reports: engine,
            // model, backlog, devices, speaker verdicts and the live event log.
            _liveDebugBtn = UiFactory.Button("Live debug…", 16, 126, 170, 28);
            _liveDebugBtn.Click += (sDbg, eDbg) => OpenLiveDebug();
            data.Controls.Add(_liveDebugBtn);

            var debugHint = UiFactory.Caption(
                Localization.T("Live engine stats and event stream — great for bug reports."), 196, 132);
            debugHint.ForeColor = _theme.TextMuted;
            debugHint.AutoSize = false;
            debugHint.Width = 480;
            debugHint.Height = 16;
            data.Controls.Add(debugHint);

            // ── Is this still the Tempo that was installed? ────────────────────
            _integrityCheck = UiFactory.Toggle("Check that Tempo hasn't been tampered with", 16, 160);
            _integrityCheck.AutoSize = true;
            _integrityCheck.CheckedChanged += (s, e) =>
            {
                if (_suppressSettingsEvents) { return; }
                _settings.IntegrityCheckEnabled = _integrityCheck.Checked;
                SettingsManager.Save(_settings);
                RefreshIntegrityStatus();
            };
            data.Controls.Add(_integrityCheck);

            var recheckBtn = UiFactory.Button("Check now", 396, 156, 140, 28);
            recheckBtn.Click += OnRecheckIntegrity;
            data.Controls.Add(recheckBtn);

            // Without this the warning is permanent and unactionable: someone who
            // replaced the exe on purpose — a self-built copy, a sideloaded update —
            // would be told forever that Tempo had been tampered with, and the only
            // way to stop it would be to switch the whole check off.
            _integrityTrustBtn = UiFactory.Button("Trust this copy", 546, 156, 140, 28);
            _integrityTrustBtn.Click += OnTrustThisCopy;
            data.Controls.Add(_integrityTrustBtn);

            // The one sentence, with everything behind it a click away: the Details window lists each layer
            // of the check, the file's hash and Official ID, and where it came from. The sentence is as tall
            // as the longest one any verdict can produce in this language, so no verdict is ever cut short.
            _integrityDetailsBtn = UiFactory.Button("Details…", 546, 190, 140, 28);
            _integrityDetailsBtn.Click += (sD, eD) => ShowIntegrityDetails();
            data.Controls.Add(_integrityDetailsBtn);

            _integrityStatusLabel = UiFactory.Caption("", 16, 192);
            _integrityStatusLabel.AutoSize = false;
            _integrityStatusLabel.Width = 520;
            _integrityStatusLabel.Height = Math.Max(32, LongestIntegrityStatusHeight(_integrityStatusLabel.Font, 520));
            _integrityStatusLabel.ForeColor = _theme.TextMuted;
            data.Controls.Add(_integrityStatusLabel);
            int afterIntegrity = Math.Max(_integrityStatusLabel.Bottom, _integrityDetailsBtn.Bottom) + 8;

            // ── Restore points ─────────────────────────────────────────────────
            // Tempo takes one by itself before a new version first touches the data. A backup nobody can
            // get back from is only a folder, so the way back sits right here beside "Back up all data".
            var restoreBtn = UiFactory.Button("Restore points…", 16, Math.Max(228, afterIntegrity), 170, 28);
            // In the window's units (MeasureLayout): this is built before the window is scaled.
            FitButtonToText(restoreBtn, 170, 28);
            restoreBtn.Click += OnRestorePointsClicked;
            data.Controls.Add(restoreBtn);

            var restoreHint = UiFactory.Caption(
                Localization.T("Tempo saves your data automatically before each update."), restoreBtn.Right + 10, restoreBtn.Top + 6);
            restoreHint.ForeColor = _theme.TextMuted;
            restoreHint.AutoSize = false;
            restoreHint.Width = 680 - restoreHint.Left;
            restoreHint.Height = Math.Max(16, MeasureLayout(restoreHint.Text, restoreHint.Font, restoreHint.Width, TextFormatFlags.Default).Height);
            data.Controls.Add(restoreHint);
            data.Height = Math.Max(266, Math.Max(restoreBtn.Bottom, restoreHint.Bottom) + 10);

            // ── Window & Display ───────────────────────────────────────────────
            // 152, not 88: this card held one row and half of it was empty, while the
            // two settings that most belong in it lived in other cards entirely.
            var windowGroup = UiFactory.Group(Localization.T("Window & Display"), 12, 1450, 696, 152, CardIcon.Gauge);

            windowGroup.Controls.Add(UiFactory.Label("Window opacity", 16, 34));
            // SmoothTrackBar, not the framework TrackBar: a raw TrackBar swallows the mouse
            // wheel whenever the pointer is over it, so scrolling THIS page silently dragged
            // the window's opacity instead of scrolling — and it painted as a grey native
            // slider among themed ones. SmoothTrackBar bubbles the wheel to the page unless
            // it has focus, and themes itself.
            _opacitySlider = new SmoothTrackBar
            {
                Left = 150, Top = 30, Width = 300, Height = 22,
                Minimum = 50, Maximum = 100, TickFrequency = 10,
                SmallChange = 1, LargeChange = 5, Value = 100
            };
            // Scroll (user-driven) rather than ValueChanged: the load path sets the label and
            // applies the window opacity itself, so it must not also fire on a programmatic set.
            _opacitySlider.Scroll += OnOpacityChanged;
            windowGroup.Controls.Add(_opacitySlider);

            _opacityValueLabel = UiFactory.Label("100%", 460, 34);
            _opacityValueLabel.AutoSize = false;
            _opacityValueLabel.Width = 60;
            windowGroup.Controls.Add(_opacityValueLabel);

            var resetPosBtn = UiFactory.Button("Reset window position", 540, 28, 140, 30);
            resetPosBtn.Click += OnResetWindowPosition;
            windowGroup.Controls.Add(resetPosBtn);

            // ── Row 2: the two window settings that were living in other cards ──
            _alwaysOnTopCheck = UiFactory.Check("Always on top", 16, 66);
            _alwaysOnTopCheck.AutoSize = true;
            _alwaysOnTopCheck.CheckedChanged += OnAlwaysOnTopToggled;
            windowGroup.Controls.Add(_alwaysOnTopCheck);

            _rememberWindowCheck = UiFactory.Check("Remember window position && size", 200, 66);
            _rememberWindowCheck.AutoSize = true;
            windowGroup.Controls.Add(_rememberWindowCheck);

            // ── Row 3: what Tempo is actually drawing on ────────────────────────
            // The About box has always shown this and the Settings page never did, so
            // "why is the window this size / why does it look soft" had no answer here.
            // Read-only: it describes the display, it doesn't change it.
            _displayInfoLabel = UiFactory.Caption(DescribeDisplay(), 16, 104);
            _displayInfoLabel.AutoSize = false;
            _displayInfoLabel.Width = 664;
            _displayInfoLabel.Height = 34;   // two lines when several monitors are listed
            windowGroup.Controls.Add(_displayInfoLabel);

            // ── Row 4: where the Roblox Account Manager lives ───────────────────
            // Here rather than on the Accounts page itself, because the page is behind a
            // locked vault half the time and this must be reachable either way — including
            // from a window the user has just closed.
            var acctPlacementLabel = UiFactory.Label("Roblox Accounts:", 16, 150);
            windowGroup.Controls.Add(acctPlacementLabel);

            _accountsPlacementCombo = UiFactory.Combo(150, 146, 250,
                Localization.T("As a tab in this window"),
                Localization.T("In its own window"));
            _accountsPlacementCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_suppressSettingsEvents || _updatingAccountsPlacement || _settings == null) { return; }
                var want = _accountsPlacementCombo.SelectedIndex == 1
                    ? AccountsPlacement.Window
                    : AccountsPlacement.Tab;
                if (_settings.AccountsPlacement == want) { return; }
                _settings.AccountsPlacement = want;
                SaveSettingsQuietly();
                ApplyAccountsPlacement();
                RefreshSidebarSelection();   // the Accounts row's ↗
            };
            windowGroup.Controls.Add(_accountsPlacementCombo);

            var acctPlacementHint = UiFactory.Caption(
                Localization.T("A separate window can sit beside your game and stay open while Tempo is minimised."),
                410, 152);
            acctPlacementHint.AutoSize = false;
            acctPlacementHint.Width = 274;
            // As tall as its words: 30 was two 100% lines, and at 150% (or in a longer language) the
            // last line did not fit and was not drawn at all.
            acctPlacementHint.Height = Math.Max(30, MeasureLayout(acctPlacementHint.Text, acctPlacementHint.Font,
                                                                  acctPlacementHint.Width, TextFormatFlags.Default).Height);
            windowGroup.Controls.Add(acctPlacementHint);
            windowGroup.Height = Math.Max(196, acctPlacementHint.Bottom + 12);

            // ── Camera-relative movement ───────────────────────────────────────
            var movement = BuildMovementGroup();

            // ── Buttons ────────────────────────────────────────────────────────
            // Placed below the last card. This row and everything under it used to be
            // pinned by hand, and was left behind twice while the Live Captions card grew
            // — burying Save/About/Check-for-updates UNDER that card for every user.
            // They are now the "tail": CaptureSettingsLayout records the gap each one
            // keeps from the bottom of the card stack, and they follow it from then on.
            // So the Y here is measured from the lowest card's bottom, 12 below it. It was a
            // fixed 1866 — 12 below where the last card used to end — and the recorded "gap"
            // came out negative the moment that card grew: the whole row sat 24 px inside it.
            int tail0 = 0;
            foreach (Control card in new Control[] { appearance, startup, behaviour, notify, captions, data, windowGroup, movement })
            {
                if (card.Bottom > tail0) { tail0 = card.Bottom; }
            }
            tail0 += 12;
            _saveSettingsBtn = UiFactory.PrimaryButton("Save Settings", 12, tail0, 150, 36, _theme);
            // It reads "Saved ✓" for a moment after a save (ConfirmOnButton): measured both ways.
            LayoutFitter.DeclareCaptions(_saveSettingsBtn, Localization.T("Save Settings"), Localization.T("Saved  ✓"));
            _saveSettingsBtn.Click += OnSaveSettings;

            var aboutBtn = UiFactory.Button("About…", 172, tail0, 120, 36);
            aboutBtn.Click += (s, e) => OpenAbout();

            var checkUpdatesBtn = UiFactory.Button("Check for updates", 302, tail0, 160, 36);
            checkUpdatesBtn.Click += OnCheckForUpdatesClicked;
            // Kept so it can say "Checking…" for ANY check — the automatic one and the tray's too.
            _checkUpdatesBtn = checkUpdatesBtn;
            LayoutFitter.DeclareCaptions(checkUpdatesBtn, Localization.T("Check for updates"), Localization.T("Checking…"));

            var resetBtn = UiFactory.Button("Reset to defaults", 560, tail0, 148, 36);
            resetBtn.Click += OnResetSettings;

            page.Controls.Add(appearance);
            page.Controls.Add(startup);
            page.Controls.Add(behaviour);
            page.Controls.Add(notify);
            page.Controls.Add(captions);
            page.Controls.Add(data);
            page.Controls.Add(windowGroup);
            page.Controls.Add(movement);
            page.Controls.Add(_saveSettingsBtn);
            page.Controls.Add(aboutBtn);
            page.Controls.Add(checkUpdatesBtn);
            page.Controls.Add(resetBtn);

            // "Last checked" sits BELOW the buttons row now (it used to hide behind
            // the Save button), and the notes stack under it without overlapping.
            _lastCheckedLabel = UiFactory.Caption("", 12, tail0 + 44);
            _lastCheckedLabel.AutoSize = false;
            _lastCheckedLabel.Width = 696;
            _lastCheckedLabel.Height = 18;
            // "Update available: v…" opens the update it names.
            _lastCheckedLabel.Click += OnLastCheckedLabelClick;
            page.Controls.Add(_lastCheckedLabel);
            UpdateLastCheckedLabel();

            // How this copy of Tempo is deployed. An installed copy needs no note. A portable copy
            // gets the note AND the way out of being portable — because the note on its own only
            // ever said "run install.cmd", and that is how so many copies came to be missing from
            // Control Panel and Settings › Apps. A copy beside an existing install, or one running
            // out of a zip, is told why it will not install instead of being offered something that
            // would go wrong.
            int tailY = tail0 + 66;
            Utils.DeploymentKind deployKind = Utils.DeploymentInfo.Kind;
            string deployNote = null;
            switch (deployKind)
            {
                case Utils.DeploymentKind.Portable:
                    deployNote = Utils.DeploymentInfo.PortableNoteText;   // UiFactory translates it
                    break;
                case Utils.DeploymentKind.PortableWithInstalledCopy:
                    // Formatted here, AFTER translating the template: UiFactory would otherwise look up
                    // the finished sentence, and no table holds a key with a real folder path in it.
                    deployNote = Localization.F(Utils.DeploymentInfo.SeparateCopyNoteText,
                                                Utils.DeploymentInfo.InstalledDirectory);
                    break;
                case Utils.DeploymentKind.Transient:
                    deployNote = Utils.DeploymentInfo.TransientNoteText;
                    break;
            }
            if (deployNote != null)
            {
                var portable = UiFactory.Caption(deployNote, 12, tailY);
                portable.ForeColor = Theme.Readable(_theme.Warning, _theme.InputBackground);
                portable.AutoSize = false;
                portable.Width = 700;
                // Measured rather than a fixed 64: every translation runs longer than the English,
                // and a caption cut off at its last line loses exactly the sentence that says what to
                // do. The layout suite checks overlaps and button captions, not a label's last line.
                // In the window's units (MeasureLayout): built before the window is scaled.
                int needed = MeasureLayout(portable.Text, portable.Font, portable.Width, TextFormatFlags.Default).Height;
                portable.Height = Math.Max(32, needed + 4);
                page.Controls.Add(portable);
                tailY += portable.Height + 6;

                if (deployKind == Utils.DeploymentKind.Portable)
                {
                    var installBtn = UiFactory.Button("Install Tempo on this PC…", 12, tailY, 220, 30);
                    // Both captions first, then as wide as the wider, in the window's units.
                    LayoutFitter.DeclareCaptions(installBtn, installBtn.Text, Localization.T("Installing…"));
                    FitButtonToText(installBtn, 220, 36);
                    installBtn.Click += OnInstallTempoClicked;
                    page.Controls.Add(installBtn);
                    _installTempoBtn = installBtn;
                    _installTempoBtnText = installBtn.Text;
                    tailY += installBtn.Height + 10;
                }
            }

            // The rest of the tail follows whatever the note above actually took up.
            int privacyY = Math.Max(tail0 + 134, tailY);
            int privacyBottom = BuildPrivacyPanel(page, privacyY);

            var asmVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            string verText = asmVer != null
                ? $"Tempo v{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}"
                : "Tempo";
            // Build ID beside the version: the version is shared by a test build and the
            // release it came from, so on its own it cannot tell you which this is. With its badge:
            // the bare ID left off the "TEST" every other screen shows, so this line — the last thing on
            // Settings — looked the same for a test build and the release.
            if (!Utils.BuildInfo.IsUnstamped) { verText += "   ·   " + Utils.BuildInfo.Short; }
            // True bottom of the page — the old y=724 had ended up BEHIND the cards
            // as the page grew over the releases. Set after construction: the factory looks its
            // text up as a phrase, and a version number is not one (it counted as untranslated).
            _versionLine = UiFactory.Label("", 12, privacyBottom + 12, FontStyle.Regular, 9.25f);
            _versionLine.Font = new Font("Segoe UI Semibold", 9.25f);
            _versionLine.Text = verText;
            page.Controls.Add(_versionLine);

            // What the build IS, said in words and colour — "TEST" at the end of the line above was
            // easy to miss and meant nothing to someone who has not met a test build before.
            _buildStatus = UiFactory.Label("", 12, privacyBottom + 12 + 22, FontStyle.Regular, 9.25f);
            _buildStatus.UseMnemonic = false;
            page.Controls.Add(_buildStatus);
            RefreshPrivacyPanel();

            // Last thing in the method, on purpose: anything added after this is not in
            // the tail and gets left behind the moment a card above it changes height.
            CaptureSettingsLayout(page);
            RestackSettingsCards();

            // Kept as a guard, not as the fix. Cards used to be positioned by hand and
            // one of them (the movement card) is built in its own method, so resizing a
            // card silently pushed it under the next one — hiding that card's title and
            // half its first row. Nothing failed; it just looked wrong, and only in a
            // screenshot. The restack above makes that impossible, so this should now
            // never fire; if it ever does, the stacker is wrong and wants knowing about.
            AssertNoCardOverlap(page);

            _tabs.TabPages.Add(page);

            // SPIKE: lay the HTML page over the finished tab. Everything underneath still
            // exists, so LoadSettingsIntoUi and the rest of the app find the controls
            // they expect and nothing else has to learn about this.
            if (_settings != null && _settings.ExperimentalWebUi)
            {
                var host = new WebSettingsHost(_theme, _settings, () =>
                {
                    // Put whatever the page changed into effect, as the WinForms
                    // handlers would have.
                    try { ReassertTopMost(); } catch { }
                    try { Opacity = (double)Clamp(_settings.WindowOpacity, 50, 100) / 100.0; } catch { }
                    try { Utils.AnimatedLogo.SetEnabled(_settings.AnimateCustomLogo); } catch { }
                });
                page.Controls.Add(host);
                host.BringToFront();
                host.Start();
                Utils.Logger.Info("[WebUI] Settings tab is rendering from HTML (ExperimentalWebUi).");
            }
        }

        // ── The privacy panel and the version at the foot of Settings ────────────────────────────

        private InfoCallout _privacyCallout;
        private Label _privacyTitle, _privacyBody, _privacyLead, _versionLine, _buildStatus;
        private static readonly Color PrivacyBlue = Color.FromArgb(92, 168, 255);
        private static readonly Color PrivacyViolet = Color.FromArgb(178, 140, 255);
        private static readonly Color PrivacyRose = Color.FromArgb(255, 122, 170);
        private readonly System.Collections.Generic.List<(Label Name, Label Text, Func<Theme, Color> Hue)> _privacyRows =
            new System.Collections.Generic.List<(Label, Label, Func<Theme, Color>)>();

        /// <summary>
        /// The privacy statement at the foot of Settings, as a tinted panel: what stays on this PC, then
        /// each thing that goes online — what it is, when, and where it is switched off. Returns its bottom.
        ///
        /// It was one muted 8.25 pt caption: small, thin and grey (on Monokai, #9B9B8D on #272822), which
        /// people said they could not read. And it was no longer true. "The only network use is the update
        /// check" left out the release check — IntegrityCheck asks GitHub once per version with a switch
        /// of its own, so it still ran with "Check for updates" off — and everything that goes online when
        /// used: speech models, a logo from a web address, the Roblox account manager.
        /// </summary>
        private int BuildPrivacyPanel(Control page, int top)
        {
            const int width = 696, pad = 16, gap = 14;
            int inner = width - pad * 2;
            var callout = new InfoCallout
            {
                ThemeSource = () => _theme,
                // Green: the panel is good news, and it reads apart from the amber warnings above it.
                Tone = t => t.SuccessText,
                Left = 12,
                Top = top,
                Width = width,
            };
            _privacyCallout = callout;

            var titleFont = new Font("Segoe UI Semibold", 10.5f);
            var nameFont = new Font("Segoe UI Semibold", 9.25f);
            Font bodyFont = UiFactory.FontFor(9.25f, FontStyle.Regular);

            int y = 11;
            Label Add(string english, Font font, int x, int w)
            {
                Label l = UiFactory.Label(english, x, y, FontStyle.Regular, 9.25f);
                l.Font = font;
                l.UseMnemonic = false;
                l.AutoSize = false;
                l.Width = w;
                // Measured in the window's units, so it wraps in every language with no last line cut off.
                l.Height = MeasureLayout(l.Text, font, w).Height + 2;
                callout.Controls.Add(l);
                return l;
            }

            // Through T() at each site as well as in the factory, so the translation audit sees every phrase.
            _privacyTitle = Add(Localization.T("🔒 Privacy: your data stays on this PC"), titleFont, pad, inner);
            y = _privacyTitle.Bottom + 4;
            _privacyBody = Add(Localization.T("Your clicks, macros, profiles, statistics and settings are stored only on this PC. "
                               + "There is no sign-up, no tracking and no analytics."), bodyFont, pad, inner);
            y = _privacyBody.Bottom + 8;
            _privacyLead = Add(Localization.T("Tempo goes online only for these, and each can be switched off or simply left unused:"),
                               bodyFont, pad, inner);
            y = _privacyLead.Bottom + 6;

            // Name | what it does, when, and where it is switched off. The names column is as wide as the
            // widest name in this language, so the descriptions line up.
            // Each name its own hue, the same in every theme: blue, green, rose, amber, violet. The theme's accent
            // was used at first, and Monokai's accent IS its green — three green names and an amber one.
            // RefreshPrivacyPanel fits each hue to the panel (Theme.Readable keeps the hue, moves lightness).
            var rows = new (string Name, string Text, Func<Theme, Color> Hue)[]
            {
                (Localization.T("Update check"),
                 Localization.T("Asks GitHub whether a newer version is out. Switch it off with “Check for updates” in Behaviour."),
                 t => PrivacyBlue),
                (Localization.T("Release check"),
                 Localization.T("Compares a new release with the copy GitHub published, once per version. "
                 + "Switch it off with “Check that Tempo hasn't been tampered with” in Data & Backup."),
                 t => t.SuccessText),
                (Localization.T("Roblox update check"),
                 Localization.T("Asks Roblox which client version it hands out and which it has uploaded since — what Roblox itself "
                 + "reads — every two hours while Roblox is installed. Nothing about you or your accounts is sent. "
                 + "Switch it off with “Check for Roblox updates” in Behaviour."),
                 t => PrivacyRose),
                (Localization.T("When you use them"),
                 Localization.T("Downloading a speech model (from Hugging Face), setting a logo from a web address, and the Roblox "
                 + "account manager, which signs in, checks sessions and launches games through Roblox."),
                 t => t.WarningText),
                (Localization.T("Local API"),
                 Localization.T("Answers only programs on this PC. Nothing on your network or the internet can reach it."),
                 t => PrivacyViolet),
            };
            int nameWidth = 0;
            foreach (var r in rows)
            {
                nameWidth = Math.Max(nameWidth, MeasureLayout(r.Name, nameFont).Width + 4);
            }
            nameWidth = Math.Min(nameWidth, inner / 3);
            foreach (var r in rows)
            {
                Label name = Add(r.Name, nameFont, pad, nameWidth);
                Label text = Add(r.Text, bodyFont, pad + nameWidth + gap, inner - nameWidth - gap);
                _privacyRows.Add((name, text, r.Hue));
                y = Math.Max(name.Bottom, text.Bottom) + 6;
            }

            callout.Height = y + 6;
            page.Controls.Add(callout);
            return callout.Bottom;
        }

        /// <summary>
        /// Colours the privacy panel and the version lines for the current theme, and words the build
        /// status. Called after every theming pass (ThemeManager gives a label its role's colour) and when
        /// the integrity verdict arrives.
        ///
        /// Body text is held to 7:1 (WCAG AAA) on the panel and the page, and every coloured name and
        /// status line to 4.5:1 — the colour keeps its hue and only its lightness moves (Theme.Readable).
        /// </summary>
        private void RefreshPrivacyPanel()
        {
            try
            {
                Theme t = _theme;
                if (t == null || _privacyCallout == null || _privacyCallout.IsDisposed) { return; }
                Color surface = _privacyCallout.SurfaceFor(t);
                foreach (Control c in _privacyCallout.Controls) { c.BackColor = surface; }
                _privacyTitle.ForeColor = _privacyCallout.InkFor(t);
                _privacyBody.ForeColor = Theme.Readable(t.Text, surface, 7.0);
                _privacyLead.ForeColor = Theme.Readable(t.TextMuted, surface, 7.0);
                foreach (var row in _privacyRows)
                {
                    row.Name.ForeColor = Theme.Readable(row.Hue(t), surface, 4.5);
                    row.Text.ForeColor = Theme.Readable(t.Text, surface, 7.0);
                }
                _privacyCallout.Invalidate();

                if (_versionLine == null || _buildStatus == null) { return; }
                Color page = t.Background;
                _versionLine.ForeColor = Theme.Readable(t.Text, page, 7.0);

                // What this build is, as far as it is KNOWN: "official" only once GitHub has confirmed this
                // exact file — the rule the sidebar footer follows.
                string status;
                Color ink;
                string officialId = Utils.IntegrityCheck.OfficialId;
                if (Utils.BuildInfo.IsUnstamped)
                {
                    status = Localization.T("⚠ Development build: it was never published.");
                    ink = t.WarningText;
                }
                else if (Utils.BuildInfo.IsTest)
                {
                    status = Localization.T("⚠ Test build: made for trying out changes, not an official release.");
                    ink = t.WarningText;
                }
                else if (officialId != null)
                {
                    status = Localization.T("✓ Official release") + "   ·   " + officialId;
                    ink = t.SuccessText;
                }
                else if (Utils.IntegrityCheck.IsProblem)
                {
                    status = Localization.T("⚠ This copy is not the published release. See “Check that Tempo hasn't been tampered with” in Data & Backup.");
                    ink = t.DangerText;
                }
                else
                {
                    status = Localization.T("Release build, not yet confirmed against GitHub.");
                    ink = t.TextMuted;
                }
                _buildStatus.AutoSize = false;
                _buildStatus.Width = L(696);
                _buildStatus.Text = status;
                // The last thing on the page: the room under its words is where the scrolling stops, so it
                // doesn't end flush against the status bar.
                _buildStatus.Height = MeasureLayout(status, _buildStatus.Font, _buildStatus.Width).Height + 2 + L(16);
                _buildStatus.ForeColor = Theme.Readable(ink, page, ink == t.TextMuted ? 7.0 : 4.5);
            }
            catch (Exception ex) { Utils.Logger.Swallow("RefreshPrivacyPanel", ex); }
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
                _followSystemThemeCheck.Checked = _settings.FollowSystemTheme;
                _themeCombo.Enabled = !_settings.FollowSystemTheme;
                _alwaysOnTopCheck.Checked = _settings.AlwaysOnTop;
                _customAccentCheck.Checked = _settings.CustomAccentEnabled;
                _animateLogoCheck.Checked = _settings.AnimateCustomLogo;
                _logoSpeedCombo.SelectedIndex = LogoSpeedIndexFor(_settings.LogoAnimationSpeed);
                _logoSpeedCombo.Enabled = _settings.AnimateCustomLogo;
                UpdateAnimateLogoNote();

                _hdrCompensateCheck.Checked = _settings.CompensateForHdr;
                // Inert on an SDR machine — say so by greying it rather than offering a
                // switch that changes nothing.
                _hdrCompensateCheck.Enabled = Utils.DisplayColorInfo.SdrIsToneMapped;
                RefreshDisplayColourNote();

                int langIndex = (int)_settings.Language;
                if (langIndex < 0 || langIndex >= _languageCombo.Items.Count) langIndex = 0;
                _languageCombo.SelectedIndex = langIndex;

                _minimizeToTrayCheck.Checked = _settings.MinimizeToTrayOnClose;
                _startMinimizedCheck.Checked = _settings.StartMinimizedToTray;
                _traySleepCheck.Checked = _settings.TraySleepEnabled;
                if (_bgDimTrack != null)
                {
                    int d = Math.Max(0, Math.Min(90, _settings.BackgroundDim));
                    _bgDimTrack.Value = d;
                    if (_bgDimLabel != null) { _bgDimLabel.Text = d + "%"; }
                }

                if (_secondCursorEnableCheck != null)
                {
                    _secondCursorEnableCheck.Checked = _settings.SecondCursorEnabled;
                }
                if (_secondMouseUseCheck != null)
                {
                    _secondMouseUseCheck.Checked = _settings.SecondCursorUsePhysicalMouse;
                }
                _mouseComboSig = "";   // force the picker to rebuild against the loaded choice
                RefreshMiceUi();
                _captionOverlayCheck.Checked = _settings.CaptionOverlayEnabled;
                _captionSpeakerCheck.Checked = _settings.CaptionSpeakerTurns;
                if (_captionOwnVoiceCheck != null)
                {
                    _captionOwnVoiceCheck.Checked = _settings.CaptionFilterOwnVoice;
                }
                if (_captionLyricsCheck != null)
                {
                    _captionLyricsCheck.Checked = _settings.CaptionLyrics;
                }
                _captionFaceCheck.Checked = _settings.CaptionFaceAnalysis;
                _captionTranscriptCheck.Checked = _settings.CaptionSaveTranscripts;
                _captionAutoStartCheck.Checked = _settings.CaptionAutoStart;
                _captionSourceCombo.SelectedIndex = (int)_settings.CaptionSource;
                int mi = WhisperModelIndexFromKey(_settings.CaptionModelKey);
                _captionModelCombo.SelectedIndex = mi;
                if (_captionLangCombo != null)
                {
                    _captionLangCombo.SelectedIndex =
                        CaptionLanguageIndexFromCode(_settings.CaptionLanguage);
                }
                _captionCaptureCombo.SelectedIndex = Math.Max(0, Math.Min(2, _settings.CaptionCaptureMode));
                _captionFontNum.Value = Math.Max(10, Math.Min(72, _settings.CaptionFontSize));
                _captionOpacityNum.Value = Math.Max(10, Math.Min(100, _settings.CaptionOpacity));
                if (_captionLinesNum != null)
                {
                    _captionLinesNum.Value = Math.Max(1, Math.Min(12, _settings.CaptionMaxLines));
                }
                UpdateCaptionSourceUi();
                _captionBackgroundCheck.Checked = _settings.CaptionShowBackground;
                _captionSourceTagCheck.Checked = _settings.CaptionShowSourceTag;
                _captionGpuCheck.Checked = _settings.CaptionTryGpu;
                // Reflect the bar switch on load, not only when it is toggled.
                UpdateCaptionControlsEnabled();

                // Camera-relative movement. Every value is clamped into the control's
                // range: a settings file hand-edited (or written by an older build)
                // must never throw an ArgumentOutOfRange on a NumericUpDown and take
                // the whole Settings tab down with it.
                _movementEnableCheck.Checked = _settings.MovementEnabled;
                _movementFrameCombo.SelectedIndex = Math.Max(0, Math.Min(1, _settings.MovementFrame));
                _movementDegPerCountNum.Value = ClampDec((decimal)_settings.MovementDegreesPerCount, _movementDegPerCountNum);
                _movementSmoothingNum.Value = ClampDec((decimal)_settings.MovementTurnSmoothing, _movementSmoothingNum);
                _movementHysteresisNum.Value = ClampDec((decimal)_settings.MovementHysteresisDegrees, _movementHysteresisNum);
                _movementHzNum.Value = ClampDec(_settings.MovementUpdateHz, _movementHzNum);
                _movementDeadzoneNum.Value = ClampDec((decimal)_settings.MovementStickDeadzone, _movementDeadzoneNum);
                _movementPadYawNum.Value = ClampDec((decimal)_settings.MovementGamepadYawDps, _movementPadYawNum);
                _movementTurnsCombo.SelectedIndex = Math.Max(0, Math.Min(1, _settings.MovementTurnDetection));
                _movementFineCheck.Checked = _settings.MovementFineSteering;
                RefreshMovementHints();

                int cfi = _captionFontCombo.FindStringExact(_settings.CaptionFontFamily);
                if (cfi < 0 && !string.IsNullOrWhiteSpace(_settings.CaptionFontFamily))
                {
                    // A font picked from the full list (not one of the recommended ones): put it back in
                    // the list, or the box would show — and the next save write — the first font instead.
                    try
                    {
                        using (var installed = new FontFamily(_settings.CaptionFontFamily)) { }
                        cfi = _captionFontCombo.Items.Add(_settings.CaptionFontFamily);
                    }
                    catch { /* no longer installed: the first font stands in */ }
                }
                _captionFontCombo.SelectedIndex = cfi >= 0 ? cfi : 0;
                _trayNotifyCheck.Checked = _settings.ShowTrayNotifications;
                _customNotifyCheck.Checked = _settings.CustomNotifications;
                _mirrorNotifyCheck.Checked = _settings.MirrorWindowsNotifications;
                _mirrorClearCheck.Checked = _settings.MirrorClearFromActionCenter;
                _notifyScreenshotCheck.Checked = _settings.NotifyOnClipboardImage;
                _notifyCloseCheck.Checked = _settings.NotificationShowClose;
                UI.NotificationToastForm.ShowCloseButton = _settings.NotificationShowClose;
                _notifyCornerCombo.SelectedIndex = Math.Max(0, Math.Min(3, _settings.NotificationCorner));
                _notifyDurationNum.Value = ClampDec(_settings.NotificationDurationSeconds, _notifyDurationNum);
                UpdateNotifyControlsEnabled();
                RefreshNotifyStatus();
                _confirmExitCheck.Checked = _settings.ConfirmBeforeExitWhileRunning;
                _safetyEscapeCheck.Checked = _settings.SafetyStopOnEscape;
                _launchStartupCheck.Checked = _settings.LaunchAtStartup;
                _openAtSignInCheck.Checked = _settings.OpenWindowAtSignIn;
                SyncOpenAtSignInEnabled();
                _hideWhenClickingCheck.Checked = _settings.HideWhenClicking;
                _checkUpdatesCheck.Checked = _settings.CheckForUpdatesOnLaunch;
                if (_updateFreqCombo != null)
                {
                    _updateFreqCombo.SelectedIndex = Math.Max(0, Math.Min(2, _settings.UpdateCheckFrequency));
                    _updateFreqCombo.Enabled = _settings.CheckForUpdatesOnLaunch;
                }
                if (_checkRobloxUpdatesCheck != null) { _checkRobloxUpdatesCheck.Checked = _settings.CheckRobloxUpdates; }
                _writeLogCheck.Checked = _settings.WriteLogFile;
                _recordHistoryCheck.Checked = _settings.RecordSessionHistory;
                _showIndicatorCheck.Checked = _settings.ShowClickingIndicator;
                if (_startDelayBeepCheck != null) { _startDelayBeepCheck.Checked = _settings.StartDelayBeep; }
                if (_startupDelayNum != null)
                {
                    _startupDelayNum.Value = ClampDec(_settings.StartupDelaySeconds, _startupDelayNum);
                }
                _minimizeRecordingCheck.Checked = _settings.MinimizeWhileRecording;
                if (_ignoreOwnWindowCheck != null)
                {
                    _ignoreOwnWindowCheck.Checked = _settings.IgnoreOwnWindowWhileRunning;
                }
                if (_guardShortcutsCheck != null)
                {
                    _guardShortcutsCheck.Checked = _settings.GuardWindowsShortcuts;
                }
                // Its copies on the Clicker and Macros tabs and in the tray.
                SyncGuardShortcutControls();
                _rememberWindowCheck.Checked = _settings.RememberWindowPosition;
                _rememberTabCheck.Checked = _settings.RememberLastTab;
                SyncAccountsPlacementCombo();

                if (_integrityCheck != null)
                {
                    _integrityCheck.Checked = _settings.IntegrityCheckEnabled;
                }
                RefreshIntegrityStatus();

                if (_unlockSpeedCheck != null && _speedTrack != null)
                {
                    bool unlocked = _settings.AdvancedUnlockSpeed;
                    _unlockSpeedCheck.Checked = unlocked;

                    // Unlock speed must also lift the Anti-Freeze hard cap, otherwise
                    // the cap (default 200 CPS) silently clamps the unlocked rate and
                    // the feature appears to do nothing. Heals an older saved state
                    // where unlock was on but the cap was left at the default.
                    //
                    // ONLY when the cap is still that untouched default. This runs on
                    // every LoadSettingsIntoUi — startup, Reset, Import — and the old
                    // "< UnlockedMaxCps" test matched every value below 2000, so with
                    // unlock on the Anti-Freeze box could not hold ANY cap the user
                    // chose: measured, a saved 20 came back as 2000 on the next launch,
                    // and the run that followed went at 194 CPS. The sibling code in the
                    // unlock handler already gets this right in the other direction ("only
                    // if the cap is still the auto-raised value so a custom cap is kept");
                    // this is the same rule going the other way.
                    if (unlocked && _settings.MaxClicksPerSecond == NormalMaxCps)
                    {
                        _settings.MaxClicksPerSecond = UnlockedMaxCps;
                    }

                    int max = unlocked ? UnlockedMaxCps : NormalMaxCps;
                    if (_speedTrack.Value > max) _speedTrack.Value = max;
                    _speedTrack.Maximum = max;
                    _speedTrack.TickFrequency = unlocked ? 200 : 20;
                    _speedTrack.LargeChange = unlocked ? 50 : 5;
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

            // These checkboxes live on the Macros tab and are not part of the loop
            // above, so Reset-to-defaults / Import (which re-run LoadSettingsIntoUi)
            // would otherwise leave them showing stale state. Refresh them here; their
            // CheckedChanged handlers re-persist and (for the trail) re-apply live.
            if (_cursorTrailCheck != null) _cursorTrailCheck.Checked = _settings.CursorTrailEnabled;
            if (_recordMovesCheck != null) _recordMovesCheck.Checked = _settings.RecordMacroMovements;
            if (_recordCameraCheck != null)
            {
                _recordCameraCheck.Checked = _settings.RecordMacroCameraMovement;
                _recordCameraCheck.Enabled = _settings.RecordMacroMovements;
            }
            if (_recordKeysCheck != null) _recordKeysCheck.Checked = _settings.RecordMacroKeyboard;
            // Same page, same reason — this one was missed when the three above were fixed.
            if (_recordCountdownNum != null)
            {
                _recordCountdownNum.Value = Clamp(_settings.RecordCountdownSeconds, 0, 10);
            }

            // Keep the Anti-Freeze cap numeric and the engine in sync with any cap
            // raised by the unlock-speed reconciliation above (guarded so it doesn't
            // re-trigger an anti-freeze save).
            if (_maxCpsNum != null)
            {
                _suppressAntiFreeze = true;
                try { _maxCpsNum.Value = Clamp(_settings.MaxClicksPerSecond, 1, 2000); }
                finally { _suppressAntiFreeze = false; }
            }
            ApplyAntiFreezeToEngine();

            UpdateAccentControlsEnabled();
            RefreshThemePreview();
            RefreshMissedNotificationsLabel();
            RefreshMutedAppsLabel();
        }

        /// <summary>1 while a label refresh is already queued, so a burst of entries costs one.</summary>
        private int _missedLabelRefreshQueued;

        /// <summary>
        /// NotificationHistory.Changed. Raised on whichever thread added the entry — the
        /// notification mirror's listener, the notification centre, or the history window's
        /// Clear — and possibly dozens of times in a burst, so it is marshalled to the UI
        /// thread and coalesced into a single refresh.
        /// </summary>
        private void OnNotificationHistoryChanged()
        {
            if (IsDisposed || !IsHandleCreated) { return; }
            if (System.Threading.Interlocked.Exchange(ref _missedLabelRefreshQueued, 1) == 1) { return; }
            try
            {
                BeginInvoke((Action)(() =>
                {
                    System.Threading.Interlocked.Exchange(ref _missedLabelRefreshQueued, 0);
                    if (!IsDisposed) { RefreshMissedNotificationsLabel(); }
                }));
            }
            catch
            {
                // The window is going away; nothing is left to refresh.
                System.Threading.Interlocked.Exchange(ref _missedLabelRefreshQueued, 0);
            }
        }

        /// <summary>
        /// "N you didn't see" beside the history button.
        ///
        /// Worth showing without opening anything: a notification dropped during a
        /// fullscreen game leaves no other trace, so a user who never opens the history
        /// would never learn there was one.
        /// </summary>
        private void RefreshMissedNotificationsLabel()
        {
            if (_notifyMissedLabel == null) { return; }
            try
            {
                int missed = Utils.NotificationHistory.MissedCount();
                _notifyMissedLabel.Text = missed == 0
                    ? ""
                    : Utils.Localization.F("{0} you didn't see", missed);
                _notifyMissedLabel.ForeColor = missed > 0 ? _theme.WarningText : _theme.TextMuted;
            }
            catch (Exception ex) { Utils.Logger.Swallow("RefreshMissedNotificationsLabel", ex); }
        }

        /// <summary>
        /// "N app(s) muted" beside the button. A mute is otherwise invisible — the app
        /// simply stops appearing, which is indistinguishable from mirroring being broken.
        /// </summary>
        private void RefreshMutedAppsLabel()
        {
            if (_mutedAppsLabel == null) { return; }
            try
            {
                int n = _settings?.MirrorMutedApps?.Count ?? 0;
                _mutedAppsLabel.Text = n == 0 ? "" : Utils.Localization.F("{0} app(s) muted", n);
                _mutedAppsLabel.ForeColor = _theme.TextMuted;
            }
            catch (Exception ex) { Utils.Logger.Swallow("RefreshMutedAppsLabel", ex); }
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (_suppressSettingsEvents || _themeCombo.SelectedIndex < 0)
            {
                return;
            }

            // Picking a theme by hand implies you no longer want the OS to drive it.
            if (_settings.FollowSystemTheme)
            {
                _settings.FollowSystemTheme = false;
                _followSystemThemeCheck.Checked = false;
            }

            // Live-preview the theme as soon as it is chosen.
            _settings.Theme = (ThemeKind)_themeCombo.SelectedIndex;
            ApplyThemeToEverything();
            RefreshThemePreview();
        }

        private void OnFollowSystemThemeToggled(object sender, EventArgs e)
        {
            if (_suppressSettingsEvents)
            {
                return;
            }

            _settings.FollowSystemTheme = _followSystemThemeCheck.Checked;
            // The manual picker is meaningless while the OS drives the theme.
            if (_themeCombo != null) { _themeCombo.Enabled = !_settings.FollowSystemTheme; }
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
            OfferRestartForLanguage();
        }

        /// <summary>
        /// Some text is built once at startup, so a new language only appears everywhere after a
        /// restart — offer one. Shared by the language picker and Import, which can bring in a
        /// different language and used to leave the window in the old one without a word.
        /// </summary>
        private void OfferRestartForLanguage()
        {
            bool restart = AskToRestart(
                Localization.T("Language changed"),
                Localization.T("Your choice is already saved — nothing is lost either way."),
                Localization.T("Some text is built when Tempo starts, so a restart is needed for the new language to appear everywhere."));

            if (restart)
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

        /// <summary>
        /// Gates the palette correction on BOTH the user's preference and HDR being on for the
        /// screen the window is on (the primary screen while it sits in the tray). On an SDR
        /// screen there is nothing to correct, and correcting anyway would crush shadows Windows
        /// had not lifted — the bug this feature exists to fix, pointed the other way. It used to
        /// ask "is HDR on ANYWHERE?", which with one HDR screen and one SDR screen corrected the
        /// window on both. True when the answer changed, so a caller knows to re-theme.
        /// </summary>
        private bool ApplyHdrCompensationSetting()
        {
            try
            {
                bool want = _settings != null && _settings.CompensateForHdr &&
                            Utils.DisplayColorInfo.HdrOnFor(PaletteScreenName());
                bool changed = want != Theme.CompensateForHdr;
                Theme.CompensateForHdr = want;
                return changed;
            }
            catch (Exception ex)
            {
                Utils.Logger.Swallow("ApplyHdrCompensationSetting", ex);
                return false;
            }
        }

        /// <summary>The screen whose HDR state decides the palette.</summary>
        private string PaletteScreenName()
        {
            try
            {
                if (IsHandleCreated && Visible && WindowState != FormWindowState.Minimized)
                {
                    return Screen.FromHandle(Handle).DeviceName;
                }
                return Screen.PrimaryScreen?.DeviceName;
            }
            catch { return null; }
        }

        private string _paletteScreen;

        /// <summary>
        /// Re-reads the display and re-themes if the correction should now be on or off: after
        /// Windows reports a display change (HDR switched on or off, a monitor added), and when
        /// the window lands on another screen. Cheap when nothing changed — one display query.
        /// </summary>
        private void RefreshHdrPalette(bool displayChanged)
        {
            try
            {
                if (_settings == null || _shuttingDown) { return; }
                string screen = PaletteScreenName();
                if (!displayChanged && string.Equals(screen, _paletteScreen, StringComparison.OrdinalIgnoreCase)) { return; }
                _paletteScreen = screen;
                if (displayChanged) { Utils.DisplayColorInfo.Invalidate(); }

                if (ApplyHdrCompensationSetting())
                {
                    Utils.Logger.Info("[Display] HDR colour correction " + (Theme.CompensateForHdr ? "on" : "off")
                                      + " for " + (screen ?? "the primary screen") + ".");
                    ApplyThemeToEverything();
                    RefreshThemePreview();
                }
                if (_hdrCompensateCheck != null) { _hdrCompensateCheck.Enabled = Utils.DisplayColorInfo.SdrIsToneMapped; }
                RefreshDisplayColourNote();
            }
            catch (Exception ex) { Utils.Logger.Swallow("RefreshHdrPalette", ex); }
        }

        /// <summary>"Open the window at sign-in" only means something while Tempo launches at sign-in.</summary>
        private void SyncOpenAtSignInEnabled()
        {
            try
            {
                if (_openAtSignInCheck != null && _launchStartupCheck != null)
                {
                    _openAtSignInCheck.Enabled = _launchStartupCheck.Checked;
                }
            }
            catch { }
        }

        private void OnHdrCompensationToggled(object sender, EventArgs e)
        {
            if (_suppressSettingsEvents || _settings == null) { return; }
            _settings.CompensateForHdr = _hdrCompensateCheck.Checked;
            ApplyHdrCompensationSetting();
            ApplyThemeToEverything();     // live: rebuild every surface with the new palette
            RefreshThemePreview();
            RefreshDisplayColourNote();
        }

        /// <summary>
        /// The display line under "Correct colours for HDR": what the screens are doing, and
        /// what that means for Tempo right now.
        ///
        /// It was an English paragraph in a 30-pixel label — cut off after two lines, English in
        /// every language, and wrong while the correction was on, since it still told people to
        /// switch HDR off to get the right colours back. It also never mentioned the one thing
        /// that makes Tempo look dim under HDR and that only the user can change: Windows' "SDR
        /// content brightness", which this PC had at its lowest, 80 nits.
        /// </summary>
        private void RefreshDisplayColourNote()
        {
            if (_displayColourNote == null) { return; }
            try
            {
                var a = Utils.DisplayColorInfo.Advanced();
                int bpp = Utils.DisplayColorInfo.BitsPerPixel;

                var facts = new System.Collections.Generic.List<string>();
                if (a.Enabled)
                {
                    facts.Add(a.Outputs > 1
                        ? Localization.F("HDR on ({0} of {1} screens)", a.OutputsHdrOn, a.Outputs)
                        : Localization.T("HDR on"));
                    if (a.LowestSdrWhiteNits > 0)
                    {
                        facts.Add(Localization.F("SDR content brightness {0} nits", Math.Round(a.LowestSdrWhiteNits)));
                    }
                }
                else
                {
                    facts.Add(a.Supported ? Localization.T("HDR available, off") : Localization.T("SDR display"));
                }
                if (bpp > 0 && bpp < 32) { facts.Add(Localization.F("{0}-bit colour", bpp)); }

                // Each line in the colour of what it says: the facts muted, "✓" in the success
                // colour, "⚠" in the warning colour.
                var lines = new System.Collections.Generic.List<string> { Localization.T("Display:") + " " + string.Join(" · ", facts) };
                var inks = new System.Collections.Generic.List<Color> { _theme.TextMuted };
                if (a.Enabled)
                {
                    if (Theme.CompensateForHdr)
                    {
                        lines.Add("✓ " + Localization.T("Colours corrected for HDR: dark shades look as designed."));
                        inks.Add(_theme.SuccessText);
                    }
                    else if (_settings != null && !_settings.CompensateForHdr)
                    {
                        lines.Add("⚠ " + Localization.T("Not corrected: with HDR on, Windows lightens Tempo's dark colours. Tick the box to correct them."));
                        inks.Add(_theme.WarningText);
                    }
                    else
                    {
                        // Wanted but off: HDR is on for another screen, not the one the window is on.
                        lines.Add("✓ " + Localization.T("This screen is SDR, so no correction is needed here."));
                        inks.Add(_theme.TextMuted);
                    }
                    if (a.LowestSdrWhiteNits > 0 && a.LowestSdrWhiteNits < 120)
                    {
                        lines.Add("⚠ " + Localization.F("SDR content brightness is low ({0} nits), so Tempo looks dim. Raise it in Windows' HDR settings.",
                                                         Math.Round(a.LowestSdrWhiteNits)));
                        inks.Add(_theme.WarningText);
                    }
                }
                else if (bpp > 0 && bpp < 32)
                {
                    lines.Add("⚠ " + Localization.T("Gradients band at this colour depth. Choose 32-bit colour in Windows' display settings."));
                    inks.Add(_theme.WarningText);
                }

                _displayColourNote.Text = string.Join("\n", lines);
                _displayColourNote.ForeColor = _theme.TextMuted;
                _displayColourNote.LineColors = inks.ToArray();
            }
            catch (Exception ex) { Utils.Logger.Swallow("RefreshDisplayColourNote", ex); }
        }

        /// <summary>The speeds the combo offers, in the same order, as percentages.</summary>
        private static readonly int[] LogoSpeedPercents = { 25, 50, 100, 150, 200, 400 };

        private void OnLogoSpeedChanged(object sender, EventArgs e)
        {
            if (_suppressSettingsEvents || _settings == null) { return; }
            int i = _logoSpeedCombo.SelectedIndex;
            if (i < 0 || i >= LogoSpeedPercents.Length) { return; }

            _settings.LogoAnimationSpeed = LogoSpeedPercents[i];
            Utils.AnimatedLogo.SpeedPercent = _settings.LogoAnimationSpeed;
            UpdateAnimateLogoNote();     // the note quotes the loop length, which just changed
        }

        /// <summary>
        /// Picks the combo entry for a stored speed, falling back to 1× for a value that
        /// is not one of the offered ratios — a hand-edited settings file, or a speed from
        /// a build that offered different steps.
        /// </summary>
        private static int LogoSpeedIndexFor(int percent)
        {
            for (int i = 0; i < LogoSpeedPercents.Length; i++)
            {
                if (LogoSpeedPercents[i] == percent) { return i; }
            }
            return 2;   // 100%
        }

        private void OnAnimateLogoToggled(object sender, EventArgs e)
        {
            if (_suppressSettingsEvents || _settings == null)
            {
                return;
            }

            _settings.AnimateCustomLogo = _animateLogoCheck.Checked;

            // Live, not on restart: SetEnabled rebuilds or tears down the animation, and
            // the frame event puts the result on the window and the tray immediately.
            Utils.AnimatedLogo.SetEnabled(_settings.AnimateCustomLogo);
            if (_logoSpeedCombo != null) { _logoSpeedCombo.Enabled = _settings.AnimateCustomLogo; }
            RefreshAppIconEverywhere();
            UpdateAnimateLogoNote();
        }

        /// <summary>
        /// Explains what the logo is doing right now — how many frames it has and how long
        /// the loop runs, or why it is not moving.
        ///
        /// The build happens on a background thread, so this is also called from the frame
        /// event: when the page is drawn the answer is often still "reading the logo…",
        /// and a note that never resolved would be worse than no note.
        /// </summary>
        private void UpdateAnimateLogoNote()
        {
            if (_animateLogoNote == null || _animateLogoCheck == null)
            {
                return;
            }

            try
            {
                bool haveLogo = Utils.CustomLogo.Exists();

                // Nothing to animate is not the same as "off". Greying the tick with the
                // reason beside it says the feature exists and where to feed it, instead
                // of offering a switch that would visibly do nothing.
                _animateLogoCheck.Enabled = haveLogo;

                string note = haveLogo
                    ? Utils.AnimatedLogo.Status
                    : Utils.Localization.T("No custom logo — set one in About.");

                // Status quotes the loop as AUTHORED, because it is built once when the
                // frames are rendered. At any speed but 1× that number is no longer what
                // the user is watching, so say what it actually is now.
                if (haveLogo && _settings != null && _settings.LogoAnimationSpeed != 100)
                {
                    double secs = Utils.AnimatedLogo.EffectiveLoopSeconds;
                    if (secs > 0)
                    {
                        note += Utils.Localization.F("  →  {0}s at {1}%",
                            secs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                            _settings.LogoAnimationSpeed);
                    }
                }
                _animateLogoNote.Text = note;
            }
            catch (Exception ex) { Utils.Logger.Swallow("UpdateAnimateLogoNote", ex); }
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
            bool on = _customAccentCheck.Checked;
            if (_chooseAccentBtn != null) { _chooseAccentBtn.Enabled = on; }
            // The hex box edits the CUSTOM accent, so it is only meaningful while the
            // custom accent is the one in use; the swatches and the contrast figure stay
            // readable either way, because they describe whatever accent is live.
            if (_accentHexBox != null) { _accentHexBox.Enabled = on; }
        }

        /// <summary>
        /// Accepts a hex colour typed or pasted into the accent box.
        ///
        /// Tolerant on input, strict on output: "#RRGGBB", "RRGGBB" and the three-digit
        /// "#RGB" shorthand all work, because those are what people copy out of design
        /// tools and brand guides. Anything unparseable simply restores the current value
        /// rather than complaining — a half-typed colour is not an error worth a dialog.
        /// </summary>
        private void CommitAccentHex()
        {
            if (_accentHexBox == null || _settings == null || _suppressSettingsEvents) { return; }
            try
            {
                string t = (_accentHexBox.Text ?? "").Trim().TrimStart('#');
                if (t.Length == 3)
                {
                    t = string.Concat(t[0], t[0], t[1], t[1], t[2], t[2]);
                }

                if (t.Length == 6 &&
                    int.TryParse(t, System.Globalization.NumberStyles.HexNumber,
                                 System.Globalization.CultureInfo.InvariantCulture, out int rgb))
                {
                    int argb = unchecked((int)(0xFF000000u | (uint)rgb));
                    if (argb != _settings.CustomAccentArgb)
                    {
                        _settings.CustomAccentArgb = argb;
                        if (!_customAccentCheck.Checked)
                        {
                            _customAccentCheck.Checked = true;   // fires the toggle handler
                        }
                        else
                        {
                            ApplyThemeToEverything();
                        }
                    }
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("CommitAccentHex", ex); }

            RefreshThemePreview();   // always resync the box to the accent actually in use
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

            // The accent actually in force — the custom one when it is on, the theme's
            // otherwise. Everything below describes THAT colour, so the details never
            // disagree with what the app is painting. As designed, not as HDR-corrected:
            // the hex box can save what it shows, and a corrected value saved as the custom
            // accent was corrected a second time. The swatch shows it as painted.
            Color liveAccent = _settings.CustomAccentEnabled
                ? Color.FromArgb(_settings.CustomAccentArgb)
                : _theme.DesignAccent;

            if (_accentSwatch != null) { _accentSwatch.BackColor = _theme.Accent; }
            if (_accentHoverSwatch != null) { _accentHoverSwatch.BackColor = _theme.AccentHover; }

            if (_accentHexBox != null && !_accentHexBox.Focused)
            {
                // Never while the user is typing in it — rewriting the box mid-edit would
                // fight them for the caret.
                string hex = "#" + liveAccent.R.ToString("X2") + liveAccent.G.ToString("X2")
                           + liveAccent.B.ToString("X2");
                if (!string.Equals(_accentHexBox.Text, hex, StringComparison.OrdinalIgnoreCase))
                {
                    _accentHexBox.Text = hex;
                }
                _accentHexBox.BackColor = _theme.InputBackground;
                _accentHexBox.ForeColor = _theme.Text;
            }

            if (_accentContrastLabel != null)
            {
                // Contrast of a primary button's LABEL against the accent it sits on.
                // 4.5:1 is the WCAG AA bar for normal text, and these labels are bold
                // 10pt, which qualifies as large text at 3:1 — so both marks are shown
                // rather than failing a colour that is genuinely fine on a button.
                double ratio = Theme.ContrastRatio(Theme.ReadableOn(liveAccent), liveAccent);
                string verdict = ratio >= 4.5 ? "AA" : (ratio >= 3.0 ? "AA large" : "low");
                _accentContrastLabel.Text = ratio.ToString("0.0",
                    System.Globalization.CultureInfo.InvariantCulture) + ":1  " + verdict;
                _accentContrastLabel.ForeColor = ratio >= 3.0 ? _theme.TextMuted : _theme.Warning;
            }

            if (_previewButton != null)
            {
                _previewButton.BackColor = _theme.Accent;
                _previewButton.ForeColor = _theme.OnAccent;
                _previewButton.FlatAppearance.BorderSize = 0;
            }

            if (_previewSample != null)
            {
                _previewSample.BackColor = _theme.Surface;
                _previewSample.ForeColor = _theme.Text;
            }
        }

        // ── Caption source / model helpers ───────────────────────────────────
        private static int WhisperModelIndexFromKey(string key)
        {
            var list = Utils.WhisperModelManager.Available;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Key, key, System.StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return 0;
        }

        private static string WhisperModelKeyFromIndex(int index)
        {
            var list = Utils.WhisperModelManager.Available;
            if (index < 0 || index >= list.Count) index = 0;
            return list[index].Key;
        }

        private void OnCaptionSourceChanged(object sender, EventArgs e)
        {
            UpdateCaptionSourceUi();
        }

        /// <summary>
        /// Shows/hides and annotates the model picker based on the chosen source, so
        /// it's obvious which engine is active and (for Tempo's own) whether the
        /// selected model is installed and exactly what to do if it isn't.
        /// </summary>
        /// <summary>
        /// Returns the caption font names to offer, filtered to those actually
        /// installed on this PC, so every option in the picker really applies. Picking
        /// a font the system doesn't have used to silently do nothing. "Segoe UI" is
        /// always included as the safe default even if the probe somehow misses it.
        /// </summary>
        private static string[] CaptionFontChoices()
        {
            string[] preferred =
            {
                "Segoe UI", "Segoe UI Semibold", "Arial", "Verdana", "Tahoma",
                "Calibri", "Consolas", "Georgia", "Trebuchet MS", "Comic Sans MS"
            };
            // Ask about the ten names we actually offer instead of enumerating every
            // font on the machine. InstalledFontCollection builds the WHOLE system font
            // list (142 families here, ~24 ms cold) to answer ten membership questions;
            // constructing a FontFamily by name throws if it isn't installed, which is
            // the same answer for ~2 ms. This runs while the user waits for the window.
            var list = new System.Collections.Generic.List<string>();
            foreach (var name in preferred)
            {
                try
                {
                    using (var probe = new FontFamily(name))
                    {
                        list.Add(name);   // constructed => installed
                    }
                }
                catch
                {
                    // Not installed on this PC — simply don't offer it.
                }
            }
            if (list.Count == 0) list.Add("Segoe UI");
            return list.ToArray();
        }

        private void UpdateCaptionSourceUi()
        {
            if (_captionSourceCombo == null) return;
            // Both "Tempo" and "Auto" run Tempo's own engine (Auto just adds a
            // Windows fallback), so the model + capture controls apply to both.
            int idx = _captionSourceCombo.SelectedIndex;
            bool tempo = idx == (int)CaptionSource.Tempo || idx == (int)CaptionSource.Auto;

            if (_captionModelLabel != null) _captionModelLabel.Visible = tempo;
            if (_captionModelCombo != null) _captionModelCombo.Visible = tempo;
            if (_captionModelStatus != null) _captionModelStatus.Visible = tempo;
            if (_captionCaptureLabel != null) _captionCaptureLabel.Visible = tempo;
            if (_captionCaptureCombo != null) _captionCaptureCombo.Visible = tempo;
            // Language is Tempo's engine only — Windows Live Captions picks its own.
            if (_captionLangLabel != null) _captionLangLabel.Visible = tempo;
            if (_captionLangCombo != null) _captionLangCombo.Visible = tempo;
            if (_captionLangHint != null) _captionLangHint.Visible = tempo;

            if (!tempo)
            {
                return;
            }

            if (_captionModelStatus == null) return;
            var model = Utils.WhisperModelManager.FindByKey(
                WhisperModelKeyFromIndex(_captionModelCombo != null ? _captionModelCombo.SelectedIndex : 0));
            bool installed = Utils.WhisperModelManager.IsInstalled(model);

            string language = model.EnglishOnly
                ? Localization.T("English only")
                : Localization.T("understands any language (auto-detected)");

            // A chosen model FILE overrides this combo entirely, so the combo must not be
            // left implying otherwise. Reporting "Large-v3 is installed and ready" while
            // captions actually run on a file the user picked is the same class of lie as
            // naming one app on a caption built from two — the reader has no way to tell.
            string customModel = _settings != null ? (_settings.CaptionCustomModelPath ?? "") : "";
            if (customModel.Length > 0)
            {
                // Describe it the way the picker describes a built-in model — by what it
                // IS. "ggml-final2.bin" tells the user nothing; "Large v3 Turbo · any
                // language · Q5_0" tells them everything they were choosing between.
                var cf = Utils.WhisperModelManager.ReadFacts(customModel);
                _captionModelStatus.Text = cf.Valid
                    ? Localization.F("✓ Your own model file · {0} · {1}"
                        + " · the picker above is ignored until you clear it (Models folder…).",
                        cf.Headline, cf.FileName)
                    : Localization.F("⚠ {0} can't be used — {1}"
                        + " Captions will fall back to the models above.", cf.FileName, cf.Problem);
                _captionModelStatus.ForeColor = cf.Valid ? _theme.SuccessText : _theme.WarningText;
                return;
            }

            if (installed)
            {
                // Real size on disk beats the approximate note once the file exists.
                string size = "";
                try
                {
                    long bytes = new System.IO.FileInfo(Utils.WhisperModelManager.PathFor(model)).Length;
                    size = bytes >= 1024L * 1024 * 1024
                        ? (bytes / (1024.0 * 1024 * 1024)).ToString("0.0") + " GB"
                        : (bytes / (1024.0 * 1024)).ToString("0") + " MB";
                }
                catch { }
                _captionModelStatus.Text = "\u2713 " + Localization.T(model.Label) + " " + Localization.T("is installed and ready") +
                    (size.Length > 0 ? " \u00b7 " + size + " " + Localization.T("on disk") : "") +
                    " \u00b7 " + language + ".";
                _captionModelStatus.ForeColor = Theme.Readable(_theme.Success, _theme.InputBackground);
            }
            else
            {
                _captionModelStatus.Text = "\u2b07 " + Localization.T(model.Label) + " " +
                    Localization.T("isn't downloaded yet \u2014 click \u201cDownload model\u201d.") + " " +
                    Localization.T(model.Note);
                _captionModelStatus.ForeColor = Theme.Readable(_theme.Warning, _theme.InputBackground);
            }
        }

        private void OnDownloadCaptionModel(object sender, EventArgs e)
        {
            var model = Utils.WhisperModelManager.FindByKey(
                WhisperModelKeyFromIndex(_captionModelCombo != null ? _captionModelCombo.SelectedIndex : 0));

            if (Utils.WhisperModelManager.IsInstalled(model))
            {
                ShowInfo(Localization.F("{0} is already installed.", model.Label));
                UpdateCaptionSourceUi();
                return;
            }

            var confirm = MessageBox.Show(this,
                Localization.F("Download the {0} speech model now?\n\n{1}"
                + "\n\nThis downloads once into Tempo's models folder, then Tempo's own offline "
                + "captions work with no further setup.",
                Localization.T(model.Label), Localization.T(model.Note)),
                "Download speech model", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            using (var dlg = new ModelDownloadForm(_theme, model))
            {
                var dr = dlg.ShowDialog(this);
                if (dr == DialogResult.OK)
                {
                    ShowInfo(Localization.F("{0} installed. Tempo's own captions are ready to use.", model.Label));
                }
                else if (dr == DialogResult.Abort)
                {
                    ShowWarning(dlg.Error ?? Localization.T("The model download failed. You can try again, or use "
                        + "\u201cOpen models folder\u201d to add it manually."));
                }
            }
            UpdateCaptionSourceUi();
        }

        private void OnPickCaptionColor(object sender, EventArgs e)
        {
            using (var dlg = new ColorDialog
            {
                Color = Color.FromArgb(_settings.CaptionColorArgb),
                FullOpen = true,
                AnyColor = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                _settings.CaptionColorArgb = dlg.Color.ToArgb();
                _settings.CaptionUseCustomColor = true;
                // Shown at once on the caption bar AND the transcript (only the bar got it), and kept
                // without waiting for Save — like the other caption looks.
                ApplyCaptionSettingsToOverlays();
                PersistCaptionLookSoon();
            }
        }

        /// <summary>Clamps a value into a NumericUpDown's own Minimum/Maximum.</summary>
        private static decimal ClampDec(decimal v, NumericUpDown nud)
        {
            if (v < nud.Minimum) { return nud.Minimum; }
            if (v > nud.Maximum) { return nud.Maximum; }
            return v;
        }

        /// <summary>
        /// Greys out the caption options that only affect the OVERLAY BAR when the bar
        /// is switched off.
        ///
        /// Deliberately narrow. Font, text size, colour and opacity look like they
        /// belong to the bar too, but the Caption History window is styled from the very
        /// same four settings — greying those out would disable controls that are still
        /// doing their job. Only two are genuinely bar-only: the background panel is
        /// drawn by the bar alone, and the "♪ source ·" tag is added to the BAR's text
        /// and deliberately never written into history.
        /// </summary>
        private void UpdateCaptionControlsEnabled()
        {
            if (_captionOverlayCheck == null) { return; }
            bool bar = _captionOverlayCheck.Checked;
            if (_captionBackgroundCheck != null) { _captionBackgroundCheck.Enabled = bar; }
            if (_captionSourceTagCheck != null) { _captionSourceTagCheck.Enabled = bar; }
        }

        /// <summary>
        /// Greys out the notification sub-options that only make sense when the parent
        /// switch is on: everything needs the custom pop-ups, and "clear from Action
        /// Center" needs mirroring.
        /// </summary>
        private void UpdateNotifyControlsEnabled()
        {
            if (_customNotifyCheck == null) { return; }
            bool custom = _customNotifyCheck.Checked;
            if (_mirrorNotifyCheck != null) { _mirrorNotifyCheck.Enabled = custom; }
            if (_notifyCornerCombo != null) { _notifyCornerCombo.Enabled = custom; }
            if (_notifyDurationNum != null) { _notifyDurationNum.Enabled = custom; }
            if (_mirrorClearCheck != null)
            {
                _mirrorClearCheck.Enabled = custom && _mirrorNotifyCheck != null && _mirrorNotifyCheck.Checked;
            }

            // These two were missed, and the TRAY MENU already knew better: it greys out
            // "Screenshot alerts" when the pop-ups are off, while this page left the same
            // option fully tickable. Two places in the same app giving opposite answers
            // about whether a setting is available.
            //
            // Both genuinely depend on the custom cards. The screenshot preview IS a card
            // — ApplyClipboardImageWatcher won't even attach the clipboard listener
            // without CustomNotifications — and the ✕ is drawn on a card that never
            // appears. Ticking either with pop-ups off did nothing at all, silently.
            if (_notifyScreenshotCheck != null) { _notifyScreenshotCheck.Enabled = custom; }
            if (_notifyCloseCheck != null) { _notifyCloseCheck.Enabled = custom; }
        }

        /// <summary>
        /// Writes the notification settings to disk the instant one is toggled, so the
        /// on/off (and corner/duration) are remembered no matter how Tempo is next closed
        /// — even a crash or a force-quit. This is what makes the toggles "sticky" for
        /// users who don't press Save; the exit-time capture is now just a backstop.
        /// </summary>
        private void PersistNotificationSettings()
        {
            try { Persistence.SettingsManager.Save(_settings); }
            catch (Exception ex) { Utils.Logger.Swallow("PersistNotify", ex); }
        }

        /// <summary>
        /// "Label speakers", "AI face &amp; mouth analysis", "Auto-start" and "Save transcripts to disk" take
        /// effect the moment they are ticked, and are kept at once — as the caption switches beside them
        /// (lyrics, own voice, the bar) already were. They waited for Save Settings: until then the Captions
        /// tab, which reads the settings, said the opposite of these boxes (its Speakers card offered to
        /// "Turn on speaker labels" under a ticked box, and its Clear went by the old "save transcripts"
        /// value), and leaving the page without saving lost the change.
        /// </summary>
        private void OnCaptionSwitchChanged()
        {
            if (_suppressSettingsEvents || _settings == null) { return; }
            try
            {
                bool labels = _captionSpeakerCheck != null ? _captionSpeakerCheck.Checked : _settings.CaptionSpeakerTurns;
                bool faces = _captionFaceCheck != null ? _captionFaceCheck.Checked : _settings.CaptionFaceAnalysis;
                bool speakersChanged = labels != _settings.CaptionSpeakerTurns || faces != _settings.CaptionFaceAnalysis;
                _settings.CaptionSpeakerTurns = labels;
                _settings.CaptionFaceAnalysis = faces;
                if (_captionAutoStartCheck != null) { _settings.CaptionAutoStart = _captionAutoStartCheck.Checked; }
                if (_captionTranscriptCheck != null) { _settings.CaptionSaveTranscripts = _captionTranscriptCheck.Checked; }
                PersistNotificationSettings();   // the shared "save settings now" helper
                // A running session picks the speaker switches up now (and keeps its speakers when only
                // the face switch moved — see ApplySpeakerTurnsLive).
                if (speakersChanged) { ApplySpeakerTurnsLive(); }
                if (IsCaptionsTabVisible()) { RefreshCaptionsTab(); }
            }
            catch (Exception ex) { Utils.Logger.Warn("[Captions] caption switch failed: " + ex.Message); }
        }

        /// <summary>
        /// Shows the live mirror state under the notification controls — whether it's
        /// off, running, or couldn't get Windows' permission (and why).
        /// </summary>
        private void RefreshNotifyStatus()
        {
            if (_notifyStatusLabel == null) { return; }
            try
            {
                if (_settings != null && _settings.MirrorWindowsNotifications && !_settings.CustomNotifications)
                {
                    // Ticked but greyed, with nothing said: mirroring looked on while nothing was
                    // mirrored, and only Windows' own pop-ups appeared. It draws Tempo's cards, so it
                    // waits for them — say so where the switch is.
                    _notifyStatusLabel.Text = Localization.T("▲ Paused: needs Tempo's pop-ups (above)");
                    _notifyStatusLabel.ForeColor = _theme.WarningText;
                    if (_notifyPermissionBtn != null) { _notifyPermissionBtn.Visible = false; }
                    return;
                }
                if (_settings == null || !_settings.MirrorWindowsNotifications)
                {
                    _notifyStatusLabel.Text = "";
                    return;
                }
                string s = _notifyMirror != null
                    ? _notifyMirror.StatusText : Localization.T("starting…");
                bool ok = _notifyMirror != null && _notifyMirror.Running;
                // When it's running, the honest next step: Windows still banners its own
                // pop-up unless Do not disturb is on (button on the left).
                _notifyStatusLabel.Text = ok
                    ? Localization.T("● On — turn on Do Not Disturb to hide Windows' own pop-ups")
                    : Localization.F("▲ Mirror: {0}", s);
                _notifyStatusLabel.ForeColor = ok ? _theme.SuccessText : _theme.WarningText;

                // The route to the only switch that governs this, offered exactly when
                // Windows has refused — and hidden the rest of the time, because sending
                // someone to a privacy page that already says Allow explains nothing.
                if (_notifyPermissionBtn != null)
                {
                    _notifyPermissionBtn.Visible = _notifyMirror != null
                                                   && _notifyMirror.NeedsWindowsPermission;
                }
            }
            catch { /* status is best-effort */ }
        }

        /// <summary>
        /// The camera-relative movement card. Built in its own method because the
        /// settings page is already a wall of absolute coordinates and this keeps the
        /// new block self-contained.
        /// </summary>
        /// <summary>
        /// Logs a warning for any two cards on a settings-style page whose rectangles
        /// intersect, and for anything overlapping the action-button row.
        ///
        /// These pages lay their cards out with absolute coordinates, several of which
        /// live in different methods, so growing one card means remembering to move
        /// every card below it by hand. Getting that wrong produces no exception and no
        /// visual error until someone looks at the page — this turns it into a log line
        /// naming both cards and the overlap in pixels.
        ///
        /// CARD vs CARD only. Cards are opaque containers, so intersecting rectangles are
        /// a genuine collision, and nothing later moves them — checking here is sound.
        /// The controls INSIDE each card are NOT checked here; see the note below.
        /// </summary>
        private static void AssertNoCardOverlap(Control page)
        {
            try
            {
                var cards = new System.Collections.Generic.List<Control>();
                foreach (Control c in page.Controls)
                {
                    if (c is GroupBox) { cards.Add(c); }
                }
                for (int i = 0; i < cards.Count; i++)
                {
                    for (int k = i + 1; k < cards.Count; k++)
                    {
                        Rectangle a = cards[i].Bounds, b = cards[k].Bounds;
                        Rectangle hit = Rectangle.Intersect(a, b);
                        if (hit.Width > 0 && hit.Height > 0)
                        {
                            Utils.Logger.Warn("[layout] settings cards OVERLAP: '" +
                                cards[i].Text + "' " + a + " and '" + cards[k].Text + "' " + b +
                                " overlap by " + hit.Height + "px vertically.");
                        }
                    }
                }
                // The controls INSIDE each card used to be checked here too, by
                // ReportOverlappingChildren. That check has been removed, for two
                // reasons that compounded:
                //
                //   1. It ran during tab construction — about 40 ms BEFORE
                //      LayoutFitter.FitAll, whose whole job is to clamp captions that
                //      overflow their column. So it reported, at WARN, collisions that
                //      were repaired a few milliseconds later. On 2026-08-30 that was
                //      441 warnings in one day describing nothing wrong.
                //   2. It compared raw Bounds. A fixed-width Label is routinely far
                //      wider than its caption, so "Líneas conservadas:" was reported as
                //      overlapping the value "6" sitting in the empty half of its own
                //      box — box against box, no glyphs anywhere near each other.
                //
                // LayoutFitter.CountOverlaps already does this properly: same control
                // filter, same 6px threshold, but it compares INKED text and runs after
                // the fitter, so it reports only what a reader could actually see. Its
                // Containers() walk recurses the whole form, so these very cards are
                // covered — the ones warned about here are the same ones it clamps and
                // then verifies at "0 remaining".
            }
            catch (Exception ex) { Utils.Logger.Swallow("AssertNoCardOverlap", ex); }
        }

        /// <summary>
        /// One line describing the display Tempo is drawn on: resolution, refresh rate
        /// and scaling, plus a note when there is more than one monitor. Same figures the
        /// About box reports — they just were not on the page where someone adjusting the
        /// window would look for them.
        /// </summary>
        private static string DescribeDisplay()
        {
            // Translated piece by piece: built from English words, this line read "100% scaling · 2
            // monitors (usable … after the taskbar)" in every language (found by the scanner's English check).
            try
            {
                Screen s = Screen.PrimaryScreen;
                if (s == null) { return Localization.T("Display information isn't available."); }

                var sb = new System.Text.StringBuilder();
                sb.Append(s.Bounds.Width).Append('×').Append(s.Bounds.Height);

                int hz = Utils.EnvironmentInfo.GetPrimaryRefreshHz();
                if (hz > 0) { sb.Append("  ·  ").Append(hz).Append(" Hz"); }

                string scale = Utils.EnvironmentInfo.GetDisplayScaleText();
                if (!string.IsNullOrEmpty(scale)) { sb.Append("  ·  ").Append(Localization.F("{0} scaling", scale)); }

                int count = Screen.AllScreens != null ? Screen.AllScreens.Length : 1;
                if (count > 1)
                {
                    sb.Append("  ·  ").Append(Localization.F("{0} monitors", count));
                }
                // Work area, not just bounds: it is what actually constrains the window,
                // and the difference is the taskbar — the usual reason a "screen-sized"
                // window comes back slightly short.
                var wa = s.WorkingArea;
                if (wa.Height != s.Bounds.Height || wa.Width != s.Bounds.Width)
                {
                    sb.Append("   ").Append(Localization.F("(usable {0}×{1} after the taskbar)", wa.Width, wa.Height));
                }
                return sb.ToString();
            }
            catch { return Localization.T("Display information isn't available."); }
        }

        private GroupBox BuildMovementGroup()
        {
            // Keeps the usual 12px gap below "Window & Display" (12,1450,696,152 →
            // ends 1602). This card lives in its own method, so it does NOT move when
            // the cards above it are resized in BuildSettingsTab — growing the captions
            // card by 36 there left this one 24px UNDER the Window & Display card,
            // swallowing its title, and growing the Data card for the tamper check
            // would have done it again. AssertNoCardOverlap below now catches that.
            var g = UiFactory.Group(Localization.T("Camera-relative movement (advanced)"),
                12, 1614, 696, 240, CardIcon.Gauge);

            _movementEnableCheck = UiFactory.Check("Enable camera-relative movement", 16, 30);
            _movementEnableCheck.AutoSize = true;
            _movementEnableCheck.CheckedChanged += (s, e) =>
            {
                if (_suppressSettingsEvents) { return; }
                _settings.MovementEnabled = _movementEnableCheck.Checked;
                ApplyMovementSetting();          // arm/disarm immediately — no Save needed
            };
            g.Controls.Add(_movementEnableCheck);

            // The honest warning. This feature takes over W/A/S/D system-wide and
            // steers by an ESTIMATED camera, so the user deserves to know both facts
            // before they wonder why their keyboard feels haunted.
            var warn = UiFactory.Caption(
                "While enabled, Tempo intercepts W/A/S/D and sends its own. It cannot read the game's " +
                "camera — it estimates it from mouse movement, so calibrate below and turn OFF in-game " +
                "mouse acceleration. Many online games forbid input automation.",
                16, 54);
            warn.ForeColor = Theme.Readable(_theme.Warning, _theme.InputBackground);
            warn.AutoSize = false;
            warn.Width = 664;
            // As tall as the words need in the language it is in. A fixed two lines stopped
            // drawing mid-sentence in French, German, Italian and Portuguese, which need three
            // — cutting off the part about online games forbidding it. Every row below
            // moves down by whatever the warning grew.
            // In the window's units (MeasureLayout): built before the window is scaled.
            warn.Height = Math.Max(32, MeasureLayout(warn.Text, warn.Font, warn.Width, TextFormatFlags.Default).Height + 2);
            g.Controls.Add(warn);
            int dy = warn.Height - 32;

            g.Controls.Add(UiFactory.Label("Mode:", 16, 96 + dy));
            // Combo items don't auto-translate (UiFactory.Combo takes them verbatim), so
            // pass them through T() here. Selection stays index-based, so translating the
            // display text is safe.
            _movementFrameCombo = UiFactory.Combo(150, 93 + dy, 330,
                Localization.T("World-locked – keep heading as the camera turns"),
                Localization.T("Camera-relative pass-through (no-op in most games)"));
            LayoutFitter.DeclareShortenedChoices(_movementFrameCombo,
                "descriptive choices in a full row; the open list and the hover show them whole");
            g.Controls.Add(_movementFrameCombo);

            // Which mouse movement is a camera turn. Roblox's camera script turns the camera only while
            // it holds the pointer (the right button held, shift lock, first person); the rest of the time
            // the mouse moves a pointer, and counting that as turning bent the heading for a camera that
            // never moved. See Engine.PointerHoldDetector.
            g.Controls.Add(UiFactory.Label("Camera turns:", 16, 132 + dy));
            _movementTurnsCombo = UiFactory.Combo(150, 129 + dy, 330,
                Localization.T("While the game holds the pointer (recommended)"),
                Localization.T("With every mouse movement"));
            LayoutFitter.DeclareShortenedChoices(_movementTurnsCombo,
                "descriptive choices in a full row; the open list and the hover show them whole");
            g.Controls.Add(_movementTurnsCombo);

            // Sensitivity + the calibrate button that fills it in.
            var sensLabel = UiFactory.Label("Camera sensitivity:", 16, 168 + dy);
            g.Controls.Add(sensLabel);
            // The row starts after its label, however wide that is, and the hint at its end gives back
            // the room. Text grows by more than the window does on a 150% screen (glyphs round to whole
            // pixels): "Sensibilidade da câmara:" came out 207 px and ran 6 into the box at 150.
            int sdx = Math.Max(0, LayoutRight(sensLabel) + 4 - 150);
            _movementDegPerCountNum = UiFactory.Numeric(150 + sdx, 164 + dy, 90, 0.0005m, 2m, 0.06m);
            _movementDegPerCountNum.DecimalPlaces = 4;
            _movementDegPerCountNum.Increment = 0.005m;
            g.Controls.Add(_movementDegPerCountNum);
            g.Controls.Add(UiFactory.Caption("°/count", 246 + sdx, 168 + dy));

            _movementCalibrateBtn = UiFactory.Button("Calibrate…", 310 + sdx, 162 + dy, 120, 28);
            _movementCalibrateBtn.Click += OnCalibrateCameraSensitivity;
            g.Controls.Add(_movementCalibrateBtn);

            // The value in a unit you can check against the game: how much mouse movement one full turn
            // takes. "0.0600 °/count" alone says nothing to anyone (RefreshMovementHints).
            _movementTurnHint = UiFactory.Caption("", 440 + sdx, 168 + dy);
            _movementTurnHint.ForeColor = _theme.TextMuted;
            _movementTurnHint.AutoSize = false;
            _movementTurnHint.Width = 244 - sdx;
            _movementTurnHint.Height = 28;
            g.Controls.Add(_movementTurnHint);

            // Feel / jitter.
            g.Controls.Add(UiFactory.Label("Smoothing (s):", 16, 204 + dy));
            _movementSmoothingNum = UiFactory.Numeric(150, 200 + dy, 70, 0m, 1m, 0m);
            _movementSmoothingNum.DecimalPlaces = 2;
            _movementSmoothingNum.Increment = 0.02m;
            g.Controls.Add(_movementSmoothingNum);
            var smoothHint = UiFactory.Caption("0 = instant", 226, 204 + dy);
            smoothHint.ForeColor = _theme.TextMuted;
            g.Controls.Add(smoothHint);

            g.Controls.Add(UiFactory.Label("Anti-jitter (°):", 310, 204 + dy));
            _movementHysteresisNum = UiFactory.Numeric(410, 200 + dy, 70, 0m, 22m, 8m);
            _movementHysteresisNum.DecimalPlaces = 1;
            g.Controls.Add(_movementHysteresisNum);

            g.Controls.Add(UiFactory.Label("Rate (Hz):", 500, 204 + dy));
            _movementHzNum = UiFactory.Numeric(580, 200 + dy, 70, 30m, 500m, 120m);
            g.Controls.Add(_movementHzNum);

            // Pulsing between the two nearest of the 8 directions (Steam Input's "analog emulation via
            // output pulsing"). Beside anti-jitter on purpose: pulsing switches keys deliberately, so
            // that setting does nothing while this is on (RefreshMovementHints greys it).
            _movementFineCheck = UiFactory.Check("Fine steering: pulse the keys to aim between the 8 directions", 16, 236 + dy);
            _movementFineCheck.AutoSize = true;
            g.Controls.Add(_movementFineCheck);

            g.Controls.Add(UiFactory.Label("Stick deadzone:", 16, 270 + dy));
            _movementDeadzoneNum = UiFactory.Numeric(150, 266 + dy, 70, 0m, 0.9m, 0.20m);
            _movementDeadzoneNum.DecimalPlaces = 2;
            _movementDeadzoneNum.Increment = 0.05m;
            g.Controls.Add(_movementDeadzoneNum);

            g.Controls.Add(UiFactory.Label("Pad look (°/s):", 310, 270 + dy));
            _movementPadYawNum = UiFactory.Numeric(410, 266 + dy, 70, 20m, 1000m, 220m);
            g.Controls.Add(_movementPadYawNum);

            // A row of its own, the card's full width. It was squeezed into the 184 px left
            // beside "Pad look", where its first message needed three lines of a two-line box
            // in English (and four in French) — the end of the sentence was simply not drawn,
            // and the ARMED/Off messages that replace it are longer still.
            _movementStatus = UiFactory.Caption("Bind a key in Keybinds → “Camera-relative movement” to arm it in-game.",
                16, 304 + dy);
            _movementStatus.ForeColor = _theme.TextMuted;
            _movementStatus.AutoSize = false;
            _movementStatus.Width = 664;
            _movementStatus.Height = 30;
            g.Controls.Add(_movementStatus);

            // Every value applies the moment it changes, to a running engine too — no Save Settings.
            // You tune this while playing: the arrows on "Anti-jitter" did nothing until a trip to the
            // Save button, and the engine they were meant for was armed the whole time.
            EventHandler changed = OnMovementControlChanged;
            _movementFrameCombo.SelectedIndexChanged += changed;
            _movementTurnsCombo.SelectedIndexChanged += changed;
            _movementDegPerCountNum.ValueChanged += changed;
            _movementSmoothingNum.ValueChanged += changed;
            _movementHysteresisNum.ValueChanged += changed;
            _movementHzNum.ValueChanged += changed;
            _movementFineCheck.CheckedChanged += changed;
            _movementDeadzoneNum.ValueChanged += changed;
            _movementPadYawNum.ValueChanged += changed;
            RefreshMovementHints();

            g.Height = _movementStatus.Bottom + 10;
            return g;
        }

        /// <summary>A movement control changed: into the settings, into the running engine, saved shortly.</summary>
        private void OnMovementControlChanged(object sender, EventArgs e)
        {
            if (_suppressSettingsEvents || _settings == null) { return; }
            ReadMovementControls();
            ApplyMovementTuning();
            RefreshMovementHints();
            PersistSettingsSoon();
        }

        /// <summary>Every movement control into the settings (shared by the live handler, Save and Export).</summary>
        private void ReadMovementControls()
        {
            _settings.MovementEnabled = _movementEnableCheck.Checked;
            _settings.MovementFrame = Math.Max(0, _movementFrameCombo.SelectedIndex);
            _settings.MovementTurnDetection = Math.Max(0, _movementTurnsCombo.SelectedIndex);
            _settings.MovementDegreesPerCount = (double)_movementDegPerCountNum.Value;
            _settings.MovementFineSteering = _movementFineCheck.Checked;
            _settings.MovementTurnSmoothing = (double)_movementSmoothingNum.Value;
            _settings.MovementHysteresisDegrees = (double)_movementHysteresisNum.Value;
            _settings.MovementUpdateHz = (int)_movementHzNum.Value;
            _settings.MovementStickDeadzone = (double)_movementDeadzoneNum.Value;
            _settings.MovementGamepadYawDps = (double)_movementPadYawNum.Value;
        }

        /// <summary>The counts-per-turn readout, and anti-jitter greyed while fine steering (which ignores it) is on.</summary>
        private void RefreshMovementHints()
        {
            if (_movementTurnHint != null && _movementDegPerCountNum != null)
            {
                double v = (double)_movementDegPerCountNum.Value;
                long counts = v > 0 ? (long)Math.Round(360.0 / v) : 0;
                _movementTurnHint.Text = Localization.F("A full turn takes {0:N0} counts.", counts);
            }
            if (_movementHysteresisNum != null && _movementFineCheck != null)
            {
                _movementHysteresisNum.Enabled = !_movementFineCheck.Checked;
            }
        }

        /// <summary>
        /// Runs the 360°-turn calibration and writes the measured value straight into
        /// the box (and into the live movement engine, if it is already armed).
        /// </summary>
        private void OnCalibrateCameraSensitivity(object sender, EventArgs e)
        {
            // CONFLICT: calibration asks you to sweep the mouse a full circle while a
            // SECOND keyboard hook (the dialog's own) is installed. If the movement engine
            // is still armed it is integrating that very sweep into its yaw estimate and,
            // when it was armed from this Settings tab, its target window is "everywhere" —
            // so it also suppresses W/A/S/D and holds keys down while the calibration
            // dialog has focus. Two hooks and a live engine fighting over the same sweep is
            // exactly the wrong state to measure in, and you calibrate PRECISELY when the
            // engine is misbehaving, so it is armed nearly every time. Stand it down for
            // the measurement and put it back the way it was.
            bool wasArmed = _movement != null && _movement.IsRunning;
            // The game it was armed on. Re-armed with nothing to go on, it took the window in front
            // once the dialog closed — Tempo's, which means "act in every window": calibrating while
            // armed for a game left W/A/S/D captured everywhere.
            IntPtr armedOn = wasArmed ? _movement.TargetWindow : IntPtr.Zero;
            if (wasArmed)
            {
                StopMovement();
                Utils.Logger.Info("[Movement] disarmed for calibration; will re-arm afterwards.");
            }

            try
            {
                using (var dlg = new CameraCalibrationForm(_theme, (double)_movementDegPerCountNum.Value))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                    decimal v = (decimal)dlg.DegreesPerCount;
                    if (v < _movementDegPerCountNum.Minimum) { v = _movementDegPerCountNum.Minimum; }
                    if (v > _movementDegPerCountNum.Maximum) { v = _movementDegPerCountNum.Maximum; }
                    _movementDegPerCountNum.Value = v;   // OnMovementControlChanged takes it from here too
                    _settings.MovementDegreesPerCount = (double)v;
                    try { Persistence.SettingsManager.Save(_settings); } catch { }
                    ApplyMovementTuning();
                    Utils.Logger.Info("[Movement] calibrated: " + ((double)v).ToString("0.#####") + " deg/count.");
                }
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("[Movement] calibration failed: " + ex.Message);
                MessageBox.Show(this, Localization.F("Calibration couldn't run: {0}", ex.Message), "Tempo",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                // In a finally on purpose: the OK path returns early on Cancel, and a
                // throw lands in the catch above. Leaving the engine disarmed because the
                // user pressed Escape would be a silent, baffling failure the next time
                // they pressed a movement key. Re-arming here picks up the value just
                // measured, since StartMovement rebuilds the tuning from settings.
                if (wasArmed)
                {
                    StartMovement(armedOn);
                    Utils.Logger.Info("[Movement] re-armed after calibration.");
                }
            }
        }

        // ── switches that apply the moment they are ticked ──────────────────────────────────

        /// <summary>"Write a log file to disk": on or off at once, and — turned off — the log already written offered for deletion.</summary>
        private void OnWriteLogChanged(object sender, EventArgs e)
        {
            if (_suppressSettingsEvents || _settings == null || _writeLogCheck == null) { return; }
            bool on = _writeLogCheck.Checked;
            _settings.WriteLogFile = on;
            Logger.Enabled = on;
            PersistNotificationSettings();   // the shared "save settings now" helper
            if (on) { Logger.Info("[Log] writing the log file again (turned on in Settings)."); }
            else { OfferToDeleteLog(); }
            RefreshLogTip();
        }

        /// <summary>The activity log's files on disk: autoclicker.log and its rotated copies (crash reports are separate).</summary>
        private static List<string> LogFilesOnDisk()
        {
            var files = new List<string>();
            try
            {
                string path = Logger.GetLogPath();
                if (string.IsNullOrEmpty(path)) { return files; }
                foreach (string f in new[] { path, path + ".1", path + ".2", path + ".3" })
                {
                    if (File.Exists(f)) { files.Add(f); }
                }
            }
            catch (Exception ex) { Logger.Swallow("LogFilesOnDisk", ex); }
            return files;
        }

        private static long BytesOf(List<string> files)
        {
            long n = 0;
            foreach (string f in files) { try { n += new FileInfo(f).Length; } catch { } }
            return n;
        }

        /// <summary>"2.4 MB", "310 KB": a file size, the way Explorer says it.</summary>
        private static string SizeText(long bytes)
        {
            if (bytes >= 1024 * 1024) { return (bytes / 1048576.0).ToString("0.0") + " MB"; }
            return Math.Max(1, (bytes + 1023) / 1024) + " KB";
        }

        /// <summary>
        /// Turning the log off "for extra privacy" stopped new lines and left up to 4 MB of the old ones on
        /// disk. Asked, not done: the log is also what a bug report sends.
        /// </summary>
        private void OfferToDeleteLog()
        {
            List<string> files = LogFilesOnDisk();
            if (files.Count == 0) { return; }
            if (MessageBox.Show(this,
                    Localization.F("Delete the log Tempo has already written ({0})?", SizeText(BytesOf(files))),
                    "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            Logger.Close();
            string why = null;
            foreach (string f in files)
            {
                try { File.Delete(f); }
                catch (Exception ex) { why = ex.Message; }
            }
            if (why != null) { ShowWarning(Localization.F("Some of the log could not be deleted ({0}).", why)); }
        }

        /// <summary>The log switch's tip: where the log is and how big it is now.</summary>
        private void RefreshLogTip()
        {
            if (_writeLogCheck == null || _tips == null) { return; }
            try
            {
                string path = Logger.GetLogPath();
                string folder = string.IsNullOrEmpty(path) ? "?" : Path.GetDirectoryName(path);
                string tip = Localization.F(
                    "Keeps a record of what Tempo does in {0} (now {1}, never more than about 4 MB). A bug report sends it. "
                    + "Turn off for extra privacy; Live debug works either way.",
                    folder, SizeText(BytesOf(LogFilesOnDisk())));
                TipStyle.Set(_tips, _writeLogCheck, tip);
                _writeLogCheck.AccessibleDescription = tip;
            }
            catch (Exception ex) { Logger.Swallow("RefreshLogTip", ex); }
        }

        /// <summary>"Show on-screen overlay while running": the badge of a run in progress appears or goes now, and tray ▸ Overlays agrees.</summary>
        private void OnShowOverlayChanged(object sender, EventArgs e)
        {
            if (_suppressSettingsEvents || _settings == null || _showIndicatorCheck == null) { return; }
            if (_settings.ShowClickingIndicator == _showIndicatorCheck.Checked) { return; }   // the tray's own copy, echoed back
            _settings.ShowClickingIndicator = _showIndicatorCheck.Checked;
            PersistNotificationSettings();
            ShowClickingIndicator(_engine != null && _engine.IsRunning);
            if (!_settings.ShowClickingIndicator) { HideMacroIndicator(); }
            if (_trayOverlayShowItem != null) { _trayOverlayShowItem.Checked = _settings.ShowClickingIndicator; }
        }

        /// <summary>
        /// A caption look control changed (font, size, opacity, background): the caption bar and the transcript
        /// show it now, and it is kept. It used to wait for Save, so a new font looked like it did nothing.
        /// </summary>
        private void OnCaptionLookChanged()
        {
            if (_suppressSettingsEvents || _settings == null) { return; }
            if (_captionFontNum != null) { _settings.CaptionFontSize = (int)_captionFontNum.Value; }
            if (_captionOpacityNum != null) { _settings.CaptionOpacity = (int)_captionOpacityNum.Value; }
            if (_captionBackgroundCheck != null) { _settings.CaptionShowBackground = _captionBackgroundCheck.Checked; }
            if (_captionFontCombo?.SelectedItem != null) { _settings.CaptionFontFamily = _captionFontCombo.SelectedItem.ToString(); }
            ApplyCaptionSettingsToOverlays();
            PersistCaptionLookSoon();
        }

        private System.Windows.Forms.Timer _captionLookSaveTimer;

        /// <summary>Saves the caption look shortly after the last change: holding a size arrow down is many changes.</summary>
        private void PersistCaptionLookSoon() => PersistSettingsSoon();

        /// <summary>
        /// Saves the settings 700 ms after the last call: a value changing as it is dragged or its arrows
        /// held is written once when it settles, not once per step.
        /// </summary>
        private void PersistSettingsSoon()
        {
            if (_captionLookSaveTimer == null)
            {
                _captionLookSaveTimer = new System.Windows.Forms.Timer { Interval = 700 };
                _captionLookSaveTimer.Tick += (s, e) =>
                {
                    _captionLookSaveTimer.Stop();
                    PersistNotificationSettings();
                };
            }
            _captionLookSaveTimer.Stop();
            _captionLookSaveTimer.Start();
        }

        /// <summary>Each font in the caption font list, drawn in itself (FlatComboBox.ItemFont). Built once per font.</summary>
        private readonly Dictionary<string, Font> _captionFontPreviews = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);

        private Font CaptionFontPreview(int index)
        {
            if (_captionFontCombo == null || index < 0 || index >= _captionFontCombo.Items.Count) { return null; }
            string name = _captionFontCombo.Items[index]?.ToString();
            if (string.IsNullOrEmpty(name)) { return null; }
            if (_captionFontPreviews.TryGetValue(name, out Font cached)) { return cached; }
            Font f = null;
            try
            {
                FontStyle style;
                using (var fam = new FontFamily(name))
                {
                    style = fam.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular
                          : fam.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Italic;
                }
                Font ui = _captionFontCombo.Font;
                f = new Font(name, ui.Size, style, ui.Unit);
            }
            catch { f = null; }   // the list's own font draws it instead
            _captionFontPreviews[name] = f;
            return f;
        }

        private bool _allCaptionFontsAdded;

        /// <summary>
        /// Every installed text font, after the recommended ones — the first time the list opens. Asking for
        /// the whole font collection costs ~24 ms cold, which the Settings page no longer pays to open.
        /// </summary>
        private void AddAllCaptionFonts()
        {
            if (_allCaptionFontsAdded || _captionFontCombo == null) { return; }
            _allCaptionFontsAdded = true;
            try
            {
                var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (object o in _captionFontCombo.Items) { have.Add(o.ToString()); }
                var more = new List<string>();
                using (var installed = new System.Drawing.Text.InstalledFontCollection())
                {
                    foreach (FontFamily fam in installed.Families)
                    {
                        string name = fam.Name;
                        if (have.Contains(name) || !IsTextFont(name)) { continue; }
                        // A family with neither a regular nor a bold face cannot be drawn by the caption bar.
                        if (!fam.IsStyleAvailable(FontStyle.Regular) && !fam.IsStyleAvailable(FontStyle.Bold)) { continue; }
                        more.Add(name);
                    }
                }
                more.Sort(StringComparer.CurrentCultureIgnoreCase);
                _captionFontCombo.BeginUpdate();
                foreach (string name in more) { _captionFontCombo.Items.Add(name); }
                _captionFontCombo.EndUpdate();
            }
            catch (Exception ex) { Logger.Swallow("AddAllCaptionFonts", ex); }
        }

        /// <summary>Not an icon or symbol font (their "letters" are pictures), and not a vertical CJK variant (@…).</summary>
        private static bool IsTextFont(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("@", StringComparison.Ordinal)) { return false; }
            foreach (string s in new[] { "MDL2", "Fluent Icons", "Wingdings", "Webdings", "Marlett", "Symbol", "Emoji", "Dings", "MT Extra", "HoloLens", "Bookshelf" })
            {
                if (name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) { return false; }
            }
            return true;
        }

        /// <summary>
        /// Opens the running-overlay customisation dialog and applies the result
        /// immediately (saved to disk, and pushed into a live badge if one is showing).
        /// </summary>
        private void OnCustomiseOverlay(object sender, EventArgs e)
        {
            if (_settings == null) { return; }
            try
            {
                using (var dlg = new OverlaySettingsForm(_theme,
                    _settings.OverlayCorner, _settings.OverlayOpacity,
                    _settings.OverlayShowClicks, _settings.OverlayShowCps, _settings.OverlayShowElapsed))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                    // A corner picked here is where the badges should go: one dragged elsewhere (tray ▸
                    // Overlays ▸ Move overlays) would otherwise ignore the choice and stay put.
                    bool cornerChanged = dlg.Corner != _settings.OverlayCorner;
                    _settings.OverlayCorner = dlg.Corner;
                    _settings.OverlayOpacity = dlg.Opacity;
                    _settings.OverlayShowClicks = dlg.ShowClicks;
                    _settings.OverlayShowCps = dlg.ShowCps;
                    _settings.OverlayShowElapsed = dlg.ShowElapsed;
                    if (cornerChanged)
                    {
                        _settings.ClickingOverlayX = _settings.ClickingOverlayY = AppSettings.OverlayUnplaced;
                        _settings.MacroOverlayX = _settings.MacroOverlayY = AppSettings.OverlayUnplaced;
                        foreach (ClickingIndicatorForm b in new[] { _clickingIndicator, _macroIndicator, _clickingPreview, _macroPreview })
                        {
                            if (b != null && !b.IsDisposed) { b.FreePosition = null; }
                        }
                    }
                    try { Persistence.SettingsManager.Save(_settings); } catch { }

                    // Reflect it live on every badge on screen (the macro and recording ones too).
                    ApplyOverlayLookToAll();
                }
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("[Overlay] customise failed: " + ex.Message);
            }
        }

        private void OnSaveSettings(object sender, EventArgs e)
        {
            // Snapshot caption engine settings BEFORE the writes below, so a running
            // session can be re-pointed when the user changed source/model/capture, the
            // speaker-label toggle can start/stop the analyzers live, and an explicit
            // model pick can clear the too-slow session override.
            var prevCaptionSpeakerTurns = _settings.CaptionSpeakerTurns;
            var prevCaptionFaceAnalysis = _settings.CaptionFaceAnalysis;
            var prevCaptionSource = _settings.CaptionSource;
            string prevCaptionModel = _settings.CaptionModelKey;
            int prevCaptionCapture = _settings.CaptionCaptureMode;

            if (_themeCombo.SelectedIndex >= 0)
            {
                _settings.Theme = (ThemeKind)_themeCombo.SelectedIndex;
            }
            _settings.AlwaysOnTop = _alwaysOnTopCheck.Checked;
            _settings.CustomAccentEnabled = _customAccentCheck.Checked;
            if (_languageCombo.SelectedIndex >= 0)
            {
                _settings.Language = (Language)_languageCombo.SelectedIndex;
            }

            _settings.MinimizeToTrayOnClose = _minimizeToTrayCheck.Checked;
            _settings.StartMinimizedToTray = _startMinimizedCheck.Checked;
            _settings.TraySleepEnabled = _traySleepCheck.Checked;
            _settings.CaptionOverlayEnabled = _captionOverlayCheck.Checked;
            _settings.CaptionSpeakerTurns = _captionSpeakerCheck.Checked;
            _settings.CaptionFilterOwnVoice = _captionOwnVoiceCheck != null ? _captionOwnVoiceCheck.Checked : _settings.CaptionFilterOwnVoice;
            _settings.CaptionLyrics = _captionLyricsCheck != null ? _captionLyricsCheck.Checked : _settings.CaptionLyrics;
            _settings.CaptionFaceAnalysis = _captionFaceCheck.Checked;
            _settings.CaptionSaveTranscripts = _captionTranscriptCheck.Checked;
            _settings.CaptionShowSourceTag = _captionSourceTagCheck.Checked;
            _settings.CaptionTryGpu = _captionGpuCheck.Checked;
            _settings.CaptionAutoStart = _captionAutoStartCheck.Checked;
            _settings.CaptionSource = (CaptionSource)Math.Max(0, _captionSourceCombo.SelectedIndex);
            _settings.CaptionModelKey = WhisperModelKeyFromIndex(_captionModelCombo.SelectedIndex);
            // Guard the index the way the combos above are guarded: a control that never
            // got built (or a page torn down mid-shutdown) reads -1, and writing that
            // back would silently reset the language to Auto-detect on the way out.
            if (_captionLangCombo != null && _captionLangCombo.SelectedIndex >= 0)
            {
                _settings.CaptionLanguage =
                    CaptionLanguageCodeFromIndex(_captionLangCombo.SelectedIndex);
            }
            _settings.CaptionCaptureMode = Math.Max(0, _captionCaptureCombo.SelectedIndex);
            _settings.CaptionFontSize = (int)_captionFontNum.Value;
            _settings.CaptionOpacity = (int)_captionOpacityNum.Value;
            if (_captionLinesNum != null)
            {
                _settings.CaptionMaxLines = (int)_captionLinesNum.Value;
            }

            ReadMovementControls();

            _settings.CaptionShowBackground = _captionBackgroundCheck.Checked;
            if (_captionFontCombo.SelectedItem != null)
                _settings.CaptionFontFamily = _captionFontCombo.SelectedItem.ToString();
            _settings.ShowTrayNotifications = _trayNotifyCheck.Checked;
            _settings.CustomNotifications = _customNotifyCheck.Checked;
            _settings.MirrorWindowsNotifications = _mirrorNotifyCheck.Checked;
            _settings.MirrorClearFromActionCenter = _mirrorClearCheck.Checked;
            _settings.NotifyOnClipboardImage = _notifyScreenshotCheck.Checked;
            _settings.NotificationShowClose = _notifyCloseCheck.Checked;
            _settings.NotificationCorner = Math.Max(0, _notifyCornerCombo.SelectedIndex);
            _settings.NotificationDurationSeconds = (int)_notifyDurationNum.Value;
            _settings.ConfirmBeforeExitWhileRunning = _confirmExitCheck.Checked;
            _settings.SafetyStopOnEscape = _safetyEscapeCheck.Checked;
            _settings.ClickerStartDelaySeconds = (int)_startDelayNum.Value;
            _settings.StartDelayBeep = _startDelayBeepCheck != null && _startDelayBeepCheck.Checked;
            _settings.LaunchAtStartup = _launchStartupCheck.Checked;
            if (_openAtSignInCheck != null) { _settings.OpenWindowAtSignIn = _openAtSignInCheck.Checked; }
            if (_startupDelayNum != null) { _settings.StartupDelaySeconds = (int)_startupDelayNum.Value; }
            _settings.HideWhenClicking = _hideWhenClickingCheck.Checked;
            _settings.CheckForUpdatesOnLaunch = _checkUpdatesCheck.Checked;
            if (_updateFreqCombo != null) { _settings.UpdateCheckFrequency = Math.Max(0, _updateFreqCombo.SelectedIndex); }
            if (_checkRobloxUpdatesCheck != null) { _settings.CheckRobloxUpdates = _checkRobloxUpdatesCheck.Checked; }
            _settings.WriteLogFile = _writeLogCheck.Checked;
            _settings.RecordSessionHistory = _recordHistoryCheck.Checked;
            _settings.ShowClickingIndicator = _showIndicatorCheck.Checked;
            _settings.MinimizeWhileRecording = _minimizeRecordingCheck.Checked;
            if (_ignoreOwnWindowCheck != null)
            {
                _settings.IgnoreOwnWindowWhileRunning = _ignoreOwnWindowCheck.Checked;
            }
            if (_guardShortcutsCheck != null)
            {
                _settings.GuardWindowsShortcuts = _guardShortcutsCheck.Checked;
            }
            _settings.RememberWindowPosition = _rememberWindowCheck.Checked;
            _settings.RememberLastTab = _rememberTabCheck.Checked;
            _settings.WindowOpacity = _opacitySlider.Value;
            if (_unlockSpeedCheck != null) _settings.AdvancedUnlockSpeed = _unlockSpeedCheck.Checked;
            Logger.Enabled = _settings.WriteLogFile;

            // Start/stop the Windows-notification mirror to match the saved choice.
            ApplyNotificationSettings();
            ApplyClipboardImageWatcher();
            RefreshNotifyStatus();

            // Reflect the overlay preference immediately if a run is in progress.
            ShowClickingIndicator(_engine != null && _engine.IsRunning);

            // Push caption look changes (size, font, colour, opacity, background) to
            // any live caption overlay so they apply now, not just on next toggle.
            ApplyCaptionSettingsToOverlays();

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

            // ── Live-apply caption engine changes ────────────────────────────────
            // Speaker-label / face-analysis toggle: start or stop the profiler and face
            // analyzer on a RUNNING session, instead of doing nothing until the next
            // caption toggle (which silently left the degraded pause-count mode on).
            if (prevCaptionSpeakerTurns != _settings.CaptionSpeakerTurns ||
                prevCaptionFaceAnalysis != _settings.CaptionFaceAnalysis)
            {
                ApplySpeakerTurnsLive();
            }
            // An explicit model pick must beat the session's too-slow downgrade override,
            // or the smaller model would keep loading and ignore the user's new choice.
            if (!string.Equals(prevCaptionModel, _settings.CaptionModelKey, StringComparison.OrdinalIgnoreCase))
            {
                _captionModelOverrideKey = null;
                _modelRecoveryBlocked = false;
            }
            // Source / model / capture change on a running session: none of these apply
            // live on their own (Start no-ops while running; capture mode is read only at
            // capture-open), so the engine is restarted — in place. Switching captions off
            // and on for it started a new session, which cleared the transcript on the
            // Captions tab and in the history window and reset the speakers' names.
            bool captionEngineChanged =
                prevCaptionSource != _settings.CaptionSource ||
                !string.Equals(prevCaptionModel, _settings.CaptionModelKey, StringComparison.OrdinalIgnoreCase) ||
                prevCaptionCapture != _settings.CaptionCaptureMode;
            if (captionEngineChanged && _captionsActive)
            {
                RestartCaptionEngine("caption source, model or capture changed in Settings");
            }

            // Re-apply everything that depends on settings.
            ReassertTopMost();
            if (_trayAlwaysOnTopItem != null)
            {
                _trayAlwaysOnTopItem.Checked = _settings.AlwaysOnTop;
            }
            ApplyThemeToEverything();
            ApplyHotkeysFromSettings();
            // "Sleep in tray" is what silences these hotkeys — keep the notice on the
            // Keybinds tab in step with the setting the moment it is saved.
            RefreshTraySleepNotice();

            // No modal. A dialog that says "Settings saved." demands a click to dismiss
            // something the user already knows they did — it interrupts the exact moment
            // they were about to carry on. The button confirms it in place instead and
            // returns to normal by itself.
            ConfirmOnButton(_saveSettingsBtn);
        }

        // Restores a Save button after its brief "Saved ✓" state.
        private System.Windows.Forms.Timer _saveConfirmTimer;
        private string _saveBtnText;
        private Control _saveConfirmTarget;

        /// <summary>
        /// Briefly turns the Save button into a "Saved ✓" confirmation, then puts it back.
        /// Replaces the old modal: same reassurance, no interruption, and it can't stack up
        /// if the user saves repeatedly (the timer just restarts).
        /// </summary>
        private void ConfirmOnButton(Control button, string caption = null)
        {
            if (button == null || button.IsDisposed)
            {
                return;
            }
            try
            {
                // A different button than last time? Restore the previous one first, so a
                // pending timer can never write the wrong caption back.
                if (_saveConfirmTarget != null && !ReferenceEquals(_saveConfirmTarget, button))
                {
                    RestoreSaveButtonText();
                }
                if (_saveConfirmTarget == null) { _saveBtnText = button.Text; }
                _saveConfirmTarget = button;
                // A Copy button confirms with "Copied ✓": it saved nothing (see OnCopyKeybinds).
                button.Text = caption ?? Localization.T("Saved  ✓");

                if (_saveConfirmTimer == null)
                {
                    _saveConfirmTimer = new System.Windows.Forms.Timer { Interval = 1600 };
                    _saveConfirmTimer.Tick += (s, e) =>
                    {
                        _saveConfirmTimer.Stop();
                        RestoreSaveButtonText();
                    };
                }
                _saveConfirmTimer.Stop();
                _saveConfirmTimer.Start();
            }
            catch (Exception ex) { Utils.Logger.Swallow("SaveConfirm", ex); }
        }

        /// <summary>Puts a confirmed button's original caption back.</summary>
        private void RestoreSaveButtonText()
        {
            try
            {
                if (_saveConfirmTarget != null && !_saveConfirmTarget.IsDisposed && _saveBtnText != null)
                {
                    _saveConfirmTarget.Text = _saveBtnText;
                }
            }
            catch { }
            _saveConfirmTarget = null;
            _saveBtnText = null;
        }

        // The four per-surface background handlers that used to live here (OnChooseBackgroundGif
        // / OnClearBackgroundGif and their "2" variants) are gone. The background became
        // ONE full-window wallpaper, OnChooseFullGif / OnClearFullGif replaced them, and
        // nothing had wired the old four to a control since — they compiled and shipped
        // in every release doing nothing, while reading like working code.

        private void OnChooseFullGif(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = Localization.T("Choose a full-window background image (GIF animates)"),
                Filter = Localization.T("Images (*.gif;*.png;*.jpg;*.jpeg)|*.gif;*.png;*.jpg;*.jpeg|All files (*.*)|*.*")
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                if (!CanLoadImageFile(dlg.FileName))
                {
                    ShowInfo(Localization.F("That file couldn't be loaded as an image, so it wasn't applied.\n\n{0}", dlg.FileName));
                    return;
                }
                // Keep our OWN copy rather than pointing at wherever the user picked it
                // from. The chosen file is very often in Downloads or on a USB stick, and
                // a path is not ownership: clear out Downloads and the window's
                // background silently disappears on the next launch, with nothing but a
                // line in the log to say why. The custom logo has always copied itself
                // into the data folder for exactly this reason; the background was the
                // odd one out. Falls back to referencing the original if the copy fails,
                // which is no worse than the old behaviour.
                string stored = CopyBackgroundIntoDataFolder(dlg.FileName);

                // The single background is authoritative: it is the sole source now
                // (the legacy header/footer slots are cleared so they can't linger as a
                // fallback that a later Clear wouldn't remove).
                _settings.FullBackgroundGifPath = stored ?? dlg.FileName;
                _settings.BackgroundGifPath = "";
                _settings.BackgroundGifPath2 = "";
                try { Persistence.SettingsManager.Save(_settings); } catch { }
                ApplyBackgroundGif();
            }
        }

        /// <summary>
        /// Copies a chosen background into Tempo's data folder and returns the new path,
        /// or null if it couldn't be copied (the caller then falls back to the original
        /// location). Any previous copy is removed first, so this never accumulates.
        /// </summary>
        /// <summary>
        /// Extensions <see cref="CopyBackgroundIntoDataFolder"/> is allowed to write and
        /// therefore allowed to delete. A bare "background.*" glob would also match
        /// something like a background.json a future version might keep beside it, and
        /// this code deletes what it matches — so it only ever removes files it could
        /// have written itself.
        /// </summary>
        private static readonly string[] BackgroundExtensions =
            { ".gif", ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".img" };

        /// <summary>Removes any background copy this feature previously stored.</summary>
        private static void DeleteStoredBackgrounds(string dir)
        {
            try
            {
                if (!System.IO.Directory.Exists(dir)) { return; }
                foreach (string old in System.IO.Directory.GetFiles(dir, "background.*"))
                {
                    string ext = System.IO.Path.GetExtension(old);
                    bool ours = false;
                    foreach (string allowed in BackgroundExtensions)
                    {
                        if (string.Equals(ext, allowed, StringComparison.OrdinalIgnoreCase)) { ours = true; break; }
                    }
                    if (!ours) { continue; }
                    try { System.IO.File.Delete(old); } catch { }
                }
            }
            catch { }
        }

        private static string CopyBackgroundIntoDataFolder(string source)
        {
            const string BaseName = "background";
            try
            {
                string dir = Persistence.SettingsManager.GetSettingsDirectory();
                System.IO.Directory.CreateDirectory(dir);

                // Drop the previous copy whatever its extension was, so switching from a
                // .gif to a .png doesn't leave the old one behind forever.
                DeleteStoredBackgrounds(dir);

                string ext = System.IO.Path.GetExtension(source);
                if (string.IsNullOrEmpty(ext)) { ext = ".img"; }
                string dest = System.IO.Path.Combine(dir, BaseName + ext);

                // Copying onto ourselves would truncate the file we are copying — this
                // happens the moment someone re-picks the stored copy from the dialog.
                if (string.Equals(System.IO.Path.GetFullPath(source),
                                  System.IO.Path.GetFullPath(dest),
                                  StringComparison.OrdinalIgnoreCase))
                {
                    return dest;
                }

                System.IO.File.Copy(source, dest, true);
                return dest;
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("Couldn't copy the background into the data folder (" +
                                  ex.GetType().Name + "); using the original location instead.");
                return null;
            }
        }

        private void OnClearFullGif(object sender, EventArgs e)
        {
            // Drop our stored copy too — otherwise clearing the background leaves an
            // orphaned file in the data folder that nothing will ever reference again.
            DeleteStoredBackgrounds(Persistence.SettingsManager.GetSettingsDirectory());

            // Clear ALL backdrop slots so nothing remains as a fallback source.
            _settings.FullBackgroundGifPath = "";
            _settings.BackgroundGifPath = "";
            _settings.BackgroundGifPath2 = "";
            try { Persistence.SettingsManager.Save(_settings); } catch { }
            ApplyBackgroundGif();
        }

        /// <summary>
        /// Puts a wholesale replacement of <c>_settings</c> into effect everywhere.
        ///
        /// Reset-to-defaults and Import both swap the whole object out, and each used to
        /// carry its own hand-written list of what to re-apply afterwards. The two lists
        /// had drifted apart, and each had gaps the other did not: Reset never updated the
        /// Windows startup entry, Import never reloaded the background, and NEITHER
        /// re-ran the parts of the UI that are filled in once while their page is built,
        /// or the statics that no control event can reach.
        ///
        /// Anything that must be PUSHED rather than observed belongs here, so the two
        /// paths cannot disagree again.
        /// </summary>
        private void ApplyReplacedSettings(AppSettings before)
        {
            _settings.EnsureConsistency();
            _lifetimeBaseline = _settings.LifetimeClicks;
            SettingsManager.Save(_settings);

            LoadSettingsIntoUi();
            LoadKeybindsIntoUi();

            // Populated when the Clicker page is built and never again, so without this
            // the anti-freeze tick, its CPU threshold and the repeat-finished alert all
            // keep showing the settings that were just replaced.
            LoadAntiFreezeIntoUi();

            // Statics and OS state. LoadSettingsIntoUi runs with events suppressed, so
            // nothing downstream of a control event happens on its own.
            Utils.AnimatedLogo.SetEnabled(_settings.AnimateCustomLogo);
            Utils.AnimatedLogo.SpeedPercent = _settings.LogoAnimationSpeed;
            ApplyHdrCompensationSetting();
            try { StartupManager.SetEnabled(_settings.LaunchAtStartup); }
            catch (Exception ex) { Utils.Logger.Swallow("StartupManager.SetEnabled", ex); }

            ApplyThemeToEverything();
            ApplyHotkeysFromSettings();
            RefreshTraySleepNotice();
            ReassertTopMost();
            ApplyBackgroundGif();
            if (_trayAlwaysOnTopItem != null)
            {
                _trayAlwaysOnTopItem.Checked = _settings.AlwaysOnTop;
            }

            // The rest was missing, so each of these kept doing what the REPLACED settings said
            // until Tempo restarted: the log went on writing (or stayed silent), the window kept
            // its opacity, the notification mirror and the clipboard watcher kept running, the
            // running badge and caption bar kept their look, the second cursor stayed up, and a
            // running caption session kept its old engine.
            Utils.Logger.Enabled = _settings.WriteLogFile;
            try { Opacity = Math.Max(50, Math.Min(100, _settings.WindowOpacity)) / 100.0; } catch { }
            ApplyNotificationSettings();
            ApplyClipboardImageWatcher();
            RefreshNotifyStatus();
            ShowClickingIndicator(_engine != null && _engine.IsRunning);
            ApplyCaptionSettingsToOverlays();
            ApplySecondCursorSettings();
            ApplyMovementTuning();
            if (before != null)
            {
                ApplyChangedCaptionEngine(before);
                if (AccountSettingsDiffer(before, _settings))
                {
                    // Import can switch the account manager, multi-instance or the local API;
                    // the page starts and stops those to match only when it is re-evaluated.
                    try { RefreshAccountsView(); }
                    catch (Exception ex) { Utils.Logger.Swallow("RefreshAccountsView after replace", ex); }
                }
            }
        }

        /// <summary>
        /// A replaced caption setting on a RUNNING caption session: the same live steps Save
        /// Settings takes, which Reset and Import skipped.
        /// </summary>
        private void ApplyChangedCaptionEngine(AppSettings before)
        {
            if (before.CaptionSpeakerTurns != _settings.CaptionSpeakerTurns ||
                before.CaptionFaceAnalysis != _settings.CaptionFaceAnalysis)
            {
                ApplySpeakerTurnsLive();
            }
            if (before.CaptionFilterOwnVoice != _settings.CaptionFilterOwnVoice)
            {
                ApplyOwnVoiceGuardLive();
            }

            bool modelChanged =
                !string.Equals(before.CaptionModelKey, _settings.CaptionModelKey, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(before.CaptionCustomModelPath ?? "", _settings.CaptionCustomModelPath ?? "", StringComparison.OrdinalIgnoreCase);
            if (modelChanged)
            {
                // An explicit model change beats the session's too-slow downgrade override.
                _captionModelOverrideKey = null;
                _modelRecoveryBlocked = false;
            }
            bool engineChanged = modelChanged
                || before.CaptionSource != _settings.CaptionSource
                || before.CaptionCaptureMode != _settings.CaptionCaptureMode
                || !string.Equals(before.CaptionLanguage ?? "", _settings.CaptionLanguage ?? "", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(before.CaptionSpeakerDeviceId ?? "", _settings.CaptionSpeakerDeviceId ?? "", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(before.CaptionMicDeviceId ?? "", _settings.CaptionMicDeviceId ?? "", StringComparison.OrdinalIgnoreCase);
            if (engineChanged && _captionsActive)
            {
                // None of these reach a running session on their own; the engine restarts in
                // place, keeping the session's transcript (an off/on cycle started a new one).
                RestartCaptionEngine("caption settings replaced");
            }

            if (!_settings.CaptionOverlayEnabled && _captionOverlay != null && !_captionOverlay.IsDisposed)
            {
                try { _captionOverlay.Hide(); } catch { }
            }
        }

        private static bool AccountSettingsDiffer(AppSettings a, AppSettings b) =>
            a.AccountsEnabled != b.AccountsEnabled
            || a.RobloxMultiInstance != b.RobloxMultiInstance
            || a.AccountApiEnabled != b.AccountApiEnabled
            || a.AccountApiPort != b.AccountApiPort
            || !string.Equals(a.AccountApiToken ?? "", b.AccountApiToken ?? "", StringComparison.Ordinal);

        private static bool HasAnyBackground(AppSettings s) =>
            s != null && (!string.IsNullOrWhiteSpace(s.FullBackgroundGifPath)
                          || !string.IsNullOrWhiteSpace(s.BackgroundGifPath)
                          || !string.IsNullOrWhiteSpace(s.BackgroundGifPath2));

        private void OnResetSettings(object sender, EventArgs e)
        {
            // Says what is KEPT. "Reset all settings?" alone read as if statistics, keybinds and
            // the account manager went too — and until CreateResetFrom, they did.
            var confirm = MessageBox.Show(this,
                Localization.T("Reset all settings to their defaults?\n\nYour profiles, macros, keybinds, statistics, Roblox accounts and language are kept."),
                "Tempo",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            AppSettings before = _settings;
            _settings = AppSettings.CreateResetFrom(before);

            // The wallpaper goes back to none. Drop Tempo's stored copy the way Clear does, or the
            // reset leaves an image in the data folder that nothing will reference again.
            if (HasAnyBackground(before) && !HasAnyBackground(_settings))
            {
                DeleteStoredBackgrounds(Persistence.SettingsManager.GetSettingsDirectory());
            }

            ApplyReplacedSettings(before);
            Utils.Logger.Info("[Settings] reset to defaults (profiles, macros, keybinds, statistics, accounts and language kept).");
        }

        /// <summary>Copies the data folder (profiles, macros, settings, history — everything
        /// except the signed-in browser profiles) into a timestamped sub-folder of
        /// <paramref name="destRoot"/>.
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

                // Not the signed-in browser profiles. Each holds a LIVE Roblox session outside the encrypted
                // vault — the reason removing an account, turning the Account Manager off and uninstalling all
                // delete them — so a backup must not scatter copies into whatever folder was picked. They were
                // also the one thing that could sink the whole backup: a browser with a profile open holds its
                // cookie store locked, File.Copy threw, and nothing was backed up. Signing in recreates them.
                string browserProfiles = RobloxAccountBrowser.BaseDir.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;

                foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                {
                    if (file.StartsWith(browserProfiles, StringComparison.OrdinalIgnoreCase)) { continue; }
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
                Description = Localization.T("Choose a folder to save your Tempo backup in")
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return false;
                }

                if (BackupAllData(dlg.SelectedPath, out string res))
                {
                    ShowInfo(Localization.F("Backup saved to:\n{0}", res));
                    return true;
                }

                ShowWarning(Localization.F("Backup failed: {0}", res));
                return false;
            }
        }

        private void OnBackupAllData(object sender, EventArgs e)
        {
            // Make sure what's in memory is on disk before we copy the folder.
            try { CaptureSettingsFromUi(); SettingsManager.Save(_settings); } catch { }
            PromptAndBackupAllData();
        }

        /// <summary>
        /// "Diagnose Windows bar" (CaptionDiagnosticForm). The checks run in the background: its UI
        /// Automation walks are cross-process calls that wait on Live Captions itself, and run on the UI
        /// thread they froze Tempo for as long as Windows took to answer.
        /// </summary>
        private void ShowCaptionDiagnostic()
        {
            try
            {
                // Tempo's own state, read here on the UI thread before the background check starts.
                string source = _settings != null ? _settings.CaptionSource.ToString() : "?";
                bool engineRunning = _captionTranscriber != null && _captionTranscriber.IsRunning;
                bool movedByTempo = _captionReader != null && _captionReader.WindowsBarMovedOffscreen;
                string version = VersionStamp() + "  ·  " + Utils.BuildInfo.Short;
                string officialId = Utils.IntegrityCheck.OfficialIdReportLine();
                using (var dlg = new CaptionDiagnosticForm(_theme,
                           () => DiagnoseWindowsCaptions(source, engineRunning, movedByTempo, version, officialId),
                           () => Utils.LiveCaptionReader.LaunchWindowsCaptions(),
                           () => _settings != null && _settings.CaptionSource == CaptionSource.Windows,
                           UseTempoCaptionsFromDiagnostic))
                {
                    dlg.ShowDialog(this);
                }
            }
            catch (Exception ex) { Utils.Logger.Warn("Caption diagnostic failed: " + ex.Message); }
        }

        /// <summary>"Use Tempo's own captions" in the diagnostic: the caption source switched, saved, and a running engine moved over in place.</summary>
        private void UseTempoCaptionsFromDiagnostic()
        {
            if (_settings == null) { return; }
            CaptionSource before = _settings.CaptionSource;
            _settings.CaptionSource = CaptionSource.Tempo;
            bool was = _suppressSettingsEvents;
            _suppressSettingsEvents = true;
            try
            {
                if (_captionSourceCombo != null && _captionSourceCombo.Items.Count > (int)CaptionSource.Tempo)
                {
                    _captionSourceCombo.SelectedIndex = (int)CaptionSource.Tempo;
                }
            }
            finally { _suppressSettingsEvents = was; }
            UpdateCaptionSourceUi();
            PersistNotificationSettings();   // the shared "save settings now" helper
            Utils.Logger.Info("[Captions] caption source set to Tempo's own engine from the Windows bar diagnostic.");
            if (before != _settings.CaptionSource && _captionsActive)
            {
                RestartCaptionEngine("caption source changed from the Windows bar diagnostic");
            }
        }

        /// <summary>
        /// The checks behind the diagnostic. Runs on a WORKER thread, so it touches nothing of the UI's: a
        /// fresh reader (the live one belongs to the caption pipeline), and Tempo's state handed in.
        /// </summary>
        private static CaptionDiagnosticForm.Result DiagnoseWindowsCaptions(string source, bool engineRunning, bool movedByTempo,
                                                                            string version, string officialId)
        {
            var r = new CaptionDiagnosticForm.Result();
            Version os = Environment.OSVersion.Version;
            r.Windows11 = os.Major > 10 || (os.Major == 10 && os.Build >= 22000);
            r.Installed = Utils.LiveCaptionReader.IsInstalled;
            r.ProcessRunning = Utils.LiveCaptionReader.IsProcessRunning;

            var reader = new Utils.LiveCaptionReader();
            try { r.BarVisible = reader.IsWindowPresentFast(); } catch { }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try { r.Found = reader.Locate(); } catch { }
            long locateMs = clock.ElapsedMilliseconds;

            string readResult = "(not run: the bar was not found)";
            if (r.Found)
            {
                clock.Restart();
                try
                {
                    string t = reader.ReadText();
                    r.ReadMs = clock.ElapsedMilliseconds;
                    r.ReadOk = !reader.UiaBroken;
                    r.ReadChars = string.IsNullOrWhiteSpace(t) ? 0 : t.Trim().Length;
                    readResult = !r.ReadOk ? "FAILED: UI Automation would not read the bar"
                               : r.ReadChars > 0 ? "OK: " + r.ReadChars + " characters in " + r.ReadMs + " ms"
                               : "(empty: nothing is being said, or nothing readable) in " + r.ReadMs + " ms";
                }
                catch (Exception ex) { readResult = "ERROR: " + ex.Message; }

                Rectangle? at = reader.WindowRect();
                if (at.HasValue)
                {
                    bool onAScreen = false;
                    try { foreach (Screen s in Screen.AllScreens) { if (s.Bounds.IntersectsWith(at.Value)) { onAScreen = true; break; } } }
                    catch { onAScreen = true; }
                    r.OffScreen = !onAScreen;
                    r.MovedAsideByTempo = r.OffScreen && movedByTempo;
                }
            }
            r.UiaOk = !reader.UiaBroken;
            r.Verdict = !r.Windows11 || !r.Installed ? "unsupported" : !r.Found ? "off" : !r.UiaOk ? "uia" : "works";

            string dump;
            try { dump = reader.DescribeCandidateWindows(); }
            catch (Exception ex) { dump = "(window list failed: " + ex.Message + ")"; }
            string selfTest = "DETECTION: " + (r.Found ? "FOUND the window" : "NOT found") + " (" + locateMs + " ms)\r\nREADING TEST: " + readResult;
            r.Report = BuildWindowsCaptionReport(r, os, source, engineRunning, version, officialId, selfTest, dump);
            return r;
        }

        /// <summary>
        /// The technical half of the diagnostic, in English for a bug report: what this PC has, the
        /// self-test, and the windows that could be the captions bar (other windows only counted).
        /// </summary>
        private static string BuildWindowsCaptionReport(CaptionDiagnosticForm.Result r, Version os, string source,
                                                        bool engineRunning, string version, string officialId,
                                                        string selfTest, string dump)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== THIS PC ===");
            sb.AppendLine("Windows          : " + os + (r.Windows11 ? "  (Windows 11)" : "  (before Windows 11: no Live Captions)"));
            // Build ID, not just the version — a bug report against "1.0.319" is
            // ambiguous between the release and every test build cut from it.
            sb.AppendLine("Tempo            : " + version);
            if (Utils.BuildInfo.SourceText.Length > 0) { sb.AppendLine("Source           : " + Utils.BuildInfo.SourceText); }
            sb.AppendLine("Official ID      : " + officialId);
            sb.AppendLine("Caption source   : " + source);
            sb.AppendLine("Tempo engine     : " + (engineRunning ? "running" : "not running"));
            sb.AppendLine("LiveCaptions.exe : " + (r.Installed ? "present" : "MISSING") + ", " + (r.ProcessRunning ? "running" : "not running"));
            sb.AppendLine("Bar on screen    : " + (r.BarVisible ? "yes" : "no") + (r.MovedAsideByTempo ? "  (moved aside by Tempo)" : r.OffScreen ? "  (off screen)" : ""));
            sb.AppendLine("Bar detected     : " + (r.Found ? "yes" : "no"));
            sb.AppendLine("UI Automation    : " + (r.UiaOk ? "ok" : "FAILED (cannot read text)"));
            sb.AppendLine();
            sb.AppendLine("=== SELF-TEST ===");
            sb.AppendLine(string.IsNullOrWhiteSpace(selfTest) ? "(none)" : selfTest.TrimEnd());
            sb.AppendLine();
            sb.AppendLine("=== CANDIDATE WINDOWS (technical detail) ===");
            sb.AppendLine(string.IsNullOrWhiteSpace(dump) ? "(no windows reported)" : dump.TrimEnd());
            return sb.ToString();
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
                ShowWarning(Localization.F("Could not open the data folder: {0}", ex.Message));
            }
        }

        private void OnEmailBug(object sender, EventArgs e)
        {
            EmailReportChannel last = EmailReportChannel.None;
            if (_settings != null &&
                Enum.TryParse(_settings.LastBugReportChannel, out EmailReportChannel parsed))
            {
                last = parsed;
            }
            ComposeAndSendReport(last);
        }

        /// <summary>
        /// Shows the report, then sends whatever the user left in the box.
        ///
        /// Both "Report a bug…" and "Email a bug…" come through here, so the review
        /// step and the privacy promise cannot be true of one entry point and not the
        /// other — which is exactly what happened before, when the GitHub button
        /// opened a browser straight away with a body nobody had seen.
        /// </summary>
        private void ComposeAndSendReport(EmailReportChannel preselect)
        {
            EmailReportChannel choice;
            string body;
            using (var dlg = new EmailReportChooserForm(_theme, preselect))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                choice = dlg.Choice;
                body = dlg.ReportText;
            }

            if (choice == EmailReportChannel.None)
            {
                return;
            }

            // The channel is remembered only once the hand-off actually worked. Saving
            // it up front meant a mail app that failed to open was still recorded as
            // "last used", so the next report defaulted to the one channel known to be
            // broken on this PC.
            bool sent;
            switch (choice)
            {
                case EmailReportChannel.GitHub:
                    sent = OpenExternal(CrashReporter.IssueUrlFromBody(body), "browser");
                    break;
                case EmailReportChannel.EmailApp:
                    // A mailto: longer than the command line allows is silently cut
                    // off, so the report would arrive truncated with nothing saying
                    // so. Offer the clipboard, which has no such limit, rather than
                    // sending half a bug report.
                    if (CrashReporter.MailtoWouldTruncate(body))
                    {
                        var answer = MessageBox.Show(this,
                            Localization.T("This report is too long for your email app to carry — it would "
                                + "arrive cut off part-way through, most likely losing the activity log at "
                                + "the end.\n\nCopy it to the clipboard instead? You can paste the whole "
                                + "thing into a new email."),
                            "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (answer != DialogResult.Yes)
                        {
                            return;
                        }
                        sent = CopyReportToClipboard(body);
                        choice = EmailReportChannel.Copy;
                        break;
                    }
                    sent = OpenExternal(CrashReporter.BuildMailtoUrlFor(body), "email app");
                    break;
                case EmailReportChannel.Gmail:
                    sent = OpenExternal(CrashReporter.BuildGmailComposeUrlFor(body), "browser");
                    break;
                case EmailReportChannel.Outlook:
                    sent = OpenExternal(CrashReporter.BuildOutlookComposeUrlFor(body), "browser");
                    break;
                case EmailReportChannel.Yahoo:
                    sent = OpenExternal(CrashReporter.BuildYahooComposeUrlFor(body), "browser");
                    break;
                case EmailReportChannel.Copy:
                    sent = CopyReportToClipboard(body);
                    break;
                default:
                    return;
            }

            if (sent && _settings != null)
            {
                _settings.LastBugReportChannel = choice.ToString();
                SettingsManager.Save(_settings);
            }
        }

        /// <summary>Puts the report on the clipboard. Returns false if the clipboard refused.</summary>
        private bool CopyReportToClipboard(string body)
        {
            try
            {
                Clipboard.SetText(CrashReporter.BuildReportTextFor(body));
                ShowInfo(Localization.F("Bug report copied to your clipboard.\n\nPaste it into an email to {0} (or anywhere else) and add what happened.", CrashReporter.SupportEmail));
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn("[UI] clipboard copy failed: " + ex.Message);
                ShowInfo(Localization.F("Couldn't copy to the clipboard. You can email bug reports to:\n\n{0}", CrashReporter.SupportEmail));
                return false;
            }
        }

        /// <summary>
        /// Opens a URL (mailto, web compose, etc.) with the user's default handler.
        /// Returns false when nothing could be opened, so the caller can tell a
        /// completed hand-off from a failed one.
        /// </summary>
        private bool OpenExternal(string url, string what)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn("[UI] could not open " + what + ": " + ex.Message);
                ShowInfo(Localization.F("Couldn't open your {0}. You can email bug reports to:\n\n{1}", what, CrashReporter.SupportEmail));
                return false;
            }
        }

        /// <summary>Re-runs the tamper check on demand and shows the result.</summary>
        private void OnRecheckIntegrity(object sender, EventArgs e) => RecheckIntegrity(null);

        /// <summary>Re-runs the tamper check; <paramref name="then"/> runs on the UI thread once the result is shown.</summary>
        private void RecheckIntegrity(Action then)
        {
            try
            {
                if (_integrityStatusLabel != null)
                {
                    _integrityStatusLabel.Text = Localization.T("Checking…");
                    _integrityStatusLabel.ForeColor = _theme.TextMuted;
                }
                Utils.IntegrityCheck.RunInBackground(_settings, verdict =>
                {
                    try
                    {
                        if (IsDisposed || !IsHandleCreated) { return; }
                        BeginInvoke((Action)(() =>
                        {
                            // A manual check still records a fresh fingerprint when
                            // there wasn't one, and still warns — same path as start-up,
                            // so the two can't disagree about what they found.
                            OnIntegrityResult(verdict);
                            RefreshIntegrityStatus();
                            try { then?.Invoke(); } catch (Exception ex) { Logger.Swallow("RecheckIntegrity.then", ex); }
                        }));
                    }
                    catch (Exception ex) { Logger.Swallow("OnRecheckIntegrity.post", ex); }
                });
            }
            catch (Exception ex) { Logger.Swallow("OnRecheckIntegrity", ex); }
        }

        /// <summary>
        /// Accepts the file as it is now, replacing the stored fingerprint.
        ///
        /// Confirmed first, and worded so the consequence is plain: this is the one
        /// action that makes a real warning go away without fixing anything, so it must
        /// not be something a person clicks to dismiss a nag.
        /// </summary>
        private void OnTrustThisCopy(object sender, EventArgs e)
        {
            // An altered UNPACKED copy is not something to accept — the same button repairs it instead.
            if (Utils.IntegrityCheck.Verdict == Utils.IntegrityVerdict.UnpackedModified)
            {
                OnRepairUnpackedCopy();
                return;
            }
            // Nor is code planted beside the exe: trusting the exe would change nothing about it. The button
            // shows where those files are instead.
            if (Utils.IntegrityCheck.Verdict == Utils.IntegrityVerdict.ForeignCode)
            {
                ShowForeignFiles();
                return;
            }
            try
            {
                var answer = MessageBox.Show(this,
                    Localization.T("Record this copy of Tempo.exe as the one you trust?\n\n"
                        + "Only do this if you know why the file changed — for example you "
                        + "installed a build yourself. If you did not change it, do not accept "
                        + "it: reinstall from the official download instead."),
                    "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    return;
                }

                Utils.IntegrityCheck.ResetBaseline(_settings);
                _settings.IntegrityLastWarned = "";
                SettingsManager.Save(_settings);
                Logger.Warn("[Integrity] the user accepted the current Tempo.exe as trusted.");
                _integritySuppressNextWarning = true;
                OnRecheckIntegrity(sender, e);   // re-runs and re-records
            }
            catch (Exception ex) { Logger.Swallow("OnTrustThisCopy", ex); }
        }

        /// <summary>
        /// Repairs an altered unpacked copy. After a confirmation, a helper waits for Tempo to exit,
        /// deletes the folder the .NET host unpacked Tempo into, and starts Tempo again — which unpacks
        /// a clean copy from Tempo.exe. The folder is held open while Tempo runs, so it cannot be
        /// cleaned from inside, and reinstalling the same exe would simply reuse it.
        /// </summary>
        private void OnRepairUnpackedCopy()
        {
            try
            {
                string dir = Utils.IntegrityCheck.UnpackDirectory;
                if (!Utils.IntegrityCheck.IsRepairable || string.IsNullOrEmpty(dir))
                {
                    return;
                }
                if (_recorder != null && _recorder.IsRecording)
                {
                    // Exiting would throw the take away; the updater asks the same question.
                    ShowInfo("Stop the macro recording first, then press Repair.");
                    return;
                }

                var answer = MessageBox.Show(this,
                    Localization.T("Repair Tempo?\n\nTempo will close, delete the unpacked copy it runs from in your TEMP folder, and start again — unpacking a clean copy from Tempo.exe. Your settings, profiles, macros and accounts are not touched."),
                    "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    return;
                }

                if (!UpdateInstaller.LaunchRepairUnpackHelper(dir, out string err))
                {
                    ShowWarning(Localization.F("Tempo couldn't start the repair. Close Tempo, delete this folder, and start Tempo again:\n\n{0}", dir)
                                + "\n\n(" + err + ")");
                    return;
                }

                // The warning token is cleared so a later tampering is announced again. Then the
                // ORDINARY close (see CloseForHelper): this used to Environment.Exit, which skips the
                // whole shutdown — the run in progress, captions, held keys, profiles and macros.
                _settings.IntegrityLastWarned = "";
                try { SettingsManager.Save(_settings); } catch { }
                Logger.Warn("[shutdown] closing so the altered unpacked copy is replaced with a clean one.");
                CloseForHelper("the altered unpacked copy is replaced with a clean one");
            }
            catch (Exception ex) { Logger.Swallow("OnRepairUnpackedCopy", ex); }
        }

        /// <summary>Puts the current verdict on the Settings page, in the theme's colours.</summary>
        private void RefreshIntegrityStatus()
        {
            try
            {
                if (_integrityStatusLabel == null || _integrityStatusLabel.IsDisposed) { return; }

                if (_settings != null && !_settings.IntegrityCheckEnabled)
                {
                    _integrityStatusLabel.Text = Localization.T(
                        "Off — Tempo will not notice if its program file is replaced.");
                    _integrityStatusLabel.ForeColor = _theme.TextMuted;
                    return;
                }

                _integrityStatusLabel.Text = IntegrityStatusText(Utils.IntegrityCheck.Verdict, out Color colour,
                    Utils.IntegrityCheck.OfficialTag, Utils.IntegrityCheck.OfficialId);
                _integrityStatusLabel.ForeColor = colour;
                if (_integrityTrustBtn != null)
                {
                    // An altered unpacked copy is repaired, not trusted, and planted code is found and deleted —
                    // the button offers what fixes each.
                    _integrityTrustBtn.Text = Utils.IntegrityCheck.Verdict == Utils.IntegrityVerdict.UnpackedModified
                        ? Localization.T("Repair")
                        : Utils.IntegrityCheck.Verdict == Utils.IntegrityVerdict.ForeignCode
                            ? Localization.T("Show files")
                            : Localization.T("Trust this copy");
                }
            }
            catch (Exception ex) { Logger.Swallow("RefreshIntegrityStatus", ex); }
        }

        /// <summary>
        /// The Settings sentence for a verdict, and its colour — also the Details window's headline.
        ///
        /// A whole translated sentence per verdict rather than a translated wrapper around
        /// IntegrityCheck.Summary. Summary is diagnostic English written for the log and Live Debug —
        /// formatting it into "✗ {0}" would have produced a label whose frame was Spanish and whose content
        /// was English, which reads worse than either.
        /// </summary>
        private string IntegrityStatusText(Utils.IntegrityVerdict verdict, out Color colour,
                                           string officialTag, string officialId)
        {
            colour = _theme.TextMuted;
            // The program file by its real name: an install is TempoClicker.exe since the
            // Discord rename, so "Tempo.exe is the file that was installed" named a file
            // that is not there.
            string exeName = System.IO.Path.GetFileName(Utils.DeploymentInfo.ExecutablePath);
            if (string.IsNullOrEmpty(exeName)) { exeName = Utils.DeploymentInfo.LegacyExeName; }
            switch (verdict)
            {
                case Utils.IntegrityVerdict.Genuine:
                    // The strongest thing Tempo can say, and worth saying distinctly:
                    // this was checked against GitHub, not against a note this PC
                    // wrote to itself. With the Official ID anyone can match on the release page.
                    colour = _theme.Success;
                    return officialId != null
                        ? Localization.F("✓ Verified against GitHub's {0} release — Official ID {1}.", officialTag, officialId)
                        : Localization.T("✓ Verified against the release published on GitHub.");
                case Utils.IntegrityVerdict.UnknownRelease:
                    colour = _theme.Danger;
                    return Localization.T("⚠ No release with this version number exists on GitHub, "
                        + "so nothing outside this PC can confirm the file. Normal for a build you "
                        + "made yourself.");
                case Utils.IntegrityVerdict.Ok:
                case Utils.IntegrityVerdict.Baselined:
                    colour = _theme.Success;
                    return Localization.F("✓ {0} is the file that was installed.", exeName);
                case Utils.IntegrityVerdict.Modified:
                    colour = _theme.Danger;
                    return Localization.T("✗ This copy does not match the release published for its "
                        + "version number. If you did not replace it yourself, reinstall from the "
                        + "official download.");
                case Utils.IntegrityVerdict.Damaged:
                    colour = _theme.Danger;
                    return Localization.F("✗ {0} is damaged — part of it is unreadable. "
                        + "Reinstalling fixes it.", exeName);
                case Utils.IntegrityVerdict.Repackaged:
                    colour = _theme.Danger;
                    return Localization.T("✗ This copy was packaged by someone else, "
                        + "not the official Tempo build.");
                case Utils.IntegrityVerdict.UnpackedModified:
                    colour = _theme.Danger;
                    return Localization.F("✗ {0} is intact, but the unpacked copy Tempo runs from was altered — "
                        + "the code running is not what {0} carries. Press Repair to unpack a clean copy.", exeName);
                case Utils.IntegrityVerdict.ForeignCode:
                    colour = _theme.Danger;
                    return Localization.F("✗ Windows loaded program files into Tempo from the folder {0} is in, "
                        + "and Tempo never puts any there. Press Show files, delete them, and restart Tempo.", exeName);
                case Utils.IntegrityVerdict.TestBuild:
                    // Deliberately not green. A test build is not a problem, but it
                    // has not been vouched for by anything either, and saying "✓" for
                    // a file nothing outside this PC has confirmed would be the check
                    // telling a comfortable lie.
                    colour = _theme.WarningText;
                    return Localization.F(
                        "⚠ This is a test build ({0}), not a published release — nothing "
                        + "outside this PC has confirmed it. Install an official release if you "
                        + "did not build this yourself.", Utils.BuildInfo.Id);
                case Utils.IntegrityVerdict.Unknown:
                    return Localization.T("Not checked yet.");
                default:
                    return Localization.T("Could not be checked right now.");
            }
        }

        /// <summary>
        /// The height the Settings sentence needs for the longest sentence any verdict produces in this
        /// language, at <paramref name="width"/> in the page's build units. The verdict arrives seconds after
        /// the page is built, so the label is sized for all of them rather than for the one on show then.
        /// </summary>
        private int LongestIntegrityStatusHeight(Font font, int width)
        {
            int tallest = 0;
            try
            {
                foreach (Utils.IntegrityVerdict v in Enum.GetValues(typeof(Utils.IntegrityVerdict)))
                {
                    string text = IntegrityStatusText(v, out _, "v1.0.000", "CC21-FB7A-D6F3-8831");
                    tallest = Math.Max(tallest, MeasureLayout(text, font, width, TextFormatFlags.Default).Height);
                }
                tallest = Math.Max(tallest, MeasureLayout(
                    Localization.T("Off — Tempo will not notice if its program file is replaced."), font, width,
                    TextFormatFlags.Default).Height);
            }
            catch (Exception ex) { Logger.Swallow("LongestIntegrityStatusHeight", ex); }
            return tallest + 2;
        }

        /// <summary>The Details window: every layer of the last check, the file's facts, and a copy for bug reports.</summary>
        private void ShowIntegrityDetails()
        {
            try
            {
                string headline;
                Color colour;
                bool enabled = _settings == null || _settings.IntegrityCheckEnabled;
                if (!enabled)
                {
                    headline = Localization.T("Off — Tempo will not notice if its program file is replaced.");
                    colour = _theme.TextMuted;
                }
                else
                {
                    headline = IntegrityStatusText(Utils.IntegrityCheck.Verdict, out colour,
                        Utils.IntegrityCheck.OfficialTag, Utils.IntegrityCheck.OfficialId);
                }
                IntegrityDetailsForm.Outcome outcome;
                using (var dlg = new IntegrityDetailsForm(_theme, Utils.IntegrityCheck.LastReport, headline, colour, enabled))
                {
                    dlg.ShowDialog(this);
                    outcome = dlg.Result;
                }
                if (outcome == IntegrityDetailsForm.Outcome.CheckAgain)
                {
                    // Run it, then open the window again on the new findings.
                    RecheckIntegrity(() => ShowIntegrityDetails());
                }
                else if (outcome == IntegrityDetailsForm.Outcome.ShowFiles)
                {
                    ShowForeignFiles();
                }
            }
            catch (Exception ex) { Logger.Swallow("ShowIntegrityDetails", ex); }
        }

        /// <summary>Opens Explorer on the first program file Windows loaded into Tempo from beside its exe.</summary>
        private void ShowForeignFiles()
        {
            try
            {
                Utils.IntegrityReport r = Utils.IntegrityCheck.LastReport;
                if (r == null || r.LoadedBeside.Count == 0) { return; }
                string first = r.LoadedBeside[0];
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                    Arguments = "/select,\"" + first + "\"",
                    UseShellExecute = false,
                });
                Logger.Info("[Integrity] showed the planted files to the user: " + string.Join(", ", r.LoadedBeside));
            }
            catch (Exception ex)
            {
                Logger.Warn("[Integrity] could not open Explorer on the planted files: " + ex.Message);
            }
        }

        private void OnReportBug(object sender, EventArgs e)
        {
            // Same composer as "Email a bug…", opened on GitHub. The button used to
            // launch the browser immediately with a pre-filled issue, which meant the
            // deliberate reporting path was the one with no review step.
            ComposeAndSendReport(EmailReportChannel.GitHub);
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

                // The logger keeps the file open while lines arrive; let it go so an editor that
                // won't share with a writer still opens it. The next line reopens it.
                Logger.Close();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowWarning(Localization.F("Could not open the log file: {0}", ex.Message));
            }
        }

        /// <summary>One key for the whole uninstall confirmation, so it can never go half-translated.</summary>
        private const string UninstallConfirmText =
            "Uninstall Tempo?\n\n"
            + "This removes Tempo from this PC:\n"
            + "   •  The program\n"
            + "   •  Start Menu and Desktop shortcuts\n"
            + "   •  The Windows start-up entry (if set)\n"
            + "   •  Tempo's entry in Settings > Apps\n"
            + "   •  Signed-in browser profiles Tempo opened for your accounts\n\n"
            + "Your profiles, macros, settings and account vault are kept unless you choose to delete them on "
            + "the next step. Continue?";

        /// <summary>The data question. {0} is the data folder. "No" (keep) is the default button.</summary>
        private const string UninstallDataText =
            "Also delete your data?\n\n"
            + "Your profiles, macros, settings, session history, account vault, restore points and downloaded "
            + "speech models are kept in:\n{0}\n\n"
            + "No — keep them. Installing Tempo again brings everything back.\n"
            + "Yes — delete them for good, together with Tempo's log files. This cannot be undone.";

        /// <summary>One key for the whole restore question.</summary>
        private const string RestoreConfirmText =
            "Restore the data saved {0}?\n\n"
            + "Tempo will restart and put back your settings, profiles, macros, history and account vault as they "
            + "were then. What you have now is saved as a restore point first, so you can undo this.";

        /// <summary>
        /// "Restore points…": lists what Tempo saved before each update and puts one back. The restore is
        /// not done here — the running Tempo saves profiles and macros as it closes, which would overwrite
        /// it — but scheduled, and applied by the next launch before anything loads.
        /// </summary>
        private void OnRestorePointsClicked(object sender, EventArgs e)
        {
            string dataDir = SettingsManager.GetSettingsDirectory();
            string scheduled = null;

            RecycleBinForm.Show(this, _theme,
                Localization.T("Restore points"),
                Localization.T("Tempo saves your settings, profiles, macros, history and account vault automatically the "
                               + "first time a new version starts. Restoring one puts that data back and restarts Tempo. "
                               + "What you have now is saved as a restore point first, so a restore can be undone."),
                new[] { Localization.T("Saved"), Localization.T("Reason"), Localization.T("Files") },
                new[] { 150, 270, 60 },
                Localization.T("No restore points yet. Tempo makes one the first time a new version starts."),
                "Delete all {0} restore point(s)? Your current data is not affected.",
                () => BuildRestorePointRows(dataDir),
                id =>
                {
                    if (scheduled != null) { return false; }   // one restore at a time
                    DataSnapshots.RestorePoint point = DataSnapshots.List(dataDir).Find(p => p.Id == id);
                    if (point == null) { return false; }
                    DialogResult go = MessageBox.Show(this,
                        Localization.F(RestoreConfirmText,
                            point.SavedLocal.ToString("g", Localization.DateCulture)),
                        "Restore points", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                    if (go != DialogResult.Yes) { return false; }
                    if (!DataSnapshots.ScheduleRestore(dataDir, id, out string err))
                    {
                        ShowWarning(Localization.F("Couldn't schedule the restore: {0}", Localization.T(err ?? "")));
                        return false;
                    }
                    scheduled = id;
                    return true;
                },
                () => DataSnapshots.DeleteAll(dataDir),
                Localization.T("Delete all"),
                closeOnRestore: true);

            if (scheduled != null)
            {
                FadeOutThenRestart("the restore point");
            }
        }

        private System.Collections.Generic.List<RecycleBinForm.Entry> BuildRestorePointRows(string dataDir)
        {
            var rows = new System.Collections.Generic.List<RecycleBinForm.Entry>();
            foreach (DataSnapshots.RestorePoint p in DataSnapshots.List(dataDir))
            {
                rows.Add(new RecycleBinForm.Entry
                {
                    Id = p.Id,
                    Cells = new[]
                    {
                        p.SavedLocal.ToString("g", Localization.DateCulture),
                        p.IsUndo ? Localization.T("Before a restore") : Localization.F("Tempo {0} started", p.Before),
                        p.Files.ToString(System.Globalization.CultureInfo.CurrentCulture)
                    }
                });
            }
            return rows;
        }

        /// <summary>One key for the whole install question, so it can never go half-translated.</summary>
        private const string InstallOfferText =
            "Install Tempo on this PC?\n\n"
            + "This copy is portable, so it isn't in the Start Menu or in Settings › Apps, and Windows "
            + "has no normal way to uninstall it.\n\n"
            + "Installing copies Tempo into your user folder, adds a Start Menu shortcut and an entry in "
            + "Settings › Apps, then restarts Tempo from there. No administrator rights are needed, and "
            + "your settings, profiles and macros stay exactly as they are.";

        private bool _installInProgress;

        // The Settings button, so it can say "Installing…" and refuse a second press while it works.
        // Null when this copy is not offered the install (it is installed, or must not install).
        private Button _installTempoBtn;
        private string _installTempoBtnText;

        /// <summary>"Install Tempo on this PC…" in Settings.</summary>
        private void OnInstallTempoClicked(object sender, EventArgs e)
        {
            if (_installInProgress) { return; }
            if (!Utils.DeploymentInfo.CanOfferInstall) { return; }   // installed meanwhile, or must not install
            DialogResult go = MessageBox.Show(this, InstallOfferText, "Install Tempo",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
            if (go == DialogResult.Yes)
            {
                InstallAndRelaunch();
            }
        }

        /// <summary>
        /// Installs this copy, then hands over to the installed copy through the same --restart path a
        /// language change uses.
        ///
        /// None of it runs on the UI thread, and the window says what it is doing the whole time. The
        /// copy and its two SHA-256 passes took 4.4 s for the 104 MB exe on a fast NVMe desktop
        /// (scratchpad/freezeprobe), and many times that on a laptop whose antivirus scans the new file
        /// — and all the window showed for it was a wait cursor, which to the person looking at it is
        /// Tempo not responding. It now covers itself with each step as it happens. The shell link is
        /// made on an STA thread of its own (shell links are apartment-threaded, as the start-up repoint
        /// already allows for), not on a pool thread.
        /// </summary>
        private async void InstallAndRelaunch()
        {
            if (_installInProgress) { return; }
            _installInProgress = true;
            SetInstallButtonBusy(true);

            long mb = 104;
            try { mb = Math.Max(1, (new FileInfo(Utils.DeploymentInfo.ExecutablePath).Length + 512 * 1024) / (1024 * 1024)); }
            catch { }
            ShowBusyOverlay(Localization.T("Installing Tempo on this PC…"),
                            InstallStageText(SelfInstaller.InstallStage.Copying, mb));

            SelfInstaller.Result r = null;
            try
            {
                r = await RunOnStaThread(() => SelfInstaller.InstallRunningCopy(stage =>
                {
                    // Reported on the install's own thread; the overlay belongs to this one.
                    try { BeginInvoke((Action)(() => SetBusyDetail(InstallStageText(stage, mb)))); } catch { }
                }), "Tempo install");
            }
            catch (Exception ex)
            {
                Logger.Warn("[Install] the install threw: " + ex.Message);
            }
            finally
            {
                _installInProgress = false;
            }

            if (r == null || !r.Ok)
            {
                HideBusyOverlay();
                SetInstallButtonBusy(false);
                ShowWarning(Localization.F("Tempo couldn't be installed. {0}",
                                           Localization.T(r?.Error ?? "")));
                return;
            }
            FadeOutThenRestart("the installation", r.InstalledExe);
        }

        /// <summary>What the busy overlay says while the install is at <paramref name="stage"/>.</summary>
        private static string InstallStageText(SelfInstaller.InstallStage stage, long mb)
        {
            switch (stage)
            {
                case SelfInstaller.InstallStage.Copying:
                    return Localization.F("Copying Tempo ({0} MB)…", mb);
                case SelfInstaller.InstallStage.Checking:
                    return Localization.T("Checking the copy…");
                case SelfInstaller.InstallStage.Shortcut:
                    return Localization.T("Adding the Start Menu shortcut…");
                default:
                    return Localization.T("Adding Tempo to Settings › Apps…");
            }
        }

        /// <summary>The Settings install button while an install runs: "Installing…", and not pressable.</summary>
        private void SetInstallButtonBusy(bool busy)
        {
            if (_installTempoBtn == null || _installTempoBtn.IsDisposed) { return; }
            _installTempoBtn.Enabled = !busy;
            _installTempoBtn.Text = busy ? Localization.T("Installing…") : _installTempoBtnText;
        }

        /// <summary>
        /// Runs <paramref name="work"/> on a background thread of its own in a single-threaded COM
        /// apartment, and completes on the caller's thread. For work that is slow AND touches the shell
        /// (shell links), which must not run on the UI thread and should not run on a pool thread either.
        /// </summary>
        private static System.Threading.Tasks.Task<T> RunOnStaThread<T>(Func<T> work, string name)
        {
            var done = new System.Threading.Tasks.TaskCompletionSource<T>(
                System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new System.Threading.Thread(() =>
            {
                try { done.SetResult(work()); }
                catch (Exception ex) { done.SetException(ex); }
            })
            {
                IsBackground = true,
                Name = name
            };
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            return done.Task;
        }

        private void OnUninstallClicked(object sender, EventArgs e)
        {
            string dataFolder;
            try { dataFolder = SettingsManager.GetSettingsDirectory(); }
            catch { dataFolder = "%LocalAppData%\\AutoClicker"; }

            // Uninstalling removes the PROGRAM; the user's data stays unless they choose otherwise — the same
            // default uninstall.cmd has always had. It used to be the other way round: the cleanup deleted the
            // whole data folder every time, only the program was optional, and that question defaulted to
            // KEEP — so Settings › Apps' Uninstall button, accepted with its defaults, deleted every profile
            // and macro, left Tempo.exe behind, and took Tempo out of the list.
            DialogResult confirm = MessageBox.Show(this,
                UninstallConfirmText,
                "Uninstall Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            // Keeping is the default button: deleting has to be chosen.
            DialogResult alsoData = MessageBox.Show(this,
                Localization.F(UninstallDataText, dataFolder),
                "Uninstall Tempo", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (alsoData == DialogResult.Cancel)
            {
                return;
            }

            bool deleteData = alsoData == DialogResult.Yes;

            if (deleteData)
            {
                // Only now is anything about to be deleted for good, so only now is a backup worth offering.
                DialogResult backup = MessageBox.Show(this,
                    Localization.T("Back up all your data first?\n\n" +
                    "Yes — choose a folder; Tempo copies all profiles, macros, settings\n" +
                    "         and history there before uninstalling\n" +
                    "No — uninstall without a backup"),
                    Localization.T("Uninstall Tempo"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1);

                if (backup == DialogResult.Cancel)
                {
                    return;
                }
                if (backup == DialogResult.Yes && !PromptAndBackupAllData())
                {
                    return;   // a cancelled or failed backup stops the uninstall, so nothing is lost
                }
            }

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
            // Also undo what INSTALLING registered: the Start Menu and Desktop
            // shortcuts and the Settings > Apps entry. Without this the in-app uninstall
            // left Windows still advertising Tempo as installed, pointing at files it
            // had just deleted — see Uninstaller.RemoveShellIntegration. Only what belongs
            // to THIS copy is removed, so uninstalling a portable copy leaves an installed one alone.
            Uninstaller.RemoveShellIntegration();

            // The program always goes — that is what uninstalling is. The data goes only if asked.
            if (Uninstaller.LaunchCleanupAndExitHelper(deleteData, true, out string err))
            {
                // The ORDINARY close, as for an update (CloseForHelper): the helper waits for this process,
                // not for a time. This used to be Environment.Exit, which saves nothing — and the data is KEPT
                // by default, so every change since the last save (the profile being edited, a macro, the
                // statistics) was thrown away from data the user had just chosen to keep, for whenever they
                // install Tempo again. When the data is being deleted, saving it first costs nothing: the
                // helper removes the folder after the process has gone.
                CloseForHelper(deleteData ? "Tempo and its data can be removed" : "Tempo can be removed (the data stays)",
                               resumeCaptions: false);
            }
            else
            {
                ShowWarning(err ?? Localization.T("Could not start the uninstaller."));
            }
        }

        private void OnExportSettings(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog
            {
                Title = Localization.T("Export settings"),
                Filter = Localization.T("Tempo settings (*.json)|*.json|All files (*.*)|*.*"),
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
                Title = Localization.T("Import settings"),
                Filter = Localization.T("Tempo settings (*.json)|*.json|All files (*.*)|*.*")
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
                    Localization.T("Replace your current settings and keybinds with the imported ones?"),
                    "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes)
                {
                    return;
                }

                // This PC's facts stay local — a file from another machine or build carries its
                // integrity baseline, crash watermark, update cache and window position.
                AppSettings before = _settings;
                imported.KeepMachineStateFrom(before);
                _settings = imported;
                ApplyReplacedSettings(before);

                ShowInfo("Settings imported.");
                if (before.Language != _settings.Language)
                {
                    OfferRestartForLanguage();
                }
            }
        }

        /// <summary>Writes the current Settings-tab control values back to _settings.</summary>
        private void CaptureSettingsFromUi()
        {
            // Never write a negative index back. This runs on form close too, where a
            // combo can report SelectedIndex == -1 while its handle is being torn down;
            // casting that to the enum silently reset the user's theme/language on the
            // next launch. (The caption combo below already guarded with Math.Max.)
            if (_themeCombo.SelectedIndex >= 0)
            {
                _settings.Theme = (ThemeKind)_themeCombo.SelectedIndex;
            }
            _settings.AlwaysOnTop = _alwaysOnTopCheck.Checked;
            _settings.CustomAccentEnabled = _customAccentCheck.Checked;
            if (_languageCombo.SelectedIndex >= 0)
            {
                _settings.Language = (Language)_languageCombo.SelectedIndex;
            }
            _settings.MinimizeToTrayOnClose = _minimizeToTrayCheck.Checked;
            _settings.StartMinimizedToTray = _startMinimizedCheck.Checked;
            _settings.TraySleepEnabled = _traySleepCheck.Checked;
            _settings.CaptionOverlayEnabled = _captionOverlayCheck.Checked;
            _settings.CaptionSpeakerTurns = _captionSpeakerCheck.Checked;
            _settings.CaptionFilterOwnVoice = _captionOwnVoiceCheck != null ? _captionOwnVoiceCheck.Checked : _settings.CaptionFilterOwnVoice;
            _settings.CaptionLyrics = _captionLyricsCheck != null ? _captionLyricsCheck.Checked : _settings.CaptionLyrics;
            _settings.CaptionFaceAnalysis = _captionFaceCheck.Checked;
            _settings.CaptionSaveTranscripts = _captionTranscriptCheck.Checked;
            _settings.CaptionShowSourceTag = _captionSourceTagCheck.Checked;
            _settings.CaptionTryGpu = _captionGpuCheck.Checked;
            _settings.CaptionAutoStart = _captionAutoStartCheck.Checked;
            _settings.CaptionSource = (CaptionSource)Math.Max(0, _captionSourceCombo.SelectedIndex);
            _settings.CaptionModelKey = WhisperModelKeyFromIndex(_captionModelCombo.SelectedIndex);
            // Guard the index the way the combos above are guarded: a control that never
            // got built (or a page torn down mid-shutdown) reads -1, and writing that
            // back would silently reset the language to Auto-detect on the way out.
            if (_captionLangCombo != null && _captionLangCombo.SelectedIndex >= 0)
            {
                _settings.CaptionLanguage =
                    CaptionLanguageCodeFromIndex(_captionLangCombo.SelectedIndex);
            }
            _settings.CaptionCaptureMode = Math.Max(0, _captionCaptureCombo.SelectedIndex);
            _settings.CaptionFontSize = (int)_captionFontNum.Value;
            _settings.CaptionOpacity = (int)_captionOpacityNum.Value;
            if (_captionLinesNum != null)
            {
                _settings.CaptionMaxLines = (int)_captionLinesNum.Value;
            }

            ReadMovementControls();

            _settings.CaptionShowBackground = _captionBackgroundCheck.Checked;
            if (_captionFontCombo.SelectedItem != null)
                _settings.CaptionFontFamily = _captionFontCombo.SelectedItem.ToString();
            _settings.ShowTrayNotifications = _trayNotifyCheck.Checked;
            _settings.CustomNotifications = _customNotifyCheck.Checked;
            _settings.MirrorWindowsNotifications = _mirrorNotifyCheck.Checked;
            _settings.MirrorClearFromActionCenter = _mirrorClearCheck.Checked;
            _settings.NotifyOnClipboardImage = _notifyScreenshotCheck.Checked;
            _settings.NotificationShowClose = _notifyCloseCheck.Checked;
            _settings.NotificationCorner = Math.Max(0, _notifyCornerCombo.SelectedIndex);
            _settings.NotificationDurationSeconds = (int)_notifyDurationNum.Value;
            _settings.ConfirmBeforeExitWhileRunning = _confirmExitCheck.Checked;
            _settings.SafetyStopOnEscape = _safetyEscapeCheck.Checked;
            _settings.ClickerStartDelaySeconds = (int)_startDelayNum.Value;
            _settings.StartDelayBeep = _startDelayBeepCheck != null && _startDelayBeepCheck.Checked;
            _settings.LaunchAtStartup = _launchStartupCheck.Checked;
            if (_openAtSignInCheck != null) { _settings.OpenWindowAtSignIn = _openAtSignInCheck.Checked; }
            if (_startupDelayNum != null) { _settings.StartupDelaySeconds = (int)_startupDelayNum.Value; }
            _settings.HideWhenClicking = _hideWhenClickingCheck.Checked;
            _settings.CheckForUpdatesOnLaunch = _checkUpdatesCheck.Checked;
            if (_updateFreqCombo != null) { _settings.UpdateCheckFrequency = Math.Max(0, _updateFreqCombo.SelectedIndex); }
            if (_checkRobloxUpdatesCheck != null) { _settings.CheckRobloxUpdates = _checkRobloxUpdatesCheck.Checked; }
            _settings.WriteLogFile = _writeLogCheck.Checked;
            _settings.RecordSessionHistory = _recordHistoryCheck.Checked;
            _settings.ShowClickingIndicator = _showIndicatorCheck.Checked;
            _settings.MinimizeWhileRecording = _minimizeRecordingCheck.Checked;
            if (_ignoreOwnWindowCheck != null)
            {
                _settings.IgnoreOwnWindowWhileRunning = _ignoreOwnWindowCheck.Checked;
            }
            if (_guardShortcutsCheck != null)
            {
                _settings.GuardWindowsShortcuts = _guardShortcutsCheck.Checked;
            }
            _settings.RememberWindowPosition = _rememberWindowCheck.Checked;
            _settings.RememberLastTab = _rememberTabCheck.Checked;
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
            // Diagnostic breadcrumb for the "Esc doesn't leave full screen" report:
            // log whether the form even SEES the key. Escape presses are rare, so
            // this cannot spam the log. NOTE: this deliberately does NOT filter on
            // _isFullScreen — the old version did, which made "no log" ambiguous
            // between "Escape never arrived" and "it arrived but the flag was off".
            if ((keyData & Keys.KeyCode) == Keys.Escape && _isFullScreen)
            {
                Utils.Logger.Info("[UI] Escape reached the window (fullscreen=" + _isFullScreen +
                                  ", focus=" + (ActiveControl != null ? ActiveControl.GetType().Name : "none") + ").");
            }

            if (keyData == Keys.F11)
            {
                ToggleFullScreen();
                return true;
            }

            // Emergency stop wins whenever the clicker is actually running — safety
            // comes before convenience, even mid-edit.
            if (keyData == Keys.Escape &&
                _settings != null && _settings.SafetyStopOnEscape &&
                _engine != null && _engine.IsRunning)
            {
                EmergencyStop();
                return true;
            }

            // Otherwise, Escape while editing a hotkey field releases it. This is the
            // only way a keyboard-only user can leave a capture field, because the
            // field now captures Tab as a binding (so Tab no longer moves focus out).
            // Handled at the FORM level on purpose: the capture control never reliably
            // receives Escape itself, but the form does — the emergency-stop handler
            // above relies on exactly that.
            if ((keyData & Keys.KeyCode) == Keys.Escape &&
                ActiveControl is HotkeyCaptureControl capture)
            {
                capture.ReleaseFocusToParent();
                return true;
            }

            // Escape leaves full-screen when it isn't being used as a safety stop.
            // Matched on the key code alone: an exact-equality match silently missed
            // any modifier bit riding along with the press.
            if ((keyData & Keys.KeyCode) == Keys.Escape && _isFullScreen)
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
                        // Ctrl+9 on a detached page raises its window — the tab behind it is an
                        // empty placeholder, so selecting it would show nothing.
                        if (AccountsNavHandled(_tabs.TabPages[digit])) { return true; }
                        _tabs.SelectedIndex = digit;
                        return true;
                    }
                }

                if (key == Keys.Tab)
                {
                    int count = _tabs.TabPages.Count;
                    bool back = (keyData & Keys.Shift) == Keys.Shift;
                    int next = ((_tabs.SelectedIndex + (back ? -1 : 1)) % count + count) % count;
                    // Cycling SKIPS a detached page rather than stopping on its empty tab. At
                    // most one step is ever skipped today, but the loop is written to survive a
                    // second detachable page, and to give up rather than spin if every page were.
                    for (int guard = 0; guard < count && IsDetachedPage(_tabs.TabPages[next]); guard++)
                    {
                        next = ((next + (back ? -1 : 1)) % count + count) % count;
                    }
                    _tabs.SelectedIndex = next;
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
