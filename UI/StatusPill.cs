using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A small rounded badge with a coloured fill and white bold text. Used in
    /// the new header bar to show the engine state at a glance (IDLE, RUNNING,
    /// PAUSED, etc.). Owner-drawn with GDI+ so it looks the same on every theme
    /// and DPI setting.
    /// </summary>
    public sealed class StatusPill : Control
    {
        private Color _pillColor = Color.FromArgb(120, 132, 152);

        public StatusPill()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            BackColor = Color.Transparent;
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            Height = 28;
            Width = 96;
            Text = Utils.Localization.T("IDLE");
        }

        /// <summary>Background colour of the pill (e.g. accent / success / warn).</summary>
        public Color PillColor
        {
            get => _pillColor;
            set
            {
                if (_pillColor != value)
                {
                    _pillColor = value;
                    Invalidate();
                }
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int radius = Math.Min(Height, Width) / 2;
            if (radius < 4)
            {
                radius = 4;
            }

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);

            using (GraphicsPath path = RoundedRect(rect, radius))
            using (var fill = new SolidBrush(_pillColor))
            {
                g.FillPath(fill, path);
            }

            // Black or white, whichever reads on THIS pill's fill — not always white.
            //
            // The pill is filled with a theme status colour (Success for RUNNING, Warning
            // for paused, and so on) and its text was hardcoded to white. Measured over
            // the 38 palettes: white on Success is below 4.5:1 in ALL 38 themes and
            // bottoms out at 1.31:1 on Synthwave; white on Warning is worse still, 1.12:1
            // on Dracula — white on amber, effectively invisible. Danger fails in 30 of
            // 38, Accent in 36. Picking the better of black and white reaches at least
            // 4.63:1 in every theme for every one of those fills.
            //
            // Neither contrast scanner could see this: it is not a ForeColor assignment
            // with a resolvable background, it is a brush inside a custom control's paint.
            using (var brush = new SolidBrush(Theme.ReadableOn(_pillColor)))
            using (var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter
            })
            {
                g.DrawString(Text ?? string.Empty, Font, brush, rect, format);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;

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
