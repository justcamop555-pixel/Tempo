using System;
using System.IO;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Removes what earlier installs left in an INSTALLED copy's folder that can only mislead now. Only ever
    /// in Tempo's own install folder (DeploymentInfo.IsInstalled), only these names, and never anything the
    /// user may have put there on purpose — a backup of the program is theirs to delete.
    ///
    /// Found on this project's own PC (2026-10-18), beside TempoClicker.exe 1.0.324:
    ///   • INSTALL-README.txt from the 1.0.322 setup zip, telling the reader that settings live in a "Data"
    ///     folder beside the exe (they never have: %LOCALAPPDATA%\AutoClicker) and to keep a "runtimes"
    ///     folder that no release has shipped since Tempo became one file;
    ///   • Tempo.exe.sha256, the checksum of 1.0.322's Tempo.exe — a file with another name and other bytes.
    /// install.cmd and the in-app installer copied both into the install folder, and updates replace only
    /// the program, so each went stale with the first update.
    /// </summary>
    internal static class InstallFolderTidy
    {
        private static bool _checksumsDone;

        /// <summary>At start-up, off the UI thread: the setup note, and an install stopped half-way.</summary>
        public static void RunAtStartup()
        {
            try
            {
                string dir = InstalledFolder();
                if (dir == null) { return; }
                TryRemove(Path.Combine(dir, "INSTALL-README.txt"), "the setup note from the download (its instructions are out of date)");
                // A copy the in-app installer was making when it was interrupted. Nothing else can be making one
                // now: installing runs inside a Tempo, and this one holds the single-instance lock.
                TryRemove(Path.Combine(dir, DeploymentInfo.InstalledExeName + ".installing"), "an install that was interrupted");
                TryRemove(Path.Combine(dir, DeploymentInfo.LegacyExeName + ".installing"), "an install that was interrupted");
            }
            catch (Exception ex) { Logger.Swallow("InstallFolderTidy.RunAtStartup", ex); }
        }

        /// <summary>
        /// Once the tamper check knows the running program's SHA-256: removes a checksum file beside it that
        /// describes some other file. One that does describe it is left alone. Once per run.
        /// </summary>
        public static void RemoveStaleChecksums(string currentSha256)
        {
            if (_checksumsDone || string.IsNullOrEmpty(currentSha256) || currentSha256.Length != 64) { return; }
            _checksumsDone = true;
            try
            {
                string dir = InstalledFolder();
                if (dir == null) { return; }
                foreach (string name in new[] { DeploymentInfo.LegacyExeName + ".sha256", DeploymentInfo.InstalledExeName + ".sha256" })
                {
                    string path = Path.Combine(dir, name);
                    if (!File.Exists(path)) { continue; }
                    string listed = ReadChecksum(path);
                    if (listed != null && string.Equals(listed, currentSha256, StringComparison.OrdinalIgnoreCase)) { continue; }
                    TryRemove(path, "a checksum of another file (" + (listed == null ? "unreadable" : listed.Substring(0, 12) + "…") + ")");
                }
            }
            catch (Exception ex) { Logger.Swallow("InstallFolderTidy.RemoveStaleChecksums", ex); }
        }

        /// <summary>The first 64-hex-character word of a ".sha256" file ("&lt;hash&gt; *Tempo.exe"), or null.</summary>
        internal static string ReadChecksum(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length > 4096) { return null; }
                foreach (string word in File.ReadAllText(path).Split(new[] { ' ', '\t', '\r', '\n', '*' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (word.Length != 64) { continue; }
                    bool hex = true;
                    foreach (char c in word) { if (!Uri.IsHexDigit(c)) { hex = false; break; } }
                    if (hex) { return word; }
                }
            }
            catch { }
            return null;
        }

        private static string InstalledFolder()
        {
            if (!DeploymentInfo.IsInstalled) { return null; }
            string exe = DeploymentInfo.ExecutablePath;
            string dir = string.IsNullOrEmpty(exe) ? null : Path.GetDirectoryName(exe);
            return string.IsNullOrEmpty(dir) || !Directory.Exists(dir) ? null : dir;
        }

        private static void TryRemove(string path, string what)
        {
            try
            {
                if (!File.Exists(path)) { return; }
                File.Delete(path);
                Logger.Info("[Install] removed " + Path.GetFileName(path) + " from the install folder — " + what + ".");
            }
            catch (Exception ex)
            {
                Logger.Warn("[Install] could not remove " + path + ": " + ex.Message);
            }
        }
    }
}
