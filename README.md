# ⚡ Tempo

**A fast, precise auto-clicker for Windows — up to 2,000 clicks per second, multi-point patterns, a full macro recorder and statistics that remember every click. Free, no account, runs entirely on your PC.**

[![Latest release](https://img.shields.io/github/v/release/justcamop555-pixel/Tempo?label=latest&color=8b6cf2)](https://github.com/justcamop555-pixel/Tempo/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/justcamop555-pixel/Tempo/total?color=3fd9a4)](https://github.com/justcamop555-pixel/Tempo/releases)
![Platform](https://img.shields.io/badge/Windows-10%20%2F%2011%20x64-0078d4)

🌐 **Website:** https://justcamop555-pixel.github.io/Tempo/ — it always shows the newest version, file sizes and download links automatically.

---

## ✨ What's inside

| | |
|---|---|
| **Clicker** | Interval, hold-to-click and burst modes · single/double/triple clicks · repeat until stopped, a fixed count, or for a duration (with a live countdown and an optional finish chime) · up to **2,000 CPS** with sub-millisecond timing above 1,000 |
| **Positions** | Click at the cursor, a fixed point, or a **multi-point list** (sequence, reverse, ping-pong or random) · restore the cursor when stopped |
| **Human touch** | Position jitter, interval randomization and a one-click **Humanize** preset |
| **Macros** | Record mouse + keyboard, edit steps, play at 0.1–10× with loops and drift-free timing · Live Monitor highlights each step · export / import / merge |
| **Statistics** | Live dashboard, lifetime records, busiest hours/weekdays, sortable session history, milestones, CSV export — all stored locally |
| **Hotkeys** | Global keybinds for everything, working even from the tray · live conflict highlighting |
| **Safety** | Anti-Freeze protection · emergency stop · start-delay countdown · **tray sleep** (a hidden, idle Tempo pauses its hotkeys so it can't surprise you later) |
| **Yours** | 38 themes · 6 languages · custom accent colors · optional GIF backdrops · profiles for full setups |

---

## 📥 Download & install

Grab the **[latest release](https://github.com/justcamop555-pixel/Tempo/releases/latest)**. Two ways in:

**Installer (recommended)**
1. Download `Tempo-Setup-<version>.zip` and unzip it.
2. Run `install.cmd` — no administrator rights needed.
3. Launch Tempo from the Start Menu. Uninstall any time from *Settings → Apps* (or `uninstall.cmd`).

**Portable**
1. Download the single `Tempo.exe`, put it anywhere, run it. That's the whole install.

> **"Unknown publisher" warning?** That's because the app isn't code-signed — click **More info → Run anyway**. It's safe, and you can verify the download yourself:
> ```
> certutil -hashfile Tempo.exe SHA256
> ```
> Compare the output with the `Tempo.exe.sha256` file attached to the release.

**Requirements:** Windows 10 (1607+) or Windows 11, 64-bit. .NET is bundled — nothing else to install.

---

## ⌨️ Default hotkeys

| Key | Action |
|---|---|
| `F6` | Start / Stop clicking |
| `F9` | Pause / Resume |
| `F7` | Pick a position on screen |
| `F8` | Emergency stop (stops clicking, playback and recording) |

Everything is rebindable on the **Keybinds** tab — including macro record/play and per-macro hotkeys.

---

## 🛠 Build from source

The complete source lives in the **`Open-Source Build_Tempo.zip`** file at the top of this repository (the name carries the version it matches).

1. Unzip it anywhere.
2. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) if you don't have it.
3. Run `publish.cmd` — it builds a self-contained `Tempo.exe` and the `Tempo-Setup-<version>.zip` installer bundle, prints a SHA-256 checksum, and tells you exactly what to attach to a release.

Prefer plain commands? `dotnet publish -c Release` works too.

---

## 🔒 Privacy

Tempo runs entirely on your PC. Your clicks, macros, profiles and statistics **never leave your computer**. The only network use is the optional update check against GitHub — and you can turn that off in *Settings → Behaviour*. No accounts, no telemetry, no ads.

Your data lives in a plain local folder (`%LocalAppData%\AutoClicker`) that you can open, back up or delete from inside the app.

---

## 🎮 A note on games

Many online games ban auto-clickers, and at high speeds the input is obviously automated — Tempo even warns you in-app before unlocking advanced speeds. Check the rules of whatever you play; single-player and idle games are the usual home for tools like this.

---

## 🐞 Found a bug?

- **In the app:** *Settings → Report a bug…* (opens a pre-filled GitHub issue) or *Email a bug…* (your email app, Gmail, Outlook, Yahoo, or copy to clipboard). Both include your system details and the log-file path automatically.
- **Here:** [open an issue](https://github.com/justcamop555-pixel/Tempo/issues/new).

Screenshots and the log file (`%LocalAppData%\AutoClicker\autoclicker.log`) make fixes much faster.

---

<p align="center">Made by one developer, improved release by release. ⚡</p>
