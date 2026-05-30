using System;
using System.Diagnostics;
using System.Threading;
using AutoClicker.Models;
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
        private readonly RandomizationHelper _random = new RandomizationHelper();
        private readonly IntervalScheduler _scheduler = new IntervalScheduler();

        private Thread _worker;
        private ManualResetEventSlim _stopSignal = new ManualResetEventSlim(false);
        private volatile bool _running;
        private volatile bool _paused;
        private volatile bool _resyncScheduler;
        private EngineState _state = EngineState.Idle;

        // ── Anti-freeze protection ────────────────────────────────────────────
        private readonly CpuMonitor _cpuMonitor = new CpuMonitor();
        private readonly Stopwatch _cpuSampleClock = new Stopwatch();
        public bool AntiFreezeEnabled { get; set; } = true;
        public int MaxClicksPerSecond { get; set; } = 200;
        public int CpuThresholdPercent { get; set; } = 80;
        private double _throttleFactor = 1.0;
        private volatile float _measuredCpu;
        private volatile float _effectiveCps;

        /// <summary>Latest measured process CPU usage (0–100%).</summary>
        public double MeasuredCpuPercent => _measuredCpu;

        /// <summary>The actual click rate the engine is currently issuing.</summary>
        public double EffectiveClicksPerSecond => _effectiveCps;

        /// <summary>True while anti-freeze is actively slowing the configured rate.</summary>
        public bool IsThrottling => _throttleFactor > 1.05;

        // ── Multi-point traversal state ───────────────────────────────────────
        private readonly Random _mpRandom = new Random();
        private volatile int _currentPointIndex = -1;
        private int _mpDirection = 1;
        private int _mpRepeatLeft = 0;

        /// <summary>Index (within the profile's Points list) of the point currently
        /// being clicked, or -1 when not in multi-point mode. For the live UI.</summary>
        public int CurrentPointIndex => _currentPointIndex;
        private ClickProfile _activeProfile;
        private bool _disposed;

        public event EventHandler<EngineStateChangedEventArgs> StateChanged;
        public event EventHandler ClickPerformed;
        public event EventHandler RunCompleted;

        public ClickEngine(SessionStatistics statistics)
        {
            _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
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
                _stopSignal.Reset();
                _running = true;
                _paused = false;
                _resyncScheduler = false;
                _throttleFactor = 1.0;
                _measuredCpu = 0;
                _effectiveCps = 0;
                _cpuSampleClock.Restart();
                _currentPointIndex = -1;
                _mpDirection = 1;
                _mpRepeatLeft = 0;

                _worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "AutoClicker.Worker"
                };

                _statistics.BeginRun();
                SetState(EngineState.Running);
                _worker.Start();
            }

            Logger.Info($"Engine started with profile '{profile.Name}'.");
            return null;
        }

        /// <summary>Requests the engine stop and waits briefly for the worker.</summary>
        public void Stop()
        {
            Thread workerToJoin = null;

            lock (_sync)
            {
                if (!_running)
                {
                    return;
                }

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

            Logger.Info("Engine stopped.");
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
                SetState(EngineState.Paused);
            }

            Logger.Info("Engine paused.");
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
                _resyncScheduler = true; // worker resets the schedule baseline
                SetState(EngineState.Running);
            }

            Logger.Info("Engine resumed.");
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

        private void WorkerLoop()
        {
            try
            {
                ClickProfile p;
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
                        RunBurstLoop(p);
                        break;
                    default:
                        RunIntervalLoop(p);
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Unhandled exception in worker loop.", ex);
            }
            finally
            {
                _running = false;

                // Ensure the state flag returns to Idle even when the run ended on
                // its own (e.g. a fixed repeat count completed) rather than via
                // Stop(). Without this, a subsequent Start() would not raise a
                // state-change because the stored state would still read Running.
                lock (_sync)
                {
                    _statistics.EndRun();
                    SetState(EngineState.Idle);
                }

                RunCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        private void RunIntervalLoop(ClickProfile p)
        {
            long performed = 0;
            long max = p.RepeatMode == RepeatMode.FixedCount ? p.RepeatCount : long.MaxValue;

            _scheduler.Start();

            while (_running && performed < max)
            {
                // Block here while paused; realign the schedule on resume.
                if (WaitWhilePaused())
                {
                    break;
                }
                if (_resyncScheduler)
                {
                    _scheduler.Start();
                    _resyncScheduler = false;
                }

                long interval = PerformOneActuation(p);
                performed++;

                if (!_running || performed >= max)
                {
                    break;
                }

                // Anti-freeze: clamp to the safety cap and back off under CPU load.
                long effInterval = ApplyAntiFreeze(interval);

                // Wait the remaining time until the next scheduled slot rather than
                // a fixed interval, so click execution time does not slow the rate.
                double wait = _scheduler.NextWait(effInterval);
                if (WaitInterruptible(wait))
                {
                    break; // stop requested
                }
            }
        }

        private void RunBurstLoop(ClickProfile p)
        {
            long performed = 0;
            long max = p.RepeatMode == RepeatMode.FixedCount ? p.RepeatCount : long.MaxValue;

            while (_running && performed < max)
            {
                if (WaitWhilePaused())
                {
                    break;
                }

                int burst = p.BurstSize < 1 ? 1 : p.BurstSize;

                // Each burst is scheduled internally so the in-burst rate is accurate.
                _scheduler.Start();
                _resyncScheduler = false;

                for (int i = 0; i < burst && _running && performed < max; i++)
                {
                    long interval = PerformOneActuation(p);
                    performed++;

                    if (i < burst - 1 && _running && performed < max)
                    {
                        double wait = _scheduler.NextWait(interval);
                        if (WaitInterruptible(wait))
                        {
                            return;
                        }
                    }
                }

                if (!_running || performed >= max)
                {
                    break;
                }

                // Pause between bursts (precise, but does not need drift tracking).
                if (WaitInterruptible(p.BurstPauseMilliseconds))
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
        private long PerformOneActuation(ClickProfile p)
        {
            MouseButtonType button = p.Button;
            ClickStyle style = p.Style;
            MouseButtonType actuatedButton = button;
            long interval = p.GetBaseIntervalMilliseconds();

            if (p.PositionMode == PositionMode.MultiPoint)
            {
                ClickPoint point = AdvanceMultiPoint(p);
                if (point != null)
                {
                    int x = point.X;
                    int y = point.Y;
                    if (p.RandomizePosition)
                    {
                        _random.JitterPosition(ref x, ref y, p.PositionJitterPixels);
                    }

                    InputSimulator.MoveAndClick(x, y, point.Button, point.Style);
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
                    _random.JitterPosition(ref x, ref y, p.PositionJitterPixels);
                }

                InputSimulator.MoveAndClick(x, y, button, style);
            }
            else if (p.RandomizePosition)
            {
                // Current position but with jitter applied around it: read the
                // cursor, jitter, then issue a batched move + click.
                if (ScreenGeometry.TryGetCursorPosition(out int cx, out int cy))
                {
                    _random.JitterPosition(ref cx, ref cy, p.PositionJitterPixels);
                    InputSimulator.MoveAndClick(cx, cy, button, style);
                }
                else
                {
                    InputSimulator.ClickStyled(button, style);
                }
            }
            else
            {
                // Pure "click where the cursor is" — no move event needed.
                InputSimulator.ClickStyled(button, style);
            }

            // Count every real mouse click, not just the actuation. A double-click
            // style produces two clicks, a triple three, and so on.
            int clicks = StyleToClicks(style);
            _statistics.RecordClicks(clicks, actuatedButton);
            ClickPerformed?.Invoke(this, EventArgs.Empty);

            if (p.RandomizeInterval)
            {
                interval = _random.JitterInterval(interval, p.IntervalJitterMilliseconds);
            }

            return interval;
        }

        /// <summary>
        /// Chooses the next point to click, honouring the profile's traversal order
        /// (sequential, reverse, random, ping-pong) and each point's repeat count.
        /// Publishes the chosen point's list index for the live UI highlight.
        /// </summary>
        private ClickPoint AdvanceMultiPoint(ClickProfile p)
        {
            // Build the list of enabled point indices.
            var enabled = new System.Collections.Generic.List<int>(p.Points.Count);
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
                        do { nextPos = _mpRandom.Next(enabled.Count); }
                        while (nextPos == pos);
                    }
                    break;

                case MultiPointOrder.PingPong:
                    if (enabled.Count == 1)
                    {
                        nextPos = 0;
                    }
                    else
                    {
                        if (pos < 0) { pos = 0; _mpDirection = 1; }
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
                default: return 1;
            }
        }

        /// <summary>
        /// Updates the active profile's interval components while a run is in
        /// progress, without stopping the engine. Returns false when nothing is
        /// running. The next loop iteration picks up the new value, and the
        /// drift-compensating scheduler is resynced to align cleanly.
        /// </summary>
        public bool UpdateInterval(int hours, int minutes, int seconds, int millis)
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
                _resyncScheduler = true;
            }

            return true;
        }

        /// <summary>
        /// Applies anti-freeze protection to a desired interval: enforces the
        /// hard clicks-per-second cap and multiplies the interval by an adaptive
        /// throttle factor that grows when this process's CPU usage exceeds the
        /// configured threshold. Also publishes the live measured CPU and the
        /// effective click rate for the UI. Returns the interval to actually wait.
        /// </summary>
        private long ApplyAntiFreeze(long interval)
        {
            long baseInterval = Math.Max(1, interval);

            if (!AntiFreezeEnabled)
            {
                _measuredCpu = 0;
                _throttleFactor = 1.0;
                _effectiveCps = baseInterval > 0 ? (float)(1000.0 / baseInterval) : 0f;
                return baseInterval;
            }

            // Hard cap: never faster than MaxClicksPerSecond.
            int cap = MaxClicksPerSecond < 1 ? 1 : MaxClicksPerSecond;
            long minInterval = (long)Math.Ceiling(1000.0 / cap);
            if (minInterval < 1) minInterval = 1;

            long effective = Math.Max(baseInterval, minInterval);

            // Sample CPU roughly every 250ms and adjust the throttle.
            UpdateThrottle();

            effective = (long)Math.Round(effective * _throttleFactor);
            if (effective < minInterval) effective = minInterval;

            _effectiveCps = effective > 0 ? (float)(1000.0 / effective) : 0f;
            return effective;
        }

        private void UpdateThrottle()
        {
            if (_cpuSampleClock.ElapsedMilliseconds < 250)
            {
                return;
            }
            _cpuSampleClock.Restart();

            double cpu = _cpuMonitor.Sample();
            _measuredCpu = (float)cpu;

            int threshold = CpuThresholdPercent < 10 ? 10 : CpuThresholdPercent;

            if (cpu > threshold)
            {
                // Over budget — back off (grow the interval). Cap the factor so a
                // pegged CPU can't slow the clicker to a crawl indefinitely.
                _throttleFactor = Math.Min(_throttleFactor * 1.35 + 0.05, 16.0);
            }
            else if (cpu < threshold - 15)
            {
                // Comfortable headroom — relax back toward full speed.
                _throttleFactor = Math.Max(1.0, _throttleFactor * 0.80);
            }
        }

        /// <summary>
        /// Blocks while the engine is paused, returning promptly if a stop is
        /// requested. Returns true if the engine is no longer running.
        /// </summary>
        private bool WaitWhilePaused()
        {
            while (_running && _paused)
            {
                // Poll in short slices so both resume and stop react quickly.
                if (_stopSignal.Wait(40))
                {
                    return true;
                }
            }

            return !_running;
        }

        /// <summary>
        /// Waits for approximately <paramref name="ms"/> milliseconds or until a
        /// stop is requested, with sub-millisecond accuracy. Returns true if a stop
        /// was requested (or the engine is no longer running).
        /// </summary>
        private bool WaitInterruptible(double ms)
        {
            if (ms <= 0)
            {
                return !_running;
            }

            if (PreciseWait.Wait(ms, _stopSignal))
            {
                return true;
            }

            return !_running;
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
}
