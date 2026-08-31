using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Shown once, on first run: tells the user where Tempo is officially
    /// published and how to verify a download, because auto-clickers are a
    /// favourite category for malware-laden clones on shady download sites.
    /// Friendly, one time only, with direct buttons to the official pages.
    ///
    /// TRANSLATION, and why this dialog used to come out half-English:
    ///
    ///  • Its heading and body were raw `new Label { Text = "..." }`. Only UiFactory
    ///    runs text through Localization.T, so anything built by hand here was never
    ///    translated — which is why the buttons appeared in Spanish while the prose
    ///    above them stayed in English.
    ///  • The body also had the two URLs CONCATENATED into the middle of the sentence.
    ///    That alone made it untranslatable in principle: the finished string contains
    ///    a runtime value, so it can never equal a dictionary key. The prose and the
    ///    URLs are separate controls now, so the sentences are stable keys and the
    ///    addresses are left exactly as they are — nobody wants a translated URL.
    ///  • The countdown then overwrote the button every second with a fresh English
    ///    literal, undoing the one translation that HAD happened. See CountdownText.
    ///
    /// LAYOUT: measured, not hard-coded. The old fixed 470x312 with a 158px body and
    /// 200/140px buttons fitted English and nothing else — German and French run a
    /// third longer and clipped. Everything now sizes to its own translated text.
    /// </summary>
    public sealed class OfficialSourceForm : Form
    {
        public const string WebsiteUrl = "https://justcamop555-pixel.github.io/Tempo/";
        public static string GitHubUrl => "https://github.com/" + Utils.UpdateChecker.Repository;

        private const int Margin = 24;
        private const int Width0 = 470;
        private const int TextWidth = Width0 - Margin * 2;

        /// <summary>
        /// The primary button's caption, with the seconds remaining appended.
        ///
        /// Built through T() every time it is set. The countdown used to assign a raw
        /// "Got it  (n)" literal once a second, so a translated button reverted to
        /// English within a second of the dialog opening and stayed that way — the
        /// exact "Abrir sitio web … Got it (2)" mismatch that made this dialog look
        /// half-finished in every language but English.
        /// </summary>
        private static string CountdownText(int secondsLeft)
        {
            string got = Localization.T("Got it");
            return secondsLeft > 0 ? got + "  (" + secondsLeft + ")" : got;
        }

        public OfficialSourceForm(Theme theme)
        {
            theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = Localization.T("Welcome to Tempo");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = theme.Background;
            // Title bar in the theme too, so the dialog matches the app.
            ThemeManager.ApplyWindowChrome(this, theme);
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            Controls.Add(new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(Width0, 4),
                BackColor = theme.Accent
            });

            int y = 20;
            y = AddText(Localization.T("Quick safety note"), theme.Accent,
                        new Font("Segoe UI", 14f, FontStyle.Bold), y) + 10;

            y = AddText(Localization.T("Tempo is free and only officially published in two places:"),
                        theme.Text, UiFactory.BodyFont, y) + 6;

            // The addresses themselves: never translated, and set apart so they read as
            // data rather than prose. Selectable, so someone can copy one to check it
            // against the address bar of wherever they actually downloaded this.
            y = AddUrl(GitHubUrl, theme, y);
            y = AddUrl(WebsiteUrl, theme, y) + 10;

            y = AddText(Localization.T("If you downloaded this copy anywhere else, it may have been modified by someone other than the developer. You can verify your copy against the SHA-256 checksum published with every official release."),
                        theme.Text, UiFactory.BodyFont, y) + 16;

            // ── Buttons, sized to their OWN translated captions ──────────────────
            Button gh = UiFactory.Button(Localization.T("Open GitHub page"), Margin, y, 200, 34);
            Button site = UiFactory.Button(Localization.T("Open website"), 0, y, 140, 34);
            FitToText(gh, 200);
            FitToText(site, 140);
            site.Left = gh.Right + 10;
            Controls.Add(gh);
            Controls.Add(site);
            y += 34 + 12;

            Button ok = UiFactory.PrimaryButton(CountdownText(0), Margin, y, TextWidth, 38, theme);
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = ok;
            y += 38 + 20;

            ClientSize = new Size(Width0, y);

            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

            // Auto-close after 10 seconds with a visible countdown on the button, so
            // the note never blocks someone who just wants the app. Clicking either link
            // stops the countdown — the user is clearly reading, don't yank it away.
            var autoClose = new Timer { Interval = 1000 };
            int secondsLeft = 10;
            ok.Text = CountdownText(secondsLeft);
            autoClose.Tick += (s, e) =>
            {
                secondsLeft--;
                if (secondsLeft <= 0)
                {
                    autoClose.Stop();
                    // Guard the close: a tick can still be queued after the user has
                    // already dismissed the dialog, and closing an closed form throws.
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

            // Both link handlers do the same three things, so they ARE the same handler.
            // They used to be two copies that each re-assigned a raw English "Got it".
            EventHandler openLink(string url) => (s, e) =>
            {
                autoClose.Stop();
                secondsLeft = 0;
                ok.Text = CountdownText(0);
                Open(url);
            };
            gh.Click += openLink(GitHubUrl);
            site.Click += openLink(WebsiteUrl);
        }

        /// <summary>Adds a wrapped label sized to its text. Returns the y below it.</summary>
        private int AddText(string text, Color colour, Font font, int y)
        {
            int h;
            using (var g = CreateGraphics())
            {
                h = TextRenderer.MeasureText(g, text, font, new Size(TextWidth, 0),
                                             TextFormatFlags.WordBreak).Height + 2;
            }
            Controls.Add(new Label
            {
                Text = text,
                Font = font,
                ForeColor = colour,
                AutoSize = false,
                Location = new Point(Margin, y),
                Size = new Size(TextWidth, h)
            });
            return y + h;
        }

        /// <summary>
        /// One official address, selectable so it can be copied and compared. A
        /// read-only borderless TextBox rather than a Label purely for that.
        /// </summary>
        private int AddUrl(string url, Theme theme, int y)
        {
            var box = new TextBox
            {
                Text = "•  " + url,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = theme.Background,
                ForeColor = theme.Accent,
                Font = UiFactory.BodyFont,
                Location = new Point(Margin, y),
                Width = TextWidth,
                TabStop = false
            };
            Controls.Add(box);
            return y + box.Height + 2;
        }

        /// <summary>
        /// Grows a button so its translated caption fits. Never shrinks below the
        /// design width, so English keeps the layout it was drawn for while longer
        /// languages get the room they need instead of an ellipsis.
        /// </summary>
        private static void FitToText(Button b, int designWidth)
        {
            try
            {
                Size need = TextRenderer.MeasureText(b.Text, b.Font);
                b.Width = Math.Max(designWidth, need.Width + 28);
            }
            catch { }
        }

        private static void Open(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("Could not open " + url + ": " + ex.Message);
            }
        }
    }
}
