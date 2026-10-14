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
    /// available version, its <c>html_url</c> as the release page, and its
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

        /// <summary>The file a release ships for the in-app updater to swap in.</summary>
        public const string ExeAssetName = "Tempo.exe";

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

            /// <summary>The file's size in bytes, as GitHub stores it.</summary>
            public long size { get; set; }

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

            /// <summary>
            /// GitHub's SHA-256 for the asset at <see cref="DownloadUrl"/>, or null. Every asset has one,
            /// so a download can always be verified — unlike <see cref="Sha256Url"/>, a separate .sha256
            /// file the releases stopped carrying, which left in-app updates unverified.
            /// </summary>
            public string DownloadSha256 { get; set; }

            /// <summary>Size in bytes of the asset at <see cref="DownloadUrl"/>, or 0 when unknown.</summary>
            public long DownloadSize { get; set; }

            /// <summary>
            /// The release's own page on GitHub — what "Download page" opens.
            ///
            /// It used to open <see cref="DownloadUrl"/>, which is the Tempo.exe ASSET: a button
            /// labelled as a page started a 106 MB download in the browser instead, and left one
            /// more loose copy of Tempo in the Downloads folder — the portable copies that fall
            /// behind every update after it.
            /// </summary>
            public string ReleasePageUrl { get; set; }

            public string Notes { get; set; }
            public DateTime? ReleaseDate { get; set; }

            /// <summary>
            /// The check was refused for GitHub's rate limit (or not sent, because it has not reset yet).
            /// </summary>
            public bool RateLimited { get; set; }

            /// <summary>
            /// When asking again can work — the rate limit's reset — or null. The automatic retry waits for
            /// it: its 5/15/60-minute steps used to fire straight into the same refusal.
            /// </summary>
            public DateTime? RetryAtUtc { get; set; }
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
        /// Deliberately separate from <see cref="Check"/>: that asks "what is
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

            // Through GitHubApi: the proxy, the rate limit (not asked at all while it is spent) and TLS
            // are decided there, once, for the update check too.
            GitHubApi.Answer answer = GitHubApi.Get("/repos/" + Repository + "/releases/tags/" + Uri.EscapeDataString(tag));

            // A 404 is an ANSWER, not a failure: GitHub is telling us this version was never
            // published. Everything else that is not a 200 means we could not ask.
            if (answer.Status == 404) { return ReleaseLookup.NoSuchRelease; }
            if (!answer.Ok)
            {
                error = answer.Error ?? "no answer";
                return ReleaseLookup.Unavailable;
            }

            try
            {
                release = JsonSerializer.Deserialize<GitHubRelease>(answer.Body, JsonOptions);
            }
            catch (JsonException)
            {
                release = null;
            }
            if (release == null || string.IsNullOrWhiteSpace(release.tag_name))
            {
                release = null;
                error = "the release data could not be read";
                return ReleaseLookup.Unavailable;
            }
            return ReleaseLookup.Found;
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
        /// The longest a <see cref="Check"/> can take: two attempts of up to 18 s each (GitHubApi's
        /// timeout covers the answer AND reading it), and the pause between them. A caller waiting on a
        /// check uses this, so its safety net cannot fire while a slow but healthy check is still
        /// running — the old 22 s net was shorter than the check it guarded.
        /// </summary>
        public static readonly TimeSpan WorstCaseCheckTime = TimeSpan.FromSeconds(2 * 18 + 2);

        /// <summary>
        /// Blocking fetch + compare. Call from a background thread. Never throws —
        /// failures are returned in <see cref="UpdateResult.Error"/>, already translated,
        /// because that text is shown to the user as it is.
        /// </summary>
        public static UpdateResult Check()
        {
            var result = new UpdateResult();
            // One retry. With a generous 18 s per attempt (GitHubApi), a second attempt keeps the
            // worst case inside WorstCaseCheckTime while still riding out a slow cold connection or a
            // brief server hiccup.
            const int maxAttempts = 2;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                GitHubApi.Answer answer = GitHubApi.Get("/repos/" + Repository + "/releases/latest");
                if (answer.Ok)
                {
                    return FromReleaseJson(answer.Body, CurrentVersion);
                }

                // A server error, a dropped connection or a timeout (often just a slow cold connection)
                // usually clears on an immediate retry, so try again before bothering the user. A rate
                // limit is NOT retried: it means "stop asking", and GitHubApi now holds every request
                // back until it resets.
                if (answer.Transient && !answer.RateLimited && attempt < maxAttempts)
                {
                    Logger.Warn("[Update] check attempt " + attempt + " of " + maxAttempts + " failed (" + answer.Error + "); trying again.");
                    try { System.Threading.Thread.Sleep(600 * attempt); } catch { }
                    continue;
                }

                result.Error = DescribeFailure(answer);
                result.RetryAtUtc = answer.RetryAtUtc;
                result.RateLimited = answer.RateLimited;
                Logger.Warn("[Update] the check failed: " + (answer.Error ?? "no answer") + ".");
                return result;
            }

            // Reached only if every attempt hit a transient error without producing a
            // more specific message above (defensive - the loop normally returns first).
            if (string.IsNullOrEmpty(result.Error))
            {
                result.Error = Localization.T("GitHub's update server is having a temporary problem. " +
                               "This isn't a problem with Tempo — please try again in a few minutes.");
            }
            return result;
        }

        /// <summary>
        /// Turns GitHub's answer into a result, with no network involved — so the parsing, the asset
        /// choice and the comparison can be tested against real release JSON. <paramref name="current"/>
        /// is explicit for the same reason <see cref="IsNewerThan"/> takes it.
        /// </summary>
        internal static UpdateResult FromReleaseJson(string json, Version current)
        {
            var result = new UpdateResult();
            if (string.IsNullOrWhiteSpace(json))
            {
                result.Error = Localization.T("The update server returned an empty response.");
                return result;
            }

            GitHubRelease release;
            try
            {
                release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);
            }
            catch (JsonException jex)
            {
                // Something other than GitHub answered — a Wi-Fi sign-in page or a proxy's error page.
                // The parser's own message ("'<' is an invalid start of a value…") said nothing useful.
                Logger.Warn("[Update] the answer was not release data: " + jex.Message);
                result.Error = Localization.T("GitHub's answer could not be read. A network sign-in page or a proxy can cause this — please try again later.");
                return result;
            }
            if (release == null || string.IsNullOrWhiteSpace(release.tag_name))
            {
                result.Error = Localization.T("No published release was found yet.");
                return result;
            }

            if (!TryParseTag(release.tag_name, out Version latest))
            {
                result.Error = Localization.F("The latest release tag ({0}) is not a valid version number.",
                                              release.tag_name);
                return result;
            }

            result.Success = true;
            result.LatestVersion = latest;
            result.Notes = string.IsNullOrWhiteSpace(release.body) ? release.name : release.body;
            result.DownloadUrl = PickDownloadUrl(release);
            result.Sha256Url = PickChecksumUrl(release);
            result.DownloadSha256 = DigestFor(release, result.DownloadUrl);
            result.DownloadSize = SizeFor(release, result.DownloadUrl);
            result.ReleasePageUrl = string.IsNullOrWhiteSpace(release.html_url) ? ReleasesPageUrl : release.html_url;
            // Both sides normalised: comparing a 3-part tag with a 4-part assembly version directly
            // is the part-count trap described on Normalize.
            result.UpdateAvailable = Normalize(latest) > Normalize(current);

            if (!string.IsNullOrWhiteSpace(release.published_at) &&
                DateTime.TryParse(release.published_at,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime pub))
            {
                result.ReleaseDate = DateTime.SpecifyKind(pub, DateTimeKind.Utc);
            }

            Logger.Info($"[Update] check: current {current}, latest {latest} " +
                        $"(tag {release.tag_name}), update available: {result.UpdateAvailable}.");
            return result;
        }

        /// <summary>
        /// What to tell the user about a check that could not complete. Translated here because
        /// the result goes straight onto the screen, and several carry a value (a status code, a
        /// time), which a finished English sentence could never be looked up by.
        /// </summary>
        private static string DescribeFailure(GitHubApi.Answer answer)
        {
            int code = answer.Status;
            if (code == 404)
            {
                // A 404 here usually just means no release has been published yet.
                return Localization.F("No published release was found for {0} yet.", Repository);
            }

            if (answer.RateLimited)
            {
                // GitHub allows 60 unauthenticated API requests an hour per address. When it says
                // when the allowance comes back, pass that on: "try again later" with no idea how
                // much later is how the button gets pressed ten more times.
                if (answer.RetryAtUtc.HasValue)
                {
                    return Localization.F("GitHub is limiting how often Tempo can ask about updates. Please try again after {0}.",
                                          answer.RetryAtUtc.Value.ToLocalTime().ToString("t", Localization.DateCulture));
                }
                return Localization.T("GitHub is rate-limiting update checks right now. " +
                                      "Please wait a little while and try again.");
            }

            if (code == 403)
            {
                // Not the rate limit (that says so in its headers): something in between refused it.
                return Localization.T("GitHub refused the update check (error 403). A firewall, proxy or network filter " +
                                      "may be blocking Tempo's connection to github.com.");
            }

            if (answer.TimedOut)
            {
                // Not "your internet is fine": a timeout proves nothing about the connection, and
                // this sentence used to claim that it did.
                return Localization.T("The update check timed out, even after retrying. GitHub may be slow right now, " +
                                      "or a firewall, VPN or antivirus may be blocking Tempo's connection to github.com. " +
                                      "Please try again in a moment.");
            }

            if (code >= 500)
            {
                // A gateway/server error on GitHub's end, not a problem with Tempo or
                // your PC, and almost always temporary - even after the retry above.
                return Localization.F("GitHub's update server is having a temporary problem (error {0}). " +
                                      "This isn't a problem with Tempo — please try again in a few minutes.", code);
            }

            if (code > 0)
            {
                return Localization.F("The update server returned an error ({0}). Please try again later.", code);
            }

            return Localization.T("Couldn't reach GitHub. Check your internet connection.");
        }

        /// <summary>
        /// A version with every part defined, so two of them can be compared without the part-count
        /// trap. <c>Version.TryParse("1.0.321")</c> leaves Revision = -1, while an assembly version is
        /// always four parts (1.0.321.0) — so the release compares as OLDER than the identical build it
        /// was cut from, and their <c>ToString()</c>s are different strings. That is precisely how
        /// "you are on the latest" turned into "update available".
        /// </summary>
        public static Version Normalize(Version v)
        {
            if (v == null) { return new Version(0, 0, 0, 0); }
            return new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor),
                               Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }

        /// <summary>
        /// A version as people read it: "1.0.323", with a fourth part only when it is not zero. The
        /// update dialog used to show "Installed 1.0.322.0 → Latest 1.0.323" — the same kind of
        /// number spelled two different ways side by side.
        /// </summary>
        public static string Display(Version v)
        {
            if (v == null) { return ""; }
            Version n = Normalize(v);
            return n.Revision > 0 ? n.ToString(4) : n.ToString(3);
        }

        /// <summary><see cref="Display(Version)"/> for a stored or tagged version string; the input unchanged when it is not one.</summary>
        public static string Display(string version)
        {
            return TryParseTag(version, out Version v) ? Display(v) : (version ?? "");
        }

        /// <summary>
        /// True when two spellings name the same version ("v1.0.323", "1.0.323", "1.0.323.0"). A skipped
        /// version is compared this way, never as a string — the string test is what kept "update
        /// available" on screen for the version already installed.
        /// </summary>
        public static bool IsSameVersion(string a, string b)
        {
            return TryParseTag(a, out Version va) && TryParseTag(b, out Version vb)
                   && Normalize(va) == Normalize(vb);
        }

        /// <summary>
        /// True when <paramref name="version"/> — a release tag, or the cached "latest seen" string — is
        /// genuinely newer than <paramref name="current"/>. Takes the current version explicitly so the
        /// rule can be exercised without being the running build.
        /// </summary>
        public static bool IsNewerThan(string version, Version current)
        {
            return TryParseTag(version, out Version parsed) && Normalize(parsed) > Normalize(current);
        }

        /// <summary>
        /// True when that version is newer than the build running now. THE single place this is decided,
        /// so the sidebar note, the Settings line and the launch check can never disagree with each other.
        /// </summary>
        public static bool IsNewerThanCurrent(string version)
        {
            return IsNewerThan(version, CurrentVersion);
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
            if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
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
        /// The asset the in-app updater should fetch: Tempo.exe by name first, so it can be
        /// swapped in place; then any other .exe; then a setup zip (unpacked afterwards); then
        /// any zip; otherwise the release's web page.
        ///
        /// By name first, because "the first .exe" is only right while a release carries exactly
        /// one — an installer or a tool attached beside Tempo.exe would otherwise be swapped in
        /// over Tempo itself.
        /// </summary>
        private static string PickDownloadUrl(GitHubRelease release)
        {
            if (release.assets != null)
            {
                string named = null, setup = null, exe = null, anyZip = null;
                foreach (GitHubAsset asset in release.assets)
                {
                    if (asset == null || string.IsNullOrWhiteSpace(asset.browser_download_url) ||
                        string.IsNullOrWhiteSpace(asset.name))
                    {
                        continue;
                    }
                    string n = asset.name;
                    if (named == null && string.Equals(n, ExeAssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        named = asset.browser_download_url;
                    }
                    else if (exe == null && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
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
                if (named != null) return named;
                if (exe != null) return exe;
                if (setup != null) return setup;
                if (anyZip != null) return anyZip;
            }

            return string.IsNullOrWhiteSpace(release.html_url) ? ReleasesPageUrl : release.html_url;
        }

        /// <summary>The asset served at <paramref name="url"/>, or null.</summary>
        private static GitHubAsset AssetAt(GitHubRelease release, string url)
        {
            if (release?.assets == null || string.IsNullOrWhiteSpace(url)) { return null; }
            foreach (GitHubAsset asset in release.assets)
            {
                if (asset != null && string.Equals(asset.browser_download_url, url, StringComparison.Ordinal))
                {
                    return asset;
                }
            }
            return null;
        }

        /// <summary>The hex SHA-256 GitHub holds for the asset served at <paramref name="url"/>, or null.</summary>
        internal static string DigestFor(GitHubRelease release, string url)
        {
            GitHubAsset asset = AssetAt(release, url);
            if (asset == null) { return null; }
            string d = (asset.digest ?? "").Trim();
            const string prefix = "sha256:";
            if (!d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { return null; }
            string hex = d.Substring(prefix.Length);
            if (hex.Length != 64) { return null; }
            foreach (char c in hex)
            {
                if (!Uri.IsHexDigit(c)) { return null; }
            }
            return hex;
        }

        /// <summary>The size GitHub reports for the asset served at <paramref name="url"/>, or 0.</summary>
        internal static long SizeFor(GitHubRelease release, string url)
        {
            GitHubAsset asset = AssetAt(release, url);
            return asset != null && asset.size > 0 ? asset.size : 0;
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
