using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoClicker.UI
{
    /// <summary>
    /// Release notes as the update dialog shows them.
    ///
    /// The notes are Markdown, written for GitHub's release page. The dialog used to strip a few
    /// markers and put the rest in a plain text box, so what people read was littered with the
    /// syntax: `accessCode` in backticks, *italics* in asterisks, a "1.0.322" heading repeating
    /// the version printed right above it, and bold lead-ins ("Fixed: …") flattened into the
    /// sentence after them. Here the same Markdown is read into blocks and drawn as formatted text
    /// — headings, bold, italic, code, bullets with a hanging indent — in one RTF assignment.
    ///
    /// Deliberately small: only what the notes actually use. Anything it does not recognise is
    /// shown as the text it is, never dropped.
    /// </summary>
    internal static class ReleaseNotes
    {
        internal enum BlockKind { Heading, Paragraph, Bullet }

        internal sealed class Span
        {
            public string Text;
            public bool Bold;
            public bool Italic;
            public bool Code;
        }

        internal sealed class Block
        {
            public BlockKind Kind;
            /// <summary>Heading level (1–6), or a bullet's nesting (0 or 1).</summary>
            public int Level;
            /// <summary>A bullet's marker: "•", or the number of a numbered item ("2.").</summary>
            public string Marker;
            public readonly StringBuilder Source = new StringBuilder();
            public List<Span> Spans;
        }

        private static readonly Regex HeadingLine = new Regex(@"^\s{0,3}(#{1,6})\s+(.*?)\s*#*\s*$");
        private static readonly Regex BulletLine = new Regex(@"^(\s*)([-*+])\s+(.*)$");
        private static readonly Regex NumberedLine = new Regex(@"^(\s*)(\d{1,3})[.)]\s+(.*)$");
        private static readonly Regex RuleLine = new Regex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$");

        /// <summary>
        /// Reads Markdown into blocks. A heading that only restates <paramref name="version"/>
        /// ("1.0.323", "v1.0.323", "Tempo 1.0.323") is left out — the dialog shows the version
        /// already — and so is the old unsigned-publisher footer, which is about the browser
        /// download and means nothing inside Tempo.
        /// </summary>
        internal static List<Block> Parse(string markdown, string version)
        {
            var blocks = new List<Block>();
            if (string.IsNullOrWhiteSpace(markdown)) { return blocks; }

            Block current = null;
            foreach (string raw in markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Trim().Length == 0)
                {
                    current = null;      // a blank line ends the paragraph or item
                    continue;
                }
                if (RuleLine.IsMatch(line))
                {
                    current = null;
                    continue;
                }

                Match m = HeadingLine.Match(line);
                if (m.Success)
                {
                    string text = m.Groups[2].Value.Trim();
                    current = null;
                    if (RestatesVersion(text, version)) { continue; }
                    var h = new Block { Kind = BlockKind.Heading, Level = m.Groups[1].Value.Length };
                    h.Source.Append(text);
                    blocks.Add(h);
                    continue;
                }

                m = BulletLine.Match(line);
                Match n = m.Success ? m : NumberedLine.Match(line);
                if (n.Success)
                {
                    bool numbered = !m.Success;
                    current = new Block
                    {
                        Kind = BlockKind.Bullet,
                        Level = n.Groups[1].Value.Replace("\t", "    ").Length >= 2 ? 1 : 0,
                        Marker = numbered ? n.Groups[2].Value + "." : "•"
                    };
                    current.Source.Append(n.Groups[3].Value.Trim());
                    blocks.Add(current);
                    continue;
                }

                // A line that is not a new block continues the one before it (Markdown's "lazy"
                // continuation), so a paragraph wrapped in the source reads as one paragraph.
                if (current != null)
                {
                    current.Source.Append(' ').Append(line.Trim());
                    continue;
                }

                current = new Block { Kind = BlockKind.Paragraph };
                current.Source.Append(line.Trim());
                blocks.Add(current);
            }

            // The unsigned-publisher footer, whichever way it was worded.
            blocks.RemoveAll(b => b.Source.ToString().IndexOf("Unknown publisher", StringComparison.OrdinalIgnoreCase) >= 0);

            foreach (Block b in blocks)
            {
                b.Spans = Inline(b.Source.ToString());
            }
            blocks.RemoveAll(b => b.Spans.Count == 0);
            return blocks;
        }

        /// <summary>True for a heading that says nothing but the version ("1.0.323", "Tempo v1.0.323").</summary>
        private static bool RestatesVersion(string heading, string version)
        {
            if (string.IsNullOrWhiteSpace(heading)) { return true; }
            string h = heading.Replace("*", "").Trim();
            if (h.StartsWith("Tempo", StringComparison.OrdinalIgnoreCase)) { h = h.Substring(5).Trim(); }
            if (string.IsNullOrWhiteSpace(version))
            {
                return Regex.IsMatch(h, @"^v?\d+(\.\d+){1,3}$", RegexOptions.IgnoreCase);
            }
            return Utils.UpdateChecker.IsSameVersion(h, version);
        }

        /// <summary>The formatted runs in one line of Markdown: **bold**, *italic*, `code`, [links](url).</summary>
        internal static List<Span> Inline(string text)
        {
            var spans = new List<Span>();
            ParseInline(text ?? "", false, false, spans);

            // Adjacent runs with the same look become one — fewer RTF groups, and a plain-text
            // comparison that is not sensitive to where the parser happened to split.
            var merged = new List<Span>();
            foreach (Span s in spans)
            {
                if (string.IsNullOrEmpty(s.Text)) { continue; }
                Span last = merged.Count > 0 ? merged[merged.Count - 1] : null;
                if (last != null && last.Bold == s.Bold && last.Italic == s.Italic && last.Code == s.Code)
                {
                    last.Text += s.Text;
                }
                else
                {
                    merged.Add(new Span { Text = s.Text, Bold = s.Bold, Italic = s.Italic, Code = s.Code });
                }
            }
            return merged;
        }

        private static void ParseInline(string s, bool bold, bool italic, List<Span> into)
        {
            var plain = new StringBuilder();
            void Flush()
            {
                if (plain.Length == 0) { return; }
                into.Add(new Span { Text = plain.ToString(), Bold = bold, Italic = italic });
                plain.Clear();
            }

            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];

                // A backslash makes the next punctuation mark literal.
                if (c == '\\' && i + 1 < s.Length && "\\`*_[]()#+-.!<>{}".IndexOf(s[i + 1]) >= 0)
                {
                    plain.Append(s[i + 1]);
                    i += 2;
                    continue;
                }

                // `code` — nothing inside it is read as formatting. A run of N backticks is closed
                // by the next run of exactly N, so ``a `quoted` word`` works too.
                if (c == '`')
                {
                    int run = RunLength(s, i, '`');
                    int close = FindBacktickRun(s, i + run, run);
                    if (close > i)
                    {
                        Flush();
                        string code = s.Substring(i + run, close - i - run);
                        if (run > 1 && code.Length > 2 && code[0] == ' ' && code[code.Length - 1] == ' ')
                        {
                            code = code.Substring(1, code.Length - 2);
                        }
                        into.Add(new Span { Text = code, Bold = bold, Italic = italic, Code = true });
                        i = close + run;
                        continue;
                    }
                    plain.Append('`', run);
                    i += run;
                    continue;
                }

                // **bold** — only when it is closed later in the same line; a stray pair stays text.
                if (c == '*' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    int close = s.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (close > i + 2)
                    {
                        Flush();
                        ParseInline(s.Substring(i + 2, close - i - 2), true, italic, into);
                        i = close + 2;
                        continue;
                    }
                    plain.Append("**");
                    i += 2;
                    continue;
                }

                // *italic* — opened only by an asterisk with text straight after it, so "2 * 3"
                // and a list of "* " markers are left alone.
                if (c == '*' && i + 1 < s.Length && !char.IsWhiteSpace(s[i + 1]))
                {
                    int close = FindClosingStar(s, i + 1);
                    if (close > i + 1)
                    {
                        Flush();
                        ParseInline(s.Substring(i + 1, close - i - 1), bold, true, into);
                        i = close + 1;
                        continue;
                    }
                }

                // [text](url) — the text is what a reader wants; the address is for GitHub's page.
                if (c == '[')
                {
                    int closeBracket = s.IndexOf(']', i + 1);
                    if (closeBracket > i + 1 && closeBracket + 1 < s.Length && s[closeBracket + 1] == '(')
                    {
                        int closeParen = s.IndexOf(')', closeBracket + 2);
                        if (closeParen > closeBracket)
                        {
                            Flush();
                            ParseInline(s.Substring(i + 1, closeBracket - i - 1), bold, italic, into);
                            i = closeParen + 1;
                            continue;
                        }
                    }
                }

                // <https://…> — an autolink; show the address without the brackets.
                if (c == '<')
                {
                    int close = s.IndexOf('>', i + 1);
                    if (close > i + 1)
                    {
                        string inner = s.Substring(i + 1, close - i - 1);
                        if (inner.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                            inner.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            plain.Append(inner);
                            i = close + 1;
                            continue;
                        }
                    }
                }

                plain.Append(c);
                i++;
            }
            Flush();
        }

        private static int RunLength(string s, int at, char ch)
        {
            int n = 0;
            while (at + n < s.Length && s[at + n] == ch) { n++; }
            return n;
        }

        private static int FindBacktickRun(string s, int from, int run)
        {
            int i = from;
            while (i < s.Length)
            {
                if (s[i] == '`')
                {
                    int len = RunLength(s, i, '`');
                    if (len == run) { return i; }
                    i += len;
                    continue;
                }
                i++;
            }
            return -1;
        }

        /// <summary>The asterisk that closes an italic span: text right before it, and not half of a "**".</summary>
        private static int FindClosingStar(string s, int from)
        {
            for (int i = from; i < s.Length; i++)
            {
                if (s[i] == '`')
                {
                    // Skip over code — an asterisk inside it is not formatting.
                    int run = RunLength(s, i, '`');
                    int close = FindBacktickRun(s, i + run, run);
                    if (close < 0) { return -1; }
                    i = close + run - 1;
                    continue;
                }
                if (s[i] != '*') { continue; }
                if (i + 1 < s.Length && s[i + 1] == '*') { i++; continue; }   // part of a bold marker
                if (!char.IsWhiteSpace(s[i - 1])) { return i; }
            }
            return -1;
        }

        /// <summary>
        /// Exactly the text the formatted view shows: one line per block, a bullet's marker then a
        /// tab. With <paramref name="spaced"/>, blocks are separated by blank lines instead, for a
        /// plain text box that has no paragraph spacing of its own.
        /// </summary>
        internal static string ToPlainText(List<Block> blocks, bool spaced = false)
        {
            var sb = new StringBuilder();
            foreach (Block b in blocks)
            {
                if (spaced && sb.Length > 0) { sb.Append('\n'); }
                if (b.Kind == BlockKind.Bullet)
                {
                    // Nesting is an indent in the formatted view, not characters — so only the
                    // plain text box gets spaces for it.
                    if (spaced && b.Level > 0) { sb.Append("    "); }
                    sb.Append(b.Marker).Append(spaced ? " " : "\t");
                }
                foreach (Span s in b.Spans) { sb.Append(s.Text); }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// The blocks as ONE RTF document for a RichTextBox in <paramref name="font"/> — assigned in
        /// one go, which is also what keeps a long set of notes from filling slowly (see RtfBuilder).
        /// </summary>
        internal static string ToRtf(List<Block> blocks, Font font, Theme theme)
        {
            var rtf = new RtfBuilder(font, 16384);
            Color text = theme.Text;
            Color marker = theme.Accent;
            bool first = true;

            foreach (Block b in blocks)
            {
                switch (b.Kind)
                {
                    case BlockKind.Heading:
                        rtf.Paragraph(spaceBefore: first ? 0 : 200, spaceAfter: 80);
                        foreach (Span s in b.Spans)
                        {
                            rtf.StyledRun(s.Text, text, bold: true, italic: s.Italic, mono: s.Code,
                                          sizeScale: b.Level <= 2 ? 1.2f : 1.08f);
                        }
                        break;

                    case BlockKind.Bullet:
                        // Hanging indent: the marker in the margin, every wrapped line under the text.
                        int left = 330 + b.Level * 330;
                        rtf.Paragraph(leftIndent: left, firstLine: -230, spaceAfter: 110);
                        rtf.StyledRun(b.Marker + "\t", marker, bold: true);
                        foreach (Span s in b.Spans)
                        {
                            rtf.StyledRun(s.Text, text, s.Bold, s.Italic, s.Code);
                        }
                        break;

                    default:
                        rtf.Paragraph(spaceAfter: 140);
                        foreach (Span s in b.Spans)
                        {
                            rtf.StyledRun(s.Text, text, s.Bold, s.Italic, s.Code);
                        }
                        break;
                }
                rtf.EndLine();
                first = false;
            }
            return rtf.ToRtf();
        }
    }
}
