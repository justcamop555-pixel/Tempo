using System.Drawing;
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

            // Theme the tooltip dark. A default ToolTip is the system light/yellow popup,
            // which flashed bright over the dark UI on every hover — users lumped that in
            // with the "light flash" report. Owner-draw it in the theme surface colour.
            TipStyle.SetTheme(_theme);
            TipStyle.Apply(_tips);

            // Translates, and that matters twice over.
            //
            // This helper used to set the text straight onto the control. Because it
            // translated nothing it was invisible to the choke-point audit — that scan
            // finds helpers which DO call Localization.T on a parameter, so one that
            // never translates is precisely the one it cannot flag. The result was 137
            // tooltips, and the AccessibleDescription a screen reader reads out, in
            // English no matter what language Tempo was running in.
            //
            // T() returns its input on a miss, so a tooltip with no entry yet is
            // unchanged and nothing regresses; the ones that have entries now appear.
            void T(Control c, string text)
            {
                if (c != null)
                {
                    string localized = Utils.Localization.T(text);
                    // Wrapped to a readable width, and merged with any other tip on the
                    // control (the layout fitter's full caption, the Set-key clash warning).
                    TipStyle.Set(_tips, c, localized);
                    // Screen readers read AccessibleDescription, so the same help is
                    // available without a mouse.
                    c.AccessibleDescription = localized;
                }
            }

            // ── Sidebar navigation ──────────────────────────────────────────────
            // Name the keyboard shortcut for each tab. Ctrl+1…9 and Ctrl+Tab have
            // always worked; until now nothing in the app mentioned them. Each tip also says
            // where the tab opens and what the right button does, so it is set again whenever a
            // tab moves into a window of its own or back (MainForm.TabWindows.cs).
            //
            // Put together from TRANSLATED pieces. It used to be assembled in English and then
            // looked up whole — "Open the Clicker tab  ·  Ctrl+1. Ctrl+Tab cycles…" can never be
            // a key in any table — so every tab's tip, and what a screen reader says for it, was
            // English in every language (and said "Ctrl" to German users, whose key reads Strg).
            RefreshNavTips();

            // ── Clicker: profile ────────────────────────────────────────────────
            T(_notifyFinishCheck, "Play a chime (and show a tray notice) when a fixed-count or fixed-duration run \u2014 or a finite macro playback \u2014 finishes by itself. Manual stops stay silent.");
            T(_sessionHistoryList, "Double-click a row for details \u00b7 right-click for more options \u00b7 click a column header to sort.");
            T(_traySleepCheck, "After 30 minutes hidden in the tray with nothing running, global hotkeys and the cursor trail pause, so a forgotten Tempo can't start clicking invisibly. The first press of a paused hotkey only wakes Tempo — a notification says so — and the next press works. Opening the window or starting something from the tray menu wakes it too.");
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
            T(_captionFontCombo, "Font for the captions. Each name in the list is drawn in its own font, and every installed text font follows the recommended ones. Changes show at once.");
            T(_captionColorBtn, "Pick the caption text colour. Bright yellow reads well over most games. (The bar background always stays transparent.)");
            T(_captionBackgroundCheck, "Show the rounded panel behind captions. Turn this off for a clean text-only look with no background \u2014 the text keeps a soft glow so it stays readable.");
            T(_captionPanelSizeCombo, "How big the panel behind the captions is. Small hugs the words, Medium keeps a calm, steady width and is only as tall as the lines shown, Large fills the whole caption bar. Changes show at once.");
            // What a profile IS, said where people meet it first. "Switch between your saved click profiles" assumed
            // they already knew, and people asked what profiles were for.
            T(_profileCombo, "A profile is one saved clicker setup: the interval, the button or key, the click type and mode, where it clicks (with its Multi-Point points), when it stops and the randomisation. Keep one for each game or task and switch between them here, on the Profiles tab, from the tray menu or with the Next / Previous profile hotkeys.");
            T(_profileNameText, "This profile's name. Type a new one and press Save to rename the profile.");
            // New starts from the defaults and Duplicate copies the profile as saved, as the code has
            // always done; New's tip promised "from the current settings", which neither does.
            T(_newProfileBtn, "Add a new profile with the default settings and switch to it. Set it up, then press Save.");
            T(_saveProfileBtn, "Save this tab's settings and the Multi-Point list into this profile. Until you do, changes are not part of the profile.");

            // ── Profiles tab ────────────────────────────────────────────────────
            // None of its controls had a tip.
            T(_profileSearch, "Find a profile by its name or description.");
            T(_profileCategoryFilter, "Show only one category. A profile's category is set with Edit details… (right-click its card).");
            T(_profileSortCombo, "The order of the cards. Favourites always come first.");
            T(_profileFavOnly, "Show only your favourites. Click the star on a card to make it one.");
            T(_profileNewButton, "Make a new profile: give it a name, an icon and a category, and Tempo switches to it. Then set up its clicking on the Clicker tab and press Save there.");
            T(_profileImportButton, "Add a profile from a file exported with Export… (right-click a card) — from this PC or someone else's.");
            T(_profileRecycleButton, "Deleted profiles wait here, so you can bring one back.");
            T(_duplicateProfileBtn, "Make a copy of the selected profile as it was last saved. Save first to include your changes.");
            T(_deleteProfileBtn, "Delete the selected profile. You can restore it from the recycle bin on the Profiles tab.");

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
            T(_intervalJitterNum, "Maximum random variation added to / removed from the delay (ms) — never more than the delay allows, so the average speed stays what you set. The hold and the pause between bursts vary with it.");
            T(_randPosCheck, "Nudge each click by a small random offset.");
            T(_posJitterNum, "Maximum random distance from the point, in pixels. Clicks stay on the window under the point.");
            T(_humanizeBtn, "Varies the delay and the position a little and holds each click for a natural moment where it was instant, so the clicking looks less robotic. Press again to turn it off.");

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
            T(_antiFreezeStatusLabel, "While it clicks, Anti-Freeze asks the app you are clicking for an answer twice a second. "
                + "If it stops answering, the clicks wait for it; if it answers slowly, or the PC's CPU or memory is full, "
                + "the rate drops and comes back as things recover. The Max CPS cap always applies.");
            T(_antiFreezePowerLabel, "How Windows is powering this PC. On battery and in the efficiency modes Windows runs apps "
                + "it thinks are in the background on slower cores; while Tempo clicks or plays a macro it asks Windows for full "
                + "speed, and gives it back when it stops.");
            T(_antiFreezeHealthLabel, "The whole PC, counted the way Task Manager counts it: the CPU across every core, the "
                + "busiest part of the GPU, and the memory in use.");

            // ── Multi-Point ─────────────────────────────────────────────────────
            T(_pointOrderCombo, "The order the engine visits enabled points each cycle:\n" +
                "  Sequential - top to bottom, then repeat\n" +
                "  Reverse - bottom to top, then repeat\n" +
                "  Random - a different random point each time\n" +
                "  Ping-Pong - down the list then back up, repeating");
            T(_addPointBtn, "Add a point by typing its coordinates.");
            T(_capturePointBtn, "Click on screen to add points, one after another — each is numbered where you click, and the magnifier shows the exact pixel. Backspace takes the last one back; Enter, Esc or a right-click finishes.");
            T(_editPointBtn, "Edit the selected point.");
            T(_duplicatePointBtn, "Duplicate the selected points (Ctrl+D).");
            T(_togglePointBtn, "Enable or disable the selected points.");
            T(_removePointBtn, "Remove the selected points (Delete).");
            T(_selectAllPointsBtn, "Select every point (Ctrl+A), so one action takes them all.");
            T(_showPointsBtn, "Flash numbered markers on screen at each point.");
            T(_clearPointsBtn, "Remove all points.");
            T(_toggleAllPointsBtn, "Enable every point, or disable them all if they're all on.");

            // ── Macros ──────────────────────────────────────────────────────────
            T(_recordBtn, "Start recording mouse and keyboard input.");
            T(_stopRecordBtn, "Stop recording.");
            T(_recordCountdownNum, "Seconds to wait (3, 2, 1…) before recording starts, so you can switch to the app you want to record. 0 starts immediately.");
            T(_recordMovesCheck, "Also record the mouse's path between actions — including where it rests and where it turns sharply, so hover menus and tooltips play back too.");
            T(_recordCameraCheck, "For games that turn the camera with the mouse — first person, shift lock, holding right-click. While the game holds or hides the pointer, how far the mouse moved is recorded and played back as camera turns; while the pointer is free (the game's menus), where it was is recorded, so clicks land on the same spots.");
            T(_playMacroBtn, "Play the selected macro (Enter).");
            T(_playOnceBtn, "Play the selected macro a single time.");
            T(_stopPlayBtn, "Stop the macro that's playing.");
            T(_deleteMacroBtn, "Delete the selected macro (Delete).");
            T(_macroLoopNum, "How many times to repeat. 0 = loop forever.");
            T(_macroSpeedNum, "Playback speed. 10 = normal (1.0x), 20 = twice as fast.");
            T(_macroPreserveHoldsCheck, "Keep held keys/buttons at their real recorded length when speeding up — so 2× / 4× only shorten the gaps between actions, not a held movement key (WASD). Off = everything scales with speed.");
            T(_macroSmoothCheck, "Glide the pointer to each position instead of jumping. The glide happens in the wait before each move or click, so the macro keeps its recorded timing, and a recording made without mouse movement glides between its clicks too.");
            T(_pinMacroBtn, "Pin the selected macro to the top of the list.");
            T(_liveFollowLink, "You scrolled away, so the list stopped following the macro. Click to follow it again.");
            T(_resetMacroStatsBtn, "Clear the selected macro's play count and last-played time.");
            T(_macroSearchBox, "Filter the macro list by name, notes, or what the macro does — type a key like F5, a button like right, or part of a script filename.");
            T(_exportMacroBtn, "Save the selected macro to a file you can share or back up.");
            T(_importMacroBtn, "Load a macro from a file and add it to your list.");
            T(_exportAllBtn, "Save all your macros to a single file.");
            T(_importAllBtn, "Load macros from a file, adding them to your list.");

            // Each hotkey capture row: explain how to set and clear a binding.
            // Through T(), like every other tooltip on this page. These two were set straight
            // onto the controls, so the hotkey boxes explained themselves in English — and what
            // a screen reader announced for the interval box was English — in every language.
            foreach (var pair in _bindingControls)
            {
                T(pair.Value,
                    "Click, then press a key combination to set this hotkey. " +
                    "Press Backspace or Delete to clear it.");
            }
            if (_intervalStepNum != null)
            {
                _intervalStepNum.AccessibleName = Utils.Localization.T("Interval step in milliseconds");
                T(_intervalStepNum, "How much the increase/decrease-speed hotkeys change the click interval, in milliseconds.");
            }

            // ── Statistics ──────────────────────────────────────────────────────
            T(_historySearchBox, "Filter the session list by date or profile name.");
            T(_historyProfileFilter, "Show only sessions from a specific profile.");
            T(_sessionGoalNum, "Set a target number of clicks for the current session.");
            T(_resetStatsBtn, "Reset the current-session counters (not your lifetime totals).");
            T(_resetLifetimeBtn, "Reset your all-time totals. This cannot be undone.");

            T(_extraShiftCtrlCheck, "F6 still fires while you hold Shift to sprint or Ctrl to crouch. This applies to the function keys, the middle and side mouse buttons, the media keys, Pause and Scroll Lock. A combination bound to something else (say Shift + F6) still does that. Keys that type or edit (letters, digits, arrows, Enter…) always need their exact combination, so typing and text shortcuts are not affected. Like the key itself, its Shift and Ctrl versions are taken from other programs while Tempo runs.");

            // ── Keybinds: the quick-play macro pickers ──────────────────────────
            // These combos took the place of the description labels on their rows, so
            // the description each one replaced is attached here instead.
            foreach (var hk in Models.HotkeyActions.All)
            {
                int slot = SlotForAction(hk.Action);
                if (slot > 0 && _macroSlotCombos.TryGetValue(slot, out ComboBox slotCombo))
                {
                    T(slotCombo, hk.Description);
                }
            }

            // ── Settings ────────────────────────────────────────────────────────
            T(_themeCombo, "Choose a colour theme. Applied instantly.");
            T(_followSystemThemeCheck, "Match Windows: with the Light or Dark theme selected, Tempo follows Windows' light/dark mode AND adopts your Windows accent colour, switching live the moment you change either in Windows Settings. Pick a colourful theme (Synthwave, Ocean, …) and it keeps that look untouched. While this is on the theme picker is turned off; a custom accent colour still overrides everything.");
            T(_audioDeviceStatus, "Live view of the audio devices Tempo can hear through, updated every few seconds. Captions capture 'what the PC is playing' via the speaker shown here; with no speaker, switch \"Listen to\" to Microphone. When a speaker appears or comes back (headphones plugged in, Bluetooth reconnects), running captions recover automatically.");
            T(_captionSourceTagCheck, "Show which app the sound comes from — '♪ YouTube', '♪ Roblox' — on a small label at the top edge of the caption panel. It stays there however long the words run. Untick to hide it and keep only the words and speaker labels. Changes show at once.");
            T(_captionGpuCheck, "Runs the speech engine on your graphics card (Vulkan: AMD, Intel and NVIDIA) instead of the processor — many times faster with the big models, so captions keep up with the speech. Ticked by itself when this PC has a graphics card of its own; untick it to keep captions on the processor, or tick it to use a graphics chip built into the processor. Takes effect the next time Tempo starts. If the card falls behind (a game using it), captions move to a faster model until it has room again.");
            T(_captionLyricsCheck, "Off (the default): Tempo's voice detection skips stretches of sound with no voice in them — music, a fan, gunfire, a game's menus — before the speech engine hears them, because that is where it invents words. Singing can count as 'no voice', so song lyrics may be skipped too. On: everything with sound is transcribed, lyrics included, and instrumental music can bring stray invented words back. Applies at once.");
            T(_captionOnlyAppCheck, "On (the default): captions listen only to the app making the sound — the one the caption bar names, such as YouTube in your browser — so other apps' pings, music and games can't break into the words or the speaker labels. When another app's sound takes over for a few seconds, captions move to it. Off: captions hear everything your speaker plays. Needs Windows 11 (or Windows 10 build 20348 and later). Applies at once.");
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
            T(_movementTurnsCombo, "Which mouse movement counts as turning the camera. 'While the game holds the pointer' counts it only while the game hides the pointer or keeps it in one place, which is what games do while the mouse turns the camera (in Roblox: holding the right button, shift lock, first person). Moving the pointer around then turns nothing, just as in the game. 'With every mouse movement' counts all of it, for a game whose camera always follows the mouse.");
            T(_movementFineCheck, "W/A/S/D can only make 8 directions, so the heading can be up to 22.5° off. With this on, Tempo pulses the keys to aim between them (W held and D pressed a quarter of the time goes about 11° right of forward), and the error stays under about 4°. The character zig-zags slightly to do it; in a game without movement acceleration, such as Roblox, its body visibly flicks between the two directions. Anti-jitter does not apply while this is on.");
            T(_movementCalibrateBtn, "Measures it from one 360° turn in your game — far better than guessing.");

            T(_languageCombo, "Interface language. Choosing one offers to restart Tempo so it applies everywhere. English, Español, Français, Deutsch, Italiano, Português.");
            T(_alwaysOnTopCheck, "Keep the Tempo window above other windows.");
            T(_rememberWindowCheck, "Reopen Tempo at the same position and size it had when you last closed it.");
            T(_rememberTabCheck, "Reopen Tempo on the tab you were last using instead of always starting on Clicker. Handy after a long unattended run: if Windows reboots overnight and Tempo restarts with it, you come back to the Macros tab you left it on rather than the Clicker page.");
            T(_opacitySlider, "Make the Tempo window see-through. 100% is solid; lower values let what's behind show through. Double-click to go back to 100%.");
            // Greyed out with no background image (the note beside it then says "(no image)").
            T(_bgDimTrack, "Tones the background image down behind Tempo's text, towards the theme's own background colour, so it reads on any picture. 0% shows the picture as it is. Double-click to go back to 55%.");
            T(_customAccentCheck, "Use your own accent colour instead of the theme's.");
            T(_chooseAccentBtn, "Pick a custom accent colour.");
            T(_hdrCompensateCheck, "With HDR on, Windows shows Tempo and other non-HDR apps with lighter shadows than an ordinary screen: near-blacks turn charcoal and dark themes look milky. This darkens Tempo's colours by exactly that difference so they look as designed. It follows the window between screens and updates by itself when HDR is switched on or off. Untick it if a colour profile already corrects SDR apps under HDR — correcting twice makes shadows too dark.");
            T(_launchStartupCheck, "Start Tempo automatically every time you sign in to Windows - it opens quietly in the system tray, ready for your hotkeys, or with its window if you tick the option below. Tempo keeps this working even if it's moved to another folder, and it respects Windows' own Startup list: if you switch Tempo off in Task Manager > Startup apps, this box turns itself off to match.");
            T(_openAtSignInCheck, "When Windows starts Tempo at sign-in, open its window instead of starting quietly in the tray. Only the sign-in launch: opening Tempo yourself follows \"Start minimised to tray\".");
            T(_minimizeToTrayCheck, "Closing the window hides it to the tray instead of quitting.");
            T(_startMinimizedCheck, "Start with the window hidden in the tray.");
            T(_trayNotifyCheck, "Show small notifications from the tray icon.");
            T(_confirmExitCheck, "Ask for confirmation before exiting while clicking is running.");
            T(_safetyEscapeCheck, "Let the Escape key act as an emergency stop.");
            T(_startDelayNum, "Wait this many seconds after pressing Start before the first click. A countdown appears — press Esc or your stop hotkey to cancel it.");
            T(_startDelayBeepCheck, "Play a soft beep on each second of the start-delay countdown, and a higher note on GO.");
            T(_updateFreqCombo, "How often the automatic check may run: every launch, at most once a day, or at most once a week. While Tempo stays open it checks again at the same pace (twice a day for 'Every launch'). Only applies while the box beside it is ticked.");
            T(_startupDelayNum, "When Windows launches Tempo at sign-in, wait this many extra seconds before its background update check — so it doesn't fight other startup apps for the network. Has no effect when you open Tempo yourself.");
            T(_checkUpdatesCheck, "Quietly check GitHub for a newer version when Tempo starts, and again while it stays open. A check that fails is tried again a few minutes later.");
            T(_checkRobloxUpdatesCheck, "Every two hours while Roblox is installed, ask Roblox which version it hands out — the same public address Roblox itself asks — and tell you when yours is behind, so you choose when to update. Nothing about you or your accounts is sent.");
            T(_robloxGameNamesCheck, "Tempo reads which Roblox game each client is in from Roblox's own log on this PC, and shows it — on the captions' source tag, in game mode, Live debug and the Accounts tab. This asks Roblox for the game's name, once per game, sending only the game's number. Off, nothing goes online for it and the game is known by its number.");
            // Where the log is and how big it is now — refreshed whenever the switch changes.
            RefreshLogTip();
            T(_showIndicatorCheck, "Shows a small click-through badge while the clicker runs or a macro plays, so you can tell Tempo is working even when its window is hidden. Customise… sets where it sits and what it shows. The recording badge always shows.");
            T(_minimizeRecordingCheck, "When you start recording or playing a macro, minimise Tempo — and the Accounts window when it's open on its own — so they aren't in the way. Only when a Record/Stop or Emergency-stop hotkey is bound, so you can always finish. They come back by themselves afterwards, unless the macro was started by a hotkey from inside another app: then Tempo stays out of the way.");
            T(_ignoreOwnWindowCheck, "Stops a run from clicking or typing into Tempo itself if the cursor is left over one of its windows — it would otherwise tick boxes, switch tabs and hit buttons at full speed. Clicks made without moving your cursor go through Tempo's windows to the app underneath. Your own clicks and keys always work, so Stop is never blocked. Turn off only if you deliberately automate Tempo's own interface.");
            T(_guardShortcutsCheck, "While the clicker runs or a macro plays or records, the Windows key and its shortcuts (Start, Win+D, Win+G, Win+Tab…), Ctrl+Esc and the Sticky Keys and Filter Keys pop-ups are held back, so a stray press can't pull the game out of focus mid-run. Tempo's own keybinds always work, and recordings leave these shortcuts out. Never held back: Alt+Tab and Ctrl+Shift+Esc, so there is always a way out; Windows' screenshot and recording keys (Win+Shift+S, Win+Print Screen, Win+Alt+R); nor Ctrl+Alt+Del and Win+L, which Windows keeps for itself. Not while the clicker uses \"Click without moving my cursor\": you are using the PC then.");
            // Its copies on the Clicker and Macros tabs explain the same thing, in the same words.
            foreach (Control copy in new Control[] { _clickerGuardCheck, _macroGuardCheck })
            {
                if (copy == null || _guardShortcutsCheck == null) { continue; }
                TipStyle.Set(_tips, copy, _guardShortcutsCheck.AccessibleDescription);
                copy.AccessibleDescription = _guardShortcutsCheck.AccessibleDescription;
            }
            // (header/footer backdrop pickers were replaced by the full-window one)
            T(_recordHistoryCheck, "When off, finished runs aren't saved to your session history and your lifetime totals stop changing — Tempo keeps no record of your activity.");

            // ── Controls that had no tip at all (found by listing every control without one) ──────────────
            // Only where the caption alone does not say what happens: "Save", "Clear" and "Test pop-up" mean
            // what they say. The one-off buttons a page keeps in no field are found by their caption on it.
            void TCaption(Control root, string caption, string text)
            {
                if (root == null) { return; }
                string shown = Utils.Localization.T(caption);
                var pending = new System.Collections.Generic.Stack<Control>();
                pending.Push(root);
                while (pending.Count > 0)
                {
                    foreach (Control c in pending.Pop().Controls)
                    {
                        if (c is ButtonBase b && string.Equals(b.Text, shown, System.StringComparison.Ordinal)) { T(b, text); }
                        if (c.HasChildren) { pending.Push(c); }
                    }
                }
            }

            // Clicker
            T(_clickerAdvancedBtn, "Show or hide the less-used cards: the second cursor, clicking without moving your cursor, and sounds.");
            T(_secondCursorEnableCheck, "Puts a second pointer on screen that Tempo can click with while your real one stays free. Aim it with the Second cursor hotkeys (Keybinds tab) or with a second mouse.");

            // Multi-Point
            T(_pointsSaveBtn, "Save the points into the current profile, together with the Clicker tab's settings.");

            // Macros
            T(_editMacroBtn, "Open the selected macro's steps to change, add or remove them.");
            T(_renameMacroBtn, "Rename the selected macro (F2).");
            T(_duplicateMacroBtn, "Make a copy of the selected macro.");
            T(_notesMacroBtn, "Write a note on the selected macro — what it is for, where to start it. The search box finds notes too.");
            T(_mergeMacroBtn, "Add the steps of another macro to the end of the selected one.");
            T(_fixMacroBtn, "Check the selected macro for problems — a key left held down, clicks on a screen that is no longer connected, robotic timing — and repair them.");
            T(_macroRecycleBtn, "Deleted macros wait here, so you can bring one back.");
            T(_macroSortCombo, "The order of the macro list. Pinned macros stay at the top.");
            T(_recordKeysCheck, "Record key presses as well as the mouse. Untick to record the mouse only.");
            T(_appendRecordCheck, "Add the new recording to the end of the selected macro instead of making a new one.");
            T(_macroCountdownNum, "Seconds to wait (3, 2, 1…) before playback starts, so you can switch to the window it plays into. 0 starts at once.");
            T(_macroLoopDelayNum, "A pause between one loop and the next, in milliseconds.");
            foreach (string speed in new[] { "0.5×", "1×", "2×", "4×" })
            {
                TCaption(_playMacroBtn?.Parent, speed, "Quick playback speed: sets the Speed box above (0.5× is half speed, 2× twice as fast).");
            }
            T(_liveEveryStepCheck, "One row for every step. Off, a run of mouse travel (the moves and the short waits between them) is one row, and so is a run of camera turns.");

            // Statistics
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            TCaption(_statsPage, "1K", Utils.Localization.F("Set the session goal to {0} clicks.", 1000.ToString("N0", culture)));
            TCaption(_statsPage, "10K", Utils.Localization.F("Set the session goal to {0} clicks.", 10000.ToString("N0", culture)));
            TCaption(_statsPage, "100K", Utils.Localization.F("Set the session goal to {0} clicks.", 100000.ToString("N0", culture)));
            TCaption(_statsPage, "Export CSV…", "Save your statistics as a CSV file, which spreadsheets such as Excel open.");
            TCaption(_statsPage, "Export JSON…", "Save your statistics as a JSON file, for scripts and other programs.");
            TCaption(_statsPage, "Clear history", "Empty the recent-sessions list. Your click totals stay as they are.");
            TCaption(_statsPage, "Copy summary", "Copy a short text summary of your statistics, to paste anywhere.");

            // Keybinds
            TCaption(_keybindsPage, "Reset to defaults", "Put every action back to its default key (F6, F7, F8 and F9 for the main four; the rest unbound). Asks first, and saves at once.");
            T(_copyKeybindsBtn, "Copy a list of your hotkeys as text — for a note, a guide or a bug report.");

            // Settings
            T(_settingsSearch, "Type part of a setting's name to highlight it. Enter jumps to the first match, opening Advanced if it is there; Esc clears.");
            T(_accentHexBox, "Type an accent colour as a hex code, such as #D79921.");
            T(_fullGifBtn, "Pick a picture or an animated GIF to show behind the whole window. The Dim slider keeps text readable over it.");
            T(_fullGifClearBtn, "Remove the background image.");
            T(_cursorTrailCheck, "Draws a colourful trail behind the mouse pointer. Just for fun: it never affects clicking.");
            T(_hideWhenClickingCheck, "When clicking starts, Tempo tucks its window into the tray. Open it again from the tray icon or with the Show / Hide window hotkey.");
            TCaption(_settingsPage, "Reset window position", "Put the window back to its first-start size, centred on the screen.");
            TCaption(_settingsPage, "Customise…", "Choose where the badge sits and what it shows.");
            T(_customNotifyCheck, "Show Tempo's notifications as its own animated cards, in the corner you choose, instead of Windows' plain ones.");
            T(_notifyCornerCombo, "The corner of the screen where Tempo's notification cards appear.");
            // Its comment always said "see tooltip" — there was none.
            T(_mirrorNotifyCheck, "Shows other apps' Windows notifications as Tempo cards too. Windows asks once for permission to read them. It does not stop Windows' own pop-ups — Do not disturb does that (Stop Windows pop-ups…).");
            T(_notifyDurationNum, "How many seconds a card stays on screen.");
            T(_mirrorClearCheck, "Once Tempo has shown another app's notification, remove it from the Windows notification centre so it is not kept twice.");
            TCaption(_settingsPage, "Stop Windows pop-ups…", "Opens Windows' notification settings. Turn on Do not disturb there: Windows stops its own pop-ups but still hands notifications to Tempo, so only Tempo's cards appear.");
            T(_notifyPermissionBtn, "Opens Windows' privacy page for notifications, where Tempo can be allowed to read other apps' notifications.");
            T(_notifyCloseCheck, "Put a ✕ on each card to close it. Without it, a card still closes when clicked and goes away by itself.");
            TCaption(_settingsPage, "Notification history…", "The notifications shown lately, including the ones you missed while you were busy.");
            T(_mutedAppsBtn, "Apps you muted (right-click one of their cards to mute it). Unmute them here.");
            T(_captionLinesNum, "How many lines of speech the caption bar keeps on screen before the oldest scrolls away.");
            T(_captionLangCombo, "The language being spoken. Auto-detect suits most videos; pick the language if captions come out in the wrong one.");
            TCaption(_settingsPage, "Models folder…", "Speech models: open their folder, get more from the official site, or use a model file from anywhere.");
            T(_captionDownloadModelBtn, "Download the speech model chosen above. It downloads once, then works offline.");
            T(_captionOwnVoiceCheck, "Skip captioning your own voice when it comes back through the speakers (a headset's sidetone, \"Listen to this device\"). Uses your microphone only to tell when you are speaking — its loudness, nothing is recorded.");
            TCaption(_settingsPage, "Diagnose Windows bar", "Checks whether Windows 11's own Live Captions bar works with Tempo on this PC, says what is wrong if it does not, and fixes what it can.");
            T(_integrityCheck, "Each time Tempo starts, it checks that its program file is the copy that was installed here and, where it can, that it matches the official release on GitHub. It can spot a changed copy; it cannot stop one.");
            TCaption(_settingsPage, "Check now", "Run the tamper check again now.");
            T(_integrityTrustBtn, "Replaced Tempo on purpose (a build of your own, an update from somewhere else)? Make this file the one Tempo expects, so the warning stops.");
            T(_integrityDetailsBtn, "Each step of the check, the file's fingerprint (SHA-256) and where this copy came from.");
            TCaption(_settingsPage, "Open data folder", "Open the folder where Tempo keeps your settings, profiles, macros and statistics.");
            TCaption(_settingsPage, "Export settings…", "Save your settings to a file — to keep, or to bring to another PC with Import settings….");
            TCaption(_settingsPage, "Import settings…", "Load settings from a file saved with Export settings….");
            TCaption(_settingsPage, "Back up all data…", "Copy everything — settings, profiles, macros, statistics — into a folder you choose.");
            TCaption(_settingsPage, "Restore points…", "Tempo saves your data before each update. Pick one of those saves to go back to it; it is put back the next time Tempo starts.");
            TCaption(_settingsPage, "Uninstall Tempo…", "Remove Tempo from this PC. Your data is kept unless you choose to delete it.");
            TCaption(_settingsPage, "Report a bug…", "Write a bug report — you see all of it and can edit it — and post it as a GitHub issue or send it by email.");
            TCaption(_settingsPage, "Email a bug…", "Write a bug report — you see all of it and can edit it — and email it to the developer: your email app, Gmail, Outlook or Yahoo, or copy it.");
            TCaption(_settingsPage, "Open log file", "Open Tempo's log: what it did and any errors, newest at the end. A bug report can include its last lines.");
            T(_settingsAdvancedBtn, "Show or hide the settings most people never need.");
            T(_animateLogoCheck, "Play your logo's animation, when the image you chose for it is an animated GIF.");
            T(_logoSpeedCombo, "How fast the logo's animation plays. 1× is the speed it was made at.");
            TCaption(_settingsPage, "Reset to defaults", "Put every setting back to its default. Asks first, and says what is kept: your keybinds, statistics and language stay, and so do your profiles and macros.");

            // Accessible names for controls a screen reader would otherwise announce
            // only by their value (e.g. an unlabelled "0" spinner).
            // Same story: a screen reader announces AccessibleName, so leaving it
            // untranslated means the spinner a Spanish user just tabbed onto is read
            // out as "Hours".
            void Name(Control c, string name)
            {
                if (c != null) c.AccessibleName = Utils.Localization.T(name);
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
            Name(_intervalJitterNum, "Interval randomisation");
            Name(_posJitterNum, "Position randomisation");
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

            // The profile cards were built before this ran (the first grid is part of start-up).
            RefreshProfileCardTips();

            // Settings → Disable features: what each switch turns off and which keys it lets go of.
            SetupFeatureTips();
        }
    }
}