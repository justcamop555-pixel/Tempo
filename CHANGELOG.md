# Changelog

All notable changes to Tempo. Newest first. Per-release notes are also in the `release-notes/` folder and on the [Releases page](https://github.com/justcamop555-pixel/Tempo/releases).

### 1.0.162
**Fixed: install.cmd couldn't find Tempo.exe after publishing**
- Fixed install.cmd failing with "couldn't find Tempo.exe" when you run it from the
  project folder after publishing. The exe lands in bin\publish\<rid>\, but the
  installer only looked next to itself and one sub-folder deep. It now searches the
  publish output folder (bin\publish\) and deeper sub-folders automatically, so it
  finds Tempo.exe whether you run it from inside the unzipped setup folder OR straight
  from the project root after a build.
- The installer now prints exactly which folder it found Tempo.exe in, and its
  "not found" message gives separate, clear fixes for the unzip case vs the
  just-published case.
- uninstall.cmd now detects when Tempo isn't actually installed (e.g. you only
  published, or you use it portably) and says so clearly instead of pretending to
  remove an installed copy. It still honours /purge to clear saved settings in that
  case.

### 1.0.161
**Blank-tab safety fix, rewritten README & website, more publish polish**
- Fixed a case where a tab (e.g. Settings) could appear blank: when you switch tabs,
  Tempo now always scrolls the new tab to the top and forces it to repaint, so its
  content can never be stuck scrolled out of view.
- Rewrote the README to be user-first: clear step-by-step install for both the
  installer and portable, plain-language first steps and recommendations, privacy,
  troubleshooting, and where data is stored - with developer/build details moved to
  the end.
- New website (landing page) with the same clear install steps, friendly getting-
  started advice, an honest first-run note about the unsigned-app warning, and a
  feature overview.
- The release builder (publish.cmd) now prints an "All checks passed" confirmation
  after verification, on top of the colourful animated output and per-check green
  ticks added last update.

Note: the blank-tab fix is a safety improvement. If a tab still ever shows blank,
please tell me your Windows version and which tab, so I can pin down the exact cause.

### 1.0.160
**A much nicer release builder + a modernized Macros tab**
- The publish/release builder (publish.cmd) got a big visual overhaul: an animated
  colour-gradient TEMPO banner with a subtitle, colour-coded step headers, green
  [OK] check marks on every verification, and a colourful success/failure summary
  box. It also makes each phase and check clearer, and still falls back to plain
  text automatically on older consoles or in CI (/ci).
- The builder continues to verify correctness at every step - that the exe was
  produced, its size looks right for a self-contained build, its SHA-256 is written,
  and its embedded version matches the project - now with clear on-screen ticks so
  you can see each check pass.
- Reworked the Macros tab so it no longer looks dated: the long loose column of
  management buttons is now an organized "Manage" panel, Delete is clearly marked in
  red, and spacing/labels are tidied. No macro features were added or removed - it's
  the same actions, just cleaner and easier to scan.

### 1.0.159
**Reliability polish across Clicker, Macros, Multi-Point, Keybinds, Live Captions & updates**
- Fixed a latent crash in macro playback: a macro with no steps (or before its
  totals were ready) could divide by zero while updating the progress bar and bring
  the app down. The progress bar is now guarded and only redraws when it actually
  changes, which also removes a little flicker during playback.
- Fixed a similar divide-by-zero risk in the CPS test's time bar, and clamped it so
  it can never be set out of range (a known cause of progress-bar errors).
- Reviewed the Clicker, Multi-Point, Keybinds, Live Captions and update
  check/download paths for the same class of problems (bad math, out-of-range values,
  missing guards). These were already solid - inputs are validated, keybind conflicts
  are detected and explained on save, Multi-Point refuses to start with no enabled
  points, the live rate clears when clicking stops, and the updater safely falls back
  to the download page when there's no direct installer - so no further changes were
  needed there.

No features were added or changed in this update - it's purely reliability and
polish.

### 1.0.158
**Stability: fixed freeze/crash under high stress**
- Fixed Tempo freezing (and sometimes crashing) at very high click speeds. At
  extreme CPS the clicking thread could spin without ever yielding the CPU, pegging
  a processor core to 100% and locking up the UI and sometimes the whole system. The
  clicker now always yields a sliver of CPU time between clicks, so it stays
  responsive even flat-out, and a hard speed floor is enforced even with anti-freeze
  turned off.

**Portable vs installed: made it fair and clear**
- Tempo now tells you, in Settings, when it's running as a portable copy and which
  two things depend on the exe's location ("Start with Windows" and in-app updates).
  Everything else works identically portable or installed - now you know the trade-off
  instead of being surprised by it.

**Installer / uninstaller fixes**
- Fixed the installer downloading the speech model to the wrong folder, where the app
  couldn't find it - so the bundled-model option now actually works. The model is
  placed where Tempo reads it.
- The uninstaller now tells you the saved-data folder can include a large (100+ MB)
  speech model before you choose whether to delete it, and removes it when you do.

### 1.0.157
**Stability: fixed random crashes**
- Fixed a cause of Tempo crashing at random, most likely while using Tempo's own
  Live Captions. The audio-capture callback (which runs constantly on a background
  thread while captions are on) wasn't protected against errors, and a single bad
  audio buffer could take the whole app down. It's now fully guarded - a bad buffer
  is skipped instead of crashing Tempo.
- Hardened the rest of the caption engine's background work (its worker thread and
  the no-audio watchdog) so any unexpected error there is logged and contained
  rather than crashing the app.
