using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AutoClicker.Utils;

namespace AutoClicker.Persistence
{
    /// <summary>
    /// Restore points: a copy of the small files that hold the user's data, taken automatically the
    /// first time a DIFFERENT build of Tempo starts — before that build has read or written any of them.
    ///
    /// Every store already keeps one previous generation beside itself (".1", see PersistenceHelper),
    /// but that copy is replaced on every save. If a new version mishandled something on its first
    /// launch, the next save would rotate the damage into ".1" and the user's last good data would be
    /// gone within minutes — and an update is exactly when that is most likely to happen. Nothing kept
    /// a copy from before it.
    ///
    /// Taken at startup rather than inside the in-app updater, so every way a new build arrives is
    /// covered: the updater, a manually replaced exe, install.cmd, Tempo's own installer, and going
    /// back to an older copy. Only the data files are copied — a few hundred KB — never the speech
    /// models, the account browser profiles or the WebView2 cache: large, re-creatable, and nothing an
    /// update rewrites.
    ///
    /// A restore is never done to a running Tempo: the running copy saves profiles and macros as it
    /// exits, which would overwrite what was just put back. It is scheduled instead, Tempo restarts,
    /// and the next launch applies it here — before anything has loaded.
    /// </summary>
    public static class DataSnapshots
    {
        /// <summary>One restore point, as the Restore points window lists it.</summary>
        public sealed class RestorePoint
        {
            public string Id { get; internal set; }
            public string Folder { get; internal set; }
            public DateTime SavedLocal { get; internal set; }
            /// <summary>The build that was starting when this was taken ("1.0.322 260920-1200").</summary>
            public string Before { get; internal set; }
            /// <summary>Taken just before a restore, so that restore can be undone.</summary>
            public bool IsUndo { get; internal set; }
            public int Files { get; internal set; }
        }

        /// <summary>The files a restore point holds. Everything else in the data folder is left alone.</summary>
        internal static readonly string[] DataFiles =
        {
            "settings.json", "profiles.json", "profiles.recycle.json", "macros.json",
            "macros.recycle.json", "sessions.json", "accounts.vault", "notification-history.tsv"
        };

        /// <summary>How many restore points are kept, newest first.</summary>
        public const int Keep = 5;

        private const string BackupsFolder = "backups";
        private const string MarkerFile = "last-build.txt";
        private const string PendingFile = "restore-pending.txt";
        private const string InfoFile = "restore-point.txt";

        // yyyyMMdd-HHmmss-fff: sorts chronologically as plain text. A "-2", "-10" collision suffix would
        // not — "-10" sorts before "-2" — and pruning keeps the newest by sorting.
        private static readonly Regex IdPattern = new Regex(@"^\d{8}-\d{6}-\d{3}$", RegexOptions.CultureInvariant);

        /// <summary>What identifies the running build: version plus build ID ("dev" when unstamped).</summary>
        public static string CurrentBuildStamp
        {
            get
            {
                string id = BuildInfo.Id;
                return AppVersion.Text + " " + (string.IsNullOrEmpty(id) ? "dev" : id);
            }
        }

        /// <summary>
        /// Call once at startup, after the single-instance mutex is owned and before anything loads or
        /// saves a store. Applies a scheduled restore, then takes a restore point if the build changed.
        /// Never throws.
        /// </summary>
        public static void OnStartup()
        {
            try
            {
                OnStartup(SettingsManager.GetSettingsDirectory(), CurrentBuildStamp);
            }
            catch (Exception ex)
            {
                Logger.Warn("[Data] the restore-point check failed: " + ex.Message);
            }
        }

        /// <summary>Test seam: the startup work against any folder and build stamp.</summary>
        internal static void OnStartup(string dataDir, string stamp)
        {
            ApplyPendingRestore(dataDir, stamp);
            TakeIfBuildChanged(dataDir, stamp);
        }

        /// <summary>
        /// Takes a restore point when this build is not the one that ran last. The marker is only moved
        /// on once the point is safely written, so a failed copy is simply tried again next launch.
        /// </summary>
        internal static RestorePoint TakeIfBuildChanged(string dataDir, string stamp)
        {
            string marker = Path.Combine(dataDir, MarkerFile);
            string last = null;
            try { if (File.Exists(marker)) { last = File.ReadAllText(marker).Trim(); } } catch { }
            if (string.Equals(last, stamp, StringComparison.Ordinal)) { return null; }

            RestorePoint point = null;
            if (HasData(dataDir))
            {
                point = Take(dataDir, stamp, isUndo: false, prune: true);
                if (point == null) { return null; }
            }

            try
            {
                Directory.CreateDirectory(dataDir);
                File.WriteAllText(marker, stamp);
            }
            catch (Exception ex)
            {
                Logger.Warn("[Data] could not record which build ran: " + ex.Message);
            }

            if (point != null)
            {
                Logger.Info("[Data] a different build is starting (" + (last ?? "no earlier record") + " -> "
                            + stamp + "); saved restore point " + point.Id + " with " + point.Files + " file(s).");
            }
            return point;
        }

