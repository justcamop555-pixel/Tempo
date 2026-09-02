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

        public AboutForm(Theme theme)
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            Text = Utils.Localization.T("About Tempo");
            // Taller and a little wider: the description below is a real summary of what
            // Tempo does now rather than one sentence about clicking, and it was being
            // squeezed into 78px.
            Size = new Size(470, 500);
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
                24, 194, FontStyle.Italic, 8f);
            dropHint.ForeColor = theme.TextMuted;
            dropHint.AutoSize = false;
            dropHint.Width = 410;
            dropHint.Height = 30;

            var chooseLogo = UiFactory.Button("Choose image…", 330, 128, 104, 26);
            chooseLogo.BackColor = theme.Surface2;
            chooseLogo.ForeColor = theme.Text;

            var resetLogo = new LinkLabel
            {
                Text = Localization.T("Reset to default logo"),
                AutoSize = true,
                Left = 330,
                Top = 160,
                LinkColor = theme.TextMuted,
                ActiveLinkColor = theme.Accent,
                Visible = CustomLogo.Exists()
            };
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

            var title = UiFactory.Label("Tempo", 24, 28, FontStyle.Bold, 18f);
            // Read the real version from the assembly so this never goes stale. The
            // csproj <Version> flows into the assembly version, so a single bump there
            // updates the About box automatically.
            string verText = "Version ?";
            try
            {
                var asmVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (asmVer != null)
                {
                    verText = "Version " + asmVer.Major + "." + asmVer.Minor + "." + asmVer.Build;
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
            var version = UiFactory.Label(verText, 24, 64, FontStyle.Regular, 9.5f);
            version.ForeColor = theme.TextMuted;

            int hz = Utils.EnvironmentInfo.GetPrimaryRefreshHz();
            string scale = Utils.EnvironmentInfo.GetDisplayScaleText();
            Screen ps = Screen.PrimaryScreen;
            string disp = ps != null
                ? $"Display: {ps.Bounds.Width}\u00d7{ps.Bounds.Height}" + (hz > 0 ? $" @ {hz} Hz" : "") +
                  (scale != null ? $" \u00b7 {scale} scaling" : "")
                : (hz > 0 ? $"Display: {hz} Hz" : "Display: unknown");
            var sysInfo = UiFactory.Label(disp, 24, 86, FontStyle.Regular, 9f);
            sysInfo.ForeColor = theme.TextMuted;

            // App details: how it's running, the .NET runtime, plus where data lives.
            // Handy when someone files a bug report.
            string edition = Utils.DeploymentInfo.IsInstalled ? "Installed" : "Portable";
            string netVer;
            try { netVer = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription; }
            catch { netVer = ".NET"; }
            string builtAt = Utils.BuildInfo.BuiltUtcText;
            if (builtAt.Length > 0) { builtAt = "  \u00b7  built " + builtAt; }
            var buildInfo = UiFactory.Label($"{edition}  \u00b7  {netVer}{builtAt}", 24, 104, FontStyle.Regular, 9f);
            buildInfo.ForeColor = theme.TextMuted;

            string dataDir = Persistence.SettingsManager.GetSettingsDirectory();
            var dataInfo = UiFactory.Label("Data: " + dataDir, 24, 122, FontStyle.Regular, 9f);
            dataInfo.ForeColor = theme.TextMuted;
            dataInfo.Width = 264;
            dataInfo.AutoSize = false;
            dataInfo.AutoEllipsis = true;
            var openData = new LinkLabel
            {
                Text = Localization.T("Open data folder"),
                AutoSize = true,
                Location = new Point(24, 142),
                LinkColor = theme.Accent,
                ActiveLinkColor = theme.Accent,
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

            // A thin rule between the identity block and the blurb, so the dialog reads
            // as two parts instead of one long column of left-aligned text.
            var rule = new Panel
            {
                Left = 24,
                Top = 182,
                Width = 410,
                Height = 1,
                // Surface2, not Border: on the darker themes Border sits so close to the
                // page colour that the rule was invisible, which makes it decoration
                // that costs a row and buys nothing.
                BackColor = theme.Surface2
            };
            Controls.Add(rule);

            // The old text described a mouse auto-clicker and nothing else — accurate in
            // about 2021. Captions, notifications and the movement engine are most of
            // what Tempo is now, and none of them were mentioned.
            var lead = UiFactory.Label(
                "A clicking, macro and accessibility tool that runs entirely on your PC.",
                24, 232, FontStyle.Bold, 9.5f);
            lead.AutoSize = false;
            lead.Width = 410;
            // Two lines of room, not one. At 9.5pt bold this sentence needs ~490px and
            // the label is 410 wide, so a single 20px line clipped it mid-word ("…runs
            // entirely on"). Sizing for the wrap keeps it correct at other DPIs and font
            // scales too, where the break lands somewhere else again.
            lead.Height = 36;
            Controls.Add(lead);

            var description = new Label
            {
                Left = 24,
                Top = 274,
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

            var openLog = UiFactory.Button("Open Log Folder", 24, 414, 150, 32);
            openLog.BackColor = theme.Surface2;
            openLog.ForeColor = theme.Text;
            openLog.Click += (s, e) => OpenLogFolder();

            var ok = UiFactory.PrimaryButton("Close", 344, 414, 90, 32, theme);
            ok.Click += (s, e) => Close();

            var siteLink = new LinkLabel
            {
                Text = Localization.T("Website"),
                AutoSize = true,
                Location = new Point(190, 422),
                LinkColor = theme.Accent,
                ActiveLinkColor = theme.Accent,
                BackColor = Color.Transparent
            };
            siteLink.LinkClicked += (s, e) => OpenUrl(OfficialSourceForm.WebsiteUrl);
            Controls.Add(siteLink);

            var ghLink = new LinkLabel
            {
                // Not translated, deliberately: a brand name, like "Tempo" itself.
                Text = "GitHub",
                AutoSize = true,
                Location = new Point(258, 422),
                LinkColor = theme.Accent,
                ActiveLinkColor = theme.Accent,
                BackColor = Color.Transparent
            };
            ghLink.LinkClicked += (s, e) => OpenUrl(OfficialSourceForm.GitHubUrl);
            Controls.Add(ghLink);

            Controls.AddRange(new Control[] { logo, dropHint, chooseLogo, resetLogo, title, version, sysInfo, buildInfo, dataInfo, description, openLog, ok });

            AcceptButton = ok;
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
