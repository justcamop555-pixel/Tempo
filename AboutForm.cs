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
        public AboutForm(Theme theme)
        {
            theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            Text = "About Tempo";
            Size = new Size(420, 320);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            var title = UiFactory.Label("Tempo", 24, 24, FontStyle.Bold, 18f);
            var version = UiFactory.Label("Version 1.0.25", 24, 60, FontStyle.Regular, 9.5f);
            version.ForeColor = theme.TextMuted;

            var description = new Label
            {
                Left = 24,
                Top = 96,
                Width = 360,
                Height = 96,
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = UiFactory.BodyFont,
                Text =
                    "A full-featured mouse auto-clicker.\n\n" +
                    "Features: configurable intervals, multi-point clicking, " +
                    "burst and hold modes, randomization, macro recording and " +
                    "playback, profiles, global hotkeys, and live statistics.\n\n" +
                    "Use responsibly and in accordance with the terms of service " +
                    "of any software you use it with."
            };

            var openLog = UiFactory.Button("Open Log Folder", 24, 208, 150, 32);
            openLog.BackColor = theme.Surface2;
            openLog.ForeColor = theme.Text;
            openLog.Click += (s, e) => OpenLogFolder();

            var ok = UiFactory.PrimaryButton("Close", 296, 208, 90, 32, theme);
            ok.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { title, version, description, openLog, ok });

            AcceptButton = ok;
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
