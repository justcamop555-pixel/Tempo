using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Windows.Forms;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Handles installing an update in place: downloading the new executable, putting a
    /// verified copy beside the running one, and swapping it in. Because Windows locks a
    /// running <c>.exe</c>, the swap itself is done by a tiny helper script that waits for
    /// Tempo to exit, renames the new file over the old one, and starts Tempo again.
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

        /// <summary>
        /// Where the verified new build waits beside <paramref name="exePath"/> until Tempo has
        /// exited. Same folder, so the swap is a RENAME — which either happens completely or not
        /// at all — instead of a 100 MB copy onto the exe that a full disk or a lock could stop
        /// half-way, leaving a Tempo.exe that no longer starts.
        /// </summary>
        internal static string StagedPathFor(string exePath)
        {
            return (exePath ?? "") + ".update";
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
        ///
        /// With GitHub's digest known, its first eight characters go into the name. An interrupted
        /// download is kept and resumed later, and this is what stops a re-uploaded asset under
        /// the same version from being spliced onto the bytes of the one it replaced.
        /// </summary>
        public static string GetDownloadTargetPath(Version version, bool isArchive, string sha256 = null)
        {
            string ext = isArchive ? ".zip" : ".exe";
            string name = version != null ? "Tempo-" + UpdateChecker.Display(version) : "Tempo-update";
            if (!string.IsNullOrWhiteSpace(sha256) && sha256.Trim().Length >= 8)
            {
                name += "-" + sha256.Trim().Substring(0, 8).ToLowerInvariant();
            }
            return Path.Combine(Path.GetTempPath(), name + ext);
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
                        if (string.Equals(Path.GetFileName(e.FullName), UpdateChecker.ExeAssetName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            entry = e;
                            break;
                        }
                    }

                    if (entry == null)
                    {
                        error = Localization.T("The downloaded zip doesn't contain Tempo.exe.");
                        return false;
                    }

                    entry.ExtractToFile(outPath, true);
                }

                // Safety net: only ever hand a real Windows executable to the swap helper.
                var info = new FileInfo(outPath);
                if (!info.Exists || info.Length < 4096)
                {
                    error = Localization.T("The extracted Tempo.exe looks incomplete.");
                    TryDelete(outPath);
                    return false;
                }
                if (!StartsWithMz(outPath))
                {
                    error = Localization.T("The extracted file is not a valid Windows program.");
                    TryDelete(outPath);
                    return false;
                }

                exePath = outPath;
                Logger.Info("[Update] unpacked from zip to " + outPath + " (" + info.Length + " bytes).");
                return true;
            }
            catch (Exception ex)
            {
                error = Localization.F("Couldn't unpack the update from the zip: {0}", ex.Message);
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

        // ── Downloading ──────────────────────────────────────────────────────────

        /// <summary>What a download is doing, for the dialog's status line.</summary>
        public enum DownloadPhase
        {
            /// <summary>Asking the server for the file.</summary>
            Connecting,
            /// <summary>Bytes are arriving.</summary>
            Downloading,
            /// <summary>The connection dropped or stalled; asking to continue from where it stopped.</summary>
            Resuming,
            /// <summary>All of it arrived; checking it is the file GitHub published.</summary>
            Verifying
        }

        /// <summary>Tries per download: the first, then up to three resumes.</summary>
        internal const int MaxDownloadAttempts = 4;

        /// <summary>How long to wait for the server to start answering.</summary>
        internal static int HeaderTimeoutMs = 20000;

        /// <summary>
        /// How long a download may go without receiving a single byte before the connection is
        /// treated as dead and resumed. The old code waited up to a minute per read with no way
        /// to abort it — Cancel included, which set a flag nothing looked at while a read hung.
        /// </summary>
        internal static int StallTimeoutMs = 30000;

        /// <summary>Pauses between attempts. Cancel cuts them short.</summary>
        private static readonly int[] RetryPauseMs = { 1000, 3000, 8000 };

        private static readonly Lazy<HttpClient> Http = new Lazy<HttpClient>(() =>
        {
            var handler = new SocketsHttpHandler
            {
                // Same as the check: automatic proxy (WPAD) discovery is not covered by any
                // timeout, and it is what made downloads hang on networks without a proxy.
                UseProxy = false,
                AllowAutoRedirect = true,        // GitHub asset URLs redirect to its CDN
                MaxAutomaticRedirections = 10,
                ConnectTimeout = TimeSpan.FromSeconds(15),
                AutomaticDecompression = DecompressionMethods.None,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2)
            };
            // No overall timeout: a slow line downloading steadily must not be cut off at an
            // arbitrary total. The header wait and the per-read stall limit bound every wait.
            var client = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            try { client.DefaultRequestHeaders.UserAgent.ParseAdd("Tempo/" + UpdateChecker.CurrentVersion); } catch { }
            return client;
        });

        /// <summary>
        /// Downloads <paramref name="url"/> to <paramref name="destPath"/>. Blocking; call on a
        /// background thread. Reports progress via <paramref name="onProgress"/> (bytesRead,
        /// totalBytes; totalBytes is -1 when unknown) and aborts when <paramref name="isCancelled"/>
        /// returns true. Never throws.
        /// </summary>
        public static bool Download(string url, string destPath,
            Action<long, long> onProgress, Func<bool> isCancelled, out string error,
            string sha256Url = null, bool isArchive = false, string expectedSha256 = null)
        {
            return Download(url, destPath, onProgress, null, isCancelled, CancellationToken.None, out error,
                            sha256Url, isArchive, expectedSha256);
        }

        /// <summary>
        /// Downloads <paramref name="url"/> to <paramref name="destPath"/>, resuming by itself when
        /// the connection drops or stalls, and hands back the file only once it has been checked.
        ///
        /// What changed, and why it matters for a 100 MB file:
        ///  · A dropped connection used to fail the whole update, and pressing it again started from
        ///    zero. Now each drop is resumed from the last byte received (an HTTP Range request —
        ///    GitHub's CDN honours it), up to <see cref="MaxDownloadAttempts"/> tries.
        ///  · Cancel used to wait for the current read to return, up to a minute on a stalled line.
        ///    <paramref name="cancel"/> aborts the read itself.
        ///  · The bytes land in "&lt;dest&gt;.partial" and only become <paramref name="destPath"/>
        ///    after every check has passed, so a half-finished file never has the real name. When
        ///    GitHub's digest is known, a partial file left by an earlier attempt is resumed too —
        ///    the digest check at the end catches anything that does not belong.
        /// </summary>
        public static bool Download(string url, string destPath,
            Action<long, long> onProgress, Action<DownloadPhase, int, int> onPhase,
            Func<bool> isCancelled, CancellationToken cancel, out string error,
            string sha256Url = null, bool isArchive = false, string expectedSha256 = null)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(url))
            {
                error = Localization.T("No download link was provided.");
                return false;
            }

            string partial = destPath + ".partial";
            bool verifiable = !string.IsNullOrWhiteSpace(expectedSha256);
            bool cancelled() => cancel.IsCancellationRequested || (isCancelled != null && isCancelled());

            try
            {
                CleanupStaleDownloads(partial);

                // Whatever sits at the destination is from an earlier run, and a FAILED download must
                // leave nothing there — the old code guaranteed that by writing straight to it, and a
                // caller finding a file at dest must be able to trust it arrived this time.
                TryDelete(destPath);

                // Carry on from an earlier attempt only when the end result can be proven.
                long have = 0;
                if (verifiable && File.Exists(partial))
                {
                    have = new FileInfo(partial).Length;
                    if (have > 0)
                    {
                        Logger.Info("[Update] resuming a download left from an earlier attempt (" + have + " bytes on disk).");
                    }
                }
                else
                {
                    TryDelete(partial);
                }

                long total = -1;
                var clock = Stopwatch.StartNew();
                long lastReportMs = -1000;

                for (int attempt = 1; ; attempt++)
                {
                    if (cancelled())
                    {
                        error = Localization.T("The download was cancelled.");
                        return false;
                    }
                    onPhase?.Invoke(attempt == 1 && have == 0 ? DownloadPhase.Connecting : DownloadPhase.Resuming,
                                    attempt, MaxDownloadAttempts);

                    try
                    {
                        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                        using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                        {
                            timeout.CancelAfter(HeaderTimeoutMs);
                            if (have > 0) { request.Headers.Range = new RangeHeaderValue(have, null); }

                            using (HttpResponseMessage response = Http.Value
                                       .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                                       .GetAwaiter().GetResult())
                            {
                                bool resumed = have > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                                if (have > 0 && !resumed)
                                {
                                    if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                                    {
                                        // What is on disk is not a prefix of this file (it is as long as
                                        // the file or longer). Start clean rather than trust it.
                                        Logger.Info("[Update] the server refused the resume point — starting the download again.");
                                        have = 0;
                                        TryDelete(partial);
                                        throw new IOException("the resume point was refused");
                                    }
                                    // A 200 means the whole file is coming again; appending it would
                                    // splice two copies together.
                                    Logger.Info("[Update] the server ignored the resume request — starting the download again.");
                                    have = 0;
                                }

                                if (!response.IsSuccessStatusCode)
                                {
                                    int code = (int)response.StatusCode;
                                    // A missing or forbidden file will not come back by asking again.
                                    if (code >= 400 && code < 500 && code != 408 && code != 429)
                                    {
                                        error = Localization.F("Download failed: {0}",
                                            Localization.F("the server answered {0} ({1})", code, response.ReasonPhrase ?? ""));
                                        TryDelete(partial);
                                        Logger.Warn("[Update] download refused by the server: " + code + " " + response.ReasonPhrase);
                                        return false;
                                    }
                                    throw new HttpRequestException("the server answered " + code + " (" + response.ReasonPhrase + ")");
                                }

                                long? length = response.Content.Headers.ContentLength;
                                total = resumed
                                    ? (response.Content.Headers.ContentRange?.Length
                                       ?? (length.HasValue ? have + length.Value : -1))
                                    : (length ?? -1);
                                if (resumed)
                                {
                                    Logger.Info("[Update] resumed at " + have + " of " + total + " bytes.");
                                }

                                onPhase?.Invoke(DownloadPhase.Downloading, attempt, MaxDownloadAttempts);
                                onProgress?.Invoke(have, total);

                                using (Stream source = response.Content.ReadAsStream(timeout.Token))
                                using (var dest = new FileStream(partial, resumed ? FileMode.Append : FileMode.Create,
                                                                 FileAccess.Write, FileShare.None, 81920))
                                {
                                    var buffer = new byte[81920];
                                    while (true)
                                    {
                                        // Restarts the stall clock for every read: a line that is slow but
                                        // moving is never cut off, one that has gone silent is.
                                        timeout.CancelAfter(StallTimeoutMs);
                                        int read = source.ReadAsync(buffer, 0, buffer.Length, timeout.Token)
                                                         .GetAwaiter().GetResult();
                                        if (read <= 0) { break; }
                                        dest.Write(buffer, 0, read);
                                        have += read;

                                        if (isCancelled != null && isCancelled())
                                        {
                                            throw new OperationCanceledException();
                                        }
                                        // ~10 updates a second. Every 80 KB was ~1,300 cross-thread
                                        // repaints for one download, most of them never seen.
                                        long now = clock.ElapsedMilliseconds;
                                        if (now - lastReportMs >= 100)
                                        {
                                            lastReportMs = now;
                                            onProgress?.Invoke(have, total);
                                        }
                                    }
                                }
                            }
                        }

                        onProgress?.Invoke(have, total);

                        // The server said how big it was: anything short is a connection that
                        // ended early, which is exactly what a resume is for.
                        if (total > 0 && have < total)
                        {
                            throw new IOException("the connection ended at " + have + " of " + total + " bytes");
                        }
                        break;
                    }
                    catch (Exception ex) when (cancelled())
                    {
                        // Kept on disk when it can be verified later, so pressing Update again
                        // carries on instead of fetching all of it from the start.
                        if (!verifiable) { TryDelete(partial); }
                        error = Localization.T("The download was cancelled.");
                        Logger.Info("[Update] download cancelled at " + have + " bytes" +
                                    (ex is OperationCanceledException ? "." : " (" + ex.Message + ")."));
                        return false;
                    }
                    catch (Exception ex)
                    {
                        string why = (ex is OperationCanceledException
                            ? "no data arrived for " + (StallTimeoutMs / 1000) + " s"
                            : ex.Message).TrimEnd('.', ' ');   // it goes inside "(…)." — no ".)."
                        if (attempt >= MaxDownloadAttempts)
                        {
                            if (!verifiable) { TryDelete(partial); }
                            error = Localization.F("The download kept failing, even after {0} tries ({1}). Please try again.",
                                                   MaxDownloadAttempts, why);
                            Logger.Warn("[Update] download gave up after " + attempt + " tries at " + have + " bytes: " + why);
                            return false;
                        }

                        Logger.Warn("[Update] download attempt " + attempt + " of " + MaxDownloadAttempts + " stopped at "
                                    + have + (total > 0 ? " of " + total : "") + " bytes (" + why + ") — resuming.");
                        onPhase?.Invoke(DownloadPhase.Resuming, attempt + 1, MaxDownloadAttempts);
                        int pause = RetryPauseMs[Math.Min(attempt - 1, RetryPauseMs.Length - 1)];
                        if (cancel.WaitHandle.WaitOne(pause) || cancelled())
                        {
                            if (!verifiable) { TryDelete(partial); }
                            error = Localization.T("The download was cancelled.");
                            return false;
                        }
                        // The loop asks for the file again, from `have` onward when there is a partial.
                        if (!File.Exists(partial)) { have = 0; }
                    }
                }

                onPhase?.Invoke(DownloadPhase.Verifying, 0, 0);

                // Basic sanity: a real build is comfortably more than a few KB.
                var info = new FileInfo(partial);
                if (!info.Exists || info.Length < 4096 || (total > 0 && info.Length != total))
                {
                    error = Localization.T("The downloaded file looks incomplete.");
                    TryDelete(partial);
                    return false;
                }

                // Confirm it's actually a Windows executable ("MZ" header) so a
                // server error page or corrupted download can never overwrite the
                // real Tempo.exe. A setup zip ("PK" header) is exempt - it gets
                // unpacked afterwards and the EXTRACTED Tempo.exe is the thing that's
                // header-checked and verified before any swap.
                if (!isArchive && !StartsWithMz(partial))
                {
                    error = Localization.T("The downloaded file is not a valid Windows program.");
                    TryDelete(partial);
                    return false;
                }

                // GitHub's own SHA-256 for this asset, from the same API response as the update
                // itself. Every release asset has one — unlike the .sha256 file checked below,
                // which the releases stopped carrying, so until this an in-app update installed
                // whatever bytes arrived. It is the digest of the asset itself, so it covers a zip
                // as well as a bare exe. A mismatch is never skipped.
                if (verifiable)
                {
                    string actual = ComputeSha256(partial);
                    if (!string.Equals(expectedSha256.Trim(), actual, StringComparison.OrdinalIgnoreCase))
                    {
                        error = Localization.T("The download does not match the file GitHub published for this release, so it was not installed. Please try again.");
                        Logger.Warn("[Update] SHA-256 mismatch against GitHub's digest: expected " + expectedSha256.Trim()
                                    + ", got " + actual + ".");
                        TryDelete(partial);
                        return false;
                    }
                    Logger.Info("[Update] SHA-256 matches GitHub's digest for the release asset.");
                }

                // If the release publishes a SHA-256 checksum, verify the download
                // against it. If the checksum can't be fetched or parsed we simply
                // skip the check (so a checksum hiccup never blocks a real update),
                // but a definite mismatch aborts the update. Skipped for a zip (the
                // published checksum is for Tempo.exe, not the zip) - the extracted
                // exe is verified against it separately.
                else if (!isArchive && !string.IsNullOrWhiteSpace(sha256Url))
                {
                    string expected = TryFetchExpectedSha(sha256Url);
                    if (!string.IsNullOrEmpty(expected))
                    {
                        string actual = ComputeSha256(partial);
                        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                        {
                            error = Localization.T("The download failed its integrity check (checksum mismatch). Please try again.");
                            Logger.Warn("Update SHA-256 mismatch: expected " + expected + ", got " + actual + ".");
                            TryDelete(partial);
                            return false;
                        }
                        Logger.Info("[Update] SHA-256 verified.");
                    }
                    else
                    {
                        Logger.Info("[Update] checksum unavailable; skipped SHA-256 verification.");
                    }
                }
                else if (!isArchive)
                {
                    Logger.Warn("[Update] no checksum is published for this download, so it could not be verified.");
                }

                // Only now does it get the real name.
                File.Move(partial, destPath, overwrite: true);
                Logger.Info($"[Update] downloaded to {destPath} ({info.Length} bytes).");
                return true;
            }
            catch (Exception ex)
            {
                error = Localization.F("Download failed: {0}", ex.Message);
                Logger.Warn("Update download failed: " + ex.Message);
                if (!verifiable) { TryDelete(partial); }
                return false;
            }
        }

        /// <summary>
        /// Removes what earlier update attempts left in TEMP: partial and finished downloads, zip
        /// extractions and helper scripts more than two days old. Only names the updater itself
        /// creates are touched, and <paramref name="keep"/> never is. Best effort, never throws.
        /// </summary>
        internal static void CleanupStaleDownloads(string keep = null)
        {
            try
            {
                string temp = Path.GetTempPath();
                DateTime cutoff = DateTime.UtcNow.AddDays(-2);
                var ours = new System.Text.RegularExpressions.Regex(
                    @"^(Tempo-(\d+(\.\d+){1,3}|update)(-[0-9a-f]{8})?\.(exe|zip)(\.partial)?|Tempo-update-[0-9a-f]{32}\.exe|tempo_update_[0-9a-f]{32}\.bat)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (string file in Directory.EnumerateFiles(temp))
                {
                    string name = Path.GetFileName(file);
                    if (!ours.IsMatch(name)) { continue; }
                    if (keep != null && string.Equals(file, keep, StringComparison.OrdinalIgnoreCase)) { continue; }
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoff)
                        {
                            File.Delete(file);
                            Logger.Info("[Update] removed a leftover download from an earlier update: " + name);
                        }
                    }
                    catch { /* in use, or already gone */ }
                }
            }
            catch { }
        }

        // ── Installing ───────────────────────────────────────────────────────────

        /// <summary>
        /// Turns a finished download into a verified copy sitting beside the running Tempo.exe,
        /// ready for the swap helper to rename into place once Tempo has exited.
        ///
        /// Done here, while Tempo is still running, so everything that can fail with a reason —
        /// a zip without Tempo.exe, a full disk, a folder that refuses writes — fails while there
        /// is still a window to say so. The helper used to COPY 100 MB onto the exe after Tempo
        /// had gone; when that failed it deleted the download and started the old version with
        /// no word to anyone.
        /// </summary>
        public static bool PrepareStagedUpdate(string downloadedPath, bool isArchive, string sha256Url,
                                               out string stagedPath, out string error)
        {
            stagedPath = null;
            error = null;
            string exe = downloadedPath;
            try
            {
                if (isArchive)
                {
                    if (!ExtractTempoExe(downloadedPath, out exe, out error)) { return false; }
                    TryDelete(downloadedPath);
                    if (!VerifyExeAgainstSha(exe, sha256Url))
                    {
                        error = Localization.T("The update failed its integrity check (checksum mismatch). Please try again, or use the download page.");
                        TryDelete(exe);
                        return false;
                    }
                }

                string staged = StagedPathFor(RunningExePath());
                if (!StageCopy(exe, staged, out error)) { return false; }

                if (!string.Equals(exe, staged, StringComparison.OrdinalIgnoreCase)) { TryDelete(exe); }
                stagedPath = staged;
                return true;
            }
            catch (Exception ex)
            {
                error = Localization.F("Tempo couldn't put the update next to Tempo.exe: {0}", ex.Message);
                Logger.Warn("[Update] staging failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Copies <paramref name="source"/> to <paramref name="staged"/> and proves the copy is identical.</summary>
        internal static bool StageCopy(string source, string staged, out string error)
        {
            error = null;
            try
            {
                string want = ComputeSha256(source);
                File.Copy(source, staged, overwrite: true);
                string got = ComputeSha256(staged);
                if (!string.Equals(want, got, StringComparison.OrdinalIgnoreCase) || !StartsWithMz(staged))
                {
                    TryDelete(staged);
                    error = Localization.T("The copy of the update next to Tempo.exe didn't match the download, so it was not used. Please try again.");
                    Logger.Warn("[Update] the staged copy differs from the download: " + want + " vs " + got + ".");
                    return false;
                }
                Logger.Info("[Update] staged the new build beside Tempo.exe: " + staged);
                return true;
            }
            catch (Exception ex)
            {
                TryDelete(staged);
                error = Localization.F("Tempo couldn't put the update next to Tempo.exe: {0}", ex.Message);
                Logger.Warn("[Update] staging failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Deletes a staged update that will not be used (the helper did not start, or it was abandoned).</summary>
        public static void DiscardStagedUpdate(string stagedPath)
        {
            TryDelete(stagedPath);
        }

        /// <summary>
        /// The swap helper. It waits (bounded) for Tempo's process to exit, renames the staged
        /// build over the exe — retrying while antivirus or the exiting process still holds the
        /// file — and starts Tempo again with --restart, so the window comes back even for someone
        /// who starts Tempo in the tray. If the rename never succeeds the old exe is untouched,
        /// the staged file is removed and the OLD version starts; Tempo then reports that the
        /// update did not install (see UpdateHandoff).
        ///
        /// No delayed expansion. With it on, a "!" anywhere in a path — a Windows user name with
        /// one in it puts one in every path under the profile — is eaten as a variable marker, and
        /// the script renames nothing. The retry counters are compared with %n% on lines of their
        /// own (a goto loop re-reads each line, so %n% is current), never inside a ( ) block.
        ///
        /// Arguments: 1 = Tempo.exe, 2 = staged build, 3 = PID to wait for, 4 = rename attempts.
        /// Full System32 paths: with Git's Unix tools on the PATH, a bare "find" is GNU find, which
        /// fails on "12345" — and the wait would decide at once that Tempo had already exited.
        /// </summary>
        internal const string SwapScript =
            "@echo off\r\n" +
            "setlocal\r\n" +
            "set \"OLD=%~1\"\r\n" +
            "set \"NEW=%~2\"\r\n" +
            "set \"PID=%~3\"\r\n" +
            "set \"TRIES=%~4\"\r\n" +
            "if \"%TRIES%\"==\"\" set \"TRIES=30\"\r\n" +
            "set /a w=0\r\n" +
            ":wait\r\n" +
            "\"%SystemRoot%\\System32\\tasklist.exe\" /fi \"PID eq %PID%\" 2>nul | \"%SystemRoot%\\System32\\find.exe\" \"%PID%\" >nul\r\n" +
            "if errorlevel 1 goto gone\r\n" +
            "set /a w+=1\r\n" +
            "if %w% geq 90 goto gone\r\n" +
            "\"%SystemRoot%\\System32\\PING.EXE\" -n 2 127.0.0.1 >nul\r\n" +
            "goto wait\r\n" +
            ":gone\r\n" +
            "set /a n=0\r\n" +
            ":swap\r\n" +
            "if not exist \"%NEW%\" goto start\r\n" +
            "move /y \"%NEW%\" \"%OLD%\" >nul 2>&1\r\n" +
            "if not exist \"%NEW%\" goto start\r\n" +
            "set /a n+=1\r\n" +
            "if %n% geq %TRIES% goto giveup\r\n" +
            "\"%SystemRoot%\\System32\\PING.EXE\" -n 2 127.0.0.1 >nul\r\n" +
            "goto swap\r\n" +
            ":giveup\r\n" +
            "del /q \"%NEW%\" >nul 2>&1\r\n" +
            ":start\r\n" +
            "start \"\" /d \"%~dp1\" \"%OLD%\" --restart\r\n" +
            "del /q \"%~f0\" >nul 2>&1\r\n";

        /// <summary>
        /// Writes the swap helper and launches it for THIS process. The caller must then close
        /// Tempo (normally, so everything is saved) — the helper does nothing until it has.
        /// </summary>
        public static bool LaunchSwapAndExitHelper(string stagedExePath, out string error)
        {
            return StartSwapHelper(RunningExePath(), stagedExePath, Process.GetCurrentProcess().Id, 30, out error);
        }

        /// <summary>Starts <see cref="SwapScript"/> for any exe and PID — the seam the harness uses.</summary>
        internal static bool StartSwapHelper(string targetExe, string stagedExe, int pid, int tries, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrEmpty(stagedExe) || !File.Exists(stagedExe))
                {
                    error = Localization.T("The update to install is missing.");
                    return false;
                }

                string scriptPath = Path.Combine(Path.GetTempPath(),
                    "tempo_update_" + Guid.NewGuid().ToString("N") + ".bat");
                File.WriteAllText(scriptPath, SwapScript);

                var psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    Arguments = "/c \"\"" + scriptPath + "\" \"" + targetExe + "\" \"" + stagedExe + "\" " + pid + " " + tries + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetTempPath()
                };

                Process.Start(psi);
                Logger.Info("[Update] swap helper launched; Tempo will close so Tempo.exe can be replaced.");
                return true;
            }
            catch (Exception ex)
            {
                error = Localization.F("Couldn't start the updater: {0}", ex.Message);
                Logger.Warn("Update swap helper failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Repairs an altered unpacked copy of Tempo. A helper waits for this process to exit, deletes
        /// the folder the .NET host unpacked Tempo into, then starts Tempo again — which unpacks a clean
        /// copy from Tempo.exe. It cannot be done from inside: the running process holds those files.
        ///
        /// Only ever deletes a folder INSIDE %TEMP%\.net\, checked here before anything is written;
        /// any other path is refused. The caller closes Tempo right after this returns true.
        /// </summary>
        public static bool LaunchRepairUnpackHelper(string unpackDir, out string error)
        {
            error = null;
            try
            {
                string full = Path.GetFullPath(unpackDir ?? "").TrimEnd('\\');
                string netRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), ".net")).TrimEnd('\\');
                if (full.Length <= netRoot.Length + 1 ||
                    !full.StartsWith(netRoot + "\\", StringComparison.OrdinalIgnoreCase) ||
                    !Directory.Exists(full))
                {
                    error = "the unpack folder is not where Tempo expects it (" + unpackDir + ")";
                    Logger.Warn("[Integrity] repair refused: " + error);
                    return false;
                }

                string exe = RunningExePath();
                int pid = Process.GetCurrentProcess().Id;
                string scriptPath = Path.Combine(Path.GetTempPath(),
                    "tempo_repair_" + Guid.NewGuid().ToString("N") + ".bat");

                // Wait (bounded) for this PID to exit, delete the folder with retries while the
                // last mapped files are released, then relaunch from the exe's own folder.
                // No delayed expansion, for the reason given on SwapScript: TEMP sits under the
                // user profile, so a "!" in the user name is in this path too.
                string script =
                    "@echo off\r\n" +
                    "setlocal\r\n" +
                    "set \"DIR=%~1\"\r\n" +
                    "set \"EXE=%~2\"\r\n" +
                    "set \"PID=%~3\"\r\n" +
                    "set /a w=0\r\n" +
                    ":wait\r\n" +
                    // Full System32 paths: with Git's Unix tools on the PATH, a bare "find" is GNU find, which
                    // fails on "12345" — and the wait would decide at once that Tempo had already exited.
                    "\"%SystemRoot%\\System32\\tasklist.exe\" /fi \"PID eq %PID%\" 2>nul | \"%SystemRoot%\\System32\\find.exe\" \"%PID%\" >nul\r\n" +
                    "if errorlevel 1 goto gone\r\n" +
                    "set /a w+=1\r\n" +
                    "if %w% geq 90 goto gone\r\n" +
                    "\"%SystemRoot%\\System32\\PING.EXE\" -n 2 127.0.0.1 >nul\r\n" +
                    "goto wait\r\n" +
                    ":gone\r\n" +
                    "set /a n=0\r\n" +
                    ":remove\r\n" +
                    "if not exist \"%DIR%\" goto done\r\n" +
                    "rmdir /s /q \"%DIR%\" >nul 2>&1\r\n" +
                    "if not exist \"%DIR%\" goto done\r\n" +
                    "set /a n+=1\r\n" +
                    "if %n% geq 30 goto done\r\n" +
                    "\"%SystemRoot%\\System32\\PING.EXE\" -n 2 127.0.0.1 >nul\r\n" +
                    "goto remove\r\n" +
                    ":done\r\n" +
                    "start \"\" /d \"%~dp2\" \"%EXE%\" --restart\r\n" +
                    "del /q \"%~f0\" >nul 2>&1\r\n";

                File.WriteAllText(scriptPath, script);

                var psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    Arguments = "/c \"\"" + scriptPath + "\" \"" + full + "\" \"" + exe + "\" " + pid + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetTempPath()
                };

                Process.Start(psi);
                Logger.Warn("[Integrity] repair helper launched — removing " + full + " and restarting Tempo.");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Logger.Warn("[Integrity] repair helper failed: " + ex.Message);
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
                using (var cts = new CancellationTokenSource(15000))
                {
                    string text = Http.Value.GetStringAsync(url, cts.Token).GetAwaiter().GetResult();
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

        private static bool StartsWithMz(string path)
        {
            using (var check = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return check.ReadByte() == 0x4D && check.ReadByte() == 0x5A;   // 'M', 'Z'
            }
        }

        internal static string ComputeSha256(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
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
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
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
