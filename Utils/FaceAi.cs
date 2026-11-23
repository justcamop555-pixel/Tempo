using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Windows.AI.MachineLearning;

namespace AutoClicker.Utils
{
    /// <summary>
    /// The caption speakers' face AI (round 26, 2026-11-15): two models from OpenCV's model zoo, run on Windows' own
    /// machine-learning engine (Windows.AI.MachineLearning — part of Windows since 10 1809, nothing added to Tempo):
    ///
    ///  • YuNet (face_detection_yunet_2023mar.onnx, 0.23 MB, MIT; built into Tempo) — faces with five landmarks: the
    ///    eyes, the nose tip and the two mouth corners. The landmarks put the mouth-motion window on the MOUTH rather than
    ///    on a fixed slice of a box, and they are what a face is aligned by for recognition.
    ///  • SFace (face_recognition_sface_2021dec.onnx, 36.9 MB, Apache-2.0; fetched from huggingface.co/opencv the first
    ///    time face analysis runs, checked against its published SHA-256) — a 128-number fingerprint of a face. Two
    ///    pictures of one person score ≥ 0.363 by cosine (OpenCV's own threshold for this model); two people below it.
    ///
    /// Why: Tempo told faces apart by the colours around them (Utils/FaceAppearance.cs). In a news conference where four
    /// people wear the same red shirt against the same backdrop, colours cannot say who is who — and one man cut from a
    /// wide shot to a close-up became two speakers on the user's Speakers card (round 26, 22.png).
    ///
    /// The decoding, the alignment template and the match rule are ported from OpenCV's FaceDetectorYN
    /// (modules/objdetect/src/face_detect.cpp) and FaceRecognizerSF (face_recognize.cpp), Apache-2.0 — see
    /// Utils/ThirdPartyNotices.cs. Not thread-safe: one analyzer thread uses it.
    /// </summary>
    public sealed class FaceAi : IDisposable
    {
        /// <summary>A face YuNet found: its box, five landmarks and score, in the coordinates of the image it was given.</summary>
        public sealed class Face
        {
            public RectangleF Box;
            /// <summary>Right eye, left eye, nose tip, right mouth corner, left mouth corner (the person's right/left).</summary>
            public readonly PointF[] Marks = new PointF[5];
            public float Score;
        }

        // ── The models ──────────────────────────────────────────────────────────
        internal const string DetectorResource = "Tempo.face_detection_yunet_2023mar.onnx";
        internal const string RecognizerFile = "face_recognition_sface_2021dec.onnx";
        internal const string RecognizerUrl = "https://huggingface.co/opencv/face_recognition_sface/resolve/main/face_recognition_sface_2021dec.onnx";
        internal const string RecognizerSha256 = "0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79";
        internal const long RecognizerBytes = 38696353;

        /// <summary>OpenCV's cosine threshold for SFace: at or above it, the same person.</summary>
        public const float SamePersonCosine = 0.363f;

        private const int DetSize = 640;                 // the YuNet build's fixed input (1×3×640×640)
        private static readonly int[] Strides = { 8, 16, 32 };
        private const float NmsIou = 0.3f;               // OpenCV's FaceDetectorYN default

        private LearningModelSession _det, _rec;
        private LearningModelBinding _detBind, _recBind;
        private readonly float[] _detInput = new float[3 * DetSize * DetSize];
        private readonly float[] _recInput = new float[3 * 112 * 112];
        private string _detIn, _recIn, _recOut;

        /// <summary>True when the recognizer (SFace) is loaded and fingerprints can be taken.</summary>
        public bool CanRecognize => _rec != null;

        /// <summary>What happened while loading, for the log and Live debug.</summary>
        public string Status { get; private set; } = "";

        /// <summary>Milliseconds the last detection / fingerprint took (for Live debug).</summary>
        public int LastDetectMs { get; private set; }
        public int LastFingerprintMs { get; private set; }

        private FaceAi() { }

