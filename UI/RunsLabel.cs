using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A label whose text comes in coloured pieces — "● Connected: in Blox Fruits as Main, since 10:21" with the dot and
    /// the verdict in the state's colour, the game bright and bold, the account in the accent and the time muted. A plain
    /// Label has one colour for all of it, and a line that says four things in one grey read as nothing in particular.
    ///
    /// Each piece names a theme ROLE, not a colour, and the roles are read at every paint, so a theme switch (or the HDR
    /// palette) repaints it right. The text wraps at spaces onto as many lines as the label's height holds; the last one
    /// ends with "…" when the rest doesn't fit — the tooltip should carry the whole. Transparent, like the labels around it.
    /// </summary>
    internal sealed class RunsLabel : Control
    {
        /// <summary>One piece of the line: its words, the theme colour they take, and whether they are bold.</summary>
        public sealed class Run
        {
            public readonly string Text;
            public readonly Func<Theme, Color> Role;
            public readonly bool Bold;

            public Run(string text, Func<Theme, Color> role, bool bold = false)
            {
                Text = text ?? "";
                Role = role;
                Bold = bold;
            }
        }

        private List<Run> _runs = new List<Run>();
        private Font _bold;

        /// <summary>The theme the pieces are coloured from, read at every paint.</summary>
        public Func<Theme> ThemeSource { get; set; }

        public RunsLabel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.Transparent;
            TabStop = false;
            AccessibleRole = AccessibleRole.StaticText;
        }

        /// <summary>The pieces to show. <see cref="Control.Text"/> becomes all of them in a row — what a screen reader, the
        /// log and the layout checks read.</summary>
        public void SetRuns(IEnumerable<Run> runs)
        {
            var list = runs != null ? new List<Run>(runs) : new List<Run>();
            if (Same(list)) { return; }
            _runs = list;
            Text = string.Concat(list.ConvertAll(r => r.Text));
            Invalidate();
        }

        private bool Same(List<Run> other)
        {
            if (other.Count != _runs.Count) { return false; }
            for (int i = 0; i < other.Count; i++)
            {
                Run a = other[i], b = _runs[i];
                // Roles are compared by what they give for the theme now: a new lambda for the same colour is the same.
                Theme t = ThemeSource?.Invoke();
                if (a.Text != b.Text || a.Bold != b.Bold
                    || (t != null && a.Role != null && b.Role != null && a.Role(t) != b.Role(t))
                    || (a.Role == null) != (b.Role == null))
                {
                    return false;
                }
            }
            return true;
        }

        private Font BoldFont => _bold ?? (_bold = new Font(Font, FontStyle.Bold));

        protected override void OnFontChanged(EventArgs e)
        {
            _bold?.Dispose();
            _bold = null;
            base.OnFontChanged(e);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _bold?.Dispose();
                _bold = null;
            }
            base.Dispose(disposing);
        }

        private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                                              | TextFormatFlags.PreserveGraphicsClipping;

        private static readonly Regex Pieces = new Regex(@"\s+|\S+", RegexOptions.CultureInvariant);

        /// <summary>A word (or a gap between words) placed on a line.</summary>
        private struct Placed
        {
            public string Text;
            public Font Font;
            public Color Color;
            public int X, Width;
            public bool Space;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_runs.Count == 0 || ClientSize.Width <= 0) { return; }
            Graphics g = e.Graphics;
            Theme theme = ThemeSource?.Invoke();
            int width = ClientSize.Width;
            int lineHeight = Math.Max(1, TextRenderer.MeasureText(g, "Ag", Font, Size.Empty, Flags).Height + 1);
            int maxLines = Math.Max(1, ClientSize.Height / lineHeight);

            // Lay every word out at its own width, wrapping at the gaps between words.
            var lines = new List<List<Placed>>();
            var line = new List<Placed>();
            int x = 0;
            foreach (Run run in _runs)
            {
                Font font = run.Bold ? BoldFont : Font;
                Color color = theme != null && run.Role != null ? run.Role(theme) : ForeColor;
                foreach (Match m in Pieces.Matches(run.Text))
                {
                    string piece = m.Value;
                    bool space = char.IsWhiteSpace(piece[0]);
                    if (space && x == 0) { continue; }   // no line starts with a gap
                    int w = TextRenderer.MeasureText(g, space ? " " : piece, font, Size.Empty, Flags).Width * (space ? piece.Length : 1);
                    if (!space && x > 0 && x + w > width)
                    {
                        TrimGap(line);
                        lines.Add(line);
                        line = new List<Placed>();
                        x = 0;
                    }
                    line.Add(new Placed { Text = piece, Font = font, Color = color, X = x, Width = w, Space = space });
                    x += w;
                }
            }
            TrimGap(line);
            if (line.Count > 0) { lines.Add(line); }

            // More lines than fit: the last one that shows ends with "…".
            if (lines.Count > maxLines)
            {
                lines.RemoveRange(maxLines, lines.Count - maxLines);
                List<Placed> last = lines[maxLines - 1];
                Color tail = last.Count > 0 ? last[last.Count - 1].Color : ForeColor;
                int dots = TextRenderer.MeasureText(g, "…", Font, Size.Empty, Flags).Width;
                while (last.Count > 0 && (last[last.Count - 1].Space || last[last.Count - 1].X + last[last.Count - 1].Width + dots > width))
                {
                    last.RemoveAt(last.Count - 1);
                }
                int end = last.Count > 0 ? last[last.Count - 1].X + last[last.Count - 1].Width : 0;
                last.Add(new Placed { Text = "…", Font = Font, Color = tail, X = end, Width = dots });
            }

            for (int i = 0; i < lines.Count; i++)
            {
                int y = i * lineHeight;
                foreach (Placed p in lines[i])
                {
                    if (p.Space) { continue; }
                    // A word wider than the whole line is cut at the edge with "…" rather than drawn past it.
                    if (p.X + p.Width > width)
                    {
                        TextRenderer.DrawText(g, p.Text, p.Font, new Rectangle(p.X, y, Math.Max(0, width - p.X), lineHeight), p.Color,
                                              Flags | TextFormatFlags.EndEllipsis);
                        continue;
                    }
                    TextRenderer.DrawText(g, p.Text, p.Font, new Point(p.X, y), p.Color, Flags);
                }
            }
        }

        private static void TrimGap(List<Placed> line)
        {
            while (line.Count > 0 && line[line.Count - 1].Space) { line.RemoveAt(line.Count - 1); }
        }
    }
}
