using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.Json;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Checks for a newer Tempo version using the GitHub Releases API for the
    /// project repository. No authentication is needed for a public repo. The
    /// "latest" release's <c>tag_name</c> (e.g. <c>v1.0.26</c>) is treated as the
    /// available version, its <c>html_url</c> as the download page, and its
    /// <c>body</c> as the release notes.
    /// </summary>
    public static class UpdateChecker
    {
        /// <summary>The GitHub repository in "owner/repo" form.</summary>
        public const string Repository = "justcamop555-pixel/Tempo";

        /// <summary>GitHub Releases API endpoint for the latest published release.</summary>
        public const string ManifestUrl =
            "https://api.github.com/repos/" + Repository + "/releases/latest";

        /// <summary>Human-facing releases page (fallback download link).</summary>
        public const string ReleasesPageUrl =
            "https://github.com/" + Repository + "/releases/latest";

        // GitHub's "latest release" JSON (only the fields we use).
        public sealed class GitHubRelease
        {
            public string tag_name { get; set; }
            public string name { get; set; }
            public string html_url { get; set; }
            public string body { get; set; }
            public bool prerelease { get; set; }
            public bool draft { get; set; }
            public string published_at { get; set; }
            public GitHubAsset[] assets { get; set; }
        }

        public sealed class GitHubAsset
        {
            public string name { get; set; }
            public string browser_download_url { get; set; }

            /// <summary>
            /// GitHub's own checksum for the uploaded file, as "sha256:&lt;hex&gt;".
            ///
            /// This is what lets Tempo prove a copy is genuine without downloading
            /// 106 MB and without the release having to carry a separate .sha256 asset
            /// — which these releases do not. It comes from GitHub's side of the
            /// upload, so it is a reference that does not live on the user's machine.
            /// </summary>
            public string digest { get; set; }
        }

        public sealed class UpdateResult
        {
            public bool Success { get; set; }
            public string Error { get; set; }
            public bool UpdateAvailable { get; set; }
            public Version LatestVersion { get; set; }
            public string DownloadUrl { get; set; }
            public string Sha256Url { get; set; }
            public string Notes { get; set; }
            public DateTime? ReleaseDate { get; set; }
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>Outcome of asking GitHub about one specific release.</summary>
        public enum ReleaseLookup
        {
            /// <summary>The release exists and was read.</summary>
            Found,
            /// <summary>GitHub answered, and there is no release with that tag.</summary>
            NoSuchRelease,
            /// <summary>Could not ask (offline, rate-limited, blocked, timed out).</summary>
            Unavailable
        }

        /// <summary>
        /// Fetches ONE release by its tag, for verifying a build rather than updating.
        ///
        /// Deliberately separate from <see cref="CheckForUpdate"/>: that asks "what is
        /// the newest version?", this asks "what does GitHub say the build I am running
        /// should look like?". The difference matters — the answer for a version that
        /// was never published is itself the finding, so "no such release" is a real
        /// result here and not an error.
        /// </summary>
        public static ReleaseLookup FetchReleaseByTag(string tag, out GitHubRelease release, out string error)
        {
            release = null;
            error = null;
            if (string.IsNullOrWhiteSpace(tag))
            {
                error = "no tag to look up";
                return ReleaseLookup.Unavailable;
            }

            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                ServicePointManager.Expect100Continue = false;

                string url = "https://api.github.com/repos/" + Repository + "/releases/tags/" +
                             Uri.EscapeDataString(tag);
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 9000;
                request.ReadWriteTimeout = 9000;
                // Same reasoning as the update check: automatic proxy discovery is not
                // covered by Timeout and is the usual cause of a long silent hang.
                request.Proxy = null;
                request.KeepAlive = false;
                request.UserAgent = "Tempo/" + CurrentVersion + " (+https://github.com/" + Repository + ")";
                request.Accept = "application/vnd.github+json";
                request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
                request.CachePolicy =
                    new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);

                string json;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    json = reader.ReadToEnd();
                }

                release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);
                if (release == null || string.IsNullOrWhiteSpace(release.tag_name))
                {
                    error = "the release data could not be read";
                    return ReleaseLookup.Unavailable;
                }
                return ReleaseLookup.Found;
            }
            catch (WebException wex)
            {
                // A 404 is an ANSWER, not a failure: GitHub is telling us this version
                // was never published. Everything else means we could not ask.
                var http = wex.Response as HttpWebResponse;
                if (http != null && http.StatusCode == HttpStatusCode.NotFound)
                {
                    return ReleaseLookup.NoSuchRelease;
                }
                error = wex.Message;
                return ReleaseLookup.Unavailable;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return ReleaseLookup.Unavailable;
            }
        }

        /// <summary>The version of the running build (e.g. 1.0.25.0).</summary>
        public static Version CurrentVersion
        {
            get
            {
                try
                {
                    return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
                }
                catch
                {
                    return new Version(0, 0);
                }
            }
        }

        /// <summary>
        /// Blocking fetch + compare. Call from a background thread. Never throws —
        /// failures are returned in <see cref="UpdateResult.Error"/>.
        /// </summary>
        public static UpdateResult Check()
        {
            var result = new UpdateResult();
            // One retry. With a generous 9s per-attempt timeout, a second attempt keeps
            // the worst case (~19s) inside the caller's safety window while still riding
            // out a slow cold connection or a brief server hiccup.
            const int maxAttempts = 2;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                ServicePointManager.Expect100Continue = false;

                var request = (HttpWebRequest)WebRequest.Create(ManifestUrl);
                request.Method = "GET";
                // Generous per-attempt timeout: a cold first connection (DNS + TLS
                // handshake) or a momentarily slow GitHub can take several seconds, so
                // don't give up too early. With one retry this still fits the caller's
                // safety window.
                request.Timeout = 9000;
                request.ReadWriteTimeout = 9000;
                // Skip automatic proxy (WPAD) discovery — it isn't covered by Timeout
                // and is the usual cause of the check intermittently hanging for a long
                // time on networks without a proxy.
                request.Proxy = null;
                request.KeepAlive = false;
                // GitHub requires a User-Agent and recommends an explicit API version.
                request.UserAgent = "Tempo/" + CurrentVersion + " (+https://github.com/" + Repository + ")";
                request.Accept = "application/vnd.github+json";
                request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
                request.CachePolicy =
                    new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);

                string json;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    json = reader.ReadToEnd();
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    result.Error = "The update server returned an empty response.";
                    return result;
                }

                GitHubRelease release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);
                if (release == null || string.IsNullOrWhiteSpace(release.tag_name))
                {
                    result.Error = "No published release was found yet.";
                    return result;
                }

                if (!TryParseTag(release.tag_name, out Version latest))
                {
                    result.Error = "The latest release tag (" + release.tag_name +
                                   ") is not a valid version number.";
                    return result;
                }

                result.Success = true;
                result.LatestVersion = latest;
                result.Notes = string.IsNullOrWhiteSpace(release.body) ? release.name : release.body;
                result.DownloadUrl = PickDownloadUrl(release);
                result.Sha256Url = PickChecksumUrl(release);
                result.UpdateAvailable = latest > CurrentVersion;

                if (!string.IsNullOrWhiteSpace(release.published_at) &&
                    DateTime.TryParse(release.published_at,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime pub))
                {
                    result.ReleaseDate = pub;
                }

                Logger.Info($"[Update] check: current {CurrentVersion}, latest {latest} " +
                            $"(tag {release.tag_name}), update available: {result.UpdateAvailable}.");
                return result;
            }
            catch (WebException wex)
            {
                var http = wex.Response as HttpWebResponse;
                int code = http != null ? (int)http.StatusCode : 0;

                // Transient server-side or connection hiccups - a 500/502/503/504 gateway
                // Transient server-side or connection hiccups - a 500/502/503/504 gateway
                // error, a 429, a dropped connection, or a timeout (often just a slow
                // cold connection) - usually clear on an immediate retry, so try again
                // before bothering the user.
                bool transient =
                    code == 500 || code == 502 || code == 503 || code == 504 || code == 429 ||
                    wex.Status == WebExceptionStatus.Timeout ||
                    wex.Status == WebExceptionStatus.ConnectFailure ||
                    wex.Status == WebExceptionStatus.ReceiveFailure ||
                    wex.Status == WebExceptionStatus.SendFailure ||
                    wex.Status == WebExceptionStatus.KeepAliveFailure ||
                    wex.Status == WebExceptionStatus.PipelineFailure;

                if (transient && attempt < maxAttempts)
                {
                    Logger.Warn($"Update check transient error (attempt {attempt} of {maxAttempts}, code {code}): " + wex.Message);
                    try { System.Threading.Thread.Sleep(600 * attempt); } catch { }
                    continue;
                }

                if (http != null && http.StatusCode == HttpStatusCode.NotFound)
                {
                    // A 404 here usually just means no release has been published yet.
                    result.Error = "No published release was found for " + Repository + " yet.";
                }
                else if (code == 403)
                {
                    // GitHub rate-limits unauthenticated API requests (60/hour).
                    result.Error = "GitHub is rate-limiting update checks right now. " +
                                   "Please wait a little while and try again.";
                }
                else if (wex.Status == WebExceptionStatus.Timeout)
                {
                    result.Error = "The update check timed out, even after retrying. GitHub may be slow " +
                                   "right now, or a firewall/VPN/antivirus may be blocking Tempo's connection " +
                                   "to github.com. Your internet is otherwise fine \u2014 please try again in a moment.";
                }
                else if (code >= 500)
                {
                    // A gateway/server error on GitHub's end, not a problem with Tempo or
                    // your PC, and almost always temporary - even after the retries above.
                    result.Error = "GitHub's update server is having a temporary problem (error " + code +
                                   "). This isn't a problem with Tempo \u2014 please try again in a few minutes.";
                }
                else if (http != null)
                {
                    result.Error = "The update server returned an error (" + code +
                                   "). Please try again later.";
                }
                else
                {
                    result.Error = "Couldn't reach GitHub. Check your internet connection.";
                }
                Logger.Warn("Update check network error: " + wex.Message);
                return result;
            }
            catch (Exception ex)
            {
                result.Error = "The update check failed: " + ex.Message;
                Logger.Warn("Update check failed: " + ex.Message);
                return result;
            }
            }

            // Reached only if every attempt hit a transient error without producing a
            // more specific message above (defensive - the loop normally returns first).
            if (string.IsNullOrEmpty(result.Error))
            {
                result.Error = "GitHub's update server is having a temporary problem. " +
                               "This isn't a problem with Tempo \u2014 please try again in a few minutes.";
            }
            return result;
        }

        /// <summary>Parses a release tag such as "v1.0.26" or "1.0.26" into a Version.</summary>
        private static bool TryParseTag(string tag, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(tag))
            {
                return false;
            }

            string cleaned = tag.Trim();
            if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase) ||
                cleaned.StartsWith("V", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(1);
            }

            // Keep only a leading numeric-dotted portion (drops suffixes like "-beta").
            int end = 0;
            while (end < cleaned.Length && (char.IsDigit(cleaned[end]) || cleaned[end] == '.'))
            {
                end++;
            }
            cleaned = cleaned.Substring(0, end);

            return Version.TryParse(cleaned, out version);
        }

        /// <summary>
        /// Prefers a downloadable .exe asset attached to the release; otherwise
        /// falls back to the release's web page.
        /// </summary>
        private static string PickDownloadUrl(GitHubRelease release)
        {
            if (release.assets != null)
            {
                // Prefer a standalone .exe so the in-app one-click updater can swap
                // it in place. If a release only ships the installer zip, return that
                // (the app opens it in the browser to run install.cmd). Fall back to
                // any zip, then the releases page.
                string setup = null, exe = null, anyZip = null;
                foreach (GitHubAsset asset in release.assets)
                {
                    if (asset == null || string.IsNullOrWhiteSpace(asset.browser_download_url) ||
                        string.IsNullOrWhiteSpace(asset.name))
                    {
                        continue;
                    }
                    string n = asset.name;
                    if (exe == null && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        exe = asset.browser_download_url;
                    }
                    else if (setup == null && n.StartsWith("Tempo-Setup", StringComparison.OrdinalIgnoreCase)
                        && n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        setup = asset.browser_download_url;
                    }
                    else if (anyZip == null && n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        anyZip = asset.browser_download_url;
                    }
                }
                if (exe != null) return exe;
                if (setup != null) return setup;
                if (anyZip != null) return anyZip;
            }

            return string.IsNullOrWhiteSpace(release.html_url) ? ReleasesPageUrl : release.html_url;
        }

        /// <summary>
        /// Finds the SHA-256 checksum asset (e.g. Tempo.exe.sha256) if the release
        /// publishes one, so the download can be integrity-checked. Returns null when
        /// there isn't one — verification is then simply skipped.
        /// </summary>
        private static string PickChecksumUrl(GitHubRelease release)
        {
            if (release.assets != null)
            {
                foreach (GitHubAsset asset in release.assets)
                {
                    if (asset != null && !string.IsNullOrWhiteSpace(asset.browser_download_url) &&
                        asset.name != null && asset.name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                    {
                        return asset.browser_download_url;
                    }
                }
            }
            return null;
        }
    }
}
