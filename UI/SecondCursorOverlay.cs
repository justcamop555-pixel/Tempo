using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>How the second cursor is drawn.</summary>
    public enum SecondCursorShape
    {
        Arrow,
        Ring,
        Crosshair,
        Dot
    }

    /// <summary>
    /// A visible "second mouse" pointer that Tempo draws and positions itself.
    /// Windows only has ONE real system cursor, so this is a click-through, always-on-
    /// top layered window showing a marker at a point Tempo controls — the user aims
    /// and parks it, and Tempo clicks whatever is under it. It never steals input
    /// (WS_EX_TRANSPARENT + WS_EX_NOACTIVATE), so it floats over both monitors without
    /// blocking anything beneath it.
    /// </summary>
    public sealed class SecondCursorOverlay : Form
    {
        private const int WsExLayered = 0x00080000;
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExTopMost = 0x00000008;

        private const int Box = 64;               // window size; marker drawn inside
        private SecondCursorShape _shape = SecondCursorShape.Arrow;
        private Color _color = Color.FromArgb(255, 64, 64);
        private int _scale = 100;                 // 50..250 %
        private bool _active;                     // grabbed (brighter) vs parked

        public SecondCursorOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Size = new Size(Box, Box);
            BackColor = Color.Black;
            // Per-pixel alpha layered window: black is treated as fully transparent
            // via the transparency key so only the marker shows.
            TransparencyKey = Color.Black;
            DoubleBuffered = true;
            Enabled = false;   // never take focus/mouse (click-through also via WS_EX_TRANSPARENT)
        }

        /// <summary>Re-asserts topmost so the marker stays above other windows.</summary>
        public void KeepOnTop()
        {
            try
            {
                if (IsHandleCreated)
                {
                    SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                }
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate | WsExTopMost;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            // Report HTTRANSPARENT for hit-testing so the marker is invisible to
            // WindowFromPoint. This is what lets the spam clicks find the GAME under
            // the second cursor instead of hitting our own overlay window (the bug
            // where "Spam-click here" did nothing).
            const int WM_NCHITTEST = 0x0084;
            const int HTTRANSPARENT = -1;
            if (m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>The marker's hotspot (the exact pixel it "points at") in screen coords.</summary>
        public void SetHotspot(int screenX, int screenY)
        {
            // Arrow points from its top-left; others are centred. Offset the window so
            // the hotspot lands on the target pixel.
            int half = Box / 2;
            int hx = _shape == SecondCursorShape.Arrow ? 6 : half;
            int hy = _shape == SecondCursorShape.Arrow ? 4 : half;
            Location = new Point(screenX - hx, screenY - hy);
        }

        public void SetAppearance(SecondCursorShape shape, Color color, int scalePercent)
        {
            _shape = shape;
            _color = color;
            _scale = Math.Max(50, Math.Min(250, scalePercent));
            Invalidate();
        }

        /// <summary>Grabbed = a brighter halo so it's obvious you're moving it.</summary>
        public void SetActiveLook(bool active)
        {
            if (_active != active) { _active = active; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = _scale / 100f;
            int cx = Width / 2, cy = Height / 2;

            using (var fill = new SolidBrush(_color))
            using (var outline = new Pen(Color.White, 1.6f))
            using (var pen = new Pen(_color, Math.Max(2f, 3f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                // A soft halo when grabbed, so it reads as "live".
                if (_active)
                {
                    using (var gp = new GraphicsPath())
                    {
                        int r = (int)(20 * s);
                        gp.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                        using (var glow = new PathGradientBrush(gp)
                        {
                            CenterColor = Color.FromArgb(110, _color),
                            SurroundColors = new[] { Color.FromArgb(0, _color) }
                        })
                        {
                            g.FillPath(glow, gp);
                        }
                    }
                }

                switch (_shape)
                {
                    case SecondCursorShape.Arrow:
                    {
                        // The real Windows 11 pointer: a white arrow with a thin black
                        // outline and a soft drop shadow, tip at (6,4). Always drawn white
                        // (not _color) so it reads exactly like the OS cursor — the whole
                        // point being that it looks like a second real mouse pointer.
                        float ox = 6, oy = 4;
                        PointF[] pts =
                        {
                            new PointF(ox, oy),                          // tip
                            new PointF(ox, oy + 18f * s),                // left edge
                            new PointF(ox + 4.4f * s, oy + 13.8f * s),   // inner notch (left of tail)
                            new PointF(ox + 8f * s, oy + 20.6f * s),     // tail bottom-left
                            new PointF(ox + 11f * s, oy + 19.2f * s),    // tail bottom-right
                            new PointF(ox + 7.4f * s, oy + 12.4f * s),   // inner notch (right of tail)
                            new PointF(ox + 12.6f * s, oy + 12.4f * s),  // right wing
                        };
                        // Soft shadow, offset down-right like the real cursor.
                        using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                        {
                            g.TranslateTransform(1.3f, 1.7f);
                            g.FillPolygon(shadow, pts);
                            g.ResetTransform();
                        }
                        using (var white = new SolidBrush(Color.White))
                        using (var black = new Pen(Color.Black, 1.5f) { LineJoin = LineJoin.Round })
                        {
                            g.FillPolygon(white, pts);
                            g.DrawPolygon(black, pts);
                        }
                        break;
                    }
                    case SecondCursorShape.Ring:
                    {
                        float r = 12 * s;
                        g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                        g.FillEllipse(fill, cx - 2.2f, cy - 2.2f, 4.4f, 4.4f);
                        break;
                    }
                    case SecondCursorShape.Crosshair:
                    {
                        float r = 13 * s, gap = 4 * s;
                        g.DrawLine(pen, cx, cy - r, cx, cy - gap);
                        g.DrawLine(pen, cx, cy + gap, cx, cy + r);
                        g.DrawLine(pen, cx - r, cy, cx - gap, cy);
                        g.DrawLine(pen, cx + gap, cy, cx + r, cy);
                        g.FillEllipse(fill, cx - 2f, cy - 2f, 4f, 4f);
                        break;
                    }
                    case SecondCursorShape.Dot:
                    {
                        float r = 8 * s;
                        g.FillEllipse(fill, cx - r, cy - r, r * 2, r * 2);
                        g.DrawEllipse(outline, cx - r, cy - r, r * 2, r * 2);
                        break;
                    }
                }
            }
        }
    }
}