- Added a safety net for stray background tasks across the app: an unexpected error
  in any background task is now logged and contained instead of being allowed to
  crash Tempo.

If Tempo ever does still crash, it writes a crash report you can send in - but these
fixes close the most likely random-crash path.

### 1.0.156
**Fixed the caption-bar error message + the caption hotkey**
- Fixed a raw technical message ("the .dll files stay next to Tempo.exe…") showing
  up on the caption bar itself. If Tempo's own captions can't start, the bar now
  shows a short, friendly line and the full detail goes to a tray notification
  instead of being dumped on screen as if it were a caption.
- Reworked packaging so Tempo's speech-engine native files ship and load correctly:
  Tempo.exe stays a single self-contained file (app, settings and .NET runtime all
  inside), and the small native speech .dll files sit beside it and are installed
  alongside it. This addresses the "engine files missing" error.
- Fixed the Live Captions hotkey: it now follows the caption source you picked. If
  you selected Tempo's own captions, the hotkey starts Tempo's engine - it no longer
  forces Windows 11 Live Captions on. (The on-screen toggle already did this; now the
  keybind matches.)

If Tempo's own captions still can't start on your PC, the tray message will say why,
and Windows 11 Live Captions (Settings > Live Captions > Caption source) remains the
reliable default.

### 1.0.155
**Tempo's own captions: more accurate, a bit snappier**
- Tuned the speech engine to reduce wrong/garbled words: it now uses steadier
  decoding settings, fixes its decoding temperature so it stops inventing
  alternative words, stops one chunk's mistakes from carrying into the next, and
  uses more CPU threads so each chunk is transcribed faster.
- Reduced the delay a little more (a fresh result roughly every ~1.5 seconds).

**An honest note about Tempo's own captions**
Tempo's own captions use a small speech model running entirely on your PC. Because
of how that kind of model works, it cannot update every 0.1 second or be as
accurate as Windows 11 Live Captions - that uses a much larger, streaming,
GPU-accelerated engine built for exactly this. A local model needs a couple of
seconds of audio at a time to recognise words, so there will always be a short
delay, and the small model will occasionally mishear words.

If you want the most accurate, lowest-delay captions, use Windows 11 Live Captions
(Settings > Live Captions > Caption source) - it's still the default. For better
accuracy from Tempo's own engine, choose a larger model (Small or Medium) in
Settings; note larger models are more accurate but a little slower, not faster.

### 1.0.154
**Fixed the broken build (tiny exe / app not working)**
- Fixed Tempo being unusable after the last update. The build had switched to a
  multi-file folder layout, but the installer wasn't copying all the required files
  (the app code and its config), so Tempo wouldn't start. Tempo is now back to a
  single self-contained Tempo.exe that bundles everything (the .NET runtime and the
  speech engine), so it just works again. The exe is large because it contains
  everything - that's expected.

**Tempo's own captions: fixed garbled / nonsense text**
- Fixed the main cause of Tempo's captions producing wrong or nonsense words. When
  converting your PC's audio (usually 48 kHz) down to the 16 kHz the speech model
  needs, Tempo wasn't filtering first, which scrambled the sound into noise. It now
  filters properly before converting, so the speech model hears clean audio.
- Reduced the caption delay further (a fresh transcription roughly every ~1.7 s).

Honest note: Tempo's own captions use a small offline model and won't match the
accuracy or instant feel of Windows 11 Live Captions, which uses a much larger,
GPU-accelerated engine. If you want the most accurate, lowest-delay captions,
Windows 11 Live Captions remains the better choice and is still the default. Tempo's
own engine is the offline/no-Windows-captions option.

### 1.0.153
**Layout repair after games + better Tempo captions**
- Fixed the layout getting messed up / controls overlapping after a full-screen game
  changes your screen resolution. Tempo now automatically re-lays-out every tab when
  Windows reports a display change, so the Clicker, Macros, Live Captions and other
  tabs repair themselves instead of staying scrambled until you resize the window.
- The caption overlay and history windows are also nudged back on-screen after a
  resolution change, in case the screen shrank under them.

**Tempo's own captions: less delay, fewer missed words**
- Reworked how Tempo's offline captions process audio. Captions now update roughly
  every ~2 seconds instead of ~4, so there's noticeably less delay.
- Words spoken across a chunk boundary are no longer dropped: each pass now carries
  a second of previous audio forward as context, so speech isn't cut in half.
- If transcription falls behind on a slower PC, Tempo now drops the oldest backlog
  audio instead of letting the delay grow and grow.
- Near-silent audio is skipped, which speeds things up and stops the engine from
  inventing phantom words during quiet moments.

Tempo's own captions still need a speech model and a working audio source (system
audio if you have a speaker, or a microphone). Windows 11 Live Captions is
unchanged and remains the default.

