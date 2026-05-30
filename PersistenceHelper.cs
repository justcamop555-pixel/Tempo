using System;
using System.IO;
using AutoClicker.Utils;

namespace AutoClicker.Persistence
{
    /// <summary>
    /// Small shared helpers used by the JSON stores. They centralise the "write
    /// atomically" and "preserve a corrupt file for inspection" behaviours so each
    /// store does not reimplement them.
    /// </summary>
    internal static class PersistenceHelper
    {
        /// <summary>
        /// Writes text to <paramref name="path"/> via a temporary file and an atomic
        /// move, so an interrupted write cannot leave a half-written file behind.
        /// Returns true on success.
        /// </summary>
        public static bool WriteAtomic(string path, string contents)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string temp = path + ".tmp";
                File.WriteAllText(temp, contents);

                // File.Move with overwrite is a single OS call: there is no window
                // in which the destination is missing, unlike Delete-then-Move.
                File.Move(temp, path, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Atomic write failed for " + path, ex);
                return false;
            }
        }

        /// <summary>
        /// Renames a file that failed to parse to a timestamped ".corrupt" copy so
        /// the user's data is preserved for recovery and the next save starts clean.
        /// </summary>
        public static void BackupCorruptFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return;
                }

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string backup = path + "." + stamp + ".corrupt";

                if (File.Exists(backup))
                {
                    File.Delete(backup);
                }

                File.Move(path, backup);
                Logger.Warn($"A corrupt file was preserved as '{backup}'.");
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not back up corrupt file '" + path + "': " + ex.Message);
            }
        }
    }
}
