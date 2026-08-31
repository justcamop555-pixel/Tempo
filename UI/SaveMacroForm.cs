using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;

namespace AutoClicker.UI
{
    /// <summary>
    /// Shown right after a recording stops: lets the user name the new macro,
    /// add an optional note, and pin it — with a summary of what was captured —
    /// instead of everything landing in the list under a timestamp name.
    /// Closing or pressing "Keep default" still saves (a recording is never lost),
    /// just under the automatic name.
    /// </summary>
    public sealed class SaveMacroForm : Form
    {
        private readonly TextBox _nameBox;
        private readonly TextBox _notesBox;
        private readonly CheckBox _pinCheck;

        public string MacroName => _nameBox.Text;
        public string Notes => _notesBox.Text;
        public bool Pin => _pinCheck.Checked;

        public SaveMacroForm(Theme theme, Macro recorded)
        {
            theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = Utils.Localization.T("Save Recording");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(440, 320);
            BackColor = theme.Background;
            // Title bar in the theme too, so the dialog matches the app.
            ThemeManager.ApplyWindowChrome(this, theme);
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            Controls.Add(new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(440, 4),
                BackColor = theme.Accent
            });

            var heading = new Label
            {
                Text = Utils.Localization.T("Save your recording"),
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = theme.Accent,
                AutoSize = false,
                Location = new Point(24, 20),
                Size = new Size(392, 30)
            };
            Controls.Add(heading);

            long ms = recorded != null ? recorded.EstimatedDurationMs : 0;
            string len = ms >= 60_000 ? $"{ms / 60_000.0:0.0} min"
                       : ms >= 1_000 ? $"{ms / 1_000.0:0.0} s"
                       : ms + " ms";
            var summary = new Label
            {
                Text = Utils.Localization.F("Captured {0} steps  \u00b7  \u2248{1}",
                    recorded != null ? recorded.StepCount : 0, len),
                ForeColor = theme.TextMuted,
                AutoSize = false,
                Location = new Point(24, 50),
                Size = new Size(392, 20)
            };
            Controls.Add(summary);

            Controls.Add(MakeCaption(theme, "NAME", 82));
            _nameBox = new TextBox
            {
                Location = new Point(24, 100),
                Size = new Size(392, 26),
                BackColor = theme.Surface2,
                ForeColor = theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Text = recorded != null ? recorded.Name : ""
            };
            Controls.Add(_nameBox);

            Controls.Add(MakeCaption(theme, "NOTES (OPTIONAL)", 134));
            _notesBox = new TextBox
            {
                Location = new Point(24, 152),
                Size = new Size(392, 56),
                Multiline = true,
                BackColor = theme.Surface2,
                ForeColor = theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_notesBox);

            _pinCheck = new CheckBox
            {
                Text = Utils.Localization.T("Pin to the top of the list (favourite)"),
                Location = new Point(24, 218),
                AutoSize = true,
                ForeColor = theme.Text
            };
            Controls.Add(_pinCheck);

            Button save = UiFactory.PrimaryButton("Save", 24, 258, 250, 40, theme);
            save.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_nameBox.Text))
                {
                    _nameBox.Focus();
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(save);
            AcceptButton = save;

            Button keep = UiFactory.Button("Keep default", 284, 258, 132, 40);
            keep.BackColor = theme.Surface2;
            keep.ForeColor = theme.TextMuted;
            keep.FlatAppearance.BorderColor = theme.Border;
            keep.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(keep);
            CancelButton = keep;

            Shown += (s, e) => { _nameBox.Focus(); _nameBox.SelectAll(); };
        }

        private static Label MakeCaption(Theme theme, string text, int y)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = theme.TextMuted,
                AutoSize = false,
                Location = new Point(24, y),
                Size = new Size(392, 16)
            };
        }
    }
}
