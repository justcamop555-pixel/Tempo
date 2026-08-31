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
        private const int MaxSlots = 4;
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

        private sealed class Slot
        {
            public double X, Y, W, H;                // smoothed face box (analysis coords)
            public double MotionEma;
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
        }
        private readonly List<Slot> _slots = new List<Slot>();
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
            _faceCount = 0;
            _talkingFaces = 0;
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
            _blankStreak = 0;
            if (idle)
            {
                try { _fullBmp?.Dispose(); } catch { }
                try { _frameBmp?.Dispose(); } catch { }
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
            lock (_slotLock)
            {
                _slots.Clear();
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

        private void AnalyzeOneFrame()
        {
            // 1. Pick the window to read: prefer the one PLAYING AUDIO (any app, even
            //    behind other windows), else the foreground window.
            IntPtr preferred = IntPtr.Zero;
            try { preferred = PreferredWindow != null ? PreferredWindow() : IntPtr.Zero; } catch { }
            bool usePreferred = preferred != IntPtr.Zero && IsWindow(preferred) && !IsIconic(preferred);
            IntPtr h = usePreferred ? preferred : GetForegroundWindow();
            if (h == IntPtr.Zero || !GetWindowRect(h, out RECT r))
            {
                return;
            }
            int srcW = r.Right - r.Left, srcH = r.Bottom - r.Top;
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
                        // PrintWindow reads the window's own surface, so it works for
                        // windows sitting BEHIND others; PW_RENDERFULLCONTENT makes
                        // DWM include hardware-accelerated video frames.
                        if (usePreferred)
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

            DateTime now = DateTime.UtcNow;

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
                if (best == null)
                {
                    // Recycle an expired slot first — videos cut between scenes
                    // constantly, and without reuse the tracker ran out of slots
                    // after the first few faces it ever saw and went blind.
                    foreach (var s in _slots)
                    {
                        if (!s.Alive) { best = s; break; }
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
                    double mouth = RegionMotion(gray, w, hgt,
                        best.X + best.W * 0.25, best.Y + best.H * 0.62,
                        best.X + best.W * 0.75, best.Y + best.H * 0.95, mvx, mvy);
                    double upper = RegionMotion(gray, w, hgt,
                        best.X + best.W * 0.25, best.Y + best.H * 0.12,
                        best.X + best.W * 0.75, best.Y + best.H * 0.45, mvx, mvy);
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

                    // Whole-box motion feeds the static-image detector. Sampled here
                    // (not just the mouth strip) because a poster's giveaway is that
                    // NOTHING in the box moves — no blink, no breath, no lighting
                    // grain — while its mouth region alone can flicker with noise.
                    UpdateStaticState(best,
                        RegionMotion(gray, w, hgt, best.X, best.Y, best.X + best.W, best.Y + best.H),
                        now);
                }
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
                    double mouth = RegionMotion(gray, w, hgt,
                        s.X + s.W * 0.25, s.Y + s.H * 0.62,
                        s.X + s.W * 0.75, s.Y + s.H * 0.95, drvx, drvy);
                    double upper = RegionMotion(gray, w, hgt,
                        s.X + s.W * 0.25, s.Y + s.H * 0.12,
                        s.X + s.W * 0.75, s.Y + s.H * 0.45, drvx, drvy);
                    double net = Math.Max(0, mouth - upper * 1.1);
                    s.MotionEma = s.MotionEma * 0.7 + net * 0.3;

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
                    s.Alive = false;
                    s.MotionEma = 0;
                }
                if (s.Alive) alive++;
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
                // mouth region's compression noise reads, it is not talking.
                if (s.IsStatic) continue;
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
                          .Append(" · ").Append(where)
                          .Append(" · ").Append((int)s.W).Append("px")
                          .Append(" · mouth-motion ").Append(s.MotionEma.ToString("0.0"))
                          .Append(" · vel ")
                          .Append(Math.Sqrt(s.VX * s.VX + s.VY * s.VY).ToString("0.0"))
                          .Append("px/f")
                          .Append(s.IsStatic ? " · STATIC (image?)" : "")
                          .Append(s.MatchedThisFrame ? "" : " · COASTING (head turned)")
                          .Append(Math.Sqrt(s.VX * s.VX + s.VY * s.VY) >= 1.5
                              ? " · MOVING (gate raised)" : "")
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
