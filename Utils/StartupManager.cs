using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Manages whether the application launches automatically when the user signs
    /// in to Windows, via the per-user Run registry key. All operations are
    /// defensive — failures are logged and reported, never thrown.
    /// </summary>
    public static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        // Windows 11's Task Manager "Startup apps" tab (and Settings > Apps > Startup)
        // can DISABLE a Run entry without deleting it, by writing a flag here. When
        // that flag says disabled, Windows ignores the Run entry at sign-in — which is
        // the usual reason "I turned Start-with-Windows on but it never starts".
        private const string StartupApprovedPath =
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string ValueName = "Tempo";

        /// <summary>
        /// The path to the running Tempo.exe. Uses Environment.ProcessPath, which is
        /// the OS-level executable path and is correct for single-file published apps
        /// (Application.ExecutablePath can return a temporary extraction path for those,
        /// which would make the Windows startup entry point at a file that no longer
        /// exists at sign-in - the usual reason "start with Windows" silently fails).
        /// </summary>
        private static string ResolveExePath()
        {
            try
            {
                string p = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(p))
                {
                    return p;
                }
            }
            catch
            {
                // Fall through to the WinForms path below.
            }
            return Application.ExecutablePath;
        }

        /// <summary>
        /// True only if Tempo will ACTUALLY launch at sign-in: the Run entry exists AND
        /// Task Manager hasn't disabled it. This is the honest, effective state — so the
        /// Settings checkbox reflects what Windows will really do, not just whether a
        /// stale registry value is present.
        /// </summary>
        public static bool IsEnabled()
        {
            return IsPresent() && !IsDisabledByTaskManager();
        }

        /// <summary>True if the Run-key entry exists (regardless of the Task Manager flag).</summary>
        public static bool IsPresent()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return key != null && key.GetValue(ValueName) != null;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to read startup registry value.", ex);
                return false;
            }
        }

        /// <summary>
        /// True when Windows (Task Manager / Settings > Startup) has explicitly disabled
        /// Tempo's Run entry. The flag is a 12-byte record whose first byte is even when
        /// enabled (2) and odd when disabled (3) — bit 0 is the on/off bit.
        /// </summary>
        public static bool IsDisabledByTaskManager()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupApprovedPath, false))
                {
                    if (key != null && key.GetValue(ValueName) is byte[] data && data.Length > 0)
                    {
                        return (data[0] & 1) == 1;
                    }
                }
            }
            catch { }
            return false;   // no flag => Windows treats it as enabled
        }

        /// <summary>
        /// Clears a Task Manager "disabled" flag so the Run entry launches again. Only
        /// touches the record if one already exists (absence already means enabled), so
        /// it never fabricates a footprint Windows didn't already have.
        /// </summary>
        private static void ClearTaskManagerDisable()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupApprovedPath, true))
                {
                    if (key != null && key.GetValue(ValueName) != null)
                    {
                        // 12-byte "enabled" record: first byte 2, remainder zero — exactly
                        // what Task Manager writes when you flip the toggle back on.
                        key.SetValue(ValueName,
                            new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                            RegistryValueKind.Binary);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Adds or removes the startup entry. When enabling, it also clears any Task
        /// Manager "disabled" flag (otherwise re-checking the box wouldn't actually make
        /// Tempo launch), and VERIFIES the result — so a silent registry failure is
        /// reported instead of leaving the user thinking startup is on when it isn't.
        /// Returns true only when the effective state matches what was requested.
        /// </summary>
        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
                                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    if (enabled)
                    {
                        string exe = ResolveExePath();
                        // Pass a flag so Tempo knows it was auto-started by Windows at
                        // sign-in and can go straight to the tray instead of popping up
                        // its window every boot.
                        key.SetValue(ValueName, "\"" + exe + "\" --startup");
                    }
                    else if (key.GetValue(ValueName) != null)
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }

                if (enabled)
                {
                    // A leftover Task Manager disable would keep it from launching even
                    // though the Run value is now present — clear it.
                    ClearTaskManagerDisable();
                }

                // Confirm the effective state actually changed, so the caller can warn
                // the user on a blocked/locked-down machine.
                return IsEnabled() == enabled;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to update startup registry value.", ex);
                return false;
            }
        }

        /// <summary>
        /// True when this process was launched by the Windows startup entry (it passes
        /// a --startup flag), so the app can start in the tray instead of showing its
        /// window at every sign-in.
        /// </summary>
        public static bool LaunchedAtStartup()
        {
            try
            {
                foreach (string a in Environment.GetCommandLineArgs())
                {
                    if (string.Equals(a, "--startup", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a, "/startup", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Brings the Windows startup entry in line with the user's Tempo setting at
        /// launch and returns the EFFECTIVE state, so the caller can correct the setting
        /// when Windows overrode it:
        ///  • want ON, entry missing  → re-create it (a cleaner/AV likely removed it);
        ///  • want ON, Task-Manager-disabled → respect that Windows-level "off" and
        ///    return false (don't silently re-enable something the user disabled there);
        ///  • want ON, entry present  → refresh the path (portable copy moved, etc.);
        ///  • want OFF, entry present → remove it so the two agree.
        /// </summary>
        public static bool Reconcile(bool wantEnabled)
        {
            try
            {
                if (wantEnabled)
                {
                    if (IsDisabledByTaskManager())
                    {
                        return false;                 // Windows-level user choice wins
                    }
                    if (!IsPresent())
                    {
                        SetEnabled(true);             // self-heal a vanished entry
                    }
                    else
                    {
                        RefreshStartupCommand();      // keep the path current
                    }
                    return IsEnabled();
                }

                if (IsPresent())
                {
                    SetEnabled(false);
                }
                return false;
            }
            catch
            {
                return IsEnabled();
            }
        }

        /// <summary>
        /// Migrates an existing startup entry that predates the --startup flag so the
        /// "start in the tray at sign-in" behaviour also works for users who enabled
        /// start-with-Windows in an older version. No-op if startup isn't enabled or the
        /// flag is already present.
        /// </summary>
        public static void RefreshStartupCommand()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null)
                    {
                        return;
                    }
                    string val = key.GetValue(ValueName) as string;
                    if (string.IsNullOrEmpty(val))
                    {
                        return; // startup isn't enabled - nothing to refresh
                    }

                    string exe = ResolveExePath();
                    string desired = "\"" + exe + "\" --startup";

                    // Keep the startup entry pointing at wherever Tempo actually is now.
                    // A portable copy that the user moved (USB stick, a different folder)
                    // would otherwise leave Windows trying to launch the old, now-missing
                    // path at sign-in; an older entry might also be missing the --startup
                    // flag. Rewrite only when it genuinely differs, to avoid needless
                    // registry writes on every launch.
                    if (!string.Equals(val, desired, StringComparison.OrdinalIgnoreCase))
                    {
                        key.SetValue(ValueName, desired);
                    }
                }
            }
            catch { }
        }
    }
}
