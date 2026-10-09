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

    /// <summary>What the marker's badge says about spam-clicking.</summary>
    public enum SecondCursorSpamLook
    {
        Off,
        Clicking,
        Paused
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

        private const int MinBox = 64;            // smallest window; the marker is drawn inside
        private int _box = MinBox;                // current window size (grows with size and DPI)
        private SecondCursorShape _shape = SecondCursorShape.Arrow;
        private Color _color = Color.FromArgb(255, 64, 64);
        private int _scale = 100;                 // 50..250 %
        private bool _active;                     // grabbed (brighter) vs parked
        private SecondCursorSpamLook _spamLook;   // the badge

        // The DPI of the monitor the marker is on, so it is drawn at the size the real
        // pointer is there: Windows scales its own cursor with the display, and at 150 % or
        // 200 % the marker drawn at 96-DPI pixels was a fraction of the pointer beside it.
        private int _dpi = 96;
        private IntPtr _dpiMonitor = IntPtr.Zero;

        public SecondCursorOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Size = new Size(MinBox, MinBox);
            // No TransparencyKey. It was black, and a colour key is all-or-nothing: every pure
            // black pixel vanished — the arrow's outline and its shadow — and every soft edge
            // was blended against black. On a light background the white arrow all but
            // disappeared, and the "grabbed" glow was a dark disc. The window's pixels now
            // come from UpdateLayeredWindow with a real alpha channel (see Redraw).
            Enabled = false;   // never take focus/mouse (click-through also via WS_EX_TRANSPARENT)
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Redraw();
        }

        // What shows is only what Redraw hands to UpdateLayeredWindow; painting a window like
        // this does nothing on screen, so nothing is painted.
        protected override void OnPaintBackground(PaintEventArgs e) { }

        /// <summary>The marker as the window shows it: <see cref="Size"/> pixels, transparent around it.</summary>
        public Bitmap RenderMarker()
        {
            var bmp = new Bitmap(_box, _box, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                DrawMarker(g);
            }
            return bmp;
        }

        /// <summary>
        /// Puts the marker on screen: drawn with a real alpha channel and handed to the window
        /// with UpdateLayeredWindow, so its outline, shadow, glow and edges look right over
        /// anything. Called when the look changes — a move is only a window move.
        /// </summary>
        private void Redraw()
        {
            if (!IsHandleCreated) { return; }
            try
            {
                using (Bitmap bmp = RenderMarker())
                {
                    IntPtr screenDc = GetDC(IntPtr.Zero);
                    IntPtr memDc = CreateCompatibleDC(screenDc);
                    IntPtr hBmp = IntPtr.Zero, old = IntPtr.Zero;
                    try
                    {
                        hBmp = bmp.GetHbitmap(Color.FromArgb(0));   // premultiplied, alpha kept
                        old = SelectObject(memDc, hBmp);
                        var size = new NativeSize { Cx = bmp.Width, Cy = bmp.Height };
                        var src = new NativePoint { X = 0, Y = 0 };
                        var dst = new NativePoint { X = Left, Y = Top };
                        var blend = new BlendFunction { BlendOp = 0 /* AC_SRC_OVER */, SourceConstantAlpha = 255, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
                        UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
                    }
                    finally
                    {
                        if (old != IntPtr.Zero) { SelectObject(memDc, old); }
                        if (hBmp != IntPtr.Zero) { DeleteObject(hBmp); }
                        DeleteDC(memDc);
                        ReleaseDC(IntPtr.Zero, screenDc);
                    }
                }
            }
            catch { /* a marker that can't be drawn this once is drawn at the next change */ }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativeSize { public int Cx, Cy; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize,
            IntPtr hdcSrc, ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hDC);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

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
            // Moving onto a monitor with a different DPI, Windows proposes a new size and
            // WinForms applies it — and the hotspot sums below assumed their own size, so the
            // ring, crosshair and dot drew off the point they aim at. The size is ours to
            // set (SetHotspot sizes for the monitor's DPI), so the proposal is declined.
            const int WM_DPICHANGED = 0x02E0;
            if (m.Msg == WM_DPICHANGED)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>The marker's hotspot (the exact pixel it "points at") in screen coords.</summary>
        public void SetHotspot(int screenX, int screenY)
        {
            UpdateDpiFor(screenX, screenY);

            // Arrow points from its top-left; others are centred. Offset the window so
            // the hotspot lands on the target pixel.
            int half = _box / 2;
            int hx = _shape == SecondCursorShape.Arrow ? 6 : half;
            int hy = _shape == SecondCursorShape.Arrow ? 4 : half;
            Location = new Point(screenX - hx, screenY - hy);
        }

        public void SetAppearance(SecondCursorShape shape, Color color, int scalePercent)
        {
            _shape = shape;
            _color = color;
            _scale = Math.Max(50, Math.Min(250, scalePercent));
            ResizeBox();
            Redraw();
        }

        /// <summary>The drawing scale: the chosen size times the monitor's DPI.</summary>
        private float DrawScale => _scale / 100f * (_dpi / 96f);

        /// <summary>The monitor's DPI as a factor of 96 (1.5 at 150 %), for sizing the click target.</summary>
        public float DpiScale => _dpi / 96f;

        /// <summary>
        /// Sizes the window to the marker. It was 64 px whatever the size, so at the larger
        /// sizes the crosshair's arms and the "grabbed" halo were cut off at its edges.
        /// </summary>
        private void ResizeBox()
        {
            int box = Math.Max(MinBox, (int)Math.Ceiling(44 * DrawScale) + 8);
            if (box != _box)
            {
                Point hot = HotspotNow();
                _box = box;
                Size = new Size(box, box);
                int hx = _shape == SecondCursorShape.Arrow ? 6 : box / 2;
                int hy = _shape == SecondCursorShape.Arrow ? 4 : box / 2;
                Location = new Point(hot.X - hx, hot.Y - hy);
            }
        }

        /// <summary>Where the marker points now, from the window's position.</summary>
        private Point HotspotNow()
        {
            int hx = _shape == SecondCursorShape.Arrow ? 6 : _box / 2;
            int hy = _shape == SecondCursorShape.Arrow ? 4 : _box / 2;
            return new Point(Left + hx, Top + hy);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(Point pt, uint flags);

        [System.Runtime.InteropServices.DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        /// <summary>Re-reads the DPI when the marker reaches a different monitor.</summary>
        private void UpdateDpiFor(int x, int y)
        {
            try
            {
                IntPtr mon = MonitorFromPoint(new Point(x, y), 2 /* MONITOR_DEFAULTTONEAREST */);
                if (mon == _dpiMonitor) { return; }
                _dpiMonitor = mon;
                if (mon != IntPtr.Zero && GetDpiForMonitor(mon, 0 /* MDT_EFFECTIVE_DPI */, out uint dx, out _) == 0 && dx > 0)
                {
                    if ((int)dx != _dpi)
                    {
                        _dpi = (int)dx;
                        ResizeBox();
                        Redraw();
                    }
                }
            }
            catch { /* shcore missing (pre-8.1): stay at 96 DPI */ }
        }

        /// <summary>Grabbed = a brighter halo so it's obvious you're moving it.</summary>
        public void SetActiveLook(bool active)
        {
            if (_active != active) { _active = active; Redraw(); }
        }

        /// <summary>
        /// A badge on the marker while it spam-clicks: green while clicks are going out,
        /// amber with pause bars while they are held back. Spam clicks are posted, so nothing
        /// on screen ever showed that they were happening — or that they had stopped because
        /// the window was covered or closed.
        /// </summary>
        public void SetSpamLook(SecondCursorSpamLook look)
        {
            if (_spamLook != look) { _spamLook = look; Redraw(); }
        }

        /// <summary>The badge showing now (for tests and Live debug).</summary>
        public SecondCursorSpamLook SpamLook => _spamLook;

        private void DrawSpamBadge(Graphics g, float s, int cx, int cy)
        {
            if (_spamLook == SecondCursorSpamLook.Off) { return; }
            // Beside the marker, clear of the point it aims at: off the lower right of a
            // centred shape, just past the arrow's tail.
            float bx = _shape == SecondCursorShape.Arrow ? 6 + 15f * s : cx + 11.5f * s;
            float by = _shape == SecondCursorShape.Arrow ? 4 + 17f * s : cy + 11.5f * s;
            float r = Math.Max(4f, 4.5f * s);
            Color fill = _spamLook == SecondCursorSpamLook.Clicking
                ? Color.FromArgb(34, 197, 94)     // green: clicking
                : Color.FromArgb(245, 158, 11);   // amber: held back
            using (var brush = new SolidBrush(fill))
            using (var ring = new Pen(Color.White, 1.5f))
            {
                g.FillEllipse(brush, bx - r, by - r, r * 2, r * 2);
                g.DrawEllipse(ring, bx - r, by - r, r * 2, r * 2);
            }
            if (_spamLook == SecondCursorSpamLook.Paused)
            {
                // Pause bars, so the two states differ in shape and not only in colour. Crisp
                // and at least two pixels wide: anti-aliased at 1.3 px, as they were at the
                // normal size, they smeared into the amber and could hardly be made out.
                float bw = Math.Max(2f, r * 0.3f), bh = Math.Max(4f, r * 1.0f), gap = Math.Max(1.5f, r * 0.34f);
                SmoothingMode smooth = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.None;
                using (var bars = new SolidBrush(Color.FromArgb(40, 40, 40)))
                {
                    float top = (float)Math.Round(by - bh / 2), w = (float)Math.Round(bw), h = (float)Math.Round(bh);
                    g.FillRectangle(bars, (float)Math.Round(bx - gap / 2 - bw), top, w, h);
                    g.FillRectangle(bars, (float)Math.Round(bx + gap / 2), top, w, h);
                }
                g.SmoothingMode = smooth;
            }
        }

        private void DrawMarker(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = DrawScale;
            int cx = _box / 2, cy = _box / 2;

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
                        // A soft dark edge under the coloured stroke, so the ring holds up over
                        // a background of its own colour — the arrow and the dot have one too.
                        float r = 12 * s;
                        using (var edge = new Pen(Color.FromArgb(120, 0, 0, 0), pen.Width + 2f))
                        using (var edgeFill = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                        {
                            g.DrawEllipse(edge, cx - r, cy - r, r * 2, r * 2);
                            g.FillEllipse(edgeFill, cx - 3.2f, cy - 3.2f, 6.4f, 6.4f);
                        }
                        g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                        g.FillEllipse(fill, cx - 2.2f, cy - 2.2f, 4.4f, 4.4f);
                        break;
                    }
                    case SecondCursorShape.Crosshair:
                    {
                        float r = 13 * s, gap = 4 * s;
                        using (var edge = new Pen(Color.FromArgb(120, 0, 0, 0), pen.Width + 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        using (var edgeFill = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                        {
                            g.DrawLine(edge, cx, cy - r, cx, cy - gap);
                            g.DrawLine(edge, cx, cy + gap, cx, cy + r);
                            g.DrawLine(edge, cx - r, cy, cx - gap, cy);
                            g.DrawLine(edge, cx + gap, cy, cx + r, cy);
                            g.FillEllipse(edgeFill, cx - 3f, cy - 3f, 6f, 6f);
                        }
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
            DrawSpamBadge(g, s, cx, cy);
        }
    }
}
