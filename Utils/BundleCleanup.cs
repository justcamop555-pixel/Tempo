using System;
using System.IO;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Removes the extraction folders older Tempo builds leave behind in TEMP.
    ///
    /// Tempo ships as a compressed single-file bundle, so on the FIRST run of any given
    /// build the runtime unpacks its native libraries into
    /// %TEMP%\.net\Tempo\&lt;content-hash&gt;\ and runs them from there. The hash changes
    /// with every build, so every version gets its own folder — and nothing ever deletes
    /// the old ones. They are not small: measured on this machine, 263 MB per build.
    ///
    /// Left alone that accumulates without limit. The machine this was written on had
    /// <b>306 folders totalling 20.9 GB</b>, all of it dead weight from builds that no
    /// longer exist. Nobody would ever find it: it is a hashed folder inside TEMP, and
    /// TEMP cleaners skip it because the files look recently used.
    ///
    /// So Tempo tidies up after itself. The rules are deliberately timid, because this
    /// is code that deletes things:
    ///   • only ever inside %TEMP%\.net\Tempo — never TEMP itself, never anywhere else;
    ///   • never the folder this process is running from;
    ///   • only folders untouched for a few days, so a build still in use is left alone;
    ///   • every failure is ignored — a locked file means some Tempo is using it, which
    ///     is exactly when we should walk away.
    /// </summary>
    internal static class BundleCleanup
    {
        /// <summary>
        /// A folder must be this stale before it is a candidate for removal.
        ///
        /// Two hours, not days. The first version of this used three days, which sounded
        /// prudent and reclaimed nothing: builds pile up fastest exactly when someone is
        /// iterating, so on the machine this was written for, 79 folders totalling 20.8 GB
        /// were all younger than the cutoff and every one was kept. The real protection
        /// isn't age — it's that a bundle in use has its native DLLs mapped, so deleting
        /// it FAILS and we skip it. The age check only has to outlast the moment after an
        /// instance exits while its files are still mapped.
        /// </summary>
        private static readonly TimeSpan MinimumAge = TimeSpan.FromHours(2);

        /// <summary>
        /// Newest folders always kept, on top of the live one — so stepping back to the
        /// previous build (a rollback, or an update that gets reverted) doesn't pay the
        /// several-second unpack again.
        /// </summary>
        private const int KeepNewest = 2;

        /// <summary>
        /// Sweeps in the background and logs what it reclaimed. Safe to call at startup:
        /// it never touches the UI thread and never throws.
        /// </summary>
        public static void SweepInBackground()
        {
            try
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { Sweep(); }
                    catch (Exception ex) { Logger.Warn("[cleanup] sweep failed: " + ex.Message); }
                });
            }
            catch { }
        }

        private static void Sweep()
        {
            string root = Path.Combine(Path.GetTempPath(), ".net", "Tempo");
            if (!Directory.Exists(root)) { return; }

            // The folder we are running from is off limits, whatever its age.
            string live = "";
            try
            {
                if (SelfCheck.IsSingleFile && !string.IsNullOrEmpty(SelfCheck.ExtractionDir))
                {
                    live = Path.GetFullPath(SelfCheck.ExtractionDir).TrimEnd('\\');
                }
            }
            catch { }

            DateTime cutoff = DateTime.UtcNow - MinimumAge;
            long freed = 0;
            int removed = 0, kept = 0;

            string[] folders;
            try { folders = Directory.GetDirectories(root); }
            catch { return; }

            // Newest first, so the KeepNewest exemption applies to the right ones.
            try
            {
                Array.Sort(folders, (a, b) =>
                    Directory.GetLastWriteTimeUtc(b).CompareTo(Directory.GetLastWriteTimeUtc(a)));
            }
            catch { }

            int seen = 0;
            foreach (string dir in folders)
            {
                seen++;
                if (seen <= KeepNewest)
                {
                    kept++;
                    continue;
                }
                try
                {
                    string full = Path.GetFullPath(dir).TrimEnd('\\');
                    if (live.Length > 0 &&
                        full.Equals(live, StringComparison.OrdinalIgnoreCase))
                    {
                        kept++;
                        continue;
                    }
                    // Also skip anything the live folder sits INSIDE, in case the runtime
                    // ever nests the staging path a level deeper than it does today.
                    if (live.Length > 0 &&
                        live.StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase))
                    {
                        kept++;
                        continue;
                    }

                    if (Directory.GetLastWriteTimeUtc(dir) > cutoff)
                    {
                        kept++;
                        continue;
                    }

                    long size = FolderSize(dir);
                    Directory.Delete(dir, true);
                    freed += size;
                    removed++;
                }
                catch
                {
                    // In use, permission-denied, or vanished under us. Leave it.
                    kept++;
                }
            }

            if (removed > 0)
            {
                Logger.Info("[cleanup] removed " + removed + " stale bundle folder(s) from TEMP, freeing " +
                            (freed / (1024 * 1024)) + " MB (" + kept + " kept).");
            }
        }

        /// <summary>Best-effort size of a folder; partial totals are fine for a log line.</summary>
        private static long FolderSize(string dir)
        {
            long total = 0;
            try
            {
                foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; }
                    catch { }
                }
            }
            catch { }
            return total;
        }
    }
}
