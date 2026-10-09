using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A multi-line note whose lines can each have their own colour: a status line in the muted
    /// colour, a "✓" in the success colour and a "⚠" under it in the warning colour, in one label.
    ///
    /// A plain Label has one ForeColor, so a note with one warning in it went amber from top to
    /// bottom — the Settings display note painted "✓ Colours corrected for HDR" in the warning
    /// colour because the line under it was a warning. Lines wrap within the label's width exactly
    /// as the Label would wrap them (the same TextRenderer flags), stacked one under the next.
    ///
    /// Without <see cref="LineColors"/> — or with one colour per line missing — it is an
    /// <see cref="EmojiLabel"/> and paints the way a Label always has. Text stays the whole note,
    /// so screen readers and copy-out still get every line.
    /// </summary>
    public class NoteLabel : EmojiLabel
    {
        private Color[] _lineColors;

        /// <summary>One colour per line of <see cref="Control.Text"/> (split on '\n').</summary>
        public Color[] LineColors
        {
            get { return _lineColors; }
            set { _lineColors = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            string[] lines = (Text ?? "").Split('\n');
            if (_lineColors == null || _lineColors.Length != lines.Length || !Enabled
                || Image != null || RightToLeft == RightToLeft.Yes)
            {
                base.OnPaint(e);
                return;
            }

            TextFormatFlags flags = TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.WordBreak
                                  | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;
            int width = Math.Max(0, ClientSize.Width - Padding.Horizontal);
            int y = Padding.Top;
            for (int i = 0; i < lines.Length && y < ClientSize.Height; i++)
            {
                string line = lines[i].Length == 0 ? " " : lines[i];
                int h = TextRenderer.MeasureText(e.Graphics, line, Font, new Size(width, int.MaxValue), flags).Height;
                var face = new Rectangle(Padding.Left, y, width, h);
                if (ColorEmoji.Available && ColorEmoji.Contains(line))
                {
                    ColorEmoji.DrawText(e.Graphics, line, Font, face, _lineColors[i], flags);
                }
                else
                {
                    TextRenderer.DrawText(e.Graphics, line, Font, face, _lineColors[i], flags);
                }
                y += h;
            }
        }
    }
}
