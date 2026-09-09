using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Plays an animated custom logo — the title bar, the taskbar button, the tray icon
    /// and every open dialog, all on the same frame at the same time.
    ///
    /// A GIF has always been accepted as a custom logo and has always animated in one
    /// place: the About page, where a PictureBox does it for free. Everywhere the logo is
    /// an ICON rather than a picture it was frozen on frame 0, because an Icon holds a
    /// still image and nothing was advancing it. This is what advances it.
    ///
    /// The shape of the thing:
    ///
    ///   * Every frame is rendered ONCE, up front, into a real multi-size .ico. Resampling
    ///     a frame each time it came round would put a bicubic downscale of a 500px source
    ///     on the UI thread ten times a second; doing it once costs some memory and then
    ///     nothing at all. Playback is an array index and a property set.
    ///   * That rendering happens on a background thread, so a 60-frame logo does not
    ///     stall startup. Until it finishes, the ordinary still-frame path is showing —
    ///     the logo appears instantly and starts moving a moment later.
    ///   * The ink rectangle is measured across ALL frames and then held fixed, so the
    ///     mark does not pump against its own bounding box. See AppIcon.RenderSquare.
    ///
    /// Nothing here is required for Tempo to run. Every failure path ends in "no
    /// animation", which is exactly what the app did before.
    /// </summary>
    public static class AnimatedLogo
    {
        private sealed class LogoFrame
        {
            public Icon Icon;      // multi-size: window, taskbar, tray, dialogs
            public Bitmap Tile;    // 64px, for the header tile
            public int DelayMs;

            /// <summary>Milliseconds from the start of the loop to this frame.</summary>
            public int StartMs;
        }

        /// <summary>
        /// The sizes packed into each animated frame.
        ///
        /// Shorter than the still icon's list, which runs to 256. An animated icon is only
        /// ever seen small — 16-24 in a title bar and the tray, 32-64 on the taskbar and in
        /// Alt-Tab — and a 256px frame is by far the most expensive one to resample and
        /// the largest to hold. Paying for it on every frame of an animation nobody can
        /// see at that size would be the whole cost of this feature for none of the
        /// benefit. Anything asking for a big still (a toast thumbnail) goes on using
        /// AppIcon.GetBitmap, which renders from the file.
        /// </summary>
        private static readonly int[] AnimationSizes = { 16, 20, 24, 32, 48, 64 };

        /// <summary>The size of <see cref="LogoFrame.Tile"/>; must be one of <see cref="AnimationSizes"/>.</summary>
        private const int TileSize = 64;

        /// <summary>
        /// Frames past which the logo is shown still instead.
        ///
        /// A logo is a handful of frames; 200 is already a ten-second loop. The cap is not
        /// really about memory — it is that the alternative to refusing is truncating, and
        /// a loop cut off part-way visibly snaps back every time it repeats, which reads
        /// as a bug. A still logo reads as a decision.
        /// </summary>
        private const int MaxFrames = 200;

        /// <summary>
        /// Playback floor, i.e. a 20 fps ceiling.
        ///
        /// GIFs in the wild routinely carry 10-20 ms delays and mean nothing by it. The
        /// backdrop code caps at 30 fps for the same reason; an icon needs less, since
        /// every frame of it is a WM_SETICON per window plus a Shell_NotifyIcon for the
        /// tray, and no one can see 20 changes a second in a 16px square anyway.
        /// </summary>
        private const int MinDelayMs = 50;

        /// <summary>
        /// What a 0 (or absurdly small) frame delay is played at.
        ///
        /// Every renderer since Netscape has treated "as fast as possible" as 100 ms, and
        /// a great many GIFs are authored against that behaviour rather than against the
        /// number they actually contain. Honouring the literal value would spin those far
        /// too fast to read.
        /// </summary>
        private const int DefaultDelayMs = 100;

        private static List<LogoFrame> _frames;
        private static List<LogoFrame> _pending;
        private static string _pendingStatus;
        private static volatile bool _building;

        private static int _index;
        private static Timer _timer;
        private static string _status = "no custom logo";
        private static bool _enabled = true;

        /// <summary>
        /// Real time since the last tick, which is NOT the interval we asked for.
        ///
        /// A WinForms Timer is WM_TIMER, and WM_TIMER only fires on the system clock tick
        /// (~15.6 ms) however fine a resolution the process has requested. A 65 ms request
        /// arrives at 78 ms and a 50 ms request at 62.5 ms, so treating Interval as the
        /// elapsed time ran a 1.5 s logo at 1.69 s — measured — and left the fast settings
        /// short of what they claimed. Advancing by the clock instead makes the loop take
        /// what the setting says whatever the timer actually does.
        /// </summary>
        private static readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();

        /// <summary>Playback speed as a percentage of the file's own timing; 100 = as authored.</summary>
        private static int _speedPercent = 100;

        /// <summary>
        /// How fast to play, relative to the GIF's own frame delays. 100 is exactly as the
        /// file was authored.
        ///
        /// Clamped to 10-400 rather than left open: a 10x slowdown already parks a frame
        /// for the better part of a minute, which reads as "the animation broke".
        ///
        /// The speed sets how long the LOOP takes; MinDelayMs separately caps how often
        /// the icon may be repainted. Those are different limits and they no longer fight:
        /// when the requested rate outruns the repaint budget, OnTick skips frames instead
        /// of refusing to go faster. An earlier version applied the floor to every frame
        /// delay, which made 4x on a 23-frame logo come out 1.3x -- indistinguishable from
        /// normal, which is exactly how it was reported.
        /// </summary>
        public static int SpeedPercent
        {
            get { return _speedPercent; }
            set
            {
                int v = value < 10 ? 10 : (value > 400 ? 400 : value);
                if (v == _speedPercent) { return; }
                _speedPercent = v;
                // Re-pace immediately: the running timer holds the OLD frame's interval,
                // so without this a change does nothing until the next frame lands — and
                // at a slow speed that could be seconds away.
                try
                {
                    if (_timer != null && _frames != null && _frames.Count > 0)
                    {
                        int i = _index >= 0 && _index < _frames.Count ? _index : 0;
                        double factor = Math.Max(0.1, v / 100.0);
                        double remaining = (_frames[i].StartMs + _frames[i].DelayMs) - _authoredPos;
                        if (remaining <= 0) { remaining = _frames[i].DelayMs; }
                        int next = (int)Math.Round(remaining / factor);
                        _timer.Interval = Math.Max(MinDelayMs, Math.Min(60000, next));
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Raised on the UI thread whenever the showing frame changes, so the window, the
        /// tray and the header can repaint. Also raised once when an animation starts or
        /// stops, so listeners settle on the right still frame.
        /// </summary>
        public static event Action FrameChanged;

        /// <summary>Whether a logo is playing right now.</summary>
        public static bool IsAnimating => _frames != null && _frames.Count > 1;

        /// <summary>The loop's length as authored, in milliseconds.</summary>
        private static int _authoredLoopMs;

        /// <summary>Where playback currently sits in the AUTHORED timeline, in ms.</summary>
        private static double _authoredPos;

        /// <summary>
        /// The frame showing at a given point in the authored loop. Linear over a list
        /// capped at 200 entries and walked once per tick — a binary search would be
        /// faster in theory and unmeasurable here.
        /// </summary>
        private static int FrameAtAuthored(double ms)
        {
            var f = _frames;
            if (f == null || f.Count == 0) { return 0; }
            for (int i = f.Count - 1; i >= 0; i--)
            {
                if (ms >= f[i].StartMs) { return i; }
            }
            return 0;
        }

        /// <summary>
        /// How long one loop actually takes at the current speed, in seconds, or 0 when
        /// nothing is animating.
        ///
        /// Not the authored figure: Status is built once when the frames are rendered, so
        /// it can only ever quote the file's own timing. Changing the speed afterwards
        /// would leave the UI stating a loop length that is no longer true.
        /// </summary>
        public static double EffectiveLoopSeconds
        {
            get
            {
                var f = _frames;
                if (f == null || f.Count == 0 || _authoredLoopMs <= 0) { return 0; }
                // The clock runs through the authored timeline at exactly this rate, so
                // the loop takes authored/speed — whatever gets skipped along the way.
                // Summing the per-frame delays would overstate a fast loop, because fast
                // playback drops frames rather than shortening every one of them.
                return _authoredLoopMs / Math.Max(0.1, _speedPercent / 100.0) / 1000.0;
            }
        }

        /// <summary>Frames in the running animation, or 0.</summary>
        public static int FrameCount => _frames?.Count ?? 0;

        /// <summary>
        /// One line for the debug panel saying what happened to the logo — playing, still,
        /// or the reason it is not animating. Without this, "my GIF isn't moving" has no
        /// answer short of a debugger.
        /// </summary>
        public static string Status => _status;

        /// <summary>
        /// The frame currently showing, or null when nothing is animating.
        /// AppIcon.Get() defers to this so a dialog opening mid-animation is on the same
        /// frame as the window that opened it.
        /// </summary>
        public static Icon CurrentIcon
        {
            get
            {
                List<LogoFrame> f = _frames;
                if (f == null || f.Count == 0) { return null; }
                int i = _index;
                return i >= 0 && i < f.Count ? f[i].Icon : f[0].Icon;
            }
        }

        /// <summary>
        /// The frame currently showing, at <see cref="TileSize"/> px, for the header tile.
        /// Borrowed, NOT owned — the caller must not dispose it.
        /// </summary>
        public static Image CurrentTile
        {
            get
            {
                List<LogoFrame> f = _frames;
                if (f == null || f.Count == 0) { return null; }
                int i = _index;
                return i >= 0 && i < f.Count ? f[i].Tile : f[0].Tile;
            }
        }

        /// <summary>
        /// Turns animation on or off (the About page's "Animate" tick). Off leaves the
        /// still first frame, which is what a custom logo looked like before this existed.
        /// </summary>
        public static void SetEnabled(bool on)
        {
            if (_enabled == on) { return; }
            _enabled = on;
            Reload();
        }

        public static bool Enabled => _enabled;

        /// <summary>
        /// Rebuilds from whatever custom logo is on disk now. Safe to call from the UI
        /// thread at any time; the expensive part happens elsewhere.
        ///
        /// Called by AppIcon.Reset(), which is the single point every "the logo changed"
        /// path already goes through — so this does not subscribe to CustomLogo.Changed
        /// itself, and there is no ordering question between two handlers racing to
        /// rebuild the same thing.
        /// </summary>
        public static void Reload()
        {
            StopTimer();

            // Dropped, not disposed. An icon from a previous animation may still be held
            // by an open dialog's title bar or Alt-Tab entry; AppIcon.Reset() explains the
            // same reasoning at more length. These are a bounded set released on
            // finalisation, and a logo change is a rare, user-driven event.
            _frames = null;
            _pending = null;
            _index = 0;

            if (!_enabled)
            {
                _status = "animation turned off";
                RaiseFrameChanged();
                return;
            }

            string path = null;
            try { path = CustomLogo.GetPath(); } catch { }
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                _status = "no custom logo";
                RaiseFrameChanged();
                return;
            }

            _status = "reading the logo…";
            RaiseFrameChanged();

            // The Timer must be created on the thread that will pump it, and this is that
            // thread. It starts before the frames exist and adopts them when they land,
            // which is why there is no SynchronizationContext to capture and no dependency
            // on a form being alive yet: the poll IS the marshal, and it stops itself if
            // the build produces nothing.
            StartTimer(120);

            _building = true;
            string src = path;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                List<LogoFrame> built = null;
                string status;
                try
                {
                    built = Build(src, out status);
                }
                catch (Exception ex)
                {
                    status = "could not be read (" + ex.Message + ")";
                    Logger.Warn("[Logo] animated logo build failed: " + ex.Message);
                }
                _pendingStatus = status;
                _pending = built;
                _building = false;
            });
        }

        /// <summary>
        /// Decodes and renders every frame. Runs off the UI thread.
        /// Returns null (with a reason) when the file is not something to animate.
        /// </summary>
        private static List<LogoFrame> Build(string path, out string status)
        {
            // Read into memory rather than Image.FromFile: that holds the file open for
            // the life of the Image, and the logo lives in a folder the user can replace
            // a file in at any moment.
            byte[] raw = File.ReadAllBytes(path);
            string name = Path.GetFileName(path);

            using (var ms = new MemoryStream(raw))
            using (var img = Image.FromStream(ms))
            {
                Guid[] dims = img.FrameDimensionsList;
                if (dims == null || dims.Length == 0)
                {
                    status = name + " · still image";
                    return null;
                }

                var dim = new FrameDimension(dims[0]);
                int count;
                try { count = img.GetFrameCount(dim); }
                catch { status = name + " · still image"; return null; }

                if (count <= 1)
                {
                    status = name + " · still image";
                    return null;
                }
                if (count > MaxFrames)
                {
                    status = name + " · " + count + " frames is over the " + MaxFrames +
                             "-frame limit, showing it still";
                    Logger.Info("[Logo] " + status);
                    return null;
                }

                var bmp = img as Bitmap;
                int[] delays = ReadDelays(img, count);

                // Pass one: where the ink is, across every frame. Measured before anything
                // is rendered because every frame has to be fitted to the SAME rectangle.
                Rectangle ink = Rectangle.Empty;
                if (bmp != null)
                {
                    for (int i = 0; i < count; i++)
                    {
                        img.SelectActiveFrame(dim, i);
                        Rectangle r = AppIcon.InkedBounds(bmp);
                        ink = ink.IsEmpty ? r : Rectangle.Union(ink, r);
                    }
                }
                if (ink.IsEmpty)
                {
                    ink = new Rectangle(0, 0, img.Width, img.Height);
                }

                // Pass two: render. One frame of the source is decoded at a time and
                // released as soon as its small copies exist, so peak memory is one frame
                // of the GIF plus the finished set — not the whole animation at full size.
                var frames = new List<LogoFrame>(count);
                for (int i = 0; i < count; i++)
                {
                    img.SelectActiveFrame(dim, i);

                    byte[] ico = AppIcon.BuildIcoBytes(img, ink, AnimationSizes);
                    frames.Add(new LogoFrame
                    {
                        Icon = AppIcon.IconFromIcoBytes(ico),
                        Tile = AppIcon.RenderSquare(img, TileSize, ink),
                        DelayMs = Math.Max(MinDelayMs, delays[i]),
                    });
                }

                int total = 0;
                foreach (LogoFrame f in frames) { f.StartMs = total; total += f.DelayMs; }
                _authoredLoopMs = total;
                status = name + " · " + count + " frames · " + (total / 1000.0).ToString("0.0") + "s loop";
                return frames;
            }
        }

        /// <summary>
        /// Per-frame delays in milliseconds, defaulted where the file does not say.
        ///
        /// GIF keeps them in one property: an array of little-endian int32 centiseconds,
        /// one per frame. GetPropertyItem throws rather than returning null when the tag
        /// is absent, which is the normal case for a single-frame image.
        /// </summary>
        private static int[] ReadDelays(Image img, int count)
        {
            var delays = new int[count];
            for (int i = 0; i < count; i++) { delays[i] = DefaultDelayMs; }

            try
            {
                const int PropertyTagFrameDelay = 0x5100;
                PropertyItem item = img.GetPropertyItem(PropertyTagFrameDelay);
                byte[] v = item?.Value;
                if (v == null) { return delays; }

                for (int i = 0; i < count && (i + 1) * 4 <= v.Length; i++)
                {
                    int ms = BitConverter.ToInt32(v, i * 4) * 10;
                    delays[i] = ms < 20 ? DefaultDelayMs : ms;
                }
            }
            catch
            {
                // No delay property: every frame keeps the default.
            }
            return delays;
        }

        private static void StartTimer(int intervalMs)
        {
            if (_timer == null)
            {
                _timer = new Timer();
                _timer.Tick += OnTick;
            }
            _timer.Interval = Math.Max(1, intervalMs);
            _timer.Start();
            _clock.Restart();
        }

        private static void StopTimer()
        {
            try { _timer?.Stop(); } catch { }
            _clock.Reset();
        }

        private static void OnTick(object sender, EventArgs e)
        {
            // Still waiting on the background build.
            if (_frames == null)
            {
                if (_building) { return; }

                List<LogoFrame> built = _pending;
                _pending = null;
                _status = _pendingStatus ?? _status;

                if (built == null || built.Count <= 1)
                {
                    // Nothing to play. The still path is already showing the right thing,
                    // so stop rather than tick forever over a static logo.
                    StopTimer();
                    RaiseFrameChanged();
                    return;
                }

                _frames = built;
                _index = 0;
                _authoredPos = 0;
                _timer.Interval = Math.Max(MinDelayMs, Math.Min(60000,
                    (int)Math.Round(built[0].DelayMs / Math.Max(0.1, _speedPercent / 100.0))));
                // Frame 0 starts now; the wait for the frames is not playback time.
                _clock.Restart();
                Logger.Info("[Logo] animating: " + _status);
                RaiseFrameChanged();
                return;
            }

            // Advance a CLOCK through the authored loop, rather than stepping one frame
            // per tick.
            //
            // Stepping per frame made the fast settings do almost nothing. The tick
            // interval cannot go below MinDelayMs — every frame is a WM_SETICON on each
            // open window plus a Shell_NotifyIcon for the tray — so on a typical logo
            // (23 frames over 1.5 s, ~65 ms each) asking for 4× produced a 50 ms floor
            // and a loop only 1.3× faster. It looked like nothing had changed, because
            // very nearly nothing had.
            //
            // The speed the user picked is a statement about how long the LOOP should
            // take, and the floor is a limit on how often the icon may be repainted.
            // Those are different things, and they only conflict if every frame has to be
            // shown. So: move a position through the authored timeline at the chosen
            // rate, draw whichever frame that position lands on, and let frames be
            // skipped when the rate outruns the repaint budget — which is exactly what a
            // video player does when it cannot keep up. 4× now takes a quarter of the
            // time whatever the frame count, and the repaint rate never exceeds 20/s.
            double factor = Math.Max(0.1, _speedPercent / 100.0);
            int loopMs = _authoredLoopMs > 0 ? _authoredLoopMs : 1;

            // Advance by the time that actually passed. See _clock: the timer fires later
            // than it was asked to, and by a margin that changes with the interval, so
            // Interval understates every step and does it unevenly.
            double elapsed = _clock.IsRunning ? _clock.Elapsed.TotalMilliseconds : _timer.Interval;
            _clock.Restart();

            // A gap this long is the machine having been asleep or the UI thread having
            // been blocked, not playback. Stepping the whole gap would fling the logo
            // through several loops in one frame; carry on from here instead.
            if (elapsed > 500) { elapsed = 500; }

            _authoredPos += elapsed * factor;
            if (_authoredPos >= loopMs || _authoredPos < 0)
            {
                _authoredPos %= loopMs;
                if (_authoredPos < 0) { _authoredPos += loopMs; }
            }

            int idx = FrameAtAuthored(_authoredPos);
            _index = idx;

            // Wait until this frame's own end, in real time. A slow speed stretches it, a
            // fast one shortens it into the floor and the next tick simply lands further
            // along the timeline.
            double remaining = (_frames[idx].StartMs + _frames[idx].DelayMs) - _authoredPos;
            if (remaining <= 0) { remaining = _frames[idx].DelayMs; }

            // Ask for a little less than the frame needs. WM_TIMER rounds the request UP
            // to the next system tick, so asking for exactly the frame's length arrives
            // ~15 ms late and skips a frame that had time to show. The clock above keeps
            // the loop honest either way; this only stops 1x dropping frames it needn't.
            int delay = (int)Math.Round(remaining / factor) - 8;
            if (delay < MinDelayMs) { delay = MinDelayMs; }
            if (delay > 60000) { delay = 60000; }
            if (_timer.Interval != delay) { _timer.Interval = delay; }

            RaiseFrameChanged();
        }

        private static void RaiseFrameChanged()
        {
            try { FrameChanged?.Invoke(); }
            catch (Exception ex) { Logger.Swallow("AnimatedLogo.FrameChanged", ex); }
        }
    }
}
