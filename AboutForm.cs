using System;
using System.Diagnostics;
using System.Drawing;
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
            AutoScaleMode = AutoScaleMode.Font;
            theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            Text = "About Tempo";
            Size = new Size(440, 388);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            // Animated logo (top-right). Drag an image/GIF onto it — or use the
            // "Choose image…" button below — to set your own logo.
            var logo = new PictureBox
            {
                Left = 300,
                Top = 20,
                Width = 104,
                Height = 104,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                AllowDrop = true,
                Cursor = Cursors.Hand
            };
            LoadLogo(logo);
            FormClosed += (s, e) =>
            {
                logo.Image = null;
                _gifStream?.Dispose();
            };

            var dropHint = UiFactory.Label(
                "Want your own logo? Click \"Choose image…\" and pick a .gif / .png / .jpg, " +
                "or drag an image straight onto the logo (even from a web page).",
                24, 110, FontStyle.Italic, 8f);
            dropHint.ForeColor = theme.TextMuted;
            dropHint.AutoSize = false;
            dropHint.Width = 264;
            dropHint.Height = 48;

            var chooseLogo = UiFactory.Button("Choose image…", 300, 128, 104, 26);
            chooseLogo.BackColor = theme.Surface2;
            chooseLogo.ForeColor = theme.Text;

            var resetLogo = new LinkLabel
            {
                Text = "Reset to default logo",
                AutoSize = true,
                Left = 300,
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
                    Title = "Choose a logo image",
                    Filter = "Images (*.png;*.gif;*.jpg;*.jpeg;*.bmp)|*.png;*.gif;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*"
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
                e.Effect = DropHasImage(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            logo.DragDrop += (s, e) =>
            {
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

                string url = null;
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.Text))
                {
                    string text = (e.Data.GetData(DataFormats.Text) as string)?.Trim();
                    if (!string.IsNullOrEmpty(text)) url = text.Split('\n', '\r')[0].Trim();
                }

                if (string.IsNullOrEmpty(url))
                {
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
            var version = UiFactory.Label("Version 1.0.115", 24, 64, FontStyle.Regular, 9.5f);
            version.ForeColor = theme.TextMuted;

            int hz = Utils.EnvironmentInfo.GetPrimaryRefreshHz();
            Screen ps = Screen.PrimaryScreen;
            string disp = ps != null
                ? $"Display: {ps.Bounds.Width}\u00d7{ps.Bounds.Height}" + (hz > 0 ? $" @ {hz} Hz" : "")
                : (hz > 0 ? $"Display: {hz} Hz" : "Display: unknown");
            var sysInfo = UiFactory.Label(disp, 24, 86, FontStyle.Regular, 9f);
            sysInfo.ForeColor = theme.TextMuted;

            var description = new Label
            {
                Left = 24,
                Top = 186,
                Width = 384,
                Height = 84,
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = UiFactory.BodyFont,
                Text =
                    "A full-featured mouse auto-clicker.\n\n" +
                    "Features: configurable intervals, multi-point clicking, " +
                    "burst and hold modes, randomization, macro recording and " +
                    "playback, profiles, global hotkeys, and live statistics."
            };

            var openLog = UiFactory.Button("Open Log Folder", 24, 290, 150, 32);
            openLog.BackColor = theme.Surface2;
            openLog.ForeColor = theme.Text;
            openLog.Click += (s, e) => OpenLogFolder();

            var ok = UiFactory.PrimaryButton("Close", 316, 290, 90, 32, theme);
            ok.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { logo, dropHint, chooseLogo, resetLogo, title, version, sysInfo, description, openLog, ok });

            AcceptButton = ok;
        }

        private static bool DropHasImage(IDataObject data)
        {
            if (data == null) return false;
            if (data.GetDataPresent(DataFormats.FileDrop)) return true;
            if (data.GetDataPresent(DataFormats.Text))
            {
                string t = data.GetData(DataFormats.Text) as string;
                return !string.IsNullOrWhiteSpace(t) &&
                       (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
            }
            return false;
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
    }
}
