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
    /// This is deliberately lightweight, on-device signal processing (no model
    /// download, negligible CPU): for each stretch of speech it measures the voice's
    /// pitch (fundamental frequency via autocorrelation) and brightness (zero-crossing
    /// rate), then matches that fingerprint against the speakers heard so far. Two
    /// people with very similar voices can still be confused — that's the honest
    /// limit of pitch-based profiling — but distinct voices (most conversations,
    /// interviews, let's-plays) separate well.
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

        private WasapiLoopbackCapture _capture;
        private volatile bool _running;
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

        /// <summary>
        /// Classifies what's playing right now: speech (confident human pitch), other
        /// sound (music, effects — energy without stable pitch), or quiet.
        /// </summary>
        public AudioKind CurrentAudioKind
        {
            get
            {
                if (!_running || _activeEma < 0.2f)
                {
                    return AudioKind.Quiet;
                }
                return _voicedEma > 0.4f ? AudioKind.Speech : AudioKind.Sound;
            }
        }

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
        }
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
                    _capture = dev != null ? new WasapiLoopbackCapture(dev) : new WasapiLoopbackCapture();
                    _captureOpens++;
                }
                int deviceRate = _capture.WaveFormat.SampleRate;
                _decim = Math.Max(1, deviceRate / 16000);
                _rate = deviceRate / _decim;
                _capture.DataAvailable += OnData;
                _capture.RecordingStopped += (s, e) =>
                {
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

                Logger.Info("[Voice] profiler listening (" + deviceRate + " Hz).");
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
            // A fresh session starts with fresh voices — old profiles would otherwise
            // mislabel a completely different video's speakers.
            _profiles.Clear();
            _pending.Clear();
            _segPitch.Clear();
            _segZcr.Clear();
            _segZcrSum = 0; _segFrames = 0; _segClassified = false; _silenceRun = 0;
            _winCount = 0; _winHead = 0; _sinceDriftCheck = 0; _driftStrikes = 0; _pauses.Clear();
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
            string target = AudioDeviceSelection.ResolveId(DataFlow.Render);
            string open = _deviceId;
            if (_capture != null && target != null && open != null &&
                string.Equals(target, open, StringComparison.OrdinalIgnoreCase))
            {
                return;            // already listening to the right device
            }
            _running = false;      // suppress the auto-restart branch during swap
            Cleanup();
            Start();               // reopens on the resolved device; profiles are kept
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
                    _segPitch.Clear();
                    _segZcr.Clear();
                    _segZcrSum = 0; _segFrames = 0;
                    _segClassified = false; _segProfileIdx = -1;
                    _winCount = 0; _winHead = 0; _sinceDriftCheck = 0; _driftStrikes = 0; _pauses.Clear();
                    // Also here, on the thread that writes it: a zero set by the caller
                    // can be overwritten by a callback that was already mid-classify.
                    _currentSpeaker = 0;
                    _activeSegmentSpeaker = 0;
                }

                WaveFormat fmt = _capture.WaveFormat;
                int channels = Math.Max(1, fmt.Channels);

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

                // Consume in ~43 ms windows hopped by half.
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
            catch
            {
                // Never let an audio glitch kill the capture callback.
            }
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

            if (rms < VoiceRms)
            {
                _activeEma *= 0.95f;                       // audio going quiet
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

            // The frame as an array: YIN below reads every sample a few hundred times, and a List
            // indexer on that path is measurably slower than an array.
            if (_frame == null || _frame.Length < win) { _frame = new float[win]; }
            for (int i = 0; i < win; i++) { _frame[i] = buf[i]; }

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
            bool voiced = f0 > 0 && aperiodicity < YinVoiced;
            if (voiced)
            {
                _segPitch.Add(f0);   // confident, voiced frame
                _segZcr.Add(zcr);
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
                bool allowNew = n >= MinFramesForNewSpeaker;
                if (allowNew && SegmentSpreadOctaves() > NewVoiceMaxSpreadOctaves)
                {
                    // Two voices' worth of pitches: not a new person, a stretch straddling a change.
                    // Keep only the newest frames — where the voice speaking now is — and look again.
                    allowNew = false;
                    TrimSegmentToWindow();
                    n = _segPitch.Count;
                }
                int idx = Classify(MedianPitch(), _segZcrSum / Math.Max(1, n), PitchSpread(),
                                   allowNew: allowNew,
                                   confidentOnly: n < MinFramesToClassify,
                                   frames: n);
                if (idx > 0)
                {
                    _segClassified = true;
                    _segProfileIdx = idx - 1;
                    _currentSpeaker = idx;
                    _activeSegmentSpeaker = idx;
                    _sinceDriftCheck = 0;
                    _driftStrikes = 0;
                }
            }
            else if (_segClassified && voiced && ++_sinceDriftCheck >= DriftCheckEvery && _winCount >= WindowFrames)
            {
                _sinceDriftCheck = 0;
                CheckForSpeakerChange();
            }
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
        /// Makes the segment's voiced frames from <paramref name="from"/> onward the whole segment, with
        /// the window holding its newest ones; everything before is dropped.
        /// </summary>
        private void KeepSegmentFrom(int from)
        {
            from = Math.Max(0, Math.Min(from, _segPitch.Count));
            if (from > 0)
            {
                _segPitch.RemoveRange(0, from);
                _segZcr.RemoveRange(0, Math.Min(from, _segZcr.Count));
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
            }
            _segPitch.Clear();
            _segZcr.Clear();
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
                for (int i = 0; i < _profiles.Count && i < MaxProfiles; i++)
                {
                    Profile p = _profiles[i];
                    if (p == null) { continue; }
                    sb.Append("    voice ").Append(i + 1)
                      .Append(_currentSpeaker == i + 1 ? " ● SPEAKING" : "          ")
                      .Append(" · pitch ").Append((int)p.Pitch).Append(" Hz")
                      .Append(" · brightness ").Append(p.Bright.ToString("0.00"))
                      .Append(" · intonation ±").Append((int)p.Spread).Append(" Hz")
                      .Append(" · evidence ").Append(p.Weight).AppendLine();
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
                             int frames = MinFramesToClassify)
        {
            if (pitch <= 0) { return 0; }

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
            _profiles.Add(new Profile { Pitch = pitch, Bright = bright, Spread = spread, Weight = 1 });
            return _profiles.Count;   // 1-based
        }
    }
}
