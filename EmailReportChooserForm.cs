using System;
using System.Drawing;
using System.Windows.Forms;

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
        Copy
    }

    /// <summary>
    /// A small themed dialog that lets the user pick how to send a bug report —
    /// their email app, Gmail or Outlook in a browser, or simply copying the
    /// pre-filled text so they can paste it anywhere they like.
    /// </summary>
    public sealed class EmailReportChooserForm : Form
    {
        public EmailReportChannel Choice { get; private set; } = EmailReportChannel.None;

        public EmailReportChooserForm(Theme theme)
        {
            theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            AutoScaleMode = AutoScaleMode.Font;
            Text = "Send a bug report";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(440, 386);
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            var accentStrip = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(440, 4),
                BackColor = theme.Accent
            };
            Controls.Add(accentStrip);

            var heading = new Label
            {
                Text = "Send a bug report",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = theme.Accent,
                AutoSize = false,
                Location = new Point(24, 22),
                Size = new Size(392, 30)
            };
            Controls.Add(heading);

            var prompt = new Label
            {
                Text = "Choose how you'd like to send it. Either way it's pre-filled with a " +
                       "template and your system details — just add what happened.",
                ForeColor = theme.TextMuted,
                AutoSize = false,
                Location = new Point(24, 54),
                Size = new Size(392, 44)
            };
            Controls.Add(prompt);

            Button email = UiFactory.PrimaryButton("Your email app", 24, 104, 392, 40, theme);
            email.Click += (s, e) => Pick(EmailReportChannel.EmailApp);
            Controls.Add(email);

            Button gmail = MakeChoice(theme, "Gmail (in your browser)", 150);
            gmail.Click += (s, e) => Pick(EmailReportChannel.Gmail);
            Controls.Add(gmail);

            Button outlook = MakeChoice(theme, "Outlook (in your browser)", 196);
            outlook.Click += (s, e) => Pick(EmailReportChannel.Outlook);
            Controls.Add(outlook);

            Button yahoo = MakeChoice(theme, "Yahoo Mail (in your browser)", 242);
            yahoo.Click += (s, e) => Pick(EmailReportChannel.Yahoo);
            Controls.Add(yahoo);

            Button copy = MakeChoice(theme, "Copy report to clipboard", 288);
            copy.Click += (s, e) => Pick(EmailReportChannel.Copy);
            Controls.Add(copy);

            var cancel = UiFactory.Button("Cancel", 24, 342, 392, 32);
            cancel.BackColor = theme.Background;
            cancel.ForeColor = theme.TextMuted;
            cancel.FlatAppearance.BorderColor = theme.Border;
            cancel.Click += (s, e) => { Choice = EmailReportChannel.None; DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            CancelButton = cancel;
        }

        private static Button MakeChoice(Theme theme, string text, int y)
        {
            Button b = UiFactory.Button(text, 24, y, 392, 40);
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
