using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Every question Tempo asks api.github.com — the update check and the release check behind the
    /// Official ID — goes through here, so they share one set of rules instead of two copies of them:
    ///
    ///   * The allowance is REMEMBERED. Without signing in GitHub answers 60 requests an hour per
    ///     address, and on a shared one (a school, a café, a VPN) that is often already spent. When it
    ///     says so — X-RateLimit-Remaining 0 with its reset time, or Retry-After — nothing is asked
    ///     again before then: the update check and the release check used to keep asking (5, 15, 60
    ///     minutes later), each one only another refusal, and the user saw "try again later" with no
    ///     idea how much later.
    ///   * A proxy set in Windows is used (<see cref="SystemProxy"/>). It was switched off outright, so
    ///     on a network that only lets traffic out through one, GitHub could never be reached.
    ///   * TLS is left to Windows — 1.3 where it can be. Both requests used to pin it to 1.2.
    ///   * What GitHub last said is kept for Live debug: the answer, the requests left, when they reset.
    ///
    /// Blocking; call it off the UI thread. Never throws.
    /// </summary>
    public static class GitHubApi
    {
        public const string Root = "https://api.github.com";

        /// <summary>For the tests: a local server instead of GitHub.</summary>
        internal static string RootForTest;

        /// <summary>For the tests: this proxy instead of the one Windows is set to use.</summary>
        internal static IWebProxy ProxyForTest;

        /// <summary>The longest Tempo will stay quiet on GitHub's word — a reset time years out is a bad header.</summary>
        private static readonly TimeSpan LongestPause = TimeSpan.FromHours(2);

        /// <summary>What one request got.</summary>
        public sealed class Answer
        {
            /// <summary>The HTTP status, or 0 when there was no answer at all.</summary>
            public int Status;
            /// <summary>The body of a 200, else null.</summary>
            public string Body;
            /// <summary>Refused for the rate limit — or not even asked, because it has not reset yet.</summary>
            public bool RateLimited;
            /// <summary>When asking again makes sense (the rate limit's reset), or null.</summary>
            public DateTime? RetryAtUtc;
            /// <summary>A timeout, a dropped connection or a server error: worth one more try.</summary>
            public bool Transient;
            public bool TimedOut;
            /// <summary>What went wrong, in English, for the log.</summary>
            public string Error;

            public bool Ok => Status == 200 && Body != null;
        }

        private static readonly object Gate = new object();
        private static int _limit = -1, _remaining = -1;
        private static DateTime? _resetUtc;
        private static DateTime? _quietUntilUtc;
        private static int _lastStatus = -1;
        private static DateTime? _lastUtc;
        private static string _lastPath;
        private static int _asked, _limited, _heldBack;

        /// <summary>Until when nothing is asked — GitHub's rate limit has not reset yet — or null.</summary>
        public static DateTime? QuietUntilUtc
        {
            get
            {
                lock (Gate)
                {
                    return _quietUntilUtc.HasValue && _quietUntilUtc.Value > DateTime.UtcNow ? _quietUntilUtc : null;
                }
            }
        }

        /// <summary>GET <paramref name="path"/> ("/repos/…") from the API.</summary>
        public static Answer Get(string path, int timeoutMs = 18000)
        {
            DateTime? quiet = QuietUntilUtc;
            if (quiet.HasValue)
            {
                lock (Gate) { _heldBack++; }
                return new Answer
                {
                    RateLimited = true,
                    RetryAtUtc = quiet,
                    Error = "not asked: GitHub's rate limit resets at " + quiet.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)
                };
            }

            string url = (RootForTest ?? Root).TrimEnd('/') + path;
            var answer = new Answer();
            try
            {
                IWebProxy proxy = ProxyForTest ?? SystemProxy.Get();
                var handler = new SocketsHttpHandler
                {
                    UseProxy = proxy != null,
                    Proxy = proxy,
                    DefaultProxyCredentials = CredentialCache.DefaultCredentials,
                    ConnectTimeout = TimeSpan.FromMilliseconds(Math.Min(timeoutMs, 10000)),
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    AllowAutoRedirect = true
                };
                using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) })
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    // GitHub requires a User-Agent and recommends naming the API version.
                    request.Headers.TryAddWithoutValidation("User-Agent",
                        "Tempo/" + UpdateChecker.CurrentVersion + " (+https://github.com/" + UpdateChecker.Repository + ")");
                    request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
                    request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
                    request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };

                    lock (Gate) { _asked++; }
                    using (HttpResponseMessage response = client.Send(request, HttpCompletionOption.ResponseContentRead))
                    {
                        answer.Status = (int)response.StatusCode;
                        string body;
                        using (Stream s = response.Content.ReadAsStream())
                        using (var reader = new StreamReader(s, Encoding.UTF8))
                        {
                            body = reader.ReadToEnd();
                        }
                        if (answer.Status == 200) { answer.Body = body; }
                        Note(path, response, answer, body);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                answer.TimedOut = true;
                answer.Transient = true;
                answer.Error = "timed out after " + timeoutMs / 1000 + " s";
            }
            catch (HttpRequestException ex)
            {
                answer.Transient = true;
                answer.Error = ex.InnerException != null ? ex.Message + " (" + ex.InnerException.Message + ")" : ex.Message;
            }
            catch (Exception ex)
            {
                answer.Error = ex.Message;
            }

            if (answer.Status == 0)
            {
                lock (Gate) { _lastStatus = 0; _lastUtc = DateTime.UtcNow; _lastPath = path; }
            }
            return answer;
        }

        /// <summary>Reads the allowance off a response and decides whether this one was refused for it.</summary>
        private static void Note(string path, HttpResponseMessage response, Answer answer, string body)
        {
            int limit = HeaderInt(response, "X-RateLimit-Limit");
            int remaining = HeaderInt(response, "X-RateLimit-Remaining");
            long resetEpoch = HeaderLong(response, "X-RateLimit-Reset");
            DateTime now = DateTime.UtcNow;
            DateTime? reset = null;
            if (resetEpoch > 0)
            {
                // The reset is GitHub's clock; measured against GitHub's own Date header it becomes a wait,
                // which a PC whose clock is off by an hour still counts out correctly.
                DateTime resetServer = DateTimeOffset.FromUnixTimeSeconds(resetEpoch).UtcDateTime;
                DateTimeOffset? serverNow = response.Headers.Date;
                reset = serverNow.HasValue ? now + (resetServer - serverNow.Value.UtcDateTime) : resetServer;
            }

            // Retry-After: seconds, or a date. Sent with a 429, and with a 403 for GitHub's secondary
            // ("too many at once") limit.
            DateTime? retryAfter = null;
            RetryConditionHeaderValue ra = response.Headers.RetryAfter;
            if (ra != null)
            {
                if (ra.Delta.HasValue) { retryAfter = now + ra.Delta.Value; }
                else if (ra.Date.HasValue) { retryAfter = ra.Date.Value.UtcDateTime; }
            }

            int status = answer.Status;
            // GitHub's "secondary" limit (too many at once) can come as a bare 403 whose only sign is the
            // message; its advice is then to wait at least a minute.
            bool saysRateLimit = status == 403 && body != null
                                 && body.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0;
            bool limited = status == 429
                        || (status == 403 && (remaining == 0 || retryAfter.HasValue || saysRateLimit));
            DateTime? quiet = null;
            if (limited)
            {
                quiet = retryAfter ?? (remaining == 0 ? reset : null) ?? now.AddMinutes(1);
                // A reset already past (a stale header) would mean no pause at all after a refusal.
                if (quiet.Value <= now) { quiet = now.AddMinutes(1); }
            }
            else if (status == 200 && remaining == 0 && reset.HasValue)
            {
                // The last one there was: the next would only be refused.
                quiet = reset;
            }
            if (quiet.HasValue && quiet.Value - now > LongestPause) { quiet = now + LongestPause; }

            answer.RateLimited = limited;
            if (limited) { answer.RetryAtUtc = quiet; }
            if (status >= 500) { answer.Transient = true; }
            if (status != 200)
            {
                answer.Error = limited
                    ? "rate-limited by GitHub" + (quiet.HasValue ? " until " + quiet.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) : "")
                    : "HTTP " + status;
            }

            lock (Gate)
            {
                if (limit >= 0) { _limit = limit; }
                if (remaining >= 0) { _remaining = remaining; }
                if (reset.HasValue) { _resetUtc = reset; }
                if (quiet.HasValue) { _quietUntilUtc = quiet; }
                if (limited) { _limited++; }
                _lastStatus = status;
                _lastUtc = now;
                _lastPath = path;
            }

            if (limited)
            {
                Logger.Warn("[GitHub] " + path + " was refused for the rate limit; not asking again before "
                            + (quiet.HasValue ? quiet.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) : "a minute from now") + ".");
            }
            else
            {
                Logger.Info("[GitHub] " + path + " → " + status
                            + (remaining >= 0 && limit > 0 ? " (" + remaining + " of " + limit + " requests left this hour)" : "") + ".");
            }
        }

        private static int HeaderInt(HttpResponseMessage r, string name)
        {
            return r.Headers.TryGetValues(name, out var values)
                   && int.TryParse(System.Linq.Enumerable.FirstOrDefault(values), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                ? v : -1;
        }

        private static long HeaderLong(HttpResponseMessage r, string name)
        {
            return r.Headers.TryGetValues(name, out var values)
                   && long.TryParse(System.Linq.Enumerable.FirstOrDefault(values), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v)
                ? v : -1;
        }

        /// <summary>One line for Live debug: the last answer, the allowance and any pause.</summary>
        public static string Describe()
        {
            lock (Gate)
            {
                if (_asked == 0 && _heldBack == 0) { return "not asked yet this session"; }
                var sb = new StringBuilder();
                sb.Append(_asked).Append(" request(s)");
                if (_lastUtc.HasValue)
                {
                    sb.Append(", last ").Append(_lastPath).Append(" → ")
                      .Append(_lastStatus == 0 ? "no answer" : _lastStatus.ToString(CultureInfo.InvariantCulture))
                      .Append(' ').Append(Ago(_lastUtc.Value));
                }
                if (_remaining >= 0 && _limit > 0)
                {
                    sb.Append(" · ").Append(_remaining).Append(" of ").Append(_limit).Append(" left");
                    if (_resetUtc.HasValue) { sb.Append(", resets ").Append(_resetUtc.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)); }
                }
                if (_quietUntilUtc.HasValue && _quietUntilUtc.Value > DateTime.UtcNow)
                {
                    sb.Append(" · RATE-LIMITED: quiet until ").Append(_quietUntilUtc.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture));
                }
                if (_limited > 0) { sb.Append(" · refused for the limit ").Append(_limited).Append('×'); }
                if (_heldBack > 0) { sb.Append(" · ").Append(_heldBack).Append(" held back while limited"); }
                return sb.ToString();
            }
        }

        private static string Ago(DateTime utc)
        {
            TimeSpan t = DateTime.UtcNow - utc;
            if (t.TotalSeconds < 90) { return (int)Math.Max(0, t.TotalSeconds) + " s ago"; }
            if (t.TotalMinutes < 90) { return (int)t.TotalMinutes + " min ago"; }
            return (int)t.TotalHours + " h ago";
        }

        /// <summary>For the tests: forget everything GitHub has said.</summary>
        internal static void ResetForTest()
        {
            lock (Gate)
            {
                _limit = _remaining = -1;
                _resetUtc = _quietUntilUtc = _lastUtc = null;
                _lastStatus = -1;
                _lastPath = null;
                _asked = _limited = _heldBack = 0;
            }
        }
    }
}
