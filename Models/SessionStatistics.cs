using System;
using System.Collections.Generic;

namespace AutoClicker.Models
{
    /// <summary>
    /// Tracks live statistics for the current run: total clicks, elapsed time and
    /// a short rolling history used to compute clicks-per-second.
    /// </summary>
    public sealed class SessionStatistics
    {
        private readonly object _sync = new object();
        private readonly Queue<DateTime> _recentClicks = new Queue<DateTime>();
        private static readonly TimeSpan CpsWindow = TimeSpan.FromSeconds(1);

        public long TotalClicks { get; private set; }
        public long SessionClicks { get; private set; }
        public long LeftClicks { get; private set; }
        public long RightClicks { get; private set; }
        public long MiddleClicks { get; private set; }
        public DateTime? RunStartedUtc { get; private set; }
        public DateTime? RunStoppedUtc { get; private set; }
        public double PeakClicksPerSecond { get; private set; }

        // Paused time, subtracted from the run's elapsed time.
        //
        // Without this, "elapsed" was wall-clock from BeginRun to EndRun, so a pause
        // counted as time spent clicking. Everything downstream inherited it: the
        // Average CPS card, the saved SessionRecord's DurationSeconds and AverageCps,
        // LifetimeRuntimeSeconds, the per-profile runtime and LifetimeLongestRunSeconds.
        //
        // Measured on build 260906-1200 — the same four seconds of clicking, twice:
        //     4s clicking, no pause      : 203 clicks, 4.07 s, 49.9 CPS   (correct)
        //     4s clicking, 5s paused     : 205 clicks, 9.19 s, 22.3 CPS   (wrong)
        // The engine already had the right number in its own _runClock, which stops
        // while paused; only the statistics kept a second, naive clock.
        private DateTime? _pausedAtUtc;
        private TimeSpan _pausedTotal;

        /// <summary>Marks the beginning of a run.</summary>
        public void BeginRun()
        {
            lock (_sync)
            {
                SessionClicks = 0;
                RunStartedUtc = DateTime.UtcNow;
                RunStoppedUtc = null;
                _recentClicks.Clear();

                // Peak belongs to THIS run, like everything else reset here. It used to
                // survive, and that quietly corrupted the session history: PeakCps on
                // every SessionRecord is read straight off this field, so run 2 onwards
                // reported the highest rate seen since the app started rather than its
                // own. Measured — a 40-clicks-as-fast-as-possible run followed by a
                // deliberate 3-click, 2-per-second run recorded 40.0 CPS for BOTH, so a
                // gentle run was filed in the history as the fastest one on record.
                // Clicks and AverageCps beside it were always per-run (both derive from a
                // start-of-run baseline); only this one was missed.
                //
                // Safe for the lifetime figure: LifetimePeakCps is folded in when the run
                // is SAVED, which happens on stop — always before the next BeginRun.
                PeakClicksPerSecond = 0;
                _pausedTotal = TimeSpan.Zero;
                _pausedAtUtc = null;
            }
        }

        /// <summary>Marks the end of a run.</summary>
        public void EndRun()
        {
            lock (_sync)
            {
                RunStoppedUtc = DateTime.UtcNow;
            }
        }

        /// <summary>The run was paused: stop counting elapsed time.</summary>
        public void PauseRun()
        {
            lock (_sync)
            {
                if (RunStartedUtc != null && _pausedAtUtc == null)
                {
                    _pausedAtUtc = DateTime.UtcNow;
                }
            }
        }

        /// <summary>The run resumed: bank the pause and start counting again.</summary>
        public void ResumeRun()
        {
            lock (_sync)
            {
                if (_pausedAtUtc != null)
                {
                    _pausedTotal += DateTime.UtcNow - _pausedAtUtc.Value;
                    _pausedAtUtc = null;
                }
            }
        }

        /// <summary>Records that one click was performed.</summary>
        public void RecordClick()
        {
            RecordClicksInternal(1, null);
        }

