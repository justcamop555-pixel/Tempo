using System;
using System.IO;
using System.Text;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Minimal thread-safe logger that writes timestamped lines to a file under
    /// the user's local application data folder. Failures while logging are
    /// swallowed so that logging can never crash the application.
    /// </summary>
    public static class Logger
    {
        private static readonly object Sync = new object();
        private static string _logPath;
        private static bool _initialised;

        // Recent lines kept in memory for the Live Debug window — independent of
        // the file (and of the Enabled privacy flag for FILE writing: the ring
        // never leaves the process unless the user copies/saves it themselves).
        private const int RingCapacity = 1500;
        private static readonly System.Collections.Generic.Queue<string> Ring =
            new System.Collections.Generic.Queue<string>(RingCapacity + 8);

        /// <summary>Fired for every log line (any thread). Used by Live Debug.</summary>
        public static event Action<string> LineLogged;

        private static int _warnCount;
        private static int _errorCount;
        /// <summary>Warnings logged this session (for the Live Debug header).</summary>
        public static int WarnCount => _warnCount;
        /// <summary>Errors logged this session (for the Live Debug header).</summary>
        public static int ErrorCount => _errorCount;

        // The single most recent warning / error, kept so the Live Debug "Health"
        // panel can surface "what just went wrong" without the user scrolling the log.
        private static string _lastWarn;
        private static string _lastError;
        private static DateTime _lastWarnUtc;
        private static DateTime _lastErrorUtc;

        /// <summary>The most recent WARN message this session (message only), or null.</summary>
        public static string LastWarn { get { lock (Sync) { return _lastWarn; } } }
        /// <summary>The most recent ERROR message this session (message only), or null.</summary>
        public static string LastError { get { lock (Sync) { return _lastError; } } }
        /// <summary>When the last warning was logged (UTC); default if none.</summary>
        public static DateTime LastWarnUtc { get { lock (Sync) { return _lastWarnUtc; } } }
        /// <summary>When the last error was logged (UTC); default if none.</summary>
        public static DateTime LastErrorUtc { get { lock (Sync) { return _lastErrorUtc; } } }

        /// <summary>
        /// Logs a swallowed exception with a consistent "[where] handled: …" prefix, at
        /// WARN level. Use this in place of a bare <c>catch { }</c> whenever the failure
        /// is recovered from but WOULD matter to someone diagnosing a problem — so it
        /// shows up in the Live Debug Health panel instead of vanishing. Truly trivial,
        /// high-frequency catches (hot audio/render callbacks) should stay silent so the
        /// log isn't flooded.
        /// </summary>
        public static void Swallow(string where, Exception ex)
        {
            Warn("[" + (where ?? "?") + "] handled: " + (ex?.Message ?? "(no detail)"));
        }

        /// <summary>Copy of the recent in-memory log lines, oldest first.</summary>
        public static string[] Snapshot()
        {
            lock (Sync)
            {
                return Ring.ToArray();
            }
        }

        /// <summary>When false, nothing is written to the log file (privacy option).</summary>
        public static bool Enabled { get; set; } = true;

        public static void Initialize()
        {
            lock (Sync)
            {
                if (_initialised)
                {
                    return;
                }

                try
                {
                    string dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Tempo",
                        "logs");

                    Directory.CreateDirectory(dir);
                    _logPath = Path.Combine(dir, "autoclicker.log");

                    // Trim the log if it grows beyond ~1 MB.
                    TrimIfLarge();
                }
                catch
                {
                    _logPath = null;
                }

                _initialised = true;
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public static void Error(string message)
        {
            Write("ERROR", message, null);
        }

        public static void Error(string message, Exception ex)
        {
            Write("ERROR", message, ex);
        }

        public static string GetLogPath()
        {
            lock (Sync)
            {
                if (!_initialised)
                {
                    Initialize();
                }

                return _logPath;
            }
        }

        private static void Write(string level, string message, Exception ex)
        {
            if (level == "WARN")
            {
                System.Threading.Interlocked.Increment(ref _warnCount);
                lock (Sync) { _lastWarn = message; _lastWarnUtc = DateTime.UtcNow; }
            }
            else if (level == "ERROR")
            {
                System.Threading.Interlocked.Increment(ref _errorCount);
                // Keep the exception type/message with it so Health shows the real cause,
                // not just the bland caller-supplied headline.
                string detail = ex != null ? message + " — " + ex.GetType().Name + ": " + ex.Message : message;
                lock (Sync) { _lastError = detail; _lastErrorUtc = DateTime.UtcNow; }
            }

            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            sb.Append(" [");
            sb.Append(level);
            sb.Append("] ");
            sb.Append(message);

            if (ex != null)
            {
                sb.AppendLine();
                sb.Append("    ");
                sb.Append(ex.GetType().Name);
                sb.Append(": ");
                sb.Append(ex.Message);
                if (ex.StackTrace != null)
                {
                    sb.AppendLine();
                    sb.Append(ex.StackTrace);
                }
            }

            string line = sb.ToString();

            lock (Sync)
            {
                // Always feed the in-memory ring (the Live Debug view), even when
                // FILE logging is off — nothing leaves the process either way.
                Ring.Enqueue(line);
                while (Ring.Count > RingCapacity)
                {
                    Ring.Dequeue();
                }

                if (Enabled)
                {
                    if (!_initialised)
                    {
                        Initialize();
                    }
                    if (_logPath != null)
                    {
                        try
                        {
                            File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
                        }
                        catch
                        {
                            // Never let logging crash the caller.
                        }
                    }
                }
            }

            try { LineLogged?.Invoke(line); } catch { }
        }

        private static void TrimIfLarge()
        {
            try
            {
                if (_logPath != null && File.Exists(_logPath))
                {
                    var info = new FileInfo(_logPath);
                    if (info.Length > 1_048_576)
                    {
                        // Rotate: keep the previous log as a single .1 backup so a
                        // crash that happened just before launch is not lost.
                        string backup = _logPath + ".1";
                        try
                        {
                            if (File.Exists(backup))
                            {
                                File.Delete(backup);
                            }

                            File.Move(_logPath, backup);
                        }
                        catch
                        {
                            // If rotation fails for any reason, fall back to deleting
                            // so the log cannot grow without bound.
                            File.Delete(_logPath);
                        }
                    }
                }
            }
            catch
            {
                // Ignore.
            }
        }
    }
}
