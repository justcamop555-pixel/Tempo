using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Windows.AI.MachineLearning;

namespace AutoClicker.Utils
{
    /// <summary>
    /// A voice's fingerprint (round 34, 2026-11-16 — the user's "it need hear different voice to help pick same speakers",
    /// and their yes to this download): WeSpeaker's ResNet34 speaker model, trained on VoxCeleb, in the ONNX file the
    /// sherpa-onnx project publishes (wespeaker_en_voxceleb_resnet34_LM.onnx, 25.3 MB; CC BY 4.0, the licence of the
    /// VoxCeleb data it was trained on — see Utils/ThirdPartyNotices.cs). Fetched into the models folder the first time
    /// speaker labels run, kept only when its SHA-256 is the one published, and run on Windows' own machine-learning engine
    /// (Windows.AI.MachineLearning), as the face models are: nothing is added to Tempo.
    ///
    /// Any second or two of one person's voice becomes 256 numbers that point the same way for that person whatever they
    /// say, however excited they get, and apart for anyone else — what pitch and the spectrum's shape (VoiceProfiler's
    /// timbre) could only approximate: an excited voice rises into another person's range, and two men's spectra sit close.
    ///
    /// Features exactly as WeSpeaker computes them for this model (wespeaker/cli/speaker.py, compute_features): Kaldi
    /// filterbank — 80 mel bands from 20 Hz to 8 kHz, 25 ms frames every 10 ms, Hamming window, no dither, DC removed,
    /// pre-emphasis 0.97, power spectrum, natural log — on samples at 16-bit scale (torchaudio.load(normalize=False)), then
    /// each band's mean over the stretch subtracted (cmn=True).
    ///
    /// Not thread-safe: one caller at a time (the voice profiler's capture thread); Dispose may come from any thread.
    /// </summary>
    public sealed class VoicePrint : IDisposable
    {
        internal const string ModelFile = "wespeaker_en_voxceleb_resnet34_LM.onnx";
        internal const string ModelUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/wespeaker_en_voxceleb_resnet34_LM.onnx";
        internal const string ModelSha256 = "e9848563da86f263117134dfd7ad63c92355b37de492b55e325400c9d9c39012";
        internal const long ModelBytes = 26530550;

        /// <summary>The rate the samples given to <see cref="Embed"/> must be at.</summary>
        public const int SampleRate = 16000;

        /// <summary>Numbers in a fingerprint.</summary>
        public const int Dim = 256;

        private const int FrameLen = 400, FrameShift = 160, FftSize = 512, Bins = 80;

        /// <summary>The fewest feature frames <see cref="Embed"/> works on (0.5 s); a shorter stretch gives no fingerprint.</summary>
        public const int MinFrames = 50;

        private readonly object _gate = new object();
        private LearningModelSession _session;
        private string _in, _out;

        // Per-frame work arrays (one caller at a time).
        private readonly double[] _frame = new double[FrameLen];
        private readonly double[] _re = new double[FftSize], _im = new double[FftSize];
        private readonly double[] _power = new double[FftSize / 2 + 1];
        private static readonly double[] Window = MakeHamming();
        private static readonly int[] BinLo;
        private static readonly double[][] BinW;
        private static readonly int[] BitReverse;
        private static readonly double[] TwiddleRe, TwiddleIm;

        /// <summary>Milliseconds the last fingerprint took, features and model together (for Live debug).</summary>
        public int LastMs { get; private set; }

        /// <summary>Fingerprints taken by this instance.</summary>
        public int Taken { get; private set; }

        /// <summary>
        /// For the harnesses: the band means are subtracted (WeSpeaker's recipe, the default). Off = the raw log filterbank,
        /// as sherpa-onnx feeds this file (it reads no normalisation from the file's metadata).
        /// </summary>
        internal static bool MeanNormalise = true;

