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
                ControlStyles.ResizeRedraw,
                true);
            Height = 64;
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

            using (var track = new SolidBrush(TrackColor))
            using (var path = Rounded(barRect, 8))
            {
                g.FillPath(track, path);
            }

            if (total > 0)
            {
                float x = 0;
                x = DrawSegment(g, barRect, x, _left / (float)total, LeftColor, total);
                x = DrawSegment(g, barRect, x, _right / (float)total, RightColor, total);
                DrawSegment(g, barRect, x, _middle / (float)total, MiddleColor, total);
            }

            // Legend
            int ly = 34;
            using (var font = new Font("Segoe UI", 8.5f, FontStyle.Regular))
            {
                float lx = 0;
                lx = DrawLegend(g, font, lx, ly, LeftColor, "Left", _left, total);
                lx = DrawLegend(g, font, lx, ly, RightColor, "Right", _right, total);
                DrawLegend(g, font, lx, ly, MiddleColor, "Middle", _middle, total);
            }
        }

        private float DrawSegment(Graphics g, Rectangle bar, float startX, float fraction, Color color, long total)
        {
            if (fraction <= 0)
            {
                return startX;
            }

            float w = (bar.Width) * fraction;
            using (var brush = new SolidBrush(color))
            {
                g.FillRectangle(brush, startX, bar.Y, w, bar.Height);
            }
            return startX + w;
        }

        private float DrawLegend(Graphics g, Font font, float x, int y, Color color, string label, long value, long total)
        {
            using (var sw = new SolidBrush(color))
            {
                g.FillRectangle(sw, x, y + 2, 10, 10);
            }

            double pct = total > 0 ? value * 100.0 / total : 0;
            string text = $"{label}  {value:N0}  ({pct:0}%)";

            using (var tb = new SolidBrush(TextColor))
            {
                g.DrawString(text, font, tb, x + 14, y);
            }

            SizeF sz = g.MeasureString(text, font);
            return x + 14 + sz.Width + 18;
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
