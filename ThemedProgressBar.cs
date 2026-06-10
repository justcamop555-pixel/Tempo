using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A small rounded progress bar drawn with the current theme's accent colour,
    /// so the session-goal indicator matches the rest of the custom UI instead of
    /// using the stock (green) Windows progress bar.
    /// </summary>
    public sealed class ThemedProgressBar : Control
    {
        private int _value;
        private int _max = 100;
        private Theme _theme = Theme.ForKind(Models.ThemeKind.Dark);

        public ThemedProgressBar()
        {
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
        }

        public int Maximum
        {
            get => _max;
            set { _max = Math.Max(1, value); Invalidate(); }
        }

        public int Value
        {
            get => _value;
            set
            {
                int v = Math.Max(0, Math.Min(_max, value));
                if (v != _value) { _value = v; Invalidate(); }
            }
        }

        public void ApplyTheme(Theme theme)
        {
            if (theme != null) { _theme = theme; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var track = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Math.Min(Height - 1, 10);

            using (var tp = Rounded(track, radius))
            {
                using (var tb = new SolidBrush(_theme.Surface2))
                {
                    g.FillPath(tb, tp);
                }
                using (var bp = new Pen(_theme.Border))
                {
                    g.DrawPath(bp, tp);
                }

                double frac = _max > 0 ? _value / (double)_max : 0;
                int fillW = (int)Math.Round((Width - 2) * frac);
                if (fillW > 2)
                {
                    var fill = new Rectangle(1, 1, fillW, Height - 3);
                    g.SetClip(tp);
                    using (var fb = new LinearGradientBrush(fill,
                               _theme.Accent, _theme.AccentHover, LinearGradientMode.Horizontal))
                    using (var fpath = Rounded(fill, Math.Min(Height - 3, radius)))
                    {
                        g.FillPath(fb, fpath);
                    }
                    g.ResetClip();
                }
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            if (radius <= 1 || r.Width <= 0 || r.Height <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
