using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Launches the Roblox client as a saved account, and can enable multi-instance so several
    /// accounts run at once.
    ///
    /// USER-TESTED — every line here touches the live client and its anti-cheat, which change
    /// without notice and cannot be exercised from inside Tempo. Multi-instance is the most
    /// Terms-sensitive part of the whole manager: running several clients at once steps around
    /// a lock the client sets deliberately, and can get accounts banned. It exists because it
    /// was asked for; the warning shown beside the toggle is not decoration.
    /// </summary>
    public static class RobloxLauncher
    {
        public static bool MultiInstanceEnabled => RobloxSingletonKiller.IsRunning;

        /// <summary>
        /// Turn on multi-instance. Holding the singleton object ourselves is dead on the current
        /// client (proven in this project's logs), so this instead starts a background worker that
        /// CLOSES the running client's own singleton handle from outside — the only non-injecting
        /// method that can still work. See <see cref="RobloxSingletonKiller"/>. Idempotent.
        /// </summary>
        public static void EnableMultiInstance() => RobloxSingletonKiller.Start();

        /// <summary>Turn multi-instance off — stop closing the client's singleton handle.</summary>
        public static void DisableMultiInstance() => RobloxSingletonKiller.Stop();

        /// <summary>What a launch attempt did, so the caller can react precisely.</summary>
        public enum LaunchResult
        {
            Launched,
            NotInstalled,   // no Roblox client on disk — offer to install
            NoTicket,       // couldn't mint a ticket, usually an expired session — offer re-login
            Error
        }

        /// <summary>
        /// Launch the client as this account. With <paramref name="home"/> true (or no place id)
        /// it opens the client to the Roblox home with no game — "launch Roblox without a game".
        /// Otherwise it joins <paramref name="placeId"/>, optionally into the private server named
        /// by <paramref name="privateServer"/> (its access/link code). Checks the client is
        /// installed first, then mints a ticket and hands the launch to the local client.
        /// </summary>
        public static async Task<LaunchResult> LaunchAsync(string cookie, long placeId,
                                                            string privateServer = null, bool home = false)
        {
            // Use the local install — no point minting a session ticket for a client that
            // isn't there. (This is also what ties Launch to the install detection.)
            if (!RobloxInstall.IsInstalled())
            {
                Logger.Warn("[Roblox] launch aborted — no Roblox client is installed.");
                return LaunchResult.NotInstalled;
            }

            string ticket = await RobloxApi.GetAuthTicketAsync(cookie);
            if (string.IsNullOrEmpty(ticket))
            {
                return LaunchResult.NoTicket;
            }

            long launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string uri;

            // The client's launch protocol. The exact token set drifts between client versions;
            // these are the widely-used shapes and the obvious place to adjust if a future client
            // rejects them. The OS routes roblox-player: to the local install.
            if (home || placeId <= 0)
            {
                // launchmode:app opens the app to the home screen instead of joining a game.
                uri = "roblox-player:1+launchmode:app"
                    + "+gameinfo:" + ticket
                    + "+launchtime:" + launchTime
                    + "+robloxLocale:en_us+gameLocale:en_us";
            }
            else
            {
                string placeLauncher =
                    "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request="
                    + (string.IsNullOrWhiteSpace(privateServer) ? "RequestGame" : "RequestPrivateGame")
                    + "&browserTrackerId=0&placeId=" + placeId;
                if (!string.IsNullOrWhiteSpace(privateServer))
                {
                    placeLauncher += "&accessCode=" + Uri.EscapeDataString(privateServer.Trim());
                }
                placeLauncher += "&isPlayTogetherGame=false";

                uri = "roblox-player:1+launchmode:play"
                    + "+gameinfo:" + ticket
                    + "+launchtime:" + launchTime
                    + "+placelauncherurl:" + Uri.EscapeDataString(placeLauncher)
                    + "+robloxLocale:en_us+gameLocale:en_us";
            }

            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                Logger.Info("[Roblox] launch requested (" + (home || placeId <= 0 ? "home" : "placeId " + placeId)
                    + ", multi-instance " + (MultiInstanceEnabled ? "on" : "off") + ").");
                return LaunchResult.Launched;
            }
            catch (Exception ex)
            {
                Logger.Warn("[Roblox] launch failed: " + ex.Message);
                return LaunchResult.Error;
            }
        }

        /// <summary>
        /// Launch the client as this account and FOLLOW another user into whatever game they are in
        /// right now — RAM's "Follow". Uses the client's <c>RequestFollowUser</c> launch request with
        /// the target's user id; the client resolves which server that user is in and joins it. It
        /// works when the target's join privacy allows being followed (a main account the user owns
        /// can be set to allow it). Same install + ticket path as <see cref="LaunchAsync"/>.
        /// </summary>
        public static async Task<LaunchResult> LaunchFollowAsync(string cookie, long targetUserId)
        {
            if (targetUserId <= 0) { return LaunchResult.Error; }
            if (!RobloxInstall.IsInstalled())
            {
                Logger.Warn("[Roblox] follow aborted — no Roblox client is installed.");
                return LaunchResult.NotInstalled;
            }

            string ticket = await RobloxApi.GetAuthTicketAsync(cookie);
            if (string.IsNullOrEmpty(ticket))
            {
                return LaunchResult.NoTicket;
            }

            long launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // RequestFollowUser lets the client find the target's current server and join it — no
            // place id needed on our side. Same launchmode:play + placelauncherurl shape as a join.
            string placeLauncher =
                "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestFollowUser"
                + "&browserTrackerId=0&userId=" + targetUserId;

            string uri = "roblox-player:1+launchmode:play"
                + "+gameinfo:" + ticket
                + "+launchtime:" + launchTime
                + "+placelauncherurl:" + Uri.EscapeDataString(placeLauncher)
                + "+robloxLocale:en_us+gameLocale:en_us";

            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                Logger.Info("[Roblox] follow launch requested (target user " + targetUserId
                    + ", multi-instance " + (MultiInstanceEnabled ? "on" : "off") + ").");
                return LaunchResult.Launched;
            }
            catch (Exception ex)
            {
                Logger.Warn("[Roblox] follow launch failed: " + ex.Message);
                return LaunchResult.Error;
            }
        }

        /// <summary>
        /// Pull a numeric place id out of a roblox.com game link
        /// (<c>roblox.com/games/&lt;id&gt;/Name</c>). 0 when there isn't one.
        /// </summary>
        public static long PlaceIdFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) { return 0; }
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(url, @"/games/(\d+)");
                if (m.Success && long.TryParse(m.Groups[1].Value, out long id)) { return id; }
            }
            catch { }
            return 0;
        }
    }
}
