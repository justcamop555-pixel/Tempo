using System;
using System.IO;
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

        /// <summary>The path of the Tempo.exe running right now. Exposed for diagnostics.</summary>
        public static string CurrentExePath()
        {
            try { return ResolveExePath(); } catch { return null; }
        }

        /// <summary>
        /// The raw command Windows will run at sign-in, or null when there is no entry.
        /// </summary>
        public static string RegisteredCommand()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return key?.GetValue(ValueName) as string;
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// Pulls the executable out of a Run command string:
        /// <c>"C:\…\Tempo.exe" --startup</c> → <c>C:\…\Tempo.exe</c>. Handles both the
        /// quoted form we write and an unquoted legacy value.
        /// </summary>
        public static string ExePathFromCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) { return null; }
            command = command.Trim();
            if (command[0] == '"')
            {
                int end = command.IndexOf('"', 1);
                return end > 1 ? command.Substring(1, end - 1) : null;
            }
            // Unquoted: everything up to the first argument separator. A path with spaces
            // can't be recovered here, but we only ever WROTE the quoted form.
            int sp = command.IndexOf(" -", StringComparison.Ordinal);
            if (sp < 0) { sp = command.IndexOf(" /", StringComparison.Ordinal); }
            return sp > 0 ? command.Substring(0, sp).Trim() : command;
        }

        /// <summary>The executable Windows will actually launch at sign-in, or null.</summary>
        public static string RegisteredExePath()
        {
            return ExePathFromCommand(RegisteredCommand());
        }

        /// <summary>
        /// The command the Run entry SHOULD hold, given the one it currently holds and
        /// where Tempo is running from. Pure (apart from asking the filesystem whether the
        /// registered exe is still there), so the keep-vs-repoint rule can be tested
        /// directly rather than through the registry.
        ///
        /// Rule: keep the registered executable while it still exists — only its missing
        /// --startup flag is added — and fall back to the running executable when it has
        /// gone. See <see cref="RefreshStartupCommand"/> for why hijacking is the bug.
        /// </summary>
        public static string DesiredCommandFor(string existingValue, string runningExe)
        {
            string registered = ExePathFromCommand(existingValue);
            bool stillThere = false;
            try { stillThere = !string.IsNullOrEmpty(registered) && File.Exists(registered); }
            catch { /* an unreadable path counts as gone */ }

            string exe = stillThere ? registered : runningExe;
            return "\"" + exe + "\" --startup";
        }

        /// <summary>
        /// Keeps an existing startup entry working, without ever stealing it.
        ///
        /// It has two jobs: add the --startup flag to entries written by older versions
        /// (so they start in the tray), and self-heal an entry whose executable has gone
        /// — a portable copy the user moved, or a reinstall to a new folder.
        ///
        /// What it must NOT do is repoint the entry at whatever copy of Tempo happens to
        /// be running. It used to rewrite the path on EVERY launch, so opening a second
        /// copy once — a freshly built exe, a portable copy on a USB stick, an update
        /// staged in Downloads — silently moved the user's sign-in entry to that file.
        /// When that copy was later deleted (a build folder gets cleaned, the stick comes
        /// out), start-with-Windows stopped working and nothing said why: the checkbox
        /// still read ON, because the Run value was still present, just pointing at a
        /// file that no longer existed. So the registered path is only replaced when it
        /// has actually stopped resolving.
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

                    string desired = DesiredCommandFor(val, ResolveExePath());

                    if (!string.Equals(val, desired, StringComparison.OrdinalIgnoreCase))
                    {
                        key.SetValue(ValueName, desired);
                        Logger.Info("[Startup] rewrote the sign-in entry: " + val + "  ->  " + desired);
                    }
                }
            }
            catch { }
        }
    }
}
