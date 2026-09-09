using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace AutoClicker.Utils
{
    /// <summary>What an API launch attempt did, mapped to an HTTP status in the handler.</summary>
    public enum ApiLaunchResult
    {
        Launched,
        UnknownAccount,
        NoSession,
        NotInstalled,
        Locked,
        Error
    }

    /// <summary>
    /// A small localhost HTTP server so other tools on this PC can list the saved accounts and
    /// trigger a launch — the "account manager API" people know from RAM. It is built to be the
    /// SAFE subset of that idea:
    ///
    ///   * Loopback only. The prefix is <c>http://127.0.0.1:&lt;port&gt;/</c> — never <c>+</c> or
    ///     <c>*</c> — so nothing off this machine can reach it, and non-admin can bind it.
    ///   * Token-gated. Every request must carry the secret <c>token</c> (query or
    ///     <c>X-Tempo-Token</c> header); it is compared in constant time. A web page cannot guess
    ///     it, so it cannot drive this server even though the port is local.
    ///   * No browser access. Any request carrying an <c>Origin</c> or <c>Referer</c> header is
    ///     refused, which blocks the fetch/XHR cross-site vector as defence in depth.
    ///   * NEVER hands out secrets. There is deliberately no endpoint that returns a cookie or a
    ///     password. The server can start a launch (the app holds the cookie and does that
    ///     itself) but a stored credential never crosses the socket.
    ///
    /// It only runs while the vault is unlocked, and the account endpoints refuse (423) otherwise.
    /// The app injects the callbacks, which marshal onto the UI thread themselves.
    /// </summary>
    public sealed class AccountApiServer : IDisposable
    {
        private readonly int _port;
        private readonly string _token;
        private readonly Func<string[]> _accounts;            // account names only — no secrets
        private readonly Func<string, long, ApiLaunchResult> _launch;
        private readonly Func<string> _current;               // last-launched account name, or ""
        private readonly Func<string, ApiLaunchResult> _relogin;
        private readonly Func<bool> _unlocked;

        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _running;

        public bool IsRunning => _running;
        public int Port => _port;

        public AccountApiServer(int port, string token,
                                Func<string[]> accounts,
                                Func<string, long, ApiLaunchResult> launch,
                                Func<string> current,
                                Func<string, ApiLaunchResult> relogin,
                                Func<bool> unlocked)
        {
            _port = port;
            _token = token ?? "";
            _accounts = accounts;
            _launch = launch;
            _current = current;
            _relogin = relogin;
            _unlocked = unlocked;
        }

        /// <summary>Start listening. Returns false (and logs) if the port can't be bound.</summary>
        public bool Start()
        {
            if (_running) { return true; }
            if (_token.Length < 8)
            {
                Logger.Warn("[AccountApi] refusing to start without a proper token.");
                return false;
            }
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://127.0.0.1:" + _port + "/");
                _listener.Start();
                _running = true;
                _thread = new Thread(Loop) { IsBackground = true, Name = "TempoAccountApi" };
                _thread.Start();
                Logger.Info("[AccountApi] listening on http://127.0.0.1:" + _port + "/");
                return true;
            }
            catch (Exception ex)
            {
                _running = false;
                try { _listener?.Close(); } catch { }
                _listener = null;
                Logger.Warn("[AccountApi] could not start on port " + _port + ": " + ex.Message);
                return false;
            }
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;
            Logger.Info("[AccountApi] stopped.");
        }

        public void Dispose() => Stop();

        private void Loop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { break; }   // listener stopped
                try { Handle(ctx); }
                catch (Exception ex) { Logger.Swallow("AccountApiServer.Handle", ex); }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            HttpListenerRequest req = ctx.Request;
            HttpListenerResponse resp = ctx.Response;

            // Defence in depth: a browser fetch/XHR always carries Origin (and usually Referer);
            // a native tool does not. Refuse anything that looks browser-driven.
            if (!string.IsNullOrEmpty(req.Headers["Origin"]) || !string.IsNullOrEmpty(req.Headers["Referer"]))
            {
                Deny(resp, 403, "browser requests are not allowed");
                return;
            }

            // The real gate: the shared secret.
            string token = req.QueryString["token"];
            if (string.IsNullOrEmpty(token)) { token = req.Headers["X-Tempo-Token"]; }
            if (!ConstantTimeEquals(token, _token))
            {
                Deny(resp, 401, "missing or wrong token");
                return;
            }

            string path = (req.Url.AbsolutePath ?? "/").TrimEnd('/').ToLowerInvariant();
            if (path.Length == 0) { path = "/ping"; }

            switch (path)
            {
                case "/ping":
                    WriteJson(resp, 200, "{\"app\":\"Tempo\",\"unlocked\":" + (_unlocked() ? "true" : "false") + "}");
                    break;

                case "/accounts":
                    if (!_unlocked()) { Deny(resp, 423, "vault is locked"); break; }
                    WriteJson(resp, 200, JsonSerializer.Serialize(_accounts() ?? Array.Empty<string>()));
                    break;

                case "/current":
                    if (!_unlocked()) { Deny(resp, 423, "vault is locked"); break; }
                    string cur = _current() ?? "";
                    WriteJson(resp, 200, "{\"account\":" + JsonSerializer.Serialize(cur) + "}");
                    break;

                case "/launch":
                    if (!_unlocked()) { Deny(resp, 423, "vault is locked"); break; }
                    string account = req.QueryString["account"];
                    if (string.IsNullOrWhiteSpace(account)) { Deny(resp, 400, "account is required"); break; }
                    long placeId = long.TryParse(req.QueryString["placeId"], out long pid) ? pid : 0;
                    RespondLaunch(resp, _launch(account, placeId));
                    break;

                case "/relogin":
                    if (!_unlocked()) { Deny(resp, 423, "vault is locked"); break; }
                    string racct = req.QueryString["account"];
                    if (string.IsNullOrWhiteSpace(racct)) { Deny(resp, 400, "account is required"); break; }
                    // Opens the sign-in window for a human at the PC; reports acceptance.
                    RespondLaunch(resp, _relogin(racct));
                    break;

                default:
                    Deny(resp, 404, "unknown endpoint");
                    break;
            }
        }

        private static void RespondLaunch(HttpListenerResponse resp, ApiLaunchResult r)
        {
            switch (r)
            {
                case ApiLaunchResult.Launched: WriteJson(resp, 200, "{\"ok\":true}"); break;
                case ApiLaunchResult.UnknownAccount: Deny(resp, 404, "no such account"); break;
                case ApiLaunchResult.NoSession: Deny(resp, 409, "account has no saved session"); break;
                case ApiLaunchResult.NotInstalled: Deny(resp, 503, "Roblox is not installed"); break;
                case ApiLaunchResult.Locked: Deny(resp, 423, "vault is locked"); break;
                default: Deny(resp, 500, "launch failed"); break;
            }
        }

        private static void WriteJson(HttpListenerResponse resp, int status, string json)
        {
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(json);
                resp.StatusCode = status;
                resp.ContentType = "application/json";
                resp.Headers["Cache-Control"] = "no-store";
                resp.ContentLength64 = body.Length;
                resp.OutputStream.Write(body, 0, body.Length);
            }
            catch { }
            finally { try { resp.OutputStream.Close(); } catch { } }
        }

        private static void Deny(HttpListenerResponse resp, int status, string message)
        {
            WriteJson(resp, status, "{\"ok\":false,\"error\":" + JsonSerializer.Serialize(message) + "}");
        }

        /// <summary>Length-independent, early-exit-free comparison so the token can't be timed out.</summary>
        private static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null) { return false; }
            byte[] ba = Encoding.UTF8.GetBytes(a);
            byte[] bb = Encoding.UTF8.GetBytes(b);
            int diff = ba.Length ^ bb.Length;
            for (int i = 0; i < ba.Length; i++)
            {
                diff |= ba[i] ^ bb[i < bb.Length ? i : 0];
            }
            return diff == 0;
        }

        /// <summary>A fresh 32-hex-character token.</summary>
        public static string NewToken()
        {
            byte[] b = new byte[16];
            System.Security.Cryptography.RandomNumberGenerator.Fill(b);
            var sb = new StringBuilder(32);
            foreach (byte x in b) { sb.Append(x.ToString("x2")); }
            return sb.ToString();
        }
    }
}
