using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Shown when Tempo is launched while a copy is already running.
    ///
    /// It replaces a raw MessageBox — system grey, system title bar, the stock blue "i" —
    /// which was jarring in an app that draws its own chrome everywhere else, and was
    /// English-only in every language because a MessageBox has no translation path.
    ///
    /// It also AUTO-CLOSES. This is the most-seen dialog in Tempo: every double-click of
    /// a tray app that is already running lands here, so it is exactly the wrong place for
    /// a modal that waits for a click. By the time it appears the running window has
    /// already been brought to the front, so the message is a courtesy, not a question —
    /// it says its piece and gets out of the way.
    /// </summary>
    public sealed class AlreadyRunningForm : Form
    {
        private const int Margin = 22;
        private const int Width0 = 460;
        private const int TextWidth = Width0 - Margin * 2;

        private static string CountdownText(int secondsLeft)
        {
            string ok = Localization.T("OK");
            return secondsLeft > 0 ? ok + "  (" + secondsLeft + ")" : ok;
        }

        public AlreadyRunningForm(Theme theme, string version)
        {
            var t = theme ?? Theme.ForKind(ThemeKind.Dark);

            Text = "Tempo" + (string.IsNullOrEmpty(version) ? "" : " " + version);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterScreen;   // no owner window exists yet
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;            // the window it is talking about was just raised
            BackColor = t.Background;
            ThemeManager.ApplyWindowChrome(this, t);
            ForeColor = t.Text;
            Font = UiFactory.BodyFont;

            Controls.Add(new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(Width0, 4),
                BackColor = t.Accent
            });

            int y = 20;

            // Tempo's own logo, so the dialog is recognisably Tempo rather than a
            // Windows system box wearing its name.
            try
            {
                var logo = new PictureBox
                {
                    Image = AppIcon.GetBitmap(40),
                    SizeMode = PictureBoxSizeMode.StretchImage,
                    Location = new Point(Margin, y + 2),
                    Size = new Size(40, 40),
                    BackColor = Color.Transparent
                };
                Controls.Add(logo);
            }
            catch { }

            int textLeft = Margin + 40 + 14;
            int textW = Width0 - textLeft - Margin;

            var title = new Label
            {
                Text = Localization.T("Tempo is already running"),
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = t.Text,
                AutoSize = false,
                Location = new Point(textLeft, y),
                Size = new Size(textW, 28)
            };
            Controls.Add(title);

            var sub = new Label
            {
                Text = string.IsNullOrEmpty(version) ? "" : "v" + version,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = t.TextMuted,
                AutoSize = false,
                Location = new Point(textLeft, y + 26),
                Size = new Size(textW, 18)
            };
            Controls.Add(sub);

            y += 54;
            y = AddParagraph(Localization.T("Its window has been brought to the front."), t.Text, y, t);
            y = AddParagraph(
                Localization.T("If you can't see it, look for the Tempo icon in the system tray — bottom-right, near the clock. You may need to click the ^ arrow first."),
                t.TextMuted, y, t);

            var ok = new Button
            {
                Text = CountdownText(0),
                Width = TextWidth,
                Height = 34,
                Left = Margin,
                Top = y + 8,
                FlatStyle = FlatStyle.Flat,
                BackColor = t.Accent,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.75f)
            };
            ok.FlatAppearance.BorderColor = t.Accent;
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = ok;

            ClientSize = new Size(Width0, ok.Bottom + 18);

            // Six seconds is plenty to read two short lines, and short enough that a user
            // who double-clicked by accident is not left clicking a box away.
            var autoClose = new Timer { Interval = 1000 };
            int secondsLeft = 6;
            ok.Text = CountdownText(secondsLeft);
            autoClose.Tick += (s, e) =>
            {
                secondsLeft--;
                if (secondsLeft <= 0)
                {
                    autoClose.Stop();
                    if (!IsDisposed && Visible)
                    {
                        DialogResult = DialogResult.OK;
                        Close();
                    }
                }
                else if (!IsDisposed)
                {
                    ok.Text = CountdownText(secondsLeft);
                }
            };
            autoClose.Start();
            FormClosed += (s, e) => { autoClose.Stop(); autoClose.Dispose(); };
        }

        /// <summary>Adds a wrapped paragraph measured to its text. Returns the y below it.</summary>
        private int AddParagraph(string text, Color colour, int y, Theme t)
        {
            if (string.IsNullOrWhiteSpace(text)) { return y; }

            var l = new Label
            {
                Text = text,
                Left = Margin,
                Top = y,
                Width = TextWidth,
                AutoSize = false,
                ForeColor = colour,
                Font = UiFactory.BodyFont
            };
            using (var g = CreateGraphics())
            {
                l.Height = Math.Max(18, TextRenderer.MeasureText(
                    g, text, l.Font, new Size(TextWidth, 0), TextFormatFlags.WordBreak).Height + 2);
            }
            Controls.Add(l);
            return y + l.Height + 10;
        }
    }
}
