using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A transparent, click-through, top-most overlay spanning all monitors that
    /// draws a colourful rainbow trail following the mouse cursor — purely for fun.
    /// It never captures input (WS_EX_TRANSPARENT), so it can't interfere with
    /// clicking or anything else, and it only repaints the small area around the
    /// trail to stay light on the CPU.
    /// </summary>
    public sealed class CursorTrailForm : Form
    {
        private const int MaxPoints = 14;
        private const int DotMaxSize = 18;

        private readonly System.Windows.Forms.Timer _timer;
        private readonly LinkedList<Point> _points = new LinkedList<Point>();
        private Rectangle _lastDirty = Rectangle.Empty;
        private double _hue;

        public CursorTrailForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Magenta;       // colour key = fully transparent
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
            Bounds = SystemInformation.VirtualScreen;

            _timer = new System.Windows.Forms.Timer { Interval = 24 };
            _timer.Tick += (s, e) => OnTick();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // Tool window (no alt-tab) + no-activate (never steals focus).
                // Transparency + layering come from TransparencyKey; click-through is
                // handled in WndProc via HTTRANSPARENT so the window still paints.
                cp.ExStyle |= 0x00000080 | 0x08000000;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTTRANSPARENT = -1;
            // Let every click fall through to whatever is underneath, so the trail
            // never interferes with clicking, the autoclicker, or recording.
            if (m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }
            base.WndProc(ref m);
        }

        public void Begin()
        {
            try
            {
                Bounds = SystemInformation.VirtualScreen;
                if (!Visible) Show();
                _points.Clear();
                _timer.Start();
            }
            catch
            {
                // The trail is a nicety; never let it throw into the app.
            }
        }

        public void End()
        {
            try
            {
                _timer.Stop();
                _points.Clear();
                if (Visible) Hide();
            }
            catch
            {
            }
        }

        private void OnTick()
        {
            Point p = Cursor.Position;
            _points.AddLast(p);
            while (_points.Count > MaxPoints)
            {
                _points.RemoveFirst();
            }

            Rectangle current = TrailBounds();
            Rectangle dirty = _lastDirty.IsEmpty ? current : Rectangle.Union(_lastDirty, current);
            _lastDirty = current;
            if (!dirty.IsEmpty)
            {
                Invalidate(dirty);
            }
        }

        private Rectangle TrailBounds()
        {
            if (_points.Count == 0)
            {
                return Rectangle.Empty;
            }
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (Point pt in _points)
            {
                int x = pt.X - Left, y = pt.Y - Top;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
            var r = Rectangle.FromLTRB(minX, minY, maxX, maxY);
            r.Inflate(DotMaxSize, DotMaxSize);
            return r;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(TransparencyKey);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_points.Count < 1)
            {
                return;
            }

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int n = _points.Count;
            int i = 0;
            foreach (Point pt in _points)
            {
                float t = n <= 1 ? 1f : (float)i / (n - 1); // 0 = tail, 1 = head
                float size = 3f + (DotMaxSize - 3f) * t;
                Color c = Hsv((_hue + t * 140.0) % 360.0, 0.95, 1.0);
                float x = pt.X - Left - size / 2f;
                float y = pt.Y - Top - size / 2f;
                using (var b = new SolidBrush(c))
                {
                    g.FillEllipse(b, x, y, size, size);
                }
                i++;
            }

            _hue = (_hue + 7.0) % 360.0;
        }

        private static Color Hsv(double h, double s, double v)
        {
            h %= 360.0;
            if (h < 0) h += 360.0;
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = v - c;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            int ri = (int)Math.Round((r + m) * 255);
            int gi = (int)Math.Round((g + m) * 255);
            int bi = (int)Math.Round((b + m) * 255);
            // Avoid producing the transparency key colour (pure magenta).
            if (ri == 255 && gi == 0 && bi == 255) { gi = 1; }
            return Color.FromArgb(ri, gi, bi);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _timer.Stop(); _timer.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}
