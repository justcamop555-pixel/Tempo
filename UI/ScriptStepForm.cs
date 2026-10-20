using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutoClicker.Models;

namespace AutoClicker.UI
{
    /// <summary>
    /// Sets up a macro's Script step: which .py file to run, how long it may take, and
    /// what playback does if it fails.
    ///
    /// It also states, up front, which interpreter Tempo found — because "my script does
    /// nothing" is nearly always "Tempo is using a different Python from the one I
    /// installed my packages into", and that is invisible unless the window says so.
    ///
    /// And it can RUN the script, here, before it is ever part of a macro. Every failure a
    /// Script step can have — the wrong Python, a missing package, a crash on the first
    /// line — used to be discovered mid-macro, with the game in front and the reason in a
    /// log. A test run shows the output and the reason right here.
    /// </summary>
    public sealed class ScriptStepForm : Form
    {
        private readonly TextBox _path;
        private readonly NumericUpDown _timeout;
        private readonly ComboBox _onFailure;
        private readonly Label _interpreter;
        private readonly Button _testBtn;
        private readonly Label _testStatus;
        private readonly TextBox _output;
        private readonly Theme _theme;

        /// <summary>
        /// The whole failure reason on hover over the status line. One for the dialog: a new
        /// ToolTip per failed run leaked a native window each time, and the last failure's
        /// text went on showing over a later "✓ Finished".
        /// </summary>
        private readonly ToolTip _testTip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 350, ReshowDelay = 120 };

        /// <summary>Re-resolves the interpreter once typing in the path box settles.</summary>
        private readonly System.Windows.Forms.Timer _resolveDebounce;
        private int _resolveSeq;

        // The test run in flight, if any.
        private ManualResetEventSlim _testStop;
        private bool _testRunning;

        /// <summary>The configured step, once the dialog returns OK.</summary>
        public MacroAction Result { get; private set; }

