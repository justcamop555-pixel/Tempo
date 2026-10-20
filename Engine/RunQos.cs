using System;
using System.Runtime.InteropServices;
using AutoClicker.Native;
using AutoClicker.Utils;

namespace AutoClicker.Engine
{
    /// <summary>
    /// Full speed while something runs — and Windows' own power management the rest of the time.
    ///
    /// Windows gives each process a Quality of Service level from its window's state: in focus = High,
    /// visible = Medium, minimized or fully covered = Low, and on battery Low means the most efficient CPU
    /// frequency and the efficient cores of a hybrid CPU (Microsoft Learn, "Quality of Service"). Tempo's runs
    /// happen exactly there: in the tray behind a game, or minimized ("Minimise window during macro record &
    /// playback" is on by default). On a laptop on battery the click thread was moved to slow cores at low
    /// clocks, and Windows 11 also stops honouring the timer resolution of a minimized or hidden window, so
    /// every hold and short wait not on the high-resolution timer was rounded up to ~15.6 ms.
    ///
    /// So for the length of a run Tempo tags itself HighQoS and asks for its timer requests to be honoured
    /// even while hidden (SetProcessInformation, ProcessPowerThrottling, EXECUTION_SPEED and
    /// IGNORE_TIMER_RESOLUTION switched OFF), and holds the 1 ms timer resolution. Between runs both are
    /// handed back — "let the system decide" and no timer request — instead of the 1 ms request Tempo used to
    /// hold for its whole life, which kept a laptop's timer firing a thousand times a second while Tempo sat
    /// in the tray doing nothing (and, before Windows 10 2004, for every other program too).
    /// </summary>
    internal static class RunQos
    {
        private static readonly object Sync = new object();
        private static bool _fullSpeed;
        private static bool _timerHeld;
        private static string _state = "Windows decides (idle)";

        /// <summary>True while a run holds full speed.</summary>
        public static bool FullSpeed { get { lock (Sync) { return _fullSpeed; } } }

        /// <summary>What was asked of Windows, for Live debug.</summary>
        public static string Describe() { lock (Sync) { return _state; } }

        /// <summary>
        /// Full speed on (something runs) or off (nothing does). Idempotent and cheap while unchanged, so it
        /// can be called on every busy-state change and every status tick.
        /// </summary>
        public static void SetRunning(bool running)
        {
            lock (Sync)
            {
                if (running == _fullSpeed) { return; }
                _fullSpeed = running;
                if (running)
                {
                    string qos = ApplyThrottling(highQos: true);
                    _timerHeld = BeginTimer();
                    _state = "full speed while running — " + qos + (_timerHeld ? ", 1 ms timer held" : ", timer request refused");
                }
                else
                {
                    if (_timerHeld) { EndTimer(); _timerHeld = false; }
                    string qos = ApplyThrottling(highQos: false);
                    _state = "Windows decides (idle) — " + qos;
                }
                Logger.Info("[perf] " + _state + ".");
            }
        }

        // ── the process's power throttling ──────────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version, ControlMask, StateMask;
        }

        private const int ProcessPowerThrottling = 4;   // PROCESS_INFORMATION_CLASS
        private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
        private const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x4;   // Windows 11

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessInformation(IntPtr process, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, uint size);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        /// <summary>
        /// HighQoS: ControlMask names the features, StateMask 0 switches them OFF ("never throttle these").
        /// Idle: both masks 0 — "let the system manage all power throttling". Windows 10 does not know the
        /// timer-resolution feature and refuses the call, so it is retried with execution speed alone.
        /// </summary>
        private static string ApplyThrottling(bool highQos)
        {
            try
            {
                if (!highQos)
                {
                    return Set(0) ? "power throttling handed back to Windows" : "could not hand power throttling back";
                }
                if (Set(PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION))
                {
                    return "HighQoS, timer kept when hidden";
                }
                return Set(PROCESS_POWER_THROTTLING_EXECUTION_SPEED) ? "HighQoS" : "power throttling unchanged (refused)";
            }
            catch (EntryPointNotFoundException)
            {
                return "power throttling not available on this Windows";
            }
        }

        private static bool Set(uint controlMask)
        {
            var s = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = controlMask,
                StateMask = 0,
            };
            return SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref s, (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }

        /// <summary>For the tests: the masks Windows holds for Tempo now (control, state), or null.</summary>
        internal static (uint Control, uint State)? ReadThrottling()
        {
            try
            {
                var s = new PROCESS_POWER_THROTTLING_STATE { Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION };
                return GetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref s, (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>())
                    ? (s.ControlMask, s.StateMask)
                    : ((uint, uint)?)null;
            }
            catch { return null; }
        }

        // ── the timer resolution ────────────────────────────────────────────────────────────────────

        private static bool BeginTimer()
        {
            try { return NativeMethods.timeBeginPeriod(1) == NativeMethods.TIMERR_NOERROR; }
            catch { return false; }
        }

        private static void EndTimer()
        {
            try { NativeMethods.timeEndPeriod(1); } catch { }
        }
    }
}