        /// <summary>
        /// The detector, and the recognizer too when its file is in the models folder. Null — with
        /// <paramref name="why"/> — when Windows' engine cannot run them (older Windows 10 builds take only older
        /// model versions); face analysis then carries on as before, by colours.
        /// </summary>
        public static FaceAi TryCreate(out string why)
        {
            // The Nemotron engine's ONNX runtime goes in first: Windows' in-box one in the process first made it crash
            // (NemotronStreaming.Prepare).
            NemotronStreaming.Prepare();
            var ai = new FaceAi();
            try
            {
                using (Stream s = typeof(FaceAi).Assembly.GetManifestResourceStream(DetectorResource))
                {
                    if (s == null) { why = "the face detector model is missing from this build"; return null; }
                    var ms = new MemoryStream();
                    s.CopyTo(ms);
                    LearningModel model = LoadFromBytes(ms.ToArray());
                    foreach (ILearningModelFeatureDescriptor f in model.InputFeatures) { ai._detIn = f.Name; break; }
                    ai._det = NewSession(model);
                    ai._detBind = new LearningModelBinding(ai._det);
                }
            }
            catch (Exception ex)
            {
                why = "Windows' machine-learning engine could not run the face detector (" + ex.Message.Trim() + ")";
                ai.Dispose();
                return null;
            }
            ai.TryLoadRecognizer();
            why = null;
            return ai;
        }

