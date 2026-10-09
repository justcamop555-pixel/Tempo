using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;

namespace AutoClicker.UI
{
    /// <summary>
    /// Shown once, the first time captions start with speaker labels enabled: an
    /// honest heads-up that the "Speaker 1/2" labels are AI guesses from voice
    /// pitch and pauses — often wrong with similar voices, music or cross-talk —
    /// so nobody mistakes them for reliable identification. Auto-closes so it
    /// never blocks someone who just wants captions, and offers a one-click way
    /// to turn the labels off.
    /// </summary>
    public sealed class SpeakerNoticeForm : Form
    {
        /// <summary>DialogResult.No means "turn speaker labels off".</summary>
        public SpeakerNoticeForm(Theme theme)
        {
            theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            // Built in 100% units; scaled with the screen at the end (UiFactory.ScaleToScreen).
            Text = Utils.Localization.T("About speaker labels");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(470, 252);
            BackColor = theme.Background;
            // Title bar in the theme too, so the dialog matches the app.
            ThemeManager.ApplyWindowChrome(this, theme);
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            Controls.Add(new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(470, 4),
                BackColor = theme.Accent
            });

            var heading = new Label
            {
                Text = Utils.Localization.T("Speaker labels are AI guesses"),
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = theme.Accent,
                AutoSize = false,
                Location = new Point(24, 20),
                Size = new Size(422, 30)
            };
            Controls.Add(heading);

            var body = new Label
            {
                Text = Utils.Localization.T(
                       "The “Speaker 1 / Speaker 2” labels come from on-device AI that "
                       + "listens to voice pitch and pauses. It makes plenty of mistakes:\n\n"
                       + "•  similar voices can get the same number\n"
                       + "•  one person can be split into two speakers\n"
                       + "•  music and cross-talk confuse it\n\n"
                       + "Treat the numbers as a reading aid, never as identification. You can "
                       + "turn them off any time in Settings → Live Captions."),
                AutoSize = false,
                Location = new Point(24, 54),
                Size = new Size(422, 144),
                ForeColor = theme.Text
            };
            Controls.Add(body);

            // The heading and the body each as tall as their words, one under the other, and the
            // buttons under both: the Spanish and French headings are two lines and the French body
            // a line longer than its box, which drew only the first line of each.
            // (In 100% units: the window is scaled after it is built — UiFactory.Measure96.)
            heading.Height = Math.Max(30, UiFactory.Measure96(heading.Text, heading.Font, 422, TextFormatFlags.Default).Height + 2);
            body.Top = heading.Bottom + 4;
            body.Height = Math.Max(144, UiFactory.Measure96(body.Text, Font, 422, TextFormatFlags.Default).Height + 2);
            int by = body.Bottom + 6;

            Button ok = UiFactory.PrimaryButton("Got it", 24, by, 280, 36, theme);
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = ok;

            // As wide as its words ("Beschriftungen ausschalten" needs 160 px of the 134); "Got it" gives
            // up the difference.
            Button off = UiFactory.Button("Turn labels off", 312, by, 134, 36);
            UiFactory.FitToText96(off, 134);
            off.Left = 446 - off.Width;
            ok.Width = off.Left - 8 - ok.Left;
            off.Click += (s, e) => { DialogResult = DialogResult.No; Close(); };
            Controls.Add(off);
            ClientSize = new Size(470, ok.Bottom + 12);

            // Auto-close like the welcome note, so a hotkey-triggered caption start is
            // never blocked for long. Countdown on the button keeps it predictable.
            // The caption is rebuilt through T() each tick. Assigning a raw English
            // literal here — which is what this did — silently undid the translation
            // UiFactory had just applied to the button, one second after it opened.
            // Same defect as OfficialSourceForm; see the note there.
            var autoClose = new Timer { Interval = 1000 };
            int secondsLeft = 12;
            ok.Text = Utils.Localization.T("Got it") + "  (" + secondsLeft + ")";
            autoClose.Tick += (s, e) =>
            {
                secondsLeft--;
                if (secondsLeft <= 0)
                {
                    autoClose.Stop();
                    // A tick can still be queued after the user dismissed the dialog.
                    if (!IsDisposed && Visible)
                    {
                        DialogResult = DialogResult.OK;
                        Close();
                    }
                }
                else if (!IsDisposed)
                {
                    ok.Text = Utils.Localization.T("Got it") + "  (" + secondsLeft + ")";
                }
            };
            autoClose.Start();
            FormClosed += (s, e) => { autoClose.Stop(); autoClose.Dispose(); };
            UiFactory.ScaleToScreen(this);
        }
    }
}
