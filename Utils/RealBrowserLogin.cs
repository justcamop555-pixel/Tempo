using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Signs the user into Roblox in a REAL browser (Edge/Chrome/Brave/Opera) rather than an
    /// embedded control, because a full browser completes flows an embedded WebView cannot —
    /// most importantly creating a NEW account (the signup captcha). RAM works the same way.
    ///
    /// How it stays honest and does not become a "cookie stealer":
    ///   * It launches the browser with a THROWAWAY <c>--user-data-dir</c> under %TEMP% — a brand
    ///     new, empty profile. It never touches the user's real browser profile, cookies or
    ///     history, and the temp profile is deleted when the window closes.
    ///   * It reads the session back over the DevTools protocol on a loopback debug port of the
    ///     instance IT launched — the standard automation channel (Playwright/Puppeteer/Selenium
    ///     all use it), not the browser's on-disk cookie database.
    ///   * The user does the signing in. Tempo only watches its own throwaway profile for the
    ///     <c>.ROBLOSECURITY</c> cookie to appear.
    /// </summary>
    public static class RealBrowser
    {
        public sealed class Info
        {
            public string Path;
            public string Name;
            public string PrivateFlag;   // Edge: --guest (bypasses its auto-sign-in/sync, unlike --inprivate);
                                         // Chrome/Brave: --incognito; Opera: --private
        }

        /// <summary>
        /// The Chromium browser to open for sign-in. With <paramref name="preferred"/> naming an
        /// installed browser ("Chrome"/"Edge"/"Brave"/"Opera") that one is used; otherwise — or when
        /// the chosen one isn't installed — the best AVAILABLE is picked in preference order. That
        /// order prefers CHROME (its Incognito never auto-signs the window into a Windows/MS account
        /// and never syncs, so the Roblox page just works), then Edge (Guest mode gets the same),
        /// then Brave, then Opera. Null only when NONE of the supported browsers is installed.
        /// </summary>
        public static Info Locate(string preferred = null)
        {
            List<Info> found = Installed();
            if (found.Count == 0) { return null; }

            if (!string.IsNullOrWhiteSpace(preferred))
            {
                foreach (Info i in found)
                {
                    if (string.Equals(i.Name, preferred, StringComparison.OrdinalIgnoreCase)) { return i; }
                }
                // Preferred browser was uninstalled since it was chosen — fall back, don't fail.
            }
            return found[0];
        }

        /// <summary>
        /// Every SUPPORTED (Chromium) browser actually installed, in auto-preference order. Used to
        /// build the sign-in browser picker and to pick automatically. A browser here can be driven
        /// over the DevTools protocol for capture; Firefox/Safari cannot, so they never appear.
        /// </summary>
        public static List<Info> Installed()
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // App Paths first, then well-known install locations — a browser can be installed
            // without an App Paths entry, so don't rely on the registry alone. Order = preference:
            // Chrome first (no sign-in/sync friction), then Edge (Guest), Brave, Opera.
            var candidates = new (string exe, string name, string priv, string[] paths)[]
            {
                ("chrome.exe", "Chrome", "--incognito", new[]
                {
                    Join(pf, @"Google\Chrome\Application\chrome.exe"),
                    Join(pf86, @"Google\Chrome\Application\chrome.exe"),
                    Join(local, @"Google\Chrome\Application\chrome.exe"),
                }),
                ("msedge.exe", "Edge", "--guest", new[]
                {
                    Join(pf86, @"Microsoft\Edge\Application\msedge.exe"),
                    Join(pf, @"Microsoft\Edge\Application\msedge.exe"),
                }),
                ("brave.exe", "Brave", "--incognito", new[]
                {
                    Join(pf, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
                    Join(pf86, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
                    Join(local, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
                }),
                ("opera.exe", "Opera", "--private", new[]
                {
                    Join(local, @"Programs\Opera\opera.exe"),
                    Join(local, @"Programs\Opera\launcher.exe"),
                }),
            };

            var found = new List<Info>();
            foreach (var (exe, name, priv, paths) in candidates)
            {
                string p = FromAppPaths(exe);
                if (p == null)
                {
                    foreach (string known in paths)
                    {
                        if (!string.IsNullOrEmpty(known) && File.Exists(known)) { p = known; break; }
                    }
                }
                if (p != null) { found.Add(new Info { Path = p, Name = name, PrivateFlag = priv }); }
            }
            return found;
        }

        private static string Join(string root, string tail)
        {
            return string.IsNullOrEmpty(root) ? null : Path.Combine(root, tail);
        }

        private static string FromAppPaths(string exe)
        {
            foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using RegistryKey k = root.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe);
                    string path = k?.GetValue(null) as string;
                    if (!string.IsNullOrEmpty(path))
                    {
                        path = path.Trim('"');
                        if (File.Exists(path)) { return path; }
                    }
                }
                catch { }
            }
            return null;
        }

        /// <summary>A free loopback TCP port (grabbed and released — small race, fine for this).</summary>
        public static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        /// <summary>
        /// Command-line args that open an isolated, debuggable window at <paramref name="url"/>.
        /// Pass <paramref name="privateFlag"/> (from <see cref="Info.PrivateFlag"/>) to open it
        /// PRIVATE — no sign-in, no sync, and a visible InPrivate/Incognito window; leave it null
        /// for a persistent, ordinary window.
        /// </summary>
        public static string BuildArgs(string profileDir, int port, string url, string privateFlag = null)
        {
            // No --new-window: a fresh --user-data-dir opens a window on its own, and with Guest
            // mode --new-window makes the launched process fork/exit (which broke close-detection).
            string priv = string.IsNullOrEmpty(privateFlag) ? "" : (" " + privateFlag);
            return "--user-data-dir=\"" + profileDir + "\""
                + " --remote-debugging-port=" + port + priv
                + " --no-first-run --no-default-browser-check \"" + url + "\"";
        }
    }

    /// <summary>One cookie as reported by the DevTools protocol.</summary>
    public sealed class CdpCookie
    {
        public string Name;
        public string Value;
        public string Domain;
    }

    /// <summary>
    /// A tiny DevTools-protocol client over the browser's loopback debug port: read all cookies of
    /// a page's browser context (<c>Network.getAllCookies</c> on a page target — works for InPrivate
    /// windows, which a browser-level read would miss), and inject a cookie. Returns null (rather
    /// than throwing) whenever the port is not ready yet, so a caller can just poll.
    /// </summary>
    public static class CdpClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        /// <summary>The debugger WebSocket URL of the first "page" target from a <c>/json/list</c> body.</summary>
        private static string FirstPageWs(string jsonList)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonList);
                foreach (var t in doc.RootElement.EnumerateArray())
                {
                    if (t.TryGetProperty("type", out var ty) && ty.GetString() == "page"
                        && t.TryGetProperty("webSocketDebuggerUrl", out var w))
                    {
                        return w.GetString();
                    }
                }
            }
            catch { }
            return null;
        }

        public static async Task<List<CdpCookie>> GetCookiesAsync(int port)
        {
            try
            {
                // Read cookies from a PAGE target with Network.getAllCookies, NOT the browser
                // target with Storage.getCookies: the login window is InPrivate, and its cookies
                // live in a separate browser context the default context can't see. A page's
                // Network.getAllCookies returns that page's own context — private or not.
                string listJson = await Http.GetStringAsync("http://127.0.0.1:" + port + "/json/list");
                string wsUrl = FirstPageWs(listJson);
                if (string.IsNullOrEmpty(wsUrl)) { return null; }

                using var ws = new ClientWebSocket();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                await ws.ConnectAsync(new Uri(wsUrl), cts.Token);

                await SendAsync(ws, "{\"id\":1,\"method\":\"Network.getAllCookies\"}", cts.Token);

                // Read frames until we see the response to id 1.
                for (int i = 0; i < 20; i++)
                {
                    string msg = await ReceiveAsync(ws, cts.Token);
                    if (msg == null) { break; }
                    using var doc = JsonDocument.Parse(msg);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("id", out var idEl) || idEl.GetInt32() != 1) { continue; }
                    if (!root.TryGetProperty("result", out var result)
                        || !result.TryGetProperty("cookies", out var cookies)) { return new List<CdpCookie>(); }

                    var list = new List<CdpCookie>();
                    foreach (var c in cookies.EnumerateArray())
                    {
                        list.Add(new CdpCookie
                        {
                            Name = c.TryGetProperty("name", out var n) ? n.GetString() : "",
                            Value = c.TryGetProperty("value", out var v) ? v.GetString() : "",
                            Domain = c.TryGetProperty("domain", out var d) ? d.GetString() : ""
                        });
                    }
                    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
                    return list;
                }
                return null;
            }
            catch
            {
                return null;   // port not up yet / transient — caller polls again
            }
        }

        /// <summary>
        /// Read the password the user typed into the Roblox login page, so the manager can save it
        /// alongside the session (the user asked for this). Best-effort and PASSWORD-ONLY:
        ///
        ///   * It installs a one-time capturing <c>input</c> listener on the login PASSWORD field and
        ///     returns the latest value it saw. The field is matched by <c>autocomplete=current-password</c>
        ///     (plus Roblox's <c>#login-password</c> id) — that semantic marker is the login password
        ///     and NOT a two-step code, which uses <c>autocomplete=one-time-code</c>. So a 2FA code is
        ///     never captured as the password.
        ///   * It only ever reads THIS throwaway login window that Tempo launched — never the user's
        ///     real browser, never a saved-password store. That would be the "stealer" line; this is
        ///     the user typing their own password into Tempo's own sign-in window, exactly like a
        ///     password manager's browser integration.
        ///   * Returns "" when nothing was typed — which is precisely how a passwordless sign-in
        ///     (passkey / "quick log in" from another device) is detected: no password, so the caller
        ///     saves only the username.
        ///
        /// The value never leaves the vault and is never logged. Returns null when the port isn't up.
        /// </summary>
        public static async Task<string> ReadTypedPasswordAsync(int port)
        {
            try
            {
                string listJson = await Http.GetStringAsync("http://127.0.0.1:" + port + "/json/list");
                string wsUrl = FirstPageWs(listJson);
                if (string.IsNullOrEmpty(wsUrl)) { return null; }

                using var ws = new ClientWebSocket();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                await ws.ConnectAsync(new Uri(wsUrl), cts.Token);

                // Capture the password only when it is COMPLETE — never mid-typing, or a poll that
                // lands between keystrokes would save a partial. So a once-installed listener grabs it
                // on the completion signals (change/blur, form submit, Enter), and the direct read below
                // only takes the value when the field is NOT focused (i.e. the user has finished and
                // moved to the Log-in button). The value is stashed on window (survives the SPA's
                // same-origin step changes) and returned; the C# caller also caches it across polls.
                const string js = @"(function(){try{
var sel='input#login-password,input[autocomplete=""current-password""],input[name=""password""]';
var grab=function(el){if(el&&el.value){window.__tmpPw=el.value;}};
if(!window.__tmpPwHook){window.__tmpPwHook=1;window.__tmpPw='';
document.addEventListener('change',function(e){if(e.target&&e.target.matches&&e.target.matches(sel))grab(e.target);},true);
document.addEventListener('submit',function(e){try{var f=e.target;grab(f&&f.querySelector&&f.querySelector(sel));}catch(x){}},true);
document.addEventListener('keydown',function(e){if(e.key==='Enter')grab(document.querySelector(sel));},true);}
var el=document.querySelector('input#login-password')||document.querySelector('input[autocomplete=""current-password""]')||document.querySelector('input[name=""password""]');
if(el&&el.value&&document.activeElement!==el){grab(el);}
return window.__tmpPw||'';}catch(e){return '';}})()";

                string eval = "{\"id\":1,\"method\":\"Runtime.evaluate\",\"params\":{\"returnByValue\":true,\"expression\":"
                    + JsonSerializer.Serialize(js) + "}}";
                await SendAsync(ws, eval, cts.Token);

                for (int i = 0; i < 20; i++)
                {
                    string msg = await ReceiveAsync(ws, cts.Token);
                    if (msg == null) { break; }
                    using var doc = JsonDocument.Parse(msg);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("id", out var idEl) || idEl.GetInt32() != 1) { continue; }
                    string val = "";
                    if (root.TryGetProperty("result", out var res)
                        && res.TryGetProperty("result", out var inner)
                        && inner.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
                    {
                        val = v.GetString() ?? "";
                    }
                    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
                    return val;
                }
                return null;
            }
            catch
            {
                return null;   // port not up yet / transient — caller polls again
            }
        }

        /// <summary>
        /// Inject the stored <c>.ROBLOSECURITY</c> into a running browser (over its debug port) and
        /// navigate a tab to <paramref name="url"/>, so the browser is signed in as that account.
        /// Returns false if the debug port isn't up yet (caller can retry).
        /// </summary>
        public static async Task<bool> SetCookieAndNavigateAsync(int port, string cookie, string url)
        {
            try
            {
                string wsUrl = FirstPageWs(await Http.GetStringAsync("http://127.0.0.1:" + port + "/json/list"));
                if (string.IsNullOrEmpty(wsUrl)) { return false; }

                using var ws = new ClientWebSocket();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await ws.ConnectAsync(new Uri(wsUrl), cts.Token);

                await SendAsync(ws, "{\"id\":1,\"method\":\"Network.enable\"}", cts.Token);
                await WaitForResponse(ws, 1, cts.Token);

                string setCookie = "{\"id\":2,\"method\":\"Network.setCookie\",\"params\":{"
                    + "\"name\":\".ROBLOSECURITY\",\"value\":" + JsonSerializer.Serialize(cookie)
                    + ",\"domain\":\".roblox.com\",\"path\":\"/\",\"secure\":true,\"httpOnly\":true}}";
                await SendAsync(ws, setCookie, cts.Token);
                await WaitForResponse(ws, 2, cts.Token);

                string nav = "{\"id\":3,\"method\":\"Page.navigate\",\"params\":{\"url\":"
                    + JsonSerializer.Serialize(url) + "}}";
                await SendAsync(ws, nav, cts.Token);
                await WaitForResponse(ws, 3, cts.Token);

                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Close the whole browser via CDP (works even when the launcher process forked,
        /// e.g. Edge Guest mode, where killing the launched PID leaves the window open).</summary>
        public static async Task CloseBrowserAsync(int port)
        {
            try
            {
                string version = await Http.GetStringAsync("http://127.0.0.1:" + port + "/json/version");
                string wsUrl;
                using (var doc = JsonDocument.Parse(version))
                {
                    if (!doc.RootElement.TryGetProperty("webSocketDebuggerUrl", out var w)) { return; }
                    wsUrl = w.GetString();
                }
                if (string.IsNullOrEmpty(wsUrl)) { return; }

                using var ws = new ClientWebSocket();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await ws.ConnectAsync(new Uri(wsUrl), cts.Token);
                await SendAsync(ws, "{\"id\":1,\"method\":\"Browser.close\"}", cts.Token);
                await Task.Delay(300, cts.Token);
            }
            catch { }
        }

        private static async Task WaitForResponse(ClientWebSocket ws, int id, CancellationToken ct)
        {
            for (int i = 0; i < 30; i++)
            {
                string msg = await ReceiveAsync(ws, ct);
                if (msg == null) { return; }
                try
                {
                    using var doc = JsonDocument.Parse(msg);
                    if (doc.RootElement.TryGetProperty("id", out var idEl) && idEl.GetInt32() == id) { return; }
                }
                catch { }
            }
        }

        private static Task SendAsync(ClientWebSocket ws, string json, CancellationToken ct)
        {
            byte[] b = Encoding.UTF8.GetBytes(json);
            return ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, ct);
        }

        private static async Task<string> ReceiveAsync(ClientWebSocket ws, CancellationToken ct)
        {
            var buffer = new byte[16384];
            using var ms = new MemoryStream();
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (r.MessageType == WebSocketMessageType.Close) { return null; }
                ms.Write(buffer, 0, r.Count);
            }
            while (!r.EndOfMessage);
            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    /// <summary>
    /// Opens a saved account, SIGNED IN, in a real browser with a PERSISTENT per-account profile —
    /// so the account's browser data (its session, its tabs, its history) is kept between opens.
    /// Each account gets its own profile folder under Tempo's data dir; on open, Tempo injects the
    /// current stored cookie over the debug port and navigates to the Roblox home, so the browser is
    /// signed in as that account. The window is left open for the user; it is NOT a throwaway.
    ///
    /// The cookie DOES land in that browser profile on disk (that is what "keeps the account data"
    /// means), so this is a step outside the encrypted vault — deliberate, because the user asked for
    /// a real persistent browser per account, the way RAM does it.
    /// </summary>
    public static class RobloxAccountBrowser
    {
        public static async Task<bool> OpenAsync(string accountName, string cookie)
        {
            if (string.IsNullOrEmpty(cookie)) { return false; }
            RealBrowser.Info browser = RealBrowser.Locate();
            if (browser == null) { return false; }

            string profileDir = ProfileDir(accountName);
            try { Directory.CreateDirectory(profileDir); } catch { }
            int port = RealBrowser.FreePort();

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(browser.Path,
                    RealBrowser.BuildArgs(profileDir, port, "about:blank")) { UseShellExecute = false };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                Logger.Warn("[Roblox] open-in-browser launch failed: " + ex.Message);
                return false;
            }

            // Inject the session and go to the home page. Best-effort: if this profile was already
            // open, the launch just focuses it (no new debug port) and it is already signed in.
            for (int i = 0; i < 8; i++)
            {
                await Task.Delay(700);
                if (await CdpClient.SetCookieAndNavigateAsync(port, cookie, "https://www.roblox.com/home"))
                {
                    Logger.Info("[Roblox] opened an account in the browser, signed in.");
                    return true;
                }
            }
            Logger.Info("[Roblox] opened the account browser (its saved session should already be present).");
            return true;
        }

        private static string ProfileDir(string accountName)
        {
            string id;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(accountName ?? ""));
                var sb = new StringBuilder();
                for (int i = 0; i < 8; i++) { sb.Append(h[i].ToString("x2")); }
                id = sb.ToString();
            }
            return Path.Combine(Persistence.SettingsManager.GetSettingsDirectory(), "accountbrowsers", id);
        }
    }
}
