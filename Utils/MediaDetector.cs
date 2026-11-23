using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.CoreAudioApi;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Notices when the user is watching or playing something with sound so captions
    /// can start automatically, and names WHAT it is for the notification.
    ///
    /// Works for ANY game or website — no fixed list required. Every couple of
    /// seconds it asks Windows which PROCESSES are currently making sound (per-app
    /// audio sessions, the same data as the Volume Mixer) and checks whether the
    /// FOREGROUND app is one of them. Browsers play audio from helper child
    /// processes, so the match is by executable name family (msedge.exe's audio
    /// helper is also msedge.exe). Identification:
    ///  • browser in front → the site name from the window title ("YouTube",
    ///    "TikTok", ... known sites get their proper name; unknown sites show the
    ///    page/site title);
    ///  • anything else → the app's window title or process name ("Roblox",
    ///    "Call of Duty", any game whatsoever).
    /// Two consecutive positive checks are required, so a notification blip or UI
    /// sound can't trigger captions. Raises <see cref="StateChanged"/> on
    /// transitions; the caller decides what to do.
    /// </summary>
    public sealed class MediaDetector : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);

        private const float SessionPeakThreshold = 0.01f;

        // Browsers: the window title carries the site, and audio comes from children.
        private static readonly string[] Browsers =
        {
            "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "librewolf",
            "waterfox", "arc", "yandex"
        };

        // Well-known sites/games get their proper display name. Order matters where
        // one key contains another ("youtube music" must precede "youtube").
        private static readonly (string key, string name)[] KnownNames =
        {
            ("youtube music", "YouTube Music"),
            ("spotify", "Spotify"), ("soundcloud", "SoundCloud"), ("apple music", "Apple Music"),
            ("youtube", "YouTube"), ("tiktok", "TikTok"), ("twitch", "Twitch"),
            ("netflix", "Netflix"), ("prime video", "Prime Video"), ("disney+", "Disney+"),
            ("hulu", "Hulu"), ("crunchyroll", "Crunchyroll"), ("vimeo", "Vimeo"),
            ("dailymotion", "Dailymotion"), ("bilibili", "Bilibili"), ("kick", "Kick"),
            ("google translate", "Google Translate"),
            ("robloxplayerbeta", "Roblox"), ("roblox", "Roblox"),
            ("cod", "Call of Duty"), ("blackops", "Call of Duty"),
            ("modernwarfare", "Call of Duty"), ("warzone", "Call of Duty"),
            ("rainbowsix", "Rainbow Six"), ("rainbow6", "Rainbow Six"),
            ("fortnite", "Fortnite"), ("valorant", "Valorant"),
            ("cs2", "Counter-Strike 2"), ("csgo", "CS:GO"),
            ("overwatch", "Overwatch"), ("r5apex", "Apex Legends"),
            ("tslgame", "PUBG"), ("minecraft", "Minecraft"),
            ("leagueclient", "League of Legends"), ("league of legends", "League of Legends"),
            ("gta5", "GTA V"), ("gtav", "GTA V"), ("eldenring", "Elden Ring"),
            ("cyberpunk2077", "Cyberpunk 2077"), ("genshinimpact", "Genshin Impact"),
            ("vlc", "VLC")
        };

        // Shell/system processes whose sounds should never count as "watching something".
        private static readonly string[] IgnoreProcesses =
        {
            // "tempoclicker": the installed exe's name — "tempo" alone let Tempo's own sounds become the source.
            "tempo", "tempoclicker", "explorer", "shellexperiencehost", "searchhost", "sihost",
            "startmenuexperiencehost", "textinputhost", "systemsettings", "dwm",
            "audiodg", "rundll32", "svchost", "widgets", "lockapp",
            // Caption tools are never the MEDIA — blaming "♪ Live Captions" for a
            // video's audio (seen live) just confuses the source tag.
            "livecaptions", "speechruntime"
        };

        private readonly System.Threading.Timer _timer;
        private MMDeviceEnumerator _audio;
        private volatile bool _active;
        private string _reason = "";
        private int _tickGuard;
        private int _consecutive;   // debounce: positives in a row before firing
        private volatile bool _disposed;
        private readonly Dictionary<uint, string> _pidNames = new Dictionary<uint, string>();
        private readonly Dictionary<uint, long> _pidNamedAt = new Dictionary<uint, long>();

        /// <summary>Fired on every transition. Args: active, human-readable source name.</summary>
        public event Action<bool, string> StateChanged;

        /// <summary>Callback deciding whether detection should run at all (setting gate).</summary>
        public Func<bool> Enabled { get; set; } = () => true;

        /// <summary>
        /// Whether Windows' media controls list a program (lower-case process name: "msedge", "spotify") as PLAYING — set by
        /// the host from MediaTitleWatcher while captions run; null = no such signal, loudness alone decides (as before).
        ///
        /// Round 40 ("hear only that's playing is sometimes not work … prevent wrong source"): the loudest app named the
        /// source and was the one captions heard alone, so a game, a Discord call or a ping out-sounding the video took
        /// both — the bar said "♪ Roblox" over a YouTube video and the video's words were left out. An app Windows says is
        /// playing media now wins over a louder one that is not, takes over in 2 looks rather than 5, and keeps the
        /// isolation through its quiet stretches while it still plays.
        /// </summary>
        public Func<string, bool> MediaPlaying { get; set; }

        // The audible app Windows lists as playing media at the latest look (0 = none) — timer thread only.
        private uint _mediaNow;

        /// <summary>Whether the process is one Windows lists as playing media (false while there is no such signal).</summary>
        private bool PidPlaysMedia(uint pid)
        {
            Func<string, bool> playing = MediaPlaying;
            if (pid == 0 || playing == null) { return false; }
            try { return playing(ProcessNameOf(pid)); }
            catch { return false; }
        }

        /// <summary>For the harness: whether a process plays media, in place of Windows' media controls (StepIsolation).</summary>
        internal Func<uint, bool> PlaysMediaForTest;

        /// <summary>True while the foreground app is making sound.</summary>
        public bool IsActive => _active;

        private volatile string _audioSource = "";
        private int _sourceSilentTicks;
        private IntPtr _audioWindow = IntPtr.Zero;
        // PID of the audio session that OWNS the current source name. A browser plays
        // every tab through ONE long-lived audio process, so this PID does not change
        // when you switch tabs — only when a genuinely different app becomes the loudest.
        // We lock the source name to it so a tab switch (whose window title is the tab you
        // switched TO, not the one still playing) can never re-brand the audio.
        private uint _audioSourcePid;

        // Source-change debounce. When TWO apps make sound at once (a game plus a
        // video on a second screen — the exact case seen live), whichever happens to
        // be momentarily louder wins the raw "loudest session" pick, so the source
        // name flip-flopped every single tick. That wasn't just log noise: every flip
        // wiped the learned voice profiles and reset speaker numbering, so the
        // speaker labels could never settle. A challenger must now hold the lead for
        // several consecutive ticks before it takes the crown; the FIRST source seen
        // is still adopted instantly, so nothing gets slower to start.
        // Net ticks a challenger must WIN (wins minus incumbent wins, see the decay
        // in Tick) before the source tag hands over. 5 with decay ≈ a genuine
        // switch in ~5 s; simultaneous dual audio (game + video trading the lead)
        // hovers near zero and never switches.
        private const int SourceSwitchTicks = 5;
        private string _pendingSource;
        private int _pendingTicks;
        private IntPtr _pendingWindow = IntPtr.Zero;
        private uint _pendingPid;   // owning PID of the challenger, adopted with it on switch

        /// <summary>
        /// Main window of the app currently making sound (IntPtr.Zero when unknown).
        /// Lets the face analyzer read faces in THAT window — a video playing in a
        /// background browser or any app — instead of only whatever is foreground.
        /// </summary>
        public IntPtr CurrentAudioWindow => _audioWindow;

        /// <summary>
        /// Name of the app currently making the loudest sound — foreground or not —
        /// e.g. "YouTube", "Roblox", "VLC", or a window title for anything unknown.
        /// Empty while nothing (interesting) is audible. Shown as the caption bar's
        /// "where this audio comes from" tag.
        /// </summary>
        public string CurrentAudioSource => _audioSource;

        /// <summary>The process behind <see cref="CurrentAudioSource"/> (0 when none) — which Roblox client, when there are
        /// several, so the tag can name the game that one is in.</summary>
        public int CurrentAudioPid => (int)_audioSourcePid;

        /// <summary>
        /// How many apps are audibly playing RIGHT NOW (active sessions above the peak
        /// threshold). More than one means the loopback capture is transcribing a mix.
        /// </summary>
        public int AudibleAppCount => _audibleAppCount;

        /// <summary>
        /// How much of the mix the named source owns: 1.0 = it is alone, near 0 = another
        /// app is just as loud. Low values are the honest explanation for captions that
        /// read like two conversations spliced together.
        /// </summary>
        public float SourceDominance => _sourceDominance;

        private volatile int _audibleAppCount;
        private volatile float _sourceDominance = 1f;

        // ── Which app captions hear alone (round 27, 2026-11-15) ─────────────────────────────────────────────────────
        //
        // AppAudioCapture lets captions hear ONE app, so other apps' pings, music and games stay out of the words (the
        // user's "my apps play random [sounds] that interrupt live caption"). Which app is decided here, and more
        // carefully than the source TAG, because a wrong pick costs words, not just a label:
        //  • nothing picked → the source app, once it has been the LOUDEST app for IsolateAfterLooks looks running. A
        //    notification's ping from an app that happened to become the first source is one look, and is never picked.
        //  • the picked app is audible → kept, unless the (already debounced) source has moved to another app that leads.
        //  • the picked app is quiet while another app plays, ReleaseAfterQuietLooks looks running → every app again, so
        //    whatever plays now is heard at once (the source it settles on is picked in its turn).
        //  • the picked app has closed → every app at once.
        // "Every app" is always the safe answer: it is exactly what captions heard before.

        /// <summary>The process captions should hear alone, or 0 for every app on the speaker.</summary>
        public int IsolationPid => _isolationPid;

        /// <summary>Raised (on the detector's timer thread) when <see cref="IsolationPid"/> changes.</summary>
        public event Action<int> IsolationChanged;

        /// <summary>Why <see cref="IsolationPid"/> last changed, in a few words for the log ("" before the first change).</summary>
        public string IsolationReason { get; private set; } = "";

        internal const int IsolateAfterLooks = 2;
        internal const int ReleaseAfterQuietLooks = 3;
        private volatile int _isolationPid;
        private long _isoCandidate = -1;   // -1 none; 0 = every app; else a process id
        private int _isoCandidateLooks;
        private int _isoQuietLooks;
        // The apps audible at the latest look (timer thread only), and whether that look could read the session list.
        private readonly HashSet<uint> _audibleNow = new HashSet<uint>();
        private bool _lookedNow;
        private uint _loudestNow;

        /// <summary>
        /// One look's worth of the isolation rules above. <paramref name="loudest"/> = the loudest audible app this look
        /// (after the switching margin), <paramref name="source"/> = the debounced source's process, <paramref name="audible"/>
        /// = every audible app. Internal and static-free of Windows so the harness can drive it look by look.
        /// </summary>
        internal void StepIsolation(uint loudest, uint source, HashSet<uint> audible, Func<uint, bool> alive)
        {
            uint iso = (uint)_isolationPid;
            // An app still PLAYING media (Windows' media controls) counts as heard through its quiet stretches — a pause
            // between sentences, a quiet scene — so a game going on underneath does not take its place (round 40).
            Func<uint, bool> playsMedia = PlaysMediaForTest ?? PidPlaysMedia;
            bool isoAudible = iso != 0 && (audible.Contains(iso) || playsMedia(iso));
            bool sourceLeads = source != 0 && loudest == source && audible.Contains(source);

            long want = iso;
            int need = IsolateAfterLooks;
            string why = null;
            if (iso != 0 && !alive(iso))
            {
                want = 0;
                need = 1;
                why = "the app it heard closed";
            }
            else if (iso == 0)
            {
                if (sourceLeads)
                {
                    want = source;
                    why = playsMedia(source) ? "Windows lists it as playing" : "the loudest app for " + IsolateAfterLooks + " looks";
                }
            }
            else if (sourceLeads && source != iso)
            {
                want = source;   // the source itself moved on to an app that leads now
                why = "the sound moved to it" + (playsMedia(source) ? " (Windows lists it as playing)" : "");
            }
            else if (isoAudible)
            {
                _isoQuietLooks = 0;
            }
            else
            {
                _isoQuietLooks = audible.Count > 0 ? _isoQuietLooks + 1 : 0;
                if (_isoQuietLooks >= ReleaseAfterQuietLooks)
                {
                    want = 0;
                    need = 1;
                    why = "the app it heard went quiet (and is not playing media) while another one played";
                }
            }

            if (want == iso)
            {
                _isoCandidate = -1;
                _isoCandidateLooks = 0;
                return;
            }
            if (want == _isoCandidate) { _isoCandidateLooks++; }
            else { _isoCandidate = want; _isoCandidateLooks = 1; }
            if (_isoCandidateLooks < need) { return; }

            IsolationReason = why ?? "";
            _isolationPid = (int)want;
            _isoCandidate = -1;
            _isoCandidateLooks = 0;
            _isoQuietLooks = 0;
            try { IsolationChanged?.Invoke((int)want); } catch { }
        }

        private static bool ProcessAlive(uint pid)
        {
            try
            {
                using (var p = Process.GetProcessById((int)pid)) { return !p.HasExited; }
            }
            catch (ArgumentException) { return false; }
            catch { return true; }   // running, just not ours to inspect
        }

        // ── Smoothed per-app loudness ──────────────────────────────────────────
        // WASAPI reports MasterPeakValue for the instant you ask, and the poll runs
        // once a second, so a single sample says almost nothing about what is really
        // carrying the audio. These keep a short rolling average per process id.
        private readonly Dictionary<uint, float> _levels = new Dictionary<uint, float>();
        private const float LevelRise = 0.5f;    // follow a rise quickly (something started)
        private const float LevelFall = 0.15f;   // let a fall decay slowly (gaps aren't silence)
        // The winner has to beat the incumbent by this much to take the title. Without
        // it two apps at similar levels swap the "source" every poll, which is what made
        // the caption source tag flicker between a game and whatever else was running.
        private const float SwitchMargin = 1.25f;
        private uint _incumbentPid;

        /// <summary>
        /// Rolling level for one process: rises fast, falls slow. Ids that stop appearing
        /// decay to nothing and are dropped, so the table can't grow across a long session.
        /// </summary>
        private float Smoothed(uint pid, float peak)
        {
            float prev;
            if (!_levels.TryGetValue(pid, out prev))
            {
                _levels[pid] = peak;
                return peak;
            }
            float a = peak > prev ? LevelRise : LevelFall;
            float now = prev + (peak - prev) * a;
            _levels[pid] = now;
            return now;
        }

        /// <summary>
        /// Decays every process that wasn't seen this pass and forgets the ones that have
        /// gone quiet, so a closed app's level can't linger and win a later comparison.
        /// </summary>
        private void DecayUnseen(HashSet<uint> seen)
        {
            if (_levels.Count == 0) { return; }
            List<uint> drop = null;
            foreach (uint pid in new List<uint>(_levels.Keys))
            {
                if (seen.Contains(pid)) { continue; }
                float v = _levels[pid] * (1f - LevelFall);
                if (v < SessionPeakThreshold * 0.5f)
                {
                    (drop ?? (drop = new List<uint>())).Add(pid);
                }
                else
                {
                    _levels[pid] = v;
                }
            }
            if (drop != null)
            {
                foreach (uint pid in drop) { _levels.Remove(pid); }
            }
        }

        public MediaDetector()
        {
            try { _audio = new MMDeviceEnumerator(); } catch { _audio = null; }
            // 1 s ticks: with the two-positives debounce below, captions auto-start
            // ~2 s after a video/game starts making sound (was ~4-5 s at 2 s ticks).
            // Each tick is a cheap volume-mixer style session enumeration.
            _timer = new System.Threading.Timer(Tick, null, 2000, 1000);
        }

        public void Dispose()
        {
            _disposed = true;
            try { _timer.Dispose(); } catch { }
            try { _audio?.Dispose(); } catch { }
        }

        private void Tick(object state)
        {
            if (_disposed || System.Threading.Interlocked.CompareExchange(ref _tickGuard, 1, 0) != 0)
            {
                return;
            }
            try
            {
                bool enabled = false;
                try { enabled = Enabled == null || Enabled(); } catch { }

                // Always keep the "who is making sound" name fresh — the caption bar's
                // source tag uses it even when auto-start is switched off. The name is
                // STICKY for a few ticks of silence: speech and videos pause naturally
                // between phrases, and a tag that blinks off at every gap reads as
                // broken. It clears only after ~10 s of sustained quiet.
                string live = NameLoudestAudioSource(out IntPtr liveWindow, out uint loudPid);
                if (live.Length > 0)
                {
                    // Same audio process still loudest → a browser TAB switch (whose title
                    // is the tab you moved TO, not the one still playing) must not re-brand
                    // the audio. Keep the locked source. The ONE exception: you switched to
                    // a genuinely different KNOWN media site (e.g. YouTube → Netflix in the
                    // same browser) — that's a real change worth adopting (via the normal
                    // debounce), since a plain window-title flip can't be a known site.
                    bool sameProcess = loudPid != 0 && loudPid == _audioSourcePid && _audioSource.Length > 0;
                    bool movedToNewKnownSite = sameProcess && live != _audioSource && IsKnownSiteName(live);
                    // The same NAME from another process is the incumbent only when it is the same program (a browser
                    // restarting its audio process) or the old process has gone quiet. Two browsers both on "YouTube",
                    // or two of "this website", used to hand the source — and now which app captions hear alone — to
                    // whichever was louder for one look (round 27).
                    bool sameNameTakesOver = live == _audioSource &&
                        (_audioSourcePid == 0 || !_audibleNow.Contains(_audioSourcePid) ||
                         string.Equals(ProcessNameOf(loudPid), ProcessNameOf(_audioSourcePid), StringComparison.Ordinal));
                    if (sameProcess && !movedToNewKnownSite)
                    {
                        _pendingSource = null;
                        _pendingTicks = 0;
                    }
                    else if (_audioSource.Length == 0 || sameNameTakesOver)
                    {
                        // First source, or the incumbent held the lead (same name). Lock
                        // the owning PID so future tab switches stay pinned to it. The
                        // challenger's progress DECAYS rather than resetting outright.
                        _audioSource = live;
                        _audioWindow = liveWindow;
                        _audioSourcePid = loudPid;
                        if (_pendingTicks > 0 && --_pendingTicks == 0)
                        {
                            _pendingSource = null;
                        }
                    }
                    else
                    {
                        // A DIFFERENT app/process is loudest. Only hand over the source once
                        // it has stayed loudest for several ticks in a row — a genuine
                        // switch does, two apps trading peaks back and forth doesn't.
                        // (Keyed by process as well as name: two processes with one name are two challengers.)
                        if (live == _pendingSource && loudPid == _pendingPid)
                        {
                            _pendingTicks++;
                        }
                        else
                        {
                            _pendingSource = live;
                            _pendingTicks = 1;
                        }
                        _pendingWindow = liveWindow;
                        _pendingPid = loudPid;
                        // Media first (round 40): an app Windows says is playing takes over from one that is not in 2 looks;
                        // one that is not takes over from an app still playing only after 15 (its quiet scenes and pauses
                        // between sentences are not the end of it). Otherwise the usual 5.
                        bool challengerPlays = PidPlaysMedia(loudPid), incumbentPlays = PidPlaysMedia(_audioSourcePid);
                        int needTicks = challengerPlays && !incumbentPlays ? 2
                                      : incumbentPlays && !challengerPlays ? SourceSwitchTicks * 3
                                      : SourceSwitchTicks;
                        if (_pendingTicks >= needTicks)
                        {
                            _audioSource = live;
                            _audioWindow = _pendingWindow;
                            _audioSourcePid = _pendingPid;
                            _pendingSource = null;
                            _pendingTicks = 0;
                        }
                    }
                    _sourceSilentTicks = 0;
                }
                else if (_audioSource.Length > 0 && ++_sourceSilentTicks >= 10)
                {
                    _audioSource = "";
                    _audioWindow = IntPtr.Zero;
                    _audioSourcePid = 0;
                    _pendingSource = null;
                    _pendingTicks = 0;
                }

                if (_lookedNow)
                {
                    StepIsolation(_loudestNow, _audioSourcePid, _audibleNow, ProcessAlive);
                }

                string reason = enabled ? DetectForegroundMedia() : "";

                // The foreground check alone is why auto-start "just didn't work" for a
                // lot of people. It only ever looks at the window that is IN FRONT, so a
                // video kept playing while you alt-tab to a game, read something else,
                // work on the other monitor — or simply click Tempo's own window — stops
                // counting as media the instant it loses focus, and captions never come
                // on. The loudest-audio-source scan above already knows what is actually
                // making sound anywhere on the PC, foreground or not, so fall back to it.
                //
                // Deliberately narrowed to RECOGNISED sites and apps (YouTube, Netflix,
                // Twitch, Spotify, the known games...). Any audible app would be far too
                // eager — a Discord blip or a browser notification would start captions
                // from the background. A known media source that is genuinely playing is
                // exactly what the setting promises.
                if (enabled && reason.Length == 0 &&
                    _audioSource.Length > 0 && IsKnownSiteName(_audioSource))
                {
                    reason = _audioSource;
                }

                bool hit = !string.IsNullOrEmpty(reason);

                // Debounce: require two positives in a row (~2 s) so a notification
                // ping or a click sound can't start captions.
                _consecutive = hit ? _consecutive + 1 : 0;
                bool nowActive = hit && _consecutive >= 2;

                if (nowActive != _active)
                {
                    _active = nowActive;
                    if (nowActive) { _reason = reason; }
                    try { StateChanged?.Invoke(nowActive, nowActive ? reason : _reason); } catch { }
                }
            }
            catch { /* detection must never destabilise the app */ }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _tickGuard, 0);
            }
        }

        /// <summary>
        /// Names what the foreground app is playing, or "" when it isn't making sound.
        /// </summary>
        private string DetectForegroundMedia()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero)
                {
                    return "";
                }
                GetWindowThreadProcessId(h, out uint pid);
                if (pid == 0)
                {
                    return "";
                }

                string procName = ProcessNameOf(pid);
                if (procName.Length == 0 || IsIgnored(procName))
                {
                    return "";
                }

                // Is this app (or a same-named helper process — how browsers play
                // audio) currently making sound?
                if (!ProcessFamilyIsAudible(procName))
                {
                    return "";
                }

                var sb = new StringBuilder(512);
                GetWindowText(h, sb, sb.Capacity);
                string title = sb.ToString();

                return IdentifySource(procName, title);
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Strips browser-tab noise from a window title: "(3) " unread counters,
        /// audio-state glyphs, and surrounding whitespace — so "(2) 🔊 Cat video"
        /// reads "Cat video".
        /// </summary>
        private static string CleanTitle(string title)
        {
            string t = (title ?? "").Trim();
            // Leading "(123) " — unread/notification counters (YouTube, WhatsApp Web...).
            while (t.StartsWith("(", StringComparison.Ordinal))
            {
                int close = t.IndexOf(") ", StringComparison.Ordinal);
                if (close <= 1 || close > 8) { break; }
                bool digits = true;
                for (int i = 1; i < close; i++) { if (!char.IsDigit(t[i])) { digits = false; break; } }
                if (!digits) { break; }
                t = t.Substring(close + 2).TrimStart();
            }
            // Audio-state glyphs some browsers prepend to noisy tabs.
            t = t.Replace("🔊", "").Replace("🔇", "").Trim();
            return t;
        }

        // A ProfileWords table used to sit here — browser-profile segments ("Personal",
        // "Work", "Incognito") to strip out of a window title before reading a site name
        // from it. It was left behind when the title segment-splitting was REMOVED on
        // purpose, and had no callers in any shipped build.
        //
        // Deliberately not revived. IdentifySource explains why the splitting went: the
        // source name is painted on an always-on-top caption bar, which is on screen
        // exactly when other people are looking — screen shares, calls, recordings — and
        // anything parsed out of a window title beyond a recognised service is page
        // content. Re-adding this table is the first step back toward broadcasting what
        // the user is reading.

        /// <summary>The KnownNames that are WEBSITES — the only ones a browser's tab title may name. The rest are apps and
        /// games, named by their own process.</summary>
        private static readonly HashSet<string> SiteKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "youtube music", "spotify", "soundcloud", "apple music", "youtube", "tiktok", "twitch", "netflix",
            "prime video", "disney+", "hulu", "crunchyroll", "vimeo", "dailymotion", "bilibili", "kick", "google translate"
        };

        /// <summary>Human name for the playing source — site for browsers, app/game otherwise.</summary>
        internal static string IdentifySource(string procName, string title)
        {
            title = CleanTitle(title);
            string titleLower = title.ToLowerInvariant();
            bool browser = IsBrowserProc(procName);

            // Known site/game names first (proper capitalisation), the PROCESS before any title: VLC playing
            // "cod_trailer.mp4" is VLC, not "♪ Call of Duty". Process names keep the looser match for the longer keys
            // ("fortniteclient-win64-shipping" is Fortnite); the short ones must be the whole name.
            foreach (var (key, name) in KnownNames)
            {
                if (ProcessHasKey(procName, key)) { return name; }
            }

            // Then a WORD of the title, never part of one: plain Contains tagged every tab about "decode", "barcode" or
            // "Unicode" — and VS Code, whose process is "code" — "♪ Call of Duty", and a Kickstarter page "♪ Kick".
            //  • A browser's title names a SITE only: a page about "COD" or "Minecraft" playing a video is a web page, not
            //    the game. And "kick" is an everyday word ("Kick-off: Arsenal v Chelsea"): the site is "Kick" only as a
            //    part of the title of its own ("xQc - Kick").
            //  • Another app's title can name a site it shows (the Prime Video and Apple Music apps), or the game it runs
            //    (Minecraft Java is "javaw") — but a player's, a chat's or an editor's title is CONTENT ("Minecraft
            //    trailer.mp4 - PotPlayer"), and no game is named by a key of three letters or fewer ("cod.txt").
            bool contentTitle = !browser && GamePresence.IsNotAGame(procName);
            foreach (var (key, name) in KnownNames)
            {
                bool site = SiteKeys.Contains(key);
                if (browser && !site) { continue; }
                if (!browser && !site && (key.Length <= 3 || contentTitle)) { continue; }
                bool named = key == "kick" ? HasSegment(titleLower, key) : HasWord(titleLower, key);
                if (named) { return name; }
            }

            if (browser)
            {
                // (The old segment-splitting that pulled a site name out of the window
                // title is gone — everything it produced beyond a recognised service was
                // page content. See below.)
                //
                // NOT the page title.
                //
                // This name is painted onto the caption bar — an always-on-top overlay
                // that exists to be looked at, and which is on screen during exactly the
                // situations where other people are looking too: screen sharing, a call,
                // a recording, a stream. Returning the parsed title meant a page whose
                // title has no " - " in it (very common) put the WHOLE title up there:
                // "♪ How to treat depression symptoms · <caption>". An accessibility
                // feature should not broadcast what you are reading or watching to
                // everyone in the meeting.
                //
                // A recognised service is fine — "YouTube" names the app, not the
                // content, and that is what the setting promises ("show audio SOURCE
                // name"). Anything unrecognised falls back to the generic label.
                return "this website";
            }

            // Any other app/game: name the APP, never its window title. A media player's
            // title is the file you are playing, a chat app's is who you are talking to,
            // an office app's is the document — all of it private, and none of it is the
            // "source name" this setting offers to show.
            return PrettyAppName(procName);
        }

        /// <summary>A KnownNames/Browsers key in a process name: the long keys anywhere in it, the short ones ("cod", "cs2",
        /// "arc" — inside "code", "starcraft") only as a whole word.</summary>
        internal static bool ProcessHasKey(string procName, string key) => key.Length > 3 ? procName.Contains(key) : HasWord(procName, key);

        /// <summary><paramref name="key"/> as a word of <paramref name="text"/>: no letter or digit right before or after it.</summary>
        internal static bool HasWord(string text, string key)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(key)) { return false; }
            int at = 0;
            while ((at = text.IndexOf(key, at, StringComparison.Ordinal)) >= 0)
            {
                int end = at + key.Length;
                bool startOk = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
                bool endOk = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (startOk && endOk) { return true; }
                at = at + 1;
            }
            return false;
        }

        /// <summary><paramref name="key"/> as a whole PART of a title — between its " - ", " – ", " | " or ": " separators,
        /// or the whole of it: "xqc - kick" has the part "kick"; "kick-off: arsenal v chelsea" does not.</summary>
        internal static bool HasSegment(string title, string key)
        {
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(key)) { return false; }
            foreach (string part in title.Split(new[] { " - ", " – ", " — ", " | ", ": " }, StringSplitOptions.None))
            {
                if (part.Trim() == key) { return true; }
            }
            return false;
        }

        /// <summary>
        /// A presentable application name from a process name — "vlc" → "VLC",
        /// "chrome" → "Chrome". Never derived from window titles, which carry content.
        /// </summary>
        private static string PrettyAppName(string procName)
        {
            if (string.IsNullOrEmpty(procName)) { return "an app"; }
            string p = procName.Trim();
            // Common short names that look wrong merely capitalised.
            switch (p.ToLowerInvariant())
            {
                case "vlc": return "VLC";
                case "mpc-hc":
                case "mpc-be": return "MPC";
                case "wmplayer": return "Windows Media Player";
                case "msedge": return "Edge";
                case "chrome": return "Chrome";
                case "firefox": return "Firefox";
                case "opera": return "Opera";
                case "brave": return "Brave";
                case "discord": return "Discord";
                case "steam": return "Steam";
            }
            if (p.Length > 24) { p = p.Substring(0, 23).TrimEnd() + "…"; }
            return char.ToUpperInvariant(p[0]) + p.Substring(1);
        }

        /// <summary>
        /// Names the app whose audio session is loudest right now, regardless of
        /// which window is in front, and reports that app's window in
        /// <paramref name="window"/>. "" when nothing relevant is audible.
        ///
        /// This only OBSERVES — the caller decides whether the answer is stable
        /// enough to become the live source (see the debounce in <see cref="Tick"/>).
        /// It used to assign _audioWindow as a side effect, which meant a momentary
        /// challenger could re-point the face analyzer at its window even on ticks
        /// where its name was rejected.
        /// </summary>
        private string NameLoudestAudioSource(out IntPtr window, out uint loudPidOut)
        {
            window = IntPtr.Zero;
            loudPidOut = 0;
            _lookedNow = false;
            _loudestNow = 0;
            try
            {
                if (_audio == null)
                {
                    return "";
                }

                uint loudPid = 0;
                // Seed the winner's bar at the threshold (an app must clear it to count)
                // but the RUNNER-UP at zero. Seeding both meant a lone quiet app was
                // scored against the threshold as if a second app were nearly as loud:
                // one app at peak 0.02 came out as dominance 1 − 0.01/0.02 = 0.5, i.e.
                // "two apps competing", and Tempo warned about a mixed source when only
                // one thing was making noise.
                float loudPeak = SessionPeakThreshold;
                using (MMDevice dev = _audio.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                {
                    var sessions = dev.AudioSessionManager.Sessions;
                    int audible = 0;
                    float runnerUpPeak = 0f;
                    var seen = new HashSet<uint>();
                    // Every audible app's smoothed level, for the media-playing preference below.
                    var levelsNow = new List<KeyValuePair<uint, float>>();
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var s = sessions[i];
                        float peak;
                        uint spid;
                        try
                        {
                            // ACTIVE only. The session list keeps entries for apps that
                            // have merely OPENED audio and for ones that have finished
                            // with it (Inactive / Expired), and nothing here filtered on
                            // that — so a game played earlier in the session could still
                            // win the "loudest" contest and get named as what you are
                            // listening to. That is the "it said COD while I was playing
                            // Rainbow Six" report: a stale session, not a mis-read name.
                            if (s.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive)
                            {
                                continue;
                            }
                            peak = s.AudioMeterInformation.MasterPeakValue;
                            spid = s.GetProcessID;
                        }
                        catch { continue; }

                        if (spid == 0 || peak < SessionPeakThreshold)
                        {
                            continue;
                        }
                        string n = ProcessNameOf(spid);
                        if (n.Length == 0 || IsIgnored(n))
                        {
                            continue;
                        }

                        // Everything past here is a genuinely audible app right now.
                        audible++;
                        seen.Add(spid);
                        // Compare on a SMOOTHED level, not this instant's peak. A game's
                        // peak swings from silence to loud between one poll and the next
                        // (gaps between gunshots, dialogue, footsteps), so an instantaneous
                        // reading hands the title to whatever happened to be making noise
                        // at that millisecond — a Discord blip out-ranking the game you are
                        // actually listening to. Averaging over the last few seconds names
                        // what is sustaining the mix instead of what spiked in it.
                        float level = Smoothed(spid, peak);
                        levelsNow.Add(new KeyValuePair<uint, float>(spid, level));
                        if (level > loudPeak)
                        {
                            if (loudPid != 0) { runnerUpPeak = loudPeak; }
                            loudPeak = level;
                            loudPid = spid;
                        }
                        else if (level > runnerUpPeak)
                        {
                            runnerUpPeak = level;
                        }
                    }

                    // How much of the mix the winner actually owns. Loopback captures
                    // everything mixed together, so when a second app is nearly as loud
                    // the captions are transcribing BOTH — a game plus Discord plus music
                    // is not one speaker, and naming only the loudest hides why the text
                    // reads like nonsense.
                    _audibleAppCount = audible;
                    _sourceDominance = loudPeak > 0f
                        ? (float)Math.Round(1.0 - (runnerUpPeak / loudPeak), 2)
                        : 1f;

                    DecayUnseen(seen);

                    // Hysteresis. Keep naming the app we already named unless the
                    // challenger is clearly, not marginally, louder — and unless the
                    // incumbent has stopped being audible at all. Two apps hovering at
                    // similar levels otherwise trade the title on every poll, and the
                    // source tag on the captions flickers between them.
                    if (_incumbentPid != 0 && loudPid != 0 && loudPid != _incumbentPid &&
                        seen.Contains(_incumbentPid))
                    {
                        float inc;
                        if (_levels.TryGetValue(_incumbentPid, out inc) &&
                            loudPeak < inc * SwitchMargin)
                        {
                            loudPid = _incumbentPid;
                            loudPeak = inc;
                        }
                    }
                    // The app Windows says is PLAYING media, when one is audible, ahead of a louder one that is not
                    // (round 40, see MediaPlaying): the video under a game's soundtrack, not the game.
                    uint mediaPid = 0;
                    float mediaLevel = 0f;
                    if (MediaPlaying != null)
                    {
                        foreach (var kv in levelsNow)
                        {
                            if (kv.Value > mediaLevel && PidPlaysMedia(kv.Key)) { mediaPid = kv.Key; mediaLevel = kv.Value; }
                        }
                    }
                    _mediaNow = mediaPid;
                    if (mediaPid != 0 && loudPid != mediaPid)
                    {
                        loudPid = mediaPid;
                        loudPeak = mediaLevel;
                    }
                    if (loudPid != 0) { _incumbentPid = loudPid; }

                    _audibleNow.Clear();
                    foreach (uint p in seen) { _audibleNow.Add(p); }
                    _loudestNow = loudPid;
                    _lookedNow = true;
                }
                if (loudPid == 0)
                {
                    return "";
                }
                loudPidOut = loudPid;

                string procName = ProcessNameOf(loudPid);

                // If the audible app is also the foreground app, the window title can
                // name it precisely (site names inside browsers) — EXCEPT for a
                // browser whose active tab isn't a recognised media site: the sound
                // may live in a DIFFERENT tab or window of the same browser (reported
                // live: a Google tab in front got credited for audio playing
                // elsewhere in Edge). Then the family scan below looks for a window
                // that IS a known media site before falling back to the active tab.
                string fgTitle = null;
                IntPtr fgWnd = IntPtr.Zero;
                try
                {
                    IntPtr h = GetForegroundWindow();
                    GetWindowThreadProcessId(h, out uint fgPid);
                    if (fgPid != 0 && ProcessNameOf(fgPid).Equals(procName, StringComparison.Ordinal))
                    {
                        var sb = new StringBuilder(512);
                        GetWindowText(h, sb, sb.Capacity);
                        fgTitle = sb.ToString();
                        fgWnd = h;

                        if (!IsBrowserProc(procName) || TitleIsKnown(fgTitle))
                        {
                            window = h;
                            return IdentifySource(procName, fgTitle);
                        }
                    }
                }
                catch { }

                // Background app: known names first, then the process family's window
                // titles. A browser can have several windows and the one making sound
                // is often NOT the active one — so collect ALL titles and prefer the
                // one that names a known media site (the "real" tab), falling back to
                // the longest title (page titles beat stub windows), then the process.
                foreach (var (key, name) in KnownNames)
                {
                    if (ProcessHasKey(procName, key))
                    {
                        return name;
                    }
                }
                try
                {
                    string bestKnown = null;
                    string longest = "";
                    IntPtr bestKnownWnd = IntPtr.Zero, longestWnd = IntPtr.Zero;
                    foreach (var p in Process.GetProcessesByName(procName))
                    {
                        using (p)
                        {
                            string t = CleanTitle(p.MainWindowTitle);
                            if (t.Length == 0)
                            {
                                continue;
                            }
                            if (bestKnown == null)
                            {
                                string lower = t.ToLowerInvariant();
                                foreach (var (key, _) in KnownNames)
                                {
                                    if (HasWord(lower, key)) { bestKnown = t; bestKnownWnd = p.MainWindowHandle; break; }
                                }
                            }
                            if (t.Length > longest.Length)
                            {
                                longest = t;
                                longestWnd = p.MainWindowHandle;
                            }
                        }
                    }
                    // Priority: a window that IS a known media site anywhere in the
                    // family; else the foreground tab we held back above (the active
                    // tab is still the best guess when nothing is recognisably a
                    // media site); else the longest title.
                    string pick = bestKnown
                        ?? (!string.IsNullOrEmpty(fgTitle) ? fgTitle : longest);
                    if (pick.Length > 0)
                    {
                        window = bestKnown != null ? bestKnownWnd
                               : !string.IsNullOrEmpty(fgTitle) ? fgWnd
                               : longestWnd;
                        return IdentifySource(procName, pick);
                    }
                }
                catch { }
                return char.ToUpperInvariant(procName[0]) + procName.Substring(1);
            }
            catch
            {
                return "";
            }
        }

        /// <summary>True when the process name belongs to a known browser.</summary>
        internal static bool IsBrowserProc(string procName)
        {
            foreach (string b in Browsers)
            {
                if (ProcessHasKey(procName, b)) { return true; }
            }
            return false;
        }

        /// <summary>True when a name is exactly one of our known media-site/app display names.</summary>
        private static bool IsKnownSiteName(string name)
        {
            if (string.IsNullOrEmpty(name)) { return false; }
            foreach (var (_, disp) in KnownNames)
            {
                if (string.Equals(name, disp, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        /// <summary>True when a window title names a known media site/game.</summary>
        private static bool TitleIsKnown(string title)
        {
            string lower = CleanTitle(title ?? "").ToLowerInvariant();
            foreach (var (key, _) in KnownNames)
            {
                if (HasWord(lower, key)) { return true; }
            }
            return false;
        }

        /// <summary>True if any process with this executable name has an audible session.</summary>
        private bool ProcessFamilyIsAudible(string procName)
        {
            try
            {
                if (_audio == null)
                {
                    return true;    // no session data — behave like the old title-only detection
                }
                using (MMDevice dev = _audio.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                {
                    var sessions = dev.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var s = sessions[i];
                        float peak;
                        uint spid;
                        try
                        {
                            peak = s.AudioMeterInformation.MasterPeakValue;
                            spid = s.GetProcessID;
                        }
                        catch { continue; }

                        if (peak < SessionPeakThreshold || spid == 0)
                        {
                            continue;
                        }
                        if (ProcessNameOf(spid).Equals(procName, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            catch
            {
                // Session enumeration failed (device switch, COM hiccup): fall back to
                // the device-wide meter so the feature keeps working, just less precise.
                try
                {
                    using (MMDevice dev = _audio.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                    {
                        return dev.AudioMeterInformation.MasterPeakValue > SessionPeakThreshold;
                    }
                }
                catch { return true; }
            }
        }

        /// <summary>Lower-case executable name for a PID (cached — PIDs churn slowly here).</summary>
        private string ProcessNameOf(uint pid)
        {
            // Kept for half a minute, not until the table fills: Windows hands a closed process's number to the next
            // one soon, and a name cached for ever named that newcomer after whatever had the number before — a game
            // started under a closed browser's pid read as the browser.
            if (_pidNames.TryGetValue(pid, out string cached) && _pidNamedAt.TryGetValue(pid, out long at)
                && Environment.TickCount64 - at < 30000)
            {
                return cached;
            }
            string name = "";
            try
            {
                using (var p = Process.GetProcessById((int)pid))
                {
                    name = p.ProcessName.ToLowerInvariant();
                }
            }
            catch { }
            // Small bounded cache, flushed wholesale when large.
            if (_pidNames.Count > 256) { _pidNames.Clear(); _pidNamedAt.Clear(); }
            _pidNames[pid] = name;
            _pidNamedAt[pid] = Environment.TickCount64;
            return name;
        }

        private static bool IsIgnored(string procName)
        {
            foreach (string s in IgnoreProcesses)
            {
                if (procName == s)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
