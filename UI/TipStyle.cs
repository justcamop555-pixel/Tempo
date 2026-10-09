using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// One width, one look and one popup for every tooltip in Tempo.
    ///
    /// Width. WinForms raises a tooltip's maximum width to the whole screen (so that text can
    /// carry its own line breaks), and a ListView row's tip and a toolbar item's tip get the same
    /// treatment — so Windows never wraps any of them: a long help text ran in one line from one
    /// side of the monitor to the other and off its edge (reported with a photo of the
    /// Camera-relative movement tip, ~400 characters on one line). Text set through here is
    /// broken into lines of a readable width first, so Windows sizes the tip to those lines and
    /// keeps it on the screen.
    ///
    /// One popup. Two ToolTip components on one control BOTH pop up, at the same spot, one over
    /// the other — the layout fitter's "full caption" tip landed on top of the help tip of every
    /// caption it shortened (in English: "Show on-screen overlay while running"), and the Set-key
    /// button's hotkey-clash warning on top of its help. Everything set through here is merged:
    /// each control shows one tip, the full caption first, then each text in the order its
    /// component first spoke.
    ///
    /// Look. Only the main window's tooltips were drawn in the theme; the Accounts, Captions and
    /// key-clash tips, Live debug's, About's, the script step's, the chart's, the status bar's
    /// and the fitter's were Windows' bright default — the flash over the dark UI the main ones
    /// had been fixed for — and even the main ones kept the start-up theme after a theme change.
    /// </summary>
    internal static class TipStyle
    {
        // Colours of the theme in use; MainForm hands over every change. Dark until then.
        private static Color _back = Color.FromArgb(45, 45, 48);
        private static Color _fore = Color.WhiteSmoke;
        private static Color _border = Color.FromArgb(70, 70, 74);

        // The font Windows draws tooltips in, and a line about 28 of its line-heights wide —
        // some 60–70 characters, a comfortable measure, and it scales with the display.
        private static readonly Font TipFont = SystemFonts.StatusFont;
        private static readonly Dictionary<char, double> CharWidth = new Dictionary<char, double>();

        /// <summary>Where the layout fitter's full captions show when the control has no other tip.</summary>
        private static readonly ToolTip CaptionTip = new ToolTip
        {
            AutoPopDelay = 20000,
            InitialDelay = 400,
            ReshowDelay = 150,
            ShowAlways = true
        };

        /// <summary>Everything said about one control, by whom, in the order they first said it.</summary>
        private sealed class Parts
        {
            public string Caption;
            public readonly List<ToolTip> Tips = new List<ToolTip>();
            public readonly List<string> Texts = new List<string>();
            public ToolTip Shown;
        }

        private static readonly ConditionalWeakTable<Control, Parts> ByControl = new ConditionalWeakTable<Control, Parts>();

        /// <summary>The theme in use. Every tooltip picks it up the next time it draws.</summary>
        public static void SetTheme(Theme theme)
        {
            if (theme == null) { return; }
            _back = theme.Surface2;
            _fore = theme.Text;
            _border = theme.Border;
        }

        /// <summary>Draws <paramref name="tip"/> in the theme. Safe to call more than once.</summary>
        public static void Apply(ToolTip tip)
        {
            if (tip == null) { return; }
            tip.OwnerDraw = true;
            tip.Draw -= OnDraw;
            tip.Draw += OnDraw;
        }

        /// <summary>
        /// The tooltip a ToolStrip (status bar, menu) shows its items' ToolTipText in, drawn in
        /// the theme. The strip keeps it private, hence the reflection; a runtime without it
        /// simply keeps Windows' look.
        /// </summary>
        public static void Apply(ToolStrip strip)
        {
            if (strip == null) { return; }
            try
            {
                PropertyInfo p = typeof(ToolStrip).GetProperty("ToolTip", BindingFlags.Instance | BindingFlags.NonPublic);
                Apply(p?.GetValue(strip) as ToolTip);
            }
            catch (Exception ex) { Utils.Logger.Swallow("TipStyle.Apply(ToolStrip)", ex); }
        }

        /// <summary>
        /// SetToolTip, in the theme, with the text broken into lines of a readable width — and
        /// merged with whatever any other tooltip component says about the same control, so
        /// one tip shows instead of two on top of each other. Empty text withdraws this
        /// component's part.
        /// </summary>
        public static void Set(ToolTip tip, Control control, string text)
        {
            if (tip == null || control == null) { return; }
            Apply(tip);
            Parts parts = ByControl.GetOrCreateValue(control);
            int i = parts.Tips.IndexOf(tip);
            if (i < 0)
            {
                parts.Tips.Add(tip);
                parts.Texts.Add(text ?? string.Empty);
            }
            else
            {
                // Unchanged: leave it alone. Re-setting a tip that is on screen makes it flicker.
                if (string.Equals(parts.Texts[i], text ?? string.Empty, StringComparison.Ordinal)) { return; }
                parts.Texts[i] = text ?? string.Empty;
            }
            Show(control, parts);
        }

        /// <summary>
        /// The full text of a caption the layout fitter had to shorten. Shown at the top of the
        /// control's tip — alone, or above its help when it has some.
        /// </summary>
        public static void SetCaption(Control control, string fullCaption)
        {
            if (control == null) { return; }
            Parts parts = ByControl.GetOrCreateValue(control);
            if (string.Equals(parts.Caption ?? string.Empty, fullCaption ?? string.Empty, StringComparison.Ordinal)) { return; }
            parts.Caption = fullCaption;
            Show(control, parts);
        }

        private static readonly ConditionalWeakTable<Label, object> Following = new ConditionalWeakTable<Label, object>();

        /// <summary>
        /// For a label that ends in "…": its whole text on hover, kept up to date as the text
        /// changes (a status line), and only while it is actually cut short. Safe to call more
        /// than once.
        /// </summary>
        public static void FollowText(Label label)
        {
            if (label == null) { return; }
            if (!Following.TryGetValue(label, out _))
            {
                Following.Add(label, null);
                label.TextChanged += (s, e) => RefreshFollowed(label);
                label.SizeChanged += (s, e) => RefreshFollowed(label);
                label.FontChanged += (s, e) => RefreshFollowed(label);
            }
            RefreshFollowed(label);
        }

        /// <summary>
        /// A control's text as it SHOWS it: with mnemonics on, "&&" is drawn as one "&" and a lone
        /// "&" only underlines the next letter. A tip drawn without mnemonics has to be given the
        /// shown text, or "record && playback" appears doubled in it.
        /// </summary>
        internal static string Shown(Control c)
        {
            string t = c?.Text ?? string.Empty;
            bool mnemonic = c is Label l ? l.UseMnemonic : c is ButtonBase b && b.UseMnemonic;
            if (!mnemonic || t.IndexOf('&') < 0) { return t; }
            var sb = new StringBuilder(t.Length);
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i] == '&')
                {
                    if (i + 1 < t.Length && t[i + 1] == '&') { sb.Append('&'); i++; }
                    continue;
                }
                sb.Append(t[i]);
            }
            return sb.ToString();
        }

        private static void RefreshFollowed(Label label)
        {
            string text = label.Text ?? string.Empty;
            bool cut = false;
            if (text.Length > 0 && label.Width > 0 && label.Height > 0)
            {
                try
                {
                    Size need = TextRenderer.MeasureText(text, label.Font, new Size(Math.Max(1, label.ClientSize.Width - label.Padding.Horizontal), int.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | (label.UseMnemonic ? 0 : TextFormatFlags.NoPrefix));
                    cut = need.Height > label.ClientSize.Height - label.Padding.Vertical + 2
                          || need.Width > label.ClientSize.Width - label.Padding.Horizontal + 2;
                }
                catch { cut = true; }
            }
            SetCaption(label, cut ? Shown(label) : null);
        }

        /// <summary>What the control's single tip says now, unwrapped (for tests).</summary>
        internal static string Combined(Control control)
        {
            return control != null && ByControl.TryGetValue(control, out Parts parts) ? Compose(parts, out _) : string.Empty;
        }

        /// <summary>
        /// What each tooltip component says about <paramref name="control"/>, as it was handed in — for the
        /// layout checks, which look for words that reached a tip without being translated.
        /// </summary>
        internal static List<string> TextsFor(Control control)
        {
            var texts = new List<string>();
            if (control != null && ByControl.TryGetValue(control, out Parts parts))
            {
                foreach (string t in parts.Texts)
                {
                    if (!string.IsNullOrWhiteSpace(t)) { texts.Add(t); }
                }
            }
            return texts;
        }

        /// <summary>The component that shows the control's tip now, or null (for tests).</summary>
        internal static ToolTip ShownBy(Control control)
        {
            return control != null && ByControl.TryGetValue(control, out Parts parts) ? parts.Shown : null;
        }

        private static string Compose(Parts parts, out ToolTip home)
        {
            home = null;
            var sb = new StringBuilder();
            var said = new List<string>();
            if (!string.IsNullOrEmpty(parts.Caption))
            {
                sb.Append(parts.Caption);
                said.Add(parts.Caption.Trim());
            }
            for (int i = 0; i < parts.Tips.Count; i++)
            {
                string t = parts.Texts[i];
                if (string.IsNullOrEmpty(t)) { continue; }
                if (home == null) { home = parts.Tips[i]; }
                // The same words twice (a help text that only repeats the caption) read once.
                if (said.Contains(t.Trim())) { continue; }
                said.Add(t.Trim());
                if (sb.Length > 0) { sb.Append("\n\n"); }
                sb.Append(t);
            }
            if (home == null && sb.Length > 0) { home = CaptionTip; }
            return sb.ToString();
        }

        private static void Show(Control control, Parts parts)
        {
            string text = Compose(parts, out ToolTip home);
            try
            {
                // Withdraw first, so there is never a moment with two.
                if (parts.Shown != null && !ReferenceEquals(parts.Shown, home))
                {
                    parts.Shown.SetToolTip(control, string.Empty);
                }
                if (home != null)
                {
                    Apply(home);
                    home.SetToolTip(control, Wrap(text));
                }
                parts.Shown = home;
            }
            catch (Exception ex) { Utils.Logger.Swallow("TipStyle.Show", ex); }
        }

        /// <summary>
        /// <paramref name="text"/> with line breaks at a readable width. Existing breaks stay, and
        /// so does the spacing between words (help texts separate items with "  ·  "); a word too
        /// long for a line (a path, or a language written without spaces) is broken between
        /// characters rather than left to run off the screen.
        /// </summary>
        public static string Wrap(string text)
        {
            if (string.IsNullOrEmpty(text)) { return text ?? string.Empty; }
            double max = Math.Max(200, TipFont.Height * 28);
            var sb = new StringBuilder(text.Length + 16);
            string[] paragraphs = text.Replace("\r\n", "\n").Split('\n');
            for (int p = 0; p < paragraphs.Length; p++)
            {
                if (p > 0) { sb.Append('\n'); }
                string para = paragraphs[p];
                double line = 0;
                bool lineHasText = false;
                int i = 0;
                while (i < para.Length)
                {
                    // The spaces before the next word, then the word.
                    int gapStart = i;
                    while (i < para.Length && para[i] == ' ') { i++; }
                    string gap = para.Substring(gapStart, i - gapStart);
                    int wordStart = i;
                    while (i < para.Length && para[i] != ' ') { i++; }
                    string word = para.Substring(wordStart, i - wordStart);
                    if (word.Length == 0) { break; }   // trailing spaces

                    double g = WidthOf(gap);
                    double w = WidthOf(word);
                    if (lineHasText && line + g + w > max)
                    {
                        // A new line; the gap that led here is dropped with the break.
                        sb.Append('\n');
                        line = 0;
                        lineHasText = false;
                        gap = string.Empty;
                        g = 0;
                    }
                    // Indentation at the very start of a paragraph is kept too.
                    sb.Append(gap);
                    line += g;
                    if (w > max - line)
                    {
                        // Too long for any line: break it between characters.
                        foreach (char c in word)
                        {
                            double cw = WidthOf(c);
                            if (lineHasText && line + cw > max)
                            {
                                sb.Append('\n');
                                line = 0;
                            }
                            sb.Append(c);
                            line += cw;
                            lineHasText = true;
                        }
                        continue;
                    }
                    sb.Append(word);
                    line += w;
                    lineHasText = true;
                }
            }
            return CapLines(sb.ToString());
        }

        /// <summary>
        /// At most <see cref="MaxLines"/> lines. A script's error report can run to hundreds, and
        /// a tip taller than the screen has nowhere to go but off it; the whole text is still in
        /// the box it came from.
        /// </summary>
        private static string CapLines(string text)
        {
            int lines = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n') { continue; }
                if (++lines > MaxLines) { return text.Substring(0, i) + "\n…"; }
            }
            return text;
        }

        private const int MaxLines = 30;

        private static double WidthOf(string word)
        {
            double w = 0;
            foreach (char c in word) { w += WidthOf(c); }
            return w;
        }

        /// <summary>One character's width in the tooltip font, measured once (ten in a row, so padding washes out).</summary>
        private static double WidthOf(char c)
        {
            if (CharWidth.TryGetValue(c, out double w)) { return w; }
            try
            {
                Size s = TextRenderer.MeasureText(new string(c, 10), TipFont, Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                w = s.Width / 10.0;
            }
            catch
            {
                w = TipFont.Height * 0.5;
            }
            CharWidth[c] = w;
            return w;
        }

        private static void OnDraw(object sender, DrawToolTipEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var b = new SolidBrush(_back))
            {
                g.FillRectangle(b, e.Bounds);
            }
            using (var p = new Pen(_border))
            {
                g.DrawRectangle(p, new Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1));
            }
            // The lines as a block, centred in the tip: each line centred on its own made a
            // paragraph ragged down both sides. GDI text, like everything else in the window.
            const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
            string text = e.ToolTipText ?? string.Empty;
            Size block = TextRenderer.MeasureText(g, text, e.Font, new Size(e.Bounds.Width, int.MaxValue), flags);
            int w = Math.Min(e.Bounds.Width, block.Width + 2);
            int h = Math.Min(e.Bounds.Height, block.Height + 2);
            var r = new Rectangle(e.Bounds.X + (e.Bounds.Width - w) / 2, e.Bounds.Y + (e.Bounds.Height - h) / 2, w, h);
            TextRenderer.DrawText(g, text, e.Font, r, _fore, flags | TextFormatFlags.Left | TextFormatFlags.Top);
        }
    }
}