        static VoicePrint()
        {
            // Kaldi's mel banks (torchaudio.compliance.kaldi.get_mel_banks, vtln off): num_bins + 2 points equally spaced in
            // mel between 20 Hz and the Nyquist, triangles over the FFT bins 0 .. N/2 - 1 (the Nyquist bin weighs nothing).
            const double sr = SampleRate, lowHz = 20, highHz = SampleRate / 2.0;
            int fftBins = FftSize / 2;
            double binWidth = sr / FftSize;
            double melLow = Mel(lowHz), melHigh = Mel(highHz), delta = (melHigh - melLow) / (Bins + 1);
            BinLo = new int[Bins];
            BinW = new double[Bins][];
            for (int b = 0; b < Bins; b++)
            {
                double left = melLow + b * delta, centre = melLow + (b + 1) * delta, right = melLow + (b + 2) * delta;
                int first = -1, last = -1;
                var w = new double[fftBins];
                for (int k = 0; k < fftBins; k++)
                {
                    double mel = Mel(binWidth * k);
                    double up = (mel - left) / (centre - left), down = (right - mel) / (right - centre);
                    double v = Math.Max(0.0, Math.Min(up, down));
                    w[k] = v;
                    if (v > 0) { if (first < 0) { first = k; } last = k; }
                }
                if (first < 0) { first = 0; last = -1; }
                BinLo[b] = first;
                BinW[b] = new double[Math.Max(0, last - first + 1)];
                for (int k = first; k <= last; k++) { BinW[b][k - first] = w[k]; }
            }
            // Radix-2 FFT tables.
            int bits = 0;
            while ((1 << bits) < FftSize) { bits++; }
            BitReverse = new int[FftSize];
            for (int i = 0; i < FftSize; i++)
            {
                int r = 0;
                for (int j = 0; j < bits; j++) { if ((i & (1 << j)) != 0) { r |= 1 << (bits - 1 - j); } }
                BitReverse[i] = r;
            }
            TwiddleRe = new double[FftSize / 2];
            TwiddleIm = new double[FftSize / 2];
            for (int k = 0; k < FftSize / 2; k++)
            {
                TwiddleRe[k] = Math.Cos(-2 * Math.PI * k / FftSize);
                TwiddleIm[k] = Math.Sin(-2 * Math.PI * k / FftSize);
            }
        }

        private static double Mel(double hz) => 1127.0 * Math.Log(1.0 + hz / 700.0);

        private static double[] MakeHamming()
        {
            // torch.hamming_window(400, periodic=False): 0.54 - 0.46 cos(2 pi n / (N - 1)).
            var w = new double[FrameLen];
            for (int i = 0; i < FrameLen; i++) { w[i] = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (FrameLen - 1)); }
            return w;
        }

        private VoicePrint() { }

        /// <summary>Where the model lives: the models folder, beside the speech and face models.</summary>
        internal static string ModelPath() => Path.Combine(WhisperModelManager.GetModelsDirectory(), ModelFile);

        /// <summary>True when the model file is in the models folder at its published size.</summary>
        public static bool ModelPresent()
        {
            try { var fi = new FileInfo(ModelPath()); return fi.Exists && fi.Length == ModelBytes; }
            catch { return false; }
        }

        /// <summary>The fingerprint model from the models folder; null — with <paramref name="why"/> — when it is not there or will not run.</summary>
        public static VoicePrint TryLoad(out string why) => TryLoad(ModelPath(), out why);

