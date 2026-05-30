using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Collects and logs a snapshot of the host environment at startup. Having this
    /// in the log file makes it far easier to understand bug reports (DPI scaling,
    /// multi-monitor layouts, OS build and so on) without asking the user to dig the
    /// information out by hand.
    /// </summary>
    public static class EnvironmentInfo
    {
        /// <summary>Writes a multi-line environment summary to the log.</summary>
        public static void LogSummary()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Environment summary:");
                sb.AppendLine("  OS              : " + SafeOsDescription());
                sb.AppendLine("  Architecture    : " + RuntimeInformation.OSArchitecture +
                              " (process: " + RuntimeInformation.ProcessArchitecture + ")");
                sb.AppendLine("  Runtime         : " + SafeFrameworkDescription());
                sb.AppendLine("  Processors      : " + Environment.ProcessorCount);
                sb.AppendLine("  64-bit process  : " + Environment.Is64BitProcess);
                sb.AppendLine("  Culture         : " + CultureInfo.CurrentCulture.Name);
                sb.AppendLine("  Monitors        : " + Screen.AllScreens.Length);

                int index = 0;
                foreach (Screen s in Screen.AllScreens)
                {
                    sb.AppendLine(
                        $"    [{index}] {s.Bounds.Width}x{s.Bounds.Height} @ ({s.Bounds.X},{s.Bounds.Y})" +
                        (s.Primary ? " (primary)" : string.Empty));
                    index++;
                }

                var vs = SystemInformation.VirtualScreen;
                sb.AppendLine($"  Virtual desktop : {vs.Width}x{vs.Height} @ ({vs.X},{vs.Y})");
                sb.Append("  Working set     : " + (Environment.WorkingSet / (1024 * 1024)) + " MB");

                Logger.Info(sb.ToString());
            }
            catch (Exception ex)
            {
                // Diagnostics must never interfere with startup.
                Logger.Warn("Could not gather environment info: " + ex.Message);
            }
        }

        private static string SafeOsDescription()
        {
            try
            {
                return RuntimeInformation.OSDescription;
            }
            catch
            {
                return Environment.OSVersion.ToString();
            }
        }

        private static string SafeFrameworkDescription()
        {
            try
            {
                return RuntimeInformation.FrameworkDescription;
            }
            catch
            {
                return "Unknown (" + Environment.Version + ")";
            }
        }
    }
}
