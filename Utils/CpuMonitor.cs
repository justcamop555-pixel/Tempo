using System;
using System.Diagnostics;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Measures how much CPU this process is using, expressed as a percentage of
    /// the whole machine's capacity (0–100, across all logical cores). Used by the
    /// anti-freeze system to detect when an extreme click rate is starting to load
    /// the CPU so the engine can back off before the system becomes unresponsive.
    ///
    /// Call <see cref="Sample"/> periodically; each call returns the average usage
    /// since the previous call.
    /// </summary>
    public sealed class CpuMonitor
    {
        private readonly Process _process = Process.GetCurrentProcess();
        private readonly int _coreCount = Math.Max(1, Environment.ProcessorCount);
        private TimeSpan _lastCpuTime;
        private DateTime _lastSampleUtc;

        // Precise mode: CPU cycles instead of CPU time. Windows adds to a process's CPU time in
        // whole scheduler ticks (15.625 ms, measured on this project's PC), so over one second on
        // a 16-core machine the figure moved in steps of ~0.1% — a second decimal would have been
        // made up. QueryProcessCycleTime counts every cycle; the cycles-per-millisecond rate comes
        // from the process's LIFETIME totals of both (the tick rounding averages out over them).
        private readonly bool _precise;
        private ulong _lastCycles;
        private readonly Stopwatch _wall = Stopwatch.StartNew();
        private double _lastWallMs;

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool QueryProcessCycleTime(IntPtr process, out ulong cycles);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        public CpuMonitor() : this(false) { }

        /// <param name="precise">Measure in CPU cycles, fine enough for two decimals (the status bar).
        /// The default whole-tick measurement is what Anti-Freeze was tuned with.</param>
        public CpuMonitor(bool precise)
        {
            _precise = precise;
            try
            {
                _lastCpuTime = _process.TotalProcessorTime;
            }
            catch
            {
                _lastCpuTime = TimeSpan.Zero;
            }
            _lastSampleUtc = DateTime.UtcNow;
            if (_precise)
            {
                try { QueryProcessCycleTime(GetCurrentProcess(), out _lastCycles); } catch { _lastCycles = 0; }
                _lastWallMs = _wall.Elapsed.TotalMilliseconds;
            }
        }

        /// <summary>
        /// Returns this process's CPU usage (0–100%) averaged over the interval
        /// since the last call. Safe to call from a worker thread.
        /// </summary>
        public double Sample()
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                TimeSpan cpuNow = _process.TotalProcessorTime;

                if (_precise && TrySamplePrecise(cpuNow, out double precisePercent))
                {
                    _lastSampleUtc = now;
                    _lastCpuTime = cpuNow;
                    return precisePercent;
                }

                double wallMs = (now - _lastSampleUtc).TotalMilliseconds;
                if (wallMs <= 0.0001)
                {
                    return 0;
                }

                double cpuMs = (cpuNow - _lastCpuTime).TotalMilliseconds;

                _lastSampleUtc = now;
                _lastCpuTime = cpuNow;

                double percent = cpuMs / (wallMs * _coreCount) * 100.0;
                if (percent < 0) percent = 0;
                if (percent > 100) percent = 100;
                return percent;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>The cycle-count reading; false (fall back to CPU time) until it can be trusted.</summary>
        private bool TrySamplePrecise(TimeSpan lifetimeCpu, out double percent)
        {
            percent = 0;
            double wallNow = _wall.Elapsed.TotalMilliseconds;
            if (!QueryProcessCycleTime(GetCurrentProcess(), out ulong cycles) || cycles == 0)
            {
                return false;
            }
            double wallMs = wallNow - _lastWallMs;
            ulong spent = cycles >= _lastCycles ? cycles - _lastCycles : 0;
            _lastCycles = cycles;
            _lastWallMs = wallNow;

            // Not before a second of CPU has been used in all: until then the tick rounding is a
            // large part of the lifetime total, and so of the rate taken from it.
            double lifetimeMs = lifetimeCpu.TotalMilliseconds;
            if (lifetimeMs < 1000 || wallMs <= 0.0001)
            {
                return false;
            }
            double cyclesPerMs = cycles / lifetimeMs;
            if (cyclesPerMs <= 0) { return false; }

            percent = spent / cyclesPerMs / (wallMs * _coreCount) * 100.0;
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            return true;
        }

        /// <summary>The cycle-count reading's test seam: the percentage from these totals, as <see cref="TrySamplePrecise"/> works it out.</summary>
        internal static double PercentFromCycles(ulong cyclesSpent, ulong lifetimeCycles, double lifetimeCpuMs, double wallMs, int cores)
        {
            if (lifetimeCpuMs <= 0 || wallMs <= 0 || cores <= 0 || lifetimeCycles == 0) { return 0; }
            double cyclesPerMs = lifetimeCycles / lifetimeCpuMs;
            double p = cyclesSpent / cyclesPerMs / (wallMs * cores) * 100.0;
            return p < 0 ? 0 : p > 100 ? 100 : p;
        }
    }
}
