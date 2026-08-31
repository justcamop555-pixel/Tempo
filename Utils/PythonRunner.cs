using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Runs a Python script for a macro's Script step, and returns what happened.
    ///
    /// WHY IT SHELLS OUT. Tempo ships as a self-contained single-file .NET exe; there is
    /// no way to bundle CPython into that, and no embedded engine worth having (IronPython
    /// stalled well before Python 3, and Python.NET needs a CPython install anyway — the
    /// very thing bundling was meant to avoid). So a Script step runs the interpreter the
    /// user already has, and says so plainly when they have none.
    ///
    /// WHAT THIS CLASS IS CAREFUL ABOUT. Everything here is a failure mode that bites the
    /// naive version of "just start a process and wait":
    ///
    ///   • Both pipes are drained ASYNCHRONOUSLY. Reading stdout to the end while stderr
    ///     fills its 4KB buffer deadlocks both sides forever — the single most common way
    ///     to hang an app that shells out.
    ///   • The wait is on the process AND the macro's stop signal, so pressing Stop during
    ///     a long script actually stops it instead of waiting out the timeout.
    ///   • Timeout and stop both kill the WHOLE process tree: a script that ran
    ///     subprocess.Popen would otherwise leave orphans behind after every play.
    ///   • Captured output is capped, so `while True: print(x)` cannot grow the log ring
    ///     until Tempo runs out of memory.
    ///   • Nothing here throws. A macro step that fails is a result the player decides
    ///     about, not an exception through the playback thread.
    /// </summary>
    internal static class PythonRunner
    {
        /// <summary>How a script run ended.</summary>
        internal enum Outcome
        {
            /// <summary>Exit code 0.</summary>
            Ok,
            /// <summary>The script ran and exited non-zero.</summary>
            ScriptError,
            /// <summary>No Python interpreter could be found on this PC.</summary>
            NoPython,
            /// <summary>The .py file is missing.</summary>
            NoScript,
            /// <summary>It outlived its timeout and was killed.</summary>
            TimedOut,
            /// <summary>The macro was stopped while it was running.</summary>
            Stopped,
            /// <summary>It could not be started at all (permissions, bad path…).</summary>
            LaunchFailed
        }

        internal sealed class Result
        {
            public Outcome Outcome;
            public int ExitCode;
            public long ElapsedMs;
            public string Output = "";      // stdout + stderr, trimmed to OutputCap
            public string Detail = "";      // why it failed, for the log and the UI

            public bool Succeeded => Outcome == Outcome.Ok;
        }

        /// <summary>
        /// Most of a script's output is noise for a macro; the tail is what matters when
        /// something went wrong. Capped rather than unbounded — see the class note.
        /// </summary>
        private const int OutputCap = 8000;

        private static readonly object _probeLock = new object();
        private static string _cachedExe;
        private static string[] _cachedArgsPrefix;
        private static bool _probed;

        /// <summary>Forgets the cached interpreter, so a fresh install is picked up.</summary>
        internal static void Rescan()
        {
            lock (_probeLock)
            {
                _probed = false;
                _cachedExe = null;
                _cachedArgsPrefix = null;
            }
        }

        /// <summary>
        /// The interpreter Tempo will use, or null if there is none. Cached: probing runs
        /// candidate executables, which is far too slow to repeat on every macro step.
        /// </summary>
        internal static string InterpreterPath
        {
            get
            {
                lock (_probeLock)
                {
                    if (!_probed) { Probe(); }
                    return _cachedExe;
                }
            }
        }

        /// <summary>A one-line description for the UI: the interpreter and its version.</summary>
        internal static string DescribeInterpreter()
        {
            string exe = InterpreterPath;
            if (string.IsNullOrEmpty(exe))
            {
                return Localization.T("No Python interpreter found on this PC.");
            }
            string version = ProbeVersion(exe, _cachedArgsPrefix);
            return string.IsNullOrEmpty(version) ? exe : version + "  ·  " + exe;
        }

        /// <summary>
        /// Finds an interpreter. The py launcher first: on Windows it is the one thing
        /// that reliably resolves to a REAL Python, where a bare "python" on PATH is very
        /// often the Microsoft Store stub that exists only to open the Store page.
        /// </summary>
        private static void Probe()
        {
            _probed = true;
            _cachedExe = null;
            _cachedArgsPrefix = null;

            var candidates = new List<(string exe, string[] prefix)>
            {
                ("py", new[] { "-3" }),
                ("python3", Array.Empty<string>()),
                ("python", Array.Empty<string>()),
            };

            // Per-user and machine installs, newest first, for the case where Python is
            // installed but was never added to PATH — which is the DEFAULT in the official
            // installer, so it is the common case rather than the exotic one.
            try
            {
                foreach (string root in new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                        + @"\Programs\Python",
                    @"C:\Program Files\Python",
                    @"C:\Python"
                })
                {
                    if (!Directory.Exists(root)) { continue; }
                    var dirs = new List<string>(Directory.GetDirectories(root));
                    dirs.Sort(StringComparer.OrdinalIgnoreCase);
                    dirs.Reverse();
                    foreach (string d in dirs)
                    {
                        string exe = Path.Combine(d, "python.exe");
                        if (File.Exists(exe)) { candidates.Add((exe, Array.Empty<string>())); }
                    }
                }
            }
            catch (Exception ex) { Logger.Swallow("PythonRunner.Probe(dirs)", ex); }

            foreach (var c in candidates)
            {
                string version = ProbeVersion(c.exe, c.prefix);
                if (!string.IsNullOrEmpty(version))
                {
                    _cachedExe = c.exe;
                    _cachedArgsPrefix = c.prefix;
                    Logger.Info("[Python] using " + version + " (" + c.exe + ").");
                    return;
                }
            }
            Logger.Info("[Python] no interpreter found (tried py -3, python3, python, and the usual install folders).");
        }

        /// <summary>
        /// Runs "-V" and returns what it printed, or null if this candidate is not a
        /// usable interpreter. The Store stub is filtered out here: it exits non-zero
        /// (or prints nothing) rather than reporting a version.
        /// </summary>
        private static string ProbeVersion(string exe, string[] prefix)
        {
            try
            {
                var psi = NewStartInfo(exe);
                foreach (string a in prefix ?? Array.Empty<string>()) { psi.ArgumentList.Add(a); }
                psi.ArgumentList.Add("-V");

                using (var p = Process.Start(psi))
                {
                    if (p == null) { return null; }
                    string outText = p.StandardOutput.ReadToEnd();
                    string errText = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(4000))
                    {
                        KillTree(p, null);
                        return null;
                    }
                    if (p.ExitCode != 0) { return null; }
                    string v = (outText + errText).Trim();       // 2.x printed to stderr
                    return v.StartsWith("Python", StringComparison.OrdinalIgnoreCase) ? v : null;
                }
            }
            catch
            {
                return null;    // not installed, not executable, blocked — all "no"
            }
        }

        private static ProcessStartInfo NewStartInfo(string exe)
        {
            return new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,     // required for redirection, and never a shell
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
        }

        /// <summary>
        /// Runs <paramref name="scriptPath"/> and waits for it, up to
        /// <paramref name="timeoutMs"/> or until <paramref name="stopSignal"/> fires.
        /// Never throws; every failure comes back as an <see cref="Outcome"/>.
        /// </summary>
        internal static Result Run(string scriptPath, int timeoutMs, ManualResetEventSlim stopSignal)
        {
            var result = new Result();
            var clock = Stopwatch.StartNew();

            if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
            {
                result.Outcome = Outcome.NoScript;
                result.Detail = Localization.F("The script file no longer exists:\n{0}", scriptPath ?? "");
                return result;
            }

            string exe = InterpreterPath;
            if (string.IsNullOrEmpty(exe))
            {
                result.Outcome = Outcome.NoPython;
                result.Detail = Localization.T(
                    "Tempo could not find Python on this PC. Install it from python.org, "
                    + "then use Rescan in the script step.");
                return result;
            }

            if (timeoutMs < 100) { timeoutMs = 100; }

            var captured = new StringBuilder();
            int dropped = 0;
            object sync = new object();

            void Capture(string line)
            {
                if (line == null) { return; }
                lock (sync)
                {
                    if (captured.Length < OutputCap) { captured.AppendLine(line); }
                    else { dropped++; }
                }
            }

            Process proc = null;
            JobObject job = null;
            try
            {
                var psi = NewStartInfo(exe);
                foreach (string a in _cachedArgsPrefix ?? Array.Empty<string>()) { psi.ArgumentList.Add(a); }
                // -u: unbuffered, so output arrives as it is produced rather than in one
                // lump when the process exits — which is what makes it useful in Live debug
                // while a long script is still running.
                psi.ArgumentList.Add("-u");
                psi.ArgumentList.Add(scriptPath);
                // Relative paths inside the script resolve next to the script, which is
                // what someone writing it expects — not next to Tempo.exe.
                try { psi.WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? ""; } catch { }

                proc = new Process { StartInfo = psi, EnableRaisingEvents = false };
                proc.OutputDataReceived += (s, e) => Capture(e.Data);
                proc.ErrorDataReceived += (s, e) => Capture(e.Data);

                if (!proc.Start())
                {
                    result.Outcome = Outcome.LaunchFailed;
                    result.Detail = Localization.T("Python could not be started.");
                    return result;
                }

                // Assign to the job IMMEDIATELY, before the script has had a chance to
                // start anything: processes inherit the job from their parent, so anything
                // spawned from here on belongs to it and dies with it.
                job = JobObject.TryCreate();
                if (job != null && !job.Assign(proc))
                {
                    Logger.Warn("[Python] could not put the interpreter in a job object; " +
                                "a script that spawns children may leave them running.");
                }

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                // Nothing is going to type at it. Closing stdin means a script that calls
                // input() gets EOF and ends, instead of blocking until the timeout.
                try { proc.StandardInput.Close(); } catch { }

                // Wait on the process AND the stop signal together, in short slices.
                var handles = stopSignal != null
                    ? new WaitHandle[] { GetHandle(proc), stopSignal.WaitHandle }
                    : new WaitHandle[] { GetHandle(proc) };

                int signalled = WaitHandle.WaitAny(handles, timeoutMs);

                if (signalled == WaitHandle.WaitTimeout)
                {
                    KillTree(proc, job);
                    result.Outcome = Outcome.TimedOut;
                    result.Detail = Localization.F("The script was still running after {0} ms and was stopped.", timeoutMs);
                }
                else if (signalled == 1)
                {
                    KillTree(proc, job);
                    result.Outcome = Outcome.Stopped;
                    result.Detail = Localization.T("The macro was stopped while the script was running.");
                }
                else
                {
                    // Let the pipes finish draining; without this the last lines of output
                    // are routinely missing from a script that printed just before exiting.
                    try { proc.WaitForExit(1500); } catch { }
                    result.ExitCode = SafeExitCode(proc);
                    result.Outcome = result.ExitCode == 0 ? Outcome.Ok : Outcome.ScriptError;
                    if (result.Outcome == Outcome.ScriptError)
                    {
                        result.Detail = Localization.F("The script exited with code {0}.", result.ExitCode);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Outcome = Outcome.LaunchFailed;
                result.Detail = ex.Message;
                Logger.Swallow("PythonRunner.Run", ex);
            }
            finally
            {
                // Job first: disposing it closes the handle, and KILL_ON_JOB_CLOSE
                // means anything still alive in there goes with it.
                try { job?.Dispose(); } catch { }
                try { proc?.Dispose(); } catch { }
            }

            lock (sync)
            {
                result.Output = captured.ToString().TrimEnd();
                if (dropped > 0)
                {
                    result.Output += Environment.NewLine +
                        Localization.F("… {0} more line(s) not shown.", dropped);
                }
            }
            result.ElapsedMs = clock.ElapsedMilliseconds;
            return result;
        }

        /// <summary>
        /// A WaitHandle for the process, so it can be waited on alongside the stop signal.
        /// </summary>
        private static WaitHandle GetHandle(Process p)
        {
            // Process.WaitHandle is not public; this is the documented stand-in and is
            // what Process.WaitForExit uses underneath.
            return new ManualResetEvent(false) { SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(p.Handle, false) };
        }

        private static int SafeExitCode(Process p)
        {
            try { return p.ExitCode; }
            catch { return -1; }
        }

        /// <summary>
        /// Kills the process and everything it started.
        ///
        /// Process.Kill(entireProcessTree: true) is NOT enough on its own, and this was
        /// measured rather than assumed: a script doing subprocess.Popen(...) left its
        /// child running after the step timed out and was killed. That API walks
        /// parent-child links at kill time, and the launcher sits in the middle of them
        /// (Tempo starts "py", which starts python.exe, which starts the script's own
        /// child) — one broken or re-parented link and a descendant is missed.
        ///
        /// The job object is the mechanism that does not depend on that bookkeeping:
        /// every process started underneath one belongs to it, and terminating the job
        /// terminates all of them at once. Kill() stays as the fallback for the case where
        /// the job could not be created at all.
        /// </summary>
        private static void KillTree(Process p, JobObject job)
        {
            try { job?.Terminate(); } catch (Exception ex) { Logger.Swallow("PythonRunner.KillJob", ex); }
            try
            {
                if (p != null && !p.HasExited) { p.Kill(entireProcessTree: true); }
            }
            catch (Exception ex) { Logger.Swallow("PythonRunner.KillTree", ex); }
        }

        /// <summary>
        /// A Windows job object holding the interpreter and everything it spawns.
        ///
        /// KILL_ON_JOB_CLOSE means that even if Tempo is killed outright, Windows tears
        /// the job down with it — so a runaway script cannot outlive the app that started
        /// it. Nested jobs have been supported since Windows 8, so assigning works even
        /// when the interpreter is already inside somebody else's job.
        /// </summary>
        private sealed class JobObject : IDisposable
        {
            private IntPtr _handle;

            private const int JobObjectExtendedLimitInformation = 9;
            private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

            [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            private static extern IntPtr CreateJobObject(IntPtr attrs, string name);

            [System.Runtime.InteropServices.DllImport("kernel32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

            [System.Runtime.InteropServices.DllImport("kernel32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

            [System.Runtime.InteropServices.DllImport("kernel32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

            [System.Runtime.InteropServices.DllImport("kernel32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            private static extern bool CloseHandle(IntPtr h);

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                public long PerProcessUserTimeLimit;
                public long PerJobUserTimeLimit;
                public uint LimitFlags;
                public UIntPtr MinimumWorkingSetSize;
                public UIntPtr MaximumWorkingSetSize;
                public uint ActiveProcessLimit;
                public UIntPtr Affinity;
                public uint PriorityClass;
                public uint SchedulingClass;
            }

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            private struct IO_COUNTERS
            {
                public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
                public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
            }

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
                public IO_COUNTERS IoInfo;
                public UIntPtr ProcessMemoryLimit;
                public UIntPtr JobMemoryLimit;
                public UIntPtr PeakProcessMemoryUsed;
                public UIntPtr PeakJobMemoryUsed;
            }

            /// <summary>Creates the job, or returns null if the OS would not give us one.</summary>
            internal static JobObject TryCreate()
            {
                try
                {
                    IntPtr h = CreateJobObject(IntPtr.Zero, null);
                    if (h == IntPtr.Zero) { return null; }

                    var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                    int size = System.Runtime.InteropServices.Marshal.SizeOf(info);
                    IntPtr buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
                    try
                    {
                        System.Runtime.InteropServices.Marshal.StructureToPtr(info, buf, false);
                        if (!SetInformationJobObject(h, JobObjectExtendedLimitInformation, buf, (uint)size))
                        {
                            CloseHandle(h);
                            return null;
                        }
                    }
                    finally
                    {
                        System.Runtime.InteropServices.Marshal.FreeHGlobal(buf);
                    }
                    return new JobObject { _handle = h };
                }
                catch (Exception ex)
                {
                    Logger.Swallow("PythonRunner.JobObject.TryCreate", ex);
                    return null;
                }
            }

            internal bool Assign(Process p)
            {
                try
                {
                    return _handle != IntPtr.Zero && p != null && AssignProcessToJobObject(_handle, p.Handle);
                }
                catch (Exception ex)
                {
                    Logger.Swallow("PythonRunner.JobObject.Assign", ex);
                    return false;
                }
            }

            internal void Terminate()
            {
                if (_handle != IntPtr.Zero) { TerminateJobObject(_handle, 1); }
            }

            public void Dispose()
            {
                if (_handle != IntPtr.Zero)
                {
                    try { CloseHandle(_handle); } catch { }
                    _handle = IntPtr.Zero;
                }
            }
        }
    }
}
