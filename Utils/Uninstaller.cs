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
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: true))
                {
                    if (key != null && key.OpenSubKey("Tempo") != null)
                    {
                        key.DeleteSubKeyTree("Tempo", throwOnMissingSubKey: false);
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
