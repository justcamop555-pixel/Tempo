using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Configures the localhost account API: on/off, port, and the shared token other tools must
    /// send. It only edits the settings and shows the token to copy — starting and stopping the
    /// actual <see cref="AccountApiServer"/> is the page's job, so the server's lifetime stays tied
    /// to the vault being unlocked. Reading the token here is fine (it is a local automation
    /// secret the user needs in their scripts, not the master password).
    /// </summary>
    public sealed class AccountApiDialog : Form
    {
        private readonly AppSettings _settings;
        private readonly CheckBox _enable;
        private readonly TextBox _port;
        private readonly TextBox _token;
        private readonly Label _url;

        public AccountApiDialog(Theme theme, AppSettings settings)
        {
            _settings = settings;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            Text = Utils.Localization.T("Local API server");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 470);
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            const int lx = 18;
            int y = 16;

            var intro = new Label
            {
                Text = Utils.Localization.T(
                    "Lets other tools on THIS PC list your accounts and start a launch. It listens only on "
                    + "127.0.0.1, needs the token below on every request, and never gives out passwords or cookies. "
                    + "It runs only while the vault is unlocked."),
                Location = new Point(lx, y),
                Size = new Size(524, 56),
                ForeColor = theme.TextMuted,
                Font = new Font("Segoe UI", 9f)
            };
            Controls.Add(intro);
            y += 62;

            _enable = new CheckBox
            {
                Text = Utils.Localization.T("Enable the local API server"),
                Location = new Point(lx, y),
                AutoSize = true,
                Checked = _settings != null && _settings.AccountApiEnabled,
                ForeColor = theme.Text
            };
            Controls.Add(_enable);
            y += 36;

            Controls.Add(UiFactory.Label(Utils.Localization.T("Port"), lx, y + 3, FontStyle.Bold));
            _port = UiFactory.Text(120, y, 90);
            _port.Text = (_settings?.AccountApiPort > 0 ? _settings.AccountApiPort : 7963)
                .ToString(CultureInfo.InvariantCulture);
            _port.TextChanged += (s, e) => UpdateUrl();
            Controls.Add(_port);
            y += 36;

            Controls.Add(UiFactory.Label(Utils.Localization.T("Token"), lx, y + 3, FontStyle.Bold));
            _token = UiFactory.Text(120, y, 300);
            _token.ReadOnly = true;
            _token.Text = string.IsNullOrEmpty(_settings?.AccountApiToken)
                ? AccountApiServer.NewToken()
                : _settings.AccountApiToken;
            Controls.Add(_token);

            var copy = UiFactory.Button(Utils.Localization.T("Copy"), 426, y, 55, 26);
            copy.Click += (s, e) => { try { Clipboard.SetText(_token.Text); } catch { } };
            var regen = UiFactory.Button(Utils.Localization.T("New"), 485, y, 55, 26);
            regen.Click += (s, e) => _token.Text = AccountApiServer.NewToken();
            Controls.Add(copy);
            Controls.Add(regen);
            y += 38;

            Controls.Add(UiFactory.Label(Utils.Localization.T("URL"), lx, y + 3, FontStyle.Bold));
            _url = new Label
            {
                Location = new Point(120, y + 3),
                Size = new Size(420, 20),
                ForeColor = theme.Accent,
                Font = new Font("Consolas", 9.5f)
            };
            Controls.Add(_url);
            y += 32;

            var endpoints = new Label
            {
                Text = Utils.Localization.T(
                    "Endpoints (add ?token=… to each):\r\n"
                    + "  GET /ping                     — is Tempo up / unlocked\r\n"
                    + "  GET /accounts                 — the account labels (no secrets)\r\n"
                    + "  GET /current                  — last-launched account\r\n"
                    + "  GET /launch?account=NAME      — launch (&placeId=ID optional)\r\n"
                    + "  GET /relogin?account=NAME     — open sign-in to refresh it"),
                Location = new Point(lx, y),
                Size = new Size(524, 108),
                ForeColor = theme.Text,
                Font = new Font("Consolas", 9f)
            };
            Controls.Add(endpoints);
            y += 114;

            var note = new Label
            {
                Text = Utils.Localization.T(
                    "Anything running on your PC that knows the token can start launches — keep it private, and "
                    + "use “New” to rotate it."),
                Location = new Point(lx, y),
                Size = new Size(524, 32),
                ForeColor = theme.WarningText,
                Font = new Font("Segoe UI", 8.5f)
            };
            Controls.Add(note);

            var ok = UiFactory.PrimaryButton("Save", ClientSize.Width - 200, ClientSize.Height - 42, 88, 30, theme);
            ok.Click += (s, e) => OnSave();
            var cancel = UiFactory.Button(Utils.Localization.T("Cancel"), ClientSize.Width - 104, ClientSize.Height - 42, 88, 30);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            ThemeManager.Apply(this, theme);
            UpdateUrl();
        }

        private void UpdateUrl()
        {
            int port = ParsePort();
            _url.Text = "http://127.0.0.1:" + port;
        }

        private int ParsePort()
        {
            if (int.TryParse(_port.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p)
                && p >= 1024 && p <= 65535)
            {
                return p;
            }
            return 7963;
        }

        private void OnSave()
        {
            if (_settings == null) { DialogResult = DialogResult.Cancel; Close(); return; }

            int port = ParsePort();
            _settings.AccountApiPort = port;
            _settings.AccountApiToken = string.IsNullOrEmpty(_token.Text)
                ? AccountApiServer.NewToken() : _token.Text.Trim();
            _settings.AccountApiEnabled = _enable.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