        /// <summary>From a given file (the harnesses).</summary>
        internal static VoicePrint TryLoad(string path, out string why)
        {
            // The Nemotron engine's ONNX runtime goes in first: Windows' in-box one in the process first made it crash
            // (NemotronStreaming.Prepare).
            NemotronStreaming.Prepare();
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length != ModelBytes) { why = "voice fingerprints: model not downloaded yet"; return null; }
                LearningModel model = LearningModel.LoadFromFilePath(path);
                var vp = new VoicePrint();
                foreach (ILearningModelFeatureDescriptor f in model.InputFeatures) { vp._in = f.Name; break; }
                foreach (ILearningModelFeatureDescriptor f in model.OutputFeatures) { vp._out = f.Name; break; }
                var device = new LearningModelDevice(LearningModelDeviceKind.Cpu);
                try
                {
                    // Windows drops its own copy of the weights once the session holds them (10 2004 and later).
                    vp._session = new LearningModelSession(model, device, new LearningModelSessionOptions { CloseModelOnSessionCreation = true });
                }
                catch
                {
                    vp._session = new LearningModelSession(model, device);
                }
                why = null;
                return vp;
            }
            catch (Exception ex)
            {
                why = "voice fingerprints unavailable: Windows' machine-learning engine could not run the model (" + ex.Message.Trim() + ")";
                return null;
            }
        }

        /// <summary>
        /// The fingerprint of <paramref name="count"/> samples (16 kHz, ±1 scale) from <paramref name="offset"/>: 256 numbers
        /// of length 1, or null when the stretch is under half a second or the model fails.
        /// </summary>
        public float[] Embed(float[] samples, int offset, int count)
        {
            if (samples == null || offset < 0 || count <= 0 || offset + count > samples.Length) { return null; }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            float[] feats = Features(samples, offset, count, out int frames);
            if (feats == null || frames < MinFrames) { return null; }
            lock (_gate)
            {
                if (_session == null) { return null; }
                try
                {
                    var binding = new LearningModelBinding(_session);
                    binding.Bind(_in, TensorFloat.CreateFromArray(new long[] { 1, frames, Bins }, feats));
                    LearningModelEvaluationResult result = _session.Evaluate(binding, "voiceprint");
                    if (!result.Succeeded || !result.Outputs.TryGetValue(_out, out object value) || !(value is TensorFloat t)) { return null; }
                    var view = t.GetAsVectorView();
                    if (view.Count < Dim) { return null; }
                    var e = new float[Dim];
                    double sq = 0;
                    for (int i = 0; i < Dim; i++) { e[i] = view[i]; sq += e[i] * (double)e[i]; }
                    if (sq <= 0 || double.IsNaN(sq)) { return null; }
                    float inv = (float)(1.0 / Math.Sqrt(sq));
                    for (int i = 0; i < Dim; i++) { e[i] *= inv; }
                    Taken++;
                    LastMs = (int)clock.ElapsedMilliseconds;
                    return e;
                }
                catch { return null; }
            }
        }

        /// <summary>
        /// The Kaldi filterbank of the stretch (frames × 80, row by row; snip_edges: whole frames only), band means
        /// subtracted (see <see cref="MeanNormalise"/>); null under one frame.
        /// </summary>
        internal float[] Features(float[] s, int offset, int count, out int frames)
        {
            frames = count >= FrameLen ? 1 + (count - FrameLen) / FrameShift : 0;
            if (frames <= 0) { return null; }
            var feats = new float[frames * Bins];
            for (int f = 0; f < frames; f++)
            {
                int start = offset + f * FrameShift;
                double mean = 0;
                for (int i = 0; i < FrameLen; i++) { double v = s[start + i] * 32768.0; _frame[i] = v; mean += v; }
                mean /= FrameLen;
                for (int i = 0; i < FrameLen; i++) { _frame[i] -= mean; }                       // remove_dc_offset
                for (int i = FrameLen - 1; i > 0; i--) { _frame[i] -= 0.97 * _frame[i - 1]; }   // preemphasis 0.97
                _frame[0] -= 0.97 * _frame[0];
                for (int i = 0; i < FrameLen; i++) { _re[i] = _frame[i] * Window[i]; _im[i] = 0; }
                for (int i = FrameLen; i < FftSize; i++) { _re[i] = 0; _im[i] = 0; }
                Fft(_re, _im);
                for (int k = 0; k <= FftSize / 2; k++) { _power[k] = _re[k] * _re[k] + _im[k] * _im[k]; }
                int row = f * Bins;
                for (int b = 0; b < Bins; b++)
                {
                    double e = 0;
                    double[] w = BinW[b];
                    int lo = BinLo[b];
                    for (int j = 0; j < w.Length; j++) { e += w[j] * _power[lo + j]; }
                    feats[row + b] = (float)Math.Log(Math.Max(e, 1.1920928955078125e-07));   // torch.finfo(float).eps
                }
            }
            if (MeanNormalise)
            {
                for (int b = 0; b < Bins; b++)
                {
                    double m = 0;
                    for (int f = 0; f < frames; f++) { m += feats[f * Bins + b]; }
                    float mf = (float)(m / frames);
                    for (int f = 0; f < frames; f++) { feats[f * Bins + b] -= mf; }
                }
            }
            return feats;
        }

        /// <summary>In-place radix-2 FFT of FftSize points.</summary>
        private static void Fft(double[] re, double[] im)
        {
            int n = FftSize;
            for (int i = 0; i < n; i++)
            {
                int j = BitReverse[i];
                if (j > i)
                {
                    double t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            for (int size = 2; size <= n; size <<= 1)
            {
                int half = size >> 1, step = n / size;
                for (int start = 0; start < n; start += size)
                {
                    for (int k = 0; k < half; k++)
                    {
                        double wr = TwiddleRe[k * step], wi = TwiddleIm[k * step];
                        int a = start + k, b = a + half;
                        double xr = re[b] * wr - im[b] * wi, xi = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi;
                        re[a] += xr; im[a] += xi;
                    }
                }
            }
        }

        /// <summary>Cosine of two unit fingerprints (1 = the same direction); NaN when either is missing.</summary>
        public static float Cosine(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length) { return float.NaN; }
            double dot = 0;
            for (int i = 0; i < a.Length; i++) { dot += a[i] * (double)b[i]; }
            return (float)dot;
        }

        /// <summary>
        /// A running fingerprint with one more folded in (mean of unit vectors, renormalised); at most
        /// <paramref name="cap"/> earlier ones keep their full say, so a voice can still settle.
        /// </summary>
        public static float[] Fold(float[] mean, int count, float[] add, int cap = 30)
        {
            if (add == null) { return mean; }
            if (mean == null || count <= 0) { return (float[])add.Clone(); }
            int n = Math.Min(count, cap);
            var m = new float[add.Length];
            double sq = 0;
            for (int i = 0; i < m.Length; i++) { m[i] = (mean[i] * n + add[i]) / (n + 1); sq += m[i] * (double)m[i]; }
            float inv = sq > 0 ? (float)(1.0 / Math.Sqrt(sq)) : 0f;
            for (int i = 0; i < m.Length; i++) { m[i] *= inv; }
            return m;
        }

        // ── The model's first-use download ──────────────────────────────────────

        private static int _downloading;
        private static long _downloadedBytes;

        /// <summary>True while the model is being fetched.</summary>
        public static bool Downloading => Volatile.Read(ref _downloading) != 0;

        /// <summary>Bytes fetched so far in this download (for Live debug).</summary>
        public static long DownloadedBytes => Interlocked.Read(ref _downloadedBytes);

        /// <summary>
        /// Fetches the model into the models folder once, in the background: through Windows' proxy like the speech and face
        /// models, resumable, kept only when its SHA-256 is the published one. <paramref name="done"/> runs on the download's
        /// thread with whether it worked and why.
        /// </summary>
        public static void EnsureDownloaded(Action<bool, string> done)
        {
            if (ModelPresent()) { try { done?.Invoke(true, "already there"); } catch { } return; }
            if (Interlocked.Exchange(ref _downloading, 1) != 0) { return; }
            Task.Run(() =>
            {
                bool ok = false;
                string why;
                try { ok = Download(ModelPath(), out why); }
                catch (Exception ex) { why = ex.Message; }
                finally { Interlocked.Exchange(ref _downloading, 0); }
                try { done?.Invoke(ok, why); } catch { }
            });
        }

        private static bool Download(string finalPath, out string why)
        {
            string part = finalPath + ".part";
            long already = 0;
            try { var p = new FileInfo(part); if (p.Exists) { already = p.Length; } } catch { already = 0; }
            if (already > ModelBytes) { TryDelete(part); already = 0; }
            var request = (HttpWebRequest)WebRequest.Create(ModelUrl);
            request.Method = "GET";
            request.UserAgent = "Tempo/" + (UpdateChecker.CurrentVersion?.ToString() ?? "1.0");
            request.Timeout = 20000;
            request.ReadWriteTimeout = 120000;
            request.AllowAutoRedirect = true;        // GitHub hands release files out from its download host
            request.Proxy = SystemProxy.Get();
            request.KeepAlive = false;
            if (already > 0 && already < ModelBytes) { try { request.AddRange(already); } catch { already = 0; } }
            using (var response = (HttpWebResponse)request.GetResponse())
            using (Stream source = response.GetResponseStream())
            {
                bool resuming = already > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                if (!resuming) { already = 0; }
                using (var dest = new FileStream(part, resuming ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[131072];
                    long total = already;
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        dest.Write(buffer, 0, read);
                        total += read;
                        Interlocked.Exchange(ref _downloadedBytes, total);
                        if (total > ModelBytes) { break; }
                    }
                }
            }
            var info = new FileInfo(part);
            if (!info.Exists || info.Length != ModelBytes)
            {
                why = "the download stopped at " + (info.Exists ? info.Length / 1048576 : 0) + " of " + ModelBytes / 1048576
                      + " MB — it carries on next time";
                if (info.Exists && info.Length > ModelBytes) { TryDelete(part); }
                return false;
            }
            string hash;
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(part))
            {
                hash = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            }
            if (hash != ModelSha256)
            {
                TryDelete(part);
                why = "the downloaded file is not the published one (SHA-256 " + hash.Substring(0, 12) + "…) — deleted";
                return false;
            }
            try { if (File.Exists(finalPath)) { File.Delete(finalPath); } } catch { }
            File.Move(part, finalPath);
            why = "downloaded and checked";
            return true;
        }

        private static void TryDelete(string path) { try { File.Delete(path); } catch { } }

        public void Dispose()
        {
            lock (_gate)
            {
                try { _session?.Dispose(); } catch { }
                _session = null;
            }
        }
    }
}
