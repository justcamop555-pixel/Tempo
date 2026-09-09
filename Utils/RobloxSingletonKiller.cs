using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Enables multiple Roblox clients by CLOSING the running client's own single-instance handle
    /// instead of trying to hold one ourselves.
    ///
    /// Holding <c>ROBLOX_singletonEvent</c>/<c>ROBLOX_singletonMutex</c> ourselves is the old method
    /// and it is dead on the current (Hyperion) client — proven in this project's logs: we owned both
    /// objects and the second client still folded. The client makes its OWN singleton object and
    /// checks that; the only thing that helps is to make its object go away. So a background thread
    /// enumerates every running Roblox process's handles (<c>NtQuerySystemInformation</c>), finds the
    /// one named <c>ROBLOX_singleton*</c>, and closes it from outside with
    /// <c>DuplicateHandle(DUPLICATE_CLOSE_SOURCE)</c>. Do that continuously and each new client sees
    /// "no singleton" and launches.
    ///
    /// It does NOT inject into or modify Roblox — it only manipulates an OS handle, the same as the
    /// established open-source launchers. It is still against Roblox's rules (multi-instancing) and
    /// carries ban risk, which the UI warns about. The one thing that can defeat it: if Hyperion
    /// blocks external <c>PROCESS_DUP_HANDLE</c> access to the client, OpenProcess fails and there is
    /// nothing more a non-injecting tool can do — the log says exactly that when it happens.
    /// </summary>
    public static class RobloxSingletonKiller
    {
        private static Thread _thread;
        private static volatile bool _running;
        private static readonly object _gate = new object();

        // Log a given diagnosis only once per process id, so the log doesn't flood every tick.
        private static readonly HashSet<int> _openFailLogged = new HashSet<int>();
        private static readonly HashSet<int> _closedLogged = new HashSet<int>();

        public static bool IsRunning => _running;

        /// <summary>What the closer is doing right now, so the UI can show a live status dot.</summary>
        public enum KillerStatus
        {
            Off,        // not running
            Waiting,    // running, but no Roblox client is up yet — nothing to close
            Active,     // running AND managing at least one live Roblox client (multi-instance is working)
            Blocked     // Roblox is up but we can't open it (anti-cheat blocking) — multi-instance can't work
        }
        private static volatile KillerStatus _statusValue = KillerStatus.Off;
        private static volatile int _activeCount;

        /// <summary>Live status of the closer (Off whenever it isn't running).</summary>
        public static KillerStatus Status => _running ? _statusValue : KillerStatus.Off;

        /// <summary>How many running Roblox clients the closer is currently able to manage.</summary>
        public static int ActiveClientCount => _running ? _activeCount : 0;

        public static void Start()
        {
            lock (_gate)
            {
                if (_running) { return; }
                _running = true;
                _statusValue = KillerStatus.Waiting;   // "on — scanning for Roblox" until the first sweep
                _activeCount = 0;
                _openFailLogged.Clear();
                _closedLogged.Clear();
                _thread = new Thread(Loop) { IsBackground = true, Name = "RobloxSingletonKiller" };
                _thread.Start();
                Logger.Info("[Roblox] multi-instance: singleton-handle closer started.");
            }
        }

        public static void Stop()
        {
            lock (_gate)
            {
                if (!_running) { return; }
                _running = false;
                _statusValue = KillerStatus.Off;
                _activeCount = 0;
            }
            Logger.Info("[Roblox] multi-instance: singleton-handle closer stopped.");
        }

        private static void Loop()
        {
            // Resolve the object-type indices for Event and Mutant, so we only ever ask for the NAME
            // of handles of those types — querying a pipe/file handle's name can hang forever. A
            // one-shot resolve at thread start can miss on a transient handle-snapshot failure, and
            // (0,0) makes the type filter match nothing, leaving the closer silently inert. Retry a
            // few times, and if it still can't resolve, say so instead of pretending to work.
            (ushort evt, ushort mut) types = (0, 0);
            for (int attempt = 0; attempt < 5 && _running; attempt++)
            {
                types = ResolveTypeIndices();
                if (types.evt != 0 && types.mut != 0) { break; }
                for (int i = 0; i < 5 && _running; i++) { Thread.Sleep(100); }
            }
            if (types.evt == 0 || types.mut == 0)
            {
                if (_running)
                {
                    Logger.Warn("[Roblox] multi-instance: could not resolve the Event/Mutant object types; "
                        + "the singleton-handle closer cannot run and multi-instance will not work.");
                }
                // Mark ourselves stopped so IsRunning is honest and a later Start() actually restarts
                // (Start() is a no-op while _running is true). Only touch it if we're still the active
                // thread — a newer Start() may have replaced us, and then the flag is its to own.
                lock (_gate) { if (_thread == Thread.CurrentThread) { _running = false; } }
                return;
            }

            while (_running)
            {
                try { Sweep(types.evt, types.mut); }
                catch (Exception ex) { Logger.Swallow("RobloxSingletonKiller.Sweep", ex); }
                for (int i = 0; i < 7 && _running; i++) { Thread.Sleep(100); }   // ~700ms, responsive to Stop
            }
        }

        private static void Sweep(ushort eventType, ushort mutantType)
        {
            var robloxPids = new HashSet<int>();
            foreach (Process p in Process.GetProcessesByName("RobloxPlayerBeta"))
            {
                robloxPids.Add(p.Id);
                p.Dispose();
            }
            if (robloxPids.Count == 0)
            {
                _activeCount = 0;
                _statusValue = KillerStatus.Waiting;   // running, nothing to do yet
                return;
            }

            SweepPids(robloxPids, eventType, mutantType, out int accessible);
            _activeCount = accessible;
            // We're "active" the moment we can open a running client — its singleton may already be
            // closed from a prior sweep, so don't require a close THIS pass. If we can't open any live
            // client at all, the anti-cheat is blocking us and multi-instance cannot work.
            _statusValue = accessible > 0 ? KillerStatus.Active : KillerStatus.Blocked;
        }

        /// <summary>Test hook: one sweep limited to a single pid. Returns how many handles were closed.</summary>
        internal static int SweepOnceForTest(int pid)
        {
            (ushort evt, ushort mut) = ResolveTypeIndices();
            return SweepPids(new HashSet<int> { pid }, evt, mut, out _);
        }

        /// <summary>Test hook: how many of the given pid's processes we could open — the "active" count.</summary>
        internal static int AccessibleCountForTest(int pid)
        {
            (ushort evt, ushort mut) = ResolveTypeIndices();
            SweepPids(new HashSet<int> { pid }, evt, mut, out int accessible);
            return accessible;
        }

        private static int SweepPids(HashSet<int> robloxPids, ushort eventType, ushort mutantType, out int accessiblePids)
        {
            accessiblePids = 0;
            int closed = 0;
            IntPtr info = QueryAllHandles(out int count);
            if (info == IntPtr.Zero) { return 0; }
            try
            {
                IntPtr self = GetCurrentProcess();
                var procHandles = new Dictionary<int, IntPtr>();
                try
                {
                    const int HeaderSize = 16;    // NumberOfHandles + Reserved (x64)
                    const int EntrySize = 40;     // SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX (x64)
                    for (int i = 0; i < count; i++)
                    {
                        long baseOff = HeaderSize + (long)i * EntrySize;
                        int pid = (int)Marshal.ReadIntPtr(info, (int)(baseOff + 8));   // UniqueProcessId
                        if (!robloxPids.Contains(pid)) { continue; }

                        ushort typeIndex = (ushort)Marshal.ReadInt16(info, (int)(baseOff + 30));
                        if (typeIndex != eventType && typeIndex != mutantType) { continue; }

                        IntPtr handleValue = Marshal.ReadIntPtr(info, (int)(baseOff + 16));

                        IntPtr proc = OpenRoblox(pid, procHandles);
                        if (proc == IntPtr.Zero) { continue; }

                        // Duplicate into us to read the name (does not disturb the source).
                        if (!DuplicateHandle(proc, handleValue, self, out IntPtr dup, 0, false, DUPLICATE_SAME_ACCESS))
                        {
                            continue;
                        }
                        string name = GetObjectName(dup);
                        CloseHandle(dup);

                        if (name != null && name.IndexOf("ROBLOX_singleton", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // Close the SOURCE handle inside Roblox — this is what frees the singleton.
                            if (DuplicateHandle(proc, handleValue, self, out IntPtr closer, 0, false, DUPLICATE_CLOSE_SOURCE))
                            {
                                CloseHandle(closer);
                                closed++;
                                if (_closedLogged.Add(pid))
                                {
                                    Logger.Info("[Roblox] multi-instance: closed the singleton handle in Roblox PID " + pid + ".");
                                }
                            }
                        }
                    }
                }
                finally
                {
                    // A non-null process handle means we successfully opened that Roblox client —
                    // i.e. we can manage it. That count drives the "active" status.
                    foreach (IntPtr h in procHandles.Values)
                    {
                        if (h != IntPtr.Zero) { accessiblePids++; CloseHandle(h); }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(info);
            }
            return closed;
        }

        private static IntPtr OpenRoblox(int pid, Dictionary<int, IntPtr> cache)
        {
            if (cache.TryGetValue(pid, out IntPtr h)) { return h; }
            IntPtr proc = OpenProcess(PROCESS_DUP_HANDLE, false, pid);
            if (proc == IntPtr.Zero && _openFailLogged.Add(pid))
            {
                Logger.Warn("[Roblox] multi-instance: cannot open Roblox PID " + pid + " (err "
                    + Marshal.GetLastWin32Error() + "). If this persists, Hyperion is blocking handle "
                    + "access and no non-injecting tool can multi-instance this client.");
            }
            cache[pid] = proc;
            return proc;
        }

        /// <summary>Find the type indices for Event and Mutant by inspecting handles we own.</summary>
        private static (ushort evt, ushort mut) ResolveTypeIndices()
        {
            ushort evt = 0, mut = 0;
            IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
            IntPtr hMutex = CreateMutex(IntPtr.Zero, false, null);
            try
            {
                IntPtr info = QueryAllHandles(out int count);
                if (info != IntPtr.Zero)
                {
                    try
                    {
                        int me = GetCurrentProcessId();
                        const int HeaderSize = 16, EntrySize = 40;
                        for (int i = 0; i < count; i++)
                        {
                            long baseOff = HeaderSize + (long)i * EntrySize;
                            int pid = (int)Marshal.ReadIntPtr(info, (int)(baseOff + 8));
                            if (pid != me) { continue; }
                            IntPtr handleValue = Marshal.ReadIntPtr(info, (int)(baseOff + 16));
                            ushort typeIndex = (ushort)Marshal.ReadInt16(info, (int)(baseOff + 30));
                            if (handleValue == hEvent) { evt = typeIndex; }
                            else if (handleValue == hMutex) { mut = typeIndex; }
                        }
                    }
                    finally { Marshal.FreeHGlobal(info); }
                }
            }
            catch (Exception ex) { Logger.Swallow("RobloxSingletonKiller.ResolveTypeIndices", ex); }
            finally
            {
                if (hEvent != IntPtr.Zero) { CloseHandle(hEvent); }
                if (hMutex != IntPtr.Zero) { CloseHandle(hMutex); }
            }
            Logger.Info("[Roblox] multi-instance: object types resolved (event=" + evt + ", mutant=" + mut + ").");
            return (evt, mut);
        }

        /// <summary>Snapshot every handle on the system. Caller frees the returned buffer.</summary>
        private static IntPtr QueryAllHandles(out int count)
        {
            count = 0;
            int len = 1 << 20;   // 1 MB to start; grow on mismatch
            IntPtr buffer = Marshal.AllocHGlobal(len);
            for (int attempt = 0; attempt < 12; attempt++)
            {
                int status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, len, out int needed);
                if (status == STATUS_INFO_LENGTH_MISMATCH || (uint)status == 0xC0000004)
                {
                    Marshal.FreeHGlobal(buffer);
                    len = Math.Max(needed, len * 2);
                    buffer = Marshal.AllocHGlobal(len);
                    continue;
                }
                if (status != 0)
                {
                    Marshal.FreeHGlobal(buffer);
                    return IntPtr.Zero;
                }
                count = (int)Marshal.ReadInt64(buffer, 0);   // NumberOfHandles
                return buffer;
            }
            Marshal.FreeHGlobal(buffer);
            return IntPtr.Zero;
        }

        private static string GetObjectName(IntPtr handle)
        {
            int len = 2048;
            IntPtr buf = Marshal.AllocHGlobal(len);
            try
            {
                int status = NtQueryObject(handle, ObjectNameInformation, buf, len, out int needed);
                if ((uint)status == 0xC0000004 && needed > 0 && needed < (1 << 20))
                {
                    Marshal.FreeHGlobal(buf);
                    len = needed;
                    buf = Marshal.AllocHGlobal(len);
                    status = NtQueryObject(handle, ObjectNameInformation, buf, len, out needed);
                }
                if (status != 0) { return null; }
                // UNICODE_STRING { ushort Length; ushort MaxLength; IntPtr Buffer; }  (x64: Buffer at +8)
                short strLen = Marshal.ReadInt16(buf, 0);
                IntPtr strPtr = Marshal.ReadIntPtr(buf, 8);
                if (strLen <= 0 || strPtr == IntPtr.Zero) { return ""; }
                return Marshal.PtrToStringUni(strPtr, strLen / 2);
            }
            catch { return null; }
            finally { Marshal.FreeHGlobal(buf); }
        }

        // ── P/Invoke ──────────────────────────────────────────────────────────────

        private const int SystemExtendedHandleInformation = 0x40;
        private const int ObjectNameInformation = 1;
        private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);
        private const uint PROCESS_DUP_HANDLE = 0x0040;
        private const uint DUPLICATE_CLOSE_SOURCE = 0x1;
        private const uint DUPLICATE_SAME_ACCESS = 0x2;

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int systemInformationClass, IntPtr systemInformation, int systemInformationLength, out int returnLength);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryObject(IntPtr handle, int objectInformationClass, IntPtr objectInformation, int objectInformationLength, out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle, IntPtr targetProcess, out IntPtr targetHandle, uint desiredAccess, bool inheritHandle, uint options);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        private static extern int GetCurrentProcessId();

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateEvent(IntPtr attrs, bool manualReset, bool initialState, string name);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateMutex(IntPtr attrs, bool initialOwner, string name);
    }
}
