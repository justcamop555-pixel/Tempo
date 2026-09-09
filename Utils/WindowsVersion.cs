using System;
using System.Globalization;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Works out which Windows this actually is, and whether Tempo supports it.
    ///
    /// Two things made this worth having. Nothing in Tempo ever NAMED the operating
    /// system: About listed the display, the .NET runtime and the data folder but not
    /// Windows, and the only version test in the whole app was one inline
    /// "Build >= 22000" for a theming decision. So a bug report said "Windows 11" and the
    /// log said 10.0.26200, and nobody could tell 21H2 from 24H2 without asking.
    ///
    /// And the supported-version check was far looser than the app's own manifest.
    /// PrerequisiteChecker only refused Major &lt; 10, so every Windows 10 build back to
    /// 1507 was waved through — while the csproj declares a floor of 10.0.17763. Builds
    /// in between start, then fail later at whichever API they lack, which reads as "Tempo
    /// is broken" rather than "this Windows is too old".
    ///
    /// The one trap this deliberately avoids: ProductName in the registry still reads
    /// "Windows 10 Pro" on Windows 11. It is not usable for naming the OS, and is only
    /// read here for the edition word.
    /// </summary>
    public static class WindowsVersion
    {
        /// <summary>The first Windows 11 build. Below this, a Major-10 Windows is 10.</summary>
        private const int FirstWindows11Build = 22000;

        /// <summary>
        /// The floor Tempo declares in AutoClicker.csproj (SupportedOSPlatformVersion
        /// 10.0.17763 — Windows 10 1809). Below it the app is outside what it claims to
        /// support, whether or not it happens to start.
        /// </summary>
        private const int MinSupportedBuild = 17763;

        public enum Support
        {
            /// <summary>Meets the floor Tempo declares.</summary>
            Supported = 0,
            /// <summary>Windows 10, but older than the declared floor. Runs at the user's risk.</summary>
            Outdated = 1,
            /// <summary>Not Windows, or older than Windows 10. Tempo cannot run.</summary>
            Unsupported = 2,
        }

        private static string _cached;
        private static Support _cachedSupport;
        private static bool _resolved;

        /// <summary>Build number, e.g. 26100. Zero if it cannot be read.</summary>
        public static int Build
        {
            get
            {
                try { return Environment.OSVersion.Version.Build; }
                catch { return 0; }
            }
        }

        /// <summary>True on Windows 11 or newer.</summary>
        public static bool IsWindows11OrNewer => Build >= FirstWindows11Build;

        /// <summary>How well Tempo supports the Windows it is running on.</summary>
        public static Support Status
        {
            get { Resolve(); return _cachedSupport; }
        }

        /// <summary>
        /// A human line, e.g. "Windows 11 Pro 25H2 (build 26200.1742)".
        /// Cached: it cannot change while the process runs.
        /// </summary>
        public static string Describe()
        {
            Resolve();
            return _cached;
        }

        private static void Resolve()
        {
            if (_resolved) { return; }
            _resolved = true;

            try
            {
                OperatingSystem os = Environment.OSVersion;

                if (os.Platform != PlatformID.Win32NT)
                {
                    _cached = "not Windows (" + os.Platform + ")";
                    _cachedSupport = Support.Unsupported;
                    return;
                }

                int major = os.Version.Major;
                int build = os.Version.Build;

                if (major < 10)
                {
                    _cached = LegacyName(major, os.Version.Minor) +
                              " (" + os.Version + ")";
                    _cachedSupport = Support.Unsupported;
                    return;
                }

                bool server = IsServer();
                string family = server
                    ? ServerName(build)
                    : (build >= FirstWindows11Build ? "Windows 11" : "Windows 10");

                string edition = ReadEdition();
                string release = ReadRelease(build, server);
                string full = BuildWithUbr(build);

                var sb = new System.Text.StringBuilder(family);
                if (edition.Length > 0) { sb.Append(' ').Append(edition); }
                if (release.Length > 0) { sb.Append(' ').Append(release); }
                sb.Append(" (build ").Append(full).Append(')');

                _cached = sb.ToString();
                _cachedSupport = build >= MinSupportedBuild ? Support.Supported : Support.Outdated;
            }
            catch (Exception ex)
            {
                Logger.Swallow("WindowsVersion.Resolve", ex);
                _cached = "Windows (version could not be read)";
                _cachedSupport = Support.Supported;   // never block on our own failure
            }
        }

        /// <summary>
        /// The marketing release ("24H2", "22H2"). Read from the registry where Windows
        /// records it, because that is authoritative and covers releases newer than this
        /// build of Tempo knows about; the table is only the fallback for older ones, and
        /// for a registry that cannot be read.
        /// </summary>
        private static string ReadRelease(int build, bool server)
        {
            string fromRegistry = ReadCurrentVersionString("DisplayVersion");
            if (!string.IsNullOrWhiteSpace(fromRegistry)) { return fromRegistry.Trim(); }

            if (server) { return ""; }   // the server NAME already carries the year

            switch (build)
            {
                case 26200: return "25H2";
                case 26100: return "24H2";
                case 22631: return "23H2";
                case 22621: return "22H2";
                case 22000: return "21H2";
                case 19045: return "22H2";
                case 19044: return "21H2";
                case 19043: return "21H1";
                case 19042: return "20H2";
                case 19041: return "2004";
                case 18363: return "1909";
                case 18362: return "1903";
                case 17763: return "1809";
                case 17134: return "1803";
                case 16299: return "1709";
                case 15063: return "1703";
                case 14393: return "1607";
                case 10586: return "1511";
                case 10240: return "1507";
                default: return "";
            }
        }

        /// <summary>
        /// "Pro", "Home", "Enterprise"… taken from ProductName's trailing words, falling
        /// back to EditionID. ProductName's LEADING words are ignored on purpose — they
        /// still say "Windows 10" on Windows 11.
        /// </summary>
        private static string ReadEdition()
        {
            try
            {
                string product = ReadCurrentVersionString("ProductName");
                if (!string.IsNullOrWhiteSpace(product))
                {
                    string p = product.Trim();
                    int cut = p.IndexOf("Windows ", StringComparison.OrdinalIgnoreCase);
                    if (cut >= 0)
                    {
                        // Drop "Windows" and the version number that follows it, keeping
                        // whatever edition words remain.
                        string[] parts = p.Substring(cut + 8).Split(new[] { ' ' },
                            StringSplitOptions.RemoveEmptyEntries);
                        var kept = new System.Collections.Generic.List<string>();
                        foreach (string w in parts)
                        {
                            if (kept.Count == 0 && IsVersionWord(w)) { continue; }
                            kept.Add(w);
                        }
                        if (kept.Count > 0) { return string.Join(" ", kept.ToArray()); }
                    }
                }

                string edition = ReadCurrentVersionString("EditionID");
                return edition == null ? "" : edition.Trim();
            }
            catch { return ""; }
        }

        /// <summary>True for the "10"/"11"/"Server"/"2022" style tokens after "Windows".</summary>
        private static bool IsVersionWord(string w)
        {
            if (string.IsNullOrEmpty(w)) { return true; }
            foreach (char c in w)
            {
                if (!char.IsDigit(c)) { return false; }
            }
            return true;
        }

        /// <summary>Build plus the update revision, e.g. "26100.4061".</summary>
        private static string BuildWithUbr(int build)
        {
            try
            {
                object ubr = ReadCurrentVersion("UBR");
                if (ubr is int u && u > 0)
                {
                    return build.ToString(CultureInfo.InvariantCulture) + "." +
                           u.ToString(CultureInfo.InvariantCulture);
                }
            }
            catch { }
            return build.ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsServer()
        {
            try
            {
                string t = ReadCurrentVersionString("InstallationType");
                return t != null && t.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static string ServerName(int build)
        {
            if (build >= 26100) { return "Windows Server 2025"; }
            if (build >= 20348) { return "Windows Server 2022"; }
            if (build >= 17763) { return "Windows Server 2019"; }
            if (build >= 14393) { return "Windows Server 2016"; }
            return "Windows Server";
        }

        private static string LegacyName(int major, int minor)
        {
            if (major == 6)
            {
                switch (minor)
                {
                    case 3: return "Windows 8.1";
                    case 2: return "Windows 8";
                    case 1: return "Windows 7";
                    case 0: return "Windows Vista";
                }
            }
            if (major == 5) { return "Windows XP / Server 2003"; }
            return "Windows " + major + "." + minor;
        }

        private static string ReadCurrentVersionString(string name)
        {
            return ReadCurrentVersion(name) as string;
        }

        private static object ReadCurrentVersion(string name)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    return key?.GetValue(name);
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// One sentence explaining an Outdated result, for the log and the debug panel.
        /// Empty when the OS is fine.
        /// </summary>
        public static string SupportNote()
        {
            switch (Status)
            {
                case Support.Outdated:
                    return "This Windows build (" + Build + ") is older than the "
                         + MinSupportedBuild + " Tempo is built for. It may start, but "
                         + "features that need newer APIs — the low-CPU click timer, the "
                         + "themed title bar, Live Captions — can fail in ways that look "
                         + "like Tempo bugs.";
                case Support.Unsupported:
                    return "Tempo needs Windows 10 or newer.";
                default:
                    return "";
            }
        }
    }
}
