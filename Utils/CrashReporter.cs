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
                return Sanitize(sb.ToString());
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

            return Sanitize(sb.ToString());
        }

        /// <summary>
        /// Strips the things in a report that identify the person sitting at the PC:
        /// the Windows account name, the machine name, the account's domain, and the
        /// user folder in any path.
        ///
        /// PUBLIC, and every report body and every report TITLE goes through it. It
        /// used to be private and applied at exactly one call site — the crash body —
        /// so the entire "Report a bug…" / "Email a bug…" path sent the account name
        /// and full profile path verbatim, and even a crash issue carried the raw
        /// exception message in its title while the body beside it was clean. A
        /// promise the UI makes about privacy has to hold on every path that can send
        /// something, not on the one that happened to call the helper.
        /// </summary>
        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            try
            {
                // The user folder in any Windows/Unix-style path first, so the name is
                // gone from paths even when the bare-name pass below cannot run.
                text = System.Text.RegularExpressions.Regex.Replace(
                    text, @"([A-Za-z]:\\Users\\)[^\\/]+", "$1<user>");
                text = System.Text.RegularExpressions.Regex.Replace(
                    text, @"(/(?:home|Users)/)[^/]+", "$1<user>");

                // Then the bare names. Machine name first: it is usually the longer
                // string and often CONTAINS the account name ("ALICE-PC"), so masking
                // it first stops the account pass from carving it into "<user>-PC".
                text = MaskWord(text, Safe(() => Environment.MachineName), "<pc>");
                text = MaskWord(text, Safe(() => Environment.UserName), "<user>");
                text = MaskWord(text, Safe(() => Environment.UserDomainName), "<domain>");
            }
            catch
            {
                // Sanitising must never be the thing that breaks reporting: fall back
                // to the text we already have rather than throwing.
            }

            return text;
        }

        /// <summary>
        /// Replaces <paramref name="word"/> where it stands on its own, not where it
        /// happens to sit inside a longer word.
        ///
        /// Both halves matter. The old version matched anywhere, so a short account
        /// name would quietly eat unrelated text — "sam" turns "sample rate" into
        /// "&lt;user&gt;ple rate" — and a report mangled that way is one nobody can act
        /// on, with no sign to the sender that it happened. It also skipped names under
        /// three characters entirely, which left accounts like "jo" or "al" unmasked
        /// outside a path. Word boundaries fix the first and make the length guard
        /// unnecessary, so the second goes away too.
        /// </summary>
        private static string MaskWord(string text, string word, string replacement)
        {
            if (string.IsNullOrWhiteSpace(word) || word.Length < 2 || word == "(unavailable)")
            {
                return text;
            }
            // \b does not fire next to '\', '.' or '-', which is exactly where these
            // names appear in paths and host names, so the boundary is spelled out as
            // "not preceded/followed by a letter, digit or underscore".
            string pattern = @"(?<![A-Za-z0-9_])" +
                             System.Text.RegularExpressions.Regex.Escape(word) +
                             @"(?![A-Za-z0-9_])";
            return System.Text.RegularExpressions.Regex.Replace(
                text, pattern, replacement,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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

        /// <summary>Builds a GitHub "new issue" URL from an explicit report body.</summary>
        public static string IssueUrlFromReport(Exception ex, string reportBody)
        {
            // The TITLE is sanitised too. It is built from the raw exception message,
            // which routinely contains a path ("could not open C:\Users\Alice\…"), and
            // unlike the body the user never sees it before the browser opens — the
            // crash window shows an editable box for the body only. So the one part
            // that could not be reviewed was the one part that was not cleaned.
            string title = Sanitize("Crash: " + (ex == null ? "unknown error" : Shorten(ex.Message, 80)));

            var body = new StringBuilder();
            body.AppendLine("**What I was doing when it happened:**");
            body.AppendLine("<!-- please describe, it really helps -->");
            body.AppendLine();
            body.AppendLine("**Details (edit out anything you don't want to share):**");
            body.AppendLine("```");
            body.AppendLine(Shorten(Sanitize(reportBody ?? string.Empty), 1500));
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

        /// <summary>
        /// The machine facts that make a report actionable, one per line.
        ///
        /// ONE copy, used by every channel. The GitHub body and the email body used to
        /// carry their own hand-written copies of the same eight lines, so adding a
        /// fact to one silently left the other without it — and nothing in the app
        /// would ever have said so.
        ///
        /// Everything here answers a question someone triaging actually asks. The GPU
        /// line explains a whole class of caption reports on its own; the install line
        /// separates "the single-file bundle didn't unpack" from a real bug; the
        /// language line matters because a translation that overflows its control is
        /// invisible to anyone reading in English. The session line is often the
        /// entire diagnosis.
        ///
        /// The result is sanitised by the callers, which is why the log's FOLDER is
        /// named rather than its full path: sanitising a path leaves
        /// "C:\Users\&lt;user&gt;\…", which is no longer somewhere the sender can click.
        /// Pointing at the button that opens it is both private and more useful.
        /// </summary>
        public static string BuildDiagnostics()
        {
            var d = new StringBuilder();
            d.AppendLine("- Tempo: " + CurrentVersion);
            d.AppendLine("- Windows: " + Safe(() => RuntimeInformation.OSDescription));
            d.AppendLine("- Architecture: " + Safe(() => RuntimeInformation.OSArchitecture +
                " (process " + RuntimeInformation.ProcessArchitecture + ")"));
            d.AppendLine("- .NET runtime: " + Safe(() => RuntimeInformation.FrameworkDescription));
            d.AppendLine("- Processors: " + Safe(() => Environment.ProcessorCount.ToString()));
            d.AppendLine("- Memory in use: " + Safe(() =>
                (Environment.WorkingSet / (1024 * 1024)) + " MB"));
            d.AppendLine("- Displays: " + Safe(DescribeScreens));
            d.AppendLine("- Display refresh: " + Safe(() =>
            {
                int hz = EnvironmentInfo.GetPrimaryRefreshHz();
                return hz > 0 ? hz + " Hz" : "unknown";
            }));
            d.AppendLine("- Display scale: " + Safe(() => EnvironmentInfo.GetDisplayScaleText() ?? "unknown"));
            d.AppendLine("- Language: " + Safe(() => Localization.Current.ToString()));
            d.AppendLine("- GPU engine: " + Safe(() => VulkanProbe.Summary));
            d.AppendLine("- Install: " + Safe(() => SelfCheck.Summary));
            d.AppendLine("- Session: " + Safe(() =>
                Logger.WarnCount + " warning(s), " + Logger.ErrorCount + " error(s)"));

            // The most recent warning is very often the bug itself, and it is the one
            // line a reporter could not find on their own.
            string lastWarn = Safe(() => Logger.LastWarn);
            if (!string.IsNullOrWhiteSpace(lastWarn) && lastWarn != "(unavailable)")
            {
                d.AppendLine("- Last warning: " + Shorten(lastWarn.Replace("\r", " ").Replace("\n", " "), 160));
            }
            d.AppendLine("- Log file: Tempo → Settings → \"Open log file\" (please attach if relevant)");
            return d.ToString();
        }

        /// <summary>"1920x1080 primary + 1 more" — size and count, no device names.</summary>
        private static string DescribeScreens()
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens == null || screens.Length == 0)
            {
                return "unknown";
            }
            System.Windows.Forms.Screen primary = System.Windows.Forms.Screen.PrimaryScreen ?? screens[0];
            string s = primary.Bounds.Width + "x" + primary.Bounds.Height + " primary";
            if (screens.Length > 1)
            {
                s += " + " + (screens.Length - 1) + " more";
            }
            return s;
        }

        /// <summary>A blank bug-report issue link for proactive (non-crash) reports.</summary>
        public static string BuildBlankIssueUrl()
        {
            return IssueUrlFromBody(BlankReportBody());
        }

        /// <summary>
        /// A GitHub issue link carrying exactly <paramref name="body"/> — the text the
        /// user reviewed and possibly edited, not a body rebuilt behind their back.
        /// </summary>
        public static string IssueUrlFromBody(string body)
        {
            return "https://github.com/" + Repository + "/issues/new"
                + "?labels=bug"
                + "&title=" + Uri.EscapeDataString("Bug report")
                + "&body=" + Uri.EscapeDataString(Sanitize(body ?? string.Empty));
        }

        /// <summary>Builds a "mailto:" URL from an explicit report body.</summary>
        public static string MailtoUrlFromReport(Exception ex, string reportBody)
        {
            // Sanitised for the same reason as the issue title above: the subject line
            // is built from the exception message and is never shown for review.
            string subject = Sanitize("Tempo bug: " + (ex == null ? "unknown error" : Shorten(ex.Message, 80)));

            var body = new StringBuilder();
            body.AppendLine("What I was doing when it happened:");
            body.AppendLine();
            body.AppendLine();
            body.AppendLine("--- details (edit out anything you don't want to share) ---");
            body.AppendLine(Shorten(Sanitize(reportBody ?? string.Empty), 1200));
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
        public static string BuildBlankMailtoUrl() { return BuildMailtoUrlFor(BlankReportBody()); }

        /// <summary>Opens a Gmail "compose" window in the browser, pre-filled.</summary>
        public static string BuildGmailComposeUrl() { return BuildGmailComposeUrlFor(BlankReportBody()); }

        /// <summary>Opens an Outlook-on-the-web "compose" window in the browser, pre-filled.</summary>
        public static string BuildOutlookComposeUrl() { return BuildOutlookComposeUrlFor(BlankReportBody()); }

        /// <summary>Opens a Yahoo Mail "compose" window in the browser, pre-filled.</summary>
        public static string BuildYahooComposeUrl() { return BuildYahooComposeUrlFor(BlankReportBody()); }

        // Each channel in a "…For(body)" form as well, so the composer can send the
        // exact text the user reviewed. Without these the preview would be theatre:
        // the user edits a box and the app sends something it rebuilt afterwards.

        /// <summary>
        /// The practical ceiling on a "mailto:" URL. Windows hands it to the mail
        /// client through a command line, so anything longer is TRUNCATED rather than
        /// refused — the report simply arrives cut off mid-sentence, and neither the
        /// sender nor the reader has any way to tell. In a bug report that is the
        /// worst possible way to fail.
        ///
        /// It matters now because the report can carry the activity log: the template
        /// alone builds a ~1 KB URL, but with 25 log lines attached it reaches ~5.5 KB.
        /// </summary>
        public const int MailtoUrlLimit = 2000;

        /// <summary>True when this body is too long for the user's mail app to carry intact.</summary>
        public static bool MailtoWouldTruncate(string body)
        {
            return BuildMailtoUrlFor(body).Length > MailtoUrlLimit;
        }

        public static string BuildMailtoUrlFor(string body)
        {
            return "mailto:" + SupportEmail
                + "?subject=" + Uri.EscapeDataString(BlankReportSubject())
                + "&body=" + Uri.EscapeDataString(Sanitize(body ?? string.Empty));
        }

        public static string BuildGmailComposeUrlFor(string body)
        {
            return "https://mail.google.com/mail/?view=cm&fs=1"
                + "&to=" + Uri.EscapeDataString(SupportEmail)
                + "&su=" + Uri.EscapeDataString(BlankReportSubject())
                + "&body=" + Uri.EscapeDataString(Sanitize(body ?? string.Empty));
        }

        public static string BuildOutlookComposeUrlFor(string body)
        {
            return "https://outlook.office.com/mail/deeplink/compose"
                + "?to=" + Uri.EscapeDataString(SupportEmail)
                + "&subject=" + Uri.EscapeDataString(BlankReportSubject())
                + "&body=" + Uri.EscapeDataString(Sanitize(body ?? string.Empty));
        }

        public static string BuildYahooComposeUrlFor(string body)
        {
            return "https://compose.mail.yahoo.com/?"
                + "to=" + Uri.EscapeDataString(SupportEmail)
                + "&subject=" + Uri.EscapeDataString(BlankReportSubject())
                + "&body=" + Uri.EscapeDataString(Sanitize(body ?? string.Empty));
        }

        /// <summary>Plain-text report (recipient + subject + body) for copying to the clipboard.</summary>
        public static string BuildBlankReportText() { return BuildReportTextFor(BlankReportBody()); }

        /// <summary>Clipboard form of an explicit body.</summary>
        public static string BuildReportTextFor(string body)
        {
            return Sanitize(
                "To: " + SupportEmail + Environment.NewLine
                + "Subject: " + BlankReportSubject() + Environment.NewLine
                + Environment.NewLine
                + (body ?? string.Empty));
        }

        /// <summary>
        /// The tail of the log, sanitised, for a reporter who chooses to include it.
        ///
        /// OPT-IN, and no longer bolted onto the clipboard report automatically. This
        /// is by far the most revealing thing Tempo can attach: the log records file
        /// paths from macro and settings import/export, update download locations and
        /// the window Tempo was acting on, so on a real machine it can carry a
        /// document name or a folder from someone's work. It is also genuinely useful,
        /// which is why it is offered rather than removed — but the person sending it
        /// should be the one who decides, after seeing it.
        /// </summary>
        public static string RecentLogTail(int maxLines)
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
                return Sanitize(sb.ToString());
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

        /// <summary>The pre-filled report template, diagnostics included.</summary>
        public static string BlankReportBody()
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
            body.Append(BuildDiagnostics());
            return Sanitize(body.ToString());
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
