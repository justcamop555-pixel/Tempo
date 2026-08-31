using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A thin, full-width band that plays an optional animated GIF as a backdrop
    /// behind a readability scrim. Used for the second ("footer") background image.
    /// Owner-drawn and double-buffered so the animation stays smooth.
    /// </summary>
    public sealed class GifBackdropPanel : Panel
    {
        private Image _sharedBg;   // shared window backdrop — NOT owned/disposed here
        private int _bgDim = 55;

        /// <summary>True when a background image is currently set.</summary>
        public bool HasGif => _sharedBg != null;
        private Theme _theme = Theme.ForKind(Models.ThemeKind.Dark);

        /// <summary>Draw a thin accent line along the top edge (mirrors the header).</summary>
        public bool TopAccent { get; set; } = true;

        public GifBackdropPanel()
        {
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
        }

        public void ApplyTheme(Theme theme)
        {
            if (theme != null)
            {
                _theme = theme;
                BackColor = theme.Surface;
                Invalidate();
            }
        }

        /// <summary>
        /// Assigns the SHARED window backdrop image (owned/animated by the form) and its
        /// dim percentage. The footer band paints the aligned BOTTOM slice so it lines
        /// up seamlessly with the page above it. Pass null to clear.
        /// </summary>
        public void SetSharedBackdrop(Image img, int dimPercent)
        {
            _sharedBg = img;
            _bgDim = dimPercent;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (!WindowBackdrop.Paint(g, this, _sharedBg, _bgDim, _theme.Surface))
            {
                using (var bg = new SolidBrush(_theme.Surface))
                {
                    g.FillRectangle(bg, new Rectangle(0, 0, Width, Height));
                }
            }

            if (TopAccent)
            {
                var line = new Rectangle(0, 0, Width, 2);
                using (var lb = new LinearGradientBrush(line,
                           _theme.Accent, _theme.AccentHover, LinearGradientMode.Horizontal))
                {
                    g.FillRectangle(lb, line);
                }
            }
        }
    }
}
