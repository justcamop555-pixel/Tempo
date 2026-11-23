using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Listens to the PC's audio and works out WHICH speaker is talking by voice,
    /// so caption labels can say "Speaker 1" again when the first person resumes —
    /// instead of only counting turns upward.
    ///
    /// On-device: for each stretch of speech it measures the voice's pitch (YIN), brightness and timbre (MFCCs), and —
    /// since round 34, when the voice-fingerprint model is in the models folder (Utils/VoicePrint.cs, a 25 MB download
    /// the first time speaker labels run) — a speaker-recognition fingerprint of every second or two of it, then matches
    /// against the speakers heard so far. The fingerprint is what tells two similar voices apart and keeps one excited
    /// person one speaker; pitch and timbre name a voice early, before a fingerprint can be taken, and carry on alone
    /// when there is no model.
    ///
    /// Runs alongside EITHER caption source: the text can come from Windows Live
    /// Captions while this listens to the same system audio purely to decide the
    /// speaker number. All failures are swallowed: if audio capture can't start,
    /// <see cref="CurrentSpeaker"/> stays 0 and the caption labeler falls back to
    /// its pause-based turn counting.
    /// </summary>
    public sealed class VoiceProfiler : IDisposable
    {
        // ── Tunables ─────────────────────────────────────────────────────────
        //
        // Measured against a real two-voice conversation (scratchpad/voiceturns: twelve text-to-
        // speech turns, a male and a female voice, fed through this class's own frame processing).
        // The version before these numbers learned ONE voice for the two people when they answered
        // each other quickly (48.9% of speech attributed right) and 59.6% with 1.2 s pauses, two
        // of six turns by the male voice never named at all. See ProcessFrame and Classify.
        private const float VoiceRms = 0.0035f;      // frame louder than this = someone speaking
        // Silence that ends a stretch of speech. Was 0.9 s — longer than most gaps between two
        // people answering each other, so a quick exchange was one stretch, decided once.
        private const double SilenceEndsSegmentSec = 0.5;
        // Voiced frames (~21 ms each) before a KNOWN voice may be named. Before 12 only a clear-cut
        // match is accepted (see Classify's confident mode) — from 5 frames when the nearest other
        // voice is far away (a man and a woman), from 8 when it is merely clearly further.
        private const int FastClassifyFrames = 5;
        private const int MinFramesToClassify = 12;
        // Creating a brand-NEW speaker needs more evidence than matching a known one:
        // a short cough/effect must not mint "Speaker 3".
        // Frames of consistent NON-match before a brand-new speaker profile may be
        // created. Raised 20 → 30 (~1.05 s): music or effects mixed under one
        // narrator's voice could shift the features long enough to mint a phantom
        // "Speaker 2" for the same person — a real speaker change still classifies
        // fast via matching, and turn-taking pauses give plenty of frames anyway.
        private const int MinFramesForNewSpeaker = 30;
        private const int MaxProfiles = 8;
        // Pitch distances are in OCTAVES (log2 of the ratio), so "20% higher" and "20% lower"
        // weigh the same. One voice's sentence-to-sentence medians wandered ±0.15 octave in the
        // measurement; a male and a female voice sat ~0.95 apart.
        private const double StrictOctaves = 0.26;       // a clear match (~±20%)
        private const double ContinuityOctaves = 0.30;   // still the person who was talking
        // Last try before minting a new voice. At 0.38 a male voice's excited "Friday works for me."
        // (119 Hz against his usual 91) minted a second profile for the same man; emphasis moves a
        // voice by 30–40%, and the old rule allowed about as much.
        private const double LooseOctaves = 0.45;
        private const double ConfidentOctaves = 0.14;    // what an early (8-frame) naming needs
        // Brightness (zero-crossing rate on voiced frames) overlapped between the two test voices
        // (0.090–0.114 vs 0.060–0.093), so it is only a weak tiebreak and a wide veto now.
        private const double BrightVeto = 0.10;
        // Speaker change INSIDE a stretch of speech: the median pitch of the latest DriftFrames voiced
        // frames leaving the named voice by this much, twice in a row, splits the stretch there.
        private const int WindowFrames = 16;
        private const int DriftFrames = 8;
        private const int DriftCheckEvery = 4;
        private const double DriftOctaves = 0.32;
        private const int DriftStrikes = 2;
        // A brand-new voice is only minted from a stretch that sounds like ONE voice: its middle half
        // of pitches within this many octaves. A stretch holding the end of one speaker and the start
        // of another sits between them, matches neither, and minted a phantom third voice.
        private const double NewVoiceMaxSpreadOctaves = 0.45;
        // YIN: a frame is voiced when its normalised difference dips under this. Measured per turn:
        // at 0.25 a male voice kept barely half its energetic frames (so naming it took ~1.7 s); at
        // 0.35 it kept ~70% with no more octave errors (0–5 per turn, of 30–80 voiced frames).
        private const double YinThreshold = 0.15;
        private const double YinVoiced = 0.35;
        // ...and while Silero says a voice is there (round 24): music mixed under it raises the aperiodicity of frames
        // that are plainly voiced, and at 0.35 too few were left to name anyone under a game's soundtrack. Measured
        // (scratchpad voicegate: David/Zira, 10 turns, a synthetic music bed at 0 / 0.1 / 0.2 / 0.3 of the voice's level)
        // over 0.35–0.50: 0.50 named the most of the talk and kept two voices at every level; shown labels right 100 /
        // 83 / 74 / 57% against 100 / 80 / 62 / 52% with no gate, which also named 46% of music alone as speech.
        internal static double YinVoicedUnderVad = 0.50;

        private IWaveIn _capture;
        private volatile bool _running;

        // The app heard alone (round 27, see TempoTranscriber.HearOnlyApp): the voices are judged on the same audio as
        // the words — without the other apps' pings and music that minted phantom voices and pulled real ones off.
        private volatile int _onlyAppPid;
        private volatile int _openAppPid;
        private int _appRefusedPid;

        /// <summary>The process the profiler hears alone, or 0 while it hears the speaker's whole mix.</summary>
        public int HearingOnlyApp => _running ? _openAppPid : 0;

        /// <summary>
        /// Hear only <paramref name="pid"/>'s audio (0 = the whole speaker). Reopens the capture when running, keeping every
        /// learned voice; otherwise applies at the next <see cref="Start"/>. Safe from any thread.
        /// </summary>
        public void HearOnlyApp(int pid)
        {
            if (pid < 0 || !AppAudioCapture.Supported) { pid = 0; }
            _onlyAppPid = pid;
            if (!_running || _disposedFlag || _openAppPid == pid || (pid != 0 && pid == _appRefusedPid)) { return; }
            System.Threading.Tasks.Task.Run(() =>
            {
                lock (_reopenGate)
                {
                    if (!_running || _disposedFlag || _openAppPid == _onlyAppPid) { return; }
                    _running = false;   // suppress the auto-restart branch during the swap
                    Cleanup();
                    Start();            // opens what _onlyAppPid asks for; profiles are kept
                }
            });
        }

        private readonly object _reopenGate = new object();
        private volatile int _currentSpeaker;         // 0 = unknown / silence so far
        private volatile bool _forgetRequested;       // see ForgetVoices()

        // Which output the profiler actually opened. It runs its OWN loopback capture,
        // separate from the caption engine's, so the two can end up on different
        // devices — and when they do, speaker labels are judged from different audio
        // than the words being captioned. Nothing could show that before.
        private volatile string _deviceId;
        private volatile string _deviceName;
        private volatile bool _usedFallback;
        private volatile int _captureOpens;
        private volatile int _forcedMerges;

        /// <summary>1-based number of the speaker currently talking; 0 while unknown.</summary>
        public int CurrentSpeaker => _currentSpeaker;

        /// <summary>
        /// The voice named for the stretch of speech happening RIGHT NOW, or 0: between stretches,
        /// and at the start of one until enough of it has been heard. Unlike <see cref="CurrentSpeaker"/>,
        /// which deliberately holds the last name through pauses, this means "that voice, now" — the
        /// evidence to pair with a face that is visibly talking at the same moment.
        /// </summary>
        public int ActiveSegmentSpeaker => _activeSegmentSpeaker;
        private volatile int _activeSegmentSpeaker;

        /// <summary>Speaker changes found inside a stretch of speech this session (no pause between).</summary>
        public int MidSpeechChanges => _midSpeechChanges;
        private volatile int _midSpeechChanges;

        /// <summary>True once capture started successfully (voice matching available).</summary>
        public bool Running => _running;

        /// <summary>Endpoint id of the output being listened to, or null when NAudio chose it.</summary>
        public string DeviceId => _deviceId;

        /// <summary>Friendly name of the output being listened to, or null.</summary>
        public string DeviceName => _deviceName;

        /// <summary>True when a chosen speaker was unavailable and the default was used instead.</summary>
        public bool UsedFallback => _usedFallback;

        /// <summary>
        /// Captures opened this session. More than one means the device was re-opened —
        /// after a device change, or by a follow that genuinely had to switch outputs.
        /// </summary>
        public int CaptureOpens => _captureOpens;

        /// <summary>Voices learned so far this session.</summary>
        public int ProfileCount => _profiles.Count;

        /// <summary>How many distinct voices can be told apart before new ones get merged.</summary>
        public int ProfileCapacity => MaxProfiles;

        /// <summary>
        /// New voices folded into an existing profile because the table was full. Each
        /// one means two different people now share a speaker number — and the shared
        /// profile drifts toward whichever of them spoke last.
        /// </summary>
        public int ForcedMerges => _forcedMerges;

        /// <summary>What the audio currently is: quiet, human speech, or other sound.</summary>
        public enum AudioKind { Quiet = 0, Speech = 1, Sound = 2 }

        // Exponential moving averages over recent frames, updated on the capture
        // thread and read (volatile floats) from the UI thread.
        private volatile float _activeEma;   // fraction of recent frames with energy
        private volatile float _voicedEma;   // fraction of recent ACTIVE frames with a human pitch
        private volatile float _vadVoiceEma; // fraction of recent ACTIVE frames Silero hears a voice in (while it gates)

        /// <summary>
        /// Classifies what's playing right now: speech (confident human pitch), other
        /// sound (music, effects — energy without stable pitch), or quiet.
        ///
        /// Speech also when Silero hears a voice in most of the sound (round 39). A voice over a video's music
        /// muddies the pitch — the frames failed YIN's clean-pitch test under the music bed — so a whole talking
        /// video read as "Sound", and the caption bar said "♪ Music or sounds playing — no speech" over a man
        /// talking. Silero is the judge the caption engine already trusts with that question; the pitch test is
        /// kept, as it was, for a voice Silero is unsure of.
        /// </summary>
        public AudioKind CurrentAudioKind
        {
            get
            {
                if (!_running || _activeEma < 0.2f)
                {
                    return AudioKind.Quiet;
                }
                if (_voicedEma > 0.4f) { return AudioKind.Speech; }
                return VadGate && VadActive && _vadVoiceEma > 0.5f ? AudioKind.Speech : AudioKind.Sound;
            }
        }

        /// <summary>For Live debug and the harnesses: how much of the recent sound Silero heard a voice in (0–1).</summary>
        internal float VadVoiceShare => _vadVoiceEma;

        // ── Silero voice gate (round 24, 2026-11-15) ─────────────────────────
        //
        // A frame counted as a voice when YIN found a steady pitch in it — and a held note of a game's music, a
        // synth pad or a siren has one as steady as any vowel. So music and effects were measured as voices: phantom
        // speakers minted from the soundtrack, real voices dragged toward it, and labels that came out wrong exactly
        // in games and videos ("label speakers sometimes wrong"). Now a pitched frame counts only while Silero — the
        // network the caption engine already gates speech with — hears a voice: it starts at 0.5 and carries on down
        // to 0.35, Silero's own rule, ending after 256 ms under that. Quiet loopback is lifted first, as the caption
        // gate does. Only at a true 16 kHz stream (Silero's rate); elsewhere the gate stays open, as before.

        /// <summary>For the harnesses (voicegate): pitched frames count only while Silero hears a voice.</summary>
        internal static bool VadGate = true;
        private SileroVad _vad;
        private bool _vadTried;
        private readonly float[] _vadChunk = new float[SileroVad.ChunkSamples];
        private readonly float[] _vadLifted = new float[SileroVad.ChunkSamples];
        private int _vadFill;
        private volatile bool _vadSpeech = true;   // open until Silero has said otherwise
        private float _vadLastP = 1f;              // Silero's latest probability (1 while there is no Silero)
        private int _vadQuiet;
        private float _vadPeakEma;
        private int _vadChunks, _vadSpeechChunks;

        /// <summary>For Live debug and the tests: 32 ms pieces Silero judged, and those it called a voice.</summary>
        public int VadChunks => _vadChunks;
        public int VadSpeechChunks => _vadSpeechChunks;
        public bool VadActive => _vad != null && _rate == SileroVad.SampleRate;

        // ── Capture-thread state (only touched inside OnData) ────────────────
        private readonly List<float> _pending = new List<float>();
        private int _rate = 16000;                    // decimated processing rate
        private int _decim = 3;
        private double _silenceRun;
        private readonly List<double> _segPitch = new List<double>();
        private double _segZcrSum;
        private int _segFrames;
        private bool _segClassified;
        private int _segProfileIdx = -1;   // profile chosen for the CURRENT segment
        private float[] _frame;            // this frame's samples as an array (YIN reads it ~100k times)
        private double[] _yin;             // YIN's normalised difference, by lag

        // The latest voiced frames of the current segment (pitch in Hz, brightness), oldest first
        // once full — what a speaker change inside the segment is judged on.
        private readonly double[] _winPitch = new double[WindowFrames];
        private readonly double[] _winZcr = new double[WindowFrames];
        private int _winCount, _winHead;
        private int _sinceDriftCheck, _driftStrikes;
        // Per voiced frame of the segment, alongside _segPitch: its brightness, and where the last
        // short pause (≥ PauseMarkSec) fell — the most likely place for one person to hand over to
        // another when they do it without a real silence.
        private readonly List<double> _segZcr = new List<double>();
        private readonly List<int> _pauses = new List<int>();   // voiced-frame index after each pause
        private const double PauseMarkSec = 0.15;
        private const int MaxPausesKept = 32;

        private sealed class Profile
        {
            public double Pitch;      // running mean F0 (Hz)
            public double Bright;     // running mean zero-crossing rate (0..1)
            public double Spread;     // running mean F0 interquartile spread (Hz)
            public int Weight;        // frames folded in
            public double[] Timbre;   // running mean of the voiced frames' timbre (see FrameTimbre); null before any
            public int TimbreN;       // voiced frames behind it (capped, so a voice can still settle)
            public float[] Print;     // the voice's fingerprint (VoicePrint, round 34): mean of agreeing prints; null before any
            public int PrintN;        // prints folded into it
        }

        // ── Voice fingerprints (round 34, 2026-11-16) ─────────────────────────
        //
        // The user: "it need hear different voice to help pick same speakers" (and yes to the model's download). Pitch and
        // the spectrum's shape (the timbre above) approximate a voice; WeSpeaker's speaker model (Utils/VoicePrint.cs) was
        // trained to tell people apart. Measured on the test voices (scratchpad/voiceprint, 1.6 s stretches): one voice's
        // stretches score cosine 0.51+ (95 %; median 0.67) whatever is said and however excited, two voices 0.35 or less
        // (95 %; median 0.18) — David and Mark, the closest pair for the timbre, 0.24; under a music bed at 0.15 the same
        // 0.44+ against 0.26-; every one of ~2,600 stretches nearest its own voice. So once a stretch has ~1.2 s of voice:
        //   · who it is goes by the print when the print is clear (PrintSame, and clear of the next voice);
        //   · a new voice is minted only when the print matches nobody known (PrintDiffer) — never by pitch or timbre alone,
        //     which is how one excited person, or a burst of music, became "Speaker 3";
        //   · inside a stretch, the newest 1.6 s is checked every 0.8 s of voice: the person changed only when the print
        //     plainly left the named voice twice running — the timbre's and pitch's own splits are not used while it works;
        //   · an early name (timbre/pitch, from ~0.5 s — what makes labels quick) the print plainly contradicts is corrected.
        // Works only at the 16 kHz the model needs (a 48 kHz output decimated by 3; a 44.1 kHz one keeps the old matcher).
        internal static bool UsePrints = true;
        // For the harnesses: pitch alone may still name a voice early while loud sound keeps the timbre untrusted
        // (EarlyPitchUnderSound: as without prints); EarlyPitchMode 0 = not at all, 1 = only a clear-cut voice.
        internal static bool EarlyPitchUnderSound = false;
        internal static int EarlyPitchMode = 1;
        // "Plainly another voice" depends on how much voice the print is of: two people's 1.2 s prints reach 0.31 (95 %),
        // their 2.4 s prints 0.38, while one voice's sit at 0.43 / 0.62 and up (5 %) — so the bar rises from PrintDiffer at
        // 1.2 s to PrintDifferLong at 2.4 s (DifferBar). Between it and PrintSame nothing is decided: more voice decides.
        internal static double PrintSame = 0.45, PrintDiffer = 0.25, PrintDifferLong = 0.38, PrintMargin = 0.10;
        private const int PrintMinSamples = VoicePrint.SampleRate * 12 / 10;      // 1.2 s of voice before a print counts
        // A new voice from 1.2 s too: conversation phrases end at a 0.5 s pause, and at 1.6 s most of a quick exchange's
        // phrases ended before anyone new could be named (voiceflip: 62 % of a David/Zira talk unnamed).
        private const int PrintNewVoiceSamples = VoicePrint.SampleRate * 12 / 10;
        private const int PrintWindowSamples = VoicePrint.SampleRate * 16 / 10;   // the newest 1.6 s for a change inside a stretch
        private const int PrintClassifySamples = VoicePrint.SampleRate * 3;       // up to 3 s for "who is this"
        private const int PrintFoldSamples = VoicePrint.SampleRate * 4;           // up to 4 s folded into a voice at its end
        private const int PrintEverySamples = VoicePrint.SampleRate * 8 / 10;     // a look every 0.8 s of voice
        private const int SegAudioMaxSamples = VoicePrint.SampleRate * 8;         // the stretch's audio kept (newest 8 s)
        private volatile VoicePrint _voicePrint;
        // The segment's voice audio (what Silero heard as a voice, at 16 kHz), oldest first; _segAudioDropped samples were cut
        // from its front (kept within SegAudioMaxSamples). Per voiced frame of the segment, where its audio began.
        private readonly List<float> _segAudio = new List<float>();
        private int _segAudioDropped;
        private readonly List<int> _segPitchAudio = new List<int>();
        private int _segAudioSincePrint;
        private float[] _segPrint;          // the latest print of the current segment (vetoes for Classify)
        private int _printStrikes;
        private volatile int _printsTaken, _printSplits, _printCorrections, _printMints, _printVetoes;
        private volatile float _lastPrintCos = float.NaN;
        private volatile string _lastPrintVerdict;

        /// <summary>
        /// The voice-fingerprint model to use (null = none; the matcher is the pitch and timbre one of before). The host
        /// loads it off the UI thread and disposes it after <see cref="Stop"/>. Safe from any thread.
        /// </summary>
        public void SetVoicePrint(VoicePrint print) => _voicePrint = print;

        /// <summary>True while fingerprints are being taken (a model set, and the stream at its 16 kHz).</summary>
        public bool PrintsActive => UsePrints && _voicePrint != null && _rate == VoicePrint.SampleRate;

        /// <summary>For Live debug: prints taken this session; stretches split, names corrected, voices minted, mints vetoed by them.</summary>
        public int PrintsTaken => _printsTaken;
        public int PrintSplits => _printSplits;
        public int PrintCorrections => _printCorrections;
        public int PrintMints => _printMints;
        public int PrintVetoes => _printVetoes;

        /// <summary>For Live debug: the latest print's verdict ("voice 2, cosine 0.71"), or null before any.</summary>
        public string LastPrintVerdict => _lastPrintVerdict;

        /// <summary>Milliseconds the latest print took (0 before any).</summary>
        public int LastPrintMs { get { var p = _voicePrint; return p != null ? p.LastMs : 0; } }

        // ── Timbre (round 25, 2026-11-15) ─────────────────────────────────────
        //
        // Pitch alone cannot tell an excited voice from another person's: the user's "speakers change by themselves
        // before they finish talking, but it is one person". Measured (scratchpad/voiceflip: David, Zira and Windows'
        // Mark reading a monologue with excited rises, a question, a quiet aside): one expressive David became TWO voices
        // with 6 label changes inside his talk (9 splits), 14–16 under a little music, and David-then-Zira four voices.
        // His excited sentences sit at +40–55% — 133–147 Hz, where Mark (another man) talks all the time.
        //
        // What does not move with pitch is the shape of the spectrum the vocal tract gives a voice: MFCCs c1..c12 over
        // 100–7000 Hz. On the same recordings (scratchpad/voiceflip/timbre) the whole-talk fingerprints sit 0.54 (David–
        // Mark), 0.71 (David–Zira) and 0.80 (Mark–Zira) apart, the same voice in two recordings 0.07–0.10; and over half a
        // second of voicing (24 frames) only 2 of 211 stretches came out nearer another voice than their own — the excited
        // ones included (their own distance 0.32–0.36). A 4 kHz band halved the David–Mark gap (0.23): the upper formants
        // and the voice's "air" carry most of it. Distances are in units of a coefficient's frame-to-frame spread in
        // speech (TimbreScale, measured on those voices), averaged over the twelve.
        private const int TimbreCoeffs = 12;
        private const int TimbreBands = 24;
        private const double TimbreLowHz = 100, TimbreHighHz = 7000;
        private static readonly double[] TimbreScale = { 14.2, 12.0, 12.0, 10.3, 7.0, 6.7, 5.3, 5.0, 5.0, 3.8, 3.4, 3.7 };
        // A voice's fingerprint is trusted from this many voiced frames (~0.65 s of voicing).
        private const int TimbreMinProfileFrames = 30;
        // Voiced frames a look inside a stretch of speech takes (a speaker change mid-speech).
        private const int TimbreWindow = 24;
        // How much pitch still counts beside timbre: octaves past a slack that prosody alone covers, at most PitchCap.
        // (0.25 / 0.15 made David-then-Mark three voices: the two men's timbres are the closest pair, 0.54, and their pitch
        // — 95 against 141 Hz — is what parts them a little further.)
        internal static double PitchWeight = 0.35, PitchSlack = 0.20, PitchCap = 0.20;
        // Kept only for the harnesses: timbre off = the pitch-only matcher of before.
        internal static bool UseTimbre = true;

        private int _fftN, _timbreWin, _timbreRate;
        private double[] _fftRe, _fftIm, _hamming;
        private int[] _melLo, _melMid, _melHi;
        private double[,] _dct;
        private readonly double[] _melLog = new double[TimbreBands];
        // Per voiced frame of the segment, alongside _segPitch: its timbre. The first _segTimbreFolded of them are in the
        // segment's profile already (a voice minted from them); the rest are folded in when the segment ends or splits.
        private readonly List<double[]> _segTimbre = new List<double[]>();
        private int _segTimbreFolded;
        private readonly List<Profile> _profiles = new List<Profile>();

        /// <summary>Starts listening. Safe to call when already running or with no audio device.</summary>
        public void Start()
        {
            if (_running)
            {
                return;
            }
            // A start that follows a real Stop() begins a NEW session. Stop() clears the
            // voice table from the UI thread, but an audio callback already inside
            // ProcessFrame can still add a profile or set the speaker number a moment
            // later — which then leaked into the next session as a stale voice. Asking
            // the capture thread to wipe it guarantees the fresh session starts empty.
            if (_disposedFlag)
            {
                _forgetRequested = true;
                _currentSpeaker = 0;
                _activeSegmentSpeaker = 0;
                _forcedMerges = 0;
                _midSpeechChanges = 0;
                _captureOpens = 0;
                _printsTaken = 0; _printSplits = 0; _printCorrections = 0; _printMints = 0; _printVetoes = 0;
                _lastPrintVerdict = null;
                _lastPrintCos = float.NaN;
            }
            _disposedFlag = false;   // a fresh session re-arms the device-change recovery
            try
            {
                // Loopback = "what the PC is playing". A PC with no output device
                // simply leaves the profiler off; the labeler still counts turns.
                // Listen through the same speaker the caption engine uses — the
                // user's chosen device when one is picked, Windows' default else.
                using (var en = new NAudio.CoreAudioApi.MMDeviceEnumerator())
                {
                    var dev = AudioDeviceSelection.Resolve(en, NAudio.CoreAudioApi.DataFlow.Render, out bool fellBack);
                    _usedFallback = fellBack;
                    _deviceId = null;
                    _deviceName = null;
                    if (dev != null)
                    {
                        try { _deviceId = dev.ID; } catch { }
                        _deviceName = AudioDeviceSelection.NameOf(dev, NAudio.CoreAudioApi.DataFlow.Render);
                    }
                    if (_capture != null) { Cleanup(); }   // a capture that died and was not closed yet
                    _openAppPid = 0;
                    int pid = _onlyAppPid;
                    if (pid > 0 && pid != _appRefusedPid)
                    {
                        AppAudioCapture app = AppAudioCapture.TryOpen(pid, out string why);
                        if (app != null) { _capture = app; _openAppPid = pid; _appRefusedPid = 0; }
                        else
                        {
                            _appRefusedPid = pid;
                            Logger.Info("[Voice] can't hear process " + pid + " on its own (" + why + ") — hearing the whole speaker.");
                        }
                    }
                    if (_capture == null)
                    {
                        _capture = dev != null ? new WasapiLoopbackCapture(dev) : new WasapiLoopbackCapture();
                    }
                    _captureOpens++;
                }
                int deviceRate = _capture.WaveFormat.SampleRate;
                _decim = Math.Max(1, deviceRate / 16000);
                _rate = deviceRate / _decim;
                _capture.DataAvailable += OnData;
                _capture.RecordingStopped += (s, e) =>
                {
                    // Only the capture in use may react. NAudio raises this through the synchronisation context of the
                    // thread that MADE the capture — the UI's, when started from there — so a capture closed by a reopen
                    // reported its stop AFTER the new one had started, set _running false and switched the new one off
                    // (it came back by itself 1.2 s later, through the reopen below — another reopen, for nothing).
                    if (!ReferenceEquals(s, _capture)) { return; }
                    bool wanted = _running;
                    _running = false;
                    // The usual cause is the default audio DEVICE changing (headphones
                    // plugged in, a game switching output). Recover by reopening the
                    // capture on the new default instead of silently going deaf.
                    if (wanted && !_disposedFlag)
                    {
                        System.Threading.Tasks.Task.Run(async () =>
                        {
                            try
                            {
                                await System.Threading.Tasks.Task.Delay(1200);
                                if (!_running && !_disposedFlag)
                                {
                                    Logger.Info("[Voice] capture stopped (device change?) - reopening.");
                                    Cleanup();
                                    Start();
                                    // Re-point the shared keep-alive at the new default
                                    // speaker so it doesn't pump the dead device.
                                    if (_running) { LoopbackKeepAlive.Poke(); }
                                }
                            }
                            catch { }
                        });
                    }
                };
                _capture.StartRecording();
                _running = true;

                // The profiler listens to loopback, which stops delivering buffers the
                // moment the PC goes quiet — short sounds then get missed or arrive
                // late while the stream wakes up. Hold the silence keep-alive so the
                // stream pumps continuously (see LoopbackKeepAlive). Held across the
                // device-change auto-restart; released by Stop().
                if (!_keepAliveHeld)
                {
                    _keepAliveHeld = true;
                    LoopbackKeepAlive.Acquire();
                }

                Logger.Info("[Voice] profiler listening (" + deviceRate + " Hz" +
                            (_openAppPid != 0 ? ", only " + ((AppAudioCapture)_capture).ProcessName + " — other apps left out" : "") + ").");
            }
            catch (Exception ex)
            {
                Logger.Warn("[Voice] profiler unavailable: " + ex.Message);
                Cleanup();
                if (_keepAliveHeld)
                {
                    _keepAliveHeld = false;
                    LoopbackKeepAlive.Release();
                }
            }
        }

        private volatile bool _disposedFlag;   // blocks auto-restart after a real Stop
        private bool _keepAliveHeld;           // this profiler's hold on LoopbackKeepAlive

        public void Stop()
        {
            _disposedFlag = true;
            _running = false;
            if (_keepAliveHeld)
            {
                _keepAliveHeld = false;
                LoopbackKeepAlive.Release();
            }
            _currentSpeaker = 0;
            _activeSegmentSpeaker = 0;
            Cleanup();
            _openAppPid = 0;
            _appRefusedPid = 0;   // a new session may try that app again
            // A fresh session starts with fresh voices — old profiles would otherwise
            // mislabel a completely different video's speakers.
            _profiles.Clear();
            _pending.Clear();
            ClearSegmentAudio();
            _segPitch.Clear();
            _segZcr.Clear();
            _segTimbre.Clear();
            _segTimbreFolded = 0;
            _bgRms = 0; _voiceRms = 0;
            _segZcrSum = 0; _segFrames = 0; _segClassified = false; _silenceRun = 0;
            _winCount = 0; _winHead = 0; _sinceDriftCheck = 0; _driftStrikes = 0; _pauses.Clear();
            ResetVad();
        }

        /// <summary>
        /// Re-opens capture on the speaker it should now be hearing — the chosen one
        /// when picked, Windows' default otherwise — while keeping every learned voice.
        /// For default-device switches where the old device stays alive (no
        /// RecordingStopped fires); without this the profiler keeps listening to the
        /// wrong, silent device.
        ///
        /// Does nothing when that is already the open device. Every default-speaker
        /// change used to tear the capture down and reopen it even with a speaker
        /// PINNED that had not changed at all, cutting the voice being measured in half
        /// for no benefit.
        /// </summary>
        public void FollowDefaultDevice()
        {
            if (!_running || _disposedFlag)
            {
                return;
            }
            // One app heard alone is heard on any output: a new default speaker changes nothing it hears.
            if (_openAppPid != 0 && _capture != null) { return; }
            string target = AudioDeviceSelection.ResolveId(DataFlow.Render);
            string open = _deviceId;
            if (_capture != null && target != null && open != null &&
                string.Equals(target, open, StringComparison.OrdinalIgnoreCase))
            {
                return;            // already listening to the right device
            }
            lock (_reopenGate)
            {
                if (!_running || _disposedFlag) { return; }
                _running = false;      // suppress the auto-restart branch during swap
                Cleanup();
                Start();               // reopens on the resolved device; profiles are kept
            }
        }

        public void Dispose() => Stop();

        /// <summary>
        /// Forgets every learned voice while capture keeps running. Called when the
        /// app making the audio CHANGES (new game / different video site): those are
        /// different people, and matching them against the previous app's voices
        /// would hand out wrong, stale speaker numbers. Thread-safe — the flag is
        /// honoured by the capture thread at the next audio frame.
        /// </summary>
        public void ForgetVoices()
        {
            _forgetRequested = true;
            _currentSpeaker = 0;
            _activeSegmentSpeaker = 0;
        }

        private void Cleanup()
        {
            try
            {
                if (_capture != null)
                {
                    _capture.DataAvailable -= OnData;
                    try { _capture.StopRecording(); } catch { }
                    try { _capture.Dispose(); } catch { }
                }
            }
            catch { }
            _capture = null;
        }

        // ── Audio processing (capture thread) ────────────────────────────────
        private void OnData(object sender, WaveInEventArgs e)
        {
            if (!_running)
            {
                return;
            }
            try
            {
                // Honour a pending ForgetVoices() here, on the capture thread, so the
                // profile list is never mutated from two threads at once.
                if (_forgetRequested)
                {
                    _forgetRequested = false;
                    _profiles.Clear();
                    ClearSegmentAudio();
                    _segPitch.Clear();
                    _segZcr.Clear();
                    _segTimbre.Clear();
                    _segTimbreFolded = 0;
                    _bgRms = 0; _voiceRms = 0;
                    _segZcrSum = 0; _segFrames = 0;
                    _segClassified = false; _segProfileIdx = -1;
                    _winCount = 0; _winHead = 0; _sinceDriftCheck = 0; _driftStrikes = 0; _pauses.Clear();
                    // Also here, on the thread that writes it: a zero set by the caller
                    // can be overwritten by a callback that was already mid-classify.
                    _currentSpeaker = 0;
                    _activeSegmentSpeaker = 0;
                    ResetVad();
                }

                WaveFormat fmt = _capture.WaveFormat;
                int channels = Math.Max(1, fmt.Channels);
                int newFrom = _pending.Count;   // what this callback adds starts here (the voice gate hears it)

                // Mix to mono and decimate to ~16 kHz by BLOCK-AVERAGING each group
                // of _decim frames (boxcar filter + decimate in one pass). Plain
                // take-every-Nth decimation aliased everything above 8 kHz back into
                // the voice band, which polluted the brightness/pitch features the
                // speaker matcher runs on — the same voice could measure differently
                // over different material. Averaging removes the bulk of that energy.
                if (fmt.Encoding == WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample == 32)
                {
                    int frames = e.BytesRecorded / 4 / channels;
                    int outCount = frames / _decim;
                    for (int j = 0; j < outCount; j++)
                    {
                        float sum = 0;
                        int start = j * _decim;
                        for (int k = 0; k < _decim; k++)
                        {
                            int b = (start + k) * channels * 4;
                            for (int c = 0; c < channels; c++)
                            {
                                sum += BitConverter.ToSingle(e.Buffer, b + c * 4);
                            }
                        }
                        _pending.Add(sum / (channels * _decim));
                    }
                }
                else if (fmt.Encoding == WaveFormatEncoding.Pcm && fmt.BitsPerSample == 16)
                {
                    int frames = e.BytesRecorded / 2 / channels;
                    int outCount = frames / _decim;
                    for (int j = 0; j < outCount; j++)
                    {
                        float sum = 0;
                        int start = j * _decim;
                        for (int k = 0; k < _decim; k++)
                        {
                            int b = (start + k) * channels * 2;
                            for (int c = 0; c < channels; c++)
                            {
                                sum += (short)(e.Buffer[b + c * 2] | (e.Buffer[b + c * 2 + 1] << 8)) / 32768f;
                            }
                        }
                        _pending.Add(sum / (channels * _decim));
                    }
                }
                else
                {
                    return; // exotic format — leave the profiler silent
                }

                ConsumePending(newFrom);
            }
            catch
            {
                // Never let an audio glitch kill the capture callback.
            }
        }

        /// <summary>
        /// The samples from <paramref name="newFrom"/> on just arrived: Silero hears them first (the voice gate), then the
        /// buffer is consumed in ~43 ms windows hopped by half.
        /// </summary>
        private void ConsumePending(int newFrom)
        {
            FeedVad(newFrom);
            int win = Math.Max(256, _rate * 43 / 1000);
            int hop = win / 2;
            while (_pending.Count >= win)
            {
                ProcessFrame(_pending, win);
                _pending.RemoveRange(0, hop);
            }
            // Bound memory if frames somehow back up.
            if (_pending.Count > _rate * 5)
            {
                _pending.Clear();
            }
        }

        /// <summary>For the harnesses: <paramref name="count"/> mono samples at the processing rate, as a callback delivers them.</summary>
        internal void PushForTest(float[] mono, int count)
        {
            int from = _pending.Count;
            for (int i = 0; i < count; i++) { _pending.Add(mono[i]); }
            ConsumePending(from);
        }

        /// <summary>Hands the new samples to Silero in 32 ms pieces and keeps its verdict (<see cref="_vadSpeech"/>).</summary>
        private void FeedVad(int from)
        {
            for (int i = Math.Max(0, from); i < _pending.Count; i++)
            {
                _vadChunk[_vadFill++] = _pending[i];
                if (_vadFill == _vadChunk.Length)
                {
                    _vadFill = 0;
                    JudgeVadChunk();
                }
            }
        }

        private void JudgeVadChunk()
        {
            if (!_vadTried)
            {
                _vadTried = true;
                try { _vad = SileroVad.LoadEmbedded(); } catch { _vad = null; }
            }
            if (_vad == null || _rate != SileroVad.SampleRate) { _vadSpeech = true; _vadLastP = 1f; return; }   // no gate: as before
            float peak = 0f;
            for (int i = 0; i < _vadChunk.Length; i++)
            {
                float a = _vadChunk[i] < 0 ? -_vadChunk[i] : _vadChunk[i];
                if (a > peak) { peak = a; }
            }
            // Quick to rise, ~0.3 s to fall: the lift follows the level of the last few hundred ms, not each piece's own.
            _vadPeakEma = Math.Max(peak, _vadPeakEma * 0.9f);
            float gain = _vadPeakEma > 0f && _vadPeakEma < 0.05f ? Math.Min(0.05f / _vadPeakEma, 30f) : 1f;
            float p;
            try
            {
                if (gain > 1f)
                {
                    for (int i = 0; i < _vadChunk.Length; i++) { _vadLifted[i] = _vadChunk[i] * gain; }
                    p = _vad.Process(_vadLifted, 0);
                }
                else
                {
                    p = _vad.Process(_vadChunk, 0);
                }
            }
            catch { _vadSpeech = true; _vadLastP = 1f; return; }
            _vadChunks++;
            _vadLastP = p;
            if (_vadSpeech)
            {
                if (p >= 0.35f) { _vadQuiet = 0; }
                else if (++_vadQuiet >= 8) { _vadSpeech = false; }   // 256 ms under the bar ends it
            }
            else if (p >= 0.5f)
            {
                _vadSpeech = true;
                _vadQuiet = 0;
            }
            if (_vadSpeech) { _vadSpeechChunks++; }
        }

        /// <summary>Silero back to a new stream (a new session, forgotten voices).</summary>
        private void ResetVad()
        {
            try { _vad?.Reset(); } catch { }
            _vadFill = 0;
            _vadQuiet = 0;
            _vadPeakEma = 0f;
            _vadSpeech = true;
            _vadLastP = 1f;
        }

        private void ProcessFrame(List<float> buf, int win)
        {
            // Energy.
            double sq = 0;
            for (int i = 0; i < win; i++)
            {
                double v = buf[i];
                sq += v * v;
            }
            double rms = Math.Sqrt(sq / win);
            double frameSec = (win / 2.0) / _rate;   // hop duration

            // The frame as an array: YIN below reads every sample a few hundred times, and a List
            // indexer on that path is measurably slower than an array.
            if (_frame == null || _frame.Length < win) { _frame = new float[win]; }
            for (int i = 0; i < win; i++) { _frame[i] = buf[i]; }

            if (rms < VoiceRms)
            {
                // Quiet: no sound under the voices right now (TimbreTrusted).
                _bgRms *= BackgroundDecay;
                _activeEma *= 0.95f;                       // audio going quiet
                _silenceRun += frameSec;
                if (_silenceRun >= SilenceEndsSegmentSec && _segFrames > 0)
                {
                    EndSegment();
                }
                return;
            }
            // Sound, but no voice in it (Silero, while the gate works): music, effects. To the voices it is a pause —
            // a turn ends in it as in silence, where the music running under a whole conversation used to hold one
            // segment open across every turn of it — while the audio is still "present" and plainly not speech.
            bool vadGated = VadGate && VadActive;
            if (vadGated && !_vadSpeech)
            {
                // Sound with no voice in it: how loud what the voices are heard over is (TimbreTrusted).
                _bgRms = _bgRms * BackgroundDecay + rms * (1 - BackgroundDecay);
                _activeEma = _activeEma * 0.95f + 0.05f;
                _voicedEma *= 0.93f;
                _vadVoiceEma *= 0.93f;
                _silenceRun += frameSec;
                if (_silenceRun >= SilenceEndsSegmentSec && _segFrames > 0)
                {
                    EndSegment();
                }
                return;
            }

            // Sound again after a short pause that did not end the segment: remember where, in case
            // the voice turns out to have changed across it.
            if (_silenceRun >= PauseMarkSec && _segPitch.Count > 0
                && (_pauses.Count == 0 || _pauses[_pauses.Count - 1] != _segPitch.Count))
            {
                _pauses.Add(_segPitch.Count);
                if (_pauses.Count > MaxPausesKept) { _pauses.RemoveAt(0); }
            }
            _silenceRun = 0;
            _activeEma = _activeEma * 0.95f + 0.05f;       // audio present
            // Sound Silero hears a voice in (vadGated here means it does): CurrentAudioKind's second witness.
            _vadVoiceEma = _vadVoiceEma * 0.93f + (vadGated ? 0.07f : 0f);

            // The voice's audio, for its fingerprint (round 34): every piece heard as a voice, once — the first half of each
            // window is the part the next window no longer holds. Where this frame's audio begins (absolute: what was cut
            // from the front is counted in _segAudioDropped).
            int framePos = _segAudioDropped + _segAudio.Count;
            if (PrintsActive)
            {
                int hop = win / 2;
                for (int i = 0; i < hop; i++) { _segAudio.Add(buf[i]); }
                _segAudioSincePrint += hop;
                if (_segAudio.Count > SegAudioMaxSamples)
                {
                    int cut = _segAudio.Count - SegAudioMaxSamples;
                    _segAudio.RemoveRange(0, cut);
                    _segAudioDropped += cut;
                }
            }

            // Brightness: zero-crossing rate.
            int zc = 0;
            for (int i = 1; i < win; i++)
            {
                if ((_frame[i - 1] < 0) != (_frame[i] < 0))
                {
                    zc++;
                }
            }
            double zcr = zc / (double)win;

            // Pitch by YIN rather than by the autocorrelation peak. The peak could not tell a
            // voice's period from half of it: measured on a male voice, several frames at the start
            // of a sentence read an octave HIGH (~190 Hz for a ~95 Hz voice), right in a female
            // voice's range — early enough to decide who was talking, and wrongly. YIN's normalised
            // difference punishes those short lags by construction.
            double f0 = YinPitch(win, out double aperiodicity);

            _segFrames++;
            // A steady pitch is a voice only while Silero hears one (see VadGate): a held note of music has a pitch too.
            // With Silero saying "a voice", a slightly less clean pitch is accepted: music under the voice muddies it.
            bool voiced = f0 > 0 && aperiodicity < (vadGated ? YinVoicedUnderVad : YinVoiced) && (!VadGate || _vadSpeech);
            if (voiced)
            {
                _segPitch.Add(f0);   // confident, voiced frame
                _segPitchAudio.Add(framePos);   // in step with _segPitch: where its audio begins (a split cuts the audio there)
                _segZcr.Add(zcr);
                // Its timbre (see FrameTimbre) — voiced frames only: the vowels carry a voice's spectrum, the hiss of an
                // "s" carries the word.
                _voiceRms = _voiceRms <= 0 ? rms : _voiceRms * VoiceLevelDecay + rms * (1 - VoiceLevelDecay);
                // Only where Silero is sure of a voice in THIS piece: its 256 ms of carry-over after a voice stops is what
                // the music under it sounds like, and a held note YIN reads as a pitch there is all music (null = none).
                if (UseTimbre)
                {
                    bool sure = !VadGate || !VadActive || _vadLastP >= 0.5f;
                    if (sure) { MelEnergies(win); }
                    _segTimbre.Add(sure ? FrameTimbre() : null);
                }
                // Brightness from VOICED frames only. Unvoiced frames are dominated
                // by whichever s/sh/f sounds the sentence happens to contain, so an
                // all-frames average made the fingerprint depend on the WORDS more
                // than the voice — the same person then mismatched their own profile
                // on a sibilant-heavy sentence and got a fresh speaker number.
                _segZcrSum += zcr;

                _winPitch[_winHead] = f0;
                _winZcr[_winHead] = zcr;
                _winHead = (_winHead + 1) % WindowFrames;
                if (_winCount < WindowFrames) { _winCount++; }
            }
            // Speech-vs-sound signal: speech frames mostly carry a stable human
            // pitch; music and effects mostly don't.
            _voicedEma = _voicedEma * 0.93f + (voiced ? 0.07f : 0f);

            // Enough evidence mid-segment? Name the speaker now so the label appears
            // while they're still talking, not only after they stop. Two guards keep
            // the voice table clean:
            //  - only classify while the audio actually sounds like SPEECH — music has
            //    pitch too, and classifying it minted phantom speakers and dragged
            //    real profiles toward the soundtrack;
            //  - a brand-new speaker needs more evidence than matching a known one
            //    (retry as frames accumulate until there's enough).
            // From 8 voiced frames a KNOWN voice can be named when the match is clear-cut; the
            // ordinary thresholds wait for 12, and a new voice for 30.
            if (!_segClassified && _segPitch.Count >= FastClassifyFrames && _voicedEma >= 0.3f)
            {
                int n = _segPitch.Count;
                bool allowNew = n >= NewVoiceFramesNow;
                // Not with prints (round 34): the stretch is the print's to judge, and cutting it back to its newest third of a
                // second kept an expressive voice's audio from ever reaching the 1.6 s a print mints from — one excited
                // monologue stayed unnamed for 18 s (voiceflip). A stretch straddling two people prints as nobody clearly.
                if (allowNew && !PrintsActive && SegmentSpreadOctaves() > NewVoiceMaxSpreadOctaves)
                {
                    // Two voices' worth of pitches: not a new person, a stretch straddling a change.
                    // Keep only the newest frames — where the voice speaking now is — and look again.
                    allowNew = false;
                    TrimSegmentToWindow();
                    n = _segPitch.Count;
                }
                int timbreN = 0;
                double[] segTimbre = TimbreTrusted ? SegmentTimbreMean(0, _segTimbre.Count, out timbreN) : null;
                // With fingerprints, a NEW voice is the print's call alone (ClassifyByPrint): pitch and timbre still name a
                // known voice early — what makes labels quick — but one excited person or a burst of music no longer mints one.
                bool prints = PrintsActive;
                // Under loud sound the timbre is not trusted and the early name would be pitch's alone, which under music
                // named the wrong one of two people often enough to be corrected a moment later (a label flip each time):
                // with prints, that stretch waits for its print (~1.2 s) instead (EarlyPitchUnderSound off).
                bool pitchAloneUnderSound = prints && segTimbre == null && UnderSound;
                bool skipEarly = pitchAloneUnderSound && !EarlyPitchUnderSound && EarlyPitchMode == 0;
                int idx = skipEarly ? 0 : Classify(MedianPitch(), _segZcrSum / Math.Max(1, n), PitchSpread(),
                                   allowNew: allowNew && !prints,
                                   // Mode 1: under loud sound pitch names only a clear-cut voice, however many frames it has.
                                   confidentOnly: n < MinFramesToClassify || (pitchAloneUnderSound && !EarlyPitchUnderSound && EarlyPitchMode == 1),
                                   frames: n,
                                   timbre: segTimbre,
                                   timbreN: timbreN);
                // ...and not a voice this stretch's print has already ruled out.
                if (idx > 0 && prints && _segPrint != null && idx - 1 < _profiles.Count && _profiles[idx - 1].Print != null
                    && VoicePrint.Cosine(_segPrint, _profiles[idx - 1].Print) <= DifferBar(_segPrintLen))
                {
                    _printVetoes++;
                    idx = 0;
                }
                if (idx > 0)
                {
                    NameSegment(idx, byPrint: false);
                }
            }
            else if (_segClassified && voiced && ++_sinceDriftCheck >= DriftCheckEvery && _winCount >= WindowFrames)
            {
                _sinceDriftCheck = 0;
                CheckForSpeakerChange();
            }

            // A look by fingerprint every 0.8 s of voice, once the stretch has 1.2 s of it.
            if (PrintsActive && _segAudio.Count >= PrintMinSamples && _segAudioSincePrint >= PrintEverySamples
                && _segPitch.Count >= FastClassifyFrames)
            {
                PrintStep();
            }
        }

        /// <summary>The segment is <paramref name="idx"/>'s (1-based) from now: named by the print, or early by pitch/timbre (then checked by the next print).</summary>
        private void NameSegment(int idx, bool byPrint)
        {
            _segClassified = true;
            _segProfileIdx = idx - 1;
            _currentSpeaker = idx;
            _activeSegmentSpeaker = idx;
            _sinceDriftCheck = 0;
            _driftStrikes = 0;
            _printStrikes = 0;
            _segPrintChecked = byPrint;
        }

        // Whether the segment's name has been checked by a print yet (an early pitch/timbre name has not).
        private bool _segPrintChecked;
        private int _segPrintLen;            // samples of voice behind _segPrint
        private float[] _printBuf;

        /// <summary>The print of the segment's voice audio [from, from + count) (indices into _segAudio); null when it can't.</summary>
        private float[] TakePrint(VoicePrint model, int from, int count)
        {
            if (model == null || count < PrintMinSamples || from < 0 || from + count > _segAudio.Count) { return null; }
            if (_printBuf == null || _printBuf.Length < count) { _printBuf = new float[SegAudioMaxSamples]; }
            _segAudio.CopyTo(from, _printBuf, 0, count);
            float[] e = model.Embed(_printBuf, 0, count);
            if (e != null) { _printsTaken++; }
            return e;
        }

        /// <summary>"Plainly another voice" for a print of <paramref name="samples"/> samples of voice (see PrintDifferLong).</summary>
        private static double DifferBar(int samples)
        {
            double t = (samples - PrintMinSamples) / (double)(VoicePrint.SampleRate * 12 / 10);   // 0 at 1.2 s, 1 at 2.4 s
            t = Math.Max(0, Math.Min(1, t));
            return PrintDiffer + (PrintDifferLong - PrintDiffer) * t;
        }

        /// <summary>The voice whose print is nearest <paramref name="e"/> (0-based, -1 when none has one), skipping <paramref name="skip"/>; and the runner-up.</summary>
        private int NearestByPrint(float[] e, int skip, out float bestCos, out int second, out float secondCos)
        {
            int best = -1;
            second = -1;
            bestCos = -2f;
            secondCos = -2f;
            for (int i = 0; i < _profiles.Count; i++)
            {
                if (i == skip || _profiles[i].Print == null) { continue; }
                float c = VoicePrint.Cosine(e, _profiles[i].Print);
                if (c > bestCos) { second = best; secondCos = bestCos; best = i; bestCos = c; }
                else if (c > secondCos) { second = i; secondCos = c; }
            }
            return best;
        }

        /// <summary>
        /// A look by fingerprint (see the note on UsePrints): for an unnamed stretch, whose it is (or a new voice); for a named
        /// one, first whether the early name holds, then whether its newest 1.6 s still is that voice.
        /// </summary>
        private void PrintStep()
        {
            _segAudioSincePrint = 0;
            VoicePrint model = _voicePrint;
            if (model == null) { return; }
            var ci = System.Globalization.CultureInfo.InvariantCulture;

            if (!_segClassified)
            {
                int n = Math.Min(_segAudio.Count, PrintClassifySamples);
                float[] e = TakePrint(model, _segAudio.Count - n, n);
                if (e == null) { return; }
                _segPrint = e;
                _segPrintLen = n;
                int idx = ClassifyByPrint(e, n);
                if (idx > 0) { NameSegment(idx, byPrint: true); }
                return;
            }
            if (_segProfileIdx < 0 || _segProfileIdx >= _profiles.Count) { return; }
            Profile named = _profiles[_segProfileIdx];

            if (!_segPrintChecked)
            {
                // The early name (pitch/timbre, from ~0.5 s) against the whole stretch's print — at every look, until the print
                // settles it one way or the other. "Confirmed" takes a clear same-voice score: Mark was once confirmed as David
                // at 0.31, in the zone where nothing is known, and talked under David's name for the rest of his monologue.
                int n = Math.Min(_segAudio.Count, PrintClassifySamples);
                float[] e = TakePrint(model, _segAudio.Count - n, n);
                if (e == null) { return; }
                _segPrint = e;
                _segPrintLen = n;
                double differ = DifferBar(n);
                if (named.Print == null)
                {
                    int other = NearestByPrint(e, _segProfileIdx, out float oc, out int _, out float _);
                    if (other >= 0 && oc >= PrintSame)
                    {
                        // Plainly someone already known: the early name was wrong.
                        _printCorrections++;
                        _lastPrintVerdict = "corrected to voice " + (other + 1) + " (cosine " + oc.ToString("0.00", ci) + ")";
                        NameSegment(other + 1, byPrint: true);
                        return;
                    }
                    if (other >= 0 && oc > differ)
                    {
                        _lastPrintVerdict = "voice " + (_segProfileIdx + 1) + "? — near voice " + (other + 1) + " (" + oc.ToString("0.00", ci) + "); more voice decides";
                        return;
                    }
                    named.Print = (float[])e.Clone();   // its first print: a voice named before it had one
                    named.PrintN = 1;
                    _segPrintChecked = true;
                    _lastPrintVerdict = "voice " + (_segProfileIdx + 1) + " — its first print";
                    return;
                }
                float c = VoicePrint.Cosine(e, named.Print);
                _lastPrintCos = c;
                int best = NearestByPrint(e, _segProfileIdx, out float bc, out int _, out float _);
                if (c >= PrintSame && (best < 0 || c - bc >= PrintMargin))
                {
                    _segPrintChecked = true;
                    _lastPrintVerdict = "voice " + (_segProfileIdx + 1) + " confirmed (cosine " + c.ToString("0.00", ci) + ")";
                    return;
                }
                if (best >= 0 && bc >= PrintSame && bc - c >= PrintMargin)
                {
                    _printCorrections++;
                    _lastPrintVerdict = "corrected voice " + (_segProfileIdx + 1) + " → " + (best + 1) + " (cosine " + c.ToString("0.00", ci)
                                        + " → " + bc.ToString("0.00", ci) + ")";
                    NameSegment(best + 1, byPrint: true);
                    return;
                }
                if (c <= differ && (best < 0 || bc <= differ))
                {
                    // Nobody known: the early name was a guess for a new person. Unnamed again, so the print can mint them.
                    _printCorrections++;
                    _lastPrintVerdict = "not voice " + (_segProfileIdx + 1) + " (cosine " + c.ToString("0.00", ci) + ") — someone new";
                    _segClassified = false;
                    _segProfileIdx = -1;
                    _activeSegmentSpeaker = 0;
                    _segAudioSincePrint = PrintEverySamples;   // look again at once
                    return;
                }
                _lastPrintVerdict = "voice " + (_segProfileIdx + 1) + " unsure (cosine " + c.ToString("0.00", ci) + "); more voice decides";
                return;
            }

            // Inside a named stretch: the newest 1.6 s against the named voice.
            int w = Math.Min(_segAudio.Count, PrintWindowSamples);
            float[] recent = TakePrint(model, _segAudio.Count - w, w);
            if (recent == null) { return; }
            if (named.Print == null) { named.Print = (float[])recent.Clone(); named.PrintN = 1; return; }
            float cr = VoicePrint.Cosine(recent, named.Print);
            _lastPrintCos = cr;
            if (cr > DifferBar(w))
            {
                _printStrikes = 0;
                _lastPrintVerdict = "voice " + (_segProfileIdx + 1) + " still (cosine " + cr.ToString("0.00", ci) + ")";
                return;
            }
            if (++_printStrikes < 2)
            {
                _lastPrintVerdict = "voice " + (_segProfileIdx + 1) + "? (cosine " + cr.ToString("0.00", ci) + ", once)";
                return;
            }
            // Plainly another person, twice running: the stretch is cut where the new voice most likely began — the window's
            // print crosses the bar about when half of it is the new voice, and the deciding look came 0.8 s after the first;
            // a short pause near there is where people hand over.
            int guessAbs = _segAudioDropped + Math.Max(0, _segAudio.Count - PrintWindowSamples / 2 - PrintEverySamples);
            int splitAt = FirstVoicedFrameFrom(guessAbs);
            int bestGap = int.MaxValue;
            foreach (int p in _pauses)
            {
                if (p <= 0 || p >= _segPitch.Count - 4 || p >= _segPitchAudio.Count) { continue; }
                int gap = Math.Abs(_segPitchAudio[p] - guessAbs);
                if (gap < bestGap && gap <= PrintEverySamples) { bestGap = gap; splitAt = p; }
            }
            if (splitAt <= 0 || splitAt >= _segPitch.Count) { _printStrikes = 0; return; }
            _lastPrintVerdict = "voice " + (_segProfileIdx + 1) + " stopped (cosine " + cr.ToString("0.00", ci) + ") — split";
            SplitSegmentAt(splitAt);
            _printSplits++;
            _segAudioSincePrint = PrintEverySamples;   // the new part's own look at once
        }

        /// <summary>The first voiced frame of the segment whose audio begins at or after <paramref name="absPos"/>.</summary>
        private int FirstVoicedFrameFrom(int absPos)
        {
            for (int i = 0; i < _segPitchAudio.Count; i++)
            {
                if (_segPitchAudio[i] >= absPos) { return i; }
            }
            return _segPitchAudio.Count;
        }

        /// <summary>
        /// The stretch before voiced frame <paramref name="splitAt"/> was the named voice's: folded into it (its pitch, timbre and
        /// print); the rest becomes a new, unnamed segment.
        /// </summary>
        private void SplitSegmentAt(int splitAt)
        {
            if (_segProfileIdx >= 0 && _segProfileIdx < _profiles.Count && splitAt >= MinFramesToClassify)
            {
                var head = _segPitch.GetRange(0, splitAt);
                double headZcr = 0;
                for (int i = 0; i < splitAt && i < _segZcr.Count; i++) { headZcr += _segZcr[i]; }
                UpdateProfile(_segProfileIdx, Median(head), headZcr / splitAt, Spread(head));
                FoldTimbre(_segProfileIdx, splitAt);
                if (splitAt < _segPitchAudio.Count)
                {
                    int headEnd = _segPitchAudio[splitAt] - _segAudioDropped;
                    int n = Math.Min(headEnd, PrintFoldSamples);
                    if (n >= PrintMinSamples) { FoldPrint(_segProfileIdx, TakePrint(_voicePrint, headEnd - n, n)); }
                }
            }
            KeepSegmentFrom(splitAt);
            _segClassified = false;
            _segProfileIdx = -1;
            _activeSegmentSpeaker = 0;
            _driftStrikes = 0;
            _printStrikes = 0;
            _segPrint = null;
            _midSpeechChanges++;
        }

        /// <summary>
        /// Folds a stretch's print into voice <paramref name="idx"/>'s — only when it agrees with it: a print of someone else
        /// (a mislabelled stretch) folded in would make one voice answer for two people, as one blended face print did.
        /// </summary>
        private void FoldPrint(int idx, float[] e)
        {
            if (e == null || idx < 0 || idx >= _profiles.Count) { return; }
            Profile p = _profiles[idx];
            if (p.Print == null)
            {
                int other = NearestByPrint(e, idx, out float oc, out int _, out float _);
                if (other >= 0 && oc >= PrintSame) { return; }   // plainly someone else's
                p.Print = (float[])e.Clone();
                p.PrintN = 1;
                return;
            }
            if (VoicePrint.Cosine(e, p.Print) < PrintDiffer + 0.10) { return; }
            p.Print = VoicePrint.Fold(p.Print, p.PrintN, e);
            p.PrintN++;
        }

        /// <summary>
        /// Whose this stretch is by its print (1-based), a new voice minted from it, or 0 = not decided (more voice, or the
        /// pitch/timbre matcher, decides).
        /// </summary>
        private int ClassifyByPrint(float[] e, int printSamples)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            int best = NearestByPrint(e, -1, out float bc, out int second, out float sc);
            if (best >= 0 && bc >= PrintSame && (second < 0 || bc - sc >= PrintMargin))
            {
                _lastPrintCos = bc;
                _lastPrintVerdict = "voice " + (best + 1) + " (cosine " + bc.ToString("0.00", ci) + ")";
                return best + 1;
            }
            bool nobody = best < 0 || bc <= DifferBar(printSamples);
            if (!nobody || printSamples < PrintNewVoiceSamples)
            {
                _lastPrintVerdict = best >= 0 ? "undecided — nearest voice " + (best + 1) + " at " + bc.ToString("0.00", ci) : "waiting for more voice";
                return 0;
            }
            // A voice learned before it had a print: the pitch/timbre matcher's word for it first.
            int n = _segPitch.Count;
            double pitch = MedianPitch();
            int tn = 0;
            double[] timbre = TimbreTrusted ? SegmentTimbreMean(0, _segTimbre.Count, out tn) : null;
            for (int i = 0; i < _profiles.Count; i++)
            {
                Profile p = _profiles[i];
                if (p.Print != null) { continue; }
                bool match = timbre != null && HasTimbre(p) ? VoiceScore(p, pitch, timbre) <= BarFor(Math.Max(tn, 1))
                                                            : p.Pitch > 0 && Math.Abs(Octaves(pitch, p.Pitch)) < StrictOctaves;
                if (match)
                {
                    _lastPrintVerdict = "voice " + (i + 1) + " (no print of its own yet — by its sound)";
                    return i + 1;
                }
            }
            if (_profiles.Count >= MaxProfiles)
            {
                // Table full: the nearest voice by print takes it (counted — two people now share a number).
                _forcedMerges++;
                _lastPrintVerdict = "voice table full — nearest voice " + (best + 1);
                return best >= 0 ? best + 1 : 1;
            }
            _profiles.Add(new Profile
            {
                Pitch = pitch, Bright = _segZcrSum / Math.Max(1, n), Spread = PitchSpread(), Weight = 1,
                Timbre = timbre != null && UseTimbre ? (double[])timbre.Clone() : null,
                TimbreN = timbre != null && UseTimbre ? tn : 0,
                Print = (float[])e.Clone(),
                PrintN = 1,
            });
            if (timbre != null && UseTimbre) { _segTimbreFolded = _segTimbre.Count; }
            _printMints++;
            _lastPrintVerdict = "a new voice, " + _profiles.Count + (best >= 0 ? " (nearest known " + bc.ToString("0.00", ci) + ")" : "");
            return _profiles.Count;
        }

        /// <summary>
        /// A speaker change with no pause in front of it. A stretch of speech used to keep the name it
        /// was given until 0.9 s of silence, so when one person answered another quickly the reply was
        /// filed under the first speaker — and folded into THEIR voice, dragging it toward the second
        /// person's. Measured: a quick exchange between a male and a female voice ended with ONE voice
        /// learned for both. The latest voiced frames are now compared with the named voice as they
        /// arrive; when they have clearly left it twice running, the stretch is cut there and the new
        /// part is named afresh.
        /// </summary>
        private void CheckForSpeakerChange()
        {
            if (_segProfileIdx < 0 || _segProfileIdx >= _profiles.Count) { return; }
            // The voice has a print and prints are being taken: a change inside the stretch is the print's call (PrintStep) —
            // the excited rises and creaky ends that pitch and timbre read as another person split nothing.
            if (PrintsActive && _profiles[_segProfileIdx].Print != null) { return; }
            if (TimbreTrusted && HasTimbre(_profiles[_segProfileIdx]) && _segTimbre.Count >= TimbreWindow)
            {
                CheckForSpeakerChangeByTimbre();
                return;
            }
            double named = _profiles[_segProfileIdx].Pitch;
            double recent = MedianOfNewest(Math.Min(DriftFrames, _winCount));
            if (recent <= 0 || named <= 0) { return; }
            if (Math.Abs(Octaves(recent, named)) <= DriftOctaves)
            {
                _driftStrikes = 0;
                return;
            }
            if (++_driftStrikes < DriftStrikes) { return; }

            // Where the new voice starts. First choice: a short pause — people hand over at one even
            // when it is too short to end the segment — the EARLIEST one after which hardly anything
            // still sounds like the named voice (a later pause can sit inside the new speaker's own
            // sentence). Otherwise: the newest run of frames that are clearly not the named voice.
            // Taking a fixed window instead swept the previous speaker's sentence-final dip in with the
            // new voice; the blend matched neither, and minted a phantom third voice.
            int splitAt = -1;
            foreach (int p in _pauses)
            {
                if (p <= 0 || p > _segPitch.Count - 4) { continue; }
                int stillNamed = 0;
                for (int i = p; i < _segPitch.Count; i++)
                {
                    if (Math.Abs(Octaves(_segPitch[i], named)) <= DriftOctaves / 2) { stillNamed++; }
                }
                if (stillNamed * 4 < _segPitch.Count - p) { splitAt = p; break; }
            }
            if (splitAt < 0)
            {
                int fresh = 0;
                for (int i = _segPitch.Count - 1; i >= 0 && fresh < WindowFrames; i--)
                {
                    if (Math.Abs(Octaves(_segPitch[i], named)) <= DriftOctaves) { break; }
                    fresh++;
                }
                if (fresh < 4) { _driftStrikes = 0; return; }   // not a clean run yet — look again later
                splitAt = _segPitch.Count - fresh;
            }

            // Everything before the split belonged to the named voice: fold it in.
            if (splitAt >= MinFramesToClassify)
            {
                var head = _segPitch.GetRange(0, splitAt);
                double headZcr = 0;
                for (int i = 0; i < splitAt && i < _segZcr.Count; i++) { headZcr += _segZcr[i]; }
                UpdateProfile(_segProfileIdx, Median(head), headZcr / splitAt, Spread(head));
                FoldTimbre(_segProfileIdx, splitAt);
            }

            // The rest becomes the new segment, unnamed until Classify says whose it is.
            KeepSegmentFrom(splitAt);
            _segClassified = false;
            _segProfileIdx = -1;
            _activeSegmentSpeaker = 0;
            _driftStrikes = 0;
            _midSpeechChanges++;
        }

        /// <summary>
        /// A speaker change inside a stretch of speech, judged on the voice's timbre (round 25): the newest TimbreWindow
        /// voiced frames against the named voice. A change only when they have plainly left it (VoiceScore over the bar for
        /// another voice) or plainly joined another known one, at two looks running. A pitch jump that keeps the voice's own
        /// timbre is the person's prosody and splits nothing — in one excited monologue it had made nine splits, a second
        /// voice, and six label changes the user saw in the middle of one person's talk.
        /// </summary>
        private void CheckForSpeakerChangeByTimbre()
        {
            int count = _segTimbre.Count;
            int from = count - TimbreWindow;
            double[] recent = SegmentTimbreMean(from, TimbreWindow, out int used);
            // Too little of the window was surely a voice (music over it): no verdict either way this time.
            if (recent == null || used < TimbreWindow * 2 / 3) { return; }
            double recentPitch = MedianOfRange(_segPitch, from, TimbreWindow);
            double sNamed = VoiceScore(_profiles[_segProfileIdx], recentPitch, recent);
            double same = BarFor(TimbreWindow), differ = same + 0.15;
            double sOther = double.MaxValue;
            for (int i = 0; i < _profiles.Count; i++)
            {
                if (i == _segProfileIdx || !HasTimbre(_profiles[i])) { continue; }
                sOther = Math.Min(sOther, VoiceScore(_profiles[i], recentPitch, recent));
            }
            bool left = sNamed > differ || (sOther <= same && sOther < sNamed - 0.15);
            if (!left)
            {
                _driftStrikes = 0;
                return;
            }
            if (++_driftStrikes < TimbreDriftStrikes) { return; }

            // Where the new voice starts. The window's mean crosses the bar about when half of it is the new voice, and the
            // deciding look comes (TimbreDriftStrikes - 1) × DriftCheckEvery frames later. A short pause near there is where
            // people hand over, and is taken when there is one.
            int guess = Math.Max(0, count - TimbreWindow / 2 - (TimbreDriftStrikes - 1) * DriftCheckEvery);
            int splitAt = guess, bestGap = int.MaxValue;
            foreach (int p in _pauses)
            {
                if (p <= 0 || p > count - 4 || p < count - TimbreWindow - 8) { continue; }
                int gap = Math.Abs(p - guess);
                if (gap < bestGap) { bestGap = gap; splitAt = p; }
            }
            if (splitAt >= MinFramesToClassify)
            {
                var head = _segPitch.GetRange(0, splitAt);
                double headZcr = 0;
                for (int i = 0; i < splitAt && i < _segZcr.Count; i++) { headZcr += _segZcr[i]; }
                UpdateProfile(_segProfileIdx, Median(head), headZcr / splitAt, Spread(head));
                FoldTimbre(_segProfileIdx, splitAt);
            }
            KeepSegmentFrom(splitAt);
            _segClassified = false;
            _segProfileIdx = -1;
            _activeSegmentSpeaker = 0;
            _driftStrikes = 0;
            _midSpeechChanges++;
        }

        /// <summary>Median of <paramref name="values"/>[from, from + count).</summary>
        private static double MedianOfRange(List<double> values, int from, int count)
        {
            from = Math.Max(0, from);
            int to = Math.Min(values.Count, from + count);
            if (to <= from) { return 0; }
            var s = values.GetRange(from, to - from);
            s.Sort();
            return s[s.Count / 2];
        }

        // How many voiced frames of its past a voice's fingerprint weighs against a new stretch (~13 s of voicing): enough
        // to stay steady, few enough to follow a voice whose sound changes (a mic moved, a different scene's mix).
        private const int TimbreMemoryFrames = 600;

        /// <summary>
        /// Folds the segment's voiced frames not yet in a profile, up to <paramref name="upTo"/>, into voice
        /// <paramref name="idx"/>'s fingerprint.
        /// </summary>
        private void FoldTimbre(int idx, int upTo)
        {
            if (!UseTimbre || idx < 0 || idx >= _profiles.Count) { return; }
            upTo = Math.Min(upTo, _segTimbre.Count);
            // Heard over loud sound (TimbreTrusted): not the voice's own spectrum — never folded in, never folded later.
            if (!TimbreTrusted)
            {
                _segTimbreFolded = Math.Max(_segTimbreFolded, upTo);
                return;
            }
            int from = Math.Min(_segTimbreFolded, upTo);
            int n = 0;
            double[] mean = upTo > from ? SegmentTimbreMean(from, upTo - from, out n) : null;
            if (mean != null && n > 0)
            {
                Profile p = _profiles[idx];
                if (p.Timbre == null || p.TimbreN <= 0)
                {
                    p.Timbre = mean;
                    p.TimbreN = n;
                }
                else
                {
                    int w = Math.Min(p.TimbreN, TimbreMemoryFrames);
                    for (int k = 0; k < TimbreCoeffs; k++) { p.Timbre[k] = (p.Timbre[k] * w + mean[k] * n) / (w + n); }
                    p.TimbreN = Math.Min(p.TimbreN + n, 1000000);
                }
            }
            _segTimbreFolded = Math.Max(_segTimbreFolded, upTo);
        }

        /// <summary>
        /// Makes the segment's voiced frames from <paramref name="from"/> onward the whole segment, with
        /// the window holding its newest ones; everything before is dropped.
        /// </summary>
        private void KeepSegmentFrom(int from)
        {
            from = Math.Max(0, Math.Min(from, _segPitch.Count));
            if (from > 0)
            {
                // The voice audio before that frame goes with it (round 34): the new segment's prints are of the new part only.
                int absCut = from < _segPitchAudio.Count ? _segPitchAudio[from] : _segAudioDropped + _segAudio.Count;
                int local = Math.Max(0, Math.Min(_segAudio.Count, absCut - _segAudioDropped));
                if (local > 0)
                {
                    _segAudio.RemoveRange(0, local);
                    _segAudioDropped += local;
                }
                _segPitchAudio.RemoveRange(0, Math.Min(from, _segPitchAudio.Count));
                _segPitch.RemoveRange(0, from);
                _segZcr.RemoveRange(0, Math.Min(from, _segZcr.Count));
                _segTimbre.RemoveRange(0, Math.Min(from, _segTimbre.Count));
                _segTimbreFolded = Math.Max(0, _segTimbreFolded - from);
            }
            _segZcrSum = 0;
            foreach (double z in _segZcr) { _segZcrSum += z; }
            _segFrames = _segPitch.Count;
            _winCount = 0;
            _winHead = 0;
            for (int i = Math.Max(0, _segPitch.Count - WindowFrames); i < _segPitch.Count; i++)
            {
                _winPitch[_winHead] = _segPitch[i];
                _winZcr[_winHead] = i < _segZcr.Count ? _segZcr[i] : 0;
                _winHead = (_winHead + 1) % WindowFrames;
                _winCount++;
            }
            // Pauses before the cut no longer exist; the ones after it move down with the frames.
            for (int k = _pauses.Count - 1; k >= 0; k--)
            {
                int moved = _pauses[k] - from;
                if (moved <= 0) { _pauses.RemoveAt(k); } else { _pauses[k] = moved; }
            }
        }

        /// <summary>How spread the segment's pitches are, in octaves between its quartiles.</summary>
        private double SegmentSpreadOctaves()
        {
            if (_segPitch.Count < 4) { return 0; }
            var s = new List<double>(_segPitch);
            s.Sort();
            double lo = s[s.Count / 4], hi = s[(s.Count * 3) / 4];
            return lo > 0 ? Octaves(hi, lo) : 0;
        }

        /// <summary>Cuts the segment back to its newest voiced frames (the window's worth).</summary>
        private void TrimSegmentToWindow()
        {
            KeepSegmentFrom(_segPitch.Count - Math.Min(_winCount, _segPitch.Count));
        }

        /// <summary>Median pitch of the newest <paramref name="count"/> voiced frames in the window.</summary>
        private double MedianOfNewest(int count)
        {
            if (count <= 0) { return 0; }
            var s = new double[count];
            for (int k = 0; k < count; k++) { s[k] = _winPitch[(_winHead - 1 - k + WindowFrames) % WindowFrames]; }
            Array.Sort(s);
            return s[count / 2];
        }

        /// <summary>
        /// Fundamental frequency of the current frame by YIN (de Cheveigné &amp; Kawahara, 2002), or 0
        /// when there is no period in the 60–400 Hz voice band. <paramref name="aperiodicity"/> is the
        /// normalised difference at the chosen lag: near 0 for a clean vowel, near 1 for noise.
        /// </summary>
        private double YinPitch(int win, out double aperiodicity)
        {
            aperiodicity = 1;
            int minLag = Math.Max(2, _rate / 400);
            int maxLag = Math.Min(win / 2, _rate / 60);
            int span = win - maxLag;                     // samples compared at every lag
            if (maxLag <= minLag + 2 || span < 32) { return 0; }
            if (_yin == null || _yin.Length < maxLag + 2) { _yin = new double[maxLag + 2]; }

            // Difference function, then its cumulative-mean normalisation.
            _yin[0] = 1;
            double running = 0;
            for (int lag = 1; lag <= maxLag; lag++)
            {
                double d = 0;
                for (int i = 0; i < span; i++)
                {
                    double diff = _frame[i] - _frame[i + lag];
                    d += diff * diff;
                }
                running += d;
                _yin[lag] = running > 0 ? d * lag / running : 1;
            }

            // The FIRST dip under the threshold, walked down to its bottom — the shortest period that
            // really repeats, which is what stops both the octave-up and the octave-down misreads.
            int best = -1;
            for (int lag = minLag; lag <= maxLag; lag++)
            {
                if (_yin[lag] < YinThreshold)
                {
                    while (lag + 1 <= maxLag && _yin[lag + 1] < _yin[lag]) { lag++; }
                    best = lag;
                    break;
                }
            }
            if (best < 0)
            {
                // Nothing under the threshold: the deepest dip, which the voicing test will usually
                // reject — breathy or noisy frames land here.
                double min = double.MaxValue;
                for (int lag = minLag; lag <= maxLag; lag++)
                {
                    if (_yin[lag] < min) { min = _yin[lag]; best = lag; }
                }
                if (best < 0) { return 0; }
            }
            aperiodicity = _yin[best];

            // Parabolic interpolation for a sub-sample period.
            double period = best;
            if (best > minLag && best < maxLag)
            {
                double a = _yin[best - 1], b = _yin[best], c = _yin[best + 1];
                double den = a - 2 * b + c;
                if (Math.Abs(den) > 1e-12) { period = best + 0.5 * (a - c) / den; }
            }
            return period > 0 ? _rate / period : 0;
        }

        /// <summary>
        /// The current frame's timbre: MFCCs c1..c12 of its spectrum over 100–7000 Hz (24 mel bands, Hamming window, the
        /// natural log of each band's power, DCT-II without c0 — so the frame's loudness does not enter). The exact recipe
        /// TimbreScale was measured with (scratchpad/voiceflip/timbre); changing it means measuring that again.
        /// </summary>
        private double[] FrameTimbre()
        {
            for (int b = 0; b < TimbreBands; b++) { _melLog[b] = Math.Log(_melE[b] + 1e-10); }
            var c = new double[TimbreCoeffs];
            for (int k = 0; k < TimbreCoeffs; k++)
            {
                double s = 0;
                for (int b = 0; b < TimbreBands; b++) { s += _dct[k, b] * _melLog[b]; }
                c[k] = s;
            }
            return c;
        }

        /// <summary>The current frame's power in each mel band (<see cref="_melE"/>).</summary>
        private void MelEnergies(int win)
        {
            if (_fftRe == null || _timbreWin != win || _timbreRate != _rate) { InitTimbre(win); }
            Array.Clear(_fftRe, 0, _fftN);
            Array.Clear(_fftIm, 0, _fftN);
            for (int i = 0; i < win; i++) { _fftRe[i] = _frame[i] * _hamming[i]; }
            Fft(_fftRe, _fftIm);
            for (int b = 0; b < TimbreBands; b++)
            {
                double e = 0;
                int lo = _melLo[b], mid = _melMid[b], hi = _melHi[b];
                for (int k = lo; k < hi && k < _fftN / 2; k++)
                {
                    double w = k < mid ? (k - lo) / (double)(mid - lo) : (hi - k) / (double)(hi - mid);
                    e += w * (_fftRe[k] * _fftRe[k] + _fftIm[k] * _fftIm[k]);
                }
                _melE[b] = e;
            }
        }

        private readonly double[] _melE = new double[TimbreBands];

        // How loud the sound UNDER the voices is (frames Silero hears no voice in), against the voices' own level. A voice's
        // timbre is the shape of ITS spectrum; under a loud music bed every frame's spectrum is half music, its shape moves
        // with the chords, and one voice's stretches no longer look alike (scratchpad/voiceflip at music 0.15: timbre made
        // more label changes than pitch alone). Taking the music away band by band (minimum statistics) was tried and made
        // even clean speech worse — it took the voice's own floor with it in continuous talk. So the timbre is trusted
        // only while the voices stand clearly above what is under them (TimbreTrustedSnrDb); under that the matcher is the
        // pitch matcher of before, and nothing heard then goes into a voice's fingerprint.
        private const double BackgroundDecay = 0.985;     // ~1.4 s at 21.5 ms a frame
        private const double VoiceLevelDecay = 0.99;      // ~2 s of voicing
        // Measured (voiceflip, music bed at 0.05 ≈ 11 dB and 0.15 ≈ 4 dB under these measures): at 11 dB the timbre
        // matcher made 0 label changes in David's, Mark's and David-then-Zira's talk (pitch alone: 9, 20, 30); at 4 dB it
        // named nothing of Mark's at all and split more than pitch alone. Trusted from 7 dB.
        internal static double TimbreTrustedSnrDb = 7;
        private double _bgRms, _voiceRms;

        /// <summary>
        /// The bar of VoiceScore for a stretch of <paramref name="frames"/> (SameVoiceBar). Raising it with the sound under
        /// the voices was tried (+25 % at the trust limit): David and Mark became ONE voice under the music bed at 0.05.
        /// </summary>
        private double BarFor(int frames) => SameVoiceBar(frames) * BarScale;
        internal static double BarScale = 1.0;
        // Looks in a row that must find the voice gone before a stretch is split by timbre (one look every DriftCheckEvery
        // voiced frames).
        internal static int TimbreDriftStrikes = 3;

        /// <summary>True while the voices stand clearly above the sound under them (the timbre can be trusted).</summary>
        private bool TimbreTrusted
        {
            get
            {
                if (!UseTimbre) { return false; }
                if (_bgRms <= 1e-6) { return true; }
                if (_voiceRms <= 0) { return false; }
                return 20 * Math.Log10(_voiceRms / _bgRms) >= TimbreTrustedSnrDb;
            }
        }

        /// <summary>For Live debug and the tests: the voices against what is under them, in dB (99 = nothing under them).</summary>
        public double VoiceOverBackgroundDb => _bgRms <= 1e-6 ? 99 : _voiceRms <= 0 ? -99 : 20 * Math.Log10(_voiceRms / _bgRms);

        // Round 26 (the user: "false positives when background music conflicts with the talk"): with music or a crowd
        // under the voices, a stretch's sound is half someone else's, and a burst of it — a sung line, a held note under
        // a word — minted voices nobody had (scratchpad/voiceflip at music 0.05/0.15: three voices for one woman, four for
        // two people). Under sound a new voice needs twice the voicing (~1.3 s by pitch, ~2 s by timbre).
        internal static double NoisyBelowDb = 15;
        private bool UnderSound => _bgRms > 1e-6 && VoiceOverBackgroundDb < NoisyBelowDb;
        // Graded, not doubled: twice the voicing everywhere under sound left most of a loud mix's speech unnamed (one
        // voice never named at all at 3 dB) and merged two men at 10 dB (voiceflip).
        internal static double NewVoiceUnderSound = 1.33, NewVoiceUnderLoudSound = 1.67;
        private double NewVoiceFactor => !UnderSound ? 1.0
                                         : VoiceOverBackgroundDb < TimbreTrustedSnrDb ? NewVoiceUnderLoudSound : NewVoiceUnderSound;
        private int NewVoiceFramesNow => (int)Math.Round(MinFramesForNewSpeaker * NewVoiceFactor);

        private void InitTimbre(int win)
        {
            _timbreWin = win;
            _timbreRate = _rate;
            _fftN = 1;
            while (_fftN < win) { _fftN <<= 1; }
            _fftRe = new double[_fftN];
            _fftIm = new double[_fftN];
            _hamming = new double[win];
            for (int i = 0; i < win; i++) { _hamming[i] = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (win - 1)); }
            double Mel(double hz) => 2595 * Math.Log10(1 + hz / 700.0);
            double Hz(double mel) => 700 * (Math.Pow(10, mel / 2595) - 1);
            double top = Math.Min(TimbreHighHz, _rate / 2.0 - 200);
            double mLo = Mel(TimbreLowHz), mHi = Mel(top);
            var edges = new int[TimbreBands + 2];
            for (int b = 0; b < TimbreBands + 2; b++)
            {
                edges[b] = (int)Math.Round(Hz(mLo + (mHi - mLo) * b / (TimbreBands + 1)) * _fftN / _rate);
            }
            _melLo = new int[TimbreBands];
            _melMid = new int[TimbreBands];
            _melHi = new int[TimbreBands];
            for (int b = 0; b < TimbreBands; b++)
            {
                _melLo[b] = edges[b];
                _melMid[b] = Math.Max(edges[b + 1], _melLo[b] + 1);
                _melHi[b] = Math.Max(edges[b + 2], _melMid[b] + 1);
            }
            _dct = new double[TimbreCoeffs, TimbreBands];
            for (int k = 0; k < TimbreCoeffs; k++)
            {
                for (int b = 0; b < TimbreBands; b++) { _dct[k, b] = Math.Cos(Math.PI * (k + 1) * (b + 0.5) / TimbreBands); }
            }
        }

        /// <summary>In-place radix-2 FFT (length a power of two).</summary>
        private static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) { j ^= bit; }
                j ^= bit;
                if (i < j)
                {
                    double t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi;
                        re[a] += xr; im[a] += xi;
                        double t = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = t;
                    }
                }
            }
        }

        /// <summary>Distance between two timbres, in frame-to-frame spreads of speech (TimbreScale), averaged.</summary>
        private static double TimbreDistance(double[] a, double[] b)
        {
            double s = 0;
            for (int k = 0; k < TimbreCoeffs; k++)
            {
                double d = (a[k] - b[k]) / TimbreScale[k];
                s += d * d;
            }
            return Math.Sqrt(s / TimbreCoeffs);
        }

        /// <summary>
        /// The mean timbre of the segment's voiced frames [from, from + count) that have one (see the voiced branch of
        /// ProcessFrame), or null with none; <paramref name="used"/> = how many had one.
        /// </summary>
        private double[] SegmentTimbreMean(int from, int count, out int used)
        {
            used = 0;
            from = Math.Max(0, from);
            int to = Math.Min(_segTimbre.Count, from + count);
            if (to <= from) { return null; }
            var m = new double[TimbreCoeffs];
            for (int i = from; i < to; i++)
            {
                double[] c = _segTimbre[i];
                if (c == null) { continue; }
                for (int k = 0; k < TimbreCoeffs; k++) { m[k] += c[k]; }
                used++;
            }
            if (used == 0) { return null; }
            for (int k = 0; k < TimbreCoeffs; k++) { m[k] /= used; }
            return m;
        }

        /// <summary>
        /// How far a stretch of voice is from a learned one: its timbre distance, plus pitch past what prosody alone moves a
        /// voice (PitchSlack) at a fraction of the weight. Pitch still parts a man from a woman whose timbres overlap in a
        /// short look; it no longer parts one person from themselves.
        /// </summary>
        private static double VoiceScore(Profile p, double pitch, double[] timbre)
        {
            double t = TimbreDistance(timbre, p.Timbre);
            double oct = pitch > 0 && p.Pitch > 0 ? Math.Abs(Octaves(pitch, p.Pitch)) : 0;
            // Capped: under music YIN can read a held note as the pitch, and an octave of that must not outvote the timbre.
            return t + Math.Min(PitchCap, PitchWeight * Math.Max(0, oct - PitchSlack));
        }

        /// <summary>
        /// The score under which a stretch of <paramref name="frames"/> voiced frames is plainly that voice. A short look is
        /// noisy (its own voice's distance: median 0.52 over 12 frames, 0.35 over 24, 0.31 over 32), so the bar falls with
        /// the evidence; another voice's stretch sat 0.62–0.88 away (median).
        /// </summary>
        private static double SameVoiceBar(int frames) => 0.45 + 0.6 / Math.Sqrt(Math.Max(1, frames));

        /// <summary>True when the profile has a fingerprint to compare with.</summary>
        private static bool HasTimbre(Profile p) => p != null && p.Timbre != null && p.TimbreN >= TimbreMinProfileFrames;

        /// <summary>Signed distance between two pitches in octaves: log2(a / b).</summary>
        private static double Octaves(double a, double b)
        {
            return Math.Log(a / b, 2);
        }

        private static double Median(List<double> values)
        {
            if (values.Count == 0) { return 0; }
            var s = new List<double>(values);
            s.Sort();
            return s[s.Count / 2];
        }

        private static double Spread(List<double> values)
        {
            if (values.Count < 4) { return 0; }
            var s = new List<double>(values);
            s.Sort();
            return s[(s.Count * 3) / 4] - s[s.Count / 4];
        }

        private void EndSegment()
        {
            // Fold the whole segment's stats into the profile chosen mid-segment so it
            // sharpens over time. Crucially this UPDATES that same profile rather than
            // re-matching — re-matching let one utterance whose start and end sounded
            // slightly different spawn a duplicate profile ("Speaker 3" for two voices).
            if (_segClassified && _segProfileIdx >= 0 && _segProfileIdx < _profiles.Count &&
                _segPitch.Count >= MinFramesToClassify)
            {
                UpdateProfile(_segProfileIdx, MedianPitch(), _segZcrSum / Math.Max(1, _segPitch.Count), PitchSpread());
                FoldTimbre(_segProfileIdx, _segTimbre.Count);
                // And its print (up to the last 4 s of it), when it agrees with the voice's own (FoldPrint).
                if (PrintsActive && _segAudio.Count >= PrintMinSamples)
                {
                    int n = Math.Min(_segAudio.Count, PrintFoldSamples);
                    FoldPrint(_segProfileIdx, TakePrint(_voicePrint, _segAudio.Count - n, n));
                }
            }
            ClearSegmentAudio();
            _segPitch.Clear();
            _segZcr.Clear();
            _segTimbre.Clear();
            _segTimbreFolded = 0;
            _segZcrSum = 0;
            _segFrames = 0;
            _segClassified = false;
            _segProfileIdx = -1;
            _activeSegmentSpeaker = 0;
            _winCount = 0;
            _winHead = 0;
            _sinceDriftCheck = 0;
            _driftStrikes = 0;
            _pauses.Clear();
            // Keep _currentSpeaker as-is through the pause: the labeler decides turns;
            // we only answer "whose voice was that".
        }

        /// <summary>The segment's voice audio and its print state gone (a segment's end, forgotten voices, a stop).</summary>
        private void ClearSegmentAudio()
        {
            _segAudio.Clear();
            _segAudioDropped = 0;
            _segPitchAudio.Clear();
            _segAudioSincePrint = 0;
            _segPrint = null;
            _printStrikes = 0;
            _segPrintChecked = false;
        }

        /// <summary>
        /// One line per learned voice for Live debug: its pitch fingerprint,
        /// brightness, intonation range and how much evidence it's built on — plus
        /// which voice is speaking right now. Read-only diagnostic; any mid-update
        /// race just skips a beat rather than affecting the profiles.
        /// </summary>
        public string DebugDetail()
        {
            try
            {
                if (!_running) { return ""; }
                var sb = new System.Text.StringBuilder();
                // The voice gate (round 24): whether Silero is judging this stream, and how much of it it calls a voice.
                int chunks = _vadChunks, speech = _vadSpeechChunks;
                sb.Append("    voice gate: ").Append(!VadGate ? "off (test switch)"
                        : VadActive ? "Silero · " + (chunks > 0 ? (100 * speech / chunks) + "% of " + chunks + " pieces a voice" : "no audio yet")
                                      + (_vadSpeech ? " · VOICE now" : " · no voice now")
                        : _rate != SileroVad.SampleRate ? "open — the stream is " + _rate + " Hz, not Silero's 16 kHz"
                        : "open — Silero not loaded").AppendLine();
                // The timbre (round 25): whether it is being trusted, and why not when it is not.
                double snr = VoiceOverBackgroundDb;
                sb.Append("    timbre: ").Append(!UseTimbre ? "off (test switch)"
                        : TimbreTrusted ? "matching voices by their sound" + (snr < 98 ? " · voices " + snr.ToString("0") + " dB over what is under them" : "")
                        : "pitch only for now — the voices are " + snr.ToString("0") + " dB over the sound under them (needs "
                          + TimbreTrustedSnrDb.ToString("0") + ")")
                  .Append(" · ").Append(_midSpeechChanges).Append(" change(s) found mid-speech").AppendLine();
                // The voice fingerprints (round 34): whether they are being taken, what they decided, the latest verdict.
                sb.Append("    voice prints: ");
                if (!UsePrints) { sb.Append("off (test switch)"); }
                else if (_voicePrint == null) { sb.Append("not loaded — voices matched by pitch and timbre"); }
                else if (_rate != VoicePrint.SampleRate) { sb.Append("not used — the stream is " + _rate + " Hz, the model needs 16 kHz"); }
                else
                {
                    sb.Append(_printsTaken).Append(" taken (").Append(LastPrintMs).Append(" ms the last)")
                      .Append(" · ").Append(_printMints).Append(" voice(s) minted, ").Append(_printSplits).Append(" split(s), ")
                      .Append(_printCorrections).Append(" correction(s), ").Append(_printVetoes).Append(" veto(es)");
                    string verdict = _lastPrintVerdict;
                    if (verdict != null) { sb.Append(" · last: ").Append(verdict); }
                }
                sb.AppendLine();
                for (int i = 0; i < _profiles.Count && i < MaxProfiles; i++)
                {
                    Profile p = _profiles[i];
                    if (p == null) { continue; }
                    sb.Append("    voice ").Append(i + 1)
                      .Append(_currentSpeaker == i + 1 ? " ● SPEAKING" : "          ")
                      .Append(" · pitch ").Append((int)p.Pitch).Append(" Hz")
                      .Append(" · brightness ").Append(p.Bright.ToString("0.00"))
                      .Append(" · intonation ±").Append((int)p.Spread).Append(" Hz")
                      .Append(" · evidence ").Append(p.Weight)
                      .Append(HasTimbre(p) ? " · timbre from " + p.TimbreN + " frames" : " · no timbre yet");
                    // How far apart the voices sound: the nearest other voice's timbre distance (same voice ~0.1, people 0.5+).
                    if (HasTimbre(p))
                    {
                        double nearest = double.MaxValue;
                        for (int j = 0; j < _profiles.Count; j++)
                        {
                            if (j != i && HasTimbre(_profiles[j])) { nearest = Math.Min(nearest, TimbreDistance(p.Timbre, _profiles[j].Timbre)); }
                        }
                        if (nearest < double.MaxValue) { sb.Append(" (nearest other ").Append(nearest.ToString("0.00")).Append(")"); }
                    }
                    // Its print: how many looks, and the most alike other voice by print (people ≤ 0.35, one voice ≥ 0.5).
                    if (p.Print != null)
                    {
                        sb.Append(" · print from ").Append(p.PrintN).Append(p.PrintN == 1 ? " look" : " looks");
                        float closest = -2f;
                        for (int j = 0; j < _profiles.Count; j++)
                        {
                            if (j != i && _profiles[j].Print != null) { closest = Math.Max(closest, VoicePrint.Cosine(p.Print, _profiles[j].Print)); }
                        }
                        if (closest > -2f) { sb.Append(" (most alike other ").Append(closest.ToString("0.00")).Append(")"); }
                    }
                    sb.AppendLine();
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        private double MedianPitch()
        {
            var s = new List<double>(_segPitch);
            s.Sort();
            return s[s.Count / 2];
        }

        /// <summary>
        /// Interquartile spread of the segment's pitch - how much the voice MOVES.
        /// A monotone reader and an animated talker can share the same median pitch
        /// yet have very different spreads, so this separates voices that the median
        /// alone confuses. Robust to octave-error outliers by construction (p75-p25).
        /// </summary>
        private double PitchSpread()
        {
            var s = new List<double>(_segPitch);
            s.Sort();
            return s[(s.Count * 3) / 4] - s[s.Count / 4];
        }

        /// <summary>
        /// True when a segment's pitch spread is compatible with a profile's - a very
        /// loose gate (monotone vs expressive voices differ several-fold) that only
        /// vetoes wildly different intonation. New profiles (little weight) accept
        /// anything; so do near-zero spreads, where the estimate is too thin to judge.
        /// </summary>
        private static bool SpreadCompatible(double spread, Profile p)
        {
            if (p.Weight < 3 || p.Spread < 8 || spread < 8)
            {
                return true;
            }
            double ratio = spread > p.Spread ? spread / p.Spread : p.Spread / spread;
            return ratio < 3.5;
        }

        /// <summary>Folds one segment's stats into an existing profile (running mean).</summary>
        private void UpdateProfile(int idx, double pitch, double bright, double spread)
        {
            Profile p = _profiles[idx];
            int w = Math.Min(p.Weight, 30);   // cap so an established voice stays stable
            p.Pitch = (p.Pitch * w + pitch) / (w + 1);
            p.Bright = (p.Bright * w + bright) / (w + 1);
            p.Spread = (p.Spread * w + spread) / (w + 1);
            p.Weight++;
        }

        /// <summary>
        /// Matches a voice fingerprint to a known speaker, or — when
        /// <paramref name="allowNew"/> permits — registers a new one. Returns the
        /// 1-based speaker number, or 0 when nothing matches and a new speaker isn't
        /// allowed yet (caller retries with more evidence).
        ///
        /// With <paramref name="confidentOnly"/> (an early look, fewer frames than usual) only a
        /// clear-cut match is accepted: one known voice very close, and no other anywhere near.
        ///
        /// A match no longer folds the part-heard segment into the profile. The segment is folded
        /// in once, whole, when it ends — it used to be counted twice, and the early, least reliable
        /// half of it went in straight away, so one misnamed opening pulled a voice toward someone else.
        /// </summary>
        private int Classify(double pitch, double bright, double spread, bool allowNew = true, bool confidentOnly = false,
                             int frames = MinFramesToClassify, double[] timbre = null, int timbreN = 0)
        {
            if (pitch <= 0) { return 0; }

            // By timbre first, wherever there is one to compare (round 25); the pitch rules below are for the case with none.
            if (UseTimbre && timbre != null && timbreN >= FastClassifyFrames)
            {
                int byTimbre = ClassifyByTimbre(pitch, bright, spread, allowNew, confidentOnly, timbre, timbreN);
                if (byTimbre != NoFingerprints) { return byTimbre; }
            }

            // Every known voice's distance, in octaves, with the brightness veto applied.
            int bestIdx = -1, secondIdx = -1;
            double bestD = double.MaxValue, secondD = double.MaxValue;
            for (int i = 0; i < _profiles.Count; i++)
            {
                Profile p = _profiles[i];
                if (Math.Abs(bright - p.Bright) >= BrightVeto || !SpreadCompatible(spread, p)) { continue; }
                // Brightness as a light tiebreak: a tenth of an octave per 0.05 of ZCR.
                double d = Math.Abs(Octaves(pitch, p.Pitch)) + 0.1 * Math.Abs(bright - p.Bright) / 0.05;
                if (d < bestD) { secondD = bestD; secondIdx = bestIdx; bestD = d; bestIdx = i; }
                else if (d < secondD) { secondD = d; secondIdx = i; }
            }

            if (confidentOnly)
            {
                // The fewer the frames, the further away the next-closest voice must be: 5–7 frames
                // name a voice only when no other is within ~0.4 octave (a man and a woman), 8–11
                // when the gap is 0.2.
                double gap = frames < 8 ? 0.40 : 0.20;
                bool clear = bestIdx >= 0 && bestD < ConfidentOctaves
                             && (secondIdx < 0 || secondD - bestD > gap);
                return clear ? bestIdx + 1 : 0;
            }

            // Continuity first: people don't usually swap mid-conversation, and one
            // voice naturally drifts (intonation, effort, distance from the mic). If
            // the fingerprint still roughly fits the CURRENT speaker, keep them —
            // this is what stops "the same person suddenly became Speaker 2". The
            // allowance was 1.6× the strict one (±35% pitch): wide enough that a male
            // voice's octave-misread opening frames passed as the female voice before it.
            int cur = _currentSpeaker - 1;
            if (cur >= 0 && cur < _profiles.Count)
            {
                Profile c = _profiles[cur];
                if (Math.Abs(Octaves(pitch, c.Pitch)) < ContinuityOctaves
                    && Math.Abs(bright - c.Bright) < BrightVeto && SpreadCompatible(spread, c))
                {
                    return cur + 1;
                }
            }

            if (bestIdx >= 0 && Math.Abs(Octaves(pitch, _profiles[bestIdx].Pitch)) < StrictOctaves)
            {
                return bestIdx + 1;
            }

            // Near-miss pass: before inventing a brand-new speaker, accept an existing
            // profile a little further out. A genuinely new voice (male vs female is
            // about an octave) still misses, but one person's sentence-to-sentence drift
            // lands here — the backstop against "the same person became Speaker 3".
            if (bestIdx >= 0 && Math.Abs(Octaves(pitch, _profiles[bestIdx].Pitch)) < LooseOctaves)
            {
                return bestIdx + 1;
            }

            if (!allowNew)
            {
                return 0;                 // not enough evidence to mint a new voice yet
            }
            if (_profiles.Count >= MaxProfiles)
            {
                // Table full: reuse the closest instead of inventing Speaker 47.
                // Counted, because it is otherwise silent: a genuinely new voice now
                // shares an existing number and pulls that profile toward itself.
                _forcedMerges++;
                int closest = 0;
                double closestD = double.MaxValue;
                for (int i = 0; i < _profiles.Count; i++)
                {
                    double d = Math.Abs(Octaves(pitch, _profiles[i].Pitch));
                    if (d < closestD) { closestD = d; closest = i; }
                }
                return closest + 1;
            }
            _profiles.Add(new Profile
            {
                Pitch = pitch, Bright = bright, Spread = spread, Weight = 1,
                Timbre = UseTimbre && timbre != null ? (double[])timbre.Clone() : null,
                TimbreN = UseTimbre && timbre != null ? timbreN : 0,
            });
            if (UseTimbre && timbre != null) { _segTimbreFolded = _segTimbre.Count; }
            return _profiles.Count;   // 1-based
        }

        private const int NoFingerprints = int.MinValue;

        /// <summary>
        /// The pitch matcher's own verdict, for the timbre classifier's second opinion: the nearest voice by pitch (with the
        /// brightness veto), 1-based, when it is clear-cut — under the confident bar with the next voice far off for an
        /// early look (<paramref name="early"/>), under the strict bar otherwise — or 0.
        /// </summary>
        private int PitchVerdict(double pitch, double bright, double spread, int frames, bool early)
        {
            int bestIdx = -1, secondIdx = -1;
            double bestD = double.MaxValue, secondD = double.MaxValue;
            for (int i = 0; i < _profiles.Count; i++)
            {
                Profile p = _profiles[i];
                if (Math.Abs(bright - p.Bright) >= BrightVeto || !SpreadCompatible(spread, p)) { continue; }
                double d = Math.Abs(Octaves(pitch, p.Pitch)) + 0.1 * Math.Abs(bright - p.Bright) / 0.05;
                if (d < bestD) { secondD = bestD; secondIdx = bestIdx; bestD = d; bestIdx = i; }
                else if (d < secondD) { secondD = d; secondIdx = i; }
            }
            if (bestIdx < 0) { return 0; }
            if (early)
            {
                double gap = frames < 8 ? 0.40 : 0.20;
                return bestD < ConfidentOctaves && (secondIdx < 0 || secondD - bestD > gap) ? bestIdx + 1 : 0;
            }
            return Math.Abs(Octaves(pitch, _profiles[bestIdx].Pitch)) < StrictOctaves
                   && (secondIdx < 0 || secondD - bestD > 0.15) ? bestIdx + 1 : 0;
        }

        /// <summary>
        /// Classify by the voice's timbre and pitch together (VoiceScore), for voices that have a fingerprint; returns
        /// <see cref="NoFingerprints"/> when none of the known voices has one (the pitch rules decide then).
        ///  · An early look (under 12 voiced frames) names only a voice that is plainly this one, with every other far off.
        ///  · Continuity: the person who was talking keeps the stretch while it still sounds like them, unless another known
        ///    voice fits clearly better — "people don't usually swap mid-conversation", now judged on the sound of the voice,
        ///    not its pitch, which is exactly what moves when one person gets excited.
        ///  · Otherwise the nearest voice when it is plainly that voice; between "plainly this" and "plainly another", wait
        ///    for more voicing (a long stretch takes the nearest); a new voice only when it is plainly none of them.
        /// </summary>
        private int ClassifyByTimbre(double pitch, double bright, double spread, bool allowNew, bool confidentOnly,
                                     double[] timbre, int n)
        {
            int best = -1, second = -1;
            double bestS = double.MaxValue, secondS = double.MaxValue;
            for (int i = 0; i < _profiles.Count; i++)
            {
                if (!HasTimbre(_profiles[i])) { continue; }
                double s = VoiceScore(_profiles[i], pitch, timbre);
                if (s < bestS) { secondS = bestS; second = best; bestS = s; best = i; }
                else if (s < secondS) { secondS = s; second = i; }
            }
            if (best < 0)
            {
                // No voice with a fingerprint: the session's first voice (the pitch rules know no more), else theirs.
                if (_profiles.Count > 0) { return NoFingerprints; }
                if (!allowNew || n < MinFramesForNewSpeaker) { return 0; }
                return MintVoice(pitch, bright, spread, timbre, n);
            }
            // A voice learned while the timbre could not be trusted has none: by pitch, before anyone new is minted.
            int pitchOnly = -1;
            double pitchOnlyD = double.MaxValue;
            for (int i = 0; i < _profiles.Count; i++)
            {
                if (HasTimbre(_profiles[i])) { continue; }
                double d = Math.Abs(Octaves(pitch, _profiles[i].Pitch));
                if (d < StrictOctaves && d < pitchOnlyD) { pitchOnlyD = d; pitchOnly = i; }
            }
            double same = BarFor(n), differ = same + 0.15;
            if (confidentOnly)
            {
                bool clear = bestS <= same - 0.05 && (second < 0 || secondS - bestS >= 0.25);
                if (clear) { return best + 1; }
                // A few frames' timbre is noisy; the pitch matcher's quick naming is sure sooner for voices far apart — and
                // now it only names the voice the timbre ranks first, near its bar (it named 0.2–0.3 s sooner per turn).
                int quick = PitchVerdict(pitch, bright, spread, n, early: true);
                return quick == best + 1 && bestS <= same + 0.10 ? best + 1 : 0;
            }
            // Continuity breaks near-ties only. A new stretch starts after a pause, which is exactly where people take turns:
            // the person who was talking used to keep it unless another voice scored 0.12 better, and a man answering
            // another man (the closest pair of voices, 0.54 apart) was filed under the first for two seconds (voiceflip).
            int cur = _currentSpeaker - 1;
            if (cur >= 0 && cur < _profiles.Count && cur != best && HasTimbre(_profiles[cur]))
            {
                double sc = VoiceScore(_profiles[cur], pitch, timbre);
                if (sc <= same && sc - bestS < 0.04) { return cur + 1; }
            }
            if (bestS <= same) { return best + 1; }
            // Between "plainly this voice" and "plainly another", more voicing decides (a long stretch takes the nearest).
            // Letting the pitch matcher settle it merged David and Mark (95 and 141 Hz) under music: 33 % named wrong.
            if (bestS <= differ) { return n >= 60 ? best + 1 : 0; }
            if (pitchOnly >= 0) { return pitchOnly + 1; }
            // A new voice from about a second of voicing, not two-thirds of one: a sentence's last words, dropped low and
            // creaky, sound unlike their speaker for that long, and minted a third voice at the end of a turn.
            if (!allowNew || n < (int)Math.Round(NewVoiceTimbreFrames * NewVoiceFactor)) { return 0; }
            return MintVoice(pitch, bright, spread, timbre, n);
        }

        internal static int NewVoiceTimbreFrames = 45;

        /// <summary>A new voice from the segment heard so far (its frames are in the fingerprint already: _segTimbreFolded).</summary>
        private int MintVoice(double pitch, double bright, double spread, double[] timbre, int n)
        {
            if (_profiles.Count >= MaxProfiles)
            {
                // Table full: the nearest voice takes it (counted — two people now share a number).
                _forcedMerges++;
                int closest = 0;
                double closestS = double.MaxValue;
                for (int i = 0; i < _profiles.Count; i++)
                {
                    double s = HasTimbre(_profiles[i]) ? VoiceScore(_profiles[i], pitch, timbre)
                             : Math.Abs(Octaves(pitch, _profiles[i].Pitch));
                    if (s < closestS) { closestS = s; closest = i; }
                }
                return closest + 1;
            }
            _profiles.Add(new Profile
            {
                Pitch = pitch, Bright = bright, Spread = spread, Weight = 1,
                Timbre = (double[])timbre.Clone(), TimbreN = n,
            });
            _segTimbreFolded = _segTimbre.Count;
            return _profiles.Count;   // 1-based
        }
    }
}
