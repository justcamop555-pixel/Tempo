<p align="center">
  <img width="160" height="160" alt="Tempo logo" src="https://github.com/user-attachments/assets/471bd647-9993-40fc-8930-8beb925518d6" />
</p>

<h1 align="center">Tempo</h1>

<p align="center">
  <strong>A precision auto-clicker for Windows — and a lot more.</strong><br>
  Clicking, multi-point routes, macros with <strong>Python steps</strong>, live statistics,<br>
  <strong>offline AI Live Captions</strong> that tell speakers apart, and a <strong>Roblox account manager</strong>,<br>
  in one free, source-available app that runs <strong>entirely on your PC</strong>.<br>
  No account. No telemetry. Every release checks itself against GitHub.
</p>

<p align="center">
  <img alt="Windows 10 &amp; 11" src="https://img.shields.io/badge/Windows-10%20%26%2011-0078D6?logo=windows&logoColor=white">
  <img alt=".NET 8 (built in)" src="https://img.shields.io/badge/.NET%208-built--in-512BD4?logo=dotnet&logoColor=white">
  <img alt="Free" src="https://img.shields.io/badge/Free-source--available-34d399">
  <img alt="No telemetry" src="https://img.shields.io/badge/Telemetry-none-7c5cff">
  <a href="https://github.com/justcamop555-pixel/Tempo/releases"><img alt="Latest release" src="https://img.shields.io/github/v/release/justcamop555-pixel/Tempo?color=7c5cff"></a>
  <a href="https://github.com/justcamop555-pixel/Tempo/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/justcamop555-pixel/Tempo/total?color=34d399"></a>
</p>

<p align="center">
  <a href="https://github.com/justcamop555-pixel/Tempo/releases/latest"><strong>⬇ Download</strong></a>
  ·
  <a href="https://justcamop555-pixel.github.io/Tempo"><strong>Website</strong></a>
  ·
  <a href="https://github.com/justcamop555-pixel/Tempo/releases/latest">What's new</a>
  ·
  <a href="CHANGELOG.md">Changelog</a>
  ·
  <a href="https://github.com/justcamop555-pixel/Tempo/issues">Report a bug</a>
</p>

---

> [!NOTE]
> **Use responsibly.** Auto-clicking may break the terms of service of some games and apps,
> and some anti-cheat systems detect it. You are responsible for how you use Tempo.

## What's new in 1.0.326

- **Roblox sign-in saves itself.** Log in or sign up in the private browser window, and Tempo saves
  the account and closes the browser. There is no extra window and no confirm screen. An account
  you already have is refreshed, never added twice, and the password you type is saved on sign-up
  too.
- **Pick a Roblox version:** Automatic, or any Roblox client already on your PC (Roblox,
  Bloxstrap, Fishstrap, Voidstrap). Nothing is downloaded, and only copies signed by Roblox are
  started.
- **Tempo's own scroll bars.** Slim, themed scroll bars with up and down arrows replace Windows
  11's everywhere, from the pages to the drop-down lists. Scrolling itself works exactly as before.
- **Live Captions change language while they run,** with no restart. Speaker names survive the
  face switch, and a long session's saved transcript starts at the very first line.
- **A Dim slider that keeps up:** the background picture dims as you drag, without reloading.
- **Live debug shows more:** new lines below where you're reading are counted, and an open
  Roblox sign-in shows how long it has waited.
- **Steadier pages** after tab switches, restores and resizes on 125% laptops.

