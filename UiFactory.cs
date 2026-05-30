using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// Factory helpers that create consistently styled WinForms controls so the
    /// large form-building code stays readable.
    /// </summary>
    public static class UiFactory
    {
        public static readonly Font BodyFont = new Font("Segoe UI", 9f);
        public static readonly Font BoldFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font TitleFont = new Font("Segoe UI", 16f, FontStyle.Bold);
        public static readonly Font SubtitleFont = new Font("Segoe UI", 8.5f);
        public static readonly Font ButtonFont = new Font("Segoe UI", 10f, FontStyle.Bold);

        public static Label Label(string text, int x, int y, FontStyle style = FontStyle.Regular, float size = 9f)
        {
            return new Label
            {
                Text = text,
                Left = x,
                Top = y,
                AutoSize = true,
                Font = new Font("Segoe UI", size, style),
                BackColor = Color.Transparent
            };
        }

        public static Label Caption(string text, int x, int y)
        {
            var lbl = Label(text, x, y, FontStyle.Regular, 8.25f);
            return lbl;
        }

        public static NumericUpDown Numeric(int x, int y, int width, decimal min, decimal max, decimal value)
        {
            var nud = new NumericUpDown
            {
                Left = x,
                Top = y,
                Width = width,
                Minimum = min,
                Maximum = max,
                Font = BodyFont,
                BorderStyle = BorderStyle.FixedSingle
            };

            // Clamp the initial value into range to avoid an exception.
            if (value < min) value = min;
            if (value > max) value = max;
            nud.Value = value;
            return nud;
        }

        public static ComboBox Combo(int x, int y, int width, params string[] items)
        {
            var combo = new ComboBox
            {
                Left = x,
                Top = y,
                Width = width,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = BodyFont
            };

            if (items != null && items.Length > 0)
            {
                combo.Items.AddRange(items);
                combo.SelectedIndex = 0;
            }

            return combo;
        }

        public static CheckBox Check(string text, int x, int y, bool isChecked = false)
        {
            return new CheckBox
            {
                Text = text,
                Left = x,
                Top = y,
                AutoSize = true,
                Checked = isChecked,
                Font = BodyFont,
                BackColor = Color.Transparent
            };
        }

        public static RadioButton Radio(string text, int x, int y, bool isChecked = false)
        {
            return new RadioButton
            {
                Text = text,
                Left = x,
                Top = y,
                AutoSize = true,
                Checked = isChecked,
                Font = BodyFont,
                BackColor = Color.Transparent
            };
        }

        public static Button Button(string text, int x, int y, int width, int height)
        {
            var b = new Button
            {
                Text = text,
                Left = x,
                Top = y,
                Width = width,
                Height = height,
                FlatStyle = FlatStyle.Flat,
                Font = BodyFont,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        public static Button PrimaryButton(string text, int x, int y, int width, int height, Theme theme)
        {
            var b = Button(text, x, y, width, height);
            b.Font = ButtonFont;
            b.BackColor = theme.Accent;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = theme.AccentHover;
            return b;
        }

        public static GroupBox Group(string title, int x, int y, int width, int height)
        {
            return new GroupBox
            {
                Text = title,
                Left = x,
                Top = y,
                Width = width,
                Height = height,
                Font = BoldFont,
                BackColor = Color.Transparent
            };
        }

        public static TextBox Text(int x, int y, int width, string value = "")
        {
            return new TextBox
            {
                Left = x,
                Top = y,
                Width = width,
                Text = value ?? string.Empty,
                Font = BodyFont,
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        public static Panel Surface(int x, int y, int width, int height, string tag = "surface2")
        {
            return new Panel
            {
                Left = x,
                Top = y,
                Width = width,
                Height = height,
                Tag = tag
            };
        }
    }
}
