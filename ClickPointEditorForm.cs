using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;

namespace AutoClicker.UI
{
    /// <summary>
    /// Modal dialog used to create or edit a single <see cref="ClickPoint"/> for
    /// the multi-point list.
    /// </summary>
    public sealed class ClickPointEditorForm : Form
    {
        private readonly Theme _theme;
        private readonly TextBox _labelText;
        private readonly NumericUpDown _xNum;
        private readonly NumericUpDown _yNum;
        private readonly ComboBox _buttonCombo;
        private readonly ComboBox _styleCombo;
        private readonly NumericUpDown _dwellNum;
        private readonly NumericUpDown _repeatNum;
        private readonly CheckBox _enabledCheck;

        public ClickPoint Result { get; private set; }

        public ClickPointEditorForm(Theme theme, ClickPoint existing)
        {
            _theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            Text = existing == null ? "Add Click Point" : "Edit Click Point";
            Size = new Size(340, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = _theme.Background;
            ForeColor = _theme.Text;
            Font = UiFactory.BodyFont;

            Controls.Add(UiFactory.Label("Label", 20, 18));
            _labelText = UiFactory.Text(120, 15, 180, existing?.Label ?? "Point");

            Controls.Add(UiFactory.Label("X", 20, 52));
            _xNum = UiFactory.Numeric(120, 49, 90, -100000, 100000, existing?.X ?? 0);

            Controls.Add(UiFactory.Label("Y", 20, 86));
            _yNum = UiFactory.Numeric(120, 83, 90, -100000, 100000, existing?.Y ?? 0);

            var pickBtn = UiFactory.Button("Pick…", 220, 49, 80, 56);
            pickBtn.Click += OnPick;

            Controls.Add(UiFactory.Label("Button", 20, 120));
            _buttonCombo = UiFactory.Combo(120, 117, 120, "Left", "Right", "Middle");
            _buttonCombo.SelectedIndex = existing == null ? 0 : (int)existing.Button;

            Controls.Add(UiFactory.Label("Type", 20, 154));
            _styleCombo = UiFactory.Combo(120, 151, 120, "Single", "Double", "Triple");
            _styleCombo.SelectedIndex = existing == null ? 0 : (int)existing.Style;

            Controls.Add(UiFactory.Label("Dwell (ms)", 20, 188));
            _dwellNum = UiFactory.Numeric(120, 185, 90, 0, 3600000, existing?.DwellMilliseconds ?? 0);

            Controls.Add(UiFactory.Label("Repeat (×)", 20, 222));
            _repeatNum = UiFactory.Numeric(120, 219, 90, 1, 100000, existing == null ? 1 : (existing.Repeat < 1 ? 1 : existing.Repeat));

            _enabledCheck = UiFactory.Check("Enabled", 120, 254, existing?.Enabled ?? true);

            var ok = UiFactory.PrimaryButton("OK", 120, 286, 90, 30, _theme);
            ok.Click += OnOk;
            var cancel = UiFactory.Button("Cancel", 220, 286, 90, 30);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(_labelText);
            Controls.Add(_xNum);
            Controls.Add(_yNum);
            Controls.Add(pickBtn);
            Controls.Add(_buttonCombo);
            Controls.Add(_styleCombo);
            Controls.Add(_dwellNum);
            Controls.Add(_repeatNum);
            Controls.Add(_enabledCheck);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            ThemeManager.Apply(this, _theme);
        }

        private void OnPick(object sender, EventArgs e)
        {
            Hide();
            System.Threading.Thread.Sleep(150);
            try
            {
                using (var picker = new CoordinatePickerForm(_theme))
                {
                    if (picker.ShowDialog() == DialogResult.OK)
                    {
                        _xNum.Value = Math.Min(Math.Max(picker.PickedX, _xNum.Minimum), _xNum.Maximum);
                        _yNum.Value = Math.Min(Math.Max(picker.PickedY, _yNum.Minimum), _yNum.Maximum);
                    }
                }
            }
            finally
            {
                Show();
                Activate();
            }
        }

        private void OnOk(object sender, EventArgs e)
        {
            Result = new ClickPoint
            {
                Label = string.IsNullOrWhiteSpace(_labelText.Text) ? "Point" : _labelText.Text.Trim(),
                X = (int)_xNum.Value,
                Y = (int)_yNum.Value,
                Button = (MouseButtonType)_buttonCombo.SelectedIndex,
                Style = (ClickStyle)_styleCombo.SelectedIndex,
                DwellMilliseconds = (int)_dwellNum.Value,
                Repeat = (int)_repeatNum.Value,
                Enabled = _enabledCheck.Checked
            };

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
