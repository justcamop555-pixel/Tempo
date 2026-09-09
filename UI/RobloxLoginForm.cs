using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Signs the user into Roblox in a REAL browser and captures the session.
    ///
    /// Tempo launches a genuine Chromium browser (Chrome preferred, else Edge/Brave/Opera) in a
    /// THROWAWAY private profile under %TEMP% and opens roblox.com there. The user logs in — or
    /// creates a brand-new account — in a full browser, so signup captchas and everything else work
    /// exactly as normal (which an embedded WebView could not manage). Tempo watches its OWN throwaway
    /// profile over the DevTools debug port for the <c>.ROBLOSECURITY</c> cookie to appear, then
    /// stores it. It never touches the user's real browser profile, and the throwaway profile is
    /// deleted on close.
    ///
    /// It ALSO captures the password the user types into that login window (<see cref="CapturedPassword"/>),
    /// so the manager can save it with the account like RAM does — read only from Tempo's own sign-in
    /// window, stored only in the encrypted vault, never logged. A passwordless sign-in (passkey /
    /// quick log in) types no password, so nothing is captured and only the username is saved.
    ///
    /// This window is just the status + controls; the browsing happens in the separate real-browser
    /// window the user interacts with.
    /// </summary>
    public sealed class RobloxLoginForm : Form
    {
        private readonly Theme _theme;
        private readonly string _preferredBrowser;   // "" = Automatic; else Chrome/Edge/Brave/Opera
        private readonly string _profileDir;
        private readonly Label _status;
        private readonly Button _captureBtn;
        private Process _proc;
        private int _port;
        private System.Windows.Forms.Timer _poll;
        private int _ticks;
        private bool _captured;
        private bool _launched;
        private bool _busy;          // single-flight: only one capture attempt at a time
        private bool _browserGone;   // the user closed the browser before signing in
        private bool _portWasUp;     // the debug port has answered at least once
        private int _portDownStreak; // consecutive polls with the port gone AFTER it was up
        private string _typedPassword = "";   // latest password seen in the login field (secret; never logged)

        private const int MinCookieLength = 80;   // a real .ROBLOSECURITY token is long

        /// <summary>The captured session cookie. Set only when the dialog returns OK.</summary>
        public string CapturedCookie { get; private set; }

        /// <summary>
        /// The password the user typed during sign-in, or "" for a passwordless sign-in
        /// (passkey / quick log in). SECRET — the caller stores it in the encrypted vault and it is
        /// never logged. Set only when the dialog returns OK.
        /// </summary>
        public string CapturedPassword { get; private set; } = "";

        public RobloxLoginForm(Theme theme, string preferredBrowser = null)
        {
            _theme = theme ?? Theme.ForKind(ThemeKind.Dark);
            _preferredBrowser = preferredBrowser;
            _profileDir = Path.Combine(Path.GetTempPath(),
                "Tempo-roblox-login-" + Guid.NewGuid().ToString("N"));

            Text = Utils.Localization.T("Log in to Roblox");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 200);
            BackColor = _theme.Background;
            ForeColor = _theme.Text;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            _status = new Label
            {
                Location = new Point(18, 18),
                Size = new Size(484, 96),
                Font = new Font("Segoe UI", 9.75f),
                ForeColor = _theme.Text,
                Text = Utils.Localization.T("Opening a private browser window…")
            };
            Controls.Add(_status);

            _captureBtn = UiFactory.PrimaryButton("I've logged in — save", ClientSize.Width - 196, ClientSize.Height - 44, 184, 30, _theme);
            _captureBtn.Enabled = false;
            _captureBtn.Click += async (s, e) => await TryCapture(true);
            var cancel = UiFactory.Button(Utils.Localization.T("Cancel"), 12, ClientSize.Height - 44, 90, 30);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_captureBtn);
            Controls.Add(cancel);

            Load += (s, e) => Start();
        }

        private void Start()
        {
            RealBrowser.Info browser = RealBrowser.Locate(_preferredBrowser);
            if (browser == null)
            {
                _status.ForeColor = _theme.DangerText;
                _status.Text = Utils.Localization.T(
                    "No supported browser was found. Install Google Chrome, Microsoft Edge, Brave or Opera and try again "
                    + "(Firefox and Safari can't be used for sign-in capture).");
                return;
            }

            try
            {
                Directory.CreateDirectory(_profileDir);
                _port = RealBrowser.FreePort();
                var psi = new ProcessStartInfo(browser.Path,
                    RealBrowser.BuildArgs(_profileDir, _port, "https://www.roblox.com/login", browser.PrivateFlag))
                {
                    UseShellExecute = false
                };
                _proc = Process.Start(psi);
                _launched = true;
                _captureBtn.Enabled = true;
                _status.Text = Utils.Localization.F(
                    "A private {0} window has opened. Log in — or create a new account — there. "
                    + "Tempo will detect it and save the session automatically; you can close this once it does.",
                    browser.Name);

                _poll = new System.Windows.Forms.Timer { Interval = 1500 };
                _poll.Tick += async (s, e) => await OnPoll();
                _poll.Start();
                Utils.Logger.Info("[RobloxLogin] launched " + browser.Name + " (isolated profile) for sign-in.");
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("[RobloxLogin] could not launch a browser: " + ex.Message);
                _status.ForeColor = _theme.DangerText;
                _status.Text = Utils.Localization.T("Tempo couldn't open a browser window for sign-in.");
            }
        }

        private async Task OnPoll()
        {
            _ticks++;
            await TryCapture(false);
            if (_captured) { return; }
            if (_ticks == 30)
            {
                _status.ForeColor = _theme.WarningText;
                _status.Text = Utils.Localization.T(
                    "Signed in and this hasn't closed? Make sure you finished in the browser window, then click "
                    + "“I've logged in — save”.");
            }
        }

        private async Task TryCapture(bool manual)
        {
            // Single-flight: a poll's cookie read can take a few seconds, and the 1.5s timer would
            // otherwise stack up concurrent reads — two of which could both "capture" and both call
            // Close(), the second on a disposed form (a crash in this async-void path).
            if (_captured || !_launched || _busy) { return; }
            _busy = true;
            try
            {
                var cookies = await CdpClient.GetCookiesAsync(_port);
                if (_captured) { return; }   // won the race elsewhere while awaiting
                if (cookies == null)
                {
                    // Detect a closed browser by the DEBUG PORT going away, not the launched
                    // process exiting — Edge Guest mode forks, so the launched PID dies while
                    // the guest window (and its port) stays up. Only count "gone" once the port
                    // has actually answered at least once.
                    if (_portWasUp && !_browserGone)
                    {
                        _portDownStreak++;
                        if (_portDownStreak >= 4)
                        {
                            _browserGone = true;
                            _poll?.Stop();
                            _captureBtn.Enabled = false;
                            _status.ForeColor = _theme.WarningText;
                            _status.Text = Utils.Localization.T(
                                "The browser window was closed before sign-in finished. Click Cancel, then try again.");
                            return;
                        }
                    }
                    if (manual)
                    {
                        _status.ForeColor = _theme.WarningText;
                        _status.Text = Utils.Localization.T("Still getting ready — give it a moment, then try again.");
                    }
                    return;
                }

                _portWasUp = true;
                _portDownStreak = 0;

                // While the login page is still up, grab whatever is in the password field. Cached in
                // C# so it survives the final navigation to the home page (where the field is gone);
                // only overwrite with a non-empty read. Stays "" for a passwordless sign-in.
                string pw = await CdpClient.ReadTypedPasswordAsync(_port);
                if (!string.IsNullOrEmpty(pw)) { _typedPassword = pw; }
                if (_captured) { return; }   // may have won the race while awaiting

                foreach (CdpCookie c in cookies)
                {
                    if (c.Name == ".ROBLOSECURITY" && c.Value != null && c.Value.Length >= MinCookieLength
                        && (c.Domain ?? "").IndexOf("roblox.com", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        CapturedCookie = c.Value;
                        CapturedPassword = _typedPassword;
                        _captured = true;
                        _poll?.Stop();
                        _status.ForeColor = _theme.TextMuted;
                        _status.Text = Utils.Localization.T("Signed in — saving…");
                        Utils.Logger.Info("[RobloxLogin] session captured from the private browser.");
                        DialogResult = DialogResult.OK;
                        Close();
                        return;
                    }
                }
                if (manual)
                {
                    _status.ForeColor = _theme.WarningText;
                    _status.Text = Utils.Localization.T("Not signed in yet — finish logging in, then try again.");
                }
            }
            finally
            {
                _busy = false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _poll?.Stop(); _poll?.Dispose(); } catch { }
                _poll = null;

                // Close the throwaway browser via CDP first — Guest mode forks, so killing the
                // launched PID alone can leave the guest window open. Run off the UI thread to
                // avoid a deadlock, and bound the wait. Kill(_proc) is the fallback.
                if (_launched && _port > 0)
                {
                    try { Task.Run(() => CdpClient.CloseBrowserAsync(_port)).Wait(2500); } catch { }
                }
                try { if (_proc != null && !_proc.HasExited) { _proc.Kill(entireProcessTree: true); } } catch { }
                try { _proc?.Dispose(); } catch { }
                _proc = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        if (Directory.Exists(_profileDir)) { Directory.Delete(_profileDir, true); }
                        break;
                    }
                    catch { System.Threading.Thread.Sleep(250); }
                }
            }
            base.Dispose(disposing);
        }
    }
}
