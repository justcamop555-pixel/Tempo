using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Shows what Tempo worked out about a speech model that came from OUTSIDE its own
    /// downloads, and asks whether to use it.
    ///
    /// WHY THIS EXISTS: picking a built-in model is a safe act — Tempo wrote the list, so
    /// it can describe every entry. A file from somewhere else is the opposite: all the
    /// user has is a filename someone else chose, which may be wrong, truncated, or
    /// meaningless ("model.bin", "final2.bin"). Committing captions to it blind and finding
    /// out later that it is English-only, or a 3 GB Large that this PC cannot run live, is
    /// a bad way to learn. So the file is read first and the facts are stated plainly —
    /// what it is, what languages it can do, how heavy it is — before anything is changed.
    ///
    /// Every fact here is read out of the file's header, never inferred from its name.
    /// </summary>
    public sealed class ModelDetailsForm : Form
    {
        public ModelDetailsForm(Theme theme, WhisperModelManager.WhisperModelFacts facts)
        {
            // Built in 100% units and scaled with the screen at the end (UiFactory.ScaleToScreen); text is
            // measured in 100% units to match.
            var t = theme ?? Theme.ForKind(ThemeKind.Dark);
            bool ok = facts != null && facts.Valid;

            Text = Utils.Localization.T(ok ? "Use this speech model?" : "This file can't be used");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            // Height is set from the CONTENT further down, once the rows exist. A fixed
            // height cannot be right for both: a Base model's three short rows leave a
            // band of dead space, while a Large multilingual one wraps every row and runs
            // into the footer. Only the invalid-file case is a known size up front.
            ClientSize = new Size(520, 250);
            BackColor = t.Background;
            ThemeManager.ApplyWindowChrome(this, t);
            ForeColor = t.Text;

            // ── headline: what this model IS, in its own right ──────────────────
            var title = new Label
            {
                Text = ok ? facts.Name : Utils.Localization.T("Not a usable speech model"),
                Left = 22,
                Top = 20,
                Width = 476,
                Height = 30,
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = ok ? t.Text : t.Danger
            };
            Controls.Add(title);

            var sub = new Label
            {
                Text = ok
                    ? facts.LanguageText + "   ·   " + facts.Precision + "   ·   " + facts.SizeText
                    : (facts != null ? facts.FileName : ""),
                Left = 22,
                Top = 52,
                Width = 476,
                Height = 22,
                Font = new Font("Segoe UI", 10f),
                ForeColor = t.TextMuted
            };
            Controls.Add(sub);

            if (!ok)
            {
                var why = new Label
                {
                    Text = facts != null && !string.IsNullOrEmpty(facts.Problem)
                        ? facts.Problem
                        : Utils.Localization.T("Tempo couldn't read this file."),
                    Left = 22,
                    Top = 88,
                    Width = 476,
                    Height = 74,
                    ForeColor = t.Text
                };
                Controls.Add(why);

                var close = MakeButton("Close", t, primary: true);
                close.Left = ClientSize.Width - close.Width - 22;
                close.Top = ClientSize.Height - close.Height - 20;
                close.DialogResult = DialogResult.Cancel;
                Controls.Add(close);
                CancelButton = close;
                AcceptButton = close;
                UiFactory.ScaleToScreen(this);
                return;
            }

            // The name column as wide as its longest name in this language (German "Geschwindigkeit" is
            // wider than the 84 px it was given), the values taking what is left.
            using (var bold = new Font("Segoe UI", 9f, FontStyle.Bold))
            {
                foreach (string name in new[] { "Speed", "Precision", "Languages", "File" })
                {
                    _labelWidth = Math.Max(_labelWidth, UiFactory.Measure96(Utils.Localization.T(name), bold, 0, TextFormatFlags.Default).Width + 2);
                }
            }

            int y = 92;
            y = AddRow(t, "Speed", facts.SpeedHint, y);
            y = AddRow(t, "Precision", facts.PrecisionHint, y);
            y = AddRow(t, "Languages", Utils.Localization.T(facts.IsMultilingual
                ? "Handles any language, and can auto-detect which is being spoken."
                : "English only. Tempo's spoken-language setting will have no effect on it."), y);
            y = AddRow(t, "File", facts.FileName + "\n" + Folder(facts.Path), y);

            // The raw shape, for anyone who wants to confirm the identification itself. As tall as
            // its words: two lines in every language but English, where a one-line box cut the second.
            var tech = new Label
            {
                Text = Utils.Localization.F(
                    "Read from the file: {0} audio / {1} text layers  ·  width {2}  ·  {3} mel bands  ·  vocab {4}",
                    facts.AudioLayers, facts.TextLayers, facts.AudioState, facts.Mels, facts.Vocab),
                Left = 22,
                Top = y + 14,
                Width = 476,
                Height = 20,
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = t.TextMuted
            };
            tech.Height = Math.Max(20, UiFactory.Measure96(tech.Text, tech.Font, 476, TextFormatFlags.Default).Height + 2);
            Controls.Add(tech);

            // Now that the rows have measured themselves, the window can be exactly as
            // tall as it needs to be: footer, buttons, and a consistent bottom margin.
            ClientSize = new Size(520, tech.Bottom + 12 + 32 + 20);

            var use = MakeButton("Use this model", t, primary: true);
            use.Left = ClientSize.Width - use.Width - 22;
            use.Top = tech.Bottom + 12;
            use.DialogResult = DialogResult.OK;
            Controls.Add(use);

            var cancel = MakeButton("Cancel", t, primary: false);
            cancel.Left = use.Left - cancel.Width - 10;
            cancel.Top = use.Top;
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = use;
            CancelButton = cancel;
            UiFactory.ScaleToScreen(this);
        }

        /// <summary>A labelled fact. Returns the y for the next one.</summary>
        private int AddRow(Theme t, string label, string value, int y)
        {
            // Translated here: the four names went in raw, so the rows read "Speed", "Precision",
            // "Languages", "File" in every language.
            var l = new Label
            {
                Text = Utils.Localization.T(label),
                Left = 22,
                Top = y,
                Width = _labelWidth,
                Height = 20,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = t.TextMuted
            };
            Controls.Add(l);

            // Measured, not guessed: the old rule ("over 62 characters is two lines") cut the third
            // line of every hint once it was translated.
            int left = 22 + _labelWidth + 6;
            var v = new Label
            {
                Text = value,
                Left = left,
                Top = y,
                Width = 498 - left,
                ForeColor = t.Text
            };
            Controls.Add(v);
            v.Height = UiFactory.Measure96(value, v.Font, v.Width, TextFormatFlags.Default).Height + 4;
            return y + Math.Max(28, v.Height + 8);
        }

        /// <summary>The name column's width: the 84 px it always had, or the longest name in this language.</summary>
        private int _labelWidth = 84;

        private static string Folder(string path)
        {
            try { return System.IO.Path.GetDirectoryName(path) ?? ""; }
            catch { return ""; }
        }

        private static Button MakeButton(string text, Theme t, bool primary)
        {
            // Translated here, and as wide as the translation: "Use this model", "Cancel" and "Close"
            // were shown in English in every language.
            var b = new Button
            {
                Text = Utils.Localization.T(text),
                Width = primary ? 136 : 96,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? t.Accent : t.Surface,
                ForeColor = primary ? t.OnAccent : t.Text,
                Font = new Font("Segoe UI", 9.75f)
            };
            b.FlatAppearance.BorderColor = primary ? t.Accent : t.Border;
            UiFactory.FitToText96(b, b.Width, 24);
            return b;
        }
    }
}
