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

            /// <summary>
            /// Things worth saying but not worth refusing to start over — an outdated but
            /// probably-workable Windows, for instance. Kept separate from Problems so
            /// Satisfied stays "can Tempo run at all".
            /// </summary>
            public List<string> Warnings { get; } = new List<string>();

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
                        $"(This PC is {WindowsVersion.Describe()}.)");
                }
                else if (WindowsVersion.Status == WindowsVersion.Support.Outdated)
                {
                    // A WARNING, not a problem: it is recorded and shown, but it does not
                    // stop Tempo starting.
                    //
                    // This gap was silent. The check only ever refused Major < 10, so every
                    // Windows 10 back to 1507 walked straight past it — while the app's own
                    // manifest declares a floor of 10.0.17763. Those builds start and then
                    // fail at whichever API they lack, which the user reads as "Tempo is
                    // broken" rather than "this Windows is too old". Blocking them instead
                    // would be worse: they may well work, and refusing to run on a machine
                    // that could is not ours to decide.
                    Logger.Warn("[OS] " + WindowsVersion.Describe() + " — " +
                                WindowsVersion.SupportNote());
                    result.Warnings.Add(WindowsVersion.SupportNote());
                }
                else
                {
                    Logger.Info("[OS] " + WindowsVersion.Describe());
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
                    Localization.F("Couldn't open your browser automatically. Please visit:\n\n{0}", DotNetDownloadUrl),
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
