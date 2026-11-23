using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Watches the video on screen and works out WHICH FACE is talking, so caption
    /// speaker labels can be driven by sight as well as sound.
    ///
    /// How it works, all on-device (nothing leaves the PC, no downloads):
    ///  1. A few times a second it captures the FOREGROUND window (that's where the
    ///     video is — the media detector already ties captions to it) at a small size.
    ///  2. Windows' built-in face detector (Windows.Media.FaceAnalysis, the same
    ///     on-device AI Photos uses) finds the faces in the frame.
    ///  3. Faces are tracked across frames by position, and for each one the MOUTH
    ///     REGION (lower-middle of the face box) is compared with the previous frame
    ///     — a talking mouth produces steady pixel motion there.
    ///  4. While the audio side hears speech, the face with clearly the most mouth
    ///     motion is the active speaker: <see cref="CurrentVisualSpeaker"/> (its
    ///     stable slot number) feeds the caption labeler, which prefers it over the
    ///     voice-only guess.
    ///
    /// Honest limits: it needs the speaker's face visible in the foreground window,
    /// mouths at very small sizes carry little signal, and scene cuts re-shuffle
    /// slots. It's a HINT that improves labels — not identification. Everything is
    /// best-effort: any failure just leaves the verdict at 0 and captions fall back
    /// to voice matching.
    /// </summary>
    public sealed class FaceSpeakerAnalyzer : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        // Ask DWM for the window's REAL rendered content — this is what makes
        // hardware-accelerated video (browsers, players) come out non-black, and it
        // works for windows that are BEHIND other windows.
        private const uint PW_RENDERFULLCONTENT = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>
        /// Optional: supplies the window that is currently PLAYING AUDIO (the media
        /// detector knows). When set and valid, faces are read from THAT window even
        /// while it sits behind others — so a video in a background browser tab, a
        /// photo viewer, any app at all gets analyzed. Falls back to the foreground
        /// window when unset/gone.
        /// </summary>
        public Func<IntPtr> PreferredWindow { get; set; }

        // ── Tunables ─────────────────────────────────────────────────────────
        // 640 (up from 480): a face that filled only ~22 analysis-pixels at 480 now
        // fills ~30 and clears MinFaceWidth — so people further from the camera get
        // analysed too, and every mouth region carries ~1.8× the pixels (steadier
        // motion signal). The OS detector absorbs the size easily at our frame rate.
        private const int FrameWidth = 640;          // analysis resolution
        private const int IntervalMs = 220;          // ~4.5 fps
        private const double SpeakMotion = 2.6;     // NET mouth-motion EMA that counts as talking
        private const double SpeakLead = 1.5;       // must lead other faces by this factor
        private const int SlotTimeoutMs = 3000;      // forget a face unseen this long
        // 6 (was 4): a panel of five — four guests and a moderator — left one person untracked in every wide shot
        // (round 26, NASA footage), the one most likely to be the person just seen in close-up.
        private const int MaxSlots = 6;
        // Faces narrower than this (analysis pixels) are too small to lip-read —
        // their mouth region is a handful of pixels of compression noise.
        private const int MinFaceWidth = 30;

        // ── Velocity coasting ────────────────────────────────────────────────
        // While a face is DETECTED its centre velocity is EMA'd; while it COASTS
        // the box DRIFTS along that velocity (decaying every frame) instead of
        // freezing — a walking person who turns their head keeps being tracked
        // where they actually are, not where they were when the detector last
        // saw them. Decay 0.85/frame halves the drift in ~4 frames, so the box
        // settles within a second instead of sailing off; the dead-zone keeps a
        // stationary head's box from wandering on sub-pixel detection jitter.
        private const double CoastVelocityDecay = 0.85;
        private const double CoastVelocityDeadZone = 0.2;  // px/frame — below this, don't drift

        // ── Static-face (poster/thumbnail) rejection ─────────────────────────
        // A face IMAGE — a video thumbnail, a poster on the set, a game UI
        // portrait — is detected and tracked exactly like a real head, and
        // compression noise in its mouth region can even win the talking
        // verdict. But a real head is never perfectly still (blinks, breathing,
        // grain tracking the lighting), while an image's WHOLE-BOX motion sits
        // at essentially zero. Whole-face motion staying under StaticMotionMax
        // for StaticAfterMs (and a minimum sample count, so a few slow idle
        // ticks can't rush the call) marks the slot Static: excluded from the
        // verdict and flagged in debug. Real motion clears it immediately.
        private const double StaticMotionMax = 0.5;   // mean |Δ|/px — under the video noise floor
        private const int StaticAfterMs = 4000;
        private const int StaticMinSamples = 10;

        // Whole-frame mean |Δ| above which the frame is a scene cut or hard camera
        // pan: normal video sits at 1–4, energetic motion 5–9, a cut 20–40. Motion
        // bookkeeping is skipped for that frame (see the guard in AnalyzeOneFrame).
        private const double CutMotion = 11.0;

        // Below this whole-frame mean |Δ| the picture is, for our purposes, IDENTICAL to
        // the previous one — a paused video, a still image, a static document. Real video
        // never sits this low; even a "still" shot carries sensor grain and compression
        // churn around 1–4. Deliberately well under the static-face threshold (0.5) so a
        // living but motionless scene is never mistaken for a frozen one.
        private const double FrozenFrameMotion = 0.12;

        private System.Threading.Timer _timer;
        private FaceDetector _detector;
        private volatile bool _running;
        private volatile int _visualSpeaker;         // 1-based slot; 0 = unknown
        private volatile int _faceCount;
        private int _tickGuard;

        private byte[] _prevGray;
        private int _prevW, _prevH;
        // Reusable per-frame buffers/bitmaps — reallocated only when the window size
        // changes. AnalyzeOneFrame used to allocate a ~230 KB gray[] and ~690 KB raw[]
        // (both over the 85 KB Large Object Heap threshold) plus two GDI+ bitmaps every
        // frame at ~6 fps — ~6 MB/s of LOH churn that forced recurring gen2 GCs whose
        // pauses show up directly as caption/audio latency.
        private byte[] _spareGray;         // recycled into _prevGray's old slot each frame
        private byte[] _rawBuf;
        private Bitmap _fullBmp;
        private Bitmap _frameBmp;
        private int _fullW, _fullH, _frameW, _frameH;

        /// <summary>
        /// How many recent frames the audio/mouth correlation looks at. At the analyzer's
        /// working cadence this is roughly two to three seconds — long enough to span
        /// several syllables and a pause, short enough to follow a change of speaker.
        /// </summary>
        private const int CorrWindow = 24;

        /// <summary>
        /// Correlation is only meaningful once the audio has actually VARIED across the
        /// window. Steady silence and steady noise both correlate with nothing, and
        /// dividing by their near-zero spread manufactures confident nonsense.
        /// </summary>
        private const double MinAudioSpread = 0.04;

        private sealed class Slot
        {
            public double X, Y, W, H;                // smoothed face box (analysis coords)
            public double MotionEma;

            /// <summary>
            /// Recent net mouth-motion samples, paired by index with <see cref="_audioRing"/>.
            /// Used to ask whether this mouth moves WHEN THE SOUND DOES, rather than just
            /// whether it moves a lot.
            /// </summary>
            public readonly double[] MotionRing = new double[CorrWindow];
            public int RingCount;

            /// <summary>Last computed audio/mouth correlation, -1..1. NaN until there is enough.</summary>
            public double AudioCorr = double.NaN;
            public DateTime LastSeenUtc;
            public bool Alive;
            // First motion sample after (re)birth SEEDS the EMA instead of easing in
            // from 0 — the talking verdict lands ~1 s sooner after every scene cut.
            public bool FreshMotion;
            // Matched by the detector THIS frame? When false the slot is COASTING:
            // the OS detector only sees near-frontal faces, so a head turned to the
            // side / looking down / tilted up vanishes from detection while still
            // being right there on screen. Coasting keeps the slot pinned at its
            // last box and keeps measuring mouth-region motion, so the speaker
            // verdict survives the whole head turn instead of dying after 3 s.
            public bool MatchedThisFrame;
            // Was the slot detector-matched LAST frame too? Centre velocity only
            // accumulates across CONSECUTIVE detections — the correction jump when
            // a face re-appears after coasting is tracking error, not movement,
            // and must never feed the velocity estimate.
            public bool MatchedPrevFrame;
            // Smoothed centre velocity (analysis px/frame), measured only while
            // DETECTED. Drives the coasting drift above; decays while coasting so
            // it can never feed back into itself.
            public double VX, VY;
            // Slow EMA of motion over the WHOLE face box — the static-image
            // detector. Seeded from the first sample (like FreshMotion) so a live
            // face's easing-in-from-0 can't read as "quiet" and start the clock.
            public double TotalMotionEma;
            public bool FreshTotal;
            // When the whole-face motion first dropped below the static
            // threshold; MinValue = currently moving. Static only after a
            // sustained quiet stretch, never off one calm frame.
            public DateTime QuietSinceUtc;
            public int QuietSamples;
            // A poster / thumbnail / UI portrait, not a living head: excluded
            // from the talking verdict, flagged in DebugDetail.
            public bool IsStatic;
            // A face with no colour in it — a drawing on a black-and-white page (manga, a comic), or a black-and-white
            // picture: not a person on camera. Excluded like a static one, even while the page scrolls or zooms (round 27:
            // the user's OPM manga review, whose drawn characters were taken for the unseen reviewer's face).
            public bool IsDrawn;
            public double ChromaEma = -1;       // mean max(R,G,B)−min(R,G,B) over the face's middle, smoothed; -1 = not yet
            public int ChromaSamples;
            // When the CURRENT occupant took this slot. The slot index IS the face's
            // speaker number, and an expired slot is handed to the next new face — so
            // someone arriving after a speaker left inherits that speaker's number.
            // Recording the takeover lets Live debug show when that happened.
            public DateTime OccupiedSinceUtc;
            // Who is in the slot: a number never reused, unlike the slot. What the caption
            // speakers are linked to — a slot index would hand a departed person's speaker
            // to whoever took their place.
            public long OccupantId;
            public DateTime ThumbUtc;           // when this occupant's kept picture was taken
            public DateTime ThumbTriedUtc;      // when a picture was last tried (kept or not)
            public double ThumbScore;           // how good the kept picture is (see MaybeTakeThumbnail)
            // What the occupant looks like, close up and small (see Utils/FaceAppearance.cs) — the two
            // are kept apart because one person's close-up and wide-shot looks are not comparable.
            public FaceLook BigLook, SmallLook;
            // A face not named yet (OccupantId 0): its first looks, until there are enough to tell
            // whether it is someone seen before (see ResolvePendingFaces).
            public FaceLook PendingLook;
            public int PendingLooks, PendingSeen;
            // The face AI (round 26, Utils/FaceAi.cs): the occupant's fingerprint — a running mean of SFace prints, with
            // how many went into it and how wide (analysis px) the face was — and where its mouth and eyes are, relative
            // to the box (from YuNet's landmarks), with when they were found.
            public float[] Print;
            public int PrintCount;
            public float PrintSize;
            public DateTime PrintUtc;
            public RectangleF MouthRel, UpperRel;
            public DateTime MarksUtc;
        }

        /// <summary>
        /// Someone who was on screen and has gone: kept so that when they come back — after a camera
        /// cut, a wide shot, the other person's close-up — they get their occupant id back, and with
        /// it their speaker, name and picture. Memory only; forgotten on Stop and by ForgetFaces.
        /// </summary>
        private sealed class KnownFace
        {
            public long OccupantId;
            public FaceLook BigLook, SmallLook;
            public DateTime LastSeenUtc;
            public float[] Print;
            public int PrintCount;
            public float PrintSize;
        }

        // ── The face AI (round 26, 2026-11-15) ──────────────────────────────────
        // One man cut from a wide shot to a close-up became TWO speakers on the user's Speakers card (22.png): the wide
        // shot's face kept "coasting" for up to 3 s after the cut, the close-up was compared only with people who had
        // LEFT (the gallery) — so it was a stranger — and with both "on screen" and both linked to his voice, the linker
        // took them for two people sharing a voice. Fixed three ways: faces not found again at a cut are retired there
        // (no coasting across a cut); a new face is compared with faces still coasting too, and takes the occupant over
        // when it is the same person; and the linker only splits faces DETECTED together that are not the same person.
        //
        // "The same person" now comes from SFace fingerprints wherever there are some (Utils/FaceAi.cs). Measured on
        // public-domain NASA footage (four astronauts in one red shirt before one backdrop, plus moderator and reporters:
        // scratchpad/faceai, 324 faces hand-labelled): close-up against close-up, one person 0.52–1.00 (median 0.84),
        // two people at most 0.39; close-up against a ~17 px wide-shot face, one person ≥ 0.40 in 87 % of pairs, two
        // people in 0.14 %; two tiny faces 78 % / 0.34 %. So: the same person from 0.40; different people only below
        // 0.30 and only between faces at least PrintBigFace wide — a tiny face's print says little against.
        // Test switch (scratchpad/facetrack: the behaviour before round 26, faces coasting across a cut).
        internal static bool RetireAtCut = true;
        private FaceAi _ai;
        private bool _aiTried;
        private volatile bool _recognizerArrived;
        private DateTime _lastAiPassUtc = DateTime.MinValue;
        private bool _aiWantSoon;
        private byte[] _fullRaw;
        private int _fullRawStride;
        private volatile string _aiState = "";
        private volatile int _aiPasses, _printsTaken, _ghostMerges, _printReids, _printChanges;
        private sealed class OccupantPrint
        {
            public float[] Print;
            public float Size;
        }
        // Every occupant's latest fingerprint, for SamePerson (the linker asks about faces that may have left).
        private readonly Dictionary<long, OccupantPrint> _prints = new Dictionary<long, OccupantPrint>();
        private const int PrintsKept = 64;
        private const int AiPassMs = 900;           // landmarks and prints about once a second
        private const int AiSoonMs = 250;           // ...sooner when a new face is waiting for a print
        private const int PrintRefreshMs = 2000;
        internal const float PrintSame = 0.40f;
        internal const float PrintDifferent = 0.30f;
        private const float PrintBigFace = 60f;     // analysis px
        private const float AiMinScore = 0.6f;
        private const double MarksFreshSec = 3.0;
        // A face whose new print disagrees this strongly with its occupant's, both big enough to judge, is somebody else:
        // a cut between two people framed alike that the colours took for one.
        private const float PrintChanged = 0.25f;

        private readonly List<KnownFace> _knownFaces = new List<KnownFace>();
        private const int KnownFacesKept = 24;
        // A new face is named after this many looks (~0.5 s): one frame can catch a blink, motion blur
        // or a face half into the frame.
        private const int LooksToName = 3;
        // ...or after this many detections without enough usable looks (a face at the frame's edge).
        private const int SeenToName = 8;
        // At a cut, a face that is this much bigger or smaller than the one the tracker had there is a
        // new shot, not the same face.
        private const double CutSizeChange = 1.5;
        private volatile int _reidentified, _changedAtCut;
        // Test seam (scratchpad/facereid replays the same footage with and without).
        private bool _reidentify = true;

        /// <summary>Faces recognised as someone seen earlier this session and given their id back.</summary>
        public int Reidentified => _reidentified;

        /// <summary>Times a different person turned up where a tracked face was (a cut between look-alike framings).</summary>
        public int ChangedAtCut => _changedAtCut;

        /// <summary>People seen this session who are off screen now but would be recognised on return.</summary>
        public int KnownFaceCount { get { lock (_slotLock) { return _knownFaces.Count; } } }

        /// <summary>Forgets everyone seen so far (new audio: new people).</summary>
        public void ForgetFaces()
        {
            lock (_slotLock)
            {
                _knownFaces.Clear();
                // Their fingerprints too (the faces on screen print again within a couple of seconds).
                _prints.Clear();
            }
        }

        private static long _nextOccupant;

        // A small picture of each recent occupant's face, for the Captions tab to show beside
        // "Speaker N". Memory only, never written anywhere; dropped on Stop().
        private readonly Dictionary<long, Bitmap> _thumbs = new Dictionary<long, Bitmap>();
        private readonly Dictionary<long, double> _thumbScores = new Dictionary<long, double>();
        private readonly List<long> _thumbOrder = new List<long>();
        // 96 px, cut from the FULL-size capture. The first version cut 48 px from the 640-wide
        // analysis frame, where a face is often 30–60 px across — so every picture was an upscaled
        // smudge. The window is captured at its real size anyway, before being shrunk for the
        // detector; the picture is now taken from that.
        private const int ThumbSize = 96;
        private const int ThumbsKept = 24;              // as many as the people it can recognise
        private const int ThumbRefreshMs = 1500;
        // Keep the best picture rather than the latest: a new one replaces it only when it is at least
        // this good (bigger and/or sharper), or when the kept one is this old — people change angle.
        private const double ThumbKeepBetterShare = 0.9;
        private const int ThumbMaxAgeMs = 15000;

        private long _speakerOccupant;                   // Interlocked; 0 = nobody confidently talking
        private volatile long[] _visibleOccupants = new long[0];

        /// <summary>The occupant id of the face visibly talking (see <see cref="CurrentVisualSpeaker"/>), or 0.</summary>
        public long CurrentSpeakerOccupant => System.Threading.Interlocked.Read(ref _speakerOccupant);

        /// <summary>Occupant ids of the living (not static) faces on screen in the last frame.</summary>
        public long[] VisibleOccupants => _visibleOccupants;

        /// <summary>
        /// Occupant ids of the faces the detector actually FOUND in the last frame — not the ones coasting on their last
        /// box (a head turned away, or a face that has just gone). Two faces in here at once are two faces on screen.
        /// </summary>
        public long[] DetectedOccupants => _detectedOccupants;
        private volatile long[] _detectedOccupants = new long[0];

        /// <summary>
        /// Are these two occupants the same person, by their face fingerprints (Utils/FaceAi.cs)? +1 the same (cosine ≥
        /// PrintSame), −1 different (below PrintDifferent, both faces big enough to judge), 0 cannot say (no prints yet,
        /// a face too small, or in between). Thread-safe.
        /// </summary>
        public int SamePerson(long a, long b)
        {
            if (a <= 0 || b <= 0) { return 0; }
            if (a == b) { return 1; }
            lock (_slotLock)
            {
                if (!_prints.TryGetValue(a, out OccupantPrint pa) || !_prints.TryGetValue(b, out OccupantPrint pb)) { return 0; }
                float sim = FaceAi.Similarity(pa.Print, pb.Print);
                if (float.IsNaN(sim)) { return 0; }
                if (sim >= PrintSame) { return 1; }
                if (sim < PrintDifferent && pa.Size >= PrintBigFace && pb.Size >= PrintBigFace) { return -1; }
                return 0;
            }
        }

        /// <summary>The face AI's state in a line, for Live debug.</summary>
        public string FaceAiState
        {
            get
            {
                string s = _aiState;
                if (FaceAi.Downloading) { s += " · fingerprint model downloading (" + FaceAi.DownloadedBytes / 1048576 + " of 37 MB)"; }
                return s + (_aiPasses > 0 ? " · " + _aiPasses + " looks, " + _printsTaken + " prints, " + _ghostMerges
                            + " ghost(s) merged, " + _printReids + " re-identified by print, " + _printChanges + " identity change(s) caught" : "");
            }
        }

        /// <summary>
        /// A copy of the latest picture of <paramref name="occupant"/>'s face, or null. The caller
        /// owns (and disposes) the copy.
        /// </summary>
        public Bitmap GetFaceThumbnail(long occupant)
        {
            if (occupant <= 0) { return null; }
            lock (_slotLock)
            {
                try
                {
                    return _thumbs.TryGetValue(occupant, out Bitmap b) && b != null ? new Bitmap(b) : null;
                }
                catch { return null; }
            }
        }

        /// <summary>
        /// A copy of the best picture among <paramref name="occupants"/> — one person's close-up and
        /// wide-shot faces — or null. Best = the largest and sharpest (see MaybeTakeThumbnail). The
        /// caller owns the copy.
        /// </summary>
        public Bitmap GetBestFaceThumbnail(IEnumerable<long> occupants)
        {
            if (occupants == null) { return null; }
            lock (_slotLock)
            {
                long best = 0;
                double bestScore = double.MinValue;
                foreach (long o in occupants)
                {
                    if (o > 0 && _thumbs.ContainsKey(o) && _thumbScores.TryGetValue(o, out double sc) && sc > bestScore)
                    {
                        bestScore = sc;
                        best = o;
                    }
                }
                try { return best > 0 && _thumbs[best] != null ? new Bitmap(_thumbs[best]) : null; }
                catch { return null; }
            }
        }

        /// <summary>
        /// Takes a square picture around <paramref name="s"/>'s face from this frame, at most every
        /// ThumbRefreshMs. Called under _slotLock with the frame bitmap unlocked.
        /// </summary>
        private void MaybeTakeThumbnail(Slot s, DateTime now)
        {
            if (s.OccupantId <= 0 || s.W < MinFaceWidth) { return; }
            // Rate-limited on the last TRY: a rejected worse picture must not make the next frame
            // try again (a 96 px high-quality resample of a full-size frame, every frame).
            if (s.ThumbTriedUtc != DateTime.MinValue && (now - s.ThumbTriedUtc).TotalMilliseconds < ThumbRefreshMs) { return; }
            s.ThumbTriedUtc = now;
            try
            {
                // From the full-size capture when there is one. The analysis frame is the same
                // picture shrunk to 640 wide, so the face box scales straight across (per axis: a
                // very short window's frame is held at 64 high, which bends the ratio).
                bool full = _fullBmp != null && _fullW > 0 && _fullH > 0 && _frameW > 0 && _frameH > 0;
                Bitmap source = full ? _fullBmp : _frameBmp;
                if (source == null) { return; }
                double sx = full ? _fullW / (double)_frameW : 1.0;
                double sy = full ? _fullH / (double)_frameH : 1.0;
                int srcW = full ? _fullW : _frameW;
                int srcH = full ? _fullH : _frameH;

                // A square a little wider than the detector's box, which cuts off the chin and the hair.
                double side = Math.Max(s.W * sx, s.H * sy) * 1.3;
                double cx = (s.X + s.W / 2) * sx, cy = (s.Y + s.H / 2) * sy;
                var src = new RectangleF((float)(cx - side / 2), (float)(cy - side / 2), (float)side, (float)side);
                src.Intersect(new RectangleF(0, 0, srcW, srcH));
                // A face at the window's edge clips the square into a strip; square it again rather
                // than stretch the face.
                float sq = Math.Min(src.Width, src.Height);
                src = new RectangleF(src.X + (src.Width - sq) / 2, src.Y + (src.Height - sq) / 2, sq, sq);
                if (src.Width < 8 || src.Height < 8) { return; }

                var thumb = new Bitmap(ThumbSize, ThumbSize, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(thumb))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(source, new Rectangle(0, 0, ThumbSize, ThumbSize), src, GraphicsUnit.Pixel);
                }

                // How good a picture: bigger (more real pixels behind the 96) and sharper (no motion
                // blur mid-sentence) is better. A worse one does not replace a better one unless the
                // kept picture has grown old.
                double score = Math.Min(src.Width, 400) * (0.5 + Sharpness(thumb));
                bool stale = s.ThumbUtc == DateTime.MinValue || (now - s.ThumbUtc).TotalMilliseconds >= ThumbMaxAgeMs;
                bool have = _thumbs.ContainsKey(s.OccupantId);
                // Seen just now: the newest in the keeping order, whether or not this picture replaces the kept one.
                _thumbOrder.Remove(s.OccupantId);
                _thumbOrder.Add(s.OccupantId);
                if (have && !stale && score < s.ThumbScore * ThumbKeepBetterShare)
                {
                    thumb.Dispose();
                    return;
                }
                if (_thumbs.TryGetValue(s.OccupantId, out Bitmap old)) { try { old?.Dispose(); } catch { } }
                _thumbs[s.OccupantId] = thumb;
                _thumbScores[s.OccupantId] = score;
                s.ThumbUtc = now;
                s.ThumbScore = score;
                TrimPictures(_thumbOrder, _thumbs, _thumbScores, _keepPictures, ThumbsKept);
            }
            catch { }
        }

        // The faces speakers are linked to (MainForm passes them about once a second): their pictures are the last to go.
        private volatile HashSet<long> _keepPictures = new HashSet<long>();

        /// <summary>
        /// The faces a speaker on the Speakers card is linked to, so their pictures outlast those of passers-by. Safe from any
        /// thread (the set is replaced whole, never changed).
        /// </summary>
        public void KeepPicturesOf(IEnumerable<long> occupants)
        {
            _keepPictures = occupants != null ? new HashSet<long>(occupants) : new HashSet<long>();
        }

        /// <summary>
        /// Drops pictures beyond <paramref name="cap"/>: the face seen longest ago first, but not one a speaker is linked to
        /// (<paramref name="keep"/>) while another can go. Round 27: they went by FIRST-seen order, so in a reaction stream —
        /// dozens of faces in the video, cut after cut — the streamer's and the early speakers' pictures were the first
        /// dropped, and a speaker on the card read "voice + face" with no picture (the user's 23.png).
        /// </summary>
        internal static void TrimPictures(List<long> order, Dictionary<long, Bitmap> thumbs, Dictionary<long, double> scores,
                                          HashSet<long> keep, int cap)
        {
            while (order.Count > cap)
            {
                int at = 0;
                if (keep != null && keep.Count > 0)
                {
                    at = order.FindIndex(o => !keep.Contains(o));
                    if (at < 0) { at = 0; }   // every one is a speaker's: the longest unseen goes
                }
                long gone = order[at];
                order.RemoveAt(at);
                if (thumbs.TryGetValue(gone, out Bitmap b)) { try { b?.Dispose(); } catch { } }
                thumbs.Remove(gone);
                scores.Remove(gone);
            }
        }

        // ── Recognising returning faces ─────────────────────────────────────
        // A face that leaves the screen for longer than SlotTimeoutMs used to come back as a stranger:
        // a new occupant, with no speaker, name or picture until it had talked long enough to be linked
        // again. And a cut between two close-ups framed alike handed the second person the first one's
        // occupant, because tracking went by position alone. Both are answered by what faces LOOK like
        // (Utils/FaceAppearance.cs, tuned and checked on real footage in scratchpad/facereid).

        private FaceLook MeasureLook(byte[] gray, int w, int hgt, double fx, double fy, double fw, double fh)
        {
            try
            {
                return FaceAppearance.Measure(_rawBuf, _rawStride, gray, w, hgt,
                                              new RectangleF((float)fx, (float)fy, (float)fw, (float)fh));
            }
            catch { return null; }
        }

        /// <summary>Folds a look into the close-up or small view it belongs to.</summary>
        private static void AddLook(ref FaceLook big, ref FaceLook small, FaceLook look)
        {
            if (look == null) { return; }
            if (FaceAppearance.IsBig(look))
            {
                if (big == null) { big = look.Clone(); } else { FaceAppearance.Accumulate(big, look); }
            }
            else
            {
                if (small == null) { small = look.Clone(); } else { FaceAppearance.Accumulate(small, look); }
            }
        }

        /// <summary>
        /// How surely <paramref name="probe"/> is the person with these views (NaN = not them): compared
        /// with the view of its own size when there is one, else — under the stricter rule — the other.
        /// </summary>
        private static double ScoreAgainst(FaceLook big, FaceLook small, FaceLook probe)
        {
            bool isBig = FaceAppearance.IsBig(probe);
            FaceLook same = isBig ? big : small;
            if (same != null) { return FaceAppearance.MatchScore(same, probe); }
            FaceLook other = isBig ? small : big;
            return other != null ? FaceAppearance.MatchScore(other, probe) : double.NaN;
        }

        /// <summary>
        /// At a cut, or when a lost face is found again at the same place: is it still the person the
        /// slot holds? A face at a very different size is a different shot, whoever it is. With nothing
        /// yet to compare at this size, position is all there is, and the track is kept.
        /// </summary>
        private static bool StillSamePerson(Slot s, FaceLook look, double fw)
        {
            double ratio = fw / Math.Max(1.0, s.W);
            if (ratio > CutSizeChange || ratio < 1.0 / CutSizeChange) { return false; }
            FaceLook view = FaceAppearance.IsBig(look) ? s.BigLook : s.SmallLook;
            return view == null || !double.IsNaN(FaceAppearance.MatchScore(view, look));
        }

        /// <summary>Ends a slot's occupancy; the occupant is remembered in case they come back.</summary>
        private void RetireOccupant(Slot s, DateTime now)
        {
            if (s.OccupantId > 0 && (s.BigLook != null || s.SmallLook != null || s.Print != null))
            {
                _knownFaces.RemoveAll(k => k.OccupantId == s.OccupantId);
                _knownFaces.Add(new KnownFace
                {
                    OccupantId = s.OccupantId,
                    BigLook = s.BigLook,
                    SmallLook = s.SmallLook,
                    LastSeenUtc = s.LastSeenUtc,
                    Print = s.Print,
                    PrintCount = s.PrintCount,
                    PrintSize = s.PrintSize,
                });
                while (_knownFaces.Count > KnownFacesKept)
                {
                    int oldest = 0;
                    for (int i = 1; i < _knownFaces.Count; i++)
                    {
                        if (_knownFaces[i].LastSeenUtc < _knownFaces[oldest].LastSeenUtc) { oldest = i; }
                    }
                    _knownFaces.RemoveAt(oldest);
                }
            }
            s.Alive = false;
            s.MotionEma = 0;
            s.PendingLook = null;
            s.PendingLooks = 0;
            s.PendingSeen = 0;
            // The print went to the gallery with the person; the slot's next occupant starts without one.
            s.Print = null;
            s.PrintCount = 0;
            s.PrintSize = 0;
            s.MarksUtc = DateTime.MinValue;
        }

        /// <summary>
        /// Names the new faces that have been looked at enough: each gets back the id of someone seen
        /// earlier who passes <see cref="FaceAppearance.MatchScore"/> — best pairings first, each
        /// remembered person to one face at most — or else a new one.
        /// </summary>
        private void ResolvePendingFaces(DateTime now)
        {
            // With fingerprints on, a new face waits for its first print (asked for at once: _aiWantSoon) — up to
            // SeenToName detections, as for a face with too few usable looks.
            FaceAi ai = _ai;
            bool printsOn = ai != null && ai.CanRecognize;
            List<Slot> ready = null;
            foreach (var s in _slots)
            {
                if (!s.Alive || s.OccupantId != 0) { continue; }
                bool looksDone = s.PendingLooks >= LooksToName && (!printsOn || s.Print != null);
                if (looksDone || s.PendingSeen >= SeenToName)
                {
                    (ready ?? (ready = new List<Slot>())).Add(s);
                }
            }
            if (ready == null) { return; }

            // Who each new face might be: someone who has left (the gallery), or someone still COASTING on screen on the
            // box they had — the same person found again in another place or shot, beside their own ghost.
            var options = new List<(double Score, Slot S, KnownFace K, Slot Ghost)>();
            foreach (Slot s in ready)
            {
                foreach (KnownFace k in _knownFaces)
                {
                    double score = IdentityScore(s, k.Print, k.PrintSize, k.BigLook, k.SmallLook);
                    if (!double.IsNaN(score)) { options.Add((score, s, k, null)); }
                }
                foreach (Slot g in _slots)
                {
                    if (g == s || !g.Alive || g.MatchedThisFrame || g.OccupantId <= 0) { continue; }
                    double score = IdentityScore(s, g.Print, g.PrintSize, g.BigLook, g.SmallLook);
                    if (!double.IsNaN(score)) { options.Add((score, s, null, g)); }
                }
            }
            options.Sort((a, b) => b.Score.CompareTo(a.Score));
            var named = new HashSet<Slot>();
            var claimed = new HashSet<object>();
            foreach (var o in options)
            {
                object who = (object)o.K ?? o.Ghost;
                if (named.Contains(o.S) || claimed.Contains(who)) { continue; }
                named.Add(o.S);
                claimed.Add(who);
                Slot s = o.S;
                if (o.K != null)
                {
                    s.OccupantId = o.K.OccupantId;
                    s.BigLook = o.K.BigLook;
                    s.SmallLook = o.K.SmallLook;
                    TakePrint(s, o.K.Print, o.K.PrintCount, o.K.PrintSize, o.Score >= IdentityByPrint);
                    _knownFaces.Remove(o.K);
                    _reidentified++;
                    if (o.Score >= IdentityByPrint) { _printReids++; }
                }
                else
                {
                    // The ghost's identity moves to the face found again; the ghost is gone (not retired to the gallery:
                    // that person is on screen).
                    Slot g = o.Ghost;
                    s.OccupantId = g.OccupantId;
                    s.BigLook = g.BigLook;
                    s.SmallLook = g.SmallLook;
                    TakePrint(s, g.Print, g.PrintCount, g.PrintSize, o.Score >= IdentityByPrint);
                    s.ThumbUtc = g.ThumbUtc;
                    s.ThumbScore = g.ThumbScore;
                    g.Alive = false;
                    g.OccupantId = 0;
                    g.MotionEma = 0;
                    g.Print = null;
                    g.PrintCount = 0;
                    _ghostMerges++;
                }
                AddLook(ref s.BigLook, ref s.SmallLook, s.PendingLook);
            }
            foreach (Slot s in ready)
            {
                if (!named.Contains(s))
                {
                    s.OccupantId = System.Threading.Interlocked.Increment(ref _nextOccupant);
                    s.BigLook = null;
                    s.SmallLook = null;
                    AddLook(ref s.BigLook, ref s.SmallLook, s.PendingLook);
                }
                if (s.Print != null) { RememberPrint(s.OccupantId, s.Print, s.PrintSize); }
                s.PendingLook = null;
                s.PendingLooks = 0;
                s.PendingSeen = 0;
            }
        }

        // An identity score at or above this came from fingerprints (they outrank colours: see IdentityScore).
        private const double IdentityByPrint = 10.0;

        /// <summary>
        /// How surely a new face (<paramref name="s"/>, its pending look and print) is the person with this print and these
        /// looks — NaN when not them. Fingerprints decide when both have one and the answer is clear: the same person
        /// scores IdentityByPrint + cosine (above every colour score), different people are NaN. In between, or without
        /// prints, the colours decide as they did (FaceAppearance.MatchScore).
        /// </summary>
        private static double IdentityScore(Slot s, float[] print, float printSize, FaceLook big, FaceLook small)
        {
            if (s.Print != null && print != null)
            {
                float sim = FaceAi.Similarity(s.Print, print);
                if (!float.IsNaN(sim))
                {
                    if (sim >= PrintSame) { return IdentityByPrint + sim; }
                    // Between faces big enough to print well the print has the last word both ways: one person's
                    // close-ups score 0.52 and up, so under PrintSame it is somebody else, whatever the colours say (the
                    // same red shirt before the same backdrop matched four people as one).
                    if (s.PrintSize >= PrintBigFace && printSize >= PrintBigFace) { return double.NaN; }
                    if (sim < PrintDifferent && (s.PrintSize >= PrintBigFace || printSize >= PrintBigFace)) { return double.NaN; }
                }
            }
            // No prints to go by, or a tiny face's: the colours, as before.
            return s.PendingLook != null ? ScoreAgainst(big, small, s.PendingLook) : double.NaN;
        }

        /// <summary>
        /// The print a re-identified face carries on: both blended when the PRINTS said it is the same person
        /// (<paramref name="byPrint"/>); otherwise never blended — the better one kept (the bigger face's) — since a match
        /// made on colours may be somebody else, and a blend of two people matches everyone.
        /// </summary>
        private static void TakePrint(Slot s, float[] print, int count, float size, bool byPrint)
        {
            if (print == null) { return; }
            if (byPrint || s.Print == null) { MergePrint(s, print, count, size); return; }
            if (size > s.PrintSize)
            {
                s.Print = (float[])print.Clone();
                s.PrintCount = Math.Max(1, count);
                s.PrintSize = size;
            }
        }

        /// <summary>Folds another print of the same person (a gallery entry's, a ghost's) into the slot's own.</summary>
        private static void MergePrint(Slot s, float[] print, int count, float size)
        {
            if (print == null) { return; }
            if (s.Print == null)
            {
                s.Print = (float[])print.Clone();
                s.PrintCount = Math.Max(1, count);
                s.PrintSize = size;
                return;
            }
            // Weighted by how many prints each holds, back to unit length.
            int a = Math.Max(1, s.PrintCount), b = Math.Max(1, count);
            var m = new float[s.Print.Length];
            double sq = 0;
            for (int i = 0; i < m.Length && i < print.Length; i++)
            {
                m[i] = (s.Print[i] * a + print[i] * b) / (a + b);
                sq += m[i] * (double)m[i];
            }
            float inv = sq > 0 ? (float)(1.0 / Math.Sqrt(sq)) : 0f;
            for (int i = 0; i < m.Length; i++) { m[i] *= inv; }
            s.Print = m;
            s.PrintCount = Math.Min(40, a + b);
            s.PrintSize = Math.Max(s.PrintSize, size);
        }

        /// <summary>Keeps <paramref name="occupant"/>'s latest print for SamePerson (bounded; living faces are kept).</summary>
        private void RememberPrint(long occupant, float[] print, float size)
        {
            if (occupant <= 0 || print == null) { return; }
            _prints[occupant] = new OccupantPrint { Print = print, Size = size };
            if (_prints.Count <= PrintsKept) { return; }
            var living = new HashSet<long>();
            foreach (var s in _slots) { if (s.Alive && s.OccupantId > 0) { living.Add(s.OccupantId); } }
            long drop = 0;
            foreach (long k in _prints.Keys)
            {
                if (!living.Contains(k) && (drop == 0 || k < drop)) { drop = k; }   // the oldest id that has left
            }
            if (drop != 0) { _prints.Remove(drop); }
        }

        /// <summary>
        /// Ends a slot's occupant and starts it again as a new face waiting to be named, on the same box — what the
        /// tracker does for a new face, minus the position (ApplyFaceAi: the print says someone else is in it).
        /// </summary>
        private void RestartAsNewFace(Slot s, DateTime now)
        {
            RetireOccupant(s, now);
            s.Alive = true;
            s.MatchedThisFrame = true;
            s.LastSeenUtc = now;
            s.OccupiedSinceUtc = now;
            s.OccupantId = 0;
            s.BigLook = null; s.SmallLook = null;
            s.PendingLook = null; s.PendingLooks = 0; s.PendingSeen = 0;
            s.ThumbUtc = DateTime.MinValue; s.ThumbTriedUtc = DateTime.MinValue; s.ThumbScore = 0;
            s.MotionEma = 0; s.FreshMotion = true;
            s.TotalMotionEma = 0; s.FreshTotal = true;
            s.QuietSinceUtc = DateTime.MinValue; s.QuietSamples = 0;
            s.IsStatic = false;
            s.IsDrawn = false; s.ChromaEma = -1; s.ChromaSamples = 0;
            s.Print = null; s.PrintCount = 0; s.PrintSize = 0;
            s.MarksUtc = DateTime.MinValue;
        }

        // ── Drawn faces (round 27) ────────────────────────────────────────────────────────────────────────────────────
        // A real face on camera always carries colour — skin, lips, eyes, the light on them: 15–80 of max−min(R,G,B) even
        // under cool or faded grading. A face drawn on a black-and-white page (manga, a comic) or a black-and-white
        // picture has next to none, compression noise only. Under DrawnChromaMax for DrawnMinSamples detections = drawn;
        // colour over DrawnChromaClear gives it back (a cut to a colour shot on the same spot). Faces too dark to judge
        // (mean luma under DrawnDarkLuma) are not measured either way.
        internal const double DrawnChromaMax = 8, DrawnChromaClear = 12;
        private const int DrawnMinSamples = 3;
        internal const double DrawnDarkLuma = 25;
        private bool _drawnLogged;

        /// <summary>Updates <see cref="Slot.IsDrawn"/> from this frame's colour in the face's middle.</summary>
        private void NoteFaceColour(Slot s)
        {
            try
            {
                byte[] raw = _rawBuf;
                if (raw == null || _rawStride <= 0) { return; }
                double chroma = MeanChroma(raw, _rawStride, _frameW, _frameH, s.X, s.Y, s.W, s.H, out double luma);
                if (chroma < 0 || luma < DrawnDarkLuma) { return; }
                s.ChromaEma = s.ChromaEma < 0 ? chroma : s.ChromaEma * 0.75 + chroma * 0.25;
                s.ChromaSamples++;
                if (!s.IsDrawn && s.ChromaSamples >= DrawnMinSamples && s.ChromaEma < DrawnChromaMax)
                {
                    s.IsDrawn = true;
                    if (!_drawnLogged)
                    {
                        _drawnLogged = true;
                        Logger.Info("[Face] a face with no colour in it (" + s.ChromaEma.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                                    + ") — a drawing on a black-and-white page (manga, a comic) or a black-and-white picture: "
                                    + "left out of the speakers, like a still picture.");
                    }
                }
                else if (s.IsDrawn && s.ChromaEma > DrawnChromaClear)
                {
                    s.IsDrawn = false;
                }
            }
            catch (Exception ex) { Logger.Swallow("NoteFaceColour", ex); }
        }

        /// <summary>
        /// Mean max(R,G,B)−min(R,G,B) over the middle 60 % of a box in a 24-bit BGR frame (every other pixel), and its mean
        /// luma; -1 when the box is too small or off the frame. Internal for the harness.
        /// </summary>
        internal static double MeanChroma(byte[] bgr, int stride, int frameW, int frameH, double x, double y, double w, double h,
                                          out double meanLuma)
        {
            meanLuma = 0;
            int x0 = (int)Math.Max(0, x + w * 0.2), x1 = (int)Math.Min(frameW, x + w * 0.8);
            int y0 = (int)Math.Max(0, y + h * 0.2), y1 = (int)Math.Min(frameH, y + h * 0.8);
            if (x1 - x0 < 4 || y1 - y0 < 4 || bgr == null) { return -1; }
            double sumChroma = 0, sumLuma = 0;
            int n = 0;
            for (int yy = y0; yy < y1; yy += 2)
            {
                int row = yy * stride;
                for (int xx = x0; xx < x1; xx += 2)
                {
                    int p = row + xx * 3;
                    if (p + 2 >= bgr.Length) { break; }
                    int b = bgr[p], g = bgr[p + 1], r = bgr[p + 2];
                    int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                    sumChroma += mx - mn;
                    sumLuma += (r * 77 + g * 150 + b * 29) >> 8;
                    n++;
                }
            }
            if (n == 0) { return -1; }
            meanLuma = sumLuma / n;
            return sumChroma / n;
        }

        // The mouth and upper-face windows when there are no landmarks (a fixed slice of the detector's box, as before).
        private static readonly RectangleF DefaultMouth = RectangleF.FromLTRB(0.25f, 0.62f, 0.75f, 0.95f);
        private static readonly RectangleF DefaultUpper = RectangleF.FromLTRB(0.25f, 0.12f, 0.75f, 0.45f);

        /// <summary>
        /// Where to measure a face's mouth motion and the motion it is compared against, relative to its box: around the
        /// mouth corners and under the eyes that YuNet found (fresh within MarksFreshSec) — a turned head's mouth is not
        /// in the middle of its box — or the fixed slice of before.
        /// </summary>
        private static void MouthAndUpper(Slot s, DateTime now, out RectangleF mouth, out RectangleF upper)
        {
            bool fresh = s.MouthRel.Width > 0 && s.MarksUtc > DateTime.MinValue && (now - s.MarksUtc).TotalSeconds < MarksFreshSec;
            mouth = fresh ? s.MouthRel : DefaultMouth;
            upper = fresh ? s.UpperRel : DefaultUpper;
        }

        /// <summary>The mouth and upper-face windows from YuNet's landmarks, relative to the slot's (smoothed) box.</summary>
        private static void SetMarks(Slot s, FaceAi.Face f, DateTime now)
        {
            if (s.W < 1 || s.H < 1) { return; }
            PointF re = f.Marks[0], le = f.Marks[1], rm = f.Marks[3], lm = f.Marks[4];
            double mw = Math.Sqrt((rm.X - lm.X) * (rm.X - lm.X) + (rm.Y - lm.Y) * (rm.Y - lm.Y));
            double ed = Math.Sqrt((re.X - le.X) * (re.X - le.X) + (re.Y - le.Y) * (re.Y - le.Y));
            if (mw < 3 || ed < 3) { return; }
            double mcy = (rm.Y + lm.Y) / 2, ecy = (re.Y + le.Y) / 2;
            // Lips and jaw: from just above the upper lip to below the lower lip.
            RectangleF mouth = RectangleF.FromLTRB((float)(Math.Min(rm.X, lm.X) - 0.3 * mw), (float)(mcy - 0.45 * mw),
                                                   (float)(Math.Max(rm.X, lm.X) + 0.3 * mw), (float)(mcy + 0.7 * mw));
            // Eyes and brows: the face moving as a whole, which the mouth's motion is measured against.
            RectangleF upper = RectangleF.FromLTRB((float)(Math.Min(re.X, le.X) - 0.3 * ed), (float)(ecy - 0.45 * ed),
                                                   (float)(Math.Max(re.X, le.X) + 0.3 * ed), (float)(ecy + 0.25 * ed));
            if (mouth.Bottom <= upper.Top) { return; }   // landmarks upside down: not a face we can read
            s.MouthRel = Relative(mouth, s);
            s.UpperRel = Relative(upper, s);
            s.MarksUtc = now;
        }

        private static RectangleF Relative(RectangleF r, Slot s)
        {
            float Clamp(double v) => (float)Math.Max(-0.25, Math.Min(1.25, v));
            return RectangleF.FromLTRB(Clamp((r.Left - s.X) / s.W), Clamp((r.Top - s.Y) / s.H),
                                       Clamp((r.Right - s.X) / s.W), Clamp((r.Bottom - s.Y) / s.H));
        }

        /// <summary>
        /// The face-AI pass (outside the slot lock): loads the models the first time (fetching the fingerprint model in the
        /// background when it is missing), and when a pass is due finds every face's landmarks and prints the faces that
        /// need it — new faces first, then prints older than PrintRefreshMs, at most MaxPrintsPerPass a pass.
        /// </summary>
        private void RunFaceAi(DateTime now, int w, int hgt, out List<FaceAi.Face> aiFaces, out List<float[]> aiPrints)
        {
            aiFaces = null;
            aiPrints = null;
            if (!_aiTried)
            {
                _aiTried = true;
                _ai = FaceAi.TryCreate(out string why);
                if (_ai == null)
                {
                    _aiState = "off — " + why;
                    Logger.Info("[Face] face AI off: " + why + " — faces are told apart by their colours, as before.");
                    return;
                }
                _aiState = _ai.CanRecognize ? "landmarks + fingerprints" : "landmarks (fingerprints once their model is fetched)";
                Logger.Info("[Face] face AI on: YuNet landmarks" + (_ai.CanRecognize ? " + SFace fingerprints." : "; SFace not downloaded yet — fetching it."));
                if (!_ai.CanRecognize)
                {
                    FaceAi.EnsureRecognizerDownloaded((ok, msg) =>
                    {
                        Logger.Info("[Face] fingerprint model (SFace, 37 MB, huggingface.co/opencv): " + msg + ".");
                        if (ok) { _recognizerArrived = true; }
                    });
                }
            }
            // One reference for the whole pass: Stop() (captions off) may drop _ai from the UI thread meanwhile.
            FaceAi ai = _ai;
            if (ai == null) { return; }
            if (_recognizerArrived && !ai.CanRecognize)
            {
                _recognizerArrived = false;
                if (ai.TryLoadRecognizer())
                {
                    _aiState = "landmarks + fingerprints";
                    Logger.Info("[Face] face fingerprints on.");
                }
            }
            double since = (now - _lastAiPassUtc).TotalMilliseconds;
            if (since >= 0 && since < (_aiWantSoon ? AiSoonMs : AiPassMs)) { return; }
            byte[] raw = _rawBuf;
            if (raw == null) { return; }
            _lastAiPassUtc = now;
            _aiWantSoon = false;
            _aiPasses++;
            List<FaceAi.Face> found = ai.Detect(raw, _rawStride, w, hgt, AiMinScore);
            var prints = new List<float[]>(found.Count);
            for (int i = 0; i < found.Count; i++) { prints.Add(null); }
            if (ai.CanRecognize && found.Count > 0)
            {
                var want = new List<(int Priority, int Index)>();
                lock (_slotLock)
                {
                    for (int i = 0; i < found.Count; i++)
                    {
                        Slot match = MatchSlot(found[i].Box);
                        if (match != null && (match.IsStatic || match.IsDrawn)) { continue; }   // a picture of a face
                        int priority = match == null || match.Print == null ? 0
                                     : (now - match.PrintUtc).TotalMilliseconds >= PrintRefreshMs ? 1 : -1;
                        if (priority >= 0) { want.Add((priority, i)); }
                    }
                }
                // New faces first; among equals, the biggest (the surest print).
                want.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority)
                                                             : found[b.Index].Box.Width.CompareTo(found[a.Index].Box.Width));
                if (want.Count > 0 && PrepareFullFrame())
                {
                    double kx = _fullW / (double)w, ky = _fullH / (double)hgt;
                    int budget = MaxPrintsPerPass;
                    foreach (var item in want)
                    {
                        if (budget <= 0) { break; }
                        FaceAi.Face f = found[item.Index];
                        if (f.Box.Width < MinPrintFace) { continue; }
                        var marks = new PointF[5];
                        for (int n = 0; n < 5; n++) { marks[n] = new PointF((float)(f.Marks[n].X * kx), (float)(f.Marks[n].Y * ky)); }
                        prints[item.Index] = ai.Fingerprint(_fullRaw, _fullRawStride, _fullW, _fullH, marks);
                        if (prints[item.Index] != null) { _printsTaken++; }
                        budget--;
                    }
                }
            }
            aiFaces = found;
            aiPrints = prints;
        }

        private const int MaxPrintsPerPass = 2;
        private const float MinPrintFace = 12f;      // analysis px: smaller faces give no useful print

        /// <summary>The living slot a face-AI box belongs to (overlap, or its centre inside the slot's box), or null.</summary>
        private Slot MatchSlot(RectangleF box)
        {
            Slot match = null;
            float best = 0.2f;
            var centre = new PointF(box.X + box.Width / 2, box.Y + box.Height / 2);
            foreach (var s in _slots)
            {
                if (!s.Alive) { continue; }
                var sb = new RectangleF((float)s.X, (float)s.Y, (float)s.W, (float)s.H);
                float iou = FaceAi.Iou(box, sb);
                if (sb.Contains(centre)) { iou = Math.Max(iou, 0.21f); }
                if (iou > best) { best = iou; match = s; }
            }
            return match;
        }

        /// <summary>The full-size capture's pixels in _fullRaw (the prints are cut from it: sharper than the 640-wide frame).</summary>
        private bool PrepareFullFrame()
        {
            Bitmap full = _fullBmp;
            if (full == null || _fullW <= 0 || _fullH <= 0) { return false; }
            var bits = full.LockBits(new Rectangle(0, 0, _fullW, _fullH), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int len = bits.Stride * _fullH;
                if (_fullRaw == null || _fullRaw.Length != len) { _fullRaw = new byte[len]; }
                Marshal.Copy(bits.Scan0, _fullRaw, 0, len);
                _fullRawStride = bits.Stride;
                return true;
            }
            finally { full.UnlockBits(bits); }
        }

        /// <summary>
        /// This frame's face-AI result for a detected face (by overlap with its detection box): its landmarks, and its
        /// print — folded into the occupant's, unless it says somebody else is in the box now (both faces big enough to
        /// judge, cosine under PrintChanged), when the slot starts over as a new face.
        /// </summary>
        private void ApplyFaceAi(Slot s, List<FaceAi.Face> aiFaces, List<float[]> aiPrints, double fx, double fy, double fw, double fh, DateTime now)
        {
            var det = new RectangleF((float)fx, (float)fy, (float)fw, (float)fh);
            int bi = -1;
            float best = 0.2f;
            for (int i = 0; i < aiFaces.Count; i++)
            {
                RectangleF b = aiFaces[i].Box;
                float iou = FaceAi.Iou(det, b);
                if (det.Contains(new PointF(b.X + b.Width / 2, b.Y + b.Height / 2))) { iou = Math.Max(iou, 0.21f); }
                if (iou > best) { best = iou; bi = i; }
            }
            if (bi < 0) { return; }
            FaceAi.Face face = aiFaces[bi];
            SetMarks(s, face, now);
            float[] print = aiPrints != null && bi < aiPrints.Count ? aiPrints[bi] : null;
            if (print == null) { return; }
            float size = face.Box.Width;
            if (s.Print != null)
            {
                float sim = FaceAi.Similarity(s.Print, print);
                bool bothBig = s.PrintSize >= PrintBigFace && size >= PrintBigFace;
                if (sim < PrintChanged && bothBig && s.OccupantId > 0)
                {
                    // Somebody else is in this box: a cut that put another person where this one was, and the colours
                    // (one red shirt, one backdrop) said "same". One person's close-ups never print this far apart
                    // (lowest 0.52 on the NASA footage).
                    RestartAsNewFace(s, now);
                    SetMarks(s, face, now);
                    _printChanges++;
                    _changedAtCut++;
                }
                else if (sim < PrintChanged && bothBig && s.OccupantId == 0)
                {
                    // Not named yet, and somebody else now: its looks so far were someone else's — start them over.
                    s.PendingLook = null;
                    s.PendingLooks = 0;
                    s.PendingSeen = 0;
                    s.Print = null;
                    s.PrintCount = 0;
                }
                else if (sim < PrintSame)
                {
                    // Not clearly the same face: never folded in. Averaged, two people's prints make one that sits
                    // between them and matches everybody — on the NASA footage four people ended up under one id that way.
                    s.PrintUtc = now;
                    return;
                }
            }
            if (s.Print == null)
            {
                s.Print = print;
                s.PrintCount = 1;
                s.PrintSize = size;
            }
            else
            {
                s.Print = FaceAi.Accumulate(s.Print, s.PrintCount, print);
                s.PrintCount = Math.Min(40, s.PrintCount + 1);
                s.PrintSize = s.PrintSize * 0.7f + size * 0.3f;
            }
            s.PrintUtc = now;
            if (s.OccupantId > 0) { RememberPrint(s.OccupantId, s.Print, s.PrintSize); }
        }

        /// <summary>
        /// How sharp a picture is, 0 (flat or blurred) to 1: the mean brightness step between
        /// neighbouring pixels, where about 20 grey levels is a crisp face and 3–6 a blurred one.
        /// </summary>
        internal static double Sharpness(Bitmap bmp)
        {
            if (bmp == null || bmp.Width < 3 || bmp.Height < 3) { return 0; }
            int w = bmp.Width, h = bmp.Height;
            var bits = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int stride = bits.Stride;
                byte[] raw = new byte[stride * h];
                Marshal.Copy(bits.Scan0, raw, 0, raw.Length);
                var luma = new int[w * h];
                for (int y = 0; y < h; y++)
                {
                    int p = y * stride;
                    for (int x = 0; x < w; x++, p += 3)
                    {
                        luma[y * w + x] = (raw[p] * 29 + raw[p + 1] * 150 + raw[p + 2] * 77) >> 8;
                    }
                }
                long sum = 0;
                for (int y = 0; y < h - 1; y++)
                {
                    for (int x = 0; x < w - 1; x++)
                    {
                        int i = y * w + x;
                        sum += Math.Abs(luma[i + 1] - luma[i]) + Math.Abs(luma[i + w] - luma[i]);
                    }
                }
                double mean = sum / (2.0 * (w - 1) * (h - 1));
                return Math.Min(1.0, mean / 20.0);
            }
            finally
            {
                bmp.UnlockBits(bits);
            }
        }
        private readonly List<Slot> _slots = new List<Slot>();

        // Expired slots handed to a new face this session (see Slot.OccupiedSinceUtc).
        private volatile int _slotRecycles;

        /// <summary>
        /// Times this session a new face took over a slot another face had left — each
        /// one a point where a new person may have inherited a previous speaker's number.
        /// </summary>
        public int SlotRecycles => _slotRecycles;

        /// <summary>
        /// The live capture level, in dB, supplied by the caption engine. Optional: when
        /// nothing sets it the analyzer behaves exactly as it did before.
        ///
        /// This is the signal the analyzer never had. It judged "who is talking" purely
        /// from how MUCH a mouth moved, with no idea whether the sound was doing anything
        /// at the same time — so a face that chews, laughs, yawns or runs an idle
        /// animation won the verdict whenever it happened to move most, and with two
        /// faces moving it could only abstain.
        /// </summary>
        public Func<int> AudioLevelDbProvider { get; set; }

        /// <summary>Recent audio energy, paired by index with every slot's MotionRing.</summary>
        private readonly double[] _audioRing = new double[CorrWindow];
        private int _audioCount;

        /// <summary>
        /// Records this frame's audio energy. dB is mapped onto a 0..1 scale across the
        /// range speech actually occupies; below -60 dB is treated as silence.
        /// </summary>
        private void PushAudioSample()
        {
            double energy = 0;
            try
            {
                Func<int> src = AudioLevelDbProvider;
                if (src != null)
                {
                    int db = src();
                    energy = db <= -60 ? 0.0 : Math.Min(1.0, (db + 60.0) / 60.0);
                }
            }
            catch { energy = 0; }

            // Oldest-first shift. CorrWindow is 24, so this is cheaper than the modular
            // arithmetic a ring head would need in the correlation loop below.
            Array.Copy(_audioRing, 1, _audioRing, 0, CorrWindow - 1);
            _audioRing[CorrWindow - 1] = energy;
            if (_audioCount < CorrWindow) { _audioCount++; }
        }

        /// <summary>Records one net mouth-motion sample for a slot, aligned with the audio ring.</summary>
        private static void PushMotionSample(Slot s, double net)
        {
            Array.Copy(s.MotionRing, 1, s.MotionRing, 0, CorrWindow - 1);
            s.MotionRing[CorrWindow - 1] = net;
            if (s.RingCount < CorrWindow) { s.RingCount++; }
        }

        /// <summary>
        /// Pearson correlation between a slot's mouth motion and the audio over the
        /// window: does this mouth move WHEN THE SOUND DOES?
        ///
        /// Returns NaN when it cannot be answered honestly — too few samples, or audio
        /// too flat to correlate with anything. Callers must treat NaN as "no opinion"
        /// rather than as zero, because "no opinion" and "definitely not this face" are
        /// very different verdicts.
        /// </summary>
        private double CorrelateWithAudio(Slot s)
        {
            int n = Math.Min(_audioCount, s.RingCount);
            if (n < CorrWindow / 2) { return double.NaN; }

            int aOff = CorrWindow - n, mOff = CorrWindow - n;
            double aSum = 0, mSum = 0;
            for (int i = 0; i < n; i++) { aSum += _audioRing[aOff + i]; mSum += s.MotionRing[mOff + i]; }
            double aMean = aSum / n, mMean = mSum / n;

            double aVar = 0, mVar = 0, cov = 0;
            for (int i = 0; i < n; i++)
            {
                double da = _audioRing[aOff + i] - aMean;
                double dm = s.MotionRing[mOff + i] - mMean;
                aVar += da * da; mVar += dm * dm; cov += da * dm;
            }

            // A flat signal on either side has no shape to match.
            if (Math.Sqrt(aVar / n) < MinAudioSpread || mVar <= 1e-9) { return double.NaN; }
            return cov / Math.Sqrt(aVar * mVar);
        }
        // Stop() runs on the UI thread while a frame may be mid-analysis on the timer
        // thread. Without this, Stop()'s _slots.Clear() could yank the list out from
        // under the foreach loops below ("Collection was modified; enumeration
        // operation may not execute"). The tick guard only stops the timer racing
        // ITSELF — it does nothing about Stop().
        private readonly object _slotLock = new object();

        /// <summary>1-based slot number of the face that is visibly talking; 0 = unknown.</summary>
        public int CurrentVisualSpeaker => _visualSpeaker;

        /// <summary>Faces currently tracked on screen (for status/diagnostics).</summary>
        public int FaceCount => _faceCount;

        /// <summary>
        /// True while the window being watched hands back a picture-less frame — DRM
        /// playback, or a capture the system won't share. Distinct from "no faces
        /// found": there is nothing to find one IN, so no setting will help and the
        /// analyzer is deliberately idling. Surfaced in Live debug because otherwise
        /// this state is indistinguishable from the feature quietly not working.
        /// </summary>
        public bool WindowIsBlank => _blankStreak >= BlankStreakToLog;

        public bool Running => _running;

        /// <summary>Starts watching. Safe to call twice or on PCs without the OS face AI.</summary>
        public void Start()
        {
            if (_running)
            {
                return;
            }
            try
            {
                // Windows' face detector runs on Windows' own ONNX Runtime (System32's onnxruntime.dll, measured: loaded by
                // its first detection). In the process first, it is what the Nemotron engine would bind to and crash on — so
                // the Nemotron engine's runtime goes in before it (NemotronStreaming.Prepare; MainForm already did this off
                // the UI thread, and then this is a no-op).
                NemotronStreaming.Prepare();
                if (!FaceDetector.IsSupported)
                {
                    Logger.Info("[Face] the OS face detector isn't supported here - visual speaker analysis off.");
                    return;
                }
                _detector = FaceDetector.CreateAsync().AsTask().GetAwaiter().GetResult();
                _running = true;
                _timer = new System.Threading.Timer(Tick, null, 500, IntervalMs);
                Logger.Info("[Face] watching the foreground window for talking faces.");
            }
            catch (Exception ex)
            {
                Logger.Warn("[Face] analyzer unavailable: " + ex.Message);
                Stop();
            }
        }

        public void Stop()
        {
            _running = false;       // the next tick entry bails immediately
            _visualSpeaker = 0;
            System.Threading.Interlocked.Exchange(ref _speakerOccupant, 0);
            _visibleOccupants = new long[0];
            _faceCount = 0;
            _talkingFaces = 0;
            _slotRecycles = 0;      // per session, like the slots themselves
            try { _timer?.Dispose(); } catch { }
            _timer = null;

            // WAIT for a frame that is already being analysed before freeing what it is
            // holding.
            //
            // Stop() is called from the UI thread (captions off, game mode, shutdown)
            // while AnalyzeOneFrame may be running on the timer thread. _slotLock covered
            // the slot list but nothing covered the bitmaps — so this could Dispose
            // _frameBmp while the timer thread was inside LockBits on it, and null
            // _detector between the running frame's null check and its use. Disposing a
            // GDI+ bitmap that another thread has LOCKED is not a caught-and-carry-on
            // situation; it corrupts the object. The tick's catch hid the symptoms and
            // made this look harmless.
            //
            // _tickGuard is already the "a frame is in flight" flag, so wait on it.
            // Bounded, because a capture can genuinely stall (PrintWindow against a
            // window that has stopped pumping messages) and freezing the UI to tidy up
            // would be a worse bug than the one being fixed.
            bool idle = false;
            for (int i = 0; i < 25; i++)
            {
                if (System.Threading.Interlocked.CompareExchange(ref _tickGuard, 0, 0) == 0)
                {
                    idle = true;
                    break;
                }
                System.Threading.Thread.Sleep(10);
            }

            _detector = null;
            _prevGray = null;
            _spareGray = null;
            _rawBuf = null;
            _fullRaw = null;
            _blankStreak = 0;
            _detectedOccupants = new long[0];
            // The face AI's sessions (the fingerprint model holds ~40 MB): built again at the next start.
            FaceAi ai = _ai;
            _ai = null;
            _aiTried = false;
            _aiPasses = 0; _printsTaken = 0; _ghostMerges = 0; _printReids = 0; _printChanges = 0;
            if (idle)
            {
                try { _fullBmp?.Dispose(); } catch { }
                try { _frameBmp?.Dispose(); } catch { }
                try { ai?.Dispose(); } catch { }
            }
            else
            {
                // Still busy after 250 ms: drop the references WITHOUT disposing and let
                // finalisation reclaim them. Leaking a bitmap for one GC cycle is far
                // cheaper than tearing one out from under a thread mid-draw.
                Logger.Info("[Face] stop: a frame was still in flight — bitmaps released to the GC.");
            }
            _fullBmp = null; _frameBmp = null;
            _fullW = _fullH = _frameW = _frameH = 0;
            _reidentified = 0;
            _changedAtCut = 0;
            lock (_slotLock)
            {
                _slots.Clear();
                _knownFaces.Clear();
                _prints.Clear();
                // The pictures go with the session: nothing of anyone's face outlives the captions.
                foreach (Bitmap b in _thumbs.Values) { try { b?.Dispose(); } catch { } }
                _thumbs.Clear();
                _thumbScores.Clear();
                _thumbOrder.Clear();
            }
        }

        public void Dispose() => Stop();

        /// <summary>
        /// True while a fullscreen game owns the screen: face analysis PAUSES
        /// COMPLETELY. PrintWindow doesn't just cost FPS — it renders the game
        /// window SYNCHRONOUSLY into our bitmap, and a game that isn't pumping
        /// messages mid-frame can stall on that call for whole seconds (reported
        /// live: Rainbow Six freezing entirely with captions on). No capture may
        /// touch a fullscreen game's window, at any rate. Analysis resumes the
        /// moment the game closes; speaker labels fall back to voice matching,
        /// which is the primary signal anyway.
        /// </summary>
        public static volatile bool LowImpactMode;
        private const int LowImpactIntervalMs = 1000;
        private bool _pausedLogged;

        // Adaptive analysis pace: while faces ARE on screen, motion sampling runs
        // faster (finer mouth-movement signal → surer verdicts); with no faces for
        // a while, it idles — most of the analysis cost only exists when there is
        // actually a face to analyse.
        private const int FastIntervalMs = 160;
        private const int IdleIntervalMs = 420;
        // A window that gives us nothing to look at gets the slowest pace of all. It is
        // still polled — protected playback can stop, or the user can switch windows, and
        // the very next non-blank frame puts the pace straight back up — but there is no
        // reason to ask twice a second.
        private const int BlankIntervalMs = 2000;
        private const int BlankStreakToLog = 5;      // ~1s of blank frames before saying so
        private int _blankStreak;
        private int _currentInterval = IntervalMs;
        private DateTime _lastFaceSeenUtc = DateTime.MinValue;

        /// <summary>
        /// True when the captured frame carries no picture — every sampled pixel within a
        /// hair of the same value, which is what a protected or unshareable window gives
        /// back. Samples one pixel in 64, the same density the motion check uses, so it
        /// costs almost nothing next to the detection it saves.
        /// </summary>
        private static bool FrameIsBlank(byte[] gray, int w, int h)
        {
            if (gray == null || w <= 0 || h <= 0) { return false; }
            const int Step = 8;                 // every 8th pixel on every 8th row
            const int Spread = 6;               // 0-255; below this there is no image
            byte min = 255, max = 0;
            for (int y = 0; y < h; y += Step)
            {
                int off = y * w;
                for (int x = 0; x < w; x += Step)
                {
                    byte v = gray[off + x];
                    if (v < min) { min = v; }
                    if (v > max) { max = v; }
                    if (max - min > Spread) { return false; }   // real content — stop early
                }
            }
            return true;
        }

        private void Tick(object state)
        {
            if (!_running || System.Threading.Interlocked.CompareExchange(ref _tickGuard, 1, 0) != 0)
            {
                return;
            }
            try
            {
                if (LowImpactMode)
                {
                    // FULL pause: not one PrintWindow may touch a fullscreen game
                    // (synchronous render forcing = the "game freezes entirely"
                    // report). Drop the verdict so stale faces can't mislabel.
                    if (!_pausedLogged)
                    {
                        _pausedLogged = true;
                        _visualSpeaker = 0;
                        System.Threading.Interlocked.Exchange(ref _speakerOccupant, 0);
                        _visibleOccupants = new long[0];
                        _faceCount = 0;
                        // Crosstalk must clear too: latched TRUE through a whole game
                        // session it silently suppressed every voice-driven label
                        // switch (the labeler reads it while Running is still true).
                        _talkingFaces = 0;
                        _prevGray = null;
                        _spareGray = null;
                        _blankStreak = 0;
                        Logger.Info("[Face] analysis paused — fullscreen game on screen (no window captures).");
                    }
                }
                else
                {
                    if (_pausedLogged)
                    {
                        _pausedLogged = false;
                        Logger.Info("[Face] analysis resumed — fullscreen closed.");
                    }
                    AnalyzeOneFrame();
                }

                if (_faceCount > 0) { _lastFaceSeenUtc = DateTime.UtcNow; }
                int want = LowImpactMode
                    ? LowImpactIntervalMs
                    : _faceCount > 0
                        ? FastIntervalMs
                        : _blankStreak >= BlankStreakToLog
                            ? BlankIntervalMs
                            : (DateTime.UtcNow - _lastFaceSeenUtc).TotalSeconds > 5
                                ? IdleIntervalMs
                                : IntervalMs;
                if (want != _currentInterval)
                {
                    _currentInterval = want;
                    try { _timer?.Change(want, want); } catch { }
                }
            }
            catch
            {
                // A capture/detection hiccup (secure window, resolution change, device
                // loss) must never bubble out of the timer thread.
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _tickGuard, 0);
            }
        }

        // Test seams, set by reflection from scratchpad/facereid and never in the app: a picture to
        // analyse in place of capturing a window (recorded footage, frame by frame), and the clock the
        // tracker reads, so minutes of video replay in seconds on their own timeline.
        private Func<Bitmap> _frameSourceForTest;
        private Func<DateTime> _clock = () => DateTime.UtcNow;

        // The analysis frame's colour rows (in _rawBuf), for measuring what faces look like.
        private int _rawStride;

        /// <summary>
        /// Asked before every frame (round 32): a reason NOT to look at any window now — the host's "the video isn't on
        /// screen" when a browser plays it in a tab nobody can see (a background tab is not drawn at all, so whatever window
        /// is in front shows another page — the user's 32.png had a page of haircut photos). Null = look as usual.
        /// </summary>
        public Func<string> SkipReason { get; set; }

        private volatile string _skipReason;
        private bool _skipLogged;
        /// <summary>Why the analyzer is not looking right now (<see cref="SkipReason"/>), or null.</summary>
        public string SkippedBecause => _skipReason;

        private void AnalyzeOneFrame()
        {
            // 0. Nothing worth looking at (SkipReason): no capture, and no faces left standing from before.
            if (_frameSourceForTest == null)
            {
                string skip = null;
                try { skip = SkipReason?.Invoke(); } catch { skip = null; }
                if (skip != null)
                {
                    if (_skipReason == null)
                    {
                        _visualSpeaker = 0;
                        System.Threading.Interlocked.Exchange(ref _speakerOccupant, 0);
                        _visibleOccupants = new long[0];
                        _detectedOccupants = new long[0];
                        _faceCount = 0;
                        _talkingFaces = 0;
                        _prevGray = null;
                        _spareGray = null;
                        // Once a run: tab switches come and go far more often than the log should say so.
                        if (!_skipLogged)
                        {
                            _skipLogged = true;
                            Logger.Info("[Face] not looking: " + skip + " — speakers by voice alone until it is (said once).");
                        }
                    }
                    _skipReason = skip;
                    return;
                }
                _skipReason = null;
            }

            // 1. Pick the window to read: prefer the one PLAYING AUDIO (any app, even
            //    behind other windows), else the foreground window.
            Bitmap testFrame = _frameSourceForTest?.Invoke();
            IntPtr h = IntPtr.Zero;
            RECT r = default(RECT);
            bool usePreferred = false;
            int srcW, srcH;
            if (testFrame != null)
            {
                srcW = testFrame.Width;
                srcH = testFrame.Height;
            }
            else
            {
                IntPtr preferred = IntPtr.Zero;
                try { preferred = PreferredWindow != null ? PreferredWindow() : IntPtr.Zero; } catch { }
                usePreferred = preferred != IntPtr.Zero && IsWindow(preferred) && !IsIconic(preferred);
                h = usePreferred ? preferred : GetForegroundWindow();
                if (h == IntPtr.Zero || !GetWindowRect(h, out r))
                {
                    return;
                }
                srcW = r.Right - r.Left;
                srcH = r.Bottom - r.Top;
            }
            if (srcW < 120 || srcH < 90)
            {
                return;
            }
            int w = FrameWidth;
            int hgt = Math.Max(64, srcH * w / srcW);

            // Reuse the gray buffer (the spare recycled from last frame's _prevGray) and
            // the scaled/source bitmaps when the size is unchanged — no per-frame LOH.
            byte[] gray = (_spareGray != null && _spareGray.Length == w * hgt)
                ? _spareGray : new byte[w * hgt];
            _spareGray = null;
            if (_frameBmp == null || _frameW != w || _frameH != hgt)
            {
                try { _frameBmp?.Dispose(); } catch { }
                _frameBmp = new Bitmap(w, hgt, PixelFormat.Format24bppRgb);
                _frameW = w; _frameH = hgt;
            }
            if (_fullBmp == null || _fullW != srcW || _fullH != srcH)
            {
                try { _fullBmp?.Dispose(); } catch { }
                _fullBmp = new Bitmap(srcW, srcH, PixelFormat.Format24bppRgb);
                _fullW = srcW; _fullH = srcH;
            }
            {
                var frame = _frameBmp;
                using (var g = Graphics.FromImage(frame))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                    {
                        var full = _fullBmp;
                        bool captured = false;
                        if (testFrame != null)
                        {
                            using (var gt = Graphics.FromImage(full))
                            {
                                gt.DrawImage(testFrame, new Rectangle(0, 0, srcW, srcH));
                            }
                            captured = true;
                        }
                        // PrintWindow reads the window's own surface, so it works for
                        // windows sitting BEHIND others; PW_RENDERFULLCONTENT makes
                        // DWM include hardware-accelerated video frames.
                        else if (usePreferred)
                        {
                            try
                            {
                                using (var gp = Graphics.FromImage(full))
                                {
                                    IntPtr hdc = gp.GetHdc();
                                    try { captured = PrintWindow(h, hdc, PW_RENDERFULLCONTENT); }
                                    finally { gp.ReleaseHdc(hdc); }
                                }
                            }
                            catch { captured = false; }
                        }
                        if (!captured)
                        {
                            // Screen copy (foreground path, or PrintWindow refused).
                            using (var gf = Graphics.FromImage(full))
                            {
                                gf.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(srcW, srcH));
                            }
                        }
                        g.DrawImage(full, 0, 0, w, hgt);
                    }
                }

                // 2. To 8-bit luminance for both the detector and mouth-motion math.
                var bits = frame.LockBits(new Rectangle(0, 0, w, hgt), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    int stride = bits.Stride;
                    _rawStride = stride;
                    int rawLen = stride * hgt;
                    if (_rawBuf == null || _rawBuf.Length != rawLen) { _rawBuf = new byte[rawLen]; }
                    byte[] raw = _rawBuf;
                    Marshal.Copy(bits.Scan0, raw, 0, rawLen);   // copy exactly rawLen, never raw.Length
                    // Luma with a SHIFT instead of a divide, and a walking source index.
                    //
                    // This is the biggest loop in the analyzer — 640x360 is 230k pixels
                    // every frame — and it was doing three multiplies plus an integer
                    // DIVIDE per pixel, then recomputing the source offset with another
                    // multiply. The weights 77/150/29 are the same Rec.601 ratios scaled
                    // to 256 (0.299/0.587/0.114 x 256 = 76.5/150.3/29.2), so >> 8
                    // replaces / 1000 for an identical result to within one grey level —
                    // and this feeds a motion DIFFERENCE, where a uniform ±1 cancels.
                    for (int y = 0; y < hgt; y++)
                    {
                        int p = y * stride;
                        int off = y * w;
                        for (int x = 0; x < w; x++, p += 3)
                        {
                            gray[off + x] = (byte)((raw[p + 2] * 77 + raw[p + 1] * 150 + raw[p] * 29) >> 8);
                        }
                    }
                }
                finally
                {
                    frame.UnlockBits(bits);
                }
            }

            // 3. OS face detection on the grayscale frame — but not on a FROZEN one.
            //
            // DetectFacesAsync is far and away the most expensive step here, and it used
            // to run on every single tick regardless of whether anything had changed. A
            // paused video, a still photo, a menu, a document left on screen — all of it
            // was being re-detected several times a second to produce the identical
            // answer. The whole-frame motion check that already exists costs almost
            // nothing (it samples one pixel in 64) and was only being computed AFTER the
            // detector had already run, so it could never save the work.
            //
            // Measured first, the same number now buys the skip: a frame that is
            // pixel-for-pixel what we already analysed cannot contain a face that moved,
            // so the detector is not asked. The slots simply coast, and because the
            // motion bookkeeping below still runs against an unchanged frame, mouth
            // motion reads ~0 and a paused video correctly stops being "talking" rather
            // than freezing the verdict.
            bool haveGlobal = _prevGray != null && _prevW == w && _prevH == hgt;
            double globalPre = haveGlobal ? FrameMeanMotion(gray, _prevGray, w, hgt) : double.MaxValue;
            bool frozen = haveGlobal && globalPre <= FrozenFrameMotion && _slots.Count > 0;

            // A frame with no picture in it at all cannot contain a face, and the frozen
            // check above will not catch it: that one requires a face to already be
            // tracked (_slots.Count > 0), which never becomes true here.
            //
            // This is the DRM case. PrintWindow returns a blank frame for protected video
            // — Netflix, Prime, Disney+ in a browser — and those are exactly the windows
            // someone turns captions on for. Without this the analyser captured the
            // window and ran the OS face detector, the most expensive step by far,
            // roughly twice a second for the length of a feature film, to be told each
            // time that a black rectangle contains no faces.
            bool blank = FrameIsBlank(gray, w, hgt);
            if (blank)
            {
                if (++_blankStreak == BlankStreakToLog)
                {
                    Logger.Info("[Face] the window being watched returns a blank image " +
                                "(protected video, or a capture the system won't share), so " +
                                "face and mouth analysis is idling — captions are unaffected.");
                }
            }
            else if (_blankStreak != 0)
            {
                _blankStreak = 0;
            }

            IList<DetectedFace> faces;
            if (frozen || blank)
            {
                _framesSkipped++;
                faces = System.Array.Empty<DetectedFace>();
            }
            else
            {
                using (var sb = SoftwareBitmap.CreateCopyFromBuffer(gray.AsBuffer(), BitmapPixelFormat.Gray8, w, hgt))
                {
                    faces = _detector.DetectFacesAsync(sb).AsTask().GetAwaiter().GetResult();
                }
            }

            DateTime now = _clock();

            // 3b. The face AI (Utils/FaceAi.cs): about once a second — sooner while a new face waits for its print — the
            // landmarks of every face, and fingerprints for the faces that need one. Run here, before the tracker takes
            // its lock: a pass costs ~20 ms plus ~25–60 ms a print, and the UI reads thumbnails under that lock.
            List<FaceAi.Face> aiFaces = null;
            List<float[]> aiPrints = null;
            if (!frozen && !blank && faces.Count > 0)
            {
                try { RunFaceAi(now, w, hgt, out aiFaces, out aiPrints); }
                catch (Exception aex) { Logger.Swallow("FaceAi pass", aex); aiFaces = null; aiPrints = null; }
            }

            // GLOBAL-MOTION GUARD: a scene cut or a hard camera pan changes nearly
            // every pixel at once. The mouth-minus-upper cancellation is built for
            // per-face movement — on a cut, both regions read enormous and the
            // ~10% that survives the subtraction still beats the talking gate, so
            // every face on screen pulsed "talking" for a frame or two (fake
            // crosstalk that HELD the speaker label right after each cut). Measure
            // the whole frame first: when it jolts, skip all motion bookkeeping
            // this frame — boxes and matching still update, the EMAs just don't
            // eat a frame of garbage. (Static-image records skip too: one cut
            // frame must not clear a poster's hard-earned STATIC flag.)
            // Reuses the value measured before detection — it was being computed twice
            // over the same two frames, once here and (now) once for the frozen-frame
            // check, for an identical result.
            bool globalJolt = false;
            if (haveGlobal)
            {
                _globalX100 = (int)(globalPre * 100);
                if (globalPre >= CutMotion)
                {
                    globalJolt = true;
                    _sceneCuts++;
                }
            }

            // Everything below touches _slots, which Stop() may clear from the UI
            // thread at any moment (a caption restart does exactly that). Hold the
            // lock across the whole tracking pass so the list can't change shape
            // mid-enumeration; Stop() just waits the few ms this frame needs.
            lock (_slotLock)
            {
            // One audio sample per analysed frame, taken BEFORE the motion loops so it
            // lines up index-for-index with the motion each slot is about to record.
            // Every alive slot gets exactly one motion sample per frame — the detected
            // path for matched slots, the coasting path for the rest — which is what
            // keeps the two rings aligned without timestamps.
            PushAudioSample();

            foreach (var s in _slots) { s.MatchedPrevFrame = s.MatchedThisFrame; s.MatchedThisFrame = false; }

            // 4. Match faces to persistent slots by nearest centre, so "Face 2" stays
            //    the same face from frame to frame.
            foreach (var f in faces)
            {
                double fx = f.FaceBox.X, fy = f.FaceBox.Y, fw = f.FaceBox.Width, fh = f.FaceBox.Height;
                double cx = fx + fw / 2, cy = fy + fh / 2;

                Slot best = null;
                double bestDist = double.MaxValue;
                foreach (var s in _slots)
                {
                    // Skip slots already claimed by an earlier face THIS frame, or two
                    // detections both matching one slot (e.g. a cut to a two-shot with
                    // the old box between the heads) would merge into one track: the
                    // second claim re-smooths the box toward face B, produces a bogus
                    // velocity spike, and leaves face A with no slot at all. Mirrors the
                    // coasting loop's guard below.
                    if (!s.Alive || s.MatchedThisFrame) continue;
                    // PREDICTIVE match: compare against where the slot's velocity says
                    // the face IS now, not where it last sat. A fast mover can cross
                    // more than a face-width between ~5 fps ticks — against the stale
                    // centre that read as "different face" and reshuffled the slots,
                    // losing the motion history mid-sentence. The gate also widens
                    // with measured speed (stationary faces keep the tight gate, so
                    // two nearby still faces can't get cross-matched).
                    double speed = Math.Sqrt(s.VX * s.VX + s.VY * s.VY);
                    double dx = (s.X + s.W / 2 + s.VX) - cx, dy = (s.Y + s.H / 2 + s.VY) - cy;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < bestDist && dist < Math.Max(s.W, fw) + speed * 2)
                    {
                        bestDist = dist; best = s;
                    }
                }

                // What this face looks like — and, at a cut or when a face the tracker had lost is
                // found again, whether it is still the SAME PERSON. Matching by position alone handed
                // the next person the previous one's identity whenever a cut went between two
                // close-ups framed alike: on a news conference, four astronauts shared one track.
                FaceLook look = _reidentify ? MeasureLook(gray, w, hgt, fx, fy, fw, fh) : null;
                if (best != null && best.OccupantId > 0 && look != null && (globalJolt || !best.MatchedPrevFrame)
                    && !StillSamePerson(best, look, fw))
                {
                    RetireOccupant(best, now);
                    _changedAtCut++;
                    best = null;
                }
                else if (best != null && best.OccupantId == 0 && globalJolt && _reidentify)
                {
                    // A face still being looked at (not named yet) does not carry its looks across a CUT: the face in its
                    // place now may be anyone. Pooled, two people's looks became one new identity (round 26, NASA
                    // footage: a close-up's looks and the next close-up's, at the same spot, named as one person).
                    best = null;
                }
                if (best == null)
                {
                    // Recycle an expired slot first — videos cut between scenes
                    // constantly, and without reuse the tracker ran out of slots
                    // after the first few faces it ever saw and went blind.
                    foreach (var s in _slots)
                    {
                        if (!s.Alive) { best = s; _slotRecycles++; break; }
                    }
                    if (best == null)
                    {
                        if (_slots.Count >= MaxSlots)
                        {
                            continue;                // scene is crowded — track the first few
                        }
                        best = new Slot();
                        _slots.Add(best);
                    }
                    best.OccupiedSinceUtc = now;     // a new occupant: may be a different person
                    // Unnamed at first when recognising faces: its first few looks decide whether this
                    // is someone seen before (ResolvePendingFaces), and until then it is nobody's.
                    best.OccupantId = _reidentify ? 0 : System.Threading.Interlocked.Increment(ref _nextOccupant);
                    best.BigLook = null; best.SmallLook = null;
                    best.PendingLook = null; best.PendingLooks = 0; best.PendingSeen = 0;
                    best.Print = null; best.PrintCount = 0; best.PrintSize = 0;
                    best.MarksUtc = DateTime.MinValue;
                    best.ThumbUtc = DateTime.MinValue;
                    best.ThumbTriedUtc = DateTime.MinValue;
                    best.ThumbScore = 0;
                    best.X = fx; best.Y = fy; best.W = fw; best.H = fh;
                    best.MotionEma = 0;              // a fresh face starts with a clean history
                    best.FreshMotion = true;
                    // ...including velocity and the static-image record — a recycled
                    // slot must not inherit the previous occupant's movement or its
                    // "has been still for seconds" verdict.
                    best.VX = 0; best.VY = 0;
                    best.MatchedPrevFrame = false;
                    best.TotalMotionEma = 0; best.FreshTotal = true;
                    best.QuietSinceUtc = DateTime.MinValue; best.QuietSamples = 0;
                    best.IsStatic = false;
                    best.IsDrawn = false; best.ChromaEma = -1; best.ChromaSamples = 0;
                }

                // Centre BEFORE this frame's smoothing — the frame-to-frame delta
                // of the smoothed box is the velocity sample below.
                double ocx = best.X + best.W / 2, ocy = best.Y + best.H / 2;

                // Smooth the box (video faces bob around a little).
                best.X = best.X * 0.6 + fx * 0.4;
                best.Y = best.Y * 0.6 + fy * 0.4;
                best.W = best.W * 0.6 + fw * 0.4;
                best.H = best.H * 0.6 + fh * 0.4;
                best.LastSeenUtc = now;
                best.Alive = true;
                best.MatchedThisFrame = true;
                // Colour in the face, or none (a drawing on a black-and-white page): see IsDrawn.
                NoteFaceColour(best);
                // The face AI's view of this face when a pass ran this frame: its landmarks, and maybe a fingerprint —
                // which can also say the occupant has changed (ApplyFaceAi).
                if (aiFaces != null) { ApplyFaceAi(best, aiFaces, aiPrints, fx, fy, fw, fh, now); }
                if (best.OccupantId > 0)
                {
                    if (look != null) { AddLook(ref best.BigLook, ref best.SmallLook, look); }
                }
                else if (_reidentify)
                {
                    best.PendingSeen++;
                    if (look != null)
                    {
                        if (best.PendingLook == null) { best.PendingLook = look; }
                        else { FaceAppearance.Accumulate(best.PendingLook, look); }
                        best.PendingLooks++;
                    }
                }
                MaybeTakeThumbnail(best, now);

                // Centre velocity, EMA'd only across CONSECUTIVE detections (a fresh
                // slot and the re-appearance jump after coasting are excluded — see
                // MatchedPrevFrame). Deltas of the SMOOTHED box are already gentle,
                // so a stationary head's velocity hugs zero and never trips the
                // coasting drift's dead-zone.
                if (best.MatchedPrevFrame)
                {
                    double ncx = best.X + best.W / 2, ncy = best.Y + best.H / 2;
                    best.VX = best.VX * 0.6 + (ncx - ocx) * 0.4;
                    best.VY = best.VY * 0.6 + (ncy - ocy) * 0.4;
                }

                // 5. Mouth motion, measured RELATIVE to the rest of the face. A talking
                //    mouth moves while the forehead/eyes stay still; a head turn, a
                //    camera pan or a scene change moves BOTH equally. Subtracting the
                //    upper-face motion cancels whole-face movement, so only genuine
                //    articulation counts — this is what stops a nodding, silent face
                //    from being labelled the speaker.
                if (!globalJolt && _prevGray != null && _prevW == w && _prevH == hgt && best.W >= MinFaceWidth)
                {
                    // Motion-compensate against the face's own velocity so a moving
                    // face is measured mouth-on-mouth, not mouth-on-cheek.
                    double mvx = best.MatchedPrevFrame ? best.VX : 0;
                    double mvy = best.MatchedPrevFrame ? best.VY : 0;
                    MouthAndUpper(best, now, out RectangleF mr, out RectangleF ur);
                    double mouth = RegionMotion(gray, w, hgt,
                        best.X + best.W * mr.Left, best.Y + best.H * mr.Top,
                        best.X + best.W * mr.Right, best.Y + best.H * mr.Bottom, mvx, mvy);
                    double upper = RegionMotion(gray, w, hgt,
                        best.X + best.W * ur.Left, best.Y + best.H * ur.Top,
                        best.X + best.W * ur.Right, best.Y + best.H * ur.Bottom, mvx, mvy);
                    double net = Math.Max(0, mouth - upper * 0.9);
                    if (best.FreshMotion)
                    {
                        best.MotionEma = net;        // seed: no slow ease-in from zero
                        best.FreshMotion = false;
                    }
                    else
                    {
                        best.MotionEma = best.MotionEma * 0.7 + net * 0.3;
                    }
                    PushMotionSample(best, net);

                    // Whole-box motion feeds the static-image detector. Sampled here
                    // (not just the mouth strip) because a poster's giveaway is that
                    // NOTHING in the box moves — no blink, no breath, no lighting
                    // grain — while its mouth region alone can flicker with noise.
                    UpdateStaticState(best,
                        RegionMotion(gray, w, hgt, best.X, best.Y, best.X + best.W, best.Y + best.H),
                        now);
                }
            }

            // A CUT: the faces the detector did not find again are not on screen any more. They used to coast on their
            // old boxes for up to 3 s (that is for a head turned away, not for a different shot) — long enough for the
            // same person, in the new shot, to stand beside his own ghost as "two faces sharing a voice" (22.png).
            if (globalJolt && RetireAtCut)
            {
                foreach (var s in _slots)
                {
                    if (s.Alive && !s.MatchedThisFrame) { RetireOccupant(s, now); }
                }
            }

            // New faces with enough looks: someone seen before, or someone new.
            if (_reidentify) { ResolvePendingFaces(now); }

            // A new face still waiting for its print asks for the next face-AI pass soon.
            FaceAi aiNow = _ai;
            foreach (var s in _slots)
            {
                if (s.Alive && s.OccupantId == 0 && s.Print == null && aiNow != null && aiNow.CanRecognize) { _aiWantSoon = true; break; }
            }

            // COASTING: slots the detector missed this frame — a head turned to the
            // side, tipped down or up (the OS detector is near-frontal only) — keep
            // their last box and KEEP measuring articulation there. Lips and jaw
            // still move visibly from the side, so the talking verdict survives the
            // whole head turn ("full head" coverage: left, right, up, down). The
            // upper-face discount is raised a touch: with no detection to re-anchor
            // the box, whole-scene motion must count for even less.
            if (_prevGray != null && _prevW == w && _prevH == hgt)
            {
                foreach (var s in _slots)
                {
                    if (!s.Alive || s.MatchedThisFrame) { continue; }

                    // VELOCITY COASTING: drift the box along the last measured
                    // detected-frames velocity instead of freezing it — a walking
                    // person who turns their head keeps being measured where they
                    // actually are. The velocity decays every frame (no detection
                    // means no fresh evidence, so trust it less and less) and the
                    // dead-zone means a head that was standing still stays PUT —
                    // sub-pixel jitter must not walk the box off a stationary face.
                    double drvx = 0, drvy = 0;       // drift actually applied this frame
                    if (Math.Abs(s.VX) >= CoastVelocityDeadZone || Math.Abs(s.VY) >= CoastVelocityDeadZone)
                    {
                        drvx = s.VX;
                        drvy = s.VY;
                        s.X += s.VX;
                        s.Y += s.VY;
                        // Never drift outside the frame — RegionMotion clamps its
                        // reads anyway, but a box parked off-screen would measure a
                        // sliver of pixels and turn into noise.
                        s.X = Math.Max(0, Math.Min(w - s.W, s.X));
                        s.Y = Math.Max(0, Math.Min(hgt - s.H, s.Y));
                    }
                    s.VX *= CoastVelocityDecay;
                    s.VY *= CoastVelocityDecay;

                    if (s.W < MinFaceWidth || globalJolt) { continue; }
                    // Same motion compensation as the detected path, using the drift
                    // the box just made — mouth measured against last frame's mouth.
                    MouthAndUpper(s, now, out RectangleF mr, out RectangleF ur);
                    double mouth = RegionMotion(gray, w, hgt,
                        s.X + s.W * mr.Left, s.Y + s.H * mr.Top,
                        s.X + s.W * mr.Right, s.Y + s.H * mr.Bottom, drvx, drvy);
                    double upper = RegionMotion(gray, w, hgt,
                        s.X + s.W * ur.Left, s.Y + s.H * ur.Top,
                        s.X + s.W * ur.Right, s.Y + s.H * ur.Bottom, drvx, drvy);
                    double net = Math.Max(0, mouth - upper * 1.1);
                    s.MotionEma = s.MotionEma * 0.7 + net * 0.3;
                    PushMotionSample(s, net);

                    // Keep the static-image record fed while coasting too, so a
                    // static face that starts moving is un-flagged just as fast no
                    // matter which state it's in when the motion arrives.
                    UpdateStaticState(s,
                        RegionMotion(gray, w, hgt, s.X, s.Y, s.X + s.W, s.Y + s.H),
                        now);
                }
            }

            // Expire slots that vanished (scene cut, speaker left frame). A coasting
            // face that is STILL clearly talking earns a longer grace — a head turn
            // mid-sentence routinely outlasts the plain 3 s window.
            int alive = 0;
            foreach (var s in _slots)
            {
                int graceMs = s.MotionEma >= SpeakMotion ? SlotTimeoutMs + 1500 : SlotTimeoutMs;
                if (s.Alive && (now - s.LastSeenUtc).TotalMilliseconds > graceMs)
                {
                    RetireOccupant(s, now);         // remembered, in case they come back
                }
                if (s.Alive && !s.IsDrawn) alive++;   // a drawing is not a face on screen (round 27)
            }
            _faceCount = alive;

            // Recycle the OLD prev-frame buffer as next frame's spare (when sizes match)
            // instead of letting it become garbage — this is what keeps the gray[] out
            // of the LOH churn. NEVER alias _prevGray = gray directly (that would make
            // motion read current-vs-current ≈ 0 and silently disable the analyzer).
            _spareGray = (_prevGray != null && _prevGray.Length == gray.Length) ? _prevGray : null;
            _prevGray = gray;
            _prevW = w; _prevH = hgt;

            // 6. Verdict: the face whose mouth clearly moves the most is talking.
            //    Motion is weighted by face SIZE: mean-per-pixel motion is already
            //    area-normalised, but a small face's mouth is a handful of pixels of
            //    compression noise while a large face's is real articulation detail —
            //    when two faces are close, trust the bigger (clearer) one.
            int speaker = 0;
            int talking = 0;
            double top = 0, second = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                if (!s.Alive) continue;
                // A Static slot is a picture of a face, not a face — whatever its
                // mouth region's compression noise reads, it is not talking. Nor is a drawn one (IsDrawn), however the
                // page it is on scrolls or zooms.
                if (s.IsStatic || s.IsDrawn) continue;
                // A new face that has looked colourless so far waits for its colour to be confirmed (DrawnMinSamples, about
                // a second) before it may talk: in that second a drawing used to win the verdict often enough to be linked
                // to the narrator's voice. A face with colour from its first look, or too dark to judge, is not held back.
                if (s.ChromaEma >= 0 && s.ChromaEma < DrawnChromaClear && s.ChromaSamples < DrawnMinSamples) continue;
                // A FAST-MOVING face earns extra skepticism: translation is
                // velocity-compensated, but rotation and scale change are not, so
                // residual motion leaks into the mouth reading roughly with speed.
                // Discount up to 2× at ≥10 px/frame — a silent face walking across
                // frame stops being called the speaker, while a talker's real
                // articulation (far above the gate) still clears the higher bar.
                double spd = Math.Sqrt(s.VX * s.VX + s.VY * s.VY);
                double eff = s.MotionEma / (1.0 + Math.Min(1.0, spd / 10.0));
                if (eff >= SpeakMotion) { talking++; }
                double sizeConf = Math.Min(1.0, Math.Sqrt(s.W / 64.0));
                double score = eff * sizeConf;

                // Does this mouth move WHEN THE SOUND DOES?
                //
                // Motion magnitude alone cannot separate speech from chewing, laughing,
                // yawning or a game character's idle loop — all of which move a mouth,
                // and any of which could out-move the real speaker. Correlating each
                // face's motion against the audio envelope asks the question that
                // actually distinguishes them, and it is the one signal this analyzer
                // was missing entirely.
                //
                // Applied as a MODEST re-weighting, not a gate. The correlation is
                // computed over a couple of seconds of a noisy per-pixel measurement, so
                // it is a useful lean and not proof; a strongly-articulating face with an
                // unlucky correlation must still be able to win. NaN — too few samples,
                // or audio too flat to say — leaves the score untouched, which is exactly
                // the old behaviour.
                s.AudioCorr = CorrelateWithAudio(s);
                if (!double.IsNaN(s.AudioCorr))
                {
                    double c = Math.Max(-1.0, Math.Min(1.0, s.AudioCorr));
                    score *= 0.75 + 0.45 * Math.Max(0.0, c);
                }
                if (score > top)
                {
                    second = top; top = score; speaker = i + 1;
                }
                else if (score > second)
                {
                    second = score;
                }
            }
            _talkingFaces = talking;
            bool confident = top >= SpeakMotion && (second <= 0.01 || top >= second * SpeakLead);
            _visualSpeaker = confident ? speaker : 0;
            System.Threading.Interlocked.Exchange(ref _speakerOccupant,
                confident && speaker >= 1 && speaker <= _slots.Count ? _slots[speaker - 1].OccupantId : 0);
            var visible = new List<long>(_slots.Count);
            var detected = new List<long>(_slots.Count);
            foreach (var s in _slots)
            {
                if (s.Alive && !s.IsStatic && !s.IsDrawn && s.OccupantId > 0)
                {
                    visible.Add(s.OccupantId);
                    if (s.MatchedThisFrame) { detected.Add(s.OccupantId); }
                }
            }
            _visibleOccupants = visible.ToArray();
            _detectedOccupants = detected.ToArray();

            // Publish the winner's correlation for Live debug.
            double winnerCorr = double.NaN;
            if (_visualSpeaker >= 1 && _visualSpeaker <= _slots.Count)
            {
                winnerCorr = _slots[_visualSpeaker - 1].AudioCorr;
            }
            _speakerCorrX100 = double.IsNaN(winnerCorr)
                ? -1000
                : (int)Math.Round(Math.Max(-1.0, Math.Min(1.0, winnerCorr)) * 100.0);
            }
        }

        private volatile int _talkingFaces;
        private volatile int _sceneCuts;
        private volatile int _globalX100;
        private volatile int _framesSkipped;

        /// <summary>
        /// Frames where the picture was identical to the previous one, so the OS face
        /// detector was not run. Surfaced in Live debug: on a paused video or a static
        /// screen this climbs steadily and the analyzer's cost collapses to the capture.
        /// </summary>
        public int FramesSkipped => _framesSkipped;

        /// <summary>How many visible faces are articulating right now (0, 1, 2…).</summary>
        public int TalkingFaceCount => _talkingFaces;

        private volatile int _speakerCorrX100 = -1000;

        /// <summary>
        /// How well the chosen speaker's mouth motion tracks the audio, -1..1, or NaN
        /// when there was not enough to say. Surfaced in Live debug: a confident label
        /// backed by a strongly negative correlation is the signature of the analyzer
        /// following the wrong face, and there was previously no way to see that.
        /// </summary>
        public double SpeakerAudioCorrelation =>
            _speakerCorrX100 <= -1000 ? double.NaN : _speakerCorrX100 / 100.0;

        /// <summary>Scene cuts / hard pans skipped by the motion guard this session.</summary>
        public int SceneCutCount => _sceneCuts;

        /// <summary>Whole-frame motion of the last frame pair (mean |Δ| per sampled px).</summary>
        public double GlobalMotion => _globalX100 / 100.0;

        /// <summary>
        /// True while TWO OR MORE faces are visibly talking at once — crosstalk. The
        /// verdict already abstains then (no confident single face); the labeler uses
        /// this to HOLD the current speaker label instead of following the voice
        /// guess, which flip-flops between overlapped voices.
        /// </summary>
        public bool CrossTalk => _talkingFaces >= 2;

        /// <summary>
        /// Feeds one whole-face motion sample into a slot's static-image record.
        /// Static = the ENTIRE box has stayed at the video noise floor for a
        /// sustained stretch — only a picture manages that; a real head blinks,
        /// breathes and carries lighting grain. Deliberately asymmetric: earning
        /// the flag takes seconds of evidence, losing it takes one moving frame.
        /// </summary>
        private static void UpdateStaticState(Slot s, double total, DateTime now)
        {
            if (s.FreshTotal)
            {
                s.TotalMotionEma = total;            // seed: ease-in from 0 must not read as "quiet"
                s.FreshTotal = false;
            }
            else
            {
                // Slow EMA — stillness is a seconds-scale property, and one busy
                // frame (a scene cut flashing over a poster) shouldn't erase the
                // whole quiet history.
                s.TotalMotionEma = s.TotalMotionEma * 0.9 + total * 0.1;
            }

            // Clearly moving RIGHT NOW clears the flag at once — a static face
            // that comes alive (paused video resuming, a portrait that was really
            // a freeze-frame) must return to normal immediately, not after the
            // slow EMA crawls back over the threshold.
            if (total > StaticMotionMax * 3)
            {
                s.IsStatic = false;
                s.QuietSinceUtc = DateTime.MinValue;
                s.QuietSamples = 0;
                return;
            }

            if (s.TotalMotionEma < StaticMotionMax)
            {
                if (s.QuietSinceUtc == DateTime.MinValue) { s.QuietSinceUtc = now; }
                s.QuietSamples++;
                // Both gates on purpose: wall-clock so a burst of fast ticks can't
                // rush the call, sample count so a couple of slow idle ticks can't
                // either.
                if (!s.IsStatic
                    && s.QuietSamples >= StaticMinSamples
                    && (now - s.QuietSinceUtc).TotalMilliseconds >= StaticAfterMs)
                {
                    s.IsStatic = true;
                }
            }
            else
            {
                s.IsStatic = false;
                s.QuietSinceUtc = DateTime.MinValue;
                s.QuietSamples = 0;
            }
        }

        /// <summary>
        /// One line per tracked face for Live debug — position on screen, size,
        /// live mouth-motion level, centre velocity, whether it's been flagged as
        /// a static image, whether the detector sees it right now or it's
        /// coasting through a head turn, and which one is judged to be talking.
        /// </summary>
        public string DebugDetail()
        {
            try
            {
                if (!_running) { return ""; }
                var sb = new System.Text.StringBuilder();
                // The face AI (round 26): what it can do here, and what it has done this session.
                string ai = FaceAiState;
                if (!string.IsNullOrEmpty(ai))
                {
                    FaceAi live = _ai;
                    sb.Append("    face AI: ").Append(ai)
                      .Append(live != null && live.LastDetectMs > 0 ? " · look " + live.LastDetectMs + " ms" : "")
                      .Append(live != null && live.LastFingerprintMs > 0 ? " · print " + live.LastFingerprintMs + " ms" : "").AppendLine();
                }
                lock (_slotLock)
                {
                    for (int i = 0; i < _slots.Count; i++)
                    {
                        var s = _slots[i];
                        if (!s.Alive) { continue; }
                        double cx = s.X + s.W / 2;
                        // _prevW is 0 until the first frame PAIR exists — everything
                        // then compared as > 0 and mislabelled "right".
                        string where = _prevW <= 0 ? "…"
                                     : cx < _prevW * 0.33 ? "left"
                                     : cx > _prevW * 0.67 ? "right" : "centre";
                        sb.Append("    face ").Append(i + 1)
                          .Append(_visualSpeaker == i + 1 ? " ● TALKING" : "        ")
                          // Who it is: the occupant id speakers are linked to, or — for its first
                          // looks — not yet decided between someone seen before and someone new.
                          .Append(s.OccupantId > 0 ? " · #" + s.OccupantId
                              : " · looking (" + s.PendingLooks + "/" + LooksToName + ")")
                          .Append(" · ").Append(where)
                          // How long THIS occupant has held the slot: a short time next
                          // to a long-running conversation marks a newcomer who took over
                          // a departed face's number.
                          .Append(s.OccupiedSinceUtc == DateTime.MinValue ? ""
                              : " · on screen " + (int)(DateTime.UtcNow - s.OccupiedSinceUtc).TotalSeconds + "s")
                          .Append(" · ").Append((int)s.W).Append("px")
                          .Append(" · mouth-motion ").Append(s.MotionEma.ToString("0.0"))
                          .Append(" · vel ")
                          .Append(Math.Sqrt(s.VX * s.VX + s.VY * s.VY).ToString("0.0"))
                          .Append("px/f")
                          .Append(s.IsStatic ? " · STATIC (image?)" : "")
                          .Append(s.IsDrawn ? " · DRAWN (no colour " + s.ChromaEma.ToString("0.0") + ")" : "")
                          .Append(s.MatchedThisFrame ? "" : " · COASTING (head turned)")
                          .Append(Math.Sqrt(s.VX * s.VX + s.VY * s.VY) >= 1.5
                              ? " · MOVING (gate raised)" : "")
                          // The face AI: a fingerprint (from how many pictures) and whether the mouth window follows
                          // its landmarks right now.
                          .Append(s.Print != null ? " · print ×" + s.PrintCount : "")
                          .Append(s.MarksUtc > DateTime.MinValue && (DateTime.UtcNow - s.MarksUtc).TotalSeconds < MarksFreshSec
                              ? " · mouth on landmarks" : "")
                          .AppendLine();
                    }
                    if (_sceneCuts > 0 || _globalX100 > 0)
                    {
                        sb.Append("    scene: motion ").Append(GlobalMotion.ToString("0.0"))
                          .Append(" · ").Append(_sceneCuts).Append(" cuts skipped")
                          .AppendLine();
                    }
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        /// <summary>
        /// Whole-frame mean |Δ| against the previous frame, sampled on an 8×8 grid —
        /// cheap (~1% of pixels) and plenty to tell a scene cut from normal motion.
        /// </summary>
        private static double FrameMeanMotion(byte[] cur, byte[] prev, int w, int h)
        {
            long sum = 0;
            int n = 0;
            for (int y = 0; y < h; y += 8)
            {
                int row = y * w;
                for (int x = 0; x < w; x += 8)
                {
                    sum += Math.Abs(cur[row + x] - prev[row + x]);
                    n++;
                }
            }
            return n > 0 ? (double)sum / n : 0;
        }

        /// <summary>
        /// Mean |Δ| against the previous frame inside the given box. When the face is
        /// MOVING, <paramref name="prevShiftX"/>/<paramref name="prevShiftY"/> carry
        /// its per-frame velocity: the previous frame is then sampled shifted back by
        /// that amount — comparing the mouth against where the mouth WAS — so pure
        /// translation cancels out of the measurement instead of reading as motion.
        /// (The upper-face subtraction already cancels most of it; this removes the
        /// residual mis-alignment leak on fast movers.)
        /// </summary>
        private double RegionMotion(byte[] gray, int w, int hgt, double x0, double y0, double x1, double y1,
            double prevShiftX = 0, double prevShiftY = 0)
        {
            int ox = (int)Math.Round(prevShiftX), oy = (int)Math.Round(prevShiftY);
            int ax0 = Math.Max(0, (int)x0), ay0 = Math.Max(0, (int)y0);
            int ax1 = Math.Min(w - 1, (int)x1), ay1 = Math.Min(hgt - 1, (int)y1);
            // Keep the SHIFTED previous-frame read in bounds too, shrinking the
            // region rather than clamping per pixel (which would smear the edge).
            if (ox > 0) { ax0 = Math.Max(ax0, ox); } else if (ox < 0) { ax1 = Math.Min(ax1, w - 1 + ox); }
            if (oy > 0) { ay0 = Math.Max(ay0, oy); } else if (oy < 0) { ay1 = Math.Min(ay1, hgt - 1 + oy); }
            long sum = 0; int n = 0;
            for (int y = ay0; y <= ay1; y++)
            {
                int off = y * w;
                int prevOff = (y - oy) * w - ox;
                for (int x = ax0; x <= ax1; x++)
                {
                    sum += Math.Abs(gray[off + x] - _prevGray[prevOff + x]);
                    n++;
                }
            }
            return n > 0 ? sum / (double)n : 0;
        }
    }
}
