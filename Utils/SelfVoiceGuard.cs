using System;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Tells the caption engine when the audio it is hearing is the USER'S OWN
    /// VOICE coming back through the speakers — mic sidetone, Windows' "Listen to
    /// this device", a voice-chat app monitoring the mic — so those chunks can be
    /// skipped instead of being captioned as another speaker ("it heard me").
    ///
    /// How: a lightweight microphone monitor (no transcription, nothing stored,
    /// nothing leaves the PC — the mic-in-use indicator will show, honestly) keeps
    /// a rolling LOUDNESS ENVELOPE of the mic at 20 ms resolution. When a caption
    /// chunk's system-audio envelope CORRELATES with the mic envelope over the
    /// same wall-clock span (scanning a small ± lag for path latency), that chunk
    /// is the user's own monitored voice. Correlation is the load-bearing part:
    /// the OTHER person in a call is loud while the user's mic is quiet, so their
    /// envelopes don't match and their words are never dropped — a plain
    /// "mic is hot → skip" gate would eat them.
    ///
    /// Everything is best-effort: no microphone, a failed capture, a device change
    /// — the guard just reports inactive and captions behave exactly as before.
    /// </summary>
    public sealed class SelfVoiceGuard : IDisposable
    {
        private const int FrameMs = 20;               // envelope resolution
        private const int RingSize = 512;             // ~10 s of history
        private const int LagScanFrames = 8;          // ±160 ms path-latency skew
        private const double HotFloorMul = 3.5;       // hot = clearly above the noise floor
        private const float HotAbsFloor = 0.0035f;

        private WasapiCapture _capture;
        private volatile bool _running;
        private WaveFormat _fmt;

        // Envelope ring: one RMS per 20 ms mic frame, stamped with its END tick.
        // Written only on the capture thread; read from the transcriber thread —
        // torn reads at the write head just cost one frame of accuracy, never crash.
        private readonly double[] _ringRms = new double[RingSize];
        private readonly long[] _ringTick = new long[RingSize];
        private int _ringHead;

        // Partial-frame accumulator (capture buffers don't align to 20 ms).
        private double _accSq;
        private int _accCount;
        private int _samplesPerFrame;

        // Adaptive noise floor: tracks the quietest recent frame, rising slowly so
        // a permanently-noisy mic doesn't leave the floor stuck at an old quiet.
        private double _floor = 1e-4;

        /// <summary>True while the mic monitor is capturing.</summary>
        public bool Running => _running;

        /// <summary>Name of the microphone being monitored, for status/debug.</summary>
        public string DeviceName { get; private set; } = "";

        public bool Start()
        {
            if (_running) { return true; }
            try
            {
                MMDevice dev;
                using (var en = new MMDeviceEnumerator())
                {
                    dev = AudioDeviceSelection.Resolve(en, DataFlow.Capture, out _);
                }
                if (dev == null)
                {
                    Logger.Info("[OwnVoice] no microphone found — own-voice filtering off.");
                    return false;
                }
                DeviceName = dev.FriendlyName ?? "microphone";
                _capture = new WasapiCapture(dev);
                _fmt = _capture.WaveFormat;
                _samplesPerFrame = Math.Max(1, _fmt.SampleRate * FrameMs / 1000);
                _accSq = 0; _accCount = 0;
                Array.Clear(_ringTick, 0, _ringTick.Length);
                _capture.DataAvailable += OnData;
                _capture.RecordingStopped += (s, e) =>
                {
                    // Device unplugged mid-session: the guard simply goes inactive;
                    // captions keep running without own-voice filtering.
                    if (_running && e?.Exception != null)
                    {
                        _running = false;
                        Logger.Info("[OwnVoice] microphone capture stopped (" + e.Exception.Message + ") — filtering off.");
                    }
                };
                _capture.StartRecording();
                _running = true;
                Logger.Info("[OwnVoice] monitoring the microphone envelope (" + DeviceName + ") to filter your own voice from captions.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Info("[OwnVoice] mic monitor unavailable: " + ex.Message);
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            _running = false;
            try
            {
                if (_capture != null)
                {
                    _capture.DataAvailable -= OnData;
                    _capture.StopRecording();
                    _capture.Dispose();
                }
            }
            catch { }
            _capture = null;
        }

        public void Dispose() => Stop();

        private void OnData(object sender, WaveInEventArgs e)
        {
            try
            {
                if (!_running || e.BytesRecorded <= 0 || _fmt == null) { return; }
                int channels = Math.Max(1, _fmt.Channels);
                int sr = Math.Max(8000, _fmt.SampleRate);

                // A polling WASAPI capture delivers ~50 ms of audio in ONE callback, so
                // every frame folded here shares this instant — stamping all of them
                // with the callback's clock clustered the ring into one bucket and made
                // the similarity gate reject EVERY step (the own-voice filter never
                // fired at all). Back-date each frame to its actual sample time so ring
                // entries are spaced ~20 ms apart, matching the transcriber's own
                // stepStartTick convention (the ±lag scan absorbs the fixed offset).
                long now = Environment.TickCount64;

                // Fold every sample into the running 20 ms frame; emit a ring entry
                // whenever a frame's worth has accumulated. Only RMS is kept — no
                // audio is stored anywhere.
                if (_fmt.Encoding == WaveFormatEncoding.IeeeFloat && _fmt.BitsPerSample == 32)
                {
                    int n = e.BytesRecorded / 4;
                    int monoTotal = n / channels, monoDone = 0;
                    for (int i = 0; i < n; i += channels)
                    {
                        float v = BitConverter.ToSingle(e.Buffer, i * 4);
                        Fold(v, now - (long)(monoTotal - ++monoDone) * 1000 / sr);
                    }
                }
                else if (_fmt.Encoding == WaveFormatEncoding.Pcm && _fmt.BitsPerSample == 16)
                {
                    int n = e.BytesRecorded / 2;
                    int monoTotal = n / channels, monoDone = 0;
                    for (int i = 0; i < n; i += channels)
                    {
                        short s = (short)(e.Buffer[i * 2] | (e.Buffer[i * 2 + 1] << 8));
                        Fold(s / 32768f, now - (long)(monoTotal - ++monoDone) * 1000 / sr);
                    }
                }
                else
                {
                    return;                          // exotic format — guard stays quiet
                }
            }
            catch { /* the mic monitor must never destabilise anything */ }
        }

        private void Fold(float v, long tick)
        {
            _accSq += (double)v * v;
            if (++_accCount < _samplesPerFrame)
            {
                return;
            }
            double rms = Math.Sqrt(_accSq / _accCount);
            _accSq = 0; _accCount = 0;

            // Slow-rising minimum tracker for the noise floor.
            if (rms < _floor) { _floor = rms; }
            else { _floor = Math.Min(_floor * 1.005, 0.01); }
            if (_floor < 1e-5) { _floor = 1e-5; }

            int h = _ringHead;
            _ringRms[h] = rms;
            // The tick of this frame's LAST sample — its end time (see OnData).
            _ringTick[h] = tick;
            _ringHead = (h + 1) % RingSize;
        }

        /// <summary>
        /// How strongly the mic envelope matches a system-audio envelope covering
        /// [stepStartTick .. stepStartTick + stepEnv.Length × 20 ms]. Returns the
        /// best normalized cross-correlation over a small ± lag scan (0 when the
        /// mic was quiet, history is missing, or there's too little signal), and
        /// reports whether the mic was actually HOT across that span — both must
        /// hold before a chunk is called "own voice".
        /// </summary>
        public double Similarity(long stepStartTick, double[] stepEnv, out bool micHot)
        {
            micHot = false;
            try
            {
                if (!_running || stepEnv == null || stepEnv.Length < 12)
                {
                    return 0;
                }

                // Pull the mic frames covering the span (plus lag margin) out of the
                // ring into time order. The ring is small; a linear pass is cheap.
                long spanStart = stepStartTick - LagScanFrames * FrameMs;
                long spanEnd = stepStartTick + (long)stepEnv.Length * FrameMs + LagScanFrames * FrameMs;
                int need = (int)((spanEnd - spanStart) / FrameMs) + 2;
                var mic = new double[need];
                var have = new bool[need];
                int found = 0;
                double hotGate = Math.Max(_floor * HotFloorMul, HotAbsFloor);
                int hotInSpan = 0, inSpan = 0;
                for (int i = 0; i < RingSize; i++)
                {
                    long t = _ringTick[i];
                    if (t < spanStart || t >= spanEnd) { continue; }
                    int idx = (int)((t - spanStart) / FrameMs);
                    if (idx < 0 || idx >= need) { continue; }
                    mic[idx] = _ringRms[i];
                    if (!have[idx]) { have[idx] = true; found++; }
                    // Hot fraction counted over the actual step span only.
                    if (t >= stepStartTick && t < stepStartTick + (long)stepEnv.Length * FrameMs)
                    {
                        inSpan++;
                        if (_ringRms[i] > hotGate) { hotInSpan++; }
                    }
                }
                if (found < need / 2 || inSpan < 5)
                {
                    return 0;                        // not enough mic history for a verdict
                }
                micHot = hotInSpan >= inSpan * 0.35;
                if (!micHot)
                {
                    return 0;                        // user isn't speaking — can't be their voice
                }

                // Fill small ring gaps by carrying the previous value forward, so a
                // missing frame doesn't zero the correlation.
                for (int i = 1; i < need; i++)
                {
                    if (!have[i]) { mic[i] = mic[i - 1]; }
                }

                // Normalized cross-correlation over the lag scan; envelopes are
                // mean-subtracted so a merely-loud mic can't fake a match.
                double best = 0;
                for (int lag = -LagScanFrames; lag <= LagScanFrames; lag++)
                {
                    int off = LagScanFrames + lag;
                    double sumA = 0, sumB = 0;
                    int n = stepEnv.Length;
                    for (int i = 0; i < n; i++) { sumA += stepEnv[i]; sumB += mic[off + i]; }
                    double meanA = sumA / n, meanB = sumB / n;
                    double dot = 0, na = 0, nb = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double a = stepEnv[i] - meanA, b = mic[off + i] - meanB;
                        dot += a * b; na += a * a; nb += b * b;
                    }
                    if (na <= 1e-12 || nb <= 1e-12) { continue; }
                    double c = dot / Math.Sqrt(na * nb);
                    if (c > best) { best = c; }
                }
                return best;
            }
            catch
            {
                micHot = false;
                return 0;
            }
        }
    }
}
