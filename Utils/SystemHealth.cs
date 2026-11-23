using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace AutoClicker.Utils
{
    /// <summary>The power mode Windows is actually running in (the Settings slider, Energy saver, Game Mode).</summary>
    public enum PowerMode
    {
        Unknown,
        EnergySaver,           // Battery saver / Energy saver: Windows slows everything to save power
        BestPowerEfficiency,   // the slider's left end ("Better battery")
        Balanced,
        HighPerformance,       // the legacy High performance plan
        BestPerformance,       // the slider's right end
        GameMode,
        MixedReality,
    }

    /// <summary>
    /// One reading of the machine, as Windows itself reports it. NaN or -1 where Windows had no answer.
    /// Immutable once published, so the click thread and the UI read it without a lock.
    /// </summary>
    public sealed class HealthSnapshot
    {
        public long TakenTick;                       // Environment.TickCount64 when it was taken
        public double CpuPercent = double.NaN;       // the whole machine, every core
        public double MemoryLoadPercent = double.NaN; // physical memory in use
        public double CommitPercent = double.NaN;    // committed memory against the most the commit limit can reach (RAM + page files, grown)
        public ulong PageFileGrowBytes;              // how much more the page files can still grow (SystemHealthMonitor.PageFileGrowRoom)
        public double GpuPercent = double.NaN;       // the busiest GPU engine, counted the way Task Manager counts it

        public bool OnBattery;
        public int BatteryPercent = -1;
        public bool SaverOn;                         // Battery saver / Energy saver is on
        public PowerMode Mode = PowerMode.Unknown;

        /// <summary>The app being clicked ("Roblox"), empty when there is none to watch.</summary>
        public string TargetName = "";
        /// <summary>How long it took to answer a do-nothing message, in ms; NaN when not asked or not answered.</summary>
        public double TargetAnswerMs = double.NaN;
        /// <summary>It did not answer within <see cref="SystemHealthMonitor.AnswerTimeoutMs"/>, or Windows calls it hung.</summary>
        public bool TargetNotAnswering;
        /// <summary>Windows would not let Tempo ask (an app running as administrator).</summary>
        public bool TargetUnwatchable;

        public bool HasTarget => TargetName.Length > 0;

        public bool IsFresh(long now, long maxAgeMs = 2000) => TakenTick != 0 && now - TakenTick <= maxAgeMs;
    }

    /// <summary>
    /// Watches the signals that actually come before a freeze, for Anti-Freeze and for the Clicker tab.
    ///
    /// Anti-Freeze used to watch Tempo's OWN CPU use against "80% of the machine". One click thread is one
    /// core — 6% of a 16-thread PC — so that test could never be met and the protection was, in practice, a
    /// fixed speed cap. What freezes things is the other side: the app being clicked falling behind (its
    /// window stops answering), the machine running out of CPU or memory. Those are what this reads:
    ///
    ///  - the target's window: a WM_NULL sent with SendMessageTimeout. It is answered the next time the app
    ///    checks its messages, so the round trip is how far behind its message loop is — the same test the
    ///    "Not responding" title uses, with a short timeout instead of Windows' 5 seconds;
    ///  - CPU for the whole machine (GetSystemTimes), memory load and commit (GlobalMemoryStatusEx);
    ///  - the GPU as Task Manager counts it: per engine, summed over every process, the busiest engine wins
    ///    (the "GPU Engine" counters);
    ///  - power: plugged in or on battery, Energy/Battery saver, and the effective power mode Windows reports
    ///    (PowerRegisterForEffectivePowerModeNotifications) — on battery and in the efficiency modes Windows
    ///    runs background apps on efficient cores at lower clocks.
    ///
    /// It samples only while someone holds it (a run, the Clicker tab on screen), on a thread of its own: the
    /// target's answer can take up to <see cref="AnswerTimeoutMs"/>, which must never be the click thread's wait.
    /// </summary>
    public sealed class SystemHealthMonitor : IDisposable
    {
        public const int SampleMs = 500;
        public const int AnswerTimeoutMs = 500;

        /// <summary>The one monitor: the clicker and the Clicker tab share its readings.</summary>
        public static SystemHealthMonitor Shared { get; } = new SystemHealthMonitor();

        private readonly object _sync = new object();
        private readonly HashSet<string> _owners = new HashSet<string>(StringComparer.Ordinal);
        private readonly ManualResetEvent _wake = new ManualResetEvent(false);
        private Thread _thread;
        private int _generation;   // each sampling thread runs while this still has the value it started with
        private volatile HealthSnapshot _latest = new HealthSnapshot();
        private bool _disposed;

        /// <summary>The window being clicked, asked on the monitor's thread before each sample; null or zero = none.</summary>
        public Func<IntPtr> TargetWindow { get; set; }

        /// <summary>The latest reading (never null; NaN fields until the first sample).</summary>
        public HealthSnapshot Latest => _latest;

        public bool IsSampling { get { lock (_sync) { return _thread != null; } } }

        /// <summary>For the tests: the time a sample took, in ms.</summary>
        public double LastSampleCostMs { get; private set; }

        /// <summary>Starts sampling for <paramref name="owner"/> (no-op if it already holds it).</summary>
        public void Acquire(string owner)
        {
            lock (_sync)
            {
                if (_disposed || !_owners.Add(owner) || _thread != null) { return; }
                int gen = ++_generation;
                _wake.Reset();
                _thread = new Thread(() => Loop(gen)) { IsBackground = true, Name = "Tempo.SystemHealth", Priority = ThreadPriority.BelowNormal };
                _thread.Start();
            }
        }

        /// <summary>
        /// Stops sampling once nobody holds it. Never waits for the thread: the UI calls this, and the thread
        /// may be waiting up to <see cref="AnswerTimeoutMs"/> on a hung target. It ends by itself.
        /// </summary>
        public void Release(string owner)
        {
            lock (_sync)
            {
                if (!_owners.Remove(owner) || _owners.Count > 0 || _thread == null) { return; }
                _generation++;
                _thread = null;
                _wake.Set();
            }
        }

        /// <summary>Takes a reading now, on the calling thread (the tests; the loop calls it too).</summary>
        public HealthSnapshot SampleNow()
        {
            var sw = Stopwatch.StartNew();
            var s = new HealthSnapshot();
            s.CpuPercent = _cpu.Sample();
            ReadMemory(s);
            s.GpuPercent = _gpu.Sample();
            ReadPower(s);
            IntPtr target = IntPtr.Zero;
            try { target = TargetWindow?.Invoke() ?? IntPtr.Zero; } catch { }
            if (target != IntPtr.Zero) { ProbeTarget(target, s); }
            s.TakenTick = Environment.TickCount64;
            _latest = s;
            LastSampleCostMs = sw.Elapsed.TotalMilliseconds;
            return s;
        }

        private void Loop(int gen)
        {
            while (Volatile.Read(ref _generation) == gen)
            {
                try
                {
                    // One sampling thread at a time: a released thread that is still finishing a sample must not
                    // race a newly acquired one through the samplers (their deltas are not thread-safe).
                    lock (_sampleLock) { if (Volatile.Read(ref _generation) == gen) { SampleNow(); } }
                }
                catch (Exception ex) { Logger.Swallow("SystemHealth.Sample", ex); }
                if (Volatile.Read(ref _generation) != gen) { break; }
                _wake.WaitOne(SampleMs);
            }
        }

        private readonly object _sampleLock = new object();

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) { return; }
                _disposed = true;
                _owners.Clear();
                _generation++;
                _thread = null;
                _wake.Set();
            }
            lock (_sampleLock) { _gpu.Dispose(); }
        }

        // ── CPU, the whole machine ──────────────────────────────────────────────────────────────────

        private readonly CpuSampler _cpu = new CpuSampler();

        private sealed class CpuSampler
        {
            private ulong _idle, _total;

            public double Sample()
            {
                if (!GetSystemTimes(out long idle, out long kernel, out long user)) { return double.NaN; }
                // Kernel time includes idle time.
                ulong i = (ulong)idle, t = (ulong)kernel + (ulong)user;
                ulong di = i - _idle, dt = t - _total;
                bool first = _total == 0;
                _idle = i; _total = t;
                if (first || dt == 0) { return double.NaN; }
                double busy = 100.0 * (dt - Math.Min(di, dt)) / dt;
                return Math.Max(0, Math.Min(100, busy));
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

        // ── memory ──────────────────────────────────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        /// <summary>Physical memory free and in all, in bytes — the "Available" and total Task Manager shows — or null
        /// when Windows can't say.</summary>
        public static (ulong Available, ulong Total)? PhysicalMemory()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref m) ? (m.ullAvailPhys, m.ullTotalPhys) : ((ulong, ulong)?)null;
        }

        /// <summary>
        /// Commit room, in bytes: how much more memory programs may still be promised (the commit limit — RAM plus page
        /// files — less the commit charge), and the limit itself; null when Windows can't say. Running OUT of this is what
        /// makes a program's next allocation fail and the program crash; a low physical "Available" only means paging.
        /// </summary>
        public static (ulong Available, ulong Limit)? CommitRoom()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref m) ? (m.ullAvailPageFile, m.ullTotalPageFile) : ((ulong, ulong)?)null;
        }

        private static void ReadMemory(HealthSnapshot s)
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref m)) { return; }
            s.MemoryLoadPercent = m.dwMemoryLoad;
            // "PageFile" here is the commit limit (RAM + page files), and its use is the commit charge — counted against
            // the limit the page files can still grow to, which is where an allocation would really fail.
            if (m.ullTotalPageFile > 0)
            {
                ulong grow = PageFileGrowRoom();
                s.PageFileGrowBytes = grow;
                double used = m.ullTotalPageFile - Math.Min(m.ullAvailPageFile, m.ullTotalPageFile);
                s.CommitPercent = 100.0 * used / ((double)m.ullTotalPageFile + grow);
            }
        }

        // ── page files: how far the commit limit can still grow ─────────────────────────────────────

        private static readonly object _growLock = new object();
        private static long _growReadTick;
        private static ulong _growRoom;
        private static string _growHow = "";

        /// <summary>
        /// How much more Windows can still add to the page files, in bytes. A system-managed page file grows "when the system
        /// commit charge is more than 90 percent of the system commit limit", up to "3 × RAM or 4 GB, whichever is larger",
        /// "limited to the volume size ÷ 8" (Microsoft Learn: "How to determine the appropriate page file size for 64-bit
        /// versions of Windows"); a custom size grows up to its maximum; both stop at the disk's free space. So the commit
        /// limit GlobalMemoryStatusEx reports is not the ceiling, and "1 GB of commit left" on a PC whose page file can still
        /// grow by 20 GB is nowhere near an allocation failing — the "almost out of memory" warnings while everything ran
        /// fine (2026-11-14). Read from Windows' own setting (Memory Management\PagingFiles) and the page files on disk;
        /// kept for a minute.
        /// </summary>
        public static ulong PageFileGrowRoom() => PageFileGrowth().Room;

        /// <summary><see cref="PageFileGrowRoom"/> and how it was worked out, drive by drive (Live debug, the log).</summary>
        public static (ulong Room, string How) PageFileGrowth()
        {
            long now = Environment.TickCount64;
            lock (_growLock)
            {
                if (_growReadTick != 0 && now - _growReadTick < 60000) { return (_growRoom, _growHow); }
                ulong room;
                string how;
                try { room = ReadPageFileGrowRoom(out how); }
                catch (Exception ex) { room = 0; how = "not known (" + ex.GetType().Name + ")"; }
                _growRoom = room;
                _growHow = how;
                _growReadTick = now;
                return (room, how);
            }
        }

        private static ulong ReadPageFileGrowRoom(out string how)
        {
            const ulong MB = 1024UL * 1024, GB = 1024 * MB;
            string[] setting = null, existing = null;
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"))
            {
                setting = key?.GetValue("PagingFiles") as string[];
                existing = key?.GetValue("ExistingPageFiles") as string[];
            }
            if (setting == null || setting.Length == 0) { how = "no page file is set"; return 0; }
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            bool haveMemory = GlobalMemoryStatusEx(ref m);
            ulong ram = haveMemory ? m.ullTotalPhys : 0;

            // Each page file: its path, and its sizes in MB (0 0, or none, = system-managed).
            var files = new List<(string Path, bool SystemManaged, ulong MaxMb)>();
            foreach (string raw in setting)
            {
                string[] bits = (raw ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (bits.Length == 0) { continue; }
                ulong initialMb = 0, maxMb = 0;
                bool sized = bits.Length >= 3 && ulong.TryParse(bits[1], out initialMb) && ulong.TryParse(bits[2], out maxMb);
                bool systemManaged = !sized || (initialMb == 0 && maxMb == 0);
                if (bits[0].StartsWith("?:", StringComparison.Ordinal))
                {
                    // "Automatically manage paging file size for all drives": the page files Windows made this boot
                    // ("\??\C:\pagefile.sys"), or the system drive's.
                    bool any = false;
                    foreach (string e in existing ?? new string[0])
                    {
                        string p = (e ?? "").Trim();
                        if (p.StartsWith(@"\??\", StringComparison.Ordinal)) { p = p.Substring(4); }
                        if (p.Length >= 3 && p[1] == ':') { files.Add((p, true, 0)); any = true; }
                    }
                    if (!any)
                    {
                        string sys = Environment.GetEnvironmentVariable("SystemDrive");
                        files.Add(((string.IsNullOrEmpty(sys) ? "C:" : sys.TrimEnd('\\')) + bits[0].Substring(2), true, 0));
                    }
                    continue;
                }
                files.Add((bits[0], systemManaged, maxMb));
            }

            ulong total = 0;
            var parts = new List<string>();
            foreach (var f in files)
            {
                string root;
                try { root = System.IO.Path.GetPathRoot(f.Path); } catch { continue; }
                if (string.IsNullOrEmpty(root)) { continue; }
                var drive = new System.IO.DriveInfo(root);
                if (!drive.IsReady) { continue; }
                ulong volume = (ulong)Math.Max(0L, drive.TotalSize);
                ulong free = (ulong)Math.Max(0L, drive.AvailableFreeSpace);
                ulong current = 0;
                try { var info = new System.IO.FileInfo(f.Path); if (info.Exists) { current = (ulong)info.Length; } } catch { }
                // Unreadable with one page file in all: the commit limit beyond RAM is that file.
                if (current == 0 && files.Count == 1 && haveMemory && m.ullTotalPageFile > ram) { current = m.ullTotalPageFile - ram; }
                ulong max = f.SystemManaged ? Math.Min(Math.Max(3 * ram, 4 * GB), volume / 8) : f.MaxMb * MB;
                ulong room = max > current ? max - current : 0;
                // Never past what the disk holds, with a little kept back.
                ulong diskRoom = free > 512 * MB ? free - 512 * MB : 0;
                room = Math.Min(room, diskRoom);
                total += room;
                parts.Add(root.TrimEnd('\\') + " " + (f.SystemManaged ? "system-managed" : "up to " + f.MaxMb + " MB")
                          + ", " + current / MB + " MB now, can grow " + room / MB + " MB");
            }
            how = parts.Count == 0 ? "no page file found" : string.Join("; ", parts);
            return total;
        }

        // ── GPU, as Task Manager counts it ──────────────────────────────────────────────────────────

        private readonly GpuSampler _gpu = new GpuSampler();

        /// <summary>
        /// "\GPU Engine(*)\Utilization Percentage": one instance per process per engine
        /// ("pid_1124_luid_0x0_0xD1C3_phys_0_eng_0_engtype_3D"). Task Manager sums each engine over every process
        /// and shows the busiest engine — averaging would make a saturated 3D engine look idle on a GPU with a
        /// dozen engines. Rate counters need two collections, so the first sample has no value.
        /// </summary>
        private sealed class GpuSampler : IDisposable
        {
            private IntPtr _query, _counter;
            private bool _failed, _primed;
            private readonly Dictionary<string, double> _perEngine = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            public double Sample()
            {
                if (_failed) { return double.NaN; }
                try
                {
                    if (_query == IntPtr.Zero)
                    {
                        if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0 ||
                            PdhAddEnglishCounterW(_query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _counter) != 0)
                        {
                            _failed = true;   // no WDDM 2 driver, counters off: no GPU reading, nothing else changes
                            return double.NaN;
                        }
                    }
                    if (PdhCollectQueryData(_query) != 0) { return double.NaN; }
                    if (!_primed) { _primed = true; return double.NaN; }
                    return ReadBusiestEngine();
                }
                catch (DllNotFoundException) { _failed = true; return double.NaN; }
                catch (EntryPointNotFoundException) { _failed = true; return double.NaN; }
            }

            private double ReadBusiestEngine()
            {
                uint size = 0;
                uint status = PdhGetFormattedCounterArrayW(_counter, PDH_FMT_DOUBLE, ref size, out uint count, IntPtr.Zero);
                if (status != PDH_MORE_DATA || size == 0) { return count == 0 && status == 0 ? 0 : double.NaN; }
                IntPtr buf = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (PdhGetFormattedCounterArrayW(_counter, PDH_FMT_DOUBLE, ref size, out count, buf) != 0) { return double.NaN; }
                    _perEngine.Clear();
                    // PDH_FMT_COUNTERVALUE_ITEM_W: a name pointer, then { DWORD CStatus; union { …; double } } at +8.
                    int item = IntPtr.Size + 16;
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr at = buf + i * item;
                        string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(at)) ?? "";
                        int cstatus = Marshal.ReadInt32(at + IntPtr.Size);
                        if (cstatus != 0 && cstatus != 1) { continue; }   // PDH_CSTATUS_VALID_DATA / NEW_DATA
                        double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(at + IntPtr.Size + 8));
                        if (double.IsNaN(v) || v <= 0) { continue; }
                        int luid = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
                        string engine = luid >= 0 ? name.Substring(luid) : name;   // the engine, whatever the process
                        _perEngine.TryGetValue(engine, out double sum);
                        _perEngine[engine] = sum + v;
                    }
                    double busiest = 0;
                    foreach (double v in _perEngine.Values) { if (v > busiest) { busiest = v; } }
                    return Math.Min(100, busiest);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }

            public void Dispose()
            {
                if (_query != IntPtr.Zero) { try { PdhCloseQuery(_query); } catch { } _query = IntPtr.Zero; }
            }

            private const uint PDH_FMT_DOUBLE = 0x00000200;
            private const uint PDH_MORE_DATA = 0x800007D2;

            [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
            private static extern uint PdhOpenQueryW(string dataSource, IntPtr userData, out IntPtr query);
            [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
            private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);
            [DllImport("pdh.dll")]
            private static extern uint PdhCollectQueryData(IntPtr query);
            [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
            private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);
            [DllImport("pdh.dll")]
            private static extern uint PdhCloseQuery(IntPtr query);
        }

        // ── power ───────────────────────────────────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
            public int BatteryLifeTime, BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        private delegate void EffectivePowerModeCallback(int mode, IntPtr context);

        [DllImport("powrprof.dll")]
        private static extern int PowerRegisterForEffectivePowerModeNotifications(uint version, EffectivePowerModeCallback callback,
                                                                                 IntPtr context, out IntPtr registration);

        private static readonly object PowerSync = new object();
        private static EffectivePowerModeCallback s_powerCallback;   // kept alive: Windows calls it for the life of the process
        private static IntPtr s_powerRegistration;
        private static bool s_powerTried;
        private static volatile int s_effectiveMode = -1;

        /// <summary>
        /// Asks Windows, once, to say whenever the effective power mode changes (Windows 10 1903+: version 2 adds
        /// Game Mode). It answers at once with the current mode, then on every change — no polling. Older Windows
        /// has no such function and the mode reads Unknown.
        /// </summary>
        private static void EnsurePowerModeNotifications()
        {
            lock (PowerSync)
            {
                if (s_powerTried) { return; }
                s_powerTried = true;
                try
                {
                    s_powerCallback = (mode, ctx) => s_effectiveMode = mode;
                    if (PowerRegisterForEffectivePowerModeNotifications(2, s_powerCallback, IntPtr.Zero, out s_powerRegistration) != 0 &&
                        PowerRegisterForEffectivePowerModeNotifications(1, s_powerCallback, IntPtr.Zero, out s_powerRegistration) != 0)
                    {
                        s_powerRegistration = IntPtr.Zero;
                    }
                }
                catch (EntryPointNotFoundException) { }
                catch (DllNotFoundException) { }
            }
        }

        private static void ReadPower(HealthSnapshot s)
        {
            EnsurePowerModeNotifications();
            if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS p))
            {
                // BatteryFlag is a set of bits: 128 = no system battery, 255 = unknown.
                bool hasBattery = p.BatteryFlag != 255 && (p.BatteryFlag & 128) == 0;
                s.OnBattery = hasBattery && p.ACLineStatus == 0;
                s.BatteryPercent = hasBattery && p.BatteryLifePercent <= 100 ? p.BatteryLifePercent : -1;
                s.SaverOn = (p.SystemStatusFlag & 1) != 0;
            }
            s.Mode = ModeFrom(s_effectiveMode, s.SaverOn);
        }

        /// <summary>EFFECTIVE_POWER_MODE (powersetting.h) in Windows' own words; later builds add energy-saver values past 6.</summary>
        internal static PowerMode ModeFrom(int effective, bool saverOn)
        {
            if (saverOn) { return PowerMode.EnergySaver; }
            switch (effective)
            {
                case 0: return PowerMode.EnergySaver;
                case 1: return PowerMode.BestPowerEfficiency;
                case 2: return PowerMode.Balanced;
                case 3: return PowerMode.HighPerformance;
                case 4: return PowerMode.BestPerformance;
                case 5: return PowerMode.GameMode;
                case 6: return PowerMode.MixedReality;
                default: return effective > 6 ? PowerMode.EnergySaver : PowerMode.Unknown;
            }
        }

        // ── the app being clicked ───────────────────────────────────────────────────────────────────

        private const uint WM_NULL = 0x0000;
        private const uint SMTO_ABORTIFHUNG = 0x0002, SMTO_ERRORONEXIT = 0x0020;
        private const int ERROR_TIMEOUT = 1460, ERROR_ACCESS_DENIED = 5;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeoutW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")]
        private static extern bool IsHungAppWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder name, int maxCount);
        // Not in the SDK headers, but in user32 since Vista; only ever used for a name, and missing is fine.
        [DllImport("user32.dll")]
        private static extern IntPtr HungWindowFromGhostWindow(IntPtr ghost);

        private static string s_lastTargetName = "";

        private static void ProbeTarget(IntPtr hwnd, HealthSnapshot s)
        {
            IntPtr root = GetAncestor(hwnd, 2 /* GA_ROOT */);
            if (root == IntPtr.Zero) { root = hwnd; }
            if (!IsWindow(root)) { return; }

            // An app that has not answered for 5 s is covered by a "Ghost": the window manager's stand-in, with
            // its picture and "(Not Responding)" title. The ghost belongs to Windows, answers for it at once and
            // drops every click — and it is what the click point (or the focus) now finds. So a ghost IS the verdict.
            if (IsGhost(root))
            {
                string hungName = "";
                try
                {
                    IntPtr hung = HungWindowFromGhostWindow(root);
                    if (hung != IntPtr.Zero)
                    {
                        GetWindowThreadProcessId(hung, out int hungPid);
                        hungName = AppName(hungPid);
                    }
                }
                catch (EntryPointNotFoundException) { }
                s.TargetName = hungName.Length > 0 ? hungName : (s_lastTargetName.Length > 0 ? s_lastTargetName : "?");
                s.TargetNotAnswering = true;
                return;
            }

            GetWindowThreadProcessId(root, out int pid);
            s.TargetName = AppName(pid);
            if (s.TargetName.Length == 0) { s.TargetName = "?"; }
            s_lastTargetName = s.TargetName;

            // Windows' own verdict first: a window it already calls hung ("Not responding", 5 s) is answered
            // at once by SMTO_ABORTIFHUNG, as a failure.
            var sw = Stopwatch.StartNew();
            IntPtr ok = SendMessageTimeoutW(root, WM_NULL, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG | SMTO_ERRORONEXIT,
                                            AnswerTimeoutMs, out _);
            double ms = sw.Elapsed.TotalMilliseconds;
            if (ok != IntPtr.Zero)
            {
                s.TargetAnswerMs = ms;
                return;
            }
            int err = Marshal.GetLastWin32Error();
            if (err == ERROR_ACCESS_DENIED) { s.TargetUnwatchable = true; return; }
            if (err == ERROR_TIMEOUT || IsHungAppWindow(root)) { s.TargetNotAnswering = true; }
        }

        internal static bool IsGhost(IntPtr window)
        {
            var cls = new System.Text.StringBuilder(16);
            return GetClassNameW(window, cls, cls.Capacity) == 5 && cls.ToString() == "Ghost";
        }

        private static readonly Dictionary<int, string> s_names = new Dictionary<int, string>();
        private static readonly int s_ownPid = Process.GetCurrentProcess().Id;

        /// <summary>A readable name for a process: its file description ("Roblox"), else its file name. Cached.</summary>
        internal static string AppName(int pid)
        {
            if (pid <= 0) { return ""; }
            if (pid == s_ownPid) { return "Tempo"; }
            lock (s_names)
            {
                if (s_names.TryGetValue(pid, out string known)) { return known; }
            }
            string name = "";
            try
            {
                string exe = ImagePath(pid);
                if (exe.Length > 0)
                {
                    try { name = (FileVersionInfo.GetVersionInfo(exe).FileDescription ?? "").Trim(); } catch { }
                    if (name.Length == 0 || name.Length > 40) { name = System.IO.Path.GetFileNameWithoutExtension(exe); }
                }
                else
                {
                    using (var p = Process.GetProcessById(pid)) { name = p.ProcessName; }
                }
            }
            catch { }
            lock (s_names)
            {
                if (s_names.Count > 64) { s_names.Clear(); }
                s_names[pid] = name;
            }
            return name;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, System.Text.StringBuilder name, ref int size);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>The program's path (PROCESS_QUERY_LIMITED_INFORMATION is enough, even for an elevated app).</summary>
        private static string ImagePath(int pid)
        {
            IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) { return ""; }
            try
            {
                var sb = new System.Text.StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString(0, size) : "";
            }
            finally { CloseHandle(h); }
        }
    }
}
