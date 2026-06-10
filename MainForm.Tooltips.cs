using System.Windows.Forms;

namespace AutoClicker.UI
{
    public partial class MainForm
    {
        private ToolTip _tips;

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

            // ── Clicker: profile ────────────────────────────────────────────────
            T(_notifyFinishCheck, "Play a chime (and show a tray notice) when a fixed-count or fixed-duration run finishes by itself. Manual stops stay silent.");
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
            T(_buttonCombo, "Which mouse button to click.");
            T(_styleCombo, "Single, double or triple click each time.");
            T(_modeCombo, "Interval = steady rate. Burst = bursts of clicks with a pause between.");
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
            T(_unlockSpeedCheck, "Advanced: raise the speed slider far above the normal limit (up to 1000 CPS). Very fast and CPU-heavy — shows a warning before enabling.");
            T(_speedMinusBtn, "Slightly slower.");
            T(_speedPlusBtn, "Slightly faster.");

            // ── Multi-Point ─────────────────────────────────────────────────────
            T(_pointOrderCombo, "The order the engine visits enabled points each cycle.");
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
            T(_recordMovesCheck, "Also record mouse movement between actions.");
            T(_playMacroBtn, "Play the selected macro (Enter).");
            T(_playOnceBtn, "Play the selected macro a single time.");
            T(_stopPlayBtn, "Stop the macro that's playing.");
            T(_deleteMacroBtn, "Delete the selected macro (Delete).");
            T(_macroLoopNum, "How many times to repeat. 0 = loop forever.");
            T(_macroSpeedNum, "Playback speed. 10 = normal (1.0x), 20 = twice as fast.");
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
            T(_languageCombo, "Interface language. Choosing one offers to restart Tempo so it applies everywhere. English, Español, Français, Deutsch, Italiano, Português.");
            T(_alwaysOnTopCheck, "Keep the Tempo window above other windows.");
            T(_rememberWindowCheck, "Reopen Tempo at the same position and size it had when you last closed it.");
            T(_opacitySlider, "Make the Tempo window see-through. 100% is solid; lower values let what's behind show through.");
            T(_customAccentCheck, "Use your own accent colour instead of the theme's.");
            T(_chooseAccentBtn, "Pick a custom accent colour.");
            T(_minimizeToTrayCheck, "Closing the window hides it to the tray instead of quitting.");
            T(_startMinimizedCheck, "Start with the window hidden in the tray.");
            T(_trayNotifyCheck, "Show small notifications from the tray icon.");
            T(_confirmExitCheck, "Ask for confirmation before exiting while clicking is running.");
            T(_safetyEscapeCheck, "Let the Escape key act as an emergency stop.");
            T(_startDelayNum, "Wait this many seconds after pressing Start before the first click.");
            T(_checkUpdatesCheck, "Quietly check GitHub for a newer version when Tempo starts.");
            T(_writeLogCheck, "Write a diagnostic log file. Turn off for extra privacy.");
            T(_showIndicatorCheck, "Show a small click-through badge on screen while clicking, so you can tell Tempo is running even when its window is hidden.");
            T(_minimizeRecordingCheck, "When you start recording or playing a macro, minimise Tempo so it isn't in the way. Stop with the Record/Stop or Emergency-stop hotkey; the window returns automatically.");
            T(_headerGifBtn, "Pick an animated GIF (or image) to play as a backdrop behind the header.");
            T(_headerGifClearBtn, "Remove the header backdrop image.");
            T(_footerGifBtn, "Pick a second GIF to play in a band along the bottom of the window.");
            T(_footerGifClearBtn, "Remove the footer backdrop image (and hide the bottom band).");
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
