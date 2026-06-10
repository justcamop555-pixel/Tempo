# Changelog

All notable changes to Tempo. Newest first. Per-release notes are also in the `release-notes/` folder and on the [Releases page](https://github.com/justcamop555-pixel/Tempo/releases).

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
