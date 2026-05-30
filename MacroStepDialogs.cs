using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;

namespace AutoClicker.UI
{
    /// <summary>
    /// Lets the user choose a mouse button and whether to pick a screen position
    /// when inserting a click step into a macro.
    /// </summary>
    public sealed class InsertClickForm : Form
    {
        private readonly RadioButton _left;
        private readonly RadioButton _right;
        private readonly RadioButton _middle;
        private readonly CheckBox _pick;

        public MouseButtonType SelectedButton =>
            _right.Checked ? MouseButtonType.Right :
            _middle.Checked ? MouseButtonType.Middle :
            MouseButtonType.Left;

        public bool PickPosition => _pick.Checked;

        public InsertClickForm(Theme theme)
        {
            theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            Text = "Insert Click";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            Size = new Size(320, 210);
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            Controls.Add(UiFactory.Label("Button:", 18, 18, FontStyle.Bold));

            _left = new RadioButton { Text = "Left", Left = 28, Top = 44, Width = 80, Checked = true, ForeColor = theme.Text };
            _right = new RadioButton { Text = "Right", Left = 118, Top = 44, Width = 80, ForeColor = theme.Text };
            _middle = new RadioButton { Text = "Middle", Left = 208, Top = 44, Width = 80, ForeColor = theme.Text };

            _pick = new CheckBox
            {
                Text = "Pick position on screen (otherwise use current cursor)",
                Left = 20,
                Top = 84,
                Width = 280,
                Height = 40,
                ForeColor = theme.Text
            };

            var ok = UiFactory.PrimaryButton("Insert", 120, 134, 80, 30, theme);
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

            var cancel = UiFactory.Button("Cancel", 210, 134, 80, 30);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(_left);
            Controls.Add(_right);
            Controls.Add(_middle);
            Controls.Add(_pick);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            ThemeManager.Apply(this, theme);
        }
    }

    /// <summary>
    /// Captures a single key for inserting a key-press step into a macro.
    /// </summary>
    public sealed class KeyCaptureForm : Form
    {
        private readonly HotkeyCaptureControl _capture;

        /// <summary>The captured virtual-key code, or 0 if none.</summary>
        public int VirtualKey =>
            _capture.Hotkey != null && _capture.Hotkey.Key != Keys.None
                ? (int)_capture.Hotkey.Key
                : 0;

        public KeyCaptureForm(Theme theme)
        {
            theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            Text = "Insert Key Press";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            Size = new Size(340, 160);
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            var label = UiFactory.Label("Click the box and press a key:", 18, 18);
            label.AutoSize = true;

            _capture = new HotkeyCaptureControl { Left = 18, Top = 48, Width = 300, Font = UiFactory.BodyFont };

            var ok = UiFactory.PrimaryButton("Insert", 138, 88, 80, 30, theme);
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

            var cancel = UiFactory.Button("Cancel", 228, 88, 90, 30);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(label);
            Controls.Add(_capture);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            ThemeManager.Apply(this, theme);
        }
    }

    /// <summary>
    /// Two-numeric dialog for editing the X/Y of a move or click step.
    /// </summary>
    public sealed class PointInputForm : Form
    {
        private readonly NumericUpDown _x;
        private readonly NumericUpDown _y;

        public int X => (int)_x.Value;
        public int Y => (int)_y.Value;

        public PointInputForm(Theme theme, int x, int y)
        {
            theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            Text = "Edit Position";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            Size = new Size(280, 170);
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            var vs = SystemInformation.VirtualScreen;

            Controls.Add(UiFactory.Label("X:", 24, 26));
            _x = UiFactory.Numeric(70, 22, 160, vs.Left, vs.Right, Math.Min(Math.Max(x, vs.Left), vs.Right));

            Controls.Add(UiFactory.Label("Y:", 24, 60));
            _y = UiFactory.Numeric(70, 56, 160, vs.Top, vs.Bottom, Math.Min(Math.Max(y, vs.Top), vs.Bottom));

            var ok = UiFactory.PrimaryButton("OK", 90, 96, 80, 30, theme);
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

            var cancel = UiFactory.Button("Cancel", 180, 96, 80, 30);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(_x);
            Controls.Add(_y);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            ThemeManager.Apply(this, theme);
        }
    }
}
