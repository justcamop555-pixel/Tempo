using System;
using System.Diagnostics;
using System.Threading;

namespace AutoClicker.Utils
{
    /// <summary>
    /// How long Tempo's low-level hook callbacks take, measured against the deadline Windows
    /// actually judges them by.
    ///
    /// WHY THIS EXISTS. A WH_KEYBOARD_LL / WH_MOUSE_LL callback runs on the thread that installed
    /// it, and Windows gives it <c>LowLevelHooksTimeout</c> milliseconds (300 by default, in
    /// HKCU\Control Panel\Desktop) to return. Miss that deadline and the OS **removes the hook** —
    /// no error, no return code, no event. From inside the app nothing looks wrong at all: the
    /// handle is still non-zero, <c>UnhookWindowsHookEx</c> still succeeds later, and the only
    /// symptom is that keys stop arriving. In Tempo that means the keyboard-fallback hotkeys and
    /// the macro recorder's cancel key quietly stop working, and the log says nothing whatsoever.
    ///
    /// This cannot prevent that — nothing can, short of keeping the callbacks fast — but it turns
    /// an invisible failure into a measured one: every callback is timed, the worst is remembered,
    /// anything near the deadline is logged once, and Live debug shows the numbers beside the
    /// budget they are spending.
    ///
    /// The cost is one Stopwatch timestamp pair per callback (tens of nanoseconds) and no
    /// allocation; the counters are plain interlocked ints, because this runs on the hook thread
    /// and must never take a lock that a UI thread might be holding.
    /// </summary>
    public static class HookHealth
    {
        /// <summary>What Windows allows a low-level hook callback, in ms, before it drops the hook.</summary>
        public static int OsTimeoutMs { get; private set; } = ReadOsTimeout();

        /// <summary>
        /// Warn at this fraction of the budget. Half, because the measurement is of OUR work only:
        /// the OS clock starts before the callback is dispatched, so a callback that measures at
        /// half the budget may already be at the edge from the OS's point of view.
        /// </summary>
        private const double WarnFraction = 0.5;

        private static int _calls;
        private static int _slow;           // over the warn line
        private static int _overBudget;     // over the OS deadline: the hook may already be gone
        private static int _worstMs;
        private static int _lastMs;
        private static long _lastSlowTicks;
        private static string _worstHook = "";

        /// <summary>Total low-level hook callbacks Tempo has run this session.</summary>
        public static int Calls => Volatile.Read(ref _calls);

        /// <summary>How many took longer than half the OS budget.</summary>
        public static int Slow => Volatile.Read(ref _slow);

        /// <summary>How many took longer than the OS budget — each one may have cost us the hook.</summary>
        public static int OverBudget => Volatile.Read(ref _overBudget);

        /// <summary>The longest callback this session, in ms, and which hook it was.</summary>
        public static int WorstMs => Volatile.Read(ref _worstMs);

        public static string WorstHook => Volatile.Read(ref _worstHook);

        /// <summary>The most recent callback's duration in ms.</summary>
        public static int LastMs => Volatile.Read(ref _lastMs);

        /// <summary>
        /// Time a callback that started at <paramref name="startedTimestamp"/>
        /// (a <see cref="Stopwatch.GetTimestamp"/> value). Called from the hook thread, so it does
        /// no work beyond arithmetic and, rarely, one log line.
        /// </summary>
        public static void NoteCallback(string hook, long startedTimestamp)
        {
            try
            {
                long elapsed = Stopwatch.GetTimestamp() - startedTimestamp;
                int ms = (int)(elapsed * 1000 / Stopwatch.Frequency);
                Interlocked.Increment(ref _calls);
                Volatile.Write(ref _lastMs, ms);

                if (ms > Volatile.Read(ref _worstMs))
                {
                    Volatile.Write(ref _worstMs, ms);
                    Volatile.Write(ref _worstHook, hook);
                }

                int budget = OsTimeoutMs;
                if (ms < budget * WarnFraction) { return; }

                Interlocked.Increment(ref _slow);
                bool over = ms >= budget;
                if (over) { Interlocked.Increment(ref _overBudget); }

                // Rate-limited: a stall usually repeats, and a hook callback is the last place to
                // start writing a log line per keystroke.
                long now = Stopwatch.GetTimestamp();
                long last = Interlocked.Read(ref _lastSlowTicks);
                if (last != 0 && (now - last) < Stopwatch.Frequency * 10) { return; }
                Interlocked.Exchange(ref _lastSlowTicks, now);

                Logger.Warn("[Hooks] the " + hook + " hook callback took " + ms + " ms of the "
                            + budget + " ms Windows allows"
                            + (over
                               ? " — over the limit, so Windows may have removed the hook (keys would stop arriving with no other sign)."
                               : " — close to the limit; if it is exceeded Windows removes the hook silently."));
            }
            catch { /* measuring must never be the thing that breaks a hook */ }
        }

        /// <summary>One line for Live debug: what the hooks cost against what they are allowed.</summary>
        public static string Describe()
        {
            int calls = Calls;
            if (calls == 0) { return "no low-level hook callbacks yet"; }
            string s = calls + " callback(s) · last " + LastMs + " ms · worst " + WorstMs + " ms"
                       + (string.IsNullOrEmpty(WorstHook) ? "" : " (" + WorstHook + ")")
                       + " · Windows allows " + OsTimeoutMs + " ms";
            if (OverBudget > 0)
            {
                s += "  ⚠ " + OverBudget + " over the limit — Windows removes a hook that overruns, silently";
            }
            else if (Slow > 0)
            {
                s += " · " + Slow + " near the limit";
            }
            return s;
        }

        /// <summary>
        /// The OS budget. Absent from the registry means the default 300 ms — which is what
        /// Windows itself uses, so a missing value is not a problem to report.
        /// </summary>
        private static int ReadOsTimeout()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                object v = key?.GetValue("LowLevelHooksTimeout");
                if (v != null && int.TryParse(Convert.ToString(v), out int ms) && ms > 0 && ms <= 60000)
                {
                    return ms;
                }
            }
            catch { }
            return 300;
        }

        /// <summary>Test seam: re-read the budget and forget the measurements.</summary>
        internal static void ResetForTests(int? timeoutMs = null)
        {
            OsTimeoutMs = timeoutMs ?? ReadOsTimeout();
            Volatile.Write(ref _calls, 0);
            Volatile.Write(ref _slow, 0);
            Volatile.Write(ref _overBudget, 0);
            Volatile.Write(ref _worstMs, 0);
            Volatile.Write(ref _lastMs, 0);
            Volatile.Write(ref _worstHook, "");
            Interlocked.Exchange(ref _lastSlowTicks, 0);
        }
    }
}
