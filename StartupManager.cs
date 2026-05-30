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
        private const string ValueName = "Tempo";

        /// <summary>True if a startup entry currently exists.</summary>
        public static bool IsEnabled()
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
        /// Adds or removes the startup entry. Returns true on success.
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
                        string exe = Application.ExecutablePath;
                        key.SetValue(ValueName, "\"" + exe + "\"");
                    }
                    else if (key.GetValue(ValueName) != null)
                    {
                        key.DeleteValue(ValueName, false);
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to update startup registry value.", ex);
                return false;
            }
        }
    }
}
