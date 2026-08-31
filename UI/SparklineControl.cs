using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A live rolling line chart. Call <see cref="Push"/> with the latest value
    /// each UI tick; the control keeps the most recent <see cref="Capacity"/>
    /// samples and draws them as a filled line graph that auto-scales to the
    /// largest visible sample. Used to visualise clicks-per-second over time.
    /// </summary>
    public sealed class SparklineControl : Control
    {
        private readonly Queue<double> _samples = new Queue<double>();

        public int Capacity { get; set; } = 150;
        public string Title { get; set; } = "Clicks per second";

        public Color CardColor { get; set; } = Color.FromArgb(24, 27, 39);
        public Color LineColor { get; set; } = Color.FromArgb(124, 92, 255);
        public Color FillColor { get; set; } = Color.FromArgb(60, 124, 92, 255);
        public Color GridColor { get; set; } = Color.FromArgb(48, 54, 74);
        public Color TextColor { get; set; } = Color.FromArgb(232, 236, 246);
        public Color MutedColor { get; set; } = Color.FromArgb(132, 142, 168);

        public SparklineControl()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            Size = new Size(700, 180);
        }

        /// <summary>Adds a sample to the right edge of the chart.</summary>
        public void Push(double value)
        {
            if (value < 0 || double.IsNaN(value) || double.IsInfinity(value))
            {
                value = 0;
            }

            _samples.Enqueue(value);
            while (_samples.Count > Capacity)
            {
                _samples.Dequeue();
            }

            Invalidate();
        }

        public void Clear()
        {
            _samples.Clear();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var card = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Rounded(card, 10))
            using (var fill = new SolidBrush(CardColor))
            {
                g.FillPath(fill, path);
            }

            // Plot area inset (leaving room for the title and a right-side scale).
            var plot = new Rectangle(14, 34, Width - 28 - 44, Height - 48);

            using (var titleBrush = new SolidBrush(MutedColor))
            using (var titleFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            {
                g.DrawString(Title.ToUpperInvariant(), titleFont, titleBrush, 14, 12);
            }

            // Determine the scale.
            double max = 1;
            foreach (double v in _samples)
            {
                if (v > max) max = v;
            }
            // Round the axis up to something tidy.
            double axisMax = NiceCeiling(max);

            // Horizontal grid lines + right-side scale labels.
            using (var gridPen = new Pen(GridColor))
            using (var scaleBrush = new SolidBrush(MutedColor))
            using (var scaleFont = new Font("Segoe UI", 7.5f, FontStyle.Regular))
            {
                int lines = 4;
                for (int i = 0; i <= lines; i++)
                {
                    float yy = plot.Top + plot.Height * i / (float)lines;
                    g.DrawLine(gridPen, plot.Left, yy, plot.Right, yy);

                    double label = axisMax * (lines - i) / lines;
                    g.DrawString(label.ToString("0.#"), scaleFont, scaleBrush, plot.Right + 4, yy - 7);
                }
            }

            if (_samples.Count >= 2)
            {
                var pts = new List<PointF>();
                int n = _samples.Count;
                int idx = 0;
                foreach (double v in _samples)
                {
                    float x = plot.Left + plot.Width * idx / (float)(Capacity - 1);
                    float y = plot.Bottom - (float)(v / axisMax) * plot.Height;
                    pts.Add(new PointF(x, y));
                    idx++;
                }

                // Filled area under the line.
                var fillPts = new List<PointF>(pts);
                fillPts.Add(new PointF(pts[pts.Count - 1].X, plot.Bottom));
                fillPts.Add(new PointF(pts[0].X, plot.Bottom));
                using (var areaBrush = new SolidBrush(FillColor))
                {
                    g.FillPolygon(areaBrush, fillPts.ToArray());
                }

                using (var linePen = new Pen(LineColor, 2f))
                {
                    g.DrawLines(linePen, pts.ToArray());
                }

                // Current value marker + label.
                PointF last = pts[pts.Count - 1];
                double cur = 0;
                foreach (double v in _samples) cur = v; // last sample
                using (var dotBrush = new SolidBrush(LineColor))
                {
                    g.FillEllipse(dotBrush, last.X - 3, last.Y - 3, 6, 6);
                }
                using (var curBrush = new SolidBrush(TextColor))
                using (var curFont = new Font("Segoe UI", 9f, FontStyle.Bold))
                {
                    g.DrawString(cur.ToString("0.0"), curFont, curBrush, 14, Height - 20);
                }
            }
            else
            {
                using (var emptyBrush = new SolidBrush(MutedColor))
                using (var emptyFont = new Font("Segoe UI", 9f, FontStyle.Italic))
                {
                    g.DrawString("Waiting for activity…", emptyFont, emptyBrush,
                        plot.Left + 8, plot.Top + plot.Height / 2 - 8);
                }
            }
        }

        private static double NiceCeiling(double value)
        {
            if (value <= 1) return 1;
            if (value <= 2) return 2;
            if (value <= 5) return 5;
            if (value <= 10) return 10;
            if (value <= 20) return 20;
            if (value <= 50) return 50;
            if (value <= 100) return 100;
            if (value <= 200) return 200;
            if (value <= 500) return 500;
            return Math.Ceiling(value / 1000.0) * 1000.0;
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
