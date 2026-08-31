using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// A small dialog for customising the on-screen "running" badge: which screen
    /// corner it sits in, how opaque it is, and which of clicks / CPS / elapsed time
    /// it shows. Kept in its own dialog so the (already packed) Settings tab only
    /// needs one "Customise…" button.
    ///
    /// The dialog edits copies; the caller reads the public properties on
    /// <see cref="DialogResult.OK"/> and applies them.
    /// </summary>
    public sealed class OverlaySettingsForm : Form
    {
        private readonly FlatComboBox _corner;
        private readonly SmoothTrackBar _opacity;
        private readonly Label _opacityValue;
        private readonly CheckBox _showClicks;
        private readonly CheckBox _showCps;
        private readonly CheckBox _showElapsed;

        public int Corner => _corner.SelectedIndex < 0 ? 0 : _corner.SelectedIndex;
        public int Opacity => _opacity.Value;
        public bool ShowClicks => _showClicks.Checked;
        public bool ShowCps => _showCps.Checked;
        public bool ShowElapsed => _showElapsed.Checked;

        public OverlaySettingsForm(Theme theme, int corner, int opacity,
            bool showClicks, bool showCps, bool showElapsed)
        {
            theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            Text = Localization.T("Tempo — running overlay");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(380, 268);
            BackColor = theme.Background;
            // Title bar in the theme too, so the dialog matches the app.
            ThemeManager.ApplyWindowChrome(this, theme);
            ForeColor = theme.Text;
            // The shared font instance, not a private "Segoe UI" 9pt: this one was
            // constructed per dialog and never disposed, and it hard-coded a family the
            // rest of the app reaches through UiFactory.
            Font = UiFactory.BodyFont;

            Controls.Add(UiFactory.Label("Position:", 16, 18));
            // FlatComboBox, not the framework ComboBox. A plain ComboBox with
            // FlatStyle.Flat still lets Windows paint its own light drop-down button and
            // border, which is why this one sat in the dialog looking like a Win32 control
            // among themed ones. FlatComboBox owns its painting completely.
            _corner = new FlatComboBox
            {
                Left = 110, Top = 15, Width = 250,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = UiFactory.BodyFont
            };
            // Selection is read back by index (Corner => SelectedIndex), so the display
            // text can be localised without affecting what gets saved.
            _corner.Items.AddRange(new object[]
            {
                Localization.T("Top centre"), Localization.T("Top left"), Localization.T("Top right"),
                Localization.T("Bottom left"), Localization.T("Bottom right"), Localization.T("Bottom centre")
            });
            _corner.SelectedIndex = corner >= 0 && corner <= 5 ? corner : 0;
            Controls.Add(_corner);

            Controls.Add(UiFactory.Label("Opacity:", 16, 58));
            // Themed slider rather than the framework TrackBar, which paints as a grey
            // native control in the middle of an otherwise themed dialog.
            _opacity = new SmoothTrackBar
            {
                Left = 104, Top = 54, Width = 210, Height = 22, Minimum = 40, Maximum = 100,
                TickFrequency = 10, SmallChange = 1, LargeChange = 10,
                Value = Math.Max(40, Math.Min(100, opacity))
            };
            _opacity.ApplyTheme(theme);
            _opacity.Scroll += (s, e) => _opacityValue.Text = _opacity.Value + "%";
            Controls.Add(_opacity);
            _opacityValue = UiFactory.Caption(_opacity.Value + "%", 320, 58);
            Controls.Add(_opacityValue);

            var badgeCaption = UiFactory.Caption("Show on the badge:", 16, 104);
            Controls.Add(badgeCaption);
            _showClicks = MakeCheck(theme, Localization.T("Click count"), 24, 130, showClicks);
            _showCps = MakeCheck(theme, Localization.T("Clicks per second (CPS)"), 24, 158, showCps);
            _showElapsed = MakeCheck(theme, Localization.T("Elapsed time"), 24, 186, showElapsed);
            Controls.Add(_showClicks);
            Controls.Add(_showCps);
            Controls.Add(_showElapsed);

            // Tempo's own rounded buttons — square Win32 rectangles were the last
            // obviously-unthemed thing left in this dialog.
            var ok = UiFactory.PrimaryButton("OK", 190, 224, 84, 30, theme);
            ok.DialogResult = DialogResult.OK;
            var cancel = UiFactory.Button("Cancel", 282, 224, 84, 30);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            // One pass over the whole tree rather than colouring each control by hand.
            // Hand-colouring is what let this dialog drift: every control added later
            // had to remember to do it, and the combo and checkboxes never did.
            ThemeManager.Apply(this, theme);

            // Apply() deliberately gives every Button the neutral Surface2 treatment, so
            // the primary action has to be re-asserted afterwards or OK comes back grey.
            ok.BackColor = theme.Accent;
            ok.ForeColor = Color.White;
            ok.FlatAppearance.BorderSize = 0;
            ok.FlatAppearance.MouseOverBackColor = theme.AccentHover;

            // Likewise the two secondary captions, which Apply() promotes to full-strength
            // text: they read as headings at that weight instead of supporting detail.
            _opacityValue.ForeColor = theme.TextMuted;
            badgeCaption.ForeColor = theme.TextMuted;
        }

        private static CheckBox MakeCheck(Theme theme, string text, int x, int y, bool value)
        {
            // ModernCheckBox, not the framework CheckBox: the native control draws
            // Windows' own blue tick, which is the mismatch you could see against every
            // other checkbox in Tempo. BackColor is a concrete colour rather than
            // Color.Transparent — transparent checkboxes ghost stale text behind
            // themselves on custom-painted surfaces (see UiFactory.Check).
            var cb = new ModernCheckBox
            {
                Left = x, Top = y, AutoSize = true, Text = text, Checked = value,
                Font = UiFactory.BodyFont,
                ForeColor = theme.Text, BackColor = theme.Background
            };
            cb.ApplyTheme(theme);
            return cb;
        }
    }
}
