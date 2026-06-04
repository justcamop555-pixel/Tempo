<p align="center">
  <img width="200" height="200" alt="Tempo logo" src="https://github.com/user-attachments/assets/471bd647-9993-40fc-8930-8beb925518d6" />
</p>

# Tempo

**Version 1.0.68**

A full-featured Windows mouse auto-clicker built with C# and Windows Forms
(**.NET 8**). It goes well beyond a basic clicker: named profiles, multi-point
click sequences, burst and hold modes, randomization, full macro
recording/playback, fully rebindable global hotkeys (keyboard **and** mouse
buttons), anti-freeze protection, a system-tray presence, a live statistics
dashboard with history and charts, in-app update checking, **twenty** built-in
themes, **four interface languages**, and built-in **bug/crash reporting**.

> **Use responsibly.** Auto-clicking may violate the terms of service of some
> games and applications. You are responsible for how you use this tool.

---

## ⚠️ Only download Tempo from the official source

**The only official place to get Tempo is this GitHub repository:**

### → <https://github.com/justcamop555-pixel/Tempo/releases>

Always download `Tempo.exe` from the **Releases** page above. That is the single
official source. **Do not download Tempo from anywhere else.**

Copies offered on other websites — "free download" portals, software mirrors,
ad links, search-result downloads, file-sharing sites, YouTube descriptions,
Discord messages, or anyone claiming to share "Tempo" — are **not official** and
**may be modified to contain malware, spyware, or miners.** If you didn't get it
from the link above, don't trust it.

**How to stay safe:**

- Get every release **only** from the official Releases page (or via the app's
  built-in updater, which only ever connects to this same GitHub repository).
- Tempo is **free**. Nobody should ever charge you for it, ask for your password,
  payment details, or personal information, or tell you to disable your
  antivirus. If something does, it isn't the real Tempo.
- Tempo only connects to the internet for **one** thing: checking GitHub for a
  newer version. It does not collect or send your data anywhere.
