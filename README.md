<p align="center">
  <img width="160" height="160" alt="Tempo logo" src="https://github.com/user-attachments/assets/471bd647-9993-40fc-8930-8beb925518d6" />
</p>

<h1 align="center">Tempo</h1>

<p align="center">
  A fast, modern, <strong>free &amp; open</strong> Windows auto-clicker — and a lot more.<br>
  Precise clicking, multi-point routes, full macro record &amp; replay, live statistics,<br>
  rebindable hotkeys, and 38 themes. Runs <strong>100% on your PC</strong> — no account, no telemetry.
</p>

<p align="center">
  <img alt="Windows 10 & 11" src="https://img.shields.io/badge/Windows-10%20%26%2011-0078D6?logo=windows&logoColor=white">
  <img alt=".NET 8 (built in)" src="https://img.shields.io/badge/.NET%208-built--in-512BD4?logo=dotnet&logoColor=white">
  <img alt="Free & Open" src="https://img.shields.io/badge/Free%20%26%20Open-%E2%9C%93-34d399">
  <img alt="No telemetry" src="https://img.shields.io/badge/Telemetry-none-7c5cff">
  <a href="https://github.com/justcamop555-pixel/Tempo/releases"><img alt="Latest release" src="https://img.shields.io/github/v/release/justcamop555-pixel/Tempo?color=7c5cff"></a>
  <a href="https://github.com/justcamop555-pixel/Tempo/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/justcamop555-pixel/Tempo/total?color=34d399"></a>
</p>

<p align="center">
  <a href="https://github.com/justcamop555-pixel/Tempo/releases"><strong>⬇ Download</strong></a>
  ·
  <a href="https://justcamop555-pixel.github.io/Tempo"><strong>Website</strong></a>
  ·
  <a href="CHANGELOG.md">Changelog</a>
  ·
  <a href="https://github.com/justcamop555-pixel/Tempo/issues">Report a bug</a>
</p>

---

## Why Tempo?

Most free auto-clickers stop at *"click here, this fast."* Tempo goes further — multi-point
routes, recordable macros, a live statistics dashboard, fully rebindable keyboard **and**
mouse hotkeys — wrapped in a clean, fast, fully themeable interface that was rebuilt from
the ground up. It's free, open, and it never phones home: everything stays on your machine.

> [!NOTE]
> **Use responsibly.** Auto-clicking may violate the terms of service of some games and
> apps, and some anti-cheat systems detect it. You're responsible for how you use it.

---

## Download &amp; install

