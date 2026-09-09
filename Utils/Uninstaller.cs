using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Removes Tempo from the machine. Because the app's own data folder holds the
    /// open log file (and the running exe is locked), the actual deletion is done
    /// by a tiny helper script that runs after Tempo exits.
    /// </summary>
    public static class Uninstaller
    {
        /// <summary>Where Windows keeps this user's Settings → Apps entries.</summary>
        private const string UninstallRoot =
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

        /// <summary>Tempo's own entry under <see cref="UninstallRoot"/>.</summary>
        private const string EntryName = "Tempo";

        /// <summary>
        /// Brings the version shown in Control Panel / Settings → Apps back in line with
        /// the exe that is actually installed.
        ///
        /// install.cmd writes DisplayVersion once, at install time, from the exe's
        /// FileVersion — and nothing ever wrote it again. Tempo updates itself by
        /// replacing Tempo.exe in place, which does not touch the registry, so the number
        /// Windows shows froze at whichever build was last put through the installer while
        /// the app moved on without it. On this machine that was 1.0.209.0 against an
        /// installed 1.0.320.0: a hundred and eleven releases of drift, and the one place
        /// a user looks to answer "what version do I have?" was the one place that lied.
        ///
        /// Deliberately only UPDATES an entry that already exists. Creating one would
        /// register a portable copy in Settings → Apps behind the user's back, complete
        /// with an Uninstall button pointing at an uninstall.cmd that was never installed
        /// — the exact broken state RemoveShellIntegration below exists to clean up.
        ///
        /// FileVersion, not Application.ProductVersion: the latter carries the git hash
        /// ("1.0.320+cc37b4d…"), which is the right thing in the About box and the wrong
        /// thing in a Windows version column. This matches what install.cmd would write.
        /// </summary>
        public static void RefreshRegisteredVersion()
        {
            try
            {
                DateTime? installedOnUtc = null;
                string version = FileVersionInfo
                    .GetVersionInfo(Application.ExecutablePath).FileVersion;
                if (string.IsNullOrWhiteSpace(version))
                {
                    return;
                }

                using (var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(UninstallRoot))
                {
                    if (root == null) { return; }

                    // Not installed (portable, or uninstalled) — nothing to correct.
                    using (var probe = root.OpenSubKey(EntryName))
                    {
                        if (probe == null) { return; }   // portable / not installed

                        // Read the key's timestamp HERE, through a read-only handle and
                        // before a single write below. This is the install date, and the
                        // first write in this method overwrites it forever.
                        installedOnUtc = KeyLastWriteUtc(probe);
                    }
                }

                using (var entry = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    UninstallRoot + "\\" + EntryName, writable: true))
                {
                    if (entry == null) { return; }

                    string was = entry.GetValue("DisplayVersion") as string;
                    bool versionChanged = !string.Equals(was, version, StringComparison.OrdinalIgnoreCase);
                    if (versionChanged)
                    {
                        entry.SetValue("DisplayVersion", version,
                                       Microsoft.Win32.RegistryValueKind.String);
                        Logger.Info("[Install] Settings > Apps version corrected: " +
                                    (string.IsNullOrEmpty(was) ? "(unset)" : was) + " -> " + version);
                    }

                    // Deliberately NOT gated on the version having changed. A broken
                    // Uninstall button on a machine that is already showing the right
                    // number would otherwise never be repaired — which is most of the
                    // installs that have one. Only the size walk is gated; everything
                    // else here is a couple of string compares that write nothing when
                    // they already agree.
                    RepairEntry(entry, version, versionChanged, installedOnUtc);
                }
            }
            catch (Exception ex)
            {
                // Never worth interrupting startup for a cosmetic registry value.
                Logger.Swallow("Uninstaller.RefreshRegisteredVersion", ex);
            }
        }

        [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet =
            System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern int RegQueryInfoKey(
            Microsoft.Win32.SafeHandles.SafeRegistryHandle hKey, IntPtr lpClass,
            IntPtr lpcchClass, IntPtr lpReserved, IntPtr lpcSubKeys,
            IntPtr lpcbMaxSubKeyLen, IntPtr lpcbMaxClassLen, IntPtr lpcValues,
            IntPtr lpcbMaxValueNameLen, IntPtr lpcbMaxValueLen,
            IntPtr lpcbSecurityDescriptor, out System.Runtime.InteropServices.ComTypes.FILETIME lpftLastWriteTime);

        /// <summary>
        /// When a registry key was last written, in UTC, or null if it cannot be read.
        ///
        /// The .NET RegistryKey class does not expose this, and it is the only record of
        /// when an installer created an entry — which is what Windows shows as
        /// "Installed on" for any app that never wrote an InstallDate value of its own.
        /// </summary>
        private static DateTime? KeyLastWriteUtc(Microsoft.Win32.RegistryKey key)
        {
            try
            {
                if (key == null) { return null; }
                if (RegQueryInfoKey(key.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero, IntPtr.Zero, out var ft) != 0)
                {
                    return null;
                }
                long ticks = ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
                if (ticks <= 0) { return null; }
                return DateTime.FromFileTimeUtc(ticks);
            }
            catch { return null; }
        }

        /// <summary>
        /// Brings the rest of the Settings → Apps entry up to date, alongside the version.
        ///
        /// Runs only when the version actually changed, because the size figure means
        /// walking a 200 MB install folder and that is not something to do on every
        /// launch for a cosmetic number.
        ///
        /// What each value fixes:
        ///  • UninstallString — the Uninstall button was DEAD. install.cmd copies
        ///    uninstall.cmd only if it finds one to copy, but wrote the registry value
        ///    unconditionally, so the button pointed at a file that was never created.
        ///    Repointed at the exe, which cannot go missing, and only when the .cmd is
        ///    genuinely absent — an install that has one keeps using it.
        ///  • QuietUninstallString — Windows 11's Settings → Apps prefers this and
        ///    uninstalls in place when it exists, instead of throwing up a console window.
        ///  • EstimatedSize — never written at all, so the size column was blank for a
        ///    200 MB app. DWORD, in KB, which is the unit Windows expects.
        ///  • InstallDate — never written, so Windows fell back to the key's own
        ///    last-write time. That fallback IS the real install date, and the moment
        ///    this method writes anything the key's timestamp becomes today — so the
        ///    date is captured before the first write and persisted once. Never from
        ///    the exe: that file is replaced on every update.
        ///  • InstallLocation / DisplayIcon — re-pointed at where the exe actually is, so
        ///    a moved install stops showing a stale path and a blank icon.
        ///  • VersionMajor / VersionMinor — what inventory and management tools read
        ///    instead of parsing DisplayVersion.
        /// </summary>
        private static void RepairEntry(Microsoft.Win32.RegistryKey entry, string version,
                                        bool recomputeSize, DateTime? installedOnUtc)
        {
            string exe = Application.ExecutablePath;
            string dir = Path.GetDirectoryName(exe) ?? "";

            SetIfDifferent(entry, "DisplayIcon", exe);
            if (dir.Length > 0) { SetIfDifferent(entry, "InstallLocation", dir); }

            // Only rescue a dangling uninstaller — never override a working one.
            string cmd = dir.Length > 0 ? Path.Combine(dir, "uninstall.cmd") : null;
            bool haveCmd = cmd != null && File.Exists(cmd);
            string uninstall = haveCmd ? "\"" + cmd + "\"" : "\"" + exe + "\" --uninstall";
            string existing = entry.GetValue("UninstallString") as string;
            bool existingWorks = LooksRunnable(existing);
            if (!existingWorks)
            {
                SetIfDifferent(entry, "UninstallString", uninstall);
                Logger.Info("[Install] repaired a dangling Uninstall button -> " + uninstall);
            }
            SetIfDifferent(entry, "QuietUninstallString",
                           haveCmd ? "\"" + cmd + "\"" : "\"" + exe + "\" --uninstall");

            try
            {
                var v = new Version(version);
                SetDwordIfDifferent(entry, "VersionMajor", v.Major);
                SetDwordIfDifferent(entry, "VersionMinor", v.Minor);
            }
            catch { /* an unparsable version is not worth a failure */ }

            SetIfDifferent(entry, "URLUpdateInfo", "https://justcamop555-pixel.github.io/Tempo/");
            SetIfDifferent(entry, "HelpLink", "https://justcamop555-pixel.github.io/Tempo/");

            // Written once, and from the only source that is actually the INSTALL date.
            //
            // Not the exe's creation time, which is what this used to read: Tempo replaces
            // Tempo.exe on every update, so that timestamp is the date of the last update
            // — or of whenever the install folder was last recreated. On the machine this
            // was found on it said 10 July against a real install date of 27 June, and it
            // overwrote a date Windows had been displaying correctly.
            //
            // Correctly is the word: with no InstallDate value, Windows falls back to the
            // KEY'S OWN last-write time, which for an untouched entry is exactly when the
            // installer created it. That is the real date — and this method destroys it,
            // because writing DisplayVersion updates the key's timestamp. So capture it
            // BEFORE any write in this method lands, and persist it once.
            if (entry.GetValue("InstallDate") == null && installedOnUtc.HasValue)
            {
                try
                {
                    entry.SetValue("InstallDate", installedOnUtc.Value.ToLocalTime()
                        .ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
                        Microsoft.Win32.RegistryValueKind.String);
                }
                catch { }
            }

            // The one expensive step: walking a 200 MB folder. Only when the version moved
            // (so the size plausibly did too) or when it was never recorded at all.
            if (!recomputeSize && entry.GetValue("EstimatedSize") != null)
            {
                return;
            }

            try
            {
                long bytes = 0;
                if (dir.Length > 0 && Directory.Exists(dir))
                {
                    foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        try { bytes += new FileInfo(f).Length; }
                        catch { }
                    }
                }
                if (bytes > 0)
                {
                    SetDwordIfDifferent(entry, "EstimatedSize", (int)Math.Min(int.MaxValue, bytes / 1024));
                }
            }
            catch (Exception ex) { Logger.Swallow("Uninstaller.EstimatedSize", ex); }
        }

        /// <summary>
        /// True when a registry command line names a file that is actually there.
        /// Handles the quoted form and a trailing switch, which is how these are written.
        /// </summary>
        private static bool LooksRunnable(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) { return false; }
            try
            {
                string s = command.Trim();
                string path;
                if (s.StartsWith("\"", StringComparison.Ordinal))
                {
                    int close = s.IndexOf('"', 1);
                    if (close < 0) { return false; }
                    path = s.Substring(1, close - 1);
                }
                else
                {
                    int space = s.IndexOf(' ');
                    path = space < 0 ? s : s.Substring(0, space);
                }
                return File.Exists(path);
            }
            catch { return false; }
        }

        private static void SetIfDifferent(Microsoft.Win32.RegistryKey key, string name, string value)
        {
            try
            {
                if (!string.Equals(key.GetValue(name) as string, value, StringComparison.Ordinal))
                {
                    key.SetValue(name, value, Microsoft.Win32.RegistryValueKind.String);
                }
            }
            catch { }
        }

        private static void SetDwordIfDifferent(Microsoft.Win32.RegistryKey key, string name, int value)
        {
            try
            {
                object current = key.GetValue(name);
                if (!(current is int i) || i != value)
                {
                    key.SetValue(name, value, Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        /// <summary>Removes the "launch at startup" registry entry, if present.</summary>
        public static void RemoveStartupEntry()
        {
            try
            {
                StartupManager.SetEnabled(false);
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not remove startup entry during uninstall: " + ex.Message);
            }
        }

        /// <summary>
        /// Removes what INSTALLING Tempo registered with Windows: the Start Menu and
        /// Desktop shortcuts, and the Settings → Apps entry.
        ///
        /// The shipped uninstall.cmd has always done this; the in-app "Uninstall Tempo…"
        /// never did — it removed the data folder, optionally the exe, and the run-at-
        /// login entry, and stopped there. So the obvious, discoverable way to uninstall
        /// left the machine in a state the batch file would not: dead shortcuts in the
        /// Start Menu and on the Desktop, and — worst of it — Tempo STILL LISTED in
        /// Windows Settings → Apps, whose Uninstall button pointed at an uninstall.cmd
        /// that no longer existed. The user believed Tempo was gone; Windows disagreed,
        /// and clearing it needed manual registry editing.
        ///
        /// Every step is best-effort and independent: a portable copy simply has none of
        /// these to remove, and one failure must not abort the rest of the uninstall.
        /// </summary>
        public static void RemoveShellIntegration()
        {
            // Same three targets, and the same paths, uninstall.cmd uses.
            TryDelete(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs", "Tempo.lnk"), "Start Menu shortcut");

            TryDelete(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "Tempo.lnk"), "Desktop shortcut");

            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    UninstallRoot, writable: true))
                {
                    if (key != null && key.OpenSubKey(EntryName) != null)
                    {
                        key.DeleteSubKeyTree(EntryName, throwOnMissingSubKey: false);
                        Logger.Info("[Uninstall] removed the Settings > Apps entry.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Uninstall: could not remove the Settings > Apps entry: " + ex.Message);
            }
        }

        private static void TryDelete(string path, string what)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Logger.Info("[Uninstall] removed the " + what + ".");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Uninstall: could not remove the " + what + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Writes and launches a helper that waits for this process to exit, deletes
        /// the data folder and (optionally) the program file, then removes itself.
        /// The caller should exit the application immediately after this returns true.
        /// </summary>
        public static bool LaunchCleanupAndExitHelper(bool deleteExe, out string error)
        {
            error = null;

            try
            {
                string dataDir = Persistence.SettingsManager.GetSettingsDirectory();
                string exe = deleteExe ? Application.ExecutablePath : string.Empty;
                int pid = Process.GetCurrentProcess().Id;
                string scriptPath = Path.Combine(Path.GetTempPath(),
                    "tempo_uninstall_" + Guid.NewGuid().ToString("N") + ".bat");

                string script =
                    "@echo off\r\n" +
                    "setlocal enabledelayedexpansion\r\n" +
                    "set \"DATA=%~1\"\r\n" +
                    "set \"EXE=%~2\"\r\n" +
                    "set \"PID=%~3\"\r\n" +
                    ":wait\r\n" +
                    "tasklist /fi \"PID eq %PID%\" 2>nul | find \"%PID%\" >nul\r\n" +
                    "if not errorlevel 1 ( ping -n 2 127.0.0.1 >nul & goto wait )\r\n" +
                    "if exist \"%DATA%\" rmdir /s /q \"%DATA%\" >nul 2>&1\r\n" +
                    "if not \"%EXE%\"==\"\" (\r\n" +
                    "  set /a n=0\r\n" +
                    "  :del\r\n" +
                    "  del /q \"%EXE%\" >nul 2>&1\r\n" +
                    "  if exist \"%EXE%\" ( set /a n+=1 & if !n! lss 15 ( ping -n 2 127.0.0.1 >nul & goto del ) )\r\n" +
                    // Tidy up what shipped ALONGSIDE the exe. Deleting Tempo.exe alone
                    // left the install folder behind holding Tempo.exe.sha256, the
                    // install/uninstall scripts, the readme and the runtimes folder —
                    // after a dialog that offered to "remove everything".
                    //
                    // ONLY these known names are touched, and the folder itself is then
                    // removed with a plain rd, which REFUSES to delete a non-empty
                    // directory. A portable copy living in Downloads or on the Desktop
                    // beside the user's own files therefore cannot lose anything: if
                    // anything we did not put there remains, the folder simply stays.
                    "  set \"DIR=%~dp2\"\r\n" +
                    "  del /q \"!DIR!Tempo.exe.sha256\" >nul 2>&1\r\n" +
                    "  del /q \"!DIR!INSTALL-README.txt\" >nul 2>&1\r\n" +
                    "  del /q \"!DIR!install.cmd\" >nul 2>&1\r\n" +
                    "  del /q \"!DIR!uninstall.cmd\" >nul 2>&1\r\n" +
                    "  if exist \"!DIR!runtimes\" rmdir /s /q \"!DIR!runtimes\" >nul 2>&1\r\n" +
                    "  rd \"!DIR!\" >nul 2>&1\r\n" +
                    ")\r\n" +
                    "del /q \"%~f0\" >nul 2>&1\r\n";

                File.WriteAllText(scriptPath, script);

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"\"" + scriptPath + "\" \"" + dataDir + "\" \"" + exe + "\" " + pid + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(psi);
                Logger.Info("[Uninstall] cleanup helper launched (deleteExe=" + deleteExe + ").");
                return true;
            }
            catch (Exception ex)
            {
                error = "Couldn't start the uninstaller: " + ex.Message;
                Logger.Warn("Uninstall helper failed: " + ex.Message);
                return false;
            }
        }
    }
}
