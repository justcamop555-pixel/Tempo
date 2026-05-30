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
            public GitHubAsset[] assets { get; set; }
        }

        public sealed class GitHubAsset
        {
            public string name { get; set; }
            public string browser_download_url { get; set; }
        }

        public sealed class UpdateResult
        {
            public bool Success { get; set; }
            public string Error { get; set; }
            public bool UpdateAvailable { get; set; }
            public Version LatestVersion { get; set; }
            public string DownloadUrl { get; set; }
            public string Notes { get; set; }
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

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

            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                var request = (HttpWebRequest)WebRequest.Create(ManifestUrl);
                request.Method = "GET";
                request.Timeout = 8000;
                request.ReadWriteTimeout = 8000;
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
                result.UpdateAvailable = latest > CurrentVersion;

                Logger.Info($"Update check: current {CurrentVersion}, latest {latest} " +
                            $"(tag {release.tag_name}), update available: {result.UpdateAvailable}.");
                return result;
            }
            catch (WebException wex)
            {
                // A 404 here usually just means no release has been published yet.
                var http = wex.Response as HttpWebResponse;
                if (http != null && http.StatusCode == HttpStatusCode.NotFound)
                {
                    result.Error = "No published release was found for " + Repository + " yet.";
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
                foreach (GitHubAsset asset in release.assets)
                {
                    if (asset != null && !string.IsNullOrWhiteSpace(asset.browser_download_url) &&
                        asset.name != null && asset.name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        return asset.browser_download_url;
                    }
                }
            }

            return string.IsNullOrWhiteSpace(release.html_url) ? ReleasesPageUrl : release.html_url;
        }
    }
}
