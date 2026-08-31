using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A clear, themed "update available" dialog with cleaned-up release notes and
    /// explicit choices, replacing the cramped multi-line message box.
    /// </summary>
    public sealed class UpdatePromptForm : Form
    {
        public enum UpdateChoice
        {
            Later,
            UpdateNow,
            OpenPage,
            Skip
        }

        public UpdateChoice Choice { get; private set; } = UpdateChoice.Later;

        private readonly Theme _theme;

        /// <summary>
        /// Themes this dialog's scroll bars, in OnShown so the child controls have real
        /// handles (SetWindowTheme needs one; a Form's HandleCreated is too early). The
        /// scroll-bar theming only ever walked the MAIN form — so every dialog kept the
        /// light Explorer scroll bar whatever theme was chosen.
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                bool dark = (_theme ?? Theme.ForKind(Models.ThemeKind.Dark))
                    .Background.GetBrightness() < 0.5f;
                NativeChrome.ApplyAllScrollbarThemes(this, dark);
            }
            catch { }
        }

        public UpdatePromptForm(Theme theme, string currentVersion, string latestVersion,
            string notes, bool canOneClick, DateTime? releaseDate = null)
        {
            _theme = theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = Utils.Localization.T("Update available");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(484, 408);
            BackColor = theme.Background;
            // Title bar in the theme too, so the dialog matches the app.
            ThemeManager.ApplyWindowChrome(this, theme);
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            // Slim accent strip along the very top for a bit of polish.
            var accentStrip = new AccentStrip(theme) { Location = new Point(0, 0), Size = new Size(484, 4) };
            Controls.Add(accentStrip);

            var heading = new Label
            {
                Text = Utils.Localization.T("A new version of Tempo is available"),
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = theme.Accent,
                AutoSize = false,
                Location = new Point(24, 22),
                Size = new Size(436, 30)
            };
            Controls.Add(heading);

            string versionsText = $"Installed  {currentVersion}      \u2192      Latest  {latestVersion}";
            if (releaseDate.HasValue)
            {
                versionsText += "      \u00b7      Released " + releaseDate.Value.ToLocalTime().ToString("d MMM yyyy");
            }
            var versions = new Label
            {
                Text = versionsText,
                ForeColor = theme.TextMuted,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                AutoSize = false,
                Location = new Point(24, 54),
                Size = new Size(436, 22)
            };
            Controls.Add(versions);

            var notesHeader = new Label
            {
                Text = Utils.Localization.T("WHAT'S NEW"),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = theme.TextMuted,
                AutoSize = true,
                Location = new Point(24, 86)
            };
            Controls.Add(notesHeader);

            // Rounded surface behind the notes for a card-like look.
            var notesCard = new RoundedPanel(theme)
            {
                Location = new Point(24, 106),
                Size = new Size(436, 196)
            };
            Controls.Add(notesCard);

            var notesBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = theme.Surface,
                ForeColor = theme.Text,
                BorderStyle = BorderStyle.None,
                Location = new Point(12, 11),
                Size = new Size(412, 174),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                TabStop = false,
                Text = CleanNotes(notes)
            };
            notesBox.Select(0, 0);
            notesCard.Controls.Add(notesBox);

            int by = 318;

            if (canOneClick)
            {
                var update = MakeButton("Update now", 24, by, 132, theme.Accent, Color.White, true, theme);
                update.Click += (s, e) => Pick(UpdateChoice.UpdateNow);
                Controls.Add(update);
                AcceptButton = update;
            }

            int pageX = canOneClick ? 164 : 24;
            var page = MakeButton(canOneClick ? "Download page" : "Open download page",
                pageX, by, canOneClick ? 132 : 196, theme.Surface2, theme.Text, false, theme);
            page.Click += (s, e) => Pick(UpdateChoice.OpenPage);
            Controls.Add(page);
            if (!canOneClick)
            {
                AcceptButton = page;
            }

            var later = MakeButton("Later", 380, by, 80, theme.Surface2, theme.Text, false, theme);
            later.Click += (s, e) => Pick(UpdateChoice.Later);
            Controls.Add(later);
            CancelButton = later;

            var skip = MakeButton("Skip this version", 24, by + 44, 156, theme.Background, theme.TextMuted, false, theme);
            skip.FlatAppearance.BorderSize = 0;
            skip.Click += (s, e) => Pick(UpdateChoice.Skip);
            Controls.Add(skip);
        }

        /// <summary>
        /// Turns the markdown-ish release notes into clean plain text for display:
        /// drops the repeated title line and the unsigned-publisher footer, removes
        /// "#"/"*" markers, and turns "- " into a bullet.
        /// </summary>
        private static string CleanNotes(string notes)
        {
            if (string.IsNullOrWhiteSpace(notes))
            {
                return "(No release notes were provided.)";
            }

            var sb = new StringBuilder();
            foreach (string raw in notes.Replace("\r\n", "\n").Split('\n'))
            {
                string t = raw.Trim();

                if (t.StartsWith("## Tempo", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // Redundant with the heading + version line above.
                }
                if (t.IndexOf("Unknown publisher", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue; // Footer note isn't useful inside this dialog.
                }

                while (t.StartsWith("#"))
                {
                    t = t.Substring(1).Trim();
                }
                if (t.StartsWith("- ") || t.StartsWith("* "))
                {
                    t = "   \u2022 " + t.Substring(2).Trim();
                }
                t = t.Replace("**", "").Replace("__", "");

                sb.AppendLine(t);
            }

            return sb.ToString().Trim('\r', '\n', ' ').Replace("\n", "\r\n");
        }

        private void Pick(UpdateChoice choice)
        {
            Choice = choice;
            DialogResult = DialogResult.OK;
            Close();
        }

        private Button MakeButton(string text, int x, int y, int w, Color back, Color fore,
            bool bold, Theme theme)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = fore,
                Font = new Font("Segoe UI", 9f, bold ? FontStyle.Bold : FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = theme.Border;
            b.FlatAppearance.BorderSize = bold ? 0 : 1;
            b.FlatAppearance.MouseOverBackColor = bold ? theme.AccentHover : theme.Surface;
            return b;
        }

        /// <summary>A thin horizontal accent gradient bar.</summary>
        private sealed class AccentStrip : Panel
        {
            private readonly Theme _t;
            public AccentStrip(Theme t) { _t = t; DoubleBuffered = true; }
            protected override void OnPaint(PaintEventArgs e)
            {
                using (var b = new LinearGradientBrush(ClientRectangle,
                           _t.Accent, _t.AccentHover, LinearGradientMode.Horizontal))
                {
                    e.Graphics.FillRectangle(b, ClientRectangle);
                }
            }
        }

        /// <summary>A rounded surface panel that hosts the notes text box.</summary>
        private sealed class RoundedPanel : Panel
        {
            private readonly Theme _t;
            public RoundedPanel(Theme t)
            {
                _t = t;
                DoubleBuffered = true;
                BackColor = t.Background;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var path = Rounded(r, 10))
                using (var fill = new SolidBrush(_t.Surface))
                using (var pen = new Pen(_t.Border))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(pen, path);
                }
            }
            private static GraphicsPath Rounded(Rectangle r, int radius)
            {
                var p = new GraphicsPath();
                int d = radius * 2;
                p.AddArc(r.X, r.Y, d, d, 180, 90);
                p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                p.CloseFigure();
                return p;
            }
        }
    }
}
