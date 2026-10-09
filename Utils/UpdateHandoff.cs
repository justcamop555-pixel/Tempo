using System;
using System.Globalization;
using System.IO;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Carries what an in-app update was meant to do across the restart it needs, so the Tempo
    /// that comes back can say whether it happened.
    ///
    /// The swap runs after Tempo has exited, in a helper with no window. When it could not replace
    /// Tempo.exe — antivirus holding the file, a folder that stopped accepting writes — it started
    /// the old version again and nothing said so: Tempo came back, looked updated, and offered the
    /// same update again at the next check with no hint of why.
    /// </summary>
    public static class UpdateHandoff
    {
        public enum Outcome
        {
            /// <summary>No update was pending, or the note is too old or no longer applies.</summary>
            None,
            /// <summary>The version the update installs is the one running now.</summary>
            Updated,
            /// <summary>Tempo came back on an older version than the update was installing.</summary>
            NotInstalled
        }

        public sealed class Report
        {
            public Outcome Outcome;
            public string FromVersion;
            public string ToVersion;
            public string ReleasePageUrl;
        }

        internal const string FileName = "update-pending.txt";

        /// <summary>
        /// Long enough for the case that matters most: the relaunch itself failed, and the next time
        /// anyone opens Tempo is hours or days later. That launch still has something true to say.
        /// </summary>
        private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

        private static string MarkerPath =>
            Path.Combine(Persistence.SettingsManager.GetSettingsDirectory(), FileName);

        /// <summary>Notes that an update from <paramref name="from"/> to <paramref name="to"/> is about to be swapped in.</summary>
        public static void Record(Version from, Version to, string releasePageUrl)
        {
            try
            {
                File.WriteAllLines(MarkerPath, new[]
                {
                    "from=" + UpdateChecker.Display(from),
                    "to=" + UpdateChecker.Display(to),
                    "utc=" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    "page=" + (releasePageUrl ?? "")
                });
            }
            catch (Exception ex)
            {
                // Not worth stopping an update for: without it the next launch simply says nothing.
                Logger.Warn("[Update] couldn't note the pending update: " + ex.Message);
            }
        }

        /// <summary>Drops the note — the update did not go ahead after all.</summary>
        public static void Forget()
        {
            try { File.Delete(MarkerPath); } catch { }
        }

        /// <summary>
        /// Reads and removes the note an update left, and says what became of it. Also removes a
        /// staged build beside <paramref name="exePath"/> that was never swapped in, which would
        /// otherwise sit there for good (and keep an uninstall from removing the folder). Never throws.
        /// </summary>
        public static Report Collect(Version running, string exePath)
        {
            var report = new Report { Outcome = Outcome.None };
            try
            {
                if (!string.IsNullOrEmpty(exePath))
                {
                    string staged = UpdateInstaller.StagedPathFor(exePath);
                    if (File.Exists(staged))
                    {
                        try
                        {
                            File.Delete(staged);
                            Logger.Info("[Update] removed a staged update that was never swapped in: " + staged);
                        }
                        catch (Exception ex) { Logger.Warn("[Update] couldn't remove " + staged + ": " + ex.Message); }
                    }
                }

                string path = MarkerPath;
                if (!File.Exists(path)) { return report; }
                string[] lines = File.ReadAllLines(path);
                try { File.Delete(path); } catch { }

                DateTime? when = null;
                foreach (string raw in lines)
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) { continue; }
                    string key = raw.Substring(0, eq).Trim();
                    string value = raw.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "from": report.FromVersion = value; break;
                        case "to": report.ToVersion = value; break;
                        case "page": report.ReleasePageUrl = value.Length > 0 ? value : null; break;
                        case "utc":
                            if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                                                  DateTimeStyles.RoundtripKind, out DateTime t))
                            {
                                when = t.ToUniversalTime();
                            }
                            break;
                    }
                }

                if (string.IsNullOrEmpty(report.ToVersion) || when == null)
                {
                    Logger.Warn("[Update] ignored an unreadable pending-update note.");
                    return report;
                }
                TimeSpan age = DateTime.UtcNow - when.Value;
                if (age > MaxAge || age < TimeSpan.FromHours(-1))
                {
                    Logger.Info("[Update] ignored a pending-update note from " + when.Value.ToString("u") + " (too old).");
                    return report;
                }

                string now = UpdateChecker.Display(running);
                if (UpdateChecker.IsSameVersion(report.ToVersion, now))
                {
                    report.Outcome = Outcome.Updated;
                    Logger.Info("[Update] updated from " + report.FromVersion + " to " + now + ".");
                }
                else if (UpdateChecker.IsNewerThan(report.ToVersion, running))
                {
                    report.Outcome = Outcome.NotInstalled;
                    Logger.Warn("[Update] the update to " + report.ToVersion + " did not install — still running " + now + ".");
                }
                // Running something NEWER than the update installed: another route got there first.
                return report;
            }
            catch (Exception ex)
            {
                Logger.Swallow("UpdateHandoff.Collect", ex);
                return report;
            }
        }
    }
}
