# Tempo

**Version 1.0.25**

A full-featured Windows mouse auto-clicker built with C# and Windows Forms
(**.NET 8**). It goes well beyond a basic clicker: named profiles, multi-point
click sequences, burst and hold modes, randomization, full macro
recording/playback, fully rebindable global hotkeys (keyboard **and** mouse
buttons), anti-freeze protection, a system-tray presence, a live statistics
dashboard with history and charts, and **ten** built-in themes.

> **Use responsibly.** Auto-clicking may violate the terms of service of some
> games and applications. You are responsible for how you use this tool.

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
> open the download page, instead of failing with a cryptic error. If the
> runtime is entirely absent, Windows itself will prompt you to install it the
> first time you launch `Tempo.exe` — install the **Desktop Runtime** (x64) from
> the link above.

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
- **Repeat** — run until stopped, or stop after a fixed number of clicks.
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
- **Per-button breakdown** with a stacked distribution bar.
- **Lifetime totals** and **records** (most clicks per run, longest run,
  averages per session) that persist across runs.
- **Charts** — clicks per recent session and clicks over the last 7 days, both
  with hover tooltips.
- **Session history** — every completed run is logged; double-click for
  details, right-click to copy or delete, and click a column header to sort.
- **Export CSV** of the full summary and history; reset session/lifetime or
  clear history.

### Settings tab
- **Theme** — Dark, Light, Midnight, Ocean, Forest, Crimson, Solarized, AMOLED,
  Nord and Dracula (applied instantly).
- **Startup & window** — launch Tempo when you sign in to Windows, and hide the
  window to the tray when clicking starts.
- **Behaviour** — minimise to tray, start in tray, tray notifications, confirm
  on exit while running, Escape as an emergency stop, and a start delay.
- **Data & backup** — open the data folder, export/import all settings.

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

---

## Updates

Tempo can check whether a newer version is available.

- **In the app:** Settings -> **Check for updates** runs a check on demand. With
  *"Check for updates when Tempo starts"* enabled (the default), Tempo also does a
  quiet background check a couple of seconds after launch and notifies you **only
  if** an update exists. Either way, if a newer version is found it shows the
  installed/latest versions and what's new, and offers to open the download page.

### How updates are distributed (for maintainers)

Tempo reads the **GitHub Releases API** for its repository
(`justcamop555-pixel/Tempo`) — no manifest file or server to maintain, and no
authentication for a public repo:

```
https://api.github.com/repos/justcamop555-pixel/Tempo/releases/latest
```

To ship an update:

1. Build the self-contained executable (`publish.cmd`).
2. On the repo, go to **Releases → Draft a new release**.
3. Set a tag like `v1.0.26` (a leading `v` is fine), write release notes in the
   description, and **attach `Tempo.exe`** as a release asset.
4. **Publish release.**

The app compares the latest release's tag to its own version. When the tag is
higher it shows the release notes and links to the attached `.exe` (falling back
to the release page if no `.exe` asset is attached).

> The repository is set in `UpdateChecker.Repository` in `Utils/UpdateChecker.cs`.

### How users get the update

Because Tempo is a single self-contained `.exe`, "updating" means downloading the
new build and replacing the old one:

1. The user clicks **Yes** on the update prompt (or opens the download link).
2. They download the new `Tempo.exe`.
3. They close Tempo and replace the old file. Settings, profiles, macros and
   history live in `%LocalAppData%\AutoClicker\`, so they are kept.

If you distribute via an installer/MSI instead, point the manifest `url` at the
new installer and users simply run it.

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

Deleting these files resets the app to defaults.

---

## Project structure

```
AutoClicker/
├── Program.cs                 entry point, single-instance guard
├── app.manifest               DPI awareness / OS compatibility
├── Native/                    P/Invoke, hotkeys, low-level hooks
├── Models/                    data types (profiles, points, macros, settings)
├── Engine/                    input simulation, click engine, macro record/play
├── Persistence/               JSON load/save for settings, profiles, macros, history
├── Utils/                     logging, screen-geometry, CPU monitor, startup registry
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

### 1.0.25
- **Update checking** — Settings -> Check for updates, plus an optional quiet
  check on launch, driven by a small hosted JSON version manifest.
- A **self-contained single-file publish** option (`publish.cmd` / VS profile) so
  the distributable `Tempo.exe` runs with no .NET install required; a startup
  **prerequisite check** advises manual installation when something is missing.
- Macro playback now shows a live **loop counter** (Loop X / Y) and estimated
  total time; macros can be **sorted** by name, most-played or newest.
- Statistics gained a **last-7-days chart**, derived average cards, sortable and
  right-clickable session history, and chart hover tooltips.
- Ten themes, a redesigned true-colour header, Windows-startup launch, settings
  backup, anti-freeze protection and many clicker/multi-point refinements.
