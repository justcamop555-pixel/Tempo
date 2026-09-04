using System;

namespace AutoClicker.Models
{
    /// <summary>
    /// Global application settings, persisted to disk between runs. Distinct from
    /// <see cref="ClickProfile"/>, which holds the clicking configuration.
    /// </summary>
    public sealed class AppSettings
    {
        // ── Appearance ────────────────────────────────────────────────────────
        public ThemeKind Theme { get; set; } = ThemeKind.Dark;

        /// <summary>
        /// When true, Tempo follows Windows' light/dark app mode (Dark when Windows is
        /// dark, Light when it's light) and updates live, ignoring the manual Theme
        /// pick. A custom accent still applies on top.
        /// </summary>
        public bool FollowSystemTheme { get; set; } = false;

        /// <summary>When true, <see cref="CustomAccentArgb"/> overrides the theme accent.</summary>
        public bool CustomAccentEnabled { get; set; } = false;

        /// <summary>ARGB of the user's custom accent colour.</summary>
        public int CustomAccentArgb { get; set; } = unchecked((int)0xFF7C5CFF);
        public bool StartMinimizedToTray { get; set; } = false;
        public bool MinimizeToTrayOnClose { get; set; } = true;
        /// <summary>
        /// Routine "here's what just happened" notifications — minimised to tray, run
        /// finished, captions started, a device came back, and so on.
        ///
        /// OFF for a fresh install. A brand-new user has not asked to be told about any
        /// of this, and a clicker that starts talking the moment it is installed reads as
        /// noise rather than helpfulness; it is opt-in under Settings → Notifications.
        /// This only changes the DEFAULT: anyone who already has a settings file keeps
        /// whatever they chose, because the value is written there explicitly.
        ///
        /// Messages a user genuinely must see are deliberately NOT gated on this — the
        /// first close-to-tray still explains where the window went, or Tempo would look
        /// like it had quit.
        /// </summary>
        public bool ShowTrayNotifications { get; set; } = false;

        // ── Custom notification pop-ups ──────────────────────────────────────────
        /// <summary>
        /// Show Tempo's own notifications as animated pop-up cards in a screen
        /// corner instead of the plain grey Windows balloon tips — and use that
        /// same style for any mirrored Windows notifications. Master switch for
        /// the custom notification UI; when off, Tempo falls back to tray balloons.
        /// </summary>
        public bool CustomNotifications { get; set; } = true;

        /// <summary>
        /// Whether an animated custom logo (a GIF) plays in the title bar, the taskbar,
        /// the tray and the header, or is shown as a still first frame.
        ///
        /// On by default: someone who chooses an animated logo has already said what they
        /// want. The switch exists because an icon that moves in the corner of the eye is
        /// exactly the kind of thing that is delightful to one person and distracting to
        /// the next, and because it is the honest off-ramp for anyone who would otherwise
        /// have to convert their logo to a PNG to make it stop.
        /// </summary>
        public bool AnimateCustomLogo { get; set; } = true;

        /// <summary>
        /// Capture OTHER apps' Windows notifications and re-show them in Tempo's
        /// style. Requires Windows to grant notification-listener access (asked
        /// once); availability depends on the Windows build. Off by default.
        /// </summary>
        public bool MirrorWindowsNotifications { get; set; } = false;

        /// <summary>
        /// After mirroring a Windows notification into a Tempo card, also remove it
        /// from the Windows Action Center so the same message isn't kept twice.
        /// </summary>
        public bool MirrorClearFromActionCenter { get; set; } = false;

        /// <summary>Corner the pop-up stack grows from: 0 top-right, 1 top-left,
        /// 2 bottom-right, 3 bottom-left.</summary>
        public int NotificationCorner { get; set; } = 0;

        /// <summary>Seconds each pop-up stays before it auto-dismisses (2–20).</summary>
        public int NotificationDurationSeconds { get; set; } = 5;

        /// <summary>
        /// Pop a Tempo card showing the picture the moment an image is copied to the
        /// clipboard (a screenshot via Snip/PrintScreen, an image copied from a page).
        /// A real photo notification — reads the clipboard image locally, nothing leaves
        /// the PC. Opt-in because it watches the clipboard for images.
        /// </summary>
        public bool NotifyOnClipboardImage { get; set; } = false;

        /// <summary>
        /// Draw the ✕ on notification cards. Off = a cleaner card; it still dismisses on
        /// click and still auto-closes, so nothing becomes unreachable.
        /// </summary>
        public bool NotificationShowClose { get; set; } = true;

        /// <summary>
        /// Check on every start that Tempo.exe is still the file that was installed,
        /// and say so if it is not. Costs one background hash of the exe (~120 ms off
        /// the UI thread) and nothing else; nothing is sent anywhere.
        /// </summary>
        public bool IntegrityCheckEnabled { get; set; } = true;

