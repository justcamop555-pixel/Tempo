using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SherpaOnnx;

namespace AutoClicker.Utils
{
    /// <summary>
    /// NVIDIA's Nemotron streaming speech models on the sherpa-onnx runtime (round 35, 2026-11-17 — the user: "add Nemotron
    /// streaming to Tempo model because it is strongest overall for local model"). A cache-aware FastConformer transducer
    /// that hears audio as it arrives, 560 ms at a time, instead of decoding a window of it again and again as Whisper does:
    /// its words come out about half a second after they are said, are never rewritten, and carry their own punctuation and
    /// capitals.
    ///
    ///  • Nemotron Speech Streaming EN 0.6B (English; NVIDIA Open Model License).
    ///  • Nemotron 3.5 ASR Streaming 0.6B (40 language-locales — every language Tempo's picker offers among them;
    ///    OpenMDW-1.1): the language is given per stream ("en", "es"…) or "auto".
    ///
    /// The files are sherpa-onnx's int8 exports (k2-fsa/sherpa-onnx releases, tag asr-models): encoder, decoder, joiner,
    /// tokens. Measured on this PC (scratchpad/nemotron, 2026-11-17/21): 0 word errors on the model's own samples and on
    /// Tempo's test conversation; fed 100 ms at a time, a token was out 0.62 s after its own time (median; 90 % by 0.72 s);
    /// ~2.4 s to load. Tempo runs it on ONE thread: 0.40× real time for 0.41 cores, where two threads (0.25×) cost 2.49
    /// cores — ONNX Runtime's worker threads spin between the engine's many small calls (TempoTranscriber.StreamingThreads).
    ///
    /// Not thread-safe beyond its own lock: one caller (the caption worker) feeds it. Dispose from anywhere.
    /// </summary>
    public sealed class NemotronStreaming : IDisposable
    {
        public const string EncoderFile = "encoder.int8.onnx", DecoderFile = "decoder.int8.onnx",
                            JoinerFile = "joiner.int8.onnx", TokensFile = "tokens.txt";
        public const int SampleRate = 16000;

        // ── Two ONNX runtimes in one process ────────────────────────────────────────────────────────────────────────────
        //
        // Windows' in-box machine learning (the face and voice fingerprints: FaceAi, VoicePrint) loads System32's
        // onnxruntime.dll (ORT 1.17.1 on this PC). sherpa-onnx brings its own, newer one, and its engine DLL imports it BY
        // NAME — so when System32's was in the process first, sherpa-onnx was bound to it, asked it for an API it does not
        // have, and the process died (0xC0000005, scratchpad/nemotron --winml-first). Loaded the other way round both live
        // side by side (System32's is then loaded by its full path). Prepare() loads sherpa-onnx's engine — and the runtime
        // beside it — first; FaceAi and VoicePrint call it before they touch Windows' machine learning.
        private static int _prepared;   // 0 = not yet, 1 = loaded, -1 = not available
        private static readonly object PrepareLock = new object();

        /// <summary>
        /// Loads sherpa-onnx's native engine (with the onnxruntime.dll beside it) into the process, once. Call before anything
        /// uses Windows' in-box machine learning. False when this build has no engine.
        /// </summary>
        public static bool Prepare()
        {
            int state = Volatile.Read(ref _prepared);
            if (state != 0) { return state > 0; }
            lock (PrepareLock)
            {
                if (_prepared != 0) { return _prepared > 0; }
                bool ok;
                try { ok = NativeLibrary.TryLoad("sherpa-onnx-c-api", typeof(OnlineRecognizer).Assembly, null, out IntPtr _); }
                catch { ok = false; }
                Volatile.Write(ref _prepared, ok ? 1 : -1);
                if (!ok) { Logger.Info("[Captions] the Nemotron engine (sherpa-onnx) could not be loaded in this build."); }
                return ok;
            }
        }

        /// <summary>True when <paramref name="dir"/> holds a whole Nemotron model (the four files, at plausible sizes).</summary>
        public static bool IsModelDir(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { return false; }
                // The encoder is the model (~650 MB in int8); the others are small but never empty.
                return Size(dir, EncoderFile) > 50L * 1024 * 1024
                       && Size(dir, DecoderFile) > 64 * 1024
                       && Size(dir, JoinerFile) > 64 * 1024
                       && Size(dir, TokensFile) > 256;
            }
            catch { return false; }
        }

