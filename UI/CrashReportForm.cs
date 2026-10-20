using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Shown when an unhandled error is caught. Tells the user plainly what
    /// happened and offers one-click reporting (opens a pre-filled GitHub issue),
    /// opening the saved report, or copying the details. Self-contained styling so
    /// it works even if the main window/theme isn't available.
    /// </summary>
    public sealed class CrashReportForm : Form
    {
        private readonly Exception _ex;
        private readonly string _report;
        private readonly string _reportPath;
        private readonly bool _fatal;

        /// <summary>True if the user asked to quit the app.</summary>
        public bool UserChoseQuit { get; private set; }

        public CrashReportForm(Exception ex, string context, string report, string reportPath, bool fatal)
        {
            // Built in 100% units; scaled with the screen at the end (UiFactory.ScaleToScreen).
            _ex = ex;
            _report = report ?? string.Empty;
            _reportPath = reportPath;
            _fatal = fatal;

            BuildUi();
            UiFactory.ScaleToScreen(this);
        }

        private void BuildUi()
        {
            // Its own fixed palette, so it works when the theme is what broke — HDR-corrected
            // like the theme when the window has the correction on.
            Color bg = Theme.Corrected(Color.FromArgb(24, 26, 32));
            Color panel = Theme.Corrected(Color.FromArgb(34, 37, 46));
            Color text = Theme.Corrected(Color.FromArgb(232, 234, 240));
            Color muted = Theme.Corrected(Color.FromArgb(150, 156, 170));
            Color accent = Theme.Corrected(Color.FromArgb(124, 92, 255));

            Text = Utils.Localization.T("Tempo — something went wrong");
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            ScreenFit.Attach(this);   // on the screen, whole, however small it is (see ScreenFit)
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(540, 420);
            BackColor = bg;
            ForeColor = text;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            TopMost = true;

            var heading = new Label
            {
                Text = Utils.Localization.T(_fatal ? "Tempo has to close" : "Tempo hit an unexpected error"),
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = text,
                AutoSize = false,
                Location = new Point(20, 18),
                Size = new Size(500, 30)
            };
            Controls.Add(heading);

            var sub = new Label
            {
                Text = Utils.Localization.T(_fatal
                    ? "Sorry about that. The details below have been saved. Reporting them helps get it fixed."
                    : "Sorry about that. You can keep using Tempo. Reporting the details helps get it fixed."),
                ForeColor = muted,
                AutoSize = false,
                Location = new Point(20, 50),
                Size = new Size(500, 20)
            };
            Controls.Add(sub);

            var privacy = new Label
            {
                // States what the code now actually does. The old wording promised the
                // account name was removed, and it was — from the body shown here, but
                // not from the issue title built beside it out of the raw exception
                // message, which the reader never got to see.
                Text = Localization.T(
                       "Privacy: this includes only Tempo's version, your Windows version and the "
                       + "technical error — never your clicks, settings or files. Your Windows account "
                       + "name and PC name are removed, from the subject line as well as the text. "
                       + "Nothing is sent until you submit. You can edit the text below first."),
                ForeColor = muted,
                Font = new Font("Segoe UI", 8f, FontStyle.Italic),
                AutoSize = false,
                Location = new Point(20, 70),
                Size = new Size(500, 44)
            };
            Controls.Add(privacy);

            // Each paragraph as tall as its words, each placed under the one above, and the rest of
            // the window moved down by what they grew: the apology is two lines in French, German and
            // Italian and the privacy note four in French and German, and their boxes cut the last.
            sub.Height = Math.Max(sub.Height, TextHeight(sub, 500));
            privacy.Top = sub.Bottom;
            privacy.Height = Math.Max(privacy.Height, TextHeight(privacy, 500));
            int shift = Math.Max(0, privacy.Bottom + 4 - 118);

            var box = new TextBox
            {
                Multiline = true,
                ReadOnly = false,
                ScrollBars = ScrollBars.Vertical,
                BackColor = panel,
                ForeColor = text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5f, FontStyle.Regular),
                Location = new Point(20, 118 + shift),
                Size = new Size(500, 204),
                Text = _report.Replace("\n", "\r\n")
            };
            Controls.Add(box);

            int by = 334 + shift;

            // Row 1 — reporting options.
            var reportBtn = MakeButton("Report on GitHub", 20, by, 160, accent, Color.White, true);
            reportBtn.Click += (s, e) => Open(CrashReporter.IssueUrlFromReport(_ex, box.Text));
            Controls.Add(reportBtn);

            var emailBtn = MakeButton("Email report", 188, by, 150, accent, Color.White, true);
            emailBtn.Click += (s, e) => Open(CrashReporter.MailtoUrlFromReport(_ex, box.Text));
            Controls.Add(emailBtn);

            // Row 2 — utilities + dismiss.
            int by2 = by + 44;
            var openBtn = MakeButton("Open report", 20, by2, 110, panel, text, false);
            openBtn.Enabled = !string.IsNullOrEmpty(_reportPath);
            openBtn.Click += (s, e) => Open(_reportPath);
            Controls.Add(openBtn);

            var copyBtn = MakeButton("Copy details", 138, by2, 110, panel, text, false);
            copyBtn.Click += (s, e) =>
            {
                try { Clipboard.SetText(box.Text); } catch { /* clipboard may be busy */ }
            };
            Controls.Add(copyBtn);

            var closeBtn = MakeButton(_fatal ? "Close" : "Continue", 410, by2, 110, panel, text, false);
            closeBtn.Click += (s, e) => { UserChoseQuit = _fatal; Close(); };
            Controls.Add(closeBtn);

            ClientSize = new Size(540, by2 + 50);

            if (!_fatal)
            {
                var quit = new LinkLabel
                {
                    Text = Localization.T("Quit Tempo"),
                    LinkColor = muted,
                    ActiveLinkColor = accent,
                    AutoSize = true,
                    Location = new Point(20, by2 + 44)
                };
                quit.LinkClicked += (s, e) => { UserChoseQuit = true; Close(); };
                Controls.Add(quit);
                ClientSize = new Size(540, by2 + 78);
            }

            AcceptButton = reportBtn;
        }

        /// <summary>
        /// How tall <paramref name="l"/>'s words are wrapped at <paramref name="width"/>, in its own font —
        /// in 100% units, because the window is scaled after it is built (UiFactory.Measure96).
        /// </summary>
        private static int TextHeight(Label l, int width)
        {
            return UiFactory.Measure96(l.Text, l.Font, width, TextFormatFlags.Default).Height + 2;
        }

        private Button MakeButton(string label, int x, int y, int w, Color back, Color fore, bool bold)
        {
            var b = new Button
            {
                // Looked up here, for every button: the labels went out in English in every language.
                Text = Localization.T(label),
                Location = new Point(x, y),
                Size = new Size(w, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = fore,
                Font = new Font("Segoe UI", 9f, bold ? FontStyle.Bold : FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private void Open(string target)
        {
            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not open '" + target + "': " + ex.Message);
                MessageBox.Show(this, Utils.Localization.F("Couldn't open it automatically:\n\n{0}", target),
                    "Tempo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
