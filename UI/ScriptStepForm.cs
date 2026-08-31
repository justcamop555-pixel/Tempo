using System;
using System.Drawing;
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
    /// </summary>
    public sealed class ScriptStepForm : Form
    {
        private readonly TextBox _path;
        private readonly NumericUpDown _timeout;
        private readonly ComboBox _onFailure;
        private readonly Label _interpreter;
        private readonly Theme _theme;

        /// <summary>The configured step, once the dialog returns OK.</summary>
        public MacroAction Result { get; private set; }

        public ScriptStepForm(Theme theme, MacroAction existing)
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            _theme = theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            Text = Utils.Localization.T("Run a Python script");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            Size = new Size(560, 320);
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
            _onFailure = new ComboBox
            {
                Left = 356,
                Top = 84,
                Width = 172,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = theme.InputBackground,
                ForeColor = theme.Text,
                FlatStyle = FlatStyle.Flat
            };
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

            var ok = UiFactory.PrimaryButton("OK", 348, 236, 88, 30, theme);
            ok.Click += (s, e) => Accept();
            Controls.Add(ok);

            var cancel = UiFactory.Button("Cancel", 444, 236, 84, 30);
            cancel.BackColor = theme.Surface2;
            cancel.ForeColor = theme.Text;
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            // Probing runs candidate executables, so it is done on Shown rather than in
            // the constructor — otherwise the dialog takes a visible beat to appear the
            // first time, on a machine with no Python (the slowest case, and the one where
            // the delay would be most confusing).
            Shown += (s, e) => ShowInterpreter();
        }

        private static decimal Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }

        private void ShowInterpreter()
        {
            try
            {
                bool have = !string.IsNullOrEmpty(Utils.PythonRunner.InterpreterPath);
                _interpreter.Text = have
                    ? Utils.Localization.F("Using: {0}", Utils.PythonRunner.DescribeInterpreter())
                    : Utils.Localization.T("No Python interpreter found on this PC.");
                _interpreter.ForeColor = have ? _theme.Success : _theme.Warning;
            }
            catch (Exception ex) { Utils.Logger.Swallow("ScriptStepForm.ShowInterpreter", ex); }
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
            string path = (_path.Text ?? "").Trim().Trim('"');
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
