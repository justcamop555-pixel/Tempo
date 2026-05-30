using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Persistence;

namespace AutoClicker.UI
{
    public partial class MainForm
    {
        private readonly Dictionary<HotkeyAction, HotkeyCaptureControl> _bindingControls
            = new Dictionary<HotkeyAction, HotkeyCaptureControl>();
        private NumericUpDown _intervalStepNum;

        private void BuildKeybindsTab()
        {
            var page = new TabPage("Keybinds") { AutoScroll = true };

            var help = UiFactory.Label(
                "Bind any action to a global hotkey. Click a field, then press a key " +
                "combo (any letter or F-key with Ctrl/Alt/Shift/Win) OR click the " +
                "middle / side mouse buttons. Bare left & right click are reserved — " +
                "add a modifier to bind them. Backspace or Delete clears a field. " +
                "Hotkeys work even when the window is in the tray. Save to apply.",
                12, 12);
            help.MaximumSize = new Size(700, 0);
            help.AutoSize = true;
            help.ForeColor = _theme.TextMuted;

            var saveBtn = UiFactory.PrimaryButton("Save Keybinds", 12, 56, 150, 32, _theme);
            saveBtn.Click += OnSaveKeybinds;

            var resetBtn = UiFactory.Button("Reset to defaults", 172, 56, 150, 32);
            resetBtn.Click += OnResetKeybinds;

            page.Controls.Add(UiFactory.Caption("Interval step (ms) for increase/decrease:", 344, 50));
            _intervalStepNum = UiFactory.Numeric(344, 66, 90, 1, 600000, 10);

            page.Controls.Add(help);
            page.Controls.Add(saveBtn);
            page.Controls.Add(resetBtn);
            page.Controls.Add(_intervalStepNum);

            // Column headers.
            page.Controls.Add(UiFactory.Label("Action", 16, 104, FontStyle.Bold));
            page.Controls.Add(UiFactory.Label("Hotkey", 300, 104, FontStyle.Bold));

            int y = 130;
            const int rowHeight = 32;

            _bindingControls.Clear();
            foreach (var info in HotkeyActions.All)
            {
                var label = UiFactory.Label(info.Label, 16, y + 3);
                label.Width = 270;
                label.AutoSize = false;
                label.Height = 24;

                var capture = new HotkeyCaptureControl
                {
                    Left = 300,
                    Top = y,
                    Width = 220,
                    Font = UiFactory.BodyFont
                };

                var desc = UiFactory.Caption(info.Description, 530, y + 4);
                desc.ForeColor = _theme.TextMuted;
                desc.MaximumSize = new Size(180, 0);
                desc.AutoSize = true;

                _bindingControls[info.Action] = capture;
                capture.HotkeyChanged += (s, e) => HighlightConflicts();

                page.Controls.Add(label);
                page.Controls.Add(capture);
                page.Controls.Add(desc);

                y += rowHeight;
            }

            _tabs.TabPages.Add(page);
        }

        private void LoadKeybindsIntoUi()
        {
            _settings.EnsureBindings();

            foreach (var pair in _bindingControls)
            {
                HotkeyDefinition hk = _settings.HotkeyFor(pair.Key);
                pair.Value.Hotkey = hk != null ? hk.Clone() : new HotkeyDefinition();
            }

            if (_intervalStepNum != null)
            {
                int step = _settings.IntervalStepMilliseconds;
                if (step < 1) step = 1;
                if (step > 600000) step = 600000;
                _intervalStepNum.Value = step;
            }

            HighlightConflicts();
        }

        /// <summary>
        /// Colours any hotkey fields that share the same combination, so duplicates
        /// are obvious before saving.
        /// </summary>
        private void HighlightConflicts()
        {
            if (_bindingControls.Count == 0)
            {
                return;
            }

            // Count how many fields use each combination.
            var counts = new Dictionary<string, int>();
            foreach (var pair in _bindingControls)
            {
                HotkeyDefinition hk = pair.Value.Hotkey;
                if (hk == null || !hk.IsValid)
                {
                    continue;
                }

                string key = hk.ToDisplayString();
                counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
            }

            Color normal = _theme.InputBackground;

            foreach (var pair in _bindingControls)
            {
                HotkeyDefinition hk = pair.Value.Hotkey;
                bool isClash = hk != null && hk.IsValid &&
                               counts.TryGetValue(hk.ToDisplayString(), out int c) && c > 1;
                pair.Value.BackColor = isClash
                    ? BlendColors(normal, _theme.Danger, 0.35)
                    : normal;
            }
        }

        private static Color BlendColors(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            int r = (int)(a.R + (b.R - a.R) * t);
            int g = (int)(a.G + (b.G - a.G) * t);
            int bl = (int)(a.B + (b.B - a.B) * t);
            return Color.FromArgb(r, g, bl);
        }

        private void OnSaveKeybinds(object sender, EventArgs e)
        {
            _settings.EnsureBindings();

            foreach (var pair in _bindingControls)
            {
                HotkeyBinding binding = _settings.GetBinding(pair.Key);
                if (binding == null)
                {
                    binding = new HotkeyBinding(pair.Key, new HotkeyDefinition());
                    _settings.Bindings.Add(binding);
                }

                binding.Hotkey = pair.Value.Hotkey != null
                    ? pair.Value.Hotkey.Clone()
                    : new HotkeyDefinition();
            }

            _settings.IntervalStepMilliseconds = (int)_intervalStepNum.Value;

            string conflicts = FindConflicts();
            SettingsManager.Save(_settings);
            ApplyHotkeysFromSettings();

            if (!string.IsNullOrEmpty(conflicts))
            {
                ShowWarning(
                    "Keybinds saved, but some combinations are used more than once. " +
                    "Only one action per combination will respond:\n\n" + conflicts);
            }
            else
            {
                ShowInfo("Keybinds saved.");
            }
        }

        private void OnResetKeybinds(object sender, EventArgs e)
        {
            var confirm = MessageBox.Show(this,
                "Reset all keybinds to their defaults?",
                "Tempo",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            _settings.Bindings = HotkeyActions.DefaultBindings();
            _settings.IntervalStepMilliseconds = 10;
            SettingsManager.Save(_settings);
            LoadKeybindsIntoUi();
            ApplyHotkeysFromSettings();
        }

        /// <summary>
        /// Returns a human-readable list of duplicate key combinations, or an empty
        /// string when there are none.
        /// </summary>
        private string FindConflicts()
        {
            var seen = new Dictionary<string, HotkeyAction>();
            var clashes = new List<string>();

            foreach (var pair in _bindingControls)
            {
                HotkeyDefinition hk = pair.Value.Hotkey;
                if (hk == null || !hk.IsValid)
                {
                    continue;
                }

                string key = hk.ToDisplayString();
                if (seen.TryGetValue(key, out HotkeyAction other))
                {
                    clashes.Add($"  • {key}: {HotkeyActions.LabelFor(other)} / {HotkeyActions.LabelFor(pair.Key)}");
                }
                else
                {
                    seen[key] = pair.Key;
                }
            }

            if (clashes.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (string c in clashes)
            {
                sb.AppendLine(c);
            }
            return sb.ToString();
        }
    }
}
