using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoClicker.Utils
{
    /// <summary>
    /// The pids of the processes with a given name, cheaply (2026-11-09).
    ///
    /// Tempo watches for Roblox clients on several timers, and each look was Process.GetProcessesByName — which asks
    /// Windows for EVERY process with EVERY thread on the PC and builds an object for each: 7.9 ms of CPU a call here
    /// (445 processes), measured, and the raw system call costs the same. The multi-instance closer alone made that call
    /// every 0.7 s for as long as the vault was unlocked: 10.5 ms of CPU a second, 70% of everything an idle Tempo used
    /// (15 ms/s where 1 ms/s had been measured before the account manager's watchers existed) — 218 s of CPU in under five
    /// hours in the tray, found by per-thread cycle counts on the user's own running copy.
    ///
    /// Listing only the process IDS (EnumProcesses) costs 0.06 ms. So this keeps a map of pid → name and, at each look,
    /// lists the ids and names only what is new, by opening it (0.015 ms each; processes that refuse are left unnamed).
    /// Two things keep the map honest:
    ///   • A pid can be handed to a new process the moment its old one exits, between two looks, and would keep the old
    ///     name. Windows lists processes in the order they were made (very nearly), so a pid that has moved back in the
    ///     list relative to the others is named again.
    ///   • Every <see cref="FullEveryMs"/> the whole map is rebuilt from the full listing, names and all — whatever the
    ///     cheap path got wrong, or could not open, is right again within that time. Callers that cannot wait (a launch
    ///     under way) ask for <c>exact</c>.
    ///
    /// Self-contained on purpose (no Logger, nothing of Tempo's): the harnesses that compile RobloxClients.cs and
    /// RobloxSingletonKiller.cs on their own add this one file.
    /// </summary>
    public static class ProcessIndex
    {
        /// <summary>The map is not refreshed more often than this: looks closer together share one listing.</summary>
        private const int MinRefreshMs = 200;

        /// <summary>How often the map is rebuilt from the full (expensive, exact) listing: 7.9 ms every 30 s is 0.26 ms/s.</summary>
        private const int FullEveryMs = 30_000;

        private static readonly object Gate = new object();
        private static readonly Dictionary<int, string> Names = new Dictionary<int, string>();   // null: could not be named
        private static Dictionary<int, int> _order = new Dictionary<int, int>();                 // pid → place in the last listing
        private static int[] _ids = new int[2048];
        private static long _lastRefresh = long.MinValue / 2, _lastFull = long.MinValue / 2;

        /// <summary>For tests and Live debug: full listings and cheap listings taken so far, and pids named by opening.</summary>
        public static int FullPasses, CheapPasses, Named;

        /// <summary>Test seam: false makes every look a full listing (the behaviour before this class).</summary>
        public static bool Enabled = true;

        /// <summary>
        /// The pids of every process named <paramref name="name"/> (without ".exe", as Process.ProcessName has it).
        /// <paramref name="exact"/> rebuilds from the full listing first — for the moments a wrong answer costs something.
        /// NULL when the processes could not be listed just now: never an empty list for a failure, which would read as
        /// "none running" (the multi-instance closer would forget which clients it must leave alone).
        /// </summary>
        public static List<int> PidsOf(string name, bool exact = false)
        {
            if (string.IsNullOrEmpty(name)) { return new List<int>(); }
            lock (Gate)
            {
                if (!Refresh(exact)) { return null; }
                var found = new List<int>();
                foreach (KeyValuePair<int, string> kv in Names)
                {
                    if (kv.Value != null && string.Equals(kv.Value, name, StringComparison.OrdinalIgnoreCase)) { found.Add(kv.Key); }
                }
                return found;
            }
        }

        /// <summary>True when the map is of a listing that worked — taken now, or moments ago.</summary>
        private static bool Refresh(bool exact)
        {
            long now = Environment.TickCount64;
            if (exact || !Enabled || now - _lastFull >= FullEveryMs)
            {
                if (Full()) { _lastFull = _lastRefresh = now; return true; }
                if (exact || !Enabled) { return false; }
            }
            if (now - _lastRefresh < MinRefreshMs) { return true; }
            if (Cheap()) { _lastRefresh = now; return true; }
            if (Full()) { _lastFull = _lastRefresh = now; return true; }
            return false;
        }

        /// <summary>The exact listing: every process and its name, from one system snapshot.</summary>
        private static bool Full()
        {
            System.Diagnostics.Process[] all;
            try { all = System.Diagnostics.Process.GetProcesses(); }
            catch { return false; }
            Names.Clear();
            for (int i = 0; i < all.Length; i++)
            {
                try { Names[all[i].Id] = all[i].ProcessName; }
                catch { }   // gone since the snapshot
                finally { all[i].Dispose(); }
            }
            // The next cheap look has no earlier listing of its own to compare places with: every pid passes once.
            _order = null;
            FullPasses++;
            return true;
        }

        /// <summary>The cheap listing: ids only; names for what is new or has changed place.</summary>
        private static bool Cheap()
        {
            int count;
            while (true)
            {
                if (!EnumProcesses(_ids, _ids.Length * sizeof(int), out int bytes)) { return false; }
                count = bytes / sizeof(int);
                if (count < _ids.Length) { break; }
                _ids = new int[_ids.Length * 2];   // filled to the brim: there may be more
            }

            var order = new Dictionary<int, int>(count);
            Dictionary<int, int> before = _order;
            int furthest = -1;   // the furthest place in the last listing matched so far
            for (int i = 0; i < count; i++)
            {
                int pid = _ids[i];
                if (pid == 0) { continue; }   // the idle process
                order[pid] = i;
                bool known = Names.ContainsKey(pid);
                if (known && before != null)
                {
                    // Still where it was among the others? One that has moved back behind processes it used to precede
                    // is a new process under an old pid.
                    if (before.TryGetValue(pid, out int was))
                    {
                        if (was < furthest) { known = false; }
                        else { furthest = was; }
                    }
                }
                if (!known) { Names[pid] = NameByOpening(pid); Named++; }
            }

            // What has gone.
            if (Names.Count > order.Count)
            {
                var gone = new List<int>();
                foreach (int pid in Names.Keys) { if (!order.ContainsKey(pid)) { gone.Add(pid); } }
                foreach (int pid in gone) { Names.Remove(pid); }
            }
            _order = order;
            CheapPasses++;
            return true;
        }

        /// <summary>The process's file name without ".exe", or null when it will not be opened (a system process).</summary>
        private static string NameByOpening(int pid)
        {
            IntPtr h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (h == IntPtr.Zero) { return null; }
            try
            {
                var sb = new StringBuilder(520);
                int size = sb.Capacity;
                if (!QueryFullProcessImageName(h, 0, sb, ref size) || size <= 0) { return null; }
                string path = sb.ToString(0, size);
                int slash = path.LastIndexOf('\\');
                string file = slash >= 0 ? path.Substring(slash + 1) : path;
                return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file.Substring(0, file.Length - 4) : file;
            }
            catch { return null; }
            finally { CloseHandle(h); }
        }

        private const uint ProcessQueryLimitedInformation = 0x1000;

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EnumProcesses([Out] int[] ids, int bytes, out int bytesReturned);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
