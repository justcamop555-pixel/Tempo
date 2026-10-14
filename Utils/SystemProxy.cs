using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AutoClicker.Utils
{
    /// <summary>
    /// The proxy Windows is set to use — but only one that has actually been SET.
    ///
    /// Tempo's downloads and its GitHub requests used to say "no proxy" outright (Proxy = null), to dodge
    /// automatic proxy DISCOVERY (WPAD): on a network without a proxy it is not covered by any timeout,
    /// and it made update checks hang. But "no proxy" also meant that a PC whose only way out is a proxy
    /// someone configured — a school, an office — could never reach GitHub: every update check failed and
    /// the in-app update with it, while the Roblox calls (which kept .NET's default) went through.
    ///
    /// So: a proxy typed into Windows' settings, or a setup script (PAC) given there, is used; automatic
    /// discovery on its own still is not. Read on every call — changing the setting takes effect at once.
    /// </summary>
    public static class SystemProxy
    {
        private const string SettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        /// <summary>
        /// For a long-lived HttpClient (the update download): follows the setting as it changes, re-read
        /// at most every 10 s. A proxy captured once would keep failing after the user set the right one,
        /// until Tempo was restarted.
        /// </summary>
        public static readonly IWebProxy Live = new LiveProxy();

        private sealed class LiveProxy : IWebProxy
        {
            private readonly object _gate = new object();
            private IWebProxy _current;
            private DateTime _readUtc = DateTime.MinValue;

            private IWebProxy Current
            {
                get
                {
                    lock (_gate)
                    {
                        DateTime now = DateTime.UtcNow;
                        if (now - _readUtc > TimeSpan.FromSeconds(10) || now < _readUtc)
                        {
                            _current = Get();
                            _readUtc = now;
                        }
                        return _current;
                    }
                }
            }

            public ICredentials Credentials
            {
                get { return CredentialCache.DefaultCredentials; }
                set { }
            }

            // No proxy set = every address is "bypassed", so the handler connects directly. GetProxy never
            // hands back the destination itself: the handler would take that for a proxy.
            public bool IsBypassed(Uri host)
            {
                IWebProxy p = Current;
                return p == null || p.IsBypassed(host);
            }

            public Uri GetProxy(Uri destination)
            {
                IWebProxy p = Current;
                Uri via = p?.GetProxy(destination);
                return via == null || via == destination ? null : via;
            }
        }

        /// <summary>The proxy to use, or null to connect directly.</summary>
        public static IWebProxy Get()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(SettingsKey, false))
                {
                    if (k == null) { return null; }
                    bool enabled = k.GetValue("ProxyEnable") is int on && on == 1;
                    return FromSettings(enabled, k.GetValue("ProxyServer") as string,
                                        k.GetValue("ProxyOverride") as string, k.GetValue("AutoConfigURL") as string);
                }
            }
            catch (Exception ex)
            {
                Logger.Swallow("SystemProxy.Get", ex);
                return null;
            }
        }

        /// <summary>One line for Live debug: which way Tempo's web requests go out.</summary>
        public static string Describe()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(SettingsKey, false))
                {
                    if (k == null) { return "direct"; }
                    string pac = k.GetValue("AutoConfigURL") as string;
                    if (!string.IsNullOrWhiteSpace(pac)) { return "the setup script Windows is given (PAC)"; }
                    bool enabled = k.GetValue("ProxyEnable") is int on && on == 1;
                    string server = PickServer(k.GetValue("ProxyServer") as string);
                    return enabled && server != null ? "proxy " + server : "direct (no proxy set in Windows)";
                }
            }
            catch { return "direct"; }
        }

        /// <summary>
        /// The decision, from Windows' four settings (separate so the tests can feed it): a setup script
        /// wins, then a proxy that is switched on, otherwise none.
        /// </summary>
        internal static IWebProxy FromSettings(bool enabled, string server, string bypass, string pacUrl)
        {
            if (!string.IsNullOrWhiteSpace(pacUrl))
            {
                // Only Windows can run a setup script, so hand over to its own proxy, which honours it.
                return WebRequest.GetSystemWebProxy();
            }
            if (!enabled) { return null; }
            string address = PickServer(server);
            if (address == null) { return null; }
            if (!Uri.TryCreate(address.Contains("://") ? address : "http://" + address, UriKind.Absolute, out Uri uri))
            {
                return null;
            }
            var proxy = new WebProxy(uri)
            {
                // A proxy that asks who you are gets the signed-in Windows user, as a browser would.
                UseDefaultCredentials = true
            };
            var patterns = new List<string>();
            foreach (string raw in (bypass ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string entry = raw.Trim();
                if (entry.Length == 0) { continue; }
                if (string.Equals(entry, "<local>", StringComparison.OrdinalIgnoreCase))
                {
                    proxy.BypassProxyOnLocal = true;
                    continue;
                }
                // Windows writes wildcards ("*.contoso.com", "10.*"); WebProxy wants regular expressions —
                // and matches them against "scheme://host[:port]", not the bare host, so a pattern anchored
                // at the host ("^10\..*") never matched anything. Scheme and port are allowed either side.
                patterns.Add(@"^(?:[a-z][a-z0-9+.\-]*://)?" + Regex.Escape(entry).Replace(@"\*", ".*") + @"(?::\d+)?$");
            }
            if (patterns.Count > 0) { proxy.BypassList = patterns.ToArray(); }
            return proxy;
        }

        /// <summary>
        /// The server out of Windows' ProxyServer value: "host:port" as it is, or from the per-protocol
        /// form "http=a:80;https=b:443" the https entry (failing that, the http one). Null when empty.
        /// </summary>
        internal static string PickServer(string server)
        {
            if (string.IsNullOrWhiteSpace(server)) { return null; }
            string s = server.Trim();
            if (s.IndexOf('=') < 0) { return s.TrimEnd(';'); }
            string https = null, http = null;
            foreach (string part in s.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) { continue; }
                string scheme = part.Substring(0, eq).Trim().ToLowerInvariant();
                string value = part.Substring(eq + 1).Trim();
                if (value.Length == 0) { continue; }
                if (scheme == "https") { https = value; }
                else if (scheme == "http") { http = value; }
            }
            return https ?? http;
        }
    }
}
