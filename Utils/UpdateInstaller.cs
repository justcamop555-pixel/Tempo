using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Windows.Forms;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Handles installing an update in place: downloading the new executable and
    /// then swapping it for the running one. Because Windows locks a running
    /// <c>.exe</c>, the swap is performed by a tiny helper script that waits for
    /// Tempo to exit, overwrites the old file, and relaunches it.
    /// </summary>
    public static class UpdateInstaller
    {
        /// <summary>
        /// The path of the Tempo.exe the user actually launched. Environment.ProcessPath
        /// is correct for single-file builds; Application.ExecutablePath can instead point
        /// at a temporary extraction folder, which is exactly why an in-app update used to
        /// overwrite a throwaway temp copy and leave the real exe (and the version the user
        /// runs) untouched. Falls back to Application.ExecutablePath if unavailable.
        /// </summary>
        private static string RunningExePath()
        {
            try
            {
                string exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe))
                {
                    exe = Application.ExecutablePath;
                }
                return exe;
            }
            catch
            {
                return Application.ExecutablePath;
            }
        }

        /// <summary>True if the folder holding Tempo.exe can be written to.</summary>
        public static bool IsExeDirWritable()
        {
            try
            {
                string dir = Path.GetDirectoryName(RunningExePath());
                if (string.IsNullOrEmpty(dir))
                {
                    return false;
                }

                string probe = Path.Combine(dir, ".tempo_write_test_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>A sensible temp path to download the new build to.</summary>
        public static string GetDownloadTargetPath(Version version)
        {
            return GetDownloadTargetPath(version, false);
        }

        /// <summary>
        /// A temp path to download to. When <paramref name="isArchive"/> is true the file
        /// is a setup .zip (which we later unpack); otherwise it's the bare Tempo.exe.
        /// </summary>
        public static string GetDownloadTargetPath(Version version, bool isArchive)
        {
            string ext = isArchive ? ".zip" : ".exe";
            string name = version != null ? "Tempo-" + version + ext : "Tempo-update" + ext;
            return Path.Combine(Path.GetTempPath(), name);
        }

        /// <summary>
        /// Unpacks Tempo.exe from a downloaded setup zip to a temp file and returns its
        /// path. Verifies the extracted file is a real Windows executable ("MZ") before
        /// returning it, so a malformed or wrong archive can never be swapped over the
        /// running exe. Returns false (with an error) if the zip has no Tempo.exe or the
        /// extracted file isn't a valid program. Never throws.
        /// </summary>
        public static bool ExtractTempoExe(string zipPath, out string exePath, out string error)
        {
            exePath = null;
            error = null;
            string outPath = Path.Combine(Path.GetTempPath(),
                "Tempo-update-" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                {
                    ZipArchiveEntry entry = null;
                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        // Match Tempo.exe by file name anywhere in the archive (root or a
                        // subfolder), case-insensitively.
                        if (string.Equals(Path.GetFileName(e.FullName), "Tempo.exe",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            entry = e;
                            break;
                        }
                    }

                    if (entry == null)
                    {
                        error = "The downloaded zip doesn't contain Tempo.exe.";
                        return false;
                    }

                    entry.ExtractToFile(outPath, true);
                }

                // Safety net: only ever hand a real Windows executable to the swap helper.
                var info = new FileInfo(outPath);
                if (!info.Exists || info.Length < 4096)
                {
                    error = "The extracted Tempo.exe looks incomplete.";
                    TryDelete(outPath);
                    return false;
                }
                using (var check = new FileStream(outPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (check.ReadByte() != 0x4D || check.ReadByte() != 0x5A) // 'M','Z'
                    {
                        error = "The extracted file is not a valid Windows program.";
                        TryDelete(outPath);
                        return false;
                    }
                }

                exePath = outPath;
                Logger.Info("[Update] unpacked from zip to " + outPath + " (" + info.Length + " bytes).");
                return true;
            }
            catch (Exception ex)
            {
                error = "Couldn't unpack the update from the zip: " + ex.Message;
                Logger.Warn("Update zip extract failed: " + ex.Message);
                TryDelete(outPath);
                return false;
            }
        }

        /// <summary>
        /// Verifies <paramref name="exePath"/> against the SHA-256 published at
        /// <paramref name="sha256Url"/>. Returns true when there's nothing to check
        /// against (no URL, or the checksum can't be fetched) so a checksum hiccup never
        /// blocks a real update; returns false only on a definite mismatch.
        /// </summary>
        public static bool VerifyExeAgainstSha(string exePath, string sha256Url)
        {
            if (string.IsNullOrWhiteSpace(sha256Url))
            {
                return true;
            }
            try
            {
                string expected = TryFetchExpectedSha(sha256Url);
                if (string.IsNullOrEmpty(expected))
                {
                    Logger.Info("[Update] checksum unavailable; skipped SHA-256 verification.");
                    return true;
                }
                string actual = ComputeSha256(exePath);
                bool match = string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
                if (match)
                {
                    Logger.Info("[Update] SHA-256 verified.");
                }
                else
                {
                    Logger.Warn("Extracted exe SHA-256 mismatch: expected " + expected + ", got " + actual + ".");
                }
                return match;
            }
            catch
            {
                // A verification error shouldn't block an otherwise-valid update.
                return true;
            }
        }

        /// <summary>
        /// Downloads <paramref name="url"/> to <paramref name="destPath"/>. Blocking;
        /// call on a background thread. Reports progress via <paramref name="onProgress"/>
        /// (bytesRead, totalBytes; totalBytes is -1 when unknown) and aborts when
        /// <paramref name="isCancelled"/> returns true. Never throws.
        /// </summary>
        public static bool Download(string url, string destPath,
            Action<long, long> onProgress, Func<bool> isCancelled, out string error,
            string sha256Url = null, bool isArchive = false)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(url))
            {
                error = "No download link was provided.";
                return false;
            }

            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                ServicePointManager.Expect100Continue = false;

                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.UserAgent = "Tempo/" + UpdateChecker.CurrentVersion;
                request.Timeout = 15000;
                request.ReadWriteTimeout = 60000;
                request.AllowAutoRedirect = true; // GitHub asset URLs redirect.
                // Skipping automatic proxy (WPAD) discovery is the key fix for the
                // download intermittently hanging on networks without a proxy - the
                // same cause that was already fixed for the update *check*.
                request.Proxy = null;
                request.KeepAlive = false;

                using (var response = (HttpWebResponse)request.GetResponse())
                using (Stream source = response.GetResponseStream())
                using (var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    long total = response.ContentLength;
                    var buffer = new byte[81920];
                    long readTotal = 0;
                    int read;

                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (isCancelled != null && isCancelled())
                        {
                            error = "The download was cancelled.";
                            TryDelete(destPath);
                            return false;
                        }

                        dest.Write(buffer, 0, read);
                        readTotal += read;
                        onProgress?.Invoke(readTotal, total);
                    }

                    // If the server told us the size, make sure we received all of
                    // it — a dropped connection can end the stream early, leaving a
                    // valid-looking but truncated file.
                    if (total > 0 && readTotal < total)
                    {
                        error = "The download was interrupted (" + readTotal + " of " +
                                total + " bytes). Please try again.";
                        TryDelete(destPath);
                        return false;
                    }
                }

                // Basic sanity: a real build is comfortably more than a few KB.
                var info = new FileInfo(destPath);
                if (!info.Exists || info.Length < 4096)
                {
                    error = "The downloaded file looks incomplete.";
                    TryDelete(destPath);
                    return false;
                }

                // Confirm it's actually a Windows executable ("MZ" header) so a
                // server error page or corrupted download can never overwrite the
                // real Tempo.exe. A setup zip ("PK" header) is exempt - it gets
                // unpacked afterwards and the EXTRACTED Tempo.exe is the thing that's
                // header-checked and verified before any swap.
                if (!isArchive)
                {
                    using (var check = new FileStream(destPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        int b0 = check.ReadByte();
                        int b1 = check.ReadByte();
                        if (b0 != 0x4D || b1 != 0x5A) // 'M', 'Z'
                        {
                            error = "The downloaded file is not a valid Windows program.";
                            TryDelete(destPath);
                            return false;
                        }
                    }
                }

                // If the release publishes a SHA-256 checksum, verify the download
                // against it. If the checksum can't be fetched or parsed we simply
                // skip the check (so a checksum hiccup never blocks a real update),
                // but a definite mismatch aborts the update. Skipped for a zip (the
                // published checksum is for Tempo.exe, not the zip) - the extracted
                // exe is verified against it separately.
                if (!isArchive && !string.IsNullOrWhiteSpace(sha256Url))
                {
                    string expected = TryFetchExpectedSha(sha256Url);
                    if (!string.IsNullOrEmpty(expected))
                    {
                        string actual = ComputeSha256(destPath);
                        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                        {
                            error = "The download failed its integrity check (checksum mismatch). Please try again.";
                            Logger.Warn("Update SHA-256 mismatch: expected " + expected + ", got " + actual + ".");
                            TryDelete(destPath);
                            return false;
                        }
                        Logger.Info("[Update] SHA-256 verified.");
                    }
                    else
                    {
                        Logger.Info("[Update] checksum unavailable; skipped SHA-256 verification.");
                    }
                }

                Logger.Info($"[Update] downloaded to {destPath} ({info.Length} bytes).");
                return true;
            }
            catch (Exception ex)
            {
                error = "Download failed: " + ex.Message;
                Logger.Warn("Update download failed: " + ex.Message);
                TryDelete(destPath);
                return false;
            }
        }

        /// <summary>
        /// Writes the swap-helper script and launches it. The helper waits for this
        /// process to exit, overwrites the current exe with <paramref name="newExePath"/>,
        /// then relaunches Tempo. The caller should exit the application immediately
        /// after this returns true.
        /// </summary>
        public static bool LaunchSwapAndExitHelper(string newExePath, out string error)
        {
            error = null;

            try
            {
                string targetExe = RunningExePath();
                int pid = Process.GetCurrentProcess().Id;
                string scriptPath = Path.Combine(Path.GetTempPath(),
                    "tempo_update_" + Guid.NewGuid().ToString("N") + ".bat");

                // Wait (bounded) for our PID to exit, copy with generous retries in
                // case antivirus briefly locks the freshly-written exe, then relaunch
                // from the exe's own folder. Bounded loops mean it can never hang.
                string script =
                    "@echo off\r\n" +
                    "setlocal enabledelayedexpansion\r\n" +
                    "set \"OLD=%~1\"\r\n" +
                    "set \"NEW=%~2\"\r\n" +
                    "set \"PID=%~3\"\r\n" +
                    "ping -n 2 127.0.0.1 >nul\r\n" +
                    "set /a w=0\r\n" +
                    ":wait\r\n" +
                    "tasklist /fi \"PID eq %PID%\" 2>nul | find \"%PID%\" >nul\r\n" +
                    "if errorlevel 1 goto gone\r\n" +
                    "set /a w+=1\r\n" +
                    "if !w! geq 90 goto gone\r\n" +
                    "ping -n 2 127.0.0.1 >nul\r\n" +
                    "goto wait\r\n" +
                    ":gone\r\n" +
                    "set /a n=0\r\n" +
                    ":copy\r\n" +
                    "copy /y \"%NEW%\" \"%OLD%\" >nul 2>&1\r\n" +
                    "if not errorlevel 1 goto done\r\n" +
                    "set /a n+=1\r\n" +
                    "if !n! lss 30 ( ping -n 2 127.0.0.1 >nul & goto copy )\r\n" +
                    ":done\r\n" +
                    "del /q \"%NEW%\" >nul 2>&1\r\n" +
                    "start \"\" /d \"%~dp1\" \"%OLD%\"\r\n" +
                    "del /q \"%~f0\" >nul 2>&1\r\n";

                File.WriteAllText(scriptPath, script);

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"\"" + scriptPath + "\" \"" + targetExe + "\" \"" + newExePath + "\" " + pid + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(psi);
                Logger.Info("[Update] swap helper launched; exiting for replacement.");
                return true;
            }
            catch (Exception ex)
            {
                error = "Couldn't start the updater: " + ex.Message;
                Logger.Warn("Update swap helper failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Downloads a small ".sha256" checksum file and returns the 64-char hex
        /// digest it contains (the publish script writes "&lt;hash&gt; *Tempo.exe").
        /// Returns null on any problem — the caller then skips verification.
        /// </summary>
        private static string TryFetchExpectedSha(string url)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                ServicePointManager.Expect100Continue = false;
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.UserAgent = "Tempo/" + UpdateChecker.CurrentVersion;
                request.Timeout = 15000;
                request.AllowAutoRedirect = true;
                request.Proxy = null;
                request.KeepAlive = false;

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    string text = reader.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return null;
                    }
                    string[] parts = text.Trim().Split(
                        new[] { ' ', '\t', '\r', '\n', '*' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && parts[0].Length == 64 && IsHex(parts[0]))
                    {
                        return parts[0];
                    }
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] hash = sha.ComputeHash(fs);
                var sb = new System.Text.StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        private static bool IsHex(string s)
        {
            foreach (char c in s)
            {
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
