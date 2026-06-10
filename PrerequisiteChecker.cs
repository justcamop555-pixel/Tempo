using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Verifies that the host meets Tempo's runtime requirements before the main
    /// window is created. When something is missing the app does not crash with a
    /// cryptic error — it explains exactly what to install by hand and offers to
    /// open the download page.
    /// </summary>
    public static class PrerequisiteChecker
    {
        /// <summary>Official download page for the .NET 8 Desktop Runtime.</summary>
        public const string DotNetDownloadUrl = "https://dotnet.microsoft.com/download/dotnet/8.0";

        private const int RequiredDotNetMajor = 8;
        private const int MinWindowsMajor = 10;

        public sealed class Result
        {
            public List<string> Problems { get; } = new List<string>();
            public bool Satisfied => Problems.Count == 0;
        }

        /// <summary>Runs every check and returns the collected problems (if any).</summary>
        public static Result Check()
        {
            var result = new Result();

            // ── Operating system ────────────────────────────────────────────────
            try
            {
                OperatingSystem os = Environment.OSVersion;
                bool isWindows = os.Platform == PlatformID.Win32NT;

                if (!isWindows)
                {
                    result.Problems.Add(
                        "Tempo runs on Windows only — it relies on Win32 input APIs that " +
                        "are not available on this operating system.");
                }
                else if (os.Version.Major < MinWindowsMajor)
                {
                    result.Problems.Add(
                        "Tempo requires Windows 10 or Windows 11. Older versions such as " +
                        "Windows 7 and Windows 8.1 are not supported, because Tempo runs on " +
                        ".NET 8, which itself requires Windows 10 (version 1607) or newer. " +
                        $"(This PC reports Windows version {os.Version}.)");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("OS prerequisite check failed: " + ex.Message);
            }

            // ── .NET runtime ────────────────────────────────────────────────────
            // A framework-dependent build cannot even start without a runtime, so
            // reaching here means *a* runtime is present. This guards the case where
            // it is present but too old (e.g. a bad roll-forward onto .NET Framework
            // or an earlier .NET version).
            try
            {
                if (Environment.Version.Major < RequiredDotNetMajor)
                {
                    result.Problems.Add(
                        $"The .NET {RequiredDotNetMajor} Desktop Runtime is required " +
                        $"(the active runtime is {DescribeRuntime()}).");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(".NET prerequisite check failed: " + ex.Message);
            }

            if (!result.Satisfied)
            {
                Logger.Warn("Prerequisite check found problems: " +
                            string.Join(" | ", result.Problems));
            }

            return result;
        }

        /// <summary>
        /// Shows the problems to the user, tells them to install the missing
        /// prerequisite manually, and offers to open the download page.
        /// </summary>
        public static void ReportAndAdvise(Result result)
        {
            if (result == null || result.Satisfied)
            {
                return;
            }

            string body =
                "Tempo can't start because the following requirement(s) are not met:\n\n  • " +
                string.Join("\n  • ", result.Problems) +
                "\n\nPlease install the required software manually, then start Tempo again.\n\n" +
                "The .NET 8 Desktop Runtime can be downloaded from:\n" + DotNetDownloadUrl +
                "\n\nOpen that download page now?";

            DialogResult choice = MessageBox.Show(
                body,
                "Tempo — setup required",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (choice == DialogResult.Yes)
            {
                OpenDownloadPage();
            }
        }

        private static void OpenDownloadPage()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = DotNetDownloadUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not open the download page: " + ex.Message);
                MessageBox.Show(
                    "Couldn't open your browser automatically. Please visit:\n\n" + DotNetDownloadUrl,
                    "Tempo",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private static string DescribeRuntime()
        {
            try
            {
                return RuntimeInformation.FrameworkDescription;
            }
            catch
            {
                return "version " + Environment.Version;
            }
        }
    }
}
