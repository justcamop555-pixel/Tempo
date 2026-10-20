using System;
using System.Diagnostics;
using System.Threading;
using AutoClicker.Models;
using AutoClicker.Native;
using AutoClicker.Utils;

namespace AutoClicker.Engine
{
    /// <summary>
    /// Arguments describing an engine state change.
    /// </summary>
    public sealed class EngineStateChangedEventArgs : EventArgs
    {
        public EngineState OldState { get; }
        public EngineState NewState { get; }

        public EngineStateChangedEventArgs(EngineState oldState, EngineState newState)
        {
            OldState = oldState;
            NewState = newState;
        }
    }

    /// <summary>
    /// The clicking core. Owns a background worker thread that performs clicks
    /// according to a <see cref="ClickProfile"/>. All public mutating methods are
    /// safe to call from the UI thread; events are raised on the worker thread, so
    /// subscribers must marshal back to the UI as required.
    /// </summary>
    public sealed class ClickEngine : IDisposable
    {
        private readonly object _sync = new object();
        private readonly SessionStatistics _statistics;
        private readonly IntervalScheduler _scheduler = new IntervalScheduler();

        private Thread _worker;
        // The CURRENT run's stop signal. Each run gets its own (see Start): a worker that
        // outlives its run keeps the signal that was set for it, instead of sharing one that
        // the next Start resets underneath it.
        private ManualResetEventSlim _stopSignal = new ManualResetEventSlim(false);
        private volatile bool _running;
        private volatile bool _paused;
        private volatile bool _resyncScheduler;
        private EngineState _state = EngineState.Idle;

        /// <summary>
        /// Bumped by every Start. A worker belongs to exactly one generation and gives up the
        /// moment it is no longer the current one.
        ///
        /// Stop waits only 2 s for the worker. When that ran out — a long "hold each click" was
        /// uninterruptible — and Start was pressed again, the OLD worker found the engine running
        /// again and simply carried on: two workers clicking at once, the old one with the old
        /// profile. And its exit path cleared the running flag, stopping the NEW run under the
        /// user's feet.
        /// </summary>
        private int _runGeneration;

        /// <summary>What one run's worker needs of its own: which run it is, and its stop signal.</summary>
        private sealed class RunContext
        {
            public readonly int Generation;
            public readonly ManualResetEventSlim Stop;
            // The run's own randomness and aim, touched only by its worker (see RandomizationHelper, CursorAnchor).
            public readonly RandomizationHelper Random = new RandomizationHelper();
            public readonly CursorAnchor Anchor = new CursorAnchor();
            public RunContext(int generation, ManualResetEventSlim stop) { Generation = generation; Stop = stop; }
        }

        /// <summary>
        /// Real clicks per press for the actuation just made — 2 for a double click, 4 for a
        /// quadruple, 1 for a key. The rate cap and the reported rate are in CLICKS, like every
        /// other "CPS" in Tempo; the cap used to count presses, so a double click at "max 200"
        /// sent 400 clicks a second.
        /// </summary>
        private volatile int _lastPressClicks = 1;

        // ── Anti-freeze protection ────────────────────────────────────────────
        // What it watches is SystemHealthMonitor: whether the app being clicked still answers, the whole
        // machine's CPU and memory, a background target's full message queue. What it does is here: slow the
        // rate down, or hold the clicks while the app is not answering at all.
        //
        // It used to watch Tempo's OWN CPU use against "80% of the machine" (CpuThresholdPercent). One click
        // thread is one core, about 6% of a 16-thread PC, so that could never happen: the panel read
        // "✓ Protected — CPU 0%" while a game drowned in clicks, and the only protection was the fixed cap.
        private readonly SystemHealthMonitor _health = SystemHealthMonitor.Shared;
        private readonly Stopwatch _throttleClock = new Stopwatch();
        public bool AntiFreezeEnabled { get; set; } = true;
        public int MaxClicksPerSecond { get; set; } = 200;
        /// <summary>From old settings files; Anti-Freeze no longer reads it (see above).</summary>
        public int CpuThresholdPercent { get; set; } = 80;
        private double _throttleFactor = 1.0;
        private volatile float _effectiveCps;
        private volatile FreezeVerdict _verdict = FreezeVerdict.None;
        private volatile bool _holding;
        private long _lastSampleSeen;
        private int _cpuFullStreak, _slowStreak;

        // The thresholds. A game drives the CPU to 100% legitimately, so a full CPU or memory counts only
        // when the app being clicked is ALSO slow to answer (or there is none to ask).
        internal const double SlowAnswerMs = 250, PromptAnswerMs = 100;
        internal const double CpuFullPercent = 97, MemoryFullPercent = 95;
        internal const double MaxThrottle = 16;

        /// <summary>The whole machine's CPU use (0–100%) at the latest reading; 0 before there is one.</summary>
        public double MeasuredCpuPercent
        {
            get { double c = _health.Latest.CpuPercent; return double.IsNaN(c) ? 0 : c; }
        }

        /// <summary>The latest reading of the machine and the app being clicked.</summary>
        public HealthSnapshot Health => _health.Latest;

        /// <summary>
        /// The actual click rate the engine is currently issuing, in real CLICKS a second (a
        /// double click counts two) — the same count as the live CPS and the statistics.
        /// </summary>
        public double EffectiveClicksPerSecond => _effectiveCps;

        /// <summary>True while anti-freeze is actively slowing the configured rate, or holding the clicks.</summary>
        public bool IsThrottling => _throttleFactor > 1.05 || _holding;

        /// <summary>True while the clicks are held because the app being clicked is not answering.</summary>
        public bool IsHolding => _holding;

        /// <summary>Why Anti-Freeze is (or was last) slowing down.</summary>
        public AntiFreezeReason FreezeReason => _verdict.Reason;

        /// <summary>
        /// Why, with the number behind it from the reading that decided it — the answer time in ms (TargetSlow),
        /// the CPU % (CpuFull), the memory % (MemoryFull), NaN for the rest. One read, so the two always agree;
        /// the latest reading may already be a newer one.
        /// </summary>
        public (AntiFreezeReason Reason, double Evidence) Freeze
        {
            get { FreezeVerdict v = _verdict; return (v.Reason, v.Evidence); }
        }

