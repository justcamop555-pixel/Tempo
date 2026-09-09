using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using AutoClicker.Models;

namespace AutoClicker.UI
{
    /// <summary>
    /// SPIKE — a Tempo page rendered as HTML in WebView2, to find out what that would
    /// really cost before anyone commits to it.
    ///
    /// It is behind <see cref="AppSettings.ExperimentalWebUi"/>, which has no switch in
    /// the UI on purpose: nothing here is a supported way to run Tempo, and a setting
    /// people can find is a setting people will turn on. Off, not one line of this file
    /// runs and WebView2 is never created — which is the point, because the startup and
    /// memory cost is half of what is being measured.
    ///
    /// What it deliberately proves, rather than assumes:
    ///   * the theme survives the trip — the palette goes in as CSS custom properties,
    ///     so all 38 themes are 38 variable sets rather than 38 palettes to paint by hand;
    ///   * the bridge works BOTH ways, with the three shapes that matter (a bool, an int
    ///     and an enum), writing through to the real AppSettings and saving;
    ///   * how long WebView2 takes to come up, and what it adds to memory.
    ///
    /// What it is NOT: the whole Settings tab. Eight cards of controls would take a week
    /// and prove nothing this doesn't.
    ///
    /// KNOWN CONFLICT, worth remembering if this goes further. A WebView2 is a separate
    /// child HWND with its own process, so it does not composite with WinForms
    /// transparency: Tempo's animated wallpaper (ApplyBackgroundGif + WS_EX_COMPOSITED,
    /// with every control transparent over a cached composite) cannot show through it.
    /// A page that goes HTML has to bring its own background with it.
    /// </summary>
    public sealed class WebSettingsHost : Panel
    {
        private readonly Theme _theme;
        private readonly AppSettings _settings;
        private readonly Action _onChanged;
        private WebView2 _view;
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>Raised on the UI thread after a setting is changed from the page.</summary>
        public WebSettingsHost(Theme theme, AppSettings settings, Action onChanged)
        {
            _theme = theme ?? Theme.ForKind(ThemeKind.Dark);
            _settings = settings;
            _onChanged = onChanged;
            Dock = DockStyle.Fill;
            BackColor = _theme.Background;
        }

