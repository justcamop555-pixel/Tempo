using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Talks to Roblox's own web API with a stored <c>.ROBLOSECURITY</c> cookie — the same
    /// endpoints the roblox.com site itself calls. It does two things: read which account a
    /// cookie belongs to, and mint a one-time launch ticket for that account.
    ///
    /// It NEVER sends a password and never performs a login — the cookie was obtained by the
    /// user signing in themselves in the embedded browser (<see cref="UI.RobloxLoginForm"/>).
    /// The cookie is a secret: it is passed in per call, put on a request header, and never
    /// logged. USER-TESTED end to end — these are live Roblox endpoints that change without
    /// notice and cannot be exercised from inside Tempo.
    /// </summary>
    public static class RobloxApi
    {
        // AllowAutoRedirect off (we read 403/302 responses for their headers); UseCookies off
        // (the cookie is set explicitly per request, so nothing is cached between accounts).
        private static readonly HttpClient Http = Build();

        private static HttpClient Build()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Add("User-Agent", "Roblox/WinInet");
            return client;
        }

        // A second client for PUBLIC, cookie-free endpoints (game info + thumbnail images). It follows
        // redirects (image CDN URLs can 30x) unlike the auth client, and never carries a cookie.
        private static readonly HttpClient PublicHttp = BuildPublic();

        private static HttpClient BuildPublic()
        {
            var client = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = true })
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            client.DefaultRequestHeaders.Add("User-Agent", "Roblox/WinInet");
            return client;
        }

        public sealed class RobloxUser
        {
            public long Id;
            public string Name = "";
            public string DisplayName = "";
        }

        /// <summary>What a Place ID resolves to, for the "confirm the game before you launch" preview.</summary>
        public sealed class GameInfo
        {
            public long PlaceId;
            public long UniverseId;
            public string Name = "";
            public string Creator = "";
            public string IconUrl = "";
            public long Playing = -1;      // active players right now (-1 = unknown)
            public long Visits = -1;       // total visits (-1 = unknown)
            public int MaxPlayers = -1;    // server size (-1 = unknown)
            public int LikePercent = -1;   // 0..100 like ratio (-1 = unknown / no votes)
        }

        /// <summary>
        /// Look up the game a Place ID belongs to — its name, creator and icon URL — so the UI can
        /// show "you've got the right game" before launching. All three calls are PUBLIC (no cookie):
        /// placeId → universeId (apis.roblox.com), universeId → name/creator (games.roblox.com), and
        /// universeId → icon image URL (thumbnails.roblox.com). Null when the id doesn't resolve to a
        /// game (bad id) or on any failure; name/icon are best-effort (may be empty individually).
        /// </summary>
        public static async Task<GameInfo> GetGameInfoAsync(long placeId)
        {
            if (placeId <= 0) { return null; }
            try
            {
                long universeId = 0;
                using (var resp = await PublicHttp.GetAsync(
                    "https://apis.roblox.com/universes/v1/places/" + placeId + "/universe"))
                {
                    if (!resp.IsSuccessStatusCode) { return null; }
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("universeId", out var u)
                        && u.ValueKind == JsonValueKind.Number) { universeId = u.GetInt64(); }
                }
                if (universeId <= 0) { return null; }   // not a real place

                var info = new GameInfo { PlaceId = placeId, UniverseId = universeId };

                // Name + creator (best-effort).
                try
                {
                    using var resp = await PublicHttp.GetAsync(
                        "https://games.roblox.com/v1/games?universeIds=" + universeId);
                    if (resp.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                        if (doc.RootElement.TryGetProperty("data", out var data)
                            && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
                        {
                            var g = data[0];
                            info.Name = g.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            if (g.TryGetProperty("creator", out var c)
                                && c.TryGetProperty("name", out var cn)) { info.Creator = cn.GetString() ?? ""; }
                            if (g.TryGetProperty("playing", out var pl) && pl.ValueKind == JsonValueKind.Number) { info.Playing = pl.GetInt64(); }
                            if (g.TryGetProperty("visits", out var vi) && vi.ValueKind == JsonValueKind.Number) { info.Visits = vi.GetInt64(); }
                            if (g.TryGetProperty("maxPlayers", out var mp) && mp.ValueKind == JsonValueKind.Number) { info.MaxPlayers = mp.GetInt32(); }
                        }
                    }
                }
                catch { }

                // Like ratio (best-effort): a separate public votes endpoint.
                try
                {
                    using var resp = await PublicHttp.GetAsync(
                        "https://games.roblox.com/v1/games/votes?universeIds=" + universeId);
                    if (resp.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                        if (doc.RootElement.TryGetProperty("data", out var data)
                            && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
                        {
                            var v = data[0];
                            long up = v.TryGetProperty("upVotes", out var uv) && uv.ValueKind == JsonValueKind.Number ? uv.GetInt64() : 0;
                            long down = v.TryGetProperty("downVotes", out var dv) && dv.ValueKind == JsonValueKind.Number ? dv.GetInt64() : 0;
                            if (up + down > 0) { info.LikePercent = (int)System.Math.Round(100.0 * up / (up + down)); }
                        }
                    }
                }
                catch { }

                // Icon image URL (best-effort). thumbnails are prepared async server-side; "Completed"
                // means the URL is ready.
                try
                {
                    using var resp = await PublicHttp.GetAsync(
                        "https://thumbnails.roblox.com/v1/games/icons?universeIds=" + universeId
                        + "&size=150x150&format=Png&isCircular=false");
                    if (resp.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                        if (doc.RootElement.TryGetProperty("data", out var data)
                            && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
                        {
                            var t = data[0];
                            bool ready = !t.TryGetProperty("state", out var st)
                                         || string.Equals(st.GetString(), "Completed", StringComparison.OrdinalIgnoreCase);
                            if (ready && t.TryGetProperty("imageUrl", out var iu))
                            {
                                info.IconUrl = iu.GetString() ?? "";
                            }
                        }
                    }
                }
                catch { }

                return info;
            }
            catch (Exception ex)
            {
                Logger.Warn("[Roblox] game info lookup failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>Download image bytes (a game icon URL from <see cref="GetGameInfoAsync"/>), or null.</summary>
        public static async Task<byte[]> GetImageBytesAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) { return null; }
            try { return await PublicHttp.GetByteArrayAsync(url); }
            catch { return null; }
        }

        /// <summary>Who the cookie is signed in as, or null on any failure (e.g. expired cookie).</summary>
        public static async Task<RobloxUser> GetAuthenticatedUserAsync(string cookie)
        {
            if (string.IsNullOrEmpty(cookie)) { return null; }
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    "https://users.roblox.com/v1/users/authenticated");
                req.Headers.Add("Cookie", ".ROBLOSECURITY=" + cookie);
                using var resp = await Http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) { return null; }
                string json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                return new RobloxUser
                {
                    Id = root.TryGetProperty("id", out var id) ? id.GetInt64() : 0,
                    Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    DisplayName = root.TryGetProperty("displayName", out var d) ? d.GetString() ?? "" : ""
                };
            }
            catch (Exception ex)
            {
                Logger.Warn("[Roblox] authenticated-user lookup failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>The verdict on a saved session, kept apart from a transient failure.</summary>
        public enum SessionCheck { Valid, Expired, Unknown }

        /// <summary>
        /// Check whether a saved session still works, distinguishing a DEFINITELY-expired cookie
        /// (Roblox answers 401) from a transient failure (network / 5xx / rate-limit) — so a caller
        /// that colours a status dot never paints a good session red over a blip. Valid = 200;
        /// Expired = 401; Unknown = anything else, incl. no network.
        /// </summary>
        public static async Task<SessionCheck> ValidateSessionAsync(string cookie)
        {
            if (string.IsNullOrEmpty(cookie)) { return SessionCheck.Unknown; }
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    "https://users.roblox.com/v1/users/authenticated");
                req.Headers.Add("Cookie", ".ROBLOSECURITY=" + cookie);
                using var resp = await Http.SendAsync(req);
                if (resp.IsSuccessStatusCode) { return SessionCheck.Valid; }
                if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized) { return SessionCheck.Expired; }
                return SessionCheck.Unknown;   // 429 / 5xx / redirect — don't judge the session on it
            }
            catch
            {
                return SessionCheck.Unknown;   // network / timeout — unknown, NOT expired
            }
        }

        /// <summary>
        /// Resolve a Roblox username to its user (id + canonical name), or null if there is no such
        /// user. Public endpoint — no cookie needed. Used by "Follow" to turn a typed username into
        /// the user id the client follows into a game.
        /// </summary>
        public static async Task<RobloxUser> ResolveUsernameAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) { return null; }
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post,
                    "https://users.roblox.com/v1/usernames/users");
                string body = "{\"usernames\":[" + JsonSerializer.Serialize(username.Trim())
                    + "],\"excludeBannedUsers\":false}";
                req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                using var resp = await Http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) { return null; }
                string json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var data)
                    || data.ValueKind != JsonValueKind.Array) { return null; }
                foreach (var u in data.EnumerateArray())
                {
                    long id = u.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0;
                    if (id <= 0) { continue; }
                    return new RobloxUser
                    {
                        Id = id,
                        Name = u.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        DisplayName = u.TryGetProperty("displayName", out var d) ? d.GetString() ?? "" : ""
                    };
                }
                return null;
            }
            catch (Exception ex)
            {
                Logger.Warn("[Roblox] username lookup failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Mint a one-time authentication ticket for launching the client as this account.
        /// This is the two-step CSRF handshake the site's Play button uses: the first POST is
        /// refused with an <c>x-csrf-token</c>, and the second carries it back and returns the
        /// ticket in <c>rbx-authentication-ticket</c>. Null on failure (an expired cookie is
        /// the usual cause — the account then needs re-login).
        /// </summary>
        public static async Task<string> GetAuthTicketAsync(string cookie)
        {
            if (string.IsNullOrEmpty(cookie)) { return null; }
            string csrf = await GetCsrfTokenAsync(cookie);
            if (string.IsNullOrEmpty(csrf)) { return null; }
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post,
                    "https://auth.roblox.com/v1/authentication-ticket");
                req.Headers.Add("Cookie", ".ROBLOSECURITY=" + cookie);
                req.Headers.Add("X-CSRF-TOKEN", csrf);
                req.Headers.Add("Origin", "https://www.roblox.com");
                req.Headers.Referrer = new Uri("https://www.roblox.com/");
                req.Content = new StringContent(string.Empty);
                using var resp = await Http.SendAsync(req);
                if (resp.Headers.TryGetValues("rbx-authentication-ticket", out var tickets))
                {
                    foreach (string t in tickets) { return t; }
                }
                Logger.Warn("[Roblox] no authentication ticket in the response (status " + (int)resp.StatusCode + ").");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Warn("[Roblox] authentication-ticket request failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>First (deliberately unauthenticated) POST to collect the CSRF token.</summary>
        private static async Task<string> GetCsrfTokenAsync(string cookie)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post,
                    "https://auth.roblox.com/v1/authentication-ticket");
                req.Headers.Add("Cookie", ".ROBLOSECURITY=" + cookie);
                req.Headers.Referrer = new Uri("https://www.roblox.com/");
                req.Content = new StringContent(string.Empty);
                using var resp = await Http.SendAsync(req);
                if (resp.Headers.TryGetValues("x-csrf-token", out var tokens))
                {
                    foreach (string t in tokens) { return t; }
                }
                return null;
            }
            catch (Exception ex)
            {
                Logger.Warn("[Roblox] CSRF handshake failed: " + ex.Message);
                return null;
            }
        }
    }
}