**You don't need to build anything** — grab the latest build from the
[**Releases page**](https://github.com/justcamop555-pixel/Tempo/releases). There are two
equally good ways to run it; pick whichever you like.

### ⚡ Portable — no install
1. Download **`Tempo.exe`** (or unzip `Tempo-Setup-<version>.zip` anywhere — a USB stick is fine).
2. Double-click **`Tempo.exe`**. That's it.

Runs in place, nothing to install. Settings, profiles, macros and stats are saved in your
user **AppData** (`%LOCALAPPDATA%\AutoClicker`), so saving always works — even from a USB
stick or a read-only folder.

### 🧩 Installed — Start Menu &amp; clean uninstall
1. Download and unzip **`Tempo-Setup-<version>.zip`** (keep the files together).
2. Double-click **`install.cmd`** — no administrator rights needed.
3. Launch **Tempo** from the Start Menu.

Adds a Start-Menu shortcut and registers Tempo under **Settings › Apps** for a clean
removal later (or run `uninstall.cmd`).

> [!IMPORTANT]
> **First-run note.** Because Tempo isn't code-signed, Windows SmartScreen may say
> *"Windows protected your PC"* / *"Unknown publisher."* That's normal for small indie
> apps — click **More info › Run anyway**. Want to be certain? Verify the download with
> `certutil -hashfile Tempo.exe SHA256` and compare it to the `Tempo.exe.sha256` published
> with every release.

**Requirements:** 64-bit Windows 10 or 11. Nothing else — the .NET runtime is built in.

---

## First steps

New to Tempo? The quickest path to something useful:

1. **Auto-click in seconds.** Open the **Clicker** tab, tap a **CPS preset** (10 / 50 / 100 / 200)
   or type an exact rate, pick the mouse button, and press **F6** to start/stop.
2. **Keep it sane.** Very high CPS can trip game anti-cheat and stress your PC — the low
   hundreds is plenty for most uses. Leave **anti-freeze protection** on (it is by default).
3. **Bind a safe hotkey.** The **Keybinds** tab maps start/stop (and more) to keyboard *or*
   mouse buttons, with live conflict detection. **F6** is the default.
4. **Save setups as profiles** — switch between, say, a "fast game" and a "slow form-filling"
   preset in one click.
5. **Record repetitive tasks** on the **Macros** tab — record once, replay with looping and a
   one-tap speed preset.
6. **Make it yours** — 38 themes, a custom accent colour, six languages, and optional animated
   backdrops in **Settings**.

---

## What Tempo can do

### 🖱 Clicker
Interval, hold-to-click and burst modes · left / right / middle button · single → quadruple
clicks · fixed point, follow-cursor or multi-point · per-click hold time · repeat by count or
duration with live time estimates · timing &amp; position **randomization** to soften the pattern.
Quick **CPS presets** with the active one highlighted, a **type-exact-CPS** box, and a **Manual
Speed slider you can scroll** to dial in the rate live.

### 🎯 Multi-Point
Click a whole sequence of points — **sequential, reverse, random or ping-pong** — each with its
own button, click style, dwell time and repeat count. On-screen numbered markers and quick-capture.

### ⏺ Macros
Record and replay mouse **and** keyboard actions, with looping, a countdown, loop delay, and
**one-tap speed presets** (0.5× / 1× / 2× / 4×). Edit steps, pin favourites, add notes, search,
merge, and export / import individual macros — plus a live step monitor and play history.

### 📊 Statistics
A live dashboard with session &amp; lifetime totals, personal records, and insights (streaks,
busiest day &amp; hour, top profile). Charts by session, day and hour · a **session goal** with
quick presets · searchable history · **copy summary** · CSV export · milestone badges.

### ⌨ Hotkeys
Fully rebindable **global** shortcuts using keyboard **and** mouse buttons, with live conflict
detection so two actions never clash — and they work even when Tempo is hidden in the tray.

### 🎨 Make it yours
A modern, redesigned UI — rounded inputs, icon buttons, toggle switches and accent-tinted
section badges. **38 built-in themes** + a custom accent colour, optional animated GIF
backdrops, six languages (English, Spanish, French, German, Italian, Portuguese), a system-tray
presence, launch-at-startup, and per-tab scroll memory.

### 🛡 Dependable
Anti-freeze protection (a CPS cap **plus** a CPU-adaptive throttle that backs off if your PC
gets busy), crash-safe saving, in-app update checks with a one-click installer, and built-in
bug/crash reporting.

### ♿ Accessibility
Optional on-screen **Live Captions** for system audio (your choice), or Tempo can open
Windows 11's own — more accurate — Live Captions.

---

## Updates

Tempo can check for updates: **Settings › Check for updates** (and, optionally, automatically
at launch). When a newer version exists, Tempo can download and install it for you. Turn the
automatic check off under **Behaviour** any time — that one version check is the *only* network
request Tempo ever makes.

## Privacy

Tempo runs **entirely on your PC**. Your clicks, macros, profiles and statistics never leave
your computer. The only network access is the optional update check against GitHub, which you
can disable. **No telemetry, no account, ever.**

---

## Troubleshooting

- **"Windows protected your PC" / Unknown publisher** — expected for unsigned indie apps.
  Click **More info › Run anyway** (verify the hash if you want to be sure).
- **A hotkey doesn't work** — another app may already use that combo. Pick a different one on
  the Keybinds tab; Tempo warns you about conflicts.
- **Clicks feel laggy or the PC struggles at high speed** — lower the CPS and keep anti-freeze
  on. Extremely high rates are rarely necessary.
- **Something looks wrong or it crashed** — Tempo writes a report you can send in (see below).

## Reporting bugs

Use **Settings › Report a bug** in the app, or open an issue on
[GitHub](https://github.com/justcamop555-pixel/Tempo/issues). If Tempo crashes it saves a
crash report — attaching it helps a lot. Please describe what you did, what you expected, and
what happened, plus your Windows and Tempo versions (shown in Settings).

## Where your data is stored

Everything lives in your user profile, whether installed or portable, so it survives updates
and never needs admin rights:

```
%LOCALAPPDATA%\AutoClicker\
```

That folder holds your settings, profiles, macros and statistics (and, if you use Tempo's
offline captions, the downloaded speech model). Uninstalling an installed copy can optionally
remove its data.

---

# For developers

The rest is for building from source or cutting releases — regular users don't need any of it.

## Build &amp; run

**Requirements:** [.NET 8 SDK](https://dotnet.microsoft.com/download), Windows.

```bat
dotnet build -c Release
dotnet run   -c Release
```

Or open `AutoClicker.csproj` in **Visual Studio 2022** (with the *.NET desktop development*
workload) and press **F5**. For a release-quality build, use **`publish.cmd`** rather than
calling `dotnet publish` by hand.

## Publishing a release (maintainers)

```bat
publish.cmd                 :: win-x64, full clean build
publish.cmd win-arm64       :: a different runtime
publish.cmd /quick          :: incremental (faster; don't ship this)
publish.cmd /ci             :: plain output for scripts/automation
publish.cmd /help           :: all options
```

It builds a self-contained Tempo and produces, under `bin\publish\<rid>\`:

- `Tempo.exe` — the app, plus a `runtimes\<rid>\native\` folder for the optional offline
  speech engine (keep the folder with the exe)
- `Tempo.exe.sha256` — checksum
- `install.cmd` / `uninstall.cmd` — the per-user installer and uninstaller
- `INSTALL-README.txt` — the short how-to that ships in the zip
- `bin\publish\Tempo-Setup-<version>.zip` — the bundle users download (run `Tempo.exe` for
  portable, or `install.cmd` to install)
- `bin\publish\CHECKSUMS.txt` — checksums for the exe and the setup zip

Each step is verified (exe produced, size sane, checksum written, embedded version matches the
project) with a progress display and green check marks; a full log goes to `publish-log.txt`.

### Cutting a release
1. Bump the version in `AutoClicker.csproj`, `Program.cs`, `UI/AboutForm.cs`, and add an entry
   to `CHANGELOG.md`.
2. Run `publish.cmd`.
3. Create a GitHub release tagged `v<version>` and attach **`Tempo-Setup-<version>.zip`** (works
   for both portable and installed users). Optionally also attach the standalone `Tempo.exe`
   (+ `.sha256`) to power the in-app updater.
4. Paste the notes from the generated `release-notes/<version>.md` file as the description.

## Project structure

```
AutoClicker/
  Program.cs        App entry point, global exception handling
  Engine/           Click engine, precise timing, schedulers
  Models/           Settings, profiles, statistics, enums
  Persistence/      Saving/loading settings, profiles, macros
  UI/               MainForm (split into per-tab partials) + dialogs + theming
  Utils/            Hotkeys, logging, updates, localization, helpers
  publish.cmd       Release builder
  install.cmd       Per-user installer
  uninstall.cmd     Matching uninstaller
```

## Changelog

See [CHANGELOG.md](CHANGELOG.md). Per-version release notes are in the `release-notes/` folder.
