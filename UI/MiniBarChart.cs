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
        private Rectangle _plot;   // where the bars went on the last paint, for the hover tip

        // Every size below is a 100% number scaled by Px, and the title row and the scale column are
        // as big as their text. They were plain pixels — title at y 10, bars from y 32, a 40 px column
        // for the top value — so on a 150% laptop the title ran into the bars ("LAST 7 DAYS · … PEAK"
        // over the bars) and a big value lost its end at the card's edge ("3,316,(" ).
        private float DpiScale => DeviceDpi / 96f;
        private int Px(float size96) => (int)Math.Round(size96 * DpiScale, MidpointRounding.AwayFromZero);

        public string Title { get; set; } = "";

        /// <summary>What a bar's hover tip says about its value, translated with F: "{0} clicks", "{0} presses".</summary>
        public string ValueFormat { get; set; } = "{0} clicks";

        /// <summary>
        /// A dashed line across the bars at this value (an average, say), labelled with
        /// <see cref="ReferenceLabel"/>. NaN, the default, draws none.
        /// </summary>
        public double ReferenceValue { get; set; } = double.NaN;

        /// <summary>The words beside the dashed line, already translated.</summary>
        public string ReferenceLabel { get; set; } = "";

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
            // The bar's value tip in the theme, like every other tip in Tempo.
            TipStyle.Apply(_tip);
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
            Rectangle plot = _plot;
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
                    // Interpolated straight into the tooltip, so "clicks" was English in
                    // every language — on the one piece of text that exists to explain
                    // what a bar means.
                    _tip.Show(lab + Utils.Localization.F(ValueFormat, _values[idx].ToString("N0")),
                              this, e.X + 12, e.Y + 8, 1200);
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
            using (var path = Rounded(card, Px(10)))
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

            int titleH;
            using (var titleBrush = new SolidBrush(MutedColor))
            using (var titleFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            {
                g.DrawString(Title.ToUpperInvariant(), titleFont, titleBrush, Px(14), Px(10));
                titleH = (int)Math.Ceiling(titleFont.GetHeight(g));
            }

            long max = 1;
            foreach (long v in _values)
            {
                if (v > max) max = v;
            }
            bool hasReference = _values.Length > 0 && !double.IsNaN(ReferenceValue) && ReferenceValue > 0;
            if (hasReference && ReferenceValue > max) { max = (long)Math.Ceiling(ReferenceValue); }

            // 8.5pt, not 7.5. The chart's scale and axis labels were the smallest text in
            // the app — about ten pixels tall at 100% scaling — and they carry real
            // numbers, not decoration. 8.5 is the size the rest of the interface already
            // uses for secondary text, so this reads as consistent rather than enlarged.
            using (var scaleFont = new Font("Segoe UI", 8.5f, FontStyle.Regular))
            {
                // At 100% with a short top value: bars from (14, 32), 40 px for the value, 16 for the labels.
                string maxText = max.ToString("N0");
                int scaleCol = _values.Length == 0 ? Px(40)
                    : Math.Max(Px(40), (int)Math.Ceiling(g.MeasureString(maxText, scaleFont).Width) + Px(4));
                int labelSpace = _labels.Length > 0 ? (int)Math.Ceiling(scaleFont.GetHeight(g)) : 0;
                int plotTop = Px(10) + titleH + Px(6);
                _plot = new Rectangle(Px(14), plotTop, Width - Px(28) - scaleCol, Height - plotTop - Px(12) - labelSpace);

                if (_values.Length == 0)
                {
                    using (var emptyBrush = new SolidBrush(MutedColor))
                    using (var emptyFont = new Font("Segoe UI", 9f, FontStyle.Italic))
                    {
                        g.DrawString(Utils.Localization.T("No data yet."), emptyFont, emptyBrush,
                                     _plot.Left + Px(4), _plot.Top + _plot.Height / 2 - Px(8));
                    }
                    return;
                }

                // Max scale label on the right.
                using (var scaleBrush = new SolidBrush(MutedColor))
                {
                    g.DrawString(maxText, scaleFont, scaleBrush, _plot.Right + Px(4), _plot.Top - Px(4));
                }
            }
            Rectangle plot = _plot;

            int n = _values.Length;
            float slot = plot.Width / (float)n;
            float barW = Math.Max(2f, slot * 0.7f);
            float barRadius = Math.Min(4f * DpiScale, barW / 2f);

            using (var labelFont = new Font("Segoe UI", 8.5f, FontStyle.Regular))
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
                        g.DrawString(_labels[i], labelFont, labelBrush, lx, plot.Bottom + Px(2));
                    }
                }
            }

            // Baseline.
            using (var pen = new Pen(Color.FromArgb(120, MutedColor)))
            {
                g.DrawLine(pen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
            }

            // The reference (an average): a dashed line over the bars, its label just above it at the
            // right — over the bars, so it reads whatever is under it.
            if (hasReference)
            {
                float y = plot.Bottom - (float)(ReferenceValue / max) * plot.Height;
                using (var pen = new Pen(TextColor, 1.2f * DpiScale) { DashStyle = DashStyle.Dash })
                {
                    g.DrawLine(pen, plot.Left, y, plot.Right, y);
                }
                if (!string.IsNullOrEmpty(ReferenceLabel))
                {
                    using (var refFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                    using (var refBrush = new SolidBrush(TextColor))
                    using (var back = new SolidBrush(Color.FromArgb(200, CardColor)))
                    {
                        SizeF rs = g.MeasureString(ReferenceLabel, refFont);
                        float rx = plot.Right - rs.Width;
                        float ry = Math.Max(plot.Top - Px(2), y - rs.Height);
                        g.FillRectangle(back, rx, ry, rs.Width, rs.Height - 1);
                        g.DrawString(ReferenceLabel, refFont, refBrush, rx, ry);
                    }
                }
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
