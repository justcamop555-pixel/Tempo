using System;
using System.IO;
using System.Windows.Forms;

namespace AutoClicker.Utils
{
    /// <summary>How the running copy of Tempo came to be on this PC.</summary>
    public enum DeploymentKind
    {
        /// <summary>Running from the folder Tempo installs into.</summary>
        Installed,

        /// <summary>Running from anywhere else — a folder the user unzipped into, a USB stick.</summary>
        Portable,

        /// <summary>
        /// Portable, but Tempo is ALSO installed on this PC. Offering to install this copy would
        /// overwrite that one, and an old download installed over a newer install is a downgrade.
        /// </summary>
        PortableWithInstalledCopy,

        /// <summary>
        /// Running from a temporary folder Windows cleans up — almost always Tempo.exe double-clicked
        /// INSIDE the downloaded zip, which Explorer runs from a scratch copy under %TEMP%. Nothing
        /// may be registered or installed from here: the path is gone once Explorer tidies up.
        /// </summary>
        Transient
    }

    /// <summary>
    /// Works out whether Tempo is running from its installed location or as a portable copy.
    ///
    /// Settings, profiles, macros and stats live in %LOCALAPPDATA%\AutoClicker whichever it is (see
    /// SettingsManager), so installing or moving the exe never moves anyone's data.
    ///
    /// This used to compare the running folder against %LOCALAPPDATA%\Programs\Tempo — the folder
    /// installers used BEFORE install.cmd moved to Programs\TempoClicker (a path ending in
    /// "tempo\tempo.exe" makes Discord show the user as playing the Steam game "Tempo"). Nothing
    /// updated this class, so every real install from then on was classified as PORTABLE: the About
    /// box said "Portable", and Settings showed installed users the portable paragraph telling them
    /// to run install.cmd to get into Settings › Apps — which they already were.
    /// </summary>
    public static class DeploymentInfo
    {
        private const string InstallFolderName = "TempoClicker";
        private const string LegacyInstallFolderName = "Tempo";

