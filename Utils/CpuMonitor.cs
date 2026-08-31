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

        public CpuMonitor()
        {
            try
            {
                _lastCpuTime = _process.TotalProcessorTime;
            }
            catch
            {
                _lastCpuTime = TimeSpan.Zero;
            }
            _lastSampleUtc = DateTime.UtcNow;
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
    }
}
