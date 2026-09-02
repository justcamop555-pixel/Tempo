using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A rounded "stat card": a small surface panel with a muted caption at the
    /// top and a large bold value below it. Used to build the Statistics
    /// dashboard. Owner-drawn so it looks consistent across themes and DPI.
    /// </summary>
    public sealed class StatCard : Control
    {
        private string _caption = "";
        private string _value = "0";
        private string _sub = "";

        public Color CardColor { get; set; } = Color.FromArgb(24, 27, 39);
        public Color CaptionColor { get; set; } = Color.FromArgb(132, 142, 168);
        public Color ValueColor { get; set; } = Color.FromArgb(232, 236, 246);
        public Color AccentBar { get; set; } = Color.FromArgb(124, 92, 255);
        public bool ShowAccent { get; set; } = true;

        public StatCard()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            Size = new Size(168, 78);
        }

        public string Caption
        {
            get => _caption;
            set { _caption = value ?? ""; Invalidate(); }
        }

        public string Value
        {
            get => _value;
            set { _value = value ?? ""; Invalidate(); }
        }

        /// <summary>Optional small text under the value (e.g. units or context).</summary>
        public string Sub
        {
            get => _sub;
            set { _sub = value ?? ""; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (Width < 8 || Height < 10)
            {
                return;
            }

            // Reserve a few pixels at the bottom/right for a soft drop shadow.
            var card = new Rectangle(1, 1, Width - 4, Height - 6);
            const int radius = 12;

            // 1) Soft drop shadow for depth.
            using (var shadowPath = Rounded(new Rectangle(card.Left + 1, card.Top + 4, card.Width, card.Height), radius))
            using (var sh = new SolidBrush(Color.FromArgb(38, 0, 0, 0)))
            {
                g.FillPath(sh, shadowPath);
            }

            // 2) Card surface with a gentle top-to-bottom gradient.
            using (var path = Rounded(card, radius))
            {
                using (var fill = new LinearGradientBrush(
                           new Rectangle(card.Left, card.Top, card.Width, card.Height),
                           Lighten(CardColor, 0.06), CardColor, LinearGradientMode.Vertical))
                {
                    g.FillPath(fill, path);
                }

                // Hairline edge highlight (subtle on dark themes, invisible on light).
                using (var border = new Pen(Lighten(CardColor, 0.10)))
                {
                    g.DrawPath(border, path);
                }
            }

            // 3) Rounded accent pill, with a little horizontal gradient.
            if (ShowAccent)
            {
                var accentRect = new Rectangle(card.Left + 13, card.Top + 14, 26, 4);
                using (var ap = Rounded(accentRect, 2))
                using (var ag = new LinearGradientBrush(accentRect, AccentBar, Lighten(AccentBar, 0.22), LinearGradientMode.Horizontal))
                {
                    g.FillPath(ag, ap);
                }
            }

            using (var capBrush = new SolidBrush(CaptionColor))
            using (var capFont = new Font("Segoe UI", 8.5f, FontStyle.Regular))
            {
                string cap = _caption.ToUpperInvariant();
                g.DrawString(cap, capFont, capBrush, 14, 24);

                // The sublabel goes on the CAPTION row, right-aligned — not beside the
                // value, where it used to live. The value is 18pt bold and a six-figure
                // click count is most of a 172px card, so "+86,377 vs previous 7 days"
                // had about 70px to sit in and was cut to "+86,377 vs". The caption row
                // is short ("THIS WEEK") and leaves roughly a hundred, and the trend
                // reads naturally as an annotation on the label rather than on the number.
                if (!string.IsNullOrEmpty(_sub))
                {
                    float capW = g.MeasureString(cap, capFont).Width;
                    float subLeft = 14 + capW + 6;
                    float subWidth = Width - subLeft - 12;
                    if (subWidth > 24)
                    {
                        using (var subFont = new Font("Segoe UI", 8f, FontStyle.Regular))
                        using (var subBrush = new SolidBrush(CaptionColor))
                        {
                            var fmt = new StringFormat
                            {
                                Alignment = StringAlignment.Far,
                                FormatFlags = StringFormatFlags.NoWrap,
                                Trimming = StringTrimming.EllipsisCharacter
                            };
                            g.DrawString(_sub, subFont, subBrush,
                                new RectangleF(subLeft, 23f, subWidth, 14f), fmt);
                        }
                    }
                }
            }

            using (var valBrush = new SolidBrush(ValueColor))
            using (var valFont = new Font("Segoe UI", 18f, FontStyle.Bold))
            {
                g.DrawString(_value, valFont, valBrush, 12, 40);
            }
        }

        private static Color Lighten(Color c, double f)
        {
            int r = (int)Math.Round(c.R + (255 - c.R) * f);
            int g = (int)Math.Round(c.G + (255 - c.G) * f);
            int b = (int)Math.Round(c.B + (255 - c.B) * f);
            return Color.FromArgb(c.A,
                Math.Min(255, Math.Max(0, r)),
                Math.Min(255, Math.Max(0, g)),
                Math.Min(255, Math.Max(0, b)));
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
