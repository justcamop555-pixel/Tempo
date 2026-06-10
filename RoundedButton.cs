using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A flat <see cref="Button"/> that paints itself with rounded corners so it
    /// matches the rounded "card" panels. It renders entirely from the standard
    /// button properties the theme already sets — <see cref="Control.BackColor"/>,
    /// <see cref="Control.ForeColor"/>, and <see cref="ButtonBase.FlatAppearance"/>'s
    /// border colour/size — so theming and the per-button accent colours (Start =
    /// green, Stop = red, primary = accent) keep working untouched. Hover and pressed
    /// states are derived by lightening/darkening the current back colour.
    /// </summary>
    public class RoundedButton : Button
    {
        public int CornerRadius { get; set; } = 6;

        /// <summary>Optional glyph drawn to the left of the text (sidebar nav).</summary>
        public NavIconKind IconKind { get; set; } = NavIconKind.None;

        private bool _hover;
        private bool _down;

        public RoundedButton()
        {
            FlatStyle = FlatStyle.Flat;
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateRegion(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateRegion(); }

        // Clip to the rounded shape so the corners reveal whatever is behind the button.
        private void UpdateRegion()
        {
            if (Width <= 1 || Height <= 1)
            {
                return;
            }
            using (var path = Rounded(new Rectangle(0, 0, Width, Height), CornerRadius))
            {
                Region = new Region(path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);

            Color back = BackColor;
            if (!Enabled)
            {
                back = Mix(BackColor, Color.Gray, 0.45);
            }
            else if (_down)
            {
                back = Shade(BackColor, -0.12);
            }
            else if (_hover)
            {
                back = Shade(BackColor, 0.12);
            }

            using (var path = Rounded(rect, CornerRadius))
            {
                using (var fill = new SolidBrush(back))
                {
                    g.FillPath(fill, path);
                }
                if (FlatAppearance.BorderSize > 0)
                {
                    using (var pen = new Pen(FlatAppearance.BorderColor, 1))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            Color fore = Enabled ? ForeColor : Mix(ForeColor, Color.Gray, 0.5);

            // Optional left-hand glyph (used by the navigation sidebar).
            int textLeftPad = Math.Max(0, Padding.Left);
            if (IconKind != NavIconKind.None)
            {
                float iconSize = Math.Min(Height - 16, 20);
                if (iconSize < 10) iconSize = 10;
                var iconBox = new RectangleF(14f, (Height - iconSize) / 2f, iconSize, iconSize);
                NavIcons.Draw(g, IconKind, iconBox, fore);
                textLeftPad = (int)(iconBox.Right + 8f);
            }

            TextFormatFlags flags = TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            var textRect = rect;
            switch (TextAlign)
            {
                case ContentAlignment.TopLeft:
                case ContentAlignment.MiddleLeft:
                case ContentAlignment.BottomLeft:
                    flags |= TextFormatFlags.Left;
                    textRect = new Rectangle(rect.X + textLeftPad, rect.Y,
                        rect.Width - textLeftPad, rect.Height);
                    break;
                case ContentAlignment.TopRight:
                case ContentAlignment.MiddleRight:
                case ContentAlignment.BottomRight:
                    flags |= TextFormatFlags.Right;
                    textRect = new Rectangle(rect.X, rect.Y,
                        rect.Width - Math.Max(0, Padding.Right), rect.Height);
                    break;
                default:
                    flags |= TextFormatFlags.HorizontalCenter;
                    break;
            }

            TextRenderer.DrawText(g, Text, Font, textRect, fore, flags);
        }

        private static Color Shade(Color c, double f)
        {
            if (f >= 0)
            {
                return Color.FromArgb(c.A,
                    (int)(c.R + (255 - c.R) * f),
                    (int)(c.G + (255 - c.G) * f),
                    (int)(c.B + (255 - c.B) * f));
            }
            f = -f;
            return Color.FromArgb(c.A,
                (int)(c.R * (1 - f)),
                (int)(c.G * (1 - f)),
                (int)(c.B * (1 - f)));
        }

        private static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;

            var path = new GraphicsPath();
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
