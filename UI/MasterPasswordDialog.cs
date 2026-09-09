using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;

namespace AutoClicker.UI
{
    /// <summary>
    /// Collects the current and new master password when changing the vault's password.
    /// It only gathers and sanity-checks the three fields; the actual re-key and the check
    /// that the current password is right happen in <see cref="Persistence.AccountVault.ChangeMasterPassword"/>,
    /// which verifies against the file on disk.
    /// </summary>
    public sealed class MasterPasswordDialog : Form
    {
        private readonly TextBox _current;
        private readonly TextBox _next;
        private readonly TextBox _confirm;
        private readonly Label _error;

        public string Current { get; private set; } = "";
        public string Next { get; private set; } = "";

        public MasterPasswordDialog(Theme theme)
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            Text = Utils.Localization.T("Change master password");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(430, 236);
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            const int lx = 18, fx = 160, fw = 250;
            int y = 20;

            Controls.Add(UiFactory.Label(Utils.Localization.T("Current password"), lx, y + 3, FontStyle.Bold));
            _current = UiFactory.Text(fx, y, fw);
            _current.UseSystemPasswordChar = true;
            Controls.Add(_current);
            y += 36;

            Controls.Add(UiFactory.Label(Utils.Localization.T("New password"), lx, y + 3, FontStyle.Bold));
            _next = UiFactory.Text(fx, y, fw);
            _next.UseSystemPasswordChar = true;
            Controls.Add(_next);
            y += 36;

            Controls.Add(UiFactory.Label(Utils.Localization.T("Confirm new password"), lx, y + 3, FontStyle.Bold));
            _confirm = UiFactory.Text(fx, y, fw);
            _confirm.UseSystemPasswordChar = true;
            _confirm.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { OnOk(); } };
            Controls.Add(_confirm);
            y += 34;

            var show = new CheckBox
            {
                Text = Utils.Localization.T("Show passwords"),
                Location = new Point(fx, y),
                AutoSize = true,
                ForeColor = theme.Text
            };
            show.CheckedChanged += (s, e) =>
            {
                bool hide = !show.Checked;
                _current.UseSystemPasswordChar = hide;
                _next.UseSystemPasswordChar = hide;
                _confirm.UseSystemPasswordChar = hide;
            };
            Controls.Add(show);
            y += 28;

            _error = new Label
            {
                Location = new Point(lx, y),
                Size = new Size(fw + fx - lx, 20),
                ForeColor = theme.DangerText,
                Font = new Font("Segoe UI", 8.75f)
            };
            Controls.Add(_error);

            var ok = UiFactory.PrimaryButton("Change", ClientSize.Width - 200, ClientSize.Height - 44, 88, 30, theme);
            ok.Click += (s, e) => OnOk();
            var cancel = UiFactory.Button(Utils.Localization.T("Cancel"), ClientSize.Width - 104, ClientSize.Height - 44, 88, 30);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            ThemeManager.Apply(this, theme);
        }

        private void OnOk()
        {
            string cur = _current.Text ?? "";
            string nxt = _next.Text ?? "";
            string cfm = _confirm.Text ?? "";

            if (nxt.Length < 8)
            {
                _error.Text = Utils.Localization.T("The new password needs at least 8 characters.");
                return;
            }
            if (nxt != cfm)
            {
                _error.Text = Utils.Localization.T("The new passwords do not match.");
                return;
            }

            Current = cur;
            Next = nxt;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
