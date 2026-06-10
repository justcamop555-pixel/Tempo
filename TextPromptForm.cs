using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A minimal modal prompt for a single line of text (used for renaming macros
    /// and profiles). WinForms has no built-in input box, so this fills the gap.
    /// </summary>
    public sealed class TextPromptForm : Form
    {
        private readonly TextBox _input;

        public string Value => _input.Text;

        public TextPromptForm(Theme theme, string title, string prompt, string initial)
        {
            AutoScaleMode = AutoScaleMode.Font;
            theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            Text = title ?? "Input";
            Size = new Size(380, 170);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            var label = UiFactory.Label(prompt ?? "Enter a value:", 18, 18);
            label.MaximumSize = new Size(340, 0);
            label.AutoSize = true;

            _input = UiFactory.Text(18, 52, 330, initial ?? string.Empty);
            _input.SelectAll();

            var ok = UiFactory.PrimaryButton("OK", 168, 92, 80, 30, theme);
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

            var cancel = UiFactory.Button("Cancel", 258, 92, 90, 30);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(label);
            Controls.Add(_input);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            ThemeManager.Apply(this, theme);
        }

        /// <summary>Convenience helper that shows the prompt and returns the text or null if cancelled.</summary>
        public static string Show(IWin32Window owner, Theme theme, string title, string prompt, string initial)
        {
            using (var form = new TextPromptForm(theme, title, prompt, initial))
            {
                return form.ShowDialog(owner) == DialogResult.OK ? form.Value : null;
            }
        }
    }
}