        // The fingerprint of the executable as it was the first time this version ran.
        // Version is stored alongside the hash so an ordinary update re-records the
        // fingerprint instead of being reported as tampering.
        public string IntegrityBaselineHash { get; set; } = "";
        public string IntegrityBaselineVersion { get; set; } = "";
        public long IntegrityBaselineSize { get; set; } = 0;
        public string IntegrityBaselineUtc { get; set; } = "";

        /// <summary>
        /// Which BUILD the baseline was taken from ("260903-0217"), or "" for a baseline
        /// recorded before build IDs existed.
        ///
        /// The version alone cannot identify a build — every test build of 1.0.320 calls
        /// itself 1.0.320 — so when the hash stops matching, this is what turns "the file
        /// was replaced or edited" into a sentence naming which build you had and which
        /// you have now.
        /// </summary>
        public string IntegrityBaselineBuild { get; set; } = "";

        // The exact file GitHub has confirmed as the published build. Remembered so the
        // question is asked once per distinct executable rather than once per launch —
        // the answer cannot change while the bytes do not.
        public string IntegrityVerifiedHash { get; set; } = "";
        public string IntegrityVerifiedUtc { get; set; } = "";

        /// <summary>
        /// The last verdict the user was warned about, so one damaged install does not
        /// pop the same card at every launch. Cleared when the verdict changes.
        /// </summary>
        public string IntegrityLastWarned { get; set; } = "";

        // Set once the very first time the app hides to the tray, so the one-time
        // "where did the window go" intro is shown exactly once.
        public bool HasShownTrayIntro { get; set; } = false;
        public bool AlwaysOnTop { get; set; } = false;

        /// <summary>Register the app to launch when the user signs in to Windows.</summary>
        public bool LaunchAtStartup { get; set; } = false;

        /// <summary>Automatically hide the window to the tray when clicking starts.</summary>
        public bool HideWhenClicking { get; set; } = false;

        /// <summary>Check for a newer Tempo version when the app starts.</summary>
        public bool CheckForUpdatesOnLaunch { get; set; } = true;

        /// <summary>When the last update check ran (UTC); used to throttle launch checks.</summary>
        public DateTime? LastUpdateCheckUtc { get; set; }

        /// <summary>A version the user chose to skip; the launch check won't nag about it.</summary>
        public string SkippedUpdateVersion { get; set; } = "";

        /// <summary>Best clicks-per-second ever recorded in the CPS test.</summary>
        public double CpsTestBest { get; set; } = 0;

        /// <summary>When false, Tempo writes nothing to its on-disk log (privacy).</summary>
        public bool WriteLogFile { get; set; } = true;

        /// <summary>
        /// When false, finished runs are not written to the session history and the
        /// lifetime totals are left untouched — Tempo keeps no record of your activity.
        /// </summary>
        public bool RecordSessionHistory { get; set; } = true;

        /// <summary>Show a small on-screen overlay while the clicker is running.</summary>
        public bool ShowClickingIndicator { get; set; } = true;

        /// <summary>Chime + tray notice when a fixed-count/duration run finishes on its own.</summary>
        public bool NotifyOnRepeatFinish { get; set; } = false;

        /// <summary>How the user last sent a bug report (chooser pre-selects it).</summary>
        public string LastBugReportChannel { get; set; } = "";

        /// <summary>While hidden in the tray AND idle, pause global hotkeys and the
        /// cursor trail so a forgotten Tempo can't start clicking invisibly.</summary>
        public bool TraySleepEnabled { get; set; } = true;

        /// <summary>One-time first-run notice about official download sources.</summary>
        public bool OfficialSourceNoticeShown { get; set; } = false;

        /// <summary>Show Tempo's own caption overlay bar when Live Captions is toggled on.</summary>
        public bool CaptionOverlayEnabled { get; set; } = true;

        /// <summary>
        /// Which engine produces the caption text. Windows uses Windows 11 Live
        /// Captions (mirrored into Tempo's bar); Tempo uses Tempo's own offline
        /// Whisper engine. Either way the text is shown in Tempo's overlay.
        /// </summary>
        public CaptionSource CaptionSource { get; set; } = CaptionSource.Auto;

        /// <summary>
        /// How many lines of caption text the bar keeps on screen.
        ///
        /// The bar used to hold three, and everything older was dropped — so a phrase
        /// survived only a few seconds before the next one pushed it off, and anyone who
        /// glanced away had lost it. More lines means the last thing said is still there
        /// to read back. Six by default; 1–12 allowed, because a taller bar covers more
        /// of what's underneath and that trade is the user's to make.
        /// </summary>
        public int CaptionMaxLines { get; set; } = 6;