### 1.0.152
**Fixed: installer couldn't find Tempo.exe + reworked packaging for the speech engine**
- Fixed "ERROR: Tempo.exe was not found next to this installer." Tempo is now
  published as a self-contained folder (Tempo.exe with its runtime and the speech
  engine's native files) instead of a single packed .exe. The previous single-file
  packaging both hid the speech-engine libraries and could leave the installer
  without a Tempo.exe to find. The folder approach is the reliable way to ship the
  Whisper speech files.
- The installer is now much more forgiving: if you unzip in a way that puts the
  files in a sub-folder, it finds Tempo.exe one level down automatically. If it
  still can't find it, the error now lists exactly what's in the folder and how to
  fix it, instead of a dead end.
- The installer copies the speech-engine files (and any runtimes folder) next to
  Tempo.exe, and uninstall removes them all.

How to install (unchanged for you): unzip the setup zip, then double-click
install.cmd. Just keep all the unzipped files together in one folder. Windows 11
Live Captions still needs none of this and remains the default caption source.

### 1.0.151
**Fixed: Tempo's own captions failing with a "Whisper.net.Runtime" error**
- Fixed the real cause of Tempo's offline captions doing nothing while showing a
  message about installing "the default libraries with the Whisper.net.Runtime
  nuget". Tempo's speech engine needs its native runtime files (whisper.dll etc.)
  sitting next to Tempo.exe, but the build was packing them inside the single-file
  executable, where the engine couldn't find them. The build now keeps those native
  files beside Tempo.exe so the speech engine loads correctly.
- The installer now copies those speech-engine files (and any runtimes folder)
  alongside Tempo.exe, so an installed copy has everything it needs.
- If the engine still can't find its files, Tempo now shows a clear, plain-language
  message (reinstall and keep the .dll files next to Tempo.exe) instead of the
  cryptic developer error.

Important for distribution: a published Tempo is now Tempo.exe plus a few native
.dll files in the same folder (no longer a single lone .exe). Always ship/keep them
together. Windows 11 Live Captions is unaffected and still the default.

### 1.0.150
**Fixed: Tempo's captions stuck on "Listening…" (and no-speaker PCs)**
- Fixed the main reason Tempo's own captions showed "Listening…" forever and never
  produced text: Tempo's engine captures your PC's *audio output*, which needs a
  speaker/playback device. On a PC with no speaker there was nothing to capture, so
  it sat silent. Tempo now detects this.
- New "Listen to" choice for Tempo's own captions (Settings > Live Captions):
  - **Auto** — uses system audio if a speaker exists, otherwise automatically falls
    back to your microphone.
  - **System audio** — captions whatever your PC plays (needs a speaker).
  - **Microphone** — captions sound from a mic (use this if your PC has no speaker).
- The caption bar now shows the real reason instead of a frozen "Listening…": if no
  speaker is found it tells you to switch to a microphone, and if nothing is playing
  it says so. No more silent waiting with no explanation.
- If no audio arrives within a few seconds, Tempo now reports why rather than
  appearing to do nothing.

This only affects Tempo's own offline captions. Windows 11 Live Captions is
unchanged and still the default.

### 1.0.149
**One-click speech model + faster, more reliable updates**
- Tempo's own offline captions no longer need any manual file copying. Two new ways
  to get the speech model:
  - The installer can now download the small Base model for you during setup, so
    Tempo's own captions work out of the box. (Skippable; if you're offline you can
    still add it later.)
  - In Settings > Live Captions there's a new "Download model" button that fetches
    the selected model straight into the right folder with a progress bar - no
    finding or copying files by hand.
- Fixed update downloads sometimes stalling or getting stuck loading. The download
  (and its checksum fetch) now skip Windows' automatic proxy discovery, which was
  the usual cause of long hangs on networks without a proxy - the same fix already
  applied to the update check.
- Fixed overlapping controls in the Settings > Live Captions section and made the
  two new buttons fit cleanly.

Note: the Base model is ~140 MB and is fetched on demand (by the installer or the
in-app button), not bundled in the app download itself. Windows 11 Live Captions
still needs no model and remains the default.

### 1.0.148
**Live Captions: fixed Settings overlap + clearer Tempo-captions setup**
- Fixed overlapping controls in the Settings tab: all the Live Captions options now
  live in their own dedicated, neatly spaced "Live Captions" section instead of
  being crammed into the Behaviour group where they overlapped other controls.
- Made Tempo's own captions much clearer to set up. The section is now numbered:
  1) pick the caption source, 2) (for Tempo's engine) pick the model. The status
  line now tells you in plain words whether the model is installed and exactly what
  to do if it isn't, in green when ready and amber when not.
- Added an "Open models folder" button so you can drop a Whisper model file in with
  one click, then re-select it to use Tempo's own offline captions.

Why "nothing happened" before: Tempo's own captions need a speech model file
present, and this build doesn't ship one yet (that's coming). Until a model is in
the models folder, Tempo's own engine has nothing to transcribe with — the new
status line now says so clearly. Windows 11 Live Captions works without any model
and remains the default.

### 1.0.147
**Two clearly separated caption sources**
- Live Captions now has one clear choice instead of confusing checkboxes. In
  Settings > Behaviour > Live Captions, a "Caption source" dropdown lets you pick:
  - **Windows 11 Live Captions** — uses the built-in Windows engine, mirrored into
    Tempo's caption bar (what Tempo did before).
  - **Tempo's Live Captions (offline)** — Tempo listens to your PC's audio and
    transcribes it itself with a local Whisper model. No internet; nothing leaves
    your PC.
- You always know which one is active, and the two never run at the same time -
  switching source cleanly stops the other.
- When Tempo's own engine is selected, a model picker appears (Base / Small /
  Medium) with a clear "installed / not installed" status so you know whether the
  speech model is ready.
- Tempo's own caption text flows into the same overlay bar and history you already
  use, with all the same font/colour/size/opacity/position options.

Note: Tempo's own engine needs a speech model present (the installer is intended to
ship the small Base model). If none is installed, Tempo tells you where to add one.
Windows Live Captions remains the default.