        /// <summary>Records that <paramref name="count"/> clicks were performed.</summary>
        public void RecordClicks(int count)
        {
            RecordClicksInternal(count, null);
        }

        /// <summary>
        /// Records <paramref name="count"/> clicks attributed to a specific mouse
        /// button, so the dashboard can show a per-button breakdown.
        /// </summary>
        public void RecordClicks(int count, MouseButtonType button)
        {
            RecordClicksInternal(count, button);
        }

        private void RecordClicksInternal(int count, MouseButtonType? button)
        {
            if (count <= 0)
            {
                return;
            }

            lock (_sync)
            {
                TotalClicks += count;
                SessionClicks += count;

                if (button.HasValue)
                {
                    switch (button.Value)
                    {
                        case MouseButtonType.Left: LeftClicks += count; break;
                        case MouseButtonType.Right: RightClicks += count; break;
                        case MouseButtonType.Middle: MiddleClicks += count; break;
                    }
                }

                var now = DateTime.UtcNow;
                for (int i = 0; i < count; i++)
                {
                    _recentClicks.Enqueue(now);
                }

                TrimWindow(now);

                double cps = _recentClicks.Count / CpsWindow.TotalSeconds;
                if (cps > PeakClicksPerSecond)
                {
                    PeakClicksPerSecond = cps;
                }
            }
        }

        /// <summary>Current clicks-per-second over the rolling window.</summary>
        public double GetCurrentCps()
        {
            lock (_sync)
            {
                TrimWindow(DateTime.UtcNow);
                return _recentClicks.Count / CpsWindow.TotalSeconds;
            }
        }

        /// <summary>Elapsed time of the current or last run.</summary>
        public TimeSpan GetElapsed()
        {
            lock (_sync)
            {
                if (RunStartedUtc == null)
                {
                    return TimeSpan.Zero;
                }

                DateTime end = RunStoppedUtc ?? DateTime.UtcNow;

                // Any pause still open at `end` counts too — a run stopped while paused
                // must not bank the pause it was sitting in.
                TimeSpan paused = _pausedTotal;
                if (_pausedAtUtc != null && end > _pausedAtUtc.Value)
                {
                    paused += end - _pausedAtUtc.Value;
                }

                TimeSpan active = end - RunStartedUtc.Value - paused;
                return active > TimeSpan.Zero ? active : TimeSpan.Zero;
            }
        }

        /// <summary>Average clicks-per-second across the whole run.</summary>
        public double GetAverageCps()
        {
            lock (_sync)
            {
                TimeSpan elapsed = GetElapsed();
                double seconds = elapsed.TotalSeconds;
                if (seconds <= 0.0001)
                {
                    return 0;
                }

                return SessionClicks / seconds;
            }
        }

        /// <summary>Average clicks-per-minute across the whole run.</summary>
        public double GetClicksPerMinute()
        {
            lock (_sync)
            {
                TimeSpan elapsed = GetElapsed();
                double minutes = elapsed.TotalMinutes;
                if (minutes <= 0.0001)
                {
                    return 0;
                }

                return SessionClicks / minutes;
            }
        }

        public void ResetAll()
        {
            lock (_sync)
            {
                TotalClicks = 0;
                SessionClicks = 0;
                LeftClicks = 0;
                RightClicks = 0;
                MiddleClicks = 0;
                PeakClicksPerSecond = 0;
                RunStartedUtc = null;
                RunStoppedUtc = null;
                _pausedTotal = TimeSpan.Zero;
                _pausedAtUtc = null;
                _recentClicks.Clear();
            }
        }

        private void TrimWindow(DateTime now)
        {
            DateTime cutoff = now - CpsWindow;
            while (_recentClicks.Count > 0 && _recentClicks.Peek() < cutoff)
            {
                _recentClicks.Dequeue();
            }
        }
    }
}