        /// <summary>Whisper model key for Tempo's own engine (e.g. "base", "small").</summary>
        public string CaptionModelKey { get; set; } = "base";

        /// <summary>
        /// Absolute path to a speech model file living ANYWHERE on disk, used instead of
        /// the built-in downloads. Empty (the default) means "use CaptionModelKey".
        ///
        /// Whisper models are big — large-v3 is about 3 GB — and people who already run
        /// whisper.cpp, Subtitle Edit, Buzz or their own fine-tune usually have one on a
        /// drive somewhere. Making Tempo the only app that needs its own copy is a real
        /// cost, so it can point at an existing file instead of duplicating it.
        /// </summary>
        public string CaptionCustomModelPath { get; set; } = "";

        /// <summary>
        /// Transient hand-over flag: captions were running when Tempo restarted itself
        /// to apply a caption setting, so the next launch should switch them back on.
        /// Written just before a restart and cleared by the launch that consumes it —
        /// never something the user sets.
        /// </summary>
        public bool CaptionResumeAfterRestart { get; set; } = false;

        /// <summary>
        /// The language to transcribe in: "auto" to detect it, or a Whisper language
        /// code ("en", "es", "ja", …) to pin it for the session.
        ///
        /// Auto-detection re-runs per chunk until it settles, and on noisy audio —
        /// gunfire, music, a game mix — it may never settle, so the language readout
        /// sits on "auto-detect" indefinitely and every chunk pays the detection cost.
        /// Pinning skips detection entirely: faster, steadier, and it can't wander into
        /// the wrong language on one bad chunk. Most people watch content in one
        /// language, so this is usually the better setting.
        /// </summary>
        public string CaptionLanguage { get; set; } = "auto";

        /// <summary>
        /// Prefix caption lines with "Speaker 1:", "Speaker 2:" ... based on
        /// pause-detected speaker turns (see Utils.SpeakerTurnLabeler), sharpened by
        /// on-device voice matching (Utils.VoiceProfiler) when audio is available.
        /// </summary>
        public bool CaptionSpeakerTurns { get; set; } = true;

        /// <summary>
        /// Start captions automatically when a video site (YouTube, TikTok, Twitch,
        /// Netflix, ...) or a game (Roblox, COD, Rainbow Six, ...) is in the
        /// foreground with audio playing (see Utils.MediaDetector).
        /// </summary>
        public bool CaptionAutoStart { get; set; } = true;

        // ── Camera-relative movement (see Engine/CameraRelativeMovement) ────────

        /// <summary>
        /// Turn the movement system on as soon as Tempo starts. Off by default: it
        /// takes over W/A/S/D system-wide, which is not something to switch on behind
        /// the user's back. The hotkey is the normal way to arm it.
        /// </summary>
        public bool MovementEnabled { get; set; } = false;

        /// <summary>0 = World-locked (re-mixes keys as the camera turns), 1 = Camera-relative pass-through.</summary>
        public int MovementFrame { get; set; } = 0;

        /// <summary>
        /// Degrees of in-game camera yaw per unit of raw mouse movement. THE value
        /// that must match your game — use the calibration button, don't guess.
        /// </summary>
        public double MovementDegreesPerCount { get; set; } = 0.06;

        /// <summary>Direction smoothing time constant, seconds. 0 = instant (no added latency).</summary>
        public double MovementTurnSmoothing { get; set; } = 0.0;

        /// <summary>Degrees past a sector boundary before the key combo flips (anti-jitter).</summary>
        public double MovementHysteresisDegrees { get; set; } = 8.0;

        /// <summary>Movement update rate in Hz.</summary>
        public int MovementUpdateHz { get; set; } = 120;

        /// <summary>Radial gamepad stick deadzone, 0..1.</summary>
        public double MovementStickDeadzone { get; set; } = 0.20;

        /// <summary>Camera yaw speed at full right-stick deflection, degrees/second.</summary>
        public double MovementGamepadYawDps { get; set; } = 220.0;

        /// <summary>
        /// One-time balloon shown when Tempo runs from a folder named "Tempo" —
        /// Discord flags any "tempo\tempo.exe" path as the Steam game "Tempo".
        /// </summary>
        public bool DiscordPathHintShown { get; set; } = false;

        /// <summary>
        /// One-time notice before speaker labels are first used, explaining the
        /// "Speaker 1/2" numbers are AI guesses that make plenty of mistakes.
        /// </summary>
        public bool SpeakerLabelsNoticeShown { get; set; } = false;

        /// <summary>
        /// Use the OS's on-device face detector to watch the foreground video and
        /// attribute speech to the face whose mouth is moving (experimental; feeds
        /// the speaker labels; see Utils.FaceSpeakerAnalyzer).
        /// </summary>
        public bool CaptionFaceAnalysis { get; set; } = false;

