using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Watches the screen for the first seconds of a caption start and writes ONE line about it: whether the screen's
    /// compositor (DWM) stopped answering, for how long, during which start-up step, and how much of the graphics card's
    /// memory Windows let Tempo have just before.
    ///
    /// Round 30 (2026-11-16): the user's "when it starts, the entire windows freeze for short seconds". Their log had DWM
    /// not answering for 1145 ms while the speech model (1.5 GB) was loading onto the GPU; loading the same model in a test
    /// with plenty of free video memory never held the screen past 15 ms. Animations' own check (Motion.FramePacer) only
    /// notices a stall while something animates, and says it once a run — this looks at every start, animating or not.
    /// </summary>
    public static class ScreenStallWatch
    {
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();

        /// <summary>A wait for the next frame this long is a stall a person sees.</summary>
        private const double StallMs = 250;

        private static int _running;

        /// <summary>What the last watch found, as its log line says it (without the tag); null before the first. For Live debug.</summary>
        public static volatile string LastResult;

        /// <summary>When the last watch ended (UTC); default before the first.</summary>
        public static DateTime LastResultUtc;

        /// <summary>True while a watch runs.</summary>
        public static bool Watching => Volatile.Read(ref _running) != 0;

        /// <summary>
        /// Starts watching for <paramref name="seconds"/> on a background thread (one watch at a time). <paramref name="step"/>
        /// says what the start is doing (read when a stall ends, so it names the step it happened in).
        /// </summary>
        public static void Watch(int seconds, Func<string> step)
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) { return; }
            string vramBefore = VideoMemoryText();
            var t = new Thread(() =>
            {
                try { Run(seconds, step, vramBefore); }
                catch (Exception ex) { Logger.Swallow("ScreenStallWatch", ex); }
                finally { Interlocked.Exchange(ref _running, 0); }
            })
            {
                IsBackground = true,
                Name = "Tempo screen stall watch",
                Priority = ThreadPriority.AboveNormal,
            };
            t.Start();
        }

        private static void Run(int seconds, Func<string> step, string vramBefore)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            double last = 0, worst = 0, worstAt = 0, stalledMs = 0;
            string worstStep = null;
            int stalls = 0, frames = 0;
            // A full-screen game in front (game mode) can own the display: the compositor then has nothing to compose, and
            // a long wait for its next frame is not Windows freezing — said so, and not a warning.
            bool worstInGame = false;
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                if (DwmFlush() != 0)
                {
                    // No composition to wait for (a display asleep, a remote session): nothing to measure.
                    Thread.Sleep(50);
                    last = clock.Elapsed.TotalMilliseconds;
                    continue;
                }
                double now = clock.Elapsed.TotalMilliseconds;
                double gap = now - last;
                last = now;
                frames++;
                if (gap >= StallMs)
                {
                    stalls++;
                    stalledMs += gap;
                    if (gap > worst)
                    {
                        worst = gap;
                        worstAt = (now - gap) / 1000.0;
                        try { worstStep = step?.Invoke(); } catch { worstStep = null; }
                        worstInGame = TempoTranscriber.LowImpactMode;
                    }
                }
                else if (gap > worst && stalls == 0)
                {
                    worst = gap;
                }
            }
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string result;
            if (stalls == 0)
            {
                result = "start-up: the screen kept up (" + frames + " frames in " + seconds
                         + " s, longest wait " + worst.ToString("0", ci) + " ms)" + (vramBefore != null ? " · " + vramBefore : "") + ".";
                Logger.Info("[Captions] " + result);
            }
            else
            {
                result = "start-up: the screen's compositor (DWM) stopped answering for " + worst.ToString("0", ci)
                         + " ms at +" + worstAt.ToString("0.0", ci) + " s, during " + (worstStep ?? "?")
                         + (stalls > 1 ? " (" + stalls + " stalls, " + stalledMs.ToString("0", ci) + " ms in all)" : "")
                         + (vramBefore != null ? " · " + vramBefore : "")
                         + (worstInGame ? " · a full-screen game was in front: it may own the display, so this need not be a freeze" : "") + ".";
                if (worstInGame) { Logger.Info("[Captions] " + result); }
                else { Logger.Warn("[Captions] " + result); }
            }
            LastResultUtc = DateTime.UtcNow;
            LastResult = result;
        }

        // ── Video memory (DXGI): what Windows lets this process have on the first graphics card, and its own use ──────

        [StructLayout(LayoutKind.Sequential)]
        private struct VideoMemoryInfo
        {
            public ulong Budget, CurrentUsage, AvailableForReservation, CurrentReservation;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumAdapters1Fn(IntPtr self, uint index, out IntPtr adapter);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryVideoMemoryInfoFn(IntPtr self, uint node, int group, out VideoMemoryInfo info);

        [DllImport("dxgi.dll")]
        private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

        private static readonly Guid IidFactory1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        private static readonly Guid IidAdapter3 = new Guid("645967a4-1392-4310-a798-8053ce3e93fd");

        /// <summary>"video memory: Windows gives Tempo 6.1 GB, Tempo uses 0.2 GB" for the first adapter, or null.</summary>
        internal static string VideoMemoryText()
        {
            IntPtr factory = IntPtr.Zero, adapter = IntPtr.Zero, adapter3 = IntPtr.Zero;
            try
            {
                Guid iid = IidFactory1;
                if (CreateDXGIFactory1(ref iid, out factory) != 0 || factory == IntPtr.Zero) { return null; }
                // IDXGIFactory1::EnumAdapters1 — slot 12 (IUnknown 3, IDXGIObject 4, IDXGIFactory 5).
                var enumAdapters = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Fn>(Slot(factory, 12));
                if (enumAdapters(factory, 0, out adapter) != 0 || adapter == IntPtr.Zero) { return null; }
                Guid iid3 = IidAdapter3;
                if (Marshal.QueryInterface(adapter, ref iid3, out adapter3) != 0 || adapter3 == IntPtr.Zero) { return null; }
                // IDXGIAdapter3::QueryVideoMemoryInfo — slot 14 (IUnknown 3, IDXGIObject 4, IDXGIAdapter 3, IDXGIAdapter1 1,
                // IDXGIAdapter2 1, then the two content-protection calls).
                var query = Marshal.GetDelegateForFunctionPointer<QueryVideoMemoryInfoFn>(Slot(adapter3, 14));
                if (query(adapter3, 0, 0 /* DXGI_MEMORY_SEGMENT_GROUP_LOCAL */, out VideoMemoryInfo info) != 0) { return null; }
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                return "video memory: Windows gives Tempo " + (info.Budget / 1073741824.0).ToString("0.0", ci) + " GB, Tempo uses "
                       + (info.CurrentUsage / 1073741824.0).ToString("0.0", ci) + " GB";
            }
            catch { return null; }
            finally
            {
                if (adapter3 != IntPtr.Zero) { Marshal.Release(adapter3); }
                if (adapter != IntPtr.Zero) { Marshal.Release(adapter); }
                if (factory != IntPtr.Zero) { Marshal.Release(factory); }
            }
        }

        private static IntPtr Slot(IntPtr comObject, int index)
        {
            IntPtr vtable = Marshal.ReadIntPtr(comObject);
            return Marshal.ReadIntPtr(vtable, index * IntPtr.Size);
        }
    }
}
