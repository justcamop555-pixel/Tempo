using System;
using System.Text.RegularExpressions;

namespace AutoClicker.Utils
{
    /// <summary>
    /// What a mirrored notification says beyond its title and text. The listener API the mirror reads
    /// hands out the text only; this comes from the notification's whole toast in Windows' own store
    /// (see <see cref="NotificationPayload"/>) — who Windows names as the sender, the toast's attribution
    /// line, a progress bar, whether Windows keeps it up until it is dismissed.
    ///
    /// Plain data, no pictures (the card owns those), so it can ride along to the history.
    /// </summary>
    public sealed class NotificationExtras
    {
        /// <summary>
        /// Who Windows names as the sender when that is not the app itself. Edge registers every web site
        /// as a sender of its own — "www.kenherbert.dev", with the site's icon — and Windows' pop-up wears
        /// that name, while the listener reports only "Microsoft Edge". Empty when the sender is the app.
        /// </summary>
        public string Sender = "";

        /// <summary>The toast's own attribution line ("via SMS"; Chrome puts the web site here), or "".</summary>
        public string Attribution = "";

        /// <summary>A browser notification's web site (https://host/), where a click goes when nothing better is known; or "".</summary>
        public string SiteUrl = "";

        /// <summary>Where Windows' own click goes, when the toast says so and it is a web address; or "".</summary>
        public string LaunchUrl = "";

        /// <summary>
        /// The toast's launch arguments: what Windows hands the app when the toast is clicked (for a web site's
        /// notification, the browser's id for it), or the address it opens for a protocol launch. Kept in memory only.
        /// </summary>
        public string Launch = "";

        /// <summary>How a click reaches the app: foreground (the default), background or protocol.</summary>
        public string ActivationType = "";

        /// <summary>The app's own notification handler — the COM server Windows starts when its toasts are clicked — or null.</summary>
        public Guid? Activator;

        /// <summary>The toast's buttons, as Windows shows them under the text (up to three; none that need a reply box).</summary>
        public readonly System.Collections.Generic.List<ToastButton> Buttons = new System.Collections.Generic.List<ToastButton>();

        public sealed class ToastButton
        {
            public string Text = "";
            /// <summary>What Windows hands the app when this button is pressed (or the address, for a protocol button).</summary>
            public string Arguments = "";
            /// <summary>foreground, background, protocol — or "dismiss" for Windows' own close button.</summary>
            public string ActivationType = "";
            /// <summary>What pressing it on Tempo's card does; set by whoever shows the card. Null: the button is not drawn.</summary>
            public Action Invoke;
        }

        /// <summary>The toast's scenario, lower-case: reminder, alarm, incomingcall, urgent — or "".</summary>
        public string Scenario = "";

        /// <summary>
        /// Windows keeps it on screen until it is dismissed: a reminder or an alarm that carries a button
        /// (Windows ignores the scenario without one), or an incoming call.
        /// </summary>
        public bool StaysUntilDismissed;

        /// <summary>duration="long": Windows shows it for about 25 s instead of about 7.</summary>
        public bool LongDuration;

        /// <summary>When the sender says it happened (the toast's displayTimestamp), or null.</summary>
        public DateTime? SentUtc;

        /// <summary>A progress bar — a download, an install — or null.</summary>
        public ProgressBar Progress;

        /// <summary>The picture beside the text is a person's: Windows draws it round (hint-crop="circle").</summary>
        public bool LogoCircle;

        public sealed class ProgressBar
        {
            public string Title = "";
            public string Status = "";
            /// <summary>What the bar's own text says ("3/26 songs"); "" to show the percentage.</summary>
            public string ValueText = "";
            /// <summary>0..1, or -1 while it cannot say how far along it is.</summary>
            public double Value = -1;

            public bool Indeterminate => Value < 0;

            /// <summary>"3/26 songs", else "40%", else "".</summary>
            public string ValueLabel()
            {
                if (!string.IsNullOrEmpty(ValueText)) { return ValueText; }
                return Indeterminate ? "" : (int)Math.Round(Value * 100) + "%";
            }
        }

        /// <summary>
        /// The line that says who sent it: "www.kenherbert.dev · Microsoft Edge" (a sender of its own),
        /// "Phone Link · via SMS" (an attribution), or just the app. Used by the card and the history
        /// alike, so both name a notification the same way.
        /// </summary>
        public static string SourceLine(string app, string sender, string attribution)
        {
            app = (app ?? "").Trim();
            sender = (sender ?? "").Trim();
            attribution = (attribution ?? "").Trim();
            if (sender.Length > 0 && !sender.Equals(app, StringComparison.OrdinalIgnoreCase))
            {
                return app.Length > 0 ? sender + " · " + app : sender;
            }
            if (attribution.Length > 0 && !attribution.Equals(app, StringComparison.OrdinalIgnoreCase))
            {
                return app.Length > 0 ? app + " · " + attribution : attribution;
            }
            return app;
        }

        /// <summary>
        /// The web site in a browser sender's identity, or null. Edge registers each site as a sender of
        /// its own — "Microsoft.MicrosoftEdge.Stable_8wekyb3d8bbwe!https://www.example.com/", or with a
        /// profile id and a space before the address on newer builds (both seen in this PC's store,
        /// 2026-10-12).
        /// </summary>
        public static Uri SiteFromAumid(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) { return null; }
            int bang = aumid.IndexOf('!');
            if (bang < 0) { return null; }
            string rest = aumid.Substring(bang + 1);
            int at = rest.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
            if (at < 0) { at = rest.IndexOf("http://", StringComparison.OrdinalIgnoreCase); }
            return at < 0 ? null : WebAddress(rest.Substring(at));
        }

        // A bare host as a browser's attribution line shows it ("kenherbert.dev", "localhost:3000").
        private static readonly Regex HostRx = new Regex(@"^(?:via\s+)?([a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)*(?::\d{1,5})?)$",
                                                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// The site a browser's attribution line names ("kenherbert.dev" → https://kenherbert.dev/), or
        /// null when the line is not just a host name. Only meant for a BROWSER's notification: any other
        /// app's attribution is its own words, not an address.
        /// </summary>
        public static Uri SiteFromHost(string attribution)
        {
            Match m = HostRx.Match((attribution ?? "").Trim());
            if (!m.Success) { return null; }
            string host = m.Groups[1].Value;
            // A dotless word ("Messages") is no site; localhost is the one dotless host worth opening.
            if (host.IndexOf('.') < 0 && !host.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)) { return null; }
            return WebAddress("https://" + host + "/");
        }

        /// <summary>An http(s) address with a host and no user name in it, or null.</summary>
        public static Uri WebAddress(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0 || s.Length > 2048) { return null; }
            if (!Uri.TryCreate(s, UriKind.Absolute, out Uri u)) { return null; }
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) { return null; }
            if (string.IsNullOrEmpty(u.Host) || !string.IsNullOrEmpty(u.UserInfo)) { return null; }
            return u;
        }

        /// <summary>"www.example.com" (and ":8080" when the port is not the usual one): a site as a sender's name.</summary>
        public static string HostLabel(Uri u)
        {
            if (u == null) { return ""; }
            return u.IsDefaultPort ? u.Host : u.Host + ":" + u.Port;
        }
    }
}