        private sealed class FreezeVerdict
        {
            public static readonly FreezeVerdict None = new FreezeVerdict(AntiFreezeReason.None, double.NaN);
            public readonly AntiFreezeReason Reason;
            public readonly double Evidence;
            public FreezeVerdict(AntiFreezeReason reason, double evidence) { Reason = reason; Evidence = evidence; }
        }

        // Where the last click went, for SystemHealthMonitor to find the window being clicked.
        private volatile int _lastX, _lastY;
        private volatile bool _lastPointValid;

        // ── Multi-point traversal state ───────────────────────────────────────
        private volatile int _currentPointIndex = -1;
        private int _mpDirection = 1;
        private int _mpRepeatLeft = 0;

        /// <summary>Index (within the profile's Points list) of the point currently
        /// being clicked, or -1 when not in multi-point mode. For the live UI.</summary>
        public int CurrentPointIndex => _currentPointIndex;
        private ClickProfile _activeProfile;
        private int _cursorStartX;
        private int _cursorStartY;
        private bool _cursorStartValid;
        private bool _disposed;

        public event EventHandler<EngineStateChangedEventArgs> StateChanged;
        public event EventHandler ClickPerformed;
        public event EventHandler RunCompleted;

        public ClickEngine(SessionStatistics statistics)
        {
            _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
            _health.TargetWindow = CurrentTargetWindow;
        }

        /// <summary>
        /// The window being clicked, asked on the health monitor's thread: the window under the last click
        /// point, under the cursor for "click where the cursor is", the focused window for a keyboard key.
        /// </summary>
        private IntPtr CurrentTargetWindow()
        {
            ClickProfile p = _activeProfile;
            if (!_running || p == null) { return IntPtr.Zero; }
            if (p.Target == ClickTarget.Key) { return NativeMethods.GetForegroundWindow(); }
            int x, y;
            if (_lastPointValid) { x = _lastX; y = _lastY; }
            // Before the first click: the fixed point — not the cursor, which is wherever the user's mouse is
            // (a browser on the other screen was watched for the first half-second of a run).
            else if (p.PositionMode == PositionMode.FixedPosition) { x = p.FixedX; y = p.FixedY; }
            else if (!ScreenGeometry.TryGetCursorPosition(out x, out y)) { return IntPtr.Zero; }
            // The window the click goes to — which, with Tempo's own window over the point and "ignore Tempo's own
            // window" on, is the one under it (InputSimulator.ResolveWindowAt): watching Tempo instead read
            // "✓ Full speed — Tempo answers in 1 ms" about a game that might be frozen.
            return InputSimulator.TargetWindowAt(x, y);
        }

        public EngineState State
        {
            get
            {
                lock (_sync)
                {
                    return _state;
                }
            }
        }

        public bool IsRunning => _running;

        /// <summary>
        /// True while the run clicks in the background ("Click without moving my cursor" with a target
        /// point): its clicks are posted to a window, the real mouse and keyboard stay the user's, so
        /// Windows' own shortcuts must keep working for them (MainForm.UpdateShortcutGuard). Key mode
        /// and "where the cursor is" always use the real input — see <see cref="Actuate"/>.
        /// </summary>
        public bool ClicksInBackground
        {
            get
            {
                ClickProfile p = _activeProfile;
                return _running && p != null && p.Target != ClickTarget.Key && p.BackgroundClick &&
                       (p.PositionMode == PositionMode.FixedPosition || p.PositionMode == PositionMode.MultiPoint);
            }
        }

        /// <summary>
        /// Starts clicking with a snapshot of the supplied profile. Returns a
        /// validation error string, or null on success.
        /// </summary>
        public string Start(ClickProfile profile)
        {
            ThrowIfDisposed();

            if (profile == null)
            {
                return "No profile supplied.";
            }

            string error = profile.Validate();
            if (error != null)
            {
                return error;
            }

            lock (_sync)
            {
                if (_running)
                {
                    return null; // Already running; treat as a no-op.
                }

                // Work on a private copy so the UI can keep editing the original.
                _activeProfile = profile.Clone();

                // Remember where the cursor was, so we can optionally restore it
                // when the run stops (useful for Fixed / Multi-Point modes).
                _cursorStartValid = ScreenGeometry.TryGetCursorPosition(out _cursorStartX, out _cursorStartY);

                // A fresh signal for this run rather than resetting the shared one: a worker from
                // an earlier run may not have seen its signal yet, and a Reset would un-stop it.
                _stopSignal = new ManualResetEventSlim(false);
                var run = new RunContext(Interlocked.Increment(ref _runGeneration), _stopSignal);
                _lastPressClicks = 1;
                _running = true;
                _paused = false;
                LastRunCompletedNaturally = true;   // Stop() flips this off.
                System.Threading.Interlocked.Exchange(ref _runPerformed, 0);
                _runClock.Restart();
                _resyncScheduler = false;
                _throttleFactor = 1.0;
                _effectiveCps = 0;
                _verdict = FreezeVerdict.None;
                _holding = false;
                _cpuFullStreak = _slowStreak = 0;
                _lastSampleSeen = 0;
                _lastPointValid = false;
                _throttleClock.Restart();
                _currentPointIndex = -1;
                _mpDirection = 1;
                _mpRepeatLeft = 0;

                // Watched for the length of THIS run, under its own name: a stopped run's worker that ends
                // late releases its own hold, never the new run's.
                _health.Acquire(HealthOwner(run));

                _worker = new Thread(() => WorkerLoop(run))
                {
                    IsBackground = true,
                    Name = "AutoClicker.Worker",
                    // Above the game's threads when it wakes, like Tempo's other timing threads (the key hook,
                    // camera movement): it sleeps between clicks, so this costs the game nothing, and under a
                    // CPU-bound game a Normal click thread waited behind it and the interval wobbled.
                    Priority = ThreadPriority.AboveNormal
                };

                _statistics.BeginRun();
                SetState(EngineState.Running);
                _worker.Start();
            }

            Logger.Info($"[Clicker] engine started with profile '{profile.Name}'.");
            return null;
        }

        /// <summary>Requests the engine stop and waits briefly for the worker.</summary>
        // Active-time clock for the current run. Unlike a plain Stopwatch left
        // running, this one is stopped while paused — so "for N seconds" means
        // N seconds of actual clicking, and the UI countdown freezes with it.
        private readonly Stopwatch _runClock = new Stopwatch();
        private long _runPerformed;

