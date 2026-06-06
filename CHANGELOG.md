# Changelog

All notable changes to Tempo. Newest first. Per-release notes are also in the `release-notes/` folder and on the [Releases page](https://github.com/justcamop555-pixel/Tempo/releases).

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
