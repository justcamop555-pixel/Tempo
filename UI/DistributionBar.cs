using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A horizontal stacked bar visualising the proportion of left / right /
    /// middle clicks, with a small legend showing each percentage.
    /// </summary>
    public sealed class DistributionBar : Control
    {
        private long _left, _right, _middle;

        public Color TrackColor { get; set; } = Color.FromArgb(34, 38, 54);
        public Color LeftColor { get; set; } = Color.FromArgb(124, 92, 255);
        public Color RightColor { get; set; } = Color.FromArgb(56, 217, 169);
        public Color MiddleColor { get; set; } = Color.FromArgb(251, 191, 36);
        public Color TextColor { get; set; } = Color.FromArgb(232, 236, 246);
        public Color MutedColor { get; set; } = Color.FromArgb(132, 142, 168);

        public DistributionBar()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                // Required before BackColor = Transparent means anything on a control
                // that paints itself; without it WinForms just fills the parent colour.
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            Height = 64;
        }

        /// <summary>
        /// Fills the buffer with whatever is behind the control — the wallpaper slice
        /// when a background image or GIF is showing, the flat page colour otherwise.
        ///
        /// This control used to erase to its own opaque BackColor, so with a GIF backdrop
        /// it stamped a solid slab across the animation: the bar and its legend sat on a
        /// grey rectangle that matched nothing around it, while the GIF carried on either
        /// side. Clearing to the actual backdrop is what makes it sit ON the image
        /// instead of punching a hole in it.
        /// </summary>
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            ModernPaint.PaintTransparentBackdrop(this, e.Graphics);
        }

        public void SetValues(long left, long right, long middle)
        {
            _left = left < 0 ? 0 : left;
            _right = right < 0 ? 0 : right;
            _middle = middle < 0 ? 0 : middle;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            long total = _left + _right + _middle;
            var barRect = new Rectangle(0, 0, Width - 1, 22);

            using (var path = Rounded(barRect, 8))
            {
                using (var track = new SolidBrush(TrackColor))
                {
                    g.FillPath(track, path);
                }

                if (total > 0)
                {
                    // Clip the segments to the rounded track. They were filled as plain
                    // rectangles, so the first and last one squared off the bar's rounded
                    // ends — the fill visibly overhung the corners it was supposed to sit
                    // inside.
                    var saved = g.Save();
                    g.SetClip(path, CombineMode.Intersect);
                    float x = 0;
                    x = DrawSegment(g, barRect, x, _left / (float)total, LeftColor);
                    x = DrawSegment(g, barRect, x, _right / (float)total, RightColor);
                    DrawSegment(g, barRect, x, _middle / (float)total, MiddleColor);
                    g.Restore(saved);
                }
                else
                {
                    // Nothing recorded yet. An empty track reads as "broken" without a
                    // word to say otherwise.
                    using (var font = new Font("Segoe UI", 8f, FontStyle.Italic))
                    {
                        TextRenderer.DrawText(g, "No clicks recorded yet", font, barRect,
                            MutedColor,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    }
                }
            }

            // Legend. Laid out in one pass so it can be COMPACTED if it doesn't fit:
            // with lifetime-sized numbers ("Left 10,876,670 (100%)") three entries ran
            // past the right edge and the last one was clipped mid-word.
            int ly = 34;
            using (var font = new Font("Segoe UI", 8.5f, FontStyle.Regular))
            {
                var entries = new[]
                {
                    (Colour: LeftColor,   Label: "Left",   Value: _left),
                    (Colour: RightColor,  Label: "Right",  Value: _right),
                    (Colour: MiddleColor, Label: "Middle", Value: _middle)
                };

                // Measure at full detail first; drop the percentage, then the count, only
                // if the row would otherwise overflow.
                int detail = 2;
                for (; detail >= 0; detail--)
                {
                    int need = 0;
                    foreach (var en in entries)
                    {
                        need += LegendWidth(g, font, LegendText(en.Label, en.Value, total, detail));
                    }
                    if (need <= Width) { break; }
                }
                if (detail < 0) { detail = 0; }

                float lx = 0;
                foreach (var en in entries)
                {
                    lx = DrawLegend(g, font, lx, ly, en.Colour,
                                    LegendText(en.Label, en.Value, total, detail));
                }
            }
        }

        /// <summary>Legend caption at the requested level of detail (2 = fullest).</summary>
        private static string LegendText(string label, long value, long total, int detail)
        {
            double pct = total > 0 ? value * 100.0 / total : 0;
            if (detail >= 2) { return $"{label}  {value:N0}  ({pct:0}%)"; }
            if (detail == 1) { return $"{label}  {value:N0}"; }
            return label;
        }

        private static int LegendWidth(Graphics g, Font font, string text)
        {
            // TextRenderer, matching the draw call — MeasureString uses different metrics
            // and its own padding, so measuring with one and drawing with the other is
            // what let the entries creep into each other.
            return 14 + TextRenderer.MeasureText(g, text, font, new Size(int.MaxValue, int.MaxValue),
                                                 LegendFlags).Width + 18;
        }

        private const TextFormatFlags LegendFlags =
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        private float DrawSegment(Graphics g, Rectangle bar, float startX, float fraction, Color color)
        {
            if (fraction <= 0)
            {
                return startX;
            }

            float w = bar.Width * fraction;
            using (var brush = new SolidBrush(color))
            {
                g.FillRectangle(brush, startX, bar.Y, w, bar.Height);
            }
            return startX + w;
        }

        private float DrawLegend(Graphics g, Font font, float x, int y, Color color, string text)
        {
            // Rounded swatch: a square chip next to rounded bar segments looked unfinished.
            var chip = new Rectangle((int)x, y + 2, 10, 10);
            using (var sw = new SolidBrush(color))
            using (var path = Rounded(chip, 3))
            {
                g.FillPath(sw, path);
            }

            TextRenderer.DrawText(g, text, font, new Point((int)x + 14, y - 1), TextColor, LegendFlags);
            return x + LegendWidth(g, font, text);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
