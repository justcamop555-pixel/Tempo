using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoClicker.Utils
{
    /// <summary>
    /// How Tempo's previous run ended, worked out once at start-up (2026-11-09).
    ///
    /// The user came back from hours of anti-AFK to find Tempo gone, and took it for a crash. Its log simply stopped:
    /// the last line was the window position being saved, 18 s after an anti-AFK press, and no "Tempo exiting".
    /// Windows had closed it. The Claude app had been updated with ForceApplicationShutdown, which closes every process
    /// started from inside that app — and Tempo had been launched by a Claude Code session (an install relaunching it).
    /// The close request came 86 ms after the update began; Windows ended the process about 220 ms later. Nothing in
    /// Tempo said so, then or at the next start.
    ///
    /// Now the next start reads the end of the previous run's log. A run that ended by itself wrote "[shutdown] Tempo
    /// exiting." (or handed over to another copy); one that did not was ended from outside. For those it asks Windows'
    /// own records about that moment — an app update that force-closed what had been started from inside it, a crash
    /// Windows recorded, the PC going down without shutting down, an installer (Restart Manager) — and says what it
    /// found: in the log, in Live debug and, when it is news to the user, in a notification.
    ///
    /// Everything here is best-effort and off the UI thread: a log that cannot be read or an event log that cannot be
    /// queried just leaves less to say.
    /// </summary>
    public static class LastRunCheck
    {
        public enum Ending
        {
            /// <summary>Nothing to go on (a first run, or a log with no earlier run in reach).</summary>
            None,
            /// <summary>It closed down by itself, or handed over to another copy.</summary>
            Clean,
            /// <summary>Windows was shutting down, restarting or signing out.</summary>
            WindowsSessionEnd,
            /// <summary>Ended moments before this copy was started as its restart (an install replacing the file).</summary>
            Replaced,
            /// <summary>Windows closed it while updating the app it had been started from (ForceApplicationShutdown).</summary>
            AppUpdate,
            /// <summary>Windows recorded a crash of Tempo.</summary>
            Crash,
            /// <summary>Tempo caught a fatal error itself and showed its crash report (the user saw it then).</summary>
            CrashShown,
            /// <summary>The PC went down without shutting down (power cut, Windows crash).</summary>
            PcRestarted,
            /// <summary>An installer or an update closed it through Windows' Restart Manager.</summary>
            Installer,
            /// <summary>Ended from outside with nothing recorded (Task Manager, another program) — or a silent crash.</summary>
            Outside,
        }

        public sealed class Report
        {
            public Ending Ending;
            /// <summary>When the run wrote its last line: the moment it ended, to within a few seconds.</summary>
            public DateTime EndedAt;
            /// <summary>Its build ("build 261109-0230 · TEST"), from its start line; null when that line is out of reach.</summary>
            public string Build;
            /// <summary>The app whose update closed it ("Claude"), and the program that ran the update ("claude.exe").</summary>
            public string App, Updater;
            /// <summary>What Windows recorded about a crash (the faulting module and code, or the exception).</summary>
            public string Detail;
            /// <summary>It logged a close asked from outside Tempo just before it ended.</summary>
            public bool AskedFromOutside;
            /// <summary>Replaced: by a different build, started minutes after it (an installer that closes Tempo itself).</summary>
            public bool OtherBuild;
            /// <summary>The programs whose process group it had been started inside ("claude.exe, cmd.exe"), or null.</summary>
            public string StartedInside;
            /// <summary>One English line for the log and Live debug.</summary>
            public string Summary;

            /// <summary>Whether the user should be told: an ending they would not otherwise know the reason for.</summary>
            public bool WorthTelling => Ending == Ending.AppUpdate || Ending == Ending.Crash || Ending == Ending.PcRestarted
                                        || Ending == Ending.Installer || Ending == Ending.Outside;

            /// <summary>It had been started from inside the very program that ran the update that closed it.</summary>
            public bool StartedInsideUpdater => !string.IsNullOrEmpty(StartedInside) && !string.IsNullOrEmpty(Updater)
                && StartedInside.Split(',').Any(n => string.Equals(n.Trim(), Updater, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Written by MainForm.OnFormClosing when the close came from outside Tempo (read back here).</summary>
        public const string AskedFromOutsideMarker = "[window] close asked from outside Tempo";

        /// <summary>Written at start-up by <see cref="NoteProcessGroup"/> (read back here).</summary>
        public const string StartedInsideMarker = "[startup] started inside the process group of ";

        private const string StartMarker = "[startup] Tempo starting";
        private const int TailBytes = 256 * 1024;

        // ── Started from inside another program ────────────────────────────────────────────────────────

        /// <summary>
        /// The other programs in the process group (job object) Tempo was started in, or null when it has one of its
        /// own or none. A program that starts Tempo can keep it in its own group; Windows then treats Tempo as part of
        /// that program, and closes it when the program is updated (MSIX apps are, with ForceApplicationShutdown) or
        /// ends the group. Started from Explorer — the Start menu, the desktop, the sign-in entry — Tempo is in no
        /// group, or alone in the compatibility group Explorer gives each program it starts. Asked first thing at
        /// start-up, before Tempo starts any program of its own (those join its group).
        /// </summary>
        public static string ForeignProcessGroupMembers()
        {
            try
            {
                if (!IsProcessInJob(GetCurrentProcess(), IntPtr.Zero, out bool inJob) || !inJob) { return null; }
                int size = 8 + 4096 * IntPtr.Size;
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (!QueryInformationJobObject(IntPtr.Zero, JobObjectBasicProcessIdList, buffer, size, out _)) { return null; }
                    int count = Marshal.ReadInt32(buffer, 4);
                    int me = Environment.ProcessId;
                    var names = new List<string>();
                    for (int i = 0; i < count && i < 4096; i++)
                    {
                        int pid = (int)Marshal.ReadIntPtr(buffer, 8 + i * IntPtr.Size).ToInt64();
                        if (pid == me) { continue; }
                        try
                        {
                            using (var p = System.Diagnostics.Process.GetProcessById(pid))
                            {
                                string name = p.ProcessName + ".exe";
                                if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) { names.Add(name); }
                            }
                        }
                        catch { }   // gone already
                    }
                    return names.Count > 0 ? string.Join(", ", names) : null;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            catch { return null; }
        }

        /// <summary>
        /// Logs the programs Tempo was started inside, if any, so that the next start can tie an ending to them and Live
        /// debug can show it. Call once, early in start-up.
        /// </summary>
        public static string NoteProcessGroup()
        {
            string others = ForeignProcessGroupMembers();
            StartedInsideNow = others;
            if (others != null)
            {
                Logger.Info(StartedInsideMarker + others + " — Windows treats Tempo as part of that program: if it is "
                            + "updated or ends its group, Tempo is closed with it. Started from the Start menu or the "
                            + "desktop, Tempo runs on its own.");
            }
            return others;
        }

        /// <summary>What <see cref="NoteProcessGroup"/> found for this run (null: on its own).</summary>
        public static string StartedInsideNow { get; private set; }

        // ── The heartbeat ──────────────────────────────────────────────────────────────────────────────
        //
        // A run's last log line dates its end only when it was busy: idle in the tray, Tempo can write nothing for hours,
        // and an end placed at its last line points Windows' records at the wrong moment (an install that stopped a copy
        // 18 minutes after its last line read as "ended from outside" instead of "replaced"). So every run notes, once a
        // minute, that it is still running — a few bytes in logs\heartbeat.txt — and "exited" when it closes down. The next
        // start reads what is left there before writing its own: the end is then known to within a minute.

        public sealed class Heartbeat
        {
            public int Pid;
            public DateTime Started, Beat;
            public bool Exited;
        }

        private const int BeatMs = 60_000;
        private static System.Threading.Timer _beat;
        private static string _beatPath, _beatHeader;

        /// <summary>What the previous run left in the heartbeat file, read before this run wrote its own (null: none).</summary>
        public static Heartbeat PreviousHeartbeat { get; private set; }

        /// <summary>Reads what the last run left, then starts this run's heartbeat. Once, early, by the copy that owns the mutex.</summary>
        public static void StartHeartbeat()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tempo", "logs");
                Directory.CreateDirectory(dir);
                _beatPath = Path.Combine(dir, "heartbeat.txt");
                PreviousHeartbeat = ReadHeartbeat(_beatPath);
                DateTime started;
                try { started = System.Diagnostics.Process.GetCurrentProcess().StartTime; }
                catch { started = DateTime.Now; }
                _beatHeader = "pid=" + Environment.ProcessId + "\nstarted=" + Iso(started);
                WriteBeat("running");
                _beat = new System.Threading.Timer(_ => WriteBeat("running"), null, BeatMs, BeatMs);
            }
            catch { }
        }

        /// <summary>This run is closing down by itself: the next start reads it as a normal end.</summary>
        public static void MarkExited()
        {
            try
            {
                System.Threading.Timer t = _beat;
                _beat = null;
                t?.Dispose();
                WriteBeat("exited");
            }
            catch { }
        }

        private static void WriteBeat(string state)
        {
            string path = _beatPath;
            if (path == null) { return; }
            try { File.WriteAllText(path, _beatHeader + "\nbeat=" + Iso(DateTime.Now) + "\nstate=" + state + "\n"); }
            catch { }   // a full disk or a locked file: the log still dates the end, less exactly
        }

        private static string Iso(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);

        public static Heartbeat ReadHeartbeat(string path)
        {
            try
            {
                if (!File.Exists(path)) { return null; }
                var h = new Heartbeat();
                foreach (string raw in File.ReadAllLines(path))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) { continue; }
                    string key = raw.Substring(0, eq).Trim(), value = raw.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "pid": int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out h.Pid); break;
                        case "started": DateTime.TryParseExact(value, "yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out h.Started); break;
                        case "beat": DateTime.TryParseExact(value, "yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out h.Beat); break;
                        case "state": h.Exited = value == "exited"; break;
                    }
                }
                return h.Beat == default ? null : h;
            }
            catch { return null; }
        }

        // ── The previous run ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Works out how the run before this one ended. <paramref name="processStart"/> is this process's start (lines
        /// from then on are this run's); <paramref name="thisIsARestart"/>: this copy was started with --restart or
        /// --handover. <paramref name="logDir"/> and <paramref name="heartbeat"/> are for tests; by default the real log
        /// folder, and the heartbeat <see cref="StartHeartbeat"/> found.
        /// </summary>
        public static Report Analyze(DateTime processStart, bool thisIsARestart, string logDir = null, Heartbeat heartbeat = null,
                                     string thisBuild = null)
        {
            thisBuild = thisBuild ?? BuildInfo.Short;
            var report = new Report { Ending = Ending.None };
            List<Line> block = PreviousRun(processStart, logDir, out bool sawStart);
            if (logDir == null && heartbeat == null) { heartbeat = PreviousHeartbeat; }
            if (block.Count == 0) { return report; }

            DateTime endedAt = DateTime.MinValue;
            foreach (Line l in block)
            {
                if (l.At < processStart && l.At > endedAt) { endedAt = l.At; }
            }
            if (endedAt == DateTime.MinValue) { return report; }
            DateTime lastLine = endedAt;

            // The heartbeat of the same run (it started when that run's log did) dates a quiet end; and "exited" in it
            // is a normal end even where the log's last lines went missing.
            bool beatSaysExited = false, beatMatched = false;
            // (Its start line follows the process start by up to the 30 s a restart can wait for the copy it replaces.)
            if (heartbeat != null && heartbeat.Pid != Environment.ProcessId && heartbeat.Beat < processStart
                && (!sawStart || ((block[0].At - heartbeat.Started).TotalSeconds > -2 && (block[0].At - heartbeat.Started).TotalSeconds < 60)))
            {
                if (heartbeat.Beat > endedAt) { endedAt = heartbeat.Beat; }
                beatSaysExited = heartbeat.Exited;
                beatMatched = true;
            }
            report.EndedAt = endedAt;
            if (sawStart)
            {
                string start = block[0].Text;
                int b = start.IndexOf("build ", StringComparison.Ordinal);
                if (b >= 0) { report.Build = start.Substring(b).TrimEnd(')', '.', ' '); }
            }
            foreach (Line l in block)
            {
                int i = l.Text.IndexOf(StartedInsideMarker, StringComparison.Ordinal);
                if (i >= 0)
                {
                    string rest = l.Text.Substring(i + StartedInsideMarker.Length);
                    int dash = rest.IndexOf(" — ", StringComparison.Ordinal);
                    report.StartedInside = (dash >= 0 ? rest.Substring(0, dash) : rest).Trim();
                }
            }
            // A close asked from outside among the run's last lines.
            report.AskedFromOutside = block.Any(l => l.At >= lastLine.AddSeconds(-10)
                                                     && l.Text.IndexOf(AskedFromOutsideMarker, StringComparison.Ordinal) >= 0);

            bool clean = beatSaysExited || block.Any(l => IsCleanEnd(l.Text));
            bool sessionEnd = block.Any(l => l.Text.IndexOf("[Shutdown] Windows is ending the session", StringComparison.Ordinal) >= 0);
            // A fatal error Tempo caught itself: its crash report was on screen, and the close after it is a normal one.
            bool crashShown = block.Any(l => l.Text.IndexOf("Fatal error while running the application", StringComparison.Ordinal) >= 0);

            // An app update force-closing what was started inside it is asked first: it can look like a session end
            // to Tempo (Windows may send the close as an end-of-session message to the app's windows).
            if (!clean && FindAppUpdate(endedAt, out string app, out string updater))
            {
                report.Ending = Ending.AppUpdate;
                report.App = app;
                report.Updater = updater;
            }
            else if (crashShown) { report.Ending = Ending.CrashShown; }
            else if (clean) { report.Ending = Ending.Clean; }
            else if (sessionEnd) { report.Ending = Ending.WindowsSessionEnd; }
            // Started as a restart, moments after the end: an install or a restart replaced the copy. Without a heartbeat to
            // date the end (a build from before it, or the file gone) a restart launch is still read that way — they come
            // from installs and restarts, and a quiet copy's last log line can be hours before it was stopped.
            else if (thisIsARestart && (!beatMatched || processStart - endedAt < TimeSpan.FromSeconds(120))) { report.Ending = Ending.Replaced; }
            else if (FindCrash(endedAt, out string detail)) { report.Ending = Ending.Crash; report.Detail = detail; }
            else if (BootedAfter(endedAt)) { report.Ending = Ending.PcRestarted; }
            // A different build, started within minutes of the end, by something that did not say "restart": an installer
            // that closes Tempo and starts the new copy itself (install.cmd: taskkill, copy, "Launch Tempo now?"). The
            // same replacement as a restart, and nothing to warn about: every update made that way read as "closed from
            // outside" and put up "Tempo was closed unexpectedly" (seen 2026-11-11 after a test install to the tray). Kept
            // to minutes, so a copy that vanished long before an install is still told.
            else if (report.Build != null && thisBuild != null && !string.Equals(report.Build, thisBuild, StringComparison.Ordinal)
                     && processStart >= endedAt && processStart - endedAt < TimeSpan.FromMinutes(10))
            {
                report.Ending = Ending.Replaced;
                report.OtherBuild = true;
            }
            else if (FindRestartManager(endedAt)) { report.Ending = Ending.Installer; }
            else { report.Ending = Ending.Outside; }

            report.Summary = Describe(report);
            return report;
        }

        /// <summary>One English line about the report, for the log and Live debug.</summary>
        public static string Describe(Report r)
        {
            if (r == null || r.Ending == Ending.None) { return "nothing earlier in the log to go on"; }
            string when = r.EndedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string which = r.Build != null ? " (" + r.Build + ")" : "";
            switch (r.Ending)
            {
                case Ending.Clean:
                    return "the last run" + which + " closed down normally at " + when;
                case Ending.WindowsSessionEnd:
                    return "the last run" + which + " ended at " + when + " as Windows shut down, restarted or signed out";
                case Ending.Replaced:
                    return r.OtherBuild
                        ? "the last run" + which + " was ended at " + when + ", and this different build was started minutes "
                          + "later (an install replaced it)"
                        : "the last run" + which + " was ended at " + when + ", moments before this copy was started as its "
                          + "restart (an install or a restart replaced it)";
                case Ending.CrashShown:
                    return "the last run" + which + " hit a fatal error and showed its crash report; it ended at " + when;
            }
            var sb = new StringBuilder("the last run" + which + " ended at " + when + " without closing down — ");
            switch (r.Ending)
            {
                case Ending.AppUpdate:
                    sb.Append("Windows closed it while updating the app “" + r.App + "” (an update that closes every program "
                              + "started from inside the app)");
                    if (r.StartedInsideUpdater) { sb.Append("; Tempo had been started from inside it (" + r.StartedInside + ")"); }
                    break;
                case Ending.Crash:
                    sb.Append("Windows recorded a crash" + (string.IsNullOrEmpty(r.Detail) ? "" : ": " + r.Detail));
                    break;
                case Ending.PcRestarted:
                    sb.Append("the PC went down without shutting down (a power cut or a Windows crash)");
                    break;
                case Ending.Installer:
                    sb.Append("an installer or an update closed it (Windows Restart Manager)");
                    break;
                default:
                    sb.Append(r.AskedFromOutside
                        ? "another program asked it to close and then ended it (Task Manager, or a program closing others)"
                        : "it was ended from outside (Task Manager or another program), or it stopped without Windows recording why");
                    break;
            }
            if (r.Ending != Ending.AppUpdate && !string.IsNullOrEmpty(r.StartedInside))
            {
                sb.Append("; it had been started from inside " + r.StartedInside);
            }
            return sb.ToString();
        }

        private static bool IsCleanEnd(string text) =>
            text.IndexOf("[shutdown] Tempo exiting", StringComparison.Ordinal) >= 0
            || text.IndexOf("[shutdown] exiting", StringComparison.Ordinal) >= 0
            || text.IndexOf("[startup] handed over to", StringComparison.Ordinal) >= 0
            || text.IndexOf("[Uninstall] handed", StringComparison.Ordinal) >= 0;

        private struct Line
        {
            public DateTime At;
            public string Text;
        }

        /// <summary>
        /// The previous run's lines: from its start line up to this run's start line (or the end), in file order. Lines a
        /// closing copy wrote after this one started (a restart's "Tempo exiting") are kept; this run's own are not.
        /// </summary>
        private static List<Line> PreviousRun(DateTime processStart, string logDir, out bool sawStart)
        {
            sawStart = false;
            string dir = logDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tempo", "logs");
            string current = Path.Combine(dir, "autoclicker.log");
            List<Line> lines = Tail(current);
            if (!lines.Any(l => l.At < processStart && IsStart(l.Text)))
            {
                // The file was rotated as this launch began (or the run was long): the rest is in .1.
                List<Line> older = Tail(current + ".1");
                older.AddRange(lines);
                lines = older;
            }
            int ours = lines.FindIndex(l => l.At >= processStart && IsStart(l.Text));
            int end = ours >= 0 ? ours : lines.Count;
            if (end == 0) { return new List<Line>(); }
            int prev = lines.FindLastIndex(end - 1, end, l => IsStart(l.Text) && l.At < processStart);
            sawStart = prev >= 0;
            int from = prev >= 0 ? prev : Math.Max(0, end - 400);
            return lines.GetRange(from, end - from);
        }

        private static bool IsStart(string text) => text.IndexOf(StartMarker, StringComparison.Ordinal) >= 0;

        /// <summary>The timestamped lines of the last <see cref="TailBytes"/> of a log file, in order.</summary>
        private static List<Line> Tail(string path)
        {
            var list = new List<Line>();
            try
            {
                if (!File.Exists(path)) { return list; }
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long from = Math.Max(0, fs.Length - TailBytes);
                    fs.Seek(from, SeekOrigin.Begin);
                    string text;
                    using (var reader = new StreamReader(fs, Encoding.UTF8)) { text = reader.ReadToEnd(); }
                    string[] raw = text.Split('\n');
                    for (int i = from > 0 ? 1 : 0; i < raw.Length; i++)   // a read from mid-file starts mid-line
                    {
                        string s = raw[i].TrimEnd('\r');
                        if (s.Length < 23) { continue; }
                        if (!DateTime.TryParseExact(s.Substring(0, 23), "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture,
                                                    DateTimeStyles.None, out DateTime at))
                        {
                            continue;   // an exception's stack-trace line
                        }
                        list.Add(new Line { At = at, Text = s });
                    }
                }
            }
            catch { }
            return list;
        }

        // ── Windows' records of the moment ─────────────────────────────────────────────────────────────

        /// <summary>
        /// A package update with ForceApplicationShutdown starting within seconds of the end: Windows closes every process
        /// started from inside that app (AppXDeploymentServer event 603: Path = the package, Flags bit 1 = force shutdown,
        /// CallingProcess = who asked).
        /// </summary>
        private static bool FindAppUpdate(DateTime endedAt, out string app, out string updater)
        {
            app = updater = null;
            // From a little before the last sign of life to the minute after it (the heartbeat's interval).
            foreach (EventRecord e in Query("Microsoft-Windows-AppXDeploymentServer/Operational", "EventID=603",
                                            endedAt.AddSeconds(-20), endedAt.AddSeconds(65)))
            {
                using (e)
                {
                    try
                    {
                        if (e.Properties.Count < 3) { continue; }
                        long flags = Convert.ToInt64(e.Properties[2].Value, CultureInfo.InvariantCulture);
                        if ((flags & 1) == 0) { continue; }
                        app = FriendlyPackage(Convert.ToString(e.Properties[1].Value, CultureInfo.InvariantCulture));
                        updater = e.Properties.Count > 4 ? Convert.ToString(e.Properties[4].Value, CultureInfo.InvariantCulture) : null;
                        if (!string.IsNullOrEmpty(app)) { return true; }
                    }
                    catch { }
                }
            }
            return false;
        }

        /// <summary>"Claude_pzs8sxrjxfjjc" → "Claude"; "Microsoft.WindowsTerminal_8wekyb3d8bbwe" → "WindowsTerminal".</summary>
        public static string FriendlyPackage(string package)
        {
            if (string.IsNullOrWhiteSpace(package)) { return null; }
            string name = package;
            int underscore = name.IndexOf('_');
            if (underscore > 0) { name = name.Substring(0, underscore); }
            int dot = name.LastIndexOf('.');
            if (dot >= 0 && dot < name.Length - 1) { name = name.Substring(dot + 1); }
            return name;
        }

        /// <summary>A crash of Tempo that Windows recorded: Application Error 1000, .NET Runtime 1026, Windows Error Reporting 1001.</summary>
        private static bool FindCrash(DateTime endedAt, out string detail)
        {
            detail = null;
            foreach (EventRecord e in Query("Application", "(EventID=1000 or EventID=1001 or EventID=1026)",
                                            endedAt.AddSeconds(-5), endedAt.AddMinutes(5)))
            {
                using (e)
                {
                    try
                    {
                        string provider = e.ProviderName ?? "";
                        if (provider != "Application Error" && provider != ".NET Runtime" && provider != "Windows Error Reporting") { continue; }
                        string all = string.Join("|", e.Properties.Select(p => Convert.ToString(p.Value, CultureInfo.InvariantCulture)));
                        if (all.IndexOf("TempoClicker", StringComparison.OrdinalIgnoreCase) < 0
                            && all.IndexOf("Tempo.exe", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                        if (provider == "Application Error" && e.Properties.Count > 6)
                        {
                            // The code is a number on some Windows builds and text ("c0000005") on others.
                            object code = e.Properties[6].Value;
                            string codeText = code is string text6 ? "0x" + text6.Replace("0x", "")
                                : "0x" + Convert.ToInt64(code, CultureInfo.InvariantCulture).ToString("x8", CultureInfo.InvariantCulture);
                            detail = "a fault in " + e.Properties[3].Value + " (exception " + codeText + ")";
                        }
                        else if (provider == ".NET Runtime")
                        {
                            string text = Convert.ToString(e.Properties[0].Value, CultureInfo.InvariantCulture) ?? "";
                            string info = text.Split('\n').Select(s => s.Trim()).FirstOrDefault(s => s.StartsWith("Exception Info:", StringComparison.Ordinal));
                            detail = info ?? detail;
                        }
                        detail = detail ?? "recorded by " + provider;
                        if (provider != "Windows Error Reporting") { return true; }   // a better one may follow a WER report
                    }
                    catch { }
                }
            }
            return detail != null;
        }

        /// <summary>
        /// The PC went down with Tempo running, without shutting down: it started again after the end, and Windows recorded
        /// no proper shutdown in between (a shutdown that came later would mean Tempo had already gone before it).
        /// </summary>
        private static bool BootedAfter(DateTime endedAt)
        {
            DateTime boot;
            try { boot = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64); }
            catch { return false; }
            if (boot <= endedAt) { return false; }
            foreach (EventRecord e in Query("System", "(EventID=13 or EventID=1074 or EventID=6006)", endedAt.AddSeconds(-5), boot))
            {
                using (e)
                {
                    string p = e.ProviderName ?? "";
                    if ((e.Id == 13 && p == "Microsoft-Windows-Kernel-General") || (e.Id == 1074 && p == "User32")
                        || (e.Id == 6006 && p == "EventLog"))
                    {
                        return false;   // shut down properly, later
                    }
                }
            }
            return true;
        }

        /// <summary>A Restart Manager session (installers use it to close programs that hold their files) began just before.</summary>
        private static bool FindRestartManager(DateTime endedAt)
        {
            foreach (EventRecord e in Query("Application", "EventID=10000", endedAt.AddSeconds(-60), endedAt.AddSeconds(65)))
            {
                using (e)
                {
                    if (string.Equals(e.ProviderName, "Microsoft-Windows-RestartManager", StringComparison.Ordinal)) { return true; }
                }
            }
            return false;
        }

        /// <summary>The events of <paramref name="log"/> matching <paramref name="idFilter"/> between two local times. Empty when it cannot read them.</summary>
        private static IEnumerable<EventRecord> Query(string log, string idFilter, DateTime fromLocal, DateTime toLocal)
        {
            string xpath = "*[System[(" + idFilter + ") and TimeCreated[@SystemTime>='" + Utc(fromLocal)
                           + "' and @SystemTime<='" + Utc(toLocal) + "']]]";
            var found = new List<EventRecord>();
            try
            {
                using (var reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, xpath)))
                {
                    EventRecord e;
                    while (found.Count < 200 && (e = reader.ReadEvent()) != null) { found.Add(e); }
                }
            }
            catch { }   // the log is missing, or not readable on this PC
            return found;
        }

        private static string Utc(DateTime local) =>
            local.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        // ── Win32 ──────────────────────────────────────────────────────────────────────────────────────
        private const int JobObjectBasicProcessIdList = 3;
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, IntPtr info, int length, out int returned);
    }
}
