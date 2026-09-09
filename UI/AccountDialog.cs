using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;

namespace AutoClicker.UI
{
    /// <summary>
    /// Add-details / edit for one account in the Roblox Account Manager.
    ///
    /// No credential boxes, and (from 2026-09-07) no Tempo profile/macro/game-link fields either —
    /// the user wanted a plain account manager, not clicker wiring. The session cookie is captured
    /// from a real sign-in (<see cref="RobloxLoginForm"/>), never typed, and rides on the account
    /// through the clone untouched; this screen is just the label, the username/ID (filled from
    /// the captured account, still editable), an icon, and a note.
    /// </summary>
    public sealed class AccountDialog : Form
    {
        private readonly TextBox _name;
        private readonly TextBox _username;
        private readonly TextBox _alias;
        private readonly TextBox _userId;
        private readonly TextBox _icon;
        private readonly TextBox _note;

        /// <summary>The edited account. Read after the dialog returns OK.</summary>
        public RobloxAccount Result { get; private set; }

        public AccountDialog(Theme theme, RobloxAccount existing, bool isAdd = false)
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            Text = Utils.Localization.T(isAdd ? "Add account" : "Edit account");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 286);
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            const int lx = 18, fx = 140;
            const int fwFull = 360;
            int y = 18;

            Controls.Add(UiFactory.Label(Utils.Localization.T("Label"), lx, y + 3, FontStyle.Bold));
            _name = UiFactory.Text(fx, y, fwFull);
            Controls.Add(_name);
            y += 34;

            Controls.Add(UiFactory.Label(Utils.Localization.T("Username"), lx, y + 3, FontStyle.Bold));
            _username = UiFactory.Text(fx, y, 150);
            Controls.Add(_username);
            Controls.Add(UiFactory.Label(Utils.Localization.T("Alias"), 300, y + 3, FontStyle.Bold));
            _alias = UiFactory.Text(360, y, 140);
            Controls.Add(_alias);
            y += 34;

            Controls.Add(UiFactory.Label(Utils.Localization.T("User ID"), lx, y + 3, FontStyle.Bold));
            _userId = UiFactory.Text(fx, y, 150);
            Controls.Add(_userId);
            Controls.Add(UiFactory.Label(Utils.Localization.T("Icon"), 300, y + 3, FontStyle.Bold));
            _icon = UiFactory.Text(360, y, 60);
            Controls.Add(_icon);
            y += 34;

            Controls.Add(UiFactory.Label(Utils.Localization.T("Note"), lx, y + 3, FontStyle.Bold));
            _note = UiFactory.Text(fx, y, fwFull);
            _note.Multiline = true;
            _note.Height = 56;
            Controls.Add(_note);
            y += 66;

            var safety = UiFactory.Caption(Utils.Localization.T(
                "The session for this account is in your encrypted vault. This screen is just its label — no "
                + "passwords or cookies are shown or entered here."), lx, y);
            safety.AutoSize = false;
            safety.Width = ClientSize.Width - lx * 2;
            safety.Height = 30;
            safety.ForeColor = theme.TextMuted;
            Controls.Add(safety);

            var ok = UiFactory.PrimaryButton("Save", ClientSize.Width - 200, ClientSize.Height - 42, 88, 30, theme);
            ok.Click += (s, e) => OnSave();
            var cancel = UiFactory.Button(Utils.Localization.T("Cancel"), ClientSize.Width - 104, ClientSize.Height - 42, 88, 30);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            ThemeManager.Apply(this, theme);

            // Fill in last, AFTER the theme pass. Clone carries the secret cookie through
            // untouched even though nothing here shows it.
            var a = existing != null ? existing.Clone() : new RobloxAccount(Utils.Localization.T("New account"));
            _name.Text = a.Name;
            _username.Text = a.Username;
            _alias.Text = a.Alias;
            _userId.Text = a.UserId;
            _icon.Text = a.Icon;
            _note.Text = a.Note;
            Result = a;
        }

        private void OnSave()
        {
            string name = (_name.Text ?? "").Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, Utils.Localization.T("Give the account a label first."),
                    "Tempo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Result.Name = name;
            Result.Username = (_username.Text ?? "").Trim();
            Result.Alias = (_alias.Text ?? "").Trim();
            Result.UserId = (_userId.Text ?? "").Trim();
            Result.Icon = (_icon.Text ?? "").Trim();
            Result.Note = _note.Text ?? "";
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
