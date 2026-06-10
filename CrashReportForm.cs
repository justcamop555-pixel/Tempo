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
            AutoScaleMode = AutoScaleMode.Font;
            _ex = ex;
            _report = report ?? string.Empty;
            _reportPath = reportPath;
            _fatal = fatal;

            BuildUi();
        }

        private void BuildUi()
        {
            Color bg = Color.FromArgb(24, 26, 32);
            Color panel = Color.FromArgb(34, 37, 46);
            Color text = Color.FromArgb(232, 234, 240);
            Color muted = Color.FromArgb(150, 156, 170);
            Color accent = Color.FromArgb(124, 92, 255);

            Text = "Tempo — something went wrong";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(540, 420);
            BackColor = bg;
            ForeColor = text;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            TopMost = true;

            var heading = new Label
            {
                Text = _fatal ? "Tempo has to close" : "Tempo hit an unexpected error",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = text,
                AutoSize = false,
                Location = new Point(20, 18),
                Size = new Size(500, 30)
            };
            Controls.Add(heading);

            var sub = new Label
            {
                Text = _fatal
                    ? "Sorry about that. The details below have been saved. Reporting them helps get it fixed."
                    : "Sorry about that. You can keep using Tempo. Reporting the details helps get it fixed.",
                ForeColor = muted,
                AutoSize = false,
                Location = new Point(20, 50),
                Size = new Size(500, 20)
            };
            Controls.Add(sub);

            var privacy = new Label
            {
                Text = "Privacy: this includes only Tempo's version, your Windows version and the " +
                       "technical error — never your clicks, settings or files, and your Windows " +
                       "account name is removed. Nothing is sent until you submit. You can edit the text below first.",
                ForeColor = muted,
                Font = new Font("Segoe UI", 8f, FontStyle.Italic),
                AutoSize = false,
                Location = new Point(20, 70),
                Size = new Size(500, 44)
            };
            Controls.Add(privacy);

            var box = new TextBox
            {
                Multiline = true,
                ReadOnly = false,
                ScrollBars = ScrollBars.Vertical,
                BackColor = panel,
                ForeColor = text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5f, FontStyle.Regular),
                Location = new Point(20, 118),
                Size = new Size(500, 204),
                Text = _report.Replace("\n", "\r\n")
            };
            Controls.Add(box);

            int by = 334;

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
                    Text = "Quit Tempo",
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

        private Button MakeButton(string label, int x, int y, int w, Color back, Color fore, bool bold)
        {
            var b = new Button
            {
                Text = label,
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
                MessageBox.Show(this, "Couldn't open it automatically:\n\n" + target,
                    "Tempo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
