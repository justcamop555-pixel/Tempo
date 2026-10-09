using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// A small modal dialog that downloads the new build on a background thread while showing
    /// progress, then puts a verified copy beside Tempo.exe ready to swap in. Returns
    /// <see cref="DialogResult.OK"/> on success with the ready file in <see cref="StagedPath"/>.
    ///
    /// Everything slow happens on the worker — the download, the checks, unpacking a zip and the
    /// 100 MB copy beside the exe — so the window keeps painting and Cancel keeps working.
    /// Cancel aborts the network read itself; it used to set a flag the download only looked at
    /// between reads, which on a stalled line meant up to a minute of "Cancelling…".
    /// </summary>
    public sealed class UpdateDownloadForm : Form
    {
        private readonly string _url;
        private readonly string _destPath;
        private readonly string _sha256Url;
        private readonly string _expectedSha256;
        private readonly bool _isArchive;
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private Thread _worker;

        // Speed shown as a moving average: the average since the start said 4 MB/s for a long
        // time after the line had dropped to a trickle, and the time left with it.
        private DateTime _lastSampleUtc;
        private long _lastSampleBytes = -1;
        private double _bytesPerSecond;

        private UpdateInstaller.DownloadPhase _phase = UpdateInstaller.DownloadPhase.Connecting;
        private int _attempt = 1;
        private int _attempts = UpdateInstaller.MaxDownloadAttempts;
        private long _read;
        private long _total = -1;

        private readonly ThemedProgressBar _bar;
        private readonly Label _status;
        private readonly Button _cancelBtn;

        /// <summary>The verified new build, beside Tempo.exe, once the dialog closes with OK.</summary>
        public string StagedPath { get; private set; }

        /// <summary>Kept for callers that only need to know a file arrived.</summary>
        public string DownloadedPath => StagedPath;

        public string Error { get; private set; }

        public UpdateDownloadForm(Theme theme, string url, string destPath, Version version, string sha256Url = null,
                                  string expectedSha256 = null)
        {
            // Built in 100% units; scaled with the screen at the end (UiFactory.ScaleToScreen).
            theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);
            _url = url;
            _destPath = destPath;
            _sha256Url = sha256Url;
            _expectedSha256 = expectedSha256;
            // A setup .zip downloads as-is (its bytes start "PK", not "MZ"); it is unpacked and
            // its Tempo.exe checked before anything is staged.
            _isArchive = !string.IsNullOrEmpty(url) && url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

            Text = Localization.T("Downloading update");
            ClientSize = new Size(440, 146);
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

            // Through F, with the version as a value: the title was English glued to a number,
            // which no dictionary entry could ever match.
            var title = new Label
            {
                Text = version != null
                    ? Localization.F("Downloading Tempo {0}…", UpdateChecker.Display(version))
                    : Localization.T("Downloading update…"),
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = theme.Text,
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(20, 16),
                Size = new Size(400, 26)
            };
            Controls.Add(title);

            _bar = new ThemedProgressBar
            {
                Left = 20,
                Top = 52,
                Width = 400,
                Height = 18,
                Maximum = 100,
                Value = 0
            };
            _bar.ApplyTheme(theme);
            Controls.Add(_bar);

            _status = new Label
            {
                Text = Localization.T("Connecting…"),
                Font = new Font("Segoe UI", 9f),
                ForeColor = theme.TextMuted,
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(20, 78),
                Size = new Size(400, 20)
            };
            Controls.Add(_status);

            _cancelBtn = UiFactory.Button("Cancel", 340, 106, 80, 28);
            _cancelBtn.Click += (s, e) => CancelDownload();
            Controls.Add(_cancelBtn);
            // Escape cancels too — there is no close box on this window.
            CancelButton = _cancelBtn;
            // Assigning CancelButton quietly gives the button DialogResult.Cancel, which would
            // close the window at the click while the worker is still mid-read. The worker
            // closes it instead, once the download has actually stopped.
            _cancelBtn.DialogResult = DialogResult.None;
            UiFactory.ScaleToScreen(this);
        }

        private void CancelDownload()
        {
            if (_cancel.IsCancellationRequested) { return; }
            _cancel.Cancel();
            _cancelBtn.Enabled = false;
            _status.Text = Localization.T("Cancelling…");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Closing any other way (Alt+F4) while the worker runs would leave it downloading
            // into a dialog that no longer exists. Treat it as Cancel and let the worker close us.
            if (_worker != null && _worker.IsAlive && DialogResult == DialogResult.None)
            {
                e.Cancel = true;
                CancelDownload();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _cancel.Cancel(); } catch { }
                try { _cancel.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _worker = new Thread(Work)
            {
                IsBackground = true,
                Name = "TempoUpdateDownload"
            };
            _worker.Start();
        }

        private void Work()
        {
            string error;
            string staged = null;
            bool ok = UpdateInstaller.Download(
                _url, _destPath,
                OnProgress,
                OnPhase,
                null,
                _cancel.Token,
                out error,
                _isArchive ? null : _sha256Url,
                _isArchive,
                _expectedSha256);

            if (ok && !_cancel.IsCancellationRequested)
            {
                Post(() =>
                {
                    _bar.Value = 100;
                    _status.Text = Localization.T("Preparing the update…");
                    _cancelBtn.Enabled = false;   // past the point where cancelling saves anything
                });
                ok = UpdateInstaller.PrepareStagedUpdate(_destPath, _isArchive, _sha256Url, out staged, out error);
            }
            else if (ok)
            {
                // Finished at the same moment Cancel was pressed: the user said no, so honour it.
                ok = false;
                error = Localization.T("The download was cancelled.");
            }

            Post(() =>
            {
                if (ok)
                {
                    StagedPath = staged;
                    DialogResult = DialogResult.OK;
                }
                else
                {
                    Error = error;
                    DialogResult = _cancel.IsCancellationRequested ? DialogResult.Cancel : DialogResult.Abort;
                }
                Close();
            });
        }

        private void Post(Action action)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated) { BeginInvoke(action); }
            }
            catch (InvalidOperationException) { /* closing (ObjectDisposedException is one of these) */ }
        }

        private void OnPhase(UpdateInstaller.DownloadPhase phase, int attempt, int attempts)
        {
            _phase = phase;
            if (attempt > 0) { _attempt = attempt; _attempts = attempts; }
            Post(RefreshStatus);
        }

        private void OnProgress(long read, long total)
        {
            _read = read;
            _total = total;
            Post(RefreshStatus);
        }

        private void RefreshStatus()
        {
            if (IsDisposed || _cancel.IsCancellationRequested) { return; }

            DateTime now = DateTime.UtcNow;
            long read = _read;
            long total = _total;

            if (_lastSampleBytes < 0 || read < _lastSampleBytes)
            {
                _lastSampleBytes = read;
                _lastSampleUtc = now;
            }
            else
            {
                double secs = (now - _lastSampleUtc).TotalSeconds;
                if (secs >= 0.5)
                {
                    double sample = (read - _lastSampleBytes) / secs;
                    _bytesPerSecond = _bytesPerSecond <= 0 ? sample : _bytesPerSecond * 0.7 + sample * 0.3;
                    _lastSampleBytes = read;
                    _lastSampleUtc = now;
                }
            }

            if (total > 0)
            {
                int pct = (int)Math.Round(read * 100.0 / total);
                _bar.Value = Math.Max(0, Math.Min(100, pct));
            }

            switch (_phase)
            {
                case UpdateInstaller.DownloadPhase.Connecting:
                    _status.Text = Localization.T("Connecting…");
                    return;
                case UpdateInstaller.DownloadPhase.Resuming:
                    _status.Text = Localization.F("Connection lost — resuming (try {0} of {1})…", _attempt, _attempts);
                    return;
                case UpdateInstaller.DownloadPhase.Verifying:
                    _status.Text = Localization.T("Checking the download…");
                    return;
            }

            string rate = _bytesPerSecond > 0 ? "  ·  " + UpdatePromptForm.FormatBytes((long)_bytesPerSecond) + "/s" : "";
            if (total > 0)
            {
                int pct = Math.Max(0, Math.Min(100, (int)Math.Round(read * 100.0 / total)));
                string eta = "";
                if (_bytesPerSecond > 0 && total > read)
                {
                    eta = "  ·  " + Localization.F("{0} left", FormatEta((total - read) / _bytesPerSecond));
                }
                _status.Text = Localization.F("{0} of {1}  ({2}%){3}{4}",
                    UpdatePromptForm.FormatBytes(read), UpdatePromptForm.FormatBytes(total), pct, rate, eta);
            }
            else
            {
                _status.Text = Localization.F("Downloaded {0}…{1}", UpdatePromptForm.FormatBytes(read), rate);
            }
        }

        private static string FormatEta(double seconds)
        {
            if (seconds < 1) return "<1s";
            if (seconds < 60) return (int)Math.Round(seconds) + "s";
            int m = (int)(seconds / 60);
            int s = (int)Math.Round(seconds - m * 60);
            if (s == 60) { m++; s = 0; }
            return $"{m}m {s:00}s";
        }
    }
}
