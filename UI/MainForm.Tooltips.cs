using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    public partial class MainForm
    {
        private ToolTip _tips;

        /// <summary>
        /// Owner-draws a ToolTip in the current theme's colours so it isn't the bright
        /// system popup over the dark UI. Safe to call before the theme exists (falls
        /// back to sensible dark defaults).
        /// </summary>
        private void ThemeTooltip(ToolTip tip)
        {
            if (tip == null)
            {
                return;
            }
            Color back = _theme != null ? _theme.Surface2 : Color.FromArgb(45, 45, 48);
            Color fore = _theme != null ? _theme.Text : Color.WhiteSmoke;
            Color line = _theme != null ? _theme.Border : Color.FromArgb(70, 70, 74);

            tip.OwnerDraw = true;
            tip.BackColor = back;
            tip.ForeColor = fore;
            tip.Draw -= OnToolTipDraw;
            tip.Draw += OnToolTipDraw;
            _tipBack = back;
            _tipFore = fore;
            _tipBorder = line;
        }

        private Color _tipBack = Color.FromArgb(45, 45, 48);
        private Color _tipFore = Color.WhiteSmoke;
        private Color _tipBorder = Color.FromArgb(70, 70, 74);

        private void OnToolTipDraw(object sender, DrawToolTipEventArgs e)
        {
            using (var b = new SolidBrush(_tipBack))
            {
                e.Graphics.FillRectangle(b, e.Bounds);
            }
            using (var p = new Pen(_tipBorder))
            {
                e.Graphics.DrawRectangle(p, new Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1));
            }
            TextRenderer.DrawText(
                e.Graphics, e.ToolTipText, e.Font, e.Bounds, _tipFore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        /// <summary>
        /// Attaches concise help text to the main controls on every tab. This is
        /// purely informational — it changes no behaviour, it just makes the existing
        /// features easier to understand on hover.
        /// </summary>
        private void SetupTooltips()
        {
            _tips = new ToolTip
            {
                AutoPopDelay = 15000,
                InitialDelay = 450,
                ReshowDelay = 150,
                ShowAlways = true
            };

            // Theme the tooltip dark. A default ToolTip is the system light/yellow popup,
            // which flashed bright over the dark UI on every hover — users lumped that in
            // with the "light flash" report. Owner-draw it in the theme surface colour.
            ThemeTooltip(_tips);

            void T(Control c, string text)
            {
                if (c != null)
                {
                    _tips.SetToolTip(c, text);
                    // Screen readers read AccessibleDescription, so the same help is
                    // available without a mouse.
                    c.AccessibleDescription = text;
                }
            }

            // ── Sidebar navigation ──────────────────────────────────────────────
            // Name the keyboard shortcut for each tab. Ctrl+1…9 and Ctrl+Tab have
            // always worked; until now nothing in the app mentioned them.
            for (int i = 0; i < _navButtons.Count; i++)
            {
                string tab = i < _tabs.TabPages.Count ? _tabs.TabPages[i].Text : "";
                string tip = "Open the " + tab + " tab";
                if (i < 9)
                {
                    tip += "  ·  Ctrl+" + (i + 1);
                }
                tip += ". Ctrl+Tab cycles through the tabs.";
                T(_navButtons[i], tip);
            }

            // ── Clicker: profile ────────────────────────────────────────────────
            T(_notifyFinishCheck, "Play a chime (and show a tray notice) when a fixed-count or fixed-duration run \u2014 or a finite macro playback \u2014 finishes by itself. Manual stops stay silent.");
            T(_sessionHistoryList, "Double-click a row for details \u00b7 right-click for more options \u00b7 click a column header to sort.");
            T(_traySleepCheck, "While Tempo is hidden in the tray and nothing is running, global hotkeys and the cursor trail are paused so a forgotten Tempo can't start clicking invisibly. Everything wakes when you open the window or start something from the tray menu.");
            T(_captionOverlayCheck, "Show Tempo's own caption bar (a transparent strip across the bottom of the screen) when you toggle Live Captions with your hotkey. Only the text is visible - it floats over any game and never blocks clicks.");
            T(_captionSourceCombo, "Choose which engine makes the caption text: Windows 11 Live Captions (uses the built-in Windows engine), or Tempo's own offline captions (Tempo listens to your PC audio and transcribes it itself, no internet). Both show in Tempo's caption bar.");
            T(_captionSpeakerCheck, "Start each spoken turn with \"Speaker 1:\", \"Speaker 2:\" ... Tempo listens to the voice itself (pitch and tone, on your PC - nothing uploaded) so a returning speaker gets their old number back; where the voice can't be told apart it falls back to the rhythm of the conversation, like the >> marks in TV captions. After 30 seconds of silence the numbering starts over at Speaker 1.");
            T(_captionAutoStartCheck, "Turn captions on by themselves when you're watching or playing something with sound: video sites in any browser (YouTube, TikTok, Twitch, Netflix...) and games (Roblox, Call of Duty, Rainbow Six, Fortnite, Valorant...). If you turn captions off during a video, Tempo won't fight you - auto-start re-arms after that video or game stops.");
            T(_captionFaceCheck, "Adds SIGHT to the speaker labels: Windows' built-in on-device face detector watches the video in the foreground window a few times a second, tracks each face, and measures mouth movement - the face whose mouth is moving while speech is heard becomes the active speaker. Nothing is uploaded and nothing is recorded. Costs some CPU; needs the speaker's face visible on screen; it's a hint, not identification.");
            T(_captionTranscriptCheck, "When captions turn off, save that session's full transcript (with per-line timestamps and speaker labels) as a plain-text file in Tempo's data folder under transcripts\\. Everything stays on your PC - but do remember the file contains whatever was said.");
            T(_captionModelCombo, "Speech model for Tempo's own captions. Bigger models understand speech better but need more CPU/GPU: Tiny and Base are quick on any PC, Small and Medium hear more accurately, and Large Turbo is the most accurate AND understands 90+ languages automatically. Each model downloads once and then works fully offline.");
            T(_captionCaptureCombo, "What Tempo listens to for its own captions. System audio captions whatever your PC plays (needs a speaker). If this PC has no speaker, choose Microphone (or Auto, which switches to the mic automatically).");
            T(_captionFontNum, "Caption text size, in points.");
            T(_captionOpacityNum, "How opaque the caption text is. The bar's background always stays fully transparent.");
            T(_captionFontCombo, "Font for the caption overlay text.");
            T(_captionColorBtn, "Pick the caption text colour. Bright yellow reads well over most games. (The bar background always stays transparent.)");
            T(_captionBackgroundCheck, "Show the rounded panel behind captions. Turn this off for a clean text-only look with no background \u2014 the text keeps a soft glow so it stays readable.");
            T(_profileCombo, "Switch between your saved click profiles.");
            T(_profileNameText, "The name used when you save this profile.");
            T(_newProfileBtn, "Create a new profile from the current settings.");
            T(_saveProfileBtn, "Save the current settings (including multi-point list) to this profile.");
            T(_duplicateProfileBtn, "Make a copy of the selected profile.");
            T(_deleteProfileBtn, "Delete the selected profile.");

            // ── Clicker: interval ───────────────────────────────────────────────
            T(_hoursNum, "Hours between clicks.");
            T(_minutesNum, "Minutes between clicks.");
            T(_secondsNum, "Seconds between clicks.");
            T(_millisNum, "Milliseconds between clicks. The four fields add up to the total delay.");

            // ── Clicker: options ────────────────────────────────────────────────
            T(_buttonCombo, "What to auto-repeat: a mouse button (Left/Right/Middle) or a keyboard key. Pick \"Keyboard key\" to press a key like Space or W at the interval.");
            T(_setKeyBtn, "Choose which keyboard key to auto-press (only used when the button is set to \"Keyboard key\"). Works with the interval, repeat, humanize and hold-time settings.");
            T(_styleCombo, "Single, double, triple or quadruple click each time.");
            T(_modeCombo, "Interval = steady rate. Hold = click only while the button/key is held. Burst = bursts of clicks with a pause between.");
            T(_holdMsNum, "Hold each click's button down for this many milliseconds before releasing. 0 = a normal instant click. Useful for games that need a press-and-hold.");

            // ── Clicker: position ───────────────────────────────────────────────
            T(_posCurrentRadio, "Click wherever the cursor currently is.");
            T(_posFixedRadio, "Always click at one fixed screen position.");
            T(_posMultiRadio, "Cycle through the points on the Multi-Point tab.");
            T(_fixedXNum, "X coordinate (pixels) for fixed-position clicking.");
            T(_fixedYNum, "Y coordinate (pixels) for fixed-position clicking.");
            T(_pickFixedBtn, "Pick the fixed position by clicking on screen.");
            T(_restoreCursorCheck, "Move the cursor back to where it started when clicking stops.");

            // ── Clicker: repeat ─────────────────────────────────────────────────
            T(_repeatUntilRadio, "Keep clicking until you stop it.");
            T(_repeatCountRadio, "Stop automatically after a set number of clicks.");
            T(_repeatCountNum, "How many clicks before stopping.");
            T(_repeatDurationRadio, "Stop automatically after a set time.");
            T(_repeatDurationNum, "How many seconds to keep clicking.");

            // ── Clicker: burst ──────────────────────────────────────────────────
            T(_burstSizeNum, "How many clicks in each burst (Burst mode).");
            T(_burstPauseNum, "Pause in milliseconds between bursts (Burst mode).");

            // ── Clicker: randomization ──────────────────────────────────────────
            T(_randIntervalCheck, "Vary the delay between clicks by a random amount.");
            T(_intervalJitterNum, "Maximum random variation added to / removed from the delay (ms).");
            T(_randPosCheck, "Nudge each click by a small random offset.");
            T(_posJitterNum, "Maximum random position offset in pixels.");
            T(_humanizeBtn, "Turn on both randomizers with sensible values for less robotic clicking.");

            // ── Clicker: action + manual speed ──────────────────────────────────
            T(_startBtn, "Start clicking (or use your Start/Stop hotkey).");
            T(_stopBtn, "Stop clicking.");
            T(_cpsTestBtn, "Open the clicks-per-second test.");
            T(_speedTrack, "Quickly set the click rate in clicks per second.");
            T(_unlockSpeedCheck, "Advanced: raise the speed slider far above the normal limit (up to 2000 CPS). Very fast and CPU-heavy — shows a warning before enabling.");
            T(_speedMinusBtn, "Slightly slower.");
            T(_speedPlusBtn, "Slightly faster.");

            // Newer controls: exact-CPS box/button, the CPS presets, and the Anti-Freeze card.
            T(_exactCpsNum, "Type an exact clicks-per-second target, then press Set to apply it precisely.");
            T(_exactCpsSetBtn, "Apply the exact CPS you typed to the speed slider.");
            if (_cpsPresetBtns != null)
            {
                foreach (var pb in _cpsPresetBtns)
                {
                    T(pb, "Quickly set the click rate to this preset. The active preset is highlighted.");
                }
            }
            T(_antiFreezeCheck, "Safety limiter that stops Tempo clicking so fast it freezes your PC. Recommended on.");
            T(_maxCpsNum, "Hard ceiling on clicks per second — the engine never clicks faster than this even if the slider asks for more.");
            T(_cpuThresholdNum, "When Tempo's own CPU use passes this %, it automatically slows down to keep the system responsive.");
            T(_antiFreezeStatusLabel, "Live status of the anti-freeze limiter: off, watching, or actively throttling.");

            // ── Multi-Point ─────────────────────────────────────────────────────
            T(_pointOrderCombo, "The order the engine visits enabled points each cycle:\n" +
                "  Sequential - top to bottom, then repeat\n" +
                "  Reverse - bottom to top, then repeat\n" +
                "  Random - a different random point each time\n" +
                "  Ping-Pong - down the list then back up, repeating");
            T(_addPointBtn, "Add a point by typing its coordinates.");
            T(_capturePointBtn, "Capture a point by clicking somewhere on screen.");
            T(_editPointBtn, "Edit the selected point.");
            T(_duplicatePointBtn, "Duplicate the selected point (Ctrl+D).");
            T(_togglePointBtn, "Enable or disable the selected point.");
            T(_removePointBtn, "Remove the selected point (Delete).");
            T(_movePointUpBtn, "Move the selected point earlier in the order.");
            T(_movePointDownBtn, "Move the selected point later in the order.");
            T(_showPointsBtn, "Flash numbered markers on screen at each point.");
            T(_clearPointsBtn, "Remove all points.");
            T(_toggleAllPointsBtn, "Enable every point, or disable them all if they're all on.");

            // ── Macros ──────────────────────────────────────────────────────────
            T(_recordBtn, "Start recording mouse and keyboard input.");
            T(_stopRecordBtn, "Stop recording.");
            T(_recordCountdownNum, "Seconds to wait (3, 2, 1…) before recording starts, so you can switch to the app you want to record. 0 starts immediately.");
            T(_recordMovesCheck, "Also record mouse movement between actions.");
            T(_playMacroBtn, "Play the selected macro (Enter).");
            T(_playOnceBtn, "Play the selected macro a single time.");
            T(_stopPlayBtn, "Stop the macro that's playing.");
            T(_deleteMacroBtn, "Delete the selected macro (Delete).");
            T(_macroLoopNum, "How many times to repeat. 0 = loop forever.");
            T(_macroSpeedNum, "Playback speed. 10 = normal (1.0x), 20 = twice as fast.");
            T(_macroPreserveHoldsCheck, "Keep held keys/buttons at their real recorded length when speeding up — so 2× / 4× only shorten the gaps between actions, not a held movement key (WASD). Off = everything scales with speed.");
            T(_pinMacroBtn, "Pin the selected macro to the top of the list.");
            T(_resetMacroStatsBtn, "Clear the selected macro's play count and last-played time.");
            T(_macroSearchBox, "Filter the macro list by name.");
            T(_exportMacroBtn, "Save the selected macro to a file you can share or back up.");
            T(_importMacroBtn, "Load a macro from a file and add it to your list.");
            T(_exportAllBtn, "Save all your macros to a single file.");
            T(_importAllBtn, "Load macros from a file, adding them to your list.");

            // Each hotkey capture row: explain how to set and clear a binding.
            foreach (var pair in _bindingControls)
            {
                _tips.SetToolTip(pair.Value,
                    "Click, then press a key combination to set this hotkey. " +
                    "Press Backspace or Delete to clear it.");
            }
            if (_intervalStepNum != null)
            {
                _intervalStepNum.AccessibleName = "Interval step in milliseconds";
                T(_intervalStepNum, "How much the increase/decrease-speed hotkeys change the click interval, in milliseconds.");
            }

            // ── Statistics ──────────────────────────────────────────────────────
            T(_historySearchBox, "Filter the session list by date or profile name.");
            T(_historyProfileFilter, "Show only sessions from a specific profile.");
            T(_sessionGoalNum, "Set a target number of clicks for the current session.");
            T(_resetStatsBtn, "Reset the current-session counters (not your lifetime totals).");
            T(_resetLifetimeBtn, "Reset your all-time totals. This cannot be undone.");

            // ── Settings ────────────────────────────────────────────────────────
            T(_themeCombo, "Choose a colour theme. Applied instantly.");
            T(_followSystemThemeCheck, "Match Windows: with the Light or Dark theme selected, Tempo follows Windows' light/dark mode AND adopts your Windows accent colour, switching live the moment you change either in Windows Settings. Pick a colourful theme (Synthwave, Ocean, …) and it keeps that look untouched. While this is on the theme picker is turned off; a custom accent colour still overrides everything.");
            T(_audioDeviceStatus, "Live view of the audio devices Tempo can hear through, updated every few seconds. Captions capture 'what the PC is playing' via the speaker shown here; with no speaker, switch \"Listen to\" to Microphone. When a speaker appears or comes back (headphones plugged in, Bluetooth reconnects), running captions recover automatically.");
            T(_captionSourceTagCheck, "Show which app the audio comes from at the start of the caption bar — '♪ YouTube ·', '♪ Roblox ·', and the app name on the '♪ Music or sounds' note. Untick to hide the tag and keep only the words and speaker labels. Takes effect from the next caption line.");
            T(_captionGpuCheck, "Run the speech engine on your graphics card (Vulkan — works on AMD, Intel and NVIDIA) instead of the processor. Experimental: on some PCs it's much faster, on others slower than CPU. Takes effect the next time Tempo starts. If the GPU engine can't keep up with live audio, Tempo switches this off again by itself and returns to the proven CPU engine. AMD processors always use the normal CPU engine — nothing special needed.");
            T(_speakerDeviceCombo, "Which SPEAKER captions listen through. Tempo hears 'what the PC plays' via this device — with several outputs (headset + monitor speakers), pick the one your videos/games actually play on. 'Default' follows whatever Windows has as its output. Devices are listed by model; a device whose model can't be read shows as 'Unknown speaker'. Applies immediately, and if the chosen device is unplugged, captions fall back to the default and say so below.");
            T(_micDeviceCombo, "Which MICROPHONE is used when 'Listen to' is set to Microphone (or Auto falls back to it). With several mics (webcam mic + USB mic), pick the good one. 'Default' follows Windows. Applies immediately; an unplugged choice falls back to the default with a note below.");
            // ── Camera-relative movement ─────────────────────────────────────────
            T(_movementEnableCheck, "While armed, Tempo intercepts W/A/S/D and sends its own combination instead, steering by the camera direction it has been tracking. It cannot READ the game's camera (that lives in another process) — it estimates it from your mouse movement, so calibrate it first. Bind the 'Camera-relative movement' hotkey to arm it without leaving the game. Emergency stop always disarms it.");
            T(_movementFrameCombo, "World-locked: the direction you press is held in WORLD space, so as you swing the camera Tempo re-mixes the keys and you keep travelling the way you aimed — this is what lets you circle-strafe or keep running one way while looking another. Camera-relative pass-through: keys mean camera-space directions, which is what nearly every third-person game already does by itself, so in those games this mode does nothing on purpose.");
            T(_movementDegPerCountNum, "How far the game's camera turns for each unit of raw mouse movement. Everything else depends on this number being right. Use Calibrate rather than guessing, and turn OFF in-game mouse acceleration — it breaks the straight-line relationship this relies on.");
            T(_movementSmoothingNum, "Softens direction changes. 0 means instant, which is what 'responsive' means — any value above 0 is deliberately adding lag in exchange for a smoother feel. Applied frame-rate independently, so the feel doesn't change with the update rate.");
            T(_movementHysteresisNum, "Anti-jitter. W/A/S/D can only express 8 directions, so a heading sitting exactly on the boundary between two of them would rattle the keys back and forth many times a second. The heading must travel this many degrees PAST a boundary before the keys flip. Raise it if you see chatter; lower it for slightly crisper turns.");
            T(_movementHzNum, "How often the movement loop runs. Everything is time-based, so this changes only how finely the camera is tracked — never how fast you move or turn.");
            T(_movementDeadzoneNum, "Gamepad stick deadzone, applied to the stick's overall push rather than to each axis separately — which is what keeps diagonals honest instead of bending them toward the cardinal directions.");
            T(_movementPadYawNum, "How fast a fully-pushed right stick turns the camera, in degrees per second. Only matters if you play with a controller; match it to the game's look speed.");

            T(_languageCombo, "Interface language. Choosing one offers to restart Tempo so it applies everywhere. English, Español, Français, Deutsch, Italiano, Português.");
            T(_alwaysOnTopCheck, "Keep the Tempo window above other windows.");
            T(_rememberWindowCheck, "Reopen Tempo at the same position and size it had when you last closed it.");
            T(_rememberTabCheck, "Reopen Tempo on the tab you were last using instead of always starting on Clicker. Handy after a long unattended run: if Windows reboots overnight and Tempo restarts with it, you come back to the Macros tab you left it on rather than the Clicker page.");
            T(_opacitySlider, "Make the Tempo window see-through. 100% is solid; lower values let what's behind show through.");
            T(_customAccentCheck, "Use your own accent colour instead of the theme's.");
            T(_chooseAccentBtn, "Pick a custom accent colour.");
            T(_launchStartupCheck, "Start Tempo automatically every time you sign in to Windows - it opens quietly in the system tray, ready for your hotkeys. Tempo keeps this working even if it's moved to another folder, and it respects Windows' own Startup list: if you switch Tempo off in Task Manager > Startup apps, this box turns itself off to match.");
            T(_minimizeToTrayCheck, "Closing the window hides it to the tray instead of quitting.");
            T(_startMinimizedCheck, "Start with the window hidden in the tray.");
            T(_trayNotifyCheck, "Show small notifications from the tray icon.");
            T(_confirmExitCheck, "Ask for confirmation before exiting while clicking is running.");
            T(_safetyEscapeCheck, "Let the Escape key act as an emergency stop.");
            T(_startDelayNum, "Wait this many seconds after pressing Start before the first click. A countdown appears — press Esc or your stop hotkey to cancel it.");
            T(_startDelayBeepCheck, "Play a soft beep on each second of the start-delay countdown, and a higher note on GO.");
            T(_updateFreqCombo, "How often the automatic check may actually run: every launch, at most once a day, or at most once a week. Only applies while 'Check for updates on start' is ticked.");
            T(_startupDelayNum, "When Windows launches Tempo at sign-in, wait this many extra seconds before its background update check — so it doesn't fight other startup apps for the network. Has no effect when you open Tempo yourself.");
            T(_checkUpdatesCheck, "Quietly check GitHub for a newer version when Tempo starts.");
            T(_writeLogCheck, "Write a diagnostic log file. Turn off for extra privacy.");
            T(_showIndicatorCheck, "Show a small click-through badge on screen while clicking, so you can tell Tempo is running even when its window is hidden.");
            T(_minimizeRecordingCheck, "When you start recording or playing a macro, minimise Tempo so it isn't in the way. Stop with the Record/Stop or Emergency-stop hotkey; the window returns automatically.");
            T(_ignoreOwnWindowCheck, "Stops a run from clicking or typing into Tempo itself if the cursor is left over this window — it would otherwise tick boxes, switch tabs and hit buttons at full speed. Your own clicks and keys always work, so Stop is never blocked. This also covers the clicker, which never minimises. Turn off only if you deliberately automate Tempo's own interface.");
            // (header/footer backdrop pickers were replaced by the full-window one)
            T(_recordHistoryCheck, "When off, finished runs aren't saved to your session history and your lifetime totals stop changing — Tempo keeps no record of your activity.");

            // Accessible names for controls a screen reader would otherwise announce
            // only by their value (e.g. an unlabelled "0" spinner).
            void Name(Control c, string name)
            {
                if (c != null) c.AccessibleName = name;
            }
            Name(_hoursNum, "Hours");
            Name(_minutesNum, "Minutes");
            Name(_secondsNum, "Seconds");
            Name(_millisNum, "Milliseconds");
            Name(_buttonCombo, "Mouse button");
            Name(_styleCombo, "Click style");
            Name(_modeCombo, "Click mode");
            Name(_holdMsNum, "Hold each click milliseconds");
            Name(_fixedXNum, "Fixed X");
            Name(_fixedYNum, "Fixed Y");
            Name(_repeatCountNum, "Repeat count");
            Name(_repeatDurationNum, "Repeat seconds");
            Name(_burstSizeNum, "Clicks per burst");
            Name(_burstPauseNum, "Burst pause");
            Name(_intervalJitterNum, "Interval randomization");
            Name(_posJitterNum, "Position randomization");
            Name(_macroLoopNum, "Macro loops");
            Name(_macroSpeedNum, "Macro speed");
            Name(_pointOrderCombo, "Point order");
            Name(_themeCombo, "Theme");
            Name(_languageCombo, "Language");
            Name(_historySearchBox, "Search sessions");
            Name(_historyProfileFilter, "Filter by profile");
            Name(_sessionGoalNum, "Session goal");
            Name(_startDelayNum, "Start delay seconds");
            Name(_profileCombo, "Profile");
            Name(_profileNameText, "Profile name");
        }
    }
}