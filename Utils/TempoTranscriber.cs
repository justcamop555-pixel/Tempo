using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Whisper.net;

namespace AutoClicker.Utils
{
    /// <summary>What audio Tempo's own caption engine listens to.</summary>
    public enum CaptureMode
    {
        /// <summary>Pick automatically: system audio if a speaker exists, else microphone.</summary>
        Auto = 0,
        /// <summary>The PC's audio output (loopback) - needs a playback device.</summary>
        SystemAudio = 1,
        /// <summary>A microphone / input device.</summary>
        Microphone = 2
    }

    /// <summary>
    /// Tempo's own Live Captions engine - an alternative to driving Windows Live
    /// Captions. It captures audio (the PC's output via WASAPI loopback, or a
    /// microphone), down-mixes and resamples to the 16 kHz mono PCM Whisper expects,
    /// and runs Whisper locally to turn it into text. Recognised text is raised on
    /// <see cref="TextRecognized"/> on a background thread; the UI marshals it onto
    /// the caption overlay.
    ///
    /// Important: loopback capture records what a *speaker/playback device* is
    /// playing. On a PC with no audio output device there is nothing to capture, so
    /// the engine now detects that, can fall back to a microphone, and reports an
    /// honest status instead of sitting silently on "Listening…".
    ///
    /// Everything is best-effort and offline: no audio leaves the machine. Whisper
    /// is processed in fixed windows (a few seconds each) on a worker thread, so a
    /// slow machine just produces captions a little behind rather than freezing.
    ///
    /// Note: this depends on NAudio and Whisper.net (NuGet) plus a Whisper model
    /// file on disk (see <see cref="WhisperModelManager"/>). Those restore on a
    /// normal Windows build.
    /// </summary>
    public sealed class TempoTranscriber : IDisposable
    {
        // Whisper requires 16 kHz, mono, 32-bit float samples.
        private const int WhisperSampleRate = 16000;
        // How many seconds of audio to transcribe per pass, and how much of the
        // previous pass to carry forward as context. Whisper is NOT a streaming
        // engine - it needs a couple of seconds of audio to recognise words - so
        // sub-second "live" updates like Windows 11 aren't possible. A ~2.0 s window
        // with ~0.5 s overlap (a fresh result roughly every ~1.5 s) is about as
        // responsive as a local Whisper model gets without hurting accuracy.
        // Window/overlap sizing is an accuracy-vs-latency trade. 2.0 s windows kept
        // captions snappy but gave Whisper so little context that words came out
        // garbled at times; 2.8 s materially improves recognition (whole phrases per
        // pass) while captions still update roughly every couple of seconds. The
        // overlap keeps boundary words seen whole by both passes — the SEAM is where
        // the most visible errors live ("sentence." re-heard as "sentence.
        // sentence"), so it was widened 0.6 → 0.75 s: each pass gets more shared
        // context at the join, and the step even shortens slightly (2.2 → 2.05 s,
        // marginally faster updates). Costs ~12% more compute; trivial for the GPU
        // and the light models, and the too-slow ladder still guards the rest.
        private const double WindowSeconds = 2.8;
        private const double OverlapSeconds = 0.75;
        // The furthest behind live audio captions are ever allowed to fall. Audio
        // older than this is discarded rather than queued (see TranscribeLoop): on an
        // engine that can't keep pace, a generous buffer doesn't rescue the words, it
        // just makes every word arrive late — permanently.
        private const double MaxBacklogSeconds = 3.0;

        private IWaveIn _capture;
        private WhisperFactory _factory;
        private WhisperProcessor _processor;

        private readonly object _bufferLock = new object();
        private readonly List<float> _mono16k = new List<float>();
        // Wakes the transcribe worker the moment fresh audio lands. Without it the
        // worker polled the buffer on a fixed 50 ms timer, adding up to a whole
        // tick of dead waiting to every caption; signalled, the pass starts the
        // instant enough sound has arrived. Never disposed while a worker can
        // still be waiting on it (the instance is reused across start/stop).
        private readonly SemaphoreSlim _samplesReady = new SemaphoreSlim(0, 1);
        private CancellationTokenSource _cts;
        private Task _worker;

        /// <summary>
        /// Guards the hand-off of the native Whisper objects between StartCore (which
        /// creates them and the worker that owns them) and Stop (which decides whether
        /// anyone is left to free them). Held only around those few field assignments —
        /// never across model loading, inference or disposal.
        /// </summary>
        private readonly object _lifecycleLock = new object();
        private volatile bool _running;
        // True while the model is loading on a background thread (before listening
        // actually begins), so we don't double-start and Stop() can cancel a load.
        private volatile bool _starting;

        // Resampling state: the source format (from the capture device) varies, so
        // we convert each captured buffer to 16 kHz mono on the fly.
        private WaveFormat _sourceFormat;

        // Continuous-stream resampler state, carried ACROSS capture buffers. Each
        // 40 ms buffer used to be resampled in isolation, which threw away the
        // fractional read position at every buffer end — on non-integer ratios
        // (44.1 kHz → 16 kHz, the most common mic/speaker rate after 48 kHz) that
        // discarded a fraction of a sample ~25× per second, a periodic timing
        // discontinuity at every seam in the audio Whisper hears. Carrying the
        // phase (and the previous buffer's last sample for interpolation across
        // the boundary) makes the stream seamless, exactly as if the whole session
        // were resampled in one pass. Touched only on the capture thread.
        private double _resamplePos;
        private float _resampleTail;
        private bool _resampleHasTail;

        // Grow-only scratch buffers for the capture hot path (conversion → downmix
        // → resample). The capture callback fires ~25×/s and previously allocated
        // three fresh arrays each time (~1 MB/s of Gen0 garbage while captioning);
        // reusing scratch keeps the steady state allocation-free. Only ever touched
        // on the (single) capture thread.
        private float[] _convScratch = Array.Empty<float>();
        private float[] _monoScratch = Array.Empty<float>();
        private float[] _outScratch = Array.Empty<float>();

        // DC-offset / rumble removal: a one-pole ~20 Hz high-pass on the mono
        // stream. Cheap microphones ride on a DC bias and HVAC-grade rumble that
        // (a) waste auto-gain headroom and (b) inflate every RMS gate (silence,
        // hot-mic, level meter) with energy that carries no speech. R is derived
        // from the capture rate at (re)open; state carries across buffers so the
        // filter is seamless, like the resampler.
        private float _hpR = 0.9974f;
        private float _hpPrevIn, _hpPrevOut;
        private bool _hpFresh = true;

        /// <summary>Flat channel average of one frame (the mic-mode downmix).</summary>
        private static float FlatAverage(float[] samples, int offset, int channels)
        {
            float sum = 0f;
            for (int c = 0; c < channels && offset + c < samples.Length; c++)
            {
                sum += samples[offset + c];
            }
            return channels > 0 ? sum / channels : 0f;
        }

        // Input clipping detector: fraction of samples at the rail, smoothed.
        // Clipped audio transcribes BADLY and looks like an engine problem — the
        // Live debug flag tells the user the fix is upstream (lower the app/system
        // volume), not a model change.
        private float _clipEma;
        /// <summary>True while the captured audio is visibly clipping.</summary>
        public bool InputClipping => _clipEma > 0.02f;

        // Live-debug facts for the newest audio-path features: whether the
        // dialogue-forward surround mix is in force, and how many end-of-utterance
        // EARLY takes have fired (each one is up to a second of latency saved —
        // the counter proves the feature is actually engaging on real content).
        private volatile bool _surroundActive;
        private volatile int _earlyTakes;
        /// <summary>True while ≥6-channel loopback is being centre-mixed for dialogue.</summary>
        public bool SurroundMixActive => _surroundActive;
        /// <summary>End-of-utterance early decodes this session (latency saver).</summary>
        public int EarlyTakes => _earlyTakes;

        // Keep-up telemetry: the ONE number that says whether this model holds
        // real-time pace on this machine (decode time ÷ audio time, smoothed —
        // under 1.0 keeps up, over 1.0 falls behind), plus how many oversized
        // catch-up takes were used to drain a backlog after a hitch.
        private volatile int _rtfX100;
        private volatile int _catchUpTakes;
        /// <summary>Smoothed decode-time ÷ audio-time. Under 1.0 = keeping up. 0 = no data yet.</summary>
        public double RealTimeFactor => _rtfX100 / 100.0;

        /// <summary>
        /// How much audio one inference covers. This is the floor under caption delay:
        /// nothing can be transcribed until its window has finished arriving, so a word
        /// spoken at the START of a window waits this long before decoding even begins.
        /// </summary>
        public static double WindowSizeSeconds => WindowSeconds;

        /// <summary>
        /// Rough delay between a word being SPOKEN and its caption appearing, in seconds.
        ///
        /// Three things add up, and separating them is what makes "captions are behind"
        /// diagnosable instead of a shrug:
        ///   • half the window on average, waiting for the window to fill,
        ///   • whatever audio is already queued ahead of it (the backlog), and
        ///   • the decode itself.
        /// A backlog that keeps growing means the model cannot hold real-time pace on
        /// this machine — see <see cref="RealTimeFactor"/>.
        /// </summary>
        public double EstimatedDelaySeconds
        {
            get
            {
                if (!IsRunning) { return 0; }
                return (WindowSeconds / 2.0) + BacklogSeconds + (_avgInferMs / 1000.0);
            }
        }
        /// <summary>Oversized takes used to drain a queued backlog this session.</summary>
        public int CatchUpTakes => _catchUpTakes;

        // Tracks whether any audio bytes have actually arrived, so a "no audio"
        // watchdog can tell the user nothing is being heard (e.g. no speaker).
        private long _bytesSeen;
        // Whether any NON-silent audio has arrived. With the loopback keep-alive
        // pumping silence, bytes always flow - this flag is what tells "capture is
        // healthy but nothing is playing" from "we're hearing real sound".
        private volatile bool _loudSeen;
        // True while this engine holds the loopback keep-alive (system-audio mode).
        private bool _keepAliveHeld;
        private CaptureMode _activeMode = CaptureMode.Auto;

        // The speaker's volume slider and mute state at the moment capture opened.
        //
        // These matter because WASAPI loopback records the output stream AFTER Windows
        // has applied the volume slider. Captioning system audio at 10% volume hands
        // Whisper a signal roughly a tenth the size of the same video at full volume —
        // and captioning a MUTED speaker hands it digital silence, no matter how loud
        // the video actually is. Neither is visible from the samples alone (quiet and
        // "playing quietly" look identical), so the endpoint is asked directly and the
        // answer is used to explain an empty caption bar instead of leaving the user
        // staring at "Listening…". -1 means "not known / not applicable".
        private volatile float _systemVolume = -1f;
        private volatile bool _systemMuted;
        private long _lastVolumeCheckTick;
        // The last caption text we emitted, used to strip the duplicated overlap
        // that consecutive (overlapping) chunks would otherwise produce.
        private string _lastEmitted = "";
        // Which native engine loaded ("Vulkan" = GPU, "Cpu" = fallback) — shown in
        // the start status so it's obvious why big models are fast (or not).
        private string _runtimeDesc;

        /// <summary>"Vulkan", "Cpu", … once the engine has loaded; null before.</summary>
        public string RuntimeDescription => _runtimeDesc;

        /// <summary>
        /// True while a fullscreen game/video owns the screen. The engine then eases
        /// off: full-length steps only (no earned fast cadence — which multiplies
        /// inference count exactly when the GPU is busiest) and beam search off.
        /// Both restore automatically when the game closes. Captions keep working
        /// throughout — a beat slower, in exchange for the game's frame rate.
        /// </summary>
        public static volatile bool LowImpactMode;

        // ── Live-debug stats (read by the Live Debug window, ~2 Hz) ─────────
        private volatile int _lastInferMs;      // how long the last chunk took
        private volatile int _lastChunkMs;      // how much audio that chunk covered
        private volatile int _backlogMs;        // audio waiting in the buffer
        private volatile string _langState = "auto-detect";
        private volatile int _levelDb = -60;    // loudness of the latest capture buffer
        // Loudest buffer since the meter last read it; see TakeLevelPeakDb.
        private volatile int _levelPeakDb = -60;
        private volatile int _gainX100 = 100;   // auto-gain applied to the last chunk
        private volatile int _chunksDone;       // transcription passes this session
        private volatile int _chunksSilent;     // chunks skipped as silence
        // Skipped chunks whose loudest moment was within 6 dB of the floor.
        private volatile int _chunksNearMiss;
        private volatile int _emits;            // captions actually shown
        private volatile int _avgInferMs;       // smoothed inference time
        private volatile int _droppedMs;        // audio thrown away by backlog trims
        private volatile int _captureBufMs = 100;
        private volatile string _srcDesc;       // "48 kHz · stereo float" etc.
        private long _lastEmitTick;             // TickCount64 of the last caption (Interlocked)
        private long _lastDropLogMs;            // rate-limits the "falling behind" warning

        /// <summary>
        /// When true, every transcription pass logs one [Trace] line (chunk length,
        /// inference time, real-time factor, backlog, gain, the text shown) so the
        /// Live Debug window can watch the pipeline beat by beat. Off by default —
        /// it writes a line every couple of seconds.
        /// </summary>
        public static volatile bool VerboseTrace;

        public int LastInferenceMs => _lastInferMs;
        public int LastChunkMs => _lastChunkMs;
        public double BacklogSeconds => _backlogMs / 1000.0;
        public string LanguageState => _langState;
        /// <summary>Loudness Tempo is hearing right now, in dBFS (−60 silent … 0 max).</summary>
        public int LevelDb => _levelDb;

        /// <summary>
        /// The loudest level heard since this was last called, then reset — what an
        /// input meter should be fed.
        /// </summary>
        /// <remarks>
        /// <see cref="LevelDb"/> stays as the instantaneous value for Live debug, where a
        /// point reading is what you want. The METER needs the peak: it is sampled far
        /// more slowly than the audio arrives, so a plain read shows whichever moment it
        /// happened to land on rather than how loud the input actually was.
        ///
        /// Read-and-reset is deliberately non-atomic with the capture thread's write. The
        /// worst case is one buffer's peak landing in the next window instead of this
        /// one, which is invisible on a meter — and worth far more than a lock on the
        /// audio hot path.
        /// </remarks>
        public int TakeLevelPeakDb()
        {
            int v = _levelPeakDb;
            _levelPeakDb = -60;
            return v;
        }
        /// <summary>Auto-gain multiplier applied to the last transcribed chunk.</summary>
        public double AppliedGain => _gainX100 / 100.0;

        /// <summary>
        /// The speaker's volume as 0-1 while captioning system audio, or -1 when it
        /// isn't known (microphone mode, or a device that won't report it). Loopback is
        /// captured after this slider, so it directly sets how loud captions "hear".
        /// </summary>
        public double SystemVolume => _systemVolume;

        /// <summary>True when the speaker being captioned is muted (loopback = silence).</summary>
        public bool SystemMuted => _systemMuted;
        public int ChunksProcessed => _chunksDone;
        public int SilentChunksSkipped => _chunksSilent;
        /// <summary>Of the skipped chunks, how many were only marginally under the gate.</summary>
        public int NearMissSkips => _chunksNearMiss;
        public int CaptionsEmitted => _emits;
        public int AverageInferenceMs => _avgInferMs;
        /// <summary>Audio lost to backlog trims (engine fell behind), in seconds.</summary>
        public double BacklogDroppedSeconds => _droppedMs / 1000.0;
        /// <summary>WASAPI buffer length actually in use, ms (smaller = less delay).</summary>
        public int CaptureBufferMs => _captureBufMs;
        /// <summary>The capture device's native format ("48 kHz · stereo float").</summary>
        public string SourceFormatDescription => _srcDesc;
        /// <summary>Seconds since the last caption reached the screen; −1 if none yet.</summary>
        public double SecondsSinceLastCaption
        {
            get
            {
                long t = Interlocked.Read(ref _lastEmitTick);
                return t == 0 ? -1 : (Environment.TickCount64 - t) / 1000.0;
            }
        }
        /// <summary>Whether the current processor decodes with beam search.</summary>
        public bool BeamActive => _beamActive;
        private volatile string _cadenceTier = "standard";
        /// <summary>The earned caption cadence: standard / fast ×0.75 / very fast ×0.6.</summary>
        public string CadenceTier => _cadenceTier;

        // ── Own-voice filtering (optional) ───────────────────────────────────
        // When the host attaches a SelfVoiceGuard (mic envelope monitor), chunks
        // whose system-audio envelope matches the microphone's — the user's own
        // voice coming back through sidetone / "Listen to this device" / voice-chat
        // monitoring — are skipped instead of captioned as a phantom speaker.
        /// <summary>Attach/detach the mic-envelope guard; null = no filtering.</summary>
        public volatile SelfVoiceGuard OwnVoiceGuard;
        private volatile int _ownVoiceSkipped;
        private volatile int _lastOwnVoiceSimX100;
        /// <summary>Chunks skipped this session because they were the user's own voice.</summary>
        public int OwnVoiceSkippedChunks => _ownVoiceSkipped;
        /// <summary>Similarity (0..1) between the last checked chunk and the mic envelope.</summary>
        public double LastOwnVoiceSimilarity => _lastOwnVoiceSimX100 / 100.0;
        // Own-voice call: envelopes must clearly match. 0.55 is comfortably above
        // the ~0.2-0.3 an unrelated voice scores against a hot mic, and below the
        // ~0.7-0.9 a monitored copy of the same speech scores.
        private const double OwnVoiceSimilarity = 0.55;

        private static bool _runtimeOrderSet;
        private static bool _runtimeGpuRequested;

        /// <summary>
        /// True once the native engine order has been fixed for this process. After
        /// this, <see cref="ConfigureRuntime"/> is a no-op — the CPU/GPU choice can
        /// only change by restarting Tempo. Callers use this to tell the user their
        /// new setting won't take effect yet, instead of silently ignoring it.
        /// </summary>
        public static bool RuntimeLocked => _runtimeOrderSet;

        /// <summary>Whether the engine order this process locked in was GPU-first.</summary>
        public static bool RuntimeGpuRequested => _runtimeGpuRequested;

        private static string _gpuUnavailableReason;

        /// <summary>
        /// Why the GPU engine was declined this run, or null if it wasn't. Set when the
        /// user asked for the GPU but the machine can't provide one — the case that used
        /// to be an invisible fallback.
        /// </summary>
        public static string GpuUnavailableReason => _gpuUnavailableReason;

        /// <summary>
        /// Set once the CPU engine has had to give up quality to keep pace on a machine
        /// that has a usable GPU engine available. Surfaced in Live debug and used to
        /// offer the switch, instead of silently downgrading the model and leaving the
        /// user to conclude captions are just poor on this PC.
        /// </summary>
        public static bool GpuWouldHelp { get; private set; }