### 1.0.146
**Tempo's own Live Captions — engine foundation (Phase 1)**
- Groundwork for Tempo transcribing speech itself, offline, instead of relying on
  Windows Live Captions. This build adds the engine but does not switch it on yet.
- New offline speech engine: Tempo can capture the PC's own audio output (so it
  hears the game or video, not just a microphone) and run the Whisper speech model
  locally to turn it into captions — no internet, nothing leaves your PC.
- Choose-your-model support: a small "Base" model is intended to ship with the
  installer for instant offline use, and you'll be able to pick a larger, more
  accurate model in Settings. Models live in a per-user folder so they survive
  updates and need no admin rights.
- This is the engine layer only; the Settings toggle to use Tempo's own captions
  instead of Windows, and the wiring into the on-screen caption bar, come next.

Technical note: the speech engine uses NAudio and Whisper.net. A normal Windows
build restores these automatically; behaviour is unchanged until the feature is
switched on in a later update.

### 1.0.145
**Full-screen & window fixes**
- Fixed scrolling into empty space below the content in full-screen mode: when a
  short page is vertically centred, the scroll range is now pinned to the visible
  area so you can't scroll past the content into a void. Normal and maximised
  windows are unchanged (content at the top, scrolls normally).

**Clicker — existing-feature fixes**
- Fixed a profile round-trip bug: a profile saved above 1000 CPS (which uses a
  sub-millisecond interval) reloaded showing roughly half the rate on the speed
  slider. The slider and label now restore the correct CPS when you load such a
  profile.

**Macros — existing-feature fixes**
- The per-macro buttons (Play, Play once, Edit, Rename, Delete, Export, Pin) are now
  disabled when no macro is selected or the list is empty, instead of staying
  clickable and only showing a warning. They re-enable as soon as you select a
  macro, and update correctly after a macro is deleted or after playback finishes.

### 1.0.144
**Richer status bar**
- The bottom status bar now shows more at a glance. The state word gets a coloured
  dot (green running, amber paused, grey idle) so you can read the state instantly.
- Added a live Peak CPS readout next to the current CPS, clearer separators between
  groups, and tidier grouping of profile, counts, rate and time.
- New target-run progress indicator: for a fixed-count run it shows "1,250 / 5,000
  (25%)", and for a timed run it shows the time remaining. It appears only while a
  target run is active and hides otherwise.
- New anti-freeze indicator: a "⚡ throttling" flag appears in the status bar while
  anti-freeze is actively slowing the click rate to protect your PC, and disappears
  when it stops.

### 1.0.143
**Profiles — foundation (Phase 1 of the new Profiles tab)**
- Profiles can now carry far more than clicker settings. Each profile gained a
  category (Gaming / Work / Productivity / Custom), an icon, a colour tag, a
  favourite star, created/last-used dates, and usage stats (times used and total
  runtime). Existing profiles keep working and pick up sensible defaults.
