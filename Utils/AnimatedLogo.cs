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
        /// Raised on the UI thread whenever the showing frame changes, so the window, the
        /// tray and the header can repaint. Also raised once when an animation starts or
        /// stops, so listeners settle on the right still frame.
        /// </summary>
        public static event Action FrameChanged;

        /// <summary>Whether a logo is playing right now.</summary>
        public static bool IsAnimating => _frames != null && _frames.Count > 1;

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
                foreach (LogoFrame f in frames) { total += f.DelayMs; }
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
        }

        private static void StopTimer()
        {
            try { _timer?.Stop(); } catch { }
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
                _timer.Interval = built[0].DelayMs;
                Logger.Info("[Logo] animating: " + _status);
                RaiseFrameChanged();
                return;
            }

            _index = (_index + 1) % _frames.Count;

            // Frame delays vary within one GIF, so the interval is per frame rather than
            // an average. Assigning Interval restarts the timer, which is the intent.
            int delay = _frames[_index].DelayMs;
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