        /// <summary>
        /// Before quietly trading accuracy away, say whether this PC has a way out.
        ///
        /// Shrinking the model is a real loss of quality; a machine with a working
        /// Vulkan device can run the SAME model many times faster instead. Tempo already
        /// knew the answer and never told anyone, so people hit the downgrade and
        /// concluded captions were simply poor on their PC.
        ///
        /// Called from BOTH paths that give up on real time — the beam-drop ladder and
        /// the raw "inference is slower than the audio" check. Hooking only the first one
        /// missed the case that actually fires on a fast CPU with a big model, which is
        /// exactly the machine that has a GPU worth using.
        /// </summary>
        private static void NoteGpuCouldHelp()
        {
            try
            {
                if (GpuWouldHelp || RuntimeGpuRequested || !VulkanProbe.HasUsableDevice) { return; }
                GpuWouldHelp = true;
                Logger.Warn("[Captions] this PC HAS a usable GPU engine (" + VulkanProbe.Summary +
                            "). Turning on Settings → Live Captions → \"Try GPU engine\" and " +
                            "restarting would run this model far faster instead of shrinking it.");
            }
            catch { }
        }

        /// <summary>
        /// Chooses which native engines the loader may use, BEFORE the first model
        /// load (the choice is fixed for the process lifetime). Default: CPU only —
        /// the proven path. With <paramref name="tryGpu"/>, the Vulkan GPU engine is
        /// put first and CPU stays as the automatic fallback if it can't initialise.
        /// </summary>
        public static void ConfigureRuntime(bool tryGpu)
        {
            if (_runtimeOrderSet)
            {
                return;                       // native library already chosen/loaded
            }
            // Ask Vulkan whether a GPU is really there before committing the process to
            // the GPU order. Without this the loader simply fell back to CPU and nothing
            // said so, so "Try GPU engine" could sit ticked forever on a machine that
            // has no Vulkan driver at all while the CPU quietly did all the work.
            if (tryGpu && !VulkanProbe.HasUsableDevice)
            {
                Logger.Warn("[Captions] GPU engine requested but unusable: " + VulkanProbe.Summary +
                            " — staying on the CPU engine.");
                _gpuUnavailableReason = VulkanProbe.Summary;
                tryGpu = false;
            }

            try
            {
                Whisper.net.LibraryLoader.RuntimeOptions.RuntimeLibraryOrder =
                    tryGpu
                        ? new List<Whisper.net.LibraryLoader.RuntimeLibrary>
                        {
                            Whisper.net.LibraryLoader.RuntimeLibrary.Vulkan,
                            Whisper.net.LibraryLoader.RuntimeLibrary.Cpu,
                            Whisper.net.LibraryLoader.RuntimeLibrary.CpuNoAvx
                        }
                        : new List<Whisper.net.LibraryLoader.RuntimeLibrary>
                        {
                            Whisper.net.LibraryLoader.RuntimeLibrary.Cpu,
                            Whisper.net.LibraryLoader.RuntimeLibrary.CpuNoAvx
                        };
                _runtimeOrderSet = true;
                _runtimeGpuRequested = tryGpu;
                Logger.Info("[Captions] engine order: " + (tryGpu ? "GPU (Vulkan) first" : "CPU only") +
                            " (fixed for this run — changing it needs a Tempo restart)");
            }
            catch (Exception ex)
            {
                Logger.Warn("[Captions] couldn't set engine order: " + ex.Message);
            }
        }

        /// <summary>Which audio source the engine should listen to.</summary>
        public CaptureMode Mode { get; set; } = CaptureMode.Auto;

        /// <summary>
        /// "auto" to detect the spoken language, or a Whisper language code ("en",
        /// "es", …) to pin it for the whole session. Read once when a run starts.
        /// </summary>
        public string PreferredLanguage { get; set; } = "auto";

        /// <summary>
        /// The language a run should BUILD its processor with: the pinned code when one
        /// is set, otherwise "auto". English-only models ignore this — they only ever
        /// speak "en" — which is handled at the call sites.
        /// </summary>
        private string StartLanguage()
        {
            string want = (PreferredLanguage ?? "auto").Trim().ToLowerInvariant();
            return (want.Length == 0 || want == "auto") ? "auto" : want;
        }

        /// <summary>True when the user pinned a specific language rather than "auto".</summary>
        private bool LanguagePinned => StartLanguage() != "auto";

        /// <summary>Raised with newly recognised caption text (background thread).</summary>
        public event Action<string> TextRecognized;

        /// <summary>Raised with a human-readable status/error (background thread).</summary>
        public event Action<string> Status;

        /// <summary>
        /// Raised (once per session) when the model repeatedly needs longer to
        /// transcribe a chunk than the chunk lasts — i.e. it can never keep up and
        /// captions would fall further behind forever. The host is expected to
        /// restart the engine with a smaller model.
        /// </summary>
        public event Action RealTimeTooSlow;

        /// <summary>
        /// Fired after MINUTES of sustained speed headroom (~60 consecutive chunks
        /// each finishing in under 30% of their audio length, game mode excluded).
        /// The other half of the too-slow ladder: a downgrade no longer ratchets —
        /// once whatever was hogging the machine stops, the host can step captions
        /// back up toward the model the user actually chose.
        /// </summary>
        public event Action RealTimeHeadroom;
        private int _headroomChunks;
        private int _slowChunks;
        private bool _slowFired;

        public bool IsRunning => _running;

        // True while the engine is running but its audio capture died for good (device
        // unplugged with no replacement, or the reopen budget exhausted). IsRunning
        // stays true, so the host's !IsRunning rescue can't see it — it watches this
        // instead and re-follows the default device when a speaker comes back.
        private volatile bool _captureLost;
        public bool CaptureLost => _captureLost;

        /// <summary>
        /// The capture actually in use, AFTER Auto has been resolved and after any
        /// fallback. <see cref="Mode"/> is only what the UI asked for; this is what the
        /// engine is really listening to.
        ///
        /// The difference is user-visible and was invisible: Auto picks system audio when
        /// a render endpoint exists and the microphone otherwise, and an explicit
        /// SystemAudio request ALSO falls back to the mic when no speaker is found. So a
        /// user on Auto with a disabled speaker gets captioned from their microphone —
        /// captioning the room instead of the video — and nothing said so.
        /// </summary>
        public CaptureMode ActiveMode => _activeMode;

        /// <summary>
        /// True while the model is loading/warming up but before audio is being
        /// transcribed. IsRunning is still false here, so a UI that only knows "on/off"
        /// reports a healthy running engine during what can be a ~20 s wait on the large
        /// model, or reports nothing at all.
        /// </summary>
        public bool IsStarting => _starting;

        /// <summary>
        /// Mean per-segment probability of the most recent decoded chunk (0..1), or -1
        /// before anything has been decoded.
        ///
        /// This is the most direct answer to "why is this transcription nonsense?", and
        /// it was being computed on every pass and thrown away: the processor is built
        /// .WithProbabilities() specifically to obtain it, but the running total lived in
        /// two method locals used only to feed the language lock.
        /// </summary>
        public double LastConfidence => _lastConfidenceX1000 < 0 ? -1.0 : _lastConfidenceX1000 / 1000.0;

        private volatile int _lastConfidenceX1000 = -1;

        /// <summary>
        /// Starts capturing system audio and transcribing with the model at
        /// <paramref name="modelPath"/>. Returns false (with a Status message) if it
        /// can't start - e.g. the model file is missing or audio capture fails.
        /// </summary>
        public bool Start(string modelPath)
        {
            if (_running || _starting) return true;

            if (string.IsNullOrEmpty(modelPath) || !System.IO.File.Exists(modelPath))
            {
                RaiseStatus("No speech model is installed. Add one in Settings > Captions.");
                return false;
            }

            // Load the model and set up audio on a BACKGROUND thread. The model can be
            // 100+ MB and take several seconds to load and build; doing that here on
            // the UI thread is what made Tempo go "Not Responding" while captions were
            // starting. Start returns immediately; status messages report progress.
            _starting = true;
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            RaiseStatus("Loading speech model\u2026");
            Task.Run(() => StartCore(modelPath, token));
            return true;
        }

