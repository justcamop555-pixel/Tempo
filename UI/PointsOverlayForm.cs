using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AutoClicker.Models;

namespace AutoClicker.UI
{
    /// <summary>
    /// A translucent full-virtual-screen overlay that draws numbered markers at
    /// each multi-point target so the user can see where the clicks will land.
    /// Dismissed by any click, key press, or automatically after a few seconds.
    /// </summary>
    public sealed class PointsOverlayForm : Form
    {
        private readonly List<ClickPoint> _points;
        private readonly Theme _theme;
        private readonly Timer _autoClose;
        private readonly MultiPointOrder _order;

        public PointsOverlayForm(Theme theme, IEnumerable<ClickPoint> points)
            : this(theme, points, MultiPointOrder.Sequential)
        {
        }

        public PointsOverlayForm(Theme theme, IEnumerable<ClickPoint> points, MultiPointOrder order)
        {
            _order = order;
            AutoScaleMode = AutoScaleMode.None; // positioned in raw screen pixels
            _theme = theme ?? Theme.ForKind(ThemeKind.Dark);
            _points = new List<ClickPoint>(points ?? new List<ClickPoint>());

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;

            // Cover the whole virtual desktop (all monitors).
            Rectangle vs = SystemInformation.VirtualScreen;
            Bounds = vs;
            _virtualOrigin = vs.Location;

            BackColor = Color.Black;
            Opacity = 0.45;
            TopMost = true;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;

            _autoClose = new Timer { Interval = 4000 };
            _autoClose.Tick += (s, e) => Close();

            KeyPreview = true;
        }

        private readonly Point _virtualOrigin;

        protected override bool ShowWithoutActivation => false;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            OverlayTopmost.Register(Handle);   // stay above fullscreen games / video
            _autoClose.Start();
            Focus();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            OverlayTopmost.Unregister(Handle);
            _autoClose.Stop();
            _autoClose.Dispose();
            base.OnFormClosed(e);
        }

        protected override void OnMouseDown(MouseEventArgs e) => Close();
        protected override void OnKeyDown(KeyEventArgs e) => Close();

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Banner with instructions.
            using (var bannerBrush = new SolidBrush(Color.FromArgb(220, _theme.Surface)))
            using (var textBrush = new SolidBrush(_theme.Text))
            using (var font = new Font("Segoe UI", 11f, FontStyle.Bold))
            {
                // Translated. This is text painted straight onto a canvas, so it touched
                // no helper and stayed English in every language.
                string msg = Utils.Localization.F("{0} active point(s)", CountEnabled())
                    + "  •  " + OrderText()
                    + "  •  " + Utils.Localization.T("click anywhere or press a key to dismiss");
                SizeF sz = g.MeasureString(msg, font);

                // Centred on the PRIMARY monitor, not on the whole virtual desktop. This
                // form spans every screen, so centring on its own width put the banner
                // halfway between two monitors — on a two-monitor desktop it straddled
                // the gap and each half read as clipped at a screen edge. The markers are
                // wherever the points are; the instructions belong where you are looking.
                Rectangle primary = Screen.PrimaryScreen != null
                    ? Screen.PrimaryScreen.Bounds
                    : new Rectangle(_virtualOrigin.X, _virtualOrigin.Y, Width, Height);
                float centreX = (primary.Left - _virtualOrigin.X) + primary.Width / 2f;
                float bx = centreX - sz.Width / 2f;
                float by = (primary.Top - _virtualOrigin.Y) + 24f;

                g.FillRectangle(bannerBrush, bx - 16, by, sz.Width + 32, sz.Height + 16);
                g.DrawString(msg, font, textBrush, bx, by + 8f);
            }

            DrawRoute(g);

