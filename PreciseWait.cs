using System;
using System.Diagnostics;
using System.Threading;

namespace AutoClicker.Engine
{
    /// <summary>
    /// Provides a high-accuracy, interruptible delay.
    ///
    /// A plain <see cref="ManualResetEventSlim.Wait(int)"/> or
    /// <see cref="Thread.Sleep(int)"/> is only accurate to the system timer
    /// resolution (≈1 ms once <see cref="TimerResolution"/> is active, but ≈15 ms
    /// otherwise) and tends to overshoot. <see cref="Wait"/> spends the bulk of the
    /// delay in an efficient wait-handle sleep, then "spins" for the final couple
    /// of milliseconds using a <see cref="Stopwatch"/> so the target time is hit
    /// closely without burning the CPU for the whole interval.
    ///
    /// The stop signal is honoured throughout: a request to stop returns almost
    /// immediately rather than after the full delay.
    /// </summary>
    public static class PreciseWait
    {
        // The final slice of the delay is spent spinning for accuracy. Keeping this
        // small bounds the CPU cost while still removing scheduler jitter.
        private const double SpinTailMilliseconds = 4.0;

        // While spinning we still poll the stop signal this often (in spins).
        private const int SpinBatch = 48;

        /// <summary>
        /// Waits for approximately <paramref name="milliseconds"/> or until
        /// <paramref name="stopSignal"/> is set. Returns <c>true</c> if the wait
        /// was cut short by the stop signal.
        /// </summary>
        public static bool Wait(double milliseconds, ManualResetEventSlim stopSignal)
        {
            if (stopSignal == null)
            {
                throw new ArgumentNullException(nameof(stopSignal));
            }

            if (milliseconds <= 0)
            {
                return stopSignal.IsSet;
            }

            var clock = Stopwatch.StartNew();

            // ── Coarse phase ──────────────────────────────────────────────────
            // Sleep on the wait handle for everything except the spin tail. Doing
            // this in chunks keeps the stop latency low even for very long waits.
            double coarseTarget = milliseconds - SpinTailMilliseconds;
            while (true)
            {
                double remaining = coarseTarget - clock.Elapsed.TotalMilliseconds;
                if (remaining <= 0)
                {
                    break;
                }

                // Cap each chunk so a multi-hour wait still reacts to a stop within
                // a second, and never overflow the int the API expects.
                int chunk = remaining > 1000.0 ? 1000 : (int)Math.Ceiling(remaining);
                if (chunk < 1)
                {
                    chunk = 1;
                }

                if (stopSignal.Wait(chunk))
                {
                    return true;
                }
            }

            // ── Spin tail ─────────────────────────────────────────────────────
            // Busy-wait the remaining sub-millisecond slice for precise landing,
            // yielding to other threads and checking the stop signal regularly.
            while (clock.Elapsed.TotalMilliseconds < milliseconds)
            {
                if (stopSignal.IsSet)
                {
                    return true;
                }

                Thread.SpinWait(SpinBatch);
            }

            return stopSignal.IsSet;
        }
    }
}