        private static bool HasData(string dataDir)
        {
            foreach (string name in DataFiles)
            {
                if (File.Exists(Path.Combine(dataDir, name))) { return true; }
            }
            return false;
        }

        /// <summary>Copies the data files into a new restore point. Null when it could not be written.</summary>
        internal static RestorePoint Take(string dataDir, string before, bool isUndo, bool prune)
        {
            string folder = null;
            try
            {
                string root = Path.Combine(dataDir, BackupsFolder);
                Directory.CreateDirectory(root);

                DateTime now = DateTime.Now;
                string id = now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
                folder = Path.Combine(root, id);
                // Two points in the same millisecond (a restore's undo point right after a startup
                // point, or a fast test) move forward a millisecond at a time, keeping the text order.
                for (int bump = 1; Directory.Exists(folder) && bump < 1000; bump++)
                {
                    id = now.AddMilliseconds(bump).ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
                    folder = Path.Combine(root, id);
                }
                Directory.CreateDirectory(folder);

                int files = 0;
                foreach (string name in DataFiles)
                {
                    string src = Path.Combine(dataDir, name);
                    if (!File.Exists(src)) { continue; }
                    File.Copy(src, Path.Combine(folder, name), overwrite: false);
                    files++;
                }

                var info = new StringBuilder();
                info.AppendLine("Tempo restore point");
                info.AppendLine("saved=" + now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                info.AppendLine("before=" + (before ?? ""));
                info.AppendLine("undo=" + (isUndo ? "1" : "0"));
                info.AppendLine("files=" + files.ToString(CultureInfo.InvariantCulture));
                File.WriteAllText(Path.Combine(folder, InfoFile), info.ToString());

                if (prune) { Prune(root); }

                return new RestorePoint
                {
                    Id = id, Folder = folder, SavedLocal = now, Before = before ?? "", IsUndo = isUndo, Files = files
                };
            }
            catch (Exception ex)
            {
                Logger.Warn("[Data] could not save a restore point: " + ex.Message);
                // A half-copied point without its info file is ignored by List and Prune alike; remove it
                // so it cannot pile up.
                try { if (folder != null && Directory.Exists(folder)) { Directory.Delete(folder, true); } } catch { }
                return null;
            }
        }

        /// <summary>The restore points in <paramref name="dataDir"/>, newest first.</summary>
        public static List<RestorePoint> List(string dataDir)
        {
            var points = new List<RestorePoint>();
            try
            {
                string root = Path.Combine(dataDir, BackupsFolder);
                if (!Directory.Exists(root)) { return points; }
                foreach (string dir in Directory.GetDirectories(root))
                {
                    RestorePoint p = Read(dir);
                    if (p != null) { points.Add(p); }
                }
                points.Sort((a, b) => string.CompareOrdinal(b.Id, a.Id));
            }
            catch (Exception ex)
            {
                Logger.Warn("[Data] could not list restore points: " + ex.Message);
            }
            return points;
        }

        /// <summary>A folder that is really a restore point, or null. Anything else in backups is not ours.</summary>
        private static RestorePoint Read(string dir)
        {
            string id = Path.GetFileName(dir);
            string infoPath = Path.Combine(dir, InfoFile);
            if (!IdPattern.IsMatch(id) || !File.Exists(infoPath)) { return null; }

            var p = new RestorePoint { Id = id, Folder = dir, Before = "" };
            try
            {
                foreach (string line in File.ReadAllLines(infoPath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) { continue; }
                    string key = line.Substring(0, eq);
                    string value = line.Substring(eq + 1);
                    switch (key)
                    {
                        case "saved":
                            if (DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                                                       DateTimeStyles.AssumeLocal, out DateTime t))
                            {
                                p.SavedLocal = t;
                            }
                            break;
                        case "before": p.Before = value; break;
                        case "undo": p.IsUndo = value == "1"; break;
                        case "files":
                            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n);
                            p.Files = n;
                            break;
                    }
                }
                if (p.SavedLocal == default(DateTime)) { p.SavedLocal = Directory.GetCreationTime(dir); }
            }
            catch { return null; }
            return p;
        }

