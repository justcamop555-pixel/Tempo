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

        // 100% sizes, scaled by Px; the title row and the scale column as big as their text. Plain
        // pixels before: on a 150% laptop the title sat on the top grid line and the current value
        // in the corner lost its bottom under the card's edge.
        private float DpiScale => DeviceDpi / 96f;
        private int Px(float size96) => (int)Math.Round(size96 * DpiScale, MidpointRounding.AwayFromZero);

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
            using (var path = Rounded(card, Px(10)))
            using (var fill = new SolidBrush(CardColor))
            {
                g.FillPath(fill, path);
            }

            int titleH;
            float titleRight;
            using (var titleBrush = new SolidBrush(MutedColor))
            using (var titleFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            {
                string title = Title.ToUpperInvariant();
                g.DrawString(title, titleFont, titleBrush, Px(14), Px(12));
                titleH = (int)Math.Ceiling(titleFont.GetHeight(g));
                titleRight = Px(14) + g.MeasureString(title, titleFont).Width;
            }

            // Determine the scale.
            double max = 1;
            foreach (double v in _samples)
            {
                if (v > max) max = v;
            }
            // A tidy step and as few lines as reach the largest sample. It was always four quarters of 1, 2, 5,
            // 10…, and a quarter of 1 or 5 has two decimals: the scale read 1 / 0.8 / 0.5 / 0.3 / 0, with 0.75 shown
            // as "0.8" and 0.25 as "0.3" (the user's crop, 2026-10-18).
            double step = NiceStep(max, 5);
            int lines = Math.Max(1, (int)Math.Ceiling(max / step - 1e-9));
            double axisMax = step * lines;

            // Horizontal grid lines + right-side scale labels.
            Rectangle plot;
            using (var gridPen = new Pen(GridColor))
            using (var scaleBrush = new SolidBrush(MutedColor))
            using (var scaleFont = new Font("Segoe UI", 8.5f, FontStyle.Regular))
            {
                // Plot area inset, leaving room for the title and the scale: at 100% (14, 34) with a 44 px column.
                float widest = 0;
                for (int i = 0; i <= lines; i++)
                {
                    widest = Math.Max(widest, g.MeasureString(ScaleLabel(step, lines - i), scaleFont).Width);
                }
                int scaleCol = Math.Max(Px(44), (int)Math.Ceiling(widest) + Px(4));
                int plotTop = Px(12) + titleH + Px(6);
                plot = new Rectangle(Px(14), plotTop, Width - Px(28) - scaleCol, Height - plotTop - Px(14));

                for (int i = 0; i <= lines; i++)
                {
                    float yy = plot.Top + plot.Height * i / (float)lines;
                    g.DrawLine(gridPen, plot.Left, yy, plot.Right, yy);
                    g.DrawString(ScaleLabel(step, lines - i), scaleFont, scaleBrush, plot.Right + Px(4), yy - Px(7));
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

                using (var linePen = new Pen(LineColor, 2f * DpiScale))
                {
                    g.DrawLines(linePen, pts.ToArray());
                }

                // Current value marker + label.
                PointF last = pts[pts.Count - 1];
                double cur = 0;
                foreach (double v in _samples) cur = v; // last sample
                using (var dotBrush = new SolidBrush(LineColor))
                {
                    g.FillEllipse(dotBrush, last.X - Px(3), last.Y - Px(3), Px(6), Px(6));
                }
                // The current value goes after the title, in the title's font. It was in the bottom-left corner,
                // on the zero line: whenever the rate was 0 the line ran straight through "0.0".
                using (var curBrush = new SolidBrush(TextColor))
                using (var curFont = new Font("Segoe UI", 9f, FontStyle.Bold))
                {
                    g.DrawString(cur.ToString("0.0"), curFont, curBrush, titleRight + Px(6), Px(12));
                }
            }
            else
            {
                using (var emptyBrush = new SolidBrush(MutedColor))
                using (var emptyFont = new Font("Segoe UI", 9f, FontStyle.Italic))
                {
                    g.DrawString(Utils.Localization.T("Waiting for activity…"), emptyFont, emptyBrush,
                        plot.Left + Px(8), plot.Top + plot.Height / 2 - Px(8));
                }
            }
        }

        /// <summary>
        /// The smallest step of 1, 2 or 5 times a power of ten that reaches <paramref name="max"/> in at most
        /// <paramref name="maxLines"/> steps. The step before it needed more than that, so it always takes at
        /// least three; with <paramref name="max"/> at 1 or more, a step has at most one decimal.
        /// </summary>
        internal static double NiceStep(double max, int maxLines)
        {
            if (!(max > 0) || double.IsInfinity(max)) { return 1; }
            double step = Math.Pow(10, Math.Floor(Math.Log10(max / maxLines)));
            double[] mantissas = { 1, 2, 5 };
            for (int guard = 0; guard < 40; guard++)
            {
                double candidate = step * mantissas[guard % 3];
                if (Math.Ceiling(max / candidate - 1e-9) <= maxLines) { return candidate; }
                if (guard % 3 == 2) { step *= 10; }
            }
            return max / maxLines;
        }

        // One scale line's label: the step times the line's number, so 0.2 × 3 is "0.6", never 0.6000000000000001.
        internal static string ScaleLabel(double step, int multiple) => (step * multiple).ToString("0.#");

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