        /// <summary>
        /// Save each caption session's transcript (with timestamps) to
        /// %LOCALAPPDATA%\AutoClicker\transcripts when captions turn off.
        /// Off by default — it writes spoken content to disk.
        /// </summary>
        public bool CaptionSaveTranscripts { get; set; } = false;

        /// <summary>
        /// Show the "♪ YouTube ·" audio-source name on the caption bar. Some users
        /// want to know where the sound comes from; others find it clutter — their
        /// choice. On by default (the long-standing behaviour).
        /// </summary>
        public bool CaptionShowSourceTag { get; set; } = true;

        /// <summary>
        /// Try the GPU (Vulkan) speech engine instead of the CPU one. Opt-in and
        /// experimental: GPU behaviour varies wildly by card/driver, so the proven
        /// CPU engine stays the default. Applies when the engine next loads
        /// (restart Tempo). Auto-switches itself back off if the GPU engine can't
        /// keep real-time pace.
        /// </summary>
        public bool CaptionTryGpu { get; set; } = false;

        /// <summary>
        /// Skip captioning the user's OWN voice when it comes back through the
        /// speakers (mic sidetone, "Listen to this device", voice-chat monitoring).
        /// Uses a lightweight local mic-envelope monitor — the mic-in-use indicator
        /// will show while captions run, which is why this is opt-in.
        /// </summary>
        public bool CaptionFilterOwnVoice { get; set; } = false;

        /// <summary>
        /// Endpoint id of the SPEAKER captions listen through (loopback). Empty =
        /// follow Windows' default output, the long-standing behaviour. Only
        /// meaningful on PCs with more than one playback device.
        /// </summary>
        public string CaptionSpeakerDeviceId { get; set; } = "";

        /// <summary>
        /// Endpoint id of the MICROPHONE used when captions listen to a mic.
        /// Empty = Windows' default input.
        /// </summary>
        public string CaptionMicDeviceId { get; set; } = "";

        /// <summary>
        /// What Tempo's own engine listens to: 0 = Auto (system audio, or mic if no
        /// speaker), 1 = System audio, 2 = Microphone.
        /// </summary>
        public int CaptionCaptureMode { get; set; } = 0;

        /// <summary>Caption overlay text size in points (10-48).</summary>
        public int CaptionFontSize { get; set; } = 20;

        /// <summary>Caption overlay text opacity, 10-100 (background stays transparent). Default 50%.</summary>
        public int CaptionOpacity { get; set; } = 50;

        /// <summary>Caption overlay font family name.</summary>
        public string CaptionFontFamily { get; set; } = "Segoe UI";

        /// <summary>Use a fixed caption colour (true) instead of the theme text colour.</summary>
        public bool CaptionUseCustomColor { get; set; } = true;

        /// <summary>Draw the rounded background panel behind captions (false = text only).</summary>
        public bool CaptionShowBackground { get; set; } = true;

        /// <summary>Caption colour as an ARGB int. Default = bright amber/yellow.</summary>
        public int CaptionColorArgb { get; set; } = unchecked((int)0xFFF4BF4F);

        /// <summary>Saved live-caption bar position; -1 means "use default (bottom centre)".</summary>
        public int CaptionBarX { get; set; } = -1;
        public int CaptionBarY { get; set; } = -1;

        /// <summary>Saved history overlay position; -1 means "use default (top left)".</summary>
        public int CaptionHistoryX { get; set; } = -1;
        public int CaptionHistoryY { get; set; } = -1;

        /// <summary>Fun: draw a colourful trail following the mouse cursor.</summary>
        public bool CursorTrailEnabled { get; set; } = false;

        /// <summary>Minimise the window automatically while recording a macro.</summary>
        public bool MinimizeWhileRecording { get; set; } = true;

        /// <summary>
        /// While a click run or macro is playing, ignore Tempo's OWN injected input when it
        /// lands on Tempo's own window, so a run cannot operate the interface that controls it.
        ///
        /// This overlaps with <see cref="MinimizeWhileRecording"/> on purpose but does not
        /// replace it: minimising only covers macros, only when a stop hotkey is bound, and
        /// the clicker never minimises at all. Real clicks and keys are never affected either
        /// way, so Stop always stays reachable. On by default; off restores the pre-1.0.318
        /// behaviour for anyone who deliberately automates Tempo's own UI.
        /// </summary>
        public bool IgnoreOwnWindowWhileRunning { get; set; } = true;

        /// <summary>Capture mouse movement when recording a macro (Macros tab checkbox).</summary>
        public bool RecordMacroMovements { get; set; } = true;