        /// <summary>Keeps the newest <see cref="Keep"/> restore points and deletes the rest — only ever real restore points.</summary>
        private static void Prune(string root)
        {
            try
            {
                var points = new List<RestorePoint>();
                foreach (string dir in Directory.GetDirectories(root))
                {
                    RestorePoint p = Read(dir);
                    if (p != null) { points.Add(p); }
                }
                points.Sort((a, b) => string.CompareOrdinal(b.Id, a.Id));
                for (int i = Keep; i < points.Count; i++)
                {
                    try { Directory.Delete(points[i].Folder, true); }
                    catch (Exception ex) { Logger.Warn("[Data] could not remove an old restore point: " + ex.Message); }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[Data] could not tidy restore points: " + ex.Message);
            }
        }

        /// <summary>Deletes every restore point. The user's current data is not touched.</summary>
        public static void DeleteAll(string dataDir)
        {
            foreach (RestorePoint p in List(dataDir))
            {
                try { Directory.Delete(p.Folder, true); }
                catch (Exception ex) { Logger.Warn("[Data] could not delete restore point " + p.Id + ": " + ex.Message); }
            }
            Logger.Info("[Data] restore points deleted at the user's request.");
        }

        /// <summary>
        /// Asks the next launch to put this restore point back. Only a real restore point in this data
        /// folder's backups can be named — the id is checked against the naming pattern and must exist,
        /// so a crafted request can never reach outside it.
        /// </summary>
        public static bool ScheduleRestore(string dataDir, string id, out string error)
        {
            error = null;
            if (!IsRestorePoint(dataDir, id, out _))
            {
                error = "That restore point no longer exists.";
                return false;
            }
            try
            {
                File.WriteAllText(Path.Combine(dataDir, PendingFile), id);
                Logger.Info("[Data] restore of " + id + " scheduled for the next start.");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool IsRestorePoint(string dataDir, string id, out string folder)
        {
            folder = null;
            if (string.IsNullOrEmpty(id) || !IdPattern.IsMatch(id)) { return false; }
            string candidate = Path.Combine(dataDir, BackupsFolder, id);
            if (Read(candidate) == null) { return false; }
            folder = candidate;
            return true;
        }

        /// <summary>
        /// Applies a scheduled restore. The request is removed FIRST, so a restore that fails — or
        /// crashes — is never retried at every launch. The data as it is now is saved as a restore point
        /// before anything is replaced, and a file the restore point does not hold is left as it is: a
        /// vault created after the point was taken is not deleted by going back.
        /// </summary>
        internal static bool ApplyPendingRestore(string dataDir, string stamp)
        {
            string pending = Path.Combine(dataDir, PendingFile);
            if (!File.Exists(pending)) { return false; }

            string id = "";
            try { id = File.ReadAllText(pending).Trim(); } catch { }
            try { File.Delete(pending); } catch { }

            if (!IsRestorePoint(dataDir, id, out string folder))
            {
                Logger.Warn("[Data] a scheduled restore named something that is not a restore point; nothing was restored.");
                return false;
            }

            // Not pruned yet: the point being restored may be the oldest one, and pruning first could
            // delete it before its files were copied back.
            RestorePoint undo = Take(dataDir, stamp, isUndo: true, prune: false);
            if (undo == null)
            {
                Logger.Warn("[Data] the current data could not be saved first, so restore point " + id + " was not applied.");
                return false;
            }

            int restored = 0;
            try
            {
                foreach (string name in DataFiles)
                {
                    string src = Path.Combine(folder, name);
                    if (!File.Exists(src)) { continue; }
                    string dst = Path.Combine(dataDir, name);
                    string staging = dst + ".restoring";
                    File.Copy(src, staging, overwrite: true);
                    File.Move(staging, dst, overwrite: true);
                    restored++;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("[Data] restoring " + id + " stopped part-way (" + restored + " file(s) done); "
                             + "the data from before the restore is kept in restore point " + undo.Id + ".", ex);
                Prune(Path.Combine(dataDir, BackupsFolder));
                return false;
            }

            Prune(Path.Combine(dataDir, BackupsFolder));
            Logger.Info("[Data] restored restore point " + id + " (" + restored + " file(s)); what was there before is kept as "
                        + undo.Id + ".");
            return true;
        }
    }
}
