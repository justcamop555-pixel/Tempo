using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A container that paints a surface of its own — a tinted notice box on a card — and so
    /// knows what colour the labels on it stand on. <see cref="ThemeManager"/> asks it rather
    /// than walking past it to the card, which would give each label a card-coloured box
    /// inside the tint.
    /// </summary>
    internal interface IThemedSurface
    {
        Color SurfaceFor(Theme theme);
    }

    /// <summary>
    /// Applies a <see cref="Theme"/> to a control hierarchy. Handles the common
    /// WinForms control types used in this application.
    /// </summary>
    public static class ThemeManager
    {
        /// <summary>
        /// Tag for a Panel that should look like the PAGE it sits on — the page colour, or
        /// see-through over a wallpaper, by the same rule as the labels on it.
        ///
        /// Untagged panels are left alone, which is right for panels that carry a colour of their
        /// own — and was wrong for the Accounts tab's screens (enable / set password / unlock /
        /// list), which are plain containers meant to vanish into the page. They kept the colour
        /// they were built with while every label on them followed the theme, so each label sat in
        /// a lighter box of its own: #FEF8F4 labels on a #FEF8EA panel, measured off the screen.
        /// </summary>
        public const string PageTag = "page";

        public static void Apply(Control root, Theme theme)
        {
            if (root == null || theme == null)
            {
                return;
            }

            // Every Tempo WINDOW gets its title bar painted in the theme, not just the
            // main one. A dark system bar still reads as a black strip above a coloured
            // theme, and each dialog that skipped this stood out against the app. Done
            // here because every themed window already routes through Apply().
            if (root is Form form)
            {
                ApplyWindowChrome(form, theme);
            }

            ApplyToControl(root, theme);

            foreach (Control child in root.Controls)
            {
                Apply(child, theme);
            }
        }

        /// <summary>
        /// Paints a window's title bar and border in the theme (Windows 11+; older builds
        /// keep the dark/light bar). Re-applied whenever the handle is recreated, so the
        /// colour survives a DPI change or a border-style switch.
        /// </summary>
        public static void ApplyWindowChrome(Form form, Theme theme)
        {
            if (form == null || theme == null)
            {
                return;
            }
            void Paint()
            {
                if (!form.IsHandleCreated) { return; }
                try
                {
                    NativeChrome.SetTitleBarDark(form.Handle, theme.Background.GetBrightness() < 0.5f);
                    NativeChrome.TintTitleBar(form.Handle, theme.Surface, theme.Text, theme.Border);
                }
                catch { /* pre-Windows-11 — the default bar stays */ }
            }
            Paint();
            // A recreated handle loses the DWM attributes; re-apply when that happens.
            form.HandleCreated -= OnChromeHandleCreated;
            form.HandleCreated += OnChromeHandleCreated;
            _chromeThemes.Remove(form);
            _chromeThemes.Add(form, theme);
            // Every themed window comes through here, so this is where each one learns to fit the screen
            // it opens on — a laptop's work area is far shorter than some dialogs (see ScreenFit).
            ScreenFit.Attach(form);
        }

        // The theme each window was last chromed with, so a handle recreation can restore it.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Form, Theme>
            _chromeThemes = new System.Runtime.CompilerServices.ConditionalWeakTable<Form, Theme>();

        private static void OnChromeHandleCreated(object sender, System.EventArgs e)
        {
            if (sender is Form f && _chromeThemes.TryGetValue(f, out Theme t) && f.IsHandleCreated)
            {
                try
                {
                    NativeChrome.SetTitleBarDark(f.Handle, t.Background.GetBrightness() < 0.5f);
                    NativeChrome.TintTitleBar(f.Handle, t.Surface, t.Text, t.Border);
                }
                catch { }
            }
        }

        /// <summary>What a label was last given: the theme, the role, and the colour that meant.</summary>
        private sealed class LabelInked
        {
            public Theme Theme;
            public int Role;
            public int Argb;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, LabelInked>
            _labelInks = new System.Runtime.CompilerServices.ConditionalWeakTable<Control, LabelInked>();

        // A theme's label inks, indexed by role (0 is Text). Several are computed — the *Text
        // colours solve for contrast — so each theme's are worked out once.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Theme, Color[]>
            _roleInks = new System.Runtime.CompilerServices.ConditionalWeakTable<Theme, Color[]>();

        private static Color[] RoleInks(Theme t)
        {
            return _roleInks.GetValue(t, th => new[]
            {
                th.Text, th.TextMuted, th.WarningText, th.DangerText, th.AccentText, th.SuccessText,
                th.Warning, th.Danger, th.Accent, th.Success
            });
        }

        private static int RoleOf(Color[] inks, int argb)
        {
            for (int i = 0; i < inks.Length; i++)
            {
                if (inks[i].ToArgb() == argb) { return i; }
            }
            return -1;
        }

        /// <summary>
        /// The colour a label takes in <paramref name="theme"/>: the same ROLE it had — muted stays
        /// muted, a warning stays a warning — and plain text when its colour is none of them.
        ///
        /// Every label used to be set to Text. More than a hundred are made muted when they are
        /// built, and at start-up this pass ran after all of them, so hints and notes showed at full
        /// text brightness from the first frame; any re-theme — picking a theme, Match Windows
        /// following the system, the HDR correction switching as HDR is turned on or the window
        /// crosses to another screen — did the same to the ones a refresher had fixed, and warnings
        /// lost their colour.
        ///
        /// The role is REMEMBERED, not re-guessed from the colour each time: a label still showing
        /// the colour it was given keeps that role. Guessing alone drifted wherever a theme gives two
        /// roles one colour (an accent that is its success green): the label came out of that theme
        /// as whichever role was checked first. A colour set since — by the label's own code, in the
        /// palette it was last given — or by its builder with the theme being applied, is matched.
        /// </summary>
        private static Color LabelInk(Label label, Theme theme)
        {
            Color[] now = RoleInks(theme);
            int argb = label.ForeColor.ToArgb();
            int role = -1;
            if (_labelInks.TryGetValue(label, out LabelInked last))
            {
                role = last.Argb == argb ? last.Role : RoleOf(RoleInks(last.Theme), argb);
            }
            if (role < 0) { role = RoleOf(now, argb); }
            if (role < 0) { role = 0; }
            _labelInks.AddOrUpdate(label, new LabelInked { Theme = theme, Role = role, Argb = now[role].ToArgb() });
            return now[role];
        }

        /// <summary>
        /// Re-evaluates only the background colour of labels, checkboxes and radios under
        /// <paramref name="root"/>. Call after a page's backdrop image is set or cleared:
        /// page-level controls then flip to a transparent (over a wallpaper) or solid (no
        /// wallpaper) background without a full re-theme.
        /// </summary>
        public static void RefreshBackdropBackgrounds(Control root, Theme theme)
        {
            if (root == null || theme == null)
            {
                return;
            }
            if (root is Label || root is CheckBox || root is RadioButton
                || (root is Panel p && p.Tag is string tag && tag == PageTag))
            {
                root.BackColor = EffectiveBackColor(root, theme);
            }
            foreach (Control child in root.Controls)
            {
                RefreshBackdropBackgrounds(child, theme);
            }
        }

        /// <summary>
        /// Returns the colour a checkbox/radio/label should use as its OWN background so
        /// it erases cleanly on repaint: the surface colour of the card it sits in, or the
        /// page background if it sits directly on a page. Walking up to the nearest
        /// CardGroupBox (or the page) means the control's solid background always matches
        /// whatever is behind it, so it blends in without relying on Color.Transparent
        /// (which leaves ghosting/stale text over custom-painted, scrolling panels).
        /// </summary>
        private static Color EffectiveBackColor(Control c, Theme theme)
        {
            Control p = c?.Parent;
            while (p != null)
            {
                // A container that paints a colour of its own (a tinted notice box) says which, so
                // the labels on it match it instead of the card underneath.
                if (p is IThemedSurface own)
                {
                    return own.SurfaceFor(theme);
                }
                if (p is CardGroupBox)
                {
                    return theme.Surface;
                }
                // A panel tagged as a surface IS the thing the label sits on. This walk used to
                // step straight past it to the page or the Form, so a label on a Surface-coloured
                // panel got the PAGE colour and sat in a darker box of its own — the exact look
                // reported on the detached Accounts window's header. ApplyToControl already
                // paints these tags; the walk now agrees with it.
                if (p is Panel surfacePanel && surfacePanel.Tag is string surfaceTag)
                {
                    if (surfaceTag == "surface") { return theme.Surface; }
                    if (surfaceTag == "surface2") { return theme.Surface2; }
                }
                if (p is BackdropTabPage backdrop)
                {
                    // Directly on a page that has a wallpaper: go transparent so the image
                    // shows through the label/checkbox instead of an opaque page-coloured
                    // box over the wallpaper. Without a wallpaper, the solid page colour
                    // is invisible anyway, and avoids any transparency edge cases.
                    return backdrop.HasBackdropImage ? Color.Transparent : theme.Background;
                }
                if (p is TabPage)
                {
                    return theme.Background;
                }
                if (p is Form)
                {
                    return theme.Background;
                }
                p = p.Parent;
            }
            // No recognised container in the chain - match the immediate parent's actual
            // background if we have one, else the page background. Never white-by-accident.
            if (c?.Parent != null && c.Parent.BackColor.A == 255)
            {
                return c.Parent.BackColor;
            }
            return theme.Background;
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
                    if (textBox is FlatTextBox flat)
                    {
                        flat.ApplyTheme(theme);   // its rounded border is painted in the theme
                    }
                    break;

                case FlatNumericUpDown flatNud:
                    flatNud.ApplyTheme(theme);
                    break;

                case NumericUpDown nud:
                    nud.BackColor = theme.InputBackground;
                    nud.ForeColor = theme.Text;
                    break;

                case ThemedProgressBar bar:
                    bar.ApplyTheme(theme);
                    break;

                case FlatComboBox flatCombo:
                    flatCombo.ApplyTheme(theme);
                    break;

                case SmoothTrackBar slider:
                    slider.ApplyTheme(theme);
                    break;

                case ComboBox combo:
                    combo.BackColor = theme.InputBackground;
                    combo.ForeColor = theme.Text;
                    combo.FlatStyle = FlatStyle.Flat;
                    break;

                // Must come BEFORE the plain ListBox case — a switch matches in order,
                // and MacroListBox paints its own rows from the theme.
                case MacroListBox macroList:
                    macroList.ApplyTheme(theme);
                    break;

                case ListBox list:
                    list.BackColor = theme.Surface2;
                    list.ForeColor = theme.Text;
                    list.BorderStyle = BorderStyle.FixedSingle;
                    break;

                // Must come BEFORE the plain ListView case — a switch matches in order,
                // and the generic one would otherwise swallow every themed list and
                // leave it drawing itself with the previous theme's colours.
                case ThemedListView tlv:
                    tlv.ApplyTheme(theme);
                    break;

                case ListView lv:
                    lv.BackColor = theme.Surface2;
                    lv.ForeColor = theme.Text;
                    break;

                case ToggleSwitch ts:
                    ts.ForeColor = theme.Text;
                    ts.BackColor = EffectiveBackColor(ts, theme);
                    ts.ApplyTheme(theme);
                    break;

                case ModernCheckBox mcb:
                    mcb.ForeColor = theme.Text;
                    mcb.BackColor = EffectiveBackColor(mcb, theme);
                    mcb.ApplyTheme(theme);
                    break;

                case ModernRadioButton mrb:
                    mrb.ForeColor = theme.Text;
                    mrb.BackColor = EffectiveBackColor(mrb, theme);
                    mrb.ApplyTheme(theme);
                    break;

                case CheckBox cb:
                    cb.ForeColor = theme.Text;
                    // A CONCRETE background (matching whatever the control sits on),
                    // never Color.Transparent. Transparent child controls over a
                    // custom-painted / scrolling panel don't erase their old pixels on
                    // repaint, which is what made checkboxes leave overlapping ghosts and
                    // stale text after scrolling or toggling. A solid colour erases
                    // cleanly every repaint.
                    cb.BackColor = EffectiveBackColor(cb, theme);
                    break;

                case RadioButton rb:
                    rb.ForeColor = theme.Text;
                    rb.BackColor = EffectiveBackColor(rb, theme);
                    break;

                case Label label:
                    label.ForeColor = LabelInk(label, theme);
                    label.BackColor = EffectiveBackColor(label, theme);
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
                    else if (panel.Tag is string tag3 && tag3 == PageTag)
                    {
                        panel.BackColor = EffectiveBackColor(panel, theme);
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
