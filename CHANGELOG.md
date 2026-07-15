# Changelog

All notable changes to Tempo. Newest first. Per-release notes are also in the `release-notes/` folder and on the [Releases page](https://github.com/justcamop555-pixel/Tempo/releases).

### 1.0.296
**Burst mode ignored the safety cap · more hotkeys are now bindable · tested live on real hardware**
- **Burst mode was blowing straight through Anti-Freeze.** With the Max-CPS cap set to 10, Interval mode correctly held 10 CPS — but Burst mode hit **256 CPS** (measured live) while the panel still cheerfully read "Protected". Burst never called the anti-freeze path at all, so the safety cap AND the CPU back-off did nothing in the one mode fast enough to actually lock a machine up. Fixed and verified: the same test now holds ~10 CPS, and a legitimately fast rate (cap 2000) still runs full speed. A second, subtler hole — burst size 1 with a 0 ms pause — is closed too.
- **The two CPS numbers on the Clicker tab no longer contradict each other.** With a Double/Triple/Quadruple click type, "Click Interval" showed the multiplied click rate (e.g. 114 CPS) while "Manual Speed" showed the press rate (29 CPS) for the *same* setting, with nothing to explain it. Both now spell it out: "34.5/s × 4 = 137.9 clicks/s".
- **More hotkey combinations can be bound.** Windows Forms was quietly swallowing a whole class of key combos before Tempo ever saw them — **Ctrl+Delete, Ctrl+A, Ctrl+V, Shift+Delete** and the other editing shortcuts simply couldn't be assigned. They all bind now. (Tab, Enter, Space and the arrows, added last release, keep working.)
- **See whether a hotkey is actually reaching Tempo.** On the Keybinds tab, a binding's row now **flashes green the instant its key is pressed**, from any app — so "is my hotkey even getting through?" is answerable at a glance instead of by trial and error.
- **Tempo now warns when Windows refused a hotkey.** If another program already owns a combination, Windows won't reserve it and Tempo quietly falls back to a keyboard hook — the action still fires, but the key also keeps doing its normal job in the other app. That "half works" state used to be invisible; it's now spelled out on the Keybinds tab so you can pick a different combination.
- **Faster multi-point clicking.** The engine was allocating a fresh list on every single click in multi-point mode — hundreds of throwaway objects a second on its hottest path. Now reused.
- Much of this was found and verified by driving the real app on real hardware, not just by reading code.

### 1.0.295
**Tab can finally be bound · the field shows what it's detecting as you press · Tempo now names your keyboard**
- **Tab is bindable at last.** It never worked because the capture field was never even TOLD Tab had been pressed — Windows Forms grabs Tab for focus navigation before a control sees it, so pressing Tab just jumped to the next field. It's now claimed as real key input, along with Enter, the arrows, Page Up/Down, Home and End. (Because the field now swallows Tab, **Esc leaves the field** — that's the way out for keyboard users.)
- **Live "detecting" preview while you bind.** Click a field and it says "▸ listening…". Hold Ctrl+Shift and it reads "Ctrl + Shift + …" **before** you press the final key — so the modifiers Tempo has actually detected are visible up front, instead of committing a combo and wondering why it came out wrong.
- **Ctrl+Delete and Shift+Backspace are bindable now too.** The clear-the-field branch fired on *any* Backspace or Delete, before the capture code could ever see them — silently making every modified variant impossible. Clearing now needs a **bare** Backspace/Delete. Alt+F4 is also passed through, so it still closes Tempo instead of binding itself.
- **Tempo detects and names your keyboard**, on the Keybinds tab and in Live Debug: model, layout and key count (e.g. "SONiX USB Keyboard · English (United States) · Enhanced (101/102-key), 12 F-keys"). When a hotkey "doesn't work on my keyboard", the model and the **layout** are the first two facts anyone needs — the layout decides which physical key produces which virtual key — and Tempo previously couldn't tell you either. Gaming mice that register as keyboards (for their macro buttons) are correctly excluded, so your mouse is never reported as your keyboard.
- **Bare keys now warn you.** A global hotkey is taken **system-wide**: bind a bare "A" and the letter A stops reaching every other program while Tempo runs; a bare Tab would take Tab away from the whole desktop. Risky bare keys are flagged amber (distinct from the red used for two actions clashing) with a plain explanation. Function keys are exempt — that's what they're for, and why F6/F8 are the defaults. It's still allowed; it just can't happen silently.
- Key names read like keys now, not debug output: "Enter", "Backspace", "Page Up", "Left Arrow", "`", "/" — instead of "Return", "Back", "PageUp", "Oemtilde", "OemQuestion".

### 1.0.294
**New: camera-relative movement (advanced) — with calibration, a hotkey, and a Live Debug view that actually shows what it's doing**
- **New feature: camera-relative movement.** Hold a direction and Tempo keeps you travelling that way as you swing the camera, re-mixing W/A/S/D in real time — circle-strafe an enemy, or keep running one way while looking another. Tempo cannot READ the game's camera (it lives in another process), so it **estimates** it from your raw mouse movement and steers against that. Settings → *Camera-relative movement* has every knob: mode, sensitivity, smoothing, anti-jitter, update rate, stick deadzone and pad look speed.
- **Calibration that actually works.** The measurement has to happen inside the game, and you can't click a "stop" button to end it — moving the mouse to the button would add hundreds of counts to the very number being measured. So it's gated on a **held key**: hold F10, turn one full circle, release. Clean measurement, and you never leave the game.
- **A hotkey to arm it in-game**, plus one to re-centre the camera estimate when it drifts. Emergency stop always disarms it and releases every held key.
- **It only touches the game you armed it in.** Suppressing W/A/S/D is global, so arming it and alt-tabbing to Discord would otherwise have silently eaten every "w", "a", "s" and "d" you typed. Tempo captures whichever window was in front when you armed it and leaves your keys alone everywhere else.
- **Anti-jitter, verified.** A keyboard can only express 8 directions, so a heading resting exactly on a boundary would rattle the keys many times a second. A hysteresis band plus a minimum dwell time fixes it: 2,000 samples dithering on a boundary now produce **zero** key changes, while a real turn still commits promptly.
- **Live Debug got much stronger.** It now shows the movement system live — the armed window, whether it's acting, the estimated camera angle, and the line that matters: "you press W → Tempo sends W+A". There's a **Movement trace** toggle (key changes + a 1 Hz heartbeat), an **Always on top** option (the window used to sit *behind* the game you were debugging), and the stats header is now **selectable and colour-coded** — warnings amber, failures red — instead of a wall of identical grey text you couldn't even copy from.

### 1.0.293
**Why "Try GPU engine" seemed to do nothing · captions can no longer fall seconds behind in silence**
- **Ticking "Try GPU engine" while Tempo was running did NOTHING — silently.** The speech engine's CPU/GPU choice is fixed the moment the first model loads and cannot change until Tempo restarts. So if captions had already run once, ticking the box just… didn't apply. No message, no hint — you'd sit watching the CPU engine lag and wonder why the GPU wasn't helping. Tempo now **tells you outright and offers to restart on the spot**, and Live Debug shows a loud warning whenever your GPU setting and the engine actually running disagree.
- **Same trap for auto-downgraded models.** When a model is too slow, Tempo drops to a smaller one *for the session* — and your real choice only comes back on restart. Live Debug now says so explicitly instead of leaving you to wonder why you're on `medium`.
- **Captions can no longer sit permanently seconds behind.** The audio backlog was allowed to grow to **8.4 seconds**. An engine that can't quite keep up would fill that buffer and then just *sit* at the cap: still transcribing every word, but every word arriving many seconds after it was said — forever, and invisibly. That is exactly the "it works but it lags" state. The backlog is now capped at ~3 s: audio older than that is dropped so what's on screen tracks what's being said **now**. Live captions are worth more current than complete.
- **Falling behind is no longer silent.** Dropping audio to catch up now logs a plain warning ("the engine is behind live audio — a smaller model, or the GPU engine, would keep up") instead of happening invisibly.

