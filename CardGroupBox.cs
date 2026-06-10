using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A <see cref="GroupBox"/> that paints itself as a modern rounded "card": a
    /// filled surface with a soft rounded border and a clean title preceded by a
    /// small accent tab.
    ///
    /// Crucially it keeps exactly the same client area and child-coordinate origin as
    /// a normal GroupBox — only <see cref="OnPaint"/> and the control region change —
    /// so every existing child control stays exactly where it was. This lets the whole
    /// app pick up the new look without re-laying-out a single tab.
    /// </summary>
    public sealed class CardGroupBox : GroupBox
    {
        private Color _surface = Color.FromArgb(37, 37, 38);
        private Color _border = Color.FromArgb(64, 64, 64);
        private Color _title = Color.Gainsboro;
        private Color _accent = Color.DodgerBlue;
        private const int Radius = 10;

        public CardGroupBox()
        {
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = _surface;
        }

        public void ApplyTheme(Theme theme)
        {
            if (theme == null)
            {
                return;
            }
            _surface = theme.Surface;
            _border = theme.Border;
            _title = theme.Text;
            _accent = theme.Accent;
            BackColor = theme.Surface; // so transparent child labels blend with the card
            UpdateRegion();
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateRegion();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateRegion();
        }

        // Clip to a rounded shape so the corners reveal the page behind the card.
        private void UpdateRegion()
        {
            if (Width <= 1 || Height <= 1)
            {
                return;
            }
            using (var path = Rounded(new Rectangle(0, 0, Width, Height), Radius))
            {
                Region = new Region(path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);

            using (var path = Rounded(rect, Radius))
            using (var fill = new SolidBrush(_surface))
            {
                g.FillPath(fill, path);
            }

            using (var path = Rounded(rect, Radius))
            using (var pen = new Pen(_border, 1))
            {
                g.DrawPath(pen, path);
            }

            // Title (undo the "&&" mnemonic escaping that GroupBox text uses).
            string title = (Text ?? string.Empty).Replace("&&", "&");
            if (title.Length > 0)
            {
                const int ty = 4;
                using (var accentBar = new SolidBrush(_accent))
                {
                    g.FillRectangle(accentBar, 12, ty + 2, 3, 12);
                }
                using (var tb = new SolidBrush(_title))
                {
                    g.DrawString(title, Font, tb, 21, ty);
                }
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;

            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
