using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace AutoClicker.UI
{
    /// <summary>
    /// Builds coloured text for a RichTextBox as ONE RTF string.
    ///
    /// WHY THIS EXISTS. Filling a RichTextBox by moving the caret, setting a colour and
    /// appending — a line, or a word, at a time — sends a separate message per step, and the
    /// control re-lays-out after each one. Measured on Live debug with the real window
    /// (scratchpad/debugcost):
    ///
    ///   · the stats pane (200 lines, rebuilt twice a second): 47 ms a time, up to 105 ms;
    ///   · the event log's reload, which opening the window and every pause in the filter box
    ///     trigger: 2.6 s for a 1,500-line ring, and 4.4 s when every line matched the filter.
    ///
    /// The whole of Tempo is frozen for that, because it is all on the UI thread. The same
    /// content built here and assigned in one go: ~4 ms and ~30 ms. Same text, same colours,
    /// same highlights — proven character by character by the harness.
    ///
    /// Only what Tempo needs: a colour per run of text, an optional background (the filter's
    /// highlight), and one line per paragraph.
    /// </summary>
    internal sealed class RtfBuilder
    {
        private readonly List<int> _palette = new List<int>();
        private readonly StringBuilder _body;
        private readonly string _fontName;
        private readonly int _halfPoints;
        private readonly string _style;

        /// <summary>The monospace face <see cref="StyledRun"/> uses for code; only in the font table once used.</summary>
        private const string MonoFont = "Consolas";
        private bool _usesMono;

        public RtfBuilder(Font font, int expectedLength = 4096)
        {
            Font f = font ?? System.Windows.Forms.Control.DefaultFont;
            _fontName = f.Name;
            _halfPoints = (int)Math.Round(f.SizeInPoints * 2);
            // Bold/italic come from the control's own font, so text drawn this way looks
            // exactly like text the control drew itself.
            _style = (f.Bold ? @"\b" : "") + (f.Italic ? @"\i" : "");
            _body = new StringBuilder(expectedLength);
        }

        /// <summary>The 1-based colour-table index for <paramref name="c"/> (\cf0 is "auto").</summary>
        private int Index(Color c)
        {
            int argb = c.ToArgb();
            int at = _palette.IndexOf(argb);
            if (at < 0) { _palette.Add(argb); at = _palette.Count - 1; }
            return at + 1;
        }

        /// <summary>Adds a run of text in <paramref name="fore"/>, optionally on <paramref name="back"/>.</summary>
        public void Run(string text, Color fore, Color? back = null)
        {
            if (string.IsNullOrEmpty(text)) { return; }
            if (back.HasValue)
            {
                _body.Append(@"{\highlight").Append(Index(back.Value)).Append(@"\cf").Append(Index(fore)).Append(' ');
                Escape(_body, text, 0, text.Length);
                _body.Append('}');
            }
            else
            {
                _body.Append(@"\cf").Append(Index(fore)).Append(' ');
                Escape(_body, text, 0, text.Length);
            }
        }

        /// <summary>
        /// Adds a run with its own weight, slant, typeface and size — for formatted prose such as
        /// release notes. Everything is set explicitly inside the group (\b0 as well as \b), so a
        /// run is never bold just because the control's own font happens to be.
        /// </summary>
        public void StyledRun(string text, Color fore, bool bold = false, bool italic = false,
                              bool mono = false, float sizeScale = 1f)
        {
            if (string.IsNullOrEmpty(text)) { return; }
            _body.Append('{').Append(bold ? @"\b" : @"\b0").Append(italic ? @"\i" : @"\i0");
            if (mono)
            {
                _usesMono = true;
                _body.Append(@"\f1");
            }
            if (Math.Abs(sizeScale - 1f) > 0.01f)
            {
                _body.Append(@"\fs").Append(Math.Max(2, (int)Math.Round(_halfPoints * sizeScale)));
            }
            _body.Append(@"\cf").Append(Index(fore)).Append(' ');
            Escape(_body, text, 0, text.Length);
            _body.Append('}');
        }

        /// <summary>
        /// Starts a paragraph with its own indents and spacing, in twips (1/20 pt). A positive
        /// <paramref name="leftIndent"/> with a negative <paramref name="firstLine"/> makes a hanging
        /// indent — a bullet at the margin and every wrapped line aligned under the text after it —
        /// with a tab stop placed where that text begins.
        /// </summary>
        public void Paragraph(int leftIndent = 0, int firstLine = 0, int spaceBefore = 0, int spaceAfter = 0)
        {
            _body.Append(@"\pard");
            if (leftIndent != 0) { _body.Append(@"\li").Append(leftIndent); }
            if (firstLine != 0) { _body.Append(@"\fi").Append(firstLine); }
            if (spaceBefore != 0) { _body.Append(@"\sb").Append(spaceBefore); }
            if (spaceAfter != 0) { _body.Append(@"\sa").Append(spaceAfter); }
            if (leftIndent > 0) { _body.Append(@"\tx").Append(leftIndent); }
            _body.Append(' ');
        }

        /// <summary>Ends the current line (a paragraph mark, as AppendText(line + NewLine) left).</summary>
        public void EndLine()
        {
            _body.Append(@"\par").Append('\n');
        }

        public string ToRtf()
        {
            var rtf = new StringBuilder(_body.Length + 256);
            rtf.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil ").Append(_fontName).Append(";}");
            if (_usesMono)
            {
                rtf.Append(@"{\f1\fmodern ").Append(MonoFont).Append(";}");
            }
            rtf.Append(@"}{\colortbl ;");
            foreach (int argb in _palette)
            {
                Color c = Color.FromArgb(argb);
                rtf.Append(@"\red").Append(c.R).Append(@"\green").Append(c.G).Append(@"\blue").Append(c.B).Append(';');
            }
            rtf.Append(@"}\f0\fs").Append(_halfPoints).Append(_style).Append(' ').Append(_body).Append('}');
            return rtf.ToString();
        }

        /// <summary>
        /// Text escaped for RTF: the three control characters, tabs, line breaks, and every other
        /// non-ASCII UTF-16 unit as \uN? — a surrogate pair as two, which RichEdit puts back together
        /// (verified with 🔒 and accented text).
        ///
        /// Line breaks are paragraph marks — CR, LF and CR LF one each, as AppendText made of them. A log
        /// entry that carries an exception holds its stack trace on lines of its own, and its "\r\n" went
        /// out as \u13?\u10?: RichEdit refuses that pair, and with it the WHOLE string ("Cannot load the
        /// text", scratchpad/debugcheck rtfprobe). Live debug then fell back to filling its log a line at a
        /// time — the seconds-long freeze RtfBuilder exists to remove — on every rebuild while such an
        /// entry was among the last 1,500 lines, and an append left half the entry glued to the line above.
        /// NUL is refused the same way; it is shown as ␀.
        /// </summary>
        private static void Escape(StringBuilder sb, string s, int start, int length)
        {
            int end = Math.Min(s.Length, start + Math.Max(0, length));
            for (int i = Math.Max(0, start); i < end; i++)
            {
                char ch = s[i];
                if (ch == '\\' || ch == '{' || ch == '}') { sb.Append('\\').Append(ch); }
                else if (ch == '\t') { sb.Append(@"\tab "); }
                else if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\r' && i + 1 < end && s[i + 1] == '\n') { i++; }
                    sb.Append(@"\par ");
                }
                else if (ch == '\0') { sb.Append(@"鈖?"); }
                else if (ch > 127 || ch < 32) { sb.Append(@"\u").Append((short)ch).Append('?'); }
                else { sb.Append(ch); }
            }
        }
    }
}
