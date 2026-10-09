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
            // Built in 100% units; scaled with the screen at the end (UiFactory.ScaleToScreen).
            theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            // Translated here too: the title used to be shown exactly as passed, so the rename,
            // delay and notes prompts had an English title bar in every language. A caller's
            // already-translated title comes back from T() unchanged.
            Text = Utils.Localization.T(string.IsNullOrEmpty(title) ? "Input" : title);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            // The prompt wraps — Scale delays takes two lines, the notes prompt of a macro with a long
            // name three or more — and the box sat at a fixed y = 52, so a third line ran underneath
            // it. The prompt is sized to its words in 100% units (UiFactory.Measure96) and the box and
            // the buttons stack below it. No mnemonic: the notes prompt names the macro, and "R&D"
            // lost its "&".
            var label = UiFactory.Label(prompt ?? "Enter a value:", 18, 18);
            label.UseMnemonic = false;
            label.AutoSize = false;
            label.Width = 330;
            label.Height = Math.Max(18, UiFactory.Measure96(label.Text, label.Font, label.Width, TextFormatFlags.NoPrefix).Height + 2);

            int inputTop = Math.Max(52, label.Bottom + 12);
            _input = UiFactory.Text(18, inputTop, 330, initial ?? string.Empty);
            _input.SelectAll();

            int buttonsTop = inputTop + 40;
            var ok = UiFactory.PrimaryButton("OK", 168, buttonsTop, 80, 30, theme);
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

            var cancel = UiFactory.Button("Cancel", 258, buttonsTop, 90, 30);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            // The client area (was a 380×170 window with a one-line prompt): see UiFactory.ScaleToScreen.
            ClientSize = new Size(364, buttonsTop + 30 + 9);

            Controls.Add(label);
            Controls.Add(_input);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            ThemeManager.Apply(this, theme);
            UiFactory.ScaleToScreen(this);
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
