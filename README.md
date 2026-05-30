# Tempo

**Version 1.0.34**

A full-featured Windows mouse auto-clicker built with C# and Windows Forms
(**.NET 8**). It goes well beyond a basic clicker: named profiles, multi-point
click sequences, burst and hold modes, randomization, full macro
recording/playback, fully rebindable global hotkeys (keyboard **and** mouse
buttons), anti-freeze protection, a system-tray presence, a live statistics
dashboard with history and charts, in-app update checking, and **fourteen** built-in
themes.

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

- **Windows 10 or 11**
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
  Nord, Dracula, Monokai, Gruvbox, Synthwave and Coffee (applied instantly), with
  a **live preview** and an optional **custom accent colour** that recolours the
  whole app.
- **Startup & window** — launch Tempo when you sign in to Windows, and hide the
  window to the tray when clicking starts.
- **Behaviour** — minimise to tray, start in tray, tray notifications, confirm
  on exit while running, Escape as an emergency stop, and a start delay.
- **Updates** — check for updates on demand, and optionally on launch.
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
  a quiet background check shortly after launch and notifies you **only if** an
  update exists. When a newer version is found it shows the installed and latest
  versions plus the release notes, and offers to **install it in place** (or to
  open the download page).

### How updates are distributed (for maintainers)

Tempo reads the **GitHub Releases API** for its repository — no manifest file or
server to maintain, and no authentication for a public repo:

```
https://api.github.com/repos/justcamop555-pixel/Tempo/releases/latest
```

To ship an update:

1. Build the self-contained executable (`publish.cmd`).
2. On the repo, go to **Releases → Draft a new release**.
3. Set a tag like `v1.0.26` (a leading `v` is fine), write the release notes in
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
├── Program.cs                 entry point, single-instance guard
├── app.manifest               DPI awareness / OS compatibility
├── publish.cmd                helper to build a self-contained Tempo.exe
├── Native/                    P/Invoke, hotkeys, low-level hooks
├── Models/                    data types (profiles, points, macros, settings)
├── Engine/                    input simulation, click engine, macro record/play
├── Persistence/               JSON load/save for settings, profiles, macros, history
├── Utils/                     logging, screen-geometry, CPU monitor, startup, updates
└── UI/                        forms, theming, controls, tab implementations
    ├── MainForm.cs            shell, tray, hotkeys, engine wiring
    ├── MainForm.Clicker.cs    clicker tab
    ├── MainForm.MultiPoint.cs multi-point tab
    ├── MainForm.Macros.cs     macros tab
    ├── MainForm.Statistics.cs statistics tab
    ├── MainForm.Settings.cs   settings tab
    └── MainForm.Keybinds.cs   keybinds tab
```

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

## Changelog

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
