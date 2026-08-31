using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace AutoClicker.Engine
{
    /// <summary>
    /// A kernel waitable timer that can sleep for sub-millisecond periods accurately.
    ///
    /// Why this exists: the ordinary wait primitives (<see cref="ManualResetEventSlim"/>,
    /// <see cref="Thread.Sleep(int)"/>) only resolve to whole milliseconds and overshoot
    /// unpredictably, so <see cref="PreciseWait"/> used to make up the difference by
    /// BUSY-SPINNING the last few milliseconds of every interval. That is accurate but
    /// expensive, and the cost grows as the interval shrinks: measured on this machine,
    /// a 2.5 ms interval (400 CPS) burned 99.9% of a CPU core doing nothing but spinning.
    ///
    /// CreateWaitableTimerEx with CREATE_WAITABLE_TIMER_HIGH_RESOLUTION (Windows 10
    /// 1803 and later) sleeps in the kernel at ~0.5 ms accuracy, so almost all of that
    /// spinning can be replaced with an actual sleep.
    ///
    /// One timer is kept per thread and reused: creating a kernel object per click
    /// would cost more than it saves. <see cref="IsAvailable"/> is false on older
    /// Windows, where <see cref="PreciseWait"/> keeps its original behaviour.
    /// </summary>
    internal sealed class HighResolutionTimer : IDisposable
    {
        private const uint CreateHighResolution = 0x00000002;
        private const uint TimerAllAccess = 0x1F0003;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeWaitHandle CreateWaitableTimerExW(
            IntPtr timerAttributes, string timerName, uint flags, uint desiredAccess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWaitableTimer(
            SafeWaitHandle timer, ref long dueTime, int period,
            IntPtr completionRoutine, IntPtr argToCompletionRoutine,
            [MarshalAs(UnmanagedType.Bool)] bool resume);

        // One timer per thread. The engine waits on its own thread, so this is a
        // single object for the whole run rather than one per click.
        [ThreadStatic]
        private static HighResolutionTimer _forThread;

        private readonly SafeWaitHandle _handle;
        private readonly WaitHandle _waitable;
        private bool _disposed;

        private HighResolutionTimer(SafeWaitHandle handle)
        {
            _handle = handle;
            // Wrap the raw timer handle so it can take part in WaitHandle.WaitAny
            // alongside the engine's stop signal — that is what keeps a stop request
            // instant instead of waiting out the remaining interval.
            _waitable = new ManualResetEvent(false) { SafeWaitHandle = handle };
        }

        /// <summary>
        /// The calling thread's timer, or null when the OS has no high-resolution
        /// timer support. Never throws — callers fall back to spinning.
        /// </summary>
        public static HighResolutionTimer ForCurrentThread()
        {
            HighResolutionTimer t = _forThread;
            if (t != null && !t._disposed)
            {
                return t;
            }

            try
            {
                SafeWaitHandle h = CreateWaitableTimerExW(
                    IntPtr.Zero, null, CreateHighResolution, TimerAllAccess);

                if (h == null || h.IsInvalid)
                {
                    h?.Dispose();
                    return null;   // pre-1803 Windows: caller keeps the old path
                }

                t = new HighResolutionTimer(h);
                _forThread = t;
                return t;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>True when a high-resolution timer could be created here.</summary>
        public static bool IsAvailable => ForCurrentThread() != null;

        /// <summary>
        /// Sleeps for <paramref name="milliseconds"/>, returning early (and true) if
        /// <paramref name="stopSignal"/> is set. Returns false when the full time
        /// elapsed, or when the timer could not be armed and the caller should fall
        /// back to its own waiting.
        /// </summary>
        public bool Sleep(double milliseconds, ManualResetEventSlim stopSignal, out bool armed)
        {
            armed = false;
            if (_disposed || milliseconds <= 0)
            {
                return stopSignal != null && stopSignal.IsSet;
            }

            // Negative = relative time, in 100-nanosecond units.
            long due = -(long)Math.Round(milliseconds * 10_000.0);
            if (due >= 0)
            {
                due = -1;
            }

            if (!SetWaitableTimer(_handle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                return false;
            }

            armed = true;

            if (stopSignal == null)
            {
                _waitable.WaitOne();
                return false;
            }

            // Wake on whichever comes first: the timer expiring or a stop request.
            int index = WaitHandle.WaitAny(new[] { _waitable, stopSignal.WaitHandle });
            return index == 1;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try { _waitable?.Dispose(); } catch { }
            try { _handle?.Dispose(); } catch { }
            if (ReferenceEquals(_forThread, this))
            {
                _forThread = null;
            }
        }
    }
}
