using System;
using AutoClicker.Native;
using AutoClicker.Utils;

namespace AutoClicker.Engine
{
    /// <summary>
    /// Requests a finer system timer resolution for the lifetime of the instance
    /// and restores the previous resolution on disposal. Raising the resolution
    /// (typically to 1 ms) tightens the accuracy of every timed wait the engine
    /// performs, so a configured click interval is honoured far more precisely.
    ///
    /// The class is safe to construct even if the underlying call fails; in that
    /// case it simply becomes a no-op and the application continues at the default
    /// (coarser) resolution.
    /// </summary>
    public sealed class TimerResolution : IDisposable
    {
        private readonly uint _period;
        private readonly bool _active;
        private bool _disposed;

        /// <summary>
        /// Requests the given resolution in milliseconds (clamped to a sane range).
        /// </summary>
        public TimerResolution(uint periodMs = 1)
        {
            if (periodMs < 1)
            {
                periodMs = 1;
            }

            if (periodMs > 15)
            {
                periodMs = 15;
            }

            _period = periodMs;

            try
            {
                _active = NativeMethods.timeBeginPeriod(_period) == NativeMethods.TIMERR_NOERROR;
                if (_active)
                {
                    Logger.Info($"[perf] raised the system timer resolution to {_period} ms.");
                }
                else
                {
                    Logger.Warn("[perf] could not raise the system timer resolution; using the default.");
                }
            }
            catch (Exception ex)
            {
                _active = false;
                Logger.Warn("timeBeginPeriod is unavailable: " + ex.Message);
            }
        }

        public bool IsActive => _active;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_active)
            {
                try
                {
                    NativeMethods.timeEndPeriod(_period);
                    Logger.Info("[perf] restored the system timer resolution.");
                }
                catch (Exception ex)
                {
                    Logger.Warn("timeEndPeriod failed: " + ex.Message);
                }
            }
        }
    }
}
