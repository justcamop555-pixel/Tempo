using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// The themed "update available" dialog: which version, what's new, and what saying yes does.
    ///
    /// Rebuilt because the first version was fixed-size and English-only in the places that
    /// mattered: all four buttons ("Update now", "Download page", "Later", "Skip this version") and
    /// the version line were raw English in every language, the buttons were fixed widths that
    /// longer translations would have overflowed, the notes were Markdown shown as plain text, and
    /// nothing said that Tempo restarts or what happens to your data. Everything here is measured
    /// and translated, and the dialog grows to fit whatever the language needs.
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

        /// <summary>What the dialog is told about the update and this copy of Tempo.</summary>
        public sealed class Details
        {
            public string CurrentVersion;
            public string LatestVersion;
            public string Notes;
            public DateTime? ReleaseDate;
            /// <summary>Bytes the one-click update downloads; 0 when unknown.</summary>
            public long DownloadSize;
            public bool CanOneClick;
            /// <summary>Why this copy cannot update itself — shown instead of the restart note.</summary>
            public string OneClickBlockedReason;
            /// <summary>What updating would interrupt right now (a run, a macro), or null.</summary>
            public string BusyWarning;
        }

        public UpdateChoice Choice { get; private set; } = UpdateChoice.Later;

        private readonly Theme _theme;
        private readonly Button _primary;

        // Layout, in 96-DPI units — AutoScaleMode.Dpi scales all of it once the window exists.
        private const int ContentWidth = 500;
        private const int Pad = 24;
        private const int NotesHeight = 262;

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
            // The decision, not the notes, has the focus: Enter answers the dialog, and the
            // notes box never shows a caret nobody asked for.
            try { _primary?.Focus(); } catch { }
        }

        public UpdatePromptForm(Theme theme, Details details)
        {
            _theme = theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);
            details = details ?? new Details();

            // Built in 100% units; scaled with the screen at the end (UiFactory.ScaleToScreen).
            Text = Localization.T("Update available");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = theme.Background;
            // Title bar in the theme too, so the dialog matches the app.
            ThemeManager.ApplyWindowChrome(this, theme);
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            int width = ContentWidth + Pad * 2;

            // Slim accent strip along the very top for a bit of polish.
            Controls.Add(new AccentStrip(theme) { Location = new Point(0, 0), Size = new Size(width, 4) });

            int y = 20;
            var headingFont = new Font("Segoe UI", 14f, FontStyle.Bold);
            string headingText = string.IsNullOrWhiteSpace(details.LatestVersion)
                ? Localization.T("A new version of Tempo is available")
                : Localization.F("Tempo {0} is available", details.LatestVersion);
            y = AddText(headingText, headingFont, theme.AccentText, y, 6);

            // "You have 1.0.322 · released 20 Sep 2026 · 102.8 MB download" — the facts that
            // decide "now or later", on one line when they fit.
            var facts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(details.CurrentVersion))
            {
                facts.Add(Localization.F("You have {0}", details.CurrentVersion));
            }
            if (details.ReleaseDate.HasValue)
            {
                facts.Add(Localization.F("released {0}",
                    details.ReleaseDate.Value.ToLocalTime().ToString("d MMM yyyy", Localization.DateCulture)));
            }
            if (details.CanOneClick && details.DownloadSize > 0)
            {
                facts.Add(Localization.F("{0} download", FormatBytes(details.DownloadSize)));
            }
            if (facts.Count > 0)
            {
                y = AddText(string.Join("   ·   ", facts), new Font("Segoe UI", 9.5f), theme.TextMuted, y, 14);
            }

            var notesHeader = new Label
            {
                Text = Localization.T("WHAT'S NEW"),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = theme.TextMuted,
                AutoSize = true,
                Location = new Point(Pad, y)
            };
            Controls.Add(notesHeader);
            y += 20;

            // Rounded surface behind the notes for a card-like look.
            var notesCard = new RoundedPanel(theme)
            {
                Location = new Point(Pad, y),
                Size = new Size(ContentWidth, NotesHeight)
            };
            Controls.Add(notesCard);
            notesCard.Controls.Add(BuildNotesView(details.Notes, details.LatestVersion, theme,
                new Rectangle(12, 10, ContentWidth - 18, NotesHeight - 20)));
            y += NotesHeight + 14;

            // What pressing the button does — the question the old dialog left open.
            if (details.CanOneClick)
            {
                y = AddText(Localization.T("Tempo restarts to finish updating. Your settings, profiles and macros stay as they are, and a restore point from before the update is kept."),
                            new Font("Segoe UI", 9f), theme.TextMuted, y, 8);
            }
            else if (!string.IsNullOrWhiteSpace(details.OneClickBlockedReason))
            {
                y = AddText(details.OneClickBlockedReason, new Font("Segoe UI", 9f), theme.TextMuted, y, 8);
            }

            bool busy = details.CanOneClick && !string.IsNullOrWhiteSpace(details.BusyWarning);
            if (busy)
            {
                // Its own line, in the danger colour: the one thing here that costs something.
                y = AddText("⚠ " + details.BusyWarning, new Font("Segoe UI", 9f, FontStyle.Bold), theme.DangerText, y, 8);
            }

            y += 8;

            // ── Buttons: [Skip this version]            [Later] [page] [Update now] ──
            Button primary;
            Button page = null;
            if (details.CanOneClick)
            {
                primary = MakeButton(Localization.T("Update now"), true, theme);
                primary.Click += (s, e) => Pick(UpdateChoice.UpdateNow);
                page = MakeButton(Localization.T("View on GitHub"), false, theme);
                page.Click += (s, e) => Pick(UpdateChoice.OpenPage);
            }
            else
            {
                primary = MakeButton(Localization.T("Open download page"), true, theme);
                primary.Click += (s, e) => Pick(UpdateChoice.OpenPage);
            }
            _primary = primary;

            var later = MakeButton(Localization.T("Later"), false, theme);
            later.Click += (s, e) => Pick(UpdateChoice.Later);

            var skip = MakeButton(Localization.T("Skip this version"), false, theme);
            skip.FlatAppearance.BorderSize = 0;
            skip.BackColor = theme.Background;
            skip.ForeColor = theme.TextMuted;
            skip.FlatAppearance.MouseOverBackColor = theme.Surface;
            skip.Click += (s, e) => Pick(UpdateChoice.Skip);

            const int gap = 8;
            int right = Pad + ContentWidth;
            int rowWidth = primary.Width + gap + later.Width + (page != null ? page.Width + gap : 0);
            bool skipOwnRow = skip.Width + 16 + rowWidth > ContentWidth;

            primary.Location = new Point(right - primary.Width, y);
            int x = primary.Left - gap;
            if (page != null)
            {
                page.Location = new Point(x - page.Width, y);
                x = page.Left - gap;
            }
            later.Location = new Point(x - later.Width, y);

            if (skipOwnRow)
            {
                // A long translation: the quiet choice gets its own row rather than being squeezed.
                skip.Location = new Point(Pad - 6, y + primary.Height + 6);
            }
            else
            {
                skip.Location = new Point(Pad - 6, y);
            }

            Controls.Add(primary);
            if (page != null) { Controls.Add(page); }
            Controls.Add(later);
            Controls.Add(skip);

            int bottom = Math.Max(primary.Bottom, skip.Bottom);
            ClientSize = new Size(width, bottom + 20);

            // "Later" is the safe default under Escape — and when a run is in progress it is the
            // ACCEPT button too, so leaning on Enter cannot stop the run by accident.
            CancelButton = later;
            AcceptButton = busy ? later : primary;
            UiFactory.ScaleToScreen(this);
        }

        /// <summary>
        /// The notes as formatted text. If RichEdit ever refuses the RTF, the same notes go into a
        /// plain box instead — the notes are worth more than their formatting.
        /// </summary>
        private static Control BuildNotesView(string notes, string version, Theme theme, Rectangle bounds)
        {
            var blocks = ReleaseNotes.Parse(notes, version);
            var font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            if (blocks.Count == 0)
            {
                return new Label
                {
                    Text = Localization.T("(No release notes were provided.)"),
                    ForeColor = theme.TextMuted,
                    BackColor = theme.Surface,
                    Font = font,
                    Bounds = bounds
                };
            }

            var rich = new RichTextBox
            {
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                DetectUrls = false,
                BackColor = theme.Surface,
                ForeColor = theme.Text,
                Font = font,
                Bounds = bounds,
                TabStop = false,
                ShortcutsEnabled = true
            };
            try
            {
                rich.Rtf = ReleaseNotes.ToRtf(blocks, font, theme);
                rich.Select(0, 0);
                return rich;
            }
            catch (Exception ex)
            {
                Logger.Warn("[Update] release notes could not be formatted (" + ex.Message + "); showing them as text.");
                rich.Dispose();
                var box = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    BackColor = theme.Surface,
                    ForeColor = theme.Text,
                    BorderStyle = BorderStyle.None,
                    Font = font,
                    Bounds = bounds,
                    TabStop = false,
                    Text = ReleaseNotes.ToPlainText(blocks, spaced: true).Replace("\n", "\r\n")
                };
                box.Select(0, 0);
                return box;
            }
        }

        /// <summary>A wrapped line of text, measured so it gets the height it needs in any language.</summary>
        private int AddText(string text, Font font, Color colour, int y, int after)
        {
            var label = new Label
            {
                Text = text,
                Font = font,
                ForeColor = colour,
                AutoSize = false,
                Location = new Point(Pad, y),
                Size = new Size(ContentWidth, Measure96(text, font, ContentWidth).Height + 2)
            };
            Controls.Add(label);
            return label.Bottom + after;
        }

        /// <summary>
        /// Text size in the 96-DPI units this layout is written in. TextRenderer measures at the
        /// screen's real DPI, and AutoScaleMode.Dpi then scales the layout again — measuring
        /// straight into it would count the display scale twice.
        /// </summary>
        internal static Size Measure96(string text, Font font, int width96)
        {
            // The shared helper now (UiFactory.Measure96): every dialog laid out in 96-dpi units needs it.
            return UiFactory.Measure96(text, font, width96, TextFormatFlags.NoPrefix);
        }

        private void Pick(UpdateChoice choice)
        {
            Choice = choice;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static Button MakeButton(string text, bool primary, Theme theme)
        {
            var font = new Font("Segoe UI", 9f, primary ? FontStyle.Bold : FontStyle.Regular);
            // Sized from its own caption, so "Diese Version überspringen" fits as well as "Later".
            int w = Math.Max(primary ? 124 : 84, Measure96(text, font, 1000).Width + 30);
            var b = new Button
            {
                Text = text,
                Size = new Size(w, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? theme.Accent : theme.Surface2,
                ForeColor = primary ? theme.OnAccent : theme.Text,
                Font = font,
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            b.FlatAppearance.BorderColor = theme.Border;
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.MouseOverBackColor = primary ? theme.AccentHover : theme.Surface;
            return b;
        }

        internal static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return value.ToString(unit == 0 ? "0" : "0.0", Localization.DateCulture) + " " + units[unit];
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

        /// <summary>A rounded surface panel that hosts the notes.</summary>
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
