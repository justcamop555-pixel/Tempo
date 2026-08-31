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
        private const float VoiceRms = 0.0035f;      // frame louder than this = someone speaking
        private const double SilenceEndsSegmentSec = 0.9;
        private const int MinFramesToClassify = 12;  // ~0.75s of voiced audio before naming a speaker
        // Creating a brand-NEW speaker needs more evidence than matching a known one:
        // a short cough/effect must not mint "Speaker 3".
        // Frames of consistent NON-match before a brand-new speaker profile may be
        // created. Raised 20 → 30 (~1.05 s): music or effects mixed under one
        // narrator's voice could shift the features long enough to mint a phantom
        // "Speaker 2" for the same person — a real speaker change still classifies
        // fast via matching, and turn-taking pauses give plenty of frames anyway.
        private const int MinFramesForNewSpeaker = 30;
        private const int MaxProfiles = 8;
        // Match thresholds: relative pitch difference and absolute brightness difference.
        // Wide enough that one voice's natural variation stays one profile; male vs
        // female voices differ by ~2x in pitch so they still separate cleanly.
        private const double PitchMatch = 0.22;
        private const double BrightMatch = 0.06;

        private WasapiLoopbackCapture _capture;
        private volatile bool _running;
        private volatile int _currentSpeaker;         // 0 = unknown / silence so far
        private volatile bool _forgetRequested;       // see ForgetVoices()

        /// <summary>1-based number of the speaker currently talking; 0 while unknown.</summary>
        public int CurrentSpeaker => _currentSpeaker;

        /// <summary>True once capture started successfully (voice matching available).</summary>
        public bool Running => _running;

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
        private double[] _corr;            // reusable autocorrelation scratch

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
            _disposedFlag = false;   // a fresh session re-arms the device-change recovery
            try
            {
                // Loopback = "what the PC is playing". A PC with no output device
                // simply leaves the profiler off; the labeler still counts turns.
                // Listen through the same speaker the caption engine uses — the
                // user's chosen device when one is picked, Windows' default else.
                using (var en = new NAudio.CoreAudioApi.MMDeviceEnumerator())
                {
                    var dev = AudioDeviceSelection.Resolve(en, NAudio.CoreAudioApi.DataFlow.Render, out _);
                    _capture = dev != null ? new WasapiLoopbackCapture(dev) : new WasapiLoopbackCapture();
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
            Cleanup();
            // A fresh session starts with fresh voices — old profiles would otherwise
            // mislabel a completely different video's speakers.
            _profiles.Clear();
            _pending.Clear();
            _segPitch.Clear();
            _segZcrSum = 0; _segFrames = 0; _segClassified = false; _silenceRun = 0;
        }

        /// <summary>
        /// Re-opens capture on the CURRENT default speaker while keeping every
        /// learned voice. For default-device switches where the old device stays
        /// alive (no RecordingStopped fires) — without this the profiler keeps
        /// listening to the wrong, silent device.
        /// </summary>
        public void FollowDefaultDevice()
        {
            if (!_running || _disposedFlag)
            {
                return;
            }
            _running = false;      // suppress the auto-restart branch during swap
            Cleanup();
            Start();               // reopens on the new default; profiles are kept
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
                    _segZcrSum = 0; _segFrames = 0;
                    _segClassified = false; _segProfileIdx = -1;
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
            _silenceRun = 0;
            _activeEma = _activeEma * 0.95f + 0.05f;       // audio present

            // Brightness: zero-crossing rate.
            int zc = 0;
            for (int i = 1; i < win; i++)
            {
                if ((buf[i - 1] < 0) != (buf[i] < 0))
                {
                    zc++;
                }
            }
            double zcr = zc / (double)win;

            // Pitch: autocorrelation over the human F0 band (60–400 Hz).
            int minLag = Math.Max(2, _rate / 400);
            int maxLag = Math.Min(win - 2, _rate / 60);
            double r0 = sq;
            if (_corr == null || _corr.Length < maxLag + 1)
            {
                _corr = new double[maxLag + 1];
            }
            double best = 0;
            for (int lag = minLag; lag <= maxLag; lag++)
            {
                double r = 0;
                for (int i = 0; i + lag < win; i++)
                {
                    r += buf[i] * buf[i + lag];
                }
                _corr[lag] = r;
                if (r > best)
                {
                    best = r;
                }
            }

            // Octave-safe peak choice. A periodic voice correlates almost equally at
            // its true period T and at 2T, 3T, ... — taking the raw global maximum
            // therefore randomly reads the same voice an octave (or two) LOW, which
            // made one person's pitch land on another's profile ("the same person
            // became Speaker 2"). The fundamental is the SHORTEST near-maximal LOCAL
            // PEAK: requiring a genuine peak skips the high "shoulder" at tiny lags
            // (any smooth signal correlates with a barely-shifted copy of itself),
            // and taking the shortest collapses the 2T/3T subharmonic picks back to T.
            int bestLag = 0;
            for (int lag = minLag + 1; lag < maxLag; lag++)
            {
                if (_corr[lag] >= 0.90 * best &&
                    _corr[lag] >= _corr[lag - 1] && _corr[lag] >= _corr[lag + 1])
                {
                    bestLag = lag;
                    best = _corr[lag];
                    break;
                }
            }

            _segFrames++;
            bool voiced = bestLag > 0 && r0 > 0 && best > 0.45 * r0;
            if (voiced)
            {
                _segPitch.Add(_rate / (double)bestLag);   // confident, voiced frame
                // Brightness from VOICED frames only. Unvoiced frames are dominated
                // by whichever s/sh/f sounds the sentence happens to contain, so an
                // all-frames average made the fingerprint depend on the WORDS more
                // than the voice — the same person then mismatched their own profile
                // on a sibilant-heavy sentence and got a fresh speaker number.
                _segZcrSum += zcr;
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
            if (!_segClassified && _segPitch.Count >= MinFramesToClassify && _voicedEma >= 0.3f)
            {
                bool allowNew = _segPitch.Count >= MinFramesForNewSpeaker;
                int idx = Classify(MedianPitch(), _segZcrSum / _segPitch.Count, PitchSpread(), allowNew);
                if (idx > 0)
                {
                    _segClassified = true;
                    _segProfileIdx = idx - 1;
                    _currentSpeaker = idx;
                }
            }
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
            _segZcrSum = 0;
            _segFrames = 0;
            _segClassified = false;
            _segProfileIdx = -1;
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
        /// </summary>
        private int Classify(double pitch, double bright, double spread, bool allowNew = true)
        {
            // Continuity first: people don't usually swap mid-conversation, and one
            // voice naturally drifts (intonation, effort, distance from the mic). If
            // the fingerprint still roughly fits the CURRENT speaker, keep them —
            // this is what stops "the same person suddenly became Speaker 2" without
            // stopping a genuinely different voice (roughly 2× pitch apart) from
            // registering as new.
            int cur = _currentSpeaker - 1;
            if (cur >= 0 && cur < _profiles.Count)
            {
                Profile c = _profiles[cur];
                double dpc = Math.Abs(pitch - c.Pitch) / Math.Max(60, c.Pitch);
                double dbc = Math.Abs(bright - c.Bright);
                if (dpc < PitchMatch * 1.6 && dbc < BrightMatch * 1.6 && SpreadCompatible(spread, c))
                {
                    UpdateProfile(cur, pitch, bright, spread);
                    return cur + 1;
                }
            }

            int bestIdx = -1;
            double bestScore = double.MaxValue;
            for (int i = 0; i < _profiles.Count; i++)
            {
                Profile p = _profiles[i];
                double dp = Math.Abs(pitch - p.Pitch) / Math.Max(60, p.Pitch);
                double db = Math.Abs(bright - p.Bright);
                if (dp < PitchMatch && db < BrightMatch && SpreadCompatible(spread, p))
                {
                    double score = dp + db * 2.5;
                    if (score < bestScore)
                    {
                        bestScore = score; bestIdx = i;
                    }
                }
            }

            if (bestIdx < 0)
            {
                // Near-miss pass: before inventing a brand-new speaker, accept an
                // existing profile at 1.5× the strict thresholds. A genuinely new
                // voice (male vs female is ~2× in pitch) still misses even the loose
                // bounds, but one person's sentence-to-sentence drift lands here —
                // this is the backstop against "the same person became Speaker 3".
                double looseScore = double.MaxValue;
                for (int i = 0; i < _profiles.Count; i++)
                {
                    Profile p = _profiles[i];
                    double dp = Math.Abs(pitch - p.Pitch) / Math.Max(60, p.Pitch);
                    double db = Math.Abs(bright - p.Bright);
                    if (dp < PitchMatch * 1.5 && db < BrightMatch * 1.5)
                    {
                        double score = dp + db * 2.5;
                        if (score < looseScore)
                        {
                            looseScore = score; bestIdx = i;
                        }
                    }
                }
            }

            if (bestIdx < 0)
            {
                if (!allowNew)
                {
                    return 0;                 // not enough evidence to mint a new voice yet
                }
                if (_profiles.Count >= MaxProfiles)
                {
                    // Table full: reuse the closest instead of inventing Speaker 47.
                    bestIdx = 0;
                    double closest = double.MaxValue;
                    for (int i = 0; i < _profiles.Count; i++)
                    {
                        double d = Math.Abs(pitch - _profiles[i].Pitch);
                        if (d < closest) { closest = d; bestIdx = i; }
                    }
                }
                else
                {
                    _profiles.Add(new Profile { Pitch = pitch, Bright = bright, Spread = spread, Weight = 1 });
                    return _profiles.Count;   // 1-based
                }
            }

            UpdateProfile(bestIdx, pitch, bright, spread);
            return bestIdx + 1;               // 1-based
        }
    }
}
