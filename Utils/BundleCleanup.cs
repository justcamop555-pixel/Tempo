using System;
using System.Collections.Generic;
using System.IO;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Removes the extraction folders older Tempo builds leave behind in TEMP.
    ///
    /// Tempo ships as a compressed single-file bundle, so on the FIRST run of any given
    /// build the runtime unpacks its native libraries into
    /// %TEMP%\.net\&lt;exe name&gt;\&lt;content-hash&gt;\ and runs them from there. The hash changes
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
    ///   • only ever inside Tempo's own %TEMP%\.net\&lt;name&gt; folders (see Roots) — never
    ///     TEMP itself, never anywhere else;
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
        ///
        /// In background mode (<see cref="BackgroundWork"/>), and after a start-with-Windows
        /// launch only once sign-in has settled: the first start after an update deletes the
        /// previous build, 270 MB, and that used to happen at full priority in the first
        /// second of the launch.
        /// </summary>
        public static void SweepInBackground()
        {
            BackgroundWork.Start("bundle clean-up",
                StartupManager.LaunchedAtStartup() ? BackgroundWork.SignInDelayMs : 0, () =>
                {
                    try { Sweep(); }
                    catch (Exception ex) { Logger.Warn("[cleanup] sweep failed: " + ex.Message); }
                });
        }

        private static void Sweep()
        {
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

            foreach (string root in Roots(live))
            {
                SweepRoot(root, live, cutoff, ref freed, ref removed, ref kept);
            }

            if (removed > 0)
            {
                Logger.Info("[cleanup] removed " + removed + " stale bundle folder(s) from TEMP, freeing " +
                            (freed / (1024 * 1024)) + " MB (" + kept + " kept).");
            }
        }

        /// <summary>
        /// Every %TEMP%\.net\&lt;name&gt; folder Tempo unpacks into. The runtime names it after the exe's
        /// FILE name, so one root was not enough once the installed copy became TempoClicker.exe: its
        /// builds went to .net\TempoClicker, which nothing swept, while .net\Tempo kept only the
        /// downloads (still Tempo.exe) and installs from before the rename. Measured on this project's
        /// PC: the same build unpacked to both .net\Tempo\t2ejvhPonQr1 and .net\TempoClicker\t2ejvhPonQr1.
        /// A copy the user renamed adds its own folder; that one is Tempo's by definition, since Tempo
        /// is running from it. Never anything but a direct child of %TEMP%\.net.
        /// </summary>
        private static List<string> Roots(string live)
        {
            string net = Path.GetFullPath(Path.Combine(Path.GetTempPath(), ".net")).TrimEnd('\\');
            var roots = new List<string>
            {
                Path.Combine(net, Path.GetFileNameWithoutExtension(DeploymentInfo.LegacyExeName)),
                Path.Combine(net, Path.GetFileNameWithoutExtension(DeploymentInfo.InstalledExeName)),
            };
            try
            {
                string own = live.Length > 0 ? Path.GetDirectoryName(live) : null;
                if (!string.IsNullOrEmpty(own) &&
                    string.Equals(Path.GetDirectoryName(own), net, StringComparison.OrdinalIgnoreCase) &&
                    !roots.Exists(r => r.Equals(own, StringComparison.OrdinalIgnoreCase)))
                {
                    roots.Add(own);
                }
            }
            catch { }
            return roots;
        }

        private static void SweepRoot(string root, string live, DateTime cutoff,
                                      ref long freed, ref int removed, ref int kept)
        {
            if (!Directory.Exists(root)) { return; }

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
