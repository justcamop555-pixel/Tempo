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
        // Written once per process - avoids touching the disk on every single save.
        private static bool _readmeChecked;

        /// <summary>
        /// Drops a small, plain-English README into the data folder the first time we
        /// write there, so anyone who browses to it understands what each file is and
        /// that it's safe to back up or delete. Best-effort; never throws.
        /// </summary>
        private static void EnsureFolderReadme(string dir)
        {
            if (_readmeChecked)
            {
                return;
            }
            _readmeChecked = true;
            try
            {
                string readme = Path.Combine(dir, "README.txt");
                if (File.Exists(readme))
                {
                    return;
                }

                string text =
                    "Tempo - data folder\r\n" +
                    "===================\r\n" +
                    "\r\n" +
                    "This folder holds everything Tempo saves for you. It all stays on this PC;\r\n" +
                    "nothing here is uploaded anywhere.\r\n" +
                    "\r\n" +
                    "What's in here:\r\n" +
                    "  settings.json   - your preferences and options\r\n" +
                    "  profiles.json   - your saved clicker profiles\r\n" +
                    "  macros.json     - your recorded macros\r\n" +
                    "  sessions.json   - your statistics and session history\r\n" +
                    "  models\\          - the offline speech model used by Tempo's own\r\n" +
                    "                    captions (only present if you've used them; can be\r\n" +
                    "                    100+ MB and is re-downloaded if removed)\r\n" +
                    "  *.corrupt       - automatic backups of any file that failed to load,\r\n" +
                    "                    kept so your data can be recovered\r\n" +
                    "\r\n" +
                    "Safe to do:\r\n" +
                    "  - Back up this folder to keep your profiles, macros and stats.\r\n" +
                    "  - Delete a file to reset just that part (Tempo recreates it).\r\n" +
                    "  - Delete the whole folder to reset Tempo completely.\r\n" +
                    "\r\n" +
                    "Tempo does not need to be running to copy these files.\r\n";

                File.WriteAllText(readme, text);
            }
            catch
            {
                // A README is a nicety, not essential - ignore any failure.
            }
        }

        /// <summary>The previous good copy of a store, kept beside it.</summary>
        public static string PreviousPathFor(string path)
        {
            return path + ".1";
        }

        /// <summary>
        /// Writes text to <paramref name="path"/> via a temporary file and an atomic
        /// move, keeping the version it replaces as a ".1" copy. Returns true on success.
        ///
        /// The atomic move already protected against a half-written file — if Tempo
        /// died mid-save, the destination was either the old contents or the new ones,
        /// never a splice of both. What it could NOT protect against is the machine
        /// itself going down: on 2026-08-31 a hard freeze left settings.json at its
        /// full 13,086 bytes with every one of them zero. NTFS had committed the
        /// directory entry and never flushed the data, so the move was atomic and the
        /// file was still destroyed — and being one file with no second copy, that was
        /// every setting the user had.
        ///
        /// Hence the generation behind. It is the same bargain Logger already makes
        /// when it rotates the log to ".1", and it turns "lose everything" into "lose
        /// the last save".
        /// </summary>
        public static bool WriteAtomic(string path, string contents)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                    EnsureFolderReadme(dir);
                }

                string temp = path + ".tmp";
                File.WriteAllText(temp, contents);

                // Keep the outgoing version before it is replaced. Copy rather than
                // move: a move would leave the destination missing for an instant,
                // which is the very window the atomic write exists to avoid.
                try
                {
                    if (File.Exists(path))
                    {
                        File.Copy(path, PreviousPathFor(path), overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    // A backup that cannot be taken must not stop the save itself.
                    Logger.Warn("[Store] could not keep a previous copy of " +
                                Path.GetFileName(path) + ": " + ex.Message);
                }

                // File.Move with overwrite is a single OS call: there is no window
                // in which the destination is missing, unlike Delete-then-Move.
                File.Move(temp, path, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("[Store] atomic write failed for " + path, ex);
                return false;
            }
        }

        /// <summary>
        /// Hands back the previous generation of a store whose main file is unusable,
        /// or null when there isn't one worth having.
        ///
        /// This is the half that makes the ".1" copy worth writing. A backup nothing
        /// ever reads is not a backup — it is a file that gets faithfully maintained
        /// for years and consulted the one time nobody remembers it exists.
        ///
        /// <paramref name="looksValid"/> is the caller's own parser: only the store
        /// knows what a good file looks like, and a backup that is itself damaged must
        /// not be swapped in on top of the damage.
        /// </summary>
        public static string ReadPreviousIfUsable(string path, Func<string, bool> looksValid)
        {
            try
            {
                string prev = PreviousPathFor(path);
                if (!File.Exists(prev))
                {
                    return null;
                }

                string text = File.ReadAllText(prev);
                if (string.IsNullOrWhiteSpace(text) || (looksValid != null && !looksValid(text)))
                {
                    Logger.Warn("[Store] the previous copy of " + Path.GetFileName(path) +
                                " is unusable too; falling back to defaults.");
                    return null;
                }

                Logger.Warn("[Store] " + Path.GetFileName(path) +
                            " could not be read; recovered the previous save from " +
                            Path.GetFileName(prev) + ".");
                return text;
            }
            catch (Exception ex)
            {
                Logger.Warn("[Store] could not read the previous copy of " +
                            Path.GetFileName(path) + ": " + ex.Message);
                return null;
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
