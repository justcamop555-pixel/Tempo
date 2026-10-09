using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Edits everything about a profile EXCEPT its click settings — the identity
    /// half that the Clicker tab has never had anywhere to put: name, description,
    /// icon, category and colour tag, plus the two opt-in snapshots.
    ///
    /// THE SNAPSHOTS ARE THE POINT. <see cref="ClickProfile.Keybinds"/> and
    /// <see cref="ClickProfile.AppSettings"/> have existed on the model, with
    /// working CaptureFrom/ApplyTo on both sides, since profiles were written —
    /// and stayed null on every profile ever saved because nothing ever offered to
    /// fill them. Ticking a box here captures the live values, and activating the
    /// profile later puts them back. That is what turns a profile from "an interval
    /// and a button" into a mode you switch into.
    ///
    /// Both are opt-in per profile, because they are surprising by default: a
    /// profile that silently re-themed the app and moved your hotkeys would be a
    /// bug report, not a feature. Null means "don't touch", and null is the default.
    /// </summary>
    public sealed class ProfileDetailsForm : Form
    {
        /// <summary>Glyphs offered by the picker. Any existing custom glyph is kept.</summary>
        private static readonly string[] Glyphs =
        {
            "🎯", "🎮", "⚔️", "🖱️", "⌨️", "💼", "📊",
            "🧪", "🚀", "🐟", "⛏️", "🌱", "🎬", "⭐"
        };

        /// <summary>Tag colours. The first entry means "follow the theme accent".</summary>
        private static readonly Color[] Swatches =
        {
            Color.Empty,
            Color.FromArgb(124, 92, 255), Color.FromArgb(64, 158, 255),
            Color.FromArgb(46, 190, 140), Color.FromArgb(240, 190, 60),
            Color.FromArgb(245, 130, 60), Color.FromArgb(235, 80, 100),
            Color.FromArgb(190, 110, 240), Color.FromArgb(140, 150, 175)
        };

        private readonly TextBox _name;
        private readonly TextBox _description;
        private readonly ComboBox _category;
        private readonly CheckBox _keybinds;
        private readonly CheckBox _appearance;
        private readonly CheckBox _updateKeybinds;     // only when the profile already has a snapshot
        private readonly CheckBox _updateAppearance;
        private readonly Label _preview;

        private readonly List<Button> _glyphButtons = new List<Button>();
        private readonly List<Button> _swatchButtons = new List<Button>();

        private readonly Theme _theme;
        private string _glyph;
        private int _colorArgb;

        public string ProfileName => _name.Text.Trim();
        public string ProfileDescription => _description.Text.Trim();
        public ProfileCategory Category => (ProfileCategory)Math.Max(0, _category.SelectedIndex);
        public string Glyph => _glyph;
        public int ColorArgb => _colorArgb;
        public bool CaptureKeybinds => _keybinds.Checked;
        public bool CaptureAppearance => _appearance.Checked;

        /// <summary>Replace the keybinds already saved with the profile by the ones in use now.</summary>
        public bool UpdateKeybinds => _updateKeybinds != null && _updateKeybinds.Checked;

        /// <summary>Replace the look already saved with the profile by the one in use now.</summary>
        public bool UpdateAppearance => _updateAppearance != null && _updateAppearance.Checked;

        public ProfileDetailsForm(Theme theme, ClickProfile profile)
        {
            // Built in 100% units; scaled with the screen at the end (UiFactory.ScaleToScreen).
            _theme = theme ?? Theme.ForKind(ThemeKind.Dark);
            profile = profile ?? new ClickProfile("New Profile");

            _glyph = string.IsNullOrEmpty(profile.Icon) ? "🎯" : profile.Icon;
            _colorArgb = profile.ColorTagArgb;

            Text = Localization.T("Profile details");
            // The client area (was a 486×512 window): see UiFactory.ScaleToScreen.
            ClientSize = new Size(470, 473);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = _theme.Background;
            ForeColor = _theme.Text;
            Font = UiFactory.BodyFont;

            int y = 16;

            // ── Name ──────────────────────────────────────────────────────────
            Controls.Add(UiFactory.Label("Name", 18, y, FontStyle.Bold));
            _name = UiFactory.Text(18, y + 20, 434, profile.Name);
            _name.MaxLength = ClickProfile.MaxNameLength;
            Controls.Add(_name);
            y += 54;

            // ── Description ───────────────────────────────────────────────────
            var descLabel = UiFactory.Label("Description", 18, y, FontStyle.Bold);
            Controls.Add(descLabel);
            // After the label, not at a fixed x=90: the bold German "Beschreibung" ran 11 px
            // into the hint.
            var descHint = UiFactory.Caption("Shown under the name on the profile card.",
                                             Math.Max(90, descLabel.Right + 8), y + 2);
            descHint.ForeColor = _theme.TextMuted;
            Controls.Add(descHint);
            _description = UiFactory.Text(18, y + 20, 434, profile.Description ?? "");
            _description.MaxLength = ClickProfile.MaxDescriptionLength;
            Controls.Add(_description);
            y += 54;

            // ── Icon ──────────────────────────────────────────────────────────
            Controls.Add(UiFactory.Label("Icon", 18, y, FontStyle.Bold));
            _preview = new EmojiLabel     // keeps the chosen icon in colour
            {
                Left = 412,
                Top = y - 6,
                Width = 40,
                Height = 40,
                Text = _glyph,
                Font = new Font("Segoe UI Emoji", 15f),
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(_preview);

            int gx = 18, gy = y + 20;
            for (int i = 0; i < Glyphs.Length; i++)
            {
                string glyph = Glyphs[i];
                var b = new RoundedButton
                {
                    Text = glyph,
                    Left = gx,
                    Top = gy,
                    Width = 40,
                    Height = 34,
                    CornerRadius = 8,
                    Font = new Font("Segoe UI Emoji", 11f),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    // The emoji alone says nothing to a screen reader.
                    AccessibleName = Localization.F("Icon {0}", (i + 1).ToString())
                };
                b.FlatAppearance.BorderSize = 1;
                b.Click += (s, e) => { _glyph = glyph; _preview.Text = glyph; HighlightGlyphs(); };
                _glyphButtons.Add(b);
                Controls.Add(b);

                gx += 46;
                if ((i + 1) % 7 == 0) { gx = 18; gy += 40; }
            }
            y = gy + 46;

            // ── Category ──────────────────────────────────────────────────────
            Controls.Add(UiFactory.Label("Category", 18, y + 4, FontStyle.Bold));
            // Combo items are NOT auto-translated by UiFactory, unlike labels.
            _category = UiFactory.Combo(96, y, 160,
                Localization.T("Gaming"),
                Localization.T("Work"),
                Localization.T("Productivity"),
                Localization.T("Custom"));
            int cat = (int)profile.Category;
            _category.SelectedIndex = (cat >= 0 && cat < _category.Items.Count) ? cat : 3;
            Controls.Add(_category);
            y += 40;

            // ── Colour tag ────────────────────────────────────────────────────
            var colourLabel = UiFactory.Label("Colour tag", 18, y + 6, FontStyle.Bold);
            Controls.Add(colourLabel);
            // The nine swatches exactly fill the row after an English label, so a longer one
            // ("Etiqueta de color", "Étiquette de couleur", "Farbmarkierung") ran up to 34 px
            // under the first swatch. When the label needs the room, the swatches take their
            // own line beneath it — the way the Icon grid above already sits.
            int sx = 96;
            if (colourLabel.Right + 8 > sx)
            {
                sx = 18;
                y += 24;
            }
            for (int i = 0; i < Swatches.Length; i++)
            {
                Color c = Swatches[i];
                var b = new RoundedButton
                {
                    Left = sx,
                    Top = y,
                    Width = 34,
                    Height = 28,
                    CornerRadius = 8,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    BackColor = c.IsEmpty ? _theme.Surface2 : c,
                    Text = c.IsEmpty ? "—" : "",
                    ForeColor = _theme.TextMuted,
                    AccessibleName = c.IsEmpty
                        ? Localization.T("Theme accent")
                        : Localization.F("Colour {0}", i.ToString())
                };
                b.FlatAppearance.BorderSize = 1;
                int argb = c.IsEmpty ? 0 : c.ToArgb();
                b.Click += (s, e) => { _colorArgb = argb; HighlightSwatches(); };
                _swatchButtons.Add(b);
                Controls.Add(b);
                sx += 40;
            }
            y += 44;

            // ── Opt-in snapshots ──────────────────────────────────────────────
            var snapTitle = UiFactory.Label("Switch this profile like a mode", 18, y, FontStyle.Bold);
            Controls.Add(snapTitle);
            y += 22;

            _keybinds = UiFactory.Check("Also restore my keybinds", 18, y, profile.Keybinds != null);
            Controls.Add(_keybinds);
            y += 24;

            // A snapshot, once taken, is kept as it was: re-taking it on every Save replaced the
            // profile's saved keys and look with whatever happened to be in use, silently, when the
            // dialog was only opened to change an emoji. Updating it is a choice of its own, and
            // these boxes are the only way to make it — before, it took unticking, saving,
            // reopening and ticking again.
            if (profile.Keybinds != null)
            {
                _updateKeybinds = UiFactory.Check("Update the saved keybinds to the ones I use now", 38, y, false);
                Controls.Add(_updateKeybinds);
                _keybinds.CheckedChanged += (s, e) => _updateKeybinds.Enabled = _keybinds.Checked;
                y += 24;
            }

            _appearance = UiFactory.Check("Also restore my theme, overlay and caption look", 18, y,
                profile.AppSettings != null);
            Controls.Add(_appearance);
            y += 24;

            if (profile.AppSettings != null)
            {
                _updateAppearance = UiFactory.Check("Update the saved look to the one I use now", 38, y, false);
                Controls.Add(_updateAppearance);
                _appearance.CheckedChanged += (s, e) => _updateAppearance.Enabled = _appearance.Checked;
                y += 24;
            }
            y += 2;

            const int NoteWidth = 434;
            var note = UiFactory.Caption(
                "Ticking these saves your current values into the profile now, and puts " +
                "them back whenever you activate it. Leave them off and the profile only " +
                "changes clicking.", 18, y);
            note.AutoSize = false;
            note.ForeColor = _theme.TextMuted;

            // MEASURE, don't guess. This paragraph wraps to three lines in English and
            // to four or five in German — and with the buttons at a hard-coded Y they
            // ended up underneath it. Measuring the translated text at the width it
            // will actually occupy, then placing the buttons below whatever came back
            // and sizing the window to match, makes the dialog fit in any language.
            // In 100% units: the window is scaled after it is built (UiFactory.Measure96).
            int noteHeight = UiFactory.Measure96(note.Text, note.Font, NoteWidth, TextFormatFlags.Default).Height;

            note.Size = new Size(NoteWidth, noteHeight);
            Controls.Add(note);

            // ── Buttons ───────────────────────────────────────────────────────
            int buttonsY = note.Bottom + 18;

            var ok = UiFactory.PrimaryButton("Save", 272, buttonsY, 86, 32, _theme);
            ok.Click += OnSave;

            var cancel = UiFactory.Button("Cancel", 366, buttonsY, 86, 32);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            // Right-aligned from their own captions ("Enregistrer" was cut at 86 px).
            UiFactory.FitToText96(ok, 86, 22);
            UiFactory.FitToText96(cancel, 86, 22);
            cancel.Left = 452 - cancel.Width;
            ok.Left = cancel.Left - 8 - ok.Width;

            Controls.Add(ok);
            Controls.Add(cancel);

            ClientSize = new Size(470, buttonsY + 32 + 16);
            AcceptButton = ok;
            CancelButton = cancel;

            HighlightGlyphs();
            HighlightSwatches();

            ThemeManager.Apply(this, _theme);

            // ThemeManager repaints every button in the theme's colours, which would
            // wipe the swatches. Put them back, and re-mark the current selections.
            for (int i = 0; i < _swatchButtons.Count; i++)
            {
                Color c = Swatches[i];
                _swatchButtons[i].BackColor = c.IsEmpty ? _theme.Surface2 : c;
            }
            HighlightGlyphs();
            HighlightSwatches();
            UiFactory.ScaleToScreen(this);
        }

        private void OnSave(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                MessageBox.Show(this, Localization.T("Give the profile a name."), "Tempo",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _name.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Rings the chosen glyph so the selection is visible, not implied.</summary>
        private void HighlightGlyphs()
        {
            for (int i = 0; i < _glyphButtons.Count; i++)
            {
                bool on = string.Equals(Glyphs[i], _glyph, StringComparison.Ordinal);
                _glyphButtons[i].FlatAppearance.BorderColor = on ? _theme.Accent : _theme.Border;
                _glyphButtons[i].FlatAppearance.BorderSize = on ? 2 : 1;
            }
        }

        private void HighlightSwatches()
        {
            for (int i = 0; i < _swatchButtons.Count; i++)
            {
                int argb = Swatches[i].IsEmpty ? 0 : Swatches[i].ToArgb();
                bool on = argb == _colorArgb;
                _swatchButtons[i].FlatAppearance.BorderColor = on ? _theme.Text : _theme.Border;
                _swatchButtons[i].FlatAppearance.BorderSize = on ? 3 : 1;
            }
        }

        /// <summary>
        /// Shows the editor and writes the result back onto <paramref name="profile"/>.
        /// Returns false when the user cancelled, in which case nothing is touched.
        /// </summary>
        public static bool Edit(IWin32Window owner, Theme theme, ClickProfile profile, AppSettings live)
        {
            if (profile == null) { return false; }

            using (var form = new ProfileDetailsForm(theme, profile))
            {
                if (form.ShowDialog(owner) != DialogResult.OK) { return false; }

                profile.Name = form.ProfileName;
                profile.Description = form.ProfileDescription;
                profile.Category = form.Category;
                profile.Icon = form.Glyph;
                profile.ColorTagArgb = form.ColorArgb;

                // Capture on an actual TICK, clear on untick, and otherwise leave the
                // stored snapshot alone.
                //
                // The box is pre-ticked whenever a snapshot already exists, and a CheckBox
                // cannot tell "the user just ticked this" from "it was already on" — so
                // re-capturing whenever it is ticked meant every OK overwrote the profile's
                // saved keys with whatever is live NOW. Opening "Edit details…" to change
                // an emoji, on a different profile, silently replaced the keybinds this
                // profile existed to restore. Nothing warned, the dialog never showed what
                // was stored, and there was no way back.
                bool hadSnapshot = profile.Keybinds != null;
                if (!form.CaptureKeybinds)
                {
                    profile.Keybinds = null;              // explicitly turned off
                }
                else if ((!hadSnapshot || form.UpdateKeybinds) && live != null)
                {
                    profile.Keybinds = ProfileKeybinds.CaptureFrom(live);   // newly ticked, or asked to
                }

                // The look by the same rule. It was re-captured on every OK — the exact bug fixed
                // above for keybinds, left in the line below it: changing a profile's emoji while
                // another theme was on replaced the look this profile existed to restore.
                bool hadLook = profile.AppSettings != null;
                if (!form.CaptureAppearance)
                {
                    profile.AppSettings = null;
                }
                else if ((!hadLook || form.UpdateAppearance) && live != null)
                {
                    profile.AppSettings = ProfileAppSettings.CaptureFrom(live);
                }

                return true;
            }
        }
    }
}