        private static string LocalAppData
        {
            get
            {
                try { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
                catch { return string.Empty; }
            }
        }

        /// <summary>
        /// The Tempo.exe the user actually launched. Environment.ProcessPath is the real host exe and
        /// is correct for single-file builds; Application.ExecutablePath can instead name the
        /// temporary extraction folder. Falls back only if ProcessPath is unavailable.
        /// </summary>
        public static string ExecutablePath
        {
            get
            {
                try
                {
                    string exe = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exe))
                    {
                        exe = Application.ExecutablePath;
                    }
                    return exe ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        /// <summary>The directory the running Tempo.exe lives in.</summary>
        public static string ExecutableDirectory
        {
            get
            {
                try { return Path.GetDirectoryName(ExecutablePath) ?? string.Empty; }
                catch { return string.Empty; }
            }
        }

        /// <summary>The folder Tempo installs into (what install.cmd uses).</summary>
        public static string InstalledDirectory =>
            Path.Combine(LocalAppData, "Programs", InstallFolderName);

        /// <summary>Where installers put Tempo before the Discord rename. Still counts as installed.</summary>
        public static string LegacyInstalledDirectory =>
            Path.Combine(LocalAppData, "Programs", LegacyInstallFolderName);

        /// <summary>The installed Tempo.exe, whether or not it exists.</summary>
        public static string InstalledExePath => Path.Combine(InstalledDirectory, "Tempo.exe");

        /// <summary>How the running copy is deployed. Re-evaluated on every read — installing changes it.</summary>
        public static DeploymentKind Kind
        {
            get
            {
                try
                {
                    bool installedCopyExists =
                        File.Exists(InstalledExePath) ||
                        File.Exists(Path.Combine(LegacyInstalledDirectory, "Tempo.exe"));
                    return Classify(ExecutableDirectory, LocalAppData, TempFolders(), installedCopyExists);
                }
                catch
                {
                    return DeploymentKind.Transient;   // unknown: never register or install from it
                }
            }
        }

        /// <summary>True when Tempo is running from its installed location.</summary>
        public static bool IsInstalled => Kind == DeploymentKind.Installed;

        /// <summary>True when running as a portable copy (not the installed one).</summary>
        public static bool IsPortable => !IsInstalled;

        /// <summary>True when this copy may offer to install itself.</summary>
        public static bool CanOfferInstall => Kind == DeploymentKind.Portable;

        /// <summary>
        /// Classifies a running folder. Pure — every input is a parameter — so each case can be
        /// tested directly, without moving a real Tempo.exe around or touching the real install.
        /// </summary>
        /// <param name="exeDir">The folder the running exe is in.</param>
        /// <param name="localAppData">%LOCALAPPDATA%.</param>
        /// <param name="tempFolders">Temporary folders; a copy running under any of them is Transient.</param>
        /// <param name="installedCopyExists">Whether an installed Tempo.exe exists on this PC.</param>
        public static DeploymentKind Classify(string exeDir, string localAppData,
                                              string[] tempFolders, bool installedCopyExists)
        {
            string dir = Normalize(exeDir);
            if (dir.Length == 0)
            {
                return DeploymentKind.Transient;
            }

            string appData = Normalize(localAppData);
            if (appData.Length > 0)
            {
                string programs = Path.Combine(appData, "Programs");
                if (SamePath(dir, Path.Combine(programs, InstallFolderName)) ||
                    SamePath(dir, Path.Combine(programs, LegacyInstallFolderName)))
                {
                    return DeploymentKind.Installed;
                }
            }

            if (tempFolders != null)
            {
                foreach (string temp in tempFolders)
                {
                    if (IsUnder(dir, temp))
                    {
                        return DeploymentKind.Transient;
                    }
                }
            }

            return installedCopyExists ? DeploymentKind.PortableWithInstalledCopy : DeploymentKind.Portable;
        }

        /// <summary>Every name this user's temporary folder goes by.</summary>
        private static string[] TempFolders()
        {
            string a = "", b = "", c = "", d = "";
            try { a = Path.GetTempPath(); } catch { }
            try { b = Environment.GetEnvironmentVariable("TEMP") ?? ""; } catch { }
            try { c = Environment.GetEnvironmentVariable("TMP") ?? ""; } catch { }
            try { d = Path.Combine(LocalAppData, "Temp"); } catch { }
            return new[] { a, b, c, d };
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) { return string.Empty; }
            try
            {
                return Path.GetFullPath(path.Trim())
                           .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private static bool SamePath(string a, string b) =>
            string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when <paramref name="dir"/> is <paramref name="root"/> or inside it. Compared with a
        /// trailing separator so C:\Temp2 is not taken to be inside C:\Temp.
        /// </summary>
        private static bool IsUnder(string dir, string root)
        {
            string r = Normalize(root);
            if (r.Length == 0) { return false; }
            string d = Normalize(dir);
            if (d.Equals(r, StringComparison.OrdinalIgnoreCase)) { return true; }
            return d.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The note Settings shows a portable copy that can install itself, or null for any other
        /// kind (installed copies need no note; the other portable kinds get their own below).
        /// </summary>
        public static string PortableNote
        {
            get
            {
                if (Kind != DeploymentKind.Portable) return null;
                return PortableNoteText;
            }
        }

        /// <summary>The portable paragraph. A translation key — keep it byte-for-byte in step with the tables.</summary>
        public const string PortableNoteText =
            "Running as a portable copy - just run Tempo.exe, no install needed. " +
            "Your settings, profiles, macros and stats are saved in your user " +
            "AppData folder (%LOCALAPPDATA%\\AutoClicker), the same place an " +
            "installed copy uses, so saving always works even from a USB stick or " +
            "a read-only folder. \u201cStart with Windows\u201d and in-app updates point " +
            "at this exe's current location, so re-enable those if you move it. To get " +
            "a Start Menu entry and an entry in Settings \u203a Apps, press " +
            "\u201cInstall Tempo on this PC\u201d below.";

        /// <summary>For <see cref="DeploymentKind.PortableWithInstalledCopy"/>. {0} is the installed folder.</summary>
        public const string SeparateCopyNoteText =
            "This is a separate copy of Tempo. Tempo is also installed on this PC, in {0} \u2014 " +
            "that is the copy listed in Settings \u203a Apps and the Start Menu. Your settings, " +
            "profiles and macros are shared by both.";

        /// <summary>For <see cref="DeploymentKind.Transient"/>.</summary>
        public const string TransientNoteText =
            "Tempo is running from a temporary folder \u2014 most likely straight out of the zip you " +
            "downloaded. Windows deletes that folder, so Tempo can't be installed from here. Extract " +
            "the zip first, then open Tempo.exe from the folder you extracted it to.";
    }
}
