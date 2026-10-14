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

        // Every line written this session, counted: the ring's first line is number _written - Ring.Count.
        // Lets a reader say "everything up to here" and mean the same lines later — Live debug's Clear view,
        // which every rebuild of its log used to undo.
        private static long _written;

        /// <summary>How many lines have been logged this session (the number the next one is past).</summary>
        public static long WrittenCount
        {
            get { lock (Sync) { return _written; } }
        }

        /// <summary>Copy of the ring, oldest first, with the session-wide number of its first line.</summary>
        public static string[] Snapshot(out long firstNumber)
        {
            lock (Sync)
            {
                firstNumber = _written - Ring.Count;
                return Ring.ToArray();
            }
        }

        /// <summary>
        /// How many lines the ring is holding, without copying it.
        ///
        /// Live debug shows "142 of 2,310" beside its filter and refreshes it as lines
        /// arrive; asking through <see cref="Snapshot"/> would allocate a 2,310-element
        /// array several times a second to learn one number.
        /// </summary>
        public static int BufferedCount
        {
            get { lock (Sync) { return Ring.Count; } }
        }

        /// <summary>When false, nothing is written to the log file (privacy option).</summary>
        public static bool Enabled
        {
            get { return _enabled; }
            set
            {
                lock (Sync)
                {
                    _enabled = value;
                    if (!value) { CloseFile(); }
                }
            }
        }
        private static bool _enabled = true;

        /// <summary>Writes DEBUG lines to the file too — for chasing one particular problem.</summary>
        public static bool FileDebug { get; set; }

        // ── The file ────────────────────────────────────────────────────────────
        // Kept OPEN while lines arrive and closed after a quiet spell. It used to be opened,
        // appended to and closed for every line (File.AppendAllText): measured on this PC at
        // 0.193 ms a line against 0.015 ms through one open stream — 12.7× — on whichever thread
        // was logging, usually the UI thread, and a start-up writes a few hundred lines. Every line
        // is still flushed to Windows as it is written, so a crash loses nothing; closing after a
        // quiet spell leaves the file free for Notepad, a bug report or the uninstaller.
        //
        // It is also rotated while Tempo runs, not only when it starts: Tempo lives in the tray for
        // days, and the size check at start-up was the only one, so a long session grew the file
        // without limit. Three old files are kept instead of one.
        private static FileStream _stream;
        private static long _fileLength;
        private static long _nextRotateAt = MaxFileBytes;
        private static long _lastWriteTick;
        private static System.Threading.Timer _idleTimer;
        private static bool _sessionMarked;
        private const int IdleCloseMs = 2000;
        private const long MaxFileBytes = 1_048_576;
        private const int Backups = 3;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        // The last report logged under each key by InfoIfChanged.
        private static readonly System.Collections.Generic.Dictionary<string, string> LastReport =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);

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

        /// <summary>
        /// Routine detail: kept in memory for Live debug, but not written to the log file (unless
        /// <see cref="FileDebug"/>). For lines with nothing new to say each time — a layout
        /// diagnostic on every tab switch, "settings saved", every hotkey registered. Over ten days
        /// of a real log they were half its lines, and the file is capped, so they pushed out the
        /// history a bug report needs.
        /// </summary>
        public static void Debug(string message)
        {
            Write("DEBUG", message, null);
        }

        /// <summary>
        /// A state report ("hotkeys: …"): INFO when it differs from the last report logged under
        /// <paramref name="key"/> this session, DEBUG when it says the same again — so the file
        /// records each state once, and Live debug still sees every report.
        /// </summary>
        public static void InfoIfChanged(string key, string message)
        {
            bool changed;
            lock (Sync)
            {
                changed = !LastReport.TryGetValue(key ?? "", out string last) || last != message;
                if (changed) { LastReport[key ?? ""] = message; }
            }
            Write(changed ? "INFO" : "DEBUG", message, null);
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
                _written++;
                while (Ring.Count > RingCapacity)
                {
                    Ring.Dequeue();
                }

                if (_enabled && (level != "DEBUG" || FileDebug))
                {
                    if (!_initialised)
                    {
                        Initialize();
                    }
                    if (_logPath != null)
                    {
                        try
                        {
                            WriteToFile(line);
                        }
                        catch
                        {
                            // Never let logging crash the caller; the next line opens it afresh.
                            CloseFile();
                        }
                    }
                }
            }

            try { LineLogged?.Invoke(line); } catch { }
        }

        /// <summary>Appends one line to the open file (opening it if needed). Under Sync.</summary>
        private static void WriteToFile(string line)
        {
            if (_stream == null)
            {
                OpenFile();
                if (_stream == null) { return; }
            }
            byte[] bytes = Utf8NoBom.GetBytes(line + Environment.NewLine);
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush();
            _fileLength += bytes.Length;
            _lastWriteTick = Environment.TickCount64;
            if (_fileLength > _nextRotateAt)
            {
                Rotate();
            }
        }

        /// <summary>Opens the log for appending; others may read it, and delete or rename it. Under Sync.</summary>
        private static void OpenFile()
        {
            try
            {
                var fs = new FileStream(_logPath, FileMode.Append, FileAccess.Write,
                                        FileShare.ReadWrite | FileShare.Delete);
                long had = fs.Length;
                if (had == 0)
                {
                    // A new file starts with the BOM File.AppendAllText used to write, so a tool that
                    // sniffs the encoding still reads it as UTF-8.
                    byte[] bom = Encoding.UTF8.GetPreamble();
                    fs.Write(bom, 0, bom.Length);
                }
                else if (!_sessionMarked)
                {
                    // A blank line between sessions, so each run stands apart in the file.
                    byte[] gap = Utf8NoBom.GetBytes(Environment.NewLine);
                    fs.Write(gap, 0, gap.Length);
                }
                _sessionMarked = true;
                _fileLength = fs.Length;
                _nextRotateAt = Math.Max(MaxFileBytes, _fileLength);
                _stream = fs;
                ScheduleIdleClose(IdleCloseMs);
            }
            catch
            {
                _stream = null;
            }
        }

        private static void CloseFile()
        {
            try { _stream?.Dispose(); } catch { }
            _stream = null;
        }

        /// <summary>Closes the log file now; the next line opens it again. For shutdown and uninstall.</summary>
        public static void Close()
        {
            lock (Sync) { CloseFile(); }
        }

        private static void ScheduleIdleClose(int dueMs)
        {
            try
            {
                if (_idleTimer == null)
                {
                    _idleTimer = new System.Threading.Timer(_ => CloseIfIdle(), null,
                        System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                }
                _idleTimer.Change(Math.Max(50, dueMs), System.Threading.Timeout.Infinite);
            }
            catch { }
        }

        /// <summary>One-shot, re-armed while lines keep coming: no timer runs while the file is closed.</summary>
        private static void CloseIfIdle()
        {
            lock (Sync)
            {
                if (_stream == null) { return; }
                long quiet = Environment.TickCount64 - _lastWriteTick;
                if (quiet >= IdleCloseMs) { CloseFile(); }
                else { ScheduleIdleClose((int)(IdleCloseMs - quiet)); }
            }
        }

        /// <summary>
        /// autoclicker.log → .1 → .2 → .3, the oldest dropped. If the file can't be moved (someone
        /// has it open without sharing), it is tried again 256 KB later rather than on every line.
        /// Under Sync (or before anything is written).
        /// </summary>
        private static void Rotate()
        {
            CloseFile();
            try
            {
                for (int i = Backups; i >= 1; i--)
                {
                    string from = i == 1 ? _logPath : _logPath + "." + (i - 1);
                    string to = _logPath + "." + i;
                    if (!File.Exists(from)) { continue; }
                    if (File.Exists(to)) { File.Delete(to); }
                    File.Move(from, to);
                }
                _fileLength = 0;
                _nextRotateAt = MaxFileBytes;
            }
            catch
            {
                _nextRotateAt = _fileLength + 262_144;
            }
        }

        private static void TrimIfLarge()
        {
            try
            {
                if (_logPath != null && File.Exists(_logPath) && new FileInfo(_logPath).Length > MaxFileBytes)
                {
                    // The previous runs move to .1 (and on down), so a crash that happened just
                    // before this launch is not lost.
                    _fileLength = new FileInfo(_logPath).Length;
                    Rotate();
                }
            }
            catch
            {
                // Ignore.
            }
        }
    }
}
