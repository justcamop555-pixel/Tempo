# Latest release notes

This file always holds the notes for the most recent version. Per-version notes live in the `release-notes/` folder, and every change is in `CHANGELOG.md`.

## Tempo 1.0.325

**Anti-Freeze watches the app you click · Roblox update alerts · sign in with any Chromium browser · a randomizer that keeps your speed and your spot · fixed for 150% laptops · themes and languages reach every window · hundreds of fixes**

### Anti-Freeze
- **It watches the app you're clicking.** It used to watch Tempo's own CPU use against 80%, which one click thread never reaches, so in practice it was only a speed cap. Now it asks the clicked window twice a second whether it still answers. It slows down while the app is slow, and holds the clicks while the app doesn't answer at all, instead of clicking into a frozen window. A full CPU or memory counts only when the app is slow too, because games often sit at 100%.
- **The card shows the PC:** CPU, GPU and memory as Task Manager counts them, and how Windows is powering it ("Battery 64% · Best power efficiency").
- **Full speed while something runs.** On battery and in the power-saving modes, Windows runs apps it thinks are in the background on slower cores. While the clicker, a macro, a recording or camera movement runs, Tempo asks Windows for full speed, and gives it back when it stops.

### A more human clicker
- **Randomized clicks stay where you put the cursor.** In "where the cursor is" mode, each click was spread around the previous click, so the clicks drifted away (about 140 px after 20,000 clicks). They now stay around your spot and follow your own mouse moves. When the run stops, the cursor goes back to your spot, unless you moved it.
- **Interval jitter keeps your speed.** A jitter bigger than the interval slowed the average and piled up 1 ms gaps (3 ± 10 ms averaged 4 ms). Jitter is now capped by the interval, so the average is exactly the speed you set, and the rate line shows the real ± range.
- **The position spread is round**, and it never reaches into the next window, so a background click near a window's edge can't land in another app.
- **Changes apply during a run:** jitter, spread and press length, as the interval already did.
- **Humanize fits the speed:** about 20% of the interval (it added at least ±5 ms, even at 1 ms) and a natural press length. Turning it off undoes only what it changed.
- **Fixed:**
  - Hold mode where the hold key is also the key Tempo presses ("hold E to press E") stopped and restarted at the key's repeat rate.
  - "Restore cursor position" moved the mouse after a keyboard run.
  - Settings that don't apply are greyed out.
  - Burst pauses barely varied.

### Roblox and accounts
- **Roblox update alerts.** Tempo asks Roblox every 2 hours, at the same public address Roblox's own client uses, whether a newer client is out. The alert offers **Update now**, **Later** or **Keep this version**. Roblox updates itself and turns away old clients after a while, so "keep" means no reminders until the next release. Turn it off in Settings › Behaviour.
- **Sign in with any Chromium browser:** Opera GX, Vivaldi, Yandex, Chromium, Thorium, the Beta, Dev and Canary channels, and any other Chromium browser Windows knows about. Before, Tempo knew only Chrome, Edge, Brave and Opera. Firefox-based browsers are listed, with why they can't be used.
- **Fixed: Roblox wouldn't launch after it updated itself.** Its links still named the version it had just deleted. Tempo said "only partly installed" and hid the game preview while Roblox was running. It now starts the installed client directly.
- **The Accounts window opens where you can see it.** Before, it could open under an always-on-top main window, too big for a laptop screen, or behind the app in front.

### 125% and 150% screens
- **Fixed for laptops at 150%:**
  - The saved macros list showed names over cut-off details.
  - The splash screen's text touched the card's edges.
  - The statistics charts, stat cards and notification cards were drawn at 100% sizes around 150% text. They now scale as a whole.
- **The window fits the screen on first start.** It came up bigger than the screen, with its title bar above the top.

### Look and language
- **Spinners no longer look like "[ 100 ]".** A stray frame was drawn inside every number box. The digits are centred now.
- **17 rows that touched or didn't line up**, in every language, are fixed: the profile row, the Repeat card, search boxes beside their filters, the Macros columns, the Statistics filter, and the notification and caption settings. The CPS chart uses round steps (0.75 was shown as "0.8").
- **A theme change reaches every window:** the notification history, on-screen cards, the recording badge, the right-click menus of lists and Live debug. The white line along the status bar is gone. Main buttons wear the accent colour in every window. "Match Windows" no longer re-themes on unrelated system changes.
- **Dates are in Tempo's language.** Some screens dated things in Windows' language: a Spanish Tempo on English Windows showed "Mon 10:42, 19 Oct". The saved transcript's heading and the crash window's buttons are translated, and German says "Salve" for bursts everywhere.

### Safety, install and the rest
- **Tamper check details:** Settings › Data & Backup › Details shows the file, its SHA-256, its signature, where it was downloaded from, and each check's result, with Copy details for a bug report.
- **New warning:** program files that aren't Tempo's, loaded into it from its folder. Tempo also no longer looks for a speech library by name in its folder and on the PATH at every start.
- **install.cmd copies only Tempo.** It copied every DLL found beside the download, including other programs' files in Downloads.
- **Restart after an update waits up to 2 minutes for the old copy to close.** On a slow PC, the new copy said "already running" and quit, leaving no Tempo.
- **Uninstall keeps your latest changes** when it keeps your data. It also removes Tempo's leftovers and its 270 MB unpack folder in Temp.
- **Short macro tests:** a take with only movement is kept, and an empty take tells you why (typed into Tempo, only hotkeys…).
- **After playback you started with a hotkey in another app, Tempo stays minimised** instead of popping up over the game.
- **Exact status bar figures** (CPU to 0.00% from processor cycles, RAM to 0.01 MB).
- **Live debug:** the PC line no longer disappears after the first refresh, every log tag has its colour, and there are new lines for the sign-in browser and Roblox updates.
- **About › Copy details**, and crash reports name the source code they were built from.