        /// <summary>Capture keyboard input when recording a macro (Macros tab checkbox).</summary>
        public bool RecordMacroKeyboard { get; set; } = true;

        /// <summary>Seconds of "3, 2, 1" countdown before macro recording actually starts,
        /// so you can switch to the target window first (0 = start immediately).</summary>
        public int RecordCountdownSeconds { get; set; } = 0;

        /// <summary>Optional path to an animated GIF shown as the header backdrop ("" = none).</summary>
        public string BackgroundGifPath { get; set; } = "";

        /// <summary>Optional second GIF shown as a backdrop band along the bottom ("" = none).</summary>
        public string BackgroundGifPath2 { get; set; } = "";
        public string FullBackgroundGifPath { get; set; } = "";

        // ── Hotkeys ───────────────────────────────────────────────────────────
        // Legacy single hotkeys, kept only so older settings files can be migrated
        // into the unified Bindings list below. New code reads Bindings.
        public HotkeyDefinition StartStopHotkey { get; set; } =
            new HotkeyDefinition(System.Windows.Forms.Keys.F6);

        public HotkeyDefinition PickPositionHotkey { get; set; } =
            new HotkeyDefinition(System.Windows.Forms.Keys.F7);

        public HotkeyDefinition EmergencyStopHotkey { get; set; } =
            new HotkeyDefinition(System.Windows.Forms.Keys.F8);

        /// <summary>The full set of action-to-hotkey bindings.</summary>
        public System.Collections.Generic.List<HotkeyBinding> Bindings { get; set; }
            = new System.Collections.Generic.List<HotkeyBinding>();

        /// <summary>
        /// Which macro each of the three quick-play hotkeys fires, BY NAME.
        ///
        /// These used to be positions: PlayMacro1 ran whatever happened to be first in
        /// the list. The Macros tab sorts that list in place and saves it — A-Z, most
        /// played, newest — and Move up / Move down and Delete shift it too, so the
        /// macro behind a hotkey changed without the hotkey being touched. Pressing
        /// "play macro 1" into a game and getting a different recording is the sort of
        /// surprise a hotkey must never spring.
        ///
        /// Empty means "not assigned"; playback then falls back to the old positional
        /// behaviour so existing setups keep working.
        /// </summary>
        public string MacroSlot1 { get; set; } = "";
        public string MacroSlot2 { get; set; } = "";
        public string MacroSlot3 { get; set; } = "";

        /// <summary>
        /// Amount (milliseconds) added or removed by the increase/decrease-interval
        /// hotkeys.
        /// </summary>
        public int IntervalStepMilliseconds { get; set; } = 10;

        /// <summary>
        /// Seconds of countdown shown before clicking begins from the Start button
        /// or the Start/Toggle hotkey (not Hold mode). Zero starts immediately.
        /// </summary>
        public int ClickerStartDelaySeconds { get; set; } = 0;

        // ── Anti-freeze protection ────────────────────────────────────────────
        /// <summary>
        /// When enabled, the engine never exceeds <see cref="MaxClicksPerSecond"/>
        /// and adaptively backs off if this process's CPU usage climbs past
        /// <see cref="AntiFreezeCpuThreshold"/>, preventing the system from being
        /// frozen by an insane spam rate.
        /// </summary>
        public bool AntiFreezeEnabled { get; set; } = true;

        /// <summary>Hard ceiling on clicks per second (the anti-freeze cap).</summary>
        public int MaxClicksPerSecond { get; set; } = 200;

        /// <summary>
        /// Process CPU usage (percent of the whole machine) above which the engine
        /// throttles itself to keep the system responsive.
        /// </summary>
        public int AntiFreezeCpuThreshold { get; set; } = 80;

        // ── Behaviour ─────────────────────────────────────────────────────────
        public string LastProfileName { get; set; } = string.Empty;
        // Default OFF so Tempo opens centered on every launch. Users who prefer the
        // window to reopen where they left it can turn this on in Settings.
        public bool RememberWindowPosition { get; set; } = false;
        public int WindowOpacity { get; set; } = 100;
        public bool AdvancedUnlockSpeed { get; set; } = false;

        // How strongly the full-window background GIF is dimmed for readability,
        // as a percentage (0 = image at full strength, higher = more dimmed).
        public int BackgroundDim { get; set; } = 55;
        public int WindowLeft { get; set; } = -1;
        public int WindowTop { get; set; } = -1;
        public int WindowWidth { get; set; } = -1;
        public int WindowHeight { get; set; } = -1;
        public bool ConfirmBeforeExitWhileRunning { get; set; } = true;
        public bool SafetyStopOnEscape { get; set; } = true;

