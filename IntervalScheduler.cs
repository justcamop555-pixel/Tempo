using System;
using System.Diagnostics;

namespace AutoClicker.Engine
{
    /// <summary>
    /// Computes how long to wait before the next actuation so that the spacing
    /// between actuation *starts* matches the configured interval, compensating for
    /// the time spent performing each click.
    ///
    /// Without compensation the real period is <c>click-execution-time + interval</c>,
    /// which makes the effective rate slower than the user requested — noticeably so
    /// at short intervals. This scheduler tracks an ideal timeline and returns the
    /// remaining time until the next slot, so the long-run rate is accurate.
    ///
    /// If a click takes longer than the interval (so the schedule falls behind), the
    /// baseline is reset rather than accumulating "debt" that would otherwise cause a
    /// burst of catch-up clicks.
    /// </summary>
    public sealed class IntervalScheduler
    {
        private readonly Stopwatch _clock = new Stopwatch();
        private double _nextStartMs;

        /// <summary>Begins (or restarts) the schedule. Call once before a run.</summary>
        public void Start()
        {
            _clock.Restart();
            _nextStartMs = 0;
        }

        /// <summary>
        /// Advances the ideal timeline by <paramref name="intervalMs"/> and returns
        /// the number of milliseconds to wait from now until that slot. Never returns
        /// a negative value.
        /// </summary>
        public double NextWait(double intervalMs)
        {
            if (intervalMs < 0)
            {
                intervalMs = 0;
            }

            _nextStartMs += intervalMs;
            double now = _clock.Elapsed.TotalMilliseconds;
            double wait = _nextStartMs - now;

            if (wait < 0)
            {
                // Behind schedule: realign to "now" so we do not fire a catch-up
                // burst. A small amount of accuracy is sacrificed only when the
                // machine genuinely cannot keep up with the requested rate.
                _nextStartMs = now;
                wait = 0;
            }

            return wait;
        }

        /// <summary>Current elapsed time of the schedule, for diagnostics.</summary>
        public TimeSpan Elapsed => _clock.Elapsed;
    }
}
