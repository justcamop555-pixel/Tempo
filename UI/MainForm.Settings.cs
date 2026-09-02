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
        private CheckBox _ignoreOwnWindowCheck;
        private CheckBox _recordHistoryCheck;
        private CheckBox _rememberWindowCheck;
        private CheckBox _rememberTabCheck;
        private CheckBox _integrityCheck;
        private Label _integrityStatusLabel;
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
        // Read-only summary of the display Tempo is on, in the Window & Display card.
        private Label _displayInfoLabel;
        private NumericUpDown _captionLinesNum;
        // "(experimental)" normally; "file missing" when the configured background
        // image can no longer be read. Updated by ApplyBackgroundGif.
        private Label _bgGifNote;

        private void BuildSettingsTab()
        {
            var page = new BackdropTabPage(Utils.Localization.T("Settings")) { AutoScroll = true };
            page.Name = "settings";   // stable key for LastTabKey

            // Sixty-odd settings over a 2,100px scroll is too many to hunt through.
            AddSettingsSearchRow(page);

            // NOTE ON THE Y VALUES BELOW. They no longer place the cards — the stack is
            // derived by RestackSettingsCards() at the end of this method, which is why
            // the warnings that used to live down by the buttons ("these y positions must
            // move whenever a group above grows") are gone. What they still do is
            // establish the ORDER the cards appear in, so keep them ascending. Heights
            // are real and do matter: a card is laid out at whatever height it declares.

            // ── Appearance ─────────────────────────────────────────────────────
            var appearance = UiFactory.Group(Localization.T("Appearance"), 12, 12, 696, 168, CardIcon.Star);
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
            _followSystemThemeCheck = UiFactory.Check("Match Windows", 512, 66);
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

            appearance.Controls.Add(UiFactory.Label("Dim", 300, 140));
            _bgDimTrack = new SmoothTrackBar
            {
                Left = 334, Top = 140, Width = 150, Height = 22,
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
                ApplyBackgroundGif();     // live: re-apply the uniform dim everywhere
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

            // ── Startup & Window ───────────────────────────────────────────────
            var startup = UiFactory.Group(Localization.T("Startup & Window"), 12, 192, 696, 114, CardIcon.Play);

            _launchStartupCheck = UiFactory.Check("Launch Tempo when I sign in to Windows (starts in the tray)", 16, 30);
            _hideWhenClickingCheck = UiFactory.Check("Hide window to tray when clicking starts", 16, 58);
            startup.Controls.Add(_launchStartupCheck);
            startup.Controls.Add(_hideWhenClickingCheck);

            // Reopen where you left off. Matters most after an unattended run: Windows
            // reboots overnight, launch-at-startup brings Tempo back, and landing on
            // Clicker instead of the Macros tab you were using reads as a lost page.
            _rememberTabCheck = UiFactory.Check("Reopen on the tab I used last", 16, 86);
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
            var behaviour = UiFactory.Group(Localization.T("Behaviour"), 12, 316, 696, 228, CardIcon.Gear);

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
            // run past the card edge and collide with the row above it).
            _traySleepCheck = UiFactory.Check("Sleep in tray (pause hotkeys && cursor trail)", 360, 174);
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
            // sets how often the launch-time check may actually run.
            _checkUpdatesCheck = UiFactory.Check("Check for updates on start", 16, 114);
            _checkUpdatesCheck.AutoSize = true;
            _checkUpdatesCheck.CheckedChanged += (s, e) =>
            {
                if (_updateFreqCombo != null) { _updateFreqCombo.Enabled = _checkUpdatesCheck.Checked; }
            };
            behaviour.Controls.Add(_checkUpdatesCheck);
            _updateFreqCombo = UiFactory.Combo(210, 111, 128,
                Localization.T("Every launch"), Localization.T("Daily"), Localization.T("Weekly"));
            behaviour.Controls.Add(_updateFreqCombo);

            _writeLogCheck = UiFactory.Check("Write a log file to disk", 360, 118);
            behaviour.Controls.Add(_writeLogCheck);

            _recordHistoryCheck = UiFactory.Check("Record session history and statistics", 16, 142);
            behaviour.Controls.Add(_recordHistoryCheck);

            _showIndicatorCheck = UiFactory.Check("Show on-screen overlay while running", 360, 146);
            _showIndicatorCheck.AutoSize = true;
            behaviour.Controls.Add(_showIndicatorCheck);
            var overlayCustomizeBtn = UiFactory.Button("Customise…", 588, 143, 100, 26);
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

            // (Live Captions controls moved to their own dedicated group below.)

            // ── Notifications ──────────────────────────────────────────────────
            var notify = UiFactory.Group(Localization.T("Notifications"), 12, 556, 696, 184, CardIcon.Gear);

            _customNotifyCheck = UiFactory.Check("Use Tempo's animated pop-up notifications", 16, 30);
            _customNotifyCheck.AutoSize = true;
            _customNotifyCheck.CheckedChanged += (s, e) =>
            {
                if (_suppressSettingsEvents) { return; }
                _settings.CustomNotifications = _customNotifyCheck.Checked;
                UpdateNotifyControlsEnabled();
                ApplyNotificationSettings();
                PersistNotificationSettings();   // remember the on/off immediately
            };
            notify.Controls.Add(_customNotifyCheck);

            notify.Controls.Add(UiFactory.Label("Corner:", 416, 32));
            _notifyCornerCombo = UiFactory.Combo(472, 28, 206,
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

            notify.Controls.Add(UiFactory.Label("Show (s):", 416, 60));
            _notifyDurationNum = UiFactory.Numeric(486, 56, 56, 2, 20, 5);
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

            _notifyStatusLabel = UiFactory.Caption("", 388, 118);
            _notifyStatusLabel.AutoSize = false;
            _notifyStatusLabel.Width = 292;
            _notifyStatusLabel.Height = 20;
            notify.Controls.Add(_notifyStatusLabel);

            // ── Live Captions ─────────────────────────────────────────────────
            // 436, not 400: the language row below needs a line of its own, and every
            // other row in this card is already occupied edge to edge.
            var captions = UiFactory.Group(Localization.T("Live Captions (accessibility)"), 12, 754, 696, 436, CardIcon.Caption);

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
            captions.Controls.Add(_captionSourceCombo);

            // Pause-based speaker-turn labels ("Speaker 1:", "Speaker 2:") on the bar.
            _captionSpeakerCheck = UiFactory.Check("Label speakers (Speaker 1 / 2)", 420, 64);
            _captionSpeakerCheck.AutoSize = true;
            captions.Controls.Add(_captionSpeakerCheck);

            // Auto-start captions when a video site or game is playing with sound.
            _captionAutoStartCheck = UiFactory.Check("Auto-start for videos && games", 420, 30);
            _captionAutoStartCheck.AutoSize = true;
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
            captions.Controls.Add(_captionCaptureCombo);

            _captionModelStatus = UiFactory.Caption("", 16, 124);
            _captionModelStatus.AutoSize = false;
            _captionModelStatus.Width = 664;
            _captionModelStatus.Height = 30;   // two lines — the model notes carry more detail now
            captions.Controls.Add(_captionModelStatus);

            // Experimental: on-device face/mouth analysis to drive speaker labels.
            _captionFaceCheck = UiFactory.Check("AI face && mouth analysis (experimental)", 16, 156);
            _captionFaceCheck.AutoSize = true;
            captions.Controls.Add(_captionFaceCheck);

            // Opt-in transcript files (writes spoken content to disk, hence opt-in).
            _captionTranscriptCheck = UiFactory.Check("Save transcripts to disk", 420, 156);
            _captionTranscriptCheck.AutoSize = true;
            captions.Controls.Add(_captionTranscriptCheck);

            // Appearance of the caption text (applies to both sources).
            captions.Controls.Add(UiFactory.Label("Font:", 16, 188));
            _captionFontCombo = UiFactory.Combo(150, 185, 200,
                CaptionFontChoices());
            captions.Controls.Add(_captionFontCombo);

            captions.Controls.Add(UiFactory.Label("Colour:", 380, 188));
            _captionColorBtn = UiFactory.Button("Choose\u2026", 450, 184, 110, 26);
            _captionColorBtn.Click += OnPickCaptionColor;
            captions.Controls.Add(_captionColorBtn);

            captions.Controls.Add(UiFactory.Label("Text size:", 16, 220));
            _captionFontNum = UiFactory.Numeric(150, 216, 70, 10, 72, 20);
            captions.Controls.Add(_captionFontNum);

            captions.Controls.Add(UiFactory.Label("Opacity (%):", 380, 220));
            _captionOpacityNum = UiFactory.Numeric(470, 216, 70, 10, 100, 50);
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
            // if the GPU can't keep pace, Tempo switches this back off by itself.
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

            // Same menu as the Captions tab's "Models…" button — one place to change, and
            // the two surfaces cannot drift apart on what a model source can be.
            var openModelsBtn = UiFactory.Button("Models folder…", 356, 244, 156, 28);
            openModelsBtn.Click += (s3, e3) => ShowModelSourceMenu(openModelsBtn);
            captions.Controls.Add(openModelsBtn);

            // One-click model install — the "skip the manual folder step" path.
            _captionDownloadModelBtn = UiFactory.PrimaryButton("Download model", 520, 244, 156, 28, _theme);
            _captionDownloadModelBtn.Click += OnDownloadCaptionModel;
            captions.Controls.Add(_captionDownloadModelBtn);

            // Troubleshooting: shows how every window on screen identifies itself, so the
            // (stubborn) Windows 11 Live Captions window can finally be matched. Screenshot
            // or copy the result. Placed between the background-panel check and the model
            // buttons, on the same row.
            var captionDiagBtn = UiFactory.Button("Diagnose Windows bar", 178, 244, 172, 28);
            captionDiagBtn.Click += (sD, eD) =>
            {
                try
                {
                    Utils.LiveCaptionReader reader = _captionReader ?? new Utils.LiveCaptionReader();
                    string dump = reader.DescribeCandidateWindows();
                    if (string.IsNullOrWhiteSpace(dump))
                    {
                        dump = "(no windows reported)";
                    }

                    // Self-test so a single screenshot tells me what actually works on this
                    // PC: did detection find the window, and can the caption text be read?
                    string selfTest;
                    try
                    {
                        bool det = reader.Locate();
                        string readResult;
                        try
                        {
                            string t = reader.ReadText();
                            readResult = string.IsNullOrWhiteSpace(t)
                                ? "(empty - no caption text read)"
                                : ("OK: " + (t.Length > 60 ? t.Substring(0, 60) + "..." : t));
                        }
                        catch (Exception rex) { readResult = "ERROR: " + rex.Message; }
                        selfTest = "DETECTION: " + (det ? "FOUND the window" : "NOT found")
                                 + "\r\nREADING TEST: " + readResult + "\r\n\r\n";
                    }
                    catch (Exception sex) { selfTest = "self-test failed: " + sex.Message + "\r\n\r\n"; }

                    // Lead with a VERDICT and the next step, not a window dump.
                    //
                    // This used to open with "Send me everything below" and then several
                    // screens of raw window information. That is the right thing to
                    // collect, but it answers a question the person in front of it isn't
                    // asking: they want to know whether the Windows bar works here and
                    // what to do about it. The dump is still all there, underneath.
                    string report = BuildWindowsCaptionReport(reader, selfTest, dump);

                    using (var dlg = new Form())
                    {
                        dlg.Text = Localization.T("Windows Live Captions — diagnostic");
                        dlg.StartPosition = FormStartPosition.CenterParent;
                        dlg.Size = new Size(780, 560);
                        dlg.MinimizeBox = false;
                        dlg.MaximizeBox = false;
                        // Themed, like every other Tempo window. A stark white system
                        // dialog in the middle of a dark app looked like another program.
                        dlg.BackColor = _theme.Background;
                        dlg.ForeColor = _theme.Text;
                        try { int dark = _theme.Background.GetBrightness() < 0.5f ? 1 : 0;
                              if (DwmSetWindowAttribute(dlg.Handle, 20, ref dark, sizeof(int)) != 0)
                              { DwmSetWindowAttribute(dlg.Handle, 19, ref dark, sizeof(int)); } } catch { }

                        var box = new TextBox
                        {
                            Multiline = true,
                            ReadOnly = true,
                            ScrollBars = ScrollBars.Both,
                            WordWrap = false,
                            Dock = DockStyle.Fill,
                            Font = new Font("Consolas", 9f),
                            BackColor = _theme.InputBackground,
                            ForeColor = _theme.Text,
                            BorderStyle = BorderStyle.None,
                            Text = report
                        };
                        var bar = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = _theme.Background };
                        var copyBtn = new Button { Text = Localization.T("Copy all"), Width = 110, Height = 30, Top = 8 };
                        copyBtn.Left = dlg.ClientSize.Width - 242;
                        copyBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                        // Copy the WHOLE report. This used to copy only the window dump,
                        // silently dropping the verdict and the self-test — the two most
                        // useful parts — from every diagnostic anyone ever sent in.
                        copyBtn.Click += (sC, eC) =>
                        {
                            try
                            {
                                Clipboard.SetText(report);
                                copyBtn.Text = Localization.T("Copied");
                            }
                            catch { copyBtn.Text = Localization.T("Copy failed"); }
                        };
                        var closeBtn = new Button { Text = Localization.T("Close"), Width = 100, Height = 30, Top = 8, DialogResult = DialogResult.OK };
                        closeBtn.Left = dlg.ClientSize.Width - 122;
                        closeBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                        foreach (var b in new[] { copyBtn, closeBtn })
                        {
                            b.FlatStyle = FlatStyle.Flat;
                            b.BackColor = _theme.Surface2;
                            b.ForeColor = _theme.Text;
                            b.FlatAppearance.BorderColor = _theme.Border;
                        }
                        bar.Controls.Add(copyBtn);
                        bar.Controls.Add(closeBtn);
                        dlg.Controls.Add(box);
                        dlg.Controls.Add(bar);
                        dlg.AcceptButton = closeBtn;
                        dlg.ShowDialog(this);
                    }
                }
                catch (Exception exD) { Utils.Logger.Warn("Caption diagnostic failed: " + exD.Message); }
            };
            captions.Controls.Add(captionDiagBtn);
            // (report builder lives in BuildWindowsCaptionReport below)

            // ── Data ───────────────────────────────────────────────────────────
            // 232, not 164: the tamper check adds a row of actions and a status line.
            // Everything below — Window & Display, the movement card in its own method,
            // the button row and the notes under it — shifts down by the same 68.
            var data = UiFactory.Group(Localization.T("Data & Backup"), 12, 1202, 696, 232, CardIcon.Folder);

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
            var trustBtn = UiFactory.Button("Trust this copy", 546, 156, 140, 28);
            trustBtn.Click += OnTrustThisCopy;
            data.Controls.Add(trustBtn);

            _integrityStatusLabel = UiFactory.Caption("", 16, 190);
            _integrityStatusLabel.AutoSize = false;
            _integrityStatusLabel.Width = 664;
            _integrityStatusLabel.Height = 32;
            _integrityStatusLabel.ForeColor = _theme.TextMuted;
            data.Controls.Add(_integrityStatusLabel);

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

            // ── Camera-relative movement ───────────────────────────────────────
            var movement = BuildMovementGroup();

            // ── Buttons ────────────────────────────────────────────────────────
            // Placed below the last card. This row and everything under it used to be
            // pinned by hand, and was left behind twice while the Live Captions card grew
            // — burying Save/About/Check-for-updates UNDER that card for every user.
            // They are now the "tail": CaptureSettingsLayout records the gap each one
            // keeps from the bottom of the card stack, and they follow it from then on.
            // The absolute Y here only has to preserve the gaps between them.
            _saveSettingsBtn = UiFactory.PrimaryButton("Save Settings", 12, 1866, 150, 36, _theme);
            _saveSettingsBtn.Click += OnSaveSettings;

            var aboutBtn = UiFactory.Button("About…", 172, 1866, 120, 36);
            aboutBtn.Click += (s, e) => OpenAbout();

            var checkUpdatesBtn = UiFactory.Button("Check for updates", 302, 1866, 160, 36);
            checkUpdatesBtn.Click += OnCheckForUpdatesClicked;

            var resetBtn = UiFactory.Button("Reset to defaults", 560, 1866, 148, 36);
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
            _lastCheckedLabel = UiFactory.Caption("", 12, 1910);
            _lastCheckedLabel.AutoSize = false;
            _lastCheckedLabel.Width = 696;
            _lastCheckedLabel.Height = 18;
            page.Controls.Add(_lastCheckedLabel);
            UpdateLastCheckedLabel();

            // When running as a portable copy (not via the installer), explain how a
            // portable copy behaves and where its data lives.
            string portableNote = Utils.DeploymentInfo.PortableNote;
            if (portableNote != null)
            {
                var portable = UiFactory.Caption(portableNote, 12, 1932);
                portable.ForeColor = _theme.Warning;
                portable.AutoSize = false;
                portable.Width = 700;
                portable.Height = 64;
                page.Controls.Add(portable);
            }

            var privacyNote = UiFactory.Caption(
                "Privacy: Tempo runs entirely on your PC. Your clicks, macros, profiles and " +
                "statistics never leave your computer. The only network use is the optional " +
                "update check (GitHub), which you can turn off under Behaviour.",
                12, 2000);
            privacyNote.ForeColor = _theme.TextMuted;
            privacyNote.AutoSize = false;
            privacyNote.Width = 700;
            privacyNote.Height = 48;
            page.Controls.Add(privacyNote);

            var asmVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            string verText = asmVer != null
                ? $"Tempo v{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}"
                : "Tempo";
            // True bottom of the page — the old y=724 had ended up BEHIND the cards
            // as the page grew over the releases.
            var versionLabel = UiFactory.Caption(verText, 12, 2054);
            versionLabel.ForeColor = _theme.TextMuted;
            page.Controls.Add(versionLabel);

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

                int cfi = _captionFontCombo.FindStringExact(_settings.CaptionFontFamily);
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
                _hideWhenClickingCheck.Checked = _settings.HideWhenClicking;
                _checkUpdatesCheck.Checked = _settings.CheckForUpdatesOnLaunch;
                if (_updateFreqCombo != null)
                {
                    _updateFreqCombo.SelectedIndex = Math.Max(0, Math.Min(2, _settings.UpdateCheckFrequency));
                    _updateFreqCombo.Enabled = _settings.CheckForUpdatesOnLaunch;
                }
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
                _rememberWindowCheck.Checked = _settings.RememberWindowPosition;
                _rememberTabCheck.Checked = _settings.RememberLastTab;

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
                    if (unlocked && _settings.MaxClicksPerSecond < UnlockedMaxCps)
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
            if (_recordKeysCheck != null) _recordKeysCheck.Checked = _settings.RecordMacroKeyboard;

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
                _captionModelStatus.ForeColor = cf.Valid ? _theme.Success : _theme.Warning;
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
                _captionModelStatus.ForeColor = _theme.Success;
            }
            else
            {
                _captionModelStatus.Text = "\u2b07 " + Localization.T(model.Label) + " " +
                    Localization.T("isn't downloaded yet \u2014 click \u201cDownload model\u201d.") + " " +
                    Localization.T(model.Note);
                _captionModelStatus.ForeColor = _theme.Warning;
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
                // Live-apply if the overlay is currently showing.
                if (_captionOverlay != null && !_captionOverlay.IsDisposed)
                {
                    _captionOverlay.SetTextColor(dlg.Color, true);
                }
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
        /// Shows the live mirror state under the notification controls — whether it's
        /// off, running, or couldn't get Windows' permission (and why).
        /// </summary>
        private void RefreshNotifyStatus()
        {
            if (_notifyStatusLabel == null) { return; }
            try
            {
                if (_settings == null || !_settings.MirrorWindowsNotifications || !_settings.CustomNotifications)
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
                _notifyStatusLabel.ForeColor = ok ? _theme.Success : _theme.Warning;
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
            try
            {
                Screen s = Screen.PrimaryScreen;
                if (s == null) { return "Display information isn't available."; }

                var sb = new System.Text.StringBuilder();
                sb.Append(s.Bounds.Width).Append('×').Append(s.Bounds.Height);

                int hz = Utils.EnvironmentInfo.GetPrimaryRefreshHz();
                if (hz > 0) { sb.Append("  ·  ").Append(hz).Append(" Hz"); }

                string scale = Utils.EnvironmentInfo.GetDisplayScaleText();
                if (!string.IsNullOrEmpty(scale)) { sb.Append("  ·  ").Append(scale).Append(" scaling"); }

                int count = Screen.AllScreens != null ? Screen.AllScreens.Length : 1;
                if (count > 1)
                {
                    sb.Append("  ·  ").Append(count).Append(" monitors");
                }
                // Work area, not just bounds: it is what actually constrains the window,
                // and the difference is the taskbar — the usual reason a "screen-sized"
                // window comes back slightly short.
                var wa = s.WorkingArea;
                if (wa.Height != s.Bounds.Height || wa.Width != s.Bounds.Width)
                {
                    sb.Append("   (usable ").Append(wa.Width).Append('×').Append(wa.Height)
                      .Append(" after the taskbar)");
                }
                return sb.ToString();
            }
            catch { return "Display information isn't available."; }
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
            warn.ForeColor = _theme.Warning;
            warn.AutoSize = false;
            warn.Width = 664;
            warn.Height = 32;
            g.Controls.Add(warn);

            g.Controls.Add(UiFactory.Label("Mode:", 16, 96));
            // Combo items don't auto-translate (UiFactory.Combo takes them verbatim), so
            // pass them through T() here. Selection stays index-based, so translating the
            // display text is safe.
            _movementFrameCombo = UiFactory.Combo(150, 93, 330,
                Localization.T("World-locked – keep heading as the camera turns"),
                Localization.T("Camera-relative pass-through (no-op in most games)"));
            g.Controls.Add(_movementFrameCombo);

            // Sensitivity + the calibrate button that fills it in.
            g.Controls.Add(UiFactory.Label("Camera sensitivity:", 16, 130));
            _movementDegPerCountNum = UiFactory.Numeric(150, 126, 90, 0.0005m, 2m, 0.06m);
            _movementDegPerCountNum.DecimalPlaces = 4;
            _movementDegPerCountNum.Increment = 0.005m;
            g.Controls.Add(_movementDegPerCountNum);
            g.Controls.Add(UiFactory.Caption("°/count", 246, 130));

            var calBtn = UiFactory.Button("Calibrate…", 310, 124, 120, 28);
            calBtn.Click += OnCalibrateCameraSensitivity;
            g.Controls.Add(calBtn);

            var calHint = UiFactory.Caption("Measures it from one 360° turn in your game — far better than guessing.",
                440, 130);
            calHint.ForeColor = _theme.TextMuted;
            calHint.AutoSize = false;
            calHint.Width = 244;
            calHint.Height = 28;
            g.Controls.Add(calHint);

            // Feel / jitter.
            g.Controls.Add(UiFactory.Label("Smoothing (s):", 16, 166));
            _movementSmoothingNum = UiFactory.Numeric(150, 162, 70, 0m, 1m, 0m);
            _movementSmoothingNum.DecimalPlaces = 2;
            _movementSmoothingNum.Increment = 0.02m;
            g.Controls.Add(_movementSmoothingNum);
            var smoothHint = UiFactory.Caption("0 = instant", 226, 166);
            smoothHint.ForeColor = _theme.TextMuted;
            g.Controls.Add(smoothHint);

            g.Controls.Add(UiFactory.Label("Anti-jitter (°):", 310, 166));
            _movementHysteresisNum = UiFactory.Numeric(410, 162, 70, 0m, 22m, 8m);
            _movementHysteresisNum.DecimalPlaces = 1;
            g.Controls.Add(_movementHysteresisNum);

            g.Controls.Add(UiFactory.Label("Rate (Hz):", 500, 166));
            _movementHzNum = UiFactory.Numeric(580, 162, 70, 30m, 500m, 120m);
            g.Controls.Add(_movementHzNum);

            g.Controls.Add(UiFactory.Label("Stick deadzone:", 16, 202));
            _movementDeadzoneNum = UiFactory.Numeric(150, 198, 70, 0m, 0.9m, 0.20m);
            _movementDeadzoneNum.DecimalPlaces = 2;
            _movementDeadzoneNum.Increment = 0.05m;
            g.Controls.Add(_movementDeadzoneNum);

            g.Controls.Add(UiFactory.Label("Pad look (°/s):", 310, 202));
            _movementPadYawNum = UiFactory.Numeric(410, 198, 70, 20m, 1000m, 220m);
            g.Controls.Add(_movementPadYawNum);

            _movementStatus = UiFactory.Caption("Bind a key in Keybinds → “Camera-relative movement” to arm it in-game.",
                500, 202);
            _movementStatus.ForeColor = _theme.TextMuted;
            _movementStatus.AutoSize = false;
            _movementStatus.Width = 184;
            _movementStatus.Height = 30;
            g.Controls.Add(_movementStatus);

            return g;
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
                    _movementDegPerCountNum.Value = v;
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
                    StartMovement();
                    Utils.Logger.Info("[Movement] re-armed after calibration.");
                }
            }
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
                    _settings.OverlayCorner = dlg.Corner;
                    _settings.OverlayOpacity = dlg.Opacity;
                    _settings.OverlayShowClicks = dlg.ShowClicks;
                    _settings.OverlayShowCps = dlg.ShowCps;
                    _settings.OverlayShowElapsed = dlg.ShowElapsed;
                    try { Persistence.SettingsManager.Save(_settings); } catch { }

                    // Reflect it live if the badge is currently on screen.
                    if (_clickingIndicator != null && !_clickingIndicator.IsDisposed)
                    {
                        ApplyOverlayConfig(_clickingIndicator);
                    }
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

            _settings.MovementEnabled = _movementEnableCheck.Checked;
            _settings.MovementFrame = Math.Max(0, _movementFrameCombo.SelectedIndex);
            _settings.MovementDegreesPerCount = (double)_movementDegPerCountNum.Value;
            _settings.MovementTurnSmoothing = (double)_movementSmoothingNum.Value;
            _settings.MovementHysteresisDegrees = (double)_movementHysteresisNum.Value;
            _settings.MovementUpdateHz = (int)_movementHzNum.Value;
            _settings.MovementStickDeadzone = (double)_movementDeadzoneNum.Value;
            _settings.MovementGamepadYawDps = (double)_movementPadYawNum.Value;

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
            if (_startupDelayNum != null) { _settings.StartupDelaySeconds = (int)_startupDelayNum.Value; }
            _settings.HideWhenClicking = _hideWhenClickingCheck.Checked;
            _settings.CheckForUpdatesOnLaunch = _checkUpdatesCheck.Checked;
            if (_updateFreqCombo != null) { _settings.UpdateCheckFrequency = Math.Max(0, _updateFreqCombo.SelectedIndex); }
            _settings.WriteLogFile = _writeLogCheck.Checked;
            _settings.RecordSessionHistory = _recordHistoryCheck.Checked;
            _settings.ShowClickingIndicator = _showIndicatorCheck.Checked;
            _settings.MinimizeWhileRecording = _minimizeRecordingCheck.Checked;
            if (_ignoreOwnWindowCheck != null)
            {
                _settings.IgnoreOwnWindowWhileRunning = _ignoreOwnWindowCheck.Checked;
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
            // capture-open), so cycle the session off→on. The off pass saves the
            // in-progress transcript rather than silently dropping it.
            bool captionEngineChanged =
                prevCaptionSource != _settings.CaptionSource ||
                !string.Equals(prevCaptionModel, _settings.CaptionModelKey, StringComparison.OrdinalIgnoreCase) ||
                prevCaptionCapture != _settings.CaptionCaptureMode;
            if (captionEngineChanged && _captionsActive)
            {
                SetCaptionsActive(false);
                SetCaptionsActive(true);
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
        private void ConfirmOnButton(Control button)
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
                button.Text = Localization.T("Saved  ✓");

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

        private void OnChooseBackgroundGif(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = Localization.T("Choose a background image (GIF animates)"),
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
                Title = Localization.T("Choose a second background image (GIF animates)"),
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

            // Inline result of that last check, so the state is visible without
            // opening the update dialog.
            if (_settings != null && _settings.LastUpdateCheckUtc != null)
            {
                if (_settings.LastCheckFoundUpdate && !string.IsNullOrWhiteSpace(_settings.LastKnownLatestVersion))
                {
                    text += "   ·   Update available: v" + _settings.LastKnownLatestVersion;
                }
                else if (!_settings.LastCheckFoundUpdate)
                {
                    text += "   ·   You're up to date.";
                }
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
                        _settings.LastKnownLatestVersion = result.LatestVersion?.ToString();
                        _settings.LastCheckFoundUpdate = result.UpdateAvailable;
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
            // Sized to fit two 9-second attempts plus the short backoff between them.
            System.Threading.Tasks.Task.Delay(22000)
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
                    ShowWarning(result.Error ?? Localization.T("The update check failed."));
                }
                return;
            }

            if (!result.UpdateAvailable)
            {
                if (announceUpToDate)
                {
                    ShowInfo(Localization.F("You're up to date.\n\nTempo {0} is the latest version.", UpdateChecker.CurrentVersion));
                }
                return;
            }

            string notes = string.IsNullOrWhiteSpace(result.Notes) ? "" : result.Notes;

            bool canAutoInstall =
                !string.IsNullOrWhiteSpace(result.DownloadUrl) &&
                (result.DownloadUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                 result.DownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) &&
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
            // The release may ship either a bare Tempo.exe or a setup .zip; support both.
            bool isZip = !string.IsNullOrWhiteSpace(result.DownloadUrl) &&
                         result.DownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            string dest = UpdateInstaller.GetDownloadTargetPath(result.LatestVersion, isZip);

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
                    (downloadError ?? Localization.T("The download failed.")) + "\n\n" + Localization.T("Open the download page instead?"),
                    "Update", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (fallback == DialogResult.Yes)
                {
                    OpenDownloadPage(result.DownloadUrl);
                }
                return;
            }

            // Decide which exe to swap in. A bare .exe is used directly; a setup zip is
            // unpacked first and its Tempo.exe is header-checked and (if a checksum is
            // published) verified, so a bad archive can never overwrite the running exe.
            string swapExe = downloadedPath;
            if (isZip)
            {
                if (!UpdateInstaller.ExtractTempoExe(downloadedPath, out swapExe, out string exErr))
                {
                    DialogResult fb = MessageBox.Show(this,
                        (exErr ?? Localization.T("Couldn't unpack the update.")) + "\n\n" + Localization.T("Open the download page instead?"),
                        "Update", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (fb == DialogResult.Yes)
                    {
                        OpenDownloadPage(result.DownloadUrl);
                    }
                    return;
                }
                if (!UpdateInstaller.VerifyExeAgainstSha(swapExe, result.Sha256Url))
                {
                    ShowWarning("The update failed its integrity check (checksum mismatch). "
                        + "Please try again, or use the download page.");
                    return;
                }
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

            // A macro being RECORDED only exists in memory until it is stopped and
            // saved. The swap force-exits the process, so an update accepted mid-record
            // used to throw the take away with no warning — the engine was stopped here,
            // the recorder never was. Give the user the choice instead of deciding for
            // them, and abort the update if they want to keep it.
            try
            {
                if (_recorder != null && _recorder.IsRecording)
                {
                    DialogResult keep = MessageBox.Show(this,
                        "A macro is still being recorded.\n\n" +
                        "Updating restarts Tempo, which discards the recording in progress.\n\n" +
                        "Yes — discard it and update now\n" +
                        "No — cancel the update so you can finish and save it",
                        "Update Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2);
                    if (keep != DialogResult.Yes)
                    {
                        Logger.Info("[Update] cancelled — a macro recording was in progress.");
                        return;
                    }
                    try { _recorder.Stop(); } catch { }
                }
            }
            catch { /* best effort */ }

            if (UpdateInstaller.LaunchSwapAndExitHelper(swapExe, out string err))
            {
                // Remove the tray icon so it doesn't linger after we force-exit.
                try { _trayIcon?.Dispose(); } catch { }
                Logger.Info("[shutdown] exiting to let the updater replace Tempo.exe.");
                Environment.Exit(0);
            }
            else
            {
                ShowWarning(err ?? Localization.F("Couldn't start the updater. The new version was downloaded to:\n{0}", swapExe));
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
                ShowWarning(Localization.F("Couldn't open the download page: {0}", ex.Message));
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
        /// Turns the raw window dump into something a person can act on: a one-line
        /// verdict, what this PC actually supports, then the technical detail.
        /// </summary>
        private string BuildWindowsCaptionReport(Utils.LiveCaptionReader reader,
                                                 string selfTest, string dump)
        {
            var sb = new System.Text.StringBuilder();
            bool found = false;
            bool uiaBroken = false;
            try { found = reader.Found || reader.IsWindowPresent(); } catch { }
            try { uiaBroken = reader.UiaBroken; } catch { }

            // Windows 11 is where Live Captions exists at all. Saying so up front saves
            // a Windows 10 user from chasing a feature their build does not have.
            Version os = Environment.OSVersion.Version;
            bool win11 = os.Major > 10 || (os.Major == 10 && os.Build >= 22000);

            sb.AppendLine("=== VERDICT ===");
            if (!win11)
            {
                sb.AppendLine("Windows Live Captions is a Windows 11 feature and this PC reports Windows "
                              + os.Major + " (build " + os.Build + ").");
                sb.AppendLine("→ Use Tempo's OWN caption engine instead: set 'Caption source' to");
                sb.AppendLine("  \"Tempo's Live Captions (offline)\". It does not need the Windows bar.");
            }
            else if (found && !uiaBroken)
            {
                sb.AppendLine("The Windows Live Captions bar was FOUND and Tempo can read it.");
                sb.AppendLine("→ Nothing to fix. If captions still look wrong, the problem is what");
                sb.AppendLine("  Windows itself is transcribing, not Tempo's ability to read it.");
            }
            else if (found && uiaBroken)
            {
                sb.AppendLine("The bar was FOUND, but Windows refused to hand its text to Tempo (UI Automation failed).");
                sb.AppendLine("→ This is usually a stuck accessibility service. Sign out and back in, or");
                sb.AppendLine("  switch 'Caption source' to Tempo's own offline engine, which does not use it.");
            }
            else
            {
                sb.AppendLine("The Windows Live Captions bar was NOT found.");
                sb.AppendLine("→ Turn it on first: press Win + Ctrl + L, or Settings → Accessibility →");
                sb.AppendLine("  Captions → Live captions. The first run downloads a speech pack.");
                sb.AppendLine("  Then click 'Diagnose Windows bar' again.");
                sb.AppendLine("→ Or avoid it entirely: set 'Caption source' to Tempo's own offline engine.");
            }
            sb.AppendLine();

            sb.AppendLine("=== THIS PC ===");
            sb.AppendLine("Windows          : " + os + (win11 ? "  (Windows 11 — Live Captions supported)"
                                                              : "  (pre-Windows 11 — no Live Captions)"));
            sb.AppendLine("Tempo            : " + VersionStamp());
            sb.AppendLine("Caption source   : " + (_settings != null ? _settings.CaptionSource.ToString() : "?"));
            sb.AppendLine("Tempo engine     : " + (_captionTranscriber != null && _captionTranscriber.IsRunning
                                                    ? "running" : "not running"));
            sb.AppendLine("Bar detected     : " + (found ? "yes" : "no"));
            sb.AppendLine("UI Automation    : " + (uiaBroken ? "FAILED (cannot read text)" : "ok"));
            sb.AppendLine();

            sb.AppendLine("=== SELF-TEST ===");
            sb.AppendLine(string.IsNullOrWhiteSpace(selfTest) ? "(none)" : selfTest.TrimEnd());
            sb.AppendLine();

            sb.AppendLine("=== CANDIDATE WINDOWS (technical detail) ===");
            sb.AppendLine(string.IsNullOrWhiteSpace(dump) ? "(no windows reported)" : dump);
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
        private void OnRecheckIntegrity(object sender, EventArgs e)
        {
            try
            {
                if (_integrityStatusLabel != null)
                {
                    _integrityStatusLabel.Text = Localization.T("Checking…");
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

                // A whole translated sentence per verdict rather than a translated
                // wrapper around IntegrityCheck.Summary. Summary is diagnostic English
                // written for the log and Live Debug — formatting it into "✗ {0}" would
                // have produced a label whose frame was Spanish and whose content was
                // English, which reads worse than either.
                string text;
                Color colour = _theme.TextMuted;
                switch (Utils.IntegrityCheck.Verdict)
                {
                    case Utils.IntegrityVerdict.Genuine:
                        // The strongest thing Tempo can say, and worth saying distinctly:
                        // this was checked against GitHub, not against a note this PC
                        // wrote to itself.
                        text = Localization.T("✓ Verified against the release published on GitHub.");
                        colour = _theme.Success;
                        break;
                    case Utils.IntegrityVerdict.UnknownRelease:
                        text = Localization.T("⚠ No release with this version number exists on GitHub, "
                            + "so nothing outside this PC can confirm the file. Normal for a build you "
                            + "made yourself.");
                        colour = _theme.Danger;
                        break;
                    case Utils.IntegrityVerdict.Ok:
                    case Utils.IntegrityVerdict.Baselined:
                        text = Localization.T("✓ Tempo.exe is the file that was installed.");
                        colour = _theme.Success;
                        break;
                    case Utils.IntegrityVerdict.Modified:
                        text = Localization.T("✗ This copy does not match the release published for its "
                            + "version number. If you did not replace it yourself, reinstall from the "
                            + "official download.");
                        colour = _theme.Danger;
                        break;
                    case Utils.IntegrityVerdict.Damaged:
                        text = Localization.T("✗ Tempo.exe is damaged — part of it is unreadable. "
                            + "Reinstalling fixes it.");
                        colour = _theme.Danger;
                        break;
                    case Utils.IntegrityVerdict.Repackaged:
                        text = Localization.T("✗ This copy was packaged by someone else, "
                            + "not the official Tempo build.");
                        colour = _theme.Danger;
                        break;
                    case Utils.IntegrityVerdict.Unknown:
                        text = Localization.T("Not checked yet.");
                        break;
                    default:
                        text = Localization.T("Could not be checked right now.");
                        break;
                }
                _integrityStatusLabel.Text = text;
                _integrityStatusLabel.ForeColor = colour;
            }
            catch (Exception ex) { Logger.Swallow("RefreshIntegrityStatus", ex); }
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

        private void OnUninstallClicked(object sender, EventArgs e)
        {
            string dataFolder;
            try { dataFolder = SettingsManager.GetSettingsDirectory(); }
            catch { dataFolder = "%LocalAppData%\\AutoClicker"; }

            DialogResult confirm = MessageBox.Show(this,
                // One key for the whole prompt. Split across a raw head and a translated
                // tail, the head could never match a dictionary entry, so half the warning
                // stayed English — on the one dialog where being understood matters most.
                Localization.F("Uninstall Tempo?\n\n"
                    + "This will permanently remove:\n"
                    + "   •  All profiles and saved macros\n"
                    + "   •  Your settings and session history\n"
                    + "   •  Downloaded speech models\n"
                    + "   •  The log file\n"
                    + "   •  The Windows start-up entry (if set)\n"
                    + "   •  Start Menu and Desktop shortcuts\n"
                    + "   •  Tempo's entry in Settings > Apps\n\n"
                    + "All of that lives in:\n{0}\n\n"
                    + "This cannot be undone. Continue?", dataFolder),
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
            // Also undo what INSTALLING registered: the Start Menu and Desktop
            // shortcuts and the Settings > Apps entry. Without this the in-app uninstall
            // left Windows still advertising Tempo as installed, pointing at files it
            // had just deleted — see Uninstaller.RemoveShellIntegration.
            Uninstaller.RemoveShellIntegration();

            if (Uninstaller.LaunchCleanupAndExitHelper(deleteExe, out string err))
            {
                try { _trayIcon?.Dispose(); } catch { }
                Logger.Info("[shutdown] exiting for uninstall cleanup.");
                Environment.Exit(0);
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

            _settings.MovementEnabled = _movementEnableCheck.Checked;
            _settings.MovementFrame = Math.Max(0, _movementFrameCombo.SelectedIndex);
            _settings.MovementDegreesPerCount = (double)_movementDegPerCountNum.Value;
            _settings.MovementTurnSmoothing = (double)_movementSmoothingNum.Value;
            _settings.MovementHysteresisDegrees = (double)_movementHysteresisNum.Value;
            _settings.MovementUpdateHz = (int)_movementHzNum.Value;
            _settings.MovementStickDeadzone = (double)_movementDeadzoneNum.Value;
            _settings.MovementGamepadYawDps = (double)_movementPadYawNum.Value;

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
            if (_startupDelayNum != null) { _settings.StartupDelaySeconds = (int)_startupDelayNum.Value; }
            _settings.HideWhenClicking = _hideWhenClickingCheck.Checked;
            _settings.CheckForUpdatesOnLaunch = _checkUpdatesCheck.Checked;
            if (_updateFreqCombo != null) { _settings.UpdateCheckFrequency = Math.Max(0, _updateFreqCombo.SelectedIndex); }
            _settings.WriteLogFile = _writeLogCheck.Checked;
            _settings.RecordSessionHistory = _recordHistoryCheck.Checked;
            _settings.ShowClickingIndicator = _showIndicatorCheck.Checked;
            _settings.MinimizeWhileRecording = _minimizeRecordingCheck.Checked;
            if (_ignoreOwnWindowCheck != null)
            {
                _settings.IgnoreOwnWindowWhileRunning = _ignoreOwnWindowCheck.Checked;
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