        // ── Second cursor ("second mouse") ────────────────────────────────────
        // A visible, Tempo-controlled second pointer the user grabs (hotkey) to aim,
        // parks anywhere on either monitor, and spam-clicks — while the real mouse
        // stays free. See SecondCursorController / SecondCursorOverlay.
        public bool SecondCursorEnabled { get; set; } = false;
        public int SecondCursorShape { get; set; } = 0;              // SecondCursorShape enum
        public int SecondCursorColorArgb { get; set; } = unchecked((int)0xFFFF4040);
        public int SecondCursorScale { get; set; } = 100;           // 50..250 %
        public int SecondCursorSpamButton { get; set; } = 0;        // MouseButtonType (Left)
        public int SecondCursorSpamCps { get; set; } = 10;          // clicks per second
        // When a SECOND physical mouse is plugged in, let it drive the second cursor
        // directly (move it, left-click = click at it, right-click = start/stop spam),
        // while your main mouse keeps controlling the normal Windows cursor. Off by
        // default; needs two mice. See SecondMouseListener.
        public bool SecondCursorUsePhysicalMouse { get; set; } = false;
        public int SecondCursorMouseSensitivity { get; set; } = 100;   // 10..400 % (100 = 1:1)
        // Raw device path of the mouse the user chose to drive the second cursor. Empty =
        // ask by wiggling. Persisted so the same mouse re-binds automatically next time.
        public string SecondCursorMouseDeviceName { get; set; } = "";

        // ── Start-delay countdown ───────────────────────────────────────────────
        /// <summary>Play a soft beep on each second of the start-delay count-in.</summary>
        public bool StartDelayBeep { get; set; } = false;

        // ── Update checking ─────────────────────────────────────────────────────
        /// <summary>
        /// How often the automatic launch-time update check may actually run, when
        /// <see cref="CheckForUpdatesOnLaunch"/> is on: 0 = every launch, 1 = at most
        /// daily, 2 = at most weekly. The master on/off stays CheckForUpdatesOnLaunch,
        /// so existing installs behave exactly as before until this is changed.
        /// </summary>
        public int UpdateCheckFrequency { get; set; } = 1;

        /// <summary>
        /// CACHE ONLY — the newest version the last SUCCESSFUL update check saw on
        /// GitHub. Null before any check.
        ///
        /// This is NOT a record of what has been released, and must never be read as
        /// one. It is only refreshed when a check actually reaches GitHub: if the last
        /// check failed (no network, DNS down, rate-limited) this keeps whatever it
        /// last saw, which can be several releases behind reality. Pair it with
        /// <see cref="LastUpdateCheckUtc"/> and <see cref="LastUpdateCheckFailed"/> —
        /// a value with an old or failed check behind it means nothing.
        /// </summary>
        public string LastKnownLatestVersion { get; set; } = null;

        /// <summary>
        /// True when the most recent update check could NOT reach GitHub. Exists so a
        /// stale <see cref="LastKnownLatestVersion"/> is self-evidently stale instead of
        /// looking like fact — Live Debug shows it, and it is the reason a cached value
        /// should never be treated as "the released version".
        /// </summary>
        public bool LastUpdateCheckFailed { get; set; } = false;

        /// <summary>Whether the last successful check found a newer version.</summary>
        public bool LastCheckFoundUpdate { get; set; } = false;

        // ── Startup ─────────────────────────────────────────────────────────────
        /// <summary>
        /// When Tempo is launched by Windows at sign-in, wait this many extra seconds
        /// before its launch-time network update check, so it doesn't compete for the
        /// network with everything else starting up. 0 = no delay. Ignored when Tempo
        /// is launched manually.
        /// </summary>
        public int StartupDelaySeconds { get; set; } = 0;

        // ── Running overlay (see UI/ClickingIndicatorForm) ──────────────────────
        /// <summary>Screen corner for the running badge: 0 top-centre, 1 top-left,
        /// 2 top-right, 3 bottom-left, 4 bottom-right, 5 bottom-centre.</summary>
        public int OverlayCorner { get; set; } = 0;
        /// <summary>Badge opacity, 40–100 %.</summary>
        public int OverlayOpacity { get; set; } = 96;
        /// <summary>Show the live clicks-per-second on the badge.</summary>
        public bool OverlayShowCps { get; set; } = true;
        /// <summary>Show the running click total on the badge.</summary>
        public bool OverlayShowClicks { get; set; } = true;
        /// <summary>Show elapsed run time on the badge.</summary>
        public bool OverlayShowElapsed { get; set; } = false;

        // ── Statistics ────────────────────────────────────────────────────────
        public long LifetimeClicks { get; set; } = 0;
        public long LifetimeSessions { get; set; } = 0;

        /// <summary>Highest clicks-per-second ever observed across all sessions.</summary>
        public double LifetimePeakCps { get; set; } = 0;

