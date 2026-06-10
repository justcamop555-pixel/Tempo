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
                    int hz = GetRefreshHz(s.DeviceName);
                    string hzText = hz > 0 ? $" @ {hz} Hz" : string.Empty;
                    sb.AppendLine(
                        $"    [{index}] {s.Bounds.Width}x{s.Bounds.Height} @ ({s.Bounds.X},{s.Bounds.Y})" +
                        hzText +
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

        /// <summary>Refresh rate (Hz) of the primary monitor, or 0 if unavailable.</summary>
        public static int GetPrimaryRefreshHz()
        {
            try
            {
                return GetRefreshHz(Screen.PrimaryScreen?.DeviceName);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Refresh rate (Hz) of a specific display device, or 0 if unknown.</summary>
        public static int GetRefreshHz(string deviceName)
        {
            try
            {
                var dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm) &&
                    dm.dmDisplayFrequency > 1) // 0 or 1 means "default/unknown"
                {
                    return dm.dmDisplayFrequency;
                }
            }
            catch { }
            return 0;
        }

        private const int ENUM_CURRENT_SETTINGS = -1;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
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
