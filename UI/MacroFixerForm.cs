using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Engine;
using AutoClicker.Models;

namespace AutoClicker.UI
{
    /// <summary>
    /// Shows what the macro doctor found and lets the user choose which repairs to
    /// apply. Problems are ticked by default (they are faults that will misbehave on
    /// playback); suggestions are left unticked, because a macro that deliberately
    /// clicks the same pixel at a fixed rate is a legitimate thing to want.
    /// </summary>
    public sealed class MacroFixerForm : Form
    {
        private readonly CheckedListBox _list;
        private readonly Label _detail;
        private readonly List<MacroFinding> _findings;

        /// <summary>The repairs the user ticked. Empty if they cancelled.</summary>
        public List<MacroFinding> Chosen { get; } = new List<MacroFinding>();

        public MacroFixerForm(Theme theme, Macro macro, List<MacroFinding> findings)
        {
            theme = theme ?? Theme.ForKind(ThemeKind.Dark);
            _findings = findings ?? new List<MacroFinding>();

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = Utils.Localization.F("Fix macro — {0}", macro?.Name ?? "");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(560, 430);
            BackColor = theme.Background;
            ThemeManager.ApplyWindowChrome(this, theme);
            ForeColor = theme.Text;

            int problems = 0, suggestions = 0;
            foreach (MacroFinding f in _findings)
            {
                if (f.Level == MacroFindingLevel.Problem) { problems++; } else { suggestions++; }
            }

            var heading = new Label
            {
                // A whole sentence per plural combination, rather than gluing " problem"
                // / " problems" onto a number. Concatenated plurals cannot be translated
                // at all: the noun's number changes the article and often the verb in
                // every language Tempo ships, and a translator handed the fragment
                // " suggestions" on its own has no sentence to fit it into.
                Text = problems > 0
                    ? Utils.Localization.F(
                        problems == 1
                            ? (suggestions == 1 ? "1 problem and 1 suggestion found"
                                                : "1 problem and {1} suggestions found")
                            : (suggestions == 1 ? "{0} problems and 1 suggestion found"
                                                : "{0} problems and {1} suggestions found"),
                        problems, suggestions)
                    : Utils.Localization.F(
                        suggestions == 1
                            ? "1 suggestion found — nothing is broken"
                            : "{0} suggestions found — nothing is broken",
                        suggestions),
                Location = new Point(16, 14),
                Size = new Size(528, 22),
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = problems > 0 ? theme.Danger : theme.Text,
                BackColor = Color.Transparent
            };
            Controls.Add(heading);

            var sub = new Label
            {
                Text = Utils.Localization.T(
                    "Ticked fixes are applied when you press Fix selected. Nothing else is changed."),
                Location = new Point(16, 38),
                Size = new Size(528, 20),
                ForeColor = theme.TextMuted,
                BackColor = Color.Transparent
            };
            Controls.Add(sub);

            _list = new CheckedListBox
            {
                Location = new Point(16, 66),
                Size = new Size(528, 190),
                BackColor = theme.Surface,
                ForeColor = theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                CheckOnClick = true,
                IntegralHeight = false
            };
            foreach (MacroFinding f in _findings)
            {
                _list.Items.Add((f.Level == MacroFindingLevel.Problem ? "⚠  " : "•  ") + f.Title,
                                f.Level == MacroFindingLevel.Problem);
            }
            _list.SelectedIndexChanged += (s, e) => ShowDetail();
            Controls.Add(_list);

            _detail = new Label
            {
                Location = new Point(16, 266),
                Size = new Size(528, 82),
                ForeColor = theme.TextMuted,
                BackColor = Color.Transparent
            };
            Controls.Add(_detail);

            var fix = new Button
            {
                Text = Utils.Localization.T("Fix selected"),
                Location = new Point(316, 366),
                Size = new Size(112, 32),
                DialogResult = DialogResult.OK,
                BackColor = theme.Accent,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            fix.Click += (s, e) =>
            {
                Chosen.Clear();
                for (int i = 0; i < _findings.Count && i < _list.Items.Count; i++)
                {
                    if (_list.GetItemChecked(i)) { Chosen.Add(_findings[i]); }
                }
            };
            Controls.Add(fix);

            var cancel = new Button
            {
                Text = Utils.Localization.T("Cancel"),
                Location = new Point(436, 366),
                Size = new Size(108, 32),
                DialogResult = DialogResult.Cancel,
                BackColor = theme.Surface2,
                ForeColor = theme.Text,
                FlatStyle = FlatStyle.Flat
            };
            Controls.Add(cancel);

            AcceptButton = fix;
            CancelButton = cancel;

            if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = 0;
            }
        }

        private void ShowDetail()
        {
            int i = _list.SelectedIndex;
            _detail.Text = i >= 0 && i < _findings.Count ? _findings[i].Detail : string.Empty;
        }
    }
}
