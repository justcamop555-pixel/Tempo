<p align="center">
  <img width="160" height="160" alt="Tempo logo" src="https://github.com/user-attachments/assets/471bd647-9993-40fc-8930-8beb925518d6" />
</p>

<h1 align="center">Tempo</h1>

<p align="center">
  A fast, modern, <strong>free &amp; open-source</strong> Windows auto-clicker — and a lot more.<br>
  Precise clicking, multi-point routes, full macro record &amp; replay with <strong>Python steps</strong>,<br>
  live statistics, rebindable hotkeys, 38 themes — and <strong>offline AI Live Captions</strong> with<br>
  speaker labels, 90+ languages and optional GPU speed. Every build <strong>verifies itself</strong><br>
  against its published release. Runs <strong>100% on your PC</strong> — no account, no telemetry.
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
mouse hotkeys, and a complete **offline Live Captions system** (subtitles for anything your
PC plays, with coloured speaker labels, in 90+ languages) — wrapped in a clean, fast, fully
themeable interface that was rebuilt from the ground up. It's free, open, and it never phones
home: everything stays on your machine.

> [!NOTE]
> **Use responsibly.** Auto-clicking may violate the terms of service of some games and
> apps, and some anti-cheat systems detect it. You're responsible for how you use it.

---

## Download &amp; install

