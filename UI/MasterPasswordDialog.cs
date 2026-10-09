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
            // Built in 100% units; scaled with the screen at the end (UiFactory.ScaleToScreen).
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

            const int lx = 18, fw = 250;
            Label currentLabel = UiFactory.Label(Utils.Localization.T("Current password"), lx, 0, FontStyle.Bold);
            Label nextLabel = UiFactory.Label(Utils.Localization.T("New password"), lx, 0, FontStyle.Bold);
            Label confirmLabel = UiFactory.Label(Utils.Localization.T("Confirm new password"), lx, 0, FontStyle.Bold);
            // The fields start after the longest label in this language, and the window widens to
            // match: at a fixed x = 160, French "Confirmer le nouveau mot de passe" ran 62 px into its
            // field (Spanish, German and Italian 12–22 px).
            // (In 100% units: the window is scaled after it is built — UiFactory.Measure96.)
            int widest = 0;
            foreach (Label l in new[] { currentLabel, nextLabel, confirmLabel })
            {
                widest = Math.Max(widest, UiFactory.Measure96(l.Text, l.Font, 0, TextFormatFlags.Default).Width);
            }
            int fx = Math.Max(160, lx + widest + 12);
            ClientSize = new Size(Math.Max(430, fx + fw + 20), 236);
            int y = 20;

            currentLabel.Top = y + 3;
            Controls.Add(currentLabel);
            _current = UiFactory.Text(fx, y, fw);
            _current.UseSystemPasswordChar = true;
            Controls.Add(_current);
            y += 36;

            nextLabel.Top = y + 3;
            Controls.Add(nextLabel);
            _next = UiFactory.Text(fx, y, fw);
            _next.UseSystemPasswordChar = true;
            Controls.Add(_next);
            y += 36;

            confirmLabel.Top = y + 3;
            Controls.Add(confirmLabel);
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
            UiFactory.ScaleToScreen(this);
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
