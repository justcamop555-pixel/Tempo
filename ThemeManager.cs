using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// Applies a <see cref="Theme"/> to a control hierarchy. Handles the common
    /// WinForms control types used in this application.
    /// </summary>
    public static class ThemeManager
    {
        public static void Apply(Control root, Theme theme)
        {
            if (root == null || theme == null)
            {
                return;
            }

            ApplyToControl(root, theme);

            foreach (Control child in root.Controls)
            {
                Apply(child, theme);
            }
        }

        private static void ApplyToControl(Control c, Theme theme)
        {
            switch (c)
            {
                case Form form:
                    form.BackColor = theme.Background;
                    form.ForeColor = theme.Text;
                    break;

                case Button button:
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = theme.Border;
                    button.FlatAppearance.BorderSize = 1;
                    button.BackColor = theme.Surface2;
                    button.ForeColor = theme.Text;
                    button.FlatAppearance.MouseOverBackColor = theme.AccentHover;
                    break;

                case TextBox textBox:
                    textBox.BackColor = theme.InputBackground;
                    textBox.ForeColor = theme.Text;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case NumericUpDown nud:
                    nud.BackColor = theme.InputBackground;
                    nud.ForeColor = theme.Text;
                    break;

                case ComboBox combo:
                    combo.BackColor = theme.InputBackground;
                    combo.ForeColor = theme.Text;
                    combo.FlatStyle = FlatStyle.Flat;
                    break;

                case ListBox list:
                    list.BackColor = theme.Surface2;
                    list.ForeColor = theme.Text;
                    list.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case ListView lv:
                    lv.BackColor = theme.Surface2;
                    lv.ForeColor = theme.Text;
                    break;

                case CheckBox cb:
                    cb.ForeColor = theme.Text;
                    cb.BackColor = Color.Transparent;
                    break;

                case RadioButton rb:
                    rb.ForeColor = theme.Text;
                    rb.BackColor = Color.Transparent;
                    break;

                case Label label:
                    label.ForeColor = theme.Text;
                    label.BackColor = Color.Transparent;
                    break;

                case CardGroupBox card:
                    card.ApplyTheme(theme);
                    break;

                case GroupBox group:
                    group.ForeColor = theme.TextMuted;
                    group.BackColor = Color.Transparent;
                    break;

                case TabControl tab:
                    tab.BackColor = theme.Background;
                    tab.ForeColor = theme.Text;
                    break;

                case BackdropTabPage backdrop:
                    backdrop.ApplyTheme(theme);
                    break;

                case TabPage page:
                    page.BackColor = theme.Background;
                    page.ForeColor = theme.Text;
                    break;

                case Panel panel:
                    // Leave panels that intentionally carry their own colour alone
                    // by only theming those tagged as themable.
                    if (panel.Tag is string tag && tag == "surface")
                    {
                        panel.BackColor = theme.Surface;
                    }
                    else if (panel.Tag is string tag2 && tag2 == "surface2")
                    {
                        panel.BackColor = theme.Surface2;
                    }
                    break;

                case StatusStrip strip:
                    strip.BackColor = theme.Surface;
                    strip.ForeColor = theme.Text;
                    break;
            }
        }
    }
}
