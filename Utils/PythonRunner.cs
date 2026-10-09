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

        /// <summary>
        /// An interpreter the USER chose, which beats everything below. "" = decide
        /// automatically. Set from settings at startup and whenever the picker changes.
        ///
        /// This exists because of the failure ScriptStepForm already names: "my script
        /// does nothing" is nearly always "Tempo is using a different Python from the one
        /// I installed my packages into". Until now the dialog could only REPORT which
        /// interpreter had been found — there was no way to point it at the right one.
        /// </summary>
        public static volatile string PreferredExe = "";

        /// <summary>
        /// Raised when <see cref="PreferredExe"/> is changed from the UI, so the choice is
        /// persisted. MainForm assigns this; the dialogs that offer the picker are two
        /// levels from any settings object and threading one down to them would be a
        /// worse trade than one hook here.
        /// </summary>
        public static Action<string> PreferredExeChanged;

        /// <summary>Sets the chosen interpreter and lets it be saved. "" = automatic.</summary>
        public static void ChoosePreferred(string exe)
        {
            PreferredExe = exe ?? "";
            Logger.Info("[Python] interpreter " +
                        (PreferredExe.Length == 0 ? "choice cleared — deciding automatically."
                                                  : "set to " + PreferredExe + " by the user."));
            try { PreferredExeChanged?.Invoke(PreferredExe); } catch { }
        }

        /// <summary>
        /// The interpreter that will actually run <paramref name="scriptPath"/>, and why.
        ///
        /// Precedence, most specific first:
        ///   1. the user's explicit choice — they said it, so it wins;
        ///   2. a virtual environment beside the script (.venv or venv, in the script's
        ///      own folder or its parent). This is the convention every Python tool
        ///      follows, and it is the whole reason a script "can't find" a package it
        ///      plainly has: the packages are in the venv, and Tempo was running the
        ///      system Python. Logged whenever it is used, so it is never silent magic;
        ///   3. whatever Probe() found.
        /// </summary>
        internal static (string exe, string[] prefix, string why) ResolveFor(string scriptPath)
        {
            string chosen = PreferredExe;
            if (!string.IsNullOrWhiteSpace(chosen))
            {
                try
                {
                    if (File.Exists(chosen)) { return (chosen, Array.Empty<string>(), "chosen in Settings"); }
                    Logger.Warn("[Python] the chosen interpreter is missing (" + chosen +
                                ") — falling back to the one Tempo can find.");
                }
                catch (Exception ex) { Logger.Swallow("PythonRunner.ResolveFor(chosen)", ex); }
            }

            string venv = FindVenvFor(scriptPath);
            if (venv != null) { return (venv, Array.Empty<string>(), "virtual environment beside the script"); }

            // Both halves under the one lock: reading the prefix after the path, outside it,
            // could pair "py" with a Rescan's cleared prefix — and "py" without "-3" can pick
            // a Python 2 still installed on the machine.
            string exe;
            string[] pre;
            lock (_probeLock)
            {
                if (!_probed) { Probe(); }
                exe = _cachedExe;
                pre = _cachedArgsPrefix;
            }
            return (exe, pre ?? Array.Empty<string>(), "found on this PC");
        }

        /// <summary>A .venv/venv interpreter beside the script, or null.</summary>
        private static string FindVenvFor(string scriptPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(scriptPath);
                for (int up = 0; up < 2 && !string.IsNullOrEmpty(dir); up++)
                {
                    foreach (string name in new[] { ".venv", "venv" })
                    {
                        string exe = Path.Combine(dir, name, "Scripts", "python.exe");
                        if (File.Exists(exe)) { return exe; }
                    }
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch (Exception ex) { Logger.Swallow("PythonRunner.FindVenvFor", ex); }
            return null;
        }

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

        // DescribeInterpreter lived here: no caller anywhere, and it ran a version probe
        // synchronously every time it was asked — on whatever thread asked, which for a
        // description is the UI thread. Removed rather than left as a trap.

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

        /// <summary>How long one candidate may take to report its version.</summary>
        private const int ProbeTimeoutMs = 4000;

        /// <summary>
        /// Runs "-V" and returns what it printed, or null if this candidate is not a
        /// usable interpreter. The Store stub is filtered out here: it exits non-zero
        /// (or prints nothing) rather than reporting a version.
        ///
        /// THE TIMEOUT USED TO BE DECORATIVE. The old version read stdout to the end, then
        /// stderr to the end, and only THEN waited up to 4 s for the process — but a read to
        /// the end does not return until the process closes the pipe, which a candidate that
        /// never exits never does. Measured with one that waits on stdin: still blocked after
        /// 12 s, and it would have stayed blocked for ever. This runs under the probe lock,
        /// and the Script step dialog asks for the interpreter, so one bad candidate on PATH
        /// could freeze Tempo outright. Now: stdin is closed at once (nothing will ever type
        /// at it), both pipes are read asynchronously, and the wait is the real limit.
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
                    try { p.StandardInput.Close(); } catch { }

                    System.Threading.Tasks.Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                    System.Threading.Tasks.Task<string> errTask = p.StandardError.ReadToEndAsync();

                    if (!p.WaitForExit(ProbeTimeoutMs))
                    {
                        KillTree(p, null);
                        Logger.Info("[Python] " + exe + " did not answer -V within " + ProbeTimeoutMs +
                                    " ms — skipped.");
                        return null;
                    }
                    if (p.ExitCode != 0) { return null; }

                    // The process has exited; its pipes close with it. A short bounded wait
                    // collects what it printed without trusting that to happen instantly.
                    System.Threading.Tasks.Task.WaitAll(new System.Threading.Tasks.Task[] { outTask, errTask }, 1000);
                    string v = ((outTask.IsCompleted ? outTask.Result : "") +
                                (errTask.IsCompleted ? errTask.Result : "")).Trim();   // 2.x printed to stderr
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

        /// <summary>Optional extras for one run.</summary>
        internal sealed class RunOptions
        {
            /// <summary>
            /// Called with each line of output AS IT ARRIVES, on a pipe-reader thread. The
            /// macro player logs through this so a long script shows its progress in Live
            /// debug while it runs — which is what running Python with -u was always for,
            /// and what nothing actually did until now.
            /// </summary>
            public Action<string> OnLine;

            /// <summary>Extra environment variables for the script (e.g. TEMPO_MACRO).</summary>
            public IDictionary<string, string> Environment;
        }

        /// <summary>
        /// Runs <paramref name="scriptPath"/> and waits for it, up to
        /// <paramref name="timeoutMs"/> or until <paramref name="stopSignal"/> fires.
        /// Never throws; every failure comes back as an <see cref="Outcome"/>.
        /// </summary>
        internal static Result Run(string scriptPath, int timeoutMs, ManualResetEventSlim stopSignal,
                                   RunOptions options = null)
        {
            var result = new Result();
            var clock = Stopwatch.StartNew();

            if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
            {
                result.Outcome = Outcome.NoScript;
                result.Detail = Localization.F("The script file no longer exists:\n{0}", scriptPath ?? "");
                return result;
            }

            (string exe, string[] prefix, string why) = ResolveFor(scriptPath);
            if (string.IsNullOrEmpty(exe))
            {
                result.Outcome = Outcome.NoPython;
                result.Detail = Localization.T(
                    "Tempo could not find Python on this PC. Install it from python.org, "
                    + "then use Rescan in the script step.");
                return result;
            }
            Logger.Info("[Python] running " + Path.GetFileName(scriptPath) + " with " + exe +
                        " (" + why + ").");

            if (timeoutMs < 100) { timeoutMs = 100; }

            var captured = new StringBuilder();
            int dropped = 0;
            object sync = new object();

            Action<string> onLine = options?.OnLine;
            void Capture(string line)
            {
                if (line == null) { return; }
                lock (sync)
                {
                    if (captured.Length < OutputCap) { captured.AppendLine(line); }
                    else { dropped++; }
                }
                if (onLine != null)
                {
                    try { onLine(line); } catch { /* a listener must never break the run */ }
                }
            }

            Process proc = null;
            JobObject job = null;
            try
            {
                var psi = NewStartInfo(exe);
                // The prefix belongs to the RESOLVED interpreter ("py" needs -3; a venv's
                // python.exe must not be handed it), so it comes from ResolveFor rather
                // than from whatever the last probe happened to cache.
                foreach (string a in prefix ?? Array.Empty<string>()) { psi.ArgumentList.Add(a); }
                // -u: unbuffered, so output arrives as it is produced rather than in one
                // lump when the process exits — which is what makes it useful in Live debug
                // while a long script is still running.
                psi.ArgumentList.Add("-u");
                psi.ArgumentList.Add(scriptPath);
                // Relative paths inside the script resolve next to the script, which is
                // what someone writing it expects — not next to Tempo.exe.
                try { psi.WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? ""; } catch { }

                // UTF-8 on the pipes. Measured: a script that printed "café ✓" or an emoji
                // CRASHED under Tempo with UnicodeEncodeError, while running perfectly in a
                // terminal. A terminal is a console, which Python writes as UTF-8; Tempo reads
                // a PIPE, which Python encodes in the ANSI code page unless told otherwise —
                // and cp1252 has no emoji. Tempo already decodes the pipes as UTF-8, so this
                // makes the two ends agree. Only the standard streams: PYTHONUTF8 would also
                // change how the script's own files are opened, which is not ours to change.
                psi.Environment["PYTHONIOENCODING"] = "utf-8";

                // What the script might want to know about the step it is running in, as
                // plain environment variables — no argument syntax to learn, no change to
                // how a macro is saved, and a script that ignores them is unaffected.
                psi.Environment["TEMPO_SCRIPT"] = scriptPath;
                psi.Environment["TEMPO_VERSION"] = TempoVersion();
                try
                {
                    System.Drawing.Point cur = System.Windows.Forms.Cursor.Position;
                    psi.Environment["TEMPO_CURSOR_X"] = cur.X.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    psi.Environment["TEMPO_CURSOR_Y"] = cur.Y.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                catch { /* no desktop (a service session): leave them unset rather than lie */ }
                if (options?.Environment != null)
                {
                    foreach (KeyValuePair<string, string> kv in options.Environment)
                    {
                        if (!string.IsNullOrEmpty(kv.Key)) { psi.Environment[kv.Key] = kv.Value ?? ""; }
                    }
                }

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

                // Wait on the process AND the stop signal together. The process's wait
                // handle is a wrapper that does not own the handle; it is disposed here
                // rather than left to the finalizer on every script step.
                int signalled;
                using (WaitHandle procHandle = GetHandle(proc))
                {
                    var handles = stopSignal != null
                        ? new WaitHandle[] { procHandle, stopSignal.WaitHandle }
                        : new WaitHandle[] { procHandle };
                    signalled = WaitHandle.WaitAny(handles, timeoutMs);
                }

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
                        string text;
                        lock (sync) { text = captured.ToString(); }
                        result.Detail = ExplainFailure(text, result.ExitCode, exe, prefix);
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

        private static string TempoVersion()
        {
            try
            {
                Version v = typeof(PythonRunner).Assembly.GetName().Version;
                return v == null ? "" : v.Major + "." + v.Minor + "." + v.Build;
            }
            catch { return ""; }
        }

        /// <summary>
        /// Module names that are NOT what you pip-install. A hint telling someone to run
        /// "pip install cv2" sends them to an error of its own, so the common mismatches are
        /// named here; anything not listed is installed under its own name, which is the
        /// usual case.
        /// </summary>
        private static readonly Dictionary<string, string> PipNameFor =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["cv2"] = "opencv-python",
                ["PIL"] = "Pillow",
                ["yaml"] = "PyYAML",
                ["win32api"] = "pywin32",
                ["win32con"] = "pywin32",
                ["win32gui"] = "pywin32",
                ["win32clipboard"] = "pywin32",
                ["pywintypes"] = "pywin32",
                ["sklearn"] = "scikit-learn",
                ["bs4"] = "beautifulsoup4",
                ["dotenv"] = "python-dotenv",
                ["serial"] = "pyserial",
                ["Crypto"] = "pycryptodome",
                ["dateutil"] = "python-dateutil",
                ["fitz"] = "PyMuPDF",
                ["docx"] = "python-docx",
            };

        /// <summary>
        /// Why a script failed, in words someone can act on.
        ///
        /// It used to be "The script exited with code 1." — true, and useless: the answer was
        /// sitting in the traceback Tempo had just captured. The last line of a Python
        /// traceback IS the reason ("ValueError: bad input 42"), so that is what is reported
        /// now. And the most common failure of all — a package installed into a different
        /// Python from the one Tempo ran — gets the exact command that fixes it, for the
        /// interpreter that actually ran the script.
        /// </summary>
        internal static string ExplainFailure(string output, int exitCode, string exe, string[] prefix)
        {
            string reason = LastExceptionLine(output);
            if (string.IsNullOrEmpty(reason))
            {
                return Localization.F("The script exited with code {0}.", exitCode);
            }

            var m = System.Text.RegularExpressions.Regex.Match(reason,
                @"^(?:ModuleNotFoundError|ImportError): No module named '([^']+)'");
            if (m.Success)
            {
                string module = m.Groups[1].Value.Split('.')[0];
                string package = PipNameFor.TryGetValue(module, out string p) ? p : module;
                string run = QuoteIfNeeded(exe);
                if (prefix != null && prefix.Length > 0) { run += " " + string.Join(" ", prefix); }
                return reason + Environment.NewLine + Localization.F(
                    "The Python that ran it ({0}) doesn't have this package. Install it with: {1}",
                    exe, run + " -m pip install " + package);
            }
            return reason;
        }

        /// <summary>
        /// The "ExceptionType: message" line that ends a Python traceback, or null when the
        /// output holds no traceback (a plain sys.exit(1), say).
        /// </summary>
        internal static string LastExceptionLine(string output)
        {
            if (string.IsNullOrEmpty(output)) { return null; }
            string[] lines = output.Replace("\r", "").Split('\n');
            int tb = -1;
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (lines[i].StartsWith("Traceback (most recent call last):", StringComparison.Ordinal)) { tb = i; break; }
            }
            if (tb < 0) { return null; }

            // The exception line is the first UNINDENTED line after the frames: every frame
            // line is indented, and a chained traceback starts again with its own header.
            for (int i = tb + 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0 || line[0] == ' ' || line[0] == '\t') { continue; }
                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^[A-Za-z_][\w.]*(: .*)?$"))
                {
                    return line.Length > 300 ? line.Substring(0, 299) + "…" : line;
                }
            }
            return null;
        }

        private static string QuoteIfNeeded(string path)
        {
            if (string.IsNullOrEmpty(path)) { return path; }
            return path.IndexOf(' ') >= 0 && !path.StartsWith("\"", StringComparison.Ordinal)
                ? "\"" + path + "\""
                : path;
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