        /// <summary>
        /// The heavy part of starting captions, run off the UI thread: load and build
        /// the model, open audio capture, and kick off the transcription loop. Checks
        /// the cancellation token at each stage so a Stop() during loading is honoured.
        /// </summary>
        private void StartCore(string modelPath, CancellationToken token)
        {
            // ".en" models only understand English; a multilingual model (no ".en"
            // in the file name, e.g. Large Turbo) detects the spoken language, so
            // any game/video in any language gets captioned.
            bool englishOnly = modelPath != null &&
                modelPath.IndexOf(".en.", StringComparison.OrdinalIgnoreCase) >= 0;
            // Build the Whisper objects into LOCALS and publish to the fields only at
            // the very end. Two overlapping StartCores are possible (Stop() during a
            // multi-second model load, then an immediate re-Start passes the gate):
            // with shared fields, the cancelled run's cleanup used to dispose the NEW
            // run's freshly built engine — dead captions that still said "started" —
            // and Stop()'s null-the-fields-first order made its own cleanup a no-op,
            // leaking the loaded native model. Locals give each run sole ownership of
            // what it built until the moment it hands off to the worker.
            WhisperFactory factory = null;
            WhisperProcessor processor = null;
            try
            {
                // Everything from here until warm-up completes runs inside the NATIVE
                // engine. If it access-violates (seen with large-v3-turbo) the process
                // dies outright and no catch block below will ever run — so record which
                // model we are about to load FIRST. A marker left behind on the next
                // launch is proof this model killed us, and it gets quarantined.
                CaptionCrashGuard.MarkLoading(System.IO.Path.GetFileName(modelPath));

                try
                {
                    factory = WhisperFactory.FromPath(modelPath);
                    processor = BuildProcessor(factory, modelPath, englishOnly ? "en" : StartLanguage());

                    // Report which native engine actually loaded (GPU via Vulkan, or
                    // CPU fallback) — decides whether the big models run real-time.
                    try
                    {
                        var lib = Whisper.net.LibraryLoader.RuntimeOptions.LoadedLibrary;
                        _runtimeDesc = lib.ToString();
                        Logger.Info("[Captions] whisper runtime: " + _runtimeDesc);
                    }
                    catch { _runtimeDesc = null; }

                    // Warm-up: the FIRST inference is far slower than steady state —
                    // the CPU pages the whole model in (GPU: compiles pipelines and
                    // uploads weights). Paying that on live audio opened every
                    // session with a backlog spike and a "dropped n s to catch up"
                    // (measured live: medium on CPU dropped 5.6 s exactly once, at
                    // the start, then held pace). Pay it on 1.5 s of silence
                    // instead, while the status still says the model is loading.
                    try
                    {
                        var warmClock = System.Diagnostics.Stopwatch.StartNew();
                        var silence = new float[(int)(WhisperSampleRate * 1.5)];
                        // NO cancellation token here — for exactly the reason the main
                        // transcribe loop documents further down: cancelling a native
                        // inference mid-pass leaves the engine part-way through, and the
                        // dispose that follows then frees a context the native side is
                        // still unwinding out of. That is a hard access violation which
                        // no try/catch can intercept, and it was reproducible by turning
                        // captions OFF a few seconds after turning them on — right in the
                        // middle of the large model's ~20 s warm-up.
                        //
                        // The warm-up is one fixed 1.5 s buffer, so letting it finish
                        // costs a bounded wait on a background thread that the user never
                        // sees; the cancellation check immediately after it then disposes
                        // safely, once the native call has genuinely returned.
                        var warm = processor.ProcessAsync(silence).GetAsyncEnumerator();
                        try { while (warm.MoveNextAsync().AsTask().GetAwaiter().GetResult()) { } }
                        finally { warm.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                        warmClock.Stop();
                        // Survived the native load AND a real inference — this model is
                        // proven good, so clear the crash marker.
                        CaptionCrashGuard.MarkLoaded();
                        Logger.Info("[Captions] engine warmed up in " + warmClock.ElapsedMilliseconds +
                                    " ms — first real chunk starts at full speed.");
                    }
                    catch (OperationCanceledException)
                    {
                        // Stop() during warm-up: the cancellation check right after
                        // the load block exits cleanly — no error status needed.
                    }
                    catch (Exception wex)
                    {
                        Logger.Info("[Captions] warm-up skipped: " + wex.Message);
                    }
                }
                catch (Exception ex)
                {
                    string msg = ex.Message ?? "";
                    if (msg.IndexOf("runtime", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        msg.IndexOf("library", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        msg.IndexOf("native", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        ex is DllNotFoundException)
                    {
                        RaiseStatus("Tempo's speech engine files are missing from the install. " +
                                    "Reinstall Tempo (or rebuild), making sure the .dll files stay next " +
                                    "to Tempo.exe. Meanwhile, Windows 11 Live Captions works without them.");
                    }
                    else
                    {
                        RaiseStatus("Couldn't load the speech model: " + ex.Message);
                    }
                    try { processor?.Dispose(); } catch { }
                    try { factory?.Dispose(); } catch { }
                    _starting = false;
                    return;
                }

                // The user may have turned captions off while the model was loading.
                if (token.IsCancellationRequested)
                {
                    try { processor?.Dispose(); } catch { }
                    try { factory?.Dispose(); } catch { }
                    _starting = false;
                    return;
                }

                _bytesSeen = 0;
                _loudSeen = false;
                _lastEmitted = "";
                _captureRestarts = 0;   // fresh session, fresh device-change budget
                _slowChunks = 0;
                _slowFired = false;
                // Fresh session, fresh debug numbers.
                _chunksDone = 0;
                _chunksSilent = 0;
                _chunksNearMiss = 0;
                _emits = 0;
                _avgInferMs = 0;
                _droppedMs = 0;
                _rtfX100 = 0;
                _lastConfidenceX1000 = -1;
                _earlyTakes = 0;
                _catchUpTakes = 0;
                _levelDb = -60;
                _gainX100 = 100;
                // Unknown until this session's capture actually opens — never report a
                // volume/mute reading left over from a previous run.
                _systemVolume = -1f;
                _systemMuted = false;
                Interlocked.Exchange(ref _lastEmitTick, 0);
                _capture = CreateCapture(out string captureError, out string captureDesc);
                if (_capture == null)
                {
                    RaiseStatus(captureError ?? "No audio device is available to listen to.");
                    try { processor?.Dispose(); } catch { }
                    try { factory?.Dispose(); } catch { }
                    _starting = false;
                    return;
                }

                if (token.IsCancellationRequested)
                {
                    try { _capture.Dispose(); } catch { }
                    _capture = null;
                    try { processor?.Dispose(); } catch { }
                    try { factory?.Dispose(); } catch { }
                    _starting = false;
                    return;
                }

                _sourceFormat = _capture.WaveFormat;
                _srcDesc = DescribeFormat(_sourceFormat);
                ResetResampler();
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                lock (_bufferLock) { _mono16k.Clear(); }

                _running = true;
                _starting = false;

                // Publishing the processor and CREATING THE WORKER must be atomic with
                // respect to Stop(). They used to be two separate statements, and a Stop
                // landing between them was a use-after-free:
                //
                //   Stop() read `_worker != null` -> false (not assigned yet), saw a
                //   published `_processor`, and disposed it immediately — then this
                //   method carried on and handed those freed native objects to the
                //   worker. The next inference wrote through a dangling pointer and the
                //   process died with an access violation inside ggml-cpu-whisper.dll.
                //
                // That is the "turn captions on, then off, and Tempo vanishes" crash,
                // and the large model's ~20 s load holds the window open the whole time
                // the user is waiting and most likely to change their mind.
                //
                // Under the lock Stop() can only ever see BOTH published (so the worker
                // owns disposal) or NEITHER (so Stop owns it) — never one without the
                // other.
                lock (_lifecycleLock)
                {
                    if (token.IsCancellationRequested)
                    {
                        // Stopped while we were still loading. Don't publish and don't
                        // start a worker for a session the user has already cancelled —
                        // clean up here instead.
                        try { processor?.Dispose(); } catch { }
                        try { factory?.Dispose(); } catch { }
                        _running = false;
                        return;
                    }

                    _processor = processor;
                    _factory = factory;

                // Capture the Whisper objects this run owns. The worker uses these
                // locals and disposes them in its finally - AFTER transcription has
                // fully finished - so the processor is never disposed mid-inference
                // (the cause of intermittent crashes when stopping captions). Using
                // locals (not the fields) also means a quick stop+restart can't make
                // an old worker dispose the new run's processor.
                var holder = new ProcessorHolder { P = processor };
                WhisperFactory ownedFactory = factory;
                string ownedModelPath = modelPath;
                bool multilingual = !englishOnly;
                _worker = Task.Run(async () =>
                {
                    try { await TranscribeLoop(ownedFactory, holder, ownedModelPath, multilingual, token); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { Logger.Warn("Transcribe worker stopped: " + ex.Message); }
                    finally
                    {
                        // holder.P is whatever processor is CURRENT at exit (the
                        // language lock may have swapped it mid-session).
                        try { holder.P?.Dispose(); } catch { }
                        try { ownedFactory?.Dispose(); } catch { }
                    }
                });
                }   // end lock (_lifecycleLock)

                _capture.StartRecording();

                // Loopback goes silent-idle when nothing plays (see LoopbackKeepAlive);
                // hold the keep-alive so the very first moments of any sound are heard.
                EnsureKeepAlive();

                string engineTag = _runtimeDesc == null ? ""
                    : _runtimeDesc.StartsWith("Cpu", StringComparison.OrdinalIgnoreCase)
                        ? " \u00b7 CPU engine"
                        : " \u00b7 GPU engine (" + _runtimeDesc + ")";
                RaiseStatus("Tempo Live Captions started \u00b7 " + captureDesc + engineTag);

                // Watchdog: if no audio arrives within a few seconds, say why instead
                // of leaving the bar on "Listening…" forever.
                StartNoAudioWatchdog();
            }
            catch (Exception ex)
            {
                RaiseStatus("Couldn't start audio capture: " + ex.Message);
                _starting = false;
                Stop();
            }
        }

        /// <summary>
        /// Builds a Whisper processor for <paramref name="language"/> ("auto", "en",
        /// "ja", …). Also used mid-session by the language lock, which re-builds with
        /// a FIXED language once detection has settled — the factory (the loaded
        /// model) is reused, so a rebuild costs a moment, not a model reload.
        ///
        /// Tuning for fewer wrong/garbled words on short caption chunks:
        ///  - A light temperature keeps it from inventing alternatives.
        ///  - "No context" stops one chunk's mistakes from poisoning the next
        ///    (our chunks already overlap, so we don't want cross-chunk drift).
        ///  - Thread count balances "can't keep up" against "starves the UI":
        ///    all PHYSICAL cores minus one, capped at 10 (see TunedThreads —
        ///    the old cap of 6 was starving big CPUs).
        ///  - Beam search on the light models (tiny/base/small, 6+ cores): weighs
        ///    several word paths instead of the first guess and hears clearly
        ///    better. (Whisper.net 1.5's beam was ~30 s per chunk — a runtime bug;
        ///    re-measured healthy on 1.8. The real-time watchdog still guards
        ///    slow PCs.) Medium/Large stay greedy to protect real-time pacing.
        /// </summary>
        // Whether the CURRENT processor decodes with beam search (better hearing,
        // ~2× decode cost). Dropped as the FIRST too-slow response — losing beam is
        // a far smaller sacrifice than shrinking the model or losing the GPU.
        private volatile bool _beamActive;

        [DllImport("kernel32.dll")]
        private static extern bool GetLogicalProcessorInformation(IntPtr buffer, ref uint length);

        private static int _physicalCores;

        /// <summary>
        /// PHYSICAL core count (cached). Hyperthreads share the physical core's
        /// math units — whisper's matrix work gains nothing from them, and
        /// oversubscribing logical cores actively hurts. Falls back to
        /// logical/2 if the OS query fails.
        /// </summary>
        private static int PhysicalCores()
        {
            if (_physicalCores > 0) { return _physicalCores; }
            int cores = 0;
            try
            {
                uint len = 0;
                GetLogicalProcessorInformation(IntPtr.Zero, ref len);
                if (len > 0)
                {
                    IntPtr buf = Marshal.AllocHGlobal((int)len);
                    try
                    {
                        if (GetLogicalProcessorInformation(buf, ref len))
                        {
                            // SYSTEM_LOGICAL_PROCESSOR_INFORMATION: ULONG_PTR mask,
                            // relationship (int, pointer-aligned), 16-byte union.
                            int size = IntPtr.Size * 2 + 16;
                            for (long off = 0; off + size <= len; off += size)
                            {
                                if (Marshal.ReadInt32(buf, (int)off + IntPtr.Size) == 0) // RelationProcessorCore
                                {
                                    cores++;
                                }
                            }
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            catch { }
            if (cores <= 0) { cores = Math.Max(1, Environment.ProcessorCount / 2); }
            _physicalCores = cores;
            return cores;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemCpuSetInformation(IntPtr information, uint bufferLength,
            out uint returnedLength, IntPtr process, uint flags);

        private static int _pCores = -1;   // -1 unknown, 0 = not hybrid, >0 = P-core count

        /// <summary>
        /// PERFORMANCE-core count on hybrid CPUs (Intel P+E designs), 0 when the CPU
        /// isn't hybrid or the query fails. Read from the CPU-set table's
        /// EfficiencyClass: P-cores carry the highest class; distinct CoreIndex
        /// values among them = physical P-cores.
        /// </summary>
        private static int HybridPCores()
        {
            if (_pCores >= 0) { return _pCores; }
            int result = 0;
            try
            {
                GetSystemCpuSetInformation(IntPtr.Zero, 0, out uint len, IntPtr.Zero, 0);
                if (len > 0 && len < 1 << 20)
                {
                    IntPtr buf = Marshal.AllocHGlobal((int)len);
                    try
                    {
                        if (GetSystemCpuSetInformation(buf, len, out len, IntPtr.Zero, 0))
                        {
                            // SYSTEM_CPU_SET_INFORMATION: Size(0), Type(4), Id(8),
                            // Group(12), LogicalProcessorIndex(14), CoreIndex(15),
                            // LastLevelCacheIndex(16), NumaNodeIndex(17),
                            // EfficiencyClass(18). Walk records by their Size field.
                            int off = 0;
                            byte maxClass = 0, minClass = byte.MaxValue;
                            var pCoreIds = new HashSet<int>();
                            var all = new List<(byte cls, int core)>();
                            while (off + 20 <= len)
                            {
                                int size = Marshal.ReadInt32(buf, off);
                                if (size < 20) { break; }
                                int type = Marshal.ReadInt32(buf, off + 4);
                                if (type == 0)             // CpuSetInformation
                                {
                                    byte core = Marshal.ReadByte(buf, off + 15);
                                    byte cls = Marshal.ReadByte(buf, off + 18);
                                    if (cls > maxClass) { maxClass = cls; }
                                    if (cls < minClass) { minClass = cls; }
                                    all.Add((cls, core));
                                }
                                off += size;
                            }
                            if (all.Count > 0 && maxClass > minClass)
                            {
                                foreach (var (cls, core) in all)
                                {
                                    if (cls == maxClass) { pCoreIds.Add(core); }
                                }
                                result = pCoreIds.Count;
                            }
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            catch { result = 0; }
            _pCores = result;
            return result;
        }

        /// <summary>
        /// Decode thread count for the current situation. The old fixed cap of 6
        /// STARVED big CPUs — a 12-core machine ran Large on 6 threads and
        /// "couldn't keep up" purely by configuration. Now: all physical cores
        /// minus one (the UI and audio capture keep a core), capped at 10 where
        /// whisper's scaling flattens into memory bandwidth. During a fullscreen
        /// game the engine takes HALF the cores instead — captions a beat slower,
        /// frame rate protected.
        ///
        /// HYBRID CPUs (Intel P+E): whisper's workers run in LOCK-STEP — every
        /// barrier waits for the slowest thread — so a thread landing on an
        /// E-core drags all the P-cores down to E-core pace. Measured widely in
        /// whisper.cpp: "physical cores − 1" on a 6P+4E chip (9 threads, 3 on
        /// E-cores) loses to 6 threads pinned by count to the P-cores. On hybrid
        /// chips the engine therefore takes exactly the P-core count (the
        /// E-cores are left to the UI and audio capture — better than a reserved
        /// P-core), and half the P-cores during a game.
        /// </summary>
        /// <summary>True when the native engine that loaded is the Vulkan (GPU) one.</summary>
        internal static bool GpuEngineLoaded()
        {
            try
            {
                return string.Equals(
                    Whisper.net.LibraryLoader.RuntimeOptions.LoadedLibrary.ToString(),
                    "Vulkan", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static int TunedThreads(bool lowImpact, bool gpu)
        {
            // On the GPU engine the matrix work is on the graphics card and these threads
            // only feed it — mel, sampling, the CPU-side glue. Handing that a core per
            // thread is waste, and it is waste taken from whatever else is running, which
            // during a game is the thing the captions are FOR. A small pool keeps the
            // encoder fed without bidding against the game for cores.
            if (gpu) { return lowImpact ? 2 : 4; }

            int p = HybridPCores();
            if (p >= 2)
            {
                // Leave one P-core free. The non-hybrid branch below has always reserved
                // a core ("physical cores minus one", per the policy documented on
                // BuildProcessor) and the hybrid branch quietly did not — so on this 6P
                // machine whisper claimed all six and left the foreground app none of
                // them. E-cores carrying the UI doesn't help a game that wants P-cores.
                return lowImpact ? Math.Max(2, p / 2) : Math.Max(2, Math.Min(10, p - 1));
            }
            int phys = PhysicalCores();
            return lowImpact ? Math.Max(2, phys / 2)
                             : Math.Max(2, Math.Min(10, phys - 1));
        }

        // What the CURRENT processor was built with, for the Live-debug stats and
        // for detecting when a game-mode transition needs a rebuild.
        private volatile int _threadsActive;
        public int ThreadsActive => _threadsActive;

        /// <summary>True when decode threads are pinned by count to the P-cores (hybrid CPU).</summary>
        public bool HybridThreadsActive => HybridPCores() >= 2;

        // The boundary-context carry currently in force (headroom-dependent) — an
        // accuracy fact that was previously invisible outside the source.
        private volatile int _carryMsActive;
        /// <summary>Seconds of previous-chunk context carried into each decode.</summary>
        public double CarryContextSeconds => _carryMsActive / 1000.0;

        private WhisperProcessor BuildProcessor(WhisperFactory factory, string modelPath, string language,
            bool allowBeam = true)
        {
            // Which engine actually loaded has to be known BEFORE the thread count is
            // chosen — it was read further down for the beam decision only, so the GPU
            // path was still being handed a full CPU thread pool it had no use for.
            bool gpuLoaded = GpuEngineLoaded();
            int threads = TunedThreads(LowImpactMode, gpuLoaded);
            _threadsActive = threads;
            // Encoder shortcut: Whisper pads EVERY chunk to 30 s and runs its encoder
            // over the whole padded window (1500 audio tokens) even though our chunks
            // are under 3 s. Capping the encoder context to what the audio actually
            // fills (plus generous margin) cuts inference several-fold — the
            // whisper.cpp streaming trick — which is what lets the bigger models keep
            // real-time pace instead of being auto-downgraded.
            int audioCtx = (int)Math.Ceiling((WindowSeconds + OverlapSeconds) / 30.0 * 1500.0) + 96;
            if (audioCtx < 192) { audioCtx = 192; }
            if (audioCtx > 1500) { audioCtx = 1500; }
            var builder = factory.CreateBuilder()
                .WithLanguage(string.IsNullOrEmpty(language) ? "auto" : language)
                .WithThreads(threads)
                .WithNoContext()
                .WithAudioContextSize(audioCtx)
                // Per-segment confidence so obvious phantom words (noise the
                // model wasn't sure about) can be dropped before display.
                .WithProbabilities()
                .WithTemperature(0.0f)
                // Repetition-loop detector, slightly stricter than whisper.cpp's
                // default (2.4): a degenerate low-entropy decode ("the the the…",
                // looping phrases on noisy audio) is retried at a higher
                // temperature instead of being shown. Retries only fire on
                // degenerate segments, so the real-time cost is negligible.
                .WithEntropyThreshold(2.8f)
                // Decoder-level sound-tag ban: tokens OPENING a bracketed
                // annotation ("[Music]", "(applause)") are suppressed at
                // sampling time, so the decoder spends its probability on real
                // words instead of Tempo scrubbing whole hallucinated segments
                // afterwards. The pattern is EXACTLY this shape for a hard
                // reason, proven in an offline harness: whisper's own special
                // tokens are bracketed too ("[_BEG_]", timestamp tokens), and a
                // naive "starts with [" regex suppresses the decoder's internal
                // machinery — first attempt produced ZERO output, second
                // produced hallucinated segment prefixes. "[_…" must stay
                // allowed ("\[($|[^_])"), and no multi-byte characters (♪) may
                // appear in the pattern (byte-level std::regex). Verified: with
                // this pattern, output is byte-identical to no-regex on clean
                // speech.
                .WithSuppressRegex("^ ?(\\[($|[^_])|\\().*");
            string fileName = System.IO.Path.GetFileName(modelPath ?? "").ToLowerInvariant();
            bool lightModel = fileName.Contains("tiny") || fileName.Contains("base") ||
                              fileName.Contains("small");
            // Beam search hears clearly better than greedy at ~2× decode cost.
            // Affordable where speed is abundant: the light models on a multi-core
            // CPU — and EVERY model once the GPU engine is doing the work (measured
            // ~10× real time here). The too-slow ladder drops beam first if a
            // weaker GPU can't afford it after all.
            bool beam = allowBeam && ((lightModel && Environment.ProcessorCount >= 6) || gpuLoaded);
            if (beam)
            {
                builder.WithBeamSearchSamplingStrategy();
                Logger.Info("[Captions] beam-search decoding enabled for " + fileName +
                            (gpuLoaded ? " (GPU headroom)." : "."));
            }
            _beamActive = beam;
            int pCores = HybridPCores();
            Logger.Info("[Captions] decode threads: " + threads + " of " + PhysicalCores() +
                        " physical cores" +
                        (gpuLoaded
                            ? " (GPU engine — the card does the work, so the CPU pool stays small)"
                            : (pCores >= 2
                                ? " (hybrid CPU — P-cores only, " + pCores + "P, one left free; E-cores to the UI)"
                                : "")) +
                        (LowImpactMode ? " (game mode — half the cores)" : "") + ".");
            return builder.Build();
        }

        /// <summary>
        /// Creates the right capture object for the chosen mode, with detection and
        /// (in Auto) a fall back from system audio to a microphone. Returns null with
        /// a clear reason when there is nothing usable to listen to.
        /// </summary>
        private IWaveIn CreateCapture(out string error, out string description)
        {
            error = null;
            description = "";

            bool hasRender = false;
            bool hasCapture = false;
            try
            {
                using (var en = new MMDeviceEnumerator())
                {
                    hasRender = en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Count > 0;
                    hasCapture = en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).Count > 0;
                }
            }
            catch
            {
                // If enumeration fails, fall through and just try loopback.
                hasRender = true;
            }

            CaptureMode mode = Mode;
            if (mode == CaptureMode.Auto)
            {
                mode = hasRender ? CaptureMode.SystemAudio : CaptureMode.Microphone;
            }

            if (mode == CaptureMode.SystemAudio)
            {
                if (!hasRender)
                {
                    // No speaker/output device at all: loopback can't work. Try mic.
                    if (hasCapture)
                    {
                        _activeMode = CaptureMode.Microphone;
                        description = "microphone (no speaker found)";
                        return OpenMicCapture();
                    }
                    error = "This PC has no speaker/playback device, so Tempo can't capture system audio. " +
                            "Plug in or enable an output device, or connect a microphone, to use Tempo's own captions.";
                    return null;
                }
                _activeMode = CaptureMode.SystemAudio;
                // Name the actual device so the status line answers "which speaker
                // is Tempo hearing?" at a glance.
                string spk = DefaultDeviceName(DataFlow.Render);
                description = spk == null ? "system audio" : "system audio · " + spk;
                return OpenLoopbackCapture();
            }

            // Microphone mode.
            if (!hasCapture)
            {
                error = "No microphone or input device was found for Tempo's captions.";
                return null;
            }
            _activeMode = CaptureMode.Microphone;
            string micName = DefaultDeviceName(DataFlow.Capture);
            description = micName == null ? "microphone" : "microphone · " + micName;
            return OpenMicCapture();
        }

        // NAudio's stock capture classes are hard-wired to a 100 ms WASAPI buffer —
        // a tenth of a second every caption spends just WAITING inside the OS before
        // Whisper can even see the audio. 40 ms keeps the same shared-mode plumbing
        // (poll cadence becomes ~20 ms) while cutting that queue by more than half.
        // Small enough to matter, big enough to never glitch on a busy machine.
        private const int WantedCaptureBufferMs = 40;

        /// <summary>
        /// WASAPI loopback capture with a caller-chosen buffer length. This is
        /// exactly NAudio's WasapiLoopbackCapture (same Loopback stream flag on top
        /// of the base flags) except the buffer isn't fixed at 100 ms.
        /// </summary>
        private sealed class LowLatencyLoopbackCapture : WasapiCapture
        {
            public LowLatencyLoopbackCapture(MMDevice device, int bufferMs)
                : base(device, false, bufferMs) { }

            protected override AudioClientStreamFlags GetAudioClientStreamFlags()
            {
                return AudioClientStreamFlags.Loopback | base.GetAudioClientStreamFlags();
            }
        }

        /// <summary>
        /// Opens system-audio (loopback) capture with the small buffer, falling back
        /// to the stock 100 ms NAudio class if anything about the fast path fails —
        /// captions must keep working on every device, just possibly a beat slower.
        /// </summary>
        private IWaveIn OpenLoopbackCapture()
        {
            try
            {
                MMDevice dev;
                using (var en = new MMDeviceEnumerator())
                {
                    // The user's CHOSEN speaker when one is picked; Windows' default
                    // otherwise (or when the chosen device has been unplugged).
                    dev = AudioDeviceSelection.Resolve(en, DataFlow.Render, out bool fellBack);
                    if (fellBack)
                    {
                        Logger.Warn("[Captions] chosen speaker not found - capturing the default output instead.");
                    }
                }
                if (dev == null) { throw new InvalidOperationException("no playback device"); }
                ReadEndpointVolume(dev);
                var cap = new LowLatencyLoopbackCapture(dev, WantedCaptureBufferMs);
                _captureBufMs = WantedCaptureBufferMs;
                return cap;
            }
            catch (Exception ex)
            {
                Logger.Warn("[Captions] low-latency loopback unavailable (" + ex.Message +
                            "); using the standard 100 ms capture.");
                _captureBufMs = 100;
                return new WasapiLoopbackCapture();
            }
        }

        /// <summary>
        /// Records the speaker's volume level and mute state so an empty caption bar can
        /// be explained. Best-effort: a device that refuses the volume interface simply
        /// leaves the values unknown, and nothing downstream depends on them.
        /// </summary>
        private void ReadEndpointVolume(MMDevice dev)
        {
            try
            {
                var vol = dev.AudioEndpointVolume;
                if (vol == null) { return; }
                _systemVolume = vol.MasterVolumeLevelScalar;
                _systemMuted = vol.Mute;
                if (_systemMuted)
                {
                    Logger.Warn("[Captions] the speaker is MUTED — loopback will only capture silence.");
                }
                else if (_systemVolume >= 0f && _systemVolume < 0.10f)
                {
                    Logger.Warn("[Captions] speaker volume is very low (" +
                                Math.Round(_systemVolume * 100) + "%) — system audio will be faint to caption.");
                }
            }
            catch
            {
                _systemVolume = -1f;
                _systemMuted = false;
            }
        }

        /// <summary>Re-reads the speaker's volume/mute; the slider can move mid-session.</summary>
        private void RefreshEndpointVolume()
        {
            if (_activeMode != CaptureMode.SystemAudio) { return; }
            try
            {
                using (var en = new MMDeviceEnumerator())
                using (var dev = AudioDeviceSelection.Resolve(en, DataFlow.Render, out _))
                {
                    if (dev != null) { ReadEndpointVolume(dev); }
                }
            }
            catch { }
        }

        /// <summary>Microphone capture with the small buffer; same fallback story.</summary>
        private IWaveIn OpenMicCapture()
        {
            try
            {
                MMDevice dev;
                using (var en = new MMDeviceEnumerator())
                {
                    // The chosen microphone when picked; NAudio's default otherwise.
                    dev = AudioDeviceSelection.Resolve(en, DataFlow.Capture, out bool fellBack);
                    if (fellBack)
                    {
                        Logger.Warn("[Captions] chosen microphone not found - using the default input instead.");
                    }
                }
                if (dev == null) { throw new InvalidOperationException("no capture device"); }
                var cap = new WasapiCapture(dev, false, WantedCaptureBufferMs);
                _captureBufMs = WantedCaptureBufferMs;
                return cap;
            }
            catch (Exception ex)
            {
                Logger.Warn("[Captions] low-latency microphone capture unavailable (" + ex.Message +
                            "); using the standard 100 ms capture.");
                _captureBufMs = 100;
                return new WasapiCapture();
            }
        }

        /// <summary>"48 kHz · stereo float" style summary of a capture format.</summary>
        private static string DescribeFormat(WaveFormat f)
        {
            if (f == null) { return null; }
            string ch = f.Channels == 1 ? "mono"
                      : f.Channels == 2 ? "stereo"
                      : f.Channels + " ch";
            string enc = f.Encoding == WaveFormatEncoding.IeeeFloat
                ? "float" : f.BitsPerSample + "-bit";
            return (f.SampleRate / 1000.0).ToString("0.#") + " kHz · " + ch + " " + enc;
        }

        /// <summary>Name of the device captions will actually use for a flow, or null.</summary>
        private static string DefaultDeviceName(DataFlow flow)
        {
            try
            {
                using (var en = new MMDeviceEnumerator())
                using (var dev = AudioDeviceSelection.Resolve(en, flow, out _))
                {
                    string name = dev?.FriendlyName;
                    return string.IsNullOrWhiteSpace(name) ? null : name;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Holds the loopback keep-alive while (and only while) this engine captures
        /// SYSTEM audio; a microphone capture doesn't need it. Balanced against
        /// <see cref="ReleaseKeepAlive"/> so Acquire/Release never drift.
        /// </summary>
        private void EnsureKeepAlive()
        {
            bool want = _activeMode == CaptureMode.SystemAudio;
            if (want && !_keepAliveHeld)
            {
                _keepAliveHeld = true;
                LoopbackKeepAlive.Acquire();
            }
            else if (!want && _keepAliveHeld)
            {
                _keepAliveHeld = false;
                LoopbackKeepAlive.Release();
            }
        }

        private void ReleaseKeepAlive()
        {
            if (_keepAliveHeld)
            {
                _keepAliveHeld = false;
                LoopbackKeepAlive.Release();
            }
        }

        private void StartNoAudioWatchdog()
        {
            CancellationToken token = _cts != null ? _cts.Token : CancellationToken.None;
            Task.Run(async () =>
            {
                try
                {
                    try { await Task.Delay(6000, token); } catch { return; }
                    if (!_running || token.IsCancellationRequested) return;
                    // Two distinct "nothing heard" cases: no bytes at all (capture is
                    // dead / no device) and bytes that are ALL silence (keep-alive is
                    // pumping but no app is actually playing sound). Both deserve the
                    // honest explanation instead of an eternal "Listening…".
                    if (System.Threading.Interlocked.Read(ref _bytesSeen) == 0 || !_loudSeen)
                    {
                        if (_activeMode == CaptureMode.SystemAudio)
                        {
                            // Re-read the slider first: it may well have moved since
                            // capture opened, and these two cases produce a perfectly
                            // healthy capture that carries nothing but silence \u2014 the
                            // single most confusing way for captions to "not work".
                            RefreshEndpointVolume();
                            if (_systemMuted)
                            {
                                RaiseStatus("Your speaker is MUTED, so there is no sound for Tempo to caption. " +
                                            "Unmute it (the speaker icon by the clock) and captions will start. " +
                                            "Tempo captions what your PC plays \u2014 muting silences that too.");
                            }
                            else if (_systemVolume >= 0f && _systemVolume < 0.08f)
                            {
                                RaiseStatus("Your speaker volume is only " + Math.Round(_systemVolume * 100) +
                                            "%, which is too faint to caption reliably. Turn it up, " +
                                            "or switch the caption source to a microphone.");
                            }
                            else
                            {
                                RaiseStatus("No audio is playing, so there's nothing to caption yet. " +
                                            "Tempo hears your PC's sound \u2014 play something with audio. " +
                                            "(If this PC has no speaker, switch the caption source to a microphone.)");
                            }
                        }
                        else
                        {
                            RaiseStatus("No sound is reaching the microphone yet. Check it's connected and not muted.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("Caption watchdog error: " + ex.Message);
                }
            });
        }

        public void Stop()
        {
            _running = false;
            _starting = false;
            ReleaseKeepAlive();
            try { _capture?.StopRecording(); } catch { }
            try { _cts?.Cancel(); } catch { }

            try
            {
                if (_capture != null)
                {
                    _capture.DataAvailable -= OnDataAvailable;
                    _capture.RecordingStopped -= OnRecordingStopped;
                    _capture.Dispose();
                }
            }
            catch { }
            _capture = null;

            // Do NOT dispose the Whisper processor here, and do NOT block the UI thread
            // waiting for the worker. The worker disposes its own Whisper objects in its
            // finally once transcription has actually finished, so we never dispose
            // mid-inference (the cause of the intermittent crash on stop) and the UI
            // never freezes. We only detach the fields. StartCore now builds on locals
            // and publishes late, so before the worker exists these fields are normally
            // null and its own paths clean up — the snapshot-dispose below covers the
            // narrow window where the fields were published but _worker wasn't assigned
            // yet (double-Dispose is try-swallowed on both sides, so the overlap with
            // the worker's finally is harmless).
            // Read the worker AND the Whisper objects together, under the same lock
            // StartCore publishes them under. Reading `_worker` separately (as this used
            // to, at the very top of Stop) let a start that had published its processor
            // but not yet assigned its worker look like "no worker owns this" — so Stop
            // freed the native objects and the worker then used them. Taking both at
            // once means we either see a worker (it disposes, after inference ends) or
            // no session at all (we dispose). There is no longer an in-between.
            Task worker;
            WhisperProcessor orphanP;
            WhisperFactory orphanF;
            lock (_lifecycleLock)
            {
                worker = _worker;
                orphanP = _processor;
                orphanF = _factory;
                _processor = null;
                _factory = null;
                _worker = null;
            }

            if (worker == null)
            {
                // No worker was ever started for these, so nothing else will free them.
                try { orphanP?.Dispose(); } catch { }
                try { orphanF?.Dispose(); } catch { }
            }

            // Stale-state hygiene: a session that STOPPED while clipping must not
            // keep reporting "input clipping" through the stopped period and the
            // next session's model load.
            _clipEma = 0f;
            _hpPrevIn = 0f;
            _hpPrevOut = 0f;
            _hpFresh = true;

            lock (_bufferLock) { _mono16k.Clear(); }
        }

        private int _captureRestarts;

        // Serializes the two capture-reopen paths (default-device follow and the
        // stopped-with-error recovery). They used to run concurrently on separate
        // Task threads when a device unplug fired BOTH signals at once — each opened
        // its own capture, one got orphaned still recording with the data handler
        // attached, and two capture threads then corrupted the single-threaded
        // filter/scratch state until captions were toggled. One reopen at a time;
        // a second request while one is in flight is simply dropped (the winner is
        // already opening the current default device).
        private int _reopening;

        /// <summary>
        /// Tears down a capture opened by a reopen that raced <see cref="Stop"/>, and
        /// reports whether it did.
        ///
        /// Both reopen paths run on a task, and opening a WASAPI device is slow —
        /// device enumeration plus client init, comfortably long enough for the user
        /// to switch captions off in the middle of it. Stop() has by then already run
        /// past the point where it disposes the capture and releases the keep-alive,
        /// so it will never come back for one opened afterwards. Left alone that
        /// capture keeps recording with the session stopped: on a microphone the
        /// Windows "in use" indicator stays lit and the device stays held, the
        /// keep-alive ref taken beside it keeps rendering silence to the speaker, and
        /// nothing disposes either until Tempo exits (a later Start overwrites
        /// _capture, orphaning it for good). So the reopen has to clean up after
        /// itself.
        /// </summary>
        private bool AbandonReopenIfStopped(IWaveIn fresh, string where)
        {
            if (_running) { return false; }
            try
            {
                if (fresh != null)
                {
                    fresh.DataAvailable -= OnDataAvailable;
                    fresh.RecordingStopped -= OnRecordingStopped;
                    try { fresh.StopRecording(); } catch { }
                    fresh.Dispose();
                }
            }
            catch { }
            // Only clear the field if it is still OURS: a Start that came after the
            // Stop has every right to have published its own capture by now.
            if (ReferenceEquals(_capture, fresh)) { _capture = null; }
            ReleaseKeepAlive();
            Logger.Info("[Captions] " + where + " abandoned — captions were stopped while the device was opening.");
            return true;
        }

        /// <summary>
        /// Re-opens audio capture on the CURRENT default device. Called when the
        /// user switches the default output in Windows while the OLD device is
        /// still present: no RecordingStopped fires then (the old device is alive,
        /// just silent), so without this captions keep listening to the wrong
        /// device — deaf until toggled. Safe to call any time while running.
        /// </summary>
        public void FollowDefaultDevice()
        {
            if (!_running)
            {
                return;
            }
            System.Threading.Tasks.Task.Run(() =>
            {
                if (System.Threading.Interlocked.CompareExchange(ref _reopening, 1, 0) != 0)
                {
                    return;    // another reopen is mid-flight — it opens the same default
                }
                try
                {
                    // Stop() can land between the caller's _running check and this task
                    // being scheduled. The recovery path below has always re-checked
                    // here; this one did not, which is how a stopped session could be
                    // left with a live capture.
                    if (!_running) { return; }

                    var old = _capture;
                    _capture = null;
                    try
                    {
                        if (old != null)
                        {
                            old.DataAvailable -= OnDataAvailable;
                            old.RecordingStopped -= OnRecordingStopped;
                            old.StopRecording();
                            old.Dispose();
                        }
                    }
                    catch { }

                    var fresh = CreateCapture(out string err, out string desc);
                    if (fresh == null)
                    {
                        _captureLost = true;   // still deaf — a later speaker arrival retries
                        RaiseStatus(err ?? "The default audio device changed and no replacement was found.");
                        return;
                    }
                    if (AbandonReopenIfStopped(fresh, "follow-device reopen")) { return; }

                    _sourceFormat = fresh.WaveFormat;
                    _srcDesc = DescribeFormat(_sourceFormat);
                    ResetResampler();
                    fresh.DataAvailable += OnDataAvailable;
                    fresh.RecordingStopped += OnRecordingStopped;
                    _capture = fresh;
                    fresh.StartRecording();
                    _captureLost = false;      // recovered
                    _captureRestarts = 0;      // fresh budget for the next incident
                    EnsureKeepAlive();
                    // Recording is live and the keep-alive ref is taken, so a Stop that
                    // lands here has to be UNDONE rather than merely skipped. Placed
                    // after EnsureKeepAlive so the release covers the ref it just took.
                    if (AbandonReopenIfStopped(fresh, "follow-device reopen")) { return; }
                    Logger.Info("[Captions] capture following new default device · " + desc);
                }
                catch (Exception ex)
                {
                    Logger.Warn("[Captions] follow-device reopen failed: " + ex.Message);
                }
                finally
                {
                    System.Threading.Volatile.Write(ref _reopening, 0);
                }
            });
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            if (e?.Exception == null)
            {
                return;                             // normal Stop() — nothing to do
            }
            Logger.Warn("Audio capture stopped with error: " + e.Exception.Message);

            // The usual cause is the audio device changing while captions run (head-
            // phones plugged in, an app switching output). The Whisper engine and the
            // transcribe loop are still healthy — only the CAPTURE died — so reopen it
            // on the new default device instead of telling the user to toggle captions.
            if (_running && _captureRestarts < 3)
            {
                _captureRestarts++;
                int attempt = _captureRestarts;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    bool tookGuard = false;
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(1200);
                        if (!_running)
                        {
                            return;                 // user turned captions off meanwhile
                        }
                        if (System.Threading.Interlocked.CompareExchange(ref _reopening, 1, 0) != 0)
                        {
                            return;                 // a follow-device reopen beat us to it
                        }
                        tookGuard = true;
                        var old = _capture;
                        _capture = null;
                        try
                        {
                            if (old != null)
                            {
                                old.DataAvailable -= OnDataAvailable;
                                old.RecordingStopped -= OnRecordingStopped;
                                old.Dispose();
                            }
                        }
                        catch { }

                        var fresh = CreateCapture(out string err, out string desc);
                        if (fresh == null)
                        {
                            _captureLost = true;   // deaf — a later speaker arrival retries
                            RaiseStatus(err ?? "The audio device changed and no replacement was found. " +
                                "Turn Live Captions off and on again once a device is back.");
                            return;
                        }
                        // The _running check above happens BEFORE CreateCapture, and
                        // opening a device is slow enough to lose the race on its own.
                        if (AbandonReopenIfStopped(fresh, "capture recovery")) { return; }

                        _sourceFormat = fresh.WaveFormat;
                        _srcDesc = DescribeFormat(_sourceFormat);
                        ResetResampler();
                        fresh.DataAvailable += OnDataAvailable;
                        fresh.RecordingStopped += OnRecordingStopped;
                        _capture = fresh;
                        fresh.StartRecording();
                        _captureLost = false;      // recovered
                        _captureRestarts = 0;      // fresh budget for the next incident
                        // Follow the device change: re-point the keep-alive at the new
                        // default speaker (mode may also have flipped system<->mic).
                        EnsureKeepAlive();
                        if (AbandonReopenIfStopped(fresh, "capture recovery")) { return; }
                        if (_keepAliveHeld) { LoopbackKeepAlive.Poke(); }
                        Logger.Info("[Audio] capture reopened after device change (attempt " + attempt + ") · " + desc);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("Audio capture reopen failed: " + ex.Message);
                        RaiseStatus("Captions lost the audio device and couldn't reconnect. " +
                                    "Turn Live Captions off and on again to resume.");
                    }
                    finally
                    {
                        // Release only if WE took the guard — otherwise this would
                        // clobber the other reopen path's in-flight hold.
                        if (tookGuard) { System.Threading.Volatile.Write(ref _reopening, 0); }
                    }
                });
            }
            else if (_running)
            {
                _captureLost = true;   // budget exhausted — a later speaker arrival retries
                RaiseStatus("Captions stopped because the audio device kept changing. " +
                            "Turn Live Captions off and on again to resume.");
            }
        }

        /// <summary>
        /// Converts each captured buffer to 16 kHz mono float samples and appends
        /// them to the shared buffer the worker drains.
        /// </summary>
        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            // This runs on an audio capture thread. Any exception that escapes here
            // would crash the whole app (unhandled background-thread exceptions are
            // fatal in .NET), so everything is wrapped defensively. A bad/short audio
            // buffer should just be skipped, never bring Tempo down.
            try
            {
                if (!_running || _sourceFormat == null || e.BytesRecorded <= 0) return;
                // Only the CURRENT capture may feed the pipeline. If a reopen ever
                // orphans an old capture with this handler still attached (the
                // reopen-race window), its callbacks go inert here instead of two
                // capture threads concurrently corrupting the single-threaded
                // scratch/filter/resampler state.
                if (!ReferenceEquals(sender, _capture)) return;

                System.Threading.Interlocked.Add(ref _bytesSeen, e.BytesRecorded);

                int sampleCount = ConvertToFloat(e.Buffer, e.BytesRecorded, _sourceFormat, out int channels);
                if (sampleCount <= 0 || channels < 1) return;
                float[] samples = _convScratch;

                // Down-mix to mono. STEREO and mono average as before; SURROUND
                // (5.1/7.1 over HDMI/DP — standard WAVE order FL FR C LFE RL RR …)
                // is centre-weighted instead: film/game DIALOGUE lives almost
                // entirely in the centre channel, and the old flat average diluted
                // it under music, effects and LFE rumble from the other five-plus
                // channels — audibly worse recognition on exactly the content
                // people caption. LFE is skipped outright (no speech below 120 Hz).
                // The same pass applies the ~20 Hz high-pass (DC/rumble removal),
                // counts rail-hitting samples for the clipping flag, and
                // accumulates the level meter — every sample is already in hand.
                int frames = sampleCount / channels;
                if (frames <= 0) return;
                if (_monoScratch.Length < frames) { _monoScratch = new float[frames]; }
                float[] mono = _monoScratch;
                // The 5.1 slot map is only trustworthy for LOOPBACK: a ≥6-channel
                // CAPTURE device is a multichannel interface (mixer, Scarlett-class),
                // where "channel 3" is someone's microphone, not an LFE — the
                // speaker-layout mix would zero-weight it and kill captions on a
                // perfectly working device. Mic mode always flat-averages.
                bool surround = channels >= 6 && _activeMode == CaptureMode.SystemAudio;
                _surroundActive = surround;
                double levelSq = 0;
                int clipped = 0;
                float hpR = _hpR, hpIn = _hpPrevIn, hpOut = _hpPrevOut;
                if (_hpFresh && frames > 0)
                {
                    // Prime the DC blocker: seeding x[n-1] with the first sample makes
                    // y0 = 0 for constant input, so a biased mic's DC never leaks one
                    // raw sample into _loudSeen / the level meter at (re)open.
                    _hpFresh = false;
                    hpIn = surround
                        ? samples[2] * 0.478f + (samples[0] + samples[1]) * 0.174f
                            + (samples[4] + samples[5]) * 0.087f
                        : FlatAverage(samples, 0, channels);
                }
                for (int i = 0; i < frames; i++)
                {
                    int b = i * channels;
                    float m;
                    // Clipping is judged on the CHANNELS, not the mix — one railed
                    // channel is real distortion even when the mono sum stays
                    // comfortably under the rail (and a >1-sum mix must never be the
                    // judge, or loud clean passages read as "clipping").
                    if (surround)
                    {
                        float fl = samples[b], fr = samples[b + 1], ce = samples[b + 2];
                        float rl = samples[b + 4], rr = samples[b + 5];
                        if (ce > 0.985f || ce < -0.985f || fl > 0.985f || fl < -0.985f ||
                            fr > 0.985f || fr < -0.985f || rl > 0.985f || rl < -0.985f ||
                            rr > 0.985f || rr < -0.985f) { clipped++; }
                        // Dialogue-forward 5.1 mix, weights normalised to sum 1.0 so
                        // legal input can never exceed the source's own peak (a 1.15
                        // sum pushed loud correlated passages past ±1 and the range
                        // clamp then manufactured the very distortion the clipping
                        // flag warns about).
                        m = ce * 0.478f + (fl + fr) * 0.174f + (rl + rr) * 0.087f;
                    }
                    else
                    {
                        float sum = 0f;
                        bool rail = false;
                        for (int c = 0; c < channels; c++)
                        {
                            float v = samples[b + c];
                            if (v > 0.985f || v < -0.985f) { rail = true; }
                            sum += v;
                        }
                        if (rail) { clipped++; }
                        m = sum / channels;
                    }
                    // One-pole high-pass, seamless across buffers.
                    float y = m - hpIn + hpR * hpOut;
                    hpIn = m;
                    hpOut = y;
                    mono[i] = y;
                    levelSq += (double)y * y;
                }
                _hpPrevIn = hpIn;
                _hpPrevOut = hpOut;
                _clipEma = _clipEma * 0.9f + (clipped / (float)frames) * 0.1f;
                double rmsNow = Math.Sqrt(levelSq / frames);
                double db = rmsNow > 0 ? 20.0 * Math.Log10(rmsNow) : -60.0;
                int dbNow = db < -60 ? -60 : db > 0 ? 0 : (int)db;
                _levelDb = dbNow;

                // Also keep the LOUDEST level since the meter last looked.
                //
                // This runs on every capture buffer — about 25 times a second — while the
                // input meter reads it on the 200 ms UI tick. So the meter was seeing one
                // buffer in five and simply not being shown the other four. Speech is
                // spiky at exactly that timescale: land on the gap between two syllables
                // and the bar reads near-silence while someone is plainly talking, and
                // the peak-hold marker above it holds the highest of those lucky samples
                // rather than the real one.
                if (dbNow > _levelPeakDb) { _levelPeakDb = dbNow; }

                // Feed the "have we heard anything real yet" watchdog flag. Strided
                // scan keeps this near-free on the hot capture path.
                if (!_loudSeen)
                {
                    for (int i = 0; i < frames; i += 16)
                    {
                        float v = mono[i];
                        if (v > 0.004f || v < -0.004f) { _loudSeen = true; break; }
                    }
                }

                // Resample mono to 16 kHz — SEAMLESSLY across buffer boundaries (the
                // fractional read position and the boundary sample carry over), so
                // Whisper hears one continuous stream, not 25 stitched snippets/s.
                int outCount = ResampleInto(mono, frames, _sourceFormat.SampleRate, WhisperSampleRate);
                if (outCount <= 0) return;

                lock (_bufferLock)
                {
                    _mono16k.AddRange(new ReadOnlySpan<float>(_outScratch, 0, outCount));
                }

                // Nudge the worker: fresh audio is in. CurrentCount check keeps the
                // semaphore at most 1 (a wake-up flag, not a counter); the rare race
                // where two capture callbacks both pass the check is absorbed by the
                // catch — an extra wake-up is harmless, the loop re-checks the buffer.
                if (_samplesReady.CurrentCount == 0)
                {
                    try { _samplesReady.Release(); } catch (SemaphoreFullException) { }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Audio capture buffer skipped: " + ex.Message);
            }
        }

        /// <summary>Mutable owner of the live processor so the language lock can swap it.</summary>
        private sealed class ProcessorHolder
        {
            public WhisperProcessor P;
        }

        /// <summary>
        /// Session language lock for multilingual models. Auto-detection runs per
        /// chunk and can FLIP on a noisy/musical chunk, transcribing a stretch in
        /// the wrong language. Once several consecutive chunks agree, the language
        /// is locked (also skipping per-chunk detection cost); if confidence later
        /// collapses for several chunks — the sign the locked language went wrong,
        /// e.g. the user switched to a video in another language — it unlocks and
        /// re-detects. Pure decision logic; the caller performs the actual swap.
        /// </summary>
        private sealed class LanguageLock
        {
            internal const int LockAfter = 4;       // votes the leader needs to win
            private const int UnlockAfter = 3;      // consecutive low-confidence chunks
            private const double LowProb = 0.35;
            private const int MaxVotes = 6;         // cap so a long session can still change its mind

            private readonly Dictionary<string, int> _votes =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            private string _locked;                 // null = auto-detect
            private int _lowProbStreak;

            /// <summary>The language currently ahead in the vote, or null before any.</summary>
            public string Leading { get; private set; }

            /// <summary>How many votes <see cref="Leading"/> has, out of <see cref="LockAfter"/>.</summary>
            public int LeadingScore { get; private set; }

            /// <summary>
            /// Feed one emission's detected language + average confidence. Returns
            /// the language the processor SHOULD be rebuilt with ("auto" to resume
            /// detection, a code like "en" to lock), or null for no change.
            ///
            /// This used to require LockAfter chunks agreeing BACK TO BACK, which is a
            /// bar game audio never clears: one burst of gunfire or a music sting gets
            /// detected as something else, the streak resets to one, and the session
            /// stays on "auto-detect" forever — paying per-chunk detection the whole
            /// time and free to transcribe a stretch in the wrong language. Votes with
            /// decay keep the same evidence bar while tolerating stray chunks, so a
            /// steady majority still wins on a noisy mix.
            /// </summary>
            public string WantedLanguage(string lang, double avgProb)
            {
                if (_locked == null)
                {
                    // A chunk the engine itself isn't sure about doesn't get to vote —
                    // it neither backs a language nor erodes the leader. Those are the
                    // chunks that were flipping the old streak.
                    if (string.IsNullOrEmpty(lang) || avgProb < LowProb)
                    {
                        return null;
                    }

                    int cur;
                    _votes.TryGetValue(lang, out cur);
                    _votes[lang] = Math.Min(cur + 1, MaxVotes);
                    foreach (string other in new List<string>(_votes.Keys))
                    {
                        if (!string.Equals(other, lang, StringComparison.OrdinalIgnoreCase) &&
                            _votes[other] > 0)
                        {
                            _votes[other]--;
                        }
                    }

                    Leading = null;
                    LeadingScore = 0;
                    foreach (var kv in _votes)
                    {
                        if (kv.Value > LeadingScore) { Leading = kv.Key; LeadingScore = kv.Value; }
                    }
                    return LeadingScore >= LockAfter ? Leading : null;
                }

                _lowProbStreak = avgProb < LowProb ? _lowProbStreak + 1 : 0;
                return _lowProbStreak >= UnlockAfter ? "auto" : null;
            }

            /// <summary>The caller successfully rebuilt the processor for <paramref name="lang"/>.</summary>
            public void Applied(string lang)
            {
                if (lang == "auto")
                {
                    _locked = null;
                    _votes.Clear();
                    Leading = null;
                    LeadingScore = 0;
                    _lowProbStreak = 0;
                }
                else
                {
                    _locked = lang;
                    _lowProbStreak = 0;
                }
            }
        }

        /// <summary>
        /// Drains the audio buffer in fixed windows and runs Whisper on each,
        /// keeping a short overlap so words spanning a boundary aren't lost.
        /// </summary>
        /// <summary>
        /// True when the first <paramref name="count"/> samples of the buffer are below
        /// the silence floor — used to tell "we threw away speech" from "we threw away
        /// nothing" when the backlog is trimmed.
        ///
        /// Sampled rather than summed in full: the discarded span can be many seconds
        /// (19 s was measured), this runs on the decode thread, and a stride of a few
        /// hundred samples answers "was anything audible in here?" just as well as
        /// reading every one.
        /// </summary>
        private static bool RangeIsMostlySilent(System.Collections.Generic.List<float> buffer,
                                                int count, double floor)
        {
            if (buffer == null || count <= 0) { return true; }
            if (count > buffer.Count) { count = buffer.Count; }

            const int Samples = 4000;                       // plenty for an RMS estimate
            int stride = Math.Max(1, count / Samples);
            double sumSq = 0;
            int n = 0;
            for (int i = 0; i < count; i += stride)
            {
                double v = buffer[i];
                sumSq += v * v;
                n++;
            }
            if (n == 0) { return true; }
            return Math.Sqrt(sumSq / n) < floor;
        }

        private async Task TranscribeLoop(WhisperFactory factory, ProcessorHolder holder,
            string modelPath, bool multilingual, CancellationToken token)
        {
            var langLock = new LanguageLock();
            // The language the CURRENT processor was built with — needed when the
            // too-slow ladder rebuilds the processor (beam off) mid-session.
            string activeLang = multilingual ? StartLanguage() : "en";
            // A pinned language takes the lock out of the loop entirely: there is
            // nothing to detect, nothing to settle on, and nothing that can wander off
            // onto a burst of gunfire. It also shows as pinned rather than sitting on
            // "auto-detect" forever, which is what the readout did on game audio that
            // never produced four agreeing chunks in a row.
            if (!multilingual) { _langState = "English model"; }
            else if (LanguagePinned) { _langState = "pinned: " + activeLang; }
            int windowSamples = (int)(WhisperSampleRate * WindowSeconds);
            int overlapSamples = (int)(WhisperSampleRate * OverlapSeconds);
            int stepSamples = windowSamples - overlapSamples;
            if (stepSamples < WhisperSampleRate / 2) stepSamples = WhisperSampleRate / 2;

            // Headroom buys ACCURACY, not only speed: carry MORE trailing context
            // into each chunk so words at chunk boundaries are decoded with more of
            // their sentence around them. GPU always affords the big carry (1.15 s);
            // a CPU engine that has EARNED a fast cadence (measured, chunk after
            // chunk) affords a 1.0 s carry — the boundary seam is where the most
            // visible errors live, so spend proven headroom exactly there. Engines
            // without headroom keep the baseline overlap; the audio-context budget
            // covers every combination (checked against the stretched worst case),
            // and the overlap dedupe strips the extra repeated words.
            bool gpuCarry = string.Equals(_runtimeDesc, "Vulkan", StringComparison.OrdinalIgnoreCase);
            int carryBig = (int)(WhisperSampleRate * 1.15);
            int carryFast = (int)(WhisperSampleRate * 1.0);
            int carryKeep = gpuCarry ? carryBig : overlapSamples;

            // Hard cap on how far behind live audio the captions may drift.
            //
            // This used to be three full windows — about 8.4 SECONDS. An engine that
            // can't quite keep up (a mid CPU on a big model) fills that buffer and then
            // just SITS at the cap: it's still transcribing every word, but every word
            // lands on screen many seconds after it was said, forever. That is the "it
            // works but it lags" state, and it was invisible.
            //
            // Live captions are worth more CURRENT than complete. Cap the backlog at a
            // few seconds and throw away the oldest audio past it, so what's on screen
            // tracks what's being said now. Dropped audio is counted and shown in Live
            // Debug ("DROPPED n s"), and the too-slow ladder still shrinks the model —
            // so a machine that genuinely can't cope gets fixed, not just hidden.
            int maxBuffer = (int)(WhisperSampleRate * MaxBacklogSeconds);
            int floorBuffer = stepSamples + stepSamples / 4;   // always room for one step
            if (maxBuffer < floorBuffer) { maxBuffer = floorBuffer; }

            // Carry the tail of the previous window forward as leading context so a
            // word split across a boundary is still seen whole by the next pass.
            float[] carry = Array.Empty<float>();

            // Auto-gain level carried across chunks (see below): smoothed so the
            // loudness Whisper hears doesn't pump 8× between chunks.
            double smoothGain = 1.0;

            // Fast start: waiting for a FULL 2.2 s step before the very first pass
            // meant ~3 s from "someone starts talking" to the first words on screen.
            // While nothing has been emitted recently (fresh session, or silence, or
            // a new speaker after a lull) a much shorter step is accepted so the
            // opening words land fast; once text is flowing, the full step returns —
            // long windows hear better mid-stream. Whisper pads every input to 30 s
            // internally, so a short chunk costs the same inference as a full one.
            int fastStartSamples = (int)(WhisperSampleRate * 1.2);
            long lastEmitMs = 0;
            // Cadence earned by measured speed: engines with real headroom take
            // shorter steps so captions update sooner. Two tiers — "fast" (mostly
            // the light models on CPU) and "very fast" (typically the GPU engine,
            // ~10× real time here) — while slow setups keep the full, stable step.
            bool engineFast = false;
            bool engineVeryFast = false;

            // The MARGINAL tier, the opposite of the earned-fast ones: an engine
            // running at ~0.95–1.4× real time (measured live: medium on a 10-core
            // CPU sits at ~1.05×) transcribes every word yet drifts behind and
            // sheds 100–350 ms of audio every few seconds, forever. The fix is
            // counter-intuitive: take BIGGER steps. The encoder's cost per chunk
            // is FLAT (fixed audio context), so a 1.5× window spreads it over 1.5×
            // more audio — ~30% less work per second of speech, which turns
            // "slowly losing" into "keeping up". Captions update a beat slower
            // while stretched; the tier drops out when real headroom returns.
            // (The existing audio context already covers the stretched chunk, so
            // no processor rebuild is needed — it's purely a pacing change.)
            bool engineSlow = false;
            int slowStreak = 0, fastStreak = 0;

            // Outcome-based escalation for the band the per-chunk watchdog can't
            // see. The too-slow ladder fires at >1.4× real time, but an engine at
            // 1.05–1.35× never trips it — it just sheds a few hundred ms of REAL
            // AUDIO every few seconds, forever (words silently missing from the
            // captions). Judge the OUTCOME instead: three separate drop stretches
            // (the 5 s-gated drop log below is the metronome) with no 30 s clean
            // gap means this configuration is losing words persistently — so
            // escalate exactly like the watchdog would: beam off first, then the
            // smaller-model ladder.
            int dropStrikes = 0;
            long lastDropTick = 0;

            // Tracks which state the processor was last built for, so entering or
            // leaving a game rebuilds it (beam off/on) exactly once per transition.
            bool lowImpactApplied = false;
            // Beam dropped by the too-slow ladder (not by game mode). It used to stay
            // off for the REST of the session even after whatever hogged the machine
            // stopped; now sustained measured headroom brings it back — the first
            // thing sacrificed is the first thing restored.
            bool beamDroppedByLadder = false;
            int beamRestoreChunks = 0;

            while (!token.IsCancellationRequested)
            {
                bool lowImpact = LowImpactMode;
                if (lowImpact != lowImpactApplied)
                {
                    lowImpactApplied = lowImpact;
                    // Rebuild when the beam state OR the thread count needs to
                    // change. (Threads: game mode halves the cores the decoder
                    // takes — this is what makes the CPU engine courteous to
                    // games, the same way beam-off is.)
                    bool wantBeam = !lowImpact;
                    if (_beamActive != wantBeam ||
                        _threadsActive != TunedThreads(lowImpact, GpuEngineLoaded()))
                    {
                        try
                        {
                            var fresh = BuildProcessor(factory, modelPath, activeLang, allowBeam: wantBeam);
                            var old = holder.P;
                            holder.P = fresh;
                            try { old?.Dispose(); } catch { }
                        }
                        catch (Exception rex)
                        {
                            Logger.Warn("[Captions] low-impact rebuild failed: " + rex.Message);
                        }
                    }
                    Logger.Info(lowImpact
                        ? "[Captions] fullscreen game detected - low-impact captions (relaxed pace, beam off)."
                        : "[Captions] fullscreen closed - full caption quality restored.");
                }

                bool quickStart = Environment.TickCount64 - lastEmitMs > 3000;
                // A VERY fast engine also earns a shorter first-words threshold: the
                // onset-trim + minimum-speech guards still protect word integrity,
                // the take just happens ~0.3 s sooner after someone starts talking.
                int fastNow = engineVeryFast && !lowImpact ? (int)(WhisperSampleRate * 0.9) : fastStartSamples;
                int need = quickStart ? fastNow
                         : engineSlow ? (stepSamples * 3) / 2
                         : lowImpact ? stepSamples
                         : engineVeryFast ? (stepSamples * 3) / 5
                         : engineFast ? (stepSamples * 3) / 4
                         : stepSamples;
                // Stretched steps need the buffer cap to sit ABOVE the take size,
                // or the backlog trim would starve the take forever.
                int takeCap = engineSlow && !quickStart ? (stepSamples * 3) / 2 : stepSamples;
                int maxBufNow = engineSlow ? maxBuffer + WhisperSampleRate : maxBuffer;

                float[] step = null;
                int droppedThisPass = 0;
                // Wall-clock span of the taken step, for the own-voice guard's
                // envelope correlation (which mic frames line up with this audio).
                long stepStartTick = 0, stepEndTick = 0;
                lock (_bufferLock)
                {
                    // END-OF-UTTERANCE early take — the biggest single latency lever
                    // for EVERY model. The keep-alive streams silence continuously,
                    // and silence counts toward the step quota: after a sentence
                    // ended, its audio used to sit in the buffer while up to a
                    // second of pure silence padded the step out. When the buffer
                    // holds real speech and its TAIL has gone quiet (the utterance
                    // is finished — nothing more is coming for this thought), decode
                    // NOW instead of waiting for the quota. Continuous speech never
                    // triggers this (the tail isn't quiet), so mid-stream accuracy
                    // pacing is untouched; stretched (marginal) engines skip it so
                    // their bigger-chunks-keep-pace strategy isn't undermined.
                    bool earlyTake = false;
                    int have = _mono16k.Count;
                    if (!engineSlow && !quickStart && have >= (int)(WhisperSampleRate * 0.8)
                        && have < stepSamples)
                    {
                        int tail = (int)(WhisperSampleRate * 0.25);
                        double sq = 0;
                        for (int i = have - tail; i < have; i++)
                        {
                            float v = _mono16k[i];
                            sq += (double)v * v;
                        }
                        bool tailSilent = Math.Sqrt(sq / tail) < 0.002;

                        if (tailSilent)
                        {
                            // Quiet tail — but only take if the BODY carries sound
                            // (a buffer of pure keep-alive silence must keep waiting).
                            for (int i = 0; i < have - tail; i += 16)
                            {
                                float v = _mono16k[i];
                                if (v > 0.004f || v < -0.004f) { earlyTake = true; break; }
                            }
                        }
                    }
                    if (earlyTake) { need = have; _earlyTakes++; }

                    // CATCH-UP take — the missing recovery path after a transient
                    // hitch (game loading screen, an installer, a browser spike).
                    // The engine used to drain a queued backlog at only
                    // (1 − decode-rate) per chunk: a 2.5 s queue behind a 0.9×-RT
                    // engine took ~25 s of wall clock to clear, captions visibly
                    // trailing the whole time. The encoder's cost per chunk is FLAT,
                    // so one oversized take (the stretched tier's proven 1.5× size —
                    // same audio-context budget) drains the whole queue in a single
                    // pass at ~2/3 the compute of two normal chunks. Quick-start and
                    // stretched passes keep their own sizing; the counter feeds
                    // Live Debug.
                    bool pressureTake = false;
                    if (!quickStart && !earlyTake && takeCap < (stepSamples * 3) / 2
                        && _mono16k.Count >= stepSamples + stepSamples / 4)
                    {
                        takeCap = (stepSamples * 3) / 2;
                        pressureTake = true;
                    }
                    _backlogMs = (int)(_mono16k.Count * 1000L / WhisperSampleRate);

                    // Trim runaway backlog first (keep newest audio) so captions stay
                    // current instead of falling further behind with every chunk.
                    if (_mono16k.Count > maxBufNow)
                    {
                        int drop = _mono16k.Count - maxBufNow;

                        // Was any of what we're discarding actually SOUND?
                        //
                        // This trim measures how deep the buffer got, not whether words
                        // were lost — and the buffer fills with whatever the capture
                        // heard, silence included. So a single long decode, or any pause
                        // in the loop, would back up seconds of DIGITAL SILENCE, trim it,
                        // and report "engine is behind live audio — a smaller model, or
                        // the GPU engine, would keep up". Three of those in a row then
                        // dropped beam search and asked for a smaller model.
                        //
                        // Seen on this machine as drops of 6.8 s, 5.4 s and 19.4 s while
                        // nothing was playing at all: the user is told their model can't
                        // keep up, on the evidence of throwing away silence. Losing
                        // silence costs nothing and is not evidence of anything, so it
                        // is discarded quietly and counts towards nothing.
                        double floor = _activeMode == CaptureMode.SystemAudio ? 0.0004 : 0.0015;
                        bool lostRealAudio = !RangeIsMostlySilent(_mono16k, drop, floor);

                        _mono16k.RemoveRange(0, drop);

                        if (lostRealAudio)
                        {
                            // Surface the loss: "DROPPED n s" in Live Debug is the honest
                            // sign the engine can't keep pace on this machine.
                            _droppedMs += (int)(drop * 1000L / WhisperSampleRate);
                            droppedThisPass = drop;
                        }
                    }

                    if (_mono16k.Count >= need)
                    {
                        // Take everything available up to a full step, so a quick-start
                        // pass still uses all the audio that has already arrived.
                        // (CopyTo, not GetRange().ToArray() — one copy instead of two.)
                        int take = Math.Min(_mono16k.Count, takeCap);
                        step = new float[take];
                        _mono16k.CopyTo(0, step, 0, take);
                        _mono16k.RemoveRange(0, take);
                        if (pressureTake && take > stepSamples) { _catchUpTakes++; }
                        // What's left in the buffer is newer than the step, so the
                        // step ENDS that much before now (± the ~40 ms capture
                        // buffer, which the guard's lag scan absorbs).
                        stepEndTick = Environment.TickCount64
                            - _mono16k.Count * 1000L / WhisperSampleRate;
                        stepStartTick = stepEndTick - take * 1000L / WhisperSampleRate;
                    }
                }

                // Falling behind is a real, user-visible condition ("captions lag") that
                // used to happen in total silence. Say it once per stretch, not once per
                // chunk, so the log shows the problem without becoming the problem.
                if (droppedThisPass > 0 && Environment.TickCount64 - _lastDropLogMs > 5000)
                {
                    _lastDropLogMs = Environment.TickCount64;
                    // Honest advice: during a fullscreen game the GAME owns the
                    // GPU/CPU and no engine choice fixes that — don't tell the
                    // user to change settings that aren't the cause.
                    string advice = LowImpactMode
                        ? "The game has the GPU/CPU right now — normal during play; captions catch up between fights."
                        : "A smaller model, or the GPU engine, would keep up.";
                    Logger.Warn("[Captions] engine is behind live audio — dropped " +
                                (droppedThisPass * 1000L / WhisperSampleRate) + " ms to catch up (total " +
                                (_droppedMs / 1000.0).ToString("0.0") + " s). " + advice);

                    // Persistent word loss OUTSIDE a game is the marginal band the
                    // per-chunk watchdog can't see — escalate on the outcome.
                    if (!LowImpactMode && ++dropStrikes >= 3)
                    {
                        dropStrikes = 0;
                        if (_beamActive)
                        {
                            try
                            {
                                var plain = BuildProcessor(factory, modelPath, activeLang, allowBeam: false);
                                var old = holder.P;
                                holder.P = plain;
                                try { old?.Dispose(); } catch { }
                                beamDroppedByLadder = true;
                                beamRestoreChunks = 0;
                                Logger.Info("[Captions] words were being dropped — decoding simplified (beam off) to keep pace.");
                            }
                            catch (Exception rex)
                            {
                                Logger.Warn("[Captions] beam drop failed: " + rex.Message);
                                _beamActive = false;          // don't retry in a loop
                            }
                        }
                        else if (!_slowFired)
                        {
                            _slowFired = true;
                            Logger.Warn("[Captions] still dropping audio with decoding already simplified — " +
                                        "asking for the next smaller model.");

                            NoteGpuCouldHelp();
                            try { RealTimeTooSlow?.Invoke(); } catch { }
                        }
                    }
                }
                if (droppedThisPass > 0)
                {
                    lastDropTick = Environment.TickCount64;
                }

                // Re-check the speaker's volume and mute while the session runs.
                //
                // These were read once, when capture opened, so turning the volume down
                // or muting mid-session made captions quietly stop working with the bar
                // still saying "Listening…" — loopback is captured after the slider, so
                // a muted speaker delivers digital silence no matter how loud the video
                // is. Cheap enough at once every 15 s to be worth it, and only ever says
                // something when the answer actually changes.
                if (_activeMode == CaptureMode.SystemAudio &&
                    Environment.TickCount64 - _lastVolumeCheckTick > 15000)
                {
                    _lastVolumeCheckTick = Environment.TickCount64;
                    bool wasMuted = _systemMuted;
                    float wasVol = _systemVolume;
                    RefreshEndpointVolume();
                    if (_systemMuted && !wasMuted)
                    {
                        RaiseStatus("Your speaker was just MUTED — there is no sound left for Tempo to caption. " +
                                    "Unmute it and captions carry on.");
                    }
                    else if (!_systemMuted && wasMuted)
                    {
                        RaiseStatus("Speaker unmuted — listening again.");
                    }
                    else if (!_systemMuted && _systemVolume >= 0f && _systemVolume < 0.08f && wasVol >= 0.08f)
                    {
                        RaiseStatus("Speaker volume dropped to " + Math.Round(_systemVolume * 100) +
                                    "% — too faint to caption reliably. Turn it up, or switch to a microphone.");
                    }
                }
                else if (dropStrikes > 0 && lastDropTick != 0
                         && Environment.TickCount64 - lastDropTick > 30000)
                {
                    dropStrikes = 0;   // 30 s clean — the squeeze has passed
                }

                if (step == null)
                {
                    // Sleep until the capture callback signals fresh audio (100 ms
                    // timeout as a safety net) rather than polling every 50 ms — the
                    // pass now begins the moment enough sound has arrived instead of
                    // up to a whole poll tick later.
                    try { await _samplesReady.WaitAsync(100, token); } catch { }
                    continue;
                }

                // With the loopback keep-alive, silence flows continuously, so a step
                // taken just as someone starts talking is mostly leading silence with
                // only the first syllable at its end - Whisper then mangles the
                // opening word. Trim the dead lead-in; and if what's left is a tiny
                // fragment of speech that hasn't paused yet, give it back and wait a
                // moment so the first word is transcribed WHOLE instead of clipped.
                if (quickStart)
                {
                    step = TrimLeadingSilence(step);
                    if (step == null || step.Length == 0)
                    {
                        // Pure silence — and the CARRY must not outlive it: left in
                        // place it held the last second of PRE-silence speech, and the
                        // first chunk after a long quiet spell re-heard (and re-showed)
                        // those old words — the seam dedup rightly refuses to strip
                        // repeats that far apart.
                        carry = Array.Empty<float>();
                        continue;
                    }
                    // The trim keeps the TAIL, so the step's start moved forward.
                    stepStartTick = stepEndTick - step.Length * 1000L / WhisperSampleRate;
                    if (step.Length < (int)(WhisperSampleRate * 0.6) && !TailIsSilent(step))
                    {
                        lock (_bufferLock) { _mono16k.InsertRange(0, step); }
                        // Same event-driven wait: resume as soon as more audio lands.
                        try { await _samplesReady.WaitAsync(80, token); } catch { }
                        continue;                       // speech just began - let it build
                    }
                }

                // Cut the step at a natural PAUSE instead of mid-word when one exists
                // near its end: scan the last ~0.8 s for the quietest 20 ms frame and,
                // if it's genuinely quiet, hand the audio after it back to the buffer.
                // Whisper then sees whole words at chunk boundaries, which removes the
                // "chopped word becomes a wrong word" class of errors. Quick-start
                // chunks are already short — shortening them further just delays the
                // opening words, so they skip the cut.
                if (step.Length >= stepSamples)
                {
                    step = CutAtSilence(step);
                }

                if (holder.P == null) break;

                // Silence gate on the STEP — the part that is actually NEW. The old
                // gate looked at the assembled chunk, whose loud CARRY tail made a
                // silent step read as sound: a full inference then re-decoded audio
                // that was already transcribed — one wasted decode (CPU or GPU) after
                // EVERY pause in speech, producing text the dedup had to throw away.
                // Real silence also makes the carry worthless as context, and holding
                // it across the quiet spell is what re-surfaced old words later.
                // System audio gets a lower floor than a microphone, and it has to be
                // lower than the auto-gain's own floor further down — otherwise the two
                // disagree and the gain work is unreachable. Loopback is captured after
                // the volume slider, so ordinary dialogue at a modest volume lands at
                // roughly 0.0004-0.0013 RMS: under the 0.0015 default, where it was
                // thrown away as "silence" BEFORE the gain stage could lift it. That is
                // the real reason quiet system audio produced no captions at all rather
                // than poor ones. Digital silence (RMS 0, what the keep-alive streams
                // when nothing plays) stays comfortably below either floor, and a
                // microphone keeps the stricter number so room hiss is still rejected.
                double gateFloor = _activeMode == CaptureMode.SystemAudio ? 0.0004 : 0.0015;
                if (IsMostlySilent(step, gateFloor, out double gatePeak))
                {
                    _chunksSilent++;
                    // Was it actually silent, or only just under the bar? A pile of
                    // near-misses means the floor is too high for THIS source and real
                    // speech is being dropped — the difference between "nothing was
                    // said" and "Tempo could not hear it", which the old counter could
                    // not tell apart.
                    if (gatePeak > gateFloor * 0.5) { _chunksNearMiss++; }
                    carry = Array.Empty<float>();
                    continue;
                }

                // Build the chunk to transcribe: previous tail (context) + new step.
                float[] chunk;
                if (carry.Length > 0)
                {
                    chunk = new float[carry.Length + step.Length];
                    Array.Copy(carry, 0, chunk, 0, carry.Length);
                    Array.Copy(step, 0, chunk, carry.Length, step.Length);
                }
                else
                {
                    chunk = step;
                }

                // Keep the tail of this chunk as context for the next pass. The keep
                // length follows the engine's MEASURED headroom: a CPU engine on the
                // earned fast tiers affords the bigger carry; one that loses its
                // headroom drops back to the baseline overlap automatically.
                carryKeep = gpuCarry ? carryBig
                          : (engineFast || engineVeryFast) && !engineSlow ? carryFast
                          : overlapSamples;
                _carryMsActive = (int)(carryKeep * 1000L / WhisperSampleRate);
                if (chunk.Length > carryKeep)
                {
                    carry = new float[carryKeep];
                    Array.Copy(chunk, chunk.Length - carryKeep, carry, 0, carryKeep);
                }
                else
                {
                    // COPY, never alias: the auto-gain below multiplies `chunk` in
                    // place, and an aliased carry would bake this pass's gain (up to
                    // 8×) into next pass's context — the seam then gets gained TWICE
                    // (g1·g2 context against g2 speech), a loudness step exactly at
                    // the chunk boundary, and the inflated carry peak makes the peak
                    // guard under-lift the next chunk's genuinely quiet speech. The
                    // sibling branch above already copies; this one must match it.
                    carry = new float[chunk.Length];
                    Array.Copy(chunk, carry, chunk.Length);
                }

                // (The silence gate now runs on the STEP above, before assembly — a
                // chunk whose step passed it cannot be silent, so no re-check here.)

                // Own voice? When the guard is attached, compare this step's loudness
                // envelope with the microphone's over the same wall-clock span. A
                // clear match while the mic is hot means the audio is the user's own
                // voice coming back through the speakers (sidetone / "Listen to this
                // device" / chat monitoring) — skip it instead of captioning the user
                // as a phantom speaker. The other side of a call never matches: they
                // are loud while the mic is quiet.
                // NEVER while the engine itself is capturing the MICROPHONE: the user's
                // voice is then the CONTENT, and correlating the mic against itself
                // (similarity ≈ 1) would skip every spoken chunk — captions dead. This
                // covers the sneaky path too: Auto mode silently falls back to the mic
                // when no speaker exists, which the UI's mode check can't see.
                var ownGuard = _activeMode == CaptureMode.Microphone ? null : OwnVoiceGuard;
                if (ownGuard != null && ownGuard.Running && stepStartTick != 0)
                {
                    double[] env = EnvelopeOf(step);
                    double sim = ownGuard.Similarity(stepStartTick, env, out bool micHot);
                    _lastOwnVoiceSimX100 = (int)Math.Round(sim * 100);
                    if (micHot && sim >= OwnVoiceSimilarity)
                    {
                        _ownVoiceSkipped++;
                        if (VerboseTrace)
                        {
                            Logger.Info("[Trace] own voice skipped (similarity "
                                + sim.ToString("0.00") + ", mic hot).");
                        }
                        // Skipped audio must not become next chunk's context: it was
                        // never emitted, so the seam dedup can't strip it — the user's
                        // own last words would open the OTHER party's first caption.
                        // Mirrors the silence gate's carry reset above.
                        carry = Array.Empty<float>();
                        continue;
                    }
                }

                // Automatic gain: Whisper hears QUIET audio badly (videos at low
                // volume, distant voices). Lift soft chunks toward a healthy level —
                // capped at 8× so noise isn't amplified into phantom speech, and never
                // touching already-loud audio. The clamp below keeps samples in range.
                double chunkRms;
                {
                    double sq = 0;
                    float peak = 0f;
                    for (int i = 0; i < chunk.Length; i++)
                    {
                        float v = chunk[i];
                        sq += v * v;
                        float a = v < 0 ? -v : v;
                        if (a > peak) { peak = a; }
                    }
                    chunkRms = Math.Sqrt(sq / Math.Max(1, chunk.Length));

                    // Smooth the gain ACROSS chunks instead of recomputing it fresh
                    // each time: a level that jumps 8× between consecutive chunks
                    // (quiet dialogue, loud effect, quiet dialogue) transcribes worse
                    // than a steady one. Loud/silent chunks pull the gain back toward
                    // 1, and a peak guard stops residual gain from clipping a loud
                    // chunk that follows quiet ones.
                    // System audio needs a wider window than a microphone.
                    //
                    // Loopback is captured AFTER the volume slider, so a video at 15%
                    // volume arrives about a seventh of the size of the same video at
                    // full volume — frequently under the 0.0015 floor, where the old
                    // rule applied NO lift at all and handed Whisper something close to
                    // silence. That is the "captions do nothing while a video is clearly
                    // playing" case. A microphone keeps the tighter numbers: it has a
                    // real analogue noise floor, and lifting that hard would transcribe
                    // hiss into phantom speech.
                    //
                    // Loopback has no such floor — when nothing plays it is digital
                    // silence at RMS 0, which sits below the floor and is still never
                    // lifted — so the extra headroom costs nothing and buys back every
                    // quiet source. The peak guard below keeps it clear of clipping.
                    bool loopback = _activeMode == CaptureMode.SystemAudio;
                    double quietFloor = loopback ? 0.00025 : 0.0015;
                    double maxLift = loopback ? 16.0 : 8.0;
                    double target = (chunkRms > quietFloor && chunkRms < 0.05)
                        ? Math.Min(maxLift, 0.1 / chunkRms)
                        : 1.0;
                    smoothGain = smoothGain * 0.6 + target * 0.4;
                    double applied = smoothGain;
                    if (peak > 0f && applied * peak > 0.95)
                    {
                        applied = 0.95 / peak;
                    }
                    if (applied > 1.02)
                    {
                        float gain = (float)applied;
                        for (int i = 0; i < chunk.Length; i++) { chunk[i] *= gain; }
                        _gainX100 = (int)Math.Round(applied * 100);
                    }
                    else
                    {
                        _gainX100 = 100;                // no lift applied this chunk
                    }
                }

                // Sanitise the audio before handing it to the native engine. A stray
                // NaN/Infinity (from an odd device buffer or a resample edge) or an
                // out-of-range sample can crash the native Whisper code rather than
                // throw a catchable exception - which is exactly the kind of hard crash
                // that took the whole app down. Clamp everything to finite [-1, 1].
                for (int i = 0; i < chunk.Length; i++)
                {
                    float v = chunk[i];
                    if (float.IsNaN(v) || float.IsInfinity(v)) { chunk[i] = 0f; }
                    else if (v > 1f) { chunk[i] = 1f; }
                    else if (v < -1f) { chunk[i] = -1f; }
                }

                try
                {
                    var inferClock = System.Diagnostics.Stopwatch.StartNew();
                    var sb = new System.Text.StringBuilder();
                    string segLang = null;
                    double probSum = 0;
                    int probCount = 0;
                    // IMPORTANT: do NOT pass the cancellation token into ProcessAsync.
                    // Cancelling the native inference mid-pass can leave the native
                    // engine in a bad state or crash the process (native crashes bypass
                    // try/catch). Instead we let each ~2s chunk finish cleanly and stop
                    // BETWEEN chunks via the while-loop's token check. Worst case,
                    // stopping captions waits one chunk - far better than a crash.
                    //
                    // And for the same reason we must never ABANDON this enumerable
                    // early. Breaking out of an `await foreach` disposes the enumerator,
                    // which runs Whisper.net's cleanup — it removes its segment handler
                    // from a list that the still-running native inference thread is
                    // concurrently enumerating to raise OnNewSegment. That threw
                    // "Collection was modified; enumeration operation may not execute"
                    // inside Whisper.net on a thread we no longer awaited, so it
                    // surfaced as an unobserved task exception on every engine restart
                    // (a model downgrade, a caption toggle). Instead, once cancelled we
                    // keep DRAINING the enumerable to its natural end — just ignoring
                    // the segments — and leave the while-loop afterwards.
                    bool cancelledMidChunk = false;
                    await foreach (var seg in holder.P.ProcessAsync(chunk))
                    {
                        if (cancelledMidChunk || token.IsCancellationRequested)
                        {
                            cancelledMidChunk = true;
                            continue;                   // drain, never break
                        }
                        if (seg == null || string.IsNullOrWhiteSpace(seg.Text)) continue;

                        // Language + confidence stats for the session language lock
                        // (over ALL segments, so wrong-language garbage counts too).
                        if (!string.IsNullOrEmpty(seg.Language)) { segLang = seg.Language; }
                        float p = seg.Probability;
                        if (!float.IsNaN(p) && p > 0f) { probSum += p; probCount++; }

                        // Confidence gate: a SHORT segment the model itself was very
                        // unsure of is almost always noise heard as words ("phantom
                        // words"). Long low-confidence segments stay — real mumbled
                        // speech is better shown than swallowed. Quick-start chunks
                        // get a gentler bar: their fragments are naturally shakier,
                        // and the OPENING words of speech are the costliest to lose
                        // (observed live: a first chunk's real words were eaten).
                        float dropBelow = quickStart ? 0.12f : 0.20f;
                        if (!float.IsNaN(p) && p > 0f && p < dropBelow)
                        {
                            string t = seg.Text.Trim();
                            int spaces = 0;
                            for (int i = 0; i < t.Length; i++) { if (t[i] == ' ') spaces++; }
                            if (spaces <= 2)                    // 1-3 words
                            {
                                continue;
                            }
                        }
                        sb.Append(seg.Text);
                    }

                    // Cancelled during the chunk: the enumerable has now drained fully
                    // (so Whisper's own cleanup runs with no native work in flight) and
                    // it is safe to leave. Nothing from this chunk is emitted or used to
                    // retune — the session is over.
                    if (cancelledMidChunk)
                    {
                        break;
                    }

                    // Session language lock (multilingual models only): settle on the
                    // detected language once it's consistent; drop back to detection
                    // if confidence collapses (wrong lock, or the content switched).
                    // Keep the figure the language lock computes anyway, so the UI can
                    // show how sure the engine is of what it just wrote.
                    if (probCount > 0)
                    {
                        _lastConfidenceX1000 = (int)Math.Round((probSum / probCount) * 1000.0);
                    }

                    if (multilingual && !LanguagePinned && probCount > 0)
                    {
                        string want = langLock.WantedLanguage(segLang, probSum / probCount);
                        // Show what detection is CURRENTLY leaning towards, not just the
                        // end state. While unsettled this used to read "auto-detect" and
                        // nothing else, so on audio that never settles — most game mixes
                        // — the language readout never said anything at all.
                        if (want == null && langLock.Leading != null)
                        {
                            _langState = "detecting: " + langLock.Leading +
                                         " (" + langLock.LeadingScore + "/" + LanguageLock.LockAfter + ")";
                        }
                        if (want != null)
                        {
                            try
                            {
                                var fresh = BuildProcessor(factory, modelPath, want, _beamActive);
                                var old = holder.P;
                                holder.P = fresh;
                                try { old?.Dispose(); } catch { }
                                langLock.Applied(want);
                                activeLang = want;
                                _langState = want == "auto" ? "auto-detect" : "locked: " + want;
                                Logger.Info(want == "auto"
                                    ? "[Captions] language unlocked - re-detecting (confidence fell)."
                                    : "[Captions] language locked: " + want);
                            }
                            catch (Exception ex)
                            {
                                // Keep the current processor; the lock will ask again.
                                Logger.Warn("[Captions] language switch failed: " + ex.Message);
                            }
                        }
                    }
                    string text = sb.ToString().Trim();
                    // Scrub Whisper's non-speech artifacts ("[Music]", "(applause)",
                    // "[BLANK_AUDIO]") and its classic quiet-audio hallucinations
                    // ("Thanks for watching!") before anything reaches the screen.
                    text = CleanWhisperArtifacts(text, chunkRms);
                    // And its decode-loop stutter: the same 3+ word phrase emitted
                    // twice (or more) back-to-back on noisy audio.
                    text = CollapseRepeats(text);
                    if (text.Length > 0)
                    {
                        // Because consecutive chunks overlap, the start of this result
                        // often repeats the end of the previous one ("sat on" / "on the
                        // mat" -> "...on on..."). Strip the duplicated overlap so the
                        // caption reads cleanly instead of stuttering words.
                        //
                        // ONLY while the chunks can PHYSICALLY overlap, though: the
                        // carry window is ~1 s, so against an emission older than a few
                        // seconds there is no shared audio and a match is a GENUINE
                        // repeat — "Okay." said again a minute later was being stripped
                        // as an artifact and silently vanished from the captions.
                        bool chunksAdjacent = lastEmitMs != 0 &&
                            Environment.TickCount64 - lastEmitMs < 6000;
                        if (chunksAdjacent)
                        {
                            text = StripDuplicateOverlap(_lastEmitted, text);
                        }
                        if (text.Length > 0)
                        {
                            _lastEmitted = text;
                            lastEmitMs = Environment.TickCount64;   // back to full-step cadence
                            Interlocked.Exchange(ref _lastEmitTick, lastEmitMs);
                            _emits++;
                            RaiseText(text);
                        }
                    }

                    // Real-time watchdog: if transcribing a chunk keeps taking longer
                    // than the audio it covers, this model can NEVER catch up on this
                    // PC — captions drift minutes behind (measured ~40 s per chunk
                    // with Large Turbo on a mid CPU). Tell the host once so it can
                    // drop to a smaller model instead of silently lagging forever.
                    inferClock.Stop();
                    double audioMs = chunk.Length * 1000.0 / WhisperSampleRate;
                    _lastInferMs = (int)inferClock.ElapsedMilliseconds;
                    _lastChunkMs = (int)audioMs;
                    _chunksDone++;
                    // Smoothed inference time (EMA ~ last 8 chunks) for Live Debug —
                    // one steady number instead of a jittering per-chunk reading.
                    _avgInferMs = _avgInferMs == 0
                        ? _lastInferMs
                        : (_avgInferMs * 7 + _lastInferMs) / 8;
                    // Smoothed real-time factor for Live Debug — only meaningful
                    // chunks count (a short quick-start chunk's flat encoder cost
                    // makes its ratio look far worse than the engine really is).
                    if (audioMs >= 800)
                    {
                        int rtfNow = (int)(inferClock.ElapsedMilliseconds * 100 / audioMs);
                        _rtfX100 = _rtfX100 == 0 ? rtfNow : (_rtfX100 * 7 + rtfNow) / 8;
                    }
                    // Plenty of headroom? Earn the shorter step (snappier updates).
                    // Judged only on meaningful chunks — a short quick-start chunk's
                    // flat encoder cost would revoke a tier the engine has genuinely
                    // earned, so after EVERY pause the second caption paid a full-step
                    // wait (+0.3–0.5 s) and the seam carry shrank for that pass. Same
                    // gate the marginal tier below uses; short chunks leave the flags
                    // untouched (neither earned nor revoked).
                    if (audioMs >= WindowSeconds * 600)
                    {
                        engineFast = inferClock.ElapsedMilliseconds < audioMs * 0.35;
                        engineVeryFast = inferClock.ElapsedMilliseconds < audioMs * 0.15;
                    }

                    // Marginal detection for the stretched tier — judged only on
                    // full-size chunks (quick-start chunks are short, so their flat
                    // encoder cost makes the ratio look far worse than it is).
                    if (audioMs >= WindowSeconds * 600)   // ≥60% of a full window
                    {
                        if (inferClock.ElapsedMilliseconds > audioMs * 0.95) { slowStreak++; fastStreak = 0; }
                        else if (inferClock.ElapsedMilliseconds < audioMs * 0.75) { fastStreak++; slowStreak = 0; }
                        // Between the two thresholds: HOLD both streaks. A marginal
                        // engine hovers around 1.0× and constantly dips through this
                        // band — resetting here meant "3 consecutive slow chunks"
                        // never happened and the stretch never engaged (seen live).
                        if (!engineSlow && slowStreak >= 3)
                        {
                            engineSlow = true;
                            slowStreak = 0;
                            Logger.Info("[Captions] engine is marginal on this model — stretching to 1.5× " +
                                        "windows (fewer, bigger chunks keep pace; captions update a beat slower).");
                            // A marginal engine needs every point of headroom NOW:
                            // beam-off buys ~20–30% on its own, often the whole gap
                            // between "sheds words forever" and "keeps up". It was
                            // the first thing the >1.4× watchdog sacrificed anyway —
                            // don't make the 1.0–1.4× band wait to lose words first.
                            // Restored automatically by the sustained-headroom check.
                            if (_beamActive && !LowImpactMode)
                            {
                                try
                                {
                                    var plain = BuildProcessor(factory, modelPath, activeLang, allowBeam: false);
                                    var old = holder.P;
                                    holder.P = plain;
                                    try { old?.Dispose(); } catch { }
                                    beamDroppedByLadder = true;
                                    beamRestoreChunks = 0;
                                    Logger.Info("[Captions] decoding simplified (beam off) alongside the stretch.");
                                }
                                catch (Exception rex)
                                {
                                    Logger.Warn("[Captions] beam drop failed: " + rex.Message);
                                }
                            }
                        }
                        else if (engineSlow && fastStreak >= 6)
                        {
                            engineSlow = false;
                            fastStreak = 0;
                            Logger.Info("[Captions] engine has headroom again — normal caption pace restored.");
                        }
                    }

                    _cadenceTier = LowImpactMode ? "low-impact (game)"
                                 : engineSlow ? "stretched ×1.5 (marginal engine)"
                                 : engineVeryFast ? "very fast ×0.6"
                                 : engineFast ? "fast ×0.75"
                                 : "standard";

                    // Beat-by-beat pipeline trace for the Live Debug window (opt-in).
                    if (VerboseTrace)
                    {
                        double rtf = audioMs > 0 ? inferClock.ElapsedMilliseconds / audioMs : 0;
                        string shown = text.Length == 0 ? "(nothing shown)"
                            : "“" + (text.Length > 60 ? text.Substring(0, 60) + "…" : text) + "”";
                        Logger.Info("[Trace] chunk " + (int)audioMs + " ms → " + _lastInferMs +
                                    " ms (" + rtf.ToString("0.00") + "×RT) · backlog " +
                                    (_backlogMs / 1000.0).ToString("0.0") + " s · gain " +
                                    (_gainX100 / 100.0).ToString("0.0") + "× · " + shown);
                    }
                    // Sustained headroom → restore what the ladder gave up. Beam comes
                    // back FIRST (cheap rebuild, it was the first thing dropped): ~25
                    // consecutive comfortably-fast chunks proves the squeeze is over.
                    // If the ladder immediately drops it again, the too-slow guard
                    // handles it — no oscillation, the 25-chunk bar re-arms each time.
                    if (!LowImpactMode && beamDroppedByLadder && !_beamActive
                        && inferClock.ElapsedMilliseconds < audioMs * 0.45)
                    {
                        beamRestoreChunks++;
                        if (beamRestoreChunks >= 25)
                        {
                            beamRestoreChunks = 0;
                            try
                            {
                                var withBeam = BuildProcessor(factory, modelPath, activeLang, allowBeam: true);
                                var old = holder.P;
                                holder.P = withBeam;
                                try { old?.Dispose(); } catch { }
                                if (_beamActive)
                                {
                                    beamDroppedByLadder = false;
                                    Logger.Info("[Captions] headroom returned — beam-search decoding restored.");
                                }
                            }
                            catch (Exception rex)
                            {
                                Logger.Warn("[Captions] beam restore failed: " + rex.Message);
                                beamDroppedByLadder = false;   // don't retry in a loop
                            }
                        }
                    }
                    else
                    {
                        beamRestoreChunks = 0;
                    }

                    // Sustained headroom → tell the host it can try stepping a
                    // too-slow downgrade back up. Only counted OUTSIDE game mode
                    // (a game's absence of load proves nothing about after), and
                    // any single slow chunk resets the streak.
                    if (!LowImpactMode && inferClock.ElapsedMilliseconds < audioMs * 0.30)
                    {
                        _headroomChunks++;
                        if (_headroomChunks >= 60)
                        {
                            _headroomChunks = 0;
                            try { RealTimeHeadroom?.Invoke(); } catch { }
                        }
                    }
                    else
                    {
                        _headroomChunks = 0;
                    }

                    if (LowImpactMode)
                    {
                        // A fullscreen game owns the GPU/CPU: slow chunks are the
                        // GAME's doing, not the model's. Escalating now would
                        // hot-load a different model file mid-match (a guaranteed
                        // freeze-length hitch) and downgrade quality for a
                        // temporary cause. Hold the too-slow ladder until the game
                        // closes — the backlog drop above already keeps captions
                        // live in the meantime.
                        _slowChunks = 0;
                    }
                    else if (inferClock.ElapsedMilliseconds > audioMs * 1.4)
                    {
                        _slowChunks++;
                        if (_slowChunks >= 2 && _beamActive)
                        {
                            // FIRST rung of the too-slow ladder: give up beam search
                            // (a small accuracy trim) before anything drastic like
                            // shrinking the model or abandoning the GPU engine.
                            try
                            {
                                var plain = BuildProcessor(factory, modelPath, activeLang, allowBeam: false);
                                var old = holder.P;
                                holder.P = plain;
                                try { old?.Dispose(); } catch { }
                                _slowChunks = 0;
                                beamDroppedByLadder = true;
                                beamRestoreChunks = 0;
                                Logger.Info("[Captions] decoding simplified (beam off) to hold real-time pace.");
                            }
                            catch (Exception rex)
                            {
                                Logger.Warn("[Captions] beam drop failed: " + rex.Message);
                                _beamActive = false;          // don't retry in a loop
                            }
                        }
                        else if (_slowChunks >= 2 && !_slowFired)
                        {
                            _slowFired = true;
                            Logger.Warn("Model too slow for real time: " +
                                inferClock.ElapsedMilliseconds + " ms for " + (int)audioMs + " ms of audio.");
                            NoteGpuCouldHelp();
                            try { RealTimeTooSlow?.Invoke(); } catch { }
                        }
                    }
                    else if (_slowChunks > 0)
                    {
                        _slowChunks--;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    RaiseStatus("Transcription error: " + ex.Message);
                    try { await Task.Delay(500, token); } catch { }
                }
            }
        }

        /// <summary>
        /// Drops the silent lead-in from a chunk, keeping a short 0.12 s ramp before
        /// the first real sound so the onset isn't clipped. Returns null when the
        /// whole chunk is silence. Threshold matches the keep-alive's "loud" gate.
        /// </summary>
        private static float[] TrimLeadingSilence(float[] step)
        {
            const float gate = 0.004f;
            int onset = -1;
            for (int i = 0; i < step.Length; i++)
            {
                float v = step[i];
                if (v > gate || v < -gate) { onset = i; break; }
            }
            if (onset < 0)
            {
                return null;                                   // nothing but silence
            }
            int leadIn = (int)(WhisperSampleRate * 0.12);
            int start = Math.Max(0, onset - leadIn);
            if (start <= 0)
            {
                return step;                                   // already starts with sound
            }
            var trimmed = new float[step.Length - start];
            Array.Copy(step, start, trimmed, 0, trimmed.Length);
            return trimmed;
        }

        /// <summary>
        /// Loudness envelope of a step at the own-voice guard's 20 ms resolution —
        /// one RMS per frame, matching the mic monitor's ring so the two series can
        /// be cross-correlated directly.
        /// </summary>
        private static double[] EnvelopeOf(float[] step)
        {
            int frame = WhisperSampleRate / 50;                    // 20 ms
            int frames = Math.Max(1, step.Length / frame);
            var env = new double[frames];
            for (int f = 0; f < frames; f++)
            {
                double sq = 0;
                int start = f * frame;
                int end = Math.Min(step.Length, start + frame);
                for (int i = start; i < end; i++) { sq += (double)step[i] * step[i]; }
                env[f] = Math.Sqrt(sq / Math.Max(1, end - start));
            }
            return env;
        }

        /// <summary>True when the last ~0.15 s of the chunk is quiet (utterance ended).</summary>
        private static bool TailIsSilent(float[] step)
        {
            int tail = Math.Min(step.Length, (int)(WhisperSampleRate * 0.15));
            if (tail <= 0) return true;
            double sq = 0;
            for (int i = step.Length - tail; i < step.Length; i++) { sq += step[i] * step[i]; }
            return Math.Sqrt(sq / tail) < 0.002;
        }

        /// <summary>
        /// Finds the quietest 20 ms frame in the tail of <paramref name="step"/> and,
        /// when it is a real pause (much quieter than the step overall) and cutting
        /// there keeps at least half the step, splits the step at that point and puts
        /// the remainder back at the FRONT of the shared buffer for the next pass.
        /// </summary>
        private float[] CutAtSilence(float[] step)
        {
            try
            {
                const int frame = WhisperSampleRate / 50;                  // 20 ms
                int tail = Math.Min(step.Length / 2, (int)(WhisperSampleRate * 0.8));
                if (tail < frame * 4)
                {
                    return step;
                }

                double totalSq = 0;
                for (int i = 0; i < step.Length; i++) { totalSq += step[i] * step[i]; }
                double stepRms = Math.Sqrt(totalSq / step.Length);
                if (stepRms <= 0)
                {
                    return step;
                }

                int bestCut = -1;
                double bestRms = double.MaxValue;
                for (int start = step.Length - tail; start + frame <= step.Length; start += frame)
                {
                    double sq = 0;
                    for (int i = start; i < start + frame; i++) { sq += step[i] * step[i]; }
                    double rms = Math.Sqrt(sq / frame);
                    if (rms < bestRms)
                    {
                        bestRms = rms;
                        bestCut = start + frame / 2;                       // middle of the quiet frame
                    }
                }

                // Only cut at a genuine pause — a merely-quieter patch of speech stays.
                if (bestCut <= step.Length / 2 || bestRms > stepRms * 0.35)
                {
                    return step;
                }

                int remainder = step.Length - bestCut;
                if (remainder > 0)
                {
                    var back = new float[remainder];
                    Array.Copy(step, bestCut, back, 0, remainder);
                    lock (_bufferLock)
                    {
                        _mono16k.InsertRange(0, back);
                    }
                    var cut = new float[bestCut];
                    Array.Copy(step, 0, cut, 0, bestCut);
                    return cut;
                }
                return step;
            }
            catch
            {
                return step;                                               // never let tuning break capture
            }
        }

        // Sound-descriptor words Whisper wraps in [brackets]/(parens) when it hears
        // non-speech. Tags made of these are noise on a caption bar, not words.
        private static readonly string[] SoundTagWords =
        {
            "music", "applause", "laugh", "laughter", "laughs", "noise", "silence",
            "blank", "blank_audio", "inaudible", "cough", "coughs", "clap", "clapping",
            "cheer", "cheering", "static", "beep", "click", "typing", "keyboard",
            "wind", "breath", "breathing", "sigh", "sighs", "hum", "humming",
            "singing", "instrumental", "foreign", "speaking", "speaks", "sound",
            "sounds", "audio", "crowd", "footsteps", "door", "engine", "birds",
            "chirping", "barking", "phone", "ringing", "sirens", "explosion", "gunshot"
        };

        // Phrases Whisper famously invents out of near-silence (YouTube-outro style).
        // Dropped ONLY when the audio really was very quiet, so genuinely spoken
        // versions of these still caption fine at normal volume.
        private static readonly string[] QuietHallucinations =
        {
            "thank you", "thank you.", "thanks for watching", "thanks for watching.",
            "thanks for watching!", "thank you for watching", "thank you for watching.",
            "thank you for watching!", "please subscribe", "subscribe",
            "you", "you.", "bye", "bye.", "thank you very much.", "thank you so much.",
            // More of Whisper's classic outro repertoire (still quiet-audio +
            // exact-whole-emission matches only, so genuinely spoken versions at
            // normal volume caption fine).
            "see you next time", "see you next time.", "see you in the next video",
            "see you in the next video.", "don't forget to subscribe",
            "don't forget to subscribe.", "like and subscribe",
            "thanks for listening", "thanks for listening."
        };

        // Subtitle-credit lines Whisper memorised from its training data and emits
        // on quiet/musical audio in MANY languages ("Subtitles by the Amara.org
        // community", "ご視聴ありがとうございました", …). Nobody ever SAYS these on a
        // soundtrack — they're safe to drop at any volume, matched anywhere in the
        // text. Kept to distinctive signatures so real speech can't collide.
        private static readonly string[] SubtitleCreditSignatures =
        {
            "amara.org", "opensubtitles", "untertitelung des zdf",
            "ご視聴ありがとうございました", "字幕by", "字幕志愿者",
            "subtítulos realizados por", "sous-titres réalisés",
            "sottotitoli creati dalla", "legendas pela comunidade",
            // Further memorised credit lines the community has catalogued — each one
            // a distinctive full signature no one ever SAYS on a soundtrack.
            "untertitel im auftrag des zdf", "sous-titrage société radio-canada",
            "sottotitoli a cura di qtss", "subtítulos por la comunidad",
            "legendas pela equipe", "untertitel von stephanie geiges"
        };

        /// <summary>
        /// Strips Whisper's non-speech tags anywhere in the text ("[Music]",
        /// "(applause)", "♪♪") and suppresses whole-chunk hallucinated outro phrases
        /// when the audio was near-silent. Returns "" when nothing real remains.
        /// </summary>
        private static string CleanWhisperArtifacts(string text, double chunkRms)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            // Remove [tag] / (tag) runs whose content is a short sound descriptor.
            text = System.Text.RegularExpressions.Regex.Replace(text,
                @"[\[\(]([^\]\)]{1,40})[\]\)]",
                m =>
                {
                    string inner = m.Groups[1].Value.Trim().ToLowerInvariant();
                    string[] words = inner.Split(new[] { ' ', '_', '-' },
                        StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length == 0 || words.Length > 4)
                    {
                        return m.Value;                    // long content: keep as-is
                    }
                    foreach (string w in words)
                    {
                        foreach (string tag in SoundTagWords)
                        {
                            if (w == tag) { return ""; }   // descriptor tag: strip
                        }
                    }
                    return m.Value;                        // real parenthesised words
                });

            // Musical-note-only output means "I heard music", not words.
            string bare = text.Replace("♪", "").Replace("♫", "").Trim();
            if (bare.Length == 0) return "";
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s{2,}", " ").Trim();

            // Memorised subtitle-credit lines: never real speech, any volume.
            string lowered = text.ToLowerInvariant();
            foreach (string sig in SubtitleCreditSignatures)
            {
                if (lowered.Contains(sig)) { return ""; }
            }

            // Quiet-audio hallucinations: exact, whole-emission matches only.
            if (chunkRms < 0.004)
            {
                string norm = text.Trim().ToLowerInvariant();
                foreach (string h in QuietHallucinations)
                {
                    if (norm == h) { return ""; }
                }
            }
            return text;
        }

        /// <summary>
        /// Removes a leading run of words in <paramref name="next"/> that simply
        /// repeats the trailing words of <paramref name="prev"/> (an artefact of the
        /// overlapping transcription windows). Compares up to the last/first 8 words.
        /// </summary>
        private static string StripDuplicateOverlap(string prev, string next)
        {
            if (string.IsNullOrWhiteSpace(prev) || string.IsNullOrWhiteSpace(next))
            {
                return next;
            }

            string[] prevWords = prev.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] nextWords = next.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            int maxK = Math.Min(8, Math.Min(prevWords.Length, nextWords.Length));
            int bestK = 0;
            for (int k = maxK; k >= 1; k--)
            {
                bool match = true;
                for (int i = 0; i < k; i++)
                {
                    string a = Normalize(prevWords[prevWords.Length - k + i]);
                    string b = Normalize(nextWords[i]);
                    if (a != b) { match = false; break; }
                }
                if (match) { bestK = k; break; }
            }

            if (bestK > 0)
            {
                return string.Join(" ", nextWords, bestK, nextWords.Length - bestK).Trim();
            }

            // Fuzzy pass: the two passes often SEGMENT the boundary differently
            // ("keep alive" re-heard as "keep a live"), which the word-by-word
            // compare above can never see. Compare LETTERS instead: the tail of
            // prev vs the head of next, joined and stripped of spaces/punctuation.
            int maxJ = Math.Min(8, nextWords.Length);
            int maxT = Math.Min(8, prevWords.Length);
            for (int j = maxJ; j >= 1; j--)
            {
                string head = LettersOf(nextWords, 0, j);
                if (head.Length < 4) { continue; }         // too little signal to trust
                for (int t = maxT; t >= 1; t--)
                {
                    string tail = LettersOf(prevWords, prevWords.Length - t, t);
                    if (tail == head)
                    {
                        return string.Join(" ", nextWords, j, nextWords.Length - j).Trim();
                    }
                }
            }
            return next;
        }

        /// <summary>
        /// Collapses an immediately-repeated phrase down to one copy — a classic
        /// Whisper decode/seam stutter. 3-6 word repeats always collapse ("and the
        /// bird flies and the bird flies"); 2-word repeats collapse ONLY when the
        /// first copy ends a sentence ("…the new build. new build", seen live), so
        /// natural doubled speech like "I know, I know" is never touched. Repeats
        /// collapse fully (a triple becomes one); clean text passes unchanged.
        /// </summary>
        private static string CollapseRepeats(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var words = new List<string>(text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            if (words.Count < 6) return text;

            bool changed = false;

            // SINGLE-word decode loops first: the same word ≥3 times in a row is a
            // decoder stutter, not speech (seen live: "blue-hipped" × 5 from clean
            // TTS audio — hyphenated tokens dodge every other dedupe). Natural
            // doubles ("I know, I know" → 2 copies) are untouched; runs of 3+
            // collapse to one copy. Normalized compare, so punctuation variants of
            // the same word still count as the run.
            {
                int r = 0;
                while (r < words.Count)
                {
                    int runEnd = r + 1;
                    string baseWord = Normalize(words[r]);
                    while (runEnd < words.Count && baseWord.Length > 0 &&
                           Normalize(words[runEnd]) == baseWord)
                    {
                        runEnd++;
                    }
                    int runLen = runEnd - r;
                    if (runLen >= 3)
                    {
                        // Keep the LAST copy — it carries the run's closing
                        // punctuation ("dog. dog. dog," keeps the final form).
                        words.RemoveRange(r, runLen - 1);
                        changed = true;
                    }
                    r++;
                }
            }

            int i = 0;
            while (i < words.Count)
            {
                bool hit = false;
                // Up to 8-gram loops: decoder repetition loops on noisy audio can
                // cycle phrases longer than the old 6-word cap caught.
                for (int g = 8; g >= 2; g--)
                {
                    if (i + 2 * g > words.Count) { continue; }
                    // Two-word repeats are only collapsed when the first copy ends a
                    // SENTENCE ("…the new build. new build" — a seam stutter, seen
                    // live). Natural doubled speech ("I know, I know") uses a comma
                    // or no punctuation and is deliberately left alone.
                    if (g == 2)
                    {
                        string w1 = words[i + 1];
                        char last = w1.Length > 0 ? w1[w1.Length - 1] : ' ';
                        if (last != '.' && last != '!' && last != '?')
                        {
                            continue;
                        }
                    }
                    bool same = true;
                    for (int k = 0; k < g; k++)
                    {
                        if (Normalize(words[i + k]) != Normalize(words[i + g + k]))
                        {
                            same = false;
                            break;
                        }
                    }
                    if (same)
                    {
                        words.RemoveRange(i + g, g);   // drop the second copy; stay
                        hit = true;                    // put to catch triple repeats
                        changed = true;
                        break;
                    }
                }
                if (!hit) { i++; }
            }
            return changed ? string.Join(" ", words) : text;
        }

        /// <summary>Lower-case letters/digits of a word range, all else stripped.</summary>
        private static string LettersOf(string[] words, int start, int count)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = start; i < start + count && i < words.Length; i++)
            {
                foreach (char c in words[i])
                {
                    if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); }
                }
            }
            return sb.ToString();
        }

        private static string Normalize(string w)
        {
            return w.Trim().Trim('.', ',', '!', '?', ';', ':', '"', '\'').ToLowerInvariant();
        }

        /// <summary>
        /// Returns true when a chunk is essentially silence, using a simple RMS
        /// threshold, so we don't waste time transcribing (and mis-transcribing)
        /// quiet audio.
        /// </summary>
        /// <summary>
        /// True when a chunk holds nothing worth transcribing — judged by its LOUDEST
        /// moment, not its average.
        ///
        /// This used to take the mean RMS across the whole chunk, and that is the wrong
        /// question. A chunk is ~2.8 s; a short reply, the last word of a sentence, or a
        /// quiet speaker occupies a fraction of it and the rest is silence. Averaging
        /// pulls the figure down until it falls under the floor, and then the WHOLE
        /// chunk is discarded — speech included. That is the "captions skip sometimes"
        /// report: not random, but specifically short or quiet phrases surrounded by
        /// quiet, which is most of ordinary conversation.
        ///
        /// Measured across 25 synthetic chunks that all contained speech (system-audio
        /// floor 0.0004):
        ///
        ///     mean-RMS gate    discarded 6 of 25
        ///     peak-window gate discarded 0 of 25
        ///
        /// e.g. 0.25 s of speech at 0.0015 RMS averages to 0.00032 — under the floor,
        /// dropped — while its loudest 100 ms reads 0.00130 and is plainly audible.
        ///
        /// The floor now means "the loudest 100 ms must reach this", which is the
        /// question actually being asked. True silence is still rejected: a chunk of
        /// dither at ~0.00002 peaks at 0.000024, far below any floor.
        /// </summary>
        private static bool IsMostlySilent(float[] samples, double floor = 0.0015)
        {
            return IsMostlySilent(samples, floor, out _);
        }

        /// <summary>As above, also reporting the loudest 100 ms level it measured.</summary>
        private static bool IsMostlySilent(float[] samples, double floor, out double peak)
        {
            peak = 0;
            if (samples == null || samples.Length == 0) return true;

            // Measured over 100 ms windows — shorter than a syllable, so a word cannot be
            // averaged away — and judged by the SECOND loudest, not the loudest.
            //
            // The plain maximum was wrong in the other direction: two samples of a click
            // give a 100 ms window an RMS of 0.03, so one notification ping or a pop in
            // the stream passed a chunk of otherwise-silence and bought a wasted decode
            // and a likely hallucinated caption. Speech fills many consecutive windows;
            // an impulse fills exactly one. Requiring two means the energy has to last
            // at least ~200 ms, which every real utterance does and no click does.
            int win = WhisperSampleRate / 10;
            if (samples.Length <= win * 2)
            {
                double sq = 0;
                for (int i = 0; i < samples.Length; i++) { sq += (double)samples[i] * samples[i]; }
                peak = Math.Sqrt(sq / Math.Max(1, samples.Length));
                return peak < floor;
            }

            double best = 0, second = 0;
            for (int start = 0; start + win <= samples.Length; start += win)
            {
                double sum = 0;
                for (int i = start; i < start + win; i++) { sum += (double)samples[i] * samples[i]; }
                double rms = Math.Sqrt(sum / win);
                if (rms > best) { second = best; best = rms; }
                else if (rms > second) { second = rms; }
            }
            peak = second;
            return second < floor;
        }

        // ── Sample conversion helpers (capture thread only) ──────────────────

        /// <summary>
        /// Converts a captured byte buffer to float samples into <see cref="_convScratch"/>
        /// (grow-only — no per-callback allocation). Returns the sample count, or 0
        /// for an unsupported format.
        /// </summary>
        private int ConvertToFloat(byte[] buffer, int bytes, WaveFormat fmt, out int channels)
        {
            channels = fmt.Channels < 1 ? 1 : fmt.Channels;

            // IEEE float (most common for loopback).
            if (fmt.Encoding == WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample == 32)
            {
                int n = bytes / 4;
                if (_convScratch.Length < n) { _convScratch = new float[n]; }
                Buffer.BlockCopy(buffer, 0, _convScratch, 0, n * 4);
                return n;
            }

            // 16-bit PCM.
            if (fmt.Encoding == WaveFormatEncoding.Pcm && fmt.BitsPerSample == 16)
            {
                int n = bytes / 2;
                if (_convScratch.Length < n) { _convScratch = new float[n]; }
                for (int i = 0; i < n; i++)
                {
                    short s = (short)(buffer[i * 2] | (buffer[i * 2 + 1] << 8));
                    _convScratch[i] = s / 32768f;
                }
                return n;
            }

            // 24-bit PCM (packed 3-byte) — some external interfaces and older
            // drivers present this; it used to fall through to "unsupported" and
            // the capture sat silently dead. Sign-extend via a <<8 then >>8 shift.
            if (fmt.Encoding == WaveFormatEncoding.Pcm && fmt.BitsPerSample == 24)
            {
                int n = bytes / 3;
                if (_convScratch.Length < n) { _convScratch = new float[n]; }
                for (int i = 0; i < n; i++)
                {
                    int o = i * 3;
                    int s = (buffer[o] << 8 | buffer[o + 1] << 16 | buffer[o + 2] << 24) >> 8;
                    _convScratch[i] = s / 8388608f;
                }
                return n;
            }

            // 32-bit PCM.
            if (fmt.Encoding == WaveFormatEncoding.Pcm && fmt.BitsPerSample == 32)
            {
                int n = bytes / 4;
                if (_convScratch.Length < n) { _convScratch = new float[n]; }
                for (int i = 0; i < n; i++)
                {
                    int s = buffer[i * 4] | (buffer[i * 4 + 1] << 8) |
                            (buffer[i * 4 + 2] << 16) | (buffer[i * 4 + 3] << 24);
                    _convScratch[i] = s / 2147483648f;
                }
                return n;
            }

            // Unsupported format - skip.
            return 0;
        }

        /// <summary>Clears the cross-buffer resampler state (call whenever capture (re)opens).</summary>
        /// <summary>
        /// One 2nd-order (biquad) section in transposed direct-form II — the numerically
        /// well-behaved arrangement for float state, and it needs only two state values.
        /// </summary>
        private sealed class Biquad
        {
            private float _b0, _b1, _b2, _a1, _a2;
            private float _z1, _z2;

            /// <summary>Low-pass at <paramref name="cutoff"/> Hz with the given Q.</summary>
            public void SetLowPass(double sampleRate, double cutoff, double q)
            {
                if (sampleRate <= 0 || cutoff <= 0) { return; }
                if (cutoff > sampleRate * 0.49) { cutoff = sampleRate * 0.49; }
                double w0 = 2.0 * Math.PI * cutoff / sampleRate;
                double cos = Math.Cos(w0), sin = Math.Sin(w0);
                double alpha = sin / (2.0 * q);
                double a0 = 1.0 + alpha;
                _b0 = (float)(((1.0 - cos) / 2.0) / a0);
                _b1 = (float)((1.0 - cos) / a0);
                _b2 = _b0;
                _a1 = (float)((-2.0 * cos) / a0);
                _a2 = (float)((1.0 - alpha) / a0);
                Reset();
            }

            public void Reset() { _z1 = 0f; _z2 = 0f; }

            public float Process(float x)
            {
                float y = _b0 * x + _z1;
                _z1 = _b1 * x - _a1 * y + _z2;
                _z2 = _b2 * x - _a2 * y;
                return y;
            }
        }

        // Two cascaded Butterworth sections = 4th order, 24 dB/octave. The Q values are
        // the standard Butterworth pair for a 4th-order cascade; using 0.707 twice would
        // sag the passband instead of keeping it flat.
        private readonly Biquad _aa1 = new Biquad();
        private readonly Biquad _aa2 = new Biquad();
        private int _aaFromRate, _aaToRate;

        /// <summary>Configures the anti-alias pair for a rate pair, only when it changes.</summary>
        private void EnsureAntiAlias(int fromRate, int toRate)
        {
            if (fromRate == _aaFromRate && toRate == _aaToRate) { return; }
            double cutoff = toRate * 0.45;
            _aa1.SetLowPass(fromRate, cutoff, 0.54119610);
            _aa2.SetLowPass(fromRate, cutoff, 1.30656296);
            _aaFromRate = fromRate;
            _aaToRate = toRate;
        }

        private void ResetResampler()
        {
            _aaFromRate = 0;          // force a re-design on the next buffer
            _aaToRate = 0;
            _aa1.Reset();
            _aa2.Reset();
            _resamplePos = 0;
            _resampleTail = 0f;
            _resampleHasTail = false;
            // High-pass: recompute the pole for the device's rate and clear state.
            int rate = _sourceFormat != null && _sourceFormat.SampleRate > 0
                ? _sourceFormat.SampleRate : 48000;
            _hpR = (float)Math.Max(0.90, Math.Min(0.9999, 1.0 - 2.0 * Math.PI * 20.0 / rate));
            _hpPrevIn = 0f;
            _hpPrevOut = 0f;
            _hpFresh = true;      // prime on the first sample — no DC warm-up leak
            _clipEma = 0f;
        }

        /// <summary>
        /// Resamples <paramref name="frames"/> mono samples from <paramref name="fromRate"/>
        /// to <paramref name="toRate"/> into <see cref="_outScratch"/>, treating the
        /// buffers as ONE continuous stream: the fractional read position and the
        /// previous buffer's final sample carry across calls, so non-integer ratios
        /// (44.1 kHz → 16 kHz) produce no dropped fractions or seams at buffer
        /// boundaries. Includes the same box low-pass as before when downsampling
        /// (sized to the decimation ratio, now a sliding sum — O(n) at any ratio).
        /// Returns the output sample count.
        /// </summary>
        private int ResampleInto(float[] mono, int frames, int fromRate, int toRate)
        {
            if (frames <= 0 || fromRate <= 0 || toRate <= 0) { return 0; }

            if (fromRate == toRate)
            {
                if (_outScratch.Length < frames) { _outScratch = new float[frames]; }
                Array.Copy(mono, _outScratch, frames);
                return frames;
            }

            // Anti-alias low-pass when downsampling.
            //
            // This was a centred moving average sized to the decimation ratio. A box
            // average is a poor filter for the job in BOTH directions that matter here.
            // Its response is a sinc: the passband droops long before the corner, and
            // the stopband leaks through sidelobes only ~13 dB down. At 48 kHz → 16 kHz
            // the window is 3 samples, which measurably rolls off the 4–8 kHz band —
            // exactly where the consonants live that separate "s" from "f" from "t" —
            // while still folding some of the 8–24 kHz content (cymbals, gunfire,
            // sibilance) back down into the speech band as alias noise.
            //
            // A cascaded pair of 2nd-order Butterworth sections is the same order of
            // cost per sample and behaves properly: flat to the corner, then 24 dB per
            // octave. Phase is no longer linear, which whisper does not care about —
            // it sees a mel spectrogram.
            float[] src = mono;
            if (toRate < fromRate)
            {
                if (_monoLpScratch.Length < frames) { _monoLpScratch = new float[frames]; }
                float[] lp = _monoLpScratch;
                // Corner just under Nyquist of the TARGET rate: 0.45 × 16 kHz = 7.2 kHz,
                // above the top of the speech band and below the 8 kHz fold point.
                EnsureAntiAlias(fromRate, toRate);
                for (int i = 0; i < frames; i++)
                {
                    lp[i] = _aa2.Process(_aa1.Process(src[i]));
                }
                src = lp;
            }

            double step = (double)fromRate / toRate;
            // Worst-case output count for this buffer, +2 for the carried fraction.
            int cap = (int)(frames / step) + 2;
            if (_outScratch.Length < cap) { _outScratch = new float[cap]; }

            double p = _resamplePos;                    // source-sample position; -1 ≤ p
            int outCount = 0;
            while (p <= frames - 1)
            {
                int i0 = (int)Math.Floor(p);
                double frac = p - i0;
                float s0 = i0 < 0 ? (_resampleHasTail ? _resampleTail : src[0]) : src[i0];
                int i1 = i0 + 1;
                float s1 = i1 < 0 ? s0 : (i1 >= frames ? src[frames - 1] : src[i1]);
                _outScratch[outCount++] = (float)(s0 * (1 - frac) + s1 * frac);
                p += step;
            }
            _resamplePos = p - frames;                  // in [-1, step) for the next buffer
            _resampleTail = src[frames - 1];
            _resampleHasTail = true;
            return outCount;
        }

        private float[] _monoLpScratch = Array.Empty<float>();

        private void RaiseText(string text)
        {
            try { TextRecognized?.Invoke(text); } catch { }
        }

        private void RaiseStatus(string msg)
        {
            Logger.Info("[TempoTranscriber] " + msg);
            try { Status?.Invoke(msg); } catch { }
        }

        public void Dispose()
        {
            // Capture the worker before Stop() detaches it. Stop() deliberately doesn't
            // wait (to keep the start/stop toggle responsive).
            //
            // This runs ONLY on shutdown — stopping captions normally goes through
            // Stop(). It used to wait up to 2000ms here so the worker could free its
            // native Whisper objects "cleanly", and with a model actually loaded that
            // is exactly what it cost: the worker sits inside a native decode call it
            // cannot abandon, so the wait ran its full length, on the UI thread, every
            // single time. Measured: Tempo's window stayed on screen 2072ms after the
            // user asked it to close, against 44ms with captions off.
            //
            // The wait bought nothing. The process is a moment from exiting, and the OS
            // reclaims the model's memory, the whisper context and the audio capture
            // device whether or not the worker got there first. So give the worker just
            // long enough to notice the stop signal if it happens to be between chunks,
            // then let go and let the process die.
            Task worker = _worker;
            Stop();
            try { worker?.Wait(150); } catch { }
        }
    }
}
