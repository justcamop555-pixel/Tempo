using System;
using System.Threading;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// A small modal that downloads a Whisper speech model into the models folder
    /// with a progress bar, so users can set up Tempo's own captions with one click
    /// instead of finding and copying a file by hand. Returns
    /// <see cref="DialogResult.OK"/> on success.
    /// </summary>
    public sealed class ModelDownloadForm : Form
    {
        private readonly WhisperModelInfo _model;
        private readonly Label _status;
        private readonly ProgressBar _bar;
        private readonly Button _cancelBtn;
        private Thread _worker;
        private volatile bool _cancelled;
        private DateTime _started;

        public string Error { get; private set; }

        public ModelDownloadForm(Theme theme, WhisperModelInfo model)
        {
            _model = model;
            var t = theme ?? Theme.ForKind(ThemeKind.Dark);

            Text = Utils.Localization.T("Downloading speech model");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new System.Drawing.Size(440, 150);
            BackColor = t.Background;
            // Title bar in the theme too, so the dialog matches the app.
            ThemeManager.ApplyWindowChrome(this, t);
            ForeColor = t.Text;

            var title = new Label
            {
                Text = model != null
                    ? Utils.Localization.F("Getting the {0} model…", model.Label)
                    : Utils.Localization.T("Downloading…"),
                Left = 18, Top = 18, Width = 404, Height = 24,
                Font = new System.Drawing.Font("Segoe UI", 10.5f, System.Drawing.FontStyle.Bold),
                ForeColor = t.Text
            };
            Controls.Add(title);

            _status = new Label
            {
                Text = Localization.T("Starting…"),
                Left = 18, Top = 50, Width = 404, Height = 20,
                ForeColor = t.TextMuted
            };
            Controls.Add(_status);

            _bar = new ProgressBar
            {
                Left = 18, Top = 78, Width = 404, Height = 18,
                Style = ProgressBarStyle.Continuous,
                Minimum = 0, Maximum = 100
            };
            Controls.Add(_bar);

            _cancelBtn = new Button
            {
                Text = Localization.T("Cancel"),
                Left = 342, Top = 108, Width = 80, Height = 30,
                DialogResult = DialogResult.None
            };
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
            _started = DateTime.UtcNow;
            _worker = new Thread(() =>
            {
                bool ok = WhisperModelManager.Download(_model, OnProgress, () => _cancelled, out string error);
                BeginInvoke((Action)(() =>
                {
                    if (ok)
                    {
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
                Name = "TempoModelDownload"
            };
            _worker.Start();
        }

        private long _lastProgressTick;
        private int _lastProgressPct = -1;
        /// <summary>Bytes already on disk when this attempt began; -1 until the first report.</summary>
        private long _baseline = -1;

        private void OnProgress(long read, long total)
        {
            if (IsDisposed) return;

            // Report at most ~10 times a second, and only when the percentage actually
            // moved. This fires once per 128 KB read on the download thread, so a 1.6 GB
            // model produced about 12,800 of them — each one a cross-thread BeginInvoke
            // that repainted a progress bar and relaid a label. That flooded the UI
            // thread for the entire download and made the whole app feel stuck while a
            // model came down.
            int pct = total > 0 ? (int)(read * 100 / total) : -1;
            long now = Environment.TickCount64;
            bool due = now - _lastProgressTick >= 100 || pct != _lastProgressPct || read >= total;
            if (!due) { return; }
            _lastProgressTick = now;
            _lastProgressPct = pct;

            try
            {
                BeginInvoke((Action)(() =>
                {
                    // Measure the rate from bytes fetched THIS attempt. A resumed
                    // download arrives with the on-disk bytes already counted in `read`,
                    // and dividing those by this attempt's elapsed time would report an
                    // absurd speed (a gigabyte "in" two seconds) and an ETA to match.
                    if (_baseline < 0) { _baseline = read; }
                    double secs = (DateTime.UtcNow - _started).TotalSeconds;
                    double thisRun = Math.Max(0, read - _baseline);
                    double speed = secs > 0.2 ? thisRun / secs : 0;
                    string rate = speed > 0 ? "  \u00b7  " + FormatBytes((long)speed) + "/s" : "";

                    if (total > 0)
                    {
                        int shown = (int)Math.Round(read * 100.0 / total);
                        if (shown < 0) shown = 0;
                        if (shown > 100) shown = 100;
                        _bar.Value = shown;

                        // Time remaining, from the rate measured so far. On a 1.6 GB
                        // model the difference between "two minutes" and "half an hour"
                        // is the difference between waiting and giving up.
                        string eta = "";
                        if (speed > 1024 && read < total)
                        {
                            var left = TimeSpan.FromSeconds((total - read) / speed);
                            string clock = left.TotalHours >= 1
                                ? ((int)left.TotalHours) + "h " + left.Minutes + "m"
                                : left.TotalMinutes >= 1
                                    ? left.Minutes + "m " + left.Seconds + "s"
                                    : Math.Max(1, left.Seconds) + "s";
                            eta = "  ·  " + Localization.F("{0} left", clock);
                        }
                        _status.Text = Localization.F("{0} of {1}  ({2}%){3}{4}",
                            FormatBytes(read), FormatBytes(total), shown, rate, eta);
                    }
                    else
                    {
                        _bar.Style = ProgressBarStyle.Marquee;
                        _status.Text = Localization.F("Downloaded {0}…{1}", FormatBytes(read), rate);
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                // Window closing; ignore.
            }
        }

        private static string FormatBytes(long value)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double v = value;
            int unit = 0;
            while (v >= 1024 && unit < units.Length - 1)
            {
                v /= 1024;
                unit++;
            }
            return v.ToString(unit == 0 ? "0" : "0.0") + " " + units[unit];
        }
    }
}
