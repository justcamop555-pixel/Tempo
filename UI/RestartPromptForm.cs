using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Asks whether to restart Tempo to finish applying a setting.
    ///
    /// Replaces two raw MessageBox.Show calls (the language change and the CPU/GPU speech
    /// engine change). Those arrived as bare Windows dialogs — system grey, system title
    /// bar, a stock "?" bubble — in the middle of an app that draws its own chrome for
    /// everything else, so the one moment Tempo asks to close itself was also the one
    /// moment it stopped looking like Tempo.
    ///
    /// It also answers the two questions the old prompt left hanging:
    ///   • what happens if I say no?  ("Later" applies it the next time Tempo opens)
    ///   • what am I about to lose?   (a run or a macro in progress is named explicitly)
    /// </summary>
    public sealed class RestartPromptForm : Form
    {
        /// <summary>True when the user chose to restart now.</summary>
        public bool RestartNow { get; private set; }

        /// <param name="headline">What changed, e.g. "Language changed".</param>
        /// <param name="saved">The reassurance that the setting is already stored.</param>
        /// <param name="why">Why a restart is needed at all.</param>
        /// <param name="busyWarning">
        /// Non-null when something would be interrupted — the clicker running, a macro
        /// playing. Shown in the danger colour, because the restart path deliberately sets
        /// _reallyClosing and therefore SKIPS the normal "confirm before exit while
        /// running" guard: this dialog is the only warning the user gets.
        /// </param>
        public RestartPromptForm(Theme theme, string headline, string saved, string why, string busyWarning)
        {
            var t = theme ?? Theme.ForKind(ThemeKind.Dark);
            bool busy = !string.IsNullOrEmpty(busyWarning);

            Text = Localization.T("Restart Tempo?");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = t.Background;
            ThemeManager.ApplyWindowChrome(this, t);
            ForeColor = t.Text;

            int y = 22;

            var title = new Label
            {
                Text = headline,
                Left = 24,
                Top = y,
                Width = 452,
                Height = 30,
                Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
                ForeColor = t.Text
            };
            Controls.Add(title);
            y += 36;

            y = AddParagraph(saved, t.TextMuted, y, t);
            y = AddParagraph(why, t.Text, y, t);

            if (busy)
            {
                // Its own line, in the danger colour, because it is the only thing here
                // that costs the user something they cannot get back.
                y = AddParagraph("⚠ " + busyWarning, t.Danger, y, t);
            }

            y = AddParagraph(
                Localization.T("Choose Later and it will apply by itself the next time you open Tempo."),
                t.TextMuted, y, t);

            var restart = MakeButton(Localization.T("Restart now"), t, primary: true);
            var later = MakeButton(Localization.T("Later"), t, primary: false);

            ClientSize = new Size(500, y + 14 + restart.Height + 20);

            restart.Left = ClientSize.Width - restart.Width - 24;
            restart.Top = ClientSize.Height - restart.Height - 20;
            restart.Click += (s, e) => { RestartNow = true; DialogResult = DialogResult.OK; };
            Controls.Add(restart);

            later.Left = restart.Left - later.Width - 10;
            later.Top = restart.Top;
            later.Click += (s, e) => { RestartNow = false; DialogResult = DialogResult.Cancel; };
            Controls.Add(later);

            // "Later" is the safe default under Escape — and when a run is in progress it
            // is the ACCEPT button too, so leaning on Enter can't end the run by accident.
            CancelButton = later;
            AcceptButton = busy ? later : restart;
        }

        private int AddParagraph(string text, Color colour, int y, Theme t)
        {
            if (string.IsNullOrWhiteSpace(text)) { return y; }

            var l = new Label
            {
                Text = text,
                Left = 24,
                Top = y,
                Width = 452,
                AutoSize = false,
                ForeColor = colour,
                Font = new Font("Segoe UI", 9.75f)
            };
            // Measure so a long sentence in any language gets the height it needs rather
            // than being clipped at a hard-coded two lines.
            using (var g = CreateGraphics())
            {
                Size need = TextRenderer.MeasureText(g, text, l.Font,
                    new Size(452, 0), TextFormatFlags.WordBreak);
                l.Height = Math.Max(20, need.Height + 2);
            }
            Controls.Add(l);
            return y + l.Height + 12;
        }

        private static Button MakeButton(string text, Theme t, bool primary)
        {
            var b = new Button
            {
                Text = text,
                Width = primary ? 132 : 96,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? t.Accent : t.Surface,
                ForeColor = primary ? Color.White : t.Text,
                Font = new Font("Segoe UI", 9.75f)
            };
            b.FlatAppearance.BorderColor = primary ? t.Accent : t.Border;
            return b;
        }
    }
}