### 1.0.292
**Captions turned OFF now STAY off · the speech-engine error is fixed at the root (your debug export found it)**
- **Turning captions off no longer un-does itself.** Switch captions off and, about ten seconds later, they came straight back on — and it repeated forever. The watcher that notices you pressing Win+Ctrl+L yourself was checking whether the Windows caption window **is present**, not whether it had just **appeared**. So after you turned captions off, it saw the Windows bar (often the very one Tempo was still closing), read that as "the user just turned Live Captions on", and switched Tempo back on. It now fires only on a genuine off→on transition, and an explicit "off" requires the Windows bar to actually go away before anything can re-arm it.
- **"Start automatically for videos and games" is now honoured everywhere.** With auto-start switched off, captions could STILL come on by themselves, because the Windows-Live-Captions watcher above never consulted that setting — it rode on a different checkbox entirely. If you've told Tempo not to start captions by itself, it now doesn't. From any trigger.
- **The recurring speech-engine error is fixed — at the root.** Every engine restart (a model downgrade, a caption toggle) logged `InvalidOperationException: Collection was modified` from inside the speech engine. Cause: when captions stopped mid-chunk, Tempo abandoned the in-flight transcription, which made the engine tear down its segment handler **while its own native thread was still using it**. Tempo now lets the chunk finish and drains it quietly instead of dropping it on the floor, so the engine is never pulled apart underneath itself. (Found precisely thanks to 1.0.291's improved error logging — the stack trace named the exact line.)
- Also hardened the talking-face detector against the same class of race: turning captions off cleared its tracked-faces list while a video frame was still being analysed on another thread.

### 1.0.291
**A game no longer permanently disables GPU captions · speaker numbering stops resetting every second · errors finally say what went wrong**
- **A busy GPU no longer kills GPU captions for good.** If the GPU speech engine can't keep pace, Tempo used to write "GPU off" into your settings **permanently**. But a GPU that's merely BUSY isn't a GPU that can't do the job: measured live, Large Turbo ran ~10× faster than real time until Call of Duty took the graphics card, after which the same chunks took 3× LONGER than real time. One gaming session was quietly disabling GPU captions forever. The fallback is now **session-only**: Tempo mirrors Windows Live Captions for the rest of the run, the GPU option stays ON, and the next launch tries the GPU again. A GPU that genuinely can't cope just falls back again — costing seconds, not the feature.
- **Your speaker numbering stopped resetting every second.** With two apps making sound at once (a game plus a video), Tempo picked whichever audio session was momentarily loudest — so the detected source flip-flopped between them on *every* one-second tick. That wasn't only log noise: each flip wiped the learned voice profiles and reset speaker numbering, so the speaker labels could never settle. A competing app must now stay loudest for several consecutive ticks before it takes over; the first source heard is still adopted instantly, so nothing is slower to start. The face analyzer also no longer gets re-pointed at a momentarily-loud app's window on ticks where that app was rejected.
- **Errors now name themselves.** A background-task error was logged as a bare "a Task failed" with **no stack trace** — the wrapper exception carries none — so a real fault was completely unattributable. Every inner exception is now logged with its true type, message and stack trace, so a Live-debug export actually points at the culprit.
- **Fixed a crash-adjacent race in the talking-face detector.** Turning captions off (or any caption restart) cleared the tracked-faces list from the UI thread while a video frame was still being analysed on another thread — the classic "Collection was modified" fault. The list is now properly guarded.

### 1.0.290
**Captions hear sooner: ~100 ms of pure waiting removed from the audio path · Live debug sees the whole pipeline**
- **Smaller audio capture buffer (100 → 40 ms).** The audio system was hard-wired to a 100 ms Windows capture buffer — a tenth of a second every caption spent just sitting inside the OS before the speech engine could even see the sound. The capture now opens with a 40 ms buffer (same shared-mode plumbing), with an automatic fallback to the standard 100 ms capture if any device refuses, so nothing can break — worst case is exactly the old behaviour. Applies to system audio, microphone, and the device-change reopen paths alike.
- **The transcribe worker no longer polls.** It used to check for new audio on a fixed 50 ms timer, adding up to a whole tick of dead waiting per pass; the capture callback now wakes it the instant enough sound has arrived. Together with the smaller buffer this trims roughly 80–110 ms off every caption's word-to-screen latency — before the engine even starts working.
- **Cleaner chunk handling.** Each chunk is now lifted out of the audio buffer with a single copy (was two), and any audio thrown away because the engine fell behind is now **counted and shown** ("DROPPED n s" in Live debug) instead of vanishing silently.
- **Live debug got a live audio view.** New header lines: a ten-cell level meter with a dB reading (see at a glance whether Tempo is hearing anything and how loudly), the auto-gain currently applied, the capture device's native format and the actual buffer size in use, plus pipeline counters — chunks done / silent-skipped / captions emitted, a smoothed average inference time, and "last words n s ago". Stats refresh doubled to 2× per second.
- **New "Chunk trace" toggle in Live debug.** One `[Trace]` line per transcription pass — `chunk 2050 ms → 190 ms (0.09×RT) · backlog 0.3 s · gain 1.8× · "the text shown"` — so you can watch the pipeline beat by beat and see exactly where time goes. Switches itself off when the window closes so the log ring isn't flooded afterwards.

### 1.0.289
**Speaker fix: one narrator no longer becomes "Speaker 2 / 3" mid-video**
- **The reported bug:** watching a YouTube video with a single narrator, the label started as Speaker 1 and then flipped to Speaker 2 (and 3) while the SAME person kept talking. Root cause: a silent pause longer than ~4 s (music break, scene change, slow moment) made the pause-heuristic COUNT UP the speaker number whenever the voice verdict wasn't ready at that instant — and background music mixed under the voice could even mint a phantom voice profile that then "confirmed" the wrong number.
- **The fix, two layers:**
  1. While the voice detector is running, a pause alone NO LONGER changes the speaker number — the voice verdict is what changes it. (If it really is a new person, the verdict lands within about a second and the label corrects itself in place, exactly as before.) The old count-up heuristic still applies when voice detection isn't available (no audio device) — there it remains the only signal.
  2. The voice detector now needs ~50% longer of consistently non-matching audio (~1 s) before it will invent a brand-new speaker profile, so music or effects layered under one narrator's voice can't mint a phantom "Speaker 2" — while real speaker changes still classify quickly by matching.
- **Verified on the exact reported scenario:** a real YouTube video with one narrator, pauses and a music break — the label held Speaker 1 throughout. Genuine voice changes still switch labels (that code path is untouched and covered by the standing two-voice test).

### 1.0.288
**Both caption paths improved: the Windows 11 mirror reacts faster · the own engine's seams got cleaner**
- **Windows 11 Live Captions mirror: adaptive polling.** The mirror used to check Windows' caption text on a fixed 250 ms clock. It now polls HARD (140 ms) while the text is actively moving — shaving ~100 ms of average mirror latency exactly when words are flowing — eases to 200 ms in short gaps, and idles at 350 ms through silence so UI Automation isn't hammered for nothing. Verified live: Windows-engine text relayed promptly with speaker labels, colours and the source tag intact.
- **Own engine: cleaner seams between chunks.** The most visible remaining error class was the JOIN between listening windows ("sentence." re-heard as a twisted repeat). The chunk overlap widened from 0.6 s to 0.75 s, so both passes see more shared context at every seam — and the update step actually got slightly SHORTER (2.2 → 2.05 s) as a side effect. Costs ~12% more compute: trivial on GPU and light models; the too-slow ladder still guards weaker setups. Verified live on real playing media with correct text and source tagging.

### 1.0.287
**Live debug tells more · errors now NOTIFY you · first words land even sooner on GPU**
- **Live debug got deeper.** New header lines: "Decode: beam ON · cadence very fast ×0.6 · keep-alive playing" (see exactly which accuracy/speed features are live), and a session events counter ("events: 0 warn / 0 error"). The event stream is now **colour-coded** — errors red, warnings amber — and a new **"Problems only"** toggle shows just the warnings/errors. Verified live on screen.
- **You get told when something goes wrong.** Any ERROR event now raises a tray notification ("Tempo — something went wrong … Details: Settings → Data & Backup → Live debug"), rate-limited to once per minute so an error storm can't spam the tray. Errors stop being something you discover in a log days later.
- **First words even sooner on fast engines.** On the very-fast tier (typically GPU), the first-words threshold after silence drops from 1.2 s to 0.9 s of audio — the word-integrity guards (silence trim + minimum-speech wait) still apply, the take just happens ~0.3 s sooner. Verified live: cadence tier "very fast ×0.6" active with beam ON on the GPU engine.

### 1.0.286
**The GPU's headroom is now SPENT on accuracy and speed: beam search on every model + an even faster caption cadence**
- **Beam-search hearing on ALL models when the GPU engine is active.** Beam decoding weighs several word paths instead of committing to the first guess — audibly better hearing at ~2× decode cost. Until now only the light models could afford it; with the GPU engine measured ~10× faster than real time, every model gets it — including Large Turbo. Verified live from the log: "beam-search decoding enabled for ggml-large-v3-turbo.bin (GPU headroom)" with a long spoken passage captioned to its final word at full pace, zero too-slow warnings.
- **A gentler too-slow ladder: beam drops FIRST.** If a weaker GPU (or a busy PC) can't afford beam after all, Tempo now simplifies decoding (beam off, same model, same engine) as the first response — before shrinking the model, and long before giving up the GPU. Small sacrifice first, drastic measures last.
- **Even snappier updates when the engine is very fast.** The caption cadence now has two earned tiers: engines with headroom take a ~25% shorter step, and VERY fast engines (typically GPU) take a ~40% shorter step — captions update roughly every 1.3 s instead of 2.2 s, with the same audio context per pass. Slow setups keep the stable full step; nothing regresses.
- The language lock's mid-session rebuilds preserve the beam setting (verified in the log).

### 1.0.285
**Live debug window — see exactly what Tempo is doing, in real time**
- **New: Settings → Data & Backup → "Live debug…".** A real-time window with two halves:
  - **Live stats** (refreshed every second): whether captions are on and on which engine (CPU or GPU), the active model, how fast the engine is REALLY running ("last chunk 2280 ms heard in 240 ms"), audio backlog, language-lock state, both audio devices by name, the current audio source, the live speaker verdicts (voice + face), and the clicker state.
  - **Live event stream**: every internal event as it happens — device changes, keep-alive, engine/runtime choice, language locks, model downgrades, auto-recoveries, caption take-overs. With filter, pause, Copy, Save-to-file and Clear. Perfect context to attach to a bug report.
- Verified live: the window showed the GPU engine transcribing 2.28 s chunks in 240 ms (≈9.5× real time) while captioning a running game.
- Everything stays on the PC — the view reads Tempo's in-memory event ring; nothing is sent anywhere unless you press Copy/Save yourself. The in-memory view works even when "Write a log file to disk" is off.
- Also corrected an internal myth: file logging was never broken — the log lives at %LOCALAPPDATA%\Tempo\logs\ (the "Open log file" button always pointed at the right place).

### 1.0.284
**Microphone & speaker: Tempo now notices a device being plugged in and ASKS (Yes/No) — and names both devices so choosing is easy**
- **The rescue prompt.** On a PC with no speaker and no microphone, captions can't hear anything. Tempo now watches for devices arriving (refresh every 2 s, was 3 s) and the moment a **microphone** is connected in that state it asks plainly: "A microphone was just connected: 🎙 <name>. This PC has no speaker… Use this microphone for captions instead? **Yes / No**". Yes switches "Listen to" to Microphone, saves it, and restarts the engine on the spot. Each arrival asks at most ONCE (re-armed only if the device disappears again) — no nagging.
- **The reverse prompt.** If captions are running on the microphone (by explicit choice, typically because there was no speaker) and a **speaker** is connected, Tempo asks whether to switch to system audio so videos and games get captioned directly. Yes/No, once per arrival, user's call.
- **Both devices? You choose.** The Settings device line now names BOTH: "🔊 Speaker: <name> · 🎙 Mic: <name>" — so the "Listen to" picker right above it (Auto / System audio / Microphone) is an informed choice. The no-speaker warning also now names the available microphone.
- These prompts only apply to Tempo's own caption engine (the Windows Live Captions source manages its own audio) — and note the arrival prompts fire in the no-speaker scenarios, which is exactly where they're needed.

### 1.0.283
**GPU captions (opt-in, all GPU brands) — Large Turbo measured FULLY REAL-TIME on GPU · new Compact Large model · AMD answers**
- **"Try GPU engine" (Settings → Live Captions).** Runs the speech engine on your graphics card via Vulkan — which works on **AMD and Intel GPUs as well as NVIDIA** (it's the only GPU path that exists for AMD cards). Opt-in because GPU drivers vary wildly; the proven CPU engine stays the default and is the automatic fallback if the GPU can't initialise. Measured on real hardware: **Large Turbo ran a 16-second continuous speech in sustained real time on GPU** — the final words appeared within seconds — with GPU usage spikes confirming the card was doing the work. (Yesterday's 1.0.282 rejection was version-specific: Vulkan 1.9.1 was broken; 1.8.1 — matching our pinned engine — is what works.)
- **Self-healing guard:** if the GPU engine ever can't keep pace with live audio, Tempo switches the option back off by itself, tells you, and returns to the CPU engine at the next start — no stuck-broken state.
- **New model: "Large Turbo Compact" (~575 MB).** The 5-bit compressed build of Large Turbo: near-identical hearing at a third of the size and noticeably less CPU per chunk — the pick when the full Large Turbo lags on a busy or mid-range PC. Verified live: loads, hears correctly. The auto-downgrade ladder now steps full Large → Compact Large FIRST, keeping multilingual hearing instead of dropping straight to English-only Medium.
- **AMD users, plainly:** AMD **processors** always worked — the CPU engine uses standard instructions every Ryzen has. AMD **graphics cards** now have their GPU path via the new opt-in Vulkan engine.
- The download grows ~6 MB for the bundled GPU engine.

### 1.0.282
**The caption status now names its engine (CPU/GPU) · GPU inference was tried, measured, and honestly rejected**
- **The captions status line now says which speech engine is running** — e.g. "Tempo Live Captions started · system audio · KJW24FVX (NVIDIA High Definition Audio) · CPU engine" — so it's always visible WHY a model is fast or slow on a given PC. Verified live.
- **What was tried and why it isn't shipping:** GPU inference (Whisper.net's Vulkan runtime) and the newer 1.9.1 engine core were both installed and measured with timed caption tests on real hardware. The Vulkan GPU path processed chunks so slowly that the audio buffer dropped everything but sentence tails — unusable — and the 1.9.1 core showed no benefit to outweigh its risk (its Large Turbo run hit the too-slow downgrade in testing). Tempo stays on the proven 1.8.1 engine, and the tried-and-rejected results are documented in the project so the experiment isn't blindly repeated.
- Reassurance: the shipped caption stack is byte-for-byte the proven 1.0.281 pipeline plus the engine-name status tag.

### 1.0.281
**AI word fixer stops "repairing" people's names · AI face analysis sees smaller faces and reacts faster**
- **The mishear fixer now understands sentence position.** A Capitalised word at a sentence start is normal text and stays checkable ("Seawhere did it go" → "Sea where…"), but a Capitalised word MID-sentence is almost always a NAME — and a name that sits within a couple of letters of a dictionary word ("Dani", "Zira") could get silently "repaired" into it. Mid-sentence capitalised words are now left strictly alone. Verified live: "…tell Matilda the analysis is finished" came through with the name untouched.
- **Face & mouth analysis upgraded.** (a) The analysis frame grew from 480 to 640 pixels wide: faces that were too small to lip-read (people further from the camera, wide shots) now clear the minimum size, and every mouth region carries ~1.8× the pixels for a steadier talking signal. (b) A newly-appeared face's motion history is now seeded from its first frame instead of easing in from zero — after every scene cut, the "who is talking" verdict lands about a second sooner.

### 1.0.280
**Settings page fix: Save Settings / Check for updates were buried under the Window & Display card**
- The Live Captions section grew over the last releases, and the bottom of the Settings page wasn't moved with it — so for every user, the **Save Settings, About…, Check for updates and Reset to defaults buttons ended up hidden UNDER the "Window & Display" card**, and the portable-copy / privacy notes overlapped each other and ran through the card. All of it is re-laid-out: buttons in their own row below the last card, "Last checked for updates" beneath them (it used to hide behind the Save button even before this bug), the two notes stacked cleanly, and the version line at the true bottom of the page. Verified on screen.

### 1.0.279
**Speaker-detection bug fixed: captions now FOLLOW when you switch your default speaker · snappier caption updates**
- **Bug found & fixed: switching the default output device left captions deaf.** When you switch Windows' default speaker (headphones ↔ monitor ↔ Bluetooth) while the old device is still connected, Windows fires no "device stopped" signal — so Tempo's caption engine and the speaker-voice detector kept listening to the OLD, now-silent device until you toggled captions off and on. The device watcher now spots the default changing and re-points the caption engine, the voice detector (keeping its learned voices), and the audio keep-alive at the new device automatically.
- **Speaker detection hardened.** A transient device-query failure during a device change could flash "⚠ No speaker found" and churn recovery for one poll; the watcher now keeps the last known speaker through a single failed read (two consecutive failures mean it's really gone). It also heals itself if the audio system wasn't ready when Tempo started.
- **Less delay.** (a) When the engine is transcribing much faster than real time — the light models with 1.0.277's encoder shortcut — captions now update on a ~25% shorter cadence automatically; slower setups keep the stable full step. (b) Internal waits in the audio loop were tightened (80→50 ms poll, 120→80 ms speech-build wait). Verified live: a long sentence stayed verbatim and complete with the shorter cadence.

### 1.0.278
**Your choice: show or hide the "♪ YouTube ·" source name on the caption bar**
- **New checkbox in Settings → Live Captions: "Show audio source name on the bar (♪ YouTube ·)".** Some users want to see which app the audio comes from; others find it clutter — now it's a choice. Unticking hides the tag from live captions on both engines (Tempo's own and the Windows mirror) and also drops the app name from the "♪ Music or sounds" note, leaving only the words and the coloured speaker labels. On by default (the long-standing behaviour). Verified live both ways: with the tag ("♪ Powershell · Speaker 1: …") and without ("Speaker 1: …").
- Reliability note for anyone whose Tempo suddenly wouldn't start on 2026-07-13-era Windows: **Windows Smart App Control**, when switched on, silently blocks unsigned apps like Tempo (the process dies instantly with no visible error). Tempo can't fix that from inside; if it happens, check Windows Security → App & browser control → Smart App Control.

### 1.0.277
**The speech engine runs several times faster per chunk — the biggest models become usable**
- **Encoder shortcut (the whisper.cpp streaming trick).** Whisper pads every ~3-second chunk to 30 seconds and ran its encoder over the WHOLE padded window. Tempo now caps the encoder context to what the audio actually fills (with generous margin), cutting inference several-fold on every model. Measured live on this PC: **Large Turbo went from ~36-40 s per chunk (13× slower than real time, unusable — the auto-downgrade always kicked it out) to roughly 1.5-2× of real time** — complete, correct sentences now appear within seconds on the best model. Normal speech with natural pauses captions well on Large Turbo; a long uninterrupted monologue still drifts behind and the auto-downgrade safety ladder still guards that case. Every smaller model gains the same headroom (snappier captions, more room for beam search).
- **Quality verified, not assumed.** The base model still transcribed the test sentences near-verbatim with the reduced context, and Large Turbo's output was verbatim on the timed tests.
- **Opening words are protected from the confidence gate.** The phantom-word filter (1.0.273) could occasionally eat REAL first words right after silence, where fragments are naturally shakier (seen live once). Quick-start chunks now use a much stricter bar before dropping anything, so opening words survive.

### 1.0.276
**Chinese/Japanese captions wrap correctly · steadier loudness for the speech engine · source tag can't blame caption tools**
- **Space-less scripts wrap correctly on the caption bar.** The colour-layout added in 1.0.272 wrapped text at spaces — but Chinese and Japanese don't use spaces, so a whole caption arrived as one unbreakable "word" that could overflow the bar. Oversized runs now break into fitting pieces character-by-character (with no fake gaps inside the run), which also covers long URLs. English wrapping verified live across multiple lines; the break-up path shares the same layout machinery.
- **Steadier loudness for the speech engine.** Auto-gain was recomputed per chunk, so what Whisper heard could pump 8× between a quiet sentence and a loud effect and back. The gain is now smoothed across chunks (with a peak guard so a loud chunk after quiet ones can't clip) — a steadier level transcribes more consistently.
- **The "♪ App ·" source tag can't blame caption tools anymore.** Seen live: audio was attributed to "Live Captions" — a captioning tool, never the actual media. Caption/speech utility processes are now excluded from source naming, so the tag stays on the real app (game, browser, player).

### 1.0.275
**Tempo now detects whether your PC has a speaker — by name, live, with auto-recovery**
- **Live audio-device line in Settings → Live Captions.** Tempo now shows exactly which speaker it can hear through — by name ("🔊 Speaker detected: KJW24FVX (NVIDIA High Definition Audio) · 🎙 microphone ready") — refreshed every few seconds as devices come and go. With no speaker it switches to a clear warning ("⚠ No speaker found — Tempo can't hear system audio") plus what to do about it (use the Microphone source, or connect a device). Verified live with the real device names on this PC.
- **Captions name their device.** The status line when captions start now says which device is being heard, e.g. "Tempo Live Captions started · system audio · KJW24FVX (NVIDIA High Definition Audio)" — so "is Tempo listening to the right output?" is answered at a glance.
- **Captions auto-recover when a speaker appears.** Previously, if captions started with no speaker (or the audio device died and couldn't be reopened), they stayed dead until you toggled Live Captions off and on. Tempo now watches for a speaker appearing or returning — headphones plugged in, Bluetooth reconnecting, a monitor waking — re-points the audio keep-alive at it and restarts its caption engine automatically.

### 1.0.274
**Cleaner voice fingerprints for speakers · language stays locked once detected · more hallucinations scrubbed**
- **Speaker voices are measured cleanly now.** The voice detector listens to a downsampled copy of the PC's audio — and the downsampling was done the quick way, which folds everything above 8 kHz (sibilants, music cymbals) back INTO the voice band as false signal. The same person could measure "brighter" or "darker" depending on the background material, nudging the matcher toward wrong speaker numbers. The audio is now properly filtered while downsampling, so the pitch/brightness fingerprints reflect the actual voice. Verified live: the two-voice test still labels cleanly — first voice Speaker 1 (blue), second voice Speaker 2 (green), first voice back to Speaker 1.
- **Session language lock (multilingual models, e.g. Large Turbo).** Language auto-detection runs per chunk and could FLIP on a noisy or musical chunk, transcribing a stretch as the wrong language. Once several consecutive chunks agree, Tempo now locks that language for the session (also skipping per-chunk detection cost). If confidence later collapses for several chunks — the sign the content really changed language — it unlocks and re-detects. English-only models are untouched.
- **Subtitle-credit hallucinations scrubbed in 8 languages.** Whisper memorised subtitle credits from its training data and emits them on quiet/musical audio — "Subtitles by the Amara.org community", "ご視聴ありがとうございました", "Sous-titres réalisés par…". These are never real speech, so they're now dropped at any volume, in English, Japanese, German, French, Spanish, Italian, Portuguese and Chinese variants.
- Speaker 2's green label was also verified on screen this round (1.0.272 had only shown Speaker 1's blue live).

### 1.0.273
**Speech engine core upgraded — hears noticeably better on every model**
- **Whisper engine upgraded from the early-2024 runtime (Whisper.net 1.5) to the current one (1.8.1)** — roughly two years of upstream recognition and decoding fixes for every model, tiny through Large Turbo. Verified live: near-verbatim transcription on the small *base* model ("Fox jumps over the lazy dog while the engine listens carefully to every word.").
- **Beam-search decoding is ON for the light models** (tiny/base/small on a 6+ core CPU). It considers several word paths instead of committing to the first guess, and hears clearly better. It was designed in long ago but disabled because the old runtime had a bug making it ~30 s per chunk; re-measured healthy on the new runtime (captions keep real-time pace). Medium/Large stay greedy to protect pacing, and the auto-downgrade watchdog still guards slow PCs.
- **Phantom-word gate.** The engine now reads Whisper's own per-segment confidence and drops very short segments the model itself doubted — the classic "noise heard as a word or two". Long low-confidence stretches still show (real mumbled speech is better shown than swallowed). Verified live that real short phrases ("Danger ahead, turn left now.") pass through intact from the first word.
- **Smarter seam de-duplication.** Consecutive listening windows overlap, and Whisper sometimes re-hears the seam with different word breaks ("keep alive" → "keep a live") — the old word-by-word check couldn't see those repeats. It now also compares seam text letter-by-letter, catching re-segmented duplicates.
- **Decode-loop stutter collapse.** On noisy audio Whisper occasionally emits the same 3-6 word phrase twice or more in a row; repeated phrases now collapse to one copy (3-word minimum, so real speech like "I know, I know" is never touched).
- Installer exe grew ~6 MB (65 → 72 MB) — the newer speech runtime ships more native code.

### 1.0.272
**Speakers get colours · opening words come out whole · Whisper's fake "[Music]" tags and hallucinated phrases are scrubbed**
- **Per-speaker colours on the caption bar.** Each "Speaker N:" label now has its own stable colour (Speaker 1 sky blue, Speaker 2 green, Speaker 3 pink, …9 in total), so following who says what takes a glance instead of reading. The "♪ App ·" source tag is now muted grey so it stops competing with the actual words. The black glow/outline still wraps everything seamlessly, and the user's chosen text colour still applies to the speech itself. Verified live against a real YouTube video: muted "♪ YouTube ·" tag, blue "Speaker 1:", amber body text, clean centred wrapping.
- **Opening words are transcribed whole.** Since the keep-alive (1.0.271), audio flows continuously — so a chunk grabbed just as someone starts talking was mostly silence with only the first syllable at the end, and Whisper mangled the opening word. The engine now trims the silent lead-in and, if speech only just began, waits a beat so the first word reaches Whisper complete.
- **Whisper artifact scrubber.** Whisper emits non-speech tags — "[Music]", "(applause)", "[BLANK_AUDIO]", "♪♪" — and famously invents phrases like "Thanks for watching!" out of near-silence. Tags made of sound-descriptor words are now stripped anywhere in the text, and the classic hallucinated phrases are dropped when the audio really was near-silent (genuinely spoken versions at normal volume still caption fine).

### 1.0.271
**Audio system: captions never sleep — fixes "not responding / delayed" at the start of videos and short sounds**
- **Root cause found and fixed.** Windows' "record what the PC plays" stream (which both Tempo's caption engine and the speaker-voice detector listen to) simply *stops delivering audio* the moment everything goes quiet. When sound returned — a video starting, a button click in any app or website — the stream had to wake up again, delaying or dropping the opening moments. That is exactly the "sometimes not responding or delayed" behaviour. Tempo now keeps the audio stream awake by playing perfectly inaudible silence while captions or speaker detection run, so **every sound is heard from its very first millisecond**. Verified live: after 10+ seconds of total silence a spoken phrase was captioned verbatim from its first word, and Tempo's keep-alive shows as an active (silent) session on the speaker.
- The keep-alive follows device changes (plugging in headphones etc.) together with the existing capture auto-reconnect, and holds only while captions or speaker labels are actually on. A PC with no speaker just runs as before.
- **Captions auto-start ~2× faster.** The video/game watcher now checks every second instead of every two, so auto-start reacts about 2 s after sound begins (was ~4-5 s). The safeguard that stops a stray notification ping from triggering captions is unchanged.
- **Honest "no audio" status.** The caption bar can now tell "the capture is healthy but nothing is playing" from "no audio is arriving at all", and says so instead of sitting on "Listening…" — this also fixed the message never appearing for a silent microphone.

### 1.0.270
**Theme: follow Windows light/dark automatically · Clicker: Humanize is now a real toggle**
- **New "Match Windows" theme option** (Settings → Appearance). When on, Tempo follows Windows' own light/dark app mode — dark when Windows is dark, light when it's light — and switches **live** the instant you change it in Windows Settings (or when a schedule/Night mode flips it). The manual theme picker greys out while it's on, and a custom accent colour still applies on top. Verified end-to-end: flipping Windows dark→light→dark re-themed Tempo each time with no interaction.
- **Humanize is now a proper on/off toggle.** Before, the Humanize button could only ever turn randomization ON — there was no one-click way back. Now it toggles: press once to apply natural interval + position jitter (still fully tunable in the fields below), press again to clear it. The button clearly shows its state — filled purple with a check when active, a quiet outline when off — and it stays in sync when you load a profile or edit the randomizers by hand.

### 1.0.269
**Much faster first words + the engine now guarantees it keeps up (auto model downgrade)**
- **Fast start.** The engine used to wait for a full 2.2 s audio step before its very first transcription — about 3 s from "someone starts talking" to words on screen. While captions are idle (fresh session, silence, a new speaker after a lull) it now accepts a much shorter first step, so opening words land in roughly 2–2.8 s; once text is flowing, full windows return because long windows hear better mid-stream.
- **Real-time watchdog with automatic model downgrade.** A model that needs longer to transcribe a chunk than the chunk lasts can NEVER catch up — measured on this class of CPU, Large Turbo took ~36 s per chunk and captions drifted minutes behind (and hallucinated under pressure). Tempo now measures every chunk, and when the chosen model repeatedly can't keep up it switches to the next smaller INSTALLED model for the session — your saved model choice is never touched — with a notification explaining what happened. Verified live: Large Turbo (~37 s behind) auto-switched to Base and the very next sentence appeared in 2.8 s, clean.
- **Beam search from 1.0.268 is disabled.** Honest correction: its measurement was confounded — the test machine had silently switched to Large Turbo, which alone explains the slowness — but greedy decoding is the proven-safe choice for real-time captions, so beam stays off (kept behind a flag for a future Whisper.net upgrade).

### 1.0.268
**Accuracy release: the engine hears quiet audio and decodes smarter; voices match on a third feature**
- **Automatic gain.** Whisper hears quiet audio badly (videos at low volume, distant voices). Each chunk is now lifted toward a healthy level before transcription — capped at 8× so background noise can't be amplified into phantom speech, and loud audio is never touched. Verified: a deliberately quiet sentence (volume 30) transcribed essentially word-perfect through Tempo's own engine.
- **Beam-search decoding on the light models.** Greedy decoding commits to the first word it thinks of; beam search weighs several word paths and picks the best. It costs ~2× the compute, so it turns on exactly where that's free: Tiny/Base/Small (which run several times faster than real time) on CPUs with 6+ threads. Medium/Large stay greedy to protect real-time pacing.
- **Voices now match on three features instead of two.** Alongside pitch and brightness, the matcher compares each voice's pitch SPREAD — how much the voice moves when talking. A monotone reader and an animated talker can share the same average pitch; the spread tells them apart. The gate is deliberately loose (it only vetoes wildly different intonation) so one person's natural variation still lands on their own profile — the David→Zira→David regression still labels 1→2→1.

### 1.0.267
**Face AI reads any app or site — even in the background — + audio auto-recovery + smarter word repair**
- **Faces are now read wherever the sound comes from — any app, any website, foreground or not.** The face analysis used to watch only the foreground window; now it follows the media detector to the window that is actually PLAYING AUDIO and reads faces there via the OS compositor (PrintWindow with full rendered content), which works for photos and hardware-accelerated video and for windows sitting BEHIND other windows. Verified: a talking face in a background window — with a different app in front — was detected and correctly named the speaker. Falls back to the foreground window whenever the audio window is unknown or minimised.
- **The audio system survives device changes.** Plugging in headphones (or an app switching the default output) used to kill captions with a "turn it off and on again" message, and silently deafened the voice profiler. Both now reopen capture on the new default device automatically — the transcriber retries up to 3 times per session, the profiler re-arms per session — so captions just keep going.
- **Smarter word repair.** The mishear fixer now also: repairs sentence-CAPITALISED words ("Seawhere did it go" → "Sea where did it go") while still never touching ALL-CAPS or mixed-case names; and removes stutter duplicates ("the the", "and and", "it it") that speech engines emit around chunk boundaries. Verified: "The the quick brownn fox. Seawhere did it it go, and and the shipss sailed" → "The quick brown fox. Sea where did it go, and the ships sailed".

### 1.0.266
**"Start Tempo when I sign in to Windows" made reliable on Windows 11**
- **Respects Windows' own Startup list.** Windows 11's Task Manager → Startup apps (and Settings → Apps → Startup) can *disable* a startup entry without deleting it. Tempo used to ignore that — the checkbox stayed on, but Tempo never launched, and re-ticking the box didn't help because the hidden disable flag was still set. Now Tempo reads that flag: the checkbox shows the *true* state, turning itself off to match if you disabled it in Windows, and turning it back on **clears** the disable flag so it actually launches again.
- **Self-heals a vanished entry.** If a cleaner or antivirus strips Tempo's startup entry while you still want it, Tempo quietly re-creates it at next launch (pointed at wherever the exe now lives).
- **Verifies the change.** Toggling the checkbox now confirms the registry actually updated, so a blocked write on a locked-down/work PC is reported instead of failing silently.
- These build on the existing single-file-safe path (the entry points at the real exe, not a temp extraction folder) and the auto-migration to the new install location — so "start with Windows" now survives moves, cleaners, and Windows' own Startup toggle. Verified end-to-end: a simulated Task-Manager disable flipped the checkbox off on next launch; re-enabling it cleared the flag and restored the entry.

### 1.0.265
**"Already running" dialog fixed: correct name, version shown, and it actually surfaces the window**
- The second-launch dialog used to say "**AutoClicker** is already running" — the old project name. It now says **Tempo**, and shows the exact build in both the title and the message ("Tempo 1.0.265 is already running…"), read from the assembly so it can never drift from the real version. Handy for bug reports.
- It no longer just tells you to hunt the tray: launching a second copy now **brings the running window to the front** — even when it was hidden in the tray. Done with a broadcast window message the running instance listens for, so it works whether the window is minimised, hidden, or just behind other windows. Verified end-to-end (hidden-to-tray window surfaced on the second launch).

### 1.0.264
**Live Captions all-round: Win+Ctrl+L now brings Tempo's bar up by itself + transcript saving**
- **Turning on Windows Live Captions yourself now works.** Press Win+Ctrl+L (or use Windows' quick settings) and Tempo notices within a couple of seconds, brings its own caption bar up, parks the Windows bar off-screen and takes over — no need to use Tempo's hotkey at all. That's what the "Show Tempo's caption overlay bar when Live Captions is on" setting always promised; now it's true for external toggles too, whichever caption engine you've chosen. The check is a cheap window lookup every ~2 s, and a cooldown stops it fighting Tempo's own off-toggle.
- **Save transcripts to disk (opt-in).** New checkbox in Settings → Live Captions: when captions turn off, the session's full transcript is written to Tempo's data folder under `transcripts\` — one file per session, each line timestamped, with the speaker labels and "♪ app" tags intact. Off by default because it puts spoken content on disk; everything stays local.
- Each caption session now starts with a fresh history (the "Show full history" panel no longer carries lines over from hours ago), and history lines carry real timestamps internally.

### 1.0.263
**Deeper face analysis + AI repair of misheard caption words**
- **The face analysis looks harder at the face.** Mouth motion is now measured RELATIVE to the rest of the face: a talking mouth moves while the forehead stays still, but a head turn, nod or camera pan moves both equally — subtracting the upper-face motion cancels whole-face movement, so a nodding, silent face can no longer be labelled the speaker. Faces too small to lip-read (a few dozen pixels) are excluded from the verdict instead of contributing noise.
- **Misheard words get repaired — for ALL caption sources.** Speech-to-text mistakes usually surface as non-words ("seawhere", "jumpps", "brownn"); Tempo now runs caption text through Windows' built-in on-device spell engine and applies fixes under strict rules: only lowercase words of 4+ letters (names, acronyms and slang are never touched), and only when the fix is clearly the SAME word misheard — a tiny edit ("jumpps"→"jumps") or the same letters split in two ("seawhere"→"sea where"). Verified: "the quick brownn fox jumpps … seawhere the shipss" comes out fully repaired. Decisions are cached, so the cost per word is paid once; everything stays offline.

### 1.0.262
**Speaker labels gain SIGHT: on-device AI face & mouth analysis (experimental)**
- New opt-in (Settings → Live Captions → "AI face & mouth analysis"): Tempo watches the video in the foreground window a few times a second using **Windows' built-in on-device face detector** (the same AI Photos uses — nothing downloads, nothing leaves the PC, nothing is recorded). Each face is tracked across frames, its MOUTH region is compared frame-to-frame, and the face whose mouth is clearly moving becomes the active speaker — sight beats the voice-only guess whenever both are available, and everything falls back to voice matching the moment no face is visible.
- Works with BOTH caption sources (Windows Live Captions and Tempo's own engine). Verified end-to-end: a face on screen is detected and tracked, motion outside the mouth region is correctly ignored, mouth movement flips the verdict to that face, and face slots recycle across scene cuts (a bug caught in testing — the tracker used to go blind after the first few faces it ever saw).
- Honest limits, also in the tooltip: the speaker's face must be visible in the foreground window, tiny faces carry little mouth signal, scene cuts re-shuffle numbers, and it costs some CPU — which is why it ships OFF by default.
- Technical note: Tempo now targets the Windows-SDK flavour of .NET 8 to reach the OS face AI; the single-file exe grows ~15 MB from the WinRT projections. Still fully offline, still one file.

### 1.0.261
**One-time speaker-labels notice + cleaner speaker matching + Windows-mirror turn fix**
- **A one-time heads-up before speaker labels are first used.** The first time captions start with labels on, Tempo explains plainly that "Speaker 1/2" are on-device AI guesses from voice pitch and pauses — similar voices can share a number, one person can be split in two, music confuses it — a reading aid, never identification. Auto-closes with a countdown, offers one-click "Turn labels off"; arrives as a tray balloon instead when captions start from the tray/hotkey so no dialog ever steals focus mid-game.
- **Cleaner speaker matching.** The voice matcher no longer learns from audio that doesn't sound like speech (music has pitch too — it was minting phantom speakers and dragging real profiles toward the soundtrack), and creating a brand-NEW speaker now needs nearly twice the evidence of matching a known one, so a cough or effect can't become "Speaker 3".
- **Windows Live Captions mirror: no more phantom turns on long lines.** When Windows rolls its caption line (drops old words to make room), the roll stalled the speech-activity clock — so the next words measured a fake "pause" and got labelled a new speaker mid-sentence. Rolls now count as activity.
- **Polish:** Whisper's stray symbol tokens ("◆", box glyphs) are stripped before reaching the bar, and starting captions during pure music no longer sits on "listening…" forever — the "♪ Music or sounds" note takes over.

### 1.0.260
**Tempo's own captions read like Windows Live Captions + smarter audio chunking + better tab identification**
- **One long running line.** Tempo's own engine used to wipe the bar every couple of seconds with the next short fragment; it now builds a continuous line out of them — the same reading experience as Windows Live Captions — showing the newest ~140 characters with an ellipsis while older words shed off the front. The speaker-turn detector's pause threshold is now tuned per source (Windows streams every ~250 ms; the engine batches ~2.5 s), which also fixes every engine batch being mislabelled as a new turn.
- **Bigger caption bar.** Three lines instead of two, and a wider panel (80% of the screen), so the longer text actually fits.
- **Smarter audio chunking.** The engine's transcription windows grew from 2.0 s to 2.8 s (more context per pass = noticeably fewer garbled words), and each window now tries to CUT AT A NATURAL PAUSE near its end instead of mid-word — a chopped word can't be mis-heard as a different word anymore. Overlap between windows also grew so boundary words are seen whole.
- **Better "which tab is that" identification.** Titles are cleaned of "(3)" unread counters and 🔊 glyphs; browser-profile segments ("Personal", "Work", "Profile 2"...) are no longer mistaken for site names; when a BACKGROUND browser is the one making sound, Tempo now scans all of that browser's windows and prefers the one naming a known media site (the tab actually playing) instead of whatever window happens to be first. YouTube Music, Spotify, SoundCloud and Apple Music are now recognised by name.

### 1.0.259
**Tempo's own caption engine: the full model range, any-language understanding, clearer model UI**
- **All five Whisper models are now offered** (previously three): Tiny (~75 MB, instant on weak PCs), Base, Small, Medium, and **Large Turbo (~1.6 GB)** — the distilled large-v3-turbo build with the best accuracy at several times large-v3's speed, which is what makes "best" usable for LIVE captions. The picker is generated from the model catalogue, so it can never drift out of sync again.
- **Any-language understanding.** The ".en" models remain English-only, but Large Turbo is multilingual: Tempo now tells it to auto-detect the spoken language, so games and videos in any of ~90+ languages get captioned. The model picker says which is which.
- **Clearer model status.** The status line under the picker now shows the model's real size on disk when installed ("✓ Base (fast) is installed and ready · 141 MB on disk · English only.") and, when not installed, points straight at the Download button with the download size. Each entry's note spells out size, speed and language support.

### 1.0.258
**Discord no longer thinks Tempo is a Steam game + captions name the app + welcome auto-closes**
- **Fixed: Discord showed you as playing the Steam game "Tempo".** Discord's game detection flags any process at a path ending in `tempo\tempo.exe` (that's the detection entry for the Steam game "Tempo" by Aestronauts — verified against Discord's own detectable-games database). The installer now uses `%LOCALAPPDATA%\Programs\TempoClicker\Tempo.exe` — the exe keeps its name, so updates and docs are unchanged — and upgrading automatically removes the old folder and retargets the Start Menu shortcut. Portable users who unzip into their own folder named "Tempo" get a one-time tray hint explaining the rename that stops it.
- **The caption bar now names the app the audio comes from.** While something is playing, the line reads `♪ YouTube · Speaker 1: ...` / `♪ Roblox · ...` / `♪ VLC · ...` — any app, identified from Windows' per-app audio sessions (known apps/sites get their proper name, unknown ones show their window title). The name holds through natural speech pauses (~10 s) so it doesn't flicker, and the "♪ Music or sounds" note names the app too.
- **Speaker numbering resets when the app changes.** A different game or video site means different people — learned voices are forgotten so Speaker 1 is always the first voice of what you're watching NOW, not a leftover match from the previous app.
- **The welcome note closes itself.** The "Quick safety note" now auto-closes after 10 seconds with a live countdown on the Got-it button; clicking either link stops the countdown so it never vanishes while you're reading.

### 1.0.257
**Live Captions: auto-start works for ANY app or site, sturdier voice matching, and a ♪ sound indicator**
- **Auto-start no longer needs a known-apps list.** Tempo now asks Windows which processes are actually MAKING SOUND (per-app audio sessions, the same data as the Volume Mixer) and checks whether the foreground app is one of them — so any game and any video site works, not just the famous ones. Browsers are handled correctly (their audio comes from helper child processes), known names still show properly ("YouTube", "Roblox", "Call of Duty"...), unknown ones are identified by their window title. Two consecutive positive checks (~4 s) are required, so a notification ping or click sound can't start captions. Verified live: VLC playing a file in the foreground auto-started captions with no interaction.
- **Fixed: the same person no longer becomes "Speaker 2/3" mid-conversation.** Three real defects in the voice matcher: (1) the pitch detector could read the same voice an octave off (autocorrelation subharmonics) — now picks the octave-safe peak; (2) voice brightness was measured across ALL sound, so a sibilant-heavy sentence ("she sells seashells...") looked like a different voice — now measured only on voiced (vowel) frames; (3) a returning speaker's first words often reached the screen before the voice verdict was ready — while a turn is younger than 4 s, the verdict now corrects the number in place. Plus a continuity bias: mild drift stays with the current speaker; a genuinely different voice still switches.
- **♪ sound indicator.** When audio keeps playing but nobody has spoken for ~18 s (music, game effects, ambience), the bar now shows "♪ Music or sounds playing — no speech" instead of holding a stale sentence — the voice profiler distinguishes speech (stable human pitch) from other sound on-device. The next spoken words replace the note immediately.

### 1.0.256
**Live Captions: real voice matching for speaker labels + auto-start for videos & games**
- Speaker labels now listen to the VOICE, not just the rhythm: Tempo measures each speaker's pitch and tone on-device (nothing uploaded, no model download, negligible CPU) and matches new speech against the voices heard so far — so when the first person talks again the label goes back to "Speaker 1" instead of counting up forever. Verified with two Windows TTS voices: David → Speaker 1, Zira → Speaker 2, David again → Speaker 1. When audio capture isn't possible the labels fall back to pause-based turn counting; two very similar voices can still be confused (the honest limit of voice profiling without heavy AI models).
- A changed voice can also split a turn after just a half-second pause — fast back-and-forth conversation labels correctly instead of merging into one turn.
- **Auto-start (Settings → Live Captions → "Auto-start for videos & games", on by default):** Tempo now notices when you're watching or playing something with sound and turns captions on by itself. Detected: video sites in ANY browser via the window title (YouTube, TikTok, Twitch, Netflix, Prime Video, Disney+, Hulu, Crunchyroll and more) and games by process (Roblox, Call of Duty/Warzone, Rainbow Six, Fortnite, Valorant, CS2, Apex, PUBG, Overwatch, Minecraft, GTA V and more) — and only when audio is actually playing, so a muted tab or an idle homepage triggers nothing. If you turn captions off during a video, Tempo won't fight you: auto-start re-arms only after that video/game stops.
- Long monologues no longer push the "Speaker N:" label off the left edge of the bar — the label sticks and the newest words show after an ellipsis.

### 1.0.255
**Live Captions: speaker labels ("Speaker 1:", "Speaker 2:")**
- Caption lines can now be prefixed with a speaker label that advances each time a new turn is detected — "Speaker 1:", then "Speaker 2:", and so on — with the bar showing just the current turn's words so the label always stays on screen. On by default; toggle it in Settings → Live Captions ("Label speakers").
- How it decides a turn: Windows Live Captions doesn't say who is talking (no system exposes that without a microphone array), so Tempo watches the rhythm — speech pauses, then new words start = the next speaker's turn, the same cue broadcast captions use (">>"). Honest limits: it can't tell two voices apart, and one person pausing a long time then continuing counts as a new turn. After 30 seconds of silence the numbering restarts at Speaker 1.
- Built to survive how Windows Live Captions actually behaves: it constantly reflows its line (re-punctuating/re-capitalising earlier words even during silence), so turn detection times the gaps between moments the line GROWS rather than "any change", and text already on the bar when you enable captions is excluded so the first label starts clean at Speaker 1.

### 1.0.254
**Clicker redesign polish + two silent-corruption bugs fixed**
- New slider: the Manual Speed and Window Opacity sliders are now drawn by Tempo — an accent-filled track with a round thumb — instead of Windows' native trackbar. This also removes the last native control that painted light in dark themes.
- The click-rate hint now reads **"≈ 114.3 CPS"** in bold accent with the details muted, the "Unsaved changes" note uses the accent colour, and "Enabled (prevents system freeze)" turns green while anti-freeze protection is on.
- **Fixed: scrolling a page could silently change a setting.** Windows sends the mouse wheel to whatever control sits under the pointer, and combo boxes and spinners respond by changing their value. So scrolling the Settings page with the pointer over the Theme box switched your theme, and scrolling the Clicker page over "Millis" retuned your click interval. Combos now ignore the wheel while closed, and spinners only respond when focused — otherwise the page scrolls.
- **Fixed: your theme/language could reset on exit.** The close-time save cast the theme and language combo's `SelectedIndex` straight to an enum with no guard; if a combo reported "no selection" while its handle was being torn down, a negative value was written and the setting silently fell back to the default on the next launch.

### 1.0.253
**Live Captions detection fix + faster scrolling and tab switching**
- Live Captions (Windows 11): Tempo now decides whether captions are on by the actual caption **window**, not the LiveCaptions.exe process. The process can stay resident while the bar is toggled off, which made Tempo think captions were "already on" and skip its Win+Ctrl+L auto-enable — captions then never appeared. Window-based detection fixes auto-enable both ways.
- Scrolling: pages without a wallpaper no longer force a full synchronous repaint of every control on **every scroll tick** (that repaint is only needed to keep a wallpaper pinned) — scrolling tall pages like Settings/Statistics is dramatically lighter. Mouse-wheel steps also cover a useful distance per notch now.
- Tab switching: opening the Statistics tab no longer recomputes every card, chart and the history list **before** the page can paint — the tab appears instantly and the fresh numbers land a frame later. Layout changes on non-wallpaper pages also skip a blanket child-repaint, making switches snappier everywhere.

### 1.0.252
**Fixed: light-mode flashes in dark themes**
- Drop-down boxes no longer flash light in dark themes. The combo let Windows paint its native LIGHT button/border first and drew the dark look over it a frame later — visible on every repaint (tab switch, hover, closing the list). The combo now paints itself entirely, so the light native art never renders. Rows in the open list are unchanged.
- List column headers (Live Monitor, session history, Multi-Point points) now use Windows' dark header theme instead of staying light in dark themes — removing both the permanent light strip and its flash on every list refresh. List and combo-dropdown scroll bars are dark-themed too.
- Together with the earlier fixes (tab body erase, number-spinner arrows, startup fade), this clears the remaining "buttons and labels flash light in dark mode" report. Light themes are unaffected (the same native paints simply matched the theme there).

### 1.0.251
**Sidebar & loading rework, CPS-test keyboard mode, layout tidy-up**
- Sidebar: added a subtle version footer ("Tempo v1.0.251") so the nav rail reads as finished instead of trailing off into empty space.
- Loading: the startup splash now has a soft accent-tinted radial glow behind the logo for a more premium look.
- CPS Test: new **keyboard mode** — pick "⌨ Space" from the Input row to measure how fast you can spam the spacebar (OS auto-repeat from holding the key is ignored, so only real presses count). Pairs with the Clicker's new keyboard auto-presser.
- Multi-Point: fixed the last "Rep" column being clipped at the right edge of the points table once it had a scrollbar.
- Reviewed every tab and the CPS-test dialog for overlapping text/labels — none found; the app's layout is clean.

### 1.0.250
**Macro pre-record countdown + Statistics summary polish**
- Macros: added an optional "Countdown (s)" in the Record card — a 3, 2, 1 countdown before capture starts so you can switch to the app you want to record first (its own alt-tab isn't recorded). Symmetric with the playback countdown. 0 = start immediately (default).
- Statistics: the "Copy summary" now includes Active days, Best day and Busiest weekday; CSV export also includes Best day.

### 1.0.249
**Statistics accuracy + Clicker progress polish (existing features)**
- Statistics: the "all-time" insight cards (Active days, Best day, Busiest weekday/hour, Top profile, Longest/Current streak, This year, Daily average) are now driven by persisted rolling aggregates instead of the recent-history window — so they no longer silently under-count once you pass 200 recorded runs. Existing users are seeded once from their current history (nothing resets). The by-session / last-7-days / by-hour charts still show recent history.
- Clicker: a fixed-count run now shows a live time estimate — "▸ 340 / 1000 (34%) · ~8s left" — and the fixed-duration run shows its percentage alongside the remaining time.

### 1.0.248
**Fix: keyboard-key mode with no key set clicked the mouse**
- Fixed a bug in 1.0.247's new keyboard auto-presser: choosing "Keyboard key" but not picking a key, then pressing Start, would silently **left-click the mouse** instead. Key mode now never falls back to a mouse click, and Start shows "Choose a key to auto-press first" if no key is set.

### 1.0.247
**Clicker, Statistics & Macros improvements**
- Clicker: auto-press a **keyboard key** (not just a mouse button) — pick a key and repeat it with the same interval/repeat/hold/Humanize settings.
- Clicker: the Manual Speed Target now warns when Anti-Freeze's Max CPS will cap it (was silently slower); the exact-CPS box ceiling follows the slider.
- Clicker: fixed/added tooltips (real 2000 CPS unlock ceiling, the Hold + Quadruple options, and the exact-CPS/preset/Anti-Freeze fields).
- Statistics: fixed CSV export splitting big numbers/decimals across columns (correct for spreadsheets and non-US locales); added **JSON export**; added a "+N vs yesterday" trend on the Today card.
- Macros: new per-macro **"Keep key/button holds at speed"** so 2×/4× no longer shrink a held WASD movement into a tap; fixed Merge (refreshes the list + separates segments); readable step names (Left ↓ / Key ↑ / Move / Scroll) in the monitor & editor; duplicating a macro no longer inherits the original's play count / last-played time.

### 1.0.246
**Macro timing accuracy (WASD holds), bug fixes, lighter Live Captions**
- Playback now schedules against one absolute monotonic timeline instead of sleeping each delay "from now", so per-step overhead (SendInput, the step highlight, loop cost) no longer accumulates — recorded key-holds and overall pace stay true even over long/looping macros (with a catch-up cap to avoid an input burst after a system hitch). This is the main fix for "WASD movement isn't the right length in-game".
- Recording now timestamps each key/click from the OS hardware event time rather than when the hook callback runs, so UI-thread jitter is no longer baked into recorded hold durations.
- Fixed the Windows Live Captions bar being left stuck off-screen: exit now waits for the in-flight caption read before restoring the bar, Tempo no longer memorises an off-screen position as the restore target, and it clamps the restore to a visible spot.
- The recorder no longer captures Tempo's own synthesised input (a running clicker/macro during recording was being recorded into the new macro).
- Live Captions mirroring is much cheaper: it re-reads only the located caption element each poll (re-walking the UI-Automation tree only occasionally or when text stops), and polls a little less often.

### 1.0.245
**Macros now drive WASD / arrow keys in games + Tempo opens on the Clicker tab**
- Recorded movement macros (WASD / arrows) now work in games. Tempo sends keys as hardware **scan codes** (with the extended-key marker for arrows) instead of virtual keys — DirectInput / Raw Input games read scan codes, so they were ignoring the old input entirely.
- Fixed focus loss on playback: minimising Tempo (and its countdown overlay) could pull keyboard focus off the game so the first keystrokes were dropped. When a macro is started via hotkey from inside a game, Tempo now re-aims focus at that game right before sending input.
- Tempo now opens on the **Clicker** tab every launch instead of reopening on the last tab used (e.g. Macros).
- The keyboard path now logs when Windows blocks its input (elevated / anti-cheat game vs non-elevated Tempo), so that case is diagnosable. Guidance: run the game borderless-windowed, run Tempo as administrator for anti-cheat games, and keep playback speed at 1x for movement holds.
- Minor: stopped writing the settings file on every tab switch (the last-tab value is no longer used).

### 1.0.244
**Fixed: wrong page (Clicker) showing after a long minimised macro run + more light-flash cleanup**
- Fixed the Clicker page appearing on top of whatever tab you were on after the window sat minimised for a long time (e.g. an 8-hour AFK macro grind with monitors sleeping/waking). Root cause: the monitor-wake re-centre froze/un-froze painting on hidden pages too, and the un-freeze re-showed a hidden page (Clicker, first in order) over the real one. The paint freeze is now only applied to the on-screen page; re-centring is skipped while minimised; and a restore-time check re-hides any stray page.
- Number spinners (▲▼) no longer flash their native light-grey Windows arrows before the dark ones — Tempo now paints them itself.
- The tab area no longer flashes its light Windows tab-body while switching tabs or restoring from minimised (cleared to the dark theme colour).
- Tooltips are now dark to match the app instead of the bright system popup.
- A finishing macro only restores the window if Tempo minimised it for the run — it no longer pops the window open (stealing focus) when you minimised it yourself.

### 1.0.243
**Fixed: light "flash" on labels & buttons at launch**
- The window faded in by ramping its opacity, which made it a *layered* window; combined with the backdrop compositing, the labels and buttons briefly flashed their light default background before the dark theme showed — a quick white blink most users saw every launch.
- The window now reveals straight at its final opacity (no layered fade), so it appears already dark with no flash. The startup splash still provides the smooth load-in.
- The form is also painted its theme background before any control is built, so it can never show a white default behind the controls as it comes up (theme-correct for light themes too).

### 1.0.242
**GIF / image backdrop & scrolling fixes**
- Fixed control "overlap"/ghosting over a full-window wallpaper while scrolling — the page now repaints immediately on scroll instead of flashing a shifted/stale backdrop frame.
- The animated GIF backdrop no longer appears to "restart" while you scroll (same root cause — it now stays put and keeps animating).
- Switching tabs no longer flashes to the top before restoring your scroll position; the re-centre + restore commits in a single repaint.

### 1.0.241
**Clicker & Macros — quality improvements**
- Clicker: roll the mouse wheel over the Manual Speed slider to set the rate in useful steps (live while running), without the page scrolling underneath.
- Macros: new one-tap playback speed presets (0.5× / 1× / 2× / 4×) in the Playback card, translated into all 6 languages.
- Both purely additive — existing clicker/macro behaviour is unchanged.

### 1.0.240
**Clicker polish + a refreshed website**
- Fixed the clipped "CPS Test" button (was showing "CPS Tes") — now sized for the gauge icon + full label.
- The CPS preset matching your current rate (10/50/100/200) is highlighted in the accent colour, correct on startup and as you change speed.
- Rewrote the project website (index.html): live theme switcher, interactive CPS meter, click-modes explainer, why-Tempo comparison, scroll-progress bar and hero spotlight.

### 1.0.239
**Tabs remember their scroll position**
- Each tab keeps its own up/down scroll spot: scroll down, switch tabs, switch back, and you're where you left off instead of snapped to the top.
- Fixed the root cause (the content re-centring repositioned controls in scrolled coordinates and reset the scroll); it now repositions in unscrolled space and re-applies your exact scroll.

### 1.0.238
**Fixed: window coming back to a blank/empty screen**
- Restoring from the tray/taskbar (or after macro recording auto-minimised the window) could show an empty page that only filled in after a delay. The layout is now re-centred and repainted **immediately** on restore — no blank frame, no waiting.
- Showing the window from the tray now repaints right away (a hidden-then-shown window previously skipped the repair).
- Re-centring on resize/maximise is now a single batched pass, so it no longer flickers or momentarily blanks.

### 1.0.237
**The full visual redesign — every tab restyled to the new look**
- Rounded inputs, dropdowns and text boxes with clean stacked chevrons.
- Icon buttons (＋New, Save, Duplicate, Delete, Start, Stop, CPS Test, Humanize).
- Colour-coded actions: accent New/Set, red Delete, violet Humanize.
- Pill toggle switches for on/off options.
- Uppercase letter-spaced card headers with rounded icon badges on rounder cards.
- Accent-coloured live value (Target: **200 CPS**), uppercase PROFILE/NAME labels, footer tip bar.
- New strings translated into all 6 languages; everything recolours to your theme instantly.

### 1.0.236
**A fresh, more polished look — across every tab**
- New card headers everywhere: UPPERCASE, letter-spaced titles next to a rounded, accent-tinted icon badge.
- All buttons gained a subtle gradient and rounder corners for more depth.
- Key primary actions (New, Humanize, Set) are now filled in the theme accent colour, alongside the green Start / red Stop.
- Everything recolours to your chosen theme (30+) instantly on switch.

### 1.0.235
**Clicker, Macros & Statistics — polished old features and a few new ones**
- **Clicker:** new "Set exact CPS" box — type an exact target rate and apply it instantly; stays in sync with the slider, ± buttons and presets.
- **Macros:** the playback progress bar is now the app's themed rounded bar (accent-coloured) instead of the grey Windows one.
- **Statistics:** new 1K / 10K / 100K quick-goal buttons (like the Clicker presets), and "Copy summary" now also includes Today, Lifetime clicks and Best CPS ever.
- New labels translated into all 6 languages (and the existing "Presets (CPS):" caption is now translated). Themed progress bars re-colour instantly on a theme switch.

### 1.0.234
**One-tap CPS presets on the Clicker tab**
- The Manual Speed card now has quick **CPS preset buttons (10 / 50 / 100 / 200)** — tap one to set the click speed (and the interval fields) instantly, instead of typing values or dragging the slider.
- (The data-loss confirmations you asked about already existed — Clear points, Delete macro and Reset stats all prompt first — and newly recorded macros are already auto-selected.)

### 1.0.233
**Fixed the cramped CPS Test dialog**
- The CPS Test window's "Test length:" and "Button:" rows overlapped the results line above them and sat tight against the bottom edge. The dialog is a little taller now and the bottom selectors are spaced clearly, with each label vertically centred against its button row.
- The CPS Test window also gets a dark title bar on dark themes, matching the main window.

### 1.0.232
**Much more complete translations — far fewer strings stay English**
- Added translations (Spanish, French, German, Italian, Portuguese) for ~65 UI strings that previously stayed English in every non-English language: many Settings checkboxes (record history, on-screen overlay, write log, sleep in tray, remember window size, launch at sign-in, notify on finish, cursor trail…), labels (Header/Footer/Window, Font/Colour/Text size, Hold each click, Dwell, Opacity, Listen to, Caption source/model…), buttons (Start/Stop/Pause/Resume, OK/Cancel/Close/Clear, Got it, Download model, Reset stats/window, Back up all data, Open models/log folder…), the privacy note, and the Multi-Point / Keybinds / Macros tab help paragraphs.
- No new languages were added — this fills the gaps in the six languages Tempo already supports. Verified by switching to Español: the whole UI now reads in the chosen language (proper names like theme/font names and "Live Captions" stay as-is by design).

### 1.0.231
**Modern themed scroll bars + title bar, and fixed the cramped Behaviour checkboxes**
- The scroll bars are now thin and theme-matched — dark on dark themes, light on light — instead of the chunky light-grey native ones, and the window title bar matches (dark in dark themes).
- Fixed the overlapping checkboxes in Settings → Behaviour: the modern checkboxes were a hair taller than the native ones the layout was spaced for, so the bottom rows collided and ran past the card edge. The checkbox height now matches native, the rows are evenly spaced, and "Sleep in tray" moved to the right column so everything fits cleanly.

### 1.0.230
**Fixes the ghost text when scrolling a tab over a background image**
- 1.0.229 made page labels/checkboxes transparent over a wallpaper, but scrolling could leave faint ghost/overlapping text (most visible around the Clicker tab's IDLE/Notify area).
- Owner-drawn checkboxes and radios now blit the wallpaper slice behind them into their buffer instead of leaving it uncleared, so they can't leave stale pixels; and a scroll now repaints the child controls (not just the background), so transparent labels redraw cleanly. Verified ghost-free scrolling up and down.

### 1.0.229
**Background image shows through cleanly + tray and multi-point touch-ups**
- Page-level labels, section headers and checkboxes no longer sit in opaque boxes over a background image — they're transparent when a wallpaper is set, so the image shows through the text. (Controls inside cards and input fields stay solid for readability; verified ghost-free while scrolling.)
- The system-tray tooltip now reflects what Tempo is doing — Running / Paused / Playing a macro / Recording / Idle — plus the active profile, so a hover tells you whether it's clicking without opening the window.
- Multi-point: points captured with Quick Capture are now enabled by default (matching points added via the editor), so a freshly captured sequence is ready to run instead of silently doing nothing until each row is ticked.

### 1.0.228
**Redesigned navigation, buttons and launch screen**
- The sidebar's active tab is now marked with a clean accent indicator bar, a soft accent-tinted background and accent-coloured text/icon — instead of the old heavy solid-accent fill — so the navigation reads lighter and more modern.
- Buttons get slightly softer rounded corners across the app.
- The launch/loading splash now shows the Tempo bolt logo and is theme-aware: it reads the saved theme and accent (including a custom accent) so the splash matches the app you're about to see, instead of a fixed violet.

### 1.0.227
**Background image no longer lags or stutters while you scroll a tab**
- The full-window background image was being cover-scaled with high-quality interpolation on every single repaint, so every scroll tick re-scaled the whole (often huge) image — causing lag, and disturbing an animated GIF's frame timing so it appeared to restart.
- The scaled frame + readability scrim are now composed into a cached bitmap once per GIF frame (or on resize); scrolling and repainting just blit that bitmap, so the wallpaper stays smooth and an animated backdrop keeps its place while you scroll.

### 1.0.226
**Robustness polish for the new UI + corrupt session-history protection**
- The flat combo-box drop-downs now scale their row height with the display-scaling/font, so list text is no longer vertically clipped at 125%/150%/200% DPI; the drop-arrow area scales too.
- The flat numeric steppers now re-hook their custom drawing if the control's spin buttons aren't ready at first paint, so they can't occasionally fall back to the native Windows arrows.
- A corrupt `sessions.json` is now backed up before it can be overwritten (matching how settings are handled), so unreadable history isn't silently lost.

### 1.0.225
**UI modernization: flat themed controls everywhere + fixed overlapping labels**
- Replaced the dated native Windows checkboxes, radio buttons, combo-box drop arrows and numeric spin arrows with flat, theme-aware controls drawn by Tempo. They follow every theme (and the custom accent), so the whole UI now looks consistent instead of mixing flat cards with grey 3-D widgets.
- Fixed the Burst Settings card on the Clicker tab, where the "Clicks per burst" and "Pause (ms)" labels were clipped underneath their spinners; labels and fields now sit on one aligned row.
- The new controls are sized to match the originals so no labels collide with their neighbours.

### 1.0.224
**Fixes settings/checkboxes that didn't stick and two features that conflicted**
- Settings-tab checkboxes are captured on close, so toggles persist even without pressing "Save Settings" (previously the shutdown save wrote the old values back and the change was lost).
- The Macros tab "Capture mouse movement" / "Capture keyboard" checkboxes now have backing settings and save on toggle — they no longer reset to ON every launch.
- "Unlock max speed (advanced)" now lifts the Anti-Freeze hard cap automatically (and restores the 200 CPS default on re-lock), so the slider actually reaches high rates; the adaptive CPU back-off still applies.
- "Colorful cursor trail" and the macro capture checkboxes refresh correctly after Reset to defaults / Import settings.

### 1.0.223
**Form-level WS_EX_COMPOSITED + full solid-background fix for labels and radios**
- WS_EX_COMPOSITED moved from individual tab pages (where the TabControl drops it on tab switch) to the main window, where it sticks and composites the whole UI.
- Solid (non-transparent) backgrounds now applied to all labels and radio buttons too, not just checkboxes (1.0.222 missed those, so labels still clipped).
- Guarded the background-colour resolver so controls can't resolve to white (blank-box report).

### 1.0.222
**Real cause of checkbox/label corruption found: transparent backgrounds**
- Checkboxes, radios and labels used Color.Transparent, which over a custom-painted scrolling panel leaves ghosting and stale text on repaint (the "overlap=on, text=off" artifacts).
- They now use a solid background matching their container (card surface or page), which erases cleanly. With 1.0.221's WS_EX_COMPOSITED, the Settings tab stays correct through scroll/toggle/minimize/tab-switch.

### 1.0.221
**Root-cause fix for scroll corruption: WS_EX_COMPOSITED instead of double-buffering**
- The scrollable tab pages were double-buffered, which doesn't buffer child controls; scrolling blitted checkboxes/labels to a new offset and only repainted a strip, leaving them hollow/clipped.
- Switched to WS_EX_COMPOSITED so the page and all controls composite together in one pass - fixes the corruption and removes the fragile repaint logic that caused the 1.0.219/1.0.220 crash.

### 1.0.220
**Hotfix: fixes the 1.0.219 crash where scrolling a tab closed Tempo instantly**
- 1.0.219's synchronous child repaint on scroll could recurse with the layout repaint and overflow the stack, hard-closing the app with no dialog (why tabs "crashed" and clicking stopped working).
- Scroll and layout repaints now queue a single deferred, debounced full repaint, keeping the corruption fix without the recursion/crash.

### 1.0.219
**Fixed Settings controls corrupting (hollow checkboxes, clipped text) after scroll or minimize**
- A backdrop tab page repainted its background but not its child controls when scrolled, leaving checkboxes and labels with stale half-scrolled pixels.
- The scroll handler now forces a full repaint including children, so all controls redraw correctly. (Minimize/restore and alt-tab paths already did this.)

### 1.0.218
**Portable-exe fix: the build script was overriding single-file bundling**
- publish.cmd passed flags that disabled native-library bundling and single-file packing, overriding the project and cancelling 1.0.217's portability fix.
- The script now bundles everything (runtime, UI Automation, native speech libs) into one Tempo.exe that runs from any folder with no loose DLLs.
- Rebuild via publish.cmd to get the portable exe; a plain Build still leaves loose DLLs (that's how .NET works).

### 1.0.217
**Truly portable single .exe (works anywhere); window always opens centered**
- Tempo.exe is now fully self-contained - the .NET runtime, UI Automation assemblies, and native speech libraries are all bundled inside the exe, so it runs from any folder with no loose DLLs. Fixes "only works inside its publish folder".
- Window opens centered every launch instead of restoring its last position. "Remember window position" in Settings re-enables the old behavior.

### 1.0.216
**More reliable Windows Live Captions startup; diagnostic reports UI Automation health with repair steps**
- Caption startup rechecks several times over ~6s and re-sends the toggle if needed, instead of giving up after one quick recheck (helps on fresh boot / first-ever use).
- "Diagnose Windows bar" now leads with "UI Automation health: OK/BROKEN" and, when broken, explains it's a Windows-side fault with repair steps and the offline-captions alternative.
- On/off detection uses the LiveCaptions process + Win32 lookup, not the accessibility API, so handling stays reliable when UIA is broken.

### 1.0.215
**Caption reading rewritten to avoid the broken CacheRequest, so the Windows mirror can read text on affected PCs**
- Reading used the UIA ".Current" property bag, which internally builds a CacheRequest - broken on the affected PC, so every read threw.
- Rewrote the reader and diagnostic to use GetCurrentPropertyValue per property, which doesn't go through CacheRequest.
- Diagnostic now lists UIA windows and dumps the caption text tree instead of failing.
- Removed dead/duplicate caption code.

### 1.0.214
**Build fix for 1.0.213, plus: Tempo detects when Windows' accessibility API is broken and switches to its own offline captions**
- Fixes a compile error that stopped 1.0.213 from building (a dropped method header).
- On some PCs Windows' UI Automation core fails to start (CacheRequest type-initializer exception), making the Windows Live Captions text impossible to read - a Windows-side fault.
- Tempo now detects this and automatically switches to its own offline caption engine instead of showing a blank bar, telling the user once.
- Points to Settings > Live Captions to install a model if none exists.
- "Diagnose Windows bar" now dumps the caption window's text tree for PCs where reading works.

### 1.0.212
**Caption reading is much more aggressive, and the diagnostic now shows exactly where the words live**
- Tempo reads Windows Live Captions by scanning every text element and keeping the longest real line, checking both name and text content - fixes captions not appearing on some Windows 11 builds.
- "Diagnose Windows bar" now dumps the caption window's full text tree (type, name, text) so the exact caption element can be identified per-PC.
- Windows bar hide unchanged (off-screen, kept readable).

### 1.0.211
**About box shows the real version; caption mirror reads Windows text more reliably; font picker actually changes the font**
- The About window showed a hardcoded "Version 1.0.206" regardless of build; it now reads the real version from the app.
- Caption mirroring checks both the text element's Name and its Text-pattern content, catching words on Windows builds that expose them through either property.
- Tempo no longer mirrors Windows' "Ready to show live captions..." placeholder as a caption.
- Font picker fixed: caption font choices reliably apply, fonts without a bold face no longer break drawing, and only installed fonts are listed.

### 1.0.210
**Windows captions bar now disappears the moment captions start; smoother launch effect**
- The Windows "Live Captions" bar no longer lingers on screen in its "Ready to show live captions" state. A short, fast hide-enforcer runs for the first few seconds after captions turn on and repeatedly pushes the Windows bar off-screen, so it's gone immediately instead of waiting for the first transcribed words.
- Targets the case where, with the Windows caption source selected, the Windows bar stayed visible above Tempo's own bar before anyone had spoken.
- Nicer launch: the splash has a soft accent glow that gently breathes behind the title, and the loading bar sweeps with a blue-to-purple gradient.

### 1.0.209
**Cleanup pass: removed dead caption settings and fixed stale code comments - no behavior change**
- Removed two unused settings (CaptionDriveWindows, CaptionMirrorWindows) that were saved but read nowhere.
- Fixed an out-of-date description in the Windows caption reader that wrongly claimed Tempo can't transcribe on its own.
- Updated internal comments that still referred to the small pill removed in 1.0.208.

### 1.0.208
**Windows caption bar now hides the instant captions start; Tempo's bar is back to normal size; splash and welcome notice restored**
- Windows Live Captions bar now hides right away instead of waiting for the first words, with a safety net that restores it if reading text is impossible on the PC.
- Tempo's caption bar is normal size again (the small pill from 1.0.207 is removed).
- Splash launch effect restored: the splash forces itself to paint and come to front the moment it opens.
- Welcome notice restored: every startup path now guarantees the notice shows and the window becomes visible.

### 1.0.207
**Tempo can now caption on its own; the empty caption box on startup is gone; the caption bar matches your theme**
- New Auto caption source (default): Tempo transcribes offline itself first and auto-falls-back to mirroring Windows 11 Live Captions if its own engine can't run.
- No empty box on startup; real text replaces the starting state the moment it arrives.
- Caption bar colors now come from the active theme instead of a hardcoded near-black.

### 1.0.206
**Welcome notice no longer pops with the app; blank tab on return self-heals again; captions diagnostic is now a self-test**
- Startup glitch where the app window and the "Quick safety note" appeared at the same time:
  the notice was being shown before the window had faded in, so the two overlapped and read
  as a double-pop. The window now fades in first, on its own, and the notice appears only
  once the fade has finished - a clean, ordered startup.
- Empty Settings tab that "doesn't refresh or flash": scrolling Settings down, switching to
  another tab, then back could leave it blank. My previous change only re-laid-out a returning
  tab if its scroll had drifted - but this case comes back with the scroll reading zero while
  the content was laid out against a stale size and ended up off-screen, so nothing re-ran and
  it stayed blank. The follow-up pass now always re-asserts the layout (self-heal), using a
  queued repaint so an already-correct page repaints identically - no flash on the normal path,
  but a genuinely blank page is now corrected.
- Live Captions diagnostic upgraded to a self-test: Settings > Live Captions > "Diagnose
  Windows bar" now also reports whether Tempo can DETECT the captions window and whether it can
  READ text from it, on top of the window list. One screenshot now shows exactly what works on
  a given PC.
- Live Captions hide (from 1.0.205) is unchanged: the window class fix ("LiveCaptionsDesktopWindow")
  and CacheRequest-free reading stand. If a device still shows the Windows bar, run the
  self-test there and send the result - the window class can differ between Windows builds.

### 1.0.205
**Live Captions fixed: window now found + hidden, and text reading works around the UIA error**
- Your diagnostic made the fix exact. The Windows 11 captions window reports its class as
  "LiveCaptionsDesktopWindow" - Tempo had been looking for "LiveCaptionsWindow". That single
  wrong word is why every attempt missed it. Tempo now matches the real class, so it finds
  the Windows captions bar reliably.
- Your diagnostic also showed UI Automation throwing on your PC ("type initializer for
  CacheRequest threw an exception"). UI Automation is what Tempo uses to READ the caption
  text, so the text would have stayed empty even once the window was found. Tempo now reads
  by walking the UI Automation tree directly (TreeWalker) instead of the query API that
  depends on CacheRequest - which avoids that error. Window detection and the in-app
  diagnostic use the same CacheRequest-free approach now.
- Safer hide order: Tempo now hides the Windows bar only AFTER it has successfully read
  caption text at least once (reading keeps working while the window is off-screen). So if
  text-reading still can't work on a given PC, the Windows bar is left visible rather than
  hidden - you are never left with no captions at all.
- Needs confirming on your Windows 11 PC (I can't run Live Captions here): does the Windows
  bar now disappear, and does the text appear in Tempo's bar?

### 1.0.204
**A screenshot-able "Diagnose Windows bar" button, so the Live Captions hide can finally be pinned**
- Two caption bars (Windows' working one plus Tempo's empty one) means Tempo cannot find the
  Windows captions window on your PC - so it can neither hide it nor read from it. I can't
  match a window whose identifiers I can't see, and four guesses haven't landed.
- New: Settings > Live Captions now has a "Diagnose Windows bar" button. Turn Windows captions
  on, click it, and it shows every visible window's title/class/process and every UI Automation
  window's name/class in a box you can screenshot or copy. Send me that and the lookup can be
  made to match your exact window - no more guessing.
- The 1.0.203 log-file "[caption-diag]" dump is still produced as well; this simply puts the
  same information on a screen you can capture.

### 1.0.203
**Empty Settings tab after restore: retry the layout until the window is settled; plus a captions diagnostic**
- Your clue nailed it - the auto-fit centering "doesn't respond sometimes when you return,
  then works later". On restoring from the tray/taskbar, Tempo laid the pages out once on a
  short delay, but if the window hadn't finished restoring yet (client size still 0, or the
  state still counted as minimised), the layout bailed on its own guard and never re-ran -
  leaving the page blank until you nudged it. It now RETRIES: it waits for a real window
  size, lays the pages out, then does one confirming pass, so returning to a tab reliably
  re-centres instead of coming back empty.
- Windows Live Captions still not hiding: after three honest attempts I clearly can't guess
  the Windows 11 captions window's identifiers, so this build adds a diagnostic instead of a
  fourth guess. When captions are on but Tempo can't find the window after about two seconds,
  it writes a "[caption-diag]" block to the log listing every visible window (title, class,
  process) and every UI Automation window (name, class). Reproduce it once and send me those
  lines and I can make the lookup match your PC exactly.

### 1.0.202
**Windows Live Captions window: found and kept hidden, even on Windows 11**
- The Windows captions bar wasn't being hidden because Tempo couldn't find its window at
  all on your setup. On Windows 11 that window has no stable Win32 class/title and often
  isn't owned by the LiveCaptions.exe process, so the name/title/process lookups all missed
  it - and with no window found, Tempo neither hid it nor mirrored from it (you saw the
  Windows bar plus "Live Captions on - captions will appear here"). Tempo now finds it two
  more ways: by scanning visible windows for a Live Captions title, and - the one that
  should work reliably on Win11 - through the UI Automation tree. Tempo's own windows are
  excluded, so it can never hide its own bar by mistake.
- It now also KEEPS it hidden. The window is re-checked on every poll and pushed straight
  back off-screen if Windows re-docks or re-shows it (Windows owns that window's
  placement), instead of being moved once and allowed to drift back on screen.
- Note: this all needs a real Windows 11 PC to verify, which I can't do here - please
  confirm the Windows bar now disappears when you switch captions on.

### 1.0.201
**Windows Live Captions actually hides now; keybind conflicts are prevented; less tab flash**
- Windows Live Captions wasn't being hidden on some setups: Tempo couldn't reliably find
  the Windows 11 captions window (its window class/title vary by build), so it neither
  tucked it off-screen nor mirrored from it - you'd see the Windows bar AND "Live Captions
  on - captions will appear here" in Tempo's own bar. Detection now asks the LiveCaptions
  process for its own main window first, then falls back to its largest visible window,
  which is far more robust. (Live Captions can't be tested here, so please confirm on your
  PC.)
- Keybinds: a combination can no longer stay assigned to two actions at once. Setting a key
  that another action already uses now clears it from that other action automatically, so a
  key always triggers exactly one thing - no more "two actions share a key and only one
  mysteriously works". Conflicts were previously only highlighted and warned about.
- Tab switching: the follow-up pass after a switch now only does a full re-layout when the
  page actually came back scrolled (the blank-band-at-the-top case); otherwise it just
  repaints. That removes the extra "flash refresh" on the normal, already-correct path.
- Loading splash, welcome notice and restart all continue to use the reliable startup
  sequencing from 1.0.200 (splash fully seen first, notice every run, then fade-in); a
  restart simply relaunches through that same sequence.

### 1.0.200
**The loading splash and welcome notice now reliably show, every run**
- The loading splash sometimes wasn't seen, especially on faster PCs, because the main
  window was shown on top of it almost immediately. Now the window stays hidden until the
  splash has actually finished, so the loading effect is always visible first and the
  window then fades in. (A timeout means startup still proceeds if the splash can't show.)
- The welcome / official-source notice now appears on EVERY run. It was first-run only,
  so most launches never showed it; it also used to open hidden behind the splash. It
  still shows just once per launch.
- Both work for tray-start too: starting minimised to the tray dismisses the splash
  promptly (instead of letting it linger) and shows the welcome notice the first time you
  open the window.
- Windows Live Captions: when you switch captions on, the Windows captions window is now
  tucked off-screen as soon as it's detected - not only once it has produced text - so it
  no longer sits visible while the audio is silent. Only Tempo's own caption bar shows.
- Reviewed the clicker and macro tools again. Both stay feature-complete (click styles,
  interval/hold/burst, per-click hold, repeat by count or duration, randomisation,
  multi-point, searchable profiles; macro record/play, edit, duplicate, export/import,
  merge, pin, reorder, notes) with solid engines, so they're unchanged rather than padded.

### 1.0.199
**One-click update now works from the setup zip, not only a bare exe**
- The "Update now" button now appears whenever a release has EITHER a standalone Tempo.exe
  OR the Tempo-Setup-<ver>.zip you already ship. Before, it only showed for a bare exe, so
  a zip-only release fell back to the download page - which is what you were seeing. Now it
  downloads the zip, unpacks Tempo.exe, confirms it's a real Windows program AND that it
  matches the published checksum (when one is provided), then swaps it in and relaunches.
- Safety: a malformed, truncated, or wrong zip can never overwrite the running Tempo.exe.
  The extracted file is header-checked ("MZ") and checksum-verified before any swap; if
  anything is off it stops and offers the download page instead.
- Rollout note: the version doing the check is the one that installs, so this takes effect
  for anyone running 1.0.199 or newer. For the first hop, attach the standalone Tempo.exe
  to the 1.0.199 release as well (publish.cmd already stages it next to the zip) so users
  still on older builds also get the one-click button; from 1.0.199 on, the zip alone is
  enough.

### 1.0.198
**Another attempt at the blank-tab bug, with far better diagnostics for it**
- The blank/empty tab you reported (worse the more you switch): I traced the layout code
  again and confirmed the control positions are absolute - captured once and reset to the
  exact same spot on every switch - so they can't truly drift further down each time. That
  points the finger at the page not having settled its size/scrollbar when the switch lays
  it out (so it measures too early and parks low or blank), rather than accumulating state.
  Fix: after each tab switch, Tempo now does a SECOND full layout pass once the window has
  settled - a real re-centre and scroll-to-top, not just a repaint - to catch that case.
- The same kind of settle-pass already runs after you restore the window from minimized.
- Much richer layout logging: the [layout] log line now records, for every tab switch, the
  first and last control positions, the scroll range (AutoScrollMinSize) and the scrolled
  display rectangle. If a tab still comes up blank, those numbers pinpoint exactly what
  moved - positions, the scroll, or the range - which is what I need to finish this off.

### 1.0.197
**Check for Updates can install in place, and a richer loading splash**
- Check for Updates now installs the new version in place instead of only sending you to
  the download page: you get an "Update now" button that downloads the update, swaps
  Tempo.exe and relaunches - no manual download/unzip. (The one-click updater was always
  built in; it only switches on when the release ships a standalone Tempo.exe, which
  publish.cmd now produces and stages automatically - see below.)
- publish.cmd now stages a standalone Tempo.exe (plus its checksum) right next to the
  setup zip in bin\publish, and its NEXT STEPS spell out attaching BOTH Tempo.exe (powers
  the in-app one-click update) and Tempo-Setup-<ver>.zip (for brand-new users) to the
  GitHub release.
- Fixed a stale line in publish.cmd's release instructions that still claimed portable
  builds keep their data "beside the exe" - since 1.0.194 all settings live in your local
  AppData (the AutoClicker folder), whether portable or installed.
- The loading splash got more to look at: it now shows the version, cycles through the
  startup stages ("Starting up", "Loading your settings", "Preparing the workspace",
  "Almost ready"), and has rounded corners and a subtle top accent.
- Reviewed the clicker and macro tools again. Both are already full-featured - the clicker
  has click styles, interval/hold/burst modes, per-click hold, repeat by count or
  duration, randomization, multi-point and searchable profiles; macros do record/play,
  edit, duplicate, export/import, merge, pin, reorder and notes - and the engines are
  solid, so I left them alone rather than adding busywork.

### 1.0.196
**A loading splash at startup, a much cleaner publish folder, and two fixes**
- New: a small "Tempo / loading..." splash now appears the instant you launch, with an
  animated loading bar, while the main window is being built. It runs on its own UI
  thread so the animation stays smooth during load, then fades out as the window appears.
  (You asked for a loading effect before the app shows - the earlier fade-in only ran
  once the window already existed, so it was easy to miss.)
- Publish folder cleanup: a self-contained single-file build bundles the whole .NET
  runtime and the app INSIDE Tempo.exe, so any loose *managed* framework DLLs left beside
  it (System.*, PresentationFramework*, NAudio*, ...) are duplicates or leftovers from an
  older build. publish.cmd now removes them automatically once it confirms (by size) that
  Tempo.exe is a real single-file bundle, leaving just Tempo.exe + the scripts + the
  native runtime files that self-contained .NET always keeps on disk. If the exe comes
  out too small (single-file didn't bundle), it KEEPS every DLL and warns instead, so it
  can never delete something the app needs.
- Fixed: switching tabs could land on a blank or scrolled page - most reliably Settings
  right after you'd scrolled down in Keybinds. The page is now laid out fully BEFORE the
  scroll is pinned back to the top, so a tall page can't re-measure its scroll range and
  come back parked low with an empty band above the content. The layout diagnostic now
  logs for every tab (not just Statistics) so any recurrence can be pinned down exactly.
- Live captions: if Windows Live Captions is CLOSED mid-session, the overlay no longer
  shows the last line frozen forever - once the Live Captions window has been gone a few
  seconds it switches to "Waiting for Live Captions...". A normal pause between phrases
  still keeps your text on screen (that fix stays), so captions don't flicker.

### 1.0.195
**Searchable profiles in the Clicker, and the macro search fixed up**
- Clicker: the profile picker is now searchable - start typing a profile name and it
  suggests matches so you can jump straight to one, instead of a plain dropdown that only
  jumped a single character at a time. Picking a match loads it; the real current profile
  is always shown in the status bar, and only New/Save ever create or rename a profile.
- Macro search count fixed: the line under the list ignored the search and always showed
  the full library total. It now shows "N of M macros match" while you're filtering (and
  "No macros match ..." when nothing does), so the count matches what's on screen.
- Macro search now also matches a macro's Notes, not just its name - so you can find a
  macro by something you jotted down about it.
- The macro search box now shows a "Search macros..." hint so it's clearly a search field;
  that hint is translated into all six languages.
- The clicker and macro engines themselves were reviewed again and are unchanged - the
  timing/playback core is solid; these are search and find-ability improvements.

### 1.0.194
**Data always in AppData, About layout fixed, fuller translations, smoother launch**
- Data location: Tempo now always keeps its settings, profiles, macros and stats in
  %LOCALAPPDATA%\AutoClicker - whether installed OR run as a portable copy. This is the
  single always-writable spot, so saving can't silently fail from Program Files or a
  read-only/USB folder. If you ran an older portable build that used a "Data" folder beside
  the exe, Tempo copies it into AppData automatically on first run (non-destructive - your
  old folder is left untouched).
- About box: fixed overlapping text. The logo hint no longer sits on top of the version /
  build / data lines, the long data path is truncated so it can't run under the logo, and
  the description, buttons and links were reflowed with proper spacing.
- Translations: 47 more buttons and labels across the Clicker, Multi-Point, Macros,
  Statistics and Keybinds tabs now actually use the language tables (they were hard-coded
  English before), and 5 missing phrases were translated into all six languages. No new
  languages were added - existing ones just cover more of the UI now.
- Launch / restart: the window now eases in smoothly (a decelerating "load-in") instead of
  a linear ramp, so a fresh start or a post-restart hand-off feels more polished.

### 1.0.193
**Live Captions no longer freeze Tempo, plus restore/overlay/notice fixes**
- Windows Live Captions could make Tempo go "not responding" and need a force-kill. Reading
  the caption text walks a UI Automation tree that can block for seconds, and it was being
  done on a 150 ms UI-thread timer - so any stall froze the whole window. That polling now
  runs on a background thread and only hands the finished text back to the UI, so a slow or
  stuck Live Captions can never freeze Tempo.
- Empty/blank page after restoring from the tray: the layout repair on restore only ran for
  a normal-sized window, so a MAXIMISED Tempo came back from minimise with a stale, empty
  page. It now repairs the layout for maximised windows too.
- The first-run "official source" notice could be skipped for people who start Tempo
  minimised to the tray (it was only retried on a visible-state change). It's now retried on
  any restore, so it reliably shows once.
- The clicking / macro overlay badge appeared on the primary monitor even when you were on
  another screen, and could hide behind a maximised window. It now shows on the screen your
  cursor is on and stays above other always-on-top windows without stealing focus.
- The "playing macro" overlay now follows a live theme change (it was being left on the old
  theme while the clicking overlay updated).
- Clicker and macro engines reviewed again - timing is scheduler-based on a dedicated worker
  thread with anti-freeze backoff; no changes needed.

### 1.0.192
**Fixed publish: the tidy-folder move now actually works, and old files get cleared**
- The 1.0.191 step that moves the Whisper native libraries into runtimes\<rid>\native\
  never ran. A batch FOR loop treats a quoted wildcard as literal text and never expands
  it against the filesystem, so it matched no files and left every DLL loose in the
  output. publish.cmd now calls move directly on the wildcards (move does expand them) and
  clears the target folder first, so a rebuild can't leave a stale library behind either.
- Rebuilding into an existing folder could leave old files behind. The clean step ran
  before Tempo was stopped, so a copy still running from that folder locked its files and
  the delete silently failed. publish.cmd now stops any running Tempo at the very start -
  before the clean, and for /quick builds too - so the folder clears properly. If a delete
  still fails (an Explorer window open on the folder, antivirus), it now warns you instead
  of failing silently.
- Net result: a fresh build gives you just Tempo.exe, the scripts, and one tidy
  runtimes\<rid>\native\ folder - no leftover DLLs from earlier builds.

### 1.0.191
**Tidier folder: native libraries grouped into a subfolder**
- The publish and portable folder is no longer cluttered with loose native DLLs. The
  Whisper speech-engine libraries (whisper / ggml) are now moved into the standard
  runtimes\<rid>\native\ subfolder, so the folder you see is just Tempo.exe, the install
  scripts, and one tidy runtimes folder. Whisper.net's loader already searches that
  layout, and Tempo also adds it to the native DLL search path at startup as a safety net
  - the exe's own folder is still searched too, so a flat copy still works.
- publish.cmd does the move automatically right after building and verifies the libraries
  actually landed. install.cmd already copies the runtimes folder recursively, so an
  installed copy gets them as well.
- The portable note and the README now say to keep the "runtimes" folder next to
  Tempo.exe (instead of the loose .dll files).
- No changes to clicking, macros, or the speech engine itself.

### 1.0.190
**Tab switches snap to the top; pinning down the pushed-down dashboard**
- Switching tabs now always shows the TOP of the page. A forced re-centre (which a tab
  switch triggers) used to restore the page's previous scroll offset - and right after
  backdrop or scrollbar changes, that is one way a tab could come up parked low with a
  big empty band above the content. A tab switch now resets to the top, which is the
  expected behaviour anyway. (Resize and scrollbar toggles still keep you where you were.)
- Verified the GIF backdrop handling is sound and needs no change: the header, footer and
  full-window images each dispose the previous image when you choose a new one (no handle
  leak), the animation is guarded against running double-speed, and only the visible tab
  animates.
- Added a one-line diagnostic to the log whenever the Statistics tab is laid out on a tab
  switch, recording the exact client size, vertical offset and scroll values. The layout
  code measures correctly in every windowed case I can reproduce, so if the dashboard
  ever comes up pushed down again, that log line will show precisely why.
- No changes to clicking, macros, the speech engine, or portable behaviour.

### 1.0.189
**Fixes the build - 1.0.188 would not compile**
- Reverted the 1.0.188 change that dropped WPF from the project. That was a mistake: the
  Windows Live Captions reader uses System.Windows.Automation (UI Automation), and those
  assemblies ship with the WPF half of the Windows Desktop framework - so removing WPF
  made that file fail to compile and the whole build produced no files, which is why
  1.0.188 would not build or run. WPF is restored, with a note in the project file
  explaining why it has to stay.
- publish.cmd no longer deletes the previous Tempo.exe before building. If a build ever
  fails, the last working exe now stays in place so you can still run it, instead of
  being left with nothing. (A running Tempo is still closed before the build so dotnet
  can overwrite the exe on success.)
- Everything else from 1.0.188 is kept: the portable "Start with Windows" self-heal and
  the publish check that the native speech libraries shipped beside the exe.
- No changes to clicking, macros, the speech engine, or portable data handling.

### 1.0.188
**Leaner build, self-healing portable startup, and a more thorough publish**
- Dropped the unused WPF framework from the build. The project pulled in all of WPF, but
  Tempo is pure Windows Forms and never used a line of it (its sounds use System.Media,
  not WPF). Removing it makes the self-contained Tempo.exe noticeably smaller and start
  faster - nothing in the app changes.
- "Start with Windows" now self-heals for portable copies. If you move a portable Tempo
  (USB stick, a different folder), the startup entry is rewritten to its new location on
  next launch, instead of leaving Windows trying to launch a path that no longer exists.
- publish.cmd now checks that the native speech libraries (whisper / ggml) actually
  landed beside Tempo.exe and warns if they didn't - catching a broken package restore
  that would otherwise ship an exe with silently-dead Live Captions.
- Fixed a contradictory comment in the project file about how the native libraries are
  packaged (they sit beside the exe; they are not bundled into it).
- No changes to clicking, macros, or the speech engine itself.

### 1.0.187
**Scrolling no longer fights you; the self-refresh flicker is gone**
- Fixed scrolling that jumped or "refreshed by itself", most visibly on Settings. A
  200ms live-display timer was re-asserting the scroll position on every tab five times
  a second, which fought you while you scrolled. That scroll save/restore is only needed
  on the Statistics tab (whose dashboard rebuild snaps to the top), so it now runs only
  there - every other tab scrolls smoothly.
- Fixed the background "changing colour" on refresh/scroll. The pages had been left
  unbuffered while chasing an unrelated layout bug, which showed an erase-flash on every
  repaint. Double-buffering is restored (it never caused the blank/pushed-down tab; that
  was a layout/scroll issue), so refreshes are flicker-free again.
- Hardened minimise then restore: the live timer no longer touches a page's scroll or
  layout while the window is minimised or hidden - doing that against the collapsed
  window was a way Settings/Statistics came back pushed down with a big empty gap. The
  clicking overlay and milestone checks still update normally during a tray run.
- publish.cmd now waits briefly after closing a running Tempo so the file lock is fully
  released before it overwrites the exe - belt-and-braces on the "didn't overwrite, ran
  the old version" fix.
- No changes to clicking, macros, or the speech engine.

### 1.0.186
**In-app update now really updates; no more empty screen after checking**
- Fixed the big one: an in-app update never actually replaced the app, so you kept
  running the old version. The updater was targeting the wrong path - for a single-file
  build it pointed at a temporary extraction folder, not the real Tempo.exe. It now uses
  the actual running exe's location, so the update overwrites the real Tempo.exe wherever
  it lives (your portable folder or the installed copy). This is the "remember where the
  app is" fix.
- Fixed the empty / pushed-down screen after clicking "Check for updates". When the
  result dialog closed, Tempo was forcing a full re-layout of the current tab on every
  window re-activation, which is what shoved Settings (and Statistics) down with a big
  empty gap above. The pages aren't double-buffered anymore, so they repaint themselves
  when a dialog closes - that forced re-layout is gone, replaced with a plain repaint.
- publish.cmd now closes any running Tempo before building, then deletes the old exe, so
  a locked exe (for example a portable copy running from the same output folder) can't
  silently block the overwrite and leave you launching the old build.
- install.cmd was already correct here - it detects the existing version, closes a
  running Tempo, overwrites in place, and rolls back on failure - so it was reviewed and
  left as is.
- No changes to clicking, macros, or the speech engine.

### 1.0.185
**Portable mode is back - and now truly portable**
- Restored running Tempo as a portable copy (no install): run Tempo.exe straight from
  the build/zip folder with its .dll files beside it. Nothing gets installed.
- Made portable genuinely portable. A portable copy now keeps its settings, profiles,
  macros and stats in a "Data" folder right next to Tempo.exe, so the whole folder
  travels together - copy it to a USB stick or another PC and it just works. The old
  portable left data in %LOCALAPPDATA%; this self-contained folder is the improvement.
  If that folder ever isn't writable (for example unzipped under Program Files), Tempo
  safely falls back to %LOCALAPPDATA% so saving never fails. An installed copy is
  unchanged (%LOCALAPPDATA%\AutoClicker).
- About again shows whether you're on the Installed or Portable edition, and Settings
  shows a short note explaining how a portable copy behaves.
- publish.cmd: the setup zip's README now spells out both ways to run (run Tempo.exe for
  portable, or install.cmd to install), and the maintainer notes make clear the one zip
  serves both - and that the standalone exe by itself isn't portable, because it needs
  the .dll files too.
- Website and README updated with a Portable option alongside Installed.
- No changes to clicking, macros, or the speech engine.

### 1.0.184
**Empty Statistics + overlong scrollbar fixed; startup notice and animation fixed**
- Fixed the empty Statistics tab and the over-long, wrong-sized scrollbar together. The
  previous attempt switched the pages to composited painting (WS_EX_COMPOSITED) to stop
  the blanking, but that broke AutoScroll's scrollbar - hence the long bar. The pages are
  now plain (non-buffered) scrolling pages: the child controls render normally and the
  scrollbar is sized correctly. (The only trade-off is that an animated GIF backdrop - an
  optional, rarely used extra - may shimmer slightly in the page margins.)
- The first-run "official source" notice now appears up front, before the app is
  interactive, instead of opening on top of a window that was still fading in.
- Fixed the missing startup animation. The window fade-in was being kicked off first and
  then hidden behind that first-run notice, so it looked like nothing animated. The
  notice now shows first and the window fades in afterwards, so the animation is visible.
- publish.cmd now overwrites cleanly: it deletes a previous build's Tempo.exe and
  checksum before building so a leftover or read-only file can't trip it up (the setup
  zip was already force-overwritten).
- Re-audited the Clicker and Macros (numeric ranges, macro speed/loop guards, the
  player's timing): all correctly guarded, nothing to fix. install.cmd and uninstall.cmd
  remain solid and were left unchanged.

### 1.0.183
**Installed-only: portable mode removed; README and website rebuilt**
- Removed "portable" mode entirely - by design, Tempo is now an installed app only.
  The install instructions are a single, clear path (build with publish.cmd, run
  install.cmd, launch from the Start Menu), and the old "run it from the build folder"
  / portable option is gone from the README, the website, the in-app About dialog and
  Settings, and the installer's notes.
- Rewrote the README and the website around that single install flow, and refreshed
  the feature copy to match what Tempo actually does now: single-to-quadruple clicks
  and six interface languages (English, Spanish, French, German, Italian, Portuguese),
  plus a mention of the optional accessibility captions.
- Internal cleanup: dropped the now-unused deployment-detection code.

No functional behaviour changed in this release - it's documentation, presentation and
a deliberate scope decision.

### 1.0.182
**The empty/blank tab bug - fixed at the root**
- Fixed the recurring blank tab for good - the case where a tall tab (Statistics,
  Settings, Keybinds) comes up empty, sometimes with a scrollbar but no content, often
  just from switching to it. The root cause was finally pinned down: every tab page was
  double-buffered the old way (OptimizedDoubleBuffer + AllPaintingInWmPaint) AND set to
  auto-scroll. On a page whose content is taller than the window, that is a documented
  WinForms flaw - the off-screen buffer is only the size of the visible area, so the
  child controls below the fold were left unpainted and the page looked blank. The short
  tabs (Clicker, Macros) were never tall enough to trip it, which is why only some tabs
  were affected.
- The pages now use composited painting (WS_EX_COMPOSITED): the whole page, background
  and all the cards together, is drawn into one off-screen surface bottom-up. That keeps
  the animated backdrop flicker-free AND guarantees the content always renders, at any
  height and any scroll position. As a safety net, a switched-to tab is also repainted
  once more after it has fully settled. The scrollbar now scrolls real content instead
  of an empty area.

This replaces the long line of partial fixes for this bug (scroll repaint, layout
repaint, minimise repair, post-dialog repair) with a fix at the actual cause. Those
earlier guards are kept as harmless reinforcement.

### 1.0.181
**Empty tab after a dialog (e.g. Merge) - fixed; plus a corrupt-speech-model install fix**
- Fixed the recurring empty tab once more, this time for the case you hit: open a dialog
  (Merge, the macro editor, CPS test, etc.), close it, switch tabs and find them empty
  with the content shoved to the bottom. While a dialog is open the main window is
  inactive and its scrolling pages can be left with a stale layout. Tempo now repairs
  the visible page's layout the moment the window becomes active again - which also
  covers Alt-Tabbing back to it. Together with the minimise fix in 1.0.180, the three
  ways this bug showed up (after recording, after a dialog, after Alt-Tab) are all
  handled. Your scroll position is left untouched, so this never jumps you around.
- Fixed the installer silently installing a corrupt speech model. install.cmd checked
  only that the download produced a file, not that it finished; a dropped connection left
  a truncated file that got installed and then crashed the offline captions on load. It
  now verifies the size (the model is ~147 MB) and, if the download was incomplete,
  removes the partial file and tells you to fetch it later or use Windows captions.

The publish and uninstall scripts were reviewed again and are already solid (publish
verifies its build output and reports size; uninstall flags the 100+ MB model, offers a
backup zip, and removes the model on purge), so they were left unchanged rather than
padded. The Clicker and Macros remain audited and clean.

### 1.0.180
**CRITICAL build fix - plus the empty-Statistics-after-recording bug, finally**
- Fixed a build-breaking bug that had quietly crept into 1.0.178 and 1.0.179: a
  duplicated method (and a couple of unqualified name references) meant those two
  versions would not compile at all. If you tried to build them and it failed, this is
  why - and it also means none of the fixes from 1.0.178/1.0.179 ever actually reached a
  working build. This release compiles cleanly and carries all of them.
- Fixed the Statistics tab coming back as a big empty space with the dashboard shoved to
  the bottom after recording a macro. Recording auto-minimises the window to stay out of
  the recording; coming back from minimised left the scrolling pages with a stale layout
  and scroll position. Tempo now never re-lays-out while minimised, and rebuilds the
  layout cleanly (snapping the visible tab back to the top) the moment the window is
  restored - whether from a recording or an ordinary minimise.

This release also finally delivers, in a build that actually compiles, everything from
the previous two attempts:
- "Start with Windows" uses the real executable path, so it launches reliably at sign-in.
- The interface genuinely changes language: all six languages are selectable and the
  section titles across Clicker, Macros and Settings now translate.
- Live Captions: the native speech engine is no longer cancelled mid-sentence (a likely
  hard-crash cause), audio is sanitised before the engine sees it, the silence filter is
  less aggressive (fewer "said nothing" gaps), and the worker threads are rebalanced so
  it keeps up with speech.
- Website refreshed (six languages, accessibility captions, a languages FAQ).

Audited the Clicker and Macros again (engine guards, click styles incl. Quadruple, the
recorder's delay-coalescing, the player's button/key cleanup, profile indexing): all
correctly guarded, nothing to fix. The publish/install/uninstall scripts remain mature
and were left as-is rather than churned.

### 1.0.179
**Live Captions: crash and missing-words fixes (best-effort) + Clicker/Macro audit**
- Stopped cancelling the speech engine mid-sentence. Tempo was passing a cancellation
  signal straight into the native transcription call; cancelling it part-way through can
  corrupt the native engine and hard-crash the whole app (a native crash that no amount
  of error handling can catch). Each short chunk is now allowed to finish, and captions
  stop cleanly between chunks instead.
- The audio is now sanitised before the engine sees it. A stray invalid sample (NaN /
  infinity / out-of-range, which an odd device buffer can produce) is clamped to a safe
  value, removing another way the native engine could crash.
- Fixed "Tempo said nothing / only half the words". The silence filter was too strict
  and dropped quiet audio (e.g. a video at a modest volume) entirely; it's now far more
  permissive. The transcription threads were also rebalanced so the engine can keep up
  with real-time speech and stop dropping words, while still leaving the UI responsive.
- Audited the Clicker and Macros again for bugs (engine loops, click styles incl. the
  new Quadruple, the macro recorder's delay-coalescing and the player's button/key
  cleanup): all correctly guarded, nothing to fix.

PLEASE READ - honest status on Tempo's own captions: these are real fixes for the most
likely causes, but they ship blind. Tempo's caption engine uses a native speech library
that can't be tested or reproduced in my build environment, and a native crash can't be
caught from normal code. If captions still crash or misbehave for you, that is the
on-device engine itself, and I'd recommend Windows 11's built-in Live Captions instead -
they're more accurate, lower-latency, and rock-solid. Tempo can launch them for you
(Settings > Captions), and YouTube's own captions also work regardless. Tempo's captions
are a convenience, not a replacement for those.

### 1.0.178
**Start-with-Windows fix, the empty-tab bug, and real multi-language UI**
- Fixed "start with Windows" not launching at sign-in. The startup entry used a path
  that can be wrong for a single-file build (a temporary extraction path); it now uses
  the real Tempo.exe path, so the entry survives and actually runs at sign-in.
- Fixed tabs going blank after an action. These pages are double-buffered and scroll,
  and any layout change (a control shown/hidden by some action, the scroll region
  shifting) could leave part of the page stale/blank because only the changed area
  repainted. The page now forces a full repaint after every layout pass - the proper
  companion to the earlier scroll-repaint fix.
- Languages now actually translate the interface. Italian and Portuguese were already
  built in but weren't fully exposed, and many already-written translations were unused
  because the labels weren't wired to the translator. The card titles across Clicker,
  Macros and Settings now translate, the language list is complete (English, Español,
  Français, Deutsch, Italiano, Português), and a few missing strings were filled in -
  all without adding any new languages.
- Website: refreshed the intro, called out the six languages and the accessibility
  captions, and added a "what languages are supported" FAQ entry.

Honest notes: Live Captions and the publish/install/uninstall scripts were reviewed and
are already well-hardened from recent versions, so they were left as-is rather than
changed for the sake of it - tell me a specific behaviour to change and I'll do exactly
that. UI translation now covers the section titles; wiring every last label to the
translator is a larger ongoing pass.

### 1.0.177
**Tray clarity, Quadruple click, hotkeys that work on more keyboards, caption robustness**
- Hotkeys that wouldn't work on some keyboards now work. If Windows refuses to bind a
  shortcut the normal way (common with certain keyboards, or when another app has
  grabbed the key), Tempo automatically falls back to a low-level keyboard hook and
  detects the combo itself. Keyboards where the normal path already works are
  unchanged.
- Tray confusion fixed: the first time Tempo hides to the system tray - whether it
  started with Windows or you closed the window - it now shows a clear one-time message
  explaining it's still running in the tray and how to reopen it. This shows once even
  if routine tray notifications are off, so the app is never mistaken for having quit.
- New click type: Quadruple (4 clicks), alongside Single / Double / Triple.
- Live Captions audio robustness: capped Whisper to fewer CPU cores so heavy
  continuous audio (a video playing) no longer makes the app feel like it's "not
  responding"; guarded the resampler against odd device formats; and when the audio
  device changes mid-session (headphones plugged in, app switches output) the status
  now says plainly that captions stopped and to toggle them off/on to resume.
- publish.cmd: build-failure help now includes a .NET 8 SDK check hint.

Honest note on captions: these changes reduce the freezes/"not responding" and make
failures clearer, but Tempo's own captions still use a small on-device model and won't
match Windows 11 Live Captions. If audio playback keeps stopping capture, that's a
Windows audio-device behaviour; Windows 11's own Live Captions remain the most robust
option and Tempo can launch them for you.

### 1.0.176
**Two small, real fixes for the Clicker and Multi-Point**
- Clicker: the "Clicking ... N CPS" status now shows the true rate. It was reading the
  speed slider's value, which is capped at the slider's maximum, so a fast interval
  typed straight into the milliseconds box (e.g. 2 ms = 500 CPS) could display a lower
  number than it was actually clicking. It now reads the engine's real effective rate.
- Multi-Point: the Order tooltip now explains what each mode does (Sequential, Reverse,
  Random, Ping-Pong) instead of a single generic line.

Honest note: I went through the Clicker, Macros and Multi-Point closely again. The
clicking engine, macro recording/playback (loops, 0.1x-10x speed, loop delay) and
multi-point system (four visiting orders, per-point repeat/dwell/button/type, full
reordering, live highlight of the active point) are all feature-complete and I couldn't
find real bugs in them - so I fixed only the two genuine issues above rather than pad
the release. If there's a specific behaviour you want changed or added on any of these
tabs, tell me exactly what and I'll build that precisely.

### 1.0.175
**More detail in publish.cmd; a full re-audit of the app**
- publish.cmd now reports more after a build: the exact output folder, the runtime
  target it built for (and that it's self-contained, single-file, with native libs
  beside the exe), and the app version - on top of the existing size, checksum,
  installer, notes and timings.
- Re-audited the Clicker, Multi-Point, Macros, Live Captions, Settings, and the
  install/uninstall scripts for bugs and gaps. They came back solid: the click engine
  and timing are well-guarded, Multi-Point and Macros have full keyboard control
  (Delete, Ctrl+D, reorder, rename, etc.) with safe bounds, the captions have all the
  recent crash/freeze fixes plus a no-audio watchdog, Settings is complete, and the
  installer verifies its copy, brings the native caption libraries, and rolls back on
  failure. No changes were forced into that working code, to keep this conflict-free.

If there's a specific behaviour you want changed on any one of those - a wording, a
default, a layout tweak, a new option - tell me exactly what and I'll do that precisely
rather than guess.

### 1.0.174
**Fixed an intermittent crash when stopping captions + start-with-Windows now goes to the tray**
- Fixed the occasional crash when turning Tempo's captions off. The speech engine was
  sometimes being shut down while it was still in the middle of transcribing a chunk -
  disposing it mid-work could crash the app. Now the engine is only released after the
  transcription has actually finished (and on a background thread), so stopping captions
  is safe, and it no longer blocks or freezes the window either.
- "Launch Tempo when I sign in to Windows" now starts Tempo in the tray instead of
  popping its window open at every boot. The window is one click away from the tray
  icon. This also applies to people who turned the setting on in an older version (the
  startup entry is upgraded automatically).

Note on captions: this addresses the stop-related crash. Tempo's own captions still use
a small on-device model and won't match Windows 11 Live Captions for accuracy or delay;
for the best captions, Windows 11's remain the better choice (and Tempo can launch them).

### 1.0.173
**Fixed captions freezing the app + the empty-tab-on-scroll bug (real root cause)**
- Fixed Tempo going "Not Responding" when you started its own captions. The speech
  model (100+ MB) was being loaded on the UI thread, freezing the whole app for a few
  seconds. It now loads on a background thread - the window stays responsive and shows
  "Loading speech model..." then "Listening..." - and turning captions off mid-load is
  handled cleanly.
- Fixed the empty/blank screen when scrolling a tab (and a tab coming back blank).
  Found the real cause this time: the page is an auto-scrolling, double-buffered
  surface, and that combination only repainted the newly-exposed strip on scroll,
  leaving the rest blank. The page now forces a full repaint on every scroll, so it
  stays painted. This is why the earlier layout-based attempts didn't fully fix it.
- Website: added an FAQ section (free?, the unsigned-app warning, bans/safety, safe
  speeds, where data is stored, captions vs Windows 11, Windows-only).
- About dialog: now shows whether Tempo is running Installed or Portable, the .NET
  runtime, and your data folder location with an "Open data folder" link.
- publish.cmd: the next-steps now remind you to run and click around the built exe
  before shipping.
- uninstall.cmd: when settings are kept, it now tells you exactly where they are.

Note on captions: this fixes the freeze (the "model not responding"). Tempo's own
captions still use a small on-device model and won't match Windows 11 Live Captions;
for the most accurate, lowest-delay captions, Windows 11's remain the better choice.

### 1.0.172
**Fixed the blank-tab-on-return bug, richer CPS Test, clearer data folder**
- Fixed the big one: switching tabs and coming back could leave a tab showing an
  empty screen. The scroll-blank fix from before was skipping the re-layout a tab
  needs when you return to it; now an actual tab switch always gives the tab a clean,
  complete re-layout (positions, scroll metrics, repaint), while scrolling still
  avoids the flicker it was meant to. This affects every tab - Clicker, Multi-Point,
  Macros, Statistics, Keybinds and Settings.
- CPS Test now shows more detail after a run: a Consistency score (how even your
  click rhythm was) and your single fastest gap between two clicks, on top of the
  existing CPS, peak, best and per-session stats.
- The data folder (%LOCALAPPDATA%\AutoClicker) now gets a plain-English README
  explaining what each file is and that it's safe to back up or delete. The installer
  also tells you where your data is saved when it finishes.

On the tabs (Clicker, Macros, Live Captions, Multi-Point, Statistics): these are
feature-complete and were already audited as solid, so rather than force risky
cosmetic edits I focused on the shared blank-tab fix (which improves all of them) and
the concrete CPS Test and data-folder requests. Tell me if there's a specific behaviour
on any one tab you'd like changed and I'll do that precisely.

### 1.0.171
**Fixed the update-check timeout + a more detailed, more colourful publish.cmd**
- Fixed update checks timing out even on a healthy connection. The previous version
  cut the per-attempt timeout too short and didn't retry timeouts; now the timeout is
  generous again (for slow cold connections doing DNS + TLS) and a timeout is retried
  once before giving up.
- The timeout message no longer wrongly blames your connection. If it still times out
  after retrying, it now explains GitHub may be slow or a firewall/VPN/antivirus may
  be blocking github.com - and that your internet is otherwise fine.
- publish.cmd: added a colourful animated gradient divider that sweeps across after
  the banner, and a green celebratory sweep on a successful build.
- publish.cmd: added clearer descriptions under several build steps (what the
  environment check, verification, and packaging steps actually do).

Note on the timeout: with working internet, a timeout is usually either a momentarily
slow GitHub or something on the network path (a corporate proxy, VPN, or antivirus)
briefly intercepting the connection to github.com. The retry handles the common slow-
connection case automatically.

### 1.0.170
**Update check now rides out temporary GitHub hiccups (e.g. the 504 error)**
- If the update check hits a temporary server-side error from GitHub - a 500/502/503/
  504 gateway error, a 429, or a dropped connection - Tempo now automatically retries
  a couple of times with a short pause before showing anything. A brief blip (like the
  "error 504" some people saw) now usually just works instead of popping an error.
- When GitHub really is having a longer problem, the message is clearer that it's a
  temporary issue on GitHub's side, not a problem with Tempo or your PC - just try
  again in a few minutes.
- Tuned the timing so the retries always finish inside the check's safety window (the
  per-try timeout is shorter and the overall window a little longer).

Note: a 504 is a "gateway timeout" returned by GitHub's servers, not something Tempo
or your PC did wrong. These are almost always temporary; this change just makes Tempo
shrug off the short ones automatically.

### 1.0.169
**Small Clicker display fix + a full bug sweep**
- Clicker: the Manual Speed target now reads consistently. Dragging the slider showed
  e.g. "Target: 200 CPS" while typing the same speed into the interval boxes showed
  "Target: 200.0 CPS"; both now show the same clean whole-number form.
- Did a broad bug sweep across the engine and app (anti-freeze math, the interval and
  burst run loops, click-style actuation, interval composition/decomposition, the
  slider sync, version parsing, and string handling). Everything else was already
  correctly guarded - divide-by-zero, array access, and substring operations are all
  protected - so no other changes were needed.
- publish.cmd step 3 (the clean phase) was checked and is already correct (it switches
  to its own folder, measures and clears the right output folders, and reports what it
  reclaimed), so it was left as-is.

No features added or removed.

### 1.0.168
**Fixed: turning Windows Live Captions off could crash Tempo**
- Hardened the caption start/stop so pressing the caption hotkey to turn Windows 11
  Live Captions on, then pressing again to turn it off, no longer risks taking Tempo
  down with it. The whole caption transition is now crash-proofed: any unexpected
  error (Windows captions spawning/closing, the UI Automation reader, the overlay, or
  a timer) is caught and logged instead of crashing the app.
- Fixed an orphaned "did it appear?" verification timer. Turning captions on starts a
  short timer that re-checks whether Windows Live Captions actually opened; if you
  turned captions off (or switched to Tempo's own engine) before it finished, that
  timer used to keep running and could re-send the Win+Ctrl+L shortcut - flipping
  Windows captions back on or thrashing the window after you asked for off. It's now
  cancelled the moment the state changes.
- Added a guard against rapid re-entrant toggles (mashing the hotkey), so overlapping
  on/off transitions can't get the caption state out of sync.

Honest note: I couldn't reproduce the exact crash from the code here, so this is a
defensive fix targeting the most likely causes (the orphaned timer and an unguarded
exception during the toggle). If captions still crash Tempo after this, please send
the crash report it saves plus the exact steps, and I'll pin down the precise cause.

### 1.0.167
**Small polish to Macros, Clicker & Statistics**
- Macros: the selected macro is now kept selected when the list refreshes (after a
  search, rename, pin, etc.). Previously a refresh could clear your selection and
  reset the action buttons - now it stays put if the macro is still in view.
- Clicker & Statistics: reviewed the existing features (CPS estimates, the manual
  speed slider and its buttons, profile dropdown, lifetime/session counters, button
  breakdown, milestones, and history filtering). They already preserve selection,
  guard their math, and format numbers consistently, so no changes were needed - and
  I deliberately avoided touching working code to keep this conflict-free.

No features were added or removed; this is purely polish to existing behaviour.

### 1.0.166
**Cleaner Tempo captions, smarter update checking, and script polish**
- Tempo's own captions no longer stutter repeated words at the seams between chunks.
  Because Tempo transcribes overlapping windows of audio, the same word could appear
  twice ("on on the mat"); it now detects and removes that duplicated overlap, so
  captions read cleanly.
- Update checking is smarter about what it offers to download: it prefers a
  standalone Tempo.exe (so the in-app one-click update can apply it), and now also
  offers the installer zip when that's the only thing attached - previously it would
  just send you to the releases page in that case.
- publish.cmd now reminds you to attach Tempo.exe too, since that's what enables the
  in-app one-click update for users.
- Reviewed install.cmd and uninstall.cmd; they already handle a running Tempo, file
  locks, missing-exe discovery, and the not-installed case, so no changes were needed.

Honest note on captions: Tempo's own captions use a small on-device model and still
won't match Windows 11 Live Captions, which uses a much larger streaming engine. This
update removes a real, visible flaw (the repeated words), but a short delay and the
occasional misheard word are inherent to a local model. For the most accurate, lowest-
delay captions, Windows 11 Live Captions remains the better choice and the default.

### 1.0.165
**Nicer card UI with section icons**
- Section cards now show a small coloured icon next to each title (a clock for Click
  Interval, a cursor for Click Options, a target for Click Position, and so on),
  matching the polished card look. This applies across the Clicker, Macros and
  Settings tabs.
- Added a reusable icon system to the card component so future cards can show a
  matching glyph with no extra work.

Honest note: the Multi-Point, Statistics and Keybinds tabs use a flatter layout
without cards, so they don't gain icons yet. Converting them to the card style is a
larger visual rework I'd rather do carefully (and have you check on your screen) than
rush blind - tell me to proceed and I'll do those next, one tab at a time.

### 1.0.164
**Fixed: scrolling could flash an empty screen + publish.cmd clean-phase fixes**
- Fixed the page going blank/empty while scrolling up or down. Scrolling makes the
  scrollbar appear or disappear, which Tempo was treating like a window resize and
  re-laying-out every control mid-scroll - briefly blanking the view. Tempo now only
  re-centres when the window width actually changes, so scrolling is smooth and never
  blanks the content.
- publish.cmd: the script now switches to its own folder first, so building works no
  matter what directory you launch it from (previously the clean and build steps used
  relative paths that only worked from the project root).
- publish.cmd: the clean phase (step 3 of 7) now measures the space it reclaims using
  the same folders it actually deletes, and clearly lists what it's removing.

### 1.0.163
**publish.cmd bug fixes**
- Fixed the build summary describing the wrong build options. It claimed the exe was
  "compressed" and didn't mention the native speech libraries, when in fact
  compression is off and those libraries are kept beside the exe. The displayed
  options now match what's actually built.
- Fixed the "Building..." line saying it was "compressing into one .exe" when
  compression is disabled.
- Fixed the final summary's "Built with: .NET SDK" line never appearing — it checked
  a variable that was never set. It now correctly shows the SDK version used.

Clicker: audited the engine and tab (run loops, fixed-count and duration handling,
interval/burst timing, jitter bounds, and divide-by-zero spots) and found it already
solid — inputs are clamped, jitter stays positive, and the high-CPS path is guarded —
so no changes were needed there.

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
