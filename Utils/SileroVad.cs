using System;
using System.IO;
using System.Numerics;
using System.Text;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Silero VAD (v5, 16 kHz) — the probability that someone is SPEAKING in each 32 ms of audio.
    ///
    /// Why it is here. Whisper writes words over anything: over music, a fan or gunfire it
    /// produced 15–35 invented words in 30 seconds, and the no-speech gate (Whisper's own
    /// estimate) cut that to 1–5. Silero is a tiny network trained for exactly this question,
    /// and on the same 30 seconds it put 1 chunk in 938 over even odds.
    ///
    /// Why it is written out here rather than taken from Whisper.net. whisper.cpp has Silero built
    /// in from 1.8.x, which Whisper.net only ships from 1.9.1 — and 1.9.1 is the version Tempo
    /// is pinned AWAY from (its Vulkan runtime was measured too slow to keep up, see the csproj).
    /// The network is small enough to run directly: the STFT, four convolutions, one LSTM cell
    /// and a sigmoid, about 0.7 M multiply-adds per 32 ms. The weights are whisper.cpp's own
    /// conversion (ggml-silero-v5.1.2.bin, embedded), and the result is checked against
    /// whisper.cpp 1.8.5's own VAD, chunk for chunk (scratchpad/vadcheck).
    ///
    /// Silero VAD: MIT licence, (c) Silero Team. The ggml conversion: MIT, (c) The ggml authors.
    ///
    /// Not thread-safe: one instance per audio stream. Feed it 512 samples at a time, in order;
    /// it carries its own state (the LSTM's h and c, and the last 64 samples as context).
    /// </summary>
    internal sealed class SileroVad
    {
        public const int SampleRate = 16000;
        /// <summary>Samples per step: 32 ms at 16 kHz.</summary>
        public const int ChunkSamples = 512;
        /// <summary>Samples carried over from the previous step, the way Silero was trained.</summary>
        public const int ContextSamples = 64;

        private const int Fft = 256;          // STFT filter length
        private const int Hop = 128;          // STFT hop
        private const int Bins = Fft / 2 + 1; // 129
        private const int Hidden = 128;

        // Weights, as float32, in PyTorch's own layout.
        private readonly float[] _basis;              // [258][256]
        private readonly float[][] _convW;            // per layer [out][in][3]
        private readonly float[][] _convB;            // per layer [out]
        private readonly int[] _convIn, _convOut;
        private static readonly int[] ConvStride = { 1, 2, 2, 1 };
        private readonly float[] _wIh, _wHh;          // [512][128]
        private readonly float[] _bIh, _bHh;          // [512]
        private readonly float[] _decW;               // [128]
        private readonly float _decB;

        // State.
        private readonly float[] _h = new float[Hidden];
        private readonly float[] _c = new float[Hidden];
        private readonly float[] _context = new float[ContextSamples];

        // Scratch, reused every step.
        private readonly float[] _padded = new float[ContextSamples + ChunkSamples + 64];
        private readonly float[] _gates = new float[4 * Hidden];

        /// <summary>How the 640 samples the STFT sees are framed. Production uses <see cref="Framing.Context"/>.</summary>
        internal enum Framing
        {
            /// <summary>The last 64 samples of the previous step, the 512 new ones, then 64 reflected — as Silero was trained.</summary>
            Context,
            /// <summary>64 reflected, the 512 new ones, 64 reflected — no memory of the previous step's audio.</summary>
            ReflectBoth
        }

        internal Framing Frame = Framing.Context;

        public string Version { get; }

        private SileroVad(byte[] model)
        {
            int p = 0;
            int I32() { int v = BitConverter.ToInt32(model, p); p += 4; return v; }

            if (I32() != 0x67676d6c) { throw new InvalidDataException("not a ggml model"); }
            int nameLen = I32();
            string type = Encoding.ASCII.GetString(model, p, nameLen);
            p += nameLen;
            if (type != "silero-16k") { throw new InvalidDataException("not a 16 kHz Silero model: " + type); }
            Version = I32() + "." + I32() + "." + I32();
            int window = I32(), context = I32(), layers = I32();
            if (window != ChunkSamples || context != ContextSamples || layers != 4)
            {
                throw new InvalidDataException("unexpected Silero shape: window " + window + ", context " + context + ", layers " + layers);
            }
            _convIn = new int[layers];
            _convOut = new int[layers];
            for (int i = 0; i < layers; i++)
            {
                _convIn[i] = I32();
                _convOut[i] = I32();
                if (I32() != 3) { throw new InvalidDataException("unexpected kernel size"); }
            }
            if (I32() != Hidden || I32() != Hidden || I32() != Hidden || I32() != 1)
            {
                throw new InvalidDataException("unexpected LSTM/decoder shape");
            }

            _convW = new float[layers][];
            _convB = new float[layers][];
            float[] wIh = null, wHh = null, bIh = null, bHh = null, decW = null, basis = null;
            float decB = 0;
            while (p < model.Length)
            {
                int nDims = I32(), len = I32(), ftype = I32();
                int count = 1;
                for (int i = 0; i < nDims; i++) { count *= I32(); }
                string name = Encoding.ASCII.GetString(model, p, len);
                p += len;
                var data = new float[count];
                if (ftype == 1)
                {
                    for (int i = 0; i < count; i++) { data[i] = (float)BitConverter.ToHalf(model, p + 2 * i); }
                    p += 2 * count;
                }
                else
                {
                    Buffer.BlockCopy(model, p, data, 0, 4 * count);
                    p += 4 * count;
                }

                // The ggml dims are written innermost first, so each tensor is already in
                // PyTorch's row-major order: conv [out][in][k], LSTM [4*hidden][in], basis [258][256].
                if (name.StartsWith("_model.encoder.", StringComparison.Ordinal))
                {
                    int layer = name[15] - '0';
                    if (name.EndsWith(".weight", StringComparison.Ordinal)) { _convW[layer] = data; } else { _convB[layer] = data; }
                }
                else if (name == "_model.decoder.rnn.weight_ih") { wIh = data; }
                else if (name == "_model.decoder.rnn.weight_hh") { wHh = data; }
                else if (name == "_model.decoder.rnn.bias_ih") { bIh = data; }
                else if (name == "_model.decoder.rnn.bias_hh") { bHh = data; }
                else if (name == "_model.decoder.decoder.2.weight") { decW = data; }
                else if (name == "_model.decoder.decoder.2.bias") { decB = data[0]; }
                else if (name == "_model.stft.forward_basis_buffer") { basis = data; }
            }

            for (int i = 0; i < layers; i++)
            {
                if (_convW[i] == null || _convB[i] == null || _convW[i].Length != _convOut[i] * _convIn[i] * 3)
                {
                    throw new InvalidDataException("encoder layer " + i + " missing or misshapen");
                }
            }
            if (wIh == null || wHh == null || bIh == null || bHh == null || decW == null || basis == null
                || basis.Length != 2 * Bins * Fft || wIh.Length != 4 * Hidden * Hidden)
            {
                throw new InvalidDataException("Silero model incomplete");
            }
            _wIh = wIh;
            _wHh = wHh;
            _bIh = bIh;
            _bHh = bHh;
            _decW = decW;
            _decB = decB;
            _basis = basis;
        }

        /// <summary>The model embedded in Tempo, or null if it cannot be read (captions then run without it).</summary>
        public static SileroVad LoadEmbedded()
        {
            try
            {
                using (Stream s = typeof(SileroVad).Assembly.GetManifestResourceStream("Tempo.ggml-silero-v5.1.2.bin"))
                {
                    if (s == null) { return null; }
                    var ms = new MemoryStream();
                    s.CopyTo(ms);
                    return new SileroVad(ms.ToArray());
                }
            }
            catch (Exception ex)
            {
                Logger.Swallow("SileroVad.LoadEmbedded", ex);
                return null;
            }
        }

        /// <summary>A model from a file (tests).</summary>
        internal static SileroVad FromFile(string path) => new SileroVad(File.ReadAllBytes(path));

        /// <summary>Back to silence: a new stream, or a gap in this one.</summary>
        public void Reset()
        {
            Array.Clear(_h, 0, _h.Length);
            Array.Clear(_c, 0, _c.Length);
            Array.Clear(_context, 0, _context.Length);
        }

        /// <summary>
        /// The speech probability (0–1) of the next <see cref="ChunkSamples"/> samples, which start
        /// at <paramref name="offset"/>. A short final chunk is padded with silence.
        /// </summary>
        public float Process(float[] samples, int offset)
        {
            int n = Math.Min(ChunkSamples, Math.Max(0, samples.Length - offset));
            float[] x = _padded;
            int start;   // where the 512 new samples begin in x
            if (Frame == Framing.Context)
            {
                Array.Copy(_context, 0, x, 0, ContextSamples);
                start = ContextSamples;
            }
            else
            {
                start = 64;
            }
            Array.Copy(samples, offset, x, start, n);
            if (n < ChunkSamples) { Array.Clear(x, start + n, ChunkSamples - n); }
            int end = start + ChunkSamples;           // one past the last real sample (576 or 576)

            // Reflect-pad 64 on the right (and, without context, 64 on the left too).
            for (int j = 0; j < 64; j++) { x[end + j] = x[end - 2 - j]; }
            if (Frame == Framing.ReflectBoth)
            {
                for (int j = 0; j < 64; j++) { x[63 - j] = x[64 + 1 + j]; }
            }

            // The next step's context: the last 64 samples of THIS step's input (before padding).
            if (Frame == Framing.Context) { Array.Copy(x, end - ContextSamples, _context, 0, ContextSamples); }

            // STFT magnitude: 129 bins x 4 frames.
            int frames = (end + 64 - Fft) / Hop + 1;
            var mag = new float[Bins * frames];
            for (int t = 0; t < frames; t++)
            {
                int at = t * Hop;
                for (int b = 0; b < Bins; b++)
                {
                    float re = Dot(_basis, b * Fft, x, at, Fft);
                    float im = Dot(_basis, (b + Bins) * Fft, x, at, Fft);
                    mag[b * frames + t] = (float)Math.Sqrt(re * re + im * im);
                }
            }

            // Four 1-D convolutions (kernel 3, padding 1), each followed by ReLU.
            float[] cur = mag;
            int len = frames;
            for (int l = 0; l < 4; l++)
            {
                int cin = _convIn[l], cout = _convOut[l], stride = ConvStride[l];
                int outLen = (len + 2 - 3) / stride + 1;
                var next = new float[cout * outLen];
                float[] w = _convW[l];
                float[] bias = _convB[l];
                for (int o = 0; o < cout; o++)
                {
                    for (int t = 0; t < outLen; t++)
                    {
                        float acc = bias[o];
                        int center = t * stride;   // input index of kernel tap 1
                        for (int i = 0; i < cin; i++)
                        {
                            int wb = (o * cin + i) * 3;
                            int ib = i * len;
                            for (int k = 0; k < 3; k++)
                            {
                                int ti = center + k - 1;
                                if (ti >= 0 && ti < len) { acc += w[wb + k] * cur[ib + ti]; }
                            }
                        }
                        next[o * outLen + t] = acc > 0 ? acc : 0;
                    }
                }
                cur = next;
                len = outLen;
            }

            // LSTM cell on the (single) remaining frame. PyTorch gate order: i, f, g, o.
            // (With more than one frame left, the last one is taken.)
            var input = new float[Hidden];
            for (int i = 0; i < Hidden; i++) { input[i] = cur[i * len + (len - 1)]; }
            for (int g = 0; g < 4 * Hidden; g++)
            {
                _gates[g] = _bIh[g] + _bHh[g] + Dot(_wIh, g * Hidden, input, 0, Hidden) + Dot(_wHh, g * Hidden, _h, 0, Hidden);
            }
            float logit = _decB;
            for (int j = 0; j < Hidden; j++)
            {
                float ig = Sigmoid(_gates[j]);
                float fg = Sigmoid(_gates[Hidden + j]);
                float gg = (float)Math.Tanh(_gates[2 * Hidden + j]);
                float og = Sigmoid(_gates[3 * Hidden + j]);
                _c[j] = fg * _c[j] + ig * gg;
                _h[j] = og * (float)Math.Tanh(_c[j]);
                // Decoder: ReLU, then a 1x1 convolution to one value.
                float r = _h[j] > 0 ? _h[j] : 0;
                logit += _decW[j] * r;
            }
            return Sigmoid(logit);
        }

        private static float Sigmoid(float v) => 1f / (1f + (float)Math.Exp(-v));

        private static float Dot(float[] a, int ai, float[] b, int bi, int n)
        {
            int i = 0;
            var acc = Vector<float>.Zero;
            int w = Vector<float>.Count;
            for (; i <= n - w; i += w)
            {
                acc += new Vector<float>(a, ai + i) * new Vector<float>(b, bi + i);
            }
            float s = Vector.Dot(acc, Vector<float>.One);
            for (; i < n; i++) { s += a[ai + i] * b[bi + i]; }
            return s;
        }
    }
}