        public ScriptStepForm(Theme theme, MacroAction existing)
        {
            // Built in 100% units; scaled with the screen at the end (UiFactory.ScaleToScreen).
            _theme = theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            Text = Utils.Localization.T("Run a Python script");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterParent;
            ScreenFit.Attach(this);   // on the screen, whole, however small it is (see ScreenFit)
            MaximizeBox = false;
            MinimizeBox = false;
            // 470, not 352: a test-run row and an output box below the interpreter rows.
            // The client area (was a 560×470 window): see UiFactory.ScaleToScreen.
            ClientSize = new Size(544, 431);
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            Controls.Add(UiFactory.Label("Script file:", 18, 20, FontStyle.Bold));
            _path = new TextBox
            {
                Left = 18,
                Top = 44,
                Width = 406,
                BackColor = theme.InputBackground,
                ForeColor = theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Text = existing != null ? (existing.ScriptPath ?? "") : ""
            };
            Controls.Add(_path);

            var browse = UiFactory.Button("Browse…", 432, 42, 96, 26);
            browse.BackColor = theme.Surface2;
            browse.ForeColor = theme.Text;
            browse.Click += (s, e) => Browse();
            Controls.Add(browse);

            Controls.Add(UiFactory.Label("Timeout (ms):", 18, 88));
            _timeout = new NumericUpDown
            {
                Left = 150,
                Top = 84,
                Width = 90,
                Minimum = 100,
                Maximum = 600000,
                Increment = 500,
                Value = Clamp(existing != null ? existing.ScriptTimeoutMs : 5000, 100, 600000),
                BackColor = theme.InputBackground,
                ForeColor = theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_timeout);

            Controls.Add(UiFactory.Label("If it fails:", 268, 88));
            // Tempo's flat combo, drawn in the theme: the stock flat one framed itself in the system's white
            // with a light-grey arrow button in a dark theme. 30 px tall (the stock one was 23), so 3 px higher
            // to stay level with its label.
            var onFailure = new FlatComboBox
            {
                Left = 356,
                Top = 81,
                Width = 172,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            onFailure.ApplyTheme(theme);
            _onFailure = onFailure;
            _onFailure.Items.Add(Utils.Localization.T("Stop the macro"));
            _onFailure.Items.Add(Utils.Localization.T("Carry on playing"));
            _onFailure.SelectedIndex =
                existing != null && existing.ScriptOnFailure == ScriptFailureAction.Continue ? 1 : 0;
            Controls.Add(_onFailure);

            var note = UiFactory.Label(
                "Tempo runs the script with your own Python and waits for it to finish. "
                + "Held keys and buttons are released first, so nothing stays pressed while it runs.",
                18, 124);
            note.MaximumSize = new Size(510, 0);
            note.AutoSize = true;
            note.ForeColor = theme.TextMuted;
            Controls.Add(note);

            _interpreter = UiFactory.Label("", 18, 176);
            // 380, not 430: the Rescan button beside it needs 120px for the longest
            // translation ("Procurar de novo" measures 106), and the label has to stop
            // short of it rather than run underneath.
            _interpreter.MaximumSize = new Size(380, 0);
            _interpreter.AutoSize = true;
            Controls.Add(_interpreter);

            var rescan = UiFactory.Button("Rescan", 416, 172, 120, 26);
            rescan.BackColor = theme.Surface2;
            rescan.ForeColor = theme.Text;
            rescan.Click += (s, e) =>
            {
                Utils.PythonRunner.Rescan();
                ShowInterpreter();
            };
            Controls.Add(rescan);

            // The answer to "Tempo is using a different Python from the one I installed my
            // packages into", which this dialog has always described and never let anyone
            // fix. Point it at a venv's python.exe and every script step uses it.
            var choose = UiFactory.Button("Choose…", 416, 204, 120, 26);
            choose.BackColor = theme.Surface2;
            choose.ForeColor = theme.Text;
            choose.Click += (s, e) => ChooseInterpreter();
            Controls.Add(choose);

            var auto = UiFactory.Button("Use automatic", 282, 204, 126, 26);
            auto.BackColor = theme.Surface2;
            auto.ForeColor = theme.Text;
            auto.Click += (s, e) =>
            {
                Utils.PythonRunner.ChoosePreferred("");
                ShowInterpreter();
            };
            Controls.Add(auto);

            // ── Test run ─────────────────────────────────────────────────────────
            _testBtn = UiFactory.Button("Test run", 18, 244, 120, 28);
            // "Stop test" while a test runs: measured both ways.
            LayoutFitter.DeclareCaptions(_testBtn, Utils.Localization.T("Test run"), Utils.Localization.T("Stop test"));
            _testBtn.BackColor = theme.Surface2;
            _testBtn.ForeColor = theme.Text;
            _testBtn.Click += (s, e) => ToggleTestRun();
            Controls.Add(_testBtn);

            _testStatus = UiFactory.Label("", 148, 250);
            _testStatus.AutoSize = false;
            _testStatus.AutoEllipsis = true;
            _testStatus.Size = new Size(380, 20);
            _testStatus.ForeColor = theme.TextMuted;
            Controls.Add(_testStatus);

            _output = new TextBox
            {
                Left = 18,
                Top = 280,
                Width = 510,
                Height = 104,
                Multiline = true,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = theme.InputBackground,
                ForeColor = theme.Text,
                Font = new Font("Consolas", 9f),
                TabStop = false
            };
            Controls.Add(_output);

            var ok = UiFactory.PrimaryButton("OK", 348, 394, 88, 30, theme);
            ok.Click += (s, e) => Accept();
            Controls.Add(ok);

            var cancel = UiFactory.Button("Cancel", 444, 394, 84, 30);
            cancel.BackColor = theme.Surface2;
            cancel.ForeColor = theme.Text;
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            // Which Python runs a script depends on WHERE the script is (a .venv beside it
            // wins), so the answer has to follow the path box. It used to be worked out once,
            // when the dialog opened — browse to a script with its own venv and the dialog
            // went on naming the system Python, which is exactly the confusion this line
            // exists to prevent.
            _resolveDebounce = new System.Windows.Forms.Timer { Interval = 400 };
            _resolveDebounce.Tick += (s, e) => { _resolveDebounce.Stop(); ShowInterpreter(); };
            _path.TextChanged += (s, e) => { _resolveDebounce.Stop(); _resolveDebounce.Start(); };

            Shown += (s, e) => ShowInterpreter();
            FormClosing += (s, e) =>
            {
                // A test still running is stopped with the dialog, not left behind.
                try { _testStop?.Set(); } catch { }
                try { _resolveDebounce.Stop(); } catch { }
            };
            UiFactory.ScaleToScreen(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _resolveDebounce?.Dispose(); } catch { }
                try { _testTip.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }

        private static decimal Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }

        private string CurrentPath => (_path.Text ?? "").Trim().Trim('"');

        /// <summary>
        /// Names the interpreter that will run THIS script, and why it was picked.
        ///
        /// Resolved against the script's own path rather than reported globally, so a
        /// virtual environment beside the script shows up here — which is the whole point:
        /// the difference between "Python 3.12" and "the Python that has your packages"
        /// is invisible until something says which one it is.
        ///
        /// OFF the UI thread. Resolving can mean probing several candidate executables — up
        /// to four seconds each — and doing that on the UI thread froze the dialog, and
        /// Tempo with it, for as long as it took.
        /// </summary>
        private void ShowInterpreter()
        {
            string path = CurrentPath;
            int seq = Interlocked.Increment(ref _resolveSeq);
            _interpreter.Text = Utils.Localization.T("Checking which Python will run it…");
            _interpreter.ForeColor = _theme.TextMuted;

            Task.Run(() =>
            {
                string exe = null, why = null;
                try { (exe, _, why) = Utils.PythonRunner.ResolveFor(path); }
                catch (Exception ex) { Utils.Logger.Swallow("ScriptStepForm.ShowInterpreter", ex); }

                try
                {
                    if (IsDisposed || !IsHandleCreated) { return; }
                    BeginInvoke((Action)(() =>
                    {
                        // A newer ask (the path changed again) wins; this answer is stale.
                        if (IsDisposed || seq != _resolveSeq) { return; }
                        bool have = !string.IsNullOrEmpty(exe);
                        _interpreter.Text = have
                            // One line, no embedded newline: a key with a line break in it is
                            // awkward to keep identical across five dictionaries, and the label
                            // wraps on its own anyway.
                            ? Utils.Localization.F("Using: {0} — {1}", exe, Utils.Localization.T(why ?? ""))
                            : Utils.Localization.T("No Python interpreter found on this PC.");
                        _interpreter.ForeColor = have ? _theme.SuccessText : _theme.WarningText;
                    }));
                }
                catch { /* the dialog closed while resolving */ }
            });
        }

        /// <summary>Starts a test run of the script in the box, or stops the one running.</summary>
        private void ToggleTestRun()
        {
            if (_testRunning)
            {
                try { _testStop?.Set(); } catch { }
                return;
            }

            string path = CurrentPath;
            if (path.Length == 0 || !System.IO.File.Exists(path))
            {
                _testStatus.Text = path.Length == 0
                    ? Utils.Localization.T("Choose a .py file for this step.")
                    : Utils.Localization.F("That file doesn't exist:\n\n{0}", path).Replace("\n\n", " ");
                _testStatus.ForeColor = _theme.WarningText;
                return;
            }

            int timeout = (int)_timeout.Value;
            _testRunning = true;
            _testStop = new ManualResetEventSlim(false);
            ManualResetEventSlim stop = _testStop;
            _testBtn.Text = Utils.Localization.T("Stop test");
            _testStatus.Text = Utils.Localization.T("Running…");
            _testStatus.ForeColor = _theme.TextMuted;
            TipStyle.Set(_testTip, _testStatus, "");
            _output.Clear();

            var options = new Utils.PythonRunner.RunOptions
            {
                // Output appears in the box as the script prints it, not all at the end.
                OnLine = line =>
                {
                    try
                    {
                        if (IsDisposed || !IsHandleCreated) { return; }
                        BeginInvoke((Action)(() =>
                        {
                            if (IsDisposed) { return; }
                            if (_output.TextLength < 60000) { _output.AppendText(line + Environment.NewLine); }
                        }));
                    }
                    catch { }
                },
                Environment = new System.Collections.Generic.Dictionary<string, string>
                {
                    // So a script can tell a dry run from the real thing if it cares to.
                    ["TEMPO_MACRO"] = "",
                    ["TEMPO_TEST_RUN"] = "1",
                }
            };

            Task.Run(() =>
            {
                Utils.PythonRunner.Result r = null;
                try { r = Utils.PythonRunner.Run(path, timeout, stop, options); }
                catch (Exception ex) { Utils.Logger.Swallow("ScriptStepForm.TestRun", ex); }

                try
                {
                    if (IsDisposed || !IsHandleCreated) { return; }
                    BeginInvoke((Action)(() => FinishTestRun(r)));
                }
                catch { }
                finally
                {
                    try { stop.Dispose(); } catch { }
                }
            });
        }

        private void FinishTestRun(Utils.PythonRunner.Result r)
        {
            if (IsDisposed) { return; }
            _testRunning = false;
            _testStop = null;
            _testBtn.Text = Utils.Localization.T("Test run");

            if (r == null)
            {
                _testStatus.Text = Utils.Localization.T("Python could not be started.");
                _testStatus.ForeColor = _theme.DangerText;
                return;
            }

            // The captured text is the complete record; the live lines above may have been
            // cut short by a script that printed faster than the box could keep up.
            if (!string.IsNullOrEmpty(r.Output)) { _output.Text = r.Output; }
            if (_output.TextLength == 0) { _output.Text = Utils.Localization.T("(no output)"); }

            switch (r.Outcome)
            {
                case Utils.PythonRunner.Outcome.Ok:
                    _testStatus.Text = "✓ " + Utils.Localization.F("Finished in {0} ms.", r.ElapsedMs);
                    _testStatus.ForeColor = _theme.SuccessText;
                    break;
                case Utils.PythonRunner.Outcome.Stopped:
                    _testStatus.Text = Utils.Localization.T("Stopped.");
                    _testStatus.ForeColor = _theme.TextMuted;
                    break;
                default:
                    // The reason is often two lines (the error, then how to fix it); the
                    // status shows the first, and the whole thing is on hover and in the box.
                    string detail = r.Detail ?? "";
                    string first = detail.Split('\n')[0].Trim();
                    _testStatus.Text = "✗ " + first;
                    _testStatus.ForeColor = _theme.DangerText;
                    TipStyle.Set(_testTip, _testStatus, detail);
                    if (detail.IndexOf('\n') >= 0)
                    {
                        _output.AppendText(Environment.NewLine + detail.Substring(detail.IndexOf('\n') + 1).Trim());
                    }
                    break;
            }
        }

        /// <summary>Lets the user point Tempo at a specific python.exe (usually a venv).</summary>
        private void ChooseInterpreter()
        {
            try
            {
                using (var dlg = new OpenFileDialog
                {
                    Title = Utils.Localization.T("Choose a Python interpreter"),
                    Filter = Utils.Localization.T("Python interpreter (python.exe)|python.exe|All programs (*.exe)|*.exe"),
                    CheckFileExists = true,
                })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                    Utils.PythonRunner.ChoosePreferred(dlg.FileName);
                    ShowInterpreter();
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("ScriptStepForm.ChooseInterpreter", ex); }
        }

        private void Browse()
        {
            try
            {
                using (var dlg = new OpenFileDialog
                {
                    Title = Utils.Localization.T("Choose a Python script"),
                    Filter = Utils.Localization.T("Python script (*.py;*.pyw)|*.py;*.pyw|All files (*.*)|*.*"),
                    CheckFileExists = true
                })
                {
                    try
                    {
                        string cur = _path.Text ?? "";
                        if (cur.Length > 0)
                        {
                            string dir = System.IO.Path.GetDirectoryName(cur);
                            if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                            {
                                dlg.InitialDirectory = dir;
                            }
                        }
                    }
                    catch { }

                    // The TextChanged handler re-resolves the interpreter for the new path.
                    if (dlg.ShowDialog(this) == DialogResult.OK) { _path.Text = dlg.FileName; }
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("ScriptStepForm.Browse", ex); }
        }

        /// <summary>
        /// Refuses to create a step that cannot possibly run. Catching an empty or missing
        /// path HERE, rather than at playback, is the difference between fixing it now and
        /// finding out mid-macro with keys held down.
        /// </summary>
        private void Accept()
        {
            string path = CurrentPath;
            if (path.Length == 0)
            {
                MessageBox.Show(this, Utils.Localization.T("Choose a .py file for this step."),
                    "Tempo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!System.IO.File.Exists(path))
            {
                MessageBox.Show(this,
                    Utils.Localization.F("That file doesn't exist:\n\n{0}", path),
                    "Tempo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Result = new MacroAction(MacroActionType.Script)
            {
                ScriptPath = path,
                ScriptTimeoutMs = (int)_timeout.Value,
                ScriptOnFailure = _onFailure.SelectedIndex == 1
                    ? ScriptFailureAction.Continue
                    : ScriptFailureAction.StopMacro
            };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