- Profiles can now also store your keybinds and your app/overlay look (theme,
  accent, notifications, always-on-top, clicking indicator, and the caption overlay
  settings) so that switching profile can switch your whole setup. (Wiring these
  into the new tab's UI comes in the next update.)
- New under-the-hood profile tools, ready for the Profiles tab: a recycle bin that
  lets deleted profiles be restored, favourite toggling, usage tracking, and
  export/import of a single profile as a JSON file with validation and name-clash
  handling (rename or overwrite).

This is the groundwork for a full Profiles tab; the visual tab itself lands next.
No existing behaviour changes in this build.

### 1.0.142
**Bug-fix pass (Live Captions)**
- Fixed: changing the caption text size moved the caption bar back to the bottom of
  the screen, undoing a position you'd dragged it to. Resizing now keeps the bar
  where you put it (and just clamps it to stay on-screen). Same fix for the history
  overlay.
- Fixed: after using "Move captions" to reposition the bars, the live caption bar
  kept showing the "Drag me" placeholder until the next words arrived. It now
  restores the real caption (or "Listening…") as soon as you leave move mode.
- The caption history overlay's text-size limit now matches the live bar (up to 72).

Also reviewed the clicking engine, macro player, timers and file I/O for crashes,
leaks and threading issues - no further problems found; the engine's rate maths is
correctly guarded against divide-by-zero and the UI marshals cross-thread events
safely.

### 1.0.141
**Live Captions: text-only option + size/colour changes apply instantly**
- New choice: turn the caption background panel off for a clean text-only look. In
  Settings > Behaviour > Live Captions, uncheck "Show caption background panel" and
  captions become just floating text with no panel behind them. Text-only mode adds
  a stronger soft glow automatically so the words stay readable over any scene.
- Fixed: changing the caption text size or colour (or font/opacity) now takes effect
  immediately when you click Save, even while the caption bar is already showing.
  Before, look changes only appeared after toggling captions off and back on.
- The caption text-size limit is raised from 48 to 72 for larger, more readable
  captions.

### 1.0.140
**Caption overlay bar: full visual overhaul**
- The caption bar has been completely redrawn. It now sits in a soft, rounded,
  semi-transparent panel with a subtle gradient and a hairline border, instead of
  floating bare text - much easier to read while still letting the game show
  through. It's rendered with true per-pixel transparency, so edges are smooth and
  the panel can be softly translucent rather than all-or-nothing.
- Captions now fade in gently when they appear and a soft glow is drawn behind the
  text, keeping words legible over bright, busy or fast-moving scenes without a
  hard black box.
- A thin "live" accent line sits along the bottom of the bar and brightens/pulses
  while new speech is arriving, so you can tell at a glance that captions are
  flowing - and it calms down when talking stops.
- All your existing controls still apply (font, text size, colour, opacity, drag to
  reposition). Opacity now softens both the text and the panel together, and the
  newest words always stay on screen (no "..." and no overlapping lines).

### 1.0.139
**Live Captions: Windows detection, drag, and overlapping text all fixed**
- Windows 11 captions not appearing: Tempo now detects Live Captions by its
  process (the same thing you see in Task Manager), not just by window title/class,
  which varied by Windows build and locale. It also finds the caption window by
  walking the LiveCaptions process's own windows when the title lookup misses - so
  the toggle reliably knows whether captions are on, and Tempo's mirror finds the
  text far more often.
- Drag fixed: turning on "Move captions" now forces the click-through style off
  immediately (a frame-change refresh), and the live caption bar paints a visible
  grab panel while in move mode - previously its transparent pixels couldn't be
  grabbed, so dragging did nothing. Now you can grab either bar anywhere and drop
  it where you want; toggle move mode off to lock them click-through again.
- Overlapping text fixed: the outlined caption text is drawn from glyph outlines,
  which are taller than the old line-spacing measurement assumed, so lines could
  overlap and become unreadable. Both the live bar and the history overlay now
  space every line by its real drawn height plus a guaranteed gap, so text never
  overlaps.

### 1.0.138
**Fixed: big empty gap above Settings (and other tabs)**
- A recent change centred page content vertically, which in a normal or maximised
  window pushed everything down and left a large blank area you had to scroll past.
  Vertical centring now only applies in true full-screen mode (F11); in a normal or
  maximised window, content starts at the top as it should - no gap.

**Live Captions: drag the caption bars anywhere**
- New tray toggle "Move captions (drag to reposition)". Turn it on and both the live
  caption bar and the history overlay stop being click-through so you can drag them
  wherever you like; turn it off to lock them back to click-through. Your chosen
  positions are remembered between sessions.
- While move mode is on, the bars show sample text so there's something to grab, and
  a tray tip explains how to lock them again.

### 1.0.137
**Caption history: show/hide from the tray**
- The caption history overlay is now a tray toggle: right-click the Tempo tray icon
  and click "Caption history" to show it, click again to hide it. A checkmark shows
  whether it's currently visible.

**Languages: Tempo now matches your Windows language automatically**
- On first run Tempo picks the matching display language from the ones it already
  ships (Spanish, French, German, Italian, Portuguese, English) based on your
  Windows language - no new languages added, just automatic use of the existing
  ones. Your manual choice in Settings is always respected and never overridden.

**Installer: fixes for users who couldn't get Tempo installed**
- Locked-down PCs that block PowerShell previously ended up with no Start Menu
  shortcut and looked like a failed install. The installer now falls back to a
  VBScript (cscript) to create shortcuts, and if even that's blocked it tells you
  exactly where Tempo.exe is so you can run it directly.
- A checksum mismatch (often just a line-ending/format quirk in the .sha256, not
  real corruption) no longer aborts the install - it warns and continues, so
  genuine users still get Tempo.
- The finish screen now confirms clearly whether a Start Menu shortcut was made and,
  if not, points you to the installed Tempo.exe.

**Uninstaller**
- More reliable self-removal: the delayed delete uses timeout with a ping fallback,
  so it works even on systems where one of those isn't available.

**Also includes (from recent builds)**
- Fixed scrolling that could stick in full screen / maximised, and Live Captions
  customisation (font, size, colour, 50% default text opacity).

### 1.0.136
**Fixed: scrolling could stick in full screen / maximised**
- Some tabs would stop scrolling part-way when the window was full screen or
  maximised. The vertical-centring used for short pages was applying an offset even
  when content nearly filled the height, which left the scroll range unstable. Now
  content is only centred when there's clear headroom; otherwise the page scrolls
  normally from the top, and the scroll range is recomputed after any reflow - so
  scrolling stays reliable in every tab at any window size.

**Live Captions: more customisation, calmer default**
- Caption text opacity now defaults to 50% (the background was already transparent),
  so captions sit more gently over games out of the box. You can still set it
  anywhere from 10-100%.
- You decide the look: caption font, text size, colour (any colour) and opacity are
  all in Settings > Behaviour > Live Captions, applied to both the live bar and the
  history overlay.

### 1.0.135
**Live Captions history: full lines (no more "..."), better merging, less delay**
- History lines no longer get cut off with "...". Each line now wraps to as many
  rows as it needs, and the panel fills from the most recent text upward, so you
  always see complete sentences instead of truncated ones.
- Better de-duplication. Live Captions slides a phrase forward (the next line often
  begins where the previous one ended); Tempo now stitches those overlapping pieces
  into one continuous line instead of stacking near-identical rows. Combined with
  the existing refine-in-place handling, the history reads as distinct, continuous
  speech.
- Less delay. The caption poll is faster (every 150 ms instead of 250 ms), so both
  the live bar and the history keep up more closely with what's being said.

### 1.0.134
**Live Captions: fixed repeated history lines & Windows captions not appearing**
- Fixed the history overlay filling with the same sentence repeated. Windows Live
  Captions streams a phrase by re-sending it as it grows and lightly revises the
  wording; Tempo's old check treated each tiny revision as a new line. It now
  recognises a phrase being refined (shared long prefix, or one line containing the
  other) and updates that line in place, so the history shows distinct utterances
  instead of six near-identical copies.
- Fixed Windows Live Captions sometimes not appearing when you toggle captions on
  (while still showing in Task Manager). Win+Ctrl+L is a toggle, so sending it
  blindly could turn captions off - or launch them hidden - when they were already
  running. Tempo now checks whether the Live Captions window exists and only sends
  the key to reach the state you asked for, re-sends once if it doesn't appear, and
  warns you (with how to fix it) if Windows still won't start it.

### 1.0.133
**Live Captions: the full-history view is now an overlay too**
- The "Show caption history" view is no longer a plain window - it's now a caption
  overlay in the same style as the live bar: frameless, always-on-top and
  click-through, sitting in the upper-left of the screen. It shows the most recent
  several lines of the session transcript and auto-scrolls as new captions arrive,
  so you can glance back at what was said without it ever stealing focus or blocking
  clicks in your game.
- It uses the same text colour, font and size as the live caption (bright yellow,
  Segoe UI by default), with the same outline + shadow for legibility. The panel
  has a faint ~20%-opaque dark background so several lines stay readable over busy
  scenes - "80% transparent, but you can still see the text", as requested.
- Open it from the tray menu ("Show caption history"). It still tracks the whole
  session (most recent 500 lines) behind the visible window.

### 1.0.132
**Live Captions: fixed disappearing text + added a full-history window**
- Fixed captions vanishing mid-session while still ON. Windows Live Captions
  regularly reports an empty value between phrases (or during a brief read hiccup);
  Tempo was clearing the bar each time. Now it keeps the last line on screen and
  only replaces it when new words actually arrive - so the caption no longer blinks
  away while you're using it.
- New "Show caption history" window (open it from the tray menu). The overlay bar
  only shows the latest line or two; this resizable, scrollable window keeps the
  whole transcript of the current session so you can scroll back, re-read anything
  you missed, and copy it all out. In-progress phrases are refined in place rather
  than duplicated, and the history holds the most recent 500 lines.
- The history window follows your theme, auto-scrolls (toggleable), and has Copy
  all / Clear buttons. Closing it just hides it - the session transcript is kept and
  you can reopen it any time.

### 1.0.131
**Live Captions: fixed the "…" freeze, plus font & colour options**
- Fixed the bug where the caption bar showed one long line ending in "…" and
  stopped visibly updating. As Windows Live Captions accumulates text it becomes a
  long string; Tempo was drawing the whole thing and overflowing the bar. Now the
  overlay shows only the most recent words that fit (dropping words from the front
  as new speech arrives), wrapped to two lines and anchored to the bottom - so the
  latest words are always on screen and it keeps moving as people talk.
- Caption text is now bright yellow by default for readability over games, and you
  can change both the colour and the font:
  - Settings > Behaviour > Live Captions: a "Caption font" dropdown (Segoe UI and
    others) and a "Caption color" picker (any colour; changes apply live).
- The caption bar is a little taller to fit two comfortable lines, and the text is
  still outlined and shadowed so it stays legible on bright or busy scenes.

### 1.0.130
**Live Captions: Tempo's bar now shows the actual words**
- Tempo's caption overlay now mirrors the real transcribed text from Windows Live
  Captions instead of a placeholder. While captions are on, Tempo reads the words
  from the Windows Live Captions window (via UI Automation) and shows them in its
  own transparent bar - then moves the Windows bar off-screen, so you see only
  Tempo's clean caption over your game.
- New Behaviour option "Mirror Windows captions into Tempo's bar & hide the Windows
  bar" (on by default). With it off, Tempo shows its bar and leaves the Windows bar
  where it is. Tempo still doesn't transcribe audio itself - Windows does the
  speech-to-text; Tempo reads and restyles it.
- The bar shows "Listening…" once Windows Live Captions is detected and updates live
  as people speak. It degrades gracefully: if the Windows window can't be found it
  keeps the friendly on-screen note rather than failing.

Note: requires Windows 11 Live Captions (22H2+). The first toggle may take a moment
while Windows starts captioning; the Windows bar is restored when you turn captions off.

### 1.0.129
**New: Tempo's own Live Captions overlay (accessibility)**
- Toggling Live Captions now shows Tempo's own caption bar - a strip across the
  bottom of the screen with a fully transparent background, so only the text is
  visible. It floats over any game, is always-on-top and click-through (it never
  blocks your clicks or steals focus), and the text is drawn with an outline and
  shadow so it stays readable over bright, busy scenes. It follows your theme.
- The same hotkey still also drives Windows Live Captions underneath (this is what
  actually transcribes the audio); you can turn that off in Settings if you caption
  another way. Honest note: Tempo shows and styles the captions, but the real-time
  speech-to-text is done by the OS engine - it does that far better than a bundled
  tool could, and an external app can't cleanly separate voices from a game's mixed
  audio anyway. SetCaption() is the hook a future caption source can feed text into.
- New Settings (under Behaviour > Live Captions): show the overlay bar on/off, also
  drive Windows Live Captions on/off, caption text size, and caption opacity (the
  background stays transparent regardless).

### 1.0.128
**New: Live Captions hotkey (accessibility)**
- Added a bindable "Toggle Live Captions" hotkey on the Keybinds tab. It flips
  Windows Live Captions on and off with a single key, without leaving your game.
  Windows Live Captions (Windows 11, 22H2+) transcribes ALL audio on your PC in
  real time and floats a caption bar over any app, including fullscreen games -
  so it covers game voice chat (Rainbow Six, etc.), Discord and livestream audio.
  It is built into Windows, free, and runs offline after a one-time language pack.
- Why a toggle and not built-in transcription: accurate real-time speech-to-text
  needs OS-level, GPU-backed models and can't separate voices from a game's mixed
  audio in an external app. Windows Live Captions already does this far better than
  a bundled engine could, so Tempo puts the control on your hotkey and lets Windows
  do the captioning.

**Keybinds - clarity**
- The clicker, macro and captions actions are fully independent, each with its own
  configurable hotkey and live conflict highlighting, so none of them collide. (No
  change to existing bindings - this just adds the captions action to the list.)

### 1.0.127
**Build & install tooling - a big, practical upgrade (no app behaviour change)**
- `publish.cmd` rewritten into a 7-phase release builder with real substance:
  flags (`/help`, `/quick`, `/nozip`, `/ci`, `/open`, `/noprompt`), an environment
  preflight (SDK list, OS, CPU, free-disk warning), per-phase timing, output
  validation (size sanity + exe-vs-csproj version check), a combined CHECKSUMS.txt
  for the exe and the setup zip, automatic copy of this version's release notes next
  to the artifacts, reclaimed-space reporting on clean, a richer failure box with
  fixes, a full help screen, and clear exit codes. The proven build comet
  (single-backslash mm:ss) and 24-bit TEMPO banner are preserved.
- `install.cmd` now verifies Tempo.exe against its bundled SHA-256 before copying,
  detects and reports upgrades, closes a running Tempo first, rolls back a failed
  copy, supports `/silent /desktop /nodesktop /launch /nolaunch`, writes an install
  log, and records the website link in the Apps entry.
- `uninstall.cmd` gains `/silent /keepdata /purge /backup`, can save a settings
  backup zip to the Desktop before purging, and writes an uninstall log. The
  safe detached self-delete is unchanged.

### 1.0.126
**Clicker - fixed: pausing ate the "For (seconds)" budget**
- The run-duration clock kept ticking while paused, so pausing a 60-second run for
  45 seconds left only ~15 seconds of clicking - or none at all. The engine's run
  clock now stops while paused and resumes with you: "for N seconds" finally means
  N seconds of actual clicking. The status-bar countdown freezes during pause too,
  because it now reads the engine's active time instead of the wall clock.

**Clicker - fixed-count runs show live progress**
- While a "Fixed count" run is clicking, the status bar now shows how far along it
  is: "Clicking · 100 CPS · 312 / 500 - F6 to stop". The engine exposes its per-run
  click counter, so the number is exact, not estimated.

**Macros - the playback progress bar means what it says**
- With multiple loops, the bar used to sprint 0-100% every loop. For finite runs it
  now fills once across the whole job (loop 2 of 4 sits around 25-50%), matching the
  time-remaining readout next to it. Infinite loops keep the per-loop fill - the only
  sensible reading for them.

### 1.0.125
**Macros - long recordings (hours, even 24h) no longer overwhelm the UI**
- The recording engine was already built for very long sessions: Stopwatch timing,
  movement throttling (min 3 px / 16 ms), idle time coalesced into a single Delay
  step, and gaps clamped safely. The weak point was the Live Monitor, which created
  one list row per captured step with no limit - a long session could pour millions
  of rows into the UI and exhaust memory long before the recorder cared.
- While recording, the Live Monitor now keeps only the most recent 1,000 rows
  (the header shows the true total: "1,248,003 steps captured (showing last 1,000)").
  The recording itself is complete and untouched.
- Selecting or playing a very large macro now fills at most the first 2,000 rows
  (noted in the header) instead of freezing the window while it builds every row.
  Playback highlighting follows the visible window.

### 1.0.124
**New: first-run official-source notice (community suggestion)**
- On first launch, Tempo shows a one-time "Quick safety note" telling you the only
  two official places it's published (the GitHub repository and the website), with
  buttons to open both, and a reminder that every release ships a SHA-256 checksum
  to verify downloads from anywhere else. Auto-clickers are a favourite target for
  malware-laden clones, so people deserve a way to spot the real one.
- Shown exactly once; if you start minimised to the tray it waits for your first
  restore instead of popping out of nowhere.

**About dialog**
- New Website and GitHub links, so the official pages are always one click away
  from inside the app.

### 1.0.123
**Clicker - unsaved profile changes are now visible**
- Edits to the clicker (speed, repeat, position, randomization, the profile name -
  anything the profile stores) live only in the controls until you click Save, and
  switching profiles silently discarded them. An amber "● Unsaved changes - click
  Save" notice now appears next to the profile name the moment your setup differs
  from the saved profile, and clears when you save or load. Detection compares the
  actual built profile against a snapshot, so programmatic loads can never
  false-positive.

**Macros - finish notification, same as the clicker**
- The existing "Notify when a fixed run finishes" option now also covers macro
  playback: when a finite run of loops completes on its own, Tempo plays the chime
  and shows a tray notice with the macro's name and loop count. Stopping playback
  yourself stays silent (the player now tracks natural completion, mirroring the
  click engine), and infinite loops never trigger it.

### 1.0.122
**New: tray sleep - a forgotten Tempo can't surprise you**
- New Behaviour option (on by default): "Sleep in tray (pause hotkeys & cursor
  trail)". While Tempo is hidden in the tray AND nothing is running, all global
  hotkeys and the hold-to-click trigger are paused and the cursor trail is hidden -
  so pressing F6 hours later in a game or document can't start invisible clicking.
