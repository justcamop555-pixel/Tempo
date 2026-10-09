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

        /// <summary>
        /// The INSTALLED program's file name — deliberately not "Tempo.exe".
        ///
        /// Discord's game detection lists the Steam game "Tempo" (Steam app 1445240, Discord
        /// application 1124354831117729802) as tempo/tempo.exe, and shows anyone running a matching
        /// program as playing it. Moving the install into a folder called TempoClicker was meant to
        /// break that match and did not: Discord went on showing Tempo users as playing that game.
        /// A different FILE name cannot match however Discord compares folders. Release downloads are
        /// still called Tempo.exe (the release process, the web installer and every older copy's
        /// updater look for that name); installing names the copy this, and an install made before is
        /// renamed on its next start (<see cref="RenameInstalledExe"/>).
        /// </summary>
        public const string InstalledExeName = "TempoClicker.exe";

        /// <summary>What the installed program was called before, and what downloads are still called.</summary>
        public const string LegacyExeName = "Tempo.exe";

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

        /// <summary>The installed program, whether or not it exists.</summary>
        public static string InstalledExePath => Path.Combine(InstalledDirectory, InstalledExeName);

        /// <summary>How the running copy is deployed. Re-evaluated on every read — installing changes it.</summary>
        public static DeploymentKind Kind
        {
            get
            {
                try
                {
                    bool installedCopyExists =
                        File.Exists(InstalledExePath) ||
                        File.Exists(Path.Combine(InstalledDirectory, LegacyExeName)) ||
                        File.Exists(Path.Combine(LegacyInstalledDirectory, LegacyExeName)) ||
                        File.Exists(Path.Combine(LegacyInstalledDirectory, InstalledExeName));
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
        /// Renames an installed copy still called Tempo.exe to <see cref="InstalledExeName"/>, and moves
        /// what points at it — the sign-in entry, Start Menu and Desktop shortcuts, taskbar pins — along
        /// with it. True when it did: the caller must then hand over to <paramref name="renamedTo"/>.
        /// To Discord THIS process is still Tempo.exe. Measured (scratchpad/installtest) on a running
        /// program whose file was renamed: QueryFullProcessImageName and .NET's ProcessName follow the
        /// new name, but GetProcessImageFileName — the call Discord's process scan is documented to use —
        /// goes on returning the name the process started under until it exits.
        ///
        /// Windows lets a running program's file be renamed (though not deleted), which is what makes
        /// this possible from inside. Only ever the INSTALLED copy: a portable one is the user's file,
        /// where they put it, under the name they gave it. Never when both names are present — that
        /// is not a state Tempo makes, so nothing is guessed about which file is which.
        /// </summary>
        public static bool RenameInstalledExe(out string renamedTo)
        {
            renamedTo = null;
            try
            {
                string exe = ExecutablePath;
                if (string.IsNullOrEmpty(exe) || !IsInstalled) { return false; }
                if (!RenameExeFile(exe, InstalledExeName, out string target)) { return false; }
                renamedTo = target;
                int moved = RepointToRenamedExe(exe, target);
                Logger.Info("[Install] renamed the installed " + LegacyExeName + " to " + InstalledExeName
                            + " so Discord no longer takes Tempo for the Steam game \"Tempo\"; "
                            + moved + " shortcut(s) and sign-in entry moved with it.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn("[Install] could not rename the installed program: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// The rename itself, apart from anything Windows-wide, so it can be tested on throwaway files:
        /// renames <paramref name="exe"/> — if it is called <see cref="LegacyExeName"/> — to
        /// <paramref name="newName"/> in the same folder. False, touching nothing, for any other name or
        /// when the new name is already taken.
        /// </summary>
        internal static bool RenameExeFile(string exe, string newName, out string target)
        {
            target = null;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) { return false; }
            if (!string.Equals(Path.GetFileName(exe), LegacyExeName, StringComparison.OrdinalIgnoreCase)) { return false; }
            string dir = Path.GetDirectoryName(exe);
            if (string.IsNullOrEmpty(dir)) { return false; }
            string to = Path.Combine(dir, newName);
            if (File.Exists(to))
            {
                Logger.Warn("[Install] both " + LegacyExeName + " and " + newName + " are in " + dir
                            + " — left as they are.");
                return false;
            }
            File.Move(exe, to);
            target = to;
            return true;
        }

        /// <summary>
        /// For the installed copy running under its new name: shortcuts, taskbar pins and the sign-in
        /// entry still aimed at the old <see cref="LegacyExeName"/> beside it — which install.cmd leaves
        /// behind when it renames by installing the new name — move to this file. Only when that old
        /// file is really gone: a real Tempo.exe there is not a leftover to point away from. Runs on
        /// its own STA thread (shell links are apartment-threaded), off the startup path.
        /// </summary>
        public static void RepointLeftoversOfOldNameInBackground()
        {
            try
            {
                var t = new System.Threading.Thread(() =>
                {
                    try
                    {
                        string exe = ExecutablePath;
                        if (string.IsNullOrEmpty(exe) || !IsInstalled ||
                            !string.Equals(Path.GetFileName(exe), InstalledExeName, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                        string old = Path.Combine(Path.GetDirectoryName(exe) ?? "", LegacyExeName);
                        if (File.Exists(old)) { return; }
                        int moved = RepointToRenamedExe(old, exe);
                        if (moved > 0)
                        {
                            Logger.Info("[Install] " + moved + " shortcut(s) still opening the old " + LegacyExeName
                                        + " now open " + InstalledExeName + ".");
                        }
                    }
                    catch (Exception ex) { Logger.Swallow("RepointLeftoversOfOldName", ex); }
                });
                t.SetApartmentState(System.Threading.ApartmentState.STA);
                t.IsBackground = true;
                t.Start();
            }
            catch (Exception ex) { Logger.Swallow("RepointLeftoversOfOldName (thread)", ex); }
        }

        /// <summary>
        /// Moves the sign-in entry and every Start Menu / Desktop shortcut and taskbar pin that opens
        /// <paramref name="oldExe"/> over to <paramref name="newExe"/>. Anything pointing elsewhere is
        /// left alone. Also run by the installed copy at every start (off the startup path), because
        /// install.cmd renames by installing the new name — and a pin made for the old file would
        /// otherwise open nothing. Returns how many shortcuts moved.
        /// </summary>
        public static int RepointToRenamedExe(string oldExe, string newExe)
        {
            int moved = 0;
            try
            {
                string registered = StartupManager.ExePathFromCommand(StartupManager.RegisteredCommand());
                if (!string.IsNullOrEmpty(registered) &&
                    string.Equals(Normalize(registered), Normalize(oldExe), StringComparison.OrdinalIgnoreCase))
                {
                    StartupManager.RepointTo(newExe);
                }
            }
            catch (Exception ex) { Logger.Swallow("RepointToRenamedExe (sign-in entry)", ex); }
            try { moved = SelfInstaller.RepointShortcuts(oldExe, newExe, SelfInstaller.ShortcutFolders()); }
            catch (Exception ex) { Logger.Swallow("RepointToRenamedExe (shortcuts)", ex); }
            return moved;
        }

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
