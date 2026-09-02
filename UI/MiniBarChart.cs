using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A compact bar chart, oldest bar on the left and newest on the right, used to
    /// visualise the click counts of recent sessions. Auto-scales to the tallest
    /// value and labels the maximum.
    /// </summary>
    public sealed class MiniBarChart : Control
    {
        private long[] _values = new long[0];
        private string[] _labels = new string[0];
        private int _hoverIndex = -1;
        private readonly ToolTip _tip = new ToolTip();

        public string Title { get; set; } = "";
        public Color CardColor { get; set; } = Color.FromArgb(24, 27, 39);
        public Color BarColor { get; set; } = Color.FromArgb(124, 92, 255);
        public Color TextColor { get; set; } = Color.FromArgb(232, 236, 246);
        public Color MutedColor { get; set; } = Color.FromArgb(132, 142, 168);

        public MiniBarChart()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            Height = 120;
        }

        /// <summary>Sets the bar values (expected oldest-first), with optional labels.</summary>
        public void SetValues(IList<long> values, IList<string> labels = null)
        {
            if (values == null)
            {
                _values = new long[0];
            }
            else
            {
                _values = new long[values.Count];
                for (int i = 0; i < values.Count; i++)
                {
                    _values[i] = values[i] < 0 ? 0 : values[i];
                }
            }

            if (labels == null)
            {
                _labels = new string[0];
            }
            else
            {
                _labels = new string[labels.Count];
                for (int i = 0; i < labels.Count; i++)
                {
                    _labels[i] = labels[i] ?? "";
                }
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_values.Length == 0)
            {
                return;
            }

            // Map the cursor to a bar slot and show its exact value as a tooltip.
            var plot = new Rectangle(14, 32, Width - 28 - 40, Height - 44 - (_labels.Length > 0 ? 16 : 0));
            int idx = -1;
            if (e.X >= plot.Left && e.X <= plot.Right && plot.Width > 0)
            {
                idx = (int)((e.X - plot.Left) / (plot.Width / (float)_values.Length));
                if (idx < 0 || idx >= _values.Length) idx = -1;
            }

            if (idx != _hoverIndex)
            {
                _hoverIndex = idx;
                if (idx >= 0)
                {
                    string lab = idx < _labels.Length && !string.IsNullOrEmpty(_labels[idx]) ? _labels[idx] + ": " : "";
                    _tip.Show($"{lab}{_values[idx]:N0} clicks", this, e.X + 12, e.Y + 8, 1200);
                }
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverIndex = -1;
            _tip.Hide(this);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var card = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Rounded(card, 10))
            {
                using (var bg = new LinearGradientBrush(new Rectangle(0, 0, Width, Height),
                           Brighten(CardColor, 0.05), CardColor, LinearGradientMode.Vertical))
                {
                    g.FillPath(bg, path);
                }
                using (var border = new Pen(Brighten(CardColor, 0.10)))
                {
                    g.DrawPath(border, path);
                }
            }

            using (var titleBrush = new SolidBrush(MutedColor))
            using (var titleFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            {
                g.DrawString(Title.ToUpperInvariant(), titleFont, titleBrush, 14, 10);
            }

            int labelSpace = _labels.Length > 0 ? 16 : 0;
            var plot = new Rectangle(14, 32, Width - 28 - 40, Height - 44 - labelSpace);

            if (_values.Length == 0)
            {
                using (var emptyBrush = new SolidBrush(MutedColor))
                using (var emptyFont = new Font("Segoe UI", 9f, FontStyle.Italic))
                {
                    g.DrawString(Utils.Localization.T("No data yet."), emptyFont, emptyBrush, plot.Left + 4, plot.Top + plot.Height / 2 - 8);
                }
                return;
            }

            long max = 1;
            foreach (long v in _values)
            {
                if (v > max) max = v;
            }

            // Max scale label on the right.
            using (var scaleBrush = new SolidBrush(MutedColor))
            using (var scaleFont = new Font("Segoe UI", 7.5f, FontStyle.Regular))
            {
                g.DrawString(max.ToString("N0"), scaleFont, scaleBrush, plot.Right + 4, plot.Top - 4);
            }

            int n = _values.Length;
            float slot = plot.Width / (float)n;
            float barW = Math.Max(2f, slot * 0.7f);
            float barRadius = Math.Min(4f, barW / 2f);

            using (var labelFont = new Font("Segoe UI", 7.5f, FontStyle.Regular))
            using (var labelBrush = new SolidBrush(MutedColor))
            using (var trackBrush = new SolidBrush(Color.FromArgb(28, MutedColor)))
            {
                for (int i = 0; i < n; i++)
                {
                    float h = (float)(_values[i] / (double)max) * plot.Height;
                    if (h < 2 && _values[i] > 0) h = 2;
                    float x = plot.Left + i * slot + (slot - barW) / 2f;

                    // Faint full-height track behind each bar.
                    using (var tp = RoundedTop(new RectangleF(x, plot.Top, barW, plot.Height), barRadius))
                    {
                        g.FillPath(trackBrush, tp);
                    }

                    if (h > 0)
                    {
                        bool hot = i == _hoverIndex;
                        Color topCol = Brighten(BarColor, hot ? 0.55 : 0.30);
                        Color botCol = hot ? Brighten(BarColor, 0.12) : BarColor;
                        var barRect = new RectangleF(x, plot.Bottom - h, barW, h);
                        using (var bp = RoundedTop(barRect, barRadius))
                        using (var grad = new LinearGradientBrush(
                                   new RectangleF(x, plot.Bottom - h, barW, h + 1f),
                                   topCol, botCol, LinearGradientMode.Vertical))
                        {
                            g.FillPath(grad, bp);
                        }
                    }

                    if (i < _labels.Length && !string.IsNullOrEmpty(_labels[i]))
                    {
                        SizeF ls = g.MeasureString(_labels[i], labelFont);
                        float lx = plot.Left + i * slot + (slot - ls.Width) / 2f;
                        g.DrawString(_labels[i], labelFont, labelBrush, lx, plot.Bottom + 2);
                    }
                }
            }

            // Baseline.
            using (var pen = new Pen(Color.FromArgb(120, MutedColor)))
            {
                g.DrawLine(pen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
            }
        }

        private static Color Brighten(Color c, double amount)
        {
            return Color.FromArgb(
                Math.Min(255, (int)(c.R + (255 - c.R) * amount)),
                Math.Min(255, (int)(c.G + (255 - c.G) * amount)),
                Math.Min(255, (int)(c.B + (255 - c.B) * amount)));
        }

        private static GraphicsPath RoundedTop(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f);
            if (radius <= 0.5f)
            {
                path.AddRectangle(r);
                return path;
            }
            float d = radius * 2f;
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddLine(r.Right, r.Top + radius, r.Right, r.Bottom);
            path.AddLine(r.Right, r.Bottom, r.Left, r.Bottom);
            path.CloseFigure();
            return path;
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
