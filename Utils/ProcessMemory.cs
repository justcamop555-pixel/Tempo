using System;
using System.Runtime.InteropServices;

namespace AutoClicker.Utils
{
    /// <summary>
    /// How much memory Tempo is using, counted the way Task Manager counts it.
    ///
    /// The status bar showed <see cref="Environment.WorkingSet"/>, under a comment saying that was
    /// "the figure Task Manager calls Memory". It is not. The working set includes every page of shared
    /// code mapped into the process — Windows' and .NET's own libraries, which other programs use too —
    /// and Task Manager leaves those out: its Memory column is the PRIVATE working set. Measured on an
    /// idle Tempo: working set 157.6 MB, private working set 58.5 MB, Task Manager 58.5 MB. The readout
    /// was nearly three times what anyone checking up on Tempo would see.
    /// </summary>
    public static class ProcessMemory
    {
        /// <summary>One reading. Sizes in bytes.</summary>
        public struct Reading
        {
            /// <summary>Task Manager's "Memory": pages in RAM that belong to this process alone.</summary>
            public long PrivateWorkingSet;

            /// <summary>All pages in RAM, shared code included.</summary>
            public long WorkingSet;

            /// <summary>Private memory committed (in RAM or in the page file).</summary>
            public long PrivateBytes;

            /// <summary>
            /// True when <see cref="PrivateWorkingSet"/> was read directly. Windows before the private
            /// working set was reported here (older Windows 10) gets the committed figure instead.
            /// </summary>
            public bool Exact;
        }

        /// <summary>Reads this process's memory now. Never throws; all zeros if Windows won't say.</summary>
        public static Reading Read()
        {
            var r = new Reading();
            try
            {
                var c = new PROCESS_MEMORY_COUNTERS_EX2();
                c.cb = (uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS_EX2));
                if (GetProcessMemoryInfo(GetCurrentProcess(), ref c, c.cb))
                {
                    r.WorkingSet = (long)(ulong)c.WorkingSetSize;
                    r.PrivateBytes = (long)(ulong)c.PrivateUsage;
                    r.PrivateWorkingSet = (long)(ulong)c.PrivateWorkingSetSize;
                    r.Exact = r.PrivateWorkingSet > 0;
                }
                else
                {
                    // The EX2 layout is refused where Windows doesn't know it; the older one still works.
                    var e = new PROCESS_MEMORY_COUNTERS_EX();
                    e.cb = (uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS_EX));
                    if (GetProcessMemoryInfo(GetCurrentProcess(), ref e, e.cb))
                    {
                        r.WorkingSet = (long)(ulong)e.WorkingSetSize;
                        r.PrivateBytes = (long)(ulong)e.PrivateUsage;
                    }
                }
                if (!r.Exact)
                {
                    // The closest figure there is: private, and never more than the working set.
                    r.PrivateWorkingSet = r.WorkingSet > 0 && r.PrivateBytes > 0
                        ? Math.Min(r.PrivateBytes, r.WorkingSet)
                        : Math.Max(r.PrivateBytes, 0);
                }
            }
            catch
            {
                // Status-bar figure: a missing number beats a crash.
            }
            return r;
        }

        /// <summary>Whole megabytes (MiB, as Task Manager shows them).</summary>
        public static long Megabytes(long bytes) => bytes <= 0 ? 0 : bytes / (1024 * 1024);

        // ── Win32 ──────────────────────────────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_MEMORY_COUNTERS_EX
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_MEMORY_COUNTERS_EX2
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
            public UIntPtr PrivateWorkingSetSize;
            public ulong SharedCommitUsage;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetProcessMemoryInfo(IntPtr process, ref PROCESS_MEMORY_COUNTERS_EX2 counters, uint size);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetProcessMemoryInfo(IntPtr process, ref PROCESS_MEMORY_COUNTERS_EX counters, uint size);
    }
}
