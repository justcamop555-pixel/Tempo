using System;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Simple About dialog showing version information and a link to the log file.
    /// </summary>
    public sealed class AboutForm : Form
    {
        private System.IO.Stream _gifStream; // kept alive so the GIF keeps animating

        /// <summary>
        /// The update line About shows: where this copy stands, as the last check found it, and the one thing to do
        /// about it. <see cref="Act"/> runs after About has closed (a check answers with dialogs of its own).
        /// </summary>
        public sealed class UpdateLine
        {
            /// <summary>The verdict: "You're up to date.", "Update available: v1.0.328", …</summary>
            public string State;
            /// <summary>When it was found, or empty: "Last checked for updates: 12 Nov 2026, 08:32".</summary>
            public string When;
            /// <summary>An update is known: the line is drawn in the accent and the button shows it.</summary>
            public bool UpdateKnown;
            /// <summary>The last check failed: the line is drawn as a warning.</summary>
            public bool Failed;
            public string ButtonText;
            public Action Act;
        }

        /// <summary>What the update button asked for; run by the owner once About has closed.</summary>
        public Action AfterClose { get; private set; }

        public AboutForm(Theme theme) : this(theme, null) { }

        public AboutForm(Theme theme, UpdateLine update)
        {
            // Built in 100% units; scaled with the screen at the end (UiFactory.ScaleToScreen).
            theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            Text = Utils.Localization.T("About Tempo");
            // Taller and a little wider: the description below is a real summary of what
            // Tempo does now rather than one sentence about clicking, and it was being
            // squeezed into 78px.
            // +22 for the Official ID row, which also gave "Open data folder" back a line of its own.
            // The client area (was a 470×522 window): see UiFactory.ScaleToScreen.
            ClientSize = new Size(454, 483);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = theme.Background;
            // Title bar in the theme too, so the dialog matches the app.
            ThemeManager.ApplyWindowChrome(this, theme);
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            // Animated logo (top-right). Drag an image/GIF onto it — or use the
            // "Choose image…" button below — to set your own logo.
            var logo = new PictureBox
            {
                Left = 330,
                Top = 20,
                Width = 104,
                Height = 104,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                AllowDrop = true,
                Cursor = Cursors.Hand
            };
            LoadLogo(logo);

            // A drop target with no drop feedback is a guessing game — and until the
            // format fix below, dragging from a browser genuinely did nothing, with no
            // way to tell whether Tempo had even noticed. The logo now outlines itself
            // in the accent colour while a droppable image is over it.
            bool dragOverLogo = false;
            logo.Paint += (s, e) =>
            {
                if (!dragOverLogo) { return; }
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var r = new Rectangle(1, 1, logo.Width - 3, logo.Height - 3);
                using (var pen = new Pen(theme.Accent, 2f))
                using (var path = RoundedRect(r, 10))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            };
            logo.DragLeave += (s, e) => { dragOverLogo = false; logo.Invalidate(); };
            FormClosed += (s, e) =>
            {
                logo.Image = null;
                _gifStream?.Dispose();
            };

            var dropHint = UiFactory.Label(
                "Want your own logo? Click \"Choose image…\" and pick a .gif / .png / .jpg, " +
                "or drag an image straight onto the logo (even from a web page).",
                24, 216, FontStyle.Italic, 8f);
            dropHint.ForeColor = theme.TextMuted;
            dropHint.AutoSize = false;
            dropHint.Width = 410;
            // As tall as its words: three lines in French and Portuguese, where a fixed two
            // stopped drawing before "…even from a web page". Placed in the flow further down.
            // (In 100% units: the window is scaled after it is built — UiFactory.Measure96.)
            dropHint.Height = Math.Max(30, UiFactory.Measure96(dropHint.Text, dropHint.Font, dropHint.Width, TextFormatFlags.Default).Height + 2);

            var chooseLogo = UiFactory.Button("Choose image…", 330, 128, 104, 26);
            // As wide as its caption needs, keeping the column's right edge at 434 ("Choisir un
            // logo…" was a pixel short of its padding at a fixed 104).
            UiFactory.FitToText96(chooseLogo, 104, 16);
            chooseLogo.Left = 434 - chooseLogo.Width;
            chooseLogo.Name = "chooseLogo";
            chooseLogo.BackColor = theme.Surface2;
            chooseLogo.ForeColor = theme.Text;

            // Wrapped inside the logo column rather than one line starting at x=330. On one line it
            // needs 116px in English and 143–215px in the other languages, while the column is 104px
            // and the window ends about 20px after it — so every translation was cut off at the right
            // edge. The height is measured: a longer translation takes another line instead.
            var resetLogo = new LinkLabel
            {
                Name = "resetLogo",
                Text = Localization.T("Reset to default logo"),
                Font = UiFactory.BodyFont,
                AutoSize = false,
                TextAlign = ContentAlignment.TopCenter,
                Left = 330,
                Top = 158,
                Width = 104,
                LinkColor = theme.TextMuted,
                ActiveLinkColor = theme.AccentText,
                Visible = CustomLogo.Exists()
            };
            resetLogo.Height = UiFactory.Measure96(resetLogo.Text, resetLogo.Font, resetLogo.Width, TextFormatFlags.Default).Height + 2;
            resetLogo.LinkClicked += (s, e) =>
            {
                CustomLogo.Clear();
                LoadLogo(logo);
                resetLogo.Visible = false;
            };

            chooseLogo.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog
                {
                    Title = Localization.T("Choose a logo image"),
                    Filter = Localization.T("Images (*.png;*.gif;*.jpg;*.jpeg;*.bmp)|*.png;*.gif;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*")
                })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        if (CustomLogo.SaveFromFile(dlg.FileName, out string err))
                        {
                            LoadLogo(logo);
                            resetLogo.Visible = true;
                        }
                        else if (!string.IsNullOrEmpty(err))
                        {
                            MessageBox.Show(this, err, "Tempo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                }
            };

            logo.DragEnter += (s, e) =>
            {
                bool ok = DropHasImage(e.Data);
                e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
                if (ok != dragOverLogo) { dragOverLogo = ok; logo.Invalidate(); }
            };
            logo.DragDrop += (s, e) =>
            {
                dragOverLogo = false;
                logo.Invalidate();
                // A dropped file is a quick local read; a dropped URL means a network
                // download, so do that off the UI thread to avoid freezing the dialog.
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                    if (files == null || files.Length == 0)
                    {
                        return;
                    }
                    if (CustomLogo.SaveFromFile(files[0], out string ferr))
                    {
                        LoadLogo(logo);
                        resetLogo.Visible = true;
                    }
                    else if (!string.IsNullOrEmpty(ferr))
                    {
                        MessageBox.Show(this, ferr, "Tempo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }

                string url = UrlFromDrop(e.Data);
                if (string.IsNullOrEmpty(url))
                {
                    return;
                }

                // An inline data: image needs no network at all — decode and save it here
                // rather than handing it to the downloader.
                if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    if (CustomLogo.SaveFromDataUri(url, out string derr))
                    {
                        LoadLogo(logo);
                        resetLogo.Visible = true;
                    }
                    else if (!string.IsNullOrEmpty(derr))
                    {
                        MessageBox.Show(this, derr, "Tempo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }

                chooseLogo.Enabled = false;
                System.Threading.Tasks.Task.Run(() =>
                {
                    bool ok = CustomLogo.SaveFromUrl(url, out string err);
                    if (IsDisposed) return;
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            chooseLogo.Enabled = true;
                            if (ok)
                            {
                                LoadLogo(logo);
                                resetLogo.Visible = true;
                            }
                            else if (!string.IsNullOrEmpty(err))
                            {
                                MessageBox.Show(this, err, "Tempo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                        }));
                    }
                    catch (ObjectDisposedException) { }
                    catch (InvalidOperationException) { }
                });
            };

            var tips = new ToolTip();
            Disposed += (s, e) => tips.Dispose();

            var title = UiFactory.Label("Tempo", 24, 18, FontStyle.Bold, 18f);

            // What Tempo is, under its name. It sat below the facts and a rule, where it read as small print.
            // The old text described a mouse auto-clicker and nothing else — accurate in about 2021.
            var lead = UiFactory.Label(
                "A clicking, macro and accessibility tool that runs entirely on your PC.",
                24, 54, FontStyle.Regular, 9.5f);
            lead.AutoSize = false;
            lead.Width = 296;   // beside the logo column, which starts at 330
            lead.Height = UiFactory.Measure96(lead.Text, lead.Font, lead.Width, TextFormatFlags.WordBreak).Height + 2;

            // The facts a bug report asks for, each under its own name. They were English lines put together at run time
            // ("Version …", "Display: … scaling", "Installed · … built …", "Data: …"), so no language ever saw them
            // translated. Names in the left column (muted), values in the right; a value too long for its row is cut
            // with "…" and shown whole in its tip.
            int y = lead.Top + lead.Height + 10;
            const int KeyLeft = 24, KeyWidth = 88, ValueLeft = 116, ValueWidth = 204;
            Font rowFont = UiFactory.FontFor(9f, FontStyle.Regular);
            Label Row(string key, string value, string tip = null, Color? colour = null)
            {
                var k = UiFactory.Label(key, KeyLeft, y, FontStyle.Regular, 9f);
                k.AutoSize = false;
                k.Width = KeyWidth;
                k.Height = 18;
                k.AutoEllipsis = true;
                k.ForeColor = theme.TextMuted;
                var v = new Label
                {
                    Text = value ?? "",
                    Left = ValueLeft,
                    Top = y,
                    Width = ValueWidth,
                    Height = 18,
                    AutoSize = false,
                    AutoEllipsis = true,
                    UseMnemonic = false,
                    Font = rowFont,
                    ForeColor = colour ?? theme.Text,
                    BackColor = Color.Transparent
                };
                if (!string.IsNullOrEmpty(tip)) { TipStyle.Set(tips, v, tip); }
                Controls.Add(k);
                Controls.Add(v);
                y += 20;
                return v;
            }

            // Read the real version from the assembly so this never goes stale: the csproj <Version> flows into it.
            string versionNumber = "?";
            string verText = "Version ?";
            try
            {
                var asmVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (asmVer != null)
                {
                    versionNumber = asmVer.Major + "." + asmVer.Minor + "." + asmVer.Build;
                    verText = "Version " + versionNumber;
                }
                // The version alone cannot say which copy this is — a test build shares
                // it with the release it was cut from. The build ID can, so it goes
                // wherever someone looks up "what am I running", which is exactly here.
                // Short, not Full: this row ends where the logo begins, and the full
                // form ran underneath it. The build TIME goes on the .NET row below,
                // which has the space for it.
                verText += "   ·   " + Utils.BuildInfo.Short;
            }
            catch { }
            // On screen: the number and the build, under the translated name. (verText, in English, is for Copy details.)
            string versionLine = versionNumber + "   ·   " + Utils.BuildInfo.Short;
            var version = Row("Version", versionLine, versionLine);

            string netVerRow;
            try { netVerRow = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription; }
            catch { netVerRow = ".NET"; }
            Row("Edition", Localization.T(Utils.DeploymentInfo.IsInstalled ? "Installed" : "Portable") + "   ·   " + netVerRow);
            if (Utils.BuildInfo.BuiltUtcText.Length > 0) { Row("Built", Utils.BuildInfo.BuiltUtcText); }

            int hz = Utils.EnvironmentInfo.GetPrimaryRefreshHz();
            string scale = Utils.EnvironmentInfo.GetDisplayScaleText();
            Screen ps = Screen.PrimaryScreen;
            string disp = ps != null
                ? $"Display: {ps.Bounds.Width}\u00d7{ps.Bounds.Height}" + (hz > 0 ? $" @ {hz} Hz" : "") +
                  (scale != null ? $" \u00b7 {scale} scaling" : "")
                : (hz > 0 ? $"Display: {hz} Hz" : "Display: unknown");
            // The row's value: no English around the numbers ("Display: …", "… scaling" were never translated).
            string dispValue = ps != null
                ? ps.Bounds.Width + "×" + ps.Bounds.Height + (hz > 0 ? " @ " + hz + " Hz" : "") + (scale != null ? "   ·   " + scale : "")
                : (hz > 0 ? hz + " Hz" : "—");

            // For Copy details, which stays in English for a report: how it's running, the runtime, when it was built.
            string edition = Utils.DeploymentInfo.IsInstalled ? "Installed" : "Portable";
            string netVer = netVerRow;
            string builtAt = Utils.BuildInfo.BuiltUtcText;
            if (builtAt.Length > 0) { builtAt = "  \u00b7  built " + builtAt; }

            string dataDir = Persistence.SettingsManager.GetSettingsDirectory();

            // Name the operating system. About listed the display, the runtime and the
            // data folder but never Windows itself, so "which Windows are you on?" — the
            // first question of every bug report — had no answer anywhere in the app.
            // It went in at y=142 as if that row were empty, but "Open data folder" already sat
            // there — and this opaque label, added first and so on top, hid the link and took its
            // clicks. The link has its own row now, under the Official ID.
            string osText;
            try
            {
                osText = Utils.WindowsVersion.Describe();
                string note = Utils.WindowsVersion.SupportNote();
                if (note.Length > 0) { osText += "  ⚠"; }
            }
            catch { osText = null; }

            if (!string.IsNullOrEmpty(osText))
            {
                // In the warning colour (the readable one: plain Warning is a fill and was used here) when this Windows
                // is not fully supported, with why in the tip; the whole name there too, as a long one is cut.
                string note = "";
                try { note = Utils.WindowsVersion.SupportNote() ?? ""; } catch { }
                bool supported = Utils.WindowsVersion.Status == Utils.WindowsVersion.Support.Supported;
                Row("Windows", osText, note.Length > 0 ? osText + "\n\n" + note : osText,
                    supported ? (Color?)null : theme.WarningText);
            }
            Row("Display", dispValue, dispValue);
            Row("Data folder", dataDir, dataDir);

            // The Official ID: the one line in this dialog anyone can check without trusting Tempo. Below the logo
            // column now, so it has the dialog's whole width.
            Control officialId = BuildOfficialIdRow(theme, rowFont);
            officialId.Top = y + 4;
            officialId.Width = 410;
            Controls.Add(officialId);
            y = officialId.Top + officialId.Height + 6;

            var openData = new LinkLabel
            {
                Text = Localization.T("Open data folder"),
                AutoSize = true,
                Location = new Point(24, y),
                LinkColor = theme.AccentText,
                ActiveLinkColor = theme.AccentText,
                BackColor = Color.Transparent
            };
            openData.LinkClicked += (s, e) =>
            {
                try
                {
                    System.IO.Directory.CreateDirectory(dataDir);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dataDir,
                        UseShellExecute = true
                    });
                }
                catch { }
            };
            Controls.Add(openData);

            // Everything above, as text. A report about a laptop arrived as a screenshot of this box —
            // the version, build, Official ID, Windows and display, read off a picture. One click copies
            // them instead (the data folder stays out: it carries the Windows account name).
            string details = string.Join(Environment.NewLine, new[]
            {
                verText + "   ·   Official ID: " + Utils.IntegrityCheck.OfficialIdReportLine(),
                $"{edition}  ·  {netVer}{builtAt}" + (Utils.BuildInfo.SourceText.Length > 0 ? "  ·  source " + Utils.BuildInfo.SourceText : ""),
                osText ?? "",
                disp,
            });
            string copyText = Localization.T("Copy details");
            var copyDetails = new LinkLabel
            {
                Text = copyText,
                AutoSize = true,
                // After "Open data folder", by its width in 100% units: its Right is screen pixels while this
                // window is built (see the links at the bottom).
                Location = new Point(openData.Left + UiFactory.Measure96(openData.Text, openData.Font, 0, TextFormatFlags.Default).Width + 18, y),
                LinkColor = theme.AccentText,
                ActiveLinkColor = theme.AccentText,
                BackColor = Color.Transparent
            };
            var copiedTimer = new Timer { Interval = 1500 };
            copiedTimer.Tick += (s, e) => { copiedTimer.Stop(); copyDetails.Text = copyText; };
            copyDetails.LinkClicked += (s, e) =>
            {
                try { Clipboard.SetText(details); } catch { return; }   // the clipboard may be held by another app
                copyDetails.Text = Localization.T("Copied ✓");
                copiedTimer.Stop();
                copiedTimer.Start();
            };
            Disposed += (s, e) => copiedTimer.Dispose();
            TipStyle.Set(new ToolTip(), copyDetails,
                Localization.T("Copies the version, build, Official ID, Windows and display — paste it into a bug report."));
            Controls.Add(copyDetails);

            // Which code this build is: the commit it was made from, and whether it carried changes on top — in the
            // version row's tip, after the version itself (one tip per control: the row already has one).
            if (Utils.BuildInfo.SourceCommit.Length > 0)
            {
                TipStyle.Set(tips, version, versionLine + "\n\n" + (Utils.BuildInfo.SourceChanged
                    ? Localization.F("Built from commit {0}, plus changes that were not committed yet.", Utils.BuildInfo.SourceCommit)
                    : Localization.F("Built from commit {0}.", Utils.BuildInfo.SourceCommit)));
            }
            y += 26;

            // Where this copy stands: the last update check's answer, and the one thing to do about it (check again,
            // or see the update it found). The button closes About first; the check answers with its own dialogs.
            if (update != null)
            {
                Button act = null;
                if (!string.IsNullOrEmpty(update.ButtonText) && update.Act != null)
                {
                    act = update.UpdateKnown
                        ? UiFactory.PrimaryButton(update.ButtonText, 0, y, 150, 30, theme)
                        : UiFactory.Button(update.ButtonText, 0, y, 150, 30);
                    UiFactory.FitToText96(act, 120, 26);
                    act.Left = 434 - act.Width;
                    if (!update.UpdateKnown)
                    {
                        act.BackColor = theme.Surface2;
                        act.ForeColor = theme.Text;
                    }
                    act.Name = "aboutUpdate";
                    act.Click += (s, e) => { AfterClose = update.Act; Close(); };
                    Controls.Add(act);
                }
                int textWidth = (act != null ? act.Left - 12 : 434) - 24;
                var state = new Label
                {
                    Name = "aboutUpdateState",
                    Text = update.State ?? "",
                    Left = 24,
                    Top = y,
                    Width = textWidth,
                    AutoSize = false,
                    UseMnemonic = false,
                    Font = UiFactory.FontFor(9.5f, FontStyle.Bold),
                    ForeColor = update.UpdateKnown ? theme.AccentText : update.Failed ? theme.WarningText : theme.SuccessText,
                    BackColor = Color.Transparent
                };
                state.Height = UiFactory.Measure96(state.Text, state.Font, state.Width, TextFormatFlags.WordBreak).Height + 2;
                Controls.Add(state);
                int textBottom = state.Top + state.Height;
                if (!string.IsNullOrEmpty(update.When))
                {
                    var when = new Label
                    {
                        Text = update.When,
                        Left = 24,
                        Top = textBottom,
                        Width = textWidth,
                        AutoSize = false,
                        UseMnemonic = false,
                        Font = rowFont,
                        ForeColor = theme.TextMuted,
                        BackColor = Color.Transparent
                    };
                    when.Height = UiFactory.Measure96(when.Text, when.Font, when.Width, TextFormatFlags.WordBreak).Height + 2;
                    Controls.Add(when);
                    textBottom = when.Top + when.Height;
                }
                y = Math.Max(textBottom, act != null ? act.Top + act.Height : 0) + 12;
            }

            // A thin rule between the identity block and the blurb, so the dialog reads
            // as two parts instead of one long column of left-aligned text.
            var rule = new Panel
            {
                Left = 24,
                Top = y,
                Width = 410,
                Height = 1,
                // Surface2, not Border: on the darker themes Border sits so close to the
                // page colour that the rule was invisible, which makes it decoration
                // that costs a row and buys nothing.
                BackColor = theme.Surface2
            };
            Controls.Add(rule);
            y += 12;

            // What Tempo does. Captions, notifications and the movement engine are most of what Tempo is now.
            var description = new Label
            {
                Left = 24,
                Top = y,
                Width = 410,
                Height = 128,
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = UiFactory.BodyFont,
                Text = Localization.T(
                    "•  Clicking — intervals, burst and hold, multi-point sequences, randomisation\n"
                    + "•  Macros — record, replay and edit input, saved into profiles\n"
                    + "•  Live captions — offline speech-to-text for any sound on this PC, on the "
                    + "processor or your graphics card, in 90+ languages\n"
                    + "•  Notifications — Tempo's own, plus a mirror of Windows' own\n"
                    + "•  Global hotkeys, themes, live statistics and a CPS test")
            };
            // The same for the list: German needs 7 px more than the 128 it had, and its last
            // line was not drawn.
            description.Height = Math.Max(128, UiFactory.Measure96(description.Text, description.Font, description.Width,
                TextFormatFlags.Default).Height + 2);
            y += description.Height + 10;

            // "Want your own logo?" — small print, after what Tempo does.
            dropHint.Top = y;
            y += dropHint.Height + 12;

            // Where to read more: the site, the code, this version's notes, and the licences of what is inside.
            // Left to right, each measured in 100% units (a link's Right is the screen's pixels while this window is
            // built), onto a second line where a language needs the room.
            int linkX = 24;
            LinkLabel AddLink(string text, Action open)
            {
                var link = new LinkLabel
                {
                    Text = text,
                    AutoSize = true,
                    LinkColor = theme.AccentText,
                    ActiveLinkColor = theme.AccentText,
                    BackColor = Color.Transparent
                };
                int w = UiFactory.Measure96(link.Text, link.Font, 0, TextFormatFlags.Default).Width;
                if (linkX > 24 && linkX + w > 434) { linkX = 24; y += 22; }
                link.Location = new Point(linkX, y);
                linkX += w + 16;
                link.LinkClicked += (s, e) => open();
                Controls.Add(link);
                return link;
            }
            AddLink(Localization.T("Website"), () => OpenUrl(SafetyNoteCard.WebsiteUrl));
            // Not translated, deliberately: a brand name, like "Tempo" itself.
            AddLink("GitHub", () => OpenUrl(SafetyNoteCard.GitHubUrl));
            // This version's release page once GitHub has confirmed this copy; every release otherwise (a test build's
            // number may have no page yet).
            AddLink(Localization.T("Release notes"), () => OpenUrl(Utils.IntegrityCheck.OfficialReleaseUrl
                ?? "https://github.com/" + UpdateChecker.Repository + "/releases"));
            LinkLabel licences = AddLink(Localization.T("Open-source licences"), () => Utils.ThirdPartyNotices.Open());
            TipStyle.Set(tips, licences, Localization.T("The open-source parts inside Tempo and their licences, as a text file."));
            y += 30;

            var openLog = UiFactory.Button("Open Log Folder", 24, y, 150, 32);
            openLog.BackColor = theme.Surface2;
            openLog.ForeColor = theme.Text;
            openLog.Click += (s, e) => OpenLogFolder();
            // "Ouvrir le dossier des journaux" needs 170 px.
            UiFactory.FitToText96(openLog, 150, 22);

            var ok = UiFactory.PrimaryButton("Close", 344, y, 90, 32, theme);
            ok.Click += (s, e) => Close();
            y += 32 + 20;

            // As tall as what it holds (in 100% units; UiFactory.ScaleToScreen scales it with everything else).
            ClientSize = new Size(454, y);
            Controls.AddRange(new Control[] { logo, dropHint, chooseLogo, resetLogo, title, lead, description, openLog, ok });

            AcceptButton = ok;
            UiFactory.ScaleToScreen(this);
        }

        /// <summary>
        /// The Official ID row: the start of the SHA-256 that GitHub lists for Tempo.exe on the
        /// release page, shown only once GitHub has confirmed this exact file (IntegrityCheck.OfficialId).
        ///
        /// A screenshot of this row proves nothing by itself — a modified copy can print any text it
        /// likes. The ID is worth showing because it can be matched somewhere Tempo does not control:
        /// the release page, or Get-FileHash on the file. So the verified row links straight to that
        /// page, and every other state says why there is no ID, rather than leaving a blank that
        /// reads as "fine".
        /// </summary>
        private Control BuildOfficialIdRow(Theme theme, Font font)
        {
            string id = Utils.IntegrityCheck.OfficialId;
            string tip = null;
            Control row;
            if (id != null)
            {
                string url = Utils.IntegrityCheck.OfficialReleaseUrl;
                var link = new LinkLabel
                {
                    Text = Localization.F("✓ Official ID {0}", id),
                    LinkColor = theme.SuccessText,
                    VisitedLinkColor = theme.SuccessText,
                    ActiveLinkColor = theme.AccentText,
                    LinkBehavior = LinkBehavior.HoverUnderline
                };
                link.LinkClicked += (s, e) => OpenUrl(url);
                tip = Localization.F("Anyone can check this without trusting Tempo: it is the start of the SHA-256 "
                    + "that GitHub lists for Tempo.exe on the {0} release page. Every release has its own ID. "
                    + "Click to open that page.", Utils.IntegrityCheck.OfficialTag);
                row = link;
            }
            else
            {
                string text;
                Color colour = theme.TextMuted;
                switch (Utils.IntegrityCheck.Verdict)
                {
                    case Utils.IntegrityVerdict.TestBuild:
                        text = Localization.T("No Official ID — test build");
                        colour = theme.WarningText;
                        tip = Localization.T("Only the Tempo.exe published on GitHub's releases page has an Official ID. "
                            + "A test build, or a copy built from source, has none.");
                        break;
                    case Utils.IntegrityVerdict.Modified:
                    case Utils.IntegrityVerdict.Repackaged:
                    case Utils.IntegrityVerdict.Damaged:
                    case Utils.IntegrityVerdict.UnknownRelease:
                    case Utils.IntegrityVerdict.UnpackedModified:
                    case Utils.IntegrityVerdict.ForeignCode:
                        text = Localization.T("✗ No Official ID — this copy failed its check");
                        colour = theme.DangerText;
                        tip = Localization.T("Open Settings to see what the check found.");
                        break;
                    case Utils.IntegrityVerdict.Unverified:
                        // Switched off, or the file could not be read: nothing was checked either way.
                        text = Localization.T("Official ID: not checked");
                        break;
                    default:
                        // Not confirmed yet: the check is still running, or GitHub was out of reach.
                        text = Localization.T("Official ID: not verified yet");
                        tip = Localization.T("Tempo shows an Official ID once GitHub confirms that this exact file "
                            + "is the one it published. That needs an internet connection once.");
                        break;
                }
                row = new Label { Text = text, ForeColor = colour };
            }
            var label = (Label)row;             // a LinkLabel is a Label
            label.AutoSize = false;
            label.AutoEllipsis = true;
            row.Name = "officialId";
            row.Font = font;
            row.BackColor = Color.Transparent;
            row.Location = new Point(24, 160);
            row.Size = new Size(300, 18);
            if (tip != null)
            {
                TipStyle.Set(new ToolTip { AutoPopDelay = 20000 }, row, tip);
            }
            return row;
        }

        /// <summary>A rounded rectangle path, for the logo's drop-target outline.</summary>
        private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) { path.AddRectangle(r); return path; }
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static bool DropHasImage(IDataObject data)
        {
            if (data == null) return false;
            if (data.GetDataPresent(DataFormats.FileDrop)) return true;
            return UrlFromDrop(data) != null;
        }

        /// <summary>
        /// Pulls an image URL out of a drag, whatever shape the source put it in.
        ///
        /// This only looked at CF_TEXT, which is why dragging a picture out of a browser
        /// did nothing: browsers dragging an IMAGE (as opposed to selected text or a
        /// link) frequently offer no plain-text flavour at all. Chrome and Edge put the
        /// address in CFSTR_INETURLW ("UniformResourceLocatorW"), Firefox uses
        /// "text/x-moz-url", and all of them include an "HTML Format" fragment holding
        /// the &lt;img&gt; tag. Because DropHasImage saw none of those, DragEnter reported
        /// DragDropEffects.None and Windows refused the drop before DragDrop ever ran —
        /// the cursor showed the "no entry" circle and nothing happened.
        ///
        /// Ordered most reliable first. Returns null when nothing usable is present.
        /// </summary>
        private static string UrlFromDrop(IDataObject data)
        {
            if (data == null) { return null; }

            // Chrome / Edge / Internet Explorer. The Unicode flavour first — the ANSI
            // one mangles any non-ASCII character in the path.
            string s = ReadDragString(data, "UniformResourceLocatorW", Encoding.Unicode);
            if (LooksLikeImageUrl(s)) { return s; }
            s = ReadDragString(data, "UniformResourceLocator", Encoding.Default);
            if (LooksLikeImageUrl(s)) { return s; }

            // Firefox: "url\ntitle" in UTF-16.
            s = ReadDragString(data, "text/x-moz-url", Encoding.Unicode);
            if (s != null)
            {
                s = s.Split('\n', '\r')[0].Trim();
                if (LooksLikeImageUrl(s)) { return s; }
            }

            // Plain text, the one case that already worked (dragging a LINK, or text
            // that happens to be a URL).
            try
            {
                if (data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text))
                {
                    string t = (data.GetData(DataFormats.UnicodeText) as string)
                               ?? (data.GetData(DataFormats.Text) as string);
                    t = t?.Split('\n', '\r')[0].Trim();
                    if (LooksLikeImageUrl(t)) { return t; }
                }
            }
            catch { }

            // Last resort: the HTML fragment every browser attaches. Dig the first
            // <img src="…"> out of it. This is what catches the sites that hand over
            // no URL flavour at all, only markup.
            try
            {
                if (data.GetDataPresent("HTML Format"))
                {
                    string html = data.GetData("HTML Format") as string;
                    if (html == null)
                    {
                        html = ReadDragString(data, "HTML Format", Encoding.UTF8);
                    }
                    string src = FirstImageSrc(html);
                    if (LooksLikeImageUrl(src)) { return src; }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Reads one drag format that arrives as raw bytes (a MemoryStream) rather than
        /// a string, decodes it and trims the trailing NUL these formats carry.
        /// </summary>
        private static string ReadDragString(IDataObject data, string format, Encoding encoding)
        {
            try
            {
                if (!data.GetDataPresent(format)) { return null; }
                object raw = data.GetData(format);
                if (raw is string direct) { return direct.Trim('\0').Trim(); }
                if (raw is System.IO.MemoryStream ms)
                {
                    string text = encoding.GetString(ms.ToArray());
                    int nul = text.IndexOf('\0');
                    if (nul >= 0) { text = text.Substring(0, nul); }
                    return text.Trim();
                }
            }
            catch { }
            return null;
        }

        /// <summary>The src of the first &lt;img&gt; in an HTML fragment, or null.</summary>
        private static string FirstImageSrc(string html)
        {
            if (string.IsNullOrEmpty(html)) { return null; }
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    html, "<img[^>]+?src\\s*=\\s*([\"'])(?<src>.*?)\\1",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                if (m.Success)
                {
                    return System.Net.WebUtility.HtmlDecode(m.Groups["src"].Value).Trim();
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Something we can actually fetch: an http(s) address, or an inline data: image
        /// (what a canvas, an SVG or a lazy-loading gallery often hands over instead).
        /// </summary>
        private static bool LooksLikeImageUrl(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) { return false; }
            return s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
        }

        private void LoadLogo(PictureBox box)
        {
            box.Image = null;
            _gifStream?.Dispose();
            _gifStream = null;

            try
            {
                string custom = CustomLogo.GetPath();
                if (custom != null && System.IO.File.Exists(custom))
                {
                    var ms = new System.IO.MemoryStream(System.IO.File.ReadAllBytes(custom));
                    _gifStream = ms;
                    box.Image = Image.FromStream(ms);
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not load custom logo: " + ex.Message);
            }

            TryLoadAnimatedLogo(box);
        }

        private void TryLoadAnimatedLogo(PictureBox box)
        {
            try
            {
                var asm = typeof(AboutForm).Assembly;
                foreach (string name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith("tempo-about.gif", StringComparison.OrdinalIgnoreCase))
                    {
                        // Copy to a MemoryStream we keep alive; an animated GIF needs
                        // its backing stream open for the whole time it's shown.
                        var src = asm.GetManifestResourceStream(name);
                        if (src == null) return;
                        var ms = new System.IO.MemoryStream();
                        src.CopyTo(ms);
                        src.Dispose();
                        ms.Position = 0;
                        _gifStream = ms;
                        box.Image = Image.FromStream(ms);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not load the animated About logo: " + ex.Message);
            }
        }

        private void OpenLogFolder()
        {
            try
            {
                string path = Logger.GetLogPath();
                if (!string.IsNullOrEmpty(path))
                {
                    string folder = System.IO.Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = folder,
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not open log folder: " + ex.Message);
            }
        }
        private static void OpenUrl(string url)
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