        /// <summary>Total accumulated active clicking time across all runs, in seconds.</summary>
        public long LifetimeRuntimeSeconds { get; set; } = 0;

        /// <summary>Most clicks performed in a single run.</summary>
        public long LifetimeMostClicksRun { get; set; } = 0;

        /// <summary>Optional target clicks for the current session (0 = no goal).</summary>
        public long SessionGoalClicks { get; set; } = 0;

        /// <summary>The tab that was active when the app last closed.</summary>
        public int LastTabIndex { get; set; } = 0;

        /// <summary>
        /// The remembered tab as a STABLE KEY ("clicker", "captions", …) rather than a
        /// position. <see cref="LastTabIndex"/> alone silently changed meaning whenever a
        /// tab was inserted — adding Captions in 1.0.319 turned a remembered "5 = Settings"
        /// into "5 = Captions" for everyone. Empty on settings written before that, which
        /// is what tells the loader to migrate the old index once.
        /// </summary>
        public string LastTabKey { get; set; } = "";

        /// <summary>
        /// Reopen on the tab that was in use last (<see cref="LastTabIndex"/>) instead of
        /// always landing on Clicker. On by default: after a long unattended run, a reboot
        /// or a launch-at-startup restart, coming back to the Clicker tab instead of the
        /// Macros tab you were actually using reads as "Tempo lost my page".
        /// </summary>
        public bool RememberLastTab { get; set; } = true;

        /// <summary>UI language.</summary>
        public Language Language { get; set; } = Language.English;

        /// <summary>True once Tempo has auto-matched the OS display language on first run.</summary>
        public bool LanguageAutoDetected { get; set; } = false;

        /// <summary>Longest single run, in seconds.</summary>
        public long LifetimeLongestRunSeconds { get; set; } = 0;

        // ── Rolling lifetime aggregates ───────────────────────────────────────
        // These accumulate as runs complete so the "all-time" insight cards stay
        // accurate even after the session history reaches its 200-entry cap and
        // starts trimming old runs. Seeded once from existing history on upgrade.
        public bool LifetimeAggregatesSeeded { get; set; } = false;
        /// <summary>Clicks by hour-of-day (0..23), all time.</summary>
        public long[] LifetimeByHour { get; set; } = new long[24];
        /// <summary>Clicks by weekday (0=Sunday .. 6=Saturday), all time.</summary>
        public long[] LifetimeByWeekday { get; set; } = new long[7];
        /// <summary>Clicks per profile name, all time.</summary>
        public System.Collections.Generic.Dictionary<string, long> LifetimeByProfile { get; set; }
            = new System.Collections.Generic.Dictionary<string, long>(System.StringComparer.OrdinalIgnoreCase);
        /// <summary>Number of distinct calendar days with any clicks, all time.</summary>
        public long LifetimeActiveDays { get; set; } = 0;
        /// <summary>The most recent active day (local, "yyyy-MM-dd"); empty if none.</summary>
        public string LifetimeLastActiveDay { get; set; } = "";
        /// <summary>Clicks accumulated on <see cref="LifetimeLastActiveDay"/> so far.</summary>
        public long LifetimeCurrentDayClicks { get; set; } = 0;
        /// <summary>Most clicks in a single calendar day, all time.</summary>
        public long LifetimeBestDayClicks { get; set; } = 0;
        /// <summary>The day of <see cref="LifetimeBestDayClicks"/> (local, "yyyy-MM-dd").</summary>
        public string LifetimeBestDay { get; set; } = "";
        /// <summary>Current consecutive-day clicking streak.</summary>
        public int LifetimeCurrentStreak { get; set; } = 0;
        /// <summary>Longest consecutive-day clicking streak, all time.</summary>
        public int LifetimeLongestStreak { get; set; } = 0;
        /// <summary>Clicks in the calendar year <see cref="LifetimeYearOf"/>.</summary>
        public long LifetimeYearClicks { get; set; } = 0;
        /// <summary>The year that <see cref="LifetimeYearClicks"/> is counting.</summary>
        public int LifetimeYearOf { get; set; } = 0;

        public AppSettings()
        {
        }

        /// <summary>Returns a fresh settings object with default values.</summary>
        public static AppSettings CreateDefault()
        {
            return new AppSettings();
        }

        /// <summary>
        /// Guards against missing nested objects after deserialization of an older
        /// or partial settings file.
        /// </summary>
        public void EnsureConsistency()
        {
            if (StartStopHotkey == null)
            {
                StartStopHotkey = new HotkeyDefinition(System.Windows.Forms.Keys.F6);
            }

            if (PickPositionHotkey == null)
            {
                PickPositionHotkey = new HotkeyDefinition(System.Windows.Forms.Keys.F7);
            }

            if (EmergencyStopHotkey == null)
            {
                EmergencyStopHotkey = new HotkeyDefinition(System.Windows.Forms.Keys.F8);
            }

            if (LastProfileName == null)
            {
                LastProfileName = string.Empty;
            }

            EnsureBindings();
        }

