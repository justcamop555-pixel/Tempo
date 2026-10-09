# Latest release notes

This file always holds the notes for the most recent version. Per-version notes live in the `release-notes/` folder, and every change is in `CHANGELOG.md`.

## Tempo 1.0.323

**Captions tell speakers apart, by name and by face, and stop inventing words over music · hotkeys fire while you hold Shift or Ctrl · Tempo starts about three times faster and idles quietly · Accounts can be a window of its own · Discord no longer shows Tempo as the Steam game "Tempo" · laptops at 125% and 150% · hundreds of fixes**

### Live captions
- **Captions ignore sound with no voice in it.** Before Whisper hears a stretch of audio, a small voice-detection network (Silero VAD) checks whether anyone is speaking. Music, fans, gunfire and game sound no longer turn into invented words: on 30 seconds of exactly that, with nobody speaking, the captions now show nothing. Speech is untouched. **Settings → Live Captions → Caption song lyrics** lets music through when you want lyrics captioned.
- **One person, one label.** The face analysis and the voice matcher used to number speakers each their own way. Faces are now linked to voices, labels land on the words that person actually said, and the voice matcher tells people apart much better.
- **See who is talking, and name them.** A new **Speakers** card on the Captions tab lists each speaker in their caption-bar colour, with their face from the video once it is linked. Click a speaker to give them a name, and "Speaker 2" becomes "Mia" on the caption bar and in the transcript.
- **Captions read like sentences, not fragments**, and the caption history is easier to follow.
- **Moving the captions shows what you're doing** and no longer leaves anything behind. Where you put the bar and the history now stays with this PC.
- **Save, Copy and Subtitles… say what happened** (a file that couldn't be written, a clipboard another program is holding, nothing transcribed yet) instead of failing silently. WebVTT subtitles now escape their text, so a "<" no longer hides the rest of a line.

### Hotkeys and keybinds
- **Hotkeys work while you hold Shift or Ctrl.** F6 now fires while you hold Shift to sprint or Ctrl to crouch. This covers the function keys, the middle and side mouse buttons, the media keys, Pause, Scroll Lock and the Hold-to-click key. Keys that type (letters, digits, arrows, Enter) still need their exact combination. It's on by default, with a tick box on the Keybinds tab.
- **Fixed: hotkeys did nothing while Tempo sat in the tray**, and every hotkey could stop working after you used a keybind field. The keys Tempo shows everywhere now match the keys that work.
- **A side mouse button bound to Tempo no longer also sends your browser Back.**
- **The Keybinds tab:** a key Tempo has reserved can now be pressed into another action's field, one combination always runs one action, and new warnings name combinations that would break other programs (Ctrl + C, keys that type a character with AltGr, Tempo's own window shortcuts). The list shows 12 rows where it showed 7.

### Faster and lighter
- **Tempo starts about three times faster.** A start to the tray is ready in about 0.45 s instead of about 1.2 s, and starting with Windows is lighter.
- **Fixed: Tempo used a tenth of a CPU core while doing nothing.**
- **Switching tabs shows the new page in one piece**, in 20–100 ms where it took up to 600 ms.

### Macros
- **Record camera turns for games, and the mouse side buttons.** Macros replay what you actually did, smooth mouse movement keeps the macro's timing, and recordings no longer include Tempo's own controls.
- **The macro hotkeys respect what's running.** Play no longer starts during a recording and Record no longer starts during playback. A quick-play key whose macro was deleted now says so, and renaming a macro carries its quick-play key along.
- **Import is safer.** Import all now asks about macros that run Python scripts, as Import… always has, and a macro you turn down is discarded rather than kept in the recycle bin.
- **Test a Python script before it's in a macro** with the new **Test run** button. A failed script now tells you why instead of "The script exited with code 1".

### Roblox accounts
- **Accounts can open in a window of its own**, beside your game, and its first screen was redesigned.
- **The Roblox client check looks at everything the game loads**, recognises Bloxstrap, Fishstrap and Voidstrap by where they are, and no longer leaks memory on every check.
- **Fixed: multi-instance could close the Roblox game you were already playing.**
- **Local API, version 4.**

### Profiles
- **Switching profiles no longer throws away unsaved changes**, and every way of switching (the dropdown, the cards, the tray, the hotkeys) now does the same thing.
- **Fixed: Save on the Clicker tab threw away the rest of the profile**, and renaming a profile through Save deleted the old one.
- **Fixed: "Click without moving my cursor" never actually worked.** It does now.

### Updates
- **Downloads survive a bad connection** and pick up where they stopped. A failed update is no longer silent and can't leave Tempo closed. Tempo also checks while it stays open in the tray, and the update dialog is in your language.

### Looks and readability
- **Laptops at 125% and 150%:** every window now grows with its text instead of cutting it off.
- **Colours are right with HDR on**, and hints and notes are muted the way they were designed.
- **The tray menu uses the app's colours**, with its own icon for every item and a new **Restart Tempo** item.
- **The status bar, the tab list and the tooltips were redone**: easier to read, and tooltips stay on the screen.
- **The privacy note at the foot of Settings is a panel you can read**, and it now lists accurately everything that goes online and where to switch it off.

### Live debug
- **Live debug no longer freezes Tempo.** Its filter is a real search (two words, `"a phrase"`, `-hide`, `tag:captions`, `level:warn`), and reading older lines, Clear view and selecting text all behave while new lines arrive.

### Also fixed
- **Discord showed Tempo users as playing the Steam game "Tempo".** The installed program is now called TempoClicker.exe.
- **Tempo sometimes didn't start with Windows** after a sign-out. There is also a new option to open the window at sign-in.
- **Tempo "not responding" on first use**: the slow first-time work no longer runs on the window's thread.
- **Double-clicking the tray icon "did nothing"**, and **"&" in macro, profile and account names** went missing in lists.
- Many texts that still showed in English are translated, and every dialog is now checked in all six languages.