- A **"Windows protected your PC" / "Unknown publisher"** prompt on the official
  build is normal (it isn't code-signed) — click **More info → Run anyway**. This
  is *not* the same as downloading from an untrusted source; it appears even on
  the genuine file. When in doubt, re-download from the official link above.
- If you find Tempo being distributed somewhere else, treat it as untrusted and
  don't run it.

---

## Requirements

- **Windows 10 (version 1607 or later) or Windows 11**, 64-bit.
  **Windows 7 and Windows 8.1 are not supported** — Tempo runs on .NET 8, which
  itself requires Windows 10 or newer, so it will not run on those older versions.
- **.NET 8 Desktop Runtime** to run a published build, or the **.NET 8 SDK** to
  build from source — <https://dotnet.microsoft.com/download/dotnet/8.0>
- Visual Studio 2022 (any edition) is recommended, but not required.

This is a Windows-only application because it relies on the Win32 input APIs
(`SendInput`, `RegisterHotKey`, low-level mouse/keyboard hooks). It will not
build or run on macOS or Linux.

> **First run:** Tempo checks the host on startup. If a requirement is missing
> (for example the .NET 8 Desktop Runtime, or an unsupported Windows version) it
> shows a clear message telling you what to install **manually** and offers to
> open the download page, instead of failing with a cryptic error. A
> **self-contained** build (see below) bundles the runtime, so nothing extra
> needs installing at all.

---

## Building & running

### Option A — Visual Studio 2022

1. Open `AutoClicker.csproj` (or the folder) in Visual Studio 2022.
2. Build configuration `Debug` or `Release`, platform `Any CPU` (or x64/x86).
3. Press **F5** to build and run.

### Option B — Command line

From the project folder:

```bash
dotnet run -c Release
```

To produce a **self-contained single executable** (recommended for sharing —
end users do **not** need to install .NET):

```bash
dotnet publish -c Release -r win-x64 -p:SelfContained=true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Or simply run the bundled helper script from the project folder:

```bat
publish.cmd            REM win-x64 (default)
publish.cmd win-x86    REM 32-bit
publish.cmd win-arm64  REM ARM64
```

The script checks that the .NET SDK is installed, builds a compressed,
**ReadyToRun** single file (faster startup) with debug symbols stripped, prints
the final size, and offers to open the output folder when it finishes.

In Visual Studio you can instead right-click the project → **Publish** and pick
the included **win-x64-selfcontained** profile.

The resulting single `Tempo.exe` is placed under `bin\publish\win-x64\`. It
bundles the .NET 8 runtime, so it runs on a clean Windows machine with nothing
else installed. (The publish-time settings only apply when a runtime identifier
is given, so ordinary Debug/Release builds are unaffected and stay
framework-dependent.)

A framework-dependent build (no `-r`/`SelfContained`) is smaller but requires
the .NET 8 Desktop Runtime on the target machine; if it's missing, Tempo's
startup check explains how to install it manually.

---

## Feature overview

### Clicker tab
- **Profiles** — create, save, duplicate, rename and delete named
  configurations. The last used profile is restored on launch.
- **Interval** — set the delay between clicks in hours / minutes / seconds /
  milliseconds (minimum 1 ms). A live hint shows the resulting click rate, and
  editing the interval while running applies it immediately.
- **Manual speed slider** — drag (or use ±) to set 1–100 clicks/sec; stays in
  sync with the interval fields.
- **Button & type** — left / right / middle button, single / double / triple
  click.
- **Modes**
  - *Interval* — repeats on a drift-compensated timer.
  - *Hold* — clicks only while the start/stop key is physically held down.
  - *Burst* — fires N clicks, pauses, repeats.
- **Position**
  - *Current cursor position* — clicks wherever the pointer is.
  - *Fixed position* — clicks a single captured coordinate.
  - *Multi-point* — cycles through a list of points (see the Multi-Point tab).
  - *Restore cursor when stopped* — for Fixed / Multi-Point modes, return the
    pointer to where it was before the run started.
- **Repeat** — run until stopped, stop after a fixed number of clicks, or run
  for a set duration (seconds), after which clicking stops automatically.
- **Randomization** — add ± jitter to the interval (ms) and/or the click
  position (px) to avoid a perfectly regular pattern.
- **Anti-freeze protection** — caps the maximum clicks/second and adaptively
  throttles when CPU load is high, so the machine stays responsive. The live
  measured CPU% and effective rate are shown.

### Multi-Point tab
Build an ordered list of click points. Each point has its own label, X/Y,
button, click type, dwell time, per-point repeat count and enabled flag.
- **Quick Capture** grabs coordinates from a full-screen overlay — and keeps
  capturing point after point until you press **Esc**.
- **Tick a row** to enable/disable that point; **Delete** removes the selected
  point and **Ctrl+D** duplicates it.
- **Traversal order**: Sequential, Reverse, Random or Ping-Pong.
- **Show on screen** flashes numbered markers at every point.
- The currently-clicked point is highlighted live during a run.

### Macros tab
- **Record** real mouse movements, clicks and key presses, preserving the
  original timing. Append new recordings onto an existing macro.
- **Play back** with per-macro defaults: loop count (0 = infinite), speed
  multiplier (0.1×–10×), pre-play countdown, and a delay between loops.
- **Live monitor** shows each step as it plays, with a loop counter and the
  macro's notes.
- **Step editor** — reorder, delete, edit delays/positions/keys, strip mouse
  moves, and insert click/key/position steps.
- **Organise** — rename, duplicate, search/filter, add notes, and **sort** by
  name, most-played or newest.
- **Backup** — export/import a single macro or the whole collection at once.
- **Merge** — append another macro's steps onto the selected one.
- **Pin favourites** — star a macro to keep it at the top of the list.
- **Reset stats** — clear a macro's play count and last-played time.
- **Smooth movement** — optionally interpolate mouse motion during playback for
  natural, human-like movement (per macro).
- **Quick-play hotkeys** — bind keys to instantly play macros #1/#2/#3.

### Statistics tab
A live dashboard of cards: session & launch clicks, current/peak/average CPS,
clicks-per-minute, elapsed time and a "today" total, plus a live CPS graph.
- **Session goal** — set a target click count for the session and watch a live
  progress bar with an ETA at the current rate; Tempo notifies you when the goal
  is reached.
- **Per-button breakdown** with a stacked distribution bar.
- **Lifetime totals** and **records** (most clicks per run, longest run,
  averages per session) that persist across runs.
- **Charts** — clicks per recent session and clicks over the last 7 days, both
  with hover tooltips.
- **Session history** — every completed run is logged; double-click for
  details, right-click to copy or delete, click a column header to sort, and
  **filter by profile** with a running totals summary.
- **Export CSV** of the full summary and history; reset session/lifetime or
  clear history.

### Settings tab
- **Theme** — Dark, Light, Midnight, Ocean, Forest, Crimson, Solarized, AMOLED,
  Nord, Dracula, Monokai, Gruvbox, Synthwave, Coffee, Cosmos, Rose, Slate,
  Sunset, Mint and Sand
  (applied instantly), with
  a **live preview** and an optional **custom accent colour** that recolours the
  whole app.
- **Startup & window** — launch Tempo when you sign in to Windows, and hide the
  window to the tray when clicking starts.
- **Behaviour** — minimise to tray, start in tray, tray notifications, confirm
  on exit while running, Escape as an emergency stop, and a start delay.
- **Updates** — check for updates on demand, and optionally on launch.
- **Language** — English, Spanish, French and German (applied on restart).
- **Data & backup** — open the data folder, open the log file, export/import all
  settings, and **uninstall Tempo** (removes the start-up entry and all saved
  data, and can optionally delete the program file).

### Keybinds tab
Every action is rebindable to a keyboard shortcut **or a mouse button**, with
live conflict highlighting. Bindable actions include Start/Stop, Pause/Resume,
Pick position, Emergency stop, profile switching, macro play/stop, quick-play
macros #1–#3, show points overlay, toggle anti-freeze, add point at cursor and
show/hide window.

### Global hotkeys (defaults)
| Action          | Default key |
|-----------------|-------------|
| Start / Stop    | **F6**      |
| Pick position   | **F7**      |
| Emergency stop  | **F8**      |

The emergency stop immediately halts clicking, macro playback and recording.

You can also switch tabs from the keyboard: **Ctrl+1…9** jump to a tab, and
**Ctrl+Tab** / **Ctrl+Shift+Tab** cycle through them.

---

## Updates

Tempo can check whether a newer version is available, using the **GitHub
Releases** for its own repository.

- **In the app:** Settings → **Check for updates** runs a check on demand. With
  *"Check for updates when Tempo starts"* enabled (the default), Tempo also does
  a quiet background check shortly after launch (at most about once a day) and
  notifies you **only if** an update exists. When a newer version is found it shows
  the installed and latest versions plus scrollable release notes, and offers to
  **install it in place**, **open the download page**, or **skip that version** so
  the automatic check won't mention it again.

### How updates are distributed (for maintainers)

Tempo reads the **GitHub Releases API** for its repository — no manifest file or
server to maintain, and no authentication for a public repo:

```
https://api.github.com/repos/justcamop555-pixel/Tempo/releases/latest
```

To ship an update:

1. Build the self-contained executable (`publish.cmd`).
2. On the repo, go to **Releases → Draft a new release**.
3. Set a tag like `v1.0.42` (a leading `v` is fine), write the release notes in
   the description, and **attach `Tempo.exe`** as a release asset.
4. **Publish release.**

The app compares the latest release's tag to its own version. When the tag is
higher it shows the release notes and links to the attached `.exe` (falling back
to the release page if no `.exe` asset is attached). The repository is set in
`UpdateChecker.Repository` in `Utils/UpdateChecker.cs`.

> Make sure the `Tempo.exe` you attach was built **after** bumping the version,
> or it will still report the old version when it checks for updates.

### How users get the update

When an update is found, Tempo offers to install it **in place**:

1. The user chooses **Yes** to update now.
2. Tempo downloads the new `Tempo.exe` (with a progress dialog).
3. A small helper waits for Tempo to close, overwrites the old executable with
   the new one, and relaunches it automatically.

Settings, profiles, macros and history live in `%LocalAppData%\AutoClicker\`, so
they are always preserved across an update.

If in-place update isn't possible — for example Tempo is installed somewhere the
user can't write to (such as `Program Files` without admin rights), or the
release has no attached `.exe` — Tempo instead offers to open the download page
so the user can replace the file manually. Running Tempo from a normal, writable
location (Downloads, Desktop, a folder in your user profile) keeps one-click
updating working without needing administrator rights.

---

## Where data is stored

Configuration lives under your local app-data folder:

```
%LocalAppData%\AutoClicker\
├── settings.json     global settings & hotkeys
├── profiles.json     saved click profiles
├── macros.json       recorded macros
├── sessions.json     session history for the statistics tab
└── logs\autoclicker.log
```

Deleting these files resets the app to defaults. You can also remove everything
from **Settings → Uninstall Tempo**, which deletes this folder, removes the
Windows start-up entry, and (optionally) deletes `Tempo.exe` itself.

---

## Project structure

```
AutoClicker/
├── Program.cs                 entry point, single-instance guard, crash handlers
├── app.manifest               DPI awareness / OS compatibility
├── publish.cmd                helper to build a self-contained Tempo.exe
├── Assets/tempo.ico           application icon
├── Native/                    P/Invoke, hotkeys, low-level hooks
├── Models/                    data types (profiles, points, macros, settings)
├── Engine/                    input simulation, click engine, macro record/play
├── Persistence/               JSON load/save for settings, profiles, macros, history
├── Utils/                     logging, screen-geometry, CPU monitor, startup,
│                              updates, localization, crash/bug reporting, app icon
└── UI/                        forms, theming, controls, tab implementations
    ├── MainForm.cs            shell, tray, hotkeys, engine wiring
    ├── MainForm.Clicker.cs    clicker tab
    ├── MainForm.MultiPoint.cs multi-point tab
    ├── MainForm.Macros.cs     macros tab
    ├── MainForm.Statistics.cs statistics tab
    ├── MainForm.Settings.cs   settings tab
    ├── MainForm.Keybinds.cs   keybinds tab
    └── CrashReportForm.cs     error/bug report dialog
```

(Other dialogs, custom controls and the theme engine also live under `UI/`; the
tree above lists the main pieces rather than every file.)

The `MainForm` is implemented as a C# `partial class` split across several
files — one per tab — so each section stays focused and readable.

---

## Notes & tips

- **"Unknown publisher" warning:** Windows may warn the first time you run
  `Tempo.exe` because it isn't code-signed. Click **More info → Run anyway** —
  this is expected for an unsigned app and is safe.
- If a global hotkey fails to register, another application is probably already
  using that combination. Pick a different one on the Keybinds tab.
- To click inside an elevated (administrator) window, run Tempo as
  administrator too. You can change the requested privilege level in
  `app.manifest` (`asInvoker` → `requireAdministrator`).
- Hold mode polls the key state directly, so the Start/Stop global hotkey is
  temporarily not registered while Hold mode is selected.
- If the window ends up off-screen, it is automatically nudged back into view on
  launch.

---

## Reporting bugs

Found a problem? There are three easy ways to report it — pick whichever suits you:

- **In the app:** if Tempo hits an unexpected error it shows a report window with
  one-click **Report on GitHub** and **Email report** buttons. You can also report
  proactively any time from **Settings → Data & Backup** (**Report a bug…** or
  **Email a bug…**).
- **On GitHub:** open an issue at
  <https://github.com/justcamop555-pixel/Tempo/issues>.
- **By email:** <jompikoo@gmail.com>.

Reports are pre-filled with the version, your Windows version and the error
details — and nothing is sent until you submit. See **Privacy** below for exactly
what a report contains.

## Privacy

Tempo is built to respect your privacy:

- **Nothing is sent anywhere automatically.** Bug reports only leave your PC when
  *you* press send/submit in your own browser or email app.
- **Reports contain only** Tempo's version, your Windows version, and the technical
  error details — **never** your clicks, recorded macros, settings, or files.
- **Your Windows account name is removed** from report text automatically (paths
  like `C:\Users\YourName\…` become `C:\Users\<user>\…`).
- **You can review and edit** the report before sending — the crash window shows
  the full text in an editable box so you can delete anything you don't want to share.
- Tempo has no servers and collects no analytics or telemetry.
- **Local-only controls for your own data:**
  - **Record session history and statistics** (Settings → Behaviour) — turn off and
    finished runs leave no trace: nothing is written to the session history and your
    lifetime totals stop changing. Clicks made while it's off never count later.
  - **Write a log file to disk** — turn off and Tempo writes nothing to its log file.
  - Everything Tempo stores lives only on your PC, and you can wipe it any time from
    Statistics (Reset session / Reset lifetime / Clear history) or by deleting the
    data folder (Settings → Data & Backup → open data folder).

## Changelog

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
