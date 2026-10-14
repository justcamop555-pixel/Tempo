using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Settings ▸ Live Captions ▸ "Diagnose Windows bar": does Windows 11's own Live Captions bar work with
    /// Tempo on this PC, and if not, what to do about it.
    ///
    /// It used to be a bare text window built on the spot, opening with a raw dump, and every check ran ON
    /// THE UI THREAD — the UI Automation walks are cross-process calls that wait on Live Captions itself,
    /// so Tempo froze for as long as Windows took to answer. Now the checks run in the background (with a
    /// time limit) while this window says it is checking; the answer comes as a verdict with the button that
    /// fixes it, a checklist of what was found, and the technical detail underneath — which no longer lists
    /// the title of every window on the screen, since it is made to be copied into a bug report.
    /// </summary>
    public sealed class CaptionDiagnosticForm : Form
    {
        /// <summary>What the check found. Built off the UI thread by the caller's diagnose function.</summary>
        public sealed class Result
        {
            public bool Windows11, Installed, ProcessRunning, BarVisible, Found, UiaOk;
            /// <summary>Text read from the bar: true when the read itself worked (even with nothing said).</summary>
            public bool ReadOk;
            public int ReadChars;
            public long ReadMs;
            public bool MovedAsideByTempo, OffScreen;
            /// <summary>works, off, uia, unsupported, timeout or error.</summary>
            public string Verdict = "error";
            /// <summary>The technical report, safe to share.</summary>
            public string Report = "";
        }

        private const int TimeLimitMs = 20000;

        private readonly Theme _theme;
        private readonly Func<Result> _diagnose;
        private readonly Action _turnOnWindowsCaptions;
        private readonly Func<bool> _canUseTempoCaptions;
        private readonly Action _useTempoCaptions;

        private readonly Label _headline;
        private readonly Label _detail;
        private readonly Button _primary;
        private readonly Button _secondary;
        private readonly Button _again;
        private readonly Label[] _glyphs = new Label[6];
        private readonly Label[] _rows = new Label[6];
        private readonly TextBox _box;
        private readonly Button _copy;
        private readonly Button _save;
        private Action _primaryAction, _secondaryAction;
        private bool _running;
        private Result _last;

        private static readonly string[] RowLabels =
        {
            "Windows 11 with Live Captions",
            "Live Captions is on",
            "Tempo can see the bar",
            "Windows shares the caption text",
            "Read test",
            "Where the bar is",
        };

        /// <param name="diagnose">Runs the checks; called on a worker thread.</param>
        /// <param name="turnOnWindowsCaptions">Starts Windows Live Captions.</param>
        /// <param name="canUseTempoCaptions">Whether switching to Tempo's own engine would change anything (the source is Windows).</param>
        /// <param name="useTempoCaptions">Switches the caption source to Tempo's own engine.</param>
        public CaptionDiagnosticForm(Theme theme, Func<Result> diagnose, Action turnOnWindowsCaptions,
                                     Func<bool> canUseTempoCaptions, Action useTempoCaptions)
        {
            _theme = theme ?? Theme.ForKind(ThemeKind.Dark);
            _diagnose = diagnose;
            _turnOnWindowsCaptions = turnOnWindowsCaptions;
            _canUseTempoCaptions = canUseTempoCaptions ?? (() => false);
            _useTempoCaptions = useTempoCaptions;

            // Built in 100% units; scaled with the screen at the end (UiFactory.ScaleToScreen).
            Text = Localization.T("Windows Live Captions — diagnostic");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(720, 556);
            BackColor = _theme.Background;
            ForeColor = _theme.Text;
            Font = UiFactory.BodyFont;
            ThemeManager.ApplyWindowChrome(this, _theme);

            const int x0 = 20, w = 680;
            _headline = new Label
            {
                Location = new Point(x0, 16),
                Size = new Size(w, 26),
                Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold),
                AutoEllipsis = true,
            };
            Controls.Add(_headline);
            _detail = new Label
            {
                Location = new Point(x0, 46),
                Size = new Size(w, 50),
                Font = new Font("Segoe UI", 9f),
                ForeColor = _theme.TextMuted,
            };
            Controls.Add(_detail);

            _primary = UiFactory.PrimaryButton("Turn on Live Captions", x0, 102, 180, 30, _theme);
            _primary.Click += (s, e) => _primaryAction?.Invoke();
            Controls.Add(_primary);
            _secondary = UiFactory.Button("Use Tempo's own captions", x0 + 190, 102, 190, 30);
            _secondary.Click += (s, e) => _secondaryAction?.Invoke();
            Controls.Add(_secondary);
            _again = UiFactory.Button("Try again", x0 + 390, 102, 120, 30);
            _again.Click += (s, e) => RunCheck();
            Controls.Add(_again);
            // Each sized from its own words (German and French need more), then laid side by side.
            UiFactory.FitToText96(_primary, 180, 24);
            UiFactory.FitToText96(_secondary, 160, 24);
            UiFactory.FitToText96(_again, 110, 24);
            _secondary.Left = _primary.Right + 10;
            _again.Left = _secondary.Right + 10;

            for (int i = 0; i < _rows.Length; i++)
            {
                int y = 146 + i * 24;
                _glyphs[i] = new Label
                {
                    Location = new Point(x0 + 4, y),
                    Size = new Size(22, 22),
                    Font = new Font("Segoe UI Symbol", 10f, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleCenter,
                };
                _rows[i] = new Label
                {
                    Location = new Point(x0 + 30, y),
                    Size = new Size(w - 30, 22),
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true,
                };
                Controls.Add(_glyphs[i]);
                Controls.Add(_rows[i]);
            }

            _box = new TextBox
            {
                Location = new Point(x0, 300),
                Size = new Size(w, 200),
                Multiline = true,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 9f),
                BackColor = _theme.InputBackground,
                ForeColor = _theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
            };
            Controls.Add(_box);

            _copy = UiFactory.Button("Copy all", x0, 512, 110, 30);
            LayoutFitter.DeclareCaptions(_copy, Localization.T("Copy all"), Localization.T("Copied"), Localization.T("Copy failed"));
            _copy.Click += (s, e) => CopyReport();
            Controls.Add(_copy);
            _save = UiFactory.Button("Save…", x0 + 120, 512, 100, 30);
            _save.Click += (s, e) => SaveReport();
            Controls.Add(_save);
            var close = UiFactory.PrimaryButton("Close", 600, 512, 100, 30, _theme);
            close.DialogResult = DialogResult.OK;
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;
            UiFactory.FitToText96(_copy, 110, 22);
            UiFactory.FitToText96(_save, 100, 22);
            UiFactory.FitToText96(close, 100, 22);
            _save.Left = _copy.Right + 10;
            close.Left = x0 + w - close.Width;

            ThemeManager.Apply(this, _theme);
            // After the theme pass, which gives every label the page colours.
            _detail.ForeColor = _theme.TextMuted;
            _box.BackColor = _theme.InputBackground;
            ShowChecking();
            Shown += (s, e) => RunCheck();
            UiFactory.ScaleToScreen(this);
            _rowLeft = _primary.Left;   // where the button row starts, at the window's scale
        }

        private int _rowLeft;

        // ── the check ───────────────────────────────────────────────────────────────

        private void ShowChecking()
        {
            _headline.Text = Localization.T("Checking…");
            _headline.ForeColor = _theme.Text;
            _detail.Text = "";
            for (int i = 0; i < _rows.Length; i++) { SetRow(i, null, Localization.T(RowLabels[i])); }
            _primary.Visible = _secondary.Visible = false;
            _again.Enabled = _copy.Enabled = _save.Enabled = false;
            _box.Text = "";
        }

        /// <summary>Runs the checks off the UI thread, with a time limit: Windows' accessibility calls can hang.</summary>
        private async void RunCheck()
        {
            if (_running || _diagnose == null) { return; }
            _running = true;
            ShowChecking();
            Result result;
            try
            {
                Task<Result> work = Task.Run(_diagnose);
                Task done = await Task.WhenAny(work, Task.Delay(TimeLimitMs));
                if (done == work && work.Status == TaskStatus.RanToCompletion && work.Result != null)
                {
                    result = work.Result;
                }
                else if (done == work && work.IsFaulted)
                {
                    result = new Result { Verdict = "error", Report = "The check failed: " + work.Exception?.GetBaseException().Message };
                }
                else
                {
                    result = new Result { Verdict = "timeout", Report = "Windows did not answer within " + TimeLimitMs / 1000 + " s (UI Automation)." };
                }
            }
            catch (Exception ex) { result = new Result { Verdict = "error", Report = "The check failed: " + ex.Message }; }
            _running = false;
            if (IsDisposed) { return; }
            ShowResult(result);
        }

        /// <summary>The verdict, its fix, the checklist and the details — for the tests too.</summary>
        internal void ShowResult(Result r)
        {
            _last = r ?? new Result();
            r = _last;
            Color ok = _theme.Success, warn = _theme.WarningText;
            _primaryAction = _secondaryAction = null;
            bool offerTempo = _useTempoCaptions != null && _canUseTempoCaptions();
            switch (r.Verdict)
            {
                case "works":
                    Say("Windows Live Captions works with Tempo", "Tempo finds the Windows bar and can read its captions.", ok);
                    break;
                case "off":
                    Say("Live Captions isn't on", "Turn it on and check again, or let Tempo caption by itself.", warn);
                    if (_turnOnWindowsCaptions != null) { _primaryAction = TurnOnAndRecheck; }
                    if (offerTempo) { _secondaryAction = UseTempo; }
                    break;
                case "uia":
                    Say("Windows won't share the captions",
                        "The bar is there, but Windows' accessibility service won't hand its text to Tempo. "
                        + "Signing out of Windows and back in usually fixes it.", warn);
                    if (offerTempo) { _secondaryAction = UseTempo; }
                    break;
                case "unsupported":
                    Say("This PC has no Windows Live Captions",
                        "Live Captions comes with Windows 11 (version 22H2 and later). Tempo's own captions work without it.", warn);
                    if (offerTempo) { _secondaryAction = UseTempo; }
                    break;
                default:
                    Say("The check didn't finish", "Windows didn't answer within 20 seconds. Try again in a moment.", _theme.Danger);
                    break;
            }
            // Side by side from the left, the offered ones first. Decided from the actions, not from Visible: a
            // window not on screen yet reports every control invisible. The hidden ones queue after them in slots
            // of their own rather than under them — a hidden button parked beneath a shown one is still an overlap
            // to the layout checks (they judge hidden by size, for exactly that never-shown reason).
            bool primary = _primaryAction != null, secondary = _secondaryAction != null;
            _primary.Visible = primary;
            _secondary.Visible = secondary;
            int gap = (int)Math.Round(10 * ScaleOf());
            var order = new System.Collections.Generic.List<Button>(3);
            if (primary) { order.Add(_primary); }
            if (secondary) { order.Add(_secondary); }
            order.Add(_again);
            if (!primary) { order.Add(_primary); }
            if (!secondary) { order.Add(_secondary); }
            int x = _rowLeft;
            foreach (Button b in order)
            {
                b.Left = x;
                x = b.Right + gap;
            }

            bool checkedBar = r.Windows11 && r.Installed;
            SetRow(0, checkedBar, Localization.T(RowLabels[0]));
            SetRow(1, checkedBar ? r.BarVisible || r.Found : (bool?)null, Localization.T(RowLabels[1]));
            SetRow(2, checkedBar ? r.Found : (bool?)null, Localization.T(RowLabels[2]));
            SetRow(3, r.Found ? r.UiaOk : (bool?)null, Localization.T(RowLabels[3]));
            if (r.Found && r.UiaOk && r.ReadOk)
            {
                SetRow(4, true, r.ReadChars > 0
                    ? Localization.F("Read {0} characters in {1} ms", r.ReadChars, r.ReadMs)
                    : Localization.T("Nothing is being said right now"));
            }
            else if (r.Found && r.UiaOk) { SetRow(4, false, Localization.T("The read failed")); }
            else { SetRow(4, null, Localization.T(RowLabels[4])); }
            if (!r.Found) { SetRow(5, null, Localization.T(RowLabels[5])); }
            else if (r.MovedAsideByTempo) { SetRow(5, true, Localization.T("Tempo has moved the bar out of sight; its own bar shows the captions")); }
            else if (r.OffScreen) { SetRow(5, false, Localization.T("The bar is off screen")); }
            else { SetRow(5, true, Localization.T("The bar is on screen")); }

            _box.Text = r.Report ?? "";
            _again.Enabled = _copy.Enabled = _save.Enabled = true;
        }

        private void Say(string headline, string detail, Color colour)
        {
            _headline.Text = Localization.T(headline);
            _headline.ForeColor = colour;
            _detail.Text = Localization.T(detail);
        }

        /// <summary>One checklist row (<paramref name="text"/> already translated): ✔ found, ✖ not, – not checked (an earlier step failed).</summary>
        private void SetRow(int i, bool? state, string text)
        {
            _glyphs[i].Text = state == null ? "–" : state.Value ? "✔" : "✖";
            _glyphs[i].ForeColor = state == null ? _theme.TextMuted : state.Value ? _theme.Success : _theme.WarningText;
            _rows[i].Text = text;
            _rows[i].ForeColor = state == null ? _theme.TextMuted : _theme.Text;
        }

        /// <summary>The window's scale (the buttons were built at 100% and scaled with it).</summary>
        private double ScaleOf() => _box.Width / 680.0;

        private async void TurnOnAndRecheck()
        {
            try { _turnOnWindowsCaptions?.Invoke(); } catch (Exception ex) { Logger.Swallow("CaptionDiagnostic.TurnOn", ex); }
            _primary.Enabled = false;
            // Live Captions takes a moment to put its bar up (longer the first time, while it fetches its speech pack).
            await Task.Delay(3500);
            if (IsDisposed) { return; }
            _primary.Enabled = true;
            RunCheck();
        }

        private void UseTempo()
        {
            try { _useTempoCaptions?.Invoke(); } catch (Exception ex) { Logger.Swallow("CaptionDiagnostic.UseTempo", ex); }
            _secondary.Visible = false;
        }

        private void CopyReport()
        {
            try
            {
                Clipboard.SetText(FullReport());
                _copy.Text = Localization.T("Copied");
            }
            catch { _copy.Text = Localization.T("Copy failed"); }
        }

        private void SaveReport()
        {
            try
            {
                using (var dlg = new SaveFileDialog
                {
                    FileName = "Tempo - Windows Live Captions check.txt",
                    Filter = "Text (*.txt)|*.txt",
                    OverwritePrompt = true,
                })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                    System.IO.File.WriteAllText(dlg.FileName, FullReport(), System.Text.Encoding.UTF8);
                }
            }
            catch (Exception ex) { Logger.Warn("[Captions] could not save the diagnostic: " + ex.Message); }
        }

        /// <summary>What is copied or saved: the verdict and checklist as shown, then the details.</summary>
        internal string FullReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(_headline.Text);
            if (_detail.Text.Length > 0) { sb.AppendLine(_detail.Text); }
            sb.AppendLine();
            for (int i = 0; i < _rows.Length; i++) { sb.AppendLine(_glyphs[i].Text + " " + _rows[i].Text); }
            sb.AppendLine();
            sb.Append(_box.Text);
            return sb.ToString();
        }
    }
}