        /// <summary>Un-paused milliseconds elapsed in the current (or last) run.</summary>
        public long RunActiveMs => _runClock.ElapsedMilliseconds;

        /// <summary>Clicks performed so far in the current (or last) run.</summary>
        public long RunClicks => System.Threading.Interlocked.Read(ref _runPerformed);

        /// <summary>
        /// True when the most recent run ended on its own (fixed count or duration
        /// reached) rather than via <see cref="Stop"/>. Lets the UI distinguish
        /// "finished" from "user stopped it" — e.g. to play a completion chime only
        /// for the former.
        /// </summary>
        public bool LastRunCompletedNaturally { get; private set; }

        public void Stop()
        {
            Thread workerToJoin = null;

            lock (_sync)
            {
                if (!_running)
                {
                    return;
                }

                LastRunCompletedNaturally = false;
                _running = false;
                _paused = false;
                _stopSignal.Set();
                workerToJoin = _worker;
            }

            // Join outside the lock to avoid deadlock if the worker raises events.
            if (workerToJoin != null && workerToJoin.IsAlive)
            {
                workerToJoin.Join(TimeSpan.FromSeconds(2));
            }

            lock (_sync)
            {
                _statistics.EndRun();
                SetState(EngineState.Idle);
                _worker = null;
            }

            Logger.Info("[Clicker] engine stopped.");
        }

        /// <summary>Toggles between running and idle.</summary>
        public string Toggle(ClickProfile profile)
        {
            if (_running)
            {
                Stop();
                return null;
            }

            return Start(profile);
        }

        /// <summary>True while a run is paused.</summary>
        public bool IsPaused => _paused;

        /// <summary>
        /// Pauses an active run. The worker stops actuating but the thread stays
        /// alive, so a later <see cref="Resume"/> continues without restarting.
        /// </summary>
        public void Pause()
        {
            lock (_sync)
            {
                if (!_running || _paused)
                {
                    return;
                }

                _paused = true;
                // Stop the statistics clock too. _runClock (below) has always stopped
                // here; the statistics kept their own wall-clock and did not, so a pause
                // was filed as time spent clicking. See SessionStatistics._pausedTotal.
                _statistics.PauseRun();
                SetState(EngineState.Paused);
            }

            Logger.Info("[Clicker] engine paused.");
        }

        /// <summary>Resumes a paused run, realigning the interval schedule.</summary>
        public void Resume()
        {
            lock (_sync)
            {
                if (!_running || !_paused)
                {
                    return;
                }

                _paused = false;
                _statistics.ResumeRun();
                _resyncScheduler = true; // worker resets the schedule baseline
                SetState(EngineState.Running);
            }

            Logger.Info("[Clicker] engine resumed.");
        }