**You don't need to build anything** — grab the latest build from the
[**Releases page**](https://github.com/justcamop555-pixel/Tempo/releases). There are two
equally good ways to run it; pick whichever you like.

### ⚡ Portable — no install
1. Download **`Tempo.exe`** (or unzip `<version>.zip` anywhere — a USB stick is fine).
2. Double-click **`Tempo.exe`**. That's it.

Runs in place, nothing to install. Settings, profiles, macros and stats are saved in your
user **AppData** (`%LOCALAPPDATA%\AutoClicker`), so saving always works — even from a USB
stick or a read-only folder.

### 🧩 Installed — Start Menu &amp; clean uninstall
1. Download and unzip **`<version>.zip`** (keep the files together).
2. Double-click **`install.cmd`** — no administrator rights needed.
3. Launch **Tempo** from the Start Menu.

Adds a Start-Menu shortcut and registers Tempo under **Settings › Apps** for a clean
removal later (or run `uninstall.cmd`).

> [!IMPORTANT]
> **First-run note.** Because Tempo isn't code-signed, Windows SmartScreen may say
> *"Windows protected your PC"* / *"Unknown publisher."* That's normal for small indie
> apps — click **More info › Run anyway**.
>
> **Want to be certain it's the real thing?** You don't have to take our word for it, and
> you don't have to check by hand: Tempo verifies its own program file against the release
> published on GitHub every time it starts, and says so in **Settings › Data &amp; Backup**.
> To check yourself, run `certutil -hashfile Tempo.exe SHA256` and compare it with the
> SHA-256 GitHub shows for the asset on the
> [release](https://github.com/justcamop555-pixel/Tempo/releases).

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
6. **Turn on Live Captions** — Settings › Live Captions, download a speech model with one
   click, and anything your PC plays gets subtitles. Try the GPU engine if you have a
   graphics card.
7. **Make it yours** — 38 themes (or Match Windows light/dark), a custom accent colour, six
   languages, and optional animated backdrops in **Settings**.

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
A **Fix…** button finds and repairs the things that quietly break a recording: inputs that are
never released, clicks that land off-screen, duplicate moves, empty waits, machine-perfect
timing and aim, and marathon pauses.

**Run a Python script as a macro step.** Point a step at a `.py` file and Tempo runs it
mid-macro — with a timeout you set, a choice of what happens if it fails, and the interpreter
it found shown up front. Held keys and buttons are released before the script starts, so a
script can never inherit a stuck input, and the whole process tree is cleaned up when the macro
stops.

### 📊 Statistics
A live dashboard with session &amp; lifetime totals, personal records, and insights (streaks,
busiest day &amp; hour, top profile). Charts by session, day and hour · a **session goal** with
quick presets · searchable history · **copy summary** · CSV export · milestone badges.

### ⌨ Hotkeys
Fully rebindable **global** shortcuts using keyboard **and** mouse buttons, with live conflict
detection so two actions never clash — and they work even when Tempo is hidden in the tray.

### 🎨 Make it yours
A modern, redesigned UI — rounded inputs, icon buttons, toggle switches and accent-tinted
section badges. **38 built-in themes** (or **Match Windows** to follow the system light/dark
setting live) + a custom accent colour, optional animated GIF backdrops, six languages
(English, Spanish, French, German, Italian, Portuguese), a themed system-tray menu,
launch-at-startup, and per-tab scroll memory.

### 🛡 Dependable
Anti-freeze protection (a CPS cap **plus** a CPU-adaptive throttle that backs off if your PC
gets busy), crash-safe saving that keeps the previous copy so an interrupted write costs one
save rather than everything, in-app update checks with a one-click installer, error
notifications so problems never hide in a log, and a **Live debug** window (health check +
live stats + colour-coded event stream, one colour per subsystem) for when you want to see
exactly what Tempo is doing.

### 🔐 Tamper check
Tempo hashes its own program file at every launch and compares it with the SHA-256 GitHub
publishes for that release — so it can tell you, in **Settings › Data &amp; Backup**, whether the
copy you are running is the one that was published. It catches a patched or repackaged build,
a copy that came from somewhere other than the releases page, and a file damaged by a crash or
a failed update. Being offline is never treated as evidence of anything, and a build you made
yourself can be marked trusted in one click.

Honest about its limits: this is tamper-**evident**, not tamper-proof. Someone who can rewrite
Tempo.exe can rewrite the check out of it too. What it reliably catches is everything that
doesn't bother to.

### 💬 Live Captions (accessibility)
Real-time subtitles for **anything your PC plays** — videos, games, calls, any app or site —
generated **fully offline** by a built-in AI speech engine (Whisper). Nothing ever leaves
your machine.

- **Five speech models**, from Tiny (75 MB, instant) to **Large Turbo** (best accuracy,
  auto-detects **90+ languages**) and a compact Large for mid-range PCs — one-click download.
- **Optional GPU engine** (Vulkan — works on NVIDIA, AMD and Intel) for real-time captions on
  the biggest models, with an automatic CPU fallback and a self-healing guard.
- **Speaker labels in colour** — "Speaker 1 / Speaker 2" detected by voice (pitch/brightness
  fingerprints) plus optional on-device **AI face &amp; mouth analysis**, so you can follow who
  says what at a glance.
- **Knows where the sound comes from** — an optional "♪ YouTube ·" tag names the app or site
  playing; pick exactly **which speaker or microphone** to listen through when you have
  several (listed by model name).
- **Game-friendly** — a fullscreen game automatically switches captions into a low-impact
  mode that protects your frame rate, and restores full quality when you're done.
- Mishear auto-correction, a music/sound note when nothing is spoken, caption history,
  optional timestamped transcripts, movable and fully styleable caption bar — or mirror
  **Windows 11's own Live Captions** through Tempo's bar instead.

### 🖱🖱 Second Cursor (experimental)
A second, real-looking Windows pointer that Tempo draws and controls — place it anywhere across
**both monitors** and spam-click there, so you can AFK-grind a windowed game on monitor 2 while
your real mouse stays free on monitor 1. Optionally **drive it with a second real mouse**: pick
the mouse from a list (by product name), and left/right-click, middle-click (spam toggle), wheel,
and press-and-hold/drag all work at the second cursor while your main pointer stays pinned in
place. A per-mouse Live-debug readout shows both mice being read independently.

> [!NOTE]
> **This is genuinely limited by Windows.** There is only ONE hardware cursor, so a fully
> independent second pointer is a kernel-driver feature no user-space app can do. It works best
> on **windowed / visible-cursor apps and games**; games that lock or hide the cursor for
> mouse-look (most first-person shooters) can't be split, and a raw-input game reads your main
> mouse too. Off by default — treat it as experimental.

### 🕹 Camera-relative movement (advanced)
An experimental input mode for games: Tempo intercepts W/A/S/D and re-aims them relative to
the in-game camera, with a calibration wizard, smoothing, anti-jitter and a deadzone. Many
online games forbid input automation — use with care.

---

## Updates

Tempo can check for updates: **Settings › Check for updates** (and, optionally, automatically
at launch). When a newer version exists, Tempo can download and install it for you. Turn the
automatic check off under **Behaviour** any time.

## Privacy

Tempo runs **entirely on your PC**. Your clicks, macros, profiles, statistics and captions
never leave your computer. **No telemetry, no account, ever** — nothing is uploaded, and
nothing is collected.

Tempo does reach the network in a few places, all of them optional and all of them things you
started. The complete list:

| When | What it contacts | Sends |
| --- | --- | --- |
| Update check | GitHub releases API | nothing but the request itself |
| Downloading an update | GitHub release asset | — |
| Tamper check, once per version | GitHub releases API | nothing but the request itself |
| Downloading a speech model | Hugging Face | — |
| A custom logo from a URL | whatever URL you enter | — |

None of them carry your data, and every one can be avoided: turn off the update check and the
tamper check in **Settings**, and simply don't download a model or set a logo URL.

**Bug reports are yours to review.** Nothing is sent until you pick how to send it, and Tempo
shows you the entire report first so you can edit or delete any of it. Your Windows account
name, PC name and personal folder paths are stripped automatically — from the subject line as
well as the body. The recent activity log is offered as a tick-box, never attached silently,
because it can mention files you have opened.

---

## Troubleshooting

- **"Windows protected your PC" / Unknown publisher** — expected for unsigned indie apps.
  Click **More info › Run anyway** (verify the hash if you want to be sure).
- **A hotkey doesn't work** — another app may already use that combo. Pick a different one on
  the Keybinds tab; Tempo warns you about conflicts.
- **Clicks feel laggy or the PC struggles at high speed** — lower the CPS and keep anti-freeze
  on. Extremely high rates are rarely necessary.
- **Captions lag or fall behind** — pick a smaller model, or turn on the GPU engine
  (Settings › Live Captions). Tempo also steps down automatically if a model can't keep up.
- **Want to see what Tempo is doing right now?** — **Settings › Data &amp; Backup › Live
  debug** shows a health check, live engine stats and a colour-coded event stream; **Copy**
  gives you the perfect text to paste into a bug report.
- **"Tempo has been modified" / "This version was never published"** — the tamper check found
  that `Tempo.exe` isn't the file published for its version number. If you built it yourself,
  click **Trust this copy** in Settings › Data &amp; Backup. If you downloaded it, get it again
  from the [releases page](https://github.com/justcamop555-pixel/Tempo/releases).
- **Something looks wrong or it crashed** — Tempo writes a report you can send in (see below).

## Reporting bugs

Use **Settings › Report a bug…** or **Email a bug…** in the app, or open an issue on
[GitHub](https://github.com/justcamop555-pixel/Tempo/issues). Either button opens a composer
that shows you the whole report — pre-filled with your Windows version, hardware, GPU, install
type and this session's warnings — which you can edit before choosing GitHub, your email app,
Gmail, Outlook, Yahoo, or the clipboard. Identifying details are removed for you.

Please describe what you did, what you expected, and what happened. If Tempo crashed it also
saves a crash report; attaching it helps a lot. **Settings › Data &amp; Backup › Live debug**
→ **Copy** gives you an even fuller picture to paste in.

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

The full source lives on this branch — clone it and it builds with one command.

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

- `Tempo.exe` — one fully self-contained file (the .NET runtime, UI-Automation assemblies and
  the native offline-speech libraries are all bundled and self-extract at run time, so the exe
  works anywhere on its own)
- `Tempo.exe.sha256` — checksum
- `install.cmd` / `uninstall.cmd` — the per-user installer and uninstaller
- `INSTALL-README.txt` — the short how-to that ships in the zip
- `bin\publish\<version>.zip` — the bundle users download (run `Tempo.exe` for portable, or
  `install.cmd` to install)
- `bin\publish\CHECKSUMS.txt` — checksums for the exe and the zip

Each step is verified (exe produced, size sane, checksum written, embedded version matches the
project) with a progress display and green check marks; a full log goes to `publish-log.txt`.

### Cutting a release
1. Bump `<Version>`, `<AssemblyVersion>` and `<FileVersion>` in `AutoClicker.csproj`, and the
   startup log line in `Program.cs`. (About reads the version from the assembly, so it needs no
   edit.) Add an entry to `CHANGELOG.md` and a `release-notes/<version>.md`.
2. Run `publish.cmd`.
3. Create a GitHub release tagged **`v<version>`** and attach both **`<version>.zip`** (for new
   users, portable or installed) and the standalone **`Tempo.exe`** (what the in-app updater
   downloads).
4. Paste `release-notes/<version>.md` as the description.

> [!IMPORTANT]
> Attach the **exact** `Tempo.exe` that `publish.cmd` produced. Tempo's tamper check compares
> the running file against the SHA-256 GitHub publishes for the release, so an exe rebuilt
> after the fact would make every user's copy report as modified. The build is deterministic —
> the same source rebuilds to a byte-identical exe — so re-running `publish.cmd` is safe;
> building from *different* source is not.

## Project structure

```
AutoClicker/
  Program.cs        App entry point, single-instance, global exception handling
  Engine/           Click engine, precise timing, schedulers, macro player/recorder
  Models/           Settings, profiles, statistics, hotkeys, enums
  Native/           Low-level keyboard/mouse hooks, raw input, second-mouse listener
  Persistence/      Crash-safe saving of settings, profiles, macros, history
  UI/               MainForm (per-tab partials) + dialogs + theming + notifications
  Utils/            Logging, updates, integrity, localization, speech engine, helpers
  Assets/           Icon and About artwork (embedded at build time)
  release-notes/    One file per version, pasted into the GitHub release
  publish.cmd       Release builder (see above)
  install.cmd       Per-user installer
  uninstall.cmd     Matching uninstaller
```

> [!NOTE]
> `.gitattributes` sets `* -text` on purpose. `publish.cmd` requires CRLF endings and is
> mis-parsed by `cmd.exe` without them, while `install.cmd`, `uninstall.cmd` and
> `INSTALL-README.txt` ship inside the release zip and must stay byte-identical to what was
> published. Git's Windows default would rewrite all of it on checkout.

## Changelog

See [CHANGELOG.md](CHANGELOG.md). Per-version release notes are in the `release-notes/` folder.
