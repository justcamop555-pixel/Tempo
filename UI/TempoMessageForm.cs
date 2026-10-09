using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Utils;
using WinForms = System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// Tempo's own message dialog: themed, translated, and shaped like a MessageBox so it
    /// can stand in for one everywhere.
    ///
    /// Tempo had 41 direct MessageBox.Show calls plus 68 more through ShowInfo/ShowWarning,
    /// and every one of them produced a system-grey box with a system title bar in an app
    /// that draws its own chrome — and in English, because a MessageBox has no translation
    /// path. Even the buttons were wrong: Windows localises "Yes"/"No" to the OS language,
    /// not the app's, so a Spanish Tempo on an English Windows asked its questions in
    /// English no matter what.
    ///
    /// The text is translated here rather than at 109 call sites, on the same reasoning as
    /// TempoNotify: one choke point translates whatever reaches it, and T() returns its
    /// input unchanged when there is no entry, so a caller that already translated its
    /// message is unaffected.
    /// </summary>
    public sealed class TempoMessageForm : Form
    {
        private const int Margin = 22;
        private const int Width0 = 460;

        /// <summary>
        /// The theme to draw with. Set once by MainForm when it applies a theme, so the
        /// static entry points below can be called from anywhere — including code that has
        /// no reference to the form, which is most of the callers.
        /// </summary>
        internal static Theme CurrentTheme;

        private TempoMessageForm(Theme theme, string text, string caption,
                                 WinForms.MessageBoxButtons buttons, WinForms.MessageBoxIcon icon,
                                 WinForms.MessageBoxDefaultButton defaultButton)
        {
            // Scaled with the screen at the end of this constructor (UiFactory.ScaleToScreen). On a 150%
            // laptop the window kept its 100% size around 150% text, and the 19 pt glyph overflowed its
            // 38 px badge. Its layout is written in 100% units, so text is measured in them too
            // (UiFactory.Measure96) — measured in screen pixels and then scaled, it would count twice.
            var t = theme ?? CurrentTheme ?? Theme.ForKind(ThemeKind.Dark);

            Text = string.IsNullOrEmpty(caption) ? "Tempo" : Localization.T(caption);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = t.Background;
            ThemeManager.ApplyWindowChrome(this, t);
            ForeColor = t.Text;
            Font = UiFactory.BodyFont;

            Color accent = AccentFor(icon, t);

            Controls.Add(new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(Width0, 4),
                BackColor = accent
            });

            int y = 22;

            // A glyph in the kind's colour, in place of the Windows system icon.
            var badge = new Label
            {
                Text = GlyphFor(icon),
                Font = new Font("Segoe UI Symbol", 19f),
                ForeColor = accent,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(Margin, y),
                Size = new Size(38, 38)
            };
            Controls.Add(badge);

            int left = Margin + 38 + 14;
            int width = Width0 - left - Margin;

            string body = Localization.T(text ?? "");
            var label = new Label
            {
                Text = body,
                ForeColor = t.Text,
                AutoSize = false,
                // Shown as written, as MessageBox shows its text: with shortcut markers on, a lone "&"
                // in a message ("…?placeId=1&linkCode=…") vanished and underlined the letter after it.
                UseMnemonic = false,
                Location = new Point(left, y),
                Width = width
            };
            label.Height = Math.Max(38, UiFactory.Measure96(body, Font, width, TextFormatFlags.NoPrefix).Height + 4);
            Controls.Add(label);

            y += Math.Max(badge.Height, label.Height) + 18;

            // Buttons: right-aligned, translated, each sized to its own caption so a
            // longer language cannot clip them.
            var specs = SpecsFor(buttons);
            int bx = Width0 - Margin;
            for (int i = specs.Length - 1; i >= 0; i--)
            {
                bool primary = IsDefault(specs.Length, i, defaultButton);
                Button b = MakeButton(Localization.T(specs[i].Item1), t, primary);
                b.DialogResult = specs[i].Item2;
                bx -= b.Width;
                b.Left = bx;
                b.Top = y;
                bx -= 10;
                Controls.Add(b);

                if (primary) { AcceptButton = b; }
                if (specs[i].Item2 == DialogResult.Cancel ||
                    (specs.Length == 1 && specs[i].Item2 == DialogResult.OK) ||
                    (specs.Length == 2 && specs[i].Item2 == DialogResult.No))
                {
                    CancelButton = b;
                }
            }

            ClientSize = new Size(Width0, y + 34 + 20);
            UiFactory.ScaleToScreen(this);
        }

        private static bool IsDefault(int count, int index, WinForms.MessageBoxDefaultButton def)
        {
            int wanted = def == WinForms.MessageBoxDefaultButton.Button2 ? 1
                       : def == WinForms.MessageBoxDefaultButton.Button3 ? 2 : 0;
            if (wanted >= count) { wanted = count - 1; }
            return index == wanted;
        }

        private static Tuple<string, DialogResult>[] SpecsFor(WinForms.MessageBoxButtons b)
        {
            switch (b)
            {
                case WinForms.MessageBoxButtons.YesNo:
                    return new[]
                    {
                        Tuple.Create("Yes", DialogResult.Yes),
                        Tuple.Create("No", DialogResult.No)
                    };
                case WinForms.MessageBoxButtons.YesNoCancel:
                    return new[]
                    {
                        Tuple.Create("Yes", DialogResult.Yes),
                        Tuple.Create("No", DialogResult.No),
                        Tuple.Create("Cancel", DialogResult.Cancel)
                    };
                case WinForms.MessageBoxButtons.OKCancel:
                    return new[]
                    {
                        Tuple.Create("OK", DialogResult.OK),
                        Tuple.Create("Cancel", DialogResult.Cancel)
                    };
                default:
                    return new[] { Tuple.Create("OK", DialogResult.OK) };
            }
        }

        private static Color AccentFor(WinForms.MessageBoxIcon icon, Theme t)
        {
            switch (icon)
            {
                case WinForms.MessageBoxIcon.Warning: return t.Warning;
                case WinForms.MessageBoxIcon.Error: return t.Danger;
                default: return t.Accent;
            }
        }

        private static string GlyphFor(WinForms.MessageBoxIcon icon)
        {
            switch (icon)
            {
                case WinForms.MessageBoxIcon.Warning: return "⚠";
                case WinForms.MessageBoxIcon.Error: return "✕";
                case WinForms.MessageBoxIcon.Question: return "?";
                default: return "ⓘ";
            }
        }

        private static Button MakeButton(string text, Theme t, bool primary)
        {
            var b = new Button
            {
                Text = text,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? t.Accent : t.Surface,
                ForeColor = primary ? t.OnAccent : t.Text,
                Font = new Font("Segoe UI", 9.75f)
            };
            b.FlatAppearance.BorderColor = primary ? t.Accent : t.Border;
            // Sized to its own translated caption, never below a comfortable minimum — in 100% units,
            // like the rest of the layout (the window is scaled after it is built).
            b.Width = Math.Max(96, UiFactory.Measure96(text, b.Font, 0, TextFormatFlags.Default).Width + 28);
            return b;
        }

        // ── Entry points, mirroring MessageBox.Show ──────────────────────────

        internal static DialogResult Show(IWin32Window owner, string text, string caption,
            WinForms.MessageBoxButtons buttons, WinForms.MessageBoxIcon icon,
            WinForms.MessageBoxDefaultButton defaultButton)
        {
            try
            {
                using (var dlg = new TempoMessageForm(CurrentTheme, text, caption, buttons, icon, defaultButton))
                {
                    // CenterParent needs a visible owner. A hidden or minimised one — the main window
                    // in the tray — has nothing to centre on, so the window the person is actually
                    // using takes its place when it is one of Tempo's (the Accounts window beside a
                    // game, say); only failing that does the message centre on the screen. It used to
                    // go straight to the screen with no owner at all: on the primary monitor, behind a
                    // Tempo window kept on top, while that window sat disabled by a dialog out of sight.
                    var ownerForm = owner as Form;
                    if (ownerForm == null || !ownerForm.Visible || ownerForm.WindowState == FormWindowState.Minimized)
                    {
                        Form active = Form.ActiveForm;
                        if (active != null && !ReferenceEquals(active, dlg) && active.Visible
                            && active.WindowState != FormWindowState.Minimized)
                        {
                            return dlg.ShowDialog(active);
                        }
                        dlg.StartPosition = FormStartPosition.CenterScreen;
                        return dlg.ShowDialog();
                    }
                    return dlg.ShowDialog(owner);
                }
            }
            catch (Exception ex)
            {
                // A message the user needs must never be lost to a styling failure.
                Logger.Warn("[ui] themed message dialog failed: " + ex.Message);
                return WinForms.MessageBox.Show(text, caption, buttons, icon, defaultButton);
            }
        }
    }
}
