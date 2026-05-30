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
            lock (Sync)
            {
                if (!_initialised)
                {
                    Initialize();
                }

                if (_logPath == null)
                {
                    return;
                }

                try
                {
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

                    sb.AppendLine();
                    File.AppendAllText(_logPath, sb.ToString(), Encoding.UTF8);
                }
                catch
                {
                    // Never let logging crash the caller.
                }
            }
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
