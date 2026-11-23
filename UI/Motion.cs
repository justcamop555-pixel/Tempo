using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace AutoClicker.UI
{
    /// <summary>
    /// What Tempo's animations share: one switch, the curves, and a frame clock.
    ///
    /// The switch is Windows' own — Settings › Accessibility › Visual effects › Animation effects. Someone who turned
    /// motion off for the whole of Windows (vestibular trouble, a slow PC, a remote session) gets Tempo without it too:
    /// every animation then ends on its first frame, in its final state.
    ///
    /// The clock paces frames by the screen. A background thread waits on DWM (DwmFlush returns once the next frame has
    /// been composed) and hands the UI thread one tick per screen refresh; a WinForms timer can only tick on Windows'
    /// ~15.6 ms grid, which beats against a 60 Hz screen and makes a short motion visibly hitch. The thread exists only
    /// while something is animating and waits on an event otherwise: an idle Tempo pays nothing for it.
    /// </summary>
    public static class Motion
    {
        // ── The switch ───────────────────────────────────────────────────────────────────────────────

        private static bool _enabled = true;
        private static long _enabledReadAt = long.MinValue;

        /// <summary>
        /// False when Windows' "Animation effects" is off (SPI_GETCLIENTAREAANIMATION), or <see cref="ForceOff"/> is set.
        /// Read again at most every two seconds, so turning it off in Windows takes effect without a restart.
        /// </summary>
        public static bool Enabled
        {
            get
            {
                if (ForceOff) { return false; }
                // A PC that can't keep up with the frames: they rest for a while (see NoteFrameCost).
                if (Resting) { return false; }
                long now = Environment.TickCount64;
                if (now - _enabledReadAt > 2000)
                {
                    _enabledReadAt = now;
                    try
                    {
                        bool on = true;
                        if (SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, ref on, 0)) { _enabled = on; }
                    }
                    catch { _enabled = true; }
                }
                return _enabled;
            }
        }

        /// <summary>For tests: every animation off, whatever Windows says.</summary>
        public static bool ForceOff;

        // ── The curves ───────────────────────────────────────────────────────────────────────────────

        public static double Clamp01(double t) => t <= 0 ? 0 : t >= 1 ? 1 : t;

        /// <summary>Fast away, soft landing — for anything arriving.</summary>
        public static double EaseOutCubic(double t) { t = 1 - Clamp01(t); return 1 - t * t * t; }

        /// <summary>A firmer landing than cubic.</summary>
        public static double EaseOutQuint(double t) { t = 1 - Clamp01(t); return 1 - t * t * t * t * t; }

        /// <summary>Slow start, quick finish — for anything leaving.</summary>
        public static double EaseInCubic(double t) { t = Clamp01(t); return t * t * t; }

        public static double EaseInQuad(double t) { t = Clamp01(t); return t * t; }

        public static double EaseInOutCubic(double t)
        {
            t = Clamp01(t);
            return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        }

        /// <summary>Between <paramref name="a"/> and <paramref name="b"/> at <paramref name="t"/> (0..1).</summary>
        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        // ── The clock ────────────────────────────────────────────────────────────────────────────────

        /// <summary>A running animation; <see cref="Stop"/> ends it early.</summary>
        public sealed class Animation
        {
            internal readonly Action<double> Frame;
            internal readonly Action Done;
            internal readonly double DurationMs;
            internal long StartTicks;
            /// <summary>
            /// Whether its clock has started. It starts at the first frame tick, not at Start: an animation started at the
            /// end of heavy work (a theme change repainting the whole window) used to find its first tick 55 ms late and the
            /// next 90 ms after that — the UI thread was still clearing what the work had queued — and a 220 ms cross-fade
            /// arrived as one step (filmed by scratchpad/animcheck, 2026-11-10). Costs at most one frame.
            /// </summary>
            internal bool Clocked;
            internal bool Ended;

            internal Animation(double durationMs, Action<double> frame, Action done)
            {
                DurationMs = Math.Max(1, durationMs);
                Frame = frame;
                Done = done;
            }

            /// <summary>Whether it is still running.</summary>
            public bool Running => !Ended;

            /// <summary>
            /// Ends it now. <paramref name="finish"/>: show its last frame first (t = 1) — otherwise it stays where it
            /// is. Its done callback runs either way, once.
            /// </summary>
            public void Stop(bool finish = false)
            {
                if (Ended) { return; }
                Ended = true;
                _animations.Remove(this);
                if (finish) { SafeFrame(this, 1.0); }
                SafeDone(this);
            }
        }

        private static readonly List<Animation> _animations = new List<Animation>();
        private static readonly List<Action> _continuous = new List<Action>();
        private static readonly Stopwatch _clock = Stopwatch.StartNew();
        private static SynchronizationContext _ui;
        private static Thread _pump;
        private static readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private static volatile bool _wanted;
        private static int _tickQueued;
        private static readonly SendOrPostCallback _tick = _ => Tick();

        /// <summary>Milliseconds on the clock the animations run by.</summary>
        public static double NowMs => _clock.Elapsed.TotalMilliseconds;

        // ── For Live debug ───────────────────────────────────────────────────────────────────────────
        /// <summary>Animations running now (a tab transition, a cross-fade).</summary>
        public static int RunningCount => _animations.Count;
        /// <summary>Things taking every frame for as long as they are shown (spinning rings).</summary>
        public static int ContinuousCount => _continuous.Count;
        /// <summary>Whether the frame clock is ticking (false: parked, costing nothing).</summary>
        public static bool Ticking => _wanted;
        /// <summary>Frames ticked since start, and animations played to their end.</summary>
        public static long Frames, Played;
        /// <summary>When the continuous ones last went from none to some (NowMs), or -1 while there are none.</summary>
        public static double ContinuousSinceMs = -1;

        /// <summary>
        /// Remembers the UI thread the clock ticks on. Called by the main window as it is made; every other call into
        /// the clock comes from that thread.
        /// </summary>
        public static void Init()
        {
            if (_ui == null && SynchronizationContext.Current is System.Windows.Forms.WindowsFormsSynchronizationContext ctx)
            {
                _ui = ctx;
            }
        }

        /// <summary>
        /// Runs <paramref name="frame"/> with t from 0 to 1 over <paramref name="durationMs"/>, once per screen refresh,
        /// then <paramref name="done"/>. On the UI thread. With animations off (or no clock), t = 1 at once and done
        /// follows: whatever the animation leads to still happens.
        /// </summary>
        public static Animation Start(double durationMs, Action<double> frame, Action done = null)
        {
            var a = new Animation(durationMs, frame, done);
            Init();
            if (!Enabled || _ui == null)
            {
                a.Ended = true;
                SafeFrame(a, 1.0);
                SafeDone(a);
                return a;
            }
            a.StartTicks = _clock.ElapsedTicks;
            _animations.Add(a);
            SafeFrame(a, 0.0);
            Wake();
            return a;
        }

        /// <summary>
        /// Calls <paramref name="tick"/> on every frame until <see cref="Unsubscribe"/> — for things that move for as long
        /// as they are shown (a spinner). On the UI thread.
        /// </summary>
        public static void Subscribe(Action tick)
        {
            if (tick == null || _continuous.Contains(tick)) { return; }
            Init();
            if (_ui == null) { return; }
            if (_continuous.Count == 0) { ContinuousSinceMs = NowMs; }
            _continuous.Add(tick);
            Wake();
        }

        /// <summary>True while <paramref name="tick"/> is on the clock (false where Subscribe found no clock to put it on).</summary>
        public static bool IsSubscribed(Action tick) => tick != null && _continuous.Contains(tick);

        public static void Unsubscribe(Action tick)
        {
            if (tick != null && _continuous.Remove(tick) && _continuous.Count == 0) { ContinuousSinceMs = -1; }
        }

        // ── A "no" ───────────────────────────────────────────────────────────────────────────────────

        private sealed class ShakeState
        {
            public int Left;
            public Animation Anim;
        }

        private static readonly Dictionary<System.Windows.Forms.Control, ShakeState> _shakes =
            new Dictionary<System.Windows.Forms.Control, ShakeState>();

        /// <summary>For tests and Live debug: shakes started so far.</summary>
        public static int ShakesStarted;

        /// <summary>
        /// A short sideways shake of <paramref name="c"/> — a wrong password, a form that will not go through (2026-11-10,
        /// the Accounts tab's unlock and create cards): three swings dying away over 360 ms, ending exactly where it stood.
        /// The answer to a refused password used to be one line of small red text under the field, easy to miss while
        /// looking at the button just pressed. With animations off in Windows nothing moves. UI thread.
        /// </summary>
        public static void Shake(System.Windows.Forms.Control c, int amplitude96 = 9)
        {
            if (c == null || c.IsDisposed || !c.IsHandleCreated || !Enabled) { return; }
            // One still going: back where it stood first, so this one starts (and ends) from the true place.
            if (_shakes.TryGetValue(c, out ShakeState running)) { running.Anim.Stop(); }
            var state = new ShakeState { Left = c.Left };
            int amplitude = Math.Max(3, (int)Math.Round(amplitude96 * c.DeviceDpi / 96.0));
            _shakes[c] = state;
            ShakesStarted++;
            state.Anim = Start(360, t =>
            {
                if (c.IsDisposed) { return; }
                double swing = amplitude * (1 - t) * (1 - t) * Math.Sin(t * 3 * 2 * Math.PI);
                int left = state.Left + (int)Math.Round(swing);
                if (c.Left != left) { c.Left = left; }
            }, () =>
            {
                if (_shakes.TryGetValue(c, out ShakeState mine) && ReferenceEquals(mine, state)) { _shakes.Remove(c); }
                try { if (!c.IsDisposed && c.Left != state.Left) { c.Left = state.Left; } }
                catch { }
            });
        }

        private static void Wake()
        {
            _wanted = true;
            if (_pump == null)
            {
                _pump = new Thread(Pump) { IsBackground = true, Name = "Tempo motion clock", Priority = ThreadPriority.AboveNormal };
                _pump.Start();
            }
            _wake.Set();
        }

        /// <summary>The background half: one tick per composed frame, never more than one waiting.</summary>
        private static void Pump()
        {
            long last = _clock.ElapsedTicks;
            while (true)
            {
                if (!_wanted)
                {
                    _wake.WaitOne();
                    last = _clock.ElapsedTicks;
                    continue;
                }
                long before = _clock.ElapsedTicks;
                // The next frame as the screen shows it — but never more than a frame and a bit: a DWM that stalls (a display
                // asleep, some laptop drivers) used to hold every animation half-way for as long as it did (FramePacer).
                bool frame = FramePacer.WaitFrame(20);
                double tookMs = (_clock.ElapsedTicks - before) * 1000.0 / Stopwatch.Frequency;
                if (frame && tookMs < 2.0)
                {
                    // Nothing for DWM to compose yet: about one 60 Hz frame from the last tick.
                    double sinceMs = (_clock.ElapsedTicks - last) * 1000.0 / Stopwatch.Frequency;
                    int sleep = (int)Math.Max(1, 16 - sinceMs);
                    Thread.Sleep(sleep);
                }
                last = _clock.ElapsedTicks;
                if (Interlocked.CompareExchange(ref _tickQueued, 1, 0) == 0)
                {
                    try { _ui.Post(_tick, null); }
                    catch { Interlocked.Exchange(ref _tickQueued, 0); }
                }
            }
        }

        // ── A PC that can't keep up ──────────────────────────────────────────────────────────────────
        //
        // An animation's frame is a few milliseconds here (a tab switch's: two pictures blended, 2–6 ms). On a PC drawing
        // without a graphics card — a virtual machine, a remote session, an old laptop on its basic display driver — the
        // same frame can take tens of milliseconds, all of them on the UI thread: for the length of every animation, Tempo
        // answered clicks and keys late ("animation cause sometimes freeze for some users"). Three frames over the budget
        // within a few seconds and animations rest for a minute — every one plays as with Windows' animations off — and
        // the log says so once each time.

        /// <summary>A frame of the animations taking longer than this is a frame this PC can't keep up with (ms).</summary>
        private const double SlowFrameMs = 45;
        private static int _slowFrames;
        private static double _slowSinceMs = -1, _restUntilMs = -1;

        /// <summary>For Live debug: frames over the budget so far, the slowest frame, and whether animations rest now.</summary>
        public static long SlowFrames;
        public static double SlowestFrameMs;
        public static bool Resting => _restUntilMs > 0 && NowMs < _restUntilMs;

        /// <summary>For tests: the rest ended at once.</summary>
        internal static void EndRestForTest() { _restUntilMs = -1; _slowFrames = 0; _slowSinceMs = -1; }

        private static void NoteFrameCost(double ms)
        {
            if (ms > SlowestFrameMs) { SlowestFrameMs = ms; }
            if (ms < SlowFrameMs) { return; }
            SlowFrames++;
            double now = NowMs;
            if (_slowSinceMs < 0 || now - _slowSinceMs > 5000) { _slowSinceMs = now; _slowFrames = 0; }
            if (++_slowFrames < 3 || Resting) { return; }
            _restUntilMs = now + 60000;
            _slowFrames = 0;
            _slowSinceMs = -1;
            Utils.Logger.Info("[motion] animation frames took up to " + (int)ms + " ms on this PC (over " + (int)SlowFrameMs
                              + " ms, three times in a few seconds) — animations rest for a minute so Tempo keeps answering at once.");
            // What is still playing finishes now, in its final state.
            foreach (Animation a in _animations.ToArray()) { a.Stop(finish: true); }
        }

        /// <summary>The UI half: every running animation at its point in time, then the continuous ones.</summary>
        private static void Tick()
        {
            Interlocked.Exchange(ref _tickQueued, 0);
            Frames++;
            long now = _clock.ElapsedTicks;
            if (_animations.Count > 0)
            {
                long workStart = Stopwatch.GetTimestamp();
                TickAnimations(now);
                NoteFrameCost((Stopwatch.GetTimestamp() - workStart) * 1000.0 / Stopwatch.Frequency);
            }
            if (_continuous.Count > 0)
            {
                foreach (Action c in _continuous.ToArray())
                {
                    try { c(); }
                    catch (Exception ex) { Utils.Logger.Swallow("Motion.continuous", ex); Unsubscribe(c); }
                }
            }
            if (_animations.Count == 0 && _continuous.Count == 0) { _wanted = false; }
        }

        private static void TickAnimations(long now)
        {
            {
                foreach (Animation a in _animations.ToArray())
                {
                    if (a.Ended) { continue; }
                    if (!a.Clocked)
                    {
                        // Its first tick: t = 0 is what Start drew, so the clock starts now (see Animation.Clocked).
                        a.Clocked = true;
                        a.StartTicks = now;
                        continue;
                    }
                    double ms = (now - a.StartTicks) * 1000.0 / Stopwatch.Frequency;
                    double t = ms / a.DurationMs;
                    if (t >= 1.0)
                    {
                        a.Ended = true;
                        _animations.Remove(a);
                        Played++;
                        SafeFrame(a, 1.0);
                        SafeDone(a);
                    }
                    else
                    {
                        SafeFrame(a, t);
                    }
                }
            }
        }

        private static void SafeFrame(Animation a, double t)
        {
            try { a.Frame?.Invoke(Clamp01(t)); }
            catch (Exception ex)
            {
                // A frame that throws ends its animation, in its final state where it can.
                Utils.Logger.Swallow("Motion.frame", ex);
                if (!a.Ended)
                {
                    a.Ended = true;
                    _animations.Remove(a);
                    SafeDone(a);
                }
            }
        }

        private static void SafeDone(Animation a)
        {
            try { a.Done?.Invoke(); }
            catch (Exception ex) { Utils.Logger.Swallow("Motion.done", ex); }
        }

        // ── Win32 ────────────────────────────────────────────────────────────────────────────────────

        private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint param, ref bool value, uint winIni);

        /// <summary>DWM's own "wait for the next frame". Never call it straight: it can block for as long as DWM stalls —
        /// use <see cref="FramePacer.WaitFrame"/>, which waits for it at most a set time.</summary>
        [DllImport("dwmapi.dll")]
        internal static extern int DwmFlush();
    }

    /// <summary>
    /// "Wait for the screen's next frame" — with a limit (2026-11-14, the user: "fix animation cause sometimes freeze for
    /// some users … fix tempo loading that not pop up when some users click it on depend windows version or PC and Laptop").
    ///
    /// DwmFlush returns once DWM has composed the next frame, and on some PCs DWM stalls: a display going to sleep, some
    /// laptop graphics drivers, a remote session, a full-screen game holding the display. Called straight, a stalled
    /// DwmFlush froze whatever called it, for as long as the stall lasted:
    ///   • the animation clock — every animation stopped half-way;
    ///   • the window's way in — the window is uncloaked at its END, so Tempo was running with nothing on the screen;
    ///   • a page's scroll — that wait is on the UI thread: all of Tempo stopped answering;
    ///   • anti-AFK's see-through steps — a client left see-through for the stall.
    /// Now ONE thread of its own does the waiting on DWM. A caller waits for it at most the time it gives and then goes
    /// on at the clock's own pace (about 60 frames a second); when DWM answers again, so does the next wait.
    /// </summary>
    internal static class FramePacer
    {
        private static readonly object _gate = new object();
        private static readonly Stopwatch _sw = Stopwatch.StartNew();
        private static Thread _thread;
        private static long _flushes;
        private static bool _pending;
        private static double _flushStartedMs = -1;

        // For Live debug and the log.
        /// <summary>Waits asked for, and those that ran out before DWM answered.</summary>
        public static long Waits, Late;
        /// <summary>The longest single wait DWM took (ms).</summary>
        public static double LongestMs;
        /// <summary>When a DWM wait last took over a second (NowMs on <see cref="Motion"/>'s clock), or -1.</summary>
        public static double LastStallAtMs = -1;
        private static bool _stallLogged;

        /// <summary>
        /// Waits for the next composed frame, at most <paramref name="maxMs"/>. True when DWM showed one in time; false when
        /// it did not (stalled, or no DWM) and the time ran out — the caller goes on at its own pace.
        /// </summary>
        public static bool WaitFrame(int maxMs)
        {
            Ensure();
            lock (_gate)
            {
                Waits++;
                long target = _flushes + 1;
                if (!_pending)
                {
                    _pending = true;
                    Monitor.PulseAll(_gate);
                }
                double deadline = _sw.Elapsed.TotalMilliseconds + Math.Max(1, maxMs);
                while (_flushes < target)
                {
                    double left = deadline - _sw.Elapsed.TotalMilliseconds;
                    if (left <= 0)
                    {
                        Late++;
                        return false;
                    }
                    Monitor.Wait(_gate, (int)Math.Ceiling(left));
                }
                return true;
            }
        }

        /// <summary>How long the waiter has been inside DWM right now (ms; 0 when it isn't) — a stall going on.</summary>
        public static double StuckMs
        {
            get
            {
                double started = Volatile.Read(ref _flushStartedMs);
                return started < 0 ? 0 : _sw.Elapsed.TotalMilliseconds - started;
            }
        }

        private static void Ensure()
        {
            if (Volatile.Read(ref _thread) != null) { return; }
            lock (_gate)
            {
                if (_thread != null) { return; }
                var t = new Thread(Run) { IsBackground = true, Name = "Tempo frame waiter", Priority = ThreadPriority.AboveNormal };
                t.Start();
                _thread = t;
            }
        }

        private static void Run()
        {
            while (true)
            {
                lock (_gate)
                {
                    while (!_pending) { Monitor.Wait(_gate); }
                    Volatile.Write(ref _flushStartedMs, _sw.Elapsed.TotalMilliseconds);
                }
                double t0 = _sw.Elapsed.TotalMilliseconds;
                bool ok = false;
                try { ok = Motion.DwmFlush() == 0; } catch { }
                double took = _sw.Elapsed.TotalMilliseconds - t0;
                // No DWM to wait on (or it said no): about one 60 Hz frame, so a caller that keeps asking keeps a pace.
                if (!ok) { Thread.Sleep(15); }
                lock (_gate)
                {
                    _pending = false;
                    _flushes++;
                    Volatile.Write(ref _flushStartedMs, -1);
                    if (took > LongestMs) { LongestMs = took; }
                    if (took > 1000)
                    {
                        LastStallAtMs = Motion.NowMs;
                        if (!_stallLogged)
                        {
                            _stallLogged = true;
                            Utils.Logger.Info("[motion] the screen's compositor (DWM) stopped answering for " + (int)took
                                              + " ms — animations went on at their own pace meanwhile (said once).");
                        }
                    }
                    Monitor.PulseAll(_gate);
                }
            }
        }
    }
}
