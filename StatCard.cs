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

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Rounded(rect, 10))
            using (var fill = new SolidBrush(CardColor))
            {
                g.FillPath(fill, path);
            }

            if (ShowAccent)
            {
                // A short accent bar in the top-left corner.
                using (var accent = new SolidBrush(AccentBar))
                {
                    g.FillRectangle(accent, 14, 14, 26, 3);
                }
            }

            using (var capBrush = new SolidBrush(CaptionColor))
            using (var capFont = new Font("Segoe UI", 8.5f, FontStyle.Regular))
            {
                g.DrawString(_caption.ToUpperInvariant(), capFont, capBrush, 14, 24);
            }

            using (var valBrush = new SolidBrush(ValueColor))
            using (var valFont = new Font("Segoe UI", 18f, FontStyle.Bold))
            {
                g.DrawString(_value, valFont, valBrush, 12, 40);
            }

            if (!string.IsNullOrEmpty(_sub))
            {
                using (var subBrush = new SolidBrush(CaptionColor))
                using (var subFont = new Font("Segoe UI", 8f, FontStyle.Regular))
                {
                    SizeF valSize;
                    using (var valFont = new Font("Segoe UI", 18f, FontStyle.Bold))
                    {
                        valSize = g.MeasureString(_value, valFont);
                    }
                    g.DrawString(_sub, subFont, subBrush, 14 + valSize.Width, 54);
                }
            }
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
