using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Turns an unhandled exception into a readable report, saves it next to the
    /// log, and builds a one-click GitHub "new issue" link pre-filled with the
    /// details. Nothing is ever transmitted automatically — the report only leaves
    /// the machine if the user clicks through and submits the GitHub form, so no
    /// data is sent silently and no credentials are needed.
    /// </summary>
    public static class CrashReporter
    {
        /// <summary>Where the GitHub issue is opened (the project repository).</summary>
        public static string Repository => UpdateChecker.Repository;

        /// <summary>Email address bug reports are sent to (for non-GitHub users).</summary>
        public const string SupportEmail = "jompikoo@gmail.com";

        public static string CurrentVersion
        {
            get
            {
                try
                {
                    Version v = Assembly.GetExecutingAssembly().GetName().Version;
                    return v == null ? "unknown" : v.ToString();
                }
                catch
                {
                    return "unknown";
                }
            }
        }

        /// <summary>Builds the full human-readable crash report text.</summary>
        public static string BuildReport(Exception ex, string context)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Tempo crash report");
            sb.AppendLine("==================");
            sb.AppendLine("When     : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Version  : " + CurrentVersion);
            sb.AppendLine("Where    : " + (string.IsNullOrEmpty(context) ? "(unspecified)" : context));
            sb.AppendLine("OS       : " + Safe(() => RuntimeInformation.OSDescription));
            sb.AppendLine("Runtime  : " + Safe(() => RuntimeInformation.FrameworkDescription));
            sb.AppendLine("64-bit   : " + Environment.Is64BitProcess);
            sb.AppendLine();

            if (ex == null)
            {
                sb.AppendLine("No exception object was supplied.");
                return Redact(sb.ToString());
            }

            sb.AppendLine("Error    : " + ex.GetType().FullName);
            sb.AppendLine("Message  : " + ex.Message);
            sb.AppendLine();
            sb.AppendLine("Stack trace:");
            sb.AppendLine(ex.StackTrace ?? "(none)");

            Exception inner = ex.InnerException;
            int depth = 0;
            while (inner != null && depth < 5)
            {
                sb.AppendLine();
                sb.AppendLine("Caused by: " + inner.GetType().FullName + ": " + inner.Message);
                sb.AppendLine(inner.StackTrace ?? "(none)");
                inner = inner.InnerException;
                depth++;
            }

            return Redact(sb.ToString());
        }

        /// <summary>
        /// Removes the Windows account name from report text so a shared report
        /// never reveals it. Paths like C:\Users\Alice\... become C:\Users\&lt;user&gt;\...
        /// and any standalone occurrence of the account name is masked.
        /// </summary>
        private static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            try
            {
                // Mask the user folder in any Windows/Unix-style path first.
                text = System.Text.RegularExpressions.Regex.Replace(
                    text, @"([A-Za-z]:\\Users\\)[^\\/]+", "$1<user>");
                text = System.Text.RegularExpressions.Regex.Replace(
                    text, @"(/(?:home|Users)/)[^/]+", "$1<user>");

                // Mask any remaining standalone occurrence of the account name.
                string user = Environment.UserName;
                if (!string.IsNullOrWhiteSpace(user) && user.Length >= 3)
                {
                    text = System.Text.RegularExpressions.Regex.Replace(
                        text,
                        System.Text.RegularExpressions.Regex.Escape(user),
                        "<user>",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
            }
            catch
            {
                // If redaction fails, fall back to the original text rather than crash.
            }

            return text;
        }

        /// <summary>Writes the report beside the log file. Returns the path, or null on failure.</summary>
        public static string WriteReportFile(string report)
        {
            try
            {
                string dir = Path.GetDirectoryName(Logger.GetLogPath());
                if (string.IsNullOrEmpty(dir))
                {
                    return null;
                }

                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "crash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                File.WriteAllText(path, report, Encoding.UTF8);
                return path;
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not write crash report file: " + ex.Message);
                return null;
            }
        }

        /// <summary>Builds a GitHub "new issue" URL from an explicit (already-redacted) report body.</summary>
        public static string IssueUrlFromReport(Exception ex, string reportBody)
        {
            string title = "Crash: " + (ex == null ? "unknown error" : Shorten(ex.Message, 80));

            var body = new StringBuilder();
            body.AppendLine("**What I was doing when it happened:**");
            body.AppendLine("<!-- please describe, it really helps -->");
            body.AppendLine();
            body.AppendLine("**Details (edit out anything you don't want to share):**");
            body.AppendLine("```");
            body.AppendLine(Shorten(reportBody ?? string.Empty, 1500));
            body.AppendLine("```");

            return "https://github.com/" + Repository + "/issues/new"
                + "?labels=bug"
                + "&title=" + Uri.EscapeDataString(title)
                + "&body=" + Uri.EscapeDataString(body.ToString());
        }

        /// <summary>
        /// Builds a GitHub "new issue" URL pre-filled with the crash details.
        /// </summary>
        public static string BuildIssueUrl(Exception ex, string context)
        {
            return IssueUrlFromReport(ex, BuildReport(ex, context));
        }

        /// <summary>A blank bug-report issue link for proactive (non-crash) reports.</summary>
        public static string BuildBlankIssueUrl()
        {
            var body = new StringBuilder();
            body.AppendLine("**Describe the bug**");
            body.AppendLine("_A clear description of what went wrong._");
            body.AppendLine();
            body.AppendLine("**Steps to reproduce**");
            body.AppendLine("1. ");
            body.AppendLine("2. ");
            body.AppendLine();
            body.AppendLine("**What you expected to happen**");
            body.AppendLine();
            body.AppendLine("**What actually happened**");
            body.AppendLine();
            body.AppendLine("---");
            body.AppendLine("_Diagnostics (auto-filled — please keep so we can help faster):_");
            body.AppendLine();
            body.AppendLine("- Tempo: " + CurrentVersion);
            body.AppendLine("- Windows: " + Safe(() => RuntimeInformation.OSDescription));
            body.AppendLine("- Architecture: " + Safe(() => RuntimeInformation.OSArchitecture +
                " (process " + RuntimeInformation.ProcessArchitecture + ")"));
            body.AppendLine("- .NET runtime: " + Safe(() => RuntimeInformation.FrameworkDescription));
            body.AppendLine("- Processors: " + Safe(() => Environment.ProcessorCount.ToString()));
            body.AppendLine("- Display: " + Safe(() =>
            {
                int hz = EnvironmentInfo.GetPrimaryRefreshHz();
                return hz > 0 ? hz + " Hz" : "unknown";
            }));

            return "https://github.com/" + Repository + "/issues/new"
                + "?labels=bug"
                + "&title=" + Uri.EscapeDataString("Bug report")
                + "&body=" + Uri.EscapeDataString(body.ToString());
        }

        /// <summary>Builds a "mailto:" URL from an explicit (already-redacted) report body.</summary>
        public static string MailtoUrlFromReport(Exception ex, string reportBody)
        {
            string subject = "Tempo bug: " + (ex == null ? "unknown error" : Shorten(ex.Message, 80));

            var body = new StringBuilder();
            body.AppendLine("What I was doing when it happened:");
            body.AppendLine();
            body.AppendLine();
            body.AppendLine("--- details (edit out anything you don't want to share) ---");
            body.AppendLine(Shorten(reportBody ?? string.Empty, 1200));
            body.AppendLine();
            body.AppendLine("(If a crash file was saved, please attach it — use 'Open report' in the crash window to find it.)");

            return "mailto:" + SupportEmail
                + "?subject=" + Uri.EscapeDataString(subject)
                + "&body=" + Uri.EscapeDataString(body.ToString());
        }

        /// <summary>
        /// Builds a "mailto:" link pre-addressed to the support email with the
        /// crash details in the body. Opens the user's default email app — works
        /// without a GitHub account.
        /// </summary>
        public static string BuildMailtoUrl(Exception ex, string context)
        {
            return MailtoUrlFromReport(ex, BuildReport(ex, context));
        }

        /// <summary>A blank bug-report email link for proactive (non-crash) reports.</summary>
        public static string BuildBlankMailtoUrl()
        {
            return "mailto:" + SupportEmail
                + "?subject=" + Uri.EscapeDataString(BlankReportSubject())
                + "&body=" + Uri.EscapeDataString(BlankReportBody());
        }

        /// <summary>Opens a Gmail "compose" window in the browser, pre-filled.</summary>
        public static string BuildGmailComposeUrl()
        {
            return "https://mail.google.com/mail/?view=cm&fs=1"
                + "&to=" + Uri.EscapeDataString(SupportEmail)
                + "&su=" + Uri.EscapeDataString(BlankReportSubject())
                + "&body=" + Uri.EscapeDataString(BlankReportBody());
        }

        /// <summary>Opens an Outlook-on-the-web "compose" window in the browser, pre-filled.</summary>
        public static string BuildOutlookComposeUrl()
        {
            return "https://outlook.office.com/mail/deeplink/compose"
                + "?to=" + Uri.EscapeDataString(SupportEmail)
                + "&subject=" + Uri.EscapeDataString(BlankReportSubject())
                + "&body=" + Uri.EscapeDataString(BlankReportBody());
        }

        /// <summary>Opens a Yahoo Mail "compose" window in the browser, pre-filled.</summary>
        public static string BuildYahooComposeUrl()
        {
            return "https://compose.mail.yahoo.com/?"
                + "to=" + Uri.EscapeDataString(SupportEmail)
                + "&subject=" + Uri.EscapeDataString(BlankReportSubject())
                + "&body=" + Uri.EscapeDataString(BlankReportBody());
        }

        /// <summary>Plain-text report (recipient + subject + body) for copying to the clipboard.</summary>
        public static string BuildBlankReportText()
        {
            string text = "To: " + SupportEmail + Environment.NewLine
                + "Subject: " + BlankReportSubject() + Environment.NewLine
                + Environment.NewLine
                + BlankReportBody();

            // The clipboard isn't length-limited like a URL, so include a little of
            // the recent log here — it's often what pins down a bug.
            string log = RecentLogTail(25);
            if (!string.IsNullOrEmpty(log))
            {
                text += Environment.NewLine + "Recent log (most recent lines):" + Environment.NewLine + log;
            }
            return text;
        }

        private static string RecentLogTail(int maxLines)
        {
            try
            {
                string path = Logger.GetLogPath();
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                {
                    return null;
                }
                string[] lines = System.IO.File.ReadAllLines(path);
                if (lines.Length == 0)
                {
                    return null;
                }
                int take = Math.Min(maxLines, lines.Length);
                var sb = new StringBuilder();
                for (int i = lines.Length - take; i < lines.Length; i++)
                {
                    sb.AppendLine(lines[i]);
                }
                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string BlankReportSubject()
        {
            return "Tempo bug report";
        }

        private static string BlankReportBody()
        {
            var body = new StringBuilder();
            body.AppendLine("Describe the bug:");
            body.AppendLine();
            body.AppendLine("Steps to reproduce:");
            body.AppendLine("1. ");
            body.AppendLine("2. ");
            body.AppendLine();
            body.AppendLine("What you expected to happen:");
            body.AppendLine();
            body.AppendLine("What actually happened:");
            body.AppendLine();
            body.AppendLine("---");
            body.AppendLine("Diagnostics (please keep so we can help faster):");
            body.AppendLine("- Tempo: " + CurrentVersion);
            body.AppendLine("- Windows: " + Safe(() => RuntimeInformation.OSDescription));
            body.AppendLine("- Architecture: " + Safe(() => RuntimeInformation.OSArchitecture +
                " (process " + RuntimeInformation.ProcessArchitecture + ")"));
            body.AppendLine("- .NET runtime: " + Safe(() => RuntimeInformation.FrameworkDescription));
            body.AppendLine("- Processors: " + Safe(() => Environment.ProcessorCount.ToString()));
            body.AppendLine("- Display: " + Safe(() =>
            {
                int hz = EnvironmentInfo.GetPrimaryRefreshHz();
                return hz > 0 ? hz + " Hz" : "unknown";
            }));
            return body.ToString();
        }

        private static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max)
            {
                return s ?? string.Empty;
            }
            return s.Substring(0, max) + "…";
        }

        private static string Safe(Func<string> f)
        {
            try { return f(); }
            catch { return "(unavailable)"; }
        }
    }
}