The full notes are on the [release page](https://github.com/justcamop555-pixel/Tempo/releases/latest),
and every change is in [CHANGELOG.md](CHANGELOG.md).

---

## Download & install

**You don't need to build anything.** The quickest way is one line in PowerShell — the ordinary
one, not "as administrator":

```powershell
irm https://justcamop555-pixel.github.io/Tempo/install.ps1 | iex
```

That fetches the current release, **checks its SHA-256 against the one GitHub published for it**
(and stops if they differ), installs Tempo for your user only, and adds a Start-Menu entry you can
uninstall from **Settings › Apps**. The script is plain text if you'd rather read it first:
[install.ps1](https://justcamop555-pixel.github.io/Tempo/install.ps1). Add `-Portable` to just
download the program instead (saved as `TempoClicker.exe` in your Downloads folder).

Prefer to do it by hand? Everything is on the
[**Releases page**](https://github.com/justcamop555-pixel/Tempo/releases). Pick either way:

### ⚡ Portable — no install
1. Download **`Tempo.exe`** (or unzip `<version>.zip` anywhere — a USB stick is fine).
2. Double-click it. That's it.

It runs in place. Your settings, profiles, macros and statistics are kept in
`%LOCALAPPDATA%\AutoClicker`, so saving works even from a USB stick or a read-only folder. The
first time, Tempo offers once to install itself on this PC; say no and it won't ask again
(**Settings** keeps an **Install Tempo on this PC…** button for later).

### 🧩 Installed — Start Menu & clean uninstall
1. Download and unzip **`<version>.zip`** (keep the files together).
2. Double-click **`install.cmd`** — no administrator rights needed.
3. Start **Tempo** from the Start Menu.

Tempo is installed in `%LOCALAPPDATA%\Programs\TempoClicker` and listed in **Settings › Apps**.
Uninstalling keeps your settings unless you ask for them to be deleted.

> [!TIP]
> The installed program is called **`TempoClicker.exe`**, while the download stays `Tempo.exe`.
> Discord's game detection takes any program named `Tempo.exe` for the Steam game "Tempo", and
> the new name stops that. An existing install renames itself on its first start.

> [!IMPORTANT]
> **First run.** Tempo isn't code-signed, so Windows SmartScreen may say *"Windows protected your
> PC"* / *"Unknown publisher."* That is normal for small free apps: click **More info › Run
> anyway**.
>
> **Want to know it's the real thing?** You don't have to take anyone's word for it. Tempo checks
> its own program file every time it starts, and asks GitHub once per release whether it is the
> file published there. Once GitHub has confirmed your copy, Tempo shows its **Official ID** at
> the bottom of the sidebar and in **About** — four groups of four characters, like
> `CC21-FB7A-D6F3-8831`. Those are the first 16 characters of the SHA-256 that GitHub lists
> beside `Tempo.exe` on the [release page](https://github.com/justcamop555-pixel/Tempo/releases),
> so anyone can match it there. Every release has its own ID; a test build or a modified copy has
> none. To check the download yourself, run `certutil -hashfile Tempo.exe SHA256` and compare it
> with that same line — the `Tempo.exe` one, not the zip's.

**Requirements:** 64-bit Windows 10 or 11. Nothing else — the .NET runtime is built in. Tempo
never needs administrator rights, for any feature.

---

## First steps

1. **Click in seconds.** On the **Clicker** tab, tap a **CPS preset** (10 / 50 / 100 / 200) or type
   an exact rate, pick the mouse button, and press **F6** to start and stop.
2. **Keep it sane.** Very high rates can trip anti-cheat and load your PC; the low hundreds is
   plenty for most uses. Leave **anti-freeze protection** on (it is by default).
3. **Bind your keys.** The **Keybinds** tab puts any action on a keyboard key or a mouse button.
   Hotkeys work while Tempo is hidden in the tray, and while you hold Shift or Ctrl in a game.
4. **Save setups as profiles** and switch between them in one click — say, a fast one for a game
   and a slow one for filling in forms.
5. **Record repetitive work** on the **Macros** tab: record once, replay with looping and a
   one-tap speed preset.
6. **Turn on Live Captions** under **Settings › Live Captions**, download a speech model with one
   click, and anything your PC plays gets subtitles. With a graphics card, try the GPU engine.
7. **Make it yours:** 38 themes (or **Match Windows**), your own accent colour, six languages and
   optional animated backdrops, all in **Settings**.

---

## What Tempo can do

### 🖱 Clicker
Interval, hold-to-click and burst modes · left, right or middle button · single to quadruple
clicks · a fixed point, follow-the-cursor or multi-point · hold time per click · repeat by count
or by time, with a live estimate · timing and position **randomisation** to soften the pattern.
Quick **CPS presets** with the active one highlighted, a box for an **exact CPS**, and a speed
slider you can scroll to dial in the rate live.

**Randomisation** varies each gap by up to the jitter you set without changing your average speed,
and spreads clicks in a circle around the spot you chose without leaving its window. **Humanize**
sets it all up for the current speed in one click, and changes apply while a run is going.

The **CPS test** measures you: your clicks in each second, your best second, how even your rhythm
was, the gaps between clicks and whether you kept your speed, with a best for each button and
length. Start the clicker with the pointer on its pad and it measures Tempo instead, against the
speed you set.

### 🎯 Multi-Point
Click a whole route — **sequential, reverse, random or ping-pong** — with each point's own
button, click style, dwell time and repeat count. Numbered markers show the points on screen, and
a hotkey adds a point wherever your cursor is.

### 🗂 Profiles
Save the whole setup as a profile and switch from the Profiles tab, the dropdown, the tray menu or
a hotkey. Switching from the window asks what to do with unsaved changes (save, drop or stay);
switching from a hotkey or the tray keeps them, so no question pops up over your game.

### ⏺ Macros
Record and replay the mouse **and** keyboard, with looping, a countdown, a loop delay and
**one-tap speed presets** (0.5× / 1× / 2× / 4×). Recording catches **camera turns in games** and
the **mouse side buttons**, and leaves out clicks on Tempo's own controls. Edit steps, pin
favourites, add notes, search, merge, and export or import macros, with a live step monitor and
play history. **Fix…** finds and repairs what quietly breaks a recording: keys that are never
released, clicks that land off-screen, duplicate moves, empty waits, machine-perfect timing, and
marathon pauses. Up to three macros can sit on their own **Play macro #1–#3** keys.

**Run a Python script as a step.** Point a step at a `.py` file and Tempo runs it mid-macro, with
a timeout you set and a choice of what happens if it fails. **Test run** tries the script before it
is part of a macro, and a failure says why. A `.venv` beside the script wins over system Python.
Held keys and buttons are released before the script starts, and the whole process tree is cleaned
up when the macro stops. Importing a macro that runs a script always asks first.

### ⌨ Hotkeys & keybinds
Every action can go on a keyboard key or a mouse button, side buttons included: start/stop, pause,
pick a position, emergency stop, next/previous profile, faster/slower, record/play/stop a macro,
show/hide the window, Live Captions, the second cursor and more.

- **Global:** they work while Tempo is minimised or hidden in the tray.
- **While you hold Shift or Ctrl:** function keys, mouse buttons, media keys, Pause, Scroll Lock
  and the Hold-to-click key still fire, so F6 works while you sprint or crouch. Keys that type
  (letters, digits, arrows, Enter) still need their exact combination. On by default, with a tick
  box on the Keybinds tab.
- **No clashes:** live conflict detection, and warnings for combinations that would break other
  programs (Ctrl + C, keys that type a character with AltGr, Tempo's own window shortcuts). A side
  button bound to Tempo no longer also sends your browser **Back**.
- **Only Tempo's keys during a run:** while the clicker runs or a macro plays or records, the
  Windows key, the Win shortcuts, Ctrl+Esc and the Sticky Keys pop-up are held back, so a stray
  press can't open Start over your game. Alt+Tab and Ctrl+Shift+Esc always work. On by default.

Defaults: **F6** start/stop · **F9** pause/resume · **F7** pick position · **F8** emergency stop.
In the Tempo window, **Esc** also stops a run (you can turn that off) and **F11** goes full screen.

### 💬 Live Captions (accessibility)
Real-time subtitles for **anything your PC plays** — videos, games, calls, any app or site — made
**fully offline** by a built-in speech engine (OpenAI's Whisper, through whisper.cpp). Audio never
leaves your PC.

- **14 speech models** in six sizes, from Tiny (75 MB, instant) to Large v3, in English-only,
  any-language (90+ languages, detected automatically, or one you pick, even while captions run)
  and compact versions — one-click download.
- **Optional GPU engine** (Vulkan: NVIDIA, AMD and Intel), with an automatic CPU fallback, and
  Tempo steps down to a smaller model by itself if one can't keep up.
- **Only speech becomes captions.** A voice-detection network (Silero VAD) runs before Whisper, so
  music, fans and game sound don't turn into invented words. **Caption song lyrics** lets music
  through.
- **Who said what.** Speakers are told apart by voice and, with on-device face analysis, matched to
  the faces in the video. The **Speakers** card on the Captions tab lists each one in their
  caption colour, with their face once it is linked. Click one to give them a name.
- **Knows where the sound comes from:** an optional "♪ YouTube ·" tag names the app or site
  playing, and you can pick **which speaker or microphone** to listen to.
- **Game-friendly:** a fullscreen game switches captions to a low-impact mode that protects your
  frame rate, and full quality returns when you're done.
- Captions read as sentences, a searchable caption history, transcripts you can **Save** or
  **Copy**, **subtitle files** (SubRip or WebVTT), and a movable, fully styleable caption bar — or
  mirror **Windows 11's own Live Captions** through Tempo's bar instead.

### 🔔 Notifications
Tempo's messages appear as its own cards in the theme, in the corner you choose, and wait while a
fullscreen game has the screen. Optionally (**Settings › Notifications**, off until you turn it on),
Tempo shows **Windows' own notifications** as its cards too, with their pictures, the website they
came from and their buttons. Clicking one does what clicking Windows' pop-up does: it opens the page
or the conversation it's about. Reminders stay until you dismiss them, any app or website can be
muted, and the notification history keeps every message in full.

### 📊 Statistics
A live dashboard with session and lifetime totals, personal records and insights (streaks, busiest
day and hour, top profile). Charts by session, day and hour · a **session goal** with quick
presets · searchable history · **copy summary** · CSV export · milestone badges.

### 👤 Accounts — a Roblox account manager
**Off until you turn it on.** Keep several Roblox accounts, launch any of them straight into a game
by **Place ID** or a private server, **follow** a player into whatever they're playing, and switch
accounts without signing in and out. It can sit on its own tab or **open in a window of its own**,
beside your game.

- **You never type a Roblox password into Tempo.** Signing in opens **your own browser** in a
  private window, and you sign in to Roblox as you always do. Chrome is used by preference, then
  Edge, Brave, Opera, Opera GX, Vivaldi, or any other Chromium browser on your PC. Firefox can't be
  driven for this. When you're done, Tempo saves the account by itself and closes the browser, with
  no window of its own and no confirm screen. An account you already have is refreshed, never added
  twice. Sign in with a **passkey or "quick log in"** and Tempo saves only the username.
- **Pick which Roblox launches:** **Automatic** (what Roblox's own links open), or any Roblox client
  already on your PC, in Roblox's, Bloxstrap's, Fishstrap's or Voidstrap's folders. Nothing is
  downloaded, and only copies signed by Roblox are started.
- **An encrypted vault.** Sessions and any saved passwords are sealed with **AES-256-GCM** under a
  key stretched from a master password you choose (PBKDF2-SHA256, 1.2 million iterations), and the
  file is sealed again by Windows to your account with **DPAPI**. It can lock itself again the
  moment you switch tabs.
- **A dot per account says whether its session still works** — green (good), red (expired), amber
  (not checked yet) — re-checked in the background. If a launch fails because a session expired,
  Tempo offers to sign that account in again there and then.
- **Type a Place ID and the game introduces itself:** name, creator, icon, players right now, likes,
  visits and server size, so a mistyped ID is obvious before you launch.
- **Several clients at once, without administrator.** Roblox allows one client per PC by holding a
  single lock; Tempo asks Windows to close that lock so more can start. It never closes a client
  that is already running, and it does not modify or inject into Roblox.
- **Knows your Roblox client:** the check looks at everything the game loads and recognises
  Bloxstrap, Fishstrap and Voidstrap.
- **An optional Local API** lets your own scripts launch accounts. It is off by default, listens only
  on this PC, needs a token, runs only while the vault is unlocked, and has no request that returns
  a cookie or a password.

> [!WARNING]
> **Running several Roblox clients at once is against Roblox's rules and can get accounts
> banned.** Tempo asks before enabling it, every time, and says exactly that in the question. Your
> accounts are your responsibility.

### 🎨 Make it yours
**38 built-in themes** (or **Match Windows** to follow the system's light/dark setting live), your
own accent colour, optional picture or animated GIF backdrops with a Dim slider, Tempo's own slim
scroll bars and six languages (English, Spanish, French, German, Italian, Portuguese). A themed tray menu, start with Windows (and, if you like, open the
window at sign-in), per-tab scroll memory, and windows that fit laptops at 125% and 150% scaling.
Colours stay right with HDR on.

### 🛡 Dependable
**Anti-freeze protection** — a CPS cap **plus** a watch on the app you're clicking. Tempo slows down
while that app is slow to answer, and holds the clicks while it's frozen. While a run is on, Tempo
asks Windows for full speed, even on battery. **Crash-safe saving** keeps the previous copy, so an interrupted write costs one save rather
than everything. Errors arrive as notifications instead of hiding in a log, and a **notification
history** keeps every message — including the ones held back while a fullscreen game had the
screen, which is exactly when you were most likely to miss one.

**Survives a restart.** Tempo never answers a Windows shutdown with a dialog, and it writes your
window position, tab, settings and click totals the moment Windows says the session is ending.

**Live debug** (Settings › Data & Backup) shows a health check, live engine stats and a colour-coded
event stream, with a real search (`"a phrase"`, `-hide`, `tag:captions`, `level:warn`). New lines
that arrive below where you're reading are counted, a click away. It also says whether Windows will
*really* start Tempo at sign-in, and whether a Roblox sign-in is open. **Copy** gives you the right
text to paste into a bug report.

### 🔐 Tamper check
Tempo hashes its own program file at every launch, and once per release compares it with the
SHA-256 that GitHub publishes for that release. **Settings › Data & Backup** then tells you whether
the copy you're running is the one that was published, and a confirmed copy shows its
**Official ID** — the first 16 characters of that SHA-256 — in the sidebar and in About, so anyone
can check the answer on GitHub instead of taking Tempo's word for it. **Details…** shows what the
check looked at — the file, its full SHA-256, its signature, the site the browser says it came
from — and the result of each of its six checks.

It catches a patched or repackaged build, a copy that came from somewhere other than the releases
page, a file damaged by a crash or a failed update, and changed files among the ones Tempo unpacks
to run. It also warns when program files that aren't Tempo's have been loaded into it from its
folder. Being offline is never treated as evidence of anything, and a build you made yourself can
be marked trusted in one click.

Honest about its limits: this is tamper-**evident**, not tamper-proof. Someone who can rewrite
Tempo's program file can rewrite the check out of it too. What it reliably catches is everything
that doesn't bother to.

### 🖱🖱 Second Cursor (experimental)
A second, real-looking pointer that Tempo draws and controls. Place it anywhere across **both
monitors** and spam-click there, so you can leave a windowed game grinding on monitor 2 while your
real mouse stays free on monitor 1. You can also **drive it with a second real mouse**: pick it from
a list by product name, and left, right and middle clicks, the wheel and press-and-drag all happen
at the second cursor while your main pointer stays where it is.

> [!NOTE]
> **Windows limits this.** There is only one hardware cursor, so a fully independent second pointer
> needs a kernel driver, which no ordinary app can provide. It works best with **windowed apps and
> games that show the cursor**; games that lock or hide the cursor for mouse-look (most
> first-person shooters) can't be split, and a raw-input game reads your main mouse too. Off by
> default — treat it as experimental.

### 🕹 Camera-relative movement (advanced)
An experimental input mode for games: Tempo takes over W/A/S/D and re-aims them relative to the
in-game camera, with a calibration wizard you can check (Tempo turns one full circle itself),
smoothing, anti-jitter, optional fine steering between the 8 key directions, and a deadzone. It
counts only the mouse movement that turns the camera (in Roblox: right button held, shift lock,
first person) and takes the keys by position, so Z/Q/S/D work on a French keyboard. Many online
games forbid input automation — use it with care.

---

## Updates

Tempo checks GitHub for a newer release when it starts and again while it stays open; you choose
how often, or turn it off, under **Settings › Behaviour › Check for updates**. When there is one,
Tempo can download and install it for you. The download picks up where it stopped after a dropped
connection, is checked against the SHA-256 GitHub publishes for it, and a failed update is reported
rather than leaving Tempo closed. The **Check for updates** button at the foot of Settings, or the
same item in the tray menu, asks straight away.

Tempo can also tell you when **Roblox** has released a new client, with **Update now**, **Later**
or **Keep this version**. Roblox updates itself and turns old clients away after a while, so
keeping a version means no reminders until the next release. Switch it off with **Settings ›
Behaviour › Check for Roblox updates**.

## Privacy

Tempo runs **entirely on your PC**. Your clicks, macros, profiles, statistics and captions never
leave it. **No telemetry and no account, ever** — nothing is uploaded and nothing is collected.

A few features do go online. The panel at the foot of **Settings** lists them, and this is the same
list:

| | What contacts what | When | Sends |
| --- | --- | --- | --- |
| **Update check** | GitHub releases API | Automatically, at start and while Tempo is open. Off: **Settings › Behaviour › Check for updates** | nothing but the request itself |
| **Release check** | GitHub releases API | Automatically, once per release (test builds never ask). Off: **Settings › Data & Backup › Check that Tempo hasn't been tampered with** | nothing but the request itself |
| **Roblox update check** | Roblox's client-version address (the one Roblox's own client asks) and its public deploy log | Automatically, every two hours, only while Roblox is installed. Off: **Settings › Behaviour › Check for Roblox updates** | nothing but the request itself |
| Downloading an update | GitHub release asset | When you choose to update | — |
| Speech models | Hugging Face | When you download a model | — |
| A logo from a URL | the address you enter | When you set it | — |
| **Accounts:** signing in | your own browser → roblox.com | When you add or re-sign an account | you sign in to Roblox yourself; Tempo never sees the password |
| **Accounts:** launching, checking a session | roblox.com | When you launch, or while the tab checks its dots | that account's own Roblox session, as signing in does |
| **Accounts:** a game from a Place ID | roblox.com | When you type one | nothing but the Place ID |
| **Local API** | nothing — it only listens on this PC (127.0.0.1 and ::1) | Only if you turn it on | — |

The **Accounts** rows only ever happen if you turn the Accounts tab on and add an account. With it
off, the only thing Tempo asks Roblox is the update check above, and that can be switched off too.
Account sessions are stored encrypted on your PC and are sent to nobody but Roblox itself. None of these rows carry your data anywhere else, and every one can be avoided.

**Bug reports are yours to review.** Nothing is sent until you choose how to send it, and Tempo shows
you the whole report first so you can edit or delete any of it. Your Windows account name, PC name
and personal folder paths are removed automatically, from the subject line as well as the body. The
recent activity log is offered as a tick box, never attached silently, because it can mention files
you have opened.

---

## Troubleshooting

- **"Windows protected your PC" / Unknown publisher** — expected for an unsigned app. Click **More
  info › Run anyway**, and check the Official ID if you want to be sure.
- **A hotkey doesn't work** — another program may already use that combination. Pick a different one
  on the Keybinds tab; Tempo warns you about conflicts. If it only fails while you hold Shift or
  Ctrl, check that **Hotkeys also work while you hold Shift or Ctrl** is ticked there.
- **Discord says you're playing "Tempo"** — install Tempo (**Install Tempo on this PC…** in
  Settings), which runs as `TempoClicker.exe`, or rename your portable `Tempo.exe`.
- **Clicks feel laggy or the PC struggles at high speed** — lower the CPS and keep anti-freeze on.
  Extremely high rates are rarely needed.
- **Captions lag or fall behind** — pick a smaller model, or turn on the GPU engine
  (Settings › Live Captions). Tempo also steps down by itself if a model can't keep up.
- **Captions stay empty over a song** — that is the voice detection doing its job. Tick **Caption
  song lyrics** in Settings › Live Captions to let music through.
- **Tempo doesn't start with Windows** — open **Live debug**. It says whether Windows will really
  start Tempo and names what can silently override the setting: a **Task Manager › Startup apps**
  switch, or an entry pointing at a copy of Tempo that has since been moved or deleted.
- **A Roblox account won't launch** — its session has probably expired (a red dot). Use **Re-login**
  on that account; Tempo also offers this the moment a launch fails for that reason.
- **"Tempo has been modified" / "This version was never published"** — the tamper check found that
  your copy isn't the file published for its version. If you built it yourself, click **Trust this
  copy** in Settings › Data & Backup. If you downloaded it, get it again from the
  [releases page](https://github.com/justcamop555-pixel/Tempo/releases).
- **Something looks wrong, or it crashed** — Tempo writes a report you can send in (see below).

## Reporting bugs

Use **Settings › Report a bug…** or **Email a bug…** in the app, or open an issue on
[GitHub](https://github.com/justcamop555-pixel/Tempo/issues). Either button opens a composer that
shows you the whole report — pre-filled with your Windows version, hardware, GPU, install type and
this session's warnings — which you can edit before choosing GitHub, your email app, Gmail, Outlook,
Yahoo or the clipboard. Identifying details are removed for you.

Please say what you did, what you expected and what happened. If Tempo crashed, it also saves a
crash report; attaching it helps a lot. **Settings › Data & Backup › Live debug** → **Copy** gives an
even fuller picture to paste in.

## Where your data is stored

Everything lives in your user profile, installed or portable, so it survives updates and never needs
administrator rights:

```
%LOCALAPPDATA%\AutoClicker\
```

That folder holds your settings, profiles, macros and statistics — plus the speech models if you use
Live Captions, and the encrypted account vault if you use the Accounts tab. **Settings › Data &
Backup** can back all of it up.

## Credits

Live Captions stand on [whisper.cpp](https://github.com/ggml-org/whisper.cpp) and
[Whisper.net](https://github.com/sandrohanea/whisper.net), running OpenAI's
[Whisper](https://github.com/openai/whisper) models, with [Silero VAD](https://github.com/snakers4/silero-vad)
for voice detection. Audio capture uses [NAudio](https://github.com/naudio/NAudio). Each is used
under its own licence.

---

# For developers

The rest is for reading the source or cutting a release — you don't need any of it to use Tempo.

## What is published here

Nearly all of Tempo is on this branch: the click engine and its timing, multi-point routes, the
macro recorder and player, Live Captions, the 38 themes, every other tab, the installer scripts, the
release tooling and the website.

The Roblox **Accounts** stack is not — the encrypted vault and its crypto, the real-browser sign-in,
the Roblox API client, the local API and the launcher. Those files are left out, so a clone is there
to **read**, not to build: `dotnet build` on a fresh clone stops on the missing types. Everything
Tempo ships can still be checked against its release the usual way — see **Tamper check** above —
and the program that check verifies is built from exactly this tree plus those files.

## Build & run

**Requirements:** [.NET 8 SDK](https://dotnet.microsoft.com/download), Windows.

```bat
dotnet build -c Release
dotnet run   -c Release
```

Or open `AutoClicker.csproj` in **Visual Studio 2022** (with the *.NET desktop development*
workload) and press **F5**. For a release-quality build, use **`publish.cmd`** rather than calling
`dotnet publish` by hand.

These commands are what a maintainer runs against the complete tree; a public clone is missing the
Accounts stack above and will not compile as it is.

## Publishing a release (maintainers)

```bat
publish.cmd                 :: win-x64 test build, full clean
publish.cmd /official       :: the build you upload to GitHub
publish.cmd win-arm64       :: a different runtime
publish.cmd /quick          :: incremental (faster; don't ship this)
publish.cmd /ci             :: plain output for scripts/automation
publish.cmd /history        :: the developer build history
publish.cmd /help           :: all options
```

Every build is stamped with a **build ID** — the minute it was made, in UTC, e.g. `build 260908-2203`.
Without `/official` it is stamped **test**, wears a TEST badge on every screen, and its artifacts are
staged in `bin\publish\test\` so they can never be mistaken for — or written over — a release.

With `/official`, the uploadable artifacts land together in `bin\publish\`:

- `Tempo.exe` — one fully self-contained file (the .NET runtime, UI-Automation assemblies and the
  native speech libraries are bundled and unpack themselves at run time)
- `Tempo.exe.sha256` — its checksum
- `<version>.zip` — the bundle users download (run `Tempo.exe` for portable, or `install.cmd` to
  install), containing `install.cmd`, `uninstall.cmd` and `INSTALL-README.txt`
- `CHECKSUMS.txt` — checksums for the exe and the zip
- `RELEASE-NOTES-<version>.md` — copied from `release-notes/`

Each step is verified (exe produced, ReadyToRun actually applied, size sane, checksum written,
embedded version matches the project) with a progress display; a full log goes to `publish-log.txt`.
`publish.cmd` uploads nothing.

### Cutting a release
1. Bump `<Version>`, `<AssemblyVersion>` and `<FileVersion>` in `AutoClicker.csproj` — the only place
   the version lives; About and the startup log read it from the assembly. Add an entry to
   `CHANGELOG.md` and a matching `release-notes/<version>.md`.
2. Run `publish.cmd /official`.
3. Create a GitHub release tagged **`v<version>`** and attach both **`<version>.zip`** (for new users,
   portable or installed) and the standalone **`Tempo.exe`** (what the in-app updater downloads).
4. Paste `release-notes/<version>.md` as the description.

> [!IMPORTANT]
> Attach the **exact** `Tempo.exe` that `publish.cmd` produced. Tempo's tamper check compares the
> running file with the SHA-256 GitHub publishes for the release, so any other exe makes every
> user's copy report as modified, with no Official ID. Every run of `publish.cmd` stamps a new build
> ID, so even unchanged source rebuilds to a **different** exe: once a release is uploaded, don't
> rebuild it. The compiler itself is deterministic. The same commit with the same build stamp
> (the exact one is in `bin\build-history.tsv`) rebuilds byte for byte.

## Project structure

```
AutoClicker/
  Program.cs        App entry point, single-instance hand-over, global exception handling
  Engine/           Click engine, precise timing, schedulers, macro player/recorder
  Models/           Settings, profiles, statistics, hotkeys, enums
  Native/           Low-level keyboard/mouse hooks, raw input, second-mouse listener
  Persistence/      Crash-safe saving of settings, profiles, macros, history
  UI/               MainForm (per-tab partials) + dialogs + theming + notifications
  Utils/            Logging, updates, integrity, localization, speech engine, voice
                    detection, startup registration, helpers
  Assets/           Icon, About artwork and the voice-detection model (embedded at build time)
  release-notes/    One file per version, pasted into the GitHub release
  publish.cmd       Release builder (see above)
  install.cmd       Per-user installer
  uninstall.cmd     Matching uninstaller (also embedded, so installs keep theirs current)
  install.ps1       The web installer behind the one-liner at the top
  index.html        The website (GitHub Pages serves this branch)
```

Not in the tree, by choice: the Accounts stack — the encrypted vault and its crypto in
`Persistence/`, the browser sign-in, Roblox API client, local API and launcher in `Utils/`, and the
account screens in `UI/`. See [What is published here](#what-is-published-here).

> [!NOTE]
> `.gitattributes` sets `* -text` on purpose. `publish.cmd` needs CRLF line endings and is mis-read by
> `cmd.exe` without them, while `install.cmd`, `uninstall.cmd` and `INSTALL-README.txt` ship inside
> the release zip and must stay byte-identical to what was published. Git's Windows default would
> rewrite all of them on checkout.

## Changelog

See [CHANGELOG.md](CHANGELOG.md). Per-version release notes are in the `release-notes/` folder.