        /// <summary>
        /// Makes sure <see cref="Bindings"/> is populated. If it is empty (a fresh
        /// install or an older settings file), it is seeded from the factory
        /// defaults and then overridden with any legacy single-hotkey values so the
        /// user's previous Start/Pick/Emergency keys carry over. Any actions missing
        /// from a partial list are added with their default binding.
        /// </summary>
        public void EnsureBindings()
        {
            if (Bindings == null)
            {
                Bindings = new System.Collections.Generic.List<HotkeyBinding>();
            }

            bool freshlySeeded = Bindings.Count == 0;
            if (freshlySeeded)
            {
                Bindings = HotkeyActions.DefaultBindings();

                // Carry over legacy values from pre-Bindings settings files.
                ApplyLegacy(HotkeyAction.ToggleStartStop, StartStopHotkey);
                ApplyLegacy(HotkeyAction.PickPosition, PickPositionHotkey);
                ApplyLegacy(HotkeyAction.EmergencyStop, EmergencyStopHotkey);
            }

            // Add any actions that are not present yet (e.g. new actions added in a
            // later version) using their defaults, and drop null entries.
            Bindings.RemoveAll(b => b == null);
            foreach (var info in HotkeyActions.All)
            {
                if (GetBinding(info.Action) == null)
                {
                    Bindings.Add(new HotkeyBinding(info.Action, info.Default.Clone()));
                }
            }

            foreach (var b in Bindings)
            {
                if (b.Hotkey == null)
                {
                    b.Hotkey = new HotkeyDefinition();
                }
            }
        }

        /// <summary>
        /// Writes the three legacy single-hotkey properties back from <see cref="Bindings"/>.
        ///
        /// Those properties are the pre-Bindings format. Nothing in Tempo READS them any
        /// more — <see cref="EnsureBindings"/> migrates them in once, on the first load of
        /// an old settings file, and after that they were never touched again. So they sat
        /// in settings.json frozen at whatever the keys were before the migration,
        /// disagreeing with the real bindings forever, for two bad outcomes:
        ///
        ///   * anyone reading settings.json — the user, a support question, a backup diff —
        ///     sees a Start/Pick/Emergency key that is simply not the one in use;
        ///   * they are still the migration fallback. Should Bindings ever come back empty
        ///     from a partial or hand-edited file, EnsureBindings would seed from these and
        ///     silently RESURRECT keys the user changed long ago.
        ///
        /// Keeping them current costs three assignments per save and fixes both. It also
        /// keeps the downgrade path honest: an older Tempo reading this file gets the keys
        /// the user actually has, which matters here because moving between a test build
        /// and the release is routine.
        /// </summary>
        public void SyncLegacyHotkeys()
        {
            // Populate first, ALWAYS. Deriving from an empty Bindings would hand back the
            // unbound fallback for all three and blank the legacy properties — destroying
            // the very migration source they exist to be. That is reachable: SettingsManager
            // returns CreateDefault() un-ensured when there is no file or it cannot be read,
            // and MainForm saves immediately after load for the language auto-detect, so the
            // first save of a fresh install would run this against a Bindings list that
            // nothing had seeded yet. EnsureBindings is idempotent and cheap.
            EnsureBindings();

            StartStopHotkey = HotkeyFor(HotkeyAction.ToggleStartStop).Clone();
            PickPositionHotkey = HotkeyFor(HotkeyAction.PickPosition).Clone();
            EmergencyStopHotkey = HotkeyFor(HotkeyAction.EmergencyStop).Clone();
        }

        private void ApplyLegacy(HotkeyAction action, HotkeyDefinition legacy)
        {
            if (legacy == null || !legacy.IsValid)
            {
                return;
            }

            HotkeyBinding binding = GetBinding(action);
            if (binding != null)
            {
                binding.Hotkey = legacy.Clone();
            }
        }

        /// <summary>Returns the binding for an action, or null if absent.</summary>
        public HotkeyBinding GetBinding(HotkeyAction action)
        {
            if (Bindings == null)
            {
                return null;
            }

            foreach (var b in Bindings)
            {
                if (b != null && b.Action == action)
                {
                    return b;
                }
            }

            return null;
        }

        /// <summary>Returns the hotkey for an action, or an empty (unbound) one.</summary>
        public HotkeyDefinition HotkeyFor(HotkeyAction action)
        {
            HotkeyBinding b = GetBinding(action);
            return b != null && b.Hotkey != null ? b.Hotkey : new HotkeyDefinition();
        }
    }
}