        /// <summary>Pauses if running, resumes if paused. No-op when idle.</summary>
        public void TogglePause()
        {
            if (!_running)
            {
                return;
            }

            if (_paused)
            {
                Resume();
            }
            else
            {
                Pause();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Worker
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>True while <paramref name="run"/> is the engine's current, running run.</summary>
        private bool IsCurrent(RunContext run)
        {
            return _running && Volatile.Read(ref _runGeneration) == run.Generation;
        }

        private void WorkerLoop(RunContext run)
        {
            ClickProfile p = null;
            try
            {
                lock (_sync)
                {
                    p = _activeProfile;
                }

                if (p == null)
                {
                    return;
                }

                switch (p.Mode)
                {
                    case ClickMode.Burst:
                        RunBurstLoop(p, run);
                        break;
                    default:
                        RunIntervalLoop(p, run);
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("[Clicker] unhandled exception in worker loop.", ex);
            }
            finally
            {
                if (Volatile.Read(ref _runGeneration) == run.Generation) { _holding = false; }
                _health.Release(HealthOwner(run));
                if (!FinishRun(p, run))
                {
                    // A newer run started after this one was stopped. The running flag, the
                    // state, the statistics, the cursor and "run completed" all belong to THAT
                    // run now, so this worker left them alone.
                    Logger.Warn("[Clicker] a stopped run's worker ended after a new run had started; it left the new run untouched.");
                }
            }
        }

        /// <summary>
        /// The end of <paramref name="run"/>: idle state, statistics, the cursor, and the event.
        /// Returns false, having touched nothing, when a newer run has already started.
        /// </summary>
        private bool FinishRun(ClickProfile p, RunContext run)
        {
            // Ensure the state flag returns to Idle even when the run ended on
            // its own (e.g. a fixed repeat count completed) rather than via
            // Stop(). Without this, a subsequent Start() would not raise a
            // state-change because the stored state would still read Running.
            //
            // Checked and cleared under the lock Start takes to begin a run, so a Start can't
            // land between "is this still my run?" and clearing the running flag — which would
            // stop the brand-new run as it began.
            lock (_sync)
            {
                if (Volatile.Read(ref _runGeneration) != run.Generation)
                {
                    return false;
                }
                _running = false;
                _statistics.EndRun();
                SetState(EngineState.Idle);
            }

            // Optionally return the cursor to where it was before the run, for
            // Fixed / Multi-Point modes that moved it away. Skipped in background
            // ("don't move my mouse") mode — the cursor was never moved, so
            // restoring it would instead YANK the user's real mouse back to where
            // it was when they started, which is exactly what the mode avoids.
            try
            {
                // Nor for a keyboard key: a key run never moves the cursor either, so the "restore"
                // only dragged the mouse back to where it had been when the run began.
                if (_cursorStartValid && p != null && p.RestoreCursorOnStop &&
                    !p.BackgroundClick && p.Target != ClickTarget.Key &&
                    (p.PositionMode == PositionMode.FixedPosition ||
                     p.PositionMode == PositionMode.MultiPoint))
                {
                    InputSimulator.MoveTo(_cursorStartX, _cursorStartY);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[Clicker] could not restore cursor position: " + ex.Message);
            }

            // "Click where the cursor is" with its position randomized leaves the cursor up to the jitter
            // away from where the user put it: back there it goes — unless the user has taken the mouse
            // since (CursorAnchor.TryHome).
            try
            {
                if (p != null && p.Target != ClickTarget.Key && p.PositionMode == PositionMode.CurrentPosition &&
                    p.RandomizePosition && ScreenGeometry.TryGetCursorPosition(out int cx, out int cy) &&
                    run.Anchor.TryHome(cx, cy, out int hx, out int hy))
                {
                    InputSimulator.MoveTo(hx, hy);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[Clicker] could not put the cursor back where it was aimed: " + ex.Message);
            }

            RunCompleted?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private void RunIntervalLoop(ClickProfile p, RunContext run)
        {
            long performed = 0;
            long max = p.RepeatMode == RepeatMode.FixedCount ? p.RepeatCount : long.MaxValue;
            long durationMs = p.RepeatMode == RepeatMode.ForDuration
                ? (long)p.RepeatDurationSeconds * 1000L : 0;
            // run clock: engine-level _runClock, restarted in Start()

            _scheduler.Start();

            while (IsCurrent(run) && performed < max)
            {
                // Stop once the configured run duration has elapsed.
                if (durationMs > 0 && _runClock.ElapsedMilliseconds >= durationMs)
                {
                    break;
                }

                // Block here while paused; realign the schedule on resume.
                if (WaitWhilePaused(run))
                {
                    break;
                }
                // And while the app being clicked is not answering: clicks sent now only queue up behind
                // whatever it is stuck on, and pour in at once when it wakes.
                if (HoldWhileTargetNotAnswering(run))
                {
                    break;
                }
                if (_resyncScheduler)
                {
                    _scheduler.Start();
                    _resyncScheduler = false;
                }

                long interval = PerformOneActuation(p, run);
                performed++;
                System.Threading.Interlocked.Increment(ref _runPerformed);

                if (!IsCurrent(run) || performed >= max)
                {
                    break;
                }

                // Anti-freeze: clamp to the safety cap and back off under CPU load.
                // Above 1000 CPS the manual slider supplies a sub-millisecond
                // interval that a whole-millisecond value can't represent.
                double desired = p.ManualPreciseIntervalMs > 0.0
                    ? p.ManualPreciseIntervalMs
                    : interval;
                // Randomized AFTER that choice: the precise interval used to replace the varied one, so
                // "randomize interval" did nothing at all above 1000 CPS.
                desired = Vary(p, run, desired);
                double effInterval = ApplyAntiFreeze(desired);

                // Wait the remaining time until the next scheduled slot rather than
                // a fixed interval, so click execution time does not slow the rate.
                double wait = _scheduler.NextWait(effInterval);
                if (WaitInterruptible(wait, run))
                {
                    break; // stop requested
                }
            }
        }

        private void RunBurstLoop(ClickProfile p, RunContext run)
        {
            long performed = 0;
            long max = p.RepeatMode == RepeatMode.FixedCount ? p.RepeatCount : long.MaxValue;
            long durationMs = p.RepeatMode == RepeatMode.ForDuration
                ? (long)p.RepeatDurationSeconds * 1000L : 0;
            // run clock: engine-level _runClock, restarted in Start()

            while (IsCurrent(run) && performed < max)
            {
                if (durationMs > 0 && _runClock.ElapsedMilliseconds >= durationMs)
                {
                    break;
                }

                if (WaitWhilePaused(run))
                {
                    break;
                }

                int burst = p.BurstSize < 1 ? 1 : p.BurstSize;

                // Each burst is scheduled internally so the in-burst rate is accurate.
                _scheduler.Start();
                _resyncScheduler = false;

                for (int i = 0; i < burst && IsCurrent(run) && performed < max
                     && (durationMs == 0 || _runClock.ElapsedMilliseconds < durationMs); i++)
                {
                    if (HoldWhileTargetNotAnswering(run))
                    {
                        return;
                    }
                    if (_resyncScheduler)
                    {
                        // Held mid-burst: the rest of the burst starts afresh rather than catching up at once.
                        _scheduler.Start();
                        _resyncScheduler = false;
                    }

                    long interval = PerformOneActuation(p, run);
                    performed++;
                    System.Threading.Interlocked.Increment(ref _runPerformed);

                    if (i < burst - 1 && IsCurrent(run) && performed < max)
                    {
                        // Anti-freeze applies to bursts EXACTLY as it does to the
                        // interval loop. It used to be missing here entirely, which
                        // meant Burst mode silently ignored the "Max CPS" safety cap
                        // AND the CPU back-off: measured live, a 10 CPS cap produced
                        // 256 CPS in Burst (peak 294) while the panel cheerfully
                        // reported "Protected". The whole point of anti-freeze is to
                        // stop a runaway rate locking up the machine, and Burst — the
                        // fastest mode there is — was the one place it did nothing.
                        //
                        // Honouring ManualPreciseIntervalMs here too: above 1000 CPS
                        // the slider hands down a sub-millisecond interval that the
                        // whole-millisecond value cannot express, and burst was
                        // rounding it away.
                        double desired = p.ManualPreciseIntervalMs > 0.0
                            ? p.ManualPreciseIntervalMs
                            : interval;
                        desired = Vary(p, run, desired);
                        double effInterval = ApplyAntiFreeze(desired);

                        double wait = _scheduler.NextWait(effInterval);
                        if (WaitInterruptible(wait, run))
                        {
                            return;
                        }
                    }
                }

                if (!IsCurrent(run) || performed >= max)
                {
                    break;
                }

                // Pause between bursts (precise, but does not need drift tracking).
                // When interval randomization is on, jitter this pause too so the
                // gap between bursts isn't a fixed, detectable rhythm — by the same SHARE
                // as the click gap (±20 ms on a 100 ms interval is ±200 ms on a 1 s pause),
                // at most half the pause. It moved by the click's ±ms, so a 1 s pause
                // between bursts of 50 ± 10 ms clicks came round every 990–1010 ms: the
                // most regular beat in the whole run.
                double burstPause = p.BurstPauseMilliseconds;
                if (p.RandomizeInterval && burstPause > 0 && p.IntervalJitterMilliseconds > 0)
                {
                    double gap = Math.Max(1.0, p.GetBaseIntervalMilliseconds());
                    double share = Math.Min(0.5, p.IntervalJitterMilliseconds / gap);
                    burstPause = run.Random.JitterInterval(burstPause, burstPause * share, 0.5);
                }

                // The gap BETWEEN bursts is a click gap too, so it has to respect the
                // rate cap as well. Otherwise a burst size of 1 with a 0 ms pause would
                // never once hit the in-burst wait above and would sail straight past
                // anti-freeze again — the same hole, just one level down.
                if (WaitInterruptible(ApplyAntiFreeze(burstPause), run))
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Performs a single click actuation and returns the number of
        /// milliseconds the caller should wait before the next one.
        ///
        /// Whenever a move is required, the move and the click are submitted as a
        /// single batched <c>SendInput</c> call via
        /// <see cref="InputSimulator.MoveAndClick"/>. That keeps the actuation
        /// atomic from the OS's point of view and removes the inter-event latency
        /// that would otherwise cap the achievable click rate.
        /// </summary>
        /// <summary>
        /// Performs one click. When <paramref name="holdMs"/> is 0 this uses the exact
        /// same batched <see cref="InputSimulator"/> calls as before (no behaviour
        /// change). When it's positive, the button is held down for that long.
        /// </summary>
        private static void Actuate(bool move, int x, int y, MouseButtonType button, ClickStyle style, int holdMs,
                                    ManualResetEventSlim stop, bool background = false)
        {
            // Background mode: post the click to the window at (x, y) WITHOUT moving
            // the real cursor (the "second mouse"). Only meaningful when a target point
            // exists (move == true); "click where the cursor is" can't be background.
            if (background && move)
            {
                if (holdMs > 0)
                {
                    InputSimulator.BackgroundClickHeld(x, y, button, style, holdMs, stop);
                }
                else
                {
                    InputSimulator.BackgroundClick(x, y, button, style);
                }
                return;
            }

            if (holdMs > 0)
            {
                if (move)
                {
                    InputSimulator.MoveTo(x, y);
                }
                // The hold waits on the run's stop signal, so Stop cuts it short — and the
                // button is released either way.
                InputSimulator.ClickStyledHeld(button, style, holdMs, stop);
            }
            else if (move)
            {
                InputSimulator.MoveAndClick(x, y, button, style);
            }
            else
            {
                InputSimulator.ClickStyled(button, style);
            }
        }

        private long PerformOneActuation(ClickProfile p, RunContext run)
        {
            MouseButtonType button = p.Button;
            ClickStyle style = p.Style;
            MouseButtonType actuatedButton = button;
            long interval = p.GetBaseIntervalMilliseconds();
            int hold = p.ClickHoldMilliseconds;

            // When anti-pattern randomization is on, vary the per-click hold a little
            // too, so held clicks aren't all exactly the same length — by up to half the
            // hold, never below 1 ms, the average staying the hold set.
            if (p.RandomizeInterval && hold > 1 && p.IntervalJitterMilliseconds > 0)
            {
                double holdJitter = Math.Min(p.IntervalJitterMilliseconds, hold / 2.0);
                hold = Math.Max(1, (int)Math.Round(run.Random.JitterInterval(hold, holdJitter, 1.0)));
            }

            // Keyboard-key mode: press a key instead of a mouse button. Reuses the same
            // interval / repeat / randomize machinery; mouse position and click-style
            // (double/triple) don't apply — one press is one actuation. When a hold time
            // is set, the key is held down for it (like the mouse hold path).
            //
            // This must catch key mode WHOLESALE (not only when a key is set): if it fell
            // through to the mouse path when no key had been chosen, key mode would
            // silently left-click instead. With no key set it simply does nothing.
            if (p.Target == ClickTarget.Key)
            {
                _lastPressClicks = 1;
                int vk = p.KeyVirtualKey;
                if (vk != 0)
                {
                    if (hold > 0)
                    {
                        InputSimulator.KeyDown(vk);
                        // On the run's stop signal, like the mouse holds: a stop mid-hold lets
                        // the key go at once instead of up to 5 s later. And on the high-resolution
                        // timer, like them: the event's own Wait follows the timer resolution, which
                        // Windows 11 stops honouring while Tempo's window is hidden.
                        PreciseWait.Wait(hold, run.Stop);
                        InputSimulator.KeyUp(vk);
                    }
                    else
                    {
                        InputSimulator.KeyPress(vk);
                    }

                    _statistics.RecordClicks(1, actuatedButton);
                    ClickPerformed?.Invoke(this, EventArgs.Empty);
                }

                // The run loop randomizes it (Vary), once the interval to vary is known.
                return interval;
            }

            if (p.PositionMode == PositionMode.MultiPoint)
            {
                ClickPoint point = AdvanceMultiPoint(p, run);
                if (point != null)
                {
                    int x = point.X;
                    int y = point.Y;
                    if (p.RandomizePosition)
                    {
                        run.Random.JitterWithin(ref x, ref y, p.PositionJitterPixels, RootWindowAt);
                    }

                    NoteClickPoint(x, y);
                    Actuate(true, x, y, point.Button, point.Style, hold, run.Stop, p.BackgroundClick);
                    style = point.Style;
                    actuatedButton = point.Button;

                    if (point.DwellMilliseconds > 0)
                    {
                        interval = point.DwellMilliseconds;
                    }
                }
            }
            else if (p.PositionMode == PositionMode.FixedPosition)
            {
                int x = p.FixedX;
                int y = p.FixedY;
                if (p.RandomizePosition)
                {
                    run.Random.JitterWithin(ref x, ref y, p.PositionJitterPixels, RootWindowAt);
                }

                NoteClickPoint(x, y);
                Actuate(true, x, y, button, style, hold, run.Stop, p.BackgroundClick);
            }
            else if (p.RandomizePosition)
            {
                // Current position but with jitter applied around it: around the spot the USER
                // put the cursor (CursorAnchor), not where the last click left it — that walked the
                // clicks off the target — then a move + click.
                if (ScreenGeometry.TryGetCursorPosition(out int cx, out int cy))
                {
                    run.Anchor.Aim(cx, cy, out int ax, out int ay);
                    run.Random.JitterWithin(ref ax, ref ay, p.PositionJitterPixels, RootWindowAt);
                    run.Anchor.Clicked(ax, ay);
                    NoteClickPoint(ax, ay);
                    Actuate(true, ax, ay, button, style, hold, run.Stop);
                }
                else
                {
                    _lastPointValid = false;
                    Actuate(false, 0, 0, button, style, hold, run.Stop);
                }
            }
            else
            {
                // Pure "click where the cursor is" — no move event needed (the health monitor reads the cursor).
                _lastPointValid = false;
                Actuate(false, 0, 0, button, style, hold, run.Stop);
            }

            // Count every real mouse click, not just the actuation. A double-click
            // style produces two clicks, a triple three, and so on.
            int clicks = StyleToClicks(style);
            _lastPressClicks = clicks;
            _statistics.RecordClicks(clicks, actuatedButton);
            ClickPerformed?.Invoke(this, EventArgs.Empty);

            // The run loop randomizes it (Vary), once the interval to vary is known.
            return interval;
        }

        /// <summary>
        /// <paramref name="interval"/> with the run's interval randomization: ± the jitter, symmetric so the average
        /// is the speed set, and never below what the engine would wait anyway — half a millisecond, or the
        /// Anti-Freeze cap's gap while it is on (RandomizationHelper.EffectiveJitter). Below that floor the
        /// clamp turned the lower half of the spread into one fixed gap.
        /// </summary>
        private double Vary(ClickProfile p, RunContext run, double interval)
        {
            if (!p.RandomizeInterval || p.IntervalJitterMilliseconds <= 0) { return interval; }
            return run.Random.JitterInterval(interval, p.IntervalJitterMilliseconds,
                JitterFloor(AntiFreezeEnabled, MaxClicksPerSecond, _lastPressClicks));
        }

        /// <summary>
        /// The least a randomized interval may come to: the engine's own floor (0.5 ms), or the gap the
        /// Anti-Freeze cap allows a press of <paramref name="clicksPerPress"/> clicks while it is on. Shared with the
        /// Clicker tab, so the range it shows is the range the engine uses.
        /// </summary>
        public static double JitterFloor(bool antiFreeze, int maxClicksPerSecond, int clicksPerPress)
        {
            double floor = 0.5;
            if (antiFreeze)
            {
                int cap = maxClicksPerSecond < 1 ? 1 : maxClicksPerSecond;
                floor = Math.Max(floor, 1000.0 * Math.Max(1, clicksPerPress) / cap);
            }
            return floor;
        }

        /// <summary>
        /// The top-level window at a screen point — the window a randomized click must stay on
        /// (RandomizationHelper.JitterWithin). A field so a test can stand in for the screen.
        /// </summary>
        internal static Func<int, int, IntPtr> RootWindowAt = (x, y) =>
        {
            IntPtr hwnd = NativeMethods.WindowFromPoint(new NativeMethods.POINT(x, y));
            return hwnd == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        };

        /// <summary>
        /// Changes the running run's anti-pattern settings — interval and position randomization, the hold — the
        /// way <see cref="UpdateInterval"/> changes its interval. Returns false when nothing is running.
        ///
        /// Only the interval reached a running clicker: ticking "Randomize", pressing Humanize or changing the hold
        /// mid-run did nothing until the next start, with the boxes saying otherwise.
        /// </summary>
        public bool UpdateHumanizing(bool randomizeInterval, int intervalJitterMs, bool randomizePosition, int positionJitterPx,
                                     int holdMs)
        {
            lock (_sync)
            {
                if (!_running || _activeProfile == null)
                {
                    return false;
                }
                _activeProfile.RandomizeInterval = randomizeInterval;
                _activeProfile.IntervalJitterMilliseconds = Math.Max(0, intervalJitterMs);
                _activeProfile.RandomizePosition = randomizePosition;
                _activeProfile.PositionJitterPixels = Math.Max(0, positionJitterPx);
                _activeProfile.ClickHoldMilliseconds = Math.Max(0, holdMs);
            }
            return true;
        }

        /// <summary>
        /// Chooses the next point to click, honouring the profile's traversal order
        /// (sequential, reverse, random, ping-pong) and each point's repeat count.
        /// Publishes the chosen point's list index for the live UI highlight.
        /// </summary>
        // Reused across actuations. This list was allocated FRESH on every single
        // click, so multi-point at a few hundred CPS was handing the GC hundreds of
        // short-lived lists a second — pure garbage on the hottest path in the engine,
        // and GC pauses are exactly what a timing-sensitive clicker cannot afford.
        // Only the worker thread touches it.
        private readonly System.Collections.Generic.List<int> _enabledPoints =
            new System.Collections.Generic.List<int>(16);

        private ClickPoint AdvanceMultiPoint(ClickProfile p, RunContext run)
        {
            // Build the list of enabled point indices (into the reused buffer).
            var enabled = _enabledPoints;
            enabled.Clear();
            for (int i = 0; i < p.Points.Count; i++)
            {
                if (p.Points[i].Enabled)
                {
                    enabled.Add(i);
                }
            }

            if (enabled.Count == 0)
            {
                _currentPointIndex = -1;
                return null;
            }

            // If we're still repeating the current point, stay on it.
            if (_mpRepeatLeft > 0 && _currentPointIndex >= 0 &&
                _currentPointIndex < p.Points.Count && p.Points[_currentPointIndex].Enabled)
            {
                _mpRepeatLeft--;
                return p.Points[_currentPointIndex];
            }

            // Find our position within the enabled list (or -1 if not present).
            int pos = enabled.IndexOf(_currentPointIndex);

            int nextPos;
            switch (p.PointOrder)
            {
                case MultiPointOrder.Reverse:
                    nextPos = pos <= 0 ? enabled.Count - 1 : pos - 1;
                    break;

                case MultiPointOrder.Random:
                    if (enabled.Count == 1)
                    {
                        nextPos = 0;
                    }
                    else
                    {
                        // Pick a different point than the current one.
                        do { nextPos = run.Random.Next(0, enabled.Count); }
                        while (nextPos == pos);
                    }
                    break;

                case MultiPointOrder.PingPong:
                    if (enabled.Count == 1)
                    {
                        nextPos = 0;
                    }
                    else if (pos < 0)
                    {
                        // First visit of the run (or the current point was disabled): START at the
                        // first point. This used to set pos = 0 and then step +1, so ping-pong began
                        // on the SECOND point and point 1 was never clicked on the opening pass —
                        // 2, 3, 2, 1 instead of the documented 1, 2, 3, 2, 1. Sequential and Reverse
                        // both start on their first point; this is the same promise.
                        _mpDirection = 1;
                        nextPos = 0;
                    }
                    else
                    {
                        nextPos = pos + _mpDirection;
                        if (nextPos >= enabled.Count)
                        {
                            _mpDirection = -1;
                            nextPos = enabled.Count - 2;
                        }
                        else if (nextPos < 0)
                        {
                            _mpDirection = 1;
                            nextPos = 1;
                        }
                    }
                    break;

                default: // Sequential
                    nextPos = pos < 0 ? 0 : (pos + 1) % enabled.Count;
                    break;
            }

            _currentPointIndex = enabled[nextPos];
            ClickPoint chosen = p.Points[_currentPointIndex];

            // Set up repeat for the newly-selected point (this actuation is #1).
            int repeat = chosen.Repeat < 1 ? 1 : chosen.Repeat;
            _mpRepeatLeft = repeat - 1;

            return chosen;
        }

        /// <summary>
        /// Maps a click style to the number of real mouse clicks it produces.
        /// </summary>
        private static int StyleToClicks(ClickStyle style)
        {
            switch (style)
            {
                case ClickStyle.Single: return 1;
                case ClickStyle.Double: return 2;
                case ClickStyle.Triple: return 3;
                case ClickStyle.Quadruple: return 4;
                default: return 1;
            }
        }

        /// <summary>
        /// Updates the active profile's interval components while a run is in
        /// progress, without stopping the engine. Returns false when nothing is
        /// running. The next loop iteration picks up the new value, and the
        /// drift-compensating scheduler is resynced to align cleanly.
        /// </summary>
        public bool UpdateInterval(int hours, int minutes, int seconds, int millis, double preciseMs = 0.0)
        {
            lock (_sync)
            {
                if (!_running || _activeProfile == null)
                {
                    return false;
                }

                _activeProfile.IntervalHours = hours;
                _activeProfile.IntervalMinutes = minutes;
                _activeProfile.IntervalSeconds = seconds;
                _activeProfile.IntervalMilliseconds = millis;
                _activeProfile.ManualPreciseIntervalMs = preciseMs;
                _resyncScheduler = true;
            }

            return true;
        }

        /// <summary>
        /// Applies anti-freeze protection to a desired interval: enforces the hard clicks-per-second cap and
        /// multiplies the interval by an adaptive factor that grows while the app being clicked falls behind or
        /// the machine runs out of room (see <see cref="UpdateThrottle"/>). Publishes the effective click rate
        /// for the UI. Returns the interval to actually wait.
        /// </summary>
        private double ApplyAntiFreeze(double interval)
        {
            // Absolute floor enforced REGARDLESS of the anti-freeze setting. Without
            // this, disabling anti-freeze and asking for an enormous rate makes the
            // worker thread spin with almost no gap, pegging a CPU core and freezing
            // the UI / system. 0.5 ms == 2000 presses a second, which is already extreme.
            double baseInterval = Math.Max(0.5, interval);

            // The rate is counted in CLICKS: a double click is two. Both the cap and the
            // reported rate follow that, the same count the live "CPS" and the statistics use.
            int perPress = Math.Max(1, _lastPressClicks);

            if (!AntiFreezeEnabled)
            {
                _throttleFactor = 1.0;
                _verdict = FreezeVerdict.None;
                _effectiveCps = baseInterval > 0 ? (float)(perPress * 1000.0 / baseInterval) : 0f;
                return baseInterval;
            }

            // Hard cap: never more than MaxClicksPerSecond CLICKS a second. It used to be applied
            // to presses, so a Quadruple click at "max 200" sent 800 clicks a second, while the
            // rate line above it said "max 200".
            int cap = MaxClicksPerSecond < 1 ? 1 : MaxClicksPerSecond;
            double minInterval = 1000.0 * perPress / cap;
            if (minInterval < 0.5) minInterval = 0.5;

            double effective = Math.Max(baseInterval, minInterval);

            // Read the latest health reading every 250 ms or so and adjust the factor.
            UpdateThrottle();

            effective = effective * _throttleFactor;
            if (effective < minInterval) effective = minInterval;

            _effectiveCps = effective > 0 ? (float)(perPress * 1000.0 / effective) : 0f;
            return effective;
        }

        private void UpdateThrottle()
        {
            if (_throttleClock.ElapsedMilliseconds < 250)
            {
                return;
            }
            _throttleClock.Restart();

            long now = Environment.TickCount64;
            HealthSnapshot h = _health.Latest;
            long queueFull = InputSimulator.LastQueueFullTick;
            var d = Decide(h, now, queueFull != 0 && now - queueFull < 2000, _throttleFactor,
                           ref _lastSampleSeen, ref _cpuFullStreak, ref _slowStreak);
            _throttleFactor = d.Factor;
            if (!_holding)
            {
                _verdict = d.Reason == AntiFreezeReason.None ? FreezeVerdict.None : new FreezeVerdict(d.Reason, Evidence(h, d.Reason));
            }
        }

        /// <summary>The number behind a slowdown, read from the same reading <see cref="Decide"/> judged.</summary>
        private static double Evidence(HealthSnapshot h, AntiFreezeReason reason)
        {
            switch (reason)
            {
                case AntiFreezeReason.TargetSlow: return h.TargetAnswerMs;
                case AntiFreezeReason.CpuFull: return h.CpuPercent;
                case AntiFreezeReason.MemoryFull:
                    // Whichever of the two ran out (NaN when Windows had no answer for it).
                    if (double.IsNaN(h.CommitPercent)) { return h.MemoryLoadPercent; }
                    if (double.IsNaN(h.MemoryLoadPercent)) { return h.CommitPercent; }
                    return Math.Max(h.MemoryLoadPercent, h.CommitPercent);
                default: return double.NaN;
            }
        }

        /// <summary>
        /// One step of Anti-Freeze, as a pure function (the tests drive it with made-up readings). Grows the
        /// factor ×1.35 per step while something is wrong — ×2 for a full message queue, the one signal that
        /// clicks are already being lost — up to <see cref="MaxThrottle"/>; eases back ×0.85 once all is well.
        /// Sustained conditions are counted in distinct readings, not in steps (a reading lasts two steps).
        /// </summary>
        internal static (double Factor, AntiFreezeReason Reason) Decide(HealthSnapshot h, long now, bool queueFull, double factor,
                                                                        ref long lastSampleSeen, ref int cpuFullStreak, ref int slowStreak)
        {
            bool fresh = h != null && h.IsFresh(now);
            if (fresh && h.TakenTick != lastSampleSeen)
            {
                lastSampleSeen = h.TakenTick;
                cpuFullStreak = h.CpuPercent >= CpuFullPercent ? cpuFullStreak + 1 : 0;
                slowStreak = h.TargetAnswerMs > SlowAnswerMs ? slowStreak + 1 : 0;
            }
            else if (!fresh)
            {
                cpuFullStreak = slowStreak = 0;
            }

            double grow = Math.Min(factor * 1.35 + 0.05, MaxThrottle);
            if (queueFull)
            {
                return (Math.Min(factor * 2, MaxThrottle), AntiFreezeReason.QueueFull);
            }
            if (!fresh)
            {
                return (Math.Max(1.0, factor * 0.85), factor * 0.85 > 1.05 ? AntiFreezeReason.Recovering : AntiFreezeReason.None);
            }
            if (h.TargetNotAnswering)
            {
                return (grow, AntiFreezeReason.TargetNotAnswering);
            }
            if (slowStreak >= 2)
            {
                return (grow, AntiFreezeReason.TargetSlow);
            }
            // A full CPU or memory is only trouble when the app is not answering promptly (or cannot be asked):
            // a CPU-bound game sits at 100% and answers in 16 ms.
            bool answersPromptly = h.HasTarget && !h.TargetUnwatchable && h.TargetAnswerMs <= PromptAnswerMs;
            if (!answersPromptly && cpuFullStreak >= 2)
            {
                return (grow, AntiFreezeReason.CpuFull);
            }
            if (!answersPromptly && (h.MemoryLoadPercent >= MemoryFullPercent || h.CommitPercent >= MemoryFullPercent))
            {
                return (grow, AntiFreezeReason.MemoryFull);
            }
            double relax = Math.Max(1.0, factor * 0.85);
            return (relax, relax > 1.05 ? AntiFreezeReason.Recovering : AntiFreezeReason.None);
        }

        /// <summary>
        /// Holds the clicks while the app being clicked is not answering at all (its window did not answer
        /// within half a second, or Windows calls it "Not responding"). Returns true when the run should stop.
        /// The schedule restarts afterwards, so the missed clicks are not fired off at once to catch up.
        /// </summary>
        private bool HoldWhileTargetNotAnswering(RunContext run)
        {
            if (!AntiFreezeEnabled)
            {
                return false;
            }
            bool held = false;
            string who = "";
            while (IsCurrent(run) && !_paused)
            {
                HealthSnapshot h = _health.Latest;
                if (!(h.IsFresh(Environment.TickCount64) && h.TargetNotAnswering))
                {
                    break;
                }
                if (!held)
                {
                    held = true;
                    who = h.TargetName;
                    _holding = true;
                    _verdict = new FreezeVerdict(AntiFreezeReason.TargetNotAnswering, double.NaN);
                    Logger.Info("[Anti-Freeze] clicks held: " + who + " is not answering.");
                }
                if (run.Stop.Wait(100))
                {
                    break;
                }
            }
            if (held)
            {
                _holding = false;
                _resyncScheduler = true;
                if (IsCurrent(run)) { Logger.Info("[Anti-Freeze] " + who + " answers again — clicking resumes."); }
            }
            return !IsCurrent(run);
        }

        private void NoteClickPoint(int x, int y)
        {
            _lastX = x;
            _lastY = y;
            _lastPointValid = true;
        }

        private static string HealthOwner(RunContext run) => "clicker#" + run.Generation;

        /// <summary>
        /// Blocks while the engine is paused, returning promptly if a stop is
        /// requested. Returns true if the engine is no longer running.
        /// </summary>
        private bool WaitWhilePaused(RunContext run)
        {
            bool froze = false;
            while (IsCurrent(run) && _paused)
            {
                if (!froze)
                {
                    // Pause must not consume the "for N seconds" budget.
                    _runClock.Stop();
                    froze = true;
                }
                // Poll in short slices so both resume and stop react quickly.
                if (run.Stop.Wait(40))
                {
                    return true;
                }
            }

            if (froze && IsCurrent(run))
            {
                _runClock.Start();
            }

            return !IsCurrent(run);
        }

        /// <summary>
        /// Waits for approximately <paramref name="ms"/> milliseconds or until a
        /// stop is requested, with sub-millisecond accuracy. Returns true if a stop
        /// was requested (or this run is no longer the current one).
        /// </summary>
        private bool WaitInterruptible(double ms, RunContext run)
        {
            if (ms <= 0)
            {
                // Even when the schedule says "no wait" (very high CPS), never spin
                // the loop with zero yielding - that pegs a CPU core to 100% and can
                // freeze the UI / the whole system. Yield the remainder of our time
                // slice so other threads (including the UI) still get to run.
                System.Threading.Thread.Yield();
                return !IsCurrent(run);
            }

            if (PreciseWait.Wait(ms, run.Stop))
            {
                return true;
            }

            return !IsCurrent(run);
        }

        private void SetState(EngineState newState)
        {
            EngineState old = _state;
            _state = newState;
            if (old != newState)
            {
                StateChanged?.Invoke(this, new EngineStateChangedEventArgs(old, newState));
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ClickEngine));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _stopSignal.Dispose();
            _disposed = true;
        }
    }

    /// <summary>Why Anti-Freeze is slowing the clicks down (or holding them).</summary>
    public enum AntiFreezeReason
    {
        None,
        /// <summary>Easing back to full speed after a slowdown.</summary>
        Recovering,
        /// <summary>The app being clicked takes long to answer.</summary>
        TargetSlow,
        /// <summary>It does not answer at all: the clicks are held.</summary>
        TargetNotAnswering,
        /// <summary>A background target's message queue is full: it is not reading its clicks.</summary>
        QueueFull,
        /// <summary>The whole machine's CPU is full, and the app is slow to answer.</summary>
        CpuFull,
        /// <summary>Memory is nearly full (RAM or commit), and the app is slow to answer.</summary>
        MemoryFull,
    }
}
