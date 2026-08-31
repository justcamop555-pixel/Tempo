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
        //
        // Two tails, because the two sleep sources have very different jitter:
        //   - the coarse ManualResetEventSlim path overshoots by whole milliseconds,
        //     so it needs a 4 ms cushion;
        //   - a high-resolution kernel timer lands within a few tenths of a
        //     millisecond, so a 0.4 ms cushion is enough.
        // The tail is pure busy-wait, so shrinking it is exactly where the CPU saving
        // comes from: at 400 CPS the old 4 ms tail was the ENTIRE interval.
        private const double SpinTailMilliseconds = 4.0;
        private const double HighResolutionSpinTailMilliseconds = 0.4;

        // While spinning we still poll the stop signal this often (in spins).
        private const int SpinBatch = 48;

        /// <summary>
        /// True when waits are backed by a high-resolution kernel timer rather than
        /// by busy-waiting. Reported in Live debug so the cost model is visible.
        /// </summary>
        public static bool HighResolutionAvailable => HighResolutionTimer.IsAvailable;

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

            // ── Preferred phase: sleep in the kernel ──────────────────────────
            // A high-resolution waitable timer can sleep the interval accurately
            // instead of spinning it away. Only the last fraction of a millisecond
            // is spun, which is what takes the CPU cost from "a whole core" to
            // "barely measurable" at high click rates.
            HighResolutionTimer hires = HighResolutionTimer.ForCurrentThread();
            if (hires != null)
            {
                double sleepFor = milliseconds - HighResolutionSpinTailMilliseconds;
                if (sleepFor > 0)
                {
                    if (hires.Sleep(sleepFor, stopSignal, out bool armed))
                    {
                        return true;   // stop signal won the race
                    }

                    if (!armed)
                    {
                        // Arming failed (rare). Fall through to the coarse path
                        // rather than returning early with the wait unserved.
                        goto coarse;
                    }
                }

                return SpinUntil(clock, milliseconds, stopSignal);
            }

        coarse:

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

            return SpinUntil(clock, milliseconds, stopSignal);
        }

        /// <summary>
        /// Busy-waits the remaining slice for a precise landing, checking the stop
        /// signal regularly. Shared by both paths — it is the only part that costs
        /// CPU, which is why each path keeps its own tail as short as it can.
        /// </summary>
        private static bool SpinUntil(Stopwatch clock, double milliseconds, ManualResetEventSlim stopSignal)
        {
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
