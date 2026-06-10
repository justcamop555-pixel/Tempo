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
                Logger.Info("Uninstall cleanup helper launched (deleteExe=" + deleteExe + ").");
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
