using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// Roblox's verified badge, and its absence, drawn rather than written.
    ///
    /// It is a VECTOR — a filled circle with a tick cut through it — for the same reason
    /// <see cref="ColorEmoji"/> exists: GDI draws an emoji or a dingbat as a flat silhouette in the
    /// label's own colour, so "✓" in a blue box is not a thing this app can render as text. Drawing the
    /// two shapes costs nothing, stays crisp at any DPI, and takes the theme's colours.
    ///
    /// Three states, and the third matters. <see cref="Verified"/> = true is the blue check; false is a
    /// muted hollow ring with a dash, so "this creator is NOT verified" is stated rather than merely
    /// absent; null draws nothing at all, for when Roblox did not tell us either way — an unanswered
    /// question must not be shown as an answer.
    /// </summary>
    internal sealed class VerifiedBadge : Control
    {
        private bool? _verified;
        private Theme _theme;

        public VerifiedBadge()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            Size = new Size(14, 14);
            TabStop = false;
            BackColor = Color.Transparent;
        }

        /// <summary>true = verified, false = explicitly not verified, null = unknown (draws nothing).</summary>
        public bool? Verified
        {
            get { return _verified; }
            set { if (_verified != value) { _verified = value; Invalidate(); } }
        }

        public void ApplyTheme(Theme theme)
        {
            _theme = theme;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_verified == null) { return; }

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Inset by one pixel so the antialiased edge has room and never clips against the bounds.
            var box = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));

            if (_verified == true)
            {
                // Roblox's badge is a solid blue disc with a white tick. Blue is taken from the theme's
                // accent only when it is actually blue-ish; otherwise a fixed Roblox blue, so the badge
                // still reads as "verified" on a theme whose accent is orange or green.
                Color blue = VerifiedBlue();
                using (var fill = new SolidBrush(blue))
                {
                    g.FillEllipse(fill, box);
                }
                using (var tick = new Pen(Color.White, Math.Max(1.4f, Width / 9f))
                {
                    StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round
                })
                {
                    g.DrawLines(tick, TickPoints(box));
                }
            }
            else
            {
                // Not verified: a hollow muted ring with a dash. Deliberately quiet — it is information,
                // not a warning; an unverified creator is ordinary, not suspect.
                Color muted = _theme != null ? _theme.TextMuted : Color.Gray;
                using (var ring = new Pen(muted, Math.Max(1f, Width / 12f)))
                {
                    g.DrawEllipse(ring, box);
                }
                using (var dash = new Pen(muted, Math.Max(1.2f, Width / 10f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float y = box.Top + box.Height / 2f;
                    g.DrawLine(dash, box.Left + box.Width * 0.3f, y, box.Right - box.Width * 0.3f, y);
                }
            }
        }

        /// <summary>The three points of the tick, in proportion to the badge so it scales with DPI.</summary>
        private static PointF[] TickPoints(Rectangle box)
        {
            return new[]
            {
                new PointF(box.Left + box.Width * 0.26f, box.Top + box.Height * 0.52f),
                new PointF(box.Left + box.Width * 0.44f, box.Top + box.Height * 0.70f),
                new PointF(box.Left + box.Width * 0.75f, box.Top + box.Height * 0.32f)
            };
        }

        /// <summary>
        /// The theme's accent when it reads as blue, else Roblox's own badge blue. A badge that takes an
        /// orange accent stops looking like the verified mark people recognise from the site.
        /// </summary>
        private Color VerifiedBlue()
        {
            Color roblox = Color.FromArgb(0, 162, 255);
            Color accent = _theme != null ? _theme.Accent : roblox;
            bool blueish = accent.B > accent.R + 24 && accent.B > 110;
            return blueish ? accent : roblox;
        }
    }
}
