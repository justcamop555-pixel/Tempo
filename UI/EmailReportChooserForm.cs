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
        private readonly ToolTip _tips;

        public EmailReportChannel Choice { get; private set; } = EmailReportChannel.None;

        /// <summary>The report as the user left it — this is what gets sent.</summary>
        public string ReportText
        {
            get { return _body == null ? string.Empty : _body.Text; }
        }

        public EmailReportChooserForm(Theme theme, EmailReportChannel lastUsed = EmailReportChannel.None)
        {
            theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            // Built in 100% units; scaled with the screen at the end (UiFactory.ScaleToScreen).
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
                ForeColor = theme.AccentText,
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
            // As tall as its words — four lines in French, German and Italian, where the box held
            // three and a half and cut the last — with everything under it moved down to match.
            // (In 100% units: the window is scaled after it is built — UiFactory.Measure96.)
            int privacyHeight = UiFactory.Measure96(privacy.Text, Font, 512, TextFormatFlags.Default).Height + 2;
            int shift = Math.Max(0, privacyHeight - privacy.Height);
            privacy.Height += shift;
            Controls.Add(privacy);

            _body = new TextBox
            {
                Multiline = true,
                // Enter is a new line in the report. Without this, Enter in the box pressed the form's default
                // button — the channel used last time — and sent a report half-written, mid-sentence.
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = theme.InputBackground,
                ForeColor = theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5f),
                Location = new Point(24, 118),
                Size = new Size(512, 210),
                // One kind of line break: the diagnostics come with "\r\n" and the "\n" → "\r\n" turned those into
                // "\r\r\n", which went out in every report.
                Text = (CrashReporter.BlankReportBody() ?? string.Empty).Replace("\r\n", "\n").Replace("\n", "\r\n")
            };
            Controls.Add(_body);
            // Ready to type: the caret on the empty line under "Describe the bug:", the first thing to fill in.
            Shown += (s, e) =>
            {
                try
                {
                    int line = _body.Text.IndexOf("\r\n", StringComparison.Ordinal);
                    _body.Focus();
                    _body.Select(line >= 0 ? line + 2 : 0, 0);
                    _body.ScrollToCaret();
                }
                catch (Exception ex) { Logger.Swallow("EmailReportChooserForm.Shown", ex); }
            };

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
            // As tall as its words too: two lines at 150% are 50 px, and in its 48 the second one was not
            // drawn ("…it is added to the text above so you can read it first" gone in every language).
            int hintShift = Math.Max(0, UiFactory.Measure96(logHint.Text, Font, logHint.Width, TextFormatFlags.Default).Height + 2 - logHint.Height);
            logHint.Height += hintShift;
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

            // What each way means, before choosing one: which is public, which needs an account, which is private.
            var howHint = new Label
            {
                Text = Localization.T("A GitHub issue is public and needs a free GitHub account. Email goes privately to "
                                      + "the developer. Copy puts the report on the clipboard to paste anywhere."),
                ForeColor = theme.TextMuted,
                AutoSize = false,
                Location = new Point(24, 414),
                Size = new Size(512, 16)
            };
            // As tall as its words (in 100% units: the window is scaled after it is built), and the buttons below
            // move down by all of it.
            howHint.Height = Math.Max(16, UiFactory.Measure96(howHint.Text, Font, howHint.Width, TextFormatFlags.Default).Height + 2);
            int channelShift = howHint.Height + 4;
            Controls.Add(howHint);

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

            // Each way to send, said on hover too.
            _tips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 400, ReshowDelay = 120 };
            TipStyle.Set(_tips, github, Localization.T("Opens a new issue on Tempo's GitHub page in your browser with this report filled in. Issues are public, and you need a free GitHub account to post one."));
            TipStyle.Set(_tips, email, Localization.T("Opens a new email to the developer in your email app (Outlook, Mail, Thunderbird…) with this report in it."));
            TipStyle.Set(_tips, gmail, Localization.T("Opens a new Gmail message to the developer in your browser with this report in it."));
            TipStyle.Set(_tips, outlook, Localization.T("Opens a new Outlook.com message to the developer in your browser with this report in it."));
            TipStyle.Set(_tips, yahoo, Localization.T("Opens a new Yahoo Mail message to the developer in your browser with this report in it."));
            TipStyle.Set(_tips, copy, Localization.F("Puts the report on the clipboard, to paste into any email to {0} — or anywhere else.", CrashReporter.SupportEmail));
            Disposed += (s, e) => _tips.Dispose();

            // All from the positions as built: everything under the privacy note moves by its growth, everything
            // under the log hint by that one's as well, and the channel buttons and Cancel by the "which way" hint.
            {
                int below = _body.Top, belowHint = how.Top, belowChannels = howHint.Top + 1;
                foreach (Control c in Controls)
                {
                    int move = (c.Top >= below ? shift : 0) + (c.Top >= belowHint ? hintShift : 0) +
                               (c.Top >= belowChannels ? channelShift : 0);
                    if (move > 0) { c.Top += move; }
                }
                ClientSize = new Size(ClientSize.Width, ClientSize.Height + shift + hintShift + channelShift);
            }

            // Highlight whichever channel was used last time and make it the default button.
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
                // The suffix makes it the longest caption here, in a column that cannot widen: Italian
                // needed 259 px of the 248. A size smaller rather than a cut word, as the main window's
                // button pass does.
                if (lastBtn is RoundedButton rb)
                {
                    foreach (float size in new[] { 8.25f, 7.5f })
                    {
                        // The text in 100% units, as the column is: the window is scaled after this
                        // (screen pixels against it made a 150% screen shrink a caption that fitted).
                        if (Math.Ceiling(rb.CaptionWidthNeeded(0) / UiFactory.BuildScale) + 8 <= rb.Width) { break; }
                        rb.Font = new Font(rb.Font.FontFamily, size, rb.Font.Style);
                    }
                }
                lastBtn.BackColor = theme.Surface;
                lastBtn.FlatAppearance.BorderColor = theme.Accent;
                // The default button, but not the focus: the report box has it, because describing the problem comes
                // first. Enter there is a new line (AcceptsReturn); anywhere else it sends this way.
                AcceptButton = lastBtn;
            }

            UiFactory.ScaleToScreen(this);
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
            // A report that says nothing about what went wrong is the PC's details and nothing else. Ask once; "No"
            // goes back to the box with the caret where the description goes.
            if (CrashReporter.IsUndescribed(_body.Text))
            {
                var answer = MessageBox.Show(this,
                    Localization.T("You haven't described the problem yet. A few words about what you were doing and "
                                   + "what went wrong make the report far more useful.\n\nSend it anyway?"),
                    "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    try
                    {
                        int line = _body.Text.IndexOf("\r\n", StringComparison.Ordinal);
                        _body.Focus();
                        _body.Select(line >= 0 ? line + 2 : 0, 0);
                        _body.ScrollToCaret();
                    }
                    catch (Exception ex) { Logger.Swallow("EmailReportChooserForm.Pick", ex); }
                    return;
                }
            }
            Choice = channel;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