        /// <summary>Loads SFace from the models folder when the file is there and checks out; quietly does nothing when not.</summary>
        public bool TryLoadRecognizer()
        {
            if (_rec != null) { return true; }
            string path = RecognizerPath();
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length != RecognizerBytes) { Status = "face fingerprints: model not downloaded yet"; return false; }
                LearningModel model = LearningModel.LoadFromFilePath(path);
                foreach (ILearningModelFeatureDescriptor f in model.InputFeatures) { _recIn = f.Name; break; }
                foreach (ILearningModelFeatureDescriptor f in model.OutputFeatures) { _recOut = f.Name; break; }
                _rec = NewSession(model);
                _recBind = new LearningModelBinding(_rec);
                Status = "face fingerprints ready";
                return true;
            }
            catch (Exception ex)
            {
                _rec = null;
                Status = "face fingerprints unavailable: " + ex.Message.Trim();
                return false;
            }
        }

        /// <summary>
        /// A CPU session that lets Windows drop its own copy of the model once the session holds the weights
        /// (CloseModelOnSessionCreation, Windows 10 2004 and later; without it the 37 MB recognizer sat in memory twice).
        /// The model's descriptors are read before this. Older Windows: a plain session.
        /// </summary>
        private static LearningModelSession NewSession(LearningModel model)
        {
            var device = new LearningModelDevice(LearningModelDeviceKind.Cpu);
            try
            {
                return new LearningModelSession(model, device, new LearningModelSessionOptions { CloseModelOnSessionCreation = true });
            }
            catch
            {
                return new LearningModelSession(model, device);
            }
        }

        private static LearningModel LoadFromBytes(byte[] bytes)
        {
            var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new Windows.Storage.Streams.DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                writer.FlushAsync().AsTask().GetAwaiter().GetResult();
                writer.DetachStream();
            }
            stream.Seek(0);
            return LearningModel.LoadFromStreamAsync(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(stream))
                                .AsTask().GetAwaiter().GetResult();
        }

        internal static string RecognizerPath() => Path.Combine(WhisperModelManager.GetModelsDirectory(), RecognizerFile);

        // ── Detection ───────────────────────────────────────────────────────────

        /// <summary>
        /// Faces in a 24-bit BGR image (rows <paramref name="stride"/> bytes apart): letterboxed into YuNet's 640×640 —
        /// scaled down only when the image is bigger, padded with black at the right and bottom as OpenCV pads — decoded
        /// as FaceDetectorYN decodes, and kept by non-maximum suppression. Coordinates come back in the image's own.
        /// </summary>
        public List<Face> Detect(byte[] bgr, int stride, int w, int h, float minScore)
        {
            var found = new List<Face>();
            if (_det == null || bgr == null || w < 16 || h < 16) { return found; }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            double scale = Math.Min(1.0, Math.Min(DetSize / (double)w, DetSize / (double)h));
            int sw = Math.Max(1, (int)Math.Floor(w * scale)), sh = Math.Max(1, (int)Math.Floor(h * scale));
            Array.Clear(_detInput, 0, _detInput.Length);
            int plane = DetSize * DetSize;
            for (int y = 0; y < sh; y++)
            {
                int sy = Math.Min(h - 1, (int)(y / scale));
                int row = sy * stride;
                int o = y * DetSize;
                for (int x = 0; x < sw; x++)
                {
                    int p = row + Math.Min(w - 1, (int)(x / scale)) * 3;
                    // BGR planes, 0–255: blobFromImage's defaults (no scaling, no mean, no channel swap).
                    _detInput[o + x] = bgr[p];
                    _detInput[plane + o + x] = bgr[p + 1];
                    _detInput[2 * plane + o + x] = bgr[p + 2];
                }
            }
            _detBind.Clear();
            _detBind.Bind(_detIn, TensorFloat.CreateFromArray(new long[] { 1, 3, DetSize, DetSize }, _detInput));
            LearningModelEvaluationResult result = _det.Evaluate(_detBind, "yunet");

            var candidates = new List<Face>();
            for (int i = 0; i < Strides.Length; i++)
            {
                int st = Strides[i];
                int cols = DetSize / st, rows = DetSize / st;
                float[] cls = Read(result, "cls_" + st), obj = Read(result, "obj_" + st);
                float[] box = Read(result, "bbox_" + st), kps = Read(result, "kps_" + st);
                if (cls == null || obj == null || box == null || kps == null) { continue; }
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        int idx = r * cols + c;
                        float cs = Math.Min(1f, Math.Max(0f, cls[idx]));
                        float os = Math.Min(1f, Math.Max(0f, obj[idx]));
                        float score = (float)Math.Sqrt(cs * os);
                        if (score < minScore) { continue; }
                        float cx = (c + box[idx * 4 + 0]) * st, cy = (r + box[idx * 4 + 1]) * st;
                        float bw = (float)Math.Exp(box[idx * 4 + 2]) * st, bh = (float)Math.Exp(box[idx * 4 + 3]) * st;
                        var f = new Face { Score = score, Box = new RectangleF(cx - bw / 2f, cy - bh / 2f, bw, bh) };
                        for (int n = 0; n < 5; n++)
                        {
                            f.Marks[n] = new PointF((kps[idx * 10 + 2 * n] + c) * st, (kps[idx * 10 + 2 * n + 1] + r) * st);
                        }
                        candidates.Add(f);
                    }
                }
            }
            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
            foreach (Face f in candidates)
            {
                bool keep = true;
                foreach (Face k in found) { if (Iou(f.Box, k.Box) > NmsIou) { keep = false; break; } }
                if (!keep) { continue; }
                found.Add(f);
                if (found.Count >= 32) { break; }
            }
            // Back to the image's coordinates.
            if (scale < 1.0)
            {
                float inv = (float)(1.0 / scale);
                foreach (Face f in found)
                {
                    f.Box = new RectangleF(f.Box.X * inv, f.Box.Y * inv, f.Box.Width * inv, f.Box.Height * inv);
                    for (int n = 0; n < 5; n++) { f.Marks[n] = new PointF(f.Marks[n].X * inv, f.Marks[n].Y * inv); }
                }
            }
            LastDetectMs = (int)clock.ElapsedMilliseconds;
            return found;
        }

        internal static float Iou(RectangleF a, RectangleF b)
        {
            float x0 = Math.Max(a.Left, b.Left), y0 = Math.Max(a.Top, b.Top);
            float x1 = Math.Min(a.Right, b.Right), y1 = Math.Min(a.Bottom, b.Bottom);
            float inter = Math.Max(0, x1 - x0) * Math.Max(0, y1 - y0);
            float union = a.Width * a.Height + b.Width * b.Height - inter;
            return union > 0 ? inter / union : 0;
        }

        // ── Fingerprints ────────────────────────────────────────────────────────

        // SFace's alignment template (OpenCV FaceRecognizerSF::getSimilarityTransformMatrix): where the five landmarks sit
        // in the 112×112 face it was trained on.
        private static readonly double[,] Template =
        {
            { 38.2946, 51.6963 }, { 73.5318, 51.5014 }, { 56.0252, 71.7366 }, { 41.5493, 92.3655 }, { 70.7299, 92.2041 },
        };

        /// <summary>
        /// The 128-number fingerprint of the face with these landmarks (in this BGR image's coordinates), unit length — or
        /// null without the recognizer. The face is aligned to SFace's template by the least-squares similarity transform
        /// (rotation, one scale, shift: the closed form of the 2-D case of OpenCV's Umeyama solution), sampled bilinearly
        /// to 112×112, and fed as RGB 0–255 (blobFromImage(…, 1, 112×112, 0, swapRB: true)).
        /// </summary>
        public float[] Fingerprint(byte[] bgr, int stride, int w, int h, PointF[] marks)
        {
            if (_rec == null || bgr == null || marks == null || marks.Length < 5) { return null; }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // Least squares: dst ≈ s·R·src + t.
            double mx = 0, my = 0, tx = 0, ty = 0;
            for (int i = 0; i < 5; i++) { mx += marks[i].X; my += marks[i].Y; tx += Template[i, 0]; ty += Template[i, 1]; }
            mx /= 5; my /= 5; tx /= 5; ty /= 5;
            double a = 0, b = 0, varP = 0;
            for (int i = 0; i < 5; i++)
            {
                double px = marks[i].X - mx, py = marks[i].Y - my;
                double qx = Template[i, 0] - tx, qy = Template[i, 1] - ty;
                a += px * qx + py * qy;
                b += px * qy - py * qx;
                varP += px * px + py * py;
            }
            double norm = Math.Sqrt(a * a + b * b);
            if (varP < 1e-6 || norm < 1e-9) { return null; }
            double s = norm / varP, cos = a / norm, sin = b / norm;
            // Inverse map: src = Rᵀ(dst − t′)/s with t′ = (tx, ty) − s·R·(mx, my).
            double t0 = tx - s * (cos * mx - sin * my), t1 = ty - s * (sin * mx + cos * my);
            int plane = 112 * 112;
            for (int v = 0; v < 112; v++)
            {
                for (int u = 0; u < 112; u++)
                {
                    double dx = (u - t0) / s, dy = (v - t1) / s;
                    double sx = cos * dx + sin * dy, sy = -sin * dx + cos * dy;
                    Sample(bgr, stride, w, h, sx, sy, out float bl, out float gr, out float rd);
                    int o = v * 112 + u;
                    _recInput[o] = rd;                    // RGB planes
                    _recInput[plane + o] = gr;
                    _recInput[2 * plane + o] = bl;
                }
            }
            _recBind.Clear();
            _recBind.Bind(_recIn, TensorFloat.CreateFromArray(new long[] { 1, 3, 112, 112 }, _recInput));
            LearningModelEvaluationResult result = _rec.Evaluate(_recBind, "sface");
            float[] feat = Read(result, _recOut);
            if (feat == null || feat.Length < 128) { return null; }
            double sq = 0;
            for (int i = 0; i < feat.Length; i++) { sq += feat[i] * (double)feat[i]; }
            float inv = sq > 0 ? (float)(1.0 / Math.Sqrt(sq)) : 0f;
            var unit = new float[feat.Length];
            for (int i = 0; i < feat.Length; i++) { unit[i] = feat[i] * inv; }
            LastFingerprintMs = (int)clock.ElapsedMilliseconds;
            return unit;
        }

        /// <summary>Bilinear sample of a BGR image at (x, y), pixel centres on the integers; black outside it.</summary>
        private static void Sample(byte[] bgr, int stride, int w, int h, double x, double y, out float b, out float g, out float r)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            double bb = 0, gg = 0, rr = 0;
            for (int j = 0; j < 2; j++)
            {
                int yy = y0 + j;
                if (yy < 0 || yy >= h) { continue; }
                double wy = j == 0 ? 1 - fy : fy;
                for (int i = 0; i < 2; i++)
                {
                    int xx = x0 + i;
                    if (xx < 0 || xx >= w) { continue; }
                    double wgt = wy * (i == 0 ? 1 - fx : fx);
                    int p = yy * stride + xx * 3;
                    bb += bgr[p] * wgt; gg += bgr[p + 1] * wgt; rr += bgr[p + 2] * wgt;
                }
            }
            b = (float)bb; g = (float)gg; r = (float)rr;
        }

        /// <summary>Cosine similarity of two unit fingerprints (OpenCV's FR_COSINE); NaN when either is missing.</summary>
        public static float Similarity(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length) { return float.NaN; }
            double dot = 0;
            for (int i = 0; i < a.Length; i++) { dot += a[i] * (double)b[i]; }
            return (float)dot;
        }

        /// <summary>Folds a new fingerprint into a running one (mean of unit vectors, renormalised); returns the new mean.</summary>
        public static float[] Accumulate(float[] mean, int count, float[] add)
        {
            if (add == null) { return mean; }
            if (mean == null || count <= 0) { return (float[])add.Clone(); }
            int n = Math.Min(count, 20);   // the latest pictures keep a say: people turn, light changes
            var m = new float[add.Length];
            double sq = 0;
            for (int i = 0; i < m.Length; i++) { m[i] = (mean[i] * n + add[i]) / (n + 1); sq += m[i] * (double)m[i]; }
            float inv = sq > 0 ? (float)(1.0 / Math.Sqrt(sq)) : 0f;
            for (int i = 0; i < m.Length; i++) { m[i] *= inv; }
            return m;
        }

        // ── Reading outputs ─────────────────────────────────────────────────────

        [ComImport, Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMemoryBufferByteAccess
        {
            void GetBuffer(out IntPtr buffer, out uint capacity);
        }

        /// <summary>An output tensor's floats: straight from its memory where Windows allows it, element by element if not.</summary>
        private static float[] Read(LearningModelEvaluationResult result, string name)
        {
            if (name == null || !result.Outputs.TryGetValue(name, out object value) || !(value is TensorFloat t)) { return null; }
            try
            {
                using (Windows.Foundation.IMemoryBufferReference reference = t.CreateReference())
                {
                    var access = WinRT.CastExtensions.As<IMemoryBufferByteAccess>(reference);
                    access.GetBuffer(out IntPtr data, out uint capacity);
                    var floats = new float[capacity / 4];
                    Marshal.Copy(data, floats, 0, floats.Length);
                    return floats;
                }
            }
            catch
            {
                var view = t.GetAsVectorView();
                var floats = new float[view.Count];
                for (int i = 0; i < floats.Length; i++) { floats[i] = view[i]; }
                return floats;
            }
        }

        // ── The recognizer's first-use download ─────────────────────────────────

        private static int _downloading;

        /// <summary>
        /// Fetches SFace into the models folder once, in the background (the user chose this, 2026-11-15: "fetch SFace on
        /// first use"). Through Windows' proxy like the speech models; resumable; kept only when its SHA-256 is the one
        /// published for the file. <paramref name="done"/> runs on the download's thread with whether it worked.
        /// </summary>
        public static void EnsureRecognizerDownloaded(Action<bool, string> done)
        {
            string path = RecognizerPath();
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length == RecognizerBytes) { done?.Invoke(true, "already there"); return; }
            }
            catch { }
            if (Interlocked.Exchange(ref _downloading, 1) != 0) { return; }
            Task.Run(() =>
            {
                bool ok = false;
                string why;
                try { ok = Download(path, out why); }
                catch (Exception ex) { why = ex.Message; }
                finally { Interlocked.Exchange(ref _downloading, 0); }
                try { done?.Invoke(ok, why); } catch { }
            });
        }

        /// <summary>True while the recognizer is being fetched.</summary>
        public static bool Downloading => Volatile.Read(ref _downloading) != 0;

        /// <summary>Bytes of the recognizer fetched so far in this download (for Live debug).</summary>
        public static long DownloadedBytes => Interlocked.Read(ref _downloadedBytes);
        private static long _downloadedBytes;

        private static bool Download(string finalPath, out string why)
        {
            string part = finalPath + ".part";
            long already = 0;
            try { var p = new FileInfo(part); if (p.Exists) { already = p.Length; } } catch { already = 0; }
            if (already > RecognizerBytes) { TryDelete(part); already = 0; }
            var request = (HttpWebRequest)WebRequest.Create(RecognizerUrl);
            request.Method = "GET";
            request.UserAgent = "Tempo/" + (UpdateChecker.CurrentVersion?.ToString() ?? "1.0");
            request.Timeout = 20000;
            request.ReadWriteTimeout = 120000;
            request.AllowAutoRedirect = true;        // Hugging Face redirects to its CDN
            request.Proxy = SystemProxy.Get();
            request.KeepAlive = false;
            if (already > 0 && already < RecognizerBytes) { try { request.AddRange(already); } catch { already = 0; } }
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
                        if (total > RecognizerBytes) { break; }
                    }
                }
            }
            var info = new FileInfo(part);
            if (!info.Exists || info.Length != RecognizerBytes)
            {
                why = "the download stopped at " + (info.Exists ? info.Length / 1048576 : 0) + " of " + RecognizerBytes / 1048576
                      + " MB — it carries on next time";
                if (info.Exists && info.Length > RecognizerBytes) { TryDelete(part); }
                return false;
            }
            string hash;
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(part))
            {
                hash = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            }
            if (hash != RecognizerSha256)
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
            try { _det?.Dispose(); } catch { }
            try { _rec?.Dispose(); } catch { }
            _det = null;
            _rec = null;
        }
    }
}
