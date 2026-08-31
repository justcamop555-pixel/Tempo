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
                return;
            }

            int y = 92;
            y = AddRow(t, "Speed", facts.SpeedHint, y);
            y = AddRow(t, "Precision", facts.PrecisionHint, y);
            y = AddRow(t, "Languages", facts.IsMultilingual
                ? "Handles any language, and can auto-detect which is being spoken."
                : "English only. Tempo's spoken-language setting will have no effect on it.", y);
            y = AddRow(t, "File", facts.FileName + "\n" + Folder(facts.Path), y);

            // Now that the rows have measured themselves, the window can be exactly as
            // tall as it needs to be: footer, buttons, and a consistent bottom margin.
            ClientSize = new Size(520, y + 14 + 20 + 12 + 32 + 20);

            // The raw shape, for anyone who wants to confirm the identification itself.
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
            Controls.Add(tech);

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
        }

        /// <summary>A labelled fact. Returns the y for the next one.</summary>
        private int AddRow(Theme t, string label, string value, int y)
        {
            var l = new Label
            {
                Text = label,
                Left = 22,
                Top = y,
                Width = 84,
                Height = 20,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = t.TextMuted
            };
            Controls.Add(l);

            int lines = value.Contains("\n") ? 2 : (value.Length > 62 ? 2 : 1);
            var v = new Label
            {
                Text = value,
                Left = 112,
                Top = y,
                Width = 386,
                Height = lines * 18 + 4,
                ForeColor = t.Text
            };
            Controls.Add(v);
            return y + Math.Max(28, v.Height + 8);
        }

        private static string Folder(string path)
        {
            try { return System.IO.Path.GetDirectoryName(path) ?? ""; }
            catch { return ""; }
        }

        private static Button MakeButton(string text, Theme t, bool primary)
        {
            var b = new Button
            {
                Text = text,
                Width = primary ? 136 : 96,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? t.Accent : t.Surface,
                ForeColor = primary ? Color.White : t.Text,
                Font = new Font("Segoe UI", 9.75f)
            };
            b.FlatAppearance.BorderColor = primary ? t.Accent : t.Border;
            return b;
        }
    }
}