            int n = 0;
            for (int i = 0; i < _points.Count; i++)
            {
                ClickPoint p = _points[i];
                if (!p.Enabled) continue;
                n++;

                // Translate screen coords into this form's client space.
                int x = p.X - _virtualOrigin.X;
                int y = p.Y - _virtualOrigin.Y;

                Color ring = p.Enabled ? _theme.Accent : _theme.TextMuted;
                using (var ringPen = new Pen(ring, 3))
                using (var dotBrush = new SolidBrush(Color.FromArgb(170, ring)))
                using (var numBrush = new SolidBrush(Color.White))
                using (var numFont = new Font("Segoe UI", 12f, FontStyle.Bold))
                {
                    g.DrawEllipse(ringPen, x - 18, y - 18, 36, 36);
                    g.FillEllipse(dotBrush, x - 18, y - 18, 36, 36);

                    string label = n.ToString();
                    SizeF ls = g.MeasureString(label, numFont);
                    g.DrawString(label, numFont, numBrush, x - ls.Width / 2f, y - ls.Height / 2f);

                    // Crosshair at the exact pixel.
                    using (var cross = new Pen(Color.White, 1))
                    {
                        g.DrawLine(cross, x - 26, y, x - 20, y);
                        g.DrawLine(cross, x + 20, y, x + 26, y);
                        g.DrawLine(cross, x, y - 26, x, y - 20);
                        g.DrawLine(cross, x, y + 20, x, y + 26);
                    }
                }
            }
        }

        /// <summary>The chosen order, named, for the banner.</summary>
        private string OrderText()
        {
            switch (_order)
            {
                case MultiPointOrder.Reverse: return Utils.Localization.T("Reverse");
                case MultiPointOrder.Random: return Utils.Localization.T("Random");
                case MultiPointOrder.PingPong: return Utils.Localization.T("Ping-Pong");
                default: return Utils.Localization.T("Sequential");
            }
        }

        /// <summary>
        /// Draws the route the engine will actually take between the enabled points.
        ///
        /// The overlay numbered the points but drew nothing between them, so the one
        /// question it exists to answer — "what will this sequence do?" — still needed
        /// working out in your head, and the Reverse and Ping-Pong orders were invisible.
        ///
        /// Random deliberately draws nothing: any line would be a specific claim about an
        /// order that is chosen fresh each time, and a wrong picture is worse than none.
        /// </summary>
        private void DrawRoute(Graphics g)
        {
            if (_order == MultiPointOrder.Random) { return; }

            var pts = new List<Point>();
            foreach (ClickPoint p in _points)
            {
                if (p.Enabled)
                {
                    pts.Add(new Point(p.X - _virtualOrigin.X, p.Y - _virtualOrigin.Y));
                }
            }
            if (pts.Count < 2) { return; }

            if (_order == MultiPointOrder.Reverse) { pts.Reverse(); }

            // Sequential and Reverse loop back to the start; Ping-Pong turns round and
            // retraces its steps, so the return leg is the same line and is left alone.
            bool closeLoop = _order != MultiPointOrder.PingPong;

            using (var pen = new Pen(Color.FromArgb(150, _theme.Accent), 2f))
            {
                pen.DashStyle = DashStyle.Dash;
                pen.CustomEndCap = new AdjustableArrowCap(4f, 5f);

                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    DrawLegBetweenMarkers(g, pen, pts[i], pts[i + 1]);
                }
                if (closeLoop)
                {
                    DrawLegBetweenMarkers(g, pen, pts[pts.Count - 1], pts[0]);
                }
            }
        }

        /// <summary>
        /// One leg, trimmed at both ends so it starts and stops at the edge of the 18px
        /// marker rings instead of disappearing under them — which is also what keeps the
        /// arrowhead visible.
        /// </summary>
        private static void DrawLegBetweenMarkers(Graphics g, Pen pen, Point a, Point b)
        {
            const float R = 22f;
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len <= R * 2 + 4f) { return; }   // markers are touching; a stub would be noise

            float ux = dx / len, uy = dy / len;
            g.DrawLine(pen,
                a.X + ux * R, a.Y + uy * R,
                b.X - ux * R, b.Y - uy * R);
        }

        private int CountEnabled()
        {
            int c = 0;
            foreach (var p in _points)
            {
                if (p.Enabled) c++;
            }
            return c;
        }
    }
}
