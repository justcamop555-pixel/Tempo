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

        public PointsOverlayForm(Theme theme, IEnumerable<ClickPoint> points)
        {
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
                string msg = $"{CountEnabled()} active point(s) — click anywhere or press a key to dismiss";
                SizeF sz = g.MeasureString(msg, font);
                float bx = (Width - sz.Width) / 2f;
                g.FillRectangle(bannerBrush, bx - 16, 24, sz.Width + 32, sz.Height + 16);
                g.DrawString(msg, font, textBrush, bx, 32);
            }

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
