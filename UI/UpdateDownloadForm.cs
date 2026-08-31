using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// A small modal dialog that downloads the new build on a background thread
    /// while showing progress. Returns <see cref="DialogResult.OK"/> on success;
    /// the downloaded file path is in <see cref="DownloadedPath"/>.
    /// </summary>
    public sealed class UpdateDownloadForm : Form
    {
        private readonly string _url;
        private readonly string _destPath;
        private readonly string _sha256Url;
        private readonly Theme _theme;
        private volatile bool _cancelled;
        private Thread _worker;
        private DateTime _startedUtc;

        private readonly ThemedProgressBar _bar;
        private readonly Label _status;
        private readonly Button _cancelBtn;

        public string DownloadedPath { get; private set; }
        public string Error { get; private set; }

        public UpdateDownloadForm(Theme theme, string url, string destPath, Version version, string sha256Url = null)
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);
            _theme = theme;
            _url = url;
            _destPath = destPath;
            _sha256Url = sha256Url;

            Text = Utils.Localization.T("Downloading update");
            Size = new Size(440, 176);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = theme.Background;
            // Title bar in the theme too, so the dialog matches the app.
            ThemeManager.ApplyWindowChrome(this, theme);
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;
            ControlBox = false;

            var accentStrip = new Panel { Left = 0, Top = 0, Width = 440, Height = 3, BackColor = theme.Accent };
            Controls.Add(accentStrip);

            var title = UiFactory.Label(
                version != null ? "Downloading Tempo " + version + "…" : "Downloading update…",
                20, 18, FontStyle.Bold, 11f);
            Controls.Add(title);

            _bar = new ThemedProgressBar
            {
                Left = 20,
                Top = 56,
                Width = 392,
                Height = 18,
                Maximum = 100,
                Value = 0
            };
            _bar.ApplyTheme(theme);
            Controls.Add(_bar);

            _status = UiFactory.Label("Starting…", 20, 84, FontStyle.Regular, 9f);
            _status.ForeColor = theme.TextMuted;
            _status.AutoSize = false;
            _status.Width = 392;
            Controls.Add(_status);

            _cancelBtn = UiFactory.Button("Cancel", 332, 108, 80, 28);
            _cancelBtn.Click += (s, e) =>
            {
                _cancelled = true;
                _cancelBtn.Enabled = false;
                _status.Text = Localization.T("Cancelling…");
            };
            Controls.Add(_cancelBtn);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _startedUtc = DateTime.UtcNow;
            _worker = new Thread(() =>
            {
                // A setup .zip downloads as-is (its bytes start "PK", not "MZ"), so tell
                // Download to skip the executable-header and exe-checksum checks; the
                // caller unpacks it and verifies the extracted Tempo.exe afterwards.
                bool isArchive = !string.IsNullOrEmpty(_url) &&
                                 _url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                bool ok = UpdateInstaller.Download(
                    _url, _destPath,
                    OnProgress,
                    () => _cancelled,
                    out string error,
                    isArchive ? null : _sha256Url,
                    isArchive);

                BeginInvoke((Action)(() =>
                {
                    if (ok)
                    {
                        DownloadedPath = _destPath;
                        DialogResult = DialogResult.OK;
                    }
                    else
                    {
                        Error = error;
                        DialogResult = _cancelled ? DialogResult.Cancel : DialogResult.Abort;
                    }
                    Close();
                }));
            })
            {
                IsBackground = true,
                Name = "TempoUpdateDownload"
            };
            _worker.Start();
        }

        private void OnProgress(long read, long total)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                BeginInvoke((Action)(() =>
                {
                    double secs = (DateTime.UtcNow - _startedUtc).TotalSeconds;
                    double speed = secs > 0.2 ? read / secs : 0; // bytes/sec
                    string rate = speed > 0 ? "  ·  " + FormatBytes((long)speed) + "/s" : "";

                    if (total > 0)
                    {
                        int pct = (int)Math.Round(read * 100.0 / total);
                        if (pct < 0) pct = 0;
                        if (pct > 100) pct = 100;
                        _bar.Value = pct;

                        string eta = "";
                        if (speed > 0 && total > read)
                        {
                            double secsLeft = (total - read) / speed;
                            eta = "  ·  " + Localization.F("{0} left", FormatEta(secsLeft));
                        }
                        _status.Text = Localization.F("{0} of {1}  ({2}%){3}{4}",
                            FormatBytes(read), FormatBytes(total), pct, rate, eta);
                    }
                    else
                    {
                        _status.Text = Localization.F("Downloaded {0}…{1}", FormatBytes(read), rate);
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                // Window handle gone (closing); ignore.
            }
        }

        private static string FormatEta(double seconds)
        {
            if (seconds < 1) return "<1s";
            if (seconds < 60) return (int)Math.Round(seconds) + "s";
            int m = (int)(seconds / 60);
            int s = (int)Math.Round(seconds - m * 60);
            return $"{m}m {s:00}s";
        }

        private static string FormatBytes(long bytes)
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
            return value.ToString(unit == 0 ? "0" : "0.0") + " " + units[unit];
        }
    }
}