        private static long Size(string dir, string file)
        {
            var fi = new FileInfo(Path.Combine(dir, file));
            return fi.Exists ? fi.Length : 0;
        }

        /// <summary>Bytes the model's files take on disk (0 when unreadable).</summary>
        public static long BytesOnDisk(string dir)
        {
            long total = 0;
            try { foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) { total += new FileInfo(f).Length; } }
            catch { }
            return total;
        }

        /// <summary>Words of a line that can no longer change, and where in the stream they were heard.</summary>
        public sealed class Piece
        {
            public string Text;
            /// <summary>Stream seconds (see <see cref="StreamSeconds"/>) of the piece's first and last token.</summary>
            public double Start, End;
        }

        private OnlineRecognizer _recognizer;
        private OnlineStream _stream;
        private readonly object _gate = new object();
        private float[] _feedBuf = new float[0];
        private string[] _tokens = new string[0];
        private float[] _times = new float[0];
        private int _taken;              // tokens of the line already handed out by TakeFinal
        private long _fed;               // samples fed to this stream
        private long _decodes;           // Decode calls on this stream: each one is ChunkSeconds of audio
        private long _lineStartDecodes;  // _decodes when the line began (the engine times a line's tokens from there)

        /// <summary>True for the 40-language model (its tokens hold language tags such as &lt;de-DE&gt;).</summary>
        public bool Multilingual { get; private set; }

        /// <summary>The folder the model was loaded from.</summary>
        public string ModelDir { get; private set; }

        /// <summary>Threads the encoder runs on.</summary>
        public int Threads { get; private set; }

        /// <summary>
        /// Audio per Decode call: the model's chunk ("560ms" in its name). Measured: 67 calls for 37.5 s of audio, every one
        /// 0.56 s. A line's token times count from what the encoder had processed when the line began — decode calls times
        /// this — not from the audio fed by then, which runs up to a chunk ahead (scratchpad/nemotron --stream).
        /// </summary>
        public double ChunkSeconds { get; private set; } = 0.56;

        /// <summary>The language the stream was given ("en", "auto"…); null for the English model.</summary>
        public string Language { get; private set; }

        /// <summary>The language the 40-language model last tagged its words with ("en", "de"…), or null.</summary>
        public string DetectedLanguage { get; private set; }

        /// <summary>Milliseconds the last Feed spent decoding, and its running average.</summary>
        public int LastDecodeMs { get; private set; }
        public double AverageDecodeMs { get; private set; }

        /// <summary>Samples fed to the current stream, and the same in seconds — the clock <see cref="Piece"/> times are on.</summary>
        public long StreamSamples { get { lock (_gate) { return _fed; } } }
        public double StreamSeconds { get { lock (_gate) { return _fed / (double)SampleRate; } } }

        /// <summary>Stream seconds at which the current line began, and how long it has run.</summary>
        public double LineStartSeconds { get { lock (_gate) { return _lineStartDecodes * ChunkSeconds; } } }
        public double LineSeconds { get { lock (_gate) { return Math.Max(0, _fed / (double)SampleRate - _lineStartDecodes * ChunkSeconds); } } }

        private NemotronStreaming() { }

        /// <summary>
        /// Loads the model in <paramref name="dir"/> on <paramref name="threads"/> threads, ending a line after
        /// <paramref name="endSilenceSec"/> of quiet following words and at <paramref name="maxLineSec"/> at the latest.
        /// Null, with <paramref name="why"/>, when it can't.
        /// </summary>
        public static NemotronStreaming TryLoad(string dir, int threads, float endSilenceSec, float maxLineSec, out string why)
        {
            if (!IsModelDir(dir)) { why = "the Nemotron model's files are not all there (" + dir + ")"; return null; }
            if (!Prepare()) { why = "this Tempo build has no Nemotron engine"; return null; }
            try
            {
                var c = new OnlineRecognizerConfig();
                c.FeatConfig.SampleRate = SampleRate;
                c.FeatConfig.FeatureDim = 80;
                c.ModelConfig.Transducer.Encoder = Path.Combine(dir, EncoderFile);
                c.ModelConfig.Transducer.Decoder = Path.Combine(dir, DecoderFile);
                c.ModelConfig.Transducer.Joiner = Path.Combine(dir, JoinerFile);
                c.ModelConfig.Tokens = Path.Combine(dir, TokensFile);
                c.ModelConfig.NumThreads = Math.Max(1, threads);
                c.ModelConfig.Provider = "cpu";
                c.ModelConfig.Debug = 0;
                // Greedy: a token, once out, is never taken back — what lets Tempo show words as final the moment they stand.
                c.DecodingMethod = "greedy_search";
                c.EnableEndpoint = 1;
                // Rule 1: a line with nothing in it ends after 2.4 s of quiet; rule 2: words, then this much quiet; rule 3: the cap.
                c.Rule1MinTrailingSilence = 2.4f;
                c.Rule2MinTrailingSilence = endSilenceSec;
                c.Rule3MinUtteranceLength = maxLineSec;
                var r = new OnlineRecognizer(c);
                why = null;
                var n = new NemotronStreaming
                {
                    _recognizer = r,
                    ModelDir = dir,
                    Threads = Math.Max(1, threads),
                    Multilingual = HasLanguageTags(Path.Combine(dir, TokensFile)),
                };
                // "…-560ms-…" in the folder's name: the chunk the model was exported for.
                Match ms = Regex.Match(Path.GetFileName(dir.TrimEnd('\\', '/')) ?? "", @"-(\d{3,4})ms-", RegexOptions.CultureInvariant);
                if (ms.Success && int.TryParse(ms.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int chunkMs)
                    && chunkMs >= 80 && chunkMs <= 2000)
                {
                    n.ChunkSeconds = chunkMs / 1000.0;
                }
                return n;
            }
            catch (Exception ex)
            {
                why = ex.Message;
                return null;
            }
        }

        private static bool HasLanguageTags(string tokens)
        {
            try
            {
                foreach (string line in File.ReadLines(tokens, Encoding.UTF8))
                {
                    if (LanguageTag.IsMatch(line)) { return true; }
                }
            }
            catch { }
            return false;
        }

        // "<de-DE>" — the 40-language model's tag for the language of the words after it.
        private static readonly Regex LanguageTag = new Regex(@"^<[a-z]{2}-[A-Z]{2}>", RegexOptions.CultureInvariant);

        /// <summary>
        /// Starts a fresh stream, in <paramref name="language"/> for the 40-language model ("auto", or a code such as "de");
        /// the English model ignores it. Stream times start again at 0.
        /// </summary>
        public void Begin(string language)
        {
            lock (_gate)
            {
                if (_recognizer == null) { return; }
                try { _stream?.Dispose(); } catch { }
                _stream = _recognizer.CreateStream();
                if (Multilingual)
                {
                    string lang = string.IsNullOrEmpty(language) ? "auto" : language;
                    try { _stream.SetOption("language", lang); } catch { }
                    Language = lang;
                }
                else
                {
                    Language = null;
                }
                _fed = 0;
                _decodes = 0;
                _lineStartDecodes = 0;
                ClearLine();
            }
        }

        /// <summary>Feeds <paramref name="count"/> samples (16 kHz, ±1) and decodes as far as they allow.</summary>
        public void Feed(float[] samples, int offset, int count)
        {
            if (samples == null || count <= 0) { return; }
            lock (_gate)
            {
                if (_recognizer == null || _stream == null) { return; }
                if (_feedBuf.Length != count) { _feedBuf = new float[count]; }
                Array.Copy(samples, offset, _feedBuf, 0, count);
                var clock = Stopwatch.StartNew();
                _stream.AcceptWaveform(SampleRate, _feedBuf);
                _fed += count;
                int ran = 0;
                while (_recognizer.IsReady(_stream)) { _recognizer.Decode(_stream); ran++; }
                _decodes += ran;
                if (ran > 0)
                {
                    // Only a decode changes the result: reading it otherwise copies the whole line for nothing.
                    OnlineRecognizerResult res = _recognizer.GetResult(_stream);
                    string[] tokens = res.Tokens ?? new string[0];
                    float[] times = res.Timestamps ?? new float[0];
                    if (times.Length != tokens.Length)
                    {
                        // Never seen; keep the arrays index for index whatever happens.
                        var fixedTimes = new float[tokens.Length];
                        for (int i = 0; i < fixedTimes.Length; i++) { fixedTimes[i] = i < times.Length ? times[i] : (times.Length > 0 ? times[times.Length - 1] : 0f); }
                        times = fixedTimes;
                    }
                    _tokens = tokens;
                    _times = times;
                    if (_taken > _tokens.Length) { _taken = _tokens.Length; }
                    for (int i = _tokens.Length - 1; i >= 0; i--)
                    {
                        string t = _tokens[i] ?? "";
                        if (LanguageTag.IsMatch(t)) { DetectedLanguage = t.Substring(1, 2); break; }
                    }
                }
                clock.Stop();
                LastDecodeMs = (int)clock.ElapsedMilliseconds;
                AverageDecodeMs = AverageDecodeMs <= 0 ? LastDecodeMs : AverageDecodeMs * 0.9 + LastDecodeMs * 0.1;
            }
        }

        /// <summary>True when the line has ended (quiet after words, or the cap): take what is left, then <see cref="NextLine"/>.</summary>
        public bool AtEndpoint
        {
            get
            {
                lock (_gate)
                {
                    try { return _recognizer != null && _stream != null && _recognizer.IsEndpoint(_stream); }
                    catch { return false; }
                }
            }
        }

        /// <summary>Starts the next line on the same stream (the encoder keeps its context; the words and times start over).</summary>
        public void NextLine()
        {
            lock (_gate)
            {
                if (_recognizer == null || _stream == null) { return; }
                try { _recognizer.Reset(_stream); } catch { }
                _lineStartDecodes = _decodes;
                ClearLine();
            }
        }

        private void ClearLine()
        {
            _tokens = new string[0];
            _times = new float[0];
            _taken = 0;
        }

        /// <summary>The whole current line, tags left out.</summary>
        public string Text { get { lock (_gate) { return Detok(0, _tokens.Length); } } }

        /// <summary>True when the line holds a word (not only a language tag).</summary>
        public bool LineHasWords
        {
            get
            {
                lock (_gate)
                {
                    foreach (string t in _tokens) { if (!IsTag(t) && (t ?? "").Trim('▁', ' ').Length > 0) { return true; } }
                    return false;
                }
            }
        }

        /// <summary>True when the line holds a word not handed out yet.</summary>
        public bool HasUntaken
        {
            get
            {
                lock (_gate)
                {
                    for (int k = _taken; k < _tokens.Length; k++)
                    {
                        string t = _tokens[k];
                        if (!IsTag(t) && (t ?? "").Trim('▁', ' ').Length > 0) { return true; }
                    }
                    return false;
                }
            }
        }

        /// <summary>The words after the last ones handed out — the end of the line, which may still grow ("" when none).</summary>
        public string Tail { get { lock (_gate) { return Detok(_taken, _tokens.Length); } } }

        /// <summary>Stream seconds of the first token of <see cref="Tail"/>, or -1 when there is none.</summary>
        public double TailStart
        {
            get
            {
                lock (_gate)
                {
                    for (int k = _taken; k < _tokens.Length; k++)
                    {
                        if (!IsTag(_tokens[k])) { return _lineStartDecodes * ChunkSeconds + _times[k]; }
                    }
                    return -1;
                }
            }
        }

        /// <summary>True when everything in the line has been handed out and its last token ends a sentence.</summary>
        public bool AtSentenceEnd
        {
            get
            {
                lock (_gate)
                {
                    if (_tokens.Length == 0 || _taken < _tokens.Length) { return false; }
                    string last = (_tokens[_tokens.Length - 1] ?? "").Trim('▁', ' ');
                    return last.Length > 0 && ".?!。？！".IndexOf(last[last.Length - 1]) >= 0;
                }
            }
        }

        /// <summary>
        /// The words that can no longer change since the last call — all of the line's when <paramref name="all"/> (its end) —
        /// or null when there are none. A word stands once the next word has begun or a punctuation mark has closed it: the
        /// decoder never takes a token back (greedy), but the last word's next piece may still be on its way ("under" →
        /// "understand"). Chinese, Japanese and Thai have no spaces to begin a word, so there the words stand up to the last
        /// punctuation mark.
        /// </summary>
        public Piece TakeFinal(bool all)
        {
            lock (_gate)
            {
                int n = _tokens.Length;
                int s = all ? n : StablePoint();
                if (s <= _taken) { return null; }
                int from = _taken;
                _taken = s;
                string text = Detok(from, s);
                if (text.Length == 0) { return null; }   // a language tag alone
                double t0 = -1, t1 = 0;
                for (int k = from; k < s; k++)
                {
                    if (IsTag(_tokens[k])) { continue; }
                    if (t0 < 0) { t0 = _times[k]; }
                    t1 = _times[k];
                }
                double line = _lineStartDecodes * ChunkSeconds;
                return new Piece { Text = text, Start = line + Math.Max(0, t0), End = line + t1 };
            }
        }

        // Closing punctuation: it ends the word before it. (¿ ¡ « “ ( open a word, and are left out on purpose.)
        private const string ClosingMarks = ".,?!;:…。，？！；：、)]}»”";

        private int StablePoint()
        {
            int n = _tokens.Length;
            if (n <= _taken) { return _taken; }
            string last = (_tokens[n - 1] ?? "").Trim('▁', ' ');
            bool noSpaces = false;
            for (int k = _taken; k < n && !noSpaces; k++) { noSpaces = HasNoSpaceScript(_tokens[k]); }
            if (noSpaces)
            {
                for (int k = n - 1; k >= _taken; k--)
                {
                    string t = (_tokens[k] ?? "").Trim('▁', ' ');
                    if (t.Length > 0 && ClosingMarks.IndexOf(t[t.Length - 1]) >= 0 && AllMarks(t)) { return k + 1; }
                }
                return _taken;
            }
            if (last.Length > 0 && AllMarks(last) && ClosingMarks.IndexOf(last[last.Length - 1]) >= 0) { return n; }
            // Everything before the first piece of the last word.
            for (int k = n - 1; k > _taken; k--)
            {
                if (StartsWord(_tokens[k])) { return k; }
            }
            return _taken;
        }

        private static bool AllMarks(string t)
        {
            foreach (char c in t) { if (!char.IsPunctuation(c) && !char.IsSymbol(c)) { return false; } }
            return t.Length > 0;
        }

        // sherpa-onnx hands the tokens over with SentencePiece's "▁" already turned into a space (" A|fter| ear|ly", and the
        // 40-language model's bare " " before "e|ar|ly"): either begins a word.
        private static bool StartsWord(string t) => !string.IsNullOrEmpty(t) && (t[0] == ' ' || t[0] == '▁' || IsTag(t));

        private static bool IsTag(string t) => !string.IsNullOrEmpty(t) && t.Length >= 3 && t[0] == '<' && t[t.Length - 1] == '>';

        /// <summary>Chinese, Japanese or Thai letters: scripts written without spaces between words.</summary>
        private static bool HasNoSpaceScript(string t)
        {
            if (string.IsNullOrEmpty(t)) { return false; }
            foreach (char c in t)
            {
                if ((c >= '぀' && c <= 'ヿ')        // Hiragana, Katakana
                    || (c >= '㐀' && c <= '鿿')     // CJK ideographs
                    || (c >= '฀' && c <= '๿'))    // Thai
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Tokens [from, to) as text: "▁" is a space, tags and &lt;unk&gt; left out, spaces collapsed.</summary>
        private string Detok(int from, int to)
        {
            var sb = new StringBuilder();
            for (int k = Math.Max(0, from); k < to && k < _tokens.Length; k++)
            {
                string t = _tokens[k];
                if (string.IsNullOrEmpty(t) || IsTag(t)) { if (IsTag(t) && sb.Length > 0) { sb.Append(' '); } continue; }
                sb.Append(t.Replace('▁', ' '));
            }
            string s = sb.ToString();
            if (s.IndexOf("  ", StringComparison.Ordinal) >= 0) { s = Regex.Replace(s, @"\s{2,}", " "); }
            return s.Trim();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                try { _stream?.Dispose(); } catch { }
                try { _recognizer?.Dispose(); } catch { }
                _stream = null;
                _recognizer = null;
            }
        }
    }
}
