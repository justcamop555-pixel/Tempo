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
        /// Runs only for the INSTALLED copy (see DeploymentInfo): it maintains the entry, and
        /// creates it when an installed copy has none. A portable copy writes nothing at all —
        /// neither registering itself behind the user's back, nor re-pointing an installed
        /// app's existing entry at itself, which it used to do on every launch.
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
                    .GetVersionInfo(RunningExe()).FileVersion;
                if (string.IsNullOrWhiteSpace(version))
                {
                    return;
                }

                bool hasEntry = false;
                using (var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(UninstallRoot))
                {
                    if (root != null)
                    {
                        using (var probe = root.OpenSubKey(EntryName))
                        {
                            if (probe != null)
                            {
                                hasEntry = true;
                                // Read the key's timestamp HERE, through a read-only handle and
                                // before a single write below. This is the install date, and the
                                // first write in this method overwrites it forever.
                                installedOnUtc = KeyLastWriteUtc(probe);
                            }
                        }
                    }
                }

                // The entry describes the INSTALLED copy, so only the installed copy may write it —
                // to create it or to maintain it. Every other copy leaves it completely alone.
                //
                // This used to guard creation only. Maintenance ran for WHICHEVER copy was running,
                // and RepairEntry rewrites DisplayIcon, InstallLocation and QuietUninstallString from
                // the running exe unconditionally — so simply opening a second copy of Tempo once (a
                // download, a USB stick, a build folder) re-pointed the installed app's Settings › Apps
                // entry at that copy. Windows 11 prefers QuietUninstallString, so Uninstall then either
                // ran a file the user had since deleted, or uninstalled the wrong copy: it deleted the
                // portable exe, removed the entry, and left the real install orphaned — invisible in
                // Settings › Apps, which is the very complaint this code exists to fix. The installed
                // copy's next launch put the paths back, which is why nobody could catch it: on this
                // project's own PC every install relaunch quietly healed what the layout suite broke.
                if (!DeploymentInfo.IsInstalled) { return; }

                if (!hasEntry)
                {
                    // The entry is missing for a copy that IS installed. install.cmd was the only thing
                    // that ever created it, so an install whose entry was never written, or was later
                    // deleted, stayed invisible to Windows no matter how often it ran.
                    if (!RegisterInstalledCopy(RunningExe(), version)) { return; }
                    Logger.Info("[Install] Tempo was installed but missing from Settings > Apps — added it.");
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
        /// <summary>
        /// The Tempo.exe that is actually running. Environment.ProcessPath, via DeploymentInfo:
        /// StartupManager and UpdateInstaller both document that Application.ExecutablePath can name
        /// the single-file extraction folder instead. On this project's own install the two agree
        /// (the entry it maintains holds the real folder), but an Apps entry must never be written
        /// from the one that can be wrong.
        /// </summary>
        private static string RunningExe() => DeploymentInfo.ExecutablePath;

        /// <summary>
        /// Creates Tempo's Settings → Apps entry for an INSTALLED copy, with the same values
        /// install.cmd writes — so an entry made here cannot be told apart from one the script made,
        /// and <see cref="RepairEntry"/> then maintains both identically.
        /// </summary>
        public static bool RegisterInstalledCopy(string exe, string version) =>
            RegisterInstalledCopy(Microsoft.Win32.Registry.CurrentUser, UninstallRoot + "\\" + EntryName, exe, version);

        /// <summary>
        /// Test seam: the same write aimed at any key, so a harness can check every value without
        /// touching the real entry of the Tempo installed on the machine it runs on.
        /// </summary>
        internal static bool RegisterInstalledCopy(Microsoft.Win32.RegistryKey hive, string keyPath,
                                                   string exe, string version)
        {
            try
            {
                if (hive == null || string.IsNullOrEmpty(exe) || !File.Exists(exe)) { return false; }
                string dir = Path.GetDirectoryName(exe) ?? "";

                using (var entry = hive.CreateSubKey(keyPath, writable: true))
                {
                    if (entry == null) { return false; }

                    entry.SetValue("DisplayName", "Tempo", Microsoft.Win32.RegistryValueKind.String);
                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        entry.SetValue("DisplayVersion", version, Microsoft.Win32.RegistryValueKind.String);
                    }
                    entry.SetValue("Publisher", "Tempo", Microsoft.Win32.RegistryValueKind.String);
                    entry.SetValue("InstallLocation", dir, Microsoft.Win32.RegistryValueKind.String);
                    entry.SetValue("DisplayIcon", exe, Microsoft.Win32.RegistryValueKind.String);

                    // Same rule as install.cmd: the script only if it is really there, else the exe,
                    // which cannot dangle because it is the thing being uninstalled.
                    string cmd = dir.Length > 0 ? Path.Combine(dir, "uninstall.cmd") : null;
                    string uninstall = cmd != null && File.Exists(cmd)
                        ? "\"" + cmd + "\""
                        : "\"" + exe + "\" --uninstall";
                    entry.SetValue("UninstallString", uninstall, Microsoft.Win32.RegistryValueKind.String);
                    entry.SetValue("QuietUninstallString", uninstall, Microsoft.Win32.RegistryValueKind.String);

                    entry.SetValue("URLInfoAbout", "https://justcamop555-pixel.github.io/Tempo/",
                                   Microsoft.Win32.RegistryValueKind.String);
                    entry.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    entry.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);

                    // Today IS the install date for an entry created now. Never overwrite one that is
                    // already there — see RepairEntry for how easily the real date is destroyed.
                    if (entry.GetValue("InstallDate") == null)
                    {
                        entry.SetValue("InstallDate",
                            DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
                            Microsoft.Win32.RegistryValueKind.String);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn("[Install] could not write the Settings > Apps entry: " + ex.Message);
                return false;
            }
        }

        private static void RepairEntry(Microsoft.Win32.RegistryKey entry, string version,
                                        bool recomputeSize, DateTime? installedOnUtc)
        {
            string exe = RunningExe();
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

        /// <summary>
        /// Removes the "launch at startup" entry — when it starts THIS copy, or a Tempo.exe that no longer
        /// exists. It used to remove it whatever it pointed at, so uninstalling a portable copy switched off
        /// start-with-Windows for the installed Tempo too.
        /// </summary>
        public static void RemoveStartupEntry()
        {
            try
            {
                string registered = StartupManager.RegisteredExePath();
                if (registered == null) { return; }
                if (!BelongsHereOrDangling(registered, RunningExe()))
                {
                    Logger.Info("[Uninstall] kept the sign-in entry: it starts another copy of Tempo (" + registered + ").");
                    return;
                }
                StartupManager.SetEnabled(false);
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not remove startup entry during uninstall: " + ex.Message);
            }
        }

        /// <summary>
        /// True when a path names this copy's exe, names an exe that is gone, or names nothing usable — the
        /// only cases in which uninstalling this copy may remove the shortcut or entry that holds the path.
        /// </summary>
        internal static bool BelongsHereOrDangling(string pathItNames, string runningExe)
        {
            if (string.IsNullOrWhiteSpace(pathItNames)) { return true; }
            string named = FullOrSelf(pathItNames);
            if (!string.IsNullOrWhiteSpace(runningExe) &&
                string.Equals(named, FullOrSelf(runningExe), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            try { return !File.Exists(named); }
            catch { return true; }
        }

        private static string FullOrSelf(string path)
        {
            try { return Path.GetFullPath(path.Trim().Trim('"')); }
            catch { return path; }
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
            string appData = "", desktop = "";
            try { appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } catch { }
            try { desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } catch { }

            // Same targets, and the same paths, uninstall.cmd uses.
            RemoveShellIntegration(RunningExe(),
                new[]
                {
                    (Path.Combine(appData, "Microsoft", "Windows", "Start Menu", "Programs", "Tempo.lnk"), "Start Menu shortcut"),
                    (Path.Combine(desktop, "Tempo.lnk"), "Desktop shortcut")
                },
                Microsoft.Win32.Registry.CurrentUser, UninstallRoot);
        }

        /// <summary>
        /// The rule, and its test seam: remove only what belongs to THIS copy — a shortcut that opens it, an
        /// Apps entry that describes it — or what points at a Tempo.exe that no longer exists.
        ///
        /// It used to remove all of them unconditionally. Uninstalling a portable copy that sits beside an
        /// installed Tempo therefore took away the INSTALLED copy's Start Menu shortcut and its Settings › Apps
        /// entry, leaving that install working but invisible to Windows.
        /// </summary>
        internal static int RemoveShellIntegration(string runningExe,
            System.Collections.Generic.IEnumerable<(string path, string what)> shortcuts,
            Microsoft.Win32.RegistryKey hive, string uninstallRoot)
        {
            int removed = 0;
            if (shortcuts != null)
            {
                foreach (var (path, what) in shortcuts)
                {
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) { continue; }
                    string target = SelfInstaller.ReadShortcutTarget(path);
                    if (!BelongsHereOrDangling(target, runningExe))
                    {
                        Logger.Info("[Uninstall] kept the " + what + ": it opens another copy of Tempo (" + target + ").");
                        continue;
                    }
                    TryDelete(path, what);
                    removed++;
                }
            }

            try
            {
                string named = null;
                bool present = false;
                using (var entry = hive?.OpenSubKey(uninstallRoot + "\\" + EntryName))
                {
                    if (entry != null)
                    {
                        present = true;
                        named = EntryExe(entry);
                    }
                }
                if (present)
                {
                    if (BelongsHereOrDangling(named, runningExe))
                    {
                        using (var root = hive.OpenSubKey(uninstallRoot, writable: true))
                        {
                            root?.DeleteSubKeyTree(EntryName, throwOnMissingSubKey: false);
                        }
                        removed++;
                        Logger.Info("[Uninstall] removed the Settings > Apps entry.");
                    }
                    else
                    {
                        Logger.Info("[Uninstall] kept the Settings > Apps entry: it belongs to the copy at " + named + ".");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Uninstall: could not remove the Settings > Apps entry: " + ex.Message);
            }
            return removed;
        }

        /// <summary>The Tempo.exe an Apps entry describes: its icon, else its install folder, else its uninstall command.</summary>
        private static string EntryExe(Microsoft.Win32.RegistryKey entry)
        {
            string icon = entry.GetValue("DisplayIcon") as string;
            if (!string.IsNullOrWhiteSpace(icon))
            {
                icon = icon.Trim().Trim('"');
                int comma = icon.LastIndexOf(',');
                if (comma > 0 && int.TryParse(icon.Substring(comma + 1), out _)) { icon = icon.Substring(0, comma); }
                return icon.Trim().Trim('"');
            }
            string dir = entry.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(dir)) { return Path.Combine(dir.Trim(), "Tempo.exe"); }
            return StartupManager.ExePathFromCommand(entry.GetValue("UninstallString") as string);
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
        /// Writes and launches the helper that finishes an uninstall once Tempo has exited. It removes the
        /// program (and what shipped beside it), and removes the data folder and the logs ONLY when
        /// <paramref name="deleteData"/> is true. With the data kept it still removes the signed-in browser
        /// profiles: each holds a live Roblox session outside the encrypted vault. The caller exits right after
        /// this returns true.
        ///
        /// The data used to be deleted every time — the script removed the whole folder unconditionally and
        /// only the program was optional — so the default answer to Uninstall wiped every profile and macro.
        /// </summary>
        public static bool LaunchCleanupAndExitHelper(bool deleteData, bool deleteExe, out string error)
        {
            error = null;

            try
            {
                string dataDir = Persistence.SettingsManager.GetSettingsDirectory();
                string exe = deleteExe ? RunningExe() : string.Empty;
                string logsDir = "";
                try { logsDir = Path.GetDirectoryName(Logger.GetLogPath()) ?? ""; } catch { }
                int pid = Process.GetCurrentProcess().Id;
                string scriptPath = Path.Combine(Path.GetTempPath(),
                    "tempo_uninstall_" + Guid.NewGuid().ToString("N") + ".bat");

                File.WriteAllText(scriptPath, CleanupScript);

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = CleanupArguments(scriptPath, dataDir, exe, pid, logsDir, keepData: !deleteData),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(psi);
                Logger.Info("[Uninstall] cleanup helper launched (program " + (deleteExe ? "removed" : "kept")
                            + ", data " + (deleteData ? "DELETED" : "kept") + ").");
                return true;
            }
            catch (Exception ex)
            {
                error = "Couldn't start the uninstaller: " + ex.Message;
                Logger.Warn("Uninstall helper failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>The command line that runs <see cref="CleanupScript"/>. Also the harness's way in.</summary>
        internal static string CleanupArguments(string scriptPath, string dataDir, string exe, int pid,
                                                string logsDir, bool keepData)
        {
            return "/c \"\"" + scriptPath + "\" \"" + (dataDir ?? "") + "\" \"" + (exe ?? "") + "\" " + pid
                   + " \"" + (logsDir ?? "") + "\" " + (keepData ? "1" : "0") + "\"";
        }

        /// <summary>
        /// The cleanup script. Arguments: data folder, exe ("" = keep the program), PID to wait for, logs
        /// folder, and 1 to keep the data.
        ///
        /// Written without delayed expansion, so a folder name containing "!" is used as it is instead of
        /// being mangled; with no labels inside parenthesised blocks, so every retry is a plain goto; and with
        /// a bounded wait, so a PID that never goes away cannot keep it running for ever.
        ///
        /// tasklist, find and ping are called by their full System32 paths. With Git for Windows' "optional Unix
        /// tools" (or Cygwin / MSYS2) on the PATH, a bare "find" is GNU find, which takes "12345" for a file name
        /// and fails — so the wait decided at once that Tempo had already exited. Measured in exactly that
        /// environment: the script finished in 156 ms instead of waiting for the process.
        ///
        /// Tidying beside the exe touches ONLY the names Tempo ships, and the folder itself is removed with a
        /// plain rd, which refuses a folder that still holds anything: a portable copy living in Downloads
        /// beside the user's own files cannot take any of them with it.
        /// </summary>
        internal const string CleanupScript =
            "@echo off\r\n" +
            "setlocal\r\n" +
            "set \"DATA=%~1\"\r\n" +
            "set \"EXE=%~2\"\r\n" +
            "set \"PID=%~3\"\r\n" +
            "set \"LOGS=%~4\"\r\n" +
            "set \"KEEP=%~5\"\r\n" +
            "set /a w=0\r\n" +
            ":wait\r\n" +
            "\"%SystemRoot%\\System32\\tasklist.exe\" /fi \"PID eq %PID%\" 2>nul | \"%SystemRoot%\\System32\\find.exe\" \"%PID%\" >nul\r\n" +
            "if errorlevel 1 goto gone\r\n" +
            "set /a w+=1\r\n" +
            "if %w% geq 150 goto gone\r\n" +
            "\"%SystemRoot%\\System32\\PING.EXE\" -n 2 127.0.0.1 >nul\r\n" +
            "goto wait\r\n" +
            ":gone\r\n" +
            "if \"%KEEP%\"==\"1\" goto keepdata\r\n" +
            "if not \"%DATA%\"==\"\" if exist \"%DATA%\" rmdir /s /q \"%DATA%\" >nul 2>&1\r\n" +
            "if not \"%LOGS%\"==\"\" if exist \"%LOGS%\" rmdir /s /q \"%LOGS%\" >nul 2>&1\r\n" +
            "if not \"%LOGS%\"==\"\" rd \"%LOGS%\\..\" >nul 2>&1\r\n" +
            "goto program\r\n" +
            ":keepdata\r\n" +
            "if not \"%DATA%\"==\"\" if exist \"%DATA%\\accountbrowsers\" rmdir /s /q \"%DATA%\\accountbrowsers\" >nul 2>&1\r\n" +
            ":program\r\n" +
            "if \"%EXE%\"==\"\" goto done\r\n" +
            "set /a n=0\r\n" +
            ":delexe\r\n" +
            "del /q \"%EXE%\" >nul 2>&1\r\n" +
            "if not exist \"%EXE%\" goto tidy\r\n" +
            "set /a n+=1\r\n" +
            "if %n% geq 15 goto tidy\r\n" +
            "\"%SystemRoot%\\System32\\PING.EXE\" -n 2 127.0.0.1 >nul\r\n" +
            "goto delexe\r\n" +
            ":tidy\r\n" +
            "del /q \"%~dp2Tempo.exe.sha256\" >nul 2>&1\r\n" +
            "del /q \"%~dp2INSTALL-README.txt\" >nul 2>&1\r\n" +
            "del /q \"%~dp2install.cmd\" >nul 2>&1\r\n" +
            "del /q \"%~dp2uninstall.cmd\" >nul 2>&1\r\n" +
            "if exist \"%~dp2runtimes\" rmdir /s /q \"%~dp2runtimes\" >nul 2>&1\r\n" +
            "rd \"%~dp2.\" >nul 2>&1\r\n" +
            ":done\r\n" +
            "del /q \"%~f0\" >nul 2>&1\r\n";
    }
}