        /// <summary>
        /// Creates the browser and loads the page. Async and swallowing: a machine with no
        /// WebView2 runtime must fall back to the ordinary tab, never take the app down.
        /// </summary>
        public async void Start()
        {
            try
            {
                string dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AutoClicker", "webview2");
                Directory.CreateDirectory(dataDir);

                CoreWebView2Environment env =
                    await CoreWebView2Environment.CreateAsync(null, dataDir, null);

                _view = new WebView2 { Dock = DockStyle.Fill };
                Controls.Add(_view);
                await _view.EnsureCoreWebView2Async(env);

                CoreWebView2Settings s = _view.CoreWebView2.Settings;
                s.AreDefaultContextMenusEnabled = false;
                s.IsStatusBarEnabled = false;
                s.AreDevToolsEnabled = false;
                s.IsZoomControlEnabled = false;

                _view.CoreWebView2.WebMessageReceived += OnWebMessage;
                _view.DefaultBackgroundColor = _theme.Background;
                _view.CoreWebView2.NavigateToString(BuildHtml());

                Utils.Logger.Info("[WebUI] WebView2 ready in " + _clock.ElapsedMilliseconds +
                                  " ms (runtime " + CoreWebView2Environment.GetAvailableBrowserVersionString() + ").");
            }
            catch (Exception ex)
            {
                // The most likely cause by far is no Evergreen runtime installed.
                Utils.Logger.Warn("[WebUI] WebView2 unavailable (" + ex.Message +
                                  ") — the experimental page cannot load.");
                Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    ForeColor = _theme.TextMuted,
                    BackColor = _theme.Background,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Text = "WebView2 is not available on this PC.",
                });
            }
        }

        /// <summary>A message from the page: one setting changed.</summary>
        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                // Treated as untrusted input even though we authored the page: it is a
                // browser, and parsing defensively costs nothing.
                string raw = e.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(raw)) { return; }

                int split = raw.IndexOf('=');
                if (split <= 0) { return; }
                string key = raw.Substring(0, split);
                string value = raw.Substring(split + 1);

                switch (key)
                {
                    case "AlwaysOnTop":
                        _settings.AlwaysOnTop = value == "1";
                        break;
                    case "MinimizeToTrayOnClose":
                        _settings.MinimizeToTrayOnClose = value == "1";
                        break;
                    case "StartMinimizedToTray":
                        _settings.StartMinimizedToTray = value == "1";
                        break;
                    case "ConfirmBeforeExitWhileRunning":
                        _settings.ConfirmBeforeExitWhileRunning = value == "1";
                        break;
                    case "AnimateCustomLogo":
                        _settings.AnimateCustomLogo = value == "1";
                        break;
                    case "LogoAnimationSpeed":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pct))
                        {
                            _settings.LogoAnimationSpeed = pct;
                            Utils.AnimatedLogo.SpeedPercent = pct;
                        }
                        break;
                    case "WindowOpacity":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int op))
                        {
                            _settings.WindowOpacity = Math.Max(50, Math.Min(100, op));
                        }
                        break;
                    default:
                        return;                       // unknown key: ignore, don't guess
                }

                Utils.Logger.Info("[WebUI] " + key + " = " + value + " (from the page).");
                try { Persistence.SettingsManager.Save(_settings); } catch { }
                try { _onChanged?.Invoke(); } catch { }
            }
            catch (Exception ex) { Utils.Logger.Swallow("WebSettingsHost.OnWebMessage", ex); }
        }

        private static string Hex(Color c)
        {
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        private string BuildHtml()
        {
            string html = ReadResource("settings.html");
            if (html == null)
            {
                return "<html><body>settings.html is missing from the build.</body></html>";
            }

            // The whole theme goes in as custom properties. This is the part worth
            // looking at: 38 themes become 38 of these blocks, and nothing in the page
            // knows a colour by name.
            var vars = new StringBuilder();
            vars.Append("--bg:").Append(Hex(_theme.Background)).Append(';');
            vars.Append("--surface:").Append(Hex(_theme.Surface)).Append(';');
            vars.Append("--surface2:").Append(Hex(_theme.Surface2)).Append(';');
            vars.Append("--border:").Append(Hex(_theme.Border)).Append(';');
            vars.Append("--text:").Append(Hex(_theme.Text)).Append(';');
            vars.Append("--muted:").Append(Hex(_theme.TextMuted)).Append(';');
            vars.Append("--accent:").Append(Hex(_theme.Accent)).Append(';');
            vars.Append("--on-accent:").Append(Hex(_theme.OnAccent)).Append(';');

            var state = new StringBuilder();
            state.Append("{");
            state.Append("\"AlwaysOnTop\":").Append(_settings.AlwaysOnTop ? "true" : "false").Append(',');
            state.Append("\"MinimizeToTrayOnClose\":").Append(_settings.MinimizeToTrayOnClose ? "true" : "false").Append(',');
            state.Append("\"StartMinimizedToTray\":").Append(_settings.StartMinimizedToTray ? "true" : "false").Append(',');
            state.Append("\"ConfirmBeforeExitWhileRunning\":").Append(_settings.ConfirmBeforeExitWhileRunning ? "true" : "false").Append(',');
            state.Append("\"AnimateCustomLogo\":").Append(_settings.AnimateCustomLogo ? "true" : "false").Append(',');
            state.Append("\"LogoAnimationSpeed\":").Append(_settings.LogoAnimationSpeed).Append(',');
            state.Append("\"WindowOpacity\":").Append(_settings.WindowOpacity);
            state.Append("}");

            return html
                .Replace("/*THEME*/", vars.ToString())
                .Replace("/*STATE*/", state.ToString());
        }

        private static string ReadResource(string name)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                foreach (string res in asm.GetManifestResourceNames())
                {
                    if (res.EndsWith(name, StringComparison.OrdinalIgnoreCase))
                    {
                        using (Stream st = asm.GetManifestResourceStream(res))
                        using (var sr = new StreamReader(st, Encoding.UTF8))
                        {
                            return sr.ReadToEnd();
                        }
                    }
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("WebSettingsHost.ReadResource", ex); }
            return null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _view?.Dispose(); } catch { }
                _view = null;
            }
            base.Dispose(disposing);
        }
    }
}
