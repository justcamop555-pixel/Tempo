using System;
using System.IO;
using System.Windows.Forms;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Works out whether Tempo is running from its installed location (the per-user
    /// Programs folder the installer uses) or as a portable copy somewhere else
    /// (a folder you unzipped, a USB stick, etc.).
    ///
    /// A portable copy is just Tempo.exe with its native .dll files beside it, run in
    /// place - nothing is installed. Portable copies also keep their data in a "Data"
    /// folder next to the exe (see SettingsManager) so the whole thing travels together.
    /// </summary>
    public static class DeploymentInfo
    {
        /// <summary>The directory the running Tempo.exe lives in.</summary>
        public static string ExecutableDirectory
        {
            get
            {
                try
                {
                    // Environment.ProcessPath is the real host exe path and is correct
                    // for single-file builds (Application.ExecutablePath can point at a
                    // temporary extraction folder). Fall back if it's unavailable.
                    string exe = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exe))
                    {
                        exe = Application.ExecutablePath;
                    }
                    return Path.GetDirectoryName(exe) ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        /// <summary>The folder the installer copies Tempo into.</summary>
        public static string InstalledDirectory =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Tempo");

        /// <summary>True when Tempo appears to be running from its installed location.</summary>
        public static bool IsInstalled
        {
            get
            {
                try
                {
                    string exeDir = ExecutableDirectory;
                    if (string.IsNullOrEmpty(exeDir))
                    {
                        return false;
                    }
                    return string.Equals(
                        exeDir.TrimEnd(Path.DirectorySeparatorChar),
                        InstalledDirectory.TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>True when running as a portable copy (not the installed one).</summary>
        public static bool IsPortable => !IsInstalled;

        /// <summary>
        /// A short, honest note about running portably, or null when installed.
        /// Surfaced in Settings so users understand how a portable copy behaves.
        /// </summary>
        public static string PortableNote
        {
            get
            {
                if (IsInstalled) return null;
                return "Running as a portable copy - just run Tempo.exe, no install needed. " +
                       "Your settings, profiles, macros and stats are saved in your user " +
                       "AppData folder (%LOCALAPPDATA%\\AutoClicker), the same place an " +
                       "installed copy uses, so saving always works even from a USB stick or " +
                       "a read-only folder. Keep the \u201cruntimes\u201d folder next to " +
                       "Tempo.exe (it holds the offline speech-caption engine). \u201cStart " +
                       "with Windows\u201d and in-app updates point at this exe's current " +
                       "location, so re-enable those if you move it. For a Start Menu entry " +
                       "and an entry in Settings \u203a Apps, run install.cmd.";
            }
        }
    }
}