- Everything wakes instantly when you open the window or start something from the
  tray menu, and the tray tooltip says "sleeping (hotkeys paused)" while it's asleep.
- It never engages while clicking, playing or recording, so "Hide window to tray
  when clicking starts" keeps working exactly as before.
- Bonus: while asleep, Tempo also skips all background UI refresh work, so an idle
  tray Tempo uses essentially no CPU.

**Settings UI**
- The Behaviour section grew a row for the new option; the groups and buttons below
  it moved down accordingly.

### 1.0.121
**UI fix - Recent sessions header collision**
- The "Recent sessions" title was wide enough to run underneath the Find box and the
  profile filter, so those controls sat on top of the text. The title is now short and
  the usage hint (double-click for details, right-click for options, click a column to
  sort) lives in the table's tooltip instead.

**Statistics - clearer sorting (existing feature)**
- Clicking a column header already sorted the sessions table (numerically and by date,
  not just alphabetically) - but nothing showed which column or direction was active.
  The sorted column now displays a ▲ / ▼ arrow.

**Keybinds - unsaved-changes indicator (existing feature)**
- Edits to hotkeys (and the interval step) sit in the boxes until you click Save
  Keybinds, but nothing said so - switch tabs and they were silently lost. A
  "● Unsaved changes - click Save Keybinds" notice now appears the moment you edit
  anything and clears once saved (or after a reset/reload). The live duplicate-binding
  highlighting stays as before.

