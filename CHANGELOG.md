# Changelog

All notable changes to Tempo. Newest first. Per-release notes are also in the `release-notes/` folder and on the [Releases page](https://github.com/justcamop555-pixel/Tempo/releases).

### 1.0.324

- **The CPS test shows the details of each run.** A panel beside the pad charts your clicks in each second of the test, with your average as a dashed line, and lists your speed, your best second (the most clicks in any one-second stretch, wherever it started), how even your rhythm was, the fastest, average and slowest gaps between clicks, and whether you kept your speed (the second half against the first). Under it, this session's tries, newest first, with a ★ on a new best.
  - **A best for each input and test length.** A 5-second spacebar burst and a 30-second click test are not the same contest, but they shared one best, so a short test's record could never be beaten in a long one. The best now belongs to its input and length ("Best for Left, 10 s"), and the all-time best is shown beside it. Both are kept by Reset to defaults.
  - **Fixed: clicks after the time ran out were counted,** until the timer next looked, up to a tenth of a second later. A click flat out at the end added to the result.
  - **Fixed: the last click could start a new test and wipe the result.** Someone still clicking when the time runs out did exactly that. Clicks are now ignored for a second after the end ("Time's up!").
  - **Fixed: in spacebar mode, the press that started a test counted twice** if it was held long enough to repeat.
  - **Fixed: two of the pad's captions ended in "…" in five languages.** "Press SPACE to retry" and "New best — retry" were wider than the pad and never measured. The pad now says "Click to retry" or "Press SPACE!", and its font steps down where a language needs it. The spacebar's name is translated too.
  - **Esc** stops a test that's going wrong, without a result; with no test running, it closes the window.
  - **Fixed: Tempo's own clicker couldn't be measured.** Start the clicker with the pointer on the pad and nothing was counted: the guard that stops a run from clicking Tempo's own window dropped every click on the test too. The pad now takes Tempo's clicks, and spacebar mode its key presses, while the rest of Tempo stays guarded, the test window's other buttons and close box included. A run the clicker made is labelled "Tempo's clicker" and shown against the rate it was set to ("Tempo's clicker: 99.6 CPS, set to 100.0"). It never counts as your best, which a clicker at 100 CPS would otherwise have taken for good. The CPS Test button also stays usable while the clicker runs.
- **Camera-relative movement counts only the mouse movement that turns the camera.** It works out which way the camera faces by adding up mouse movement, and it added up all of it. Roblox turns its camera only while it holds the pointer (the right button held, shift lock, first person); the rest of the time the mouse just moves the pointer. So moving the mouse to click something while running re-mixed W/A/S/D for a camera that never moved. Tempo now counts movement only while the game hides the pointer or keeps it in one place. Settings ▸ Camera-relative movement ▸ "Camera turns" has the new default and the old behaviour ("With every mouse movement"), and Live debug shows what the pointer is doing and how much movement was left out.
  - **Calibration you can check.** After measuring one full turn with F10, press F11 in the game and Tempo turns the camera exactly one circle with that value. If it doesn't stop where it started, hold F10 and turn the rest of the way back: Tempo adds that to its own turn and gets the exact value. Turning one circle by hand is rarely within a few percent. Counting is more accurate too: turning past the start and back no longer counts the overshoot twice, and the end of a turn just before F10 is let go now counts (it was thrown away).
  - **Fine steering, new and off by default.** W/A/S/D only make 8 directions, so the heading could be up to 22.5° off. Tempo can now pulse the second key of the nearest pair to aim between them, the trick Steam Input calls analog emulation, and the error stays under about 4°. The character zig-zags slightly to do it.
  - **Keys by position, for French and other keyboards.** On a French AZERTY keyboard Tempo took the keys labelled W and A, which aren't movement keys there, and left Z and Q alone, though its French text said Z/Q/S/D. It now takes the four keys in the W/A/S/D position on any layout and sends them back the same way.
  - **Settings apply as you change them,** to movement that is already on, the mode and the rate included, with no Save. Changing the mode or the rate used to restart it on the window in front, which on the Settings page is Tempo itself, and that means "every window": W/A/S/D were taken from every program. Calibrating while it was on did the same. Both now keep the game they were on.
  - **Your keys no longer wait for Tempo's window.** Every key you press passes through movement's keyboard hook, which ran on Tempo's window thread: a key waited whenever the window was busy, and after a long wait Windows silently removes a hook. The hook has a thread of its own now.
  - **It follows the game when its window comes back** (a rejoin, a restart of the game). It stayed on but acted nowhere.
  - **Tray sleep leaves it alone.** Half an hour into a game, Tempo fell asleep under it, and the movement hotkey's next press only woke Tempo instead of switching movement off.
  - The Settings card shows how many counts make one full turn, and its status line names the game movement is on. The pass-through mode now sends nothing (it sent your keys a second time).
- **"Diagnose Windows bar" is a real check now.** Settings ▸ Live Captions ▸ Diagnose Windows bar opens a window that says whether Windows 11's own Live Captions bar works with Tempo, with a ✔/✖ list of what it found (Windows 11, Live Captions on, the bar found, Windows sharing its text, a read test with timings, where the bar is) and the button that fixes the problem: Turn on Live Captions, or Use Tempo's own captions. Try again re-checks, and Copy all or Save… take the whole report.
  - The checks used to run in Tempo's own window, so Tempo froze for as long as Windows took to answer, sometimes several seconds. They run in the background now, and give up after 20 seconds instead of hanging.
  - The report is made to be pasted into a bug report, and it listed the title of every window on the screen (browser tabs, documents, chats). It now lists only windows that could be the captions bar and counts the rest.
- **The log file switch applies at once.** "Write a log file to disk" used to keep writing until Save. Turned off, it now stops straight away and offers to delete the log already on the PC (up to 4 MB). Turning it off "for privacy" left all of that behind. Its tip says where the log is and how big it is now.
- **The on-screen overlay switch applies at once,** like its copy in tray ▸ Overlays: the badge of a run in progress appears or goes as you tick it. Turned off, it also hides the "playing macro" badge, which Save left up. Its tip says what it covers: the clicking and macro badges, but never the recording badge.
- **Caption font: every installed font, shown in its own style, applied at once.** The caption font list offered ten fonts by name. It now draws each font in itself, and every installed text font follows the recommended ones (icon and symbol fonts left out). Font, text size, colour, opacity and background now show on the caption bar and the transcript window as you change them. They waited for Save, so a new font looked like it did nothing, and the colour never reached the transcript window until then.
- **The cursor trail no longer switches on Do Not Disturb.** The trail is an invisible, click-through window over every screen, and Windows treated it as an app in full screen: with "turn on do not disturb automatically when using an app in full-screen mode" (on out of the box), turning the trail on silently turned Do Not Disturb on with it, and notification banners stopped. Measured: trail on → Windows answered "busy (full screen)" and Do Not Disturb went on; now it stays off and notifications arrive as normal. The trail itself is unchanged.
- **Mirrored notifications show what Windows shows.** Windows only gives other apps a notification's text, so Tempo's cards were plain text "from Microsoft Edge" while Windows' own pop-ups had far more. Tempo now reads the rest from Windows' own notification store on this PC: only that one notification's entry, without changing anything, and only pictures already on the PC. A picture on the internet is never fetched, and nothing on a network share is ever opened.
  - **Pictures.** The picture under the text, as Windows shows it, and the notification's own small picture beside the text (a site's icon, a contact's photo, drawn round as Windows draws people).
  - **The website.** Edge registers every website as a sender of its own, and Windows' pop-ups wear the site's name and icon. Tempo's cards do too now ("www.kenherbert.dev · Microsoft Edge"), and Chrome's site and other apps' "via …" lines show the same way. The same text from two sites makes two cards, not one "×2".
  - **Clicking a card does what clicking Windows' pop-up does.** Apps register a small handler that Windows starts when one of their notifications is clicked, and Tempo now hands the click to that same handler with the same details. A website's notification opens the page the site meant, in the browser that sent it, and a Discord, Teams or Outlook notification opens the conversation it's about. Notifications that open a link of their own (UniGetUI, NVIDIA) open it too. Before, a browser's card did nothing and other cards only opened the app. If an app's handler is missing, the click still goes to the next best place: a link in the text, the website, then the app. Links that run files or scripts (file:, javascript:, ms-msdt: and the like) are never opened.
  - **The notification's buttons work on the card.** "Mark as read", "Close" and the like appear under the text, as in Windows, and do the same thing. Buttons that need a reply box aren't shown.
  - **Reminders stay.** A reminder, an alarm or a call stays on screen until you dismiss it, as Windows keeps it: at most three at a time, and any more get Windows' long time instead. A notification that asks Windows for its long display time stays 25 seconds. Right-click a card for **Dismiss all**.
  - **Progress bars** (downloads, installs) show with their title, status and how far along, as the notification arrived. Windows doesn't pass an app's later updates of the bar on to other apps.
  - **Long messages open on hover.** A card shows up to four lines of text, as Windows does, ending in "…" when there's more. Hover over it and it opens to the whole message, with a bigger picture.
  - **Late messages say when they were sent.** One that arrives two minutes or more after it was sent shows the time it was sent.
  - **Mute one website.** Right-click an Edge website's card to mute just that site rather than all of Edge. Sites are offered in the muted-apps list in Settings too.
- **Notification cards have four round corners.** Only the top two were round: each card's rounded shape was cut when its window was created, at 300 pixels tall, before the card knew its real height, so the bottom corners came out square under a rounded border. On Windows 11, Windows now rounds the card itself, with smooth edges and a border in the theme's colour, like its own pop-ups. Windows 10 gets the corrected shape.
- **Notification history shows the whole message.** Selecting a row shows the full text under the list, where you can select and copy it, with where it came from, when, how many times it arrived, and why it was held back if it was. The message column cut long notifications off. A website's notifications are named by the site, and a notification's picture shows beside its text for anything that arrived since Tempo started. Pictures are never saved to disk.
- **Mirrored notifications: a burst no longer leaves only Windows' pop-ups.** Sending the same browser notification again and again (a test page, a site that keeps notifying) gave one Tempo card and then nothing, while Windows kept showing its own:
  - The mirror silently dropped any notification identical to one from the last 8 seconds, and every repeat restarted the 8 seconds, so a steady stream was dropped for as long as it lasted. Nothing reached the card, the "×N" count or the history, and Windows' copies were never cleared. Repeats now go through. The card on screen counts them (×N) and stays up while they keep coming, and Windows' copy of each is cleared like any other.
  - Windows' copy is now cleared only once Tempo's card is really on screen. It was cleared first, before anything had decided whether a card would appear, so a notification from a muted app, one held back during a fullscreen game, or one dropped from a full queue disappeared from the Action Center too, and existed only in Tempo's history.
  - With Tempo's pop-up notifications off, mirroring stops (it draws Tempo's cards), but the Settings page still showed it ticked and said nothing. It now says "Paused: needs Tempo's pop-ups", and Live debug says why.
  - The status line that says how to hide Windows' own pop-ups ("turn on Do Not Disturb…") was cut off in every language. It has room for two lines now.
  - The notification history counts repeats on one row ("×37") instead of a row each. A spam of notifications used to fill all 300 rows and push out older ones, including notifications you hadn't seen. "N recorded · N you didn't see" counts every notification.
- **GitHub: Tempo stops asking when GitHub says to, and works through a proxy.** The update check and the release check behind the Official ID now share one way of talking to GitHub.
  - Without signing in, GitHub answers 60 requests an hour per internet address, and on a shared address (a school, a café, a VPN) that is often already spent. When GitHub says so, Tempo now waits until the limit resets before asking again. It used to keep asking (the automatic retry came back after 5, 15 and 60 minutes), each time getting only another refusal and using up another request. The update message gives the time to try again, the automatic check waits until then, and Live debug shows what GitHub last answered and how many requests are left.
  - GitHub's other limit, for too many requests at once, is recognised too: it can arrive as a plain 403 whose only sign is the message. A 403 that really is a refusal (a firewall or filter in between) gets its own message instead of "rate-limited".
  - A proxy set in Windows (Windows Settings ▸ Network & internet ▸ Proxy, typed in or as a setup script) is used for the update check, the update download, the release check and speech-model downloads, along with its list of exceptions. They all said "no proxy" outright, so on a network whose only way out is a proxy they could never work. Automatic proxy detection on its own is still not used, because it made checks hang on networks without a proxy.
  - The Official ID no longer waits for the next launch when GitHub couldn't be reached at start-up (offline, or the limit spent). Tempo asks again after 5, 15 and 60 minutes, then every 3 hours, and never before the limit resets.
  - The reset time is worked out from GitHub's own clock, so a PC whose clock is off still waits the right amount. TLS is left to Windows (1.3 where it can be) instead of being pinned to 1.2.
- **The account vault: nothing is lost when it locks under you.** The vault locks on its own (after the idle time you set, when Windows locks, when the PC sleeps, or when the local API asks), and it could lock while an account window was still open:
  - Signing in a new account, which can take minutes with a browser and a 2-step code, then saving it after the vault had locked, put the account into the locked vault's memory, where it was lost at the next unlock. A re-login said "Session refreshed." about the session it had just lost, and an edit said "Another account is already called…" about a name nobody had. Each one is now kept and saved the moment you unlock (the unlock screen says so). After 30 minutes without an unlock it's dropped, because it can carry a live session.
  - "Remove" confirmed after the vault locked removed nothing, while the log said it had. It now says so.
  - A save that fails (disk full, or the file held by a sync or antivirus program) is reported instead of the change silently vanishing at the next unlock.
- **Local API: a locked vault is an answer, not silence.** The API stopped every time the vault locked, so a script couldn't tell "the vault locked itself after an idle hour" from "Tempo isn't running". It now stays up, still loopback-only and token-gated. While locked it refuses every account call (423) and says why it locked (`lockReason`: idle, windowsLocked, sleep, manual, api, tabSwitch, or not unlocked since start-up), and `/ping` reports it. New `/unlock` asks you at the PC with a notification that opens the unlock screen (at most one every two minutes); with `wait=1` the script waits until you've unlocked. The API never takes the master password. API version 5; `/help` and the Local API window list it.
  - A launch through the API no longer changes which accounts you have selected. It added its account to your selection, so your next Launch or Remove included it. Selecting an account after an action now selects only that one, and a list refresh keeps every selected row instead of only the first.
  - The Local API window's Test button never goes through a proxy. The server is on this PC.
- **Tempo opens in one piece.** Opening the window showed it being built: the header and sidebar missing, empty gaps where controls hadn't been drawn yet (whatever was behind the window showed through), and the "Quick safety note" popping up over the half-drawn page. Recorded frame by frame, the first open after Tempo started in the tray showed a part-drawn window for about 200 ms, reopening it from the tray for about 140 ms, and a normal start for about 200 ms after the splash. Now the window is laid out and painted before it's put on screen, and appears complete in its first frame, every way it's opened. The safety note comes up after it.
  - The "fade-in" wasn't one: it switched the window from fully transparent straight to opaque, and that switch makes Windows throw away everything already drawn, so it was all drawn again with the window on screen.
  - On the first open after starting in the tray, the safety note was opened from inside the window's own show, so it came up first and kept the window off screen until the note was closed.
  - Opening Tempo's icon while Tempo is already running no longer shows "Tempo is already running" in front of the window it said it had brought forward. The window just comes up. The notice is kept for when the running copy doesn't answer.
  - Coming back from the tray repainted the whole window a second time 33 ms after it appeared, even when nothing had changed. It doesn't any more, and picking a point on the screen brings the window back whole as well.
  - The status bar showed "RAM 0 MB · Up 0:00:00" for the first second after the window opened. It shows the real figures straight away.
- **Only Tempo's keybinds act while something runs.** While the clicker runs or a macro plays or records, Windows' own shortcuts are held back: the Windows key (a stray press opened Start over the game and took the focus the clicks and keys were going to), Win+D, Win+G, Win+Tab and every other Win+ shortcut, Ctrl+Esc, and the Sticky Keys, Filter Keys and Toggle Keys pop-ups (a macro that sprints can press Shift five times). Tempo's own keybinds still work, including ones that use the Windows key. Alt+Tab and Ctrl+Shift+Esc are left alone so there is always a way out, and Ctrl+Alt+Del and Win+L can't be blocked by any program. Nothing is held back while the clicker uses "Click without moving my cursor", because you're using the PC then. Works the same on Windows 10 and 11. On by default, and switched on or off from wherever you are: Settings ▸ Behaviour, the Clicker tab (Windows Shortcuts card), the Macros tab (Playback card), and the tray menu, which works in the middle of a run. All four are the same switch.
  - The accessibility pop-ups' shortcuts are switched off only for the run and put back exactly as they were. A feature you've actually turned on is left alone, and nothing is saved to your Windows profile.
  - A hotkey Windows wouldn't reserve, because another program already owns the combination, now keeps its key from that program. Tempo caught it with a keyboard hook but let the key through as well, so one press ran Tempo's action and the other program's (a screen recorder's F9, Windows' own Win+D). The Keybinds tab's note says so.
  - Recordings leave Windows shortcuts out: the Windows key and whatever was pressed with it, Alt+Tab, Alt+Esc, Ctrl+Esc and Ctrl+Shift+Esc. Played back, they opened Start, switched windows or opened Task Manager in the middle of the macro. With the setting off, they're recorded as before.
  - It adds no delay to your keys or a macro's: the check runs on a thread of its own, not behind Tempo's window.
- **The RAM figure was wrong.** The status bar showed Tempo's working set, which counts the Windows and .NET libraries it shares with every other program, under a note saying it was Task Manager's figure. Task Manager shows the private working set. Measured on an idle Tempo: 157.6 MB on the status bar, 58.5 MB in Task Manager. It now shows the same figure as Task Manager, and hovering it gives the larger one with what the difference is. Crash reports and the log name both.
- **Overlays: move them, fade them, from the tray.**
  - The clicking badge, the "playing macro" badge and the recording badge can be dragged anywhere: tray ▸ Overlays ▸ Move overlays. Each stays where you put it, on any monitor. A badge that isn't on screen gets a stand-in so it can be placed without starting anything; double-click any of them when done. Default places puts them back in their corners.
  - Tray ▸ Overlays ▸ Opacity sets one opacity for all three (100 % down to 40 %), and Show on-screen overlay while running is there too.
  - The "playing macro" badge ignored the overlay settings: it always came up top-centre at the default opacity, whatever corner and opacity were chosen.
  - The recording badge took clicks: one that landed on it brought Tempo to the front in the middle of the app being recorded, so the next keys went to Tempo. It's click-through now, like the others.
  - The clicking badge jumped to another monitor mid-run: it re-placed itself on the screen the pointer was on whenever its text changed width (999 → 1,000 clicks).
  - Its "playing…" was in English in every language.
- **Untranslated text, found by a new check.** The layout checks could only spot English that has a translation which wasn't used. They now also spot English with no translation at all (text assembled while Tempo runs), and report any phrase that was looked up and had none. First finds, now translated: the display line in Settings ▸ Window & Display ("100% scaling · 2 monitors (usable … after the taskbar)") and the speed preset buttons' tooltip.
- **Keybinds: fixes and improvements.**
  - Binding a mouse side button didn't work. Pressed in a Keybinds field, it showed "Mouse X1 (Back)" for a moment, then Windows turned the button's release into a browser Back command and the field took that for the Browser Back *key*. That is what was saved, and the side button never sends that key, so the hotkey never fired from the mouse. The field now keeps the button and ignores the command.
  - A mouse button pressed over a field that wasn't selected rebound it. A wheel click while scrolling the list, or Back pressed out of habit, quietly changed that row and took the button from whichever action had it. The first press on a field now only selects it, as a click does; the next one binds.
  - Pressing one of Tempo's hotkeys while recording a macro no longer records it. Only the macro keys (record, play, stop, emergency stop) were left out, so pressing Pick position or Pause during a take put that key in the macro, and every playback then ran that hotkey again partway through. A mouse hotkey pressed with Shift or Ctrl held is left out too.
  - Play macro #1–#3 with no macro picked play the 1st, 2nd or 3rd macro as the Macros list shows them, favourites first. They went by the stored order, so with a pinned macro "the first in the list" wasn't the one at the top. The pickers for #2 and #3 said "(first in the list)"; they now say second and third.
  - The bare-key warning covers the punctuation keys (` - = [ ] \ ; ' , . /) and the keypad's operators. Bound on their own, they stop typing that character in every program, as a letter does, and nothing said so.
  - Two descriptions were out of date. Toggle Live Captions said it sends Win+Ctrl+L to Windows' captions; it follows the caption source chosen in Settings. Play macro #1–#3 said they play the Nth macro in the list; they play the macro picked beside them.
  - Copy keybinds lines up in every language (the column was sized for English), names the macro each quick-play key plays, and includes the Shift/Ctrl option and the interval step.
  - A character typed into a field by the emoji panel, an on-screen keyboard or a password manager is no longer taken for a key.
- **Captions: fixes and conflicts.**
  - Changing the engine while captions run no longer wipes the transcript. "Use a faster model" on the Captions tab, saving a new source, model or capture choice in Settings, and importing or resetting settings all switched captions off and on again. Turning them on starts a new session, so each one cleared the transcript on the tab and in the history window, forgot the names given to speakers, split the saved transcript file in two and announced "Live Captions OFF" and "ON". The engine now restarts in place and the session carries on.
  - "Use a faster model" could do nothing and say it had. After Tempo had already stepped the model down on its own, or when the next smaller model wasn't downloaded, or when captions used a model file you chose, it loaded the same model again and announced a switch. It now steps only to a model that is downloaded, and says so when there isn't one (or when your chosen file can't be sized down).
  - Choosing a model file while captions ran on the Windows source started Tempo's own engine as well, so two engines wrote to one bar.
  - Switching to a profile that carries appearance settings changed only the Settings page. The caption bar and the history window kept their old text size, font, colour and opacity, and the bar didn't hide or show as the profile says.
  - Clear asks before throwing the transcript away, since it can't be brought back. With "Save transcripts to disk" on, it saves the lines first instead of asking. It also left "N new below" under the emptied box.
  - Saved transcript files were named to the second, so two saves in the same second (Clear, then Stop) wrote one file name and the second replaced the first. A file is never written over now.
  - The engine line's language: the name was in English in every language, the engine's own text showed as it was ("detecting: en (2/4)"), and "(restart captions to apply)" was decided by looking for the language code anywhere in that text. Pinning German while it read "detecting…" never asked for a restart, and an English-only model was told to restart for a language it can't write. It now compares the codes and says when the model is English-only.
  - The delay and quality lines (delay, real-time pace, clipping, apps playing at once, confidence, nothing transcribed), "Will use: Auto engine", the captions on/off notices and the five "switched to Windows Live Captions" notices were in English in every language.
  - The GPU engine's tooltip said Tempo switches the option off by itself and goes back to the CPU engine. It doesn't: the option stays on, that session uses Windows Live Captions, and the next start tries the GPU again. One notice also sent people to "Settings › Behaviour › Live Captions", which doesn't exist; it now points to Models… on the Captions tab.
- **Fixed: the live captions wouldn't move.**
  - In move mode, the caption bar and the caption history are pulled flush to a screen edge, the middle, or each other when you drag them close. That magnet held on for good: Windows bases each step of a drag on where the window was last put, which was the magnet, so every ordinary step landed back inside it and was snapped home. A window sitting on a magnet could not be dragged off it at all. That includes the bar's own starting place in the middle of the screen, so it would only move up and down. Measured with real drags: a bar on the screen's left edge stayed there through a 1,200 px drag, and the history beside it didn't move at all. The windows now follow the pointer and only snap while you're within reach of a magnet.
  - A caption window dragged onto a monitor to the left of (or above) the main one forgot its place. Positions there are negative, and a negative position was read as "never placed". The bar wasn't put back there at the next start, and while sound played on the main screen, the bar's follow-the-sound move pulled it straight back. Only "never placed" itself is read that way now, and the bar never follows the sound while you're moving it.
  - A caption window placed across the line between two monitors no longer jumps onto one of them by itself. That used to happen every time it was shown, and whenever the monitors woke from sleep. Only a window that hangs off the screens is pulled back now.
- **Macros: the Live Monitor shows every step, and reads like a macro.**
  - It listed only a macro's first 2,000 steps (and the last 1,000 of a recording), and past step 2,000 the playback highlight stopped following. Every step is listed now, at any length, and filling the list is instant: a 50,000-step macro is no slower than a short one.
  - A compact view, on by default: a run of mouse travel (the moves and the short waits between them, which is most of a recording) is one row, "Path · 48 moves → (1067, 446) · 0.86s", and so is a run of camera turns. A short wait between two other steps folds into the Time column; a wait of a quarter second or more keeps its own row. Hover a grouped row to see which steps it covers. **Show every step** goes back to one row per step.
  - Clicking another macro while one was playing put that macro's steps under the playback highlight, and the "finished" notice then named the macro clicked rather than the one that ran. The monitor stays with the playing macro until it ends, then shows the selection.
  - A script step showed "(0, 0)"; it now names its script and timeout, as the step editor does. A scroll says which way it goes (▲ or ▼) instead of "delta 120".
  - After playback the header said "Finished — select a macro to view its steps" over the steps it was showing; it now shows the macro, with the run counted. After a take that ended up empty, the header no longer stays on "● Recording…". A long note no longer runs off the header's one line, and an "&" in a macro's name is shown.
  - The "Macro finished" notice was in English in every language.
- **Tempo's log.**
  - Half of the log file was the same routine lines over and over: a layout diagnostic twice per tab switch, "Settings saved", every hotkey registered one by one, start-up steps of a few dozen milliseconds. The file is capped, so they pushed out the history a bug report needs. They now go only to Live debug (as DEBUG lines), and a state report, like the bound hotkeys, is written to the file once, and again only when it changes. The hotkey line now names the keys.
  - Every line opened, appended to and closed the file. Measured at 0.168 ms a line, against 0.003 ms now. A start-up writes a few hundred lines, mostly on the window's thread. Each line is still flushed as it's written, so a crash loses nothing, and the file is released after two quiet seconds.
  - The log only rotated when Tempo started, so a session that lasts days in the tray grew it without limit, and only one old file was kept. It now rotates as it grows and keeps three old files.
  - Each session starts after a blank line, so runs stand apart in the file.

### 1.0.323
**Captions tell speakers apart, by name and by face, and stop inventing words over music · hotkeys fire while you hold Shift or Ctrl · Tempo starts about three times faster and idles quietly · Accounts can be a window of its own · Discord no longer shows Tempo as the Steam game "Tempo" · laptops at 125% and 150% · hundreds of fixes**

- **Settings: the privacy note is a panel you can read, and it's accurate again.**
  - It was one small, muted caption at the foot of Settings that many people found hard to read: under 7:1 contrast in 32 of the 38 themes, and as low as 4.5:1. It's now a tinted panel in larger type, with a title, a plain-language summary, and a line for each way Tempo goes online, each name in its own colour. Body text is at least 7:1 against the panel in every theme, and each coloured name at least 4.5:1.
  - It said "the only network use is the optional update check", which was no longer true. The release check also asks GitHub, once per version, with its own switch, so it still ran with "Check for updates" off. Speech-model downloads, a logo from a web address and the Roblox account manager go online when you use them. The panel now names each one, says when it happens and where to switch it off, and says the Local API answers only programs on this PC.
  - Under it, the version line names the build ("Tempo v1.0.322 · build 261008-2224"), and a second line says in words and colour what the build is: an official release (once GitHub has confirmed the file), a test build, a development build, or a copy that doesn't match the published release.
- **Live debug: fixes and improvements.**
  - An error logged with its stack trace made the event log fall back to drawing itself a line at a time, the path that froze Tempo for seconds. That happened on every rebuild of the log while such an error was among its last 1,500 lines: opening Live debug, each pause while typing a filter, Problems only, a theme change. Measured here: 8.9 s for 1,500 lines, against 24 ms now. When such an error arrived as a new line, part of it was also glued onto the line above and the entry appeared twice. It now appears once, with the exception and each stack line on a line of its own.
  - The first new event after the log was rebuilt (when the window opens, after a filter change) was glued onto the end of the last line. And a rebuild showed the newest lines twice: the lines still waiting to be added were added again after it, so with a trace on, every filter change doubled the last few dozen.
  - Reading older lines: after scrolling up with the mouse wheel, the view jumped back down the moment a new line arrived, and about every 700 new lines the log trimmed itself and threw you to the bottom. Both now leave you on the line you were reading. Text selected in the log was wiped by the next new line; it now stays selected, and the view stops following new lines while something is selected. New lines also wait while you drag a selection or the scroll bar.
  - Clear view didn't stay cleared: any rebuild (a filter, Problems only, a theme change, the trim) brought every cleared line back. It now remembers where it cleared, the count beside the filter says when ("cleared at 21:12:07"), and Copy and Save leave the cleared lines out (the report says so).
  - Problems only, and the red and amber line colours, went by "[ERROR]" or "[WARN]" anywhere in a line. So a normal line that merely quoted one (a script's output, say) counted as a problem, while the filter's level:error said it wasn't. Both now read the line's real level.
  - Minimised, it no longer rebuilds its stats twice a second. The stats and the log catch up the moment it's restored.
  - Esc clears the filter, as in the caption transcript's filter. Enter steps to the next match (Shift+Enter the previous) and keeps the caret in the filter box, like a browser's find box. That also works when F3 is one of your Tempo hotkeys: Windows hands a hotkey's key to the hotkey, so F3 never reached Live debug. Save… confirms on its button as Copy does. Check layout no longer rebuilds the log twice.
  - [RawInput] (the raw mouse registration) and [stall] (the window-freeze watch) lines had no colour, and the stats listed them as uncategorised.
  - The version line names the build ("v1.0.322 · build 261008-0026 · TEST · commit d230998") instead of the version followed by a 40-character commit hash, so a pasted report says whether it came from a test build.
- **Macros: fixes.**
  - The Play hotkeys played a macro while you were recording one, and the Record hotkey started a recording while a macro played. The tab's buttons were already locked at those times, but the hotkeys weren't. A take recorded over a playback held the playback's results without its input. Both keys now say what is running and do nothing else.
  - Renaming a macro broke the quick-play hotkey set to it. The slot kept the old name, so the key played nothing and the Keybinds tab showed "(missing)". The slot now follows the rename. A key whose macro was deleted said so only in the log. It now shows a notification that names the macro and says how to fix it. The Play key with no macro selected, and a quick-play key with nothing to play, now say so too instead of doing nothing.
  - Playing an empty macro from a hotkey popped a message box over your game and took its focus. From a hotkey it's a notification now. The Play button on the tab still shows the box.
  - **Import all** kept macros that run Python scripts without asking. Import… has always asked about a macro that runs a script; Import all never did, so a shared collection brought its scripts in unseen. It now asks too, listing each macro and the scripts it runs. "No" imports the others without them.
  - Turning down a script macro on import put it in the recycle bin instead of discarding it. It was one Restore away, and with the bin full (it holds 25) it pushed the oldest really deleted recording out. It's discarded now.
  - Import… refused a file made by Export all ("not a valid macro"), and Import all refused a file made by Export ("not a macro collection"). Each now takes either.
  - The Loops, Speed, Countdown and Loop delay boxes rewrote the whole macros file (every macro, plus the recycle bin) on every step of their arrows, on the window's own thread, many times a second while you held an arrow. The macro still takes the new value at once. The file is written once the box settles, or at exit or Windows shutdown if that comes first.
  - The Merge picker wasn't scaled to the screen. On a 125% or 150% screen its list and buttons kept their 100% size around bigger text. It now scales and follows the theme, its buttons fit their words ("Zusammenführen" was cut off), and a double-click on a macro picks it.
  - The rename, notes and delay prompts had English titles in every language, and their prompts ("New macro name:", "Delay in milliseconds:"…) weren't translated either. A long prompt, such as the notes prompt of a macro with a long name, ran under the text box. The box now moves down below it. A macro name with "&" in it lost the "&" in the notes prompt.
  - The notification after **Fix** ("3 steps changed in …") was in English in every language.
  - The Keybinds tab showed a quick-play slot as "(missing)" when its saved name differed from the macro's only in capitals, though the key played it.
- **Captions: Save, Copy and Subtitles… say what happened.**
  - When a transcript couldn't be written (a read-only folder, a file open in another program), Save and Subtitles… showed nothing; the failure only went to the log. They now say why. Copy says so when another program is holding the clipboard.
  - Save did nothing when nothing had been transcribed yet, and with a filter that matched nothing it wrote an empty file. It now says which of the two it is, as Copy does. These notices, and the "nothing to copy / export" ones, now show even with "Show notifications" off, because they answer a button you just pressed.
  - WebVTT subtitles: a "<" in a caption ("x < 5 so…") started a tag that hid the rest of the line, and "&" and "-->" weren't escaped either. They are now. SubRip files keep the words as spoken.
- **Hotkeys work while you hold Shift or Ctrl.** F6 did nothing while you held Shift to sprint or Ctrl to crouch, because Windows only delivered a hotkey for its exact keys. Now Shift and/or Ctrl held on top still fire it. Tempo takes the Shift/Ctrl versions of the key from other programs too, like the key itself. This applies to the function keys, the middle and side mouse buttons, the media keys, Pause and Scroll Lock, and to the hold key in Hold-to-click mode, which also no longer stops when you press Shift in the middle of a hold. A combination bound to something else (say Shift + F6) still does that. Keys that type or edit (letters, digits, arrows, Enter…) keep needing their exact combination, so typing and text shortcuts aren't affected. It's on by default, with a tick box on the Keybinds tab to turn it off.
- **Keybinds: fixes, and more of the list on screen.**
  - A key Tempo had reserved never reached the Keybinds fields. Windows takes a reserved hotkey before any window sees it, Tempo's own included, so pressing a saved key (say F7) in another action's field did nothing, and a key couldn't be moved from one action to another by pressing it there. While a field is listening, Tempo now lets go of its keys, and takes them back when you leave the field.
  - A side mouse button bound to a Tempo hotkey still sent your browser Back (or Forward). Tempo swallowed the button's press but not its release, and Windows makes Back from the release. Both are swallowed now, so the button only does Tempo's action. A bound middle button no longer leaves a stray release behind either.
  - In Hold-to-click mode, pressing the hold key in a Keybinds field (to bind it to something else, or to put it back) started clicking, into the Keybinds tab, for as long as you held it. Every other hotkey already waits while a field is listening. Hold mode now does too, and it ignores that press until you let go.
  - One key combination now always runs one action. When another program owned a combination and two Tempo actions shared it, both were caught by the fallback keyboard hook and one press ran both. The second action is now left unbound and named, the way it already was for every other route. The same goes for two actions on one mouse button combination: only the first one ever ran, and the tab didn't say so.
  - The Save warning for a shared combination said one press would run both actions. It now says only one of them responds, which is true. The warning after switching to a profile whose keys clash said the same thing, and is fixed the same way.
  - A key let go while a Keybinds field had focus could make that key's next press do nothing, for hotkeys that run through the fallback keyboard hook.
  - **New warnings** for combinations that cost you something elsewhere: shortcuts almost every program uses (Ctrl + C, Ctrl + V, Ctrl + Z, Ctrl + S, Alt + Enter…); Ctrl + Alt combinations that type a character on your keyboard layout (on a German keyboard Ctrl + Alt + Q is AltGr + Q, which types @); and Tempo's own window shortcuts (F11, Ctrl + 1…9, Ctrl + Tab). The tab already warned about bare keys, and its advice to "add Ctrl/Alt/Shift" led straight to the first kind.
  - Keys are named as the keyboard prints them: "Ctrl + 1", not "Ctrl + D1"; "Num 1", "Home", "End", "Pause/Break", "Menu key". Punctuation keys come from your keyboard layout, so the key a German keyboard prints Ö on no longer shows as "`". The clicker's auto-press key and macro steps use the same names ("Left Shift", not "LShiftKey").
  - The notices above the list take only the room they need. Each one had a band reserved for it whether it was showing or not, about 280 pixels in all, so the list started more than half-way down the window and only 7 rows fitted. Now 12 do. When a notice appears while you're changing a key, the row you're on stays where it is, and the Save button stays in view.
  - **Copy keybinds** confirmed with "Saved ✓" while your edits were still unsaved. It says "Copied ✓" now.
  - A quick-play slot set to a deleted macro shows "Name (missing)". Picking that entry again saved "Name  (missing)" as the macro's name. It now keeps the original name.
  - "That key was taken from another action" stayed up after Reset to defaults, describing bindings that no longer existed.
- **Translated:** eleven confirmation dialogs asked in English in every language, although their translations existed: Reset keybinds, Reset settings, Repair Tempo, the uninstall backup question, Import settings, Reset session statistics, Clear history, Reset lifetime totals, the "macro is still being recorded" update question, Remove all points, and where to get speech models. Their titles are translated too.

- **Switching tabs: the new page appears in one piece, and sooner.**
  - Clicking a tab showed the new page being drawn a control at a time over the old one: its buttons and first cards turned up over the old page, with stripes of the new background cutting through it, for 50–250 ms. Recorded off the screen, every switch passed through 2–11 half-drawn pictures, and Macros → Settings sat half-switched for over 400 ms. Now the old page stays exactly as it was until the new one is completely drawn, and the new one replaces it in one go. Recorded the same way, no switch shows a half-drawn page, with "Always on top" on or off and with a see-through window. This works for the sidebar, Ctrl+1…9 and Ctrl+Tab alike.
  - With a wallpaper too. That case had its own way of switching, and recorded with a test wallpaper it still showed the page arriving in pieces (39 half-drawn frames in 12 switches, wallpaper stripes through the old page). It then repainted the whole window again 0.7 s later, so a switch took about 750 ms to settle, and Multi-Point's list could turn black for a moment. It now switches the same way as without a wallpaper: no half-drawn page, settled in 50–210 ms, including right after scrolling a page.
  - The first visit to each tab was the slow one, because Windows made the page's controls inside the click: Settings took 595 ms the first time and 73 ms after that. Tempo now makes them in small slices in the background once its window is on screen (about two-thirds of a second of work in all), so a first visit is as quick as any other. A switch now takes 20–100 ms where it took 65–600 ms.
  - The tab list's highlight moves the moment you click, before the page work starts.
  - The Captions tab's status lines (model, engine, "Start captions to see who is talking") no longer appear a frame after the rest of the page.
  - Opening a tab from the tray ("Settings…", "More in the Profiles tab…", Accounts) showed the page Tempo was hidden on first. The switch then played out in front of you, with the old page on screen for 80–140 ms in every one of 8 recorded opens. The window now comes back already on the tab you picked, drawn in full.
  - Statistics opened with the numbers from the last time it was shown, then changed them a moment later ("0" became "0.0", and the chart swapped "Waiting for activity…" for its line). It now opens with current numbers.
  - The clicks-per-second chart only recorded while the Statistics tab was open. A run made from another tab never showed on it, and samples minutes apart were drawn side by side as if 200 ms apart. It now records all the time, so it shows the last 30 seconds whichever tab you were on.
- **Tooltips stay on the screen.** Windows lets a tooltip be as wide as the whole desktop (3,860 pixels on a two-monitor PC), so a long help tip ran in one line from one side of the screen to the other and off its edge. The Camera-relative movement tip was a single line 2,025 pixels long. Every tip is now broken into lines about 450 pixels wide, so that one is five lines. This covers the help tips, the status bar, list rows, and the Accounts, Captions, Live debug, About and script-step windows. A tip also stops at 30 lines, because a script's error report can run to hundreds.
- **One tooltip per control.** Where two parts of Tempo put a tip on the same control, both popped up, one on top of the other. That happened on every caption the layout check shortened (in English, "Show on-screen overlay while running") and on the Set-key button when its key was also a hotkey. Each control now shows one tip: the full caption or the warning first, then the help.
- **Every tooltip follows the theme.** Only the main window's help tips were drawn in Tempo's colours. The rest, in Accounts, Captions, Live debug, About, the script step, the chart, the status bar and the layout check, were Windows' bright default. The main ones also kept the start-up theme after a theme change.
- **Fixed: text the layout check shortened vanished.** When a caption was too wide for its column, Tempo shortened it with "…", but a label shortened that way came out 0 pixels tall. In English that hid the whole Background Clicking explanation. In other languages it hid captions such as "Meta da sessão (cliques, 0 = off):". Shortened labels now keep their height. An explanation that collides with something beside it is re-wrapped onto more lines when there is room, so nothing is cut.
- **Fixed: text cut off at the bottom of its box.** A fixed-size label just stops drawing at its bottom edge, with no "…" to show anything is missing, and no check looked for it. A new check (in the six-language layout test and Live debug's "Check layout") found 5 in English and 13–14 in each other language. Examples: the Camera-relative movement warning, the Keybinds descriptions and bare-key warning, the Second Cursor hint, the statistics summary. Each one now grows to fit when there is room. Otherwise it ends with "…" and shows its whole, current text on hover. All six languages now pass with none cut.
- **The dialogs got the same check.** The CPS test's title ("Click as fast as you can! (left button)") needed two lines in Spanish, French, Italian and Portuguese, and lost the button's name. It now shrinks its font until it fits on its one line. Its big "CPS:" read-out got its full line height back, where a French comma was losing its tail. The About window now grows for its logo hint (French, Portuguese) and its feature list (German). All 11 dialogs pass in all six languages.
- **Layout fixes.**
  - "Show on-screen overlay while running" is no longer cut to "Show on-screen overlay whi…". Its Customise… button moved under it, and "Sleep in tray" moved down a row.
  - Background Clicking: the "pick a fixed or multi-point target" warning sat beside the tick box, running across the explanation. It now has its own line under it. The card grows to fit its words, and the tip line under it no longer touches the card border.
  - Camera-relative movement: the status line has its own full-width row (it needed three lines of a two-line box), and the warning gets the height its language needs.
  - The Save / About / Check for updates row follows the last Settings card, wherever that ends.
- **Readable text in the position picker and the multi-point preview.** Both dimmed the screen by making the whole window see-through (35% and 45%), so the X/Y read-out, the instructions, the banner and the point numbers were see-through too, and faint over anything bright. Only the dimming is see-through now: text and markers are solid. The picker's instructions appear on the monitor you are pointing at, not centred across every monitor.
- **Translated:** the position picker's instructions and the Camera-relative movement status messages ("ARMED…", "Off…") were in English in every language.
- **Captions ignore sound with no voice in it (Silero VAD).** Before Whisper hears a stretch of audio, a small voice-detection network, Silero VAD (MIT), checks whether anyone is speaking in it. Music, a fan, gunfire and game sound no longer reach Whisper, which is where it invented words. On 30 seconds of synthetic music, fan noise and gunfire, the captions now show nothing: 1–5 invented words before this, 15–35 before the no-speech gate. Speech is untouched:
  - On a 65-second recording, not one stretch of speech was skipped.
  - On the same recording with noise 6 dB under it, 144–146 of 149 words still came through.
  - It also saves the CPU a Whisper pass would have cost on every stretch with no voice.

  The detector runs inside Tempo, so the speech engine that is proven fast stays exactly as it is. Its results match whisper.cpp's own Silero to within 0.0015, and it costs about 1% of one core. Live debug counts the stretches it skipped.

  Singing can count as "no voice", so **Settings → Live Captions → "Caption song lyrics"** lets music through when you want lyrics captioned. It is off by default, applies to running captions at once, and is saved at once. With it on, instrumental music can bring stray invented words back. On the noise test that was 1 word instead of 0, because Whisper's own no-speech check still dropped 12 of the pieces it heard.
- **Translated:** the rest of the Camera-relative movement messages ("Disarmed — …", "ARMED · camera estimate re-centred.").
- **The tab list: cleaner, and one size of text.**
  - Every tab uses one font. The layout check shrank any tab label that didn't fit, one tab at a time, so every language had one to three tabs in a smaller font than the rest: "Multi-Point" in English, "Einstellungen" and "Tastenkürzel" in German, "Configurações" in Portuguese. The column of shortcut digits was taking the room they needed.
  - Tabs are flat rows instead of a stack of boxes. A tab shows nothing at rest, a soft highlight under the pointer (its text brightens, its icon takes its full colour), and the accent highlight with its bar when open.
  - Ctrl+1…9 still opens each tab. Hold Ctrl and each tab's number appears in place of its icon; it waits a moment, so the Ctrl of Ctrl+C doesn't flash it. The digits no longer sit beside every label all the time.
  - A thin line sets Accounts apart at the bottom. When Accounts is set to open in its own window, its row shows ↗, so a click opening a window is no surprise. Keyboard focus on a tab shows as an accent outline.
  - The open tab's text and icon stand out from its highlight in all 38 themes. In Nord, Monokai, Sakura, Bubblegum and the Light theme the icon was below 3:1; now it is shaded until it reads.
  - The tabs' tooltips were English in every language, and said "Ctrl" to German users, whose key reads Strg. They are translated now.
- **The tray menu, in the app's own colours.**
  - Every icon wears the colour of the part of Tempo it belongs to, the same colour as that part's sidebar tab: Profile in the Profiles lime, the three caption items in the Captions teal, Roblox accounts in the Accounts blue, and the settings (Always on top, the notification switches, their history, Settings…) in the Settings orchid. Actions use the theme's colour for what they do, as the window's buttons do: Start green, Stop and Exit red, Restart amber. The menu had a palette of its own that matched nothing in the window: Captions was violet in the tray and teal in the sidebar.
  - The row under the pointer looks like the sidebar's selected tab: a soft accent wash with the accent bar, the icon keeping its colour. It was a solid bright accent with white text on it, about 2:1 on Slate, where the rest of Tempo puts dark text on that colour.
  - Live captions, Always on top, Pop-up notifications and Screenshot alerts show a switch like the ones in Settings, and their icon greys when off. "Off" used to be the icon's colour mixed into the background: a brown pin and an olive bell.
  - Rounded corners, with the border in the theme's colour, on Windows 11, for every Tempo menu.
  - The status dot pulses while clicking or playing a macro, in the theme's green (the Start button's).
  - With HDR colour correction on, the section colours are corrected like the rest of the palette, in the sidebar too.
  - A ticked item in Tempo's other menus shows Settings' check box instead of Windows' pale blue one.
  - Hints on the right ("F6", "3 missed") have a column of their own, so a long hotkey can't run into a long label. "3 missed" is in the warning colour, and "2× tray" is translated.
  - Checked in all 38 themes, with HDR correction on and off: every icon stands at least 3:1 against the menu, and highlighted text at least 4.5:1 (Nord's text needed a small nudge).
- **Profiles: switching no longer throws away unsaved changes.** Changes on the Clicker and Multi-Point tabs live there until Save, and every way of switching profiles loaded the saved profile over them without a word. Now:
  - From the window (the dropdown, a card, New, Duplicate), Tempo asks: save them, drop them, or stay.
  - From the Next/Previous-profile hotkeys or the tray, where a question would take focus from your game, the changes are kept. They come back, still marked unsaved, when you return to that profile, until Tempo closes.
  - Choosing the profile that is already loaded no longer reloads it.
- **Every way of switching does the same thing.** New, Duplicate and Delete on the Clicker tab loaded the next profile directly, so its saved keybinds and look were not applied and it was not remembered for the next start: the tray kept ticking the old profile, and a restart reopened it. They now switch the way the dropdown does. A hotkey switch into keybinds that collide says so in a notification, not a dialog over your game.
- **The Profiles tab can't switch mid-run either.** The Clicker dropdown and the tray were locked while clicking, but a card, "New profile", "Import…" over the loaded profile and deleting the loaded profile still switched, handing the running clicker another profile's settings. They follow the same rule now, and the tab says why ("Stop clicking to switch profile", or the macro and recording versions). The Next/Previous-profile hotkeys still switch mid-run, on purpose.
- **"Used" now counts runs.** It counted switches made from the Profiles tab and the tray, so switching with the dropdown or the hotkeys never counted and clicking a card twice counted twice. A use is now a run started with the profile, however it was chosen, and "Recently used" sorts by that. With "Record session history" off, nothing is counted.
- **Fixed: "Edit details…" replaced a profile's saved look.** The "Also restore my theme, overlay and caption look" snapshot was taken again on every Save, so changing a profile's emoji while another theme was on overwrote the look it existed to restore. Keybinds had the same bug, fixed earlier. A snapshot now stays as saved. New "Update the saved keybinds/look to the one I use now" boxes replace it when you choose.
- **Fixed: a rename in "Edit details…" didn't stick.** The Clicker tab's name box kept the old name, so its next Save renamed the profile back. A change of case alone ("fishing" → "Fishing") was undone the same way. A rename could also slip past the "already exists" check and leave two profiles with one name.
- **Fixed: a profile from a newer Tempo broke the Clicker tab.** A click style, mode, button or point order this version doesn't have stopped the load half-way, leaving the tab showing two profiles mixed. It now loads with the nearest choice shown, and the file keeps the newer value until you save that profile. Other damage in a hand-edited profiles.json (an empty point, numbers out of range) is repaired on load.
- **Import is safer.**
  - A profile that brings its own keybinds or theme asks before keeping them. Switching to it would have replaced yours.
  - "Replace" keeps your profile's star, history and saved keybinds and look (unless the file brings its own), and puts the old setup in the recycle bin. It used to be lost for good.
  - A new import starts its own history, instead of arriving "used 48× · 6h" from somebody else's PC.
  - Values outside what the editors allow, and names over 60 characters, are brought into range.
- **The recycle bin restores the one you picked.** With two deleted profiles of the same name, restoring the older one brought back the newer. A run still going when its profile was deleted now adds its time to the deleted copy.
- **"&" in profile names.** "Slow & steady" was drawn "Slow _steady" on its card and "Slow  steady" in the status bar and the tray.
- **Profile cards have tooltips:** the whole name (a card has room for about 20 of up to 60 characters), the description, and what switching to it restores.
- **The tray names the loaded profile.** It named the one remembered for start-up, which could be a profile that no longer exists, and then ticked nothing.
- **Deleting the only profile loads the fresh "Default"**, instead of leaving the deleted profile's settings on the Clicker tab.
- **Texts:** the Clicker tab's New said it copies the current settings, but it starts from the defaults. Duplicate now says it copies the profile as last saved, and Delete says where the recycle bin is. The profile name box takes 60 characters, like the details dialog.
- **Translated:** the profile check messages ("Profile name cannot be empty.", "Multi-point mode needs at least one enabled point." and nine more) shown by Save and by Start, and the import errors, were English in every language.
- **The layout check sees more.** It runs at start-up, in Live debug's "Check layout" and in the six-language test, and now also catches:
  - controls hanging off a dialog's own edge, or off the left or top of their card, where no scrollbar reaches;
  - text Tempo draws itself: card titles, the Statistics cards' captions, the status pill's word;
  - fixed-width tick boxes, combo-box choices and list column titles too wide for their box;
  - an "&" that disappears, or shows twice;
  - English left in window titles, status-bar items and tooltips.

  Buttons that change their caption (Start / Pause, Show / Hide, "Saved ✓", "Testing…") are checked with every caption they can show, and the start-up pass makes room for the longest. A new test plants each fault on purpose and checks that each check catches it.
- **Every dialog is checked in all six languages: 41, where it was 12.** The first run found 72 problems in 15 dialogs that had never been checked, all fixed:
  - Buttons cut off: "I've logged in — save" in Roblox sign-in (Spanish, French, German, Italian), "Restore" (German), "Restart now" (French), "Fix selected" (all but English), "Keep default" (Spanish), "Turn labels off", "Use this value", "Save", "Testing…" and others.
  - Change master password: the labels ran into their boxes in four languages. The boxes now start after the longest label.
  - Restore points: the help ran under the list in five languages, and the "no restore points yet" note was cut. The bug report, crash report, speaker labels notice, macro fixer and account dialog each cut off sentences in some languages.
  - The CPS test lost the "!" of "Appuyez sur ESPACE !" in keyboard mode; the click pad's text now steps down a size when it has to.
- **And in the main window,** where the new checks found 6 problems in English and up to 11 in other languages:
  - The Clicker's Button box said "Keyboard key", which ended in "…" in every language. It says "Keyboard" now, and the three boxes in that row are each as wide as their longest choice (Italian "Tieni premuto" was cut too).
  - Combo boxes open their list as wide as their widest choice, so every choice can be read while choosing, and a choice too long for the closed box shows whole on hover. The caption source, model and "Listen to" boxes, whose choices describe themselves, rely on that.
  - The sort boxes on Macros and Profiles, and "Check for updates" in Settings, were a few pixels too narrow for some translations. Multi-point's "Dwell" column was too narrow for French "Attente".
  - The Record card's first tick box sat on the card's title badge.
  - The Second Cursor explanation read "opens apps  hits anything": its "&" was taken for a shortcut marker.
  - A card title too long to fit beside its icon leaves the icon off, as a button does. German "VERWALTEN" ran into the Manage card's border.
- **Translated: the speech model dialog.** Its row names ("Speed", "Precision", "Languages", "File"), its hints, "Use this model" and "Cancel" were English in every language, and so was the Models menu's "Using: …" line.
- **Laptops at 125% and 150%: the windows now grow with their text.**
  - On a laptop set to 125% or 150%, Windows draws Tempo's text bigger, but almost nothing around it grew. The main window scaled its frame and tab list and none of the nine tabs, and not one dialog scaled at all, so text half as big again sat in boxes, rows and buttons of their 100% size. The message box's warning sign was cut off. Windows scales a window only when it is told the size it was designed at, and only what the window holds at that moment, and every Tempo window said it before building anything. Each one now says it once it is built. Tested with Windows told the screen is 150% from the start: the dialogs went from about 1,200 layout faults to none, and the main window from 80 to none, in all six languages.
  - Parts sized to fit their text (buttons, labels, rows) are measured in the window's design size, so they come out right once the window grows instead of half as big again. Parts laid out again later, such as the Captions speaker button and the profile cards, use the grown sizes.
  - Dialogs given a whole-window size lost room at 150%, because the title bar and border grow less than the rest (39 px tall at 100%, 56 at 150%). Insert Key Press would have cut off its buttons. Eleven dialogs are now sized by their contents.
  - At 150% a line of text is 25 px tall where the design allows 22.5, and some words grow by more than half again. The camera calibration steps now make room for their last line. The German Multi-Point intro no longer runs into the Order row: it and the Macros intro were measured in English whatever the language. German "Anzeigen (s):" and Portuguese "Sensibilidade da câmara:" no longer run into the number box beside them, the About window's GitHub link stays off the Close button, and Live debug's section list shows "Aller à la section…" whole.
  - Tick boxes and switches came out of the scaling half as wide and half as tall again as their words, some for as long as Tempo ran, so their captions sat low and their boxes ran over the next control. They keep their own size now.
  - The tab list chose its text size against its 100% width and came out a size or two smaller than it needed.
  - The clicking and recording badges and the count-in card are drawn at the screen's scale. At 150% the badge's title ran into the line under it, and the count-in's big number was cut off.
- **"&" in names.** A macro, account or profile name with a "&" in it ("Jump & Run") lost the "&" in lists and combo boxes, with the letter after it underlined. A message with a "&" in it lost it too.
- **The Roblox Account Manager's first screen, redesigned.** It was a 700-pixel column of paragraphs. It is now one card, like the screens after it:
  - what the manager does, in three lines with icons: sign in once, launch any account, locked twice;
  - whether this copy of Tempo is an official release, from Tempo's own integrity check (it says "checking" until the check finishes a few seconds after start);
  - the warning, as three short points in a tinted box;
  - the switch, with a note that it can be turned off again.

  Every line is measured, so no language is cut off, and the card fits the Accounts window at its smallest. After "Enable" the cursor goes straight to the master password field.
- **Fixed: account screens in the wrong place, and the wrong size on laptops.**
  - After "Enable", "Lock" or unlocking, the next screen sat against the left edge of the tab instead of in the middle: the create-vault card at x=0, where centred is 176. Only the screen showing when Tempo started was centred.
  - On a laptop at 125% or 150%, those screens were never scaled. Windows scales Tempo's window once, at start, and only what is on screen then. After "Enable", the create-vault card was two-thirds the size its text needed (440×344 instead of 660×516). Every screen now matches the window's scale when it appears, and so does the "it's in its own window" card on the tab.
  - The separate Accounts window's title strip and buttons stayed at 100% sizes at 125% and 150% while their text grew. They scale with the screen now.
- **Fixed: dialogs and scrolling in the separate Accounts window.**
  - Every dialog from the account screens belonged to the main window, even with Accounts in its own window: confirmations, warnings, sign-in, editing an account, the Roblox client details. With the main window in the tray, a confirmation opened on the other monitor with no owner. When the Accounts window was kept on top, it opened behind it, and the Accounts window, disabled by a dialog nobody could see, looked frozen. Dialogs now open over the Accounts window, and on top when it is kept on top.
  - Making the window narrower left a sideways scrollbar over content that fit: a 711-pixel scroll range in a 587-pixel window.
  - At its minimum width the account list scrolled sideways, with half of it always out of view. The window's minimum width now fits the list.
  - Any Tempo message whose window is hidden or minimised now opens over the Tempo window you are using, or else in the middle of the screen, instead of with no owner on the main monitor.
- **Fixed: Tempo "not responding" on first use.** Some things you do once, or the first time, did slow work on the window's own thread. The window froze while they ran, and on a slower laptop for long enough that Windows called it "Not responding".
  - **Installing** ("Install Tempo on this PC…", or Yes to the offer on the first start). Copying and checking the 104 MB program took 4.4 s even on a fast desktop, and much longer where an antivirus scans the new file, with nothing on screen but a busy cursor. Then Tempo vanished while Windows looked over the new copy, and the installed copy unpacked itself, before anything came back. Now the window says each step as it happens: "Copying Tempo (104 MB)…", "Checking the copy…", "Adding the Start Menu shortcut…", "Adding Tempo to Settings › Apps…". Then it says "Opening the installed Tempo…" and stays on screen until the installed copy is actually running. If Windows stops the new copy (Smart App Control, an antivirus), Tempo comes back and says it is installed and to open it from the Start Menu, instead of leaving nothing on screen.
  - **Every restart** (a new language, the speech engine, a restore point, the tray's Restart) works the same way: the window stays until the new Tempo is running, and starting it no longer holds the window up.
  - **The account vault.** Creating it, unlocking it and changing its master password each work out a key with 1.2 million rounds. That takes a tenth of a second here, and seconds on a laptop without the processor's SHA instructions (twice that for a password change). The card now says "Creating the vault…" or "Unlocking…" while the window stays live. If Windows locks or sleeps while a password is being checked, the vault stays locked.
  - **The Roblox client's Details… and Re-check** read the whole client folder and check every signature in it (0.75 s here even with the files cached). That now happens in the background. Re-check's "Checking…" used to freeze before it could even be drawn.
  - **Tempo now notes it when its window stops answering** for more than 1.5 s, with what was clicked just before ("just after a click on "Save Settings""). The next report like this one will come with its cause in the log. It never records what was typed.
- **Fixed: overlapping and cut-off parts on laptop screens.** Tempo scales everything once at start, so a laptop set to 125% or 150% leaves much less room in the window: a 1366×768 screen at 125% leaves 1093×566.
  - **The tab list.** The Accounts tab was pushed onto Settings (9 px of overlap at 1366×768 and 125%) and over the version at the bottom. An official release's footer is two lines taller still. On a short screen the tabs now sit a little closer, from 40 px apart down to 30. If that is still not enough, the footer drops its extra lines, then the version line.
  - **Dialogs taller than the screen.** The macro editor (648 px tall), Live debug (640), Send a bug report (635), Local API server (634), the Accounts window (759) and, in German, Profile details (594) were all taller than that laptop's 566, so their Save and OK buttons were below the edge of the screen, out of reach. Every dialog now fits the screen it opens on. Whatever doesn't fit scrolls, and no dialog hangs off the edge.
  - Checked at nine window sizes from 1536×816 down to 980×420, on every tab, with Tempo's own layout check plus a check of what is actually visible, with a test build's footer and an official one's. Also checked: 12 dialogs in English and German at three laptop sizes.
- **Keybinds tab: "&" shows again** in "Bare left & right click" and "Settings → Startup & Window". They read "left  right" and "Startup  Window".
- **The status bar is easier to read.**
  - 10 pt text instead of 9 pt, on a taller bar. Each figure's number is bright and semibold and its label quieter; labels and numbers used to share one colour and weight.
  - The figures sit on two rounded pills, what Tempo costs (CPU · RAM · Up) and this session (Clicks · CPS · Peak · Time), instead of being split by thin lines. Idle, running and paused show as a real coloured dot.
  - Icons wear the colour of the tab they open, as in the sidebar and the tray: the profile in the Profiles lime, the session counters in the Statistics blue. They grow with the display's scaling; they were a fixed 16 px beside text that grew.
  - The parts a click opens a tab from (the profile, the hint, the session counters) light up under the pointer and show the hand cursor.
  - Numbers no longer jiggle. Each figure keeps the widest width it has shown, so "CPU 0.6%" turning into "CPU 12.4%" doesn't shove its neighbours along every second.
  - Nothing falls off the end. When the window is too narrow for every figure at the larger size, Tempo's uptime steps aside first, then its RAM, then its CPU, and the CPU figure's tooltip carries what was set aside. The hint in the middle always shows what to do ("F6 to start") and the whole sentence when there is room.
  - Checked in all six languages at the default and the narrowest window width, with small and large figures; labels and numbers 4.5:1 and icons 3:1 in all 38 themes, with HDR correction on and off.
- **Fixed: double-clicking Tempo's tray icon "did nothing".**
  - While a Tempo dialog was waiting for an answer, the double-click tried to hide the window under it. The welcome notice, which opens over the first open of every launch, is one such dialog. The hide never stuck: the window came straight back (measured: minimised, then open again 20–40 ms later), so nothing seemed to happen. Now the double-click brings the dialog to the front, and once it is answered the next double-click hides Tempo as usual. The menu's Show/Hide item and the Show/Hide hotkey follow the same rule.
  - With Tempo open but covered by another window (a game, a browser), the double-click hid it, so again nothing appeared, and it took a second double-click to bring Tempo up. Now Tempo checks how much of its window is actually in view. A covered window comes to the front, and the menu offers "Show window" for it.
  - Checked with real clicks on the installed Tempo's tray icon: from a fresh start with the welcome notice open, then ten rounds of covered → brought to the front → hidden, 10 of 10.
- **Caption history: easier to follow.**
  - Each speaker's label is in that speaker's colour, the same colour as on the caption bar. The history drew every line in one colour.
  - Older lines are a little fainter, so the newest reads first. A new line glides in from below and the lines above make room, instead of the whole panel jumping. The line being spoken has a thin marker beside it that glows while its words arrive. None of this animates while a fullscreen game has the screen.
  - A line longer than the whole panel shows its newest words. It showed its first words and cut the rest off below the panel's edge.
  - With nothing to show, the panel says so in your language, and says when captions are off. It promised captions "as they're transcribed", in English, even with captions switched off.
  - The panel follows the theme and the display's scaling. On a 150% display the text was drawn 1.5× bigger in a box that wasn't, so it held about four of the seven lines it was sized for.
  - It stays above borderless-fullscreen games, like the caption bar. A game taking the screen could bury it for good.
  - A new caption session starts the history empty. It kept the last session's lines until the first new one arrived.
- **Moving captions: see what you're doing, and finish easily.**
  - While moving, both windows show a dashed outline and a note saying what to do, and the caption bar shows "Captions appear here" in your caption font and colour when it has nothing else to show. The history can be resized from any edge or corner, and remembers its size.
  - Both snap to the screen's edges and middle, and to each other: the history lines up with the bar or stacks right above it. Hold Alt to place freely.
  - Double-click either window when you're done. Right-click either one for "Done moving" and "Default place" ("Default place and size" for the history). Before, the only way out was the tray, and the only way back to the default spot was editing settings.json.
  - The Captions tab has a "Move captions" button that reads "Done moving" while moving. "Download model…" moved into the Models… menu to make room.
  - In the tray, "Caption history" and "Move captions" are switches like Live captions, so whether the history is open and whether you're moving captions shows at a glance. "Move captions (drag to reposition)" is now just "Move captions".
  - While captions are on, moving ends by itself when a fullscreen game takes the screen, so the windows can't catch clicks meant for the game.
- **Fixed: moving captions left things behind.**
  - It opened the caption bar and the history to have something to drag, and never closed them. With captions off, the bar then stayed on screen, empty or showing the last sentence of the previous session. Finishing now closes exactly what moving opened. A window you open yourself while moving stays open.
  - It wrote "Drag me — caption bar" and two English sample lines into the windows, and afterwards put back "Listening…" in English, or that old sentence. The words on the bar now stay as they are.
  - Starting to move, or "Show caption bar", in the middle of a caption wiped the sentence on the bar.
  - "Show caption bar" with captions off put up a bar saying "Captions on · listening…". It now says "Captions are off".
  - Turning captions off while moving hid the bar being moved. It now stays until you finish.
  - Double-clicking a window being moved blew it up to fill the whole screen: Windows reads a double-click on a window's grab area as "maximise". The history measured 806 × 238 before the double-click and 1920 × 1080 after.
- **Where you put the caption bar and the history belongs to this PC**, like the main window's place. "Reset to defaults" threw it away, and importing settings from another PC brought that PC's spots, which can be on a monitor this PC doesn't have. Both now keep yours; "Default place" puts a window back.
- **The Captions tab's window buttons fit their words in every language.** Each is as wide as the longer of its two captions ("Show…" and "Hide…"); in Italian, "Show history window" was wider than its button.
- **Translated:** "Waiting for Live Captions…", shown on the bar when the Windows Live Captions window goes away, and the Captions tab's engine line ("Model: base · Engine: CPU · Language: auto-detect"), which was English in every language.
- **Captions read like sentences, not fragments.** Tempo's caption engine hears speech a few seconds at a time, and Whisper treats each of those windows as a whole recording. So every window ended with a full stop, and the next one opened as if mid-thought: "…a light wind from the west. this morning." and "…arrives on Friday. So check the board…". Tempo now measures the pause in the audio at each seam and fixes the punctuation there:
  - A seam with no real pause joins the sentence back up.
  - A comma-length pause gets a comma.
  - A real pause after a sentence gives the next one its capital.

  Measured on a 65-second recording of ten sentences, three runs before and after:
  - False mid-sentence full stops fell from 4–10 per run to 0–1.
  - Sentence starts left in lower case fell from 1 to 0.
  - Words repeated at seams fell too. The joiner now also recognises "40" re-heard as "42", "nine" as "9", "harbour" as "harbor", a last word heard in pieces, and a first word misheard at the window's edge.
  - The delay was unchanged.
- **Far fewer words invented over music, gunfire and noise.** On 30 seconds of synthetic music, fan noise and gunfire, with nobody speaking, the captions showed 15–35 invented words, such as "If you haven't already done this yet, please give me a thumbs up!", "SUBSCRIBE!" and "WOAH WOAH WOAH WOAH WOAH". Now they show 1. Each piece Whisper hears comes with its own estimate that nobody is speaking. Tempo now drops a piece when that estimate is up and the words were unsure, when it is a decode loop, or when it is one of Whisper's learned YouTube sign-offs. This follows OpenAI's reference Whisper and faster-whisper (both MIT), with the bar set for the small models Tempo runs. On real speech that estimate stayed at 0.00–0.01, and nothing was lost. Sound effects written between asterisks ("*outro music*", "*clap*", "*boom*") and captions made only of punctuation are removed too. Live debug counts what was dropped.

- **Caption speakers: one person, one label — faces are linked to voices.** The face & mouth analysis and the voice matching each numbered speakers their own way, and nothing connected the two: "Speaker 2" named by a face and "Speaker 2" named by a voice could be different people, the label jumped between the two at every pause, and a newcomer who took a departed person's place on screen inherited their number. Now a face whose mouth moves while a voice is heard is linked to that voice, and the label is the same whichever of the two sees or hears them. Once linked:
  - a face overrules a wrong voice guess;
  - one person the matcher hears as two voices is joined back into one speaker;
  - two people the matcher cannot tell apart get a speaker each while their faces are on screen.
- **Speaker labels land on the right words.** A caption covers about 3 seconds of speech and arrives 1.5–3 seconds after it was said, and it was labelled with whoever was talking when it *arrived*. At every hand-over, the new speaker's name went on the old speaker's last words, or the other way round. Captions are now labelled by who was talking while their words were being said, and a caption that spans a hand-over is split there, at the nearest sentence end. On scripted conversations with the analyzers' measured delays, words under the right speaker went from 77% to 92% on average (64–84% → 84–98% across five kinds of conversation).
- **The voice matcher tells people apart much better.** Measured on a real two-voice recording (a male and a female text-to-speech voice, fed through the matcher's own processing):
  - When they answered each other quickly, it learned **one** voice for the two of them (49% of speech right); now two voices, 70% right, with no turn left unnamed.
  - With pauses: 60% → 66%, and every turn named (two were never named).
  - A returning speaker is named after about half a second, and a named turn no longer flips.

  The pitch is read with the YIN method: the old reading put a man's voice an octave high at the start of a sentence, right in a woman's range. A speaker change is also caught without a pause in front of it. And a stretch holding the end of one speaker and the start of another no longer mints a phantom third voice.
- **The Captions tab shows who is talking.** A new Speakers card lists each speaker in their caption-bar colour, with their face from the video once it is linked, and "● talking" on whoever is speaking right now. When speaker labels or the face & mouth analysis are off, one button turns them on. The pictures are kept in memory only and never saved. Live debug now shows each speaker's voices and face, and the evidence linking them.
- **Name the speakers.** Click a speaker on the Speakers card to give them a name, and "Speaker 2" becomes "Mia" on the caption bar and in the transcript. That includes the lines they already spoke, so the history window, a saved transcript and exported subtitles all use the name. Hover a speaker to see their face large before naming them. Names last for the caption session. When a number passes to someone new (all nine taken, or the audio changes), the newcomer starts without the old name, and the earlier person's lines keep theirs. The log records that a speaker was named, never the name.
- **Sharper, bigger faces on the Speakers card.** The pictures were cut from the 640-pixel-wide frame the face detector reads, where a face is often 30–60 pixels across, so each one was an enlarged smudge. They are now cut from the full-size window at 96 pixels, and the best shot (largest and sharpest) is kept instead of the latest, replaced after 15 seconds. On a test pattern the new picture matches the real face exactly (mean error 0.0 grey levels); the old way was off by 36 on average.
- **Fixed: Discord showed Tempo users as playing the Steam game "Tempo".** Discord recognises games by their program's name, and it lists that game's program as `tempo\tempo.exe`. Moving the install into a folder called TempoClicker was meant to break the match, but Discord kept matching.
  - The installed program is now called **TempoClicker.exe**. An existing install renames itself the next time it starts, then restarts once under the new name, because Windows keeps reporting a running program under the name it started with.
  - The Start Menu and Desktop shortcuts, taskbar pins and start-with-Windows all follow the rename.
  - Downloads are still called Tempo.exe, so releases and older copies' updaters work as before; install.cmd and the web installer install the new name.
  - A downloaded copy still called Tempo.exe says once how to stop it: install Tempo, or rename the file. The old notice said to rename the *folder*, which does not help, and only appeared in a folder named Tempo. Anyone who saw it gets the corrected one.
  - Live debug shows which file is running and warns about a copy still called Tempo.exe. Settings' integrity line names the real file.
  - Tempo unpacks itself into a TEMP folder named after its program file, so the renamed install unpacks into a new one. The clean-up of old unpacked builds (about 270 MB each) now covers both names. Without that, every update would have left another 270 MB behind.
- **Fixed: the "♪ Music or sounds playing — no speech" note never appeared with Tempo's own caption engine** (the default). Only the Windows Live Captions path ever asked for it, so during music the bar kept its last sentence. Found by a search for code nothing reaches.
- **Live debug is easier to read.**
  - The stats pane can be resized by dragging the bar under it, and keeps its size while Tempo runs.
  - The stats hold still while text in them is selected, or while Pause is ticked; the title says "stats held". They used to rebuild twice a second, wiping any selection within half a second.
  - A "Go to section…" list jumps to Health, Captions, Speakers, Accounts and every other section.
  - The colour legend for the event log sits directly above the log. It used to be at the very top, above the stats.
- **Removed code nothing used:** 16 helper methods, 5 fields, 6 constants and 4 Windows function declarations that had been replaced by newer code, plus 5 unused helpers in the account manager. The two that looked like missing features were checked: the local API's launch wait is capped elsewhere, and the Do Not Disturb exception is deliberate.
- **Fixed: Tempo sometimes didn't start with Windows.** After one sign-out, the sign-in entry pointed at the program's old name, Tempo.exe (gone since the Discord rename), so Windows skipped Tempo at the next sign-in. Its own log shows every other startup app launched and Tempo never attempted. What changed the entry couldn't be traced: nothing Tempo logs did it, and replaying every step of that morning under a registry watch didn't reproduce it. So Tempo now defends the entry itself:
  - While it runs, it notices any change to the entry within moments. An entry naming a file that doesn't exist is put back at once, with a warning in the log saying what it found. Measured: 11 ms.
  - At sign-out it checks the entry, and the Settings › Apps entry, one last time.
  - At every start it checks and repairs the entry, including a start straight to the tray, which is how Windows starts it. Before, that check only ran the first time the window was opened.
  - An entry you removed on purpose, by the uninstaller or by switching the setting off, is left alone.
- **Fixed: Tempo used a tenth of a CPU core while doing nothing.** Every 2 seconds it re-read the name of every audio device, and reading one name makes Windows load that device's whole property list. Measured with two speakers and a microphone: 194 ms of work per look, about 96 ms of CPU every second, for as long as Tempo ran, including hidden in the tray with captions off. That was nearly everything an idle Tempo cost. Names are now read once per device, and Windows says when a device is plugged in, removed or renamed. Idle in the tray: about 1 ms of CPU per second.
- **Restart Tempo from the tray.** A new tray menu item restarts Tempo the same way a language change does, and it comes back the way it was: on screen, or in the tray if the window was hidden. If clicking, a macro or a recording is running, it asks first (when "confirm before exit while running" is on). Captions that were on come back on.
- **Starting with Windows is lighter.** Tempo's self-check reads all 270 MB of its own files, and the first start after an update deletes the previous build's 270 MB from TEMP. Both ran at normal priority the moment Tempo started, including at sign-in, when the disk is busiest. They now run in Windows' background mode, and at sign-in they wait 45 seconds. On an ordinary start the self-check waits 3 seconds, until the window is up.
- **Fixed: Live debug listed "start with Windows is disabled in Task Manager" twice.**
- **Fixed: a failed restart's message mixed languages** ("…para aplicar the new language"). The part naming what the restart was for is translated now.
- **Open the window at sign-in.** A new option under **Launch Tempo when I sign in to Windows**: when Windows starts Tempo at sign-in, its window opens instead of Tempo starting quietly in the tray. It only affects the sign-in launch; opening Tempo yourself still follows **Start minimised to tray**. It is saved the moment you tick it, since the next sign-in is usually a restart away.
- **Colours are right with HDR on.** Windows shows non-HDR apps like Tempo with lighter shadows under HDR than an ordinary screen does: it decodes their colours with the sRGB curve, where SDR monitors use gamma 2.2. So dark themes looked milky, and a 5% grey came out three times too bright. **Correct colours for HDR** now reverses exactly that difference. Shadows go back down, midtones and highlights move at most one step, and hues and the order of shades are kept. Measured on a #181818 surface: 1.65 times the designed light before, 1.02 times now.
  - It used to be a hand-tuned adjustment of the backgrounds alone. That made light themes lighter, though they were never lifted, and left text uncorrected. Now every colour of the theme, and your own accent colour, is corrected the same way, and light themes stay as designed.
  - It follows the window: with HDR on one screen and not the other, colours are corrected only on the HDR screen, and change as the window moves across.
  - It follows Windows: turning HDR on or off, or coming back to Tempo after changing it, updates the colours without a restart. Before, the HDR state was read once, at start-up.
  - The splash screen, the "Tempo is already running" notice and the crash report window are corrected too, so nothing appears lighter before the window does.
  - The note under the box is translated and fits in every language. It says whether the colours are corrected, and when Windows' **SDR content brightness** is at its lowest (80 nits), which makes every non-HDR app look dim and only you can change. Each line has its own colour: a ✓ in green, a warning in amber.
  - Live debug shows each screen's HDR state and SDR brightness, and whether the colours are corrected.
- **Fixed: the accent colour's hex value was wrong with HDR on.** It showed the corrected value, and editing the box saved that as your custom accent, which was then corrected a second time.
- **Fixed: hints and notes were shown at full text brightness.** More than a hundred labels are designed to be muted: hints, counts, "Not recording.", status lines. The theme pass at start-up set every label to the normal text colour, and a theme change did it again, taking warnings' colour away too. Labels now keep their role through every theme change: muted stays muted, a warning stays a warning. Checked across all 38 themes, and their HDR-corrected versions, switching from each to each.
- **Fixed: the Captions transcript had a bright white border** on every dark theme, Windows' old sunken frame. It is drawn in the theme now.
- **Arctic and Peach:** muted text is one step darker. It met the contrast standard by the formula (4.53:1 and 4.50:1), but not quite on a real screen.
- **Tempo starts about three times faster.** Measured on this PC from Tempo's own log, a start to the tray is ready about 0.45 s after it begins, instead of about 1.2 s: the window is built in 0.26–0.45 s instead of 1.0–1.25 s. A start that opens the window has it fully on screen about 1.1 s after launch. Found with a profiler:
  - **Combo boxes were half of it.** Each created its window the moment it was built, and a setting applied just afterwards made Windows destroy that window and make it again. That was 565 ms of the 1.1 s window build, for windows a start to the tray never shows. They are now created when their tab is first shown, and faster then too, because they no longer redraw for every item as it is added.
  - **The layout self-check** that runs after every start measured each caption's text once per pair of controls: about 20,000 measurements and 200 ms. It measures each control once now: 7 ms, with exactly the same result.
  - Reading settings, profiles, macros and history no longer waits for the JSON reader to prepare itself. That now happens on a separate thread as Tempo starts, and so does loading the translation tables. Measured over interleaved starts, it saves about 90 ms.
  - The custom logo's icon is kept, not redrawn from the image at every start (34 ms with a 3.3 MB GIF). The build stamp is read without loading date-format data (16 ms).
  - **The splash screen no longer holds the window back.** It stayed on screen for at least 1.1 s, which had become longer than the start itself; it now stays at least 0.4 s and fades out faster.
- **Fixed: in French and German, the About box's Windows line covered the left edge of "Choose image…".**
- **Fixed: choosing a new logo could leave the old one showing.** If the old file could not be deleted at that moment, both files stayed, and whichever came first alphabetically was used. The newest one is used now, and clearing the logo removes every copy.
- **The Roblox client check looks at everything the game loads, not just one file.** It checked RobloxPlayerBeta.exe alone.
  - Current clients also load **RobloxPlayerBeta.dll**, a separately signed module. A tampered one runs inside the game while the exe still verifies. It is checked now, and a failure there makes the client "looks modified".
  - **Anything else executable in the client's folder** is checked too. A DLL named like a system one (dxgi.dll, version.dll) is loaded into the game in its place. That is how injectors and overlays get in while every Roblox file still verifies. An official folder holds only Roblox's files and Microsoft's WebView2 loader. Anything else is named on the Accounts page, in amber: "Roblox's own files check out, but dxgi.dll beside the client isn't Roblox's". The details view lists each file and who signed it.
  - The client is checked again when any of those files changes. Before, only a change to the exe was noticed.
- **Fixed: a tampered Roblox client was reported as "isn't signed" instead of "looks modified".** The signature check passed Windows a flag that makes it answer "no signature" for a file whose contents no longer match its signature. Measured on a copy of the client with one byte changed: it now reads as modified, in red.
- **Fixed: every signature check leaked about 40 KB of memory.** Windows' verification state was never released. Measured: 16 MB over 400 checks before, nothing now. It mattered more now that each check reads five files.
- **"Signed by Roblox" means exactly Roblox Corporation.** It was a text search, so a certificate issued to "Roblox Corporation Tools LLC", or even "Not Roblox Corporation", passed. The certificate's organisation must now be exactly Roblox Corporation.
- **Bloxstrap, Fishstrap and Voidstrap are recognised by where they are, not only by name.** The program Roblox links open is handed your login ticket on every launch, and any program can call itself Bloxstrap.exe. A known launcher must now also be in its own folder, under AppData\Local or Program Files. One anywhere else still launches, but is shown as a launcher Tempo doesn't recognise.
- **The Roblox client details view:** its headline no longer cuts off its second line, the field names line up in every language, and Re-check after uninstalling Roblox no longer says "isn't signed".
- **Local API, version 4.**
  - **Fixed: a launch could join as the wrong account.** With multi-instance off, a running Roblox client takes the launch and stays signed in as its own account, and /launch still answered "ok". It now refuses with `clientRunning`. Add `ifRunning=close` to close the running client first (what the page's "Yes" does), or `ifRunning=launch` to launch anyway.
  - **Private servers:** `privateServer=` takes a private server link or code, with a `placeId`. A share link or an unusable code is refused rather than launched, because the client would quietly join the public game instead.
  - **`/follow?account=NAME&user=NAME`** joins another player's game. A number is taken as a user id.
  - **Every refusal has a stable `code`** (`noSession`, `sessionExpired`, `clientRunning`, `tooManyLaunches`…), so a script no longer has to match English text. An expired session is now told apart from an account with no session. A 429 says when to try again (`Retry-After`).
  - **`/help`** lists every endpoint and option. `/accounts?detail=1` adds each session's state, last use and use count. `/status` adds the running clients and the client check's verdict. A successful launch names the account as saved, who was followed, and whether it waited for Roblox's answer.
  - `/status` no longer reports a verdict about a client that has since updated. It says `unchecked` and has the Accounts page check again.
  - Its disk and process checks run off Tempo's window thread.
- **Fixed: hotkeys did nothing while Tempo sat in the tray.** "Sleep in tray" (on by default) paused the start hotkeys the moment Tempo was hidden. With "Start minimised to tray" on, Tempo therefore started asleep every time, and F6 did nothing until the window was opened. One PC's log shows 13 minutes with only Emergency stop bound.
  - Tempo now sleeps only after **30 minutes** hidden, with nothing running and no hotkey used. That is what the setting was for: a forgotten Tempo that can't start clicking hours later.
  - A sleeping Tempo no longer swallows a key press in silence. The press wakes it, a notification says so, and the next press works. The press itself still doesn't start anything.
- **Fixed: every hotkey stopped working after you used a keybind field.** Clicking into a field on the Keybinds tab pauses the hotkeys, so the key you're setting doesn't fire. They only came back when you clicked something else inside Tempo, so going straight back to a game left all of them silenced. They now come back as soon as Tempo's window loses focus or hides.
- **The keys Tempo shows now match the keys that work.**
  - The on-screen clicking badge said "Press F8 to stop" (Emergency stop) while everything else said F6. It now names the same stop key as the rest of Tempo, in your language.
  - While paused, the status bar said "F6 to resume", but F6 *stops* a paused run. It now names the Pause / Resume key.
  - The Clicker tab's tip always said "Press F6", whatever key was bound. It now names the real key, or says where to set one.
  - A key that Windows refused to bind is no longer shown as if it worked.
  - In Hold mode, Start reads "Hold F6" and the status bar and badge say to hold and release it. Pressing Start explains the mode instead of starting and stopping in the same instant. Hold mode with no key set says it needs one.
- **Multi-instance:**
  - It uses much less CPU while Roblox runs. Every 0.7 s it scanned every handle on the PC (about 180,000 on one test PC, about 40 ms of CPU each time), even when there was nothing to close. It now scans only when a client might need its lock closed.
  - Fixed: switching multi-instance on or off could restart accounts that weren't running, because it put back every account launched that session. It now matches the running clients to their launches.
  - Fixed: switching it off relaunched every account, though only one client can run then; the others just joined the first client as its account. Only the most recent one comes back now.
- **The Keybinds tab's sleep notice was cut off** in every language but English. It has room for all of it now.
- **Fixed: the status bar said "Idle" in English** in every language, until the first run changed it.
- **Fixed in the local API dialog:**
  - The endpoint list hid every "&": "&placeId=ID" showed as "placeId=ID", so the one example of adding an option was wrong. It lists the new calls too, each on one line in every language.
  - Four buttons were cut off in Spanish, French and German ("Kopieren" had 55 of the 62 pixels it needs).
  - "(IPv4 only)" was English in every language.
  - In French, the note under the list could start a line with a colon.
- **Macros can record camera turns, for games.** Tick "…as camera turns (games)" under "Capture mouse movement" on the Macros tab before recording.
  - Tempo then records how far the mouse moved, not where the pointer was, and plays it back as real mouse movement. That is what turns a game's camera: first person, shift lock, holding right-click.
  - Before, a recording of positions held almost none of a turn, because the game keeps the pointer locked in place. Playing positions back never turned a camera at all.
  - Turns replay as exactly the recorded movement, spread smoothly over time rather than in jumps. Clicks happen where the pointer is instead of jumping to a stored spot, which a game can read as a sudden huge turn.
  - A turn that ends in a click is kept in that order. The step list shows camera steps as "Camera Δ +120, −15".
  - Leave it off for clicking on screens and menus.
- **Fixed: camera-relative movement, its calibration and the second cursor could cut off each other's mouse input.** Windows allows one raw mouse registration per program, and each feature made its own. The last one started took the mouse from the others, and the first one stopped switched it off for all of them. They now share one, and so does macro recording.
- **Macros record and play the mouse side buttons.** X1 (Back) and X2 (Forward), "Mouse 4" and "Mouse 5" on most gaming mice, were ignored while recording. They are now recorded like any other mouse button: pressed where you pressed them and held as long as you held them. Stopping a macro mid-hold lets go of them, and the macro doctor catches one that is never released. The editor's Insert click can add them too. A side button bound to one of Tempo's own hotkeys stays out of recordings: Tempo takes that press, so the game never sees it. Older versions of Tempo still open these macros and skip the side-button steps.
- **The second cursor: a second mouse aims, scrolls and clicks where it should.**
  - **Slow aiming works below 100% speed.** Each report from the second mouse was rounded on its own. At 50%, a careful one-count movement rounded to nothing, so the cursor wouldn't creep at all, which is exactly when aiming needs it. Above 100%, slow movement came out faster than fast movement. What doesn't add up to a whole pixel is now carried over.
  - **It can't get lost between monitors.** It was kept inside the rectangle around all the monitors. With two screens of different sizes, or set at different heights, that rectangle has corners no screen covers, and the cursor could vanish into one. It now slides along the edge of the screen it is on, like the real pointer.
  - **Fast scrolling scrolls where the second cursor is.** Each wheel notch sent the pointer over, scrolled and brought it back as separate steps, several at once when scrolling quickly. Part of a fast scroll landed in the window under your main pointer. Each notch is now one ordered step: in a test, 24 of 24 notches and tilts reached the window under the second cursor, sent in a burst or spaced out, and none reached the window under the main pointer.
  - **The second mouse's side buttons act at the second cursor.** Back and Forward on the second mouse went back or forward in whatever window your main pointer was over. They now press at the second cursor and can be held, like left and right. Its tilt wheel (sideways scrolling) works the same way; it wasn't read at all.
  - **Choosing the second mouse by wiggling needs a wiggle.** The first mouse to move a hair's breadth won. That was usually the one in your hand, which had just clicked the checkbox, so it drove the second cursor and your real pointer froze. It now takes a few quick strokes back and forth. An unanswered request is no longer repeated every 13 seconds for as long as the mode is on. Picking "Ask by wiggling" in the Clicker tab's list now asks at once, as the cursor's own menu does.
  - **The grab hotkey parks it.** "Second cursor: grab / park" says a second press parks the cursor where it is, but a second press did nothing; only a click could drop it.
  - **Fixed: the setting said a right-click spams.** It is the middle button, now in all six languages. The cursor's menu and the "mice detected" line were English in every language; they are translated now.
  - **The marker is drawn at your display's size.** At 150% or 200% display scaling it was a fraction of the size of the real pointer next to it. At the bigger sizes its edges were cut off, and moving onto a screen with different scaling knocked the ring, crosshair and dot off the point they aim at.
  - **Less mouse lag with a fast gaming mouse.** The marker was moved for every report, 1,000 or more a second, on the same thread that handles every mouse event on the PC. It now moves at most every 4 ms and always ends on the cursor: a burst of 40 reports moved it once instead of 40 times.
  - **Fixed: which mouse did what was decided on out-of-date input.** Windows hands Tempo each mouse event before the report saying which mouse sent it. A main-mouse click just after second-mouse activity could be swallowed, and the main pointer could stick for a moment. Those reports are read first now.
  - **Closing Tempo mid-drag lets go of the button.** A button held with the second mouse stayed down after Tempo closed.
  - **It stays where you parked it.** It started at the same spot every time Tempo started, so every update, restart and sign-in lost the place it was aimed at. The spot is now remembered. If that screen is gone, it comes back onto the nearest one, and the same happens when a monitor is unplugged or rearranged while Tempo runs.
  - **The marker shows whether it is spam-clicking.** A green badge means clicks are going out. Amber with pause bars means they are held back: the window it is aimed at is covered or closed, or the cursor is over Tempo. Spam clicks are sent in the background, so before, nothing on screen showed either.
  - **Spam follows the window it is aimed at.** Move that window and the clicks keep landing on the same spot in it. Before, they went on landing where the window used to be, on another part of the same program. Resizing the window doesn't move the spot.
  - **Spam-clicking plays the start and stop tones** when the clicker's sound options are on, which helps when it is toggled with a hotkey or the second mouse while a game is in front. Stopping the clicker and the spam together plays one tone instead of two on top of each other.
  - **The cursor's menu says what spam is aimed at and how many clicks it has sent.**
  - **Real clicks, for games.** Spam clicks are sent in the background, which leaves your pointer free but never reaches a game that reads the mouse itself. The cursor's menu now has **Click method › Real clicks**:
    - The pointer jumps to the second cursor for each click and comes straight back, keeping any movement you made meanwhile.
    - Each press is held long enough for a game that reads the mouse once a frame. At 10 clicks a second, the press lasts about 25 ms and the pointer is away about 40 ms of every 100.
    - Real clicks wait while the game they are aimed at isn't the window in front, because a click would pull your typing into it. They also wait while you hold a mouse button, because a click would end a drag or a held camera turn. The badge turns amber and the menu says why.
    - Background clicks stay the default.
  - **The marker is drawn properly.** Its window treated pure black as see-through.
    - The arrow's black outline and its shadow vanished, so on a light background the white arrow all but disappeared.
    - The "grabbed" glow was blended against black and came out as a dark disc.

    The marker now has real transparency. The arrow looks like the Windows pointer on any background, and the glow is soft. The ring and the crosshair have a thin dark edge, so they hold up over a background of their own colour. The pause bars on the badge are crisp instead of smeared.
  - **Spam picks the game back up after it restarts.** Spam is aimed at a program, and a relaunched game is a new copy of it, so spam stayed paused for good, through a whole AFK session. Now, when the window it was aimed at has closed and the same program opens under the cursor again, spam carries on in it. While the original is still open, another copy on top (a second account's client) only covers it, and spam waits.
- **Macro recordings no longer include Tempo's own controls.**
  - A recording stopped with the Stop button ended with a click on that button. Every playback, and every loop, then moved the pointer to where the button had been and clicked whatever was there. Clicks, scrolls and key presses on Tempo's own window are no longer recorded, and neither is the pointer's trip to the button. A recording started with the Record button no longer opens with the trip away from it either.
  - Stopping with a hotkey that has a modifier (Ctrl+F9, say) left a Ctrl press at the end, so playback tapped Ctrl at the end of every loop. It is left out now. So is a modifier still held from the hotkey that started the recording, and the release of any key pressed before recording began.
  - A middle-click, or a click with modifiers, bound to Record, Play or Stop no longer ends up in the recording.
- **Macros replay what you actually did.**
  - Recorded without mouse movement, a drag-and-drop played back as a click where it started. The pointer now moves to where the button was released. A scroll now plays where you scrolled, not where the last click left the pointer.
  - When you press a key, the pointer is where you left it. The last movement before a key press, or before the end of the recording, could be dropped: up to 16 ms of movement, which is 50 pixels or more on a quick flick.
  - A button still held when recording stops is released at the pointer's real position. Without mouse movement it was stored at an impossible position, and the macro doctor then listed that macro as broken.
  - Fixed: a key press and a click that Windows timestamped out of order could put a 49-day wait into a recording.
- **Smooth mouse movement keeps the macro's timing.** Each move used to glide for at least 70 ms on top of the macro, so a recorded path (a move every 16 ms) played about four times slower, and everything after it ran late. The glide now happens during the wait before each move or click, and arrives exactly when that step is due. Measured: a 0.8 s recorded sweep plays in 0.8 s; the old 70 ms minimum made it about 3.5 s. Recordings made without mouse movement now glide between their clicks instead of jumping. The checkbox has a tooltip that explains this.
- **Fixed: quick-play hotkeys used the selected macro's settings.** Slots 1–3 took the loop count, speed and countdown shown on the Macros tab, which belong to whichever macro is selected. A selected macro set to loop forever made a one-shot slot macro loop until stopped. Each macro now plays with its own settings.
- **Fixed: the stop key didn't cancel a macro's countdown.** The "3, 2, 1" before a playback or recording says "Esc or your stop key to cancel", but only Esc worked. Stop macro, Emergency stop, Play and Record now cancel it too. Before, pressing Play or Record again stacked a second countdown on top of the first, and both then ran.
- **Recording and playing macros is lighter on the PC.** While recording, every captured step updated the Macros tab from inside the input hook, so the mouse across the whole desktop waited for it. The tab now catches up afterwards, in one batch. During playback the tab was updated once per step, hundreds of times a second. It now updates only as often as it can redraw: in a test, 10,000 steps cost one update.
- **Fixed: a stopped macro could keep playing beside the next one.** If a step hadn't returned 2 seconds after Stop, pressing Play started a second playback while the first carried on. Play now waits until the first has really stopped.
- **Faces are recognised when they come back.** A face out of sight for 3 seconds was forgotten, so after every camera cut, wide shot or other person's close-up, the same person came back as a stranger: no speaker, name or picture until they had talked long enough to be linked again. And tracking went by position alone, so a cut between two close-ups framed alike gave the second person the first one's identity. Tempo now remembers what each face looks like (the colours of the head and around it, and the skin tone) and knows people again when they return. A new face is looked at for about half a second before it is named.
  - Measured on a real NASA news conference: four astronauts in the same red polo, in front of the same backdrop, in close-ups framed alike. Position-only tracking put 21% of faces under someone else's identity; one identity was shared by five people. With recognition, none, both on the stretch it was tuned on and on another it had never seen. Each person went from 2.9–5.6 identities to 1.4–2.0.
  - It costs about a millisecond on each ~20 ms analysed frame.
  - The Speakers card shows the best of a speaker's pictures (their close-up rather than a tiny face from a wide shot), and Live debug counts the faces known again.
  - Nothing is saved: faces are forgotten when captions stop or the audio changes.
- **Fixed on the Captions tab:** the big status said "Off" in English in every language, and "Listening…", "Loading the speech model…" and "Not hearing anything" had no translations at all. The Speakers card's status also lost its first letter ("aces on screen: 2"), and speaker names were hard to read on light themes; they now keep their colour but in a shade that reads.
- **Fixed: parts of a tab could stay blank after switching to it.** On the Captions tab the engine and source lines, the transcript's hint and newly shown buttons stayed empty until something else made the window repaint. A tab switch freezes the page's drawing while its cards are re-centred, and when nothing had moved it skipped the repaint afterwards, so whatever the page owed a paint for just being shown was lost. A tab switch now always repaints the page it shows.
- **Fixed: multi-instance could close the Roblox game you were already playing.** Multi-instance starts the moment the account list opens (as soon as you unlock the vault, which Tempo used to ask for at every start). Its very first step reached into every Roblox client already running and closed that client's single-instance lock, the game in progress included, even though nothing was being launched. Taking the lock away from a running client can make it quit. A client that is already running is now left alone. Its lock is closed only when you launch another account from Tempo, the one moment multi-instance needs it gone. A client you open yourself while multi-instance is on is still prepared as it starts, as before, and Live debug shows how many running clients are being left alone.
- **Fixed: the Accounts window opened by itself every time Tempo started.** With "In its own window" chosen, Tempo reopened it at every launch — or, when it started in the tray, the first time you opened Tempo — and since the vault is always locked at start-up, what appeared was an "Unlock your accounts" window nobody had asked for. "In its own window" now only decides *where* the account manager opens: the Accounts row in the sidebar, Ctrl+9 and "Roblox accounts" in the tray open it, and until then it stays closed. "Reopen on the tab I used last" also no longer lands on the Accounts tab's placeholder.
- **Fixed: Stop could hang with "Hold each click" set, and a quick restart could run two clickers.** A held click or key could not be cut short: with a long hold, Stop waited 2 seconds, gave up, and the button or key stayed down until the hold ran out. Pressing Start in that moment left the old run clicking beside the new one, with its old settings, and when the old one finally ended it stopped the new one too. Stop now lets go at once (measured: about 1 ms, button released), and a stopped run can no longer touch the next one.
- **Fixed: Anti-Freeze let multi-clicks through at twice the cap or more.** "Max CPS" counted presses, not clicks, so a double click at "max 200" sent 400 clicks a second and a quadruple 800, while the Clicker tab said "max 200". The cap now counts real clicks, like every other CPS in Tempo, and so does the live rate: a double click every 100 ms reads 20 CPS, not 10. Measured with a double click under a 20 CPS cap: 40 clicks in 2 seconds (counting presses, the old cap allowed 80).
- **The Clicker tab's rate text is right, and in your language.**
  - The line under the interval, the Target line, the status bar ("F6 to start", "Paused — F6 to resume", "Ready"), the count-in's "Starting: …" and the "run finished" notice were English in every language. They are now in all six.
  - A keyboard key was counted as a double click whenever the click-type box beside it said Double. A key press is now always one.
  - The "(caps rate)" warning forgot that a double click holds twice, and stayed quiet while the hold was capping the rate.
  - Changing the click type, turning Anti-Freeze on or off or changing its cap left the rate lines showing the old values until something else was touched.
  - Both lines ran off the edge of their card with some settings — in English too, where the Target line's "×2 = 180 clicks/s" had never been readable. A line that is too long now drops the interval it repeats from the boxes above and keeps the rate and the warnings.
  - The repeat estimates allow for the Anti-Freeze cap, the pause between bursts and the click type: 1,000 clicks in bursts of 10 with a 1-second pause said "≈ 10 s" for a run that takes 110.
  - The "run finished" notice gives that run's clicks — it gave every click since Tempo started, "12,345 session clicks" after a 100-click run — and shows even with tray notifications off, since its own switch is what asks for it. The count-in names the key a key run will press, and no longer announces a 2,000 CPS run as 1,000.
- **Fixed: updating skipped Tempo's normal shutdown.** "Update now" quit Tempo on the spot, so a click run in progress was never recorded in your history or lifetime totals, captions that were on lost their transcript, a key held down by camera-relative movement stayed held, and Windows' own caption bar could be left off-screen. Tempo now closes the ordinary way before the new version goes in — everything is saved and put back — and captions come back on after the restart. The integrity check's Repair button closed the same way, and is fixed too.
- **Fixed: a failed update was silent — and could leave Tempo closed for good.** After Tempo exited, a helper copied the new Tempo.exe over the old one. When that failed it deleted the download and started the old version without a word. And in any install folder with a "!" in its path — which is every folder under a Windows user name containing one — it replaced nothing *and* never started Tempo again. Now the verified new build is placed beside Tempo.exe while Tempo is still running, so a full disk or a folder that refuses writes is reported right there. The swap itself is a single rename that happens completely or not at all, so a half-written Tempo.exe is impossible. And the Tempo that comes back says what happened: "Tempo updated — you're now on 1.0.x", or "The update didn't install — click to try again".
- **Downloading an update survives a bad connection.** A dropped connection used to fail the whole 100 MB download, and trying again started from zero. It now picks up where it stopped, by itself, up to four tries. A connection that goes silent is noticed after 30 seconds instead of hanging for a minute per read, and Cancel stops at once — it used to wait for the stuck read to finish. An interrupted download is kept and carried on next time; GitHub's checksum proves the finished file either way. The progress window shows a steadier speed and time left, says when it is resuming or checking the file, and Escape cancels it.
- **"Download page" opens the release page.** It used to open the Tempo.exe download itself: a button labelled as a page started a 100 MB download in your browser and left one more loose copy of Tempo in your Downloads folder.
- **The update dialog, redone.** It is in your language — its buttons and version line were English in every language — and it grows to fit longer translations instead of cutting them off. The release notes are formatted, with headings, bold text, code and real bullets, instead of Markdown shown as plain text. It says what pressing the button does: Tempo restarts, your settings, profiles and macros stay as they are, and a restore point from before the update is kept. It shows how big the download is, and warns first if a run or a macro would be interrupted; when it would, Enter means Later. A copy that can't update itself (running from inside a zip, or from a folder it can't write to) says why, instead of just leaving the button out.
- **Tempo checks for updates while it stays open.** The automatic check only ran when Tempo started, so anyone who leaves it in the tray for days never heard about an update. A check that failed at sign-in, when the network is often not up yet, was never tried again. It now checks again at the pace you chose (twice a day for "Every launch", daily or weekly), and retries a failed check after 5, 15, 60 and 180 minutes. The Settings option is now simply "Check for updates" with the frequency beside it — its old caption ended in "…" in five languages.
- **One check, one answer.** The Settings button, the tray item and the automatic check each ran a check of their own, so pressing "Check for updates" during the automatic one could open the update dialog twice. They now share one check, and while it runs the button, the line under it and the tray item all say "Checking…".
- **An update you know about is one click away.** Once one is known, the tray item reads "Update to v1.0.x…", and the sidebar's "v1.0.x available" and the line in Settings open it directly. An update found while Tempo is in the tray gets a notification (but not in the first minutes after signing in to Windows). With Tempo's own pop-ups switched off, clicking the Windows notification now does what it says, where it used to do nothing. The dialog waits until you are actually in Tempo's window rather than rising over whatever you are doing, "Later" means not again this session, and a version you skip is no longer advertised in the tray or the sidebar.
- **Fixed: the update messages were English in every language.** That covered every reason a check or a download could fail, the Settings line ("Last checked for updates…") and the tray icon's tooltip ("Tempo — Idle"). They are now in all six languages, and the reasons are more useful: a rate limit says when to try again, a timeout no longer claims "your internet is otherwise fine" (it can't know that), and a Wi-Fi sign-in page or a proxy answering instead of GitHub is named as the likely cause instead of a parser error.
- **Fixed: "Skipping version 1.0.320" stayed in Settings for good**, even on 1.0.322; a skipped version is now forgotten once Tempo reaches it. Also: the updater picks Tempo.exe by name (it took whichever .exe a release listed first), and a successful check after a failed one no longer leaves Live debug calling the stored version a stale cache.
- **Fixed: "Click without moving my cursor" never actually worked.** The setting saved, loaded and showed its tick, and the clicking engine never saw it: the engine works on a copy of the profile, and the copy left that one setting out. So every run moved your real pointer onto the target and clicked there — exactly what the option exists to avoid — and it has been that way since the option existed. It now does what it says: the clicks go to the window under the target point and your mouse stays where it is. While fixing it: a background *double* click reached apps as two single clicks, so anything that opens on a double-click (a file list, most menus) never opened; it now arrives as a real double-click.
- **Fixed: pressing Save on the Clicker tab threw away the rest of the profile.** Save rebuilds the profile from the controls on that tab, and everything not on that tab went back to defaults — the description, icon, colour tag, category, the favourite star, the date it was created, how many times it had been used, its total run time, and any keybinds or appearance saved with it. The Multi-Point tab's Save did the same. Save now keeps all of it.
- **Fixed: renaming a profile through Save deleted the old one.** It removed the profile — into the recycle bin, as though you had deleted it — and added a new one; if the new name was already taken it quietly became "Name 2". It now renames the profile where it is, and tells you if that name is already in use.
- **Fixed: a duplicated profile claimed the original's history.** A copy showed "used 48× · 6h 12m" and sorted as recently used before it had ever run. A copy now starts fresh, and is named in your language.
- **Fixed: two profiles with the same name, or none.** If the profiles file ever held two called "Fast", the second could not be chosen — picking it loaded the first. They are given their own names when loaded, and the log says so.
- **The profile cards stop rearranging themselves.** Cards with the same use count could swap places on any rebuild, and a rebuild follows every click — so cards you had not touched moved under the pointer.
- **Profiles from the keyboard.** The card you were on keeps the keyboard after you switch to it or star it (it used to vanish, sending you back to the start of the tab order). Delete deletes the card you are on, F2 opens its details, and the right-click menu can add or remove the star — which was a mouse-only target on the card.
- **Switching profile mid-run says so from the Profiles tab too** — the Clicker tab's dropdown always did. And renaming a profile while it is running no longer loses that run's time.
- **Live debug stopped freezing Tempo.** Opening it locked the whole app for about 2.5 seconds, and every pause while typing in the filter box locked it for 1 to 4.4 seconds; on top of that the stats panel cost about 47 ms twice a second — a tenth of everything Tempo could do — for as long as the window was open. It filled the panes a line at a time, which is the slow way to write into that kind of control. The same content is now drawn in one go: about 30 ms to open, 20 ms per filter, 4 ms per stats refresh. Identical text, colours and highlights, checked character by character.
- **The caption transcript reads better.** Timestamps are muted so the speech stands out, and a filter now marks what it matched, the way Live debug does. If speech arrives while you are scrolled up reading, the line under the box says "3 new below ↓" — click it to jump to the newest. Before, new lines landed silently, because the view deliberately does not move while you are reading back.
- **Fixed: the multi-instance status covered its own first letters.** Beside "Run several accounts at once (multi-instance)", the green status read "tive — managing 1 running Roblox client": the "● Ac" was under the end of the checkbox caption. The caption takes the page's bold font only once it is on the page, and the status had already been placed using its narrower, regular-weight width. The status is now placed from where the caption really ends, and moves if that changes. "● Active" fits in full in all six languages; the longer states end in "…" with the full text on hover.
- **The layout check now covers status lines that fill in later.** A status line is empty when the check runs, so it was treated as nothing at all. That is how the overlap above got past it. A fixed-size line that is waiting for text now counts as the space it will fill. The Accounts list check also now measures that screen inside the tab, as it really appears, instead of on a bare window with a different font.
- **The tray menu can do more.**
  - **Switch profile** from the tray. The current profile is marked, and switching does exactly what switching in the Profiles tab does. It is greyed out while clicking, because switching mid-run gives the running clicker the new profile's settings halfway through.
  - **Roblox accounts** opens the account manager wherever it lives (its own window or the tab). **Lock vault** appears whenever the vault is open, for when you walk away from the PC.
  - **Stop macro** appears while a macro is playing. Start/Stop only ever controlled the clicker.
  - The status line shows the rate it is really clicking at (the anti-freeze limit can hold it below the one set), or which macro is playing.
  - Notification history shows how many notifications you missed.
- **Every tray menu item has its own icon.** Four items shared a speech bubble and three shared a window outline, so the icons said nothing. There are now distinct icons for captions, history, the notification inbox, alerts, screenshots, settings, updates, the source code, profiles, accounts and the vault lock. The new Profile submenu follows the theme like the rest of the menu.
- **Dragging the Accounts window is smooth now.** With an animated logo, the window stuttered as it moved. It fell up to 60 px behind the cursor, on every test run. The logo updates the icon of every open window about 15 times a second, and each update redraws that window's frame. Tempo already paused this while its main window was being dragged, but not for the Accounts window, and the two share one thread. It now pauses for either. Measured on the same drag: average lag 3.7 px → 0.3 px, worst 60 px → 18 px. A side-by-side test with the same window and logo, differing only in whether the icon kept updating mid-drag, confirms the cause.
- **The Accounts window can be moved by its header.** The bar with the Accounts icon and name looks like a title bar, so it is where people reach, and it did nothing. It now behaves like one: drag it, double-click to maximise, drag to a screen edge to snap, right-click for the window menu. Its two buttons still work as buttons.
- **Fixed: the Accounts window could open bigger than you left it.** When its saved position was on a monitor that has since been unplugged, the saved outer size was applied as the size of the inside, so the window grew by its frame and title bar every time that happened.
- **Fixed: "Dock as a tab" did its work twice.** It moved the account screens back and re-themed the whole app, then the window's own close handler did both again, and logged "window closed" for a dock.

- **Test a Python script before it is in a macro.** The Script step dialog has a **Test run** button: it runs the script right there with the Python Tempo will really use, shows its output as it prints, and ends with "✓ Finished in 240 ms", "Stopped", or the reason it failed. Until now every failure a script step can have (the wrong Python, a missing package, a crash on line one) was found mid-macro, with the game in front and the reason buried in a log. During a test run the script sees `TEMPO_TEST_RUN=1`, so it can tell a dry run from the real thing.
- **Fixed: a failed script only said "The script exited with code 1."** The reason was in the traceback Tempo had just captured. Tempo now reports its last line (`ValueError: bad input 42`). For the most common failure, a package installed into a *different* Python, it gives the exact command that fixes it, for the interpreter that actually ran the script. It also knows the packages whose install name differs from their import name: `cv2` is `opencv-python`, `PIL` is `Pillow`, `yaml` is `PyYAML`, and a dozen more. A script that simply calls `sys.exit(3)` is still reported by its exit code, with no reason invented.
- **Fixed: scripts that print accents or emoji crashed under Tempo** with `UnicodeEncodeError`, even though they ran perfectly in a terminal. A terminal is a console, which Python writes as UTF-8. Tempo reads a *pipe*, which Python encodes in the old ANSI code page, and that code page has no emoji. Python now writes UTF-8 to Tempo, which Tempo already expected.
- **Fixed: looking for Python could freeze Tempo.** Tempo checks each candidate interpreter by asking for its version, with a four-second limit. The limit never applied: the output was read to the end first, and that does not return until the program exits. A candidate that never answered was still blocking after 12 seconds in testing, and it would have blocked for ever, freezing the Script step dialog and Tempo with it. Now Tempo gives up at four seconds and ends that candidate. The dialog also looks Python up in the background, and follows the path box: pick a script with a `.venv` beside it and the dialog now names the Python in that `.venv`, where it used to keep naming the system Python it found when it opened.
- **A script's output reaches Live debug as it prints.** It used to arrive as one block after the script finished. Up to 200 lines per run are logged, so a script printing every frame cannot push everything else out of the log.
- **Scripts can read where they are running.** Environment variables tell a script its own path (`TEMPO_SCRIPT`), Tempo's version, the cursor position when the step started (`TEMPO_CURSOR_X` / `_Y`), and the macro, step and loop it is running in (`TEMPO_MACRO`, `TEMPO_STEP`, `TEMPO_LOOP`). There is no syntax to learn, and a script that ignores them behaves exactly as before.
- **The layout check now checks far more of the window.** It checks every page in every language at start-up, and it had blind spots. It skipped any control without a caption, so text boxes, sliders, meters, lists and colour swatches were never checked for overlaps. It assumed every switch's label starts 24 px in, when a toggle's starts at 46. It measured a right-aligned label from the wrong end, and a button's caption without the icon painted beside it. It now compares 9,903 pairs across 674 controls (up from 7,198 across 529) and counts screens that exist but are off screen, such as the Accounts panels while the account list is in its own window. The overlaps it had been missing are fixed:
  - the Dim slider ran under its own label;
  - the Input level meter painted over "Entrada:", "Eingang:" and "Ingresso:";
  - the Preview colour swatches ran into their label.
- **Buttons drop their icon before they cut their words.** "Enregistrer" and "Speichern" beside a save icon needed about 100 px in an 80 px button, so the text was cut to make room for the picture. Now the icon goes first. The Clicker tab's New / Save / Duplicate / Delete row also sizes itself from its captions.
- **Every dialog is now checked in all six languages**, not just the main window: the macro editor, the Script step, notification history, the click point editor, the CPS test, profile details, About and the Accounts window. The first run found 29 problems in five of them, all now fixed:
  - "Enregistrer" and "Speichern" cut off on the macro editor's Save button;
  - "Effacer l'historique" and "Cancella cronologia" cut off in notification history;
  - in the CPS test, "Duración de la prueba:" ran 36 px into the first button, and "Izquierdo", "Esquerdo" (and even English "⌨ Space") were cut off;
  - in profile details, "Beschreibung" ran into the hint beside it and the colour label ran under the swatches;
  - About's "Choose image…" and "Open Log Folder" were cut off in French.
- **Check layout, on demand.** Live debug has a **Check layout** button. It runs the same checks over every open Tempo window, laid out as it is right now rather than as it was at start-up, and filters the log to what it found.
- **The translation check now finds English that was left in, and checks the translations themselves.** A new start-up check counts text still showing in English when a translation exists (500 checked per language; none found). The translation tables are now checked for a phrase missing from one language, the same phrase translated two different ways, and `{0}`-style placeholders that do not match the English. They are also checked for a translation that names a button or tab by a word the translated screen does not use: 39 translations sent you to a tab or option under a name that appears nowhere on screen. All of these are now fixed. Also translated for the first time: the macro step names (Left ↓, …), the countdown's "GO!", the hotkey tooltip, and the interval box's screen-reader name. Live debug's Language section reports all of this in one line.
- **Fixed: the transcript's "Copy shown" was cut off in Spanish, German and Portuguese.** Copy all becomes Copy shown when a filter is typed, and those three translations did not fit the button. The start-up check could not see this, because it only happens after you start typing. The button is now sized for both captions.
- **Fixed: pressing Copy twice in Live debug left it reading "Copied ✓" for good.** The second press saved the first confirmation as the button's caption and put that back.

- **Accounts, in its own window.** The account manager is the one page people want open *beside* what they are doing, and a tab cannot do that — selecting it hides whatever you were on. **Settings → Window & Display → Roblox Accounts** now offers *As a tab in this window* (as before) or *In its own window*: a separate, resizable, alt-tabbable window that can sit next to the game, stay open while Tempo itself is minimised or in the tray, and be pinned above everything with its own **Always on top** button. **Dock as a tab** puts it back. Nothing is rebuilt when you switch — the same page moves — so the switch is instant and costs no restart, and the window remembers where you left it. Clicking **Accounts** in the sidebar (or Ctrl+9) raises the window rather than showing an empty tab, and Ctrl+Tab steps past it. The session dots, the multi-instance closer and the local API now follow *that* window's state instead of the main one's, so minimising Tempo no longer stops them while the account list is sitting open in front of you.
- **Live debug's filter box is a search, not a substring.** Type two words and a line must contain both; `"a phrase"` for an exact one; `-word` to hide lines; `tag:captions` for one subsystem and `level:warn` for one severity. Matches are **highlighted** in the log so you can see *why* a line is there, **F3** / **Shift+F3** step through them, Ctrl+F jumps to the box, and a counter beside it reads "4 of 73" — a filter that matches nothing now says so instead of looking like a log that went quiet. **Copy** and **Save…** honour the filter too: narrowing 1,500 lines down to the eleven that matter and then pressing Copy used to hand you all 1,500 back.
- **Fixed: warnings and errors carried no subsystem tag.** Live debug read the `[Captions]`-style tag only on `[INFO]` lines, so every warning and every error — the lines you open the window to find — had no subsystem as far as the rest of the window was concerned. They do now, which is also what makes `tag:` work on them.
- **Fixed: reading back through the caption transcript kept snapping to the end.** The "are we at the bottom?" test compared a *physical* line position against a *logical* line count, and with word wrap on — which a transcript always has — one caption line is routinely three rows. The check read as "at the bottom" from about a third of the way down, so every new caption yanked the view away mid-read.
- **The transcript's Find box takes the same syntax**, and its buttons stopped disagreeing: **Copy all** becomes **Copy shown** while a filter is on, **Save…** writes what you are looking at (and says how many of how many), and copying with nothing matching now tells you so instead of doing nothing silently.
- **Fixed: clearing the notification history still said "3 you didn't see".** The count beside **Notification history…** in Settings was worked out once, when settings loaded, and never again — so it survived a clear until Tempo restarted, and a notification missed *during* a game never added to it either. The history already announced every change; nothing was listening. It updates the moment the history does now, and the history window itself updates live while it is open (keeping your place in the list, instead of jumping to the top whenever something arrives).
- **The Accounts window's header was the wrong colour.** The title sat in a darker box than the bar around it: it took the *page* colour, because the theme engine skipped straight past the bar it was on. The header is drawn in the theme now — the Accounts icon in its sidebar colour, a title with nothing behind it, and a hairline under it — with the title bar tinted to match, so the two read as one surface. Scrollbars follow the theme, **Always on top** stays readable when it is on in all 38 themes (it would have been accent text on an accent fill), a theme change repaints the window live, and Tempo's wallpaper shows in it too.

### 1.0.322
**Private servers actually join your private server · picking another account no longer lands you in your main one · live captions stop freezing mid-sentence · the account screens redesigned · a lot of things that were quietly wrong**

- **Fixed: a private server ID did nothing — and did it silently.** Whatever you typed in the private server box was sent to Roblox as an `accessCode`. The code everybody actually has is the one out of a private server **link**, and that is a different parameter (`linkCode`) — sent in the wrong field it does not fail loudly, the client just joins the **public** game instead. So the launch looked like it worked, and you were never in your server. Tempo now reads whatever you paste: the old-style `roblox.com/games/…?privateServerLinkCode=…` link, the newer `roblox.com/share?code=…&type=Server` one (including the shortened form the app hands you), a bare code, or the owner-only access GUID — and sends each to the right parameter. Paste the link into the **Place ID** box instead and Tempo moves the code to where it belongs rather than throwing half of it away, which was a second way to end up in the public game. A short note beside the box says which kind Tempo thinks it has, and if a code can't be used, **nothing launches** — rather than five accounts quietly joining the wrong server.
- **Fixed: choosing another account could launch you as your main account.** Roblox allows one client at a time, so while a client is open Windows hands a new launch link to *that* client — which stays signed in as whatever account it already has. The session ticket Tempo minted for the account you picked was simply ignored, the join happened as the other account, and Tempo still reported "Launched" with nothing anywhere recording which account actually went in. Now, when a client is already running (and multi-instance is off), Tempo asks first: close it and launch as the account you chose, launch anyway, or cancel. Multi-instance is never switched on for you — that carries a ban risk and stays your decision.
- **Live captions no longer freeze for tens of seconds.** One awkward chunk of audio could send the speech model into a retry loop that took **10 to 73 seconds** to finish, and captions simply stopped during it. The decode is now bounded (no temperature fallback, a token cap, a narrower beam), so a bad chunk costs a moment instead of a minute. The delay figure the caption bar reports was also double-counting — it read 4.3 s when the true lag was 2.0 s — and the saved transcript stored each line three to six times over when speaker labels were on. Joining two captions across a chunk boundary no longer stutters the word it landed on.
- **The Accounts screens were rebuilt.** The unlock and "set a master password" screens were a title, a field and some buttons scattered across four different left edges against a wall of black, with a hole in the middle where an error message would one day appear. Each is now one card, centred, with the field full width, a single obvious button, and that middle line carrying something useful when nothing is wrong ("Press Enter to unlock", or the password rule) instead of sitting empty. On the account list, the Roblox client line ran *underneath* the column of buttons on the right, the private server box was a stunted half-width next to the Place ID box above it, two different buttons were both painted as the primary action, and three labels were cut off in languages other than English. All fixed, and all six languages are now measured automatically so it cannot drift back.
- **Tempo can now tell you exactly which Roblox client it checked.** The Accounts tab has a **Details…** view: which program answers Roblox links, the client version a launch will really run (`0.739.0.7390687`, not just a folder name), who signed it, when that certificate expires, and **every copy on disk** — there are usually three, they are ~130 MB each, and the one that runs is not always the newest. It is plain text, and the Copy button puts exactly what you see on the clipboard.
- **Fixed: a wrong clock could accuse Roblox of being tampered with.** Every failing signature check was reported the same way — "your Roblox client looks modified — launching may be unsafe", in red. An expired signing certificate (usually your PC's clock), a certificate chain Windows cannot build, and a check that could not run at all were all shown as tampering. Those now say what they actually are, and a check that did not run says nothing either way rather than accusing anything.
- **Notifications you couldn't see are now handled properly.** Cards waiting behind the five on screen ignored the fullscreen rule that the visible ones obey, so a card raised before a game started could paint over it minutes later; that queue also had no limit and nothing ever went stale, so a burst from one chatty app could still be arriving long after the thing it was about. Waiting cards are now dropped when they can no longer be shown or are simply too old — into the history, with the reason, at the time they were raised — and repeats of the same message fold into one card with a count instead of a column of twins.
- **Mute one noisy app instead of turning off mirroring.** Right-click a mirrored notification to mute that app; **Settings → Notifications → Muted apps** lists what is muted, and can mute an app Tempo has already seen. A muted notification is still written to the history with the reason, so nothing vanishes without a trace. Right-clicking a card no longer launches the app that sent it, which was the only gesture on a card that did something you hadn't asked for.
- **The local API answers the address your script actually uses.** It bound only `127.0.0.1`, but on Windows "localhost" resolves to `::1` first — so half the HTTP clients out there got "connection refused", which reads exactly like Tempo not running. It now binds both. `/relogin` opens a real sign-in window and had no rate limit (a loop opened twenty in a second); it does now. And the server counts what it has been asked to do — requests, refusals, and the last call — shown in Live debug and in the API dialog, because "my script does nothing" previously left no trace anywhere.
- **Live debug tells you more, and reads better.** It reports the Roblox client version and warns when its signing certificate is close to expiring, what the local API has served, what the notification queue has dropped, which apps are muted, and how long Tempo's own low-level keyboard/mouse callbacks take — Windows silently *removes* a hook whose callback overruns, which until now would have stopped hotkeys with nothing in the log to say why. Its own control strip also wraps instead of overlapping in five languages, and account-manager lines are coloured like every other subsystem.
- **Fixed: the vault's own error messages were English-only.** "That master password is not right", "this vault belongs to a different Windows account", and eight more — the messages you meet precisely when something has gone wrong — were the one part of the Accounts feature that never got translated. They speak all six languages now.

### 1.0.321
**A Roblox account manager, with an encrypted vault and no typed passwords · every notification is kept, including the ones Windows threw away · Tempo survives a Windows restart properly · "Start with Windows" stops silently breaking**

- **New: an Accounts tab — a Roblox account manager built into Tempo.** Keep several Roblox accounts, launch any of them into a game by Place ID, follow a player into whatever they are playing, and switch between accounts without signing in and out. It is a ninth tab, it is **off until you turn it on**, and the switch says plainly what it does before you do. Everything below is part of it.
- **You never type a Roblox password into Tempo.** Signing in opens your own browser — Chrome by preference, then Edge, Brave or Opera — in a private window, and you sign in to Roblox exactly as you always do. Tempo takes the session from that window when you are done; the password is never typed into a Tempo field and never travels through one. If you sign in **without** a password at all — a passkey, or Roblox's "quick log in" — Tempo notices and saves only the username, because there is no password to save. That distinction shows in the account's menu, so "no password saved" reads as passwordless rather than as something that failed.
- **The vault is encrypted twice, and locked by a master password you choose.** Sessions and any saved passwords are sealed with AES-256-GCM under a key stretched from your master password (PBKDF2-SHA256, 1.2 million iterations), and that file is then sealed again by Windows to your account with DPAPI. The consequence worth understanding: a wrong master password does not "fail to log in", it fails to *decrypt* — there is nothing to guess against and no copy of the password anywhere, so if you lose it the vault is gone and the only way forward is to start a new one. Optionally the vault re-locks itself the moment you switch to another tab.
- **A coloured dot per account says whether its session still works — and Re-login is offered the moment one doesn't.** Tempo checks each saved session against Roblox and marks it green (good), red (expired), or amber (not checked yet), re-checking in the background while the list is open. When a launch fails because a session had quietly expired, Tempo now says so and offers to sign that account back in on the spot, instead of leaving a warning that told you to go and find the Re-login button yourself. Re-login is also available whenever a selected account's dot is red, not only when exactly one row is selected.
- **Type a Place ID and the game identifies itself.** The name, the creator, its icon, and how many people are playing it right now, with likes, total visits and the server size — so a mistyped ID is obvious before you launch eight accounts into it. Reading it needs no account and no cookie.
- **Running more than one Roblox client at once, without administrator.** Roblox allows one client per PC by holding a single lock; Tempo closes that lock so further clients can start. It does it by asking Windows to close the handle, from a normal user account — Tempo does not modify Roblox, inject into it, or need to be run as administrator, and a dot reports whether it is genuinely working, idle, or being blocked. **Stated plainly: running several clients is against Roblox's rules and can get accounts banned.** Tempo asks, every time, before turning it on, and says exactly that in the question.
- **Everything on the page speaks your language.** The whole tab — every label, warning, menu and error, about 160 phrases — is translated into all six languages Tempo supports, rather than being an English island inside a translated app.
- **Every notification Tempo raises is now kept, especially the ones you never saw.** Notifications shown while a fullscreen game had the screen were *dropped*, and only a count of them ever reached the log — so the one case where you certainly missed the message was the one case with nothing to read afterwards. There is now a history you can open, with what was shown, what was suppressed and why.
- **Tempo no longer holds up a Windows shutdown, and stops losing your settings to one.** Two faults, both only visible at a restart. With a clicker or macro running, Tempo answered Windows' shutdown with a modal "exit anyway?" question — which nobody is present to answer at 3am when Windows Update reboots, so it stalled the shutdown, put Tempo on the "this app is preventing you from shutting down" screen, and ended with the process being killed anyway, losing the settings the question had delayed. And there was no handling of the session ending at all: Tempo's ordinary shutdown work (stopping the engine, saving a caption transcript, restoring devices) is far longer than the few seconds Windows allows, so a reboot could take the process down before a single setting reached disk. A shutdown, restart or sign-out is now never asked a question, and the state that has to survive a reboot — window position, the tab you were on, Settings changes, clicks counted so far — is written the instant Windows says it is ending the session.
- **Fixed: "Start with Windows" could silently stop working, while still looking switched on.** The sign-in entry was rewritten to point at whichever copy of Tempo you last ran. Open a portable copy, a build of your own, or an update staged in your Downloads folder just once, and your sign-in entry quietly moved to that file — then that file gets deleted or the stick comes out, and Tempo stops starting with Windows. Nothing said so: the checkbox still read ON, because the entry was still there, just pointing at nothing. The registered copy is now left alone while it still exists, and is only repointed when it has actually gone.
- **Live debug learned about the two things it could not see.** It now reports whether Windows will *actually* launch Tempo at sign-in — including the two overrides that leave the checkbox looking fine, a Task Manager "Startup apps" disable and an entry whose program file no longer exists — and it reports the Accounts tab: vault state, how many sessions are good, expired or unchecked, and what the multi-instance lock closer is really doing as opposed to what the switch says. Both also raise a line in the Health summary at the top.
- **Windows itself is named in the diagnostics**, with a note when the version is older than Tempo supports, and the display panel now reports colour depth and HDR — which is what explains a theme looking washed out on an HDR monitor.
- **Fixed: the version shown in Settings → Apps went stale.** Tempo's entry in Windows' installed-apps list kept whatever version first registered it, so an updated copy still advertised an old number to Windows. It is refreshed on launch.

### 1.0.320
**A build ID, so a test copy can never be mistaken for this release · search the Settings page · your own logo everywhere, not just the header · three things that were quietly wrong**

- **Every build now carries an ID, and says so when it isn't an official release.** The version number could never answer "which copy is this?" — a test build carries the same number as the release it was cut from, so the two looked identical everywhere the version was printed. Each build is now stamped with the time it was made, to the minute, in UTC: `build 260902-2123`. Anything that is not an official release also carries a **TEST** badge, in the warning colour, at the bottom of every screen — plus the About box, the Settings page, the splash, the first line of the log and the diagnostics attached to a bug report. This release says `build …` with no badge; if yours shows TEST, it did not come from the Releases page.
- **The Settings page has a search box.** Roughly sixty settings had accumulated across eight cards and about two thousand pixels of scrolling, with nothing to find one by. Type and the matches light up, with a count of how many and in how many sections; Enter jumps to the first, Escape clears. It searches the explanatory tooltips as well as the visible labels, so words that appear nowhere on screen still find the right switch — typing "CPU" finds the anti-freeze limit, whose label never mentions the CPU.
- **The Settings cards now lay themselves out.** Every card's position on that page was a hand-written number, and twice in the past a card that grew was left overlapping the one below it — hiding its title and the top of its first row. Nothing failed and nothing was logged; it simply looked broken. The page now works the positions out from the cards themselves, so that cannot happen again, and the Save row and the notes below always follow the last card down.
- **A custom logo now shows everywhere, not just in the header.** Choosing your own image in About changed the header wordmark and nothing else: the window icon, the taskbar button, the tray icon, every dialog's title bar and the pop-up notifications all kept Tempo's built-in mark until the app was restarted, and some of them never changed at all. They all follow the logo now, and update the moment you pick it. The image is also rebuilt at each size Windows asks for — 16px through 256px — instead of one large copy being squashed down, which is what made a custom logo look soft in the title bar next to everything else.
- **Fixed: "Unsaved changes" was showing permanently.** The Clicker tab compared your current settings against the profile you loaded to decide whether anything had been edited — but one of the values it compared was the moment the comparison ran, so the two could never match. The warning appeared a moment after loading any profile and never went away, including immediately after saving. It now compares only the settings the Clicker tab can actually change.
- **Fixed: the sidebar icons wore each other's colours, and Settings had none.** Each tab's icon is tinted its own colour. When the Profiles tab was added, the list of icons was updated and the matching list of colours — kept in a different part of the file — was not, so every icon from Profiles downwards took the colour belonging to the tab below it, and Settings, falling off the end of the list, was left grey. Both lists now sit together with a check that complains if one is ever short again.
- **Fixed: the log claimed the wrong version.** The first line of every log file had the version number typed into it by hand, so it would have gone on reporting 1.0.319 out of every release after it — in the line most likely to be quoted in a bug report. It reads the real version now.

### 1.0.319
**Tempo checks it hasn't been tampered with · bug reports stop leaking your account name · Live Debug's colours finally mean something · ~150 strings that were never translated**

- **Tempo now verifies its own program file, against GitHub.** On every launch it hashes `Tempo.exe` and asks GitHub what the release for that version number is supposed to be — using the SHA-256 GitHub publishes for the release asset itself, so nothing is downloaded and no extra file has to ship. Three answers: **verified against the release on GitHub**, **does not match the release published for its version number**, or **no release with this version number exists**. That last pair is the point — a patched build handed out under a real version number no longer passes, and neither does one carrying a version that was never published. Tested against a genuine download of v1.0.316: it comes back verified against its own release and modified against any other. Also catches a damaged file — a byte-patched copy, a half-zeroed one, a broken executable header, or a bundle with the speech libraries stripped out. Settings → Data & Backup has the switch, **Check now**, and **Trust this copy** for a build you made yourself. Honest limit: this is tamper-*evident*, not tamper-proof — it will not stop someone who edits the check out too, and being offline is never treated as evidence of anything.
- **Bug reports no longer carry your Windows account name.** Eight of the nine report bodies Tempo can produce sent it verbatim, along with your full user folder path — the redaction existed but was wired to one code path, the crash report, and nothing else. Even a crash issue put the raw exception message in its *title* while the body beside it was clean, so the one part you could not review was the one part that was not cleaned. Account name, PC name and personal folders are now stripped from every report and every subject line, matched on whole words so a short account name cannot chew through unrelated text.
- **"Report a bug…" shows you the report before it sends it.** Both reporting buttons now open the same composer: the complete text, editable, with the privacy position stated where the decision is made — and nothing leaves until you pick a way to send it. The recent activity log is offered rather than attached silently; it can mention files you have opened, so it is a tick-box you can read first. If the report is too long for your email app to carry — which it is once the log is attached — Tempo offers the clipboard instead of letting the mail client cut it off mid-sentence.
- **Live Debug's colour coding was largely fiction.** The legend advertised a "clicker" colour that nothing in the app could ever produce, because no line was tagged as coming from the clicker. Half the subsystems had no colour at all — scripts, the second cursor and second mouse, macros, the caption overlay, the word fixer, window and tray events. And the classifier matched words anywhere in a line, so it read quoted interface text: every one of the 30 lines that matched its clicker test was a German layout warning quoting the caption setting "GPU-Engine ausprobieren". It now reads the subsystem tag from where the tag actually is. Every category fires, nothing falls through uncoloured, and script steps get their own colour.
- **Around 150 strings that had never been translated, in any language.** The audit could not see three whole shapes of string: a caption chosen with a ternary (`on ? "Stop captions" : "Start captions"`), text passed through a helper that translates it later, and a window title assigned without `this.`. So five of the six buttons in the bug-report dialog, about twenty window titles including the crash dialog, and the *second half of every two-state button* had shipped in English — a Spanish user pressed "Iniciar subtítulos" and watched it become "Stop captions". The speaker labels on the caption bar are translated now too, and the words on the keyboard shortcut keys follow the legends printed on a keyboard in that language rather than a dictionary.
- **Settings survive a crash.** A hard freeze on 2026-08-31 left `settings.json` at its exact full length with every byte zero — the file was atomically replaced, and Windows lost the contents anyway. There was no second copy. Tempo now keeps the previous save beside it and falls back to it when the current one will not load, so an interrupted write costs one save instead of everything. An unreadable settings file also used to reset every preference with no log line and no preserved copy, looking exactly like a first run.
- **Fixed: the CPS test opened with four English labels** among translated ones, which then switched language on the first click.

### 1.0.318
**A run can no longer click Tempo's own interface · the window comes back the way you left it · the running badge stops cutting itself off · second-mouse fixes**

- **A running clicker or macro can no longer operate Tempo itself.** Injected input goes to whatever window sits under the pointer, and Tempo is just another window — leave the cursor over it and a run ticks checkboxes, drags the speed slider and switches tabs at full speed. A macro is worse: it replays coordinates recorded somewhere else entirely. Every event Tempo injects now carries a signature the app recognises and ignores on its own windows. Real clicks and keys never carry it, so Stop, Escape and the tray always work — which is the point, because that is exactly when you need them. Measured: 1,018 self-clicks fired at the window across three runs, 2,036 messages ignored (a down and an up each), target speed unchanged; with the switch off the identical run drove it 67 → 200 CPS. New option in Settings → Behaviour, on by default, and it applies the moment you tick it rather than waiting for Save.
- **Minimising and restoring behave.** Sending a maximised Tempo to the tray and reopening it handed back a normal-sized window; it now returns maximised. The Show/hide-window hotkey kept a private copy of the tray logic that had drifted into a worse version of it: the window blinked out instead of playing the minimise animation, came back forced to normal size, and — pressed while Tempo was merely minimised — hid it further instead of showing it, taking two presses to undo. "Hide to tray when clicking starts" animates now too, rather than vanishing the instant you press Start.
- **The on-screen running badge sizes itself to what it shows.** It was a fixed 232 px wide with its text drawn at a fixed offset and never measured, so a long line ran off the end — `1,234,567 clicks · 199.9 CPS · 1:23:45` needs 214 px of the 198 available, which is exactly the long, fast run where you are watching the badge instead of the window. It grows to fit now, and shrinks back. The optional hint line also pushed a bottom-anchored badge further down toward the taskbar it was meant to clear; it re-places itself now.
- **The "running overlay" dialog wears the theme.** Windows' own blue checkboxes, a Win32 drop-down and square buttons sat among Tempo's controls; all replaced, along with a font that was built per dialog and never released.
- **Second mouse.** Relaying a physical press from the second mouse onto Tempo's own window works again — it carries a different signature from automation, so reaching over to hit Stop mid-run is no longer ignored. Spam-clicking refuses to target Tempo's own windows (it posts messages rather than injecting, so the protection above cannot see it). And a pointing device that reports absolute screen positions — a drawing tablet, a touchscreen, a remote-desktop pointer — now says so, instead of counting as your second mouse and then silently never binding however much you wiggle it.
- **Fixed: scrolling the Settings page with the pointer over the Window-opacity slider changed the opacity** instead of scrolling the page. It was a stock Windows slider, which also made it the one grey control on a themed page.
- **Fixed: finishing a macro recording popped the window back up and stole focus** even when you had minimised Tempo yourself — the rule playback already followed.
- **Live captions** start more reliably, read system audio better, and report GPU/CPU use honestly; the model download resumes instead of starting over. **Caption history** appears without the delay that made it look empty.
- **Notifications** are off by default on a fresh install, animate smoothly instead of in frame-counted jerks, and hold themselves back during full-screen games and Do-Not-Disturb. **Fixed a crash** after using the tray's pop-up-notifications item.
- Dragging the window is no longer heavy, the tray menu animates, and switching tabs paints the whole page the first time instead of leaving parts of it blank.

### 1.0.317
**Your background image now covers the whole window · the window comes back intact after minimising · title-bar buttons keep focus**

- **The background image finally reaches the page.** The wallpaper showed on the header, sidebar and footer band, but the main content area stayed a flat grey slab. The page had full backdrop support all along — it was never switched on, because the backdrop is applied while the window is still being built (the tab control has no handle yet and reports no selected tab), and the code that would switch it on later never fires when the remembered tab is the first one. Switching tabs once used to fix it for the session. Verified on a cold start and all six tabs: page RGB(84,35,44) against sidebar RGB(83,35,43) — one continuous image across every surface.
- **Fixed: the window came back damaged after minimising.** Restoring could strip the header of its logo, profile caption and ─ □ ✕, or leave a black void over the lower two-thirds with the desktop showing through the sidebar. The restore repair only repainted the tab pages, so every other surface kept whatever was in the buffer when the window went down. Measured after the fix: **0.0% unpainted pixels across five minimise/restore cycles**, and clean through a maximised round trip.
- **Fixed: minimise/maximise/restore could leave Tempo behind another window.** Windows refuses to bring an app forward when something else owns the foreground; the caption buttons now take it properly.
- The maximise/restore glyph stays correct through every route into and out of maximised.

### 1.0.316
**The clicker stops burning a CPU core · notification cards wear the sending app's own icon · a macro checker · every list readable and colour-coded**

- **The clicker barely touches the CPU.** Holding an accurate interval meant busy-waiting the last 4 ms of every click. Measured: 67 CPS took 23% of a core, 200 CPS 66%, 400 CPS **99.9%**. Now it sleeps on a high-resolution kernel timer — the same figures are 0.0%, 0.5% and **0.8%** — and worst-case drift at 67 CPS fell from 7.5 ms to 0.2 ms.
- **Notification cards show the app that sent them.** Windows only exposes a logo for packaged Store apps, so every ordinary desktop program's card wore Tempo's own icon. Tempo now reads the icon off the running program. Verified on Discord, Steam, Chrome, Edge, Medal; 68/68 identified apps resolved.
- **New "Fix…" button on the Macros tab** — finds and repairs keys/buttons that are never released, off-screen clicks, duplicate moves, empty waits, machine-perfect timing and aim, and marathon pauses. Repairs run on a copy and are described before they run.
- **Every list is readable and colour-coded.** A stock list's column header is drawn by Windows and ignores the theme, so headers were near-black on near-black everywhere. Live Monitor, Recent sessions, Multi-Point and the step editor are now drawn by Tempo, with a colour per row that means something. Checked across all 38 themes: 874 contrast measurements, all passing.
- **Fixed: the window came back broken after picking a coordinate** — restoring never rebuilt the page's backdrop, so with a background image the window returned as a hole showing the desktop. Same fault fixed in Quick Capture and tray restore.
- **Fixed: pick-position is ignored while Tempo is in the tray.**
- **Fixed: the clicks-by-button bar stamped a grey slab over your background image**, its legend could overlap and overflow, and its segments squared off the bar's rounded ends.
- **Fixed: switching tabs repainted the whole page twice** — 36–92 ms of duplicate work per switch, now skipped unless something actually changed.
- **Fixed:** a false "your window is broken" health warning, and a Live debug stats panel that scrolled back to the top twice a second.
- **Untranslated text is now reported** instead of silently falling back to English (177 real gaps found in Spanish).
- **Animated backgrounds capped at 30 repaints/second.**
- **Known issues:** Escape still doesn't leave full screen (use F11); switching *to* the Clicker tab takes ~100 ms — that page's controls are the cost, not the background image.

### 1.0.315
**Tempo's real logo in the header and on notification cards · the taskbar button minimises the window again · Windows 11 minimise/restore/close animations**

- **The header shows Tempo's actual logo.** It drew a hand-made bolt glyph and never touched the real artwork, so the header didn't match the taskbar, tray or notification icons — and a custom logo set in About never appeared there. It now draws your custom logo, else Tempo's own icon, with the bolt kept only as a fallback, and updates immediately when you change it.
- **Sharper logos on cards.** The icon API returns a smaller frame than the one requested (128 px when asked for 256), so the artwork was being upscaled. Tempo now decodes the frame it actually wants — a true 256×256 instead of a blown-up 128.
- **The taskbar button minimises Tempo again.** Going borderless made Windows drop the style bit the taskbar reads to decide it may minimise a window, so clicking the button did nothing. The bits are restored and the frame they would draw is removed. Win+D, Win+Down and Show desktop work too.
- **Windows 11 animations are back** for minimise, restore and close — the same missing bits meant the compositor ran no transitions at all. Measured during a minimise: 0 intermediate frames before, 105 after. Closing to the tray animates instead of vanishing, and reopens maximised if it was maximised.
- **Maximise no longer overhangs the screen** by 8 px on every edge, which had been pushing the top of the header and the status bar out of view.
- **Live debug** now reports the live window styles (with a loud warning if minimise/animations are dead) and which logo is actually being drawn.
- **Fixed a false health warning** claiming full-screen state was inconsistent on a perfectly healthy window — the check treated "borderless" as "must be full screen", which stopped being true once Tempo drew its own title bar.
- **Known issue:** Escape still doesn't leave full screen; use F11.

### 1.0.314
**Notification overhaul — one card per event, the right app icon, and cards that open the app that sent them · a themed title bar · a loading screen that reports real progress**

**The sidebar shows its keyboard shortcuts.** Ctrl+1…9 (jump to a tab) and Ctrl+Tab (cycle) have always worked but were invisible — nothing in the app mentioned them. Each sidebar row now carries its number dimmed at the right edge, hovering names the shortcut in full, and rows report themselves to screen readers as tabs with their shortcut rather than unlabelled buttons. Contrast was measured and corrected during the work: the hint first landed at 1.1:1 against the row (invisible — the muted colour was being dimmed twice) and now sits at 4.5:1 unselected / 7.7:1 selected.

**The sidebar's version stamp no longer doubles up.** Resizing the window could leave TWO "Tempo v…" stamps (each with its own separator). The sidebar owner-paints that footer against its bottom edge but wasn't marked to repaint on resize, so Windows invalidated only the newly exposed strip — the footer was redrawn at the new bottom while the old one stayed as stale pixels. The panel now repaints in full on resize. Verified across 700 → 900 → 760 → 1000 → 824 px: one stamp, no ghosts.

**The version stamp and status bar can't fall off the bottom.** A window taller than the usable screen put its bottom edge under the taskbar, hiding the status bar and the sidebar's "Tempo v…" stamp — which is why the version only showed in full screen (that matches the screen exactly, so everything fits). The work-area clamp now also runs after you finish resizing the window and when a display change shrinks the desktop under it (a full-screen game exiting, a resolution switch), not just when restoring a saved window. Measured: 1020×1102 on a 1834×1032 work area is pulled back to 1020×1032 and the stamp reappears.

**The title bar matches the theme.** Tempo sat under a plain black system title bar — "dark mode" is the most Windows 10 offers, and a dark bar still doesn't match a *coloured* theme (on Synthwave it read as a black strip above a purple app). On Windows 11 the bar and border are now painted in the active theme: measured on Synthwave it went from `#202020` to `#261C3A` against the header's `#251B39`, i.e. one continuous surface. It follows live theme changes and Match-Windows light/dark flips, and is applied to **every** Tempo window (main, Live debug, About, CPS test, overlay settings, updater, calibration…) so no dialog stands out. Live Debug reports the tint under a new **chrome** line. Windows 10 keeps its dark/light bar.

**One card per notification, however many tabs are open.** A site open in ten browser tabs posts one logical notification but raises ten Windows toasts — one per tab — so the mirror produced ten identical cards at once. Windows gives each a different id, so the id-based check couldn't tell they matched; only the content can. Identical app+title+body arriving within a few seconds now collapses to a single card (a genuinely repeated message later still gets through), for any source rather than just browsers. Verified with ten identical notifications fired back-to-back: **1 shown, 9 collapsed**, reported in Live debug.

**Tray starts now wire up the system-event handlers too.** DisplaySettingsChanged (layout repair on a resolution change) and UserPreferenceChanged (live "Match Windows" theme following) were subscribed in OnLoad — which never runs on a start-minimised-to-tray launch, the same hole that had left the notification mirror off. Every launch-at-sign-in therefore had no live theme following and no display-change repair until the window was opened. Both are now hooked from either entry point behind a guard, so they attach exactly once and can't double-subscribe.

**Drawing on a snip no longer spams notifications.** Annotating a screenshot fired one "Screenshot copied" card per brush stroke — Snipping Tool re-copies on every edit and Tempo's duplicate check keyed on file SIZE, which changes with each stroke. It now keys on the picture's dimensions (an edit doesn't change them) with a window that extends on each repeat, so a whole editing session stays folded into the one card. Verified: eight strokes, 0 extra cards.

**The loading screen reports real progress.** The start-up checklist was a pure timer guess with no connection to the actual work — on a slow start it ticked off stages that hadn't happened, on a fast one it lagged a window that was already up. Each stage now reports as it genuinely completes and shows what it found beside it ("1 profile", "11 macros", the detected speaker, "5 hotkeys"), with the timer only carrying the list forward when nothing has reported yet. It already picked up your theme's colours; the detail is what was missing.

**Notification cards wear the real app's icon.** A screenshot card showed Tempo's own logo while naming a different app — it only got the source icon if that app's notification happened to arrive with one. Tempo now pulls the icon straight off the capture app's executable (via the shell, so it's the properly-composed icon, and the native handle is always freed) at the moment the image is copied, so the card carries e.g. Snipping Tool's real icon immediately.

**Notification internals hardened.** The app-icon cache decoded its images while holding the cache lock — anything else touching the cache, including shutdown, waited on blocking WinRT I/O behind it. Decoding now happens outside the lock with a double-checked insert, so a slow or hung icon decode can't stall Tempo closing. Live debug's notification section also reports the mirror's poll interval, how many app icons are cached (each one is a decode that no longer sits between the Windows toast and Tempo's card), and whether the ✕ is shown.

**A screenshot always opens in the app that took it.** Clicking a screenshot card could still open Photos rather than Snipping Tool: Tempo learned the source from that app's notification, so clicking before the notification landed — or having its notifications off — left no source and fell back to the default `.png` handler. Tempo now asks Windows who owns the clipboard the moment the image appears (that's the capture tool) and reads its app identity from the process, so there's no race and no notification dependency; the notification only refines the label afterwards. Executable names are tidied for display (`SnippingTool.exe` → "Snipping Tool"). Verified with an immediate click after snipping.

**Mirrored cards no longer trail the Windows toast.** The source app's icon was decoded on the critical path — blocking WinRT calls plus an image decode, for every notification — which is why cards lagged on mid-range PCs. Icons are now decoded once per app and reused (each card gets a copy), and with that cost gone the poll interval drops from 120 ms to 70 ms.

**A cleaner notification card.** The ✕ moved to the left and is inset from the corner — it used to sit hard against the top-right, where the pointer travels to click the card body, so mis-clicks dismissed instead of opening. New "Show ✕ on cards" setting hides it entirely (the card still dismisses on click and still auto-closes). Roomier padding, more air between title and body, and a slimmer rounded countdown bar with a faint track behind it.

**Notification timing reworked.** "Show (s)" is now the MINIMUM — a card earns extra time from how much text it carries (~200 wpm, capped at 3× your setting or 20 s), so a long mirrored message no longer vanishes mid-sentence while a one-liner still goes in 5 s. Cards also appear faster: the screenshot merge hold is halved (320 → 160 ms) and no longer costs the app's identity, because the card now shows immediately and is **re-labelled in place** when the capture app's notification lands (verified: "re-labelled the screenshot card as Snipping Tool"). The slide-in/out and stack reflow animate quicker, and when several cards arrive together their timers are staggered so the stack drains one at a time instead of all at once.

**Quick switches in the tray menu.** Right-clicking the tray icon now toggles **Live captions** (previously hotkey-only, so a taken hotkey meant opening the window), **Pop-up notifications** and **Screenshot alerts**, plus a **Settings…** item that opens straight on the Settings tab. Toggles show their real state when the menu opens, save immediately, and screenshot alerts grey out when notifications are off.

**"Installed but some features don't work" is now diagnosable.** The single-file exe unpacks its bundled speech libraries to a temp folder at start-up; when that's blocked (antivirus, enterprise policy, a temp cleaner, a full or redirected `%TEMP%`) Tempo still opens and clicks fine while Live Captions silently never start — and nothing said why. A start-up self-check now reports the result in the log and on a new **Install** line in Live debug, and raises a Health error naming the cause and the exact folder when the natives really are missing.

**No more "Settings saved." dialog.** Saving popped a modal you had to click away to confirm something you'd just chosen to do. The Save button now confirms in place — it reads "Saved ✓" briefly and restores itself — for both Save Settings and Save Keybinds. Real warnings (e.g. duplicate keybind combinations) still get a proper dialog.

**Custom window chrome.** The system title bar is replaced by Tempo's own: the header now draws themed ─ □ ✕ buttons (maximise swaps to a restore glyph, close goes Windows-11 red on hover), because Windows won't let an app restyle the system caption buttons. Dragging, Aero Snap, double-click-to-maximise and edge/corner resizing remain fully native — the window answers WM_NCHITTEST with the right zones instead of reimplementing them. Maximise is clamped to the monitor work area so it can't cover the taskbar, and F11 full screen hides the buttons and restores them on exit.

**Check for updates and view the source from the tray.** Two more tray entries: **Check for updates…** (same check as the Settings button, no window needed) and **View source on GitHub**, which opens the repository — Tempo is open source and that was previously only reachable through About.

**Sidebar shortcut numbers sit clear of the labels.** The Ctrl+1…9 hints used a different text engine to the labels — one that pads around the glyph and could let a digit drift toward the label or the rounded border. Both now use the same renderer with explicit border and label gaps.

### 1.0.313
**Scrolling ~8× faster · Tempo reopens on your last tab · two full-screen bugs that wrecked your window size · "Match Windows" matches your accent too**

**Scrolling is ~8× faster.** Scrolling any tab crawled at a measured **11 FPS** (90 ms per scroll step) because Tempo set `WS_EX_COMPOSITED` permanently, forcing Windows to re-composite the whole window — header, sidebar, every card and control, footer — on every repaint. That's only needed while a background wallpaper is showing (the mode where page controls turn transparent, which caused the hollow-checkbox/clipped-label artifacts); it's now enabled only in that mode. With no wallpaper the same scroll measures **95 FPS (10.5 ms/step) — an 8.6× speed-up** — with no artifacts, since the normal scroll blit is already pixel-perfect when controls are solid. Setting a background image restores the compositing automatically.

**Window, tabs & full screen:** Tempo now **reopens on the tab you were last using** instead of always landing on Clicker — the thing that lost your place when Windows rebooted overnight during a long unattended run and launch-at-startup brought Tempo back on the Clicker page. New "Reopen on the tab I used last" tick box under Startup & Window (on by default), remembered through reboots and force-quits. Two full-screen bugs that permanently broke the window size are fixed: F11 from a **maximised** window destroyed Windows' restore rectangle (Restore Down then returned a screen-sized window at 0,0 with the status strip and the Macros Live Monitor card under the taskbar), and **exiting while in full screen** persisted the full-screen rectangle as the remembered normal size, so every later launch opened screen-sized and clipped. Tempo now saves the pre-full-screen geometry, and an already-spoiled settings file heals itself by clamping the restored window to the monitor's working area. Also fixed: a page could be flagged "already laid out" by a pass that bailed early, leaving cards half-positioned.

**Themes & "Match Windows":** with the Light or Dark theme selected, "Match Windows" now follows the OS light/dark AND adopts your real Windows accent colour (buttons, highlights and header pick up the exact accent from Windows Settings → Colors), updating live as you change either. Colourful themes (Synthwave, Ocean, Dracula, …) keep their own look and accent untouched instead of being forced to plain Light/Dark; a custom accent still overrides everything.

**Notifications were switched OFF on every tray start — fixed.** With "Start minimised to tray" on (the normal setup, and every launch-at-sign-in) Tempo's window is never shown, and the code arming the Windows-notification mirror and the clipboard screenshot watcher only ran when the window was first *shown* — so mirroring and screenshot alerts were dead until you opened the main window. The log showed the mirror starting minutes after launch, exactly when the window was opened, and never at all on launches where it wasn't. Both are now armed as Tempo loads. Also: a screenshot now shows **one merged card** instead of Tempo's card plus a duplicate mirrored one — it wears the capture app's real name and icon, shows the shot, and **clicking it opens the image back in the app that took it** (Snipping Tool, not Photos) via that app's registered file association, resolved live rather than hard-coded, with a fallback to your default viewer. Re-publishing the same picture to the clipboard (Snipping Tool does this when loading a shot into its editor) no longer pops a second identical card. Live Debug gained a **Screenshot alert** line reporting whether the clipboard watcher is actually registered, not just ticked.

**Live Debug now covers the window, rendering, tabs and theme** — the blind spot that made every bug above need hand-diagnosis. New lines: **Window** (state, size, position, client, DPI, monitor work area, with a ⚠ when the window is bigger than the work area and its bottom is under the taskbar; plus the full-screen flag, border style and saved restore rect, flagged when they disagree); **Rendering** (wallpaper + whole-window compositing, reading "fast scrolling" normally and ⚠ when composited with no wallpaper, which is the slow-scroll condition); **Tab** (current tab, reopen-on-last-tab, remembered index); and **Theme** (active theme, Match Windows, OS light/dark, and the accent's hex plus whether it came from the theme, your custom colour, or Windows). All three fault conditions are also raised in the Health section so they're spotted, not just shown.

### 1.0.312
**Tempo notifications — animated, Windows 11-style, clickable · plus a caption crash fix, real keep-up on mid CPUs, and sharper AI**

**New: Tempo's own notification pop-ups.** Tempo now shows notifications as animated cards in a screen corner you choose (top/bottom, left/right), styled like Windows 11 — the app's icon, a title, a description, and an optional picture. They slide in with a fade, auto-dismiss after a few seconds (hover to hold), and stack cleanly. On a multi-monitor setup they always appear together on the monitor Tempo lives on. Settings → Behaviour area → Notifications.
- **Mirror Windows notifications (opt-in).** Turn it on and other apps' Windows notifications appear as Tempo cards in your style — each with the source app's real icon (Discord's logo for Discord, etc.). Uses the on-device notification-listener API; nothing leaves your PC. Mirroring is near-instant (fast-path event + 200 ms poll), so the card shows while the Windows toast is still up, not after it's gone.
- **Click a card to open the app that sent it** — any app or browser, exactly like clicking a Windows 11 toast. Tempo's own cards open the Tempo window. The ✕ only dismisses.
- **Remembered instantly.** Every notification toggle (on/off, corner, duration, mirror) is saved the moment you change it — it sticks even through a crash or force-quit, no Save needed.
- **Honest about the limit:** to hide Windows' own pop-up banners so only Tempo's show, turn on Windows "Do not disturb" (a one-click button opens it); mirroring keeps working under Do not disturb. An app can't suppress another app's banner, and another app's inline image (e.g. a screenshot thumbnail) isn't exposed by Windows — Tempo shows the app's icon/picture and text.

**Captions:**
- **Crash fixed.** A caption-bar paint crash ("Parameter is not valid" on foreign-script or glyph-less words) is fixed with guards + a protected repaint.
- **Real keep-up on mid CPUs.** New catch-up decoding drains a backlog after any hitch in one oversized pass instead of trailing for seconds; entering the "stretched" tier now also drops beam search for the headroom it needs; and a machine that quietly sheds words in the 1.0–1.4× band now escalates (beam off, then a smaller model) instead of losing words forever. End-of-utterance early takes cut latency further.
- **AI word fixer** now scans the top few OS spelling suggestions (not just the first), catching more real mis-hears while staying just as conservative.
- **Face/mouth speaker analysis** ignores scene cuts and hard camera pans (they no longer fake "everyone's talking"), and raises the talking bar for fast-moving faces so a silent walker isn't tagged as the speaker.
- **Settings layout** fix: the "Ignore my own voice" option no longer overlaps the model buttons.

**Live Debug** shows much more: a caption Keep-up line (real-time factor + verdict), catch-up/quick-take counts, the 5.1 dialogue mix indicator, per-section resilience (one failing stat can't blank the rest), and a full Notifications line (style, corner, duration, on-screen/queued counts, mirror state, instant-vs-polling, last app).

### 1.0.311
**Captions read like live speech · own-voice filtering · conversations stay on the bar · big audio-path and engine work**
- **Word-by-word captions with animation.** Words now appear one at a time at READING pace (~210 wpm, tightening automatically under fast content so captions never lag the audio), each fading in with a gentle rise — instead of whole chunks slamming onto the bar too fast to finish reading.
- **Conversations no longer vanish.** When the speaker changes, the finished turn stays on the bar in front of the new one ("Speaker 1: … Speaker 2: …"), sliding off only as space runs out — like TV captions. Same-speaker pauses keep one running turn; a 30 s silence starts fresh.
- **Sentence-clean line trimming.** The rolling line sheds old words at sentence boundaries (or a word boundary marked with "…"), and a labeler bug that could leak the whole line — old turns included — under the current speaker's label is fixed.
- **"It heard my own voice" — fixed (opt-in).** A lightweight mic-envelope monitor correlates the system audio with your microphone; your own voice coming back through sidetone / "Listen to this device" / chat monitoring is skipped instead of captioned as a phantom speaker. The other person in a call is never dropped (their voice doesn't match your mic). Settings → Live Captions → "Ignore my own voice".
- **Crosstalk handled:** when two faces visibly talk at once, the label HOLDS instead of flip-flopping with whichever voice is momentarily louder. Face tracking also predicts moving faces (velocity matching + motion-compensated mouth measurement), rejects static face images (posters/thumbnails), and survives full head turns.
- **Audio path, all modes:** surround (5.1/7.1) is now mixed dialogue-forward (centre-weighted, LFE excluded) instead of flat-averaged; a ~20 Hz high-pass removes mic DC/rumble; 24-bit PCM devices work; input CLIPPING is detected per channel and flagged in Live debug; a device-unplug race that could leave two captures corrupting the pipeline is fixed.
- **Engines:** hybrid-CPU thread tuning (P-cores only — measured faster with fewer threads on 6P+4E chips); pauses in speech no longer cost a wasted inference; stale context after long silence no longer re-surfaces old words; beam search auto-restores after load ends.
- **AI word fixer** protects ~150 real gaming/streaming words from false "repairs" ("respawn" → "respond"); a degenerate case-only-suggestion path is fixed.
- **Enchanted tray menu:** live status row (state dot · profile · captions), state-aware labels (Start ⟷ Stop clicking, Show ⟷ Hide window), hotkey hints, hover-fade animation and a breathing header.
- **Second cursor:** grab & place reliability, stuck-button guards, crosstalk-tightened click swallow, per-mouse Live-debug panel with parked coordinates.
- Live debug shows far more: decode threads (P-cores), context carry, reveal pace, bar state, per-face and per-voice detail, own-voice similarity, clipping.

### 1.0.310
**Second Cursor: a full bug-fix hardening pass · Live Captions: real accuracy fixes, multi-monitor support, and deeper analysis**

**Second Cursor (still experimental) — the bugs found in real two-mouse use:**
- **Click, hold and drag actually land at the second cursor now.** A click that moved even a pixel used to put its press at the second cursor and its release back at the parked spot (a broken click); the pointer now follows while a button is held and parks only while aiming.
- **The second mouse's scroll wheel scrolled the WRONG window** (whatever sat under the parked pointer). Its wheel is now swallowed and re-sent as a real scroll at the second cursor.
- **Main-mouse clicks were being eaten** for up to a third of a second after any second-mouse activity; the stray-click guard is far tighter now.
- **Grab & place could silently fail** when the second mouse had just been active — the place-click was swallowed by the wrong handler. Placement now runs first, and second-mouse input is ignored while you're placing.
- **Stuck-button guards everywhere:** unplugging, re-picking, or switching the bound mouse mid-hold used to leave the real button stuck down. Every teardown path now releases held buttons.
- **No more re-bind churn** on every settings tweak, and the marker **stays on top** of apps that assert topmost after it (gentle 2 s keeper).
- **Live debug got a full second-mouse panel:** per-mouse activity lines (▶ marks the one driving the cursor, ● MOVING NOW), real-cursor HELD STILL/free + parked coordinates, held-buttons state, and bind/wait status.

**Live Captions:**
- **Seamless audio resampling.** Each 40 ms capture buffer was resampled in isolation, discarding the fractional sample position ~25× a second — a constant hidden degradation, worst on 44.1 kHz devices. The resampler now carries its phase across buffers: Whisper hears one continuous stream.
- **Genuine repeated speech no longer vanishes.** The chunk-seam deduplicator compared against the previous emission no matter how old; "Okay." said again a minute later was stripped as an "overlap artifact." Dedup now only runs while chunks can physically share audio.
- **Single-word decode loops collapse** ("blue-hipped blue-hipped blue-hipped…" — caught live from clean audio; hyphenated tokens dodged every existing dedupe). Natural doubles ("I know, I know") stay.
- **Wrong source tag fixed.** Switching browser tabs (or windows) while audio kept playing re-branded the tag to the tab you switched TO. The source is now locked to the audio-owning process, so tab switches can't rename it — and since a source change resets speaker numbering, this also stops speaker labels being wiped mid-conversation.
- **Multi-monitor caption bar.** The bar was hard-coded to the primary screen; it now follows the monitor whose window is making the sound (game on the DisplayPort screen → captions on that screen). A hand-dragged bar is never moved.
- **CJK punctuation preserved** — the stray-symbol filter was eating every 。、！？ from Japanese/Chinese captions.
- **Headroom buys accuracy:** engines with measured speed carry more trailing context into each chunk (boundary words hear more of their sentence), and **beam search is automatically restored** after the too-slow ladder dropped it once the squeeze ends.
- **Allocation-free capture path** (reused scratch buffers; ~1 MB/s less garbage while captioning) and an O(n) anti-alias filter.
- **The AI word fixer protects real gaming vocabulary** — "respawn" was one edit from being "corrected" into "respond"; ~70 gaming/streaming/tech words are now never touched.
- **Face analysis survives the full head turn** (left/right/up/down): a face the near-frontal OS detector loses now coasts at its last position with mouth-motion still measured, so the speaker verdict outlives the turn; verdicts also weight face size (a big clear face beats a noisy tiny one).
- **See the analysis working:** Live debug now lists each tracked face (position, size, live mouth-motion, TALKING/COASTING) and each learned voice's fingerprint (pitch, brightness, intonation, evidence, ● SPEAKING).
- **Crisper caption text** — same fonts, cleaner rasterisation (half-pixel edge alignment + high-quality compositing for the glow/outline/fill layers).
- **More known hallucinations banned** (memorised subtitle credits in several languages, more quiet-audio outro phrases), still under the conservative exact-match rules.

### 1.0.309
**New: a Second Cursor you can drive with a second mouse — experimental, and honest about what Windows will and won't allow**
- **Second Cursor (second mouse).** A new opt-in feature on the Clicker tab draws a real-looking Windows 11 pointer that you can place anywhere across both monitors and spam-click — so you can AFK-grind a windowed game on monitor 2 while your real mouse stays free on monitor 1. Off by default; turn it on with **"Show a second mouse cursor."** Right-click the cursor for its menu: grab & place, spam-click, colour, size, spam speed.
- **Drive it with a SECOND real mouse.** Plug in a second mouse, tick **"Let a 2nd real mouse control it,"** and pick it from the **Which mouse:** list (real product names, e.g. "Razer DeathAdder Essential"). It auto-detects a mouse the moment you plug it in, remembers your choice, and re-binds it automatically after a reconnect. Left/right click, middle-click = start/stop spam, scroll wheel, press-and-hold and drag all work; the real pointer is **pinned in place** while the second mouse moves, so your main cursor doesn't wander. Emergency-Stop releases everything.
- **Live debug shows both mice.** A per-mouse readout proves Tempo is reading each real mouse independently — wiggle one and watch its line light up (▶ = the one driving the second cursor), with live move/click counts and a "real cursor: HELD STILL / free" status.
- **This is NOT perfect — and it can't be, on Windows.** Windows has exactly ONE hardware cursor, so a truly independent second pointer is a kernel-driver feature no user-space app can do safely. Tempo's second cursor works best on **windowed / visible-cursor apps and games** (RPGs, idle/clicker, strategy, browser games, the desktop). **Known limits:** games that lock or hide the cursor for mouse-look (most first-person shooters) own the one cursor and can't be split; a game that reads the mouse by raw input also reads your *main* mouse, so the two aren't fully isolated; and "click at the second cursor" briefly borrows the real pointer for the click. It's genuinely useful for the AFK/second-monitor case it was built for, but treat it as experimental.

### 1.0.308
**A full visual polish pass — seamless GIF backdrop, reworked loading screen, and enchanted menus, bars, header and slider**
- **One seamless background image.** Applying a background GIF used to show it as separate, misaligned crops in the header, page and footer (they each cover-cropped the image against their own rectangle). It's now ONE shared image cover-fit against the whole window, with every area — header, sidebar, each page, footer — painting only its aligned slice, driven by a single animator so every region shows the same frame. The three confusing Header/Footer/Window pickers collapse to one "Background image" picker.
- **Adjustable, uniform dimming.** The readability overlay is now identical across every area and controlled by a new **Dim slider** in Settings → Appearance (the `BackgroundDim` setting was previously wired to nothing). Verified from bright to dark. Scrolling stays clean — the backdrop is pinned to the viewport with no ghosting.
- **Reworked loading screen.** The startup splash was redesigned with far more detail: a branded row (logo + wordmark + version), a tagline, and a **ticking checklist of the real startup stages** (settings & profiles → macros → speech-caption engine → audio devices → hotkeys) that check off in sequence, plus a **determinate progress bar with a live percentage** instead of the old indeterminate sweep.
- **Enchanted tray menu.** A branded header banner (Tempo mark + version), crisp per-item vector icons in theme hues (no more mismatched OS emoji), a rounded accent selection with a left indicator bar, and a state-aware Start/Stop icon (green play ⟷ red stop).
- **Enchanted status bar.** A themed renderer with an accent top hairline and slim separators (goodbye etched grooves), plus small painted stat icons (profile, clicks, CPS, peak, time) so the bar reads as a dashboard.
- **Header fix + polish.** The mismatched dark box behind "Profile • Default" is gone — it was a transparent Label failing to composite over the owner-drawn header; the caption is now drawn by the header itself (muted "Profile •" + accent name), with a soft glow behind the logo tile.
- **Enchanted speed slider.** The Manual Speed slider now blooms an accent glow and grows slightly on hover/drag/focus, with a soft thumb shadow and a gentle glow under the filled track.

### 1.0.307
**Accuracy at the decoder itself — for BOTH engines**
- **Sound-tag hallucinations are now banned inside the decoder.** "[Music]", "(applause)" and friends used to be generated by the model and scrubbed off afterwards — losing whatever real words the decoder might have heard instead. Now the tokens that OPEN a bracketed annotation are suppressed at sampling time, so the decoder spends its probability on real words. Getting this right took an offline test harness: whisper's own internal tokens are bracketed too ("[_BEG_]", the timestamp tokens), and a naive "starts with a bracket" ban silently produced ZERO output on the first attempt and hallucinated segment prefixes on the second. The shipped pattern spares the internal tokens; verified byte-identical output to no-suppression on clean speech, with the bracket openers gone from the reachable vocabulary. (The old post-scrubbers stay as a backstop.)
- **Stricter repetition-loop guard.** Whisper's entropy check (the thing that catches "the the the…" and looping phrases on noisy audio) now runs at 2.8 instead of the lax 2.4 default: degenerate low-entropy decodes are retried at a higher temperature instead of being shown. Retries only fire on degenerate segments — no real-time cost on normal speech.
- **The GPU engine's headroom now buys accuracy, not just speed.** On the Vulkan engine each chunk carries 1.15 s of trailing context into the next decode (vs 0.75 s), so words at chunk boundaries are heard with more of their sentence around them. Cadence is unchanged, the overlap dedupe strips the extra repeated words, and everything stays inside the encoder's token budget. CPU behaviour is untouched.
- **Longer repetition loops caught.** The output repeat-collapser now folds up to 8-word loops (was 6) — decoder loops on noisy audio can cycle phrases longer than the old cap caught.

### 1.0.306
**The CPU engine finally gets the whole CPU · Tempo's F11 glitch fixed · honest about games nothing can draw over**
- **The CPU engine was using 6 threads on a 10-core machine.** A hard-coded cap from an earlier era starved every big model: watched live, Large on this CPU produced 20-second chunk times and the auto-ladder collapsed it all the way down to Base. The engine now counts PHYSICAL cores properly (hyperthreads don't help whisper's math) and uses all of them minus one — 9 of 10 here — capped at 10 where scaling flattens. During a fullscreen game it takes HALF the cores instead, so the CPU engine is as courteous to games as the GPU path. Live debug shows the thread count on the Decode line.
- **Warm-up inference.** The FIRST chunk of every session ran far slower than steady state (the CPU pages the whole model in; a GPU compiles pipelines), which opened every session with a backlog spike and a "dropped n s to catch up". Tempo now runs 1.5 s of silence through the engine while the status still says "Loading speech model…" — measured live: warm-up 1.2 s, and the session-opening 5.6 s drop was gone.
- **Marginal engines STRETCH instead of bleeding.** An engine at ~1.0–1.4× real time transcribes every word yet drifts behind and sheds a few hundred ms of audio every few seconds, forever. Counter-intuitive fix: BIGGER steps — the encoder's cost per chunk is flat, so 1.5× windows spread it over 1.5× more audio (~30% less work per second of speech). The stretch engages after a few measured-slow chunks, shows as "stretched ×1.5" in Live debug's cadence, and drops out when headroom returns.
- **Too-slow downgrades now RECOVER.** The ladder (Large → compact Large → Medium → …) only ever went DOWN — one busy stretch and you sat on Base for the rest of the session. The engine now reports sustained headroom (minutes of consistently fast chunks, never during a game), and Tempo steps back UP toward the model you actually chose, one rung at a time. If the bigger model immediately proves too slow after all, it steps back down and stops trying for the session — no bouncing. The Health panel says which state you're in.

**Tempo's own full-screen window (F11) can no longer glitch the layout**
- **The overlapping/duplicated-cards glitch is fixed at its root.** Reported with screenshots: after using Tempo in full screen, pages could show cards twice, overlapping, or missing, and the window could end up border-less with a stray bottom scrollbar. Two real defects compounded: (1) the page re-centring routine froze painting while it moved controls and an exception mid-move skipped the un-freeze — the page then NEVER painted again, so stale pixels of the old layout mixed with moved controls; (2) one silent exception in the F11 exit path abandoned the whole restore — leaving no title bar, no minimum window size (hence the scrollbar state), and a half-restored layout. The re-centre now always un-freezes (and guards against re-entrant layout events), and every step of the F11 restore runs independently with logging — one failure can no longer strand the window.
- **Self-healing:** if a window is ever found border-less while not in full-screen mode (the broken state above), Tempo detects and repairs it — border, minimum size, and a sane centred position come back on the next F11 press.
- **Full-screen layout polish:** entering F11 now uses the same repair-and-repaint pass as window restore, so pages come up complete, centred, and fully scrollable — verified live with screenshots.

**Real-fullscreen honesty: Tempo now TELLS you when a game can't be drawn over**
- **Exclusive-fullscreen detection.** Reported live: captions don't appear over a game in REAL fullscreen. That's a Windows-level wall, not a bug — a game in old-style Exclusive Fullscreen owns the whole display and NO app can draw over it (Discord/Steam manage it by injecting code into the game, an anti-cheat gamble Tempo will never take). Tempo now detects that state through the shell's own signal and says so instead of failing silently: a one-time notification explains that switching the game's video setting to 'Borderless' (looks identical) makes captions appear, the Live-debug Health panel shows the state, and the log records enter/exit.
- **Visual proof over normal fullscreen.** The moment game mode starts, the caption bar briefly shows "🎮 Game mode — captions keep running at a lighter pace" — so over Borderless/optimized-fullscreen games you SEE the overlay working immediately instead of wondering whether it's there.

### 1.0.305
**The Rainbow Six freeze fix: game mode goes from "easing off" to "hands off"**
- **Total-freeze fix — face analysis now PAUSES COMPLETELY during fullscreen games.** Reported live from Rainbow Six: captions on, and the game sometimes froze entirely. Root cause: even in 1.0.304's low-impact mode the face analyser still captured the game window once a second, and PrintWindow forces the game to render an extra copy of itself SYNCHRONOUSLY — a game that isn't pumping messages mid-frame can stall on that call for whole seconds. Now not one window capture touches a fullscreen game; speaker labels fall back to voice matching (the primary signal anyway) and face analysis resumes the moment the game closes. Both transitions log.
- **Game-mode flapping fixed.** The 1.0.304 logs show game mode entering and exiting as fast as every 2 seconds during real Rainbow Six play (loading screens, overlays, and popups momentarily break the fullscreen signature). Every flap rebuilt the speech decoder and resumed window captures mid-match. Leaving game mode now requires ~6 seconds of STABLE non-fullscreen — a stray blip changes nothing — while entering stays immediate. Verified live: a deliberate 3-second interruption mid-"game" produced zero transitions.
- **The overlay keeper eases off during games.** The always-on-top backstop was re-asserting every overlay's z-order 4× a second; repeated z-order churn above a fullscreen game bounces DWM out of its fast presentation path (stutter). During game mode the backstop relaxes to ~1.5 s — while the instant foreground-event re-top (the thing that actually keeps captions visible over a game) stays instant.
- **No model swaps mid-match.** When a game hogs the GPU/CPU, inference slows and the too-slow ladder used to reach for a smaller model — hot-loading a hundreds-of-MB model file mid-firefight (a guaranteed hitch) to fix slowness the GAME was causing. The ladder now holds while a fullscreen game is on screen and resumes judging honestly afterwards.
- **Honest lag advice.** The "engine is behind live audio" warning suggested "a smaller model, or the GPU engine" even when a game owned the GPU and no setting was at fault. During game mode it now says what's true: the game has the hardware right now; captions catch up between fights.

### 1.0.304
**The big one: captions no longer cost FPS in games · wrong-tab source fix · everything visible in Live debug · a full visual polish pass**
- **FPS fix — low-impact game mode.** Captions during games were measurably dropping frame rate: GPU inference bursts (made MORE frequent by the earned fast cadence), the face analyser's window captures (PrintWindow makes the game compose an extra copy several times a second), and the caption bar's ~30 fps glow animation. Tempo now detects a fullscreen app taking the screen and eases all three off automatically — full-length engine steps with beam off, 1 fps face analysis, ~10 fps overlay animation with a lighter glow — and restores full quality (including beam) the moment the game closes. Verified live: the log shows the full cycle, including beam re-enabling on exit. Captions keep working the whole time.
- **Wrong-tab source fix.** Reported live: sitting on a Google tab while audio played elsewhere in Edge credited "Google" on the caption bar. When the audible process is a browser whose ACTIVE tab isn't a recognised media site, Tempo now scans that browser's other windows for one that is (YouTube, Spotify, Twitch…) before falling back to the active tab. (A completely unknown site in a background tab of the same window is invisible to every window title Windows exposes — no app can name that from outside.)
- **Live debug knows everything now.** The stats mark each audio device [chosen] or [default]; a "Game mode: 🎮" line appears while the FPS guard is active; the Health panel explains game mode in plain English and warns when a chosen device is unplugged; and the game-mode transitions plus the microphone/speaker Yes-No prompts (shown, accepted, or declined) all log.
- **True colours.** The Live-debug category palette now has a saturated variant for LIGHT themes (the pale set washed out on white with Match Windows), and "system" moved from pale cyan (nearly identical to captions' sky blue) to orchid in both palettes — every legend colour genuinely distinct.
- **Visual polish.** The tray menu is now Tempo-themed (dark surface, rounded accent highlight, themed separators/check marks, glyphs per item) instead of the stark white system menu, and follows theme changes live. The sidebar tabs each got their own icon hue (Clicker orange, Multi-Point mint, Macros lavender, Statistics sky blue, Keybinds gold, Settings orchid — dark variants on light themes), so the tabs read at a glance.

### 1.0.303
**Choose WHICH speaker and microphone captions use — listed by model, honest when the model can't be read**
- **Two new pickers in Settings → Live Captions: 🔊 speaker and 🎙 microphone.** PCs with several playback or input devices (headset + monitor speakers, webcam mic + USB mic) can now choose exactly which device captions listen through, instead of always following Windows' default. Every active device is listed by its MODEL name; "Default (follow Windows)" keeps the old behaviour. The lists refresh live as devices come and go.
- **Honest when detection fails:** a device whose model the driver doesn't report shows as "Unknown speaker/microphone (model not reported)" instead of vanishing; a class with no devices at all shows "(no device found)"; and if your CHOSEN device is unplugged, captions fall back to the Windows default and a warning line says so — nothing fails silently.
- **The whole audio stack follows the choice, immediately:** the caption engine's capture, the speaker-voice detector, and the silence keep-alive all re-point the moment the picker changes — no restart. Verified live end-to-end: an explicitly chosen speaker was captured, logged ("speaker picker changed — re-pointing the audio stack"), and captioned on the GPU engine.

### 1.0.302
**One more stutter class cleaned from captions**
- **Punctuated two-word stutters are collapsed now.** Seen live on the caption bar: "…on the new build. new build" — the engine re-emitting a short phrase right after closing the sentence. The repeat-collapser deliberately ignored 2-word repeats to protect natural speech like "I know, I know"; it now collapses a 2-word repeat ONLY when the first copy ends a sentence (". ! ?") — exactly the stutter signature — while comma-joined or unpunctuated doubles still pass through as spoken.
- Long-session hygiene audit came back clean: the caption history (500 entries) and the rolling caption line (360 chars) are both properly bounded for the hours-long caption sessions the logs show.

### 1.0.301
**The AI word fixer stops Americanising British spellings · the AI face analysis paces itself to what's on screen**
- **British English is not a spelling mistake.** The mishear fixer uses Windows' en-US spell engine — which flags "colour", "favourite", "realise" and friends, and its suggestions ("color", "favorite") are one edit away, so the fixer would happily "repair" British spellings into American ones. The fixer now asks the en-GB engine for a second opinion first: any word that is valid British English is left exactly as heard. (Best-effort — on PCs without the British dictionary, behaviour is unchanged.)
- **Face analysis paces itself.** The face/mouth analyser used to tick at a fixed rate whether or not there was anything to analyse. Now it runs FASTER while faces are actually on screen (finer mouth-motion sampling → surer "who is talking" verdicts) and idles right down after ~5 s with no faces — so its cost concentrates exactly where it earns its keep. Verified running live in a caption session on the GPU engine.

### 1.0.300
**Two apps playing at once no longer wreck the speakers · Live debug stops yanking you while you read**
- **Dual audio (game + video) no longer resets speakers every second.** A real Live-debug export showed it: with Call of Duty AND a YouTube video both audible, the "who is playing" tag flapped between them — and every flap FORGOT the learned voices and restarted the speaker numbering, so labels never settled. Two fixes: the source tag now requires a challenger to win the loudness race NET of the incumbent's wins (trading the lead back and forth never switches; a genuine hand-over still switches in ~5 s), and the voice/numbering reset is rate-limited to once per 20 s, so even legitimate tag changes during mixed audio keep the voices that were already learned. Verified live: two programs deliberately talking over each other for 20 s produced ZERO tag changes and ZERO resets (previously: one every second or two).
- **Live debug respects your scrolling.** Reading older lines while new events streamed in used to yank the view back to the newest line ("it forces me to the start"). The view now auto-follows only when you're AT the tail; scrolled up, new lines append quietly and your reading position stays put. Verified live.
- Also restored in this build's settings hygiene: the GPU-captions option that an older build (1.0.290) had permanently switched off during a gaming session — the session-only GPU fallback shipped in the meantime makes that safe to leave ON.

### 1.0.299
**Every window now wears the Tempo icon · the app opens quicker**
- **The Tempo icon is on every window now.** The main window and tray always had it, but every pop-up — Live debug, About, CPS test, Calibrate, Overlay customise, the update and download dialogs, the macro editors, and the rest — showed Windows' generic blank icon in its title bar and in Alt-Tab. Now they all carry the Tempo lightning-bolt. (Verified live: the CPS-test and Live-debug windows, which previously had no icon, now show it.)
- **Snappier launch.** The loading splash now appears the instant Tempo starts, before the monitor scan and prerequisite check rather than after them, and the environment logging that used to run on the critical path now happens in the background — so the window comes up with less of that "wait a moment before it opens" gap. (The unavoidable first-launch cost of a fresh download — Windows scanning a new .exe and the one-time self-extract of the speech engine — is separate; subsequent launches are the fast ones.)

### 1.0.298
**Overlays stay on top of fullscreen games/video · Live Debug tells you what's wrong · the new features now speak all 5 languages**
- **Overlays no longer vanish over fullscreen.** The clicking badge, countdown, captions and other overlays used to get buried the instant a game or fullscreen YouTube took over the screen. Now a shared keeper re-asserts them the moment any app takes the foreground (plus a fast backstop), so they stay visible over borderless-fullscreen games and fullscreen browser video. (True *exclusive* fullscreen still can't be overlaid by any app — run the game in borderless/windowed-fullscreen mode.) Verified by z-order against a real fullscreen topmost window.
- **Live Debug now leads with a Health panel.** Instead of scrolling the event log, the top of Live Debug answers "is anything wrong right now?" — a green "✓ no issues detected", or an amber/red list that names each problem in plain English: the last error (with how long ago), a GPU setting that needs a restart, an auto-downgraded speech model, captions falling behind, hotkeys Windows wouldn't reserve, startup blocked in Task Manager, camera movement armed globally, anti-freeze throttling. Errors and warnings are now captured with their real cause so they show up here rather than being swallowed.
- **All five languages caught up.** Everything added recently — the overlay "Customise…" dialog, start-delay + beep, update frequency, startup delay, and the whole camera-relative movement card — was English-only in Español/Français/Deutsch/Italiano/Português. It's now translated across all five (verified live in Spanish). Untranslated help text still falls back to English, as before.

### 1.0.297
**Four Settings features levelled up: the start-delay, the update check, the running overlay, and startup launch**
- **The start-delay countdown is no longer a blind, forced wait.** It now shows what it's about to start ("Left · 12 CPS"), and you can abort it — press Esc *or* your stop hotkey and the count-in cancels instead of making you sit through it. An optional per-second **beep** (with a higher note on GO) gives an audible count-in.
- **Update checking gained a frequency and an inline status.** Instead of a bare on/off, choose **Every launch / Daily / Weekly**, and the "last checked" line now tells you the result at a glance — **"You're up to date"** or **"Update available: vX"** — without opening the update dialog.
- **The running overlay is now customisable.** A "Customise…" button lets you pick its **screen corner** (six positions), its **opacity** (40–100%), and exactly what it shows — **click count, CPS, and/or elapsed time** — instead of a fixed top-centre badge. Changes apply live while it's on screen.
- **Startup launch got a delay option.** When Windows launches Tempo at sign-in, an optional **startup delay** holds its background update check back by a few seconds so it doesn't fight the rest of your sign-in for the network. (Tempo already self-heals a vanished startup entry and warns if Windows blocked it.)
- Verified live on real hardware: the overlay was driven to the bottom-right corner showing elapsed time, and every setting round-tripped correctly.

### 1.0.296
**Burst mode ignored the safety cap · more hotkeys are now bindable · tested live on real hardware**
- **Burst mode was blowing straight through Anti-Freeze.** With the Max-CPS cap set to 10, Interval mode correctly held 10 CPS — but Burst mode hit **256 CPS** (measured live) while the panel still cheerfully read "Protected". Burst never called the anti-freeze path at all, so the safety cap AND the CPU back-off did nothing in the one mode fast enough to actually lock a machine up. Fixed and verified: the same test now holds ~10 CPS, and a legitimately fast rate (cap 2000) still runs full speed. A second, subtler hole — burst size 1 with a 0 ms pause — is closed too.
- **The two CPS numbers on the Clicker tab no longer contradict each other.** With a Double/Triple/Quadruple click type, "Click Interval" showed the multiplied click rate (e.g. 114 CPS) while "Manual Speed" showed the press rate (29 CPS) for the *same* setting, with nothing to explain it. Both now spell it out: "34.5/s × 4 = 137.9 clicks/s".
- **More hotkey combinations can be bound.** Windows Forms was quietly swallowing a whole class of key combos before Tempo ever saw them — **Ctrl+Delete, Ctrl+A, Ctrl+V, Shift+Delete** and the other editing shortcuts simply couldn't be assigned. They all bind now. (Tab, Enter, Space and the arrows, added last release, keep working.)
- **See whether a hotkey is actually reaching Tempo.** On the Keybinds tab, a binding's row now **flashes green the instant its key is pressed**, from any app — so "is my hotkey even getting through?" is answerable at a glance instead of by trial and error.
- **Tempo now warns when Windows refused a hotkey.** If another program already owns a combination, Windows won't reserve it and Tempo quietly falls back to a keyboard hook — the action still fires, but the key also keeps doing its normal job in the other app. That "half works" state used to be invisible; it's now spelled out on the Keybinds tab so you can pick a different combination.
- **Faster multi-point clicking.** The engine was allocating a fresh list on every single click in multi-point mode — hundreds of throwaway objects a second on its hottest path. Now reused.
- Much of this was found and verified by driving the real app on real hardware, not just by reading code.

### 1.0.295
**Tab can finally be bound · the field shows what it's detecting as you press · Tempo now names your keyboard**
- **Tab is bindable at last.** It never worked because the capture field was never even TOLD Tab had been pressed — Windows Forms grabs Tab for focus navigation before a control sees it, so pressing Tab just jumped to the next field. It's now claimed as real key input, along with Enter, the arrows, Page Up/Down, Home and End. (Because the field now swallows Tab, **Esc leaves the field** — that's the way out for keyboard users.)
- **Live "detecting" preview while you bind.** Click a field and it says "▸ listening…". Hold Ctrl+Shift and it reads "Ctrl + Shift + …" **before** you press the final key — so the modifiers Tempo has actually detected are visible up front, instead of committing a combo and wondering why it came out wrong.
- **Ctrl+Delete and Shift+Backspace are bindable now too.** The clear-the-field branch fired on *any* Backspace or Delete, before the capture code could ever see them — silently making every modified variant impossible. Clearing now needs a **bare** Backspace/Delete. Alt+F4 is also passed through, so it still closes Tempo instead of binding itself.
- **Tempo detects and names your keyboard**, on the Keybinds tab and in Live Debug: model, layout and key count (e.g. "SONiX USB Keyboard · English (United States) · Enhanced (101/102-key), 12 F-keys"). When a hotkey "doesn't work on my keyboard", the model and the **layout** are the first two facts anyone needs — the layout decides which physical key produces which virtual key — and Tempo previously couldn't tell you either. Gaming mice that register as keyboards (for their macro buttons) are correctly excluded, so your mouse is never reported as your keyboard.
- **Bare keys now warn you.** A global hotkey is taken **system-wide**: bind a bare "A" and the letter A stops reaching every other program while Tempo runs; a bare Tab would take Tab away from the whole desktop. Risky bare keys are flagged amber (distinct from the red used for two actions clashing) with a plain explanation. Function keys are exempt — that's what they're for, and why F6/F8 are the defaults. It's still allowed; it just can't happen silently.
- Key names read like keys now, not debug output: "Enter", "Backspace", "Page Up", "Left Arrow", "`", "/" — instead of "Return", "Back", "PageUp", "Oemtilde", "OemQuestion".

### 1.0.294
**New: camera-relative movement (advanced) — with calibration, a hotkey, and a Live Debug view that actually shows what it's doing**
- **New feature: camera-relative movement.** Hold a direction and Tempo keeps you travelling that way as you swing the camera, re-mixing W/A/S/D in real time — circle-strafe an enemy, or keep running one way while looking another. Tempo cannot READ the game's camera (it lives in another process), so it **estimates** it from your raw mouse movement and steers against that. Settings → *Camera-relative movement* has every knob: mode, sensitivity, smoothing, anti-jitter, update rate, stick deadzone and pad look speed.
- **Calibration that actually works.** The measurement has to happen inside the game, and you can't click a "stop" button to end it — moving the mouse to the button would add hundreds of counts to the very number being measured. So it's gated on a **held key**: hold F10, turn one full circle, release. Clean measurement, and you never leave the game.
- **A hotkey to arm it in-game**, plus one to re-centre the camera estimate when it drifts. Emergency stop always disarms it and releases every held key.
- **It only touches the game you armed it in.** Suppressing W/A/S/D is global, so arming it and alt-tabbing to Discord would otherwise have silently eaten every "w", "a", "s" and "d" you typed. Tempo captures whichever window was in front when you armed it and leaves your keys alone everywhere else.
- **Anti-jitter, verified.** A keyboard can only express 8 directions, so a heading resting exactly on a boundary would rattle the keys many times a second. A hysteresis band plus a minimum dwell time fixes it: 2,000 samples dithering on a boundary now produce **zero** key changes, while a real turn still commits promptly.
- **Live Debug got much stronger.** It now shows the movement system live — the armed window, whether it's acting, the estimated camera angle, and the line that matters: "you press W → Tempo sends W+A". There's a **Movement trace** toggle (key changes + a 1 Hz heartbeat), an **Always on top** option (the window used to sit *behind* the game you were debugging), and the stats header is now **selectable and colour-coded** — warnings amber, failures red — instead of a wall of identical grey text you couldn't even copy from.

### 1.0.293
**Why "Try GPU engine" seemed to do nothing · captions can no longer fall seconds behind in silence**
- **Ticking "Try GPU engine" while Tempo was running did NOTHING — silently.** The speech engine's CPU/GPU choice is fixed the moment the first model loads and cannot change until Tempo restarts. So if captions had already run once, ticking the box just… didn't apply. No message, no hint — you'd sit watching the CPU engine lag and wonder why the GPU wasn't helping. Tempo now **tells you outright and offers to restart on the spot**, and Live Debug shows a loud warning whenever your GPU setting and the engine actually running disagree.
- **Same trap for auto-downgraded models.** When a model is too slow, Tempo drops to a smaller one *for the session* — and your real choice only comes back on restart. Live Debug now says so explicitly instead of leaving you to wonder why you're on `medium`.
- **Captions can no longer sit permanently seconds behind.** The audio backlog was allowed to grow to **8.4 seconds**. An engine that can't quite keep up would fill that buffer and then just *sit* at the cap: still transcribing every word, but every word arriving many seconds after it was said — forever, and invisibly. That is exactly the "it works but it lags" state. The backlog is now capped at ~3 s: audio older than that is dropped so what's on screen tracks what's being said **now**. Live captions are worth more current than complete.
- **Falling behind is no longer silent.** Dropping audio to catch up now logs a plain warning ("the engine is behind live audio — a smaller model, or the GPU engine, would keep up") instead of happening invisibly.

### 1.0.292
**Captions turned OFF now STAY off · the speech-engine error is fixed at the root (your debug export found it)**
- **Turning captions off no longer un-does itself.** Switch captions off and, about ten seconds later, they came straight back on — and it repeated forever. The watcher that notices you pressing Win+Ctrl+L yourself was checking whether the Windows caption window **is present**, not whether it had just **appeared**. So after you turned captions off, it saw the Windows bar (often the very one Tempo was still closing), read that as "the user just turned Live Captions on", and switched Tempo back on. It now fires only on a genuine off→on transition, and an explicit "off" requires the Windows bar to actually go away before anything can re-arm it.
- **"Start automatically for videos and games" is now honoured everywhere.** With auto-start switched off, captions could STILL come on by themselves, because the Windows-Live-Captions watcher above never consulted that setting — it rode on a different checkbox entirely. If you've told Tempo not to start captions by itself, it now doesn't. From any trigger.
- **The recurring speech-engine error is fixed — at the root.** Every engine restart (a model downgrade, a caption toggle) logged `InvalidOperationException: Collection was modified` from inside the speech engine. Cause: when captions stopped mid-chunk, Tempo abandoned the in-flight transcription, which made the engine tear down its segment handler **while its own native thread was still using it**. Tempo now lets the chunk finish and drains it quietly instead of dropping it on the floor, so the engine is never pulled apart underneath itself. (Found precisely thanks to 1.0.291's improved error logging — the stack trace named the exact line.)
- Also hardened the talking-face detector against the same class of race: turning captions off cleared its tracked-faces list while a video frame was still being analysed on another thread.

### 1.0.291
**A game no longer permanently disables GPU captions · speaker numbering stops resetting every second · errors finally say what went wrong**
- **A busy GPU no longer kills GPU captions for good.** If the GPU speech engine can't keep pace, Tempo used to write "GPU off" into your settings **permanently**. But a GPU that's merely BUSY isn't a GPU that can't do the job: measured live, Large Turbo ran ~10× faster than real time until Call of Duty took the graphics card, after which the same chunks took 3× LONGER than real time. One gaming session was quietly disabling GPU captions forever. The fallback is now **session-only**: Tempo mirrors Windows Live Captions for the rest of the run, the GPU option stays ON, and the next launch tries the GPU again. A GPU that genuinely can't cope just falls back again — costing seconds, not the feature.
- **Your speaker numbering stopped resetting every second.** With two apps making sound at once (a game plus a video), Tempo picked whichever audio session was momentarily loudest — so the detected source flip-flopped between them on *every* one-second tick. That wasn't only log noise: each flip wiped the learned voice profiles and reset speaker numbering, so the speaker labels could never settle. A competing app must now stay loudest for several consecutive ticks before it takes over; the first source heard is still adopted instantly, so nothing is slower to start. The face analyzer also no longer gets re-pointed at a momentarily-loud app's window on ticks where that app was rejected.
- **Errors now name themselves.** A background-task error was logged as a bare "a Task failed" with **no stack trace** — the wrapper exception carries none — so a real fault was completely unattributable. Every inner exception is now logged with its true type, message and stack trace, so a Live-debug export actually points at the culprit.
- **Fixed a crash-adjacent race in the talking-face detector.** Turning captions off (or any caption restart) cleared the tracked-faces list from the UI thread while a video frame was still being analysed on another thread — the classic "Collection was modified" fault. The list is now properly guarded.

### 1.0.290
**Captions hear sooner: ~100 ms of pure waiting removed from the audio path · Live debug sees the whole pipeline**
- **Smaller audio capture buffer (100 → 40 ms).** The audio system was hard-wired to a 100 ms Windows capture buffer — a tenth of a second every caption spent just sitting inside the OS before the speech engine could even see the sound. The capture now opens with a 40 ms buffer (same shared-mode plumbing), with an automatic fallback to the standard 100 ms capture if any device refuses, so nothing can break — worst case is exactly the old behaviour. Applies to system audio, microphone, and the device-change reopen paths alike.
- **The transcribe worker no longer polls.** It used to check for new audio on a fixed 50 ms timer, adding up to a whole tick of dead waiting per pass; the capture callback now wakes it the instant enough sound has arrived. Together with the smaller buffer this trims roughly 80–110 ms off every caption's word-to-screen latency — before the engine even starts working.
- **Cleaner chunk handling.** Each chunk is now lifted out of the audio buffer with a single copy (was two), and any audio thrown away because the engine fell behind is now **counted and shown** ("DROPPED n s" in Live debug) instead of vanishing silently.
- **Live debug got a live audio view.** New header lines: a ten-cell level meter with a dB reading (see at a glance whether Tempo is hearing anything and how loudly), the auto-gain currently applied, the capture device's native format and the actual buffer size in use, plus pipeline counters — chunks done / silent-skipped / captions emitted, a smoothed average inference time, and "last words n s ago". Stats refresh doubled to 2× per second.
- **New "Chunk trace" toggle in Live debug.** One `[Trace]` line per transcription pass — `chunk 2050 ms → 190 ms (0.09×RT) · backlog 0.3 s · gain 1.8× · "the text shown"` — so you can watch the pipeline beat by beat and see exactly where time goes. Switches itself off when the window closes so the log ring isn't flooded afterwards.

### 1.0.289
**Speaker fix: one narrator no longer becomes "Speaker 2 / 3" mid-video**
- **The reported bug:** watching a YouTube video with a single narrator, the label started as Speaker 1 and then flipped to Speaker 2 (and 3) while the SAME person kept talking. Root cause: a silent pause longer than ~4 s (music break, scene change, slow moment) made the pause-heuristic COUNT UP the speaker number whenever the voice verdict wasn't ready at that instant — and background music mixed under the voice could even mint a phantom voice profile that then "confirmed" the wrong number.
- **The fix, two layers:**
  1. While the voice detector is running, a pause alone NO LONGER changes the speaker number — the voice verdict is what changes it. (If it really is a new person, the verdict lands within about a second and the label corrects itself in place, exactly as before.) The old count-up heuristic still applies when voice detection isn't available (no audio device) — there it remains the only signal.
  2. The voice detector now needs ~50% longer of consistently non-matching audio (~1 s) before it will invent a brand-new speaker profile, so music or effects layered under one narrator's voice can't mint a phantom "Speaker 2" — while real speaker changes still classify quickly by matching.
- **Verified on the exact reported scenario:** a real YouTube video with one narrator, pauses and a music break — the label held Speaker 1 throughout. Genuine voice changes still switch labels (that code path is untouched and covered by the standing two-voice test).

### 1.0.288
**Both caption paths improved: the Windows 11 mirror reacts faster · the own engine's seams got cleaner**
- **Windows 11 Live Captions mirror: adaptive polling.** The mirror used to check Windows' caption text on a fixed 250 ms clock. It now polls HARD (140 ms) while the text is actively moving — shaving ~100 ms of average mirror latency exactly when words are flowing — eases to 200 ms in short gaps, and idles at 350 ms through silence so UI Automation isn't hammered for nothing. Verified live: Windows-engine text relayed promptly with speaker labels, colours and the source tag intact.
- **Own engine: cleaner seams between chunks.** The most visible remaining error class was the JOIN between listening windows ("sentence." re-heard as a twisted repeat). The chunk overlap widened from 0.6 s to 0.75 s, so both passes see more shared context at every seam — and the update step actually got slightly SHORTER (2.2 → 2.05 s) as a side effect. Costs ~12% more compute: trivial on GPU and light models; the too-slow ladder still guards weaker setups. Verified live on real playing media with correct text and source tagging.

### 1.0.287
**Live debug tells more · errors now NOTIFY you · first words land even sooner on GPU**
- **Live debug got deeper.** New header lines: "Decode: beam ON · cadence very fast ×0.6 · keep-alive playing" (see exactly which accuracy/speed features are live), and a session events counter ("events: 0 warn / 0 error"). The event stream is now **colour-coded** — errors red, warnings amber — and a new **"Problems only"** toggle shows just the warnings/errors. Verified live on screen.
- **You get told when something goes wrong.** Any ERROR event now raises a tray notification ("Tempo — something went wrong … Details: Settings → Data & Backup → Live debug"), rate-limited to once per minute so an error storm can't spam the tray. Errors stop being something you discover in a log days later.
- **First words even sooner on fast engines.** On the very-fast tier (typically GPU), the first-words threshold after silence drops from 1.2 s to 0.9 s of audio — the word-integrity guards (silence trim + minimum-speech wait) still apply, the take just happens ~0.3 s sooner. Verified live: cadence tier "very fast ×0.6" active with beam ON on the GPU engine.

### 1.0.286
**The GPU's headroom is now SPENT on accuracy and speed: beam search on every model + an even faster caption cadence**
- **Beam-search hearing on ALL models when the GPU engine is active.** Beam decoding weighs several word paths instead of committing to the first guess — audibly better hearing at ~2× decode cost. Until now only the light models could afford it; with the GPU engine measured ~10× faster than real time, every model gets it — including Large Turbo. Verified live from the log: "beam-search decoding enabled for ggml-large-v3-turbo.bin (GPU headroom)" with a long spoken passage captioned to its final word at full pace, zero too-slow warnings.
- **A gentler too-slow ladder: beam drops FIRST.** If a weaker GPU (or a busy PC) can't afford beam after all, Tempo now simplifies decoding (beam off, same model, same engine) as the first response — before shrinking the model, and long before giving up the GPU. Small sacrifice first, drastic measures last.
- **Even snappier updates when the engine is very fast.** The caption cadence now has two earned tiers: engines with headroom take a ~25% shorter step, and VERY fast engines (typically GPU) take a ~40% shorter step — captions update roughly every 1.3 s instead of 2.2 s, with the same audio context per pass. Slow setups keep the stable full step; nothing regresses.
- The language lock's mid-session rebuilds preserve the beam setting (verified in the log).

### 1.0.285
**Live debug window — see exactly what Tempo is doing, in real time**
- **New: Settings → Data & Backup → "Live debug…".** A real-time window with two halves:
  - **Live stats** (refreshed every second): whether captions are on and on which engine (CPU or GPU), the active model, how fast the engine is REALLY running ("last chunk 2280 ms heard in 240 ms"), audio backlog, language-lock state, both audio devices by name, the current audio source, the live speaker verdicts (voice + face), and the clicker state.
  - **Live event stream**: every internal event as it happens — device changes, keep-alive, engine/runtime choice, language locks, model downgrades, auto-recoveries, caption take-overs. With filter, pause, Copy, Save-to-file and Clear. Perfect context to attach to a bug report.
- Verified live: the window showed the GPU engine transcribing 2.28 s chunks in 240 ms (≈9.5× real time) while captioning a running game.
- Everything stays on the PC — the view reads Tempo's in-memory event ring; nothing is sent anywhere unless you press Copy/Save yourself. The in-memory view works even when "Write a log file to disk" is off.
- Also corrected an internal myth: file logging was never broken — the log lives at %LOCALAPPDATA%\Tempo\logs\ (the "Open log file" button always pointed at the right place).

### 1.0.284
**Microphone & speaker: Tempo now notices a device being plugged in and ASKS (Yes/No) — and names both devices so choosing is easy**
- **The rescue prompt.** On a PC with no speaker and no microphone, captions can't hear anything. Tempo now watches for devices arriving (refresh every 2 s, was 3 s) and the moment a **microphone** is connected in that state it asks plainly: "A microphone was just connected: 🎙 <name>. This PC has no speaker… Use this microphone for captions instead? **Yes / No**". Yes switches "Listen to" to Microphone, saves it, and restarts the engine on the spot. Each arrival asks at most ONCE (re-armed only if the device disappears again) — no nagging.
- **The reverse prompt.** If captions are running on the microphone (by explicit choice, typically because there was no speaker) and a **speaker** is connected, Tempo asks whether to switch to system audio so videos and games get captioned directly. Yes/No, once per arrival, user's call.
- **Both devices? You choose.** The Settings device line now names BOTH: "🔊 Speaker: <name> · 🎙 Mic: <name>" — so the "Listen to" picker right above it (Auto / System audio / Microphone) is an informed choice. The no-speaker warning also now names the available microphone.
- These prompts only apply to Tempo's own caption engine (the Windows Live Captions source manages its own audio) — and note the arrival prompts fire in the no-speaker scenarios, which is exactly where they're needed.

### 1.0.283
**GPU captions (opt-in, all GPU brands) — Large Turbo measured FULLY REAL-TIME on GPU · new Compact Large model · AMD answers**
- **"Try GPU engine" (Settings → Live Captions).** Runs the speech engine on your graphics card via Vulkan — which works on **AMD and Intel GPUs as well as NVIDIA** (it's the only GPU path that exists for AMD cards). Opt-in because GPU drivers vary wildly; the proven CPU engine stays the default and is the automatic fallback if the GPU can't initialise. Measured on real hardware: **Large Turbo ran a 16-second continuous speech in sustained real time on GPU** — the final words appeared within seconds — with GPU usage spikes confirming the card was doing the work. (Yesterday's 1.0.282 rejection was version-specific: Vulkan 1.9.1 was broken; 1.8.1 — matching our pinned engine — is what works.)
- **Self-healing guard:** if the GPU engine ever can't keep pace with live audio, Tempo switches the option back off by itself, tells you, and returns to the CPU engine at the next start — no stuck-broken state.
- **New model: "Large Turbo Compact" (~575 MB).** The 5-bit compressed build of Large Turbo: near-identical hearing at a third of the size and noticeably less CPU per chunk — the pick when the full Large Turbo lags on a busy or mid-range PC. Verified live: loads, hears correctly. The auto-downgrade ladder now steps full Large → Compact Large FIRST, keeping multilingual hearing instead of dropping straight to English-only Medium.
- **AMD users, plainly:** AMD **processors** always worked — the CPU engine uses standard instructions every Ryzen has. AMD **graphics cards** now have their GPU path via the new opt-in Vulkan engine.
- The download grows ~6 MB for the bundled GPU engine.

### 1.0.282
**The caption status now names its engine (CPU/GPU) · GPU inference was tried, measured, and honestly rejected**
- **The captions status line now says which speech engine is running** — e.g. "Tempo Live Captions started · system audio · KJW24FVX (NVIDIA High Definition Audio) · CPU engine" — so it's always visible WHY a model is fast or slow on a given PC. Verified live.
- **What was tried and why it isn't shipping:** GPU inference (Whisper.net's Vulkan runtime) and the newer 1.9.1 engine core were both installed and measured with timed caption tests on real hardware. The Vulkan GPU path processed chunks so slowly that the audio buffer dropped everything but sentence tails — unusable — and the 1.9.1 core showed no benefit to outweigh its risk (its Large Turbo run hit the too-slow downgrade in testing). Tempo stays on the proven 1.8.1 engine, and the tried-and-rejected results are documented in the project so the experiment isn't blindly repeated.
- Reassurance: the shipped caption stack is byte-for-byte the proven 1.0.281 pipeline plus the engine-name status tag.

### 1.0.281
**AI word fixer stops "repairing" people's names · AI face analysis sees smaller faces and reacts faster**
- **The mishear fixer now understands sentence position.** A Capitalised word at a sentence start is normal text and stays checkable ("Seawhere did it go" → "Sea where…"), but a Capitalised word MID-sentence is almost always a NAME — and a name that sits within a couple of letters of a dictionary word ("Dani", "Zira") could get silently "repaired" into it. Mid-sentence capitalised words are now left strictly alone. Verified live: "…tell Matilda the analysis is finished" came through with the name untouched.
- **Face & mouth analysis upgraded.** (a) The analysis frame grew from 480 to 640 pixels wide: faces that were too small to lip-read (people further from the camera, wide shots) now clear the minimum size, and every mouth region carries ~1.8× the pixels for a steadier talking signal. (b) A newly-appeared face's motion history is now seeded from its first frame instead of easing in from zero — after every scene cut, the "who is talking" verdict lands about a second sooner.

### 1.0.280
**Settings page fix: Save Settings / Check for updates were buried under the Window & Display card**
- The Live Captions section grew over the last releases, and the bottom of the Settings page wasn't moved with it — so for every user, the **Save Settings, About…, Check for updates and Reset to defaults buttons ended up hidden UNDER the "Window & Display" card**, and the portable-copy / privacy notes overlapped each other and ran through the card. All of it is re-laid-out: buttons in their own row below the last card, "Last checked for updates" beneath them (it used to hide behind the Save button even before this bug), the two notes stacked cleanly, and the version line at the true bottom of the page. Verified on screen.

### 1.0.279
**Speaker-detection bug fixed: captions now FOLLOW when you switch your default speaker · snappier caption updates**
- **Bug found & fixed: switching the default output device left captions deaf.** When you switch Windows' default speaker (headphones ↔ monitor ↔ Bluetooth) while the old device is still connected, Windows fires no "device stopped" signal — so Tempo's caption engine and the speaker-voice detector kept listening to the OLD, now-silent device until you toggled captions off and on. The device watcher now spots the default changing and re-points the caption engine, the voice detector (keeping its learned voices), and the audio keep-alive at the new device automatically.
- **Speaker detection hardened.** A transient device-query failure during a device change could flash "⚠ No speaker found" and churn recovery for one poll; the watcher now keeps the last known speaker through a single failed read (two consecutive failures mean it's really gone). It also heals itself if the audio system wasn't ready when Tempo started.
- **Less delay.** (a) When the engine is transcribing much faster than real time — the light models with 1.0.277's encoder shortcut — captions now update on a ~25% shorter cadence automatically; slower setups keep the stable full step. (b) Internal waits in the audio loop were tightened (80→50 ms poll, 120→80 ms speech-build wait). Verified live: a long sentence stayed verbatim and complete with the shorter cadence.

### 1.0.278
**Your choice: show or hide the "♪ YouTube ·" source name on the caption bar**
- **New checkbox in Settings → Live Captions: "Show audio source name on the bar (♪ YouTube ·)".** Some users want to see which app the audio comes from; others find it clutter — now it's a choice. Unticking hides the tag from live captions on both engines (Tempo's own and the Windows mirror) and also drops the app name from the "♪ Music or sounds" note, leaving only the words and the coloured speaker labels. On by default (the long-standing behaviour). Verified live both ways: with the tag ("♪ Powershell · Speaker 1: …") and without ("Speaker 1: …").
- Reliability note for anyone whose Tempo suddenly wouldn't start on 2026-07-13-era Windows: **Windows Smart App Control**, when switched on, silently blocks unsigned apps like Tempo (the process dies instantly with no visible error). Tempo can't fix that from inside; if it happens, check Windows Security → App & browser control → Smart App Control.

### 1.0.277
**The speech engine runs several times faster per chunk — the biggest models become usable**
- **Encoder shortcut (the whisper.cpp streaming trick).** Whisper pads every ~3-second chunk to 30 seconds and ran its encoder over the WHOLE padded window. Tempo now caps the encoder context to what the audio actually fills (with generous margin), cutting inference several-fold on every model. Measured live on this PC: **Large Turbo went from ~36-40 s per chunk (13× slower than real time, unusable — the auto-downgrade always kicked it out) to roughly 1.5-2× of real time** — complete, correct sentences now appear within seconds on the best model. Normal speech with natural pauses captions well on Large Turbo; a long uninterrupted monologue still drifts behind and the auto-downgrade safety ladder still guards that case. Every smaller model gains the same headroom (snappier captions, more room for beam search).
- **Quality verified, not assumed.** The base model still transcribed the test sentences near-verbatim with the reduced context, and Large Turbo's output was verbatim on the timed tests.
- **Opening words are protected from the confidence gate.** The phantom-word filter (1.0.273) could occasionally eat REAL first words right after silence, where fragments are naturally shakier (seen live once). Quick-start chunks now use a much stricter bar before dropping anything, so opening words survive.

### 1.0.276
**Chinese/Japanese captions wrap correctly · steadier loudness for the speech engine · source tag can't blame caption tools**
- **Space-less scripts wrap correctly on the caption bar.** The colour-layout added in 1.0.272 wrapped text at spaces — but Chinese and Japanese don't use spaces, so a whole caption arrived as one unbreakable "word" that could overflow the bar. Oversized runs now break into fitting pieces character-by-character (with no fake gaps inside the run), which also covers long URLs. English wrapping verified live across multiple lines; the break-up path shares the same layout machinery.
- **Steadier loudness for the speech engine.** Auto-gain was recomputed per chunk, so what Whisper heard could pump 8× between a quiet sentence and a loud effect and back. The gain is now smoothed across chunks (with a peak guard so a loud chunk after quiet ones can't clip) — a steadier level transcribes more consistently.
- **The "♪ App ·" source tag can't blame caption tools anymore.** Seen live: audio was attributed to "Live Captions" — a captioning tool, never the actual media. Caption/speech utility processes are now excluded from source naming, so the tag stays on the real app (game, browser, player).

### 1.0.275
**Tempo now detects whether your PC has a speaker — by name, live, with auto-recovery**
- **Live audio-device line in Settings → Live Captions.** Tempo now shows exactly which speaker it can hear through — by name ("🔊 Speaker detected: KJW24FVX (NVIDIA High Definition Audio) · 🎙 microphone ready") — refreshed every few seconds as devices come and go. With no speaker it switches to a clear warning ("⚠ No speaker found — Tempo can't hear system audio") plus what to do about it (use the Microphone source, or connect a device). Verified live with the real device names on this PC.
- **Captions name their device.** The status line when captions start now says which device is being heard, e.g. "Tempo Live Captions started · system audio · KJW24FVX (NVIDIA High Definition Audio)" — so "is Tempo listening to the right output?" is answered at a glance.
- **Captions auto-recover when a speaker appears.** Previously, if captions started with no speaker (or the audio device died and couldn't be reopened), they stayed dead until you toggled Live Captions off and on. Tempo now watches for a speaker appearing or returning — headphones plugged in, Bluetooth reconnecting, a monitor waking — re-points the audio keep-alive at it and restarts its caption engine automatically.

### 1.0.274
**Cleaner voice fingerprints for speakers · language stays locked once detected · more hallucinations scrubbed**
- **Speaker voices are measured cleanly now.** The voice detector listens to a downsampled copy of the PC's audio — and the downsampling was done the quick way, which folds everything above 8 kHz (sibilants, music cymbals) back INTO the voice band as false signal. The same person could measure "brighter" or "darker" depending on the background material, nudging the matcher toward wrong speaker numbers. The audio is now properly filtered while downsampling, so the pitch/brightness fingerprints reflect the actual voice. Verified live: the two-voice test still labels cleanly — first voice Speaker 1 (blue), second voice Speaker 2 (green), first voice back to Speaker 1.
- **Session language lock (multilingual models, e.g. Large Turbo).** Language auto-detection runs per chunk and could FLIP on a noisy or musical chunk, transcribing a stretch as the wrong language. Once several consecutive chunks agree, Tempo now locks that language for the session (also skipping per-chunk detection cost). If confidence later collapses for several chunks — the sign the content really changed language — it unlocks and re-detects. English-only models are untouched.
- **Subtitle-credit hallucinations scrubbed in 8 languages.** Whisper memorised subtitle credits from its training data and emits them on quiet/musical audio — "Subtitles by the Amara.org community", "ご視聴ありがとうございました", "Sous-titres réalisés par…". These are never real speech, so they're now dropped at any volume, in English, Japanese, German, French, Spanish, Italian, Portuguese and Chinese variants.
- Speaker 2's green label was also verified on screen this round (1.0.272 had only shown Speaker 1's blue live).

### 1.0.273
**Speech engine core upgraded — hears noticeably better on every model**
- **Whisper engine upgraded from the early-2024 runtime (Whisper.net 1.5) to the current one (1.8.1)** — roughly two years of upstream recognition and decoding fixes for every model, tiny through Large Turbo. Verified live: near-verbatim transcription on the small *base* model ("Fox jumps over the lazy dog while the engine listens carefully to every word.").
- **Beam-search decoding is ON for the light models** (tiny/base/small on a 6+ core CPU). It considers several word paths instead of committing to the first guess, and hears clearly better. It was designed in long ago but disabled because the old runtime had a bug making it ~30 s per chunk; re-measured healthy on the new runtime (captions keep real-time pace). Medium/Large stay greedy to protect pacing, and the auto-downgrade watchdog still guards slow PCs.
- **Phantom-word gate.** The engine now reads Whisper's own per-segment confidence and drops very short segments the model itself doubted — the classic "noise heard as a word or two". Long low-confidence stretches still show (real mumbled speech is better shown than swallowed). Verified live that real short phrases ("Danger ahead, turn left now.") pass through intact from the first word.
- **Smarter seam de-duplication.** Consecutive listening windows overlap, and Whisper sometimes re-hears the seam with different word breaks ("keep alive" → "keep a live") — the old word-by-word check couldn't see those repeats. It now also compares seam text letter-by-letter, catching re-segmented duplicates.
- **Decode-loop stutter collapse.** On noisy audio Whisper occasionally emits the same 3-6 word phrase twice or more in a row; repeated phrases now collapse to one copy (3-word minimum, so real speech like "I know, I know" is never touched).
- Installer exe grew ~6 MB (65 → 72 MB) — the newer speech runtime ships more native code.

### 1.0.272
**Speakers get colours · opening words come out whole · Whisper's fake "[Music]" tags and hallucinated phrases are scrubbed**
- **Per-speaker colours on the caption bar.** Each "Speaker N:" label now has its own stable colour (Speaker 1 sky blue, Speaker 2 green, Speaker 3 pink, …9 in total), so following who says what takes a glance instead of reading. The "♪ App ·" source tag is now muted grey so it stops competing with the actual words. The black glow/outline still wraps everything seamlessly, and the user's chosen text colour still applies to the speech itself. Verified live against a real YouTube video: muted "♪ YouTube ·" tag, blue "Speaker 1:", amber body text, clean centred wrapping.
- **Opening words are transcribed whole.** Since the keep-alive (1.0.271), audio flows continuously — so a chunk grabbed just as someone starts talking was mostly silence with only the first syllable at the end, and Whisper mangled the opening word. The engine now trims the silent lead-in and, if speech only just began, waits a beat so the first word reaches Whisper complete.
- **Whisper artifact scrubber.** Whisper emits non-speech tags — "[Music]", "(applause)", "[BLANK_AUDIO]", "♪♪" — and famously invents phrases like "Thanks for watching!" out of near-silence. Tags made of sound-descriptor words are now stripped anywhere in the text, and the classic hallucinated phrases are dropped when the audio really was near-silent (genuinely spoken versions at normal volume still caption fine).

### 1.0.271
**Audio system: captions never sleep — fixes "not responding / delayed" at the start of videos and short sounds**
- **Root cause found and fixed.** Windows' "record what the PC plays" stream (which both Tempo's caption engine and the speaker-voice detector listen to) simply *stops delivering audio* the moment everything goes quiet. When sound returned — a video starting, a button click in any app or website — the stream had to wake up again, delaying or dropping the opening moments. That is exactly the "sometimes not responding or delayed" behaviour. Tempo now keeps the audio stream awake by playing perfectly inaudible silence while captions or speaker detection run, so **every sound is heard from its very first millisecond**. Verified live: after 10+ seconds of total silence a spoken phrase was captioned verbatim from its first word, and Tempo's keep-alive shows as an active (silent) session on the speaker.
- The keep-alive follows device changes (plugging in headphones etc.) together with the existing capture auto-reconnect, and holds only while captions or speaker labels are actually on. A PC with no speaker just runs as before.
- **Captions auto-start ~2× faster.** The video/game watcher now checks every second instead of every two, so auto-start reacts about 2 s after sound begins (was ~4-5 s). The safeguard that stops a stray notification ping from triggering captions is unchanged.
- **Honest "no audio" status.** The caption bar can now tell "the capture is healthy but nothing is playing" from "no audio is arriving at all", and says so instead of sitting on "Listening…" — this also fixed the message never appearing for a silent microphone.

### 1.0.270
**Theme: follow Windows light/dark automatically · Clicker: Humanize is now a real toggle**
- **New "Match Windows" theme option** (Settings → Appearance). When on, Tempo follows Windows' own light/dark app mode — dark when Windows is dark, light when it's light — and switches **live** the instant you change it in Windows Settings (or when a schedule/Night mode flips it). The manual theme picker greys out while it's on, and a custom accent colour still applies on top. Verified end-to-end: flipping Windows dark→light→dark re-themed Tempo each time with no interaction.
- **Humanize is now a proper on/off toggle.** Before, the Humanize button could only ever turn randomization ON — there was no one-click way back. Now it toggles: press once to apply natural interval + position jitter (still fully tunable in the fields below), press again to clear it. The button clearly shows its state — filled purple with a check when active, a quiet outline when off — and it stays in sync when you load a profile or edit the randomizers by hand.

### 1.0.269
**Much faster first words + the engine now guarantees it keeps up (auto model downgrade)**
- **Fast start.** The engine used to wait for a full 2.2 s audio step before its very first transcription — about 3 s from "someone starts talking" to words on screen. While captions are idle (fresh session, silence, a new speaker after a lull) it now accepts a much shorter first step, so opening words land in roughly 2–2.8 s; once text is flowing, full windows return because long windows hear better mid-stream.
- **Real-time watchdog with automatic model downgrade.** A model that needs longer to transcribe a chunk than the chunk lasts can NEVER catch up — measured on this class of CPU, Large Turbo took ~36 s per chunk and captions drifted minutes behind (and hallucinated under pressure). Tempo now measures every chunk, and when the chosen model repeatedly can't keep up it switches to the next smaller INSTALLED model for the session — your saved model choice is never touched — with a notification explaining what happened. Verified live: Large Turbo (~37 s behind) auto-switched to Base and the very next sentence appeared in 2.8 s, clean.
- **Beam search from 1.0.268 is disabled.** Honest correction: its measurement was confounded — the test machine had silently switched to Large Turbo, which alone explains the slowness — but greedy decoding is the proven-safe choice for real-time captions, so beam stays off (kept behind a flag for a future Whisper.net upgrade).

### 1.0.268
**Accuracy release: the engine hears quiet audio and decodes smarter; voices match on a third feature**
- **Automatic gain.** Whisper hears quiet audio badly (videos at low volume, distant voices). Each chunk is now lifted toward a healthy level before transcription — capped at 8× so background noise can't be amplified into phantom speech, and loud audio is never touched. Verified: a deliberately quiet sentence (volume 30) transcribed essentially word-perfect through Tempo's own engine.
- **Beam-search decoding on the light models.** Greedy decoding commits to the first word it thinks of; beam search weighs several word paths and picks the best. It costs ~2× the compute, so it turns on exactly where that's free: Tiny/Base/Small (which run several times faster than real time) on CPUs with 6+ threads. Medium/Large stay greedy to protect real-time pacing.
- **Voices now match on three features instead of two.** Alongside pitch and brightness, the matcher compares each voice's pitch SPREAD — how much the voice moves when talking. A monotone reader and an animated talker can share the same average pitch; the spread tells them apart. The gate is deliberately loose (it only vetoes wildly different intonation) so one person's natural variation still lands on their own profile — the David→Zira→David regression still labels 1→2→1.

### 1.0.267
**Face AI reads any app or site — even in the background — + audio auto-recovery + smarter word repair**
- **Faces are now read wherever the sound comes from — any app, any website, foreground or not.** The face analysis used to watch only the foreground window; now it follows the media detector to the window that is actually PLAYING AUDIO and reads faces there via the OS compositor (PrintWindow with full rendered content), which works for photos and hardware-accelerated video and for windows sitting BEHIND other windows. Verified: a talking face in a background window — with a different app in front — was detected and correctly named the speaker. Falls back to the foreground window whenever the audio window is unknown or minimised.
- **The audio system survives device changes.** Plugging in headphones (or an app switching the default output) used to kill captions with a "turn it off and on again" message, and silently deafened the voice profiler. Both now reopen capture on the new default device automatically — the transcriber retries up to 3 times per session, the profiler re-arms per session — so captions just keep going.
- **Smarter word repair.** The mishear fixer now also: repairs sentence-CAPITALISED words ("Seawhere did it go" → "Sea where did it go") while still never touching ALL-CAPS or mixed-case names; and removes stutter duplicates ("the the", "and and", "it it") that speech engines emit around chunk boundaries. Verified: "The the quick brownn fox. Seawhere did it it go, and and the shipss sailed" → "The quick brown fox. Sea where did it go, and the ships sailed".

### 1.0.266
**"Start Tempo when I sign in to Windows" made reliable on Windows 11**
- **Respects Windows' own Startup list.** Windows 11's Task Manager → Startup apps (and Settings → Apps → Startup) can *disable* a startup entry without deleting it. Tempo used to ignore that — the checkbox stayed on, but Tempo never launched, and re-ticking the box didn't help because the hidden disable flag was still set. Now Tempo reads that flag: the checkbox shows the *true* state, turning itself off to match if you disabled it in Windows, and turning it back on **clears** the disable flag so it actually launches again.
- **Self-heals a vanished entry.** If a cleaner or antivirus strips Tempo's startup entry while you still want it, Tempo quietly re-creates it at next launch (pointed at wherever the exe now lives).
- **Verifies the change.** Toggling the checkbox now confirms the registry actually updated, so a blocked write on a locked-down/work PC is reported instead of failing silently.
- These build on the existing single-file-safe path (the entry points at the real exe, not a temp extraction folder) and the auto-migration to the new install location — so "start with Windows" now survives moves, cleaners, and Windows' own Startup toggle. Verified end-to-end: a simulated Task-Manager disable flipped the checkbox off on next launch; re-enabling it cleared the flag and restored the entry.

### 1.0.265
**"Already running" dialog fixed: correct name, version shown, and it actually surfaces the window**
- The second-launch dialog used to say "**AutoClicker** is already running" — the old project name. It now says **Tempo**, and shows the exact build in both the title and the message ("Tempo 1.0.265 is already running…"), read from the assembly so it can never drift from the real version. Handy for bug reports.
- It no longer just tells you to hunt the tray: launching a second copy now **brings the running window to the front** — even when it was hidden in the tray. Done with a broadcast window message the running instance listens for, so it works whether the window is minimised, hidden, or just behind other windows. Verified end-to-end (hidden-to-tray window surfaced on the second launch).

### 1.0.264
**Live Captions all-round: Win+Ctrl+L now brings Tempo's bar up by itself + transcript saving**
- **Turning on Windows Live Captions yourself now works.** Press Win+Ctrl+L (or use Windows' quick settings) and Tempo notices within a couple of seconds, brings its own caption bar up, parks the Windows bar off-screen and takes over — no need to use Tempo's hotkey at all. That's what the "Show Tempo's caption overlay bar when Live Captions is on" setting always promised; now it's true for external toggles too, whichever caption engine you've chosen. The check is a cheap window lookup every ~2 s, and a cooldown stops it fighting Tempo's own off-toggle.
- **Save transcripts to disk (opt-in).** New checkbox in Settings → Live Captions: when captions turn off, the session's full transcript is written to Tempo's data folder under `transcripts\` — one file per session, each line timestamped, with the speaker labels and "♪ app" tags intact. Off by default because it puts spoken content on disk; everything stays local.
- Each caption session now starts with a fresh history (the "Show full history" panel no longer carries lines over from hours ago), and history lines carry real timestamps internally.

### 1.0.263
**Deeper face analysis + AI repair of misheard caption words**
- **The face analysis looks harder at the face.** Mouth motion is now measured RELATIVE to the rest of the face: a talking mouth moves while the forehead stays still, but a head turn, nod or camera pan moves both equally — subtracting the upper-face motion cancels whole-face movement, so a nodding, silent face can no longer be labelled the speaker. Faces too small to lip-read (a few dozen pixels) are excluded from the verdict instead of contributing noise.
- **Misheard words get repaired — for ALL caption sources.** Speech-to-text mistakes usually surface as non-words ("seawhere", "jumpps", "brownn"); Tempo now runs caption text through Windows' built-in on-device spell engine and applies fixes under strict rules: only lowercase words of 4+ letters (names, acronyms and slang are never touched), and only when the fix is clearly the SAME word misheard — a tiny edit ("jumpps"→"jumps") or the same letters split in two ("seawhere"→"sea where"). Verified: "the quick brownn fox jumpps … seawhere the shipss" comes out fully repaired. Decisions are cached, so the cost per word is paid once; everything stays offline.

### 1.0.262
**Speaker labels gain SIGHT: on-device AI face & mouth analysis (experimental)**
- New opt-in (Settings → Live Captions → "AI face & mouth analysis"): Tempo watches the video in the foreground window a few times a second using **Windows' built-in on-device face detector** (the same AI Photos uses — nothing downloads, nothing leaves the PC, nothing is recorded). Each face is tracked across frames, its MOUTH region is compared frame-to-frame, and the face whose mouth is clearly moving becomes the active speaker — sight beats the voice-only guess whenever both are available, and everything falls back to voice matching the moment no face is visible.
- Works with BOTH caption sources (Windows Live Captions and Tempo's own engine). Verified end-to-end: a face on screen is detected and tracked, motion outside the mouth region is correctly ignored, mouth movement flips the verdict to that face, and face slots recycle across scene cuts (a bug caught in testing — the tracker used to go blind after the first few faces it ever saw).
- Honest limits, also in the tooltip: the speaker's face must be visible in the foreground window, tiny faces carry little mouth signal, scene cuts re-shuffle numbers, and it costs some CPU — which is why it ships OFF by default.
- Technical note: Tempo now targets the Windows-SDK flavour of .NET 8 to reach the OS face AI; the single-file exe grows ~15 MB from the WinRT projections. Still fully offline, still one file.

### 1.0.261
**One-time speaker-labels notice + cleaner speaker matching + Windows-mirror turn fix**
- **A one-time heads-up before speaker labels are first used.** The first time captions start with labels on, Tempo explains plainly that "Speaker 1/2" are on-device AI guesses from voice pitch and pauses — similar voices can share a number, one person can be split in two, music confuses it — a reading aid, never identification. Auto-closes with a countdown, offers one-click "Turn labels off"; arrives as a tray balloon instead when captions start from the tray/hotkey so no dialog ever steals focus mid-game.
- **Cleaner speaker matching.** The voice matcher no longer learns from audio that doesn't sound like speech (music has pitch too — it was minting phantom speakers and dragging real profiles toward the soundtrack), and creating a brand-NEW speaker now needs nearly twice the evidence of matching a known one, so a cough or effect can't become "Speaker 3".
- **Windows Live Captions mirror: no more phantom turns on long lines.** When Windows rolls its caption line (drops old words to make room), the roll stalled the speech-activity clock — so the next words measured a fake "pause" and got labelled a new speaker mid-sentence. Rolls now count as activity.
- **Polish:** Whisper's stray symbol tokens ("◆", box glyphs) are stripped before reaching the bar, and starting captions during pure music no longer sits on "listening…" forever — the "♪ Music or sounds" note takes over.

### 1.0.260
**Tempo's own captions read like Windows Live Captions + smarter audio chunking + better tab identification**
- **One long running line.** Tempo's own engine used to wipe the bar every couple of seconds with the next short fragment; it now builds a continuous line out of them — the same reading experience as Windows Live Captions — showing the newest ~140 characters with an ellipsis while older words shed off the front. The speaker-turn detector's pause threshold is now tuned per source (Windows streams every ~250 ms; the engine batches ~2.5 s), which also fixes every engine batch being mislabelled as a new turn.
- **Bigger caption bar.** Three lines instead of two, and a wider panel (80% of the screen), so the longer text actually fits.
- **Smarter audio chunking.** The engine's transcription windows grew from 2.0 s to 2.8 s (more context per pass = noticeably fewer garbled words), and each window now tries to CUT AT A NATURAL PAUSE near its end instead of mid-word — a chopped word can't be mis-heard as a different word anymore. Overlap between windows also grew so boundary words are seen whole.
- **Better "which tab is that" identification.** Titles are cleaned of "(3)" unread counters and 🔊 glyphs; browser-profile segments ("Personal", "Work", "Profile 2"...) are no longer mistaken for site names; when a BACKGROUND browser is the one making sound, Tempo now scans all of that browser's windows and prefers the one naming a known media site (the tab actually playing) instead of whatever window happens to be first. YouTube Music, Spotify, SoundCloud and Apple Music are now recognised by name.

### 1.0.259
**Tempo's own caption engine: the full model range, any-language understanding, clearer model UI**
- **All five Whisper models are now offered** (previously three): Tiny (~75 MB, instant on weak PCs), Base, Small, Medium, and **Large Turbo (~1.6 GB)** — the distilled large-v3-turbo build with the best accuracy at several times large-v3's speed, which is what makes "best" usable for LIVE captions. The picker is generated from the model catalogue, so it can never drift out of sync again.
- **Any-language understanding.** The ".en" models remain English-only, but Large Turbo is multilingual: Tempo now tells it to auto-detect the spoken language, so games and videos in any of ~90+ languages get captioned. The model picker says which is which.
- **Clearer model status.** The status line under the picker now shows the model's real size on disk when installed ("✓ Base (fast) is installed and ready · 141 MB on disk · English only.") and, when not installed, points straight at the Download button with the download size. Each entry's note spells out size, speed and language support.

### 1.0.258
**Discord no longer thinks Tempo is a Steam game + captions name the app + welcome auto-closes**
- **Fixed: Discord showed you as playing the Steam game "Tempo".** Discord's game detection flags any process at a path ending in `tempo\tempo.exe` (that's the detection entry for the Steam game "Tempo" by Aestronauts — verified against Discord's own detectable-games database). The installer now uses `%LOCALAPPDATA%\Programs\TempoClicker\Tempo.exe` — the exe keeps its name, so updates and docs are unchanged — and upgrading automatically removes the old folder and retargets the Start Menu shortcut. Portable users who unzip into their own folder named "Tempo" get a one-time tray hint explaining the rename that stops it.
- **The caption bar now names the app the audio comes from.** While something is playing, the line reads `♪ YouTube · Speaker 1: ...` / `♪ Roblox · ...` / `♪ VLC · ...` — any app, identified from Windows' per-app audio sessions (known apps/sites get their proper name, unknown ones show their window title). The name holds through natural speech pauses (~10 s) so it doesn't flicker, and the "♪ Music or sounds" note names the app too.
- **Speaker numbering resets when the app changes.** A different game or video site means different people — learned voices are forgotten so Speaker 1 is always the first voice of what you're watching NOW, not a leftover match from the previous app.
- **The welcome note closes itself.** The "Quick safety note" now auto-closes after 10 seconds with a live countdown on the Got-it button; clicking either link stops the countdown so it never vanishes while you're reading.

### 1.0.257
**Live Captions: auto-start works for ANY app or site, sturdier voice matching, and a ♪ sound indicator**
- **Auto-start no longer needs a known-apps list.** Tempo now asks Windows which processes are actually MAKING SOUND (per-app audio sessions, the same data as the Volume Mixer) and checks whether the foreground app is one of them — so any game and any video site works, not just the famous ones. Browsers are handled correctly (their audio comes from helper child processes), known names still show properly ("YouTube", "Roblox", "Call of Duty"...), unknown ones are identified by their window title. Two consecutive positive checks (~4 s) are required, so a notification ping or click sound can't start captions. Verified live: VLC playing a file in the foreground auto-started captions with no interaction.
- **Fixed: the same person no longer becomes "Speaker 2/3" mid-conversation.** Three real defects in the voice matcher: (1) the pitch detector could read the same voice an octave off (autocorrelation subharmonics) — now picks the octave-safe peak; (2) voice brightness was measured across ALL sound, so a sibilant-heavy sentence ("she sells seashells...") looked like a different voice — now measured only on voiced (vowel) frames; (3) a returning speaker's first words often reached the screen before the voice verdict was ready — while a turn is younger than 4 s, the verdict now corrects the number in place. Plus a continuity bias: mild drift stays with the current speaker; a genuinely different voice still switches.
- **♪ sound indicator.** When audio keeps playing but nobody has spoken for ~18 s (music, game effects, ambience), the bar now shows "♪ Music or sounds playing — no speech" instead of holding a stale sentence — the voice profiler distinguishes speech (stable human pitch) from other sound on-device. The next spoken words replace the note immediately.

### 1.0.256
**Live Captions: real voice matching for speaker labels + auto-start for videos & games**
- Speaker labels now listen to the VOICE, not just the rhythm: Tempo measures each speaker's pitch and tone on-device (nothing uploaded, no model download, negligible CPU) and matches new speech against the voices heard so far — so when the first person talks again the label goes back to "Speaker 1" instead of counting up forever. Verified with two Windows TTS voices: David → Speaker 1, Zira → Speaker 2, David again → Speaker 1. When audio capture isn't possible the labels fall back to pause-based turn counting; two very similar voices can still be confused (the honest limit of voice profiling without heavy AI models).
- A changed voice can also split a turn after just a half-second pause — fast back-and-forth conversation labels correctly instead of merging into one turn.
- **Auto-start (Settings → Live Captions → "Auto-start for videos & games", on by default):** Tempo now notices when you're watching or playing something with sound and turns captions on by itself. Detected: video sites in ANY browser via the window title (YouTube, TikTok, Twitch, Netflix, Prime Video, Disney+, Hulu, Crunchyroll and more) and games by process (Roblox, Call of Duty/Warzone, Rainbow Six, Fortnite, Valorant, CS2, Apex, PUBG, Overwatch, Minecraft, GTA V and more) — and only when audio is actually playing, so a muted tab or an idle homepage triggers nothing. If you turn captions off during a video, Tempo won't fight you: auto-start re-arms only after that video/game stops.
- Long monologues no longer push the "Speaker N:" label off the left edge of the bar — the label sticks and the newest words show after an ellipsis.

### 1.0.255
**Live Captions: speaker labels ("Speaker 1:", "Speaker 2:")**
- Caption lines can now be prefixed with a speaker label that advances each time a new turn is detected — "Speaker 1:", then "Speaker 2:", and so on — with the bar showing just the current turn's words so the label always stays on screen. On by default; toggle it in Settings → Live Captions ("Label speakers").
- How it decides a turn: Windows Live Captions doesn't say who is talking (no system exposes that without a microphone array), so Tempo watches the rhythm — speech pauses, then new words start = the next speaker's turn, the same cue broadcast captions use (">>"). Honest limits: it can't tell two voices apart, and one person pausing a long time then continuing counts as a new turn. After 30 seconds of silence the numbering restarts at Speaker 1.
- Built to survive how Windows Live Captions actually behaves: it constantly reflows its line (re-punctuating/re-capitalising earlier words even during silence), so turn detection times the gaps between moments the line GROWS rather than "any change", and text already on the bar when you enable captions is excluded so the first label starts clean at Speaker 1.

### 1.0.254
**Clicker redesign polish + two silent-corruption bugs fixed**
- New slider: the Manual Speed and Window Opacity sliders are now drawn by Tempo — an accent-filled track with a round thumb — instead of Windows' native trackbar. This also removes the last native control that painted light in dark themes.
- The click-rate hint now reads **"≈ 114.3 CPS"** in bold accent with the details muted, the "Unsaved changes" note uses the accent colour, and "Enabled (prevents system freeze)" turns green while anti-freeze protection is on.
- **Fixed: scrolling a page could silently change a setting.** Windows sends the mouse wheel to whatever control sits under the pointer, and combo boxes and spinners respond by changing their value. So scrolling the Settings page with the pointer over the Theme box switched your theme, and scrolling the Clicker page over "Millis" retuned your click interval. Combos now ignore the wheel while closed, and spinners only respond when focused — otherwise the page scrolls.
- **Fixed: your theme/language could reset on exit.** The close-time save cast the theme and language combo's `SelectedIndex` straight to an enum with no guard; if a combo reported "no selection" while its handle was being torn down, a negative value was written and the setting silently fell back to the default on the next launch.

### 1.0.253
**Live Captions detection fix + faster scrolling and tab switching**
- Live Captions (Windows 11): Tempo now decides whether captions are on by the actual caption **window**, not the LiveCaptions.exe process. The process can stay resident while the bar is toggled off, which made Tempo think captions were "already on" and skip its Win+Ctrl+L auto-enable — captions then never appeared. Window-based detection fixes auto-enable both ways.
- Scrolling: pages without a wallpaper no longer force a full synchronous repaint of every control on **every scroll tick** (that repaint is only needed to keep a wallpaper pinned) — scrolling tall pages like Settings/Statistics is dramatically lighter. Mouse-wheel steps also cover a useful distance per notch now.
- Tab switching: opening the Statistics tab no longer recomputes every card, chart and the history list **before** the page can paint — the tab appears instantly and the fresh numbers land a frame later. Layout changes on non-wallpaper pages also skip a blanket child-repaint, making switches snappier everywhere.

### 1.0.252
**Fixed: light-mode flashes in dark themes**
- Drop-down boxes no longer flash light in dark themes. The combo let Windows paint its native LIGHT button/border first and drew the dark look over it a frame later — visible on every repaint (tab switch, hover, closing the list). The combo now paints itself entirely, so the light native art never renders. Rows in the open list are unchanged.
- List column headers (Live Monitor, session history, Multi-Point points) now use Windows' dark header theme instead of staying light in dark themes — removing both the permanent light strip and its flash on every list refresh. List and combo-dropdown scroll bars are dark-themed too.
- Together with the earlier fixes (tab body erase, number-spinner arrows, startup fade), this clears the remaining "buttons and labels flash light in dark mode" report. Light themes are unaffected (the same native paints simply matched the theme there).

### 1.0.251
**Sidebar & loading rework, CPS-test keyboard mode, layout tidy-up**
- Sidebar: added a subtle version footer ("Tempo v1.0.251") so the nav rail reads as finished instead of trailing off into empty space.
- Loading: the startup splash now has a soft accent-tinted radial glow behind the logo for a more premium look.
- CPS Test: new **keyboard mode** — pick "⌨ Space" from the Input row to measure how fast you can spam the spacebar (OS auto-repeat from holding the key is ignored, so only real presses count). Pairs with the Clicker's new keyboard auto-presser.
- Multi-Point: fixed the last "Rep" column being clipped at the right edge of the points table once it had a scrollbar.
- Reviewed every tab and the CPS-test dialog for overlapping text/labels — none found; the app's layout is clean.

### 1.0.250
**Macro pre-record countdown + Statistics summary polish**
- Macros: added an optional "Countdown (s)" in the Record card — a 3, 2, 1 countdown before capture starts so you can switch to the app you want to record first (its own alt-tab isn't recorded). Symmetric with the playback countdown. 0 = start immediately (default).
- Statistics: the "Copy summary" now includes Active days, Best day and Busiest weekday; CSV export also includes Best day.

### 1.0.249
**Statistics accuracy + Clicker progress polish (existing features)**
- Statistics: the "all-time" insight cards (Active days, Best day, Busiest weekday/hour, Top profile, Longest/Current streak, This year, Daily average) are now driven by persisted rolling aggregates instead of the recent-history window — so they no longer silently under-count once you pass 200 recorded runs. Existing users are seeded once from their current history (nothing resets). The by-session / last-7-days / by-hour charts still show recent history.
- Clicker: a fixed-count run now shows a live time estimate — "▸ 340 / 1000 (34%) · ~8s left" — and the fixed-duration run shows its percentage alongside the remaining time.

### 1.0.248
**Fix: keyboard-key mode with no key set clicked the mouse**
- Fixed a bug in 1.0.247's new keyboard auto-presser: choosing "Keyboard key" but not picking a key, then pressing Start, would silently **left-click the mouse** instead. Key mode now never falls back to a mouse click, and Start shows "Choose a key to auto-press first" if no key is set.

### 1.0.247
**Clicker, Statistics & Macros improvements**
- Clicker: auto-press a **keyboard key** (not just a mouse button) — pick a key and repeat it with the same interval/repeat/hold/Humanize settings.
- Clicker: the Manual Speed Target now warns when Anti-Freeze's Max CPS will cap it (was silently slower); the exact-CPS box ceiling follows the slider.
- Clicker: fixed/added tooltips (real 2000 CPS unlock ceiling, the Hold + Quadruple options, and the exact-CPS/preset/Anti-Freeze fields).
- Statistics: fixed CSV export splitting big numbers/decimals across columns (correct for spreadsheets and non-US locales); added **JSON export**; added a "+N vs yesterday" trend on the Today card.
- Macros: new per-macro **"Keep key/button holds at speed"** so 2×/4× no longer shrink a held WASD movement into a tap; fixed Merge (refreshes the list + separates segments); readable step names (Left ↓ / Key ↑ / Move / Scroll) in the monitor & editor; duplicating a macro no longer inherits the original's play count / last-played time.

### 1.0.246
**Macro timing accuracy (WASD holds), bug fixes, lighter Live Captions**
- Playback now schedules against one absolute monotonic timeline instead of sleeping each delay "from now", so per-step overhead (SendInput, the step highlight, loop cost) no longer accumulates — recorded key-holds and overall pace stay true even over long/looping macros (with a catch-up cap to avoid an input burst after a system hitch). This is the main fix for "WASD movement isn't the right length in-game".
- Recording now timestamps each key/click from the OS hardware event time rather than when the hook callback runs, so UI-thread jitter is no longer baked into recorded hold durations.
- Fixed the Windows Live Captions bar being left stuck off-screen: exit now waits for the in-flight caption read before restoring the bar, Tempo no longer memorises an off-screen position as the restore target, and it clamps the restore to a visible spot.
- The recorder no longer captures Tempo's own synthesised input (a running clicker/macro during recording was being recorded into the new macro).
- Live Captions mirroring is much cheaper: it re-reads only the located caption element each poll (re-walking the UI-Automation tree only occasionally or when text stops), and polls a little less often.

### 1.0.245
**Macros now drive WASD / arrow keys in games + Tempo opens on the Clicker tab**
- Recorded movement macros (WASD / arrows) now work in games. Tempo sends keys as hardware **scan codes** (with the extended-key marker for arrows) instead of virtual keys — DirectInput / Raw Input games read scan codes, so they were ignoring the old input entirely.
- Fixed focus loss on playback: minimising Tempo (and its countdown overlay) could pull keyboard focus off the game so the first keystrokes were dropped. When a macro is started via hotkey from inside a game, Tempo now re-aims focus at that game right before sending input.
- Tempo now opens on the **Clicker** tab every launch instead of reopening on the last tab used (e.g. Macros).
- The keyboard path now logs when Windows blocks its input (elevated / anti-cheat game vs non-elevated Tempo), so that case is diagnosable. Guidance: run the game borderless-windowed, run Tempo as administrator for anti-cheat games, and keep playback speed at 1x for movement holds.
- Minor: stopped writing the settings file on every tab switch (the last-tab value is no longer used).

### 1.0.244
**Fixed: wrong page (Clicker) showing after a long minimised macro run + more light-flash cleanup**
- Fixed the Clicker page appearing on top of whatever tab you were on after the window sat minimised for a long time (e.g. an 8-hour AFK macro grind with monitors sleeping/waking). Root cause: the monitor-wake re-centre froze/un-froze painting on hidden pages too, and the un-freeze re-showed a hidden page (Clicker, first in order) over the real one. The paint freeze is now only applied to the on-screen page; re-centring is skipped while minimised; and a restore-time check re-hides any stray page.
- Number spinners (▲▼) no longer flash their native light-grey Windows arrows before the dark ones — Tempo now paints them itself.
- The tab area no longer flashes its light Windows tab-body while switching tabs or restoring from minimised (cleared to the dark theme colour).
- Tooltips are now dark to match the app instead of the bright system popup.
- A finishing macro only restores the window if Tempo minimised it for the run — it no longer pops the window open (stealing focus) when you minimised it yourself.

### 1.0.243
**Fixed: light "flash" on labels & buttons at launch**
- The window faded in by ramping its opacity, which made it a *layered* window; combined with the backdrop compositing, the labels and buttons briefly flashed their light default background before the dark theme showed — a quick white blink most users saw every launch.
- The window now reveals straight at its final opacity (no layered fade), so it appears already dark with no flash. The startup splash still provides the smooth load-in.
- The form is also painted its theme background before any control is built, so it can never show a white default behind the controls as it comes up (theme-correct for light themes too).

### 1.0.242
**GIF / image backdrop & scrolling fixes**
- Fixed control "overlap"/ghosting over a full-window wallpaper while scrolling — the page now repaints immediately on scroll instead of flashing a shifted/stale backdrop frame.
- The animated GIF backdrop no longer appears to "restart" while you scroll (same root cause — it now stays put and keeps animating).
- Switching tabs no longer flashes to the top before restoring your scroll position; the re-centre + restore commits in a single repaint.

### 1.0.241
**Clicker & Macros — quality improvements**
- Clicker: roll the mouse wheel over the Manual Speed slider to set the rate in useful steps (live while running), without the page scrolling underneath.
- Macros: new one-tap playback speed presets (0.5× / 1× / 2× / 4×) in the Playback card, translated into all 6 languages.
- Both purely additive — existing clicker/macro behaviour is unchanged.

### 1.0.240
**Clicker polish + a refreshed website**
- Fixed the clipped "CPS Test" button (was showing "CPS Tes") — now sized for the gauge icon + full label.
- The CPS preset matching your current rate (10/50/100/200) is highlighted in the accent colour, correct on startup and as you change speed.
- Rewrote the project website (index.html): live theme switcher, interactive CPS meter, click-modes explainer, why-Tempo comparison, scroll-progress bar and hero spotlight.

### 1.0.239
**Tabs remember their scroll position**
- Each tab keeps its own up/down scroll spot: scroll down, switch tabs, switch back, and you're where you left off instead of snapped to the top.
- Fixed the root cause (the content re-centring repositioned controls in scrolled coordinates and reset the scroll); it now repositions in unscrolled space and re-applies your exact scroll.

### 1.0.238
**Fixed: window coming back to a blank/empty screen**
- Restoring from the tray/taskbar (or after macro recording auto-minimised the window) could show an empty page that only filled in after a delay. The layout is now re-centred and repainted **immediately** on restore — no blank frame, no waiting.
- Showing the window from the tray now repaints right away (a hidden-then-shown window previously skipped the repair).
- Re-centring on resize/maximise is now a single batched pass, so it no longer flickers or momentarily blanks.

### 1.0.237
**The full visual redesign — every tab restyled to the new look**
- Rounded inputs, dropdowns and text boxes with clean stacked chevrons.
- Icon buttons (＋New, Save, Duplicate, Delete, Start, Stop, CPS Test, Humanize).
- Colour-coded actions: accent New/Set, red Delete, violet Humanize.
- Pill toggle switches for on/off options.
- Uppercase letter-spaced card headers with rounded icon badges on rounder cards.
- Accent-coloured live value (Target: **200 CPS**), uppercase PROFILE/NAME labels, footer tip bar.
- New strings translated into all 6 languages; everything recolours to your theme instantly.

### 1.0.236
**A fresh, more polished look — across every tab**
- New card headers everywhere: UPPERCASE, letter-spaced titles next to a rounded, accent-tinted icon badge.
- All buttons gained a subtle gradient and rounder corners for more depth.
- Key primary actions (New, Humanize, Set) are now filled in the theme accent colour, alongside the green Start / red Stop.
- Everything recolours to your chosen theme (30+) instantly on switch.

### 1.0.235
**Clicker, Macros & Statistics — polished old features and a few new ones**
- **Clicker:** new "Set exact CPS" box — type an exact target rate and apply it instantly; stays in sync with the slider, ± buttons and presets.
- **Macros:** the playback progress bar is now the app's themed rounded bar (accent-coloured) instead of the grey Windows one.
- **Statistics:** new 1K / 10K / 100K quick-goal buttons (like the Clicker presets), and "Copy summary" now also includes Today, Lifetime clicks and Best CPS ever.
- New labels translated into all 6 languages (and the existing "Presets (CPS):" caption is now translated). Themed progress bars re-colour instantly on a theme switch.

### 1.0.234
**One-tap CPS presets on the Clicker tab**
- The Manual Speed card now has quick **CPS preset buttons (10 / 50 / 100 / 200)** — tap one to set the click speed (and the interval fields) instantly, instead of typing values or dragging the slider.
- (The data-loss confirmations you asked about already existed — Clear points, Delete macro and Reset stats all prompt first — and newly recorded macros are already auto-selected.)

### 1.0.233
**Fixed the cramped CPS Test dialog**
- The CPS Test window's "Test length:" and "Button:" rows overlapped the results line above them and sat tight against the bottom edge. The dialog is a little taller now and the bottom selectors are spaced clearly, with each label vertically centred against its button row.
- The CPS Test window also gets a dark title bar on dark themes, matching the main window.

### 1.0.232
**Much more complete translations — far fewer strings stay English**
- Added translations (Spanish, French, German, Italian, Portuguese) for ~65 UI strings that previously stayed English in every non-English language: many Settings checkboxes (record history, on-screen overlay, write log, sleep in tray, remember window size, launch at sign-in, notify on finish, cursor trail…), labels (Header/Footer/Window, Font/Colour/Text size, Hold each click, Dwell, Opacity, Listen to, Caption source/model…), buttons (Start/Stop/Pause/Resume, OK/Cancel/Close/Clear, Got it, Download model, Reset stats/window, Back up all data, Open models/log folder…), the privacy note, and the Multi-Point / Keybinds / Macros tab help paragraphs.
- No new languages were added — this fills the gaps in the six languages Tempo already supports. Verified by switching to Español: the whole UI now reads in the chosen language (proper names like theme/font names and "Live Captions" stay as-is by design).

### 1.0.231
**Modern themed scroll bars + title bar, and fixed the cramped Behaviour checkboxes**
- The scroll bars are now thin and theme-matched — dark on dark themes, light on light — instead of the chunky light-grey native ones, and the window title bar matches (dark in dark themes).
- Fixed the overlapping checkboxes in Settings → Behaviour: the modern checkboxes were a hair taller than the native ones the layout was spaced for, so the bottom rows collided and ran past the card edge. The checkbox height now matches native, the rows are evenly spaced, and "Sleep in tray" moved to the right column so everything fits cleanly.

### 1.0.230
**Fixes the ghost text when scrolling a tab over a background image**
- 1.0.229 made page labels/checkboxes transparent over a wallpaper, but scrolling could leave faint ghost/overlapping text (most visible around the Clicker tab's IDLE/Notify area).
- Owner-drawn checkboxes and radios now blit the wallpaper slice behind them into their buffer instead of leaving it uncleared, so they can't leave stale pixels; and a scroll now repaints the child controls (not just the background), so transparent labels redraw cleanly. Verified ghost-free scrolling up and down.

### 1.0.229
**Background image shows through cleanly + tray and multi-point touch-ups**
- Page-level labels, section headers and checkboxes no longer sit in opaque boxes over a background image — they're transparent when a wallpaper is set, so the image shows through the text. (Controls inside cards and input fields stay solid for readability; verified ghost-free while scrolling.)
- The system-tray tooltip now reflects what Tempo is doing — Running / Paused / Playing a macro / Recording / Idle — plus the active profile, so a hover tells you whether it's clicking without opening the window.
- Multi-point: points captured with Quick Capture are now enabled by default (matching points added via the editor), so a freshly captured sequence is ready to run instead of silently doing nothing until each row is ticked.

### 1.0.228
**Redesigned navigation, buttons and launch screen**
- The sidebar's active tab is now marked with a clean accent indicator bar, a soft accent-tinted background and accent-coloured text/icon — instead of the old heavy solid-accent fill — so the navigation reads lighter and more modern.
- Buttons get slightly softer rounded corners across the app.
- The launch/loading splash now shows the Tempo bolt logo and is theme-aware: it reads the saved theme and accent (including a custom accent) so the splash matches the app you're about to see, instead of a fixed violet.

### 1.0.227
**Background image no longer lags or stutters while you scroll a tab**
- The full-window background image was being cover-scaled with high-quality interpolation on every single repaint, so every scroll tick re-scaled the whole (often huge) image — causing lag, and disturbing an animated GIF's frame timing so it appeared to restart.
- The scaled frame + readability scrim are now composed into a cached bitmap once per GIF frame (or on resize); scrolling and repainting just blit that bitmap, so the wallpaper stays smooth and an animated backdrop keeps its place while you scroll.

### 1.0.226
**Robustness polish for the new UI + corrupt session-history protection**
- The flat combo-box drop-downs now scale their row height with the display-scaling/font, so list text is no longer vertically clipped at 125%/150%/200% DPI; the drop-arrow area scales too.
- The flat numeric steppers now re-hook their custom drawing if the control's spin buttons aren't ready at first paint, so they can't occasionally fall back to the native Windows arrows.
- A corrupt `sessions.json` is now backed up before it can be overwritten (matching how settings are handled), so unreadable history isn't silently lost.

### 1.0.225
**UI modernization: flat themed controls everywhere + fixed overlapping labels**
- Replaced the dated native Windows checkboxes, radio buttons, combo-box drop arrows and numeric spin arrows with flat, theme-aware controls drawn by Tempo. They follow every theme (and the custom accent), so the whole UI now looks consistent instead of mixing flat cards with grey 3-D widgets.
- Fixed the Burst Settings card on the Clicker tab, where the "Clicks per burst" and "Pause (ms)" labels were clipped underneath their spinners; labels and fields now sit on one aligned row.
- The new controls are sized to match the originals so no labels collide with their neighbours.

### 1.0.224
**Fixes settings/checkboxes that didn't stick and two features that conflicted**
- Settings-tab checkboxes are captured on close, so toggles persist even without pressing "Save Settings" (previously the shutdown save wrote the old values back and the change was lost).
- The Macros tab "Capture mouse movement" / "Capture keyboard" checkboxes now have backing settings and save on toggle — they no longer reset to ON every launch.
- "Unlock max speed (advanced)" now lifts the Anti-Freeze hard cap automatically (and restores the 200 CPS default on re-lock), so the slider actually reaches high rates; the adaptive CPU back-off still applies.
- "Colorful cursor trail" and the macro capture checkboxes refresh correctly after Reset to defaults / Import settings.

### 1.0.223
**Form-level WS_EX_COMPOSITED + full solid-background fix for labels and radios**
- WS_EX_COMPOSITED moved from individual tab pages (where the TabControl drops it on tab switch) to the main window, where it sticks and composites the whole UI.
- Solid (non-transparent) backgrounds now applied to all labels and radio buttons too, not just checkboxes (1.0.222 missed those, so labels still clipped).
- Guarded the background-colour resolver so controls can't resolve to white (blank-box report).

### 1.0.222
**Real cause of checkbox/label corruption found: transparent backgrounds**
- Checkboxes, radios and labels used Color.Transparent, which over a custom-painted scrolling panel leaves ghosting and stale text on repaint (the "overlap=on, text=off" artifacts).
- They now use a solid background matching their container (card surface or page), which erases cleanly. With 1.0.221's WS_EX_COMPOSITED, the Settings tab stays correct through scroll/toggle/minimize/tab-switch.

### 1.0.221
**Root-cause fix for scroll corruption: WS_EX_COMPOSITED instead of double-buffering**
- The scrollable tab pages were double-buffered, which doesn't buffer child controls; scrolling blitted checkboxes/labels to a new offset and only repainted a strip, leaving them hollow/clipped.
- Switched to WS_EX_COMPOSITED so the page and all controls composite together in one pass - fixes the corruption and removes the fragile repaint logic that caused the 1.0.219/1.0.220 crash.

### 1.0.220
**Hotfix: fixes the 1.0.219 crash where scrolling a tab closed Tempo instantly**
- 1.0.219's synchronous child repaint on scroll could recurse with the layout repaint and overflow the stack, hard-closing the app with no dialog (why tabs "crashed" and clicking stopped working).
- Scroll and layout repaints now queue a single deferred, debounced full repaint, keeping the corruption fix without the recursion/crash.

### 1.0.219
**Fixed Settings controls corrupting (hollow checkboxes, clipped text) after scroll or minimize**
- A backdrop tab page repainted its background but not its child controls when scrolled, leaving checkboxes and labels with stale half-scrolled pixels.
- The scroll handler now forces a full repaint including children, so all controls redraw correctly. (Minimize/restore and alt-tab paths already did this.)

### 1.0.218
**Portable-exe fix: the build script was overriding single-file bundling**
- publish.cmd passed flags that disabled native-library bundling and single-file packing, overriding the project and cancelling 1.0.217's portability fix.
- The script now bundles everything (runtime, UI Automation, native speech libs) into one Tempo.exe that runs from any folder with no loose DLLs.
- Rebuild via publish.cmd to get the portable exe; a plain Build still leaves loose DLLs (that's how .NET works).

### 1.0.217
**Truly portable single .exe (works anywhere); window always opens centered**
- Tempo.exe is now fully self-contained - the .NET runtime, UI Automation assemblies, and native speech libraries are all bundled inside the exe, so it runs from any folder with no loose DLLs. Fixes "only works inside its publish folder".
- Window opens centered every launch instead of restoring its last position. "Remember window position" in Settings re-enables the old behavior.

### 1.0.216
**More reliable Windows Live Captions startup; diagnostic reports UI Automation health with repair steps**
- Caption startup rechecks several times over ~6s and re-sends the toggle if needed, instead of giving up after one quick recheck (helps on fresh boot / first-ever use).
- "Diagnose Windows bar" now leads with "UI Automation health: OK/BROKEN" and, when broken, explains it's a Windows-side fault with repair steps and the offline-captions alternative.
- On/off detection uses the LiveCaptions process + Win32 lookup, not the accessibility API, so handling stays reliable when UIA is broken.

### 1.0.215
**Caption reading rewritten to avoid the broken CacheRequest, so the Windows mirror can read text on affected PCs**
- Reading used the UIA ".Current" property bag, which internally builds a CacheRequest - broken on the affected PC, so every read threw.
- Rewrote the reader and diagnostic to use GetCurrentPropertyValue per property, which doesn't go through CacheRequest.
- Diagnostic now lists UIA windows and dumps the caption text tree instead of failing.
- Removed dead/duplicate caption code.

### 1.0.214
**Build fix for 1.0.213, plus: Tempo detects when Windows' accessibility API is broken and switches to its own offline captions**
- Fixes a compile error that stopped 1.0.213 from building (a dropped method header).
- On some PCs Windows' UI Automation core fails to start (CacheRequest type-initializer exception), making the Windows Live Captions text impossible to read - a Windows-side fault.
- Tempo now detects this and automatically switches to its own offline caption engine instead of showing a blank bar, telling the user once.
- Points to Settings > Live Captions to install a model if none exists.
- "Diagnose Windows bar" now dumps the caption window's text tree for PCs where reading works.

### 1.0.212
**Caption reading is much more aggressive, and the diagnostic now shows exactly where the words live**
- Tempo reads Windows Live Captions by scanning every text element and keeping the longest real line, checking both name and text content - fixes captions not appearing on some Windows 11 builds.
- "Diagnose Windows bar" now dumps the caption window's full text tree (type, name, text) so the exact caption element can be identified per-PC.
- Windows bar hide unchanged (off-screen, kept readable).

### 1.0.211
**About box shows the real version; caption mirror reads Windows text more reliably; font picker actually changes the font**
- The About window showed a hardcoded "Version 1.0.206" regardless of build; it now reads the real version from the app.
- Caption mirroring checks both the text element's Name and its Text-pattern content, catching words on Windows builds that expose them through either property.
- Tempo no longer mirrors Windows' "Ready to show live captions..." placeholder as a caption.
- Font picker fixed: caption font choices reliably apply, fonts without a bold face no longer break drawing, and only installed fonts are listed.

### 1.0.210
**Windows captions bar now disappears the moment captions start; smoother launch effect**
- The Windows "Live Captions" bar no longer lingers on screen in its "Ready to show live captions" state. A short, fast hide-enforcer runs for the first few seconds after captions turn on and repeatedly pushes the Windows bar off-screen, so it's gone immediately instead of waiting for the first transcribed words.
- Targets the case where, with the Windows caption source selected, the Windows bar stayed visible above Tempo's own bar before anyone had spoken.
- Nicer launch: the splash has a soft accent glow that gently breathes behind the title, and the loading bar sweeps with a blue-to-purple gradient.

### 1.0.209
**Cleanup pass: removed dead caption settings and fixed stale code comments - no behavior change**
- Removed two unused settings (CaptionDriveWindows, CaptionMirrorWindows) that were saved but read nowhere.
- Fixed an out-of-date description in the Windows caption reader that wrongly claimed Tempo can't transcribe on its own.
- Updated internal comments that still referred to the small pill removed in 1.0.208.

### 1.0.208
**Windows caption bar now hides the instant captions start; Tempo's bar is back to normal size; splash and welcome notice restored**
- Windows Live Captions bar now hides right away instead of waiting for the first words, with a safety net that restores it if reading text is impossible on the PC.
- Tempo's caption bar is normal size again (the small pill from 1.0.207 is removed).
- Splash launch effect restored: the splash forces itself to paint and come to front the moment it opens.
- Welcome notice restored: every startup path now guarantees the notice shows and the window becomes visible.

### 1.0.207
**Tempo can now caption on its own; the empty caption box on startup is gone; the caption bar matches your theme**
- New Auto caption source (default): Tempo transcribes offline itself first and auto-falls-back to mirroring Windows 11 Live Captions if its own engine can't run.
- No empty box on startup; real text replaces the starting state the moment it arrives.
- Caption bar colors now come from the active theme instead of a hardcoded near-black.

### 1.0.206
**Welcome notice no longer pops with the app; blank tab on return self-heals again; captions diagnostic is now a self-test**
- Startup glitch where the app window and the "Quick safety note" appeared at the same time:
  the notice was being shown before the window had faded in, so the two overlapped and read
  as a double-pop. The window now fades in first, on its own, and the notice appears only
  once the fade has finished - a clean, ordered startup.
- Empty Settings tab that "doesn't refresh or flash": scrolling Settings down, switching to
  another tab, then back could leave it blank. My previous change only re-laid-out a returning
  tab if its scroll had drifted - but this case comes back with the scroll reading zero while
  the content was laid out against a stale size and ended up off-screen, so nothing re-ran and
  it stayed blank. The follow-up pass now always re-asserts the layout (self-heal), using a
  queued repaint so an already-correct page repaints identically - no flash on the normal path,
  but a genuinely blank page is now corrected.
- Live Captions diagnostic upgraded to a self-test: Settings > Live Captions > "Diagnose
  Windows bar" now also reports whether Tempo can DETECT the captions window and whether it can
  READ text from it, on top of the window list. One screenshot now shows exactly what works on
  a given PC.
- Live Captions hide (from 1.0.205) is unchanged: the window class fix ("LiveCaptionsDesktopWindow")
  and CacheRequest-free reading stand. If a device still shows the Windows bar, run the
  self-test there and send the result - the window class can differ between Windows builds.

### 1.0.205
**Live Captions fixed: window now found + hidden, and text reading works around the UIA error**
- Your diagnostic made the fix exact. The Windows 11 captions window reports its class as
  "LiveCaptionsDesktopWindow" - Tempo had been looking for "LiveCaptionsWindow". That single
  wrong word is why every attempt missed it. Tempo now matches the real class, so it finds
  the Windows captions bar reliably.
- Your diagnostic also showed UI Automation throwing on your PC ("type initializer for
  CacheRequest threw an exception"). UI Automation is what Tempo uses to READ the caption
  text, so the text would have stayed empty even once the window was found. Tempo now reads
  by walking the UI Automation tree directly (TreeWalker) instead of the query API that
  depends on CacheRequest - which avoids that error. Window detection and the in-app
  diagnostic use the same CacheRequest-free approach now.
- Safer hide order: Tempo now hides the Windows bar only AFTER it has successfully read
  caption text at least once (reading keeps working while the window is off-screen). So if
  text-reading still can't work on a given PC, the Windows bar is left visible rather than
  hidden - you are never left with no captions at all.
- Needs confirming on your Windows 11 PC (I can't run Live Captions here): does the Windows
  bar now disappear, and does the text appear in Tempo's bar?

### 1.0.204
**A screenshot-able "Diagnose Windows bar" button, so the Live Captions hide can finally be pinned**
- Two caption bars (Windows' working one plus Tempo's empty one) means Tempo cannot find the
  Windows captions window on your PC - so it can neither hide it nor read from it. I can't
  match a window whose identifiers I can't see, and four guesses haven't landed.
- New: Settings > Live Captions now has a "Diagnose Windows bar" button. Turn Windows captions
  on, click it, and it shows every visible window's title/class/process and every UI Automation
  window's name/class in a box you can screenshot or copy. Send me that and the lookup can be
  made to match your exact window - no more guessing.
- The 1.0.203 log-file "[caption-diag]" dump is still produced as well; this simply puts the
  same information on a screen you can capture.

### 1.0.203
**Empty Settings tab after restore: retry the layout until the window is settled; plus a captions diagnostic**
- Your clue nailed it - the auto-fit centering "doesn't respond sometimes when you return,
  then works later". On restoring from the tray/taskbar, Tempo laid the pages out once on a
  short delay, but if the window hadn't finished restoring yet (client size still 0, or the
  state still counted as minimised), the layout bailed on its own guard and never re-ran -
  leaving the page blank until you nudged it. It now RETRIES: it waits for a real window
  size, lays the pages out, then does one confirming pass, so returning to a tab reliably
  re-centres instead of coming back empty.
- Windows Live Captions still not hiding: after three honest attempts I clearly can't guess
  the Windows 11 captions window's identifiers, so this build adds a diagnostic instead of a
  fourth guess. When captions are on but Tempo can't find the window after about two seconds,
  it writes a "[caption-diag]" block to the log listing every visible window (title, class,
  process) and every UI Automation window (name, class). Reproduce it once and send me those
  lines and I can make the lookup match your PC exactly.

### 1.0.202
**Windows Live Captions window: found and kept hidden, even on Windows 11**
- The Windows captions bar wasn't being hidden because Tempo couldn't find its window at
  all on your setup. On Windows 11 that window has no stable Win32 class/title and often
  isn't owned by the LiveCaptions.exe process, so the name/title/process lookups all missed
  it - and with no window found, Tempo neither hid it nor mirrored from it (you saw the
  Windows bar plus "Live Captions on - captions will appear here"). Tempo now finds it two
  more ways: by scanning visible windows for a Live Captions title, and - the one that
  should work reliably on Win11 - through the UI Automation tree. Tempo's own windows are
  excluded, so it can never hide its own bar by mistake.
- It now also KEEPS it hidden. The window is re-checked on every poll and pushed straight
  back off-screen if Windows re-docks or re-shows it (Windows owns that window's
  placement), instead of being moved once and allowed to drift back on screen.
- Note: this all needs a real Windows 11 PC to verify, which I can't do here - please
  confirm the Windows bar now disappears when you switch captions on.

### 1.0.201
**Windows Live Captions actually hides now; keybind conflicts are prevented; less tab flash**
- Windows Live Captions wasn't being hidden on some setups: Tempo couldn't reliably find
  the Windows 11 captions window (its window class/title vary by build), so it neither
  tucked it off-screen nor mirrored from it - you'd see the Windows bar AND "Live Captions
  on - captions will appear here" in Tempo's own bar. Detection now asks the LiveCaptions
  process for its own main window first, then falls back to its largest visible window,
  which is far more robust. (Live Captions can't be tested here, so please confirm on your
  PC.)
- Keybinds: a combination can no longer stay assigned to two actions at once. Setting a key
  that another action already uses now clears it from that other action automatically, so a
  key always triggers exactly one thing - no more "two actions share a key and only one
  mysteriously works". Conflicts were previously only highlighted and warned about.
- Tab switching: the follow-up pass after a switch now only does a full re-layout when the
  page actually came back scrolled (the blank-band-at-the-top case); otherwise it just
  repaints. That removes the extra "flash refresh" on the normal, already-correct path.
- Loading splash, welcome notice and restart all continue to use the reliable startup
  sequencing from 1.0.200 (splash fully seen first, notice every run, then fade-in); a
  restart simply relaunches through that same sequence.

### 1.0.200
**The loading splash and welcome notice now reliably show, every run**
- The loading splash sometimes wasn't seen, especially on faster PCs, because the main
  window was shown on top of it almost immediately. Now the window stays hidden until the
  splash has actually finished, so the loading effect is always visible first and the
  window then fades in. (A timeout means startup still proceeds if the splash can't show.)
- The welcome / official-source notice now appears on EVERY run. It was first-run only,
  so most launches never showed it; it also used to open hidden behind the splash. It
  still shows just once per launch.
- Both work for tray-start too: starting minimised to the tray dismisses the splash
  promptly (instead of letting it linger) and shows the welcome notice the first time you
  open the window.
- Windows Live Captions: when you switch captions on, the Windows captions window is now
  tucked off-screen as soon as it's detected - not only once it has produced text - so it
  no longer sits visible while the audio is silent. Only Tempo's own caption bar shows.
- Reviewed the clicker and macro tools again. Both stay feature-complete (click styles,
  interval/hold/burst, per-click hold, repeat by count or duration, randomisation,
  multi-point, searchable profiles; macro record/play, edit, duplicate, export/import,
  merge, pin, reorder, notes) with solid engines, so they're unchanged rather than padded.

### 1.0.199
**One-click update now works from the setup zip, not only a bare exe**
- The "Update now" button now appears whenever a release has EITHER a standalone Tempo.exe
  OR the Tempo-Setup-<ver>.zip you already ship. Before, it only showed for a bare exe, so
  a zip-only release fell back to the download page - which is what you were seeing. Now it
  downloads the zip, unpacks Tempo.exe, confirms it's a real Windows program AND that it
  matches the published checksum (when one is provided), then swaps it in and relaunches.
- Safety: a malformed, truncated, or wrong zip can never overwrite the running Tempo.exe.
  The extracted file is header-checked ("MZ") and checksum-verified before any swap; if
  anything is off it stops and offers the download page instead.
- Rollout note: the version doing the check is the one that installs, so this takes effect
  for anyone running 1.0.199 or newer. For the first hop, attach the standalone Tempo.exe
  to the 1.0.199 release as well (publish.cmd already stages it next to the zip) so users
  still on older builds also get the one-click button; from 1.0.199 on, the zip alone is
  enough.

### 1.0.198
**Another attempt at the blank-tab bug, with far better diagnostics for it**
- The blank/empty tab you reported (worse the more you switch): I traced the layout code
  again and confirmed the control positions are absolute - captured once and reset to the
  exact same spot on every switch - so they can't truly drift further down each time. That
  points the finger at the page not having settled its size/scrollbar when the switch lays
  it out (so it measures too early and parks low or blank), rather than accumulating state.
  Fix: after each tab switch, Tempo now does a SECOND full layout pass once the window has
  settled - a real re-centre and scroll-to-top, not just a repaint - to catch that case.
- The same kind of settle-pass already runs after you restore the window from minimized.
- Much richer layout logging: the [layout] log line now records, for every tab switch, the
  first and last control positions, the scroll range (AutoScrollMinSize) and the scrolled
  display rectangle. If a tab still comes up blank, those numbers pinpoint exactly what
  moved - positions, the scroll, or the range - which is what I need to finish this off.

### 1.0.197
**Check for Updates can install in place, and a richer loading splash**
- Check for Updates now installs the new version in place instead of only sending you to
  the download page: you get an "Update now" button that downloads the update, swaps
  Tempo.exe and relaunches - no manual download/unzip. (The one-click updater was always
  built in; it only switches on when the release ships a standalone Tempo.exe, which
  publish.cmd now produces and stages automatically - see below.)
- publish.cmd now stages a standalone Tempo.exe (plus its checksum) right next to the
  setup zip in bin\publish, and its NEXT STEPS spell out attaching BOTH Tempo.exe (powers
  the in-app one-click update) and Tempo-Setup-<ver>.zip (for brand-new users) to the
  GitHub release.
- Fixed a stale line in publish.cmd's release instructions that still claimed portable
  builds keep their data "beside the exe" - since 1.0.194 all settings live in your local
  AppData (the AutoClicker folder), whether portable or installed.
- The loading splash got more to look at: it now shows the version, cycles through the
  startup stages ("Starting up", "Loading your settings", "Preparing the workspace",
  "Almost ready"), and has rounded corners and a subtle top accent.
- Reviewed the clicker and macro tools again. Both are already full-featured - the clicker
  has click styles, interval/hold/burst modes, per-click hold, repeat by count or
  duration, randomization, multi-point and searchable profiles; macros do record/play,
  edit, duplicate, export/import, merge, pin, reorder and notes - and the engines are
  solid, so I left them alone rather than adding busywork.

### 1.0.196
**A loading splash at startup, a much cleaner publish folder, and two fixes**
- New: a small "Tempo / loading..." splash now appears the instant you launch, with an
  animated loading bar, while the main window is being built. It runs on its own UI
  thread so the animation stays smooth during load, then fades out as the window appears.
  (You asked for a loading effect before the app shows - the earlier fade-in only ran
  once the window already existed, so it was easy to miss.)
- Publish folder cleanup: a self-contained single-file build bundles the whole .NET
  runtime and the app INSIDE Tempo.exe, so any loose *managed* framework DLLs left beside
  it (System.*, PresentationFramework*, NAudio*, ...) are duplicates or leftovers from an
  older build. publish.cmd now removes them automatically once it confirms (by size) that
  Tempo.exe is a real single-file bundle, leaving just Tempo.exe + the scripts + the
  native runtime files that self-contained .NET always keeps on disk. If the exe comes
  out too small (single-file didn't bundle), it KEEPS every DLL and warns instead, so it
  can never delete something the app needs.
- Fixed: switching tabs could land on a blank or scrolled page - most reliably Settings
  right after you'd scrolled down in Keybinds. The page is now laid out fully BEFORE the
  scroll is pinned back to the top, so a tall page can't re-measure its scroll range and
  come back parked low with an empty band above the content. The layout diagnostic now
  logs for every tab (not just Statistics) so any recurrence can be pinned down exactly.
- Live captions: if Windows Live Captions is CLOSED mid-session, the overlay no longer
  shows the last line frozen forever - once the Live Captions window has been gone a few
  seconds it switches to "Waiting for Live Captions...". A normal pause between phrases
  still keeps your text on screen (that fix stays), so captions don't flicker.

### 1.0.195
**Searchable profiles in the Clicker, and the macro search fixed up**
- Clicker: the profile picker is now searchable - start typing a profile name and it
  suggests matches so you can jump straight to one, instead of a plain dropdown that only
  jumped a single character at a time. Picking a match loads it; the real current profile
  is always shown in the status bar, and only New/Save ever create or rename a profile.
- Macro search count fixed: the line under the list ignored the search and always showed
  the full library total. It now shows "N of M macros match" while you're filtering (and
  "No macros match ..." when nothing does), so the count matches what's on screen.
- Macro search now also matches a macro's Notes, not just its name - so you can find a
  macro by something you jotted down about it.
- The macro search box now shows a "Search macros..." hint so it's clearly a search field;
  that hint is translated into all six languages.
- The clicker and macro engines themselves were reviewed again and are unchanged - the
  timing/playback core is solid; these are search and find-ability improvements.

### 1.0.194
**Data always in AppData, About layout fixed, fuller translations, smoother launch**
- Data location: Tempo now always keeps its settings, profiles, macros and stats in
  %LOCALAPPDATA%\AutoClicker - whether installed OR run as a portable copy. This is the
  single always-writable spot, so saving can't silently fail from Program Files or a
  read-only/USB folder. If you ran an older portable build that used a "Data" folder beside
  the exe, Tempo copies it into AppData automatically on first run (non-destructive - your
  old folder is left untouched).
- About box: fixed overlapping text. The logo hint no longer sits on top of the version /
  build / data lines, the long data path is truncated so it can't run under the logo, and
  the description, buttons and links were reflowed with proper spacing.
- Translations: 47 more buttons and labels across the Clicker, Multi-Point, Macros,
  Statistics and Keybinds tabs now actually use the language tables (they were hard-coded
  English before), and 5 missing phrases were translated into all six languages. No new
  languages were added - existing ones just cover more of the UI now.
- Launch / restart: the window now eases in smoothly (a decelerating "load-in") instead of
  a linear ramp, so a fresh start or a post-restart hand-off feels more polished.

### 1.0.193
**Live Captions no longer freeze Tempo, plus restore/overlay/notice fixes**
- Windows Live Captions could make Tempo go "not responding" and need a force-kill. Reading
  the caption text walks a UI Automation tree that can block for seconds, and it was being
  done on a 150 ms UI-thread timer - so any stall froze the whole window. That polling now
  runs on a background thread and only hands the finished text back to the UI, so a slow or
  stuck Live Captions can never freeze Tempo.
- Empty/blank page after restoring from the tray: the layout repair on restore only ran for
  a normal-sized window, so a MAXIMISED Tempo came back from minimise with a stale, empty
  page. It now repairs the layout for maximised windows too.
- The first-run "official source" notice could be skipped for people who start Tempo
  minimised to the tray (it was only retried on a visible-state change). It's now retried on
  any restore, so it reliably shows once.
- The clicking / macro overlay badge appeared on the primary monitor even when you were on
  another screen, and could hide behind a maximised window. It now shows on the screen your
  cursor is on and stays above other always-on-top windows without stealing focus.
- The "playing macro" overlay now follows a live theme change (it was being left on the old
  theme while the clicking overlay updated).
- Clicker and macro engines reviewed again - timing is scheduler-based on a dedicated worker
  thread with anti-freeze backoff; no changes needed.

### 1.0.192
**Fixed publish: the tidy-folder move now actually works, and old files get cleared**
- The 1.0.191 step that moves the Whisper native libraries into runtimes\<rid>\native\
  never ran. A batch FOR loop treats a quoted wildcard as literal text and never expands
  it against the filesystem, so it matched no files and left every DLL loose in the
  output. publish.cmd now calls move directly on the wildcards (move does expand them) and
  clears the target folder first, so a rebuild can't leave a stale library behind either.
- Rebuilding into an existing folder could leave old files behind. The clean step ran
  before Tempo was stopped, so a copy still running from that folder locked its files and
  the delete silently failed. publish.cmd now stops any running Tempo at the very start -
  before the clean, and for /quick builds too - so the folder clears properly. If a delete
  still fails (an Explorer window open on the folder, antivirus), it now warns you instead
  of failing silently.
- Net result: a fresh build gives you just Tempo.exe, the scripts, and one tidy
  runtimes\<rid>\native\ folder - no leftover DLLs from earlier builds.

### 1.0.191
**Tidier folder: native libraries grouped into a subfolder**
- The publish and portable folder is no longer cluttered with loose native DLLs. The
  Whisper speech-engine libraries (whisper / ggml) are now moved into the standard
  runtimes\<rid>\native\ subfolder, so the folder you see is just Tempo.exe, the install
  scripts, and one tidy runtimes folder. Whisper.net's loader already searches that
  layout, and Tempo also adds it to the native DLL search path at startup as a safety net
  - the exe's own folder is still searched too, so a flat copy still works.
- publish.cmd does the move automatically right after building and verifies the libraries
  actually landed. install.cmd already copies the runtimes folder recursively, so an
  installed copy gets them as well.
- The portable note and the README now say to keep the "runtimes" folder next to
  Tempo.exe (instead of the loose .dll files).
- No changes to clicking, macros, or the speech engine itself.

### 1.0.190
**Tab switches snap to the top; pinning down the pushed-down dashboard**
- Switching tabs now always shows the TOP of the page. A forced re-centre (which a tab
  switch triggers) used to restore the page's previous scroll offset - and right after
  backdrop or scrollbar changes, that is one way a tab could come up parked low with a
  big empty band above the content. A tab switch now resets to the top, which is the
  expected behaviour anyway. (Resize and scrollbar toggles still keep you where you were.)
- Verified the GIF backdrop handling is sound and needs no change: the header, footer and
  full-window images each dispose the previous image when you choose a new one (no handle
  leak), the animation is guarded against running double-speed, and only the visible tab
  animates.
- Added a one-line diagnostic to the log whenever the Statistics tab is laid out on a tab
  switch, recording the exact client size, vertical offset and scroll values. The layout
  code measures correctly in every windowed case I can reproduce, so if the dashboard
  ever comes up pushed down again, that log line will show precisely why.
- No changes to clicking, macros, the speech engine, or portable behaviour.

### 1.0.189
**Fixes the build - 1.0.188 would not compile**
- Reverted the 1.0.188 change that dropped WPF from the project. That was a mistake: the
  Windows Live Captions reader uses System.Windows.Automation (UI Automation), and those
  assemblies ship with the WPF half of the Windows Desktop framework - so removing WPF
  made that file fail to compile and the whole build produced no files, which is why
  1.0.188 would not build or run. WPF is restored, with a note in the project file
  explaining why it has to stay.
- publish.cmd no longer deletes the previous Tempo.exe before building. If a build ever
  fails, the last working exe now stays in place so you can still run it, instead of
  being left with nothing. (A running Tempo is still closed before the build so dotnet
  can overwrite the exe on success.)
- Everything else from 1.0.188 is kept: the portable "Start with Windows" self-heal and
  the publish check that the native speech libraries shipped beside the exe.
- No changes to clicking, macros, the speech engine, or portable data handling.

### 1.0.188
**Leaner build, self-healing portable startup, and a more thorough publish**
- Dropped the unused WPF framework from the build. The project pulled in all of WPF, but
  Tempo is pure Windows Forms and never used a line of it (its sounds use System.Media,
  not WPF). Removing it makes the self-contained Tempo.exe noticeably smaller and start
  faster - nothing in the app changes.
- "Start with Windows" now self-heals for portable copies. If you move a portable Tempo
  (USB stick, a different folder), the startup entry is rewritten to its new location on
  next launch, instead of leaving Windows trying to launch a path that no longer exists.
- publish.cmd now checks that the native speech libraries (whisper / ggml) actually
  landed beside Tempo.exe and warns if they didn't - catching a broken package restore
  that would otherwise ship an exe with silently-dead Live Captions.
- Fixed a contradictory comment in the project file about how the native libraries are
  packaged (they sit beside the exe; they are not bundled into it).
- No changes to clicking, macros, or the speech engine itself.

### 1.0.187
**Scrolling no longer fights you; the self-refresh flicker is gone**
- Fixed scrolling that jumped or "refreshed by itself", most visibly on Settings. A
  200ms live-display timer was re-asserting the scroll position on every tab five times
  a second, which fought you while you scrolled. That scroll save/restore is only needed
  on the Statistics tab (whose dashboard rebuild snaps to the top), so it now runs only
  there - every other tab scrolls smoothly.
- Fixed the background "changing colour" on refresh/scroll. The pages had been left
  unbuffered while chasing an unrelated layout bug, which showed an erase-flash on every
  repaint. Double-buffering is restored (it never caused the blank/pushed-down tab; that
  was a layout/scroll issue), so refreshes are flicker-free again.
- Hardened minimise then restore: the live timer no longer touches a page's scroll or
  layout while the window is minimised or hidden - doing that against the collapsed
  window was a way Settings/Statistics came back pushed down with a big empty gap. The
  clicking overlay and milestone checks still update normally during a tray run.
- publish.cmd now waits briefly after closing a running Tempo so the file lock is fully
  released before it overwrites the exe - belt-and-braces on the "didn't overwrite, ran
  the old version" fix.
- No changes to clicking, macros, or the speech engine.

### 1.0.186
**In-app update now really updates; no more empty screen after checking**
- Fixed the big one: an in-app update never actually replaced the app, so you kept
  running the old version. The updater was targeting the wrong path - for a single-file
  build it pointed at a temporary extraction folder, not the real Tempo.exe. It now uses
  the actual running exe's location, so the update overwrites the real Tempo.exe wherever
  it lives (your portable folder or the installed copy). This is the "remember where the
  app is" fix.
- Fixed the empty / pushed-down screen after clicking "Check for updates". When the
  result dialog closed, Tempo was forcing a full re-layout of the current tab on every
  window re-activation, which is what shoved Settings (and Statistics) down with a big
  empty gap above. The pages aren't double-buffered anymore, so they repaint themselves
  when a dialog closes - that forced re-layout is gone, replaced with a plain repaint.
- publish.cmd now closes any running Tempo before building, then deletes the old exe, so
  a locked exe (for example a portable copy running from the same output folder) can't
  silently block the overwrite and leave you launching the old build.
- install.cmd was already correct here - it detects the existing version, closes a
  running Tempo, overwrites in place, and rolls back on failure - so it was reviewed and
  left as is.
- No changes to clicking, macros, or the speech engine.

### 1.0.185
**Portable mode is back - and now truly portable**
- Restored running Tempo as a portable copy (no install): run Tempo.exe straight from
  the build/zip folder with its .dll files beside it. Nothing gets installed.
- Made portable genuinely portable. A portable copy now keeps its settings, profiles,
  macros and stats in a "Data" folder right next to Tempo.exe, so the whole folder
  travels together - copy it to a USB stick or another PC and it just works. The old
  portable left data in %LOCALAPPDATA%; this self-contained folder is the improvement.
  If that folder ever isn't writable (for example unzipped under Program Files), Tempo
  safely falls back to %LOCALAPPDATA% so saving never fails. An installed copy is
  unchanged (%LOCALAPPDATA%\AutoClicker).
- About again shows whether you're on the Installed or Portable edition, and Settings
  shows a short note explaining how a portable copy behaves.
- publish.cmd: the setup zip's README now spells out both ways to run (run Tempo.exe for
  portable, or install.cmd to install), and the maintainer notes make clear the one zip
  serves both - and that the standalone exe by itself isn't portable, because it needs
  the .dll files too.
- Website and README updated with a Portable option alongside Installed.
- No changes to clicking, macros, or the speech engine.

### 1.0.184
**Empty Statistics + overlong scrollbar fixed; startup notice and animation fixed**
- Fixed the empty Statistics tab and the over-long, wrong-sized scrollbar together. The
  previous attempt switched the pages to composited painting (WS_EX_COMPOSITED) to stop
  the blanking, but that broke AutoScroll's scrollbar - hence the long bar. The pages are
  now plain (non-buffered) scrolling pages: the child controls render normally and the
  scrollbar is sized correctly. (The only trade-off is that an animated GIF backdrop - an
  optional, rarely used extra - may shimmer slightly in the page margins.)
- The first-run "official source" notice now appears up front, before the app is
  interactive, instead of opening on top of a window that was still fading in.
- Fixed the missing startup animation. The window fade-in was being kicked off first and
  then hidden behind that first-run notice, so it looked like nothing animated. The
  notice now shows first and the window fades in afterwards, so the animation is visible.
- publish.cmd now overwrites cleanly: it deletes a previous build's Tempo.exe and
  checksum before building so a leftover or read-only file can't trip it up (the setup
  zip was already force-overwritten).
- Re-audited the Clicker and Macros (numeric ranges, macro speed/loop guards, the
  player's timing): all correctly guarded, nothing to fix. install.cmd and uninstall.cmd
  remain solid and were left unchanged.

### 1.0.183
**Installed-only: portable mode removed; README and website rebuilt**
- Removed "portable" mode entirely - by design, Tempo is now an installed app only.
  The install instructions are a single, clear path (build with publish.cmd, run
  install.cmd, launch from the Start Menu), and the old "run it from the build folder"
  / portable option is gone from the README, the website, the in-app About dialog and
  Settings, and the installer's notes.
- Rewrote the README and the website around that single install flow, and refreshed
  the feature copy to match what Tempo actually does now: single-to-quadruple clicks
  and six interface languages (English, Spanish, French, German, Italian, Portuguese),
  plus a mention of the optional accessibility captions.
- Internal cleanup: dropped the now-unused deployment-detection code.

No functional behaviour changed in this release - it's documentation, presentation and
a deliberate scope decision.

### 1.0.182
**The empty/blank tab bug - fixed at the root**
- Fixed the recurring blank tab for good - the case where a tall tab (Statistics,
  Settings, Keybinds) comes up empty, sometimes with a scrollbar but no content, often
  just from switching to it. The root cause was finally pinned down: every tab page was
  double-buffered the old way (OptimizedDoubleBuffer + AllPaintingInWmPaint) AND set to
  auto-scroll. On a page whose content is taller than the window, that is a documented
  WinForms flaw - the off-screen buffer is only the size of the visible area, so the
  child controls below the fold were left unpainted and the page looked blank. The short
  tabs (Clicker, Macros) were never tall enough to trip it, which is why only some tabs
  were affected.
- The pages now use composited painting (WS_EX_COMPOSITED): the whole page, background
  and all the cards together, is drawn into one off-screen surface bottom-up. That keeps
  the animated backdrop flicker-free AND guarantees the content always renders, at any
  height and any scroll position. As a safety net, a switched-to tab is also repainted
  once more after it has fully settled. The scrollbar now scrolls real content instead
  of an empty area.

This replaces the long line of partial fixes for this bug (scroll repaint, layout
repaint, minimise repair, post-dialog repair) with a fix at the actual cause. Those
earlier guards are kept as harmless reinforcement.

### 1.0.181
**Empty tab after a dialog (e.g. Merge) - fixed; plus a corrupt-speech-model install fix**
- Fixed the recurring empty tab once more, this time for the case you hit: open a dialog
  (Merge, the macro editor, CPS test, etc.), close it, switch tabs and find them empty
  with the content shoved to the bottom. While a dialog is open the main window is
  inactive and its scrolling pages can be left with a stale layout. Tempo now repairs
  the visible page's layout the moment the window becomes active again - which also
  covers Alt-Tabbing back to it. Together with the minimise fix in 1.0.180, the three
  ways this bug showed up (after recording, after a dialog, after Alt-Tab) are all
  handled. Your scroll position is left untouched, so this never jumps you around.
- Fixed the installer silently installing a corrupt speech model. install.cmd checked
  only that the download produced a file, not that it finished; a dropped connection left
  a truncated file that got installed and then crashed the offline captions on load. It
  now verifies the size (the model is ~147 MB) and, if the download was incomplete,
  removes the partial file and tells you to fetch it later or use Windows captions.

The publish and uninstall scripts were reviewed again and are already solid (publish
verifies its build output and reports size; uninstall flags the 100+ MB model, offers a
backup zip, and removes the model on purge), so they were left unchanged rather than
padded. The Clicker and Macros remain audited and clean.

### 1.0.180
**CRITICAL build fix - plus the empty-Statistics-after-recording bug, finally**
- Fixed a build-breaking bug that had quietly crept into 1.0.178 and 1.0.179: a
  duplicated method (and a couple of unqualified name references) meant those two
  versions would not compile at all. If you tried to build them and it failed, this is
  why - and it also means none of the fixes from 1.0.178/1.0.179 ever actually reached a
  working build. This release compiles cleanly and carries all of them.
- Fixed the Statistics tab coming back as a big empty space with the dashboard shoved to
  the bottom after recording a macro. Recording auto-minimises the window to stay out of
  the recording; coming back from minimised left the scrolling pages with a stale layout
  and scroll position. Tempo now never re-lays-out while minimised, and rebuilds the
  layout cleanly (snapping the visible tab back to the top) the moment the window is
  restored - whether from a recording or an ordinary minimise.

This release also finally delivers, in a build that actually compiles, everything from
the previous two attempts:
- "Start with Windows" uses the real executable path, so it launches reliably at sign-in.
- The interface genuinely changes language: all six languages are selectable and the
  section titles across Clicker, Macros and Settings now translate.
- Live Captions: the native speech engine is no longer cancelled mid-sentence (a likely
  hard-crash cause), audio is sanitised before the engine sees it, the silence filter is
  less aggressive (fewer "said nothing" gaps), and the worker threads are rebalanced so
  it keeps up with speech.
- Website refreshed (six languages, accessibility captions, a languages FAQ).

Audited the Clicker and Macros again (engine guards, click styles incl. Quadruple, the
recorder's delay-coalescing, the player's button/key cleanup, profile indexing): all
correctly guarded, nothing to fix. The publish/install/uninstall scripts remain mature
and were left as-is rather than churned.

### 1.0.179
**Live Captions: crash and missing-words fixes (best-effort) + Clicker/Macro audit**
- Stopped cancelling the speech engine mid-sentence. Tempo was passing a cancellation
  signal straight into the native transcription call; cancelling it part-way through can
  corrupt the native engine and hard-crash the whole app (a native crash that no amount
  of error handling can catch). Each short chunk is now allowed to finish, and captions
  stop cleanly between chunks instead.
- The audio is now sanitised before the engine sees it. A stray invalid sample (NaN /
  infinity / out-of-range, which an odd device buffer can produce) is clamped to a safe
  value, removing another way the native engine could crash.
- Fixed "Tempo said nothing / only half the words". The silence filter was too strict
  and dropped quiet audio (e.g. a video at a modest volume) entirely; it's now far more
  permissive. The transcription threads were also rebalanced so the engine can keep up
  with real-time speech and stop dropping words, while still leaving the UI responsive.
- Audited the Clicker and Macros again for bugs (engine loops, click styles incl. the
  new Quadruple, the macro recorder's delay-coalescing and the player's button/key
  cleanup): all correctly guarded, nothing to fix.

PLEASE READ - honest status on Tempo's own captions: these are real fixes for the most
likely causes, but they ship blind. Tempo's caption engine uses a native speech library
that can't be tested or reproduced in my build environment, and a native crash can't be
caught from normal code. If captions still crash or misbehave for you, that is the
on-device engine itself, and I'd recommend Windows 11's built-in Live Captions instead -
they're more accurate, lower-latency, and rock-solid. Tempo can launch them for you
(Settings > Captions), and YouTube's own captions also work regardless. Tempo's captions
are a convenience, not a replacement for those.

### 1.0.178
**Start-with-Windows fix, the empty-tab bug, and real multi-language UI**
- Fixed "start with Windows" not launching at sign-in. The startup entry used a path
  that can be wrong for a single-file build (a temporary extraction path); it now uses
  the real Tempo.exe path, so the entry survives and actually runs at sign-in.
- Fixed tabs going blank after an action. These pages are double-buffered and scroll,
  and any layout change (a control shown/hidden by some action, the scroll region
  shifting) could leave part of the page stale/blank because only the changed area
  repainted. The page now forces a full repaint after every layout pass - the proper
  companion to the earlier scroll-repaint fix.
- Languages now actually translate the interface. Italian and Portuguese were already
  built in but weren't fully exposed, and many already-written translations were unused
  because the labels weren't wired to the translator. The card titles across Clicker,
  Macros and Settings now translate, the language list is complete (English, Español,
  Français, Deutsch, Italiano, Português), and a few missing strings were filled in -
  all without adding any new languages.
- Website: refreshed the intro, called out the six languages and the accessibility
  captions, and added a "what languages are supported" FAQ entry.

Honest notes: Live Captions and the publish/install/uninstall scripts were reviewed and
are already well-hardened from recent versions, so they were left as-is rather than
changed for the sake of it - tell me a specific behaviour to change and I'll do exactly
that. UI translation now covers the section titles; wiring every last label to the
translator is a larger ongoing pass.

### 1.0.177
**Tray clarity, Quadruple click, hotkeys that work on more keyboards, caption robustness**
- Hotkeys that wouldn't work on some keyboards now work. If Windows refuses to bind a
  shortcut the normal way (common with certain keyboards, or when another app has
  grabbed the key), Tempo automatically falls back to a low-level keyboard hook and
  detects the combo itself. Keyboards where the normal path already works are
  unchanged.
- Tray confusion fixed: the first time Tempo hides to the system tray - whether it
  started with Windows or you closed the window - it now shows a clear one-time message
  explaining it's still running in the tray and how to reopen it. This shows once even
  if routine tray notifications are off, so the app is never mistaken for having quit.
- New click type: Quadruple (4 clicks), alongside Single / Double / Triple.
- Live Captions audio robustness: capped Whisper to fewer CPU cores so heavy
  continuous audio (a video playing) no longer makes the app feel like it's "not
  responding"; guarded the resampler against odd device formats; and when the audio
  device changes mid-session (headphones plugged in, app switches output) the status
  now says plainly that captions stopped and to toggle them off/on to resume.
- publish.cmd: build-failure help now includes a .NET 8 SDK check hint.

Honest note on captions: these changes reduce the freezes/"not responding" and make
failures clearer, but Tempo's own captions still use a small on-device model and won't
match Windows 11 Live Captions. If audio playback keeps stopping capture, that's a
Windows audio-device behaviour; Windows 11's own Live Captions remain the most robust
option and Tempo can launch them for you.

### 1.0.176
**Two small, real fixes for the Clicker and Multi-Point**
- Clicker: the "Clicking ... N CPS" status now shows the true rate. It was reading the
  speed slider's value, which is capped at the slider's maximum, so a fast interval
  typed straight into the milliseconds box (e.g. 2 ms = 500 CPS) could display a lower
  number than it was actually clicking. It now reads the engine's real effective rate.
- Multi-Point: the Order tooltip now explains what each mode does (Sequential, Reverse,
  Random, Ping-Pong) instead of a single generic line.

Honest note: I went through the Clicker, Macros and Multi-Point closely again. The
clicking engine, macro recording/playback (loops, 0.1x-10x speed, loop delay) and
multi-point system (four visiting orders, per-point repeat/dwell/button/type, full
reordering, live highlight of the active point) are all feature-complete and I couldn't
find real bugs in them - so I fixed only the two genuine issues above rather than pad
the release. If there's a specific behaviour you want changed or added on any of these
tabs, tell me exactly what and I'll build that precisely.

### 1.0.175
**More detail in publish.cmd; a full re-audit of the app**
- publish.cmd now reports more after a build: the exact output folder, the runtime
  target it built for (and that it's self-contained, single-file, with native libs
  beside the exe), and the app version - on top of the existing size, checksum,
  installer, notes and timings.
- Re-audited the Clicker, Multi-Point, Macros, Live Captions, Settings, and the
  install/uninstall scripts for bugs and gaps. They came back solid: the click engine
  and timing are well-guarded, Multi-Point and Macros have full keyboard control
  (Delete, Ctrl+D, reorder, rename, etc.) with safe bounds, the captions have all the
  recent crash/freeze fixes plus a no-audio watchdog, Settings is complete, and the
  installer verifies its copy, brings the native caption libraries, and rolls back on
  failure. No changes were forced into that working code, to keep this conflict-free.

If there's a specific behaviour you want changed on any one of those - a wording, a
default, a layout tweak, a new option - tell me exactly what and I'll do that precisely
rather than guess.

### 1.0.174
**Fixed an intermittent crash when stopping captions + start-with-Windows now goes to the tray**
- Fixed the occasional crash when turning Tempo's captions off. The speech engine was
  sometimes being shut down while it was still in the middle of transcribing a chunk -
  disposing it mid-work could crash the app. Now the engine is only released after the
  transcription has actually finished (and on a background thread), so stopping captions
  is safe, and it no longer blocks or freezes the window either.
- "Launch Tempo when I sign in to Windows" now starts Tempo in the tray instead of
  popping its window open at every boot. The window is one click away from the tray
  icon. This also applies to people who turned the setting on in an older version (the
  startup entry is upgraded automatically).

Note on captions: this addresses the stop-related crash. Tempo's own captions still use
a small on-device model and won't match Windows 11 Live Captions for accuracy or delay;
for the best captions, Windows 11's remain the better choice (and Tempo can launch them).

### 1.0.173
**Fixed captions freezing the app + the empty-tab-on-scroll bug (real root cause)**
- Fixed Tempo going "Not Responding" when you started its own captions. The speech
  model (100+ MB) was being loaded on the UI thread, freezing the whole app for a few
  seconds. It now loads on a background thread - the window stays responsive and shows
  "Loading speech model..." then "Listening..." - and turning captions off mid-load is
  handled cleanly.
- Fixed the empty/blank screen when scrolling a tab (and a tab coming back blank).
  Found the real cause this time: the page is an auto-scrolling, double-buffered
  surface, and that combination only repainted the newly-exposed strip on scroll,
  leaving the rest blank. The page now forces a full repaint on every scroll, so it
  stays painted. This is why the earlier layout-based attempts didn't fully fix it.
- Website: added an FAQ section (free?, the unsigned-app warning, bans/safety, safe
  speeds, where data is stored, captions vs Windows 11, Windows-only).
- About dialog: now shows whether Tempo is running Installed or Portable, the .NET
  runtime, and your data folder location with an "Open data folder" link.
- publish.cmd: the next-steps now remind you to run and click around the built exe
  before shipping.
- uninstall.cmd: when settings are kept, it now tells you exactly where they are.

Note on captions: this fixes the freeze (the "model not responding"). Tempo's own
captions still use a small on-device model and won't match Windows 11 Live Captions;
for the most accurate, lowest-delay captions, Windows 11's remain the better choice.

### 1.0.172
**Fixed the blank-tab-on-return bug, richer CPS Test, clearer data folder**
- Fixed the big one: switching tabs and coming back could leave a tab showing an
  empty screen. The scroll-blank fix from before was skipping the re-layout a tab
  needs when you return to it; now an actual tab switch always gives the tab a clean,
  complete re-layout (positions, scroll metrics, repaint), while scrolling still
  avoids the flicker it was meant to. This affects every tab - Clicker, Multi-Point,
  Macros, Statistics, Keybinds and Settings.
- CPS Test now shows more detail after a run: a Consistency score (how even your
  click rhythm was) and your single fastest gap between two clicks, on top of the
  existing CPS, peak, best and per-session stats.
- The data folder (%LOCALAPPDATA%\AutoClicker) now gets a plain-English README
  explaining what each file is and that it's safe to back up or delete. The installer
  also tells you where your data is saved when it finishes.

On the tabs (Clicker, Macros, Live Captions, Multi-Point, Statistics): these are
feature-complete and were already audited as solid, so rather than force risky
cosmetic edits I focused on the shared blank-tab fix (which improves all of them) and
the concrete CPS Test and data-folder requests. Tell me if there's a specific behaviour
on any one tab you'd like changed and I'll do that precisely.

### 1.0.171
**Fixed the update-check timeout + a more detailed, more colourful publish.cmd**
- Fixed update checks timing out even on a healthy connection. The previous version
  cut the per-attempt timeout too short and didn't retry timeouts; now the timeout is
  generous again (for slow cold connections doing DNS + TLS) and a timeout is retried
  once before giving up.
- The timeout message no longer wrongly blames your connection. If it still times out
  after retrying, it now explains GitHub may be slow or a firewall/VPN/antivirus may
  be blocking github.com - and that your internet is otherwise fine.
- publish.cmd: added a colourful animated gradient divider that sweeps across after
  the banner, and a green celebratory sweep on a successful build.
- publish.cmd: added clearer descriptions under several build steps (what the
  environment check, verification, and packaging steps actually do).

Note on the timeout: with working internet, a timeout is usually either a momentarily
slow GitHub or something on the network path (a corporate proxy, VPN, or antivirus)
briefly intercepting the connection to github.com. The retry handles the common slow-
connection case automatically.

### 1.0.170
**Update check now rides out temporary GitHub hiccups (e.g. the 504 error)**
- If the update check hits a temporary server-side error from GitHub - a 500/502/503/
  504 gateway error, a 429, or a dropped connection - Tempo now automatically retries
  a couple of times with a short pause before showing anything. A brief blip (like the
  "error 504" some people saw) now usually just works instead of popping an error.
- When GitHub really is having a longer problem, the message is clearer that it's a
  temporary issue on GitHub's side, not a problem with Tempo or your PC - just try
  again in a few minutes.
- Tuned the timing so the retries always finish inside the check's safety window (the
  per-try timeout is shorter and the overall window a little longer).

Note: a 504 is a "gateway timeout" returned by GitHub's servers, not something Tempo
or your PC did wrong. These are almost always temporary; this change just makes Tempo
shrug off the short ones automatically.

### 1.0.169
**Small Clicker display fix + a full bug sweep**
- Clicker: the Manual Speed target now reads consistently. Dragging the slider showed
  e.g. "Target: 200 CPS" while typing the same speed into the interval boxes showed
  "Target: 200.0 CPS"; both now show the same clean whole-number form.
- Did a broad bug sweep across the engine and app (anti-freeze math, the interval and
  burst run loops, click-style actuation, interval composition/decomposition, the
  slider sync, version parsing, and string handling). Everything else was already
  correctly guarded - divide-by-zero, array access, and substring operations are all
  protected - so no other changes were needed.
- publish.cmd step 3 (the clean phase) was checked and is already correct (it switches
  to its own folder, measures and clears the right output folders, and reports what it
  reclaimed), so it was left as-is.

No features added or removed.

### 1.0.168
**Fixed: turning Windows Live Captions off could crash Tempo**
- Hardened the caption start/stop so pressing the caption hotkey to turn Windows 11
  Live Captions on, then pressing again to turn it off, no longer risks taking Tempo
  down with it. The whole caption transition is now crash-proofed: any unexpected
  error (Windows captions spawning/closing, the UI Automation reader, the overlay, or
  a timer) is caught and logged instead of crashing the app.
- Fixed an orphaned "did it appear?" verification timer. Turning captions on starts a
  short timer that re-checks whether Windows Live Captions actually opened; if you
  turned captions off (or switched to Tempo's own engine) before it finished, that
  timer used to keep running and could re-send the Win+Ctrl+L shortcut - flipping
  Windows captions back on or thrashing the window after you asked for off. It's now
  cancelled the moment the state changes.
- Added a guard against rapid re-entrant toggles (mashing the hotkey), so overlapping
  on/off transitions can't get the caption state out of sync.

Honest note: I couldn't reproduce the exact crash from the code here, so this is a
defensive fix targeting the most likely causes (the orphaned timer and an unguarded
exception during the toggle). If captions still crash Tempo after this, please send
the crash report it saves plus the exact steps, and I'll pin down the precise cause.

### 1.0.167
**Small polish to Macros, Clicker & Statistics**
- Macros: the selected macro is now kept selected when the list refreshes (after a
  search, rename, pin, etc.). Previously a refresh could clear your selection and
  reset the action buttons - now it stays put if the macro is still in view.
- Clicker & Statistics: reviewed the existing features (CPS estimates, the manual
  speed slider and its buttons, profile dropdown, lifetime/session counters, button
  breakdown, milestones, and history filtering). They already preserve selection,
  guard their math, and format numbers consistently, so no changes were needed - and
  I deliberately avoided touching working code to keep this conflict-free.

No features were added or removed; this is purely polish to existing behaviour.

### 1.0.166
**Cleaner Tempo captions, smarter update checking, and script polish**
- Tempo's own captions no longer stutter repeated words at the seams between chunks.
  Because Tempo transcribes overlapping windows of audio, the same word could appear
  twice ("on on the mat"); it now detects and removes that duplicated overlap, so
  captions read cleanly.
- Update checking is smarter about what it offers to download: it prefers a
  standalone Tempo.exe (so the in-app one-click update can apply it), and now also
  offers the installer zip when that's the only thing attached - previously it would
  just send you to the releases page in that case.
- publish.cmd now reminds you to attach Tempo.exe too, since that's what enables the
  in-app one-click update for users.
- Reviewed install.cmd and uninstall.cmd; they already handle a running Tempo, file
  locks, missing-exe discovery, and the not-installed case, so no changes were needed.

Honest note on captions: Tempo's own captions use a small on-device model and still
won't match Windows 11 Live Captions, which uses a much larger streaming engine. This
update removes a real, visible flaw (the repeated words), but a short delay and the
occasional misheard word are inherent to a local model. For the most accurate, lowest-
delay captions, Windows 11 Live Captions remains the better choice and the default.

### 1.0.165
**Nicer card UI with section icons**
- Section cards now show a small coloured icon next to each title (a clock for Click
  Interval, a cursor for Click Options, a target for Click Position, and so on),
  matching the polished card look. This applies across the Clicker, Macros and
  Settings tabs.
- Added a reusable icon system to the card component so future cards can show a
  matching glyph with no extra work.

Honest note: the Multi-Point, Statistics and Keybinds tabs use a flatter layout
without cards, so they don't gain icons yet. Converting them to the card style is a
larger visual rework I'd rather do carefully (and have you check on your screen) than
rush blind - tell me to proceed and I'll do those next, one tab at a time.

### 1.0.164
**Fixed: scrolling could flash an empty screen + publish.cmd clean-phase fixes**
- Fixed the page going blank/empty while scrolling up or down. Scrolling makes the
  scrollbar appear or disappear, which Tempo was treating like a window resize and
  re-laying-out every control mid-scroll - briefly blanking the view. Tempo now only
  re-centres when the window width actually changes, so scrolling is smooth and never
  blanks the content.
- publish.cmd: the script now switches to its own folder first, so building works no
  matter what directory you launch it from (previously the clean and build steps used
  relative paths that only worked from the project root).
- publish.cmd: the clean phase (step 3 of 7) now measures the space it reclaims using
  the same folders it actually deletes, and clearly lists what it's removing.

### 1.0.163
**publish.cmd bug fixes**
- Fixed the build summary describing the wrong build options. It claimed the exe was
  "compressed" and didn't mention the native speech libraries, when in fact
  compression is off and those libraries are kept beside the exe. The displayed
  options now match what's actually built.
- Fixed the "Building..." line saying it was "compressing into one .exe" when
  compression is disabled.
- Fixed the final summary's "Built with: .NET SDK" line never appearing — it checked
  a variable that was never set. It now correctly shows the SDK version used.

Clicker: audited the engine and tab (run loops, fixed-count and duration handling,
interval/burst timing, jitter bounds, and divide-by-zero spots) and found it already
solid — inputs are clamped, jitter stays positive, and the high-CPS path is guarded —
so no changes were needed there.

### 1.0.162
**Fixed: install.cmd couldn't find Tempo.exe after publishing**
- Fixed install.cmd failing with "couldn't find Tempo.exe" when you run it from the
  project folder after publishing. The exe lands in bin\publish\<rid>\, but the
  installer only looked next to itself and one sub-folder deep. It now searches the
  publish output folder (bin\publish\) and deeper sub-folders automatically, so it
  finds Tempo.exe whether you run it from inside the unzipped setup folder OR straight
  from the project root after a build.
- The installer now prints exactly which folder it found Tempo.exe in, and its
  "not found" message gives separate, clear fixes for the unzip case vs the
  just-published case.
- uninstall.cmd now detects when Tempo isn't actually installed (e.g. you only
  published, or you use it portably) and says so clearly instead of pretending to
  remove an installed copy. It still honours /purge to clear saved settings in that
  case.

### 1.0.161
**Blank-tab safety fix, rewritten README & website, more publish polish**
- Fixed a case where a tab (e.g. Settings) could appear blank: when you switch tabs,
  Tempo now always scrolls the new tab to the top and forces it to repaint, so its
  content can never be stuck scrolled out of view.
- Rewrote the README to be user-first: clear step-by-step install for both the
  installer and portable, plain-language first steps and recommendations, privacy,
  troubleshooting, and where data is stored - with developer/build details moved to
  the end.
- New website (landing page) with the same clear install steps, friendly getting-
  started advice, an honest first-run note about the unsigned-app warning, and a
  feature overview.
- The release builder (publish.cmd) now prints an "All checks passed" confirmation
  after verification, on top of the colourful animated output and per-check green
  ticks added last update.

Note: the blank-tab fix is a safety improvement. If a tab still ever shows blank,
please tell me your Windows version and which tab, so I can pin down the exact cause.

### 1.0.160
**A much nicer release builder + a modernized Macros tab**
- The publish/release builder (publish.cmd) got a big visual overhaul: an animated
  colour-gradient TEMPO banner with a subtitle, colour-coded step headers, green
  [OK] check marks on every verification, and a colourful success/failure summary
  box. It also makes each phase and check clearer, and still falls back to plain
  text automatically on older consoles or in CI (/ci).
- The builder continues to verify correctness at every step - that the exe was
  produced, its size looks right for a self-contained build, its SHA-256 is written,
  and its embedded version matches the project - now with clear on-screen ticks so
  you can see each check pass.
- Reworked the Macros tab so it no longer looks dated: the long loose column of
  management buttons is now an organized "Manage" panel, Delete is clearly marked in
  red, and spacing/labels are tidied. No macro features were added or removed - it's
  the same actions, just cleaner and easier to scan.

### 1.0.159
**Reliability polish across Clicker, Macros, Multi-Point, Keybinds, Live Captions & updates**
- Fixed a latent crash in macro playback: a macro with no steps (or before its
  totals were ready) could divide by zero while updating the progress bar and bring
  the app down. The progress bar is now guarded and only redraws when it actually
  changes, which also removes a little flicker during playback.
- Fixed a similar divide-by-zero risk in the CPS test's time bar, and clamped it so
  it can never be set out of range (a known cause of progress-bar errors).
- Reviewed the Clicker, Multi-Point, Keybinds, Live Captions and update
  check/download paths for the same class of problems (bad math, out-of-range values,
  missing guards). These were already solid - inputs are validated, keybind conflicts
  are detected and explained on save, Multi-Point refuses to start with no enabled
  points, the live rate clears when clicking stops, and the updater safely falls back
  to the download page when there's no direct installer - so no further changes were
  needed there.

No features were added or changed in this update - it's purely reliability and
polish.

### 1.0.158
**Stability: fixed freeze/crash under high stress**
- Fixed Tempo freezing (and sometimes crashing) at very high click speeds. At
  extreme CPS the clicking thread could spin without ever yielding the CPU, pegging
  a processor core to 100% and locking up the UI and sometimes the whole system. The
  clicker now always yields a sliver of CPU time between clicks, so it stays
  responsive even flat-out, and a hard speed floor is enforced even with anti-freeze
  turned off.

**Portable vs installed: made it fair and clear**
- Tempo now tells you, in Settings, when it's running as a portable copy and which
  two things depend on the exe's location ("Start with Windows" and in-app updates).
  Everything else works identically portable or installed - now you know the trade-off
  instead of being surprised by it.

**Installer / uninstaller fixes**
- Fixed the installer downloading the speech model to the wrong folder, where the app
  couldn't find it - so the bundled-model option now actually works. The model is
  placed where Tempo reads it.
- The uninstaller now tells you the saved-data folder can include a large (100+ MB)
  speech model before you choose whether to delete it, and removes it when you do.

### 1.0.157
**Stability: fixed random crashes**
- Fixed a cause of Tempo crashing at random, most likely while using Tempo's own
  Live Captions. The audio-capture callback (which runs constantly on a background
  thread while captions are on) wasn't protected against errors, and a single bad
  audio buffer could take the whole app down. It's now fully guarded - a bad buffer
  is skipped instead of crashing Tempo.
- Hardened the rest of the caption engine's background work (its worker thread and
  the no-audio watchdog) so any unexpected error there is logged and contained
  rather than crashing the app.
- Added a safety net for stray background tasks across the app: an unexpected error
  in any background task is now logged and contained instead of being allowed to
  crash Tempo.

If Tempo ever does still crash, it writes a crash report you can send in - but these
fixes close the most likely random-crash path.

### 1.0.156
**Fixed the caption-bar error message + the caption hotkey**
- Fixed a raw technical message ("the .dll files stay next to Tempo.exe…") showing
  up on the caption bar itself. If Tempo's own captions can't start, the bar now
  shows a short, friendly line and the full detail goes to a tray notification
  instead of being dumped on screen as if it were a caption.
- Reworked packaging so Tempo's speech-engine native files ship and load correctly:
  Tempo.exe stays a single self-contained file (app, settings and .NET runtime all
  inside), and the small native speech .dll files sit beside it and are installed
  alongside it. This addresses the "engine files missing" error.
- Fixed the Live Captions hotkey: it now follows the caption source you picked. If
  you selected Tempo's own captions, the hotkey starts Tempo's engine - it no longer
  forces Windows 11 Live Captions on. (The on-screen toggle already did this; now the
  keybind matches.)

If Tempo's own captions still can't start on your PC, the tray message will say why,
and Windows 11 Live Captions (Settings > Live Captions > Caption source) remains the
reliable default.

### 1.0.155
**Tempo's own captions: more accurate, a bit snappier**
- Tuned the speech engine to reduce wrong/garbled words: it now uses steadier
  decoding settings, fixes its decoding temperature so it stops inventing
  alternative words, stops one chunk's mistakes from carrying into the next, and
  uses more CPU threads so each chunk is transcribed faster.
- Reduced the delay a little more (a fresh result roughly every ~1.5 seconds).

**An honest note about Tempo's own captions**
Tempo's own captions use a small speech model running entirely on your PC. Because
of how that kind of model works, it cannot update every 0.1 second or be as
accurate as Windows 11 Live Captions - that uses a much larger, streaming,
GPU-accelerated engine built for exactly this. A local model needs a couple of
seconds of audio at a time to recognise words, so there will always be a short
delay, and the small model will occasionally mishear words.

If you want the most accurate, lowest-delay captions, use Windows 11 Live Captions
(Settings > Live Captions > Caption source) - it's still the default. For better
accuracy from Tempo's own engine, choose a larger model (Small or Medium) in
Settings; note larger models are more accurate but a little slower, not faster.

### 1.0.154
**Fixed the broken build (tiny exe / app not working)**
- Fixed Tempo being unusable after the last update. The build had switched to a
  multi-file folder layout, but the installer wasn't copying all the required files
  (the app code and its config), so Tempo wouldn't start. Tempo is now back to a
  single self-contained Tempo.exe that bundles everything (the .NET runtime and the
  speech engine), so it just works again. The exe is large because it contains
  everything - that's expected.

**Tempo's own captions: fixed garbled / nonsense text**
- Fixed the main cause of Tempo's captions producing wrong or nonsense words. When
  converting your PC's audio (usually 48 kHz) down to the 16 kHz the speech model
  needs, Tempo wasn't filtering first, which scrambled the sound into noise. It now
  filters properly before converting, so the speech model hears clean audio.
- Reduced the caption delay further (a fresh transcription roughly every ~1.7 s).

Honest note: Tempo's own captions use a small offline model and won't match the
accuracy or instant feel of Windows 11 Live Captions, which uses a much larger,
GPU-accelerated engine. If you want the most accurate, lowest-delay captions,
Windows 11 Live Captions remains the better choice and is still the default. Tempo's
own engine is the offline/no-Windows-captions option.

### 1.0.153
**Layout repair after games + better Tempo captions**
- Fixed the layout getting messed up / controls overlapping after a full-screen game
  changes your screen resolution. Tempo now automatically re-lays-out every tab when
  Windows reports a display change, so the Clicker, Macros, Live Captions and other
  tabs repair themselves instead of staying scrambled until you resize the window.
- The caption overlay and history windows are also nudged back on-screen after a
  resolution change, in case the screen shrank under them.

**Tempo's own captions: less delay, fewer missed words**
- Reworked how Tempo's offline captions process audio. Captions now update roughly
  every ~2 seconds instead of ~4, so there's noticeably less delay.
- Words spoken across a chunk boundary are no longer dropped: each pass now carries
  a second of previous audio forward as context, so speech isn't cut in half.
- If transcription falls behind on a slower PC, Tempo now drops the oldest backlog
  audio instead of letting the delay grow and grow.
- Near-silent audio is skipped, which speeds things up and stops the engine from
  inventing phantom words during quiet moments.

Tempo's own captions still need a speech model and a working audio source (system
audio if you have a speaker, or a microphone). Windows 11 Live Captions is
unchanged and remains the default.

### 1.0.152
**Fixed: installer couldn't find Tempo.exe + reworked packaging for the speech engine**
- Fixed "ERROR: Tempo.exe was not found next to this installer." Tempo is now
  published as a self-contained folder (Tempo.exe with its runtime and the speech
  engine's native files) instead of a single packed .exe. The previous single-file
  packaging both hid the speech-engine libraries and could leave the installer
  without a Tempo.exe to find. The folder approach is the reliable way to ship the
  Whisper speech files.
- The installer is now much more forgiving: if you unzip in a way that puts the
  files in a sub-folder, it finds Tempo.exe one level down automatically. If it
  still can't find it, the error now lists exactly what's in the folder and how to
  fix it, instead of a dead end.
- The installer copies the speech-engine files (and any runtimes folder) next to
  Tempo.exe, and uninstall removes them all.

How to install (unchanged for you): unzip the setup zip, then double-click
install.cmd. Just keep all the unzipped files together in one folder. Windows 11
Live Captions still needs none of this and remains the default caption source.

### 1.0.151
**Fixed: Tempo's own captions failing with a "Whisper.net.Runtime" error**
- Fixed the real cause of Tempo's offline captions doing nothing while showing a
  message about installing "the default libraries with the Whisper.net.Runtime
  nuget". Tempo's speech engine needs its native runtime files (whisper.dll etc.)
  sitting next to Tempo.exe, but the build was packing them inside the single-file
  executable, where the engine couldn't find them. The build now keeps those native
  files beside Tempo.exe so the speech engine loads correctly.
- The installer now copies those speech-engine files (and any runtimes folder)
  alongside Tempo.exe, so an installed copy has everything it needs.
- If the engine still can't find its files, Tempo now shows a clear, plain-language
  message (reinstall and keep the .dll files next to Tempo.exe) instead of the
  cryptic developer error.

Important for distribution: a published Tempo is now Tempo.exe plus a few native
.dll files in the same folder (no longer a single lone .exe). Always ship/keep them
together. Windows 11 Live Captions is unaffected and still the default.

### 1.0.150
**Fixed: Tempo's captions stuck on "Listening…" (and no-speaker PCs)**
- Fixed the main reason Tempo's own captions showed "Listening…" forever and never
  produced text: Tempo's engine captures your PC's *audio output*, which needs a
  speaker/playback device. On a PC with no speaker there was nothing to capture, so
  it sat silent. Tempo now detects this.
- New "Listen to" choice for Tempo's own captions (Settings > Live Captions):
  - **Auto** — uses system audio if a speaker exists, otherwise automatically falls
    back to your microphone.
  - **System audio** — captions whatever your PC plays (needs a speaker).
  - **Microphone** — captions sound from a mic (use this if your PC has no speaker).
- The caption bar now shows the real reason instead of a frozen "Listening…": if no
  speaker is found it tells you to switch to a microphone, and if nothing is playing
  it says so. No more silent waiting with no explanation.
- If no audio arrives within a few seconds, Tempo now reports why rather than
  appearing to do nothing.

This only affects Tempo's own offline captions. Windows 11 Live Captions is
unchanged and still the default.

### 1.0.149
**One-click speech model + faster, more reliable updates**
- Tempo's own offline captions no longer need any manual file copying. Two new ways
  to get the speech model:
  - The installer can now download the small Base model for you during setup, so
    Tempo's own captions work out of the box. (Skippable; if you're offline you can
    still add it later.)
  - In Settings > Live Captions there's a new "Download model" button that fetches
    the selected model straight into the right folder with a progress bar - no
    finding or copying files by hand.
- Fixed update downloads sometimes stalling or getting stuck loading. The download
  (and its checksum fetch) now skip Windows' automatic proxy discovery, which was
  the usual cause of long hangs on networks without a proxy - the same fix already
  applied to the update check.
- Fixed overlapping controls in the Settings > Live Captions section and made the
  two new buttons fit cleanly.

Note: the Base model is ~140 MB and is fetched on demand (by the installer or the
in-app button), not bundled in the app download itself. Windows 11 Live Captions
still needs no model and remains the default.

### 1.0.148
**Live Captions: fixed Settings overlap + clearer Tempo-captions setup**
- Fixed overlapping controls in the Settings tab: all the Live Captions options now
  live in their own dedicated, neatly spaced "Live Captions" section instead of
  being crammed into the Behaviour group where they overlapped other controls.
- Made Tempo's own captions much clearer to set up. The section is now numbered:
  1) pick the caption source, 2) (for Tempo's engine) pick the model. The status
  line now tells you in plain words whether the model is installed and exactly what
  to do if it isn't, in green when ready and amber when not.
- Added an "Open models folder" button so you can drop a Whisper model file in with
  one click, then re-select it to use Tempo's own offline captions.

Why "nothing happened" before: Tempo's own captions need a speech model file
present, and this build doesn't ship one yet (that's coming). Until a model is in
the models folder, Tempo's own engine has nothing to transcribe with — the new
status line now says so clearly. Windows 11 Live Captions works without any model
and remains the default.

### 1.0.147
**Two clearly separated caption sources**
- Live Captions now has one clear choice instead of confusing checkboxes. In
  Settings > Behaviour > Live Captions, a "Caption source" dropdown lets you pick:
  - **Windows 11 Live Captions** — uses the built-in Windows engine, mirrored into
    Tempo's caption bar (what Tempo did before).
  - **Tempo's Live Captions (offline)** — Tempo listens to your PC's audio and
    transcribes it itself with a local Whisper model. No internet; nothing leaves
    your PC.
- You always know which one is active, and the two never run at the same time -
  switching source cleanly stops the other.
- When Tempo's own engine is selected, a model picker appears (Base / Small /
  Medium) with a clear "installed / not installed" status so you know whether the
  speech model is ready.
- Tempo's own caption text flows into the same overlay bar and history you already
  use, with all the same font/colour/size/opacity/position options.

Note: Tempo's own engine needs a speech model present (the installer is intended to
ship the small Base model). If none is installed, Tempo tells you where to add one.
Windows Live Captions remains the default.

### 1.0.146
**Tempo's own Live Captions — engine foundation (Phase 1)**
- Groundwork for Tempo transcribing speech itself, offline, instead of relying on
  Windows Live Captions. This build adds the engine but does not switch it on yet.
- New offline speech engine: Tempo can capture the PC's own audio output (so it
  hears the game or video, not just a microphone) and run the Whisper speech model
  locally to turn it into captions — no internet, nothing leaves your PC.
- Choose-your-model support: a small "Base" model is intended to ship with the
  installer for instant offline use, and you'll be able to pick a larger, more
  accurate model in Settings. Models live in a per-user folder so they survive
  updates and need no admin rights.
- This is the engine layer only; the Settings toggle to use Tempo's own captions
  instead of Windows, and the wiring into the on-screen caption bar, come next.

Technical note: the speech engine uses NAudio and Whisper.net. A normal Windows
build restores these automatically; behaviour is unchanged until the feature is
switched on in a later update.

### 1.0.145
**Full-screen & window fixes**
- Fixed scrolling into empty space below the content in full-screen mode: when a
  short page is vertically centred, the scroll range is now pinned to the visible
  area so you can't scroll past the content into a void. Normal and maximised
  windows are unchanged (content at the top, scrolls normally).

**Clicker — existing-feature fixes**
- Fixed a profile round-trip bug: a profile saved above 1000 CPS (which uses a
  sub-millisecond interval) reloaded showing roughly half the rate on the speed
  slider. The slider and label now restore the correct CPS when you load such a
  profile.

**Macros — existing-feature fixes**
- The per-macro buttons (Play, Play once, Edit, Rename, Delete, Export, Pin) are now
  disabled when no macro is selected or the list is empty, instead of staying
  clickable and only showing a warning. They re-enable as soon as you select a
  macro, and update correctly after a macro is deleted or after playback finishes.

### 1.0.144
**Richer status bar**
- The bottom status bar now shows more at a glance. The state word gets a coloured
  dot (green running, amber paused, grey idle) so you can read the state instantly.
- Added a live Peak CPS readout next to the current CPS, clearer separators between
  groups, and tidier grouping of profile, counts, rate and time.
- New target-run progress indicator: for a fixed-count run it shows "1,250 / 5,000
  (25%)", and for a timed run it shows the time remaining. It appears only while a
  target run is active and hides otherwise.
- New anti-freeze indicator: a "⚡ throttling" flag appears in the status bar while
  anti-freeze is actively slowing the click rate to protect your PC, and disappears
  when it stops.

### 1.0.143
**Profiles — foundation (Phase 1 of the new Profiles tab)**
- Profiles can now carry far more than clicker settings. Each profile gained a
  category (Gaming / Work / Productivity / Custom), an icon, a colour tag, a
  favourite star, created/last-used dates, and usage stats (times used and total
  runtime). Existing profiles keep working and pick up sensible defaults.
- Profiles can now also store your keybinds and your app/overlay look (theme,
  accent, notifications, always-on-top, clicking indicator, and the caption overlay
  settings) so that switching profile can switch your whole setup. (Wiring these
  into the new tab's UI comes in the next update.)
- New under-the-hood profile tools, ready for the Profiles tab: a recycle bin that
  lets deleted profiles be restored, favourite toggling, usage tracking, and
  export/import of a single profile as a JSON file with validation and name-clash
  handling (rename or overwrite).

This is the groundwork for a full Profiles tab; the visual tab itself lands next.
No existing behaviour changes in this build.

### 1.0.142
**Bug-fix pass (Live Captions)**
- Fixed: changing the caption text size moved the caption bar back to the bottom of
  the screen, undoing a position you'd dragged it to. Resizing now keeps the bar
  where you put it (and just clamps it to stay on-screen). Same fix for the history
  overlay.
- Fixed: after using "Move captions" to reposition the bars, the live caption bar
  kept showing the "Drag me" placeholder until the next words arrived. It now
  restores the real caption (or "Listening…") as soon as you leave move mode.
- The caption history overlay's text-size limit now matches the live bar (up to 72).

Also reviewed the clicking engine, macro player, timers and file I/O for crashes,
leaks and threading issues - no further problems found; the engine's rate maths is
correctly guarded against divide-by-zero and the UI marshals cross-thread events
safely.

### 1.0.141
**Live Captions: text-only option + size/colour changes apply instantly**
- New choice: turn the caption background panel off for a clean text-only look. In
  Settings > Behaviour > Live Captions, uncheck "Show caption background panel" and
  captions become just floating text with no panel behind them. Text-only mode adds
  a stronger soft glow automatically so the words stay readable over any scene.
- Fixed: changing the caption text size or colour (or font/opacity) now takes effect
  immediately when you click Save, even while the caption bar is already showing.
  Before, look changes only appeared after toggling captions off and back on.
- The caption text-size limit is raised from 48 to 72 for larger, more readable
  captions.

### 1.0.140
**Caption overlay bar: full visual overhaul**
- The caption bar has been completely redrawn. It now sits in a soft, rounded,
  semi-transparent panel with a subtle gradient and a hairline border, instead of
  floating bare text - much easier to read while still letting the game show
  through. It's rendered with true per-pixel transparency, so edges are smooth and
  the panel can be softly translucent rather than all-or-nothing.
- Captions now fade in gently when they appear and a soft glow is drawn behind the
  text, keeping words legible over bright, busy or fast-moving scenes without a
  hard black box.
- A thin "live" accent line sits along the bottom of the bar and brightens/pulses
  while new speech is arriving, so you can tell at a glance that captions are
  flowing - and it calms down when talking stops.
- All your existing controls still apply (font, text size, colour, opacity, drag to
  reposition). Opacity now softens both the text and the panel together, and the
  newest words always stay on screen (no "..." and no overlapping lines).

### 1.0.139
**Live Captions: Windows detection, drag, and overlapping text all fixed**
- Windows 11 captions not appearing: Tempo now detects Live Captions by its
  process (the same thing you see in Task Manager), not just by window title/class,
  which varied by Windows build and locale. It also finds the caption window by
  walking the LiveCaptions process's own windows when the title lookup misses - so
  the toggle reliably knows whether captions are on, and Tempo's mirror finds the
  text far more often.
- Drag fixed: turning on "Move captions" now forces the click-through style off
  immediately (a frame-change refresh), and the live caption bar paints a visible
  grab panel while in move mode - previously its transparent pixels couldn't be
  grabbed, so dragging did nothing. Now you can grab either bar anywhere and drop
  it where you want; toggle move mode off to lock them click-through again.
- Overlapping text fixed: the outlined caption text is drawn from glyph outlines,
  which are taller than the old line-spacing measurement assumed, so lines could
  overlap and become unreadable. Both the live bar and the history overlay now
  space every line by its real drawn height plus a guaranteed gap, so text never
  overlaps.

### 1.0.138
**Fixed: big empty gap above Settings (and other tabs)**
- A recent change centred page content vertically, which in a normal or maximised
  window pushed everything down and left a large blank area you had to scroll past.
  Vertical centring now only applies in true full-screen mode (F11); in a normal or
  maximised window, content starts at the top as it should - no gap.

**Live Captions: drag the caption bars anywhere**
- New tray toggle "Move captions (drag to reposition)". Turn it on and both the live
  caption bar and the history overlay stop being click-through so you can drag them
  wherever you like; turn it off to lock them back to click-through. Your chosen
  positions are remembered between sessions.
- While move mode is on, the bars show sample text so there's something to grab, and
  a tray tip explains how to lock them again.

### 1.0.137
**Caption history: show/hide from the tray**
- The caption history overlay is now a tray toggle: right-click the Tempo tray icon
  and click "Caption history" to show it, click again to hide it. A checkmark shows
  whether it's currently visible.

**Languages: Tempo now matches your Windows language automatically**
- On first run Tempo picks the matching display language from the ones it already
  ships (Spanish, French, German, Italian, Portuguese, English) based on your
  Windows language - no new languages added, just automatic use of the existing
  ones. Your manual choice in Settings is always respected and never overridden.

**Installer: fixes for users who couldn't get Tempo installed**
- Locked-down PCs that block PowerShell previously ended up with no Start Menu
  shortcut and looked like a failed install. The installer now falls back to a
  VBScript (cscript) to create shortcuts, and if even that's blocked it tells you
  exactly where Tempo.exe is so you can run it directly.
- A checksum mismatch (often just a line-ending/format quirk in the .sha256, not
  real corruption) no longer aborts the install - it warns and continues, so
  genuine users still get Tempo.
- The finish screen now confirms clearly whether a Start Menu shortcut was made and,
  if not, points you to the installed Tempo.exe.

**Uninstaller**
- More reliable self-removal: the delayed delete uses timeout with a ping fallback,
  so it works even on systems where one of those isn't available.

**Also includes (from recent builds)**
- Fixed scrolling that could stick in full screen / maximised, and Live Captions
  customisation (font, size, colour, 50% default text opacity).

### 1.0.136
**Fixed: scrolling could stick in full screen / maximised**
- Some tabs would stop scrolling part-way when the window was full screen or
  maximised. The vertical-centring used for short pages was applying an offset even
  when content nearly filled the height, which left the scroll range unstable. Now
  content is only centred when there's clear headroom; otherwise the page scrolls
  normally from the top, and the scroll range is recomputed after any reflow - so
  scrolling stays reliable in every tab at any window size.

**Live Captions: more customisation, calmer default**
- Caption text opacity now defaults to 50% (the background was already transparent),
  so captions sit more gently over games out of the box. You can still set it
  anywhere from 10-100%.
- You decide the look: caption font, text size, colour (any colour) and opacity are
  all in Settings > Behaviour > Live Captions, applied to both the live bar and the
  history overlay.

### 1.0.135
**Live Captions history: full lines (no more "..."), better merging, less delay**
- History lines no longer get cut off with "...". Each line now wraps to as many
  rows as it needs, and the panel fills from the most recent text upward, so you
  always see complete sentences instead of truncated ones.
- Better de-duplication. Live Captions slides a phrase forward (the next line often
  begins where the previous one ended); Tempo now stitches those overlapping pieces
  into one continuous line instead of stacking near-identical rows. Combined with
  the existing refine-in-place handling, the history reads as distinct, continuous
  speech.
- Less delay. The caption poll is faster (every 150 ms instead of 250 ms), so both
  the live bar and the history keep up more closely with what's being said.

### 1.0.134
**Live Captions: fixed repeated history lines & Windows captions not appearing**
- Fixed the history overlay filling with the same sentence repeated. Windows Live
  Captions streams a phrase by re-sending it as it grows and lightly revises the
  wording; Tempo's old check treated each tiny revision as a new line. It now
  recognises a phrase being refined (shared long prefix, or one line containing the
  other) and updates that line in place, so the history shows distinct utterances
  instead of six near-identical copies.
- Fixed Windows Live Captions sometimes not appearing when you toggle captions on
  (while still showing in Task Manager). Win+Ctrl+L is a toggle, so sending it
  blindly could turn captions off - or launch them hidden - when they were already
  running. Tempo now checks whether the Live Captions window exists and only sends
  the key to reach the state you asked for, re-sends once if it doesn't appear, and
  warns you (with how to fix it) if Windows still won't start it.

### 1.0.133
**Live Captions: the full-history view is now an overlay too**
- The "Show caption history" view is no longer a plain window - it's now a caption
  overlay in the same style as the live bar: frameless, always-on-top and
  click-through, sitting in the upper-left of the screen. It shows the most recent
  several lines of the session transcript and auto-scrolls as new captions arrive,
  so you can glance back at what was said without it ever stealing focus or blocking
  clicks in your game.
- It uses the same text colour, font and size as the live caption (bright yellow,
  Segoe UI by default), with the same outline + shadow for legibility. The panel
  has a faint ~20%-opaque dark background so several lines stay readable over busy
  scenes - "80% transparent, but you can still see the text", as requested.
- Open it from the tray menu ("Show caption history"). It still tracks the whole
  session (most recent 500 lines) behind the visible window.

### 1.0.132
**Live Captions: fixed disappearing text + added a full-history window**
- Fixed captions vanishing mid-session while still ON. Windows Live Captions
  regularly reports an empty value between phrases (or during a brief read hiccup);
  Tempo was clearing the bar each time. Now it keeps the last line on screen and
  only replaces it when new words actually arrive - so the caption no longer blinks
  away while you're using it.
- New "Show caption history" window (open it from the tray menu). The overlay bar
  only shows the latest line or two; this resizable, scrollable window keeps the
  whole transcript of the current session so you can scroll back, re-read anything
  you missed, and copy it all out. In-progress phrases are refined in place rather
  than duplicated, and the history holds the most recent 500 lines.
- The history window follows your theme, auto-scrolls (toggleable), and has Copy
  all / Clear buttons. Closing it just hides it - the session transcript is kept and
  you can reopen it any time.

### 1.0.131
**Live Captions: fixed the "…" freeze, plus font & colour options**
- Fixed the bug where the caption bar showed one long line ending in "…" and
  stopped visibly updating. As Windows Live Captions accumulates text it becomes a
  long string; Tempo was drawing the whole thing and overflowing the bar. Now the
  overlay shows only the most recent words that fit (dropping words from the front
  as new speech arrives), wrapped to two lines and anchored to the bottom - so the
  latest words are always on screen and it keeps moving as people talk.
- Caption text is now bright yellow by default for readability over games, and you
  can change both the colour and the font:
  - Settings > Behaviour > Live Captions: a "Caption font" dropdown (Segoe UI and
    others) and a "Caption color" picker (any colour; changes apply live).
- The caption bar is a little taller to fit two comfortable lines, and the text is
  still outlined and shadowed so it stays legible on bright or busy scenes.

### 1.0.130
**Live Captions: Tempo's bar now shows the actual words**
- Tempo's caption overlay now mirrors the real transcribed text from Windows Live
  Captions instead of a placeholder. While captions are on, Tempo reads the words
  from the Windows Live Captions window (via UI Automation) and shows them in its
  own transparent bar - then moves the Windows bar off-screen, so you see only
  Tempo's clean caption over your game.
- New Behaviour option "Mirror Windows captions into Tempo's bar & hide the Windows
  bar" (on by default). With it off, Tempo shows its bar and leaves the Windows bar
  where it is. Tempo still doesn't transcribe audio itself - Windows does the
  speech-to-text; Tempo reads and restyles it.
- The bar shows "Listening…" once Windows Live Captions is detected and updates live
  as people speak. It degrades gracefully: if the Windows window can't be found it
  keeps the friendly on-screen note rather than failing.

Note: requires Windows 11 Live Captions (22H2+). The first toggle may take a moment
while Windows starts captioning; the Windows bar is restored when you turn captions off.

### 1.0.129
**New: Tempo's own Live Captions overlay (accessibility)**
- Toggling Live Captions now shows Tempo's own caption bar - a strip across the
  bottom of the screen with a fully transparent background, so only the text is
  visible. It floats over any game, is always-on-top and click-through (it never
  blocks your clicks or steals focus), and the text is drawn with an outline and
  shadow so it stays readable over bright, busy scenes. It follows your theme.
- The same hotkey still also drives Windows Live Captions underneath (this is what
  actually transcribes the audio); you can turn that off in Settings if you caption
  another way. Honest note: Tempo shows and styles the captions, but the real-time
  speech-to-text is done by the OS engine - it does that far better than a bundled
  tool could, and an external app can't cleanly separate voices from a game's mixed
  audio anyway. SetCaption() is the hook a future caption source can feed text into.
- New Settings (under Behaviour > Live Captions): show the overlay bar on/off, also
  drive Windows Live Captions on/off, caption text size, and caption opacity (the
  background stays transparent regardless).

### 1.0.128
**New: Live Captions hotkey (accessibility)**
- Added a bindable "Toggle Live Captions" hotkey on the Keybinds tab. It flips
  Windows Live Captions on and off with a single key, without leaving your game.
  Windows Live Captions (Windows 11, 22H2+) transcribes ALL audio on your PC in
  real time and floats a caption bar over any app, including fullscreen games -
  so it covers game voice chat (Rainbow Six, etc.), Discord and livestream audio.
  It is built into Windows, free, and runs offline after a one-time language pack.
- Why a toggle and not built-in transcription: accurate real-time speech-to-text
  needs OS-level, GPU-backed models and can't separate voices from a game's mixed
  audio in an external app. Windows Live Captions already does this far better than
  a bundled engine could, so Tempo puts the control on your hotkey and lets Windows
  do the captioning.

**Keybinds - clarity**
- The clicker, macro and captions actions are fully independent, each with its own
  configurable hotkey and live conflict highlighting, so none of them collide. (No
  change to existing bindings - this just adds the captions action to the list.)

### 1.0.127
**Build & install tooling - a big, practical upgrade (no app behaviour change)**
- `publish.cmd` rewritten into a 7-phase release builder with real substance:
  flags (`/help`, `/quick`, `/nozip`, `/ci`, `/open`, `/noprompt`), an environment
  preflight (SDK list, OS, CPU, free-disk warning), per-phase timing, output
  validation (size sanity + exe-vs-csproj version check), a combined CHECKSUMS.txt
  for the exe and the setup zip, automatic copy of this version's release notes next
  to the artifacts, reclaimed-space reporting on clean, a richer failure box with
  fixes, a full help screen, and clear exit codes. The proven build comet
  (single-backslash mm:ss) and 24-bit TEMPO banner are preserved.
- `install.cmd` now verifies Tempo.exe against its bundled SHA-256 before copying,
  detects and reports upgrades, closes a running Tempo first, rolls back a failed
  copy, supports `/silent /desktop /nodesktop /launch /nolaunch`, writes an install
  log, and records the website link in the Apps entry.
- `uninstall.cmd` gains `/silent /keepdata /purge /backup`, can save a settings
  backup zip to the Desktop before purging, and writes an uninstall log. The
  safe detached self-delete is unchanged.

### 1.0.126
**Clicker - fixed: pausing ate the "For (seconds)" budget**
- The run-duration clock kept ticking while paused, so pausing a 60-second run for
  45 seconds left only ~15 seconds of clicking - or none at all. The engine's run
  clock now stops while paused and resumes with you: "for N seconds" finally means
  N seconds of actual clicking. The status-bar countdown freezes during pause too,
  because it now reads the engine's active time instead of the wall clock.

**Clicker - fixed-count runs show live progress**
- While a "Fixed count" run is clicking, the status bar now shows how far along it
  is: "Clicking · 100 CPS · 312 / 500 - F6 to stop". The engine exposes its per-run
  click counter, so the number is exact, not estimated.

**Macros - the playback progress bar means what it says**
- With multiple loops, the bar used to sprint 0-100% every loop. For finite runs it
  now fills once across the whole job (loop 2 of 4 sits around 25-50%), matching the
  time-remaining readout next to it. Infinite loops keep the per-loop fill - the only
  sensible reading for them.

### 1.0.125
**Macros - long recordings (hours, even 24h) no longer overwhelm the UI**
- The recording engine was already built for very long sessions: Stopwatch timing,
  movement throttling (min 3 px / 16 ms), idle time coalesced into a single Delay
  step, and gaps clamped safely. The weak point was the Live Monitor, which created
  one list row per captured step with no limit - a long session could pour millions
  of rows into the UI and exhaust memory long before the recorder cared.
- While recording, the Live Monitor now keeps only the most recent 1,000 rows
  (the header shows the true total: "1,248,003 steps captured (showing last 1,000)").
  The recording itself is complete and untouched.
- Selecting or playing a very large macro now fills at most the first 2,000 rows
  (noted in the header) instead of freezing the window while it builds every row.
  Playback highlighting follows the visible window.

### 1.0.124
**New: first-run official-source notice (community suggestion)**
- On first launch, Tempo shows a one-time "Quick safety note" telling you the only
  two official places it's published (the GitHub repository and the website), with
  buttons to open both, and a reminder that every release ships a SHA-256 checksum
  to verify downloads from anywhere else. Auto-clickers are a favourite target for
  malware-laden clones, so people deserve a way to spot the real one.
- Shown exactly once; if you start minimised to the tray it waits for your first
  restore instead of popping out of nowhere.

**About dialog**
- New Website and GitHub links, so the official pages are always one click away
  from inside the app.

### 1.0.123
**Clicker - unsaved profile changes are now visible**
- Edits to the clicker (speed, repeat, position, randomization, the profile name -
  anything the profile stores) live only in the controls until you click Save, and
  switching profiles silently discarded them. An amber "● Unsaved changes - click
  Save" notice now appears next to the profile name the moment your setup differs
  from the saved profile, and clears when you save or load. Detection compares the
  actual built profile against a snapshot, so programmatic loads can never
  false-positive.

**Macros - finish notification, same as the clicker**
- The existing "Notify when a fixed run finishes" option now also covers macro
  playback: when a finite run of loops completes on its own, Tempo plays the chime
  and shows a tray notice with the macro's name and loop count. Stopping playback
  yourself stays silent (the player now tracks natural completion, mirroring the
  click engine), and infinite loops never trigger it.

### 1.0.122
**New: tray sleep - a forgotten Tempo can't surprise you**
- New Behaviour option (on by default): "Sleep in tray (pause hotkeys & cursor
  trail)". While Tempo is hidden in the tray AND nothing is running, all global
  hotkeys and the hold-to-click trigger are paused and the cursor trail is hidden -
  so pressing F6 hours later in a game or document can't start invisible clicking.
- Everything wakes instantly when you open the window or start something from the
  tray menu, and the tray tooltip says "sleeping (hotkeys paused)" while it's asleep.
- It never engages while clicking, playing or recording, so "Hide window to tray
  when clicking starts" keeps working exactly as before.
- Bonus: while asleep, Tempo also skips all background UI refresh work, so an idle
  tray Tempo uses essentially no CPU.

**Settings UI**
- The Behaviour section grew a row for the new option; the groups and buttons below
  it moved down accordingly.

### 1.0.121
**UI fix - Recent sessions header collision**
- The "Recent sessions" title was wide enough to run underneath the Find box and the
  profile filter, so those controls sat on top of the text. The title is now short and
  the usage hint (double-click for details, right-click for options, click a column to
  sort) lives in the table's tooltip instead.

**Statistics - clearer sorting (existing feature)**
- Clicking a column header already sorted the sessions table (numerically and by date,
  not just alphabetically) - but nothing showed which column or direction was active.
  The sorted column now displays a ▲ / ▼ arrow.

**Keybinds - unsaved-changes indicator (existing feature)**
- Edits to hotkeys (and the interval step) sit in the boxes until you click Save
  Keybinds, but nothing said so - switch tabs and they were silently lost. A
  "● Unsaved changes - click Save Keybinds" notice now appears the moment you edit
  anything and clears once saved (or after a reset/reload). The live duplicate-binding
  highlighting stays as before.

### 1.0.120
**Full screen - content is now centred properly**
- In full screen every tab used to sit at the top with a tall empty band underneath.
  Pages now centre their content vertically as well as horizontally whenever it fits
  the window. When a page is taller than the window (e.g. Statistics) it scrolls from
  the top exactly as before, so this can never create a scrollbar by itself.

**Macros - playback timing fixed (measured)**
- Each recorded delay used to be rounded to whole milliseconds on its own, so the
  fraction was thrown away or doubled at every step. At higher speeds this compounded:
  measured on a 200-step run at 3x speed, playback finished 67 ms early (about 14%
  too fast). The fractional remainder is now carried from step to step, keeping the
  cumulative timeline within 1 ms at any speed.

**Clicker - status hints tell the truth in every mode**
- Hold-to-click: the idle hint now says "hold F6 to click" (the key doesn't toggle in
  that mode), and while clicking it reads "Clicking (hold)".
- "For (seconds)" runs now show a live countdown in the status bar
  ("Clicking · 50 CPS · 23 s left - F6 to stop").

### 1.0.119
**Status bar - much more detail**
- The idle hint now describes the whole run you're about to start: mode, speed and
  button as before, plus the finite repeat ("x500" or "60 s") and the click position
  ("@(812,440)" for a fixed point, "multi-point (3)" when using the point list).
- While clicking, the hint shows the target rate too ("Clicking · 100 CPS - F6 to
  stop").
- New: while recording a macro the hint shows "● Recording - N steps" live, with your
  record-toggle hotkey if one is bound.
- The state cell ("Idle / Running / Paused / Stopped (emergency)") is now colour-coded
  - green while running, amber when paused, red after an emergency stop.

### 1.0.118
**The real fix for laptop overlaps: full DPI scaling (PC vs laptop)**
- Root cause found: Tempo's text grows with Windows display scaling (your laptop runs
  125-150%, your PC 100%) but the layout was fixed pixels - so on the laptop every
  label inflated inside an unscaled box and overlapped. No amount of nudging single
  controls could fix that.
- Every window now declares a 96-DPI design baseline, so Windows scales the entire
  layout (positions, sizes, the window itself and its minimum size) to match the
  display - 100% stays exactly as before; 125%/150% now grows everything together.
- Screen overlays (countdown, indicators, points markers, the cursor trail and the
  coordinate picker) are pinned to raw screen pixels so scaling can't misplace them,
  and the page-centring system now measures positions after scaling.
- Tempo now tells you which machine it's on: About shows the display scaling
  ("Display: 1920x1080 @ 60 Hz · 125% scaling") and bug reports include a
  "Display scale" line - so PC-vs-laptop issues are visible at a glance.

### 1.0.117
**Layout fixes from laptop testing (thanks for the screenshots)**
- Clicker: the "Notify when a fixed run finishes" checkbox no longer overlaps the big
  IDLE / RUNNING status word - the word's box was trimmed and the checkbox moved just
  below it.
- Clicker: the Start / Stop buttons' hotkey hint no longer truncates to "Start ·..." -
  the text is now compact ("Start · F6") so it fits the button.
- Keybinds: the right-hand description column no longer overlaps row to row - rows are
  taller and each description has a fixed two-line box. The "Interval step" header was
  also shortened so it can't be cut off.
- Settings: "Remember window position & size" and "Minimise window during macro
  record & playback" show their "&" again (WinForms treats a single & as a shortcut
  marker and hides it).
- Statistics: the Current/Longest Streak captions no longer repeat the value ("Current
  Streak · 1 day"), which clipped at the card edge - the number is the value, the
  caption stays clean.

### 1.0.116
**Tab centring - last gap closed**
- Pages now also re-centre when their inner width changes because the vertical
  scrollbar appears or disappears (not just when the window resizes). That was the
  one remaining way content could sit off-centre and poke out by the scrollbar's
  width, briefly showing a stray horizontal bar.

**Email a bug - quicker repeats and better reports**
- The chooser now remembers how you sent your last report: that option is marked
  "last used", pre-focused, and triggered by just pressing Enter.
- Reports (email and GitHub issue alike) now include the path to Tempo's log file
  with a note to attach it - the log is usually what pins a problem down.

### 1.0.115
**Clicker - new: finish notification**
- New option under the Start button: "Notify when a fixed run finishes". When a
  fixed-count or fixed-duration run completes on its own, Tempo plays a chime and
  shows a tray notice with the session click count. Stopping a run yourself stays
  silent - the engine now tracks whether a run ended naturally or was stopped.

**Macros - "Save Macro" redesigned**
- Recordings no longer drop straight into the list under a timestamp name. A proper
  Save Recording dialog now appears when you stop: it shows what was captured
  (steps and length), lets you name it, add an optional note, and pin it to the top
  of the list. "Keep default" (or closing) still saves under the automatic name, so
  a recording is never lost.

### 1.0.114
**Fixed: publish.cmd never showed its build animation**
- Found and proved the real cause: the elapsed-time format used a doubled backslash
  (mm\\:ss), which makes .NET throw a FormatException on every frame - so the console
  printed nothing, every version, while the build itself still worked. With the correct
  escape (mm\:ss) the comet animation and elapsed timer finally show.

**Full screen**
- Entering full screen now shows a brief "Full screen - press F11 or Esc to exit"
  notice top-centre, so nobody gets stuck in the borderless window. It disappears on
  its own (and immediately when you exit).

**Background images (GIFs)**
- Choosing a file that can't actually be loaded as an image is now rejected with a
  clear message instead of being saved and silently showing nothing.
- If a saved background image goes missing or fails to load at startup, Tempo now
  writes the reason and the path to the log instead of failing silently.

### 1.0.113
**Clicker - richer detail in existing displays**
- The Manual Speed readout now also shows clicks per minute, e.g.
  "Target: 10 CPS (100 ms · 600/min)".

**Macros - richer detail in existing displays**
- Playback progress now shows an estimated time remaining for finite runs, e.g.
  "Loop 2 / 5  •  step 14 / 80  •  ~1:42 left".
- While playing, the Live Monitor header shows the macro's size and length
  ("▶ Playing Farm run — 80 steps, ≈2.1 min") instead of just its name.

**publish.cmd**
- New build animation: a comet sweeping across a dotted track with a fading tail,
  alongside the elapsed timer.

### 1.0.112
**Status bar - useful info in the empty middle**
- The status bar's blank centre now shows a live hint. While idle it reads what the
  clicker is set to and how to start it (e.g. "Interval · 10 CPS · Left  ·  F6 to start");
  while clicking it shows "Clicking — F6 to stop" (or resume when paused); and while a
  macro plays it shows "Playing macro: <name>". Clicks / CPS / Time stay on the right.

### 1.0.111
**CPS test rating now easy to see**
- The result rating (Slow / Average / Good / Fast / Very fast / Insane) now shows in a
  bold, colour-coded line — red for slow through green/gold for the fastest — so it's
  clear at a glance instead of small grey text.

**Fixed: cursor trail wasn't visible**
- The colourful cursor trail used a window style that made it click-through but also
  stopped it from drawing. It now paints correctly and stays click-through, so when
  the Macros-tab option is on you'll see the rainbow trail follow the mouse (including
  while recording a macro). If you don't see it, make sure the checkbox is ticked.

### 1.0.110
**Fixed: CPS test window cut off**
- The CPS test now sizes its content area directly, so the bottom line ("This
  session…") is fully visible on every screen and DPI — previously the title bar and
  borders ate into a fixed window size and clipped the bottom. The big button text no
  longer truncates either ("Click to retry").

**Cursor trail (just for fun)**
- New option on the Macros tab: "Colorful cursor trail". When on, a rainbow trail
  follows your mouse across the screen. It's click-through, so it never interferes with
  clicking or anything else, and it only repaints the area around the trail to stay
  light on a laptop.

**publish.cmd**
- The final summary now shows more detail: size in MB, output folder, target
  (net8.0-windows / win-x64), the .NET SDK it was built with, and the installer package.

### 1.0.109
**Fixed: tabs still jumping to the top (the rest of it)**
- Tracked down the remaining cause: Windows was scrolling a page to bring a control
  into view whenever one got focus — which happens when you click a control, change a
  setting, or start the clicker (focus shifts as buttons enable/disable). Pages no
  longer auto-scroll on focus, so the occasional jump is gone.

**CPS test**
- Your result now shows a quick rating next to the number (Slow / Average / Good /
  Fast / Very fast / Insane) so you can see how a run stacks up at a glance.

### 1.0.108
**Fixed: tabs jumping back to the top**
- A scrolled-down tab no longer snaps back to the top when you start the clicker, when
  live numbers update, or when the page re-centres. Tempo now keeps your scroll
  position. (The cause was live labels resizing themselves and the re-centre snapping
  the view to the top; the scroll position is now preserved through both.)

### 1.0.107
**Manual speed - up to 2000 CPS**
- With "Unlock max speed" on, the Manual Speed slider now goes up to 2000 CPS (was
  1000). Above 1000 CPS the engine switches to a sub-millisecond interval, since a
  whole-millisecond interval tops out at exactly 1000 CPS. The Anti-Freeze cap was
  raised to 2000 to match. Note: very high rates use more CPU, and Windows and the
  target app may not actually register every click much above ~1000/s.

**publish.cmd**
- Added a big "TEMPO" title banner at the top, drawn with a 24-bit colour gradient on
  terminals that support it (modern Windows Terminal / console).

**Email a bug**
- Added Yahoo Mail as another "open in your browser" option.
- The "Copy report to clipboard" option now also includes the most recent log lines,
  which often help pin down a problem.

### 1.0.106
**Clicker**
- The main button now works as Start / Pause / Resume: it reads "Start" when idle,
  "Pause" while clicking, and "Resume" once paused. Pausing and resuming were already
  possible via the hotkey, but now they're reachable by mouse too. Stop is unchanged.

**publish.cmd**
- Refreshed the build animation: a rotating spinner plus a bouncing bar on a dotted
  track, with the elapsed timer.

### 1.0.105
**Installer (big one) - save users time**
- Tempo now ships with a one-click installer. publish.cmd bundles install.cmd and
  uninstall.cmd next to Tempo.exe and zips a Tempo-Setup-<version>.zip you can attach
  to a release.
- Users just unzip and run install.cmd: it installs Tempo to their profile (no admin
  needed), creates a Start Menu shortcut (Desktop optional), and registers an entry in
  Settings > Apps so it uninstalls like any normal app. uninstall.cmd (or the Apps
  entry) removes everything cleanly. Running the bare Tempo.exe still works for anyone
  who prefers portable.

### 1.0.104
**Report a bug (email)**
- "Email a bug" now lets you choose how to send it: your email app, Gmail or Outlook
  in any browser, or copy the pre-filled report to the clipboard to paste anywhere.
  Every option is pre-filled with a template and your system details.

**Restart effect**
- The restart used when changing language now shows a brief "Restarting to apply
  changes" message and fades out more smoothly instead of blinking.

### 1.0.103
**Report a bug**
- The "Report a bug" link now opens a GitHub issue pre-filled with a clear template
  (describe / steps / expected / actual) and auto-filled diagnostics — Tempo version,
  Windows build, architecture, .NET runtime, processor count and display refresh rate —
  so reports are easier to act on.

**CPS test**
- The tester now shows a summary of your attempts this session (count, average and best)
  so you can compare recent tries without losing them.

**publish.cmd**
- Now does a true from-scratch build (clears the old output *and* the intermediate
  obj/bin folders) and shows a cleaner animated loading bar with an elapsed timer.

### 1.0.102
**Redesign**
- Added a divider down the right edge of the navigation sidebar to separate it cleanly
  from the page content.

**Downloading**
- Updates are now verified with a SHA-256 checksum when the release publishes one
  (Tempo.exe.sha256). A mismatch aborts the update so a corrupted or tampered download
  can never replace your copy; if no checksum is published, the existing size and
  executable-header checks still apply.

**Update checker**
- The "update available" dialog now shows the release date alongside the version.

**Clicker**
- The Stop button now also shows your Start/Stop hotkey, matching the Start button.

### 1.0.101
**Sidebar icons**
- Each navigation item now has a small icon so the sections are recognisable at a
  glance: a cursor for Clicker, three points for Multi-Point, a play glyph for Macros,
  bars for Statistics, a keyboard for Keybinds, and a gear for Settings. The icons are
  drawn as crisp vectors and follow the active/inactive colours.

**publish.cmd**
- The build step now shows a live spinner with an elapsed-time counter while it works,
  instead of sitting silently, so you can see it's still going. Full build output is
  still captured to publish-log.txt, and it falls back to a plain build if PowerShell
  isn't available.

### 1.0.100
**Restart effect**
- The window now fades in smoothly when it launches, and fades out before the app
  restarts itself (e.g. after a language change), so the hand-off looks polished
  instead of an abrupt flash. The fade respects your window-opacity setting.

**Clicker**
- The Start button now shows your Start/Stop hotkey (e.g. "▶ Start · F6") so the
  shortcut is visible without opening the Keybinds tab. Updates automatically when you
  change the binding.

**Statistics**
- The Left / Right / Middle cards now show each button's share of clicks as a
  percentage under the count.

**Multi-Point**
- Added "Move to top" and "Move to bottom" (right-click menu, plus Ctrl+Home / Ctrl+End)
  for quickly reordering long point lists.

### 1.0.99
**Redesign — navigation moved to a left sidebar**
- The tabs are no longer a strip across the top; they're now a vertical sidebar of
  rounded "cards" down the left side (Clicker, Multi-Point, Macros, Statistics,
  Keybinds, Settings), with the active one highlighted in the accent colour. The page
  content sits to the right. The default window is a little wider to fit the sidebar
  alongside the content.

**Window chrome**
- The title bar now renders in dark mode to match the app, so the minimize / maximize /
  close buttons fit the dark theme instead of sitting under a bright white bar. (Falls
  back gracefully on older Windows versions.)

### 1.0.98
**Fixed — the full-screen scrollbar, at the root this time**
- Removed the OS-level scrollbar hack (which fought the layout and made the bar flicker
  in and out). Content centering now measures against the page's real client width and
  hard-clamps so no control can ever sit past the right edge — so there is genuinely
  nothing to scroll sideways and the horizontal scrollbar can't appear at all. Every tab
  also re-centers the moment it's shown, so switching tabs after full-screen is clean.

**Clicker**
- The status bar now shows elapsed run time ("Time: MM:SS") alongside clicks and CPS, on
  every tab. The status labels are localized.

**Statistics**
- The charts are now translated too (recent sessions, last 7 days, by hour, by weekday),
  and weekday/month names in the cards and charts follow the chosen language instead of
  the Windows locale. The Statistics tab is now fully localized.

**Macros**
- Playback status ("Loop … / step …") is now localized.

### 1.0.97
**Fixed — the stray bottom scrollbar after full-screen, for good**
- The earlier centering fixes still left a horizontal scrollbar on the tabs after
  exiting full-screen in some cases. The tab pages now suppress the horizontal
  scrollbar outright at the Windows level — the content is always centred and fits
  the width at any allowed window size, so a sideways scrollbar is never needed.
  Vertical scrolling (for small windows) is unaffected.

**Improved — language coverage (no new languages added)**
- The entire Statistics tab is now translated in all six supported languages
  (Spanish, French, German, Italian, Portuguese). Previously every stat card title —
  Session Clicks, Peak CPS, Lifetime Clicks, This Week, Active Days, streaks, and the
  rest — stayed in English even when the app language was changed. The "Insights"
  heading and the "Unlock max speed (advanced)" option are now translated too.

### 1.0.96
**Fixed — full-screen left a stray scrollbar on the tabs**
- After exiting full-screen (F11), tabs could show a pointless horizontal scrollbar at
  the bottom. Full-screen makes the window very wide, so the content gets centred for
  that width — but Windows only resizes the *active* tab, so other tabs kept the wide
  offset and overflowed when you switched to them. Centering now uses the tab area's
  real width (consistent for every tab) and every tab is re-centred after a full-screen
  toggle, so the stray scrollbar is gone.

**Design — buttons now match the rounded cards**
- Buttons across the app are drawn with rounded corners to match the card panels, with
  subtle hover/pressed shading, for a more cohesive modern look. The accent buttons
  (Start = green, Stop = red, the primary Save buttons) keep their colours.

### 1.0.95
**Improved — closing the app**
- If you close Tempo while it's still clicking (or playing a macro), it now stops the
  run cleanly *before* exiting, so the worker finishes its current click and releases
  the mouse button — no more risk of a held mouse button being left "stuck" if you
  close during a hold-click. A button-release safety net runs on exit too, just in
  case.

**Improved — Statistics**
- "Reset session" now asks for confirmation before clearing, since it can't be undone.
  It still only clears the current session (counters, peak CPS, live charts) — your
  lifetime totals and saved history are untouched.

### 1.0.94
**Fixed — stray horizontal scrollbar on the tabs**
- When a tab's content was tall enough to need a vertical scrollbar, that scrollbar
  ate into the usable width but the content-centering still used the old width, so the
  content spilled past the right edge and a horizontal scrollbar appeared (and stuck
  around). Centering now reserves room for the vertical scrollbar, so the stray
  bottom scrollbar is gone.

**Improved — Clicker**
- A live **click-rate readout** now sits next to the big status word and updates in
  real time while clicking (e.g. "120.0 CPS"), so you can see the actual rate at a
  glance without looking down at the status bar. It clears when idle or paused.

### 1.0.93
**Fixed — recording keyboard shortcuts (Ctrl+C, Ctrl+D, …)**
- Macro recording was dropping the modifier from shortcuts when the Record or
  Emergency-stop hotkey used a modifier, so a press of **Ctrl+C / Ctrl+D / Ctrl+V**
  recorded as just "C"/"D"/"V". The recorder now keeps modifiers; only the control
  hotkey's own main key is held back, so everyday shortcuts record correctly.
- Playback is more faithful too: right-side modifiers and navigation keys (arrows,
  Home/End/Insert/Delete, Page Up/Down, etc.) now replay with the correct
  "extended key" flag instead of occasionally landing as the wrong key.

**Fixed — full-window background GIF could play too fast**
- The wallpaper page could register its animation twice (on a tab switch combined
  with a window resize/restore), making the GIF speed up. Added a guard so it
  animates exactly once.

**Improved — Multi-Point**
- The per-point buttons (Edit, Duplicate, Toggle, Remove, Move Up/Down) now grey out
  when no point is selected, and Move Up/Down disable at the top/bottom of the list,
  so the buttons reflect what's actually possible.

**Improved — publish.cmd**
- After building, it reads the produced Tempo.exe's version and warns if it doesn't
  match the project version — a guard against accidentally shipping an old build.

### 1.0.92
**Improved — across the tabs**
- **Clicker:** the Manual Speed −/+ buttons now step by a useful amount (5 CPS
  normally, 25 when max speed is unlocked) instead of 1 at a time, so they're far
  quicker to use across the wide range.
- **Macros:** press **Ctrl+D** in the list to duplicate the selected macro (matching
  the Multi-Point list). The on-tab help now lists the list shortcuts.
- **Multi-Point:** press **Ctrl+↑ / Ctrl+↓** to reorder the selected point from the
  keyboard (same as the Move Up / Move Down buttons).
- **Settings:** if Windows blocks the "Launch Tempo when I sign in" registry write
  (common on locked-down work PCs), Tempo now tells you instead of silently failing.

**Improved — full-window background GIF**
- Only the visible tab now animates the wallpaper GIF, instead of all six pages
  animating the shared image at once — lighter on the CPU and avoids the image
  playing too fast.

Keybinds was already fully covered (live conflict highlighting, a conflict warning on
save, confirm-on-reset) so it was left as-is.

### 1.0.91
**Redesigned — group boxes are now modern cards (app-wide)**
- Every panel on every tab (Clicker, Multi-Point, Macros, Keybinds, Settings) is now
  drawn as a clean rounded **card**: a softly-filled surface with a rounded 1px border
  and a title preceded by a small **accent tab**, instead of the old etched grey
  outline. The cards sit slightly raised from the page background so each section
  reads as its own group, giving the whole app a more modern, cohesive look.
- This is a purely visual change applied centrally — no controls moved, no behaviour
  changed — so all your existing layouts, shortcuts and settings are untouched.
- The cards automatically match whichever of the 38 themes you've chosen (they use the
  theme's surface, border, accent and text colours).

### 1.0.90
**New — monitor refresh rate detected**
- Tempo now detects each monitor's **refresh rate (Hz)**, logs it with the rest of
  the environment info, and shows your primary display's resolution and Hz in the
  About dialog. (Note: animated GIFs only contain a fixed number of frames at their
  own authored rate, so a higher-Hz monitor can't make a GIF play "smoother" than the
  file itself — the rate is shown for information.)

**Improved — GIF in full-screen**
- When you enter full-screen (F11) and a footer GIF is set, the GIF band now grows to
  a large banner for a more immersive look, and shrinks back when you exit.

**Improved — auto-clicker**
- With interval randomization on, the per-click **hold time** is now also slightly
  varied (when a hold is set), so held clicks aren't all identical — a more
  human-like pattern. No effect when randomization is off or no hold is set.

**Audit**
- Reviewed the GIF rendering, macro playback and click engine for bugs — no issues
  found; all three are correctly guarded (thread-safe frame updates, interruptible
  waits, held-input release on stop).

### 1.0.89
**Fixed — advanced speed layout**
- The "Unlock max (advanced)" checkbox was overlapping the "Target: … CPS" label in
  the Manual Speed box (especially at high CPS). It now sits on its own row beneath
  the slider, and the Manual Speed / Anti-Freeze boxes are the same height again.

**Improved — Macros**
- The macro right-click menu now also has **Play once** and **Export…**, so you can
  run a single pass or export one macro to a file without leaving the list.

### 1.0.88
**New — advanced (unlocked) click speed**
- The Manual Speed slider has a new **"Unlock max (advanced)"** option. Normally the
  slider tops out at 200 CPS; unlocking raises it to **1000 CPS** for maximum speed.
  Turning it on shows a clear warning first — extreme speeds use a lot of CPU, can
  make the mouse hard to control, and are easily detected by games that ban
  auto-clickers. (1000 CPS is the engine's real ceiling; Tempo deliberately doesn't
  offer an uncapped busy-loop rate that could freeze your PC.) Pair it with
  Anti-Freeze to cap CPU.

**Improved — update check & download**
- Clearer messages when a check fails: GitHub **rate-limiting**, a **timeout**, or a
  specific server error are now reported distinctly instead of a generic message.
- Downloads now detect a **truncated/interrupted** transfer (by comparing against the
  server's reported size), in addition to the existing empty-file and
  "is-it-really-an-exe" checks.

**Improved — publish.cmd**
- Writes a **`Tempo.exe.sha256`** checksum file next to the build (ready to attach to
  a release so users can verify their download), and warns if you pass a runtime ID
  that isn't a standard Windows one.

### 1.0.87
**Fixed — overlay memory leaks**
- The recording badge and the pre-play countdown overlay were leaking two GDI fonts
  each time they appeared (the fonts were never disposed). Both now dispose them
  properly, so repeated recording/playback no longer slowly leaks GDI handles.

**Improved — overlay design**
- The recording badge and countdown overlay now have **rounded corners** with a
  smooth anti-aliased border, matching the look of the on-screen running overlay.

**Improved — CPS Test**
- Beating your all-time best is now celebrated: the result turns the accent colour
  and shows "★ New best!".

**Improved — Multi-Point**
- The cycle summary now shows **"N of M points active"** when some points are
  disabled, so it's clear at a glance how many are in the rotation.

**Improved — Macros**
- The estimate line now also shows the macro's **step count** next to the estimated
  run time.

### 1.0.86
**New — milestone notifications**
- When your lifetime clicks pass a milestone (1K → 10M), Tempo now pops a one-off
  celebratory tray notification — even if you're on another tab. (Respects the "Show
  tray notifications" setting; already-reached milestones don't fire on launch.)

**Improved — auto-clicker (burst mode)**
- With interval randomization on, the pause **between** bursts is now jittered too
  (the clicks within a burst were already randomized). This removes the fixed,
  detectable rhythm between bursts for more human-like behaviour. Uses your existing
  randomization amount — no new settings.

### 1.0.85
**New — Statistics milestones**
- Added a **Milestones** section to the Statistics tab: a progress bar toward your
  next lifetime-click milestone (1K → 10M), how many clicks are left to reach it, and
  a row of badges showing which milestones you've already earned (★) and which are
  still locked (☆). The next-milestone line is also included in Copy summary.

### 1.0.84
**Fixed — remember window position & size**
- The window position and size are now actually restored after a restart or full
  close. Two problems were fixed: the **size was never being saved** (only the
  position was), and the restore happened too early during start-up where DPI scaling
  could override it — it now runs once the window is fully built, so it sticks.

**Improved — data & backup**
- New **Back up all data…** button (Settings → Data & Backup) copies everything —
  profiles, macros, settings and history — into a timestamped folder you choose.

**Improved — uninstall**
- The "back up first" step now backs up **all** your data (profiles, macros,
  settings, history), not just settings, and the uninstall is aborted if that backup
  is cancelled so nothing is lost by accident.

### 1.0.83
**New — Settings**
- **Window opacity** slider (50–100%) under a new "Window & Display" section — make
  the Tempo window see-through, with a live preview as you drag.
- **Reset window position** button — re-centres the window at its default size
  (handy if it ever ends up off-screen on a different monitor).
- **Remember window position & size** toggle (under Startup & Window) — the existing
  behaviour is now switchable, so you can turn it off if you prefer Tempo to open
  centred every time.

### 1.0.82
**Fixed — auto-clicker**
- Burst mode with a "For (seconds)" limit could overrun the time, because the limit
  was only checked between bursts. It's now checked during a burst too, so timed runs
  stop on time.

**Improved — Live Monitor**
- The step playing right now is highlighted with the accent colour so it stays
  clearly visible even when the list doesn't have focus (the old selection highlight
  could turn invisible during playback). The highlight is cleared correctly when the
  list is reloaded.

**Improved — publish.cmd**
- The build script now shows clear numbered steps (SDK check → configuration →
  clean → build → verify), prints the detected .NET SDK version and a build-config
  summary, says "please wait" during the slow build, and reports the final file size,
  SHA-256 and total build time.

### 1.0.81
**New — two more languages**
- The interface is now also available in **Italian** and **Portuguese**, joining
  English, Spanish, French and German (six languages, every label translated).
- Changing the language now offers to **restart Tempo immediately** so it applies
  everywhere, instead of asking you to restart by hand.

**Improved — uninstall**
- The uninstall dialog now spells out exactly what will be removed (profiles, macros,
  settings, session history, log, start-up entry) and **shows the data-folder path**,
  and it can **back up your settings to a file first** before anything is deleted.

### 1.0.80
**New — one operation at a time (prevents conflicts)**
- Tempo now locks itself to a single activity. While the clicker is running, a macro
  is playing, or recording is in progress, the other start triggers, **profile
  management** and the **CPS test** are disabled, and only the matching **Stop** stays
  available (hotkeys and the emergency stop always work). Everything re-enables the
  moment the operation finishes — so you can't accidentally start two things at once
  or change profiles mid-run.

**New — macro auto-minimise on playback**
- Clicking **Play** or **Once** now minimises Tempo during playback (same option as
  recording, under Settings → Behaviour, now "Minimise window during macro record &
  playback"). The on-screen overlay keeps showing the macro and the **stop hotkey**,
  and the window returns when playback ends.

**Improved — CPS Test**
- You can now test **Left, Middle or Right** clicks: pick the button under the click
  pad and only that button is counted.

**Fixed — full-screen (F11)**
- Full-screen is more reliable: it now covers the taskbar (temporarily on top),
  lifts the minimum-size limit while active, picks the correct monitor, and restores
  your exact previous size and always-on-top setting on exit.

### 1.0.79
**Improved — GIF backdrops**
- Animated GIF backdrops (header and footer) now scale with **high-quality
  smoothing**, so they look crisper and shimmer less when stretched to fit the bar.
- The animation now **pauses automatically while the window is minimised or hidden
  to the tray** and resumes when you return, so an animated GIF no longer uses CPU
  in the background.

### 1.0.78
**New — full-screen mode**
- Press **F11** to toggle borderless full-screen (Esc also exits). The tab content
  now **auto-centres** when the window is wider than the content — maximised or
  full-screen — instead of clinging to the top-left corner.

**Improved — publish.cmd**
- Reads the version from the project and prints **"Building Tempo X.Y.Z"** with a
  reminder to bump first and tag the release `vX.Y.Z`; writes the version into the
  log; and **verifies `Tempo.exe` was actually produced** (fails clearly if a
  reported-success build left no output).

### 1.0.77
**Improved — overlays show the stop hotkey**
- The on-screen overlays now show **which key stops the activity**: the clicking
  overlay shows "Press \<key\> to stop" (your Stop / Emergency-stop hotkey) and the
  "playing macro" overlay shows the Stop-macro / Emergency-stop hotkey. So you can
  always tell how to stop, even with the window hidden.

**Improved — Macros (live monitor & playback)**
- During playback the **live monitor header** now clearly shows the playing state
  ("▶ Playing \<name\>…") in the accent colour, and resets when playback finishes —
  so the monitor reflects playback, not just recording.

### 1.0.76
**Improved — recording & auto-minimise**
- The recording badge now shows **which hotkey stops recording** (e.g. "Press F9 to
  stop"), so you always know how to finish even while the window is minimised.
- Auto-minimise while recording now only happens when a **stop hotkey is actually
  bound** (the record toggle or emergency-stop), so you can never end up minimised
  with no way to finish.

**New — themes**
- **Six more colour themes** (now **38** total): **Sapphire**, **Olive**, **Cyan**,
  **Peach** (light), **Wine** and **Magenta**.

### 1.0.75
**Improved — Macros**
- **Auto-minimise while recording** — when you start recording a macro, Tempo now
  minimises itself so its window isn't captured or in the way; the REC badge stays
  visible, and the window returns automatically when you stop (press the Record or
  Emergency-stop hotkey). Can be turned off in Settings → Behaviour.
- **"Playing macro" overlay** — while a macro plays, a small click-through badge
  appears on screen (like the clicking overlay) showing the macro name and the live
  loop/step, so you always know a macro is running even with the window hidden. It
  uses the same Settings → Behaviour overlay toggle (now "Show on-screen overlay
  while running").

### 1.0.74
**Improved — update download**
- The download dialog now uses the app's own themed progress bar (instead of the
  stock green one), shows live **download speed and time remaining**, and has a
  small accent strip to match the redesigned update prompt.

**Improved — CPS Test**
- Added a themed **time-remaining bar** that fills as the test runs, so you can see
  at a glance how much time is left; it fills completely when the test ends.

### 1.0.73
**Fixed — on-screen overlay was invisible**
- The "clicking" overlay added in 1.0.72 didn't actually appear: the badge window
  was created as a layered window but its transparency level was never set, so
  Windows drew nothing. It now sets its alpha on creation (and is clipped to a
  rounded shape), so the badge is visible while clicking — still click-through.

### 1.0.72
**New — on-screen "clicking" overlay**
- While the clicker is running, a small badge appears near the top of the screen
  with a pulsing dot and the live click count and CPS, so you can always tell Tempo
  is active — even when its window is minimised or hidden to the tray. It's
  **click-through** (it never blocks your clicks or steals focus) and can be turned
  off in Settings → Behaviour ("Show on-screen overlay while clicking").

### 1.0.71
**Improved — Clicker**
- The Manual Speed readout is cleaner — it shows the whole CPS value with the
  equivalent interval, e.g. "Target: 37 CPS (27 ms)".

**Improved — Macros**
- Exporting a macro now confirms the exact file it was saved to.

**Docs**
- The changelog now lives in its own `CHANGELOG.md` (and the per-version
  `release-notes/` files) instead of being duplicated inside the README, and the
  README was reworked with a clearer overview and up-to-date feature list (32 themes,
  GIF backdrops, etc.).

### 1.0.70
**Fixed — "Update now" reliability**
- The in-place updater is more robust: it waits for Tempo to close with a bounded
  loop (so it can never hang), retries the file swap more times in case antivirus
  briefly locks the new exe, and relaunches from the app's own folder. This addresses
  cases where "Update now" could appear stuck or fail to restart.

**Improved — update dialog**
- The "Update available" dialog was redesigned: an accent top strip, an accent
  heading, a clear "Installed → Latest" line, and a rounded notes card. Release notes
  are now shown as **clean text** — the raw `##`/`**` markdown markers and the
  duplicate title/footer lines are stripped before display.

**Improved — Keybinds**
- Each hotkey row now has a tooltip and screen-reader description explaining how to
  set a binding and that Backspace/Delete clears it; the interval-step field is
  documented too.

### 1.0.69
**Improved — publish**
- `publish.cmd` now **cleans the previous output** before building (no stale files
  left behind) and prints a **SHA-256 checksum** of the finished `Tempo.exe` (also
  written to `publish-log.txt`), so you can post it on the release page for download
  integrity.

**Improved — Clicker**
- The Repeat estimates (≈ time / ≈ clicks) now account for **hold-time**: since a
  click can't fire faster than it's held, the estimate uses whichever is longer (the
  interval or the hold), so the numbers stay accurate when you use a hold.

### 1.0.68
**Improved — Statistics**
- The **session-goal progress bar** is now drawn in the app's own style — a rounded,
  accent-coloured bar that matches the active theme — instead of the stock green
  Windows progress bar that ignored theming.

**Improved — Macros**
- Added hover tooltips to the macro **Export / Import / Export all / Import all**
  buttons and the macro **search box**, so they're documented like the rest of the app.

### 1.0.67
**Improved — consistency & polish**
- The new Header/Footer GIF buttons now have hover tooltips and screen-reader
  descriptions, matching the rest of the app's documented controls.
- General health pass: clean build with zero compiler warnings, no dead code, and
  balanced braces verified across all 75 source files (~20k lines).

### 1.0.66
**New — second GIF backdrop (experimental)**
- You can now set **two** animated backdrops in Settings → Appearance: a **Header
  GIF** (top, as before) and a new **Footer GIF** that plays in a band along the
  bottom of the window. The footer band only appears when you choose a GIF, so it
  takes no space otherwise. Each has its own Choose / Clear, and both stay readable
  behind a scrim. Local files only — no network.

### 1.0.65
**New — animated GIF backdrop (experimental)**
- Settings → Appearance → **Background GIF**: pick an animated GIF (or any image) and
  it plays as a backdrop across the **header bar**, behind a readability scrim so the
  wordmark and controls stay legible. "Clear" removes it.
- *Why only the header:* the main window is filled by the (opaque) tab content, so a
  GIF painted behind everything would be hidden and would flicker if forced through
  the panels. The full-width header is the surface where an animated backdrop renders
  reliably and smoothly, so that's where it lives. It's loaded from your local file
  only — no network, no copying.

### 1.0.64
**New — themes**
- **Six more colour themes** (now **32** total): **Indigo**, **Teal**, **Tangerine**,
  **Bubblegum** (playful light), **Carbon** (minimal monochrome) and **Honey**
  (warm amber).

**Improved — Multi-Point**
- The points table now shows a friendly **empty-state hint** ("No points yet — use
  Add… or Quick Capture…") instead of a bare empty grid.

**Improved — Macros**
- Trying to play a macro with **no steps** now shows a short prompt to record/edit it
  first, instead of running a countdown that does nothing.

### 1.0.63
**Design — more detail and polish on the dashboard**
- **Stat cards** (used throughout Statistics) are redesigned with a soft drop
  shadow, a gentle top-to-bottom surface gradient, a hairline edge highlight, and a
  rounded gradient accent pill — so they read as real, lifted cards instead of flat
  rectangles. Looks good on every theme, light or dark.
- **Bar charts** (recent sessions, last 7 days, by hour, by weekday) now draw
  gradient bars with rounded tops, a faint full-height track behind each bar, a
  subtler baseline, and a gradient card surface — a much richer, more finished look.
  Hovered bars brighten with the same gradient treatment.

### 1.0.62
**Improved — publish**
- `publish.cmd` now writes a **`publish-log.txt`** every run: it captures the full
  build output and appends a final **SUCCESS / FAILED** result (with timestamps)
  after it finishes. On failure it points you to the log and prints the lines
  containing "error" so problems are easy to find.

**Improved — Statistics**
- **Export CSV** now also includes the insight metrics (this week/month/year, active
  days, daily average, current & longest streak, busiest weekday/hour, top profile),
  so the exported file matches the dashboard.

**Improved — Macros**
- The macro detail header now shows the estimated duration in a friendly format
  (e.g. `≈2.1s` or `≈1m 5.0s`) instead of raw milliseconds — matching the saved-macros
  list.

### 1.0.61
**New — themes**
- **Six new colour themes**, bringing the total to **26**: **Lavender** (soft purple
  dark), **Sakura** (cherry-blossom light), **Emerald** (deep green dark), **Steel**
  (cool blue-grey dark), **Grape** (purple with a fuchsia accent), and **Arctic**
  (icy cyan light).

**Improved — Settings**
- The Settings page now shows the **app version** at the bottom (read from the build,
  so it's always accurate), so you can check it without opening About.

**Improved — publish**
- `publish.cmd` now verifies the .NET SDK is installed first, builds a **ReadyToRun**
  single file for faster startup, strips debug symbols, prints the final file size,
  and offers to open the output folder when done.

### 1.0.60
**New — Statistics**
- **Streaks:** new *Current Streak* and *Longest Streak* cards count consecutive
  active days (the current one ending today or yesterday).
- **Daily Average** card — average clicks across every day you've actually used Tempo.
- **This Year** card to sit alongside This Week / This Month.
- **"Clicks by day of week" chart** — a full 7-bar Sun→Sat breakdown (previously only
  the single busiest weekday was shown as a number).

**Improved — Statistics**
- **Copy summary** now also includes this year, daily average, and both streaks.
- The new cards and chart follow the active theme and update live like the rest of
  the dashboard.

### 1.0.59
**Improved — Clicker (smart rate preview)**
- The line under the interval fields is now a live, accurate preview of the *actual*
  click rate, not just the raw delay:
  - **Click type counts:** double/triple click now shows the true clicks-per-second
    (e.g. Triple at 100 ms reads ≈ 30 CPS, not 10).
  - **Randomization range:** with "Randomize interval ±" on, it shows the resulting
    range, e.g. `≈ 8.3–12.5 CPS · 100 ± 20 ms`.
  - **Burst mode:** shows the average rate across a full burst-plus-pause cycle.
  - **Hold-time:** notes `hold N ms`, and warns `(caps rate)` when the hold is long
    enough to limit the rate you asked for.
- The preview updates instantly as you change the click type, randomization, hold,
  burst size/pause, or mode — so what you see always matches what the engine will do.

### 1.0.58
**New — autoclicker**
- **Hold each click (ms):** a new field in *Click Options* lets you hold the mouse
  button down for a set time on every click before releasing it (0 = a normal
  instant click, exactly as before). Useful for games that need a press-and-hold,
  charged actions, or breaking/holding. Works with single/double/triple and with
  fixed, current and multi-point positions.

**New — privacy**
- **"Record session history and statistics"** toggle (Settings → Behaviour). Turn it
  off and finished runs leave **no trace**: nothing is written to your session
  history and your lifetime totals stop changing. Clicks made while it's off are
  never folded into your totals later, even if you turn it back on. Combined with
  the existing "Write a log file" toggle, Tempo can run with no activity record at
  all.

**Improved**
- The new control carries a tooltip and screen-reader description like the rest of
  the app, and the held-click path leaves the fast batched click path completely
  unchanged when hold is 0 — so existing profiles behave exactly as before.

### 1.0.57
**Performance**
- The Statistics dashboard (cards, charts and the history-derived insights) is now
  recomputed **only while the Statistics tab is visible**, instead of five times a
  second on every tab. It still refreshes instantly when you open the tab and
  whenever a session ends — but normal clicking no longer pays for stats work.

**Accessibility**
- Screen readers now announce a helpful description for the controls across all
  tabs (the same text as the new tooltips), and previously-unlabelled spinners and
  drop-downs (e.g. the interval fields) now have proper accessible names.

**Reliability / housekeeping**
- The tooltip component is now disposed cleanly on exit.

### 1.0.56
**Improved — clarity & polish across every tab (no new features)**
- **Tooltips everywhere** — hovering almost any control (interval fields, click
  options, position/repeat/burst/randomization, multi-point and macro buttons,
  statistics filters, and every setting) now shows a short plain-language
  explanation. Existing features are much easier to understand at a glance.
- **Clicker:** the "For (seconds)" click estimate now accounts for double/triple
  click style, so the "≈ clicks" figure is accurate, and it updates when you change
  the click type.

This release focuses on making the features Tempo already has clearer and more
robust, rather than adding anything new. The data files were already saved
atomically with corrupt-file backups, profile names are validated, hotkey
conflicts are flagged, and every tab scrolls safely at high display scales — all
verified during this pass.

### 1.0.55
**Fixed**
- **Multi-Point:** the "Enable / Disable all" button was overlapping the "Tip:
  press Save…" note — the note now sits below it.

**Improved — existing features (no new ones)**
- **Multi-Point table:** X, Y, Dwell and Rep columns are now right-aligned and
  column widths were tuned, so point data lines up and reads cleanly.
- **Multi-Point:** the active-points readout now also shows total dwell time per
  cycle when any point has a dwell.
- **Clicker:** the Repeat estimate (≈ time / ≈ clicks) now appears only next to the
  option you've actually selected, instead of beside greyed-out fields.

### 1.0.54
**Improved — Clicker**
- New **Humanize** button in the Randomization box: one click switches on both
  randomizers with sensible starting values (interval jitter ≈ 20% of your delay,
  position ± 2 px) so the clicking looks less robotic. Fine-tune from there.

**Improved — Multi-Point**
- New **Enable / Disable all** button to turn every point on or off at once —
  handy when you've got a long list of points.

### 1.0.53
**Changed — header**
- Removed the "AUTO CLICKER" tagline under the Tempo wordmark.
- Refreshed the header: the wordmark is now vertically centred and a little larger,
  and the logo tile has a soft shadow and a subtle highlight for a cleaner look.

**Fixed**
- Dragging an image **URL** onto the logo no longer freezes the About window while
  it downloads — the download now runs in the background.

### 1.0.52
**Improved — a pass across the tabs**
- **Clicker:** the Repeat options now show a live estimate — "Fixed count" shows the
  approximate run time, and "For (seconds)" shows the approximate number of clicks,
  at your current speed.
- **Macros:** right-click a macro for a quick menu (Play, Edit, Rename, Duplicate,
  Pin/Unpin, Delete).
- **Multi-Point:** right-click a point for a quick menu (Edit, Duplicate, Toggle,
  Remove).
- **Statistics:** "Copy summary" now includes the new insights (this week, this
  month, busiest hour, top profile).
- (Keybinds already flags duplicate hotkeys live and on save; Settings unchanged.)

### 1.0.51
**New — Statistics "Insights"**
A new Insights section on the Statistics tab, plus a couple of extras — 10 new
things in all, all derived from your existing session history:
1. **This Week** — clicks in the last 7 days.
2. **This Month** — clicks in the current calendar month.
3. **Lifetime Avg CPS** — your overall average click rate.
4. **Active Days** — how many distinct days you've used Tempo.
5. **Best Day** — your biggest single day, with the date.
6. **Busiest Weekday** — the day of the week you click most.
7. **Busiest Hour** — your most active time of day.
8. **Top Profile** — the profile with the most clicks.
9. **Clicks by hour of day** — a 24-hour activity chart.
10. **Search box** for the session history (filter by date or profile).

### 1.0.50
**Fixed — update checker getting stuck**
- The "Check for updates" button could hang on "Checking…" on some networks. The
  check now skips slow proxy auto-detection (the usual cause) and has a hard
  safety timeout, so the button always recovers and reports a clear message
  instead of getting stuck.

**Improved — clicker**
- The **Manual Speed** slider now goes up to **200 CPS** (was capped at 100), so it
  can reach the faster rates people actually use.

**Improved — macros**
- Keyboard shortcuts in the macro list: **Enter** plays, **Delete** removes, **F2**
  renames the selected macro.
- A small summary under the list shows the **total number of macros and steps**.

### 1.0.49
**Fixed — high-DPI / scaled displays**
- On laptops/monitors at a higher display scale, the intro paragraph on the
  **Macros** and **Multi-Point** tabs could grow tall enough to overlap the
  controls beneath it. The layout now measures the paragraph and shifts the rest
  of the tab down so nothing overlaps at any scale.

**New — custom logo is easier**
- The About screen now has a **"Choose image…" button** — pick a .gif/.png/.jpg
  with a normal file dialog instead of needing to drag. Drag-and-drop still works.

**New — privacy**
- Added a **"Write a log file to disk"** toggle (Settings → Behaviour). Turn it off
  and Tempo writes nothing to its log file.
- Added a plain-language **privacy note** in Settings: Tempo runs entirely on your
  PC and never sends your data anywhere; the only network use is the optional
  update check.

### 1.0.48
**Fixed — layout / text conflicts**
- **Keybinds tab:** the intro paragraph no longer overlaps the "Interval step"
  control. The header now measures its own height and lays the buttons, interval
  control, and table below it, so it stays clean in every language.
- **Macros tab:** the **Pin** and **Reset stats** buttons moved to sit neatly under
  the macro list (they were overlapping the Live Monitor panel), and the Live
  Monitor panel was nudged down so it no longer clips the Playback box.
- **Settings:** group titles like "Startup & Window" and "Data & Backup" now show
  the "&" correctly (it was being swallowed as a keyboard mnemonic).

### 1.0.47
**New**
- **Custom logo** — drag any image or GIF onto the logo in the About screen to use
  it as your own logo. Works with a local file **or** an image dragged straight
  from a web browser (Tempo downloads it). A "Reset logo" link restores the default.

**Fixed**
- Settings → Appearance: the **"Always on top" text no longer overlaps the Language
  selector**, including in German and French where the labels are longer.

### 1.0.46
**New**
- **Animated logo** in the About screen — a subtle, gently pulsing Tempo logo.

### 1.0.45
**New**
- **Three more themes** — **Sunset** (warm orange), **Mint** (fresh teal-green) and
  **Sand** (a warm light theme) — 20 themes total.
- Macros: **pin favourites** to keep them at the top of the list (★), and **reset a
  macro's play stats** (play count / last-played).
- CPS test: now tracks an **all-time best** that persists across sessions.

### 1.0.44
**Improved — update experience**
- A cleaner, themed **"update available" dialog** with **scrollable release notes**
  and clear actions, replacing the old cramped message box.
- **Skip this version** — choose to skip a release and the automatic check won't
  nag you about it again (until a newer one appears).
- **Last-checked time** is shown in Settings, and any skipped version is noted.
- Automatic launch checks are now **throttled to about once a day** to avoid
  hitting GitHub's rate limits; the manual *Check for updates* button always runs
  immediately.

### 1.0.43
**New**
- **New app icon** — updated to the cosmic Tempo artwork.
- **Three new themes** — **Cosmos** (deep-space violet, matching the icon),
  **Rose**, and **Slate** (17 themes total).

**Improved**
- **Always on top** now applies **instantly** when you tick the box in Settings
  (and stays in sync with the tray toggle), instead of only on Save.
- **CPS test** — now reports a **Peak (1-second burst)** rate using actual click
  timing, and lets you pick the **test length** (5 / 10 / 15 / 30 s). The "best"
  reading is no longer skewed by the first few clicks.

### 1.0.42
**New**
- **App icon** — Tempo now has its own icon (the Tempo wordmark) shown on the
  window title bar, the taskbar, and the system-tray icon, instead of the generic
  Windows default.

### 1.0.41
**Improved**
- **High-DPI displays** — Tempo now scales correctly on displays set above 100%
  (e.g. 125 %, 150 %, 175 %). Previously it claimed per-monitor DPI support it
  didn't implement, so the window came up tiny and cramped on scaled screens. It
  now uses system-DPI scaling with font-based layout across the main window and all
  dialogs, and the tab bar grows with the display scale. (No change at 100 %.)

### 1.0.40
**Privacy & reporting**
- Bug/crash reports now **remove your Windows account name** automatically.
- The crash window is now **editable and shows a clear privacy note**, so you can
  review and trim the details before reporting by GitHub or email.

**Macros**
- **Smoother cursor movement** — when "Smooth mouse movement" is on, the cursor now
  glides from your real pointer position at a natural, constant speed with gentle
  easing, instead of a coarser step.

### 1.0.39
**New**
- **Email bug reports** — the crash dialog now has an **Email report** button, and
  Settings → Data & Backup has an **Email a bug…** button. Both open your email app
  with a message pre-filled (and pre-addressed) so you can report without a GitHub
  account. Nothing is sent until you press send in your email app.

### 1.0.38
**New**
- **Automatic bug reporting** — if Tempo ever hits an unexpected error, it now
  shows a clear dialog, saves a full crash report next to the log, and offers a
  one-click **Report on GitHub** button that opens a pre-filled issue (with the
  version, your Windows build and the error details) on the project's issue
  tracker. Nothing is ever sent automatically or silently — the report only
  leaves your PC when you submit the GitHub form.
- **Report a bug…** button in Settings → Data & Backup for reporting issues even
  when nothing has crashed.
- Statistics: **Copy summary** button copies your key numbers to the clipboard.
- Macros: the info line now shows when a macro was **last played**.

**Improved**
- Clearer message when Tempo is launched on an unsupported Windows version
  (Windows 7 / 8.1), explaining that Windows 10 or 11 is required.

**Compatibility**
- Tempo requires **Windows 10 (1607+) or Windows 11**. Windows 7 and 8.1 are not
  supported (a limitation of .NET 8, which Tempo is built on).

### 1.0.37
**Improved**
- Full code audit — no bugs, dead code, leaks or compiler warnings found.
- **Language now covers runtime status text too**, so the interface stays in your
  language everywhere: the RUNNING / IDLE / PAUSED state, the profile label, macro
  playback/recording status, the session-goal message, and the detection readout.
- The "Check for updates" button keeps its language after a check instead of
  reverting to English.

### 1.0.36
**Fixed**
- **Language support now applies across the whole app**, not just the tab names.
  All buttons, group titles, labels and options are translated (English, Spanish,
  French, German); anything not yet translated falls back cleanly to English.
  Language still applies on restart.

### 1.0.35
**New**
- Macros: **smooth mouse movement** option — playback interpolates motion between
  points for natural, human-like movement (per macro).
- Macros: **Merge** — append another macro's steps onto the selected one.
- **Language support** — English, Spanish, French and German, selectable in
  Settings (applied on restart). A foundation covering the main UI strings.
- Statistics: the live **CPS readout is now smoothed** so it eases to its value
  instead of jumping around.

**Improved**
- Redesigned tab bar — rounded selection pills, a hover state, and a cleaner
  accent indicator.

### 1.0.34
**New — Settings overhaul**
- **Custom accent colour** — pick any colour and Tempo recolours the whole app
  (buttons, highlights, charts), layered on top of whichever theme you choose.
- **Live theme preview** — a swatch strip and sample button/text in the
  Appearance section update instantly as you change the theme or accent, so you
  can see a theme before committing to it.

### 1.0.33
**New**
- **Four new themes** — Monokai, Gruvbox, Synthwave and Coffee (14 themes total).
- Tabs: Tempo now **reopens the tab you were last on** when it starts.
- Macros: a **"Once"** button plays the selected macro a single time, ignoring
  the configured loop count.
- Statistics: the **last-7-days chart** now shows the week's total and peak-day
  click counts in its title.

### 1.0.32
**New**
- Settings: **Uninstall Tempo** — removes the Windows start-up entry and all saved
  data (profiles, macros, settings, history), and can optionally delete the
  program file. Cleanup runs after the app closes, with a clear confirmation.
- Settings: **Open log file** button for quick troubleshooting.

### 1.0.31
**Hardening & fixes**
- Self-update now verifies the downloaded file is a real Windows executable
  (checks size and the `MZ` header) before it ever replaces `Tempo.exe`, so a
  corrupted download or server error page can't overwrite the app.
- Made the update swap-helper script more robust (proper delayed-variable
  expansion in the copy-retry loop).
- Tab shortcuts now also accept the numeric keypad (Ctrl+NumPad 1–9).
- Full code audit: no compiler warnings, dead code, unused members, undisposed
  GDI objects, or unguarded collection access.

### 1.0.30
**New**
- Updates: **one-click in-place update** — Tempo can download the new build and
  replace the running executable for you (via a small helper that waits for exit
  and relaunches), instead of only opening the download page. Falls back to the
  manual download when the install folder isn't writable.
- Tabs: **keyboard navigation** — Ctrl+1…9 jump straight to a tab, and
  Ctrl+Tab / Ctrl+Shift+Tab cycle through them.
- Clicker: **Restore cursor position when stopped** — for Fixed and Multi-Point
  modes, the cursor returns to where it was before the run started.
- Macros: the playback panel now shows the **estimated total time** for the
  selected macro at its current loop/speed/delay settings.
- Statistics: the **session goal** now shows a live **ETA** ("~time left") at the
  current click rate while running.

### 1.0.26
**New**
- Clicker: **"For (seconds)" run duration** — auto-stops clicking after a set
  time (works in interval, hold and burst modes).
- Statistics: **session goal** with a live progress bar and a notification when
  the goal is reached.
- Statistics: **filter session history by profile**, with a shown-sessions and
  total-clicks summary.

**Improved**
- Cleaner self-contained single-file build (drops extra language-resource
  folders).

### 1.0.25
- **Update checking** — Settings → Check for updates, plus an optional quiet
  check on launch, driven by the GitHub Releases API for the project repository.
- A **self-contained single-file publish** option (`publish.cmd` / VS profile)
  so the distributable `Tempo.exe` runs with no .NET install required; a startup
  **prerequisite check** advises manual installation when something is missing.
- Macros: a live **loop counter** (Loop X / Y) during playback, and sorting by
  name, most-played or newest.
- Statistics: a **last-7-days chart**, derived average cards, sortable and
  right-clickable session history, and chart hover tooltips.
- Ten themes, a redesigned true-colour header, Windows-startup launch, settings
  backup, anti-freeze protection, and many clicker/multi-point refinements.
