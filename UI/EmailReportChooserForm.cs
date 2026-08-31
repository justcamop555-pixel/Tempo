using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>How the user wants to send a bug report.</summary>
    public enum EmailReportChannel
    {
        None,
        EmailApp,
        Gmail,
        Outlook,
        Yahoo,
        Copy,
        GitHub
    }

    /// <summary>
    /// Composes a bug report: shows the user EXACTLY what will be sent, lets them
    /// edit it, and only then asks how to send it.
    ///
    /// This used to be a chooser only — six buttons and nothing else. Picking one
    /// opened a browser or mail client already filled in, so the first time anyone
    /// saw the contents of their own bug report was in the window that was about to
    /// send it. The crash dialog had shown an editable copy for exactly this reason
    /// since it was written; the deliberate "Report a bug…" path, the one a person
    /// chooses to use, had no equivalent.
    ///
    /// The recent activity log is offered here rather than attached silently. It is
    /// the most revealing thing Tempo can include — it records the paths of macros
    /// and settings a person has imported or exported — and it used to be appended
    /// to the clipboard report with no mention and no way to decline.
    /// </summary>
    public sealed class EmailReportChooserForm : Form
    {
        /// <summary>Marks where the optional log section begins, so it can be removed again.</summary>
        private const string LogHeader = "--- recent activity log ---";

        private readonly TextBox _body;
        private readonly CheckBox _includeLog;

        public EmailReportChannel Choice { get; private set; } = EmailReportChannel.None;

        /// <summary>The report as the user left it — this is what gets sent.</summary>
        public string ReportText
        {
            get { return _body == null ? string.Empty : _body.Text; }
        }

        public EmailReportChooserForm(Theme theme, EmailReportChannel lastUsed = EmailReportChannel.None)
        {
            theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = Localization.T("Send a bug report");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(560, 596);
            BackColor = theme.Background;
            // Title bar in the theme too, so the dialog matches the app.
            ThemeManager.ApplyWindowChrome(this, theme);
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            var accentStrip = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(560, 4),
                BackColor = theme.Accent
            };
            Controls.Add(accentStrip);

            var heading = new Label
            {
                Text = Localization.T("Send a bug report"),
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = theme.Accent,
                AutoSize = false,
                Location = new Point(24, 22),
                Size = new Size(512, 30)
            };
            Controls.Add(heading);

            // The privacy position, stated where the decision is made. The same promise
            // the crash window makes — and now true on this path as well.
            var privacy = new Label
            {
                Text = Localization.T(
                       "Nothing is sent until you choose a way to send it below. Your Windows "
                       + "account name, PC name and personal folders are removed automatically — "
                       + "and this is the whole report, so you can edit or delete anything else "
                       + "before it goes."),
                ForeColor = theme.TextMuted,
                AutoSize = false,
                Location = new Point(24, 56),
                Size = new Size(512, 56)
            };
            Controls.Add(privacy);

            _body = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = theme.InputBackground,
                ForeColor = theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5f),
                Location = new Point(24, 118),
                Size = new Size(512, 210),
                Text = (CrashReporter.BlankReportBody() ?? string.Empty).Replace("\n", "\r\n")
            };
            Controls.Add(_body);

            _includeLog = new CheckBox
            {
                Text = Localization.T("Also include Tempo's recent activity log"),
                ForeColor = theme.Text,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(24, 336)
            };
            _includeLog.CheckedChanged += (s, e) => ToggleLog();
            Controls.Add(_includeLog);

            var logHint = new Label
            {
                Text = Localization.T(
                       "Helps a lot, but may mention files you have opened in Tempo — it is "
                       + "added to the text above so you can read it first."),
                ForeColor = theme.TextMuted,
                AutoSize = false,
                Location = new Point(44, 358),
                Size = new Size(492, 32)
            };
            Controls.Add(logHint);

            var how = new Label
            {
                Text = Localization.T("How would you like to send it?"),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = theme.Text,
                AutoSize = false,
                Location = new Point(24, 394),
                Size = new Size(512, 20)
            };
            Controls.Add(how);

            // Two columns: six channels stacked in one column made the dialog taller
            // than the report it is meant to show.
            Button github = MakeChoice(theme, "GitHub issue", 24, 418);
            github.Click += (s, e) => Pick(EmailReportChannel.GitHub);
            Controls.Add(github);

            Button email = MakeChoice(theme, "Your email app", 288, 418);
            email.Click += (s, e) => Pick(EmailReportChannel.EmailApp);
            Controls.Add(email);

            Button gmail = MakeChoice(theme, "Gmail (in your browser)", 24, 460);
            gmail.Click += (s, e) => Pick(EmailReportChannel.Gmail);
            Controls.Add(gmail);

            Button outlook = MakeChoice(theme, "Outlook (in your browser)", 288, 460);
            outlook.Click += (s, e) => Pick(EmailReportChannel.Outlook);
            Controls.Add(outlook);

            Button yahoo = MakeChoice(theme, "Yahoo Mail (in your browser)", 24, 502);
            yahoo.Click += (s, e) => Pick(EmailReportChannel.Yahoo);
            Controls.Add(yahoo);

            Button copy = MakeChoice(theme, "Copy report to clipboard", 288, 502);
            copy.Click += (s, e) => Pick(EmailReportChannel.Copy);
            Controls.Add(copy);

            var cancel = UiFactory.Button("Cancel", 24, 550, 512, 32);
            cancel.BackColor = theme.Background;
            cancel.ForeColor = theme.TextMuted;
            cancel.FlatAppearance.BorderColor = theme.Border;
            cancel.Click += (s, e) => { Choice = EmailReportChannel.None; DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            CancelButton = cancel;

            // Highlight and focus whichever channel was used last time, so a repeat
            // report is just Enter.
            Button lastBtn = lastUsed == EmailReportChannel.GitHub ? github
                           : lastUsed == EmailReportChannel.EmailApp ? email
                           : lastUsed == EmailReportChannel.Gmail ? gmail
                           : lastUsed == EmailReportChannel.Outlook ? outlook
                           : lastUsed == EmailReportChannel.Yahoo ? yahoo
                           : lastUsed == EmailReportChannel.Copy ? copy
                           : null;
            if (lastBtn != null)
            {
                // Through F(), not "+=". Appending a raw English suffix to an already
                // translated caption left every language reading "Gmail (dans votre
                // navigateur)   ·  last used" — and because the string was appended
                // rather than assigned, no translation audit could see it either.
                lastBtn.Text = Localization.F("{0}   ·  last used", lastBtn.Text);
                lastBtn.BackColor = theme.Surface;
                lastBtn.FlatAppearance.BorderColor = theme.Accent;
                AcceptButton = lastBtn;
                Shown += (s, e) => lastBtn.Focus();
            }
        }

        /// <summary>
        /// Adds or removes the log section, leaving anything the user typed above it
        /// untouched — the marker line is what makes that possible. Rebuilding the
        /// whole box on each toggle would throw away their description.
        /// </summary>
        private void ToggleLog()
        {
            try
            {
                string text = _body.Text;
                int at = text.IndexOf(LogHeader, StringComparison.Ordinal);

                if (!_includeLog.Checked)
                {
                    if (at >= 0) { _body.Text = text.Substring(0, at).TrimEnd() + Environment.NewLine; }
                    return;
                }

                if (at >= 0) { return; }        // already there
                string tail = CrashReporter.RecentLogTail(25);
                if (string.IsNullOrEmpty(tail))
                {
                    _includeLog.Checked = false;
                    MessageBox.Show(this, Localization.T("There is no activity log to include yet."),
                        "Tempo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                _body.Text = text.TrimEnd() + Environment.NewLine + Environment.NewLine
                           + LogHeader + Environment.NewLine
                           + tail.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
                _body.SelectionStart = _body.TextLength;
                _body.ScrollToCaret();
            }
            catch (Exception ex)
            {
                Logger.Swallow("EmailReportChooserForm.ToggleLog", ex);
            }
        }

        private static Button MakeChoice(Theme theme, string text, int x, int y)
        {
            Button b = UiFactory.Button(text, x, y, 248, 36);
            b.BackColor = theme.Surface2;
            b.ForeColor = theme.Text;
            b.FlatAppearance.BorderColor = theme.Border;
            return b;
        }

        private void Pick(EmailReportChannel channel)
        {
            Choice = channel;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