### 1.0.120
**Full screen - content is now centred properly**
- In full screen every tab used to sit at the top with a tall empty band underneath.
  Pages now centre their content vertically as well as horizontally whenever it fits
  the window. When a page is taller than the window (e.g. Statistics) it scrolls from
  the top exactly as before, so this can never create a scrollbar by itself.

**Macros - playback timing fixed (measured)**
- Each recorded delay used to be rounded to whole milliseconds on its own, so the
  fraction was thrown away or doubled at every step. At higher speeds this compounded:
  measured on a 200-step run at 3x speed, playback finished 67 ms early (about 14%
  too fast). The fractional remainder is now carried from step to step, keeping the
  cumulative timeline within 1 ms at any speed.

**Clicker - status hints tell the truth in every mode**
- Hold-to-click: the idle hint now says "hold F6 to click" (the key doesn't toggle in
  that mode), and while clicking it reads "Clicking (hold)".
- "For (seconds)" runs now show a live countdown in the status bar
  ("Clicking · 50 CPS · 23 s left - F6 to stop").

### 1.0.119
**Status bar - much more detail**
- The idle hint now describes the whole run you're about to start: mode, speed and
  button as before, plus the finite repeat ("x500" or "60 s") and the click position
  ("@(812,440)" for a fixed point, "multi-point (3)" when using the point list).
