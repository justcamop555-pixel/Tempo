<p align="center">
  <img width="160" height="160" alt="Tempo logo" src="https://github.com/user-attachments/assets/471bd647-9993-40fc-8930-8beb925518d6" />
</p>

<h1 align="center">Tempo</h1>

<p align="center">
  A fast, modern, <strong>free</strong> Windows auto-clicker.<br>
  Clicking, multi-point, macros, statistics, themes — lightweight and easy to use.
</p>

<p align="center">
  <a href="https://github.com/justcamop555-pixel/Tempo/releases"><strong>Download</strong></a>
  ·
  <a href="CHANGELOG.md">Changelog</a>
  ·
  <a href="https://justcamop555-pixel.github.io/Tempo">Website</a>
</p>

---

## Install Tempo (about 60 seconds)

> **Only ever download Tempo from the official Releases page below.** Copies from
> other sites, "free download" portals, ad links, or Discord messages may be
> tampered with. The one official source is:
>
> ### https://github.com/justcamop555-pixel/Tempo/releases

You have **two easy ways** to install. Most people should pick Option 1.

### Option 1 — Recommended: the installer (Start Menu + easy uninstall)

1. On the [Releases page](https://github.com/justcamop555-pixel/Tempo/releases),
   download **`Tempo-Setup-<version>.zip`** from the latest release.
2. **Right-click the .zip and choose Extract All**, then keep all the files together
   in one folder.
3. Double-click **`install.cmd`**. No administrator rights are needed.
4. Launch **Tempo** from the Start Menu. Done!

This puts Tempo in your account, adds a Start Menu shortcut, and registers it under
**Settings > Apps** so you can uninstall it cleanly later (or run `uninstall.cmd`).

### Option 2 — Portable: just run the app

1. Download **`Tempo.exe`** (and `Tempo.exe.sha256`) from the latest release.
2. Put it in any folder you like and double-click it. That's it.

Portable mode is great for a USB stick or a quick try. Two small things to know:
"**Start with Windows**" and **in-app updates** point at wherever the .exe currently
sits, so if you move or delete it, just re-enable those. Tempo tells you in Settings
when it's running portably.

> **First-run note:** Because Tempo isn't code-signed, Windows SmartScreen may say
> *"Windows protected your PC"* or *"Unknown publisher."* This is normal for small
> indie apps. Click **More info > Run anyway** — it's safe. You can verify your
> download with `certutil -hashfile Tempo.exe SHA256` and compare it to the
> `Tempo.exe.sha256` file on the release.

**Requirements:** 64-bit Windows 10 or 11. Nothing else — the .NET runtime is built
in, so you don't need to install anything.

---

## First steps & recommendations

New to Tempo? Here's the quickest path to something useful:

- **Just want to auto-click?** Open the **Clicker** tab, set your speed (CPS),
  choose the mouse button, and press **F6** to start/stop. That's the whole loop.
- **Set a comfortable speed.** Very high CPS can trip game anti-cheat and stress
  your PC. For most uses, somewhere in the low hundreds is plenty. Tempo's
  **anti-freeze** protection is on by default — leave it on unless you have a
  specific reason not to.
- **Pick a hotkey you won't hit by accident.** The Keybinds tab lets you bind
  start/stop (and more) to keyboard **or** mouse buttons. F6 is the default.
- **Save your setup as a profile** so you can switch between, say, a "fast game"
  preset and a "slow form-filling" preset in one click.
- **Record repetitive tasks** on the **Macros** tab instead of clicking manually —
  record once, replay with looping and speed control.
- **Make it yours.** Settings has 38 themes, a custom accent colour, four languages,
  and optional animated backdrops.

> **Use responsibly.** Auto-clicking may violate the terms of service of some games
> and apps. You're responsible for how you use it.

---

## What Tempo can do

- **Clicker** — interval, hold-to-click, and burst modes; left/right/middle button;
  single/double/triple clicks; click a fixed point or follow the cursor; per-click
  hold time; repeat by count or duration with live time estimates; optional
  randomization to vary the timing.
- **Multi-Point** — click a sequence of points in order, reverse, random, or
  ping-pong, each with its own button, click style, dwell time, and repeat.
- **Macros** — record and replay mouse and keyboard actions, with looping, speed
  control, pinning, notes, search, and per-macro export/import.
- **Statistics** — a live dashboard with lifetime totals, personal records, insights
  (streaks, busiest day and hour, top profile), charts, a session goal, searchable
  history, and CSV export.
- **Hotkeys** — fully rebindable global shortcuts using keyboard **and** mouse
  buttons, with live conflict detection so two actions never clash.
- **Make it yours** — 38 built-in themes, a custom accent colour, optional animated
  GIF backdrops, four languages (English, Spanish, French, German), a system-tray
  presence, and launch-at-startup.
- **Dependable** — anti-freeze protection, crash-safe saving, in-app update checks
  with a one-click installer, and built-in bug/crash reporting.

---

## Updates

Tempo can check for updates for you: **Settings > Check for updates** (and,
optionally, automatically at launch). When a new version exists, Tempo can download
and install it for you. You can turn the automatic check off under **Behaviour** —
Tempo never sends anything but that one version check.

---

## Privacy

Tempo runs **entirely on your PC**. Your clicks, macros, profiles, and statistics
never leave your computer. The only network access is the optional update check
against GitHub, which you can disable. There is no telemetry and no account.

---

## Troubleshooting

- **"Windows protected your PC" / Unknown publisher** — expected for unsigned indie
  apps. Click **More info > Run anyway**. Verify the download hash if you want to be
  sure (see the install note above).
- **A hotkey doesn't work** — another app may already use that combo. Pick a
  different one on the Keybinds tab; Tempo warns you about conflicts.
- **Clicks feel laggy or the PC struggles at high speed** — lower the CPS and keep
  anti-freeze on. Extremely high rates aren't usually necessary.
- **Something looks wrong or it crashed** — Tempo writes a report you can send in;
  see Reporting bugs below.

---

## Reporting bugs

Use **Settings > Report a bug** inside the app, or open an issue on GitHub:
<https://github.com/justcamop555-pixel/Tempo/issues>. If Tempo ever crashes, it
saves a crash report — including it helps a lot. Please describe what you did, what
you expected, and what happened, and mention your Windows version and Tempo version
(shown in Settings).

---

## Where your data is stored

Everything Tempo saves lives in your user profile, so it survives updates and never
needs admin rights:

```
%LOCALAPPDATA%\AutoClicker\
```

That folder holds your settings, profiles, macros, and statistics (and, if you use
Tempo's offline captions, the downloaded speech model). Uninstalling can optionally
remove it.

---

# For developers

The sections below are for building Tempo from source or maintaining releases.
Regular users don't need any of this.

## Building & running

**Requirements:** [.NET 8 SDK](https://dotnet.microsoft.com/download), Windows.

### Option A — Visual Studio 2022

1. Open `AutoClicker.csproj` (or the solution) in Visual Studio 2022.
2. Make sure the **.NET desktop development** workload is installed.
3. Press **F5** to build and run.

### Option B — Command line

```bat
dotnet build -c Release
dotnet run   -c Release
```

To produce a release build the way the project ships it, use the included
**`publish.cmd`** (see below) rather than calling `dotnet publish` by hand.

## Publishing a release (maintainers)

Run the release builder from the project folder:

```bat
publish.cmd                 :: win-x64, full clean build
publish.cmd win-arm64       :: a different runtime
publish.cmd /quick          :: incremental (faster; don't ship this)
publish.cmd /ci             :: plain output for scripts/automation
publish.cmd /help           :: all options
```

It builds a self-contained Tempo and produces, under `bin\publish\<rid>\`:

- `Tempo.exe` — the app (plus a few native files beside it for the optional
  offline speech engine; keep them together)
- `Tempo.exe.sha256` — checksum
- `install.cmd` / `uninstall.cmd` — the per-user installer and uninstaller
- `INSTALL-README.txt` — the tiny how-to that ships in the zip
- `bin\publish\Tempo-Setup-<version>.zip` — the bundle users download
- `bin\publish\CHECKSUMS.txt` — checksums for the exe and the setup zip

The builder verifies each step (the exe was produced, its size is sane, its checksum
is written, and its embedded version matches the project) and prints a colourful,
animated progress display with green check marks. A full log is written to
`publish-log.txt`.

### Cutting a release

1. Bump the version in `AutoClicker.csproj`, `Program.cs`, `UI/AboutForm.cs`, and add
   an entry to `CHANGELOG.md`.
2. Run `publish.cmd`.
3. Create a GitHub release tagged `v<version>` and attach **`Tempo-Setup-<version>.zip`**
   (and, optionally, `Tempo.exe` + `Tempo.exe.sha256` for portable users).
4. Paste the release notes from the generated notes file as the description.

## Project structure

```
AutoClicker/
  Program.cs              App entry point, global exception handling
  Engine/                 Click engine, precise timing, schedulers
  Models/                 Settings, profiles, statistics, enums
  Persistence/            Saving/loading settings, profiles, macros
  UI/                     MainForm (split into per-tab partials) + dialogs
  Utils/                  Hotkeys, logging, updates, helpers
  publish.cmd             Release builder
  install.cmd             Per-user installer
  uninstall.cmd           Matching uninstaller
```

## Changelog

See [CHANGELOG.md](CHANGELOG.md). Per-version release notes are in the
`release-notes/` folder.
