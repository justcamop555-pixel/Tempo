using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// A translucent full-screen overlay that lets the user pick a point on
    /// screen. Left-click confirms and stores the coordinate; Escape or
    /// right-click cancels.
    ///
    /// Drawn with per-pixel alpha (see <see cref="LayeredSurface"/>): only the dim over the
    /// screen is translucent. It used to be the whole form at 35% opacity, so the X/Y read-out
    /// and the instructions were 35% opaque too — hard to read over anything bright, which is
    /// exactly where people pick points.
    ///
    /// Round 40 ("improve for quick capture and better visual"):
    ///  • <see cref="MultiCapture"/>: Multi-Point's Quick Capture stays in ONE overlay for a whole sequence — each click adds
    ///    a point, shown as a numbered marker joined to the one before (the order they will be clicked in), Backspace takes
    ///    the last one back, Enter / Esc / right-click finish. It used to close the overlay after every click and open a new
    ///    full-screen one, with nothing on screen to show the points already taken.
    ///  • The points the list already has show too (dimmed, numbered), so a new one is placed in relation to them.
    ///  • A magnifier beside the pointer (every picker): the pixels under it eight times as large, the one that will be
    ///    picked framed, so a point lands on the exact pixel of a small button.
    /// </summary>
    public sealed class CoordinatePickerForm : Form
    {
        private readonly Theme _theme;
        private Point _cursor;
        private LayeredSurface _surface;
        private bool _renderQueued;

        private readonly Font _readoutFont = new Font("Consolas", 11f, FontStyle.Bold);
        private readonly Font _hintFont = new Font("Segoe UI", 12f, FontStyle.Bold);
        private readonly Font _markerFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);

        /// <summary>The dim over the screen: 35% black, as the whole window used to be.</summary>
        private const int DimAlpha = 90;

        // The magnifier: a square of LoupeSource × LoupeSource screen pixels, each drawn LoupeZoom times as large.
        private const int LoupeSource = 15;
        private const int LoupeZoom = 8;
        private Bitmap _loupeBits;
        // Whether this window is left out of screen captures (so the magnifier reads the screen UNDER it); when Windows
        // cannot do that, the magnifier reads a picture of the screen taken before the overlay came up.
        private bool _excludedFromCapture;
        private Bitmap _snapshot;
        private Rectangle _snapshotBounds;

        private readonly List<Point> _captured = new List<Point>();

        public int PickedX { get; private set; }
        public int PickedY { get; private set; }

        /// <summary>Stay open and collect a point per click until Enter, Esc or a right-click (Quick Capture).</summary>
        public bool MultiCapture { get; set; }

        /// <summary>The points taken in a <see cref="MultiCapture"/> session, in screen coordinates, in click order.</summary>
        public IReadOnlyList<Point> CapturedPoints => _captured;

        /// <summary>Points that already exist (screen coordinates), shown dimmed and numbered from 1. Null: none.</summary>
        public IList<Point> ExistingPoints { get; set; }

        public CoordinatePickerForm(Theme theme)
        {
            AutoScaleMode = AutoScaleMode.None; // positioned in raw screen pixels
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Cursor = Cursors.Cross;

            // Cover the entire virtual desktop (all monitors).
            Bounds = SystemInformation.VirtualScreen;

            // Where the pointer already is, so the first frame has the crosshair under it
            // rather than in the top-left corner until the mouse moves.
            Point p = Control.MousePosition;
            _cursor = new Point(p.X - Bounds.Left, p.Y - Bounds.Top);

            KeyPreview = true;
            KeyDown += OnKeyDown;
            MouseMove += OnMouseMove;
            MouseDown += OnMouseDown;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_LAYERED = 0x00080000;
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Left out of screen captures, so the magnifier's copy of the screen shows what is under the overlay rather
            // than the overlay itself (Windows 10 2004 and later). Otherwise: one picture of the screen, taken now, while
            // the overlay is not yet on it.
            try { _excludedFromCapture = SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE); }
            catch { _excludedFromCapture = false; }
            if (!_excludedFromCapture)
            {
                TakeSnapshot();
            }
            // A layered window shows nothing until it is given its first frame.
            Render();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _surface?.Dispose();
            _surface = null;
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _surface?.Dispose();
                _surface = null;
                _loupeBits?.Dispose();
                _loupeBits = null;
                _snapshot?.Dispose();
                _snapshot = null;
                _readoutFont.Dispose();
                _hintFont.Dispose();
                _markerFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Finish();
                return;
            }
            if (!MultiCapture) { return; }
            if (e.KeyCode == Keys.Enter)
            {
                Finish();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Back || (e.Control && e.KeyCode == Keys.Z))
            {
                if (_captured.Count > 0)
                {
                    Point gone = _captured[_captured.Count - 1];
                    _captured.RemoveAt(_captured.Count - 1);
                    Logger.Info($"[UI] quick capture: took back ({gone.X}, {gone.Y}); {_captured.Count} point(s) now.");
                    Render();
                }
                e.Handled = true;
            }
        }

        /// <summary>
        /// Closes the overlay. A single pick that was not made is a Cancel; a Quick Capture session keeps what it took (Esc
        /// ended the old one-overlay-per-point loop the same way: the points already taken stayed).
        /// </summary>
        private void Finish()
        {
            DialogResult = MultiCapture && _captured.Count > 0 ? DialogResult.OK : DialogResult.Cancel;
            Close();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            _cursor = e.Location;
            QueueRender();
        }

        /// <summary>One frame per batch of moves: a fast mouse reports far more often than a full-screen frame is worth drawing.</summary>
        private void QueueRender()
        {
            if (_renderQueued || !IsHandleCreated) { return; }
            _renderQueued = true;
            BeginInvoke(new Action(() =>
            {
                _renderQueued = false;
                Render();
            }));
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                Finish();
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                // Convert the client point to absolute screen coordinates.
                int x = Bounds.Left + e.X;
                int y = Bounds.Top + e.Y;
                if (MultiCapture)
                {
                    _captured.Add(new Point(x, y));
                    PickedX = x;
                    PickedY = y;
                    Logger.Info($"[UI] quick capture: point {ExistingCount + _captured.Count} at ({x}, {y}).");
                    Render();
                    return;
                }
                PickedX = x;
                PickedY = y;
                Logger.Info($"[UI] coordinate picked: ({PickedX}, {PickedY}).");
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private int ExistingCount => ExistingPoints?.Count ?? 0;

        private void Render()
        {
            if (!IsHandleCreated || IsDisposed) { return; }
            try
            {
                if (_surface == null || _surface.Width != Width || _surface.Height != Height)
                {
                    _surface?.Dispose();
                    _surface = new LayeredSurface(Width, Height);
                }
                using (Graphics g = Graphics.FromImage(_surface.Bitmap))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.Clear(Color.FromArgb(DimAlpha, 0, 0, 0));
                    g.CompositingMode = CompositingMode.SourceOver;
                    // Greyscale: ClearType fringes on a canvas that is not opaque.
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    Draw(g);
                }
                _surface.Push(Handle, Bounds.Location);
            }
            catch (Exception ex)
            {
                Logger.Swallow("CoordinatePicker.Render", ex);
            }
        }

        /// <summary>The monitor under the pointer, in this form's client coordinates.</summary>
        private Rectangle MonitorUnderCursor()
        {
            Rectangle m = Screen.FromPoint(new Point(Bounds.Left + _cursor.X, Bounds.Top + _cursor.Y)).Bounds;
            m.Offset(-Bounds.Left, -Bounds.Top);
            return m;
        }

        private Point ToClient(Point screen) => new Point(screen.X - Bounds.Left, screen.Y - Bounds.Top);

        private void Draw(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawPoints(g);

            // Crosshair lines across the whole overlay.
            g.SmoothingMode = SmoothingMode.None;
            using (var pen = new Pen(Color.FromArgb(200, _theme.Accent), 1f))
            {
                g.DrawLine(pen, 0, _cursor.Y, Width, _cursor.Y);
                g.DrawLine(pen, _cursor.X, 0, _cursor.X, Height);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle monitor = MonitorUnderCursor();
            int absX = Bounds.Left + _cursor.X;
            int absY = Bounds.Top + _cursor.Y;

            // The magnifier, then the coordinate read-out under it — beside the pointer, on the monitor it is on, turned to
            // whichever side has room. (The virtual desktop's edges are not enough: with monitors of different sizes it has
            // corners that are on no screen at all, and a box there is drawn where nobody can see it.)
            int loupeSize = LoupeSource * LoupeZoom;
            string text = MultiCapture
                ? Localization.F("Point {0}", ExistingCount + _captured.Count + 1) + $"   X: {absX}   Y: {absY}"
                : $"X: {absX}   Y: {absY}";
            SizeF readSize = g.MeasureString(text, _readoutFont);
            int boxW = Math.Max(loupeSize, (int)Math.Ceiling(readSize.Width) + 12);
            int boxH = (int)Math.Ceiling(readSize.Height) + 8;
            const int underLoupe = 14;   // room for the colour tag on the magnifier's foot
            int blockH = loupeSize + underLoupe + boxH;
            int left = _cursor.X + 22;
            int top = _cursor.Y + 22;
            if (left + boxW > monitor.Right) { left = _cursor.X - boxW - 22; }
            if (top + blockH > monitor.Bottom) { top = _cursor.Y - blockH - 22; }
            if (left < monitor.Left) { left = monitor.Left; }
            if (top < monitor.Top) { top = monitor.Top; }

            DrawLoupe(g, new Rectangle(left, top, loupeSize, loupeSize), absX, absY);

            int boxY = top + loupeSize + underLoupe;
            using (var back = new SolidBrush(Color.FromArgb(235, 20, 20, 30)))
            using (var fore = new SolidBrush(Color.White))
            {
                using (var path = RoundedRect(new RectangleF(left, boxY, boxW, boxH), 6f))
                {
                    g.FillPath(back, path);
                }
                g.DrawString(text, _readoutFont, fore, left + 6, boxY + 4);
            }

            // What to do, on the monitor being looked at. It was centred on the whole virtual
            // desktop, which on two monitors put it across the gap between them, and it was
            // English in every language. Wrapped when it is wider than the monitor.
            string msg = MultiCapture
                ? Localization.F("Click to add point {0}  •  Backspace undoes the last  •  Enter, Esc or right-click when done",
                                 ExistingCount + _captured.Count + 1)
                : Localization.T("Click to capture position  •  Esc or right-click to cancel");
            using (var back = new SolidBrush(Color.FromArgb(225, 20, 20, 30)))
            using (var brush = new SolidBrush(Color.White))
            {
                int maxW = Math.Max(120, monitor.Width - 68);
                SizeF size = g.MeasureString(msg, _hintFont, maxW);
                float w = Math.Min(size.Width, maxW);
                float x = monitor.Left + (monitor.Width - w) / 2f;
                float y = monitor.Top + 24;
                using (var path = RoundedRect(new RectangleF(x - 14, y - 6, w + 28, size.Height + 12), 8f))
                {
                    g.FillPath(back, path);
                }
                using (var accent = new Pen(Color.FromArgb(220, _theme.Accent), 2f))
                {
                    g.DrawLine(accent, x - 6, y + size.Height + 5, x + w + 6, y + size.Height + 5);
                }
                g.DrawString(msg, _hintFont, brush, new RectangleF(x, y, w + 1, size.Height + 2));
            }
        }

        /// <summary>
        /// The points already in the list (dimmed) and the ones taken in this session (in the accent), each a numbered
        /// marker, joined in click order; in a Quick Capture session a dashed line runs from the last one to the pointer —
        /// where the next will go.
        /// </summary>
        private void DrawPoints(Graphics g)
        {
            var all = new List<Point>();
            if (ExistingPoints != null)
            {
                foreach (Point p in ExistingPoints) { all.Add(ToClient(p)); }
            }
            int existing = all.Count;
            foreach (Point p in _captured) { all.Add(ToClient(p)); }
            if (all.Count == 0 && !MultiCapture) { return; }

            Color accent = _theme.Accent;
            Color old = Color.FromArgb(150, 200, 200, 210);

            // The order: a line from each point to the next.
            for (int i = 1; i < all.Count; i++)
            {
                bool isNew = i >= existing;
                using (var pen = new Pen(isNew ? Color.FromArgb(210, accent) : Color.FromArgb(110, old), isNew ? 2f : 1.5f))
                {
                    g.DrawLine(pen, all[i - 1], all[i]);
                }
            }
            if (MultiCapture && all.Count > 0)
            {
                using (var pen = new Pen(Color.FromArgb(150, accent), 1.5f) { DashStyle = DashStyle.Dash })
                {
                    g.DrawLine(pen, all[all.Count - 1], _cursor);
                }
            }

            for (int i = 0; i < all.Count; i++)
            {
                bool isNew = i >= existing;
                float r = isNew ? 12f : 10f;
                Point c = all[i];
                Color fill = isNew ? accent : Color.FromArgb(170, 60, 60, 72);
                using (var halo = new SolidBrush(Color.FromArgb(isNew ? 200 : 140, 0, 0, 0)))
                {
                    g.FillEllipse(halo, c.X - r - 2, c.Y - r - 2, (r + 2) * 2, (r + 2) * 2);
                }
                using (var b = new SolidBrush(fill))
                {
                    g.FillEllipse(b, c.X - r, c.Y - r, r * 2, r * 2);
                }
                // The exact spot: a pixel-sized dot in the middle would hide under the number, so a small tick ring.
                using (var ring = new Pen(isNew ? Theme.ReadableOn(accent) : Color.FromArgb(200, 230, 230, 235), 1.2f))
                {
                    g.DrawEllipse(ring, c.X - r + 2.5f, c.Y - r + 2.5f, (r - 2.5f) * 2, (r - 2.5f) * 2);
                }
                string n = (i + 1).ToString();
                using (var ink = new SolidBrush(isNew ? Theme.ReadableOn(accent) : Color.FromArgb(235, 235, 235, 240)))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(n, _markerFont, ink, new RectangleF(c.X - r, c.Y - r + 0.5f, r * 2, r * 2), sf);
                }
            }
        }

        /// <summary>
        /// The magnifier: the screen around (<paramref name="absX"/>, <paramref name="absY"/>) drawn eight times as large in
        /// a round frame, the pixel that would be picked outlined in the middle, its colour written under the frame.
        /// </summary>
        private void DrawLoupe(Graphics g, Rectangle box, int absX, int absY)
        {
            int half = LoupeSource / 2;
            Color center = Color.Empty;
            try
            {
                if (_loupeBits == null) { _loupeBits = new Bitmap(LoupeSource, LoupeSource); }
                using (Graphics lg = Graphics.FromImage(_loupeBits))
                {
                    lg.Clear(Color.Black);
                    if (_excludedFromCapture)
                    {
                        lg.CopyFromScreen(absX - half, absY - half, 0, 0, new Size(LoupeSource, LoupeSource), CopyPixelOperation.SourceCopy);
                    }
                    else if (_snapshot != null)
                    {
                        lg.DrawImage(_snapshot, new Rectangle(0, 0, LoupeSource, LoupeSource),
                            new Rectangle(absX - half - _snapshotBounds.Left, absY - half - _snapshotBounds.Top, LoupeSource, LoupeSource),
                            GraphicsUnit.Pixel);
                    }
                }
                center = _loupeBits.GetPixel(half, half);
            }
            catch (Exception ex)
            {
                // A screen that cannot be read (a secure desktop, a protected video) leaves the magnifier black.
                Logger.Swallow("CoordinatePicker.Loupe", ex);
            }

            using (var clip = new GraphicsPath())
            {
                clip.AddEllipse(box);
                using (var shadow = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                {
                    g.FillEllipse(shadow, Rectangle.Inflate(box, 4, 4));
                }
                GraphicsState state = g.Save();
                try
                {
                    g.SetClip(clip);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.SmoothingMode = SmoothingMode.None;
                    if (_loupeBits != null)
                    {
                        g.DrawImage(_loupeBits, box);
                    }
                    // The pixel that will be picked.
                    using (var pen = new Pen(Color.FromArgb(235, 255, 255, 255), 1f))
                    using (var dark = new Pen(Color.FromArgb(200, 0, 0, 0), 1f))
                    {
                        var cell = new Rectangle(box.Left + half * LoupeZoom, box.Top + half * LoupeZoom, LoupeZoom, LoupeZoom);
                        g.DrawRectangle(dark, Rectangle.Inflate(cell, 1, 1));
                        g.DrawRectangle(pen, cell);
                    }
                }
                finally
                {
                    g.Restore(state);
                }
                using (var ring = new Pen(_theme.Accent, 2.5f))
                {
                    g.DrawEllipse(ring, box);
                }
            }

            if (!center.IsEmpty)
            {
                // A swatch of the pixel's colour and its value, on the frame's foot.
                string hex = $"#{center.R:X2}{center.G:X2}{center.B:X2}";
                SizeF hs = g.MeasureString(hex, _markerFont);
                float w = hs.Width + 22, h = hs.Height + 4;
                var tag = new RectangleF(box.Left + (box.Width - w) / 2f, box.Bottom - h / 2f, w, h);
                using (var path = RoundedRect(tag, h / 2f))
                using (var back = new SolidBrush(Color.FromArgb(235, 20, 20, 30)))
                {
                    g.FillPath(back, path);
                }
                using (var sw = new SolidBrush(Color.FromArgb(255, center)))
                using (var edge = new Pen(Color.FromArgb(200, 255, 255, 255), 1f))
                {
                    var s = new RectangleF(tag.Left + 6, tag.Top + (h - 9) / 2f, 9, 9);
                    g.FillRectangle(sw, s);
                    g.DrawRectangle(edge, s.X, s.Y, s.Width, s.Height);
                }
                using (var ink = new SolidBrush(Color.White))
                {
                    g.DrawString(hex, _markerFont, ink, tag.Left + 18, tag.Top + 2);
                }
            }
        }

        /// <summary>The whole virtual desktop as it is before the overlay is shown, for a magnifier that cannot see under it.</summary>
        private void TakeSnapshot()
        {
            try
            {
                Rectangle vs = SystemInformation.VirtualScreen;
                _snapshot = new Bitmap(vs.Width, vs.Height);
                using (Graphics sg = Graphics.FromImage(_snapshot))
                {
                    sg.CopyFromScreen(vs.Left, vs.Top, 0, 0, vs.Size, CopyPixelOperation.SourceCopy);
                }
                _snapshotBounds = vs;
            }
            catch (Exception ex)
            {
                _snapshot?.Dispose();
                _snapshot = null;
                Logger.Swallow("CoordinatePicker.TakeSnapshot", ex);
            }
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { path.AddRectangle(r); return path; }
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Finish();
                return true;
            }
            if (MultiCapture && keyData == Keys.Enter)
            {
                Finish();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
    }
}