- While clicking, the hint shows the target rate too ("Clicking · 100 CPS - F6 to
  stop").
- New: while recording a macro the hint shows "● Recording - N steps" live, with your
  record-toggle hotkey if one is bound.
- The state cell ("Idle / Running / Paused / Stopped (emergency)") is now colour-coded
  - green while running, amber when paused, red after an emergency stop.

### 1.0.118
**The real fix for laptop overlaps: full DPI scaling (PC vs laptop)**
- Root cause found: Tempo's text grows with Windows display scaling (your laptop runs
  125-150%, your PC 100%) but the layout was fixed pixels - so on the laptop every
  label inflated inside an unscaled box and overlapped. No amount of nudging single
  controls could fix that.
- Every window now declares a 96-DPI design baseline, so Windows scales the entire
  layout (positions, sizes, the window itself and its minimum size) to match the
  display - 100% stays exactly as before; 125%/150% now grows everything together.
- Screen overlays (countdown, indicators, points markers, the cursor trail and the
  coordinate picker) are pinned to raw screen pixels so scaling can't misplace them,
  and the page-centring system now measures positions after scaling.
- Tempo now tells you which machine it's on: About shows the display scaling
  ("Display: 1920x1080 @ 60 Hz · 125% scaling") and bug reports include a
  "Display scale" line - so PC-vs-laptop issues are visible at a glance.

### 1.0.117
**Layout fixes from laptop testing (thanks for the screenshots)**
- Clicker: the "Notify when a fixed run finishes" checkbox no longer overlaps the big
  IDLE / RUNNING status word - the word's box was trimmed and the checkbox moved just
  below it.
- Clicker: the Start / Stop buttons' hotkey hint no longer truncates to "Start ·..." -
  the text is now compact ("Start · F6") so it fits the button.
- Keybinds: the right-hand description column no longer overlaps row to row - rows are
  taller and each description has a fixed two-line box. The "Interval step" header was
  also shortened so it can't be cut off.
- Settings: "Remember window position & size" and "Minimise window during macro
  record & playback" show their "&" again (WinForms treats a single & as a shortcut
  marker and hides it).
- Statistics: the Current/Longest Streak captions no longer repeat the value ("Current
  Streak · 1 day"), which clipped at the card edge - the number is the value, the
  caption stays clean.

### 1.0.116
**Tab centring - last gap closed**
- Pages now also re-centre when their inner width changes because the vertical
  scrollbar appears or disappears (not just when the window resizes). That was the
  one remaining way content could sit off-centre and poke out by the scrollbar's
  width, briefly showing a stray horizontal bar.

**Email a bug - quicker repeats and better reports**
- The chooser now remembers how you sent your last report: that option is marked
  "last used", pre-focused, and triggered by just pressing Enter.
- Reports (email and GitHub issue alike) now include the path to Tempo's log file
  with a note to attach it - the log is usually what pins a problem down.

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
